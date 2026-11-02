using System;
using ScrollCapture.Core.Imaging;

namespace ScrollCapture.Core.Tests
{
    /// <summary>
    /// A tall "document" (text-like lines with blank gaps) and the viewport screenshots a window
    /// would show of it: a static header, the scrolled part of the page, a static footer.
    /// </summary>
    internal sealed class SyntheticPage
    {
        private const int White = unchecked((int)0xFFFFFFFF);
        private const int Ink = unchecked((int)0xFF202020);

        public SyntheticPage(int width, int height, int seed = 1)
        {
            Page = new PixelFrame(width, height);
            var random = new Random(seed);
            for (int i = 0; i < Page.Pixels.Length; i++)
            {
                Page.Pixels[i] = White;
            }

            for (int line = 0; line * 18 < height; line++)
            {
                if (random.Next(5) == 0)
                {
                    continue; // blank line
                }

                int length = random.Next(width / 4, width - 8);
                for (int row = 3; row < 15 && line * 18 + row < height; row++)
                {
                    for (int x = 4; x < length; x++)
                    {
                        if (random.Next(3) == 0)
                        {
                            Page[x, line * 18 + row] = Ink;
                        }
                    }
                }
            }
        }

        public PixelFrame Page { get; }

        public int HeaderRows { get; set; }

        public int FooterRows { get; set; }

        public int ViewportHeight { get; set; } = 300;

        /// <summary>Right columns drawn as a scroll bar whose thumb follows the position (0 = none).</summary>
        public int ScrollBarColumns { get; set; }

        public int BandHeight => ViewportHeight - HeaderRows - FooterRows;

        public int MaxOffset => Page.Height - BandHeight;

        public PixelFrame Viewport(int offset)
        {
            offset = Math.Max(0, Math.Min(offset, MaxOffset));
            int width = Page.Width;
            var frame = new PixelFrame(width, ViewportHeight);
            for (int y = 0; y < ViewportHeight; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    frame[x, y] = y < HeaderRows ? HeaderColor(x, y)
                        : y >= ViewportHeight - FooterRows ? FooterColor(x, y)
                        : Page[x, offset + y - HeaderRows];
                }
            }

            if (ScrollBarColumns > 0)
            {
                int thumbTop = HeaderRows + (int)((long)offset * (BandHeight - 40) / Math.Max(1, MaxOffset));
                for (int y = HeaderRows; y < ViewportHeight - FooterRows; y++)
                {
                    for (int x = width - ScrollBarColumns; x < width; x++)
                    {
                        frame[x, y] = y >= thumbTop && y < thumbTop + 40 ? unchecked((int)0xFF808080) : unchecked((int)0xFFE0E0E0);
                    }
                }
            }

            return frame;
        }

        /// <summary>What a perfect stitch looks like: header, the whole page, footer.</summary>
        public PixelFrame Expected()
        {
            int width = Page.Width;
            var frame = new PixelFrame(width, HeaderRows + Page.Height + FooterRows);
            var top = Viewport(0);
            var bottom = Viewport(MaxOffset);
            Array.Copy(top.Pixels, 0, frame.Pixels, 0, HeaderRows * width);
            Array.Copy(Page.Pixels, 0, frame.Pixels, HeaderRows * width, Page.Pixels.Length);
            Array.Copy(bottom.Pixels, (ViewportHeight - FooterRows) * width, frame.Pixels, (HeaderRows + Page.Height) * width, FooterRows * width);
            return frame;
        }

        private static int HeaderColor(int x, int y) => unchecked((int)0xFF3060A0) + ((x * 7 + y * 13) % 50);

        private static int FooterColor(int x, int y) => unchecked((int)0xFF404040) + ((x * 3 + y * 5) % 40);
    }
}
