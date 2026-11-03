using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CausaFix.ProjectCleanup.Model;

namespace CausaFix.ProjectCleanup.Planning
{
    public enum PlanAction
    {
        /// <summary>Deleted by the cleanup.</summary>
        Delete = 0,
        /// <summary>Kept to preserve the active view (the view itself, its primary, the content of the active sheet…).</summary>
        Protected,
        /// <summary>Kept because Revit or worksharing does not allow deleting it, or a kept view depends on it.</summary>
        Kept
    }

    public sealed class PlanEntry
    {
        public DocItem Item { get; set; }
        public PlanAction Action { get; set; }
        public string Reason { get; set; } = string.Empty;
        /// <summary>Position in the delete order (Delete entries only; 0 otherwise).</summary>
        public int Order { get; set; }
    }

    /// <summary>Result of the analysis: what is deleted, what stays and why, and the expected consequences.</summary>
    public sealed class CleanupPlan
    {
        public CleanupCatalog Catalog { get; set; }
        public CleanupOptions Options { get; set; } = new CleanupOptions();
        public List<PlanEntry> Entries { get; } = new List<PlanEntry>();
        public List<string> Consequences { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();
        /// <summary>The analysis cannot be executed (read-only document, family document…).</summary>
        public string BlockedReason { get; set; } = string.Empty;
        /// <summary>Identity of exactly this review (document, active view, every action). Execution requires the same one.</summary>
        public string Signature { get; set; } = string.Empty;

        public IEnumerable<PlanEntry> ToDelete => Entries.Where(e => e.Action == PlanAction.Delete).OrderBy(e => e.Order);
        public IEnumerable<PlanEntry> Preserved => Entries.Where(e => e.Action != PlanAction.Delete);
        public int Count(DocKind k) => Entries.Count(e => e.Action == PlanAction.Delete && e.Item.Kind == k);
        public int DeleteCount => Entries.Count(e => e.Action == PlanAction.Delete);
        public bool NothingToDelete => DeleteCount == 0;
        public bool CanRun => string.IsNullOrEmpty(BlockedReason) && !NothingToDelete;
        public HashSet<long> ProtectedIds => new HashSet<long>(Entries.Where(e => e.Action != PlanAction.Delete).Select(e => e.Item.Id)) { Catalog.ActiveViewId };
    }

    /// <summary>
    /// Builds the cleanup plan from the catalog. Rules:
    ///  - The active view is always protected (also when it is not a listed documentation item).
    ///  - A protected or kept view keeps its primary view (deleting a primary deletes its dependents), recursively.
    ///  - Active sheet: the sheet, every view/legend placed on it, its schedules and panel schedules are protected,
    ///    and with them the primaries of placed dependent views.
    ///  - A view Revit cannot delete, or owned by another user, is kept with its reason; anything whose deletion
    ///    would take a kept or protected element with it is kept too.
    ///  - Delete order: sheets (their viewports and schedule placements go with them), then dependent views before
    ///    their primaries, then the other views, legends, schedules and panel schedules.
    /// </summary>
    public static class CleanupPlanner
    {
        public const string SingleViewTip = "Para una limpieza que deje una sola vista de usuario, ejecuta la herramienta desde una vista independiente (por ejemplo, una vista 3D).";

