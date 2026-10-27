using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CausaFix.Revit
{
    /// <summary>
    /// Select / show / isolate host elements from the table. Isolation is always the temporary
    /// Hide/Isolate mode of the active view (reset with "Reset temporary isolate"); CausaFix never hides
    /// elements permanently and never switches views silently.
    /// </summary>
    public static class ViewActions
    {
        private static List<ElementId> HostIds(Document doc, IEnumerable<string> uniqueIds, out int missing)
        {
            var ids = new List<ElementId>();
            missing = 0;
            foreach (var uid in uniqueIds.Distinct())
            {
                if (string.IsNullOrEmpty(uid) || uid.StartsWith("L|", StringComparison.Ordinal) || uid.StartsWith("catstyle:", StringComparison.Ordinal)) { missing++; continue; }
                Element e = doc.GetElement(uid);
                if (e == null) { missing++; continue; }
                ids.Add(e.Id);
            }
            return ids;
        }

        public static string Select(UIDocument uidoc, IEnumerable<string> uniqueIds)
        {
            var ids = HostIds(uidoc.Document, uniqueIds, out int missing);
            uidoc.Selection.SetElementIds(ids);
            return ids.Count + " element(s) selected in Revit" + (missing > 0 ? "; " + missing + " not selectable (linked, deleted or not an element)." : ".");
        }

        public static string Show(UIDocument uidoc, IEnumerable<string> uniqueIds)
        {
            var ids = HostIds(uidoc.Document, uniqueIds, out int missing);
            if (ids.Count == 0) return "Nothing to show" + (missing > 0 ? " (linked, deleted or not an element)." : ".");
            uidoc.Selection.SetElementIds(ids);
            uidoc.ShowElements(ids);
            return "Zoomed to " + ids.Count + " element(s). Revit may open a suitable view if the active one cannot show them.";
        }

        public static string IsolateTemporary(UIDocument uidoc, IEnumerable<string> uniqueIds)
        {
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;
            if (view == null || !view.CanUseTemporaryVisibilityModes())
                return "The active view does not support temporary isolate. Open a plan, section or 3D view.";
            var ids = HostIds(doc, uniqueIds, out int missing);
            if (ids.Count == 0) return "Nothing to isolate.";
            using (var t = new Transaction(doc, "CausaFixIt: temporary isolate"))
            {
                t.Start();
                view.IsolateElementsTemporary(ids);
                t.Commit();
            }
            uidoc.Selection.SetElementIds(ids);
            return "Temporarily isolated " + ids.Count + " element(s) in '" + view.Name + "'. Use Reset Isolate (or Revit's Reset Temporary Hide/Isolate) to restore.";
        }

        public static string ResetIsolate(UIDocument uidoc)
        {
            View view = uidoc.ActiveView;
            if (view == null || !view.CanUseTemporaryVisibilityModes()) return "The active view has no temporary isolate.";
            using (var t = new Transaction(uidoc.Document, "CausaFixIt: reset temporary isolate"))
            {
                t.Start();
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                t.Commit();
            }
            return "Temporary isolate reset in '" + view.Name + "'.";
        }
    }
}
