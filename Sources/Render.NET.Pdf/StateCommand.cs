using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal record StateCommand(bool Save) : IPdfDrawCommand
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
}
