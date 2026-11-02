using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using static ScrollCapture.App.Platform.NativeMethods;

namespace ScrollCapture.App.Platform
{
    /// <summary>Finds windows of other programs under a screen point, skipping this program's own.</summary>
    internal static class WindowFinder
    {
        private static readonly uint OwnProcessId = (uint)Process.GetCurrentProcess().Id;

        /// <summary>The innermost window of another program at <paramref name="point"/>, or zero.</summary>
        public static IntPtr WindowAt(Point point)
        {
            var top = TopLevelAt(point);
            if (top == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var current = top;
            for (int depth = 0; depth < 64; depth++)
            {
                var local = new POINT(point.X, point.Y);
                ScreenToClient(current, ref local);
                var child = ChildWindowFromPointEx(current, local, CWP_SKIPINVISIBLE | CWP_SKIPTRANSPARENT);
                if (child == IntPtr.Zero || child == current)
                {
                    break;
                }

                current = child;
            }

            return current;
        }

        /// <summary>
        /// The part of the window's client area that is on screen: clipped by its parents, the
        /// top-level frame and the virtual screen. Empty when nothing is visible.
        /// </summary>
        public static Rectangle VisibleClientBounds(IntPtr hwnd)
        {
            var bounds = ClientBounds(hwnd);
            var root = GetAncestor(hwnd, GA_ROOT);
            for (var parent = GetAncestor(hwnd, 1 /* GA_PARENT */); parent != IntPtr.Zero && parent != root && parent != GetDesktop(); parent = GetAncestor(parent, 1))
            {
                bounds.Intersect(ClientBounds(parent));
            }

            if (root != IntPtr.Zero)
            {
                bounds.Intersect(FrameBounds(root));
            }

            bounds.Intersect(SystemInformation.VirtualScreen);
            return bounds.Width > 0 && bounds.Height > 0 ? bounds : Rectangle.Empty;
        }

        /// <summary>The window itself or its nearest parent with a standard vertical scroll bar, or zero.</summary>
        public static IntPtr ScrollBarOwner(IntPtr hwnd)
        {
            var root = GetAncestor(hwnd, GA_ROOT);
            for (var current = hwnd; current != IntPtr.Zero; current = GetAncestor(current, 1))
            {
                if ((GetWindowLong(current, GWL_STYLE) & WS_VSCROLL) != 0)
                {
                    return current;
                }

                if (current == root)
                {
                    break;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>The visible outer frame of a top-level window (without the invisible resize borders of Windows 10/11).</summary>
        public static Rectangle FrameBounds(IntPtr hwnd)
        {
            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf(typeof(RECT))) == 0)
                {
                    return frame.ToRectangle();
                }
            }
            catch (DllNotFoundException)
            {
                // No DWM: the window rectangle is the frame.
            }

            return GetWindowRect(hwnd, out RECT rect) ? rect.ToRectangle() : Rectangle.Empty;
        }

        public static string Describe(IntPtr hwnd)
        {
            var root = GetAncestor(hwnd, GA_ROOT);
            var title = new StringBuilder(256);
            GetWindowText(root == IntPtr.Zero ? hwnd : root, title, title.Capacity);
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            return title.Length > 0 ? title + " [" + name + "]" : name.ToString();
        }

        private static IntPtr TopLevelAt(Point point)
        {
            var found = IntPtr.Zero;
            EnumWindows((hwnd, lParam) =>
            {
                if (!IsCandidate(hwnd) || !FrameBounds(hwnd).Contains(point))
                {
                    return true;
                }

                found = hwnd;
                return false; // windows are enumerated from the top of the Z order
            }, IntPtr.Zero);
            return found;
        }

        private static bool IsCandidate(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            {
                return false;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == OwnProcessId || (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0)
            {
                return false;
            }

            // Windows 8+: suspended store apps and windows on other virtual desktops are "cloaked".
            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                {
                    return false;
                }
            }
            catch (DllNotFoundException)
            {
                // No DWM, nothing is cloaked.
            }

            return true;
        }

        private static Rectangle ClientBounds(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out RECT client))
            {
                return Rectangle.Empty;
            }

            var origin = new POINT(0, 0);
            ClientToScreen(hwnd, ref origin);
            return new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        }

        [DllImport("user32.dll", EntryPoint = "GetDesktopWindow")]
        private static extern IntPtr GetDesktop();
    }
}
