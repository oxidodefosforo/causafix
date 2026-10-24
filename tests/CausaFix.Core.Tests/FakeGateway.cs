using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;
using CausaFix.ExcelLink;

namespace CausaFix.Core.Tests
{
    /// <summary>In-memory Revit stand-in implementing the same gateway as CausaFix.Revit.</summary>
    public sealed class FakeGateway : IExcelLinkGateway
    {
        public readonly ExportPackage Template;
        public readonly FakeModel Model;
        public readonly FakeUnits UnitsImpl = new FakeUnits();
        public HashSet<string> FailWritesOn { get; } = new HashSet<string>();
        public DocumentIdentity Identity { get; set; } = TestData.Doc();
        public int ApplyCalls;

        public FakeGateway()
        {
            Template = TestData.LightingPackage();
            Model = FakeModel.From(Template);
        }

        public string AddinVersion => "0.1.0-test";

        public Task<DocumentSummary> BindActiveDocument() => Task.FromResult(new DocumentSummary
        {
            Title = Identity.Title, RevitVersion = "2026", Identity = Identity, CreationGuidAvailable = true, ActiveViewName = "Level 1"
        });

        public Task<List<CategoryInfo>> Categories(ScopeMode mode) => Task.FromResult(new List<CategoryInfo>
        {
            new CategoryInfo { Id = -2001120, Name = "Lighting Fixtures", Kind = CategoryKind.Model, Count = 4 },
            new CategoryInfo { Id = -2005020, Name = "Lighting Fixture Tags", Kind = CategoryKind.Annotation, Count = 2 },
            new CategoryInfo { Id = -2000160, Name = "Rooms", Kind = CategoryKind.Model, Count = 3, IsSpatial = true }
        });

        public Task<List<TypeInfo>> Types(ScopeMode mode, IList<long> categoryIds) => Task.FromResult(new List<TypeInfo>
        {
            new TypeInfo { UniqueId = TestData.T1, Id = 1, CategoryId = -2001120, CategoryName = "Lighting Fixtures", FamilyName = "Downlight", TypeName = "DL-600", InstanceCount = 2 },
            new TypeInfo { UniqueId = TestData.T2, Id = 2, CategoryId = -2001120, CategoryName = "Lighting Fixtures", FamilyName = "Panel", TypeName = "PN-1200", InstanceCount = 2 }
        });

        public Task<List<ParameterInfo>> Parameters(ScopeMode mode, IList<long> categoryIds, RowMode rowMode, IList<string> typeUniqueIds)
        {
            if (!categoryIds.Contains(-2001120)) return Task.FromResult(new List<ParameterInfo>());
            var list = Template.Sheets[0].Columns.Where(c => !c.IsContext)
                .Where(c => rowMode == RowMode.Instance || c.Scope == ParamScope.Type)
                .Select(c => new ParameterInfo
                {
                    Key = c.Key, Name = c.Header, Kind = c.Kind, SpecTypeId = c.SpecTypeId, UnitTypeId = c.UnitTypeId, UnitLabel = c.UnitLabel,
                    ReadOnly = c.ColumnReadOnly, ReadOnlyReason = c.ReadOnlyReason, AppliesTo = 4, Total = 4, DataTypeLabel = c.Kind.ToString()
                }).ToList();
            return Task.FromResult(list);
        }

        public Task<List<ScheduleInfo>> Schedules() => Task.FromResult(new List<ScheduleInfo>
        {
            new ScheduleInfo { UniqueId = "sch-1", Name = "Lighting Schedule", Kind = ScheduleKind.Regular, DataExchangeSupported = true, Itemized = false, FieldCount = 4 },
            new ScheduleInfo { UniqueId = "sch-key", Name = "Room Style Keys", Kind = ScheduleKind.KeySchedule, DataExchangeSupported = false, DataExchangeNote = "Key schedule", OnActiveSheet = true }
        });

