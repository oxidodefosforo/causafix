using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.Core.Mvvm;
using CausaFix.Core.Settings;
using CausaFix.ProjectSetup.Execution;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;
using CausaFix.ProjectSetup.Planning;
using CausaFix.ProjectSetup.Profiles;

namespace CausaFix.ProjectSetup.ViewModels
{
    /// <summary>
    /// Project Setup window: Levels → View Sets → Sheets → Numbering → Placement → Review. Everything is a draft until Run
    /// Setup; editing a step never calls Revit except explicit actions (Refresh, Pick..., Run, Show) and the read-only
    /// value reading of a Revit parameter just added to a name rule.
    /// </summary>
    public sealed partial class ProjectSetupViewModel : ObservableObject
    {
        public const int NumberingStep = 3;
        public const int PlacementStep = 4;
        public const int ReviewStep = 5;

        private readonly IProjectSetupGateway _gw;
        private readonly ISetupDialogs _dialogs;
        private readonly SetupProfileStore _profiles;
        private readonly FileLog _log;
        private SetupConfig _cfg = new SetupConfig();
        private SetupCatalog _cat = new SetupCatalog();
        private SetupPlan _plan = new SetupPlan();
        private int _step;
        private bool _busy;
        private string _status = "Ready.";
        private string _busyText = string.Empty;
        private ViewSetItem _selectedSet;
        private GroupItem _selectedGroup;
        private PlanRowItem _selectedRow;
        private bool _errorsOnly;
        private SetupResult _result;
        private string _selectedProfile;
        private CancellationTokenSource _cts;
        private bool _suspend;

