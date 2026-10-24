using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using CausaFix.Core.Compare;
using CausaFix.Core.Model;
using CausaFix.Core.Values;

namespace CausaFix.Revit
{
    /// <summary>
    /// Name-independent parameter identity (see ParamKey) and resolution on elements.
    /// Never uses LookupParameter(name).
    /// </summary>
    public static class ParamIdentity
    {
        public static ParamKey KeyOf(Parameter p, Document doc, ParamScope scope)
        {
            if (p?.Definition == null) return null;
            if (p.Definition is InternalDefinition idf && idf.BuiltInParameter != BuiltInParameter.INVALID)
                return ParamKey.BuiltIn((int)idf.BuiltInParameter, scope);
            if (p.IsShared) return ParamKey.Shared(p.GUID, scope);
            if (RevitCompat.IsValid(p.Id) && doc.GetElement(p.Id) is ParameterElement)
                return ParamKey.Project(RevitCompat.Value(p.Id), scope);
            return ParamKey.Family(SpecOf(p)?.TypeId ?? string.Empty, p.Definition.Name, scope);
        }

        public static ForgeTypeId SpecOf(Parameter p)
        {
            try { return p.Definition.GetDataType(); }
            catch (Exception) { return new ForgeTypeId(string.Empty); }
        }

        public enum Resolution
        {
            Found,
            NotFound,
            Ambiguous
        }

        /// <summary>Finds the parameter identified by <paramref name="key"/> on <paramref name="holder"/> (the instance or its type).</summary>
        public static Resolution Resolve(Element holder, ParamKey key, out Parameter parameter)
        {
            parameter = null;
            if (holder == null) return Resolution.NotFound;
            Document doc = holder.Document;
            switch (key.Source)
            {
                case ParamSource.BuiltIn:
                    {
                        long v = key.BuiltInValue;
                        if (v < int.MinValue || v > int.MaxValue) return Resolution.NotFound;
                        var bip = (BuiltInParameter)(int)v;
                        if (!Enum.IsDefined(typeof(BuiltInParameter), bip)) return Resolution.NotFound;
                        parameter = holder.get_Parameter(bip);
                        return parameter != null ? Resolution.Found : Resolution.NotFound;
                    }
                case ParamSource.Shared:
                    parameter = holder.get_Parameter(key.SharedGuid);
                    return parameter != null ? Resolution.Found : Resolution.NotFound;
                case ParamSource.Project:
                    {
                        if (doc.GetElement(RevitCompat.Id(key.ProjectElementId)) is ParameterElement pe)
                        {
                            parameter = holder.get_Parameter(pe.GetDefinition());
                            if (parameter != null) return Resolution.Found;
                        }
                        return Resolution.NotFound;
                    }
                case ParamSource.Family:
                    {
                        string name = key.FamilyName;
                        string spec = key.FamilySpec;
                        int found = 0;
                        foreach (Parameter p in holder.Parameters)
                        {
                            if (p?.Definition == null || p.IsShared) continue;
                            if (p.Definition is InternalDefinition idf && idf.BuiltInParameter != BuiltInParameter.INVALID) continue;
                            if (!string.Equals(p.Definition.Name, name, StringComparison.Ordinal)) continue;
                            if (!string.Equals(SpecOf(p)?.TypeId ?? string.Empty, spec, StringComparison.Ordinal)) continue;
                            if (RevitCompat.IsValid(p.Id) && doc.GetElement(p.Id) is ParameterElement) continue;
                            parameter = p;
                            found++;
                        }
                        if (found > 1) { parameter = null; return Resolution.Ambiguous; }
                        return found == 1 ? Resolution.Found : Resolution.NotFound;
                    }
                default:
                    return Resolution.NotFound;
            }
        }

        /// <summary>Element that holds a parameter of the given scope for an instance element.</summary>
        public static Element Holder(Element element, ParamScope scope)
        {
            if (element == null) return null;
            if (scope == ParamScope.Instance) return element;
            if (element is ElementType) return element;
            ElementId tid = element.GetTypeId();
            return RevitCompat.IsValid(tid) ? element.Document.GetElement(tid) : null;
        }

