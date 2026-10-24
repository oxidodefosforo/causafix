using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CausaFix.ProjectSetup.Naming;

namespace CausaFix.ProjectSetup.Model
{
    /// <summary>Order in which new sheets of a numbering scope receive their sequence numbers.</summary>
    public enum SequenceOrder
    {
        /// <summary>Level order of the Levels step, then View Set order.</summary>
        LevelThenViewSet = 0,
        /// <summary>View Set order, then level order (the order of earlier versions).</summary>
        ViewSetThenLevel
    }

    /// <summary>
    /// A reusable Sheet Number rule: ordered optional segments (prefix, discipline, series, zone, condition, level
    /// code, sequence, fixed text, parameters…) plus how the sequence is counted. Independent of the View Sets;
    /// assigned to View Sets, compositions or single sheets. The unit of numbering is the sheet, not the view.
    /// </summary>
    [DataContract]
    public sealed class NumberingProfile
    {
        [DataMember(Order = 1)] public string Key { get; set; } = "num-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        [DataMember(Order = 2)] public string Name { get; set; } = "Numbering";
        [DataMember(Order = 3)] public NameRule Rule { get; set; } = new NameRule();
        [DataMember(Order = 4)] public int Start { get; set; } = 1;
        [DataMember(Order = 5)] public int Increment { get; set; } = 1;
        /// <summary>The sequence restarts for each combination of these fields (empty = one global sequence per profile).</summary>
        [DataMember(Order = 6)] public List<NameField> GroupBy { get; set; } = new List<NameField>();
        [DataMember(Order = 7)] public SequenceOrder Order { get; set; } = SequenceOrder.ViewSetThenLevel;
        [DataMember(Order = 8)] public string Description { get; set; } = string.Empty;
        /// <summary>Profiles with the same counter key share one sequence ("" = own counter).</summary>
        [DataMember(Order = 9)] public string CounterKey { get; set; } = string.Empty;
        /// <summary>Created automatically from a format-1 Sheet Number pattern.</summary>
        [DataMember(Order = 10)] public bool DerivedFromLegacy { get; set; }

        public string Counter => string.IsNullOrEmpty(CounterKey) ? Key : CounterKey;

        public NumberingProfile Clone()
        {
            var c = (NumberingProfile)MemberwiseClone();
            c.Rule = Rule?.Clone() ?? new NameRule();
            c.GroupBy = new List<NameField>(GroupBy ?? new List<NameField>());
            return c;
        }

        [OnDeserialized]
        private void Fix(StreamingContext c)
        {
            Rule = Rule ?? new NameRule();
            GroupBy = GroupBy ?? new List<NameField>();
            Description = Description ?? string.Empty;
            CounterKey = CounterKey ?? string.Empty;
            if (Increment == 0) Increment = 1;
        }
    }

    public enum CompositionMode
    {
        /// <summary>Each member in its own slot of the sheet.</summary>
        Separate = 0,
        /// <summary>Members superimposed: every member projects the building anchor to the same paper point.</summary>
        Overlay
    }

    public enum TitleVisibility
    {
        /// <summary>Only the primary member shows its title (secondaries use the hidden-title viewport type).</summary>
        OnlyPrimary = 0,
        All,
        PerMember
    }

    public enum MemberRole
    {
        None = 0,
        Primary,
        Secondary
    }

    /// <summary>A view on a composed sheet: from a View Set (on the composition's level) or an existing view.</summary>
    [DataContract]
    public sealed class CompositionMember
    {
        [DataMember(Order = 1)] public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        [DataMember(Order = 2)] public bool Enabled { get; set; } = true;
        [DataMember(Order = 3)] public string ViewSetKey { get; set; } = string.Empty;
        /// <summary>Existing view (instead of a View Set). Applies only on the view's own level.</summary>
        [DataMember(Order = 4)] public string ExistingViewUniqueId { get; set; } = string.Empty;
        /// <summary>Slot for Separate placement ("" = the View Set's slot).</summary>
        [DataMember(Order = 5)] public string SlotName { get; set; } = string.Empty;
        /// <summary>Title shown (Per member visibility).</summary>
        [DataMember(Order = 6)] public bool ShowTitle { get; set; } = true;
        /// <summary>Viewport type of this member ("" = the View Set's / the hidden-title type when its title is hidden).</summary>
        [DataMember(Order = 7)] public string ViewportTypeUniqueId { get; set; } = string.Empty;
        /// <summary>Extra title offset of this member (mm), so several visible titles do not overlap.</summary>
        [DataMember(Order = 8)] public double TitleOffsetXmm { get; set; }
        [DataMember(Order = 9)] public double TitleOffsetYmm { get; set; }

