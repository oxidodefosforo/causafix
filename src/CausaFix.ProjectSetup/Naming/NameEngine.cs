using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CausaFix.ProjectSetup.Model;

namespace CausaFix.ProjectSetup.Naming
{
    public enum ValueState
    {
        /// <summary>Value known (may come from the plan or from the document).</summary>
        Known = 0,
        /// <summary>The source exists and is empty.</summary>
        Empty,
        /// <summary>The value cannot be known now (element not created yet, parameter not read, no unique source).</summary>
        Unknown
    }

    public struct SourceValue
    {
        public string Value;
        public ValueState State;
        /// <summary>Why the value is empty/unknown, or where a known value came from.</summary>
        public string Note;

        public static SourceValue Of(string v, string note = null) => string.IsNullOrEmpty(v) ? new SourceValue { Value = string.Empty, State = ValueState.Empty, Note = note } : new SourceValue { Value = v, State = ValueState.Known, Note = note };
        public static SourceValue Unknown(string note) => new SourceValue { Value = string.Empty, State = ValueState.Unknown, Note = note };
    }

    /// <summary>Values of one row (and its sheet) for the name engine. Implemented by the plan builder.</summary>
    public interface INameSource
    {
        SourceValue Field(NameField field, NameComponent component);
        SourceValue Param(ParamRef parameter);
        /// <summary>Rule producing a target for this row (null = not produced here).</summary>
        NameRule RuleFor(NameContext target);
        /// <summary>A value fixed outside the rule (row override, value from the sheet's primary member). Null = evaluate the rule.</summary>
        string FixedValue(NameContext target);
        /// <summary>Token values for Advanced patterns.</summary>
        TokenValues Tokens();
    }

    public enum PartState
    {
        Used = 0,
        Mapped,
        Fallback,
        /// <summary>Optional and empty: omitted together with its separator.</summary>
        Omitted,
        Disabled,
        /// <summary>Required and without value: error.</summary>
        Missing
    }

