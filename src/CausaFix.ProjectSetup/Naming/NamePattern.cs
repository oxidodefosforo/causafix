using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CausaFix.ProjectSetup.Model;

namespace CausaFix.ProjectSetup.Naming
{
    /// <summary>Which field a pattern produces (decides token availability and character rules).</summary>
    public enum NameContext
    {
        ViewName = 0,
        TitleOnSheet,
        SheetNumber,
        SheetName
    }

    /// <summary>Values available to the patterns of one row.</summary>
    public sealed class TokenValues
    {
        public string ProjectNumber { get; set; }
        public string DisciplineCode { get; set; }
        public string ViewSetCode { get; set; }
        public string ViewSetName { get; set; }
        public string LevelName { get; set; }
        public string LevelCode { get; set; }
        public string Zone { get; set; }
        public int? Sequence { get; set; }
    }

    public sealed class PatternResult
    {
        public string Text { get; set; } = string.Empty;
        public List<string> Errors { get; } = new List<string>();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Token engine. Syntax: {Token} or {Token:format}. Formats: "000" (zero padding, Sequence only),
    /// "upper", "lower". Fixed text, separators, prefixes and suffixes are written literally.
    /// "{{" and "}}" produce literal braces. Missing values and unknown tokens are errors, never silently blank.
    /// </summary>
    public static class NamePattern
    {
        public static readonly string[] Tokens =
        {
            "ProjectNumber", "DisciplineCode", "ViewSetCode", "ViewSetName", "LevelName", "LevelCode", "Zone", "Sequence"
        };

        /// <summary>Tokens allowed in each context. All row tokens are available everywhere in this version.</summary>
        public static IReadOnlyList<string> Available(NameContext ctx) => Tokens;

        public static PatternResult Expand(string pattern, TokenValues v, NameContext ctx, TextCase textCase = TextCase.AsTyped)
        {
            var r = new PatternResult();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                r.Errors.Add(ctx + ": pattern is empty.");
                return r;
            }
            var sb = new StringBuilder();
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                if (c == '{' && i + 1 < pattern.Length && pattern[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < pattern.Length && pattern[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
                if (c == '}') { r.Errors.Add(ctx + ": unmatched '}' at position " + (i + 1) + "."); i++; continue; }
                if (c != '{') { sb.Append(c); i++; continue; }

                int end = pattern.IndexOf('}', i + 1);
                if (end < 0) { r.Errors.Add(ctx + ": unclosed '{' at position " + (i + 1) + "."); break; }
                string body = pattern.Substring(i + 1, end - i - 1);
                i = end + 1;
                string name = body, format = null;
                int colon = body.IndexOf(':');
                if (colon >= 0) { name = body.Substring(0, colon); format = body.Substring(colon + 1); }
                string token = Tokens.FirstOrDefault(t => string.Equals(t, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (token == null) { r.Errors.Add(ctx + ": unknown token {" + body + "}. Available: " + string.Join(", ", Tokens.Select(t => "{" + t + "}")) + "."); continue; }
                if (!Available(ctx).Contains(token)) { r.Errors.Add(ctx + ": token {" + token + "} is not available here."); continue; }

                string value = Value(token, v, format, r, ctx);
                if (value == null) continue;
                sb.Append(value);
            }
            string text = sb.ToString();
            if (textCase == TextCase.Upper) text = text.ToUpper(CultureInfo.CurrentCulture);
            else if (textCase == TextCase.Lower) text = text.ToLower(CultureInfo.CurrentCulture);
            r.Text = text.Trim();
            string invalid = NameRules.InvalidCharacters(r.Text, ctx);
            if (invalid.Length > 0) r.Errors.Add(ctx + " '" + r.Text + "' contains characters Revit does not allow: " + invalid);
            if (r.Text.Length == 0 && r.Errors.Count == 0) r.Errors.Add(ctx + ": the result is empty.");
            return r;
        }

        private static string Value(string token, TokenValues v, string format, PatternResult r, NameContext ctx)
        {
            string raw;
            switch (token)
            {
                case "ProjectNumber": raw = v.ProjectNumber; break;
                case "DisciplineCode": raw = v.DisciplineCode; break;
                case "ViewSetCode": raw = v.ViewSetCode; break;
                case "ViewSetName": raw = v.ViewSetName; break;
                case "LevelName": raw = v.LevelName; break;
                case "LevelCode": raw = v.LevelCode; break;
                case "Zone": raw = v.Zone; break;
                case "Sequence":
                    if (!v.Sequence.HasValue) { r.Errors.Add(ctx + ": {Sequence} has no value for this row."); return null; }
                    if (!string.IsNullOrEmpty(format) && format.All(ch => ch == '0'))
                        return v.Sequence.Value.ToString(new string('0', format.Length), CultureInfo.InvariantCulture);
                    raw = v.Sequence.Value.ToString(CultureInfo.InvariantCulture);
                    break;
                default: raw = null; break;
            }
            if (string.IsNullOrWhiteSpace(raw))
            {
                r.Errors.Add(ctx + ": {" + token + "} has no value for this row" + (token == "LevelCode" ? " (set a code in Levels)." : token == "ProjectNumber" ? " (Project Information > Project Number is empty)." : "."));
                return null;
            }
            if (string.IsNullOrEmpty(format)) return raw;
            switch (format.Trim().ToLowerInvariant())
            {
                case "upper": return raw.ToUpper(CultureInfo.CurrentCulture);
                case "lower": return raw.ToLower(CultureInfo.CurrentCulture);
                default:
                    r.Errors.Add(ctx + ": unknown format '" + format + "' in {" + token + ":" + format + "} (use upper, lower or 000).");
                    return null;
            }
        }
    }

    /// <summary>Revit naming constraints relevant to Project Setup.</summary>
    public static class NameRules
    {
        /// <summary>Characters Revit rejects in view names and sheet numbers/names.</summary>
        public const string ForbiddenInNames = "\\:{}[]|;<>?`~";

        public static string InvalidCharacters(string text, NameContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (ctx == NameContext.TitleOnSheet) return string.Empty; // free text parameter
            var bad = text.Where(ch => ForbiddenInNames.IndexOf(ch) >= 0 || char.IsControl(ch)).Distinct().ToArray();
            return new string(bad);
        }

        /// <summary>View names are unique per view type in Revit; sheet numbers are unique; sheet names and titles may repeat.</summary>
        public static bool RequiresUniqueness(NameContext ctx) => ctx == NameContext.ViewName || ctx == NameContext.SheetNumber;

        public static string Normalize(string s) => (s ?? string.Empty).Trim();

        public static bool SameName(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }
}
