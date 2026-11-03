using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.Core.Mvvm;
using CausaFix.ProjectCleanup.Execution;
using CausaFix.ProjectCleanup.Model;
using CausaFix.ProjectCleanup.Planning;
using CausaFix.ProjectCleanup.Reports;

namespace CausaFix.ProjectCleanup.ViewModels
{
    /// <summary>Dialogs the cleanup window needs (implemented by the add-in's WpfDialogs).</summary>
    public interface ICleanupDialogs
    {
        void Info(string title, string message);
        void Error(string title, string message);
        void OpenWithShell(string path);
    }

    public sealed class CleanupRow
    {
        public string Action { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool IsDelete { get; set; }
    }

    public enum RowFilter { All = 0, ToDelete, Preserved, NotDocumentation }

    /// <summary>
    /// Project Cleanup window: analyse → review → one explicit "Eliminar documentación" → result. Opening, analysing
    /// and cancelling never change the model. The run uses exactly the reviewed scope (signature); when the document or
    /// the active view changed, the review is rebuilt and must be confirmed again.
    /// </summary>
    public sealed class ProjectCleanupViewModel : ObservableObject
    {
        private readonly ICleanupGateway _gw;
        private readonly ICleanupDialogs _dialogs;
        private readonly string _reportsFolder;
        private readonly Action<string> _log;
        private CleanupPlan _plan;
        private CleanupCatalog _catalog;
        private readonly CleanupOptions _options = new CleanupOptions();
        private CleanupResult _result;
        private bool _busy;
        private string _busyText = "", _status = "", _reportPath = "";
        private RowFilter _filter;
        private bool _staleNotice;

        public ProjectCleanupViewModel(ICleanupGateway gateway, ICleanupDialogs dialogs, string reportsFolder, Action<string> log = null)
        {
            _gw = gateway; _dialogs = dialogs; _reportsFolder = reportsFolder; _log = log ?? (_ => { });
            Action<Exception> err = ReportError;
            RefreshCommand = new AsyncCommand(AnalyzeAsync, () => !IsBusy, err);
            DeleteCommand = new AsyncCommand(ExecuteAsync, () => !IsBusy && _plan != null && _plan.CanRun, err);
            CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
            OpenReportCommand = new RelayCommand(() => _dialogs.OpenWithShell(_reportPath), () => !string.IsNullOrEmpty(_reportPath));
        }

        public event EventHandler CloseRequested;

        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand DeleteCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand OpenReportCommand { get; }

        public CleanupPlan Plan => _plan;

        /// <summary>Options (off by default): also delete CAD (linked and imported), Revit links, images. Changing one rebuilds the review.</summary>
        public bool DeleteCad { get => _options.DeleteCad; set { if (_options.DeleteCad == value) return; _options.DeleteCad = value; OnPropertyChanged(); Replan(); } }
        public bool DeleteRevitLinks { get => _options.DeleteRevitLinks; set { if (_options.DeleteRevitLinks == value) return; _options.DeleteRevitLinks = value; OnPropertyChanged(); Replan(); } }
        public bool DeleteImages { get => _options.DeleteImages; set { if (_options.DeleteImages == value) return; _options.DeleteImages = value; OnPropertyChanged(); Replan(); } }
        public string CadAvailable => Available(DocKind.CadLink, DocKind.CadImport);
        public string LinksAvailable => Available(DocKind.RevitLink);
        public string ImagesAvailable => Available(DocKind.Image);
        private string Available(params DocKind[] kinds) => _catalog == null ? "" : "(" + _catalog.Items.Count(i => kinds.Contains(i.Kind)) + " en el modelo)";

        private CleanupOptions OptionsCopy() => new CleanupOptions { DeleteCad = _options.DeleteCad, DeleteRevitLinks = _options.DeleteRevitLinks, DeleteImages = _options.DeleteImages };