        public Task<List<ParameterInfo>> ScheduleFields(string uid) => Parameters(ScopeMode.WholeModel, new List<long> { -2001120 }, RowMode.Instance, null);

        public Task<AffineUnitConverter> UnitConverter(IEnumerable<string> unitTypeIds)
        {
            var c = new AffineUnitConverter();
            foreach (var u in unitTypeIds.Where(u => !string.IsNullOrEmpty(u)).Distinct()) c.Register(u, UnitsImpl.FromInternal(0, u), UnitsImpl.FromInternal(1, u));
            return Task.FromResult(c);
        }

        /// <summary>Builds the snapshot from the template rows, with CURRENT model values.</summary>
        public Task<ExportPackage> Snapshot(SnapshotRequest req, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var src = Template.Sheets[0];
            var pkg = new ExportPackage { Document = Identity, AddinVersion = AddinVersion, RevitVersion = "2026", ScopeDescription = req.Scope.ToString() };
            var ds = new DataSheet { SheetKey = "cat:-2001120:I", Title = "Lighting Fixtures", Source = req.Source, RowMode = RowMode.Instance };
            ds.Columns.AddRange(src.Columns.Where(c => c.IsContext));
            foreach (var p in req.Parameters) ds.Columns.Add(src.FindColumn(p.Key)?.Clone() ?? p.ToColumn());
            foreach (var r in src.Rows)
            {
                var row = new RowData { RowKey = r.RowKey, UniqueId = r.UniqueId, ElementId = r.ElementId, TypeUniqueId = r.TypeUniqueId, IsLinked = r.IsLinked, DocKey = r.DocKey, Label = r.Label };
                foreach (var c in ds.Columns)
                {
                    var orig = r.Get(c.Key);
                    if (orig == null) continue;
                    if (c.IsContext || r.IsLinked || orig.Raw.IsNotApplicable) { row.Cells[c.Key] = orig; continue; }
                    string uid = c.Scope == ParamScope.Type ? r.TypeUniqueId : r.UniqueId;
                    var st = Model.Get(uid, c.Key);
                    var cell = new CellValue { Raw = st.Value, Editable = orig.Editable && st.Editable, LockReason = st.LockReason, DisplayText = st.Value.ToString() };
                    if (c.Kind == ValueKind.Double && st.Value.Kind == RawKind.Double) cell.ExportNumber = UnitsImpl.FromInternal(st.Value.Double, c.UnitTypeId);
                    if (c.Kind == ValueKind.Integer && st.Value.Kind == RawKind.Integer) cell.ExportNumber = st.Value.Integer;
                    if (c.Kind == ValueKind.ElementRef) cell.DisplayText = orig.DisplayText;
                    row.Cells[c.Key] = cell;
                }
                ds.Rows.Add(row);
            }
            pkg.Sheets.Add(ds);
            progress?.Report(new ProgressInfo(ds.Rows.Count, ds.Rows.Count, "Reading"));
            return Task.FromResult(pkg);
        }

        public Task<ExportPackage> SnapshotSchedules(IList<string> ids, ScheduleExportMode mode, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            var pkg = new ExportPackage { Document = Identity };
            var pres = new DataSheet { SheetKey = "schp:sch-1", Title = "Lighting Schedule (view)", Source = SheetSource.SchedulePresentation, ReadOnly = true, PresentationHeaderRows = 1 };
            pres.PresentationGrid.Add(new[] { "Type", "Count" });
            pres.PresentationGrid.Add(new[] { "DL-600", "2" });
            pkg.Sheets.Add(pres);
            return Task.FromResult(pkg);
        }

        public Task<ExportPackage> SnapshotInventory(InventoryKind kind, IProgress<ProgressInfo> progress, CancellationToken ct)
            => Task.FromResult(new ExportPackage { Document = Identity });

        public Task<DocumentIdentity> CurrentIdentity() => Task.FromResult(Identity);

        public Task<ChangeSet> Compare(IList<ProposedCell> cells, ChangeSet seed) => Task.FromResult(ChangeSetBuilder.Build(cells, Model, seed));

