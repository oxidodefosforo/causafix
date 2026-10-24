using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CausaFix.Core.Mvvm;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
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
            set { var v = (value ?? "").Trim(); if (Selection.Code == v) return; Selection.Code = v; OnPropertyChanged(); _changed(); }
        }

        public string Zone
        {
            get => Selection.Zone;
            set { var v = (value ?? "").Trim(); if (Selection.Zone == v) return; Selection.Zone = v; OnPropertyChanged(); _changed(); }
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

        /// <summary>Assigns only when the value really changes (combo boxes re-write equal values when their lists refresh).</summary>
        private bool Upd<T>(T current, T value, Action<T> assign, string name)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return false;
            assign(value);
            C(name);
            return true;
        }

        public bool Enabled { get => Rule.Enabled; set => Upd(Rule.Enabled, value, v => Rule.Enabled = v, nameof(Enabled)); }
        public string Name { get => Rule.Name; set => Upd(Rule.Name, value ?? "", v => Rule.Name = v, nameof(Name)); }
        public string Code { get => Rule.Code; set => Upd(Rule.Code, value ?? "", v => Rule.Code = v, nameof(Code)); }
        public string DisciplineCode { get => Rule.DisciplineCode; set => Upd(Rule.DisciplineCode, value ?? "", v => Rule.DisciplineCode = v, nameof(DisciplineCode)); }
        public PlanFamily Family
        {
            get => Rule.Family;
            set
            {
                if (Rule.Family == value) return;
                Rule.Family = value;
                Rule.ViewFamilyTypeUniqueId = "";
                Rule.TemplateUniqueId = "";
                C(nameof(Family));
                OnPropertyChanged(nameof(ViewFamilyTypeUniqueId));
                OnPropertyChanged(nameof(TemplateUniqueId));
            }
        }
        public string ViewFamilyTypeUniqueId { get => Rule.ViewFamilyTypeUniqueId; set => Upd(Rule.ViewFamilyTypeUniqueId, value ?? Rule.ViewFamilyTypeUniqueId, v => Rule.ViewFamilyTypeUniqueId = v, nameof(ViewFamilyTypeUniqueId)); }
        public string TemplateUniqueId { get => Rule.TemplateUniqueId; set => Upd(Rule.TemplateUniqueId, value ?? Rule.TemplateUniqueId, v => Rule.TemplateUniqueId = v, nameof(TemplateUniqueId)); }
        public string ViewNamePattern { get => Rule.ViewNamePattern; set => Upd(Rule.ViewNamePattern, value ?? "", v => Rule.ViewNamePattern = v, nameof(ViewNamePattern)); }
        public string TitleOnSheetPattern { get => Rule.TitleOnSheetPattern; set => Upd(Rule.TitleOnSheetPattern, value ?? "", v => Rule.TitleOnSheetPattern = v, nameof(TitleOnSheetPattern)); }
        public int Scale { get => Rule.Scale; set => Upd(Rule.Scale, Math.Max(1, value), v => Rule.Scale = v, nameof(Scale)); }
        public CropOption Crop { get => Rule.Crop; set => Upd(Rule.Crop, value, v => Rule.Crop = v, nameof(Crop)); }
        public string ScopeBoxUniqueId { get => Rule.ScopeBoxUniqueId; set => Upd(Rule.ScopeBoxUniqueId, value ?? Rule.ScopeBoxUniqueId, v => Rule.ScopeBoxUniqueId = v, nameof(ScopeBoxUniqueId)); }
        public SheetMode SheetMode { get => Rule.SheetMode; set => Upd(Rule.SheetMode, value, v => Rule.SheetMode = v, nameof(SheetMode)); }
        public string SheetNumberPattern { get => Rule.SheetNumberPattern; set => Upd(Rule.SheetNumberPattern, value ?? "", v => Rule.SheetNumberPattern = v, nameof(SheetNumberPattern)); }
        public string SheetNamePattern { get => Rule.SheetNamePattern; set => Upd(Rule.SheetNamePattern, value ?? "", v => Rule.SheetNamePattern = v, nameof(SheetNamePattern)); }
        public TextCase ViewNameCase { get => Rule.ViewNameCase; set => Upd(Rule.ViewNameCase, value, v => Rule.ViewNameCase = v, nameof(ViewNameCase)); }
        public TextCase SheetNameCase { get => Rule.SheetNameCase; set => Upd(Rule.SheetNameCase, value, v => Rule.SheetNameCase = v, nameof(SheetNameCase)); }
        public int SequenceStart { get => Rule.Sequence.Start; set => Upd(Rule.Sequence.Start, value, v => Rule.Sequence.Start = v, nameof(SequenceStart)); }
        public int SequenceIncrement { get => Rule.Sequence.Increment; set => Upd(Rule.Sequence.Increment, value == 0 ? 1 : value, v => Rule.Sequence.Increment = v, nameof(SequenceIncrement)); }
        public SequenceScope SequenceScope { get => Rule.Sequence.Scope; set => Upd(Rule.Sequence.Scope, value, v => Rule.Sequence.Scope = v, nameof(SequenceScope)); }
        public string TitleBlockUniqueId { get => Rule.TitleBlockUniqueId; set => Upd(Rule.TitleBlockUniqueId, value ?? Rule.TitleBlockUniqueId, v => Rule.TitleBlockUniqueId = v, nameof(TitleBlockUniqueId)); }
        public string AlignmentGroupKey { get => Rule.AlignmentGroupKey; set => Upd(Rule.AlignmentGroupKey, value ?? Rule.AlignmentGroupKey, v => Rule.AlignmentGroupKey = v, nameof(AlignmentGroupKey)); }
        public string SlotName { get => Rule.SlotName; set => Upd(Rule.SlotName, value ?? "A", v => Rule.SlotName = v, nameof(SlotName)); }
        public string ViewportTypeUniqueId { get => Rule.ViewportTypeUniqueId; set => Upd(Rule.ViewportTypeUniqueId, value ?? Rule.ViewportTypeUniqueId, v => Rule.ViewportTypeUniqueId = v, nameof(ViewportTypeUniqueId)); }
        public ViewportRotationOption Rotation { get => Rule.Rotation; set => Upd(Rule.Rotation, value, v => Rule.Rotation = v, nameof(Rotation)); }
        public string Zone { get => Rule.Zone; set => Upd(Rule.Zone, value ?? "", v => Rule.Zone = v, nameof(Zone)); }
        public string Condition { get => Rule.Condition; set => Upd(Rule.Condition, (value ?? "").Trim(), v => Rule.Condition = v, nameof(Condition)); }
        public string Purpose { get => Rule.Purpose; set => Upd(Rule.Purpose, (value ?? "").Trim(), v => Rule.Purpose = v, nameof(Purpose)); }
        public string Series { get => Rule.Series; set => Upd(Rule.Series, (value ?? "").Trim(), v => Rule.Series = v, nameof(Series)); }
        public string NumberingProfileKey { get => Rule.NumberingProfileKey; set => Upd(Rule.NumberingProfileKey, value ?? Rule.NumberingProfileKey, v => Rule.NumberingProfileKey = v, nameof(NumberingProfileKey)); }
        private string _composition = string.Empty;
        /// <summary>Name of the Views on Sheet composition this View Set belongs to (its sheet is decided there).</summary>
        public string Composition { get => _composition; set => Set(ref _composition, value ?? ""); }

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
        public string Name { get => Slot.Name; set { var v = (value ?? "").Trim(); if (Slot.Name == v) return; Slot.Name = v; C(nameof(Name)); } }
        public double Xmm { get => Slot.Xmm; set { if (Slot.Xmm == value) return; Slot.Xmm = value; Slot.ReferenceDescription = ""; C(nameof(Xmm)); OnPropertyChanged(nameof(ReferenceDescription)); } }
        public double Ymm { get => Slot.Ymm; set { if (Slot.Ymm == value) return; Slot.Ymm = value; Slot.ReferenceDescription = ""; C(nameof(Ymm)); OnPropertyChanged(nameof(ReferenceDescription)); } }
        public SheetFrame Frame { get => Slot.Frame; set { if (Slot.Frame == value) return; Slot.Frame = value; C(nameof(Frame)); } }
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
        public string Key { get => Group.Key; set { var v = (value ?? "").Trim(); if (v.Length == 0 || Group.Key == v) return; Group.Key = v; OnPropertyChanged(); _changed(); } }
        public AlignmentMethod Method { get => Group.Method; set { if (Group.Method == value) return; Group.Method = value; OnPropertyChanged(); OnPropertyChanged(nameof(MethodHelp)); _changed(); } }
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
        public string Slot => Row.PlaceAction == PlaceAction.None ? "" : Row.GroupKey + " / " + Row.SlotName + " · " + (Row.Method == AlignmentMethod.ModelAnchor ? "Anchor" : "Centre") + (string.IsNullOrEmpty(Row.OverlayGroupId) ? "" : " · overlay");
        /// <summary>Group header of the Review (rows grouped by sheet).</summary>
        public string SheetGroup => string.IsNullOrEmpty(Row.SheetKey) ? "(no sheet)" : "Sheet " + (string.IsNullOrEmpty(Row.SheetNumber) ? "?" : Row.SheetNumber) + "  " + Row.SheetName +
                                    (string.IsNullOrEmpty(Row.CompositionName) ? "" : "   [" + Row.CompositionName + " · " + Row.CompositionMode + "]") +
                                    (Row.RenumberPending ? "   renumber pending → " + Row.ProposedSheetNumber : "");
        public string Role => Row.Role == MemberRole.None ? "" : Row.Role + (Row.ShowsTitle ? "" : " · title hidden");
        public string Numbering => Row.IsSheetPrimary ? Row.NumberingProfileName + (Row.Sequence.HasValue ? " #" + Row.Sequence.Value : "") : "";
        public string TitleText => Row.TitleAction == TitleAction.Apply ? Row.Title?.Describe() ?? "" : Row.ShowsTitle ? "Keep existing" : "Hidden (viewport type)";
        /// <summary>Component breakdown of every name of the row (sources and values), for the inspector.</summary>
        public string NameDetails => string.Join("\n", Row.Names.Where(n => n.Value.Produced).Select(n => NameFields.TargetLabel(n.Key) + " = '" + n.Value.Text + "'" + (n.Value.FromFixedValue ? "  (edited / from the sheet)" : "") + "\n    " + n.Value.Breakdown));
        public string SheetNumberText => Row.RenumberPending ? Row.SheetNumber + " → " + Row.ProposedSheetNumber + " (pending)" : Row.SheetNumber;
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
                if (value == Row.Include) return;
                if (IsGenerated) _cfg.EnsureOverride(Row.RowKey).Include = value;
                else { var p = _cfg.Placements.FirstOrDefault(x => PlanBuilder.PlacementRowKey(x.ViewUniqueId) == Row.RowKey); if (p != null) p.Include = value; }
                _rebuild();
            }
        }

        public string ViewName { get => Row.ViewName; set { if (value != Row.ViewName) Edit(o => o.ViewName = value); } }
        public string TitleOnSheet { get => Row.TitleOnSheet; set { if (value != Row.TitleOnSheet) Edit(o => o.TitleOnSheet = value); } }
        /// <summary>Sheet number and name are edited on the sheet's primary row (members show the sheet's values).</summary>
        public bool CanEditSheet => Row.IsSheetPrimary;
        public string SheetNumber { get => Row.SheetNumber; set { if (value != Row.SheetNumber && CanEditSheet) Edit(o => o.SheetNumber = value); } }
        public string SheetName { get => Row.SheetName; set { if (value != Row.SheetName && CanEditSheet) Edit(o => o.SheetName = value); } }
        /// <summary>Numbering profile of this sheet only ("" = composition / View Set).</summary>
        public string NumberingOverride
        {
            get => _cfg.Override(Row.RowKey)?.NumberingProfileKey ?? "";
            set { var v = value ?? ""; if (!Row.IsSheetPrimary || v == NumberingOverride) return; Edit(o => o.NumberingProfileKey = v.Length == 0 ? null : v); }
        }
        public bool PlaceAsDependent { get => _cfg.Override(Row.RowKey)?.PlaceAsDependent ?? false; set { if (value != PlaceAsDependent) Edit(o => o.PlaceAsDependent = value); } }

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
            o.Title = null;
            _rebuild();
        }
    }

    /// <summary>Existing view + target sheet for Place / Re-align Existing.</summary>
    public sealed class PlacementItem : ObservableObject
    {
        private readonly Action _changed;
        public PlacementItem(ExistingPlacement p, Action changed) { Placement = p; _changed = changed; }
        public ExistingPlacement Placement { get; }
        public string ViewUniqueId { get => Placement.ViewUniqueId; set { if (value == null || Placement.ViewUniqueId == value) return; Placement.ViewUniqueId = value; OnPropertyChanged(); _changed(); } }
        public string SheetUniqueId { get => Placement.SheetUniqueId; set { if (value == null || Placement.SheetUniqueId == value) return; Placement.SheetUniqueId = value; OnPropertyChanged(); _changed(); } }
        public string GroupKey { get => Placement.GroupKey; set { if (value == null || Placement.GroupKey == value) return; Placement.GroupKey = value; OnPropertyChanged(); _changed(); } }
        public string SlotName { get => Placement.SlotName; set { var v = value ?? "A"; if (Placement.SlotName == v) return; Placement.SlotName = v; OnPropertyChanged(); _changed(); } }
        public string OverlayGroupId { get => Placement.OverlayGroupId; set { var v = (value ?? "").Trim(); if (Placement.OverlayGroupId == v) return; Placement.OverlayGroupId = v; OnPropertyChanged(); _changed(); } }
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
        /// <summary>Title outlines measured in Revit after a run (mm).</summary>
        public List<(string name, double[] box)> MeasuredLabels { get; } = new List<(string, double[])>();
        /// <summary>Existing viewports/titles read from the document before the run (mm) – real positions, not yet changed.</summary>
        public List<(string name, double[] box, double[] label)> ExistingViewports { get; } = new List<(string, double[], double[])>();
        /// <summary>Estimated title positions (dashed): fixed targets, or relative to an existing viewport box.</summary>
        public List<(string name, double[] box)> EstimatedLabels { get; } = new List<(string, double[])>();
        /// <summary>Overlay groups: anchor point shared by several members.</summary>
        public List<(string name, double x, double y, int members)> Overlays { get; } = new List<(string, double, double, int)>();
        public List<string> Notes { get; } = new List<string>();
    }
}