        public static CleanupPlan Build(CleanupCatalog cat, CleanupOptions options = null)
        {
            options = options ?? new CleanupOptions();
            var plan = new CleanupPlan { Catalog = cat, Options = options };
            if (cat.IsFamilyDocument) plan.BlockedReason = "Project Cleanup trabaja en documentos de proyecto, no en familias.";
            else if (cat.IsReadOnly) plan.BlockedReason = "El documento es de solo lectura: no se puede eliminar nada.";

            // CAD, links and images take part only when their option is on (otherwise they are not touched at all).
            var items = cat.Items.Where(i => options.Includes(i.Kind)).ToList();
            var byId = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var reasons = new Dictionary<long, (PlanAction action, string reason)>();
            void Mark(long id, PlanAction a, string why)
            {
                if (!byId.ContainsKey(id) && id != cat.ActiveViewId) return;
                if (reasons.TryGetValue(id, out var cur) && (cur.action == PlanAction.Protected || a == PlanAction.Kept)) return; // keep the first protection reason
                reasons[id] = (a, why);
            }

            // 1. Active view (and, for a sheet, what is placed on it).
            Mark(cat.ActiveViewId, PlanAction.Protected, "Vista activa: se conserva.");
            byId.TryGetValue(cat.ActiveViewId, out DocItem active);
            if (active != null && active.Kind == DocKind.Sheet)
            {
                foreach (long v in active.PlacedViewIds) Mark(v, PlanAction.Protected, "Colocada en el plano activo " + active.Display + ".");
                foreach (long s in active.PlacedScheduleIds) Mark(s, PlanAction.Protected, "Schedule colocado en el plano activo " + active.Display + ".");
                foreach (long s in active.PlacedPanelScheduleIds) Mark(s, PlanAction.Protected, "Panel schedule colocado en el plano activo " + active.Display + ".");
            }

            // 2. Elements Revit or worksharing does not let us delete.
            foreach (var i in items)
            {
                if (reasons.ContainsKey(i.Id)) continue;
                if (!string.IsNullOrEmpty(i.LockReason)) Mark(i.Id, PlanAction.Kept, "Worksharing: " + i.LockReason);
                else if (!i.CanDelete) Mark(i.Id, PlanAction.Kept, "Revit no permite eliminarla" + (string.IsNullOrEmpty(i.CannotDeleteReason) ? "." : ": " + i.CannotDeleteReason));
            }

            // A sheet that cannot be edited (worksharing) keeps what is placed on it: deleting a placed view or schedule
            // would remove its viewport/placement from that sheet, which needs the sheet to be editable.
            foreach (var s in items.Where(i => i.Kind == DocKind.Sheet && !string.IsNullOrEmpty(i.LockReason)).ToList())
                foreach (long v in s.PlacedViewIds.Concat(s.PlacedScheduleIds).Concat(s.PlacedPanelScheduleIds))
                    if (!reasons.ContainsKey(v)) Mark(v, PlanAction.Kept, "Colocada en el plano " + s.Display + ", que no se puede editar (worksharing).");

            // 3. Closure: a preserved view keeps its primary (deleting the primary would delete it too).
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var kv in reasons.ToList())
                {
                    if (!byId.TryGetValue(kv.Key, out var item) || item.PrimaryViewId == 0) continue;
                    if (reasons.ContainsKey(item.PrimaryViewId)) continue;
                    if (!byId.ContainsKey(item.PrimaryViewId)) continue;
                    string why = kv.Key == cat.ActiveViewId
                        ? "Vista primaria de la vista activa (dependiente): eliminarla eliminaría también la vista activa."
                        : "Vista primaria de '" + item.Display + "', que se conserva: eliminarla la eliminaría también.";
                    reasons[item.PrimaryViewId] = (kv.Value.action == PlanAction.Kept ? PlanAction.Kept : PlanAction.Protected, why);
                    changed = true;
                }
            }

            // CAD / images placed in a preserved view (view-specific instance) stay: the preserved view keeps its content.
            var preservedViews = new HashSet<long>(reasons.Keys) { cat.ActiveViewId };
            foreach (var i in items.Where(i => CleanupOptions.IsExternal(i.Kind)))
            {
                if (reasons.ContainsKey(i.Id)) continue;
                long owner = i.InstanceOwnerViewIds.FirstOrDefault(preservedViews.Contains);
                if (owner != 0)
                    Mark(i.Id, PlanAction.Protected, "Tiene una instancia colocada en " + (owner == cat.ActiveViewId ? "la vista activa" : "'" + (byId.TryGetValue(owner, out var ov) ? ov.Display : owner.ToString(CultureInfo.InvariantCulture)) + "', que se conserva") + ".");
            }

