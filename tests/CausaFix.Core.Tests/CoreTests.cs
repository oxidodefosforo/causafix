using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CausaFix.Core.Model;
using CausaFix.Core.Values;
using CausaFix.Core.Xlsx;

namespace CausaFix.Core.Tests
{
    public static class KeyTests
    {
        [Test]
        public static void ParamKeys_RoundTrip_AllSources()
        {
            var keys = new[]
            {
                ParamKey.BuiltIn(-1010106, ParamScope.Instance),
                ParamKey.BuiltIn(-1010105, ParamScope.Type),
                ParamKey.Shared(TestData.QaA, ParamScope.Instance),
                ParamKey.Project(123456789012L, ParamScope.Type),
                ParamKey.Family("autodesk.spec.aec:length-2.0.0", "Head: Height@x %20 / ü", ParamScope.Instance),
                ParamKey.Pseudo("Category")
            };
            foreach (var k in keys)
            {
                Assert.True(ParamKey.TryParse(k.ToString(), out var back), "parse " + k);
                Assert.Equal(k, back);
                Assert.Equal(k.GetHashCode(), back.GetHashCode());
            }
            var fam = keys[4];
            Assert.Equal("Head: Height@x %20 / ü", fam.FamilyName);
            Assert.Equal("autodesk.spec.aec:length-2.0.0", fam.FamilySpec);
        }

        [Test]
        public static void ParamKeys_HomonymsAndScopesAreDistinct()
        {
            var a = ParamKey.Shared(TestData.QaA, ParamScope.Instance);
            var b = ParamKey.Shared(TestData.QaB, ParamScope.Instance);
            Assert.False(a == b, "different GUIDs, same name must differ");
            Assert.False(a == a.WithScope(ParamScope.Type), "instance vs type must differ");
            var f1 = ParamKey.Family("autodesk.spec:spec.string-2.0.0", "Width", ParamScope.Instance);
            var f2 = ParamKey.Family("autodesk.spec.aec:length-2.0.0", "Width", ParamScope.Instance);
            Assert.False(f1 == f2, "same name, different data type must differ");
        }

        [Test]
        public static void ParamKeys_RejectGarbage()
        {
            foreach (var s in new[] { "", "X:1@I", "BIP:abc@I", "SP:not-a-guid@T", "BIP:1@Q", "FP:nocolon@I", "BIP:1", "PS:" })
                Assert.False(ParamKey.TryParse(s, out _), "should reject '" + s + "'");
        }

        [Test]
        public static void RawValues_EncodeDecodeExactly()
        {
            var values = new[]
            {
                RawValue.NotApplicable, RawValue.Empty, RawValue.FromText(""), RawValue.FromText("s:tricky\r\n:colons:"),
                RawValue.FromInteger(-42), RawValue.FromDouble(0.1), RawValue.FromDouble(1.0 / 3.0), RawValue.FromDouble(-1e-300),
                RawValue.FromDouble(123456789.123456789), RawValue.FromElementId(TestData.BigId)
            };
            foreach (var v in values)
            {
                Assert.True(RawValue.TryDecode(v.Encode(), out var back), "decode " + v.Encode());
                Assert.Equal(v.Kind, back.Kind);
                Assert.Equal(v.Text, back.Text);
                Assert.Equal(v.Integer, back.Integer);
                Assert.True(v.Double.Equals(back.Double), "exact double " + v.Double.ToString("R"));
            }
        }

        [Test]
        public static void NotApplicable_IsNeitherEmptyNorZero()
        {
            Assert.False(ValueComparer.AreEqual(RawValue.NotApplicable, RawValue.Empty));
            Assert.False(ValueComparer.AreEqual(RawValue.NotApplicable, RawValue.FromInteger(0)));
            Assert.False(ValueComparer.AreEqual(RawValue.NotApplicable, RawValue.FromDouble(0)));
            Assert.True(ValueComparer.AreEqual(RawValue.Empty, RawValue.FromText("")));
        }
    }

