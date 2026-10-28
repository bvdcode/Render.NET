using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;

namespace Render.NET.Pdf
{
    internal static class ShadingBounds
    {
        internal static void Clip(RasterCanvas canvas, Shading shading, Matrix3x2 placement, CancellationToken token)
        {
            if (shading.BBox is { } box)
            {
                canvas.ClipPath(new VectorPath().Rectangle((float)box.Left, (float)box.Bottom, (float)box.Width, (float)box.Height), placement, cancellationToken: token);
            }
        }
    }
}
