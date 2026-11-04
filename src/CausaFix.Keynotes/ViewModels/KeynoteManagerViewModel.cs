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
        public const string FileFilter = "Archivos de keynotes (*.txt)|*.txt|Todos los archivos (*.*)|*.*";

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
        public string DirtyText => IsDirty ? ChangeCount + " cambio(s) sin guardar" : HasFile ? "Sin cambios pendientes" : "";
        /// <summary>Editing allowed: a writable, standard (non-DeffrelDB) file.</summary>
        public bool CanEdit => HasFile && _access.CanWrite && !_deffrel;
        public bool IsReadOnly => HasFile && !CanEdit;
        public string ReadOnlyReason => !HasFile ? "" : _deffrel ? "Archivo administrado por pyRevit (DeffrelDB): solo lectura. Usa «Guardar como…» para crear una copia en formato estándar de Revit (el original no se toca)." : _access.CanWrite ? "" : "Solo lectura: " + _access.ReadOnlyReason;
        public string AccessNotes => string.Join(" ", _access.Notes);
        public string FileStatus { get => _fileStatus; private set => Set(ref _fileStatus, value); }
        public string RevitStatus { get => _revitStatus; private set => Set(ref _revitStatus, value); }
        public string Status { get => _status; private set => Set(ref _status, value); }
        public string IssuesText { get => _issuesText; private set => Set(ref _issuesText, value); }
        public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RefreshCommands(); } }
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
        public string Summary => HasFile ? _draft.Current.Records.Count + " registros · " + Rows.Count + " visibles" : "";
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
                if (_usage == null) return "Uso: sin leer.";
                if (!_usage.Succeeded) return "Uso: desconocido (la consulta falló: " + _usage.Error + ").";
                var u = _usage.Of(SelectedRow.Key);
                if (u == null || !u.IsUsed) return "Sin etiquetas ni asignaciones en este proyecto (otros proyectos que compartan el archivo no se ven desde aquí).";
                return u.Tags + " etiqueta(s) [" + u.UserTags + " usuario, " + u.ElementTags + " elemento, " + u.MaterialTags + " material] en " + u.Views.Count + " vista(s); "
                     + u.ElementAssignments + " tipo(s)/elemento(s) y " + u.MaterialAssignments + " material(es) con esta clave. Solo este proyecto.";
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
        public string BranchText => string.IsNullOrEmpty(_branchKey) ? "" : "Solo la rama " + _branchKey;
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
            await Busy("Leyendo la tabla de keynotes del modelo", async () =>
            {
                _table = await _gw.ReadTable();
                if (_table.IsServerResource)
                {
                    FileStatus = "La tabla del modelo viene de un servidor (" + _table.ServerName + "): no es un archivo editable desde aquí. Abre o crea un TXT local y vincúlalo.";
                }
                else if (!string.IsNullOrEmpty(_table.Path))
                {
                    if (File.Exists(_table.Path)) OpenPath(_table.Path);
                    else FileStatus = "El archivo configurado no se encuentra: " + _table.Path + ". Puede estar en una unidad no conectada o sin sincronizar.";
                }
                else FileStatus = "El modelo no tiene archivo de keynotes configurado. Crea uno nuevo o abre uno existente y vincúlalo.";
                await ReadUsageAsync();
                UpdateRevitStatus();
            });
        }

        private void OpenPath(string path)
        {
            FileSnapshot snap;
            try { snap = _store.Read(path); }
            catch (Exception ex) { _dlg.Error("Abrir archivo", "No se pudo leer '" + path + "': " + ex.Message); return; }
            Load(snap);
            var st = _store.LoadDraft(path);
            if (st != null && CanEdit)
            {
                var recovered = new KeynoteLibrary(st.Current);
                bool differs = recovered.Records.Count != snap.Document.Library.Records.Count || recovered.Records.Zip(snap.Document.Library.Records, (a, b) => a.SameAs(b)).Any(x => !x);
                if (differs && _dlg.Confirm("Cambios sin guardar", "Hay cambios sin guardar de una sesión anterior en este archivo (" + st.SavedUtc.ToLocalTime().ToString("g") + ")." +
                        (st.BaseHash != snap.Hash ? " El archivo cambió desde entonces: al guardar se combinarán con los cambios del archivo y se te pedirá resolver los conflictos." : "") + "\n\n¿Recuperarlos?"))
                {
                    _draft.ResetTo(new KeynoteLibrary(st.Base));
                    _draft.Replace(recovered, st.Renames.Select(r => new KeyRename { From = r.From, To = r.To }));
                    _baseHash = st.BaseHash;
                    Status = "Cambios recuperados: " + ChangeCount + ".";
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
            FileStatus = "Archivo: " + Path.GetFileName(_path) + " · " + snap.Document.Library.Records.Count + " registros · " + KeynoteFormat.Describe(_encoding)
                + (issues.Count > 0 ? " · " + issues.Count + " problema(s) en el archivo" : " · válido")
                + (_deffrel ? " · formato pyRevit (DeffrelDB)" : "");
            IssuesText = string.Join("\n", issues.Take(50).Select(i => i.ToString())) + (issues.Count > 50 ? "\n… y " + (issues.Count - 50) + " más." : "");
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
            return _dlg.Confirm("Cambios sin guardar", "Hay " + ChangeCount + " cambio(s) sin guardar en " + Path.GetFileName(_path) + ". ¿Descartarlos?");
        }

        private async Task OpenFileAsync()
        {
            if (!ConfirmLeaveDraft()) return;
            string p = _dlg.PickOpenFile("Abrir archivo de keynotes", FileFilter, _path);
            if (string.IsNullOrEmpty(p)) return;
            if (HasFile) _store.DeleteDraft(_path);
            OpenPath(p);
            UpdateRevitStatus();
            if (!IsLinked && HasFile && !_deffrel && _table != null && !_table.DocumentReadOnly
                && _dlg.Confirm("Vincular al modelo", "¿Vincular '" + Path.GetFileName(p) + "' como archivo de keynotes del modelo?\n\nRevit cargará este archivo en lugar de " + (string.IsNullOrEmpty(_table.Path) ? "ninguno" : "'" + _table.Path + "'") + "."))
                await LinkAsync();
        }

        private async Task NewFileAsync()
        {
            if (!ConfirmLeaveDraft()) return;
            string p = _dlg.PickSaveFile("Nueva biblioteca de keynotes", FileFilter, string.IsNullOrEmpty(_path) ? "Keynotes.txt" : Path.Combine(Path.GetDirectoryName(_path) ?? "", "Keynotes.txt"));
            if (string.IsNullOrEmpty(p)) return;
            var res = _store.CreateNew(p, new KeynoteLibrary());
            if (res.Outcome != SaveOutcome.Saved) { _dlg.Error("Nuevo archivo", res.Message); return; }
            if (HasFile) _store.DeleteDraft(_path);
            OpenPath(p);
            Status = "Archivo nuevo creado (UTF-16 LE con BOM). Añade un grupo con «Nuevo grupo».";
            UpdateRevitStatus();
            if (_table != null && !_table.DocumentReadOnly && _dlg.Confirm("Vincular al modelo", "¿Vincular el archivo nuevo como archivo de keynotes del modelo?"))
                await LinkAsync();
        }

        private async Task LinkAsync()
        {
            if (_draft.IsDirty) { _dlg.Info("Vincular", "Guarda los cambios antes de vincular el archivo."); return; }
            await Busy("Vinculando el archivo al modelo", async () =>
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
            await Busy("Releyendo archivo y modelo", async () =>
            {
                _table = await _gw.ReadTable();
                if (HasFile && File.Exists(_path))
                {
                    string now = _store.CurrentHash(_path);
                    if (now != _baseHash)
                    {
                        if (!_draft.IsDirty) { OpenPath(_path); Status = "El archivo cambió en disco: releído."; }
                        else Status = "El archivo cambió en disco. Tus cambios se combinarán con los del archivo al guardar.";
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
            if (_table == null) { RevitStatus = "Revit: sin leer."; return; }
            string loaded = _table.IsLoaded ? _table.LoadedEntries.Count + " notas cargadas" : "tabla no cargada";
            string where = string.IsNullOrEmpty(_table.Path) ? "sin archivo configurado" : "configurado: " + _table.Path;
            string sync = "";
            if (IsLinked && _table.IsLoaded)
                sync = SameContent(_table.LoadedEntries, _draft.Base) ? " · coincide con el archivo" : " · distinto del archivo (usa «Guardar y recargar»)";
            else if (HasFile && !IsLinked) sync = " · el archivo abierto NO es el del modelo";
            RevitStatus = "Revit: " + where + " · " + _table.LoadStatus + " · " + loaded + sync + (_table.DocumentReadOnly ? " · documento de solo lectura: " + _table.DocumentReadOnlyReason : "");
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
            RegexError = e1 != null ? "Regex incluir no válida: " + e1 : e2 != null ? "Regex excluir no válida: " + e2 : "";
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
                row.State = ch.Kind == ChangeKind.Added ? "Nueva" : ch.Kind == ChangeKind.Rekeyed ? "Re-key" : "Modificada";
                row.StateTip = ch.Describe();
            }
            if (_usage == null) { row.Usage = ""; row.UsageTip = "Uso sin leer."; }
            else if (!_usage.Succeeded) { row.Usage = "?"; row.UsageTip = "Uso desconocido: la consulta falló (" + _usage.Error + ")."; }
            else
            {
                string orig = _draft.OriginalKeyOf(r.Key);
                var u = _usage.Of(orig);
                if (u != null && u.IsUsed)
                {
                    row.IsUsed = true;
                    var parts = new List<string>();
                    if (u.Tags > 0) parts.Add(u.Tags + " etiq.");
                    if (u.Views.Count > 0) parts.Add(u.Views.Count + " vista" + (u.Views.Count == 1 ? "" : "s"));
                    if (u.Assignments > 0) parts.Add(u.Assignments + " asig.");
                    row.Usage = string.Join(" · ", parts);
                    row.UsageTip = u.UserTags + " etiqueta(s) de usuario, " + u.ElementTags + " de elemento, " + u.MaterialTags + " de material; " + u.ElementAssignments + " tipo(s)/elemento(s) y " + u.MaterialAssignments + " material(es) con esta clave (este proyecto)." + (orig != r.Key ? " Uso de la clave original '" + orig + "'." : "");
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
            var list = new List<KeynoteRecord> { new KeynoteRecord { Key = "", Text = "(primer nivel: grupo)" } };
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
            var f = Form("Nuevo grupo", "", true, "", "", false, "Un grupo es una nota de primer nivel (categoría en el diálogo de Revit). Ejemplo: clave E, texto ELECTRICAL.",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "El texto no puede estar vacío." : null));
            if (f == null) return;
            if (_draft.Add(f.Key, f.Text, "") is string e) { Op(e); return; }
            _containers.Add(f.Key.Trim());
            Select(f.Key.Trim());
        }

        private void NewSubgroup()
        {
            string parent = SelectedRow.Key;
            var f = Form("Nuevo subgrupo dentro de " + parent, SuggestChildKey(parent), true, "", parent, true, "Un subgrupo es una nota con padre que contendrá otras notas. Ejemplo: clave E-LTG, texto LIGHTING, padre E.",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "El texto no puede estar vacío." : null));
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
                _dlg.Info("Nueva nota", "La biblioteca está vacía: crea primero un grupo (por ejemplo E – ELECTRICAL).");
                return;
            }
            var f = Form("Nueva nota", SuggestChildKey(parent), true, "", parent, true, "Pega texto de varias líneas: se convertirá en una sola línea (vista previa abajo).",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "El texto no puede estar vacío." : null) ?? (string.IsNullOrEmpty(p) ? null : _draft.Current.Contains(p) ? null : "El padre no existe."));
            if (f == null) return;
            Op(_draft.Add(f.Key, f.Text, f.ParentKey));
            if (!string.IsNullOrEmpty(f.ParentKey)) _expanded.Add(f.ParentKey);
            Select(f.Key.Trim());
        }

        private void EditSelected()
        {
            var r = _draft.Current.Find(SelectedRow.Key);
            if (r == null) return;
            var f = Form("Editar " + r.Key, r.Key, false, r.Text, r.ParentKey, true, "La clave no cambia al editar (para cambiarla usa «Cambiar clave…»). Las etiquetas existentes mostrarán el texto nuevo tras «Guardar y recargar».",
                (k, t, p) => KeynoteText.Normalize(t).Length == 0 ? "El texto no puede estar vacío." : null, r.Key);
            if (f == null) return;
            Op(_draft.Edit(r.Key, f.Text, f.ParentKey));
            Select(r.Key);
        }

        private void DuplicateSelected()
        {
            var r = _draft.Current.Find(SelectedRow.Key);
            if (r == null) return;
            var f = Form("Duplicar " + r.Key, _draft.SuggestKey(r.Key), true, r.Text, r.ParentKey, true, "Copia una sola nota (sin sus hijas). Para copiar una rama usa Copiar (Ctrl+C) y Pegar (Ctrl+V).",
                (k, t, p) => _draft.ValidateNewKey((k ?? "").Trim()) ?? (KeynoteText.Normalize(t).Length == 0 ? "El texto no puede estar vacío." : null));
            if (f == null) return;
            Op(_draft.Add(f.Key, f.Text, f.ParentKey));
            Select(f.Key.Trim());
        }

        private void RekeySelected()
        {
            string key = SelectedRow.Key;
            string orig = _draft.OriginalKeyOf(key);
            var u = _usage != null && _usage.Succeeded ? _usage.Of(orig) : null;
            string usage = _usage == null || !_usage.Succeeded ? "Uso en el modelo desconocido: se comprobará al guardar."
                : u == null || !u.IsUsed ? "No hay etiquetas ni asignaciones con esta clave en este proyecto."
                : "Al «Guardar y recargar» se actualizarán " + u.Tags + " etiqueta(s) y " + u.Assignments + " asignación(es) de este proyecto. Otros proyectos que usen el archivo quedarán con la clave antigua.";
            string n = _dlg.AskText("Cambiar clave", "Clave nueva para '" + key + "'. Las notas hijas pasan a la clave nueva.\n" + usage, key);
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
                string use = unknown ? " — uso desconocido" : u != null && u.IsUsed ? " — EN USO: " + u.Tags + " etiqueta(s), " + u.Assignments + " asignación(es)" : "";
                if (u != null && u.IsUsed) used++;
                lines.Add(r.Key + "  " + r.Text + use);
            }
            string intro = branch.Count == 1 ? "Se eliminará esta nota del borrador." : "Se eliminará la rama completa (" + branch.Count + " notas).";
            if (used > 0) intro += "\n\n" + used + " de ellas se usan en este proyecto: sus etiquetas quedarán sin texto (clave no encontrada) y las asignaciones apuntarán a una clave inexistente. Revit no borra las etiquetas.";
            else if (unknown) intro += "\n\nNo se pudo comprobar el uso en el modelo.";
            intro += "\nNada cambia en el archivo hasta guardar.";
            if (!_dlg.Review("Eliminar " + key, intro, lines, used > 0 ? "Eliminar de todos modos" : "Eliminar", danger: used > 0 || unknown)) return;
            Op(_draft.Delete(key, true));
        }

        private void DiscardAll()
        {
            if (!_dlg.Confirm("Descartar cambios", "¿Descartar los " + ChangeCount + " cambio(s) sin guardar y volver al archivo?")) return;
            _draft.ResetTo(_draft.Base);
            _store.DeleteDraft(_path);
            Status = "Cambios descartados.";
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
            Status = ch.Count == 0 ? "El texto ya estaba así." : ch.Count + " texto(s) cambiados.";
        }

        private void SaveTemplate()
        {
            string name = _dlg.AskText("Guardar plantilla de texto", "Nombre de la plantilla (texto: '" + SelectedRow.Text + "'):", SelectedRow.Text.Length > 30 ? SelectedRow.Text.Substring(0, 30) : SelectedRow.Text);
            if (string.IsNullOrWhiteSpace(name)) return;
            var list = Templates.Where(t => !t.BuiltIn && t.Name != name).ToList();
            list.Add(new TextTemplate { Name = name.Trim(), Text = SelectedRow.Text });
            try { _store.SaveTemplates(list); _templates = null; Status = "Plantilla guardada."; }
            catch (Exception ex) { _dlg.Error("Plantillas", "No se pudo guardar: " + ex.Message); }
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
            if (!CanEdit) return "El archivo es de solo lectura.";
            var r = _draft.Current.Find(key);
            if (r == null) return "La nota no existe.";
            if ((r.ParentKey ?? "") == (newParent ?? "")) return null;
            string e = _draft.SetParent(key, newParent ?? "");
            if (e == null) { if (!string.IsNullOrEmpty(newParent)) _expanded.Add(newParent); Select(key); }
            else _dlg.Error("Mover", e);
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
            Status = "Copiada la rama " + SelectedRow.Key + " (" + branch.Count + " nota(s)) en formato de keynotes.";
        }

        private void Paste()
        {
            var recs = FromClipboardText(_dlg.GetClipboard());
            if (recs == null) { _dlg.Info("Pegar", "El portapapeles no contiene notas en formato «clave TAB texto [TAB padre]». Para pegar texto en una nota, edítala (F2)."); return; }
            string target = SelectedRow?.Key ?? "";
            var plan = KeynoteImport.PlanPaste(_draft, recs, target);
            if (!_dlg.ReviewIncoming("Pegar " + plan.Count + " nota(s)", "Se pegarán dentro de " + (target.Length == 0 ? "el primer nivel" : "'" + target + "'") + ". Las claves que ya existen reciben una clave nueva propuesta (puedes cambiarla). Nada se guarda hasta «Guardar».", plan, true)) return;
            string e = KeynoteImport.ApplyPaste(_draft, plan);
            if (e != null) { _dlg.Error("Pegar", e); return; }
            if (target.Length > 0) _expanded.Add(target);
            Status = plan.Count(i => i.Take) + " nota(s) pegadas.";
            Rebuild();
        }

        // ------------------------------------------------------------------ import / export

        private async Task ImportAsync()
        {
            string p = _dlg.PickOpenFile("Importar keynotes (TXT de Revit o exportación de pyRevit)", FileFilter, _path);
            if (string.IsNullOrEmpty(p)) return;
            KeynoteDocument doc;
            try { doc = await Task.Run(() => KeynoteFormat.Read(File.ReadAllBytes(p))); }
            catch (Exception ex) { _dlg.Error("Importar", "No se pudo leer: " + ex.Message); return; }
            var plan = KeynoteImport.PlanImport(_draft, doc.Library);
            int nNew = plan.Count(i => i.Status == IncomingStatus.New), nSame = plan.Count(i => i.Status == IncomingStatus.Identical), nConf = plan.Count(i => i.Status == IncomingStatus.Conflict);
            string intro = Path.GetFileName(p) + " (" + KeynoteFormat.Describe(doc.Encoding) + (doc.IsDeffrelDb ? ", pyRevit DeffrelDB" : "") + "): " + nNew + " nueva(s), " + nSame + " idéntica(s), " + nConf + " en conflicto."
                + (nConf > 0 ? " En los conflictos se conserva tu versión salvo que marques la importada." : "")
                + (doc.Unparsed.Count > 0 ? "\n" + doc.Unparsed.Count + " línea(s) mal formada(s) no se importan: " + string.Join("; ", doc.Unparsed.Take(3).Select(u => "línea " + u.Line + " " + u.Reason)) : "");
            if (!_dlg.ReviewIncoming("Importar keynotes", intro, plan, false)) return;
            string e = KeynoteImport.ApplyImport(_draft, plan);
            if (e != null) { _dlg.Error("Importar", e); return; }
            Status = "Importadas " + plan.Count(i => i.Take) + " nota(s). Revisa y guarda.";
        }

        private void Export(bool filtered)
        {
            var recs = Ordered().Where(r => !filtered || _filtered.Contains(r.Key)).ToList();
            if (recs.Count == 0) { _dlg.Info("Exportar", "No hay notas que exportar con el filtro actual."); return; }
            string p = _dlg.PickSaveFile(filtered ? "Exportar notas filtradas (con sus padres)" : "Exportar todas las notas", FileFilter, Path.Combine(Path.GetDirectoryName(_path) ?? "", Path.GetFileNameWithoutExtension(_path) + (filtered ? "_filtrado" : "_export") + ".txt"));
            if (string.IsNullOrEmpty(p)) return;
            if (SamePath(p, _path)) { _dlg.Error("Exportar", "Elige un archivo distinto del que estás editando (usa «Guardar como…» para eso)."); return; }
            try
            {
                File.WriteAllBytes(p, KeynoteFormat.Encode(KeynoteFormat.Compose(new KeynoteLibrary(recs)), KeynoteEncoding.Utf16LeBom, out _));
                Status = "Exportadas " + recs.Count + " nota(s) a " + p + " (UTF-16 LE).";
            }
            catch (Exception ex) { _dlg.Error("Exportar", ex.Message); }
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
                _dlg.Error("Guardar", ReadOnlyReason);
                return false;
            }
            if (!_draft.IsDirty && !reload) { Status = "No hay cambios que guardar."; return true; }
            if (!_draft.IsDirty && reload) return await ReloadOnlyAsync();

            var issues = _draft.Current.Validate();
            if (issues.Count > 0)
            {
                _dlg.Review("No se puede guardar", "Corrige estos problemas antes de guardar (el archivo no se ha modificado):", issues.Select(i => i.Message).ToList(), "Entendido");
                return false;
            }
            var renames = _draft.Renames.Select(r => new KeyRename { From = r.From, To = r.To }).ToList();
            if (renames.Count > 0 && !IsLinked)
            {
                _dlg.Error("Cambio de clave", "Hay cambios de clave y el archivo abierto no es el del modelo: las referencias del modelo no se podrían actualizar. Vincula el archivo o revierte los cambios de clave.");
                return false;
            }
            if (renames.Count > 0) reload = true;
            if (reload && !IsLinked)
            {
                if (!_dlg.Confirm("Guardar y recargar", "El archivo abierto no es el del modelo. ¿Guardar sin recargar Revit?")) return false;
                reload = false;
            }

            bool ok = false;
            await Busy("Guardando", async () =>
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
                        _dlg.Review("Cambio de clave bloqueado", "No se guardó nada. Las referencias del modelo no se pueden actualizar de forma coherente:", check.Blockers, "Entendido");
                        return;
                    }
                }
                if (risky.Count > 0)
                {
                    var lines = risky.Select(c => c.Describe()).ToList();
                    if (check != null) lines.AddRange(check.Lines);
                    if (!_dlg.Review("Revisar antes de guardar", "Estos cambios afectan a notas existentes" + (reload ? " y se aplicarán también al modelo en un solo paso (un Deshacer)." : "."), lines, reload ? "Guardar y recargar" : "Guardar", danger: true)) return;
                }
                bool keepUnparsed = true;
                if (_unparsed.Count > 0)
                    keepUnparsed = !_dlg.Confirm("Líneas mal formadas", _unparsed.Count + " línea(s) del archivo no son registros válidos (por ejemplo línea " + _unparsed[0].Line + ": " + _unparsed[0].Reason + ").\n\n¿Quitarlas al guardar? (No = se conservan tal cual y Revit las reportará al recargar.)");

                byte[] previous = File.Exists(_path) ? File.ReadAllBytes(_path) : new byte[0];
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var lib = _draft.Current.Clone();
                    byte[] bytes = Compose(lib, keepUnparsed, out var used);
                    var res = _store.Save(_path, bytes, _baseHash);
                    if (res.Outcome == SaveOutcome.Failed) { _dlg.Error("Guardar", res.Message + "\n\nTus cambios siguen en el borrador (también guardado localmente)."); FileStatus = "Archivo: no guardado — " + res.Message; return; }
                    if (res.Outcome == SaveOutcome.Conflict)
                    {
                        if (res.Theirs == null) { _dlg.Error("Guardar", res.Message + " Usa «Guardar como…»."); return; }
                        if (!MergeWith(res.Theirs)) { Status = "Guardado cancelado: el archivo cambió y no se resolvieron los conflictos."; return; }
                        renames = _draft.Renames.Select(r => new KeyRename { From = r.From, To = r.To }).ToList();
                        continue;
                    }
                    // Saved.
                    if (used != _encoding)
                    {
                        _dlg.Info("Codificación", "El texto contiene caracteres que " + KeynoteFormat.Describe(_encoding) + " no puede guardar: el archivo se guardó como " + KeynoteFormat.Describe(used) + ".");
                        _encoding = used;
                    }
                    if (!keepUnparsed) _unparsed.Clear();
                    _baseHash = res.WrittenHash;
                    _draft.MarkSaved(lib);
                    _store.DeleteDraft(_path);
                    _access = _store.Probe(_path);
                    FileStatus = "Archivo guardado " + DateTime.Now.ToString("HH:mm:ss") + " · " + lib.Records.Count + " registros · " + KeynoteFormat.Describe(_encoding) + " · copia previa en " + Path.GetFileName(res.BackupPath);
                    if (reload)
                    {
                        BusyText = "Recargando la tabla de keynotes en Revit";
                        ReloadResult rr;
                        try { rr = await _gw.Reload(renames); }
                        catch (Exception ex) { rr = new ReloadResult { Succeeded = false, Status = "Error", Path = _path }; rr.Messages.Add(ex.Message); }
                        AfterReload(rr, lib, new RestoreInfo { Previous = previous, WrittenHash = res.WrittenHash, Draft = lib, Renames = renames });
                        _table = await SafeReadTable();
                        await ReadUsageAsync();
                    }
                    else Status = "Guardado. Revit todavía usa la versión anterior hasta «Guardar y recargar» (F5 en Revit no recarga el archivo).";
                    UpdateRevitStatus();
                    NotifyFile();
                    ok = true;
                    return;
                }
                _dlg.Error("Guardar", "El archivo sigue cambiando mientras se guarda (¿sincronización activa?). Inténtalo de nuevo.");
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
            await Busy("Recargando la tabla de keynotes en Revit", async () =>
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
                string refs = rr.UpdatedTags + rr.UpdatedAssignments > 0 ? " Referencias actualizadas: " + rr.UpdatedTags + " etiqueta(s), " + rr.UpdatedAssignments + " asignación(es)." : "";
                Status = same ? "Guardado y recargado: Revit cargó " + rr.LoadedEntries.Count + " notas, verificado contra el archivo." + refs
                              : "Revit recargó, pero lo cargado no coincide con el archivo (" + rr.LoadedEntries.Count + " notas en Revit, " + expected.Records.Count + " en el archivo). Revisa los avisos." + refs;
                if (rr.Messages.Count > 0) _dlg.Review("Avisos de Revit al recargar", "Revit cargó la tabla con estos avisos:", rr.Messages, "Entendido");
                return;
            }
            _log("Keynotes: reload failed " + rr.Status + " " + string.Join(" | ", rr.Messages));
            string msg = "Revit no pudo recargar el archivo (" + rr.Status + "). El modelo no cambió (operación revertida).";
            if (restore == null) { _dlg.Review("Recarga fallida", msg, rr.Messages, "Entendido"); Status = msg; return; }
            var lines = rr.Messages.ToList();
            if (_dlg.Review("Recarga fallida", msg + "\n\nEl archivo SÍ quedó guardado con tus cambios. ¿Restaurar el archivo anterior? (solo si nadie lo ha cambiado desde que se guardó; tus cambios volverán a quedar pendientes en el borrador)", lines, "Restaurar archivo anterior"))
            {
                var r = _store.RestoreIfUnchanged(_path, restore.WrittenHash, restore.Previous);
                if (r.Outcome == SaveOutcome.Saved)
                {
                    var prev = KeynoteFormat.Read(restore.Previous);
                    _baseHash = r.WrittenHash;
                    _draft.ResetTo(prev.Library);
                    _draft.Replace(restore.Draft, restore.Renames);
                    _store.SaveDraft(_path, _draft, _baseHash);
                    Status = "Archivo restaurado; tus cambios siguen pendientes en el borrador.";
                }
                else _dlg.Error("Restaurar", r.Message);
            }
            else Status = msg + " El archivo quedó guardado con tus cambios.";
        }

        /// <summary>Three-way merge with the file as it is now. Returns false if the user cancelled.</summary>
        private bool MergeWith(FileSnapshot theirs)
        {
            var m = KeynoteMerge.Merge(_draft.Base, _draft.Current, theirs.Document.Library, _draft.Renames);
            if (m.Conflicts.Count > 0 && !_dlg.ResolveConflicts(m)) return false;
            var merged = KeynoteMerge.Resolve(m, out string err);
            if (merged == null || err != null) { _dlg.Error("Combinar", err ?? "No se pudo combinar."); return false; }
            if (m.TheirChanges.Count > 0 && !_dlg.Review("El archivo cambió", "Otro usuario o proceso cambió el archivo desde que lo abriste. Sus cambios se conservan junto a los tuyos:", m.TheirChanges, "Continuar guardando")) return false;
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
            string p = _dlg.PickSaveFile("Guardar como", FileFilter, _path);
            if (string.IsNullOrEmpty(p)) return;
            if (SamePath(p, _path)) { await SaveAsync(false); return; }
            if (_draft.Renames.Count > 0) { _dlg.Error("Guardar como", "Hay cambios de clave pendientes: se aplican al modelo solo con «Guardar y recargar» del archivo vinculado. Guarda primero o revierte el cambio de clave."); return; }
            var issues = _draft.Current.Validate();
            if (issues.Count > 0) { _dlg.Review("No se puede guardar", "Corrige estos problemas:", issues.Select(i => i.Message).ToList(), "Entendido"); return; }
            string expected = "";
            if (File.Exists(p))
            {
                if (!_dlg.Confirm("Guardar como", "'" + p + "' ya existe. ¿Reemplazarlo? (se guarda una copia de seguridad)")) return;
                expected = _store.CurrentHash(p);
            }
            bool fromDeffrel = _deffrel;
            if (fromDeffrel) { _preamble.Clear(); _unparsed.Clear(); _encoding = KeynoteEncoding.Utf16LeBom; }
            var lib = _draft.Current.Clone();
            var res = _store.Save(p, Compose(lib, true, out var used), expected);
            if (res.Outcome != SaveOutcome.Saved) { _dlg.Error("Guardar como", res.Message); return; }
            if (HasFile) _store.DeleteDraft(_path);
            OpenPath(p);
            Status = "Guardado como " + p + (fromDeffrel ? " (copia en formato estándar; el archivo de pyRevit no se modificó)" : "") + ".";
            UpdateRevitStatus();
            if (!IsLinked && _table != null && !_table.DocumentReadOnly && _dlg.Confirm("Vincular al modelo", "¿Vincular '" + Path.GetFileName(p) + "' como archivo de keynotes del modelo?"))
                await LinkAsync();
        }

        // ------------------------------------------------------------------ placement / usage

        private async Task PlaceAsync(string mode)
        {
            var m = mode == "element" ? PlacementMode.Element : mode == "material" ? PlacementMode.Material : PlacementMode.User;
            string key = SelectedRow.Key;
            if (_table == null || !_table.HasKeynoteTagType) { _dlg.Error("Colocar nota", "El modelo no tiene un tipo de etiqueta de nota clave cargado. Carga una familia de etiqueta de nota clave (Keynote Tag)."); return; }
            if (!_table.ActiveViewAcceptsTags) { _dlg.Error("Colocar nota", "La vista activa (" + _table.ActiveViewName + ") no admite etiquetas de nota clave. Abre una vista de planta, alzado, sección, detalle o leyenda."); return; }
            bool inRevit = IsLinked && _table.IsLoaded && _table.LoadedEntries.Any(e => e.Key == key);
            bool pending = _draft.ChangeOf(key) != null || _draft.IsDirty && !inRevit;
            if (!inRevit || pending)
            {
                if (!IsLinked) { _dlg.Error("Colocar nota", "El archivo abierto no es el del modelo: Revit no conoce estas notas. Vincúlalo primero."); return; }
                if (!_dlg.Confirm("Colocar nota", "'" + key + "' " + (inRevit ? "tiene cambios sin recargar" : "no está todavía en la tabla cargada en Revit") + ". ¿Guardar y recargar ahora?")) return;
                if (!await SaveAsync(true)) return;
                _table = await SafeReadTable();
                if (!_table.LoadedEntries.Any(e => e.Key == key)) { _dlg.Error("Colocar nota", "'" + key + "' no aparece en la tabla recargada por Revit."); return; }
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
            switch (_dlg.AskSaveDiscardCancel("Hay " + ChangeCount + " cambio(s) sin guardar en " + Path.GetFileName(_path) + "."))
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
