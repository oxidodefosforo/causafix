using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Planning;
using CausaFix.ProjectSetup.Profiles;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Core.Tests
{
    /// <summary>In-memory stand-in for the Revit side of Project Setup (a mock: it does not prove the API calls).</summary>
    internal sealed class FakeSetupGateway : IProjectSetupGateway
    {
        public SetupCatalog Catalog = SetupData.Catalog(2);
        public int Runs;
        public bool FailOnPlacement;
        public string DocumentKey => SetupData.Doc;

        public Task<string> BindActiveDocument() => Task.FromResult(Catalog.DocumentTitle);
        public Task<SetupCatalog> ReadCatalog() => Task.FromResult(SetupJsonCopy(Catalog));

        public Task<AnchorPick> PickGridIntersection(bool second) => Task.FromResult(new AnchorPick
        {
            Ok = true, Message = "Grid A / Grid 1", Anchor = new ModelAnchor { IsSet = true, X = second ? 60 : 10, Y = 20, Description = "Grid A / Grid 1" }
        });

        public Task<AnchorPick> PickPoint(bool second) => Task.FromResult(new AnchorPick { Ok = false, Message = "The active view has no work plane." });

        public Task<ReferencePick> PickReferenceViewport(ModelAnchor anchor, AlignmentMethod method) => Task.FromResult(new ReferencePick
        {
            Ok = true, SheetXmm = 410, SheetYmm = 290, TitleBlockXmm = 400, TitleBlockYmm = 280, Description = "viewport of E-GF-LIGHTING on E-101"
        });

        public Task<SetupResult> Run(SetupConfig config, SetupPlan plan, IProgress<string> progress, CancellationToken ct)
        {
            Runs++;
            var res = new SetupResult();
            foreach (var r in plan.Included)
            {
                var rr = new RowResult { RowKey = r.RowKey, ViewName = r.ViewName, SheetNumber = r.SheetNumber };
                if (r.ViewAction == ViewAction.Create)
                {
                    Catalog.Views.Add(new ExistingViewInfo { UniqueId = "v-" + r.RowKey, Name = r.ViewName, TitleOnSheet = r.TitleOnSheet, ViewType = r.Family.ToString(), Family = r.Family, LevelUniqueId = r.LevelUniqueId, TemplateUniqueId = r.TemplateUniqueId, SetupRowKey = r.RowKey });
                    rr.Outcomes.Add(RowOutcome.Created);
                }
                else rr.Outcomes.Add(RowOutcome.Unchanged);
                if (r.SheetAction == SheetAction.Create && !Catalog.Sheets.Any(s => s.SetupRowKey == r.SheetKey))
                    Catalog.Sheets.Add(new ExistingSheetInfo { UniqueId = "s-" + r.SheetKey, Number = r.SheetNumber, Name = r.SheetName, SetupRowKey = r.SheetKey });
                if (r.PlaceAction == PlaceAction.Place)
                {
                    if (FailOnPlacement) { rr.Outcomes.Add(RowOutcome.Failed); res.Rows.Add(rr); res.MarkRolledBack("Rolled back."); return Task.FromResult(res); }
                    var sheet = Catalog.Sheets.First(s => s.SetupRowKey == r.SheetKey);
                    var view = Catalog.Views.First(v => v.SetupRowKey == r.RowKey);
                    sheet.PlacedViewUniqueIds.Add(view.UniqueId);
                    view.PlacedOnSheet = sheet.Number;
                    rr.Outcomes.Add(RowOutcome.Aligned);
                    rr.ErrorMm = 0.01;
                    rr.BoxMm = new[] { 100.0, 100, 500, 400 };
                }
                res.Rows.Add(rr);
            }
            res.Committed = true;
            res.Summary = "Done.";
            return Task.FromResult(res);
        }

        public Task<string> ShowElement(string uniqueId) => Task.FromResult("shown " + uniqueId);

        private static SetupCatalog SetupJsonCopy(SetupCatalog c)
        {
            var copy = new SetupCatalog { DocumentTitle = c.DocumentTitle, ProjectNumber = c.ProjectNumber };
            copy.Levels.AddRange(c.Levels); copy.ViewFamilyTypes.AddRange(c.ViewFamilyTypes); copy.Templates.AddRange(c.Templates);
            copy.TitleBlocks.AddRange(c.TitleBlocks); copy.ScopeBoxes.AddRange(c.ScopeBoxes); copy.ViewportTypes.AddRange(c.ViewportTypes);
            foreach (var v in c.Views) copy.Views.Add(new ExistingViewInfo { UniqueId = v.UniqueId, Name = v.Name, TitleOnSheet = v.TitleOnSheet, ViewType = v.ViewType, Family = v.Family, LevelUniqueId = v.LevelUniqueId, TemplateUniqueId = v.TemplateUniqueId, SetupRowKey = v.SetupRowKey, PlacedOnSheet = v.PlacedOnSheet });
            foreach (var s in c.Sheets) copy.Sheets.Add(new ExistingSheetInfo { UniqueId = s.UniqueId, Number = s.Number, Name = s.Name, SetupRowKey = s.SetupRowKey, PlacedViewUniqueIds = new List<string>(s.PlacedViewUniqueIds) });
            return copy;
        }
    }

    internal sealed class FakeSetupDialogs : ISetupDialogs
    {
        public bool Answer = true;
        public string PromptAnswer = "Electrical setup";
        public List<string> Messages { get; } = new List<string>();
        public bool Confirm(string title, string message) { Messages.Add(message); return Answer; }
        public void Info(string title, string message) => Messages.Add(message);
        public void Error(string title, string message) => Messages.Add("ERROR " + message);
        public string Prompt(string title, string label, string defaultValue) => PromptAnswer;
        public string PickOpenProfile() => null;
        public string PickSaveProfile(string suggestedName) => null;
    }

    public static class ProjectSetupFlowTests
    {
        [Test]
        public static void ViewModel_FullSetup_Run_ThenRerunIsNoOp()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-psvm-" + Guid.NewGuid().ToString("N"));
            try
            {
                var gw = new FakeSetupGateway();
                var dlg = new FakeSetupDialogs();
                var vm = new ProjectSetupViewModel(gw, dlg, Path.Combine(dir, "p"), Path.Combine(dir, "l"));
                vm.InitializeAsync().GetAwaiter().GetResult();
                Assert.Equal(2, vm.Levels.Count);
                Assert.Equal(2, vm.ViewSets.Count, "two example View Sets seeded");
                Assert.Equal("GF", vm.Levels[0].Code);
                Assert.Equal("L01", vm.Levels[1].Code);
                foreach (var l in vm.Levels) l.Selected = true;
                vm.ViewSets[0].Name = "LIGHTING"; vm.ViewSets[0].TemplateUniqueId = "tpl-ltg";
                vm.ViewSets[1].Name = "POWER"; vm.ViewSets[1].TemplateUniqueId = "tpl-pwr";
                Assert.Contains("E-GF-LIGHTING", vm.ViewSets[0].Example);

                vm.Step = ProjectSetupViewModel.ReviewStep;
                Assert.Equal(4, vm.Rows.Count);
                Assert.False(vm.RunCommand.CanExecute(null), "no anchor yet");
                Assert.Contains("no model anchor", string.Join(" ", vm.Rows.Select(r => r.Reason)));

                vm.SelectedGroup = vm.Groups[0];
                vm.PickGridsCommand.ExecuteAsync().GetAwaiter().GetResult();
                vm.PickReferenceCommand.ExecuteAsync(vm.SelectedGroup.Slots[0]).GetAwaiter().GetResult();
                Assert.Near(410, vm.SelectedGroup.Slots[0].Xmm, 1e-9, "target taken from reference viewport");
                vm.Rebuild();
                Assert.True(vm.Plan.CanRun, string.Join(" | ", vm.Rows.Select(r => r.Reason)));

                // a row edit is an override kept by key
                vm.Rows.First(r => r.ViewName == "E-GF-POWER").SheetName = "GF POWER (edited)";
                Assert.Equal("GF POWER (edited)", vm.Rows.First(r => r.ViewName == "E-GF-POWER").SheetName);

                vm.RunAsync().GetAwaiter().GetResult();
                Assert.Equal(1, gw.Runs);
                Assert.True(vm.Result.Committed);
                Assert.Equal(4, vm.Result.Count(RowOutcome.Created));
                Assert.Equal(4, gw.Catalog.Views.Count);
                Assert.Equal(4, gw.Catalog.Sheets.Count);

                // After the refresh the plan is a verification only.
                Assert.Equal(0, vm.Plan.ViewsToCreate);
                Assert.Equal(0, vm.Plan.SheetsToCreate);
                Assert.Equal(0, vm.Plan.Placements);

                vm.SaveProfileCommand.Execute(null);
                Assert.True(vm.Profiles.Contains("Electrical setup"));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void ViewModel_FailedPlacement_ReportsRollbackNotSuccess()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-psvm-" + Guid.NewGuid().ToString("N"));
            try
            {
                var gw = new FakeSetupGateway { FailOnPlacement = true };
                var vm = new ProjectSetupViewModel(gw, new FakeSetupDialogs(), Path.Combine(dir, "p"), Path.Combine(dir, "l"));
                vm.InitializeAsync().GetAwaiter().GetResult();
                foreach (var l in vm.Levels) l.Selected = true;
                vm.SelectedGroup = vm.Groups[0];
                vm.PickGridsCommand.ExecuteAsync().GetAwaiter().GetResult();
                vm.Step = ProjectSetupViewModel.ReviewStep;
                vm.RunAsync().GetAwaiter().GetResult();
                Assert.False(vm.Result.Committed);
                Assert.Contains("NOT applied", vm.ResultSummary);
                Assert.True(vm.Result.Rows.All(r => r.Outcomes.Contains(RowOutcome.Failed) || r.Outcomes.Contains(RowOutcome.RolledBack)), "no row reported as created after a rollback");
                Assert.Equal(4, vm.Plan.ViewsToCreate, "plan unchanged: nothing was kept");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        /// <summary>
        /// Regression for the Revit crash when opening Project Setup: combo boxes re-write their value whenever their
        /// item list is replaced. The view model must not hand out new lists on every edit (endless re-entrant loop →
        /// stack overflow → Revit closes) and must not lose a stored choice when a combo pushes null.
        /// </summary>
        [Test]
        public static void ViewModel_ComboRewrites_DoNotLoopOrEraseChoices()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-psvm-" + Guid.NewGuid().ToString("N"));
            try
            {
                var vm = new ProjectSetupViewModel(new FakeSetupGateway(), new FakeSetupDialogs(), Path.Combine(dir, "p"), Path.Combine(dir, "l"));
                int writes = 0, depth = 0, maxDepth = 0;
                vm.PropertyChanged += (o, e) =>
                {
                    if (vm.SelectedSet == null) return;
                    if (e.PropertyName != nameof(ProjectSetupViewModel.TemplatesForSelected) && e.PropertyName != nameof(ProjectSetupViewModel.ViewTypesForSelected)
                        && e.PropertyName != nameof(ProjectSetupViewModel.TitleBlocks)) return;
                    if (++writes > 10000) throw new InvalidOperationException("endless loop");
                    depth++; maxDepth = Math.Max(maxDepth, depth);
                    // What WPF does: re-assign the current value, or null when it is not in the new list.
                    vm.SelectedSet.TemplateUniqueId = vm.SelectedSet.TemplateUniqueId;
                    vm.SelectedSet.TemplateUniqueId = null;
                    vm.SelectedSet.ViewFamilyTypeUniqueId = vm.SelectedSet.ViewFamilyTypeUniqueId;
                    vm.SelectedSet.TitleBlockUniqueId = null;
                    depth--;
                };
                vm.InitializeAsync().GetAwaiter().GetResult();
                foreach (var l in vm.Levels) l.Selected = true;
                vm.SelectedSet = vm.ViewSets[0];
                vm.SelectedSet.TemplateUniqueId = "tpl-ltg";
                var lists = vm.TemplatesForSelected;
                vm.SelectedSet.Name = "LIGHTING";
                vm.SelectedSet.Scale = 50;
                Assert.True(ReferenceEquals(lists, vm.TemplatesForSelected), "editing does not replace combo lists");
                Assert.Equal("tpl-ltg", vm.SelectedSet.TemplateUniqueId, "a null from a combo never erases the choice");
                Assert.Equal("tb-a1", vm.SelectedSet.TitleBlockUniqueId);
                Assert.True(maxDepth <= 2, "no re-entrant cascade (depth " + maxDepth + ")");
                vm.SelectedSet.Family = PlanFamily.CeilingPlan;   // a family change does rebuild the lists, once
                Assert.False(ReferenceEquals(lists, vm.TemplatesForSelected));
                Assert.True(vm.ViewTypesForSelected.All(t => t.Family == PlanFamily.CeilingPlan));
                vm.Step = ProjectSetupViewModel.ReviewStep;
                Assert.True(writes < 100, "bounded number of combo writes: " + writes);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
