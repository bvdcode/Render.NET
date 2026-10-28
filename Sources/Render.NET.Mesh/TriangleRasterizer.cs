using System;
using System.Numerics;
using System.Threading;

namespace Render.NET.Mesh
{
    internal static class TriangleRasterizer
    {
        internal static float[] Draw(PreparedMesh mesh, RasterCamera camera, Vector3[]? normals, CancellationToken token)
        {
            float[] depths = new float[camera.Width * camera.Height];
            Array.Fill(depths, float.PositiveInfinity);
            Vector3[] projected = new Vector3[mesh.Positions.Length];
            for (int index = 0; index < projected.Length; index++)
            {
                projected[index] = camera.Project(mesh.Positions[index]);
            }

            for (int corner = 0; corner < mesh.Indices.Length; corner += 3)
            {
                token.ThrowIfCancellationRequested();
                if (mesh.Normals[corner] == Vector3.Zero)
                {
                    continue;
                }

                Vector3 a = projected[mesh.Indices[corner]];
                Vector3 b = projected[mesh.Indices[corner + 1]];
                Vector3 c = projected[mesh.Indices[corner + 2]];
                float area = Edge(a, b, c.X, c.Y);
                if (MathF.Abs(area) < 0.000001f)
                {
                    continue;
                }

                int left = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, camera.Width - 1);
                int right = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, camera.Width - 1);
                int top = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, camera.Height - 1);
                int bottom = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, camera.Height - 1);
                float inverseArea = 1 / area;
                for (int y = top; y <= bottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = left; x <= right; x++)
                    {
                        if ((x & 255) == 0)
                        {
                            token.ThrowIfCancellationRequested();
                        }

                        float wa = Edge(b, c, x + 0.5f, y + 0.5f) * inverseArea;
                        float wb = Edge(c, a, x + 0.5f, y + 0.5f) * inverseArea;
                        float wc = 1 - wa - wb;
                        if (wa < -0.000001f || wb < -0.000001f || wc < -0.000001f)
                        {
                            continue;
                        }

                        float depth = (wa * a.Z) + (wb * b.Z) + (wc * c.Z);
                        int offset = (y * camera.Width) + x;
                        if (depth >= depths[offset])
                        {
                            continue;
                        }

                        depths[offset] = depth;
                        if (normals is not null)
                        {
                            Vector3 normal = (mesh.Normals[corner] * wa) + (mesh.Normals[corner + 1] * wb) + (mesh.Normals[corner + 2] * wc);
                            if (normal.LengthSquared() < 0.000001f)
                            {
                                normal = Vector3.Cross(mesh.Positions[mesh.Indices[corner + 1]] - mesh.Positions[mesh.Indices[corner]],
                                    mesh.Positions[mesh.Indices[corner + 2]] - mesh.Positions[mesh.Indices[corner]]);
                            }

                            normal = Vector3.Normalize(normal);
                            normals[offset] = Vector3.Dot(normal, camera.ViewDirection) < 0 ? -normal : normal;
                        }
                    }
                }
            }

            return depths;
        }

        private static float Edge(Vector3 a, Vector3 b, float x, float y) =>
            ((b.X - a.X) * (y - a.Y)) - ((b.Y - a.Y) * (x - a.X));
    }
}
