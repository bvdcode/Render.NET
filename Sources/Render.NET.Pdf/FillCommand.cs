using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal record FillCommand(VectorPath Path, Rgba32 Color, FillRule Rule, ColorBlendMode Blend) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken) => canvas.FillPath(Path, Color, device, Rule, Blend, cancellationToken);
    }
}
