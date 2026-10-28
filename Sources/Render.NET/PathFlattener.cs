using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{

    internal static class PathFlattener
    {
        internal static List<PathContour> Flatten(VectorPath path, Matrix3x2 transform, float tolerance = 0.2f, CancellationToken cancellationToken = default)
        {
            var contours = new List<PathContour>();
            List<Vector2>? points = null;
            Vector2 current = default;
            int remaining = 1_000_000;
            foreach (VectorPath.PathElement element in path.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Consume(ref remaining);
                Vector2 first = Vector2.Transform(element.First, transform);
                switch (element.Kind)
                {
                    case 0:
                        points = new List<Vector2> { first };
                        contours.Add(new PathContour(points, false));
                        current = first;
                        break;
                    case 1 when points is not null:
                        points.Add(first);
                        current = first;
                        break;
                    case 2 when points is not null:
                        Vector2 end = Vector2.Transform(element.Second, transform);
                        FlattenCubic(points, current, current + ((first - current) * (2f / 3)),
                            end + ((first - end) * (2f / 3)), end, tolerance, 0, ref remaining, cancellationToken);
                        current = end;
                        break;
                    case 3 when points is not null:
                        Vector2 third = Vector2.Transform(element.Third, transform);
                        FlattenCubic(points, current, first, Vector2.Transform(element.Second, transform), third, tolerance, 0, ref remaining, cancellationToken);
                        current = third;
                        break;
                    case 4 when points is not null:
                        contours[contours.Count - 1] = new PathContour(points, true);
                        current = points[0];
                        points = new List<Vector2> { current };
                        contours.Add(new PathContour(points, false));
                        break;
                }
            }

            return contours;
        }

        private static void FlattenCubic(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            float tolerance, int depth, ref int remaining, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vector2 chord = d - a;
            float length = chord.Length();
            float error = length < 0.00001f
                ? Math.Max(Vector2.Distance(a, b), Vector2.Distance(a, c))
                : Math.Max(Math.Abs(Cross(chord, b - a)), Math.Abs(Cross(chord, c - a))) / length;
            if (error <= tolerance || depth >= 12)
            {
                Consume(ref remaining);
                points.Add(d);
                return;
            }

            Vector2 ab = (a + b) / 2;
            Vector2 bc = (b + c) / 2;
            Vector2 cd = (c + d) / 2;
            Vector2 abc = (ab + bc) / 2;
            Vector2 bcd = (bc + cd) / 2;
            Vector2 mid = (abc + bcd) / 2;
            FlattenCubic(points, a, ab, abc, mid, tolerance, depth + 1, ref remaining, cancellationToken);
            FlattenCubic(points, mid, bcd, cd, d, tolerance, depth + 1, ref remaining, cancellationToken);
        }

        private static void Consume(ref int remaining)
        {
            if (--remaining < 0)
            {
                throw new InvalidOperationException("A flattened path exceeds one million segments.");
            }
        }

        internal static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);
    }
}
