using System;
using System.Collections.Generic;
using System.IO;
using CoreJ2K;
using CoreJ2K.Configuration;
using CoreJ2K.Util;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Filters.Jbig2.PdfboxJbig2;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfImageDecoder
    {
        private Memory<byte> DecodeJbig2(IPdfImage image, Memory<byte> bytes, int index)
        {
            Jbig2Dimensions.Validate(bytes.Span, image.WidthInSamples, image.HeightInSamples, _limits.MaximumImagePixels);
            DictionaryToken dictionary = image.ImageDictionary;
            IToken? parameters = null;
            if (dictionary.TryGet(NameToken.DecodeParms, out IToken entry))
            {
                if (DirectObjectFinder.TryGet<ArrayToken>(entry, _scanner, out var array) && index < array.Length)
                {
                    parameters = array.Data[index];
                }
                else
                {
                    parameters = entry;
                }
            }

            var values = new Dictionary<NameToken, IToken>();
            if (parameters != null && DirectObjectFinder.TryGet<DictionaryToken>(parameters, _scanner, out var resolved))
            {
                foreach (var pair in resolved.Data)
                {
                    values[NameToken.Create(pair.Key)] = pair.Value;
                }

                if (resolved.TryGet(NameToken.Jbig2Globals, out IToken globals)
                    && DirectObjectFinder.TryGet<StreamToken>(globals, _scanner, out var stream))
                {
                    values[NameToken.Jbig2Globals] = stream;
                }
            }

            var filterDictionary = new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                [NameToken.Filter] = NameToken.Jbig2Decode,
                [NameToken.DecodeParms] = new DictionaryToken(values)
            });
            return new PdfboxJbig2DecodeFilter().Decode(bytes, filterDictionary, _filters, 0);
        }

        private PixelBuffer DecodeJpeg2000(IPdfImage image, Memory<byte> bytes)
        {
            var configuration = new J2KDecoderConfiguration { Verbose = false, UseColorSpace = image.ColorSpaceDetails == null };
            configuration.WithCancellationToken(_cancellationToken).WithMaxDegreeOfParallelism(1)
                .WithLimits(DecoderLimits.Strict.WithMaxPixels(_limits.MaximumImagePixels));
            using var decoded = J2kImage.FromBytes(bytes.ToArray(), configuration);
            if (decoded.Width != image.WidthInSamples || decoded.Height != image.HeightInSamples)
            {
                throw new InvalidDataException("JPEG 2000 dimensions do not match the PDF image dictionary.");
            }

            (int components, int alpha) = Jpeg2000Layout(image, decoded.NumberOfComponents);
            var result = new PixelBuffer(decoded.Width, decoded.Height);
            var values = new double[components];
            var raw = new int[components];
            for (int y = 0; y < result.Height; y++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < result.Width; x++)
                {
                    result[x, y] = Jpeg2000Color(image, decoded, x, y, values, raw, alpha);
                }
            }

            return result;
        }

        private static (int Components, int Alpha) Jpeg2000Layout(IPdfImage image, int decodedComponents)
        {
            int components = image.ColorSpaceDetails?.NumberOfColorComponents ?? decodedComponents;
            int alpha = 0;
            if (image.ImageDictionary.TryGet<NumericToken>(NameToken.Create("SMaskInData"), out var mask))
            {
                alpha = mask.Int;
                components = decodedComponents - (alpha == 0 ? 0 : 1);
            }

            if (components < 1 || components > 4 || decodedComponents != components + (alpha == 0 ? 0 : 1))
            {
                throw new NotSupportedException("JPEG 2000 component layout");
            }

            if (image.ColorSpaceDetails == null && components != 1 && components != 3)
            {
                throw new NotSupportedException("JPEG 2000 embedded color space");
            }

            return (components, alpha);
        }

        private static Rgba32 Jpeg2000Color(IPdfImage image, InterleavedImage decoded, int x, int y, double[] values, int[] raw, int alpha)
        {
            int components = values.Length;
            for (int c = 0; c < components; c++)
            {
                raw[c] = decoded.GetSample(x, y, c);
                values[c] = MapJpeg2000Sample(image.ColorSpaceDetails, c, raw[c], decoded.GetBitDepth(c));
            }

            double opacity = alpha == 0 ? 1 : decoded.GetSample(x, y, components) / (Math.Pow(2, decoded.GetBitDepth(components)) - 1);
            if (alpha == 2 && opacity > 0)
            {
                for (int c = 0; c < components; c++)
                {
                    values[c] /= opacity;
                }
            }

            if (image.ImageDictionary.TryGet<ArrayToken>(NameToken.Mask, out var mask) && MatchesColorKey(mask, raw))
            {
                opacity = 0;
            }

            return image.ColorSpaceDetails != null ? PdfStreamProcessor.Color(image.ColorSpaceDetails.GetColor(values), opacity)
                : new Rgba32(Byte(values[0]), Byte(values[components == 1 ? 0 : 1]), Byte(values[components == 1 ? 0 : 2]), Byte(opacity));
        }

        private static double MapJpeg2000Sample(ColorSpaceDetails? space, int component, int sample, int bits)
        {
            if (space?.Type == ColorSpace.Indexed)
            {
                return sample;
            }

            double value = sample / (Math.Pow(2, bits) - 1);
            if (space is LabColorSpaceDetails lab)
            {
                double low = component == 0 ? 0 : lab.Matrix[(component - 1) * 2];
                double high = component == 0 ? 100 : lab.Matrix[((component - 1) * 2) + 1];
                return low + (value * (high - low));
            }

            return value;
        }

        private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    }
}
