using System;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class PacketBits
    {
        private readonly ByteReader _input;
        private int _left;
        private int _value;
        internal PacketBits(ByteReader input) => _input = input;
        internal int Read()
        {
            if (_left == 0)
            {
                _left = _value == 255 ? 7 : 8;
                _value = _input.U8();
                if (_left == 7 && _value >= 128)
                {
                    throw new InvalidDataException("Invalid packet header stuffing bit.");
                }
            }

            return (_value >> --_left) & 1;
        }

        internal int Read(int count)
        {
            if (count < 0 || count > 30)
            {
                throw new InvalidDataException("Invalid packet header bit count.");
            }

            int value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | Read();
            }

            return value;
        }

        internal void Align()
        {
            if (_value == 255 && (_input.U8() & 128) != 0)
            {
                throw new InvalidDataException("Invalid packet header padding.");
            }

            _left = 0;
            _value = 0;
        }

        internal int PassCount()
        {
            if (Read() == 0)
            {
                return 1;
            }

            if (Read() == 0)
            {
                return 2;
            }

            int count = Read(2);
            if (count != 3)
            {
                return count + 3;
            }

            count = Read(5);
            return count != 31 ? count + 6 : Read(7) + 37;
        }
    }
}
