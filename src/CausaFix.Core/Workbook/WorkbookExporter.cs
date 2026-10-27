using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CausaFix.Core.Model;
using CausaFix.Core.Xlsx;
using S = CausaFix.Core.Workbook.WorkbookSchema;

namespace CausaFix.Core.Workbook
{
    /// <summary>Builds the CausaFix workbook (see <see cref="WorkbookSchema"/>) from an export package.</summary>
    public static class WorkbookExporter
    {
        public const int MaxCellText = 32767;

        public static void Save(ExportPackage package, string path)
        {
            XlsxWriter.Save(Build(package), path);
        }

        public static XWorkbook Build(ExportPackage pkg)
        {
            if (pkg == null) throw new ArgumentNullException(nameof(pkg));
            var wb = new XWorkbook();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { S.InstructionsSheet, S.MetaSheet };

            var instructions = new XSheet(S.InstructionsSheet) { TabColorRgb = S.BrandTab };
            wb.Sheets.Add(instructions);

            var meta = new XSheet(S.MetaSheet) { State = XSheetState.VeryHidden };
            int metaRow = 1;
            void M(params string[] values)
            {
                for (int i = 0; i < values.Length; i++) meta.Set(metaRow, i + 1, XCell.Str(values[i] ?? string.Empty));
                metaRow++;
            }
            M("schema", S.Version.ToString(CultureInfo.InvariantCulture));
            M("tool", S.ToolName);
            M("addinVersion", pkg.AddinVersion);
            M("revitVersion", pkg.RevitVersion);
            M("exportedUtc", pkg.ExportedUtc.ToString("o", CultureInfo.InvariantCulture));
            M("scope", pkg.ScopeDescription);
            foreach (var kv in pkg.Document.ToPairs()) M(kv.Key, kv.Value);

            var shadows = new List<XSheet>();
            var lists = new XSheet(S.ListsSheet) { State = XSheetState.VeryHidden };
            int listColumn = 0;
            var sheetNames = new Dictionary<DataSheet, string>();
            int shadowIndex = 1;
            foreach (var ds in pkg.Sheets)
            {
                string name = SheetNames.MakeUnique(string.IsNullOrWhiteSpace(ds.Title) ? "Sheet" : ds.Title, used);
                sheetNames[ds] = name;
                if (ds.Source == SheetSource.SchedulePresentation)
                {
                    wb.Sheets.Add(BuildPresentation(ds, name));
                    M("sheet", ds.SheetKey, name, "", ds.Title, ds.Source.ToString(), ds.RowMode.ToString(), "1");
                    continue;
                }
                string shadowName = SheetNames.MakeUnique(S.ShadowPrefix + shadowIndex.ToString(CultureInfo.InvariantCulture), used);
                shadowIndex++;
                wb.Sheets.Add(BuildDataSheet(ds, name, lists, ref listColumn));
                shadows.Add(BuildShadow(ds, shadowName));
                M("sheet", ds.SheetKey, name, shadowName, ds.Title, ds.Source.ToString(), ds.RowMode.ToString(), ds.ReadOnly ? "1" : "0");
                foreach (var c in ds.Columns)
                {
                    bool ro = ColumnIsReadOnly(ds, c);
                    M("column", ds.SheetKey, c.Key.ToString(), c.Header, c.Kind.ToString(), c.SpecTypeId, c.UnitTypeId, c.UnitLabel,
                      ro ? "1" : "0", c.ReadOnlyReason, c.DataTypeLabel, c.RefClass ?? "");
                }
            }

            FillInstructions(instructions, pkg, sheetNames);
            wb.Sheets.AddRange(shadows);
            if (listColumn > 0) wb.Sheets.Add(lists);
            wb.Sheets.Add(meta);
            return wb;
        }

        /// <summary>Header text: name [unit] (T) (RO).</summary>
        public static string HeaderText(ColumnDef c, bool sheetReadOnly)
        {
            string h = c.Header ?? c.Key.ToString();
            if (!string.IsNullOrEmpty(c.UnitLabel)) h += " [" + c.UnitLabel + "]";
            if (!c.IsContext && c.Scope == ParamScope.Type) h += S.TypeTag;
            if (!c.IsContext && (c.ColumnReadOnly || sheetReadOnly)) h += S.ReadOnlyTag;
            return h;
        }

