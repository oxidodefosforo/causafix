using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using CausaFix.Addin.Shared;
using CausaFix.ExcelLink;

namespace CausaFix.Addin.Modules.ExcelLink
{
    /// <summary>
    /// Preview/Edit grid. Columns are generated per sheet (one template column per parameter, bound to
    /// Cells[i]); per-cell read-only is enforced in BeginningEdit; values are pushed to the view model only
    /// when an edit is committed (Esc cancels).
    /// </summary>
    public partial class PreviewView : UserControl
    {
        private ExcelLinkViewModel _vm;
        private IClipboard _clipboard;
        private readonly Dictionary<DataGridColumn, GridColumn> _columns = new Dictionary<DataGridColumn, GridColumn>();

        public PreviewView()
        {
            InitializeComponent();
        }

        public void Attach(ExcelLinkViewModel vm, IClipboard clipboard)
        {
            _vm = vm;
            _clipboard = clipboard;
            _vm.Preview.ColumnsChanged += (s, e) => BuildColumns();
            BuildColumns();
        }

        private PreviewViewModel Preview => _vm?.Preview;

        private void BuildColumns()
        {
            Table.Columns.Clear();
            _columns.Clear();
            if (Preview?.Current == null) return;
            int frozen = 0;
            foreach (var col in Preview.Columns)
            {
                var c = new DataGridTemplateColumn
                {
                    Header = new TextBlock { Text = col.Header, ToolTip = col.Tooltip, FontWeight = FontWeights.SemiBold },
                    HeaderStyle = HeaderStyle(col),
                    CellTemplate = CellTemplate(col),
                    CellEditingTemplate = col.IsEditable ? EditTemplate(col) : null,
                    SortMemberPath = "Cells[" + col.Index + "].SortKey",
                    CanUserSort = true,
                    IsReadOnly = !col.IsEditable,
                    Width = new DataGridLength(Math.Max(70, Math.Min(260, col.Header.Length * 7.5 + 24))),
                    MinWidth = 40
                };
                _columns[c] = col;
                Table.Columns.Add(c);
                if (col.Def.IsContext && frozen == Table.Columns.Count - 1 && frozen < 3) frozen++;
            }
            Table.FrozenColumnCount = frozen;
        }

        private GridColumn ColumnOf(DataGridColumn c) => c != null && _columns.TryGetValue(c, out var col) ? col : null;

