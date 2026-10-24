using System;
using System.Collections.Generic;

namespace CausaFix.Core.Model
{
    /// <summary>Column definition. All metadata needed to export, display and re-import a column.</summary>
    public sealed class ColumnDef
    {
        public ParamKey Key { get; set; }
        /// <summary>Visible header (parameter name). May be renamed by the user in Excel; the key governs mapping.</summary>
        public string Header { get; set; }
        public ValueKind Kind { get; set; }
        /// <summary>Revit spec ForgeTypeId string (e.g. autodesk.spec.aec:length-2.0.0). Empty if none.</summary>
        public string SpecTypeId { get; set; } = string.Empty;
        /// <summary>Unit ForgeTypeId used for the exported numbers of this column (Double columns).</summary>
        public string UnitTypeId { get; set; } = string.Empty;
        /// <summary>Short unit label for headers (e.g. "mm", "V", "VA").</summary>
        public string UnitLabel { get; set; } = string.Empty;
        /// <summary>True when the whole column cannot be imported (context, calculated, reference...).</summary>
        public bool ColumnReadOnly { get; set; }
        public string ReadOnlyReason { get; set; } = string.Empty;
        /// <summary>Data type caption shown in the UI (Text, Length, Electrical Potential...).</summary>
        public string DataTypeLabel { get; set; } = string.Empty;
        /// <summary>Reference columns: class name of the elements that may be assigned (e.g. "Level", "Material").</summary>
        public string RefClass { get; set; } = string.Empty;
        /// <summary>Reference columns: allowed values as "Name [Id]" (drop-down list in Excel).</summary>
        public System.Collections.Generic.List<string> RefCandidates { get; set; } = new System.Collections.Generic.List<string>();

        public ParamScope Scope => Key.Scope;
        public bool IsContext => Kind == ValueKind.Context || Key.Source == ParamSource.Pseudo;

        public ColumnDef Clone()
        {
            var c = (ColumnDef)MemberwiseClone();
            c.RefCandidates = new System.Collections.Generic.List<string>(RefCandidates ?? new System.Collections.Generic.List<string>());
            return c;
        }
    }

    /// <summary>One cell as read from Revit.</summary>
    public sealed class CellValue
    {
        /// <summary>Internal Revit value.</summary>
        public RawValue Raw { get; set; }
        /// <summary>
        /// Number written to Excel: Double columns in the column unit (full precision, not rounded);
        /// Integer columns as is. Null for other kinds.
        /// </summary>
        public double? ExportNumber { get; set; }
        /// <summary>Text shown in the grid/Excel for non-numeric kinds (names, Yes/No, formatted text).</summary>
        public string DisplayText { get; set; } = string.Empty;
        /// <summary>Editable for this specific element (resolved per cell).</summary>
        public bool Editable { get; set; }
        public string LockReason { get; set; } = string.Empty;

        public static CellValue NotApplicable() => new CellValue
        {
            Raw = RawValue.NotApplicable,
            DisplayText = "N/A",
            Editable = false,
            LockReason = "Parameter not present on this element."
        };
    }

    /// <summary>Kind of row target.</summary>
    public enum RowMode
    {
        /// <summary>One row per element instance.</summary>
        Instance = 0,
        /// <summary>One row per element type (instance columns are not available).</summary>
        Type = 1
    }

    public sealed class RowData
    {
        /// <summary>Stable row key: UniqueId for host rows, "L|linkInstanceUniqueId|UniqueId" for linked rows.</summary>
        public string RowKey { get; set; }
        /// <summary>UniqueId of the row target (instance or type, depending on row mode).</summary>
        public string UniqueId { get; set; }
        /// <summary>Visible reference only. 64-bit.</summary>
        public long ElementId { get; set; }
        /// <summary>UniqueId of the element type (instance rows) — target of type-scope columns. Empty if none.</summary>
        public string TypeUniqueId { get; set; } = string.Empty;
        /// <summary>"host" or the link instance UniqueId.</summary>
        public string DocKey { get; set; } = DocKeys.Host;
        public bool IsLinked { get; set; }
        /// <summary>Readable description (Category: Family : Type).</summary>
        public string Label { get; set; } = string.Empty;
        public Dictionary<ParamKey, CellValue> Cells { get; } = new Dictionary<ParamKey, CellValue>();

        public CellValue Get(ParamKey key) => Cells.TryGetValue(key, out CellValue c) ? c : null;
    }

    public static class DocKeys
    {
        public const string Host = "host";
        public static string ForLink(string linkInstanceUniqueId) => "link:" + linkInstanceUniqueId;
        public static string LinkedRowKey(string linkInstanceUniqueId, string uniqueId) => "L|" + linkInstanceUniqueId + "|" + uniqueId;
    }

    public enum SheetSource
    {
        ModelCategories = 0,
        AnnotationCategories,
        Elements,
        ScheduleData,
        SchedulePresentation,
        Spatial,
        ProjectInformation,
        FamilyInventory,
        ObjectStyles,
        LineStyles
    }

    /// <summary>One logical table (becomes one worksheet).</summary>
    public sealed class DataSheet
    {
        /// <summary>Stable technical key (e.g. "cat:-2001120:I", "sch:&lt;uid&gt;"). Governs mapping, not the worksheet name.</summary>
        public string SheetKey { get; set; }
        public string Title { get; set; }
        public SheetSource Source { get; set; }
        public RowMode RowMode { get; set; }
        /// <summary>Whole sheet is informative only (presentation export, inventories).</summary>
        public bool ReadOnly { get; set; }
        public List<ColumnDef> Columns { get; } = new List<ColumnDef>();
        public List<RowData> Rows { get; } = new List<RowData>();
        /// <summary>Notes about limitations shown in the Instructions sheet and UI.</summary>
        public List<string> Notes { get; } = new List<string>();
        /// <summary>For presentation exports: the table as text (rows x columns).</summary>
        public List<string[]> PresentationGrid { get; } = new List<string[]>();
        /// <summary>For presentation exports: number of header rows at the top of PresentationGrid.</summary>
        public int PresentationHeaderRows { get; set; }

        public ColumnDef FindColumn(ParamKey key)
        {
            foreach (var c in Columns) if (c.Key == key) return c;
            return null;
        }
    }

    /// <summary>A complete export: what goes into one workbook.</summary>
    public sealed class ExportPackage
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string AddinVersion { get; set; } = string.Empty;
        public string RevitVersion { get; set; } = string.Empty;
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
        public DocumentIdentity Document { get; set; } = new DocumentIdentity();
        /// <summary>Scope description (Whole Model, Active View: X, Current Selection).</summary>
        public string ScopeDescription { get; set; } = string.Empty;
        public List<DataSheet> Sheets { get; } = new List<DataSheet>();
    }
}
