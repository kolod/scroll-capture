using System;
using ScrollCapture.Core.Imaging;

namespace ScrollCapture.Core.Stitching
{
    /// <summary>
    /// A 64-bit hash of every row of a frame over a column range, plus whether the row is a
    /// single color. Rows of one color (blank margins, empty lines) match anywhere, so they do
    /// not count as evidence for an alignment.
    /// </summary>
    public sealed class RowSignatures
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private RowSignatures(long[] hashes, bool[] uniform)
        {
            Hashes = hashes;
            Uniform = uniform;
        }

        public long[] Hashes { get; }

        public bool[] Uniform { get; }

        public int Count => Hashes.Length;

        /// <summary>Signatures of columns <paramref name="left"/> (inclusive) to <paramref name="right"/> (exclusive).</summary>
        public static RowSignatures Compute(PixelFrame frame, int left, int right)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            left = Math.Max(0, Math.Min(left, frame.Width - 1));
            right = Math.Max(left + 1, Math.Min(right, frame.Width));

            var hashes = new long[frame.Height];
            var uniform = new bool[frame.Height];
            var pixels = frame.Pixels;
            for (int y = 0; y < frame.Height; y++)
            {
                int start = y * frame.Width + left;
                int end = y * frame.Width + right;
                int first = pixels[start] & PixelFrame.ColorMask;
                bool same = true;
                ulong hash = FnvOffset;
                for (int i = start; i < end; i++)
                {
                    int color = pixels[i] & PixelFrame.ColorMask;
                    same &= color == first;
                    hash = (hash ^ (uint)color) * FnvPrime;
                }

                hashes[y] = unchecked((long)hash);
                uniform[y] = same;
            }

            return new RowSignatures(hashes, uniform);
        }
    }
}
