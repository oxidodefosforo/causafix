using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.ProjectSetup.Model
{
    /// <summary>View families supported by the level-based generation of this version.</summary>
    public enum PlanFamily
    {
        FloorPlan = 0,
        CeilingPlan,
        StructuralPlan
    }

    public sealed class LevelInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Name { get; set; }
        /// <summary>Elevation in internal units (feet).</summary>
        public double Elevation { get; set; }
        public string ElevationText { get; set; } = string.Empty;
        /// <summary>Parameter values of the level (key = ParamRef.IdentityKey).</summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ViewFamilyTypeInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Name { get; set; }
        public PlanFamily Family { get; set; }
        public string Display => Family + " : " + Name;
    }

    public sealed class TemplateInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Name { get; set; }
        /// <summary>Plan family the template was made for (null when not a plan template).</summary>
        public PlanFamily? Family { get; set; }
        public bool ControlsScale { get; set; }
        public int Scale { get; set; }
        public bool ControlsDiscipline { get; set; }
        public string Discipline { get; set; } = string.Empty;
        public bool ControlsScopeBox { get; set; }
        public bool ControlsCrop { get; set; }
    }

    public sealed class TitleBlockInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        /// <summary>Sheet width/height from the type (mm). 0 when the family does not report it.</summary>
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Type parameter values (key = ParamRef.IdentityKey).</summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        public string Display => FamilyName + " : " + TypeName + (WidthMm > 0 ? "  (" + WidthMm.ToString("0") + " × " + HeightMm.ToString("0") + " mm)" : "");
    }

    public sealed class NamedRef
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Name { get; set; }
        /// <summary>Viewport types: value of "Show Title" as Revit displays it (empty when not readable).</summary>
        public string ShowTitleText { get; set; } = string.Empty;
        /// <summary>Viewport types: raw value of "Show Title" (null when not readable).</summary>
        public int? ShowTitleValue { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>A Revit parameter offered as a name source (definition only; values are on the elements).</summary>
    public sealed class ParamInfo
    {
        public ParamRef Ref { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsReadOnly { get; set; }
        public string StorageType { get; set; } = string.Empty;
        /// <summary>"Instance" or "Type".</summary>
        public bool IsType { get; set; }
        /// <summary>Label that disambiguates homonyms: kind, instance/type and read-only state.</summary>
        public string Detail => (Ref.Kind == ParamKind.Shared ? "shared" : Ref.Kind == ParamKind.Project ? "project" : "built-in") + " · " + (IsType ? "type" : "instance") + (IsReadOnly ? " · read-only (readable)" : "");
    }

    /// <summary>A viewport placed on a sheet (read from the document).</summary>
    public sealed class ExistingViewportInfo
    {
        public string UniqueId { get; set; }
        public string ViewUniqueId { get; set; }
        public string SheetUniqueId { get; set; }
        public string TypeUniqueId { get; set; } = string.Empty;
        public bool Pinned { get; set; }
        /// <summary>0 none, 1 clockwise, 2 counterclockwise.</summary>
        public int Rotation { get; set; }
        /// <summary>Box outline on the sheet (mm, minX minY maxX maxY), excluding the title.</summary>
        public double[] BoxMm { get; set; }
        /// <summary>Title (label) outline on the sheet (mm), null when the title is not shown.</summary>
        public double[] LabelMm { get; set; }
    }

    public sealed class ExistingViewInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Name { get; set; }
        public string TitleOnSheet { get; set; } = string.Empty;
        public PlanFamily? Family { get; set; }
        /// <summary>"FloorPlan", "Section"... (used for name uniqueness, which Revit applies per view type).</summary>
        public string ViewType { get; set; } = string.Empty;
        public string LevelUniqueId { get; set; } = string.Empty;
        public string TemplateUniqueId { get; set; } = string.Empty;
        public int Scale { get; set; }
        /// <summary>Sheet number where the view is placed, empty if not placed.</summary>
        public string PlacedOnSheet { get; set; } = string.Empty;
        public bool IsDependent { get; set; }
        /// <summary>CausaFix association (Extensible Storage) – row key it was generated for.</summary>
        public string SetupRowKey { get; set; } = string.Empty;
        /// <summary>Values of the parameters used by name rules (key = ParamRef.IdentityKey); only requested ones are read.</summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ExistingSheetInfo
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public bool IsPlaceholder { get; set; }
        public string TitleBlockTypeUniqueId { get; set; } = string.Empty;
        public string SetupRowKey { get; set; } = string.Empty;
        public List<string> PlacedViewUniqueIds { get; set; } = new List<string>();
        public int TitleBlockCount { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        /// <summary>Instance values of the sheet's title block (only when it has exactly one).</summary>
        public Dictionary<string, string> TitleBlockValues { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>Values of View / Sheet / Title Block instance parameters read on demand (element UniqueId → key → value).</summary>
    public sealed class ParamValueSet
    {
        public List<string> Keys { get; set; } = new List<string>();
        public Dictionary<string, Dictionary<string, string>> Views { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        public Dictionary<string, Dictionary<string, string>> Sheets { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        /// <summary>Sheet UniqueId → values of its single title block instance.</summary>
        public Dictionary<string, Dictionary<string, string>> TitleBlocks { get; set; } = new Dictionary<string, Dictionary<string, string>>();
    }

    /// <summary>Everything Project Setup needs to know about the document (read once, refreshed on demand).</summary>
    public sealed class SetupCatalog
    {
        public string DocumentTitle { get; set; } = string.Empty;
        public string ProjectNumber { get; set; } = string.Empty;
        public List<LevelInfo> Levels { get; set; } = new List<LevelInfo>();
        public List<ViewFamilyTypeInfo> ViewFamilyTypes { get; set; } = new List<ViewFamilyTypeInfo>();
        public List<TemplateInfo> Templates { get; set; } = new List<TemplateInfo>();
        public List<TitleBlockInfo> TitleBlocks { get; set; } = new List<TitleBlockInfo>();
        public List<NamedRef> ViewportTypes { get; set; } = new List<NamedRef>();
        public List<NamedRef> ScopeBoxes { get; set; } = new List<NamedRef>();
        public List<ExistingViewInfo> Views { get; set; } = new List<ExistingViewInfo>();
        public List<ExistingSheetInfo> Sheets { get; set; } = new List<ExistingSheetInfo>();
        public List<ExistingViewportInfo> Viewports { get; set; } = new List<ExistingViewportInfo>();
        /// <summary>Parameters available as name sources, per scope.</summary>
        public List<ParamInfo> Parameters { get; set; } = new List<ParamInfo>();
        public Dictionary<string, string> ProjectInfoValues { get; set; } = new Dictionary<string, string>();
        /// <summary>Scopes whose element values were read for these parameter keys (View/Sheet/TitleBlockInstance are read on demand).</summary>
        public HashSet<string> ValuesRead { get; set; } = new HashSet<string>();
        public bool StructuralPlansAvailable => ViewFamilyTypes.Any(v => v.Family == PlanFamily.StructuralPlan);

        public LevelInfo Level(string uid) => Levels.FirstOrDefault(l => l.UniqueId == uid);
        public TemplateInfo Template(string uid) => string.IsNullOrEmpty(uid) ? null : Templates.FirstOrDefault(t => t.UniqueId == uid);
        public ViewFamilyTypeInfo ViewType(string uid) => ViewFamilyTypes.FirstOrDefault(t => t.UniqueId == uid);
        public TitleBlockInfo TitleBlock(string uid) => string.IsNullOrEmpty(uid) ? null : TitleBlocks.FirstOrDefault(t => t.UniqueId == uid);
        public ExistingViewInfo View(string uid) => string.IsNullOrEmpty(uid) ? null : Views.FirstOrDefault(v => v.UniqueId == uid);
        public ExistingSheetInfo Sheet(string uid) => string.IsNullOrEmpty(uid) ? null : Sheets.FirstOrDefault(s => s.UniqueId == uid);
        public NamedRef ViewportType(string uid) => string.IsNullOrEmpty(uid) ? null : ViewportTypes.FirstOrDefault(t => t.UniqueId == uid);
        public ExistingViewportInfo ViewportOf(string viewUid, string sheetUid) => Viewports.FirstOrDefault(v => v.ViewUniqueId == viewUid && v.SheetUniqueId == sheetUid);
        public ParamInfo Param(ParamRef r) => r == null ? null : Parameters.FirstOrDefault(p => p.Ref.Key == r.Key);
        public static string ReadKey(ParamRef r) => r.Key;

        /// <summary>Merges values read on demand into the catalog.</summary>
        public void Merge(ParamValueSet set)
        {
            if (set == null) return;
            foreach (var k in set.Keys) ValuesRead.Add(k);
            foreach (var v in Views) if (set.Views.TryGetValue(v.UniqueId, out var d)) foreach (var kv in d) v.Values[kv.Key] = kv.Value;
            foreach (var s in Sheets)
            {
                if (set.Sheets.TryGetValue(s.UniqueId, out var d)) foreach (var kv in d) s.Values[kv.Key] = kv.Value;
                if (set.TitleBlocks.TryGetValue(s.UniqueId, out var t)) foreach (var kv in t) s.TitleBlockValues[kv.Key] = kv.Value;
            }
        }
    }
}
