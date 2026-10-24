using System;

namespace CausaFix.ProjectSetup.Geometry
{
    public readonly struct P2
    {
        public readonly double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public double Length => Math.Sqrt(X * X + Y * Y);
        public override string ToString() => "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ")";
    }

    /// <summary>
    /// 2D affine transform p' = [a b; c d]·p + (tx, ty). Mirrors the XY part of Revit Transforms
    /// (model → view projection → sheet) so the alignment rules can be tested without Revit.
    /// </summary>
    public readonly struct Affine2
    {
        public readonly double A, B, C, D, Tx, Ty;

        public Affine2(double a, double b, double c, double d, double tx, double ty) { A = a; B = b; C = c; D = d; Tx = tx; Ty = ty; }

        public static readonly Affine2 Identity = new Affine2(1, 0, 0, 1, 0, 0);

        public static Affine2 Translation(double x, double y) => new Affine2(1, 0, 0, 1, x, y);

        /// <summary>Uniform scale s, then rotation by angle (radians), then translation.</summary>
        public static Affine2 Similarity(double scale, double angleRad, double tx, double ty)
        {
            double cs = Math.Cos(angleRad) * scale, sn = Math.Sin(angleRad) * scale;
            return new Affine2(cs, -sn, sn, cs, tx, ty);
        }

        public P2 Apply(P2 p) => new P2(A * p.X + B * p.Y + Tx, C * p.X + D * p.Y + Ty);

        /// <summary>this ∘ inner: applies <paramref name="inner"/> first, then this (same order as Revit's Transform.Multiply).</summary>
        public Affine2 Compose(Affine2 inner) => new Affine2(
            A * inner.A + B * inner.C, A * inner.B + B * inner.D,
            C * inner.A + D * inner.C, C * inner.B + D * inner.D,
            A * inner.Tx + B * inner.Ty + Tx, C * inner.Tx + D * inner.Ty + Ty);

        public double Determinant => A * D - B * C;

        public Affine2 Inverse()
        {
            double det = Determinant;
            if (Math.Abs(det) < 1e-15) throw new InvalidOperationException("Transform is not invertible.");
            double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
            return new Affine2(ia, ib, ic, id, -(ia * Tx + ib * Ty), -(ic * Tx + id * Ty));
        }

        /// <summary>Scale factor (paper length per model length).</summary>
        public double ScaleFactor => Math.Sqrt(Math.Abs(Determinant));

        /// <summary>Rotation angle in radians.</summary>
        public double Angle => Math.Atan2(C, A);
    }

    public enum AlignmentOutcome
    {
        /// <summary>Within tolerance and, when checked, same scale/orientation as the reference.</summary>
        Aligned = 0,
        /// <summary>Anchor within tolerance but scale or orientation differ from the reference: not a full overlay.</summary>
        AnchorAlignedDifferentScaleOrientation,
        /// <summary>Error larger than the tolerance after moving.</summary>
        OutOfTolerance,
        /// <summary>Not attempted (unsupported crop, no anchor...).</summary>
        Blocked
    }

    public static class AlignmentMath
    {
        public const double MmPerFoot = 304.8;

        public static double FeetToMm(double ft) => ft * MmPerFoot;
        public static double MmToFeet(double mm) => mm / MmPerFoot;

        /// <summary>Translation (sheet units) that moves the projected anchor onto the target.</summary>
        public static P2 Delta(Affine2 modelToSheet, P2 modelAnchor, P2 sheetTarget) => sheetTarget - modelToSheet.Apply(modelAnchor);

        /// <summary>Distance between where the anchor lands and the target, in sheet units.</summary>
        public static double Error(Affine2 modelToSheet, P2 modelAnchor, P2 sheetTarget) => (modelToSheet.Apply(modelAnchor) - sheetTarget).Length;

        /// <summary>
        /// Maps a target expressed relative to the reference title block onto another sheet's title block:
        /// target_dest = TB_dest ∘ TB_ref⁻¹ (target_ref).
        /// </summary>
        public static P2 TransferBetweenTitleBlocks(P2 targetOnReferenceSheet, Affine2 referenceTitleBlock, Affine2 destinationTitleBlock)
            => destinationTitleBlock.Apply(referenceTitleBlock.Inverse().Apply(targetOnReferenceSheet));

        /// <summary>True when two model→sheet transforms have the same scale and orientation (full overlay possible).</summary>
        public static bool SameScaleAndOrientation(Affine2 a, Affine2 b, double relTolerance = 1e-6)
        {
            double sa = a.ScaleFactor, sb = b.ScaleFactor;
            if (Math.Abs(sa - sb) > relTolerance * Math.Max(sa, sb)) return false;
            double da = Math.Atan2(Math.Sin(a.Angle - b.Angle), Math.Cos(a.Angle - b.Angle));
            if (Math.Abs(da) > 1e-6) return false;
            return Math.Sign(a.Determinant) == Math.Sign(b.Determinant);
        }

        /// <summary>
        /// Classifies the result after moving. The second point (if any) is checked against where it lands on
        /// the reference transform; without a second point the scale/orientation comparison decides.
        /// </summary>
        public static AlignmentOutcome Classify(Affine2 finalTransform, P2 anchor, P2 target, double toleranceSheetUnits,
                                                Affine2? reference, P2? secondPoint, out double errorAnchor, out double errorSecond)
        {
            errorAnchor = Error(finalTransform, anchor, target);
            errorSecond = 0;
            if (errorAnchor > toleranceSheetUnits) return AlignmentOutcome.OutOfTolerance;
            if (reference.HasValue)
            {
                bool same = SameScaleAndOrientation(finalTransform, reference.Value);
                if (secondPoint.HasValue)
                {
                    // Where would the second point be if the views overlaid perfectly?
                    P2 expected = reference.Value.Apply(secondPoint.Value) - reference.Value.Apply(anchor) + target;
                    errorSecond = (finalTransform.Apply(secondPoint.Value) - expected).Length;
                    if (errorSecond > toleranceSheetUnits) return AlignmentOutcome.AnchorAlignedDifferentScaleOrientation;
                }
                if (!same) return AlignmentOutcome.AnchorAlignedDifferentScaleOrientation;
            }
            return AlignmentOutcome.Aligned;
        }
    }
}

