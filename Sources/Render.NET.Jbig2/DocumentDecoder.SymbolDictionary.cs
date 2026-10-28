using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal partial class DocumentDecoder
    {
        private List<T> References<T>(Segment segment)
            where T : class => segment.References.Select(n => _decoded.TryGetValue(n, out object? value) ? value as T : null).OfType<T>().ToList();
        private static int CodeBits(int count)
        {
            int bits = 0;
            while ((1L << bits) < count)
            {
                bits++;
            }

            return bits;
        }

        private HuffmanTable Select(List<HuffmanTable> custom, ref int index, int selection, int first, bool third = false)
        {
            if (selection == 3)
            {
                if (index >= custom.Count)
                {
                    throw new InvalidDataException("Missing JBIG2 custom Huffman table.");
                }

                return custom[index++];
            }

            if (selection == 2 && !third)
            {
                throw new InvalidDataException("Invalid JBIG2 Huffman selection.");
            }

            return StandardHuffman.Tables[first + selection];
        }

        private SymbolSettings SymbolHeader(BitReader input)
        {
            int flags = input.U16(), template = (flags >> 10) & 3, refTemplate = (flags >> 12) & 1;
            bool huffman = (flags & 1) != 0, aggregate = (flags & 2) != 0;
            if ((flags & 0xe000) != 0)
            {
                throw new NotSupportedException("Unsupported JBIG2 dictionary flags.");
            }

            (int, int)[]? at = huffman ? Array.Empty<(int, int)>() : BitmapDecoder.Adaptive(input, template == 0 ? 4 : 1);
            (int, int)[]? refAt = aggregate && refTemplate == 0 ? BitmapDecoder.Adaptive(input, 2) : Array.Empty<(int, int)>();
            int exported = _budget.Count(input.U32(), _budget.Options.MaximumSymbols), added = _budget.Count(input.U32(), _budget.Options.MaximumSymbols);
            return new SymbolSettings(flags, at, refAt, exported, added, GenericContexts(template), _budget.Contexts(refTemplate == 0 ? 8192 : 1024));
        }

        private SymbolDictionary Symbols(Segment segment, BitReader input)
        {
            SymbolSettings? settings = SymbolHeader(input);
            List<SymbolDictionary>? dictionaries = References<SymbolDictionary>(segment);
            int total = _budget.Count(checked((uint)(dictionaries.Sum(d => (long)d.Symbols.Length) + settings.Added)), _budget.Options.MaximumSymbols);
            _budget.Allocate((long)total * 16);
            var symbols = dictionaries.SelectMany(d => d.Symbols).ToList();
            if (total > _budget.Options.MaximumSymbols)
            {
                throw new InvalidDataException("JBIG2 symbol count exceeds MaximumSymbols.");
            }

            if ((settings.Flags & 256) != 0)
            {
                Restore(dictionaries, settings.Flags, settings.At, settings.RefAt, settings.Generic, settings.Refinement);
            }

            settings.IdBits = Math.Max(settings.Huffman ? 1 : 0, CodeBits(total));
            settings.Tables = SymbolTables(segment, settings.Flags);
            RegionCoding? coding = new RegionCoding(input, settings.Huffman, _budget);
            int height = 0, decoded = 0;
            while (decoded < settings.Added)
            {
                _budget.Cancellation.ThrowIfCancellationRequested();
                height = checked(height + coding.Number("IADH", settings.Tables[0]));
                decoded = SymbolHeightClass(coding, symbols, settings, height, decoded);
            }

            return Export(coding, symbols, settings.Exported, settings.Generic, settings.Refinement, settings.Flags, settings.At, settings.RefAt);
        }

        private HuffmanTable[] SymbolTables(Segment segment, int flags)
        {
            List<HuffmanTable>? custom = References<HuffmanTable>(segment);
            int index = 0;
            return new[]
            {
                Select(custom, ref index, (flags >> 2) & 3, 4),
                Select(custom, ref index, (flags >> 4) & 3, 2),
                (flags & 64) == 0 ? StandardHuffman.Tables[1] : Select(custom, ref index, 3, 1),
                (flags & 128) == 0 ? StandardHuffman.Tables[1] : Select(custom, ref index, 3, 1)
            };
        }

        private int SymbolHeightClass(RegionCoding coding, List<Bitmap> symbols, SymbolSettings settings, int height, int decoded)
        {
            int width = 0, first = decoded;
            var widths = new List<int>();
            while (true)
            {
                int? delta = coding.Read("IADW", settings.Tables[1]);
                if (delta is null)
                {
                    break;
                }

                if (decoded >= settings.Added)
                {
                    throw new InvalidDataException("JBIG2 height class exceeds symbol count.");
                }

                width = checked(width + delta.Value);
                if (width < 0 || height < 0)
                {
                    throw new InvalidDataException("Negative JBIG2 symbol dimensions.");
                }

                if (settings.Aggregate)
                {
                    symbols.Add(Aggregate(coding, symbols, width, height, settings.RefTemplate, settings.RefAt, settings.Refinement, settings.IdBits, settings.Tables[3]));
                }
                else if (!settings.Huffman)
                {
                    symbols.Add(BitmapDecoder.Generic(width, height, coding.Arithmetic!, settings.Template, settings.At, false, settings.Generic, _budget));
                }
                else
                {
                    widths.Add(width);
                }

                decoded++;
            }

            if (decoded == first)
            {
                throw new InvalidDataException("Empty JBIG2 height class.");
            }

            if (settings.Huffman && !settings.Aggregate)
            {
                Collective(coding.Input, symbols, widths, height, settings.Tables[2]);
            }

            return decoded;
        }

        private void Collective(BitReader input, List<Bitmap> symbols, List<int> widths, int height, HuffmanTable size)
        {
            int combined = widths.Sum(), length = size.Required(input);
            input.Align();
            Bitmap collective;
            if (length == 0)
            {
                collective = new Bitmap(combined, height, _budget);
                input.Take(collective.Data.Length).Span.CopyTo(collective.Data);
                collective.ClearPadding();
            }
            else
            {
                collective = MmrDecoder.Decode(new BitReader(input.Take(length)), combined, height, _budget);
            }

            int x = 0;
            foreach (int width in widths)
            {
                symbols.Add(collective.Slice(x, width, _budget));
                x += width;
            }
        }

        private static void Restore(List<SymbolDictionary> dictionaries, int flags, (int, int)[] at, (int, int)[] refAt, byte[] generic, byte[] refinement)
        {
            if (dictionaries.Count == 0)
            {
                throw new InvalidDataException("Missing retained JBIG2 contexts.");
            }

            SymbolDictionary? last = dictionaries[^1];
            if ((last.Flags & 512) == 0 || (last.Flags & 0x1c03) != (flags & 0x1c03) || !at.SequenceEqual(last.At) || !refAt.SequenceEqual(last.RefAt))
            {
                throw new InvalidDataException("Incompatible retained JBIG2 contexts.");
            }

            last.Generic.CopyTo(generic, 0);
            last.Refinement.CopyTo(refinement, 0);
        }

        private SymbolDictionary Export(RegionCoding coding, List<Bitmap> symbols, int count, byte[] generic, byte[] refinement, int flags, (int, int)[] at, (int, int)[] refAt)
        {
            var result = new List<Bitmap>();
            int position = 0, runs = 0;
            bool include = false;
            while (position < symbols.Count)
            {
                if (++runs > _budget.Options.MaximumInstances)
                {
                    throw new InvalidDataException("JBIG2 export run limit exceeded.");
                }

                int length = coding.Number("IAEX", StandardHuffman.Tables[1]);
                if (length < 0 || length > symbols.Count - position)
                {
                    throw new InvalidDataException("Invalid JBIG2 export run.");
                }

                for (int i = 0; i < length; i++)
                {
                    if (include)
                    {
                        result.Add(symbols[position + i]);
                    }
                }

                position += length;
                include = !include;
            }

            if (result.Count != count)
            {
                throw new InvalidDataException("JBIG2 exported symbol count differs from header.");
            }

            return new SymbolDictionary(result.ToArray(), generic, refinement, flags, at, refAt);
        }

        private Bitmap Aggregate(RegionCoding coding, List<Bitmap> symbols, int width, int height, int template, (int, int)[] at, byte[] contexts, int idBits, HuffmanTable agg)
        {
            int count = coding.Number("IAAI", agg);
            if (count <= 0 || count > _budget.Options.MaximumInstances)
            {
                throw new InvalidDataException("Invalid JBIG2 aggregation count.");
            }

            if (count > 1)
            {
                TextSettings? settings = new TextSettings(18, template, at, count)
                {
                    IdBits = idBits
                };
                for (int i = 3; i < 7; i++)
                {
                    settings.Tables[i] = StandardHuffman.Tables[15];
                }

                return RenderText(width, height, coding, symbols, settings, contexts);
            }

            int id = coding.Id(idBits);
            if ((uint)id >= symbols.Count)
            {
                throw new InvalidDataException("Invalid JBIG2 refinement symbol ID.");
            }

            int dx = coding.Number("IARDX", StandardHuffman.Tables[15]), dy = coding.Number("IARDY", StandardHuffman.Tables[15]);
            return BitmapDecoder.Refine(width, height, coding.Refinement(StandardHuffman.Tables[1], _budget), template, at, false, contexts, symbols[id], dx, dy, _budget);
        }
    }
}
