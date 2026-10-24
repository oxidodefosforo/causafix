using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using CausaFix.Core.Values;

namespace CausaFix.Revit
{
    /// <summary>
    /// Unit conversion through the Revit API (UnitUtils + ForgeTypeId). The unit of each exported column is
    /// the project's display unit for that spec at export time and is stored in the workbook, so imports are
    /// read in the recorded unit even if project units change afterwards.
    /// </summary>
    public sealed class RevitUnits : IUnitConverter
    {
        private readonly Dictionary<string, ForgeTypeId> _cache = new Dictionary<string, ForgeTypeId>(StringComparer.Ordinal);

        public double ToInternal(double value, string unitTypeId)
        {
            if (string.IsNullOrEmpty(unitTypeId)) return value;
            return UnitUtils.ConvertToInternalUnits(value, Unit(unitTypeId));
        }

        public double FromInternal(double value, string unitTypeId)
        {
            if (string.IsNullOrEmpty(unitTypeId)) return value;
            return UnitUtils.ConvertFromInternalUnits(value, Unit(unitTypeId));
        }

        private ForgeTypeId Unit(string id)
        {
            if (!_cache.TryGetValue(id, out var f))
            {
                f = new ForgeTypeId(id);
                if (!UnitUtils.IsUnit(f)) throw new ArgumentException("Unknown unit '" + id + "' in workbook metadata.");
                _cache[id] = f;
            }
            return f;
        }

        /// <summary>Display unit and short label for a spec in this document. Empty for non-measurable specs.</summary>
        public static (string unitTypeId, string label) DisplayUnit(Document doc, ForgeTypeId spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.TypeId) || !UnitUtils.IsMeasurableSpec(spec)) return (string.Empty, string.Empty);
            FormatOptions fo = doc.GetUnits().GetFormatOptions(spec);
            ForgeTypeId unit = fo.GetUnitTypeId();
            string label = string.Empty;
            try
            {
                ForgeTypeId symbol = fo.GetSymbolTypeId();
                if (symbol != null && !string.IsNullOrEmpty(symbol.TypeId)) label = LabelUtils.GetLabelForSymbol(symbol);
            }
            catch (Exception) { /* some units have no symbol */ }
            if (string.IsNullOrEmpty(label)) label = LabelUtils.GetLabelForUnit(unit);
            return (unit.TypeId, label);
        }

        public static string SpecLabel(ForgeTypeId spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.TypeId)) return "None";
            try { return LabelUtils.GetLabelForSpec(spec); }
            catch (Exception) { return spec.TypeId; }
        }
    }
}
