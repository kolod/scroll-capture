using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ScrollCapture.Core.Imaging
{
    /// <summary>
    /// Writes a frame as an 8-bit RGB PNG. Unlike the Windows (GDI+) encoder it has no limit of
    /// 65 535 rows and streams row by row, so the image is never copied in memory.
    /// </summary>
    public static class PngWriter
    {
        private const int ChunkSize = 1 << 20;

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static void Write(PixelFrame frame, string path)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                Write(frame, file);
            }
        }

        public static void Write(PixelFrame frame, Stream output)
        {
            if (frame == null || output == null)
            {
                throw new ArgumentNullException(frame == null ? nameof(frame) : nameof(output));
            }

            output.Write(Signature, 0, Signature.Length);

            var header = new byte[13];
            PutUInt32(header, 0, (uint)frame.Width);
            PutUInt32(header, 4, (uint)frame.Height);
            header[8] = 8; // bits per channel
            header[9] = 2; // RGB
            WriteChunk(output, "IHDR", header, header.Length);

            using (var idat = new ChunkStream(output))
            {
                // zlib stream: header, raw deflate data, Adler-32 of the uncompressed bytes.
                idat.WriteByte(0x78);
                idat.WriteByte(0x9C);
                uint adler = 1;
                using (var deflate = new DeflateStream(idat, CompressionLevel.Optimal, leaveOpen: true))
                {
                    int rowBytes = frame.Width * 3;
                    var previous = new byte[rowBytes];
                    var current = new byte[rowBytes];
                    var candidates = new[] { new byte[rowBytes + 1], new byte[rowBytes + 1], new byte[rowBytes + 1] };
                    for (int y = 0; y < frame.Height; y++)
                    {
                        int offset = y * frame.Width;
                        for (int x = 0; x < frame.Width; x++)
                        {
                            int pixel = frame.Pixels[offset + x];
                            current[x * 3] = (byte)(pixel >> 16);
                            current[x * 3 + 1] = (byte)(pixel >> 8);
                            current[x * 3 + 2] = (byte)pixel;
                        }

                        var row = Filter(current, previous, candidates);
                        deflate.Write(row, 0, row.Length);
                        adler = Adler32(adler, row);

                        var swap = previous;
                        previous = current;
                        current = swap;
                    }
                }

                var trailer = new byte[4];
                PutUInt32(trailer, 0, adler);
                idat.Write(trailer, 0, trailer.Length);
            }

            WriteChunk(output, "IEND", new byte[0], 0);
        }

        // Picks the filter (None, Sub or Up) with the smallest sum of absolute signed bytes, the
        // usual heuristic for the best compression; returns the filter byte plus the row.
        private static byte[] Filter(byte[] row, byte[] above, byte[][] candidates)
        {
            var none = candidates[0];
            var sub = candidates[1];
            var up = candidates[2];
            none[0] = 0;
            sub[0] = 1;
            up[0] = 2;
            long noneCost = 0;
            long subCost = 0;
            long upCost = 0;
            for (int i = 0; i < row.Length; i++)
            {
                byte value = row[i];
                byte s = (byte)(value - (i >= 3 ? row[i - 3] : 0));
                byte u = (byte)(value - above[i]);
                none[i + 1] = value;
                sub[i + 1] = s;
                up[i + 1] = u;
                noneCost += (sbyte)value < 0 ? -(sbyte)value : (sbyte)value;
                subCost += (sbyte)s < 0 ? -(sbyte)s : (sbyte)s;
                upCost += (sbyte)u < 0 ? -(sbyte)u : (sbyte)u;
            }

            return upCost <= subCost && upCost <= noneCost ? up : subCost <= noneCost ? sub : none;
        }

        private static uint Adler32(uint adler, byte[] data)
        {
            const uint Mod = 65521;
            uint a = adler & 0xFFFF;
            uint b = adler >> 16;
            int index = 0;
            while (index < data.Length)
            {
                // 5552 is the largest block whose sums cannot overflow before the modulo.
                int end = Math.Min(data.Length, index + 5552);
                for (; index < end; index++)
                {
                    a += data[index];
                    b += a;
                }

                a %= Mod;
                b %= Mod;
            }

            return (b << 16) | a;
        }

        private static void WriteChunk(Stream output, string type, byte[] data, int length)
        {
            var typeBytes = Encoding.ASCII.GetBytes(type);
            var lengthBytes = new byte[4];
            PutUInt32(lengthBytes, 0, (uint)length);
            output.Write(lengthBytes, 0, 4);
            output.Write(typeBytes, 0, 4);
            output.Write(data, 0, length);
            uint crc = UpdateCrc(UpdateCrc(0xFFFFFFFF, typeBytes, typeBytes.Length), data, length) ^ 0xFFFFFFFF;
            var crcBytes = new byte[4];
            PutUInt32(crcBytes, 0, crc);
            output.Write(crcBytes, 0, 4);
        }

        private static uint UpdateCrc(uint crc, byte[] data, int length)
        {
            for (int i = 0; i < length; i++)
            {
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }

        private static void PutUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        /// <summary>Collects written bytes and emits them as IDAT chunks of at most 1 MB.</summary>
        private sealed class ChunkStream : Stream
        {
            private readonly Stream _output;
            private readonly byte[] _buffer = new byte[ChunkSize];
            private int _length;

            public ChunkStream(Stream output)
            {
                _output = output;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    int take = Math.Min(count, _buffer.Length - _length);
                    Buffer.BlockCopy(buffer, offset, _buffer, _length, take);
                    _length += take;
                    offset += take;
                    count -= take;
                    if (_length == _buffer.Length)
                    {
                        Flush();
                    }
                }
            }

            public override void Flush()
            {
                if (_length > 0)
                {
                    WriteChunk(_output, "IDAT", _buffer, _length);
                    _length = 0;
                }
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Flush();
                }

                base.Dispose(disposing);
            }
        }
    }
}
