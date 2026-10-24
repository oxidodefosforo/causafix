using System;
using System.Collections.Generic;
using CausaFix.Addin.Modules.ExcelLink;
using CausaFix.Addin.Shared;

namespace CausaFix.Addin.Bootstrap
{
    /// <summary>A ribbon button of a module.</summary>
    public sealed class CommandSpec
    {
        /// <summary>Unique internal name (e.g. "CausaFix.ExcelLink").</summary>
        public string Name { get; set; }
        /// <summary>Button text (may contain \n for two lines).</summary>
        public string Text { get; set; }
        public string Tooltip { get; set; }
        public string LongDescription { get; set; }
        /// <summary>IExternalCommand implementation.</summary>
        public Type CommandType { get; set; }
        /// <summary>Optional IExternalCommandAvailability implementation.</summary>
        public Type AvailabilityType { get; set; }
        /// <summary>Icon drawn by Icons.Create (two or three letters + accent colour).</summary>
        public string IconGlyph { get; set; }
        public string IconColor { get; set; } = "#0E7C86";
        /// <summary>Large (32 px) or small (16 px, stacked) button.</summary>
        public bool Large { get; set; } = true;
    }

    /// <summary>
    /// A CausaFix tool. A module owns its panel(s) and commands; the bootstrapper only places them on the
    /// "CausaFix" tab. To add a tool: create a folder under Modules/, implement ICausaFixModule, and add
    /// one line to ModuleRegistry.Modules. See docs/AGREGAR_MODULO.md.
    /// </summary>
    public interface ICausaFixModule
    {
        string Id { get; }
        /// <summary>Ribbon panel title. Modules with the same panel title share the panel.</summary>
        string Panel { get; }
        IEnumerable<CommandSpec> Commands { get; }
    }

    public static class ModuleRegistry
    {
        public const string TabName = "CausaFix";

        /// <summary>Registered modules, in ribbon order. Adding a tool = adding its module here.</summary>
        public static readonly IReadOnlyList<ICausaFixModule> Modules = new ICausaFixModule[]
        {
            new ExcelLinkModule(),
            new SuiteModule()
        };
    }
}
