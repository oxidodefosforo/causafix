using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CausaFix.Core.Model;

namespace CausaFix.Revit
{
    /// <summary>
    /// Read-only access to the model for the Excel Link tabs. Every method must run in a valid API
    /// context (through RevitDispatcher) and returns DTOs only.
    /// </summary>
    public static class ModelReader
    {
        private static readonly HashSet<long> SpatialCategoryIds = new HashSet<long>
        {
            (long)(int)BuiltInCategory.OST_Rooms, (long)(int)BuiltInCategory.OST_MEPSpaces, (long)(int)BuiltInCategory.OST_Areas
        };

        public static bool IsSpatial(long categoryId) => SpatialCategoryIds.Contains(categoryId);

        // ------------------------------------------------------------------ scope

        /// <summary>Null when the scope can be used; otherwise a message for the user.</summary>
        public static string ScopeError(UIDocument uidoc, ScopeMode mode)
        {
            if (mode == ScopeMode.ActiveView)
            {
                View v = uidoc.ActiveView;
                if (v == null) return "There is no active view.";
                if (v is ViewSchedule || v is ViewSheet || v.ViewType == ViewType.ProjectBrowser || v.ViewType == ViewType.SystemBrowser ||
                    v.ViewType == ViewType.Internal || v.ViewType == ViewType.Undefined || v.ViewType == ViewType.PanelSchedule || v.IsTemplate)
                    return "Active View needs a graphical view (plan, section, 3D...). '" + v.Name + "' is a " + v.ViewType +
                           ". Use the Schedules tab for schedules and sheets.";
            }
            if (mode == ScopeMode.CurrentSelection && uidoc.Selection.GetElementIds().Count == 0)
                return "Nothing is selected in Revit.";
            return null;
        }

        public static string ScopeDescription(UIDocument uidoc, ScopeMode mode)
        {
            switch (mode)
            {
                case ScopeMode.ActiveView: return "Active view: " + uidoc.ActiveView?.Name + " (elements the view's filter returns; links not filtered by the view)";
                case ScopeMode.CurrentSelection: return "Current selection: " + uidoc.Selection.GetElementIds().Count + " element(s)";
                default: return "Whole model (host document)";
            }
        }

        private static FilteredElementCollector Collector(Document doc, UIDocument uidoc, ScopeMode mode)
        {
            switch (mode)
            {
                case ScopeMode.ActiveView:
                    return new FilteredElementCollector(doc, uidoc.ActiveView.Id);
                case ScopeMode.CurrentSelection:
                    {
                        var ids = uidoc.Selection.GetElementIds();
                        if (ids.Count == 0) return null;
                        return new FilteredElementCollector(doc, ids);
                    }
                default:
                    return new FilteredElementCollector(doc);
            }
        }

        private static Category TopCategory(Category c)
        {
            while (c?.Parent != null) c = c.Parent;
            return c;
        }

        // ------------------------------------------------------------------ categories