            // 4. Entries and delete order (CAD, links and images last: view-specific ones may already be gone with their views).
            int order = 0;
            int Rank(DocItem i) => i.Kind == DocKind.Sheet ? 0 : i.Kind == DocKind.View && i.PrimaryViewId != 0 ? 1 : i.Kind == DocKind.View ? 2 : i.Kind == DocKind.Legend ? 3 : i.Kind == DocKind.Schedule ? 4
                                 : i.Kind == DocKind.PanelSchedule ? 5 : i.Kind == DocKind.Image ? 6 : i.Kind == DocKind.CadImport ? 7 : i.Kind == DocKind.CadLink ? 8 : 9;
            foreach (var i in items.OrderBy(Rank).ThenBy(i => i.Kind == DocKind.Sheet ? i.SheetNumber : i.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Id))
            {
                if (reasons.TryGetValue(i.Id, out var r)) plan.Entries.Add(new PlanEntry { Item = i, Action = r.action, Reason = r.reason });
                else plan.Entries.Add(new PlanEntry { Item = i, Action = PlanAction.Delete, Reason = DeleteReason(i), Order = ++order });
            }

            Consequences(plan, byId);
            Notes(plan, active);
            plan.Signature = Sign(plan);
            return plan;
        }

        private static string DeleteReason(DocItem i)
        {
            switch (i.Kind)
            {
                case DocKind.Sheet: return "Se elimina el plano" + (i.ViewportCount + i.ScheduleInstanceCount > 0 ? " con sus colocaciones (" + (i.ViewportCount + i.ScheduleInstanceCount) + "); las vistas colocadas se tratan por separado." : ".");
                case DocKind.Schedule: return "Se elimina la tabla y todas sus colocaciones en planos.";
                case DocKind.PanelSchedule: return "Se elimina el panel schedule (documentación); el panel y sus circuitos no se tocan.";
                case DocKind.CadLink: return "Se quita el CAD vinculado y sus " + i.InstanceIds.Count + " instancia(s); el archivo de origen no se toca.";
                case DocKind.CadImport: return "Se elimina el CAD importado y sus " + i.InstanceIds.Count + " instancia(s).";
                case DocKind.RevitLink: return "Se quita el vínculo de Revit y sus " + i.InstanceIds.Count + " instancia(s) (y sus vínculos anidados); el archivo .rvt no se toca.";
                case DocKind.Image: return "Se elimina la imagen y sus " + i.InstanceIds.Count + " instancia(s).";
                default: return i.PrimaryViewId != 0 ? "Se elimina (vista dependiente)." : "Se elimina.";
            }
        }

        private static void Consequences(CleanupPlan plan, Dictionary<long, DocItem> byId)
        {
            var del = new HashSet<long>(plan.ToDelete.Select(e => e.Item.Id));
            var deleted = plan.ToDelete.Select(e => e.Item).ToList();
            int viewports = deleted.Where(i => i.Kind == DocKind.Sheet).Sum(i => i.ViewportCount);
            int schedInst = deleted.Where(i => i.Kind == DocKind.Sheet).Sum(i => i.ScheduleInstanceCount);
            int owned = deleted.Sum(i => i.OwnedElementCount);
            int areas = deleted.Sum(i => i.AreaCount);
            if (viewports > 0) plan.Consequences.Add(viewports + " viewport(s) de los planos eliminados.");
            if (schedInst > 0) plan.Consequences.Add(schedInst + " colocación(es) de schedules en los planos eliminados (las tablas se tratan por separado).");
            if (owned > 0) plan.Consequences.Add(owned + " elemento(s) propios de las vistas eliminadas (anotaciones, detalles, cajetines de los planos…).");
            if (areas > 0) plan.Consequences.Add(areas + " área(s) de los planos de áreas eliminados: Revit las elimina con su plano.");

            // Placements on sheets that stay but show something that is deleted.
            foreach (var s in plan.Preserved.Select(e => e.Item).Where(i => i.Kind == DocKind.Sheet))
            {
                int v = s.PlacedViewIds.Count(del.Contains);
                int sc = s.PlacedScheduleIds.Concat(s.PlacedPanelScheduleIds).Count(del.Contains);
                if (v + sc > 0) plan.Consequences.Add("El plano conservado " + s.Display + " pierde " + (v + sc) + " colocación(es) de vistas o tablas que se eliminan.");
            }
            int dependentsWithPrimary = deleted.Count(i => i.PrimaryViewId != 0 && del.Contains(i.PrimaryViewId));
            if (dependentsWithPrimary > 0) plan.Consequences.Add(dependentsWithPrimary + " vista(s) dependiente(s) se eliminan junto con su primaria (no se borran dos veces).");
            if (deleted.Any(i => i.IsOpen)) plan.Consequences.Add(deleted.Count(i => i.IsOpen) + " vista(s) abiertas en Revit se cerrarán antes de eliminarlas (la vista activa sigue abierta).");
            if (deleted.Any(i => i.IsStartingView)) plan.Consequences.Add("Se elimina la vista de inicio del proyecto (Starting View): Revit usará su opción por defecto.");
            var ext = deleted.Where(i => CleanupOptions.IsExternal(i.Kind)).ToList();
            if (ext.Count > 0) plan.Consequences.Add(ext.Sum(i => i.InstanceIds.Count) + " instancia(s) de CAD, vínculos o imágenes se eliminan con sus tipos. Los archivos externos no se tocan.");
            if (deleted.Any(i => i.Kind == DocKind.RevitLink))
                plan.Consequences.Add("Al quitar un vínculo de Revit se pierden sus elementos copiados/monitorizados (Copy/Monitor), anotaciones y cotas que lo referencian; Revit puede pedir confirmación si hay elementos dependientes.");
            plan.Consequences.Add("Pueden desaparecer marcadores de sección, alzado y llamada, y referencias a las vistas eliminadas; no se eliminan niveles, ejes, elementos del modelo, familias, paneles ni circuitos" +
                                  (plan.Options.DeleteRevitLinks || plan.Options.DeleteCad ? "." : ", ni CAD, vínculos o imágenes (opciones desactivadas)."));
        }

