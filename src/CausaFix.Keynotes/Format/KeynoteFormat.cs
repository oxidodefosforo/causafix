using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CausaFix.Keynotes.Model;

namespace CausaFix.Keynotes.Format
{
    /// <summary>Encodings this implementation reads and writes.</summary>
    public enum KeynoteEncoding
    {
        /// <summary>UTF-16 little endian with BOM ("Unicode" in Notepad). Used for every new file.</summary>
        Utf16LeBom = 0,
        Utf16BeBom,
        Utf8Bom,
        /// <summary>UTF-8 without BOM (only when the bytes are valid UTF-8 and contain non-ASCII text).</summary>
        Utf8,
        /// <summary>Windows-1252 (ANSI, Western). Also used for pure ASCII files.</summary>
        Ansi1252
    }

    public sealed class UnparsedLine
    {
        public int Line { get; set; }
        public string Raw { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>A keynote file read from bytes: records, issues, preserved lines and how it was encoded.</summary>
    public sealed class KeynoteDocument
    {
        public KeynoteLibrary Library { get; set; } = new KeynoteLibrary();
        public KeynoteEncoding Encoding { get; set; } = KeynoteEncoding.Utf16LeBom;
        /// <summary>Lines starting with '#' (comments) kept at the top when the file is written again.</summary>
        public List<string> Preamble { get; } = new List<string>();
        /// <summary>Lines that are not a valid record (kept as they are unless the user drops them).</summary>
        public List<UnparsedLine> Unparsed { get; } = new List<UnparsedLine>();
        public List<LibraryIssue> Issues { get; } = new List<LibraryIssue>();
        /// <summary>pyRevit DeffrelDB-managed file (metadata, tables, locks): read-only here; import a standard copy instead.</summary>
        public bool IsDeffrelDb { get; set; }
        public string Hash { get; set; } = string.Empty;
        public long Length { get; set; }
        public bool HadCrLf { get; set; } = true;
    }

    /// <summary>
    /// Native Revit keynote TXT: one record per line, "Key TAB Text" (root/category) or "Key TAB Text TAB ParentKey".
    /// Keys are kept as text. Empty lines are ignored; lines starting with '#' are kept as comments. pyRevit files
    /// managed by DeffrelDB (#…@datastore/@db/@table metadata) are recognised and only their keynote tables are read.
    /// </summary>
    public static class KeynoteFormat
    {
        private static readonly Regex DeffrelMeta = new Regex(@"@(datastore|db|table)\(", RegexOptions.Compiled);
        private static readonly Regex DeffrelTable = new Regex(@"@table\((?<name>[^:)]*)", RegexOptions.Compiled);

        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes ?? new byte[0])).Replace("-", "");
        }

