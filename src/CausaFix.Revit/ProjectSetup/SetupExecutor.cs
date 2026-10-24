using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Geometry;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
using CausaFix.ProjectSetup.Planning;

namespace CausaFix.Revit.ProjectSetup
{
    /// <summary>
    /// Executes an approved plan atomically inside one TransactionGroup (one Undo):
    ///   1 temporary names (frees names/numbers for swaps) → 2 views → 3 sheets → 4 viewports → 5 alignment + verification.
    /// Any failure of an included row, or an alignment outside tolerance, rolls everything back, so no orphan
    /// views/sheets are left. The plan is re-validated against the live document. Never saves or synchronises.
    /// </summary>
    public sealed class SetupExecutor
    {
        private readonly Document _doc;
        private readonly SetupConfig _cfg;
        private readonly string _version;
        private readonly Dictionary<string, RowResult> _results = new Dictionary<string, RowResult>();
        private readonly Dictionary<string, View> _views = new Dictionary<string, View>();
        private readonly Dictionary<string, ViewSheet> _sheets = new Dictionary<string, ViewSheet>();
        private readonly Dictionary<string, Viewport> _viewports = new Dictionary<string, Viewport>();
        private readonly List<string> _failures = new List<string>();

        public SetupExecutor(Document doc, SetupConfig cfg, string addinVersion)
        {
            _doc = doc; _cfg = cfg; _version = addinVersion;
        }

        private sealed class Failures : IFailuresPreprocessor
        {
            public readonly List<string> Messages = new List<string>();
            public bool HadErrors;
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (FailureMessageAccessor m in a.GetFailureMessages())
                {
                    Messages.Add(m.GetSeverity() + ": " + m.GetDescriptionText());
                    if (m.GetSeverity() != FailureSeverity.Warning) HadErrors = true;
                }
                return HadErrors ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        public SetupResult Run(SetupPlan plan, IProgress<string> progress)
        {
            var result = new SetupResult();
            if (_doc.IsReadOnly) throw new InvalidOperationException("The document is read-only.");
            if (_doc.IsModifiable) throw new InvalidOperationException("Another transaction is open in Revit; finish it first.");
            var rows = plan.Included.ToList();
            foreach (var r in rows) _results[r.RowKey] = new RowResult { RowKey = r.RowKey, ViewName = r.ViewName, SheetNumber = r.SheetNumber };
            result.Rows.AddRange(_results.Values);
            if (rows.Count == 0) { result.Summary = "Nothing to run."; return result; }

            var failures = new Failures();
            using (var group = new TransactionGroup(_doc, "CausaFix Project Setup (" + rows.Count + " rows)"))
            {
                group.Start();
                bool ok = Step("CausaFix Setup: free names", failures, () => FreeNames(rows))
                       && Step("CausaFix Setup: views", failures, () => { progress?.Report("Creating/updating views"); DoViews(rows); })
                       && Step("CausaFix Setup: sheets", failures, () => { progress?.Report("Creating/updating sheets"); DoSheets(rows); })
                       && Step("CausaFix Setup: viewports", failures, () => { progress?.Report("Placing viewports"); DoViewports(rows); })
                       && Step("CausaFix Setup: alignment", failures, () => { progress?.Report("Aligning and verifying"); DoAlignment(rows); });

                result.RevitMessages.AddRange(failures.Messages);
                if (ok && _failures.Count == 0)
                {
                    group.Assimilate();
                    result.Committed = true;
                    result.Summary = "Setup applied as one Undo step. The model was not saved.";
                }
                else
                {
                    group.RollBack();
                    result.MarkRolledBack("Rolled back: " + (_failures.Count > 0 ? _failures[0] : "Revit reported an error."));
                    result.Summary = "Nothing was kept. " + string.Join(" ", _failures.Take(3));
                }
            }
            return result;
        }

        /// <summary>Runs one transaction; a thrown exception or a Revit error marks the run as failed.</summary>
        private bool Step(string name, Failures failures, Action body)
        {
            if (_failures.Count > 0) return false;
            using (var t = new Transaction(_doc, name))
            {
                var o = t.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(failures);
                o.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(o);
                t.Start();
                try { body(); }
                catch (Exception ex)
                {
                    _failures.Add(name + ": " + ex.Message);
                    t.RollBack();
                    return false;
                }
                if (_failures.Count > 0) { t.RollBack(); return false; }
                var status = t.Commit();
                if (status != TransactionStatus.Committed || failures.HadErrors)
                {
                    _failures.Add(name + ": Revit rolled back the transaction (" + status + ").");
                    return false;
                }
                return true;
            }
        }

