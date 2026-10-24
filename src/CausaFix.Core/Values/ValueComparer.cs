using System;
using CausaFix.Core.Model;

namespace CausaFix.Core.Values
{
    /// <summary>
    /// Equality used by the three-state comparison. Doubles are compared in internal units with a
    /// tolerance far below any displayable precision, so unit conversion round-trips and Excel's
    /// 15-significant-digit storage never produce false changes.
    /// </summary>
    public static class ValueComparer
    {
        /// <summary>Relative tolerance for doubles (internal units).</summary>
        public const double RelativeTolerance = 1e-10;
        /// <summary>Absolute tolerance for values near zero (internal units).</summary>
        public const double AbsoluteTolerance = 1e-12;

        public static bool DoublesEqual(double a, double b)
        {
            if (a == b) return true;
            if (double.IsNaN(a) || double.IsNaN(b)) return false;
            double diff = Math.Abs(a - b);
            if (diff <= AbsoluteTolerance) return true;
            double scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return diff <= scale * RelativeTolerance;
        }

        public static bool AreEqual(RawValue a, RawValue b)
        {
            // Empty and empty-string are the same state for a parameter.
            if (a.IsEmpty && b.IsEmpty) return true;
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case RawKind.NotApplicable:
                case RawKind.Empty:
                    return true;
                case RawKind.Text:
                    return string.Equals(NormalizeText(a.Text), NormalizeText(b.Text), StringComparison.Ordinal);
                case RawKind.Integer:
                case RawKind.ElementId:
                    return a.Integer == b.Integer;
                case RawKind.Double:
                    return DoublesEqual(a.Double, b.Double);
                default:
                    return false;
            }
        }

        /// <summary>Line endings are not meaningful differences (Excel stores LF, Revit may hold CRLF).</summary>
        public static string NormalizeText(string s) => s == null ? string.Empty : s.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>Converts between Revit internal units and the export unit of a column. Implemented with UnitUtils in Revit.</summary>
    public interface IUnitConverter
    {
        /// <summary>Converts a number expressed in <paramref name="unitTypeId"/> to internal units.</summary>
        double ToInternal(double value, string unitTypeId);
        /// <summary>Converts an internal value to <paramref name="unitTypeId"/>.</summary>
        double FromInternal(double value, string unitTypeId);
    }

    /// <summary>Converter for unitless columns and tests: values pass through.</summary>
    public sealed class IdentityUnitConverter : IUnitConverter
    {
        public static readonly IdentityUnitConverter Instance = new IdentityUnitConverter();
        public double ToInternal(double value, string unitTypeId) => value;
        public double FromInternal(double value, string unitTypeId) => value;
    }
}
