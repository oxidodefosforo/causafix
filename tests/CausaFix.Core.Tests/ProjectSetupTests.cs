using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Geometry;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
using CausaFix.ProjectSetup.Planning;
using CausaFix.ProjectSetup.Profiles;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Core.Tests
{
    internal static class SetupData
    {
        public const string Doc = "doc-key-1";

        public static SetupCatalog Catalog(int levels = 2)
        {
            var c = new SetupCatalog { DocumentTitle = "Test", ProjectNumber = "P123" };
            string[] names = { "Ground Floor", "Level 01", "Level 02", "Level 03", "Level 04", "Level 05", "Level 06", "Level 07", "Level 08", "Roof" };
            for (int i = 0; i < levels; i++) c.Levels.Add(new LevelInfo { UniqueId = "lvl-" + i, Id = 100 + i, Name = names[i % names.Length] + (i >= names.Length ? " " + i : ""), Elevation = i * 12 });
            c.ViewFamilyTypes.Add(new ViewFamilyTypeInfo { UniqueId = "vft-fp", Name = "Floor Plan", Family = PlanFamily.FloorPlan });
            c.ViewFamilyTypes.Add(new ViewFamilyTypeInfo { UniqueId = "vft-rcp", Name = "Ceiling Plan", Family = PlanFamily.CeilingPlan });
            c.Templates.Add(new TemplateInfo { UniqueId = "tpl-ltg", Name = "E - Lighting", Family = PlanFamily.FloorPlan, ControlsScale = true, Scale = 100 });
            c.Templates.Add(new TemplateInfo { UniqueId = "tpl-pwr", Name = "E - Power", Family = PlanFamily.FloorPlan, ControlsScale = true, Scale = 50 });
            c.Templates.Add(new TemplateInfo { UniqueId = "tpl-rcp", Name = "RCP", Family = PlanFamily.CeilingPlan });
            c.TitleBlocks.Add(new TitleBlockInfo { UniqueId = "tb-a1", FamilyName = "TB", TypeName = "A1", WidthMm = 841, HeightMm = 594 });
            return c;
        }

        public static SetupConfig Config(SetupCatalog cat, int sets = 2)
        {
            var cfg = new SetupConfig { DocumentKey = Doc };
            int o = 0;
            foreach (var l in cat.Levels) cfg.Levels.Add(new LevelSelection { LevelUniqueId = l.UniqueId, LevelName = l.Name, Code = ProjectSetupViewModel.SuggestCode(l.Name, l.Elevation), Selected = true, Order = o++ });
            string[] n = { "Lighting", "Power", "Communications", "Security" };
            string[] t = { "tpl-ltg", "tpl-pwr", "", "" };
            for (int i = 0; i < sets; i++)
                cfg.ViewSets.Add(new ViewSetRule
                {
                    Key = "set" + i, Name = n[i % 4].ToUpperInvariant(), Code = n[i % 4].Substring(0, 3).ToUpperInvariant(), DisciplineCode = "E",
                    ViewFamilyTypeUniqueId = "vft-fp", TemplateUniqueId = t[i % 4], Scale = 100, TitleBlockUniqueId = "tb-a1",
                    ViewNamePattern = "{DisciplineCode}-{LevelCode}-{ViewSetName}",
                    SheetNumberPattern = "{DisciplineCode}-" + (i + 1) + "{Sequence:00}",
                    SheetNamePattern = "{LevelName:upper} - {ViewSetName}",
                    Sequence = new SequenceSettings { Start = 1, Increment = 1, Scope = SequenceScope.PerViewSet }
                });
            cfg.Groups[0].Anchor = new ModelAnchor { IsSet = true, X = 10, Y = 20, Description = "Grid A / 1", DocumentKey = Doc };
            cfg.Groups[0].Slots[0].Xmm = 400; cfg.Groups[0].Slots[0].Ymm = 300;
            return cfg;
        }
    }

    public static class ProjectSetupTests
    {
        [Test]
        public static void Tokens_ExpandPadCaseAndValidate()
        {
            var v = new TokenValues { ProjectNumber = "P1", DisciplineCode = "E", ViewSetCode = "LTG", ViewSetName = "Lighting", LevelName = "Level 01", LevelCode = "L01", Sequence = 7 };
            Assert.Equal("E-L01-Lighting", NamePattern.Expand("{DisciplineCode}-{LevelCode}-{ViewSetName}", v, NameContext.ViewName).Text);
            Assert.Equal("E-007", NamePattern.Expand("{DisciplineCode}-{Sequence:000}", v, NameContext.SheetNumber).Text);
            Assert.Equal("LEVEL 01 - Lighting", NamePattern.Expand("{LevelName:upper} - {ViewSetName}", v, NameContext.SheetName).Text);
            Assert.Equal("e-l01", NamePattern.Expand("{DisciplineCode}-{LevelCode}", v, NameContext.ViewName, TextCase.Lower).Text);
            Assert.Equal("{x} E", NamePattern.Expand("{{x}} {DisciplineCode}", v, NameContext.TitleOnSheet).Text);
            var missing = NamePattern.Expand("{Zone}-{LevelCode}", v, NameContext.ViewName);
            Assert.False(missing.Ok, "a token without value is an error, not a blank");
            Assert.Contains("{Zone}", missing.Errors[0]);
            Assert.False(NamePattern.Expand("{Levle}", v, NameContext.ViewName).Ok, "unknown token");
            Assert.False(NamePattern.Expand("{LevelCode", v, NameContext.ViewName).Ok, "unclosed brace");
            Assert.False(NamePattern.Expand("A:{LevelCode}", v, NameContext.ViewName).Ok, "':' not allowed in view names");
            Assert.True(NamePattern.Expand("A:{LevelCode}", v, NameContext.TitleOnSheet).Ok, "title on sheet is free text");
        }

        [Test]
        public static void Combinations_AcceptanceExample_FourRows()
        {
            var cat = SetupData.Catalog(2);
            var cfg = SetupData.Config(cat, 2);
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(4, plan.Rows.Count);
            var names = plan.Rows.Select(r => r.ViewName + "|" + r.SheetNumber + "|" + r.SheetName).ToList();
            Assert.True(names.Contains("E-GF-LIGHTING|E-101|GROUND FLOOR - LIGHTING"), string.Join("; ", names));
            Assert.True(names.Contains("E-L01-LIGHTING|E-102|LEVEL 01 - LIGHTING"));
            Assert.True(names.Contains("E-GF-POWER|E-201|GROUND FLOOR - POWER"));
            Assert.True(names.Contains("E-L01-POWER|E-202|LEVEL 01 - POWER"));
            Assert.True(plan.CanRun, string.Join(" ", plan.Rows.SelectMany(r => r.Errors)));
            Assert.Equal(4, plan.ViewsToCreate);
            Assert.Equal(4, plan.SheetsToCreate);
            var pwr = plan.Rows.First(r => r.ViewSetName == "POWER");
            Assert.Equal(50, pwr.EffectiveScale, "template-controlled scale is the effective one");
            Assert.Contains("controlled by template", pwr.Reason);
        }

        [Test]
        public static void Combinations_RuleSubsetAndExcludedRows()
        {
            var cat = SetupData.Catalog(3);
            var cfg = SetupData.Config(cat, 2);
            cfg.ViewSets[1].LevelUniqueIds.Add("lvl-2");      // Power only on Level 02
            cfg.EnsureOverride(PlanBuilder.RowKey("set0", "lvl-1")).Include = false;
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(4, plan.Rows.Count);
            Assert.Equal(3, plan.Included.Count());
            Assert.Equal(RowStatus.Excluded, plan.Rows.First(r => r.RowKey == "set0|lvl-1").Status);
        }

        [Test]
        public static void Sequences_StableWhenLevelsAreAdded_RegenerateIsExplicit()
        {
            var cat = SetupData.Catalog(3);
            var cfg = SetupData.Config(cat, 1);
            cfg.Levels.First(l => l.LevelUniqueId == "lvl-1").Selected = false;
            var p1 = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-101", p1.Rows.First(r => r.LevelUniqueId == "lvl-0").SheetNumber);
            Assert.Equal("E-102", p1.Rows.First(r => r.LevelUniqueId == "lvl-2").SheetNumber);
            cfg.Levels.First(l => l.LevelUniqueId == "lvl-1").Selected = true;   // level added in between
            var p2 = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-101", p2.Rows.First(r => r.LevelUniqueId == "lvl-0").SheetNumber, "existing rows keep their number");
            Assert.Equal("E-102", p2.Rows.First(r => r.LevelUniqueId == "lvl-2").SheetNumber);
            Assert.Equal("E-103", p2.Rows.First(r => r.LevelUniqueId == "lvl-1").SheetNumber, "new row takes the next free number");
            PlanBuilder.RegenerateSequences(cfg, cfg.SequenceAssignments.Keys.ToList());
            var p3 = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-102", p3.Rows.First(r => r.LevelUniqueId == "lvl-1").SheetNumber, "explicit regeneration follows level order");
        }

        [Test]
        public static void Sequences_ScopesAndIncrement()
        {
            var cat = SetupData.Catalog(2);
            var cfg = SetupData.Config(cat, 2);
            foreach (var r in cfg.ViewSets) { r.Sequence.Scope = SequenceScope.Global; r.Sequence.Start = 10; r.Sequence.Increment = 10; r.SheetNumberPattern = "S-{Sequence}"; }
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            var nums = plan.Rows.Select(r => r.SheetNumber).OrderBy(x => x).ToList();
            Assert.Equal("S-10,S-20,S-30,S-40", string.Join(",", nums));
            Assert.True(plan.CanRun);
        }

        [Test]
        public static void Collisions_ExistingAndProposed()
        {
            var cat = SetupData.Catalog(2);
            cat.Views.Add(new ExistingViewInfo { UniqueId = "v-old", Name = "E-GF-LIGHTING", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-0" });
            cat.Views.Add(new ExistingViewInfo { UniqueId = "v-rcp", Name = "E-L01-LIGHTING", ViewType = "CeilingPlan", Family = PlanFamily.CeilingPlan, LevelUniqueId = "lvl-1" });
            cat.Sheets.Add(new ExistingSheetInfo { UniqueId = "s-ph", Number = "E-201", Name = "placeholder", IsPlaceholder = true });
            cat.Sheets.Add(new ExistingSheetInfo { UniqueId = "s-x", Number = "X-1", Name = "GROUND FLOOR - POWER" });
            var cfg = SetupData.Config(cat, 2);
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            var gfL = plan.Rows.First(r => r.ViewName == "E-GF-LIGHTING");
            Assert.Contains("already used", gfL.Reason);
            Assert.False(plan.Rows.First(r => r.ViewName == "E-L01-LIGHTING").Reason.Contains("already used"), "same name in another view type is allowed");
            Assert.Contains("placeholder", plan.Rows.First(r => r.SheetNumber == "E-201").Reason);
            Assert.False(plan.Rows.First(r => r.SheetName == "GROUND FLOOR - POWER").Reason.Contains("X-1"), "sheet names may repeat");
            Assert.False(plan.CanRun);

            // proposed duplicates
            cfg.ViewSets[1].ViewNamePattern = "{DisciplineCode}-{LevelCode}-LIGHTING";
            cat.Views.Clear(); cat.Sheets.Clear();
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(plan.Rows.Count(r => r.Reason.Contains("proposed by 2 rows")) == 4);

            // fixing by row override
            cfg.EnsureOverride("set1|lvl-0").ViewName = "E-GF-POWER";
            cfg.EnsureOverride("set1|lvl-1").ViewName = "E-L01-POWER";
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(plan.CanRun, string.Join(" ", plan.Rows.SelectMany(r => r.Errors)));
        }

        [Test]
        public static void Renames_SwapBetweenExistingViewsIsAllowed()
        {
            var cat = SetupData.Catalog(1);
            var cfg = SetupData.Config(cat, 2);
            cfg.Mode = SetupMode.ViewsOnly;
            // Existing generated views carry each other's target names.
            cat.Views.Add(new ExistingViewInfo { UniqueId = "vA", Name = "E-GF-POWER", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-0", SetupRowKey = "set0|lvl-0", TemplateUniqueId = "tpl-ltg", TitleOnSheet = "Ground Floor - LIGHTING" });
            cat.Views.Add(new ExistingViewInfo { UniqueId = "vB", Name = "E-GF-LIGHTING", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-0", SetupRowKey = "set1|lvl-0", TemplateUniqueId = "tpl-pwr", TitleOnSheet = "Ground Floor - POWER" });
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(plan.CanRun, string.Join(" ", plan.Rows.SelectMany(r => r.Errors)));
            Assert.True(plan.Rows.All(r => r.ViewAction == ViewAction.Update && r.Updates.Contains("view:name")));
        }

        [Test]
        public static void Templates_IncompatibleFamilyIsAnError()
        {
            var cat = SetupData.Catalog(1);
            var cfg = SetupData.Config(cat, 1);
            cfg.ViewSets[0].TemplateUniqueId = "tpl-rcp";
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Contains("CeilingPlan template", plan.Rows[0].Reason);
            cfg.ViewSets[0].TemplateUniqueId = "gone";
            Assert.Contains("template not found", PlanBuilder.Build(cfg, cat, SetupData.Doc).Rows[0].Reason);
            cfg.ViewSets[0].TemplateUniqueId = "";
            cfg.ViewSets[0].Family = PlanFamily.StructuralPlan;
            Assert.Contains("Structural plans are not available", PlanBuilder.Build(cfg, cat, SetupData.Doc).Rows[0].Reason);
        }

        [Test]
        public static void Rerun_WithAssociations_CreatesNothing()
        {
            var cat = SetupData.Catalog(2);
            var cfg = SetupData.Config(cat, 2);
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            // Simulate what Run Setup leaves behind: views and sheets associated by row key, views placed.
            foreach (var r in plan.Rows)
            {
                cat.Views.Add(new ExistingViewInfo { UniqueId = "v-" + r.RowKey, Name = r.ViewName, TitleOnSheet = r.TitleOnSheet, ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = r.LevelUniqueId, TemplateUniqueId = r.TemplateUniqueId, SetupRowKey = r.RowKey, PlacedOnSheet = r.SheetNumber });
                cat.Sheets.Add(new ExistingSheetInfo { UniqueId = "s-" + r.RowKey, Number = r.SheetNumber, Name = r.SheetName, SetupRowKey = r.SheetKey, PlacedViewUniqueIds = { "v-" + r.RowKey } });
            }
            var again = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(0, again.ViewsToCreate);
            Assert.Equal(0, again.SheetsToCreate);
            Assert.Equal(0, again.Placements);
            Assert.True(again.Rows.All(r => r.PlaceAction == PlaceAction.Realign), "existing viewports are only verified");
            Assert.True(again.Rows.All(r => r.Status == RowStatus.Unchanged || r.Status == RowStatus.Warning), string.Join(" | ", again.Rows.Select(r => r.Status + ":" + r.Reason)));

            // Renaming a view in Revit is detected and proposed as an update, not as a new view.
            cat.Views[0].Name = "manually renamed";
            var third = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(ViewAction.Update, third.Rows.First(r => r.RowKey == cat.Views[0].SetupRowKey).ViewAction);
            Assert.Equal(0, third.ViewsToCreate);
        }

        [Test]
        public static void Placement_ViewOnOtherSheet_RequiresExplicitDependent()
        {
            var cat = SetupData.Catalog(1);
            var cfg = SetupData.Config(cat, 1);
            cat.Views.Add(new ExistingViewInfo { UniqueId = "v1", Name = "E-GF-LIGHTING", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-0", SetupRowKey = "set0|lvl-0", PlacedOnSheet = "OTHER-1" });
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Contains("already placed on sheet OTHER-1", plan.Rows[0].Reason);
            Assert.False(plan.CanRun);
            cfg.EnsureOverride("set0|lvl-0").PlaceAsDependent = true;
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(ViewAction.CreateDependent, plan.Rows[0].ViewAction);
            Assert.True(plan.CanRun, plan.Rows[0].Reason);
        }

        [Test]
        public static void Placement_AnchorSlotAndSharedSheets()
        {
            var cat = SetupData.Catalog(1);
            var cfg = SetupData.Config(cat, 2);
            cfg.Groups[0].Anchor = new ModelAnchor();
            Assert.Contains("no model anchor", PlanBuilder.Build(cfg, cat, SetupData.Doc).Rows[0].Reason);
            cfg.Groups[0].Anchor = new ModelAnchor { IsSet = true, X = 1, Y = 1, DocumentKey = "another-doc" };
            Assert.Contains("another document", PlanBuilder.Build(cfg, cat, SetupData.Doc).Rows[0].Reason);
            cfg.Groups[0].Anchor.DocumentKey = SetupData.Doc;

            // Both views on one shared sheet, same slot → overlap error; separate slots → ok.
            foreach (var r in cfg.ViewSets) { r.SheetMode = SheetMode.SharedBySheetNumber; r.SheetNumberPattern = "E-{LevelCode}"; r.SheetNamePattern = "{LevelName}"; }
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(1, plan.SheetsToCreate);
            Assert.True(plan.Rows.All(r => r.Reason.Contains("used by 2 rows")));
            cfg.Groups[0].Slots.Add(new SlotTarget { Name = "B", Xmm = 600, Ymm = 300 });
            cfg.ViewSets[1].SlotName = "B";
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(plan.CanRun, string.Join(" ", plan.Rows.SelectMany(r => r.Errors)));
            cfg.ViewSets[1].SlotName = "Z";
            Assert.Contains("Slot 'Z' is not defined", PlanBuilder.Build(cfg, cat, SetupData.Doc).Rows[1].Reason);
        }

        [Test]
        public static void Modes_ViewsOnly_SheetsOnly_PlaceExisting()
        {
            var cat = SetupData.Catalog(2);
            var cfg = SetupData.Config(cat, 1);
            cfg.Mode = SetupMode.ViewsOnly;
            var p = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(p.Rows.All(r => r.ViewAction == ViewAction.Create && r.SheetAction == SheetAction.None && r.PlaceAction == PlaceAction.None));
            cfg.Mode = SetupMode.SheetsOnly;
            p = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(p.Rows.All(r => r.ViewAction == ViewAction.None && r.SheetAction == SheetAction.Create && r.PlaceAction == PlaceAction.None));
            cfg.Mode = SetupMode.PlaceRealignExisting;
            cat.Views.Add(new ExistingViewInfo { UniqueId = "v1", Name = "Any plan", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-0" });
            cat.Sheets.Add(new ExistingSheetInfo { UniqueId = "s1", Number = "A-1", Name = "Sheet" });
            cfg.Placements.Add(new ExistingPlacement { ViewUniqueId = "v1", SheetUniqueId = "s1" });
            p = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(1, p.Rows.Count);
            Assert.Equal(PlaceAction.Place, p.Rows[0].PlaceAction);
            Assert.True(p.CanRun, p.Rows[0].Reason);
        }

        [Test]
        public static void Transforms_ComposeInvertAndAlign()
        {
            // model (feet) → projection: scale 1/100, rotated 30°, offset; projection → sheet: translation.
            var m2p = Affine2.Similarity(0.01, Math.PI / 6, 0.3, -0.2);
            var p2s = Affine2.Translation(1.2, 0.8);
            var m2s = p2s.Compose(m2p);
            var pt = new P2(10, 20);
            var direct = p2s.Apply(m2p.Apply(pt));
            Assert.Near(direct.X, m2s.Apply(pt).X, 1e-12, "composition order");
            Assert.Near(direct.Y, m2s.Apply(pt).Y, 1e-12);
            var back = m2s.Inverse().Apply(m2s.Apply(pt));
            Assert.Near(10, back.X, 1e-9); Assert.Near(20, back.Y, 1e-9);
            Assert.Near(0.01, m2s.ScaleFactor, 1e-12, "scale counted once");

            var target = new P2(AlignmentMath.MmToFeet(400), AlignmentMath.MmToFeet(300));
            var moved = Affine2.Translation(AlignmentMath.Delta(m2s, pt, target).X, AlignmentMath.Delta(m2s, pt, target).Y).Compose(m2s);
            Assert.True(AlignmentMath.Error(moved, pt, target) < 1e-12);

            // Title-block-relative target follows the title block on another sheet.
            var tbRef = Affine2.Translation(0, 0);
            var tbDest = Affine2.Translation(AlignmentMath.MmToFeet(50), AlignmentMath.MmToFeet(-10));
            var t2 = AlignmentMath.TransferBetweenTitleBlocks(target, tbRef, tbDest);
            Assert.Near(450, AlignmentMath.FeetToMm(t2.X), 1e-9);
            Assert.Near(290, AlignmentMath.FeetToMm(t2.Y), 1e-9);
        }

        private sealed class FakeViewport : IViewportProbe
        {
            public Affine2 ModelToProjection;
            public P2 Offset;
            public P2 CropCenterInProjection;
            public int Moves;
            public Affine2 MeasureModelToSheet() => Affine2.Translation(Offset.X, Offset.Y).Compose(ModelToProjection);
            public P2 BoxCenter() => CropCenterInProjection + Offset;
            public void Translate(P2 d) { Offset = Offset + d; Moves++; }
        }

        [Test]
        public static void AlignmentRunner_DifferentCropsMatchAnchor_ScaleDifferenceIsReported()
        {
            var anchor = new P2(10, 20);
            var target = new P2(AlignmentMath.MmToFeet(400), AlignmentMath.MmToFeet(300));
            double tol = AlignmentMath.MmToFeet(0.1);
            var lighting = new FakeViewport { ModelToProjection = Affine2.Similarity(0.01, 0, -0.4, 0.1), Offset = new P2(1, 1), CropCenterInProjection = new P2(0.2, 0.1) };
            var power = new FakeViewport { ModelToProjection = Affine2.Similarity(0.01, 0, -0.9, 0.6), Offset = new P2(1.3, 0.9), CropCenterInProjection = new P2(0.5, -0.2) };
            var r1 = AlignmentRunner.AlignAnchor(lighting, anchor, target, tol);
            var r2 = AlignmentRunner.AlignAnchor(power, anchor, target, tol, lighting.MeasureModelToSheet(), new P2(60, 20));
            Assert.Equal(AlignmentOutcome.Aligned, r1.Outcome);
            Assert.Equal(AlignmentOutcome.Aligned, r2.Outcome, "different crops: anchor and second point coincide");
            Assert.True(r2.ErrorMm < 0.1);

            // re-run does not move an aligned viewport
            int before = power.Moves;
            var again = AlignmentRunner.AlignAnchor(power, anchor, target, tol);
            Assert.False(again.Moved);
            Assert.Equal(before, power.Moves);

            var halfScale = new FakeViewport { ModelToProjection = Affine2.Similarity(0.02, 0, 0, 0), Offset = new P2(0, 0) };
            var r3 = AlignmentRunner.AlignAnchor(halfScale, anchor, target, tol, lighting.MeasureModelToSheet(), new P2(60, 20));
            Assert.Equal(AlignmentOutcome.AnchorAlignedDifferentScaleOrientation, r3.Outcome, "different scale is not a full overlay");
            var rotated = new FakeViewport { ModelToProjection = Affine2.Similarity(0.01, Math.PI / 2, 0, 0), Offset = new P2(0, 0) };
            Assert.Equal(AlignmentOutcome.AnchorAlignedDifferentScaleOrientation, AlignmentRunner.AlignAnchor(rotated, anchor, target, tol, lighting.MeasureModelToSheet()).Outcome);
        }

        [Test]
        public static void Profiles_RoundTripAndPortability()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-setup-" + Guid.NewGuid().ToString("N"));
            try
            {
                var cat = SetupData.Catalog(2);
                var cfg = SetupData.Config(cat, 2);
                PlanBuilder.Build(cfg, cat, SetupData.Doc);
                cfg.Name = "Electrical";
                var store = new SetupProfileStore(dir);
                store.Save(cfg);
                store.Duplicate("Electrical", "Copy");
                store.Rename("Copy", "Renamed");
                Assert.Equal(2, store.Names().Count);
                var loaded = store.Load("Electrical");
                Assert.Equal(4, loaded.SequenceAssignments.Count, "sequence assignments survive");
                Assert.Equal("E-{LevelCode}".Length > 0, true);
                Assert.Equal(cfg.ViewSets[1].SheetNumberPattern, loaded.ViewSets[1].SheetNumberPattern);

                // Another document: different level ids, no templates, anchor from elsewhere.
                var other = SetupData.Catalog(2);
                other.Levels[1].UniqueId = "other-level";
                other.Templates.Clear();
                var report = ProfilePortability.Prepare(loaded, other, "doc-key-2");
                Assert.False(report.Complete);
                Assert.True(report.MissingLevels.Contains("Level 01"));
                Assert.Equal(2, report.MissingTemplates.Count);
                Assert.True(report.AnchorsReset.Contains("Main"), "anchors are never reused across documents");
                Assert.False(loaded.Groups[0].Anchor.IsSet);
                Assert.Equal(0, loaded.SequenceAssignments.Count);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void Performance_10Levels_x_4Sets_Planning()
        {
            var cat = SetupData.Catalog(10);
            for (int i = 0; i < 2000; i++) cat.Views.Add(new ExistingViewInfo { UniqueId = "x" + i, Name = "Existing " + i, ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-" + (i % 10) });
            for (int i = 0; i < 500; i++) cat.Sheets.Add(new ExistingSheetInfo { UniqueId = "s" + i, Number = "X-" + i, Name = "S" + i });
            var cfg = SetupData.Config(cat, 4);
            var sw = Stopwatch.StartNew();
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            long ms = sw.ElapsedMilliseconds;
            Console.WriteLine("        10 levels × 4 sets with 2000 views / 500 sheets: plan built in " + ms + " ms");
            Assert.Equal(40, plan.Rows.Count);
            Assert.True(plan.CanRun, string.Join(" ", plan.Rows.SelectMany(r => r.Errors).Take(3)));
            Assert.Equal(40, plan.Rows.Select(r => r.SheetNumber).Distinct().Count());
        }
    }
}
