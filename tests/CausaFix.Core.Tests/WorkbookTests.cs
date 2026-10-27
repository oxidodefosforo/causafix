using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Profiles;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;
using CausaFix.Core.Workbook;
using CausaFix.Core.Xlsx;

namespace CausaFix.Core.Tests
{
    public static class WorkbookTests
    {
        private static (ExportPackage pkg, XWorkbook wb, FakeModel model) Fresh()
        {
            var pkg = TestData.LightingPackage();
            var wb = TestData.RoundTrip(WorkbookExporter.Build(pkg));
            return (pkg, wb, FakeModel.From(pkg));
        }

        private static void AssertNoChanges(ChangeSet set)
        {
            string detail = string.Join("\n", set.Entries.Select(e => e.Status + " " + e.Column.Header + " " + e.Reason + " @ " + e.SourceSummary));
            Assert.Equal(0, set.Entries.Count, "expected zero changes:\n" + detail);
            Assert.Equal(0, set.Issues.Count(i => i.Severity != IssueSeverity.Info), "expected no warnings: " + string.Join("; ", set.Issues));
        }

        [Test]
        public static void ExportImport_WithoutEdits_IsZeroChanges()
        {
            var (_, wb, model) = Fresh();
            var set = TestData.Import(wb, model);
            AssertNoChanges(set);
            Assert.True(set.UnchangedCells > 0, "cells were compared");
        }

        [Test]
        public static void ZeroChanges_AfterExcelLikeResave_With15Digits()
        {
            var (_, wb, model) = Fresh();
            // Simulate Excel keeping only 15 significant digits on every number.
            foreach (var sh in wb.Sheets)
                foreach (var row in sh.Rows)
                    foreach (var kv in row.Value)
                        if (kv.Value.Kind == XCellKind.Number)
                            kv.Value.Number = double.Parse(kv.Value.Number.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            AssertNoChanges(set);
        }

        [Test]
        public static void Export_StructureAndStyles()
        {
            var (pkg, wb, _) = Fresh();
            Assert.Equal(WorkbookSchema.InstructionsSheet, wb.Sheets[0].Name, "instructions first");
            Assert.Equal(XSheetState.VeryHidden, wb.Find(WorkbookSchema.MetaSheet).State);
            var s = TestData.DataSheet(wb);
            Assert.Equal(WorkbookSchema.SheetMarker + "cat:-2001120:I", s.GetText(1, 1));
            Assert.True(s.HiddenRows.Contains(1), "key row hidden");
            Assert.Contains("Width [mm]", s.GetText(2, TestData.Col(s, TestData.Width)));
            Assert.Contains("Type Mark (T)", s.GetText(2, TestData.Col(s, TestData.TypeMark)));
            Assert.False(s.GetText(2, TestData.Col(s, TestData.ApparentLoad)).Contains("(RO)"), "parameter columns are never read-only as a whole");
            int r4 = TestData.Row(s, TestData.Uid(4));
            Assert.Equal("N/A", s.GetText(r4, TestData.Col(s, TestData.QaStatusB)), "N/A shown explicitly");
            int r3 = TestData.Row(s, TestData.Uid(3));
            Assert.Equal(XCellKind.Text, s.Get(r3, TestData.Col(s, TestData.Comments)).Kind);
            Assert.False(s.Get(r3, TestData.Col(s, TestData.Comments)).IsFormula, "'=' text is not a formula");
            Assert.Near(1200.5, s.Get(r3, TestData.Col(s, TestData.Width)).Number, 1e-9, "exported in mm");
            Assert.Near(240, s.Get(r3, TestData.Col(s, TestData.Voltage)).Number, 1e-9, "exported in V, not internal units");
            // 64-bit id kept exactly as text in the shadow sheet.
            var shadow = wb.Sheets.First(x => x.Name.StartsWith(WorkbookSchema.ShadowPrefix));
            bool found = false;
            for (int r = 2; r <= shadow.MaxRow; r++) if (shadow.GetText(r, WorkbookSchema.ShadowElementIdCol) == TestData.BigId.ToString(CultureInfo.InvariantCulture)) found = true;
            Assert.True(found, "64-bit ElementId preserved as text");
        }

        [Test]
        public static void SortedRowsAndRenamedHeaders_KeepAssociation()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            // Reverse data rows (as an Excel sort would) and rename every visible header.
            var rows = new List<Dictionary<int, XCell>>();
            for (int r = 3; r <= s.MaxRow; r++)
            {
                var d = new Dictionary<int, XCell>();
                for (int c = 1; c <= s.MaxColumn; c++) { var cell = s.Get(r, c); if (cell != null) d[c] = cell; }
                rows.Add(d);
            }
            rows.Reverse();
            var sorted = new XSheet(s.Name);
            for (int c = 1; c <= s.MaxColumn; c++)
            {
                sorted.Set(1, c, s.Get(1, c));
                sorted.Set(2, c, XCell.Str("Renamed " + c));
            }
            for (int i = 0; i < rows.Count; i++) foreach (var kv in rows[i]) sorted.Set(3 + i, kv.Key, kv.Value);
            wb.Sheets[wb.Sheets.IndexOf(s)] = sorted;

            int row2 = TestData.Row(sorted, TestData.Uid(2));
            sorted.Set(row2, TestData.Col(sorted, TestData.Mark), XCell.Str("M-2-new"));

            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.Change, e.Status);
            Assert.Equal(TestData.Uid(2), e.TargetUniqueId);
            Assert.Equal(TestData.Mark, e.Key);
            Assert.Equal("M-2-new", e.Proposed.Text);
        }

