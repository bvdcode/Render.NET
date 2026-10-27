using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    public sealed partial class VectorPath
    {
        /// <summary>Rounds contour corners at the supplied user-unit distance. Curves are flattened at 0.05 user units.</summary>
        public VectorPath RoundCorners(float radius, CancellationToken cancellationToken = default)
        {
            if (!float.IsFinite(radius) || radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            if (radius == 0)
            {
                return new VectorPath().Append(this);
            }

            var result = new VectorPath();
            foreach (PathContour contour in PathFlattener.Flatten(this, Matrix3x2.Identity, .05f, cancellationToken))
            {
                List<Vector2> points = Clean(contour.Points);
                if (points.Count < 2)
                {
                    continue;
                }

                RoundContour(result, points, contour.Closed, radius, cancellationToken);
            }

            return result;
        }

        private static void RoundContour(VectorPath result, List<Vector2> points, bool closed, float radius, CancellationToken cancellationToken)
        {
            for (int i = 0; i < points.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Vector2 current = points[i];
                if (!closed && (i == 0 || i == points.Count - 1))
                {
                    if (i == 0)
                    {
                        result.MoveTo(current.X, current.Y);
                    }
                    else
                    {
                        result.LineTo(current.X, current.Y);
                    }

                    continue;
                }

                Vector2 previous = points[((i + points.Count) - 1) % points.Count], next = points[(i + 1) % points.Count];
                Vector2 a = current + ((previous - current) * Math.Min(.5f, radius / Vector2.Distance(current, previous)));
                Vector2 b = current + ((next - current) * Math.Min(.5f, radius / Vector2.Distance(current, next)));
                if (i == 0)
                {
                    result.MoveTo(a.X, a.Y);
                }
                else
                {
                    result.LineTo(a.X, a.Y);
                }

                Vector2 c = a + ((current - a) * .551915f), d = b + ((current - b) * .551915f);
                result.CubicTo(c.X, c.Y, d.X, d.Y, b.X, b.Y);
            }

            if (closed)
            {
                result.Close();
            }
        }

        /// <summary>Offsets closed contours with a miter, bevel or round join. Curves use 0.05 user-unit flattening.
        /// Positive distances expand outer contours and shrink holes; open paths use their left-hand offset.</summary>
        public VectorPath Offset(float distance, StrokeJoin join = StrokeJoin.Miter, float miterLimit = 4, CancellationToken cancellationToken = default)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(miterLimit) || miterLimit < 1 || !Enum.IsDefined(join))
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }

            if (distance == 0)
            {
                return new VectorPath().Append(this);
            }

            List<PathContour> contours = PathFlattener.Flatten(this, Matrix3x2.Identity, .05f, cancellationToken);
            var closed = new VectorPath();
            foreach (PathContour contour in contours.Where(c => c.Closed))
            {
                List<Vector2> points = Clean(contour.Points);
                if (points.Count > 2)
                {
                    closed.MoveTo(points[0].X, points[0].Y);
                    foreach (Vector2 p in points.Skip(1))
                    {
                        closed.LineTo(p.X, p.Y);
                    }

                    closed.Close();
                }
            }

            VectorPath band = PathStroker.Outline(closed, new StrokeStyle { Width = Math.Abs(distance) * 2, Join = join, MiterLimit = miterLimit }, Matrix3x2.Identity, cancellationToken);
            VectorPath result = closed.Combine(band, distance > 0 ? PathBooleanOperation.Union : PathBooleanOperation.Difference, cancellationToken: cancellationToken);
            foreach (PathContour contour in contours)
            {
                if (contour.Closed)
                {
                    continue;
                }

                List<Vector2> points = Clean(contour.Points);
                if (points.Count < 2)
                {
                    continue;
                }

                var shifted = OffsetOpenContour(points, distance, join, miterLimit, cancellationToken);
                result.Append(shifted);
            }

            return result;
        }

        private static VectorPath OffsetOpenContour(List<Vector2> points, float distance, StrokeJoin join, float limit, CancellationToken token)
        {
            var result = new VectorPath();
            for (int i = 0; i < points.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                Vector2 current = points[i], previous = points[((i + points.Count) - 1) % points.Count], next = points[(i + 1) % points.Count];
                Vector2 a = Vector2.Normalize(current - previous), b = Vector2.Normalize(next - current);
                if (i == 0)
                {
                    a = b;
                }

                if (i == points.Count - 1)
                {
                    b = a;
                }

                Vector2 p = current + (new Vector2(a.Y, -a.X) * distance), q = current + (new Vector2(b.Y, -b.X) * distance);
                float denominator = PathFlattener.Cross(a, b);
                Vector2 intersection = Math.Abs(denominator) < 1e-7f ? (p + q) / 2 : p + (a * (PathFlattener.Cross(q - p, b) / denominator));
                bool outer = denominator * distance > 0;
                if (!outer || (join == StrokeJoin.Miter && Vector2.Distance(intersection, current) <= Math.Abs(distance) * limit))
                {
                    Add(intersection);
                }
                else
                {
                    Add(p);
                    if (join == StrokeJoin.Round)
                    {
                        OffsetArc(p, q, current, distance, Add);
                    }

                    Add(q);
                }
            }

            return result;
            void Add(Vector2 point)
            {
                if (result.CommandCount == 0)
                {
                    result.MoveTo(point.X, point.Y);
                }
                else
                {
                    result.LineTo(point.X, point.Y);
                }
            }
        }

        private static void OffsetArc(Vector2 p, Vector2 q, Vector2 current, float distance, Action<Vector2> Add)
        {
            float start = MathF.Atan2(p.Y - current.Y, p.X - current.X), end = MathF.Atan2(q.Y - current.Y, q.X - current.X);
            float sweep = end - start;
            while (distance > 0 && sweep < 0)
            {
                sweep += 2 * MathF.PI;
            }

            while (distance < 0 && sweep > 0)
            {
                sweep -= 2 * MathF.PI;
            }

            int count = Math.Max(1, (int)MathF.Ceiling(Math.Abs(sweep) * Math.Max(2, MathF.Sqrt(Math.Abs(distance) * 4))));
            if (count > 4096)
            {
                throw new InvalidDataException("Path offset exceeds its arc subdivision budget.");
            }

            for (int step = 1; step < count; step++)
            {
                float angle = start + ((sweep * step) / count);
                Add(current + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Math.Abs(distance)));
            }
        }

        private static List<Vector2> Clean(List<Vector2> source)
        {
            var points = new List<Vector2>();
            foreach (Vector2 point in source)
            {
                if (points.Count == 0 || Vector2.DistanceSquared(points[^1], point) > 1e-12)
                {
                    points.Add(point);
                }
            }

            if (points.Count > 1 && points[0] == points[^1])
            {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }
    }
}
