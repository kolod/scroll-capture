using System;
using System.Collections.Generic;

namespace ScrollCapture.Core.Imaging
{
    /// <summary>A rectangle in pixel coordinates of a frame.</summary>
    public struct PixelRect
    {
        public PixelRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public bool IsEmpty => Width <= 0 || Height <= 0;
    }

    /// <summary>
    /// Cuts away the plain background around the useful content: the background color is the
    /// most common color along the edges of the image, the content is everything that differs
    /// from it, and a margin of background is kept around the content.
    /// </summary>
    public static class BackgroundTrimmer
    {
        public const int DefaultMargin = 5;

        /// <summary>Largest difference per color channel still counted as background (noise, dithering).</summary>
        public const int DefaultTolerance = 8;

        /// <summary>The most common color of the outermost rows and columns (alpha ignored).</summary>
        public static int DetectBackground(PixelFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var counts = new Dictionary<int, int>();
            void Count(int x, int y)
            {
                int color = frame[x, y] & PixelFrame.ColorMask;
                counts.TryGetValue(color, out int n);
                counts[color] = n + 1;
            }

            for (int x = 0; x < frame.Width; x++)
            {
                Count(x, 0);
                Count(x, frame.Height - 1);
            }

            for (int y = 1; y < frame.Height - 1; y++)
            {
                Count(0, y);
                Count(frame.Width - 1, y);
            }

            int best = 0;
            int bestCount = -1;
            foreach (var pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }

            return best;
        }

        /// <summary>The smallest rectangle holding every pixel that is not background; empty when there is none.</summary>
        public static PixelRect FindContent(PixelFrame frame, int background, int tolerance = DefaultTolerance)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            int left = frame.Width;
            int right = -1;
            int top = -1;
            int bottom = -1;
            for (int y = 0; y < frame.Height; y++)
            {
                int row = y * frame.Width;
                int first = -1;
                for (int x = 0; x < frame.Width; x++)
                {
                    if (!IsBackground(frame.Pixels[row + x], background, tolerance))
                    {
                        first = x;
                        break;
                    }
                }

                if (first < 0)
                {
                    continue;
                }

                int last = first;
                for (int x = frame.Width - 1; x > first; x--)
                {
                    if (!IsBackground(frame.Pixels[row + x], background, tolerance))
                    {
                        last = x;
                        break;
                    }
                }

                if (top < 0)
                {
                    top = y;
                }

                bottom = y;
                left = Math.Min(left, first);
                right = Math.Max(right, last);
            }

            return top < 0 ? new PixelRect(0, 0, 0, 0) : new PixelRect(left, top, right - left + 1, bottom - top + 1);
        }

        /// <summary>
        /// The frame without the background at its edges, keeping <paramref name="margin"/> pixels
        /// around the content (fewer where the content is closer to an edge). Returns the frame
        /// itself when nothing can be cut or the image is background only.
        /// </summary>
        public static PixelFrame Trim(PixelFrame frame, int margin = DefaultMargin, int tolerance = DefaultTolerance)
        {
            var content = FindContent(frame, DetectBackground(frame), tolerance);
            if (content.IsEmpty)
            {
                return frame;
            }

            int left = Math.Max(0, content.X - margin);
            int top = Math.Max(0, content.Y - margin);
            int right = Math.Min(frame.Width, content.X + content.Width + margin);
            int bottom = Math.Min(frame.Height, content.Y + content.Height + margin);
            if (left == 0 && top == 0 && right == frame.Width && bottom == frame.Height)
            {
                return frame;
            }

            return Crop(frame, new PixelRect(left, top, right - left, bottom - top));
        }

        public static PixelFrame Crop(PixelFrame frame, PixelRect area)
        {
            if (area.IsEmpty || area.X < 0 || area.Y < 0 || area.X + area.Width > frame.Width || area.Y + area.Height > frame.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(area));
            }

            var result = new PixelFrame(area.Width, area.Height);
            for (int y = 0; y < area.Height; y++)
            {
                Array.Copy(frame.Pixels, (area.Y + y) * frame.Width + area.X, result.Pixels, y * area.Width, area.Width);
            }

            return result;
        }

        private static bool IsBackground(int pixel, int background, int tolerance)
        {
            return Math.Abs(((pixel >> 16) & 0xFF) - ((background >> 16) & 0xFF)) <= tolerance
                && Math.Abs(((pixel >> 8) & 0xFF) - ((background >> 8) & 0xFF)) <= tolerance
                && Math.Abs((pixel & 0xFF) - (background & 0xFF)) <= tolerance;
        }
    }
}
