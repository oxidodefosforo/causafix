using CausaFix.Core.Xlsx;

namespace CausaFix.Core.Workbook
{
    /// <summary>
    /// Layout of a CausaFix workbook (schema version 1). Documented in docs/FORMATO_LIBRO.md.
    ///
    ///  Instructions      visible  – legend, units and editing rules.
    ///  &lt;data sheets&gt;     visible  – row 1 (hidden): technical keys; row 2: headers; data from row 3.
    ///                               column A (hidden): row key (UniqueId); A1 = "CFSHEET|&lt;sheetKey&gt;".
    ///  CF_O1..n          veryHidden – original values per row/column key (three-state comparison).
    ///  CF_Meta           veryHidden – schema, document identity, sheet and column metadata.
    ///
    /// Technical keys, never positions or visible header text, drive the mapping: users may sort rows,
    /// rename headers or reorder columns.
    /// </summary>
    public static class WorkbookSchema
    {
        /// <summary>2: parameter columns are never exported as read-only (raw originals for every parameter cell). 1 is still read.</summary>
        public const int Version = 2;
        public const string ToolName = "CausaFixIt Excel Link";

        public const string InstructionsSheet = "Instructions";
        public const string MetaSheet = "CF_Meta";
        public const string ListsSheet = "CF_Lists";
        public const string ShadowPrefix = "CF_O";

        public const string SheetMarker = "CFSHEET|";
        public const string ColumnMarker = "CFCOL|";
        public const string ShadowMarker = "CFSHADOW|";
        public const string PresentationMarker = "CFPRES|";
        /// <summary>Prefix in the shadow sheet for values of read-only columns: the text written to the cell.</summary>
        public const string WrittenPrefix = "w:";

        public const int KeyRow = 1;
        public const int HeaderRow = 2;
        public const int FirstDataRow = 3;
        public const int KeyColumn = 1;

        // Shadow sheet fixed columns
        public const int ShadowElementIdCol = 2;
        public const int ShadowUniqueIdCol = 3;
        public const int ShadowTypeUidCol = 4;
        public const int ShadowDocKeyCol = 5;
        public const int ShadowLinkedCol = 6;
        public const int ShadowFirstValueCol = 7;

        // Colours (ARGB). Colour is never the only signal: headers carry (T)/(RO) tags and the legend explains them.
        public const string GreenInstance = "FFE2F0D9";
        public const string YellowType = "FFFFF2CC";
        public const string RedReadOnly = "FFF8D7DA";
        public const string GrayNotApplicable = "FFEDEDED";
        public const string ContextFill = "FFF5F5F5";
        public const string HeaderFill = "FF23394D";
        public const string TechnicalFill = "FFD9D9D9";
        public const string TypeBorder = "FFE0A800";
        public const string BrandTab = "FF0E7C86";

        public const string TypeTag = " (T)";
        public const string ReadOnlyTag = " (RO)";
        public const string NotApplicableText = "N/A";

        public static class Styles
        {
            public static readonly XStyle Header = new XStyle { Bold = true, FontRgb = "FFFFFFFF", FillRgb = HeaderFill, Wrap = true, ThinBorder = true };
            public static readonly XStyle Technical = new XStyle { Italic = true, FontRgb = "FF7F7F7F", FillRgb = TechnicalFill, FontSize = 8, NumFmtId = 49 };
            public static readonly XStyle Context = new XStyle { FillRgb = ContextFill, FontRgb = "FF404040", ThinBorder = true, NumFmtId = 49 };
            public static readonly XStyle InstanceText = new XStyle { FillRgb = GreenInstance, ThinBorder = true, NumFmtId = 49, Locked = false };
            public static readonly XStyle InstanceNumber = new XStyle { FillRgb = GreenInstance, ThinBorder = true, Locked = false };
            public static readonly XStyle TypeText = new XStyle { FillRgb = YellowType, ThinBorder = true, NumFmtId = 49, Locked = false };
            public static readonly XStyle TypeNumber = new XStyle { FillRgb = YellowType, ThinBorder = true, Locked = false };
            public static readonly XStyle ReadOnly = new XStyle { FillRgb = RedReadOnly, FontRgb = "FF7F2F35", ThinBorder = true, NumFmtId = 49 };
            public static readonly XStyle ReadOnlyNumber = new XStyle { FillRgb = RedReadOnly, FontRgb = "FF7F2F35", ThinBorder = true };
            /// <summary>Type + read-only: red fill with a thick amber left border.</summary>
            public static readonly XStyle TypeReadOnly = new XStyle { FillRgb = RedReadOnly, FontRgb = "FF7F2F35", ThinBorder = true, LeftBorderRgb = TypeBorder, NumFmtId = 49 };
            public static readonly XStyle TypeReadOnlyNumber = new XStyle { FillRgb = RedReadOnly, FontRgb = "FF7F2F35", ThinBorder = true, LeftBorderRgb = TypeBorder };
            public static readonly XStyle NotApplicable = new XStyle { FillRgb = GrayNotApplicable, FontRgb = "FF8C8C8C", Italic = true, ThinBorder = true, NumFmtId = 49 };
            public static readonly XStyle Title = new XStyle { Bold = true, FontSize = 14, FontRgb = "FF0E7C86" };
            public static readonly XStyle Bold = new XStyle { Bold = true };
            public static readonly XStyle Wrap = new XStyle { Wrap = true };
            public static readonly XStyle PresentationHeader = new XStyle { Bold = true, FillRgb = TechnicalFill, ThinBorder = true, NumFmtId = 49 };
            public static readonly XStyle PresentationBody = new XStyle { ThinBorder = true, NumFmtId = 49 };
            public static XStyle Swatch(string rgb, string border = null) => new XStyle { FillRgb = rgb, ThinBorder = true, LeftBorderRgb = border };
        }
    }
}
