using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using CausaFix.ProjectSetup.Model;

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
        public static SetupCatalog Read(Document doc)
        {
            var skipped = new List<string>();
            var cat = new SetupCatalog { DocumentTitle = doc.Title };
            Trace("catalog: start '" + doc.Title + "'");
            try { cat.ProjectNumber = doc.ProjectInformation?.Number ?? string.Empty; } catch (Exception) { }

            Units units = doc.GetUnits();
            Each("level", new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList(), l =>
            {
                string elev;
                try { elev = UnitFormatUtils.Format(units, SpecTypeId.Length, l.Elevation, false); }
                catch (Exception) { elev = (l.Elevation * 304.8).ToString("0", CultureInfo.InvariantCulture) + " mm"; }
                cat.Levels.Add(new LevelInfo { UniqueId = l.UniqueId, Id = RevitCompat.Value(l.Id), Name = l.Name, Elevation = l.Elevation, ElevationText = elev });
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
                cat.TitleBlocks.Add(new TitleBlockInfo
                {
                    UniqueId = tb.UniqueId, Id = RevitCompat.Value(tb.Id), FamilyName = tb.FamilyName, TypeName = tb.Name,
                    WidthMm = Math.Round(w * 304.8, 1), HeightMm = Math.Round(h * 304.8, 1), IsActive = tb.IsActive
                });
            }, skipped);
            Trace("catalog: title blocks " + cat.TitleBlocks.Count);

            try { cat.ViewportTypes.AddRange(ViewportTypes(doc).Select(t => new NamedRef { UniqueId = t.UniqueId, Id = RevitCompat.Value(t.Id), Name = t.Name })); }
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

            // Title block of each sheet: one collector over all title block instances (OwnerViewId = sheet).
            var tbBySheet = new Dictionary<long, string>();
            Each("title block instance", new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().ToList(), e =>
            {
                long owner = RevitCompat.Value(e.OwnerViewId);
                if (!tbBySheet.ContainsKey(owner)) tbBySheet[owner] = doc.GetElement(e.GetTypeId())?.UniqueId ?? string.Empty;
            }, skipped);

            Each("sheet", sheets, s =>
            {
                long id = RevitCompat.Value(s.Id);
                var info = new ExistingSheetInfo
                {
                    UniqueId = s.UniqueId, Id = id, Number = s.SheetNumber, Name = s.Name, IsPlaceholder = s.IsPlaceholder,
                    SetupRowKey = SetupStorage.ReadRowKey(s),
                    TitleBlockTypeUniqueId = tbBySheet.TryGetValue(id, out string tbu) ? tbu : string.Empty
                };
                if (placedViews.TryGetValue(id, out var pv)) info.PlacedViewUniqueIds.AddRange(pv);
                cat.Sheets.Add(info);
            }, skipped);
            Trace("catalog: sheets " + cat.Sheets.Count + (skipped.Count > 0 ? "; skipped " + skipped.Count + ": " + string.Join(" | ", skipped.Take(10)) : ""));
            return cat;
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