        public bool IsExistingView => !string.IsNullOrEmpty(ExistingViewUniqueId);
        public CompositionMember Clone() => (CompositionMember)MemberwiseClone();
    }

    /// <summary>
    /// Views on Sheet: one sheet per level holding several members, either in separate slots or overlaid
    /// (a floor plan with its reflected ceiling plan superimposed). The sheet identity is the composition key
    /// plus the level, never its visible number: renaming or renumbering does not create another sheet.
    /// </summary>
    [DataContract]
    public sealed class SheetComposition
    {
        [DataMember(Order = 1)] public string Key { get; set; } = "cmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        [DataMember(Order = 2)] public string Name { get; set; } = "Composition";
        [DataMember(Order = 3)] public bool Enabled { get; set; } = true;
        [DataMember(Order = 4)] public CompositionMode Mode { get; set; } = CompositionMode.Overlay;
        [DataMember(Order = 5)] public string PrimaryMemberId { get; set; } = string.Empty;
        [DataMember(Order = 6)] public List<CompositionMember> Members { get; set; } = new List<CompositionMember>();
        /// <summary>"" = the primary View Set's numbering profile.</summary>
        [DataMember(Order = 7)] public string NumberingProfileKey { get; set; } = string.Empty;
        /// <summary>Null = the primary View Set's Sheet Name rule.</summary>
        [DataMember(Order = 8)] public NameRule SheetNameRule { get; set; }
        /// <summary>"" = the primary View Set's title block.</summary>
        [DataMember(Order = 9)] public string TitleBlockUniqueId { get; set; } = string.Empty;
        /// <summary>Alignment group / slot of the overlay ("" = the primary View Set's).</summary>
        [DataMember(Order = 10)] public string GroupKey { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string SlotName { get; set; } = string.Empty;
        [DataMember(Order = 12)] public TitleVisibility TitleVisibility { get; set; } = TitleVisibility.OnlyPrimary;
        /// <summary>Viewport type used for members whose title is hidden (chosen by the user; shared types are never edited).</summary>
        [DataMember(Order = 13)] public string HiddenTitleViewportTypeUniqueId { get; set; } = string.Empty;
        /// <summary>Sheet-level field overrides (null = from the primary View Set).</summary>
        [DataMember(Order = 14)] public string Condition { get; set; }
        [DataMember(Order = 15)] public string Purpose { get; set; }
        [DataMember(Order = 16)] public string Series { get; set; }
        /// <summary>Title placement of this composition (null = group / profile default).</summary>
        [DataMember(Order = 17)] public TitlePlacement Title { get; set; }
        /// <summary>Sheet keys of earlier versions per level (migrated shared sheets), so existing sheets keep their association.</summary>
        [DataMember(Order = 18)] public Dictionary<string, string> LegacySheetKeys { get; set; } = new Dictionary<string, string>();

        public CompositionMember Primary => Members.FirstOrDefault(m => m.Id == PrimaryMemberId && m.Enabled) ?? Members.FirstOrDefault(m => m.Enabled && !m.IsExistingView);
        public string SheetKeyFor(string levelUniqueId) => "cmp|" + Key + "|" + levelUniqueId;

        public SheetComposition Clone()
        {
            var c = (SheetComposition)MemberwiseClone();
            c.Key = "cmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            c.Members = Members.Select(m => m.Clone()).ToList();
            c.SheetNameRule = SheetNameRule?.Clone();
            c.Title = Title?.Clone();
            c.LegacySheetKeys = new Dictionary<string, string>();
            return c;
        }

        [OnDeserialized]
        private void Fix(StreamingContext c)
        {
            Members = Members ?? new List<CompositionMember>();
            LegacySheetKeys = LegacySheetKeys ?? new Dictionary<string, string>();
        }
    }

    public enum TitleMode
    {
        /// <summary>The title is not touched.</summary>
        KeepExisting = 0,
        /// <summary>Values captured from a correctly placed reference title (as a relative offset or a fixed position).</summary>
        MatchReference,
        RelativeToViewport,
        FixedSheetPosition
    }

    /// <summary>Which corner (or edge centre) of the title is placed, and on which point of the viewport box it is measured.</summary>
    public enum TitleAnchor
    {
        BottomLeft = 0,
        BottomCenter,
        BottomRight,
        TopLeft,
        TopCenter,
        TopRight
    }

