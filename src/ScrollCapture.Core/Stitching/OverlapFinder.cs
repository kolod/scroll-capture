using System;

namespace ScrollCapture.Core.Stitching
{
    public enum OverlapStatus
    {
        /// <summary>A single best shift was found.</summary>
        Found,

        /// <summary>The band is one color in both frames, so every shift fits.</summary>
        Ambiguous,

        /// <summary>No shift explains the new frame (it scrolled too far or changed).</summary>
        NotFound
    }

    public struct OverlapMatch
    {
        public OverlapMatch(OverlapStatus status, int shift)
        {
            Status = status;
            Shift = shift;
        }

        public OverlapStatus Status { get; }

        /// <summary>How many rows the content moved up between the frames.</summary>
        public int Shift { get; }
    }

    /// <summary>Finds how far the content moved between two screenshots of the same viewport.</summary>
    public static class OverlapFinder
    {
        // Shifts whose mismatch ratio is this close to the best count as equally good, so the
        // expected shift (from the previous steps) can decide between repeating patterns.
        private const double TieMargin = 0.005;

        /// <summary>
        /// Searches the shift of rows <paramref name="top"/>…<paramref name="bottom"/> (exclusive):
        /// row <c>y</c> of <paramref name="next"/> must equal row <c>y + shift</c> of
        /// <paramref name="previous"/> for the overlapping part of that band.
        /// </summary>
        public static OverlapMatch Find(RowSignatures previous, RowSignatures next, int top, int bottom, StitchOptions options, int? expectedShift)
        {
            if (previous == null || next == null || options == null)
            {
                throw new ArgumentNullException(previous == null ? nameof(previous) : next == null ? nameof(next) : nameof(options));
            }

            int band = bottom - top;
            int minOverlap = Math.Max(1, Math.Min(options.MinOverlapRows, band / 2));
            int bestShift = -1;
            double bestRatio = double.MaxValue;
            int uniformShift = -1;
            var ratios = new double[band];
            var evidences = new int[band];

            for (int shift = 1; shift <= band - minOverlap; shift++)
            {
                int overlap = band - shift;
                int allowed = (int)(options.Tolerance * overlap);
                int mismatches = 0;
                int evidence = 0;
                for (int i = 0; i < overlap; i++)
                {
                    int y = top + i;
                    if (next.Hashes[y] != previous.Hashes[y + shift])
                    {
                        if (++mismatches > allowed)
                        {
                            break;
                        }
                    }
                    else if (!next.Uniform[y])
                    {
                        evidence++;
                    }
                }

                ratios[shift] = double.MaxValue;
                if (mismatches > allowed)
                {
                    continue;
                }

                if (evidence == 0)
                {
                    if (mismatches == 0 && uniformShift < 0)
                    {
                        uniformShift = shift;
                    }

                    continue;
                }

                double ratio = (double)mismatches / (mismatches + evidence);
                if (ratio > options.Tolerance)
                {
                    continue;
                }

                ratios[shift] = ratio;
                evidences[shift] = evidence;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestShift = shift;
                }
            }

            if (bestShift < 0)
            {
                if (uniformShift >= 0)
                {
                    return expectedShift.HasValue && expectedShift.Value > 0 && expectedShift.Value <= band - minOverlap
                        ? new OverlapMatch(OverlapStatus.Found, expectedShift.Value)
                        : new OverlapMatch(OverlapStatus.Ambiguous, 0);
                }

                return new OverlapMatch(OverlapStatus.NotFound, 0);
            }

            if (expectedShift.HasValue)
            {
                // Among equally good shifts (repeating content) take the one nearest to what the
                // scroll step predicts, but only from those resting on at least half the evidence
                // of the strongest: a few rows that happen to match at a huge shift (the short last
                // step at the end of a document) must not win just by being near the prediction.
                int strongest = 0;
                for (int shift = 1; shift < ratios.Length; shift++)
                {
                    if (ratios[shift] <= bestRatio + TieMargin)
                    {
                        strongest = Math.Max(strongest, evidences[shift]);
                    }
                }

                int chosen = -1;
                for (int shift = 1; shift < ratios.Length; shift++)
                {
                    if (ratios[shift] <= bestRatio + TieMargin
                        && evidences[shift] * 2 >= strongest
                        && (chosen < 0 || Math.Abs(shift - expectedShift.Value) < Math.Abs(chosen - expectedShift.Value)))
                    {
                        chosen = shift;
                    }
                }

                return new OverlapMatch(OverlapStatus.Found, chosen);
            }

            // Without a prediction the best shift wins; bestShift is already the smallest of equals.
            return new OverlapMatch(OverlapStatus.Found, bestShift);
        }

        /// <summary>
        /// Rows that did not move: the number of equal rows at the same position from the top and
        /// from the bottom (toolbars, sticky headers, status bars). Leaves a band of at least
        /// <paramref name="minBand"/> rows.
        /// </summary>
        public static void FindStatic(RowSignatures previous, RowSignatures next, int minBand, out int top, out int bottom)
        {
            int count = Math.Min(previous.Count, next.Count);
            top = 0;
            while (top < count && previous.Hashes[top] == next.Hashes[top])
            {
                top++;
            }

            bottom = 0;
            while (bottom < count - top && previous.Hashes[count - 1 - bottom] == next.Hashes[count - 1 - bottom])
            {
                bottom++;
            }

            int maxStatic = Math.Max(0, count - Math.Max(minBand, count / 4));
            if (top + bottom > maxStatic)
            {
                // Blank areas above or below the content look static; give the band its space back.
                int total = top + bottom;
                top = (int)((long)top * maxStatic / total);
                bottom = maxStatic - top;
            }
        }
    }
}
