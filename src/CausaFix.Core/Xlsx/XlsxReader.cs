using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CausaFix.Core.Xlsx
{
    public sealed class XlsxFormatException : Exception
    {
        public XlsxFormatException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Dependency-free SpreadsheetML reader. Reads values, formulas and their cached results, and sheet
    /// visibility. Styles are not read (the importer never relies on formatting).
    /// It does not evaluate formulas: a formula cell exposes only the result Excel stored when saving.
    /// </summary>
    public static class XlsxReader
    {
        private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string NsStrictMain = "http://purl.oclc.org/ooxml/spreadsheetml/main";

        public static XWorkbook Load(string path)
        {
            // FileShare.ReadWrite lets us read a workbook that is still open in Excel (its last saved state).
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return Load(fs);
            }
        }

        public static XWorkbook Load(Stream stream)
        {
            ZipArchive zip;
            try { zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true); }
            catch (InvalidDataException ex) { throw new XlsxFormatException("The file is not a valid .xlsx workbook (it may be .xls, .csv or encrypted).", ex); }

            using (zip)
            {
                var wbEntry = Find(zip, "xl/workbook.xml");
                if (wbEntry == null) throw new XlsxFormatException("The file is not a valid .xlsx workbook (xl/workbook.xml missing).");

                var rels = ReadRels(zip, "xl/_rels/workbook.xml.rels");
                string sstPath = null;
                foreach (var r in rels.Values)
                    if (r.Type.EndsWith("/sharedStrings", StringComparison.Ordinal)) sstPath = ResolveTarget("xl/", r.Target);
                var sst = sstPath != null ? ReadSharedStrings(zip, sstPath) : new List<string>();

                var wb = new XWorkbook();
                using (var s = wbEntry.Open())
                using (var x = XmlReader.Create(s, ReaderSettings()))
                {
                    while (x.Read())
                    {
                        if (x.NodeType != XmlNodeType.Element || x.LocalName != "sheet") continue;
                        string name = x.GetAttribute("name");
                        string state = x.GetAttribute("state");
                        string rid = x.GetAttribute("id", NsRel) ?? x.GetAttribute("r:id");
                        if (rid == null)
                        {
                            // strict namespace fallback
                            for (int i = 0; i < x.AttributeCount; i++)
                            {
                                x.MoveToAttribute(i);
                                if (x.LocalName == "id" && x.Prefix.Length > 0) { rid = x.Value; break; }
                            }
                            x.MoveToElement();
                        }
                        var sheet = new XSheet(name)
                        {
                            State = state == "veryHidden" ? XSheetState.VeryHidden : state == "hidden" ? XSheetState.Hidden : XSheetState.Visible
                        };
                        if (rid != null && rels.TryGetValue(rid, out var rel))
                        {
                            string part = ResolveTarget("xl/", rel.Target);
                            var entry = Find(zip, part);
                            if (entry != null) ReadSheet(entry, sheet, sst);
                        }
                        wb.Sheets.Add(sheet);
                    }
                }
                return wb;
            }
        }

        private static XmlReaderSettings ReaderSettings() => new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            CheckCharacters = false
        };

        private static ZipArchiveEntry Find(ZipArchive zip, string path)
        {
            path = path.TrimStart('/');
            var e = zip.GetEntry(path);
            if (e != null) return e;
            foreach (var x in zip.Entries)
                if (string.Equals(x.FullName.TrimStart('/'), path, StringComparison.OrdinalIgnoreCase)) return x;
            return null;
        }

        private static string ResolveTarget(string baseDir, string target)
        {
            if (target.StartsWith("/", StringComparison.Ordinal)) return target.TrimStart('/');
            var parts = new List<string>(baseDir.TrimEnd('/').Split('/'));
            foreach (var seg in target.Split('/'))
            {
                if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (seg != "." && seg.Length > 0) parts.Add(seg);
            }
            return string.Join("/", parts);
        }

        private sealed class Rel { public string Type; public string Target; }

        private static Dictionary<string, Rel> ReadRels(ZipArchive zip, string path)
        {
            var map = new Dictionary<string, Rel>(StringComparer.Ordinal);
            var e = Find(zip, path);
            if (e == null) return map;
            using (var s = e.Open())
            using (var x = XmlReader.Create(s, ReaderSettings()))
            {
                while (x.Read())
                {
                    if (x.NodeType == XmlNodeType.Element && x.LocalName == "Relationship")
                        map[x.GetAttribute("Id")] = new Rel { Type = x.GetAttribute("Type") ?? "", Target = x.GetAttribute("Target") ?? "" };
                }
            }
            return map;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip, string path)
        {
            var list = new List<string>();
            var e = Find(zip, path);
            if (e == null) return list;
            using (var s = e.Open())
            using (var x = XmlReader.Create(s, ReaderSettings()))
            {
                while (x.Read())
                {
                    if (x.NodeType == XmlNodeType.Element && x.LocalName == "si")
                        list.Add(ReadRichText(x));
                }
            }
            return list;
        }

        /// <summary>Reads an si/is element: concatenates t elements, ignoring phonetic runs (rPh).</summary>
        private static string ReadRichText(XmlReader x)
        {
            if (x.IsEmptyElement) return string.Empty;
            var sb = new StringBuilder();
            int depth = x.Depth;
            x.Read();
            while (!x.EOF && !(x.NodeType == XmlNodeType.EndElement && x.Depth == depth))
            {
                if (x.NodeType == XmlNodeType.Element)
                {
                    if (x.LocalName == "rPh") { x.Skip(); continue; }
                    if (x.LocalName == "t") { sb.Append(x.ReadElementContentAsString()); continue; }
                }
                x.Read();
            }
            return XmlTextCodec.Decode(sb.ToString());
        }

        private static void ReadSheet(ZipArchiveEntry entry, XSheet sheet, List<string> sst)
        {
            using (var s = entry.Open())
            using (var x = XmlReader.Create(s, ReaderSettings()))
            {
                int currentRow = 0;
                int nextCol = 1;
                while (x.Read())
                {
                    if (x.NodeType != XmlNodeType.Element) continue;
                    if (x.LocalName == "row")
                    {
                        string r = x.GetAttribute("r");
                        currentRow = r != null && int.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out int rr) ? rr : currentRow + 1;
                        nextCol = 1;
                        if (x.GetAttribute("hidden") == "1" || x.GetAttribute("hidden") == "true") sheet.HiddenRows.Add(currentRow);
                    }
                    else if (x.LocalName == "c")
                    {
                        string reference = x.GetAttribute("r");
                        int row = currentRow, col = nextCol;
                        if (reference != null && CellRef.TryParse(reference, out int pr, out int pc)) { row = pr; col = pc; }
                        nextCol = col + 1;
                        string t = x.GetAttribute("t") ?? "n";
                        var cell = ReadCell(x, t, sst);
                        if (cell != null && row > 0) sheet.Set(row, col, cell);
                    }
                }
            }
        }

        private static XCell ReadCell(XmlReader x, string t, List<string> sst)
        {
            string v = null, f = null, inline = null;
            bool hasV = false;
            if (!x.IsEmptyElement)
            {
                int depth = x.Depth;
                x.Read();
                while (!x.EOF && !(x.NodeType == XmlNodeType.EndElement && x.Depth == depth))
                {
                    if (x.NodeType == XmlNodeType.Element)
                    {
                        if (x.LocalName == "v") { hasV = true; v = x.ReadElementContentAsString(); continue; }
                        if (x.LocalName == "f") { f = x.ReadElementContentAsString(); continue; }
                        if (x.LocalName == "is") { inline = ReadRichText(x); x.Read(); continue; }
                        x.Skip();
                        continue;
                    }
                    x.Read();
                }
            }

            var cell = new XCell { Formula = f, HasCachedValue = f != null && hasV };
            switch (t)
            {
                case "s":
                    if (!hasV) { cell.Kind = XCellKind.Blank; break; }
                    if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int idx) || idx < 0 || idx >= sst.Count)
                        throw new XlsxFormatException("Invalid shared string reference.");
                    cell.Kind = XCellKind.Text; cell.Text = sst[idx];
                    break;
                case "inlineStr":
                    cell.Kind = XCellKind.Text; cell.Text = inline ?? string.Empty;
                    if (f != null) cell.HasCachedValue = inline != null;
                    break;
                case "str":
                    if (!hasV) { cell.Kind = XCellKind.Blank; break; }
                    cell.Kind = XCellKind.Text; cell.Text = XmlTextCodec.Decode(v);
                    break;
                case "b":
                    if (!hasV) { cell.Kind = XCellKind.Blank; break; }
                    cell.Kind = XCellKind.Bool; cell.Bool = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "e":
                    cell.Kind = XCellKind.Error; cell.Text = v ?? "#ERROR";
                    break;
                case "d":
                    // ISO 8601 date stored as text; CausaFix does not use dates — kept as text.
                    if (!hasV) { cell.Kind = XCellKind.Blank; break; }
                    cell.Kind = XCellKind.Text; cell.Text = v;
                    break;
                default:
                    if (!hasV || string.IsNullOrEmpty(v)) { cell.Kind = XCellKind.Blank; break; }
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    { cell.Kind = XCellKind.Number; cell.Number = d; }
                    else
                    { cell.Kind = XCellKind.Error; cell.Text = "#VALUE!"; }
                    break;
            }
            if (cell.Kind == XCellKind.Blank && f == null) return null;
            return cell;
        }
    }
}
