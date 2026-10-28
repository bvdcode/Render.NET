using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;

namespace Render.NET.Svg
{
    internal record SvgGradient(bool Radial, bool ObjectBox, Matrix3x2 Transform, string Spread, Vector2 Start, Vector2 End, float Radius, float FocalRadius, (float Offset, Rgba32 Color)[] Stops) : SvgPaint
    {
        internal bool Linear { get; init; }

        internal override void DrawUnbounded(RasterCanvas canvas, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, float opacity, CancellationToken token)
        {
            if (Stops.Length == 0)
            {
                return;
            }

            Matrix3x2 shader = ObjectBox ? Transform * Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y) : Transform;
            canvas.DrawShader(p => SvgColor.Alpha(Sample(p), opacity), shader * matrix, cancellationToken: token);
        }

        internal override void Draw(RasterCanvas canvas, VectorPath path, Matrix3x2 matrix, FillRule rule, float opacity, StrokeStyle? stroke, CancellationToken token)
        {
            if (Stops.Length == 0)
            {
                return;
            }

            Matrix3x2 shader = Transform;
            if (ObjectBox)
            {
                (float X, float Y, float Width, float Height) box = path.GetBounds(token);
                shader *= Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y);
            }

            canvas.Save();
            try
            {
                if (stroke is null)
                {
                    canvas.ClipPath(path, matrix, rule, token);
                }
                else
                {
                    canvas.ClipStrokePath(path, stroke, matrix, token);
                }

                canvas.DrawShader(p => SvgColor.Alpha(Sample(p), opacity), shader * matrix, cancellationToken: token);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private Rgba32 Sample(Vector2 point)
        {
            double t = Parameter(point);
            t = Spread switch
            {
                "repeat" => t - Math.Floor(t),
                "reflect" => 1 - Math.Abs((((t % 2) + 2) % 2) - 1),
                _ => Math.Clamp(t, 0, 1)
            };
            int next = 0;
            while (next < Stops.Length && Stops[next].Offset <= t)
            {
                next++;
            }

            if (next == 0)
            {
                return Stops[0].Color;
            }

            if (next == Stops.Length)
            {
                return Stops[^1].Color;
            }

            (float Offset, Rgba32 Color) a = Stops[next - 1];
            (float Offset, Rgba32 Color) b = Stops[next];
            float weight = b.Offset == a.Offset ? 1 : (float)((t - a.Offset) / (b.Offset - a.Offset));
            byte Mix(byte x, byte y) => (byte)Math.Clamp((int)MathF.Round(x + ((y - x) * weight)), 0, 255);
            byte LinearMix(byte x, byte y) => PixelCompositor.Byte(SvgPixels.Srgb(SvgPixels.Linear(x / 255f) + ((SvgPixels.Linear(y / 255f) - SvgPixels.Linear(x / 255f)) * weight)));
            return Linear ? new Rgba32(LinearMix(a.Color.R, b.Color.R), LinearMix(a.Color.G, b.Color.G), LinearMix(a.Color.B, b.Color.B), Mix(a.Color.A, b.Color.A)) : new Rgba32(Mix(a.Color.R, b.Color.R), Mix(a.Color.G, b.Color.G), Mix(a.Color.B, b.Color.B), Mix(a.Color.A, b.Color.A));
        }

        private double Parameter(Vector2 p)
        {
            Vector2 axis = End - Start;
            if (!Radial)
            {
                return axis.LengthSquared() == 0 ? 1 : Vector2.Dot(p - Start, axis) / axis.LengthSquared();
            }

            double dr = Radius - FocalRadius;
            Vector2 v = p - Start;
            double a = axis.LengthSquared() - (dr * dr);
            double b = -2 * (Vector2.Dot(v, axis) + (FocalRadius * dr));
            double c = v.LengthSquared() - (FocalRadius * FocalRadius);
            if (Math.Abs(a) < 1e-12)
            {
                return b == 0 ? 1 : -c / b;
            }

            double discriminant = Math.Max(0, (b * b) - (4 * a * c));
            double first = (-b - Math.Sqrt(discriminant)) / (2 * a), second = (-b + Math.Sqrt(discriminant)) / (2 * a);
            return Math.Max(first, second);
        }
    }
}
