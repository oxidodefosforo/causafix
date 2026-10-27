using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;

namespace CausaFix.Revit
{
    /// <summary>Reads the current model state for the three-state comparison (runs in API context).</summary>
    public sealed class CurrentStateReader : ICurrentStateSource
    {
        private readonly Document _doc;
        private readonly ParamAccess _access;
        private readonly ParamOwner _owners;
        private readonly Dictionary<string, Element> _elements = new Dictionary<string, Element>(StringComparer.Ordinal);
        private Dictionary<long, int> _typeCounts;

        public CurrentStateReader(Document doc)
        {
            _doc = doc;
            _access = new ParamAccess(doc);
            _owners = new ParamOwner(doc);
        }

        private Element Find(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            if (!_elements.TryGetValue(uid, out var e))
            {
                e = _doc.GetElement(uid);
                _elements[uid] = e;
            }
            return e;
        }

        public CurrentState Get(string targetUniqueId, ParamKey key)
        {
            Element e = Find(targetUniqueId);
            if (e == null) return CurrentState.Missing();
            var state = new CurrentState { ElementExists = true, TargetLabel = ModelReader.Label(e, _doc) };
            OwnerResolution or;
            try { or = _owners.Resolve(e, key); }
            catch (Exception ex)
            {
                // Keep the original error: a resolution failure is not a read-only parameter.
                state.ParameterExists = false;
                state.Value = RawValue.NotApplicable;
                state.LockCode = BlockCode.MissingParameter;
                state.LockReason = "Parameter resolution failed: " + ex.GetType().Name + ": " + ex.Message;
                return state;
            }
            if (or.Resolution == ParamIdentity.Resolution.Ambiguous) { state.Ambiguous = true; state.Value = RawValue.NotApplicable; state.LockCode = BlockCode.Ambiguous; state.Evidence = or.Evidence; return state; }
            if (or.Resolution == ParamIdentity.Resolution.NotFound || or.Parameter == null) { state.ParameterExists = false; state.Value = RawValue.NotApplicable; state.LockCode = BlockCode.MissingParameter; return state; }

            Parameter p = or.Parameter;
            Element owner = or.Owner ?? e;
            state.ParameterExists = true;
            state.ParameterName = p.Definition?.Name ?? string.Empty;
            state.StorageType = p.StorageType.ToString();
            state.HasValue = p.HasValue;
            state.IsReadOnly = p.IsReadOnly;
            try { state.UserModifiable = p.UserModifiable; } catch (Exception) { state.UserModifiable = null; }
            try { state.IsShared = p.IsShared; if (p.IsShared) state.ParameterGuid = p.GUID.ToString("D"); } catch (Exception) { state.IsShared = null; }
            state.Binding = or.Binding;
            state.OwnerUniqueId = owner.UniqueId;
            state.OwnerLabel = ModelReader.Label(owner, _doc);
            state.OwnerIsType = owner is ElementType;
            state.Evidence = or.Evidence;
            state.Value = ValueIO.ReadRaw(p);
            if (p.StorageType == StorageType.ElementId && state.Value.Kind == RawKind.ElementId)
                state.Display = References.Format(_doc.GetElement(RevitCompat.Id(state.Value.Integer))?.Name, state.Value.Integer);

            if (or.ScopeAmbiguous)
            {
                state.Ambiguous = true;
                state.LockCode = BlockCode.Ambiguous;
                state.LockReason = "Ambiguous owner: " + or.Evidence + ". CausaFixIt does not choose one.";
                return state;
            }
            if (or.OwnerMismatch)
            {
                // The workbook aims at this element, but the value lives on another one (normally its type).
                state.OwnerMismatch = true;
                state.Editable = false;
                state.LockCode = BlockCode.WrongScope;
                state.LockReason = "Wrong destination: the workbook targets '" + state.TargetLabel + "', but this parameter belongs to " +
                                   (owner is ElementType ? "its type '" : "'") + state.OwnerLabel + "' (" + or.Evidence + "). Re-export to get the correct scope.";
                return state;
            }

            var lockInfo = _access.Lock(e, p, false);
            state.Editable = lockInfo == null;
            state.LockReason = lockInfo?.Reason ?? string.Empty;
            state.LockCode = lockInfo?.Code ?? BlockCode.None;
            if (lockInfo?.Code == BlockCode.ReadOnlyParameter)
            {
                string ro = _owners.ReadOnlyEvidence(p, owner);
                state.Evidence = (string.IsNullOrEmpty(state.Evidence) ? "" : state.Evidence + "; ") + ro;
                state.LockReason = "Revit reports IsReadOnly = true for this parameter on " + (owner is ElementType ? "type" : "element") + " '" + state.OwnerLabel + "'. Checked: " + ro + ".";
            }
            if (e is ElementType) state.AffectedInstances = InstancesOf(e.Id);
            return state;
        }

