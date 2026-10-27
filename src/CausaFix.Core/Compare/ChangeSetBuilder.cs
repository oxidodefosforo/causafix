using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CausaFix.Core.Model;
using CausaFix.Core.Values;

namespace CausaFix.Core.Compare
{
    /// <summary>
    /// Three-state comparison (original / proposed / current) and consolidation per real destination
    /// (document + owner UniqueId + parameter key; the key already includes the scope).
    ///
    /// Per cell:
    ///  - Blank cell: no proposal, the model keeps its current value (Clear needs the explicit token/action).
    ///  - Proposed == original: not an edit. Never a proposal, never a conflict.
    ///  - Edited: resolved against the live Revit state:
    ///      current == original  → Change (ready to write)
    ///      current == proposed  → AlreadyMatches
    ///      otherwise            → Conflict (changed in Revit since export; not written by default)
    ///    Editability is decided here from the real parameter, cell by cell — never from export metadata.
    ///
    /// Per destination (all sheets, all rows):
    ///  - one edited value (or several equivalent ones) → one entry, one write;
    ///  - two or more different edited values → ProposalConflict, zero writes until the user resolves it;
    ///  - any edited value of the group invalid → GroupError, zero writes for that destination.
    /// </summary>
    public static class ChangeSetBuilder
    {
        public const string MultipleValuesText = "Multiple conflicting values";

        public static ChangeSet Build(IEnumerable<ProposedCell> cells, ICurrentStateSource current, ChangeSet into = null)
        {
            var set = into ?? new ChangeSet();
            var pending = new List<ChangeEntry>();
            var stateCache = new Dictionary<string, CurrentState>(StringComparer.Ordinal);

            CurrentState StateOf(ProposedCell c)
            {
                string k = c.DocKey + "\u0001" + c.TargetUniqueId + "\u0001" + c.Column.Key;
                if (!stateCache.TryGetValue(k, out var st))
                {
                    st = string.IsNullOrEmpty(c.TargetUniqueId) ? CurrentState.Missing() : (current.Get(c.TargetUniqueId, c.Column.Key) ?? CurrentState.Missing());
                    stateCache[k] = st;
                }
                return st;
            }

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

                var e = NewEntry(cell);

                if (p.Kind == ProposalKind.Invalid)
                {
                    Block(e, cell.Column.IsContext ? BlockCode.ReadOnlyColumn : p.Code, p.Message);
                    pending.Add(e);
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
                    Block(e, BlockCode.NotApplicable, "The parameter did not exist on this element when the data was exported (N/A). Nothing to write.");
                    pending.Add(e);
                    continue;
                }
                if (cell.IsLinked)
                {
                    Block(e, BlockCode.Linked, "Linked model data is read-only. Edit it in the linked model.");
                    pending.Add(e);
                    continue;
                }

                var cur = StateOf(cell);
                if (cur.ElementExists && cur.OwnerMismatch && !cell.TargetIsType)
                {
                    if (set.RetargetWrongScope && cur.OwnerIsType == true && !string.IsNullOrEmpty(cur.OwnerUniqueId))
                    {
                        // Same parameter identity, real owner: the type. Visible in Review as a type target.
                        var col = cell.Column.Clone();
                        col.Key = cell.Column.Key.WithScope(ParamScope.Type);
                        var moved = cell.WithTarget(cur.OwnerUniqueId, true, col);
                        e = NewEntry(moved);
                        e.ScopeNote = "Scope corrected: the workbook has this column as an instance parameter, but on '" + cur.TargetLabel +
                                      "' Revit reports that the parameter belongs to its type '" + cur.OwnerLabel + "'. Compared and written on the type.";
                        set.ScopeCorrectedCells++;
                        cur = StateOf(moved);
                    }
                    else
                    {
                        e.State = cur;
                        e.Current = cur.Value;
                        Block(e, BlockCode.WrongScope, string.IsNullOrEmpty(cur.LockReason) ? "The parameter belongs to another element than the workbook's target." : cur.LockReason);
                        pending.Add(e);
                        continue;
                    }
                }
                e.State = cur;
                e.Current = cur.Value;
                e.CurrentEditable = cur.Editable;
                e.AffectedInstances = cur.AffectedInstances;
                if (!string.IsNullOrEmpty(cur.TargetLabel)) e.TargetLabel = cur.TargetLabel;

                if (!cur.ElementExists)
                    Block(e, BlockCode.MissingElement, "The element was not found in the model (deleted, or the workbook belongs to another model). Action: re-export from this model.");
                else if (cur.Ambiguous)
                    Block(e, BlockCode.Ambiguous, "Several parameters on this element match the column key (same name and data type). CausaFixIt cannot tell which one to write.");
                else if (cur.OwnerMismatch)
                    Block(e, BlockCode.WrongScope, cur.LockReason);
                else if (!cur.ParameterExists)
                    Block(e, BlockCode.MissingParameter, "The parameter was not found on the " + (cell.TargetIsType ? "type" : "element") + " any more (removed or renamed definition).");
                else if (Matches(cur.Value, e))
                {
                    e.Status = ChangeStatus.AlreadyMatches;
                    e.Reason = "The model already has this value. Nothing to write.";
                }
                else if (!ValueComparer.AreEqual(cur.Value, cell.Original))
                {
                    if (cur.Editable)
                    {
                        e.Status = ChangeStatus.Conflict;
                        e.Reason = "Changed in Revit after export (was " + cell.Original + ", now " + cur.Value + "). Not overwritten unless you include it.";
                    }
                    else Block(e, cur.LockCode == BlockCode.None ? BlockCode.ReadOnlyParameter : cur.LockCode, cur.LockReason);
                }
                else if (!cur.Editable)
                {
                    Block(e, cur.LockCode == BlockCode.None ? BlockCode.ReadOnlyParameter : cur.LockCode,
                          string.IsNullOrEmpty(cur.LockReason) ? "Revit does not allow writing this parameter on this element." : cur.LockReason);
                }
                else
                {
                    e.Status = ChangeStatus.Change;
                    e.Include = true;
                    e.Reason = e.TargetIsType ? "Ready to write on the type (all its instances change)." : "Ready to write.";
                }
                if (!string.IsNullOrEmpty(e.ScopeNote)) e.Reason = e.Reason + " " + e.ScopeNote;
                pending.Add(e);
            }

