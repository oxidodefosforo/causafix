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
        public int ValueReads;
        public TitlePick NextTitlePick = new TitlePick { Ok = true, RelativeXmm = 0, RelativeYmm = -12, SheetXmm = 120, SheetYmm = 40, LineLengthMm = 80, Description = "title of E-GF-LIGHTING on E-101" };
        public Task<SetupCatalog> ReadCatalog(IReadOnlyCollection<CausaFix.ProjectSetup.Naming.ParamRef> wanted) => Task.FromResult(SetupJsonCopy(Catalog));
        public Task<ParamValueSet> ReadParameterValues(IReadOnlyCollection<CausaFix.ProjectSetup.Naming.ParamRef> wanted)
        {
            ValueReads++;
            var set = new ParamValueSet();
            set.Keys.AddRange(wanted.Select(w => w.Key));
            foreach (var v in Catalog.Views) set.Views[v.UniqueId] = new Dictionary<string, string>(v.Values);
            foreach (var s in Catalog.Sheets) set.Sheets[s.UniqueId] = new Dictionary<string, string>(s.Values);
            return Task.FromResult(set);
        }
        public Task<TitlePick> PickReferenceTitle(TitleAnchor anchor) => Task.FromResult(NextTitlePick);

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
            copy.Parameters.AddRange(c.Parameters); copy.Viewports.AddRange(c.Viewports);
            foreach (var kv in c.ProjectInfoValues) copy.ProjectInfoValues[kv.Key] = kv.Value;
            copy.TitleBlocks.AddRange(c.TitleBlocks); copy.ScopeBoxes.AddRange(c.ScopeBoxes); copy.ViewportTypes.AddRange(c.ViewportTypes);
            foreach (var v in c.Views) copy.Views.Add(new ExistingViewInfo { UniqueId = v.UniqueId, Name = v.Name, TitleOnSheet = v.TitleOnSheet, ViewType = v.ViewType, Family = v.Family, LevelUniqueId = v.LevelUniqueId, TemplateUniqueId = v.TemplateUniqueId, SetupRowKey = v.SetupRowKey, PlacedOnSheet = v.PlacedOnSheet, Scale = v.Scale });
            foreach (var s in c.Sheets) copy.Sheets.Add(new ExistingSheetInfo { UniqueId = s.UniqueId, Number = s.Number, Name = s.Name, SetupRowKey = s.SetupRowKey, PlacedViewUniqueIds = new List<string>(s.PlacedViewUniqueIds), TitleBlockCount = s.TitleBlockCount });
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
    
        /// <summary>Build Name, numbering, Views on Sheet, title capture and renumbering through the view model (mocked Revit).</summary>
        [Test]
        public static void ViewModel_BuilderCompositionTitleAndRenumber()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-psvm-" + Guid.NewGuid().ToString("N"));
            try
            {
                var gw = new FakeSetupGateway();
                gw.Catalog = V2Data.Catalog(2);
                gw.Catalog.Parameters.Add(new ParamInfo { Ref = new CausaFix.ProjectSetup.Naming.ParamRef { Scope = CausaFix.ProjectSetup.Naming.ParamScope.Sheet, Kind = CausaFix.ProjectSetup.Naming.ParamKind.Shared, Guid = "44444444-4444-4444-4444-444444444444", Name = "Package" }, Name = "Package" });
                var vm = new ProjectSetupViewModel(gw, new FakeSetupDialogs(), Path.Combine(dir, "p"), Path.Combine(dir, "l"));
                vm.InitializeAsync().GetAwaiter().GetResult();
                foreach (var l in vm.Levels) l.Selected = true;
                vm.SelectedSet = vm.ViewSets[0];
                Assert.True(vm.ViewNameBuilder != null && vm.ViewNameBuilder.Components.Count == 3, "builder shows the View Name components");
                Assert.True(vm.ViewNameBuilder.Preview.Count == 2, "preview with the real rows");
                Assert.Equal("E-GF-Lighting", vm.ViewNameBuilder.Preview[0].Result);

                // Picker: search, homonyms are disambiguated, parameter added without typing tokens.
                vm.ViewNameBuilder.Filter = "level code";
                Assert.True(vm.ViewNameBuilder.PickerOptions.Count(o => o.Source == CausaFix.ProjectSetup.Naming.ComponentSource.Parameter) == 2);
                Assert.True(vm.ViewNameBuilder.PickerOptions.Where(o => o.Source == CausaFix.ProjectSetup.Naming.ComponentSource.Parameter).Select(o => o.Label).Distinct().Count() == 2, "homonyms get distinct labels");
                vm.ViewNameBuilder.SelectedOption = vm.ViewNameBuilder.PickerOptions.First(o => o.Param?.Guid == V2Data.LevelCodeShared.Guid);
                vm.ViewNameBuilder.AddCommand.Execute(null);
                var added = vm.ViewNameBuilder.Components.Last();
                vm.ViewNameBuilder.MoveUpCommand.Execute(added);
                Assert.Equal("E-GF-00-Lighting", vm.ViewNameBuilder.Preview[0].Result);
                vm.ViewNameBuilder.RemoveCommand.Execute(added);
                Assert.Equal("E-GF-Lighting", vm.ViewNameBuilder.Preview[0].Result);

                // A sheet parameter needs its values: they are read (read-only) when it is added.
                vm.SheetNameBuilder.Filter = "package";
                vm.SheetNameBuilder.SelectedOption = vm.SheetNameBuilder.PickerOptions.First();
                vm.SheetNameBuilder.AddCommand.Execute(null);
                Assert.Equal(1, gw.ValueReads);
                vm.SheetNameBuilder.Components.Last().Required = false;

                // Numbering: the seeded standard profile, a prefix and a zone.
                Assert.True(vm.NumberingProfiles.Count >= 1);
                vm.SelectedSet.Purpose = "FCC";
                vm.Levels[0].Zone = "A";
                vm.Step = ProjectSetupViewModel.ReviewStep;
                Assert.True(vm.Rows.Any(r => r.Row.SheetNumber == "FCC-E-A-101"), string.Join(", ", vm.Rows.Select(r => r.Row.SheetNumber)));
                Assert.True(vm.Rows.Any(r => r.Row.SheetNumber == "FCC-E-102"), "zone omitted with its separator");

                // Views on Sheet: overlay Lighting + Power.
                vm.Step = 2;
                vm.AddCompositionCommand.Execute(null);
                vm.AddViewSetMemberCommand.Execute(null);
                Assert.Equal(2, vm.SelectedComposition.Members.Count);
                vm.SelectedComposition.HiddenTitleViewportTypeUniqueId = "vpt-none";
                vm.SelectedGroup = vm.Groups[0];
                vm.PickGridsCommand.ExecuteAsync().GetAwaiter().GetResult();
                vm.Step = ProjectSetupViewModel.ReviewStep;
                Assert.True(vm.Plan.CanRun, string.Join(" | ", vm.Rows.Select(r => r.Reason)));
                Assert.Equal(2, vm.Plan.SheetsToCreate, "one overlay sheet per level");
                Assert.True(vm.Rows.Any(r => r.SheetGroup.Contains("Overlay")));

                // View title: capture from a reference title (relative offset) for the profile default.
                vm.SelectedTitleScope = vm.TitleScopes[0];
                vm.SelectedTitleScope.Mode = TitleMode.RelativeToViewport;
                vm.PickReferenceTitleCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(TitleMode.MatchReference, vm.Config.Title.Mode);
                Assert.Near(-12, vm.Config.Title.OffsetYmm, 1e-9);
                vm.Rebuild();
                Assert.Equal(2, vm.Plan.TitlesToPlace, "only the primary of each overlay");

                vm.RunAsync().GetAwaiter().GetResult();
                Assert.True(vm.Result.Committed);
                Assert.Equal(0, vm.Plan.SheetsToCreate + vm.Plan.ViewsToCreate, "re-run creates nothing");

                // Prefix change: pending renumber shown, approved explicitly.
                vm.SelectedSet = vm.ViewSets[0];
                vm.SelectedSet.Purpose = "QA";
                vm.Step = ProjectSetupViewModel.NumberingStep;
                Assert.Equal(2, vm.PendingRenumbers.Count);
                Assert.True(vm.PendingRenumbers.All(p => p.Proposed.StartsWith("QA-")));
                vm.PendingRenumbers[1].Include = false;
                vm.ApprovePendingCommand.Execute(null);
                vm.Step = ProjectSetupViewModel.ReviewStep;
                Assert.Equal(1, vm.Plan.Rows.Count(r => r.Updates.Contains("sheet:number")));
                Assert.Equal(0, vm.Plan.SheetsToCreate);

                vm.SaveProfileCommand.Execute(null);
                Assert.True(vm.Profiles.Contains("Electrical setup"));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    
        /// <summary>A format-1 profile loaded through the window: migrated, editors in sync, no duplicates proposed.</summary>
        [Test]
        public static void ViewModel_LoadsFormat1Profile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-psvm-" + Guid.NewGuid().ToString("N"));
            try
            {
                var gw = new FakeSetupGateway { Catalog = V2Data.Catalog(2) };
                var dlg = new FakeSetupDialogs();
                var vm = new ProjectSetupViewModel(gw, dlg, Path.Combine(dir, "p"), Path.Combine(dir, "l"));
                vm.InitializeAsync().GetAwaiter().GetResult();
                string json = ProjectSetupV2Tests.Format1Profile;
                File.WriteAllText(Path.Combine(dir, "p", "setup-" + CausaFix.Core.Profiles.ProfileStore.FileNameFor("Old electrical")), json);
                vm.ReloadProfilesForTest();
                vm.SelectedProfile = "Old electrical";
                vm.LoadProfileCommand.Execute(null);
                Assert.Equal(1, vm.Compositions.Count, "migrated composition shown in Views on Sheet");
                Assert.Equal(vm.Config.NumberingProfiles.Count, vm.NumberingProfiles.Count);
                Assert.True(dlg.Messages.Any(m => m.Contains("Migrated")), string.Join(" | ", dlg.Messages));
                vm.Step = ProjectSetupViewModel.ReviewStep;
                Assert.True(vm.Rows.Count == 6, "3 View Sets × 2 levels");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    
        /// <summary>Same regression as above for the new combo sources (numbering, View Sets, viewport types, title scopes).</summary>
        [Test]
        public static void ViewModel_NewComboSources_DoNotLoop()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-psvm-" + Guid.NewGuid().ToString("N"));
            try
            {
                var gw = new FakeSetupGateway { Catalog = V2Data.Catalog(2) };
                var vm = new ProjectSetupViewModel(gw, new FakeSetupDialogs(), Path.Combine(dir, "p"), Path.Combine(dir, "l"));
                int writes = 0;
                var watched = new[] { nameof(ProjectSetupViewModel.NumberingChoices), nameof(ProjectSetupViewModel.ViewSetChoices), nameof(ProjectSetupViewModel.ViewportTypeChoices), nameof(ProjectSetupViewModel.NumberingChoicesInherit), nameof(ProjectSetupViewModel.SelectedTitleScope) };
                vm.PropertyChanged += (o, e) =>
                {
                    if (Array.IndexOf(watched, e.PropertyName) < 0) return;
                    if (++writes > 5000) throw new InvalidOperationException("endless loop");
                    foreach (var vs in vm.ViewSets) { vs.NumberingProfileKey = vs.NumberingProfileKey; vs.NumberingProfileKey = null; }
                    foreach (var c in vm.Compositions) { c.NumberingProfileKey = null; c.HiddenTitleViewportTypeUniqueId = null; foreach (var m in c.Members) { m.ViewSetKey = null; m.ViewportTypeUniqueId = null; } }
                    if (vm.SelectedTitleScope != null) vm.SelectedTitleScope.Mode = vm.SelectedTitleScope.Mode;
                };
                vm.InitializeAsync().GetAwaiter().GetResult();
                foreach (var l in vm.Levels) l.Selected = true;
                string key = vm.ViewSets[0].NumberingProfileKey;
                vm.AddNumberingCommand.Execute(null);
                vm.SelectedNumbering.Name = "Renamed";
                vm.AddCompositionCommand.Execute(null);
                vm.AddViewSetMemberCommand.Execute(null);
                vm.SelectedComposition.Name = "Overlay A";
                vm.ViewSets[0].Name = "LIGHTING 2";
                Assert.Equal(key, vm.ViewSets[0].NumberingProfileKey, "a null from a combo never erases the numbering choice");
                Assert.True(vm.SelectedComposition.Members.All(m => !string.IsNullOrEmpty(m.ViewSetKey)));
                Assert.True(writes < 200, "bounded: " + writes);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