    public enum TitleRelativeTo
    {
        /// <summary>The member's own viewport box (for overlays: each member, or the primary when only it shows a title).</summary>
        Viewport = 0,
        /// <summary>The joint envelope of all overlay members.</summary>
        OverlayEnvelope
    }

    public enum TitleCaptureKind
    {
        /// <summary>Offset of the title from the reference viewport box (moves with the drawing).</summary>
        RelativeOffset = 0,
        /// <summary>Absolute paper position of the reference title.</summary>
        FixedPosition
    }

    public enum TitleLineMode
    {
        Keep = 0,
        Set
    }

    /// <summary>
    /// Where the viewport title (not the title block) goes. The title's <see cref="Anchor"/> corner is placed at the same
    /// corner of the viewport box plus the offset (Relative), or at a fixed paper point (Fixed). Applied after the drawing
    /// is aligned, with Viewport.LabelOffset; it never moves the viewport.
    /// </summary>
    [DataContract]
    public sealed class TitlePlacement
    {
        [DataMember(Order = 1)] public TitleMode Mode { get; set; } = TitleMode.KeepExisting;
        [DataMember(Order = 2)] public TitleAnchor Anchor { get; set; } = TitleAnchor.BottomLeft;
        [DataMember(Order = 3)] public double OffsetXmm { get; set; }
        [DataMember(Order = 4)] public double OffsetYmm { get; set; } = -8;
        [DataMember(Order = 5)] public TitleRelativeTo RelativeTo { get; set; } = TitleRelativeTo.Viewport;
        [DataMember(Order = 6)] public double FixedXmm { get; set; } = 30;
        [DataMember(Order = 7)] public double FixedYmm { get; set; } = 30;
        [DataMember(Order = 8)] public SheetFrame Frame { get; set; } = SheetFrame.Sheet;
        [DataMember(Order = 9)] public TitleCaptureKind CaptureKind { get; set; } = TitleCaptureKind.RelativeOffset;
        [DataMember(Order = 10)] public string ReferenceDescription { get; set; } = string.Empty;
        [DataMember(Order = 11)] public TitleLineMode LineMode { get; set; } = TitleLineMode.Keep;
        /// <summary>Line length in mm of paper; negative extends to the left (Revit's sign convention).</summary>
        [DataMember(Order = 12)] public double LineLengthMm { get; set; } = 60;
        /// <summary>True once the user chose "Capture as" himself; a new capture then keeps that kind (otherwise FixedPosition).</summary>
        [DataMember(Order = 13)] public bool CaptureKindExplicit { get; set; }

        /// <summary>
        /// Kind a new reference capture uses: the explicit "Capture as" choice, the kind of an earlier capture, or the
        /// explicit mode the user had (Relative / Fixed). With no previous choice, FixedPosition: titles line up on paper.
        /// </summary>
        public TitleCaptureKind CaptureKindForNewCapture
        {
            get
            {
                if (CaptureKindExplicit) return CaptureKind;
                if (Mode == TitleMode.MatchReference && !string.IsNullOrEmpty(ReferenceDescription)) return CaptureKind;
                if (Mode == TitleMode.RelativeToViewport) return TitleCaptureKind.RelativeOffset;
                return TitleCaptureKind.FixedPosition;
            }
        }

        /// <summary>Effective geometric mode (MatchReference resolves to its captured kind).</summary>
        public bool IsFixed => Mode == TitleMode.FixedSheetPosition || (Mode == TitleMode.MatchReference && CaptureKind == TitleCaptureKind.FixedPosition);
        public bool Applies => Mode != TitleMode.KeepExisting;

        public TitlePlacement Clone() => (TitlePlacement)MemberwiseClone();

        public string Describe()
        {
            if (Mode == TitleMode.KeepExisting) return "Keep existing";
            string line = LineMode == TitleLineMode.Set ? ", line " + LineLengthMm.ToString("0.#") + " mm" : "";
            string src = Mode == TitleMode.MatchReference ? "Match reference (" + (string.IsNullOrEmpty(ReferenceDescription) ? "not captured" : ReferenceDescription) + "): " : "";
            return src + (IsFixed
                ? "title " + Anchor + " at " + FixedXmm.ToString("0.#") + ", " + FixedYmm.ToString("0.#") + " mm (" + Frame + ")"
                : "title " + Anchor + " at viewport " + Anchor + (RelativeTo == TitleRelativeTo.OverlayEnvelope ? " (overlay envelope)" : "") + " + (" + OffsetXmm.ToString("0.#") + ", " + OffsetYmm.ToString("0.#") + ") mm") + line;
        }
    }
}
