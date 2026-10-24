using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using CausaFix.Core.Model;

namespace CausaFix.Revit
{
    /// <summary>
    /// Additional exports. Project Information is a normal (editable) element sheet; the family inventory
    /// and object/line styles are read-only inventories in this version.
    /// </summary>
    public static class Inventories
    {
        private static ColumnDef Ctx(string key, string header) => PseudoColumns.Column(key, header);

        private static void Put(RowData row, string key, string text)
            => row.Cells[ParamKey.Pseudo(key)] = new CellValue { Raw = RawValue.FromText(text ?? ""), DisplayText = text ?? "", Editable = false, LockReason = "Inventory (read-only)." };

        /// <summary>Plan for the single Project Information element with every visible parameter.</summary>
        public static SheetPlan PlanProjectInformation(Document doc)
        {
            ProjectInfo pi = doc.ProjectInformation;
            var ds = new DataSheet { SheetKey = "pinfo", Title = "Project Information", Source = SheetSource.ProjectInformation, RowMode = RowMode.Instance };
            ds.Columns.Add(PseudoColumns.Column(PseudoColumns.ElementId));
            foreach (Parameter p in pi.GetOrderedParameters())
            {
                if (p?.Definition == null || p.StorageType == StorageType.None) continue;
                var info = ParamIdentity.Describe(p, doc, ParamScope.Instance);
                if (info == null || ds.FindColumn(info.Key) != null) continue;
                string reason = ModelReader.ColumnLock(p);
                info.ReadOnly = reason != null;
                info.ReadOnlyReason = reason ?? string.Empty;
                ds.Columns.Add(info.ToColumn());
            }
            var plan = new SheetPlan { Sheet = ds };
            plan.Ids.Add(RevitCompat.Value(pi.Id));
            return plan;
        }

        public static DataSheet Families(Document doc)
        {
            var ds = new DataSheet { SheetKey = "inv:families", Title = "Family inventory", Source = SheetSource.FamilyInventory, ReadOnly = true };
            foreach (var (k, h) in new[] { ("Category", "Category"), ("Family", "Family"), ("InPlace", "In-place"), ("Editable", "Editable"), ("Types", "Types"), ("Instances", "Instances") })
                ds.Columns.Add(Ctx(k, h));
            var instances = new Dictionary<long, int>();
            foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)))
            {
                long fid = RevitCompat.Value(fi.Symbol?.Family?.Id ?? ElementId.InvalidElementId);
                instances[fid] = instances.TryGetValue(fid, out int n) ? n + 1 : 1;
            }
            foreach (Family f in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                         .OrderBy(x => x.FamilyCategory?.Name).ThenBy(x => x.Name))
            {
                var row = new RowData { RowKey = f.UniqueId, UniqueId = f.UniqueId, ElementId = RevitCompat.Value(f.Id), Label = f.Name };
                Put(row, "Category", f.FamilyCategory?.Name);
                Put(row, "Family", f.Name);
                Put(row, "InPlace", f.IsInPlace ? "Yes" : "No");
                Put(row, "Editable", f.IsEditable ? "Yes" : "No");
                Put(row, "Types", f.GetFamilySymbolIds().Count.ToString(CultureInfo.InvariantCulture));
                Put(row, "Instances", (instances.TryGetValue(RevitCompat.Value(f.Id), out int c) ? c : 0).ToString(CultureInfo.InvariantCulture));
                ds.Rows.Add(row);
            }
            return ds;
        }

        public static DataSheet ObjectStyles(Document doc)
        {
            var ds = new DataSheet { SheetKey = "inv:objectstyles", Title = "Object Styles", Source = SheetSource.ObjectStyles, ReadOnly = true };
            foreach (var (k, h) in new[] { ("Kind", "Kind"), ("Category", "Category"), ("Subcategory", "Subcategory"), ("WeightProjection", "Line weight projection"),
                                           ("WeightCut", "Line weight cut"), ("Color", "Line color (RGB)"), ("Pattern", "Line pattern"), ("Material", "Material") })
                ds.Columns.Add(Ctx(k, h));
            foreach (Category c in doc.Settings.Categories.Cast<Category>().OrderBy(c => c.CategoryType).ThenBy(c => c.Name))
            {
                if (c.CategoryType != CategoryType.Model && c.CategoryType != CategoryType.Annotation) continue;
                AddStyleRow(ds, doc, c, null);
                foreach (Category sub in c.SubCategories.Cast<Category>().OrderBy(s => s.Name)) AddStyleRow(ds, doc, c, sub);
            }
            return ds;
        }

        public static DataSheet LineStyles(Document doc)
        {
            var ds = new DataSheet { SheetKey = "inv:linestyles", Title = "Line Styles", Source = SheetSource.LineStyles, ReadOnly = true };
            foreach (var (k, h) in new[] { ("Subcategory", "Line style"), ("WeightProjection", "Line weight"), ("Color", "Line color (RGB)"), ("Pattern", "Line pattern") })
                ds.Columns.Add(Ctx(k, h));
            Category lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
            if (lines == null) return ds;
            foreach (Category sub in lines.SubCategories.Cast<Category>().OrderBy(s => s.Name)) AddStyleRow(ds, doc, lines, sub);
            return ds;
        }

        private static void AddStyleRow(DataSheet ds, Document doc, Category parent, Category sub)
        {
            Category c = sub ?? parent;
            string key = "catstyle:" + RevitCompat.Value(c.Id).ToString(CultureInfo.InvariantCulture);
            var row = new RowData { RowKey = key, UniqueId = key, ElementId = RevitCompat.Value(c.Id), Label = parent.Name + (sub != null ? " / " + sub.Name : "") };
            Put(row, "Kind", parent.CategoryType.ToString());
            Put(row, "Category", parent.Name);
            Put(row, "Subcategory", sub?.Name ?? "");
            Put(row, "WeightProjection", Weight(c, GraphicsStyleType.Projection));
            Put(row, "WeightCut", Weight(c, GraphicsStyleType.Cut));
            Color col = null;
            try { col = c.LineColor; } catch (Exception) { }
            Put(row, "Color", col != null && col.IsValid ? col.Red + "," + col.Green + "," + col.Blue : "");
            string pattern = "";
            try
            {
                ElementId pid = c.GetLinePatternId(GraphicsStyleType.Projection);
                if (RevitCompat.IsValid(pid)) pattern = doc.GetElement(pid)?.Name ?? "";
                else if (pid != null && RevitCompat.Value(pid) == RevitCompat.Value(LinePatternElement.GetSolidPatternId())) pattern = "Solid";
            }
            catch (Exception) { }
            Put(row, "Pattern", pattern);
            Put(row, "Material", c.Material?.Name ?? "");
            ds.Rows.Add(row);
        }

        private static string Weight(Category c, GraphicsStyleType t)
        {
            try
            {
                int? w = c.GetLineWeight(t);
                return w.HasValue ? w.Value.ToString(CultureInfo.InvariantCulture) : "";
            }
            catch (Exception) { return ""; }
        }
    }
}