        public ProjectSetupViewModel(IProjectSetupGateway gateway, ISetupDialogs dialogs, string profilesFolder, string logsFolder)
        {
            _gw = gateway;
            _dialogs = dialogs;
            _profiles = new SetupProfileStore(profilesFolder);
            _log = new FileLog(logsFolder);

            Action<Exception> err = ReportError;
            RefreshCommand = new AsyncCommand(RefreshCatalogAsync, () => !IsBusy, err);
            SortByElevationCommand = new RelayCommand(SortByElevation);
            MoveLevelUpCommand = new RelayCommand(o => MoveLevel(o as LevelItem, -1));
            MoveLevelDownCommand = new RelayCommand(o => MoveLevel(o as LevelItem, 1));
            SelectAllLevelsCommand = new RelayCommand(() => SetAllLevels(true));
            SelectNoLevelsCommand = new RelayCommand(() => SetAllLevels(false));
            SuggestCodesCommand = new RelayCommand(SuggestCodes);
            AddSetCommand = new RelayCommand(AddSet);
            DuplicateSetCommand = new RelayCommand(DuplicateSet, () => SelectedSet != null);
            RemoveSetCommand = new RelayCommand(RemoveSet, () => SelectedSet != null);
            AddGroupCommand = new RelayCommand(AddGroup);
            RemoveGroupCommand = new RelayCommand(RemoveGroup, () => SelectedGroup != null && Groups.Count > 1);
            AddSlotCommand = new RelayCommand(AddSlot, () => SelectedGroup != null);
            RemoveSlotCommand = new RelayCommand(o => RemoveSlot(o as SlotItem));
            PickGridsCommand = new AsyncCommand(() => PickAnchor(grid: true, second: false), () => !IsBusy && SelectedGroup != null, err);
            PickPointCommand = new AsyncCommand(() => PickAnchor(grid: false, second: false), () => !IsBusy && SelectedGroup != null, err);
            PickSecondGridsCommand = new AsyncCommand(() => PickAnchor(grid: true, second: true), () => !IsBusy && SelectedGroup != null && SelectedGroup.Group.Anchor.IsSet, err);
            PickReferenceCommand = new AsyncCommand(o => PickReference(o as SlotItem), _ => !IsBusy && SelectedGroup != null, err);
            AddPlacementCommand = new RelayCommand(AddPlacement);
            RemovePlacementCommand = new RelayCommand(o => RemovePlacement(o as PlacementItem));
            RebuildCommand = new RelayCommand(Rebuild);
            RegenerateNumbersCommand = new RelayCommand(RegenerateNumbers);
            RunCommand = new AsyncCommand(RunAsync, () => !IsBusy && _plan.CanRun, err);
            CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy && _cts != null);
            ShowElementCommand = new AsyncCommand(o => ShowElement(o as string), _ => !IsBusy, err);
            ResetRowEditsCommand = new RelayCommand(o => (o as PlanRowItem)?.ResetEdits());
            NewProfileCommand = new RelayCommand(NewConfig);
            SaveProfileCommand = new RelayCommand(() => Guard(SaveProfile));
            LoadProfileCommand = new RelayCommand(() => Guard(LoadProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            DuplicateProfileCommand = new RelayCommand(() => Guard(DuplicateProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            RenameProfileCommand = new RelayCommand(() => Guard(RenameProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            DeleteProfileCommand = new RelayCommand(() => Guard(DeleteProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            ImportProfileCommand = new RelayCommand(() => Guard(ImportProfile));
            ExportProfileCommand = new RelayCommand(() => Guard(ExportProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            InitSheetFeatures(err);
            ReloadProfiles();
        }

        // ------------------------------------------------------------------ state

        public SetupConfig Config => _cfg;
        public SetupCatalog Catalog => _cat;
        public SetupPlan Plan => _plan;
        public SetupResult Result => _result;

        public ObservableCollection<LevelItem> Levels { get; } = new ObservableCollection<LevelItem>();
        public ObservableCollection<ViewSetItem> ViewSets { get; } = new ObservableCollection<ViewSetItem>();
        public ObservableCollection<GroupItem> Groups { get; } = new ObservableCollection<GroupItem>();
        public ObservableCollection<PlacementItem> Placements { get; } = new ObservableCollection<PlacementItem>();
        public ObservableCollection<PlanRowItem> Rows { get; } = new ObservableCollection<PlanRowItem>();
        public ObservableCollection<RowResult> Results { get; } = new ObservableCollection<RowResult>();
        public ObservableCollection<string> Profiles { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> PlanMessages { get; } = new ObservableCollection<string>();

        public IEnumerable<SetupMode> Modes => (SetupMode[])Enum.GetValues(typeof(SetupMode));
        public IEnumerable<PlanFamily> Families => _cat.StructuralPlansAvailable
            ? new[] { PlanFamily.FloorPlan, PlanFamily.CeilingPlan, PlanFamily.StructuralPlan }
            : new[] { PlanFamily.FloorPlan, PlanFamily.CeilingPlan };
        public IEnumerable<SheetMode> SheetModes => (SheetMode[])Enum.GetValues(typeof(SheetMode));
        public IEnumerable<SequenceScope> SequenceScopes => (SequenceScope[])Enum.GetValues(typeof(SequenceScope));
        public IEnumerable<TextCase> TextCases => (TextCase[])Enum.GetValues(typeof(TextCase));
        public IEnumerable<CropOption> CropOptions => (CropOption[])Enum.GetValues(typeof(CropOption));
        public IEnumerable<AlignmentMethod> AlignmentMethods => (AlignmentMethod[])Enum.GetValues(typeof(AlignmentMethod));
        public IEnumerable<SheetFrame> SheetFrames => (SheetFrame[])Enum.GetValues(typeof(SheetFrame));
        public IEnumerable<ViewportRotationOption> Rotations => (ViewportRotationOption[])Enum.GetValues(typeof(ViewportRotationOption));
        /// <summary>Sheet modes offered in the UI (the format-1 "shared by number" mode is migrated to compositions).</summary>
        public IEnumerable<SheetMode> SheetModesUi => new[] { SheetMode.OnePerView, SheetMode.ExistingSheet, SheetMode.None };
        public IList<string> GroupKeys { get; private set; } = new List<string>();
        public string TokenHelp => "Tokens: " + string.Join(" ", NamePattern.Tokens.Select(t => "{" + t + "}")) + "  ·  {Sequence:000} pads with zeros  ·  {LevelName:upper} / :lower  ·  {{ }} for literal braces";

        /// <summary>Catalog lists filtered for the selected View Set's family (for combo boxes).</summary>
        // Combo box sources are cached lists. They are rebuilt only when the catalog, the selected View Set or its
        // family changes: handing WPF a new collection on every edit makes each combo re-write its value, which
        // triggers another edit – an endless synchronous loop that overflows the stack and closes Revit.
        public IList<ViewFamilyTypeInfo> ViewTypesForSelected { get; private set; } = new List<ViewFamilyTypeInfo>();
        public IList<NamedRef> TemplatesForSelected { get; private set; } = new List<NamedRef>();
        public IList<TitleBlockInfo> TitleBlocks { get; private set; } = new List<TitleBlockInfo>();
        public IList<NamedRef> ViewportTypes { get; private set; } = new List<NamedRef>();
        public IList<NamedRef> ScopeBoxes { get; private set; } = new List<NamedRef>();
        public IList<ExistingViewInfo> ExistingPlanViews { get; private set; } = new List<ExistingViewInfo>();
        public IList<ExistingSheetInfo> ExistingSheets { get; private set; } = new List<ExistingSheetInfo>();
        private PlanFamily? _listsFamily;
        private bool _inChanged;

        private void RebuildCatalogLists()
        {
            TitleBlocks = _cat.TitleBlocks.ToList();
            ViewportTypes = new[] { new NamedRef { UniqueId = "", Name = "(default)" } }.Concat(_cat.ViewportTypes).ToList();
            ScopeBoxes = new[] { new NamedRef { UniqueId = "", Name = "(none)" } }.Concat(_cat.ScopeBoxes).ToList();
            ExistingPlanViews = _cat.Views.Where(v => v.Family.HasValue).OrderBy(v => v.Name).ToList();
            ExistingSheets = _cat.Sheets.Where(x => !x.IsPlaceholder).OrderBy(x => x.Number).ToList();
            foreach (var n in new[] { nameof(TitleBlocks), nameof(ViewportTypes), nameof(ScopeBoxes), nameof(ExistingPlanViews), nameof(ExistingSheets), nameof(Families) })
                OnPropertyChanged(n);
            RebuildSelectedLists(force: true);
        }

        private void RebuildSelectedLists(bool force = false)
        {
            var fam = SelectedSet?.Family;
            if (!force && fam == _listsFamily) return;
            _listsFamily = fam;
            ViewTypesForSelected = _cat.ViewFamilyTypes.Where(v => fam == null || v.Family == fam).ToList();
            TemplatesForSelected = new[] { new NamedRef { UniqueId = "", Name = "(none)" } }
                .Concat(_cat.Templates.Where(t => fam == null || !t.Family.HasValue || t.Family == fam)
                .Select(t => new NamedRef { UniqueId = t.UniqueId, Name = t.Name + (t.ControlsScale ? "  [scale 1:" + t.Scale + "]" : "") })).ToList();
            OnPropertyChanged(nameof(ViewTypesForSelected));
            OnPropertyChanged(nameof(TemplatesForSelected));
        }

        private void RebuildGroupKeys()
        {
            var keys = Groups.Select(g => g.Key).ToList();
            if (keys.SequenceEqual(GroupKeys)) return;
            GroupKeys = keys;
            OnPropertyChanged(nameof(GroupKeys));
        }

        public SetupMode Mode
        {
            get => _cfg.Mode;
            set { if (_cfg.Mode == value) return; _cfg.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPlaceExisting)); OnPropertyChanged(nameof(ShowPlacementsList)); Changed(); }
        }
        public bool IsPlaceExisting => _cfg.Mode == SetupMode.PlaceRealignExisting;
        /// <summary>The list of existing views/sheets is used by Place / Re-align Existing and by Update Titles Only.</summary>
        public bool ShowPlacementsList => _cfg.Mode == SetupMode.PlaceRealignExisting || _cfg.Mode == SetupMode.UpdateTitlesOnly;

        public double ToleranceMm { get => _cfg.ToleranceMm; set { _cfg.ToleranceMm = value <= 0 ? 0.1 : value; OnPropertyChanged(); } }
        public bool AllowRealignPinned { get => _cfg.AllowRealignPinned; set { _cfg.AllowRealignPinned = value; OnPropertyChanged(); } }
        public string ProjectNumber { get => string.IsNullOrEmpty(_cfg.ProjectNumberOverride) ? _cat.ProjectNumber : _cfg.ProjectNumberOverride; set { _cfg.ProjectNumberOverride = value ?? ""; OnPropertyChanged(); Changed(); } }
        public double UsableLeft { get => _cfg.UsableLeftMm; set { _cfg.UsableLeftMm = value; OnPropertyChanged(); RaisePreview(); } }
        public double UsableBottom { get => _cfg.UsableBottomMm; set { _cfg.UsableBottomMm = value; OnPropertyChanged(); RaisePreview(); } }
        public double UsableRight { get => _cfg.UsableRightMm; set { _cfg.UsableRightMm = value; OnPropertyChanged(); RaisePreview(); } }
        public double UsableTop { get => _cfg.UsableTopMm; set { _cfg.UsableTopMm = value; OnPropertyChanged(); RaisePreview(); } }

        public int Step
        {
            get => _step;
            set
            {
                if (!Set(ref _step, value)) return;
                if (value == ReviewStep) Rebuild();
                RefreshExamples();
                RefreshSetTitleInfo();
                if (value == NumberingStep) RefreshPendingRenumbers();
            }
        }

        public ViewSetItem SelectedSet
        {
            get => _selectedSet;
            set
            {
                if (!Set(ref _selectedSet, value)) return;
                RebuildSelectedLists();
                RebuildSetBuilders();
                DuplicateSetCommand.Refresh(); RemoveSetCommand.Refresh();
                RebuildSetTitleScopes();
            }
        }

        public GroupItem SelectedGroup
        {
            get => _selectedGroup;
            set { if (Set(ref _selectedGroup, value)) { RemoveGroupCommand.Refresh(); AddSlotCommand.Refresh(); PickGridsCommand.Refresh(); PickPointCommand.Refresh(); PickSecondGridsCommand.Refresh(); PickReferenceCommand.Refresh(); } }
        }

        public PlanRowItem SelectedRow { get => _selectedRow; set { if (Set(ref _selectedRow, value)) { RaisePreview(); RebuildRowTitle(); } } }
        public bool ErrorsOnly { get => _errorsOnly; set { if (Set(ref _errorsOnly, value)) FillRows(); } }
        public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { RunCommand.Refresh(); RefreshCommand.Refresh(); CancelCommand.Refresh(); PickGridsCommand.Refresh(); PickPointCommand.Refresh(); PickReferenceCommand.Refresh(); PickReferenceTitleCommand?.Refresh(); PickSetTitleReferenceCommand?.Refresh(); } } }
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
        public string Status { get => _status; set => Set(ref _status, value ?? ""); }
        public string DocumentTitle => _cat.DocumentTitle;
        public string PlanSummary => _plan.Summary;
        public string ResultSummary => _result == null ? "" : (_result.Committed ? "Committed. " : _result.Cancelled ? "Cancelled – nothing was kept. " : "NOT applied (rolled back). ") + _result.Summary + "  " + _result.Counts;
        public SheetPreviewData Preview => BuildPreview();
        public string SelectedProfile { get => _selectedProfile; set { if (Set(ref _selectedProfile, value)) { LoadProfileCommand.Refresh(); DuplicateProfileCommand.Refresh(); RenameProfileCommand.Refresh(); DeleteProfileCommand.Refresh(); ExportProfileCommand.Refresh(); } } }

        public AsyncCommand RefreshCommand { get; }
        public RelayCommand SortByElevationCommand { get; }
        public RelayCommand MoveLevelUpCommand { get; }
        public RelayCommand MoveLevelDownCommand { get; }
        public RelayCommand SelectAllLevelsCommand { get; }
        public RelayCommand SelectNoLevelsCommand { get; }
        public RelayCommand SuggestCodesCommand { get; }
        public RelayCommand AddSetCommand { get; }
        public RelayCommand DuplicateSetCommand { get; }
        public RelayCommand RemoveSetCommand { get; }
        public RelayCommand AddGroupCommand { get; }
        public RelayCommand RemoveGroupCommand { get; }
        public RelayCommand AddSlotCommand { get; }
        public RelayCommand RemoveSlotCommand { get; }
        public AsyncCommand PickGridsCommand { get; }
        public AsyncCommand PickPointCommand { get; }
        public AsyncCommand PickSecondGridsCommand { get; }
        public AsyncCommand PickReferenceCommand { get; }
        public RelayCommand AddPlacementCommand { get; }
        public RelayCommand RemovePlacementCommand { get; }
        public RelayCommand RebuildCommand { get; }
        public RelayCommand RegenerateNumbersCommand { get; }
        public AsyncCommand RunCommand { get; }
        public RelayCommand CancelCommand { get; }
        public AsyncCommand ShowElementCommand { get; }
        public RelayCommand ResetRowEditsCommand { get; }
        public RelayCommand NewProfileCommand { get; }
        public RelayCommand SaveProfileCommand { get; }
        public RelayCommand LoadProfileCommand { get; }
        public RelayCommand DuplicateProfileCommand { get; }
        public RelayCommand RenameProfileCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }
        public RelayCommand ImportProfileCommand { get; }
        public RelayCommand ExportProfileCommand { get; }

        // ------------------------------------------------------------------ lifecycle

        public async Task InitializeAsync()
        {
            _log.Info("Project Setup: open");
            await RunBusy("Connecting to the model", async ct => { await _gw.BindActiveDocument(); });
            _log.Info("Project Setup: bound to document");
            await RefreshCatalogAsync();
            if (_cfg.ViewSets.Count == 0) SeedDefaults();
            _log.Info("Project Setup: ready");
        }

        public async Task RefreshCatalogAsync()
        {
            SetupCatalog cat = null;
            await RunBusy("Reading levels, view types, templates and sheets", async ct => { cat = await _gw.ReadCatalog(PlanBuilder.ReferencedParams(_cfg)); });
            if (cat == null) return;
            _log.Info("Project Setup: catalog received (" + cat.Levels.Count + " levels, " + cat.Views.Count + " plan views, " + cat.Sheets.Count + " sheets, " + cat.Parameters.Count + " parameters)");
            _cat = cat;
            _fieldOptions = null;
            if (string.IsNullOrEmpty(_cfg.DocumentKey)) _cfg.DocumentKey = _gw.DocumentKey;
            SyncLevels();
            _log.Info("Project Setup: levels synced");
            LoadEditors();
            _log.Info("Project Setup: editors loaded");
            RebuildCatalogLists();
            _log.Info("Project Setup: lists bound");
            OnPropertyChanged(nameof(DocumentTitle));
            OnPropertyChanged(nameof(ProjectNumber));
            Status = _cat.Levels.Count + " level(s), " + _cat.Templates.Count + " template(s), " + _cat.TitleBlocks.Count + " title block type(s), " + _cat.Sheets.Count + " sheet(s) read from '" + _cat.DocumentTitle + "'.";
            Changed();
        }

        /// <summary>Two example View Sets so a first run is quick; every value stays editable.</summary>
        private void SeedDefaults()
        {
            var vft = _cat.ViewFamilyTypes.FirstOrDefault(v => v.Family == PlanFamily.FloorPlan);
            var tb = _cat.TitleBlocks.FirstOrDefault();
            _cfg.ViewSets.Add(NewRule("Lighting", "LTG", vft?.UniqueId, tb?.UniqueId, "1"));
            _cfg.ViewSets.Add(NewRule("Power", "PWR", vft?.UniqueId, tb?.UniqueId, "2"));
            if (tb != null && tb.WidthMm > 0)
            {
                _cfg.UsableRightMm = tb.WidthMm - 200;
                _cfg.UsableTopMm = tb.HeightMm - 20;
                _cfg.Groups[0].Slots[0].Xmm = Math.Round((tb.WidthMm - 200) / 2);
                _cfg.Groups[0].Slots[0].Ymm = Math.Round(tb.HeightMm / 2);
            }
            LoadEditors();
            Changed();
        }

        private void SyncLevels()
        {
            int order = _cfg.Levels.Count == 0 ? 0 : _cfg.Levels.Max(l => l.Order) + 1;
            foreach (var l in _cat.Levels.OrderBy(l => l.Elevation))
                if (!_cfg.Levels.Any(s => s.LevelUniqueId == l.UniqueId))
                    _cfg.Levels.Add(new LevelSelection { LevelUniqueId = l.UniqueId, LevelName = l.Name, Code = SuggestCode(l.Name, l.Elevation), Selected = false, Order = order++ });
            foreach (var s in _cfg.Levels) { var info = _cat.Level(s.LevelUniqueId); if (info != null) s.LevelName = info.Name; }
        }

        private void LoadEditors()
        {
            _suspend = true;
            Levels.Clear();
            foreach (var s in _cfg.Levels.OrderBy(l => l.Order))
            {
                var info = _cat.Level(s.LevelUniqueId);
                var views = _cat.Views.Where(v => v.LevelUniqueId == s.LevelUniqueId && v.Family.HasValue).Select(v => v.Name).ToList();
                Levels.Add(new LevelItem(s, info, views.Count == 0 ? "" : views.Count + ": " + string.Join(", ", views.Take(4)) + (views.Count > 4 ? "…" : ""), Changed));
            }
            var keepSet = _selectedSet?.Rule.Key;
            ViewSets.Clear();
            foreach (var r in _cfg.ViewSets) ViewSets.Add(NewSetItem(r));
            SelectedSet = ViewSets.FirstOrDefault(v => v.Rule.Key == keepSet) ?? ViewSets.FirstOrDefault();
            var keepGroup = _selectedGroup?.Group.Key;
            Groups.Clear();
            foreach (var g in _cfg.Groups) Groups.Add(new GroupItem(g, Changed));
            SelectedGroup = Groups.FirstOrDefault(g => g.Key == keepGroup) ?? Groups.FirstOrDefault();
            Placements.Clear();
            foreach (var p in _cfg.Placements) Placements.Add(new PlacementItem(p, Changed));
            LoadSheetEditors();
            OnPropertyChanged(nameof(Mode)); OnPropertyChanged(nameof(IsPlaceExisting)); OnPropertyChanged(nameof(ShowPlacementsList)); OnPropertyChanged(nameof(ToleranceMm)); RebuildGroupKeys();
            foreach (var n in new[] { nameof(UsableLeft), nameof(UsableBottom), nameof(UsableRight), nameof(UsableTop), nameof(AllowRealignPinned), nameof(ProjectNumber) }) OnPropertyChanged(n);
            _suspend = false;
        }

        private ViewSetItem NewSetItem(ViewSetRule r)
        {
            var item = new ViewSetItem(r, Changed);
            foreach (var lv in Levels)
            {
                var s = new Selectable<LevelItem>(lv);
                s.SetCheckedSilently(r.LevelUniqueIds.Contains(lv.Selection.LevelUniqueId));
                s.CheckedChanged += (o, e) =>
                {
                    if (s.IsChecked && !r.LevelUniqueIds.Contains(lv.Selection.LevelUniqueId)) r.LevelUniqueIds.Add(lv.Selection.LevelUniqueId);
                    if (!s.IsChecked) r.LevelUniqueIds.Remove(lv.Selection.LevelUniqueId);
                    Changed();
                };
                item.Levels.Add(s);
            }
            return item;
        }

        private void Changed()
        {
            if (_suspend || _inChanged) return;
            _inChanged = true;
            try
            {
                RebuildSelectedLists();   // only when the family changed
                RebuildGroupKeys();
                RebuildChoiceLists();     // only when keys/names changed
                RefreshExamples();
                RebuildSetTitleScopes();
                if (Step == ReviewStep) Rebuild();
            }
            finally { _inChanged = false; }
        }

        private void RefreshExamples()
        {
            if (ViewSets.Count == 0) return;
            var probe = SetupJson.Clone(_cfg);
            probe.Mode = SetupMode.FullSetup;
            var plan = PlanBuilder.Build(probe, _cat, _gw.DocumentKey);
            RefreshBuilderPreviews(plan);
            foreach (var vs in ViewSets)
            {
                var row = plan.Rows.FirstOrDefault(r => r.RuleKey == vs.Rule.Key);
                vs.Example = row == null ? (vs.Enabled ? "No selected level applies to this View Set." : "Disabled.")
                    : "e.g. " + row.LevelName + ":  view '" + row.ViewName + "'" + (string.IsNullOrEmpty(row.TitleOnSheet) ? "" : " · title '" + row.TitleOnSheet + "'") +
                      (string.IsNullOrEmpty(row.SheetNumber) ? "" : " · sheet " + row.SheetNumber + " '" + row.SheetName + "'") +
                      (row.Errors.Count > 0 ? "   ⚠ " + row.Errors[0] : "");
            }
        }

        // ------------------------------------------------------------------ levels

        public static string SuggestCode(string name, double elevation)
        {
            string n = (name ?? "").Trim().ToUpperInvariant();
            if (n.Contains("GROUND") || n == "GF" || n.Contains("PLANTA BAJA")) return "GF";
            if (n.Contains("ROOF") || n.Contains("TECHO") || n.Contains("AZOTEA")) return "RF";
            if (n.Contains("BASEMENT") || n.Contains("SOTANO") || n.Contains("SÓTANO")) return "B" + new string(n.Where(char.IsDigit).ToArray()).PadLeft(2, '0');
            var digits = new string(n.Where(char.IsDigit).ToArray());
            if (digits.Length > 0 && digits.Length <= 3) return "L" + digits.PadLeft(2, '0');
            return new string(n.Where(char.IsLetterOrDigit).Take(4).ToArray());
        }

        private void SuggestCodes()
        {
            foreach (var l in Levels.Where(l => l.Info != null && string.IsNullOrWhiteSpace(l.Code))) l.Code = SuggestCode(l.Info.Name, l.Info.Elevation);
        }

        private void SortByElevation()
        {
            int i = 0;
            foreach (var s in _cfg.Levels.OrderBy(l => _cat.Level(l.LevelUniqueId)?.Elevation ?? double.MaxValue)) s.Order = i++;
            LoadEditors();
            Changed();
        }

        private void MoveLevel(LevelItem item, int delta)
        {
            if (item == null) return;
            var ordered = _cfg.Levels.OrderBy(l => l.Order).ToList();
            int i = ordered.IndexOf(item.Selection), j = i + delta;
            if (i < 0 || j < 0 || j >= ordered.Count) return;
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
            for (int k = 0; k < ordered.Count; k++) ordered[k].Order = k;
            LoadEditors();
            Changed();
        }

        private void SetAllLevels(bool value)
        {
            _suspend = true;
            foreach (var l in Levels.Where(l => !l.IsMissing)) l.Selected = value;
            _suspend = false;
            Changed();
        }

        // ------------------------------------------------------------------ view sets / groups / placements

        private void AddSet()
        {
            var vft = _cat.ViewFamilyTypes.FirstOrDefault(v => v.Family == PlanFamily.FloorPlan);
            var r = NewRule("New set", "NEW", vft?.UniqueId, _cat.TitleBlocks.FirstOrDefault()?.UniqueId, (_cfg.ViewSets.Count + 1).ToString());
            _cfg.ViewSets.Add(r);
            var item = NewSetItem(r);
            ViewSets.Add(item);
            SelectedSet = item;
            Changed();
        }

        private void DuplicateSet()
        {
            var c = SelectedSet.Rule.Clone();
            c.Key = Guid.NewGuid().ToString("N").Substring(0, 12);
            c.Name += " (copy)";
            _cfg.ViewSets.Add(c);
            var item = NewSetItem(c);
            ViewSets.Add(item);
            SelectedSet = item;
            Changed();
        }

        private void RemoveSet()
        {
            if (!_dialogs.Confirm("Remove View Set", "Remove View Set '" + SelectedSet.Name + "' from the configuration? Views and sheets already created are not deleted.")) return;
            _cfg.ViewSets.Remove(SelectedSet.Rule);
            foreach (var c in _cfg.Compositions) c.Members.RemoveAll(m => m.ViewSetKey == SelectedSet.Rule.Key);
            ViewSets.Remove(SelectedSet);
            SelectedSet = ViewSets.FirstOrDefault();
            Changed();
        }

        private void AddGroup()
        {
            string key = "Group " + (Groups.Count + 1);
            var g = new AlignmentGroup { Key = key };
            _cfg.Groups.Add(g);
            var item = new GroupItem(g, Changed);
            Groups.Add(item);
            SelectedGroup = item;
            RebuildGroupKeys();
            Changed();
        }

        private void RemoveGroup()
        {
            _cfg.Groups.Remove(SelectedGroup.Group);
            Groups.Remove(SelectedGroup);
            SelectedGroup = Groups.FirstOrDefault();
            RebuildGroupKeys();
            Changed();
        }

        private void AddSlot()
        {
            var names = SelectedGroup.Group.Slots.Select(s => s.Name).ToList();
            string name = "A";
            for (char c = 'A'; c <= 'Z'; c++) if (!names.Contains(c.ToString())) { name = c.ToString(); break; }
            var slot = new SlotTarget { Name = name };
            SelectedGroup.Group.Slots.Add(slot);
            SelectedGroup.Slots.Add(new SlotItem(slot, Changed));
            Changed();
        }

        private void RemoveSlot(SlotItem s)
        {
            if (s == null || SelectedGroup == null) return;
            SelectedGroup.Group.Slots.Remove(s.Slot);
            SelectedGroup.Slots.Remove(s);
            Changed();
        }

        private void AddPlacement()
        {
            var p = new ExistingPlacement { GroupKey = Groups.FirstOrDefault()?.Key ?? "Main" };
            _cfg.Placements.Add(p);
            Placements.Add(new PlacementItem(p, Changed));
            Changed();
        }

        private void RemovePlacement(PlacementItem p)
        {
            if (p == null) return;
            _cfg.Placements.Remove(p.Placement);
            Placements.Remove(p);
            Changed();
        }

        private async Task PickAnchor(bool grid, bool second)
        {
            var group = SelectedGroup;
            AnchorPick pick = null;
            Status = grid ? "In Revit: pick two straight grids in the active view (Esc cancels)." : "In Revit: pick a point in the active plan view (Esc cancels).";
            await RunBusy("Waiting for the pick in Revit", async ct => { pick = grid ? await _gw.PickGridIntersection(second) : await _gw.PickPoint(second); });
            if (pick == null) return;
            if (!pick.Ok) { Status = pick.Message; _dialogs.Info("Model anchor", pick.Message); return; }
            if (second)
            {
                group.Group.Anchor.HasSecond = true;
                group.Group.Anchor.X2 = pick.Anchor.X;
                group.Group.Anchor.Y2 = pick.Anchor.Y;
            }
            else
            {
                bool hadSecond = group.Group.Anchor.HasSecond;
                group.Group.Anchor = pick.Anchor;
                group.Group.Anchor.HasSecond = false;
                if (hadSecond) Status = "Anchor changed: pick the second reference again if you want the full-overlay check.";
            }
            group.Group.Anchor.DocumentKey = _gw.DocumentKey;
            group.RefreshAnchor();
            PickSecondGridsCommand.Refresh();
            Status = (second ? "Second reference: " : "Anchor: ") + pick.Message;
            Changed();
        }

        private async Task PickReference(SlotItem slot)
        {
            var group = SelectedGroup;
            if (slot == null) slot = group?.Slots.FirstOrDefault();
            if (slot == null) return;
            if (group.Method == AlignmentMethod.ModelAnchor && !group.Group.Anchor.IsSet)
            {
                _dialogs.Info("Match Reference Viewport", "Pick the model anchor of group '" + group.Key + "' first.");
                return;
            }
            ReferencePick pick = null;
            Status = "In Revit: open the reference sheet and pick the correctly placed viewport.";
            await RunBusy("Waiting for the viewport pick in Revit", async ct => { pick = await _gw.PickReferenceViewport(group.Group.Anchor, group.Method); });
            if (pick == null) return;
            if (!pick.Ok) { Status = pick.Message; _dialogs.Info("Match Reference Viewport", pick.Message); return; }
            if (slot.Frame == SheetFrame.TitleBlock)
            {
                if (!pick.TitleBlockXmm.HasValue) { _dialogs.Info("Match Reference Viewport", "The reference sheet has no single title block, so a title-block-relative target cannot be computed. Use the Sheet frame or a sheet with one title block."); return; }
                slot.Slot.Xmm = Math.Round(pick.TitleBlockXmm.Value, 3);
                slot.Slot.Ymm = Math.Round(pick.TitleBlockYmm.Value, 3);
            }
            else
            {
                slot.Slot.Xmm = Math.Round(pick.SheetXmm, 3);
                slot.Slot.Ymm = Math.Round(pick.SheetYmm, 3);
            }
            slot.Slot.ReferenceDescription = pick.Description;
            slot.Refresh();
            Status = "Slot " + slot.Name + " target taken from " + pick.Description;
            Changed();
        }

        // ------------------------------------------------------------------ review

        public void Rebuild()
        {
            _plan = PlanBuilder.Build(_cfg, _cat, _gw.DocumentKey);
            SyncEditorsAfterPlan();
            FillRows();
            PlanMessages.Clear();
            foreach (var e in _plan.GlobalErrors) PlanMessages.Add("Error: " + e);
            foreach (var w in _plan.GlobalWarnings) PlanMessages.Add("Note: " + w);
            OnPropertyChanged(nameof(PlanSummary));
            RunCommand.Refresh();
            RaisePreview();
        }

        private void FillRows()
        {
            string keep = _selectedRow?.Row.RowKey;
            Rows.Clear();
            // Grouped by sheet in the Review: the sheet's primary first, then its members.
            var order = _plan.Rows.Select((r, i) => (r, i)).OrderBy(x => string.IsNullOrEmpty(x.r.SheetKey) ? 1 : 0)
                             .ThenBy(x => x.r.SheetKey == null ? 0 : _plan.Rows.FindIndex(y => y.SheetKey == x.r.SheetKey))
                             .ThenBy(x => x.r.IsSheetPrimary ? 0 : 1).ThenBy(x => x.i).Select(x => x.r);
            foreach (var r in order)
            {
                if (ErrorsOnly && r.Status != RowStatus.Error) continue;
                Rows.Add(new PlanRowItem(r, _cfg, Rebuild));
            }
            _selectedRow = Rows.FirstOrDefault(r => r.Row.RowKey == keep) ?? Rows.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedRow));
            RebuildRowTitle();
        }

        /// <summary>Opens the renumbering preview (Numbering step): nothing is renumbered without approving the list.</summary>
        private void RegenerateNumbers()
        {
            PreviewRenumber();
            Step = NumberingStep;
        }

        private void RaisePreview() => OnPropertyChanged(nameof(Preview));

        private SheetPreviewData BuildPreview()
        {
            var d = new SheetPreviewData { UsableLeft = _cfg.UsableLeftMm, UsableBottom = _cfg.UsableBottomMm, UsableRight = _cfg.UsableRightMm, UsableTop = _cfg.UsableTopMm };
            var row = _selectedRow?.Row;
            var tb = _cat.TitleBlock(row?.TitleBlockUniqueId) ?? _cat.TitleBlocks.FirstOrDefault();
            if (tb != null && tb.WidthMm > 0) { d.WidthMm = tb.WidthMm; d.HeightMm = tb.HeightMm; d.SizeKnown = true; d.Title = tb.Display; }
            else { d.WidthMm = 841; d.HeightMm = 594; d.Notes.Add("Sheet size unknown (title block does not report it): A1 outline shown as an estimate."); }
            var sameSheet = row == null || string.IsNullOrEmpty(row.SheetKey) ? new List<PlanRow>() : _plan.Rows.Where(r => r.SheetKey == row.SheetKey && r.Include).ToList();
            foreach (var og in sameSheet.Where(r => !string.IsNullOrEmpty(r.OverlayGroupId)).GroupBy(r => r.OverlayGroupId))
            {
                var r0 = og.First();
                var s0 = _cfg.Group(r0.GroupKey)?.Slot(r0.SlotName);
                if (s0 != null) d.Overlays.Add(("Overlay " + string.Join(" + ", og.Select(x => x.ViewName)), s0.Xmm, s0.Ymm, og.Count()));
            }
            foreach (var r in sameSheet.Where(r => string.IsNullOrEmpty(r.OverlayGroupId)))
            {
                var g = _cfg.Group(r.GroupKey);
                var s = g?.Slot(r.SlotName);
                if (s != null) d.Anchors.Add((r.GroupKey + "/" + s.Name + " · " + r.ViewName, s.Xmm, s.Ymm, s.Frame.ToString()));
            }
            AddTitlePreview(d, sameSheet);
            if (row == null)
                foreach (var g in _cfg.Groups) foreach (var s in g.Slots) d.Anchors.Add((g.Key + "/" + s.Name, s.Xmm, s.Ymm, s.Frame.ToString()));
            if (d.Anchors.Any(a => a.frame == SheetFrame.TitleBlock.ToString())) d.Notes.Add("Title-block-relative anchors are drawn assuming the title block origin is at the sheet origin (estimate).");
            if (_result != null && row != null)
                foreach (var rr in _result.Rows.Where(x => sameSheet.Any(s => s.RowKey == x.RowKey)))
                {
                    if (rr.BoxMm != null) d.MeasuredBoxes.Add((rr.ViewName, rr.BoxMm));
                    if (rr.LabelMm != null) d.MeasuredLabels.Add((rr.ViewName, rr.LabelMm));
                }
            d.Notes.Add(d.MeasuredBoxes.Count > 0 ? "Solid boxes: viewport and title outlines measured in Revit after the run." : "Viewport extents are known only after Run Setup (measured in Revit); grey boxes are existing viewports as read from the model.");
            if (d.Overlays.Count > 0) d.Notes.Add("Overlay: one anchor for all members. The drawing shows the intended overlay; only the Revit measurement after the run proves it.");
            return d;
        }

        private async Task ShowElement(string uid)
        {
            if (string.IsNullOrEmpty(uid)) { Status = "This row has no existing element yet."; return; }
            Status = await _gw.ShowElement(uid);
        }

        public async Task RunAsync()
        {
            Rebuild();
            if (!_plan.CanRun)
            {
                _dialogs.Info("Run Setup", "Fix or exclude the rows with errors first.\n\n" + string.Join("\n", _plan.GlobalErrors.Concat(_plan.Included.Where(r => r.HasErrors).Take(5).Select(r => r.ViewName + ": " + r.Errors[0]))));
                return;
            }
            if (!_dialogs.Confirm("Run Setup", _plan.Summary + "\n\nAll or nothing: if a required operation or alignment check fails, everything is rolled back. The model is not saved or synchronised. One Undo reverts the operation."))
                return;
            SetupResult result = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await RunBusy("Running setup", async ct =>
            {
                var progress = new InlineProgress(t => BusyText = t);
                result = await _gw.Run(SetupJson.Clone(_cfg), _plan, progress, ct);
            }, cancellable: true);
            if (result == null) return;
            result.Duration = sw.Elapsed;
            _result = result;
            Results.Clear();
            foreach (var r in result.Rows) Results.Add(r);
            OnPropertyChanged(nameof(Result));
            OnPropertyChanged(nameof(ResultSummary));
            _log.Info("Project Setup: " + ResultSummary + " in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            Status = ResultSummary;
            if (result.Committed)
            {
                await RefreshCatalogAsync(); // new associations → a second run is a no-op
                Rebuild();
            }
            RaisePreview();
        }

        private sealed class InlineProgress : IProgress<string>
        {
            private readonly Action<string> _a;
            public InlineProgress(Action<string> a) { _a = a; }
            public void Report(string value) => _a(value);
        }

        // ------------------------------------------------------------------ profiles

        /// <summary>Re-reads the profile folder (tests and external changes).</summary>
        public void ReloadProfilesForTest() => ReloadProfiles();

        private void ReloadProfiles()
        {
            var errors = new List<string>();
            var names = _profiles.Names(errors);
            Profiles.Clear();
            foreach (var n in names) Profiles.Add(n);
            if (errors.Count > 0) Status = "Some profiles could not be read: " + string.Join("; ", errors);
        }

        private void NewConfig()
        {
            if (!_dialogs.Confirm("New configuration", "Start a new configuration? Unsaved changes are lost.")) return;
            _cfg = new SetupConfig { DocumentKey = _gw.DocumentKey };
            _result = null;
            Results.Clear();
            SyncLevels();
            SeedDefaults();
            Rebuild();
        }

        private void SaveProfile()
        {
            string name = _dialogs.Prompt("Save profile", "Profile name:", string.IsNullOrEmpty(SelectedProfile) ? _cfg.Name : SelectedProfile);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (_profiles.Exists(name) && name != SelectedProfile && !_dialogs.Confirm("Save profile", "Replace profile '" + name + "'?")) return;
            _cfg.Name = name;
            _profiles.Save(_cfg);
            ReloadProfiles();
            SelectedProfile = name;
            Status = "Profile '" + name + "' saved.";
        }

        private void LoadProfile()
        {
            var c = _profiles.Load(SelectedProfile);
            ApplyLoaded(c);
        }

        private void ApplyLoaded(SetupConfig c)
        {
            var report = ProfilePortability.Prepare(c, _cat, _gw.DocumentKey);
            if (c.LoadedFormat < SetupConfig.CurrentFormat) report.Migrated.Add("Profile format " + c.LoadedFormat + " → " + SetupConfig.CurrentFormat + ": patterns became Build Name rules (or Advanced rules where noted), Sheet Number patterns became numbering profiles. Numbers and associations are kept.");
            _cfg = c;
            _result = null;
            Results.Clear();
            SyncLevels();
            LoadEditors();
            Rebuild();
            RefreshExamples();
            Status = "Profile '" + c.Name + "' loaded.";
            foreach (var w in _plan.GlobalWarnings.Where(w => w.Contains("shared sheets by number"))) report.Migrated.Add(w);
            if (!report.Complete) _dialogs.Info("Profile '" + c.Name + "' – review these references", report + "\n\nNames are kept only as hints; nothing was matched by name.");
        }

        private void DuplicateProfile()
        {
            string n = _dialogs.Prompt("Duplicate profile", "New name:", SelectedProfile + " (copy)");
            if (string.IsNullOrWhiteSpace(n)) return;
            _profiles.Duplicate(SelectedProfile, n.Trim());
            ReloadProfiles();
            SelectedProfile = n.Trim();
        }

        private void RenameProfile()
        {
            string n = _dialogs.Prompt("Rename profile", "New name:", SelectedProfile);
            if (string.IsNullOrWhiteSpace(n)) return;
            _profiles.Rename(SelectedProfile, n.Trim());
            ReloadProfiles();
            SelectedProfile = n.Trim();
        }

        private void DeleteProfile()
        {
            if (!_dialogs.Confirm("Delete profile", "Delete profile '" + SelectedProfile + "'?")) return;
            _profiles.Delete(SelectedProfile);
            ReloadProfiles();
            SelectedProfile = null;
        }

        private void ImportProfile()
        {
            string f = _dialogs.PickOpenProfile();
            if (string.IsNullOrEmpty(f)) return;
            var c = _profiles.Import(f);
            ReloadProfiles();
            SelectedProfile = c.Name;
            ApplyLoaded(_profiles.Load(c.Name));
        }

        private void ExportProfile()
        {
            string f = _dialogs.PickSaveProfile(SelectedProfile + ".cfsetup.json");
            if (string.IsNullOrEmpty(f)) return;
            _profiles.Export(SelectedProfile, f);
            Status = "Profile exported to " + f;
        }

        // ------------------------------------------------------------------ busy / errors

        private async Task RunBusy(string text, Func<CancellationToken, Task> work, bool cancellable = false)
        {
            if (IsBusy) return;
            _cts = cancellable ? new CancellationTokenSource() : null;
            BusyText = text;
            IsBusy = true;
            try { await work(_cts?.Token ?? CancellationToken.None); }
            catch (OperationCanceledException) { Status = "Cancelled."; }
            catch (Exception ex) { ReportError(ex); }
            finally
            {
                IsBusy = false;
                BusyText = string.Empty;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void ReportError(Exception ex)
        {
            _log.Error("Project Setup", ex);
            Status = "Error: " + ex.Message;
            _dialogs.Error("CausaFix Project Setup", ex.Message);
        }

        private void Guard(Action a)
        {
            try { a(); } catch (Exception ex) { ReportError(ex); }
        }
    }
}
