using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CausaFix.Core.Mvvm;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
using CausaFix.ProjectSetup.Planning;

namespace CausaFix.ProjectSetup.ViewModels
{
    /// <summary>An entry of the Build Name field picker (searchable, grouped by origin).</summary>
    public sealed class FieldOption
    {
        public string Group { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public ComponentSource Source { get; set; }
        public NameField Field { get; set; }
        public ParamRef Param { get; set; }
        public string Search => (Group + " " + Label + " " + Detail).ToLowerInvariant();
        public override string ToString() => Label;
    }

    /// <summary>One line of the Build Name preview (a real level / View Set row of the current plan).</summary>
    public sealed class PreviewLine
    {
        public string Row { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Breakdown { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public bool HasError => Error.Length > 0;
    }

    /// <summary>A component of a name rule as edited in the Build Name control.</summary>
    public sealed class ComponentItem : ObservableObject
    {
        private readonly Action _changed;
        public ComponentItem(NameComponent c, string sourceDetail, Action changed) { Component = c; SourceDetail = sourceDetail; _changed = changed; }
        public NameComponent Component { get; }
        public string SourceDetail { get; }
        private void C(string n) { OnPropertyChanged(n); _changed(); }

        public string Label => Component.Label;
        public bool IsText => Component.Source == ComponentSource.Text;
        public bool IsElevation => Component.Source == ComponentSource.Field && Component.Field == NameField.LevelElevation;
        public bool Enabled { get => Component.Enabled; set { if (Component.Enabled == value) return; Component.Enabled = value; C(nameof(Enabled)); } }
        public bool Required { get => Component.Required; set { if (Component.Required == value) return; Component.Required = value; C(nameof(Required)); } }
        public string Separator { get => Component.Separator; set { var v = value ?? ""; if (Component.Separator == v) return; Component.Separator = v; C(nameof(Separator)); } }
        public string Text { get => Component.Text; set { var v = value ?? ""; if (Component.Text == v) return; Component.Text = v; C(nameof(Text)); OnPropertyChanged(nameof(Label)); } }
        public TextCase Case { get => Component.Case; set { if (Component.Case == value) return; Component.Case = value; C(nameof(Case)); } }
        public int PadDigits { get => Component.PadDigits; set { int v = Math.Max(0, Math.Min(12, value)); if (Component.PadDigits == v) return; Component.PadDigits = v; C(nameof(PadDigits)); } }
        /// <summary>Empty = no fallback (a missing required value stays an error).</summary>
        public string Fallback { get => Component.Fallback ?? ""; set { var v = string.IsNullOrEmpty(value) ? null : value; if (Component.Fallback == v) return; Component.Fallback = v; C(nameof(Fallback)); } }
        public ElevationUnit Unit { get => Component.Unit; set { if (Component.Unit == value) return; Component.Unit = value; C(nameof(Unit)); } }
        public int Decimals { get => Component.Decimals; set { int v = Math.Max(0, Math.Min(6, value)); if (Component.Decimals == v) return; Component.Decimals = v; C(nameof(Decimals)); } }

        /// <summary>Value mappings as "From=To; From=To" (e.g. "Proposed=P; Demolished=D; Existing=E").</summary>
        public string MappingsText
        {
            get => string.Join("; ", Component.Mappings.Select(m => m.From + "=" + m.To));
            set
            {
                var list = ParseMappings(value);
                if (string.Join("; ", list.Select(m => m.From + "=" + m.To)) == MappingsText) return;
                Component.Mappings = list;
                C(nameof(MappingsText));
            }
        }

        public static List<ValueMapping> ParseMappings(string text)
        {
            var list = new List<ValueMapping>();
            foreach (var part in (text ?? "").Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string from = part.Substring(0, eq).Trim(), to = part.Substring(eq + 1).Trim();
                if (from.Length > 0) list.Add(new ValueMapping { From = from, To = to });
            }
            return list;
        }
    }

    /// <summary>
    /// Build Name: edits one <see cref="NameRule"/> (View Name, Title on Sheet, Sheet Name or Sheet Number) without typing
    /// tokens. Components come from a searchable picker grouped by origin; the preview shows real rows of the plan.
    /// </summary>
    public sealed class NameRuleItem : ObservableObject
    {
        private readonly Func<IList<FieldOption>> _options;
        private readonly Action _changed;
        private string _filter = string.Empty;
        private FieldOption _selectedOption;
        private ComponentItem _selected;
        private IList<FieldOption> _pickerOptions = new List<FieldOption>();

        public NameRuleItem(NameRule rule, NameContext target, string title, Func<IList<FieldOption>> options, Action changed, IEnumerable<NameContext> sameAsChoices = null)
        {
            Rule = rule;
            Target = target;
            Title = title;
            _options = options;
            _changed = changed;
            SameAsChoices = new List<string> { "(own rule)" }.Concat((sameAsChoices ?? Enumerable.Empty<NameContext>()).Select(NameFields.TargetLabel)).ToList();
            _sameAsTargets = (sameAsChoices ?? Enumerable.Empty<NameContext>()).ToList();
            foreach (var c in rule.Components) Components.Add(Wrap(c));
            AddCommand = new RelayCommand(() => Add(SelectedOption), () => SelectedOption != null);
            AddTextCommand = new RelayCommand(() => AddComponent(NameComponent.OfText("-")));
            MoveUpCommand = new RelayCommand(o => Move(o as ComponentItem, -1));
            MoveDownCommand = new RelayCommand(o => Move(o as ComponentItem, 1));
            DuplicateCommand = new RelayCommand(o => Duplicate(o as ComponentItem));
            RemoveCommand = new RelayCommand(o => Remove(o as ComponentItem));
            ConvertToBuilderCommand = new RelayCommand(ConvertToBuilder, () => IsAdvanced);
            RefreshPicker();
        }

        private readonly List<NameContext> _sameAsTargets;
        public NameRule Rule { get; }
        public NameContext Target { get; }
        public string Title { get; }
        public ObservableCollection<ComponentItem> Components { get; } = new ObservableCollection<ComponentItem>();
        public ObservableCollection<PreviewLine> Preview { get; } = new ObservableCollection<PreviewLine>();
        public IEnumerable<TextCase> TextCases => (TextCase[])Enum.GetValues(typeof(TextCase));
        public IEnumerable<ElevationUnit> Units => (ElevationUnit[])Enum.GetValues(typeof(ElevationUnit));
        public IList<string> SameAsChoices { get; }
        public bool HasSameAs => SameAsChoices.Count > 1;

        public RelayCommand AddCommand { get; }
        public RelayCommand AddTextCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        public RelayCommand DuplicateCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand ConvertToBuilderCommand { get; }

        /// <summary>Called by the owner after a parameter component is added (values of View/Sheet parameters are read on demand).</summary>
        public Action<ParamRef> ParameterAdded { get; set; }

        public bool IsAdvanced
        {
            get => Rule.Mode == NameRuleMode.Advanced;
            set
            {
                var m = value ? NameRuleMode.Advanced : NameRuleMode.Builder;
                if (Rule.Mode == m) return;
                if (value && string.IsNullOrWhiteSpace(Rule.Pattern)) Rule.Pattern = ToPattern(Rule);
                Rule.Mode = m;
                OnPropertyChanged(); OnPropertyChanged(nameof(IsBuilder)); ConvertToBuilderCommand.Refresh();
                _changed();
            }
        }
        public bool IsBuilder => !IsAdvanced && !IsSameAs;
        public bool IsSameAs => Rule.SameAs.HasValue;
        public string Pattern { get => Rule.Pattern; set { var v = value ?? ""; if (Rule.Pattern == v) return; Rule.Pattern = v; OnPropertyChanged(); _changed(); } }
        public TextCase Case { get => Rule.Case; set { if (Rule.Case == value) return; Rule.Case = value; OnPropertyChanged(); _changed(); } }
        public string LegacyNote => Rule.LegacyNote;
        public bool HasLegacyNote => !string.IsNullOrEmpty(Rule.LegacyNote);
        public string TokenHelp => "Advanced pattern: " + string.Join(" ", NamePattern.Tokens.Select(t => "{" + t + "}")) + "  ·  {Sequence:000} pads  ·  :upper / :lower  ·  {{ }} literal braces. Revit parameters are available in the builder only.";

        public string SameAs
        {
            get => Rule.SameAs.HasValue ? NameFields.TargetLabel(Rule.SameAs.Value) : SameAsChoices[0];
            set
            {
                int i = SameAsChoices.IndexOf(value ?? "");
                NameContext? t = i <= 0 ? (NameContext?)null : _sameAsTargets[i - 1];
                if (Rule.SameAs == t) return;
                Rule.SameAs = t;
                OnPropertyChanged(); OnPropertyChanged(nameof(IsSameAs)); OnPropertyChanged(nameof(IsBuilder));
                _changed();
            }
        }

        public string Filter { get => _filter; set { if (Set(ref _filter, value ?? "")) RefreshPicker(); } }
        public IList<FieldOption> PickerOptions { get => _pickerOptions; private set => Set(ref _pickerOptions, value); }
        public FieldOption SelectedOption { get => _selectedOption; set { if (Set(ref _selectedOption, value)) AddCommand.Refresh(); } }
        public ComponentItem SelectedComponent { get => _selected; set => Set(ref _selected, value); }

        public void RefreshPicker()
        {
            string f = _filter.Trim().ToLowerInvariant();
            var all = _options?.Invoke() ?? new List<FieldOption>();
            PickerOptions = f.Length == 0 ? all.ToList() : all.Where(o => f.Split(' ').All(w => o.Search.Contains(w))).ToList();
        }

        private ComponentItem Wrap(NameComponent c)
        {
            string detail = c.Source == ComponentSource.Text ? "fixed text"
                          : c.Source == ComponentSource.Field ? (NameFields.IsConfig(c.Field) ? "CausaFix field" : "computed by the plan")
                          : "Revit parameter · " + (c.Param == null ? "" : ParamRef.ScopeLabel(c.Param.Scope) + " · " + c.Param.Kind.ToString().ToLowerInvariant());
            return new ComponentItem(c, detail, _changed);
        }

        private void Add(FieldOption o)
        {
            if (o == null) return;
            NameComponent c = o.Source == ComponentSource.Parameter ? NameComponent.OfParam(o.Param.Clone()) : NameComponent.OfField(o.Field);
            if (Rule.Components.Any(x => x.Enabled)) c.Separator = DefaultSeparator();
            if (c.Source == ComponentSource.Field && c.Field == NameField.Sequence) c.PadDigits = 2;
            AddComponent(c);
            if (c.Source == ComponentSource.Parameter) ParameterAdded?.Invoke(c.Param);
        }

        private string DefaultSeparator() => Target == NameContext.TitleOnSheet || Target == NameContext.SheetName ? " - " : "-";

        private void AddComponent(NameComponent c)
        {
            Rule.Components.Add(c);
            var item = Wrap(c);
            Components.Add(item);
            SelectedComponent = item;
            _changed();
        }

        private void Move(ComponentItem item, int delta)
        {
            if (item == null) return;
            int i = Rule.Components.IndexOf(item.Component), j = i + delta;
            if (i < 0 || j < 0 || j >= Rule.Components.Count) return;
            (Rule.Components[i], Rule.Components[j]) = (Rule.Components[j], Rule.Components[i]);
            Components.Move(i, j);
            SelectedComponent = item;
            _changed();
        }

        private void Duplicate(ComponentItem item)
        {
            if (item == null) return;
            var copy = item.Component.Clone();
            int i = Rule.Components.IndexOf(item.Component) + 1;
            Rule.Components.Insert(i, copy);
            Components.Insert(i, Wrap(copy));
            _changed();
        }

        private void Remove(ComponentItem item)
        {
            if (item == null) return;
            Rule.Components.Remove(item.Component);
            Components.Remove(item);
            _changed();
        }

        /// <summary>Advanced → builder, only when the pattern converts without changing its meaning.</summary>
        private void ConvertToBuilder()
        {
            var r = LegacyPatternConverter.Convert(Rule.Pattern, Rule.Case);
            if (r.Mode == NameRuleMode.Advanced) { Rule.LegacyNote = r.LegacyNote; OnPropertyChanged(nameof(LegacyNote)); OnPropertyChanged(nameof(HasLegacyNote)); return; }
            Rule.Components = r.Components;
            Rule.Mode = NameRuleMode.Builder;
            Rule.LegacyNote = string.Empty;
            Components.Clear();
            foreach (var c in Rule.Components) Components.Add(Wrap(c));
            foreach (var n in new[] { nameof(IsAdvanced), nameof(IsBuilder), nameof(LegacyNote), nameof(HasLegacyNote) }) OnPropertyChanged(n);
            ConvertToBuilderCommand.Refresh();
            _changed();
        }

        /// <summary>Best-effort pattern of a builder rule (fields only) used when switching to Advanced.</summary>
        public static string ToPattern(NameRule r)
        {
            string Tok(NameField f)
            {
                switch (f)
                {
                    case NameField.Discipline: return "DisciplineCode";
                    case NameField.ViewSetCode: return "ViewSetCode";
                    case NameField.ViewSetName: return "ViewSetName";
                    case NameField.LevelName: return "LevelName";
                    case NameField.LevelCode: return "LevelCode";
                    case NameField.Zone: return "Zone";
                    case NameField.Sequence: return "Sequence";
                    case NameField.ProjectNumber: return "ProjectNumber";
                    case NameField.Condition: return "Condition";
                    case NameField.Purpose: return "Purpose";
                    case NameField.Series: return "Series";
                    default: return null;
                }
            }
            var sb = new System.Text.StringBuilder();
            foreach (var c in r.Components.Where(c => c.Enabled))
            {
                if (sb.Length > 0) sb.Append((c.Separator ?? "").Replace("{", "{{").Replace("}", "}}"));
                if (c.Source == ComponentSource.Text) { sb.Append((c.Text ?? "").Replace("{", "{{").Replace("}", "}}")); continue; }
                string t = c.Source == ComponentSource.Field ? Tok(c.Field) : null;
                if (t == null) continue;
                string fmt = c.Field == NameField.Sequence && c.PadDigits > 0 ? ":" + new string('0', c.PadDigits) : c.Case == TextCase.Upper ? ":upper" : c.Case == TextCase.Lower ? ":lower" : "";
                sb.Append("{" + t + fmt + "}");
            }
            return sb.ToString();
        }

        public void SetPreview(IEnumerable<PreviewLine> lines)
        {
            Preview.Clear();
            foreach (var l in lines) Preview.Add(l);
        }
    }

    /// <summary>A numbering profile in the Numbering step.</summary>
    public sealed class NumberingProfileItem : ObservableObject
    {
        private readonly Action _changed;
        private string _usage = string.Empty;

        public NumberingProfileItem(NumberingProfile p, NameRuleItem builder, Action changed)
        {
            Profile = p;
            Builder = builder;
            _changed = changed;
            foreach (var f in new[] { NameField.Discipline, NameField.Series, NameField.ViewSetName, NameField.Zone, NameField.Condition, NameField.Purpose, NameField.LevelName })
            {
                var s = new Selectable<string>(NameFields.Label(f) + (f == NameField.ViewSetName ? " (View Set)" : f == NameField.LevelName ? " (level)" : ""));
                s.SetCheckedSilently(p.GroupBy.Contains(f));
                var field = f;
                s.CheckedChanged += (o, e) =>
                {
                    if (s.IsChecked && !p.GroupBy.Contains(field)) p.GroupBy.Add(field);
                    if (!s.IsChecked) p.GroupBy.Remove(field);
                    _changed();
                };
                GroupBy.Add(s);
            }
        }

        public NumberingProfile Profile { get; }
        public NameRuleItem Builder { get; }
        public ObservableCollection<Selectable<string>> GroupBy { get; } = new ObservableCollection<Selectable<string>>();
        private void C(string n) { OnPropertyChanged(n); _changed(); }
        public string Key => Profile.Key;
        public string Name { get => Profile.Name; set { var v = (value ?? "").Trim(); if (v.Length == 0 || Profile.Name == v) return; Profile.Name = v; C(nameof(Name)); } }
        public int Start { get => Profile.Start; set { if (Profile.Start == value) return; Profile.Start = value; C(nameof(Start)); } }
        public int Increment { get => Profile.Increment; set { int v = value == 0 ? 1 : value; if (Profile.Increment == v) return; Profile.Increment = v; C(nameof(Increment)); } }
        public SequenceOrder Order { get => Profile.Order; set { if (Profile.Order == value) return; Profile.Order = value; C(nameof(Order)); } }
        public string CounterKey { get => Profile.CounterKey; set { var v = (value ?? "").Trim(); if (Profile.CounterKey == v) return; Profile.CounterKey = v; C(nameof(CounterKey)); } }
        public bool IsDerived => Profile.DerivedFromLegacy;
        public string Usage { get => _usage; set => Set(ref _usage, value ?? ""); }
        public override string ToString() => Name;
    }

    /// <summary>Current → proposed number of one sheet (renumbering preview / approval).</summary>
    public sealed class RenumberItem : ObservableObject
    {
        private bool _include = true;
        public string SheetKey { get; set; }
        public string Sheet { get; set; } = string.Empty;
        public string Current { get; set; } = string.Empty;
        public string Proposed { get; set; } = string.Empty;
        public int? NewSequence { get; set; }
        public string Scope { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public bool Include { get => _include; set => Set(ref _include, value); }
    }

    /// <summary>A member of a Views on Sheet composition.</summary>
    public sealed class MemberItem : ObservableObject
    {
        private readonly CompositionItem _owner;
        public MemberItem(CompositionMember m, CompositionItem owner) { Member = m; _owner = owner; }
        public CompositionMember Member { get; }
        private void C(string n) { OnPropertyChanged(n); _owner.MemberChanged(); }
        public bool IsExistingView => Member.IsExistingView;
        public string Kind => Member.IsExistingView ? "Existing view" : "View Set";
        public bool Enabled { get => Member.Enabled; set { if (Member.Enabled == value) return; Member.Enabled = value; C(nameof(Enabled)); } }
        public string ViewSetKey { get => Member.ViewSetKey; set { if (value == null || Member.ViewSetKey == value) return; Member.ViewSetKey = value; C(nameof(ViewSetKey)); } }
        public string ExistingViewUniqueId { get => Member.ExistingViewUniqueId; set { if (value == null || Member.ExistingViewUniqueId == value) return; Member.ExistingViewUniqueId = value; C(nameof(ExistingViewUniqueId)); } }
        public bool IsPrimary
        {
            get => _owner.Composition.PrimaryMemberId == Member.Id || (_owner.Composition.Primary == Member && string.IsNullOrEmpty(_owner.Composition.PrimaryMemberId));
            set { if (!value || Member.IsExistingView) return; _owner.Composition.PrimaryMemberId = Member.Id; _owner.MemberChanged(); _owner.RefreshPrimary(); }
        }
        public string SlotName { get => Member.SlotName; set { var v = (value ?? "").Trim(); if (Member.SlotName == v) return; Member.SlotName = v; C(nameof(SlotName)); } }
        public bool ShowTitle { get => Member.ShowTitle; set { if (Member.ShowTitle == value) return; Member.ShowTitle = value; C(nameof(ShowTitle)); } }
        public string ViewportTypeUniqueId { get => Member.ViewportTypeUniqueId; set { if (value == null || Member.ViewportTypeUniqueId == value) return; Member.ViewportTypeUniqueId = value; C(nameof(ViewportTypeUniqueId)); } }
        public double TitleOffsetXmm { get => Member.TitleOffsetXmm; set { if (Member.TitleOffsetXmm == value) return; Member.TitleOffsetXmm = value; C(nameof(TitleOffsetXmm)); } }
        public double TitleOffsetYmm { get => Member.TitleOffsetYmm; set { if (Member.TitleOffsetYmm == value) return; Member.TitleOffsetYmm = value; C(nameof(TitleOffsetYmm)); } }
        public void RefreshPrimary() => OnPropertyChanged(nameof(IsPrimary));
    }

    /// <summary>A Views on Sheet composition in the Sheets step.</summary>
    public sealed class CompositionItem : ObservableObject
    {
        private readonly Action _changed;
        private NameRuleItem _sheetNameBuilder;
        private readonly Func<NameRule, NameRuleItem> _builderFactory;

        public CompositionItem(SheetComposition c, Action changed, Func<NameRule, NameRuleItem> builderFactory)
        {
            Composition = c;
            _changed = changed;
            _builderFactory = builderFactory;
            foreach (var m in c.Members) Members.Add(new MemberItem(m, this));
            if (c.SheetNameRule != null) _sheetNameBuilder = builderFactory(c.SheetNameRule);
        }

        public SheetComposition Composition { get; }
        public ObservableCollection<MemberItem> Members { get; } = new ObservableCollection<MemberItem>();
        private void C(string n) { OnPropertyChanged(n); _changed(); }
        internal void MemberChanged() => _changed();
        internal void RefreshPrimary() { foreach (var m in Members) m.RefreshPrimary(); }

        public string Name { get => Composition.Name; set { var v = (value ?? "").Trim(); if (v.Length == 0 || Composition.Name == v) return; Composition.Name = v; C(nameof(Name)); } }
        public bool Enabled { get => Composition.Enabled; set { if (Composition.Enabled == value) return; Composition.Enabled = value; C(nameof(Enabled)); } }
        public CompositionMode Mode { get => Composition.Mode; set { if (Composition.Mode == value) return; Composition.Mode = value; C(nameof(Mode)); OnPropertyChanged(nameof(IsOverlay)); OnPropertyChanged(nameof(ModeHelp)); } }
        public bool IsOverlay => Composition.Mode == CompositionMode.Overlay;
        public string ModeHelp => IsOverlay
            ? "Overlay: every member is aligned so the building anchor lands on the same paper point (Model Anchor alignment, same scale and orientation). Use it for a floor plan with its reflected ceiling plan superimposed."
            : "Separate placement: each member goes to its own slot of the sheet.";
        public TitleVisibility TitleVisibility { get => Composition.TitleVisibility; set { if (Composition.TitleVisibility == value) return; Composition.TitleVisibility = value; C(nameof(TitleVisibility)); OnPropertyChanged(nameof(IsPerMemberTitle)); } }
        public bool IsPerMemberTitle => Composition.TitleVisibility == TitleVisibility.PerMember;
        public string HiddenTitleViewportTypeUniqueId { get => Composition.HiddenTitleViewportTypeUniqueId; set { if (value == null || Composition.HiddenTitleViewportTypeUniqueId == value) return; Composition.HiddenTitleViewportTypeUniqueId = value; C(nameof(HiddenTitleViewportTypeUniqueId)); } }
        public string NumberingProfileKey { get => Composition.NumberingProfileKey; set { if (value == null || Composition.NumberingProfileKey == value) return; Composition.NumberingProfileKey = value; C(nameof(NumberingProfileKey)); } }
        public string TitleBlockUniqueId { get => Composition.TitleBlockUniqueId; set { if (value == null || Composition.TitleBlockUniqueId == value) return; Composition.TitleBlockUniqueId = value; C(nameof(TitleBlockUniqueId)); } }
        public string GroupKey { get => Composition.GroupKey; set { if (value == null || Composition.GroupKey == value) return; Composition.GroupKey = value; C(nameof(GroupKey)); } }
        public string SlotName { get => Composition.SlotName; set { var v = (value ?? "").Trim(); if (Composition.SlotName == v) return; Composition.SlotName = v; C(nameof(SlotName)); } }
        /// <summary>Empty = take the value of the primary View Set.</summary>
        public string Condition { get => Composition.Condition ?? ""; set { var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (Composition.Condition == v) return; Composition.Condition = v; C(nameof(Condition)); } }
        public string Purpose { get => Composition.Purpose ?? ""; set { var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (Composition.Purpose == v) return; Composition.Purpose = v; C(nameof(Purpose)); } }
        public string Series { get => Composition.Series ?? ""; set { var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (Composition.Series == v) return; Composition.Series = v; C(nameof(Series)); } }

        /// <summary>Own Sheet Name rule for the composition (otherwise the primary View Set's).</summary>
        public bool OwnSheetName
        {
            get => Composition.SheetNameRule != null;
            set
            {
                if (OwnSheetName == value) return;
                Composition.SheetNameRule = value ? NameRule.Of(NameComponent.OfField(NameField.LevelName), NameComponent.OfField(NameField.CompositionName, " - ")) : null;
                _sheetNameBuilder = value ? _builderFactory(Composition.SheetNameRule) : null;
                OnPropertyChanged(); OnPropertyChanged(nameof(SheetNameBuilder));
                _changed();
            }
        }
        public NameRuleItem SheetNameBuilder => _sheetNameBuilder;
        public override string ToString() => Name;
    }

    /// <summary>Edits one title placement (profile default, group, composition or a single viewport).</summary>
    public sealed class TitlePlacementItem : ObservableObject
    {
        private readonly Func<TitlePlacement> _get;
        private readonly Action<TitlePlacement> _set;
        private readonly bool _canInherit;
        private readonly Action _changed;

        public TitlePlacementItem(string scope, Func<TitlePlacement> get, Action<TitlePlacement> set, bool canInherit, Func<TitlePlacement> inheritedValue, Action changed)
        {
            Scope = scope; _get = get; _set = set; _canInherit = canInherit; _changed = changed; _inherited = inheritedValue;
        }

        private readonly Func<TitlePlacement> _inherited;
        public string Scope { get; }
        public override string ToString() => Scope;
        public bool CanInherit => _canInherit;
        private TitlePlacement T => _get() ?? _inherited?.Invoke() ?? new TitlePlacement();
        private bool Own => _get() != null;

        /// <summary>Group / composition / viewport: use its own placement instead of the inherited one.</summary>
        public bool Override
        {
            get => !_canInherit || Own;
            set
            {
                if (!_canInherit || Override == value) return;
                _set(value ? (_inherited?.Invoke() ?? new TitlePlacement()).Clone() : null);
                RaiseAll();
                _changed();
            }
        }
        public bool IsEditable => Override;

        private void E(Action<TitlePlacement> a, string n)
        {
            if (!Own) { if (_canInherit) return; _set(new TitlePlacement()); }
            a(_get());
            OnPropertyChanged(n); OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(IsFixed)); OnPropertyChanged(nameof(IsRelative)); OnPropertyChanged(nameof(NextCaptureKind));
            _changed();
        }

        public TitleMode Mode { get => T.Mode; set { if (T.Mode == value) return; E(t => t.Mode = value, nameof(Mode)); OnPropertyChanged(nameof(IsMatch)); } }
        public TitleAnchor Anchor { get => T.Anchor; set { if (T.Anchor == value) return; E(t => t.Anchor = value, nameof(Anchor)); } }
        public double OffsetXmm { get => T.OffsetXmm; set { if (T.OffsetXmm == value) return; E(t => t.OffsetXmm = value, nameof(OffsetXmm)); } }
        public double OffsetYmm { get => T.OffsetYmm; set { if (T.OffsetYmm == value) return; E(t => t.OffsetYmm = value, nameof(OffsetYmm)); } }
        public TitleRelativeTo RelativeTo { get => T.RelativeTo; set { if (T.RelativeTo == value) return; E(t => t.RelativeTo = value, nameof(RelativeTo)); } }
        public double FixedXmm { get => T.FixedXmm; set { if (T.FixedXmm == value) return; E(t => t.FixedXmm = value, nameof(FixedXmm)); } }
        public double FixedYmm { get => T.FixedYmm; set { if (T.FixedYmm == value) return; E(t => t.FixedYmm = value, nameof(FixedYmm)); } }
        public SheetFrame Frame { get => T.Frame; set { if (T.Frame == value) return; E(t => t.Frame = value, nameof(Frame)); } }
        /// <summary>
        /// "Capture as": shows the kind the placement uses (after a capture or an explicit choice) or, before any choice,
        /// the kind the next capture will use (FixedPosition). Choosing a value marks it as explicit.
        /// </summary>
        public TitleCaptureKind CaptureKind { get => T.CaptureKindForNewCapture; set { if (CaptureKind == value) return; E(t => { t.CaptureKind = value; t.CaptureKindExplicit = true; }, nameof(CaptureKind)); } }
        public TitleLineMode LineMode { get => T.LineMode; set { if (T.LineMode == value) return; E(t => t.LineMode = value, nameof(LineMode)); } }
        public double LineLengthMm { get => T.LineLengthMm; set { if (T.LineLengthMm == value) return; E(t => t.LineLengthMm = value, nameof(LineLengthMm)); } }
        public string ReferenceDescription => T.ReferenceDescription;
        public bool IsMatch => T.Mode == TitleMode.MatchReference;
        public bool IsFixed => T.IsFixed;
        public bool IsRelative => T.Applies && !T.IsFixed;
        public string Description => (Override ? "" : "Inherited: ") + T.Describe();

        /// <summary>Kind the next capture will use (explicit choice, earlier capture or mode; otherwise FixedPosition).</summary>
        public TitleCaptureKind NextCaptureKind => T.CaptureKindForNewCapture;

        /// <summary>Why <paramref name="pick"/> cannot be applied to this placement, or null when it can.</summary>
        public string CaptureProblem(Execution.TitlePick pick)
        {
            if (pick == null || !pick.Ok) return pick?.Message ?? "Nothing was picked.";
            if (T.CaptureKindForNewCapture == TitleCaptureKind.FixedPosition && T.Frame == SheetFrame.TitleBlock && !pick.TitleBlockXmm.HasValue)
                return "The position is measured in the title block frame, but sheet " + pick.SheetNumber + " has " + pick.TitleBlockCount +
                       " title blocks (exactly one is needed to know the frame). Pick a viewport on a sheet with one title block, or set the frame to 'Sheet'.";
            return null;
        }

        /// <summary>
        /// Applies a captured reference title (Match reference title). Only the title geometry is taken: anchor corner,
        /// line length and direction. The title text keeps coming from the Title on Sheet rule. Both results are stored
        /// in their own fields (offset from the viewport / point on the paper) and only the chosen kind is applied.
        /// </summary>
        public void ApplyCapture(Execution.TitlePick pick)
        {
            var kind = T.CaptureKindForNewCapture;
            E(t =>
            {
                t.Mode = TitleMode.MatchReference;
                t.CaptureKind = kind;
                t.ReferenceDescription = pick.Description;
                t.OffsetXmm = Math.Round(pick.RelativeXmm, 3); t.OffsetYmm = Math.Round(pick.RelativeYmm, 3);
                if (t.Frame == SheetFrame.TitleBlock)
                {
                    if (pick.TitleBlockXmm.HasValue) { t.FixedXmm = Math.Round(pick.TitleBlockXmm.Value, 3); t.FixedYmm = Math.Round(pick.TitleBlockYmm.Value, 3); }
                }
                else { t.FixedXmm = Math.Round(pick.SheetXmm, 3); t.FixedYmm = Math.Round(pick.SheetYmm, 3); }
                t.LineMode = TitleLineMode.Set;
                t.LineLengthMm = Math.Round(pick.LineLengthMm, 3);
            }, nameof(Mode));
            RaiseAll();
        }

        public void RaiseAll()
        {
            foreach (var n in new[] { nameof(Override), nameof(IsEditable), nameof(Mode), nameof(Anchor), nameof(OffsetXmm), nameof(OffsetYmm), nameof(RelativeTo), nameof(FixedXmm), nameof(FixedYmm),
                                      nameof(Frame), nameof(CaptureKind), nameof(LineMode), nameof(LineLengthMm), nameof(ReferenceDescription), nameof(IsMatch), nameof(IsFixed), nameof(IsRelative), nameof(Description), nameof(NextCaptureKind) })
                OnPropertyChanged(n);
        }
    }

    /// <summary>Values offered by combo boxes for a numbering profile choice ("" = inherit).</summary>
    public sealed class KeyName
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    internal static class Fmt
    {
        public static string Mm(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
