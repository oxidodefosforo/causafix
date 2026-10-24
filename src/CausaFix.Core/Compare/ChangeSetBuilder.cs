using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.Core.Model;
using CausaFix.Core.Values;

namespace CausaFix.Core.Compare
{
    /// <summary>
    /// Three-state comparison (original / proposed / current) and consolidation per real target.
    ///
    /// Rules:
    ///  - Blank cell: no proposal, the model keeps its current value (Clear needs the explicit token/action).
    ///  - Proposed == original: no change; Revit's current value is kept even if it changed meanwhile.
    ///  - Proposed != original and current == original: Change.
    ///  - Proposed != original and current == proposed: AlreadyMatches (no write).
    ///  - Proposed != original and current is a third value: Conflict (not written by default).
    ///  - Several modified proposals for the same target+parameter that disagree: ProposalConflict.
    ///    Unmodified rows never count as proposals.
    /// </summary>
    public static class ChangeSetBuilder
    {
        public static ChangeSet Build(IEnumerable<ProposedCell> cells, ICurrentStateSource current, ChangeSet into = null)
        {
            var set = into ?? new ChangeSet();
            var pending = new List<(ProposedCell cell, ChangeEntry entry)>();

            foreach (var cell in cells)
            {
                var p = cell.Proposal ?? Proposal.None();
                if (p.Kind == ProposalKind.None)
                {
                    if (!cell.Original.IsEmpty && !cell.Original.IsNotApplicable) set.EmptyCellsIgnored++;
                    continue;
                }

                if (!string.IsNullOrEmpty(p.Info))
                    set.Issues.Add(new ImportIssue(IssueSeverity.Info, cell.Location, p.Info));

                if (p.Kind == ProposalKind.Invalid)
                {
                    pending.Add((cell, Blocked(cell, p.Message)));
                    continue;
                }

                bool sameAsOriginal = p.Kind == ProposalKind.Clear ? cell.Original.IsEmpty : ValueComparer.AreEqual(p.Value, cell.Original);
                if (sameAsOriginal)
                {
                    set.UnchangedCells++;
                    continue;
                }

                if (cell.Original.IsNotApplicable)
                {
                    pending.Add((cell, Blocked(cell, "The parameter did not exist on this element when the data was exported.")));
                    continue;
                }
                if (cell.IsLinked)
                {
                    pending.Add((cell, Blocked(cell, "Linked model data is read-only.")));
                    continue;
                }
                if (cell.Column.ColumnReadOnly)
                {
                    pending.Add((cell, Blocked(cell, "Read-only column: " + cell.Column.ReadOnlyReason)));
                    continue;
                }

                var cur = current.Get(cell.TargetUniqueId, cell.Column.Key) ?? CurrentState.Missing();
                var e = NewEntry(cell);
                e.Current = cur.Value;
                e.CurrentEditable = cur.Editable;
                e.AffectedInstances = cur.AffectedInstances;
                if (!string.IsNullOrEmpty(cur.TargetLabel)) e.TargetLabel = cur.TargetLabel;
                e.IsClear = p.Kind == ProposalKind.Clear;
                e.Proposed = e.IsClear ? RawValue.Empty : p.Value;
                e.Info = p.Info;

                if (!cur.ElementExists)
                {
                    e.Status = ChangeStatus.Blocked;
                    e.Reason = "The element no longer exists in the model (deleted or different model).";
                }
                else if (cur.Ambiguous)
                {
                    e.Status = ChangeStatus.Blocked;
                    e.Reason = "Several parameters on this element match the column key; the target is ambiguous.";
                }
                else if (!cur.ParameterExists)
                {
                    e.Status = ChangeStatus.Blocked;
                    e.Reason = "The parameter was not found on the element.";
                }
                else if (Matches(cur.Value, p))
                {
                    e.Status = ChangeStatus.AlreadyMatches;
                    e.Reason = "The model already has this value.";
                }
                else if (!ValueComparer.AreEqual(cur.Value, cell.Original))
                {
                    e.Status = cur.Editable ? ChangeStatus.Conflict : ChangeStatus.Blocked;
                    e.Reason = "Changed in Revit since export (was " + cell.Original + ", now " + cur.Value + ")." +
                               (cur.Editable ? " Not overwritten unless you choose to." : " " + cur.LockReason);
                }
                else if (!cur.Editable)
                {
                    e.Status = ChangeStatus.Blocked;
                    e.Reason = string.IsNullOrEmpty(cur.LockReason) ? "Parameter is read-only for this element." : cur.LockReason;
                }
                else
                {
                    e.Status = ChangeStatus.Change;
                    e.Include = true;
                }
                pending.Add((cell, e));
            }

            Consolidate(set, pending);
            int id = 1;
            foreach (var e in set.Entries) e.Id = id++;
            return set;
        }

        private static bool Matches(RawValue current, Proposal p)
            => p.Kind == ProposalKind.Clear ? current.IsEmpty : ValueComparer.AreEqual(current, p.Value);

