using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace Render.NET.Svg
{
    internal static class SvgPng
    {
        internal static PixelBuffer Decode(byte[] bytes, int maximumPixels, CancellationToken token)
        {
            if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                throw new InvalidDataException("Invalid embedded PNG signature.");
            }

            using var compressed = new MemoryStream();
            PngHeader header = ReadChunks(bytes, compressed, maximumPixels, token);
            int width = header.Width, height = header.Height, depth = header.Depth, color = header.Color, interlace = header.Interlace;
            byte[] palette = header.Palette, transparency = header.Transparency;
            var image = new PixelBuffer(width, height);
            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            int[][] passes = interlace == 0 ? new[]
            {
                new[]
                {
                    0,
                    0,
                    1,
                    1
                }
            }

            : new[]
            {
                new[]
                {
                    0,
                    0,
                    8,
                    8
                },
                new[]
                {
                    4,
                    0,
                    8,
                    8
                },
                new[]
                {
                    0,
                    4,
                    4,
                    8
                },
                new[]
                {
                    2,
                    0,
                    4,
                    4
                },
                new[]
                {
                    0,
                    2,
                    2,
                    4
                },
                new[]
                {
                    1,
                    0,
                    2,
                    2
                },
                new[]
                {
                    0,
                    1,
                    1,
                    2
                }
            };
            foreach (int[] pass in passes)
            {
                ReadPass(zlib, image, depth, color, palette, transparency, pass, token);
            }

            if (zlib.ReadByte() != -1)
            {
                throw new InvalidDataException("Embedded PNG exceeds its declared dimensions.");
            }

            return image;
        }

        private sealed class PngHeader
        {
            internal int Width, Height, Depth, Color, Interlace;
            internal byte[] Palette = Array.Empty<byte>(), Transparency = Array.Empty<byte>();
        }

        private static PngHeader ReadChunks(byte[] bytes, Stream compressed, int maximumPixels, CancellationToken token)
        {
            var header = new PngHeader();
            int position = 8;
            bool ended = false;
            while (position <= bytes.Length - 12 && !ended)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> data = Chunk(bytes, position, out string name);
                switch (name)
                {
                    case "IHDR":
                        ReadHeader(data, position, maximumPixels, header);
                        break;
                    case "PLTE":
                        header.Palette = Palette(data);
                        break;
                    case "tRNS":
                        header.Transparency = data.ToArray();
                        break;
                    case "IDAT":
                        compressed.Write(data);
                        break;
                    case "IEND":
                        ended = true;
                        break;
                    default:
                        if ((bytes[position + 4] & 32) == 0)
                        {
                            throw new NotSupportedException("Embedded PNG critical chunk: " + name);
                        }

                        break;
                }

                position += data.Length + 12;
            }

            if (!ended || header.Width == 0 || (header.Color == 3 && header.Palette.Length == 0))
            {
                throw new InvalidDataException("Incomplete embedded PNG.");
            }

            return header;
        }

        private static ReadOnlySpan<byte> Chunk(byte[] bytes, int position, out string name)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
            if (size > bytes.Length - position - 12)
            {
                throw new InvalidDataException("Truncated embedded PNG chunk.");
            }

            int length = (int)size;
            name = Encoding.ASCII.GetString(bytes, position + 4, 4);
            if (Crc(bytes.AsSpan(position + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 8 + length)))
            {
                throw new InvalidDataException("Embedded PNG CRC mismatch.");
            }

            return bytes.AsSpan(position + 8, length);
        }

        private static byte[] Palette(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0 || data.Length > 768 || data.Length % 3 != 0)
            {
                throw new InvalidDataException("Invalid embedded PNG palette.");
            }

            return data.ToArray();
        }

        private static void ReadHeader(ReadOnlySpan<byte> data, int position, int maximumPixels, PngHeader header)
        {
            if (position != 8 || data.Length != 13)
            {
                throw new InvalidDataException("Invalid PNG header.");
            }

            header.Width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
            header.Height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
            header.Depth = data[8];
            header.Color = data[9];
            header.Interlace = data[12];
            if (header.Width <= 0 || header.Height <= 0 || (long)header.Width * header.Height > maximumPixels || data[10] != 0 || data[11] != 0 || header.Interlace > 1 || !ValidDepth(header.Color, header.Depth))
            {
                throw new InvalidDataException("Invalid or oversized embedded PNG.");
            }
        }

        private static bool ValidDepth(int color, int depth) => color switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => depth is 8 or 16,
            _ => false
        };
        private static void ReadPass(Stream zlib, PixelBuffer image, int depth, int color, byte[] palette, byte[] transparency, int[] pass, CancellationToken token)
        {
            int width = Math.Max(0, (((image.Width - pass[0]) + pass[2]) - 1) / pass[2]), height = Math.Max(0, (((image.Height - pass[1]) + pass[3]) - 1) / pass[3]);
            if (width == 0 || height == 0)
            {
                return;
            }

            int components = color switch
            {
                2 => 3,
                4 => 2,
                6 => 4,
                _ => 1
            };
            int stride = checked(((width * components * depth) + 7) / 8), bpp = Math.Max(1, ((components * depth) + 7) / 8);
            byte[] previous = new byte[stride], row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                int filter = zlib.ReadByte();
                zlib.ReadExactly(row);
                Unfilter(row, previous, bpp, filter);
                for (int x = 0; x < width; x++)
                {
                    image[pass[0] + (x * pass[2]), pass[1] + (y * pass[3])] = Pixel(row, x, depth, color, components, palette, transparency);
                }

                (previous, row) = (row, previous);
            }
        }

        private static void Unfilter(byte[] row, byte[] previous, int bpp, int filter)
        {
            for (int i = 0; i < row.Length; i++)
            {
                int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException("Invalid PNG row filter.")};
                row[i] = unchecked((byte)(row[i] + predictor));
            }
        }

        private static Rgba32 Pixel(byte[] data, int x, int depth, int color, int components, byte[] palette, byte[] transparency)
        {
            int Sample(int component)
            {
                int index = (x * components) + component;
                return depth == 16 ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(index * 2)) : depth == 8 ? data[index] : (data[(index * depth) / 8] >> (8 - depth - ((index * depth) % 8))) & ((1 << depth) - 1);
            }

            byte ByteValue(int value) => (byte)MathF.Round((value * 255f) / ((1 << depth) - 1));
            int a = Sample(0);
            if (color == 3)
            {
                if (a >= palette.Length / 3)
                {
                    throw new InvalidDataException("PNG palette index is out of range.");
                }

                return new Rgba32(palette[a * 3], palette[(a * 3) + 1], palette[(a * 3) + 2], a < transparency.Length ? transparency[a] : (byte)255);
            }

            if (color is 0 or 4)
            {
                byte gray = ByteValue(a);
                byte alpha = color == 4 ? ByteValue(Sample(1)) : transparency.Length >= 2 && a == BinaryPrimitives.ReadUInt16BigEndian(transparency) ? (byte)0 : (byte)255;
                return new Rgba32(gray, gray, gray, alpha);
            }

            return RgbPixel(a, Sample(1), Sample(2), color == 6 ? ByteValue(Sample(3)) : (byte)255, depth, color, transparency);
        }

        private static Rgba32 RgbPixel(int a, int b, int c, byte alpha, int depth, int color, byte[] transparency)
        {
            byte ByteValue(int value) => (byte)MathF.Round((value * 255f) / ((1 << depth) - 1));
            byte opacity = color == 6 ? alpha : transparency.Length >= 6 && a == BinaryPrimitives.ReadUInt16BigEndian(transparency) && b == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) && c == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)) ? (byte)0 : (byte)255;
            return new Rgba32(ByteValue(a), ByteValue(b), ByteValue(c), opacity);
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = (a + b) - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static uint Crc(ReadOnlySpan<byte> data)
        {
            uint crc = uint.MaxValue;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
                }
            }

            return ~crc;
        }
    }
}
