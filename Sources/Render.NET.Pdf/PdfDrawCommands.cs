using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal interface IPdfDrawCommand
    {
        void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken);
    }

    internal sealed record FillCommand(VectorPath Path, Rgba32 Color, FillRule Rule, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
            => canvas.FillPath(Path, Color, device, Rule, Blend, cancellationToken);
    }

    internal sealed record StrokeCommand(VectorPath Path, Rgba32 Color, StrokeStyle Style, Matrix3x2 Pen, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
        {
            if (Matrix3x2.Invert(Pen, out Matrix3x2 inverse))
            {
                canvas.StrokePath(Path.Transform(inverse), Color, Style, Pen * device, Blend, cancellationToken);
            }
        }
    }

    internal sealed record ClipCommand(VectorPath Path, FillRule Rule) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
            => canvas.ClipPath(Path, device, Rule, cancellationToken);
    }

    internal sealed record StateCommand(bool Save) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
        {
            if (Save)
            {
                canvas.Save();
            }
            else
            {
                canvas.Restore();
            }
        }
    }

    internal sealed record ImageCommand(PixelBuffer Image, Matrix3x2 Placement, float Opacity, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
            => canvas.DrawImage(Image, Placement * device, Opacity, Blend, cancellationToken);
    }
}
