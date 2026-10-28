using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    internal static class PathStroker
    {
        internal static VectorPath Outline(VectorPath path, StrokeStyle style, Matrix3x2 transform, CancellationToken cancellationToken)
        {
            Validate(style);
            float scale = Math.Max(new Vector2(transform.M11, transform.M12).Length(), new Vector2(transform.M21, transform.M22).Length());
            float radius = style.Width == 0 ? 0.5f / Math.Max(scale, 0.000001f) : style.Width / 2;
            VectorPath? outline = new VectorPath();
            foreach (PathContour contour in PathFlattener.Flatten(path, Matrix3x2.Identity, 0.2f / Math.Max(scale, 0.000001f), cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (style.Dashes.Count == 0)
                {
                    Solid(outline, contour, radius, style);
                }
                else
                {
                    Dashed(outline, contour, radius, style, cancellationToken);
                }

                if (outline.CommandCount > 1_000_000)
                {
                    throw new InvalidOperationException("A stroke outline exceeds one million commands.");
                }
            }

            return outline;
        }

        private static void Validate(StrokeStyle style)
        {
            if (!float.IsFinite(style.Width) || style.Width < 0 || !float.IsFinite(style.MiterLimit) || style.MiterLimit < 1 || !float.IsFinite(style.DashOffset))
            {
                throw new ArgumentOutOfRangeException(nameof(style));
            }

            if (style.Dashes.Count > 10_000)
            {
                throw new ArgumentOutOfRangeException(nameof(style), "A dash pattern exceeds 10,000 entries.");
            }

            float total = 0;
            foreach (float dash in style.Dashes)
            {
                if (!float.IsFinite(dash) || dash < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(style), "Dash lengths must be finite and nonnegative.");
                }

                total += dash;
            }

            if (style.Dashes.Count > 0 && (!float.IsFinite(total) || total <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(style), "A dash pattern must have positive total length.");
            }
        }

        private static void Solid(VectorPath output, PathContour contour, float radius, StrokeStyle style)
        {
            List<Vector2> points = contour.Points;
            int count = points.Count;
            int segments = contour.Closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                Segment(output, points[i], points[(i + 1) % count], radius);
            }

            for (int i = 0; i < count; i++)
            {
                if (contour.Closed || (i > 0 && i < count - 1))
                {
                    Join(output, points[((i + count) - 1) % count], points[i], points[(i + 1) % count], radius, style);
                }
            }

            if (!contour.Closed && count > 1)
            {
                Cap(output, points[0], points[1], radius, style.Cap);
                Cap(output, points[count - 1], points[count - 2], radius, style.Cap);
            }
        }

        private static void Dashed(VectorPath output, PathContour contour, float radius, StrokeStyle style, CancellationToken cancellationToken)
        {
            var pattern = new List<float>(style.Dashes);
            if (pattern.Count % 2 != 0)
            {
                pattern.AddRange(style.Dashes);
            }

            float total = 0;
            foreach (float value in pattern)
            {
                total += value;
            }

            float phase = ((style.DashOffset % total) + total) % total;
            int index = 0;
            while (phase >= pattern[index])
            {
                phase -= pattern[index];
                index = (index + 1) % pattern.Count;
            }

            float remaining = pattern[index] - phase;
            List<Vector2>? run = null;
            int pieces = 0;
            int segments = contour.Closed ? contour.Points.Count : contour.Points.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector2 start = contour.Points[i];
                Vector2 end = contour.Points[(i + 1) % contour.Points.Count];
                float length = Vector2.Distance(start, end);
                float distance = 0;
                while (distance < length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++pieces > 100_000 || !float.IsFinite(length))
                    {
                        throw new InvalidOperationException("A dashed stroke exceeds its segment budget.");
                    }

                    float step = Math.Min(remaining, length - distance);
                    Vector2 a = Vector2.Lerp(start, end, distance / length);
                    Vector2 b = Vector2.Lerp(start, end, (distance + step) / length);
                    if (index % 2 == 0)
                    {
                        run ??= new List<Vector2> { a };
                        run.Add(b);
                    }

                    distance += step;
                    remaining -= step;
                    if (remaining <= 0.000001f)
                    {
                        if (run is not null)
                        {
                            Solid(output, new PathContour(run, false), radius, style);
                            run = null;
                        }

                        index = (index + 1) % pattern.Count;
                        remaining = pattern[index];
                    }
                }
            }

            if (run is not null)
            {
                Solid(output, new PathContour(run, false), radius, style);
            }
        }

        private static void Segment(VectorPath output, Vector2 a, Vector2 b, float radius)
        {
            Vector2 difference = b - a;
            if (difference.LengthSquared() < 0.00000001f)
            {
                return;
            }

            Vector2 normal = Vector2.Normalize(new Vector2(-difference.Y, difference.X)) * radius;
            Polygon(output, a + normal, a - normal, b - normal, b + normal);
        }

        private static void Cap(VectorPath output, Vector2 end, Vector2 other, float radius, StrokeCap cap)
        {
            if (cap == StrokeCap.Round)
            {
                Circle(output, end, radius);
            }
            else if (cap == StrokeCap.Square && end != other)
            {
                Segment(output, end, end + (Vector2.Normalize(end - other) * radius), radius);
            }
        }

        private static void Join(VectorPath output, Vector2 before, Vector2 at, Vector2 after, float radius, StrokeStyle style)
        {
            if (at == before || at == after)
            {
                return;
            }

            if (style.Join == StrokeJoin.Round)
            {
                Circle(output, at, radius);
                return;
            }

            Vector2 first = Vector2.Normalize(at - before);
            Vector2 second = Vector2.Normalize(after - at);
            float cross = PathFlattener.Cross(first, second);
            if (Math.Abs(cross) < 0.00001f)
            {
                return;
            }

            float side = cross > 0 ? -1 : 1;
            Vector2 a = at + (new Vector2(-first.Y, first.X) * radius * side);
            Vector2 b = at + (new Vector2(-second.Y, second.X) * radius * side);
            Vector2 miter = a + (first * (PathFlattener.Cross(b - a, second) / cross));
            if (style.Join == StrokeJoin.Miter && Vector2.Distance(miter, at) <= style.MiterLimit * radius)
            {
                Polygon(output, at, a, miter, b);
            }
            else
            {
                Polygon(output, at, a, b);
            }
        }

        private static void Circle(VectorPath output, Vector2 center, float radius)
        {
            float k = radius * 0.55228475f;
            float x = center.X;
            float y = center.Y;
            output.MoveTo(x + radius, y)
                .CubicTo(x + radius, y + k, x + k, y + radius, x, y + radius)
                .CubicTo(x - k, y + radius, x - radius, y + k, x - radius, y)
                .CubicTo(x - radius, y - k, x - k, y - radius, x, y - radius)
                .CubicTo(x + k, y - radius, x + radius, y - k, x + radius, y).Close();
        }

        private static void Polygon(VectorPath output, params Vector2[] points)
        {
            float area = 0;
            for (int i = 0; i < points.Length; i++)
            {
                area += PathFlattener.Cross(points[i], points[(i + 1) % points.Length]);
            }

            if (area < 0)
            {
                Array.Reverse(points);
            }

            output.MoveTo(points[0].X, points[0].Y);
            for (int i = 1; i < points.Length; i++)
            {
                output.LineTo(points[i].X, points[i].Y);
            }

            output.Close();
        }
    }
}
