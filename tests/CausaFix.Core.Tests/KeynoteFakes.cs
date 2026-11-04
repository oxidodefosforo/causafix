using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CausaFix.Keynotes.Editing;
using CausaFix.Keynotes.Execution;
using CausaFix.Keynotes.Files;
using CausaFix.Keynotes.Format;
using CausaFix.Keynotes.Model;
using CausaFix.Keynotes.ViewModels;

namespace CausaFix.Core.Tests
{
    /// <summary>A keynote tag in the simulated model: shows the text of its key in the loaded table (as Revit does).</summary>
    public sealed class FakeTag
    {
        public long Id;
        public string Key = "";
        public TagSource Source;
        public long ViewId;
    }

    /// <summary>
    /// Simulated Revit for Keynote Manager: a keynote table that loads the file from disk on Reload/LinkFile, tags whose
    /// displayed text comes from the loaded table, Keynote parameters on types/materials, failure switches.
    /// </summary>
    public sealed class FakeKeynoteGateway : IKeynoteGateway
    {
        public string TablePath = "";
        public bool Loaded;
        public List<KeynoteRecord> LoadedEntries = new List<KeynoteRecord>();
        public List<FakeTag> Tags = new List<FakeTag>();
        public Dictionary<string, string> TypeKeynotes = new Dictionary<string, string>();      // type name → key
        public Dictionary<string, string> MaterialKeynotes = new Dictionary<string, string>();  // material → key
        public long ActiveViewId = 100;
        public bool FailUsage, FailNextReload, BlockRekey, HasTagType = true, ViewAcceptsTags = true;
        public int ReloadCount;
        public List<string> Calls = new List<string>();
        private long _nextId = 1000;

        public string DisplayedText(FakeTag t) => LoadedEntries.FirstOrDefault(e => e.Key == t.Key)?.Text ?? "?";

        public Task<KeynoteTableInfo> ReadTable()
        {
            Calls.Add("ReadTable");
            return Task.FromResult(new KeynoteTableInfo
            {
                DocumentTitle = "Hospital", Path = TablePath, PathType = "Absolute", LoadStatus = Loaded ? "Loaded" : TablePath.Length == 0 ? "No configurado" : "NotLoaded",
                IsLoaded = Loaded, LoadedEntries = LoadedEntries.Select(e => e.Clone()).ToList(), ActiveViewName = "L01 LIGHTING",
                ActiveViewAcceptsTags = ViewAcceptsTags, HasKeynoteTagType = HasTagType
            });
        }

        public Task<UsageReport> ReadUsage()
        {
            Calls.Add("ReadUsage");
            var r = new UsageReport { Succeeded = !FailUsage, ReadUtc = DateTime.UtcNow };
            if (FailUsage) { r.Error = "simulated failure"; return Task.FromResult(r); }
            KeyUsage U(string k) { if (!r.ByKey.TryGetValue(k, out var u)) r.ByKey[k] = u = new KeyUsage { Key = k }; return u; }
            foreach (var t in Tags)
            {
                var u = U(t.Key);
                if (t.Source == TagSource.User) u.UserTags++; else if (t.Source == TagSource.Element) u.ElementTags++; else u.MaterialTags++;
                var v = u.Views.FirstOrDefault(x => x.Id == t.ViewId);
                if (v == null) u.Views.Add(v = new ViewRef { Id = t.ViewId, Name = "View " + t.ViewId });
                v.Count++;
                if (t.ViewId == ActiveViewId) u.InActiveView = true;
            }
            foreach (var k in TypeKeynotes.Values) U(k).ElementAssignments++;
            foreach (var k in MaterialKeynotes.Values) U(k).MaterialAssignments++;
            return Task.FromResult(r);
        }

        private ReloadResult LoadFromDisk(IList<KeyRename> renames)
        {
            ReloadCount++;
            var res = new ReloadResult { Path = TablePath };
            if (FailNextReload) { FailNextReload = false; res.Status = "Failure"; res.Messages.Add("Simulated: the file could not be read."); return res; }
            var doc = KeynoteFormat.Read(File.ReadAllBytes(TablePath));
            LoadedEntries = doc.Library.Records.Select(r => r.Clone()).ToList();
            Loaded = true;
            foreach (var rn in renames ?? new List<KeyRename>())
            {
                foreach (var t in Tags.Where(t => t.Key == rn.From)) { t.Key = rn.To; res.UpdatedTags++; }
                foreach (var k in TypeKeynotes.Keys.ToList()) if (TypeKeynotes[k] == rn.From) { TypeKeynotes[k] = rn.To; res.UpdatedAssignments++; }
                foreach (var k in MaterialKeynotes.Keys.ToList()) if (MaterialKeynotes[k] == rn.From) { MaterialKeynotes[k] = rn.To; res.UpdatedAssignments++; }
            }
            res.Succeeded = true; res.Status = "Success";
            res.LoadedEntries = LoadedEntries.Select(e => e.Clone()).ToList();
            return res;
        }

        public Task<ReloadResult> Reload(IList<KeyRename> renames) { Calls.Add("Reload"); return Task.FromResult(LoadFromDisk(renames)); }

        public Task<ReloadResult> LinkFile(string path, IList<KeyRename> renames)
        {
            Calls.Add("LinkFile");
            string old = TablePath;
            TablePath = path;
            var r = LoadFromDisk(renames);
            if (!r.Succeeded) TablePath = old;
            return Task.FromResult(r);
        }

        public Task<RekeyCheck> CheckRekey(IList<KeyRename> renames)
        {
            Calls.Add("CheckRekey");
            var c = new RekeyCheck { CanApply = !BlockRekey };
            if (BlockRekey) c.Blockers.Add("Simulated: 1 tag is owned by another user.");
            foreach (var rn in renames) c.Lines.Add(rn.From + " → " + rn.To + ": " + Tags.Count(t => t.Key == rn.From) + " etiqueta(s)");
            return Task.FromResult(c);
        }

