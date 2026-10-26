using System;
using System.Numerics;

namespace Render.NET.Mesh
{
    internal static class GeometryMath
    {
        internal static Vector3 Normalize(Vector3 vector)
        {
            float scale = MathF.Max(MathF.Abs(vector.X), MathF.Max(MathF.Abs(vector.Y), MathF.Abs(vector.Z)));
            return scale == 0 ? Vector3.Zero : Vector3.Normalize(vector / scale);
        }

        internal static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            double bx = (double)b.X - a.X, by = (double)b.Y - a.Y, bz = (double)b.Z - a.Z;
            double cx = (double)c.X - a.X, cy = (double)c.Y - a.Y, cz = (double)c.Z - a.Z;
            double x = by * cz - bz * cy, y = bz * cx - bx * cz, z = bx * cy - by * cx;
            double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            return scale == 0 ? Vector3.Zero : Normalize(new Vector3((float)(x / scale), (float)(y / scale), (float)(z / scale)));
        }
    }
}
