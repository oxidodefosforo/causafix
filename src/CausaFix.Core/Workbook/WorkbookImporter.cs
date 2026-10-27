using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Values;
using CausaFix.Core.Xlsx;
using S = CausaFix.Core.Workbook.WorkbookSchema;

namespace CausaFix.Core.Workbook
{
    public sealed class ImportedSheet
    {
        public string SheetKey { get; set; }
        public string Title { get; set; }
        public string WorksheetName { get; set; }
        public SheetSource Source { get; set; }
        public RowMode RowMode { get; set; }
        public bool ReadOnly { get; set; }
        public bool Usable { get; set; }
        public List<ColumnDef> Columns { get; } = new List<ColumnDef>();
        public int RowsRead { get; set; }
    }

    public sealed class ImportedWorkbook
    {
        public int SchemaVersion { get; set; }
        public bool IsCausaFixWorkbook { get; set; }
        public Dictionary<string, string> Meta { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public DocumentIdentity Document { get; set; }
        public List<ImportedSheet> Sheets { get; } = new List<ImportedSheet>();
        public List<ProposedCell> Cells { get; } = new List<ProposedCell>();
        public List<ImportIssue> Issues { get; } = new List<ImportIssue>();
        /// <summary>True when the workbook cannot be used at all (not CausaFix, unsupported schema...).</summary>
        public bool Fatal { get; set; }
        /// <summary>All row target UniqueIds referenced by the workbook (host rows).</summary>
        public HashSet<string> ReferencedUniqueIds { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads a CausaFix workbook into proposals. Validates schema and technical keys; never trusts
    /// row positions or visible headers. Does not touch Revit: comparison happens in ChangeSetBuilder.
    /// </summary>
    public static class WorkbookImporter
    {
        public static ImportedWorkbook Read(XWorkbook wb, IUnitConverter units, ImportOptions options)
        {
            options = options ?? new ImportOptions();
            var result = new ImportedWorkbook();
            var meta = wb.Find(S.MetaSheet);
            if (meta == null)
            {
                result.Fatal = true;
                result.Issues.Add(new ImportIssue(IssueSeverity.Error, "", "This is not a CausaFixIt workbook (metadata sheet missing). Only workbooks exported by CausaFixIt (or CausaFix) Excel Link can be imported."));
                return result;
            }
            result.IsCausaFixWorkbook = true;

            var sheetRows = new List<string[]>();
            var colRows = new List<string[]>();
            foreach (var kv in meta.Rows)
            {
                var values = kv.Value.ToDictionary(x => x.Key, x => x.Value.AsText());
                string a = values.TryGetValue(1, out string av) ? av : "";
                if (a == "sheet") sheetRows.Add(Row(values, 8));
                else if (a == "column") colRows.Add(Row(values, 12));
                else if (a.Length > 0) result.Meta[a] = values.TryGetValue(2, out string bv) ? bv : "";
            }

            if (!result.Meta.TryGetValue("schema", out string schemaText) || !int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema))
            {
                result.Fatal = true;
                result.Issues.Add(new ImportIssue(IssueSeverity.Error, S.MetaSheet, "Schema version missing or damaged."));
                return result;
            }
            result.SchemaVersion = schema;
            if (schema < 1 || schema > S.Version)
            {
                result.Fatal = true;
                result.Issues.Add(new ImportIssue(IssueSeverity.Error, "", "The workbook uses schema " + schema + ", newer than this add-in supports (" + S.Version + "). Update CausaFix."));
                return result;
            }
            result.Document = DocumentIdentity.FromPairs(result.Meta);

            // Locate data sheets by their key marker, not by name (users may rename tabs).
            var byMarker = new Dictionary<string, XSheet>(StringComparer.Ordinal);
            var markerColumn = new Dictionary<XSheet, int>();
            foreach (var sh in wb.Sheets)
            {
                if (sh.State == XSheetState.VeryHidden) continue;
                for (int c = 1; c <= Math.Min(sh.MaxColumn, 200); c++)
                {
                    string t = sh.GetText(S.KeyRow, c);
                    if (t.StartsWith(S.SheetMarker, StringComparison.Ordinal))
                    {
                        string key = t.Substring(S.SheetMarker.Length);
                        if (byMarker.ContainsKey(key))
                        {
                            result.Issues.Add(new ImportIssue(IssueSeverity.Error, sh.Name, "Duplicate sheet key (sheet copied inside the workbook?). Both copies are ignored."));
                            byMarker[key] = null;
                        }
                        else byMarker[key] = sh;
                        markerColumn[sh] = c;
                        break;
                    }
                }
            }

            foreach (var sr in sheetRows)
            {
                var info = new ImportedSheet
                {
                    SheetKey = sr[1],
                    WorksheetName = sr[2],
                    Title = sr[4],
                    Source = Enum.TryParse(sr[5], out SheetSource src) ? src : SheetSource.ModelCategories,
                    RowMode = Enum.TryParse(sr[6], out RowMode rm) ? rm : RowMode.Instance,
                    ReadOnly = sr[7] == "1"
                };
                string shadowName = sr[3];
                foreach (var cr in colRows.Where(x => x[1] == info.SheetKey))
                {
                    if (!ParamKey.TryParse(cr[2], out ParamKey pk))
                    {
                        result.Issues.Add(new ImportIssue(IssueSeverity.Error, info.Title, "Damaged column key in metadata: " + cr[2]));
                        continue;
                    }
                    info.Columns.Add(new ColumnDef
                    {
                        Key = pk,
                        Header = cr[3],
                        Kind = Enum.TryParse(cr[4], out ValueKind vk) ? vk : ValueKind.Context,
                        SpecTypeId = cr[5],
                        UnitTypeId = cr[6],
                        UnitLabel = cr[7],
                        ColumnReadOnly = cr[8] == "1",
                        ReadOnlyReason = cr[9],
                        DataTypeLabel = cr[10],
                        RefClass = cr[11]
                    });
                }
                result.Sheets.Add(info);

                if (info.Source == SheetSource.SchedulePresentation)
                {
                    result.Issues.Add(new ImportIssue(IssueSeverity.Info, info.Title, "Presentation export: read-only, not imported."));
                    continue;
                }
                if (!byMarker.TryGetValue(info.SheetKey, out XSheet data) || data == null)
                {
                    result.Issues.Add(new ImportIssue(IssueSeverity.Error, info.Title,
                        "Sheet not found by its technical key (hidden cell A1 deleted or the sheet was removed). Nothing from this sheet is imported. Restore the sheet from the original export."));
                    continue;
                }
                info.WorksheetName = data.Name;
                var shadow = wb.Find(shadowName);
                if (shadow == null || shadow.GetText(1, 1) != S.ShadowMarker + info.SheetKey)
                {
                    result.Issues.Add(new ImportIssue(IssueSeverity.Error, data.Name, "Original values for this sheet are missing; the three-state comparison is impossible. Nothing from this sheet is imported."));
                    continue;
                }
                ReadSheet(result, info, data, markerColumn[data], shadow, units, options);
            }
            return result;
        }

        /// <summary>Unit ForgeTypeIds recorded in the workbook metadata (needed before parsing numbers).</summary>
        public static List<string> UnitIds(XWorkbook wb)
        {
            var units = new HashSet<string>(StringComparer.Ordinal);
            var meta = wb.Find(S.MetaSheet);
            if (meta == null) return new List<string>();
            foreach (var kv in meta.Rows)
            {
                if (meta.GetText(kv.Key, 1) != "column") continue;
                string u = meta.GetText(kv.Key, 7);
                if (u.Length > 0) units.Add(u);
            }
            return units.ToList();
        }

        private static string[] Row(Dictionary<int, string> values, int n)
        {
            var arr = new string[n];
            for (int i = 0; i < n; i++) arr[i] = values.TryGetValue(i + 1, out string v) ? v : "";
            return arr;
        }

        private sealed class ShadowRow
        {
            public int Row;
            public string ElementId, UniqueId, TypeUid, DocKey;
            public bool Linked;
        }

        private static void ReadSheet(ImportedWorkbook result, ImportedSheet info, XSheet data, int keyCol, XSheet shadow,
                                      IUnitConverter units, ImportOptions options)
        {
            string where = "'" + data.Name + "'";

            // --- column keys in the data sheet
            var dataCols = new Dictionary<ParamKey, int>();
            var dupCols = new HashSet<ParamKey>();
            for (int c = 1; c <= data.MaxColumn; c++)
            {
                string t = data.GetText(S.KeyRow, c);
                if (!t.StartsWith(S.ColumnMarker, StringComparison.Ordinal))
                {
                    if (c != keyCol && t.Length == 0 && data.GetText(S.HeaderRow, c).Length > 0 && ColumnHasData(data, c))
                        result.Issues.Add(new ImportIssue(IssueSeverity.Warning, where + " column " + CellRef.ColumnName(c),
                            "Column '" + data.GetText(S.HeaderRow, c) + "' has no CausaFixIt key: ignored (new columns do not create parameters)."));
                    continue;
                }
                if (!ParamKey.TryParse(t.Substring(S.ColumnMarker.Length), out ParamKey k))
                {
                    result.Issues.Add(new ImportIssue(IssueSeverity.Error, where + " column " + CellRef.ColumnName(c), "Damaged column key: column ignored."));
                    continue;
                }
                if (dataCols.ContainsKey(k)) dupCols.Add(k);
                else dataCols[k] = c;
            }
            foreach (var k in dupCols)
            {
                dataCols.Remove(k);
                result.Issues.Add(new ImportIssue(IssueSeverity.Error, where, "Column key " + k + " appears more than once (column copied?). That column is ignored."));
            }

            // --- shadow columns
            var shadowCols = new Dictionary<ParamKey, int>();
            for (int c = S.ShadowFirstValueCol; c <= shadow.MaxColumn; c++)
            {
                string t = shadow.GetText(1, c);
                if (t.StartsWith(S.ColumnMarker, StringComparison.Ordinal) && ParamKey.TryParse(t.Substring(S.ColumnMarker.Length), out ParamKey k))
                    shadowCols[k] = c;
            }

            var activeCols = new List<(ColumnDef def, int dataCol, int shadowCol)>();
            int migratedCells = 0;
            foreach (var colDef in info.Columns)
            {
                var def = colDef;
                if (!info.ReadOnly && !def.IsContext && def.ColumnReadOnly)
                {
                    // Parameter columns are never read-only as a whole: writability is checked live per cell.
                    def = def.Clone();
                    def.ColumnReadOnly = false;
                }
                if (!shadowCols.TryGetValue(def.Key, out int sc)) continue;
                if (!dataCols.TryGetValue(def.Key, out int dc))
                {
                    if (!def.IsContext && !dupCols.Contains(def.Key))
                        result.Issues.Add(new ImportIssue(IssueSeverity.Warning, where,
                            "Column '" + def.Header + "' was removed or its hidden key was deleted: not imported. Its values in the model are kept."));
                    continue;
                }
                activeCols.Add((def, dc, sc));
            }

            // --- shadow rows
            var shadowRows = new Dictionary<string, ShadowRow>(StringComparer.Ordinal);
            foreach (var kv in shadow.Rows)
            {
                if (kv.Key < 2) continue;
                string key = shadow.GetText(kv.Key, 1);
                if (key.Length == 0) continue;
                shadowRows[key] = new ShadowRow
                {
                    Row = kv.Key,
                    ElementId = shadow.GetText(kv.Key, S.ShadowElementIdCol),
                    UniqueId = shadow.GetText(kv.Key, S.ShadowUniqueIdCol),
                    TypeUid = shadow.GetText(kv.Key, S.ShadowTypeUidCol),
                    DocKey = shadow.GetText(kv.Key, S.ShadowDocKeyCol),
                    Linked = shadow.GetText(kv.Key, S.ShadowLinkedCol) == "1"
                };
            }

            int elementIdCol = dataCols.TryGetValue(ParamKey.Pseudo(PseudoColumns.ElementId), out int eic) ? eic : -1;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            int unknown = 0, keyless = 0;
            info.Usable = true;

            foreach (var kv in data.Rows)
            {
                int r = kv.Key;
                if (r < S.FirstDataRow) continue;
                string rowKey = data.GetText(r, keyCol).Trim();
                if (rowKey.Length == 0)
                {
                    if (kv.Value.Any(x => x.Key != keyCol && x.Value.Kind != XCellKind.Blank)) keyless++;
                    continue;
                }
                if (!shadowRows.TryGetValue(rowKey, out ShadowRow sr))
                {
                    unknown++;
                    result.Issues.Add(new ImportIssue(IssueSeverity.Warning, where + " row " + r,
                        "Row key not found in the export (new row, or the hidden key was edited/copied). Row ignored."));
                    continue;
                }
                if (seen.TryGetValue(rowKey, out int firstRow))
                {
                    result.Issues.Add(new ImportIssue(IssueSeverity.Warning, where + " row " + r,
                        "Duplicate of row " + firstRow + " (same element). Both rows are compared; different edits for the same value become a conflict."));
                }
                else seen[rowKey] = r;
                info.RowsRead++;
                if (!sr.Linked && !string.IsNullOrEmpty(sr.UniqueId)) result.ReferencedUniqueIds.Add(sr.UniqueId);

                if (elementIdCol > 0)
                {
                    string shown = data.GetText(r, elementIdCol).Trim();
                    if (shown.Length > 0 && shown != sr.ElementId && !(double.TryParse(shown, NumberStyles.Float, CultureInfo.InvariantCulture, out double sd) && sd.ToString("R", CultureInfo.InvariantCulture) == sr.ElementId))
                        result.Issues.Add(new ImportIssue(IssueSeverity.Warning, where + " row " + r,
                            "The visible ElementId was edited (" + shown + " ≠ " + sr.ElementId + "). It is a reference only; the hidden key identifies the element."));
                }

                long.TryParse(sr.ElementId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long elementId);
                string label = BuildLabel(data, r, info, dataCols);

                foreach (var (def, dc, sc) in activeCols)
                {
                    if (def.IsContext) continue;
                    var cell = data.Get(r, dc);
                    string encoded = shadow.GetText(sr.Row, sc);
                    var pc = new ProposedCell
                    {
                        Origin = ProposalOrigin.Excel,
                        SheetKey = info.SheetKey,
                        SheetTitle = data.Name,
                        ExcelRow = r,
                        ExcelColumn = CellRef.ColumnName(dc),
                        RowKey = rowKey,
                        RowLabel = label,
                        ElementId = elementId,
                        IsLinked = sr.Linked,
                        DocKey = string.IsNullOrEmpty(sr.DocKey) ? DocKeys.Host : sr.DocKey,
                        CellKind = KindOf(cell),
                        Column = def,
                        TargetIsType = def.Scope == ParamScope.Type,
                        TargetUniqueId = def.Scope == ParamScope.Type && info.RowMode == RowMode.Instance ? sr.TypeUid : sr.UniqueId
                    };

                    if (info.ReadOnly)
                    {
                        // Informative sheet (inventories): detect edits by comparing with the text written at export.
                        if (encoded == "na") { if (!IsBlankOrNa(cell)) pc.Proposal = Proposal.Invalid("This sheet is informative only.", cell?.AsText(), BlockCode.ReadOnlyColumn); }
                        else
                        {
                            string written = encoded.StartsWith(S.WrittenPrefix, StringComparison.Ordinal) ? encoded.Substring(S.WrittenPrefix.Length) : encoded;
                            if (!SameWritten(written, cell)) pc.Proposal = Proposal.Invalid("This sheet is informative only.", cell?.AsText() ?? "", BlockCode.ReadOnlyColumn);
                        }
                        if (pc.Proposal == null) continue;
                        pc.Original = RawValue.Empty;
                        result.Cells.Add(pc);
                        continue;
                    }

                    RawValue original;
                    if (encoded.StartsWith(S.WrittenPrefix, StringComparison.Ordinal))
                    {
                        // Schema 1 stored only the displayed text for columns it had (wrongly) classified as read-only.
                        // Recover the original value from that text so the user's edits are compared, not discarded.
                        string written = encoded.Substring(S.WrittenPrefix.Length);
                        if (SameWritten(written, cell)) continue; // not edited
                        if (!TryMigrateOriginal(def, written, units, out original, out string why))
                        {
                            pc.Original = RawValue.Empty;
                            pc.Proposal = Proposal.Invalid("The original value of this cell cannot be recovered from the workbook metadata (" + why +
                                                           "). Re-export this column from CausaFixIt and repeat the edit.", cell?.AsText() ?? "", BlockCode.MetadataMismatch);
                            result.Cells.Add(pc);
                            continue;
                        }
                        pc.OriginalMigrated = true;
                        migratedCells++;
                    }
                    else if (!RawValue.TryDecode(encoded, out original))
                    {
                        result.Issues.Add(new ImportIssue(IssueSeverity.Error, where + " row " + r, "Original value for '" + def.Header + "' is damaged; cell ignored."));
                        continue;
                    }
                    pc.Original = original;

                    if (original.IsNotApplicable && IsBlankOrNa(cell)) continue;
                    pc.Proposal = ProposalParser.FromCell(def, cell, units, options);
                    if (pc.Proposal.Kind == ProposalKind.None && (original.IsEmpty || original.IsNotApplicable)) continue;
                    result.Cells.Add(pc);
                }
            }

            if (migratedCells > 0)
                result.Issues.Add(new ImportIssue(IssueSeverity.Info, where,
                    migratedCells + " edited cell(s) came from columns that an earlier CausaFix version marked read-only. Their original values were recovered from the workbook and they are compared normally."));
            int missing = shadowRows.Count - seen.Count - 0;
            if (missing > 0)
                result.Issues.Add(new ImportIssue(IssueSeverity.Info, where,
                    missing + " exported row(s) are no longer in the sheet. Deleted rows never delete elements; those elements keep their values."));
            if (keyless > 0)
                result.Issues.Add(new ImportIssue(IssueSeverity.Warning, where,
                    keyless + " row(s) without a key were ignored. New rows do not create elements."));
        }

        private static string KindOf(XCell c)
        {
            if (c == null) return "Blank";
            return (c.IsFormula ? "Formula result " : "") + c.Kind;
        }

        /// <summary>Recovers an internal value from the text an older export wrote into a cell.</summary>
        private static bool TryMigrateOriginal(ColumnDef def, string written, IUnitConverter units, out RawValue value, out string why)
        {
            value = RawValue.Empty;
            why = string.Empty;
            if (written.Length == 0) return true;
            switch (def.Kind)
            {
                case ValueKind.Text:
                    value = RawValue.FromText(written);
                    return true;
                case ValueKind.Integer:
                    if (long.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)) { value = RawValue.FromInteger(i); return true; }
                    why = "integer text '" + written + "'";
                    return false;
                case ValueKind.YesNo:
                    if (written == "Yes") { value = RawValue.FromInteger(1); return true; }
                    if (written == "No") { value = RawValue.FromInteger(0); return true; }
                    why = "Yes/No text '" + written + "'";
                    return false;
                case ValueKind.Double:
                    if (double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    {
                        value = RawValue.FromDouble(string.IsNullOrEmpty(def.UnitTypeId) ? d : (units ?? IdentityUnitConverter.Instance).ToInternal(d, def.UnitTypeId));
                        return true;
                    }
                    why = "number text '" + written + "'";
                    return false;
                default:
                    why = "reference columns exported by an older version contain names only, not element ids";
                    return false;
            }
        }

        private static string Reason(ColumnDef def, ImportedSheet info)
            => info.ReadOnly ? "the sheet is informative only." : string.IsNullOrEmpty(def.ReadOnlyReason) ? "value cannot be written." : def.ReadOnlyReason;

        private static bool IsBlankOrNa(XCell c)
            => c == null || (c.Kind == XCellKind.Blank && !c.IsFormula) || (c.Kind == XCellKind.Text && c.Text.Trim() == S.NotApplicableText);

        private static bool SameWritten(string written, XCell cell)
        {
            string now = cell == null ? string.Empty : cell.AsText();
            if (string.Equals(ValueComparer.NormalizeText(written), ValueComparer.NormalizeText(now), StringComparison.Ordinal)) return true;
            if (double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
                cell != null && cell.Kind == XCellKind.Number)
                return ValueComparer.DoublesEqual(a, cell.Number);
            return false;
        }

        private static bool ColumnHasData(XSheet s, int c)
        {
            for (int r = S.FirstDataRow; r <= Math.Min(s.MaxRow, S.FirstDataRow + 500); r++)
                if (s.GetText(r, c).Length > 0) return true;
            return false;
        }

        private static string BuildLabel(XSheet data, int r, ImportedSheet info, Dictionary<ParamKey, int> cols)
        {
            string get(string pseudo) => cols.TryGetValue(ParamKey.Pseudo(pseudo), out int c) ? data.GetText(r, c) : "";
            var parts = new[] { get(PseudoColumns.Category), get(PseudoColumns.Family), get(PseudoColumns.Type) }.Where(p => p.Length > 0).ToList();
            string id = get(PseudoColumns.ElementId);
            string label = string.Join(" : ", parts);
            if (id.Length > 0) label = (label.Length > 0 ? label + " " : "") + "[" + id + "]";
            return label.Length > 0 ? label : info.Title;
        }
    }
}
