using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CausaFix.ProjectSetup.Geometry;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Planning;

namespace CausaFix.ProjectSetup.Execution
{
    public enum RowOutcome
    {
        Created = 0,
        Reused,
        Updated,
        Aligned,
        /// <summary>Anchor within tolerance but scale/orientation differ from the reference: no full overlay claimed.</summary>
        AnchorAlignedDifferentScale,
        Unchanged,
        Skipped,
        Failed,
        /// <summary>Was done inside the transaction but the whole operation was rolled back.</summary>
        RolledBack
    }

    public sealed class RowResult
    {
        public string RowKey { get; set; }
        public string ViewName { get; set; } = string.Empty;
        public string SheetNumber { get; set; } = string.Empty;
        public string ViewUniqueId { get; set; } = string.Empty;
        public string SheetUniqueId { get; set; } = string.Empty;
        public string ViewportUniqueId { get; set; } = string.Empty;
        public List<RowOutcome> Outcomes { get; } = new List<RowOutcome>();
        public string Message { get; set; } = string.Empty;
        /// <summary>Alignment measurement (mm of paper). Null when not placed/measured.</summary>
        public double? TargetXmm { get; set; }
        public double? TargetYmm { get; set; }
        public double? FinalXmm { get; set; }
        public double? FinalYmm { get; set; }
        public double? ErrorMm { get; set; }
        public double? SecondPointErrorMm { get; set; }
        public bool Moved { get; set; }
        /// <summary>Measured viewport box on the sheet (mm): minX, minY, maxX, maxY.</summary>
        public double[] BoxMm { get; set; }
        public string OutcomeText => string.Join(", ", Outcomes);
        public bool Failed => Outcomes.Contains(RowOutcome.Failed) || Outcomes.Contains(RowOutcome.RolledBack);
    }

    public sealed class SetupResult
    {
        public bool Committed { get; set; }
        public bool Cancelled { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<RowResult> Rows { get; } = new List<RowResult>();
        public List<string> RevitMessages { get; } = new List<string>();
        public TimeSpan Duration { get; set; }

        public int Count(RowOutcome o) => Rows.Count(r => r.Outcomes.Contains(o));

        /// <summary>Called when the transaction group did not commit: nothing is reported as done.</summary>
        public void MarkRolledBack(string reason)
        {
            Committed = false;
            foreach (var r in Rows)
            {
                if (r.Outcomes.Contains(RowOutcome.Failed) || r.Outcomes.Contains(RowOutcome.Skipped)) continue;
                r.Outcomes.Clear();
                r.Outcomes.Add(RowOutcome.RolledBack);
                r.Message = string.IsNullOrEmpty(r.Message) ? reason : r.Message + " " + reason;
                r.Moved = false;
            }
        }

        public string Counts => "Created " + Count(RowOutcome.Created) + " · Reused " + Count(RowOutcome.Reused) + " · Updated " + Count(RowOutcome.Updated) +
                                " · Aligned " + Count(RowOutcome.Aligned) + " · Anchor only " + Count(RowOutcome.AnchorAlignedDifferentScale) +
                                " · Unchanged " + Count(RowOutcome.Unchanged) + " · Skipped " + Count(RowOutcome.Skipped) +
                                " · Failed " + Count(RowOutcome.Failed) + " · Rolled back " + Count(RowOutcome.RolledBack);
    }

    /// <summary>Result of picking a model anchor in Revit.</summary>
    public sealed class AnchorPick
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public ModelAnchor Anchor { get; set; }
    }

    /// <summary>Result of reading a reference viewport (Match Reference Viewport).</summary>
    public sealed class ReferencePick
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        /// <summary>Where the group's anchor lands on the reference sheet, mm (sheet frame).</summary>
        public double SheetXmm { get; set; }
        public double SheetYmm { get; set; }
        /// <summary>Same point in the local frame of the reference sheet's title block, mm. Null when there is no single title block.</summary>
        public double? TitleBlockXmm { get; set; }
        public double? TitleBlockYmm { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>Everything Project Setup needs from Revit (DTOs only). Implemented by CausaFix.Revit through ExternalEvent.</summary>
    public interface IProjectSetupGateway
    {
        Task<string> BindActiveDocument();
        /// <summary>Stable key of the active document (creation GUID / project information), used to validate anchors.</summary>
        string DocumentKey { get; }
        Task<SetupCatalog> ReadCatalog();
        /// <summary>Asks the user to pick two straight grids in the active view; returns their intersection.</summary>
        Task<AnchorPick> PickGridIntersection(bool second);
        /// <summary>Asks the user to pick a point in the active plan view (needs a work plane).</summary>
        Task<AnchorPick> PickPoint(bool second);
        /// <summary>Asks the user to pick a viewport on the active sheet and computes where the anchor lands.</summary>
        Task<ReferencePick> PickReferenceViewport(ModelAnchor anchor, AlignmentMethod method);
        Task<SetupResult> Run(SetupConfig config, SetupPlan plan, IProgress<string> progress, CancellationToken ct);
        Task<string> ShowElement(string uniqueId);
    }

    /// <summary>Dialogs requested by the Project Setup view models (implemented by the WPF host).</summary>
    public interface ISetupDialogs
    {
        bool Confirm(string title, string message);
        void Info(string title, string message);
        void Error(string title, string message);
        string Prompt(string title, string label, string defaultValue);
        string PickOpenProfile();
        string PickSaveProfile(string suggestedName);
    }
}
