using System;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal record ShadingCommand(Shading Shading, Rgba32[] Colors, Matrix3x2 Placement, ColorBlendMode Blend, Rgba32? Background = null) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
        {
            canvas.Save();
            try
            {
                if (Shading.BBox is { } box)
                {
                    canvas.ClipPath(new VectorPath().Rectangle((float)box.Left, (float)box.Bottom, (float)box.Width, (float)box.Height), Placement * device, cancellationToken: cancellationToken);
                }

                canvas.DrawShader(Sample, Contains, Placement * device, Blend, cancellationToken);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private Rgba32 Sample(Vector2 point) => TryParameter(point, out double t) ? Colors[(int)Math.Round(Math.Clamp(t, 0, 1) * (Colors.Length - 1))] : Background ?? default;
        private bool Contains(Vector2 point) => Background.HasValue || TryParameter(point, out _);
        private bool TryParameter(Vector2 point, out double t)
        {
            t = double.NaN;
            bool[] extend;
            if (Shading is AxialShading axial)
            {
                double dx = axial.Coords[2] - axial.Coords[0];
                double dy = axial.Coords[3] - axial.Coords[1];
                double length = (dx * dx) + (dy * dy);
                if (length == 0)
                {
                    return false;
                }

                t = (((point.X - axial.Coords[0]) * dx) + ((point.Y - axial.Coords[1]) * dy)) / length;
                extend = axial.Extend;
            }
            else
            {
                RadialShading? radial = (RadialShading)Shading;
                t = RadialParameter(radial, point);
                extend = radial.Extend;
            }

            return Valid(t, extend);
        }

        private static bool Valid(double t, bool[] extend) => double.IsFinite(t) && (t >= 0 || extend[0]) && (t <= 1 || extend[1]);
        private static double RadialParameter(RadialShading shading, Vector2 point)
        {
            double[] c = shading.Coords;
            double dx = c[3] - c[0];
            double dy = c[4] - c[1];
            double dr = c[5] - c[2];
            double x = point.X - c[0];
            double y = point.Y - c[1];
            double a = ((dx * dx) + (dy * dy)) - (dr * dr);
            double b = -2 * ((x * dx) + (y * dy) + (c[2] * dr));
            double constant = ((x * x) + (y * y)) - (c[2] * c[2]);
            if (Math.Abs(a) < 1e-12)
            {
                double t = -constant / b;
                return c[2] + (t * dr) >= 0 ? t : double.NaN;
            }

            double discriminant = (b * b) - (4 * a * constant);
            if (discriminant < 0)
            {
                return double.NaN;
            }

            double first = (-b - Math.Sqrt(discriminant)) / (2 * a);
            double second = (-b + Math.Sqrt(discriminant)) / (2 * a);
            double larger = Math.Max(first, second);
            double smaller = Math.Min(first, second);
            return Valid(larger, shading.Extend) && c[2] + (larger * dr) >= 0 ? larger : Valid(smaller, shading.Extend) && c[2] + (smaller * dr) >= 0 ? smaller : double.NaN;
        }
    }
}