        private void Fail(PlanRow r, string message)
        {
            var rr = _results[r.RowKey];
            rr.Outcomes.Add(RowOutcome.Failed);
            rr.Message = (rr.Message + " " + message).Trim();
            _failures.Add((string.IsNullOrEmpty(r.ViewName) ? r.SheetNumber : r.ViewName) + ": " + message);
        }

        private void Note(PlanRow r, RowOutcome o, string msg = null)
        {
            var rr = _results[r.RowKey];
            if (!rr.Outcomes.Contains(o)) rr.Outcomes.Add(o);
            if (!string.IsNullOrEmpty(msg)) rr.Message = (rr.Message + " " + msg).Trim();
        }

        private T Get<T>(string uid) where T : Element => string.IsNullOrEmpty(uid) ? null : _doc.GetElement(uid) as T;

        private string Editable(Element e)
        {
            if (!_doc.IsWorkshared) return null;
            try
            {
                if (WorksharingUtils.GetCheckoutStatus(_doc, e.Id) == CheckoutStatus.OwnedByOtherUser)
                    return "'" + e.Name + "' is owned by " + (WorksharingUtils.GetWorksharingTooltipInfo(_doc, e.Id)?.Owner ?? "another user") + ".";
                if (WorksharingUtils.GetModelUpdatesStatus(_doc, e.Id) == ModelUpdatesStatus.UpdatedInCentral)
                    return "'" + e.Name + "' was updated in central: Reload Latest first.";
            }
            catch (Exception) { }
            return null;
        }

        // ------------------------------------------------------------------ 1. names

        /// <summary>Gives a temporary unique name/number to existing elements that change name, so swaps never collide.</summary>
        private void FreeNames(List<PlanRow> rows)
        {
            string tmp() => "~cf~" + Guid.NewGuid().ToString("N").Substring(0, 10);
            foreach (var r in rows)
            {
                if (r.Updates.Contains("view:name") && (r.ViewAction == ViewAction.Update))
                {
                    var v = Get<View>(r.ExistingViewUniqueId);
                    if (v == null) { Fail(r, "The view to rename no longer exists."); return; }
                    string lockReason = Editable(v);
                    if (lockReason != null) { Fail(r, lockReason); return; }
                    v.Name = tmp();
                }
                if (r.Updates.Contains("sheet:number") && r.SheetAction == SheetAction.Update)
                {
                    var s = Get<ViewSheet>(r.ExistingSheetUniqueId);
                    if (s == null) { Fail(r, "The sheet to renumber no longer exists."); return; }
                    string lockReason = Editable(s);
                    if (lockReason != null) { Fail(r, lockReason); return; }
                    s.SheetNumber = tmp();
                }
            }
        }

        // ------------------------------------------------------------------ 2. views

        private void DoViews(List<PlanRow> rows)
        {
            foreach (var r in rows)
            {
                if (r.ViewAction == ViewAction.None || r.ViewAction == ViewAction.Skip) continue;
                View view;
                switch (r.ViewAction)
                {
                    case ViewAction.Create:
                        {
                            var level = Get<Level>(r.LevelUniqueId);
                            var vft = Get<ViewFamilyType>(r.ViewFamilyTypeUniqueId);
                            if (level == null) { Fail(r, "Level no longer exists."); return; }
                            if (vft == null) { Fail(r, "View type no longer exists."); return; }
                            if (SetupCatalogReader.FamilyOf(vft.ViewFamily) != r.Family) { Fail(r, "View type '" + vft.Name + "' is not a " + r.Family + "."); return; }
                            view = ViewPlan.Create(_doc, vft.Id, level.Id);   // bound to the requested level
                            Note(r, RowOutcome.Created, "View created.");
                            break;
                        }
                    case ViewAction.CreateDependent:
                        {
                            var src = Get<View>(r.ExistingViewUniqueId);
                            if (src == null) { Fail(r, "Source view no longer exists."); return; }
                            if (!src.CanViewBeDuplicated(ViewDuplicateOption.AsDependent)) { Fail(r, "Revit cannot create a dependent copy of '" + src.Name + "'."); return; }
                            view = _doc.GetElement(src.Duplicate(ViewDuplicateOption.AsDependent)) as View;
                            Note(r, RowOutcome.Created, "Dependent view created.");
                            break;
                        }
                    default:
                        {
                            view = Get<View>(r.ExistingViewUniqueId);
                            if (view == null) { Fail(r, "The view to reuse no longer exists."); return; }
                            string lockReason = Editable(view);
                            if (lockReason != null && r.ViewAction == ViewAction.Update) { Fail(r, lockReason); return; }
                            Note(r, r.ViewAction == ViewAction.Update ? RowOutcome.Updated : RowOutcome.Reused);
                            break;
                        }
                }
                if (view == null) { Fail(r, "Revit did not create the view."); return; }
                _views[r.RowKey] = view;
                _results[r.RowKey].ViewUniqueId = view.UniqueId;
                if (!ConfigureView(r, view)) return;
                if (r.ViewAction != ViewAction.Reuse) SetupStorage.Write(view, r.RowKey, SetupStorage.KindView, _version);
                else if (SetupStorage.ReadRowKey(view) != r.RowKey) SetupStorage.Write(view, r.RowKey, SetupStorage.KindView, _version);
            }
        }

