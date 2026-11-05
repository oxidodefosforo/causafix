using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace CausaFix.Keynotes.Model
{
    /// <summary>
    /// One record of a Revit keynote file: Key TAB Text [TAB ParentKey]. A record without parent is a root (a category in
    /// Revit's dialog); any record can have children (groups and subgroups are records with children). Keys are text:
    /// leading zeros and alphanumeric codes are preserved exactly.
    /// </summary>
    [DataContract]
    public sealed class KeynoteRecord
    {
        [DataMember(Order = 1)] public string Key { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string Text { get; set; } = string.Empty;
        /// <summary>Empty for a root record.</summary>
        [DataMember(Order = 3)] public string ParentKey { get; set; } = string.Empty;

        public bool IsRoot => string.IsNullOrEmpty(ParentKey);
        public KeynoteRecord Clone() => new KeynoteRecord { Key = Key, Text = Text, ParentKey = ParentKey };
        public bool SameAs(KeynoteRecord o) => o != null && Key == o.Key && Text == o.Text && (ParentKey ?? "") == (o.ParentKey ?? "");
        public override string ToString() => Key + " – " + Text;
    }

    public enum IssueKind
    {
        Malformed,
        EmptyKey,
        DuplicateKey,
        MissingParent,
        SelfParent,
        Cycle,
        InvalidCharacters
    }

    public sealed class LibraryIssue
    {
        public IssueKind Kind { get; set; }
        public string Key { get; set; } = string.Empty;
        /// <summary>1-based line of the file (0 when not from a file).</summary>
        public int Line { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsError => true;
        public override string ToString() => (Line > 0 ? "Line " + Line + ": " : "") + Message;
    }

    /// <summary>
    /// An ordered set of keynote records (file order is kept for writing; the tree view sorts naturally). Validation
    /// never changes the data: it reports duplicates, missing parents, self references, cycles and invalid characters.
    /// </summary>
    public sealed class KeynoteLibrary
    {
        public List<KeynoteRecord> Records { get; } = new List<KeynoteRecord>();

        public KeynoteLibrary() { }
        public KeynoteLibrary(IEnumerable<KeynoteRecord> records) { Records.AddRange(records.Select(r => r.Clone())); }

        public KeynoteLibrary Clone() => new KeynoteLibrary(Records);
        public KeynoteRecord Find(string key) => Records.FirstOrDefault(r => r.Key == key);
        public bool Contains(string key) => Records.Any(r => r.Key == key);
        public IEnumerable<KeynoteRecord> ChildrenOf(string key) => Records.Where(r => (r.ParentKey ?? "") == (key ?? ""));

        /// <summary>The record and all its descendants (depth first, natural order), cycle-safe.</summary>
        public List<KeynoteRecord> Branch(string key)
        {
            var result = new List<KeynoteRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Walk(string k)
            {
                var r = Find(k);
                if (r == null || !seen.Add(k)) return;
                result.Add(r);
                foreach (var c in ChildrenOf(k).OrderBy(x => x.Key, NaturalComparer.Instance)) Walk(c.Key);
            }
            Walk(key);
            return result;
        }

        /// <summary>Ancestors from the root down to the direct parent (cycle-safe).</summary>
        public List<KeynoteRecord> Ancestors(string key)
        {
            var list = new List<KeynoteRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { key };
            var r = Find(key);
            while (r != null && !r.IsRoot)
            {
                var p = Find(r.ParentKey);
                if (p == null || !seen.Add(p.Key)) break;
                list.Insert(0, p);
                r = p;
            }
            return list;
        }

        public bool IsDescendant(string candidate, string ancestorKey)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var r = Find(candidate);
            while (r != null && !r.IsRoot && seen.Add(r.Key))
            {
                if (r.ParentKey == ancestorKey) return true;
                r = Find(r.ParentKey);
            }
            return false;
        }

        public List<LibraryIssue> Validate()
        {
            var issues = new List<LibraryIssue>();
            var counts = Records.GroupBy(r => r.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var kv in counts.Where(k => k.Value > 1))
                issues.Add(new LibraryIssue { Kind = IssueKind.DuplicateKey, Key = kv.Key, Message = "Key '" + kv.Key + "' appears " + kv.Value + " times." });
            var keys = new HashSet<string>(counts.Keys, StringComparer.Ordinal);
            foreach (var r in Records)
            {
                if (string.IsNullOrWhiteSpace(r.Key)) { issues.Add(new LibraryIssue { Kind = IssueKind.EmptyKey, Message = "A record has no key (text: '" + Short(r.Text) + "')." }); continue; }
                string bad = KeynoteText.InvalidCharacters(r.Key) ?? KeynoteText.InvalidCharacters(r.Text) ?? KeynoteText.InvalidCharacters(r.ParentKey);
                if (bad != null) issues.Add(new LibraryIssue { Kind = IssueKind.InvalidCharacters, Key = r.Key, Message = "'" + r.Key + "' contains " + bad + ": the file would have broken records." });
                if (!r.IsRoot && r.ParentKey == r.Key) issues.Add(new LibraryIssue { Kind = IssueKind.SelfParent, Key = r.Key, Message = "'" + r.Key + "' is its own parent." });
                else if (!r.IsRoot && !keys.Contains(r.ParentKey)) issues.Add(new LibraryIssue { Kind = IssueKind.MissingParent, Key = r.Key, Message = "Parent '" + r.ParentKey + "' of '" + r.Key + "' does not exist." });
            }
            // Cycles: follow parents; a key visited twice on one path is a cycle (reported once per cycle).
            var reported = new HashSet<string>(StringComparer.Ordinal);
            var byKey = Records.GroupBy(r => r.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var r in Records)
            {
                var path = new List<string>();
                var cur = r;
                while (cur != null && !cur.IsRoot && cur.ParentKey != cur.Key)
                {
                    int at = path.IndexOf(cur.Key);
                    if (at >= 0)
                    {
                        var cycle = path.Skip(at).ToList();
                        if (cycle.All(k => reported.Add(k)))
                            issues.Add(new LibraryIssue { Kind = IssueKind.Cycle, Key = cycle[0], Message = "Parent cycle: " + string.Join(" → ", cycle.Concat(new[] { cycle[0] })) + "." });
                        break;
                    }
                    path.Add(cur.Key);
                    byKey.TryGetValue(cur.ParentKey, out cur);
                }
            }
            return issues;
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }

    /// <summary>Text rules of a keynote record: one line, no tabs.</summary>
    public static class KeynoteText
    {
        /// <summary>Line breaks recognised when pasting (Word Shift+Enter = VT, form feed, NEL, line/paragraph separators).</summary>
        private static readonly char[] Breaks = { '\r', '\n', '\v', '\f', '\u0085', '\u2028', '\u2029' };

        /// <summary>
        /// One-line form of pasted text: every line break and tab becomes a single space, spaces around breaks collapse,
        /// the rest is kept exactly (accents, ñ, Ø, ±, °, ², µ…). Nothing after the first line is lost.
        /// </summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(value.Length);
            bool pendingSpace = false;
            foreach (char c in value)
            {
                bool brk = Array.IndexOf(Breaks, c) >= 0 || c == '\t';
                if (brk) { pendingSpace = true; continue; }
                if (pendingSpace && c == ' ') continue;            // indentation of the next pasted line
                if (pendingSpace)
                {
                    while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
                    if (sb.Length > 0) sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        public static bool NeedsNormalization(string value) => !string.IsNullOrEmpty(value) && (value.IndexOfAny(Breaks) >= 0 || value.IndexOf('\t') >= 0 || value != value.Trim());

        /// <summary>Description of characters that would break a record line, or null.</summary>
        public static string InvalidCharacters(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value.IndexOf('\t') >= 0) return "a tab";
            if (value.IndexOfAny(Breaks) >= 0) return "a line break";
            if (value.Any(c => c < ' ' && c != '\t')) return "a control character";
            return null;
        }

        public static string ToUpper(string s) => (s ?? "").ToUpper(CultureInfo.CurrentCulture);
        public static string ToLower(string s) => (s ?? "").ToLower(CultureInfo.CurrentCulture);
        public static string ToTitle(string s) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase((s ?? "").ToLower(CultureInfo.CurrentCulture));
        public static string ToSentence(string s)
        {
            string l = (s ?? "").ToLower(CultureInfo.CurrentCulture);
            var sb = new StringBuilder(l);
            bool start = true;
            for (int i = 0; i < sb.Length; i++)
            {
                if (start && char.IsLetter(sb[i])) { sb[i] = char.ToUpper(sb[i], CultureInfo.CurrentCulture); start = false; }
                else if (sb[i] == '.' || sb[i] == '!' || sb[i] == '?') start = true;
            }
            return sb.ToString();
        }
    }

    /// <summary>Natural alphanumeric order: "2" before "10", "E-LTG-002" before "E-LTG-010"; leading zeros break ties.</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                    int z = (i - si).CompareTo(j - sj);              // more leading zeros last
                    if (z != 0) return z;
                }
                else
                {
                    int c = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            int len = (a.Length - i).CompareTo(b.Length - j);
            return len != 0 ? len : string.CompareOrdinal(a, b);
        }
    }
}
