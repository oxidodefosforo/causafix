using System;
using System.Collections.Generic;
using System.IO;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Values;
using CausaFix.Core.Workbook;
using CausaFix.Core.Xlsx;

namespace CausaFix.Core.Tests
{
    /// <summary>Unit conversion like Revit's: lengths in feet, electrical values in SI-feet internal units.</summary>
    public sealed class FakeUnits : IUnitConverter
    {
        public const string Millimeters = "autodesk.unit.unit:millimeters-1.0.1";
        public const string Volts = "autodesk.unit.unit:volts-1.0.0";
        public const string VoltAmperes = "autodesk.unit.unit:voltAmperes-1.0.0";
        // 1 V and 1 VA expressed in Revit internal units (kg·ft²/(s³·A) and kg·ft²/s³).
        public const double VoltFactor = 10.763910416709722;

        public double ToInternal(double value, string unit)
        {
            switch (unit)
            {
                case Millimeters: return value / 304.8;
                case Volts:
                case VoltAmperes: return value * VoltFactor;
                default: return value;
            }
        }

        public double FromInternal(double value, string unit)
        {
            switch (unit)
            {
                case Millimeters: return value * 304.8;
                case Volts:
                case VoltAmperes: return value / VoltFactor;
                default: return value;
            }
        }
    }

    /// <summary>In-memory stand-in for the Revit model state (what the Revit adapter reads at import time).</summary>
    public sealed class FakeModel : ICurrentStateSource
    {
        private readonly Dictionary<string, CurrentState> _states = new Dictionary<string, CurrentState>(StringComparer.Ordinal);
        public HashSet<string> Deleted { get; } = new HashSet<string>();
        public Dictionary<string, int> TypeInstanceCounts { get; } = new Dictionary<string, int>();

        private static string K(string uid, ParamKey key) => uid + "\u0001" + key;

        public void Set(string uid, ParamKey key, RawValue value, bool editable = true, string reason = "")
            => _states[K(uid, key)] = new CurrentState { ElementExists = true, ParameterExists = true, Value = value, Editable = editable, LockReason = reason };

        public RawValue Value(string uid, ParamKey key) => _states[K(uid, key)].Value;

        public CurrentState Get(string uid, ParamKey key)
        {
            if (uid == null || Deleted.Contains(uid)) return CurrentState.Missing();
            if (!_states.TryGetValue(K(uid, key), out var s))
                return new CurrentState { ElementExists = true, ParameterExists = false, Value = RawValue.NotApplicable };
            return new CurrentState
            {
                ElementExists = true,
                ParameterExists = !s.Value.IsNotApplicable,
                Value = s.Value,
                Editable = s.Editable,
                LockReason = s.LockReason,
                AffectedInstances = TypeInstanceCounts.TryGetValue(uid, out int n) ? n : 0
            };
        }

        /// <summary>Loads the "model" from an export package: current == exported.</summary>
        public static FakeModel From(ExportPackage pkg)
        {
            var m = new FakeModel();
            foreach (var s in pkg.Sheets)
                foreach (var r in s.Rows)
                {
                    if (r.IsLinked) continue;
                    foreach (var c in s.Columns)
                    {
                        if (c.IsContext) continue;
                        var cell = r.Get(c.Key);
                        if (cell == null || cell.Raw.IsNotApplicable) continue;
                        string uid = c.Scope == ParamScope.Type && s.RowMode == RowMode.Instance ? r.TypeUniqueId : r.UniqueId;
                        m.Set(uid, c.Key, cell.Raw, cell.Editable && !c.ColumnReadOnly);
                    }
                }
            return m;
        }
    }

    public static class TestData
    {
        public static readonly Guid QaA = new Guid("11111111-aaaa-4aaa-8aaa-111111111111");
        public static readonly Guid QaB = new Guid("22222222-bbbb-4bbb-8bbb-222222222222");
        public static readonly Guid VoltageGuid = new Guid("33333333-cccc-4ccc-8ccc-333333333333");
        public static readonly Guid EmergencyGuid = new Guid("44444444-dddd-4ddd-8ddd-444444444444");

        public static readonly ParamKey Comments = ParamKey.BuiltIn(-1010106, ParamScope.Instance);
        public static readonly ParamKey Mark = ParamKey.BuiltIn(-1001203, ParamScope.Instance);
        public static readonly ParamKey TypeMark = ParamKey.BuiltIn(-1010105, ParamScope.Type);
        public static readonly ParamKey QaStatusA = ParamKey.Shared(QaA, ParamScope.Instance);
        public static readonly ParamKey QaStatusB = ParamKey.Shared(QaB, ParamScope.Instance);
        public static readonly ParamKey Width = ParamKey.Family("autodesk.spec.aec:length-2.0.0", "Width", ParamScope.Instance);
        public static readonly ParamKey Voltage = ParamKey.Shared(VoltageGuid, ParamScope.Type);
        public static readonly ParamKey ApparentLoad = ParamKey.BuiltIn(-1140100, ParamScope.Instance);
        public static readonly ParamKey Emergency = ParamKey.Shared(EmergencyGuid, ParamScope.Instance);
        public static readonly ParamKey Level = ParamKey.BuiltIn(-1001352, ParamScope.Instance);
        public static readonly ParamKey Count = ParamKey.Project(778899, ParamScope.Instance);

