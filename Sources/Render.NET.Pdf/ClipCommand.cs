using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal record ClipCommand(VectorPath Path, FillRule Rule) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken) => canvas.ClipPath(Path, device, Rule, cancellationToken);
    }
}
