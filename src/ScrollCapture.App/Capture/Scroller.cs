using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ScrollCapture.App.Platform;
using static ScrollCapture.App.Platform.NativeMethods;

namespace ScrollCapture.App.Capture
{
    internal enum ScrollMethod
    {
        /// <summary>Mouse wheel first; scroll-bar messages and Page Down when the wheel does nothing.</summary>
        Auto,

        MouseWheel,

        ScrollMessages,

        PageDownKey
    }

    /// <summary>Scrolls the target window by simulated input or window messages.</summary>
    internal sealed class Scroller
    {
        private readonly CaptureTarget _target;
        private readonly Queue<ScrollMethod> _fallbacks = new Queue<ScrollMethod>();

        public Scroller(CaptureTarget target, ScrollMethod method)
        {
            _target = target;
            if (method == ScrollMethod.Auto)
            {
                Method = ScrollMethod.MouseWheel;
                if (target.ScrollBarOwner != IntPtr.Zero)
                {
                    _fallbacks.Enqueue(ScrollMethod.ScrollMessages);
                }

                _fallbacks.Enqueue(ScrollMethod.PageDownKey);
            }
            else
            {
                Method = method;
            }
        }

        public ScrollMethod Method { get; private set; }

        /// <summary>Units for the first step: one wheel notch, three lines, one page.</summary>
        public int InitialUnits => Method == ScrollMethod.ScrollMessages ? 3 : 1;

        /// <summary>A page is the largest step there is; wheel and lines adapt up to this.</summary>
        public int MaxUnits => Method == ScrollMethod.PageDownKey ? 1 : Method == ScrollMethod.ScrollMessages ? 200 : 40;

        /// <summary>Switches to the next method of <see cref="ScrollMethod.Auto"/>; false when none is left.</summary>
        public bool TryNextMethod()
        {
            if (_fallbacks.Count == 0)
            {
                return false;
            }

            Method = _fallbacks.Dequeue();
            return true;
        }

        public void Down(int units)
        {
            Scroll(units, down: true);
        }

        public void Up(int units)
        {
            Scroll(units, down: false);
        }

        /// <summary>Jumps to the beginning where the method allows it (else scrolls up by <paramref name="units"/>).</summary>
        public void ToTop(int units)
        {
            switch (Method)
            {
                case ScrollMethod.ScrollMessages:
                    PostScroll(SB_TOP, 1);
                    break;
                case ScrollMethod.PageDownKey:
                    Focus();
                    Send(Key(VK_CONTROL, false), Key(VK_HOME, true), Key(VK_HOME, true, up: true), Key(VK_CONTROL, false, up: true));
                    break;
                default:
                    Scroll(units, down: false);
                    break;
            }
        }

        private void Scroll(int units, bool down)
        {
            switch (Method)
            {
                case ScrollMethod.ScrollMessages:
                    PostScroll(down ? SB_LINEDOWN : SB_LINEUP, units);
                    break;
                case ScrollMethod.PageDownKey:
                    Focus();
                    ushort key = down ? VK_NEXT : VK_PRIOR;
                    var inputs = new List<INPUT>();
                    for (int i = 0; i < units; i++)
                    {
                        inputs.Add(Key(key, true));
                        inputs.Add(Key(key, true, up: true));
                    }

                    Send(inputs.ToArray());
                    break;
                default:
                    Wheel(down ? -units : units);
                    break;
            }
        }

        private void Wheel(int notches)
        {
            // The wheel goes to the window under the pointer (Windows 10+) or the focused one.
            var point = _target.ScrollPoint;
            SetCursorPos(point.X, point.Y);
            int count = Math.Abs(notches);
            var inputs = new INPUT[count];
            for (int i = 0; i < count; i++)
            {
                inputs[i].type = INPUT_MOUSE;
                inputs[i].u.mi.mouseData = unchecked((uint)(Math.Sign(notches) * WHEEL_DELTA));
                inputs[i].u.mi.dwFlags = MOUSEEVENTF_WHEEL;
            }

            Send(inputs);
        }

        private void PostScroll(int code, int count)
        {
            var owner = _target.ScrollBarOwner != IntPtr.Zero ? _target.ScrollBarOwner : _target.Window;
            for (int i = 0; i < count; i++)
            {
                PostMessage(owner, WM_VSCROLL, (IntPtr)code, IntPtr.Zero);
            }

            PostMessage(owner, WM_VSCROLL, (IntPtr)SB_ENDSCROLL, IntPtr.Zero);
        }

        // Keys go to the focused window of the foreground thread: focus the target first.
        private void Focus()
        {
            uint targetThread = GetWindowThreadProcessId(_target.Window, out _);
            uint ownThread = GetCurrentThreadId();
            bool attached = targetThread != ownThread && AttachThreadInput(ownThread, targetThread, true);
            try
            {
                SetFocus(_target.Window);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(ownThread, targetThread, false);
                }
            }
        }

        private static INPUT Key(ushort key, bool extended, bool up = false)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wVk = key;
            input.u.ki.dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);
            return input;
        }

        private static void Send(params INPUT[] inputs)
        {
            if (inputs.Length > 0)
            {
                // Input to a program running as administrator is blocked (UIPI); it then just does not scroll.
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
        }
    }
}
