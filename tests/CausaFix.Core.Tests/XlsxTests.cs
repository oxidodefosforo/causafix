using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using CausaFix.Core.Xlsx;

namespace CausaFix.Core.Tests
{
    public static class XlsxTests
    {
        [Test]
        public static void Writer_Reader_RoundTripValuesAndSheetStates()
        {
            var wb = new XWorkbook();
            var a = new XSheet("Data") { FrozenRows = 2, FrozenColumns = 1, AutoFilterRange = "A2:D5", TabColorRgb = "FF0E7C86" };
            a.Set(1, 1, XCell.Str("key"));
            a.HiddenRows.Add(1);
            a.HiddenColumns.Add(1);
            a.Set(3, 1, XCell.Str("  spaces kept  "));
            a.Set(3, 2, XCell.Num(0.1));
            a.Set(3, 3, XCell.Num(1.0 / 3.0));
            a.Set(3, 4, XCell.Num(-1e-300));
            a.Set(4, 2, XCell.Boolean(true));
            a.Set(4, 3, XCell.Str("=SUM(A1:A2)"));
            a.Set(4, 4, XCell.Str("multi\nline\r\nwith CR"));
            a.Set(5, 27, XCell.Str("column AA"));
            a.Set(6, 2, XCell.Empty(new XStyle { FillRgb = "FFFF0000" }));
            var v = new XDataValidationList { Range = "B3:B5" };
            v.Items.Add("Yes"); v.Items.Add("No");
            a.Validations.Add(v);
            wb.Sheets.Add(a);
            wb.Sheets.Add(new XSheet("Hidden") { State = XSheetState.Hidden });
            var vh = new XSheet("VeryHidden") { State = XSheetState.VeryHidden };
            vh.Set(1, 1, XCell.Str("secret"));
            wb.Sheets.Add(vh);

            var back = TestData.RoundTrip(wb);
            var s = back.Find("Data");
            Assert.Equal("  spaces kept  ", s.GetText(3, 1));
            Assert.True(s.Get(3, 2).Number.Equals(0.1));
            Assert.True(s.Get(3, 3).Number.Equals(1.0 / 3.0), "full double precision");
            Assert.True(s.Get(3, 4).Number.Equals(-1e-300));
            Assert.Equal(XCellKind.Bool, s.Get(4, 2).Kind);
            Assert.Equal(XCellKind.Text, s.Get(4, 3).Kind);
            Assert.False(s.Get(4, 3).IsFormula, "text starting with '=' is never written as a formula");
            Assert.Equal("multi\nline\r\nwith CR", s.GetText(4, 4));
            Assert.Equal("column AA", s.GetText(5, 27));
            Assert.True(s.HiddenRows.Contains(1));
            Assert.Equal(XSheetState.Hidden, back.Find("Hidden").State);
            Assert.Equal(XSheetState.VeryHidden, back.Find("VeryHidden").State);
            Assert.Equal("secret", back.Find("VeryHidden").GetText(1, 1));
        }

        [Test]
        public static void Writer_RejectsInvalidWorkbooks()
        {
            var wb = new XWorkbook();
            wb.Sheets.Add(new XSheet("Only") { State = XSheetState.VeryHidden });
            Assert.Throws<InvalidOperationException>(() => XlsxWriter.Write(wb, new MemoryStream()));
            var wb2 = new XWorkbook();
            wb2.Sheets.Add(new XSheet("A"));
            wb2.Sheets.Add(new XSheet("a"));
            Assert.Throws<InvalidOperationException>(() => XlsxWriter.Write(wb2, new MemoryStream()));
            var wb3 = new XWorkbook();
            var s = new XSheet("F");
            s.Set(1, 1, new XCell { Formula = "1+1", Kind = XCellKind.Number });
            wb3.Sheets.Add(s);
            Assert.Throws<InvalidOperationException>(() => XlsxWriter.Write(wb3, new MemoryStream()));
        }

        [Test]
        public static void SheetNames_AreValidUniqueAndStable()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string a = SheetNames.MakeUnique("Electrical Equipment - Very Long Category Name Here", used);
            string b = SheetNames.MakeUnique("Electrical Equipment - Very Long Category Name Here", used);
            string c = SheetNames.MakeUnique("Bad/Name:[x]*?", used);
            string d = SheetNames.MakeUnique("'quoted'", used);
            string e = SheetNames.MakeUnique("history", used);
            foreach (var n in new[] { a, b, c, d, e })
                Assert.True(SheetNames.Validate(n) == null, "invalid: " + n + " -> " + SheetNames.Validate(n));
            Assert.False(string.Equals(a, b, StringComparison.OrdinalIgnoreCase), "unique");
            Assert.True(a.Length <= 31 && b.Length <= 31);
        }

