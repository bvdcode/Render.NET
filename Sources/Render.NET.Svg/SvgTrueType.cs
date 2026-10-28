using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Render.NET.Svg
{
    internal partial class SvgTrueType
    {
        private readonly byte[] _data;
        private readonly Dictionary<string, (int Start, int Length)> _tables = new(StringComparer.Ordinal);
        private readonly int _glyphs;
        private readonly int _metrics;
        private readonly int _locaFormat;
        private readonly Dictionary<int, VectorPath> _outlines = new();
        private readonly Dictionary<(int, int), float> _pairs = new();
        internal float Units { get; }
        internal float Ascent { get; }
        internal float Descent { get; }
        internal float XHeight { get; }

        internal SvgTrueType(ReadOnlyMemory<byte> source)
        {
            if (source.Length > 32 * 1024 * 1024 || source.Length < 12)
            {
                throw new InvalidDataException("Invalid SVG font length (maximum 32 MiB).");
            }

            _data = DecodeWoff(source.ToArray());
            int start = Tag(0) == "ttcf" ? Offset(12) : 0;
            ReadTables(start);
            int head = Table("head", 54), maxp = Table("maxp", 6), hhea = Table("hhea", 36);
            Units = U16(head + 18);
            _locaFormat = S16(head + 50);
            _glyphs = U16(maxp + 4);
            _metrics = U16(hhea + 34);
            Ascent = S16(hhea + 4);
            Descent = S16(hhea + 6);
            XHeight = _tables.TryGetValue("OS/2", out (int Start, int Length) os) && os.Length >= 88 && U16(os.Start) >= 2 ? S16(os.Start + 86) : Units * .5f;
            if (Units == 0 || _glyphs == 0 || _metrics == 0 || _metrics > _glyphs || _locaFormat is < 0 or > 1)
            {
                throw new InvalidDataException("Invalid TrueType font metrics.");
            }

            Table("loca", (_glyphs + 1) * (_locaFormat == 0 ? 2 : 4));
            Table("glyf", 0);
            Table("hmtx", (_metrics * 4) + ((_glyphs - _metrics) * 2));
            Table("cmap", 4);
        }

        private void ReadTables(int start)
        {
            int count = U16(start + 4);
            if (count > 256)
            {
                throw new InvalidDataException("Too many OpenType tables.");
            }

            for (int i = 0; i < count; i++)
            {
                int record = start + 12 + (i * 16), offset = Offset(record + 8), length = Offset(record + 12);
                Check(offset, length);
                if (!_tables.TryAdd(Tag(record), (offset, length)))
                {
                    throw new InvalidDataException("Duplicate OpenType table.");
                }
            }
        }

        private int Table(string name, int minimum)
        {
            if (!_tables.TryGetValue(name, out (int Start, int Length) table) || table.Length < minimum)
            {
                throw new NotSupportedException("SVG font requires an OpenType " + name + " table with TrueType outlines.");
            }

            return table.Start;
        }

        private void Check(int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > _data.Length - count)
            {
                throw new InvalidDataException("Truncated OpenType font.");
            }
        }

        private byte Byte(int position)
        {
            Check(position, 1);
            return _data[position];
        }

        private int U16(int position)
        {
            Check(position, 2);
            return (_data[position] << 8) | _data[position + 1];
        }

        private int S16(int position) => (short)U16(position);
        private uint U32(int position)
        {
            Check(position, 4);
            return ((uint)_data[position] << 24) | ((uint)_data[position + 1] << 16) | ((uint)_data[position + 2] << 8) | _data[position + 3];
        }

        private int Offset(int position)
        {
            uint value = U32(position);
            if (value > int.MaxValue)
            {
                throw new InvalidDataException("OpenType offset is too large.");
            }

            return (int)value;
        }

        private string Tag(int position)
        {
            Check(position, 4);
            return Encoding.ASCII.GetString(_data, position, 4);
        }

        internal int Glyph(int codepoint)
        {
            int table = Table("cmap", 4), count = U16(table + 2);
            int selected = 0, rank = -1;
            for (int i = 0; i < count; i++)
            {
                int record = table + 4 + (i * 8), platform = U16(record), encoding = U16(record + 2);
                if (platform != 0 && !(platform == 3 && encoding is 1 or 10))
                {
                    continue;
                }

                int offset = checked(table + Offset(record + 4)), format = U16(offset);
                int score = CmapScore(format);
                if (score > rank)
                {
                    selected = offset;
                    rank = score;
                }
            }

            if (rank < 0)
            {
                return 0;
            }

            int glyph = Cmap(selected, codepoint);
            return glyph >= 0 && glyph < _glyphs ? glyph : 0;
        }

        private static int CmapScore(int format) => format switch
        {
            12 => 6,
            13 => 5,
            4 => 4,
            10 => 3,
            6 => 2,
            0 => 1,
            _ => -1
        };
        private int Cmap(int offset, int codepoint)
        {
            int format = U16(offset);
            if (format is 12 or 13)
            {
                return CmapGroups(offset, codepoint, format == 13);
            }

            if (format == 4)
            {
                return Cmap4(offset, codepoint);
            }

            if (format == 0)
            {
                return codepoint < 256 ? Byte(offset + 6 + codepoint) : 0;
            }

            int first = format == 10 ? Offset(offset + 12) : U16(offset + 6), length = format == 10 ? Offset(offset + 16) : U16(offset + 8);
            return codepoint >= first && codepoint - first < length ? U16(offset + (format == 10 ? 20 : 10) + ((codepoint - first) * 2)) : 0;
        }

        private int CmapGroups(int offset, int codepoint, bool constantGlyph)
        {
            int count = Offset(offset + 12), lo = 0, hi = count - 1;
            Check(offset + 16, checked(count * 12));
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2), group = offset + 16 + (mid * 12);
                uint start = U32(group), end = U32(group + 4);
                if (codepoint < start)
                {
                    hi = mid - 1;
                }
                else if (codepoint > end)
                {
                    lo = mid + 1;
                }
                else
                {
                    return checked((int)U32(group + 8) + (constantGlyph ? 0 : codepoint - (int)start));
                }
            }

            return 0;
        }

        private int Cmap4(int offset, int codepoint)
        {
            if (codepoint > 65535)
            {
                return 0;
            }

            int count = U16(offset + 6) / 2, end = offset + 14, start = end + (count * 2) + 2, delta = start + (count * 2), range = delta + (count * 2);
            Check(range, count * 2);
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (U16(end + (mid * 2)) < codepoint)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            int first = U16(start + (lo * 2));
            if (codepoint < first || codepoint > U16(end + (lo * 2)))
            {
                return 0;
            }

            int distance = U16(range + (lo * 2));
            if (distance == 0)
            {
                return (codepoint + S16(delta + (lo * 2))) & 65535;
            }

            int glyph = U16(range + (lo * 2) + distance + ((codepoint - first) * 2));
            return glyph == 0 ? 0 : (glyph + S16(delta + (lo * 2))) & 65535;
        }

        internal float Advance(int glyph) => U16(Table("hmtx", 0) + (Math.Min(glyph, _metrics - 1) * 4));
        internal VectorPath Outline(int glyph)
        {
            if (!_outlines.TryGetValue(glyph, out VectorPath? path))
            {
                GlyphShape shape = ReadGlyph(glyph, new HashSet<int>(), 0);
                path = Path(shape);
                _outlines.Add(glyph, path);
            }

            return path;
        }

        private static byte[] DecodeWoff(byte[] data)
        {
            if (Encoding.ASCII.GetString(data, 0, 4) != "wOFF")
            {
                return data;
            }

            using MemoryStream? input = new MemoryStream(data, false);
            using BinaryReader? reader = new BinaryReader(input);
            uint Read32() => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            ushort Read16() => System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            input.Position = 4;
            uint flavor = Read32();
            input.Position = 12;
            int count = Read16();
            input.Position = 16;
            uint length = Read32();
            if (length > 32 * 1024 * 1024 || count > 256 || length < 12 + (count * 16))
            {
                throw new InvalidDataException("Invalid WOFF font size.");
            }

            byte[]? result = new byte[length];
            void Write32(int position, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(position), value);
            Write32(0, flavor);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), (ushort)count);
            int destination = 12 + (count * 16);
            for (int i = 0; i < count; i++)
            {
                input.Position = 44 + (i * 20);
                uint tag = Read32(), offset = Read32(), compressed = Read32(), original = Read32(), checksum = Read32();
                if (offset > data.Length || compressed > data.Length - offset || original > result.Length - destination || compressed > original)
                {
                    throw new InvalidDataException("Invalid WOFF table.");
                }

                Write32(12 + (i * 16), tag);
                Write32(16 + (i * 16), checksum);
                Write32(20 + (i * 16), (uint)destination);
                Write32(24 + (i * 16), original);
                if (compressed == original)
                {
                    data.AsSpan((int)offset, (int)original).CopyTo(result.AsSpan(destination));
                }
                else
                {
                    using MemoryStream? compressedData = new MemoryStream(data, (int)offset, (int)compressed, false);
                    using ZLibStream? zlib = new ZLibStream(compressedData, CompressionMode.Decompress);
                    zlib.ReadExactly(result.AsSpan(destination, (int)original));
                    if (zlib.ReadByte() != -1)
                    {
                        throw new InvalidDataException("WOFF table exceeds declared size.");
                    }
                }

                destination = checked((destination + (int)original + 3) & ~3);
            }

            return result;
        }
    }
}
