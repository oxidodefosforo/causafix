using System;
using System.Globalization;
using System.Text;

namespace CausaFix.Core.Values
{
    public enum DecimalSeparatorMode
    {
        /// <summary>Decide only when unambiguous; otherwise report an error.</summary>
        Auto = 0,
        /// <summary>'.' is decimal, ',' is grouping.</summary>
        Point,
        /// <summary>',' is decimal, '.' is grouping.</summary>
        Comma
    }

    /// <summary>
    /// Parses numbers typed as TEXT (cells formatted as text, pasted values, grid input).
    /// Numeric Excel cells never go through here — they are stored as binary doubles.
    /// The parser never guesses: "1,234" or "1.234" are ambiguous in Auto mode and rejected.
    /// </summary>
    public static class NumberText
    {
        public static bool TryParse(string input, DecimalSeparatorMode mode, out double value, out string error)
        {
            value = 0;
            error = null;
            if (input == null) { error = "Empty value."; return false; }
            string s = input.Trim();
            if (s.Length == 0) { error = "Empty value."; return false; }

            // Thin/no-break/regular spaces are only accepted as digit grouping.
            var sb = new StringBuilder(s.Length);
            bool sawSpaceGroup = false;
            foreach (char ch in s)
            {
                if (ch == ' ' || ch == ' ' || ch == ' ' || ch == ' ') { sb.Append(' '); sawSpaceGroup = true; }
                else sb.Append(ch);
            }
            s = sb.ToString();

            string sign = "";
            if (s[0] == '+' || s[0] == '-') { sign = s[0] == '-' ? "-" : ""; s = s.Substring(1).TrimStart(); }
            if (s.Length == 0) { error = "Not a number: '" + input + "'."; return false; }

            // Scientific notation: only with '.' decimal and no grouping.
            int ePos = s.IndexOfAny(new[] { 'e', 'E' });
            if (ePos >= 0)
            {
                if (s.IndexOf(',') >= 0 || sawSpaceGroup)
                {
                    error = "Scientific notation must use '.' as decimal separator and no grouping: '" + input + "'.";
                    return false;
                }
                if (double.TryParse(sign + s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && IsFinite(value)) return true;
                error = "Not a number: '" + input + "'.";
                return false;
            }

            foreach (char ch in s)
            {
                if (!(char.IsDigit(ch) && ch < 128) && ch != '.' && ch != ',' && ch != ' ')
                {
                    error = "Not a number: '" + input + "' (only digits and separators are allowed; units go in the column header).";
                    return false;
                }
            }

            int dots = Count(s, '.');
            int commas = Count(s, ',');
            char? decimalSep = null;
            char? groupSep = null;

            if (dots > 0 && commas > 0)
            {
                // The separator that occurs last must be the decimal one, and only once.
                char last = s.LastIndexOf('.') > s.LastIndexOf(',') ? '.' : ',';
                char other = last == '.' ? ',' : '.';
                if (Count(s, last) != 1)
                {
                    error = "Ambiguous number '" + input + "': mixed separators.";
                    return false;
                }
                if (!ModeAllowsDecimal(mode, last))
                {
                    error = "Number '" + input + "' uses '" + last + "' as decimal separator but the setting is '" + ModeChar(mode) + "'.";
                    return false;
                }
                decimalSep = last;
                groupSep = other;
            }
            else if (dots + commas == 1)
            {
                char sep = dots == 1 ? '.' : ',';
                int idx = s.IndexOf(sep);
                string intPart = s.Substring(0, idx).Replace(" ", "");
                string frac = s.Substring(idx + 1);
                if (mode == DecimalSeparatorMode.Auto)
                {
                    bool canBeGroup = frac.Length == 3 && intPart.Length >= 1 && intPart.Length <= 3 && intPart != "0" && !sawSpaceGroup;
                    if (canBeGroup)
                    {
                        error = "Ambiguous number '" + input + "': '" + sep + "' could be a decimal or a thousands separator. " +
                                "Type it without grouping (e.g. 1234 or 1.234 written as 1.2340), or set the decimal separator in Settings.";
                        return false;
                    }
                    decimalSep = sep;
                }
                else if (ModeAllowsDecimal(mode, sep))
                {
                    decimalSep = sep;
                }
                else
                {
                    groupSep = sep;
                }
            }
            else if (dots + commas > 1)
            {
                char sep = dots > 0 ? '.' : ',';
                if (mode != DecimalSeparatorMode.Auto && ModeAllowsDecimal(mode, sep))
                {
                    error = "Number '" + input + "' has several '" + sep + "' but it is the decimal separator.";
                    return false;
                }
                groupSep = sep;
            }

            // Split integer / fractional part.
            string intText = s;
            string fracText = "";
            if (decimalSep.HasValue)
            {
                int d = s.LastIndexOf(decimalSep.Value);
                intText = s.Substring(0, d);
                fracText = s.Substring(d + 1);
                if (fracText.IndexOf(' ') >= 0) { error = "Not a number: '" + input + "'."; return false; }
            }

            // Validate grouping of the integer part.
            if (groupSep.HasValue && intText.IndexOf(groupSep.Value) >= 0 && intText.IndexOf(' ') >= 0)
            {
                error = "Not a number: '" + input + "' (mixed grouping).";
                return false;
            }
            char? grp = groupSep.HasValue && intText.IndexOf(groupSep.Value) >= 0 ? groupSep : (intText.IndexOf(' ') >= 0 ? ' ' : (char?)null);
            if (grp.HasValue)
            {
                string[] groups = intText.Split(grp.Value);
                if (groups[0].Length < 1 || groups[0].Length > 3) { error = "Invalid digit grouping in '" + input + "'."; return false; }
                for (int i = 1; i < groups.Length; i++)
                    if (groups[i].Length != 3) { error = "Invalid digit grouping in '" + input + "'."; return false; }
                intText = string.Concat(groups);
            }
            if (intText.Length == 0 && fracText.Length == 0) { error = "Not a number: '" + input + "'."; return false; }
            if (intText.Length == 0) intText = "0";
            foreach (char ch in intText + fracText) if (!char.IsDigit(ch)) { error = "Not a number: '" + input + "'."; return false; }

            string normalized = sign + intText + (fracText.Length > 0 ? "." + fracText : "");
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && IsFinite(value)) return true;
            error = "Not a number: '" + input + "'.";
            return false;
        }

        private static bool ModeAllowsDecimal(DecimalSeparatorMode mode, char sep)
        {
            switch (mode)
            {
                case DecimalSeparatorMode.Point: return sep == '.';
                case DecimalSeparatorMode.Comma: return sep == ',';
                default: return true;
            }
        }

        private static string ModeChar(DecimalSeparatorMode mode) => mode == DecimalSeparatorMode.Comma ? "," : mode == DecimalSeparatorMode.Point ? "." : "auto";

        private static int Count(string s, char c)
        {
            int n = 0;
            foreach (char ch in s) if (ch == c) n++;
            return n;
        }

        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>Shortest round-trip invariant text of a double.</summary>
        public static string Invariant(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
