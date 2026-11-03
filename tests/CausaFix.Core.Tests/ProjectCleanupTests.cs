using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.ProjectCleanup.Execution;
using CausaFix.ProjectCleanup.Model;
using CausaFix.ProjectCleanup.Planning;
using CausaFix.ProjectCleanup.Reports;
using CausaFix.ProjectCleanup.ViewModels;

namespace CausaFix.Core.Tests
{
    /// <summary>
    /// Project Cleanup rules with a simulated document. These prove classification, protection, order and the atomic
    /// run logic; Revit's real cascades, transactions and Undo are checked in Revit (docs/PROJECT_CLEANUP.md §5).
    /// </summary>
    public static class ProjectCleanupTests
    {
        // ids: 1 3D, 2 plan (primary), 3 dependent of 2, 4 section, 5 dependent of 6, 6 RCP, 10 sheet A, 11 sheet B,
        //      20 schedule, 21 schedule, 30 legend, 40 panel schedule
        private static DocItem V(long id, string name, DocKind k = DocKind.View, long primary = 0, string type = null)
            => new DocItem { Id = id, UniqueId = "u" + id, Name = name, Kind = k, PrimaryViewId = primary, TypeLabel = type ?? k.ToString() };

        private static CleanupCatalog Doc(long active)
        {
            var c = new CleanupCatalog { DocumentTitle = "Hospital", DocumentKey = "doc-1", ActiveViewId = active, ActiveUniqueId = "u" + active, ActiveName = "n" + active, ActiveTypeLabel = "t" };
            c.Items.Add(V(1, "3D trabajo", type: "Vista 3D"));
            c.Items.Add(V(2, "L01 LIGHTING", type: "Planta"));
            c.Items.Add(V(3, "L01 LIGHTING - Zona A", primary: 2, type: "Planta (dependiente)"));
            c.Items.Add(V(4, "Sección 1", type: "Sección"));
            c.Items.Add(V(5, "L01 RCP - Zona A", primary: 6, type: "Plano de techo (RCP) (dependiente)"));
            c.Items.Add(V(6, "L01 RCP", type: "Plano de techo (RCP)"));
            var a = V(10, "LIGHTING + RCP", DocKind.Sheet); a.SheetNumber = "E-101"; a.PlacedViewIds = new List<long> { 2, 5, 30 }; a.PlacedScheduleIds = new List<long> { 20 }; a.PlacedPanelScheduleIds = new List<long> { 40 }; a.ViewportCount = 3; a.ScheduleInstanceCount = 3; a.OwnedElementCount = 4;
            var b = V(11, "POWER", DocKind.Sheet); b.SheetNumber = "E-201"; b.PlacedViewIds = new List<long> { 4 }; b.PlacedScheduleIds = new List<long> { 20 }; b.ViewportCount = 1; b.ScheduleInstanceCount = 2;
            c.Items.Add(a); c.Items.Add(b);
            c.Items.Add(V(20, "Lighting Fixture Schedule", DocKind.Schedule));
            c.Items.Add(V(21, "Material Takeoff", DocKind.Schedule));
            c.Items.Add(V(30, "Leyenda eléctrica", DocKind.Legend));
            c.Items.Add(V(40, "DB-01", DocKind.PanelSchedule));
            c.TemplateCount = 7;
            c.Excluded.Add(new ExcludedItem { Id = 99, Name = "Project Browser", TypeLabel = "ProjectBrowser", Reason = "sistema" });
            return c;
        }

        private static PlanEntry E(CleanupPlan p, long id) => p.Entries.Single(e => e.Item.Id == id);