        public static KeynoteDocument Read(byte[] bytes)
        {
            bytes = bytes ?? new byte[0];
            var doc = new KeynoteDocument { Hash = Sha256(bytes), Length = bytes.LongLength };
            string text = Decode(bytes, out KeynoteEncoding enc);
            doc.Encoding = enc;
            doc.HadCrLf = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 || text.IndexOf('\n') < 0;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            doc.IsDeffrelDb = lines.Any(l => l.StartsWith("#", StringComparison.Ordinal) && DeffrelMeta.IsMatch(l));
            string table = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int n = i + 1;
                if (line.Trim().Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    if (doc.IsDeffrelDb)
                    {
                        var m = DeffrelTable.Match(line);
                        if (m.Success) table = m.Groups["name"].Value.Trim().Trim('"');
                    }
                    else doc.Preamble.Add(line);
                    continue;
                }
                var f = line.Split('\t');
                if (doc.IsDeffrelDb)
                {
                    // Only pyRevit's keynote tables ("@table(categories:…)", "@table(keynotes:…)"); locks and any other table are not keynotes.
                    // DeffrelDB writes a missing value as NULL; tables are tab separated, values not quoted.
                    string V(int i) => f.Length > i && f[i] != "NULL" ? f[i] : "";
                    if (table == "categories" && f.Length >= 2) doc.Library.Records.Add(new KeynoteRecord { Key = V(0), Text = V(1) });
                    else if (table == "keynotes" && f.Length >= 2) doc.Library.Records.Add(new KeynoteRecord { Key = V(0), Text = V(1), ParentKey = V(2) });
                    continue;
                }
                if (f.Length < 2 || f.Length > 3)
                {
                    string reason = f.Length < 2 ? "no tab between key and text" : (f.Length - 1) + " tabs (1 or 2 expected)";
                    doc.Unparsed.Add(new UnparsedLine { Line = n, Raw = line, Reason = reason });
                    doc.Issues.Add(new LibraryIssue { Kind = IssueKind.Malformed, Line = n, Message = "Malformed record (" + reason + "): '" + Short(line) + "'." });
                    continue;
                }
                doc.Library.Records.Add(new KeynoteRecord { Key = f[0], Text = f[1], ParentKey = f.Length == 3 ? f[2] : "" });
            }
            foreach (var issue in doc.Library.Validate())
            {
                issue.Line = LineOf(lines, issue.Key);
                doc.Issues.Add(issue);
            }
            return doc;
        }

        private static int LineOf(string[] lines, string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            for (int i = 0; i < lines.Length; i++) if (lines[i].StartsWith(key + "\t", StringComparison.Ordinal)) return i + 1;
            return 0;
        }

        /// <summary>Text of the file: preamble comments, records in their order, kept unparsed lines (unless dropped).</summary>
        public static string Compose(KeynoteLibrary lib, IEnumerable<string> preamble = null, IEnumerable<UnparsedLine> keepUnparsed = null, bool crlf = true)
        {
            string nl = crlf ? "\r\n" : "\n";
            var sb = new StringBuilder();
            foreach (var p in preamble ?? Enumerable.Empty<string>()) sb.Append(p).Append(nl);
            foreach (var r in lib.Records)
            {
                sb.Append(r.Key).Append('\t').Append(r.Text);
                if (!r.IsRoot) sb.Append('\t').Append(r.ParentKey);
                sb.Append(nl);
            }
            foreach (var u in keepUnparsed ?? Enumerable.Empty<UnparsedLine>()) sb.Append(u.Raw).Append(nl);
            return sb.ToString();
        }

        /// <summary>Bytes in <paramref name="encoding"/>. An ANSI file that would lose characters is written as UTF-16 LE instead (reported).</summary>
        public static byte[] Encode(string text, KeynoteEncoding encoding, out KeynoteEncoding used)
        {
            used = encoding;
            if (encoding == KeynoteEncoding.Ansi1252 && !Ansi.CanEncode(text)) used = KeynoteEncoding.Utf16LeBom;
            switch (used)
            {
                case KeynoteEncoding.Utf16LeBom: return WithBom(new UnicodeEncoding(false, true), text);
                case KeynoteEncoding.Utf16BeBom: return WithBom(new UnicodeEncoding(true, true), text);
                case KeynoteEncoding.Utf8Bom: return WithBom(new UTF8Encoding(true), text);
                case KeynoteEncoding.Utf8: return new UTF8Encoding(false).GetBytes(text);
                default: return Ansi.GetBytes(text);
            }
        }

        private static byte[] WithBom(Encoding e, string text)
        {
            var bom = e.GetPreamble();
            var body = e.GetBytes(text);
            var all = new byte[bom.Length + body.Length];
            Buffer.BlockCopy(bom, 0, all, 0, bom.Length);
            Buffer.BlockCopy(body, 0, all, bom.Length, body.Length);
            return all;
        }

        public static string Decode(byte[] b, out KeynoteEncoding enc)
        {
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = KeynoteEncoding.Utf16LeBom; return new UnicodeEncoding(false, false).GetString(b, 2, b.Length - 2); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = KeynoteEncoding.Utf16BeBom; return new UnicodeEncoding(true, false).GetString(b, 2, b.Length - 2); }
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = KeynoteEncoding.Utf8Bom; return new UTF8Encoding(false).GetString(b, 3, b.Length - 3); }
            // UTF-16 LE without BOM: ASCII text has a zero in every odd byte.
            if (b.Length >= 4 && b.Length % 2 == 0)
            {
                int zerosOdd = 0, pairs = Math.Min(b.Length / 2, 512);
                for (int i = 0; i < pairs; i++) if (b[2 * i + 1] == 0 && b[2 * i] != 0) zerosOdd++;
                if (zerosOdd > pairs * 0.6) { enc = KeynoteEncoding.Utf16LeBom; return new UnicodeEncoding(false, false).GetString(b); }
            }
            bool ascii = b.All(x => x < 0x80);
            if (!ascii)
            {
                try { var s = new UTF8Encoding(false, true).GetString(b); enc = KeynoteEncoding.Utf8; return s; }
                catch (DecoderFallbackException) { }
            }
            enc = KeynoteEncoding.Ansi1252;
            return Ansi.GetString(b);
        }

        public static string Describe(KeynoteEncoding e)
        {
            switch (e)
            {
                case KeynoteEncoding.Utf16LeBom: return "Unicode (UTF-16 LE with BOM)";
                case KeynoteEncoding.Utf16BeBom: return "Unicode big endian (UTF-16 BE with BOM)";
                case KeynoteEncoding.Utf8Bom: return "UTF-8 with BOM";
                case KeynoteEncoding.Utf8: return "UTF-8 without BOM";
                default: return "ANSI (Windows-1252)";
            }
        }

        private static string Short(string s) => s.Length > 60 ? s.Substring(0, 60) + "…" : s;
    }

    /// <summary>Windows-1252 without the code-pages provider (0x80–0x9F table; the rest is Latin-1).</summary>
    public static class Ansi
    {
        private static readonly char[] High =
        {
            '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
            '\u0090', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ'
        };
        private static readonly Dictionary<char, byte> Reverse = Enumerable.Range(0, 32).ToDictionary(i => High[i], i => (byte)(0x80 + i));

        public static string GetString(byte[] b)
        {
            var c = new char[b.Length];
            for (int i = 0; i < b.Length; i++) c[i] = b[i] >= 0x80 && b[i] <= 0x9F ? High[b[i] - 0x80] : (char)b[i];
            return new string(c);
        }

        public static bool CanEncode(string s) => s.All(ch => ch < 0x80 || (ch >= 0xA0 && ch <= 0xFF) || Reverse.ContainsKey(ch));

        public static byte[] GetBytes(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                b[i] = ch < 0x80 || (ch >= 0xA0 && ch <= 0xFF) ? (byte)ch : Reverse.TryGetValue(ch, out byte v) ? v : (byte)'?';
            }
            return b;
        }
    }
}
