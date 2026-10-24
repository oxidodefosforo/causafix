using System;
using System.Collections.Generic;

namespace CausaFix.Core.Values
{
    /// <summary>
    /// Unit conversions captured from Revit (all Revit unit conversions are affine: y = a·x + b, b ≠ 0 only for
    /// temperatures). Built inside a Revit API context, then usable anywhere (UI thread, background tasks)
    /// without calling the Revit API.
    /// </summary>
    public sealed class AffineUnitConverter : IUnitConverter
    {
        private readonly Dictionary<string, (double a, double b)> _fromInternal = new Dictionary<string, (double, double)>(StringComparer.Ordinal);

        /// <summary>Registers a unit from two samples of the Revit conversion: f(0) and f(1) (internal → unit).</summary>
        public void Register(string unitTypeId, double fromInternalOf0, double fromInternalOf1)
        {
            if (string.IsNullOrEmpty(unitTypeId)) return;
            double a = fromInternalOf1 - fromInternalOf0;
            if (a == 0 || double.IsNaN(a) || double.IsInfinity(a)) throw new ArgumentException("Invalid conversion for unit " + unitTypeId);
            _fromInternal[unitTypeId] = (a, fromInternalOf0);
        }

        public bool Knows(string unitTypeId) => string.IsNullOrEmpty(unitTypeId) || _fromInternal.ContainsKey(unitTypeId);

        public double FromInternal(double value, string unitTypeId)
        {
            if (string.IsNullOrEmpty(unitTypeId)) return value;
            var (a, b) = Get(unitTypeId);
            return a * value + b;
        }

        public double ToInternal(double value, string unitTypeId)
        {
            if (string.IsNullOrEmpty(unitTypeId)) return value;
            var (a, b) = Get(unitTypeId);
            return (value - b) / a;
        }

        private (double a, double b) Get(string unit)
        {
            if (!_fromInternal.TryGetValue(unit, out var f)) throw new KeyNotFoundException("Unit not loaded: " + unit);
            return f;
        }

        public IEnumerable<string> Units => _fromInternal.Keys;
    }
}
