using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jbig2
{
    internal sealed record HuffmanRow(int Length, int Range, int Base, bool Lower = false);
    internal sealed class HuffmanTable
    {
        private readonly Dictionary<ulong, HuffmanRow> _codes = new();
        internal HuffmanTable(IReadOnlyList<HuffmanRow> rows)
        {
            ulong code = 0;
            for (int length = 1; length <= 32; length++)
            {
                foreach (var row in rows)
                {
                    if (row.Length != length)
                    {
                        continue;
                    }

                    if (code >= (1UL << length))
                    {
                        throw new InvalidDataException("Oversubscribed JBIG2 Huffman table.");
                    }

                    _codes.Add(((ulong)length << 32) | code++, row);
                }

                code <<= 1;
            }
        }

        internal int? Read(BitReader input)
        {
            uint code = 0;
            for (int length = 1; length <= 32; length++)
            {
                code = (code << 1) | (uint)input.Bit();
                if (!_codes.TryGetValue(((ulong)length << 32) | code, out var row))
                {
                    continue;
                }

                if (row.Range < 0)
                {
                    return null;
                }

                long extra = input.Bits(row.Range), value = row.Base + (row.Lower ? -extra : extra);
                if (value < int.MinValue || value > int.MaxValue)
                {
                    throw new InvalidDataException("JBIG2 Huffman integer overflow.");
                }

                return (int)value;
            }

            throw new InvalidDataException("Invalid JBIG2 Huffman code.");
        }

        internal int Required(BitReader input) => Read(input) ?? throw new InvalidDataException("Unexpected JBIG2 Huffman OOB.");
        internal static HuffmanTable Custom(BitReader input, DecodeBudget budget)
        {
            int flags = input.Byte(), prefixBits = ((flags >> 1) & 7) + 1, rangeBits = ((flags >> 4) & 7) + 1;
            long low = input.I32(), high = input.I32();
            int lowerBase = checked((int)low - 1);
            var rows = new List<HuffmanRow>();
            if (low >= high || (flags & 128) != 0)
            {
                throw new InvalidDataException("Invalid JBIG2 custom table limits.");
            }

            while (low < high)
            {
                if (rows.Count >= 65536)
                {
                    throw new InvalidDataException("JBIG2 custom table is too large.");
                }

                int prefix = (int)input.Bits(prefixBits), range = (int)input.Bits(rangeBits);
                if (prefix > 32 || range > 32)
                {
                    throw new InvalidDataException("Invalid JBIG2 Huffman length.");
                }

                budget.Allocate(128);
                rows.Add(new HuffmanRow(prefix, range, (int)low));
                low += 1L << range;
            }

            int lower = lowerBase;
            return CustomTail(input, rows, prefixBits, flags, lower, high);
        }

        private static HuffmanTable CustomTail(BitReader input, List<HuffmanRow> rows, int prefixBits, int flags, int low, long high)
        {
            rows.Add(new HuffmanRow((int)input.Bits(prefixBits), 32, low, true));
            rows.Add(new HuffmanRow((int)input.Bits(prefixBits), 32, checked((int)high)));
            if ((flags & 1) != 0)
            {
                rows.Add(new HuffmanRow((int)input.Bits(prefixBits), -1, 0));
            }

            if (rows.Exists(r => r.Length > 32))
            {
                throw new InvalidDataException("Invalid JBIG2 prefix length.");
            }

            return new HuffmanTable(rows);
        }

        internal static HuffmanTable SymbolCodes(BitReader input, int count, DecodeBudget budget)
        {
            var runRows = new HuffmanRow[35];
            for (int i = 0; i < runRows.Length; i++)
            {
                runRows[i] = new HuffmanRow((int)input.Bits(4), 0, i);
            }

            var runs = new HuffmanTable(runRows);
            var rows = new HuffmanRow[count];
            int index = 0, previous = 0;
            budget.Allocate((long)count * 128);
            while (index < count)
            {
                int code = runs.Required(input), length = code, repeat = 1;
                if (code == 32)
                {
                    if (index == 0)
                    {
                        throw new InvalidDataException("Invalid JBIG2 symbol code repeat.");
                    }

                    repeat = 3 + (int)input.Bits(2);
                    length = previous;
                }

                if (code == 33)
                {
                    repeat = 3 + (int)input.Bits(3);
                    length = 0;
                }

                if (code == 34)
                {
                    repeat = 11 + (int)input.Bits(7);
                    length = 0;
                }

                if (repeat > count - index)
                {
                    throw new InvalidDataException("JBIG2 symbol code run exceeds dictionary.");
                }

                while (repeat-- > 0)
                {
                    rows[index] = new HuffmanRow(length, 0, index);
                    index++;
                }

                previous = length;
            }

            input.Align();
            return new HuffmanTable(rows);
        }
    }
}
