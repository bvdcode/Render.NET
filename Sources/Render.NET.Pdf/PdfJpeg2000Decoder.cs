using System;
using Render.NET.Jpeg2000;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfImageDecoder
    {
        private PixelBuffer DecodeJpeg2000(IPdfImage image, Memory<byte> bytes)
        {
            var decoded = Jpeg2000Decoder.Decode(bytes, new Jpeg2000DecodeOptions { MaximumPixels = _limits.MaximumImagePixels, ExpectedWidth = image.WidthInSamples, ExpectedHeight = image.HeightInSamples }, _cancellationToken);
            var layout = Jpeg2000Layout(image, decoded);
            var result = new PixelBuffer(decoded.Width, decoded.Height);
            var values = new double[layout.Components];
            var raw = new int[layout.Components];
            for (int y = 0; y < result.Height; y++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < result.Width; x++)
                {
                    result[x, y] = Jpeg2000Pixel(image, decoded, layout, x, y, values, raw);
                }
            }

            return result;
        }

        private static (int Components, int Alpha, int Mask) Jpeg2000Layout(IPdfImage image, Jpeg2000Image decoded)
        {
            int alpha = decoded.AlphaComponent;
            int mask = 0;
            if (image.ImageDictionary.TryGet<NumericToken>(NameToken.Create("SMaskInData"), out var token))
            {
                mask = token.Int;
            }

            if (mask < 0 || mask > 2)
            {
                throw new NotSupportedException("JPEG 2000 embedded mask mode");
            }

            if (mask != 0 && alpha < 0)
            {
                alpha = decoded.ComponentCount - 1;
            }

            int components = image.ColorSpaceDetails?.NumberOfColorComponents ?? decoded.ComponentCount - (alpha >= 0 ? 1 : 0);
            if (components < 1 || decoded.ComponentCount != components + (alpha >= 0 ? 1 : 0))
            {
                throw new NotSupportedException("JPEG 2000 component layout");
            }

            ValidateJpeg2000ColorSpace(image, decoded, components);
            return (components, alpha, mask);
        }

        private static void ValidateJpeg2000ColorSpace(IPdfImage image, Jpeg2000Image decoded, int components)
        {
            if (image.ColorSpaceDetails == null && (decoded.HasIccProfile || (components != 1 && components != 3)))
            {
                throw new NotSupportedException("JPEG 2000 embedded colour profile or component layout");
            }
        }

        private static Rgba32 Jpeg2000Pixel(IPdfImage image, Jpeg2000Image decoded, (int Components, int Alpha, int Mask) layout, int x, int y, double[] values, int[] raw)
        {
            for (int c = 0; c < values.Length; c++)
            {
                int index = layout.Alpha >= 0 && c >= layout.Alpha ? c + 1 : c;
                raw[c] = decoded.GetSample(x, y, index);
                values[c] = Jpeg2000Sample(image.ColorSpaceDetails, c, raw[c], decoded.GetComponent(index));
                if (image.ColorSpaceDetails == null && decoded.ColorSpace == Jpeg2000ColorSpace.Sycc && c > 0)
                {
                    int bits = decoded.GetComponent(index).BitDepth;
                    values[c] -= (1 << (bits - 1)) / ((1 << bits) - 1.0);
                }
            }

            double opacity = layout.Mask == 0 ? 1 : Normalized(decoded.GetSample(x, y, layout.Alpha), decoded.GetComponent(layout.Alpha));
            if (layout.Mask == 2 && opacity > 0)
            {
                for (int c = 0; c < values.Length; c++)
                {
                    values[c] /= opacity;
                }
            }

            if (image.ImageDictionary.TryGet<ArrayToken>(NameToken.Mask, out var key) && MatchesColorKey(key, raw))
            {
                opacity = 0;
            }

            if (image.ColorSpaceDetails != null)
            {
                return PdfStreamProcessor.Color(image.ColorSpaceDetails.GetColor(values), opacity);
            }

            return Jpeg2000EmbeddedColor(values, decoded.ColorSpace, opacity);
        }

        private static double Normalized(int sample, Jpeg2000Component component)
        {
            double value = sample + (component.IsSigned ? 1 << (component.BitDepth - 1) : 0);
            return value / ((1 << component.BitDepth) - 1.0);
        }

        private static double Jpeg2000Sample(ColorSpaceDetails? space, int channel, int sample, Jpeg2000Component component)
        {
            if (space?.Type == ColorSpace.Indexed)
            {
                return sample;
            }

            double value = Normalized(sample, component);
            if (space is not LabColorSpaceDetails lab)
            {
                return value;
            }

            double low = channel == 0 ? 0 : lab.Matrix[(channel - 1) * 2];
            double high = channel == 0 ? 100 : lab.Matrix[((channel - 1) * 2) + 1];
            return low + (value * (high - low));
        }

        private static Rgba32 Jpeg2000EmbeddedColor(double[] values, Jpeg2000ColorSpace space, double opacity)
        {
            double r = values[0];
            double g = values[values.Length == 1 ? 0 : 1];
            double b = values[values.Length == 1 ? 0 : 2];
            if (space == Jpeg2000ColorSpace.Sycc && values.Length == 3)
            {
                double cb = values[1];
                double cr = values[2];
                g = r - (0.344136 * cb) - (0.714136 * cr);
                b = r + (1.772 * cb);
                r += 1.402 * cr;
            }

            return new Rgba32(Jpeg2000Byte(r), Jpeg2000Byte(g), Jpeg2000Byte(b), Jpeg2000Byte(opacity));
        }

        private static byte Jpeg2000Byte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    }
}
