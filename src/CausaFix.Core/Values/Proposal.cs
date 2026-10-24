using System;
using System.Globalization;
using CausaFix.Core.Model;
using CausaFix.Core.Xlsx;

namespace CausaFix.Core.Values
{
    public enum ProposalKind
    {
        /// <summary>No proposal (blank cell). The model keeps its current value.</summary>
        None = 0,
        /// <summary>Explicit request to clear the value (Clear Value action or the &lt;&lt;CLEAR&gt;&gt; token).</summary>
        Clear,
        /// <summary>A parsed value in Revit internal units.</summary>
        Value,
        /// <summary>The input cannot be used. Message explains why.</summary>
        Invalid
    }

    public sealed class Proposal
    {
        public ProposalKind Kind { get; private set; }
        public RawValue Value { get; private set; }
        /// <summary>Error for Invalid proposals.</summary>
        public string Message { get; private set; }
        /// <summary>Informational note (e.g. "value taken from formula =A1*2 → 25").</summary>
        public string Info { get; private set; }
        /// <summary>What the user typed / what the cell showed, for review screens.</summary>
        public string SourceText { get; private set; }

        public static Proposal None() => new Proposal { Kind = ProposalKind.None, SourceText = string.Empty };
        public static Proposal Clear(string src) => new Proposal { Kind = ProposalKind.Clear, SourceText = src };
        public static Proposal Of(RawValue v, string src, string info = null) => new Proposal { Kind = ProposalKind.Value, Value = v, SourceText = src, Info = info };
        public static Proposal Invalid(string message, string src) => new Proposal { Kind = ProposalKind.Invalid, Message = message, SourceText = src };

        internal Proposal WithInfo(string info) { Info = string.IsNullOrEmpty(Info) ? info : Info + " " + info; return this; }
    }

    public sealed class ImportOptions
    {
        public const string DefaultClearToken = "<<CLEAR>>";
        public DecimalSeparatorMode DecimalMode { get; set; } = DecimalSeparatorMode.Auto;
        public string ClearToken { get; set; } = DefaultClearToken;
    }

    /// <summary>Turns a cell or a typed text into a proposal for a column, in internal units.</summary>
    public static class ProposalParser
    {
        public static Proposal FromCell(ColumnDef col, XCell cell, IUnitConverter units, ImportOptions options)
        {
            if (cell == null || (cell.Kind == XCellKind.Blank && !cell.IsFormula)) return Proposal.None();
            string info = null;
            if (cell.IsFormula)
            {
                string shown = "=" + cell.Formula;
                if (!cell.HasCachedValue || cell.Kind == XCellKind.Blank)
                    return Proposal.Invalid("Formula " + shown + " has no saved result. Recalculate and save the workbook in Excel, or paste values.", shown);
                if (cell.Kind == XCellKind.Error)
                    return Proposal.Invalid("Formula " + shown + " evaluates to an error (" + cell.Text + ").", shown);
                info = "Value taken from the saved result of formula " + shown + ": " + cell.AsText() + ".";
            }
            if (cell.Kind == XCellKind.Error)
                return Proposal.Invalid("Excel error value " + cell.Text + ".", cell.Text);

            Proposal p;
            switch (cell.Kind)
            {
                case XCellKind.Number:
                    p = FromNumber(col, cell.Number, units);
                    break;
                case XCellKind.Bool:
                    p = FromBool(col, cell.Bool);
                    break;
                default:
                    p = FromText(col, cell.Text ?? string.Empty, units, options);
                    break;
            }
            if (info != null) p.WithInfo(info);
            return p;
        }

        /// <summary>Text input (Excel text cells, grid edits, pasted values).</summary>
        public static Proposal FromText(ColumnDef col, string text, IUnitConverter units, ImportOptions options)
        {
            options = options ?? new ImportOptions();
            if (text == null) return Proposal.None();
            string trimmed = text.Trim();
            if (!string.IsNullOrEmpty(options.ClearToken) && string.Equals(trimmed, options.ClearToken, StringComparison.OrdinalIgnoreCase))
                return Proposal.Clear(trimmed);

            if (col.IsContext || col.ColumnReadOnly || col.Kind == ValueKind.ElementRef)
                return Proposal.Invalid("Read-only column" + (string.IsNullOrEmpty(col.ReadOnlyReason) ? "." : ": " + col.ReadOnlyReason), text);

            switch (col.Kind)
            {
                case ValueKind.Text:
                    if (text.Length == 0) return Proposal.None();
                    return Proposal.Of(RawValue.FromText(text), text);

                case ValueKind.Integer:
                    {
                        if (trimmed.Length == 0) return Proposal.None();
                        if (!NumberText.TryParse(trimmed, options.DecimalMode, out double d, out string err)) return Proposal.Invalid(err, text);
                        return IntegerFrom(d, text);
                    }

                case ValueKind.YesNo:
                    {
                        if (trimmed.Length == 0) return Proposal.None();
                        if (Eq(trimmed, "Yes") || trimmed == "1" || Eq(trimmed, "True")) return Proposal.Of(RawValue.FromInteger(1), text);
                        if (Eq(trimmed, "No") || trimmed == "0" || Eq(trimmed, "False")) return Proposal.Of(RawValue.FromInteger(0), text);
                        return Proposal.Invalid("Yes/No parameter: use Yes or No (or " + options.ClearToken + ").", text);
                    }

                case ValueKind.Double:
                    {
                        if (trimmed.Length == 0) return Proposal.None();
                        if (!NumberText.TryParse(trimmed, options.DecimalMode, out double d, out string err)) return Proposal.Invalid(err, text);
                        return FromNumber(col, d, units, text);
                    }

                default:
                    return Proposal.Invalid("Unsupported data type.", text);
            }
        }

