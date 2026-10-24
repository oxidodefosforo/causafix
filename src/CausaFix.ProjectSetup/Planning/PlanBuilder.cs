using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.ProjectSetup.Planning
{
    /// <summary>
    /// Turns the configuration into the Review plan without touching the document: expands levels × View Sets,
    /// resolves names and numbers, keeps sequences stable, detects reuse (by stored association), collisions,
    /// template/scale conflicts and placement problems. The executor re-validates everything inside Revit.
    /// </summary>
    public static class PlanBuilder
    {
        public static string RowKey(string ruleKey, string levelUniqueId) => ruleKey + "|" + levelUniqueId;
        public static string PlacementRowKey(string viewUniqueId) => "place|" + viewUniqueId;
        public static string SharedSheetKey(string sheetNumber) => "sheet|" + NameRules.Normalize(sheetNumber).ToUpperInvariant();

        public static SetupPlan Build(SetupConfig cfg, SetupCatalog cat, string currentDocumentKey)
        {
            var plan = new SetupPlan { Mode = cfg.Mode };
            if (cfg.Mode == SetupMode.PlaceRealignExisting) BuildPlacements(cfg, cat, plan);
            else BuildGenerated(cfg, cat, plan);

            if (cfg.Mode == SetupMode.FullSetup || cfg.Mode == SetupMode.PlaceRealignExisting) CheckPlacement(cfg, cat, plan, currentDocumentKey);
            CheckCollisions(cfg, cat, plan);
            foreach (var r in plan.Rows) Finish(r);
            return plan;
        }

        // ------------------------------------------------------------------ generated rows

        private static void BuildGenerated(SetupConfig cfg, SetupCatalog cat, SetupPlan plan)
        {
            var levels = cfg.Levels.Where(l => l.Selected).OrderBy(l => l.Order).ThenBy(l => cat.Level(l.LevelUniqueId)?.Elevation ?? 0).ToList();
            if (levels.Count == 0) plan.GlobalErrors.Add("Select at least one level (step Levels).");
            var rules = cfg.ViewSets.Where(r => r.Enabled).ToList();
            if (rules.Count == 0) plan.GlobalErrors.Add("Enable at least one View Set (step View Sets).");

            foreach (var lv in levels)
                if (cat.Level(lv.LevelUniqueId) == null)
                    plan.GlobalErrors.Add("Level '" + lv.LevelName + "' is not in this document (host levels only; CausaFix does not create or copy levels). Map it in Levels.");

            string projectNumber = !string.IsNullOrWhiteSpace(cfg.ProjectNumberOverride) ? cfg.ProjectNumberOverride : cat.ProjectNumber;
            var combos = new List<(ViewSetRule rule, LevelSelection level, LevelInfo info)>();
            foreach (var rule in rules)
                foreach (var lv in levels)
                {
                    if (rule.LevelUniqueIds.Count > 0 && !rule.LevelUniqueIds.Contains(lv.LevelUniqueId)) continue;
                    var info = cat.Level(lv.LevelUniqueId);
                    if (info == null) continue;
                    combos.Add((rule, lv, info));
                }

            AssignSequences(cfg, combos.Select(c => (c.rule, c.level)).ToList());

            foreach (var (rule, lv, info) in combos)
            {
                string key = RowKey(rule.Key, lv.LevelUniqueId);
                var o = cfg.Override(key);
                var row = new PlanRow
                {
                    RowKey = key,
                    RuleKey = rule.Key,
                    LevelUniqueId = lv.LevelUniqueId,
                    LevelName = info.Name,
                    LevelCode = lv.Code,
                    ViewSetName = rule.Name,
                    Family = rule.Family,
                    ViewFamilyTypeUniqueId = rule.ViewFamilyTypeUniqueId,
                    TemplateUniqueId = o?.TemplateUniqueId ?? rule.TemplateUniqueId,
                    RequestedScale = rule.Scale,
                    TitleBlockUniqueId = o?.TitleBlockUniqueId ?? rule.TitleBlockUniqueId,
                    ViewportTypeUniqueId = rule.ViewportTypeUniqueId,
                    Rotation = rule.Rotation,
                    GroupKey = rule.AlignmentGroupKey,
                    SlotName = o?.SlotName ?? rule.SlotName,
                    Crop = rule.Crop,
                    ScopeBoxUniqueId = rule.ScopeBoxUniqueId,
                    Include = o?.Include ?? true,
                    Sequence = cfg.SequenceAssignments.TryGetValue(key, out int seq) ? seq : (int?)null
                };
                var tokens = new TokenValues
                {
                    ProjectNumber = projectNumber,
                    DisciplineCode = rule.DisciplineCode,
                    ViewSetCode = rule.Code,
                    ViewSetName = rule.Name,
                    LevelName = info.Name,
                    LevelCode = lv.Code,
                    Zone = !string.IsNullOrWhiteSpace(lv.Zone) ? lv.Zone : rule.Zone,
                    Sequence = row.Sequence
                };

                bool wantsViews = cfg.Mode != SetupMode.SheetsOnly;
                bool wantsSheets = cfg.Mode != SetupMode.ViewsOnly && rule.SheetMode != SheetMode.None;

                row.ViewName = Name(o?.ViewName, rule.ViewNamePattern, tokens, NameContext.ViewName, rule.ViewNameCase, row, wantsViews);
                row.TitleOnSheet = string.IsNullOrWhiteSpace(rule.TitleOnSheetPattern) && o?.TitleOnSheet == null ? string.Empty
                                 : Name(o?.TitleOnSheet, rule.TitleOnSheetPattern, tokens, NameContext.TitleOnSheet, TextCase.AsTyped, row, wantsViews);
                if (wantsSheets)
                {
                    row.SheetNumber = Name(o?.SheetNumber, rule.SheetNumberPattern, tokens, NameContext.SheetNumber, TextCase.AsTyped, row, true);
                    row.SheetName = Name(o?.SheetName, rule.SheetNamePattern, tokens, NameContext.SheetName, rule.SheetNameCase, row, true);
                }

                if (wantsViews) ResolveView(cfg, cat, rule, row, o);
                if (wantsSheets) ResolveSheet(cfg, cat, rule, row, o);
                if (cfg.Mode == SetupMode.FullSetup && wantsSheets && row.ViewAction != ViewAction.Skip) ResolvePlacement(cat, row, o);
                plan.Rows.Add(row);
            }
        }

        private static string Name(string overrideValue, string pattern, TokenValues t, NameContext ctx, TextCase textCase, PlanRow row, bool required)
        {
            if (!required) return string.Empty;
            if (overrideValue != null)
            {
                string v = NameRules.Normalize(overrideValue);
                if (v.Length == 0 && ctx != NameContext.TitleOnSheet) row.Errors.Add(ctx + " is empty.");
                string bad = NameRules.InvalidCharacters(v, ctx);
                if (bad.Length > 0) row.Errors.Add(ctx + " '" + v + "' contains characters Revit does not allow: " + bad);
                return v;
            }
            var r = NamePattern.Expand(pattern, t, ctx, textCase);
            row.Errors.AddRange(r.Errors);
            return r.Text;
        }

        // ------------------------------------------------------------------ sequences

        /// <summary>
        /// Gives each new row the next free number of its scope; rows that already have a number keep it, so adding
        /// levels never renumbers existing sheets. Use <see cref="RegenerateSequences"/> to renumber explicitly.
        /// </summary>
        public static void AssignSequences(SetupConfig cfg, IList<(ViewSetRule rule, LevelSelection level)> orderedCombos)
        {
            var used = new Dictionary<string, HashSet<int>>();
            string Scope(ViewSetRule r, LevelSelection l) =>
                r.Sequence.Scope == SequenceScope.Global ? "global" : r.Sequence.Scope == SequenceScope.PerViewSet ? "set|" + r.Key : "level|" + l.LevelUniqueId;

            foreach (var (rule, level) in orderedCombos)
            {
                string scope = Scope(rule, level);
                if (!used.ContainsKey(scope)) used[scope] = new HashSet<int>();
                if (cfg.SequenceAssignments.TryGetValue(RowKey(rule.Key, level.LevelUniqueId), out int n)) used[scope].Add(n);
            }
            foreach (var (rule, level) in orderedCombos)
            {
                string key = RowKey(rule.Key, level.LevelUniqueId);
                if (cfg.SequenceAssignments.ContainsKey(key)) continue;
                string scope = Scope(rule, level);
                int inc = rule.Sequence.Increment == 0 ? 1 : rule.Sequence.Increment;
                int candidate = rule.Sequence.Start;
                while (used[scope].Contains(candidate)) candidate += inc;
                used[scope].Add(candidate);
                cfg.SequenceAssignments[key] = candidate;
            }
        }

        /// <summary>Explicit "Regenerate numbering": forgets stored numbers of the given rows and assigns them again in order.</summary>
        public static void RegenerateSequences(SetupConfig cfg, IEnumerable<string> rowKeys)
        {
            foreach (var k in rowKeys.ToList()) cfg.SequenceAssignments.Remove(k);
        }

        // ------------------------------------------------------------------ views

        private static void ResolveView(SetupConfig cfg, SetupCatalog cat, ViewSetRule rule, PlanRow row, RowOverride o)
        {
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
            row.EffectiveScale = rule.Scale;
            if (tpl != null)
            {
                row.TemplateName = tpl.Name;
                if (tpl.Family.HasValue && tpl.Family.Value != rule.Family)
                    row.Errors.Add("Template '" + tpl.Name + "' is a " + tpl.Family + " template; it cannot be applied to a " + rule.Family + ".");
                if (tpl.ControlsScale)
                {
                    row.EffectiveScale = tpl.Scale;
                    if (tpl.Scale != rule.Scale) row.Warnings.Add("Scale is controlled by template '" + tpl.Name + "': effective 1:" + tpl.Scale + " (requested 1:" + rule.Scale + "). The template is not changed.");
                    row.ScaleNote = "from template";
                }
                if (rule.Crop == CropOption.ScopeBox && tpl.ControlsScopeBox)
                    row.Warnings.Add("The template controls the scope box; the requested scope box will not apply.");
            }
            if (rule.Crop == CropOption.ScopeBox && string.IsNullOrEmpty(rule.ScopeBoxUniqueId))
                row.Errors.Add("Crop = Scope box but no scope box is chosen.");

            // Reuse: explicit choice, else stored association. Names are only hints, never identity.
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

        private static void ResolveSheet(SetupConfig cfg, SetupCatalog cat, ViewSetRule rule, PlanRow row, RowOverride o)
        {
            var tb = cat.TitleBlock(row.TitleBlockUniqueId);
            if (tb != null) row.TitleBlockName = tb.FamilyName + " : " + tb.TypeName;

            ExistingSheetInfo existing = null;
            if (rule.SheetMode == SheetMode.ExistingSheet || !string.IsNullOrEmpty(o?.ExistingSheetUniqueId))
            {
                existing = cat.Sheet(o?.ExistingSheetUniqueId);
                if (existing == null)
                {
                    row.Errors.Add(rule.SheetMode == SheetMode.ExistingSheet ? "Choose the existing sheet for this row." : "The sheet chosen for this row no longer exists.");
                    row.SheetAction = SheetAction.Skip;
                    return;
                }
                row.SheetKey = "existing|" + existing.UniqueId;
            }
            else
            {
                row.SheetKey = rule.SheetMode == SheetMode.SharedBySheetNumber ? SharedSheetKey(row.SheetNumber) : row.RowKey;
                var assoc = cat.Sheets.Where(s => s.SetupRowKey == row.SheetKey).ToList();
                if (assoc.Count > 1) row.Errors.Add("Several sheets are associated with this row (" + string.Join(", ", assoc.Select(a => a.Number)) + ").");
                else if (assoc.Count == 1) existing = assoc[0];
            }

            if (existing == null)
            {
                if (tb == null) row.Errors.Add(string.IsNullOrEmpty(row.TitleBlockUniqueId) ? "Choose a title block (step Sheets)." : "Title block type not found in this document. Load the family or choose another.");
                row.SheetAction = SheetAction.Create;
                return;
            }

            row.ExistingSheetUniqueId = existing.UniqueId;
            if (existing.IsPlaceholder) row.Errors.Add("Sheet " + existing.Number + " is a placeholder; CausaFix does not convert placeholders.");
            bool explicitExisting = rule.SheetMode == SheetMode.ExistingSheet || !string.IsNullOrEmpty(o?.ExistingSheetUniqueId);
            if (!explicitExisting)
            {
                if (existing.Number != row.SheetNumber) row.Updates.Add("sheet:number");
                if (existing.Name != row.SheetName) row.Updates.Add("sheet:name");
            }
            else
            {
                row.SheetNumber = existing.Number;
                row.SheetName = existing.Name;
            }
            row.SheetAction = row.Updates.Any(u => u.StartsWith("sheet")) ? SheetAction.Update : SheetAction.Reuse;
        }

        // ------------------------------------------------------------------ placement

        private static void ResolvePlacement(SetupCatalog cat, PlanRow row, RowOverride o)
        {
            if (row.SheetAction == SheetAction.Skip) { row.PlaceAction = PlaceAction.Skip; return; }
            var view = cat.View(row.ExistingViewUniqueId);
            var sheet = cat.Sheet(row.ExistingSheetUniqueId);
            if (view == null) { row.PlaceAction = PlaceAction.Place; return; }

            if (string.IsNullOrEmpty(view.PlacedOnSheet)) { row.PlaceAction = PlaceAction.Place; return; }
            if (sheet != null && sheet.PlacedViewUniqueIds.Contains(view.UniqueId)) { row.PlaceAction = PlaceAction.Realign; return; }

            if (o?.PlaceAsDependent == true)
            {
                row.ViewAction = ViewAction.CreateDependent;
                row.PlaceAction = PlaceAction.Place;
                row.Warnings.Add("View is placed on sheet " + view.PlacedOnSheet + "; a dependent copy will be created and placed (explicit choice).");
                return;
            }
            row.PlaceAction = PlaceAction.Skip;
            row.Errors.Add("View '" + view.Name + "' is already placed on sheet " + view.PlacedOnSheet +
                           ". A plan view can be on one sheet only: exclude the row, or choose 'Place as dependent copy'.");
        }

        private static void BuildPlacements(SetupConfig cfg, SetupCatalog cat, SetupPlan plan)
        {
            if (cfg.Placements.Count == 0) plan.GlobalErrors.Add("Add existing views and their target sheets (step Placement).");
            foreach (var p in cfg.Placements)
            {
                var view = cat.View(p.ViewUniqueId);
                var sheet = cat.Sheet(p.SheetUniqueId);
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
                    SheetAction = SheetAction.Reuse
                };
                if (view == null) row.Errors.Add("View not found.");
                else
                {
                    row.ViewName = view.Name;
                    row.TitleOnSheet = view.TitleOnSheet;
                    row.EffectiveScale = view.Scale;
                    row.Family = view.Family ?? PlanFamily.FloorPlan;
                    row.LevelName = cat.Level(view.LevelUniqueId)?.Name ?? "";
                    row.LevelUniqueId = view.LevelUniqueId;
                }
                if (sheet == null) row.Errors.Add("Choose the target sheet.");
                else { row.SheetNumber = sheet.Number; row.SheetName = sheet.Name; }

                if (view != null && sheet != null)
                {
                    if (sheet.PlacedViewUniqueIds.Contains(view.UniqueId)) row.PlaceAction = PlaceAction.Realign;
                    else if (!string.IsNullOrEmpty(view.PlacedOnSheet))
                    {
                        row.PlaceAction = PlaceAction.Skip;
                        row.Errors.Add("View is already on sheet " + view.PlacedOnSheet + "; CausaFix never moves views between sheets silently.");
                    }
                    else row.PlaceAction = PlaceAction.Place;
                }
                plan.Rows.Add(row);
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
            }
            // Two rows in the same slot of the same sheet would overlap.
            foreach (var dup in placing.Where(r => !string.IsNullOrEmpty(r.SheetKey)).GroupBy(r => r.SheetKey + "\u0001" + r.GroupKey + "\u0001" + r.SlotName.ToUpperInvariant()).Where(g => g.Count() > 1))
                foreach (var r in dup)
                    r.Errors.Add("Slot '" + r.SlotName + "' of sheet " + r.SheetNumber + " is used by " + dup.Count() + " rows. Give each view on a shared sheet its own slot.");
        }

        // ------------------------------------------------------------------ collisions

        private static void CheckCollisions(SetupConfig cfg, SetupCatalog cat, SetupPlan plan)
        {
            var rows = plan.Included.ToList();

            // View names: unique per view type. Existing views renamed by another row free their name (swap handled by the executor).
            var renamedAway = new HashSet<string>(rows.Where(r => !string.IsNullOrEmpty(r.ExistingViewUniqueId) && r.Updates.Contains("view:name")).Select(r => r.ExistingViewUniqueId));
            foreach (var r in rows.Where(r => r.ViewAction != ViewAction.None && r.ViewAction != ViewAction.Skip && !string.IsNullOrEmpty(r.ViewName)))
            {
                string vt = r.Family.ToString();
                var clash = cat.Views.FirstOrDefault(v => v.ViewType == vt && NameRules.SameName(v.Name, r.ViewName) && v.UniqueId != r.ExistingViewUniqueId && !renamedAway.Contains(v.UniqueId));
                if (clash != null) r.Errors.Add("View name '" + r.ViewName + "' is already used by another " + vt + " view. Change the pattern, edit the name or add a suffix.");
            }
            foreach (var g in rows.Where(r => r.ViewAction != ViewAction.None && r.ViewAction != ViewAction.Skip && !string.IsNullOrEmpty(r.ViewName))
                                  .GroupBy(r => r.Family + "\u0001" + NameRules.Normalize(r.ViewName).ToUpperInvariant()).Where(g => g.Count() > 1))
                foreach (var r in g) r.Errors.Add("View name '" + r.ViewName + "' is proposed by " + g.Count() + " rows.");

            // Sheet numbers: unique among all sheets including placeholders. Shared sheets legitimately repeat the number.
            var sheetRows = rows.Where(r => r.SheetAction == SheetAction.Create || r.SheetAction == SheetAction.Update).ToList();
            var renumbered = new HashSet<string>(rows.Where(r => r.Updates.Contains("sheet:number")).Select(r => r.ExistingSheetUniqueId));
            foreach (var r in sheetRows)
            {
                var clash = cat.Sheets.FirstOrDefault(s => NameRules.SameName(s.Number, r.SheetNumber) && s.UniqueId != r.ExistingSheetUniqueId && !renumbered.Contains(s.UniqueId));
                if (clash != null)
                    r.Errors.Add("Sheet number " + r.SheetNumber + " already exists" + (clash.IsPlaceholder ? " (placeholder)" : " ('" + clash.Name + "')") +
                                 ". Change the pattern/number, or choose that sheet explicitly as an existing sheet.");
            }
            foreach (var g in sheetRows.GroupBy(r => NameRules.Normalize(r.SheetNumber).ToUpperInvariant()).Where(g => g.Select(x => x.SheetKey).Distinct().Count() > 1))
                foreach (var r in g) r.Errors.Add("Sheet number " + r.SheetNumber + " is proposed for " + g.Select(x => x.SheetKey).Distinct().Count() + " different sheets.");
            // Rows sharing one sheet must agree on its name and title block.
            foreach (var g in rows.Where(r => r.SheetAction == SheetAction.Create).GroupBy(r => r.SheetKey).Where(g => g.Count() > 1))
            {
                if (g.Select(r => r.SheetName).Distinct().Count() > 1)
                    foreach (var r in g) r.Errors.Add("Rows on shared sheet " + r.SheetNumber + " propose different sheet names.");
                if (g.Select(r => r.TitleBlockUniqueId).Distinct().Count() > 1)
                    foreach (var r in g) r.Errors.Add("Rows on shared sheet " + r.SheetNumber + " use different title blocks.");
            }
        }

        private static void Finish(PlanRow r)
        {
            if (!r.Include) { r.Status = RowStatus.Excluded; r.Errors.Clear(); return; }
            var distinct = r.Errors.Distinct().ToList();
            r.Errors.Clear();
            r.Errors.AddRange(distinct);
            if (r.Errors.Count > 0) r.Status = RowStatus.Error;
            else if (r.Warnings.Count > 0) r.Status = RowStatus.Warning;
            else if ((r.ViewAction == ViewAction.Reuse || r.ViewAction == ViewAction.None) && (r.SheetAction == SheetAction.Reuse || r.SheetAction == SheetAction.None)
                     && (r.PlaceAction == PlaceAction.Realign || r.PlaceAction == PlaceAction.None)) r.Status = RowStatus.Unchanged;
            else r.Status = RowStatus.Ready;
        }
    }
}