        [Test]
        public static void MovedColumns_KeepAssociation()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int cm = TestData.Col(s, TestData.Mark), cc = TestData.Col(s, TestData.Comments);
            // swap the two columns entirely (keys move with them)
            for (int r = 1; r <= s.MaxRow; r++)
            {
                var a = s.Get(r, cm); var b = s.Get(r, cc);
                s.Set(r, cm, b ?? XCell.Empty(null)); s.Set(r, cc, a ?? XCell.Empty(null));
            }
            int r1 = TestData.Row(s, TestData.Uid(1));
            s.Set(r1, TestData.Col(s, TestData.Comments), XCell.Str("moved column edit"));
            var e = TestData.Import(TestData.RoundTrip(wb), model).Entries.Single();
            Assert.Equal(TestData.Comments, e.Key);
            Assert.Equal("moved column edit", e.Proposed.Text);
        }

        [Test]
        public static void HomonymSharedParameters_AreNotConfused()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1));
            s.Set(r1, TestData.Col(s, TestData.QaStatusB), XCell.Str("B-approved"));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var e = set.Entries.Single();
            Assert.Equal(TestData.QaStatusB, e.Key);
            Assert.Equal("B-approved", e.Proposed.Text);
        }

        [Test]
        public static void UnitsAndDecimals_NoMagnitudeErrors()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1)), r2 = TestData.Row(s, TestData.Uid(2)), r3 = TestData.Row(s, TestData.Uid(3));
            s.Set(r1, TestData.Col(s, TestData.Width), XCell.Num(650));            // numeric mm
            s.Set(r2, TestData.Col(s, TestData.Width), XCell.Str("612,5"));        // text with decimal comma
            s.Set(r3, TestData.Col(s, TestData.Width), XCell.Str("1,200"));        // ambiguous
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var e1 = set.Entries.Single(x => x.TargetUniqueId == TestData.Uid(1));
            Assert.Near(650 / 304.8, e1.Proposed.Double, 1e-12, "650 mm -> feet");
            var e2 = set.Entries.Single(x => x.TargetUniqueId == TestData.Uid(2));
            Assert.Near(612.5 / 304.8, e2.Proposed.Double, 1e-12);
            var e3 = set.Entries.Single(x => x.Sources[0].RowKey == TestData.Uid(3));
            Assert.Equal(ChangeStatus.Blocked, e3.Status);
            Assert.Contains("Ambiguous", e3.Reason);
            Assert.Equal("650 mm", ValueFormatter.Format(e1.Proposed, e1.Column, new FakeUnits(), CultureInfo.InvariantCulture));
        }

        [Test]
        public static void ElectricalUnits_VoltsStayVolts()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r3 = TestData.Row(s, TestData.Uid(3)), r4 = TestData.Row(s, TestData.Uid(4));
            s.Set(r3, TestData.Col(s, TestData.Voltage), XCell.Num(400));
            s.Set(r4, TestData.Col(s, TestData.Voltage), XCell.Num(400));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.Change, e.Status, "same value from two instances of one type = one write");
            Assert.Equal(TestData.T2, e.TargetUniqueId);
            Assert.Equal(2, e.Sources.Count);
            Assert.Near(400 * FakeUnits.VoltFactor, e.Proposed.Double, 1e-9);
        }

        [Test]
        public static void TypeParameter_IncompatibleProposals_AreAConflict()
        {
            var (_, wb, model) = Fresh();
            model.TypeInstanceCounts[TestData.T1] = 37;
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1)), r2 = TestData.Row(s, TestData.Uid(2));
            s.Set(r1, TestData.Col(s, TestData.TypeMark), XCell.Str("LF-A1"));
            s.Set(r2, TestData.Col(s, TestData.TypeMark), XCell.Str("LF-A2"));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var e = set.Entries.Single();
            Assert.Equal(ChangeStatus.ProposalConflict, e.Status);
            Assert.False(e.WillWrite, "never written arbitrarily");
            Assert.Equal(2, e.Alternatives.Count);
            Assert.Equal(37, e.AffectedInstances, "instances affected in the whole document");
            ChangeSetBuilder.ChooseAlternative(e, 1);
            Assert.Equal(ChangeStatus.Change, e.Status);
            Assert.Equal("LF-A2", e.Proposed.Text);
            Assert.True(e.WillWrite);
            Assert.Equal(1, ChangeSetBuilder.ToWriteOps(set).Count);
        }

        [Test]
        public static void TypeParameter_UneditedRowsDoNotCountAsProposals()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1));
            s.Set(r1, TestData.Col(s, TestData.TypeMark), XCell.Str("LF-A1"));   // row 2 keeps LF-A
            var e = TestData.Import(TestData.RoundTrip(wb), model).Entries.Single();
            Assert.Equal(ChangeStatus.Change, e.Status);
            Assert.Equal(TestData.T1, e.TargetUniqueId);
        }

        [Test]
        public static void TypeParameter_ConflictAcrossSheets()
        {
            var pkg = TestData.LightingPackage();
            // A second sheet (e.g. Elements tab) exporting the same type parameter.
            var s2 = new DataSheet { SheetKey = "el:types", Title = "Fixture types", Source = SheetSource.Elements, RowMode = RowMode.Type };
            s2.Columns.Add(new ColumnDef { Key = TestData.TypeMark, Header = "Type Mark", Kind = ValueKind.Text });
            var tr = new RowData { RowKey = TestData.T1, UniqueId = TestData.T1, ElementId = 9001, Label = "DL-600" };
            tr.Cells[TestData.TypeMark] = TestData.Text("LF-A");
            s2.Rows.Add(tr);
            pkg.Sheets.Add(s2);
            var wb = TestData.RoundTrip(WorkbookExporter.Build(pkg));
            var model = FakeModel.From(pkg);
            var a = TestData.DataSheet(wb);
            a.Set(TestData.Row(a, TestData.Uid(1)), TestData.Col(a, TestData.TypeMark), XCell.Str("FROM-INSTANCES"));
            var b = wb.Find("Fixture types");
            b.Set(TestData.Row(b, TestData.T1), TestData.Col(b, TestData.TypeMark), XCell.Str("FROM-TYPES"));
            var e = TestData.Import(TestData.RoundTrip(wb), model).Entries.Single();
            Assert.Equal(ChangeStatus.ProposalConflict, e.Status);
            Assert.Contains("Fixture types", e.SourceSummary);
        }

        [Test]
        public static void ConcurrentModelEdit_IsDetected()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1)), r2 = TestData.Row(s, TestData.Uid(2)), r3 = TestData.Row(s, TestData.Uid(3));
            model.Set(TestData.Uid(1), TestData.Mark, RawValue.FromText("M-1-revit"));      // changed in Revit
            model.Set(TestData.Uid(2), TestData.Mark, RawValue.FromText("same"));           // changed in Revit to the proposed value
            model.Set(TestData.Uid(3), TestData.Mark, RawValue.FromText("M-3-revit"));      // changed in Revit, Excel untouched
            s.Set(r1, TestData.Col(s, TestData.Mark), XCell.Str("M-1-excel"));
            s.Set(r2, TestData.Col(s, TestData.Mark), XCell.Str("same"));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var c1 = set.Entries.Single(e => e.TargetUniqueId == TestData.Uid(1));
            Assert.Equal(ChangeStatus.Conflict, c1.Status);
            Assert.False(c1.Include, "not overwritten by default");
            Assert.Equal("M-1-revit", c1.Current.Text);
            var c2 = set.Entries.Single(e => e.TargetUniqueId == TestData.Uid(2));
            Assert.Equal(ChangeStatus.AlreadyMatches, c2.Status);
            Assert.False(set.Entries.Any(e => e.TargetUniqueId == TestData.Uid(3)), "untouched in Excel keeps model state");
            Assert.Equal(0, ChangeSetBuilder.ToWriteOps(set).Count);
            c1.Include = true; // explicit user decision
            var op = ChangeSetBuilder.ToWriteOps(set).Single();
            Assert.Equal("M-1-revit", op.ExpectedCurrent.Text, "apply re-checks the value seen at review");
        }

        [Test]
        public static void EmptyCell_IsIgnored_ClearTokenClears()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1)), r2 = TestData.Row(s, TestData.Uid(2));
            s.Set(r1, TestData.Col(s, TestData.Comments), XCell.Empty(null));
            s.Set(r1, TestData.Col(s, TestData.Width), XCell.Empty(null));
            s.Set(r2, TestData.Col(s, TestData.Comments), XCell.Str("<<CLEAR>>"));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var e = set.Entries.Single();
            Assert.True(e.IsClear);
            Assert.Equal(TestData.Uid(2), e.TargetUniqueId);
            Assert.Equal(2, set.EmptyCellsIgnored, "empty cells reported, never turned into zero/empty");
        }

        [Test]
        public static void FormulaWithoutResult_DuplicateRow_FormulaWithResult()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1)), r2 = TestData.Row(s, TestData.Uid(2));
            s.Set(r1, TestData.Col(s, TestData.Width), new XCell { Formula = "B3*2", HasCachedValue = false });
            s.Set(r2, TestData.Col(s, TestData.Width), new XCell { Formula = "300+400", HasCachedValue = true, Kind = XCellKind.Number, Number = 700 });
            // duplicate row 2 at the bottom with the same edit
            int dup = s.MaxRow + 1;
            for (int c = 1; c <= s.MaxColumn; c++) { var cell = s.Get(r2, c); if (cell != null) s.Set(dup, c, cell); }

            // Our writer refuses formulas, so feed the importer directly (as if Excel saved it).
            var imp = WorkbookImporter.Read(wb, new FakeUnits(), new ImportOptions());
            var set = new ChangeSet();
            set.Issues.AddRange(imp.Issues);
            ChangeSetBuilder.Build(imp.Cells, model, set);

            var blocked = set.Entries.Single(e => e.Status == ChangeStatus.Blocked);
            Assert.Contains("no saved result", blocked.Reason);
            var change = set.Entries.Single(e => e.Status == ChangeStatus.Change);
            Assert.Near(700 / 304.8, change.Proposed.Double, 1e-12);
            Assert.Equal(2, change.Sources.Count, "duplicate rows consolidated into one write");
            Assert.True(set.Issues.Any(i => i.Message.Contains("Duplicate")), "duplicate reported");
            Assert.True(set.Issues.Any(i => i.Message.Contains("formula =300+400")), "formula result shown");
        }

        [Test]
        public static void LinkedAndReadOnly_AreNeverWritten()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int linked = TestData.Row(s, DocKeys.LinkedRowKey("link-instance-uid", TestData.Uid(5)));
            int r1 = TestData.Row(s, TestData.Uid(1));
            s.Set(linked, TestData.Col(s, TestData.Mark), XCell.Str("edit linked"));
            s.Set(r1, TestData.Col(s, TestData.ApparentLoad), XCell.Num(1));
            s.Set(r1, TestData.Col(s, TestData.Level), XCell.Str("Level 2"));
            // A per-element lock (e.g. owned by another user) on an editable column
            model.Set(TestData.Uid(1), TestData.Comments, RawValue.FromText("Note, with comma"), editable: false, reason: "Owned by another user (j.perez).");
            s.Set(r1, TestData.Col(s, TestData.Comments), XCell.Str("try"));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            Assert.Equal(4, set.Entries.Count);
            Assert.True(set.Entries.All(e => e.Status == ChangeStatus.Blocked && !e.WillWrite), "all blocked");
            Assert.True(set.Entries.Any(e => e.Reason.Contains("Linked")));
            Assert.True(set.Entries.Any(e => e.Reason.Contains("Owned by another user")));
            Assert.Equal(0, ChangeSetBuilder.ToWriteOps(set).Count);
        }

        [Test]
        public static void NotApplicableCell_CannotReceiveValue()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r4 = TestData.Row(s, TestData.Uid(4));
            s.Set(r4, TestData.Col(s, TestData.QaStatusB), XCell.Str("value"));
            var e = TestData.Import(TestData.RoundTrip(wb), model).Entries.Single();
            Assert.Equal(ChangeStatus.Blocked, e.Status);
            Assert.Contains("did not exist", e.Reason);
        }

        [Test]
        public static void DeletedElementAndYesNo()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            int r1 = TestData.Row(s, TestData.Uid(1)), r2 = TestData.Row(s, TestData.Uid(2));
            s.Set(r1, TestData.Col(s, TestData.Mark), XCell.Str("x"));
            s.Set(r2, TestData.Col(s, TestData.Emergency), XCell.Str("Yes"));
            model.Deleted.Add(TestData.Uid(1));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            var missing = set.Entries.Single(e => e.TargetUniqueId == TestData.Uid(1));
            Assert.Equal(BlockCode.MissingElement, missing.Code);
            Assert.Contains("not found", missing.Reason);
            var yn = set.Entries.Single(e => e.TargetUniqueId == TestData.Uid(2));
            Assert.Equal(ChangeStatus.Change, yn.Status);
            Assert.Equal(1L, yn.Proposed.Integer);
        }

        [Test]
        public static void StructuralDamage_IsReportedAndContained()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            // New row without key, a row with an altered key, a new column, a deleted row and an edited ElementId.
            int newRow = s.MaxRow + 1;
            s.Set(newRow, 5, XCell.Str("new element?"));
            int r1 = TestData.Row(s, TestData.Uid(1));
            s.Set(r1, 1, XCell.Str("altered-key"));
            s.Set(1, s.MaxColumn + 1, XCell.Empty(null));
            int newCol = s.MaxColumn;
            s.Set(2, newCol, XCell.Str("My new param"));
            s.Set(3, newCol, XCell.Str("x"));
            int r2 = TestData.Row(s, TestData.Uid(2));
            s.Set(r2, TestData.Col(s, ParamKey.Pseudo(PseudoColumns.ElementId)), XCell.Str("999"));
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            Assert.Equal(0, set.Entries.Count);
            string all = string.Join("\n", set.Issues);
            Assert.Contains("without a key", all);
            Assert.Contains("Row key not found", all);
            Assert.Contains("no CausaFixIt key", all);
            Assert.Contains("visible ElementId was edited", all);
            Assert.Contains("no longer in the sheet", all);
        }

        [Test]
        public static void MissingKeyRowOrMeta_BlocksAffectedPartOnly()
        {
            var (_, wb, model) = Fresh();
            var s = TestData.DataSheet(wb);
            s.Set(1, 1, XCell.Str(""));  // sheet key destroyed
            var set = TestData.Import(TestData.RoundTrip(wb), model);
            Assert.Equal(0, set.Entries.Count);
            Assert.True(set.Issues.Any(i => i.Severity == IssueSeverity.Error && i.Message.Contains("technical key")));

            var plain = new XWorkbook();
            var p = new XSheet("Sheet1"); p.Set(1, 1, XCell.Str("hello")); plain.Sheets.Add(p);
            var imp = WorkbookImporter.Read(plain, new FakeUnits(), new ImportOptions());
            Assert.True(imp.Fatal && !imp.IsCausaFixWorkbook);
        }

        [Test]
        public static void DocumentIdentity_Rules()
        {
            var exported = TestData.Doc();
            Assert.Equal(IdentityMatch.Verified, DocumentIdentityMatcher.Compare(exported, TestData.Doc()).Match);

            var other = TestData.Doc(); other.CreationGuid = "ffffffff-0000-0000-0000-000000000000";
            Assert.Equal(IdentityMatch.Mismatch, DocumentIdentityMatcher.Compare(exported, other).Match);

            var otherProject = TestData.Doc(); otherProject.ProjectInfoUniqueId = "pi-other";
            Assert.Equal(IdentityMatch.Mismatch, DocumentIdentityMatcher.Compare(exported, otherProject).Match);

            var saveAs = TestData.Doc(); saveAs.CentralPath = @"\\server\HRDT\copy.rvt"; saveAs.PathName = @"C:\tmp\copy.rvt";
            var r = DocumentIdentityMatcher.Compare(exported, saveAs);
            Assert.Equal(IdentityMatch.SameLineageDifferentLocation, r.Match);
            Assert.True(r.RequiresConfirmation);

            var detached = TestData.Doc(); detached.IsDetached = true;
            Assert.Equal(IdentityMatch.SameLineageDifferentLocation, DocumentIdentityMatcher.Compare(exported, detached).Match);

            var noGuidA = TestData.Doc(); noGuidA.CreationGuid = "";
            var noGuidB = TestData.Doc(); noGuidB.CreationGuid = "";
            Assert.Equal(IdentityMatch.Unverifiable, DocumentIdentityMatcher.Compare(noGuidA, noGuidB).Match, "never silently verified without lineage proof");

            var cloudA = new DocumentIdentity { IsCloud = true, CloudModelGuid = "m1", CloudProjectGuid = "p1" };
            var cloudB = new DocumentIdentity { IsCloud = true, CloudModelGuid = "m2", CloudProjectGuid = "p1" };
            Assert.Equal(IdentityMatch.Verified, DocumentIdentityMatcher.Compare(cloudA, cloudA).Match);
            Assert.Equal(IdentityMatch.Mismatch, DocumentIdentityMatcher.Compare(cloudA, cloudB).Match);

            // identity survives the workbook
            var wb = TestData.RoundTrip(WorkbookExporter.Build(TestData.LightingPackage()));
            var imp = WorkbookImporter.Read(wb, new FakeUnits(), new ImportOptions());
            Assert.Equal(IdentityMatch.Verified, DocumentIdentityMatcher.Compare(imp.Document, TestData.Doc()).Match);
        }

        [Test]
        public static void PresentationSheet_IsNeverImported()
        {
            var pkg = TestData.LightingPackage();
            var pres = new DataSheet { SheetKey = "schp:abc", Title = "Lighting Schedule (presentation)", Source = SheetSource.SchedulePresentation, ReadOnly = true, PresentationHeaderRows = 2 };
            pres.PresentationGrid.Add(new[] { "LIGHTING SCHEDULE" });
            pres.PresentationGrid.Add(new[] { "Type", "Count", "Load" });
            pres.PresentationGrid.Add(new[] { "DL-600", "12", "2400 VA" });
            pres.PresentationGrid.Add(new[] { "Grand total: 12", "", "" });
            pkg.Sheets.Add(pres);
            var wb = TestData.RoundTrip(WorkbookExporter.Build(pkg));
            var ps = wb.Sheets.First(x => x.Name.StartsWith("Lighting Schedule"));
            ps.Set(4, 2, XCell.Str("999"));
            var set = TestData.Import(TestData.RoundTrip(wb), FakeModel.From(pkg));
            Assert.Equal(0, set.Entries.Count);
            Assert.True(set.Issues.Any(i => i.Message.Contains("Presentation export")));
        }

        [Test]
        public static void ApplyReport_NeverClaimsRolledBackChanges()
        {
            var result = new ApplyResult { Mode = ApplyMode.Atomic };
            result.Ops.Add(new OpResult { EntryId = 1, Outcome = OpOutcome.Applied, Parameter = "Mark", OldValue = "a", NewValue = "=b" });
            result.Ops.Add(new OpResult { EntryId = 2, Outcome = OpOutcome.Failed, Parameter = "Mark", Message = "Element is in a group." });
            result.MarkRolledBack("Transaction rolled back because a change failed.");
            Assert.Equal(0, result.Applied);
            Assert.Equal(1, result.RolledBack);
            string csv = ApplyReportWriter.Build("HRDT", null, result, new FakeUnits());
            Assert.Contains("Rolled back", csv);
            Assert.Contains("'=b", csv, "formula injection neutralised in CSV");
        }

        [Test]
        public static void Profiles_CrudAndMissingReferences()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cf-prof-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new ProfileStore(dir);
                var p = new ExcelLinkProfile { Name = "QA / Lighting", Tab = "ModelCategories" };
                p.Categories.Add(new ProfileItem("-2001120", "Lighting Fixtures"));
                p.Parameters.Add(new ProfileItem(TestData.QaStatusA.ToString(), "QA_Status"));
                p.Parameters.Add(new ProfileItem(TestData.Mark.ToString(), "Mark"));
                store.Save(p);
                Assert.Throws<IOException>(() => store.Save(new ExcelLinkProfile { Name = "QA / Lighting" }, overwrite: false));
                store.Duplicate("QA / Lighting", "Copy");
                store.Rename("Copy", "Renamed");
                Assert.Equal(2, store.List().Count);
                var loaded = store.Load("Renamed");
                Assert.Equal(TestData.QaStatusA.ToString(), loaded.Parameters[0].Key, "order and keys preserved");
                string export = Path.Combine(dir, "exported.cfprofile");
                store.Export("Renamed", export);
                store.Delete("Renamed");
                Assert.Equal(1, store.List().Count);
                var imported = store.Import(export, overwrite: false);
                Assert.Equal("Renamed", imported.Name);

                // Another project has a homonym QA_Status with a different GUID: must be reported missing, not matched by name.
                var res = ProfileResolution.Resolve(loaded,
                    new HashSet<string> { "-2001120" },
                    new HashSet<string> { TestData.QaStatusB.ToString(), TestData.Mark.ToString() },
                    new HashSet<string>());
                Assert.Equal(1, res.MissingParameters.Count);
                Assert.Equal("QA_Status", res.MissingParameters[0].Name);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        /// <summary>
        /// Core-only performance: 10 000 rows x 20 parameters, export to xlsx bytes, read back, compare.
        /// Excludes Revit collection and parameter reading (measured separately inside Revit, see docs).
        /// </summary>
        [Test]
        public static void Performance_10k_x_20_CoreOnly()
        {
            var units = new FakeUnits();
            var pkg = new ExportPackage { Document = TestData.Doc(), AddinVersion = "0.1.0", RevitVersion = "2026" };
            var s = new DataSheet { SheetKey = "cat:perf:I", Title = "Perf", Source = SheetSource.ModelCategories };
            s.Columns.Add(PseudoColumns.Column(PseudoColumns.ElementId));
            var keys = new List<ParamKey>();
            for (int i = 0; i < 20; i++)
            {
                var k = ParamKey.Shared(Guid.NewGuid(), i % 4 == 0 ? ParamScope.Type : ParamScope.Instance);
                keys.Add(k);
                s.Columns.Add(i % 2 == 0
                    ? new ColumnDef { Key = k, Header = "P" + i, Kind = ValueKind.Double, UnitTypeId = FakeUnits.Millimeters, UnitLabel = "mm" }
                    : new ColumnDef { Key = k, Header = "P" + i, Kind = ValueKind.Text });
            }
            for (int r = 0; r < 10000; r++)
            {
                var row = new RowData { RowKey = "uid-" + r, UniqueId = "uid-" + r, ElementId = 100000 + r, TypeUniqueId = "type-" + (r % 50) };
                row.Cells[ParamKey.Pseudo(PseudoColumns.ElementId)] = new CellValue { Raw = RawValue.FromText((100000 + r).ToString()), DisplayText = (100000 + r).ToString() };
                for (int i = 0; i < 20; i++)
                    row.Cells[keys[i]] = i % 2 == 0 ? TestData.Dbl(units, (r + i) / 304.8, FakeUnits.Millimeters) : TestData.Text("v" + r + "-" + (keys[i].Scope == ParamScope.Type ? r % 50 : i));
                s.Rows.Add(row);
            }
            pkg.Sheets.Add(s);

            var sw = Stopwatch.StartNew();
            var wb = WorkbookExporter.Build(pkg);
            byte[] bytes;
            using (var ms = new MemoryStream()) { XlsxWriter.Write(wb, ms); bytes = ms.ToArray(); }
            long tExport = sw.ElapsedMilliseconds;
            sw.Restart();
            var back = XlsxReader.Load(new MemoryStream(bytes));
            long tRead = sw.ElapsedMilliseconds;
            sw.Restart();
            var model = FakeModel.From(pkg);
            var imp = WorkbookImporter.Read(back, units, new ImportOptions());
            var set = ChangeSetBuilder.Build(imp.Cells, model);
            long tCompare = sw.ElapsedMilliseconds;
            Console.WriteLine("        10k x 20: build+write " + tExport + " ms, read " + tRead + " ms, import+compare " + tCompare + " ms, file " + (bytes.Length / 1024) + " KB");
            Assert.Equal(0, set.Entries.Count, "zero changes on 200k cells");
        }
    }
}
