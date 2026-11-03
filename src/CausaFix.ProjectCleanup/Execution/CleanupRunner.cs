using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.ProjectCleanup.Model;
using CausaFix.ProjectCleanup.Planning;

namespace CausaFix.ProjectCleanup.Execution
{
    /// <summary>The document inside an open transaction (implemented over Document.Delete; simulated in tests).</summary>
    public interface IDeleteHost
    {
        bool Exists(long id);
        /// <summary>Document.Delete(id): every id Revit removed (the element and its dependents). Revit errors propagate.</summary>
        ICollection<long> Delete(long id);
    }

    public enum ItemOutcome
    {
        NotAttempted = 0,
        Deleted,
        /// <summary>Removed by Revit together with another deleted element (e.g. a dependent view with its primary).</summary>
        DeletedWithParent,
        Failed,
        /// <summary>Was deleted inside the transaction, which was then rolled back: NOT deleted.</summary>
        RolledBack,
        Preserved
    }

    public sealed class ItemResult
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DocKind Kind { get; set; }
        public string TypeLabel { get; set; } = string.Empty;
        public string SheetNumber { get; set; } = string.Empty;
        public ItemOutcome Outcome { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class CleanupResult
    {
        /// <summary>The document or the active view changed since the review: nothing was done, the new analysis is attached.</summary>
        public bool Stale { get; set; }
        public CleanupCatalog NewCatalog { get; set; }
        public bool Committed { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string Failure { get; set; } = string.Empty;
        public string TransactionStatus { get; set; } = string.Empty;
        public string GroupStatus { get; set; } = string.Empty;
        public string RevitVersion { get; set; } = string.Empty;
        public string AddinBuild { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty;
        public string KeptView { get; set; } = string.Empty;
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;
        public List<ItemResult> Items { get; } = new List<ItemResult>();
        /// <summary>Dependent elements Revit deleted with the requested ones, by kind (viewports, annotations…).</summary>
        public Dictionary<string, int> Dependents { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> RevitMessages { get; } = new List<string>();
        public List<string> ClosedViews { get; } = new List<string>();

        public int Deleted(DocKind k) => Items.Count(i => i.Kind == k && (i.Outcome == ItemOutcome.Deleted || i.Outcome == ItemOutcome.DeletedWithParent));
        public int DeletedTotal => Items.Count(i => i.Outcome == ItemOutcome.Deleted || i.Outcome == ItemOutcome.DeletedWithParent);
        public int DependentTotal => Dependents.Values.Sum();

        /// <summary>The transaction did not commit: nothing reported as deleted is really deleted.</summary>
        public void MarkRolledBack(string reason)
        {
            Committed = false;
            foreach (var i in Items)
                if (i.Outcome == ItemOutcome.Deleted || i.Outcome == ItemOutcome.DeletedWithParent) { i.Outcome = ItemOutcome.RolledBack; i.Message = reason; }
            Dependents.Clear();
            if (string.IsNullOrEmpty(Failure)) Failure = reason;
        }
    }

    /// <summary>Everything Project Cleanup needs from Revit (DTOs only, through ExternalEvent).</summary>
    public interface ICleanupGateway
    {
        /// <summary>Binds the active document and reads its documentation (read-only).</summary>
        Task<CleanupCatalog> Analyze();
        /// <summary>
        /// Re-reads the active document, rebuilds the plan and runs it only when its signature equals
        /// <paramref name="approvedSignature"/>; otherwise returns Stale with the new catalog and changes nothing.
        /// </summary>
        Task<CleanupResult> Execute(string approvedSignature);
    }

    /// <summary>
    /// Runs an approved plan inside the caller's transaction. Ids are materialised and de-duplicated in the plan;
    /// elements already gone (removed with a parent) are not deleted again; every id Revit returns is checked:
    /// a protected element or a model element outside the scope aborts the run (the caller rolls back). After the
    /// deletes, every protected element must still exist.
    /// </summary>
    public static class CleanupRunner
    {
        public sealed class RunOutcome
        {
            public bool Ok { get; set; }
            public string Failure { get; set; } = string.Empty;
        }

        /// <param name="outOfScope">Model elements that must never be deleted (levels, grids, model elements, families, links, circuits…) with a label.</param>
        /// <param name="documentary">Known documentary dependents (viewport, schedule placement, annotation…) with their kind, for the result.</param>
        public static RunOutcome Run(CleanupPlan plan, IDeleteHost host, IDictionary<long, string> outOfScope, IDictionary<long, string> documentary, CleanupResult result)
        {
            var protectedIds = plan.ProtectedIds;
            var names = plan.Entries.ToDictionary(e => e.Item.Id, e => e.Item.Display);
            var requested = new HashSet<long>(plan.ToDelete.Select(e => e.Item.Id));
            var gone = new HashSet<long>();
            var itemById = new Dictionary<long, ItemResult>();
            foreach (var e in plan.Entries)
            {
                var r = new ItemResult { Id = e.Item.Id, Name = e.Item.Name, Kind = e.Item.Kind, TypeLabel = e.Item.TypeLabel, SheetNumber = e.Item.SheetNumber,
                                         Outcome = e.Action == PlanAction.Delete ? ItemOutcome.NotAttempted : ItemOutcome.Preserved, Message = e.Action == PlanAction.Delete ? "" : e.Reason };
                result.Items.Add(r);
                itemById[e.Item.Id] = r;
            }

            foreach (var e in plan.ToDelete)
            {
                var r = itemById[e.Item.Id];
                if (gone.Contains(e.Item.Id) || !host.Exists(e.Item.Id))
                {
                    r.Outcome = ItemOutcome.DeletedWithParent;
                    r.Message = "Eliminada por Revit junto con otro elemento eliminado.";
                    continue;
                }
                ICollection<long> deleted;
                try { deleted = host.Delete(e.Item.Id); }
                catch (Exception ex)
                {
                    r.Outcome = ItemOutcome.Failed;
                    r.Message = ex.GetType().Name + ": " + ex.Message;
                    return new RunOutcome { Failure = "No se pudo eliminar '" + e.Item.Display + "' (" + e.Item.TypeLabel + "): " + ex.Message };
                }
                r.Outcome = ItemOutcome.Deleted;
                foreach (long d in deleted ?? new List<long>())
                {
                    if (d == e.Item.Id) continue;
                    if (protectedIds.Contains(d))
                    {
                        r.Outcome = ItemOutcome.Failed;
                        return new RunOutcome { Failure = "Eliminar '" + e.Item.Display + "' eliminaría también el elemento protegido '" + (names.TryGetValue(d, out var n) ? n : d.ToString(CultureInfo.InvariantCulture)) + "'. No se aplicó nada." };
                    }
                    if (outOfScope != null && outOfScope.TryGetValue(d, out string label))
                    {
                        r.Outcome = ItemOutcome.Failed;
                        return new RunOutcome { Failure = "Eliminar '" + e.Item.Display + "' eliminaría un elemento fuera del alcance (" + label + " [" + d.ToString(CultureInfo.InvariantCulture) + "]). No se aplicó nada." };
                    }
                    if (!gone.Add(d)) continue;
                    if (requested.Contains(d)) continue;              // a planned item removed with its parent: counted when its turn comes
                    string kind = documentary != null && documentary.TryGetValue(d, out string k) ? k : "Otros elementos internos de las vistas";
                    result.Dependents[kind] = result.Dependents.TryGetValue(kind, out int c) ? c + 1 : 1;
                }
                gone.Add(e.Item.Id);
            }

            foreach (long id in protectedIds)
                if (!host.Exists(id))
                    return new RunOutcome { Failure = "El elemento protegido '" + (names.TryGetValue(id, out var n) ? n : id.ToString(CultureInfo.InvariantCulture)) + "' ya no existe tras el borrado. No se aplicó nada." };
            return new RunOutcome { Ok = true };
        }
    }
}
