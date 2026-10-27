using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using CausaFix.Core.Model;

namespace CausaFix.Revit
{
    /// <summary>Result of resolving a parameter identity on a requested target, with the evidence about its real owner.</summary>
    public sealed class OwnerResolution
    {
        public ParamIdentity.Resolution Resolution { get; set; } = ParamIdentity.Resolution.NotFound;
        /// <summary>The parameter on its real owner (null when not found or ambiguous).</summary>
        public Parameter Parameter { get; set; }
        public Element Target { get; set; }
        /// <summary>Element that owns <see cref="Parameter"/> (Parameter.Element, or the type that holds the identity).</summary>
        public Element Owner { get; set; }
        /// <summary>The identity was requested on <see cref="Target"/> but belongs to another element (normally its type).</summary>
        public bool OwnerMismatch { get; set; }
        /// <summary>Instance and type both expose the identity and the binding cannot tell which one is real.</summary>
        public bool ScopeAmbiguous { get; set; }
        public string Binding { get; set; } = string.Empty;
        /// <summary>What was checked, in plain words (for diagnostics).</summary>
        public string Evidence { get; set; } = string.Empty;
        public bool Found => Resolution == ParamIdentity.Resolution.Found && Parameter != null && !OwnerMismatch && !ScopeAmbiguous;
    }

    /// <summary>
    /// Real owner of a parameter. Revit schedules report every shared parameter as ScheduleFieldType.Instance, "regardless of
    /// whether they are instance or type parameters" (API reference), so a field type is not proof of the owner. The owner is
    /// established from the API: Parameter.Element, whether the element's type exposes the same identity, and the project
    /// binding (InstanceBinding / TypeBinding) for project and shared parameters.
    /// </summary>
    public sealed class ParamOwner
    {
        private readonly Document _doc;
        private readonly Dictionary<long, string> _bindings = new Dictionary<long, string>();
        private bool _bindingsRead;

        public ParamOwner(Document doc) { _doc = doc; }

        public static Element OwnerOf(Parameter p)
        {
            try { return p?.Element; }
            catch (Exception) { return null; }
        }

        /// <summary>Resolves <paramref name="key"/> on <paramref name="target"/> and checks that <paramref name="target"/> really owns it.</summary>
        public OwnerResolution Resolve(Element target, ParamKey key)
        {
            var r = new OwnerResolution { Target = target };
            if (target == null) return r;
            var res = ParamIdentity.Resolve(target, key, out Parameter p);
            r.Resolution = res;
            if (res == ParamIdentity.Resolution.Ambiguous) { r.Evidence = "several parameters with the same name and data type on the element"; return r; }

            Element type = null;
            Parameter onType = null;
            bool canHaveType = !(target is ElementType) && (key.Source == ParamSource.Shared || key.Source == ParamSource.Project || key.Source == ParamSource.Family);
            if (canHaveType)
            {
                ElementId tid = target.GetTypeId();
                type = RevitCompat.IsValid(tid) ? _doc.GetElement(tid) : null;
                if (type != null && ParamIdentity.Resolve(type, key.WithScope(ParamScope.Type), out onType) != ParamIdentity.Resolution.Found) onType = null;
            }

            if (res == ParamIdentity.Resolution.Found)
            {
                Element owner = OwnerOf(p);
                r.Parameter = p;
                r.Owner = owner ?? target;
                r.Binding = BindingOf(p, key);
                if (owner != null && !owner.Id.Equals(target.Id))
                {
                    r.OwnerMismatch = true;
                    r.Evidence = "Parameter.Element is '" + ModelReader.Label(owner, _doc) + "', not the requested element";
                    return r;
                }
                if (onType != null)
                {
                    // Both expose the identity. A project InstanceBinding makes the instance parameter real; anything else
                    // (TypeBinding, or a family parameter with no project binding) means the value lives on the type.
                    if (r.Binding == "InstanceBinding")
                    {
                        r.ScopeAmbiguous = true;
                        r.Evidence = "the instance (project InstanceBinding) and its type '" + ModelReader.Label(type, _doc) + "' both expose this parameter";
                        return r;
                    }
                    r.OwnerMismatch = true;
                    r.Owner = type;
                    r.Parameter = onType;
                    r.Evidence = "the type '" + ModelReader.Label(type, _doc) + "' holds this parameter (binding: " + r.Binding + ")";
                    return r;
                }
                r.Evidence = "Parameter.Element = requested element" + (owner == null ? " (owner not reported by Revit)" : "");
                return r;
            }

            if (onType != null)
            {
                r.Resolution = ParamIdentity.Resolution.Found;
                r.OwnerMismatch = true;
                r.Owner = type;
                r.Parameter = onType;
                r.Binding = BindingOf(onType, key);
                r.Evidence = "not on the element; its type '" + ModelReader.Label(type, _doc) + "' holds this parameter";
            }
            return r;
        }

