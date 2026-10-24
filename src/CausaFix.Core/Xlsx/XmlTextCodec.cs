using System;
using System.Globalization;
using System.Text;

namespace CausaFix.Core.Xlsx
{
    /// <summary>
    /// OOXML ST_Xstring escaping: characters that XML 1.0 cannot carry are written as _xHHHH_,
    /// and a literal "_xHHHH_" sequence is protected as "_x005F_xHHHH_". Same convention as Excel,
    /// so any Revit text round-trips unchanged.
    /// </summary>
    public static class XmlTextCodec
    {
        public static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string replacement = null;
                if (IsEscapeSequenceAt(s, i))
                {
                    // Protect a literal "_xHHHH_" so it is not decoded later.
                    if (sb == null) { sb = new StringBuilder(s.Length + 16); sb.Append(s, 0, i); }
                    sb.Append("_x005F_").Append(c);
                    continue;
                }
                else if (c == '\r')
                {
                    // XML parsers normalise CR; Excel writes it escaped too.
                    replacement = "_x000D_";
                }
                else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    sb?.Append(c).Append(s[i + 1]);
                    i++;
                    continue;
                }
                else if (!IsXmlChar(c))
                {
                    replacement = "_x" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + "_";
                }

                if (replacement != null)
                {
                    if (sb == null) { sb = new StringBuilder(s.Length + 16); sb.Append(s, 0, i); }
                    sb.Append(replacement);
                    continue;
                }
                sb?.Append(c);
            }
            return sb?.ToString() ?? s;
        }

        public static string Decode(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("_x", StringComparison.Ordinal) < 0) return s ?? string.Empty;
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                if (IsEscapeSequenceAt(s, i))
                {
                    int code = int.Parse(s.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (code == 0x5F && IsEscapeSequenceAt(s, i + 7))
                    {
                        // "_x005F_" protecting a literal escape sequence
                        sb.Append(s, i + 7, 7);
                        i += 14;
                        continue;
                    }
                    sb.Append((char)code);
                    i += 7;
                    continue;
                }
                sb.Append(s[i]);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsEscapeSequenceAt(string s, int i)
        {
            if (i + 6 >= s.Length) return false;
            if (s[i] != '_' || s[i + 1] != 'x' || s[i + 6] != '_') return false;
            for (int k = 2; k < 6; k++) if (!Uri.IsHexDigit(s[i + k])) return false;
            return true;
        }

        private static bool IsXmlChar(char c)
        {
            if (c == '\t' || c == '\n' || c == '\r') return true;
            if (c < 0x20) return false;
            if (char.IsSurrogate(c)) return false; // lone surrogate
            if (c == '￾' || c == '￿') return false;
            return true;
        }
    }
}
