using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;

namespace CausaFix.Revit
{
    /// <summary>
    /// Facade used by the Excel Link UI. Every Revit access goes through the dispatcher (valid API context)
    /// and returns DTOs. Long reads are split into chunks: progress is reported and cancellation is honoured
    /// between chunks; no transaction is kept open between callbacks.
    /// </summary>
    public sealed class ExcelLinkService : IExcelLinkGateway, IDisposable
    {
        public const int ChunkSize = 400;

        private readonly RevitDispatcher _dispatcher;
        private readonly RevitSession _session = new RevitSession();
        private readonly RevitUnits _units = new RevitUnits();

        public ExcelLinkService(RevitDispatcher dispatcher) { _dispatcher = dispatcher; }

        public string AddinVersion => typeof(ExcelLinkService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        public RevitSession Session => _session;

        private Task<T> Run<T>(Func<UIApplication, Document, T> f, CancellationToken ct = default)
            => _dispatcher.InvokeAsync(app => f(app, _session.Require(app)), ct);

        public Task<DocumentSummary> BindActiveDocument() => _dispatcher.InvokeAsync(app =>
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) throw new DocumentChangedException("No document is open in Revit.");
            if (doc.IsFamilyDocument) throw new DocumentChangedException("Excel Link works on project documents, not families.");
            _session.Bind(doc);
            return new DocumentSummary
            {
                Title = doc.Title,
                RevitVersion = app.Application.VersionNumber,
                IsWorkshared = doc.IsWorkshared,
                Identity = DocumentIdentityReader.Read(doc),
                CreationGuidAvailable = RevitCompat.HasCreationGuid,
                ActiveViewName = app.ActiveUIDocument.ActiveView?.Name ?? "",
                SelectionCount = app.ActiveUIDocument.Selection.GetElementIds().Count
            };
        });

        public Task<string> ScopeError(ScopeMode mode) => Run((app, doc) => ModelReader.ScopeError(app.ActiveUIDocument, mode));

        public Task<List<CategoryInfo>> Categories(ScopeMode mode) => Run((app, doc) =>
        {
            string err = ModelReader.ScopeError(app.ActiveUIDocument, mode);
            if (err != null) throw new InvalidOperationException(err);
            return ModelReader.Categories(app.ActiveUIDocument, mode);
        });

        public Task<List<TypeInfo>> Types(ScopeMode mode, IList<long> categoryIds)
            => Run((app, doc) => ModelReader.Types(app.ActiveUIDocument, mode, categoryIds));

        public Task<List<ParameterInfo>> Parameters(ScopeMode mode, IList<long> categoryIds, RowMode rowMode, IList<string> typeUniqueIds)
            => Run((app, doc) => ModelReader.Parameters(app.ActiveUIDocument, mode, categoryIds, rowMode, typeUniqueIds));

        public Task<List<ScheduleInfo>> Schedules() => Run((app, doc) => ScheduleReader.List(app.ActiveUIDocument));

        public Task<List<ParameterInfo>> ScheduleFields(string scheduleUniqueId) => Run((app, doc) =>
        {
            var vs = doc.GetElement(scheduleUniqueId) as ViewSchedule ?? throw new InvalidOperationException("Schedule not found.");
            var info = ScheduleReader.Describe(doc, vs);
            if (!info.DataExchangeSupported) return new List<ParameterInfo>();
            var plan = ScheduleReader.PlanData(doc, vs, _units);
            return plan.Sheet.Columns.Where(c => !c.IsContext).Select(c => new ParameterInfo
            {
                Key = c.Key, Name = c.Header, Kind = c.Kind, DataTypeLabel = c.DataTypeLabel, SpecTypeId = c.SpecTypeId,
                UnitTypeId = c.UnitTypeId, UnitLabel = c.UnitLabel, ReadOnly = c.ColumnReadOnly, ReadOnlyReason = c.ReadOnlyReason,
                AppliesTo = plan.Ids.Count, Total = plan.Ids.Count
            }).ToList();
        });

        /// <summary>Captures unit conversions (affine) for use outside the API context.</summary>
        public Task<AffineUnitConverter> UnitConverter(IEnumerable<string> unitTypeIds)
        {
            var list = unitTypeIds.Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
            return _dispatcher.InvokeAsync(app => BuildConverter(list));
        }

        private AffineUnitConverter BuildConverter(IEnumerable<string> units)
        {
            var conv = new AffineUnitConverter();
            foreach (var u in units) conv.Register(u, _units.FromInternal(0, u), _units.FromInternal(1, u));
            return conv;
        }

        private ExportPackage NewPackage(UIApplication app, Document doc, string scope) => new ExportPackage
        {
            AddinVersion = AddinVersion,
            RevitVersion = app.Application.VersionNumber,
            Document = DocumentIdentityReader.Read(doc),
            ScopeDescription = scope,
            ExportedUtc = DateTime.UtcNow
        };

