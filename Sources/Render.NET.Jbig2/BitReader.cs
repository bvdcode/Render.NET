using System;
using System.Buffers.Binary;
using System.IO;

namespace Render.NET.Jbig2
{
    internal sealed class BitReader
    {
        private readonly ReadOnlyMemory<byte> _data;
        internal BitReader(ReadOnlyMemory<byte> data)
        {
            _data = data;
        }

        internal int BitPosition { get; private set; }
        internal int Position => (BitPosition + 7) / 8;
        internal long AvailableBits => ((long)_data.Length * 8) - BitPosition;

        internal uint Peek(int count)
        {
            int saved = BitPosition;
            uint value = Bits(count);
            BitPosition = saved;
            return value;
        }

        internal int Remaining => _data.Length - Position;

        internal int Bit()
        {
            if ((long)BitPosition >= (long)_data.Length * 8)
            {
                throw new InvalidDataException("Truncated JBIG2 data.");
            }

            int p = BitPosition++;
            return (_data.Span[p >> 3] >> (7 - (p & 7))) & 1;
        }

        internal uint Bits(int count)
        {
            uint result = 0;
            for (int i = 0; i < count; i++)
            {
                result = (result << 1) | (uint)Bit();
            }

            return result;
        }

        internal void Align()
        {
            BitPosition = checked(Position * 8);
        }

        internal byte Byte()
        {
            return Take(1).Span[0];
        }

        internal int SignedByte() => (sbyte)Byte();
        internal int U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2).Span);
        internal uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4).Span);
        internal int I32() => unchecked((int)U32());
        internal ReadOnlyMemory<byte> Take(int count)
        {
            Align();
            int start = Position;
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("Truncated JBIG2 segment.");
            }

            BitPosition = checked((start + count) * 8);
            return _data.Slice(start, count);
        }

        internal ReadOnlyMemory<byte> Rest() => Take(Remaining);
    }
}
