using System;
using System.IO;
using System.Linq;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Geometry;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Planning;
using CausaFix.ProjectSetup.Profiles;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Core.Tests
{
    /// <summary>
    /// Rule selection in the view model and the "take title position from a placed view" flow (mocked Revit).
    /// The hit testing of the real WPF list is checked by CausaFix.Ui.Tests on Windows; these tests do not prove it.
    /// </summary>
    public static class ProjectSetupTitleRefTests
    {
        private static ProjectSetupViewModel Open(FakeSetupGateway gw, FakeSetupDialogs dlg, string dir, int sets = 4)
        {
            var vm = new ProjectSetupViewModel(gw, dlg, Path.Combine(dir, "p"), Path.Combine(dir, "l"));
            vm.InitializeAsync().GetAwaiter().GetResult();
            foreach (var l in vm.Levels) l.Selected = true;
            string[] names = { "LIGHTING", "POWER", "CONTAINMENT", "RCP" };
            while (vm.ViewSets.Count < sets) vm.AddSetCommand.Execute(null);
            for (int i = 0; i < sets; i++) vm.ViewSets[i].Name = names[i];
            vm.ViewSets[3].Family = PlanFamily.CeilingPlan;
            return vm;
        }

        private static string Dir() => Path.Combine(Path.GetTempPath(), "cf-pstr-" + Guid.NewGuid().ToString("N"));

        private static TitlePick Pick(double sx = 120, double sy = 40) => new TitlePick
        {
            Ok = true, RelativeXmm = 1.5, RelativeYmm = -12, SheetXmm = sx, SheetYmm = sy, LineLengthMm = 80,
            TitleBlockCount = 1, TitleBlockXmm = sx - 10, TitleBlockYmm = sy - 10, SheetNumber = "E-101", ViewName = "BASEMENT - LIGHTING",
            Description = "title of 'BASEMENT - LIGHTING' on sheet E-101 - LIGHTING LAYOUT"
        };

        [Test]
        public static void Rules_SelectionKeepsEnabled_ToggleOnce_EditsSurviveSwitchAndRebuild()
        {
            string dir = Dir();
            try
            {
                var vm = Open(new FakeSetupGateway(), new FakeSetupDialogs(), dir);
                foreach (var name in new[] { "POWER", "LIGHTING", "CONTAINMENT", "RCP" })
                {
                    var item = vm.ViewSets.First(v => v.Name == name);
                    bool enabled = item.Enabled;
                    vm.SelectedSet = item;
                    Assert.True(ReferenceEquals(item, vm.SelectedSet), "selected " + name);
                    Assert.Equal(enabled, item.Enabled, "selecting " + name + " keeps Enabled");
                    Assert.True(vm.ViewNameBuilder != null && vm.TitleBuilder != null, "editor shows " + name);
                }

                // Toggling the box changes Enabled exactly once and does not move the selection elsewhere.
                var power = vm.ViewSets.First(v => v.Name == "POWER");
                vm.SelectedSet = power;
                int notifications = 0;
                power.PropertyChanged += (o, e) => { if (e.PropertyName == nameof(ViewSetItem.Enabled)) notifications++; };
                power.Enabled = false;
                power.Enabled = false;                    // WPF re-assigning the same value
                Assert.Equal(1, notifications, "one change per toggle");
                Assert.False(vm.Config.ViewSets.First(r => r.Name == "POWER").Enabled);
                Assert.True(ReferenceEquals(power, vm.SelectedSet));

                // A disabled rule is still editable.
                power.Scale = 50;
                power.Code = "PW";
                Assert.Equal(50, vm.Config.ViewSets.First(r => r.Name == "POWER").Scale);
                Assert.Equal("PW", vm.Config.ViewSets.First(r => r.Name == "POWER").Code);

                // An edit committed before switching rows stays on its rule.
                var rcp = vm.ViewSets.First(v => v.Name == "RCP");
                vm.SelectedSet = rcp;
                rcp.Zone = "B";
                vm.SelectedSet = vm.ViewSets.First(v => v.Name == "LIGHTING");
                Assert.Equal("B", vm.Config.ViewSets.First(r => r.Name == "RCP").Zone);
                Assert.Equal(PlanFamily.CeilingPlan, vm.Config.ViewSets.First(r => r.Name == "RCP").Family);

                // Rebuilding the editors keeps the selection by identity (rule key), not by position.
                vm.SelectedSet = rcp;
                string key = rcp.Rule.Key;
                vm.RefreshCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(key, vm.SelectedSet.Rule.Key, "selection restored by key after the rebuild");
                Assert.False(vm.Config.ViewSets.First(r => r.Name == "POWER").Enabled, "rebuild does not restore defaults");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void TitleRef_FromViewSet_DefaultsToFixedPaperPosition_TextStaysFromRules()
        {
            string dir = Dir();
            try
            {
                var gw = new FakeSetupGateway { Catalog = SetupData.Catalog(3) };
                var vm = Open(gw, new FakeSetupDialogs(), dir);
                vm.SelectedSet = vm.ViewSets[0];
                Assert.Equal("Profile default", vm.SetTitleScope.Scope, "default scope: what these views use now");
                Assert.Contains("Used by:", vm.SetTitleScopeInfo);
                Assert.Contains("LIGHTING", vm.SetTitleScopeInfo);
                Assert.Contains("FixedPosition", vm.SetTitleScopeInfo);

                gw.NextTitlePick = Pick();
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                var t = vm.Config.Title;
                Assert.Equal(TitleMode.MatchReference, t.Mode);
                Assert.Equal(TitleCaptureKind.FixedPosition, t.CaptureKind, "no earlier explicit choice → fixed paper position");
                Assert.True(t.IsFixed);
                Assert.Near(120, t.FixedXmm, 1e-9); Assert.Near(40, t.FixedYmm, 1e-9);
                Assert.Near(-12, t.OffsetYmm, 1e-9, "the relative result is kept in its own fields (not applied)");
                Assert.Equal(TitleLineMode.Set, t.LineMode); Assert.Near(80, t.LineLengthMm, 1e-9);
                Assert.Contains("BASEMENT - LIGHTING", t.ReferenceDescription);
                Assert.Contains("E-101", t.ReferenceDescription);
                Assert.Contains("E-101", vm.Status);
                Assert.Contains("Profile default", vm.SetTitleCurrent);

                // Same configuration as View Title Placement (no second copy).
                Assert.True(ReferenceEquals(vm.TitleScopes[0], vm.SetTitleScope));
                Assert.Equal(TitleCaptureKind.FixedPosition, vm.TitleScopes[0].CaptureKind);

                // Every level gets the same fixed paper point; each keeps its own title text.
                vm.Step = ProjectSetupViewModel.ReviewStep;
                var rows = vm.Plan.Rows.Where(r => r.RuleKey == vm.ViewSets[0].Rule.Key).ToList();
                Assert.Equal(3, rows.Count);
                Assert.True(rows.All(r => r.TitleAction == TitleAction.Apply && r.Title.IsFixed && r.Title.FixedXmm == 120 && r.Title.FixedYmm == 40));
                Assert.Equal(3, rows.Select(r => r.TitleOnSheet).Distinct().Count(), "title text per level, not the reference's text");
                Assert.False(rows.Any(r => r.TitleOnSheet.Contains("BASEMENT")));
                Assert.Equal(0, gw.Runs, "picking only changes the draft");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void TitleRef_ExplicitRelativeChoiceAndSavedConfigsAreKept()
        {
            string dir = Dir();
            try
            {
                var gw = new FakeSetupGateway();
                var vm = Open(gw, new FakeSetupDialogs(), dir);
                vm.SelectedSet = vm.ViewSets[0];
                vm.SetTitleScope.Mode = TitleMode.MatchReference;
                vm.SetTitleScope.CaptureKind = TitleCaptureKind.RelativeOffset;     // explicit
                Assert.True(vm.Config.Title.CaptureKindExplicit);
                gw.NextTitlePick = Pick();
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(TitleCaptureKind.RelativeOffset, vm.Config.Title.CaptureKind);
                Assert.False(vm.Config.Title.IsFixed);
                Assert.Near(1.5, vm.Config.Title.OffsetXmm, 1e-9);

                // Mode chosen as Relative earlier also counts as an explicit choice.
                var p = new TitlePlacement { Mode = TitleMode.RelativeToViewport };
                Assert.Equal(TitleCaptureKind.RelativeOffset, p.CaptureKindForNewCapture);
                // A saved earlier capture keeps its kind (old profiles have no explicit flag).
                var saved = new TitlePlacement { Mode = TitleMode.MatchReference, CaptureKind = TitleCaptureKind.RelativeOffset, ReferenceDescription = "ref" };
                Assert.Equal(TitleCaptureKind.RelativeOffset, saved.CaptureKindForNewCapture);
                Assert.Equal(TitleCaptureKind.FixedPosition, new TitlePlacement().CaptureKindForNewCapture);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void TitleRef_CancelAndInvalidPicksKeepPreviousValues_RetryWorks()
        {
            string dir = Dir();
            try
            {
                var gw = new FakeSetupGateway();
                var dlg = new FakeSetupDialogs();
                var vm = Open(gw, dlg, dir);
                vm.SelectedSet = vm.ViewSets[0];
                string before = SetupJson.Serialize(vm.Config);

                gw.TitlePicks.Enqueue(new TitlePick { Cancelled = true, Message = "Pick cancelled: nothing changed." });
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(before, SetupJson.Serialize(vm.Config), "cancel keeps everything");
                Assert.Contains("did not change", vm.Status);

                // Not a sheet → message, user declines the retry → unchanged.
                dlg.Answer = false;
                gw.TitlePicks.Enqueue(new TitlePick { Message = "The active view in Revit is not a sheet." });
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(before, SetupJson.Serialize(vm.Config));
                Assert.True(dlg.Messages.Any(m => m.Contains("not a sheet") && m.Contains("Try again")));

                // Hidden title, then a good pick on retry.
                dlg.Answer = true;
                int n = gw.TitlePickCount;
                gw.TitlePicks.Enqueue(new TitlePick { Message = "The viewport of 'RCP' shows no title." });
                gw.TitlePicks.Enqueue(Pick(200, 30));
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(n + 2, gw.TitlePickCount, "retried once");
                Assert.Near(200, vm.Config.Title.FixedXmm, 1e-9);

                // Title block frame on a sheet with two title blocks: ambiguous → reported, nothing guessed.
                vm.SetTitleScope.Frame = SheetFrame.TitleBlock;
                string mid = SetupJson.Serialize(vm.Config);
                dlg.Answer = false;
                var amb = Pick(300, 30); amb.TitleBlockCount = 2; amb.TitleBlockXmm = null; amb.TitleBlockYmm = null;
                gw.TitlePicks.Enqueue(amb);
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(mid, SetupJson.Serialize(vm.Config));
                Assert.True(dlg.Messages.Any(m => m.Contains("2 title blocks")));

                // Valid title block frame: local coordinates of the title block are stored.
                gw.TitlePicks.Enqueue(Pick(300, 30));
                dlg.Answer = true;
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.Equal(SheetFrame.TitleBlock, vm.Config.Title.Frame);
                Assert.Near(290, vm.Config.Title.FixedXmm, 1e-9);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void TitleRef_GroupScope_OverrideOnlyAfterValidPick_OthersUntouched()
        {
            string dir = Dir();
            try
            {
                var gw = new FakeSetupGateway();
                var vm = Open(gw, new FakeSetupDialogs(), dir);
                vm.SelectedSet = vm.ViewSets[1];
                var group = vm.SetTitleScopes.FirstOrDefault(s => s.Scope.StartsWith("Group: "));
                Assert.True(group != null, "the View Set's group is offered");
                vm.SetTitleScope = group;
                Assert.Contains("inherits now", vm.SetTitleScopeInfo);
                var profileBefore = SetupJson.Serialize(new SetupConfig { Title = vm.Config.Title });

                gw.TitlePicks.Enqueue(new TitlePick { Cancelled = true });
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                Assert.True(vm.Config.Groups.All(g => g.Title == null), "cancel creates no override");

                gw.NextTitlePick = Pick();
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                var g0 = vm.Config.Groups.First(g => "Group: " + g.Key == group.Scope);
                Assert.True(g0.Title != null && g0.Title.IsFixed, "the group got its own placement");
                Assert.Equal(profileBefore, SetupJson.Serialize(new SetupConfig { Title = vm.Config.Title }), "profile default unchanged");

                // A viewport override in Review stays more specific.
                vm.Step = ProjectSetupViewModel.ReviewStep;
                var row = vm.Plan.Rows.First(r => r.TitleAction == TitleAction.Apply);
                vm.Config.EnsureOverride(row.RowKey).Title = new TitlePlacement { Mode = TitleMode.RelativeToViewport, OffsetYmm = -3 };
                vm.Rebuild();
                Assert.False(vm.Plan.Rows.First(r => r.RowKey == row.RowKey).Title.IsFixed, "viewport override wins");
                vm.Step = 2;
                Assert.Contains("viewport override", vm.SetTitleScopeInfo);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void TitleRef_ProfileRoundTrip_AndOldProfileWithoutFlag()
        {
            string dir = Dir();
            try
            {
                var gw = new FakeSetupGateway();
                var dlg = new FakeSetupDialogs { PromptAnswer = "AZ" };
                var vm = Open(gw, dlg, dir);
                vm.SelectedSet = vm.ViewSets[0];
                gw.NextTitlePick = Pick(150, 22);
                vm.PickSetTitleReferenceCommand.ExecuteAsync().GetAwaiter().GetResult();
                vm.SaveProfileCommand.Execute(null);
                Assert.True(vm.Profiles.Contains("AZ"));

                var vm2 = new ProjectSetupViewModel(new FakeSetupGateway(), new FakeSetupDialogs(), Path.Combine(dir, "p"), Path.Combine(dir, "l2"));
                vm2.InitializeAsync().GetAwaiter().GetResult();
                vm2.SelectedProfile = "AZ";
                vm2.LoadProfileCommand.Execute(null);
                var t = vm2.Config.Title;
                Assert.Equal(TitleMode.MatchReference, t.Mode);
                Assert.Equal(TitleCaptureKind.FixedPosition, t.CaptureKind);
                Assert.Near(150, t.FixedXmm, 1e-9); Assert.Near(22, t.FixedYmm, 1e-9);
                Assert.Near(80, t.LineLengthMm, 1e-9);
                Assert.Contains("E-101", t.ReferenceDescription);
                Assert.Equal(vm.Config.ViewSets.Count, vm2.Config.ViewSets.Count, "other options load unchanged");

                // An older profile (no CaptureKindExplicit) keeps its relative capture.
                var old = SetupJson.Deserialize(SetupJson.Serialize(new SetupConfig
                {
                    Title = new TitlePlacement { Mode = TitleMode.MatchReference, CaptureKind = TitleCaptureKind.RelativeOffset, OffsetYmm = -9, ReferenceDescription = "old ref" }
                }).Replace(",\"CaptureKindExplicit\":false", "").Replace("\"CaptureKindExplicit\":false,", ""));
                Assert.False(old.Title.CaptureKindExplicit);
                Assert.False(old.Title.IsFixed, "behaviour of the old profile unchanged");
                Assert.Near(-9, old.Title.OffsetYmm, 1e-9);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        /// <summary>Viewport of any crop size; the title text corner sits at a family offset from the line start.</summary>
        private sealed class Title : ITitleProbe
        {
            public Box2 Box;
            public int Quarter;
            public P2 FamilyDelta = new P2(2, -1.5);
            public double TextW, TextH = 7;
            public P2 Offset = new P2(0, -10);
            public double Line = 60;
            public Box2 ViewportBox() => Box;
            public Box2? LabelBox()
            {
                var c = Box.Center;
                var corner = TitleGeometry.ToLocal(Box, c, Quarter).Min + Offset + FamilyDelta;
                double w = Math.Max(TextW, Math.Abs(Line));
                return TitleGeometry.Rotate(new Box2(corner.X, corner.Y, corner.X + w, corner.Y + TextH), c, Quarter);
            }
            public P2 LabelOffset { get => Offset; set => Offset = value; }
            public double LineLength { get => Line; set => Line = value; }
        }

        [Test]
        public static void TitleRef_FixedCapture_AlignsTitlesAcrossCropsAndLengths_DrawingUntouched()
        {
            // Reference title captured as the paper point of its bottom-left corner (what the Revit pick measures).
            var reference = new Title { Box = new Box2(60, 80, 520, 430), TextW = 40, Offset = new P2(5, -14) };
            var refCorner = TitleGeometry.Corner(reference.LabelBox().Value, TitleAnchor.BottomLeft);
            var t = new TitlePlacement { Mode = TitleMode.MatchReference, CaptureKind = TitleCaptureKind.FixedPosition, Anchor = TitleAnchor.BottomLeft,
                                         FixedXmm = refCorner.X, FixedYmm = refCorner.Y, LineMode = TitleLineMode.Set, LineLengthMm = 90 };
            var others = new[]
            {
                new Title { Box = new Box2(120, 140, 400, 380), TextW = 75 },                 // smaller crop, longer title
                new Title { Box = new Box2(40, 60, 700, 500), TextW = 120, Offset = new P2(30, 2) },
                new Title { Box = new Box2(200, 100, 500, 450), TextW = 60, Quarter = 1 },    // rotated viewport
            };
            foreach (var o in others)
            {
                var box = o.Box;
                var rep = TitleRunner.Place(o, t, null, new P2(t.FixedXmm, t.FixedYmm), o.Quarter, 0, 0, 0.1);
                Assert.True(rep.Placed, rep.Message);
                // In the unrotated frame the anchor corner lands on the captured point (rotated viewports: about their centre).
                var c = o.Box.Center;
                var corner = TitleGeometry.Corner(TitleGeometry.ToLocal(o.LabelBox().Value, c, o.Quarter), TitleAnchor.BottomLeft);
                var target = TitleGeometry.ToLocal(refCorner, c, o.Quarter);
                Assert.True((corner - target).Length <= 0.1, "anchor within tolerance");
                if (o.Quarter == 0) Assert.True((TitleGeometry.Corner(o.LabelBox().Value, TitleAnchor.BottomLeft) - refCorner).Length <= 0.1, "same paper point");
                Assert.Equal(box.ToArray()[0], o.Box.ToArray()[0]); Assert.Equal(box.ToArray()[3], o.Box.ToArray()[3]);   // drawing not moved
                Assert.Near(90, o.Line, 1e-9);
            }
            // Longer title keeps the anchor: the left edge stays, the text grows to the right.
            var a = others[0]; var b = others[1];
            Assert.Near(TitleGeometry.Corner(a.LabelBox().Value, TitleAnchor.BottomLeft).X, TitleGeometry.Corner(b.LabelBox().Value, TitleAnchor.BottomLeft).X, 0.1);
        }
    }
}
