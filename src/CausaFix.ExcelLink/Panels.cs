using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CausaFix.Core.Mvvm;
using CausaFix.Core.Model;

namespace CausaFix.ExcelLink
{
    /// <summary>Colour role of a parameter or cell. The view maps it to soft green / yellow / red / grey AND a label.</summary>
    public enum CellRole
    {
        InstanceEditable = 0,
        TypeEditable,
        InstanceReadOnly,
        TypeReadOnly,
        NotApplicable,
        Context
    }

    public sealed class ParamItem : Selectable<ParameterInfo>
    {
        private bool _isMissing;

        public ParamItem(ParameterInfo p) : base(p) { }

        public string Name => Item.Name;
        public string KeyText => Item.Key.ToString();
        public bool IsType => Item.Scope == ParamScope.Type;
        public bool IsReadOnly => Item.ReadOnly || Item.Kind == ValueKind.ElementRef;
        public CellRole Role => IsReadOnly ? (IsType ? CellRole.TypeReadOnly : CellRole.InstanceReadOnly) : (IsType ? CellRole.TypeEditable : CellRole.InstanceEditable);
        /// <summary>Text tag shown next to the colour (never colour alone).</summary>
        public string Tag => (IsType ? "T" : "I") + (IsReadOnly ? " · RO" : Item.PartiallyReadOnly ? " · RO*" : "");
        public string Details
        {
            get
            {
                var p = Item;
                string unit = string.IsNullOrEmpty(p.UnitLabel) ? "" : " [" + p.UnitLabel + "]";
                string s = (IsType ? "Type" : "Instance") + " · " + p.SourceLabel + " · " + p.DataTypeLabel + unit;
                if (p.Total > 0) s += " · on " + p.AppliesTo.ToString(CultureInfo.CurrentCulture) + " of " + p.Total.ToString(CultureInfo.CurrentCulture) + " elements";
                if (IsReadOnly) s += " · Read-only: " + p.ReadOnlyReason;
                else if (p.PartiallyReadOnly) s += " · Read-only on some elements: " + p.ReadOnlyReason;
                if (IsMissing) s += " · NOT available in the current selection";
                return s;
            }
        }
        /// <summary>Selected parameter not available in the current categories (kept by key, never re-matched by name).</summary>
        public bool IsMissing { get => _isMissing; set { if (Set(ref _isMissing, value)) OnPropertyChanged(nameof(Details)); } }
    }

    /// <summary>Available / selected parameter lists with filters and ordering (three-panel layout).</summary>
    public sealed class ParameterPanelViewModel : ObservableObject
    {
        private readonly List<ParamItem> _available = new List<ParamItem>();
        private string _searchAvailable = string.Empty, _searchSelected = string.Empty;
        private bool _showInstance = true, _showType = true, _showReadOnly = true, _commonOnly;

        public ParameterPanelViewModel()
        {
            AddCommand = new RelayCommand(_ => Add(VisibleAvailable.Where(i => i.IsSelected).ToList()), _ => VisibleAvailable.Any(i => i.IsSelected));
            AddAllCommand = new RelayCommand(_ => Add(VisibleAvailable.ToList()), _ => VisibleAvailable.Count > 0);
            AddOneCommand = new RelayCommand(o => { if (o is ParamItem p) Add(new List<ParamItem> { p }); });
            RemoveCommand = new RelayCommand(_ => Remove(Selected.Where(i => i.IsSelected).ToList()), _ => Selected.Any(i => i.IsSelected));
            RemoveAllCommand = new RelayCommand(_ => Remove(Selected.ToList()), _ => Selected.Count > 0);
            RemoveOneCommand = new RelayCommand(o => { if (o is ParamItem p) Remove(new List<ParamItem> { p }); });
            MoveTopCommand = new RelayCommand(_ => Move(int.MinValue));
            MoveUpCommand = new RelayCommand(_ => Move(-1));
            MoveDownCommand = new RelayCommand(_ => Move(1));
            MoveBottomCommand = new RelayCommand(_ => Move(int.MaxValue));
            SortSelectedCommand = new RelayCommand(_ => SortSelected());
        }

        public ObservableCollection<ParamItem> VisibleAvailable { get; } = new ObservableCollection<ParamItem>();
        public ObservableCollection<ParamItem> Selected { get; } = new ObservableCollection<ParamItem>();
        public ObservableCollection<ParamItem> VisibleSelected { get; } = new ObservableCollection<ParamItem>();

        public RelayCommand AddCommand { get; }
        public RelayCommand AddAllCommand { get; }
        public RelayCommand AddOneCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand RemoveAllCommand { get; }
        public RelayCommand RemoveOneCommand { get; }
        public RelayCommand MoveTopCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        public RelayCommand MoveBottomCommand { get; }
        public RelayCommand SortSelectedCommand { get; }

