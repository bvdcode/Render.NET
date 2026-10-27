using System;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal sealed class PacketBits
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

    internal sealed class TagTree
    {
        private readonly int[][] _values;
        private readonly bool[][] _known;
        private readonly int[] _widths;
        internal TagTree(int width, int height, DecodeBudget budget)
        {
            int levels = 1;
            for (int w = width, h = height; w > 1 || h > 1; w = (w + 1) / 2, h = (h + 1) / 2)
            {
                levels++;
            }

            _values = new int[levels][];
            _known = new bool[levels][];
            _widths = new int[levels];
            for (int i = 0; i < levels; i++)
            {
                budget.Reserve((long)width * height * 5);
                _widths[i] = width;
                _values[i] = new int[width * height];
                _known[i] = new bool[width * height];
                width = (width + 1) / 2;
                height = (height + 1) / 2;
            }
        }

        internal bool Below(int leaf, int threshold, PacketBits bits)
        {
            int x = leaf % _widths[0];
            int y = leaf / _widths[0];
            int minimum = 0;
            for (int level = _values.Length - 1; level >= 0; level--)
            {
                int index = ((y >> level) * _widths[level]) + (x >> level);
                int value = Math.Max(_values[level][index], minimum);
                while (value < threshold && !_known[level][index])
                {
                    if (bits.Read() == 1)
                    {
                        _known[level][index] = true;
                    }
                    else
                    {
                        value++;
                    }
                }

                _values[level][index] = value;
                minimum = value;
                if (value >= threshold)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
