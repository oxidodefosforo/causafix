using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CausaFix.Addin.Bootstrap;
using CausaFix.Core.Settings;
using CausaFix.ExcelLink;

namespace CausaFix.Addin.Shared
{
    /// <summary>Services shared by every CausaFix module: paths, settings, log, version.</summary>
    public static class Services
    {
        public static readonly ModulePaths Paths = ModulePaths.Default();
        public static readonly FileLog Log = new FileLog(Paths.LogsFolder);

        public static string Version => typeof(Services).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        public static string InstallFolder => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public static CausaFixSettings LoadSettings() => new SettingsStore(Paths.SettingsFile).Load();
        public static void SaveSettings(CausaFixSettings s) => new SettingsStore(Paths.SettingsFile).Save(s);

        /// <summary>Makes a WPF window a child of the Revit main window (stays on top of Revit, minimises with it).</summary>
        public static void SetOwner(Window w, UIApplication app)
        {
            try { new WindowInteropHelper(w).Owner = app.MainWindowHandle; }
            catch (Exception ex) { Log.Warn("Could not set window owner: " + ex.Message); }
        }

        public static void OpenWithShell(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Could not open " + path + ":\n" + ex.Message, "CausaFix"); }
        }
    }

    /// <summary>Ribbon icons drawn at runtime (no image files to ship): rounded tile + short glyph.</summary>
    public static class Icons
    {
        public static ImageSource Create(string glyph, string color, int size)
        {
            var visual = new DrawingVisual();
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color ?? "#0E7C86"));
            brush.Freeze();
            using (var dc = visual.RenderOpen())
            {
                double r = size * 0.18;
                dc.DrawRoundedRectangle(brush, null, new Rect(0.5, 0.5, size - 1, size - 1), r, r);
                // accent stripe
                var accent = new SolidColorBrush(Color.FromRgb(0xF2, 0xB5, 0x3A));
                accent.Freeze();
                dc.DrawRectangle(accent, null, new Rect(size * 0.12, size * 0.78, size * 0.76, Math.Max(1, size * 0.07)));
                var text = new FormattedText(glyph ?? "CF", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    size * (glyph != null && glyph.Length > 2 ? 0.34 : 0.44), Brushes.White, 1.0);
                dc.DrawText(text, new Point((size - text.Width) / 2, (size * 0.86 - text.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }

    /// <summary>Settings and About buttons (suite-level, not part of any tool).</summary>
    public sealed class SuiteModule : ICausaFixModule
    {
        public string Id => "CausaFix.Suite";
        public string Panel => "CausaFix";
        public System.Collections.Generic.IEnumerable<CommandSpec> Commands => new[]
        {
            new CommandSpec { Name = "CausaFix.Settings", Text = "Settings", Tooltip = "CausaFix settings (number parsing, apply mode, folders).",
                              CommandType = typeof(SettingsCommand), AvailabilityType = typeof(AlwaysAvailable), IconGlyph = "S", IconColor = "#4A5A6A", Large = false },
            new CommandSpec { Name = "CausaFix.About", Text = "About", Tooltip = "Version, install folder and logs.",
                              CommandType = typeof(AboutCommand), AvailabilityType = typeof(AlwaysAvailable), IconGlyph = "i", IconColor = "#4A5A6A", Large = false }
        };
    }

    [Transaction(TransactionMode.ReadOnly)]
    public sealed class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var settings = Services.LoadSettings();
            var dialogs = new WpfDialogs(null);
            int dec = dialogs.Choose("CausaFix settings", "Numbers typed as TEXT in Excel cells (numeric cells are unaffected). Current: " + settings.DecimalMode,
                new[] { "Auto – reject ambiguous input such as 1,234", "Point is decimal (1,234.5)", "Comma is decimal (1.234,5)" });
            if (dec >= 0) settings.DecimalMode = (Core.Values.DecimalSeparatorMode)dec;
            int mode = dialogs.Choose("CausaFix settings", "Default apply mode. Current: " + settings.DefaultApplyMode,
                new[] { "All or nothing (recommended)", "Apply valid changes only" });
            if (mode >= 0) settings.DefaultApplyMode = (Core.Reports.ApplyMode)mode;
            int open = dialogs.Choose("CausaFix settings", "Open the workbook after exporting? Current: " + (settings.OpenFileAfterExport ? "Yes" : "No"),
                new[] { "Yes", "No" });
            if (open >= 0) settings.OpenFileAfterExport = open == 0;
            Services.SaveSettings(settings);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var td = new TaskDialog("About CausaFix")
            {
                MainInstruction = "CausaFix " + Services.Version,
                MainContent = "Tools for Revit. Excel Link: exchange element, type, schedule and spatial data with local .xlsx files, with a review step before anything is written.\n\n" +
                              "Install folder: " + Services.InstallFolder + "\nProfiles: " + Services.Paths.ProfilesFolder + "\nReports: " + Services.Paths.ReportsFolder +
                              "\nLogs: " + Services.Paths.LogsFolder,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open logs folder");
            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                Directory.CreateDirectory(Services.Paths.LogsFolder);
                Services.OpenWithShell(Services.Paths.LogsFolder);
            }
            return Result.Succeeded;
        }
    }
}
