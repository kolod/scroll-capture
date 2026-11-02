using ScrollCapture.Core.Imaging;
using Xunit;

namespace ScrollCapture.Core.Tests
{
    public class BackgroundTrimmerTests
    {
        private const int White = unchecked((int)0xFFFFFFFF);
        private const int Ink = unchecked((int)0xFF202020);

        private static PixelFrame Filled(int width, int height, int color)
        {
            var frame = new PixelFrame(width, height);
            for (int i = 0; i < frame.Pixels.Length; i++)
            {
                frame.Pixels[i] = color;
            }

            return frame;
        }

        private static void Box(PixelFrame frame, int x, int y, int width, int height, int color)
        {
            for (int row = y; row < y + height; row++)
            {
                for (int column = x; column < x + width; column++)
                {
                    frame[column, row] = color;
                }
            }
        }

        [Fact]
        public void Content_IsKeptWithAFivePixelMargin()
        {
            var frame = Filled(200, 300, White);
            Box(frame, 40, 60, 50, 20, Ink);
            Box(frame, 120, 200, 10, 10, Ink);

            var trimmed = BackgroundTrimmer.Trim(frame);

            Assert.Equal(130 - 40 + 10, trimmed.Width);  // content 40..129 plus 5 on each side
            Assert.Equal(210 - 60 + 10, trimmed.Height); // content 60..209 plus 5 on each side
            Assert.Equal(White, trimmed[4, 4]);
            Assert.Equal(Ink, trimmed[5, 5]);
            Assert.Equal(Ink, trimmed[trimmed.Width - 6, trimmed.Height - 6]);
            Assert.Equal(White, trimmed[trimmed.Width - 5, trimmed.Height - 5]);
        }

        [Fact]
        public void MarginIsClampedAtTheImageEdges()
        {
            var frame = Filled(100, 100, White);
            Box(frame, 0, 2, 30, 30, Ink);

            var trimmed = BackgroundTrimmer.Trim(frame);

            Assert.Equal(35, trimmed.Width);  // no room on the left: 0..29 plus 5 on the right
            Assert.Equal(37, trimmed.Height); // 2 rows above (all there is), 30 rows, 5 below
            Assert.Equal(Ink, trimmed[0, 2]);
        }

        [Fact]
        public void BackgroundOnly_IsReturnedUnchanged()
        {
            var frame = Filled(50, 40, White);

            Assert.Same(frame, BackgroundTrimmer.Trim(frame));
        }

        [Fact]
        public void BackgroundColor_IsDetected_EvenWhenAToolbarTouchesTheTop()
        {
            var frame = Filled(300, 400, unchecked((int)0xFFF0F0F0));
            Box(frame, 0, 0, 300, 30, unchecked((int)0xFF3060A0)); // a full-width bar along the top edge
            Box(frame, 100, 150, 40, 40, Ink);

            Assert.Equal(0xF0F0F0, BackgroundTrimmer.DetectBackground(frame));
            var trimmed = BackgroundTrimmer.Trim(frame);
            Assert.Equal(300, trimmed.Width); // the bar spans the whole width
            Assert.Equal(190 + 5, trimmed.Height);
        }

        [Fact]
        public void NoiseWithinTheTolerance_CountsAsBackground()
        {
            var frame = Filled(100, 100, White);
            frame[3, 3] = unchecked((int)0xFFFAFAFA); // almost white
            Box(frame, 50, 50, 10, 10, Ink);

            var content = BackgroundTrimmer.FindContent(frame, BackgroundTrimmer.DetectBackground(frame));

            Assert.Equal(new PixelRect(50, 50, 10, 10), content);
        }
    }
}
