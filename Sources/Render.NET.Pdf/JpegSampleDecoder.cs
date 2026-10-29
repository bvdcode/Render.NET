using System;
using System.IO;
using System.Threading;
using StbImageSharp;

namespace Render.NET.Pdf
{
    internal static class JpegSampleDecoder
    {
        internal static unsafe byte[] Decode(byte[] encoded, int width, int height, int components,
            int? colorTransform, CancellationToken cancellationToken)
        {
            using MemoryStream input = new MemoryStream(encoded, false);
            ImageInfo? info = ImageInfo.FromStream(input);
            if (info is null || info.Value.Width != width || info.Value.Height != height)
            {
                throw new InvalidDataException("JPEG dimensions do not match the PDF image dictionary.");
            }

            input.Position = 0;
            StbImage.stbi__jpeg jpeg = new StbImage.stbi__jpeg { s = new StbImage.stbi__context(input) };
            StbImage.stbi__setup_jpeg(jpeg);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (StbImage.stbi__decode_jpeg_image(jpeg) == 0)
                {
                    throw new InvalidDataException("Invalid JPEG image: " + StbImage.stbi__g_failure_reason);
                }

                if (jpeg.s.img_n != components)
                {
                    throw new InvalidDataException("JPEG component count does not match the PDF color space.");
                }

                bool convert = Transform(jpeg.app14_color_transform, components, colorTransform);
                return ReadSamples(jpeg, width, height, components, convert, cancellationToken);
            }
            finally
            {
                StbImage.stbi__cleanup_jpeg(jpeg);
            }
        }

        private static bool Transform(int marker, int components, int? parameter)
        {
            if (components == 1)
            {
                return false;
            }

            return marker switch
            {
                -1 => (parameter ?? (components == 3 ? 1 : 0)) == 1,
                0 => false,
                1 when components == 3 => true,
                2 when components == 4 => true,
                _ => throw new NotSupportedException("JPEG Adobe color transform " + marker)
            };
        }

        private static unsafe byte[] ReadSamples(StbImage.stbi__jpeg jpeg, int width, int height,
            int components, bool convert, CancellationToken cancellationToken)
        {
            JpegComponentResampler[] planes = new JpegComponentResampler[components];
            for (int c = 0; c < components; c++)
            {
                planes[c] = new JpegComponentResampler(jpeg, c, width);
            }

            byte[] samples = new byte[checked(width * height * components)];
            byte[] converted = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int c = 0; c < components; c++)
                {
                    planes[c].ReadRow(y);
                }

                if (convert)
                {
                    fixed (byte* output = converted, first = planes[0].Row, second = planes[1].Row, third = planes[2].Row)
                    {
                        StbImage.stbi__YCbCr_to_RGB_row(output, first, second, third, width, 4);
                    }
                }

                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < components; c++)
                    {
                        int value = convert && c < 3 ? converted[(x * 4) + c] : planes[c].Row[x];
                        samples[(((y * width) + x) * components) + c] = (byte)(convert && components == 4 && c < 3 ? 255 - value : value);
                    }
                }
            }

            return samples;
        }
    }
}
