using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CausaFix.Addin.Bootstrap;
using CausaFix.Addin.Shared;
using CausaFix.Core.Settings;
using CausaFix.Keynotes.Files;
using CausaFix.Keynotes.ViewModels;
using CausaFix.Revit;
using CausaFix.Revit.Keynotes;

namespace CausaFix.Addin.Modules.Keynotes
{
    /// <summary>Fourth tool: Keynote Manager, in the "Documentation" panel after Project Cleanup.</summary>
    public sealed class KeynoteManagerModule : ICausaFixModule
    {
        public string Id => "CausaFix.Keynotes";
        public string Panel => "Documentation";

        public IEnumerable<CommandSpec> Commands => new[]
        {
            new CommandSpec
            {
                Name = "CausaFix.KeynoteManager",
                Text = "Keynote\nManager",
                Tooltip = "Crear y editar las keynotes de Revit: grupos, subgrupos, notas y su texto; guardar y recargar.",
                LongDescription = "Editor del archivo de keynotes del modelo (formato TXT nativo de Revit): grupos y subgrupos, nuevas notas, edición de texto, cambio de clave con actualización de referencias, importación y exportación, búsqueda y filtros, uso en el proyecto y colocación de notas de usuario, elemento o material. Guarda el archivo de forma segura (copia previa y detección de cambios ajenos) y recarga la tabla en Revit verificando lo cargado. No guarda ni sincroniza el modelo.",
                CommandType = typeof(KeynoteManagerCommand),
                AvailabilityType = typeof(ProjectDocumentAvailability),
                IconGlyph = "KN",
                IconColor = "#6A4C93"
            }
        };
    }

    /// <summary>Opens the modeless Keynote Manager window (one per session), bound to the document it was opened for.</summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class KeynoteManagerCommand : IExternalCommand
    {
        private static KeynoteManagerWindow _window;

        public static KeynoteFileStore CreateStore()
        {
            string local = Path.Combine(SettingsStore.DefaultLocalRoot, "Keynotes");
            return new KeynoteFileStore(Path.Combine(local, "Backups"), Path.Combine(local, "Drafts"),
                Path.Combine(SettingsStore.DefaultRoot, "Keynotes", "templates.json"), m => Services.Log.Info(m));
        }

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
                Services.Log.Info("Keynote Manager: command start (" + Services.LoadedBuild + ", Revit " + commandData.Application.Application.VersionNumber + ")");
                var dispatcher = new RevitDispatcher();
                var service = new KeynoteService(dispatcher) { Trace = m => Services.Log.Info(m) };
                var window = new KeynoteManagerWindow();
                var vm = new KeynoteManagerViewModel(service, new KeynoteDialogs(window), CreateStore(), m => Services.Log.Info(m));
                window.DataContext = vm;
                Services.SetOwner(window, commandData.Application);

                string title = commandData.Application.ActiveUIDocument?.Document?.Title ?? string.Empty;
                window.Title = "CausaFixIt · Keynote Manager — " + title;
                Action<string> onClosing = null;
                onClosing = closing =>
                {
                    if (!string.Equals(closing, title, StringComparison.Ordinal)) return;
                    // The document goes away: close without asking; pending edits stay in the local draft and are offered next time.
                    window.Dispatcher.BeginInvoke(new Action(() => { window.ForceClose = true; window.Close(); }));
                };
                App.DocumentClosing += onClosing;
                window.Closed += (s, e) =>
                {
                    App.DocumentClosing -= onClosing;
                    vm.Detach();
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
                Services.Log.Error("Keynote Manager failed to open", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
