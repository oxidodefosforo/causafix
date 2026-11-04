using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.Keynotes.Model;

namespace CausaFix.Keynotes.Editing
{
    public enum ChangeKind { Added, Modified, Removed, Rekeyed }

    public sealed class RecordChange
    {
        public ChangeKind Kind { get; set; }
        public string Key { get; set; } = string.Empty;
        public string OldKey { get; set; } = string.Empty;
        public KeynoteRecord Before { get; set; }
        public KeynoteRecord After { get; set; }
        public string Describe()
        {
            switch (Kind)
            {
                case ChangeKind.Added: return "Nueva: " + Key + " – " + After?.Text;
                case ChangeKind.Removed: return "Eliminada: " + Key + " – " + Before?.Text;
                case ChangeKind.Rekeyed: return "Re-key: " + OldKey + " → " + Key + (Before != null && After != null && Before.Text != After.Text ? " (texto también cambiado)" : "");
                default:
                    var parts = new List<string>();
                    if (Before?.Text != After?.Text) parts.Add("texto");
                    if ((Before?.ParentKey ?? "") != (After?.ParentKey ?? "")) parts.Add("padre " + (string.IsNullOrEmpty(Before?.ParentKey) ? "(raíz)" : Before.ParentKey) + " → " + (string.IsNullOrEmpty(After?.ParentKey) ? "(raíz)" : After.ParentKey));
                    return "Modificada: " + Key + " (" + string.Join(", ", parts) + ")";
            }
        }
    }

    /// <summary>A key renamed in the draft: the model's references (tags, types, materials) must follow it on Save &amp; Reload.</summary>
    public sealed class KeyRename
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    /// <summary>
    /// Editable copy of a keynote library. Every operation validates first and changes nothing on error (returns the
    /// message). Organising (parent, Indent/Outdent, drag-and-drop) never changes keys; changing a key is the explicit
    /// <see cref="Rekey"/>, recorded so the model's references can be updated with the file.
    /// </summary>
    public sealed class KeynoteDraft
    {
        public KeynoteDraft(KeynoteLibrary baseline)
        {
            Base = (baseline ?? new KeynoteLibrary()).Clone();
            Current = Base.Clone();
        }

        /// <summary>The library as last read from (or written to) the file.</summary>
        public KeynoteLibrary Base { get; private set; }
        public KeynoteLibrary Current { get; private set; }
        private readonly List<KeyRename> _renames = new List<KeyRename>();
        /// <summary>Base keys renamed in the draft (composed: A→B then B→C is A→C).</summary>
        public IReadOnlyList<KeyRename> Renames => _renames;
        public event EventHandler Changed;

        public bool IsDirty => Changes().Count > 0;

        public void ResetTo(KeynoteLibrary baseline)
        {
            Base = baseline.Clone();
            Current = Base.Clone();
            _renames.Clear();
            Raise();
        }

        /// <summary>Accepts <paramref name="library"/> as the current draft against an unchanged base (merge result, recovery).</summary>
        public void Replace(KeynoteLibrary library, IEnumerable<KeyRename> renames = null)
        {
            Current = library.Clone();
            _renames.Clear();
            if (renames != null) _renames.AddRange(renames.Where(r => Base.Contains(r.From) && Current.Contains(r.To)));
            Raise();
        }

        /// <summary>The draft was written: it becomes the new base.</summary>
        public void MarkSaved(KeynoteLibrary written)
        {
            Base = written.Clone();
            Current = written.Clone();
            _renames.Clear();
            Raise();
        }

        private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public string OriginalKeyOf(string key) => _renames.FirstOrDefault(r => r.To == key)?.From ?? key;

        public List<RecordChange> Changes()
        {
            var list = new List<RecordChange>();
            var renamedFrom = new HashSet<string>(_renames.Select(r => r.From), StringComparer.Ordinal);
            var renamedTo = _renames.ToDictionary(r => r.To, r => r.From, StringComparer.Ordinal);
            foreach (var r in Current.Records)
            {
                if (renamedTo.TryGetValue(r.Key, out string from))
                {
                    list.Add(new RecordChange { Kind = ChangeKind.Rekeyed, Key = r.Key, OldKey = from, Before = Base.Find(from), After = r });
                    continue;
                }
                var b = Base.Find(r.Key);
                if (b == null || renamedFrom.Contains(r.Key)) list.Add(new RecordChange { Kind = ChangeKind.Added, Key = r.Key, After = r });
                else if (!b.SameAs(r)) list.Add(new RecordChange { Kind = ChangeKind.Modified, Key = r.Key, Before = b, After = r });
            }
            foreach (var b in Base.Records)
            {
                if (renamedFrom.Contains(b.Key)) continue;                       // it lives on under its new key
                bool replaced = renamedTo.ContainsKey(b.Key);                     // its key now belongs to a renamed record
                if (!Current.Contains(b.Key) || replaced)
                    list.Add(new RecordChange { Kind = ChangeKind.Removed, Key = b.Key, Before = b });
            }
            return list;
        }

