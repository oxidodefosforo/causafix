using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using CausaFix.ProjectCleanup.Model;

namespace CausaFix.Revit.ProjectCleanup
{
    /// <summary>
    /// Reads the documentation of the active document (read-only, API context). Classification comes from the Revit
    /// class and ViewType, never from names or the Project Browser: templates, panel schedule templates (not views),
    /// title-block revision schedules, browsers and internal/report views are not deletable user documentation.
    /// </summary>
    public static class CleanupCatalogReader
    {
        private static readonly HashSet<string> SystemViewTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Undefined", "ProjectBrowser", "SystemBrowser", "Internal", "Report", "CostReport", "LoadsReport", "PresureLossReport", "SystemsAnalysisReport"
        };

        public static CleanupCatalog Read(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            View active = uidoc.ActiveView;
            var cat = new CleanupCatalog
            {
                DocumentTitle = doc.Title,
                DocumentKey = DocumentIdentityReader.SessionKey(doc),
                IsReadOnly = doc.IsReadOnly,
                IsWorkshared = doc.IsWorkshared,
                IsFamilyDocument = doc.IsFamilyDocument
            };
            if (active != null)
            {
                cat.ActiveViewId = RevitCompat.Value(active.Id);
                cat.ActiveUniqueId = active.UniqueId;
                cat.ActiveName = active is ViewSheet avs ? avs.SheetNumber + " - " + avs.Name : active.Name;
                cat.ActiveTypeLabel = Label(active);
            }
            if (doc.IsFamilyDocument) return cat;

            var open = new HashSet<long>();
            try { foreach (UIView uv in uidoc.GetOpenUIViews()) open.Add(RevitCompat.Value(uv.ViewId)); } catch (Exception) { }
            long starting = 0;
            try { var sv = StartingViewSettings.GetStartingViewSettings(doc); if (sv != null && RevitCompat.IsValid(sv.ViewId)) starting = RevitCompat.Value(sv.ViewId); } catch (Exception) { }
            try { cat.PanelScheduleTemplateCount = new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleTemplate)).GetElementCount(); } catch (Exception) { }

            // One pass over elements owned by a view (annotations, details, title blocks, placements): counts per view.
            var owned = new Dictionary<long, int>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                ElementId ov;
                try { ov = e.OwnerViewId; } catch (Exception) { continue; }
                if (!RevitCompat.IsValid(ov)) continue;
                long k = RevitCompat.Value(ov);
                owned[k] = owned.TryGetValue(k, out int n) ? n + 1 : 1;
            }

            // Placements of schedules and panel schedules on sheets (GetAllPlacedViews does not include them).
            var schedulesBySheet = new Dictionary<long, List<long>>();
            var scheduleInstancesBySheet = new Dictionary<long, int>();
            foreach (ScheduleSheetInstance ssi in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)))
            {
                if (ssi.IsTitleblockRevisionSchedule) continue;
                long sheet = RevitCompat.Value(ssi.OwnerViewId);
                if (!schedulesBySheet.TryGetValue(sheet, out var l)) schedulesBySheet[sheet] = l = new List<long>();
                long sid = RevitCompat.Value(ssi.ScheduleId);
                if (!l.Contains(sid)) l.Add(sid);                    // segments of the same schedule: one definition
                scheduleInstancesBySheet[sheet] = scheduleInstancesBySheet.TryGetValue(sheet, out int c) ? c + 1 : 1;
            }
            var panelsBySheet = new Dictionary<long, List<long>>();
            foreach (PanelScheduleSheetInstance psi in new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleSheetInstance)))
            {
                long sheet = RevitCompat.Value(psi.OwnerViewId);
                if (!panelsBySheet.TryGetValue(sheet, out var l)) panelsBySheet[sheet] = l = new List<long>();
                long pid = RevitCompat.Value(psi.ScheduleId);
                if (!l.Contains(pid)) l.Add(pid);
                scheduleInstancesBySheet[sheet] = scheduleInstancesBySheet.TryGetValue(sheet, out int c) ? c + 1 : 1;
            }

            var access = new ParamAccess(doc);
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();   // materialised
            foreach (View v in views)
            {
                if (v.IsTemplate) { cat.TemplateCount++; continue; }
                string vt = v.ViewType.ToString();
                long id = RevitCompat.Value(v.Id);
                if (SystemViewTypes.Contains(vt))
                {
                    cat.Excluded.Add(new ExcludedItem { Id = id, Name = v.Name, TypeLabel = vt, Reason = "Vista de sistema o de informe de Revit: no es documentación de usuario eliminable." });
                    continue;
                }
                if (v is ViewSchedule vs && vs.IsTitleblockRevisionSchedule)
                {
                    cat.Excluded.Add(new ExcludedItem { Id = id, Name = v.Name, TypeLabel = "Tabla de revisiones del cajetín", Reason = "Pertenece al cajetín (title block): no es documentación eliminable." });
                    continue;
                }

                var item = new DocItem
                {
                    Id = id,
                    UniqueId = v.UniqueId,
                    Name = v.Name,
                    Kind = KindOf(v),
                    TypeLabel = Label(v),
                    IsOpen = open.Contains(id),
                    IsStartingView = starting == id,
                    OwnedElementCount = owned.TryGetValue(id, out int ownedCount) ? ownedCount : 0
                };
                try { ElementId pv = v.GetPrimaryViewId(); if (RevitCompat.IsValid(pv)) item.PrimaryViewId = RevitCompat.Value(pv); } catch (Exception) { }
                if (v is ViewSheet sheet)
                {
                    item.SheetNumber = sheet.SheetNumber;
                    item.IsPlaceholder = sheet.IsPlaceholder;
                    try { item.PlacedViewIds = sheet.GetAllPlacedViews().Select(RevitCompat.Value).Distinct().ToList(); } catch (Exception) { }
                    try { item.ViewportCount = sheet.GetAllViewports().Count; } catch (Exception) { }
                    if (schedulesBySheet.TryGetValue(id, out var sl)) item.PlacedScheduleIds = sl;
                    if (panelsBySheet.TryGetValue(id, out var pl)) item.PlacedPanelScheduleIds = pl;
                    item.ScheduleInstanceCount = scheduleInstancesBySheet.TryGetValue(id, out int si) ? si : 0;
                }
                if (v is ViewPlan plan && v.ViewType == ViewType.AreaPlan) item.AreaCount = AreasOf(doc, plan);
                try
                {
                    if (!DocumentValidation.CanDeleteElement(doc, v.Id)) { item.CanDelete = false; item.CannotDeleteReason = "DocumentValidation.CanDeleteElement = false"; }
                }
                catch (Exception ex) { item.CanDelete = false; item.CannotDeleteReason = ex.GetType().Name + ": " + ex.Message; }
                item.LockReason = access.ElementLock(v) ?? string.Empty;
                cat.Items.Add(item);
            }
            return cat;
        }

        public static DocKind KindOf(View v)
        {
            if (v is ViewSheet) return DocKind.Sheet;
            if (v is PanelScheduleView) return DocKind.PanelSchedule;
            if (v is ViewSchedule) return DocKind.Schedule;
            if (v.ViewType == ViewType.Legend) return DocKind.Legend;
            return DocKind.View;
        }

        public static string Label(View v)
        {
            string dep = RevitCompat.IsValid(SafePrimary(v)) ? " (dependiente)" : "";
            if (v is ViewSheet s) return s.IsPlaceholder ? "Plano (marcador de posición)" : "Plano";
            if (v is PanelScheduleView) return "Panel schedule";
            if (v is ViewSchedule vs)
            {
                try
                {
                    var d = vs.Definition;
                    if (vs.IsInternalKeynoteSchedule) return "Leyenda de notas clave";
                    if (d.IsMaterialTakeoff) return "Tabla de materiales (takeoff)";
                    if (d.IsKeySchedule) return "Tabla de claves";
                    if (RevitCompat.IsValid(d.FamilyId)) return "Bloque de notas";
                    long c = RevitCompat.Value(d.CategoryId);
                    if (c == (long)BuiltInCategory.OST_Sheets) return "Lista de planos";
                    if (c == (long)BuiltInCategory.OST_Views) return "Lista de vistas";
                }
                catch (Exception) { }
                return "Schedule";
            }
            switch (v.ViewType.ToString())
            {
                case "FloorPlan": return "Planta" + dep;
                case "CeilingPlan": return "Plano de techo (RCP)" + dep;
                case "EngineeringPlan": return "Planta estructural" + dep;
                case "AreaPlan": return "Plano de áreas" + dep;
                case "Section": return "Sección" + dep;
                case "Elevation": return "Alzado" + dep;
                case "Detail": return "Detalle" + dep;
                case "ThreeD": return "Vista 3D" + dep;
                case "DraftingView": return "Vista de dibujo (drafting)";
                case "Legend": return "Leyenda";
                case "Walkthrough": return "Recorrido";
                case "Rendering": return "Imagen de render";
                case "ColumnSchedule": return "Tabla gráfica de pilares";
                default: return v.ViewType.ToString() + dep;
            }
        }

        private static ElementId SafePrimary(View v)
        {
            try { return v.GetPrimaryViewId(); } catch (Exception) { return ElementId.InvalidElementId; }
        }

        private static int AreasOf(Document doc, ViewPlan plan)
        {
            try
            {
                ElementId scheme = plan.AreaScheme?.Id, level = plan.GenLevel?.Id;
                if (scheme == null || level == null) return 0;
                return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType()
                    .OfType<Area>().Count(a => a.AreaScheme != null && a.AreaScheme.Id.Equals(scheme) && a.LevelId.Equals(level));
            }
            catch (Exception) { return 0; }
        }
    }
}
