using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CausaFix.Core.Mvvm;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Planning;

namespace CausaFix.ProjectSetup.ViewModels
{
    /// <summary>A host level in the Levels step.</summary>
    public sealed class LevelItem : ObservableObject
    {
        private readonly Action _changed;

        public LevelItem(LevelSelection sel, LevelInfo info, string existingViews, Action changed)
        {
            Selection = sel;
            Info = info;
            ExistingViews = existingViews;
            _changed = changed;
        }

        public LevelSelection Selection { get; }
        public LevelInfo Info { get; }
        public string Name => Info?.Name ?? Selection.LevelName + " (missing)";
        public string Elevation => Info?.ElevationText ?? "";
        public string ExistingViews { get; }
        public bool IsMissing => Info == null;

        public bool Selected
        {
            get => Selection.Selected;
            set { if (Selection.Selected == value) return; Selection.Selected = value; OnPropertyChanged(); _changed(); }
        }

        /// <summary>CausaFix-only code (GF, L01...). Never written to the Level.</summary>
        public string Code
        {
            get => Selection.Code;
            set { Selection.Code = (value ?? "").Trim(); OnPropertyChanged(); _changed(); }
        }

        public string Zone
        {
            get => Selection.Zone;
            set { Selection.Zone = (value ?? "").Trim(); OnPropertyChanged(); _changed(); }
        }