        private void Replan()
        {
            if (_catalog == null || IsBusy) return;
            ShowPlan(CleanupPlanner.Build(_catalog, OptionsCopy()));
            StaleNotice = false;
        }
        public CleanupResult Result => _result;
        public ObservableCollection<CleanupRow> Rows { get; } = new ObservableCollection<CleanupRow>();
        public ObservableCollection<CleanupRow> ResultRows { get; } = new ObservableCollection<CleanupRow>();
        public ObservableCollection<string> Consequences { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Notes { get; } = new ObservableCollection<string>();
        public IEnumerable<RowFilter> Filters => (RowFilter[])Enum.GetValues(typeof(RowFilter));

        public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { RefreshCommand.Refresh(); DeleteCommand.Refresh(); CancelCommand.Refresh(); } } }
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
        public string Status { get => _status; private set => Set(ref _status, value ?? ""); }
        public bool StaleNotice { get => _staleNotice; private set => Set(ref _staleNotice, value); }
        public RowFilter Filter { get => _filter; set { if (Set(ref _filter, value)) FillRows(); } }

        public string DocumentTitle => _plan?.Catalog.DocumentTitle ?? "";
        public string KeptView => _plan == null ? "" : "Se conservará: " + _plan.Catalog.ActiveName + " — " + _plan.Catalog.ActiveTypeLabel;
        public string BlockedReason => _plan?.BlockedReason ?? "";
        public string CountsText
        {
            get
            {
                if (_plan == null) return "";
                if (_plan.NothingToDelete) return "No hay documentación que eliminar.";
                return "Se eliminarán: " + _plan.Count(DocKind.View) + " vista(s), " + _plan.Count(DocKind.Sheet) + " plano(s), " +
                       _plan.Count(DocKind.Schedule) + " schedule(s), " + _plan.Count(DocKind.Legend) + " leyenda(s)" +
                       (_plan.Count(DocKind.PanelSchedule) > 0 ? ", " + _plan.Count(DocKind.PanelSchedule) + " panel schedule(s)" : "") +
                       (_plan.Count(DocKind.CadLink) + _plan.Count(DocKind.CadImport) > 0 ? ", " + (_plan.Count(DocKind.CadLink) + _plan.Count(DocKind.CadImport)) + " CAD" : "") +
                       (_plan.Count(DocKind.RevitLink) > 0 ? ", " + _plan.Count(DocKind.RevitLink) + " vínculo(s) de Revit" : "") +
                       (_plan.Count(DocKind.Image) > 0 ? ", " + _plan.Count(DocKind.Image) + " imagen(es)" : "") +
                       ". Se conservan " + _plan.Preserved.Count() + " elemento(s) por protección, dependencia o limitación de Revit.";
            }
        }
        public string DeleteButtonText => _plan == null || _plan.NothingToDelete ? "Eliminar documentación" : "Eliminar documentación (" + _plan.DeleteCount + ")";
        public bool HasResult => _result != null;
        public string ResultSummary => _result == null ? "" : _result.Summary;
        public string ReportPath => _reportPath;

        public async Task InitializeAsync() => await RefreshCommand.ExecuteAsync();

        private async Task AnalyzeAsync()
        {
            await Busy("Analizando vistas, planos, schedules y sus relaciones", async () =>
            {
                var cat = await _gw.Analyze();
                ShowPlan(CleanupPlanner.Build(cat, OptionsCopy()));
                StaleNotice = false;
                Status = _plan.CanRun ? "Revisa el alcance. Nada se elimina hasta pulsar «" + DeleteButtonText + "»." : (!string.IsNullOrEmpty(_plan.BlockedReason) ? _plan.BlockedReason : "No hay documentación que eliminar.");
                _log("Project Cleanup: analysis of '" + cat.DocumentTitle + "' keeping '" + cat.ActiveName + "' [" + cat.ActiveViewId + "]: " + _plan.DeleteCount + " to delete, " + _plan.Preserved.Count() + " preserved, signature " + _plan.Signature);
            });
        }