        public RecordChange ChangeOf(string key) => Changes().FirstOrDefault(c => c.Key == key);

        // ------------------------------------------------------------------ validation helpers

        public static string ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "La clave no puede estar vacía.";
            if (key != key.Trim()) return "La clave no puede empezar ni terminar con espacios.";
            string bad = KeynoteText.InvalidCharacters(key);
            return bad == null ? null : "La clave no puede contener " + bad + ".";
        }

        public string ValidateNewKey(string key, string except = null)
        {
            string e = ValidateKey(key);
            if (e != null) return e;
            if (key != except && Current.Contains(key)) return "La clave '" + key + "' ya existe.";
            return null;
        }

        private string ValidateParent(string key, string parent)
        {
            if (string.IsNullOrEmpty(parent)) return null;
            if (!Current.Contains(parent)) return "El padre '" + parent + "' no existe.";
            if (parent == key) return "Una nota no puede ser su propio padre.";
            if (key != null && Current.IsDescendant(parent, key)) return "'" + parent + "' está dentro de '" + key + "': se crearía un ciclo.";
            return null;
        }

        // ------------------------------------------------------------------ operations

        /// <summary>Adds a record (group, subgroup or keynote: the same native record with or without parent).</summary>
        public string Add(string key, string text, string parentKey)
        {
            key = (key ?? "").Trim();
            string e = ValidateNewKey(key) ?? ValidateParent(key, parentKey);
            if (e != null) return e;
            string t = KeynoteText.Normalize(text);
            if (t.Length == 0) return "El texto no puede estar vacío.";
            Current.Records.Add(new KeynoteRecord { Key = key, Text = t, ParentKey = parentKey ?? "" });
            Raise();
            return null;
        }

        /// <summary>Changes text and/or parent. The key and the model's references are kept.</summary>
        public string Edit(string key, string text, string parentKey)
        {
            var r = Current.Find(key);
            if (r == null) return "La nota '" + key + "' no existe.";
            string t = KeynoteText.Normalize(text);
            if (t.Length == 0) return "El texto no puede estar vacío.";
            string e = ValidateParent(key, parentKey ?? "");
            if (e != null) return e;
            if (r.Text == t && (r.ParentKey ?? "") == (parentKey ?? "")) return null;
            r.Text = t;
            r.ParentKey = parentKey ?? "";
            Raise();
            return null;
        }

        public string SetParent(string key, string parentKey)
        {
            var r = Current.Find(key);
            if (r == null) return "La nota '" + key + "' no existe.";
            return Edit(key, r.Text, parentKey);
        }

        /// <summary>Indent: the previous sibling (natural order) becomes the parent.</summary>
        public string Indent(string key)
        {
            var r = Current.Find(key);
            if (r == null) return "La nota '" + key + "' no existe.";
            var siblings = Current.ChildrenOf(r.ParentKey).OrderBy(x => x.Key, NaturalComparer.Instance).ToList();
            int i = siblings.FindIndex(x => x.Key == key);
            if (i <= 0) return "No hay una nota anterior al mismo nivel bajo la que mover '" + key + "'.";
            return SetParent(key, siblings[i - 1].Key);
        }

        /// <summary>Outdent: the parent's parent becomes the parent (a child of a root becomes a root).</summary>
        public string Outdent(string key)
        {
            var r = Current.Find(key);
            if (r == null) return "La nota '" + key + "' no existe.";
            if (r.IsRoot) return "'" + key + "' ya está en el primer nivel.";
            var p = Current.Find(r.ParentKey);
            return SetParent(key, p?.ParentKey ?? "");
        }

        /// <summary>Copies one record under the same parent with a new key (children are not copied; use copy/paste for a branch).</summary>
        public string Duplicate(string key, string newKey)
        {
            var r = Current.Find(key);
            if (r == null) return "La nota '" + key + "' no existe.";
            return Add(newKey, r.Text, r.ParentKey);
        }

