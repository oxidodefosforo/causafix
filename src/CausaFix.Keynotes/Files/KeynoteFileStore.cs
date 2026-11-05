using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using CausaFix.Keynotes.Editing;
using CausaFix.Keynotes.Format;
using CausaFix.Keynotes.Model;

namespace CausaFix.Keynotes.Files
{
    /// <summary>What the file system says about a keynote file before reading or writing it.</summary>
    public sealed class FileAccessInfo
    {
        public string Path { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public bool CanWrite { get; set; }
        /// <summary>Why the file cannot be written (read-only attribute, locked, not hydrated, pyRevit lock, no permission…).</summary>
        public string ReadOnlyReason { get; set; } = string.Empty;
        /// <summary>Informational notes (cloud/ACC path, network path, placeholder that will be downloaded…).</summary>
        public List<string> Notes { get; } = new List<string>();
        public bool IsCloud { get; set; }
        public bool IsNetwork { get; set; }
        public bool IsPlaceholder { get; set; }
        public bool PyRevitLock { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }

    /// <summary>The file as read: bytes, parsed document and access state. Hash is the concurrency token for Save.</summary>
    public sealed class FileSnapshot
    {
        public string Path { get; set; } = string.Empty;
        public byte[] Bytes { get; set; } = new byte[0];
        public KeynoteDocument Document { get; set; } = new KeynoteDocument();
        public FileAccessInfo Access { get; set; } = new FileAccessInfo();
        public string Hash => Document.Hash;
    }

    public enum SaveOutcome { Saved = 0, Conflict, Failed }

    public sealed class SaveResult
    {
        public SaveOutcome Outcome { get; set; }
        public string Message { get; set; } = string.Empty;
        /// <summary>Hash of the bytes now on disk after a successful save.</summary>
        public string WrittenHash { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        /// <summary>Conflict: the file as it is now (someone else changed it since it was read).</summary>
        public FileSnapshot Theirs { get; set; }
        public KeynoteEncoding EncodingUsed { get; set; }
        public bool EncodingChanged { get; set; }
    }

    [DataContract]
    public sealed class DraftState
    {
        [DataMember(Order = 1)] public string Path { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string BaseHash { get; set; } = string.Empty;
        [DataMember(Order = 3)] public List<KeynoteRecord> Base { get; set; } = new List<KeynoteRecord>();
        [DataMember(Order = 4)] public List<KeynoteRecord> Current { get; set; } = new List<KeynoteRecord>();
        [DataMember(Order = 5)] public List<KeyRenameData> Renames { get; set; } = new List<KeyRenameData>();
        [DataMember(Order = 6)] public DateTime SavedUtc { get; set; }
    }

    [DataContract]
    public sealed class KeyRenameData
    {
        [DataMember(Order = 1)] public string From { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string To { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class TextTemplate
    {
        [DataMember(Order = 1)] public string Name { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string Text { get; set; } = string.Empty;
        public bool BuiltIn { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// Controlled access to keynote files. Save never overwrites blindly: the bytes on disk must still have the hash that
    /// was read (otherwise a conflict is returned with the current content for a merge); the previous file is copied to a
    /// backup, the new content is written to a temporary file in the same folder and swapped in, and the result is read
    /// back and verified. A failed save leaves the original in place. Restore after a failed Revit reload only happens
    /// when the file still has exactly the bytes this tool wrote.
    /// </summary>
    public sealed class KeynoteFileStore
    {
        private const FileAttributes Offline = (FileAttributes)0x1000;
        private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
        private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

        private readonly string _backupRoot, _draftRoot, _templatesFile;
        private readonly Action<string> _log;

        public KeynoteFileStore(string backupRoot, string draftRoot, string templatesFile, Action<string> log = null)
        {
            _backupRoot = backupRoot; _draftRoot = draftRoot; _templatesFile = templatesFile; _log = log ?? (_ => { });
        }

        public string BackupRoot => _backupRoot;

        // ---------- access ----------

        public static bool IsCloudPath(string path)
        {
            string p = (path ?? "").Replace('/', '\\');
            return p.IndexOf("\\DC\\ACCDocs\\", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("\\ACCDocs\\", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("\\BIM 360\\", StringComparison.OrdinalIgnoreCase) >= 0 || p.StartsWith("autodesk", StringComparison.OrdinalIgnoreCase)
                || p.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("\\SharePoint\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsNetworkPath(string path) => (path ?? "").StartsWith("\\\\", StringComparison.Ordinal) || (path ?? "").StartsWith("//", StringComparison.Ordinal);

        /// <summary>pyRevit's DeffrelDB lock: a sidecar "&lt;name&gt;.lock" next to the file (never touched here).</summary>
        public static string PyRevitLockPath(string path) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", System.IO.Path.GetFileNameWithoutExtension(path) + ".lock");

        public FileAccessInfo Probe(string path, bool testWrite = true)
        {
            var a = new FileAccessInfo { Path = path ?? "" };
            if (string.IsNullOrWhiteSpace(path)) { a.ReadOnlyReason = "No path."; return a; }
            a.IsCloud = IsCloudPath(path);
            a.IsNetwork = IsNetworkPath(path);
            if (a.IsCloud) a.Notes.Add("Cloud location (ACC/Desktop Connector or another sync client): the file must be downloaded and synced. External changes are checked on save.");
            if (a.IsNetwork) a.Notes.Add("Network location: external changes are checked on save.");
            try
            {
                var fi = new FileInfo(path);
                a.Exists = fi.Exists;
                if (!a.Exists)
                {
                    a.ReadOnlyReason = Directory.Exists(fi.DirectoryName ?? "") ? "The file does not exist." : "The file's folder cannot be found (disconnected drive or not synced?).";
                    return a;
                }
                a.LastWriteUtc = fi.LastWriteTimeUtc;
                var attr = fi.Attributes;
                if ((attr & (Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
                {
                    a.IsPlaceholder = true;
                    a.Notes.Add("The file is an online-only placeholder: reading it downloads it, and reading fails while offline.");
                }
                if (File.Exists(PyRevitLockPath(path)))
                {
                    a.PyRevitLock = true;
                    a.ReadOnlyReason = "pyRevit has an active lock (" + System.IO.Path.GetFileName(PyRevitLockPath(path)) + "): someone is editing it. The lock is not touched.";
                    return a;
                }
                if ((attr & FileAttributes.ReadOnly) != 0) { a.ReadOnlyReason = "The file has the read-only attribute."; return a; }
                if (testWrite)
                {
                    // Opening for write without truncating does not change the file; it fails if it is locked or forbidden.
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                }
                a.CanWrite = true;
            }
            catch (UnauthorizedAccessException) { a.ReadOnlyReason = "No write permission on the file or folder."; }
            catch (IOException ex) { a.ReadOnlyReason = "The file is locked or unavailable: " + ex.Message; }
            catch (Exception ex) { a.ReadOnlyReason = "The file could not be checked: " + ex.Message; }
            return a;
        }

        public FileSnapshot Read(string path)
        {
            var access = Probe(path);
            if (!access.Exists) throw new FileNotFoundException(access.ReadOnlyReason, path);
            byte[] bytes = ReadAllShared(path);
            return new FileSnapshot { Path = path, Bytes = bytes, Document = KeynoteFormat.Read(bytes), Access = access };
        }

        private static byte[] ReadAllShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public string CurrentHash(string path) => File.Exists(path) ? KeynoteFormat.Sha256(ReadAllShared(path)) : string.Empty;

        // ---------- write ----------

        /// <summary>Creates a new file (fails if it exists). New files are UTF-16 LE with BOM and CRLF, as Revit expects.</summary>
        public SaveResult CreateNew(string path, KeynoteLibrary lib)
        {
            if (File.Exists(path)) return new SaveResult { Outcome = SaveOutcome.Failed, Message = "A file already exists at that path: choose another name or open it." };
            byte[] bytes = KeynoteFormat.Encode(KeynoteFormat.Compose(lib), KeynoteEncoding.Utf16LeBom, out var used);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
                using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
                string h = CurrentHash(path);
                return h == KeynoteFormat.Sha256(bytes)
                    ? new SaveResult { Outcome = SaveOutcome.Saved, WrittenHash = h, EncodingUsed = used, Message = "File created." }
                    : new SaveResult { Outcome = SaveOutcome.Failed, Message = "The created file does not match when read back." };
            }
            catch (Exception ex) { return new SaveResult { Outcome = SaveOutcome.Failed, Message = "The file could not be created: " + ex.Message }; }
        }

        /// <summary>
        /// Writes <paramref name="bytes"/> over <paramref name="path"/> only if the file still has <paramref name="expectedHash"/>
        /// (empty = the file must not exist, i.e. Save As to a new path).
        /// </summary>
        public SaveResult Save(string path, byte[] bytes, string expectedHash)
        {
            var access = Probe(path, testWrite: File.Exists(path));
            bool exists = access.Exists;
            if (exists && !access.CanWrite) return new SaveResult { Outcome = SaveOutcome.Failed, Message = "Cannot write: " + access.ReadOnlyReason };
            byte[] now = null;
            if (exists)
            {
                try { now = ReadAllShared(path); }
                catch (Exception ex) { return new SaveResult { Outcome = SaveOutcome.Failed, Message = "The file could not be read before saving: " + ex.Message }; }
                string nowHash = KeynoteFormat.Sha256(now);
                if (!string.Equals(nowHash, expectedHash ?? "", StringComparison.OrdinalIgnoreCase))
                    return new SaveResult { Outcome = SaveOutcome.Conflict, Message = "The file changed since it was read (another user, pyRevit or sync).", Theirs = new FileSnapshot { Path = path, Bytes = now, Document = KeynoteFormat.Read(now), Access = access } };
            }
            else if (!string.IsNullOrEmpty(expectedHash))
                return new SaveResult { Outcome = SaveOutcome.Conflict, Message = "The file is no longer at its path (moved or deleted).", Theirs = null };

            var res = new SaveResult();
            if (exists)
            {
                try { res.BackupPath = Backup(path, now); }
                catch (Exception ex) { return new SaveResult { Outcome = SaveOutcome.Failed, Message = "The backup could not be created; nothing was saved: " + ex.Message }; }
            }
            string err = WriteAtomic(path, bytes, exists);
            if (err != null) { res.Outcome = SaveOutcome.Failed; res.Message = err; return res; }
            string written = CurrentHash(path);
            if (written != KeynoteFormat.Sha256(bytes))
            {
                res.Outcome = SaveOutcome.Failed;
                res.Message = "The content read back after saving does not match what was written." + (res.BackupPath.Length > 0 ? " Previous copy: " + res.BackupPath : "");
                return res;
            }
            res.Outcome = SaveOutcome.Saved; res.WrittenHash = written; res.Message = "File saved.";
            _log("Keynotes: saved " + path + " (" + bytes.Length + " bytes, backup " + res.BackupPath + ")");
            return res;
        }

        private string WriteAtomic(string path, byte[] bytes, bool exists)
        {
            string dir = System.IO.Path.GetDirectoryName(path) ?? ".";
            string tmp = System.IO.Path.Combine(dir, "." + System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".causafix.tmp");
            try
            {
                Directory.CreateDirectory(dir);
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
                if (KeynoteFormat.Sha256(ReadAllShared(tmp)) != KeynoteFormat.Sha256(bytes)) throw new IOException("the temporary file does not match");
                if (!exists) { File.Move(tmp, path); return null; }
                try { File.Replace(tmp, path, null, true); }
                catch (Exception ex) when (ex is PlatformNotSupportedException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Some network shares / sync folders do not support ReplaceFile: overwrite in place (backup already exists).
                    _log("Keynotes: File.Replace failed (" + ex.Message + "), copying over");
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
                return null;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return "The file could not be written (the original was not changed): " + ex.Message;
            }
        }

        private string Backup(string path, byte[] bytes)
        {
            Directory.CreateDirectory(_backupRoot);
            string name = System.IO.Path.GetFileNameWithoutExtension(path) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + System.IO.Path.GetExtension(path);
            string b = System.IO.Path.Combine(_backupRoot, name);
            File.WriteAllBytes(b, bytes);
            PruneBackups(System.IO.Path.GetFileNameWithoutExtension(path), 30);
            return b;
        }

        private void PruneBackups(string stem, int keep)
        {
            try
            {
                foreach (var f in new DirectoryInfo(_backupRoot).GetFiles(stem + "_*").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(keep)) f.Delete();
            }
            catch { }
        }

        /// <summary>Puts <paramref name="original"/> back only if the file still has exactly the bytes this tool wrote.</summary>
        public SaveResult RestoreIfUnchanged(string path, string writtenHash, byte[] original)
        {
            string now = CurrentHash(path);
            if (!string.Equals(now, writtenHash, StringComparison.OrdinalIgnoreCase))
                return new SaveResult { Outcome = SaveOutcome.Conflict, Message = "The file changed after it was saved: it is not restored (that would lose someone else's changes)." };
            return Save(path, original, writtenHash);
        }

        // ---------- drafts ----------

        public static string DraftKey(string path)
        {
            string norm = (path ?? "").Trim().Replace('/', '\\').ToUpperInvariant();
            return KeynoteFormat.Sha256(Encoding.UTF8.GetBytes(norm)).Substring(0, 24);
        }

        private string DraftPath(string path) => System.IO.Path.Combine(_draftRoot, DraftKey(path) + ".json");

        public void SaveDraft(string path, KeynoteDraft draft, string baseHash)
        {
            try
            {
                Directory.CreateDirectory(_draftRoot);
                var st = new DraftState
                {
                    Path = path, BaseHash = baseHash, SavedUtc = DateTime.UtcNow,
                    Base = draft.Base.Records.Select(r => r.Clone()).ToList(), Current = draft.Current.Records.Select(r => r.Clone()).ToList(),
                    Renames = draft.Renames.Select(r => new KeyRenameData { From = r.From, To = r.To }).ToList()
                };
                File.WriteAllBytes(DraftPath(path), ToJson(st));
            }
            catch (Exception ex) { _log("Keynotes: draft not saved: " + ex.Message); }
        }

        public DraftState LoadDraft(string path)
        {
            try
            {
                string p = DraftPath(path);
                return File.Exists(p) ? FromJson<DraftState>(File.ReadAllBytes(p)) : null;
            }
            catch (Exception ex) { _log("Keynotes: draft unreadable: " + ex.Message); return null; }
        }

        public void DeleteDraft(string path)
        {
            try { string p = DraftPath(path); if (File.Exists(p)) File.Delete(p); } catch { }
        }

        // ---------- templates ----------

        public static IEnumerable<TextTemplate> BuiltInTemplates()
        {
            yield return new TextTemplate { Name = "RESERVED", Text = "RESERVED", BuiltIn = true };
            yield return new TextTemplate { Name = "DO NOT USE", Text = "DO NOT USE", BuiltIn = true };
        }

        public List<TextTemplate> LoadTemplates()
        {
            var list = BuiltInTemplates().ToList();
            try
            {
                if (File.Exists(_templatesFile)) list.AddRange(FromJson<List<TextTemplate>>(File.ReadAllBytes(_templatesFile)) ?? new List<TextTemplate>());
            }
            catch (Exception ex) { _log("Keynotes: templates unreadable: " + ex.Message); }
            return list;
        }

        public void SaveTemplates(IEnumerable<TextTemplate> user)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_templatesFile) ?? ".");
            File.WriteAllBytes(_templatesFile, ToJson(user.Where(t => !t.BuiltIn).ToList()));
        }

        private static byte[] ToJson<T>(T value)
        {
            using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value); return ms.ToArray(); }
        }

        private static T FromJson<T>(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }
    }
}
