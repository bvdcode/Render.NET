using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal interface IPdfDrawCommand
    {
        void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken);
    }
}
