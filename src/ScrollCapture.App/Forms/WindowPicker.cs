using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScrollCapture.App.Capture;
using ScrollCapture.App.Localization;
using ScrollCapture.App.Platform;

namespace ScrollCapture.App.Forms
{
    /// <summary>
    /// Lets the user point at the window to capture: every monitor is covered by a frozen
    /// screenshot with a crosshair pointer; the window under the pointer is outlined. A click
    /// picks that window, a drag picks an area, Esc or the right button cancels.
    /// </summary>
    internal sealed class WindowPicker
    {
        private const int MinRegionSize = 16;

        private readonly List<Overlay> _overlays = new List<Overlay>();
        // Asynchronous continuations: the capture must not run inside an overlay's mouse handler.
        private readonly TaskCompletionSource<CaptureTarget> _result =
            new TaskCompletionSource<CaptureTarget>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Font _font;
        private bool _finished;
        private IntPtr _hover;
        private Rectangle _highlight;
        private string _label;
        private Point? _dragStart;

        private WindowPicker(Font font)
        {
            _font = new Font(font.FontFamily, 10.5f);
        }

        /// <summary>Shows the picker; the result is null when the user cancelled.</summary>
        public static Task<CaptureTarget> PickAsync(Font font)
        {
            var picker = new WindowPicker(font);
            picker._result.Task.ContinueWith(t => picker._font.Dispose(), TaskScheduler.FromCurrentSynchronizationContext());
            foreach (var screen in Screen.AllScreens)
            {
                picker._overlays.Add(new Overlay(picker, screen.Bounds, FrameGrabber.GrabScreen(screen.Bounds)));
            }

            foreach (var overlay in picker._overlays)
            {
                overlay.Show();
            }

            var cursor = Cursor.Position;
            (picker._overlays.Find(o => o.Bounds.Contains(cursor)) ?? picker._overlays[0]).Activate();
            picker.Hover(cursor);
            return picker._result.Task;
        }

        private Rectangle Selection => _dragStart.HasValue ? DragRectangle(_dragStart.Value, Cursor.Position) : _highlight;

        private static Rectangle DragRectangle(Point a, Point b)
        {
            var rect = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
            rect.Intersect(SystemInformation.VirtualScreen);
            return rect;
        }

        private static bool IsDrag(Rectangle rect) => rect.Width >= MinRegionSize && rect.Height >= MinRegionSize;

        private void Hover(Point point)
        {
            if (_dragStart.HasValue)
            {
                var rect = Selection;
                _label = rect.Width + " × " + rect.Height;
                Redraw();
                return;
            }

            var window = WindowFinder.WindowAt(point);
            if (window == _hover)
            {
                return;
            }

            _hover = window;
            _highlight = window == IntPtr.Zero ? Rectangle.Empty : WindowFinder.VisibleClientBounds(window);
            _label = window == IntPtr.Zero ? null : _highlight.Width + " × " + _highlight.Height + "   " + WindowFinder.Describe(window);
            Redraw();
        }

        private void Press(MouseButtons button, Point point)
        {
            if (button == MouseButtons.Right)
            {
                Finish(null);
            }
            else if (button == MouseButtons.Left)
            {
                _dragStart = point;
            }
        }

        private void Release(MouseButtons button, Point point)
        {
            if (button != MouseButtons.Left || !_dragStart.HasValue)
            {
                return;
            }

            var rect = DragRectangle(_dragStart.Value, point);
            _dragStart = null;
            CaptureTarget target;
            if (IsDrag(rect))
            {
                var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
                var window = WindowFinder.WindowAt(center);
                target = window == IntPtr.Zero ? null : CaptureTarget.ForRegion(window, rect);
            }
            else
            {
                var window = WindowFinder.WindowAt(point);
                target = window == IntPtr.Zero ? null : CaptureTarget.ForWindow(window);
            }

            if (target != null)
            {
                Finish(target);
            }
            else
            {
                _hover = IntPtr.Zero;
                Hover(point); // nothing usable there: keep picking
            }
        }

        private void Finish(CaptureTarget target)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            foreach (var overlay in _overlays)
            {
                overlay.Close(); // a modeless form disposes itself when closed
            }

            _result.SetResult(target);
        }

        private void Redraw()
        {
            foreach (var overlay in _overlays)
            {
                overlay.Invalidate();
            }
        }

        private sealed class Overlay : Form
        {
            private static readonly Color Accent = Color.FromArgb(255, 0, 120, 215);

            private readonly WindowPicker _picker;
            private readonly Bitmap _shot;

            public Overlay(WindowPicker picker, Rectangle bounds, Bitmap shot)
            {
                _picker = picker;
                _shot = shot;
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                KeyPreview = true;
                DoubleBuffered = true;
                Cursor = Cursors.Cross;
                Bounds = bounds;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                _picker.Hover(Cursor.Position);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                _picker.Press(e.Button, Cursor.Position);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _picker.Release(e.Button, Cursor.Position);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Escape)
                {
                    _picker.Finish(null);
                }
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                // Closed some other way (Alt+F4): treat as cancel.
                _picker.Finish(null);
                base.OnFormClosed(e);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                // The screenshot covers everything.
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.DrawImageUnscaled(_shot, 0, 0);

                var selection = _picker.Selection;
                selection.Offset(-Bounds.X, -Bounds.Y);
                using (var dim = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                using (var outside = new Region(ClientRectangle))
                {
                    if (!selection.IsEmpty)
                    {
                        outside.Exclude(selection);
                    }

                    g.FillRegion(dim, outside);
                }

                if (!selection.IsEmpty)
                {
                    using (var pen = new Pen(Accent, 3) { Alignment = PenAlignment.Inset })
                    {
                        g.DrawRectangle(pen, selection);
                    }

                    if (!string.IsNullOrEmpty(_picker._label))
                    {
                        var at = new Point(selection.Left, selection.Top - 26 >= 0 ? selection.Top - 26 : selection.Top + 4);
                        DrawTag(g, _picker._label, at, Accent);
                    }
                }

                var hint = Strings.Get("Picker_Hint");
                var size = TextRenderer.MeasureText(g, hint, _picker._font);
                DrawTag(g, hint, new Point((ClientSize.Width - size.Width) / 2, 16), Color.FromArgb(230, 32, 32, 32));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _shot.Dispose();
                }

                base.Dispose(disposing);
            }

            private void DrawTag(Graphics g, string text, Point at, Color background)
            {
                var size = TextRenderer.MeasureText(g, text, _picker._font);
                var box = new Rectangle(at.X, at.Y, size.Width + 12, size.Height + 6);
                using (var brush = new SolidBrush(background))
                {
                    g.FillRectangle(brush, box);
                }

                TextRenderer.DrawText(g, text, _picker._font, new Point(box.X + 6, box.Y + 3), Color.White);
            }
        }
    }
}