        public const string T1 = "aaaaaaaa-0000-0000-0000-000000000001-0003e9a1";
        public const string T2 = "aaaaaaaa-0000-0000-0000-000000000001-0003e9a2";
        public static string Uid(int i) => "bbbbbbbb-0000-0000-0000-000000000002-" + i.ToString("x8");
        public const long BigId = 1099511627777L; // 2^40 + 1: beyond Excel's 15-digit-safe integers is not needed, but beyond Int32.

        public static DocumentIdentity Doc() => new DocumentIdentity
        {
            Title = "HRDT-ELE",
            PathName = @"C:\Projects\HRDT\HRDT-ELE.rvt",
            IsWorkshared = true,
            CentralPath = @"\\server\HRDT\HRDT-ELE.rvt",
            CreationGuid = "9b7d2f3a-1111-2222-3333-444455556666",
            ProjectInfoUniqueId = "pi-0000-0001"
        };

        public static ExportPackage LightingPackage(IUnitConverter units = null)
        {
            units = units ?? new FakeUnits();
            var pkg = new ExportPackage { AddinVersion = "0.1.0", RevitVersion = "2026", Document = Doc(), ScopeDescription = "Whole model" };
            var s = new DataSheet { SheetKey = "cat:-2001120:I", Title = "Lighting Fixtures", Source = SheetSource.ModelCategories, RowMode = RowMode.Instance };
            s.Columns.Add(PseudoColumns.Column(PseudoColumns.ElementId));
            s.Columns.Add(PseudoColumns.Column(PseudoColumns.Family));
            s.Columns.Add(PseudoColumns.Column(PseudoColumns.Type));
            s.Columns.Add(new ColumnDef { Key = Comments, Header = "Comments", Kind = ValueKind.Text });
            s.Columns.Add(new ColumnDef { Key = Mark, Header = "Mark", Kind = ValueKind.Text });
            s.Columns.Add(new ColumnDef { Key = TypeMark, Header = "Type Mark", Kind = ValueKind.Text });
            s.Columns.Add(new ColumnDef { Key = QaStatusA, Header = "QA_Status", Kind = ValueKind.Text });
            s.Columns.Add(new ColumnDef { Key = QaStatusB, Header = "QA_Status", Kind = ValueKind.Text });
            s.Columns.Add(new ColumnDef { Key = Width, Header = "Width", Kind = ValueKind.Double, SpecTypeId = "autodesk.spec.aec:length-2.0.0", UnitTypeId = FakeUnits.Millimeters, UnitLabel = "mm" });
            s.Columns.Add(new ColumnDef { Key = Voltage, Header = "Voltage", Kind = ValueKind.Double, SpecTypeId = "autodesk.spec.aec.electrical:electricalPotential-2.0.0", UnitTypeId = FakeUnits.Volts, UnitLabel = "V" });
            s.Columns.Add(new ColumnDef { Key = ApparentLoad, Header = "Apparent Load", Kind = ValueKind.Double, UnitTypeId = FakeUnits.VoltAmperes, UnitLabel = "VA", ColumnReadOnly = true, ReadOnlyReason = "Calculated by Revit." });
            s.Columns.Add(new ColumnDef { Key = Emergency, Header = "Emergency", Kind = ValueKind.YesNo });
            s.Columns.Add(new ColumnDef { Key = Level, Header = "Level", Kind = ValueKind.ElementRef, ColumnReadOnly = true, ReadOnlyReason = "Reference parameters are read-only in this version." });
            s.Columns.Add(new ColumnDef { Key = Count, Header = "Circuit Count", Kind = ValueKind.Integer });

            s.Rows.Add(Fixture(units, 1, 352001, T1, "LF-A", "Downlight", "DL-600", 600.0 / 304.8, 230 * FakeUnits.VoltFactor, "Yes", "Note, with comma"));
            s.Rows.Add(Fixture(units, 2, 352002, T1, "LF-A", "Downlight", "DL-600", 600.0 / 304.8, 230 * FakeUnits.VoltFactor, "No", "  leading spaces"));
            s.Rows.Add(Fixture(units, 3, BigId, T2, "LF-B", "Panel", "PN-1200", 1200.5 / 304.8, 240 * FakeUnits.VoltFactor, null, "=SUM(A1:A2) is text"));
            var r4 = Fixture(units, 4, 352004, T2, "LF-B", "Panel", "PN-1200", 1.0 / 3.0, 240 * FakeUnits.VoltFactor, "Yes", "line1\r\nline2");
            r4.Cells[QaStatusB] = CellValue.NotApplicable();
            s.Rows.Add(r4);

            // Linked row (read-only)
            var linked = Fixture(units, 5, 352005, T1, "LF-A", "Downlight", "DL-600", 0.5, 230 * FakeUnits.VoltFactor, "No", "linked");
            linked.RowKey = DocKeys.LinkedRowKey("link-instance-uid", Uid(5));
            linked.DocKey = DocKeys.ForLink("link-instance-uid");
            linked.IsLinked = true;
            s.Rows.Add(linked);

            pkg.Sheets.Add(s);
            return pkg;
        }

