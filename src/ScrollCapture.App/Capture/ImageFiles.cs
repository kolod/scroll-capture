using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using ScrollCapture.Core.Imaging;

namespace ScrollCapture.App.Capture
{
    internal enum ImageFileFormat
    {
        // Same order as the save dialog filter (1-based FilterIndex - 1).
        Png,
        Jpeg,
        Bmp,
        Tiff
    }

    /// <summary>Writes the captured image in the format the user chose.</summary>
    internal static class ImageFiles
    {
        /// <summary>The Windows JPEG encoder cannot write images taller or wider than this.</summary>
        public const int JpegLimit = 65500;

        private const long JpegQuality = 92;

        public static ImageFileFormat FromExtension(string path)
        {
            switch (Path.GetExtension(path)?.ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return ImageFileFormat.Jpeg;
                case ".bmp":
                    return ImageFileFormat.Bmp;
                case ".tif":
                case ".tiff":
                    return ImageFileFormat.Tiff;
                default:
                    return ImageFileFormat.Png;
            }
        }

        public static ImageFileFormat FromFilterIndex(int filterIndex)
        {
            var format = (ImageFileFormat)(filterIndex - 1);
            return Enum.IsDefined(typeof(ImageFileFormat), format) ? format : ImageFileFormat.Png;
        }

        public static bool Fits(FrameBitmap image, ImageFileFormat format)
        {
            return format != ImageFileFormat.Jpeg || (image.Width <= JpegLimit && image.Height <= JpegLimit);
        }

        /// <summary>
        /// True when the format is written from the pixels alone (our own PNG encoder), so it may
        /// run on another thread while the bitmap is still shown.
        /// </summary>
        public static bool IsThreadSafe(ImageFileFormat format) => format == ImageFileFormat.Png;

        public static void Write(FrameBitmap image, string path, ImageFileFormat format)
        {
            switch (format)
            {
                case ImageFileFormat.Jpeg:
                    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                        image.Bitmap.Save(path, codec, parameters);
                    }

                    break;
                case ImageFileFormat.Bmp:
                    image.Bitmap.Save(path, ImageFormat.Bmp);
                    break;
                case ImageFileFormat.Tiff:
                    image.Bitmap.Save(path, ImageFormat.Tiff);
                    break;
                default:
                    // The Windows PNG encoder stops at 65 535 rows; ours has no such limit.
                    PngWriter.Write(image.Frame, path);
                    break;
            }
        }
    }
}
