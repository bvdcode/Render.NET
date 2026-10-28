using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;

namespace Render.NET.Pdf
{
    internal record FunctionShadingCommand(FunctionBasedShading Shading, Matrix3x2 Placement, double Opacity, ColorBlendMode Blend, Rgba32? Background) : IPdfDrawCommand
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
                Matrix3x2 functionMatrix = PdfStreamProcessor.Matrix(Shading.Matrix) * Placement * device;
                canvas.DrawShader(Sample,
                    point => Background.HasValue || (point.X >= d[0] && point.X <= d[1] && point.Y >= d[2] && point.Y <= d[3]),
                    functionMatrix, Blend, token);
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
                return Background ?? default;
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
}
