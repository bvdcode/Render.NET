using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal record ImageCommand(PixelBuffer Image, Matrix3x2 Placement, float Opacity, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken) => canvas.DrawImage(Image, Placement * device, Opacity, Blend, cancellationToken);
    }
}
