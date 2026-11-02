using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

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
        /// <summary>GDI+ cannot write JPEG images taller or wider than this.</summary>
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

        public static bool Fits(Image image, ImageFileFormat format)
        {
            return format != ImageFileFormat.Jpeg || (image.Width <= JpegLimit && image.Height <= JpegLimit);
        }

        public static void Write(Image image, string path, ImageFileFormat format)
        {
            switch (format)
            {
                case ImageFileFormat.Jpeg:
                    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                        image.Save(path, codec, parameters);
                    }

                    break;
                case ImageFileFormat.Bmp:
                    image.Save(path, ImageFormat.Bmp);
                    break;
                case ImageFileFormat.Tiff:
                    image.Save(path, ImageFormat.Tiff);
                    break;
                default:
                    image.Save(path, ImageFormat.Png);
                    break;
            }
        }

        public static ImageFileFormat FromFilterIndex(int filterIndex)
        {
            var format = (ImageFileFormat)(filterIndex - 1);
            return Enum.IsDefined(typeof(ImageFileFormat), format) ? format : ImageFileFormat.Png;
        }
    }
}