        /// <summary>Same semantics as ApplyEngine: re-check expected value, atomic rollback.</summary>
        public Task<ApplyResult> Apply(IList<WriteOp> ops, ApplyMode mode, IUnitConverter units)
        {
            ApplyCalls++;
            var result = new ApplyResult { Mode = mode };
            var undo = new List<Action>();
            bool abort = false;
            foreach (var op in ops)
            {
                var r = new OpResult { EntryId = op.EntryId, TargetUniqueId = op.TargetUniqueId, Parameter = op.Column.Header, Label = op.Label,
                    OldValue = ValueFormatter.Format(op.ExpectedCurrent, op.Column, units), NewValue = op.Clear ? "(cleared)" : ValueFormatter.Format(op.Value, op.Column, units) };
                result.Ops.Add(r);
                if (abort) { r.Outcome = OpOutcome.Skipped; continue; }
                var st = Model.Get(op.TargetUniqueId, op.Key);
                string fail = null;
                if (!st.ElementExists) fail = "missing";
                else if (!ValueComparer.AreEqual(st.Value, op.ExpectedCurrent)) fail = "Changed in Revit after the review";
                else if (FailWritesOn.Contains(op.TargetUniqueId)) fail = "Simulated Revit failure";
                if (fail != null)
                {
                    r.Outcome = OpOutcome.Failed; r.Message = fail;
                    if (mode == ApplyMode.Atomic) abort = true;
                    continue;
                }
                var before = st.Value;
                Model.Set(op.TargetUniqueId, op.Key, op.Clear ? RawValue.Empty : op.Value);
                undo.Add(() => Model.Set(op.TargetUniqueId, op.Key, before));
                r.Outcome = OpOutcome.Applied;
            }
            if (abort)
            {
                for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
                result.MarkRolledBack("Rolled back.");
            }
            else result.Committed = true;
            result.Summary = result.Committed ? result.Applied + " applied" : "nothing applied";
            return Task.FromResult(result);
        }

        public List<string> ViewCalls { get; } = new List<string>();
        public Task<string> Select(IEnumerable<string> ids) { ViewCalls.Add("select:" + string.Join(",", ids)); return Task.FromResult("selected"); }
        public Task<string> Show(IEnumerable<string> ids) { ViewCalls.Add("show"); return Task.FromResult("shown"); }
        public Task<string> Isolate(IEnumerable<string> ids) { ViewCalls.Add("isolate"); return Task.FromResult("isolated"); }
        public Task<string> ResetIsolate() { ViewCalls.Add("reset"); return Task.FromResult("reset"); }
    }

    public sealed class FakeDialogs : IDialogService
    {
        public string SavePath, OpenPath, ProfilePath;
        public bool ConfirmAnswer = true;
        public string PromptAnswer = "Profile A";
        public List<string> Errors { get; } = new List<string>();
        public List<string> Infos { get; } = new List<string>();
        public List<string> Confirms { get; } = new List<string>();
        public ReviewViewModel LastReview;
        public List<string> Opened { get; } = new List<string>();

        public string PickSaveWorkbook(string suggestedName, string folder) => SavePath;
        public string PickOpenWorkbook(string folder) => OpenPath;
        public string PickOpenProfile() => ProfilePath;
        public string PickSaveProfile(string suggestedName) => ProfilePath;
        public bool Confirm(string title, string message) { Confirms.Add(title + ": " + message); return ConfirmAnswer; }
        public void Info(string title, string message) => Infos.Add(title + ": " + message);
        public void Error(string title, string message) => Errors.Add(title + ": " + message);
        public string Prompt(string title, string label, string defaultValue) => PromptAnswer;
        public int Choose(string title, string message, IList<string> options) => -1;
        public void OpenWithShell(string path) => Opened.Add(path);
        public void ShowReview(ReviewViewModel review) => LastReview = review;
    }
}