    public static class NumberTests
    {
        private static void Ok(string s, DecimalSeparatorMode m, double expected)
        {
            Assert.True(NumberText.TryParse(s, m, out double v, out string err), "'" + s + "' (" + m + ") failed: " + err);
            Assert.Near(expected, v, 1e-12, "'" + s + "' (" + m + ")");
        }

        private static void Bad(string s, DecimalSeparatorMode m)
        {
            Assert.False(NumberText.TryParse(s, m, out double v, out string err), "'" + s + "' (" + m + ") should fail but gave " + v);
            Assert.True(!string.IsNullOrEmpty(err), "error message expected");
        }

        [Test]
        public static void Auto_AcceptsOnlyUnambiguousInput()
        {
            var a = DecimalSeparatorMode.Auto;
            Ok("1200", a, 1200);
            Ok("1200.5", a, 1200.5);
            Ok("1200,5", a, 1200.5);
            Ok("0,125", a, 0.125);
            Ok(",5", a, 0.5);
            Ok("-3.25", a, -3.25);
            Ok("1.234,5", a, 1234.5);
            Ok("1,234.5", a, 1234.5);
            Ok("1.234.567", a, 1234567);
            Ok("1,234,567", a, 1234567);
            Ok("1 234,5", a, 1234.5);
            Ok("1.5E3", a, 1500);
            Ok("  42  ", a, 42);
            Bad("1,234", a);
            Bad("1.234", a);
            Bad("12,345", a);
            Bad("1,2,3", a);
            Bad("1.234,5,6", a);
            Bad("1200 mm", a);
            Bad("abc", a);
            Bad("1,5E3", a);
            Bad("", a);
            Bad("-", a);
        }

        [Test]
        public static void ExplicitSeparator_ResolvesAmbiguity()
        {
            Ok("1,234", DecimalSeparatorMode.Point, 1234);
            Ok("1,234", DecimalSeparatorMode.Comma, 1.234);
            Ok("1.234", DecimalSeparatorMode.Point, 1.234);
            Ok("1.234", DecimalSeparatorMode.Comma, 1234);
            Bad("1,5", DecimalSeparatorMode.Point);       // ',' would be grouping with a 1-digit group
            Bad("1.234,5", DecimalSeparatorMode.Point);   // decimal ',' contradicts the setting
            Bad("1,1,1", DecimalSeparatorMode.Comma);
        }

        [Test]
        public static void DoubleTolerance_IgnoresRoundTripNoiseButNotEdits()
        {
            double feet = 1200.5 / 304.8;
            double back = (feet * 304.8) / 304.8;
            Assert.True(ValueComparer.DoublesEqual(feet, back), "unit round trip");
            double excel15 = double.Parse((feet * 304.8).ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) / 304.8;
            Assert.True(ValueComparer.DoublesEqual(feet, excel15), "Excel 15 significant digits");
            Assert.False(ValueComparer.DoublesEqual(1200.5 / 304.8, 1200.6 / 304.8), "0.1 mm edit must be detected");
            Assert.False(ValueComparer.DoublesEqual(1e-6, 2e-6), "small but real values must differ");
            Assert.True(ValueComparer.DoublesEqual(0, 1e-14), "noise near zero");
        }

