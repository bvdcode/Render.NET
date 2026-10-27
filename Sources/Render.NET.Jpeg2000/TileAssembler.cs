using System;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal static class TileAssembler
    {
        internal static void Copy(Tile tile, Jpeg2000Component[] outputs, DecodeBudget budget)
        {
            if (tile.Settings.DefaultCoding!.Mct)
            {
                Transform(tile.Components, tile.Settings.DefaultCoding.Reversible, budget);
            }

            for (int c = 0; c < outputs.Length; c++)
            {
                var source = tile.Components[c];
                var input = source.Output;
                var output = outputs[c];
                double shift = output.IsSigned ? 0 : 1 << (output.BitDepth - 1);
                int minimum = output.IsSigned ? -(1 << (output.BitDepth - 1)) : 0;
                int maximum = output.IsSigned ? (1 << (output.BitDepth - 1)) - 1 : (1 << output.BitDepth) - 1;
                for (int y = 0; y < input.Height; y++)
                {
                    budget.Cancellation.ThrowIfCancellationRequested();
                    for (int x = 0; x < input.Width; x++)
                    {
                        double value = source.Reconstructed[(y * input.Width) + x] + shift;
                        output.Values[(((((input.Y0 - output.Y0) + y) * output.Width) + input.X0) - output.X0) + x] = (int)Math.Clamp(Math.Floor(value + 0.5), minimum, maximum);
                    }
                }
            }
        }

        private static void ValidateGrid(Jpeg2000Component a, Jpeg2000Component b)
        {
            if (a.Width != b.Width || a.Height != b.Height || a.X0 != b.X0 || a.Y0 != b.Y0)
            {
                throw new InvalidDataException("JPEG 2000 MCT component grids must match.");
            }
        }

        private static void Transform(TileComponent[] components, bool reversible, DecodeBudget budget)
        {
            if (components.Length < 3)
            {
                throw new InvalidDataException("JPEG 2000 MCT requires three components.");
            }

            var a = components[0];
            var b = components[1];
            var c = components[2];
            ValidateGrid(a.Output, b.Output);
            ValidateGrid(a.Output, c.Output);
            for (int i = 0; i < a.Reconstructed.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    budget.Cancellation.ThrowIfCancellationRequested();
                }

                double y = a.Reconstructed[i];
                double cb = b.Reconstructed[i];
                double cr = c.Reconstructed[i];
                double g = reversible ? y - Math.Floor((cb + cr) / 4) : y - (0.34413 * cb) - (0.71414 * cr);
                a.Reconstructed[i] = reversible ? cr + g : y + (1.402 * cr);
                b.Reconstructed[i] = g;
                c.Reconstructed[i] = reversible ? cb + g : y + (1.772 * cb);
            }
        }
    }
}
