using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace CausaFix.Revit
{
    /// <summary>
    /// Version differences in one place.
    ///  - ElementId(long) and ElementId.Value exist from Revit 2024; ElementId(int)/IntegerValue were removed in 2026.
    ///    CausaFix supports 2024–2026, so it uses the 64-bit API everywhere.
    ///  - Document.CreationGUID is documented for 2025+. It is read by reflection so the 2024 build
    ///    works whether or not the property exists in that year's API; when absent the identity
    ///    check falls back to the weaker rules (and asks for confirmation).
    /// </summary>
    public static class RevitCompat
    {
        public static long Value(ElementId id) => id == null ? -1 : id.Value;

        public static ElementId Id(long value) => new ElementId(value);

        public static bool IsValid(ElementId id) => id != null && id != ElementId.InvalidElementId;

        private static readonly PropertyInfo CreationGuidProperty = typeof(Document).GetProperty("CreationGUID", BindingFlags.Public | BindingFlags.Instance);

        public static bool HasCreationGuid => CreationGuidProperty != null;

        public static string CreationGuid(Document doc)
        {
            if (CreationGuidProperty == null) return string.Empty;
            try
            {
                var v = CreationGuidProperty.GetValue(doc);
                return v is Guid g && g != Guid.Empty ? g.ToString("D") : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string RevitVersion(Autodesk.Revit.ApplicationServices.Application app) => app?.VersionNumber ?? string.Empty;
    }
}
