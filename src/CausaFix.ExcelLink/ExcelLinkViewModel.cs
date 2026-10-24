using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Profiles;
using CausaFix.Core.Settings;
using CausaFix.Core.Values;
using CausaFix.Core.Workbook;
using CausaFix.Core.Xlsx;

namespace CausaFix.ExcelLink
{
    /// <summary>Main view model of the Excel Link window.</summary>
    public sealed class ExcelLinkViewModel : ObservableObject, IBusyHost
    {
        private readonly IExcelLinkGateway _gateway;
        private readonly IDialogService _dialogs;
        private readonly ModulePaths _paths;
        private readonly SettingsStore _settingsStore;
        private readonly FileLog _log;
        private readonly ProfileStore _profiles;
        private CancellationTokenSource _cts;
        private int _busyDepth;
        private bool _isBusy, _indeterminate = true;
        private string _busyText = string.Empty, _status = "Ready.", _documentTitle = string.Empty, _documentInfo = string.Empty;
        private double _progress, _progressMax = 1;
        private int _selectedTab;
        private SourceTabViewModel _lastSource;
        private string _selectedProfile;

        public ExcelLinkViewModel(IExcelLinkGateway gateway, IDialogService dialogs, ModulePaths paths)
        {
            _gateway = gateway;
            _dialogs = dialogs;
            _paths = paths ?? ModulePaths.Default();
            _settingsStore = new SettingsStore(_paths.SettingsFile);
            Settings = _settingsStore.Load();
            _log = new FileLog(_paths.LogsFolder);
            _profiles = new ProfileStore(_paths.ProfilesFolder);

            ModelTab = new CategoryTabViewModel(gateway, this, CategoryTabViewModel.Mode.Model);
            AnnotationTab = new CategoryTabViewModel(gateway, this, CategoryTabViewModel.Mode.Annotation);
            ElementsTab = new ElementsTabViewModel(gateway, this);
            SchedulesTab = new SchedulesTabViewModel(gateway, this);
            SpatialTab = new CategoryTabViewModel(gateway, this, CategoryTabViewModel.Mode.Spatial);
            Preview = new PreviewViewModel(gateway, dialogs, ShowReviewAsync);
            Preview.GridOptions = GridOptions();
            SourceTabs = new List<SourceTabViewModel> { ModelTab, AnnotationTab, ElementsTab, SchedulesTab, SpatialTab };
            foreach (var t in SourceTabs) t.StateChanged += (s, e) => { OnPropertyChanged(nameof(Counters)); RefreshCommands(); };
            Preview.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(PreviewViewModel.Summary)) { OnPropertyChanged(nameof(Counters)); RefreshCommands(); } };
            _lastSource = ModelTab;

