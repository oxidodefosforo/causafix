using System;
using System.Collections.Generic;
using System.Text;
using CausaFix.Core.Model;
using CausaFix.Core.Values;

namespace CausaFix.Core.Reports
{
    /// <summary>Why a write did not end in the model. Distinguishes Revit restrictions, identity problems and persistence.</summary>
    public enum WriteFailure
    {
        None = 0,
        /// <summary>Revit reports the parameter as read-only on its real owner.</summary>
        ReadOnly,
        /// <summary>Worksharing, group or document restriction on the owner.</summary>
        Locked,
        /// <summary>The identity matches more than one parameter, or instance and type both expose it.</summary>
        AmbiguousIdentity,
        /// <summary>The parameter belongs to another element than the approved destination (e.g. its type).</summary>
        WrongTarget,
        /// <summary>The element or the parameter no longer exists.</summary>
        Missing,
        /// <summary>The value changed in Revit after the review.</summary>
        ChangedSinceReview,
        /// <summary>The value does not fit the parameter (type, reference class).</summary>
        InvalidValue,
        /// <summary>Parameter.Set threw or returned false.</summary>
        SetFailed,
        /// <summary>Set reported success but the model does not hold the value (after Set, after regenerating or after commit).</summary>
        NotPersisted,
        /// <summary>The transaction or the group was rolled back.</summary>
        RolledBack
    }