        private async Task ReadPlans(List<SheetPlan> plans, ExportPackage pkg, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            int total = plans.Sum(p => p.Ids.Count), done = 0;
            foreach (var plan in plans)
            {
                for (int start = 0; start < plan.Ids.Count; start += ChunkSize)
                {
                    ct.ThrowIfCancellationRequested();
                    int s = start;
                    var rows = await Run((app, doc) => SnapshotBuilder.ReadChunk(doc, plan, s, ChunkSize, _units), ct).ConfigureAwait(true);
                    plan.Sheet.Rows.AddRange(rows);
                    done += Math.Min(ChunkSize, plan.Ids.Count - s);
                    progress?.Report(new ProgressInfo(done, total, "Reading " + plan.Sheet.Title));
                }
                await FillReferenceCandidates(plan, ct).ConfigureAwait(true);
                pkg.Sheets.Add(plan.Sheet);
            }
        }

        /// <summary>For reference columns: the class of the referenced elements and the list of assignable values.</summary>
        private async Task FillReferenceCandidates(SheetPlan plan, CancellationToken ct)
        {
            if (plan.LinkInstanceUniqueId != null) return;
            foreach (var col in plan.Sheet.Columns)
            {
                if (col.Kind != ValueKind.ElementRef) continue;
                var ids = plan.Sheet.Rows.Select(r => r.Get(col.Key)).Where(c => c != null && c.Raw.Kind == RawKind.ElementId)
                                          .Select(c => c.Raw.Integer).Distinct().ToList();
                if (ids.Count == 0) continue;
                var (cls, list) = await Run((app, doc) => ReferenceCatalog.Build(doc, ids), ct).ConfigureAwait(true);
                col.RefClass = cls;
                col.RefCandidates = list;
            }
        }

        /// <summary>Categories / Elements / Spatial snapshot.</summary>
        public async Task<ExportPackage> Snapshot(SnapshotRequest req, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            progress?.Report(new ProgressInfo(0, 0, "Collecting elements"));
            var (plan, pkg) = await Run((app, doc) =>
            {
                var p = SnapshotBuilder.Plan(app.ActiveUIDocument, req);
                return (p, NewPackage(app, doc, p.ScopeDescription));
            }, ct).ConfigureAwait(true);
            foreach (var note in plan.Notes) foreach (var s in plan.Sheets) if (!s.Sheet.Notes.Contains(note)) s.Sheet.Notes.Add(note);
            await ReadPlans(plan.Sheets, pkg, progress, ct).ConfigureAwait(true);
            return pkg;
        }

        public async Task<ExportPackage> SnapshotSchedules(IList<string> scheduleUniqueIds, ScheduleExportMode mode, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            progress?.Report(new ProgressInfo(0, 0, "Reading schedules"));
            var (plans, pkg) = await Run((app, doc) =>
            {
                var list = new List<SheetPlan>();
                var p = NewPackage(app, doc, "Schedules (" + mode + ")");
                foreach (var uid in scheduleUniqueIds)
                {
                    var vs = doc.GetElement(uid) as ViewSchedule;
                    if (vs == null) continue;
                    if (mode == ScheduleExportMode.Presentation) p.Sheets.Add(ScheduleReader.Presentation(vs));
                    else list.Add(ScheduleReader.PlanData(doc, vs, _units));
                }
                return (list, p);
            }, ct).ConfigureAwait(true);
            await ReadPlans(plans, pkg, progress, ct).ConfigureAwait(true);
            return pkg;
        }

        public async Task<ExportPackage> SnapshotInventory(InventoryKind kind, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            if (kind == InventoryKind.ProjectInformation)
            {
                var (plan, pkg) = await Run((app, doc) => (Inventories.PlanProjectInformation(doc), NewPackage(app, doc, "Project Information")), ct).ConfigureAwait(true);
                await ReadPlans(new List<SheetPlan> { plan }, pkg, progress, ct).ConfigureAwait(true);
                return pkg;
            }
            return await Run((app, doc) =>
            {
                var pkg = NewPackage(app, doc, kind.ToString());
                pkg.Sheets.Add(kind == InventoryKind.Families ? Inventories.Families(doc)
                             : kind == InventoryKind.ObjectStyles ? Inventories.ObjectStyles(doc)
                             : Inventories.LineStyles(doc));
                return pkg;
            }, ct).ConfigureAwait(true);
        }

        public Task<DocumentIdentity> CurrentIdentity() => Run((app, doc) => DocumentIdentityReader.Read(doc));

        /// <summary>Three-state comparison against the live model (API context; reads only).</summary>
        public Task<ChangeSet> Compare(IList<ProposedCell> cells, ChangeSet seed) => Run((app, doc) =>
            ChangeSetBuilder.Build(cells, new CurrentStateReader(doc), seed));

        public Task<ApplyResult> Apply(IList<WriteOp> ops, ApplyMode mode, IUnitConverter units) => Run((app, doc) =>
            ApplyEngine.Apply(doc, ops, mode, units));

        public Task<string> Select(IEnumerable<string> uids) { var l = uids.ToList(); return Run((app, doc) => ViewActions.Select(app.ActiveUIDocument, l)); }
        public Task<string> Show(IEnumerable<string> uids) { var l = uids.ToList(); return Run((app, doc) => ViewActions.Show(app.ActiveUIDocument, l)); }
        public Task<string> Isolate(IEnumerable<string> uids) { var l = uids.ToList(); return Run((app, doc) => ViewActions.IsolateTemporary(app.ActiveUIDocument, l)); }
        public Task<string> ResetIsolate() => Run((app, doc) => ViewActions.ResetIsolate(app.ActiveUIDocument));

        public void Dispose() => _dispatcher.Dispose();
    }
}
