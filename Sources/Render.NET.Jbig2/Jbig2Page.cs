using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Render.NET.Jbig2
{
    /// <summary>A monochrome page. Set bits represent black pixels; rows are byte aligned.</summary>
    public class Jbig2Page
    {
        private readonly Bitmap _bitmap;
        internal Jbig2Page(uint number, Bitmap bitmap)
        {
            Number = number;
            _bitmap = bitmap;
        }

        /// <summary>The page association number in the stream.</summary>
        public uint Number { get; }
        /// <summary>Width in pixels.</summary>
        public int Width => _bitmap.Width;
        /// <summary>Height in pixels.</summary>
        public int Height => _bitmap.Height;
        /// <summary>Bytes per row.</summary>
        public int Stride => _bitmap.Stride;
        /// <summary>MSB-first packed pixels. Unused low bits in the final byte are zero.</summary>
        public ReadOnlyMemory<byte> Data => _bitmap.Data;

        /// <summary>Returns whether a pixel is black.</summary>
        public bool GetPixel(int x, int y)
        {
            if ((uint)x >= Width || (uint)y >= Height)
            {
                throw new ArgumentOutOfRangeException();
            }

            return _bitmap.Get(x, y) != 0;
        }
    }
}