        /// <summary>Instances of a type in the whole document (not only the exported selection).</summary>
        private int InstancesOf(ElementId typeId)
        {
            if (_typeCounts == null)
            {
                _typeCounts = new Dictionary<long, int>();
                foreach (Element x in new FilteredElementCollector(_doc).WhereElementIsNotElementType())
                {
                    ElementId t = x.GetTypeId();
                    if (!RevitCompat.IsValid(t)) continue;
                    long k = RevitCompat.Value(t);
                    _typeCounts[k] = _typeCounts.TryGetValue(k, out int n) ? n + 1 : 1;
                }
            }
            return _typeCounts.TryGetValue(RevitCompat.Value(typeId), out int c) ? c : 0;
        }
    }

    /// <summary>Collects Revit failure messages; errors roll the transaction back, warnings are left to Revit (not deleted).</summary>
    internal sealed class FailureCollector : IFailuresPreprocessor
    {
        public List<string> Messages { get; } = new List<string>();
        public bool HadErrors { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool error = false;
            foreach (FailureMessageAccessor m in accessor.GetFailureMessages())
            {
                FailureSeverity sev = m.GetSeverity();
                Messages.Add(sev + ": " + m.GetDescriptionText());
                if (sev != FailureSeverity.Warning) error = true;
            }
            if (error)
            {
                HadErrors = true;
                return FailureProcessingResult.ProceedWithRollBack;
            }
            return FailureProcessingResult.Continue;
        }
    }