        /// <summary>A free key derived from <paramref name="key"/> (E-001 → E-002 … or KEY-COPY, KEY-COPY2…).</summary>
        public string SuggestKey(string key, ISet<string> reserved = null)
        {
            bool Free(string k) => !Current.Contains(k) && (reserved == null || !reserved.Contains(k));
            int end = key.Length;
            while (end > 0 && char.IsDigit(key[end - 1])) end--;
            if (end < key.Length)
            {
                string prefix = key.Substring(0, end), digits = key.Substring(end);
                long n = long.Parse(digits);
                for (long i = n + 1; i < n + 10000; i++)
                {
                    string candidate = prefix + i.ToString().PadLeft(digits.Length, '0');
                    if (Free(candidate)) return candidate;
                }
            }
            string c = key + "-COPY";
            for (int i = 2; !Free(c); i++) c = key + "-COPY" + i;
            return c;
        }

        /// <summary>Removes a record; a record with children needs <paramref name="branch"/> (removes them too).</summary>
        public string Delete(string key, bool branch)
        {
            var r = Current.Find(key);
            if (r == null) return "La nota '" + key + "' no existe.";
            var items = Current.Branch(key);
            if (items.Count > 1 && !branch) return "'" + key + "' tiene " + (items.Count - 1) + " nota(s) dentro: elimina la rama completa o muévelas antes.";
            var gone = new HashSet<string>(items.Select(x => x.Key), StringComparer.Ordinal);
            Current.Records.RemoveAll(x => gone.Contains(x.Key));
            _renames.RemoveAll(x => gone.Contains(x.To));
            Raise();
            return null;
        }

        /// <summary>
        /// Explicit change of key: the record and its children's ParentKey follow. When the old key came from the file,
        /// the rename is recorded so the model's references (tags, types, materials) are updated on Save &amp; Reload.
        /// </summary>
        public string Rekey(string oldKey, string newKey)
        {
            newKey = (newKey ?? "").Trim();
            var r = Current.Find(oldKey);
            if (r == null) return "La nota '" + oldKey + "' no existe.";
            if (newKey == oldKey) return "La clave nueva es igual a la actual.";
            string e = ValidateNewKey(newKey);
            if (e != null) return e;
            r.Key = newKey;
            foreach (var c in Current.Records.Where(x => x.ParentKey == oldKey)) c.ParentKey = newKey;
            var existing = _renames.FirstOrDefault(x => x.To == oldKey);
            if (existing != null)
            {
                existing.To = newKey;
                if (existing.From == newKey) _renames.Remove(existing);      // renamed back
            }
            else if (Base.Contains(oldKey)) _renames.Add(new KeyRename { From = oldKey, To = newKey });
            Raise();
            return null;
        }

        /// <summary>Applies a text transform to the given keys; returns the (key, before, after) changes it made.</summary>
        public List<(string key, string before, string after)> Transform(IEnumerable<string> keys, Func<string, string> f)
        {
            var changes = new List<(string, string, string)>();
            foreach (var k in keys)
            {
                var r = Current.Find(k);
                if (r == null) continue;
                string t = KeynoteText.Normalize(f(r.Text));
                if (t.Length == 0 || t == r.Text) continue;
                changes.Add((k, r.Text, t));
                r.Text = t;
            }
            if (changes.Count > 0) Raise();
            return changes;
        }

        /// <summary>Discards the draft change of one key (back to the file's value).</summary>
        public string Revert(string key)
        {
            var ch = ChangeOf(key);
            if (ch == null) return null;
            switch (ch.Kind)
            {
                case ChangeKind.Added:
                    if (Current.Records.Any(x => x.ParentKey == key)) return "'" + key + "' tiene notas nuevas dentro: elimínalas o revierte la rama.";
                    Current.Records.RemoveAll(x => x.Key == key);
                    break;
                case ChangeKind.Modified:
                    var r = Current.Find(key);
                    if (!string.IsNullOrEmpty(ch.Before.ParentKey) && !Current.Contains(ch.Before.ParentKey)) return "Su padre original '" + ch.Before.ParentKey + "' ya no existe en el borrador.";
                    r.Text = ch.Before.Text; r.ParentKey = ch.Before.ParentKey;
                    break;
                case ChangeKind.Rekeyed:
                    if (Current.Contains(ch.OldKey)) return "La clave original '" + ch.OldKey + "' está ocupada en el borrador.";
                    return Rekey(key, ch.OldKey) ?? RevertText(ch.OldKey, ch.Before);
                case ChangeKind.Removed:
                    return "Para recuperar una nota eliminada usa «Descartar cambios» o vuelve a crearla.";
            }
            Raise();
            return null;
        }

        private string RevertText(string key, KeynoteRecord before)
        {
            var r = Current.Find(key);
            if (r != null && before != null) { r.Text = before.Text; Raise(); }
            return null;
        }
    }
}
