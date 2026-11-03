using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CausaFix.Addin.Bootstrap;
using CausaFix.Addin.Shared;
using CausaFix.ProjectCleanup.ViewModels;
using CausaFix.Revit;
using CausaFix.Revit.ProjectCleanup;

namespace CausaFix.Addin.Modules.ProjectCleanup
{
    /// <summary>Third tool: Project Cleanup, in the "Documentation" panel after Project Setup.</summary>
    public sealed class ProjectCleanupModule : ICausaFixModule
    {
        public string Id => "CausaFix.ProjectCleanup";
        public string Panel => "Documentation";

        public IEnumerable<CommandSpec> Commands => new[]
        {
            new CommandSpec
            {
                Name = "CausaFix.ProjectCleanup",
                Text = "Project\nCleanup",
                Tooltip = "Eliminar documentación conservando la vista activa.",
                LongDescription = "Analiza vistas, planos, schedules, leyendas y panel schedules del documento activo, protege la vista activa y lo que necesita (primaria, contenido del plano activo), muestra la revisión y elimina el resto en una sola operación atómica (un Deshacer). No toca plantillas, niveles, ejes, elementos del modelo, familias, vínculos, paneles ni circuitos, y no guarda el modelo.",
                CommandType = typeof(ProjectCleanupCommand),
                AvailabilityType = typeof(ProjectDocumentAvailability),
                IconGlyph = "PC",
                IconColor = "#B03A2E"
            }
        };
    }

    /// <summary>Opens the modeless Project Cleanup window (one per session). ExternalEvent is created in API context here.</summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class ProjectCleanupCommand : IExternalCommand
    {
        private static ProjectCleanupWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                if (_window != null)
                {
                    if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                    _window.Activate();
                    // The active view may have changed: the open review is rebuilt for the current one.
                    _window.Initialize();
                    return Result.Succeeded;
                }
                Services.Log.Info("Project Cleanup: command start (" + Services.LoadedBuild + ", Revit " + commandData.Application.Application.VersionNumber + ")");
                var dispatcher = new RevitDispatcher();
                var service = new ProjectCleanupService(dispatcher) { Trace = m => Services.Log.Info(m) };
                var window = new ProjectCleanupWindow();
                var vm = new ProjectCleanupViewModel(service, new WpfDialogs(window), Services.Paths.ReportsFolder, m => Services.Log.Info(m));
                window.DataContext = vm;
                Services.SetOwner(window, commandData.Application);

                string title = commandData.Application.ActiveUIDocument?.Document?.Title ?? string.Empty;
                Action<string> onClosing = null;
                onClosing = closing =>
                {
                    if (!string.Equals(closing, title, StringComparison.Ordinal)) return;
                    window.Dispatcher.BeginInvoke(new Action(() => { window.ForceClose = true; window.Close(); }));
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
                Services.Log.Error("Project Cleanup failed to open", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
