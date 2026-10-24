using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Values;
using CausaFix.Core.Workbook;
using CausaFix.Core.Xlsx;

namespace CausaFix.Core.Tests
{
    /// <summary>
    /// Acceptance tests of section A.7: the engine works by identity, owner, storage type and real
    /// writability — never by parameter names. Every fixture uses random GUIDs, random names and an
    /// arbitrary category, so no special path can be keyed on "Comments", "Type Mark" or "Height".
    /// </summary>
    public static class GenericExchangeTests
    {
        private sealed class Fixture
        {
            public readonly Random Rnd;
            public readonly ExportPackage Pkg;
            public readonly FakeModel Model;
            public readonly string Cat;
            public readonly ParamKey InstText, TypeText, TypeLength, InstYesNo, InstInt, TypeText2, InstRef;
            public readonly string[] Types;
            public readonly List<string> Instances = new List<string>();
            public readonly Dictionary<string, string> TypeOf = new Dictionary<string, string>();

            public Fixture(int seed, bool lockFirstInstanceText = false)
            {
                Rnd = new Random(seed);
                Cat = "Cat" + Rnd.Next(1000, 9999);
                string N(string p) => p + "_" + Rnd.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
                InstText = ParamKey.Shared(Guid.NewGuid(), ParamScope.Instance);
                TypeText = ParamKey.BuiltIn(-1000000 - Rnd.Next(1, 99999), ParamScope.Type);
                TypeLength = ParamKey.Family("autodesk.spec.aec:length-2.0.0", N("Len"), ParamScope.Type);
                InstYesNo = ParamKey.Project(Rnd.Next(100000, 900000), ParamScope.Instance);
                InstInt = ParamKey.Shared(Guid.NewGuid(), ParamScope.Instance);
                TypeText2 = ParamKey.Shared(Guid.NewGuid(), ParamScope.Type);
                InstRef = ParamKey.BuiltIn(-2000000 - Rnd.Next(1, 99999), ParamScope.Instance);

                var units = new FakeUnits();
                Pkg = new ExportPackage { Document = TestData.Doc(), AddinVersion = "t", RevitVersion = "2026" };
                var s = new DataSheet { SheetKey = "cat:" + Cat + ":I", Title = Cat, Source = SheetSource.ModelCategories };
                s.Columns.Add(PseudoColumns.Column(PseudoColumns.ElementId));
                s.Columns.Add(new ColumnDef { Key = InstText, Header = N("Txt"), Kind = ValueKind.Text });
                s.Columns.Add(new ColumnDef { Key = TypeText, Header = N("TT"), Kind = ValueKind.Text });
                s.Columns.Add(new ColumnDef { Key = TypeLength, Header = N("L"), Kind = ValueKind.Double, UnitTypeId = FakeUnits.Millimeters, UnitLabel = "mm" });
                s.Columns.Add(new ColumnDef { Key = InstYesNo, Header = N("YN"), Kind = ValueKind.YesNo });
                s.Columns.Add(new ColumnDef { Key = InstInt, Header = N("Int"), Kind = ValueKind.Integer });
                s.Columns.Add(new ColumnDef { Key = TypeText2, Header = N("TT2"), Kind = ValueKind.Text });
                s.Columns.Add(new ColumnDef { Key = InstRef, Header = N("Ref"), Kind = ValueKind.ElementRef, RefClass = "Level", RefCandidates = new List<string> { "A [501]", "B [502]" } });

                Types = new[] { "type-" + Guid.NewGuid().ToString("N"), "type-" + Guid.NewGuid().ToString("N") };
                int n = 0;
                foreach (var t in Types)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        n++;
                        string uid = "inst-" + Guid.NewGuid().ToString("N");
                        Instances.Add(uid);
                        TypeOf[uid] = t;
                        var r = new RowData { RowKey = uid, UniqueId = uid, ElementId = 7000 + n, TypeUniqueId = t, Label = Cat + " #" + n };
                        r.Cells[ParamKey.Pseudo(PseudoColumns.ElementId)] = new CellValue { Raw = RawValue.FromText((7000 + n).ToString()), DisplayText = (7000 + n).ToString() };
                        var txt = TestData.Text(n == 3 ? null : "v" + n); // one instance starts empty
                        if (lockFirstInstanceText && n == 1) { txt.Editable = false; txt.LockReason = "Owned by someone."; }
                        r.Cells[InstText] = txt;
                        r.Cells[TypeText] = TestData.Text("T-" + t.Substring(5, 4));
                        r.Cells[TypeLength] = TestData.Dbl(units, 1000.0 / 304.8, FakeUnits.Millimeters);
                        r.Cells[InstYesNo] = new CellValue { Raw = RawValue.FromInteger(0), DisplayText = "No", Editable = true };
                        r.Cells[InstInt] = new CellValue { Raw = RawValue.FromInteger(n), ExportNumber = n, Editable = true };
                        r.Cells[TypeText2] = TestData.Text("x");
                        r.Cells[InstRef] = new CellValue { Raw = RawValue.FromElementId(501), DisplayText = "A [501]", Editable = true };
                        s.Rows.Add(r);
                    }
                }
                Pkg.Sheets.Add(s);
                Model = FakeModel.From(Pkg);
                foreach (var t in Types) Model.TypeInstanceCounts[t] = 25; // more instances than exported
            }

