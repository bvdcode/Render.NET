using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal record GroupCommand(IPdfDrawCommand[] Commands, float Opacity, ColorBlendMode Blend, bool Isolated, bool Knockout = false) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken) => canvas.DrawLayer(layer => DrawCommands(Commands, layer, device, cancellationToken), Knockout, Opacity, Blend, Isolated, cancellationToken: cancellationToken);
        internal static void DrawCommands(IPdfDrawCommand[] commands, RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            foreach (IPdfDrawCommand command in commands)
            {
                token.ThrowIfCancellationRequested();
                if (command is StateCommand or ClipCommand)
                {
                    command.Draw(canvas, device, token);
                }
                else
                {
                    canvas.DrawObject(element => command.Draw(element, device, token), token);
                }
            }
        }
    }
}
