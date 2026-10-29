using System;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal record PatternPaintCommand(VectorPath Path, FillRule Rule, IPdfDrawCommand Pattern, StrokeStyle? Stroke = null, Matrix3x2 Pen = default) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            if (Stroke is not null)
            {
                if (Matrix3x2.Invert(Pen, out Matrix3x2 inverse))
                {
                    canvas.PaintStrokePath(Path.Transform(inverse), Stroke, Pen * device,
                        layer => Pattern.Draw(layer, device, token), token);
                }

                return;
            }

            canvas.Save();
            try
            {
                canvas.ClipPath(Path, device, Rule, token);
                Pattern.Draw(canvas, device, token);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }
}