            Action<Exception> onError = ReportError;
            RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy, onError);
            ResetSelectionCommand = new RelayCommand(() => CurrentSource?.ResetSelection(), () => !IsBusy);
            PreviewCommand = new AsyncCommand(PreviewAsync, () => !IsBusy && (CurrentSource?.CanExport ?? false), onError);
            ExportCommand = new AsyncCommand(ExportAsync, () => !IsBusy && (IsPreviewTab ? Preview.HasData : (CurrentSource?.CanExport ?? false)), onError);
            ImportCommand = new AsyncCommand(ImportAsync, () => !IsBusy, onError);
            ApplyPreviewCommand = new AsyncCommand(() => Preview.ApplyFlowAsync(ReviewContextFor(string.Empty)), () => !IsBusy && Preview.PendingCount > 0, onError);
            ReloadPreviewCommand = new AsyncCommand(() => Preview.ReloadAsync(), () => !IsBusy && Preview.HasData, onError);
            CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy && _cts != null);
            InventoryCommand = new AsyncCommand(o => InventoryAsync(o as string), _ => !IsBusy, onError);
            SaveProfileCommand = new RelayCommand(() => Guard(SaveProfile), () => !IsBusy);
            SaveProfileAsCommand = new RelayCommand(() => Guard(() => SaveProfileAs(null)), () => !IsBusy);
            LoadProfileCommand = new AsyncCommand(LoadProfileAsync, () => !IsBusy && !string.IsNullOrEmpty(SelectedProfile), onError);
            DuplicateProfileCommand = new RelayCommand(() => Guard(DuplicateProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            RenameProfileCommand = new RelayCommand(() => Guard(RenameProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            DeleteProfileCommand = new RelayCommand(() => Guard(DeleteProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            ImportProfileCommand = new RelayCommand(() => Guard(ImportProfile));
            ExportProfileCommand = new RelayCommand(() => Guard(ExportProfile), () => !string.IsNullOrEmpty(SelectedProfile));
            SelectInRevitCommand = new AsyncCommand(o => RowAction(o, _gateway.Select), _ => !IsBusy, onError);
            ShowInRevitCommand = new AsyncCommand(o => RowAction(o, _gateway.Show), _ => !IsBusy, onError);
            IsolateCommand = new AsyncCommand(o => RowAction(o, _gateway.Isolate), _ => !IsBusy, onError);
            ResetIsolateCommand = new AsyncCommand(async () => Status = await _gateway.ResetIsolate(), () => !IsBusy, onError);
            OpenLogsCommand = new RelayCommand(() => { Directory.CreateDirectory(_paths.LogsFolder); _dialogs.OpenWithShell(_paths.LogsFolder); });
            OpenReportsCommand = new RelayCommand(() => { Directory.CreateDirectory(_paths.ReportsFolder); _dialogs.OpenWithShell(_paths.ReportsFolder); });
            SettingsCommand = new RelayCommand(EditSettings);
            ReloadProfiles();
        }

        // ------------------------------------------------------------ state

        public CausaFixSettings Settings { get; }
        public CategoryTabViewModel ModelTab { get; }
        public CategoryTabViewModel AnnotationTab { get; }
        public ElementsTabViewModel ElementsTab { get; }
        public SchedulesTabViewModel SchedulesTab { get; }
        public CategoryTabViewModel SpatialTab { get; }
        public PreviewViewModel Preview { get; }
        public List<SourceTabViewModel> SourceTabs { get; }
        public ObservableCollection<string> Profiles { get; } = new ObservableCollection<string>();

        public const int PreviewTabIndex = 5;

        /// <summary>0..4 source tabs, 5 Preview/Edit.</summary>
        public int SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (!Set(ref _selectedTab, value)) return;
                if (value >= 0 && value < SourceTabs.Count)
                {
                    _lastSource = SourceTabs[value];
                    if (!_lastSource.IsLoaded && !IsBusy) _ = SafeRun(_lastSource.RefreshAsync);
                }
                OnPropertyChanged(nameof(IsPreviewTab));
                OnPropertyChanged(nameof(Counters));
                RefreshCommands();
            }
        }

        public bool IsPreviewTab => SelectedTab == PreviewTabIndex;
        /// <summary>The source tab used by Preview/Export (the last source tab the user worked in).</summary>
        public SourceTabViewModel CurrentSource => SelectedTab >= 0 && SelectedTab < SourceTabs.Count ? SourceTabs[SelectedTab] : _lastSource;

        public string Counters => IsPreviewTab ? Preview.Summary : CurrentSource?.Counters ?? string.Empty;
        public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) RefreshCommands(); } }
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
        public double Progress { get => _progress; private set => Set(ref _progress, value); }
        public double ProgressMax { get => _progressMax; private set => Set(ref _progressMax, value); }
        public bool ProgressIndeterminate { get => _indeterminate; private set => Set(ref _indeterminate, value); }
        public string Status { get => _status; set => Set(ref _status, value ?? string.Empty); }
        public string DocumentTitle { get => _documentTitle; private set => Set(ref _documentTitle, value); }
        public string DocumentInfo { get => _documentInfo; private set => Set(ref _documentInfo, value); }
        public string SelectedProfile { get => _selectedProfile; set { if (Set(ref _selectedProfile, value)) RefreshCommands(); } }

        public AsyncCommand RefreshCommand { get; }
        public RelayCommand ResetSelectionCommand { get; }
        public AsyncCommand PreviewCommand { get; }
        public AsyncCommand ExportCommand { get; }
        public AsyncCommand ImportCommand { get; }
        public AsyncCommand ApplyPreviewCommand { get; }
        public AsyncCommand ReloadPreviewCommand { get; }
        public RelayCommand CancelCommand { get; }
        public AsyncCommand InventoryCommand { get; }
        public RelayCommand SaveProfileCommand { get; }
        public RelayCommand SaveProfileAsCommand { get; }
        public AsyncCommand LoadProfileCommand { get; }
        public RelayCommand DuplicateProfileCommand { get; }
        public RelayCommand RenameProfileCommand { get; }
        public RelayCommand DeleteProfileCommand { get; }
        public RelayCommand ImportProfileCommand { get; }
        public RelayCommand ExportProfileCommand { get; }
        public AsyncCommand SelectInRevitCommand { get; }
        public AsyncCommand ShowInRevitCommand { get; }
        public AsyncCommand IsolateCommand { get; }
        public AsyncCommand ResetIsolateCommand { get; }
        public RelayCommand OpenLogsCommand { get; }
        public RelayCommand OpenReportsCommand { get; }
        public RelayCommand SettingsCommand { get; }

        private void RefreshCommands()
        {
            foreach (var c in new[] { RefreshCommand, PreviewCommand, ExportCommand, ImportCommand, ApplyPreviewCommand, ReloadPreviewCommand, InventoryCommand, LoadProfileCommand,
                                      SelectInRevitCommand, ShowInRevitCommand, IsolateCommand, ResetIsolateCommand })
                c?.Refresh();
            foreach (var c in new[] { ResetSelectionCommand, CancelCommand, SaveProfileCommand, SaveProfileAsCommand, DuplicateProfileCommand, RenameProfileCommand, DeleteProfileCommand, ExportProfileCommand })
                c?.Refresh();
        }

        // ------------------------------------------------------------ busy / errors

        public async Task RunAsync(string text, Func<CancellationToken, IProgress<ProgressInfo>, Task> work)
        {
            bool outer = _busyDepth == 0;
            _busyDepth++;
            if (outer)
            {
                _cts = new CancellationTokenSource();
                ProgressIndeterminate = true;
                Progress = 0;
            }
            BusyText = text;
            IsBusy = true;
            var progress = new SyncProgress(p =>
            {
                if (p.Total > 0) { ProgressIndeterminate = false; ProgressMax = p.Total; Progress = p.Done; }
                BusyText = string.IsNullOrEmpty(p.Stage) ? text : p.Stage + (p.Total > 0 ? " (" + p.Done + "/" + p.Total + ")" : "");
            });
            try
            {
                await work(_cts.Token, progress);
            }
            catch (OperationCanceledException)
            {
                Status = "Cancelled.";
            }
            finally
            {
                _busyDepth--;
                if (_busyDepth == 0)
                {
                    IsBusy = false;
                    BusyText = string.Empty;
                    _cts?.Dispose();
                    _cts = null;
                }
                CancelCommand.Refresh();
            }
        }

        /// <summary>IProgress that invokes synchronously (we are always on the UI thread here).</summary>
        private sealed class SyncProgress : IProgress<ProgressInfo>
        {
            private readonly Action<ProgressInfo> _a;
            public SyncProgress(Action<ProgressInfo> a) { _a = a; }
            public void Report(ProgressInfo value) => _a(value);
        }

        public void ReportError(Exception ex)
        {
            if (ex is OperationCanceledException) { Status = "Cancelled."; return; }
            _log.Error("Excel Link", ex);
            Status = "Error: " + ex.Message;
            _dialogs.Error("CausaFix Excel Link", ex.Message);
        }

        public void SetStatus(string text) => Status = text;

        private async Task SafeRun(Func<Task> f)
        {
            try { await f(); } catch (Exception ex) { ReportError(ex); }
        }

        private void Guard(Action a)
        {
            try { a(); } catch (Exception ex) { ReportError(ex); }
        }

        private ImportOptions GridOptions()
        {
            var o = Settings.ToImportOptions();
            if (o.DecimalMode == DecimalSeparatorMode.Auto)
                o.DecimalMode = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? DecimalSeparatorMode.Comma : DecimalSeparatorMode.Point;
            return o;
        }

        // ------------------------------------------------------------ lifecycle

        /// <summary>Binds to the active document and loads the first tab.</summary>
        public async Task InitializeAsync()
        {
            DocumentSummary doc = null;
            await RunAsync("Connecting to the model", async (ct, p) => { doc = await _gateway.BindActiveDocument(); });
            if (doc == null) return;
            DocumentTitle = doc.Title;
            DocumentInfo = "Revit " + doc.RevitVersion + (doc.IsWorkshared ? " · workshared" : "") + " · active view: " + doc.ActiveViewName +
                           " · selection: " + doc.SelectionCount + (doc.CreationGuidAvailable ? "" : " · creation GUID unavailable (identity checks are weaker)");
            _log.Info("Excel Link opened on '" + doc.Title + "' (Revit " + doc.RevitVersion + ").");
            await CurrentSource.RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (IsPreviewTab) { await Preview.ReloadAsync(); return; }
            // Rebind (the user may have switched documents) – pending preview edits are protected by the preview itself.
            if (Preview.PendingCount > 0 && !_dialogs.Confirm("Refresh", "Refreshing does not discard the " + Preview.PendingCount + " pending edit(s) in Preview/Edit, but they belong to the previous data. Continue?"))
                return;
            DocumentSummary doc = null;
            await RunAsync("Connecting to the model", async (ct, p) => { doc = await _gateway.BindActiveDocument(); });
            if (doc != null && doc.Title != DocumentTitle)
            {
                DocumentTitle = doc.Title;
                foreach (var t in SourceTabs) t.ResetSelection();
            }
            await CurrentSource.RefreshAsync();
            Status = "Refreshed " + CurrentSource.Title + ".";
        }

        // ------------------------------------------------------------ preview / export

        private async Task<ExportPackage> SnapshotCurrent(SourceTabViewModel tab)
        {
            ExportPackage pkg = null;
            await RunAsync("Reading data from Revit", async (ct, p) => { pkg = await tab.SnapshotAsync(p, ct); });
            return pkg;
        }

        private async Task PreviewAsync()
        {
            var tab = CurrentSource;
            if (Preview.PendingCount > 0 && !_dialogs.Confirm("Preview/Edit", Preview.PendingCount + " pending edit(s) in Preview/Edit will be discarded. Continue?"))
                return;
            var pkg = await SnapshotCurrent(tab);
            if (pkg == null) return;
            var units = await _gateway.UnitConverter(pkg.Sheets.SelectMany(s => s.Columns).Select(c => c.UnitTypeId));
            Preview.GridOptions = GridOptions();
            Preview.Load(pkg, units, () => tab.SnapshotAsync(null, CancellationToken.None));
            SelectedTab = PreviewTabIndex;
            Status = "Preview loaded: " + pkg.Sheets.Sum(s => s.Rows.Count) + " row(s). Edits are not written until you Apply Changes.";
        }

        private async Task ExportAsync()
        {
            ExportPackage pkg;
            string name;
            if (IsPreviewTab)
            {
                if (!Preview.HasData) return;
                if (Preview.PendingCount + Preview.InvalidCount > 0 &&
                    !_dialogs.Confirm("Export", "The workbook will contain the values loaded from Revit. Your " + (Preview.PendingCount + Preview.InvalidCount) +
                                                " pending edit(s) stay in the grid and are NOT applied or exported. Continue?"))
                    return;
                pkg = Preview.Package;
                name = "Preview";
            }
            else
            {
                pkg = null;
                name = CurrentSource.Title;
            }

            string suggested = Sanitize(DocumentTitle + " - " + name + " - " + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)) + ".xlsx";
            string path = _dialogs.PickSaveWorkbook(suggested, Settings.LastFolder);
            if (string.IsNullOrEmpty(path)) return;

            if (pkg == null) pkg = await SnapshotCurrent(CurrentSource);
            if (pkg == null) return;
            if (pkg.Sheets.Count == 0 || pkg.Sheets.All(s => s.Rows.Count == 0 && s.PresentationGrid.Count == 0))
            {
                _dialogs.Info("Export", "Nothing to export: the selection produced no rows.");
                return;
            }

            await RunAsync("Writing workbook", async (ct, p) =>
            {
                try
                {
                    await Task.Run(() => WorkbookExporter.Save(pkg, path), ct);
                }
                catch (XlsxFileLockedException)
                {
                    throw new IOException("The file is open in Excel or locked. Close it and export again.\n" + path);
                }
                catch (UnauthorizedAccessException)
                {
                    throw new IOException("No permission to write to this location:\n" + path);
                }
            });
            Settings.LastFolder = Path.GetDirectoryName(path);
            SaveSettings();
            int rows = pkg.Sheets.Sum(s => s.Rows.Count);
            Status = "Exported " + rows + " row(s) in " + pkg.Sheets.Count + " sheet(s) to " + path;
            _log.Info(Status);
            var notes = pkg.Sheets.SelectMany(s => s.Notes).Distinct().ToList();
            if (notes.Count > 0) _dialogs.Info("Export notes", string.Join("\n\n", notes));
            if (Settings.OpenFileAfterExport) _dialogs.OpenWithShell(path);
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        private async Task InventoryAsync(string kind)
        {
            if (!Enum.TryParse(kind, out InventoryKind k)) return;
            if (k == InventoryKind.ProjectInformation)
            {
                // Project Information is editable: open it in Preview/Edit (it can also be exported from there).
                ExportPackage pkg = null;
                await RunAsync("Reading Project Information", async (ct, p) => { pkg = await _gateway.SnapshotInventory(k, p, ct); });
                if (pkg == null) return;
                var units = await _gateway.UnitConverter(pkg.Sheets.SelectMany(s => s.Columns).Select(c => c.UnitTypeId));
                Preview.GridOptions = GridOptions();
                Preview.Load(pkg, units, () => _gateway.SnapshotInventory(k, null, CancellationToken.None));
                SelectedTab = PreviewTabIndex;
                return;
            }
            string path = _dialogs.PickSaveWorkbook(Sanitize(DocumentTitle + " - " + k) + ".xlsx", Settings.LastFolder);
            if (string.IsNullOrEmpty(path)) return;
            ExportPackage inv = null;
            await RunAsync("Reading " + k, async (ct, p) => { inv = await _gateway.SnapshotInventory(k, p, ct); });
            if (inv == null) return;
            await RunAsync("Writing workbook", async (ct, p) => await Task.Run(() => WorkbookExporter.Save(inv, path), ct));
            Status = k + " exported (read-only inventory) to " + path;
            if (Settings.OpenFileAfterExport) _dialogs.OpenWithShell(path);
        }

        // ------------------------------------------------------------ import

        private ReviewContext ReviewContextFor(string identityNote) => new ReviewContext
        {
            DocumentTitle = DocumentTitle,
            ReportsFolder = _paths.ReportsFolder,
            WriteReport = Settings.WriteReportAfterApply,
            DefaultMode = Settings.DefaultApplyMode,
            IdentityNote = identityNote
        };

        private async Task ShowReviewAsync(ReviewViewModel review)
        {
            review.Applied += (s, e) =>
            {
                var r = review.Result;
                _log.Info("Apply: " + review.ResultSummary + (string.IsNullOrEmpty(review.ReportPath) ? "" : " Report: " + review.ReportPath));
                Status = review.ResultSummary;
            };
            _dialogs.ShowReview(review);
            await Task.CompletedTask;
        }

        public async Task ImportAsync()
        {
            string path = _dialogs.PickOpenWorkbook(Settings.LastFolder);
            if (string.IsNullOrEmpty(path)) return;
            await ImportFileAsync(path);
        }

        /// <summary>open → validate schema/document → resolve → compare → review (→ apply → report).</summary>
        public async Task<ReviewViewModel> ImportFileAsync(string path)
        {
            XWorkbook wb = null;
            ImportedWorkbook imported = null;
            AffineUnitConverter units = null;
            ChangeSet set = null;
            string identityNote = string.Empty;

            await RunAsync("Reading workbook", async (ct, p) =>
            {
                try { wb = await Task.Run(() => XlsxReader.Load(path), ct); }
                catch (XlsxFormatException ex) { throw new InvalidDataException(ex.Message); }
                catch (IOException ex) { throw new IOException("The workbook cannot be read: " + ex.Message); }
            });
            if (wb == null) return null;

            units = await _gateway.UnitConverter(WorkbookImporter.UnitIds(wb));
            var options = Settings.ToImportOptions();
            await RunAsync("Validating workbook", async (ct, p) => { imported = await Task.Run(() => WorkbookImporter.Read(wb, units, options), ct); });
            if (imported.Fatal)
            {
                _dialogs.Error("Import", string.Join("\n", imported.Issues.Select(i => i.Message)));
                return null;
            }

            var current = await _gateway.CurrentIdentity();
            var id = DocumentIdentityMatcher.Compare(imported.Document, current);
            string reasons = string.Join("\n• ", id.Reasons);
            if (id.Blocked)
            {
                _dialogs.Error("Import – different model",
                    "This workbook was exported from '" + imported.Document.Title + "', not from the active model '" + current.Title + "'.\n\n• " + reasons +
                    "\n\nNothing was compared or written. Open the right model, or export a new workbook from this one.");
                return null;
            }
            if (id.RequiresConfirmation)
            {
                if (!_dialogs.Confirm("Import – confirm the model",
                        "CausaFix could not fully prove that this workbook belongs to the active model.\n\n• " + reasons +
                        "\n\nRows are matched by UniqueId (never by ElementId). Continue only if this is the intended model."))
                    return null;
                identityNote = "Model identity confirmed by the user: " + id.Match + ". " + string.Join(" ", id.Reasons);
            }

            await RunAsync("Comparing with the model", async (ct, p) =>
            {
                var seed = new ChangeSet();
                seed.Issues.AddRange(imported.Issues);
                set = await _gateway.Compare(imported.Cells, seed);
            });
            if (set == null) return null;
            Settings.LastFolder = Path.GetDirectoryName(path);
            SaveSettings();

            if (set.Entries.Count == 0 && set.Issues.All(i => i.Severity == IssueSeverity.Info))
            {
                _dialogs.Info("Import", "No differences: the workbook matches the model.\n" + set.UnchangedCells + " cell(s) unchanged" +
                                        (set.EmptyCellsIgnored > 0 ? ", " + set.EmptyCellsIgnored + " empty cell(s) ignored" : "") + ".");
                Status = "Import: no changes.";
                return null;
            }
            var review = new ReviewViewModel(_gateway, _dialogs, set, units, ReviewContextFor(identityNote), Path.GetFileName(path));
            review.Applied += async (s, e) =>
            {
                if (review.Result != null && review.Result.Committed && Preview.HasData && Preview.PendingCount == 0)
                    await SafeRun(() => Preview.ReloadAsync(force: true));
            };
            await ShowReviewAsync(review);
            return review;
        }

        // ------------------------------------------------------------ selection in Revit

        private async Task RowAction(object parameter, Func<IEnumerable<string>, Task<string>> action)
        {
            var rows = (parameter as System.Collections.IEnumerable)?.OfType<GridRow>().ToList() ?? new List<GridRow>();
            var ids = PreviewViewModel.HostIds(rows);
            if (ids.Count == 0) { Status = "Select host rows in the table first (linked rows cannot be selected)."; return; }
            Status = await action(ids);
        }

        // ------------------------------------------------------------ profiles

        private void ReloadProfiles()
        {
            var errors = new List<string>();
            var names = _profiles.List(errors).Select(p => p.Name).ToList();
            Profiles.Clear();
            foreach (var n in names) Profiles.Add(n);
            if (errors.Count > 0) Status = "Some profiles could not be read: " + string.Join("; ", errors);
        }

        private void SaveProfile()
        {
            if (string.IsNullOrEmpty(SelectedProfile)) { SaveProfileAs(null); return; }
            var p = CurrentSource.CaptureProfile(SelectedProfile);
            _profiles.Save(p);
            Status = "Profile '" + p.Name + "' saved.";
        }

        private void SaveProfileAs(string suggested)
        {
            string name = _dialogs.Prompt("Save profile", "Profile name:", suggested ?? CurrentSource.Title);
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_profiles.Exists(name) && !_dialogs.Confirm("Save profile", "A profile named '" + name + "' exists. Replace it?")) return;
            _profiles.Save(CurrentSource.CaptureProfile(name.Trim()));
            ReloadProfiles();
            SelectedProfile = name.Trim();
            Status = "Profile '" + name + "' saved.";
        }

        private async Task LoadProfileAsync()
        {
            var p = _profiles.Load(SelectedProfile);
            int index = SourceTabs.FindIndex(t => string.Equals(t.ProfileTab, p.Tab, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = 0;
            SelectedTab = index;
            var tab = SourceTabs[index];
            if (!tab.IsLoaded) await tab.RefreshAsync();
            string missing = await tab.ApplyProfileAsync(p);
            if (!string.IsNullOrEmpty(missing)) _dialogs.Info("Profile '" + p.Name + "' – missing references", missing);
            Status = "Profile '" + p.Name + "' applied to " + tab.Title + ".";
        }

        private void DuplicateProfile()
        {
            string name = _dialogs.Prompt("Duplicate profile", "New name:", SelectedProfile + " (copy)");
            if (string.IsNullOrWhiteSpace(name)) return;
            _profiles.Duplicate(SelectedProfile, name.Trim());
            ReloadProfiles();
            SelectedProfile = name.Trim();
        }

        private void RenameProfile()
        {
            string name = _dialogs.Prompt("Rename profile", "New name:", SelectedProfile);
            if (string.IsNullOrWhiteSpace(name)) return;
            _profiles.Rename(SelectedProfile, name.Trim());
            ReloadProfiles();
            SelectedProfile = name.Trim();
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
            string file = _dialogs.PickOpenProfile();
            if (string.IsNullOrEmpty(file)) return;
            var p = ProfileJson.Deserialize(File.ReadAllText(file));
            if (_profiles.Exists(p.Name) && !_dialogs.Confirm("Import profile", "A profile named '" + p.Name + "' exists. Replace it?")) return;
            _profiles.Import(file, overwrite: true);
            ReloadProfiles();
            SelectedProfile = p.Name;
        }

        private void ExportProfile()
        {
            string file = _dialogs.PickSaveProfile(Sanitize(SelectedProfile) + ".cfprofile.json");
            if (string.IsNullOrEmpty(file)) return;
            _profiles.Export(SelectedProfile, file);
            Status = "Profile exported to " + file;
        }

        // ------------------------------------------------------------ settings

        private void EditSettings()
        {
            int dec = _dialogs.Choose("Settings – numbers typed as text in Excel",
                "How should CausaFix read numbers typed as TEXT in Excel cells? (Numeric cells are unaffected.)\nCurrent: " + Settings.DecimalMode,
                new[] { "Auto – reject ambiguous input such as 1,234", "Point is decimal (1,234.5)", "Comma is decimal (1.234,5)" });
            if (dec >= 0) Settings.DecimalMode = (DecimalSeparatorMode)dec;
            int mode = _dialogs.Choose("Settings – apply mode", "Default mode when applying changes. Current: " + Settings.DefaultApplyMode,
                new[] { "All or nothing (recommended)", "Apply valid changes only" });
            if (mode >= 0) Settings.DefaultApplyMode = (Core.Reports.ApplyMode)mode;
            SaveSettings();
            Preview.GridOptions = GridOptions();
            Status = "Settings saved.";
        }

        private void SaveSettings()
        {
            try { _settingsStore.Save(Settings); } catch (Exception ex) { _log.Warn("Settings not saved: " + ex.Message); }
        }
    }
}
