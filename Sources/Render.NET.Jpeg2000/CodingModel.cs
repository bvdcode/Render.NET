using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal sealed record CodingStyle(int Flags, int Progression, int Layers, bool Mct, int Levels, int BlockX, int BlockY, int BlockStyle, bool Reversible, byte[] Precincts);
    internal sealed record Quantization(int Guard, int Kind, int[] Exponents, int[] Mantissas)
    {
        internal (int Planes, double Step) ForBand(int index, int resolution, int levels, int bits, int orientation, bool reversible)
        {
            int i = Kind == 1 ? 0 : index;
            if (i >= Exponents.Length)
            {
                throw new InvalidDataException("Missing JPEG 2000 quantization parameters.");
            }

            int exponent = Exponents[i] - (Kind == 1 && resolution > 0 ? resolution - 1 : 0);
            int planes = (Guard + exponent) - 1;
            if (planes < 0 || planes > 30)
            {
                throw new NotSupportedException("JPEG 2000 coefficient precision above 30 bits.");
            }

            int gain = orientation == 0 ? 0 : orientation == 3 ? 2 : 1;
            double step = reversible ? 1 : Math.ScaleB(1 + (Mantissas[i] / 2048.0), (bits + gain) - exponent);
            return (planes, step);
        }
    }

    internal sealed class HeaderSettings
    {
        internal CodingStyle? DefaultCoding { get; set; }
        internal Quantization? DefaultQuantization { get; set; }
        internal Dictionary<int, CodingStyle> Coding { get; } = new();
        internal Dictionary<int, Quantization> Quantization { get; } = new();
        internal Dictionary<int, int> Roi { get; } = new();

        internal HeaderSettings Clone()
        {
            var result = new HeaderSettings
            {
                DefaultCoding = DefaultCoding,
                DefaultQuantization = DefaultQuantization
            };
            foreach (var item in Coding)
            {
                result.Coding.Add(item.Key, item.Value);
            }

            foreach (var item in Quantization)
            {
                result.Quantization.Add(item.Key, item.Value);
            }

            foreach (var item in Roi)
            {
                result.Roi.Add(item.Key, item.Value);
            }

            return result;
        }
    }

    internal sealed class CodewordSegment
    {
        internal int FirstPass { get; }
        internal int Capacity { get; }
        internal bool Raw { get; }
        internal int Passes { get; set; }
        internal List<ReadOnlyMemory<byte>> Parts { get; } = new();

        internal CodewordSegment(int pass, int style)
        {
            FirstPass = pass;
            Raw = (style & 1) != 0 && pass >= 10 && pass % 3 != 0;
            Capacity = (style & 4) != 0 ? 1 : (style & 1) == 0 ? int.MaxValue : pass < 10 ? 10 : Raw ? 2 : 1;
        }

        internal byte[] Join(DecodeBudget budget)
        {
            int count = 0;
            foreach (var part in Parts)
            {
                count = checked(count + part.Length);
            }

            budget.Reserve(count);
            var data = new byte[count];
            int offset = 0;
            foreach (var part in Parts)
            {
                part.Span.CopyTo(data.AsSpan(offset));
                offset += part.Length;
            }

            return data;
        }
    }

    internal sealed class CodeBlock
    {
        internal int X0 { get; init; }
        internal int Y0 { get; init; }
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal int ZeroPlanes { get; set; }
        internal int Passes { get; set; }
        internal int LengthBits { get; set; } = 3;
        internal List<CodewordSegment> Segments { get; } = new();
    }

    internal sealed class Subband
    {
        internal int X0 { get; init; }
        internal int Y0 { get; init; }
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal int Orientation { get; init; }
        internal int Planes { get; init; }
        internal double Step { get; init; }
        internal List<CodeBlock> Blocks { get; } = new();
    }

    internal sealed class PrecinctBand
    {
        internal Subband Band { get; }
        internal List<CodeBlock> Blocks { get; } = new();
        internal TagTree Inclusion { get; set; } = null!;
        internal TagTree ZeroPlanes { get; set; } = null!;

        internal PrecinctBand(Subband band) => Band = band;
    }

    internal sealed class Precinct
    {
        internal long X { get; init; }
        internal long Y { get; init; }
        internal List<PrecinctBand> Bands { get; } = new();
    }

    internal sealed class Resolution
    {
        internal int X0 { get; init; }
        internal int Y0 { get; init; }
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal List<Subband> Bands { get; } = new();
        internal Precinct[] Precincts { get; set; } = Array.Empty<Precinct>();
    }

    internal sealed class TileComponent
    {
        internal Jpeg2000Component Output { get; init; } = null!;
        internal CodingStyle Coding { get; init; } = null!;
        internal int RoiShift { get; init; }
        internal Resolution[] Resolutions { get; init; } = null!;
        internal double[] Reconstructed { get; set; } = Array.Empty<double>();
    }

    internal readonly record struct Packet(int Layer, int Resolution, int Component, int Precinct, long X, long Y);
    internal sealed class Tile
    {
        internal TileComponent[] Components { get; init; } = null!;
        internal HeaderSettings Settings { get; init; } = null!;
        internal Packet[] Packets { get; init; } = null!;
        internal int PacketIndex { get; set; }
        internal int NextPart { get; set; }
    }
}
