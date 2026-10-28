using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal record MaskedCommand(IPdfDrawCommand Command, MaskPlan Mask) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            PixelBuffer mask = Mask.Render(canvas, device, token);
            canvas.DrawLayer(layer => Command.Draw(layer, device, token), isolated: false, alphaMask: mask, cancellationToken: token);
        }
    }
}
