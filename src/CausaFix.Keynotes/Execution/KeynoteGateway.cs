using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.Keynotes.Editing;
using CausaFix.Keynotes.Model;

namespace CausaFix.Keynotes.Execution
{
    /// <summary>The model's keynote table as Revit reports it (DTO; no Revit types).</summary>
    public sealed class KeynoteTableInfo
    {
        public string DocumentTitle { get; set; } = string.Empty;
        public bool DocumentReadOnly { get; set; }
        public string DocumentReadOnlyReason { get; set; } = string.Empty;
        public bool IsWorkshared { get; set; }
        /// <summary>User-visible path of the configured keynote file ("" when none is configured).</summary>
        public string Path { get; set; } = string.Empty;
        public string PathType { get; set; } = string.Empty;
        /// <summary>The reference is served by an external server (cloud/Autodesk server): it cannot be edited as a local file.</summary>
        public bool IsServerResource { get; set; }
        public string ServerName { get; set; } = string.Empty;
        /// <summary>Revit's own load state of the table (Loaded, NotFound, LoadedFromCache…).</summary>
        public string LoadStatus { get; set; } = string.Empty;
        public bool IsLoaded { get; set; }
        /// <summary>Entries Revit has in memory now (may differ from the file).</summary>
        public List<KeynoteRecord> LoadedEntries { get; set; } = new List<KeynoteRecord>();
        public string ActiveViewName { get; set; } = string.Empty;
        public bool ActiveViewAcceptsTags { get; set; }
        public bool HasKeynoteTagType { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public enum TagSource { Unknown = 0, User, Element, Material }

    public sealed class ViewRef
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public override string ToString() => Name + (Count > 1 ? " (" + Count + ")" : "");
    }

    /// <summary>Where one key is used in the current project (only this document; other projects that share the file are not visible).</summary>
    public sealed class KeyUsage
    {
        public string Key { get; set; } = string.Empty;
        public int UserTags { get; set; }
        public int ElementTags { get; set; }
        public int MaterialTags { get; set; }
        public int Tags => UserTags + ElementTags + MaterialTags;
        public List<ViewRef> Views { get; } = new List<ViewRef>();
        /// <summary>Types/instances whose Keynote parameter is this key.</summary>
        public int ElementAssignments { get; set; }
        /// <summary>Materials whose Keynote parameter is this key.</summary>
        public int MaterialAssignments { get; set; }
        public int Assignments => ElementAssignments + MaterialAssignments;
        public bool InActiveView { get; set; }
        public bool IsUsed => Tags > 0 || Assignments > 0;
    }

    public sealed class UsageReport
    {
        /// <summary>False when the query failed: every count is unknown (never shown as zero).</summary>
        public bool Succeeded { get; set; }
        public string Error { get; set; } = string.Empty;
        public Dictionary<string, KeyUsage> ByKey { get; } = new Dictionary<string, KeyUsage>(StringComparer.Ordinal);
        public DateTime ReadUtc { get; set; }
        public KeyUsage Of(string key) => key != null && ByKey.TryGetValue(key, out var u) ? u : null;
    }

    public sealed class ReloadResult
    {
        public bool Succeeded { get; set; }
        /// <summary>Revit's ExternalResourceLoadStatus as text.</summary>
        public string Status { get; set; } = string.Empty;
        public List<string> Messages { get; } = new List<string>();
        /// <summary>Table entries as loaded after the reload (to verify against what was written).</summary>
        public List<KeynoteRecord> LoadedEntries { get; set; } = new List<KeynoteRecord>();
        public int UpdatedTags { get; set; }
        public int UpdatedAssignments { get; set; }
        public string Path { get; set; } = string.Empty;
    }

    public sealed class RekeyCheck
    {
        public bool CanApply { get; set; }
        public List<string> Lines { get; } = new List<string>();
        public List<string> Blockers { get; } = new List<string>();
    }

    public enum PlacementMode { User = 0, Element, Material }

    public sealed class PlacementResult
    {
        public string Key { get; set; } = string.Empty;
        public PlacementMode Mode { get; set; }
        public int TagsCreated { get; set; }
        public int TagsKeyed { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool Cancelled { get; set; }
    }

    /// <summary>
    /// Everything Keynote Manager asks of Revit. Implemented in CausaFix.Revit through the dispatcher (valid API
    /// context); always bound to the document the window was opened for. Nothing here saves or synchronises the model.
    /// </summary>
    public interface IKeynoteGateway
    {
        Task<KeynoteTableInfo> ReadTable();
        Task<UsageReport> ReadUsage();
        /// <summary>Reloads the configured file and applies <paramref name="renames"/> to tags/types/materials in the same undo step.</summary>
        Task<ReloadResult> Reload(IList<KeyRename> renames);
        /// <summary>Points the model's keynote table at <paramref name="path"/> (LoadFrom) and applies <paramref name="renames"/>.</summary>
        Task<ReloadResult> LinkFile(string path, IList<KeyRename> renames);
        Task<RekeyCheck> CheckRekey(IList<KeyRename> renames);
        /// <summary>Opens <paramref name="viewId"/> (or the best view) and selects the tags of <paramref name="key"/> there.</summary>
        Task<string> ShowTags(string key, long viewId);
        /// <summary>Starts Revit's native keynote command; the tags it creates get <paramref name="key"/>. Finishes via <see cref="PlacementFinished"/>.</summary>
        Task<string> StartPlacement(PlacementMode mode, string key);
        void CancelPlacement();
        bool IsPlacing { get; }
        event EventHandler<PlacementResult> PlacementFinished;
    }

    // ---------------------------------------------------------------- dialogs

    public sealed class RecordFormRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public bool KeyEditable { get; set; } = true;
        public string Text { get; set; } = string.Empty;
        public string ParentKey { get; set; } = string.Empty;
        public List<KeynoteRecord> ParentChoices { get; set; } = new List<KeynoteRecord>();
        public bool ParentEditable { get; set; } = true;
        public List<string> Templates { get; set; } = new List<string>();
        public string Hint { get; set; } = string.Empty;
        /// <summary>(key, text, parent) → error or null; the form stays open until valid.</summary>
        public Func<string, string, string, string> Validate { get; set; }
    }

    public sealed class RecordFormResult
    {
        public string Key { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string ParentKey { get; set; } = string.Empty;
    }

    public enum CloseChoice { Cancel = 0, Save, Discard }

    public interface IKeynoteDialogs
    {
        RecordFormResult EditRecord(RecordFormRequest request);
        /// <summary>Generic review: a list of lines; returns true only on the explicit confirm button.</summary>
        bool Review(string title, string intro, IList<string> lines, string confirmText, bool danger = false);
        /// <summary>Import/paste review: checkboxes per item, editable target keys when <paramref name="keysEditable"/>.</summary>
        bool ReviewIncoming(string title, string intro, IList<IncomingItem> items, bool keysEditable);
        /// <summary>Save conflict: a choice per conflicting key (mine/theirs); false = cancel.</summary>
        bool ResolveConflicts(MergeResult merge);
        CloseChoice AskSaveDiscardCancel(string message);
        bool Confirm(string title, string message);
        void Info(string title, string message);
        void Error(string title, string message);
        string AskText(string title, string prompt, string initial);
        string PickOpenFile(string title, string filter, string initialPath);
        string PickSaveFile(string title, string filter, string initialPath);
        void SetClipboard(string text);
        string GetClipboard();
        void ShowInFolder(string path);
    }
}