        private static RowData Fixture(IUnitConverter units, int i, long id, string typeUid, string typeMark, string family, string type,
                                       double widthInternal, double voltInternal, string emergency, string comments)
        {
            var r = new RowData { RowKey = Uid(i), UniqueId = Uid(i), ElementId = id, TypeUniqueId = typeUid, Label = "Lighting Fixtures : " + family + " : " + type };
            r.Cells[ParamKey.Pseudo(PseudoColumns.ElementId)] = new CellValue { Raw = RawValue.FromText(id.ToString()), DisplayText = id.ToString() };
            r.Cells[ParamKey.Pseudo(PseudoColumns.Family)] = new CellValue { Raw = RawValue.FromText(family), DisplayText = family };
            r.Cells[ParamKey.Pseudo(PseudoColumns.Type)] = new CellValue { Raw = RawValue.FromText(type), DisplayText = type };
            r.Cells[Comments] = Text(comments);
            r.Cells[Mark] = Text("M-" + i);
            r.Cells[TypeMark] = Text(typeMark);
            r.Cells[QaStatusA] = Text("A-ok");
            r.Cells[QaStatusB] = Text("B-pending");
            r.Cells[Width] = Dbl(units, widthInternal, FakeUnits.Millimeters);
            r.Cells[Voltage] = Dbl(units, voltInternal, FakeUnits.Volts);
            var load = Dbl(units, 1234.5678 * FakeUnits.VoltFactor * i, FakeUnits.VoltAmperes);
            load.Editable = false; load.LockReason = "Calculated by Revit.";
            r.Cells[ApparentLoad] = load;
            r.Cells[Emergency] = emergency == null ? new CellValue { Raw = RawValue.Empty, Editable = true }
                                                   : new CellValue { Raw = RawValue.FromInteger(emergency == "Yes" ? 1 : 0), DisplayText = emergency, Editable = true };
            r.Cells[Level] = new CellValue { Raw = RawValue.FromElementId(311), DisplayText = "Level 1", Editable = false, LockReason = "Reference" };
            r.Cells[Count] = new CellValue { Raw = RawValue.FromInteger(i * 10), ExportNumber = i * 10, Editable = true };
            return r;
        }

        public static CellValue Text(string s) => new CellValue { Raw = s == null ? RawValue.Empty : RawValue.FromText(s), DisplayText = s ?? "", Editable = true };

        public static CellValue Dbl(IUnitConverter units, double internalValue, string unit)
            => new CellValue { Raw = RawValue.FromDouble(internalValue), ExportNumber = units.FromInternal(internalValue, unit), DisplayText = internalValue.ToString("R"), Editable = true };

        /// <summary>Export → bytes → read back (what happens when the user opens and saves nothing).</summary>
        public static XWorkbook RoundTrip(XWorkbook wb)
        {
            using (var ms = new MemoryStream())
            {
                XlsxWriter.Write(wb, ms);
                ms.Position = 0;
                return XlsxReader.Load(ms);
            }
        }

        public static XSheet DataSheet(XWorkbook wb, string name = "Lighting Fixtures") => wb.Find(name);

        /// <summary>Finds the worksheet column whose hidden key is <paramref name="key"/>.</summary>
        public static int Col(XSheet s, ParamKey key)
        {
            for (int c = 1; c <= s.MaxColumn; c++)
                if (s.GetText(1, c) == WorkbookSchema.ColumnMarker + key) return c;
            throw new InvalidOperationException("Column not found: " + key);
        }

        /// <summary>Finds the worksheet row of an element by its row key.</summary>
        public static int Row(XSheet s, string rowKey)
        {
            for (int r = 3; r <= s.MaxRow; r++)
                if (s.GetText(r, 1) == rowKey) return r;
            throw new InvalidOperationException("Row not found: " + rowKey);
        }

        public static ChangeSet Import(XWorkbook wb, ICurrentStateSource model, ImportOptions options = null)
        {
            var imp = WorkbookImporter.Read(wb, new FakeUnits(), options ?? new ImportOptions());
            var set = new ChangeSet();
            set.Issues.AddRange(imp.Issues);
            return ChangeSetBuilder.Build(imp.Cells, model, set);
        }
    }
}
