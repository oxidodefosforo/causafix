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
    /// Schedules (ViewSchedule only — PanelScheduleView is a different class and is not supported in this version).
    ///
    /// Data exchange: rows are the elements Revit's schedule collector returns (one per element, even for
    /// grouped / non-itemized schedules); columns are fields bound to real instance or type parameters.
    /// Calculated, combined, count/percentage and related-element fields (room, material, project info,
    /// link) are not writable targets and are left out (listed in the sheet notes).
    ///
    /// Presentation: the table text as Revit displays it (headers, groups, totals). Read-only.
    /// </summary>
    public static class ScheduleReader
    {
        public static List<ScheduleInfo> List(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var onSheet = new HashSet<long>();
            long activeScheduleId = -1;
            View active = uidoc.ActiveView;
            if (active is ViewSheet sheet)
            {
                foreach (ScheduleSheetInstance ssi in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)))
                    if (!ssi.IsTitleblockRevisionSchedule) onSheet.Add(RevitCompat.Value(ssi.ScheduleId));
            }
            else if (active is ViewSchedule avs) activeScheduleId = RevitCompat.Value(avs.Id);

            var list = new List<ScheduleInfo>();
            foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)))
            {
                if (vs.IsTemplate || vs.IsTitleblockRevisionSchedule) continue;
                var info = Describe(doc, vs);
                info.OnActiveSheet = onSheet.Contains(info.Id);
                info.IsActiveView = info.Id == activeScheduleId;
                list.Add(info);
            }
            return list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static ScheduleInfo Describe(Document doc, ViewSchedule vs)
        {
            ScheduleDefinition def = vs.Definition;
            var info = new ScheduleInfo
            {
                UniqueId = vs.UniqueId,
                Id = RevitCompat.Value(vs.Id),
                Name = vs.Name,
                Itemized = def.IsItemized,
                FilteredBySheet = SafeBool(() => def.IsFilteredBySheet),
                HasEmbedded = SafeBool(() => def.HasEmbeddedSchedule),
                IncludesLinks = SafeBool(() => def.IncludeLinkedFiles),
                FieldCount = def.GetFieldCount()
            };
            Category cat = RevitCompat.IsValid(def.CategoryId) ? Category.GetCategory(doc, def.CategoryId) : null;
            info.CategoryName = cat?.Name ?? "Multi-Category";
            long catId = RevitCompat.Value(def.CategoryId);

            if (vs.IsInternalKeynoteSchedule) info.Kind = ScheduleKind.KeynoteLegend;
            else if (def.IsKeySchedule) info.Kind = ScheduleKind.KeySchedule;
            else if (def.IsMaterialTakeoff) info.Kind = ScheduleKind.MaterialTakeoff;
            else if (RevitCompat.IsValid(def.FamilyId)) info.Kind = ScheduleKind.NoteBlock;
            else if (catId == (int)BuiltInCategory.OST_Sheets) info.Kind = ScheduleKind.SheetList;
            else if (catId == (int)BuiltInCategory.OST_Views) info.Kind = ScheduleKind.ViewList;
            else info.Kind = ScheduleKind.Regular;

            for (int i = 0; i < info.FieldCount; i++)
            {
                ScheduleField f = def.GetField(i);
                if (f.IsHidden) info.HiddenFieldCount++;
                if (IsCalculated(f)) info.CalculatedFieldCount++;
            }

            switch (info.Kind)
            {
                case ScheduleKind.KeySchedule:
                    info.DataExchangeSupported = false;
                    info.DataExchangeNote = "Key schedule: rows are keys, not model elements. Not supported for data exchange in this version.";
                    break;
                case ScheduleKind.KeynoteLegend:
                    info.DataExchangeSupported = false;
                    info.DataExchangeNote = "Keynote legend: not supported for data exchange.";
                    break;
                default:
                    if (info.HasEmbedded)
                    {
                        info.DataExchangeSupported = false;
                        info.DataExchangeNote = "Contains an embedded schedule: nested rows cannot be identified safely. Use Presentation export.";
                    }
                    else if (info.FilteredBySheet)
                    {
                        info.DataExchangeSupported = false;
                        info.DataExchangeNote = "Filtered by sheet: its rows depend on the sheet instance. Use Presentation export.";
                    }
                    else
                    {
                        info.DataExchangeSupported = true;
                        var notes = new List<string>();
                        if (!info.Itemized) notes.Add("Not itemized: rows are expanded to one per element; totals are not exported in data mode.");
                        if (info.Kind == ScheduleKind.MaterialTakeoff) notes.Add("Material takeoff: material and quantity fields are read-only and left out; element fields are editable.");
                        if (info.IncludesLinks) notes.Add("Includes linked files: only host elements are exported.");
                        if (info.CalculatedFieldCount > 0) notes.Add(info.CalculatedFieldCount + " calculated field(s) are left out (see Presentation export).");
                        info.DataExchangeNote = string.Join(" ", notes);
                    }
                    break;
            }
            return info;
        }

        private static bool SafeBool(Func<bool> f)
        {
            try { return f(); } catch (Exception) { return false; }
        }

        private static bool IsCalculated(ScheduleField f)
        {
            if (f.IsCalculatedField) return true;
            if (SafeBool(() => f.IsCombinedParameterField)) return true;
            return f.FieldType == ScheduleFieldType.Count || f.FieldType == ScheduleFieldType.Formula || f.FieldType == ScheduleFieldType.Percentage;
        }

        /// <summary>Plans a data-exchange sheet for one schedule (ids + columns bound to parameters).</summary>
        public static SheetPlan PlanData(Document doc, ViewSchedule vs, RevitUnits units)
        {
            var info = Describe(doc, vs);
            if (!info.DataExchangeSupported) throw new InvalidOperationException("'" + vs.Name + "': " + info.DataExchangeNote);
            ScheduleDefinition def = vs.Definition;

            var ds = new DataSheet { SheetKey = "sch:" + vs.UniqueId, Title = vs.Name, Source = SheetSource.ScheduleData, RowMode = RowMode.Instance };
            if (!string.IsNullOrEmpty(info.DataExchangeNote)) ds.Notes.Add(info.DataExchangeNote);
            ds.Columns.Add(PseudoColumns.Column(PseudoColumns.ElementId));
            ds.Columns.Add(PseudoColumns.Column(PseudoColumns.Category));
            ds.Columns.Add(PseudoColumns.Column(PseudoColumns.Family));
            ds.Columns.Add(PseudoColumns.Column(PseudoColumns.Type));

            var plan = new SheetPlan { Sheet = ds };
            foreach (Element e in new FilteredElementCollector(doc, vs.Id).WhereElementIsNotElementType())
                plan.Ids.Add(RevitCompat.Value(e.Id));
            var samples = plan.Ids.Take(300).Select(id => doc.GetElement(RevitCompat.Id(id))).Where(e => e != null).ToList();

            var skipped = new List<string>();
            var seen = new HashSet<ParamKey>();
            var owners = new ParamOwner(doc);
            var corrected = new List<string>();
            foreach (ScheduleFieldId fid in def.GetFieldOrder())
            {
                ScheduleField f = def.GetField(fid);
                string heading = string.IsNullOrWhiteSpace(f.ColumnHeading) ? f.GetName() : f.ColumnHeading;
                if (IsCalculated(f)) { skipped.Add(heading + " (calculated)"); continue; }
                ParamScope fieldScope;
                if (f.FieldType == ScheduleFieldType.Instance) fieldScope = ParamScope.Instance;
                else if (f.FieldType == ScheduleFieldType.ElementType) fieldScope = ParamScope.Type;
                else { skipped.Add(heading + " (" + f.FieldType + " field)"); continue; }

                ParamKey fieldKey = KeyFromParameterId(doc, f.ParameterId, fieldScope);
                if (fieldKey == null) { skipped.Add(heading + " (no parameter)"); continue; }

                // ScheduleFieldType.Instance is also used for shared TYPE parameters (API reference), so the field type does not
                // identify the owner. The real scope is read on the scheduled elements: the element owns it, or its type does.
                var byScope = new Dictionary<ParamScope, int>();
                int ambiguous = 0;
                foreach (var e in samples)
                {
                    ParamScope? real = fieldScope == ParamScope.Type ? (ParamScope?)ParamScope.Type : owners.RealScope(e, fieldKey);
                    if (real == null) { if (owners.Resolve(e, fieldKey).ScopeAmbiguous) ambiguous++; continue; }
                    byScope[real.Value] = byScope.TryGetValue(real.Value, out int n) ? n + 1 : 1;
                }
                if (byScope.Count == 0)
                {
                    skipped.Add(heading + (ambiguous > 0 ? " (instance and type both expose it: ambiguous)" : " (not found on scheduled elements)"));
                    continue;
                }
                bool both = byScope.Count > 1;
                foreach (var scope in new[] { ParamScope.Instance, ParamScope.Type })
                {
                    if (!byScope.ContainsKey(scope)) continue;
                    ParamKey key = fieldKey.WithScope(scope);
                    if (!seen.Add(key)) continue;
                    if (scope != fieldScope) corrected.Add(heading + (scope == ParamScope.Type ? " → type parameter" : " → instance parameter"));

                    ParameterInfo pinfo = null;
                    int editable = 0;
                    string reason = null;
                    foreach (var e in samples)
                    {
                        Element holder = ParamIdentity.Holder(e, scope);
                        var r = owners.Resolve(holder, key);
                        if (!r.Found) continue;
                        if (pinfo == null) pinfo = ParamIdentity.Describe(r.Parameter, doc, scope);
                        string lockReason = ModelReader.ColumnLock(r.Parameter);
                        if (lockReason == null) editable++; else reason = reason ?? lockReason;
                    }
                    if (pinfo == null) continue;
                    pinfo.Name = heading + (both ? (scope == ParamScope.Type ? " (type)" : " (instance)") : "") + (f.IsHidden ? " (hidden in schedule)" : "");
                    pinfo.ReadOnly = editable == 0;
                    pinfo.ReadOnlyReason = reason ?? string.Empty;
                    ds.Columns.Add(pinfo.ToColumn());
                }
            }
            if (corrected.Count > 0)
                ds.Notes.Add("Scope read from the elements (schedules report shared type parameters as instance fields): " + string.Join(", ", corrected) + ".");
            if (skipped.Count > 0) ds.Notes.Add("Fields not exported in data mode: " + string.Join(", ", skipped) + ".");
            return plan;
        }

        public static ParamKey KeyFromParameterId(Document doc, ElementId pid, ParamScope scope)
        {
            if (!RevitCompat.IsValid(pid)) return null;
            long v = RevitCompat.Value(pid);
            if (v < 0) return ParamKey.BuiltIn(v, scope);
            Element pe = doc.GetElement(pid);
            if (pe is SharedParameterElement spe) return ParamKey.Shared(spe.GuidValue, scope);
            if (pe is ParameterElement) return ParamKey.Project(v, scope);
            return null;
        }

        /// <summary>Presentation export: the schedule text, section by section. Read-only.</summary>
        public static DataSheet Presentation(ViewSchedule vs)
        {
            var ds = new DataSheet
            {
                SheetKey = "schp:" + vs.UniqueId,
                Title = vs.Name + " (view)",
                Source = SheetSource.SchedulePresentation,
                ReadOnly = true
            };
            ds.Notes.Add("Presentation export of '" + vs.Name + "': text as displayed by Revit (grouping, totals, formatting as text). " +
                         "Cell merges, fonts, colours and graphics are not reproduced. Read-only; cannot be imported.");
            TableData td = vs.GetTableData();
            int headerRows = 0;
            foreach (SectionType st in new[] { SectionType.Header, SectionType.Body })
            {
                TableSectionData sec;
                try { sec = td.GetSectionData(st); }
                catch (Exception) { continue; }
                if (sec == null) continue;
                int r0 = sec.FirstRowNumber, c0 = sec.FirstColumnNumber;
                for (int r = r0; r < r0 + sec.NumberOfRows; r++)
                {
                    var line = new string[sec.NumberOfColumns];
                    for (int c = c0; c < c0 + sec.NumberOfColumns; c++)
                    {
                        string text;
                        try { text = vs.GetCellText(st, r, c); }
                        catch (Exception) { text = string.Empty; }
                        line[c - c0] = text ?? string.Empty;
                    }
                    ds.PresentationGrid.Add(line);
                    if (st == SectionType.Header) headerRows++;
                }
                if (st == SectionType.Body && vs.Definition.ShowHeaders && sec.NumberOfRows > 0) headerRows++;
            }
            ds.PresentationHeaderRows = headerRows;
            return ds;
        }
    }
}
