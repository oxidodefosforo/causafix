using System.Collections.Generic;

namespace CausaFix.Core.Model
{
    /// <summary>Where elements come from.</summary>
    public enum ScopeMode
    {
        /// <summary>All elements of the selected categories in the host model.</summary>
        WholeModel = 0,
        /// <summary>Elements visible in the active graphical view (FilteredElementCollector by view).</summary>
        ActiveView,
        /// <summary>Elements currently selected in Revit.</summary>
        CurrentSelection
    }

    public enum CategoryKind
    {
        Model = 0,
        Annotation,
        Other
    }

    /// <summary>A category present in the current scope (Revit-independent DTO for the UI).</summary>
    public sealed class CategoryInfo
    {
        /// <summary>Category ElementId value (negative for built-in categories).</summary>
        public long Id { get; set; }
        public string Name { get; set; }
        public CategoryKind Kind { get; set; }
        /// <summary>Instances in scope.</summary>
        public int Count { get; set; }
        /// <summary>Types of this category in the model (for Type rows).</summary>
        public int TypeCount { get; set; }
        public bool IsSpatial { get; set; }
        public string Key => Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A parameter available in the selection (union of instance and type parameters).</summary>
    public sealed class ParameterInfo
    {
        public ParamKey Key { get; set; }
        public string Name { get; set; }
        public ParamSource Source => Key.Source;
        public ParamScope Scope => Key.Scope;
        public ValueKind Kind { get; set; }
        public string DataTypeLabel { get; set; } = string.Empty;
        public string SpecTypeId { get; set; } = string.Empty;
        public string UnitTypeId { get; set; } = string.Empty;
        public string UnitLabel { get; set; } = string.Empty;
        /// <summary>Read-only for every element it applies to.</summary>
        public bool ReadOnly { get; set; }
        /// <summary>Editable on some elements, read-only on others.</summary>
        public bool PartiallyReadOnly { get; set; }
        public string ReadOnlyReason { get; set; } = string.Empty;
        /// <summary>Number of elements (in scope) that have this parameter.</summary>
        public int AppliesTo { get; set; }
        /// <summary>Number of elements in scope.</summary>
        public int Total { get; set; }
        public bool IsCommon => Total > 0 && AppliesTo >= Total;
        /// <summary>"Built-in", "Shared", "Project", "Family".</summary>
        public string SourceLabel => Source == ParamSource.BuiltIn ? "Built-in" : Source == ParamSource.Shared ? "Shared" : Source == ParamSource.Project ? "Project" : Source == ParamSource.Family ? "Family" : "Context";

        public ColumnDef ToColumn() => new ColumnDef
        {
            Key = Key,
            Header = Name,
            Kind = Kind,
            SpecTypeId = SpecTypeId,
            UnitTypeId = UnitTypeId,
            UnitLabel = UnitLabel,
            ColumnReadOnly = ReadOnly || Kind == ValueKind.ElementRef,
            ReadOnlyReason = ReadOnlyReason,
            DataTypeLabel = DataTypeLabel
        };
    }

    /// <summary>A family type for the Elements tab.</summary>
    public sealed class TypeInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public long CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public int InstanceCount { get; set; }
        public string Display => CategoryName + " : " + FamilyName + " : " + TypeName;
    }

    public enum ScheduleKind
    {
        Regular = 0,
        KeySchedule,
        MaterialTakeoff,
        NoteBlock,
        RevisionSchedule,
        KeynoteLegend,
        SheetList,
        ViewList,
        Other
    }

    /// <summary>Support of one schedule for each export mode (the capability matrix shown to the user).</summary>
    public sealed class ScheduleInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Name { get; set; }
        public ScheduleKind Kind { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public bool Itemized { get; set; }
        public bool FilteredBySheet { get; set; }
        public bool HasEmbedded { get; set; }
        public bool IncludesLinks { get; set; }
        public int FieldCount { get; set; }
        public int HiddenFieldCount { get; set; }
        public int CalculatedFieldCount { get; set; }
        public bool OnActiveSheet { get; set; }
        public bool IsActiveView { get; set; }
        public bool DataExchangeSupported { get; set; }
        public string DataExchangeNote { get; set; } = string.Empty;
        public bool PresentationSupported { get; set; } = true;
        public List<ParameterInfo> Fields { get; } = new List<ParameterInfo>();
    }

    public enum ScheduleExportMode
    {
        /// <summary>One row per verifiable element, columns bound to real parameters. Re-importable.</summary>
        DataExchange = 0,
        /// <summary>The table as Revit shows it (headers, groups, totals). Read-only.</summary>
        Presentation
    }

    /// <summary>What to snapshot (built by the UI, executed by the Revit adapter).</summary>
    public sealed class SnapshotRequest
    {
        public SheetSource Source { get; set; }
        public ScopeMode Scope { get; set; }
        public List<long> CategoryIds { get; } = new List<long>();
        /// <summary>Elements tab: restrict to these type UniqueIds (empty = all types of the categories).</summary>
        public List<string> TypeUniqueIds { get; } = new List<string>();
        public RowMode RowMode { get; set; }
        /// <summary>Ordered columns (user order). Context columns are added by the adapter.</summary>
        public List<ParameterInfo> Parameters { get; } = new List<ParameterInfo>();
        public bool IncludeLinks { get; set; }
        /// <summary>Optional case-insensitive filter on family/type/mark text (Elements tab).</summary>
        public string TextFilter { get; set; } = string.Empty;
        public List<string> ScheduleUniqueIds { get; } = new List<string>();
        public ScheduleExportMode ScheduleMode { get; set; }
        /// <summary>One sheet per category (true) or one combined sheet.</summary>
        public bool SheetPerCategory { get; set; } = true;
    }
}
