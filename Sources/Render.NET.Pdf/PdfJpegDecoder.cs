using System;
using System.IO;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal partial class PdfImageDecoder
    {
        private PixelBuffer DecodeJpeg(IPdfImage image, Memory<byte> bytes, int filterIndex)
        {
            if (image.BitsPerComponent != 8 || image.ColorSpaceDetails is null)
            {
                throw new NotSupportedException("JPEG sample depth or color space");
            }

            int components = image.ColorSpaceDetails.NumberOfColorComponents;
            int? transform = JpegColorTransform(image, filterIndex);
            byte[] samples = JpegSampleDecoder.Decode(bytes.ToArray(), image.WidthInSamples,
                image.HeightInSamples, components, transform, _cancellationToken);
            return DecodeSamples(image, samples, default);
        }

        private int? JpegColorTransform(IPdfImage image, int filterIndex)
        {
            if (!image.ImageDictionary.TryGet(NameToken.DecodeParms, out IToken entry))
            {
                return null;
            }

            if (DirectObjectFinder.TryGet<ArrayToken>(entry, _scanner, out ArrayToken? array))
            {
                if (filterIndex >= array.Length)
                {
                    throw new InvalidDataException("JPEG filter parameters do not match the filter array.");
                }

                entry = array.Data[filterIndex];
            }

            if (!DirectObjectFinder.TryGet<DictionaryToken>(entry, _scanner, out DictionaryToken? parameters)
                || !parameters.TryGet(NameToken.Create("ColorTransform"), out IToken value))
            {
                return null;
            }

            if (!DirectObjectFinder.TryGet<NumericToken>(value, _scanner, out NumericToken? number)
                || (number.Data != 0 && number.Data != 1))
            {
                throw new InvalidDataException("JPEG ColorTransform must be zero or one.");
            }

            return number.Int;
        }
    }
}
