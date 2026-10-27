using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CausaFix.Addin.Bootstrap;
using CausaFix.Addin.Shared;
using CausaFix.ExcelLink;
using CausaFix.Revit;

namespace CausaFix.Addin.Modules.ExcelLink
{
    /// <summary>Registers the Excel Link button in the "Data" panel.</summary>
    public sealed class ExcelLinkModule : ICausaFixModule
    {
        public string Id => "CausaFix.ExcelLink";
        public string Panel => "Data";

        public IEnumerable<CommandSpec> Commands => new[]
        {
            new CommandSpec
            {
                Name = "CausaFix.ExcelLink",
                Text = "Excel\nLink",
                Tooltip = "Exchange parameters of categories, elements, schedules and spatial elements with Excel (.xlsx), or edit them in a table.",
                LongDescription = "Select data, export to Excel, edit, import and review every change (original / current / proposed) before anything is written to the model.",
                CommandType = typeof(ExcelLinkCommand),
                AvailabilityType = typeof(ProjectDocumentAvailability),
                IconGlyph = "XL"
            }
        };
    }

    /// <summary>
    /// Opens the modeless Excel Link window (one per Revit session). The ExternalEvent dispatcher is created
    /// here because ExternalEvent.Create requires a valid API context.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class ExcelLinkCommand : IExternalCommand
    {
        private static ExcelLinkWindow _window;

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

                Services.Log.Info("Excel Link: command start (" + Services.LoadedBuild + ", Revit " + commandData.Application.Application.VersionNumber +
                                  " " + commandData.Application.Application.VersionBuild + ")");
                var dispatcher = new RevitDispatcher();
                var service = new ExcelLinkService(dispatcher);
                var window = new ExcelLinkWindow();
                var vm = new ExcelLinkViewModel(service, new WpfDialogs(window), Services.Paths);
                window.Attach(vm, new WpfClipboard());
                Services.SetOwner(window, commandData.Application);

                Action<string> onClosing = null;
                onClosing = title =>
                {
                    if (!string.Equals(title, vm.DocumentTitle, StringComparison.Ordinal)) return;
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        window.ForceClose = true;
                        MessageBox.Show(window, "The model '" + title + "' is closing. Excel Link will close; pending edits that were not applied are discarded.", "CausaFix Excel Link");
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
                Services.Log.Error("Excel Link failed to open", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
