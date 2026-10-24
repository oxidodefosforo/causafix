using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.ProjectSetup.Model
{
    public enum SetupMode
    {
        /// <summary>Create/reuse views, configure sheets and place/align views.</summary>
        FullSetup = 0,
        /// <summary>Generate or update views only.</summary>
        ViewsOnly,
        /// <summary>Create/configure sheets only (no views).</summary>
        SheetsOnly,
        /// <summary>Place or re-align existing views on existing sheets.</summary>
        PlaceRealignExisting,
        /// <summary>Only apply the approved view-title placement to viewports that already exist (nothing created, renamed or moved).</summary>
        UpdateTitlesOnly
    }

    public enum SequenceScope
    {
        Global = 0,
        PerViewSet,
        PerLevel
    }

    public enum TextCase
    {
        AsTyped = 0,
        Upper,
        Lower
    }

    public enum SheetMode
    {
        /// <summary>One sheet per view (default).</summary>
        OnePerView = 0,
        /// <summary>Earlier versions: rows whose Sheet Number resolved to the same value shared one sheet. Migrated to Views on Sheet compositions.</summary>
        SharedBySheetNumber,
        /// <summary>Place on an existing sheet chosen per row.</summary>
        ExistingSheet,
        /// <summary>Do not create or use sheets for this rule.</summary>
        None
    }

    public enum CropOption
    {
        KeepExisting = 0,
        ScopeBox,
        NoCrop
    }

    public enum AlignmentMethod
    {
        /// <summary>A model reference point lands on a sheet anchor (default).</summary>
        ModelAnchor = 0,
        /// <summary>The viewport box centre is placed at coordinates (layout only, no building match guarantee).</summary>
        ViewportCenter
    }

    public enum SheetFrame
    {
        /// <summary>Coordinates in the sheet's own system (mm).</summary>
        Sheet = 0,
        /// <summary>Coordinates relative to the local system of the sheet's title block instance.</summary>
        TitleBlock
    }

    public enum ViewportRotationOption
    {
        None = 0,
        Clockwise,
        Counterclockwise
    }

    /// <summary>A level selected for the setup with its CausaFix-only short code.</summary>
    [DataContract]
    public sealed class LevelSelection
    {
        [DataMember(Order = 1)] public string LevelUniqueId { get; set; }
        /// <summary>Name at save time: search hint only, never identity.</summary>
        [DataMember(Order = 2)] public string LevelName { get; set; }
        [DataMember(Order = 3)] public string Code { get; set; } = string.Empty;
        [DataMember(Order = 4)] public bool Selected { get; set; }
        [DataMember(Order = 5)] public int Order { get; set; }
        [DataMember(Order = 6)] public string Zone { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class SequenceSettings
    {
        [DataMember(Order = 1)] public int Start { get; set; } = 1;
        [DataMember(Order = 2)] public int Increment { get; set; } = 1;
        [DataMember(Order = 3)] public SequenceScope Scope { get; set; } = SequenceScope.PerViewSet;
    }

    /// <summary>A View Set: a generation rule applied to selected levels.</summary>
    [DataContract]
    public sealed class ViewSetRule
    {
        /// <summary>Stable key of the rule (survives renames). Part of every row key.</summary>
        [DataMember(Order = 1)] public string Key { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        [DataMember(Order = 2)] public string Name { get; set; } = "Lighting";
        [DataMember(Order = 3)] public string Code { get; set; } = "LTG";
        [DataMember(Order = 4)] public string DisciplineCode { get; set; } = "E";
        [DataMember(Order = 5)] public bool Enabled { get; set; } = true;
        /// <summary>Levels this rule applies to (UniqueIds). Empty = all selected levels.</summary>
        [DataMember(Order = 6)] public List<string> LevelUniqueIds { get; set; } = new List<string>();
        [DataMember(Order = 7)] public PlanFamily Family { get; set; } = PlanFamily.FloorPlan;
        [DataMember(Order = 8)] public string ViewFamilyTypeUniqueId { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string TemplateUniqueId { get; set; } = string.Empty;
        // Format-1 patterns. Setting one (code, tests, old profiles) re-derives the matching builder rule.
        private string _viewNamePattern = "{DisciplineCode}-{LevelCode}-{ViewSetName}";
        private string _titlePattern = "{LevelName} - {ViewSetName}";
        private string _sheetNumberPattern = "{DisciplineCode}-{Sequence:000}";
        private string _sheetNamePattern = "{LevelName} - {ViewSetName}";
        private TextCase _viewNameCase, _sheetNameCase;
        [DataMember(Order = 10)] public string ViewNamePattern { get => _viewNamePattern; set { _viewNamePattern = value; ViewNameRule = null; } }
        [DataMember(Order = 11)] public string TitleOnSheetPattern { get => _titlePattern; set { _titlePattern = value; TitleRule = null; } }
        [DataMember(Order = 12)] public int Scale { get; set; } = 100;
        [DataMember(Order = 13)] public CropOption Crop { get; set; } = CropOption.KeepExisting;
        [DataMember(Order = 14)] public string ScopeBoxUniqueId { get; set; } = string.Empty;
        [DataMember(Order = 15)] public SheetMode SheetMode { get; set; } = SheetMode.OnePerView;
        [DataMember(Order = 16)] public string SheetNumberPattern { get => _sheetNumberPattern; set { _sheetNumberPattern = value; NumberingProfileKey = string.Empty; } }
        [DataMember(Order = 17)] public string SheetNamePattern { get => _sheetNamePattern; set { _sheetNamePattern = value; SheetNameRule = null; } }
        [DataMember(Order = 18)] public TextCase ViewNameCase { get => _viewNameCase; set { _viewNameCase = value; ViewNameRule = null; } }
        [DataMember(Order = 19)] public TextCase SheetNameCase { get => _sheetNameCase; set { _sheetNameCase = value; SheetNameRule = null; } }
        [DataMember(Order = 20)] public SequenceSettings Sequence { get; set; } = new SequenceSettings();
        [DataMember(Order = 21)] public string TitleBlockUniqueId { get; set; } = string.Empty;
        [DataMember(Order = 22)] public string AlignmentGroupKey { get; set; } = "Main";
        [DataMember(Order = 23)] public string SlotName { get; set; } = "A";
        [DataMember(Order = 24)] public string ViewportTypeUniqueId { get; set; } = string.Empty;
        [DataMember(Order = 25)] public ViewportRotationOption Rotation { get; set; } = ViewportRotationOption.None;
        [DataMember(Order = 26)] public string Zone { get; set; } = string.Empty;
        // ---- format 2 ----
        /// <summary>Visual name rules (format 2). The *Pattern properties above are the format-1 source kept for reference.</summary>
        [DataMember(Order = 27)] public NameRule ViewNameRule { get; set; }
        [DataMember(Order = 28)] public NameRule TitleRule { get; set; }
        [DataMember(Order = 29)] public NameRule SheetNameRule { get; set; }
        [DataMember(Order = 30)] public string NumberingProfileKey { get; set; } = string.Empty;
        [DataMember(Order = 31)] public string Condition { get; set; } = string.Empty;
        [DataMember(Order = 32)] public string Purpose { get; set; } = string.Empty;
        [DataMember(Order = 33)] public string Series { get; set; } = string.Empty;

        public ViewSetRule Clone()
        {
            var c = (ViewSetRule)MemberwiseClone();
            c.LevelUniqueIds = new List<string>(LevelUniqueIds);
            c.Sequence = new SequenceSettings { Start = Sequence.Start, Increment = Sequence.Increment, Scope = Sequence.Scope };
            c.ViewNameRule = ViewNameRule?.Clone();
            c.TitleRule = TitleRule?.Clone();
            c.SheetNameRule = SheetNameRule?.Clone();
            return c;
        }

        /// <summary>Format 1 → 2: the token patterns become equivalent builder rules (or Advanced rules when not convertible).</summary>
        public void EnsureRules()
        {
            if (ViewNameRule == null) ViewNameRule = LegacyPatternConverter.Convert(ViewNamePattern, ViewNameCase);
            if (TitleRule == null) TitleRule = LegacyPatternConverter.Convert(TitleOnSheetPattern, TextCase.AsTyped);
            if (SheetNameRule == null) SheetNameRule = LegacyPatternConverter.Convert(SheetNamePattern, SheetNameCase);
            Condition = Condition ?? string.Empty;
            Purpose = Purpose ?? string.Empty;
            Series = Series ?? string.Empty;
            NumberingProfileKey = NumberingProfileKey ?? string.Empty;
        }
    }

    /// <summary>Where on the paper a slot's anchor goes.</summary>
    [DataContract]
    public sealed class SlotTarget
    {
        [DataMember(Order = 1)] public string Name { get; set; } = "A";
        /// <summary>Target X/Y in mm, in the chosen frame.</summary>
        [DataMember(Order = 2)] public double Xmm { get; set; } = 420;
        [DataMember(Order = 3)] public double Ymm { get; set; } = 297;
        [DataMember(Order = 4)] public SheetFrame Frame { get; set; } = SheetFrame.Sheet;
        /// <summary>Set when the target was taken from a reference viewport (Match Reference Viewport).</summary>
        [DataMember(Order = 5)] public string ReferenceDescription { get; set; } = string.Empty;
    }

    /// <summary>A model reference point (internal coordinates, feet). Never copied between projects as valid.</summary>
    [DataContract]
    public sealed class ModelAnchor
    {
        [DataMember(Order = 1)] public bool IsSet { get; set; }
        [DataMember(Order = 2)] public double X { get; set; }
        [DataMember(Order = 3)] public double Y { get; set; }
        [DataMember(Order = 4)] public string Description { get; set; } = string.Empty;
        /// <summary>Document the anchor was picked in (creation GUID / project info UniqueId). Anchors from another document are invalid.</summary>
        [DataMember(Order = 5)] public string DocumentKey { get; set; } = string.Empty;
        /// <summary>Optional second reference to verify full overlay (scale/orientation).</summary>
        [DataMember(Order = 6)] public bool HasSecond { get; set; }
        [DataMember(Order = 7)] public double X2 { get; set; }
        [DataMember(Order = 8)] public double Y2 { get; set; }
    }

    [DataContract]
    public sealed class AlignmentGroup
    {
        [DataMember(Order = 1)] public string Key { get; set; } = "Main";
        [DataMember(Order = 2)] public AlignmentMethod Method { get; set; } = AlignmentMethod.ModelAnchor;
        [DataMember(Order = 3)] public ModelAnchor Anchor { get; set; } = new ModelAnchor();
        [DataMember(Order = 4)] public List<SlotTarget> Slots { get; set; } = new List<SlotTarget> { new SlotTarget() };
        /// <summary>Title placement of this group (null = the profile default).</summary>
        [DataMember(Order = 5)] public TitlePlacement Title { get; set; }

        public SlotTarget Slot(string name) => Slots.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Per-row edits that override the rule (kept by row key).</summary>
    [DataContract]
    public sealed class RowOverride
    {
        [DataMember(Order = 1)] public string RowKey { get; set; }
        [DataMember(Order = 2)] public bool? Include { get; set; }
        [DataMember(Order = 3)] public string ViewName { get; set; }
        [DataMember(Order = 4)] public string TitleOnSheet { get; set; }
        [DataMember(Order = 5)] public string SheetNumber { get; set; }
        [DataMember(Order = 6)] public string SheetName { get; set; }
        [DataMember(Order = 7)] public string TitleBlockUniqueId { get; set; }
        [DataMember(Order = 8)] public string TemplateUniqueId { get; set; }
        /// <summary>Reuse this existing view instead of creating one.</summary>
        [DataMember(Order = 9)] public string ExistingViewUniqueId { get; set; }
        /// <summary>Place on this existing sheet.</summary>
        [DataMember(Order = 10)] public string ExistingSheetUniqueId { get; set; }
        [DataMember(Order = 11)] public string SlotName { get; set; }
        /// <summary>Explicit choice when the view is already placed on another sheet.</summary>
        [DataMember(Order = 12)] public bool PlaceAsDependent { get; set; }
        /// <summary>Title placement of this viewport (null = composition / group / profile).</summary>
        [DataMember(Order = 13)] public TitlePlacement Title { get; set; }
        /// <summary>Numbering profile of the sheet of this row ("" or null = composition / View Set).</summary>
        [DataMember(Order = 14)] public string NumberingProfileKey { get; set; }
    }

    /// <summary>Place / Re-align Existing: an existing view and the existing sheet it goes on.</summary>
    [DataContract]
    public sealed class ExistingPlacement
    {
        [DataMember(Order = 1)] public string ViewUniqueId { get; set; }
        [DataMember(Order = 2)] public string SheetUniqueId { get; set; }
        [DataMember(Order = 3)] public string GroupKey { get; set; } = "Main";
        [DataMember(Order = 4)] public string SlotName { get; set; } = "A";
        [DataMember(Order = 5)] public bool Include { get; set; } = true;
        /// <summary>Placements on the same sheet and slot with the same id are an intentional overlay (re-aligned as one group).</summary>
        [DataMember(Order = 6)] public string OverlayGroupId { get; set; } = string.Empty;
    }

    /// <summary>Complete Project Setup configuration (also the profile content).</summary>
    [DataContract]
    public sealed class SetupConfig
    {
        /// <summary>1 = token patterns; 2 = name builder, numbering profiles, compositions, title placement.</summary>
        public const int CurrentFormat = 2;

        [DataMember(Order = 1)] public int Format { get; set; } = CurrentFormat;
        [DataMember(Order = 2)] public string Name { get; set; } = "Project Setup";
        [DataMember(Order = 3)] public SetupMode Mode { get; set; } = SetupMode.FullSetup;
        [DataMember(Order = 4)] public List<LevelSelection> Levels { get; set; } = new List<LevelSelection>();
        [DataMember(Order = 5)] public List<ViewSetRule> ViewSets { get; set; } = new List<ViewSetRule>();
        [DataMember(Order = 6)] public List<AlignmentGroup> Groups { get; set; } = new List<AlignmentGroup> { new AlignmentGroup() };
        [DataMember(Order = 7)] public double ToleranceMm { get; set; } = 0.1;
        [DataMember(Order = 8)] public List<RowOverride> Overrides { get; set; } = new List<RowOverride>();
        /// <summary>Sequence numbers already given to row keys: kept stable when rows are added.</summary>
        [DataMember(Order = 9)] public Dictionary<string, int> SequenceAssignments { get; set; } = new Dictionary<string, int>();
        [DataMember(Order = 10)] public string ProjectNumberOverride { get; set; } = string.Empty;
        /// <summary>Usable drawing area on the sheet, mm (preview / margin checks only).</summary>
        [DataMember(Order = 11)] public double UsableLeftMm { get; set; } = 20;
        [DataMember(Order = 12)] public double UsableBottomMm { get; set; } = 20;
        [DataMember(Order = 13)] public double UsableRightMm { get; set; } = 700;
        [DataMember(Order = 14)] public double UsableTopMm { get; set; } = 574;
        [DataMember(Order = 15)] public bool AllowRealignPinned { get; set; }
        [DataMember(Order = 16)] public bool AlignTitleLabel { get; set; }
        [DataMember(Order = 17)] public List<ExistingPlacement> Placements { get; set; } = new List<ExistingPlacement>();
        /// <summary>Document the project references (levels, templates, anchors) were taken from.</summary>
        [DataMember(Order = 18)] public string DocumentKey { get; set; } = string.Empty;
        // ---- format 2 ----
        [DataMember(Order = 19)] public List<NumberingProfile> NumberingProfiles { get; set; } = new List<NumberingProfile>();
        [DataMember(Order = 20)] public List<SheetComposition> Compositions { get; set; } = new List<SheetComposition>();
        /// <summary>Default view-title placement of the profile.</summary>
        [DataMember(Order = 21)] public TitlePlacement Title { get; set; } = new TitlePlacement();
        /// <summary>Renumbering approved by the user: sheet key → the proposed number that was approved.</summary>
        [DataMember(Order = 22)] public Dictionary<string, string> ApprovedSheetNumbers { get; set; } = new Dictionary<string, string>();
        /// <summary>Counter scope each stored sequence number belongs to (sheet key → scope), so deselected sheets keep their number reserved.</summary>
        [DataMember(Order = 23)] public Dictionary<string, string> SequenceScopes { get; set; } = new Dictionary<string, string>();
        /// <summary>Format the file had when it was read (not saved). Used to report migrations.</summary>
        public int LoadedFormat { get; private set; } = CurrentFormat;

        public AlignmentGroup Group(string key) => Groups.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));
        public NumberingProfile Numbering(string key) => string.IsNullOrEmpty(key) ? null : NumberingProfiles.FirstOrDefault(n => n.Key == key);
        public SheetComposition Composition(string key) => string.IsNullOrEmpty(key) ? null : Compositions.FirstOrDefault(c => c.Key == key);
        public ViewSetRule Rule(string key) => string.IsNullOrEmpty(key) ? null : ViewSets.FirstOrDefault(r => r.Key == key);
        /// <summary>The enabled composition a View Set belongs to (a View Set can be in one composition only).</summary>
        public SheetComposition CompositionOf(string ruleKey) => Compositions.FirstOrDefault(c => c.Enabled && c.Members.Any(m => m.Enabled && m.ViewSetKey == ruleKey));
        public RowOverride Override(string rowKey) => Overrides.FirstOrDefault(o => o.RowKey == rowKey);

        public RowOverride EnsureOverride(string rowKey)
        {
            var o = Override(rowKey);
            if (o == null) { o = new RowOverride { RowKey = rowKey }; Overrides.Add(o); }
            return o;
        }

        [OnDeserialized]
        private void Fix(StreamingContext c)
        {
            Levels = Levels ?? new List<LevelSelection>();
            ViewSets = ViewSets ?? new List<ViewSetRule>();
            Groups = Groups ?? new List<AlignmentGroup>();
            if (Groups.Count == 0) Groups.Add(new AlignmentGroup());
            Overrides = Overrides ?? new List<RowOverride>();
            SequenceAssignments = SequenceAssignments ?? new Dictionary<string, int>();
            Placements = Placements ?? new List<ExistingPlacement>();
            NumberingProfiles = NumberingProfiles ?? new List<NumberingProfile>();
            Compositions = Compositions ?? new List<SheetComposition>();
            Title = Title ?? new TitlePlacement();
            ApprovedSheetNumbers = ApprovedSheetNumbers ?? new Dictionary<string, string>();
            SequenceScopes = SequenceScopes ?? new Dictionary<string, string>();
            foreach (var r in ViewSets)
            {
                r.LevelUniqueIds = r.LevelUniqueIds ?? new List<string>();
                r.Sequence = r.Sequence ?? new SequenceSettings();
            }
            foreach (var g in Groups)
            {
                g.Anchor = g.Anchor ?? new ModelAnchor();
                g.Slots = g.Slots ?? new List<SlotTarget>();
            }
            foreach (var p in Placements) p.OverlayGroupId = p.OverlayGroupId ?? string.Empty;
            LoadedFormat = Format;
            SetupMigration.Upgrade(this);
        }
    }
}
