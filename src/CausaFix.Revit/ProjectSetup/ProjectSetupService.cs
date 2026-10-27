using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Geometry;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
using CausaFix.ProjectSetup.Planning;

namespace CausaFix.Revit.ProjectSetup
{
    /// <summary>Project Setup gateway: every Revit access through the ExternalEvent dispatcher, DTOs only.</summary>
    public sealed class ProjectSetupService : IProjectSetupGateway, IDisposable
    {
        private readonly RevitDispatcher _dispatcher;
        private readonly RevitSession _session = new RevitSession();
        private string _docKey = string.Empty;

        public ProjectSetupService(RevitDispatcher dispatcher) { _dispatcher = dispatcher; }

        public string DocumentKey => _docKey;
        public string AddinVersion => typeof(ProjectSetupService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        private Task<T> Run<T>(Func<UIApplication, Document, T> f) => _dispatcher.InvokeAsync(app => f(app, _session.Require(app)));

        public static string KeyOf(Document doc)
        {
            string g = RevitCompat.CreationGuid(doc);
            return (string.IsNullOrEmpty(g) ? "" : g + "|") + (doc.ProjectInformation?.UniqueId ?? "");
        }

        public Task<string> BindActiveDocument() => _dispatcher.InvokeAsync(app =>
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) throw new DocumentChangedException("No document is open in Revit.");
            if (doc.IsFamilyDocument) throw new DocumentChangedException("Project Setup works on project documents, not families.");
            _session.Bind(doc);
            _docKey = KeyOf(doc);
            return doc.Title;
        });

        public Task<SetupCatalog> ReadCatalog(IReadOnlyCollection<ParamRef> wantedValues) => Run((app, doc) => SetupCatalogReader.Read(doc, wantedValues));

        public Task<ParamValueSet> ReadParameterValues(IReadOnlyCollection<ParamRef> wanted) => Run((app, doc) => SetupCatalogReader.ReadValues(doc, wanted));

        public Task<TitlePick> PickReferenceTitle(TitleAnchor anchor) => Run((app, doc) =>
        {
            var uidoc = app.ActiveUIDocument;
            if (!(uidoc.ActiveView is ViewSheet sheet))
                return new TitlePick { Message = "The active view in Revit is not a sheet. Open the sheet that holds the reference viewport (its title correctly placed) and try again." };
            Viewport vp;
            try
            {
                var r = uidoc.Selection.PickObject(ObjectType.Element, new ViewportFilter(), "CausaFixIt: pick the viewport whose title is correctly placed (Esc cancels)");
                vp = doc.GetElement(r.ElementId) as Viewport;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return new TitlePick { Cancelled = true, Message = "Pick cancelled: nothing changed." }; }
            if (vp == null) return new TitlePick { Message = "The picked element is not a viewport placed on this sheet. Pick the viewport (the drawing on the sheet)." };
            var view = doc.GetElement(vp.ViewId) as View;
            // Read-only: no transaction is open here, so nothing may regenerate or modify the document.
            var label = ViewportTitleReader.Label(vp);
            if (label == null)
                return new TitlePick { Message = "The viewport of '" + view?.Name + "' shows no title (its viewport type hides it). Pick a viewport whose title is visible." };
            var box = ViewportTitleReader.Box(vp);
            int q = vp.Rotation == ViewportRotation.Clockwise ? -1 : vp.Rotation == ViewportRotation.Counterclockwise ? 1 : 0;
            var c = box.Center;
            // Relative: anchor corner of the title from the same corner of the viewport box, in the unrotated viewport frame.
            var rel = TitleGeometry.CaptureRelative(TitleGeometry.ToLocal(box, c, q), TitleGeometry.ToLocal(label.Value, c, q), anchor);
            // Fixed: anchor corner of the measured title outline on the sheet (mm of paper; sheet space has no view scale).
            var corner = TitleGeometry.Corner(label.Value, anchor);
            var pick = new TitlePick
            {
                Ok = true,
                RelativeXmm = rel.X, RelativeYmm = rel.Y,
                SheetXmm = corner.X, SheetYmm = corner.Y,
                LineLengthMm = ViewportTitleReader.LineLength(vp),
                SheetNumber = sheet.SheetNumber,
                ViewName = view?.Name ?? "",
                Description = "title of '" + view?.Name + "' on sheet " + sheet.SheetNumber + (string.IsNullOrEmpty(sheet.Name) ? "" : " - " + sheet.Name)
            };
            var tbs = SetupCatalogReader.TitleBlocksOn(doc, sheet);
            pick.TitleBlockCount = tbs.Count;
            if (tbs.Count == 1)
            {
                var local = SheetGeometry.ToAffine(tbs[0].GetTransform()).Inverse().Apply(new P2(AlignmentMath.MmToFeet(corner.X), AlignmentMath.MmToFeet(corner.Y)));
                pick.TitleBlockXmm = AlignmentMath.FeetToMm(local.X);
                pick.TitleBlockYmm = AlignmentMath.FeetToMm(local.Y);
            }
            return pick;
        });

