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

        public static SetupCatalog Read(Document doc)
        {
            var cat = new SetupCatalog { DocumentTitle = doc.Title, ProjectNumber = doc.ProjectInformation?.Number ?? string.Empty };

            foreach (Level l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            {
                cat.Levels.Add(new LevelInfo
                {
                    UniqueId = l.UniqueId, Id = RevitCompat.Value(l.Id), Name = l.Name, Elevation = l.Elevation,
                    ElevationText = UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, l.Elevation, false)
                });
            }

            foreach (ViewFamilyType t in new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>())
            {
                var f = FamilyOf(t.ViewFamily);
                if (f.HasValue) cat.ViewFamilyTypes.Add(new ViewFamilyTypeInfo { UniqueId = t.UniqueId, Id = RevitCompat.Value(t.Id), Name = t.Name, Family = f.Value });
            }

            var scaleId = new ElementId(BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC);
            var scaleId2 = new ElementId(BuiltInParameter.VIEW_SCALE);
            var disciplineId = new ElementId(BuiltInParameter.VIEW_DISCIPLINE);
            var scopeId = new ElementId(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            var cropId = new ElementId(BuiltInParameter.VIEWER_CROP_REGION);
            var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
            foreach (View v in allViews.Where(v => v.IsTemplate))
            {
                var controlled = new HashSet<long>();
                try
                {
                    var all = v.GetTemplateParameterIds().Select(RevitCompat.Value);
                    var non = new HashSet<long>(v.GetNonControlledTemplateParameterIds().Select(RevitCompat.Value));
                    foreach (var id in all) if (!non.Contains(id)) controlled.Add(id);
                }
                catch (Exception) { /* not a view template with parameters */ }
                bool Controls(ElementId id) => controlled.Contains(RevitCompat.Value(id));
                cat.Templates.Add(new TemplateInfo
                {
                    UniqueId = v.UniqueId, Id = RevitCompat.Value(v.Id), Name = v.Name, Family = FamilyOf(v.ViewType),
                    ControlsScale = Controls(scaleId) || Controls(scaleId2), Scale = SafeScale(v),
                    ControlsDiscipline = Controls(disciplineId), ControlsScopeBox = Controls(scopeId), ControlsCrop = Controls(cropId)
                });
            }

            foreach (FamilySymbol tb in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<FamilySymbol>())
            {
                double w = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH)?.AsDouble() ?? 0;
                double h = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT)?.AsDouble() ?? 0;
                cat.TitleBlocks.Add(new TitleBlockInfo
                {
                    UniqueId = tb.UniqueId, Id = RevitCompat.Value(tb.Id), FamilyName = tb.FamilyName, TypeName = tb.Name,
                    WidthMm = Math.Round(w * 304.8, 1), HeightMm = Math.Round(h * 304.8, 1), IsActive = tb.IsActive
                });
            }

            cat.ViewportTypes.AddRange(ViewportTypes(doc).Select(t => new NamedRef { UniqueId = t.UniqueId, Id = RevitCompat.Value(t.Id), Name = t.Name }));
            foreach (Element sb in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType())
                cat.ScopeBoxes.Add(new NamedRef { UniqueId = sb.UniqueId, Id = RevitCompat.Value(sb.Id), Name = sb.Name });

            // Where each view is placed (one viewport per plan view).
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            var placedOn = new Dictionary<long, string>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                var sheet = doc.GetElement(vp.SheetId) as ViewSheet;
                if (sheet != null) placedOn[RevitCompat.Value(vp.ViewId)] = sheet.SheetNumber;
            }

            foreach (View v in allViews.Where(v => !v.IsTemplate && !(v is ViewSheet)))
            {
                var f = FamilyOf(v.ViewType);
                var info = new ExistingViewInfo
                {
                    UniqueId = v.UniqueId, Id = RevitCompat.Value(v.Id), Name = v.Name, Family = f, ViewType = NameGroup(v),
                    TitleOnSheet = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.AsString() ?? string.Empty,
                    Scale = SafeScale(v),
                    PlacedOnSheet = placedOn.TryGetValue(RevitCompat.Value(v.Id), out string sn) ? sn : string.Empty
                };
                if (f.HasValue)
                {
                    if (v is ViewPlan vp && vp.GenLevel != null) info.LevelUniqueId = vp.GenLevel.UniqueId;
                    if (RevitCompat.IsValid(v.ViewTemplateId)) info.TemplateUniqueId = doc.GetElement(v.ViewTemplateId)?.UniqueId ?? string.Empty;
                    info.IsDependent = RevitCompat.IsValid(v.GetPrimaryViewId());
                    info.SetupRowKey = SetupStorage.ReadRowKey(v);
                }
                cat.Views.Add(info);
            }

            foreach (ViewSheet s in sheets)
            {
                var info = new ExistingSheetInfo
                {
                    UniqueId = s.UniqueId, Id = RevitCompat.Value(s.Id), Number = s.SheetNumber, Name = s.Name, IsPlaceholder = s.IsPlaceholder,
                    SetupRowKey = SetupStorage.ReadRowKey(s)
                };
                var tb = TitleBlocksOn(doc, s).FirstOrDefault();
                if (tb != null) info.TitleBlockTypeUniqueId = doc.GetElement(tb.GetTypeId())?.UniqueId ?? string.Empty;
                if (!s.IsPlaceholder)
                    foreach (var id in s.GetAllPlacedViews()) { var pv = doc.GetElement(id); if (pv != null) info.PlacedViewUniqueIds.Add(pv.UniqueId); }
                cat.Sheets.Add(info);
            }
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
