using System;
using System.Collections.Generic;
using System.Linq;

namespace CausaFix.ProjectCleanup.Model
{
    /// <summary>Kind of documentation element, decided from the Revit class (never from names or the Project Browser).</summary>
    public enum DocKind
    {
        /// <summary>Plans, RCPs, structural and area plans, sections, elevations, details, callouts, 3D, drafting…</summary>
        View = 0,
        /// <summary>ViewSheet (placeholders included).</summary>
        Sheet,
        /// <summary>ViewSchedule (material takeoffs, note blocks, sheet/view lists, keynote legends).</summary>
        Schedule,
        /// <summary>ViewType.Legend.</summary>
        Legend,
        /// <summary>PanelScheduleView (documentation; the panel and its circuits are model elements and stay).</summary>
        PanelSchedule,
        /// <summary>Linked CAD (CADLinkType that is an external file reference) with its ImportInstances. Option.</summary>
        CadLink,
        /// <summary>Imported CAD (CADLinkType that is not a link) with its ImportInstances. Option.</summary>
        CadImport,
        /// <summary>Revit link (RevitLinkType) with its RevitLinkInstances. Option.</summary>
        RevitLink,
        /// <summary>Raster image (ImageType) with its ImageInstances. Option.</summary>
        Image
    }

    /// <summary>Optional, explicit extras of the cleanup (off by default).</summary>
    public sealed class CleanupOptions
    {
        public bool DeleteCad { get; set; }
        public bool DeleteRevitLinks { get; set; }
        public bool DeleteImages { get; set; }
        public bool Includes(DocKind k) => k == DocKind.CadLink || k == DocKind.CadImport ? DeleteCad : k == DocKind.RevitLink ? DeleteRevitLinks : k == DocKind.Image ? DeleteImages : true;
        public static bool IsExternal(DocKind k) => k == DocKind.CadLink || k == DocKind.CadImport || k == DocKind.RevitLink || k == DocKind.Image;
        public string Key => (DeleteCad ? "C" : "-") + (DeleteRevitLinks ? "L" : "-") + (DeleteImages ? "I" : "-");
    }

    /// <summary>One documentation element of the active document (DTO read in API context).</summary>
    public sealed class DocItem
    {
        public long Id { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DocKind Kind { get; set; }
        /// <summary>Revit view type in plain words (Floor Plan, Section, 3D View, Sheet, Schedule, Legend…).</summary>
        public string TypeLabel { get; set; } = string.Empty;
        public string SheetNumber { get; set; } = string.Empty;
        public bool IsPlaceholder { get; set; }
        /// <summary>View.GetPrimaryViewId (0 when the view is independent).</summary>
        public long PrimaryViewId { get; set; }
        /// <summary>Sheets: views and legends placed with viewports (ViewSheet.GetAllPlacedViews).</summary>
        public List<long> PlacedViewIds { get; set; } = new List<long>();
        /// <summary>Sheets: schedules placed with ScheduleSheetInstance.ScheduleId (all segments).</summary>
        public List<long> PlacedScheduleIds { get; set; } = new List<long>();
        /// <summary>Sheets: panel schedules placed with PanelScheduleSheetInstance.</summary>
        public List<long> PlacedPanelScheduleIds { get; set; } = new List<long>();
        public int ViewportCount { get; set; }
        public int ScheduleInstanceCount { get; set; }
        /// <summary>Elements owned by this view (annotations, details, title blocks of sheets…): removed with it.</summary>
        public int OwnedElementCount { get; set; }
        /// <summary>Area plans: Areas of the plan, removed with it by Revit.</summary>
        public int AreaCount { get; set; }
        /// <summary>False when Revit reports the element cannot be deleted (DocumentValidation.CanDeleteElement).</summary>
        public bool CanDelete { get; set; } = true;
        public string CannotDeleteReason { get; set; } = string.Empty;
        /// <summary>Worksharing: owned by another user / not up to date (reason), or empty.</summary>
        public string LockReason { get; set; } = string.Empty;
        /// <summary>Open in a Revit window (it is closed before deleting).</summary>
        public bool IsOpen { get; set; }
        public bool IsStartingView { get; set; }
        /// <summary>CAD / links / images: instances deleted with the type, and the views that own them (view-specific placements).</summary>
        public List<long> InstanceIds { get; set; } = new List<long>();
        public List<long> InstanceOwnerViewIds { get; set; } = new List<long>();
        /// <summary>Links: file path or "not loaded" (information).</summary>
        public string Detail { get; set; } = string.Empty;

        public string Display => (Kind == DocKind.Sheet && !string.IsNullOrEmpty(SheetNumber) ? SheetNumber + " - " : "") + Name;
    }

    /// <summary>Views that are not deletable user documentation: listed in the analysis, never deleted.</summary>
    public sealed class ExcludedItem
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TypeLabel { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Everything the analysis needs, read once from the active document.</summary>
    public sealed class CleanupCatalog
    {
        public string DocumentTitle { get; set; } = string.Empty;
        /// <summary>Stable identity of the document for this session (stale detection).</summary>
        public string DocumentKey { get; set; } = string.Empty;
        public bool IsReadOnly { get; set; }
        public bool IsWorkshared { get; set; }
        public bool IsFamilyDocument { get; set; }

        public long ActiveViewId { get; set; }
        public string ActiveUniqueId { get; set; } = string.Empty;
        public string ActiveName { get; set; } = string.Empty;
        public string ActiveTypeLabel { get; set; } = string.Empty;

        public List<DocItem> Items { get; } = new List<DocItem>();
        public List<ExcludedItem> Excluded { get; } = new List<ExcludedItem>();
        public int TemplateCount { get; set; }
        public int PanelScheduleTemplateCount { get; set; }

        public DocItem Item(long id) => Items.FirstOrDefault(i => i.Id == id);
    }
}
