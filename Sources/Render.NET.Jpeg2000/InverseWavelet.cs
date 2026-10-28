using System;

namespace Render.NET.Jpeg2000
{
    internal static class InverseWavelet
    {
        internal static void Reconstruct(TileComponent component, DecodeBudget budget)
        {
            double[] previous = Array.Empty<double>();
            Resolution? low = null;
            foreach (Resolution resolution in component.Resolutions)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                budget.Reserve((long)resolution.Width * resolution.Height * 8);
                double[]? data = new double[resolution.Width * resolution.Height];
                if (low is not null)
                {
                    Interleave(previous, low, data, resolution);
                }

                foreach (Subband band in resolution.Bands)
                {
                    DecodeBand(band, component, data, resolution, low is null, budget);
                }

                if (low is not null)
                {
                    Filter(data, resolution, component.Coding.Reversible, budget);
                }

                previous = data;
                low = resolution;
            }

            component.Reconstructed = previous;
        }

        private static void Interleave(double[] previous, Resolution low, double[] data, Resolution resolution)
        {
            for (int y = 0; y < low.Height; y++)
            {
                for (int x = 0; x < low.Width; x++)
                {
                    int dx = (2 * (low.X0 + x)) - resolution.X0;
                    int dy = (2 * (low.Y0 + y)) - resolution.Y0;
                    data[(dy * resolution.Width) + dx] = previous[(y * low.Width) + x];
                }
            }
        }

        private static void DecodeBand(Subband band, TileComponent component, double[] data, Resolution resolution, bool lowest, DecodeBudget budget)
        {
            foreach (CodeBlock block in band.Blocks)
            {
                BlockDecoder? decoder = new BlockDecoder(block, band.Orientation, component.Coding.BlockStyle, budget);
                double[] values = decoder.Decode(band.Planes, band.Step, component.Coding.Reversible, component.RoiShift);
                int stride = lowest ? 1 : 2;
                int x0 = ((stride * block.X0) + (band.Orientation & 1)) - resolution.X0;
                int y0 = ((stride * block.Y0) + ((band.Orientation >> 1) & 1)) - resolution.Y0;
                for (int y = 0; y < block.Height; y++)
                {
                    for (int x = 0; x < block.Width; x++)
                    {
                        data[((y0 + (stride * y)) * resolution.Width) + x0 + (stride * x)] = values[(y * block.Width) + x];
                    }
                }
            }
        }

        private static void Filter(double[] data, Resolution resolution, bool reversible, DecodeBudget budget)
        {
            int w = resolution.Width;
            int h = resolution.Height;
            budget.Reserve((long)Math.Max(w, h) * 8);
            double[]? line = new double[Math.Max(w, h)];
            for (int y = 0; y < h; y++)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                Array.Copy(data, y * w, line, 0, w);
                FilterLine(line.AsSpan(0, w), resolution.X0, reversible);
                line.AsSpan(0, w).CopyTo(data.AsSpan(y * w));
            }

            for (int x = 0; x < w; x++)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                for (int y = 0; y < h; y++)
                {
                    line[y] = data[(y * w) + x];
                }

                FilterLine(line.AsSpan(0, h), resolution.Y0, reversible);
                for (int y = 0; y < h; y++)
                {
                    data[(y * w) + x] = line[y];
                }
            }
        }

        private static void FilterLine(Span<double> line, int origin, bool reversible)
        {
            if (line.Length == 0)
            {
                return;
            }

            if (line.Length == 1)
            {
                if ((origin & 1) != 0)
                {
                    line[0] = reversible ? Math.Truncate(line[0] / 2) : line[0] / 2;
                }

                return;
            }

            int even = origin & 1;
            if (reversible)
            {
                for (int i = even; i < line.Length; i += 2)
                {
                    line[i] -= Math.Floor((Neighbors(line, i) + 2) / 4);
                }

                for (int i = 1 - even; i < line.Length; i += 2)
                {
                    line[i] += Math.Floor(Neighbors(line, i) / 2);
                }
            }
            else
            {
                for (int i = 0; i < line.Length; i++)
                {
                    line[i] *= (i & 1) == even ? 1.230174104914001 : 1 / 1.230174104914001;
                }

                Lift(line, even, -0.443506852043971);
                Lift(line, 1 - even, -0.882911075530934);
                Lift(line, even, 0.052980118572961);
                Lift(line, 1 - even, 1.586134342059924);
            }
        }

        private static double Neighbors(Span<double> line, int index) => line[index == 0 ? 1 : index - 1] + line[index + 1 == line.Length ? index - 1 : index + 1];
        private static void Lift(Span<double> line, int parity, double factor)
        {
            for (int i = parity; i < line.Length; i += 2)
            {
                line[i] += factor * Neighbors(line, i);
            }
        }
    }
}