        /// <summary>Only context columns and informative sheets are read-only as a whole; parameter cells carry their own state.</summary>
        private static bool ColumnIsReadOnly(DataSheet ds, ColumnDef c) => ds.ReadOnly || c.ColumnReadOnly || c.IsContext;

        private static XSheet BuildDataSheet(DataSheet ds, string name, XSheet lists, ref int listColumn)
        {
            var sh = new XSheet(name);
            int lastCol = ds.Columns.Count + 1;
            int lastRow = S.FirstDataRow + ds.Rows.Count - 1;

            sh.Set(S.KeyRow, S.KeyColumn, XCell.Str(S.SheetMarker + ds.SheetKey, S.Styles.Technical));
            sh.Set(S.HeaderRow, S.KeyColumn, XCell.Str("Row key (UniqueId) – do not edit", S.Styles.Header));
            sh.HiddenRows.Add(S.KeyRow);
            sh.HiddenColumns.Add(S.KeyColumn);
            sh.ColumnWidths[S.KeyColumn] = 40;

            for (int i = 0; i < ds.Columns.Count; i++)
            {
                var c = ds.Columns[i];
                int col = i + 2;
                sh.Set(S.KeyRow, col, XCell.Str(S.ColumnMarker + c.Key, S.Styles.Technical));
                string header = HeaderText(c, ds.ReadOnly);
                sh.Set(S.HeaderRow, col, XCell.Str(header, S.Styles.Header));
                int longest = header.Length;
                foreach (var row in ds.Rows.Take(200))
                {
                    var cv = row.Get(c.Key);
                    int len = cv == null ? 3 : c.Kind == ValueKind.Double || c.Kind == ValueKind.Integer ? 10 : (cv.Raw.Kind == RawKind.Text ? cv.Raw.Text.Length : (cv.DisplayText ?? "").Length);
                    if (len > longest) longest = len;
                }
                sh.ColumnWidths[col] = Math.Max(9, Math.Min(50, longest * 1.05 + 2));
            }

            int r = S.FirstDataRow;
            foreach (var row in ds.Rows)
            {
                sh.Set(r, S.KeyColumn, XCell.Str(row.RowKey, S.Styles.Technical));
                for (int i = 0; i < ds.Columns.Count; i++)
                {
                    var c = ds.Columns[i];
                    var cell = row.Get(c.Key) ?? CellValue.NotApplicable();
                    sh.Set(r, i + 2, MakeCell(ds, row, c, cell));
                }
                r++;
            }

            // Freeze headers and the first context columns (max 3 incl. the hidden key column).
            int frozenCols = 1;
            int idIdx = ds.Columns.FindIndex(c => c.Key == ParamKey.Pseudo(PseudoColumns.ElementId));
            if (idIdx >= 0 && idIdx < 3) frozenCols = idIdx + 2;
            sh.FrozenRows = S.HeaderRow;
            sh.FrozenColumns = frozenCols;
            sh.AutoFilterRange = CellRef.Ref(S.HeaderRow, 1) + ":" + CellRef.Ref(Math.Max(lastRow, S.HeaderRow), lastCol);

            if (ds.Rows.Count > 0 && !ds.ReadOnly)
            {
                // Reference columns: drop-down of "Name [Id]" values kept in the hidden CF_Lists sheet.
                for (int i = 0; i < ds.Columns.Count; i++)
                {
                    var c = ds.Columns[i];
                    if (c.Kind != ValueKind.ElementRef || ColumnIsReadOnly(ds, c) || c.RefCandidates == null || c.RefCandidates.Count == 0) continue;
                    listColumn++;
                    for (int k = 0; k < c.RefCandidates.Count; k++) lists.Set(k + 1, listColumn, XCell.Str(c.RefCandidates[k]));
                    string lc = CellRef.ColumnName(listColumn);
                    string colName = CellRef.ColumnName(i + 2);
                    sh.Validations.Add(new XDataValidationList
                    {
                        Range = colName + S.FirstDataRow + ":" + colName + lastRow,
                        Formula = S.ListsSheet + "!$" + lc + "$1:$" + lc + "$" + c.RefCandidates.Count.ToString(CultureInfo.InvariantCulture),
                        Prompt = "Choose a value (Name [Id]) or type " + Values.ImportOptions.DefaultClearToken,
                        Strict = false
                    });
                }
                for (int i = 0; i < ds.Columns.Count; i++)
                {
                    var c = ds.Columns[i];
                    if (c.Kind != ValueKind.YesNo || ColumnIsReadOnly(ds, c)) continue;
                    string colName = CellRef.ColumnName(i + 2);
                    var v = new XDataValidationList { Range = colName + S.FirstDataRow + ":" + colName + lastRow, Prompt = "Yes / No / " + Values.ImportOptions.DefaultClearToken };
                    v.Items.Add("Yes"); v.Items.Add("No"); v.Items.Add(Values.ImportOptions.DefaultClearToken);
                    sh.Validations.Add(v);
                }
            }
            return sh;
        }

