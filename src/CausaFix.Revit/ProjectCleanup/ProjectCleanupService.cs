using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using CausaFix.ProjectCleanup.Execution;
using CausaFix.ProjectCleanup.Model;
using CausaFix.ProjectCleanup.Planning;

namespace CausaFix.Revit.ProjectCleanup
{
    /// <summary>
    /// Project Cleanup gateway: every Revit access through the ExternalEvent dispatcher. Analysis is read-only. Execution
    /// re-reads the active document and runs only the reviewed plan (same signature), atomically: one TransactionGroup
    /// with one Transaction, Document.Delete per planned element, protected and out-of-scope checks on every id Revit
    /// returns, verification of the protected elements before committing, and a single Undo step. Never saves or syncs.
    /// </summary>
    public sealed class ProjectCleanupService : ICleanupGateway, IDisposable
    {
        private readonly RevitDispatcher _dispatcher;
        private readonly RevitSession _session = new RevitSession();

        public ProjectCleanupService(RevitDispatcher dispatcher) { _dispatcher = dispatcher; }

        /// <summary>Optional trace (set by the add-in) for the CausaFixIt log.</summary>
        public Action<string> Trace { get; set; }

        public Task<CleanupCatalog> Analyze() => _dispatcher.InvokeAsync(app =>
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc?.Document == null) throw new DocumentChangedException("No hay ningún documento abierto en Revit.");
            _session.Bind(uidoc.Document);                         // the cleanup always works on the active document
            return CleanupCatalogReader.Read(uidoc);
        });

        public Task<CleanupResult> Execute(string approvedSignature, CleanupOptions options) => _dispatcher.InvokeAsync(app =>
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc?.Document == null) throw new DocumentChangedException("No hay ningún documento abierto en Revit.");
            Document doc = uidoc.Document;
            bool sameDocument = _session.IsBound && DocumentIdentityReader.SessionKey(doc) == _session.DocumentKey;

            // Never trust an old review: rebuild it from the live document and compare.
            var cat = CleanupCatalogReader.Read(uidoc);
            var plan = CleanupPlanner.Build(cat, options);
            if (!sameDocument || plan.Signature != approvedSignature)
            {
                _session.Bind(doc);
                return new CleanupResult { Stale = true, NewCatalog = cat, DocumentTitle = cat.DocumentTitle };
            }

            var result = new CleanupResult
            {
                DocumentTitle = cat.DocumentTitle,
                KeptView = cat.ActiveName + " — " + cat.ActiveTypeLabel,
                RevitVersion = Safe(() => app.Application.VersionNumber + " (" + app.Application.VersionBuild + ")"),
                AddinBuild = BuildInfo.Text,
                StartedUtc = DateTime.UtcNow
            };
            if (!plan.CanRun)
            {
                result.Failure = string.IsNullOrEmpty(plan.BlockedReason) ? "No hay documentación que eliminar." : plan.BlockedReason;
                result.Summary = result.Failure;
                return result;
            }
            if (doc.IsModifiable) { result.Failure = "Hay otra transacción abierta en Revit: termínala antes de limpiar."; result.Summary = result.Failure; return result; }

            var toDelete = new HashSet<long>(plan.ToDelete.Select(e => e.Item.Id));
            CloseOpenViews(uidoc, toDelete, cat.ActiveViewId, result);
            // CAD, links and images approved for deletion (types, instances, nested links) are in scope; everything else stays guarded.
            var allowed = new HashSet<long>(plan.ToDelete.Where(e => CleanupOptions.IsExternal(e.Item.Kind)).SelectMany(e => e.Item.InstanceIds.Concat(new[] { e.Item.Id })));
            var outOfScope = OutOfScope(doc);
            foreach (long id in allowed) outOfScope.Remove(id);
            var documentary = Documentary(doc);
            Trace?.Invoke("Project Cleanup: run " + plan.Signature + " – " + toDelete.Count + " to delete, " + outOfScope.Count + " model elements guarded");

            var collector = new FailureCollector { DismissWarnings = true };
            using (var group = new TransactionGroup(doc, "CausaFixIt Project Cleanup"))
            {
                group.Start();
                bool commitGroup = false;
                using (var t = new Transaction(doc, "CausaFixIt Project Cleanup: eliminar documentación"))
                {
                    FailureHandlingOptions fho = t.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(collector);
                    fho.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(fho);
                    t.Start();
                    CleanupRunner.RunOutcome run;
                    try { run = CleanupRunner.Run(plan, new DeleteHost(doc), outOfScope, documentary, result); }
                    catch (Exception ex) { run = new CleanupRunner.RunOutcome { Failure = "Error inesperado: " + ex.GetType().Name + ": " + ex.Message }; }

                    if (!run.Ok)
                    {
                        t.RollBack();
                        result.TransactionStatus = "RolledBack";
                        result.MarkRolledBack(run.Failure);
                    }
                    else
                    {
                        TransactionStatus status = t.Commit();
                        result.TransactionStatus = status + (collector.HadErrors ? " (Revit informó errores)" : "");
                        if (status != TransactionStatus.Committed || collector.HadErrors)
                            result.MarkRolledBack("Revit revirtió la transacción (" + status + ")" + (collector.Messages.Count > 0 ? ": " + string.Join(" | ", collector.Messages) : "."));
                        else
                        {
                            // After commit, before closing the group: the active view and every protected element must exist.
                            var missing = plan.ProtectedIds.Where(id => doc.GetElement(RevitCompat.Id(id)) == null).ToList();
                            if (missing.Count > 0) result.MarkRolledBack("Tras el commit faltan " + missing.Count + " elemento(s) protegido(s) (ids " + string.Join(", ", missing.Take(5)) + "): se revierte todo.");
                            else commitGroup = true;
                        }
                    }
                }
                if (commitGroup)
                {
                    TransactionStatus gs = group.Assimilate();
                    result.GroupStatus = gs.ToString();
                    if (gs == TransactionStatus.Committed) result.Committed = true;
                    else result.MarkRolledBack("El TransactionGroup no se confirmó (" + gs + ").");
                }
                else result.GroupStatus = group.RollBack().ToString();
            }
            result.RevitMessages.AddRange(collector.Messages);
            result.FinishedUtc = DateTime.UtcNow;
            var kept = result.Items.Where(i => i.Outcome == ItemOutcome.Preserved && i.Id != cat.ActiveViewId).ToList();
            result.Summary = result.Committed
                ? "Eliminado: " + result.Deleted(DocKind.View) + " vista(s), " + result.Deleted(DocKind.Sheet) + " plano(s), " + result.Deleted(DocKind.Schedule) + " schedule(s), " +
                  result.Deleted(DocKind.Legend) + " leyenda(s)" + (result.Deleted(DocKind.PanelSchedule) > 0 ? ", " + result.Deleted(DocKind.PanelSchedule) + " panel schedule(s)" : "") +
                  (result.Deleted(DocKind.CadLink) + result.Deleted(DocKind.CadImport) > 0 ? ", " + (result.Deleted(DocKind.CadLink) + result.Deleted(DocKind.CadImport)) + " CAD" : "") +
                  (result.Deleted(DocKind.RevitLink) > 0 ? ", " + result.Deleted(DocKind.RevitLink) + " vínculo(s) de Revit" : "") +
                  (result.Deleted(DocKind.Image) > 0 ? ", " + result.Deleted(DocKind.Image) + " imagen(es)" : "") +
                  " y " + result.DependentTotal + " elemento(s) dependientes. Se conservó " + result.KeptView +
                  (kept.Count == 0 ? "." : " y " + kept.Count + " elemento(s) más: " + string.Join(", ", kept.Take(8).Select(k => k.Name)) + (kept.Count > 8 ? "…" : "") + ".") +
                  " Un solo Deshacer en Revit lo revierte. El modelo no se ha guardado."
                : "No se aplicó la limpieza: no se eliminó nada. " + result.Failure;
            Trace?.Invoke("Project Cleanup: " + (result.Committed ? "committed" : "rolled back") + " – " + result.Summary);
            return result;
        });

        /// <summary>Open windows of views about to be deleted are closed first (UI only; the active view stays open).</summary>
        private static void CloseOpenViews(UIDocument uidoc, HashSet<long> toDelete, long activeId, CleanupResult result)
        {
            IList<UIView> open;
            try { open = uidoc.GetOpenUIViews(); } catch (Exception) { return; }
            foreach (UIView uv in open.ToList())
            {
                long id = RevitCompat.Value(uv.ViewId);
                if (id == activeId || !toDelete.Contains(id)) continue;
                string name = uidoc.Document.GetElement(uv.ViewId)?.Name ?? id.ToString();
                try { uv.Close(); result.ClosedViews.Add(name); }
                catch (Exception ex) { result.ClosedViews.Add(name + " (no se pudo cerrar: " + ex.Message + ")"); }
            }
        }

        private static readonly HashSet<string> DocumentaryCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "OST_Viewers", "OST_Cameras", "OST_Views", "OST_Sheets", "OST_Schedules", "OST_ElevationMarks", "OST_Elev", "OST_Sections",
            "OST_Callouts", "OST_ReferenceViewer", "OST_SectionBox", "OST_CropBoundary", "OST_Areas", "OST_AreaSchemeLines", "OST_Matchline", "OST_VolumeOfInterest"
        };

        private static string CategoryName(Category c)
        {
            if (c == null) return string.Empty;
            long v = RevitCompat.Value(c.Id);
            return v < int.MinValue || v > int.MaxValue ? string.Empty : ((BuiltInCategory)(int)v).ToString();
        }

        /// <summary>
        /// Model elements outside the scope: model-category elements not owned by a view, levels, grids, reference planes,
        /// links, electrical circuits, families, types and materials. Deleting documentation must never remove one of them.
        /// Lines are not guarded: model lines and detail lines that Revit removes together with a view, a sheet or a CAD
        /// are deleted as well (they are drawing content, not building elements) and are counted in the result.
        /// </summary>
        private static Dictionary<long, string> OutOfScope(Document doc)
        {
            var d = new Dictionary<long, string>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (e is View || e is ElevationMarker || e is Viewport || e is ScheduleSheetInstance || e is PanelScheduleSheetInstance) continue;
                ElementId ov;
                try { ov = e.OwnerViewId; } catch (Exception) { continue; }
                if (RevitCompat.IsValid(ov)) continue;                                   // view-owned: documentary
                if (IsLine(e)) continue;                                                 // model/detail lines: always deletable
                string label = null;
                if (e is Level) label = "Nivel '" + e.Name + "'";
                else if (e is Grid) label = "Eje '" + e.Name + "'";
                else if (e is ReferencePlane) label = "Plano de referencia";
                else if (e is RevitLinkInstance) label = "Vínculo '" + e.Name + "'";
                else if (e is ElectricalSystem) label = "Circuito '" + e.Name + "'";
                else if (e.Category != null && e.Category.CategoryType == CategoryType.Model && !DocumentaryCategories.Contains(CategoryName(e.Category)))
                    label = e.Category.Name + " '" + e.Name + "'";
                if (label != null) d[RevitCompat.Value(e.Id)] = label;
            }
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsElementType()) d[RevitCompat.Value(e.Id)] = "Tipo '" + e.Name + "'";
            foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(Family))) d[RevitCompat.Value(e.Id)] = "Familia '" + e.Name + "'";
            foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(Material))) d[RevitCompat.Value(e.Id)] = "Material '" + e.Name + "'";
            return d;
        }

        /// <summary>Known documentary dependents, by kind, for the result (viewports, placements, markers, annotations…).</summary>
        private static Dictionary<long, string> Documentary(Document doc)
        {
            var d = new Dictionary<long, string>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string kind = null;
                if (IsLine(e)) kind = "Líneas (de modelo y de detalle)";
                else if (e is Viewport) kind = "Viewports";
                else if (e is ImportInstance) kind = "Instancias de CAD";
                else if (e is RevitLinkInstance) kind = "Instancias de vínculos Revit";
                else if (e is ImageInstance) kind = "Instancias de imagen";
                else if (e is ScheduleSheetInstance) kind = "Colocaciones de schedules";
                else if (e is PanelScheduleSheetInstance) kind = "Colocaciones de panel schedules";
                else if (e is ElevationMarker) kind = "Marcadores de alzado";
                else if (e is View) kind = "Vistas dependientes / asociadas";
                else
                {
                    string c = CategoryName(e.Category);
                    if (c == "OST_Areas") kind = "Áreas";
                    else if (c == "OST_TitleBlocks") kind = "Cajetines de los planos";
                    else if (c == "OST_Viewers" || c == "OST_Cameras" || c == "OST_Elev" || c == "OST_Sections" || c == "OST_Callouts") kind = "Marcadores y cámaras de vista";
                    else
                    {
                        ElementId ov;
                        try { ov = e.OwnerViewId; } catch (Exception) { continue; }
                        if (RevitCompat.IsValid(ov)) kind = "Anotaciones y detalles propios de las vistas";
                    }
                }
                if (kind != null) d[RevitCompat.Value(e.Id)] = kind;
            }
            foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType))) d[RevitCompat.Value(e.Id)] = "Vínculos Revit anidados";
            return d;
        }

        /// <summary>Model lines and detail lines (any line style), including those created from CAD or sketches of views.</summary>
        private static bool IsLine(Element e) => e is ModelCurve || e is DetailCurve;

        private static string Safe(Func<string> f) { try { return f() ?? ""; } catch (Exception) { return ""; } }

        private sealed class DeleteHost : IDeleteHost
        {
            private readonly Document _doc;
            public DeleteHost(Document doc) { _doc = doc; }
            public bool Exists(long id) => _doc.GetElement(RevitCompat.Id(id)) != null;
            public ICollection<long> Delete(long id) => _doc.Delete(RevitCompat.Id(id)).Select(RevitCompat.Value).ToList();
        }

        public void Dispose() => _dispatcher.Dispose();
    }
}
