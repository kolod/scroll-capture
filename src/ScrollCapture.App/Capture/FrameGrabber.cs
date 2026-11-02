using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScrollCapture.App.Platform;
using ScrollCapture.Core.Imaging;

namespace ScrollCapture.App.Capture
{
    internal enum CaptureMethod
    {
        /// <summary>Copy the pixels from the screen (what the user sees).</summary>
        Screen,

        /// <summary>
        /// Let the window draw itself into the program's own canvas (PrintWindow). Works when the
        /// window is covered by others; some programs draw black or nothing this way.
        /// </summary>
        PrintWindow
    }

    /// <summary>
    /// A GDI+ bitmap that shows the pixels of a frame without copying them: the frame's array is
    /// pinned and used as the bitmap memory, so a tall capture is held in memory only once.
    /// </summary>
    internal sealed class FrameBitmap : IDisposable
    {
        private GCHandle _pin;

        public FrameBitmap(PixelFrame frame)
        {
            Frame = frame;
            _pin = GCHandle.Alloc(frame.Pixels, GCHandleType.Pinned);
            try
            {
                Bitmap = new Bitmap(frame.Width, frame.Height, frame.Width * 4, PixelFormat.Format32bppRgb, _pin.AddrOfPinnedObject());
            }
            catch
            {
                _pin.Free();
                throw;
            }
        }

        public PixelFrame Frame { get; }

        public Bitmap Bitmap { get; }

        public int Width => Frame.Width;

        public int Height => Frame.Height;

        public void Dispose()
        {
            Bitmap.Dispose();
            if (_pin.IsAllocated)
            {
                _pin.Free();
            }
        }
    }

    /// <summary>Takes one screenshot of the target area and converts between frames and bitmaps.</summary>
    internal static class FrameGrabber
    {
        public static PixelFrame Grab(CaptureTarget target, CaptureMethod method)
        {
            using (var bitmap = method == CaptureMethod.PrintWindow ? GrabPrintWindow(target) : GrabScreen(target.Bounds))
            {
                return ToFrame(bitmap);
            }
        }

        public static Bitmap GrabScreen(Rectangle area)
        {
            var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }

        public static PixelFrame ToFrame(Bitmap bitmap)
        {
            var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                var frame = new PixelFrame(bitmap.Width, bitmap.Height);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, frame.Pixels, y * bitmap.Width, bitmap.Width);
                }

                return frame;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static Bitmap ToBitmap(PixelFrame frame)
        {
            var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppRgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < frame.Height; y++)
                {
                    Marshal.Copy(frame.Pixels, y * frame.Width, data.Scan0 + y * data.Stride, frame.Width);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        // The whole top-level window drawn into our bitmap, then cut to the target area.
        private static Bitmap GrabPrintWindow(CaptureTarget target)
        {
            var root = target.Root != IntPtr.Zero ? target.Root : target.Window;
            if (!NativeMethods.GetWindowRect(root, out NativeMethods.RECT rect))
            {
                throw new InvalidOperationException("The window is gone.");
            }

            var windowBounds = rect.ToRectangle();
            using (var whole = new Bitmap(windowBounds.Width, windowBounds.Height, PixelFormat.Format32bppRgb))
            {
                using (var graphics = Graphics.FromImage(whole))
                {
                    var hdc = graphics.GetHdc();
                    try
                    {
                        // PW_RENDERFULLCONTENT (Windows 8.1+) also renders DirectX/DirectComposition content.
                        if (!NativeMethods.PrintWindow(root, hdc, IsWindows81OrLater ? NativeMethods.PW_RENDERFULLCONTENT : 0))
                        {
                            throw new InvalidOperationException("PrintWindow failed.");
                        }
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                var area = target.Bounds;
                area.Offset(-windowBounds.X, -windowBounds.Y);
                area.Intersect(new Rectangle(Point.Empty, whole.Size));
                if (area.Width <= 0 || area.Height <= 0)
                {
                    throw new InvalidOperationException("The area is outside the window.");
                }

                return whole.Clone(area, PixelFormat.Format32bppRgb);
            }
        }

        private static bool IsWindows81OrLater
        {
            get
            {
                // Without a manifest entry Windows 8.1+ reports 6.2; ours declares 8.1 and 10.
                var version = Environment.OSVersion.Version;
                return version.Major > 6 || (version.Major == 6 && version.Minor >= 3);
            }
        }
    }
}