        [Test]
        public static void Proposals_ParseByColumnKind()
        {
            var units = new FakeUnits();
            var opt = new ImportOptions();
            var width = new ColumnDef { Key = TestData.Width, Header = "Width", Kind = ValueKind.Double, UnitTypeId = FakeUnits.Millimeters, UnitLabel = "mm" };
            var p = ProposalParser.FromCell(width, XCell.Num(1200.5), units, opt);
            Assert.Equal(ProposalKind.Value, p.Kind);
            Assert.Near(1200.5 / 304.8, p.Value.Double, 1e-15);
            p = ProposalParser.FromCell(width, XCell.Str("1200,5"), units, opt);
            Assert.Near(1200.5 / 304.8, p.Value.Double, 1e-15, "text with decimal comma");
            Assert.Equal(ProposalKind.Invalid, ProposalParser.FromCell(width, XCell.Str("1,200"), units, opt).Kind);
            Assert.Equal(ProposalKind.Clear, ProposalParser.FromCell(width, XCell.Str(" <<clear>> "), units, opt).Kind);
            Assert.Equal(ProposalKind.None, ProposalParser.FromCell(width, null, units, opt).Kind);
            Assert.Equal(ProposalKind.None, ProposalParser.FromCell(width, XCell.Str("   "), units, opt).Kind);

            var yn = new ColumnDef { Key = TestData.Emergency, Kind = ValueKind.YesNo };
            Assert.Equal(1L, ProposalParser.FromCell(yn, XCell.Str("yes"), units, opt).Value.Integer);
            Assert.Equal(0L, ProposalParser.FromCell(yn, XCell.Boolean(false), units, opt).Value.Integer);
            Assert.Equal(ProposalKind.Invalid, ProposalParser.FromCell(yn, XCell.Str("maybe"), units, opt).Kind);
            Assert.Equal(ProposalKind.Invalid, ProposalParser.FromCell(yn, XCell.Num(2), units, opt).Kind);

            var integer = new ColumnDef { Key = TestData.Count, Kind = ValueKind.Integer };
            Assert.Equal(7L, ProposalParser.FromCell(integer, XCell.Num(7), units, opt).Value.Integer);
            Assert.Equal(ProposalKind.Invalid, ProposalParser.FromCell(integer, XCell.Num(7.5), units, opt).Kind);

            var text = new ColumnDef { Key = TestData.Comments, Kind = ValueKind.Text };
            Assert.Equal("=A1+1", ProposalParser.FromCell(text, XCell.Str("=A1+1"), units, opt).Value.Text);
            Assert.Equal("  spaced ", ProposalParser.FromCell(text, XCell.Str("  spaced "), units, opt).Value.Text);
            var numAsText = ProposalParser.FromCell(text, XCell.Num(12), units, opt);
            Assert.Equal("12", numAsText.Value.Text);
            Assert.True(numAsText.Info != null, "info about number converted to text");
        }

        [Test]
        public static void Proposals_FormulasUseSavedResultOnly()
        {
            var units = new FakeUnits();
            var width = new ColumnDef { Key = TestData.Width, Kind = ValueKind.Double, UnitTypeId = FakeUnits.Millimeters, UnitLabel = "mm" };
            var withResult = new XCell { Formula = "A1*2", HasCachedValue = true, Kind = XCellKind.Number, Number = 900 };
            var p = ProposalParser.FromCell(width, withResult, units, new ImportOptions());
            Assert.Equal(ProposalKind.Value, p.Kind);
            Assert.Near(900 / 304.8, p.Value.Double, 1e-15);
            Assert.Contains("=A1*2", p.Info);

            var noResult = new XCell { Formula = "A1*2", HasCachedValue = false, Kind = XCellKind.Blank };
            p = ProposalParser.FromCell(width, noResult, units, new ImportOptions());
            Assert.Equal(ProposalKind.Invalid, p.Kind);
            Assert.Contains("no saved result", p.Message);

            var error = new XCell { Formula = "1/0", HasCachedValue = true, Kind = XCellKind.Error, Text = "#DIV/0!" };
            Assert.Equal(ProposalKind.Invalid, ProposalParser.FromCell(width, error, units, new ImportOptions()).Kind);
        }
    }

    public static class XmlCodecTests
    {
        [Test]
        public static void Codec_RoundTripsAnyText()
        {
            var samples = new[]
            {
                "plain", "", "tab\tnewline\nCR\r\nend", "\u0001\u0002\u001F ctrl", "literal _x0041_ escape", "_x005F_ already",
                "emoji 😀 ok", "lone \uD800 surrogate", "trailing _x12", "_xZZZZ_ not hex"
            };
            foreach (var s in samples)
            {
                string enc = XmlTextCodec.Encode(s);
                foreach (char c in enc)
                    Assert.True(c == '\t' || c == '\n' || c >= 0x20, "encoded text contains invalid char for: " + s);
                Assert.Equal(s, XmlTextCodec.Decode(enc), "round trip of '" + s + "'");
            }
        }
    }
}
