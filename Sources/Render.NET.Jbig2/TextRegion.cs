using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Render.NET.Jbig2
{
    internal class TextSettings
    {
        internal TextSettings(int flags, int template, (int, int)[] at, int instances)
        {
            Flags = flags;
            Template = template;
            At = at;
            Instances = instances;
        }

        internal int LogStrips => (Flags >> 2) & 3;
        internal int Strips => 1 << LogStrips;
        internal int Offset => ((Flags >> 10) & 15) - ((Flags >> 10) & 16);

        internal (long X, long Y) Position(Bitmap symbol, ref long currentS, long t)
        {
            bool transpose = (Flags & 64) != 0, right = (Flags & 32) != 0, bottom = (Flags & 16) == 0;
            int advance = (transpose ? symbol.Height : symbol.Width) - 1;
            bool advanceFirst = transpose ? bottom : right;
            if (advanceFirst)
            {
                currentS += advance;
            }

            long x = transpose ? t : currentS, y = transpose ? currentS : t;
            if (right)
            {
                x -= symbol.Width - 1;
            }

            if (bottom)
            {
                y -= symbol.Height - 1;
            }

            if (!advanceFirst)
            {
                currentS += advance;
            }

            return (x, y);
        }

        internal int Flags { get; }
        internal int Template { get; }
        internal (int, int)[] At { get; }
        internal int Instances { get; }
        internal int IdBits { get; set; }
        internal HuffmanTable? IdTable { get; set; }
        internal HuffmanTable[] Tables { get; } =
        {
            StandardHuffman.Tables[6],
            StandardHuffman.Tables[8],
            StandardHuffman.Tables[11],
            StandardHuffman.Tables[14],
            StandardHuffman.Tables[14],
            StandardHuffman.Tables[14],
            StandardHuffman.Tables[14],
            StandardHuffman.Tables[1]
        };
    }
}
