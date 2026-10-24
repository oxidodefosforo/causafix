using System;
using System.Collections.Generic;
using System.Globalization;

namespace CausaFix.Core.Xlsx
{
    public enum XCellKind
    {
        Blank = 0,
        Number,
        Text,
        Bool,
        Error
    }

    /// <summary>A worksheet cell. For formula cells, Kind/Number/Text hold the CACHED result (if any).</summary>
    public sealed class XCell
    {
        public XCellKind Kind { get; set; }
        public double Number { get; set; }
        public string Text { get; set; }
        public bool Bool { get; set; }
        /// <summary>Formula text (without '='), when the cell holds a formula.</summary>
        public string Formula { get; set; }
        /// <summary>For formula cells: whether a cached result value was stored in the file.</summary>
        public bool HasCachedValue { get; set; }
        public XStyle Style { get; set; }

        public bool IsFormula => Formula != null;

        public static XCell Str(string s, XStyle style = null) => new XCell { Kind = XCellKind.Text, Text = s ?? string.Empty, Style = style };
        public static XCell Num(double d, XStyle style = null) => new XCell { Kind = XCellKind.Number, Number = d, Style = style };
        public static XCell Boolean(bool b, XStyle style = null) => new XCell { Kind = XCellKind.Bool, Bool = b, Style = style };
        public static XCell Empty(XStyle style) => new XCell { Kind = XCellKind.Blank, Style = style };

        /// <summary>Plain text of the stored value (cached value for formulas).</summary>
        public string AsText()
        {
            switch (Kind)
            {
                case XCellKind.Text: return Text ?? string.Empty;
                case XCellKind.Number: return Number.ToString("R", CultureInfo.InvariantCulture);
                case XCellKind.Bool: return Bool ? "TRUE" : "FALSE";
                case XCellKind.Error: return Text ?? "#ERROR";
                default: return string.Empty;
            }
        }
    }

    /// <summary>Cell format. Instances are compared by value when the styles part is written.</summary>
    public sealed class XStyle
    {
        /// <summary>ARGB fill, e.g. "FFE2F0D9". Null = no fill.</summary>
        public string FillRgb { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        /// <summary>ARGB font color. Null = default.</summary>
        public string FontRgb { get; set; }
        public double FontSize { get; set; } = 10;
        /// <summary>49 = Text ("@"), 0 = General.</summary>
        public int NumFmtId { get; set; }
        /// <summary>ARGB of a thick left border (used to mark "type + read-only"). Null = none.</summary>
        public string LeftBorderRgb { get; set; }
        /// <summary>Thin border on all sides.</summary>
        public bool ThinBorder { get; set; }
        public bool Wrap { get; set; }
        public bool Locked { get; set; } = true;

        internal string Signature() => string.Join("|", FillRgb, Bold, Italic, FontRgb,
            FontSize.ToString(CultureInfo.InvariantCulture), NumFmtId, LeftBorderRgb, ThinBorder, Wrap, Locked);
    }

    public enum XSheetState
    {
        Visible = 0,
        Hidden,
        VeryHidden
    }

    public sealed class XDataValidationList
    {
        /// <summary>Range, e.g. "D3:D200".</summary>
        public string Range { get; set; }
        public List<string> Items { get; } = new List<string>();
        public string Prompt { get; set; }
    }

    public sealed class XSheet
    {
        private readonly SortedDictionary<int, SortedDictionary<int, XCell>> _rows = new SortedDictionary<int, SortedDictionary<int, XCell>>();

        public XSheet(string name) { Name = name; }

        public string Name { get; set; }
        public XSheetState State { get; set; }
        public int FrozenRows { get; set; }
        public int FrozenColumns { get; set; }
        /// <summary>A1-style range for the auto filter, or null.</summary>
        public string AutoFilterRange { get; set; }
        public Dictionary<int, double> ColumnWidths { get; } = new Dictionary<int, double>();
        public HashSet<int> HiddenColumns { get; } = new HashSet<int>();
        public HashSet<int> HiddenRows { get; } = new HashSet<int>();
        public List<XDataValidationList> Validations { get; } = new List<XDataValidationList>();
        /// <summary>ARGB tab color, or null.</summary>
        public string TabColorRgb { get; set; }

        public int MaxRow { get; private set; }
        public int MaxColumn { get; private set; }

        /// <summary>Row and column are 1-based.</summary>
        public void Set(int row, int col, XCell cell)
        {
            if (row < 1 || col < 1) throw new ArgumentOutOfRangeException(nameof(row));
            if (!_rows.TryGetValue(row, out var r)) { r = new SortedDictionary<int, XCell>(); _rows[row] = r; }
            r[col] = cell;
            if (row > MaxRow) MaxRow = row;
            if (col > MaxColumn) MaxColumn = col;
        }

        public XCell Get(int row, int col)
        {
            if (_rows.TryGetValue(row, out var r) && r.TryGetValue(col, out var c)) return c;
            return null;
        }

        public string GetText(int row, int col) => Get(row, col)?.AsText() ?? string.Empty;

        public IEnumerable<KeyValuePair<int, SortedDictionary<int, XCell>>> Rows => _rows;

        public bool TryGetRow(int row, out SortedDictionary<int, XCell> cells) => _rows.TryGetValue(row, out cells);
    }

    public sealed class XWorkbook
    {
        public List<XSheet> Sheets { get; } = new List<XSheet>();

        public XSheet Find(string name)
        {
            foreach (var s in Sheets) if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }
    }

    public static class CellRef
    {
        public static string ColumnName(int col)
        {
            if (col < 1) throw new ArgumentOutOfRangeException(nameof(col));
            var chars = new Stack<char>();
            while (col > 0)
            {
                int m = (col - 1) % 26;
                chars.Push((char)('A' + m));
                col = (col - 1) / 26;
            }
            return new string(chars.ToArray());
        }

        public static string Ref(int row, int col) => ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);

        /// <summary>Parses "AB12" into (12, 28). Returns false for invalid refs.</summary>
        public static bool TryParse(string reference, out int row, out int col)
        {
            row = 0; col = 0;
            if (string.IsNullOrEmpty(reference)) return false;
            int i = 0;
            while (i < reference.Length && char.IsLetter(reference[i]))
            {
                col = col * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
                i++;
            }
            if (i == 0 || i == reference.Length) return false;
            return int.TryParse(reference.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out row) && row > 0;
        }
    }
}
