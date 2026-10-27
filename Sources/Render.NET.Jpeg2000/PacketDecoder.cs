using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Render.NET.Jpeg2000
{
    internal static class PacketDecoder
    {
        internal static void Read(ByteReader input, Tile tile, DecodeBudget budget)
        {
            while (input.Remaining > 0)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                if (tile.PacketIndex >= tile.Packets.Length)
                {
                    throw new InvalidDataException("Unexpected bytes after JPEG 2000 packets.");
                }

                Packet packet = tile.Packets[tile.PacketIndex++];
                TileComponent component = tile.Components[packet.Component];
                int flags = tile.Settings.DefaultCoding!.Flags;
                if ((flags & 2) != 0)
                {
                    Sop(input);
                }

                var bits = new PacketBits(input);
                var contributions = new List<(CodewordSegment Segment, int Length)>();
                if (bits.Read() != 0)
                {
                    Header(bits, component.Resolutions[packet.Resolution].Precincts[packet.Precinct], packet.Layer, component.Coding.BlockStyle, contributions, budget);
                }

                bits.Align();
                if ((flags & 4) != 0 && input.Marker() != 0xff92)
                {
                    throw new InvalidDataException("Missing JPEG 2000 EPH marker.");
                }

                foreach (var item in contributions)
                {
                    item.Segment.Parts.Add(input.Take(item.Length));
                }
            }
        }

        private static void Sop(ByteReader input)
        {
            if (input.Marker() != 0xff91 || input.U16() != 4)
            {
                throw new InvalidDataException("Invalid JPEG 2000 SOP marker.");
            }

            input.U16();
        }

        private static void Header(PacketBits bits, Precinct precinct, int layer, int style, List<(CodewordSegment Segment, int Length)> contributions, DecodeBudget budget)
        {
            foreach (var band in precinct.Bands)
            {
                for (int i = 0; i < band.Blocks.Count; i++)
                {
                    var block = band.Blocks[i];
                    bool fresh = block.Passes == 0;
                    bool included = fresh ? band.Inclusion.Below(i, layer + 1, bits) : bits.Read() != 0;
                    if (!included)
                    {
                        continue;
                    }

                    if (fresh)
                    {
                        while (!band.ZeroPlanes.Below(i, block.ZeroPlanes + 1, bits))
                        {
                            if (++block.ZeroPlanes > 30)
                            {
                                throw new InvalidDataException("Invalid JPEG 2000 zero bit-plane count.");
                            }
                        }
                    }

                    int passes = bits.PassCount();
                    while (bits.Read() != 0)
                    {
                        if (++block.LengthBits > 30)
                        {
                            throw new InvalidDataException("Invalid JPEG 2000 segment length bits.");
                        }
                    }

                    Segments(block, passes, bits, style, contributions, budget);
                }
            }
        }

        private static void Segments(CodeBlock block, int passes, PacketBits bits, int style, List<(CodewordSegment Segment, int Length)> contributions, DecodeBudget budget)
        {
            if (block.Passes + passes > 91)
            {
                throw new NotSupportedException("JPEG 2000 coefficient precision above 30 bits.");
            }

            while (passes > 0)
            {
                CodewordSegment? segment = block.Segments.Count == 0 ? null : block.Segments[^1];
                if (segment == null || segment.Passes == segment.Capacity)
                {
                    budget.Reserve(64);
                    segment = new CodewordSegment(block.Passes, style);
                    block.Segments.Add(segment);
                }

                int count = Math.Min(passes, segment.Capacity - segment.Passes);
                int length = bits.Read(block.LengthBits + BitOperations.Log2((uint)count));
                contributions.Add((segment, length));
                segment.Passes += count;
                block.Passes += count;
                passes -= count;
            }
        }
    }
}