    /// <summary>
    /// Applies approved changes. Default mode is atomic: any failure rolls back the whole operation.
    /// "Valid only" applies each change in a sub-transaction and reports the ones that failed.
    /// One TransactionGroup is assimilated so the user can undo everything with a single Undo.
    /// Every write is verified stage by stage (WriteVerifier) and again after the transaction commits, with a fresh query,
    /// before the group is assimilated: a value that did not persist is never reported as Applied. Never saves or synchronises.
    /// </summary>
    public static class ApplyEngine
    {
        public static ApplyResult Apply(Document doc, IList<WriteOp> ops, ApplyMode mode, IUnitConverter units)
        {
            var result = new ApplyResult
            {
                Mode = mode,
                StartedUtc = DateTime.UtcNow,
                RevitVersion = SafeText(() => doc.Application.VersionNumber + " (" + doc.Application.VersionBuild + ")"),
                AddinBuild = BuildInfo.Text,
                DocumentTitle = SafeText(() => doc.Title)
            };
            if (ops.Count == 0)
            {
                result.Summary = "Nothing to apply.";
                result.FinishedUtc = DateTime.UtcNow;
                return result;
            }
            if (doc.IsReadOnly) throw new InvalidOperationException("The document is read-only.");
            if (doc.IsModifiable) throw new InvalidOperationException("Another transaction is open; finish it in Revit first.");

            var access = new ParamAccess(doc);
            var owners = new ParamOwner(doc);
            var collector = new FailureCollector();
            var written = new List<(WriteOp op, OpResult r)>();
            using (var group = new TransactionGroup(doc, "CausaFixIt Excel Link: apply " + ops.Count + " change(s)"))
            {
                group.Start();
                bool commitGroup = false;
                using (var t = new Transaction(doc, "CausaFixIt Excel Link"))
                {
                    FailureHandlingOptions fho = t.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(collector);
                    fho.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(fho);
                    t.Start();

                    bool abort = false;
                    foreach (var op in ops)
                    {
                        var r = new OpResult
                        {
                            EntryId = op.EntryId,
                            TargetUniqueId = op.TargetUniqueId,
                            Label = op.Label,
                            Parameter = op.Column?.Header ?? op.Key.ToString(),
                            OldValue = ValueFormatter.Format(op.ExpectedCurrent, op.Column, units),
                            NewValue = op.Clear ? "(cleared)" : ValueFormatter.Format(op.Value, op.Column, units)
                        };
                        r.Trace.Document = result.DocumentTitle;
                        r.Trace.ColumnHeader = op.Column?.Header ?? string.Empty;
                        r.Trace.Key = op.Key.ToString();
                        r.Trace.Scope = op.Key.Scope.ToString();
                        r.Trace.Expected = op.ExpectedCurrent.Encode();
                        r.Trace.Proposed = op.Clear ? "<<CLEAR>>" : op.Value.Encode();
                        r.Trace.TransactionContext = "Transaction '" + t.GetName() + "' open (document modifiable: " + doc.IsModifiable + ")" + (mode == ApplyMode.ValidOnly ? " + SubTransaction" : "");
                        result.Ops.Add(r);
                        if (abort) { r.Outcome = OpOutcome.Skipped; r.Message = "Not attempted: an earlier change failed (atomic mode)."; continue; }

                        WriteFailure failure = Precheck(doc, access, owners, op, units, r.Trace, out Parameter p, out string message);
                        if (failure == WriteFailure.None)
                        {
                            var target = new ParameterWriteTarget(doc, owners, op.TargetUniqueId, op.Key, p);
                            if (mode == ApplyMode.ValidOnly)
                            {
                                using (var st = new SubTransaction(doc))
                                {
                                    st.Start();
                                    failure = WriteVerifier.Run(target, op.Clear, op.Value, r.Trace, out message);
                                    if (failure == WriteFailure.None) st.Commit(); else st.RollBack();
                                }
                            }
                            else
                            {
                                failure = WriteVerifier.Run(target, op.Clear, op.Value, r.Trace, out message);
                            }
                        }

                        if (failure == WriteFailure.None) { r.Outcome = OpOutcome.Applied; written.Add((op, r)); }
                        else
                        {
                            r.Outcome = OpOutcome.Failed;
                            r.Failure = failure;
                            r.Message = message;
                            if (mode == ApplyMode.Atomic) abort = true;
                        }
                    }

                    if (abort)
                    {
                        t.RollBack();
                        result.TransactionStatus = "RolledBack";
                        result.MarkRolledBack("Rolled back: atomic mode and at least one change failed.");
                    }
                    else
                    {
                        TransactionStatus status = t.Commit();
                        result.TransactionStatus = status.ToString() + (collector.HadErrors ? " (Revit reported errors)" : "");
                        if (status == TransactionStatus.Committed && !collector.HadErrors)
                        {
                            // Fresh query after commit: the value must be in the model, not only in the reference used for Set.
                            int lost = VerifyAfterCommit(doc, owners, written);
                            if (lost > 0 && mode == ApplyMode.Atomic)
                                result.MarkRolledBack("Rolled back: " + lost + " value(s) were not in the model after commit (atomic mode).");
                            else commitGroup = true;
                        }
                        else
                        {
                            result.MarkRolledBack("Revit rolled back the transaction (" + status + ").");
                        }
                    }
                }
                if (commitGroup)
                {
                    TransactionStatus gs = group.Assimilate();
                    result.GroupStatus = gs.ToString();
                    if (gs == TransactionStatus.Committed) result.Committed = true;
                    else result.MarkRolledBack("The TransactionGroup did not commit (" + gs + ").");
                }
                else
                {
                    result.GroupStatus = group.RollBack().ToString();
                }
            }

            result.RevitMessages.AddRange(collector.Messages);
            result.FinishedUtc = DateTime.UtcNow;
            result.Summary = result.Committed && result.Applied > 0
                ? result.Applied + " change(s) applied" + (result.Failed > 0 ? ", " + result.Failed + " failed and skipped" : "") + ". Undo is available in Revit as one step."
                : "No change was applied (" + (result.Failed > 0 ? result.Failed + " failure(s)" : "transaction rolled back") + ").";
            return result;
        }