        private bool ConfigureView(PlanRow r, View view)
        {
            if (r.ViewAction == ViewAction.Reuse) return true; // nothing changes on a reused, up-to-date view
            if (!string.IsNullOrEmpty(r.ViewName) && view.Name != r.ViewName)
            {
                try { view.Name = r.ViewName; }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex) { Fail(r, "Revit refused the view name '" + r.ViewName + "': " + ex.Message); return false; }
            }
            if (!string.IsNullOrEmpty(r.TitleOnSheet))
            {
                var p = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                if (p != null && !p.IsReadOnly) p.Set(r.TitleOnSheet);
            }
            if (!string.IsNullOrEmpty(r.TemplateUniqueId))
            {
                var tpl = Get<View>(r.TemplateUniqueId);
                if (tpl == null || !tpl.IsTemplate) { Fail(r, "View template no longer exists."); return false; }
                if (!view.IsValidViewTemplate(tpl.Id)) { Fail(r, "Template '" + tpl.Name + "' is not valid for view '" + view.Name + "' (Revit IsValidViewTemplate = false)."); return false; }
                if (view.ViewTemplateId != tpl.Id) view.ViewTemplateId = tpl.Id;
            }
            if (r.ViewAction == ViewAction.Create || r.ViewAction == ViewAction.CreateDependent)
            {
                // Scale only where the template does not control it (the template is never modified).
                var sp = view.get_Parameter(BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC) ?? view.get_Parameter(BuiltInParameter.VIEW_SCALE);
                if (r.RequestedScale > 0 && sp != null && !sp.IsReadOnly && view.Scale != r.RequestedScale)
                {
                    try { view.Scale = r.RequestedScale; } catch (Exception) { /* controlled by template: reported in Review */ }
                }
                if (r.Crop == CropOption.ScopeBox && !string.IsNullOrEmpty(r.ScopeBoxUniqueId))
                {
                    var sb = _doc.GetElement(r.ScopeBoxUniqueId);
                    var p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (sb == null) { Fail(r, "Scope box no longer exists."); return false; }
                    if (p == null || p.IsReadOnly) _results[r.RowKey].Message += " Scope box not applied (controlled by the template).";
                    else p.Set(sb.Id);
                }
                else if (r.Crop == CropOption.NoCrop && view.CropBoxActive)
                {
                    try { view.CropBoxActive = false; } catch (Exception) { }
                }
            }
            _results[r.RowKey].Message += " Effective scale 1:" + SetupCatalogReader.SafeScale(view) + ".";
            return true;
        }

        // ------------------------------------------------------------------ 3. sheets

        private void DoSheets(List<PlanRow> rows)
        {
            foreach (var r in rows)
            {
                if (r.SheetAction == SheetAction.None || r.SheetAction == SheetAction.Skip) continue;
                if (_sheets.TryGetValue(r.SheetKey, out ViewSheet shared)) { _results[r.RowKey].SheetUniqueId = shared.UniqueId; continue; }
                ViewSheet sheet;
                if (r.SheetAction == SheetAction.Create)
                {
                    var tb = Get<FamilySymbol>(r.TitleBlockUniqueId);
                    if (tb == null) { Fail(r, "Title block type no longer exists."); return; }
                    if (!tb.IsActive) tb.Activate();
                    if (new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Any(s => NameRules.SameName(s.SheetNumber, r.SheetNumber)))
                    { Fail(r, "Sheet number " + r.SheetNumber + " exists now (created after the review). Rebuild the review."); return; }
                    sheet = ViewSheet.Create(_doc, tb.Id);
                    Note(r, RowOutcome.Created, "Sheet created.");
                }
                else
                {
                    sheet = Get<ViewSheet>(r.ExistingSheetUniqueId);
                    if (sheet == null) { Fail(r, "The sheet no longer exists."); return; }
                    string lockReason = Editable(sheet);
                    if (lockReason != null && r.SheetAction == SheetAction.Update) { Fail(r, lockReason); return; }
                    Note(r, r.SheetAction == SheetAction.Update ? RowOutcome.Updated : RowOutcome.Reused);
                }
                if (r.SheetAction != SheetAction.Reuse)
                {
                    try
                    {
                        if (sheet.SheetNumber != r.SheetNumber) sheet.SheetNumber = r.SheetNumber;
                        if (sheet.Name != r.SheetName) sheet.Name = r.SheetName;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException ex) { Fail(r, "Revit refused sheet number/name '" + r.SheetNumber + "': " + ex.Message); return; }
                }
                if (!r.SheetKey.StartsWith("existing|", StringComparison.Ordinal) && SetupStorage.ReadRowKey(sheet) != r.SheetKey)
                    SetupStorage.Write(sheet, r.SheetKey, SetupStorage.KindSheet, _version);
                _sheets[r.SheetKey] = sheet;
                _results[r.RowKey].SheetUniqueId = sheet.UniqueId;
            }
        }

