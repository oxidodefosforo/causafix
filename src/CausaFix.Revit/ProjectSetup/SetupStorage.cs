using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace CausaFix.Revit.ProjectSetup
{
    /// <summary>
    /// Associations between Project Setup rows and the views/sheets they generated, stored on the elements with
    /// Extensible Storage (travels with the model, survives renames). Names are never used to recognise elements.
    ///
    /// Schema "CausaFixProjectSetup" (GUID below, never change it): RowKey (string), Kind ("View"|"Sheet"), Version (string).
    /// Read and write access are Public so the data can be inspected; it contains only CausaFix row keys.
    /// </summary>
    public static class SetupStorage
    {
        public static readonly Guid SchemaGuid = new Guid("3F8C6D1E-5A47-4B2B-9E0C-CA05AF1C5E70");
        private const string FieldRowKey = "RowKey";
        private const string FieldKind = "Kind";
        private const string FieldVersion = "Version";
        public const string KindView = "View";
        public const string KindSheet = "Sheet";

        public static Schema GetOrCreate()
        {
            Schema s = Schema.Lookup(SchemaGuid);
            if (s != null) return s;
            var b = new SchemaBuilder(SchemaGuid);
            b.SetSchemaName("CausaFixProjectSetup");
            b.SetDocumentation("CausaFix Project Setup: row key a view or sheet was generated for.");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(FieldRowKey, typeof(string));
            b.AddSimpleField(FieldKind, typeof(string));
            b.AddSimpleField(FieldVersion, typeof(string));
            return b.Finish();
        }

        /// <summary>Row key stored on the element, or empty. Does not create the schema.</summary>
        public static string ReadRowKey(Element e)
        {
            Schema s = Schema.Lookup(SchemaGuid);
            if (s == null || e == null) return string.Empty;
            try
            {
                Entity ent = e.GetEntity(s);
                if (ent == null || !ent.IsValid()) return string.Empty;
                return ent.Get<string>(FieldRowKey) ?? string.Empty;
            }
            catch (Exception) { return string.Empty; }
        }

        /// <summary>Must run inside a transaction.</summary>
        public static void Write(Element e, string rowKey, string kind, string version)
        {
            Schema s = GetOrCreate();
            var ent = new Entity(s);
            ent.Set<string>(FieldRowKey, rowKey ?? string.Empty);
            ent.Set<string>(FieldKind, kind);
            ent.Set<string>(FieldVersion, version ?? string.Empty);
            e.SetEntity(ent);
        }
    }
}
