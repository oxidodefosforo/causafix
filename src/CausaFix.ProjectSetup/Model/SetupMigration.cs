using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.ProjectSetup.Model
{
    /// <summary>
    /// Format 1 → 2 (idempotent, never renumbers, never changes a name):
    ///  • token patterns → equivalent builder rules (or Advanced rules with a note when not convertible);
    ///  • each Sheet Number pattern + sequence settings → a numbering profile with the same counter semantics;
    ///  • View Sets that shared sheets by number → explicit Views on Sheet compositions (see <see cref="MigrateSharedSheets"/>),
    ///    keeping the old sheet keys so the existing sheets stay associated, and their sequence numbers.
    /// </summary>
    public static class SetupMigration
    {
        public const string LegacyGlobalCounter = "legacy-global";
        public const string LegacyLevelCounter = "legacy-level";

        public static void Upgrade(SetupConfig cfg)
        {
            foreach (var r in cfg.ViewSets) r.EnsureRules();
            foreach (var r in cfg.ViewSets)
            {
                if (!string.IsNullOrEmpty(r.NumberingProfileKey) && cfg.Numbering(r.NumberingProfileKey) != null) continue;
                r.NumberingProfileKey = DerivedProfile(cfg, r).Key;
            }
            // Derived profiles nobody uses any more (their pattern was replaced) are dropped; user profiles are kept.
            var used = new HashSet<string>(cfg.ViewSets.Select(r => r.NumberingProfileKey)
                .Concat(cfg.Compositions.Select(c => c.NumberingProfileKey))
                .Concat(cfg.Overrides.Select(o => o.NumberingProfileKey)).Where(k => !string.IsNullOrEmpty(k)));
            cfg.NumberingProfiles.RemoveAll(p => p.DerivedFromLegacy && !used.Contains(p.Key));
            cfg.Format = SetupConfig.CurrentFormat;
        }

        private static string Signature(ViewSetRule r) => r.SheetNumberPattern + "\u0001" + r.Sequence.Start + "\u0001" + r.Sequence.Increment + "\u0001" + r.Sequence.Scope;

        private static NumberingProfile DerivedProfile(SetupConfig cfg, ViewSetRule r)
        {
            string sig = Signature(r);
            var existing = cfg.NumberingProfiles.FirstOrDefault(p => p.DerivedFromLegacy && p.Description == sig);
            if (existing != null) return existing;
            var p2 = new NumberingProfile
            {
                Name = UniqueName(cfg, "Numbering " + (string.IsNullOrWhiteSpace(r.SheetNumberPattern) ? r.Name : r.SheetNumberPattern)),
                Rule = LegacyPatternConverter.Convert(r.SheetNumberPattern, TextCase.AsTyped),
                Start = r.Sequence.Start,
                Increment = r.Sequence.Increment == 0 ? 1 : r.Sequence.Increment,
                Order = SequenceOrder.ViewSetThenLevel,
                DerivedFromLegacy = true,
                Description = sig
            };
            switch (r.Sequence.Scope)
            {
                case SequenceScope.Global: p2.CounterKey = LegacyGlobalCounter; break;
                case SequenceScope.PerLevel: p2.CounterKey = LegacyLevelCounter; p2.GroupBy.Add(NameField.LevelName); break;
                default: p2.GroupBy.Add(NameField.ViewSetName); break;
            }
            cfg.NumberingProfiles.Add(p2);
            return p2;
        }

        public static string UniqueName(SetupConfig cfg, string name)
        {
            string n = name, b = name;
            for (int i = 2; cfg.NumberingProfiles.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)); i++) n = b + " (" + i + ")";
            return n;
        }

        /// <summary>
        /// Converts format-1 "shared sheet by number" View Sets into explicit compositions. <paramref name="sheetKeysByRow"/>
        /// gives, for every legacy row (rule|level), the sheet key earlier versions used ("sheet|NUMBER"), computed by the
        /// plan builder from the old rules. Rules that met on a sheet become members of one composition (Separate mode);
        /// the old key is stored per level so the existing sheets stay associated; the sequence number of the primary row
        /// becomes the number of the composed sheet. Returns a description of what was migrated (empty = nothing).
        /// </summary>
        public static List<string> MigrateSharedSheets(SetupConfig cfg, IDictionary<string, string> sheetKeysByRow)
        {
            var notes = new List<string>();
            var shared = cfg.ViewSets.Where(r => r.SheetMode == SheetMode.SharedBySheetNumber && cfg.CompositionOf(r.Key) == null).ToList();
            if (shared.Count == 0) return notes;

            // Union rules that produced the same sheet key on some level.
            var parent = shared.ToDictionary(r => r.Key, r => r.Key);
            string Find(string k) { while (parent[k] != k) k = parent[k] = parent[parent[k]]; return k; }
            var byKey = new Dictionary<string, List<string>>();
            foreach (var kv in sheetKeysByRow)
            {
                string ruleKey = kv.Key.Split('|')[0];
                if (!parent.ContainsKey(ruleKey) || string.IsNullOrEmpty(kv.Value)) continue;
                if (!byKey.TryGetValue(kv.Value, out var list)) byKey[kv.Value] = list = new List<string>();
                list.Add(ruleKey);
            }
            foreach (var list in byKey.Values)
                for (int i = 1; i < list.Count; i++) { string a = Find(list[0]), b = Find(list[i]); if (a != b) parent[b] = a; }

            foreach (var set in shared.GroupBy(r => Find(r.Key)))
            {
                var rules = set.ToList();
                var comp = new SheetComposition { Name = string.Join(" + ", rules.Select(r => r.Name)), Mode = CompositionMode.Separate, TitleVisibility = TitleVisibility.All };
                foreach (var r in rules) comp.Members.Add(new CompositionMember { ViewSetKey = r.Key }); // slot "" = each View Set's own slot
                comp.PrimaryMemberId = comp.Members[0].Id;
                var primary = rules[0];
                foreach (var kv in sheetKeysByRow.Where(k => k.Key.StartsWith(primary.Key + "|", StringComparison.Ordinal) && !string.IsNullOrEmpty(k.Value)))
                {
                    string level = kv.Key.Substring(primary.Key.Length + 1);
                    comp.LegacySheetKeys[level] = kv.Value;
                    if (cfg.SequenceAssignments.TryGetValue(kv.Key, out int seq)) cfg.SequenceAssignments[comp.SheetKeyFor(level)] = seq;
                }
                // Row numbers of the old shared rules are replaced by the number of the composed sheet.
                foreach (var k in cfg.SequenceAssignments.Keys.Where(k => rules.Any(r => k.StartsWith(r.Key + "|", StringComparison.Ordinal))).ToList())
                {
                    cfg.SequenceAssignments.Remove(k);
                    cfg.SequenceScopes.Remove(k);
                }
                foreach (var r in rules) r.SheetMode = SheetMode.OnePerView; // the composition decides the sheet now
                cfg.Compositions.Add(comp);
                notes.Add("View Sets " + string.Join(", ", rules.Select(r => "'" + r.Name + "'")) + " shared sheets by number: now the composition '" + comp.Name + "' (sheet identity kept, numbers unchanged).");
            }
            return notes;
        }
    }
}
