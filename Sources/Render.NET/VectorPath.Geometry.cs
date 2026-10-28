using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    public partial class VectorPath
    {
        /// <summary>Returns numerical path bounds using curve flattening with 0.01 user-unit tolerance.</summary>
        public (float X, float Y, float Width, float Height) GetBounds(CancellationToken cancellationToken = default)
        {
            float left = float.PositiveInfinity, top = float.PositiveInfinity;
            float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            foreach (PathContour contour in PathFlattener.Flatten(this, Matrix3x2.Identity, 0.01f, cancellationToken))
            {
                foreach (Vector2 p in contour.Points)
                {
                    left = Math.Min(left, p.X);
                    right = Math.Max(right, p.X);
                    top = Math.Min(top, p.Y);
                    bottom = Math.Max(bottom, p.Y);
                }
            }

            return float.IsPositiveInfinity(left) ? default : (left, top, right - left, bottom - top);
        }

        /// <summary>Returns the selected fraction of total path length, with a wrapping offset.
        /// Curves are flattened in user units. Start/end must be in [0,1]; start greater than end wraps.</summary>
        public VectorPath Trim(float start, float end, float offset = 0, float tolerance = 0.1f, CancellationToken cancellationToken = default)
        {
            if (!float.IsFinite(start + end + offset + tolerance) || start < 0 || end < 0 || start > 1 || end > 1 || tolerance <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (Math.Abs(end - start) >= 1)
            {
                return new VectorPath().Append(this);
            }

            VectorPath? result = new VectorPath();
            if (start == end)
            {
                return result;
            }

            List<PathContour> contours = PathFlattener.Flatten(this, Matrix3x2.Identity, tolerance, cancellationToken);
            double total = 0;
            foreach (PathContour contour in contours)
            {
                if (contour.Closed && contour.Points.Count > 1)
                {
                    contour.Points.Add(contour.Points[0]);
                }

                for (int i = 1; i < contour.Points.Count; i++)
                {
                    total += Vector2.Distance(contour.Points[i - 1], contour.Points[i]);
                }
            }

            double a = (((start + offset) % 1) + 1) % 1, b = (((end + offset) % 1) + 1) % 1;
            if (a < b)
            {
                TrimRange(result, contours, a * total, b * total, cancellationToken);
            }
            else
            {
                TrimRange(result, contours, a * total, total, cancellationToken);
                TrimRange(result, contours, 0, b * total, cancellationToken);
            }

            return result;
        }

        private static void TrimRange(VectorPath result, List<PathContour> contours, double from, double to, CancellationToken token)
        {
            double position = 0;
            foreach (PathContour contour in contours)
            {
                bool started = false;
                for (int i = 1; i < contour.Points.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    Vector2 p = contour.Points[i - 1], q = contour.Points[i];
                    double length = Vector2.Distance(p, q), next = position + length;
                    if (length > 0 && next > from && position < to)
                    {
                        Vector2 a = Vector2.Lerp(p, q, (float)Math.Clamp((from - position) / length, 0, 1));
                        Vector2 b = Vector2.Lerp(p, q, (float)Math.Clamp((to - position) / length, 0, 1));
                        if (!started)
                        {
                            result.MoveTo(a.X, a.Y);
                            started = true;
                        }

                        result.LineTo(b.X, b.Y);
                    }

                    position = next;
                }
            }
        }
    }
}
