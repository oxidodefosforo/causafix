using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.Revit.ProjectSetup
{
    /// <summary>Reads everything Project Setup needs from the host document (API context, read-only).</summary>
    public static class SetupCatalogReader
    {
        public static PlanFamily? FamilyOf(ViewFamily f)
        {
            switch (f)
            {
                case ViewFamily.FloorPlan: return PlanFamily.FloorPlan;
                case ViewFamily.CeilingPlan: return PlanFamily.CeilingPlan;
                case ViewFamily.StructuralPlan: return PlanFamily.StructuralPlan;
                default: return null;
            }
        }

        public static PlanFamily? FamilyOf(ViewType t)
        {
            switch (t)
            {
                case ViewType.FloorPlan: return PlanFamily.FloorPlan;
                case ViewType.CeilingPlan: return PlanFamily.CeilingPlan;
                case ViewType.EngineeringPlan: return PlanFamily.StructuralPlan;
                default: return null;
            }
        }

        /// <summary>Uniqueness group used for view names (plan families use the PlanFamily name).</summary>
        public static string NameGroup(View v) => FamilyOf(v.ViewType)?.ToString() ?? v.ViewType.ToString();

        /// <summary>Breadcrumbs written to the CausaFix log (set by the add-in). Each line is flushed immediately.</summary>
        public static Action<string> Trace = _ => { };

        private static void Each<T>(string what, IEnumerable<T> items, Action<T> body, List<string> skipped)
        {
            foreach (var item in items)
            {
                try { body(item); }
                catch (Exception ex) { skipped.Add(what + ": " + ex.GetType().Name + " " + ex.Message); }
            }
        }

        /// <summary>
        /// Reads only what Project Setup uses: host levels, plan view types, plan templates, title blocks, viewport types,
        /// scope boxes, plan views and sheets. Internal/browser views are never touched; a problem with one element skips it.
        /// </summary>
        public static SetupCatalog Read(Document doc, IReadOnlyCollection<ParamRef> wanted = null)
        {
            var skipped = new List<string>();
            var cat = new SetupCatalog { DocumentTitle = doc.Title };
            var defs = new Dictionary<string, ParamInfo>();
            Trace("catalog: start '" + doc.Title + "'");
            try { cat.ProjectNumber = doc.ProjectInformation?.Number ?? string.Empty; } catch (Exception) { }
            try { ParamReader.Collect(doc, doc.ProjectInformation, ParamScope.ProjectInformation, false, defs, cat.ProjectInfoValues); }
            catch (Exception ex) { skipped.Add("project information parameters: " + ex.Message); }

            Units units = doc.GetUnits();
            Each("level", new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList(), l =>
            {
                string elev;
                try { elev = UnitFormatUtils.Format(units, SpecTypeId.Length, l.Elevation, false); }
                catch (Exception) { elev = (l.Elevation * 304.8).ToString("0", CultureInfo.InvariantCulture) + " mm"; }
                var info = new LevelInfo { UniqueId = l.UniqueId, Id = RevitCompat.Value(l.Id), Name = l.Name, Elevation = l.Elevation, ElevationText = elev };
                ParamReader.Collect(doc, l, ParamScope.Level, false, defs, info.Values);
                cat.Levels.Add(info);
            }, skipped);
            Trace("catalog: levels " + cat.Levels.Count);

            Each("view type", new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList(), t =>
            {
                var f = FamilyOf(t.ViewFamily);
                if (f.HasValue) cat.ViewFamilyTypes.Add(new ViewFamilyTypeInfo { UniqueId = t.UniqueId, Id = RevitCompat.Value(t.Id), Name = t.Name, Family = f.Value });
            }, skipped);
            Trace("catalog: view types " + cat.ViewFamilyTypes.Count);

            // Plan views and plan templates only (ViewPlan class): browser, schedule, sheet and internal views are not read.
            var plans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => FamilyOf(v.ViewType).HasValue).ToList();
            Trace("catalog: plan views+templates " + plans.Count);

            var scaleIds = new[] { RevitCompat.Value(new ElementId(BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC)), RevitCompat.Value(new ElementId(BuiltInParameter.VIEW_SCALE)) };
            long disciplineId = RevitCompat.Value(new ElementId(BuiltInParameter.VIEW_DISCIPLINE));
            long scopeId = RevitCompat.Value(new ElementId(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP));
            long cropId = RevitCompat.Value(new ElementId(BuiltInParameter.VIEWER_CROP_REGION));
            Each("template", plans.Where(v => v.IsTemplate).ToList(), v =>
            {
                var controlled = new HashSet<long>();
                try
                {
                    var non = new HashSet<long>(v.GetNonControlledTemplateParameterIds().Select(RevitCompat.Value));
                    foreach (var id in v.GetTemplateParameterIds()) { long k = RevitCompat.Value(id); if (!non.Contains(k)) controlled.Add(k); }
                }
                catch (Exception) { }
                cat.Templates.Add(new TemplateInfo
                {
                    UniqueId = v.UniqueId, Id = RevitCompat.Value(v.Id), Name = v.Name, Family = FamilyOf(v.ViewType),
                    ControlsScale = scaleIds.Any(controlled.Contains), Scale = SafeScale(v),
                    ControlsDiscipline = controlled.Contains(disciplineId), ControlsScopeBox = controlled.Contains(scopeId), ControlsCrop = controlled.Contains(cropId)
                });
            }, skipped);
            Trace("catalog: templates " + cat.Templates.Count);

            Each("title block", new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().OfType<FamilySymbol>().ToList(), tb =>
            {
                double w = 0, h = 0;
                try { w = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH)?.AsDouble() ?? 0; h = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT)?.AsDouble() ?? 0; } catch (Exception) { }
                var info = new TitleBlockInfo
                {
                    UniqueId = tb.UniqueId, Id = RevitCompat.Value(tb.Id), FamilyName = tb.FamilyName, TypeName = tb.Name,
                    WidthMm = Math.Round(w * 304.8, 1), HeightMm = Math.Round(h * 304.8, 1), IsActive = tb.IsActive
                };
                ParamReader.Collect(doc, tb, ParamScope.TitleBlockType, true, defs, info.Values);
                cat.TitleBlocks.Add(info);
            }, skipped);
            Trace("catalog: title blocks " + cat.TitleBlocks.Count);

            try
            {
                foreach (var t in ViewportTypes(doc))
                {
                    var nr = new NamedRef { UniqueId = t.UniqueId, Id = RevitCompat.Value(t.Id), Name = t.Name };
                    try
                    {
                        var st = ParamReader.ShowTitle(t);
                        if (st != null) { nr.ShowTitleValue = st.AsInteger(); nr.ShowTitleText = st.AsValueString() ?? ""; }
                    }
                    catch (Exception) { }
                    cat.ViewportTypes.Add(nr);
                }
            }
            catch (Exception ex) { skipped.Add("viewport types: " + ex.Message); }
            Each("scope box", new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().ToList(), sb =>
                cat.ScopeBoxes.Add(new NamedRef { UniqueId = sb.UniqueId, Id = RevitCompat.Value(sb.Id), Name = sb.Name }), skipped);
            Trace("catalog: viewport types " + cat.ViewportTypes.Count + ", scope boxes " + cat.ScopeBoxes.Count);

            // Placements from the viewports themselves (no per-sheet collectors).
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            var sheetById = sheets.ToDictionary(x => RevitCompat.Value(x.Id));
            var placedOn = new Dictionary<long, string>();
            var placedViews = new Dictionary<long, List<string>>();
            Each("viewport", new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().ToList(), vp =>
            {
                long sid = RevitCompat.Value(vp.SheetId);
                if (!sheetById.TryGetValue(sid, out ViewSheet sh)) return;
                placedOn[RevitCompat.Value(vp.ViewId)] = sh.SheetNumber;
                var view = doc.GetElement(vp.ViewId);
                if (view == null) return;
                if (!placedViews.TryGetValue(sid, out var list)) placedViews[sid] = list = new List<string>();
                list.Add(view.UniqueId);
                var info = new ExistingViewportInfo
                {
                    UniqueId = vp.UniqueId, ViewUniqueId = view.UniqueId, SheetUniqueId = sh.UniqueId,
                    TypeUniqueId = doc.GetElement(vp.GetTypeId())?.UniqueId ?? "", Pinned = vp.Pinned,
                    Rotation = vp.Rotation == ViewportRotation.Clockwise ? 1 : vp.Rotation == ViewportRotation.Counterclockwise ? 2 : 0
                };
                try { info.BoxMm = Mm(vp.GetBoxOutline()); } catch (Exception) { }
                try { info.LabelMm = Mm(vp.GetLabelOutline()); } catch (Exception) { }
                cat.Viewports.Add(info);
            }, skipped);
            Trace("catalog: viewports " + placedOn.Count);

            Each("view", plans.Where(v => !v.IsTemplate).ToList(), v =>
            {
                var info = new ExistingViewInfo
                {
                    UniqueId = v.UniqueId, Id = RevitCompat.Value(v.Id), Name = v.Name, Family = FamilyOf(v.ViewType), ViewType = NameGroup(v),
                    TitleOnSheet = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.AsString() ?? string.Empty,
                    Scale = SafeScale(v),
                    PlacedOnSheet = placedOn.TryGetValue(RevitCompat.Value(v.Id), out string sn) ? sn : string.Empty,
                    LevelUniqueId = v.GenLevel?.UniqueId ?? string.Empty
                };
                if (RevitCompat.IsValid(v.ViewTemplateId)) info.TemplateUniqueId = doc.GetElement(v.ViewTemplateId)?.UniqueId ?? string.Empty;
                info.IsDependent = RevitCompat.IsValid(v.GetPrimaryViewId());
                info.SetupRowKey = SetupStorage.ReadRowKey(v);
                cat.Views.Add(info);
            }, skipped);
            Trace("catalog: plan views " + cat.Views.Count);
            // View parameters offered as sources: definitions from one view of each plan family (values are read on demand).
            foreach (var g in plans.Where(v => !v.IsTemplate).GroupBy(v => v.ViewType))
                try { ParamReader.Collect(doc, g.First(), ParamScope.View, false, defs, null); } catch (Exception) { }

            // Title block of each sheet: one collector over all title block instances (OwnerViewId = sheet).
            var tbBySheet = new Dictionary<long, string>();
            var tbCount = new Dictionary<long, int>();
            var tbInstances = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().ToList();
            Each("title block instance", tbInstances, e =>
            {
                long owner = RevitCompat.Value(e.OwnerViewId);
                tbCount[owner] = tbCount.TryGetValue(owner, out int n) ? n + 1 : 1;
                if (!tbBySheet.ContainsKey(owner)) tbBySheet[owner] = doc.GetElement(e.GetTypeId())?.UniqueId ?? string.Empty;
            }, skipped);
            if (tbInstances.Count > 0) try { ParamReader.Collect(doc, tbInstances[0], ParamScope.TitleBlockInstance, false, defs, null); } catch (Exception) { }
            if (sheets.Count > 0) try { ParamReader.Collect(doc, sheets[0], ParamScope.Sheet, false, defs, null); } catch (Exception) { }

            Each("sheet", sheets, s =>
            {
                long id = RevitCompat.Value(s.Id);
                var info = new ExistingSheetInfo
                {
                    UniqueId = s.UniqueId, Id = id, Number = s.SheetNumber, Name = s.Name, IsPlaceholder = s.IsPlaceholder,
                    SetupRowKey = SetupStorage.ReadRowKey(s),
                    TitleBlockTypeUniqueId = tbBySheet.TryGetValue(id, out string tbu) ? tbu : string.Empty,
                    TitleBlockCount = tbCount.TryGetValue(id, out int tbn) ? tbn : 0
                };
                if (placedViews.TryGetValue(id, out var pv)) info.PlacedViewUniqueIds.AddRange(pv);
                cat.Sheets.Add(info);
            }, skipped);
            cat.Parameters.AddRange(defs.Values.OrderBy(p => p.Ref.Scope).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase));
            if (wanted != null && wanted.Count > 0) cat.Merge(ReadValues(doc, wanted));
            Trace("catalog: sheets " + cat.Sheets.Count + ", parameters " + cat.Parameters.Count + ", viewports " + cat.Viewports.Count + (skipped.Count > 0 ? "; skipped " + skipped.Count + ": " + string.Join(" | ", skipped.Take(10)) : ""));
            return cat;
        }

        private static double[] Mm(Outline o)
        {
            if (o == null) return null;
            return new[] { o.MinimumPoint.X * 304.8, o.MinimumPoint.Y * 304.8, o.MaximumPoint.X * 304.8, o.MaximumPoint.Y * 304.8 };
        }

        /// <summary>Values of View / Sheet / Title Block instance parameters used by the name rules (read-only, on demand).</summary>
        public static ParamValueSet ReadValues(Document doc, IReadOnlyCollection<ParamRef> wanted)
        {
            var set = new ParamValueSet();
            var views = wanted.Where(w => w.Scope == ParamScope.View).ToList();
            var sheets = wanted.Where(w => w.Scope == ParamScope.Sheet).ToList();
            var tbs = wanted.Where(w => w.Scope == ParamScope.TitleBlockInstance).ToList();
            set.Keys.AddRange(views.Concat(sheets).Concat(tbs).Select(w => w.Key));
            if (views.Count > 0)
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate))
                {
                    var d = new Dictionary<string, string>();
                    ParamReader.Read(doc, v, views, d);
                    set.Views[v.UniqueId] = d;
                }
            var allSheets = sheets.Count > 0 || tbs.Count > 0 ? new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList() : new List<ViewSheet>();
            foreach (var s in allSheets)
            {
                if (sheets.Count > 0) { var d = new Dictionary<string, string>(); ParamReader.Read(doc, s, sheets, d); set.Sheets[s.UniqueId] = d; }
                if (tbs.Count > 0)
                {
                    var inst = TitleBlocksOn(doc, s);
                    if (inst.Count == 1) { var d = new Dictionary<string, string>(); ParamReader.Read(doc, inst[0], tbs, d); set.TitleBlocks[s.UniqueId] = d; }
                }
            }
            return set;
        }

        public static int SafeScale(View v)
        {
            try { return v.Scale; } catch (Exception) { return 0; }
        }

        public static List<FamilyInstance> TitleBlocksOn(Document doc, ViewSheet s)
            => new FilteredElementCollector(doc, s.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();

        /// <summary>Viewport types: all element types of the same class as the document's default viewport type.</summary>
        public static List<ElementType> ViewportTypes(Document doc)
        {
            var list = new List<ElementType>();
            ElementId def = doc.GetDefaultElementTypeId(ElementTypeGroup.ViewportType);
            var defType = RevitCompat.IsValid(def) ? doc.GetElement(def) as ElementType : null;
            if (defType == null) return list;
            foreach (ElementType t in new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>())
                if (t.FamilyName == defType.FamilyName && t.GetType() == defType.GetType()) list.Add(t);
            return list.OrderBy(t => t.Name).ToList();
        }
    }
}
