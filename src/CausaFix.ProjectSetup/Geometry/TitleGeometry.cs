using System;
using System.Collections.Generic;
using System.Linq;
using CausaFix.ProjectSetup.Model;

namespace CausaFix.ProjectSetup.Geometry
{
    /// <summary>Axis-aligned rectangle on the sheet (any unit).</summary>
    public readonly struct Box2
    {
        public readonly double MinX, MinY, MaxX, MaxY;
        public Box2(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX); MinY = Math.Min(minY, maxY); MaxX = Math.Max(minX, maxX); MaxY = Math.Max(minY, maxY);
        }
        public P2 Min => new P2(MinX, MinY);
        public P2 Center => new P2((MinX + MaxX) / 2, (MinY + MaxY) / 2);
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public static Box2 Of(double[] a) => new Box2(a[0], a[1], a[2], a[3]);
        public double[] ToArray() => new[] { MinX, MinY, MaxX, MaxY };
        public Box2 Translate(P2 d) => new Box2(MinX + d.X, MinY + d.Y, MaxX + d.X, MaxY + d.Y);
        public Box2 Scale(double f) => new Box2(MinX * f, MinY * f, MaxX * f, MaxY * f);
        public static Box2 Union(IEnumerable<Box2> boxes)
        {
            var l = boxes.ToList();
            return new Box2(l.Min(b => b.MinX), l.Min(b => b.MinY), l.Max(b => b.MaxX), l.Max(b => b.MaxY));
        }
        public bool Inside(Box2 outer) => MinX >= outer.MinX && MinY >= outer.MinY && MaxX <= outer.MaxX && MaxY <= outer.MaxY;
    }

    /// <summary>
    /// View-title placement math, Revit-independent and tested. Revit's Viewport.LabelOffset is "a two-dimensional vector
    /// from the left bottom corner of the viewport with Rotation set to None to the left end of the viewport label line".
    /// So all work happens in that unrotated viewport frame ("local"): sheet measurements are rotated back about the box
    /// centre. Where the title text sits relative to the line start depends on the title family, so it is measured
    /// (GetLabelOutline) instead of assumed: offset' = desiredCorner − boxMin − (labelCorner − (boxMin + offset)).
    /// </summary>
    public static class TitleGeometry
    {
        /// <summary>Quarter turns of the viewport on the sheet: Clockwise = −1, Counterclockwise = +1.</summary>
        public static int QuarterTurns(ViewportRotationOption r) => r == ViewportRotationOption.Clockwise ? -1 : r == ViewportRotationOption.Counterclockwise ? 1 : 0;

        public static P2 Rotate(P2 p, P2 center, int quarterTurns)
        {
            int q = ((quarterTurns % 4) + 4) % 4;
            double dx = p.X - center.X, dy = p.Y - center.Y;
            switch (q)
            {
                case 1: return new P2(center.X - dy, center.Y + dx);
                case 2: return new P2(center.X - dx, center.Y - dy);
                case 3: return new P2(center.X + dy, center.Y - dx);
                default: return p;
            }
        }

        public static Box2 Rotate(Box2 b, P2 center, int quarterTurns)
        {
            var a = Rotate(new P2(b.MinX, b.MinY), center, quarterTurns);
            var c = Rotate(new P2(b.MaxX, b.MaxY), center, quarterTurns);
            return new Box2(a.X, a.Y, c.X, c.Y);
        }

        /// <summary>Sheet rectangle → unrotated viewport frame (rotation undone about the viewport box centre).</summary>
        public static Box2 ToLocal(Box2 sheet, P2 viewportCenter, int quarterTurns) => Rotate(sheet, viewportCenter, -quarterTurns);
        public static P2 ToLocal(P2 sheet, P2 viewportCenter, int quarterTurns) => Rotate(sheet, viewportCenter, -quarterTurns);

        public static P2 Corner(Box2 b, TitleAnchor a)
        {
            double cx = (b.MinX + b.MaxX) / 2;
            switch (a)
            {
                case TitleAnchor.BottomCenter: return new P2(cx, b.MinY);
                case TitleAnchor.BottomRight: return new P2(b.MaxX, b.MinY);
                case TitleAnchor.TopLeft: return new P2(b.MinX, b.MaxY);
                case TitleAnchor.TopCenter: return new P2(cx, b.MaxY);
                case TitleAnchor.TopRight: return new P2(b.MaxX, b.MaxY);
                default: return new P2(b.MinX, b.MinY);
            }
        }

        /// <summary>
        /// Where the title's anchor corner must be, in the local frame. Relative: the same corner of the reference box
        /// (the viewport, or the overlay envelope) plus the offset. Fixed: a sheet point (already converted from the
        /// title block frame when needed) brought into the local frame. Member offsets are added in both cases.
        /// </summary>
        public static P2 DesiredCorner(TitlePlacement t, Box2 localReference, P2 fixedSheetPoint, P2 viewportCenter, int quarterTurns, double extraX, double extraY)
        {
            P2 p = t.IsFixed ? ToLocal(fixedSheetPoint, viewportCenter, quarterTurns)
                             : Corner(localReference, t.Anchor) + new P2(t.OffsetXmm, t.OffsetYmm);
            return p + new P2(extraX, extraY);
        }

        /// <summary>LabelOffset that puts the measured title's anchor corner on <paramref name="desired"/>.</summary>
        public static P2 NewLabelOffset(Box2 localViewport, Box2 localLabel, P2 currentOffset, P2 desired, TitleAnchor a)
        {
            P2 lineStart = localViewport.Min + currentOffset;
            P2 familyDelta = Corner(localLabel, a) - lineStart;
            return desired - localViewport.Min - familyDelta;
        }

        /// <summary>Relative capture: offset of the title's anchor corner from the same corner of its viewport box.</summary>
        public static P2 CaptureRelative(Box2 localViewport, Box2 localLabel, TitleAnchor a) => Corner(localLabel, a) - Corner(localViewport, a);

        /// <summary>Distance between where the title's corner is and where it should be.</summary>
        public static double Error(Box2 localLabel, P2 desired, TitleAnchor a) => (Corner(localLabel, a) - desired).Length;

        /// <summary>
        /// Estimated title rectangle for the preview, before Revit measures it: placed at the desired corner with a nominal
        /// size (the real size depends on the title family). Only used for drawing – never as a measurement.
        /// </summary>
        public static Box2 EstimatedLabel(P2 corner, TitleAnchor a, double width, double height)
        {
            double x0 = a == TitleAnchor.BottomLeft || a == TitleAnchor.TopLeft ? corner.X : a == TitleAnchor.BottomRight || a == TitleAnchor.TopRight ? corner.X - width : corner.X - width / 2;
            double y0 = a == TitleAnchor.TopLeft || a == TitleAnchor.TopCenter || a == TitleAnchor.TopRight ? corner.Y - height : corner.Y;
            return new Box2(x0, y0, x0 + width, y0 + height);
        }
    }
}

