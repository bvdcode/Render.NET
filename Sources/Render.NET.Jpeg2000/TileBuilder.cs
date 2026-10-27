using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jpeg2000
{
    internal static class TileBuilder
    {
        internal static Tile Build(Jpeg2000Component[] outputs, HeaderSettings settings, int x0, int y0, int x1, int y1, DecodeBudget budget)
        {
            CodingStyle coding = settings.DefaultCoding ?? throw new InvalidDataException("JPEG 2000 is missing COD.");
            var components = new TileComponent[outputs.Length];
            var packets = new List<Packet>();
            for (int c = 0; c < outputs.Length; c++)
            {
                components[c] = Component(outputs[c], settings, c, x0, y0, x1, y1, budget);
                AddPackets(packets, components[c], c, coding.Layers, budget);
            }

            return new Tile
            {
                Components = components,
                Settings = settings,
                Packets = Order(packets, coding.Progression).ToArray()
            };
        }

        private static TileComponent Component(Jpeg2000Component output, HeaderSettings settings, int c, int x0, int y0, int x1, int y1, DecodeBudget budget)
        {
            CodingStyle coding = settings.Coding.GetValueOrDefault(c) ?? settings.DefaultCoding!;
            Quantization quantization = settings.Quantization.GetValueOrDefault(c) ?? settings.DefaultQuantization ?? throw new InvalidDataException("JPEG 2000 is missing QCD.");
            if (coding.Reversible && quantization.Kind != 0)
            {
                throw new InvalidDataException("Reversible JPEG 2000 requires no quantization.");
            }

            int cx0 = ByteReader.Ceil(x0, output.HorizontalSampling);
            int cy0 = ByteReader.Ceil(y0, output.VerticalSampling);
            int cx1 = ByteReader.Ceil(x1, output.HorizontalSampling);
            int cy1 = ByteReader.Ceil(y1, output.VerticalSampling);
            var metadata = new Jpeg2000Component(cx0, cy0, cx1 - cx0, cy1 - cy0, output.BitDepth, output.IsSigned, output.HorizontalSampling, output.VerticalSampling, Array.Empty<int>());
            var resolutions = new Resolution[coding.Levels + 1];
            for (int r = 0; r < resolutions.Length; r++)
            {
                resolutions[r] = Resolution(metadata, coding, quantization, r, x0, y0, budget);
            }

            return new TileComponent
            {
                Output = metadata,
                Coding = coding,
                RoiShift = settings.Roi.GetValueOrDefault(c),
                Resolutions = resolutions
            };
        }

        private static Resolution Resolution(Jpeg2000Component component, CodingStyle coding, Quantization quantization, int r, int tileX, int tileY, DecodeBudget budget)
        {
            int scale = 1 << (coding.Levels - r);
            int x0 = ByteReader.Ceil(component.X0, scale);
            int y0 = ByteReader.Ceil(component.Y0, scale);
            int x1 = ByteReader.Ceil(component.X0 + component.Width, scale);
            int y1 = ByteReader.Ceil(component.Y0 + component.Height, scale);
            var resolution = new Resolution
            {
                X0 = x0,
                Y0 = y0,
                Width = x1 - x0,
                Height = y1 - y0
            };
            int px = coding.Precincts[r] & 15;
            int py = coding.Precincts[r] >> 4;
            int pw = 1 << px;
            int ph = 1 << py;
            int gridX = x0 / pw;
            int gridY = y0 / ph;
            int across = x1 > x0 ? ByteReader.Ceil(x1, pw) - gridX : 0;
            int down = y1 > y0 ? ByteReader.Ceil(y1, ph) - gridY : 0;
            long count = (long)across * down;
            if (count > budget.Options.MaximumPackets)
            {
                throw new InvalidDataException("Too many JPEG 2000 precincts.");
            }

            budget.Reserve(count * 64);
            resolution.Precincts = new Precinct[(int)count];
            for (int y = 0; y < down; y++)
            {
                for (int x = 0; x < across; x++)
                {
                    resolution.Precincts[(y * across) + x] = new Precinct
                    {
                        X = Math.Max(tileX, (long)(gridX + x) * pw * scale * component.HorizontalSampling),
                        Y = Math.Max(tileY, (long)(gridY + y) * ph * scale * component.VerticalSampling)
                    };
                }
            }

            for (int orientation = r == 0 ? 0 : 1; orientation <= (r == 0 ? 0 : 3); orientation++)
            {
                Subband band = Band(component, coding, quantization, r, orientation);
                resolution.Bands.Add(band);
                Populate(band, resolution, coding, r, gridX, gridY, across, down, budget);
            }

            return resolution;
        }

        private static Subband Band(Jpeg2000Component component, CodingStyle coding, Quantization quantization, int r, int orientation)
        {
            int scale = 1 << (coding.Levels - r);
            int divisor = orientation == 0 ? scale : scale * 2;
            int ox = (orientation & 1) * scale;
            int oy = ((orientation >> 1) & 1) * scale;
            int x0 = ByteReader.Ceil(component.X0 - ox, divisor);
            int y0 = ByteReader.Ceil(component.Y0 - oy, divisor);
            int x1 = ByteReader.Ceil((component.X0 + component.Width) - ox, divisor);
            int y1 = ByteReader.Ceil((component.Y0 + component.Height) - oy, divisor);
            int index = r == 0 ? 0 : (3 * (r - 1)) + orientation;
            var quant = quantization.ForBand(index, r, coding.Levels, component.BitDepth, orientation, coding.Reversible);
            return new Subband
            {
                X0 = x0,
                Y0 = y0,
                Width = x1 - x0,
                Height = y1 - y0,
                Orientation = orientation,
                Planes = quant.Planes,
                Step = quant.Step
            };
        }

        private static void Populate(Subband band, Resolution resolution, CodingStyle coding, int r, int gridX, int gridY, int across, int down, DecodeBudget budget)
        {
            int px = (coding.Precincts[r] & 15) - (r == 0 ? 0 : 1);
            int py = (coding.Precincts[r] >> 4) - (r == 0 ? 0 : 1);
            int pw = 1 << px;
            int ph = 1 << py;
            int bw = 1 << Math.Min(coding.BlockX, px);
            int bh = 1 << Math.Min(coding.BlockY, py);
            for (int y = 0; y < down; y++)
            {
                for (int x = 0; x < across; x++)
                {
                    int x0 = (int)Math.Max(band.X0, (long)(gridX + x) * pw);
                    int y0 = (int)Math.Max(band.Y0, (long)(gridY + y) * ph);
                    int x1 = (int)Math.Min(band.X0 + band.Width, ((long)gridX + x + 1) * pw);
                    int y1 = (int)Math.Min(band.Y0 + band.Height, ((long)gridY + y + 1) * ph);
                    var part = new PrecinctBand(band);
                    if (x0 < x1 && y0 < y1)
                    {
                        Blocks(part, x0, y0, x1, y1, bw, bh, budget);
                    }

                    resolution.Precincts[(y * across) + x].Bands.Add(part);
                }
            }
        }

        private static void Blocks(PrecinctBand part, int x0, int y0, int x1, int y1, int width, int height, DecodeBudget budget)
        {
            int bx0 = x0 / width;
            int by0 = y0 / height;
            int bx1 = ByteReader.Ceil(x1, width);
            int by1 = ByteReader.Ceil(y1, height);
            budget.Reserve((long)(bx1 - bx0) * (by1 - by0) * 160);
            part.Inclusion = new TagTree(bx1 - bx0, by1 - by0, budget);
            part.ZeroPlanes = new TagTree(bx1 - bx0, by1 - by0, budget);
            for (int y = by0; y < by1; y++)
            {
                for (int x = bx0; x < bx1; x++)
                {
                    int left = Math.Max(x0, x * width);
                    int top = Math.Max(y0, y * height);
                    var block = new CodeBlock
                    {
                        X0 = left,
                        Y0 = top,
                        Width = (int)Math.Min(x1, ((long)x + 1) * width) - left,
                        Height = (int)Math.Min(y1, ((long)y + 1) * height) - top
                    };
                    part.Blocks.Add(block);
                    part.Band.Blocks.Add(block);
                }
            }
        }

        private static void AddPackets(List<Packet> packets, TileComponent component, int c, int layers, DecodeBudget budget)
        {
            for (int r = 0; r < component.Resolutions.Length; r++)
            {
                var precincts = component.Resolutions[r].Precincts;
                budget.Packets((long)precincts.Length * layers);
                budget.Reserve((long)precincts.Length * layers * 80);
                for (int p = 0; p < precincts.Length; p++)
                {
                    for (int l = 0; l < layers; l++)
                    {
                        packets.Add(new Packet(l, r, c, p, precincts[p].X, precincts[p].Y));
                    }
                }
            }
        }

        private static IOrderedEnumerable<Packet> Order(List<Packet> packets, int progression) => progression switch
        {
            0 => packets.OrderBy(p => p.Layer).ThenBy(p => p.Resolution).ThenBy(p => p.Component).ThenBy(p => p.Precinct),
            1 => packets.OrderBy(p => p.Resolution).ThenBy(p => p.Layer).ThenBy(p => p.Component).ThenBy(p => p.Precinct),
            2 => packets.OrderBy(p => p.Resolution).ThenBy(p => p.Y).ThenBy(p => p.X).ThenBy(p => p.Component).ThenBy(p => p.Layer),
            3 => packets.OrderBy(p => p.Y).ThenBy(p => p.X).ThenBy(p => p.Component).ThenBy(p => p.Resolution).ThenBy(p => p.Layer),
            4 => packets.OrderBy(p => p.Component).ThenBy(p => p.Y).ThenBy(p => p.X).ThenBy(p => p.Resolution).ThenBy(p => p.Layer),
            _ => throw new NotSupportedException("JPEG 2000 progression order.")};
    }
}
