using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Reports;
using CausaFix.Core.Values;

namespace CausaFix.ExcelLink
{
    /// <summary>Context shared by reviews (where reports go, defaults, document title).</summary>
    public sealed class ReviewContext
    {
        public string DocumentTitle { get; set; } = string.Empty;
        public string ReportsFolder { get; set; } = string.Empty;
        public bool WriteReport { get; set; } = true;
        public ApplyMode DefaultMode { get; set; } = ApplyMode.Atomic;
        /// <summary>Identity notes shown at the top (e.g. "Same lineage, different location – confirmed by user").</summary>
        public string IdentityNote { get; set; } = string.Empty;
    }

    public sealed class ReviewEntryViewModel : ObservableObject
    {
        private readonly ReviewViewModel _owner;
        private int _alternative = -1;

        public ReviewEntryViewModel(ReviewViewModel owner, ChangeEntry entry)
        {
            _owner = owner;
            Entry = entry;
            Alternatives = entry.Alternatives.Select(a => a.Kind == ProposalKind.Clear ? "(clear)" : ValueFormatter.Format(a.Value, entry.Column, owner.Units)).ToList();
        }

        public ChangeEntry Entry { get; }
        public int Id => Entry.Id;
        public string Element => Entry.TargetLabel;
        public string Parameter => (Entry.Column?.Header ?? Entry.Key.ToString()) + (Entry.TargetIsType ? " (T)" : "");
        public string Original => ValueFormatter.Format(Entry.Original, Entry.Column, _owner.Units);
        public string Current => Entry.State == null ? "(not read)" : !Entry.State.ElementExists || !Entry.State.ParameterExists ? "(not found)"
                               : !string.IsNullOrEmpty(Entry.State.Display) ? Entry.State.Display : ValueFormatter.Format(Entry.Current, Entry.Column, _owner.Units);
        public string Proposed => Entry.ProposedText != null ? (Entry.ProposedText.Length == 0 ? "(empty cell)" : Entry.ProposedText)
                                : Entry.IsClear ? "(clear)" : ValueFormatter.Format(Entry.Proposed, Entry.Column, _owner.Units);
        /// <summary>Full technical diagnostics for the detail panel.</summary>
        public string Diagnostic => Entry.Diagnostic;
        public string Action
        {
            get
            {
                switch (Entry.Status)
                {
                    case ChangeStatus.Change: return "Will be written if checked.";
                    case ChangeStatus.Conflict: return "Check 'Write' to overwrite the value changed in Revit, or leave it unchecked to keep Revit's value.";
                    case ChangeStatus.ProposalConflict: return "Choose one value in the Proposed column, or correct the workbook and import again.";
                    case ChangeStatus.AlreadyMatches: return "Nothing to do.";
                }
                switch (Entry.Code)
                {
                    case BlockCode.InvalidValue: return "Correct the value in the workbook (see the reason) and import again.";
                    case BlockCode.GroupError: return "Correct the invalid edit of this destination and import again; nothing is written for it meanwhile.";
                    case BlockCode.ReadOnlyParameter: return "Revit does not allow writing it (formula/calculated/constrained). Change it at its source in Revit.";
                    case BlockCode.Locked: return "Release the element (worksharing) or edit it in its group, then import again.";
                    case BlockCode.MissingElement: return "Make sure the workbook belongs to this model; re-export if elements were replaced.";
                    case BlockCode.MissingParameter: return "The parameter is not on the element any more; re-export to refresh the columns.";
                    case BlockCode.NotApplicable: return "Leave the cell as N/A.";
                    case BlockCode.Linked: return "Edit it in the linked model.";
                    case BlockCode.Unsupported: return "A CausaFix limitation (not a Revit restriction). Edit it in Revit.";
                    case BlockCode.MetadataMismatch: return "Re-export the sheet with this CausaFix version and repeat the edit.";
                    case BlockCode.ReadOnlyColumn: return "Context/informative cell; it is never imported.";
                    default: return string.Empty;
                }
            }
        }
        public string Unit => Entry.Column?.UnitLabel ?? string.Empty;
        public string Reason => Entry.Reason ?? string.Empty;
        public string Sources => Entry.SourceSummary;
        public string Affects => Entry.TargetIsType && Entry.AffectedInstances > 0 ? Entry.AffectedInstances.ToString(CultureInfo.CurrentCulture) + " instance(s) in the model" : "";
        public ChangeStatus Status => Entry.Status;
        public string StatusLabel
        {
            get
            {
                switch (Entry.Status)
                {
                    case ChangeStatus.Change: return "Ready to write";
                    case ChangeStatus.Conflict: return "Concurrent conflict";
                    case ChangeStatus.ProposalConflict: return Entry.TargetIsType ? "Type conflict" : "Conflicting edits";
                    case ChangeStatus.AlreadyMatches: return "Already in model";
                }
                switch (Entry.Code)
                {
                    case BlockCode.InvalidValue: return "Invalid value";
                    case BlockCode.GroupError: return "Group error";
                    case BlockCode.ReadOnlyParameter: return "Read-only parameter";
                    case BlockCode.Locked: return "Locked element";
                    case BlockCode.MissingElement: return "Missing element";
                    case BlockCode.MissingParameter: return "Missing parameter";
                    case BlockCode.Ambiguous: return "Ambiguous parameter";
                    case BlockCode.NotApplicable: return "N/A on element";
                    case BlockCode.Linked: return "Linked (read-only)";
                    case BlockCode.Unsupported: return "Unsupported";
                    case BlockCode.MetadataMismatch: return "Metadata mismatch";
                    case BlockCode.ReadOnlyColumn: return "Informative cell";
                    default: return "Blocked";
                }
            }
        }
        public bool CanInclude => Entry.CanInclude;
        public bool Include
        {
            get => Entry.Include;
            set
            {
                if (!Entry.CanInclude) { OnPropertyChanged(); return; }
                if (Entry.Include == value) return;
                Entry.Include = value;
                OnPropertyChanged();
                _owner.Recount();
            }
        }
        public bool HasAlternatives => Entry.Alternatives.Count > 0;
        public List<string> Alternatives { get; }

