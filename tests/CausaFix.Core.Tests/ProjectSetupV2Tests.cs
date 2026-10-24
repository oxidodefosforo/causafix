using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Geometry;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
using CausaFix.ProjectSetup.Planning;
using CausaFix.ProjectSetup.Profiles;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Core.Tests
{
    internal static class V2Data
    {
        public static readonly ParamRef LevelCodeShared = new ParamRef { Scope = ParamScope.Level, Kind = ParamKind.Shared, Guid = "11111111-1111-1111-1111-111111111111", Name = "Level Code" };
        /// <summary>Homonym: same visible name, another shared parameter (other GUID) with other values.</summary>
        public static readonly ParamRef LevelCodeOther = new ParamRef { Scope = ParamScope.Level, Kind = ParamKind.Shared, Guid = "22222222-2222-2222-2222-222222222222", Name = "Level Code" };
        public static readonly ParamRef ClientCode = new ParamRef { Scope = ParamScope.ProjectInformation, Kind = ParamKind.Shared, Guid = "33333333-3333-3333-3333-333333333333", Name = "Client Code" };
        public static readonly ParamRef ViewPhaseText = new ParamRef { Scope = ParamScope.View, Kind = ParamKind.Project, ElementUniqueId = "pe-phase-text", Name = "Drawing Status" };

        public static SetupCatalog Catalog(int levels = 2)
        {
            var c = SetupData.Catalog(levels);
            c.Parameters.Add(new ParamInfo { Ref = LevelCodeShared, Name = "Level Code", IsReadOnly = true, StorageType = "String" });
            c.Parameters.Add(new ParamInfo { Ref = LevelCodeOther, Name = "Level Code", StorageType = "String" });
            c.Parameters.Add(new ParamInfo { Ref = ClientCode, Name = "Client Code", StorageType = "String" });
            c.Parameters.Add(new ParamInfo { Ref = ViewPhaseText, Name = "Drawing Status", StorageType = "String" });
            for (int i = 0; i < c.Levels.Count; i++)
            {
                c.Levels[i].Values[LevelCodeShared.IdentityKey] = i == 0 ? "00" : (i * 10).ToString("000");
                c.Levels[i].Values[LevelCodeOther.IdentityKey] = "WRONG" + i;
            }
            c.ProjectInfoValues[ClientCode.IdentityKey] = "ACME";
            c.ViewportTypes.Add(new NamedRef { UniqueId = "vpt-title", Name = "Title w Line", ShowTitleText = "Yes", ShowTitleValue = 1 });
            c.ViewportTypes.Add(new NamedRef { UniqueId = "vpt-none", Name = "No Title", ShowTitleText = "No", ShowTitleValue = 0 });
            return c;
        }

        public static NumberingProfile Profile(SetupConfig cfg, string name, params NameComponent[] comps)
        {
            var p = new NumberingProfile { Name = name, Rule = NameRule.Of(comps), Start = 1, GroupBy = new List<NameField> { NameField.Discipline, NameField.Series } };
            cfg.NumberingProfiles.Add(p);
            return p;
        }

        public static NameComponent Seq(int pad = 2, string sep = "") { var c = NameComponent.OfField(NameField.Sequence, sep); c.PadDigits = pad; return c; }

        /// <summary>Standard numbering (optional prefix, zone and condition) as offered by the add-in.</summary>
        public static NumberingProfile Standard(SetupConfig cfg)
        {
            var cond = NameComponent.OfField(NameField.Condition, "-", required: false);
            cond.Mappings.Add(new ValueMapping { From = "Proposed", To = "P" });
            cond.Mappings.Add(new ValueMapping { From = "Demolished", To = "D" });
            cond.Mappings.Add(new ValueMapping { From = "Existing", To = "E" });
            return Profile(cfg, "Standard",
                NameComponent.OfField(NameField.Purpose, "", required: false), NameComponent.OfField(NameField.Discipline, "-"),
                NameComponent.OfField(NameField.Zone, "-", required: false), cond, NameComponent.OfField(NameField.Series, "-"), Seq());
        }

        /// <summary>Simulates what Run Setup leaves behind: views, sheets (associated by key) and viewports.</summary>
        public static void Commit(SetupPlan plan, SetupCatalog cat)
        {
            foreach (var r in plan.Included)
            {
                string vid = "v-" + r.RowKey;
                if (r.ViewAction == ViewAction.Create)
                    cat.Views.Add(new ExistingViewInfo { UniqueId = vid, Name = r.ViewName, TitleOnSheet = r.TitleOnSheet, ViewType = r.Family.ToString(), Family = r.Family, LevelUniqueId = r.LevelUniqueId, TemplateUniqueId = r.TemplateUniqueId, SetupRowKey = r.RowKey, Scale = r.EffectiveScale });
                else if (!string.IsNullOrEmpty(r.ExistingViewUniqueId)) vid = r.ExistingViewUniqueId;
                var sheet = cat.Sheets.FirstOrDefault(s => s.SetupRowKey == r.SheetKey);
                if (r.SheetAction == SheetAction.Create && sheet == null)
                    cat.Sheets.Add(sheet = new ExistingSheetInfo { UniqueId = "s-" + r.SheetKey, Number = r.SheetNumber, Name = r.SheetName, SetupRowKey = r.SheetKey, TitleBlockCount = 1 });
                if (r.PlaceAction == PlaceAction.Place && sheet != null)
                {
                    sheet.PlacedViewUniqueIds.Add(vid);
                    var view = cat.View(vid);
                    view.PlacedOnSheet = sheet.Number;
                    cat.Viewports.Add(new ExistingViewportInfo { UniqueId = "vp-" + r.RowKey, ViewUniqueId = vid, SheetUniqueId = sheet.UniqueId, TypeUniqueId = r.ViewportTypeUniqueId, BoxMm = new[] { 100.0, 100, 500, 400 }, LabelMm = new[] { 100.0, 90, 160, 98 } });
                }
            }
        }
    }

    public static class ProjectSetupV2Tests
    {
        private static string Errors(SetupPlan p) => string.Join(" | ", p.GlobalErrors.Concat(p.Rows.SelectMany(r => r.Errors)));

        // ------------------------------------------------------------------ A. names with real parameters

        [Test]
        public static void A_NameBuilder_SharedParameterHomonymsAndIndependentTargets()
        {
            var cat = V2Data.Catalog(2);
            var cfg = SetupData.Config(cat, 1);
            var r = cfg.ViewSets[0];
            r.ViewNameRule = NameRule.Of(NameComponent.OfParam(V2Data.LevelCodeShared), NameComponent.OfField(NameField.ViewSetName, "-"));
            r.TitleRule = NameRule.Of(NameComponent.OfField(NameField.LevelName), NameComponent.OfField(NameField.ViewSetName, " – "));
            r.SheetNameRule = NameRule.Of(NameComponent.OfParam(V2Data.ClientCode), NameComponent.OfField(NameField.LevelCode, " "), NameComponent.OfField(NameField.ViewSetName, " "));
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            var gf = plan.Rows.First(x => x.LevelUniqueId == "lvl-0");
            var l1 = plan.Rows.First(x => x.LevelUniqueId == "lvl-1");
            Assert.Equal("00-LIGHTING", gf.ViewName, "text code keeps its leading zeros");
            Assert.Equal("010-LIGHTING", l1.ViewName, "the shared parameter is read by GUID, not by name");
            Assert.False(l1.ViewName.Contains("WRONG"), "homonym parameter not used");
            Assert.Equal("Level 01 – LIGHTING", l1.TitleOnSheet);
            Assert.Equal("ACME L01 LIGHTING", l1.SheetName);
            Assert.True(plan.CanRun, Errors(plan));

            // Reordering the View Name changes only the View Name.
            (r.ViewNameRule.Components[0], r.ViewNameRule.Components[1]) = (r.ViewNameRule.Components[1], r.ViewNameRule.Components[0]);
            r.ViewNameRule.Components[0].Separator = ""; r.ViewNameRule.Components[1].Separator = "_";
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            l1 = plan.Rows.First(x => x.LevelUniqueId == "lvl-1");
            Assert.Equal("LIGHTING_010", l1.ViewName);
            Assert.Equal("Level 01 – LIGHTING", l1.TitleOnSheet, "other targets unchanged");

            // Use same rule
            r.TitleRule.SameAs = NameContext.ViewName;
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("LIGHTING_010", plan.Rows.First(x => x.LevelUniqueId == "lvl-1").TitleOnSheet);
            // Read-only parameter is a valid source (readable ≠ writable).
            Assert.True(cat.Param(V2Data.LevelCodeShared).IsReadOnly);
        }

        [Test]
        public static void A_NameBuilder_CircularDependencyIsAnError()
        {
            var cat = V2Data.Catalog(1);
            var cfg = SetupData.Config(cat, 1);
            var r = cfg.ViewSets[0];
            r.ViewNameRule = NameRule.Of(NameComponent.OfField(NameField.SheetName), NameComponent.OfField(NameField.LevelCode, "-"));
            r.SheetNameRule = NameRule.Of(NameComponent.OfField(NameField.ViewName), NameComponent.OfText(" sheet"));
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.False(plan.CanRun);
            Assert.Contains("Circular reference", plan.Rows[0].Reason);
            Assert.Contains("View Name", plan.Rows[0].Reason);
            // A valid dependency works: Sheet Name built from the View Name.
            r.ViewNameRule = NameRule.Of(NameComponent.OfField(NameField.Discipline), NameComponent.OfField(NameField.LevelCode, "-"));
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-GF sheet", plan.Rows[0].SheetName, Errors(plan));
        }

        [Test]
        public static void A_NameBuilder_OptionalComponentsDropTheirSeparator()
        {
            var cat = V2Data.Catalog(2);
            var cfg = SetupData.Config(cat, 1);
            cfg.ViewSets[0].Name = "POWER";
            cfg.ViewSets[0].ViewNameRule = NameRule.Of(NameComponent.OfField(NameField.Discipline), NameComponent.OfField(NameField.Zone, "-", required: false),
                                                       NameComponent.OfField(NameField.LevelCode, "-"), NameComponent.OfField(NameField.ViewSetName, "-"));
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-L01-POWER", plan.Rows.First(x => x.LevelUniqueId == "lvl-1").ViewName, "no E--L01");
            cfg.Levels[1].Zone = "A-1";
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-A-1-L01-POWER", plan.Rows.First(x => x.LevelUniqueId == "lvl-1").ViewName, "hyphens inside a value are kept");
            // A disabled component is omitted too.
            cfg.ViewSets[0].ViewNameRule.Components[1].Enabled = false;
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-L01-POWER", plan.Rows.First(x => x.LevelUniqueId == "lvl-1").ViewName);
            // Required and empty: error naming the source, not a silent blank.
            cfg.ViewSets[0].ViewNameRule.Components[1].Enabled = true;
            cfg.ViewSets[0].ViewNameRule.Components[1].Required = true;
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            var gf = plan.Rows.First(x => x.LevelUniqueId == "lvl-0");
            Assert.Contains("required component Zone has no value", gf.Reason);
            Assert.Equal(RowStatus.Error, gf.Status);
        }

        [Test]
        public static void A_NameBuilder_UnknownValuesOfPlannedElements()
        {
            var cat = V2Data.Catalog(1);
            var cfg = SetupData.Config(cat, 1);
            var status = NameComponent.OfParam(V2Data.ViewPhaseText, " ");
            cfg.ViewSets[0].TitleRule = NameRule.Of(NameComponent.OfField(NameField.LevelName), status);
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Contains("created by this run", plan.Rows[0].Reason, "a view that does not exist has no parameter values yet");
            status.Fallback = "FOR REVIEW";
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("Ground Floor FOR REVIEW", plan.Rows[0].TitleOnSheet, "explicit fallback");
            Assert.True(plan.Rows[0].Names[NameContext.TitleOnSheet].Parts.Any(p => p.State == PartState.Fallback));
            // Existing (reused) view: its value is used once read.
            status.Fallback = null;
            cat.Views.Add(new ExistingViewInfo { UniqueId = "v1", Name = "old", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-0", SetupRowKey = "set0|lvl-0" });
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Contains("not read yet", plan.Rows[0].Reason);
            cat.Merge(new ParamValueSet { Keys = { V2Data.ViewPhaseText.Key }, Views = { ["v1"] = new Dictionary<string, string> { [V2Data.ViewPhaseText.IdentityKey] = "ISSUED" } } });
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("Ground Floor ISSUED", plan.Rows[0].TitleOnSheet);
            // A project parameter from another document is reported, never matched by name.
            cat.Parameters.RemoveAll(p => p.Ref.Key == V2Data.ViewPhaseText.Key);
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Contains("does not exist in this document", plan.Rows[0].Reason);
        }

        [Test]
        public static void A_Format_PadMappingCaseElevation()
        {
            Assert.Equal("007", NameEngine.Pad("7", 3));
            Assert.Equal("007", NameEngine.Pad("007", 2), "leading zeros of codes kept");
            Assert.Equal("L7", NameEngine.Pad("L7", 3), "text codes are never padded");
            Assert.Equal("3500", NameEngine.FormatElevation(3500 / 304.8, ElevationUnit.Millimeters, 0));
            Assert.Equal("3.50", NameEngine.FormatElevation(3500 / 304.8, ElevationUnit.Meters, 2));
            Assert.Equal("-1.2", NameEngine.FormatElevation(-1200 / 304.8, ElevationUnit.Meters, 1));
            var c = new ComponentItem(NameComponent.OfField(NameField.Condition), "", () => { });
            c.MappingsText = "Proposed=P; Demolished = D ;Existing=E";
            Assert.Equal(3, c.Component.Mappings.Count);
            Assert.Equal("D", c.Component.Mappings[1].To);
        }

        [Test]
        public static void A_LegacyPatterns_ConvertWithoutChangingMeaning()
        {
            var v = new TokenValues { ProjectNumber = "P1", DisciplineCode = "E", ViewSetCode = "LTG", ViewSetName = "Lighting", LevelName = "Level 01", LevelCode = "L01", Zone = "A", Sequence = 7 };
            string[] patterns = { "{DisciplineCode}-{LevelCode}-{ViewSetName}", "{DisciplineCode}-1{Sequence:00}", "{LevelName:upper} - {ViewSetName}", "X{{1}} {Zone:lower}/{LevelCode} end", "{ProjectNumber}_{ViewSetCode}" };
            foreach (var pat in patterns)
            {
                var rule = LegacyPatternConverter.Convert(pat, TextCase.Upper);
                Assert.Equal(NameRuleMode.Builder, rule.Mode, pat);
                string legacy = NamePattern.Expand(pat, v, NameContext.TitleOnSheet, TextCase.Upper).Text;
                var src = new StubSource(v) { Rule = rule };
                string now = new NameEngine(src).Evaluate(NameContext.TitleOnSheet).Text;
                Assert.Equal(legacy, now, pat);
            }
            var bad = LegacyPatternConverter.Convert("{LevelCode:000}-{Unknown}", TextCase.AsTyped);
            Assert.Equal(NameRuleMode.Advanced, bad.Mode, "kept as Advanced");
            Assert.Contains("cannot be converted", bad.LegacyNote);
        }

        private sealed class StubSource : INameSource
        {
            private readonly TokenValues _v;
            public StubSource(TokenValues v) { _v = v; }
            public NameRule Rule;
            public SourceValue Field(NameField f, NameComponent c)
            {
                switch (f)
                {
                    case NameField.ProjectNumber: return SourceValue.Of(_v.ProjectNumber);
                    case NameField.Discipline: return SourceValue.Of(_v.DisciplineCode);
                    case NameField.ViewSetCode: return SourceValue.Of(_v.ViewSetCode);
                    case NameField.ViewSetName: return SourceValue.Of(_v.ViewSetName);
                    case NameField.LevelName: return SourceValue.Of(_v.LevelName);
                    case NameField.LevelCode: return SourceValue.Of(_v.LevelCode);
                    case NameField.Zone: return SourceValue.Of(_v.Zone);
                    case NameField.Sequence: return SourceValue.Of(_v.Sequence?.ToString());
                    default: return SourceValue.Of("");
                }
            }
            public SourceValue Param(ParamRef p) => SourceValue.Unknown("n/a");
            public NameRule RuleFor(NameContext t) => Rule;
            public string FixedValue(NameContext t) => null;
            public TokenValues Tokens() => _v;
        }

        // ------------------------------------------------------------------ B. numbering

        [Test]
        public static void B_Numbering_OptionalSegments_NoDanglingSeparators()
        {
            var cat = V2Data.Catalog(1);
            var cfg = SetupData.Config(cat, 1);
            var std = V2Data.Standard(cfg);
            var r = cfg.ViewSets[0];
            r.NumberingProfileKey = std.Key;
            r.Series = "1";
            string Num() { var p = PlanBuilder.Build(cfg, cat, SetupData.Doc); Assert.True(p.CanRun, Errors(p)); return p.Rows[0].SheetNumber; }
            Assert.Equal("E-101", Num());
            cfg.Levels[0].Zone = "A";
            Assert.Equal("E-A-101", Num());
            r.Condition = "Proposed";
            Assert.Equal("E-A-P-101", Num(), "condition mapped Proposed → P");
            r.Purpose = "FCC";
            Assert.Equal("FCC-E-A-P-101", Num());
            cfg.Levels[0].Zone = "";
            Assert.Equal("FCC-E-P-101", Num(), "zone omitted with its separator");
            r.Purpose = "QA";
            Assert.Equal("QA-E-P-101", Num(), "any prefix, not only FCC");
            Assert.Equal(1, cfg.SequenceAssignments.Count, "one sheet, one sequence – changing segments never consumed numbers");
        }

        [Test]
        public static void B_Numbering_ConditionCodeDoesNotTouchPhases()
        {
            // The documentary condition is a CausaFix field: nothing in the plan writes a phase or phase filter.
            var cat = V2Data.Catalog(1);
            var cfg = SetupData.Config(cat, 1);
            cfg.ViewSets[0].NumberingProfileKey = V2Data.Standard(cfg).Key;
            cfg.ViewSets[0].Series = "1";
            cfg.ViewSets[0].Condition = "Demolished";
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal("E-D-101", plan.Rows[0].SheetNumber);
            Assert.False(plan.Rows[0].Updates.Any(u => u.IndexOf("phase", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public static void B_E_ExistingNumbersKeptUntilRenumberApproved_SameSheet()
        {
            var cat = V2Data.Catalog(2);
            var cfg = SetupData.Config(cat, 1);
            cfg.ViewSets[0].NumberingProfileKey = V2Data.Standard(cfg).Key;
            cfg.ViewSets[0].Series = "1";
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            V2Data.Commit(plan, cat);
            Assert.Equal(2, cat.Sheets.Count);

            cfg.ViewSets[0].Purpose = "FCC";              // prefix changed
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            var gf = plan.Rows.First(r => r.LevelUniqueId == "lvl-0");
            Assert.Equal(0, plan.SheetsToCreate, "same sheet: changing a prefix never creates another sheet");
            Assert.True(gf.RenumberPending);
            Assert.Equal("E-101", gf.SheetNumber, "the number stays until approved");
            Assert.Equal("FCC-E-101", gf.ProposedSheetNumber);
            Assert.False(gf.Updates.Contains("sheet:number"));

            cfg.ApprovedSheetNumbers[gf.SheetKey] = gf.ProposedSheetNumber;   // explicit approval
            plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            gf = plan.Rows.First(r => r.LevelUniqueId == "lvl-0");
            Assert.Equal("FCC-E-101", gf.SheetNumber);
            Assert.True(gf.Updates.Contains("sheet:number"));
            Assert.Equal(SheetAction.Update, gf.SheetAction);
            Assert.Equal(0, plan.SheetsToCreate);
            Assert.True(plan.Rows.First(r => r.LevelUniqueId == "lvl-1").RenumberPending, "other sheets still pending");
        }

        [Test]
        public static void E_TwoDifferentSheetsWithTheSameNumberAreAConflict()
        {
            var cat = V2Data.Catalog(1);
            var cfg = SetupData.Config(cat, 2);
            var p = V2Data.Profile(cfg, "Discipline only", NameComponent.OfField(NameField.Discipline), NameComponent.OfText("-100"));
            foreach (var r in cfg.ViewSets) r.NumberingProfileKey = p.Key;
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.False(plan.CanRun);
            Assert.True(plan.Rows.All(r => r.Reason.Contains("proposed for 2 different sheets")), Errors(plan));
            Assert.Equal(2, plan.SheetsToCreate, "never merged into one sheet");
        }

        [Test]
        public static void B_Sequences_PerSheet_GroupBy_AndExplicitRegeneration()
        {
            var cat = V2Data.Catalog(3);
            var cfg = SetupData.Config(cat, 2);
            var p = V2Data.Profile(cfg, "Global", NameComponent.OfText("S"), V2Data.Seq(3, "-"));
            p.GroupBy.Clear();
            p.Start = 10; p.Increment = 5; p.Order = SequenceOrder.LevelThenViewSet;
            foreach (var r in cfg.ViewSets) r.NumberingProfileKey = p.Key;
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            var nums = plan.Rows.OrderBy(r => r.Sequence).Select(r => r.LevelName + ":" + r.ViewSetName + "=" + r.SheetNumber).ToList();
            Assert.Equal("Ground Floor:LIGHTING=S-010", nums[0]);
            Assert.Equal("Ground Floor:POWER=S-015", nums[1], "level then View Set order");
            Assert.Equal(6, plan.Rows.Select(r => r.SheetNumber).Distinct().Count());
            // Deselecting a level keeps its numbers reserved; reselecting gives them back.
            cfg.Levels[1].Selected = false;
            PlanBuilder.Build(cfg, cat, SetupData.Doc);
            cfg.Levels.Add(new LevelSelection { LevelUniqueId = "lvl-x", LevelName = "x", Selected = false });
            cfg.Levels[1].Selected = true;
            var again = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(string.Join(",", plan.Rows.Select(r => r.SheetNumber)), string.Join(",", again.Rows.Select(r => r.SheetNumber)));
            // Per-level counter
            p.GroupBy.Add(NameField.LevelName);
            PlanBuilder.RegenerateSequences(cfg, cfg.SequenceAssignments.Keys.ToList());
            var perLevel = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(3, perLevel.Rows.Count(r => r.SheetNumber == "S-010"), "each level restarts – which is a conflict here (shown, not merged)");
            Assert.False(perLevel.CanRun);
        }

        // ------------------------------------------------------------------ C. overlay plan + RCP

        private static SetupConfig OverlayConfig(SetupCatalog cat, out SheetComposition comp)
        {
            var cfg = SetupData.Config(cat, 2);
            cfg.ViewSets[1].Name = "RCP"; cfg.ViewSets[1].Code = "RCP";
            cfg.ViewSets[1].Family = PlanFamily.CeilingPlan; cfg.ViewSets[1].ViewFamilyTypeUniqueId = "vft-rcp"; cfg.ViewSets[1].TemplateUniqueId = "tpl-rcp";
            cfg.ViewSets[0].TemplateUniqueId = "tpl-ltg";
            comp = new SheetComposition { Name = "Lighting + RCP", Mode = CompositionMode.Overlay, HiddenTitleViewportTypeUniqueId = "vpt-none" };
            var a = new CompositionMember { ViewSetKey = "set0" };
            var b = new CompositionMember { ViewSetKey = "set1" };
            comp.Members.Add(a); comp.Members.Add(b);
            comp.PrimaryMemberId = a.Id;
            cfg.Compositions.Add(comp);
            return cfg;
        }

        [Test]
        public static void C_Overlay_PlanAndRcp_OneSheet_OneNumber_OnlyPrimaryTitle()
        {
            var cat = V2Data.Catalog(2);
            var cfg = OverlayConfig(cat, out var comp);
            cfg.Title = new TitlePlacement { Mode = TitleMode.RelativeToViewport, Anchor = TitleAnchor.BottomLeft, OffsetYmm = -10 };
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(plan.CanRun, Errors(plan));
            Assert.Equal(4, plan.Rows.Count);
            Assert.Equal(2, plan.SheetsToCreate, "one sheet per level, not one per view");
            Assert.Equal(2, cfg.SequenceAssignments.Count(k => k.Key.StartsWith("cmp|", StringComparison.Ordinal)), "the sheet is the unit of numbering");
            foreach (var g in plan.Rows.GroupBy(r => r.LevelUniqueId))
            {
                var fp = g.First(r => r.Family == PlanFamily.FloorPlan);
                var rcp = g.First(r => r.Family == PlanFamily.CeilingPlan);
                Assert.Equal(fp.SheetKey, rcp.SheetKey);
                Assert.Equal(fp.SheetNumber, rcp.SheetNumber);
                Assert.Equal(fp.OverlayGroupId, rcp.OverlayGroupId);
                Assert.Equal(fp.SlotName, rcp.SlotName);
                Assert.Equal(MemberRole.Primary, fp.Role);
                Assert.Equal("tpl-rcp", rcp.TemplateUniqueId, "each member keeps its own template");
                Assert.True(fp.ShowsTitle && fp.TitleAction == TitleAction.Apply, "only the primary shows a title");
                Assert.False(rcp.ShowsTitle);
                Assert.Equal(TitleAction.None, rcp.TitleAction);
                Assert.Equal("vpt-none", rcp.ViewportTypeUniqueId, "title hidden through the chosen viewport type");
            }
            // Without the hidden-title type the plan says what to choose.
            comp.HiddenTitleViewportTypeUniqueId = "";
            Assert.Contains("viewport type without title", Errors(PlanBuilder.Build(cfg, cat, SetupData.Doc)));
            comp.HiddenTitleViewportTypeUniqueId = "vpt-none";

            // Adding the RCP to an existing sheet consumes no number and creates no sheet.
            cfg.Compositions[0].Members.RemoveAt(1);
            cfg.ViewSets[1].Enabled = false;
            var p1 = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            V2Data.Commit(p1, cat);
            int seqs = cfg.SequenceAssignments.Count;
            var nums = p1.Rows.Where(r => r.CompositionKey != "").Select(r => r.SheetNumber).ToList();
            cfg.Compositions[0].Members.Add(new CompositionMember { ViewSetKey = "set1" });
            cfg.ViewSets[1].Enabled = true;
            var p2 = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(p2.CanRun, Errors(p2));
            Assert.Equal(0, p2.SheetsToCreate);
            Assert.Equal(seqs, cfg.SequenceAssignments.Count);
            Assert.Equal(string.Join(",", nums), string.Join(",", p2.Rows.Where(r => r.Family == PlanFamily.FloorPlan).Select(r => r.SheetNumber)));
            Assert.Equal(2, p2.Placements, "only the two RCPs are placed");
            Assert.Equal(2, p2.ViewsToCreate);
        }

        [Test]
        public static void C_Overlay_ChecksScaleAndKeepsAccidentalOverlapDetection()
        {
            var cat = V2Data.Catalog(1);
            var cfg = OverlayConfig(cat, out var comp);
            cfg.ViewSets[0].TemplateUniqueId = "tpl-pwr";   // 1:50 controlled by the template; RCP 1:100
            Assert.Contains("same scale", Errors(PlanBuilder.Build(cfg, cat, SetupData.Doc)));
            cfg.ViewSets[0].TemplateUniqueId = "tpl-ltg";
            cfg.Groups[0].Method = AlignmentMethod.ViewportCenter;
            Assert.Contains("Overlay needs Model Anchor alignment", Errors(PlanBuilder.Build(cfg, cat, SetupData.Doc)));
            cfg.Groups[0].Method = AlignmentMethod.ModelAnchor;
            Assert.True(PlanBuilder.Build(cfg, cat, SetupData.Doc).CanRun);

            // Separate mode in the same slot is an accidental overlap.
            comp.Mode = CompositionMode.Separate;
            Assert.Contains("are not one overlay", Errors(PlanBuilder.Build(cfg, cat, SetupData.Doc)));
            cfg.Groups[0].Slots.Add(new SlotTarget { Name = "B", Xmm = 600, Ymm = 300 });
            comp.Members[1].SlotName = "B";
            Assert.True(PlanBuilder.Build(cfg, cat, SetupData.Doc).CanRun, "separate slots");

            // Two unrelated rows explicitly on the same existing sheet and slot still collide.
            var cfg2 = SetupData.Config(cat, 2);
            cat.Sheets.Add(new ExistingSheetInfo { UniqueId = "s-x", Number = "X-1", Name = "Existing" });
            foreach (var r in cfg2.ViewSets) cfg2.EnsureOverride(PlanBuilder.RowKey(r.Key, "lvl-0")).ExistingSheetUniqueId = "s-x";
            var p = PlanBuilder.Build(cfg2, cat, SetupData.Doc);
            Assert.Contains("are not one overlay", Errors(p));
        }

        [Test]
        public static void C_Overlay_FloorAndCeilingPlansCoincideWithDifferentCrops()
        {
            // Floor plan and RCP: same scale; projected results coincide on paper although the crops (projection
            // offsets) and boxes differ. The check compares the projected 2D transforms, not view directions.
            var anchor = new P2(10, 20);
            var second = new P2(60, 35);
            var target = new P2(AlignmentMath.MmToFeet(400), AlignmentMath.MmToFeet(300));
            double tol = AlignmentMath.MmToFeet(0.1);
            var fp = new Fake { M2P = Affine2.Similarity(0.01, 0, -0.4, 0.1), Offset = new P2(1, 1) };
            var rcp = new Fake { M2P = Affine2.Similarity(0.01, 0, 0.7, -0.3), Offset = new P2(0.2, 1.6) };
            var r1 = AlignmentRunner.AlignAnchor(fp, anchor, target, tol);
            var r2 = AlignmentRunner.AlignAnchor(rcp, anchor, target, tol, fp.MeasureModelToSheet(), second);
            Assert.Equal(AlignmentOutcome.Aligned, r1.Outcome);
            Assert.Equal(AlignmentOutcome.Aligned, r2.Outcome);
            Assert.True(r2.ErrorMm < 0.1 && AlignmentMath.FeetToMm(r2.SecondErrorFeet) < 0.1, "anchor and second reference coincide");
            var mirrored = new Fake { M2P = new Affine2(-0.01, 0, 0, 0.01, 0, 0), Offset = new P2(0, 0) };
            Assert.Equal(AlignmentOutcome.AnchorAlignedDifferentScaleOrientation, AlignmentRunner.AlignAnchor(mirrored, anchor, target, tol, fp.MeasureModelToSheet(), second).Outcome,
                         "a projection that would not superimpose on paper is detected");
        }

        private sealed class Fake : IViewportProbe
        {
            public Affine2 M2P; public P2 Offset;
            public Affine2 MeasureModelToSheet() => Affine2.Translation(Offset.X, Offset.Y).Compose(M2P);
            public P2 BoxCenter() => Offset;
            public void Translate(P2 d) => Offset = Offset + d;
        }

        // ------------------------------------------------------------------ D. view title placement

        /// <summary>
        /// Emulates Revit's title: LabelOffset is measured in the unrotated viewport frame from the box's lower-left corner
        /// to the start of the title line; the family draws the text at a fixed delta from the line start; the whole
        /// title rotates with the viewport about the box centre. The box (drawing) never moves.
        /// </summary>
        private sealed class FakeTitle : ITitleProbe
        {
            public Box2 Box = new Box2(100, 100, 500, 400);
            public int Quarter;
            public P2 FamilyDelta = new P2(2, -1.5);      // text corner relative to the line start (family-specific)
            public double TextW = 55, TextH = 7;
            public P2 Offset = new P2(0, -10);
            public double Line = 60;
            public int Sets;
            public Box2 ViewportBox() => Box;
            public Box2? LabelBox()
            {
                var c = Box.Center;
                var local = TitleGeometry.ToLocal(Box, c, Quarter);
                var corner = local.Min + Offset + FamilyDelta;
                double w = Math.Max(TextW, Math.Abs(Line));
                var lb = new Box2(corner.X, corner.Y, corner.X + w, corner.Y + TextH);
                return TitleGeometry.Rotate(lb, c, Quarter);
            }
            public P2 LabelOffset { get => Offset; set { Offset = value; Sets++; } }
            public double LineLength { get => Line; set => Line = value; }
        }

        [Test]
        public static void D_Title_RelativeFixedRotatedAndReference()
        {
            var t = new TitlePlacement { Mode = TitleMode.RelativeToViewport, Anchor = TitleAnchor.BottomLeft, OffsetXmm = 0, OffsetYmm = -12 };
            var fake = new FakeTitle();
            var boxBefore = fake.Box;
            var rep = TitleRunner.Place(fake, t, null, default(P2), 0, 0, 0, 0.1);
            Assert.True(rep.Placed, rep.Message);
            var lb = fake.LabelBox().Value;
            Assert.Near(100, lb.MinX, 1e-6, "title left corner = viewport left");
            Assert.Near(88, lb.MinY, 1e-6, "12 mm below the viewport");
            Assert.True(fake.Box.Equals(boxBefore), "the drawing did not move");

            // Line length and direction are applied first (they change the outline).
            t.LineMode = TitleLineMode.Set; t.LineLengthMm = 90;
            TitleRunner.Place(fake, t, null, default(P2), 0, 0, 0, 0.1);
            Assert.Near(90, fake.Line, 1e-9);

            // Bottom-right with an offset.
            t.Anchor = TitleAnchor.BottomRight; t.OffsetXmm = -5; t.OffsetYmm = -8;
            Assert.True(TitleRunner.Place(fake, t, null, default(P2), 0, 0, 0, 0.1).Placed);
            Assert.Near(495, fake.LabelBox().Value.MaxX, 1e-6);

            // Rotated viewport (counterclockwise): placement happens in the viewport frame and is verified in sheet space.
            var rot = new FakeTitle { Quarter = 1 };
            var t2 = new TitlePlacement { Mode = TitleMode.RelativeToViewport, Anchor = TitleAnchor.BottomLeft, OffsetYmm = -12 };
            var r2 = TitleRunner.Place(rot, t2, null, default(P2), 1, 0, 0, 0.1);
            Assert.True(r2.Placed, r2.Message);
            var c = rot.Box.Center;
            var local = TitleGeometry.ToLocal(rot.LabelBox().Value, c, 1);
            var localBox = TitleGeometry.ToLocal(rot.Box, c, 1);
            Assert.Near(localBox.MinY - 12, local.MinY, 1e-6, "12 mm below the rotated viewport's own bottom edge");

            // Fixed sheet position.
            var t3 = new TitlePlacement { Mode = TitleMode.FixedSheetPosition, Anchor = TitleAnchor.TopLeft, FixedXmm = 40, FixedYmm = 60 };
            var f3 = new FakeTitle();
            Assert.True(TitleRunner.Place(f3, t3, null, new P2(40, 60), 0, 0, 0, 0.1).Placed);
            Assert.Near(40, f3.LabelBox().Value.MinX, 1e-6);
            Assert.Near(60, f3.LabelBox().Value.MaxY, 1e-6);

            // Match reference: capture relative offset from a correctly placed title, apply to a viewport with another box.
            var reference = new FakeTitle { Offset = new P2(3, -20) };
            var refBox = reference.ViewportBox();
            var captured = TitleGeometry.CaptureRelative(refBox, reference.LabelBox().Value, TitleAnchor.BottomLeft);
            var t4 = new TitlePlacement { Mode = TitleMode.MatchReference, CaptureKind = TitleCaptureKind.RelativeOffset, Anchor = TitleAnchor.BottomLeft, OffsetXmm = captured.X, OffsetYmm = captured.Y, ReferenceDescription = "ref" };
            var other = new FakeTitle { Box = new Box2(300, 50, 700, 260), FamilyDelta = new P2(5, -3) };   // other family / type
            Assert.True(TitleRunner.Place(other, t4, null, default(P2), 0, 0, 0, 0.1).Placed);
            var a = TitleGeometry.CaptureRelative(other.ViewportBox(), other.LabelBox().Value, TitleAnchor.BottomLeft);
            Assert.Near(captured.X, a.X, 1e-6, "same relative placement although the title family offsets differ");
            Assert.Near(captured.Y, a.Y, 1e-6);

            // Overlay envelope: relative to the joint box of the members.
            var env = Box2.Union(new[] { new Box2(100, 100, 500, 400), new Box2(80, 120, 520, 380) });
            var f5 = new FakeTitle();
            Assert.True(TitleRunner.Place(f5, new TitlePlacement { Mode = TitleMode.RelativeToViewport, RelativeTo = TitleRelativeTo.OverlayEnvelope, OffsetYmm = -5 }, env, default(P2), 0, 0, 0, 0.1).Placed);
            Assert.Near(80, f5.LabelBox().Value.MinX, 1e-6);
        }

        [Test]
        public static void D_Title_NoTitleAndUnreachableTargetAreReported()
        {
            var hidden = new HiddenTitle();
            var rep = TitleRunner.Place(hidden, new TitlePlacement { Mode = TitleMode.RelativeToViewport }, null, default(P2), 0, 0, 0, 0.1);
            Assert.True(rep.NoTitle);
            var stuck = new Stuck();
            var r2 = TitleRunner.Place(stuck, new TitlePlacement { Mode = TitleMode.RelativeToViewport, OffsetYmm = -30 }, null, default(P2), 0, 0, 0, 0.1);
            Assert.False(r2.Placed);
            Assert.Near(0, stuck.Offset.Y, 1e-9, "original offset restored");
        }

        private sealed class HiddenTitle : ITitleProbe
        {
            public Box2 ViewportBox() => new Box2(0, 0, 10, 10);
            public Box2? LabelBox() => null;
            public P2 LabelOffset { get; set; }
            public double LineLength { get; set; }
        }

        private sealed class Stuck : ITitleProbe
        {
            public P2 Offset;
            public Box2 ViewportBox() => new Box2(0, 0, 100, 100);
            public Box2? LabelBox() => new Box2(0, -10, 50, -2);                // never moves (a family that ignores the offset)
            public P2 LabelOffset { get => Offset; set => Offset = value; }
            public double LineLength { get; set; }
        }

        [Test]
        public static void D_UpdateTitlesOnly_TouchesOnlyExistingViewportTitles()
        {
            var cat = V2Data.Catalog(3);
            var cfg = SetupData.Config(cat, 1);
            cfg.Levels[2].Selected = false;
            var first = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            V2Data.Commit(first, cat);
            cfg.ViewSets[0].Name = "RENAMED";                 // would rename views – must be ignored here
            cfg.Levels[2].Selected = true;                    // a level without viewport yet
            cfg.Mode = SetupMode.UpdateTitlesOnly;
            cfg.Title = new TitlePlacement { Mode = TitleMode.RelativeToViewport, Anchor = TitleAnchor.BottomLeft, OffsetYmm = -10 };
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.True(plan.CanRun, Errors(plan));
            Assert.True(plan.Rows.All(r => r.ViewAction == ViewAction.None && r.SheetAction == SheetAction.None && r.PlaceAction == PlaceAction.None), "nothing created, renamed or moved");
            Assert.Equal(2, plan.TitlesToPlace);
            Assert.Equal(0, plan.ViewsToCreate + plan.SheetsToCreate + plan.Placements);
            Assert.Contains("No viewport", plan.Rows.First(r => r.LevelUniqueId == "lvl-2").Reason);
            // A viewport can keep its title by override.
            cfg.EnsureOverride(first.Rows[0].RowKey).Title = new TitlePlacement { Mode = TitleMode.KeepExisting };
            Assert.Equal(1, PlanBuilder.Build(cfg, cat, SetupData.Doc).TitlesToPlace);
        }

        // ------------------------------------------------------------------ E. persistence, migration, regressions

        /// <summary>A profile written by the previous version (format 1: token patterns, shared sheets by number).</summary>
        internal const string Format1Profile = @"{""Format"":1,""Name"":""Old electrical"",""Mode"":0,
""Levels"":[{""LevelUniqueId"":""lvl-0"",""LevelName"":""Ground Floor"",""Code"":""GF"",""Selected"":true,""Order"":0,""Zone"":""""},{""LevelUniqueId"":""lvl-1"",""LevelName"":""Level 01"",""Code"":""L01"",""Selected"":true,""Order"":1,""Zone"":""""}],
""ViewSets"":[
{""Key"":""ltg"",""Name"":""LIGHTING"",""Code"":""LTG"",""DisciplineCode"":""E"",""Enabled"":true,""LevelUniqueIds"":[],""Family"":0,""ViewFamilyTypeUniqueId"":""vft-fp"",""TemplateUniqueId"":""tpl-ltg"",""ViewNamePattern"":""{DisciplineCode}-{LevelCode}-{ViewSetName}"",""TitleOnSheetPattern"":""{LevelName} - {ViewSetName}"",""Scale"":100,""Crop"":0,""ScopeBoxUniqueId"":"""",""SheetMode"":1,""SheetNumberPattern"":""E-{LevelCode}"",""SheetNamePattern"":""{LevelName:upper}"",""ViewNameCase"":0,""SheetNameCase"":0,""Sequence"":{""Start"":1,""Increment"":1,""Scope"":1},""TitleBlockUniqueId"":""tb-a1"",""AlignmentGroupKey"":""Main"",""SlotName"":""A"",""ViewportTypeUniqueId"":"""",""Rotation"":0,""Zone"":""""},
{""Key"":""pwr"",""Name"":""POWER"",""Code"":""PWR"",""DisciplineCode"":""E"",""Enabled"":true,""LevelUniqueIds"":[],""Family"":0,""ViewFamilyTypeUniqueId"":""vft-fp"",""TemplateUniqueId"":""tpl-ltg"",""ViewNamePattern"":""{DisciplineCode}-{LevelCode}-{ViewSetName}"",""TitleOnSheetPattern"":"""",""Scale"":100,""Crop"":0,""ScopeBoxUniqueId"":"""",""SheetMode"":1,""SheetNumberPattern"":""E-{LevelCode}"",""SheetNamePattern"":""{LevelName:upper}"",""ViewNameCase"":0,""SheetNameCase"":0,""Sequence"":{""Start"":1,""Increment"":1,""Scope"":1},""TitleBlockUniqueId"":""tb-a1"",""AlignmentGroupKey"":""Main"",""SlotName"":""B"",""ViewportTypeUniqueId"":"""",""Rotation"":0,""Zone"":""""},
{""Key"":""com"",""Name"":""COMMS"",""Code"":""COM"",""DisciplineCode"":""T"",""Enabled"":true,""LevelUniqueIds"":[],""Family"":0,""ViewFamilyTypeUniqueId"":""vft-fp"",""TemplateUniqueId"":"""",""ViewNamePattern"":""{DisciplineCode}-{LevelCode}-{Bogus}"",""TitleOnSheetPattern"":"""",""Scale"":100,""Crop"":0,""ScopeBoxUniqueId"":"""",""SheetMode"":0,""SheetNumberPattern"":""{DisciplineCode}-{Sequence:000}"",""SheetNamePattern"":""{LevelName}"",""ViewNameCase"":1,""SheetNameCase"":0,""Sequence"":{""Start"":5,""Increment"":1,""Scope"":1},""TitleBlockUniqueId"":""tb-a1"",""AlignmentGroupKey"":""Main"",""SlotName"":""A"",""ViewportTypeUniqueId"":"""",""Rotation"":0,""Zone"":""""}],
""Groups"":[{""Key"":""Main"",""Method"":0,""Anchor"":{""IsSet"":true,""X"":10,""Y"":20,""Description"":""Grid"",""DocumentKey"":""doc-key-1"",""HasSecond"":false,""X2"":0,""Y2"":0},""Slots"":[{""Name"":""A"",""Xmm"":300,""Ymm"":300,""Frame"":0,""ReferenceDescription"":""""},{""Name"":""B"",""Xmm"":600,""Ymm"":300,""Frame"":0,""ReferenceDescription"":""""}]}],
""ToleranceMm"":0.1,""Overrides"":[{""RowKey"":""com|lvl-1"",""Include"":null,""ViewName"":""T-L01-COMMS (edited)"",""TitleOnSheet"":null,""SheetNumber"":null,""SheetName"":null,""TitleBlockUniqueId"":null,""TemplateUniqueId"":null,""ExistingViewUniqueId"":null,""ExistingSheetUniqueId"":null,""SlotName"":null,""PlaceAsDependent"":false}],
""SequenceAssignments"":{""ltg|lvl-0"":1,""ltg|lvl-1"":2,""pwr|lvl-0"":1,""pwr|lvl-1"":2,""com|lvl-0"":5,""com|lvl-1"":6},
""ProjectNumberOverride"":"""",""UsableLeftMm"":20,""UsableBottomMm"":20,""UsableRightMm"":700,""UsableTopMm"":574,""AllowRealignPinned"":false,""AlignTitleLabel"":false,""Placements"":[],""DocumentKey"":""doc-key-1""}";

        [Test]
        public static void E_Format1Profile_MigratesWithoutDuplicatesOrRenumbering()
        {
            var cfg = SetupJson.Deserialize(Format1Profile);
            Assert.Equal(1, cfg.LoadedFormat);
            Assert.Equal(SetupConfig.CurrentFormat, cfg.Format);
            var com = cfg.Rule("com");
            Assert.Equal(NameRuleMode.Advanced, com.ViewNameRule.Mode, "a pattern that cannot be converted is kept, not changed");
            Assert.True(com.ViewNameRule.LegacyNote.Length > 0);
            Assert.Equal(NameRuleMode.Builder, cfg.Rule("ltg").ViewNameRule.Mode);

            // The model as the previous version left it: views by row key, one shared sheet per level keyed by number.
            var cat = V2Data.Catalog(2);
            foreach (var lv in new[] { ("lvl-0", "GF"), ("lvl-1", "L01") })
            {
                cat.Views.Add(new ExistingViewInfo { UniqueId = "v-ltg-" + lv.Item2, Name = "E-" + lv.Item2 + "-LIGHTING", TitleOnSheet = (lv.Item2 == "GF" ? "Ground Floor" : "Level 01") + " - LIGHTING", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = lv.Item1, TemplateUniqueId = "tpl-ltg", SetupRowKey = "ltg|" + lv.Item1, PlacedOnSheet = "E-" + lv.Item2 });
                cat.Views.Add(new ExistingViewInfo { UniqueId = "v-pwr-" + lv.Item2, Name = "E-" + lv.Item2 + "-POWER", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = lv.Item1, TemplateUniqueId = "tpl-ltg", SetupRowKey = "pwr|" + lv.Item1, PlacedOnSheet = "E-" + lv.Item2 });
                cat.Sheets.Add(new ExistingSheetInfo { UniqueId = "s-" + lv.Item2, Number = "E-" + lv.Item2, Name = lv.Item2 == "GF" ? "GROUND FLOOR" : "LEVEL 01", SetupRowKey = "sheet|E-" + lv.Item2, PlacedViewUniqueIds = { "v-ltg-" + lv.Item2, "v-pwr-" + lv.Item2 } });
            }
            var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
            Assert.Equal(1, cfg.Compositions.Count, "shared-by-number View Sets became one explicit composition");
            Assert.Equal(CompositionMode.Separate, cfg.Compositions[0].Mode);
            Assert.True(plan.GlobalWarnings.Any(w => w.Contains("shared sheets by number")), "the migration is reported");
            var shared = plan.Rows.Where(r => r.RuleKey == "ltg" || r.RuleKey == "pwr").ToList();
            Assert.True(shared.All(r => r.SheetAction != SheetAction.Create), "existing shared sheets are found through their old keys");
            Assert.True(shared.All(r => r.ViewAction == ViewAction.Reuse || r.ViewAction == ViewAction.Update), "no duplicated views");
            Assert.True(shared.All(r => r.PlaceAction == PlaceAction.Realign), "no duplicated viewports");
            Assert.Equal("E-GF", shared.First(r => r.LevelUniqueId == "lvl-0").SheetNumber, "numbers unchanged");
            Assert.True(shared.Where(r => r.IsSheetPrimary).All(r => r.Updates.Contains("sheet:association")), "the association is rewritten to the new stable key");
            Assert.Equal("T-005", plan.Rows.First(r => r.RowKey == "com|lvl-0").SheetNumber, "sequence kept (start 5, stored 5)");
            Assert.Equal("T-L01-COMMS (edited)", plan.Rows.First(r => r.RowKey == "com|lvl-1").ViewName, "row overrides kept");
            Assert.Equal(0, plan.SheetsToCreate - 2, "only the two COMMS sheets are new");

            // Saved again it is format 2 and reloads identically.
            string json = SetupJson.Serialize(cfg);
            var again = SetupJson.Deserialize(json);
            Assert.Equal(SetupConfig.CurrentFormat, again.LoadedFormat);
            Assert.Equal(json, SetupJson.Serialize(again), "round trip is stable");
        }

        [Test]
        public static void E_Profile_RoundTripKeepsEveryRule_AndRerunCreatesNothing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-v2-" + Guid.NewGuid().ToString("N"));
            try
            {
                var cat = V2Data.Catalog(2);
                var cfg = OverlayConfig(cat, out var comp);
                var std = V2Data.Standard(cfg);
                foreach (var r in cfg.ViewSets) { r.NumberingProfileKey = std.Key; r.Series = "1"; }
                cfg.ViewSets[0].ViewNameRule = NameRule.Of(NameComponent.OfParam(V2Data.LevelCodeShared), NameComponent.OfField(NameField.ViewSetName, "-"));
                comp.Title = new TitlePlacement { Mode = TitleMode.FixedSheetPosition, FixedXmm = 50, FixedYmm = 40, Anchor = TitleAnchor.BottomLeft };
                comp.Condition = "Existing";
                cfg.Name = "V2";
                var plan = PlanBuilder.Build(cfg, cat, SetupData.Doc);
                Assert.True(plan.CanRun, Errors(plan));
                var store = new SetupProfileStore(dir);
                store.Save(cfg);
                var loaded = store.Load("V2");
                Assert.Equal(SetupJson.Serialize(cfg), SetupJson.Serialize(loaded), "builder rules, numbering, composition and titles survive");
                Assert.Equal(V2Data.LevelCodeShared.Guid, loaded.ViewSets[0].ViewNameRule.Components[0].Param.Guid);

                V2Data.Commit(plan, cat);
                var again = PlanBuilder.Build(loaded, cat, SetupData.Doc);
                Assert.Equal(0, again.ViewsToCreate);
                Assert.Equal(0, again.SheetsToCreate);
                Assert.Equal(0, again.Placements);
                Assert.True(again.Rows.All(r => r.PlaceAction == PlaceAction.Realign));
                Assert.Equal(string.Join(",", plan.Rows.Select(r => r.SheetNumber)), string.Join(",", again.Rows.Select(r => r.SheetNumber)));

                // Numbering profiles exchange on their own, with new keys.
                string file = Path.Combine(dir, "n.json");
                NumberingProfileFile.Write(file, new[] { std });
                var into = new SetupConfig();
                var imported = NumberingProfileFile.Read(file, into);
                Assert.Equal(1, imported.Count);
                Assert.False(imported[0].Key == std.Key, "an import never takes over an existing key");
                Assert.Equal(std.Rule.Components.Count, imported[0].Rule.Components.Count);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public static void E_Portability_ProjectParametersAndMemberViewsAreRemapped()
        {
            var cat = V2Data.Catalog(1);
            var cfg = OverlayConfig(cat, out var comp);
            cat.Views.Add(new ExistingViewInfo { UniqueId = "v-old", Name = "Old", ViewType = "FloorPlan", Family = PlanFamily.FloorPlan, LevelUniqueId = "lvl-0" });
            comp.Members.Add(new CompositionMember { ExistingViewUniqueId = "v-old" });
            cfg.ViewSets[0].TitleRule = NameRule.Of(NameComponent.OfParam(V2Data.ViewPhaseText));
            var other = V2Data.Catalog(1);
            other.Parameters.RemoveAll(p => p.Ref.Key == V2Data.ViewPhaseText.Key);
            var rep = ProfilePortability.Prepare(cfg, other, "doc-key-2");
            Assert.False(rep.Complete);
            Assert.True(rep.ParametersToRemap.Count == 1, rep.ToString());
            Assert.True(rep.MissingMemberViews.Count == 1);
            Assert.False(comp.Members.Any(m => m.IsExistingView));
        }
    }
}
