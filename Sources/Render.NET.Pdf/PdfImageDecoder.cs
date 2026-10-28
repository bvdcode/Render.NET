using System;
using System.IO;
using System.Threading;
using StbImageSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal partial class PdfImageDecoder
    {
        private readonly PdfReaderOptions _limits;
        private readonly ILookupFilterProvider _filters;
        private readonly IPdfTokenScanner _scanner;
        private readonly CancellationToken _cancellationToken;
        private long _imageBytes;

        internal PdfImageDecoder(PdfReaderOptions limits, ILookupFilterProvider filters, IPdfTokenScanner scanner, CancellationToken cancellationToken)
        {
            _limits = limits;
            _filters = filters;
            _scanner = scanner;
            _cancellationToken = cancellationToken;
        }

        internal PixelBuffer Decode(IPdfImage image, Rgba32 stencilColor)
        {
            CheckSize(image.WidthInSamples, image.HeightInSamples);
            Memory<byte> bytes = image.RawMemory;
            bool jpeg = false;
            System.Collections.Generic.IReadOnlyList<IFilter>? filters = _filters.GetFilters(image.ImageDictionary, _scanner);
            for (int i = 0; i < filters.Count; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                IFilter filter = filters[i];
                if (filter is DctDecodeFilter)
                {
                    jpeg = true;
                    break;
                }

                if (filter is JpxDecodeFilter)
                {
                    PixelBuffer decoded = DecodeJpeg2000(image, bytes);
                    ApplyMask(decoded, image.MaskImage);
                    return decoded;
                }

                if (filter is Jbig2DecodeFilter)
                {
                    bytes = DecodeJbig2(image, bytes, i);
                    continue;
                }

                if (!filter.IsSupported)
                {
                    throw new NotSupportedException("Image filter " + filter.GetType().Name);
                }

                bytes = filter.Decode(bytes, image.ImageDictionary, _filters, i);
            }

            PixelBuffer result = jpeg ? DecodeJpeg(image, bytes) : DecodeSamples(image, bytes.Span, stencilColor);
            ApplyMask(result, image.MaskImage);
            return result;
        }

        private void CheckSize(int width, int height)
        {
            long pixels = (long)width * height;
            if (width <= 0 || height <= 0 || pixels > _limits.MaximumImagePixels)
            {
                throw new InvalidDataException("PDF image exceeds MaximumImagePixels or has invalid dimensions.");
            }

            _imageBytes += pixels * 4;
            if (_imageBytes > _limits.MaximumImageBytes)
            {
                throw new InvalidDataException("PDF page exceeds MaximumImageBytes.");
            }
        }

        private PixelBuffer DecodeJpeg(IPdfImage image, Memory<byte> bytes)
        {
            byte[] encoded = bytes.ToArray();
            using MemoryStream? input = new MemoryStream(encoded, false);
            ImageInfo? info = ImageInfo.FromStream(input);
            if (info is null || info.Value.Width != image.WidthInSamples || info.Value.Height != image.HeightInSamples)
            {
                throw new InvalidDataException("JPEG dimensions do not match the PDF image dictionary.");
            }

            if (image.Decode.Count != 0 || image.ImageDictionary.TryGet<ArrayToken>(NameToken.Mask, out _))
            {
                throw new NotSupportedException("JPEG Decode arrays and color-key masks");
            }

            ImageResult decoded = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
            PixelBuffer? result = new PixelBuffer(decoded.Width, decoded.Height);
            decoded.Data.CopyTo(result.Pixels.Span);
            return result;
        }

        private PixelBuffer DecodeSamples(IPdfImage image, ReadOnlySpan<byte> bytes, Rgba32 stencilColor)
        {
            int bits = image.BitsPerComponent;
            if (bits != 1 && bits != 2 && bits != 4 && bits != 8 && bits != 16)
            {
                throw new NotSupportedException("Image sample depth " + bits);
            }

            int components = image.IsImageMask ? 1 : image.ColorSpaceDetails?.NumberOfColorComponents ?? 0;
            if (components <= 0 || components > 32)
            {
                throw new NotSupportedException("Image color space");
            }

            int rowBytes = checked((int)((((long)image.WidthInSamples * components * bits) + 7) / 8));
            if ((long)rowBytes * image.HeightInSamples > bytes.Length)
            {
                throw new InvalidDataException("The PDF image sample data is truncated.");
            }

            PixelBuffer? output = new PixelBuffer(image.WidthInSamples, image.HeightInSamples);
            double[]? values = new double[components];
            int[]? raw = new int[components];
            for (int y = 0; y < output.Height; y++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> row = bytes.Slice(y * rowBytes, rowBytes);
                for (int x = 0; x < output.Width; x++)
                {
                    ReadComponents(image, row, x, bits, values, raw);
                    output[x, y] = SampleColor(image, values, raw, stencilColor);
                }
            }

            return output;
        }

        private static void ReadComponents(IPdfImage image, ReadOnlySpan<byte> row, int x, int bits, double[] values, int[] raw)
        {
            double maximum = (1 << bits) - 1;
            for (int c = 0; c < values.Length; c++)
            {
                int bit = ((x * values.Length) + c) * bits;
                int sample = bits == 16 ? (row[bit / 8] << 8) | row[(bit / 8) + 1]
                    : (row[bit / 8] >> (8 - bits - (bit % 8))) & (int)maximum;
                raw[c] = sample;
                double minimum = 0;
                double upper = image.ColorSpaceDetails?.Type == ColorSpace.Indexed ? maximum : 1;
                if (image.ColorSpaceDetails is LabColorSpaceDetails lab)
                {
                    minimum = c == 0 ? 0 : lab.Matrix[(c - 1) * 2];
                    upper = c == 0 ? 100 : lab.Matrix[((c - 1) * 2) + 1];
                }
                if (image.Decode.Count >= (c + 1) * 2)
                {
                    minimum = image.Decode[c * 2];
                    upper = image.Decode[(c * 2) + 1];
                }

                values[c] = minimum + ((sample / maximum) * (upper - minimum));
            }
        }

        private static Rgba32 SampleColor(IPdfImage image, double[] values, int[] raw, Rgba32 stencilColor)
        {
            if (image.IsImageMask)
            {
                return stencilColor with { A = (byte)Math.Clamp((int)Math.Round((1 - values[0]) * 255), 0, 255) };
            }

            IColor? rgb = image.ColorSpaceDetails!.GetColor(values);
            Rgba32 result = PdfStreamProcessor.Color(rgb);
            if (image.ImageDictionary.TryGet<ArrayToken>(NameToken.Mask, out ArrayToken? mask) && MatchesColorKey(mask, raw))
            {
                return result with { A = 0 };
            }

            return result;
        }

        private static bool MatchesColorKey(ArrayToken mask, int[] samples)
        {
            if (mask.Length != samples.Length * 2)
            {
                return false;
            }

            for (int c = 0; c < samples.Length; c++)
            {
                if (mask.Data[c * 2] is not NumericToken low || mask.Data[(c * 2) + 1] is not NumericToken high
                    || samples[c] < low.Int || samples[c] > high.Int)
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyMask(PixelBuffer image, IPdfImage? mask)
        {
            if (mask is null)
            {
                return;
            }

            PixelBuffer decoded = Decode(mask, new Rgba32(0, 0, 0));
            for (int y = 0; y < image.Height; y++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < image.Width; x++)
                {
                    Rgba32 sample = decoded[(int)(((long)x * decoded.Width) / image.Width), (int)(((long)y * decoded.Height) / image.Height)];
                    byte alpha = mask.IsImageMask ? sample.A : sample.R;
                    Rgba32 original = image[x, y];
                    image[x, y] = original with { A = (byte)((original.A * alpha) / 255) };
                }
            }
        }
    }
}
