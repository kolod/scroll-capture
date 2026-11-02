using ScrollCapture.Core.Imaging;
using ScrollCapture.Core.Stitching;
using Xunit;

namespace ScrollCapture.Core.Tests
{
    public class OverlapFinderTests
    {
        private static readonly StitchOptions Options = new StitchOptions { IgnoreRightColumns = 0 };

        // Rows repeating every `period` rows, like a table of identical lines.
        private static PixelFrame Periodic(int width, int height, int period, int offset)
        {
            var frame = new PixelFrame(width, height);
            for (int y = 0; y < height; y++)
            {
                int phase = (y + offset) % period;
                for (int x = 0; x < width; x++)
                {
                    frame[x, y] = phase < 3 ? 0x202020 : x == phase ? 0x808080 : 0xFFFFFF;
                }
            }

            return frame;
        }

        [Fact]
        public void RepeatingContent_UsesTheExpectedShift()
        {
            var previous = RowSignatures.Compute(Periodic(64, 300, 25, 0), 0, 64);
            var next = RowSignatures.Compute(Periodic(64, 300, 25, 100), 0, 64);

            var withHint = OverlapFinder.Find(previous, next, 0, 300, Options, 100);
            var withoutHint = OverlapFinder.Find(previous, next, 0, 300, Options, null);

            Assert.Equal(OverlapStatus.Found, withHint.Status);
            Assert.Equal(100, withHint.Shift);
            Assert.Equal(25, withoutHint.Shift);
        }

        [Fact]
        public void TinyCoincidentalOverlap_DoesNotWinOverTheRealShift_EvenNearTheHint()
        {
            // The last step at the end of a document is short; the bottom rows of the previous
            // frame happen to equal the top rows of the next one at a much larger shift.
            const int width = 40;
            const int height = 100;
            var random = new System.Random(7);
            var rows = new int[height + 36][];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new int[width];
                for (int x = 0; x < width; x++)
                {
                    rows[i][x] = random.Next(0x1000000);
                }
            }

            for (int i = 0; i < 11; i++)
            {
                rows[89 + i] = rows[36 + i];
            }

            var previous = new PixelFrame(width, height);
            var next = new PixelFrame(width, height);
            for (int y = 0; y < height; y++)
            {
                System.Array.Copy(rows[y], 0, previous.Pixels, y * width, width);
                System.Array.Copy(rows[y + 36], 0, next.Pixels, y * width, width);
            }

            var match = OverlapFinder.Find(RowSignatures.Compute(previous, 0, width), RowSignatures.Compute(next, 0, width), 0, height, Options, 80);

            Assert.Equal(OverlapStatus.Found, match.Status);
            Assert.Equal(36, match.Shift);
        }

        [Fact]
        public void BlankBand_IsAmbiguousWithoutAHint()
        {
            var blank = RowSignatures.Compute(new PixelFrame(32, 100), 0, 32);

            Assert.Equal(OverlapStatus.Ambiguous, OverlapFinder.Find(blank, blank, 0, 100, Options, null).Status);
            var hinted = OverlapFinder.Find(blank, blank, 0, 100, Options, 40);
            Assert.Equal(OverlapStatus.Found, hinted.Status);
            Assert.Equal(40, hinted.Shift);
        }

        [Fact]
        public void StaticRows_AreFoundAtBothEdges()
        {
            var page = new SyntheticPage(200, 1200) { HeaderRows = 30, FooterRows = 12 };
            var a = RowSignatures.Compute(page.Viewport(0), 0, 200);
            var b = RowSignatures.Compute(page.Viewport(97), 0, 200);

            OverlapFinder.FindStatic(a, b, 16, out int top, out int bottom);

            // Blank page rows right below the header may also look static; never fewer than the real ones.
            Assert.InRange(top, 30, 60);
            Assert.InRange(bottom, 12, 40);
        }
    }
}