        private sealed class GridFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Grid g && !g.IsCurved;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        private sealed class ViewportFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Viewport;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        public Task<AnchorPick> PickGridIntersection(bool second) => Run((app, doc) =>
        {
            var uidoc = app.ActiveUIDocument;
            try
            {
                var r1 = uidoc.Selection.PickObject(ObjectType.Element, new GridFilter(), "CausaFixIt: pick the FIRST straight grid of the " + (second ? "second " : "") + "anchor");
                var r2 = uidoc.Selection.PickObject(ObjectType.Element, new GridFilter(), "CausaFixIt: pick the SECOND straight grid");
                var g1 = doc.GetElement(r1.ElementId) as Grid;
                var g2 = doc.GetElement(r2.ElementId) as Grid;
                if (g1 == null || g2 == null || g1.Id == g2.Id) return new AnchorPick { Message = "Pick two different straight grids." };
                XYZ p = Intersect2D(g1.Curve, g2.Curve);
                if (p == null) return new AnchorPick { Message = "Grids '" + g1.Name + "' and '" + g2.Name + "' are parallel: no unique intersection." };
                return new AnchorPick
                {
                    Ok = true,
                    Message = "Intersection of grids " + g1.Name + " / " + g2.Name,
                    Anchor = new ModelAnchor { IsSet = true, X = p.X, Y = p.Y, Description = "Grid " + g1.Name + " / " + g2.Name + " (internal coordinates)", DocumentKey = _docKey }
                };
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return new AnchorPick { Message = "Pick cancelled." }; }
        });

        /// <summary>Intersection of the infinite XY lines through two straight grid curves (grids may not touch in 3D).</summary>
        private static XYZ Intersect2D(Curve a, Curve b)
        {
            XYZ p1 = a.GetEndPoint(0), p2 = a.GetEndPoint(1), q1 = b.GetEndPoint(0), q2 = b.GetEndPoint(1);
            double d1x = p2.X - p1.X, d1y = p2.Y - p1.Y, d2x = q2.X - q1.X, d2y = q2.Y - q1.Y;
            double den = d1x * d2y - d1y * d2x;
            double len = Math.Sqrt(d1x * d1x + d1y * d1y) * Math.Sqrt(d2x * d2x + d2y * d2y);
            if (len < 1e-12 || Math.Abs(den) / len < 1e-9) return null;
            double t = ((q1.X - p1.X) * d2y - (q1.Y - p1.Y) * d2x) / den;
            return new XYZ(p1.X + t * d1x, p1.Y + t * d1y, 0);
        }