        /// <summary>Choosing an alternative resolves a ProposalConflict explicitly.</summary>
        public int SelectedAlternative
        {
            get => _alternative;
            set
            {
                if (value < 0 || value >= Entry.Alternatives.Count || value == _alternative) return;
                _alternative = value;
                ChangeSetBuilder.ChooseAlternative(Entry, value);
                foreach (var n in new[] { nameof(SelectedAlternative), nameof(Proposed), nameof(Status), nameof(StatusLabel), nameof(Reason), nameof(CanInclude), nameof(Include), nameof(Diagnostic), nameof(Action) })
                    OnPropertyChanged(n);
                _owner.Recount();
            }
        }
    }

    /// <summary>
    /// Review before writing: original / current / proposed per target, conflicts and blocked cells,
    /// explicit include/exclude, then Apply (atomic by default) and a local report.
    /// </summary>
    public sealed class ReviewViewModel : ObservableObject
    {
        public enum EntryFilter { All, ToApply, Conflicts, Blocked, AlreadyMatching }

        private readonly IExcelLinkGateway _gateway;
        private readonly IDialogService _dialogs;
        private readonly ReviewContext _context;
        private readonly List<ReviewEntryViewModel> _all;
        private EntryFilter _filter = EntryFilter.All;
        private bool _atomic;
        private bool _busy;
        private ApplyResult _result;
        private ReviewEntryViewModel _selected;
        private string _reportPath = string.Empty;

        public ReviewViewModel(IExcelLinkGateway gateway, IDialogService dialogs, ChangeSet set, IUnitConverter units, ReviewContext context, string sourceTitle)
        {
            _gateway = gateway;
            _dialogs = dialogs;
            _context = context ?? new ReviewContext();
            Changes = set;
            Units = units ?? IdentityUnitConverter.Instance;
            SourceTitle = sourceTitle;
            _atomic = _context.DefaultMode == ApplyMode.Atomic;
            _all = set.Entries.Select(e => new ReviewEntryViewModel(this, e)).ToList();
            foreach (var i in set.Issues.OrderByDescending(i => i.Severity)) Issues.Add(i);
            ApplyCommand = new AsyncCommand(ApplyAsync, () => !_busy && _result == null && WriteCount > 0, ex => _dialogs.Error("Apply changes", ex.Message));
            IncludeAllCommand = new RelayCommand(() => SetIncludes(e => e.Entry.Status == ChangeStatus.Change, true));
            ExcludeAllCommand = new RelayCommand(() => SetIncludes(e => true, false));
            OpenReportCommand = new RelayCommand(() => _dialogs.OpenWithShell(_reportPath), () => !string.IsNullOrEmpty(_reportPath));
            OpenDiagnosticCommand = new RelayCommand(() => _dialogs.OpenWithShell(DiagnosticPath), () => !string.IsNullOrEmpty(DiagnosticPath));
            RefreshVisible();
        }

        public ChangeSet Changes { get; }
        public IUnitConverter Units { get; }
        public string SourceTitle { get; }
        /// <summary>CSV with the full diagnostics of this comparison.</summary>
        public string DiagnosticPath { get; set; } = string.Empty;
        public string IdentityNote => _context.IdentityNote;
        public string DocumentTitle => _context.DocumentTitle;
        public ObservableCollection<ReviewEntryViewModel> Visible { get; } = new ObservableCollection<ReviewEntryViewModel>();
        public ObservableCollection<ImportIssue> Issues { get; } = new ObservableCollection<ImportIssue>();
        public ObservableCollection<OpResult> ResultOps { get; } = new ObservableCollection<OpResult>();
        public AsyncCommand ApplyCommand { get; }
        public RelayCommand IncludeAllCommand { get; }
        public RelayCommand ExcludeAllCommand { get; }
        public RelayCommand OpenReportCommand { get; }
        public RelayCommand OpenDiagnosticCommand { get; }

