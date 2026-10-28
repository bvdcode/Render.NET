using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal class RegionCoding
    {
        internal RegionCoding(BitReader input, bool huffman, DecodeBudget budget)
        {
            Input = input;
            if (!huffman)
            {
                Arithmetic = new Arithmetic(input.Rest(), budget);
            }
        }

        internal BitReader Input { get; }
        internal Arithmetic? Arithmetic { get; }

        internal int? Read(string name, HuffmanTable table) => Arithmetic is not null ? Arithmetic.Integer(name) : table.Read(Input);
        internal int Number(string name, HuffmanTable table) => Read(name, table) ?? throw new InvalidDataException("Unexpected JBIG2 out-of-band value.");
        internal int Id(int bits, HuffmanTable? table = null) => Arithmetic is not null ? Arithmetic.Id(bits) : table?.Required(Input) ?? (int)Input.Bits(bits);
        internal Arithmetic Refinement(HuffmanTable size, DecodeBudget budget)
        {
            if (Arithmetic is not null)
            {
                return Arithmetic;
            }

            int length = size.Required(Input);
            Input.Align();
            return new Arithmetic(Input.Take(length), budget);
        }
    }
}
