using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using CausaFix.ProjectSetup.Model;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.Revit.ProjectSetup
{
    /// <summary>
    /// Parameters as name sources: stable identity (built-in id, shared GUID, project ParameterElement) and a display
    /// value. Reading a value never requires the parameter to be writable (read-only parameters are valid sources).
    /// </summary>
    internal static class ParamReader
    {
        public static ParamRef RefOf(Document doc, Parameter p, ParamScope scope)
        {
            var r = new ParamRef { Scope = scope, Name = p.Definition?.Name ?? "" };
            if (p.IsShared) { r.Kind = ParamKind.Shared; r.Guid = p.GUID.ToString("D"); return r; }
            long id = RevitCompat.Value(p.Id);
            if (id < 0) { r.Kind = ParamKind.BuiltIn; r.BuiltInId = id; return r; }
            r.Kind = ParamKind.Project;
            r.ElementUniqueId = doc.GetElement(p.Id)?.UniqueId ?? ("id:" + id);
            return r;
        }

        public static string ValueOf(Document doc, Parameter p)
        {
            if (p == null || !p.HasValue) return string.Empty;
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? string.Empty;
                case StorageType.ElementId:
                    {
                        string s = p.AsValueString();
                        if (!string.IsNullOrEmpty(s)) return s;
                        var e = doc.GetElement(p.AsElementId());
                        return e?.Name ?? string.Empty;
                    }
                case StorageType.Integer:
                    return p.AsValueString() ?? p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.Double:
                    return p.AsValueString() ?? p.AsDouble().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                default: return string.Empty;
            }
        }

        public static Parameter Find(Document doc, Element e, ParamRef r)
        {
            if (e == null || r == null) return null;
            switch (r.Kind)
            {
                case ParamKind.BuiltIn: return e.get_Parameter((BuiltInParameter)(int)r.BuiltInId);
                case ParamKind.Shared:
                    return Guid.TryParse(r.Guid, out Guid g) ? e.get_Parameter(g) : null;
                default:
                    var pe = string.IsNullOrEmpty(r.ElementUniqueId) ? null : doc.GetElement(r.ElementUniqueId) as ParameterElement;
                    var def = pe?.GetDefinition();
                    return def == null ? null : e.get_Parameter(def);
            }
        }

        /// <summary>Adds the element's parameter definitions (and, when <paramref name="values"/> is given, their values).</summary>
        public static void Collect(Document doc, Element e, ParamScope scope, bool isType, Dictionary<string, ParamInfo> defs, Dictionary<string, string> values)
        {
            if (e == null) return;
            foreach (Parameter p in e.Parameters)
            {
                try
                {
                    if (p?.Definition == null) continue;
                    var r = RefOf(doc, p, scope);
                    if (defs != null && !defs.ContainsKey(r.Key))
                        defs[r.Key] = new ParamInfo { Ref = r, Name = r.Name, IsReadOnly = p.IsReadOnly, StorageType = p.StorageType.ToString(), IsType = isType };
                    if (values != null) values[r.IdentityKey] = ValueOf(doc, p);
                }
                catch (Exception) { /* one unreadable parameter never blocks the others */ }
            }
        }

        /// <summary>Values of the requested parameters on one element (absent parameters are not added).</summary>
        public static void Read(Document doc, Element e, IEnumerable<ParamRef> wanted, Dictionary<string, string> into)
        {
            if (e == null) return;
            foreach (var r in wanted)
            {
                try
                {
                    var p = Find(doc, e, r);
                    if (p != null) into[r.IdentityKey] = ValueOf(doc, p);
                }
                catch (Exception) { }
            }
        }

        private static BuiltInParameter? _showLabel;
        private static bool _showLabelResolved;

        /// <summary>The viewport type "Show Title" parameter, when this Revit version exposes it as a built-in parameter.</summary>
        public static Parameter ShowTitle(Element viewportType)
        {
            if (!_showLabelResolved)
            {
                _showLabelResolved = true;
                if (Enum.TryParse("VIEWPORT_ATTR_SHOW_LABEL", out BuiltInParameter b)) _showLabel = b;
            }
            return _showLabel.HasValue ? viewportType?.get_Parameter(_showLabel.Value) : null;
        }
    }
}