        private static void Notes(CleanupPlan plan, DocItem active)
        {
            var cat = plan.Catalog;
            if (cat.TemplateCount > 0) plan.Notes.Add(cat.TemplateCount + " plantilla(s) de vista (view templates): no se tocan.");
            if (cat.PanelScheduleTemplateCount > 0) plan.Notes.Add(cat.PanelScheduleTemplateCount + " plantilla(s) de panel schedule: no se tocan.");
            foreach (var g in cat.Items.Where(i => CleanupOptions.IsExternal(i.Kind) && !plan.Options.Includes(i.Kind)).GroupBy(i => i.Kind))
                plan.Notes.Add(g.Count() + " " + (g.Key == DocKind.RevitLink ? "vínculo(s) de Revit" : g.Key == DocKind.Image ? "imagen(es)" : g.Key == DocKind.CadLink ? "CAD vinculado(s)" : "CAD importado(s)") +
                               " no se tocan: activa la opción correspondiente para eliminarlos.");
            if (cat.Excluded.Count > 0) plan.Notes.Add(cat.Excluded.Count + " vista(s) internas o de sistema no son documentación eliminable: se listan y no se tocan.");
            bool extraProtected = plan.Entries.Any(e => e.Action == PlanAction.Protected && e.Item.Id != cat.ActiveViewId);
            if (active != null && active.PrimaryViewId != 0) plan.Notes.Add("La vista activa es dependiente: se conserva también su vista primaria. " + SingleViewTip);
            else if (active != null && active.Kind == DocKind.Sheet && extraProtected) plan.Notes.Add("La vista activa es un plano: se conservan el plano y su contenido colocado.");
            if (plan.NothingToDelete && string.IsNullOrEmpty(plan.BlockedReason)) plan.Notes.Add("No hay documentación que eliminar: solo quedan la vista activa y lo que se conserva.");
        }

        public static string Sign(CleanupPlan plan)
        {
            var sb = new StringBuilder();
            sb.Append(plan.Catalog.DocumentKey).Append('|').Append(plan.Catalog.ActiveUniqueId).Append('|').Append(plan.Catalog.ActiveViewId.ToString(CultureInfo.InvariantCulture))
              .Append('|').Append(plan.Options?.Key ?? "---");
            foreach (var e in plan.Entries.OrderBy(e => e.Item.UniqueId, StringComparer.Ordinal))
                sb.Append('|').Append(e.Item.UniqueId).Append(':').Append((int)e.Action);
            // Short, stable fingerprint (FNV-1a 64) of the review.
            ulong h = 1469598103934665603UL;
            foreach (char c in sb.ToString()) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16", CultureInfo.InvariantCulture) + "-" + plan.Entries.Count.ToString(CultureInfo.InvariantCulture);
        }
    }
}
