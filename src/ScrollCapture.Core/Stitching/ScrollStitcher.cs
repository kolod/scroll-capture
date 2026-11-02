using System;
using System.Collections.Generic;
using ScrollCapture.Core.Imaging;

namespace ScrollCapture.Core.Stitching
{
    public enum StitchStatus
    {
        /// <summary>The first frame was stored.</summary>
        First,

        /// <summary>The frame was aligned and its new rows were added.</summary>
        Appended,

        /// <summary>The frame shows the same content as the previous one (the end was reached).</summary>
        NoChange,

        /// <summary>The frame could not be aligned with the previous one; nothing was added.</summary>
        NoOverlap,

        /// <summary>The frame has another size than the previous ones (the window was resized).</summary>
        SizeChanged
    }

    public struct StitchResult
    {
        public StitchResult(StitchStatus status, int shift, int addedRows)
        {
            Status = status;
            Shift = shift;
            AddedRows = addedRows;
        }

        public StitchStatus Status { get; }

        /// <summary>Rows the content moved between the previous frame and this one.</summary>
        public int Shift { get; }

        public int AddedRows { get; }
    }

    /// <summary>
    /// Joins screenshots of a viewport taken while its content scrolls down into one tall image.
    /// Rows that stay in place from frame to frame (toolbars, sticky headers, status bars) are
    /// kept once: the header from the first frame and the footer from the last one.
    /// </summary>
    public sealed class ScrollStitcher
    {
        private readonly StitchOptions _options;
        private readonly List<int[]> _strips = new List<int[]>();
        private PixelFrame _last;
        private RowSignatures _lastSignatures;
        private int _committedRows;

        // Rows of the last frame above this index are already in the strips; -1 before the
        // first alignment (the first frame's footer is not known yet).
        private int _end = -1;

        public ScrollStitcher(StitchOptions options = null)
        {
            _options = options ?? new StitchOptions();
        }

        public int Width => _last?.Width ?? 0;

        /// <summary>Height of the image <see cref="Compose"/> would produce now.</summary>
        public int Height => _last == null ? 0 : _committedRows + _last.Height - Math.Max(0, _end);

        public int FrameCount { get; private set; }

        /// <summary>Rows that scroll (between the static header and footer) in the last aligned pair.</summary>
        public int BandHeight { get; private set; }

        public StitchResult Add(PixelFrame frame, int? expectedShift = null)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (_last == null)
            {
                _last = frame;
                _lastSignatures = Signatures(frame);
                BandHeight = frame.Height;
                FrameCount = 1;
                return new StitchResult(StitchStatus.First, 0, frame.Height);
            }

            if (frame.Width != _last.Width || frame.Height != _last.Height)
            {
                return new StitchResult(StitchStatus.SizeChanged, 0, 0);
            }

            var signatures = Signatures(frame);
            if (Unchanged(_lastSignatures, signatures))
            {
                return new StitchResult(StitchStatus.NoChange, 0, 0);
            }

            OverlapFinder.FindStatic(_lastSignatures, signatures, _options.MinOverlapRows * 2, out int top, out int bottom);
            int footerStart = frame.Height - bottom;
            var match = OverlapFinder.Find(_lastSignatures, signatures, top, footerStart, _options, expectedShift);
            if (match.Status != OverlapStatus.Found)
            {
                return new StitchResult(StitchStatus.NoOverlap, 0, 0);
            }

            if (_end < 0)
            {
                Append(_last, 0, footerStart);
                _end = footerStart;
            }
            else if (_end > footerStart)
            {
                // A footer appeared (e.g. a banner): its rows of the previous frame were taken as content.
                _end -= Truncate(_end - footerStart);
            }

            int from = Math.Max(top, _end - match.Shift);
            int added = Math.Max(0, footerStart - from);
            Append(frame, from, added);
            _end = footerStart;
            _last = frame;
            _lastSignatures = signatures;
            BandHeight = footerStart - top;
            FrameCount++;
            return new StitchResult(StitchStatus.Appended, match.Shift, added);
        }

