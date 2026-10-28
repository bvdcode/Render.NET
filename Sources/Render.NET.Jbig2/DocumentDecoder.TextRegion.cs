using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal partial class DocumentDecoder
    {
        private void Text(Segment segment, BitReader input)
        {
            Region? region = Region(input);
            int flags = input.U16();
            bool huffman = (flags & 1) != 0;
            int huffFlags = huffman ? input.U16() : 0, template = (flags >> 15) & 1;
            (int, int)[]? at = (flags & 2) != 0 && template == 0 ? BitmapDecoder.Adaptive(input, 2) : Array.Empty<(int, int)>();
            TextSettings? settings = new TextSettings(flags, template, at, _budget.Count(input.U32(), _budget.Options.MaximumInstances));
            List<SymbolDictionary>? dictionaries = References<SymbolDictionary>(segment);
            int count = _budget.Count(checked((uint)dictionaries.Sum(d => (long)d.Symbols.Length)), _budget.Options.MaximumSymbols);
            _budget.Allocate((long)count * 16);
            var symbols = dictionaries.SelectMany(d => d.Symbols).ToList();
            if (symbols.Count > _budget.Options.MaximumSymbols)
            {
                throw new InvalidDataException("Too many JBIG2 input symbols.");
            }

            settings.IdBits = CodeBits(symbols.Count);
            if (huffman)
            {
                List<HuffmanTable>? custom = References<HuffmanTable>(segment);
                int index = 0;
                for (int i = 0; i < 7; i++)
                {
                    settings.Tables[i] = Select(custom, ref index, (huffFlags >> (2 * i)) & 3, i == 0 ? 6 : i == 1 ? 8 : i == 2 ? 11 : 14, i == 1 || i == 2);
                }

                if ((huffFlags & 16384) != 0)
                {
                    settings.Tables[7] = Select(custom, ref index, 3, 1);
                }

                settings.IdTable = HuffmanTable.SymbolCodes(input, symbols.Count, _budget);
            }

            Bitmap? bitmap = RenderText(region.Width, region.Height, new RegionCoding(input, huffman, _budget), symbols, settings, _budget.Contexts(template == 0 ? 8192 : 1024));
            Finish(segment, region, bitmap);
        }

        private Bitmap RenderText(int width, int height, RegionCoding coding, List<Bitmap> symbols, TextSettings settings, byte[] contexts)
        {
            Bitmap? result = new Bitmap(width, height, _budget, (settings.Flags >> 9) & 1);
            int decoded = 0, loops = 0;
            long stripT = -(long)coding.Number("IADT", settings.Tables[2]) * settings.Strips, firstS = 0;
            while (decoded < settings.Instances)
            {
                if (++loops > _budget.Options.MaximumInstances)
                {
                    throw new InvalidDataException("JBIG2 text strip limit exceeded.");
                }

                _budget.Cancellation.ThrowIfCancellationRequested();
                stripT += (long)coding.Number("IADT", settings.Tables[2]) * settings.Strips;
                firstS += coding.Number("IAFS", settings.Tables[0]);
                decoded = RenderStrip(result, coding, symbols, settings, contexts, stripT, firstS, decoded);
            }

            return result;
        }

        private int RenderStrip(Bitmap result, RegionCoding coding, List<Bitmap> symbols, TextSettings settings, byte[] contexts, long stripT, long currentS, int decoded)
        {
            while (decoded < settings.Instances)
            {
                int t = StripOffset(coding, settings);
                Bitmap symbol = TextSymbol(coding, symbols, settings, contexts);
                (long X, long Y) position = settings.Position(symbol, ref currentS, stripT + t);
                result.Compose(symbol, position.X, position.Y, (settings.Flags >> 7) & 3, _budget);
                decoded++;
                int? delta = coding.Read("IADS", settings.Tables[1]);
                if (delta is null)
                {
                    break;
                }

                currentS += (long)delta.Value + settings.Offset;
            }

            return decoded;
        }

        private static int StripOffset(RegionCoding coding, TextSettings settings)
        {
            int t = settings.Strips == 1 ? 0 : coding.Arithmetic is not null ? coding.Arithmetic.Required("IAIT") : (int)coding.Input.Bits(settings.LogStrips);
            if ((uint)t >= settings.Strips)
            {
                throw new InvalidDataException("JBIG2 symbol lies outside its strip.");
            }

            return t;
        }

        private Bitmap TextSymbol(RegionCoding coding, List<Bitmap> symbols, TextSettings settings, byte[] contexts)
        {
            int id = coding.Id(settings.IdBits, settings.IdTable);
            if ((uint)id >= symbols.Count)
            {
                throw new InvalidDataException("JBIG2 symbol ID is outside the dictionary.");
            }

            Bitmap symbol = symbols[id];
            if ((settings.Flags & 2) == 0)
            {
                return symbol;
            }

            int ri = coding.Arithmetic is not null ? coding.Arithmetic.Required("IARI") : coding.Input.Bit();
            return ri switch
            {
                0 => symbol,
                1 => RefineSymbol(coding, symbol, settings, contexts),
                _ => throw new InvalidDataException("Invalid JBIG2 refinement indicator.")
            };
        }

        private Bitmap RefineSymbol(RegionCoding coding, Bitmap symbol, TextSettings settings, byte[] contexts)
        {
            int dw = coding.Number("IARDW", settings.Tables[3]), dh = coding.Number("IARDH", settings.Tables[4]);
            int dx = checked((dw >> 1) + coding.Number("IARDX", settings.Tables[5])), dy = checked((dh >> 1) + coding.Number("IARDY", settings.Tables[6]));
            return BitmapDecoder.Refine(checked(symbol.Width + dw), checked(symbol.Height + dh), coding.Refinement(settings.Tables[7], _budget), settings.Template, settings.At, false, contexts, symbol, dx, dy, _budget);
        }
    }
}
