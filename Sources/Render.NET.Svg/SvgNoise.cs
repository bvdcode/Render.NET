using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal class SvgNoise
    {
        private readonly int[] _permutation = new int[256];
        private readonly Vector2[,] _gradients = new Vector2[4, 256];
        private long _seed;
        internal SvgNoise(long seed)
        {
            _seed = seed <= 0 ? 1 - (seed % 2147483646) : Math.Min(seed, 2147483646);
            for (int i = 0; i < 256; i++)
            {
                _permutation[i] = i;
            }

            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i < 256; i++)
                {
                    Vector2 gradient = new((Next() % 512) - 256, (Next() % 512) - 256);
                    _gradients[c, i] = gradient.LengthSquared() == 0 ? Vector2.UnitX : Vector2.Normalize(gradient);
                }
            }

            for (int i = 255; i > 0; i--)
            {
                int j = Next() % 256;
                (_permutation[i], _permutation[j]) = (_permutation[j], _permutation[i]);
            }
        }

        private int Next()
        {
            _seed = (_seed * 16807) % 2147483647;
            return (int)_seed;
        }

        internal float Sample(float x, float y, int channel, int stitchWidth, int stitchHeight, int originX, int originY)
        {
            x += 4096;
            y += 4096;
            int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            int Wrap(int v, int size, int origin) => size == 0 ? v : origin + ((((v - origin) % size) + size) % size);
            float Corner(int dx, int dy)
            {
                int px = Wrap(ix + dx, stitchWidth, originX + 4096) & 255, py = Wrap(iy + dy, stitchHeight, originY + 4096) & 255;
                int index = _permutation[(_permutation[px] + py) & 255];
                return Vector2.Dot(_gradients[channel, index], new Vector2(fx - dx, fy - dy));
            }

            float sx = fx * fx * (3 - (2 * fx)), sy = fy * fy * (3 - (2 * fy));
            float a = Corner(0, 0), b = Corner(1, 0), c = Corner(0, 1), d = Corner(1, 1);
            return a + ((b - a) * sx) + (((c + ((d - c) * sx)) - a - ((b - a) * sx)) * sy);
        }
    }
}