        /// <summary>The text written to Excel for a read-only cell (stored in the shadow sheet to detect edits).</summary>
        public static string WrittenText(XCell c) => c == null ? string.Empty : c.AsText();

        private static XCell MakeCell(DataSheet ds, RowData row, ColumnDef c, CellValue cell)
        {
            if (cell.Raw.IsNotApplicable)
                return XCell.Str(S.NotApplicableText, S.Styles.NotApplicable);

            bool readOnly = ColumnIsReadOnly(ds, c) || !cell.Editable || row.IsLinked;
            bool isType = !c.IsContext && c.Scope == ParamScope.Type;
            XStyle text, number;
            if (c.IsContext) { text = S.Styles.Context; number = S.Styles.Context; }
            else if (readOnly) { text = isType ? S.Styles.TypeReadOnly : S.Styles.ReadOnly; number = isType ? S.Styles.TypeReadOnlyNumber : S.Styles.ReadOnlyNumber; }
            else { text = isType ? S.Styles.TypeText : S.Styles.InstanceText; number = isType ? S.Styles.TypeNumber : S.Styles.InstanceNumber; }

            switch (c.Kind)
            {
                case ValueKind.Context:
                    return string.IsNullOrEmpty(cell.DisplayText) ? XCell.Empty(text) : XCell.Str(Clip(cell.DisplayText), text);
                case ValueKind.ElementRef:
                    {
                        if (cell.Raw.IsEmpty) return XCell.Empty(text);
                        if (cell.Raw.Kind != RawKind.ElementId) return XCell.Str(Clip(cell.DisplayText ?? string.Empty), text);
                        string shown = cell.DisplayText ?? string.Empty;
                        if (!Values.References.TryParse(shown, out long id, out _) || id != cell.Raw.Integer)
                            shown = Values.References.Format(shown, cell.Raw.Integer);
                        return XCell.Str(Clip(shown), text);
                    }
                case ValueKind.Text:
                    if (cell.Raw.IsEmpty) return XCell.Empty(text);
                    return XCell.Str(Clip(cell.Raw.Kind == RawKind.Text ? cell.Raw.Text : cell.DisplayText), text);
                case ValueKind.Integer:
                    if (cell.Raw.IsEmpty) return XCell.Empty(number);
                    return XCell.Num(cell.ExportNumber ?? cell.Raw.Integer, number);
                case ValueKind.YesNo:
                    if (cell.Raw.IsEmpty) return XCell.Empty(text);
                    return XCell.Str(cell.Raw.Integer == 1 ? "Yes" : cell.Raw.Integer == 0 ? "No" : cell.Raw.Integer.ToString(CultureInfo.InvariantCulture), text);
                case ValueKind.Double:
                    if (cell.Raw.IsEmpty) return XCell.Empty(number);
                    if (cell.ExportNumber.HasValue) return XCell.Num(cell.ExportNumber.Value, number);
                    if (string.IsNullOrEmpty(c.UnitTypeId) && cell.Raw.Kind == RawKind.Double) return XCell.Num(cell.Raw.Double, number);
                    throw new InvalidOperationException("Double cell without export number for column " + c.Key);
                default:
                    return XCell.Str(Clip(cell.DisplayText), text);
            }
        }

        private static string Clip(string s) => s == null ? string.Empty : s.Length <= MaxCellText ? s : s.Substring(0, MaxCellText);

