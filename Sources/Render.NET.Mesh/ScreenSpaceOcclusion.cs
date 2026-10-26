using System;
using System.Numerics;

namespace Render.NET.Mesh
{
    internal static class ScreenSpaceOcclusion
    {
        private static readonly Vector2[] Directions =
        [
            new(1, 0), new(0.707107f, 0.707107f), new(0, 1), new(-0.707107f, 0.707107f),
            new(-1, 0), new(-0.707107f, -0.707107f), new(0, -1), new(0.707107f, -0.707107f)
        ];

        internal static float Evaluate(int x, int y, Vector3 point, Vector3 normal, RasterCamera camera, float[] depths)
        {
            const float worldRadius = 0.18f;
            float radius = MathF.Max(2, camera.Scale * worldRadius);
            float occlusion = 0;
            foreach (Vector2 direction in Directions)
            {
                float horizon = 0;
                for (int step = 1; step <= 3; step++)
                {
                    float distance = radius * step / 3;
                    int sx = (int)MathF.Round(x + direction.X * distance);
                    int sy = (int)MathF.Round(y + direction.Y * distance);
                    if ((uint)sx >= (uint)camera.Width || (uint)sy >= (uint)camera.Height)
                    {
                        continue;
                    }

                    float depth = depths[sy * camera.Width + sx];
                    if (!float.IsFinite(depth))
                    {
                        continue;
                    }

                    Vector3 delta = camera.Unproject(sx + 0.5f, sy + 0.5f, depth) - point;
                    float length = delta.Length();
                    if (length < 0.0001f || length >= worldRadius)
                    {
                        continue;
                    }

                    float contribution = MathF.Max(Vector3.Dot(normal, delta) / length - 0.06f, 0) * (1 - length / worldRadius);
                    horizon = MathF.Max(horizon, contribution);
                }

                occlusion += horizon;
            }

            return Math.Clamp(1 - occlusion * (2.5f / Directions.Length), 0.25f, 1);
        }
    }
}