            public XWorkbook Export() => TestData.RoundTrip(WorkbookExporter.Build(Pkg));
            public XSheet Sheet(XWorkbook wb) => wb.Find(Cat);
            public void Set(XWorkbook wb, string uid, ParamKey key, XCell cell) { var s = Sheet(wb); s.Set(TestData.Row(s, uid), TestData.Col(s, key), cell); }
            public ChangeSet Import(XWorkbook wb) => TestData.Import(TestData.RoundTrip(wb), Model);
            public List<string> InstancesOf(int typeIndex) => Instances.Where(i => TypeOf[i] == Types[typeIndex]).ToList();
        }

        [Test]
        public static void InstanceEdit_ChangesOnlyThatInstance()
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var f = new Fixture(seed);
                var wb = f.Export();
                f.Set(wb, f.Instances[1], f.InstText, XCell.Str("edited"));
                var set = f.Import(wb);
                var e = set.Entries.Single();
                Assert.Equal(ChangeStatus.Change, e.Status);
                Assert.Equal(f.Instances[1], e.TargetUniqueId);
                Assert.False(e.TargetIsType);
                Assert.Equal(1, ChangeSetBuilder.ToWriteOps(set).Count);
            }
        }

        [Test]
        public static void SingleTypeCellEdited_OneWriteOnTheType()
        {
            var f = new Fixture(11);
            var wb = f.Export();
            var rows = f.InstancesOf(0);
            f.Set(wb, rows[1], f.TypeText, XCell.Str("NEW"));   // rows 0 and 2 keep the exported value
            var set = f.Import(wb);
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.Change, e.Status);
            Assert.True(e.TargetIsType);
            Assert.Equal(f.Types[0], e.TargetUniqueId);
            Assert.Equal(25, e.AffectedInstances, "all instances in the model, not only the exported ones");
            var ops = ChangeSetBuilder.ToWriteOps(set);
            Assert.Equal(1, ops.Count);
            Assert.Equal(f.Types[0], ops[0].TargetUniqueId);
        }

        [Test]
        public static void SameTypeEditedEqually_OneConsolidatedWrite_IncludingEquivalentUnits()
        {
            var f = new Fixture(12);
            var wb = f.Export();
            var rows = f.InstancesOf(1);
            f.Set(wb, rows[0], f.TypeLength, XCell.Num(1200));
            f.Set(wb, rows[1], f.TypeLength, XCell.Str("1200,0"));     // same magnitude, different representation
            f.Set(wb, rows[2], f.TypeLength, XCell.Num(1200.0000000000002));
            var set = f.Import(wb);
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.Change, e.Status);
            Assert.Equal(3, e.Sources.Count);
            Assert.Near(1200 / 304.8, e.Proposed.Double, 1e-12);
        }

        [Test]
        public static void SameTypeEditedDifferently_TypeConflict_NoWrite()
        {
            var f = new Fixture(13);
            var wb = f.Export();
            var rows = f.InstancesOf(0);
            f.Set(wb, rows[0], f.TypeText, XCell.Str("B"));
            f.Set(wb, rows[1], f.TypeText, XCell.Str("C"));
            var set = f.Import(wb);
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.ProposalConflict, e.Status);
            Assert.Equal(ChangeSetBuilder.MultipleValuesText, e.ProposedText, "no value presented as the resolved proposal");
            Assert.Contains("'B' at", e.Reason);
            Assert.Contains("'C' at", e.Reason);
            e.Include = true; // e.g. "Include all changes" or a UI bug
            Assert.False(e.WillWrite);
            Assert.Equal(0, ChangeSetBuilder.ToWriteOps(set).Count);
        }

        [Test]
        public static void ValidAndInvalidEditForSameType_GroupError_NoPartialWrite()
        {
            var f = new Fixture(14);
            var wb = f.Export();
            var rows = f.InstancesOf(0);
            f.Set(wb, rows[0], f.TypeLength, XCell.Num(900));
            f.Set(wb, rows[2], f.TypeLength, XCell.Str("nine hundred"));
            var set = f.Import(wb);
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.Blocked, e.Status);
            Assert.Equal(BlockCode.GroupError, e.Code);
            Assert.Equal(0, ChangeSetBuilder.ToWriteOps(set).Count);
        }

        [Test]
        public static void DifferentTypesAndDifferentParameters_AreIndependent()
        {
            var f = new Fixture(15);
            var wb = f.Export();
            f.Set(wb, f.InstancesOf(0)[0], f.TypeText, XCell.Str("ONE"));
            f.Set(wb, f.InstancesOf(1)[0], f.TypeText, XCell.Str("TWO"));       // other type: no conflict
            f.Set(wb, f.InstancesOf(0)[1], f.TypeText2, XCell.Str("other param")); // same type, other parameter
            var set = f.Import(wb);
            Assert.Equal(3, set.Entries.Count);
            Assert.True(set.Entries.All(x => x.Status == ChangeStatus.Change), string.Join(";", set.Entries.Select(x => x.Status + " " + x.Reason)));
            Assert.Equal(3, ChangeSetBuilder.ToWriteOps(set).Count);
        }

        [Test]
        public static void ApiWritable_ReadOnly_Empty_AndPartialColumns()
        {
            var f = new Fixture(16, lockFirstInstanceText: true);
            // Parameter writable through the API even if the UI flag says otherwise: the model reports it editable.
            f.Model.Set(f.Instances[4], f.InstInt, RawValue.FromInteger(5), editable: true);
            // A parameter Revit reports as read-only (formula).
            f.Model.Set(f.Instances[5], f.InstInt, RawValue.FromInteger(6), editable: false, reason: "Revit reports this parameter as read-only (formula).");
            var wb = f.Export();
            f.Set(wb, f.Instances[0], f.InstText, XCell.Str("try locked"));   // locked cell
            f.Set(wb, f.Instances[1], f.InstText, XCell.Str("ok"));           // same column, editable cell
            f.Set(wb, f.Instances[2], f.InstText, XCell.Str("was empty"));    // empty parameter gets a value
            f.Set(wb, f.Instances[4], f.InstInt, XCell.Num(50));
            f.Set(wb, f.Instances[5], f.InstInt, XCell.Num(60));
            var set = f.Import(wb);
            var locked = set.Entries.Single(e => e.TargetUniqueId == f.Instances[0]);
            Assert.Equal(ChangeStatus.Blocked, locked.Status);
            Assert.True(locked.State != null, "blocked after reading the live parameter (current value available)");
            Assert.Equal(ChangeStatus.Change, set.Entries.Single(e => e.TargetUniqueId == f.Instances[1]).Status);
            var empty = set.Entries.Single(e => e.TargetUniqueId == f.Instances[2]);
            Assert.Equal(ChangeStatus.Change, empty.Status);
            Assert.True(empty.Original.IsEmpty);
            Assert.Equal(ChangeStatus.Change, set.Entries.Single(e => e.TargetUniqueId == f.Instances[4]).Status);
            var ro = set.Entries.Single(e => e.TargetUniqueId == f.Instances[5]);
            Assert.Equal(BlockCode.ReadOnlyParameter, ro.Code);
            Assert.Contains("formula", ro.Reason);
        }

        [Test]
        public static void AllStorageKinds_ConvertCorrectly()
        {
            var f = new Fixture(17);
            var wb = f.Export();
            string i = f.Instances[0];
            f.Set(wb, i, f.InstYesNo, XCell.Str("Yes"));
            f.Set(wb, i, f.InstInt, XCell.Num(42));
            f.Set(wb, i, f.InstRef, XCell.Str("B [502]"));
            f.Set(wb, f.InstancesOf(0)[0], f.TypeLength, XCell.Num(2500));
            var set = f.Import(wb);
            Assert.Equal(1L, set.Entries.Single(e => e.Key == f.InstYesNo).Proposed.Integer);
            Assert.Equal(42L, set.Entries.Single(e => e.Key == f.InstInt).Proposed.Integer);
            var r = set.Entries.Single(e => e.Key == f.InstRef);
            Assert.Equal(RawKind.ElementId, r.Proposed.Kind);
            Assert.Equal(502L, r.Proposed.Integer);
            Assert.Near(2500 / 304.8, set.Entries.Single(e => e.Key == f.TypeLength).Proposed.Double, 1e-12);
            Assert.True(set.Entries.All(e => e.Status == ChangeStatus.Change));
        }

        [Test]
        public static void ReferenceByNameOnly_IsRejected_ListIsExported()
        {
            var f = new Fixture(18);
            var built = WorkbookExporter.Build(f.Pkg);
            var lists = built.Find(WorkbookSchema.ListsSheet);
            Assert.True(lists != null && lists.State == XSheetState.VeryHidden, "reference candidates in a hidden list sheet");
            var data = built.Find(f.Cat);
            Assert.True(data.Validations.Any(v => v.Formula != null && v.Formula.StartsWith(WorkbookSchema.ListsSheet + "!")), "drop-down bound to the list");
            var wb = TestData.RoundTrip(built);
            f.Set(wb, f.Instances[0], f.InstRef, XCell.Str("B"));
            var e = f.Import(wb).Entries.Single();
            Assert.Equal(BlockCode.InvalidValue, e.Code);
            Assert.Equal("B", e.ProposedText, "the value read from Excel is shown, not '(empty)'");
        }

        [Test]
        public static void UnchangedReorderedRenamed_ZeroWrites()
        {
            var f = new Fixture(19);
            var wb = f.Export();
            var s = f.Sheet(wb);
            for (int c = 2; c <= s.MaxColumn; c++) s.Set(2, c, XCell.Str("Renamed " + c));
            var set = f.Import(wb);
            Assert.Equal(0, set.Entries.Count);
            Assert.True(set.UnchangedCells > 0);
        }

        [Test]
        public static void CellColors_FollowRealCellState()
        {
            var f = new Fixture(20, lockFirstInstanceText: true);
            var na = f.Pkg.Sheets[0].Rows[5];
            na.Cells[f.TypeText2] = CellValue.NotApplicable();
            var tro = f.Pkg.Sheets[0].Rows[4];
            tro.Cells[f.TypeText].Editable = false;
            var wb = WorkbookExporter.Build(f.Pkg);   // styles are inspected before writing
            var s = wb.Find(f.Cat);
            XStyle St(int row, ParamKey k) => s.Get(TestData.Row(s, f.Pkg.Sheets[0].Rows[row].RowKey), TestData.Col(s, k)).Style;
            Assert.Equal(WorkbookSchema.GreenInstance, St(1, f.InstText).FillRgb, "instance editable");
            Assert.Equal(WorkbookSchema.RedReadOnly, St(0, f.InstText).FillRgb, "locked instance cell");
            Assert.Equal(WorkbookSchema.YellowType, St(0, f.TypeText).FillRgb, "type editable");
            Assert.Equal(WorkbookSchema.RedReadOnly, St(4, f.TypeText).FillRgb, "type read-only: red...");
            Assert.Equal(WorkbookSchema.TypeBorder, St(4, f.TypeText).LeftBorderRgb, "...with the type border");
            Assert.Equal(WorkbookSchema.GrayNotApplicable, St(5, f.TypeText2).FillRgb, "N/A");
            Assert.Equal(WorkbookSchema.GreenInstance, St(2, f.InstText).FillRgb, "empty text cell still coloured");
            Assert.Equal(WorkbookSchema.YellowType, St(0, f.TypeLength).FillRgb, "numeric type cell coloured");
            Assert.Equal(XCellKind.Blank, s.Get(TestData.Row(s, f.Pkg.Sheets[0].Rows[2].RowKey), TestData.Col(s, f.InstText)).Kind);
        }

        [Test]
        public static void LegacySchema1ReadOnlyColumns_AreMigratedNotBlocked()
        {
            var f = new Fixture(21);
            // Reproduce what version 0.1.0 wrote when it wrongly classified columns as read-only.
            foreach (var c in f.Pkg.Sheets[0].Columns.Where(c => !c.IsContext && c.Kind != ValueKind.ElementRef)) c.ColumnReadOnly = true;
            var built = WorkbookExporter.Build(f.Pkg);
            var meta = built.Find(WorkbookSchema.MetaSheet);
            meta.Set(1, 2, XCell.Str("1")); // schema 1
            var wb = TestData.RoundTrip(built);
            f.Set(wb, f.InstancesOf(0)[0], f.TypeText, XCell.Str("from v1 file"));
            f.Set(wb, f.Instances[3], f.InstText, XCell.Str("edited text"));
            f.Set(wb, f.InstancesOf(1)[0], f.TypeLength, XCell.Num(1500));
            var set = f.Import(wb);
            Assert.Equal(3, set.Entries.Count, string.Join("\n", set.Entries.Select(e => e.Status + " " + e.Reason)));
            Assert.True(set.Entries.All(e => e.Status == ChangeStatus.Change), string.Join("\n", set.Entries.Select(e => e.Status + " " + e.Reason)));
            Assert.True(set.Entries.All(e => e.Sources.All(x => x.OriginalMigrated)));
            Assert.True(set.Issues.Any(i => i.Message.Contains("earlier CausaFix version")));
        }

        [Test]
        public static void Diagnostics_DescribeEveryBlockedCell()
        {
            var f = new Fixture(22);
            f.Model.Deleted.Add(f.Instances[0]);
            var wb = f.Export();
            f.Set(wb, f.Instances[0], f.InstText, XCell.Str("x"));
            var e = f.Import(wb).Entries.Single();
            Assert.Equal(BlockCode.MissingElement, e.Code);
            Assert.Contains("element=NOT found", e.Diagnostic);
            Assert.Contains("read 'x' (Text)", e.Diagnostic);
            Assert.Contains(f.InstText.ToString(), e.Diagnostic);
        }
    }
}