    public sealed class NamePart
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public PartState State { get; set; }
        public string Note { get; set; } = string.Empty;
        public override string ToString() => Label + " = " + (State == PartState.Omitted || State == PartState.Disabled ? "(" + State.ToString().ToLowerInvariant() + ")" : State == PartState.Missing ? "MISSING" : "'" + Value + "'") + (string.IsNullOrEmpty(Note) ? "" : " [" + Note + "]");
    }

    public sealed class NameResult
    {
        public NameContext Target { get; set; }
        public string Text { get; set; } = string.Empty;
        /// <summary>False when the rule is empty (the target is not written, e.g. an empty Title on Sheet).</summary>
        public bool Produced { get; set; } = true;
        public bool FromFixedValue { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public List<NamePart> Parts { get; } = new List<NamePart>();
        public bool Ok => Errors.Count == 0;
        public string Breakdown => string.Join("  ·  ", Parts.Select(p => p.ToString()));
    }

    /// <summary>
    /// Evaluates name rules for one row. Targets may depend on each other (a Sheet Name using the View Name);
    /// dependencies are resolved on demand and circular references are reported as errors, never looped.
    /// </summary>
    public sealed class NameEngine
    {
        private readonly INameSource _src;
        private readonly Dictionary<NameContext, NameResult> _done = new Dictionary<NameContext, NameResult>();
        private readonly List<NameContext> _stack = new List<NameContext>();

        public NameEngine(INameSource source) { _src = source; }

        public NameResult Evaluate(NameContext target)
        {
            if (_done.TryGetValue(target, out NameResult cached)) return cached;
            if (_stack.Contains(target))
            {
                var cyc = new NameResult { Target = target };
                var path = _stack.Skip(_stack.IndexOf(target)).Concat(new[] { target }).Select(NameFields.TargetLabel);
                cyc.Errors.Add("Circular reference: " + string.Join(" → ", path) + ". A name cannot depend on its own result.");
                return cyc;
            }
            _stack.Add(target);
            try
            {
                var r = Compute(target);
                _done[target] = r;
                return r;
            }
            finally { _stack.RemoveAt(_stack.Count - 1); }
        }

        private NameResult Compute(NameContext target)
        {
            var res = new NameResult { Target = target };
            string label = NameFields.TargetLabel(target);
            string fixedValue = _src.FixedValue(target);
            if (fixedValue != null)
            {
                res.Text = NameRules.Normalize(fixedValue);
                res.FromFixedValue = true;
                Validate(res, target, label);
                return res;
            }
            var rule = _src.RuleFor(target);
            if (rule == null || rule.IsEmpty)
            {
                res.Produced = false;
                return res;
            }
            if (rule.SameAs.HasValue)
            {
                if (rule.SameAs.Value == target) { res.Errors.Add(label + ": 'Use same rule' points to itself."); return res; }
                var other = Evaluate(rule.SameAs.Value);
                res.Parts.Add(new NamePart { Label = "Same as " + NameFields.TargetLabel(rule.SameAs.Value), Value = other.Text, State = PartState.Used });
                res.Errors.AddRange(other.Errors.Select(e => e.StartsWith("Circular", StringComparison.Ordinal) ? e : label + " (same as " + NameFields.TargetLabel(rule.SameAs.Value) + "): " + e));
                res.Text = ApplyCase(other.Text, rule.Case);
                if (!other.Produced) { res.Produced = false; return res; }
                Validate(res, target, label);
                return res;
            }
            if (rule.Mode == NameRuleMode.Advanced)
            {
                var p = NamePattern.Expand(rule.Pattern, _src.Tokens(), target, rule.Case);
                res.Text = p.Text;
                res.Errors.AddRange(p.Errors);
                res.Parts.Add(new NamePart { Label = "Advanced pattern " + rule.Pattern, Value = p.Text, State = p.Ok ? PartState.Used : PartState.Missing });
                return res;
            }

            var sb = new StringBuilder();
            foreach (var c in rule.Components)
            {
                var part = new NamePart { Label = c.Label };
                res.Parts.Add(part);
                if (!c.Enabled) { part.State = PartState.Disabled; continue; }
                SourceValue v = Resolve(c, res, target);
                string value = v.Value ?? string.Empty;
                part.Note = v.Note ?? string.Empty;
                bool mapped = false;
                if (value.Length > 0 && c.Mappings != null && c.Mappings.Count > 0)
                {
                    var m = c.Mappings.FirstOrDefault(x => string.Equals((x.From ?? "").Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (m != null) { value = m.To ?? string.Empty; mapped = true; }
                }
                bool usedFallback = false;
                if (value.Length == 0 && c.Fallback != null && c.Fallback.Length > 0) { value = c.Fallback; usedFallback = true; }
                if (value.Length == 0)
                {
                    if (c.Required && c.Source != ComponentSource.Text)
                    {
                        part.State = PartState.Missing;
                        string why = v.State == ValueState.Unknown ? "not known: " + v.Note : "empty" + (string.IsNullOrEmpty(v.Note) ? "" : " (" + v.Note + ")");
                        res.Errors.Add(label + ": required component " + c.Label + " has no value – " + why + ". Set a value, a fallback, or make it optional.");
                    }
                    else part.State = PartState.Omitted;
                    continue;
                }
                value = Pad(value, c.PadDigits);
                value = ApplyCase(value, c.Case);
                part.Value = value;
                part.State = usedFallback ? PartState.Fallback : mapped ? PartState.Mapped : PartState.Used;
                if (sb.Length > 0 && !string.IsNullOrEmpty(c.Separator)) sb.Append(c.Separator);
                sb.Append(value);
            }
            res.Text = ApplyCase(sb.ToString(), rule.Case).Trim();
            if (res.Errors.Count == 0) Validate(res, target, label);
            return res;
        }

        private SourceValue Resolve(NameComponent c, NameResult res, NameContext target)
        {
            switch (c.Source)
            {
                case ComponentSource.Text: return SourceValue.Of(c.Text);
                case ComponentSource.Parameter:
                    if (c.Param == null) return SourceValue.Unknown("no parameter chosen");
                    return _src.Param(c.Param);
                default:
                    var dep = NameFields.TargetOf(c.Field);
                    if (dep.HasValue)
                    {
                        var d = Evaluate(dep.Value);
                        foreach (var e in d.Errors.Where(e => e.StartsWith("Circular", StringComparison.Ordinal))) if (!res.Errors.Contains(e)) res.Errors.Add(e);
                        if (d.Errors.Count > 0) return SourceValue.Unknown(NameFields.TargetLabel(dep.Value) + " has errors");
                        if (!d.Produced) return SourceValue.Of(string.Empty, NameFields.TargetLabel(dep.Value) + " is not produced");
                        return SourceValue.Of(d.Text);
                    }
                    return _src.Field(c.Field, c);
            }
        }

        private static void Validate(NameResult res, NameContext target, string label)
        {
            string bad = NameRules.InvalidCharacters(res.Text, target);
            if (bad.Length > 0) res.Errors.Add(label + " '" + res.Text + "' contains characters Revit does not allow: " + bad);
            if (res.Text.Length == 0 && target != NameContext.TitleOnSheet) res.Errors.Add(label + ": the result is empty.");
        }

        /// <summary>Zero-pads values made only of digits. Text codes (with letters) are never changed; existing leading zeros are kept.</summary>
        public static string Pad(string value, int digits)
        {
            if (digits <= 0 || string.IsNullOrEmpty(value)) return value;
            if (!value.All(char.IsDigit)) return value;
            return value.Length >= digits ? value : new string('0', digits - value.Length) + value;
        }

        public static string ApplyCase(string s, TextCase c)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            if (c == TextCase.Upper) return s.ToUpper(CultureInfo.CurrentCulture);
            if (c == TextCase.Lower) return s.ToLower(CultureInfo.CurrentCulture);
            return s;
        }

        /// <summary>Level elevation (internal feet) in the component's unit and decimals, invariant culture.</summary>
        public static string FormatElevation(double feet, ElevationUnit unit, int decimals)
        {
            double v = unit == ElevationUnit.Feet ? feet : unit == ElevationUnit.Meters ? feet * 0.3048 : feet * 304.8;
            decimals = Math.Max(0, Math.Min(6, decimals));
            v = Math.Round(v, decimals, MidpointRounding.AwayFromZero);
            if (Math.Abs(v) < Math.Pow(10, -decimals) / 2) v = 0;
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}