        public string SearchAvailable { get => _searchAvailable; set { if (Set(ref _searchAvailable, value ?? "")) RefreshAvailable(); } }
        public string SearchSelected { get => _searchSelected; set { if (Set(ref _searchSelected, value ?? "")) RefreshSelected(); } }
        public bool ShowInstance { get => _showInstance; set { if (Set(ref _showInstance, value)) RefreshAvailable(); } }
        public bool ShowType { get => _showType; set { if (Set(ref _showType, value)) RefreshAvailable(); } }
        public bool ShowReadOnly { get => _showReadOnly; set { if (Set(ref _showReadOnly, value)) RefreshAvailable(); } }
        /// <summary>Only parameters present on every element in scope (otherwise the union).</summary>
        public bool CommonOnly { get => _commonOnly; set { if (Set(ref _commonOnly, value)) RefreshAvailable(); } }

        public int AvailableCount => _available.Count;
        public int SelectedCount => Selected.Count;
        public int MissingCount => Selected.Count(s => s.IsMissing);

        public event EventHandler SelectionChanged;

        /// <summary>Replaces the available parameters. Selected parameters are kept by key (never by name).</summary>
        public void SetAvailable(IEnumerable<ParameterInfo> parameters)
        {
            _available.Clear();
            var byKey = new Dictionary<ParamKey, ParamItem>();
            foreach (var p in parameters)
            {
                var item = new ParamItem(p);
                _available.Add(item);
                byKey[p.Key] = item;
            }
            for (int i = 0; i < Selected.Count; i++)
            {
                var s = Selected[i];
                if (byKey.TryGetValue(s.Item.Key, out var fresh))
                {
                    var replacement = new ParamItem(fresh.Item);
                    Selected[i] = replacement;
                }
                else s.IsMissing = true;
            }
            RefreshAvailable();
            RefreshSelected();
        }

        /// <summary>Restores a selection (profiles). Keys not available are kept as missing.</summary>
        public void SetSelected(IEnumerable<ParamKey> keys, IDictionary<ParamKey, string> namesForMissing = null)
        {
            Selected.Clear();
            foreach (var k in keys)
            {
                var avail = _available.FirstOrDefault(a => a.Item.Key == k);
                if (avail != null) Selected.Add(new ParamItem(avail.Item));
                else
                {
                    string name = namesForMissing != null && namesForMissing.TryGetValue(k, out string n) ? n : k.ToString();
                    Selected.Add(new ParamItem(new ParameterInfo { Key = k, Name = name, Kind = ValueKind.Text }) { IsMissing = true });
                }
            }
            RefreshAvailable();
            RefreshSelected();
            Changed();
        }

        public void ClearAll()
        {
            Selected.Clear();
            RefreshAvailable();
            RefreshSelected();
            Changed();
        }

        /// <summary>Parameters to export, in user order, excluding missing ones.</summary>
        public List<ParameterInfo> SelectedForExport() => Selected.Where(s => !s.IsMissing).Select(s => s.Item).ToList();

        private void Add(List<ParamItem> items)
        {
            var have = new HashSet<ParamKey>(Selected.Select(s => s.Item.Key));
            foreach (var i in items)
                if (have.Add(i.Item.Key)) Selected.Add(new ParamItem(i.Item));
            foreach (var i in items) i.IsSelected = false;
            RefreshAvailable();
            RefreshSelected();
            Changed();
        }

        private void Remove(List<ParamItem> items)
        {
            foreach (var i in items) Selected.Remove(i);
            RefreshAvailable();
            RefreshSelected();
            Changed();
        }

        private void Move(int delta)
        {
            var sel = Selected.Where(s => s.IsSelected).ToList();
            if (sel.Count == 0) return;
            if (delta == int.MinValue || delta == int.MaxValue)
            {
                foreach (var s in sel) Selected.Remove(s);
                if (delta == int.MinValue) for (int i = sel.Count - 1; i >= 0; i--) Selected.Insert(0, sel[i]);
                else foreach (var s in sel) Selected.Add(s);
            }
            else
            {
                var ordered = delta < 0 ? sel.OrderBy(s => Selected.IndexOf(s)).ToList() : sel.OrderByDescending(s => Selected.IndexOf(s)).ToList();
                foreach (var s in ordered)
                {
                    int i = Selected.IndexOf(s), j = i + delta;
                    if (j < 0 || j >= Selected.Count || Selected[j].IsSelected) continue;
                    Selected.Move(i, j);
                }
            }
            RefreshSelected();
            Changed();
        }

        private void SortSelected()
        {
            var sorted = Selected.OrderBy(s => s.Item.Scope).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Selected.Clear();
            foreach (var s in sorted) Selected.Add(s);
            RefreshSelected();
            Changed();
        }

        private bool PassesFilters(ParamItem i)
        {
            var p = i.Item;
            if (i.IsType && !ShowType) return false;
            if (!i.IsType && !ShowInstance) return false;
            if (i.IsReadOnly && !ShowReadOnly) return false;
            if (CommonOnly && !p.IsCommon) return false;
            return Matches(i.Name, SearchAvailable);
        }