        [Test]
        public static void Active3D_DeletesEveryOtherDocument_KeepsOnlyThe3D_SheetsFirst_DependentsBeforePrimaries()
        {
            var p = CleanupPlanner.Build(Doc(1));
            Assert.Equal(PlanAction.Protected, E(p, 1).Action);
            Assert.Equal(1, p.Preserved.Count(), "only the active 3D view");
            Assert.Equal(11, p.DeleteCount);
            Assert.Equal(5, p.Count(DocKind.View)); Assert.Equal(2, p.Count(DocKind.Sheet)); Assert.Equal(2, p.Count(DocKind.Schedule));
            Assert.Equal(1, p.Count(DocKind.Legend)); Assert.Equal(1, p.Count(DocKind.PanelSchedule));
            var order = p.ToDelete.Select(e => e.Item.Id).ToList();
            Assert.True(order.IndexOf(10) < order.IndexOf(2) && order.IndexOf(11) < order.IndexOf(4), "sheets first");
            Assert.True(order.IndexOf(3) < order.IndexOf(2) && order.IndexOf(5) < order.IndexOf(6), "dependents before their primaries");
            Assert.True(p.CanRun);
            Assert.True(p.Notes.Any(n => n.Contains("7 plantilla")), "templates reported, never listed for deletion");
            Assert.False(p.Entries.Any(e => e.Item.Id == 99), "system views are not candidates");
            Assert.True(p.Consequences.Any(c => c.Contains("viewport")));
        }

        [Test]
        public static void ActiveDependent_ProtectsItsPrimary_WithReasonAndTip()
        {
            var p = CleanupPlanner.Build(Doc(3));
            Assert.Equal(PlanAction.Protected, E(p, 3).Action);
            Assert.Equal(PlanAction.Protected, E(p, 2).Action);
            Assert.Contains("primaria de la vista activa", E(p, 2).Reason);
            Assert.True(p.Notes.Any(n => n.Contains(CleanupPlanner.SingleViewTip)));
            Assert.Equal(PlanAction.Delete, E(p, 1).Action);
            Assert.Equal(2, p.Preserved.Count());
        }

        [Test]
        public static void ActiveSheet_KeepsSheetAndPlacedContent_RecursivelyWithPrimaries()
        {
            var p = CleanupPlanner.Build(Doc(10));
            foreach (long id in new long[] { 10, 2, 5, 6, 20, 30, 40 }) Assert.Equal(PlanAction.Protected, E(p, id).Action, "protected " + id);
            Assert.Contains("plano activo", E(p, 2).Reason);
            Assert.Contains("primaria", E(p, 6).Reason);
            foreach (long id in new long[] { 1, 3, 4, 11, 21 }) Assert.Equal(PlanAction.Delete, E(p, id).Action, "deleted " + id);
            // The schedule on both sheets is kept (it is on the active sheet); the other sheet goes with its own placement only.
            Assert.False(p.Consequences.Any(c => c.Contains("E-101") && c.Contains("pierde")), "the active sheet loses nothing");
        }

        [Test]
        public static void ActiveScheduleOrLegend_IsTheViewKept()
        {
            var s = CleanupPlanner.Build(Doc(20));
            Assert.Equal(PlanAction.Protected, E(s, 20).Action);
            Assert.Equal(PlanAction.Delete, E(s, 10).Action, "sheets showing it are deleted; the definition stays");
            Assert.Equal(1, s.Preserved.Count());
            var l = CleanupPlanner.Build(Doc(30));
            Assert.Equal(PlanAction.Protected, E(l, 30).Action);
            Assert.Equal(1, l.Preserved.Count());
        }

        [Test]
        public static void WorksharingAndRevitLimits_KeepWithReason_AndNeverCascadeIntoKept()
        {
            var c = Doc(1);
            c.Item(3).LockReason = "Owned by Ana (worksharing).";              // dependent locked → its primary must stay
            c.Item(11).LockReason = "Owned by Ana (worksharing).";             // locked sheet → what is placed on it stays
            c.Item(21).CanDelete = false; c.Item(21).CannotDeleteReason = "DocumentValidation.CanDeleteElement = false";
            var p = CleanupPlanner.Build(c);
            Assert.Equal(PlanAction.Kept, E(p, 3).Action);
            Assert.Equal(PlanAction.Kept, E(p, 2).Action);
            Assert.Contains("Vista primaria de", E(p, 2).Reason);
            Assert.Equal(PlanAction.Kept, E(p, 11).Action);
            Assert.Equal(PlanAction.Kept, E(p, 4).Action, "placed on the locked sheet");
            Assert.Equal(PlanAction.Kept, E(p, 20).Action, "schedule placed on the locked sheet");
            Assert.Equal(PlanAction.Kept, E(p, 21).Action);
            Assert.Contains("CanDeleteElement", E(p, 21).Reason);
        }

