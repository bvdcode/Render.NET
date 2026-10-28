using System;
using System.IO;

namespace Render.NET.Jbig2
{
    internal partial class DocumentDecoder
    {
        private Bitmap[] Patterns(BitReader input)
        {
            int flags = input.Byte(), width = input.Byte(), height = input.Byte();
            int count = checked(_budget.Count(input.U32(), _budget.Options.MaximumSymbols - 1) + 1), template = (flags >> 1) & 3;
            if (width == 0 || height == 0 || (flags & 0xf8) != 0)
            {
                throw new InvalidDataException("Invalid JBIG2 pattern dictionary.");
            }

            Bitmap collective;
            if ((flags & 1) != 0)
            {
                collective = MmrDecoder.Decode(input, checked(count * width), height, _budget);
            }
            else
            {
                (int, int)[]? at = new[]
                {
                    (-width, 0),
                    (-3, -1),
                    (2, -2),
                    (-2, -2)
                };
                collective = BitmapDecoder.Generic(checked(count * width), height, new Arithmetic(input.Rest(), _budget), template, at, false, GenericContexts(template), _budget);
            }

            _budget.Allocate((long)count * 8);
            Bitmap[]? result = new Bitmap[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = collective.Slice(i * width, width, _budget);
            }

            return result;
        }

        private byte[] GenericContexts(int template) => _budget.Contexts(1 << (template == 0 ? 16 : template == 1 ? 13 : 10));
        private void Halftone(Segment segment, BitReader input)
        {
            Region? region = Region(input);
            int flags = input.Byte(), template = (flags >> 1) & 3;
            int width = Dimension(input), height = Dimension(input), gx = input.I32(), gy = input.I32(), rx = input.U16(), ry = input.U16();
            System.Collections.Generic.List<Bitmap[]>? dictionaries = References<Bitmap[]>(segment);
            if (dictionaries.Count != 1)
            {
                throw new InvalidDataException("JBIG2 halftone requires one pattern dictionary.");
            }

            Bitmap[]? patterns = dictionaries[0];
            bool mmr = (flags & 1) != 0;
            Bitmap? result = new Bitmap(region.Width, region.Height, _budget, flags >> 7);
            if (mmr && (flags & 8) != 0)
            {
                throw new InvalidDataException("MMR halftones cannot skip pixels.");
            }

            Bitmap? skip = (flags & 8) == 0 ? null : HalftoneSkip(width, height, gx, gy, rx, ry, patterns[0], region);
            int bits = CodeBits(patterns.Length);
            Bitmap[]? planes = new Bitmap[bits];
            Arithmetic? arithmetic = mmr ? null : new Arithmetic(input.Rest(), _budget);
            byte[]? contexts = GenericContexts(template);
            (int, int)[]? at = new[]
            {
                (template <= 1 ? 3 : 2, -1),
                (-3, -1),
                (2, -2),
                (-2, -2)
            };
            for (int bit = bits - 1; bit >= 0; bit--)
            {
                planes[bit] = mmr ? MmrDecoder.Decode(input, width, height, _budget, true) : BitmapDecoder.Generic(width, height, arithmetic!, template, at, false, contexts, _budget, skip);
                if (bit + 1 < bits)
                {
                    planes[bit].Compose(planes[bit + 1], 0, 0, 2, _budget);
                }
            }

            RenderHalftone(result, planes, patterns, skip, width, height, gx, gy, rx, ry, (flags >> 4) & 7);
            Finish(segment, region, result);
        }

        private Bitmap HalftoneSkip(int width, int height, int gx, int gy, int rx, int ry, Bitmap pattern, Region region)
        {
            Bitmap? skip = new Bitmap(width, height, _budget);
            _budget.Work((long)width * height);
            for (int y = 0; y < height; y++)
            {
                _budget.Cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    (long X, long Y) p = Grid(x, y, gx, gy, rx, ry);
                    if (p.X + pattern.Width <= 0 || p.Y + pattern.Height <= 0 || p.X >= region.Width || p.Y >= region.Height)
                    {
                        skip.Set(x, y, 1);
                    }
                }
            }

            return skip;
        }

        private void RenderHalftone(Bitmap result, Bitmap[] planes, Bitmap[] patterns, Bitmap? skip, int width, int height, int gx, int gy, int rx, int ry, int op)
        {
            _budget.Work((long)width * height);
            for (int y = 0; y < height; y++)
            {
                _budget.Cancellation.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    if (skip?.Get(x, y) == 1)
                    {
                        continue;
                    }

                    int value = 0;
                    for (int bit = 0; bit < planes.Length; bit++)
                    {
                        value |= planes[bit].Get(x, y) << bit;
                    }

                    if (value >= patterns.Length)
                    {
                        throw new InvalidDataException("Invalid JBIG2 halftone pattern index.");
                    }

                    (long X, long Y) p = Grid(x, y, gx, gy, rx, ry);
                    result.Compose(patterns[value], p.X, p.Y, op, _budget);
                }
            }
        }

        private static (long X, long Y) Grid(int x, int y, int gx, int gy, int rx, int ry) => (((long)gx + ((long)y * ry) + ((long)x * rx)) >> 8, (((long)gy + ((long)y * rx)) - ((long)x * ry)) >> 8);
    }
}
