using System;
using System.Drawing;
using ScrollCapture.App.Platform;

namespace ScrollCapture.App.Capture
{
    /// <summary>What to capture: a window of another program and the screen area to take from it.</summary>
    internal sealed class CaptureTarget
    {
        private CaptureTarget(IntPtr window, Rectangle bounds, bool isRegion)
        {
            Window = window;
            Root = NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT);
            Bounds = bounds;
            IsRegion = isRegion;
            ScrollBarOwner = WindowFinder.ScrollBarOwner(window);
            Description = WindowFinder.Describe(window);
        }

        /// <summary>The window under the click; it receives the scrolling.</summary>
        public IntPtr Window { get; }

        /// <summary>Its top-level window, brought to the front for the capture.</summary>
        public IntPtr Root { get; }

        /// <summary>Screen area to capture, in physical pixels.</summary>
        public Rectangle Bounds { get; }

        /// <summary>True when the user dragged an area instead of clicking a window.</summary>
        public bool IsRegion { get; }

        /// <summary>The window (or a parent) with a standard vertical scroll bar, or zero.</summary>
        public IntPtr ScrollBarOwner { get; }

        public string Description { get; }

        /// <summary>
        /// Where the mouse pointer rests for wheel scrolling: inside the area, near its right
        /// edge, so hover effects stay out of most of the content.
        /// </summary>
        public Point ScrollPoint => new Point(Bounds.Right - Math.Max(1, Math.Min(8, Bounds.Width / 4)), Bounds.Top + Bounds.Height / 2);

        public static CaptureTarget ForWindow(IntPtr window)
        {
            var bounds = WindowFinder.VisibleClientBounds(window);
            return bounds.IsEmpty ? null : new CaptureTarget(window, bounds, false);
        }

        public static CaptureTarget ForRegion(IntPtr window, Rectangle region)
        {
            return region.Width <= 0 || region.Height <= 0 ? null : new CaptureTarget(window, region, true);
        }
    }
}