        private static Proposal FromNumber(ColumnDef col, double number, IUnitConverter units, string src = null)
        {
            src = src ?? number.ToString("R", CultureInfo.InvariantCulture);
            if (!NumberText.IsFinite(number)) return Proposal.Invalid("Not a finite number.", src);
            if (col.IsContext || col.ColumnReadOnly || col.Kind == ValueKind.ElementRef)
                return Proposal.Invalid("Read-only column" + (string.IsNullOrEmpty(col.ReadOnlyReason) ? "." : ": " + col.ReadOnlyReason), src);
            switch (col.Kind)
            {
                case ValueKind.Text:
                    return Proposal.Of(RawValue.FromText(number.ToString("R", CultureInfo.InvariantCulture)), src,
                        "Excel stored this cell as a number; it was converted to text.");
                case ValueKind.Integer:
                    return IntegerFrom(number, src);
                case ValueKind.YesNo:
                    if (number == 1) return Proposal.Of(RawValue.FromInteger(1), src);
                    if (number == 0) return Proposal.Of(RawValue.FromInteger(0), src);
                    return Proposal.Invalid("Yes/No parameter: use Yes or No.", src);
                case ValueKind.Double:
                    {
                        double internalValue = string.IsNullOrEmpty(col.UnitTypeId) ? number : (units ?? IdentityUnitConverter.Instance).ToInternal(number, col.UnitTypeId);
                        if (!NumberText.IsFinite(internalValue)) return Proposal.Invalid("Value out of range.", src);
                        return Proposal.Of(RawValue.FromDouble(internalValue), src);
                    }
                default:
                    return Proposal.Invalid("Unsupported data type.", src);
            }
        }

        private static Proposal FromBool(ColumnDef col, bool b)
        {
            string src = b ? "TRUE" : "FALSE";
            if (col.IsContext || col.ColumnReadOnly || col.Kind == ValueKind.ElementRef)
                return Proposal.Invalid("Read-only column" + (string.IsNullOrEmpty(col.ReadOnlyReason) ? "." : ": " + col.ReadOnlyReason), src);
            switch (col.Kind)
            {
                case ValueKind.YesNo: return Proposal.Of(RawValue.FromInteger(b ? 1 : 0), src);
                case ValueKind.Text: return Proposal.Of(RawValue.FromText(src), src, "Excel stored this cell as a boolean; it was converted to text.");
                default: return Proposal.Invalid("Boolean value not valid for this parameter.", src);
            }
        }

        private static Proposal IntegerFrom(double d, string src)
        {
            double r = Math.Round(d);
            if (Math.Abs(d - r) > 1e-9) return Proposal.Invalid("Integer parameter: '" + src + "' is not a whole number.", src);
            if (r > int.MaxValue || r < int.MinValue) return Proposal.Invalid("Integer out of range.", src);
            return Proposal.Of(RawValue.FromInteger((long)r), src);
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Formats internal values for review screens and reports, in the column unit.</summary>
    public static class ValueFormatter
    {
        public static string Format(RawValue v, ColumnDef col, IUnitConverter units, CultureInfo culture = null)
        {
            culture = culture ?? CultureInfo.CurrentCulture;
            switch (v.Kind)
            {
                case RawKind.NotApplicable: return "N/A";
                case RawKind.Empty: return "(empty)";
                case RawKind.Text: return v.Text.Length == 0 ? "(empty)" : v.Text;
                case RawKind.Integer:
                    if (col != null && col.Kind == ValueKind.YesNo) return v.Integer == 1 ? "Yes" : v.Integer == 0 ? "No" : v.Integer.ToString(culture);
                    return v.Integer.ToString(culture);
                case RawKind.ElementId: return "Id " + v.Integer.ToString(CultureInfo.InvariantCulture);
                case RawKind.Double:
                    {
                        double d = v.Double;
                        string unit = string.Empty;
                        if (col != null && !string.IsNullOrEmpty(col.UnitTypeId))
                        {
                            d = (units ?? IdentityUnitConverter.Instance).FromInternal(d, col.UnitTypeId);
                            unit = string.IsNullOrEmpty(col.UnitLabel) ? string.Empty : " " + col.UnitLabel;
                        }
                        return d.ToString("G12", culture) + unit;
                    }
                default: return v.ToString();
            }
        }

        /// <summary>Display-unit number for an internal double, at full precision.</summary>
        public static double ToDisplayNumber(double internalValue, ColumnDef col, IUnitConverter units)
        {
            if (col == null || string.IsNullOrEmpty(col.UnitTypeId)) return internalValue;
            return (units ?? IdentityUnitConverter.Instance).FromInternal(internalValue, col.UnitTypeId);
        }
    }
}
