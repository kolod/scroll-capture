using System;

namespace ScrollCapture.Core.Stitching
{
    /// <summary>
    /// Chooses how many scroll units (wheel notches, scroll-bar lines) to send per step: it
    /// learns how many pixels one unit moves and aims at a shift of a fixed share of the
    /// scrolling band, so consecutive screenshots always overlap.
    /// </summary>
    public sealed class ScrollStepPlanner
    {
        private const int StepsToRelax = 3;

        private readonly int _maxUnits;
        private readonly double _targetShare;
        private long _measuredPixels;
        private long _measuredUnits;
        private int _limit;
        private int _goodSteps;

        public ScrollStepPlanner(int initialUnits, int maxUnits, double targetShare = 0.6)
        {
            if (initialUnits < 1 || maxUnits < initialUnits)
            {
                throw new ArgumentOutOfRangeException(nameof(initialUnits));
            }

            InitialUnits = initialUnits;
            _maxUnits = maxUnits;
            _limit = maxUnits;
            _targetShare = targetShare;
        }

        public int InitialUnits { get; }

        /// <summary>Average pixels per unit, or null before the first measured step.</summary>
        public double? PixelsPerUnit => _measuredUnits == 0 ? (double?)null : (double)_measuredPixels / _measuredUnits;

        /// <summary>Units for the next step through a band of <paramref name="bandHeight"/> rows.</summary>
        public int Next(int bandHeight)
        {
            var perUnit = PixelsPerUnit;
            if (!perUnit.HasValue || perUnit.Value <= 0)
            {
                return Math.Min(InitialUnits, _limit);
            }

            int units = (int)Math.Floor(bandHeight * _targetShare / perUnit.Value);
            return Math.Max(1, Math.Min(_limit, units));
        }

        /// <summary>The shift a step of <paramref name="units"/> should produce, if known.</summary>
        public int? ExpectedShift(int units)
        {
            var perUnit = PixelsPerUnit;
            return perUnit.HasValue ? (int?)Math.Max(1, (int)Math.Round(perUnit.Value * units)) : null;
        }

        /// <summary>
        /// Records a step that moved the content by <paramref name="shift"/> rows. After a few
        /// good steps in a row a limit set by <see cref="TooFar"/> is relaxed again, so one
        /// glitch does not slow down the rest of a long capture.
        /// </summary>
        public void Record(int units, int shift)
        {
            if (units > 0 && shift > 0)
            {
                _measuredUnits += units;
                _measuredPixels += shift;
            }

            if (_limit < _maxUnits && ++_goodSteps >= StepsToRelax)
            {
                _limit = Math.Min(_maxUnits, _limit * 2);
                _goodSteps = 0;
            }
        }

        /// <summary>
        /// A step of <paramref name="units"/> went too far to align: later steps stay below it.
        /// Returns false when no smaller step is possible.
        /// </summary>
        public bool TooFar(int units)
        {
            if (units <= 1)
            {
                return false;
            }

            _limit = Math.Max(1, units / 2);
            _goodSteps = 0;
            return true;
        }
    }
}