        public static ValueKind KindOf(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String: return ValueKind.Text;
                case StorageType.Double: return ValueKind.Double;
                case StorageType.ElementId: return ValueKind.ElementRef;
                case StorageType.Integer:
                    {
                        ForgeTypeId spec = SpecOf(p);
                        if (spec != null && spec.Equals(SpecTypeId.Boolean.YesNo)) return ValueKind.YesNo;
                        return ValueKind.Integer;
                    }
                default: return ValueKind.Context;
            }
        }

        public static ParameterInfo Describe(Parameter p, Document doc, ParamScope scope)
        {
            var key = KeyOf(p, doc, scope);
            if (key == null) return null;
            var spec = SpecOf(p);
            var kind = KindOf(p);
            var info = new ParameterInfo
            {
                Key = key,
                Name = p.Definition.Name,
                Kind = kind,
                SpecTypeId = spec?.TypeId ?? string.Empty,
                DataTypeLabel = kind == ValueKind.YesNo ? "Yes/No" : kind == ValueKind.ElementRef ? "Reference" : RevitUnits.SpecLabel(spec)
            };
            if (kind == ValueKind.Double)
            {
                var (unit, label) = RevitUnits.DisplayUnit(doc, spec);
                info.UnitTypeId = unit;
                info.UnitLabel = label;
            }
            return info;
        }
    }

    public sealed class LockInfo
    {
        public LockInfo(BlockCode code, string reason) { Code = code; Reason = reason; }
        public BlockCode Code { get; }
        public string Reason { get; }
    }

    /// <summary>Per element and per parameter editability, with a useful reason.</summary>
    public sealed class ParamAccess
    {
        private readonly Document _doc;
        private readonly Dictionary<long, string> _elementLocks = new Dictionary<long, string>();

        public ParamAccess(Document doc) { _doc = doc; }

        /// <summary>Returns null when the parameter can be written on this holder, otherwise the reason.</summary>
        public string LockReason(Element holder, Parameter p, bool isLinkedDocument) => Lock(holder, p, isLinkedDocument)?.Reason;

        /// <summary>
        /// Real writability of one parameter on one element (null = writable through the API).
        /// Parameter.UserModifiable is NOT used: it describes editing in the Revit UI, and the API can write
        /// shared parameters whose UserModifiable flag is false. Parameter.IsReadOnly is what Revit enforces
        /// (formulas, calculated values, constraints).
        /// </summary>
        public LockInfo Lock(Element holder, Parameter p, bool isLinkedDocument)
        {
            if (isLinkedDocument) return new LockInfo(BlockCode.Linked, "Linked model data is read-only. Edit it in the linked model.");
            var parameterLock = ParameterLock(p);
            if (parameterLock != null) return parameterLock;
            if (holder.GroupId != null && RevitCompat.IsValid(holder.GroupId) &&
                p.Definition is InternalDefinition idf && !idf.VariesAcrossGroups)
                return new LockInfo(BlockCode.Locked, "The element is in a model group and this parameter does not vary across group instances. Edit the group in Revit.");
            string ws = ElementLock(holder);
            return ws == null ? null : new LockInfo(BlockCode.Locked, ws);
        }

        /// <summary>Element-independent restrictions of a parameter (Revit read-only, or not supported by CausaFix).</summary>
        public static LockInfo ParameterLock(Parameter p)
        {
            if (p.StorageType == StorageType.None) return new LockInfo(BlockCode.Unsupported, "Unsupported: this parameter has no value storage.");
            if (p.IsReadOnly) return new LockInfo(BlockCode.ReadOnlyParameter, "Revit reports this parameter as read-only (calculated, driven by a formula or constrained).");
            if (p.StorageType == StorageType.ElementId && p.Definition is InternalDefinition d && TypeChangingParameters.Contains(d.BuiltInParameter))
                return new LockInfo(BlockCode.Unsupported, "Unsupported: changing an element's family or type is not done by Excel Link.");
            return null;
        }

        private static readonly HashSet<BuiltInParameter> TypeChangingParameters = new HashSet<BuiltInParameter>
        {
            BuiltInParameter.ELEM_TYPE_PARAM, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, BuiltInParameter.ELEM_FAMILY_PARAM, BuiltInParameter.SYMBOL_ID_PARAM
        };

        /// <summary>Worksharing locks are per element (cached).</summary>
        public string ElementLock(Element e)
        {
            if (!_doc.IsWorkshared) return null;
            long id = RevitCompat.Value(e.Id);
            if (_elementLocks.TryGetValue(id, out string cached)) return cached;
            string reason = null;
            try
            {
                var status = WorksharingUtils.GetCheckoutStatus(_doc, e.Id);
                if (status == CheckoutStatus.OwnedByOtherUser)
                {
                    string owner = WorksharingUtils.GetWorksharingTooltipInfo(_doc, e.Id)?.Owner ?? "another user";
                    reason = "Owned by " + owner + " (worksharing). Ask them to relinquish it.";
                }
                else
                {
                    var upd = WorksharingUtils.GetModelUpdatesStatus(_doc, e.Id);
                    if (upd == ModelUpdatesStatus.UpdatedInCentral) reason = "Updated in central: Reload Latest before editing.";
                    else if (upd == ModelUpdatesStatus.DeletedInCentral) reason = "Deleted in central.";
                }
            }
            catch (Exception) { /* status not available for this element: no lock reported, Revit will validate on write */ }
            _elementLocks[id] = reason;
            return reason;
        }

        public void Invalidate() => _elementLocks.Clear();

    }

    /// <summary>Reads and writes parameter values as RawValue (internal units).</summary>
    public static class ValueIO
    {
        public static CellValue Read(Parameter p, ColumnDef col, IUnitConverter units, Document doc)
        {
            var cell = new CellValue();
            switch (p.StorageType)
            {
                case StorageType.String:
                    {
                        string s = p.AsString();
                        cell.Raw = string.IsNullOrEmpty(s) ? RawValue.Empty : RawValue.FromText(s);
                        cell.DisplayText = s ?? string.Empty;
                        break;
                    }
                case StorageType.Integer:
                    {
                        if (!p.HasValue) { cell.Raw = RawValue.Empty; break; }
                        int v = p.AsInteger();
                        cell.Raw = RawValue.FromInteger(v);
                        cell.ExportNumber = v;
                        cell.DisplayText = col.Kind == ValueKind.YesNo ? (v == 1 ? "Yes" : v == 0 ? "No" : v.ToString(CultureInfo.InvariantCulture))
                                                                       : (p.AsValueString() ?? v.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
                case StorageType.Double:
                    {
                        if (!p.HasValue) { cell.Raw = RawValue.Empty; break; }
                        double d = p.AsDouble();
                        cell.Raw = RawValue.FromDouble(d);
                        cell.ExportNumber = string.IsNullOrEmpty(col.UnitTypeId) ? d : units.FromInternal(d, col.UnitTypeId);
                        cell.DisplayText = p.AsValueString() ?? d.ToString("R", CultureInfo.InvariantCulture);
                        break;
                    }
                case StorageType.ElementId:
                    {
                        ElementId id = p.AsElementId();
                        if (!RevitCompat.IsValid(id)) { cell.Raw = RawValue.Empty; cell.DisplayText = string.Empty; break; }
                        cell.Raw = RawValue.FromElementId(RevitCompat.Value(id));
                        string name = doc.GetElement(id)?.Name;
                        if (string.IsNullOrEmpty(name)) name = p.AsValueString();
                        cell.DisplayText = References.Format(name, RevitCompat.Value(id));
                        break;
                    }
                default:
                    cell.Raw = RawValue.Empty;
                    break;
            }
            return cell;
        }

        public static RawValue ReadRaw(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    {
                        string s = p.AsString();
                        return string.IsNullOrEmpty(s) ? RawValue.Empty : RawValue.FromText(s);
                    }
                case StorageType.Integer: return p.HasValue ? RawValue.FromInteger(p.AsInteger()) : RawValue.Empty;
                case StorageType.Double: return p.HasValue ? RawValue.FromDouble(p.AsDouble()) : RawValue.Empty;
                case StorageType.ElementId:
                    {
                        ElementId id = p.AsElementId();
                        return RevitCompat.IsValid(id) ? RawValue.FromElementId(RevitCompat.Value(id)) : RawValue.Empty;
                    }
                default: return RawValue.Empty;
            }
        }

        /// <summary>
        /// Writes and verifies by reading back. Returns null on success, otherwise the failure reason.
        /// Must be called inside an open transaction.
        /// </summary>
        public static string Write(Parameter p, bool clear, RawValue value)
        {
            bool ok;
            try
            {
                if (clear)
                {
                    if (p.StorageType == StorageType.String) ok = p.Set(string.Empty);
                    else if (p.StorageType == StorageType.ElementId) ok = p.Set(ElementId.InvalidElementId);
                    else ok = p.ClearValue();
                    if (!ok) return "Revit does not allow clearing this parameter.";
                    var after = ReadRaw(p);
                    return after.IsEmpty ? null : "Revit kept a value after clearing (" + after + ").";
                }
                switch (p.StorageType)
                {
                    case StorageType.String:
                        if (value.Kind != RawKind.Text) return "Text expected.";
                        ok = p.Set(value.Text);
                        break;
                    case StorageType.Integer:
                        if (value.Kind != RawKind.Integer) return "Integer expected.";
                        if (value.Integer < int.MinValue || value.Integer > int.MaxValue) return "Integer out of range.";
                        ok = p.Set((int)value.Integer);
                        break;
                    case StorageType.Double:
                        if (value.Kind != RawKind.Double) return "Number expected.";
                        ok = p.Set(value.Double);
                        break;
                    case StorageType.ElementId:
                        if (value.Kind != RawKind.ElementId) return "Reference expected.";
                        ok = p.Set(RevitCompat.Id(value.Integer));
                        break;
                    default:
                        return "This parameter type cannot be written by CausaFix.";
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { return "Revit refused the change: " + ex.Message; }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex) { return "Revit refused the value: " + ex.Message; }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex) { return "Revit error: " + ex.GetType().Name + ": " + ex.Message; }

            if (!ok) return "Revit refused the change (Parameter.Set returned false).";
            var back = ReadRaw(p);
            if (!ValueComparer.AreEqual(back, value))
                return "Revit stored a different value (" + back + ") – the value may be out of range or constrained.";
            return null;
        }
    }
}

