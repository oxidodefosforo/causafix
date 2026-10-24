using System.Collections.Generic;
using System.Linq;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.ProjectSetup.Planning
{
    public enum ViewAction
    {
        None = 0,
        Create,
        Reuse,
        /// <summary>Reuse and update name/title/template.</summary>
        Update,
        /// <summary>Create a dependent copy because the view is already placed on another sheet (explicit choice).</summary>
        CreateDependent,
        Skip
    }

    public enum SheetAction
    {
        None = 0,
        Create,
        Reuse,
        Update,
        Skip
    }

    public enum PlaceAction
    {
        None = 0,
        Place,
        /// <summary>Viewport already on the target sheet: measured, moved only if out of tolerance.</summary>
        Realign,
        Skip
    }

    public enum TitleAction
    {
        None = 0,
        /// <summary>Apply the approved title placement after the drawing is aligned.</summary>
        Apply,
        Skip
    }

    public enum RowStatus
    {
        Ready = 0,
        Unchanged,
        Warning,
        Error,
        Excluded
    }

    /// <summary>One line of the Review table: a level × View Set combination (or an existing placement).</summary>
    public sealed class PlanRow
    {
        public string RowKey { get; set; }
        public string RuleKey { get; set; } = string.Empty;
        public string LevelUniqueId { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string LevelCode { get; set; } = string.Empty;
        public string ViewSetName { get; set; } = string.Empty;
        public PlanFamily Family { get; set; }
        public string ViewFamilyTypeUniqueId { get; set; } = string.Empty;
        public string ViewFamilyTypeName { get; set; } = string.Empty;
        public string TemplateUniqueId { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        public int RequestedScale { get; set; }
        public int EffectiveScale { get; set; }
        public string ScaleNote { get; set; } = string.Empty;
        public string ViewName { get; set; } = string.Empty;
        public string TitleOnSheet { get; set; } = string.Empty;
        public string SheetNumber { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public string TitleBlockUniqueId { get; set; } = string.Empty;
        public string TitleBlockName { get; set; } = string.Empty;
        public string ViewportTypeUniqueId { get; set; } = string.Empty;
        public ViewportRotationOption Rotation { get; set; }
        public string GroupKey { get; set; } = "Main";
        public string SlotName { get; set; } = "A";
        public AlignmentMethod Method { get; set; }
        public CropOption Crop { get; set; }
        public string ScopeBoxUniqueId { get; set; } = string.Empty;
        public int? Sequence { get; set; }
        public bool Include { get; set; } = true;

        // ---- sheet composition (Views on Sheet) ----
        public string CompositionKey { get; set; } = string.Empty;
        public string CompositionName { get; set; } = string.Empty;
        public MemberRole Role { get; set; }
        public CompositionMode CompositionMode { get; set; }
        /// <summary>Rows with the same id overlay each other on purpose (same sheet, same slot).</summary>
        public string OverlayGroupId { get; set; } = string.Empty;
        /// <summary>Row whose values define the sheet (number, name, title block). Equal to RowKey for the sheet's primary row.</summary>
        public string SheetPrimaryRowKey { get; set; } = string.Empty;
        public bool IsSheetPrimary => SheetPrimaryRowKey == RowKey;
        public string MemberId { get; set; } = string.Empty;

        // ---- numbering ----
        public string NumberingProfileKey { get; set; } = string.Empty;
        public string NumberingProfileName { get; set; } = string.Empty;
        /// <summary>Number the sheet has in Revit now (existing sheets).</summary>
        public string CurrentSheetNumber { get; set; } = string.Empty;
        /// <summary>Number the rule proposes (may differ from <see cref="SheetNumber"/> while a renumber is not approved).</summary>
        public string ProposedSheetNumber { get; set; } = string.Empty;
        public bool RenumberPending { get; set; }
        /// <summary>Sequence counter scope (for the Numbering preview).</summary>
        public string SequenceScopeKey { get; set; } = string.Empty;

        // ---- names ----
        public Dictionary<NameContext, NameResult> Names { get; } = new Dictionary<NameContext, NameResult>();

        // ---- view title ----
        public bool ShowsTitle { get; set; } = true;
        public TitlePlacement Title { get; set; }
        public TitleAction TitleAction { get; set; }
        /// <summary>Extra per-member title offset (mm).</summary>
        public double TitleExtraXmm { get; set; }
        public double TitleExtraYmm { get; set; }
        public string ExistingViewportUniqueId { get; set; } = string.Empty;

        public ViewAction ViewAction { get; set; }
        public SheetAction SheetAction { get; set; }
        public PlaceAction PlaceAction { get; set; }
        /// <summary>Existing view reused (or the source of a dependent copy).</summary>
        public string ExistingViewUniqueId { get; set; } = string.Empty;
        public string ExistingSheetUniqueId { get; set; } = string.Empty;
        /// <summary>Key stored on the sheet (shared sheets use a key per sheet number).</summary>
        public string SheetKey { get; set; } = string.Empty;
        /// <summary>Fields that will change on a reused view/sheet (for the action text).</summary>
        public List<string> Updates { get; } = new List<string>();

        public RowStatus Status { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public bool HasErrors => Errors.Count > 0;
        public string Reason => string.Join(" ", Errors.Concat(Warnings));

        public string ActionText
        {
            get
            {
                if (!Include) return "Excluded";
                var parts = new List<string>();
                if (ViewAction != ViewAction.None) parts.Add("View: " + ViewAction + (Updates.Any(u => u.StartsWith("view")) ? " (" + string.Join(", ", Updates.Where(u => u.StartsWith("view")).Select(u => u.Substring(5))) + ")" : ""));
                if (SheetAction != SheetAction.None) parts.Add("Sheet: " + SheetAction + (Updates.Any(u => u.StartsWith("sheet")) ? " (" + string.Join(", ", Updates.Where(u => u.StartsWith("sheet")).Select(u => u.Substring(6))) + ")" : ""));
                if (PlaceAction != PlaceAction.None) parts.Add("Viewport: " + PlaceAction + (Updates.Contains("viewport:type") ? " (type)" : ""));
                else if (Updates.Contains("viewport:type")) parts.Add("Viewport: type");
                if (TitleAction == TitleAction.Apply) parts.Add("Title: place");
                if (RenumberPending) parts.Add("Renumber pending");
                return parts.Count == 0 ? "No change" : string.Join(" · ", parts);
            }
        }
    }

    public sealed class SetupPlan
    {
        public SetupMode Mode { get; set; }
        public List<PlanRow> Rows { get; } = new List<PlanRow>();
        /// <summary>Problems not tied to one row (missing anchor, no levels selected...).</summary>
        public List<string> GlobalErrors { get; } = new List<string>();
        public List<string> GlobalWarnings { get; } = new List<string>();

        public IEnumerable<PlanRow> Included => Rows.Where(r => r.Include);
        /// <summary>Atomic by design: any error in an included row blocks the run (exclude the row or fix it).</summary>
        public bool CanRun => GlobalErrors.Count == 0 && Included.Any() && Included.All(r => !r.HasErrors);

        public int Count(RowStatus s) => Rows.Count(r => r.Status == s);
        public int ViewsToCreate => Included.Count(r => r.ViewAction == ViewAction.Create || r.ViewAction == ViewAction.CreateDependent);
        public int SheetsToCreate => Included.Where(r => r.SheetAction == SheetAction.Create).Select(r => r.SheetKey).Distinct().Count();
        public int Placements => Included.Count(r => r.PlaceAction == PlaceAction.Place);
        public int TitlesToPlace => Included.Count(r => r.TitleAction == TitleAction.Apply);
        public int PendingRenumbers => Included.Where(r => r.RenumberPending).Select(r => r.SheetKey).Distinct().Count();

        /// <summary>Rows grouped by sheet (Review): the sheet's primary row first.</summary>
        public IEnumerable<IGrouping<string, PlanRow>> BySheet => Rows.Where(r => !string.IsNullOrEmpty(r.SheetKey)).OrderBy(r => r.IsSheetPrimary ? 0 : 1).GroupBy(r => r.SheetKey);

        public string Summary =>
            Included.Count() + " row(s): " + ViewsToCreate + " view(s) to create, " + SheetsToCreate + " sheet(s) to create, " + Placements + " viewport(s) to place, " +
            Included.Count(r => r.PlaceAction == PlaceAction.Realign) + " to verify/re-align, " + TitlesToPlace + " title(s) to place" +
            (PendingRenumbers > 0 ? ", " + PendingRenumbers + " renumber(s) pending approval" : "") + " | errors " + Count(RowStatus.Error) +
            " | warnings " + Count(RowStatus.Warning) + " | unchanged " + Count(RowStatus.Unchanged);
    }
}
