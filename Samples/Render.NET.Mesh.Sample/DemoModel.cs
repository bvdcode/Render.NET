using System.Numerics;
using System.Text;

namespace Render.NET.Mesh.Sample
{
    internal static class DemoModel
    {
        internal static void WriteStl(string path)
        {
            const int rings = 320;
            const int sides = 32;
            Vector3[,] points = new Vector3[rings, sides];
            for (int ring = 0; ring < rings; ring++)
            {
                float t = (ring * MathF.Tau) / rings;
                Vector3 center = Curve(t);
                Vector3 tangent = Vector3.Normalize(Curve(t + 0.001f) - Curve(t - 0.001f));
                Vector3 normal = Vector3.Normalize(Vector3.Cross(tangent, Vector3.UnitZ));
                Vector3 binormal = Vector3.Cross(tangent, normal);
                for (int side = 0; side < sides; side++)
                {
                    float angle = (side * MathF.Tau) / sides;
                    points[ring, side] = center + (0.32f * ((normal * MathF.Cos(angle)) + (binormal * MathF.Sin(angle))));
                }
            }

            using BinaryWriter writer = new(File.Create(path));
            byte[] header = new byte[80];
            Encoding.ASCII.GetBytes("solid Render.NET generated torus knot").CopyTo(header, 0);
            writer.Write(header);
            writer.Write((uint)(rings * sides * 2));
            for (int ring = 0; ring < rings; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    Vector3 a = points[ring, side];
                    Vector3 b = points[(ring + 1) % rings, side];
                    Vector3 c = points[(ring + 1) % rings, (side + 1) % sides];
                    Vector3 d = points[ring, (side + 1) % sides];
                    WriteTriangle(writer, a, c, b);
                    WriteTriangle(writer, a, d, c);
                }
            }
        }

        private static Vector3 Curve(float t) => new(
            (2 + (0.7f * MathF.Cos(3 * t))) * MathF.Cos(2 * t),
            (2 + (0.7f * MathF.Cos(3 * t))) * MathF.Sin(2 * t), 0.7f * MathF.Sin(3 * t));

        private static void WriteTriangle(BinaryWriter writer, Vector3 a, Vector3 b, Vector3 c)
        {
            WriteVector(writer, Vector3.Normalize(Vector3.Cross(b - a, c - a)));
            WriteVector(writer, a);
            WriteVector(writer, b);
            WriteVector(writer, c);
            writer.Write((ushort)0);
        }

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }
    }
}
