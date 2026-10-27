using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CausaFix.Core.Model;

namespace CausaFix.Revit
{
    /// <summary>One sheet to read: columns are fixed, element ids are read in chunks (stages).</summary>
    public sealed class SheetPlan
    {
        public DataSheet Sheet { get; set; }
        public List<long> Ids { get; } = new List<long>();
        /// <summary>UniqueId of the RevitLinkInstance for linked rows; null for host rows.</summary>
        public string LinkInstanceUniqueId { get; set; }
        public string LinkName { get; set; }
        public bool RowsAreTypes { get; set; }
        public bool AddStatusColumn { get; set; }
    }

    public sealed class SnapshotPlan
    {
        public List<SheetPlan> Sheets { get; } = new List<SheetPlan>();
        public List<string> Notes { get; } = new List<string>();
        public string ScopeDescription { get; set; }
        public int TotalRows => Sheets.Sum(s => s.Ids.Count);
    }

    /// <summary>
    /// Builds DataSheets from the model in two stages: Plan (ids + columns) and ReadChunk (values).
    /// Chunks let the UI show progress and cancel between external-event callbacks.
    /// </summary>
    public static class SnapshotBuilder
    {
        public static SnapshotPlan Plan(UIDocument uidoc, SnapshotRequest req)
        {
            Document doc = uidoc.Document;
            var plan = new SnapshotPlan { ScopeDescription = ModelReader.ScopeDescription(uidoc, req.Scope) };
            string err = ModelReader.ScopeError(uidoc, req.Scope);
            if (err != null) throw new InvalidOperationException(err);

            bool typeRows = req.RowMode == RowMode.Type;
            var columns = BuildColumns(req, plan, typeRows, linked: false);
            var typeFilter = req.TypeUniqueIds.Count > 0 ? new HashSet<string>(req.TypeUniqueIds, StringComparer.Ordinal) : null;
            string filter = (req.TextFilter ?? string.Empty).Trim();

            var categoryNames = new Dictionary<long, string>();
            foreach (long c in req.CategoryIds)
            {
                Category cat = Category.GetCategory(doc, RevitCompat.Id(c));
                categoryNames[c] = cat?.Name ?? ("Category " + c.ToString(CultureInfo.InvariantCulture));
            }

            var byCategory = new Dictionary<long, List<long>>();
            foreach (long c in req.CategoryIds) byCategory[c] = new List<long>();

            if (typeRows)
            {
                foreach (var t in ModelReader.Types(uidoc, req.Scope, req.CategoryIds))
                {
                    if (typeFilter != null && !typeFilter.Contains(t.UniqueId)) continue;
                    if (filter.Length > 0 && !Matches(filter, t.FamilyName, t.TypeName)) continue;
                    if (byCategory.TryGetValue(t.CategoryId, out var list)) list.Add(t.Id);
                }
            }
            else
            {
                foreach (var id in ModelReader.InstanceIds(uidoc, req.Scope, req.CategoryIds))
                {
                    Element e = doc.GetElement(id);
                    if (e == null) continue;
                    if (typeFilter != null)
                    {
                        Element te = doc.GetElement(e.GetTypeId());
                        if (te == null || !typeFilter.Contains(te.UniqueId)) continue;
                    }
                    if (filter.Length > 0)
                    {
                        var (_, fam, typ) = ModelReader.Names(e, doc);
                        string mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                        if (!Matches(filter, fam, typ, mark, e.Name)) continue;
                    }
                    long catId = RevitCompat.Value(e.Category?.Id ?? ElementId.InvalidElementId);
                    if (!byCategory.ContainsKey(catId))
                    {
                        Category top = e.Category;
                        while (top?.Parent != null) top = top.Parent;
                        catId = RevitCompat.Value(top?.Id ?? ElementId.InvalidElementId);
                    }
                    if (byCategory.TryGetValue(catId, out var list)) list.Add(RevitCompat.Value(id));
                }
            }

            string suffix = typeRows ? " (types)" : "";
            if (req.SheetPerCategory)
            {
                foreach (long c in req.CategoryIds)
                {
                    var sp = NewSheet(req, columns, "cat:" + c.ToString(CultureInfo.InvariantCulture) + ":" + (typeRows ? "T" : "I") + KeySuffix(req),
                                      categoryNames[c] + suffix, typeRows);
                    sp.Ids.AddRange(byCategory[c]);
                    plan.Sheets.Add(sp);
                }
            }
            else
            {
                var sp = NewSheet(req, columns, "multi:" + string.Join(",", req.CategoryIds) + ":" + (typeRows ? "T" : "I") + KeySuffix(req),
                                  (req.Source == SheetSource.Elements ? "Elements" : "Selection") + suffix, typeRows);
                foreach (long c in req.CategoryIds) sp.Ids.AddRange(byCategory[c]);
                plan.Sheets.Add(sp);
            }

            if (req.IncludeLinks) PlanLinks(uidoc, req, plan, typeRows, categoryNames);
            return plan;
        }