        [Test]
        public static void OnlyTheActiveView_NothingToDelete_AndReadOnlyBlocks()
        {
            var c = new CleanupCatalog { DocumentKey = "d", ActiveViewId = 1, ActiveUniqueId = "u1", ActiveName = "3D" };
            c.Items.Add(V(1, "3D"));
            var p = CleanupPlanner.Build(c);
            Assert.True(p.NothingToDelete);
            Assert.False(p.CanRun);
            Assert.True(p.Notes.Any(n => n.Contains("No hay documentación que eliminar")));
            var ro = Doc(1); ro.IsReadOnly = true;
            var q = CleanupPlanner.Build(ro);
            Assert.False(q.CanRun);
            Assert.Contains("solo lectura", q.BlockedReason);
        }

        [Test]
        public static void Signature_IdentifiesTheReview()
        {
            string a = CleanupPlanner.Build(Doc(1)).Signature;
            Assert.Equal(a, CleanupPlanner.Build(Doc(1)).Signature, "stable for the same document and active view");
            Assert.False(a == CleanupPlanner.Build(Doc(4)).Signature, "another active view → another review");
            var c = Doc(1); c.Items.Add(V(50, "Nueva vista"));
            Assert.False(a == CleanupPlanner.Build(c).Signature, "new documentation → another review");
            var d = Doc(1); d.DocumentKey = "doc-2";
            Assert.False(a == CleanupPlanner.Build(d).Signature, "another document → another review");
        }

        // ------------------------------------------------------------------ atomic run over a simulated document

        private sealed class Host : IDeleteHost
        {
            public readonly HashSet<long> Alive;
            public readonly Dictionary<long, long[]> Cascade = new Dictionary<long, long[]>();
            public readonly List<long> Calls = new List<long>();
            public long ThrowOn = -1;
            public Host(IEnumerable<long> alive) { Alive = new HashSet<long>(alive); }
            public bool Exists(long id) => Alive.Contains(id);
            public ICollection<long> Delete(long id)
            {
                if (!Alive.Contains(id)) throw new InvalidOperationException("The element does not exist (double delete)");
                Calls.Add(id);
                if (id == ThrowOn) throw new InvalidOperationException("Revit: the element cannot be deleted");
                var all = new List<long> { id };
                if (Cascade.TryGetValue(id, out var more)) all.AddRange(more.Where(Alive.Contains));
                foreach (var x in all) Alive.Remove(x);
                return all;
            }
        }

        private static Host HostFor(CleanupPlan p)
        {
            var h = new Host(p.Entries.Select(e => e.Item.Id).Concat(new long[] { 100, 101, 200, 201, 202, 500, 900 }));
            h.Cascade[2] = new long[] { 3, 100 };          // primary takes its dependent and an annotation
            h.Cascade[10] = new long[] { 200, 201, 202 };  // sheet takes its viewports / placements
            h.Cascade[6] = new long[] { 5, 101 };
            return h;
        }

        private static readonly Dictionary<long, string> Documentary = new Dictionary<long, string> { { 100, "Anotaciones" }, { 101, "Anotaciones" }, { 200, "Viewports" }, { 201, "Viewports" }, { 202, "Colocaciones de schedules" } };

        [Test]
        public static void Run_NoDoubleDeletes_CountsDependents_ProtectedSurvive()
        {
            var p = CleanupPlanner.Build(Doc(1));
            var h = HostFor(p);
            // Order forces dependents first: make the primary go first to prove an already-deleted id is never deleted again.
            var res = new CleanupResult();
            var run = CleanupRunner.Run(p, h, new Dictionary<long, string> { { 900, "Nivel 'L01'" } }, Documentary, res);
            Assert.True(run.Ok, run.Failure);
            Assert.Equal(h.Calls.Count, h.Calls.Distinct().Count(), "never deleted twice");
            Assert.True(h.Alive.Contains(1), "active view still there");
            Assert.True(h.Alive.Contains(900), "model element untouched");
            Assert.Equal(2, res.Dependents["Viewports"]);
            Assert.Equal(11, res.DeletedTotal);
            Assert.Equal(5, res.Deleted(DocKind.View));
        }

