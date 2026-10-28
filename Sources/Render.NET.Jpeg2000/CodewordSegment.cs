using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class CodewordSegment
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
            foreach (ReadOnlyMemory<byte> part in Parts)
            {
                count = checked(count + part.Length);
            }

            budget.Reserve(count);
            byte[]? data = new byte[count];
            int offset = 0;
            foreach (ReadOnlyMemory<byte> part in Parts)
            {
                part.Span.CopyTo(data.AsSpan(offset));
                offset += part.Length;
            }

            return data;
        }
    }
}