        private static string KeySuffix(SnapshotRequest req) => req.Source == SheetSource.Elements ? ":el" : req.Source == SheetSource.Spatial ? ":sp" : "";

        private static bool Matches(string filter, params string[] values)
            => values.Any(v => !string.IsNullOrEmpty(v) && v.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);

        private static SheetPlan NewSheet(SnapshotRequest req, List<ColumnDef> columns, string key, string title, bool typeRows)
        {
            var ds = new DataSheet { SheetKey = key, Title = title, Source = req.Source, RowMode = typeRows ? RowMode.Type : RowMode.Instance };
            ds.Columns.AddRange(columns.Select(c => c.Clone()));
            var sp = new SheetPlan { Sheet = ds, RowsAreTypes = typeRows, AddStatusColumn = req.Source == SheetSource.Spatial };
            if (req.Source == SheetSource.Spatial)
                ds.Notes.Add("Spatial elements are updated only; new Rooms/Spaces/Areas are not created or placed in this version.");
            return sp;
        }

        private static List<ColumnDef> BuildColumns(SnapshotRequest req, SnapshotPlan plan, bool typeRows, bool linked)
        {
            var cols = new List<ColumnDef> { PseudoColumns.Column(PseudoColumns.ElementId) };
            if (req.IncludeLinks) cols.Add(PseudoColumns.Column(PseudoColumns.Document, "Document"));
            cols.Add(PseudoColumns.Column(PseudoColumns.Category));
            cols.Add(PseudoColumns.Column(PseudoColumns.Family));
            cols.Add(PseudoColumns.Column(PseudoColumns.Type));
            if (req.Source == SheetSource.Spatial) cols.Add(PseudoColumns.Column(PseudoColumns.Status, "Placement"));
            bool skippedInstance = false;
            foreach (var p in req.Parameters)
            {
                if (typeRows && p.Scope == ParamScope.Instance) { skippedInstance = true; continue; }
                cols.Add(p.ToColumn());
            }
            if (skippedInstance) plan.Notes.Add("Instance parameters are not available when exporting one row per type; they were left out.");
            return cols;
        }

