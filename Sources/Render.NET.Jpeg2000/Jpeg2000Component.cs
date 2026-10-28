using System;

namespace Render.NET.Jpeg2000
{
    /// <summary>A reconstructed component at its native sampling resolution.</summary>
    public class Jpeg2000Component
    {
        internal readonly int[] Values;
        internal int X0 { get; }
        internal int Y0 { get; }
        /// <summary>Gets the native width.</summary>
        public int Width { get; }
        /// <summary>Gets the native height.</summary>
        public int Height { get; }
        /// <summary>Gets the sample precision.</summary>
        public int BitDepth { get; }
        /// <summary>Gets whether samples are signed.</summary>
        public bool IsSigned { get; }
        /// <summary>Gets the horizontal sampling period on the reference grid.</summary>
        public int HorizontalSampling { get; }
        /// <summary>Gets the vertical sampling period on the reference grid.</summary>
        public int VerticalSampling { get; }
        /// <summary>Gets row-major reconstructed integer samples.</summary>
        public ReadOnlyMemory<int> Samples => Values;

        internal Jpeg2000Component(int x0, int y0, int width, int height, int bits, bool signed, int dx, int dy, int[] values)
        {
            X0 = x0;
            Y0 = y0;
            Width = width;
            Height = height;
            BitDepth = bits;
            IsSigned = signed;
            HorizontalSampling = dx;
            VerticalSampling = dy;
            Values = values;
        }
    }
}