        [Test]
        public static void Run_PrimaryFirst_DependentIsDeletedWithParent_NotAgain()
        {
            var c = new CleanupCatalog { DocumentKey = "d", ActiveViewId = 1, ActiveUniqueId = "u1" };
            c.Items.Add(V(1, "3D")); c.Items.Add(V(2, "P")); c.Items.Add(V(3, "D", primary: 2));
            var p = CleanupPlanner.Build(c);
            // Simulate a plan where the dependent is reached after its primary (e.g. ordering by another rule).
            var e2 = E(p, 2); var e3 = E(p, 3); int o = e2.Order; e2.Order = e3.Order; e3.Order = o;
            var h = new Host(new long[] { 1, 2, 3 }); h.Cascade[2] = new long[] { 3 };
            var res = new CleanupResult();
            Assert.True(CleanupRunner.Run(p, h, null, null, res).Ok);
            Assert.Equal(ItemOutcome.DeletedWithParent, res.Items.Single(i => i.Id == 3).Outcome);
            Assert.False(h.Calls.Contains(3));
        }

        [Test]
        public static void Run_StopsOnProtectedOrOutOfScopeCascade_OrRevitError_AndRollbackClaimsNothing()
        {
            var p = CleanupPlanner.Build(Doc(1));
            var h = HostFor(p); h.Cascade[4] = new long[] { 1 };   // deleting a section would remove the active view
            var res = new CleanupResult();
            var run = CleanupRunner.Run(p, h, null, Documentary, res);
            Assert.False(run.Ok);
            Assert.Contains("protegido", run.Failure);
            res.MarkRolledBack(run.Failure);
            Assert.Equal(0, res.DeletedTotal, "after rollback nothing is reported as deleted");
            Assert.Equal(0, res.DependentTotal);

            h = HostFor(p); h.Cascade[21] = new long[] { 900 };
            res = new CleanupResult();
            run = CleanupRunner.Run(p, h, new Dictionary<long, string> { { 900, "Nivel 'L01'" } }, Documentary, res);
            Assert.False(run.Ok);
            Assert.Contains("fuera del alcance (Nivel 'L01'", run.Failure);

            h = HostFor(p); h.ThrowOn = 30;
            res = new CleanupResult();
            run = CleanupRunner.Run(p, h, null, Documentary, res);
            Assert.False(run.Ok);
            Assert.Contains("No se pudo eliminar", run.Failure);
            Assert.Equal(ItemOutcome.Failed, res.Items.Single(i => i.Id == 30).Outcome);
        }

        // ------------------------------------------------------------------ view model

        private sealed class Gateway : ICleanupGateway
        {
            public CleanupCatalog Catalog = Doc(1);
            public CleanupCatalog Next;     // what Revit shows at Execute (null = unchanged)
            public int Executions, Analyses;
            public string LastSignature;
            public Task<CleanupCatalog> Analyze() { Analyses++; return Task.FromResult(Catalog); }
            public Task<CleanupResult> Execute(string sig, CleanupOptions options)
            {
                Executions++; LastSignature = sig;
                var live = Next ?? Catalog;
                var plan = CleanupPlanner.Build(live, options);
                if (plan.Signature != sig) { Catalog = live; return Task.FromResult(new CleanupResult { Stale = true, NewCatalog = live }); }
                var res = new CleanupResult { DocumentTitle = live.DocumentTitle, KeptView = live.ActiveName };
                var run = CleanupRunner.Run(plan, HostFor(plan), null, Documentary, res);
                res.Committed = run.Ok; res.Summary = run.Ok ? "ok" : run.Failure;
                var after = new CleanupCatalog { DocumentKey = live.DocumentKey, ActiveViewId = live.ActiveViewId, ActiveUniqueId = live.ActiveUniqueId, ActiveName = live.ActiveName };
                after.Items.Add(live.Item(live.ActiveViewId));
                Catalog = after;
                return Task.FromResult(res);
            }
        }