namespace CausaFix.ProjectSetup.Geometry
{
    /// <summary>A placed viewport whose title can be measured and moved (mm of paper). Implemented over Revit and by tests.</summary>
    public interface ITitleProbe
    {
        /// <summary>Viewport box on the sheet, excluding the title (regenerated).</summary>
        Box2 ViewportBox();
        /// <summary>Title outline on the sheet, or null when the viewport type shows no title.</summary>
        Box2? LabelBox();
        /// <summary>Viewport.LabelOffset (local frame, mm).</summary>
        P2 LabelOffset { get; set; }
        double LineLength { get; set; }
    }

    public sealed class TitleReport
    {
        public bool Placed { get; set; }
        public bool NoTitle { get; set; }
        public double ErrorMm { get; set; }
        public Box2? FinalLabel { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Title placement loop shared by the executor and the tests: line length first (it changes the outline), then
    /// measure → compute LabelOffset → set → measure again, up to three times. When the result is not within tolerance
    /// the original offset and line length are restored and the title is reported as not placed.
    /// </summary>
    public static class TitleRunner
    {
        public static TitleReport Place(ITitleProbe probe, TitlePlacement t, Box2? sheetReference, P2 fixedSheetPoint, int quarterTurns,
                                        double extraX, double extraY, double toleranceMm, int maxIterations = 3)
        {
            var rep = new TitleReport();
            P2 originalOffset = probe.LabelOffset;
            double originalLine = probe.LineLength;
            if (t.LineMode == TitleLineMode.Set && Math.Abs(t.LineLengthMm - originalLine) > 1e-6) probe.LineLength = t.LineLengthMm;

            var label = probe.LabelBox();
            if (label == null) { rep.NoTitle = true; rep.Message = "The viewport type shows no title: nothing to place."; probe.LineLength = originalLine; return rep; }
            var box = probe.ViewportBox();
            P2 c = box.Center;
            Box2 localBox = TitleGeometry.ToLocal(box, c, quarterTurns);
            Box2 localRef = sheetReference.HasValue ? TitleGeometry.ToLocal(sheetReference.Value, c, quarterTurns) : localBox;
            P2 desired = TitleGeometry.DesiredCorner(t, localRef, fixedSheetPoint, c, quarterTurns, extraX, extraY);

            double err = TitleGeometry.Error(TitleGeometry.ToLocal(label.Value, c, quarterTurns), desired, t.Anchor);
            for (int i = 0; i < maxIterations && err > toleranceMm; i++)
            {
                var localLabel = TitleGeometry.ToLocal(label.Value, c, quarterTurns);
                probe.LabelOffset = TitleGeometry.NewLabelOffset(localBox, localLabel, probe.LabelOffset, desired, t.Anchor);
                label = probe.LabelBox();
                if (label == null) break;
                err = TitleGeometry.Error(TitleGeometry.ToLocal(label.Value, c, quarterTurns), desired, t.Anchor);
            }
            rep.ErrorMm = err;
            if (label != null && err <= toleranceMm)
            {
                rep.Placed = true;
                rep.FinalLabel = label;
                rep.Message = "Title placed (error " + err.ToString("0.###") + " mm).";
                return rep;
            }
            probe.LabelOffset = originalOffset;
            probe.LineLength = originalLine;
            rep.FinalLabel = probe.LabelBox();
            rep.Message = "The title could not be placed within " + toleranceMm.ToString("0.##") + " mm (error " + err.ToString("0.###") + " mm); it was left as it was.";
            return rep;
        }
    }
}
