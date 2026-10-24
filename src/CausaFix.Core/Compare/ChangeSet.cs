using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.Core.Model;
using CausaFix.Core.Values;

namespace CausaFix.Core.Compare
{
    /// <summary>Where a proposal came from.</summary>
    public enum ProposalOrigin
    {
        Excel = 0,
        Grid
    }

    /// <summary>One user proposal for one cell, with the value it was based on (original).</summary>
    public sealed class ProposedCell
    {
        public ProposalOrigin Origin { get; set; }
        public string SheetKey { get; set; }
        public string SheetTitle { get; set; }
        /// <summary>Excel row number (1-based) for messages; 0 for grid edits.</summary>
        public int ExcelRow { get; set; }
        public string ExcelColumn { get; set; }
        public string RowKey { get; set; }
        public string RowLabel { get; set; }
        public long ElementId { get; set; }
        /// <summary>UniqueId of the element that holds the parameter (the instance, or its type for type-scope columns).</summary>
        public string TargetUniqueId { get; set; }
        public bool TargetIsType { get; set; }
        public bool IsLinked { get; set; }
        public ColumnDef Column { get; set; }
        /// <summary>Value when exported (Excel) or loaded (grid).</summary>
        public RawValue Original { get; set; }
        public Proposal Proposal { get; set; }

        public string Location => ExcelRow > 0 ? "'" + SheetTitle + "' row " + ExcelRow + (string.IsNullOrEmpty(ExcelColumn) ? "" : ", column " + ExcelColumn) : SheetTitle;
    }

    /// <summary>The model state of one target parameter at comparison time (read from Revit).</summary>
    public sealed class CurrentState
    {
        public bool ElementExists { get; set; }
        public bool ParameterExists { get; set; }
        public bool Ambiguous { get; set; }
        public RawValue Value { get; set; }
        public bool Editable { get; set; }
        public string LockReason { get; set; } = string.Empty;
        /// <summary>Type targets: number of instances in the whole document using this type.</summary>
        public int AffectedInstances { get; set; }
        public string TargetLabel { get; set; } = string.Empty;

        public static CurrentState Missing() => new CurrentState { ElementExists = false, Value = RawValue.NotApplicable };
    }

    public interface ICurrentStateSource
    {
        CurrentState Get(string targetUniqueId, ParamKey key);
    }

    public enum ChangeStatus
    {
        /// <summary>Excel/grid changed it; Revit still has the original value. Will be written.</summary>
        Change = 0,
        /// <summary>Revit changed the same value since export, differently. Not written unless the user decides.</summary>
        Conflict,
        /// <summary>Several rows/sheets propose different values for the same target (e.g. a type parameter).</summary>
        ProposalConflict,
        /// <summary>Revit already holds the proposed value. Nothing to write.</summary>
        AlreadyMatches,
        /// <summary>Cannot be written (read-only, invalid input, missing element/parameter, linked model...).</summary>
        Blocked
    }

    public sealed class ChangeEntry
    {
        public int Id { get; internal set; }
        public string TargetUniqueId { get; internal set; }
        public bool TargetIsType { get; internal set; }
        public string TargetLabel { get; internal set; }
        public long ElementId { get; internal set; }
        public ParamKey Key { get; internal set; }
        public ColumnDef Column { get; internal set; }
        public RawValue Original { get; internal set; }
        public RawValue Current { get; internal set; }
        public RawValue Proposed { get; internal set; }
        public bool IsClear { get; internal set; }
        public ChangeStatus Status { get; internal set; }
        public string Reason { get; internal set; }
        public string Info { get; internal set; }
        /// <summary>Whether the change will be applied. Only meaningful for Change / Conflict.</summary>
        public bool Include { get; set; }
        public bool CurrentEditable { get; internal set; }
        public int AffectedInstances { get; internal set; }
        public List<ProposedCell> Sources { get; } = new List<ProposedCell>();
        /// <summary>For ProposalConflict: the distinct proposals (value, clear) to choose from.</summary>
        public List<Proposal> Alternatives { get; } = new List<Proposal>();

        public bool CanInclude => (Status == ChangeStatus.Change || Status == ChangeStatus.Conflict) && CurrentEditable;
        public bool WillWrite => Include && CanInclude;
        public string SourceSummary => string.Join("; ", Sources.Select(s => s.Location).Distinct().Take(5)) + (Sources.Count > 5 ? " (+" + (Sources.Count - 5) + ")" : "");
    }

    public enum IssueSeverity
    {
        Info = 0,
        Warning,
        Error
    }

    public sealed class ImportIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public ImportIssue() { }
        public ImportIssue(IssueSeverity s, string location, string message) { Severity = s; Location = location ?? string.Empty; Message = message; }
        public override string ToString() => Severity + ": " + (Location.Length > 0 ? Location + " – " : "") + Message;
    }

    public sealed class ChangeSet
    {
        public List<ChangeEntry> Entries { get; } = new List<ChangeEntry>();
        public List<ImportIssue> Issues { get; } = new List<ImportIssue>();
        public int UnchangedCells { get; internal set; }
        public int EmptyCellsIgnored { get; internal set; }

        public int Count(ChangeStatus s) => Entries.Count(e => e.Status == s);
        public IEnumerable<ChangeEntry> ToWrite => Entries.Where(e => e.WillWrite);
    }

    /// <summary>A single, consolidated write that the Revit adapter executes.</summary>
    public sealed class WriteOp
    {
        public int EntryId { get; set; }
        public string TargetUniqueId { get; set; }
        public ParamKey Key { get; set; }
        public ColumnDef Column { get; set; }
        public bool Clear { get; set; }
        public RawValue Value { get; set; }
        /// <summary>Value seen during review. Re-checked just before writing to catch edits made in between.</summary>
        public RawValue ExpectedCurrent { get; set; }
        public string Label { get; set; }
    }
}
