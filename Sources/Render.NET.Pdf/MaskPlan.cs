using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal record MaskPlan(IPdfDrawCommand[] Commands, bool Luminosity, Rgba32 Background, byte[] Transfer)
    {
        internal PixelBuffer Render(RasterCanvas parent, Matrix3x2 device, CancellationToken token)
        {
            PixelBuffer? image = new PixelBuffer(parent.Buffer.Width, parent.Buffer.Height);
            if (Luminosity)
            {
                image.Clear(Background);
            }

            GroupCommand.DrawCommands(Commands, new RasterCanvas(image), device, token);
            for (int y = 0; y < image.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < image.Width; x++)
                {
                    Rgba32 c = image[x, y];
                    double alpha = c.A / 255d;
                    double luminosity = (0.3 * ((c.R * alpha) + (Background.R * (1 - alpha)))) + (0.59 * ((c.G * alpha) + (Background.G * (1 - alpha)))) + (0.11 * ((c.B * alpha) + (Background.B * (1 - alpha))));
                    int value = Luminosity ? Math.Clamp((int)Math.Round(luminosity), 0, 255) : c.A;
                    image[x, y] = new Rgba32(0, 0, 0, Transfer[value]);
                }
            }

            return image;
        }
    }
}