        private static void PlanLinks(UIDocument uidoc, SnapshotRequest req, SnapshotPlan plan, bool typeRows, Dictionary<long, string> categoryNames)
        {
            Document doc = uidoc.Document;
            if (req.Scope == ScopeMode.CurrentSelection)
            {
                plan.Notes.Add("Linked models are not included for Current Selection (host selection cannot contain linked elements).");
                return;
            }
            if (req.Scope == ScopeMode.ActiveView)
                plan.Notes.Add("Linked rows include every element of the selected categories in each link: the host view's visibility is not applied to links.");
            if (req.Parameters.Any(p => p.Source == ParamSource.Project))
                plan.Notes.Add("Non-shared project parameters are identified per document; in linked models they appear as N/A.");

            var linkCols = BuildColumns(req, new SnapshotPlan(), typeRows, linked: true);
            foreach (RevitLinkInstance li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
            {
                Document ld = li.GetLinkDocument();
                string name = li.Name;
                if (ld == null)
                {
                    plan.Notes.Add("Link '" + name + "' is not loaded: skipped.");
                    continue;
                }
                foreach (long c in req.CategoryIds)
                {
                    if (c >= 0) { plan.Notes.Add("Category " + categoryNames[c] + " is not built-in; it cannot be matched inside link '" + name + "'."); continue; }
                    var sp = NewSheet(req, linkCols, "lnk:" + li.UniqueId + ":" + c.ToString(CultureInfo.InvariantCulture) + ":" + (typeRows ? "T" : "I") + KeySuffix(req),
                                      categoryNames[c] + " – " + ld.Title + suffixFor(typeRows), typeRows);
                    sp.LinkInstanceUniqueId = li.UniqueId;
                    sp.LinkName = ld.Title + " (" + name + ")";
                    sp.Sheet.ReadOnly = true;
                    sp.Sheet.Notes.Add("Linked model '" + sp.LinkName + "': read-only.");
                    var filter = new ElementCategoryFilter(RevitCompat.Id(c));
                    var col = new FilteredElementCollector(ld).WherePasses(filter);
                    var elems = typeRows ? col.WhereElementIsElementType() : col.WhereElementIsNotElementType();
                    foreach (Element e in elems) sp.Ids.Add(RevitCompat.Value(e.Id));
                    if (sp.Ids.Count > 0) plan.Sheets.Add(sp);
                }
            }

            string suffixFor(bool t) => t ? " (types)" : "";
        }

        /// <summary>Resolves the document of a plan (host or loaded link). Throws if the link is gone.</summary>
        public static Document DocumentOf(Document host, SheetPlan plan)
        {
            if (plan.LinkInstanceUniqueId == null) return host;
            var li = host.GetElement(plan.LinkInstanceUniqueId) as RevitLinkInstance;
            Document ld = li?.GetLinkDocument();
            if (ld == null) throw new InvalidOperationException("Link '" + plan.LinkName + "' was unloaded or removed while reading.");
            return ld;
        }

        /// <summary>Reads rows [start, start+count) of a sheet plan.</summary>
        public static List<RowData> ReadChunk(Document host, SheetPlan plan, int start, int count, RevitUnits units)
        {
            Document doc = DocumentOf(host, plan);
            bool linked = plan.LinkInstanceUniqueId != null;
            var access = new ParamAccess(doc);
            var owners = new ParamOwner(doc);
            var rows = new List<RowData>(count);
            int end = Math.Min(plan.Ids.Count, start + count);
            for (int i = start; i < end; i++)
            {
                Element e = doc.GetElement(RevitCompat.Id(plan.Ids[i]));
                if (e == null) continue; // deleted between stages
                rows.Add(ReadRow(doc, e, plan, linked, access, units, owners));
            }
            return rows;
        }

        public static RowData ReadRow(Document doc, Element e, SheetPlan plan, bool linked, ParamAccess access, RevitUnits units, ParamOwner owners = null)
        {
            owners = owners ?? new ParamOwner(doc);
            var row = new RowData
            {
                UniqueId = e.UniqueId,
                RowKey = linked ? DocKeys.LinkedRowKey(plan.LinkInstanceUniqueId, e.UniqueId) : e.UniqueId,
                ElementId = RevitCompat.Value(e.Id),
                IsLinked = linked,
                DocKey = linked ? DocKeys.ForLink(plan.LinkInstanceUniqueId) : DocKeys.Host,
                Label = ModelReader.Label(e, doc)
            };
            if (!(e is ElementType))
            {
                ElementId tid = e.GetTypeId();
                if (RevitCompat.IsValid(tid)) row.TypeUniqueId = doc.GetElement(tid)?.UniqueId ?? string.Empty;
            }

            var (cat, fam, typ) = ModelReader.Names(e, doc);
            foreach (var col in plan.Sheet.Columns)
            {
                if (col.IsContext)
                {
                    string text;
                    switch (col.Key.Id)
                    {
                        case PseudoColumns.ElementId: text = row.ElementId.ToString(CultureInfo.InvariantCulture); break;
                        case PseudoColumns.Document: text = linked ? plan.LinkName : doc.Title; break;
                        case PseudoColumns.Category: text = cat; break;
                        case PseudoColumns.Family: text = fam; break;
                        case PseudoColumns.Type: text = typ; break;
                        case PseudoColumns.Status: text = SpatialStatus(e); break;
                        default: text = string.Empty; break;
                    }
                    row.Cells[col.Key] = new CellValue { Raw = RawValue.FromText(text), DisplayText = text, Editable = false, LockReason = "Context column." };
                    continue;
                }

                Element holder = plan.RowsAreTypes ? e : ParamIdentity.Holder(e, col.Scope);
                var or = owners.Resolve(holder, col.Key);
                if (or.Resolution == ParamIdentity.Resolution.Ambiguous || or.ScopeAmbiguous)
                {
                    var amb = CellValue.NotApplicable();
                    amb.LockReason = or.ScopeAmbiguous ? "Instance and type both expose this parameter (" + or.Evidence + "): ambiguous, not editable."
                                                       : "Several parameters with the same name and data type on this element: ambiguous, not editable.";
                    row.Cells[col.Key] = amb;
                    continue;
                }
                if (or.Resolution == ParamIdentity.Resolution.NotFound || or.OwnerMismatch)
                {
                    // Not owned by this row's holder (e.g. it belongs to the type): N/A here, the other scope's column carries it.
                    var na = CellValue.NotApplicable();
                    if (or.OwnerMismatch) na.LockReason = "Owned by another element (" + or.Evidence + ").";
                    row.Cells[col.Key] = na;
                    continue;
                }
                Parameter p = or.Parameter;
                var cell = ValueIO.Read(p, col, units, doc);
                // Writability is decided per cell from the real parameter (never from the column or a sample).
                var lockInfo = access.Lock(holder, p, linked);
                cell.Editable = lockInfo == null;
                cell.LockReason = lockInfo?.Reason ?? string.Empty;
                row.Cells[col.Key] = cell;
            }
            return row;
        }

        public static string SpatialStatus(Element e)
        {
            if (!(e is SpatialElement se)) return string.Empty;
            if (se.Location == null) return "Not placed";
            if (se.Area <= 0) return "Not enclosed or redundant";
            return "Placed";
        }
    }
}
