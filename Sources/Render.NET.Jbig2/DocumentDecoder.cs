using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal sealed record Segment(uint Number, int Type, uint Page, uint[] References, int Length)
    {
        internal ReadOnlyMemory<byte> Data { get; set; }
    }

    internal sealed record Region(int Width, int Height, int X, int Y, int Op);
    internal sealed record PageState(Bitmap Bitmap, int Flags);
    internal sealed partial class DocumentDecoder
    {
        private static readonly byte[] Signature =
        {
            0x97,
            0x4a,
            0x42,
            0x32,
            0x0d,
            0x0a,
            0x1a,
            0x0a
        };
        private readonly DecodeBudget _budget;
        private readonly Dictionary<uint, Segment> _segments = new();
        private readonly Dictionary<uint, object> _decoded = new();
        private readonly SortedDictionary<uint, PageState> _pages = new();
        internal DocumentDecoder(DecodeBudget budget)
        {
            _budget = budget;
        }

        internal Jbig2Document Decode(ReadOnlyMemory<byte> encoded, ReadOnlyMemory<byte> globals)
        {
            Read(globals, true);
            Read(encoded, false);
            if (_pages.Count == 0)
            {
                throw new InvalidDataException("JBIG2 contains no pages.");
            }

            return new Jbig2Document(_pages.Select(p => new Jbig2Page(p.Key, p.Value.Bitmap)).ToArray());
        }

        private void Read(ReadOnlyMemory<byte> data, bool globals)
        {
            var input = new BitReader(data);
            bool sequential = true;
            if (data.Span.StartsWith(Signature))
            {
                input.Take(8);
                int flags = input.Byte();
                if ((flags & 0xfc) != 0)
                {
                    throw new NotSupportedException("Unsupported JBIG2 file flags.");
                }

                sequential = (flags & 1) != 0;
                if ((flags & 2) == 0)
                {
                    _budget.Count(input.U32(), _budget.Options.MaximumPages);
                }
            }

            var pending = new List<Segment>();
            while (input.Remaining > 0)
            {
                _budget.Cancellation.ThrowIfCancellationRequested();
                var segment = Header(input);
                if (globals && segment.Page != 0)
                {
                    throw new InvalidDataException("JBIG2 globals contain a page-associated segment.");
                }

                if (_segments.Count >= _budget.Options.MaximumSegments || !_segments.TryAdd(segment.Number, segment))
                {
                    throw new InvalidDataException("Duplicate JBIG2 segment or MaximumSegments exceeded.");
                }

                if (sequential)
                {
                    segment.Data = input.Take(segment.Length);
                    DecodeSegment(segment);
                }
                else
                {
                    pending.Add(segment);
                }

                if (segment.Type == 51)
                {
                    break;
                }
            }

            foreach (var segment in pending)
            {
                segment.Data = input.Take(segment.Length);
                DecodeSegment(segment);
            }

            if (input.Remaining != 0)
            {
                throw new InvalidDataException("Trailing JBIG2 data.");
            }
        }

        private Segment Header(BitReader input)
        {
            uint number = input.U32();
            int flags = input.Byte(), field = input.Byte(), count = field >> 5;
            if (count == 7)
            {
                uint extended = ((uint)field << 24) | ((uint)input.Byte() << 16) | ((uint)input.Byte() << 8) | input.Byte();
                count = _budget.Count(extended & 0x1fffffff, _budget.Options.MaximumSegments);
                input.Take((count + 8) / 8);
            }
            else if (count > 4)
            {
                throw new InvalidDataException("Invalid JBIG2 reference count.");
            }

            _budget.Allocate(((long)count * 4) + 128);
            var references = new uint[count];
            for (int i = 0; i < count; i++)
            {
                uint reference = number <= 256 ? input.Byte() : number <= 65536 ? (uint)input.U16() : input.U32();
                if (reference >= number)
                {
                    throw new InvalidDataException("JBIG2 references must precede the referring segment.");
                }

                references[i] = reference;
            }

            uint page = (flags & 64) == 0 ? input.Byte() : input.U32(), length = input.U32();
            if (length == uint.MaxValue)
            {
                throw new NotSupportedException("Unknown-length JBIG2 segments are not supported.");
            }

            return new Segment(number, flags & 63, page, references, _budget.Count(length, _budget.Options.MaximumEncodedBytes));
        }

        private void DecodeSegment(Segment segment)
        {
            foreach (uint reference in segment.References)
            {
                if (!_segments.TryGetValue(reference, out var target) || (target.Page != 0 && target.Page != segment.Page) || !AllowedReference(segment.Type, target.Type))
                {
                    throw new InvalidDataException("Missing or cross-page JBIG2 segment reference.");
                }
            }

            var input = new BitReader(segment.Data);
            if (segment.Type >= 48)
            {
                Metadata(segment, input);
                return;
            }

            switch (segment.Type)
            {
                case 0:
                    _decoded[segment.Number] = Symbols(segment, input);
                    break;
                case 16:
                    _decoded[segment.Number] = Patterns(input);
                    break;
                case 4 or 6 or 7:
                    Text(segment, input);
                    break;
                case 20 or 22 or 23:
                    Halftone(segment, input);
                    break;
                case 36 or 38 or 39:
                    Generic(segment, input);
                    break;
                case 40 or 42 or 43:
                    Refinement(segment, input);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported JBIG2 segment type {segment.Type}.");
            }
        }

        private static bool AllowedReference(int type, int target) => type switch
        {
            0 or 4 or 6 or 7 => target is 0 or 53,
            20 or 22 or 23 => target == 16,
            40 or 42 or 43 => target is 4 or 20 or 36 or 40,
            62 => true,
            _ => false
        };
        private void Metadata(Segment segment, BitReader input)
        {
            switch (segment.Type)
            {
                case 48:
                    Page(segment, input);
                    break;
                case 49 or 50 or 51 or 52:
                    break;
                case 53:
                    _decoded[segment.Number] = HuffmanTable.Custom(input, _budget);
                    break;
                case 62:
                    if ((input.U32() & 0x80000000) != 0)
                    {
                        throw new NotSupportedException("Necessary JBIG2 extension is unsupported.");
                    }

                    break;
                default:
                    throw new NotSupportedException($"Unsupported JBIG2 metadata type {segment.Type}.");
            }
        }

        private int Dimension(BitReader input) => _budget.Count(input.U32(), _budget.Options.MaximumPixels);
        private void Page(Segment segment, BitReader input)
        {
            int width = Dimension(input), height = Dimension(input);
            input.Take(8);
            int flags = input.Byte();
            input.U16();
            if (segment.Page == 0 || _pages.ContainsKey(segment.Page) || _pages.Count >= _budget.Options.MaximumPages || width == 0 || height == 0)
            {
                throw new InvalidDataException("Invalid JBIG2 page information.");
            }

            if ((_budget.Options.ExpectedWidth is int w && w != width) || (_budget.Options.ExpectedHeight is int h && h != height))
            {
                throw new InvalidDataException("JBIG2 page dimensions differ from the required dimensions.");
            }

            _pages.Add(segment.Page, new PageState(new Bitmap(width, height, _budget, (flags >> 2) & 1), flags));
        }

        private Region Region(BitReader input)
        {
            int w = Dimension(input), h = Dimension(input), x = input.I32(), y = input.I32(), op = input.Byte();
            if (op > 4)
            {
                throw new NotSupportedException("Unsupported JBIG2 region flags.");
            }

            return new Region(w, h, x, y, op);
        }

        private void Finish(Segment segment, Region region, Bitmap bitmap)
        {
            if (!_pages.TryGetValue(segment.Page, out var page))
            {
                throw new InvalidDataException("JBIG2 region precedes its page information.");
            }

            if ((segment.Type & 2) == 0)
            {
                _decoded.Add(segment.Number, bitmap);
                return;
            }

            int op = (page.Flags & 64) != 0 || segment.Type >= 40 ? region.Op : (page.Flags >> 3) & 3;
            page.Bitmap.Compose(bitmap, region.X, region.Y, op, _budget);
        }

        private void Generic(Segment segment, BitReader input)
        {
            var region = Region(input);
            int flags = input.Byte(), template = (flags >> 1) & 3;
            if ((flags & 0xf0) != 0)
            {
                throw new NotSupportedException("Extended JBIG2 templates are unsupported.");
            }

            Bitmap bitmap;
            if ((flags & 1) != 0)
            {
                bitmap = MmrDecoder.Decode(input, region.Width, region.Height, _budget);
            }
            else
            {
                var at = BitmapDecoder.Adaptive(input, template == 0 ? 4 : 1);
                bitmap = BitmapDecoder.Generic(region.Width, region.Height, new Arithmetic(input.Rest(), _budget), template, at, (flags & 8) != 0, _budget.Contexts(1 << (template == 0 ? 16 : template == 1 ? 13 : 10)), _budget);
            }

            Finish(segment, region, bitmap);
        }

        private void Refinement(Segment segment, BitReader input)
        {
            var region = Region(input);
            int flags = input.Byte(), template = flags & 1;
            if ((flags & 0xfc) != 0)
            {
                throw new InvalidDataException("Invalid JBIG2 refinement flags.");
            }

            var at = template == 0 ? BitmapDecoder.Adaptive(input, 2) : Array.Empty<(int, int)>();
            Bitmap reference;
            int dx = 0, dy = 0;
            if (segment.References.Length == 1 && _decoded[segment.References[0]] is Bitmap referred)
            {
                reference = referred;
            }
            else if (segment.References.Length == 0 && _pages.TryGetValue(segment.Page, out var page))
            {
                reference = page.Bitmap;
                dx = -region.X;
                dy = -region.Y;
            }
            else
            {
                throw new InvalidDataException("Invalid JBIG2 refinement reference.");
            }

            var bitmap = BitmapDecoder.Refine(region.Width, region.Height, new Arithmetic(input.Rest(), _budget), template, at, (flags & 2) != 0, _budget.Contexts(template == 0 ? 8192 : 1024), reference, dx, dy, _budget);
            Finish(segment, region, bitmap);
        }
    }
}
