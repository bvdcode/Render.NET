using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal sealed record PdfMeshPatch(Vector2[] Points, double[][] Colors)
    {
        private static readonly int[] TensorOrder = { 0, 11, 10, 9, 1, 12, 15, 8, 2, 13, 14, 7, 3, 4, 5, 6 };

        internal Vector2 Position(float u, float v)
        {
            if (Points.Length == 16)
            {
                Vector2 result = default;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        result += Points[TensorOrder[(y * 4) + x]] * Bernstein(x, u) * Bernstein(y, v);
                    }
                }

                return result;
            }

            Vector2 bottom = Curve(Points[0], Points[11], Points[10], Points[9], u);
            Vector2 top = Curve(Points[3], Points[4], Points[5], Points[6], u);
            Vector2 left = Curve(Points[0], Points[1], Points[2], Points[3], v);
            Vector2 right = Curve(Points[9], Points[8], Points[7], Points[6], v);
            Vector2 corners = Vector2.Lerp(Vector2.Lerp(Points[0], Points[9], u), Vector2.Lerp(Points[3], Points[6], u), v);
            return (Vector2.Lerp(bottom, top, v) + Vector2.Lerp(left, right, u)) - corners;
        }

        internal int Steps(Matrix3x2 device)
        {
            int steps = 8;
            while (steps < 128 && Error(steps, device) > 0.2f)
            {
                steps *= 2;
            }

            return steps;
        }

        private float Error(int steps, Matrix3x2 device)
        {
            float error = 0;
            for (int y = 0; y < steps; y++)
            {
                for (int x = 0; x < steps; x++)
                {
                    float u = x / (float)steps;
                    float v = y / (float)steps;
                    float delta = 1f / steps;
                    Vector2 a = Position(u, v);
                    Vector2 b = Position(u + delta, v);
                    Vector2 c = Position(u, v + delta);
                    Vector2 d = Position(u + delta, v + delta);
                    error = Math.Max(error, Deviation(Position(u + (delta / 2), v), (a + b) / 2, device));
                    error = Math.Max(error, Deviation(Position(u, v + (delta / 2)), (a + c) / 2, device));
                    error = Math.Max(error, Deviation(Position(u + (delta / 2), v + (delta / 2)), (b + c) / 2, device));
                    error = Math.Max(error, Deviation(Position(u + delta, v + (delta / 2)), (b + d) / 2, device));
                }
            }

            return error;
        }

        internal void Tessellate(List<MeshTriangle> triangles, int steps, int limit, CancellationToken token)
        {
            var previous = Row(0, steps);
            for (int y = 1; y <= steps; y++)
            {
                token.ThrowIfCancellationRequested();
                MeshVertex[] row = Row(y / (float)steps, steps);
                for (int x = 0; x < steps; x++)
                {
                    PdfShadingMesh.AddTriangle(triangles, new MeshTriangle(previous[x], previous[x + 1], row[x]), limit);
                    PdfShadingMesh.AddTriangle(triangles, new MeshTriangle(previous[x + 1], row[x], row[x + 1]), limit);
                }

                previous = row;
            }
        }

        private MeshVertex[] Row(float v, int steps)
        {
            var row = new MeshVertex[steps + 1];
            for (int x = 0; x <= steps; x++)
            {
                float u = x / (float)steps;
                var components = new double[Colors[0].Length];
                for (int i = 0; i < components.Length; i++)
                {
                    double bottom = (Colors[0][i] * (1 - u)) + (Colors[3][i] * u);
                    double top = (Colors[1][i] * (1 - u)) + (Colors[2][i] * u);
                    components[i] = (bottom * (1 - v)) + (top * v);
                }

                row[x] = new MeshVertex(Position(u, v), components);
            }

            return row;
        }

        private static float Deviation(Vector2 point, Vector2 linear, Matrix3x2 device)
            => Vector2.TransformNormal(point - linear, device).Length();
        private static Vector2 Curve(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
            => (a * Bernstein(0, t)) + (b * Bernstein(1, t)) + (c * Bernstein(2, t)) + (d * Bernstein(3, t));
        private static float Bernstein(int index, float t) => index switch
        {
            0 => (1 - t) * (1 - t) * (1 - t),
            1 => 3 * t * (1 - t) * (1 - t),
            2 => 3 * t * t * (1 - t),
            _ => t * t * t
        };
    }
}
