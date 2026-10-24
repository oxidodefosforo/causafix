using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CausaFix.ProjectSetup.Geometry;

namespace CausaFix.Revit.ProjectSetup
{
    /// <summary>
    /// Model → sheet mapping of a placed plan view, from the documented API:
    ///   View.GetModelToProjectionTransforms()  (model → view projection; distances in projection = paper distances)
    ///   Viewport.GetProjectionToSheetTransform() (projection → sheet; includes viewport position and rotation)
    /// sheet = P2S · M2P · model. The scale is contained in M2P only, so it is never applied twice.
    /// </summary>
    public static class SheetGeometry
    {
        public sealed class Mapping
        {
            public Transform ModelToSheet;
            public string Problem;
            public bool Ok => Problem == null;
        }

        public static Mapping ModelToSheet(View view, Viewport vp)
        {
            IList<TransformWithBoundary> trs;
            try { trs = view.GetModelToProjectionTransforms(); }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { return new Mapping { Problem = "The view does not provide model-to-projection transforms: " + ex.Message }; }
            if (trs == null || trs.Count == 0) return new Mapping { Problem = "The view returned no model-to-projection transform." };
            if (trs.Count > 1) return new Mapping { Problem = "Split crop regions (" + trs.Count + " regions) are not supported for alignment in this version: each region moves independently. Remove the split or align manually." };
            Transform m2p = trs[0].GetModelToProjectionTransform();
            Transform p2s = vp.GetProjectionToSheetTransform();
            return new Mapping { ModelToSheet = p2s.Multiply(m2p) };
        }

        /// <summary>XY affine part of a Revit transform evaluated on the plane z = <paramref name="z"/>.</summary>
        public static Affine2 ToAffine(Transform t, double z)
        {
            XYZ o = t.OfPoint(new XYZ(0, 0, z));
            XYZ x = t.OfPoint(new XYZ(1, 0, z)) - o;
            XYZ y = t.OfPoint(new XYZ(0, 1, z)) - o;
            return new Affine2(x.X, y.X, x.Y, y.Y, o.X, o.Y);
        }

        public static Affine2 ToAffine(Transform t) => new Affine2(t.BasisX.X, t.BasisY.X, t.BasisX.Y, t.BasisY.Y, t.Origin.X, t.Origin.Y);

        /// <summary>Elevation used to project the XY anchor onto a plan view (its associated level).</summary>
        public static double PlaneZ(View view) => view is ViewPlan vp && vp.GenLevel != null ? vp.GenLevel.Elevation : 0;

        public static bool HasNonRectangularCrop(View view)
        {
            try { return view.CropBoxActive && view.GetCropRegionShapeManager().ShapeSet; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>IViewportProbe over a real viewport: measuring regenerates first; translating uses SetBoxCenter.</summary>
    internal sealed class RevitViewportProbe : IViewportProbe
    {
        private readonly Document _doc;
        private readonly View _view;
        private readonly Viewport _vp;
        private readonly double _z;

        public RevitViewportProbe(Document doc, View view, Viewport vp)
        {
            _doc = doc; _view = view; _vp = vp;
            _z = SheetGeometry.PlaneZ(view);
        }

        public string Problem { get; private set; }

        public Affine2 MeasureModelToSheet()
        {
            _doc.Regenerate();
            var m = SheetGeometry.ModelToSheet(_view, _vp);
            if (!m.Ok) { Problem = m.Problem; throw new InvalidOperationException(m.Problem); }
            return SheetGeometry.ToAffine(m.ModelToSheet, _z);
        }

        public P2 BoxCenter()
        {
            _doc.Regenerate();
            XYZ c = _vp.GetBoxCenter();
            return new P2(c.X, c.Y);
        }

        public void Translate(P2 delta)
        {
            XYZ c = _vp.GetBoxCenter();
            _vp.SetBoxCenter(new XYZ(c.X + delta.X, c.Y + delta.Y, c.Z));
            _doc.Regenerate();
        }
    }
}
