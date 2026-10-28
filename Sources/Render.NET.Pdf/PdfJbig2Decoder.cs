using System;
using System.IO;
using Render.NET.Jbig2;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal partial class PdfImageDecoder
    {
        private Memory<byte> DecodeJbig2(IPdfImage image, Memory<byte> bytes, int index)
        {
            ReadOnlyMemory<byte> globals = default;
            if (image.ImageDictionary.TryGet(NameToken.DecodeParms, out IToken entry))
            {
                if (DirectObjectFinder.TryGet<ArrayToken>(entry, _scanner, out ArrayToken? array) && index < array.Length)
                {
                    entry = array.Data[index];
                }

                if (DirectObjectFinder.TryGet<DictionaryToken>(entry, _scanner, out DictionaryToken? parameters) && parameters.TryGet(NameToken.Jbig2Globals, out IToken globalEntry) && DirectObjectFinder.TryGet<StreamToken>(globalEntry, _scanner, out StreamToken? stream))
                {
                    globals = stream.Decode(_filters, _scanner);
                }
            }

            Jbig2Document? document = Jbig2Decoder.Decode(bytes, globals, new Jbig2DecodeOptions { MaximumPixels = _limits.MaximumImagePixels, MaximumWorkingBytes = _limits.MaximumImageBytes, MaximumPages = 1, ExpectedWidth = image.WidthInSamples, ExpectedHeight = image.HeightInSamples }, _cancellationToken);
            if (document.Pages.Count != 1)
            {
                throw new InvalidDataException("PDF JBIG2 image must contain one page.");
            }

            byte[] samples = document.Pages[0].Data.ToArray();
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] ^= 255;
            }

            return samples;
        }
    }
}
