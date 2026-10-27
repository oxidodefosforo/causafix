using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.ProjectSetup.Planning
{
    /// <summary>
    /// Turns the configuration into the Review plan without touching the document: expands levels × View Sets and the
    /// Views on Sheet compositions, builds names with the name engine, numbers each sheet (the unit of numbering) with
    /// its numbering profile, keeps sequences stable, detects reuse by stored association (never by name), collisions,
    /// template/scale conflicts, placement/overlay problems and the view-title placement. The executor re-validates
    /// everything inside Revit.
    /// </summary>
    public static class PlanBuilder
    {
        public static string RowKey(string ruleKey, string levelUniqueId) => ruleKey + "|" + levelUniqueId;
        public static string PlacementRowKey(string viewUniqueId) => "place|" + viewUniqueId;
        public static string MemberRowKey(string compositionKey, string memberId, string levelUniqueId) => "member|" + compositionKey + "|" + memberId + "|" + levelUniqueId;
        /// <summary>Sheet key of format 1 "shared by sheet number" (kept to find sheets created by earlier versions).</summary>
        public static string SharedSheetKey(string sheetNumber) => "sheet|" + NameRules.Normalize(sheetNumber).ToUpperInvariant();

        public static SetupPlan Build(SetupConfig cfg, SetupCatalog cat, string currentDocumentKey)
        {
            SetupMigration.Upgrade(cfg);
            var plan = new SetupPlan { Mode = cfg.Mode };
            if (cfg.ViewSets.Any(r => r.SheetMode == SheetMode.SharedBySheetNumber && cfg.CompositionOf(r.Key) == null))
                plan.GlobalWarnings.AddRange(SetupMigration.MigrateSharedSheets(cfg, LegacySharedKeys(cfg, cat)));

            if (cfg.Mode == SetupMode.PlaceRealignExisting) BuildPlacements(cfg, cat, plan);
            else
            {
                BuildGenerated(cfg, cat, plan, cfg.Mode == SetupMode.UpdateTitlesOnly ? SetupMode.FullSetup : cfg.Mode);
                if (cfg.Mode == SetupMode.UpdateTitlesOnly)
                {
                    BuildPlacements(cfg, cat, plan, requireList: false);
                    ToTitlesOnly(plan);
                }
            }

            if (cfg.Mode == SetupMode.FullSetup || cfg.Mode == SetupMode.PlaceRealignExisting) CheckPlacement(cfg, cat, plan, currentDocumentKey);
            CheckTitles(cfg, cat, plan);
            if (cfg.Mode != SetupMode.UpdateTitlesOnly) CheckCollisions(cfg, cat, plan);
            foreach (var r in plan.Rows) Finish(r);
            return plan;
        }

        // ------------------------------------------------------------------ expansion

        private sealed class Ctx
        {
            public ViewSetRule Rule;               // null for an existing-view member
            public LevelSelection Level;
            public LevelInfo Info;
            public SheetComposition Comp;
            public CompositionMember Member;
            public ExistingViewInfo MemberView;    // existing-view member
            public MemberRole Role;
            public string RowKey;
            public string SheetKey = string.Empty;
            public bool WantsSheet;
            public string SheetProblem;
            public RowOverride O;
            public PlanRow Row;
            public RowSource Source;
            public ExistingViewInfo ExistingView;
            public int RuleIndex, LevelIndex;
        }

        private sealed class Unit
        {
            public string SheetKey;
            public List<Ctx> Members = new List<Ctx>();
            public Ctx Primary;
            public SheetComposition Comp;
            public NumberingProfile Profile;
            public ExistingSheetInfo ExistingSheet;
            public bool ExplicitExisting;
            public int? Sequence;
            public string Scope;
        }

        private static List<LevelSelection> SelectedLevels(SetupConfig cfg, SetupCatalog cat) =>
            cfg.Levels.Where(l => l.Selected).OrderBy(l => l.Order).ThenBy(l => cat.Level(l.LevelUniqueId)?.Elevation ?? 0).ToList();

        private static bool Applies(ViewSetRule r, string levelUid) => r != null && r.Enabled && (r.LevelUniqueIds.Count == 0 || r.LevelUniqueIds.Contains(levelUid));

        private static void BuildGenerated(SetupConfig cfg, SetupCatalog cat, SetupPlan plan, SetupMode mode)
        {
            var levels = SelectedLevels(cfg, cat);
            if (levels.Count == 0) plan.GlobalErrors.Add("Select at least one level (step Levels).");
            var rules = cfg.ViewSets.Where(r => r.Enabled).ToList();
            if (rules.Count == 0) plan.GlobalErrors.Add("Enable at least one View Set (step View Sets).");
            foreach (var lv in levels)
                if (cat.Level(lv.LevelUniqueId) == null)
                    plan.GlobalErrors.Add("Level '" + lv.LevelName + "' is not in this document (host levels only; CausaFixIt does not create or copy levels). Map it in Levels.");
            CheckCompositions(cfg, cat, plan);

            var ctxs = new List<Ctx>();
            for (int ri = 0; ri < rules.Count; ri++)
                for (int li = 0; li < levels.Count; li++)
                {
                    var rule = rules[ri]; var lv = levels[li];
                    if (!Applies(rule, lv.LevelUniqueId)) continue;
                    var info = cat.Level(lv.LevelUniqueId);
                    if (info == null) continue;
                    ctxs.Add(new Ctx { Rule = rule, Level = lv, Info = info, RowKey = RowKey(rule.Key, lv.LevelUniqueId), RuleIndex = ri, LevelIndex = li });
                }
            foreach (var comp in cfg.Compositions.Where(c => c.Enabled))
                foreach (var m in comp.Members.Where(m => m.Enabled && m.IsExistingView))
                {
                    var view = cat.View(m.ExistingViewUniqueId);
                    if (view == null) { plan.GlobalErrors.Add("Composition '" + comp.Name + "': an existing member view no longer exists. Remove it or choose another view."); continue; }
                    int li = levels.FindIndex(l => l.LevelUniqueId == view.LevelUniqueId);
                    if (li < 0) continue; // the view's level is not part of this setup
                    ctxs.Add(new Ctx { Level = levels[li], Info = cat.Level(view.LevelUniqueId), Comp = comp, Member = m, MemberView = view, Role = MemberRole.Secondary,
                                       RowKey = MemberRowKey(comp.Key, m.Id, view.LevelUniqueId), RuleIndex = rules.Count, LevelIndex = li });
                }

            bool modeWantsSheets = mode != SetupMode.ViewsOnly;
            foreach (var c in ctxs)
            {
                c.O = cfg.Override(c.RowKey);
                if (c.Rule != null)
                {
                    c.Comp = cfg.CompositionOf(c.Rule.Key);
                    if (c.Comp != null) c.Member = c.Comp.Members.First(m => m.Enabled && m.ViewSetKey == c.Rule.Key);
                }
                if (c.Comp != null)
                {
                    var primary = c.Comp.Primary;
                    var primaryRule = primary == null ? null : cfg.Rule(primary.ViewSetKey);
                    c.Role = primary != null && c.Member.Id == primary.Id ? MemberRole.Primary : MemberRole.Secondary;
                    c.WantsSheet = modeWantsSheets;
                    if (primaryRule == null) c.SheetProblem = "Composition '" + c.Comp.Name + "' needs a View Set as its primary member (Views on Sheet).";
                    else if (!Applies(primaryRule, c.Level.LevelUniqueId)) c.SheetProblem = "The primary View Set '" + primaryRule.Name + "' of composition '" + c.Comp.Name + "' does not apply to " + c.Info?.Name + ", so this member has no sheet on that level.";
                    else c.SheetKey = c.Comp.SheetKeyFor(c.Level.LevelUniqueId);
                }
                else
                {
                    c.WantsSheet = modeWantsSheets && c.Rule.SheetMode != SheetMode.None;
                    if (c.WantsSheet)
                        c.SheetKey = c.Rule.SheetMode == SheetMode.ExistingSheet || !string.IsNullOrEmpty(c.O?.ExistingSheetUniqueId)
                            ? (string.IsNullOrEmpty(c.O?.ExistingSheetUniqueId) ? "existing|?" + c.RowKey : "existing|" + c.O.ExistingSheetUniqueId)
                            : c.RowKey;
                }
            }

            // Sheet units (the unit of numbering): one per sheet key, its primary member defines the sheet.
            var units = new Dictionary<string, Unit>();
            foreach (var c in ctxs.Where(c => c.WantsSheet && !string.IsNullOrEmpty(c.SheetKey) && c.SheetProblem == null))
            {
                if (!units.TryGetValue(c.SheetKey, out Unit u)) units[c.SheetKey] = u = new Unit { SheetKey = c.SheetKey, Comp = c.Comp };
                u.Members.Add(c);
            }
            foreach (var u in units.Values)
            {
                u.Primary = u.Members.FirstOrDefault(m => m.Role == MemberRole.Primary) ?? u.Members.First();
                var po = u.Primary.O;
                string profileKey = !string.IsNullOrEmpty(po?.NumberingProfileKey) ? po.NumberingProfileKey
                                  : !string.IsNullOrEmpty(u.Comp?.NumberingProfileKey) ? u.Comp.NumberingProfileKey
                                  : u.Primary.Rule?.NumberingProfileKey;
                u.Profile = cfg.Numbering(profileKey);
                FindExistingSheet(cfg, cat, u);
            }
            AssignSequences(cfg, units.Values.Where(u => !u.ExplicitExisting).ToList(), (u, f) => UnitField(cfg, cat, u.Primary, f));

            string projectNumber = !string.IsNullOrWhiteSpace(cfg.ProjectNumberOverride) ? cfg.ProjectNumberOverride : cat.ProjectNumber;
            foreach (var c in ctxs)
            {
                c.ExistingView = FindExistingView(cat, c);
                c.Row = NewRow(cfg, cat, c, mode);
                c.Source = new RowSource(cfg, cat, c, projectNumber);
            }
            var byKey = units.Values.SelectMany(u => u.Members.Select(m => (m, u))).ToDictionary(x => x.m, x => x.u);

            // Sheet values first (primary of each unit), then every row's view values.
            foreach (var u in units.Values)
            {
                var p = u.Primary;
                var row = p.Row;
                p.Source.Unit = u;
                var engine = new NameEngine(p.Source);
                row.Names[NameContext.SheetNumber] = engine.Evaluate(NameContext.SheetNumber);
                row.Names[NameContext.SheetName] = engine.Evaluate(NameContext.SheetName);
                row.ProposedSheetNumber = Take(row, NameContext.SheetNumber, required: !u.ExplicitExisting);
                row.SheetName = Take(row, NameContext.SheetName, required: !u.ExplicitExisting);
                // An existing sheet keeps its number until the renumbering is approved (or the number is typed in Review).
                var ex = u.ExistingSheet;
                bool approved = p.O?.SheetNumber != null || (cfg.ApprovedSheetNumbers.TryGetValue(u.SheetKey, out string a) && a == row.ProposedSheetNumber);
                row.SheetNumber = ex != null && !u.ExplicitExisting && !approved && !string.IsNullOrEmpty(row.ProposedSheetNumber) ? ex.Number : row.ProposedSheetNumber;
                if (u.Profile == null && !u.ExplicitExisting) row.Errors.Add("Choose a numbering profile for this sheet (step Numbering).");
                row.NumberingProfileKey = u.Profile?.Key ?? "";
                row.NumberingProfileName = u.Profile?.Name ?? "";
                row.SequenceScopeKey = u.Scope ?? "";
                row.Sequence = u.Sequence;
                row.SheetKey = u.SheetKey;
                row.SheetPrimaryRowKey = row.RowKey;
                p.Source.SheetFrom = row;   // view names of the primary see the sheet values actually used
            }
            foreach (var c in ctxs)
            {
                byKey.TryGetValue(c, out Unit u);
                c.Source.Unit = u;
                if (u != null) c.Source.SheetFrom = u.Primary.Row;
                var engine = new NameEngine(c.Source);
                bool wantsViews = mode != SetupMode.SheetsOnly;
                var row = c.Row;
                if (wantsViews)
                {
                    row.Names[NameContext.ViewName] = engine.Evaluate(NameContext.ViewName);
                    row.Names[NameContext.TitleOnSheet] = engine.Evaluate(NameContext.TitleOnSheet);
                    row.ViewName = Take(row, NameContext.ViewName, required: true);
                    row.TitleOnSheet = Take(row, NameContext.TitleOnSheet, required: false);
                }
                if (u != null)
                {
                    row.SheetPrimaryRowKey = u.Primary.Row.RowKey;
                    row.SheetKey = u.SheetKey;
                }
                else if (c.SheetProblem != null) row.Errors.Add(c.SheetProblem);

                if (wantsViews && (c.Rule != null || c.MemberView != null)) ResolveView(cfg, cat, c);
            }
            foreach (var u in units.Values) ResolveSheet(cfg, cat, u);
            foreach (var c in ctxs)
            {
                if (byKey.TryGetValue(c, out Unit u) && u.Primary != c) CopySheet(u.Primary.Row, c.Row);
                if (mode == SetupMode.FullSetup && c.Row.SheetAction != SheetAction.None && c.Row.ViewAction != ViewAction.Skip) ResolvePlacement(cat, c.Row, c.O);
                ResolveViewportAndTitle(cfg, cat, c);
                plan.Rows.Add(c.Row);
            }
            CheckOverlays(cfg, cat, plan);
        }

        private static string Take(PlanRow row, NameContext t, bool required)
        {
            if (!row.Names.TryGetValue(t, out NameResult r)) return string.Empty;
            row.Errors.AddRange(r.Errors);
            if (!r.Produced)
            {
                if (required) row.Errors.Add(NameFields.TargetLabel(t) + ": the rule is empty. Add components (Build Name) or an Advanced pattern.");
                return string.Empty;
            }
            return r.Text;
        }

        private static PlanRow NewRow(SetupConfig cfg, SetupCatalog cat, Ctx c, SetupMode mode)
        {
            var rule = c.Rule;
            var o = c.O;
            var mv = c.MemberView;
            var primaryRule = c.Comp?.Primary == null ? null : cfg.Rule(c.Comp.Primary.ViewSetKey);
            var placeRule = rule ?? primaryRule;
            var row = new PlanRow
            {
                RowKey = c.RowKey,
                RuleKey = rule?.Key ?? "",
                LevelUniqueId = c.Level.LevelUniqueId,
                LevelName = c.Info?.Name ?? c.Level.LevelName,
                LevelCode = c.Level.Code,
                ViewSetName = rule?.Name ?? ("existing view '" + mv?.Name + "'"),
                Family = rule?.Family ?? mv?.Family ?? PlanFamily.FloorPlan,
                ViewFamilyTypeUniqueId = rule?.ViewFamilyTypeUniqueId ?? "",
                TemplateUniqueId = o?.TemplateUniqueId ?? rule?.TemplateUniqueId ?? mv?.TemplateUniqueId ?? "",
                RequestedScale = rule?.Scale ?? mv?.Scale ?? 0,
                TitleBlockUniqueId = o?.TitleBlockUniqueId ?? (!string.IsNullOrEmpty(c.Comp?.TitleBlockUniqueId) ? c.Comp.TitleBlockUniqueId : (primaryRule ?? rule)?.TitleBlockUniqueId) ?? "",
                ViewportTypeUniqueId = placeRule?.ViewportTypeUniqueId ?? "",
                Rotation = placeRule?.Rotation ?? ViewportRotationOption.None,
                GroupKey = placeRule?.AlignmentGroupKey ?? "Main",
                SlotName = o?.SlotName ?? placeRule?.SlotName ?? "A",
                Crop = rule?.Crop ?? CropOption.KeepExisting,
                ScopeBoxUniqueId = rule?.ScopeBoxUniqueId ?? "",
                Include = o?.Include ?? true,
                CompositionKey = c.Comp?.Key ?? "",
                CompositionName = c.Comp?.Name ?? "",
                CompositionMode = c.Comp?.Mode ?? CompositionMode.Separate,
                Role = c.Role,
                MemberId = c.Member?.Id ?? ""
            };
            if (c.Comp != null)
            {
                var pr = primaryRule ?? rule;
                if (c.Comp.Mode == CompositionMode.Overlay)
                {
                    row.GroupKey = !string.IsNullOrEmpty(c.Comp.GroupKey) ? c.Comp.GroupKey : pr?.AlignmentGroupKey ?? "Main";
                    row.SlotName = !string.IsNullOrEmpty(c.Comp.SlotName) ? c.Comp.SlotName : pr?.SlotName ?? "A";
                    row.OverlayGroupId = c.SheetKey;
                    row.Rotation = pr?.Rotation ?? ViewportRotationOption.None;
                }
                else
                {
                    if (!string.IsNullOrEmpty(c.Comp.GroupKey)) row.GroupKey = c.Comp.GroupKey;
                    if (o?.SlotName == null && !string.IsNullOrEmpty(c.Member?.SlotName)) row.SlotName = c.Member.SlotName;
                }
            }
            row.EffectiveScale = row.RequestedScale;
            var tpl = cat.Template(row.TemplateUniqueId);
            if (tpl != null && tpl.ControlsScale) row.EffectiveScale = tpl.Scale;
            return row;
        }

        // ------------------------------------------------------------------ values for the name engine

        private static SourceValue UnitField(SetupConfig cfg, SetupCatalog cat, Ctx c, NameField f)
        {
            var src = new RowSource(cfg, cat, c, cat.ProjectNumber);
            return src.Field(f, new NameComponent());
        }

        /// <summary>Values of one row for the name engine. Sheet-level targets of secondary members come from the sheet's primary.</summary>
        private sealed class RowSource : INameSource
        {
            private readonly SetupConfig _cfg;
            private readonly SetupCatalog _cat;
            private readonly Ctx _c;
            private readonly string _projectNumber;
            public Unit Unit;
            public PlanRow SheetFrom;

            public RowSource(SetupConfig cfg, SetupCatalog cat, Ctx c, string projectNumber) { _cfg = cfg; _cat = cat; _c = c; _projectNumber = projectNumber; }

            private ViewSetRule Rule => _c.Rule ?? (_c.Comp?.Primary == null ? null : _cfg.Rule(_c.Comp.Primary.ViewSetKey));
            private string CompOr(string compValue, string ruleValue) => compValue ?? ruleValue ?? string.Empty;

            public SourceValue Field(NameField f, NameComponent comp)
            {
                var r = Rule;
                switch (f)
                {
                    case NameField.Discipline: return SourceValue.Of(r?.DisciplineCode, "View Set");
                    case NameField.ViewSetName: return SourceValue.Of(r?.Name, "View Set");
                    case NameField.ViewSetCode: return SourceValue.Of(r?.Code, "View Set");
                    case NameField.LevelCode: return SourceValue.Of(_c.Level.Code, "set a code in Levels");
                    case NameField.Zone: return SourceValue.Of(!string.IsNullOrWhiteSpace(_c.Level.Zone) ? _c.Level.Zone : r?.Zone, "Levels / View Set zone");
                    case NameField.Condition: return SourceValue.Of(CompOr(_c.Comp?.Condition, r?.Condition), "View Set / composition condition");
                    case NameField.Purpose: return SourceValue.Of(CompOr(_c.Comp?.Purpose, r?.Purpose), "View Set / composition prefix");
                    case NameField.Series: return SourceValue.Of(CompOr(_c.Comp?.Series, r?.Series), "View Set / composition series");
                    case NameField.ProjectNumber: return SourceValue.Of(_projectNumber, "Project Information > Project Number is empty");
                    case NameField.LevelName: return SourceValue.Of(_c.Info?.Name);
                    case NameField.LevelElevation: return _c.Info == null ? SourceValue.Unknown("level missing") : SourceValue.Of(NameEngine.FormatElevation(_c.Info.Elevation, comp.Unit, comp.Decimals));
                    case NameField.ViewTemplateName: return SourceValue.Of(_cat.Template(_c.Row?.TemplateUniqueId)?.Name, "no view template");
                    case NameField.ViewTypeName: return SourceValue.Of(_cat.ViewType(_c.Row?.ViewFamilyTypeUniqueId)?.Name ?? "", "no view type");
                    case NameField.Scale: return _c.Row == null || _c.Row.EffectiveScale <= 0 ? SourceValue.Unknown("scale unknown") : SourceValue.Of(_c.Row.EffectiveScale.ToString(CultureInfo.InvariantCulture));
                    case NameField.Sequence:
                        var seq = Unit?.Sequence ?? SheetFrom?.Sequence;
                        return seq.HasValue ? SourceValue.Of(seq.Value.ToString(CultureInfo.InvariantCulture)) : SourceValue.Unknown("the sequence is given per sheet; this row has no sheet");
                    case NameField.CompositionName: return SourceValue.Of(_c.Comp?.Name, "not in a composition");
                    default: return SourceValue.Unknown(f + " is not available here");
                }
            }

            public SourceValue Param(ParamRef p)
            {
                if (p.Kind == ParamKind.Project && _cat.Parameters.Count > 0 && !_cat.Parameters.Any(x => x.Ref.IdentityKey == p.IdentityKey))
                    return SourceValue.Unknown("project parameter '" + p.Name + "' does not exist in this document (project parameters do not travel between documents): pick it again");
                string k = p.IdentityKey;
                string NotRead(string what) => _cat.ValuesRead.Contains(p.Key) ? what + " has no parameter '" + p.Name + "'" : "values of '" + p.Name + "' not read yet (Refresh)";
                switch (p.Scope)
                {
                    case ParamScope.ProjectInformation:
                        return _cat.ProjectInfoValues.TryGetValue(k, out string pv) ? SourceValue.Of(pv, "Project Information") : SourceValue.Unknown("Project Information has no parameter '" + p.Name + "'");
                    case ParamScope.Level:
                        if (_c.Info == null) return SourceValue.Unknown("level missing");
                        return _c.Info.Values.TryGetValue(k, out string lv) ? SourceValue.Of(lv, "Level") : SourceValue.Unknown("level '" + _c.Info.Name + "' has no parameter '" + p.Name + "'");
                    case ParamScope.View:
                        var v = _c.ExistingView;
                        if (v == null) return SourceValue.Unknown("the view is created by this run, so its parameter '" + p.Name + "' has no value yet (view templates are not used as the final value)");
                        return v.Values.TryGetValue(k, out string vv) ? SourceValue.Of(vv, "view '" + v.Name + "'") : SourceValue.Unknown(NotRead("view '" + v.Name + "'"));
                    case ParamScope.Sheet:
                        var s = Unit?.ExistingSheet;
                        if (s == null) return SourceValue.Unknown("the sheet is created by this run, so its parameter '" + p.Name + "' has no value yet");
                        return s.Values.TryGetValue(k, out string sv) ? SourceValue.Of(sv, "sheet " + s.Number) : SourceValue.Unknown(NotRead("sheet " + s.Number));
                    case ParamScope.TitleBlockType:
                        var tb = _cat.TitleBlock(_c.Row?.TitleBlockUniqueId);
                        if (tb == null) return SourceValue.Unknown("no title block type chosen");
                        return tb.Values.TryGetValue(k, out string tv) ? SourceValue.Of(tv, "title block type " + tb.TypeName) : SourceValue.Unknown("title block type '" + tb.TypeName + "' has no parameter '" + p.Name + "'");
                    case ParamScope.TitleBlockInstance:
                        var es = Unit?.ExistingSheet;
                        if (es == null) return SourceValue.Unknown("the title block instance exists only after the sheet is created");
                        if (es.TitleBlockCount != 1) return SourceValue.Unknown("sheet " + es.Number + " has " + es.TitleBlockCount + " title blocks: no unique source");
                        return es.TitleBlockValues.TryGetValue(k, out string iv) ? SourceValue.Of(iv, "title block on " + es.Number) : SourceValue.Unknown(NotRead("the title block of " + es.Number));
                    default: return SourceValue.Unknown("unsupported scope");
                }
            }

            public NameRule RuleFor(NameContext t)
            {
                switch (t)
                {
                    case NameContext.ViewName: return _c.Rule?.ViewNameRule;
                    case NameContext.TitleOnSheet: return _c.Rule?.TitleRule;
                    case NameContext.SheetName: return _c.Comp?.SheetNameRule ?? Rule?.SheetNameRule;
                    default: return Unit?.Profile?.Rule;
                }
            }

            public string FixedValue(NameContext t)
            {
                var o = _c.O;
                bool sheetTarget = t == NameContext.SheetNumber || t == NameContext.SheetName;
                if (sheetTarget && SheetFrom != null) return t == NameContext.SheetNumber ? SheetFrom.SheetNumber : SheetFrom.SheetName;
                if (_c.MemberView != null)
                {
                    if (t == NameContext.ViewName) return _c.MemberView.Name;           // existing views keep their own name and title
                    if (t == NameContext.TitleOnSheet) return _c.MemberView.TitleOnSheet;
                }
                switch (t)
                {
                    case NameContext.ViewName: return o?.ViewName;
                    case NameContext.TitleOnSheet: return o?.TitleOnSheet;
                    case NameContext.SheetNumber: return o?.SheetNumber;
                    default: return o?.SheetName;
                }
            }

            public TokenValues Tokens()
            {
                var r = Rule;
                var seq = Unit?.Sequence ?? SheetFrom?.Sequence;
                return new TokenValues
                {
                    ProjectNumber = _projectNumber, DisciplineCode = r?.DisciplineCode, ViewSetCode = r?.Code, ViewSetName = r?.Name,
                    LevelName = _c.Info?.Name, LevelCode = _c.Level.Code, Zone = !string.IsNullOrWhiteSpace(_c.Level.Zone) ? _c.Level.Zone : r?.Zone,
                    Condition = CompOr(_c.Comp?.Condition, r?.Condition), Purpose = CompOr(_c.Comp?.Purpose, r?.Purpose), Series = CompOr(_c.Comp?.Series, r?.Series),
                    Sequence = seq
                };
            }
        }

        // ------------------------------------------------------------------ sequences

        private static string GroupValue(Unit u, NameField f, Func<Unit, NameField, SourceValue> field)
        {
            switch (f)
            {
                case NameField.ViewSetName:
                case NameField.ViewSetCode: return "set:" + (u.Primary.Rule?.Key ?? "");
                case NameField.LevelName:
                case NameField.LevelCode: return "level:" + u.Primary.Level.LevelUniqueId;
                default: return f + ":" + (field(u, f).Value ?? "").Trim().ToUpperInvariant();
            }
        }

        /// <summary>
        /// Gives each new sheet the next free number of its counter scope; sheets that already have a number keep it, so
        /// adding levels or members never renumbers. Numbers stored for sheets outside the current selection stay reserved.
        /// Use <see cref="RegenerateSequences"/> (with a preview) to renumber explicitly.
        /// </summary>
        private static void AssignSequences(SetupConfig cfg, List<Unit> units, Func<Unit, NameField, SourceValue> field)
        {
            foreach (var u in units.Where(x => x.Profile != null))
                u.Scope = u.Profile.Counter + "|" + string.Join("|", u.Profile.GroupBy.Distinct().OrderBy(g => g).Select(g => GroupValue(u, g, field)));
            var ordered = units.Where(x => x.Profile != null)
                .OrderBy(x => x.Profile.Order == SequenceOrder.LevelThenViewSet ? x.Primary.LevelIndex : x.Primary.RuleIndex)
                .ThenBy(x => x.Profile.Order == SequenceOrder.LevelThenViewSet ? x.Primary.RuleIndex : x.Primary.LevelIndex)
                .ToList();
            var used = new Dictionary<string, HashSet<int>>();
            HashSet<int> Used(string scope) { if (!used.TryGetValue(scope, out var h)) used[scope] = h = new HashSet<int>(); return h; }
            var inPlan = new HashSet<string>(ordered.Select(u => u.SheetKey));
            foreach (var kv in cfg.SequenceAssignments)
            {
                if (inPlan.Contains(kv.Key)) continue;
                if (cfg.SequenceScopes.TryGetValue(kv.Key, out string sc)) Used(sc).Add(kv.Value);   // reserved by a sheet not in this selection
            }
            foreach (var u in ordered)
                if (cfg.SequenceAssignments.TryGetValue(u.SheetKey, out int n)) { Used(u.Scope).Add(n); u.Sequence = n; cfg.SequenceScopes[u.SheetKey] = u.Scope; }
            foreach (var u in ordered)
            {
                if (u.Sequence.HasValue) continue;
                int inc = u.Profile.Increment == 0 ? 1 : u.Profile.Increment;
                int candidate = u.Profile.Start;
                var h = Used(u.Scope);
                while (h.Contains(candidate)) candidate += inc;
                h.Add(candidate);
                cfg.SequenceAssignments[u.SheetKey] = candidate;
                cfg.SequenceScopes[u.SheetKey] = u.Scope;
                u.Sequence = candidate;
            }
        }

        /// <summary>Explicit "Renumber": forgets stored numbers of the given sheet keys so they are assigned again in order.</summary>
        public static void RegenerateSequences(SetupConfig cfg, IEnumerable<string> sheetKeys)
        {
            foreach (var k in sheetKeys.ToList()) { cfg.SequenceAssignments.Remove(k); cfg.SequenceScopes.Remove(k); }
        }

        /// <summary>Sheet keys format-1 "shared by number" rows used (row key → "sheet|NUMBER"), computed with the old per-row numbering.</summary>
        private static Dictionary<string, string> LegacySharedKeys(SetupConfig cfg, SetupCatalog cat)
        {
            var map = new Dictionary<string, string>();
            // All levels of the document (not only the selected ones), so sheets of deselected levels stay associated.
            var levels = cfg.Levels.Where(l => cat.Level(l.LevelUniqueId) != null).OrderBy(l => l.Order).ToList();
            var rules = cfg.ViewSets.Where(r => r.Enabled).ToList();
            var units = new List<Unit>();
            for (int ri = 0; ri < rules.Count; ri++)
                for (int li = 0; li < levels.Count; li++)
                {
                    var r = rules[ri]; var lv = levels[li];
                    if (!Applies(r, lv.LevelUniqueId) || cat.Level(lv.LevelUniqueId) == null) continue;
                    var c = new Ctx { Rule = r, Level = lv, Info = cat.Level(lv.LevelUniqueId), RowKey = RowKey(r.Key, lv.LevelUniqueId), RuleIndex = ri, LevelIndex = li };
                    c.O = cfg.Override(c.RowKey);
                    var u = new Unit { SheetKey = c.RowKey, Primary = c, Profile = cfg.Numbering(r.NumberingProfileKey) };
                    u.Members.Add(c);
                    units.Add(u);
                }
            // Numbers are computed on a scratch copy: only numbers already stored are real (the migration copies those).
            var savedSeq = new Dictionary<string, int>(cfg.SequenceAssignments);
            var savedScopes = new Dictionary<string, string>(cfg.SequenceScopes);
            AssignSequences(cfg, units, (u, f) => UnitField(cfg, cat, u.Primary, f));
            cfg.SequenceAssignments = savedSeq;
            cfg.SequenceScopes = savedScopes;
            foreach (var u in units.Where(x => x.Primary.Rule.SheetMode == SheetMode.SharedBySheetNumber))
            {
                var c = u.Primary;
                c.Row = new PlanRow { RowKey = c.RowKey, TemplateUniqueId = c.Rule.TemplateUniqueId, TitleBlockUniqueId = c.Rule.TitleBlockUniqueId, ViewFamilyTypeUniqueId = c.Rule.ViewFamilyTypeUniqueId };
                c.Source = new RowSource(cfg, cat, c, !string.IsNullOrWhiteSpace(cfg.ProjectNumberOverride) ? cfg.ProjectNumberOverride : cat.ProjectNumber) { Unit = u };
                var res = new NameEngine(c.Source).Evaluate(NameContext.SheetNumber);
                string number = c.O?.SheetNumber ?? res.Text;
                if (!string.IsNullOrEmpty(number)) map[c.RowKey] = SharedSheetKey(number);
            }
            return map;
        }

        // ------------------------------------------------------------------ views

        private static ExistingViewInfo FindExistingView(SetupCatalog cat, Ctx c)
        {
            if (c.MemberView != null) return c.MemberView;
            if (!string.IsNullOrEmpty(c.O?.ExistingViewUniqueId)) return cat.View(c.O.ExistingViewUniqueId);
            var assoc = cat.Views.Where(v => v.SetupRowKey == c.RowKey && !v.IsDependent).ToList();
            return assoc.Count == 1 ? assoc[0] : null;
        }

        private static void ResolveView(SetupConfig cfg, SetupCatalog cat, Ctx c)
        {
            var row = c.Row;
            var rule = c.Rule;
            var o = c.O;
            if (c.MemberView != null)
            {
                // Existing view chosen as a member: keeps its type, template, name and title.
                var mv = c.MemberView;
                row.ExistingViewUniqueId = mv.UniqueId;
                row.ViewAction = ViewAction.Reuse;
                row.EffectiveScale = mv.Scale;
                row.TemplateName = cat.Template(mv.TemplateUniqueId)?.Name ?? "";
                if (mv.IsDependent) row.Warnings.Add("Member '" + mv.Name + "' is a dependent view.");
                return;
            }
            var vft = cat.ViewType(rule.ViewFamilyTypeUniqueId);
            if (vft == null) row.Errors.Add("Choose a view type (" + rule.Family + ") for View Set '" + rule.Name + "'.");
            else
            {
                row.ViewFamilyTypeName = vft.Name;
                if (vft.Family != rule.Family) row.Errors.Add("View type '" + vft.Name + "' is a " + vft.Family + ", not a " + rule.Family + ".");
            }
            if (rule.Family == PlanFamily.StructuralPlan && !cat.StructuralPlansAvailable)
                row.Errors.Add("Structural plans are not available in this document (no structural plan view type).");

            var tpl = cat.Template(row.TemplateUniqueId);
            if (!string.IsNullOrEmpty(row.TemplateUniqueId) && tpl == null) row.Errors.Add("View template not found in this document. Choose one in View Sets.");
            if (tpl != null)
            {
                row.TemplateName = tpl.Name;
                if (tpl.Family.HasValue && tpl.Family.Value != rule.Family)
                    row.Errors.Add("Template '" + tpl.Name + "' is a " + tpl.Family + " template; it cannot be applied to a " + rule.Family + ".");
                if (tpl.ControlsScale)
                {
                    if (tpl.Scale != rule.Scale) row.Warnings.Add("Scale is controlled by template '" + tpl.Name + "': effective 1:" + tpl.Scale + " (requested 1:" + rule.Scale + "). The template is not changed.");
                    row.ScaleNote = "from template";
                }
                if (rule.Crop == CropOption.ScopeBox && tpl.ControlsScopeBox)
                    row.Warnings.Add("The template controls the scope box; the requested scope box will not apply.");
            }
            if (rule.Crop == CropOption.ScopeBox && string.IsNullOrEmpty(rule.ScopeBoxUniqueId))
                row.Errors.Add("Crop = Scope box but no scope box is chosen.");

            ExistingViewInfo existing = null;
            if (!string.IsNullOrEmpty(o?.ExistingViewUniqueId))
            {
                existing = cat.View(o.ExistingViewUniqueId);
                if (existing == null) row.Errors.Add("The view chosen for reuse no longer exists.");
            }
            else
            {
                var assoc = cat.Views.Where(v => v.SetupRowKey == row.RowKey && !v.IsDependent).ToList();
                if (assoc.Count > 1) row.Errors.Add("Several views are associated with this row (" + string.Join(", ", assoc.Select(a => a.Name)) + "). Choose one to reuse.");
                else if (assoc.Count == 1) existing = assoc[0];
            }

            if (existing == null) { row.ViewAction = ViewAction.Create; return; }

            row.ExistingViewUniqueId = existing.UniqueId;
            if (existing.LevelUniqueId != row.LevelUniqueId)
                row.Errors.Add("Existing view '" + existing.Name + "' belongs to another level; it cannot be reused for " + row.LevelName + ".");
            if (existing.Family.HasValue && existing.Family.Value != rule.Family)
                row.Errors.Add("Existing view '" + existing.Name + "' is a " + existing.Family + ", not a " + rule.Family + ".");
            if (existing.Name != row.ViewName) row.Updates.Add("view:name");
            if (!string.IsNullOrEmpty(row.TitleOnSheet) && existing.TitleOnSheet != row.TitleOnSheet) row.Updates.Add("view:title");
            if (!string.IsNullOrEmpty(row.TemplateUniqueId) && existing.TemplateUniqueId != row.TemplateUniqueId) row.Updates.Add("view:template");
            row.ViewAction = row.Updates.Any(u => u.StartsWith("view")) ? ViewAction.Update : ViewAction.Reuse;
            if (existing.SetupRowKey != row.RowKey)
                row.Warnings.Add("Reusing view '" + existing.Name + "', which was not generated for this row; it will be associated with it.");
        }

        // ------------------------------------------------------------------ sheets

        private static void FindExistingSheet(SetupConfig cfg, SetupCatalog cat, Unit u)
        {
            var p = u.Primary;
            if (u.SheetKey.StartsWith("existing|", StringComparison.Ordinal))
            {
                u.ExplicitExisting = true;
                u.ExistingSheet = cat.Sheet(u.SheetKey.Substring("existing|".Length));
                return;
            }
            var assoc = cat.Sheets.Where(s => s.SetupRowKey == u.SheetKey).ToList();
            if (assoc.Count == 0 && u.Comp != null && u.Comp.LegacySheetKeys.TryGetValue(p.Level.LevelUniqueId, out string legacy))
                assoc = cat.Sheets.Where(s => s.SetupRowKey == legacy).ToList();
            if (assoc.Count == 1) u.ExistingSheet = assoc[0];
        }

        private static void ResolveSheet(SetupConfig cfg, SetupCatalog cat, Unit u)
        {
            var row = u.Primary.Row;
            var o = u.Primary.O;
            var tb = cat.TitleBlock(row.TitleBlockUniqueId);
            if (tb != null) row.TitleBlockName = tb.FamilyName + " : " + tb.TypeName;

            if (u.ExplicitExisting)
            {
                var ex = u.ExistingSheet;
                if (ex == null)
                {
                    row.Errors.Add(u.Primary.Rule?.SheetMode == SheetMode.ExistingSheet && string.IsNullOrEmpty(o?.ExistingSheetUniqueId) ? "Choose the existing sheet for this row." : "The sheet chosen for this row no longer exists.");
                    row.SheetAction = SheetAction.Skip;
                    return;
                }
                row.ExistingSheetUniqueId = ex.UniqueId;
                row.SheetNumber = row.CurrentSheetNumber = row.ProposedSheetNumber = ex.Number;
                row.SheetName = ex.Name;
                // Names of an explicitly chosen sheet are not changed: discard name/number errors of the rules.
                row.Errors.RemoveAll(e => e.StartsWith("Sheet Number", StringComparison.Ordinal) || e.StartsWith("Sheet Name", StringComparison.Ordinal) || e.StartsWith("Choose a numbering profile", StringComparison.Ordinal));
                if (ex.IsPlaceholder) row.Errors.Add("Sheet " + ex.Number + " is a placeholder; CausaFixIt does not convert placeholders.");
                row.SheetAction = SheetAction.Reuse;
                return;
            }
            if (cat.Sheets.Count(s => s.SetupRowKey == u.SheetKey) > 1)
                row.Errors.Add("Several sheets are associated with this sheet (" + string.Join(", ", cat.Sheets.Where(s => s.SetupRowKey == u.SheetKey).Select(a => a.Number)) + ").");
            var existing = u.ExistingSheet;
            if (existing == null)
            {
                if (tb == null) row.Errors.Add(string.IsNullOrEmpty(row.TitleBlockUniqueId) ? "Choose a title block (step Sheets)." : "Title block type not found in this document. Load the family or choose another.");
                row.SheetAction = SheetAction.Create;
                return;
            }
            row.ExistingSheetUniqueId = existing.UniqueId;
            row.CurrentSheetNumber = existing.Number;
            if (existing.IsPlaceholder) row.Errors.Add("Sheet " + existing.Number + " is a placeholder; CausaFixIt does not convert placeholders.");
            if (!string.Equals(existing.Number, row.SheetNumber, StringComparison.Ordinal)) row.Updates.Add("sheet:number");
            else if (!string.IsNullOrEmpty(row.ProposedSheetNumber) && !string.Equals(existing.Number, row.ProposedSheetNumber, StringComparison.Ordinal))
            {
                row.RenumberPending = true;
                row.Warnings.Add("Renumber pending: sheet " + existing.Number + " → proposed " + row.ProposedSheetNumber + ". Existing numbers are kept until you approve the renumbering (step Numbering).");
            }
            if (existing.Name != row.SheetName && !string.IsNullOrEmpty(row.SheetName)) row.Updates.Add("sheet:name");
            if (existing.SetupRowKey != u.SheetKey) row.Updates.Add("sheet:association");
            row.SheetAction = row.Updates.Any(x => x == "sheet:number" || x == "sheet:name") ? SheetAction.Update : SheetAction.Reuse;
        }

        private static void CopySheet(PlanRow from, PlanRow to)
        {
            to.SheetNumber = from.SheetNumber;
            to.SheetName = from.SheetName;
            to.CurrentSheetNumber = from.CurrentSheetNumber;
            to.ProposedSheetNumber = from.ProposedSheetNumber;
            to.TitleBlockUniqueId = from.TitleBlockUniqueId;
            to.TitleBlockName = from.TitleBlockName;
            to.ExistingSheetUniqueId = from.ExistingSheetUniqueId;
            to.NumberingProfileKey = from.NumberingProfileKey;
            to.NumberingProfileName = from.NumberingProfileName;
            to.Sequence = from.Sequence;
            // The sheet is created/updated by its primary row; members only reuse it (the executor shares it by key).
            to.SheetAction = from.SheetAction == SheetAction.Skip ? SheetAction.Skip : from.SheetAction == SheetAction.None ? SheetAction.None : SheetAction.Reuse;
            if (from.SheetAction == SheetAction.Create) to.SheetAction = SheetAction.Create;
        }

        // ------------------------------------------------------------------ placement, viewport type and title

        private static void ResolvePlacement(SetupCatalog cat, PlanRow row, RowOverride o)
        {
            if (row.SheetAction == SheetAction.Skip) { row.PlaceAction = PlaceAction.Skip; return; }
            var view = cat.View(row.ExistingViewUniqueId);
            var sheet = cat.Sheet(row.ExistingSheetUniqueId);
            if (view == null || row.ViewAction == ViewAction.Create) { row.PlaceAction = PlaceAction.Place; return; }

            if (string.IsNullOrEmpty(view.PlacedOnSheet)) { row.PlaceAction = PlaceAction.Place; return; }
            if (sheet != null && sheet.PlacedViewUniqueIds.Contains(view.UniqueId)) { row.PlaceAction = PlaceAction.Realign; return; }

            if (o?.PlaceAsDependent == true)
            {
                row.ViewAction = ViewAction.CreateDependent;
                row.PlaceAction = PlaceAction.Place;
                // A dependent copy needs a name of its own (the parent keeps its name).
                row.ViewName = (string.IsNullOrEmpty(row.ViewName) ? view.Name : row.ViewName) + " - " + (string.IsNullOrEmpty(row.SheetNumber) ? "Dependent" : row.SheetNumber);
                row.Warnings.Add("View is placed on sheet " + view.PlacedOnSheet + "; a dependent copy '" + row.ViewName + "' will be created and placed (explicit choice). The original view is not moved.");
                return;
            }
            row.PlaceAction = PlaceAction.Skip;
            row.Errors.Add("View '" + view.Name + "' is already placed on sheet " + view.PlacedOnSheet +
                           ". A plan view can be on one sheet only: exclude the row, choose another view, or choose 'Place as dependent copy'. CausaFixIt never removes a view from another sheet.");
        }

        private static void ResolveViewportAndTitle(SetupConfig cfg, SetupCatalog cat, Ctx c)
        {
            var row = c.Row;
            var comp = c.Comp;
            var m = c.Member;
            // Title visibility through the viewport type (a shared type is never edited).
            if (comp != null)
            {
                bool shows = comp.TitleVisibility == TitleVisibility.All || (comp.TitleVisibility == TitleVisibility.OnlyPrimary ? c.Role == MemberRole.Primary : m.ShowTitle);
                row.ShowsTitle = shows;
                if (!string.IsNullOrEmpty(m?.ViewportTypeUniqueId)) row.ViewportTypeUniqueId = m.ViewportTypeUniqueId;
                else if (!shows)
                {
                    if (string.IsNullOrEmpty(comp.HiddenTitleViewportTypeUniqueId))
                        row.Errors.Add("Composition '" + comp.Name + "' hides this member's title: choose the viewport type without title (Views on Sheet › Hidden-title viewport type).");
                    else row.ViewportTypeUniqueId = comp.HiddenTitleViewportTypeUniqueId;
                }
                if (m != null) { row.TitleExtraXmm = m.TitleOffsetXmm; row.TitleExtraYmm = m.TitleOffsetYmm; }
            }
            if (!string.IsNullOrEmpty(row.ViewportTypeUniqueId) && cat.ViewportType(row.ViewportTypeUniqueId) == null && cat.ViewportTypes.Count > 0)
                row.Errors.Add("Viewport type not found in this document. Choose another.");

            var vp = cat.ViewportOf(row.ExistingViewUniqueId, row.ExistingSheetUniqueId);
            if (vp != null)
            {
                row.ExistingViewportUniqueId = vp.UniqueId;
                if (!string.IsNullOrEmpty(row.ViewportTypeUniqueId) && vp.TypeUniqueId != row.ViewportTypeUniqueId) row.Updates.Add("viewport:type");
            }
            row.Title = ResolveTitle(cfg, row.GroupKey, comp, c.O);
            bool placed = row.PlaceAction == PlaceAction.Place || row.PlaceAction == PlaceAction.Realign;
            row.TitleAction = placed && row.ShowsTitle && row.Title.Applies ? TitleAction.Apply : TitleAction.None;
        }

        public static TitlePlacement ResolveTitle(SetupConfig cfg, string groupKey, SheetComposition comp, RowOverride o)
            => (o?.Title ?? comp?.Title ?? cfg.Group(groupKey)?.Title ?? cfg.Title ?? new TitlePlacement()).Clone();

        private static void BuildPlacements(SetupConfig cfg, SetupCatalog cat, SetupPlan plan, bool requireList = true)
        {
            if (cfg.Placements.Count == 0 && requireList) plan.GlobalErrors.Add("Add existing views and their target sheets (step Placement).");
            foreach (var p in cfg.Placements)
            {
                var view = cat.View(p.ViewUniqueId);
                var sheet = cat.Sheet(p.SheetUniqueId);
                var o = cfg.Override(PlacementRowKey(p.ViewUniqueId));
                var row = new PlanRow
                {
                    RowKey = PlacementRowKey(p.ViewUniqueId),
                    Include = p.Include,
                    GroupKey = p.GroupKey,
                    SlotName = p.SlotName,
                    ExistingViewUniqueId = p.ViewUniqueId,
                    ExistingSheetUniqueId = p.SheetUniqueId,
                    SheetKey = "existing|" + p.SheetUniqueId,
                    ViewAction = ViewAction.Reuse,
                    SheetAction = SheetAction.Reuse,
                    OverlayGroupId = string.IsNullOrWhiteSpace(p.OverlayGroupId) ? "" : "existing|" + p.SheetUniqueId + "|" + p.OverlayGroupId.Trim().ToUpperInvariant()
                };
                row.SheetPrimaryRowKey = row.RowKey;
                if (view == null) row.Errors.Add("View not found.");
                else
                {
                    row.ViewName = view.Name;
                    row.TitleOnSheet = view.TitleOnSheet;
                    row.EffectiveScale = view.Scale;
                    row.Family = view.Family ?? PlanFamily.FloorPlan;
                    row.LevelName = cat.Level(view.LevelUniqueId)?.Name ?? "";
                    row.LevelUniqueId = view.LevelUniqueId;
                    row.ViewSetName = "existing view";
                }
                if (sheet == null) row.Errors.Add("Choose the target sheet.");
                else { row.SheetNumber = row.CurrentSheetNumber = sheet.Number; row.SheetName = sheet.Name; }

                if (view != null && sheet != null)
                {
                    if (sheet.PlacedViewUniqueIds.Contains(view.UniqueId)) row.PlaceAction = PlaceAction.Realign;
                    else if (!string.IsNullOrEmpty(view.PlacedOnSheet))
                    {
                        row.PlaceAction = PlaceAction.Skip;
                        row.Errors.Add("View is already on sheet " + view.PlacedOnSheet + "; CausaFixIt never moves views between sheets silently.");
                    }
                    else row.PlaceAction = PlaceAction.Place;
                    var vp = cat.ViewportOf(view.UniqueId, sheet.UniqueId);
                    if (vp != null) row.ExistingViewportUniqueId = vp.UniqueId;
                }
                row.Title = ResolveTitle(cfg, row.GroupKey, null, o);
                row.TitleAction = (row.PlaceAction == PlaceAction.Place || row.PlaceAction == PlaceAction.Realign) && row.Title.Applies ? TitleAction.Apply : TitleAction.None;
                plan.Rows.Add(row);
            }
            if (cfg.Mode == SetupMode.PlaceRealignExisting) CheckOverlays(cfg, cat, plan);
        }

        /// <summary>Update Titles Only: nothing is created, renamed or moved; only approved title settings of existing viewports.</summary>
        private static void ToTitlesOnly(SetupPlan plan)
        {
            foreach (var r in plan.Rows)
            {
                bool hasViewport = !string.IsNullOrEmpty(r.ExistingViewportUniqueId);
                r.ViewAction = ViewAction.None;
                r.SheetAction = SheetAction.None;
                r.PlaceAction = PlaceAction.None;
                r.RenumberPending = false;
                bool typeChange = r.Updates.Contains("viewport:type");
                r.Updates.Clear();
                if (typeChange && hasViewport) r.Updates.Add("viewport:type");
                // Names, numbers and placement do not matter here.
                r.Errors.RemoveAll(e => !e.StartsWith("View title:", StringComparison.Ordinal) && !e.Contains("viewport type"));
                r.Warnings.Clear();
                if (!hasViewport) { r.TitleAction = TitleAction.None; r.Warnings.Add("No viewport of this view on its sheet yet: nothing to update (run Full Setup or Place first)."); continue; }
                r.TitleAction = r.ShowsTitle && r.Title != null && r.Title.Applies ? TitleAction.Apply : TitleAction.None;
                if (r.TitleAction == TitleAction.None && !typeChange) r.Warnings.Add("Title placement is 'Keep existing' (or the title is hidden): nothing to update.");
            }
        }

        // ------------------------------------------------------------------ checks

        private static void CheckCompositions(SetupConfig cfg, SetupCatalog cat, SetupPlan plan)
        {
            foreach (var g in cfg.Compositions.Where(c => c.Enabled).SelectMany(c => c.Members.Where(m => m.Enabled && !m.IsExistingView).Select(m => (c, m))).GroupBy(x => x.m.ViewSetKey).Where(g => g.Select(x => x.c.Key).Distinct().Count() > 1))
                plan.GlobalErrors.Add("View Set '" + cfg.Rule(g.Key)?.Name + "' is a member of several compositions (" + string.Join(", ", g.Select(x => x.c.Name).Distinct()) + "). A View Set can be on one composed sheet per level.");
            foreach (var c in cfg.Compositions.Where(c => c.Enabled))
            {
                if (c.Members.Count(m => m.Enabled) == 0) plan.GlobalWarnings.Add("Composition '" + c.Name + "' has no members.");
                foreach (var m in c.Members.Where(m => m.Enabled && !m.IsExistingView && cfg.Rule(m.ViewSetKey) == null))
                    plan.GlobalErrors.Add("Composition '" + c.Name + "': a member refers to a View Set that no longer exists.");
                if (c.Primary != null && c.Primary.IsExistingView) plan.GlobalErrors.Add("Composition '" + c.Name + "': the primary member must be a View Set (it defines the sheet name and number).");
                foreach (var dup in c.Members.Where(m => m.Enabled && m.IsExistingView).GroupBy(m => m.ExistingViewUniqueId).Where(g => g.Count() > 1))
                    plan.GlobalErrors.Add("Composition '" + c.Name + "': the same existing view is added twice.");
            }
        }

        private static void CheckOverlays(SetupConfig cfg, SetupCatalog cat, SetupPlan plan)
        {
            foreach (var g in plan.Rows.Where(r => r.Include && !string.IsNullOrEmpty(r.OverlayGroupId)).GroupBy(r => r.OverlayGroupId))
            {
                var rows = g.ToList();
                if (rows.Count < 2) continue;
                var scales = rows.Where(r => r.EffectiveScale > 0).Select(r => r.EffectiveScale).Distinct().ToList();
                if (scales.Count > 1)
                    foreach (var r in rows) r.Errors.Add("Overlay members must have the same scale (" + string.Join(" / ", scales.Select(s => "1:" + s)) + "). Choose compatible templates or scales.");
                var rot = rows.Select(r => r.Rotation).Distinct().Count();
                if (rot > 1) foreach (var r in rows) r.Errors.Add("Overlay members must have the same viewport rotation on the sheet.");
                int titles = rows.Count(r => r.ShowsTitle && r.Title != null && r.Title.Applies);
                if (titles > 1 && rows.Where(r => r.ShowsTitle).Select(r => r.TitleExtraXmm + "|" + r.TitleExtraYmm).Distinct().Count() < titles)
                    foreach (var r in rows.Where(r => r.ShowsTitle)) r.Warnings.Add("Several titles of this overlay are visible with the same placement: give each member a title offset (Views on Sheet) so they do not overlap.");
            }
        }

        private static void CheckPlacement(SetupConfig cfg, SetupCatalog cat, SetupPlan plan, string documentKey)
        {
            var placing = plan.Included.Where(r => r.PlaceAction == PlaceAction.Place || r.PlaceAction == PlaceAction.Realign).ToList();
            foreach (var row in placing)
            {
                var g = cfg.Group(row.GroupKey);
                if (g == null) { row.Errors.Add("Alignment group '" + row.GroupKey + "' does not exist."); continue; }
                row.Method = g.Method;
                var slot = g.Slot(row.SlotName);
                if (slot == null) row.Errors.Add("Slot '" + row.SlotName + "' is not defined in group '" + g.Key + "' (step Placement).");
                if (g.Method == AlignmentMethod.ModelAnchor)
                {
                    if (!g.Anchor.IsSet) row.Errors.Add("Group '" + g.Key + "' has no model anchor. Pick a grid intersection or a point (step Placement).");
                    else if (!string.IsNullOrEmpty(g.Anchor.DocumentKey) && !string.IsNullOrEmpty(documentKey) && g.Anchor.DocumentKey != documentKey)
                        row.Errors.Add("The model anchor of group '" + g.Key + "' was picked in another document. Pick it again in this model.");
                }
                else if (!string.IsNullOrEmpty(row.OverlayGroupId))
                    row.Errors.Add("Overlay needs Model Anchor alignment: viewport centres do not make the building coincide when crops differ (group '" + g.Key + "').");
            }
            // Two rows in the same slot of the same sheet overlap – unless they are members of the same intentional overlay.
            foreach (var dup in placing.Where(r => !string.IsNullOrEmpty(r.SheetKey)).GroupBy(r => r.SheetKey + "\u0001" + r.GroupKey + "\u0001" + r.SlotName.ToUpperInvariant()).Where(g => g.Count() > 1))
            {
                var ids = dup.Select(r => r.OverlayGroupId).Distinct().ToList();
                if (ids.Count == 1 && !string.IsNullOrEmpty(ids[0])) continue;
                foreach (var r in dup)
                    r.Errors.Add("Slot '" + r.SlotName + "' of sheet " + r.SheetNumber + " is used by " + dup.Count() + " rows that are not one overlay. Give each view its own slot, or make them an Overlay composition.");
            }
        }

        private static void CheckTitles(SetupConfig cfg, SetupCatalog cat, SetupPlan plan)
        {
            foreach (var r in plan.Included.Where(x => x.TitleAction == TitleAction.Apply))
            {
                var t = r.Title;
                if (t.Mode == TitleMode.MatchReference && string.IsNullOrEmpty(t.ReferenceDescription))
                    r.Errors.Add("View title: 'Match reference title' has no captured reference. Pick a reference title (step Placement › View Title Placement).");
                if (!t.IsFixed && t.RelativeTo == TitleRelativeTo.OverlayEnvelope && string.IsNullOrEmpty(r.OverlayGroupId))
                    r.Warnings.Add("View title: 'overlay envelope' applies to overlays only; this viewport uses its own box.");
                if (t.IsFixed && (t.FixedXmm < cfg.UsableLeftMm || t.FixedXmm > cfg.UsableRightMm || t.FixedYmm < cfg.UsableBottomMm || t.FixedYmm > cfg.UsableTopMm) && t.Frame == SheetFrame.Sheet)
                    r.Warnings.Add("View title: the fixed position (" + t.FixedXmm.ToString("0") + ", " + t.FixedYmm.ToString("0") + " mm) is outside the configured usable area.");
                if (t.LineMode == TitleLineMode.Set && Math.Abs(t.LineLengthMm) < 0.01)
                    r.Warnings.Add("View title: line length 0 does not hide the title; use a viewport type without title for that.");
            }
        }

        private static void CheckCollisions(SetupConfig cfg, SetupCatalog cat, SetupPlan plan)
        {
            var rows = plan.Included.ToList();

            // View names: unique per view type. Existing views renamed by another row free their name (swap handled by the executor).
            var renamedAway = new HashSet<string>(rows.Where(r => !string.IsNullOrEmpty(r.ExistingViewUniqueId) && r.Updates.Contains("view:name")).Select(r => r.ExistingViewUniqueId));
            var naming = rows.Where(r => r.ViewAction != ViewAction.None && r.ViewAction != ViewAction.Skip && !(r.RowKey.StartsWith("member|", StringComparison.Ordinal) && r.ViewAction == ViewAction.Reuse))
                             .Where(r => !string.IsNullOrEmpty(r.ViewName)).ToList();
            foreach (var r in naming)
            {
                string vt = r.Family.ToString();
                bool dependent = r.ViewAction == ViewAction.CreateDependent;
                var clash = cat.Views.FirstOrDefault(v => v.ViewType == vt && NameRules.SameName(v.Name, r.ViewName) && (dependent || v.UniqueId != r.ExistingViewUniqueId) && !renamedAway.Contains(v.UniqueId));
                if (clash != null) r.Errors.Add("View name '" + r.ViewName + "' is already used by another " + vt + " view. Change the name rule, edit the name or add a component.");
            }
            foreach (var g in naming.GroupBy(r => r.Family + "\u0001" + NameRules.Normalize(r.ViewName).ToUpperInvariant()).Where(g => g.Count() > 1))
                foreach (var r in g) r.Errors.Add("View name '" + r.ViewName + "' is proposed by " + g.Count() + " rows.");

            // Sheet numbers: unique among all sheets including placeholders. Rows of one sheet share its number; two
            // different sheets with the same number are a conflict (never merged).
            var sheetRows = rows.Where(r => r.IsSheetPrimary && (r.SheetAction == SheetAction.Create || r.SheetAction == SheetAction.Update)).ToList();
            var renumbered = new HashSet<string>(rows.Where(r => r.Updates.Contains("sheet:number")).Select(r => r.ExistingSheetUniqueId));
            foreach (var r in sheetRows.Where(r => r.SheetAction == SheetAction.Create || r.Updates.Contains("sheet:number")))
            {
                var clash = cat.Sheets.FirstOrDefault(s => NameRules.SameName(s.Number, r.SheetNumber) && s.UniqueId != r.ExistingSheetUniqueId && !renumbered.Contains(s.UniqueId));
                if (clash != null)
                    r.Errors.Add("Sheet number " + r.SheetNumber + " already exists" + (clash.IsPlaceholder ? " (placeholder)" : " ('" + clash.Name + "')") +
                                 ". Change the numbering, edit the number, or choose that sheet explicitly as an existing sheet.");
            }
            var allSheets = rows.Where(r => r.IsSheetPrimary && !string.IsNullOrEmpty(r.SheetNumber) && r.SheetAction != SheetAction.None && r.SheetAction != SheetAction.Skip).ToList();
            foreach (var g in allSheets.GroupBy(r => NameRules.Normalize(r.SheetNumber).ToUpperInvariant()).Where(g => g.Select(x => x.SheetKey).Distinct().Count() > 1))
                foreach (var r in g) r.Errors.Add("Sheet number " + r.SheetNumber + " is proposed for " + g.Select(x => x.SheetKey).Distinct().Count() + " different sheets. Sheets are shared only through a Views on Sheet composition, never by an equal number.");
            // Members inherit the sheet's errors so the whole sheet is visibly blocked.
            var bySheet = rows.Where(r => !string.IsNullOrEmpty(r.SheetKey)).GroupBy(r => r.SheetKey);
            foreach (var g in bySheet)
            {
                var primary = g.FirstOrDefault(r => r.IsSheetPrimary);
                if (primary == null) continue;
                var sheetErrors = primary.Errors.Where(e => e.StartsWith("Sheet", StringComparison.Ordinal) || e.StartsWith("Choose a title block", StringComparison.Ordinal) || e.StartsWith("Choose a numbering", StringComparison.Ordinal)).ToList();
                foreach (var r in g.Where(r => r != primary)) foreach (var e in sheetErrors) r.Errors.Add("(sheet) " + e);
            }
        }

        private static void Finish(PlanRow r)
        {
            if (!r.Include) { r.Status = RowStatus.Excluded; r.Errors.Clear(); return; }
            var distinct = r.Errors.Distinct().ToList();
            r.Errors.Clear();
            r.Errors.AddRange(distinct);
            var w = r.Warnings.Distinct().ToList();
            r.Warnings.Clear();
            r.Warnings.AddRange(w);
            if (r.Errors.Count > 0) r.Status = RowStatus.Error;
            else if (r.Warnings.Count > 0) r.Status = RowStatus.Warning;
            else if ((r.ViewAction == ViewAction.Reuse || r.ViewAction == ViewAction.None) && (r.SheetAction == SheetAction.Reuse || r.SheetAction == SheetAction.None)
                     && (r.PlaceAction == PlaceAction.Realign || r.PlaceAction == PlaceAction.None) && !r.Updates.Contains("viewport:type") && r.TitleAction != TitleAction.Apply) r.Status = RowStatus.Unchanged;
            else r.Status = RowStatus.Ready;
        }

        // ------------------------------------------------------------------ helpers for the UI

        /// <summary>Parameters of View, Sheet and Title Block instance scope used by any rule (their values are read on demand).</summary>
        public static List<ParamRef> ReferencedParams(SetupConfig cfg)
        {
            var rules = cfg.ViewSets.SelectMany(r => new[] { r.ViewNameRule, r.TitleRule, r.SheetNameRule })
                .Concat(cfg.NumberingProfiles.Select(p => p.Rule)).Concat(cfg.Compositions.Select(c => c.SheetNameRule)).Where(r => r != null);
            return rules.SelectMany(r => r.Components).Where(c => c.Source == ComponentSource.Parameter && c.Param != null)
                        .Select(c => c.Param).GroupBy(p => p.Key).Select(g => g.First()).ToList();
        }
    }
}
