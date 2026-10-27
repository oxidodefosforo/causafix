using System;
using System.Globalization;

namespace CausaFix.Core.Model
{
    /// <summary>Instance or type scope of a parameter column.</summary>
    public enum ParamScope
    {
        Instance = 0,
        Type = 1
    }

    /// <summary>Where a parameter definition comes from. Drives how it is resolved on import.</summary>
    public enum ParamSource
    {
        /// <summary>Revit built-in parameter. Id = integer value of BuiltInParameter (stable across years).</summary>
        BuiltIn,
        /// <summary>Shared parameter. Id = GUID ("D" format, lower case).</summary>
        Shared,
        /// <summary>Non-shared project parameter. Id = ParameterElement id (document specific).</summary>
        Project,
        /// <summary>
        /// Non-shared family parameter. Revit exposes no cross-family identity for these, so the
        /// id combines data type (spec) and name. Homonyms with different spec or scope stay distinct;
        /// two parameters with identical spec+name+scope on the same element are reported as ambiguous.
        /// </summary>
        Family,
        /// <summary>Context column produced by CausaFix (Category, Family, Type, ElementId...). Always read-only.</summary>
        Pseudo
    }

    /// <summary>
    /// Stable, name-independent identity of a column/parameter. Serialised form examples:
    ///   BIP:-1010106@I            built-in, instance
    ///   SP:9f1c...-...@T          shared, type
    ///   PP:123456@I               project parameter element id
    ///   FP:autodesk.spec.aec:length-2.0.0:Head%20Height@I
    ///   PS:Category               pseudo/context
    /// </summary>
    public sealed class ParamKey : IEquatable<ParamKey>
    {
        public ParamSource Source { get; }
        public ParamScope Scope { get; }
        /// <summary>Source-specific identifier (see <see cref="ParamSource"/>).</summary>
        public string Id { get; }

        private readonly string _text;

        private ParamKey(ParamSource source, ParamScope scope, string id)
        {
            Source = source;
            Scope = scope;
            Id = id ?? string.Empty;
            _text = Format(source, scope, Id);
        }

        public static ParamKey BuiltIn(long builtInParameterValue, ParamScope scope)
            => new ParamKey(ParamSource.BuiltIn, scope, builtInParameterValue.ToString(CultureInfo.InvariantCulture));

        public static ParamKey Shared(Guid guid, ParamScope scope)
            => new ParamKey(ParamSource.Shared, scope, guid.ToString("D").ToLowerInvariant());

        public static ParamKey Project(long parameterElementId, ParamScope scope)
            => new ParamKey(ParamSource.Project, scope, parameterElementId.ToString(CultureInfo.InvariantCulture));

        public static ParamKey Family(string specTypeId, string name, ParamScope scope)
            => new ParamKey(ParamSource.Family, scope, (specTypeId ?? string.Empty) + ":" + Uri.EscapeDataString(name ?? string.Empty));

        public static ParamKey Pseudo(string name)
            => new ParamKey(ParamSource.Pseudo, ParamScope.Instance, name);

        /// <summary>Same identity with the other scope (used when a column is requested at type level).</summary>
        public ParamKey WithScope(ParamScope scope) => new ParamKey(Source, scope, Id);

        public long BuiltInValue
        {
            get
            {
                if (Source != ParamSource.BuiltIn) throw new InvalidOperationException("Not a built-in key: " + _text);
                return long.Parse(Id, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
        }

        public Guid SharedGuid
        {
            get
            {
                if (Source != ParamSource.Shared) throw new InvalidOperationException("Not a shared key: " + _text);
                return Guid.Parse(Id);
            }
        }

        public long ProjectElementId
        {
            get
            {
                if (Source != ParamSource.Project) throw new InvalidOperationException("Not a project key: " + _text);
                return long.Parse(Id, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>For family keys: the spec (data type) part.</summary>
        public string FamilySpec
        {
            get
            {
                if (Source != ParamSource.Family) throw new InvalidOperationException("Not a family key: " + _text);
                int i = Id.LastIndexOf(':');
                return i < 0 ? string.Empty : Id.Substring(0, i);
            }
        }

        /// <summary>For family keys: the parameter name part.</summary>
        public string FamilyName
        {
            get
            {
                if (Source != ParamSource.Family) throw new InvalidOperationException("Not a family key: " + _text);
                int i = Id.LastIndexOf(':');
                return Uri.UnescapeDataString(i < 0 ? Id : Id.Substring(i + 1));
            }
        }

        private static string Format(ParamSource source, ParamScope scope, string id)
        {
            if (source == ParamSource.Pseudo) return "PS:" + id;
            string prefix;
            switch (source)
            {
                case ParamSource.BuiltIn: prefix = "BIP"; break;
                case ParamSource.Shared: prefix = "SP"; break;
                case ParamSource.Project: prefix = "PP"; break;
                default: prefix = "FP"; break;
            }
            return prefix + ":" + id + "@" + (scope == ParamScope.Type ? "T" : "I");
        }

        public static bool TryParse(string text, out ParamKey key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int colon = text.IndexOf(':');
            if (colon <= 0) return false;
            string prefix = text.Substring(0, colon);
            string rest = text.Substring(colon + 1);
            if (prefix == "PS")
            {
                if (rest.Length == 0) return false;
                key = Pseudo(rest);
                return true;
            }
            int at = rest.LastIndexOf('@');
            if (at <= 0 || at != rest.Length - 2) return false;
            char s = rest[at + 1];
            ParamScope scope;
            if (s == 'I') scope = ParamScope.Instance;
            else if (s == 'T') scope = ParamScope.Type;
            else return false;
            string id = rest.Substring(0, at);
            switch (prefix)
            {
                case "BIP":
                    if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bip)) return false;
                    key = BuiltIn(bip, scope);
                    return true;
                case "SP":
                    if (!Guid.TryParse(id, out Guid g)) return false;
                    key = Shared(g, scope);
                    return true;
                case "PP":
                    if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long pp)) return false;
                    key = Project(pp, scope);
                    return true;
                case "FP":
                    if (id.IndexOf(':') < 0) return false;
                    key = new ParamKey(ParamSource.Family, scope, id);
                    return true;
                default:
                    return false;
            }
        }

        public static ParamKey Parse(string text)
        {
            if (!TryParse(text, out ParamKey k)) throw new FormatException("Invalid CausaFixIt parameter key: '" + text + "'");
            return k;
        }

        public override string ToString() => _text;
        public bool Equals(ParamKey other) => other != null && string.Equals(_text, other._text, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as ParamKey);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_text);
        public static bool operator ==(ParamKey a, ParamKey b) => ReferenceEquals(a, b) || (a is object && a.Equals(b));
        public static bool operator !=(ParamKey a, ParamKey b) => !(a == b);
    }
}
