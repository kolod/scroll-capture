using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ScrollCapture.App.Platform;
using ScrollCapture.Core.Imaging;
using ScrollCapture.Core.Stitching;

namespace ScrollCapture.App.Capture
{
    internal enum StopReason
    {
        EndReached,
        NotScrollable,
        MaxHeight,
        SizeLimit,
        OutOfMemory,
        Cancelled,
        AlignmentFailed,
        WindowChanged
    }

    internal sealed class CaptureOptions
    {
        public ScrollMethod ScrollMethod { get; set; } = ScrollMethod.Auto;

        public CaptureMethod CaptureMethod { get; set; } = CaptureMethod.Screen;

        /// <summary>Wait after each scroll before the first screenshot (smooth scrolling, lazy loading).</summary>
        public int DelayMs { get; set; } = 250;

        /// <summary>Optional height limit (command line); the image is otherwise limited only by <see cref="CaptureSession.MaxPixels"/>.</summary>
        public int MaxHeight { get; set; } = int.MaxValue;

        public bool StartFromTop { get; set; } = true;

        public StitchOptions Stitch { get; set; } = new StitchOptions();

        /// <summary>Diagnostics: when set, every screenshot given to the stitcher is saved there as a PNG.</summary>
        public string FrameFolder { get; set; }
    }

    internal sealed class CaptureResult
    {
        public CaptureResult(PixelFrame image, int frames, StopReason reason)
        {
            Image = image;
            Frames = frames;
            Reason = reason;
        }

        public PixelFrame Image { get; }

        public int Frames { get; }

        public StopReason Reason { get; }
    }

    /// <summary>
    /// One scrolling capture: scrolls the target step by step, waits until each screenshot is
    /// stable, and joins them. Esc stops it; what was captured so far is kept.
    /// </summary>
    internal sealed class CaptureSession
    {
        /// <summary>
        /// The largest image there can be: one .NET array holds at most 2 GB, 4 bytes per pixel
        /// (e.g. 1920 × 260 000 px).
        /// </summary>
        public const long MaxPixels = 500000000;

        private const int StableChecks = 12;
        private const int StablePollMs = 70;
        private const int TopSearchSteps = 200;
        private const int TopStepUnits = 20;

        private readonly CaptureTarget _target;
        private readonly CaptureOptions _options;
        private readonly ScrollStitcher _stitcher;
        private bool _stopped;
        private int _savedFrames;

        public CaptureSession(CaptureTarget target, CaptureOptions options)
        {
            _target = target;
            _options = options;
            _stitcher = new ScrollStitcher(options.Stitch);
        }

        /// <summary>Called after every added screenshot with the frame count and the image height.</summary>
        public event Action<int, int> Progress;

        public async Task<CaptureResult> RunAsync()
        {
            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            StopReason reason;
            try
            {
                reason = await CaptureAsync();
            }
            catch (Exception ex) when (IsCaptureFailure(ex) && _stitcher.FrameCount > 0)
            {
                reason = StopReason.WindowChanged; // keep what was captured before the window went away
            }
            catch (OutOfMemoryException) when (_stitcher.FrameCount > 0)
            {
                reason = StopReason.OutOfMemory; // keep what fits
            }
            finally
            {
                NativeMethods.SetCursorPos(cursor.X, cursor.Y);
            }

            // Frees the stored rows while copying them: memory for one copy of the image is enough.
            var image = await Task.Run(() => _stitcher.Compose(_options.MaxHeight, release: true));
            return new CaptureResult(image, _stitcher.FrameCount, reason);
        }

        public static bool IsCaptureFailure(Exception ex)
        {
            return ex is Win32Exception || ex is ExternalException || ex is InvalidOperationException || ex is ArgumentException;
        }