namespace CausaFix.ProjectSetup.Geometry
{
    /// <summary>A viewport whose current model→sheet transform can be measured and that can be translated on the sheet.</summary>
    public interface IViewportProbe
    {
        /// <summary>Current model→sheet transform (XY), measured after regeneration. Units: feet of paper.</summary>
        Affine2 MeasureModelToSheet();
        /// <summary>Current viewport box centre on the sheet (feet).</summary>
        P2 BoxCenter();
        /// <summary>Moves the viewport box by <paramref name="delta"/> (feet of paper) and regenerates.</summary>
        void Translate(P2 delta);
    }

    public sealed class AlignmentReport
    {
        public AlignmentOutcome Outcome { get; set; }
        public P2 Target { get; set; }
        public P2 Final { get; set; }
        public double ErrorFeet { get; set; }
        public double SecondErrorFeet { get; set; }
        public bool Moved { get; set; }
        public string Message { get; set; } = string.Empty;
        public double ErrorMm => AlignmentMath.FeetToMm(ErrorFeet);
    }

    /// <summary>
    /// The alignment loop shared by the Revit executor and the tests: measure → translate by the delta →
    /// regenerate → measure again. Moves nothing when already within tolerance (re-runs do not shift viewports).
    /// </summary>
    public static class AlignmentRunner
    {
        public static AlignmentReport AlignAnchor(IViewportProbe vp, P2 modelAnchor, P2 target, double toleranceFeet,
                                                  Affine2? reference = null, P2? second = null, int maxIterations = 2)
        {
            var rep = new AlignmentReport { Target = target };
            var m = vp.MeasureModelToSheet();
            for (int i = 0; i < maxIterations; i++)
            {
                double err = AlignmentMath.Error(m, modelAnchor, target);
                if (err <= toleranceFeet) break;
                vp.Translate(AlignmentMath.Delta(m, modelAnchor, target));
                rep.Moved = true;
                m = vp.MeasureModelToSheet();
            }
            rep.Outcome = AlignmentMath.Classify(m, modelAnchor, target, toleranceFeet, reference, second, out double e1, out double e2);
            rep.Final = m.Apply(modelAnchor);
            rep.ErrorFeet = e1;
            rep.SecondErrorFeet = e2;
            rep.Message = rep.Outcome == AlignmentOutcome.Aligned ? "Aligned within tolerance."
                        : rep.Outcome == AlignmentOutcome.AnchorAlignedDifferentScaleOrientation ? "Anchor aligned; different scale/orientation – not a full overlay."
                        : "Out of tolerance after moving (error " + rep.ErrorMm.ToString("0.###") + " mm).";
            return rep;
        }

        /// <summary>Viewport Center Placement: box centre to the target (layout only).</summary>
        public static AlignmentReport PlaceCenter(IViewportProbe vp, P2 target, double toleranceFeet)
        {
            var rep = new AlignmentReport { Target = target };
            var c = vp.BoxCenter();
            if ((c - target).Length > toleranceFeet) { vp.Translate(target - c); rep.Moved = true; c = vp.BoxCenter(); }
            rep.Final = c;
            rep.ErrorFeet = (c - target).Length;
            rep.Outcome = rep.ErrorFeet <= toleranceFeet ? AlignmentOutcome.Aligned : AlignmentOutcome.OutOfTolerance;
            rep.Message = rep.Outcome == AlignmentOutcome.Aligned ? "Viewport centre placed (layout only; building match not guaranteed)." : "Centre out of tolerance.";
            return rep;
        }
    }
}
