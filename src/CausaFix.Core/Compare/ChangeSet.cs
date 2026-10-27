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
        /// <summary>"host" or the link key (diagnostics; grouping is per document + owner + parameter).</summary>
        public string DocKey { get; set; } = DocKeys.Host;
        /// <summary>How the value was stored in the source (Text, Number, Bool, Formula…) – for diagnostics.</summary>
        public string CellKind { get; set; } = string.Empty;
        /// <summary>True when the original value was reconstructed from legacy metadata (schema 1).</summary>
        public bool OriginalMigrated { get; set; }
        /// <summary>Value when exported (Excel) or loaded (grid).</summary>
        public RawValue Original { get; set; }
        public Proposal Proposal { get; set; }

        /// <summary>Copy of this cell aimed at another owner (scope correction of older workbooks).</summary>
        public ProposedCell WithTarget(string targetUniqueId, bool targetIsType, ColumnDef column)
        {
            var c = (ProposedCell)MemberwiseClone();
            c.TargetUniqueId = targetUniqueId;
            c.TargetIsType = targetIsType;
            c.Column = column;
            return c;
        }

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
        /// <summary>Why the parameter cannot be written (when Editable is false).</summary>
        public BlockCode LockCode { get; set; }
        /// <summary>Diagnostics read from the real Parameter (empty when not resolved).</summary>
        public string ParameterName { get; set; } = string.Empty;
        public string StorageType { get; set; } = string.Empty;
        public bool? HasValue { get; set; }
        public bool? IsReadOnly { get; set; }
        public bool? UserModifiable { get; set; }
        /// <summary>Human readable current value (e.g. reference name).</summary>
        public string Display { get; set; } = string.Empty;
        /// <summary>Element that really owns the resolved Parameter (Parameter.Element), and whether it is an element type.</summary>
        public string OwnerUniqueId { get; set; } = string.Empty;
        public string OwnerLabel { get; set; } = string.Empty;
        public bool? OwnerIsType { get; set; }
        /// <summary>The parameter was found through the requested element but belongs to another one (e.g. its type).</summary>
        public bool OwnerMismatch { get; set; }
        public bool? IsShared { get; set; }
        public string ParameterGuid { get; set; } = string.Empty;
        /// <summary>Project binding of the definition (InstanceBinding / TypeBinding / none: defined in the family).</summary>
        public string Binding { get; set; } = string.Empty;
        /// <summary>Relations checked through the API (global parameter association…), or what could not be checked.</summary>
        public string Evidence { get; set; } = string.Empty;

        public static CurrentState Missing() => new CurrentState { ElementExists = false, Value = RawValue.NotApplicable, LockCode = BlockCode.MissingElement };
    }

    /// <summary>Precise reason a change cannot be written. Distinguishes Revit restrictions from add-in limits and input errors.</summary>
    public enum BlockCode
    {
        None = 0,
        /// <summary>The cell content cannot be converted for this parameter.</summary>
        InvalidValue,
        /// <summary>Revit reports the parameter as read-only (formula, calculated, constrained).</summary>
        ReadOnlyParameter,
        /// <summary>The parameter does not exist on the element (N/A at export).</summary>
        NotApplicable,
        /// <summary>Linked model data.</summary>
        Linked,
        MissingElement,
        MissingParameter,
        /// <summary>Several parameters match the key on this element.</summary>
        Ambiguous,
        /// <summary>Element locked by worksharing, group membership or similar.</summary>
        Locked,
        /// <summary>A limitation of CausaFix (not a Revit restriction).</summary>
        Unsupported,
        /// <summary>Context columns and informative sheets.</summary>
        ReadOnlyColumn,
        /// <summary>One of the edited proposals for this destination is invalid: no write for the group.</summary>
        GroupError,
        /// <summary>Workbook metadata cannot be matched safely.</summary>
        MetadataMismatch,
        /// <summary>The workbook's scope does not match the real owner of the parameter (e.g. a type parameter exported as instance).</summary>
        WrongScope
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
        /// <summary>Precise cause for Blocked entries.</summary>
        public BlockCode Code { get; internal set; }
        public string Reason { get; internal set; }
        /// <summary>Text to show in the Proposed column (raw input for invalid values, a notice for conflicts).</summary>
        public string ProposedText { get; internal set; }
        /// <summary>Technical diagnostics (one line per source cell) for the detail panel and the local log.</summary>
        public string Diagnostic { get; internal set; } = string.Empty;
        /// <summary>Diagnostics of the real Revit parameter at comparison time.</summary>
        public CurrentState State { get; internal set; }
        public string Info { get; internal set; }
        /// <summary>Whether the change will be applied. Only meaningful for Change / Conflict.</summary>
        public bool Include { get; set; }
        public bool CurrentEditable { get; internal set; }
        public int AffectedInstances { get; internal set; }
        public List<ProposedCell> Sources { get; } = new List<ProposedCell>();
        /// <summary>For ProposalConflict: the distinct proposals (value, clear) to choose from.</summary>
        public List<Proposal> Alternatives { get; } = new List<Proposal>();
        /// <summary>
        /// Set when the workbook's scope was corrected to the parameter's real owner (an older workbook exported a type
        /// parameter as an instance column): the entry targets the type and is shown and approved as such.
        /// </summary>
        public string ScopeNote { get; internal set; } = string.Empty;

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
        /// <summary>Unique destinations (document + owner + parameter) that will be written.</summary>
        public int WriteTargets => Entries.Count(e => e.WillWrite);
        public int EmptyCellsIgnored { get; internal set; }

        /// <summary>
        /// Older workbooks may hold a type parameter as an instance column (schedules report shared type parameters
        /// as instance fields). True: such cells are compared and written on the real owner (the type), visibly, with
        /// the type rules (one write per type, conflicts between rows). False: they are blocked as WrongScope.
        /// </summary>
        public bool RetargetWrongScope { get; set; } = true;
        /// <summary>Revit version, add-in build and loaded DLL path of the comparison (diagnostics).</summary>
        public string Environment { get; set; } = string.Empty;
        /// <summary>Cells whose scope was corrected to the real owner (see <see cref="RetargetWrongScope"/>).</summary>
        public int ScopeCorrectedCells { get; internal set; }

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