        private async Task<StopReason> CaptureAsync()
        {
            var scroller = new Scroller(_target, _options.ScrollMethod);
            if (_options.StartFromTop)
            {
                await ScrollToTopAsync(scroller);
            }

            var first = await GrabStableAsync(0);
            _stitcher.Add(first);
            SaveFrame(first, "First");
            Progress?.Invoke(_stitcher.FrameCount, _stitcher.Height);
            var planner = new ScrollStepPlanner(scroller.InitialUnits, scroller.MaxUnits);

            while (true)
            {
                if (_stopped)
                {
                    return StopReason.Cancelled;
                }

                if (_stitcher.Height >= _options.MaxHeight)
                {
                    return StopReason.MaxHeight;
                }

                // A step adds at most one screenshot's height.
                if ((long)(_stitcher.Height + first.Height) * _stitcher.Width > MaxPixels)
                {
                    return StopReason.SizeLimit;
                }

                if (!NativeMethods.IsWindow(_target.Window))
                {
                    return StopReason.WindowChanged;
                }

                int units = planner.Next(_stitcher.BandHeight);
                scroller.Down(units);
                var frame = await GrabStableAsync(_options.DelayMs);
                if (_stopped)
                {
                    return StopReason.Cancelled;
                }

                var result = await AddAsync(frame, planner.ExpectedShift(units));
                if (result.Status == StitchStatus.NoOverlap)
                {
                    // Still moving, or content loaded late: look once more before giving up on this step.
                    frame = await GrabStableAsync(_options.DelayMs * 2 + 300);
                    result = await AddAsync(frame, planner.ExpectedShift(units));
                }

                switch (result.Status)
                {
                    case StitchStatus.Appended:
                        planner.Record(units, result.Shift);
                        Progress?.Invoke(_stitcher.FrameCount, _stitcher.Height);
                        break;

                    case StitchStatus.NoChange:
                        if (_stitcher.FrameCount == 1 && scroller.TryNextMethod())
                        {
                            planner = new ScrollStepPlanner(scroller.InitialUnits, scroller.MaxUnits);
                            break;
                        }

                        return _stitcher.FrameCount == 1 ? StopReason.NotScrollable : StopReason.EndReached;

                    case StitchStatus.SizeChanged:
                        return StopReason.WindowChanged;

                    default:
                        if (!planner.TooFar(units))
                        {
                            return StopReason.AlignmentFailed;
                        }

                        // The step jumped past the overlap: go back and continue with smaller steps.
                        scroller.Up(units);
                        await GrabStableAsync(_options.DelayMs);
                        break;
                }
            }
        }

        private async Task<StitchResult> AddAsync(PixelFrame frame, int? expectedShift)
        {
            var result = await Task.Run(() => _stitcher.Add(frame, expectedShift));
            SaveFrame(frame, result.Status + "-shift" + result.Shift + (expectedShift.HasValue ? "-expected" + expectedShift : string.Empty));
            return result;
        }

        private void SaveFrame(PixelFrame frame, string label)
        {
            if (string.IsNullOrEmpty(_options.FrameFolder))
            {
                return;
            }

            System.IO.Directory.CreateDirectory(_options.FrameFolder);
            using (var bitmap = FrameGrabber.ToBitmap(frame))
            {
                var name = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:D4}-{1}.png", ++_savedFrames, label);
                bitmap.Save(System.IO.Path.Combine(_options.FrameFolder, name), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // Scrolls up until the content stops changing.
        private async Task ScrollToTopAsync(Scroller scroller)
        {
            var frame = await GrabStableAsync(0);
            for (int i = 0; i < TopSearchSteps && !_stopped; i++)
            {
                scroller.ToTop(TopStepUnits);
                var next = await GrabStableAsync(_options.DelayMs);
                if (_stitcher.AreUnchanged(frame, next))
                {
                    return;
                }

                frame = next;
            }
        }

        private async Task<PixelFrame> GrabStableAsync(int delayMs)
        {
            await WaitAsync(delayMs);
            var frame = Grab();
            for (int i = 0; i < StableChecks && !_stopped; i++)
            {
                await WaitAsync(StablePollMs);
                var next = Grab();
                if (_stitcher.AreUnchanged(frame, next))
                {
                    return next;
                }

                frame = next;
            }

            return frame;
        }

        private PixelFrame Grab()
        {
            return FrameGrabber.Grab(_target, _options.CaptureMethod);
        }

        private async Task WaitAsync(int ms)
        {
            int until = Environment.TickCount + ms;
            do
            {
                if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0)
                {
                    _stopped = true;
                    return;
                }

                await Task.Delay(Math.Max(1, Math.Min(20, until - Environment.TickCount)));
            }
            while (Environment.TickCount - until < 0);
        }
    }
}
