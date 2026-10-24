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
        private readonly Dictionary<string, Element> _elements = new Dictionary<string, Element>(StringComparer.Ordinal);
        private Dictionary<long, int> _typeCounts;

        public CurrentStateReader(Document doc)
        {
            _doc = doc;
            _access = new ParamAccess(doc);
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
            ParamIdentity.Resolution res;
            Parameter p;
            try { res = ParamIdentity.Resolve(e, key, out p); }
            catch (Exception ex)
            {
                // Keep the original error: a resolution failure is not a read-only parameter.
                state.ParameterExists = false;
                state.Value = RawValue.NotApplicable;
                state.LockCode = BlockCode.MissingParameter;
                state.LockReason = "Parameter resolution failed: " + ex.GetType().Name + ": " + ex.Message;
                return state;
            }
            if (res == ParamIdentity.Resolution.Ambiguous) { state.Ambiguous = true; state.Value = RawValue.NotApplicable; state.LockCode = BlockCode.Ambiguous; return state; }
            if (res == ParamIdentity.Resolution.NotFound) { state.ParameterExists = false; state.Value = RawValue.NotApplicable; state.LockCode = BlockCode.MissingParameter; return state; }
            state.ParameterExists = true;
            state.ParameterName = p.Definition?.Name ?? string.Empty;
            state.StorageType = p.StorageType.ToString();
            state.HasValue = p.HasValue;
            state.IsReadOnly = p.IsReadOnly;
            try { state.UserModifiable = p.UserModifiable; } catch (Exception) { state.UserModifiable = null; }
            state.Value = ValueIO.ReadRaw(p);
            if (p.StorageType == StorageType.ElementId && state.Value.Kind == RawKind.ElementId)
                state.Display = References.Format(_doc.GetElement(RevitCompat.Id(state.Value.Integer))?.Name, state.Value.Integer);
            var lockInfo = _access.Lock(e, p, false);
            state.Editable = lockInfo == null;
            state.LockReason = lockInfo?.Reason ?? string.Empty;
            state.LockCode = lockInfo?.Code ?? BlockCode.None;
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
    /// Never saves or synchronises the model.
    /// </summary>
    public static class ApplyEngine
    {
        public static ApplyResult Apply(Document doc, IList<WriteOp> ops, ApplyMode mode, IUnitConverter units)
        {
            var result = new ApplyResult { Mode = mode, StartedUtc = DateTime.UtcNow };
            if (ops.Count == 0)
            {
                result.Summary = "Nothing to apply.";
                result.FinishedUtc = DateTime.UtcNow;
                return result;
            }
            if (doc.IsReadOnly) throw new InvalidOperationException("The document is read-only.");
            if (doc.IsModifiable) throw new InvalidOperationException("Another transaction is open; finish it in Revit first.");

            var access = new ParamAccess(doc);
            var collector = new FailureCollector();
            using (var group = new TransactionGroup(doc, "CausaFix Excel Link: apply " + ops.Count + " change(s)"))
            {
                group.Start();
                bool commitGroup = false;
                using (var t = new Transaction(doc, "CausaFix Excel Link"))
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
                        result.Ops.Add(r);
                        if (abort) { r.Outcome = OpOutcome.Skipped; r.Message = "Not attempted: an earlier change failed (atomic mode)."; continue; }

                        string failure = Precheck(doc, access, op, units, out Parameter p);
                        if (failure == null)
                        {
                            if (mode == ApplyMode.ValidOnly)
                            {
                                using (var st = new SubTransaction(doc))
                                {
                                    st.Start();
                                    failure = ValueIO.Write(p, op.Clear, op.Value);
                                    if (failure == null) st.Commit(); else st.RollBack();
                                }
                            }
                            else
                            {
                                failure = ValueIO.Write(p, op.Clear, op.Value);
                            }
                        }

                        if (failure == null) r.Outcome = OpOutcome.Applied;
                        else
                        {
                            r.Outcome = OpOutcome.Failed;
                            r.Message = failure;
                            if (mode == ApplyMode.Atomic) abort = true;
                        }
                    }

                    if (abort)
                    {
                        t.RollBack();
                        result.MarkRolledBack("Rolled back: atomic mode and at least one change failed.");
                    }
                    else
                    {
                        TransactionStatus status = t.Commit();
                        if (status == TransactionStatus.Committed && !collector.HadErrors)
                        {
                            result.Committed = true;
                            commitGroup = true;
                        }
                        else
                        {
                            result.MarkRolledBack("Revit rolled back the transaction (" + status + ").");
                        }
                    }
                }
                if (commitGroup) group.Assimilate(); else group.RollBack();
            }

            result.RevitMessages.AddRange(collector.Messages);
            result.FinishedUtc = DateTime.UtcNow;
            result.Summary = result.Committed
                ? result.Applied + " change(s) applied" + (result.Failed > 0 ? ", " + result.Failed + " failed and skipped" : "") + ". Undo is available in Revit as one step."
                : "No change was applied (" + (result.Failed > 0 ? result.Failed + " failure(s)" : "transaction rolled back") + ").";
            return result;
        }

        /// <summary>Re-validates the target just before writing. Returns null when the write may proceed.</summary>
        private static string Precheck(Document doc, ParamAccess access, WriteOp op, IUnitConverter units, out Parameter p)
        {
            p = null;
            Element e = doc.GetElement(op.TargetUniqueId);
            if (e == null) return "The element no longer exists.";
            var res = ParamIdentity.Resolve(e, op.Key, out p);
            if (res == ParamIdentity.Resolution.Ambiguous) return "Ambiguous parameter on this element.";
            if (res != ParamIdentity.Resolution.Found) return "The parameter is no longer on the element.";
            var lockInfo = access.Lock(e, p, false);
            if (lockInfo != null) return "Not editable now: " + lockInfo.Reason;
            if (p.StorageType == StorageType.ElementId && !op.Clear)
            {
                string refError = ReferenceCatalog.Validate(doc, op.Value.Integer, op.Column?.RefClass, p);
                if (refError != null) return refError;
            }
            RawValue now = ValueIO.ReadRaw(p);
            if (!ValueComparer.AreEqual(now, op.ExpectedCurrent))
                return "Changed in Revit after the review (now " + ValueFormatter.Format(now, op.Column, units) + "). Run the comparison again.";
            return null;
        }
    }
}
