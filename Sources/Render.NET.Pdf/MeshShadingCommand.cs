using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;

namespace Render.NET.Pdf
{
    internal record MeshShadingCommand(PdfShadingMesh Mesh, Matrix3x2 Placement, double Opacity, ColorBlendMode Blend, Rgba32? Background) : IPdfDrawCommand
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

            PdfMeshIndex? index = new PdfMeshIndex(triangles, token);
            canvas.Save();
            try
            {
                ShadingBounds.Clip(canvas, Mesh.Shading, Placement * device, token);
                canvas.DrawShader(point => Sample(index, point), point => Background.HasValue || index.Find(point, out _) is not null, Placement * device, Blend, token);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private Rgba32 Sample(PdfMeshIndex index, Vector2 point)
        {
            MeshTriangle? t = index.Find(point, out Vector3 weights);
            if (t is null)
            {
                return Background ?? default;
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
}
