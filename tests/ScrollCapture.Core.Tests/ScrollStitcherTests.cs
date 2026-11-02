using System.Collections.Generic;
using ScrollCapture.Core.Imaging;
using ScrollCapture.Core.Stitching;
using Xunit;

namespace ScrollCapture.Core.Tests
{
    public class ScrollStitcherTests
    {
        private static PixelFrame Stitch(SyntheticPage page, IEnumerable<int> offsets, out List<StitchResult> results)
        {
            var stitcher = new ScrollStitcher();
            results = new List<StitchResult>();
            foreach (var offset in offsets)
            {
                results.Add(stitcher.Add(page.Viewport(offset)));
            }

            return stitcher.Compose();
        }

        private static IEnumerable<int> Steps(SyntheticPage page, int step)
        {
            for (int offset = 0; ; offset += step)
            {
                yield return offset;
                if (offset >= page.MaxOffset)
                {
                    yield break;
                }
            }
        }

        private static void AssertSameImage(PixelFrame expected, PixelFrame actual, int ignoreRightColumns = 0)
        {
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            for (int y = 0; y < expected.Height; y++)
            {
                for (int x = 0; x < expected.Width - ignoreRightColumns; x++)
                {
                    if (((expected[x, y] ^ actual[x, y]) & PixelFrame.ColorMask) != 0)
                    {
                        Assert.Fail("Pixel differs at " + x + "," + y);
                    }
                }
            }
        }

        [Theory]
        [InlineData(0, 0, 120)]
        [InlineData(40, 0, 150)]
        [InlineData(40, 25, 100)]
        [InlineData(0, 30, 173)]
        public void ScrolledViewports_AreJoinedIntoThePage(int header, int footer, int step)
        {
            var page = new SyntheticPage(240, 2000) { HeaderRows = header, FooterRows = footer };

            var image = Stitch(page, Steps(page, step), out var results);

            Assert.Equal(StitchStatus.First, results[0].Status);
            Assert.All(results.GetRange(1, results.Count - 1), r => Assert.Equal(StitchStatus.Appended, r.Status));
            AssertSameImage(page.Expected(), image);
        }

        [Fact]
        public void ScrollBarInsideTheViewport_DoesNotBreakTheAlignment()
        {
            var page = new SyntheticPage(260, 1500) { HeaderRows = 30, ScrollBarColumns = 16 };

            var image = Stitch(page, Steps(page, 140), out var results);

            Assert.All(results.GetRange(1, results.Count - 1), r => Assert.Equal(StitchStatus.Appended, r.Status));
            AssertSameImage(page.Expected(), image, ignoreRightColumns: 16);
        }

        [Fact]
        public void SameFrameAgain_IsReportedAsNoChange()
        {
            var page = new SyntheticPage(200, 1000);
            var stitcher = new ScrollStitcher();
            stitcher.Add(page.Viewport(0));

            var result = stitcher.Add(page.Viewport(0));

            Assert.Equal(StitchStatus.NoChange, result.Status);
            Assert.Equal(page.ViewportHeight, stitcher.Height);
        }

        [Fact]
        public void FrameWithoutOverlap_IsRejectedAndNothingIsAdded()
        {
            var page = new SyntheticPage(200, 2000);
            var stitcher = new ScrollStitcher();
            stitcher.Add(page.Viewport(0));

            var result = stitcher.Add(page.Viewport(page.ViewportHeight + 50));

            Assert.Equal(StitchStatus.NoOverlap, result.Status);
            Assert.Equal(page.ViewportHeight, stitcher.Height);
        }

        [Fact]
        public void ResizedFrame_IsReported()
        {
            var stitcher = new ScrollStitcher();
            stitcher.Add(new PixelFrame(100, 100));

            Assert.Equal(StitchStatus.SizeChanged, stitcher.Add(new PixelFrame(100, 120)).Status);
        }

        [Fact]
        public void HoverHighlightAtAFixedPlace_IsTolerated()
        {
            var page = new SyntheticPage(240, 3000) { ViewportHeight = 700 };
            var stitcher = new ScrollStitcher();
            int offset = 0;
            var first = page.Viewport(0);
            stitcher.Add(first);
            for (int step = 0; step < 5; step++)
            {
                offset += 310;
                var frame = page.Viewport(offset);
                for (int y = 350; y < 368; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        frame[x, y] ^= 0x00101010; // a highlighted row under the mouse pointer
                    }
                }

                var result = stitcher.Add(frame, 310);
                Assert.Equal(StitchStatus.Appended, result.Status);
                Assert.Equal(310, result.Shift);
            }
        }

        [Fact]
        public void LastShortStep_AtTheEndOfThePage_IsDetected()
        {
            var page = new SyntheticPage(240, 1000) { HeaderRows = 20, FooterRows = 20 };
            var offsets = new List<int> { 0, 200, 400, 600, page.MaxOffset };

            var image = Stitch(page, offsets, out var results);

            Assert.Equal(page.MaxOffset - 600, results[4].Shift);
            AssertSameImage(page.Expected(), image);
        }

        [Fact]
        public void Compose_RespectsMaximumHeight()
        {
            var page = new SyntheticPage(200, 2000);
            var image = Stitch(page, Steps(page, 150), out _);
            Assert.Equal(2000, image.Height);

            var stitcher = new ScrollStitcher();
            foreach (var offset in Steps(page, 150))
            {
                stitcher.Add(page.Viewport(offset));
            }

            Assert.Equal(700, stitcher.Compose(700).Height);
        }
    }
}
