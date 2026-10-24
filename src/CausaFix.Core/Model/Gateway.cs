using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.Core.Compare;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;

namespace CausaFix.Core.Model
{
    public sealed class DocumentSummary
    {
        public string Title { get; set; } = string.Empty;
        public string RevitVersion { get; set; } = string.Empty;
        public bool IsWorkshared { get; set; }
        public DocumentIdentity Identity { get; set; } = new DocumentIdentity();
        public bool CreationGuidAvailable { get; set; }
        public string ActiveViewName { get; set; } = string.Empty;
        public int SelectionCount { get; set; }
    }

    public struct ProgressInfo
    {
        public int Done;
        public int Total;
        public string Stage;
        public ProgressInfo(int done, int total, string stage) { Done = done; Total = total; Stage = stage; }
    }

    public enum InventoryKind
    {
        ProjectInformation,
        Families,
        ObjectStyles,
        LineStyles
    }

    /// <summary>
    /// Everything the Excel Link module needs from Revit, as DTOs. Implemented by CausaFix.Revit
    /// (ExcelLinkService, through ExternalEvent) and by an in-memory fake in tests.
    /// Implementations must run every Revit access in a valid API context.
    /// </summary>
    public interface IExcelLinkGateway
    {
        string AddinVersion { get; }
        Task<DocumentSummary> BindActiveDocument();
        Task<List<CategoryInfo>> Categories(ScopeMode mode);
        Task<List<TypeInfo>> Types(ScopeMode mode, IList<long> categoryIds);
        Task<List<ParameterInfo>> Parameters(ScopeMode mode, IList<long> categoryIds, RowMode rowMode, IList<string> typeUniqueIds);
        Task<List<ScheduleInfo>> Schedules();
        Task<List<ParameterInfo>> ScheduleFields(string scheduleUniqueId);
        Task<AffineUnitConverter> UnitConverter(IEnumerable<string> unitTypeIds);
        Task<ExportPackage> Snapshot(SnapshotRequest request, IProgress<ProgressInfo> progress, CancellationToken ct);
        Task<ExportPackage> SnapshotSchedules(IList<string> scheduleUniqueIds, ScheduleExportMode mode, IProgress<ProgressInfo> progress, CancellationToken ct);
        Task<ExportPackage> SnapshotInventory(InventoryKind kind, IProgress<ProgressInfo> progress, CancellationToken ct);
        Task<DocumentIdentity> CurrentIdentity();
        Task<ChangeSet> Compare(IList<ProposedCell> cells, ChangeSet seed);
        Task<ApplyResult> Apply(IList<WriteOp> ops, ApplyMode mode, IUnitConverter units);
        Task<string> Select(IEnumerable<string> uniqueIds);
        Task<string> Show(IEnumerable<string> uniqueIds);
        Task<string> Isolate(IEnumerable<string> uniqueIds);
        Task<string> ResetIsolate();
    }
}