        private static Style HeaderStyle(GridColumn col)
        {
            var s = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            Brush bg = col.Role == CellRole.TypeEditable ? (Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x99))
                     : col.Role == CellRole.InstanceEditable ? new SolidColorBrush(Color.FromRgb(0xC9, 0xE4, 0xB9))
                     : col.Role == CellRole.Context ? new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEA))
                     : new SolidColorBrush(Color.FromRgb(0xF1, 0xB9, 0xBF));
            s.Setters.Add(new Setter(BackgroundProperty, bg));
            s.Setters.Add(new Setter(PaddingProperty, new Thickness(4, 3, 4, 3)));
            s.Setters.Add(new Setter(BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xC8, 0xCE, 0xD3))));
            s.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            s.Setters.Add(new Setter(ToolTipProperty, col.Tooltip));
            return s;
        }

        private const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

        private static DataTemplate CellTemplate(GridColumn col)
        {
            string p = "Cells[" + col.Index + "]";
            string align = col.IsNumeric ? "Right" : "Left";
            string xaml =
                "<DataTemplate " + Ns + ">" +
                "<Border x:Name='b' Padding='4,1' Background='#F5F5F5' ToolTip='{Binding " + p + ".Tooltip}'>" +
                "<TextBlock x:Name='t' Text='{Binding " + p + ".Text, Mode=OneWay}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center' HorizontalAlignment='" + align + "'/>" +
                "</Border>" +
                "<DataTemplate.Triggers>" +
                Role(p, "InstanceEditable", "#E2F0D9") + Role(p, "TypeEditable", "#FFF2CC") +
                Role(p, "InstanceReadOnly", "#F8D7DA") + Role(p, "TypeReadOnly", "#F8D7DA") +
                Role(p, "NotApplicable", "#EDEDED") + Role(p, "Context", "#F5F5F5") +
                "<DataTrigger Binding='{Binding " + p + ".Role}' Value='TypeReadOnly'><Setter TargetName='b' Property='BorderBrush' Value='#E0A800'/><Setter TargetName='b' Property='BorderThickness' Value='3,0,0,0'/></DataTrigger>" +
                "<DataTrigger Binding='{Binding " + p + ".State}' Value='NotApplicable'><Setter TargetName='t' Property='FontStyle' Value='Italic'/><Setter TargetName='t' Property='Foreground' Value='#8C8C8C'/></DataTrigger>" +
                "<DataTrigger Binding='{Binding " + p + ".State}' Value='Edited'><Setter TargetName='b' Property='Background' Value='#D6E4FF'/><Setter TargetName='t' Property='FontWeight' Value='SemiBold'/></DataTrigger>" +
                "<DataTrigger Binding='{Binding " + p + ".State}' Value='Invalid'><Setter TargetName='b' Property='Background' Value='#FFB8B8'/><Setter TargetName='b' Property='BorderBrush' Value='#B00020'/><Setter TargetName='b' Property='BorderThickness' Value='1'/></DataTrigger>" +
                "</DataTemplate.Triggers>" +
                "</DataTemplate>";
            return (DataTemplate)XamlReader.Parse(xaml);
        }

        private static string Role(string p, string role, string color)
            => "<DataTrigger Binding='{Binding " + p + ".Role}' Value='" + role + "'><Setter TargetName='b' Property='Background' Value='" + color + "'/></DataTrigger>";

        private static DataTemplate EditTemplate(GridColumn col)
        {
            string p = "Cells[" + col.Index + "]";
            string xaml =
                "<DataTemplate " + Ns + ">" +
                "<TextBox Text='{Binding " + p + ".Text, Mode=TwoWay, UpdateSourceTrigger=Explicit}' BorderThickness='0' Padding='2,0' " +
                "HorizontalContentAlignment='" + (col.IsNumeric ? "Right" : "Left") + "'/>" +
                "</DataTemplate>";
            return (DataTemplate)XamlReader.Parse(xaml);
        }

        // ------------------------------------------------------------------ editing

        private GridCell CellOf(object rowItem, DataGridColumn column)
        {
            var col = ColumnOf(column);
            return rowItem is GridRow row && col != null ? row.Cells[col.Index] : null;
        }

        private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var cell = CellOf(e.Row.Item, e.Column);
            if (cell == null || !cell.IsEditable)
            {
                e.Cancel = true;
                if (cell != null && Preview != null) Preview.Status = string.IsNullOrEmpty(cell.Message) ? "This cell is read-only." : cell.Message;
            }
        }

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var box = FindChild<TextBox>(e.EditingElement);
            box?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            if (root is T t) return t;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var found = FindChild<T>(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private List<GridCell> SelectedCells()
        {
            var list = new List<GridCell>();
            foreach (var info in Table.SelectedCells)
            {
                var c = CellOf(info.Item, info.Column);
                if (c != null) list.Add(c);
            }
            return list;
        }

        private List<GridRow> SelectedRows()
        {
            var rows = Table.SelectedCells.Select(i => i.Item).OfType<GridRow>().Distinct().ToList();
            if (rows.Count == 0 && Table.CurrentItem is GridRow r) rows.Add(r);
            return rows;
        }

        private void OnSetValue(object sender, RoutedEventArgs e)
        {
            var cells = SelectedCells();
            if (cells.Count == 0) { Preview.Status = "Select cells first."; return; }
            string value = new WpfDialogs(Window.GetWindow(this)).Prompt("Set value", "Value for " + cells.Count(c => c.IsEditable) + " editable cell(s):", cells[0].Text);
            if (value == null) return;
            Table.CommitEdit();
            Preview.SetText(cells, value);
        }

        private void OnClearValue(object sender, RoutedEventArgs e)
        {
            Table.CommitEdit();
            Preview.ClearValues(SelectedCells());
        }

        private void OnRevertSelected(object sender, RoutedEventArgs e)
        {
            Table.CommitEdit();
            Preview.Revert(SelectedCells());
        }

        private void OnCopy(object sender, RoutedEventArgs e) => Copy();
        private void OnPaste(object sender, RoutedEventArgs e) => Paste();

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl && e.Key == Key.C && !IsEditing()) { Copy(); e.Handled = true; }
            else if (ctrl && e.Key == Key.V && !IsEditing()) { Paste(); e.Handled = true; }
        }

        private bool IsEditing() => Keyboard.FocusedElement is TextBox;

        private void Copy()
        {
            if (Preview?.Current == null) return;
            var infos = Table.SelectedCells.ToList();
            if (infos.Count == 0) return;
            var visibleRows = Preview.VisibleRows;
            var rows = infos.Select(i => i.Item).OfType<GridRow>().Distinct().OrderBy(r => visibleRows.IndexOf(r)).ToList();
            var cols = infos.Select(i => i.Column).Distinct().OrderBy(c => c.DisplayIndex).Select(ColumnOf).Where(c => c != null).ToList();
            _clipboard.SetText(PreviewViewModel.Copy(rows, cols));
            Preview.Status = rows.Count + " × " + cols.Count + " cell(s) copied.";
        }

        private void Paste()
        {
            if (Preview?.Current == null) return;
            Table.CommitEdit();
            string text = _clipboard.GetText();
            var start = Table.CurrentCell;
            var row = start.Item as GridRow ?? Table.SelectedCells.Select(i => i.Item).OfType<GridRow>().FirstOrDefault();
            var col = ColumnOf(start.Column) ?? ColumnOf(Table.SelectedCells.Select(i => i.Column).FirstOrDefault());
            if (row == null || col == null) { Preview.Status = "Select a start cell first."; return; }
            Preview.Status = Preview.Paste(text, row, col, SelectedCells());
        }

        // ------------------------------------------------------------------ Revit actions

        private void OnSelectInRevit(object sender, RoutedEventArgs e) => _vm.SelectInRevitCommand.Execute(SelectedRows());
        private void OnShowInRevit(object sender, RoutedEventArgs e) => _vm.ShowInRevitCommand.Execute(SelectedRows());
        private void OnIsolate(object sender, RoutedEventArgs e) => _vm.IsolateCommand.Execute(SelectedRows());
    }
}
