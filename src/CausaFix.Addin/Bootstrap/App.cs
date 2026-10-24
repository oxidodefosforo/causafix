using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using CausaFix.Addin.Shared;

namespace CausaFix.Addin.Bootstrap
{
    /// <summary>Revit entry point (registered in CausaFix.addin). Builds the CausaFix ribbon tab from the module registry.</summary>
    public sealed class App : IExternalApplication
    {
        /// <summary>Raised when a document is about to close (title). Open tool windows bound to it react.</summary>
        public static event Action<string> DocumentClosing;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                Services.Log.Info("CausaFix " + Services.Version + " starting in Revit " + app.ControlledApplication.VersionNumber);
                BuildRibbon(app);
                app.ControlledApplication.DocumentClosing += (s, e) =>
                {
                    try { DocumentClosing?.Invoke(e.Document?.Title ?? string.Empty); }
                    catch (Exception ex) { Services.Log.Error("DocumentClosing handler", ex); }
                };
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Services.Log.Error("Startup failed", ex);
                TaskDialog.Show("CausaFix", "CausaFix could not start: " + ex.Message + "\n\nLog: " + Services.Log.CurrentFile);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            Services.Log.Info("CausaFix shutting down");
            return Result.Succeeded;
        }

        private static void BuildRibbon(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(ModuleRegistry.TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab already exists (another CausaFix load) */ }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            var panels = new Dictionary<string, RibbonPanel>(StringComparer.Ordinal);
            foreach (var existing in app.GetRibbonPanels(ModuleRegistry.TabName)) panels[existing.Name] = existing;

            foreach (var module in ModuleRegistry.Modules)
            {
                if (!panels.TryGetValue(module.Panel, out RibbonPanel panel))
                {
                    panel = app.CreateRibbonPanel(ModuleRegistry.TabName, module.Panel);
                    panels[module.Panel] = panel;
                }
                var small = new List<PushButtonData>();
                foreach (var spec in module.Commands)
                {
                    var data = new PushButtonData(spec.Name, spec.Text, assemblyPath, spec.CommandType.FullName)
                    {
                        ToolTip = spec.Tooltip,
                        LongDescription = spec.LongDescription,
                        LargeImage = Icons.Create(spec.IconGlyph, spec.IconColor, 32),
                        Image = Icons.Create(spec.IconGlyph, spec.IconColor, 16)
                    };
                    if (spec.AvailabilityType != null) data.AvailabilityClassName = spec.AvailabilityType.FullName;
                    if (spec.Large) panel.AddItem(data);
                    else small.Add(data);
                }
                // Small buttons are stacked in groups of up to three.
                for (int i = 0; i < small.Count; i += 3)
                {
                    var group = small.Skip(i).Take(3).ToList();
                    if (group.Count == 1) panel.AddItem(group[0]);
                    else if (group.Count == 2) panel.AddStackedItems(group[0], group[1]);
                    else panel.AddStackedItems(group[0], group[1], group[2]);
                }
            }
        }
    }

    /// <summary>Commands are available only with a project document open.</summary>
    public sealed class ProjectDocumentAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, Autodesk.Revit.DB.CategorySet selectedCategories)
        {
            var doc = app?.ActiveUIDocument?.Document;
            return doc != null && !doc.IsFamilyDocument;
        }
    }

    /// <summary>Always available (Settings, About).</summary>
    public sealed class AlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, Autodesk.Revit.DB.CategorySet selectedCategories) => true;
    }
}
