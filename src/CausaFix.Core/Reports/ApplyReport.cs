using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CausaFix.Core.Compare;
using CausaFix.Core.Values;

namespace CausaFix.Core.Reports
{
    public enum ApplyMode
    {
        /// <summary>All approved changes or none: any failure rolls back the whole operation.</summary>
        Atomic = 0,
        /// <summary>Apply each valid change; failures are skipped and reported.</summary>
        ValidOnly
    }

    public enum OpOutcome
    {
        /// <summary>Written and committed.</summary>
        Applied = 0,
        /// <summary>Not attempted or skipped (changed since review, not editable anymore...).</summary>
        Skipped,
        /// <summary>Attempted and failed.</summary>
        Failed,
        /// <summary>Was written inside the transaction but the transaction was rolled back: NOT applied.</summary>
        RolledBack
    }

    public sealed class OpResult
    {
        public int EntryId { get; set; }
        public string TargetUniqueId { get; set; }
        public string Label { get; set; }
        public string Parameter { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public OpOutcome Outcome { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class ApplyResult
    {
        public ApplyMode Mode { get; set; }
        public bool Committed { get; set; }
        public string Summary { get; set; } = string.Empty;
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime FinishedUtc { get; set; } = DateTime.UtcNow;
        public List<OpResult> Ops { get; } = new List<OpResult>();
        /// <summary>Revit warnings/errors reported during commit.</summary>
        public List<string> RevitMessages { get; } = new List<string>();

        public int Applied => Ops.Count(o => o.Outcome == OpOutcome.Applied);
        public int Failed => Ops.Count(o => o.Outcome == OpOutcome.Failed);
        public int Skipped => Ops.Count(o => o.Outcome == OpOutcome.Skipped);
        public int RolledBack => Ops.Count(o => o.Outcome == OpOutcome.RolledBack);

        /// <summary>
        /// Called when the transaction did not commit: anything marked Applied becomes RolledBack.
        /// Guarantees the report never claims a change that is not in the model.
        /// </summary>
        public void MarkRolledBack(string reason)
        {
            Committed = false;
            foreach (var o in Ops)
            {
                if (o.Outcome == OpOutcome.Applied)
                {
                    o.Outcome = OpOutcome.RolledBack;
                    o.Message = string.IsNullOrEmpty(o.Message) ? reason : o.Message + " " + reason;
                }
            }
        }
    }

    /// <summary>Writes a local CSV report (UTF-8 with BOM, opens correctly in Excel).</summary>
    public static class ApplyReportWriter
    {
        public static string Write(string folder, string documentTitle, ChangeSet set, ApplyResult result, IUnitConverter units)
        {
            Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string safeTitle = new string((documentTitle ?? "model").Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());
            string path = Path.Combine(folder, "CausaFix-ExcelLink-" + safeTitle + "-" + stamp + ".csv");
            File.WriteAllText(path, Build(documentTitle, set, result, units), new UTF8Encoding(true));
            return path;
        }

        public static string Build(string documentTitle, ChangeSet set, ApplyResult result, IUnitConverter units)
        {
            var sb = new StringBuilder();
            void Row(params string[] cols) => sb.AppendLine(string.Join(",", cols.Select(Csv)));
            Row("CausaFix Excel Link – apply report");
            Row("Model", documentTitle);
            Row("Started (UTC)", result.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Row("Mode", result.Mode.ToString());
            Row("Transaction", result.Committed ? "Committed" : "Rolled back / not committed – no change below marked RolledBack or Failed is in the model");
            Row("Applied", result.Applied.ToString(CultureInfo.InvariantCulture), "Failed", result.Failed.ToString(CultureInfo.InvariantCulture),
                "Skipped", result.Skipped.ToString(CultureInfo.InvariantCulture), "Rolled back", result.RolledBack.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(result.Summary)) Row("Summary", result.Summary);
            sb.AppendLine();
            Row("Outcome", "Element", "UniqueId", "Parameter", "Old value", "New value", "Message");
            foreach (var o in result.Ops)
                Row(o.Outcome.ToString(), o.Label, o.TargetUniqueId, o.Parameter, o.OldValue, o.NewValue, o.Message);

            if (set != null)
            {
                sb.AppendLine();
                Row("Not written (review decisions)");
                Row("Status", "Element", "Parameter", "Original", "Current", "Proposed", "Reason", "Source");
                foreach (var e in set.Entries.Where(e => !e.WillWrite))
                    Row(e.Status.ToString() + (e.CanInclude && !e.Include ? " (excluded)" : ""), e.TargetLabel, e.Column?.Header,
                        ValueFormatter.Format(e.Original, e.Column, units, CultureInfo.InvariantCulture),
                        ValueFormatter.Format(e.Current, e.Column, units, CultureInfo.InvariantCulture),
                        e.IsClear ? "<<CLEAR>>" : ValueFormatter.Format(e.Proposed, e.Column, units, CultureInfo.InvariantCulture),
                        e.Reason, e.SourceSummary);
            }
            if (result.RevitMessages.Count > 0)
            {
                sb.AppendLine();
                Row("Revit messages");
                foreach (var m in result.RevitMessages) Row(m);
            }
            return sb.ToString();
        }

        private static string Csv(string s)
        {
            s = s ?? string.Empty;
            // Neutralise spreadsheet formula injection in a report opened by Excel.
            bool negativeNumber = s.Length > 1 && s[0] == '-' && (char.IsDigit(s[1]) || s[1] == '.');
            if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '@' || (s[0] == '-' && !negativeNumber))) s = "'" + s;
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
