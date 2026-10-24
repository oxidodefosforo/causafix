using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Values;

namespace CausaFix.ExcelLink
{
    public enum GridCellState
    {
        Unchanged = 0,
        Edited,
        Invalid,
        Locked,
        NotApplicable,
        Context
    }

    public sealed class GridColumn
    {
        public GridColumn(int index, ColumnDef def, bool sheetReadOnly)
        {
            Index = index;
            Def = def;
            Header = Core.Workbook.WorkbookExporter.HeaderText(def, sheetReadOnly);
            bool ro = sheetReadOnly || def.ColumnReadOnly || def.IsContext || def.Kind == ValueKind.ElementRef;
            IsEditable = !ro;
            Role = def.IsContext ? CellRole.Context : ro ? (def.Scope == ParamScope.Type ? CellRole.TypeReadOnly : CellRole.InstanceReadOnly)
                                                         : (def.Scope == ParamScope.Type ? CellRole.TypeEditable : CellRole.InstanceEditable);
            IsNumeric = def.Kind == ValueKind.Double || def.Kind == ValueKind.Integer;
        }

        public int Index { get; }
        public ColumnDef Def { get; }
        public string Header { get; }
        public bool IsEditable { get; }
        public bool IsNumeric { get; }
        public CellRole Role { get; }
        public string Tooltip => Def.IsContext ? "Context" :
            (Def.Scope == ParamScope.Type ? "Type parameter – changes affect every instance of the type" : "Instance parameter") +
            " · " + Def.DataTypeLabel + (string.IsNullOrEmpty(Def.UnitLabel) ? "" : " [" + Def.UnitLabel + "]") +
            (IsEditable ? "" : " · Read-only: " + Def.ReadOnlyReason) + Environment.NewLine + Def.Key;
    }

    public sealed class GridCell : ObservableObject
    {
        private readonly GridRow _row;
        private string _text;
        private GridCellState _state;
        private string _message = string.Empty;

        internal GridCell(GridRow row, GridColumn column, CellValue original, bool editable, string originalText)
        {
            _row = row;
            Column = column;
            Original = original;
            IsEditable = editable;
            OriginalText = originalText;
            _text = originalText;
            _state = column.Def.IsContext ? GridCellState.Context : original.Raw.IsNotApplicable ? GridCellState.NotApplicable
                   : editable ? GridCellState.Unchanged : GridCellState.Locked;
            if (!editable && !column.Def.IsContext) _message = original.Raw.IsNotApplicable ? "Parameter not present on this element (N/A)." : original.LockReason;
        }

        public GridColumn Column { get; }
        public CellValue Original { get; }
        public string OriginalText { get; }
        public bool IsEditable { get; }
        public Proposal Proposal { get; private set; }

