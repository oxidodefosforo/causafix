using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.Core.Mvvm;
using CausaFix.Core.Model;
using CausaFix.Core.Profiles;

namespace CausaFix.ExcelLink
{
    /// <summary>Implemented by the main view model: busy state, progress, cancellation, errors.</summary>
    public interface IBusyHost
    {
        Task RunAsync(string text, Func<CancellationToken, IProgress<ProgressInfo>, Task> work);
        void ReportError(Exception ex);
        void SetStatus(string text);
    }

    /// <summary>A source tab (categories, elements, schedules, spatial).</summary>
    public abstract class SourceTabViewModel : ObservableObject
    {
        protected readonly IExcelLinkGateway Gateway;
        protected readonly IBusyHost Host;
        private bool _loaded;

        protected SourceTabViewModel(IExcelLinkGateway gateway, IBusyHost host, string title, string profileTab)
        {
            Gateway = gateway; Host = host; Title = title; ProfileTab = profileTab;
        }

        public string Title { get; }
        public string ProfileTab { get; }
        public bool IsLoaded { get => _loaded; protected set => Set(ref _loaded, value); }

        public abstract Task RefreshAsync();
        public abstract bool CanExport { get; }
        public abstract string Counters { get; }
        public abstract Task<ExportPackage> SnapshotAsync(IProgress<ProgressInfo> progress, CancellationToken ct);
        public abstract void ResetSelection();
        public abstract ExcelLinkProfile CaptureProfile(string name);
        /// <summary>Applies a profile; returns a human readable list of missing references (empty if complete).</summary>
        public abstract Task<string> ApplyProfileAsync(ExcelLinkProfile profile);

