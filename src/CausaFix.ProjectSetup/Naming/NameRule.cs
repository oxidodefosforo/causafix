using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CausaFix.ProjectSetup.Model;

namespace CausaFix.ProjectSetup.Naming
{
    /// <summary>Element a Revit parameter is read from.</summary>
    public enum ParamScope
    {
        ProjectInformation = 0,
        Level,
        /// <summary>The planned/reused view of the row (instance parameters of an existing view only).</summary>
        View,
        /// <summary>The planned/reused sheet (instance parameters of an existing sheet only).</summary>
        Sheet,
        /// <summary>The title block type chosen for the sheet (known before the sheet exists).</summary>
        TitleBlockType,
        /// <summary>The single title block instance of an existing sheet.</summary>
        TitleBlockInstance
    }

    public enum ParamKind
    {
        BuiltIn = 0,
        /// <summary>Shared parameter: identity is its GUID.</summary>
        Shared,
        /// <summary>Project (non-shared) parameter: identity is its ParameterElement in the document.</summary>
        Project
    }

    /// <summary>
    /// Stable identity of a Revit parameter as a name source. The visible name is a label only: built-in parameters
    /// are identified by their BuiltInParameter id, shared parameters by GUID and project parameters by the
    /// UniqueId of their ParameterElement (valid only in the document they came from).
    /// </summary>
    [DataContract]
    public sealed class ParamRef
    {
        [DataMember(Order = 1)] public ParamScope Scope { get; set; }
        [DataMember(Order = 2)] public ParamKind Kind { get; set; }
        [DataMember(Order = 3)] public long BuiltInId { get; set; }
        [DataMember(Order = 4)] public string Guid { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string ElementUniqueId { get; set; } = string.Empty;
        /// <summary>Name when picked (label / search hint, never identity).</summary>
        [DataMember(Order = 6)] public string Name { get; set; } = string.Empty;
        /// <summary>Document the project parameter was picked in (project parameters do not travel between documents).</summary>
        [DataMember(Order = 7)] public string DocumentKey { get; set; } = string.Empty;

        public string Key => Scope + "|" + IdentityKey;
        public string IdentityKey => Kind == ParamKind.Shared ? "g:" + (Guid ?? "").ToLowerInvariant()
                                   : Kind == ParamKind.BuiltIn ? "b:" + BuiltInId
                                   : "p:" + ElementUniqueId;

        public ParamRef Clone() => (ParamRef)MemberwiseClone();
        public override string ToString() => ScopeLabel(Scope) + " · " + Name;

        public static string ScopeLabel(ParamScope s)
        {
            switch (s)
            {
                case ParamScope.ProjectInformation: return "Project Information";
                case ParamScope.TitleBlockType: return "Title Block (type)";
                case ParamScope.TitleBlockInstance: return "Title Block (instance)";
                default: return s.ToString();
            }
        }
    }

    /// <summary>CausaFix configuration fields and values computed by the plan (never Revit parameters).</summary>
    public enum NameField
    {
        // CausaFix configuration fields
        Discipline = 0,
        ViewSetName,
        ViewSetCode,
        LevelCode,
        Zone,
        Condition,
        /// <summary>Special prefix / purpose (FCC = Fire Coverage Check…).</summary>
        Purpose,
        /// <summary>Series / sheet-type code.</summary>
        Series,
        // Computed by the plan
        ProjectNumber,
        LevelName,
        LevelElevation,
        ViewName,
        TitleOnSheet,
        SheetNumber,
        SheetName,
        ViewTemplateName,
        ViewTypeName,
        Scale,
        Sequence,
        CompositionName
    }

    public enum ComponentSource
    {
        Text = 0,
        Field,
        Parameter
    }

    public enum ElevationUnit
    {
        Millimeters = 0,
        Meters,
        Feet
    }

    [DataContract]
    public sealed class ValueMapping
    {
        [DataMember(Order = 1)] public string From { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string To { get; set; } = string.Empty;
    }

    /// <summary>One piece of a name: fixed text, a CausaFix field or a Revit parameter, with its own format.</summary>
    [DataContract]
    public sealed class NameComponent
    {
        [DataMember(Order = 1)] public string Id { get; set; } = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        [DataMember(Order = 2)] public bool Enabled { get; set; } = true;
        /// <summary>Required: a missing value is an error. Optional: the component and its separator are omitted.</summary>
        [DataMember(Order = 3)] public bool Required { get; set; } = true;
        [DataMember(Order = 4)] public ComponentSource Source { get; set; }
        [DataMember(Order = 5)] public NameField Field { get; set; }
        [DataMember(Order = 6)] public ParamRef Param { get; set; }
        [DataMember(Order = 7)] public string Text { get; set; } = string.Empty;
        /// <summary>Written before this component only when something precedes it and this component has a value.</summary>
        [DataMember(Order = 8)] public string Separator { get; set; } = string.Empty;
        [DataMember(Order = 9)] public TextCase Case { get; set; } = TextCase.AsTyped;
        /// <summary>Zero padding for numeric values (0 = none). Text codes keep their own characters, leading zeros included.</summary>
        [DataMember(Order = 10)] public int PadDigits { get; set; }
        [DataMember(Order = 11)] public List<ValueMapping> Mappings { get; set; } = new List<ValueMapping>();
        /// <summary>Explicit value used when the source is empty or unknown. Null = no fallback.</summary>
        [DataMember(Order = 12)] public string Fallback { get; set; }
        [DataMember(Order = 13)] public ElevationUnit Unit { get; set; } = ElevationUnit.Millimeters;
        [DataMember(Order = 14)] public int Decimals { get; set; }

