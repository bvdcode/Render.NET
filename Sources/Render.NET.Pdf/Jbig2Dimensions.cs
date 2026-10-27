using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Pdf
{
    internal static class Jbig2Dimensions
    {
        private static readonly HashSet<int> RegionTypes = new() { 4, 6, 7, 20, 22, 23, 36, 38, 39, 40, 42, 43 };

        internal static void Validate(ReadOnlySpan<byte> data, int width, int height, int maximumPixels)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                Require(data, offset, 6);
                uint number = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
                byte flags = data[offset + 4];
                offset += 5;
                int references = ReadReferences(data, ref offset);
                int referenceBytes = number <= 256 ? 1 : number <= 65536 ? 2 : 4;
                long skip = ((long)references * referenceBytes) + ((flags & 64) != 0 ? 4 : 1);
                if (skip > int.MaxValue)
                {
                    throw new InvalidDataException("JBIG2 reference list is too large.");
                }

                Require(data, offset, (int)skip + 4);
                offset += (int)skip;
                uint length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
                offset += 4;
                if (length == uint.MaxValue)
                {
                    throw new NotSupportedException("JBIG2 segments with unknown lengths");
                }

                if (length > int.MaxValue)
                {
                    throw new InvalidDataException("JBIG2 segment is too large.");
                }

                Require(data, offset, (int)length);
                CheckSegment(data.Slice(offset, (int)length), flags & 63, width, height, maximumPixels);
                offset += (int)length;
            }
        }

        private static int ReadReferences(ReadOnlySpan<byte> data, ref int offset)
        {
            int count = data[offset] >> 5;
            if (count < 5)
            {
                offset++;
                return count;
            }

            if (count != 7)
            {
                throw new InvalidDataException("Invalid JBIG2 reference count.");
            }

            Require(data, offset, 4);
            count = (int)(BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4)) & 0x1fffffff);
            offset += 4;
            int retentionBytes = (count + 8) / 8;
            Require(data, offset, retentionBytes);
            offset += retentionBytes;
            return count;
        }

        private static void CheckSegment(ReadOnlySpan<byte> data, int type, int width, int height, int maximumPixels)
        {
            bool page = type == 48;
            bool region = RegionTypes.Contains(type);
            if (!page && !region)
            {
                return;
            }

            Require(data, 0, 8);
            uint w = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(0, 4));
            uint h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
            if (page && h == uint.MaxValue)
            {
                throw new NotSupportedException("Striped JBIG2 pages with unknown height");
            }

            if (page && (w != width || h != height))
            {
                throw new InvalidDataException("JBIG2 dimensions do not match the PDF image dictionary.");
            }

            if (w == 0 || h == 0 || (ulong)w * h > (ulong)maximumPixels)
            {
                throw new InvalidDataException("JBIG2 region exceeds MaximumImagePixels.");
            }
        }

        private static void Require(ReadOnlySpan<byte> data, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > data.Length - count)
            {
                throw new InvalidDataException("The JBIG2 segment data is truncated.");
            }
        }
    }
}
