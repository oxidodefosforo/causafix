using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CausaFix.Keynotes.Editing;
using CausaFix.Keynotes.Execution;
using CausaFix.Keynotes.Model;

namespace CausaFix.Revit.Keynotes
{
    /// <summary>
    /// Keynote Manager gateway. Every API call runs through the dispatcher (valid API context) and is bound to the
    /// document the window was opened for. Reload / LoadFrom and the update of model references for re-keys run in one
    /// TransactionGroup (one Undo step) and roll back together. Placement uses Revit's own keynote commands
    /// (PostCommand) and writes the key into the tags they create on the next Idling, then releases every event.
    /// Nothing here saves or synchronises the model.
    /// </summary>
    public sealed class KeynoteService : IKeynoteGateway, IDisposable
    {
        private readonly RevitDispatcher _dispatcher;
        private readonly RevitSession _session = new RevitSession();

        public KeynoteService(RevitDispatcher dispatcher) { _dispatcher = dispatcher; }

        public Action<string> Trace { get; set; }
        private void Log(string m) { try { Trace?.Invoke("Keynotes: " + m); } catch { } }

        // ------------------------------------------------------------------ table

        public Task<KeynoteTableInfo> ReadTable() => _dispatcher.InvokeAsync(app =>
        {
            Document doc = _session.Require(app);
            var info = new KeynoteTableInfo { DocumentTitle = doc.Title, IsWorkshared = doc.IsWorkshared };
            KeynoteTable kt = KeynoteTable.GetKeynoteTable(doc);
            ResolvePath(doc, kt, info);
            info.LoadedEntries = Entries(kt);
            info.IsLoaded = info.LoadedEntries.Count > 0 || info.LoadStatus.IndexOf("Loaded", StringComparison.OrdinalIgnoreCase) >= 0;
            if (doc.IsReadOnly) { info.DocumentReadOnly = true; info.DocumentReadOnlyReason = "el documento es de solo lectura"; }
            else if (doc.IsWorkshared)
            {
                try
                {
                    if (WorksharingUtils.GetCheckoutStatus(doc, kt.Id) == CheckoutStatus.OwnedByOtherUser)
                    {
                        info.DocumentReadOnly = true;
                        info.DocumentReadOnlyReason = "la tabla de keynotes está tomada por " + SafeStr(() => WorksharingUtils.GetWorksharingTooltipInfo(doc, kt.Id).Owner);
                    }
                }
                catch (Exception ex) { Log("checkout status: " + ex.Message); }
            }
            View av = app.ActiveUIDocument?.ActiveView;
            info.ActiveViewName = av?.Name ?? "";
            info.ActiveViewAcceptsTags = av != null && !av.IsTemplate && AcceptsTags(av.ViewType);
            info.HasKeynoteTagType = DefaultTagType(doc) != null;
            return info;
        });

        private static bool AcceptsTags(ViewType t)
        {
            switch (t)
            {
                case ViewType.FloorPlan: case ViewType.CeilingPlan: case ViewType.Elevation: case ViewType.Section: case ViewType.Detail:
                case ViewType.DraftingView: case ViewType.Legend: case ViewType.EngineeringPlan: case ViewType.AreaPlan: case ViewType.ThreeD:
                    return true;
                default: return false;
            }
        }

