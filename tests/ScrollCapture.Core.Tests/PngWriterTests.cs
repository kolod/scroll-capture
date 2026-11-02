using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using ScrollCapture.Core.Imaging;
using Xunit;

namespace ScrollCapture.Core.Tests
{
    public class PngWriterTests
    {
        private static PixelFrame Random(int width, int height, int seed)
        {
            var random = new Random(seed);
            var frame = new PixelFrame(width, height);
            for (int i = 0; i < frame.Pixels.Length; i++)
            {
                // Mostly smooth areas with noise, so every filter gets chosen somewhere.
                frame.Pixels[i] = random.Next(4) == 0 ? random.Next(0x1000000) : (i % width) * 0x010203 & 0xFFFFFF;
            }

            return frame;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
        }

        // Chunks of a PNG file as (type, data); checks every CRC on the way.
        private static List<KeyValuePair<string, byte[]>> Chunks(byte[] png)
        {
            var chunks = new List<KeyValuePair<string, byte[]>>();
            int offset = 8;
            while (offset < png.Length)
            {
                int length = (int)ReadUInt32(png, offset);
                string type = Encoding.ASCII.GetString(png, offset + 4, 4);
                var data = new byte[length];
                Array.Copy(png, offset + 8, data, 0, length);
                uint crc = 0xFFFFFFFF;
                for (int i = offset + 4; i < offset + 8 + length; i++)
                {
                    crc ^= png[i];
                    for (int k = 0; k < 8; k++)
                    {
                        crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                    }
                }

                Assert.Equal(crc ^ 0xFFFFFFFF, ReadUInt32(png, offset + 8 + length));
                chunks.Add(new KeyValuePair<string, byte[]>(type, data));
                offset += 12 + length;
            }

            return chunks;
        }

        [Fact]
        public void Image_DecodesToTheSamePixels()
        {
            var frame = Random(97, 61, 3);
            var stream = new MemoryStream();

            PngWriter.Write(frame, stream);

            stream.Position = 0;
            using (var bitmap = new Bitmap(stream))
            {
                Assert.Equal(frame.Width, bitmap.Width);
                Assert.Equal(frame.Height, bitmap.Height);
                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        Assert.Equal(frame[x, y] & PixelFrame.ColorMask, bitmap.GetPixel(x, y).ToArgb() & PixelFrame.ColorMask);
                    }
                }
            }
        }

        [Fact]
        public void TallerThanWindowsCanWrite_IsAValidPng()
        {
            var frame = Random(3, 70000, 5);
            var stream = new MemoryStream();

            PngWriter.Write(frame, stream);

            var png = stream.ToArray();
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, new ArraySegment<byte>(png, 0, 8));
            var chunks = Chunks(png);
            Assert.Equal("IHDR", chunks[0].Key);
            Assert.Equal(3u, ReadUInt32(chunks[0].Value, 0));
            Assert.Equal(70000u, ReadUInt32(chunks[0].Value, 4));
            Assert.Equal("IEND", chunks[chunks.Count - 1].Key);

            var zlib = new MemoryStream();
            foreach (var chunk in chunks)
            {
                if (chunk.Key == "IDAT")
                {
                    zlib.Write(chunk.Value, 0, chunk.Value.Length);
                }
            }

            var compressed = zlib.ToArray();
            var raw = new MemoryStream();
            using (var inflate = new DeflateStream(new MemoryStream(compressed, 2, compressed.Length - 6), CompressionMode.Decompress))
            {
                inflate.CopyTo(raw);
            }

            // Each row: filter byte + 3 bytes per pixel. Undo the filters and compare the last row.
            var data = raw.ToArray();
            Assert.Equal(70000 * (1 + 3 * 3), data.Length);
            var previous = new byte[9];
            var row = new byte[9];
            for (int y = 0; y < 70000; y++)
            {
                byte filter = data[y * 10];
                for (int i = 0; i < 9; i++)
                {
                    byte value = data[y * 10 + 1 + i];
                    row[i] = filter == 0 ? value : filter == 1 ? (byte)(value + (i >= 3 ? row[i - 3] : 0)) : (byte)(value + previous[i]);
                }

                var swap = previous;
                previous = row;
                row = swap;
            }

            for (int x = 0; x < 3; x++)
            {
                int pixel = frame[x, 69999];
                Assert.Equal((byte)(pixel >> 16), previous[x * 3]);
                Assert.Equal((byte)(pixel >> 8), previous[x * 3 + 1]);
                Assert.Equal((byte)pixel, previous[x * 3 + 2]);
            }
        }
    }
}
