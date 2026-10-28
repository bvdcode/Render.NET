using System;
using System.Buffers.Binary;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class ByteReader
    {
        internal ReadOnlyMemory<byte> Data { get; }
        internal int Position { get; set; }
        internal int Remaining => Data.Length - Position;

        internal ByteReader(ReadOnlyMemory<byte> data) => Data = data;
        internal void Require(int count)
        {
            if (count < 0 || Position < 0 || count > Remaining)
            {
                throw new InvalidDataException("Truncated JPEG 2000 data.");
            }
        }

        internal byte U8()
        {
            Require(1);
            return Data.Span[Position++];
        }

        internal int U16()
        {
            Require(2);
            int value = BinaryPrimitives.ReadUInt16BigEndian(Data.Span.Slice(Position));
            Position += 2;
            return value;
        }

        internal uint U32()
        {
            Require(4);
            uint value = BinaryPrimitives.ReadUInt32BigEndian(Data.Span.Slice(Position));
            Position += 4;
            return value;
        }

        internal ReadOnlyMemory<byte> Take(int count)
        {
            Require(count);
            ReadOnlyMemory<byte> value = Data.Slice(Position, count);
            Position += count;
            return value;
        }

        internal int Marker()
        {
            if (U8() != 255)
            {
                throw new InvalidDataException("Expected a JPEG 2000 marker.");
            }

            int value;
            do
            {
                value = U8();
            }
            while (value == 255);
            if (value < 0x30)
            {
                throw new InvalidDataException("Invalid JPEG 2000 marker.");
            }

            return 0xff00 | value;
        }

        internal ByteReader Segment()
        {
            int length = U16();
            if (length < 2)
            {
                throw new InvalidDataException("Invalid JPEG 2000 marker length.");
            }

            return new ByteReader(Take(length - 2));
        }

        internal void End()
        {
            if (Remaining != 0)
            {
                throw new InvalidDataException("Unexpected data in JPEG 2000 structure.");
            }
        }

        internal static int Ceil(int value, int divisor) => (int)-Math.Floor(-(double)value / divisor);
    }
}