        public NameComponent Clone()
        {
            var c = (NameComponent)MemberwiseClone();
            c.Id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            c.Param = Param?.Clone();
            c.Mappings = (Mappings ?? new List<ValueMapping>()).Select(m => new ValueMapping { From = m.From, To = m.To }).ToList();
            return c;
        }

        public static NameComponent OfText(string text, string separator = "") => new NameComponent { Source = ComponentSource.Text, Text = text, Separator = separator };
        public static NameComponent OfField(NameField f, string separator = "", bool required = true) => new NameComponent { Source = ComponentSource.Field, Field = f, Separator = separator, Required = required };
        public static NameComponent OfParam(ParamRef p, string separator = "", bool required = true) => new NameComponent { Source = ComponentSource.Parameter, Param = p, Separator = separator, Required = required };

        public string Label => Source == ComponentSource.Text ? "\"" + Text + "\""
                             : Source == ComponentSource.Field ? NameFields.Label(Field)
                             : Param == null ? "(parameter)" : Param.ToString();
    }

    public enum NameRuleMode
    {
        /// <summary>Visual builder: ordered components.</summary>
        Builder = 0,
        /// <summary>Manual token pattern (the format of earlier versions).</summary>
        Advanced
    }

    /// <summary>How one name (View Name, Title on Sheet, Sheet Name or Sheet Number) is built.</summary>
    [DataContract]
    public sealed class NameRule
    {
        [DataMember(Order = 1)] public NameRuleMode Mode { get; set; } = NameRuleMode.Builder;
        [DataMember(Order = 2)] public List<NameComponent> Components { get; set; } = new List<NameComponent>();
        [DataMember(Order = 3)] public string Pattern { get; set; } = string.Empty;
        [DataMember(Order = 4)] public TextCase Case { get; set; } = TextCase.AsTyped;
        /// <summary>"Use same rule": this target takes the value of another target of the same row.</summary>
        [DataMember(Order = 5)] public NameContext? SameAs { get; set; }
        /// <summary>Set by the migration when an old pattern was kept as an Advanced rule (and why).</summary>
        [DataMember(Order = 6)] public string LegacyNote { get; set; } = string.Empty;
        /// <summary>Empty rule = the target is not written (allowed for Title on Sheet only).</summary>
        public bool IsEmpty => SameAs == null && (Mode == NameRuleMode.Advanced ? string.IsNullOrWhiteSpace(Pattern) : !Components.Any(c => c.Enabled));

        public NameRule Clone()
        {
            var r = (NameRule)MemberwiseClone();
            r.Components = (Components ?? new List<NameComponent>()).Select(c => { var k = c.Clone(); k.Id = c.Id; return k; }).ToList();
            return r;
        }

        public static NameRule Of(params NameComponent[] components) => new NameRule { Components = components.ToList() };

        [OnDeserialized]
        private void Fix(StreamingContext c)
        {
            Components = Components ?? new List<NameComponent>();
            foreach (var k in Components) k.Mappings = k.Mappings ?? new List<ValueMapping>();
            Pattern = Pattern ?? string.Empty;
            LegacyNote = LegacyNote ?? string.Empty;
        }
    }

    public static class NameFields
    {
        public static readonly NameField[] ConfigFields = { NameField.Discipline, NameField.ViewSetName, NameField.ViewSetCode, NameField.LevelCode, NameField.Zone, NameField.Condition, NameField.Purpose, NameField.Series };
        public static readonly NameField[] ComputedFields = { NameField.ProjectNumber, NameField.LevelName, NameField.LevelElevation, NameField.ViewName, NameField.TitleOnSheet, NameField.SheetNumber, NameField.SheetName, NameField.ViewTemplateName, NameField.ViewTypeName, NameField.Scale, NameField.Sequence, NameField.CompositionName };

        public static bool IsConfig(NameField f) => Array.IndexOf(ConfigFields, f) >= 0;

        /// <summary>The name target a computed field refers to (dependency), or null.</summary>
        public static NameContext? TargetOf(NameField f)
        {
            switch (f)
            {
                case NameField.ViewName: return NameContext.ViewName;
                case NameField.TitleOnSheet: return NameContext.TitleOnSheet;
                case NameField.SheetNumber: return NameContext.SheetNumber;
                case NameField.SheetName: return NameContext.SheetName;
                default: return null;
            }
        }

        public static string Label(NameField f)
        {
            switch (f)
            {
                case NameField.Discipline: return "Discipline code";
                case NameField.ViewSetName: return "View Set name";
                case NameField.ViewSetCode: return "View Set code";
                case NameField.LevelCode: return "Level code";
                case NameField.Zone: return "Zone";
                case NameField.Condition: return "Condition / status";
                case NameField.Purpose: return "Prefix / purpose";
                case NameField.Series: return "Series code";
                case NameField.ProjectNumber: return "Project number";
                case NameField.LevelName: return "Level name";
                case NameField.LevelElevation: return "Level elevation";
                case NameField.ViewName: return "View Name (result)";
                case NameField.TitleOnSheet: return "Title on Sheet (result)";
                case NameField.SheetNumber: return "Sheet Number (result)";
                case NameField.SheetName: return "Sheet Name (result)";
                case NameField.ViewTemplateName: return "View template name";
                case NameField.ViewTypeName: return "View type name";
                case NameField.Scale: return "Scale (1:n → n)";
                case NameField.Sequence: return "Sequence";
                case NameField.CompositionName: return "Sheet composition name";
                default: return f.ToString();
            }
        }

        public static string TargetLabel(NameContext t)
        {
            switch (t)
            {
                case NameContext.ViewName: return "View Name";
                case NameContext.TitleOnSheet: return "Title on Sheet";
                case NameContext.SheetNumber: return "Sheet Number";
                default: return "Sheet Name";
            }
        }
    }
}