        /// <summary>
        /// True when two screenshots show the same content at the same place, apart from a few
        /// changed rows (a blinking caret, an animation). Used to wait until scrolling settles.
        /// </summary>
        public bool AreUnchanged(PixelFrame previous, PixelFrame next)
        {
            if (previous == null || next == null || previous.Width != next.Width || previous.Height != next.Height)
            {
                return false;
            }

            return Unchanged(Signatures(previous), Signatures(next));
        }

        /// <summary>
        /// The joined image, cut at <paramref name="maxHeight"/> rows when given. With
        /// <paramref name="release"/> the stored rows are freed while they are copied, so a tall
        /// image needs memory for one copy only; the stitcher cannot be used afterwards.
        /// </summary>
        public PixelFrame Compose(int maxHeight = int.MaxValue, bool release = false)
        {
            if (_last == null)
            {
                throw new InvalidOperationException("No frame was added.");
            }

            int width = _last.Width;
            int height = Math.Min(Height, Math.Max(1, maxHeight));
            var pixels = new int[checked(width * height)];
            int offset = 0;
            for (int i = 0; i < _strips.Count; i++)
            {
                var strip = _strips[i];
                int length = Math.Min(strip.Length, pixels.Length - offset);
                Array.Copy(strip, 0, pixels, offset, length);
                offset += length;
                if (release)
                {
                    _strips[i] = null;
                }
            }

            if (release)
            {
                _strips.Clear();
                _committedRows = 0;
            }

            int footerTop = Math.Max(0, _end);
            int footerLength = Math.Min((_last.Height - footerTop) * width, pixels.Length - offset);
            Array.Copy(_last.Pixels, footerTop * width, pixels, offset, footerLength);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] |= unchecked((int)0xFF000000); // opaque, whatever the capture left in alpha
            }

            return new PixelFrame(width, height, pixels);
        }

        private RowSignatures Signatures(PixelFrame frame)
        {
            // Never ignore more than a quarter of a narrow frame.
            int ignore = Math.Max(0, Math.Min(_options.IgnoreRightColumns, frame.Width / 4));
            return RowSignatures.Compute(frame, 0, frame.Width - ignore);
        }

        // Same content at the same place, apart from a few changed rows (caret, animation).
        private bool Unchanged(RowSignatures previous, RowSignatures next)
        {
            int mismatches = 0;
            int evidence = 0;
            for (int y = 0; y < next.Count; y++)
            {
                if (previous.Hashes[y] != next.Hashes[y])
                {
                    mismatches++;
                }
                else if (!next.Uniform[y])
                {
                    evidence++;
                }
            }

            return mismatches == 0 || mismatches <= _options.Tolerance * (mismatches + evidence) / 2;
        }

        private void Append(PixelFrame frame, int top, int count)
        {
            if (count > 0)
            {
                _strips.Add(frame.CopyRows(top, count));
                _committedRows += count;
            }
        }

        // Removes up to rowCount rows from the end, never the last committed row; returns the count removed.
        private int Truncate(int rowCount)
        {
            int width = _last.Width;
            int removed = 0;
            while (removed < rowCount && _strips.Count > 0 && _committedRows > 1)
            {
                var strip = _strips[_strips.Count - 1];
                int rows = strip.Length / width;
                int take = Math.Min(Math.Min(rows, rowCount - removed), _committedRows - 1);
                if (take == rows)
                {
                    _strips.RemoveAt(_strips.Count - 1);
                }
                else
                {
                    var kept = new int[(rows - take) * width];
                    Array.Copy(strip, kept, kept.Length);
                    _strips[_strips.Count - 1] = kept;
                }

                removed += take;
                _committedRows -= take;
            }

            return removed;
        }
    }
}