        public int Order => Selection.Order;
        public void Refresh() { OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(Order)); }
    }

    /// <summary>One View Set rule (edited in the View Sets and Sheets steps).</summary>
    public sealed class ViewSetItem : ObservableObject
    {
        private readonly Action _changed;
        private string _example = string.Empty;

        public ViewSetItem(ViewSetRule rule, Action changed)
        {
            Rule = rule;
            _changed = changed;
        }

        public ViewSetRule Rule { get; }
        private void C(string name) { OnPropertyChanged(name); _changed(); }

        public bool Enabled { get => Rule.Enabled; set { Rule.Enabled = value; C(nameof(Enabled)); } }
        public string Name { get => Rule.Name; set { Rule.Name = value ?? ""; C(nameof(Name)); } }
        public string Code { get => Rule.Code; set { Rule.Code = value ?? ""; C(nameof(Code)); } }
        public string DisciplineCode { get => Rule.DisciplineCode; set { Rule.DisciplineCode = value ?? ""; C(nameof(DisciplineCode)); } }
        public PlanFamily Family { get => Rule.Family; set { Rule.Family = value; Rule.ViewFamilyTypeUniqueId = ""; Rule.TemplateUniqueId = ""; C(nameof(Family)); OnPropertyChanged(nameof(ViewFamilyTypeUniqueId)); OnPropertyChanged(nameof(TemplateUniqueId)); } }
        public string ViewFamilyTypeUniqueId { get => Rule.ViewFamilyTypeUniqueId; set { Rule.ViewFamilyTypeUniqueId = value ?? ""; C(nameof(ViewFamilyTypeUniqueId)); } }
        public string TemplateUniqueId { get => Rule.TemplateUniqueId; set { Rule.TemplateUniqueId = value ?? ""; C(nameof(TemplateUniqueId)); } }
        public string ViewNamePattern { get => Rule.ViewNamePattern; set { Rule.ViewNamePattern = value ?? ""; C(nameof(ViewNamePattern)); } }
        public string TitleOnSheetPattern { get => Rule.TitleOnSheetPattern; set { Rule.TitleOnSheetPattern = value ?? ""; C(nameof(TitleOnSheetPattern)); } }
        public int Scale { get => Rule.Scale; set { Rule.Scale = Math.Max(1, value); C(nameof(Scale)); } }
        public CropOption Crop { get => Rule.Crop; set { Rule.Crop = value; C(nameof(Crop)); } }
        public string ScopeBoxUniqueId { get => Rule.ScopeBoxUniqueId; set { Rule.ScopeBoxUniqueId = value ?? ""; C(nameof(ScopeBoxUniqueId)); } }
        public SheetMode SheetMode { get => Rule.SheetMode; set { Rule.SheetMode = value; C(nameof(SheetMode)); } }
        public string SheetNumberPattern { get => Rule.SheetNumberPattern; set { Rule.SheetNumberPattern = value ?? ""; C(nameof(SheetNumberPattern)); } }
        public string SheetNamePattern { get => Rule.SheetNamePattern; set { Rule.SheetNamePattern = value ?? ""; C(nameof(SheetNamePattern)); } }
        public TextCase ViewNameCase { get => Rule.ViewNameCase; set { Rule.ViewNameCase = value; C(nameof(ViewNameCase)); } }
        public TextCase SheetNameCase { get => Rule.SheetNameCase; set { Rule.SheetNameCase = value; C(nameof(SheetNameCase)); } }
        public int SequenceStart { get => Rule.Sequence.Start; set { Rule.Sequence.Start = value; C(nameof(SequenceStart)); } }
        public int SequenceIncrement { get => Rule.Sequence.Increment; set { Rule.Sequence.Increment = value == 0 ? 1 : value; C(nameof(SequenceIncrement)); } }
        public SequenceScope SequenceScope { get => Rule.Sequence.Scope; set { Rule.Sequence.Scope = value; C(nameof(SequenceScope)); } }
        public string TitleBlockUniqueId { get => Rule.TitleBlockUniqueId; set { Rule.TitleBlockUniqueId = value ?? ""; C(nameof(TitleBlockUniqueId)); } }
        public string AlignmentGroupKey { get => Rule.AlignmentGroupKey; set { Rule.AlignmentGroupKey = value ?? "Main"; C(nameof(AlignmentGroupKey)); } }
        public string SlotName { get => Rule.SlotName; set { Rule.SlotName = value ?? "A"; C(nameof(SlotName)); } }
        public string ViewportTypeUniqueId { get => Rule.ViewportTypeUniqueId; set { Rule.ViewportTypeUniqueId = value ?? ""; C(nameof(ViewportTypeUniqueId)); } }
        public ViewportRotationOption Rotation { get => Rule.Rotation; set { Rule.Rotation = value; C(nameof(Rotation)); } }
        public string Zone { get => Rule.Zone; set { Rule.Zone = value ?? ""; C(nameof(Zone)); } }

        /// <summary>Levels this rule is limited to (empty = all selected).</summary>
        public ObservableCollection<Selectable<LevelItem>> Levels { get; } = new ObservableCollection<Selectable<LevelItem>>();

        /// <summary>Name preview for the first level (refreshed by the main view model).</summary>
        public string Example { get => _example; set => Set(ref _example, value); }
    }

    /// <summary>A sheet slot of an alignment group.</summary>
    public sealed class SlotItem : ObservableObject
    {
        private readonly Action _changed;
        public SlotItem(SlotTarget slot, Action changed) { Slot = slot; _changed = changed; }
        public SlotTarget Slot { get; }
        private void C(string n) { OnPropertyChanged(n); _changed(); }
        public string Name { get => Slot.Name; set { Slot.Name = (value ?? "").Trim(); C(nameof(Name)); } }
        public double Xmm { get => Slot.Xmm; set { Slot.Xmm = value; Slot.ReferenceDescription = ""; C(nameof(Xmm)); OnPropertyChanged(nameof(ReferenceDescription)); } }
        public double Ymm { get => Slot.Ymm; set { Slot.Ymm = value; Slot.ReferenceDescription = ""; C(nameof(Ymm)); OnPropertyChanged(nameof(ReferenceDescription)); } }
        public SheetFrame Frame { get => Slot.Frame; set { Slot.Frame = value; C(nameof(Frame)); } }
        public string ReferenceDescription => Slot.ReferenceDescription;
        public void Refresh() { OnPropertyChanged(nameof(Xmm)); OnPropertyChanged(nameof(Ymm)); OnPropertyChanged(nameof(Frame)); OnPropertyChanged(nameof(ReferenceDescription)); }
    }

    public sealed class GroupItem : ObservableObject
    {
        private readonly Action _changed;
        public GroupItem(AlignmentGroup g, Action changed)
        {
            Group = g;
            _changed = changed;
            foreach (var s in g.Slots) Slots.Add(new SlotItem(s, changed));
        }
        public AlignmentGroup Group { get; }
        public ObservableCollection<SlotItem> Slots { get; } = new ObservableCollection<SlotItem>();
        public string Key { get => Group.Key; set { Group.Key = (value ?? "").Trim(); OnPropertyChanged(); _changed(); } }
        public AlignmentMethod Method { get => Group.Method; set { Group.Method = value; OnPropertyChanged(); OnPropertyChanged(nameof(MethodHelp)); _changed(); } }
        public string AnchorText => Group.Anchor.IsSet
            ? Group.Anchor.Description + "  (internal X " + Group.Anchor.X.ToString("0.###") + " ft, Y " + Group.Anchor.Y.ToString("0.###") + " ft)" + (Group.Anchor.HasSecond ? " + second reference" : "")
            : "No anchor – pick a grid intersection or a point.";
        public string MethodHelp => Method == AlignmentMethod.ModelAnchor
            ? "Model Anchor Alignment: the model reference lands on the slot point of every sheet, even with different crops/annotations."
            : "Viewport Center Placement: the viewport box centre goes to the slot point. Layout only – does not guarantee that the building matches between sheets.";
        public void RefreshAnchor() => OnPropertyChanged(nameof(AnchorText));
    }

    /// <summary>A Review row. Edits are stored as row overrides (kept by row key) and the plan is rebuilt.</summary>
    public sealed class PlanRowItem : ObservableObject
    {
        private readonly SetupConfig _cfg;
        private readonly Action _rebuild;

        public PlanRowItem(PlanRow row, SetupConfig cfg, Action rebuild)
        {
            Row = row;
            _cfg = cfg;
            _rebuild = rebuild;
        }

        public PlanRow Row { get; }
        public string Level => Row.LevelName + (string.IsNullOrEmpty(Row.LevelCode) ? "" : " (" + Row.LevelCode + ")");
        public string ViewSet => Row.ViewSetName;
        public string ViewType => Row.Family + (string.IsNullOrEmpty(Row.ViewFamilyTypeName) ? "" : " : " + Row.ViewFamilyTypeName);
        public string Template => Row.TemplateName;
        public string Scale => Row.EffectiveScale > 0 ? "1:" + Row.EffectiveScale + (string.IsNullOrEmpty(Row.ScaleNote) ? "" : " (" + Row.ScaleNote + ")") : "";
        public string TitleBlock => Row.TitleBlockName;
        public string Slot => Row.PlaceAction == PlaceAction.None ? "" : Row.GroupKey + " / " + Row.SlotName + " · " + (Row.Method == AlignmentMethod.ModelAnchor ? "Anchor" : "Centre");
        public string Action => Row.ActionText;
        public RowStatus Status => Row.Status;
        public string Reason => Row.Reason;
        public bool IsGenerated => !Row.RowKey.StartsWith("place|", StringComparison.Ordinal);
        public bool CanPlaceAsDependent => Row.Errors.Any(e => e.Contains("already placed on sheet")) || (_cfg.Override(Row.RowKey)?.PlaceAsDependent ?? false);

        public bool Include
        {
            get => Row.Include;
            set
            {
                if (IsGenerated) _cfg.EnsureOverride(Row.RowKey).Include = value;
                else { var p = _cfg.Placements.FirstOrDefault(x => PlanBuilder.PlacementRowKey(x.ViewUniqueId) == Row.RowKey); if (p != null) p.Include = value; }
                _rebuild();
            }
        }

        public string ViewName { get => Row.ViewName; set => Edit(o => o.ViewName = value); }
        public string TitleOnSheet { get => Row.TitleOnSheet; set => Edit(o => o.TitleOnSheet = value); }
        public string SheetNumber { get => Row.SheetNumber; set => Edit(o => o.SheetNumber = value); }
        public string SheetName { get => Row.SheetName; set => Edit(o => o.SheetName = value); }
        public bool PlaceAsDependent { get => _cfg.Override(Row.RowKey)?.PlaceAsDependent ?? false; set => Edit(o => o.PlaceAsDependent = value); }

        private void Edit(Action<RowOverride> a)
        {
            if (!IsGenerated) return;
            a(_cfg.EnsureOverride(Row.RowKey));
            _rebuild();
        }

        /// <summary>Clears manual edits of this row (back to the patterns).</summary>
        public void ResetEdits()
        {
            var o = _cfg.Override(Row.RowKey);
            if (o == null) return;
            o.ViewName = o.TitleOnSheet = o.SheetNumber = o.SheetName = null;
            _rebuild();
        }
    }

    /// <summary>Existing view + target sheet for Place / Re-align Existing.</summary>
    public sealed class PlacementItem : ObservableObject
    {
        private readonly Action _changed;
        public PlacementItem(ExistingPlacement p, Action changed) { Placement = p; _changed = changed; }
        public ExistingPlacement Placement { get; }
        public string ViewUniqueId { get => Placement.ViewUniqueId; set { Placement.ViewUniqueId = value; OnPropertyChanged(); _changed(); } }
        public string SheetUniqueId { get => Placement.SheetUniqueId; set { Placement.SheetUniqueId = value; OnPropertyChanged(); _changed(); } }
        public string GroupKey { get => Placement.GroupKey; set { Placement.GroupKey = value ?? "Main"; OnPropertyChanged(); _changed(); } }
        public string SlotName { get => Placement.SlotName; set { Placement.SlotName = value ?? "A"; OnPropertyChanged(); _changed(); } }
    }

    /// <summary>Data for the 2D sheet preview (drawn by the view).</summary>
    public sealed class SheetPreviewData
    {
        public string Title { get; set; } = string.Empty;
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public bool SizeKnown { get; set; }
        public double UsableLeft, UsableBottom, UsableRight, UsableTop;
        public List<(string name, double x, double y, string frame)> Anchors { get; } = new List<(string, double, double, string)>();
        /// <summary>Viewport boxes measured in Revit after a run (verified), mm.</summary>
        public List<(string name, double[] box)> MeasuredBoxes { get; } = new List<(string, double[])>();
        public List<string> Notes { get; } = new List<string>();
    }
}
