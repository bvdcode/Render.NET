using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;

namespace Render.NET.Pdf
{
    internal sealed record FunctionShadingCommand(FunctionBasedShading Shading, Matrix3x2 Placement, double Opacity, ColorBlendMode Blend, Rgba32 Background) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            double[] d = Shading.Domain;
            if (d.Length != 4 || !double.IsFinite(d[0] + d[1] + d[2] + d[3]) || d[0] > d[1] || d[2] > d[3])
            {
                throw new InvalidDataException("Invalid function shading domain.");
            }

            canvas.Save();
            try
            {
                ShadingBounds.Clip(canvas, Shading, Placement * device, token);
                var functionMatrix = PdfStreamProcessor.Matrix(Shading.Matrix) * Placement * device;
                canvas.DrawShader(Sample, functionMatrix, Blend, token);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private Rgba32 Sample(Vector2 point)
        {
            double[] domain = Shading.Domain;
            if (point.X < domain[0] || point.X > domain[1] || point.Y < domain[2] || point.Y > domain[3])
            {
                return Background;
            }

            lock (Shading)
            {
                Span<double> input = stackalloc double[] { point.X, point.Y };
                Span<double> output = stackalloc double[32];
                int count = Shading.Eval(input, output);
                return PdfStreamProcessor.Color(Shading.ColorSpace.GetColor(output[..count]), Opacity);
            }
        }
    }

    internal sealed record MeshShadingCommand(PdfShadingMesh Mesh, Matrix3x2 Placement, double Opacity, ColorBlendMode Blend, Rgba32 Background) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            var triangles = new List<MeshTriangle>(Mesh.Triangles);
            int steps = 8;
            foreach (PdfMeshPatch patch in Mesh.Patches)
            {
                token.ThrowIfCancellationRequested();
                steps = Math.Max(steps, patch.Steps(Placement * device));
            }

            foreach (PdfMeshPatch patch in Mesh.Patches)
            {
                patch.Tessellate(triangles, steps, Mesh.MaximumTriangles, token);
            }

            var index = new PdfMeshIndex(triangles, token);
            canvas.Save();
            try
            {
                ShadingBounds.Clip(canvas, Mesh.Shading, Placement * device, token);
                canvas.DrawShader(point => Sample(index, point), Placement * device, Blend, token);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private Rgba32 Sample(PdfMeshIndex index, Vector2 point)
        {
            MeshTriangle? t = index.Find(point, out Vector3 weights);
            if (t == null)
            {
                return Background;
            }

            Span<double> values = stackalloc double[t.A.Components.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (t.A.Components[i] * weights.X) + (t.B.Components[i] * weights.Y) + (t.C.Components[i] * weights.Z);
            }

            lock (Mesh.Shading)
            {
                Span<double> output = stackalloc double[32];
                IColor color;
                if (Mesh.Shading.Functions?.Length > 0)
                {
                    int count = Mesh.Shading.Eval(values, output);
                    color = Mesh.Shading.ColorSpace.GetColor(output[..count]);
                }
                else
                {
                    color = Mesh.Shading.ColorSpace.GetColor(values);
                }
                return PdfStreamProcessor.Color(color, Opacity);
            }
        }
    }

    internal static class ShadingBounds
    {
        internal static void Clip(RasterCanvas canvas, Shading shading, Matrix3x2 placement, CancellationToken token)
        {
            if (shading.BBox is { } box)
            {
                canvas.ClipPath(new VectorPath().Rectangle((float)box.Left, (float)box.Bottom, (float)box.Width, (float)box.Height), placement, cancellationToken: token);
            }
        }
    }
}
