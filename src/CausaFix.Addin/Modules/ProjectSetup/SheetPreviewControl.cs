using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CausaFix.ProjectSetup.ViewModels;

namespace CausaFix.Addin.Modules.ProjectSetup
{
    /// <summary>
    /// 2D sketch of a sheet: outline (from the title block size, or an A1 estimate), usable area, slot anchors, overlays
    /// (one anchor shared by several members), existing viewports/titles as read from the model (grey), estimated title
    /// positions (dashed) and, after a run, the viewport and title outlines measured in Revit (solid).
    /// Estimated elements are dashed; measured ones are solid – a sketch is never presented as a measurement.
    /// </summary>
    public sealed class SheetPreviewControl : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(SheetPreviewData), typeof(SheetPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public SheetPreviewData Data
        {
            get => (SheetPreviewData)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        private static readonly Pen Outline = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x23, 0x39, 0x4D)), 1.2));
        private static readonly Pen Estimated = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)), 1) { DashStyle = DashStyles.Dash });
        private static readonly Pen Usable = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x0E, 0x7C, 0x86)), 1) { DashStyle = DashStyles.Dot });
        private static readonly Pen Measured = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x5B, 0x4B, 0x8A)), 1.5));
        private static readonly Pen Anchor = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)), 1.5));
        private static readonly Pen Existing = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xA8, 0xB2, 0xBB)), 1));
        private static readonly Pen TitleEstimated = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0x8E, 0x04)), 1) { DashStyle = DashStyles.Dash });
        private static readonly Pen TitleMeasured = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0x8E, 0x04)), 1.5));
        private static readonly Pen Overlay = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x0E, 0x7C, 0x86)), 2));

        private static Pen Frozen(Pen p) { p.Freeze(); return p; }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var d = Data;
            if (d == null || d.WidthMm <= 0 || d.HeightMm <= 0) return;
            double margin = 14;
            double s = Math.Min((ActualWidth - 2 * margin) / d.WidthMm, (ActualHeight - 2 * margin) / d.HeightMm);
            if (s <= 0) return;
            Point P(double xmm, double ymm) => new Point(margin + xmm * s, ActualHeight - margin - ymm * s); // sheet Y up

            dc.DrawRectangle(null, d.SizeKnown ? Outline : Estimated, new Rect(P(0, d.HeightMm), P(d.WidthMm, 0)));
            dc.DrawRectangle(null, Usable, new Rect(P(d.UsableLeft, d.UsableTop), P(d.UsableRight, d.UsableBottom)));
            // origin and axes
            dc.DrawLine(Outline, P(0, 0), P(30, 0));
            dc.DrawLine(Outline, P(0, 0), P(0, 30));
            Text(dc, "0,0", P(0, 0) + new Vector(2, 2), Brushes.DimGray);

            Rect R(double[] b) => new Rect(P(b[0], b[3]), P(b[2], b[1]));
            foreach (var (name, box, label) in d.ExistingViewports)
            {
                if (box != null && box.Length >= 4) dc.DrawRectangle(null, Existing, R(box));
                if (label != null && label.Length >= 4) dc.DrawRectangle(null, Existing, R(label));
            }
            foreach (var (name, box) in d.EstimatedLabels)
                if (box != null && box.Length >= 4) { dc.DrawRectangle(null, TitleEstimated, R(box)); Text(dc, name, P(box[0], box[1]) + new Vector(1, 1), TitleEstimated.Brush); }
            foreach (var (name, box) in d.MeasuredLabels)
                if (box != null && box.Length >= 4) dc.DrawRectangle(null, TitleMeasured, R(box));
            foreach (var (name, x, y, members) in d.Overlays)
            {
                var c = P(x, y);
                dc.DrawEllipse(null, Overlay, c, 7, 7);
                dc.DrawLine(Overlay, c + new Vector(-10, 0), c + new Vector(10, 0));
                dc.DrawLine(Overlay, c + new Vector(0, -10), c + new Vector(0, 10));
                Text(dc, name + "  (×" + members + ")", c + new Vector(9, -16), Overlay.Brush);
            }
            foreach (var (name, box) in d.MeasuredBoxes)
            {
                if (box == null || box.Length < 4) continue;
                dc.DrawRectangle(null, Measured, new Rect(P(box[0], box[3]), P(box[2], box[1])));
                Text(dc, name, P(box[0], box[3]) + new Vector(2, 1), Measured.Brush);
            }
            foreach (var (name, x, y, frame) in d.Anchors)
            {
                var c = P(x, y);
                dc.DrawLine(Anchor, c + new Vector(-6, 0), c + new Vector(6, 0));
                dc.DrawLine(Anchor, c + new Vector(0, -6), c + new Vector(0, 6));
                Text(dc, name + (frame == "TitleBlock" ? " (TB)" : ""), c + new Vector(5, -14), Anchor.Brush);
            }
            if (!string.IsNullOrEmpty(d.Title)) Text(dc, d.Title, new Point(margin, 1), Brushes.DimGray);
        }

        private void Text(DrawingContext dc, string text, Point at, Brush brush)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, brush,
                                       VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, at);
        }
    }
}
