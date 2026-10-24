using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using CausaFix.Addin.Modules.ProjectSetup;
using CausaFix.Core.Tests;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Ui.Tests
{
    /// <summary>
    /// Clicks the real Project Setup window with the real mouse (SendInput): WPF hit testing, routed events, focus and
    /// bindings run exactly as for a user. Revit is replaced by the in-memory gateway of the core tests.
    /// </summary>
    public static class Program
    {
        private static int _fails, _passes;
        private static readonly List<string> Report = new List<string>();

        [STAThread]
        public static int Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Console.WriteLine("UNHANDLED " + e.ExceptionObject); Environment.Exit(1); };
            Dispatcher.CurrentDispatcher.UnhandledException += (s, e) => { Console.WriteLine("UI EXCEPTION " + e.Exception); _fails++; e.Handled = true; };
            try
            {
                if (!OldTemplateConfirmsCause()) { Print(); Console.WriteLine("NOT EXECUTABLE: the mouse input did not reach the window (no interactive desktop)."); return 2; }
                ProjectSetupWindowRuleList();
            }
            catch (Exception ex) { Fail("exception: " + ex); }
            Print();
            return _fails == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ the cause, with the template used before the fix

        private sealed class OldRule : INotifyPropertyChanged
        {
            private bool _enabled = true;
            public string Name { get; set; }
            public string Family { get; set; } = "FloorPlan";
            public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled))); } }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        /// <summary>
        /// The ItemTemplate of commit b27fa6e (a CheckBox whose content is the name/family text). Returns false when the
        /// click produced no effect at all (input not delivered: the check cannot run on this machine).
        /// </summary>
        private static bool OldTemplateConfirmsCause()
        {
            const string xaml = @"<ListBox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' BorderThickness='0' HorizontalContentAlignment='Stretch'>
  <ListBox.ItemTemplate><DataTemplate>
    <CheckBox IsChecked='{Binding Enabled}'>
      <TextBlock><Run Text='{Binding Name, Mode=OneWay}' FontWeight='SemiBold' /><Run Text='  ' /><Run Text='{Binding Family, Mode=OneWay}' Foreground='#6B7680' FontSize='10' /></TextBlock>
    </CheckBox>
  </DataTemplate></ListBox.ItemTemplate>
</ListBox>";
            var items = new ObservableCollection<OldRule> { new OldRule { Name = "LIGHTING" }, new OldRule { Name = "POWER" }, new OldRule { Name = "CONTAINMENT" } };
            var lb = (ListBox)XamlReader.Parse(xaml);
            lb.ItemsSource = items;
            lb.SelectedIndex = 0;
            var w = new Window { Content = lb, Left = 20, Top = 20, Width = 320, Height = 240, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual, Title = "old template" };
            w.Show(); w.Activate(); Pump(600);
            try
            {
                var item = (ListBoxItem)lb.ItemContainerGenerator.ContainerFromIndex(1);
                var text = Find<TextBlock>(item, t => true);
                Click(text, new Point(6, text.ActualHeight / 2));
                bool toggled = !items[1].Enabled;
                bool selected = lb.SelectedIndex == 1;
                // Free space at the right of the row (the CheckBox is stretched across the row).
                var item2 = (ListBoxItem)lb.ItemContainerGenerator.ContainerFromIndex(2);
                Click(item2, new Point(item2.ActualWidth - 10, item2.ActualHeight / 2));
                bool toggled2 = !items[2].Enabled;
                bool selected2 = lb.SelectedIndex == 2;
                Report.Add("Old template, click on POWER text: selected=" + selected + ", Enabled toggled=" + toggled +
                           " · click on free space of CONTAINMENT: selected=" + selected2 + ", Enabled toggled=" + toggled2);
                if (!toggled && !selected && !toggled2 && !selected2) return false;
                if (toggled && !selected) Report.Add("  → cause confirmed: the CheckBox handles MouseLeftButtonDown (toggles) and the ListBoxItem never selects.");
                else Report.Add("  → the old template did not reproduce the fault on this machine.");
                return true;
            }
            finally { w.Close(); Pump(100); }
        }

        // ------------------------------------------------------------------ the fixed window

        private static void ProjectSetupWindowRuleList()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-ui-" + Guid.NewGuid().ToString("N"));
            var gw = new FakeSetupGateway();
            var vm = new ProjectSetupViewModel(gw, new FakeSetupDialogs(), Path.Combine(dir, "p"), Path.Combine(dir, "l"));
            vm.InitializeAsync().GetAwaiter().GetResult();
            foreach (var l in vm.Levels) l.Selected = true;
            string[] names = { "LIGHTING", "POWER", "CONTAINMENT", "RCP" };
            while (vm.ViewSets.Count < 4) vm.AddSetCommand.Execute(null);
            for (int i = 0; i < 4; i++) vm.ViewSets[i].Name = names[i];
            vm.ViewSets[3].Family = PlanFamily.CeilingPlan;
            vm.ViewSets[3].Enabled = false;                       // a disabled rule must stay selectable and editable
            vm.SelectedSet = vm.ViewSets[0];
            vm.Step = 1;

            var win = new ProjectSetupWindow { DataContext = vm, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, Topmost = true };
            win.Show(); win.Activate(); Pump(1200);
            try
            {
                var list = (ListBox)win.FindName("ViewSetList");
                var nameBox = (TextBox)win.FindName("SetNameBox");
                Check(list != null && nameBox != null, "View Sets list and editor found");
                ViewSetItem Rule(string n) => vm.ViewSets.First(v => v.Rule.Name.StartsWith(n, StringComparison.Ordinal));
                ListBoxItem Row(ViewSetItem it) => (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(it);
                TextBlock Text(ViewSetItem it) => Find<TextBlock>(Row(it), t => t.Name == "RuleText");
                CheckBox Box(ViewSetItem it) => Find<CheckBox>(Row(it), c => c.Name == "EnabledBox");

                int selChanges = 0;
                vm.PropertyChanged += (o, e) => { if (e.PropertyName == nameof(ProjectSetupViewModel.SelectedSet)) selChanges++; };

                // 1. Click on the name selects the rule, keeps Enabled and updates the editor.
                foreach (var n in new[] { "POWER", "LIGHTING", "CONTAINMENT", "RCP" })
                {
                    var it = Rule(n);
                    bool enabled = it.Enabled;
                    selChanges = 0;
                    Click(Text(it), new Point(8, Text(it).ActualHeight / 2));
                    Check(ReferenceEquals(vm.SelectedSet, it) && Row(it).IsSelected, "click on the text of " + n + " selects it");
                    Check(it.Enabled == enabled, "selecting " + n + " keeps Enabled=" + enabled);
                    Check(nameBox.Text == n, "editor shows " + n + " (was '" + nameBox.Text + "')");
                    Check(selChanges == 1, "one selection change for " + n + " (" + selChanges + ")");
                }

                // 2. Click on the family text and on the free space of the row.
                var power = Rule("POWER");
                var t = Text(power);
                Click(t, new Point(t.ActualWidth - 4, t.ActualHeight / 2));
                Check(ReferenceEquals(vm.SelectedSet, power) && power.Enabled, "click on the family text selects POWER, Enabled unchanged");
                var cont = Rule("CONTAINMENT");
                Click(Row(cont), new Point(Row(cont).ActualWidth - 6, Row(cont).ActualHeight / 2));
                Check(ReferenceEquals(vm.SelectedSet, cont) && cont.Enabled, "click on the free space selects CONTAINMENT, Enabled unchanged");

                // 3. Selected row is visually distinct.
                Brush Bg(ListBoxItem r) => Find<Border>(r, b => true)?.Background;
                Check(!SameBrush(Bg(Row(cont)), Bg(Row(power))), "selected row has a different background");

                // 4. The box toggles exactly once and selects the row being manipulated.
                int toggles = 0;
                power.PropertyChanged += (o, e) => { if (e.PropertyName == nameof(ViewSetItem.Enabled)) toggles++; };
                selChanges = 0;
                Click(Box(power));
                Check(!power.Enabled && toggles == 1, "one click on POWER's box: Enabled false, one change (" + toggles + ")");
                Check(ReferenceEquals(vm.SelectedSet, power), "the box selects the row it belongs to");
                Check(selChanges == 1, "one selection change (" + selChanges + ")");
                Click(Box(power));
                Check(power.Enabled && toggles == 2, "second click: Enabled true again (" + toggles + " changes)");
                Check(ReferenceEquals(vm.SelectedSet, power), "selection stays on POWER");

                // 5. A disabled rule is selectable and editable; switching rows commits the pending edit.
                var rcp = Rule("RCP");
                Click(Text(rcp), new Point(8, Text(rcp).ActualHeight / 2));
                Check(ReferenceEquals(vm.SelectedSet, rcp) && !rcp.Enabled, "disabled RCP selected, still disabled");
                Click(nameBox);
                nameBox.SelectAll();
                nameBox.Text = "RCP-EDITED";                          // pending: the binding updates on LostFocus
                var lighting = Rule("LIGHTING");
                Click(Text(lighting), new Point(8, Text(lighting).ActualHeight / 2));
                Check(vm.Config.ViewSets.Any(r => r.Name == "RCP-EDITED"), "edit of the disabled rule committed when switching rows");
                Check(ReferenceEquals(vm.SelectedSet, lighting) && nameBox.Text == "LIGHTING", "LIGHTING selected after the edit");

                // 6. Pending edit, then a click on another row's box: edit stays on its rule, the other toggles once.
                Click(nameBox);
                nameBox.SelectAll();
                nameBox.Text = "LIGHTING-2";
                toggles = 0;
                Click(Box(power));
                Check(vm.Config.ViewSets.Any(r => r.Name == "LIGHTING-2"), "pending edit committed to LIGHTING before the selection moved");
                Check(toggles == 1 && !power.Enabled, "POWER toggled once (" + toggles + ")");
                Check(ReferenceEquals(vm.SelectedSet, power) && nameBox.Text == "POWER", "POWER selected and shown");

                // 7. Keyboard: arrows move the selection, Enabled unchanged.
                Click(Text(power), new Point(8, Text(power).ActualHeight / 2));
                int idx = list.SelectedIndex;
                Key(0x28);  // Down
                Check(list.SelectedIndex == idx + 1 && ReferenceEquals(vm.SelectedSet, vm.ViewSets[idx + 1]), "Down arrow selects the next rule");
                Key(0x26);  // Up
                Check(list.SelectedIndex == idx && !power.Enabled, "Up arrow back, Enabled unchanged");

                // 8. Rebuilding the editors keeps the selection by identity.
                string key = vm.SelectedSet.Rule.Key;
                vm.RefreshCommand.ExecuteAsync().GetAwaiter().GetResult();
                Pump(300);
                Check(vm.SelectedSet?.Rule.Key == key && Row(vm.SelectedSet).IsSelected, "selection kept by key after the editors are rebuilt");
            }
            finally
            {
                win.ForceClose = true; win.Close(); Pump(100);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static bool SameBrush(Brush a, Brush b) => a is SolidColorBrush x && b is SolidColorBrush y ? x.Color == y.Color : ReferenceEquals(a, b);

        private static void Check(bool ok, string what) { if (ok) { _passes++; Report.Add("  PASS  " + what); } else Fail(what); }
        private static void Fail(string what) { _fails++; Report.Add("  FAIL  " + what); }
        private static void Print()
        {
            foreach (var l in Report) Console.WriteLine(l);
            Console.WriteLine(_passes + " passed, " + _fails + " failed (real WPF window, real mouse/keyboard input)");
        }

        private static T Find<T>(DependencyObject root, Func<T, bool> pred) where T : DependencyObject
        {
            if (root == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t && pred(t)) return t;
                var r = Find(c, pred);
                if (r != null) return r;
            }
            return null;
        }

        private static void Pump(int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            do
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Thread.Sleep(10);
            } while (DateTime.UtcNow < end);
        }

        private static void Click(FrameworkElement el, Point? at = null)
        {
            el.UpdateLayout();
            var p = el.PointToScreen(at ?? new Point(el.ActualWidth / 2, el.ActualHeight / 2));
            Native.SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
            Pump(80);
            Native.Mouse(Native.MOUSEEVENTF_LEFTDOWN);
            Pump(60);
            Native.Mouse(Native.MOUSEEVENTF_LEFTUP);
            Pump(250);
        }

        private static void Key(ushort vk)
        {
            Native.Key(vk, false); Pump(40);
            Native.Key(vk, true); Pump(250);
        }
    }

    internal static class Native
    {
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

        public static void Mouse(uint flags)
        {
            var i = new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } };
            SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());
        }

        public static void Key(ushort vk, bool up)
        {
            var i = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };
            SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());
        }
    }
}
