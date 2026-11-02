namespace ScrollCapture.Core.Stitching
{
    /// <summary>Tuning of the alignment between consecutive screenshots.</summary>
    public sealed class StitchOptions
    {
        /// <summary>
        /// Columns at the right edge left out of the comparison: a scroll bar inside the captured
        /// area (browsers, Electron apps) moves its thumb and never matches.
        /// </summary>
        public int IgnoreRightColumns { get; set; } = 24;

        /// <summary>Fewest overlapping rows of the scrolling band that an alignment may rest on.</summary>
        public int MinOverlapRows { get; set; } = 8;

        /// <summary>
        /// Share of the non-blank overlapping rows that may differ (hover highlights, a blinking
        /// caret, a sticky header that appeared while scrolling). A highlight at a fixed place
        /// counts twice: once in each frame. A wrong shift mismatches almost every row.
        /// </summary>
        public double Tolerance { get; set; } = 0.2;
    }
}
