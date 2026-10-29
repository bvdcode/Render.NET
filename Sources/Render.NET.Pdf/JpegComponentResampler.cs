using System;
using StbImageSharp;

namespace Render.NET.Pdf
{
    internal unsafe class JpegComponentResampler
    {
        private readonly StbImage.stbi__jpeg.unnamed1 _plane;
        private readonly int _horizontal;
        private readonly int _vertical;
        private readonly int _sampleWidth;
        private readonly int _width;

        internal byte[] Row { get; }

        internal JpegComponentResampler(StbImage.stbi__jpeg jpeg, int component, int width)
        {
            _plane = jpeg.img_comp[component];
            _horizontal = jpeg.img_h_max / _plane.h;
            _vertical = jpeg.img_v_max / _plane.v;
            _sampleWidth = (width + _horizontal - 1) / _horizontal;
            _width = width;
            Row = new byte[width + 3];
        }

        internal void ReadRow(int y)
        {
            int position = y + (_vertical / 2);
            int nearest = position / _vertical;
            int other = nearest + (position % _vertical >= _vertical / 2 ? -1 : 1);
            byte* near = _plane.data + (Math.Clamp(nearest, 0, _plane.y - 1) * _plane.w2);
            byte* far = _plane.data + (Math.Clamp(other, 0, _plane.y - 1) * _plane.w2);
            fixed (byte* row = Row)
            {
                byte* source = (_horizontal, _vertical) switch
                {
                    (1, 1) => StbImage.resample_row_1(row, near, far, _sampleWidth, _horizontal),
                    (1, 2) => StbImage.stbi__resample_row_v_2(row, near, far, _sampleWidth, _horizontal),
                    (2, 1) => StbImage.stbi__resample_row_h_2(row, near, far, _sampleWidth, _horizontal),
                    (2, 2) => StbImage.stbi__resample_row_hv_2(row, near, far, _sampleWidth, _horizontal),
                    _ => StbImage.stbi__resample_row_generic(row, near, far, _sampleWidth, _horizontal)
                };
                if (source != row)
                {
                    new ReadOnlySpan<byte>(source, _width).CopyTo(Row);
                }
            }
        }
    }
}