        private sealed class Dialogs : ICleanupDialogs
        {
            public List<string> Messages = new List<string>();
            public void Info(string t, string m) => Messages.Add(m);
            public void Error(string t, string m) => Messages.Add("ERROR " + m);
            public void OpenWithShell(string p) { }
        }

        [Test]
        public static void ViewModel_ReviewThenOneConfirmation_StaleReviewIsRebuilt_CancelChangesNothing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-pc-" + Guid.NewGuid().ToString("N"));
            try
            {
                var gw = new Gateway();
                var vm = new ProjectCleanupViewModel(gw, new Dialogs(), dir);
                vm.InitializeAsync().GetAwaiter().GetResult();
                Assert.Equal("Se conservará: n1 — t", vm.KeptView);
                Assert.Contains("Se eliminarán: 5 vista(s), 2 plano(s), 2 schedule(s), 1 leyenda(s), 1 panel schedule(s)", vm.CountsText);
                Assert.True(vm.DeleteCommand.CanExecute(null));
                Assert.Equal(0, gw.Executions, "analysing changes nothing");

                bool closed = false;
                vm.CloseRequested += (s, e) => closed = true;
                vm.CancelCommand.Execute(null);
                Assert.True(closed);
                Assert.Equal(0, gw.Executions, "cancel changes nothing");

                // The user switched to another view before confirming: nothing is deleted, the new review must be confirmed.
                gw.Next = Doc(4);
                vm.DeleteCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.True(vm.StaleNotice);
                Assert.False(vm.HasResult, "no deletion with the old review");
                Assert.Equal("Se conservará: n4 — t", vm.KeptView);
                Assert.Contains("cambiaron", vm.Status);

                gw.Next = null;
                vm.DeleteCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.True(vm.HasResult && vm.Result.Committed, vm.Status);
                Assert.True(File.Exists(vm.ReportPath), "report written");
                string csv = File.ReadAllText(vm.ReportPath);
                Assert.Contains("CausaFixIt Project Cleanup", csv);
                Assert.Contains("Eliminada", csv);
                Assert.Equal("No hay documentación que eliminar.", vm.CountsText, "re-analysed after the run");
                Assert.False(vm.DeleteCommand.CanExecute(null));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}

namespace CausaFix.Core.Tests
{
    using System.Linq;
    using CausaFix.ProjectCleanup.Execution;
    using CausaFix.ProjectCleanup.Model;
    using CausaFix.ProjectCleanup.Planning;

    /// <summary>Optional deletion of CAD (linked/imported), Revit links and images.</summary>
    public static class ProjectCleanupExternalTests
    {
        private static CleanupCatalog Doc()
        {
            var c = new CleanupCatalog { DocumentKey = "d", ActiveViewId = 1, ActiveUniqueId = "u1", ActiveName = "3D" };
            c.Items.Add(new DocItem { Id = 1, UniqueId = "u1", Name = "3D", Kind = DocKind.View });
            c.Items.Add(new DocItem { Id = 2, UniqueId = "u2", Name = "L01", Kind = DocKind.View });
            c.Items.Add(new DocItem { Id = 60, UniqueId = "u60", Name = "Base.dwg", Kind = DocKind.CadLink, InstanceIds = { 61, 62 }, InstanceOwnerViewIds = { 2 } });
            c.Items.Add(new DocItem { Id = 63, UniqueId = "u63", Name = "Detalle.dwg", Kind = DocKind.CadImport, InstanceIds = { 64 }, InstanceOwnerViewIds = { 1 } });   // in the active view
            c.Items.Add(new DocItem { Id = 70, UniqueId = "u70", Name = "ARQ.rvt", Kind = DocKind.RevitLink, InstanceIds = { 71, 72, 73 } });                            // 72 = nested type, 73 its instance
            c.Items.Add(new DocItem { Id = 80, UniqueId = "u80", Name = "Logo.png", Kind = DocKind.Image, InstanceIds = { 81 }, InstanceOwnerViewIds = { 2 } });
            return c;
        }