        /// <summary>Fresh element + parameter for every written change after commit; a missing value turns the op into a failure.</summary>
        private static int VerifyAfterCommit(Document doc, ParamOwner owners, List<(WriteOp op, OpResult r)> written)
        {
            int lost = 0;
            foreach (var (op, r) in written)
            {
                RawValue now;
                string problem = null;
                Element e = doc.GetElement(op.TargetUniqueId);
                var res = e == null ? null : owners.Resolve(e, op.Key);
                if (res == null || !res.Found) { now = RawValue.NotApplicable; problem = "the parameter could not be resolved again after commit"; }
                else now = ValueIO.ReadRaw(res.Parameter);
                r.Trace.AfterCommit = now.Encode();
                if (problem == null && WriteVerifier.Holds(now, op.Clear, op.Value)) continue;
                lost++;
                r.Outcome = OpOutcome.Failed;
                r.Failure = WriteFailure.NotPersisted;
                r.Trace.Stage = "after commit";
                r.Message = "Write not persisted: expected " + (op.Clear ? "(empty)" : WriteVerifier.Show(op.Value)) + "; read " + WriteVerifier.Show(now) +
                            " after the transaction committed" + (problem == null ? "" : " (" + problem + ")") +
                            ". Something reset it at commit (another add-in's updater, a family constraint or a Revit rule): see the diagnostic and the Revit messages.";
            }
            return lost;
        }

        private static string SafeText(Func<string> f) { try { return f() ?? string.Empty; } catch (Exception) { return string.Empty; } }

        /// <summary>Re-validates the target just before writing against its real owner. Fills the identity part of the trace.</summary>
        private static WriteFailure Precheck(Document doc, ParamAccess access, ParamOwner owners, WriteOp op, IUnitConverter units, WriteTrace trace, out Parameter p, out string message)
        {
            p = null;
            message = null;
            trace.Stage = "precheck";
            Element e = doc.GetElement(op.TargetUniqueId);
            if (e == null) { message = "The element no longer exists."; return WriteFailure.Missing; }
            trace.RowElement = ModelReader.Label(e, doc) + " " + e.UniqueId;
            var res = owners.Resolve(e, op.Key);
            if (res.Parameter != null)
            {
                var rp = res.Parameter;
                trace.ParameterName = rp.Definition?.Name ?? string.Empty;
                trace.StorageType = rp.StorageType.ToString();
                trace.IsReadOnly = rp.IsReadOnly;
                trace.HasValue = rp.HasValue;
                try { trace.UserModifiable = rp.UserModifiable; } catch (Exception) { }
                try { trace.IsShared = rp.IsShared; if (rp.IsShared) trace.Guid = rp.GUID.ToString("D"); } catch (Exception) { }
                trace.Binding = res.Binding;
                if (res.Owner != null) { trace.Owner = ModelReader.Label(res.Owner, doc) + (res.Owner is ElementType ? " (type)" : ""); trace.OwnerUniqueId = res.Owner.UniqueId; }
            }
            if (!string.IsNullOrEmpty(res.Evidence)) trace.Notes.Add("owner check: " + res.Evidence);
            if (res.Resolution == ParamIdentity.Resolution.Ambiguous || res.ScopeAmbiguous) { message = "Ambiguous identity: " + (string.IsNullOrEmpty(res.Evidence) ? "several parameters match" : res.Evidence) + "."; return WriteFailure.AmbiguousIdentity; }
            if (res.OwnerMismatch) { message = "Wrong destination: the approved target '" + ModelReader.Label(e, doc) + "' does not own this parameter (" + res.Evidence + "). Nothing was written; the scope is not changed during Apply."; return WriteFailure.WrongTarget; }
            if (res.Resolution != ParamIdentity.Resolution.Found || res.Parameter == null) { message = "The parameter is no longer on the element."; return WriteFailure.Missing; }
            p = res.Parameter;
            var lockInfo = access.Lock(e, p, false);
            if (lockInfo != null)
            {
                message = "Not editable now: " + lockInfo.Reason + (lockInfo.Code == BlockCode.ReadOnlyParameter ? " Checked: " + owners.ReadOnlyEvidence(p, e) + "." : "");
                return lockInfo.Code == BlockCode.ReadOnlyParameter ? WriteFailure.ReadOnly : lockInfo.Code == BlockCode.Unsupported ? WriteFailure.InvalidValue : WriteFailure.Locked;
            }
            if (p.StorageType == StorageType.ElementId && !op.Clear)
            {
                string refError = ReferenceCatalog.Validate(doc, op.Value.Integer, op.Column?.RefClass, p);
                if (refError != null) { message = refError; return WriteFailure.InvalidValue; }
            }
            RawValue now = ValueIO.ReadRaw(p);
            if (!ValueComparer.AreEqual(now, op.ExpectedCurrent))
            {
                message = "Changed in Revit after the review (now " + ValueFormatter.Format(now, op.Column, units) + "). Run the comparison again.";
                return WriteFailure.ChangedSinceReview;
            }
            return WriteFailure.None;
        }
    }
}