        public event EventHandler StateChanged;
        protected void RaiseState()
        {
            OnPropertyChanged(nameof(Counters));
            OnPropertyChanged(nameof(CanExport));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Model categories, annotation categories and spatial (Rooms/Spaces/Areas) tabs.</summary>
    public class CategoryTabViewModel : SourceTabViewModel
    {
        public enum Mode { Model, Annotation, Spatial }

        private readonly Mode _mode;
        private ScopeMode _scope = ScopeMode.WholeModel;
        private bool _oneRowPerType, _includeLinks;
        private int _paramVersion;
        private string _scopeWarning = string.Empty;

        public CategoryTabViewModel(IExcelLinkGateway gateway, IBusyHost host, Mode mode, string title = null)
            : base(gateway, host, title ?? (mode == Mode.Model ? "Model Categories" : mode == Mode.Annotation ? "Annotation Categories" : "Spatial"), title ?? mode.ToString())
        {
            _mode = mode;
            Categories = new CheckListViewModel<CategoryInfo>(c => c.Name, c => c.Key);
            Categories.CheckedChanged += async (s, e) =>
            {
                try { await OnCategoriesChanged(); }
                catch (Exception ex) { Host.ReportError(ex); }
            };
            Parameters = new ParameterPanelViewModel();
            Parameters.SelectionChanged += (s, e) => RaiseState();
        }

        public CheckListViewModel<CategoryInfo> Categories { get; }
        public ParameterPanelViewModel Parameters { get; }

        public ScopeMode Scope
        {
            get => _scope;
            set
            {
                if (!Set(ref _scope, value)) return;
                OnPropertyChanged(nameof(ScopeHelp));
                OnPropertyChanged(nameof(IsWholeModel)); OnPropertyChanged(nameof(IsActiveView)); OnPropertyChanged(nameof(IsCurrentSelection));
                _ = SafeRefresh();
            }
        }

        public bool IsWholeModel { get => Scope == ScopeMode.WholeModel; set { if (value) Scope = ScopeMode.WholeModel; } }
        public bool IsActiveView { get => Scope == ScopeMode.ActiveView; set { if (value) Scope = ScopeMode.ActiveView; } }
        public bool IsCurrentSelection { get => Scope == ScopeMode.CurrentSelection; set { if (value) Scope = ScopeMode.CurrentSelection; } }

        public string ScopeHelp
        {
            get
            {
                switch (Scope)
                {
                    case ScopeMode.ActiveView: return "Elements returned for the active graphical view (respects its visibility and crop). Not available for schedules or sheets. Linked elements are not filtered by the view.";
                    case ScopeMode.CurrentSelection: return "Only the elements selected in Revit when you press Refresh.";
                    default: return "Every element of the checked categories in the host model.";
                }
            }
        }

        public string ScopeWarning { get => _scopeWarning; private set => Set(ref _scopeWarning, value); }

        /// <summary>"Export by Type": one row per type (type parameters only).</summary>
        public bool OneRowPerType { get => _oneRowPerType; set { if (Set(ref _oneRowPerType, value)) _ = ReloadParameters(); } }
        public bool IncludeLinks { get => _includeLinks; set => Set(ref _includeLinks, value); }
        public bool IsSpatial => _mode == Mode.Spatial;
        public virtual bool SupportsTypeRows => _mode != Mode.Spatial;
        /// <summary>True for the Elements tab (families/types list and text filter).</summary>
        public virtual bool ShowTypes => false;

        protected RowMode RowMode => OneRowPerType ? RowMode.Type : RowMode.Instance;

        public override bool CanExport => Categories.CheckedCount > 0 && Parameters.SelectedForExport().Count > 0;

        public override string Counters =>
            Title + ": categories selected " + Categories.CheckedCount.ToString(CultureInfo.CurrentCulture) +
            " | parameters found " + Parameters.AvailableCount.ToString(CultureInfo.CurrentCulture) +
            " | parameters selected " + Parameters.SelectedCount.ToString(CultureInfo.CurrentCulture) +
            (Parameters.MissingCount > 0 ? " (" + Parameters.MissingCount + " missing)" : "");

        private async Task SafeRefresh()
        {
            try { await RefreshAsync(); } catch (Exception ex) { Host.ReportError(ex); }
        }

        public override async Task RefreshAsync()
        {
            List<CategoryInfo> cats = null;
            ScopeWarning = string.Empty;
            await Host.RunAsync("Reading categories", async (ct, p) =>
            {
                try { cats = await Gateway.Categories(Scope); }
                catch (InvalidOperationException ex) { ScopeWarning = ex.Message; cats = new List<CategoryInfo>(); }
            });
            if (cats == null) return;
            Categories.SetItems(Filter(cats));
            IsLoaded = true;
            await OnCategoriesChanged(); // Elements tab also reloads its types
        }

        protected virtual IEnumerable<CategoryInfo> Filter(IEnumerable<CategoryInfo> cats)
        {
            switch (_mode)
            {
                case Mode.Annotation: return cats.Where(c => c.Kind == CategoryKind.Annotation);
                case Mode.Spatial: return cats.Where(c => c.IsSpatial);
                default: return cats.Where(c => c.Kind == CategoryKind.Model);
            }
        }

        protected virtual Task OnCategoriesChanged() => ReloadParameters();

        protected virtual IList<string> TypeFilter() => new List<string>();

        protected async Task ReloadParameters()
        {
            int version = ++_paramVersion;
            var ids = Categories.Checked.Select(c => c.Id).ToList();
            List<ParameterInfo> list;
            if (ids.Count == 0) list = new List<ParameterInfo>();
            else
            {
                list = null;
                await Host.RunAsync("Reading parameters", async (ct, p) => { list = await Gateway.Parameters(Scope, ids, RowMode, TypeFilter()); });
                if (list == null) return;
            }
            if (version != _paramVersion) return; // a newer request superseded this one
            Parameters.SetAvailable(list);
            RaiseState();
        }

        public virtual SnapshotRequest BuildRequest()
        {
            var req = new SnapshotRequest
            {
                Source = _mode == Mode.Annotation ? SheetSource.AnnotationCategories : _mode == Mode.Spatial ? SheetSource.Spatial : SheetSource.ModelCategories,
                Scope = Scope,
                RowMode = RowMode,
                IncludeLinks = IncludeLinks,
                SheetPerCategory = true
            };
            req.CategoryIds.AddRange(Categories.Checked.Select(c => c.Id));
            req.Parameters.AddRange(Parameters.SelectedForExport());
            return req;
        }

        public override Task<ExportPackage> SnapshotAsync(IProgress<ProgressInfo> progress, CancellationToken ct)
            => Gateway.Snapshot(BuildRequest(), progress, ct);

        public override void ResetSelection()
        {
            Categories.CheckNoneCommand.Execute(null);
            Parameters.ClearAll();
            RaiseState();
        }

        public override ExcelLinkProfile CaptureProfile(string name)
        {
            var p = new ExcelLinkProfile
            {
                Name = name,
                Tab = ProfileTab,
                Scope = Scope.ToString(),
                RowMode = RowMode.ToString(),
                IncludeLinks = IncludeLinks,
                CommonParametersOnly = Parameters.CommonOnly,
                ShowInstance = Parameters.ShowInstance,
                ShowType = Parameters.ShowType,
                ShowReadOnly = Parameters.ShowReadOnly
            };
            p.Categories.AddRange(Categories.Checked.Select(c => new ProfileItem(c.Key, c.Name)));
            p.Parameters.AddRange(Parameters.Selected.Select(s => new ProfileItem(s.Item.Key.ToString(), s.Name)));
            return p;
        }

        public override async Task<string> ApplyProfileAsync(ExcelLinkProfile profile)
        {
            if (Enum.TryParse(profile.Scope, out ScopeMode scope) && scope != Scope)
            {
                _scope = scope;
                OnPropertyChanged(nameof(Scope)); OnPropertyChanged(nameof(ScopeHelp));
                OnPropertyChanged(nameof(IsWholeModel)); OnPropertyChanged(nameof(IsActiveView)); OnPropertyChanged(nameof(IsCurrentSelection));
                await RefreshAsync();
            }
            IncludeLinks = profile.IncludeLinks;
            _oneRowPerType = SupportsTypeRows && profile.RowMode == RowMode.Type.ToString();
            OnPropertyChanged(nameof(OneRowPerType));
            Parameters.CommonOnly = profile.CommonParametersOnly;
            Parameters.ShowInstance = profile.ShowInstance;
            Parameters.ShowType = profile.ShowType;
            Parameters.ShowReadOnly = profile.ShowReadOnly;

            var catKeys = new HashSet<string>(profile.Categories.Select(c => c.Key), StringComparer.Ordinal);
            Categories.CheckKeys(catKeys); // triggers parameter reload
            await ReloadParameters();

            var keys = new List<ParamKey>();
            var names = new Dictionary<ParamKey, string>();
            var invalid = new List<string>();
            foreach (var item in profile.Parameters)
            {
                if (ParamKey.TryParse(item.Key, out var k)) { keys.Add(k); names[k] = item.Name; }
                else invalid.Add(item.Name);
            }
            Parameters.SetSelected(keys, names);

            var resolution = ProfileResolution.Resolve(profile,
                new HashSet<string>(Categories.All.Select(c => c.Item.Key), StringComparer.Ordinal),
                new HashSet<string>(Parameters.Selected.Where(s => !s.IsMissing).Select(s => s.Item.Key.ToString()), StringComparer.Ordinal),
                null);
            var lines = new List<string>();
            if (resolution.MissingCategories.Count > 0) lines.Add("Categories not present in this scope: " + string.Join(", ", resolution.MissingCategories.Select(c => c.Name)));
            if (resolution.MissingParameters.Count > 0) lines.Add("Parameters not found (matched by identity, never by name): " + string.Join(", ", resolution.MissingParameters.Select(c => c.Name)));
            if (invalid.Count > 0) lines.Add("Damaged parameter keys: " + string.Join(", ", invalid));
            RaiseState();
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Elements tab: categories → families/types → instances or types, with a text filter.</summary>
    public sealed class ElementsTabViewModel : CategoryTabViewModel
    {
        private string _textFilter = string.Empty;

        public ElementsTabViewModel(IExcelLinkGateway gateway, IBusyHost host) : base(gateway, host, Mode.Model, "Elements")
        {
            Types = new CheckListViewModel<TypeInfo>(t => t.Display, t => t.UniqueId);
            Types.CheckedChanged += async (s, e) => { try { await ReloadParameters(); } catch (Exception ex) { Host.ReportError(ex); } };
        }

        public CheckListViewModel<TypeInfo> Types { get; }
        public override bool ShowTypes => true;

        /// <summary>Case-insensitive text matched against family, type, Mark and name when exporting.</summary>
        public string TextFilter { get => _textFilter; set => Set(ref _textFilter, value ?? string.Empty); }

        protected override async Task OnCategoriesChanged()
        {
            var ids = Categories.Checked.Select(c => c.Id).ToList();
            List<TypeInfo> types = new List<TypeInfo>();
            if (ids.Count > 0) await Host.RunAsync("Reading types", async (ct, p) => { types = await Gateway.Types(Scope, ids); });
            Types.SetItems(types);
            await ReloadParameters();
        }

        protected override IList<string> TypeFilter() => Types.Checked.Select(t => t.UniqueId).ToList();

        public override string Counters =>
            "Elements: categories " + Categories.CheckedCount + " | types checked " + Types.CheckedCount + " of " + Types.Count +
            " | parameters selected " + Parameters.SelectedCount + (Parameters.MissingCount > 0 ? " (" + Parameters.MissingCount + " missing)" : "");

        public override SnapshotRequest BuildRequest()
        {
            var req = base.BuildRequest();
            req.Source = SheetSource.Elements;
            req.TextFilter = TextFilter;
            req.TypeUniqueIds.AddRange(Types.Checked.Select(t => t.UniqueId));
            return req;
        }

        public override ExcelLinkProfile CaptureProfile(string name)
        {
            var p = base.CaptureProfile(name);
            p.Tab = "Elements";
            return p;
        }

        public override void ResetSelection()
        {
            Types.CheckNoneCommand.Execute(null);
            TextFilter = string.Empty;
            base.ResetSelection();
        }
    }

    /// <summary>Schedules tab: several schedules, data exchange or presentation export.</summary>
    public sealed class SchedulesTabViewModel : SourceTabViewModel
    {
        private ScheduleExportMode _mode = ScheduleExportMode.DataExchange;
        private Selectable<ScheduleInfo> _focused;
        private int _fieldsVersion;

        public SchedulesTabViewModel(IExcelLinkGateway gateway, IBusyHost host) : base(gateway, host, "Schedules", "Schedules")
        {
            Schedules = new CheckListViewModel<ScheduleInfo>(s => s.Name + " " + s.CategoryName, s => s.UniqueId);
            Schedules.CheckedChanged += (s, e) => RaiseState();
            CheckOnActiveSheetCommand = new RelayCommand(() =>
                Schedules.CheckKeys(new HashSet<string>(Schedules.All.Where(x => x.Item.OnActiveSheet || x.Item.IsActiveView).Select(x => x.Item.UniqueId))));
        }

        public CheckListViewModel<ScheduleInfo> Schedules { get; }
        public System.Collections.ObjectModel.ObservableCollection<ParamItem> Fields { get; } = new System.Collections.ObjectModel.ObservableCollection<ParamItem>();
        public RelayCommand CheckOnActiveSheetCommand { get; }

        public ScheduleExportMode ExportMode
        {
            get => _mode;
            set { if (Set(ref _mode, value)) { OnPropertyChanged(nameof(IsDataExchange)); OnPropertyChanged(nameof(IsPresentation)); RaiseState(); } }
        }
        public bool IsDataExchange { get => ExportMode == ScheduleExportMode.DataExchange; set { if (value) ExportMode = ScheduleExportMode.DataExchange; } }
        public bool IsPresentation { get => ExportMode == ScheduleExportMode.Presentation; set { if (value) ExportMode = ScheduleExportMode.Presentation; } }

        /// <summary>Schedule highlighted in the list; its fields are shown in the right panel.</summary>
        public Selectable<ScheduleInfo> Focused
        {
            get => _focused;
            set { if (Set(ref _focused, value)) { OnPropertyChanged(nameof(FocusedNote)); _ = LoadFields(); } }
        }

        public string FocusedNote
        {
            get
            {
                var s = _focused?.Item;
                if (s == null) return "Select a schedule to see its fields and how it can be exported.";
                string data = s.DataExchangeSupported ? "Data exchange: supported. " + s.DataExchangeNote : "Data exchange: NOT supported. " + s.DataExchangeNote;
                return s.Name + " – " + s.Kind + ", " + s.CategoryName + ", " + s.FieldCount + " field(s)" +
                       (s.HiddenFieldCount > 0 ? ", " + s.HiddenFieldCount + " hidden" : "") + (s.Itemized ? "" : ", not itemized") +
                       ".\n" + data + "\nPresentation export: supported (read-only).";
            }
        }

        private async Task LoadFields()
        {
            int v = ++_fieldsVersion;
            Fields.Clear();
            var s = _focused?.Item;
            if (s == null || !s.DataExchangeSupported) return;
            try
            {
                var fields = await Gateway.ScheduleFields(s.UniqueId);
                if (v != _fieldsVersion) return;
                foreach (var f in fields) Fields.Add(new ParamItem(f));
            }
            catch (Exception ex) { Host.ReportError(ex); }
        }

        public override bool CanExport
        {
            get
            {
                var chosen = Schedules.Checked.ToList();
                if (chosen.Count == 0) return false;
                return ExportMode == ScheduleExportMode.Presentation || chosen.Any(c => c.DataExchangeSupported);
            }
        }

        public override string Counters
        {
            get
            {
                var chosen = Schedules.Checked.ToList();
                int unsupported = ExportMode == ScheduleExportMode.DataExchange ? chosen.Count(c => !c.DataExchangeSupported) : 0;
                return "Schedules selected " + chosen.Count + " of " + Schedules.Count + " | mode " + (IsDataExchange ? "Data exchange (re-importable)" : "Presentation (read-only)") +
                       (unsupported > 0 ? " | " + unsupported + " not supported in data mode (skipped)" : "");
            }
        }

        public override async Task RefreshAsync()
        {
            List<ScheduleInfo> list = null;
            await Host.RunAsync("Reading schedules", async (ct, p) => { list = await Gateway.Schedules(); });
            if (list == null) return;
            Schedules.SetItems(list);
            IsLoaded = true;
            RaiseState();
        }

        public override Task<ExportPackage> SnapshotAsync(IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            var ids = Schedules.Checked.Where(s => ExportMode == ScheduleExportMode.Presentation || s.DataExchangeSupported).Select(s => s.UniqueId).ToList();
            return Gateway.SnapshotSchedules(ids, ExportMode, progress, ct);
        }

        public override void ResetSelection()
        {
            Schedules.CheckNoneCommand.Execute(null);
            RaiseState();
        }

        public override ExcelLinkProfile CaptureProfile(string name)
        {
            var p = new ExcelLinkProfile { Name = name, Tab = "Schedules", ScheduleMode = ExportMode.ToString() };
            p.Schedules.AddRange(Schedules.Checked.Select(s => new ProfileItem(s.UniqueId, s.Name)));
            return p;
        }

        public override Task<string> ApplyProfileAsync(ExcelLinkProfile profile)
        {
            if (Enum.TryParse(profile.ScheduleMode, out ScheduleExportMode m)) ExportMode = m;
            Schedules.CheckKeys(new HashSet<string>(profile.Schedules.Select(s => s.Key), StringComparer.Ordinal));
            var missing = profile.Schedules.Where(s => !Schedules.All.Any(x => x.Item.UniqueId == s.Key)).Select(s => s.Name).ToList();
            RaiseState();
            return Task.FromResult(missing.Count == 0 ? "" : "Schedules not found in this model (matched by UniqueId): " + string.Join(", ", missing));
        }
    }
}