        public Task<string> ShowTags(string key, long viewId) { Calls.Add("ShowTags " + key); return Task.FromResult("ok"); }

        public bool IsPlacing { get; private set; }
        public event EventHandler<PlacementResult> PlacementFinished;

        public Task<string> StartPlacement(PlacementMode mode, string key)
        {
            Calls.Add("Place " + mode + " " + key);
            if (!LoadedEntries.Any(e => e.Key == key)) return Task.FromResult("not loaded");
            // The user clicks once in the view: Revit creates the tag; the gateway writes the key into it.
            Tags.Add(new FakeTag { Id = _nextId++, Key = key, Source = mode == PlacementMode.User ? TagSource.User : mode == PlacementMode.Element ? TagSource.Element : TagSource.Material, ViewId = ActiveViewId });
            PlacementFinished?.Invoke(this, new PlacementResult { Key = key, Mode = mode, TagsCreated = 1, TagsKeyed = 1, Message = "1 etiqueta colocada con " + key });
            return Task.FromResult("Colocando " + key);
        }

        public void CancelPlacement() { IsPlacing = false; }
    }

    /// <summary>Scripted dialogs: every answer is a delegate or a queue so tests read like the user's clicks.</summary>
    public sealed class FakeKeynoteDialogs : IKeynoteDialogs
    {
        public Queue<Func<RecordFormRequest, RecordFormResult>> Forms = new Queue<Func<RecordFormRequest, RecordFormResult>>();
        public Queue<bool> Confirms = new Queue<bool>();
        public bool DefaultConfirm = true;
        public Func<string, IList<string>, bool> OnReview = (t, l) => true;
        public Func<IList<IncomingItem>, bool> OnIncoming = i => true;
        public Func<MergeResult, bool> OnConflicts = m => false;
        public CloseChoice CloseAnswer = CloseChoice.Cancel;
        public Queue<string> Texts = new Queue<string>();
        public Queue<string> Paths = new Queue<string>();
        public string Clipboard = "";
        public List<string> Errors = new List<string>(), Infos = new List<string>(), Reviews = new List<string>(), Log = new List<string>();
        public RecordFormRequest LastForm;

        public RecordFormResult EditRecord(RecordFormRequest r)
        {
            LastForm = r;
            if (Forms.Count == 0) return null;
            var res = Forms.Dequeue()(r);
            if (res != null && r.Validate != null)
            {
                string e = r.Validate(res.Key, res.Text, res.ParentKey);
                if (e != null) { Errors.Add("form: " + e); return null; }
            }
            return res;
        }

        public bool Review(string title, string intro, IList<string> lines, string confirmText, bool danger = false)
        {
            Reviews.Add(title + " | " + intro + " | " + string.Join(" / ", lines));
            return OnReview(title, lines);
        }

        public bool ReviewIncoming(string title, string intro, IList<IncomingItem> items, bool keysEditable) { Reviews.Add(title + " | " + intro); return OnIncoming(items); }
        public bool ResolveConflicts(MergeResult merge) => OnConflicts(merge);
        public CloseChoice AskSaveDiscardCancel(string message) { Log.Add("close: " + message); return CloseAnswer; }
        public bool Confirm(string title, string message) { Log.Add("confirm: " + title); return Confirms.Count > 0 ? Confirms.Dequeue() : DefaultConfirm; }
        public void Info(string title, string message) => Infos.Add(title + ": " + message);
        public void Error(string title, string message) => Errors.Add(title + ": " + message);
        public string AskText(string title, string prompt, string initial) => Texts.Count > 0 ? Texts.Dequeue() : null;
        public string PickOpenFile(string title, string filter, string initialPath) => Paths.Count > 0 ? Paths.Dequeue() : null;
        public string PickSaveFile(string title, string filter, string initialPath) => Paths.Count > 0 ? Paths.Dequeue() : null;
        public void SetClipboard(string text) => Clipboard = text;
        public string GetClipboard() => Clipboard;
        public void ShowInFolder(string path) => Log.Add("folder: " + path);

        public static Func<RecordFormRequest, RecordFormResult> Answer(string key, string text, string parent = null)
            => r => new RecordFormResult { Key = key ?? r.Key, Text = text, ParentKey = parent ?? r.ParentKey };
    }

    public sealed class KeynoteSandbox : IDisposable
    {
        public readonly string Root;
        public readonly KeynoteFileStore Store;
        public readonly FakeKeynoteGateway Gw = new FakeKeynoteGateway();
        public readonly FakeKeynoteDialogs Dlg = new FakeKeynoteDialogs();

        public KeynoteSandbox()
        {
            Root = Path.Combine(Path.GetTempPath(), "cfx-kn-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(Root);
            Store = new KeynoteFileStore(Path.Combine(Root, "_backups"), Path.Combine(Root, "_drafts"), Path.Combine(Root, "_cfg", "templates.json"));
        }

        public string P(string name) => Path.Combine(Root, name);

        public string Write(string name, string text, KeynoteEncoding enc = KeynoteEncoding.Utf16LeBom)
        {
            string p = P(name);
            File.WriteAllBytes(p, KeynoteFormat.Encode(text, enc, out _));
            return p;
        }

        public KeynoteManagerViewModel Vm()
        {
            var vm = new KeynoteManagerViewModel(Gw, Dlg, Store);
            vm.InitializeAsync().GetAwaiter().GetResult();
            return vm;
        }

        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Root, true);
            }
            catch { }
        }
    }
}