namespace CausaFix.Revit
{
    /// <summary>Allowed values for reference (ElementId) parameters: elements of the same class as the referenced ones.</summary>
    public static class ReferenceCatalog
    {
        public const int MaxCandidates = 2000;

        /// <summary>Class name used to validate assignments ("Level", "Material", ...). Element types are excluded.</summary>
        public static string ClassOf(Element e) => e == null || e is ElementType ? string.Empty : e.GetType().Name;

        /// <summary>Most common class among the referenced ids, and every element of that class as "Name [Id]".</summary>
        public static (string cls, List<string> candidates) Build(Document doc, IEnumerable<long> referencedIds)
        {
            var counts = new Dictionary<Type, int>();
            foreach (long id in referencedIds)
            {
                Element e = doc.GetElement(RevitCompat.Id(id));
                if (e == null || e is ElementType) continue;
                counts[e.GetType()] = counts.TryGetValue(e.GetType(), out int n) ? n + 1 : 1;
            }
            if (counts.Count == 0) return (string.Empty, new List<string>());
            Type t = null; int best = -1;
            foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; t = kv.Key; }
            var list = new List<string>();
            foreach (Element e in new FilteredElementCollector(doc).OfClass(t))
            {
                if (e.GetType() != t) continue;
                list.Add(References.Format(e.Name, RevitCompat.Value(e.Id)));
                if (list.Count >= MaxCandidates) break;
            }
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return (t.Name, list);
        }

        /// <summary>Null when <paramref name="id"/> may be assigned to a column of class <paramref name="cls"/>.</summary>
        public static string Validate(Document doc, long id, string cls, Parameter p)
        {
            Element target = doc.GetElement(RevitCompat.Id(id));
            if (target == null) return "The referenced element [" + id + "] does not exist in this model.";
            if (target is ElementType) return "Unsupported: assigning element types through a reference parameter.";
            string expected = cls;
            if (string.IsNullOrEmpty(expected))
            {
                Element now = doc.GetElement(p.AsElementId());
                expected = ClassOf(now);
            }
            if (string.IsNullOrEmpty(expected)) return "Unsupported: the kind of element this parameter accepts is unknown (the parameter is empty and no list was exported).";
            if (target.GetType().Name != expected) return "Element [" + id + "] is a " + target.GetType().Name + "; this parameter expects a " + expected + ".";
            return null;
        }
    }
}