        // ------------------------------------------------------------------ 4. viewports

        private void DoViewports(List<PlanRow> rows)
        {
            foreach (var r in rows)
            {
                if (r.PlaceAction != PlaceAction.Place && r.PlaceAction != PlaceAction.Realign) continue;
                View view = _views.TryGetValue(r.RowKey, out View v) ? v : Get<View>(r.ExistingViewUniqueId);
                ViewSheet sheet = _sheets.TryGetValue(r.SheetKey, out ViewSheet s) ? s : Get<ViewSheet>(r.ExistingSheetUniqueId);
                if (view == null || sheet == null) { Fail(r, "View or sheet missing for placement."); return; }

                Viewport vp = new FilteredElementCollector(_doc, sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>().FirstOrDefault(x => x.ViewId == view.Id);
                if (vp == null)
                {
                    if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
                    { Fail(r, "Revit cannot place '" + view.Name + "' on " + sheet.SheetNumber + " (already placed elsewhere or not placeable)."); return; }
                    XYZ provisional = ProvisionalPoint(r, sheet);
                    vp = Viewport.Create(_doc, sheet.Id, view.Id, provisional);
                    if (vp == null) { Fail(r, "Revit did not create the viewport."); return; }
                    Note(r, RowOutcome.Created, "Viewport placed.");
                    if (!string.IsNullOrEmpty(r.ViewportTypeUniqueId))
                    {
                        var t = _doc.GetElement(r.ViewportTypeUniqueId);
                        if (t != null && vp.GetTypeId() != t.Id) vp.ChangeTypeId(t.Id);
                    }
                    var rot = r.Rotation == ViewportRotationOption.Clockwise ? ViewportRotation.Clockwise
                            : r.Rotation == ViewportRotationOption.Counterclockwise ? ViewportRotation.Counterclockwise : ViewportRotation.None;
                    if (vp.Rotation != rot) vp.Rotation = rot;
                }
                _viewports[r.RowKey] = vp;
                _results[r.RowKey].ViewportUniqueId = vp.UniqueId;
            }
        }

        private XYZ ProvisionalPoint(PlanRow r, ViewSheet sheet)
        {
            var g = _cfg.Group(r.GroupKey);
            var slot = g?.Slot(r.SlotName);
            if (slot == null) return XYZ.Zero;
            var t = SlotTargetOnSheet(slot, sheet, out _);
            return new XYZ(t.X, t.Y, 0);
        }

        /// <summary>Slot target in sheet coordinates (feet). Title-block frame uses the real title block instance transform.</summary>
        private P2 SlotTargetOnSheet(SlotTarget slot, ViewSheet sheet, out string problem)
        {
            problem = null;
            var local = new P2(AlignmentMath.MmToFeet(slot.Xmm), AlignmentMath.MmToFeet(slot.Ymm));
            if (slot.Frame == SheetFrame.Sheet) return local;
            var tbs = SetupCatalogReader.TitleBlocksOn(_doc, sheet);
            if (tbs.Count != 1) { problem = "Sheet " + sheet.SheetNumber + " has " + tbs.Count + " title blocks; a title-block-relative target needs exactly one."; return local; }
            return SheetGeometry.ToAffine(tbs[0].GetTransform()).Apply(local);
        }

        // ------------------------------------------------------------------ 5. alignment

        private void DoAlignment(List<PlanRow> rows)
        {
            double tol = AlignmentMath.MmToFeet(_cfg.ToleranceMm <= 0 ? 0.1 : _cfg.ToleranceMm);
            var references = new Dictionary<string, Affine2>(); // first aligned transform per group+slot, for the overlay check
            foreach (var r in rows)
            {
                if (!_viewports.TryGetValue(r.RowKey, out Viewport vp)) continue;
                var view = _doc.GetElement(vp.ViewId) as View;
                var sheet = _doc.GetElement(vp.SheetId) as ViewSheet;
                var g = _cfg.Group(r.GroupKey);
                var slot = g?.Slot(r.SlotName);
                if (g == null || slot == null) { Fail(r, "Alignment group/slot missing."); return; }
                var target = SlotTargetOnSheet(slot, sheet, out string problem);
                if (problem != null) { Fail(r, problem); return; }

                bool wasPinned = vp.Pinned;
                if (wasPinned)
                {
                    if (!_cfg.AllowRealignPinned) { Note(r, RowOutcome.Skipped, "Viewport is pinned; not moved (allow re-aligning pinned viewports in Placement)."); continue; }
                    vp.Pinned = false;
                }
                string lockReason = Editable(vp);
                if (lockReason != null) { Fail(r, lockReason); return; }

                var probe = new RevitViewportProbe(_doc, view, vp);
                AlignmentReport rep;
                try
                {
                    if (g.Method == AlignmentMethod.ViewportCenter) rep = AlignmentRunner.PlaceCenter(probe, target, tol);
                    else
                    {
                        if (!g.Anchor.IsSet) { Fail(r, "No model anchor for group " + g.Key + "."); return; }
                        if (SheetGeometry.HasNonRectangularCrop(view)) _results[r.RowKey].Message += " Non-rectangular crop: supported for anchor alignment (single crop region, one transform).";
                        string refKey = g.Key + "|" + slot.Name;
                        Affine2? reference = references.TryGetValue(refKey, out Affine2 a) ? a : (Affine2?)null;
                        P2? second = g.Anchor.HasSecond ? new P2(g.Anchor.X2, g.Anchor.Y2) : (P2?)null;
                        rep = AlignmentRunner.AlignAnchor(probe, new P2(g.Anchor.X, g.Anchor.Y), target, tol, reference, second);
                        if (!reference.HasValue && rep.Outcome == AlignmentOutcome.Aligned) references[refKey] = probe.MeasureModelToSheet();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if (wasPinned) vp.Pinned = true;
                    Fail(r, ex.Message);
                    return;
                }
                if (wasPinned) vp.Pinned = true;

                var rr = _results[r.RowKey];
                rr.TargetXmm = AlignmentMath.FeetToMm(rep.Target.X); rr.TargetYmm = AlignmentMath.FeetToMm(rep.Target.Y);
                rr.FinalXmm = AlignmentMath.FeetToMm(rep.Final.X); rr.FinalYmm = AlignmentMath.FeetToMm(rep.Final.Y);
                rr.ErrorMm = rep.ErrorMm;
                rr.SecondPointErrorMm = g.Anchor.HasSecond ? AlignmentMath.FeetToMm(rep.SecondErrorFeet) : (double?)null;
                rr.Moved = rep.Moved;
                try
                {
                    Outline box = vp.GetBoxOutline();
                    rr.BoxMm = new[] { AlignmentMath.FeetToMm(box.MinimumPoint.X), AlignmentMath.FeetToMm(box.MinimumPoint.Y), AlignmentMath.FeetToMm(box.MaximumPoint.X), AlignmentMath.FeetToMm(box.MaximumPoint.Y) };
                    CheckMargins(r, rr);
                }
                catch (Exception) { }

                switch (rep.Outcome)
                {
                    case AlignmentOutcome.Aligned: Note(r, rep.Moved ? RowOutcome.Aligned : RowOutcome.Unchanged, rep.Message + " Error " + rep.ErrorMm.ToString("0.###") + " mm."); break;
                    case AlignmentOutcome.AnchorAlignedDifferentScaleOrientation: Note(r, RowOutcome.AnchorAlignedDifferentScale, rep.Message); break;
                    default: Fail(r, rep.Message); return;
                }
            }
        }

        private void CheckMargins(PlanRow r, RowResult rr)
        {
            var b = rr.BoxMm;
            if (b == null) return;
            if (b[0] < _cfg.UsableLeftMm || b[1] < _cfg.UsableBottomMm || b[2] > _cfg.UsableRightMm || b[3] > _cfg.UsableTopMm)
                rr.Message += " Viewport extends outside the configured usable area (" + b[0].ToString("0") + "," + b[1].ToString("0") + " – " + b[2].ToString("0") + "," + b[3].ToString("0") + " mm).";
        }
    }
}