        /// <summary>XML as Excel writes it: inline strings, rich text with phonetic runs, cells without r, shared formulas, cached values.</summary>
        [Test]
        public static void Reader_HandlesExcelStyleXml()
        {
            string sheet = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
<sheetData>
<row r=""1""><c r=""A1"" t=""s""><v>0</v></c><c t=""s""><v>1</v></c><c r=""D1"" t=""inlineStr""><is><t>inline</t></is></c></row>
<row r=""2""><c r=""A2""><f>B2*2</f><v>12.5</v></c><c r=""B2""><f t=""shared"" ref=""B2:B3"" si=""0"">C2+1</f><v>6.25</v></c><c r=""C2""><f>X9</f></c></row>
<row r=""3""><c r=""A3"" t=""str""><f>""a""&amp;""b""</f><v>ab</v></c><c r=""B3""><f t=""shared"" si=""0""/><v>7</v></c><c r=""C3"" t=""e""><f>1/0</f><v>#DIV/0!</v></c><c r=""D3"" t=""b""><v>1</v></c></row>
</sheetData></worksheet>";
            string sst = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sst xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" count=""2"" uniqueCount=""2"">
<si><r><rPr><b/></rPr><t>Rich </t></r><r><t xml:space=""preserve"">text_x000D_
</t></r><rPh sb=""0"" eb=""1""><t>PHONETIC</t></rPh></si>
<si><t>second</t></si></sst>";
            var bytes = BuildPackage(sheet, sst);
            var wb = XlsxReader.Load(new MemoryStream(bytes));
            var s = wb.Find("Sheet1");
            Assert.Equal("Rich text\r\n", s.GetText(1, 1));
            Assert.Equal("second", s.GetText(1, 2), "cell without r attribute follows previous");
            Assert.Equal("inline", s.GetText(1, 4));
            var a2 = s.Get(2, 1);
            Assert.True(a2.IsFormula && a2.HasCachedValue && a2.Number == 12.5, "formula with cached number");
            Assert.True(s.Get(2, 2).IsFormula && s.Get(2, 2).Number == 6.25, "shared formula master");
            var c2 = s.Get(2, 3);
            Assert.True(c2.IsFormula && !c2.HasCachedValue, "formula without cached value");
            Assert.Equal("ab", s.GetText(3, 1));
            var b3 = s.Get(3, 2);
            Assert.True(b3.IsFormula && b3.Formula == "" && b3.Number == 7, "shared formula child");
            Assert.Equal(XCellKind.Error, s.Get(3, 3).Kind);
            Assert.True(s.Get(3, 4).Bool);
        }

        [Test]
        public static void Reader_RejectsNonXlsx()
        {
            Assert.Throws<XlsxFormatException>(() => XlsxReader.Load(new MemoryStream(Encoding.ASCII.GetBytes("a,b,c\n1,2,3"))));
        }

        [Test]
        public static void Save_IsAtomicAndReportsLockedFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "out.xlsx");
                var wb = new XWorkbook();
                var s = new XSheet("S"); s.Set(1, 1, XCell.Str("v1")); wb.Sheets.Add(s);
                XlsxWriter.Save(wb, path);
                s.Set(1, 1, XCell.Str("v2"));
                XlsxWriter.Save(wb, path); // overwrite
                Assert.Equal("v2", XlsxReader.Load(path).Find("S").GetText(1, 1));
                Assert.Equal(1, Directory.GetFiles(dir).Length, "no temporary files left behind");

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        Assert.Throws<XlsxFileLockedException>(() => XlsxWriter.Save(wb, path));
                }
                Assert.Throws<DirectoryNotFoundException>(() => XlsxWriter.Save(wb, Path.Combine(dir, "missing", "x.xlsx")));
            }
            finally { Directory.Delete(dir, true); }
        }

        private static byte[] BuildPackage(string sheetXml, string sstXml)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    void Put(string name, string content)
                    {
                        using (var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(content);
                    }
                    Put("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
                    Put("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId7\"/></sheets></workbook>");
                    Put("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId7\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"/xl/worksheets/s1.xml\"/><Relationship Id=\"rId9\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
                    Put("xl/worksheets/s1.xml", sheetXml);
                    Put("xl/sharedStrings.xml", sstXml);
                }
                return ms.ToArray();
            }
        }
    }
}
