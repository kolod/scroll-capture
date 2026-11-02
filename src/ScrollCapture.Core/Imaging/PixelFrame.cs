using System;

namespace ScrollCapture.Core.Imaging
{
    /// <summary>
    /// A 32-bit image in memory, row by row, one <c>int</c> (0xAARRGGBB) per pixel. The alpha
    /// byte of screen captures is undefined, so comparisons use <see cref="ColorMask"/>.
    /// </summary>
    public sealed class PixelFrame
    {
        /// <summary>The color bits of a pixel; alpha is ignored when frames are compared.</summary>
        public const int ColorMask = 0x00FFFFFF;

        public PixelFrame(int width, int height)
            : this(width, height, new int[checked(width * height)])
        {
        }

        public PixelFrame(int width, int height, int[] pixels)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The frame must not be empty.");
            }

            if (pixels == null || pixels.Length != checked(width * height))
            {
                throw new ArgumentException("The pixel buffer does not match the frame size.", nameof(pixels));
            }

            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }

        public int Height { get; }

        public int[] Pixels { get; }

        public int this[int x, int y]
        {
            get => Pixels[y * Width + x];
            set => Pixels[y * Width + x] = value;
        }

        /// <summary>Copies rows <paramref name="top"/>…<paramref name="top"/>+<paramref name="count"/>.</summary>
        public int[] CopyRows(int top, int count)
        {
            if (top < 0 || count < 0 || top + count > Height)
            {
                throw new ArgumentOutOfRangeException(nameof(top));
            }

            var rows = new int[count * Width];
            Array.Copy(Pixels, top * Width, rows, 0, rows.Length);
            return rows;
        }

        /// <summary>True when both frames have the same size and the same colors everywhere.</summary>
        public bool SameAs(PixelFrame other)
        {
            if (other == null || other.Width != Width || other.Height != Height)
            {
                return false;
            }

            var a = Pixels;
            var b = other.Pixels;
            for (int i = 0; i < a.Length; i++)
            {
                if (((a[i] ^ b[i]) & ColorMask) != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
