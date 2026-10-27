using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace CausaFix.Core.Xlsx
{
    public sealed class XlsxFileLockedException : IOException
    {
        public XlsxFileLockedException(string path, Exception inner)
            : base("The file is open in another program (Excel?) or cannot be replaced: " + path, inner) { }
    }

    /// <summary>
    /// Minimal, dependency-free SpreadsheetML writer (Office Open XML, ECMA-376 transitional).
    /// Supports what CausaFix needs: shared strings, numbers, booleans, cell styles, column widths,
    /// hidden rows/columns, frozen panes, auto filter, list validations, sheet visibility and tab color.
    /// Never writes formulas.
    /// </summary>
    public static class XlsxWriter
    {
        private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const string NsCt = "http://schemas.openxmlformats.org/package/2006/content-types";

        /// <summary>
        /// Writes the workbook to a temporary file next to <paramref name="path"/> and then replaces the
        /// destination, so a failure never leaves a half-written file behind.
        /// </summary>
        public static void Save(XWorkbook wb, string path)
        {
            if (wb == null) throw new ArgumentNullException(nameof(wb));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path required.", nameof(path));
            Validate(wb);

            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                throw new DirectoryNotFoundException("Folder does not exist: " + dir);

            if (File.Exists(full))
            {
                try
                {
                    using (new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                }
                catch (IOException ex) { throw new XlsxFileLockedException(full, ex); }
            }

            string temp = Path.Combine(dir, "~cf_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Write(wb, fs);
                }
                if (File.Exists(full))
                {
                    try { File.Replace(temp, full, null, true); }
                    catch (IOException ex) { throw new XlsxFileLockedException(full, ex); }
                }
                else
                {
                    File.Move(temp, full);
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            }
        }

        public static void Validate(XWorkbook wb)
        {
            if (wb.Sheets.Count == 0) throw new InvalidOperationException("A workbook needs at least one sheet.");
            if (!wb.Sheets.Any(s => s.State == XSheetState.Visible))
                throw new InvalidOperationException("A workbook needs at least one visible sheet.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in wb.Sheets)
            {
                string err = SheetNames.Validate(s.Name);
                if (err != null) throw new InvalidOperationException(err);
                if (!names.Add(s.Name)) throw new InvalidOperationException("Duplicate sheet name: " + s.Name);
            }
        }

        public static void Write(XWorkbook wb, Stream output)
        {
            Validate(wb);
            var styles = new StyleTable();
            var strings = new SharedStrings();

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Worksheets first, so shared strings and styles are collected.
                for (int i = 0; i < wb.Sheets.Count; i++)
                {
                    WritePart(zip, "xl/worksheets/sheet" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml",
                        w => WriteSheet(w, wb.Sheets[i], styles, strings, i == FirstVisible(wb)));
                }
                WritePart(zip, "[Content_Types].xml", w => WriteContentTypes(w, wb));
                WritePart(zip, "_rels/.rels", WriteRootRels);
                WritePart(zip, "xl/workbook.xml", w => WriteWorkbook(w, wb));
                WritePart(zip, "xl/_rels/workbook.xml.rels", w => WriteWorkbookRels(w, wb));
                WritePart(zip, "xl/styles.xml", styles.Write);
                WritePart(zip, "xl/sharedStrings.xml", strings.Write);
            }
        }

        private static int FirstVisible(XWorkbook wb)
        {
            for (int i = 0; i < wb.Sheets.Count; i++) if (wb.Sheets[i].State == XSheetState.Visible) return i;
            return 0;
        }

        private static void WritePart(ZipArchive zip, string name, Action<XmlWriter> body)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = entry.Open())
            using (var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CheckCharacters = true }))
            {
                w.WriteStartDocument(true);
                body(w);
                w.WriteEndDocument();
            }
        }

        private static void WriteContentTypes(XmlWriter w, XWorkbook wb)
        {
            w.WriteStartElement("Types", NsCt);
            Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
            Default(w, "xml", "application/xml");
            Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            for (int i = 0; i < wb.Sheets.Count; i++)
                Override(w, "/xl/worksheets/sheet" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            Override(w, "/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml");
            w.WriteEndElement();

            void Default(XmlWriter x, string ext, string ct) { x.WriteStartElement("Default", NsCt); x.WriteAttributeString("Extension", ext); x.WriteAttributeString("ContentType", ct); x.WriteEndElement(); }
            void Override(XmlWriter x, string part, string ct) { x.WriteStartElement("Override", NsCt); x.WriteAttributeString("PartName", part); x.WriteAttributeString("ContentType", ct); x.WriteEndElement(); }
        }

        private static void WriteRootRels(XmlWriter w)
        {
            w.WriteStartElement("Relationships", NsPkgRel);
            Rel(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
            w.WriteEndElement();
        }

        private static void Rel(XmlWriter w, string id, string type, string target)
        {
            w.WriteStartElement("Relationship", NsPkgRel);
            w.WriteAttributeString("Id", id);
            w.WriteAttributeString("Type", type);
            w.WriteAttributeString("Target", target);
            w.WriteEndElement();
        }

        private static void WriteWorkbookRels(XmlWriter w, XWorkbook wb)
        {
            w.WriteStartElement("Relationships", NsPkgRel);
            int n = wb.Sheets.Count;
            for (int i = 0; i < n; i++)
                Rel(w, "rId" + (i + 1).ToString(CultureInfo.InvariantCulture),
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                    "worksheets/sheet" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".xml");
            Rel(w, "rId" + (n + 1).ToString(CultureInfo.InvariantCulture), "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
            Rel(w, "rId" + (n + 2).ToString(CultureInfo.InvariantCulture), "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings", "sharedStrings.xml");
            w.WriteEndElement();
        }

        private static void WriteWorkbook(XmlWriter w, XWorkbook wb)
        {
            w.WriteStartElement("workbook", NsMain);
            w.WriteAttributeString("xmlns", "r", null, NsRel);
            w.WriteStartElement("bookViews", NsMain);
            w.WriteStartElement("workbookView", NsMain);
            w.WriteAttributeString("activeTab", FirstVisible(wb).ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("sheets", NsMain);
            for (int i = 0; i < wb.Sheets.Count; i++)
            {
                var s = wb.Sheets[i];
                w.WriteStartElement("sheet", NsMain);
                w.WriteAttributeString("name", s.Name);
                w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                if (s.State == XSheetState.Hidden) w.WriteAttributeString("state", "hidden");
                else if (s.State == XSheetState.VeryHidden) w.WriteAttributeString("state", "veryHidden");
                w.WriteAttributeString("id", NsRel, "rId" + (i + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
            w.WriteEndElement();

            bool anyFilter = wb.Sheets.Any(s => !string.IsNullOrEmpty(s.AutoFilterRange));
            if (anyFilter)
            {
                w.WriteStartElement("definedNames", NsMain);
                for (int i = 0; i < wb.Sheets.Count; i++)
                {
                    var s = wb.Sheets[i];
                    if (string.IsNullOrEmpty(s.AutoFilterRange)) continue;
                    w.WriteStartElement("definedName", NsMain);
                    w.WriteAttributeString("name", "_xlnm._FilterDatabase");
                    w.WriteAttributeString("localSheetId", i.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("hidden", "1");
                    w.WriteString("'" + s.Name.Replace("'", "''") + "'!" + AbsoluteRange(s.AutoFilterRange));
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        private static string AbsoluteRange(string range)
        {
            var parts = range.Split(':');
            var outParts = new List<string>();
            foreach (var p in parts)
            {
                if (CellRef.TryParse(p, out int r, out int c)) outParts.Add("$" + CellRef.ColumnName(c) + "$" + r.ToString(CultureInfo.InvariantCulture));
                else outParts.Add(p);
            }
            return string.Join(":", outParts);
        }

        private static void WriteSheet(XmlWriter w, XSheet s, StyleTable styles, SharedStrings strings, bool selected)
        {
            w.WriteStartElement("worksheet", NsMain);
            w.WriteAttributeString("xmlns", "r", null, NsRel);

            if (!string.IsNullOrEmpty(s.TabColorRgb))
            {
                w.WriteStartElement("sheetPr", NsMain);
                w.WriteStartElement("tabColor", NsMain);
                w.WriteAttributeString("rgb", s.TabColorRgb);
                w.WriteEndElement();
                w.WriteEndElement();
            }

            int maxRow = Math.Max(1, s.MaxRow);
            int maxCol = Math.Max(1, s.MaxColumn);
            w.WriteStartElement("dimension", NsMain);
            w.WriteAttributeString("ref", "A1:" + CellRef.Ref(maxRow, maxCol));
            w.WriteEndElement();

            w.WriteStartElement("sheetViews", NsMain);
            w.WriteStartElement("sheetView", NsMain);
            if (selected) w.WriteAttributeString("tabSelected", "1");
            w.WriteAttributeString("workbookViewId", "0");
            if (s.FrozenRows > 0 || s.FrozenColumns > 0)
            {
                w.WriteStartElement("pane", NsMain);
                if (s.FrozenColumns > 0) w.WriteAttributeString("xSplit", s.FrozenColumns.ToString(CultureInfo.InvariantCulture));
                if (s.FrozenRows > 0) w.WriteAttributeString("ySplit", s.FrozenRows.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("topLeftCell", CellRef.Ref(s.FrozenRows + 1, s.FrozenColumns + 1));
                string pane = s.FrozenRows > 0 && s.FrozenColumns > 0 ? "bottomRight" : s.FrozenRows > 0 ? "bottomLeft" : "topRight";
                w.WriteAttributeString("activePane", pane);
                w.WriteAttributeString("state", "frozen");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("sheetFormatPr", NsMain);
            w.WriteAttributeString("defaultRowHeight", "15");
            w.WriteEndElement();

            var colIdx = new SortedSet<int>(s.ColumnWidths.Keys);
            colIdx.UnionWith(s.HiddenColumns);
            if (colIdx.Count > 0)
            {
                w.WriteStartElement("cols", NsMain);
                foreach (int c in colIdx)
                {
                    w.WriteStartElement("col", NsMain);
                    string cs = c.ToString(CultureInfo.InvariantCulture);
                    w.WriteAttributeString("min", cs);
                    w.WriteAttributeString("max", cs);
                    double width = s.ColumnWidths.TryGetValue(c, out double wd) ? wd : 10;
                    w.WriteAttributeString("width", width.ToString("0.##", CultureInfo.InvariantCulture));
                    w.WriteAttributeString("customWidth", "1");
                    if (s.HiddenColumns.Contains(c)) w.WriteAttributeString("hidden", "1");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            w.WriteStartElement("sheetData", NsMain);
            var rowIndices = new SortedSet<int>(s.Rows.Select(r => r.Key));
            rowIndices.UnionWith(s.HiddenRows);
            foreach (int r in rowIndices)
            {
                w.WriteStartElement("row", NsMain);
                w.WriteAttributeString("r", r.ToString(CultureInfo.InvariantCulture));
                if (s.HiddenRows.Contains(r)) w.WriteAttributeString("hidden", "1");
                if (s.TryGetRow(r, out var row))
                {
                    foreach (var kv in row)
                    {
                        WriteCell(w, r, kv.Key, kv.Value, styles, strings);
                    }
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();

            if (!string.IsNullOrEmpty(s.AutoFilterRange))
            {
                w.WriteStartElement("autoFilter", NsMain);
                w.WriteAttributeString("ref", s.AutoFilterRange);
                w.WriteEndElement();
            }

            if (s.Validations.Count > 0)
            {
                w.WriteStartElement("dataValidations", NsMain);
                w.WriteAttributeString("count", s.Validations.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var v in s.Validations)
                {
                    string list = string.Join(",", v.Items);
                    if (v.Formula == null && list.Length > 255) throw new InvalidOperationException("Validation list too long.");
                    w.WriteStartElement("dataValidation", NsMain);
                    w.WriteAttributeString("type", "list");
                    w.WriteAttributeString("allowBlank", "1");
                    w.WriteAttributeString("showInputMessage", string.IsNullOrEmpty(v.Prompt) ? "0" : "1");
                    w.WriteAttributeString("showErrorMessage", "1");
                    w.WriteAttributeString("errorStyle", v.Strict ? "stop" : "information");
                    if (!string.IsNullOrEmpty(v.Prompt)) w.WriteAttributeString("prompt", Truncate(v.Prompt, 255));
                    w.WriteAttributeString("sqref", v.Range);
                    w.WriteElementString("formula1", NsMain, v.Formula ?? "\"" + list + "\"");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            w.WriteStartElement("pageMargins", NsMain);
            w.WriteAttributeString("left", "0.7"); w.WriteAttributeString("right", "0.7");
            w.WriteAttributeString("top", "0.75"); w.WriteAttributeString("bottom", "0.75");
            w.WriteAttributeString("header", "0.3"); w.WriteAttributeString("footer", "0.3");
            w.WriteEndElement();

            w.WriteEndElement();
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        private static void WriteCell(XmlWriter w, int r, int c, XCell cell, StyleTable styles, SharedStrings strings)
        {
            if (cell == null) return;
            if (cell.Formula != null) throw new InvalidOperationException("CausaFixIt never writes formulas.");
            int styleIndex = styles.IndexOf(cell.Style);
            w.WriteStartElement("c", NsMain);
            w.WriteAttributeString("r", CellRef.Ref(r, c));
            if (styleIndex > 0) w.WriteAttributeString("s", styleIndex.ToString(CultureInfo.InvariantCulture));
            switch (cell.Kind)
            {
                case XCellKind.Text:
                    w.WriteAttributeString("t", "s");
                    w.WriteElementString("v", NsMain, strings.Add(cell.Text ?? string.Empty).ToString(CultureInfo.InvariantCulture));
                    break;
                case XCellKind.Number:
                    if (double.IsNaN(cell.Number) || double.IsInfinity(cell.Number))
                        throw new InvalidOperationException("Cannot write a non-finite number to " + CellRef.Ref(r, c));
                    w.WriteElementString("v", NsMain, cell.Number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case XCellKind.Bool:
                    w.WriteAttributeString("t", "b");
                    w.WriteElementString("v", NsMain, cell.Bool ? "1" : "0");
                    break;
                case XCellKind.Error:
                    w.WriteAttributeString("t", "e");
                    w.WriteElementString("v", NsMain, cell.Text ?? "#N/A");
                    break;
                case XCellKind.Blank:
                default:
                    break;
            }
            w.WriteEndElement();
        }

        private sealed class SharedStrings
        {
            private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<string> _list = new List<string>();
            private int _count;

            public int Add(string s)
            {
                _count++;
                if (_index.TryGetValue(s, out int i)) return i;
                i = _list.Count;
                _list.Add(s);
                _index[s] = i;
                return i;
            }

            public void Write(XmlWriter w)
            {
                w.WriteStartElement("sst", NsMain);
                w.WriteAttributeString("count", _count.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("uniqueCount", _list.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var s in _list)
                {
                    w.WriteStartElement("si", NsMain);
                    w.WriteStartElement("t", NsMain);
                    if (s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1]) || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0))
                        w.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                    w.WriteString(XmlTextCodec.Encode(s));
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
        }

        private sealed class StyleTable
        {
            private readonly List<XStyle> _xfs = new List<XStyle> { null };
            private readonly Dictionary<string, int> _bySig = new Dictionary<string, int>(StringComparer.Ordinal);

            public int IndexOf(XStyle style)
            {
                if (style == null) return 0;
                string sig = style.Signature();
                if (_bySig.TryGetValue(sig, out int i)) return i;
                i = _xfs.Count;
                _xfs.Add(style);
                _bySig[sig] = i;
                return i;
            }

            public void Write(XmlWriter w)
            {
                var fonts = new List<string> { FontSig(null) };
                var fills = new List<string> { "none", "gray125" };
                var borders = new List<string> { BorderSig(null) };
                var xfFont = new List<int>(); var xfFill = new List<int>(); var xfBorder = new List<int>();
                foreach (var s in _xfs)
                {
                    xfFont.Add(IndexOrAdd(fonts, FontSig(s)));
                    xfFill.Add(s?.FillRgb == null ? 0 : IndexOrAdd(fills, "solid:" + s.FillRgb));
                    xfBorder.Add(IndexOrAdd(borders, BorderSig(s)));
                }

                w.WriteStartElement("styleSheet", NsMain);

                w.WriteStartElement("fonts", NsMain);
                w.WriteAttributeString("count", fonts.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var f in fonts) WriteFont(w, f);
                w.WriteEndElement();

                w.WriteStartElement("fills", NsMain);
                w.WriteAttributeString("count", fills.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var f in fills)
                {
                    w.WriteStartElement("fill", NsMain);
                    w.WriteStartElement("patternFill", NsMain);
                    if (f == "none" || f == "gray125") w.WriteAttributeString("patternType", f);
                    else
                    {
                        w.WriteAttributeString("patternType", "solid");
                        w.WriteStartElement("fgColor", NsMain); w.WriteAttributeString("rgb", f.Substring(6)); w.WriteEndElement();
                        w.WriteStartElement("bgColor", NsMain); w.WriteAttributeString("indexed", "64"); w.WriteEndElement();
                    }
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();

                w.WriteStartElement("borders", NsMain);
                w.WriteAttributeString("count", borders.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var b in borders) WriteBorder(w, b);
                w.WriteEndElement();

                w.WriteStartElement("cellStyleXfs", NsMain);
                w.WriteAttributeString("count", "1");
                w.WriteStartElement("xf", NsMain);
                w.WriteAttributeString("numFmtId", "0"); w.WriteAttributeString("fontId", "0");
                w.WriteAttributeString("fillId", "0"); w.WriteAttributeString("borderId", "0");
                w.WriteEndElement();
                w.WriteEndElement();

                w.WriteStartElement("cellXfs", NsMain);
                w.WriteAttributeString("count", _xfs.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < _xfs.Count; i++)
                {
                    var s = _xfs[i];
                    w.WriteStartElement("xf", NsMain);
                    w.WriteAttributeString("numFmtId", (s?.NumFmtId ?? 0).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("fontId", xfFont[i].ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("fillId", xfFill[i].ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("borderId", xfBorder[i].ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("xfId", "0");
                    if (s != null)
                    {
                        if (s.NumFmtId != 0) w.WriteAttributeString("applyNumberFormat", "1");
                        w.WriteAttributeString("applyFont", "1");
                        if (s.FillRgb != null) w.WriteAttributeString("applyFill", "1");
                        if (xfBorder[i] != 0) w.WriteAttributeString("applyBorder", "1");
                        if (s.Wrap) w.WriteAttributeString("applyAlignment", "1");
                        if (!s.Locked) w.WriteAttributeString("applyProtection", "1");
                        if (s.Wrap)
                        {
                            w.WriteStartElement("alignment", NsMain);
                            w.WriteAttributeString("vertical", "top");
                            w.WriteAttributeString("wrapText", "1");
                            w.WriteEndElement();
                        }
                        if (!s.Locked)
                        {
                            w.WriteStartElement("protection", NsMain);
                            w.WriteAttributeString("locked", "0");
                            w.WriteEndElement();
                        }
                    }
                    w.WriteEndElement();
                }
                w.WriteEndElement();

                w.WriteStartElement("cellStyles", NsMain);
                w.WriteAttributeString("count", "1");
                w.WriteStartElement("cellStyle", NsMain);
                w.WriteAttributeString("name", "Normal"); w.WriteAttributeString("xfId", "0"); w.WriteAttributeString("builtinId", "0");
                w.WriteEndElement();
                w.WriteEndElement();

                w.WriteEndElement();
            }

            private static int IndexOrAdd(List<string> list, string sig)
            {
                int i = list.IndexOf(sig);
                if (i >= 0) return i;
                list.Add(sig);
                return list.Count - 1;
            }

            private static string FontSig(XStyle s)
            {
                if (s == null) return "0|0||11";
                return (s.Bold ? "1" : "0") + "|" + (s.Italic ? "1" : "0") + "|" + (s.FontRgb ?? "") + "|" + s.FontSize.ToString(CultureInfo.InvariantCulture);
            }

            private static void WriteFont(XmlWriter w, string sig)
            {
                var p = sig.Split('|');
                w.WriteStartElement("font", NsMain);
                if (p[0] == "1") { w.WriteStartElement("b", NsMain); w.WriteEndElement(); }
                if (p[1] == "1") { w.WriteStartElement("i", NsMain); w.WriteEndElement(); }
                w.WriteStartElement("sz", NsMain); w.WriteAttributeString("val", p[3]); w.WriteEndElement();
                if (p[2].Length > 0) { w.WriteStartElement("color", NsMain); w.WriteAttributeString("rgb", p[2]); w.WriteEndElement(); }
                w.WriteStartElement("name", NsMain); w.WriteAttributeString("val", "Calibri"); w.WriteEndElement();
                w.WriteStartElement("family", NsMain); w.WriteAttributeString("val", "2"); w.WriteEndElement();
                w.WriteEndElement();
            }

            private static string BorderSig(XStyle s)
            {
                if (s == null) return "|0";
                return (s.LeftBorderRgb ?? "") + "|" + (s.ThinBorder ? "1" : "0");
            }

            private static void WriteBorder(XmlWriter w, string sig)
            {
                var p = sig.Split('|');
                string left = p[0];
                bool thin = p[1] == "1";
                w.WriteStartElement("border", NsMain);
                Side("left", left.Length > 0 ? "thick" : thin ? "thin" : null, left.Length > 0 ? left : "FFBFBFBF");
                Side("right", thin ? "thin" : null, "FFBFBFBF");
                Side("top", thin ? "thin" : null, "FFBFBFBF");
                Side("bottom", thin ? "thin" : null, "FFBFBFBF");
                w.WriteStartElement("diagonal", NsMain); w.WriteEndElement();
                w.WriteEndElement();

                void Side(string name, string style, string rgb)
                {
                    w.WriteStartElement(name, NsMain);
                    if (style != null)
                    {
                        w.WriteAttributeString("style", style);
                        w.WriteStartElement("color", NsMain); w.WriteAttributeString("rgb", rgb); w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }
            }
        }
    }

    public static class SheetNames
    {
        private static readonly char[] Invalid = { '[', ']', ':', '*', '?', '/', '\\' };

        /// <summary>Returns null when valid, otherwise an error message.</summary>
        public static string Validate(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Sheet name is empty.";
            if (name.Length > 31) return "Sheet name longer than 31 characters: " + name;
            if (name.IndexOfAny(Invalid) >= 0) return "Sheet name contains invalid characters: " + name;
            if (name[0] == '\'' || name[name.Length - 1] == '\'') return "Sheet name cannot start or end with an apostrophe: " + name;
            if (string.Equals(name, "History", StringComparison.OrdinalIgnoreCase)) return "'History' is reserved by Excel.";
            return null;
        }

        /// <summary>Builds a valid sheet name that is unique (case-insensitive) within <paramref name="used"/> and adds it.</summary>
        public static string MakeUnique(string desired, ISet<string> used)
        {
            var sb = new StringBuilder();
            foreach (char ch in desired ?? string.Empty)
            {
                if (Array.IndexOf(Invalid, ch) >= 0 || char.IsControl(ch)) sb.Append('_');
                else sb.Append(ch);
            }
            string baseName = sb.ToString().Trim().Trim('\'').Trim();
            if (baseName.Length == 0) baseName = "Sheet";
            if (string.Equals(baseName, "History", StringComparison.OrdinalIgnoreCase)) baseName = "History_";
            string candidate = Cut(baseName, 31);
            int n = 2;
            while (used.Contains(candidate) || Validate(candidate) != null)
            {
                string suffix = " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
                candidate = Cut(baseName, 31 - suffix.Length).TrimEnd('\'') + suffix;
                n++;
            }
            used.Add(candidate);
            return candidate;
        }

        private static string Cut(string s, int max)
        {
            if (s.Length <= max) return s.TrimEnd('\'');
            string c = s.Substring(0, max);
            // Do not split a surrogate pair.
            if (char.IsHighSurrogate(c[c.Length - 1])) c = c.Substring(0, c.Length - 1);
            return c.TrimEnd('\'');
        }
    }
}
