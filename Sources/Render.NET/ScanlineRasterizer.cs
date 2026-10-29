using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    internal static partial class ScanlineRasterizer
    {
        internal static void Paint(VectorPath path, Matrix3x2 transform, FillRule rule, int width, int height,
            Action<int, int, int> span, CancellationToken cancellationToken)
        {
            var edges = new List<Edge>();
            float minimumY = height;
            float maximumY = 0;
            foreach (PathContour contour in PathFlattener.Flatten(path, transform, cancellationToken: cancellationToken))
            {
                AddEdges(contour.Points, edges, ref minimumY, ref maximumY);
            }

            int top = (int)Math.Clamp(MathF.Ceiling(minimumY - 0.5f), 0, height);
            int bottom = (int)Math.Clamp(MathF.Ceiling(maximumY - 0.5f), 0, height);
            var crossings = new List<Crossing>();
            for (int y = top; y < bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CrossingsAt(edges, y + 0.5f, crossings);
                PaintRow(crossings, rule, width, y, span);
            }
        }

        private static void AddEdges(List<Vector2> points, List<Edge> edges, ref float minimumY, ref float maximumY)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                if (!float.IsFinite(a.X) || !float.IsFinite(a.Y))
                {
                    throw new ArgumentOutOfRangeException(nameof(points), "Transformed path coordinates must be finite.");
                }

                minimumY = Math.Min(minimumY, a.Y);
                maximumY = Math.Max(maximumY, a.Y);
                if (a.Y != b.Y)
                {
                    Vector2 low = a.Y < b.Y ? a : b;
                    Vector2 high = a.Y < b.Y ? b : a;
                    edges.Add(new Edge(low.X, low.Y, high.Y, (high.X - low.X) / (high.Y - low.Y), a.Y < b.Y ? 1 : -1));
                }
            }
        }

        private static void PaintRow(List<Crossing> crossings, FillRule rule, int width, int y, Action<int, int, int> span)
        {
            VisitSpans(crossings, rule, (start, end) =>
            {
                int left = (int)Math.Clamp(MathF.Ceiling(start - 0.5f), 0, width);
                int right = (int)Math.Clamp(MathF.Ceiling(end - 0.5f), 0, width);
                if (right > left)
                {
                    span(y, left, right);
                }
            });
        }

        private static void VisitSpans(List<Crossing> crossings, FillRule rule, Action<float, float> span)
        {
            int winding = 0;
            float start = 0;
            foreach (Crossing crossing in crossings)
            {
                bool wasInside = Inside(winding, rule);
                winding += crossing.Winding;
                bool isInside = Inside(winding, rule);
                if (!wasInside && isInside)
                {
                    start = crossing.X;
                }
                else if (wasInside && !isInside)
                {
                    span(start, crossing.X);
                }
            }
        }

        private static bool Inside(int winding, FillRule rule) => rule == FillRule.NonZero ? winding != 0 : winding % 2 != 0;

        private readonly record struct Edge(float X, float MinimumY, float MaximumY, float Slope, int Winding);
        private readonly record struct Crossing(float X, int Winding);
    }
}