        public Task<AnchorPick> PickPoint(bool second) => Run((app, doc) =>
        {
            var uidoc = app.ActiveUIDocument;
            var view = uidoc.ActiveView;
            if (!(view is ViewPlan)) return new AnchorPick { Message = "Open a host plan view to pick a point (or use a grid intersection)." };
            if (view.SketchPlane == null) return new AnchorPick { Message = "The active plan has no work plane, so Revit cannot pick a point. Use a grid intersection, or set a work plane in the view first." };
            try
            {
                XYZ p = uidoc.Selection.PickPoint("CausaFixIt: pick the " + (second ? "second " : "") + "model anchor point");
                return new AnchorPick
                {
                    Ok = true,
                    Message = "Point picked in '" + view.Name + "'",
                    Anchor = new ModelAnchor { IsSet = true, X = p.X, Y = p.Y, Description = "Point picked in " + view.Name + " (internal coordinates)", DocumentKey = _docKey }
                };
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return new AnchorPick { Message = "Pick cancelled." }; }
        });

        public Task<ReferencePick> PickReferenceViewport(ModelAnchor anchor, AlignmentMethod method) => Run((app, doc) =>
        {
            var uidoc = app.ActiveUIDocument;
            if (!(uidoc.ActiveView is ViewSheet sheet)) return new ReferencePick { Message = "Open the reference sheet first (the active view must be a sheet)." };
            Viewport vp;
            try
            {
                var r = uidoc.Selection.PickObject(ObjectType.Element, new ViewportFilter(), "CausaFixIt: pick the correctly placed reference viewport");
                vp = doc.GetElement(r.ElementId) as Viewport;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return new ReferencePick { Message = "Pick cancelled." }; }
            if (vp == null) return new ReferencePick { Message = "That is not a viewport." };
            var view = doc.GetElement(vp.ViewId) as View;
            P2 onSheet;
            if (method == AlignmentMethod.ViewportCenter)
            {
                XYZ c = vp.GetBoxCenter();
                onSheet = new P2(c.X, c.Y);
            }
            else
            {
                if (anchor == null || !anchor.IsSet) return new ReferencePick { Message = "Pick the model anchor first." };
                var map = SheetGeometry.ModelToSheet(view, vp);
                if (!map.Ok) return new ReferencePick { Message = map.Problem };
                onSheet = SheetGeometry.ToAffine(map.ModelToSheet, SheetGeometry.PlaneZ(view)).Apply(new P2(anchor.X, anchor.Y));
            }
            var res = new ReferencePick
            {
                Ok = true,
                SheetXmm = AlignmentMath.FeetToMm(onSheet.X),
                SheetYmm = AlignmentMath.FeetToMm(onSheet.Y),
                Description = "viewport of '" + view?.Name + "' on sheet " + sheet.SheetNumber
            };
            var tbs = SetupCatalogReader.TitleBlocksOn(doc, sheet);
            if (tbs.Count == 1)
            {
                var local = SheetGeometry.ToAffine(tbs[0].GetTransform()).Inverse().Apply(onSheet);
                res.TitleBlockXmm = AlignmentMath.FeetToMm(local.X);
                res.TitleBlockYmm = AlignmentMath.FeetToMm(local.Y);
            }
            return res;
        });

        public Task<SetupResult> Run(SetupConfig config, SetupPlan plan, IProgress<string> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // One API callback: the atomic transaction group never spans callbacks. Cancellation is honoured before it starts.
            return Run((app, doc) =>
            {
                if (KeyOf(doc) != _docKey) throw new DocumentChangedException("The active document changed since the plan was built. Refresh Project Setup.");
                return new SetupExecutor(doc, config, AddinVersion).Run(plan, progress);
            });
        }

        public Task<string> ShowElement(string uniqueId) => Run((app, doc) =>
        {
            var e = doc.GetElement(uniqueId);
            if (e == null) return "Element not found.";
            var uidoc = app.ActiveUIDocument;
            if (e is View v && !v.IsTemplate) { uidoc.RequestViewChange(v); return "Opening '" + v.Name + "'."; }
            uidoc.ShowElements(e.Id);
            return "Showing " + e.Name + ".";
        });

        public void Dispose() => _dispatcher.Dispose();
    }
}
