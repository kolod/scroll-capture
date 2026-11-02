using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollCapture.App.Forms
{
    /// <summary>
    /// Scrollable view of the captured image, optionally shrunk to the panel width. Only the
    /// visible part is drawn, so images tens of thousands of pixels tall stay responsive.
    /// </summary>
    internal sealed class ImagePreview : Panel
    {
        private Image _image;
        private bool _fitWidth = true;

        public ImagePreview()
        {
            AutoScroll = true;
            DoubleBuffered = true;
            BackColor = SystemColors.AppWorkspace;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        /// <summary>Text shown when there is no image.</summary>
        public string EmptyText { get; set; }

        public Image Image
        {
            get => _image;
            set
            {
                _image = value;
                AutoScrollPosition = Point.Empty;
                UpdateLayoutSize();
            }
        }

        public bool FitWidth
        {
            get => _fitWidth;
            set
            {
                _fitWidth = value;
                UpdateLayoutSize();
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateLayoutSize();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (_image == null)
            {
                TextRenderer.DrawText(g, EmptyText ?? string.Empty, Font, ClientRectangle, SystemColors.ControlLightLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            double scale = ZoomFactor();
            var size = ScaledSize(scale);
            var origin = new Point(AutoScrollPosition.X + Math.Max(0, (ClientSize.Width - size.Width) / 2), AutoScrollPosition.Y);

            // The visible part of the scaled image, mapped back to source pixels.
            var visible = Rectangle.Intersect(new Rectangle(origin, size), e.ClipRectangle);
            if (visible.Width <= 0 || visible.Height <= 0)
            {
                return;
            }

            var source = new RectangleF(
                (float)((visible.X - origin.X) / scale),
                (float)((visible.Y - origin.Y) / scale),
                (float)(visible.Width / scale),
                (float)(visible.Height / scale));
            g.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, visible, source, GraphicsUnit.Pixel);
        }

        private double ZoomFactor()
        {
            if (_image == null || !_fitWidth)
            {
                return 1;
            }

            int available = ClientSize.Width - (VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
            return Math.Min(1, Math.Max(1, available) / (double)_image.Width);
        }

        private Size ScaledSize(double scale)
        {
            return new Size(Math.Max(1, (int)(_image.Width * scale)), Math.Max(1, (int)(_image.Height * scale)));
        }

        private void UpdateLayoutSize()
        {
            AutoScrollMinSize = _image == null ? Size.Empty : ScaledSize(ZoomFactor());
            Invalidate();
        }
    }
}