        /// <summary>Cell text. Setting it validates the input; nothing is written to Revit.</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (!IsEditable) { OnPropertyChanged(); return; }
                _row.Owner.Edit(this, value ?? string.Empty);
            }
        }

        public GridCellState State { get => _state; private set { if (Set(ref _state, value)) { OnPropertyChanged(nameof(IsEdited)); OnPropertyChanged(nameof(IsInvalid)); } } }
        public bool IsEdited => State == GridCellState.Edited;
        public bool IsInvalid => State == GridCellState.Invalid;
        public string Message { get => _message; private set => Set(ref _message, value ?? string.Empty); }
        public CellRole Role => State == GridCellState.NotApplicable ? CellRole.NotApplicable : Column.Role == CellRole.InstanceEditable && !IsEditable ? CellRole.InstanceReadOnly
                              : Column.Role == CellRole.TypeEditable && !IsEditable ? CellRole.TypeReadOnly : Column.Role;
        /// <summary>Sort key: numbers sort numerically, everything else as text.</summary>
        /// <remarks>A column always yields one key type (DataGrid sorting compares keys of a column with each other).</remarks>
        public IComparable SortKey => Column.IsNumeric ? (IComparable)(Original.ExportNumber ?? double.MinValue) : (_text ?? string.Empty);
        public string Tooltip => string.IsNullOrEmpty(Message) ? (State == GridCellState.Edited ? "Original: " + OriginalText : null) : Message;

        internal GridRow Row => _row;

        internal void Update(string text, GridCellState state, Proposal proposal, string message)
        {
            var old = _state;
            _text = text;
            OnPropertyChanged(nameof(Text));
            Proposal = proposal;
            State = state;
            Message = message;
            if (old != state) _row.Owner.Owner.CellStateChanged(old, state);
            OnPropertyChanged(nameof(SortKey));
            OnPropertyChanged(nameof(Tooltip));
            OnPropertyChanged(nameof(Role));
        }
    }

    public sealed class GridRow : ObservableObject
    {
        internal GridRow(SheetGrid owner, RowData data)
        {
            Owner = owner;
            Data = data;
        }

        internal SheetGrid Owner { get; }
        public RowData Data { get; }
        public GridCell[] Cells { get; internal set; }
        public string Label => Data.Label;
        public bool IsLinked => Data.IsLinked;
        public bool HasEdits => Cells.Any(c => c.State == GridCellState.Edited || c.State == GridCellState.Invalid);
        internal void RaiseEdits() => OnPropertyChanged(nameof(HasEdits));
    }

    /// <summary>One sheet of the preview (one category / schedule).</summary>
    public sealed class SheetGrid : ObservableObject
    {
        private readonly PreviewViewModel _owner;

        internal SheetGrid(PreviewViewModel owner, DataSheet sheet)
        {
            _owner = owner;
            Sheet = sheet;
            Columns = sheet.Columns.Select((c, i) => new GridColumn(i, c, sheet.ReadOnly)).ToList();
            foreach (var r in sheet.Rows)
            {
                var row = new GridRow(this, r);
                row.Cells = Columns.Select(c =>
                {
                    var cv = r.Get(c.Def.Key) ?? CellValue.NotApplicable();
                    bool editable = c.IsEditable && cv.Editable && !r.IsLinked && !cv.Raw.IsNotApplicable;
                    return new GridCell(row, c, cv, editable, PreviewViewModel.DisplayText(c.Def, cv));
                }).ToArray();
                Rows.Add(row);
            }
        }

        public DataSheet Sheet { get; }
        public string Title => Sheet.Title + " (" + Rows.Count + ")";
        public List<GridColumn> Columns { get; }
        public List<GridRow> Rows { get; } = new List<GridRow>();
        public string Notes => string.Join(" ", Sheet.Notes);

        internal PreviewViewModel Owner => _owner;

        internal void Edit(GridCell cell, string text) => _owner.EditCell(this, cell, text);
    }

    /// <summary>
    /// Preview / Edit: a virtualised table over live model data. Editing only prepares changes;
    /// Apply Changes sends them through the same comparison and apply engine as Excel imports.
    /// </summary>
    public sealed class PreviewViewModel : ObservableObject
    {
        public enum RowFilter { All, Edited, Invalid }

        private readonly IExcelLinkGateway _gateway;
        private readonly IDialogService _dialogs;
        private readonly Func<ReviewViewModel, Task> _showReview;
        private SheetGrid _current;
        private string _search = string.Empty;
        private RowFilter _filter = RowFilter.All;
        private string _status = string.Empty;
        private Func<Task<ExportPackage>> _reload;

        public PreviewViewModel(IExcelLinkGateway gateway, IDialogService dialogs, Func<ReviewViewModel, Task> showReview)
        {
            _gateway = gateway;
            _dialogs = dialogs;
            _showReview = showReview;
            RevertAllCommand = new RelayCommand(RevertAll, () => PendingCount + InvalidCount > 0);
        }

        public ExportPackage Package { get; private set; }
        public IUnitConverter Units { get; private set; } = IdentityUnitConverter.Instance;
        public ImportOptions GridOptions { get; set; } = new ImportOptions();
        public ObservableCollection<SheetGrid> Sheets { get; } = new ObservableCollection<SheetGrid>();
        public ObservableCollection<GridRow> VisibleRows { get; } = new ObservableCollection<GridRow>();
        public RelayCommand RevertAllCommand { get; }
        public bool HasData => Sheets.Count > 0;
        public string Status { get => _status; set => Set(ref _status, value ?? string.Empty); }

        public SheetGrid Current
        {
            get => _current;
            set { if (Set(ref _current, value)) { OnPropertyChanged(nameof(Columns)); RefreshRows(); ColumnsChanged?.Invoke(this, EventArgs.Empty); } }
        }

        public List<GridColumn> Columns => _current?.Columns ?? new List<GridColumn>();

        /// <summary>Raised when the visible sheet changes: the view rebuilds its columns.</summary>
        public event EventHandler ColumnsChanged;
        /// <summary>Raised after changes were applied to Revit (other views may refresh).</summary>
        public event EventHandler Applied;

        public string Search { get => _search; set { if (Set(ref _search, value ?? "")) RefreshRows(); } }
        public RowFilter Filter { get => _filter; set { if (Set(ref _filter, value)) RefreshRows(); } }
        public bool ShowAll { get => Filter == RowFilter.All; set { if (value) Filter = RowFilter.All; } }
        public bool ShowEdited { get => Filter == RowFilter.Edited; set { if (value) Filter = RowFilter.Edited; } }
        public bool ShowInvalid { get => Filter == RowFilter.Invalid; set { if (value) Filter = RowFilter.Invalid; } }

        private int _pending, _invalid;
        public int PendingCount => _pending;
        public int InvalidCount => _invalid;

        internal void CellStateChanged(GridCellState from, GridCellState to)
        {
            if (from == GridCellState.Edited) _pending--;
            if (from == GridCellState.Invalid) _invalid--;
            if (to == GridCellState.Edited) _pending++;
            if (to == GridCellState.Invalid) _invalid++;
        }
        public string Summary => !HasData ? "No data loaded. Choose a source tab and press Preview/Edit."
            : Sheets.Sum(s => s.Rows.Count) + " row(s) in " + Sheets.Count + " sheet(s) | pending edits " + PendingCount + (InvalidCount > 0 ? " | invalid " + InvalidCount : "");

        private IEnumerable<GridCell> AllCells() => Sheets.SelectMany(s => s.Rows).SelectMany(r => r.Cells);

        /// <summary>Loads a snapshot. <paramref name="reload"/> re-reads the same request after applying.</summary>
        public void Load(ExportPackage pkg, IUnitConverter units, Func<Task<ExportPackage>> reload)
        {
            Package = pkg;
            Units = units ?? IdentityUnitConverter.Instance;
            _reload = reload;
            _pending = 0;
            _invalid = 0;
            Sheets.Clear();
            foreach (var s in pkg.Sheets.Where(s => s.Source != SheetSource.SchedulePresentation)) Sheets.Add(new SheetGrid(this, s));
            _current = null;
            Current = Sheets.FirstOrDefault();
            Status = pkg.Sheets.Any(s => s.Source == SheetSource.SchedulePresentation) ? "Presentation exports are not shown here (export them to Excel)." : string.Empty;
            Changed();
        }

        public static string DisplayText(ColumnDef col, CellValue cv)
        {
            if (cv.Raw.IsNotApplicable) return "N/A";
            if (cv.Raw.IsEmpty) return string.Empty;
            switch (col.Kind)
            {
                case ValueKind.Text: return cv.Raw.Kind == RawKind.Text ? cv.Raw.Text : cv.DisplayText;
                case ValueKind.Integer: return cv.Raw.Integer.ToString(CultureInfo.CurrentCulture);
                case ValueKind.YesNo: return cv.Raw.Integer == 1 ? "Yes" : cv.Raw.Integer == 0 ? "No" : cv.Raw.Integer.ToString(CultureInfo.CurrentCulture);
                case ValueKind.Double: return cv.ExportNumber.HasValue ? cv.ExportNumber.Value.ToString("G12", CultureInfo.CurrentCulture) : cv.DisplayText;
                default: return cv.DisplayText ?? string.Empty;
            }
        }

        internal void EditCell(SheetGrid sheet, GridCell cell, string text)
        {
            if (!cell.IsEditable) return;
            if (text == cell.OriginalText)
            {
                cell.Update(text, GridCellState.Unchanged, null, string.Empty);
            }
            else if (text.Trim().Length == 0)
            {
                cell.Update(cell.OriginalText, GridCellState.Unchanged, null,
                    "Empty input is ignored. Use Clear Value (" + GridOptions.ClearToken + ") to erase the value.");
                Status = "Empty input ignored – use Clear Value to erase a value.";
            }
            else
            {
                var p = ProposalParser.FromText(cell.Column.Def, text, Units, GridOptions);
                if (p.Kind == ProposalKind.Invalid) cell.Update(text, GridCellState.Invalid, p, p.Message);
                else if (p.Kind == ProposalKind.Clear && cell.Original.Raw.IsEmpty) cell.Update(cell.OriginalText, GridCellState.Unchanged, null, string.Empty);
                else if (p.Kind == ProposalKind.Value && ValueComparer.AreEqual(p.Value, cell.Original.Raw)) cell.Update(text, GridCellState.Unchanged, null, string.Empty);
                else cell.Update(text, GridCellState.Edited, p, p.Kind == ProposalKind.Clear ? "Will be cleared." : "Original: " + cell.OriginalText);
            }
            cell.Row.RaiseEdits();
            Changed();
        }

        public void SetText(IEnumerable<GridCell> cells, string text)
        {
            foreach (var c in cells.Where(c => c.IsEditable).ToList()) c.Text = text;
        }

        /// <summary>Clear Value action: marks the cells to be erased (when Revit allows it).</summary>
        public void ClearValues(IEnumerable<GridCell> cells) => SetText(cells, GridOptions.ClearToken);

        public void Revert(IEnumerable<GridCell> cells)
        {
            var rows = new HashSet<GridRow>();
            foreach (var c in cells.ToList())
            {
                if (!c.IsEditable) continue;
                c.Update(c.OriginalText, GridCellState.Unchanged, null, string.Empty);
                rows.Add(c.Row);
            }
            foreach (var r in rows) r.RaiseEdits();
            Changed();
        }

        public void RevertAll() => Revert(AllCells().Where(c => c.State == GridCellState.Edited || c.State == GridCellState.Invalid));

        /// <summary>Tab-separated text for the given rectangular block (rows × columns in view order).</summary>
        public static string Copy(IList<GridRow> rows, IList<GridColumn> cols)
        {
            var sb = new StringBuilder();
            foreach (var r in rows)
            {
                sb.Append(string.Join("\t", cols.Select(c => (r.Cells[c.Index].Text ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", " "))));
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Pastes tab-separated text starting at (startRow, startColumn) in the visible order. A single value
        /// pasted over a multi-cell selection fills the selection. Read-only cells are skipped and counted.
        /// </summary>
        public string Paste(string text, GridRow startRow, GridColumn startColumn, IList<GridCell> selection = null)
        {
            if (string.IsNullOrEmpty(text) || _current == null) return "Nothing to paste.";
            var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(l => l.Split('\t')).ToList();
            int skipped = 0, written = 0;
            if (lines.Count == 1 && lines[0].Length == 1 && selection != null && selection.Count > 1)
            {
                foreach (var c in selection) { if (c.IsEditable) { c.Text = lines[0][0]; written++; } else skipped++; }
                return Report(written, skipped);
            }
            int r0 = VisibleRows.IndexOf(startRow);
            var visibleCols = _current.Columns;
            int c0 = visibleCols.IndexOf(startColumn);
            if (r0 < 0 || c0 < 0) return "Select a start cell first.";
            for (int i = 0; i < lines.Count && r0 + i < VisibleRows.Count; i++)
            {
                for (int j = 0; j < lines[i].Length && c0 + j < visibleCols.Count; j++)
                {
                    var cell = VisibleRows[r0 + i].Cells[visibleCols[c0 + j].Index];
                    if (!cell.IsEditable) { skipped++; continue; }
                    cell.Text = lines[i][j];
                    written++;
                }
            }
            return Report(written, skipped);

            string Report(int w, int s) => w + " cell(s) pasted" + (s > 0 ? ", " + s + " read-only cell(s) skipped." : ".");
        }

        public void RefreshRows()
        {
            VisibleRows.Clear();
            if (_current == null) { Changed(); return; }
            string q = Search.Trim();
            foreach (var r in _current.Rows)
            {
                if (Filter == RowFilter.Edited && !r.Cells.Any(c => c.State == GridCellState.Edited)) continue;
                if (Filter == RowFilter.Invalid && !r.Cells.Any(c => c.State == GridCellState.Invalid)) continue;
                if (q.Length > 0 && !r.Cells.Any(c => (c.Text ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)) continue;
                VisibleRows.Add(r);
            }
            Changed();
        }

        private void Changed()
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(InvalidCount));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasData));
            RevertAllCommand.Refresh();
        }

        /// <summary>Edited cells as proposals for the shared comparison/apply engine.</summary>
        public List<ProposedCell> Proposals()
        {
            var list = new List<ProposedCell>();
            foreach (var s in Sheets)
                foreach (var r in s.Rows)
                    foreach (var c in r.Cells)
                    {
                        if (c.State != GridCellState.Edited || c.Proposal == null) continue;
                        list.Add(new ProposedCell
                        {
                            Origin = ProposalOrigin.Grid,
                            SheetKey = s.Sheet.SheetKey,
                            SheetTitle = "Preview: " + s.Sheet.Title,
                            RowKey = r.Data.RowKey,
                            RowLabel = r.Data.Label,
                            ElementId = r.Data.ElementId,
                            IsLinked = r.Data.IsLinked,
                            Column = c.Column.Def,
                            TargetIsType = c.Column.Def.Scope == ParamScope.Type,
                            TargetUniqueId = c.Column.Def.Scope == ParamScope.Type && s.Sheet.RowMode == RowMode.Instance ? r.Data.TypeUniqueId : r.Data.UniqueId,
                            Original = c.Original.Raw,
                            Proposal = c.Proposal
                        });
                    }
            return list;
        }

        /// <summary>Builds the review of pending edits (same engine as Excel imports). Returns null if nothing to review.</summary>
        public async Task<ReviewViewModel> BuildReviewAsync(ReviewContext context)
        {
            if (InvalidCount > 0 && !_dialogs.Confirm("Invalid values", InvalidCount + " cell(s) contain invalid values and will be ignored. Continue with the valid edits?"))
                return null;
            var proposals = Proposals();
            if (proposals.Count == 0)
            {
                _dialogs.Info("Apply changes", "There are no pending edits.");
                return null;
            }
            var set = await _gateway.Compare(proposals, new ChangeSet());
            return new ReviewViewModel(_gateway, _dialogs, set, Units, context, "Preview / Edit");
        }

        public async Task ApplyFlowAsync(ReviewContext context)
        {
            var review = await BuildReviewAsync(context);
            if (review == null) return;
            review.Applied += async (s, e) =>
            {
                if (!review.Result.Committed) return;
                await ReloadAsync(force: true);
                Applied?.Invoke(this, EventArgs.Empty);
            };
            await _showReview(review);
        }

        /// <summary>Re-reads the data from Revit. Asks before discarding pending edits.</summary>
        public async Task ReloadAsync(bool force = false)
        {
            if (_reload == null) return;
            if (!force && PendingCount + InvalidCount > 0 &&
                !_dialogs.Confirm("Refresh preview", (PendingCount + InvalidCount) + " pending edit(s) will be discarded. Continue?"))
                return;
            string sheetKey = _current?.Sheet.SheetKey;
            var pkg = await _reload();
            Load(pkg, Units, _reload);
            var again = Sheets.FirstOrDefault(s => s.Sheet.SheetKey == sheetKey);
            if (again != null) Current = again;
        }

        /// <summary>Host UniqueIds of rows (for select / show / isolate).</summary>
        public static List<string> HostIds(IEnumerable<GridRow> rows) => rows.Where(r => !r.IsLinked).Select(r => r.Data.UniqueId).Distinct().ToList();
    }
}
