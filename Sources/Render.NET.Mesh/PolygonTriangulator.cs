using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Render.NET.Mesh
{
    internal static class PolygonTriangulator
    {
        internal static List<int> Triangulate(Vector3[] vertices, CancellationToken token)
        {
            if (vertices.Length == 3)
            {
                return [0, 1, 2];
            }

            double nx = 0, ny = 0, nz = 0;
            Vector3 origin = vertices[0];
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 a = vertices[index], b = vertices[(index + 1) % vertices.Length];
                nx += ((double)a.Y - b.Y) * ((double)a.Z + b.Z - 2d * origin.Z);
                ny += ((double)a.Z - b.Z) * ((double)a.X + b.X - 2d * origin.X);
                nz += ((double)a.X - b.X) * ((double)a.Y + b.Y - 2d * origin.Y);
            }

            int dropAxis = Math.Abs(nx) > Math.Abs(ny) ? 0 : 1;
            if (Math.Abs(nz) > (dropAxis == 0 ? Math.Abs(nx) : Math.Abs(ny)))
            {
                dropAxis = 2;
            }

            double scale = 0;
            foreach (Vector3 vertex in vertices)
            {
                scale = Math.Max(scale, Math.Max(Math.Abs((double)vertex.X - origin.X),
                    Math.Max(Math.Abs((double)vertex.Y - origin.Y), Math.Abs((double)vertex.Z - origin.Z))));
            }

            if (scale == 0)
            {
                throw new InvalidDataException("OBJ polygon has no area.");
            }

            Vector2[] points = new Vector2[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 p = vertices[index];
                points[index] = dropAxis switch
                {
                    0 => new Vector2((float)(((double)p.Y - origin.Y) / scale), (float)(((double)p.Z - origin.Z) / scale)),
                    1 => new Vector2((float)(((double)p.X - origin.X) / scale), (float)(((double)p.Z - origin.Z) / scale)),
                    _ => new Vector2((float)(((double)p.X - origin.X) / scale), (float)(((double)p.Y - origin.Y) / scale))
                };
            }

            List<int> remaining = [];
            for (int index = 0; index < points.Length; index++)
            {
                if (remaining.Count == 0 || points[remaining[^1]] != points[index])
                {
                    remaining.Add(index);
                }
            }

            if (remaining.Count > 1 && points[remaining[0]] == points[remaining[^1]])
            {
                remaining.RemoveAt(remaining.Count - 1);
            }

            double area = 0;
            for (int index = 0; index < remaining.Count; index++)
            {
                Vector2 a = points[remaining[index]], b = points[remaining[(index + 1) % remaining.Count]];
                area += (double)a.X * b.Y - (double)a.Y * b.X;
            }

            if (Math.Abs(area) < 1e-12)
            {
                throw new InvalidDataException("OBJ polygon is degenerate or self-intersecting.");
            }

            double sign = Math.Sign(area);
            List<int> triangles = new((vertices.Length - 2) * 3);
            while (remaining.Count > 3)
            {
                token.ThrowIfCancellationRequested();
                bool removed = false;
                for (int index = 0; index < remaining.Count; index++)
                {
                    int a = remaining[(index + remaining.Count - 1) % remaining.Count];
                    int b = remaining[index];
                    int c = remaining[(index + 1) % remaining.Count];
                    double turn = Cross(points[a], points[b], points[c]) * sign;
                    if (Math.Abs(turn) < 1e-12)
                    {
                        remaining.RemoveAt(index);
                        removed = true;
                        break;
                    }

                    if (turn < 0)
                    {
                        continue;
                    }

                    bool contains = false;
                    foreach (int other in remaining)
                    {
                        if (other != a && other != b && other != c &&
                            points[other] != points[a] && points[other] != points[b] && points[other] != points[c] &&
                            Cross(points[a], points[b], points[other]) * sign >= -1e-12 &&
                            Cross(points[b], points[c], points[other]) * sign >= -1e-12 &&
                            Cross(points[c], points[a], points[other]) * sign >= -1e-12)
                        {
                            contains = true;
                            break;
                        }
                    }

                    if (!contains)
                    {
                        triangles.AddRange([a, b, c]);
                        remaining.RemoveAt(index);
                        removed = true;
                        break;
                    }
                }

                if (!removed)
                {
                    throw new InvalidDataException("OBJ polygon cannot be triangulated as a simple planar polygon.");
                }
            }

            if (remaining.Count == 3)
            {
                triangles.AddRange(remaining);
            }

            return triangles;
        }

        private static double Cross(Vector2 a, Vector2 b, Vector2 c) =>
            ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);
    }
}
