using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    internal static partial class ScanlineRasterizer
    {
        private const int CoverageSamples = 8;

        internal static void PaintCoverage(VectorPath path, Matrix3x2 transform, FillRule rule, int width, int height,
            Action<int, int, float> pixel, CancellationToken cancellationToken)
        {
            List<Edge> edges = new List<Edge>();
            float minimumY = height;
            float maximumY = 0;
            foreach (PathContour contour in PathFlattener.Flatten(path, transform, cancellationToken: cancellationToken))
            {
                AddEdges(contour.Points, edges, ref minimumY, ref maximumY);
            }

            int top = (int)Math.Clamp(MathF.Floor(minimumY), 0, height);
            int bottom = (int)Math.Clamp(MathF.Ceiling(maximumY), 0, height);
            float[] coverage = new float[width];
            List<float> divisions = new List<float>();
            List<Crossing> crossings = new List<Crossing>();
            for (int y = top; y < bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Array.Clear(coverage);
                DivideRow(edges, y, divisions);
                for (int i = 1; i < divisions.Count; i++)
                {
                    float start = divisions[i - 1];
                    float end = divisions[i];
                    if (end > start)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CrossingsAt(edges, (start + end) / 2, crossings);
                        VisitSpans(crossings, rule, (left, right) => AddCoverage(coverage, left, right, end - start));
                    }
                }

                for (int x = 0; x < width; x++)
                {
                    if (coverage[x] > 0)
                    {
                        pixel(x, y, Math.Min(1, coverage[x]));
                    }
                }
            }
        }

        private static void DivideRow(List<Edge> edges, int y, List<float> divisions)
        {
            divisions.Clear();
            for (int i = 0; i <= CoverageSamples; i++)
            {
                divisions.Add(y + (i / (float)CoverageSamples));
            }

            foreach (Edge edge in edges)
            {
                if (edge.MinimumY > y && edge.MinimumY < y + 1)
                {
                    divisions.Add(edge.MinimumY);
                }

                if (edge.MaximumY > y && edge.MaximumY < y + 1)
                {
                    divisions.Add(edge.MaximumY);
                }
            }

            divisions.Sort();
        }

        private static void CrossingsAt(List<Edge> edges, float sample, List<Crossing> crossings)
        {
            crossings.Clear();
            foreach (Edge edge in edges)
            {
                if (sample >= edge.MinimumY && sample < edge.MaximumY)
                {
                    crossings.Add(new Crossing(edge.X + ((sample - edge.MinimumY) * edge.Slope), edge.Winding));
                }
            }

            crossings.Sort((a, b) => a.X.CompareTo(b.X));
        }

        private static void AddCoverage(float[] coverage, float start, float end, float height)
        {
            float left = Math.Clamp(start, 0, coverage.Length);
            float right = Math.Clamp(end, 0, coverage.Length);
            for (int x = (int)MathF.Floor(left); x < MathF.Ceiling(right); x++)
            {
                coverage[x] += (Math.Min(right, x + 1) - Math.Max(left, x)) * height;
            }
        }
    }
}