        private static bool Matches(string text, string search)
            => string.IsNullOrWhiteSpace(search) || (text ?? "").IndexOf(search.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;

        private void RefreshAvailable()
        {
            var selectedKeys = new HashSet<ParamKey>(Selected.Select(s => s.Item.Key));
            VisibleAvailable.Clear();
            foreach (var i in _available)
                if (!selectedKeys.Contains(i.Item.Key) && PassesFilters(i)) VisibleAvailable.Add(i);
            OnPropertyChanged(nameof(AvailableCount));
            AddCommand.Refresh(); AddAllCommand.Refresh();
        }

        private void RefreshSelected()
        {
            VisibleSelected.Clear();
            foreach (var s in Selected) if (Matches(s.Name, SearchSelected)) VisibleSelected.Add(s);
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(MissingCount));
            RemoveCommand.Refresh(); RemoveAllCommand.Refresh();
        }

        /// <summary>Called by the view when list selection changes so commands re-evaluate.</summary>
        public void NotifySelectionChanged()
        {
            AddCommand.Refresh(); RemoveCommand.Refresh();
        }

        private void Changed() => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A checkable list with search, select all / none and "hide unchecked".</summary>
    public sealed class CheckListViewModel<T> : ObservableObject
    {
        private readonly List<Selectable<T>> _all = new List<Selectable<T>>();
        private readonly Func<T, string> _text;
        private readonly Func<T, string> _key;
        private string _search = string.Empty;
        private bool _hideUnchecked;
        private bool _bulk;

        public CheckListViewModel(Func<T, string> text, Func<T, string> key)
        {
            _text = text; _key = key;
            CheckAllCommand = new RelayCommand(() => SetAll(true));
            CheckNoneCommand = new RelayCommand(() => SetAll(false));
        }

        public ObservableCollection<Selectable<T>> Visible { get; } = new ObservableCollection<Selectable<T>>();
        public RelayCommand CheckAllCommand { get; }
        public RelayCommand CheckNoneCommand { get; }
        public string Search { get => _search; set { if (Set(ref _search, value ?? "")) Refresh(); } }
        public bool HideUnchecked { get => _hideUnchecked; set { if (Set(ref _hideUnchecked, value)) Refresh(); } }
        public int CheckedCount => _all.Count(x => x.IsChecked);
        public int Count => _all.Count;
        public IEnumerable<Selectable<T>> All => _all;

        /// <summary>Header checkbox state: true (all visible checked), false (none), null (some).</summary>
        public bool? AllChecked
        {
            get
            {
                if (Visible.Count == 0) return false;
                int c = Visible.Count(v => v.IsChecked);
                return c == 0 ? false : c == Visible.Count ? true : (bool?)null;
            }
            set => SetAll(value == true);
        }

        public event EventHandler CheckedChanged;

        public IEnumerable<T> Checked => _all.Where(x => x.IsChecked).Select(x => x.Item);

        /// <summary>Replaces the items, keeping checks by key.</summary>
        public void SetItems(IEnumerable<T> items, ISet<string> checkedKeys = null)
        {
            var keep = checkedKeys ?? new HashSet<string>(_all.Where(x => x.IsChecked).Select(x => _key(x.Item)), StringComparer.Ordinal);
            foreach (var x in _all) x.CheckedChanged -= OnItemChecked;
            _all.Clear();
            foreach (var i in items)
            {
                var s = new Selectable<T>(i);
                s.SetCheckedSilently(keep.Contains(_key(i)));
                s.CheckedChanged += OnItemChecked;
                _all.Add(s);
            }
            Refresh();
        }

        public void CheckKeys(ISet<string> keys)
        {
            _bulk = true;
            foreach (var x in _all) x.SetCheckedSilently(keys.Contains(_key(x.Item)));
            _bulk = false;
            Refresh();
            Raise();
        }

        private void SetAll(bool value)
        {
            _bulk = true;
            foreach (var x in Visible) x.SetCheckedSilently(value);
            _bulk = false;
            Refresh();
            Raise();
        }

        private void OnItemChecked(object sender, EventArgs e)
        {
            if (_bulk) return;
            if (HideUnchecked) Refresh(); else { OnPropertyChanged(nameof(AllChecked)); OnPropertyChanged(nameof(CheckedCount)); }
            Raise();
        }

        private void Raise() => CheckedChanged?.Invoke(this, EventArgs.Empty);

        public void Refresh()
        {
            Visible.Clear();
            foreach (var x in _all)
            {
                if (HideUnchecked && !x.IsChecked) continue;
                if (!string.IsNullOrWhiteSpace(Search) && (_text(x.Item) ?? "").IndexOf(Search.Trim(), StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                Visible.Add(x);
            }
            OnPropertyChanged(nameof(AllChecked));
            OnPropertyChanged(nameof(CheckedCount));
            OnPropertyChanged(nameof(Count));
        }
    }
}
