using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CausaFix.Core.Mvvm;
using CausaFix.Keynotes.Editing;
using CausaFix.Keynotes.Execution;
using CausaFix.Keynotes.Files;
using CausaFix.Keynotes.Format;
using CausaFix.Keynotes.Model;

namespace CausaFix.Keynotes.ViewModels
{
    /// <summary>One visible line of the tree (the tree is flattened so every row is hit-testable on its whole width).</summary>
    public sealed class KeynoteRow : ObservableObject
    {
        private bool _isSelected;
        internal KeynoteManagerViewModel Owner;

        public string Key { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string ParentKey { get; set; } = string.Empty;
        public int Depth { get; set; }
        public double IndentWidth => Depth * 18;
        public bool HasChildren { get; set; }
        public int ChildCount { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsRoot => string.IsNullOrEmpty(ParentKey);
        /// <summary>Shown only because a descendant matches the filter.</summary>
        public bool IsContext { get; set; }
        /// <summary>"", "Nueva", "Modificada", "Re-key".</summary>
        public string State { get; set; } = string.Empty;
        public string StateTip { get; set; } = string.Empty;
        public bool HasIssue => !string.IsNullOrEmpty(Issue);
        public string Issue { get; set; } = string.Empty;
        public string Usage { get; set; } = string.Empty;
        public string UsageTip { get; set; } = string.Empty;
        public bool IsUsed { get; set; }
        public bool IsSelected { get => _isSelected; set { if (Set(ref _isSelected, value) && value && Owner != null) Owner.SelectedRow = this; } }
        public string Glyph => HasChildren ? (IsExpanded ? "▾" : "▸") : "";
        public void Toggle() => Owner?.ToggleExpanded(this);
        public override string ToString() => Key + "\t" + Text;
    }

    public enum UsageFilter { All = 0, WithTags, WithoutTags, InActiveView, Changed, WithIssues }
    public enum SourceFilter { Any = 0, User, Element, Material }

    /// <summary>
    /// Keynote Manager. Edits a draft of the keynote file; nothing reaches the file until Save, and nothing reaches the
    /// model until Save &amp; Reload (which writes the file safely and reloads Revit's table in one step, verifying what
    /// Revit loaded). File state and Revit state are shown separately. The model is never saved or synchronised.
    /// </summary>
    public sealed class KeynoteManagerViewModel : ObservableObject
    {
        public const string FileFilter = "Keynote files (*.txt)|*.txt|All files (*.*)|*.*";

        private readonly IKeynoteGateway _gw;
        private readonly IKeynoteDialogs _dlg;
        private readonly KeynoteFileStore _store;
        private readonly Action<string> _log;

        private KeynoteDraft _draft = new KeynoteDraft(new KeynoteLibrary());
        private string _path = "", _baseHash = "";
        private KeynoteEncoding _encoding = KeynoteEncoding.Utf16LeBom;
        private List<string> _preamble = new List<string>();
        private List<UnparsedLine> _unparsed = new List<UnparsedLine>();
        private bool _crlf = true, _deffrel;
        private FileAccessInfo _access = new FileAccessInfo();
        private KeynoteTableInfo _table;
        private UsageReport _usage;
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> _filtered = new HashSet<string>(StringComparer.Ordinal);
        private KeynoteRow _selected;
        private string _selectedKey;
        private bool _busy;
        private string _busyText = "", _status = "", _fileStatus = "", _revitStatus = "", _issuesText = "";
        private string _search = "", _regexInclude = "", _regexExclude = "", _regexError = "", _branchKey = "";
        private UsageFilter _usageFilter;
        private SourceFilter _sourceFilter;
        private List<TextTemplate> _templates;

        public KeynoteManagerViewModel(IKeynoteGateway gateway, IKeynoteDialogs dialogs, KeynoteFileStore store, Action<string> log = null)
        {
            _gw = gateway; _dlg = dialogs; _store = store; _log = log ?? (_ => { });
            Action<Exception> err = ReportError;
            Func<bool> idle = () => !IsBusy;
            Func<bool> editable = () => !IsBusy && CanEdit;
            Func<bool> sel = () => !IsBusy && CanEdit && SelectedRow != null;

            NewFileCommand = new AsyncCommand(NewFileAsync, idle, err);
            OpenFileCommand = new AsyncCommand(OpenFileAsync, idle, err);
            LinkCommand = new AsyncCommand(LinkAsync, () => !IsBusy && HasFile && !IsLinked && !(_table?.DocumentReadOnly ?? true), err);
            SaveCommand = new AsyncCommand(() => SaveAsync(false), () => !IsBusy && HasFile && CanEdit, err);
            SaveReloadCommand = new AsyncCommand(() => SaveAsync(true), () => !IsBusy && HasFile && (CanEdit || IsLinked), err);
            SaveAsCommand = new AsyncCommand(SaveAsAsync, () => !IsBusy && HasFile, err);
            RefreshCommand = new AsyncCommand(RefreshAsync, idle, err);
            OpenFolderCommand = new RelayCommand(() => _dlg.ShowInFolder(_path), () => HasFile);
            ImportCommand = new AsyncCommand(ImportAsync, editable, err);
            ExportAllCommand = new RelayCommand(() => Export(false), () => HasFile);
            ExportFilteredCommand = new RelayCommand(() => Export(true), () => HasFile);
            NewGroupCommand = new RelayCommand(NewGroup, editable);
            NewSubgroupCommand = new RelayCommand(NewSubgroup, sel);
            NewKeynoteCommand = new RelayCommand(NewKeynote, editable);
            EditCommand = new RelayCommand(EditSelected, sel);
            DuplicateCommand = new RelayCommand(DuplicateSelected, sel);
            DeleteCommand = new RelayCommand(DeleteSelected, sel);
            RekeyCommand = new RelayCommand(RekeySelected, sel);
            IndentCommand = new RelayCommand(() => Op(_draft.Indent(SelectedRow.Key)), sel);
            OutdentCommand = new RelayCommand(() => Op(_draft.Outdent(SelectedRow.Key)), sel);
            RevertCommand = new RelayCommand(() => Op(_draft.Revert(SelectedRow.Key)), () => sel() && _draft.ChangeOf(SelectedRow.Key) != null);
            DiscardCommand = new RelayCommand(DiscardAll, () => !IsBusy && IsDirty);
            CopyCommand = new RelayCommand(CopyBranch, () => SelectedRow != null);
            CopyKeyCommand = new RelayCommand(() => _dlg.SetClipboard(SelectedRow.Key), () => SelectedRow != null);
            CopyTextCommand = new RelayCommand(() => _dlg.SetClipboard(SelectedRow.Text), () => SelectedRow != null);
            PasteCommand = new RelayCommand(Paste, editable);
            TransformCommand = new RelayCommand(p => TransformSelected(p as string), _ => sel());
            SaveTemplateCommand = new RelayCommand(SaveTemplate, () => SelectedRow != null);
            ExpandAllCommand = new RelayCommand(() => { foreach (var r in _draft.Current.Records) _expanded.Add(r.Key); Rebuild(); });
            CollapseAllCommand = new RelayCommand(() => { _expanded.Clear(); Rebuild(); });
            BranchFilterCommand = new RelayCommand(() => BranchKey = SelectedRow?.Key ?? "", () => SelectedRow != null);
            ClearFiltersCommand = new RelayCommand(ClearFilters);
            PlaceCommand = new AsyncCommand(p => PlaceAsync(p as string), _ => !IsBusy && SelectedRow != null && !_gw.IsPlacing, err);
            ShowTagsCommand = new AsyncCommand(p => ShowTagsAsync(p), _ => !IsBusy && SelectedRow != null, err);
            _gw.PlacementFinished += OnPlacementFinished;
        }

        // ------------------------------------------------------------------ commands

        public AsyncCommand NewFileCommand { get; }
        public AsyncCommand OpenFileCommand { get; }
        public AsyncCommand LinkCommand { get; }
        public AsyncCommand SaveCommand { get; }
        public AsyncCommand SaveReloadCommand { get; }
        public AsyncCommand SaveAsCommand { get; }
        public AsyncCommand RefreshCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public AsyncCommand ImportCommand { get; }
        public RelayCommand ExportAllCommand { get; }
        public RelayCommand ExportFilteredCommand { get; }
        public RelayCommand NewGroupCommand { get; }
        public RelayCommand NewSubgroupCommand { get; }
        public RelayCommand NewKeynoteCommand { get; }
        public RelayCommand EditCommand { get; }
        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand RekeyCommand { get; }
        public RelayCommand IndentCommand { get; }
        public RelayCommand OutdentCommand { get; }
        public RelayCommand RevertCommand { get; }
        public RelayCommand DiscardCommand { get; }
        public RelayCommand CopyCommand { get; }
        public RelayCommand CopyKeyCommand { get; }
        public RelayCommand CopyTextCommand { get; }
        public RelayCommand PasteCommand { get; }
        public RelayCommand TransformCommand { get; }
        public RelayCommand SaveTemplateCommand { get; }
        public RelayCommand ExpandAllCommand { get; }
        public RelayCommand CollapseAllCommand { get; }
        public RelayCommand BranchFilterCommand { get; }
        public RelayCommand ClearFiltersCommand { get; }
        public AsyncCommand PlaceCommand { get; }
        public AsyncCommand ShowTagsCommand { get; }

        public event EventHandler CloseRequested;

        // ------------------------------------------------------------------ state

        public ObservableCollection<KeynoteRow> Rows { get; } = new ObservableCollection<KeynoteRow>();
        public KeynoteDraft Draft => _draft;
        public string FilePath => _path;
        public bool HasFile => !string.IsNullOrEmpty(_path);
        public string EncodingText => HasFile ? KeynoteFormat.Describe(_encoding) : "";
        public KeynoteTableInfo Table => _table;
        public UsageReport Usage => _usage;
        public bool IsLinked => HasFile && _table != null && SamePath(_table.Path, _path);
        public bool IsDirty => _draft.IsDirty;
        public int ChangeCount => _draft.Changes().Count;
        public string DirtyText => IsDirty ? ChangeCount + " unsaved change(s)" : HasFile ? "No pending changes" : "";
        /// <summary>Editing allowed: a writable, standard (non-DeffrelDB) file.</summary>
        public bool CanEdit => HasFile && _access.CanWrite && !_deffrel;
        public bool IsReadOnly => HasFile && !CanEdit;
        public string ReadOnlyReason => !HasFile ? "" : _deffrel ? "File managed by pyRevit (DeffrelDB): read-only. Use \"Save as…\" to create a copy in Revit's standard format (the original is not touched)." : _access.CanWrite ? "" : "Read-only: " + _access.ReadOnlyReason;
        public string AccessNotes => string.Join(" ", _access.Notes);
        public string FileStatus { get => _fileStatus; private set => Set(ref _fileStatus, value); }
        public string RevitStatus { get => _revitStatus; private set => Set(ref _revitStatus, value); }
        public string Status { get => _status; private set => Set(ref _status, value); }
        public string IssuesText { get => _issuesText; private set => Set(ref _issuesText, value); }
        public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RefreshCommands(); } }
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
        public string Summary => HasFile ? _draft.Current.Records.Count + " records · " + Rows.Count + " visible" : "";
        public List<TextTemplate> Templates => _templates ?? (_templates = _store.LoadTemplates());

