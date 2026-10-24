using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CausaFix.Addin.Bootstrap;
using CausaFix.Addin.Shared;
using CausaFix.ProjectSetup.ViewModels;
using CausaFix.Revit;
using CausaFix.Revit.ProjectSetup;

namespace CausaFix.Addin.Modules.ProjectSetup
{
    /// <summary>Registers the Project Setup button in the "Documentation" panel.</summary>
    public sealed class ProjectSetupModule : ICausaFixModule
    {
        public string Id => "CausaFix.ProjectSetup";
        public string Panel => "Documentation";

        public IEnumerable<CommandSpec> Commands => new[]
        {
            new CommandSpec
            {
                Name = "CausaFix.ProjectSetup",
                Text = "Project\nSetup",
                Tooltip = "Create plan views per level with templates and names, create sheets, and place the views so a building reference lands on the same point of every sheet.",
                LongDescription = "Levels → View Sets → Sheets (rules, Sheet Name, Views on Sheet) → Numbering → Placement (alignment, view titles) → Review → Run Setup. Everything is a draft until Run Setup; the run is atomic (one Undo).",
                CommandType = typeof(ProjectSetupCommand),
                AvailabilityType = typeof(ProjectDocumentAvailability),
                IconGlyph = "PS",
                IconColor = "#5B4B8A"
            }
        };
    }

    /// <summary>Opens the modeless Project Setup window (one per session). ExternalEvent is created in API context here.</summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class ProjectSetupCommand : IExternalCommand
    {
        private static ProjectSetupWindow _window;

        public static string ProfilesFolder => Path.Combine(Core.Settings.SettingsStore.DefaultRoot, "ProjectSetup", "Profiles");

        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                if (_window != null)
                {
                    if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                    _window.Activate();
                    return Result.Succeeded;
                }
                SetupCatalogReader.Trace = m => Services.Log.Info("Project Setup " + m);
                Services.Log.Info("Project Setup: command start (CausaFix " + Services.Build + ", built " + System.IO.File.GetLastWriteTime(typeof(ProjectSetupCommand).Assembly.Location).ToString("yyyy-MM-dd HH:mm") + ", Revit " + commandData.Application.Application.VersionNumber + ")");
                var dispatcher = new RevitDispatcher();
                var service = new ProjectSetupService(dispatcher);
                var window = new ProjectSetupWindow();
                var vm = new ProjectSetupViewModel(service, new WpfDialogs(window), ProfilesFolder, Services.Paths.LogsFolder);
                window.DataContext = vm;
                Services.SetOwner(window, commandData.Application);

                string title = commandData.Application.ActiveUIDocument?.Document?.Title ?? string.Empty;
                Action<string> onClosing = null;
                onClosing = closing =>
                {
                    if (!string.Equals(closing, title, StringComparison.Ordinal)) return;
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        window.ForceClose = true;
                        MessageBox.Show(window, "The model '" + closing + "' is closing. Project Setup will close; the unsaved configuration is discarded (save a profile to keep it).", "CausaFix Project Setup");
                        window.Close();
                    }));
                };
                App.DocumentClosing += onClosing;
                window.Closed += (s, e) =>
                {
                    App.DocumentClosing -= onClosing;
                    service.Dispose();
                    _window = null;
                };
                _window = window;
                window.Show();
                window.Initialize();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Services.Log.Error("Project Setup failed to open", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
