using System;
using System.Numerics;

namespace Render.NET.Svg
{
    internal static class SvgArc
    {
        internal static void Append(VectorPath path, Vector2 start, Vector2 end, float radiusX, float radiusY, float angle, bool large, bool sweep)
        {
            if (start == end)
            {
                return;
            }

            double rx = Math.Abs(radiusX), ry = Math.Abs(radiusY);
            if (rx == 0 || ry == 0)
            {
                path.LineTo(end.X, end.Y);
                return;
            }

            double phi = (angle * Math.PI) / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (start.X - (double)end.X) / 2, dy = (start.Y - (double)end.Y) / 2;
            double x = (cos * dx) + (sin * dy), y = (-sin * dx) + (cos * dy);
            double scale = ((x * x) / (rx * rx)) + ((y * y) / (ry * ry));
            if (scale > 1)
            {
                rx *= Math.Sqrt(scale);
                ry *= Math.Sqrt(scale);
            }

            double rx2 = rx * rx, ry2 = ry * ry;
            double factor = Math.Sqrt(Math.Max(0, ((rx2 * ry2) - (rx2 * y * y) - (ry2 * x * x)) / ((rx2 * y * y) + (ry2 * x * x))));
            if (large == sweep)
            {
                factor = -factor;
            }

            double cx1 = (factor * rx * y) / ry, cy1 = (-factor * ry * x) / rx;
            double cx = ((cos * cx1) - (sin * cy1)) + ((start.X + (double)end.X) / 2);
            double cy = (sin * cx1) + (cos * cy1) + ((start.Y + (double)end.Y) / 2);
            double first = Math.Atan2((y - cy1) / ry, (x - cx1) / rx);
            double delta = Math.Atan2((-y - cy1) / ry, (-x - cx1) / rx) - first;
            if (sweep && delta < 0)
            {
                delta += 2 * Math.PI;
            }

            if (!sweep && delta > 0)
            {
                delta -= 2 * Math.PI;
            }

            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)));
            for (int i = 0; i < count; i++)
            {
                double a = first + ((delta * i) / count), b = first + ((delta * (i + 1)) / count);
                double k = (4d / 3) * Math.Tan((b - a) / 4);
                Vector2 p = Map(Math.Cos(a) - (k * Math.Sin(a)), Math.Sin(a) + (k * Math.Cos(a)));
                Vector2 q = Map(Math.Cos(b) + (k * Math.Sin(b)), Math.Sin(b) - (k * Math.Cos(b)));
                Vector2 r = i == count - 1 ? end : Map(Math.Cos(b), Math.Sin(b));
                path.CubicTo(p.X, p.Y, q.X, q.Y, r.X, r.Y);
            }

            Vector2 Map(double u, double v) => new Vector2((float)((cx + (rx * cos * u)) - (ry * sin * v)), (float)(cy + (rx * sin * u) + (ry * cos * v)));
        }
    }
}