        /// <summary>
        /// Real scope(s) of a key on an element: Instance when the element owns it, Type when its type does.
        /// Used to classify schedule fields (whose field type does not tell shared type parameters apart).
        /// </summary>
        public ParamScope? RealScope(Element e, ParamKey key)
        {
            var onInstance = Resolve(e, key.WithScope(ParamScope.Instance));
            if (onInstance.Found) return ParamScope.Instance;
            if (onInstance.OwnerMismatch && onInstance.Owner is ElementType) return ParamScope.Type;
            if (onInstance.ScopeAmbiguous) return null;
            Element holder = ParamIdentity.Holder(e, ParamScope.Type);
            if (holder != null && ParamIdentity.Resolve(holder, key.WithScope(ParamScope.Type), out _) == ParamIdentity.Resolution.Found) return ParamScope.Type;
            return null;
        }

        /// <summary>Project binding of a project/shared parameter, or "none" (family parameter) / "" (not applicable).</summary>
        public string BindingOf(Parameter p, ParamKey key)
        {
            if (p == null || (key.Source != ParamSource.Shared && key.Source != ParamSource.Project)) return string.Empty;
            if (!_bindingsRead)
            {
                _bindingsRead = true;
                try
                {
                    DefinitionBindingMapIterator it = _doc.ParameterBindings.ForwardIterator();
                    it.Reset();
                    while (it.MoveNext())
                    {
                        if (!(it.Key is InternalDefinition d)) continue;
                        ElementId id = d.Id;
                        if (!RevitCompat.IsValid(id)) continue;
                        object b = it.Current;
                        _bindings[RevitCompat.Value(id)] = b is TypeBinding ? "TypeBinding" : b is InstanceBinding ? "InstanceBinding" : b?.GetType().Name ?? "unknown";
                    }
                }
                catch (Exception ex) { _bindings[long.MinValue] = "not readable (" + ex.GetType().Name + ")"; }
            }
            if (!RevitCompat.IsValid(p.Id)) return "none";
            return _bindings.TryGetValue(RevitCompat.Value(p.Id), out string s) ? s
                 : _bindings.TryGetValue(long.MinValue, out string err) ? err
                 : "none (not bound in the project: defined in the family)";
        }

        /// <summary>
        /// What the API lets us check about a read-only parameter, kept apart from what it cannot tell. Never claims a formula.
        /// </summary>
        public string ReadOnlyEvidence(Parameter p, Element owner)
        {
            var parts = new List<string>();
            parts.Add("IsReadOnly=true on " + (owner is ElementType ? "type" : "element") + " '" + ModelReader.Label(owner, _doc) + "'");
            try
            {
                ElementId gp = p.GetAssociatedGlobalParameter();
                if (RevitCompat.IsValid(gp))
                {
                    parts.Add("associated with global parameter '" + (_doc.GetElement(gp)?.Name ?? gp.ToString()) + "' [" + RevitCompat.Value(gp) + "] (change it there, or remove the association in Revit)");
                    return string.Join("; ", parts);
                }
                parts.Add("no global parameter association");
            }
            catch (Exception ex) { parts.Add("global parameter association not readable (" + ex.GetType().Name + ")"); }
            parts.Add("the project API does not expose family formulas or reporting parameters: if the value is not editable in Revit's Properties either, check this parameter in the family (formula, reporting parameter, label)");
            return string.Join("; ", parts);
        }
    }
}
