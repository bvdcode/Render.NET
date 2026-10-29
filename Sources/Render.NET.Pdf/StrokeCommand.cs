using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal record StrokeCommand(VectorPath Path, Rgba32 Color, StrokeStyle Style, Matrix3x2 Pen, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
        {
            if (Matrix3x2.Invert(Pen, out Matrix3x2 inverse))
            {
                canvas.StrokePathAntialiased(Path.Transform(inverse), Color, Style, Pen * device, Blend, cancellationToken);
            }
        }
    }
}
