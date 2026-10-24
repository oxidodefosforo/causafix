using System;
using System.Globalization;

namespace CausaFix.Core.Model
{
    /// <summary>Data kind of a column, derived from Revit StorageType + spec.</summary>
    public enum ValueKind
    {
        /// <summary>CausaFix context column (Category, Family...). Never imported.</summary>
        Context = 0,
        Text,
        Integer,
        /// <summary>Integer storage with Yes/No spec.</summary>
        YesNo,
        /// <summary>Double storage. Internal value is in Revit internal units.</summary>
        Double,
        /// <summary>ElementId storage (material, level...). Exported as a name; read-only in this version.</summary>
        ElementRef
    }

    public enum RawKind
    {
        /// <summary>The parameter does not exist on the element (N/A). Never equal to empty or zero.</summary>
        NotApplicable = 0,
        /// <summary>The parameter exists but has no value.</summary>
        Empty,
        Text,
        Integer,
        Double,
        ElementId
    }

    /// <summary>
    /// A parameter value in Revit's internal representation (doubles in internal units).
    /// Immutable. The encoded form is used for the hidden "original values" sheet.
    /// </summary>
    public readonly struct RawValue
    {
        public RawKind Kind { get; }
        public string Text { get; }
        public long Integer { get; }
        public double Double { get; }

        private RawValue(RawKind kind, string text, long integer, double dbl)
        {
            Kind = kind;
            Text = text;
            Integer = integer;
            Double = dbl;
        }

        public static readonly RawValue NotApplicable = new RawValue(RawKind.NotApplicable, null, 0, 0);
        public static readonly RawValue Empty = new RawValue(RawKind.Empty, null, 0, 0);
        public static RawValue FromText(string s) => s == null ? Empty : new RawValue(RawKind.Text, s, 0, 0);
        public static RawValue FromInteger(long v) => new RawValue(RawKind.Integer, null, v, 0);
        public static RawValue FromDouble(double v) => new RawValue(RawKind.Double, null, 0, v);
        public static RawValue FromElementId(long v) => new RawValue(RawKind.ElementId, null, v, 0);

        public bool IsNotApplicable => Kind == RawKind.NotApplicable;
        public bool IsEmpty => Kind == RawKind.Empty || (Kind == RawKind.Text && Text.Length == 0);

        /// <summary>Lossless, culture-invariant encoding.</summary>
        public string Encode()
        {
            switch (Kind)
            {
                case RawKind.NotApplicable: return "na";
                case RawKind.Empty: return "e";
                case RawKind.Text: return "s:" + Text;
                case RawKind.Integer: return "i:" + Integer.ToString(CultureInfo.InvariantCulture);
                case RawKind.Double: return "d:" + Double.ToString("R", CultureInfo.InvariantCulture);
                case RawKind.ElementId: return "id:" + Integer.ToString(CultureInfo.InvariantCulture);
                default: throw new InvalidOperationException();
            }
        }

        public static bool TryDecode(string encoded, out RawValue value)
        {
            value = NotApplicable;
            if (encoded == null) return false;
            if (encoded == "na") { value = NotApplicable; return true; }
            if (encoded == "e") { value = Empty; return true; }
            if (encoded.StartsWith("s:", StringComparison.Ordinal)) { value = FromText(encoded.Substring(2)); return true; }
            if (encoded.StartsWith("i:", StringComparison.Ordinal))
            {
                if (!long.TryParse(encoded.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)) return false;
                value = FromInteger(i);
                return true;
            }
            if (encoded.StartsWith("d:", StringComparison.Ordinal))
            {
                if (!double.TryParse(encoded.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
                value = FromDouble(d);
                return true;
            }
            if (encoded.StartsWith("id:", StringComparison.Ordinal))
            {
                if (!long.TryParse(encoded.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) return false;
                value = FromElementId(id);
                return true;
            }
            return false;
        }

        /// <summary>Short human readable form for reports (internal units for doubles).</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case RawKind.NotApplicable: return "N/A";
                case RawKind.Empty: return "";
                case RawKind.Text: return Text;
                case RawKind.Integer: return Integer.ToString(CultureInfo.InvariantCulture);
                case RawKind.Double: return Double.ToString("R", CultureInfo.InvariantCulture);
                case RawKind.ElementId: return "#" + Integer.ToString(CultureInfo.InvariantCulture);
                default: return "?";
            }
        }
    }
}