        private static Element DefaultTagType(Document doc)
        {
            try
            {
                ElementId id = doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_KeynoteTags));
                if (RevitCompat.IsValid(id) && doc.GetElement(id) != null) return doc.GetElement(id);
            }
            catch { }
            return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_KeynoteTags).WhereElementIsElementType().FirstOrDefault();
        }

        /// <summary>Path of the keynote file: external resource reference (InSessionPath) or external file reference.</summary>
        private void ResolvePath(Document doc, KeynoteTable kt, KeynoteTableInfo info)
        {
            try
            {
                if (R.Call(kt, "RefersToExternalResourceReferences") is bool refers && refers && R.Call(kt, "GetExternalResourceReferences") is IDictionary refs)
                {
                    foreach (DictionaryEntry e in refs)
                    {
                        string p = R.Get(e.Value, "InSessionPath") as string ?? "";
                        info.Path = p;
                        info.PathType = "Recurso externo";
                        bool local = p.Length > 2 && (Path.IsPathRooted(p) && (p[1] == ':' || p.StartsWith("\\\\", StringComparison.Ordinal)));
                        if (!local)
                        {
                            info.IsServerResource = true;
                            info.ServerName = Convert.ToString(R.Get(e.Value, "ServerId")) ?? "";
                        }
                        break;
                    }
                }
            }
            catch (Exception ex) { Log("external resource reference: " + ex.Message); }
            try
            {
                if (kt.IsExternalFileReference())
                {
                    object efr = R.Call(kt, "GetExternalFileReference");
                    if (efr != null)
                    {
                        if (string.IsNullOrEmpty(info.Path) && R.Call(efr, "GetAbsolutePath") is ModelPath mp) info.Path = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                        info.PathType = Convert.ToString(R.Get(efr, "PathType")) ?? info.PathType;
                        info.LoadStatus = Convert.ToString(R.Call(efr, "GetLinkedFileStatus")) ?? "";
                    }
                }
            }
            catch (Exception ex) { Log("external file reference: " + ex.Message); }
            if (string.IsNullOrEmpty(info.LoadStatus)) info.LoadStatus = string.IsNullOrEmpty(info.Path) ? "sin archivo" : "estado no informado";
        }

        /// <summary>Entries Revit has loaded now (KeynoteEntry: Key, ParentKey, KeynoteText).</summary>
        private static List<KeynoteRecord> Entries(KeynoteTable kt)
        {
            var list = new List<KeynoteRecord>();
            if (R.Call(kt, "GetKeyBasedTreeEntries") is IEnumerable entries)
                foreach (object e in entries)
                {
                    if (e == null) continue;
                    list.Add(new KeynoteRecord
                    {
                        Key = R.Get(e, "Key") as string ?? "",
                        ParentKey = R.Get(e, "ParentKey") as string ?? "",
                        Text = R.Get(e, "KeynoteText") as string ?? ""
                    });
                }
            return list;
        }

        // ------------------------------------------------------------------ usage

        private sealed class TagInfo
        {
            public Element Tag;
            public string Key;
            public TagSource Source;
            public long ViewId;
        }

        private static TagSource SourceOf(Element tag)
        {
            try
            {
                Parameter p = tag.get_Parameter(BuiltInParameter.KEY_SOURCE_PARAM);
                string s = ((p?.AsValueString() ?? p?.AsString()) ?? "").ToLowerInvariant();
                if (s.Contains("mater")) return TagSource.Material;
                if (s.Contains("user") || s.Contains("usuar") || s.Contains("utilisat") || s.Contains("benutzer") || s.Contains("utente")) return TagSource.User;
                if (s.Contains("elem") || s.Contains("élém")) return TagSource.Element;
                if (tag is IndependentTag it && it.IsMaterialTag) return TagSource.Material;
            }
            catch { }
            return TagSource.Unknown;
        }

        private static List<TagInfo> Tags(Document doc)
        {
            var list = new List<TagInfo>();
            foreach (Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_KeynoteTags).WhereElementIsNotElementType())
            {
                string key = e.get_Parameter(BuiltInParameter.KEY_VALUE)?.AsString() ?? "";
                list.Add(new TagInfo { Tag = e, Key = key, Source = SourceOf(e), ViewId = RevitCompat.Value(e.OwnerViewId) });
            }
            return list;
        }

        /// <summary>Types (element keynotes) and materials (material keynotes) whose Keynote parameter is set.</summary>
        private static IEnumerable<(Element owner, Parameter p, bool material)> Assignments(Document doc)
        {
            foreach (Element t in new FilteredElementCollector(doc).WhereElementIsElementType())
            {
                Parameter p = null;
                try { p = t.get_Parameter(BuiltInParameter.KEYNOTE_PARAM); } catch { }
                if (p != null && !string.IsNullOrEmpty(p.AsString())) yield return (t, p, false);
            }
            foreach (Element m in new FilteredElementCollector(doc).OfClass(typeof(Material)))
            {
                Parameter p = null;
                try { p = m.get_Parameter(BuiltInParameter.KEYNOTE_PARAM); } catch { }
                if (p != null && !string.IsNullOrEmpty(p.AsString())) yield return (m, p, true);
            }
        }

        public Task<UsageReport> ReadUsage() => _dispatcher.InvokeAsync(app =>
        {
            var r = new UsageReport { ReadUtc = DateTime.UtcNow };
            try
            {
                Document doc = _session.Require(app);
                long active = RevitCompat.Value(app.ActiveUIDocument?.ActiveView?.Id);
                var names = new Dictionary<long, string>();
                KeyUsage U(string k) { if (!r.ByKey.TryGetValue(k, out var u)) r.ByKey[k] = u = new KeyUsage { Key = k }; return u; }
                foreach (var t in Tags(doc))
                {
                    if (string.IsNullOrEmpty(t.Key)) continue;
                    var u = U(t.Key);
                    if (t.Source == TagSource.Material) u.MaterialTags++;
                    else if (t.Source == TagSource.Element) u.ElementTags++;
                    else u.UserTags++;
                    if (!names.TryGetValue(t.ViewId, out string vn)) names[t.ViewId] = vn = doc.GetElement(RevitCompat.Id(t.ViewId))?.Name ?? ("Vista " + t.ViewId);
                    var v = u.Views.FirstOrDefault(x => x.Id == t.ViewId);
                    if (v == null) u.Views.Add(v = new ViewRef { Id = t.ViewId, Name = vn });
                    v.Count++;
                    if (t.ViewId == active) u.InActiveView = true;
                }
                foreach (var a in Assignments(doc))
                {
                    var u = U(a.p.AsString());
                    if (a.material) u.MaterialAssignments++; else u.ElementAssignments++;
                }
                r.Succeeded = true;
            }
            catch (Exception ex)
            {
                r.Succeeded = false;
                r.Error = ex.Message;
                Log("usage failed: " + ex);
            }
            return r;
        });

        // ------------------------------------------------------------------ reload / link / re-key

        public Task<RekeyCheck> CheckRekey(IList<KeyRename> renames) => _dispatcher.InvokeAsync(app =>
        {
            Document doc = _session.Require(app);
            var c = new RekeyCheck();
            var tags = Tags(doc);
            var assigns = Assignments(doc).ToList();
            foreach (var rn in renames)
            {
                var t = tags.Where(x => x.Key == rn.From).ToList();
                var a = assigns.Where(x => x.p.AsString() == rn.From).ToList();
                c.Lines.Add(rn.From + " → " + rn.To + ": " + t.Count + " etiqueta(s), " + a.Count(x => !x.material) + " tipo(s), " + a.Count(x => x.material) + " material(es) se actualizarán.");
                if (tags.Any(x => x.Key == rn.To) || assigns.Any(x => x.p.AsString() == rn.To))
                    c.Blockers.Add("La clave nueva '" + rn.To + "' ya está usada en el modelo: se mezclarían referencias distintas.");
                foreach (var x in t)
                {
                    Parameter p = x.Tag.get_Parameter(BuiltInParameter.KEY_VALUE);
                    // Element/material tags take their key from the type/material: those are updated there instead.
                    if (x.Source == TagSource.User && (p == null || p.IsReadOnly)) c.Blockers.Add("Etiqueta " + RevitCompat.Value(x.Tag.Id) + " de '" + rn.From + "' no editable.");
                    if (Locked(doc, x.Tag, out string who)) c.Blockers.Add("Etiqueta " + RevitCompat.Value(x.Tag.Id) + " tomada por " + who + ".");
                }
                foreach (var x in a)
                {
                    if (x.p.IsReadOnly) c.Blockers.Add("'" + x.owner.Name + "': parámetro Keynote de solo lectura.");
                    if (Locked(doc, x.owner, out string who)) c.Blockers.Add("'" + x.owner.Name + "' tomado por " + who + ".");
                }
            }
            if (doc.IsReadOnly) c.Blockers.Add("El documento es de solo lectura.");
            c.CanApply = c.Blockers.Count == 0;
            return c;
        });

        private static bool Locked(Document doc, Element e, out string who)
        {
            who = "";
            if (!doc.IsWorkshared) return false;
            try
            {
                if (WorksharingUtils.GetCheckoutStatus(doc, e.Id) != CheckoutStatus.OwnedByOtherUser) return false;
                who = SafeStr(() => WorksharingUtils.GetWorksharingTooltipInfo(doc, e.Id).Owner);
                return true;
            }
            catch { return false; }
        }

        public Task<ReloadResult> Reload(IList<KeyRename> renames) => _dispatcher.InvokeAsync(app => Run(app, null, renames));

        public Task<ReloadResult> LinkFile(string path, IList<KeyRename> renames) => _dispatcher.InvokeAsync(app => Run(app, path, renames));

        private static bool Ok(object status)
        {
            string s = Convert.ToString(status) ?? "";
            return s == "Success" || s == "ResourceAlreadyCurrent";
        }

        private ReloadResult Run(UIApplication app, string linkPath, IList<KeyRename> renames)
        {
            Document doc = _session.Require(app);
            var res = new ReloadResult();
            if (doc.IsModifiable) { res.Status = "Ocupado"; res.Messages.Add("Hay otra transacción abierta en Revit: termina el comando en curso y vuelve a intentarlo."); return res; }
            if (doc.IsReadOnly) { res.Status = "Solo lectura"; res.Messages.Add("El documento es de solo lectura."); return res; }
            KeynoteTable kt = KeynoteTable.GetKeynoteTable(doc);
            var failures = new FailureCollector();
            using (var group = new TransactionGroup(doc, linkPath == null ? "CausaFixIt – Guardar y recargar keynotes" : "CausaFixIt – Vincular archivo de keynotes"))
            {
                group.Start();
                using (var t = new Transaction(doc, linkPath == null ? "Recargar keynotes" : "Vincular keynotes"))
                {
                    var fho = t.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(failures);
                    t.SetFailureHandlingOptions(fho);
                    t.Start();
                    var results = new KeyBasedTreeEntriesLoadResults();
                    object status;
                    try
                    {
                        status = linkPath == null ? kt.Reload(results) : LoadFrom(doc, kt, linkPath, results);
                    }
                    catch (Exception ex)
                    {
                        t.RollBack(); group.RollBack();
                        res.Status = "Error"; res.Messages.Add(ex.Message);
                        Log("reload/link threw: " + ex);
                        return res;
                    }
                    res.Status = Convert.ToString(status);
                    res.Messages.AddRange(LoadMessages(results));
                    if (!Ok(status))
                    {
                        t.RollBack(); group.RollBack();
                        Log("reload status " + res.Status + " " + string.Join(" | ", res.Messages));
                        return res;
                    }
                    if (renames != null && renames.Count > 0)
                    {
                        var tags = Tags(doc);
                        var assigns = Assignments(doc).ToList();
                        foreach (var rn in renames)
                        {
                            foreach (var x in tags.Where(x => x.Key == rn.From && x.Source == TagSource.User))
                            {
                                Parameter p = x.Tag.get_Parameter(BuiltInParameter.KEY_VALUE);
                                if (p != null && !p.IsReadOnly && p.Set(rn.To)) res.UpdatedTags++;
                            }
                            foreach (var x in assigns.Where(x => x.p.AsString() == rn.From))
                                if (!x.p.IsReadOnly && x.p.Set(rn.To)) res.UpdatedAssignments++;
                            // Element/material tags follow their type/material: count them as updated when their owner was.
                            res.UpdatedTags += tags.Count(x => x.Key == rn.From && x.Source != TagSource.User);
                        }
                    }
                    if (t.Commit() != TransactionStatus.Committed || failures.HadErrors)
                    {
                        group.RollBack();
                        res.Status = "Revertido";
                        res.Messages.AddRange(failures.Messages);
                        return res;
                    }
                }
                group.Assimilate();
            }
            res.Messages.AddRange(failures.Messages);
            res.Succeeded = true;
            res.LoadedEntries = Entries(KeynoteTable.GetKeynoteTable(doc));
            var info = new KeynoteTableInfo();
            ResolvePath(doc, KeynoteTable.GetKeynoteTable(doc), info);
            res.Path = info.Path;
            Log((linkPath == null ? "reloaded " : "linked " + linkPath + " ") + res.Status + ", " + res.LoadedEntries.Count + " entries, " + res.UpdatedTags + " tags / " + res.UpdatedAssignments + " assignments re-keyed");
            return res;
        }

        /// <summary>KeynoteTable.LoadFrom(ExternalResourceReference.CreateLocalResource(...), results) by reflection (signature differs between years).</summary>
        private static object LoadFrom(Document doc, KeynoteTable kt, string path, KeyBasedTreeEntriesLoadResults results)
        {
            Assembly api = typeof(Document).Assembly;
            Type errType = api.GetType("Autodesk.Revit.DB.ExternalResourceReference", true);
            Type ertTypes = api.GetType("Autodesk.Revit.DB.ExternalResourceTypes+BuiltInExternalResourceTypes", true);
            object keynoteType = ertTypes.GetProperty("KeynoteTable", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Type pathType = api.GetType("Autodesk.Revit.DB.PathType", true);
            object absolute = Enum.Parse(pathType, "Absolute");
            ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
            MethodInfo create = errType.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "CreateLocalResource" && m.GetParameters().Length == 4);
            object reference = create.Invoke(null, new[] { doc, keynoteType, mp, absolute });
            MethodInfo load = kt.GetType().GetMethods().First(m => m.Name == "LoadFrom" && m.GetParameters().Length == 2);
            object status = load.Invoke(kt, new[] { reference, results });
            // Some years return void: the reload that follows reports the real status.
            return status ?? kt.Reload(results);
        }

        private static IEnumerable<string> LoadMessages(KeyBasedTreeEntriesLoadResults r)
        {
            var list = new List<string>();
            foreach (string m in new[] { "GetFailureMessages", "GetFileReadErrors", "GetFileSyntaxErrors", "GetKeyBasedTreeEntryErrors" })
            {
                try
                {
                    if (R.Call(r, m) is IEnumerable items)
                        foreach (object o in items)
                        {
                            string s = Describe(o);
                            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                        }
                }
                catch { }
            }
            return list.Distinct().Take(100);
        }

        private static string Describe(object o)
        {
            if (o == null) return "";
            if (o is string s) return s;
            // KeyBasedTreeEntryError / FileReadError objects: show their readable properties.
            var parts = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType == typeof(string) || p.PropertyType.IsPrimitive || p.PropertyType.IsEnum))
                .Select(p => { try { return p.Name + "=" + p.GetValue(o); } catch { return null; } }).Where(x => x != null);
            string text = string.Join(", ", parts);
            return text.Length > 0 ? text : o.ToString();
        }

        // ------------------------------------------------------------------ show tags

        public Task<string> ShowTags(string key, long viewId) => _dispatcher.InvokeAsync(app =>
        {
            Document doc = _session.Require(app);
            UIDocument uidoc = app.ActiveUIDocument;
            var tags = Tags(doc).Where(t => t.Key == key).ToList();
            if (tags.Count == 0) return "No hay etiquetas de '" + key + "' en este proyecto.";
            long target = viewId;
            if (target == 0)
            {
                long active = RevitCompat.Value(uidoc.ActiveView?.Id);
                target = tags.Any(t => t.ViewId == active) ? active : tags.GroupBy(t => t.ViewId).OrderByDescending(g => g.Count()).First().Key;
            }
            var inView = tags.Where(t => t.ViewId == target).Select(t => t.Tag.Id).ToList();
            View v = doc.GetElement(RevitCompat.Id(target)) as View;
            if (v != null && RevitCompat.Value(uidoc.ActiveView?.Id) != target) uidoc.RequestViewChange(v);
            uidoc.Selection.SetElementIds(inView);
            return inView.Count + " etiqueta(s) de '" + key + "' seleccionadas en " + (v?.Name ?? "la vista") + ".";
        });

        // ------------------------------------------------------------------ placement

        private sealed class Placement
        {
            public Document Doc;
            public UIApplication App;
            public string Key = "";
            public PlacementMode Mode;
            public readonly List<ElementId> Pending = new List<ElementId>();
            public int Created, Keyed;
            public readonly List<string> Notes = new List<string>();
            public DateTime LastActivity = DateTime.UtcNow;
            public bool Processed;
        }

        private Placement _placing;

        public bool IsPlacing => _placing != null;
        public event EventHandler<PlacementResult> PlacementFinished;

        public Task<string> StartPlacement(PlacementMode mode, string key) => _dispatcher.InvokeAsync(app =>
        {
            Document doc = _session.Require(app);
            if (_placing != null) return "Ya hay una colocación en curso.";
            if (DefaultTagType(doc) == null) return "No hay ningún tipo de etiqueta de nota clave cargado en el proyecto.";
            if (!Entries(KeynoteTable.GetKeynoteTable(doc)).Any(e => e.Key == key)) return "'" + key + "' no está en la tabla cargada en Revit: guarda y recarga primero.";
            PostableCommand cmd = mode == PlacementMode.Element ? PostableCommand.ElementKeynote : mode == PlacementMode.Material ? PostableCommand.MaterialKeynote : PostableCommand.UserKeynote;
            RevitCommandId id = RevitCommandId.LookupPostableCommandId(cmd);
            if (id == null || !app.CanPostCommand(id)) return "Revit no permite iniciar el comando de nota clave ahora (¿hay otro comando activo?).";
            _placing = new Placement { Doc = doc, App = app, Key = key, Mode = mode };
            app.Application.DocumentChanged += OnDocumentChanged;
            app.Idling += OnIdling;
            if (mode != PlacementMode.Material) app.DialogBoxShowing += OnDialog;
            app.PostCommand(id);
            Log("placement started " + mode + " " + key);
            return mode == PlacementMode.Material
                ? "Coloca la nota de material en la vista. Revit mostrará su diálogo para elegir la clave del material (API: no permite fijarla desde aquí). Esc para terminar."
                : "Haz clic en la vista para colocar " + key + " (" + (mode == PlacementMode.User ? "nota de usuario" : "nota de elemento") + "). Esc para terminar.";
        });

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            var p = _placing;
            if (p == null) return;
            try
            {
                if (!ReferenceEquals(e.GetDocument(), p.Doc) && e.GetDocument()?.Title != p.Doc.Title) return;
                foreach (ElementId id in e.GetAddedElementIds())
                {
                    Element el = p.Doc.GetElement(id);
                    if (el?.Category != null && RevitCompat.Value(el.Category.Id) == (long)BuiltInCategory.OST_KeynoteTags && !(el is ElementType))
                    {
                        p.Pending.Add(id);
                        p.Created++;
                        p.LastActivity = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex) { Log("document changed: " + ex.Message); }
        }

        private void OnDialog(object sender, DialogBoxShowingEventArgs e)
        {
            var p = _placing;
            if (p == null) return;
            string id = e.DialogId ?? "";
            Log("dialog during placement: " + id);
            // Only Revit's keynote picker: the key is written by this tool right after.
            if (id.IndexOf("Keynote", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (e.Cancellable) e.Cancel();
            else e.OverrideResult(1);
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            var p = _placing;
            if (p == null) return;
            try
            {
                if (p.Pending.Count > 0)
                {
                    if (p.Doc.IsModifiable) return;                      // still inside the command's transaction
                    var ids = p.Pending.ToList();
                    p.Pending.Clear();
                    using (var t = new Transaction(p.Doc, "CausaFixIt – Nota clave " + p.Key))
                    {
                        t.Start();
                        foreach (ElementId id in ids) KeyTag(p, p.Doc.GetElement(id));
                        t.Commit();
                    }
                    p.Processed = true;
                    p.LastActivity = DateTime.UtcNow;
                    return;
                }
                // Done: an idle cycle with nothing new after tags were keyed, or nothing placed for 3 minutes.
                if (p.Processed || DateTime.UtcNow - p.LastActivity > TimeSpan.FromMinutes(3)) Finish(false);
            }
            catch (Exception ex)
            {
                Log("placement idling: " + ex);
                p.Notes.Add("Error: " + ex.Message);
                Finish(false);
            }
        }

        private void KeyTag(Placement p, Element tag)
        {
            if (tag == null) return;
            if (p.Mode == PlacementMode.User)
            {
                Parameter kp = tag.get_Parameter(BuiltInParameter.KEY_VALUE);
                if (kp != null && !kp.IsReadOnly && kp.Set(p.Key)) p.Keyed++;
                else p.Notes.Add("Etiqueta " + RevitCompat.Value(tag.Id) + ": no se pudo escribir la clave.");
                return;
            }
            if (p.Mode == PlacementMode.Element)
            {
                // The key belongs to the tagged element's type: set it only when empty (never overwrite another key silently).
                Element host = null;
                if (tag is IndependentTag it)
                {
                    try { host = it.GetTaggedLocalElementIds().Select(id => p.Doc.GetElement(id)).FirstOrDefault(x => x != null); } catch { }
                }
                Element type = host == null ? null : p.Doc.GetElement(host.GetTypeId()) ?? host;
                Parameter kp = type?.get_Parameter(BuiltInParameter.KEYNOTE_PARAM);
                if (kp == null) { p.Notes.Add("Etiqueta " + RevitCompat.Value(tag.Id) + ": el elemento etiquetado no tiene parámetro Keynote."); return; }
                string current = kp.AsString() ?? "";
                if (current == p.Key) { p.Keyed++; return; }
                if (current.Length > 0) { p.Notes.Add("'" + type.Name + "' ya tiene la clave " + current + ": no se cambió (la etiqueta muestra la clave del tipo)."); return; }
                if (kp.IsReadOnly || Locked(p.Doc, type, out _)) { p.Notes.Add("'" + type.Name + "': parámetro Keynote no editable."); return; }
                if (kp.Set(p.Key)) { p.Keyed++; p.Notes.Add("Clave " + p.Key + " asignada al tipo '" + type.Name + "' (afecta a todas sus instancias)."); }
            }
        }

        private void Finish(bool cancelled)
        {
            var p = _placing;
            if (p == null) return;
            _placing = null;
            try { p.App.Application.DocumentChanged -= OnDocumentChanged; } catch { }
            try { p.App.Idling -= OnIdling; } catch { }
            try { p.App.DialogBoxShowing -= OnDialog; } catch { }
            string msg = cancelled ? "Colocación terminada." :
                p.Created == 0 ? "No se colocó ninguna etiqueta." :
                p.Created + " etiqueta(s) colocada(s)" + (p.Mode == PlacementMode.Material ? " (clave elegida en el diálogo de Revit)." : ", " + p.Keyed + " con " + p.Key + ".");
            if (p.Notes.Count > 0) msg += " " + string.Join(" ", p.Notes.Distinct().Take(5));
            Log("placement finished: " + msg);
            try { PlacementFinished?.Invoke(this, new PlacementResult { Key = p.Key, Mode = p.Mode, TagsCreated = p.Created, TagsKeyed = p.Keyed, Message = msg, Cancelled = cancelled }); }
            catch (Exception ex) { Log("placement finished handler: " + ex.Message); }
        }

        public void CancelPlacement()
        {
            if (_placing == null) return;
            try { _dispatcher.InvokeAsync(app => { Finish(true); return true; }); } catch { }
        }

        public void Dispose() => CancelPlacement();

        private static string SafeStr(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
    }

    /// <summary>Reflection helpers for API members whose exact signature changed between Revit years.</summary>
    internal static class R
    {
        public static object Call(object target, string method, params object[] args)
        {
            if (target == null) return null;
            var m = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m?.Invoke(target, args);
        }

        public static object Get(object target, string property)
        {
            if (target == null) return null;
            return target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        }
    }
}