        private static ChangeEntry NewEntry(ProposedCell cell)
        {
            var e = new ChangeEntry
            {
                TargetUniqueId = cell.TargetUniqueId,
                TargetIsType = cell.TargetIsType,
                TargetLabel = cell.RowLabel,
                ElementId = cell.ElementId,
                Key = cell.Column.Key,
                Column = cell.Column,
                Original = cell.Original,
                Current = RawValue.NotApplicable,
                Proposed = cell.Proposal != null && cell.Proposal.Kind == ProposalKind.Value ? cell.Proposal.Value : RawValue.Empty
            };
            e.Sources.Add(cell);
            return e;
        }

        private static ChangeEntry Blocked(ProposedCell cell, string reason)
        {
            var e = NewEntry(cell);
            e.Status = ChangeStatus.Blocked;
            e.Reason = reason;
            e.IsClear = cell.Proposal?.Kind == ProposalKind.Clear;
            return e;
        }

        /// <summary>Groups proposals by (target, parameter). Blocked entries stay per cell.</summary>
        private static void Consolidate(ChangeSet set, List<(ProposedCell cell, ChangeEntry entry)> pending)
        {
            var groups = new Dictionary<string, List<ChangeEntry>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var (_, e) in pending)
            {
                // Blocked cells are reported individually so each one shows its own location and reason.
                if (e.Status == ChangeStatus.Blocked)
                {
                    set.Entries.Add(e);
                    continue;
                }
                string k = e.TargetUniqueId + "\u0001" + e.Key;
                if (!groups.TryGetValue(k, out var list)) { list = new List<ChangeEntry>(); groups[k] = list; order.Add(k); }
                list.Add(e);
            }

            foreach (var k in order)
            {
                var list = groups[k];
                if (list.Count == 1) { set.Entries.Add(list[0]); continue; }

                var live = list;

                var distinct = new List<ChangeEntry>();
                foreach (var e in live)
                    if (!distinct.Any(d => SameProposal(d, e))) distinct.Add(e);

                var merged = live[0];
                for (int i = 1; i < live.Count; i++) merged.Sources.AddRange(live[i].Sources);

                if (distinct.Count > 1)
                {
                    merged.Status = ChangeStatus.ProposalConflict;
                    merged.Include = false;
                    merged.Reason = distinct.Count + " different values were proposed for the same " +
                                    (merged.TargetIsType ? "type parameter" : "parameter") + " (" + merged.SourceSummary + "). Choose one.";
                    foreach (var d in distinct)
                        merged.Alternatives.Add(d.IsClear ? Proposal.Clear(ImportOptions.DefaultClearToken) : Proposal.Of(d.Proposed, d.Sources[0].Proposal?.SourceText));
                }
                else if (live.Any(x => x.Status == ChangeStatus.Conflict))
                {
                    var c = live.First(x => x.Status == ChangeStatus.Conflict);
                    merged.Status = ChangeStatus.Conflict;
                    merged.Include = false;
                    merged.Reason = c.Reason;
                }
                set.Entries.Add(merged);
            }
        }

        private static bool SameProposal(ChangeEntry a, ChangeEntry b)
        {
            if (a.IsClear || b.IsClear) return a.IsClear == b.IsClear;
            return ValueComparer.AreEqual(a.Proposed, b.Proposed);
        }

        /// <summary>
        /// Resolves a ProposalConflict by picking one alternative. The entry is re-evaluated against the
        /// current model value; a concurrent Revit change still yields a Conflict that must be accepted explicitly.
        /// </summary>
        public static void ChooseAlternative(ChangeEntry entry, int alternativeIndex)
        {
            if (entry.Status != ChangeStatus.ProposalConflict && entry.Alternatives.Count == 0)
                throw new InvalidOperationException("Entry has no alternatives.");
            var alt = entry.Alternatives[alternativeIndex];
            entry.IsClear = alt.Kind == ProposalKind.Clear;
            entry.Proposed = entry.IsClear ? RawValue.Empty : alt.Value;
            if (entry.IsClear ? entry.Current.IsEmpty : ValueComparer.AreEqual(entry.Current, entry.Proposed))
            {
                entry.Status = ChangeStatus.AlreadyMatches;
                entry.Include = false;
                entry.Reason = "The model already has this value.";
            }
            else if (!ValueComparer.AreEqual(entry.Current, entry.Original))
            {
                entry.Status = entry.CurrentEditable ? ChangeStatus.Conflict : ChangeStatus.Blocked;
                entry.Include = false;
                entry.Reason = "Value chosen; it was also changed in Revit since export. Include it explicitly to overwrite.";
            }
            else
            {
                entry.Status = entry.CurrentEditable ? ChangeStatus.Change : ChangeStatus.Blocked;
                entry.Include = entry.CurrentEditable;
                entry.Reason = "Value chosen by the user.";
            }
        }

        public static List<WriteOp> ToWriteOps(ChangeSet set)
        {
            var ops = new List<WriteOp>();
            foreach (var e in set.Entries)
            {
                if (!e.WillWrite) continue;
                ops.Add(new WriteOp
                {
                    EntryId = e.Id,
                    TargetUniqueId = e.TargetUniqueId,
                    Key = e.Key,
                    Column = e.Column,
                    Clear = e.IsClear,
                    Value = e.Proposed,
                    ExpectedCurrent = e.Current,
                    Label = e.TargetLabel
                });
            }
            return ops;
        }
    }
}
