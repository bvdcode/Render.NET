using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal record Quantization(int Guard, int Kind, int[] Exponents, int[] Mantissas)
    {
        internal (int Planes, double Step) ForBand(int index, int resolution, int levels, int bits, int orientation, bool reversible)
        {
            int i = Kind == 1 ? 0 : index;
            if (i >= Exponents.Length)
            {
                throw new InvalidDataException("Missing JPEG 2000 quantization parameters.");
            }

            int exponent = Exponents[i] - (Kind == 1 && resolution > 0 ? resolution - 1 : 0);
            int planes = (Guard + exponent) - 1;
            if (planes < 0 || planes > 30)
            {
                throw new NotSupportedException("JPEG 2000 coefficient precision above 30 bits.");
            }

            int gain = orientation == 0 ? 0 : orientation == 3 ? 2 : 1;
            double step = reversible ? 1 : Math.ScaleB(1 + (Mantissas[i] / 2048.0), (bits + gain) - exponent);
            return (planes, step);
        }
    }
}
