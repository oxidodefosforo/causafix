using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.Keynotes.Model;

namespace CausaFix.Keynotes.Editing
{
    public enum IncomingStatus
    {
        /// <summary>The key does not exist in the draft: it can be added.</summary>
        New = 0,
        /// <summary>Same key, same text and parent: nothing to do.</summary>
        Identical,
        /// <summary>Same key with another text or parent: the draft is kept unless the user takes the incoming one.</summary>
        Conflict
    }

    public sealed class IncomingItem
    {
        public KeynoteRecord Incoming { get; set; }
        public KeynoteRecord Existing { get; set; }
        public IncomingStatus Status { get; set; }
        /// <summary>New: add it. Conflict: replace the draft's text/parent with the incoming one (explicit choice).</summary>
        public bool Take { get; set; }
        /// <summary>Paste: key the record gets (editable; must be free).</summary>
        public string TargetKey { get; set; } = string.Empty;
        public string TargetParent { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Import of a keynote file (native TXT or pyRevit export) and paste of a branch, always reviewed before applying.</summary>
    public static class KeynoteImport
    {
        /// <summary>Compares incoming records with the draft by key. Nothing is chosen silently for a conflicting key.</summary>
        public static List<IncomingItem> PlanImport(KeynoteDraft draft, KeynoteLibrary incoming)
        {
            var list = new List<IncomingItem>();
            var dup = new HashSet<string>(incoming.Records.GroupBy(r => r.Key).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
            foreach (var r in incoming.Records)
            {
                var mine = draft.Current.Find(r.Key);
                var item = new IncomingItem { Incoming = r.Clone(), Existing = mine?.Clone(), TargetKey = r.Key, TargetParent = r.ParentKey };
                if (dup.Contains(r.Key)) { item.Status = IncomingStatus.Conflict; item.Take = false; item.Note = "La clave aparece varias veces en el archivo importado: no se elige ninguna."; }
                else if (mine == null) { item.Status = IncomingStatus.New; item.Take = true; }
                else if (mine.SameAs(r)) { item.Status = IncomingStatus.Identical; item.Take = false; }
                else
                {
                    item.Status = IncomingStatus.Conflict; item.Take = false;
                    item.Note = (mine.Text != r.Text ? "texto distinto" : "") + ((mine.ParentKey ?? "") != (r.ParentKey ?? "") ? (mine.Text != r.Text ? " y " : "") + "padre distinto" : "") + ": se conserva el del borrador salvo que elijas el importado.";
                }
                list.Add(item);
            }
            return list;
        }

        /// <summary>Applies the chosen items atomically: on any error the draft is not changed.</summary>
        public static string ApplyImport(KeynoteDraft draft, IList<IncomingItem> items)
        {
            if (items.Any(i => i.Take && i.Note.StartsWith("La clave aparece varias veces", StringComparison.Ordinal))) return "Una clave repetida en el archivo importado no se puede tomar: corrige el archivo.";
            var lib = draft.Current.Clone();
            foreach (var i in items.Where(i => i.Take))
            {
                if (i.Status == IncomingStatus.New) lib.Records.Add(i.Incoming.Clone());
                else if (i.Status == IncomingStatus.Conflict)
                {
                    var r = lib.Find(i.Incoming.Key);
                    r.Text = i.Incoming.Text; r.ParentKey = i.Incoming.ParentKey;
                }
            }
            var issues = lib.Validate();
            if (issues.Count > 0) return "La importación dejaría la biblioteca inválida: " + string.Join(" ", issues.Take(5).Select(x => x.Message)) + " Marca también los padres necesarios.";
            draft.Replace(lib, draft.Renames.ToList());
            return null;
        }

        /// <summary>Paste of a branch under <paramref name="targetParent"/>: keys that exist get a suggested free key; the parents inside the branch follow.</summary>
        public static List<IncomingItem> PlanPaste(KeynoteDraft draft, IList<KeynoteRecord> branch, string targetParent)
        {
            var keys = new HashSet<string>(branch.Select(b => b.Key), StringComparer.Ordinal);
            var reserved = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<IncomingItem>();
            foreach (var r in branch)
            {
                string target = draft.Current.Contains(r.Key) || reserved.Contains(r.Key) ? draft.SuggestKey(r.Key, reserved) : r.Key;
                reserved.Add(target);
                list.Add(new IncomingItem
                {
                    Incoming = r.Clone(), Status = draft.Current.Contains(r.Key) ? IncomingStatus.Conflict : IncomingStatus.New, Take = true, TargetKey = target,
                    TargetParent = keys.Contains(r.ParentKey) ? r.ParentKey : (targetParent ?? ""),
                    Note = target != r.Key ? "La clave ya existe: se propone " + target + "." : ""
                });
            }
            return list;
        }

        public static string ApplyPaste(KeynoteDraft draft, IList<IncomingItem> items)
        {
            var map = items.Where(i => i.Take).ToDictionary(i => i.Incoming.Key, i => (i.TargetKey ?? "").Trim(), StringComparer.Ordinal);
            var lib = draft.Current.Clone();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in items.Where(i => i.Take))
            {
                string k = map[i.Incoming.Key];
                string e = KeynoteDraft.ValidateKey(k);
                if (e != null) return k + ": " + e;
                if (lib.Contains(k) || !seen.Add(k)) return "La clave '" + k + "' ya existe: cámbiala en la lista.";
                string parent = map.TryGetValue(i.TargetParent ?? "", out string mapped) ? mapped : i.TargetParent ?? "";
                lib.Records.Add(new KeynoteRecord { Key = k, Text = i.Incoming.Text, ParentKey = parent });
            }
            var issues = lib.Validate();
            if (issues.Count > 0) return "El pegado dejaría la biblioteca inválida: " + string.Join(" ", issues.Take(5).Select(x => x.Message));
            draft.Replace(lib, draft.Renames.ToList());
            return null;
        }
    }

    public enum ConflictChoice { Unresolved = 0, Mine, Theirs }

    public sealed class MergeConflict
    {
        public string Key { get; set; } = string.Empty;
        public KeynoteRecord Base { get; set; }
        public KeynoteRecord Mine { get; set; }
        public KeynoteRecord Theirs { get; set; }
        public ConflictChoice Choice { get; set; }
        public static string Show(KeynoteRecord r) => r == null ? "(eliminada)" : r.Text + (r.IsRoot ? "" : "  [padre " + r.ParentKey + "]");
    }

    public sealed class MergeResult
    {
        public KeynoteLibrary Merged { get; set; }
        public List<MergeConflict> Conflicts { get; } = new List<MergeConflict>();
        /// <summary>Changes made by someone else since this draft was read (applied when they do not collide with ours).</summary>
        public List<string> TheirChanges { get; } = new List<string>();
        public List<KeyRename> Renames { get; } = new List<KeyRename>();
    }

    /// <summary>
    /// Three-way merge used by Save when the file changed since it was read: base (as read), mine (draft), theirs (file now).
    /// A key changed only on one side takes that change; a key changed differently on both sides is a conflict that the
    /// user resolves explicitly ("last save wins" never happens). A rename in the draft counts as removing the old key.
    /// </summary>
    public static class KeynoteMerge
    {
        public static MergeResult Merge(KeynoteLibrary b, KeynoteLibrary mine, KeynoteLibrary theirs, IEnumerable<KeyRename> renames = null)
        {
            var res = new MergeResult();
            var rn = (renames ?? Enumerable.Empty<KeyRename>()).ToList();
            var B = b.Records.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var M = mine.Records.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var T = theirs.Records.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            bool Same(KeynoteRecord x, KeynoteRecord y) => x == null ? y == null : x.SameAs(y);
            KeynoteRecord Get(Dictionary<string, KeynoteRecord> d, string k) => d.TryGetValue(k, out var r) ? r : null;

            var order = theirs.Records.Select(r => r.Key).Concat(mine.Records.Select(r => r.Key)).Concat(b.Records.Select(r => r.Key)).Distinct().ToList();
            var chosen = new Dictionary<string, KeynoteRecord>(StringComparer.Ordinal);
            foreach (var k in order)
            {
                var bb = Get(B, k); var mm = Get(M, k); var tt = Get(T, k);
                if (!Same(tt, bb)) res.TheirChanges.Add(tt == null ? "Eliminada por otro usuario: " + k : bb == null ? "Añadida por otro usuario: " + k : "Modificada por otro usuario: " + k);
                if (Same(mm, bb)) chosen[k] = tt;
                else if (Same(tt, bb)) chosen[k] = mm;
                else if (Same(mm, tt)) chosen[k] = mm;
                else res.Conflicts.Add(new MergeConflict { Key = k, Base = bb?.Clone(), Mine = mm?.Clone(), Theirs = tt?.Clone() });
            }
            res.Merged = new KeynoteLibrary(order.Where(k => chosen.TryGetValue(k, out var r) && r != null).Select(k => chosen[k]));
            res.Renames.AddRange(rn.Where(r => res.Merged.Contains(r.To) && !res.Merged.Contains(r.From)));
            return res;
        }

        /// <summary>The merged library with every conflict resolved by its choice (all must be resolved).</summary>
        public static KeynoteLibrary Resolve(MergeResult m, out string error)
        {
            error = null;
            if (m.Conflicts.Any(c => c.Choice == ConflictChoice.Unresolved)) { error = "Hay conflictos sin resolver."; return null; }
            var lib = m.Merged.Clone();
            foreach (var c in m.Conflicts)
            {
                var r = c.Choice == ConflictChoice.Mine ? c.Mine : c.Theirs;
                if (r != null) lib.Records.Add(r.Clone());
            }
            var issues = lib.Validate();
            if (issues.Count > 0) error = "El resultado combinado no es válido: " + string.Join(" ", issues.Take(5).Select(x => x.Message));
            return lib;
        }
    }
}