        private static XSheet BuildShadow(DataSheet ds, string name)
        {
            var sh = new XSheet(name) { State = XSheetState.VeryHidden };
            sh.Set(1, 1, XCell.Str(S.ShadowMarker + ds.SheetKey));
            sh.Set(1, S.ShadowElementIdCol, XCell.Str("elementId"));
            sh.Set(1, S.ShadowUniqueIdCol, XCell.Str("uniqueId"));
            sh.Set(1, S.ShadowTypeUidCol, XCell.Str("typeUniqueId"));
            sh.Set(1, S.ShadowDocKeyCol, XCell.Str("docKey"));
            sh.Set(1, S.ShadowLinkedCol, XCell.Str("linked"));
            for (int i = 0; i < ds.Columns.Count; i++)
                sh.Set(1, S.ShadowFirstValueCol + i, XCell.Str(S.ColumnMarker + ds.Columns[i].Key));

            int r = 2;
            foreach (var row in ds.Rows)
            {
                sh.Set(r, 1, XCell.Str(row.RowKey));
                // Ids stored as text: no loss beyond Excel's 15-digit precision.
                sh.Set(r, S.ShadowElementIdCol, XCell.Str(row.ElementId.ToString(CultureInfo.InvariantCulture)));
                sh.Set(r, S.ShadowUniqueIdCol, XCell.Str(row.UniqueId ?? string.Empty));
                sh.Set(r, S.ShadowTypeUidCol, XCell.Str(row.TypeUniqueId ?? string.Empty));
                sh.Set(r, S.ShadowDocKeyCol, XCell.Str(row.DocKey ?? DocKeys.Host));
                sh.Set(r, S.ShadowLinkedCol, XCell.Str(row.IsLinked ? "1" : "0"));
                for (int i = 0; i < ds.Columns.Count; i++)
                {
                    var c = ds.Columns[i];
                    var cell = row.Get(c.Key) ?? CellValue.NotApplicable();
                    string enc;
                    if (cell.Raw.IsNotApplicable) enc = cell.Raw.Encode();
                    else if (ColumnIsReadOnly(ds, c)) enc = S.WrittenPrefix + WrittenText(MakeCell(ds, row, c, cell));
                    else enc = cell.Raw.Encode();
                    sh.Set(r, S.ShadowFirstValueCol + i, XCell.Str(Clip(enc)));
                }
                r++;
            }
            return sh;
        }

        private static XSheet BuildPresentation(DataSheet ds, string name)
        {
            var sh = new XSheet(name);
            sh.Set(1, 1, XCell.Str(S.SheetMarker + ds.SheetKey, S.Styles.Technical));
            sh.Set(1, 2, XCell.Str(S.PresentationMarker + "read-only presentation export – cannot be imported", S.Styles.Technical));
            sh.HiddenRows.Add(1);
            int r = 2;
            int maxCols = 1;
            for (int i = 0; i < ds.PresentationGrid.Count; i++)
            {
                var line = ds.PresentationGrid[i];
                var style = i < ds.PresentationHeaderRows ? S.Styles.PresentationHeader : S.Styles.PresentationBody;
                for (int c = 0; c < line.Length; c++)
                    sh.Set(r, c + 1, string.IsNullOrEmpty(line[c]) ? XCell.Empty(style) : XCell.Str(Clip(line[c]), style));
                maxCols = Math.Max(maxCols, line.Length);
                r++;
            }
            for (int c = 1; c <= maxCols; c++) sh.ColumnWidths[c] = 18;
            if (ds.PresentationHeaderRows > 0) sh.FrozenRows = 1 + ds.PresentationHeaderRows;
            return sh;
        }

