using System;
using System.Collections.Generic;
using System.IO;
using Render.NET.Entropy;

namespace Render.NET.Jbig2
{
    // ITU-T T.88 Annex A. Each integer class owns an independent set of contexts.
    internal class Arithmetic
    {
        private readonly Dictionary<string, byte[]> _integers = new();
        private readonly DecodeBudget _budget;
        private byte[]? _ids;
        internal Arithmetic(ReadOnlyMemory<byte> data, DecodeBudget budget)
        {
            if (data.Length < 2 || data.Span[^2] != 255 || data.Span[^1] != 0xac)
            {
                throw new InvalidDataException("JBIG2 arithmetic stream is missing its terminating marker.");
            }

            budget.Allocate(data.Length);
            Decoder = new MqDecoder(data.ToArray(), Array.Empty<byte>());
            _budget = budget;
        }

        internal MqDecoder Decoder { get; }

        internal int? Integer(string name)
        {
            if (!_integers.TryGetValue(name, out byte[]? contexts))
            {
                contexts = _budget.Contexts(512);
                _integers.Add(name, contexts);
            }

            int previous = 1;
            int Read()
            {
                int bit = Decoder.Read(contexts, previous);
                previous = previous < 256 ? (previous << 1) | bit : (((previous << 1) | bit) & 511) | 256;
                return bit;
            }

            int sign = Read(), count = 2, offset = 0;
            if (Read() != 0)
            {
                count = 4;
                offset = 4;
                if (Read() != 0)
                {
                    count = 6;
                    offset = 20;
                    if (Read() != 0)
                    {
                        count = 8;
                        offset = 84;
                        if (Read() != 0)
                        {
                            count = 12;
                            offset = 340;
                            if (Read() != 0)
                            {
                                count = 32;
                                offset = 4436;
                            }
                        }
                    }
                }
            }

            long value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | (uint)Read();
            }

            value += offset;
            if (sign != 0 && value == 0)
            {
                return null;
            }

            if (sign != 0)
            {
                value = -value;
            }

            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new InvalidDataException("JBIG2 integer overflow.");
            }

            return (int)value;
        }

        internal int Required(string name) => Integer(name) ?? throw new InvalidDataException("Unexpected JBIG2 OOB integer.");
        internal int Id(int bits)
        {
            _ids ??= _budget.Contexts(1 << bits);
            int previous = 1;
            for (int i = 0; i < bits; i++)
            {
                previous = (previous << 1) | Decoder.Read(_ids, previous);
            }

            return previous - (1 << bits);
        }
    }
}