        public KeynoteRow SelectedRow
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                if (_selected != null && _selected.IsSelected) { var old = _selected; _selected = null; old.IsSelected = false; }
                _selected = value;
                if (value != null) { _selectedKey = value.Key; if (!value.IsSelected) value.IsSelected = true; }
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedUsageText));
                OnPropertyChanged(nameof(SelectedViews));
                RefreshCommands();
            }
        }

        public string SelectedUsageText
        {
            get
            {
                if (SelectedRow == null) return "";
                if (_usage == null) return "Usage: not read yet.";
                if (!_usage.Succeeded) return "Usage: unknown (the query failed: " + _usage.Error + ").";
                var u = _usage.Of(SelectedRow.Key);
                if (u == null || !u.IsUsed) return "No tags or assignments in this project (projects sharing this file are not visible from here).";
                return u.Tags + " tag(s) [" + u.UserTags + " user, " + u.ElementTags + " element, " + u.MaterialTags + " material] in " + u.Views.Count + " view(s); "
                     + u.ElementAssignments + " type(s)/element(s) and " + u.MaterialAssignments + " material(s) with this key. This project only.";
            }
        }

        public List<ViewRef> SelectedViews => SelectedRow == null ? new List<ViewRef>() : _usage?.Of(SelectedRow.Key)?.Views.ToList() ?? new List<ViewRef>();

        // filters
        public string SearchText { get => _search; set { if (Set(ref _search, value ?? "")) Rebuild(); } }
        public string RegexInclude { get => _regexInclude; set { if (Set(ref _regexInclude, value ?? "")) Rebuild(); } }
        public string RegexExclude { get => _regexExclude; set { if (Set(ref _regexExclude, value ?? "")) Rebuild(); } }
        public string RegexError { get => _regexError; private set => Set(ref _regexError, value); }
        public UsageFilter UsageFilter { get => _usageFilter; set { if (Set(ref _usageFilter, value)) Rebuild(); } }
        public SourceFilter SourceFilter { get => _sourceFilter; set { if (Set(ref _sourceFilter, value)) Rebuild(); } }
        public string BranchKey { get => _branchKey; set { if (Set(ref _branchKey, value ?? "")) { OnPropertyChanged(nameof(BranchText)); Rebuild(); } } }
        public string BranchText => string.IsNullOrEmpty(_branchKey) ? "" : "Branch only: " + _branchKey;
        public bool IsFiltering => _search.Trim().Length > 0 || _regexInclude.Length > 0 || _regexExclude.Length > 0 || _usageFilter != UsageFilter.All || _sourceFilter != SourceFilter.Any || _branchKey.Length > 0;

        private void ClearFilters()
        {
            _search = _regexInclude = _regexExclude = _branchKey = "";
            _usageFilter = UsageFilter.All; _sourceFilter = SourceFilter.Any;
            foreach (var n in new[] { nameof(SearchText), nameof(RegexInclude), nameof(RegexExclude), nameof(UsageFilter), nameof(SourceFilter), nameof(BranchKey), nameof(BranchText) }) OnPropertyChanged(n);
            Rebuild();
        }

        // ------------------------------------------------------------------ start / open

        public async Task InitializeAsync()
        {
            await Busy("Reading the model's keynote table", async () =>
            {
                _table = await _gw.ReadTable();
                if (_table.IsServerResource)
                {
                    FileStatus = "The model's keynote table comes from a server (" + _table.ServerName + "): it is not a file that can be edited here. Open or create a local TXT and link it.";
                }
                else if (!string.IsNullOrEmpty(_table.Path))
                {
                    if (File.Exists(_table.Path)) OpenPath(_table.Path);
                    else FileStatus = "The configured file cannot be found: " + _table.Path + ". It may be on a disconnected drive or not synced.";
                }
                else FileStatus = "The model has no keynote file configured. Create a new one or open an existing one and link it.";
                await ReadUsageAsync();
                UpdateRevitStatus();
                if (string.IsNullOrEmpty(Status)) Status = "Ready. Select a keynote to view or edit its details.";
            });
        }

        private void OpenPath(string path)
        {
            FileSnapshot snap;
            try { snap = _store.Read(path); }
            catch (Exception ex) { _dlg.Error("Open file", "Could not read '" + path + "': " + ex.Message); return; }
            Load(snap);
            var st = _store.LoadDraft(path);
            if (st != null && CanEdit)
            {
                var recovered = new KeynoteLibrary(st.Current);
                bool differs = recovered.Records.Count != snap.Document.Library.Records.Count || recovered.Records.Zip(snap.Document.Library.Records, (a, b) => a.SameAs(b)).Any(x => !x);
                if (differs && _dlg.Confirm("Unsaved changes", "There are unsaved changes to this file from a previous session (" + st.SavedUtc.ToLocalTime().ToString("g") + ")." +
                        (st.BaseHash != snap.Hash ? " The file has changed since then: on save they will be merged with the file's changes and you will be asked to resolve any conflicts." : "") + "\n\nRecover them?"))
                {
                    _draft.ResetTo(new KeynoteLibrary(st.Base));
                    _draft.Replace(recovered, st.Renames.Select(r => new KeyRename { From = r.From, To = r.To }));
                    _baseHash = st.BaseHash;
                    Status = "Changes recovered: " + ChangeCount + ".";
                }
                else _store.DeleteDraft(path);
            }
            Rebuild();
        }

        private void Load(FileSnapshot snap)
        {
            if (_draft != null) _draft.Changed -= OnDraftChanged;
            _path = snap.Path;
            _baseHash = snap.Hash;
            _encoding = snap.Document.Encoding;
            _preamble = snap.Document.Preamble.ToList();
            _unparsed = snap.Document.Unparsed.ToList();
            _crlf = snap.Document.HadCrLf;
            _deffrel = snap.Document.IsDeffrelDb;
            _access = snap.Access;
            _draft = new KeynoteDraft(snap.Document.Library);
            _draft.Changed += OnDraftChanged;
            _expanded.Clear();
            // Small libraries open fully expanded; large ones show the groups only.
            foreach (var r in _draft.Current.Records.Where(r => r.IsRoot || _draft.Current.Records.Count <= 300)) _expanded.Add(r.Key);
            var issues = snap.Document.Issues;
            FileStatus = "File: " + Path.GetFileName(_path) + " · " + snap.Document.Library.Records.Count + " records · " + KeynoteFormat.Describe(_encoding)
                + (issues.Count > 0 ? " · " + issues.Count + " issue(s) in the file" : " · valid")
                + (_deffrel ? " · pyRevit format (DeffrelDB)" : "");
            IssuesText = string.Join("\n", issues.Take(50).Select(i => i.ToString())) + (issues.Count > 50 ? "\n… and " + (issues.Count - 50) + " more." : "");
            NotifyFile();
        }

        private void OnDraftChanged(object sender, EventArgs e)
        {
            if (HasFile && CanEdit)
            {
                if (_draft.IsDirty) _store.SaveDraft(_path, _draft, _baseHash);
                else _store.DeleteDraft(_path);
            }
            Rebuild();
            OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(ChangeCount)); OnPropertyChanged(nameof(DirtyText));
            RefreshCommands();
        }

        private bool ConfirmLeaveDraft()
        {
            if (!_draft.IsDirty) return true;
            return _dlg.Confirm("Unsaved changes", "There are " + ChangeCount + " unsaved change(s) in " + Path.GetFileName(_path) + ". Discard them?");
        }

        private async Task OpenFileAsync()
        {
            if (!ConfirmLeaveDraft()) return;
            string p = _dlg.PickOpenFile("Open keynote file", FileFilter, _path);
            if (string.IsNullOrEmpty(p)) return;
            if (HasFile) _store.DeleteDraft(_path);
            OpenPath(p);
            UpdateRevitStatus();
            if (!IsLinked && HasFile && !_deffrel && _table != null && !_table.DocumentReadOnly
                && _dlg.Confirm("Link to model", "Link '" + Path.GetFileName(p) + "' as the model's keynote file?\n\nRevit will load this file instead of " + (string.IsNullOrEmpty(_table.Path) ? "none" : "'" + _table.Path + "'") + "."))
                await LinkAsync();
        }

        private async Task NewFileAsync()
        {
            if (!ConfirmLeaveDraft()) return;
            string p = _dlg.PickSaveFile("New keynote library", FileFilter, string.IsNullOrEmpty(_path) ? "Keynotes.txt" : Path.Combine(Path.GetDirectoryName(_path) ?? "", "Keynotes.txt"));
            if (string.IsNullOrEmpty(p)) return;
            var res = _store.CreateNew(p, new KeynoteLibrary());
            if (res.Outcome != SaveOutcome.Saved) { _dlg.Error("New file", res.Message); return; }
            if (HasFile) _store.DeleteDraft(_path);
            OpenPath(p);
            Status = "New file created (UTF-16 LE with BOM). Add a group with \"New group\".";
            UpdateRevitStatus();
            if (_table != null && !_table.DocumentReadOnly && _dlg.Confirm("Link to model", "Link the new file as the model's keynote file?"))
                await LinkAsync();
        }

        private async Task LinkAsync()
        {
            if (_draft.IsDirty) { _dlg.Info("Link to model", "Save your changes before linking the file."); return; }
            await Busy("Linking the file to the model", async () =>
            {
                var res = await _gw.LinkFile(_path, new List<KeyRename>());
                AfterReload(res, _draft.Current, null);
                _table = await _gw.ReadTable();
                UpdateRevitStatus();
                NotifyFile();
            });
        }

        private async Task RefreshAsync()
        {
            await Busy("Reloading file and model", async () =>
            {
                _table = await _gw.ReadTable();
                if (HasFile && File.Exists(_path))
                {
                    string now = _store.CurrentHash(_path);
                    if (now != _baseHash)
                    {
                        if (!_draft.IsDirty) { OpenPath(_path); Status = "The file changed on disk: reloaded."; }
                        else Status = "The file changed on disk. Your changes will be merged with the file's changes on save.";
                    }
                    else _access = _store.Probe(_path);
                }
                await ReadUsageAsync();
                UpdateRevitStatus();
                NotifyFile();
            });
        }

        private async Task ReadUsageAsync()
        {
            try { _usage = await _gw.ReadUsage(); }
            catch (Exception ex) { _usage = new UsageReport { Succeeded = false, Error = ex.Message }; }
            if (!_usage.Succeeded) _log("Keynotes: usage query failed: " + _usage.Error);
            Rebuild();
            OnPropertyChanged(nameof(SelectedUsageText)); OnPropertyChanged(nameof(SelectedViews));
        }

        private void UpdateRevitStatus()
        {
            if (_table == null) { RevitStatus = "Revit: not read yet."; return; }
            string loaded = _table.IsLoaded ? _table.LoadedEntries.Count + " notes loaded" : "table not loaded";
            string where = string.IsNullOrEmpty(_table.Path) ? "no file configured" : "configured: " + _table.Path;
            string sync = "";
            if (IsLinked && _table.IsLoaded)
                sync = SameContent(_table.LoadedEntries, _draft.Base) ? " · Matches file" : " · Differs from file (use \"Save & Reload\")";
            else if (HasFile && !IsLinked) sync = " · the open file is NOT the model's file";
            RevitStatus = "Revit: " + where + " · " + _table.LoadStatus + " · " + loaded + sync + (_table.DocumentReadOnly ? " · read-only document: " + _table.DocumentReadOnlyReason : "");
            OnPropertyChanged(nameof(IsLinked));
        }

        // ------------------------------------------------------------------ rows

        internal void ToggleExpanded(KeynoteRow row)
        {
            if (!row.HasChildren) return;
            if (!_expanded.Remove(row.Key)) _expanded.Add(row.Key);
            Rebuild();
        }

        public void Expand(string key, bool expand)
        {
            if (expand) _expanded.Add(key); else _expanded.Remove(key);
            Rebuild();
        }

        private Regex Compile(string pattern, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(pattern)) return null;
            try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
            catch (ArgumentException ex) { error = ex.Message; return null; }
        }

        private bool Matches(KeynoteRecord r, Regex inc, Regex exc)
        {
            string s = _search.Trim();
            if (s.Length > 0 && r.Key.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 && r.Text.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0) return false;
            try
            {
                if (inc != null && !inc.IsMatch(r.Key + "\t" + r.Text)) return false;
                if (exc != null && exc.IsMatch(r.Key + "\t" + r.Text)) return false;
            }
            catch (RegexMatchTimeoutException) { return false; }
            var u = _usage != null && _usage.Succeeded ? _usage.Of(r.Key) : null;
            bool known = _usage != null && _usage.Succeeded;
            switch (_usageFilter)
            {
                case UsageFilter.WithTags: if (!known || u == null || u.Tags == 0) return false; break;
                case UsageFilter.WithoutTags: if (!known || (u != null && u.Tags > 0)) return false; break;
                case UsageFilter.InActiveView: if (!known || u == null || !u.InActiveView) return false; break;
                case UsageFilter.Changed: if (_draft.ChangeOf(r.Key) == null) return false; break;
            }
            if (_sourceFilter != SourceFilter.Any)
            {
                if (!known || u == null) return false;
                int n = _sourceFilter == SourceFilter.User ? u.UserTags : _sourceFilter == SourceFilter.Element ? u.ElementTags + u.ElementAssignments : u.MaterialTags + u.MaterialAssignments;
                if (n == 0) return false;
            }
            return true;
        }

        /// <summary>Rebuilds the flattened tree (natural order; filters keep ancestors as context; expansion state is kept).</summary>
        public void Rebuild()
        {
            var lib = _draft.Current;
            var inc = Compile(_regexInclude, out string e1);
            var exc = Compile(_regexExclude, out string e2);
            RegexError = e1 != null ? "Invalid include regex: " + e1 : e2 != null ? "Invalid exclude regex: " + e2 : "";
            var issues = lib.Validate().Where(i => !string.IsNullOrEmpty(i.Key)).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => string.Join(" ", g.Select(i => i.Message)), StringComparer.Ordinal);
            var changes = _draft.Changes().ToDictionary(c => c.Key, c => c, StringComparer.Ordinal);
            var children = lib.Records.Where(r => !r.IsRoot).GroupBy(r => r.ParentKey).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Key, NaturalComparer.Instance).ToList(), StringComparer.Ordinal);
            var keys = new HashSet<string>(lib.Records.Select(r => r.Key), StringComparer.Ordinal);
            HashSet<string> scope = null;
            if (_branchKey.Length > 0) scope = new HashSet<string>(lib.Branch(_branchKey).Select(r => r.Key), StringComparer.Ordinal);

            bool filtering = IsFiltering;
            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in lib.Records)
            {
                if (scope != null && !scope.Contains(r.Key)) continue;
                if (_usageFilter == UsageFilter.WithIssues ? issues.ContainsKey(r.Key) : Matches(r, inc, exc)) matched.Add(r.Key);
            }
            var context = new HashSet<string>(StringComparer.Ordinal);
            if (filtering) foreach (var k in matched) foreach (var a in lib.Ancestors(k)) if (!matched.Contains(a.Key)) context.Add(a.Key);
            _filtered = filtering ? new HashSet<string>(matched.Concat(context), StringComparer.Ordinal) : new HashSet<string>(keys, StringComparer.Ordinal);

            var rows = new List<KeynoteRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Walk(KeynoteRecord r, int depth)
            {
                if (!seen.Add(r.Key + "\u0001" + depth) || depth > 64) return;
                bool show = !filtering || matched.Contains(r.Key) || context.Contains(r.Key);
                if (!show) return;
                children.TryGetValue(r.Key, out var kids);
                kids = kids ?? new List<KeynoteRecord>();
                bool expanded = filtering || _expanded.Contains(r.Key);
                rows.Add(MakeRow(r, depth, kids.Count, expanded, context.Contains(r.Key), issues, changes));
                if (expanded) foreach (var c in kids) if (c.Key != r.Key) Walk(c, depth + 1);
            }
            var roots = lib.Records.Where(r => r.IsRoot || !keys.Contains(r.ParentKey)).OrderBy(r => r.Key, NaturalComparer.Instance).ToList();
            foreach (var r in roots) Walk(r, 0);
            // Records inside a parent cycle are not reachable from a root: show them flat so they can be fixed.
            var shown = new HashSet<string>(rows.Select(r => r.Key), StringComparer.Ordinal);
            foreach (var r in lib.Records.Where(r => !shown.Contains(r.Key) && issues.ContainsKey(r.Key) && (!filtering || matched.Contains(r.Key))))
                rows.Add(MakeRow(r, 0, 0, false, false, issues, changes));

            Rows.Clear();
            KeynoteRow toSelect = null;
            foreach (var row in rows)
            {
                Rows.Add(row);
                if (row.Key == _selectedKey && toSelect == null) toSelect = row;
            }
            _selected = null;
            if (toSelect != null) { toSelect.IsSelected = true; _selected = toSelect; }
            OnPropertyChanged(nameof(SelectedRow)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(IsFiltering));
            OnPropertyChanged(nameof(SelectedUsageText)); OnPropertyChanged(nameof(SelectedViews));
            RefreshCommands();
        }

        private KeynoteRow MakeRow(KeynoteRecord r, int depth, int kids, bool expanded, bool isContext, Dictionary<string, string> issues, Dictionary<string, RecordChange> changes)
        {
            var row = new KeynoteRow
            {
                Owner = this, Key = r.Key, Text = r.Text, ParentKey = r.ParentKey ?? "", Depth = depth, HasChildren = kids > 0, ChildCount = kids,
                IsExpanded = expanded, IsContext = isContext, Issue = issues.TryGetValue(r.Key, out var iss) ? iss : ""
            };
            if (changes.TryGetValue(r.Key, out var ch))
            {
                row.State = ch.Kind == ChangeKind.Added ? "New" : ch.Kind == ChangeKind.Rekeyed ? "Re-key" : "Modified";
                row.StateTip = ch.Describe();
            }
            if (_usage == null) { row.Usage = ""; row.UsageTip = "Usage not read yet."; }
            else if (!_usage.Succeeded) { row.Usage = "?"; row.UsageTip = "Usage unknown: the query failed (" + _usage.Error + ")."; }
            else
            {
                string orig = _draft.OriginalKeyOf(r.Key);
                var u = _usage.Of(orig);
                if (u != null && u.IsUsed)
                {
                    row.IsUsed = true;
                    var parts = new List<string>();
                    if (u.Tags > 0) parts.Add(u.Tags + (u.Tags == 1 ? " tag" : " tags"));
                    if (u.Views.Count > 0) parts.Add(u.Views.Count + " view" + (u.Views.Count == 1 ? "" : "s"));
                    if (u.Assignments > 0) parts.Add(u.Assignments + " assign.");
                    row.Usage = string.Join(" · ", parts);
                    row.UsageTip = u.UserTags + " user tag(s), " + u.ElementTags + " element, " + u.MaterialTags + " material; " + u.ElementAssignments + " type(s)/element(s) and " + u.MaterialAssignments + " material(s) with this key (this project)." + (orig != r.Key ? " Usage of the original key '" + orig + "'." : "");
                }
            }
            return row;
        }

        // ------------------------------------------------------------------ editing

        private void Op(string error)
        {
            if (error != null) { _dlg.Error("Keynote Manager", error); return; }
            Status = "";
        }

        private List<KeynoteRecord> ParentChoices(string excludeBranchOf = null)
        {
            var excluded = excludeBranchOf == null ? new HashSet<string>() : new HashSet<string>(_draft.Current.Branch(excludeBranchOf).Select(r => r.Key), StringComparer.Ordinal);
            var list = new List<KeynoteRecord> { new KeynoteRecord { Key = "", Text = "(top level: group)" } };
            list.AddRange(Ordered().Where(r => !excluded.Contains(r.Key)));
            return list;
        }

        /// <summary>All records in tree order (natural), for parent pickers and export.</summary>
        private IEnumerable<KeynoteRecord> Ordered()
        {
            var lib = _draft.Current;
            var keys = new HashSet<string>(lib.Records.Select(r => r.Key), StringComparer.Ordinal);
            foreach (var root in lib.Records.Where(r => r.IsRoot || !keys.Contains(r.ParentKey)).OrderBy(r => r.Key, NaturalComparer.Instance))
                foreach (var r in lib.Branch(root.Key)) yield return r;
        }

        private RecordFormResult Form(string title, string key, bool keyEditable, string text, string parent, bool parentEditable, string hint, Func<string, string, string, string> validate, string excludeBranchOf = null)
        {
            return _dlg.EditRecord(new RecordFormRequest
            {
                Title = title, Key = key, KeyEditable = keyEditable, Text = text, ParentKey = parent, ParentEditable = parentEditable,
                ParentChoices = ParentChoices(excludeBranchOf), Templates = Templates.Select(t => t.Text).ToList(), Hint = hint, Validate = validate
            });
        }

        private string SuggestChildKey(string parent)
        {
            var kids = _draft.Current.ChildrenOf(parent).OrderBy(r => r.Key, NaturalComparer.Instance).ToList();
            if (kids.Count > 0) return _draft.SuggestKey(kids.Last().Key);
            return string.IsNullOrEmpty(parent) ? "" : parent + "-001";
        }

        /// <summary>Records created as groups/subgroups in this session count as containers even before they have children.</summary>
        private readonly HashSet<string> _containers = new HashSet<string>(StringComparer.Ordinal);
        private bool IsContainer(KeynoteRow r) => r.HasChildren || r.IsRoot || _containers.Contains(r.Key);

        private void NewGroup()
        {
            var f = Form("New group", "", true, "", "", false, "A group is a top-level keynote (a category in Revit's dialog). Example: key E, text ELECTRICAL.",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "The text cannot be empty." : null));
            if (f == null) return;
            if (_draft.Add(f.Key, f.Text, "") is string e) { Op(e); return; }
            _containers.Add(f.Key.Trim());
            Select(f.Key.Trim());
        }

        private void NewSubgroup()
        {
            string parent = SelectedRow.Key;
            var f = Form("New subgroup in " + parent, SuggestChildKey(parent), true, "", parent, true, "A subgroup is a keynote with a parent that will contain other keynotes. Example: key E-LTG, text LIGHTING, parent E.",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "The text cannot be empty." : null));
            if (f == null) return;
            if (_draft.Add(f.Key, f.Text, f.ParentKey) is string e) { Op(e); return; }
            _containers.Add(f.Key.Trim());
            _expanded.Add(f.ParentKey);
            Select(f.Key.Trim());
        }

        private void NewKeynote()
        {
            string parent = "";
            if (SelectedRow != null) parent = IsContainer(SelectedRow) ? SelectedRow.Key : SelectedRow.ParentKey;
            if (string.IsNullOrEmpty(parent) && !_draft.Current.Records.Any())
            {
                _dlg.Info("New keynote", "The library is empty: create a group first (for example E – ELECTRICAL).");
                return;
            }
            var f = Form("New keynote", SuggestChildKey(parent), true, "", parent, true, "Multi-line pasted text is converted to a single line (preview below).",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "The text cannot be empty." : null) ?? (string.IsNullOrEmpty(p) ? null : _draft.Current.Contains(p) ? null : "The parent does not exist."));
            if (f == null) return;
            Op(_draft.Add(f.Key, f.Text, f.ParentKey));
            if (!string.IsNullOrEmpty(f.ParentKey)) _expanded.Add(f.ParentKey);
            Select(f.Key.Trim());
        }

        private void EditSelected()
        {
            var r = _draft.Current.Find(SelectedRow.Key);
            if (r == null) return;
            var f = Form("Edit " + r.Key, r.Key, false, r.Text, r.ParentKey, true, "Editing does not change the key (use \"Change key…\" for that). Existing tags show the new text after \"Save & Reload\".",
                (k, t, p) => KeynoteText.Normalize(t).Length == 0 ? "The text cannot be empty." : null, r.Key);
            if (f == null) return;
            Op(_draft.Edit(r.Key, f.Text, f.ParentKey));
            Select(r.Key);
        }

        private void DuplicateSelected()
        {
            var r = _draft.Current.Find(SelectedRow.Key);
            if (r == null) return;
            var f = Form("Duplicate " + r.Key, _draft.SuggestKey(r.Key), true, r.Text, r.ParentKey, true, "Copies a single keynote (without its children). To copy a branch use Copy (Ctrl+C) and Paste (Ctrl+V).",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "The text cannot be empty." : null));
            if (f == null) return;
            Op(_draft.Add(f.Key, f.Text, f.ParentKey));
            Select(f.Key.Trim());
        }

        private void RekeySelected()
        {
            string key = SelectedRow.Key;
            string orig = _draft.OriginalKeyOf(key);
            var u = _usage != null && _usage.Succeeded ? _usage.Of(orig) : null;
            string usage = _usage == null || !_usage.Succeeded ? "Usage in the model is unknown: it will be checked on save."
                : u == null || !u.IsUsed ? "There are no tags or assignments with this key in this project."
                : "\"Save & Reload\" will update " + u.Tags + " tag(s) and " + u.Assignments + " assignment(s) in this project. Other projects that use the file keep the old key.";
            string n = _dlg.AskText("Change key", "New key for '" + key + "'. Child keynotes move to the new key.\n" + usage, key);
            if (n == null) return;
            n = n.Trim();
            if (n == key) return;
            Op(_draft.Rekey(key, n));
            if (_expanded.Remove(key)) _expanded.Add(n);
            Select(n);
        }

        private void DeleteSelected()
        {
            string key = SelectedRow.Key;
            var branch = _draft.Current.Branch(key);
            var lines = new List<string>();
            bool unknown = _usage == null || !_usage.Succeeded;
            int used = 0;
            foreach (var r in branch)
            {
                var u = unknown ? null : _usage.Of(_draft.OriginalKeyOf(r.Key));
                string use = unknown ? " — usage unknown" : u != null && u.IsUsed ? " — IN USE: " + u.Tags + " tag(s), " + u.Assignments + " assignment(s)" : "";
                if (u != null && u.IsUsed) used++;
                lines.Add(r.Key + "  " + r.Text + use);
            }
            string intro = branch.Count == 1 ? "This keynote will be deleted from the draft." : "The whole branch will be deleted (" + branch.Count + " keynotes).";
            if (used > 0) intro += "\n\n" + used + " of them are used in this project: their tags will show no text (key not found) and assignments will point to a missing key. Revit does not delete the tags.";
            else if (unknown) intro += "\n\nUsage in the model could not be checked.";
            intro += "\nNothing changes in the file until you save.";
            if (!_dlg.Review("Delete " + key, intro, lines, used > 0 ? "Delete anyway" : "Delete", danger: used > 0 || unknown)) return;
            Op(_draft.Delete(key, true));
        }

        private void DiscardAll()
        {
            if (!_dlg.Confirm("Discard changes", "Discard the " + ChangeCount + " unsaved change(s) and go back to the file?")) return;
            _draft.ResetTo(_draft.Base);
            _store.DeleteDraft(_path);
            Status = "Changes discarded.";
        }

        private void TransformSelected(string mode)
        {
            var keys = new[] { SelectedRow.Key };
            Func<string, string> f;
            switch (mode)
            {
                case "upper": f = KeynoteText.ToUpper; break;
                case "lower": f = KeynoteText.ToLower; break;
                case "title": f = KeynoteText.ToTitle; break;
                case "sentence": f = KeynoteText.ToSentence; break;
                case "upper-branch": f = KeynoteText.ToUpper; keys = _draft.Current.Branch(SelectedRow.Key).Select(r => r.Key).ToArray(); break;
                default: return;
            }
            var ch = _draft.Transform(keys, f);
            Status = ch.Count == 0 ? "The text was already like that." : ch.Count + " text(s) changed.";
        }

        private void SaveTemplate()
        {
            string name = _dlg.AskText("Save text template", "Template name (text: '" + SelectedRow.Text + "'):", SelectedRow.Text.Length > 30 ? SelectedRow.Text.Substring(0, 30) : SelectedRow.Text);
            if (string.IsNullOrWhiteSpace(name)) return;
            var list = Templates.Where(t => !t.BuiltIn && t.Name != name).ToList();
            list.Add(new TextTemplate { Name = name.Trim(), Text = SelectedRow.Text });
            try { _store.SaveTemplates(list); _templates = null; Status = "Template saved."; }
            catch (Exception ex) { _dlg.Error("Templates", "Could not save: " + ex.Message); }
        }

        public void Select(string key)
        {
            _selectedKey = key;
            foreach (var a in _draft.Current.Ancestors(key)) _expanded.Add(a.Key);
            Rebuild();
        }

        // ------------------------------------------------------------------ drag & drop

        /// <summary>Moves <paramref name="key"/> under <paramref name="newParent"/> ("" = first level). Keys never change.</summary>
        public string Move(string key, string newParent)
        {
            if (!CanEdit) return "The file is read-only.";
            var r = _draft.Current.Find(key);
            if (r == null) return "The keynote does not exist.";
            if ((r.ParentKey ?? "") == (newParent ?? "")) return null;
            string e = _draft.SetParent(key, newParent ?? "");
            if (e == null) { if (!string.IsNullOrEmpty(newParent)) _expanded.Add(newParent); Select(key); }
            else _dlg.Error("Move", e);
            return e;
        }

        // ------------------------------------------------------------------ clipboard

        public static string ToClipboardText(IEnumerable<KeynoteRecord> records) => KeynoteFormat.Compose(new KeynoteLibrary(records));

        public static List<KeynoteRecord> FromClipboardText(string text)
        {
            var list = new List<KeynoteRecord>();
            foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Trim().Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var f = line.Split('\t');
                if (f.Length < 2 || f.Length > 3) return null;
                list.Add(new KeynoteRecord { Key = f[0], Text = f[1], ParentKey = f.Length == 3 ? f[2] : "" });
            }
            return list.Count == 0 ? null : list;
        }

        private void CopyBranch()
        {
            var branch = _draft.Current.Branch(SelectedRow.Key);
            _dlg.SetClipboard(ToClipboardText(branch));
            Status = "Copied branch " + SelectedRow.Key + " (" + branch.Count + " keynote(s)) in keynote format.";
        }

        private void Paste()
        {
            var recs = FromClipboardText(_dlg.GetClipboard());
            if (recs == null) { _dlg.Info("Paste", "The clipboard does not contain keynotes in \"key TAB text [TAB parent]\" format. To paste text into a keynote, edit it (F2)."); return; }
            string target = SelectedRow?.Key ?? "";
            var plan = KeynoteImport.PlanPaste(_draft, recs, target);
            if (!_dlg.ReviewIncoming("Paste " + plan.Count + " keynote(s)", "They will be pasted into " + (target.Length == 0 ? "the top level" : "'" + target + "'") + ". Keys that already exist get a suggested new key (you can change it). Nothing is saved until \"Save\".", plan, true)) return;
            string e = KeynoteImport.ApplyPaste(_draft, plan);
            if (e != null) { _dlg.Error("Paste", e); return; }
            if (target.Length > 0) _expanded.Add(target);
            Status = plan.Count(i => i.Take) + " keynote(s) pasted.";
            Rebuild();
        }

        // ------------------------------------------------------------------ import / export

        private async Task ImportAsync()
        {
            string p = _dlg.PickOpenFile("Import keynotes (Revit TXT or pyRevit export)", FileFilter, _path);
            if (string.IsNullOrEmpty(p)) return;
            KeynoteDocument doc;
            try { doc = await Task.Run(() => KeynoteFormat.Read(File.ReadAllBytes(p))); }
            catch (Exception ex) { _dlg.Error("Import", "Could not read: " + ex.Message); return; }
            var plan = KeynoteImport.PlanImport(_draft, doc.Library);
            int nNew = plan.Count(i => i.Status == IncomingStatus.New), nSame = plan.Count(i => i.Status == IncomingStatus.Identical), nConf = plan.Count(i => i.Status == IncomingStatus.Conflict);
            string intro = Path.GetFileName(p) + " (" + KeynoteFormat.Describe(doc.Encoding) + (doc.IsDeffrelDb ? ", pyRevit DeffrelDB" : "") + "): " + nNew + " new, " + nSame + " identical, " + nConf + " in conflict."
                + (nConf > 0 ? " For conflicts your version is kept unless you select the imported one." : "")
                + (doc.Unparsed.Count > 0 ? "\n" + doc.Unparsed.Count + " malformed line(s) are not imported: " + string.Join("; ", doc.Unparsed.Take(3).Select(u => "line " + u.Line + " " + u.Reason)) : "");
            if (!_dlg.ReviewIncoming("Import keynotes", intro, plan, false)) return;
            string e = KeynoteImport.ApplyImport(_draft, plan);
            if (e != null) { _dlg.Error("Import", e); return; }
            Status = "Imported " + plan.Count(i => i.Take) + " keynote(s). Review and save.";
        }

        private void Export(bool filtered)
        {
            var recs = Ordered().Where(r => !filtered || _filtered.Contains(r.Key)).ToList();
            if (recs.Count == 0) { _dlg.Info("Export", "There are no keynotes to export with the current filter."); return; }
            string p = _dlg.PickSaveFile(filtered ? "Export filtered keynotes (with their parents)" : "Export all keynotes", FileFilter, Path.Combine(Path.GetDirectoryName(_path) ?? "", Path.GetFileNameWithoutExtension(_path) + (filtered ? "_filtered" : "_export") + ".txt"));
            if (string.IsNullOrEmpty(p)) return;
            if (SamePath(p, _path)) { _dlg.Error("Export", "Choose a different file from the one you are editing (use \"Save as…\" for that)."); return; }
            try
            {
                File.WriteAllBytes(p, KeynoteFormat.Encode(KeynoteFormat.Compose(new KeynoteLibrary(recs)), KeynoteEncoding.Utf16LeBom, out _));
                Status = "Exported " + recs.Count + " keynote(s) to " + p + " (UTF-16 LE).";
            }
            catch (Exception ex) { _dlg.Error("Export", ex.Message); }
        }

        // ------------------------------------------------------------------ save & reload

        private byte[] Compose(KeynoteLibrary lib, bool keepUnparsed, out KeynoteEncoding used)
            => KeynoteFormat.Encode(KeynoteFormat.Compose(lib, _preamble, keepUnparsed ? _unparsed : null, _crlf), _encoding, out used);

        /// <summary>
        /// Save (and optionally reload Revit): validate → review risky changes → check model references for re-keys →
        /// write with concurrency check (merge on conflict) → reload the table and update references in one undo step →
        /// verify what Revit loaded. A re-key always reloads (the file and the model must change together).
        /// </summary>
        public async Task<bool> SaveAsync(bool reload)
        {
            if (!HasFile) return false;
            if (!CanEdit)
            {
                if (reload && IsLinked && !_draft.IsDirty) return await ReloadOnlyAsync();
                _dlg.Error("Save", ReadOnlyReason);
                return false;
            }
            if (!_draft.IsDirty && !reload) { Status = "There are no changes to save."; return true; }
            if (!_draft.IsDirty && reload) return await ReloadOnlyAsync();

            var issues = _draft.Current.Validate();
            if (issues.Count > 0)
            {
                _dlg.Review("Cannot save", "Fix these issues before saving (the file has not been changed):", issues.Select(i => i.Message).ToList(), "OK");
                return false;
            }
            var renames = _draft.Renames.Select(r => new KeyRename { From = r.From, To = r.To }).ToList();
            if (renames.Count > 0 && !IsLinked)
            {
                _dlg.Error("Change key", "There are key changes and the open file is not the model's file: the model's references could not be updated. Link the file or revert the key changes.");
                return false;
            }
            if (renames.Count > 0) reload = true;
            if (reload && !IsLinked)
            {
                if (!_dlg.Confirm("Save & Reload", "The open file is not the model's file. Save without reloading Revit?")) return false;
                reload = false;
            }

            bool ok = false;
            await Busy("Saving", async () =>
            {
                // Risky changes are reviewed first: deletions, re-keys, model references.
                var changes = _draft.Changes();
                var risky = changes.Where(c => c.Kind == ChangeKind.Removed || c.Kind == ChangeKind.Rekeyed).ToList();
                RekeyCheck check = null;
                if (renames.Count > 0)
                {
                    check = await _gw.CheckRekey(renames);
                    if (!check.CanApply)
                    {
                        _dlg.Review("Key change blocked", "Nothing was saved. The model's references cannot be updated consistently:", check.Blockers, "OK");
                        return;
                    }
                }
                if (risky.Count > 0)
                {
                    var lines = risky.Select(c => c.Describe()).ToList();
                    if (check != null) lines.AddRange(check.Lines);
                    if (!_dlg.Review("Review before saving", "These changes affect existing keynotes" + (reload ? " and will also be applied to the model in a single step (one Undo)." : "."), lines, reload ? "Save & Reload" : "Save", danger: true)) return;
                }
                bool keepUnparsed = true;
                if (_unparsed.Count > 0)
                    keepUnparsed = !_dlg.Confirm("Malformed lines", _unparsed.Count + " line(s) in the file are not valid records (for example line " + _unparsed[0].Line + ": " + _unparsed[0].Reason + ").\n\nRemove them on save? (Cancel = keep them as they are; Revit will report them on reload.)");

                byte[] previous = File.Exists(_path) ? File.ReadAllBytes(_path) : new byte[0];
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var lib = _draft.Current.Clone();
                    byte[] bytes = Compose(lib, keepUnparsed, out var used);
                    var res = _store.Save(_path, bytes, _baseHash);
                    if (res.Outcome == SaveOutcome.Failed) { _dlg.Error("Save", res.Message + "\n\nYour changes are still in the draft (also saved locally)."); FileStatus = "File: not saved — " + res.Message; return; }
                    if (res.Outcome == SaveOutcome.Conflict)
                    {
                        if (res.Theirs == null) { _dlg.Error("Save", res.Message + " Use \"Save as…\"."); return; }
                        if (!MergeWith(res.Theirs)) { Status = "Save cancelled: the file changed and the conflicts were not resolved."; return; }
                        renames = _draft.Renames.Select(r => new KeyRename { From = r.From, To = r.To }).ToList();
                        continue;
                    }
                    // Saved.
                    if (used != _encoding)
                    {
                        _dlg.Info("Encoding", "The text contains characters that " + KeynoteFormat.Describe(_encoding) + " cannot store: the file was saved as " + KeynoteFormat.Describe(used) + ".");
                        _encoding = used;
                    }
                    if (!keepUnparsed) _unparsed.Clear();
                    _baseHash = res.WrittenHash;
                    _draft.MarkSaved(lib);
                    _store.DeleteDraft(_path);
                    _access = _store.Probe(_path);
                    FileStatus = "File saved at " + DateTime.Now.ToString("HH:mm:ss") + " · " + lib.Records.Count + " records · " + KeynoteFormat.Describe(_encoding) + " · Backup: " + Path.GetFileName(res.BackupPath);
                    if (reload)
                    {
                        BusyText = "Reloading the keynote table in Revit";
                        ReloadResult rr;
                        try { rr = await _gw.Reload(renames); }
                        catch (Exception ex) { rr = new ReloadResult { Succeeded = false, Status = "Error", Path = _path }; rr.Messages.Add(ex.Message); }
                        AfterReload(rr, lib, new RestoreInfo { Previous = previous, WrittenHash = res.WrittenHash, Draft = lib, Renames = renames });
                        _table = await SafeReadTable();
                        await ReadUsageAsync();
                    }
                    else Status = "Saved. Revit keeps using the previous version until \"Save & Reload\".";
                    UpdateRevitStatus();
                    NotifyFile();
                    ok = true;
                    return;
                }
                _dlg.Error("Save", "The file keeps changing while saving (active sync?). Try again.");
            });
            return ok;
        }

        private sealed class RestoreInfo
        {
            public byte[] Previous;
            public string WrittenHash;
            public KeynoteLibrary Draft;
            public List<KeyRename> Renames;
        }

        private async Task<KeynoteTableInfo> SafeReadTable()
        {
            try { return await _gw.ReadTable(); } catch (Exception ex) { _log("Keynotes: ReadTable failed " + ex.Message); return _table; }
        }

        private async Task<bool> ReloadOnlyAsync()
        {
            bool ok = false;
            await Busy("Reloading the keynote table in Revit", async () =>
            {
                var rr = await _gw.Reload(new List<KeyRename>());
                ok = rr.Succeeded;
                AfterReload(rr, _draft.Base, null);
                _table = await SafeReadTable();
                await ReadUsageAsync();
                UpdateRevitStatus();
            });
            return ok;
        }

        private void AfterReload(ReloadResult rr, KeynoteLibrary expected, RestoreInfo restore)
        {
            if (rr.Succeeded)
            {
                bool same = SameContent(rr.LoadedEntries, expected);
                string refs = rr.UpdatedTags + rr.UpdatedAssignments > 0 ? " References updated: " + rr.UpdatedTags + " tag(s), " + rr.UpdatedAssignments + " assignment(s)." : "";
                Status = same ? "Saved and reloaded: Revit loaded " + rr.LoadedEntries.Count + " notes, verified against the file." + refs
                              : "Revit reloaded, but what it loaded does not match the file (" + rr.LoadedEntries.Count + " notes in Revit, " + expected.Records.Count + " in the file). Check the warnings." + refs;
                if (rr.Messages.Count > 0) _dlg.Review("Revit warnings on reload", "Revit loaded the table with these warnings:", rr.Messages, "OK");
                return;
            }
            _log("Keynotes: reload failed " + rr.Status + " " + string.Join(" | ", rr.Messages));
            string msg = "Revit could not reload the file (" + rr.Status + "). The model did not change (operation rolled back).";
            if (restore == null) { _dlg.Review("Reload failed", msg, rr.Messages, "OK"); Status = msg; return; }
            var lines = rr.Messages.ToList();
            if (_dlg.Review("Reload failed", msg + "\n\nThe file WAS saved with your changes. Restore the previous file? (only if nobody changed it since it was saved; your changes will be pending in the draft again)", lines, "Restore previous file"))
            {
                var r = _store.RestoreIfUnchanged(_path, restore.WrittenHash, restore.Previous);
                if (r.Outcome == SaveOutcome.Saved)
                {
                    var prev = KeynoteFormat.Read(restore.Previous);
                    _baseHash = r.WrittenHash;
                    _draft.ResetTo(prev.Library);
                    _draft.Replace(restore.Draft, restore.Renames);
                    _store.SaveDraft(_path, _draft, _baseHash);
                    Status = "File restored; your changes are still pending in the draft.";
                }
                else _dlg.Error("Restore", r.Message);
            }
            else Status = msg + " The file was saved with your changes.";
        }

        /// <summary>Three-way merge with the file as it is now. Returns false if the user cancelled.</summary>
        private bool MergeWith(FileSnapshot theirs)
        {
            var m = KeynoteMerge.Merge(_draft.Base, _draft.Current, theirs.Document.Library, _draft.Renames);
            if (m.Conflicts.Count > 0 && !_dlg.ResolveConflicts(m)) return false;
            var merged = KeynoteMerge.Resolve(m, out string err);
            if (merged == null || err != null) { _dlg.Error("Merge", err ?? "The changes could not be merged."); return false; }
            if (m.TheirChanges.Count > 0 && !_dlg.Review("The file changed", "Another user or process changed the file since you opened it. Their changes are kept along with yours:", m.TheirChanges, "Continue saving")) return false;
            _draft.ResetTo(theirs.Document.Library);
            _draft.Replace(merged, m.Renames);
            _baseHash = theirs.Hash;
            _preamble = theirs.Document.Preamble.ToList();
            _unparsed = theirs.Document.Unparsed.ToList();
            _encoding = theirs.Document.Encoding;
            return true;
        }

        private async Task SaveAsAsync()
        {
            string p = _dlg.PickSaveFile("Save as", FileFilter, _path);
            if (string.IsNullOrEmpty(p)) return;
            if (SamePath(p, _path)) { await SaveAsync(false); return; }
            if (_draft.Renames.Count > 0) { _dlg.Error("Save as", "There are pending key changes: they are applied to the model only with \"Save & Reload\" of the linked file. Save first or revert the key change."); return; }
            var issues = _draft.Current.Validate();
            if (issues.Count > 0) { _dlg.Review("Cannot save", "Fix these issues:", issues.Select(i => i.Message).ToList(), "OK"); return; }
            string expected = "";
            if (File.Exists(p))
            {
                if (!_dlg.Confirm("Save as", "'" + p + "' already exists. Replace it? (a backup is kept)")) return;
                expected = _store.CurrentHash(p);
            }
            bool fromDeffrel = _deffrel;
            if (fromDeffrel) { _preamble.Clear(); _unparsed.Clear(); _encoding = KeynoteEncoding.Utf16LeBom; }
            var lib = _draft.Current.Clone();
            var res = _store.Save(p, Compose(lib, true, out var used), expected);
            if (res.Outcome != SaveOutcome.Saved) { _dlg.Error("Save as", res.Message); return; }
            if (HasFile) _store.DeleteDraft(_path);
            OpenPath(p);
            Status = "Saved as " + p + (fromDeffrel ? " (copy in standard format; the pyRevit file was not changed)" : "") + ".";
            UpdateRevitStatus();
            if (!IsLinked && _table != null && !_table.DocumentReadOnly && _dlg.Confirm("Link to model", "Link '" + Path.GetFileName(p) + "' as the model's keynote file?"))
                await LinkAsync();
        }

        // ------------------------------------------------------------------ placement / usage

        private async Task PlaceAsync(string mode)
        {
            var m = mode == "element" ? PlacementMode.Element : mode == "material" ? PlacementMode.Material : PlacementMode.User;
            string key = SelectedRow.Key;
            if (_table == null || !_table.HasKeynoteTagType) { _dlg.Error("Place keynote", "The model has no keynote tag type loaded. Load a Keynote Tag family."); return; }
            if (!_table.ActiveViewAcceptsTags) { _dlg.Error("Place keynote", "The active view (" + _table.ActiveViewName + ") does not accept keynote tags. Open a plan, elevation, section, detail or legend view."); return; }
            bool inRevit = IsLinked && _table.IsLoaded && _table.LoadedEntries.Any(e => e.Key == key);
            bool pending = _draft.ChangeOf(key) != null || _draft.IsDirty && !inRevit;
            if (!inRevit || pending)
            {
                if (!IsLinked) { _dlg.Error("Place keynote", "The open file is not the model's file: Revit does not know these keynotes. Link it first."); return; }
                if (!_dlg.Confirm("Place keynote", "'" + key + "' " + (inRevit ? "has changes that are not reloaded" : "is not yet in the table loaded in Revit") + ". Save & Reload now?")) return;
                if (!await SaveAsync(true)) return;
                _table = await SafeReadTable();
                if (!_table.LoadedEntries.Any(e => e.Key == key)) { _dlg.Error("Place keynote", "'" + key + "' is not in the table reloaded by Revit."); return; }
            }
            string msg = await _gw.StartPlacement(m, key);
            Status = msg;
            RefreshCommands();
        }

        private async void OnPlacementFinished(object sender, PlacementResult r)
        {
            try
            {
                Status = r.Message;
                RefreshCommands();
                await ReadUsageAsync();
            }
            catch (Exception ex) { _log("Keynotes: after placement " + ex.Message); }
        }

        private async Task ShowTagsAsync(object p)
        {
            long viewId = p is ViewRef v ? v.Id : 0;
            string key = _draft.OriginalKeyOf(SelectedRow.Key);
            Status = await _gw.ShowTags(key, viewId);
        }

        // ------------------------------------------------------------------ close

        /// <summary>Called when the window is closing. False keeps it open.</summary>
        public async Task<bool> ConfirmCloseAsync()
        {
            if (_gw.IsPlacing) _gw.CancelPlacement();
            if (!_draft.IsDirty) return true;
            switch (_dlg.AskSaveDiscardCancel("There are " + ChangeCount + " unsaved change(s) in " + Path.GetFileName(_path) + "."))
            {
                case CloseChoice.Save: return await SaveAsync(IsLinked);
                case CloseChoice.Discard: _store.DeleteDraft(_path); _draft.ResetTo(_draft.Base); return true;
                default: return false;
            }
        }

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public void Detach() => _gw.PlacementFinished -= OnPlacementFinished;

        // ------------------------------------------------------------------ helpers

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string Norm(string p) { try { p = Path.GetFullPath(p); } catch { } return p.Replace('/', '\\').TrimEnd('\\'); }
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool SameContent(IList<KeynoteRecord> loaded, KeynoteLibrary lib)
        {
            if (loaded == null || loaded.Count != lib.Records.Count) return false;
            var map = lib.Records.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return loaded.All(e => map.TryGetValue(e.Key, out var r) && r.Text == e.Text && (r.ParentKey ?? "") == (e.ParentKey ?? ""));
        }

        private void NotifyFile()
        {
            foreach (var n in new[] { nameof(FilePath), nameof(HasFile), nameof(EncodingText), nameof(IsLinked), nameof(CanEdit), nameof(IsReadOnly), nameof(ReadOnlyReason), nameof(AccessNotes), nameof(IsDirty), nameof(ChangeCount), nameof(DirtyText), nameof(Table), nameof(Summary) })
                OnPropertyChanged(n);
            RefreshCommands();
        }

        private async Task Busy(string text, Func<Task> body)
        {
            BusyText = text; IsBusy = true;
            try { await body(); }
            catch (Exception ex) { ReportError(ex); }
            finally { IsBusy = false; BusyText = ""; RefreshCommands(); }
        }

        private void ReportError(Exception ex)
        {
            _log("Keynotes error: " + ex);
            _dlg.Error("Keynote Manager", ex.Message);
        }

        public void RefreshCommands()
        {
            foreach (var c in new object[] { NewFileCommand, OpenFileCommand, LinkCommand, SaveCommand, SaveReloadCommand, SaveAsCommand, RefreshCommand, ImportCommand, PlaceCommand, ShowTagsCommand })
                (c as AsyncCommand)?.Refresh();
            foreach (var c in new[] { OpenFolderCommand, ExportAllCommand, ExportFilteredCommand, NewGroupCommand, NewSubgroupCommand, NewKeynoteCommand, EditCommand, DuplicateCommand, DeleteCommand, RekeyCommand, IndentCommand, OutdentCommand, RevertCommand, DiscardCommand, CopyCommand, CopyKeyCommand, CopyTextCommand, PasteCommand, TransformCommand, SaveTemplateCommand, BranchFilterCommand })
                c?.Refresh();
        }
    }
}