    /// <summary>
    /// Evidence of one write, stage by stage, kept also when the operation is rolled back (Open report / diagnostic).
    /// Values are encoded RawValues (internal units).
    /// </summary>
    public sealed class WriteTrace
    {
        public string Stage { get; set; } = string.Empty;
        public string Document { get; set; } = string.Empty;
        public string RowElement { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string OwnerUniqueId { get; set; } = string.Empty;
        public string ParameterName { get; set; } = string.Empty;
        public string ColumnHeader { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Guid { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string StorageType { get; set; } = string.Empty;
        public string Binding { get; set; } = string.Empty;
        public bool? IsShared { get; set; }
        public bool? IsReadOnly { get; set; }
        public bool? UserModifiable { get; set; }
        public bool? HasValue { get; set; }
        public string TransactionContext { get; set; } = string.Empty;
        public string Original { get; set; } = string.Empty;
        public string Expected { get; set; } = string.Empty;
        public string Proposed { get; set; } = string.Empty;
        public string BeforeSet { get; set; }
        public bool? SetReturned { get; set; }
        public string AfterSet { get; set; }
        public string AfterRegenerate { get; set; }
        public string AfterCommit { get; set; }
        public string Exception { get; set; } = string.Empty;
        public List<string> Notes { get; } = new List<string>();

        private static string B(bool? b) => b.HasValue ? (b.Value ? "true" : "false") : "n/a";

        public string ToText()
        {
            var sb = new StringBuilder();
            void Add(string k, string v) { if (!string.IsNullOrEmpty(v)) sb.Append(sb.Length == 0 ? "" : " | ").Append(k).Append('=').Append(v); }
            Add("stage", Stage);
            Add("document", Document);
            Add("row element", RowElement);
            Add("owner", Owner + (string.IsNullOrEmpty(OwnerUniqueId) ? "" : " " + OwnerUniqueId));
            Add("column", ColumnHeader);
            Add("parameter", ParameterName);
            Add("key", Key);
            Add("GUID", Guid);
            Add("scope", Scope);
            Add("StorageType", StorageType);
            Add("binding", Binding);
            sb.Append(" | IsShared=").Append(B(IsShared)).Append(" IsReadOnly=").Append(B(IsReadOnly))
              .Append(" UserModifiable=").Append(B(UserModifiable)).Append(" HasValue=").Append(B(HasValue));
            Add("transaction", TransactionContext);
            Add("original", Original);
            Add("current at review", Expected);
            Add("proposed", Proposed);
            Add("before Set", BeforeSet);
            if (SetReturned.HasValue) Add("Set returned", B(SetReturned));
            Add("after Set", AfterSet);
            Add("after regenerate", AfterRegenerate);
            Add("after commit", AfterCommit);
            Add("exception", Exception);
            foreach (var n in Notes) Add("note", n);
            return sb.ToString();
        }
    }

    /// <summary>One parameter reference inside an open transaction (implemented over Revit; faked in tests).</summary>
    public interface IWriteTarget
    {
        /// <summary>Value through the reference used for Set.</summary>
        RawValue Read();
        /// <summary>Calls the Set overload of the real StorageType. Returns Revit's result; Revit exceptions propagate.</summary>
        bool Set(bool clear, RawValue value);
        /// <summary>Regenerates (inside the open transaction), gets the element and the parameter again by identity and reads it.</summary>
        RawValue RegenerateAndReadFresh();
    }

    /// <summary>
    /// Stage-by-stage write: before Set → Set (return + exception) → read through the same reference → when it differs,
    /// regenerate and read a fresh reference. Never reports success unless the model holds the value. The post-commit check
    /// is done by the caller with a fresh query after the transaction commits.
    /// </summary>
    public static class WriteVerifier
    {
        public static string Show(RawValue v)
        {
            if (v.IsNotApplicable) return "(not applicable)";
            if (v.IsEmpty) return "(empty)";
            return v.Kind == RawKind.Text ? "'" + v.Text + "'" : v.ToString();
        }

        public static bool Holds(RawValue current, bool clear, RawValue value) => clear ? current.IsEmpty : ValueComparer.AreEqual(current, value);

        public static WriteFailure Run(IWriteTarget t, bool clear, RawValue value, WriteTrace trace, out string message)
        {
            message = null;
            string expected = clear ? "(empty)" : Show(value);
            trace.BeforeSet = t.Read().Encode();
            trace.Stage = "Set";
            bool ok;
            try { ok = t.Set(clear, value); }
            catch (WriteRejectedException ex)
            {
                trace.Exception = ex.Detail;
                message = ex.Message;
                return ex.Failure;
            }
            trace.SetReturned = ok;
            if (!ok)
            {
                message = "Set failed: Parameter.Set returned false (Revit did not accept " + expected + ").";
                return WriteFailure.SetFailed;
            }
            var after = t.Read();
            trace.AfterSet = after.Encode();
            trace.Stage = "read after Set";
            if (Holds(after, clear, value)) { trace.Stage = "written"; return WriteFailure.None; }

            RawValue fresh;
            trace.Stage = "regenerate";
            try { fresh = t.RegenerateAndReadFresh(); }
            catch (WriteRejectedException ex)
            {
                trace.Exception = ex.Detail;
                message = "Write not verified: regenerating failed (" + ex.Message + "). Read after Set: " + Show(after) + ".";
                return ex.Failure == WriteFailure.SetFailed ? WriteFailure.NotPersisted : ex.Failure;
            }
            trace.AfterRegenerate = fresh.Encode();
            if (Holds(fresh, clear, value))
            {
                trace.Notes.Add("the reference read right after Set returned " + Show(after) + "; a fresh read after regenerating has the value");
                trace.Stage = "written";
                return WriteFailure.None;
            }
            trace.Stage = "after regenerate";
            message = "Write not persisted: expected " + expected + "; read " + Show(after) + " after Set and " + Show(fresh) + " after regenerating" +
                      (string.IsNullOrEmpty(trace.Owner) ? "" : " (owner '" + trace.Owner + "'" + (string.IsNullOrEmpty(trace.Guid) ? "" : ", GUID " + trace.Guid) + ")") +
                      ". Parameter.Set returned true. See the diagnostic.";
            return WriteFailure.NotPersisted;
        }
    }

    /// <summary>A Revit rejection captured with its full text.</summary>
    public sealed class WriteRejectedException : Exception
    {
        public WriteRejectedException(WriteFailure failure, string message, string detail) : base(message) { Failure = failure; Detail = detail ?? string.Empty; }
        public WriteFailure Failure { get; }
        public string Detail { get; }
    }
}
