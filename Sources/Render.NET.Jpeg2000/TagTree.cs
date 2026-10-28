using System;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class TagTree
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