            if (set.ScopeCorrectedCells > 0)
                set.Issues.Add(new ImportIssue(IssueSeverity.Warning, "",
                    set.ScopeCorrectedCells + " cell(s) come from columns this workbook exported as instance parameters, but Revit reports them as type parameters. " +
                    "They are compared and written on the type (one value per type; different values in rows of the same type are a conflict). " +
                    "Re-export the schedule to get a workbook with the correct scope."));
            Consolidate(set, pending);
            int id = 1;
            foreach (var e in set.Entries)
            {
                e.Id = id++;
                e.Diagnostic = BuildDiagnostic(e);
            }
            return set;
        }

        private static bool Matches(RawValue current, ChangeEntry e)
            => e.IsClear ? current.IsEmpty : ValueComparer.AreEqual(current, e.Proposed);

        private static ChangeEntry NewEntry(ProposedCell cell)
        {
            var p = cell.Proposal ?? Proposal.None();
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
                IsClear = p.Kind == ProposalKind.Clear,
                Proposed = p.Kind == ProposalKind.Value ? p.Value : RawValue.Empty,
                Info = p.Info
            };
            e.ProposedText = p.Kind == ProposalKind.Invalid ? (p.SourceText ?? string.Empty) : null;
            e.Sources.Add(cell);
            return e;
        }

        private static void Block(ChangeEntry e, BlockCode code, string reason)
        {
            e.Status = ChangeStatus.Blocked;
            e.Code = code == BlockCode.None ? BlockCode.InvalidValue : code;
            e.Reason = reason;
            e.Include = false;
        }

        private static string GroupKey(ChangeEntry e)
        {
            var s = e.Sources[0];
            if (string.IsNullOrEmpty(e.TargetUniqueId))
                return "\u0002" + s.SheetKey + "\u0001" + s.RowKey + "\u0001" + e.Key + "\u0001" + s.ExcelRow;
            return s.DocKey + "\u0001" + e.TargetUniqueId + "\u0001" + e.Key;
        }

        /// <summary>One entry per destination; see class remarks for the rules.</summary>
        private static void Consolidate(ChangeSet set, List<ChangeEntry> pending)
        {
            var groups = new Dictionary<string, List<ChangeEntry>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var e in pending)
            {
                string k = GroupKey(e);
                if (!groups.TryGetValue(k, out var list)) { list = new List<ChangeEntry>(); groups[k] = list; order.Add(k); }
                list.Add(e);
            }

            foreach (var k in order)
            {
                var list = groups[k];
                var merged = list[0];
                for (int i = 1; i < list.Count; i++) merged.Sources.AddRange(list[i].Sources);
                if (list.Count == 1) { set.Entries.Add(merged); continue; }

                // Use the live state from any entry that queried it.
                var withState = list.FirstOrDefault(x => x.State != null);
                if (withState != null && merged.State == null)
                {
                    merged.State = withState.State;
                    merged.Current = withState.Current;
                    merged.CurrentEditable = withState.CurrentEditable;
                    merged.AffectedInstances = withState.AffectedInstances;
                }

                var invalid = list.Where(x => x.Status == ChangeStatus.Blocked && x.Code == BlockCode.InvalidValue).ToList();
                if (invalid.Count > 0)
                {
                    Block(merged, BlockCode.GroupError,
                        "One of the " + list.Count + " edited values for this " + (merged.TargetIsType ? "type parameter" : "parameter") +
                        " is invalid (" + string.Join("; ", invalid.Select(x => x.Sources[0].Location + ": " + x.Reason)) +
                        "). Nothing is written for this destination until all its edits are valid.");
                    merged.ProposedText = MultipleValuesText;
                    set.Entries.Add(merged);
                    continue;
                }

                var blocked = list.FirstOrDefault(x => x.Status == ChangeStatus.Blocked);
                if (blocked != null)
                {
                    Block(merged, blocked.Code, blocked.Reason);
                    set.Entries.Add(merged);
                    continue;
                }

                var distinct = new List<ChangeEntry>();
                foreach (var e in list)
                    if (!distinct.Any(d => SameProposal(d, e))) distinct.Add(e);

                if (distinct.Count > 1)
                {
                    merged.Status = ChangeStatus.ProposalConflict;
                    merged.Include = false;
                    merged.IsClear = false;
                    merged.Proposed = RawValue.Empty;
                    merged.ProposedText = MultipleValuesText;
                    merged.Reason = distinct.Count + " different values were edited for the same " +
                                    (merged.TargetIsType ? "type parameter" : "parameter") + ": " +
                                    string.Join(" | ", distinct.Select(d => "'" + (d.Sources[0].Proposal?.SourceText ?? d.Proposed.ToString()) + "' at " +
                                        string.Join(", ", list.Where(x => SameProposal(x, d)).Select(x => x.Sources[0].Location)))) +
                                    ". Nothing is written until one value is chosen or the workbook is corrected.";
                    foreach (var d in distinct)
                        merged.Alternatives.Add(d.IsClear ? Proposal.Clear(ImportOptions.DefaultClearToken) : Proposal.Of(d.Proposed, d.Sources[0].Proposal?.SourceText));
                }
                else if (list.Any(x => x.Status == ChangeStatus.Conflict))
                {
                    var c = list.First(x => x.Status == ChangeStatus.Conflict);
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
        /// Resolves a ProposalConflict by an explicit user choice. Never called automatically. The entry is
        /// re-evaluated against the live value; a concurrent Revit change still needs explicit inclusion.
        /// </summary>
        public static void ChooseAlternative(ChangeEntry entry, int alternativeIndex)
        {
            if (entry.Alternatives.Count == 0) throw new InvalidOperationException("Entry has no alternatives.");
            var alt = entry.Alternatives[alternativeIndex];
            entry.IsClear = alt.Kind == ProposalKind.Clear;
            entry.Proposed = entry.IsClear ? RawValue.Empty : alt.Value;
            entry.ProposedText = null;
            if (entry.IsClear ? entry.Current.IsEmpty : ValueComparer.AreEqual(entry.Current, entry.Proposed))
            {
                entry.Status = ChangeStatus.AlreadyMatches;
                entry.Include = false;
                entry.Reason = "Value chosen by the user; the model already has it.";
            }
            else if (!ValueComparer.AreEqual(entry.Current, entry.Original))
            {
                entry.Status = entry.CurrentEditable ? ChangeStatus.Conflict : ChangeStatus.Blocked;
                entry.Include = false;
                entry.Reason = "Value chosen by the user; it was also changed in Revit since export. Include it explicitly to overwrite.";
            }
            else
            {
                entry.Status = entry.CurrentEditable ? ChangeStatus.Change : ChangeStatus.Blocked;
                entry.Include = entry.CurrentEditable;
                entry.Reason = "Value chosen by the user.";
            }
            entry.Diagnostic = BuildDiagnostic(entry);
        }

        /// <summary>
        /// Write operations for the included entries. Re-validates that no destination is written twice and
        /// that no unresolved conflict or blocked entry can produce a write, whatever the UI did.
        /// </summary>
        public static List<WriteOp> ToWriteOps(ChangeSet set)
        {
            var ops = new List<WriteOp>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in set.Entries)
            {
                if (!e.WillWrite) continue;
                if (e.Status == ChangeStatus.ProposalConflict || e.Status == ChangeStatus.Blocked) continue;
                string k = (e.Sources.Count > 0 ? e.Sources[0].DocKey : DocKeys.Host) + "\u0001" + e.TargetUniqueId + "\u0001" + e.Key;
                if (!seen.Add(k)) throw new InvalidOperationException("Internal error: two writes for the same destination (" + e.TargetLabel + " / " + e.Column?.Header + ").");
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

        private static string BuildDiagnostic(ChangeEntry e)
        {
            var sb = new StringBuilder();
            var st = e.State;
            sb.Append("Status=").Append(e.Status);
            if (e.Status == ChangeStatus.Blocked) sb.Append(" Code=").Append(e.Code);
            sb.Append(" | Target=").Append(e.TargetIsType ? "Type " : "Instance ").Append(e.TargetUniqueId ?? "(none)")
              .Append(" | Key=").Append(e.Key).Append(" (").Append(e.Key.Source).Append(", ").Append(e.Key.Scope).Append(')')
              .Append(" | Column '").Append(e.Column?.Header).Append("' kind=").Append(e.Column?.Kind);
            if (!string.IsNullOrEmpty(e.ScopeNote)) sb.Append(" | ").Append(e.ScopeNote);
            if (!string.IsNullOrEmpty(e.Column?.UnitTypeId)) sb.Append(" unit=").Append(e.Column.UnitTypeId);
            if (st != null)
            {
                sb.Append(" | Revit: element=").Append(st.ElementExists ? "found" : "NOT found")
                  .Append(", parameter=").Append(st.Ambiguous ? "ambiguous" : st.ParameterExists ? "found '" + st.ParameterName + "'" : "NOT found");
                if (!string.IsNullOrEmpty(st.OwnerUniqueId))
                    sb.Append(", owner=").Append(st.OwnerIsType == true ? "type " : st.OwnerIsType == false ? "element " : "").Append('\'').Append(st.OwnerLabel).Append("' ").Append(st.OwnerUniqueId)
                      .Append(st.OwnerMismatch ? " (NOT the requested element)" : "");
                if (st.ParameterExists)
                {
                    sb.Append(", Definition.Name='").Append(st.ParameterName).Append('\'')
                      .Append(", IsShared=").Append(Fmt(st.IsShared));
                    if (!string.IsNullOrEmpty(st.ParameterGuid)) sb.Append(", GUID=").Append(st.ParameterGuid);
                    if (!string.IsNullOrEmpty(st.Binding)) sb.Append(", binding=").Append(st.Binding);
                    sb.Append(", StorageType=").Append(st.StorageType)
                      .Append(", HasValue=").Append(Fmt(st.HasValue))
                      .Append(", IsReadOnly=").Append(Fmt(st.IsReadOnly))
                      .Append(", UserModifiable=").Append(Fmt(st.UserModifiable))
                      .Append(", writable=").Append(st.Editable ? "yes" : "no (" + st.LockCode + ": " + st.LockReason + ")");
                    if (!string.IsNullOrEmpty(st.Evidence)) sb.Append(", checked: ").Append(st.Evidence);
                }
            }
            else sb.Append(" | Revit state not queried (blocked before resolution)");
            sb.Append(" | Original=").Append(e.Original.Encode())
              .Append(" Current=").Append(e.Current.Encode())
              .Append(" Proposed=").Append(e.IsClear ? "<<CLEAR>>" : e.ProposedText ?? e.Proposed.Encode());
            foreach (var s in e.Sources)
            {
                sb.Append(Environment.NewLine).Append("  source ").Append(s.Location)
                  .Append(": read '").Append(s.Proposal?.SourceText ?? "").Append("' (").Append(string.IsNullOrEmpty(s.CellKind) ? "?" : s.CellKind).Append(')')
                  .Append(", original ").Append(s.Original.Encode()).Append(s.OriginalMigrated ? " (migrated from schema 1)" : "")
                  .Append(", doc ").Append(s.DocKey);
                if (s.Proposal?.Kind == ProposalKind.Invalid) sb.Append(", parse error: ").Append(s.Proposal.Message);
            }
            return sb.ToString();
        }

        private static string Fmt(bool? b) => b.HasValue ? (b.Value ? "true" : "false") : "n/a";
    }
}