        private static void FillInstructions(XSheet sh, ExportPackage pkg, Dictionary<DataSheet, string> names)
        {
            sh.ColumnWidths[1] = 26;
            sh.ColumnWidths[2] = 100;
            int r = 1;
            void Line(string a, string b = null, XStyle sa = null, XStyle sb = null)
            {
                if (a != null) sh.Set(r, 1, XCell.Str(a, sa));
                if (b != null) sh.Set(r, 2, XCell.Str(b, sb ?? S.Styles.Wrap));
                r++;
            }

            Line("CausaFixIt Excel Link", null, S.Styles.Title);
            Line("Model", pkg.Document.Title);
            Line("Exported (UTC)", pkg.ExportedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            Line("Revit / add-in", pkg.RevitVersion + " / " + pkg.AddinVersion);
            Line("Scope", pkg.ScopeDescription);
            r++;
            Line("Legend", null, S.Styles.Bold);
            sh.Set(r, 1, XCell.Str("Instance – editable", S.Styles.Swatch(S.GreenInstance))); sh.Set(r++, 2, XCell.Str("Green. Parameter of the element itself.", S.Styles.Wrap));
            sh.Set(r, 1, XCell.Str("Type – editable (T)", S.Styles.Swatch(S.YellowType))); sh.Set(r++, 2, XCell.Str("Yellow. Type parameter: changing it affects EVERY instance of that type in the model, including instances not exported.", S.Styles.Wrap));
            sh.Set(r, 1, XCell.Str("Read-only (RO)", S.Styles.Swatch(S.RedReadOnly))); sh.Set(r++, 2, XCell.Str("Red. Calculated, formula-driven, reference or locked values. Edits are reported and never written.", S.Styles.Wrap));
            sh.Set(r, 1, XCell.Str("Type + read-only (T)(RO)", S.Styles.Swatch(S.RedReadOnly, S.TypeBorder))); sh.Set(r++, 2, XCell.Str("Red with an amber left border.", S.Styles.Wrap));
            sh.Set(r, 1, XCell.Str("N/A", S.Styles.NotApplicable)); sh.Set(r++, 2, XCell.Str("Grey. The parameter does not exist on that element. N/A is not empty and not zero.", S.Styles.Wrap));
            sh.Set(r, 1, XCell.Str("Context", S.Styles.Context)); sh.Set(r++, 2, XCell.Str("Light grey. Category, family, type, ElementId: reference only.", S.Styles.Wrap));
            r++;
            Line("Editing rules", null, S.Styles.Bold);
            Line("Identity", "Rows are matched by the hidden row key (UniqueId) and columns by hidden technical keys. You may sort, filter, reorder columns and rename the visible headers. Do not delete hidden row 1 or hidden column A; do not edit or copy keys between rows.");
            Line("Empty cells", "An empty cell is ignored: the model keeps its value. To erase a value type " + Values.ImportOptions.DefaultClearToken + " in the cell.");
            Line("Numbers and units", "Numbers are in the unit shown in the header, e.g. [mm], [V], [VA], recorded at export. They are read in that unit even if the project units change later. Type plain numbers; for text cells avoid thousands separators – ambiguous input such as 1,234 is rejected.");
            Line("Yes/No", "Use Yes or No.");
            Line("Formulas", "Formulas are allowed only if the workbook was saved by Excel with their results. The saved result is imported, never the formula. Formulas without a saved result are blocked.");
            Line("Rows and columns", "Deleting rows does not delete elements. New rows do not create elements. New columns do not create parameters. All of them are ignored and reported.");
            Line("Review", "Importing never writes directly: CausaFixIt compares the exported value, your value and the current model value, shows changes and conflicts, and writes only what you approve. Changes made in Revit after export are never overwritten silently.");
            Line("Linked models", "Rows from linked models are read-only.");
            r++;
            Line("Sheets", null, S.Styles.Bold);
            foreach (var ds in pkg.Sheets)
            {
                string desc = ds.Source + (ds.Source == SheetSource.SchedulePresentation ? " – presentation only, not importable" : ds.ReadOnly ? " – read-only" : "") +
                              " – " + ds.Rows.Count.ToString(CultureInfo.InvariantCulture) + " rows";
                if (ds.Notes.Count > 0) desc += ". " + string.Join(" ", ds.Notes);
                Line(names[ds], desc);
            }
            r++;
            Line("Units by column", null, S.Styles.Bold);
            foreach (var ds in pkg.Sheets)
            {
                var withUnits = ds.Columns.Where(c => !string.IsNullOrEmpty(c.UnitLabel)).ToList();
                if (withUnits.Count == 0) continue;
                Line(names[ds], string.Join("; ", withUnits.Select(c => c.Header + " [" + c.UnitLabel + "]")));
            }
        }
    }
}