        [Test]
        public static void OffByDefault_NotTouched_AndReported()
        {
            var p = CleanupPlanner.Build(Doc());
            Assert.False(p.Entries.Any(e => CleanupOptions.IsExternal(e.Item.Kind)), "CAD, links and images are not in the review unless asked");
            Assert.Equal(1, p.DeleteCount);
            Assert.True(p.Notes.Any(n => n.Contains("vínculo(s) de Revit no se tocan")));
            Assert.True(p.Notes.Any(n => n.Contains("imagen(es) no se tocan")));
        }

        [Test]
        public static void Options_AddThemToTheReview_InstanceInKeptViewProtects_SignatureChanges()
        {
            var off = CleanupPlanner.Build(Doc());
            var on = CleanupPlanner.Build(Doc(), new CleanupOptions { DeleteCad = true, DeleteRevitLinks = true, DeleteImages = true });
            Assert.False(off.Signature == on.Signature, "options are part of the reviewed scope");
            Assert.Equal(PlanAction.Delete, on.Entries.Single(e => e.Item.Id == 60).Action);
            Assert.Equal(PlanAction.Protected, on.Entries.Single(e => e.Item.Id == 63).Action, "imported CAD placed in the active view stays");
            Assert.Contains("vista activa", on.Entries.Single(e => e.Item.Id == 63).Reason);
            Assert.Equal(PlanAction.Delete, on.Entries.Single(e => e.Item.Id == 70).Action);
            Assert.Equal(PlanAction.Delete, on.Entries.Single(e => e.Item.Id == 80).Action);
            var order = on.ToDelete.Select(e => e.Item.Id).ToList();
            Assert.True(order.IndexOf(2) < order.IndexOf(80) && order.IndexOf(80) < order.IndexOf(60) && order.IndexOf(60) < order.IndexOf(70), "views first, then images, CAD, links");
            Assert.True(on.Consequences.Any(c => c.Contains("instancia(s) de CAD, vínculos o imágenes")));
            Assert.True(on.Consequences.Any(c => c.Contains("Copy/Monitor")));
            var only = CleanupPlanner.Build(Doc(), new CleanupOptions { DeleteImages = true });
            Assert.True(only.Entries.Any(e => e.Item.Kind == DocKind.Image) && !only.Entries.Any(e => e.Item.Kind == DocKind.RevitLink || e.Item.Kind == DocKind.CadLink), "each option is independent");
        }

        [Test]
        public static void ViewModel_TogglingAnOption_RebuildsTheReview_AndExecuteUsesIt()
        {
            var gw = new Gw();
            var vm = new CausaFix.ProjectCleanup.ViewModels.ProjectCleanupViewModel(gw, new NoDialogs(), System.IO.Path.GetTempPath());
            vm.InitializeAsync().GetAwaiter().GetResult();
            Assert.Contains("Se eliminarán: 1 vista(s)", vm.CountsText);
            vm.DeleteRevitLinks = true;
            Assert.Contains("1 vínculo(s) de Revit", vm.CountsText);
            Assert.Equal("(1 en el modelo)", vm.LinksAvailable);
            vm.DeleteCommand.ExecuteAsync().GetAwaiter().GetResult();
            Assert.True(gw.LastOptions != null && gw.LastOptions.DeleteRevitLinks && !gw.LastOptions.DeleteCad, "the reviewed options reach Revit");
            Assert.Equal(CleanupPlanner.Build(Doc(), gw.LastOptions).Signature, gw.LastSignature);
        }

        private sealed class Gw : ICleanupGateway
        {
            public CleanupOptions LastOptions; public string LastSignature;
            public System.Threading.Tasks.Task<CleanupCatalog> Analyze() => System.Threading.Tasks.Task.FromResult(Doc());
            public System.Threading.Tasks.Task<CleanupResult> Execute(string sig, CleanupOptions o)
            {
                LastOptions = o; LastSignature = sig;
                return System.Threading.Tasks.Task.FromResult(new CleanupResult { Committed = true, Summary = "ok" });
            }
        }

        private sealed class NoDialogs : CausaFix.ProjectCleanup.ViewModels.ICleanupDialogs
        {
            public void Info(string t, string m) { }
            public void Error(string t, string m) { }
            public void OpenWithShell(string p) { }
        }
    }
}