        public event EventHandler Applied;

        public EntryFilter Filter { get => _filter; set { if (Set(ref _filter, value)) RefreshVisible(); } }
        /// <summary>Row shown in the detail panel.</summary>
        public ReviewEntryViewModel Selected { get => _selected; set => Set(ref _selected, value); }
        public bool AtomicMode { get => _atomic; set { if (Set(ref _atomic, value)) OnPropertyChanged(nameof(ValidOnlyMode)); } }
        public bool ValidOnlyMode { get => !_atomic; set => AtomicMode = !value; }
        public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) ApplyCommand.Refresh(); } }
        public ApplyResult Result => _result;
        public bool HasResult => _result != null;
        public string ReportPath => _reportPath;

        public int WriteCount => _all.Count(e => e.Entry.WillWrite);
        public int TypeInstancesAffected => _all.Where(e => e.Entry.WillWrite && e.Entry.TargetIsType).Sum(e => e.Entry.AffectedInstances);
        public int Count(ChangeStatus s) => _all.Count(e => e.Entry.Status == s);

        public string Summary =>
            WriteCount + " destination(s) ready to write" +
            (TypeInstancesAffected > 0 ? " (type changes affect " + TypeInstancesAffected + " instance(s) in the whole model)" : "") +
            " | conflicts " + (Count(ChangeStatus.Conflict) + Count(ChangeStatus.ProposalConflict)) +
            " | blocked " + Count(ChangeStatus.Blocked) +
            " | already in model " + Count(ChangeStatus.AlreadyMatches) +
            " | unchanged cells " + Changes.UnchangedCells +
            (Changes.EmptyCellsIgnored > 0 ? " | empty cells ignored " + Changes.EmptyCellsIgnored : "") +
            " | messages " + Issues.Count;

        public string ResultSummary => _result == null ? string.Empty
            : (_result.Committed ? "Applied. " : "NOT applied. ") + _result.Summary +
              " (applied " + _result.Applied + ", failed " + _result.Failed + ", skipped " + _result.Skipped + ", rolled back " + _result.RolledBack + ")";

        public void Recount()
        {
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(WriteCount));
            ApplyCommand.Refresh();
        }

        private void SetIncludes(Func<ReviewEntryViewModel, bool> which, bool value)
        {
            foreach (var e in _all) if (e.CanInclude && which(e)) e.Include = value;
            Recount();
        }

        private void RefreshVisible()
        {
            Visible.Clear();
            foreach (var e in _all)
            {
                bool show;
                switch (Filter)
                {
                    case EntryFilter.ToApply: show = e.Entry.WillWrite; break;
                    case EntryFilter.Conflicts: show = e.Status == ChangeStatus.Conflict || e.Status == ChangeStatus.ProposalConflict; break;
                    case EntryFilter.Blocked: show = e.Status == ChangeStatus.Blocked; break;
                    case EntryFilter.AlreadyMatching: show = e.Status == ChangeStatus.AlreadyMatches; break;
                    default: show = true; break;
                }
                if (show) Visible.Add(e);
            }
            Recount();
        }

        public async Task ApplyAsync()
        {
            var ops = ChangeSetBuilder.ToWriteOps(Changes);
            if (ops.Count == 0) { _dialogs.Info("Apply changes", "Nothing is selected to apply."); return; }
            string msg = "Write " + ops.Count + " change(s) to '" + _context.DocumentTitle + "'?" +
                         (TypeInstancesAffected > 0 ? "\n\nType parameter changes affect " + TypeInstancesAffected + " instance(s) in the whole model, including instances that were not exported." : "") +
                         "\n\nMode: " + (AtomicMode ? "all or nothing (any failure rolls everything back)." : "apply valid changes only; failures are skipped and reported.") +
                         "\nThe model is not saved or synchronised. Undo is available in Revit.";
            if (!_dialogs.Confirm("Apply changes", msg)) return;

            IsBusy = true;
            try
            {
                _result = await _gateway.Apply(ops, AtomicMode ? ApplyMode.Atomic : ApplyMode.ValidOnly, Units);
                ResultOps.Clear();
                foreach (var o in _result.Ops) ResultOps.Add(o);
                if (_context.WriteReport && !string.IsNullOrEmpty(_context.ReportsFolder))
                {
                    try
                    {
                        var r = _result;
                        _reportPath = await Task.Run(() => ApplyReportWriter.Write(_context.ReportsFolder, _context.DocumentTitle, Changes, r, Units));
                    }
                    catch (Exception ex) { _dialogs.Error("Report", "The report could not be written: " + ex.Message); }
                }
                OnPropertyChanged(nameof(Result));
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(ResultSummary));
                OnPropertyChanged(nameof(ReportPath));
                OpenReportCommand.Refresh();
                Applied?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                IsBusy = false;
                ApplyCommand.Refresh();
            }
        }
    }
}