        private async Task ExecuteAsync()
        {
            if (_plan == null || !_plan.CanRun) return;
            var approved = _plan;
            CleanupResult res = null;
            await Busy("Eliminando documentación (operación atómica)", async () => { res = await _gw.Execute(approved.Signature, approved.Options); });
            if (res == null) return;
            if (res.Stale)
            {
                ShowPlan(CleanupPlanner.Build(res.NewCatalog, OptionsCopy()));
                StaleNotice = true;
                Status = "El documento o la vista activa cambiaron después del análisis. No se eliminó nada: revisa el análisis actualizado y pulsa de nuevo «" + DeleteButtonText + "».";
                _log("Project Cleanup: stale review (" + approved.Signature + "), new analysis " + _plan.Signature);
                return;
            }
            _result = res;
            ResultRows.Clear();
            foreach (var i in res.Items.Where(i => i.Outcome != ItemOutcome.Preserved).Concat(res.Items.Where(i => i.Outcome == ItemOutcome.Preserved)))
                ResultRows.Add(new CleanupRow { Action = CleanupReport.OutcomeLabel(i.Outcome), Kind = CleanupReport.KindLabel(i.Kind), Type = i.TypeLabel, Number = i.SheetNumber, Name = i.Name, Reason = i.Message });
            try { _reportPath = await Task.Run(() => CleanupReport.Write(_reportsFolder, approved, res)); }
            catch (Exception ex) { _reportPath = ""; _log("Project Cleanup: report not written: " + ex.Message); }
            OnPropertyChanged(nameof(Result)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(ResultSummary)); OnPropertyChanged(nameof(ReportPath));
            OpenReportCommand.Refresh();
            _log("Project Cleanup: " + (res.Committed ? "committed" : "NOT applied") + " – " + res.Summary + (string.IsNullOrEmpty(res.Failure) ? "" : " – " + res.Failure));
            if (!res.Committed) _dialogs.Error("Project Cleanup", "No se aplicó la limpieza: no se eliminó nada.\n\n" + res.Failure + "\n\nPulsa «Actualizar análisis» para revisar de nuevo.");
            // The model changed (or not): the review must reflect the current document before anything else can run.
            var cat = await SafeAnalyze();
            if (cat != null) ShowPlan(CleanupPlanner.Build(cat, OptionsCopy()));
            Status = res.Summary;
        }

        private async Task<CleanupCatalog> SafeAnalyze()
        {
            try { return await _gw.Analyze(); }
            catch (Exception ex) { _log("Project Cleanup: re-analysis failed: " + ex.Message); return null; }
        }

        private void ShowPlan(CleanupPlan plan)
        {
            _plan = plan;
            _catalog = plan.Catalog;
            Consequences.Clear(); foreach (var c in plan.Consequences) Consequences.Add(c);
            Notes.Clear(); foreach (var n in plan.Notes) Notes.Add(n);
            FillRows();
            foreach (var n in new[] { nameof(Plan), nameof(DocumentTitle), nameof(KeptView), nameof(CountsText), nameof(BlockedReason), nameof(DeleteButtonText), nameof(CadAvailable), nameof(LinksAvailable), nameof(ImagesAvailable) }) OnPropertyChanged(n);
            DeleteCommand.Refresh();
        }

        private void FillRows()
        {
            Rows.Clear();
            if (_plan == null) return;
            if (_filter != RowFilter.NotDocumentation)
                foreach (var e in _plan.Entries.OrderBy(e => e.Action == PlanAction.Delete ? 1 : 0).ThenBy(e => e.Order))
                {
                    if (_filter == RowFilter.ToDelete && e.Action != PlanAction.Delete) continue;
                    if (_filter == RowFilter.Preserved && e.Action == PlanAction.Delete) continue;
                    Rows.Add(new CleanupRow
                    {
                        Action = e.Action == PlanAction.Delete ? "Eliminar" : e.Item.Id == _plan.Catalog.ActiveViewId ? "Conservar (vista activa)" : e.Action == PlanAction.Protected ? "Conservar (protección)" : "Conservar (limitación)",
                        Kind = CleanupReport.KindLabel(e.Item.Kind), Type = e.Item.TypeLabel, Number = e.Item.SheetNumber, Name = e.Item.Name, Reason = e.Reason, IsDelete = e.Action == PlanAction.Delete
                    });
                }
            if (_filter == RowFilter.All || _filter == RowFilter.NotDocumentation)
                foreach (var x in _plan.Catalog.Excluded)
                    Rows.Add(new CleanupRow { Action = "No se toca", Kind = "Sistema", Type = x.TypeLabel, Name = x.Name, Reason = x.Reason });
        }

        private async Task Busy(string text, Func<Task> body)
        {
            IsBusy = true; BusyText = text;
            try { await body(); }
            finally { IsBusy = false; BusyText = ""; }
        }

        public void ReportError(Exception ex)
        {
            _log("Project Cleanup error: " + ex);
            Status = "Error: " + ex.Message;
            _dialogs.Error("Project Cleanup", ex.Message);
        }
    }
}
