using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;

namespace Render.NET.Svg
{
    internal sealed record SvgClipShape(VectorPath Path, FillRule Rule);
    internal sealed record SvgClip(SvgClipShape[] Shapes, bool ObjectBox);
    internal sealed record SvgNode(VectorPath? Path, SvgPaint? Fill, SvgPaint? Stroke, StrokeStyle Pen, FillRule Rule, FillRule ClipRule, Matrix3x2 Transform, float Opacity, float FillOpacity, float StrokeOpacity, SvgClip? Clip, SvgNode[] Children)
    {
        internal void Draw(RasterCanvas canvas, Matrix3x2 parent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Opacity == 0)
            {
                return;
            }

            Matrix3x2 matrix = Transform * parent;
            canvas.Save();
            try
            {
                if (Clip != null)
                {
                    Matrix3x2 clipMatrix = matrix;
                    if (Clip.ObjectBox)
                    {
                        var box = Bounds(token);
                        clipMatrix = Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y) * matrix;
                    }

                    canvas.ClipUnion(Clip.Shapes.Select(p => (p.Path, clipMatrix, p.Rule)), token);
                }

                if (Opacity < 1)
                {
                    canvas.DrawLayer(layer => Paint(layer, matrix, token), Opacity, cancellationToken: token);
                }
                else
                {
                    Paint(canvas, matrix, token);
                }
            }
            finally
            {
                canvas.Restore();
            }
        }

        private void Paint(RasterCanvas canvas, Matrix3x2 matrix, CancellationToken token)
        {
            if (Path != null)
            {
                Fill?.Draw(canvas, Path, matrix, Rule, FillOpacity, null, token);
                if (Pen.Width > 0)
                {
                    Stroke?.Draw(canvas, Path, matrix, Rule, StrokeOpacity, Pen, token);
                }
            }

            foreach (SvgNode child in Children)
            {
                child.Draw(canvas, matrix, token);
            }
        }

        internal (float X, float Y, float Width, float Height) Bounds(CancellationToken token) => Outline(Matrix3x2.Identity).GetBounds(token);
        internal void ClipShapes(Matrix3x2 parent, List<SvgClipShape> result)
        {
            if (Clip != null)
            {
                throw new NotSupportedException("Nested SVG clipping inside clipPath");
            }

            if (Path != null)
            {
                result.Add(new SvgClipShape(Path.Transform(parent), ClipRule));
            }

            foreach (SvgNode child in Children)
            {
                child.ClipShapes(child.Transform * parent, result);
            }
        }

        internal VectorPath Outline(Matrix3x2 parent)
        {
            var result = new VectorPath();
            if (Path != null)
            {
                result.Append(Path.Transform(parent));
            }

            foreach (SvgNode child in Children)
            {
                result.Append(child.Outline(child.Transform * parent));
            }

            return result;
        }
    }

    internal abstract record SvgPaint
    {
        internal abstract void Draw(RasterCanvas canvas, VectorPath path, Matrix3x2 matrix, FillRule rule, float opacity, StrokeStyle? stroke, CancellationToken token);
    }

    internal sealed record SvgSolid(Rgba32 Color) : SvgPaint
    {
        internal override void Draw(RasterCanvas canvas, VectorPath path, Matrix3x2 matrix, FillRule rule, float opacity, StrokeStyle? stroke, CancellationToken token)
        {
            Rgba32 color = SvgColor.Alpha(Color, opacity);
            if (stroke == null)
            {
                canvas.FillPath(path, color, matrix, rule, cancellationToken: token);
            }
            else
            {
                canvas.StrokePath(path, color, stroke, matrix, cancellationToken: token);
            }
        }
    }

    internal sealed record SvgGradient(bool Radial, bool ObjectBox, Matrix3x2 Transform, string Spread, Vector2 Start, Vector2 End, float Radius, float FocalRadius, (float Offset, Rgba32 Color)[] Stops) : SvgPaint
    {
        internal override void Draw(RasterCanvas canvas, VectorPath path, Matrix3x2 matrix, FillRule rule, float opacity, StrokeStyle? stroke, CancellationToken token)
        {
            if (Stops.Length == 0)
            {
                return;
            }

            Matrix3x2 shader = Transform;
            if (ObjectBox)
            {
                var box = path.GetBounds(token);
                shader *= Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y);
            }

            canvas.Save();
            try
            {
                if (stroke == null)
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
                _ => Math.Clamp(t, 0, 1)};
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

            var a = Stops[next - 1];
            var b = Stops[next];
            float weight = b.Offset == a.Offset ? 1 : (float)((t - a.Offset) / (b.Offset - a.Offset));
            byte Mix(byte x, byte y) => (byte)Math.Clamp((int)MathF.Round(x + ((y - x) * weight)), 0, 255);
            return new Rgba32(Mix(a.Color.R, b.Color.R), Mix(a.Color.G, b.Color.G), Mix(a.Color.B, b.Color.B), Mix(a.Color.A, b.Color.A));
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
