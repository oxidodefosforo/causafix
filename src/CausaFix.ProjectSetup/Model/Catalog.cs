using System.Collections.Generic;
using System.Linq;

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
        public string Display => FamilyName + " : " + TypeName + (WidthMm > 0 ? "  (" + WidthMm.ToString("0") + " × " + HeightMm.ToString("0") + " mm)" : "");
    }

    public sealed class NamedRef
    {
        public string UniqueId { get; set; }
        public long Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
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
        public bool StructuralPlansAvailable => ViewFamilyTypes.Any(v => v.Family == PlanFamily.StructuralPlan);

        public LevelInfo Level(string uid) => Levels.FirstOrDefault(l => l.UniqueId == uid);
        public TemplateInfo Template(string uid) => string.IsNullOrEmpty(uid) ? null : Templates.FirstOrDefault(t => t.UniqueId == uid);
        public ViewFamilyTypeInfo ViewType(string uid) => ViewFamilyTypes.FirstOrDefault(t => t.UniqueId == uid);
        public TitleBlockInfo TitleBlock(string uid) => string.IsNullOrEmpty(uid) ? null : TitleBlocks.FirstOrDefault(t => t.UniqueId == uid);
        public ExistingViewInfo View(string uid) => string.IsNullOrEmpty(uid) ? null : Views.FirstOrDefault(v => v.UniqueId == uid);
        public ExistingSheetInfo Sheet(string uid) => string.IsNullOrEmpty(uid) ? null : Sheets.FirstOrDefault(s => s.UniqueId == uid);
    }
}