        public static List<CategoryInfo> Categories(UIDocument uidoc, ScopeMode mode)
        {
            Document doc = uidoc.Document;
            var map = new Dictionary<long, CategoryInfo>();
            var col = Collector(doc, uidoc, mode);
            if (col == null) return new List<CategoryInfo>();
            foreach (Element e in col.WhereElementIsNotElementType())
            {
                Category c = TopCategory(e.Category);
                if (c == null) continue;
                if (c.CategoryType != CategoryType.Model && c.CategoryType != CategoryType.Annotation) continue;
                long id = RevitCompat.Value(c.Id);
                if (!map.TryGetValue(id, out var info))
                {
                    info = new CategoryInfo
                    {
                        Id = id,
                        Name = c.Name,
                        Kind = c.CategoryType == CategoryType.Model ? CategoryKind.Model : CategoryKind.Annotation,
                        IsSpatial = IsSpatial(id)
                    };
                    map[id] = info;
                }
                info.Count++;
            }
            return map.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static List<ElementId> InstanceIds(UIDocument uidoc, ScopeMode mode, IEnumerable<long> categoryIds, Document docOverride = null)
        {
            Document doc = docOverride ?? uidoc.Document;
            var ids = new List<ElementId>();
            var cats = new HashSet<long>(categoryIds);
            if (cats.Count == 0) return ids;
            FilteredElementCollector col = docOverride != null ? new FilteredElementCollector(doc) : Collector(doc, uidoc, mode);
            if (col == null) return ids;
            var filter = new ElementMulticategoryFilter(cats.Select(c => RevitCompat.Id(c)).ToList());
            foreach (Element e in col.WhereElementIsNotElementType().WherePasses(filter))
            {
                Category c = TopCategory(e.Category);
                if (c != null && cats.Contains(RevitCompat.Value(c.Id))) ids.Add(e.Id);
            }
            return ids;
        }

        // ------------------------------------------------------------------ types (Elements tab)

        public static List<TypeInfo> Types(UIDocument uidoc, ScopeMode mode, IEnumerable<long> categoryIds)
        {
            Document doc = uidoc.Document;
            var counts = new Dictionary<long, int>();
            foreach (var id in InstanceIds(uidoc, mode, categoryIds))
            {
                ElementId t = doc.GetElement(id)?.GetTypeId();
                if (!RevitCompat.IsValid(t)) continue;
                long k = RevitCompat.Value(t);
                counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            var list = new List<TypeInfo>();
            var cats = new HashSet<long>(categoryIds);
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsElementType()
                         .WherePasses(new ElementMulticategoryFilter(cats.Select(c => RevitCompat.Id(c)).ToList())))
            {
                if (!(e is ElementType et)) continue;
                long id = RevitCompat.Value(et.Id);
                int count = counts.TryGetValue(id, out int n) ? n : 0;
                if (mode != ScopeMode.WholeModel && count == 0) continue;
                Category c = TopCategory(et.Category);
                list.Add(new TypeInfo
                {
                    UniqueId = et.UniqueId,
                    Id = id,
                    CategoryId = c == null ? -1 : RevitCompat.Value(c.Id),
                    CategoryName = c?.Name ?? "",
                    FamilyName = et.FamilyName ?? "",
                    TypeName = et.Name ?? "",
                    InstanceCount = count
                });
            }
            return list.OrderBy(t => t.CategoryName).ThenBy(t => t.FamilyName).ThenBy(t => t.TypeName).ToList();
        }

        // ------------------------------------------------------------------ parameter discovery

        /// <summary>
        /// Union of parameters over the selection. One sample instance per type (plus its type) is enough:
        /// the parameter set is defined by the family/type and the category bindings. AppliesTo counts
        /// the instances represented by the samples that expose the parameter.
        /// </summary>
        public static List<ParameterInfo> Parameters(UIDocument uidoc, ScopeMode mode, IEnumerable<long> categoryIds, RowMode rowMode,
                                                     ICollection<string> typeUniqueIds)
        {
            Document doc = uidoc.Document;
            var groups = new Dictionary<string, (Element sample, int count)>(StringComparer.Ordinal);
            var typeFilter = typeUniqueIds != null && typeUniqueIds.Count > 0 ? new HashSet<string>(typeUniqueIds, StringComparer.Ordinal) : null;

            if (rowMode == RowMode.Type)
            {
                foreach (var t in Types(uidoc, mode, categoryIds))
                {
                    if (typeFilter != null && !typeFilter.Contains(t.UniqueId)) continue;
                    var et = doc.GetElement(t.UniqueId);
                    if (et != null) groups[t.UniqueId] = (et, 1);
                }
            }
            else
            {
                foreach (var id in InstanceIds(uidoc, mode, categoryIds))
                {
                    Element e = doc.GetElement(id);
                    if (e == null) continue;
                    ElementId tid = e.GetTypeId();
                    if (typeFilter != null)
                    {
                        Element te = RevitCompat.IsValid(tid) ? doc.GetElement(tid) : null;
                        if (te == null || !typeFilter.Contains(te.UniqueId)) continue;
                    }
                    string g = RevitCompat.IsValid(tid) ? "t" + RevitCompat.Value(tid).ToString(CultureInfo.InvariantCulture)
                                                         : "c" + RevitCompat.Value(TopCategory(e.Category)?.Id ?? ElementId.InvalidElementId).ToString(CultureInfo.InvariantCulture);
                    groups[g] = groups.TryGetValue(g, out var cur) ? (cur.sample, cur.count + 1) : (e, 1);
                }
            }

            int total = groups.Values.Sum(v => v.count);
            var infos = new Dictionary<ParamKey, ParameterInfo>();
            var editableCounts = new Dictionary<ParamKey, int>();
            var lockReasons = new Dictionary<ParamKey, string>();

            void Visit(Element holder, ParamScope scope, int weight)
            {
                if (holder == null) return;
                foreach (Parameter p in holder.GetOrderedParameters())
                {
                    if (p?.Definition == null || p.StorageType == StorageType.None) continue;
                    var key = ParamIdentity.KeyOf(p, doc, scope);
                    if (key == null) continue;
                    if (!infos.TryGetValue(key, out var info))
                    {
                        info = ParamIdentity.Describe(p, doc, scope);
                        if (info == null) continue;
                        infos[key] = info;
                        editableCounts[key] = 0;
                    }
                    info.AppliesTo += weight;
                    string reason = ColumnLock(p);
                    if (reason == null) editableCounts[key] += weight;
                    else if (!lockReasons.ContainsKey(key)) lockReasons[key] = reason;
                }
            }

            foreach (var kv in groups.Values)
            {
                if (rowMode == RowMode.Type)
                {
                    Visit(kv.sample, ParamScope.Type, kv.count);
                }
                else
                {
                    Visit(kv.sample, ParamScope.Instance, kv.count);
                    Visit(ParamIdentity.Holder(kv.sample, ParamScope.Type), ParamScope.Type, kv.count);
                }
            }

            foreach (var kv in infos)
            {
                var info = kv.Value;
                info.Total = total;
                int editable = editableCounts[kv.Key];
                info.ReadOnly = editable == 0;
                info.PartiallyReadOnly = editable > 0 && editable < info.AppliesTo;
                if (lockReasons.TryGetValue(kv.Key, out string r)) info.ReadOnlyReason = r;
            }
            // Mark homonyms so the UI can show them distinctly (same name, different identity).
            foreach (var g in infos.Values.GroupBy(i => i.Name + "|" + i.Scope).Where(g => g.Count() > 1))
                foreach (var i in g) i.Name = i.Name + " [" + i.SourceLabel + (i.Source == ParamSource.Shared ? " " + i.Key.Id.Substring(0, 8) : "") + "]";

            return infos.Values.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Scope).ToList();
        }

        /// <summary>Parameter-level lock (independent of the element). Element-level locks are resolved per cell.</summary>
        public static string ColumnLock(Parameter p) => ParamAccess.ParameterLock(p)?.Reason;

        // ------------------------------------------------------------------ labels

        public static (string category, string family, string type) Names(Element e, Document doc)
        {
            string cat = TopCategory(e.Category)?.Name ?? "";
            if (e is ElementType et) return (cat, et.FamilyName ?? "", et.Name ?? "");
            ElementId tid = e.GetTypeId();
            if (RevitCompat.IsValid(tid) && doc.GetElement(tid) is ElementType t) return (cat, t.FamilyName ?? "", t.Name ?? "");
            return (cat, "", e.Name ?? "");
        }

        public static string Label(Element e, Document doc)
        {
            var (c, f, t) = Names(e, doc);
            var parts = new[] { c, f, t }.Where(x => !string.IsNullOrEmpty(x));
            return string.Join(" : ", parts) + " [" + RevitCompat.Value(e.Id).ToString(CultureInfo.InvariantCulture) + "]";
        }
    }
}
