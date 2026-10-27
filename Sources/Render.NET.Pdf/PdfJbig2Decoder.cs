using System;
using System.Collections.Generic;
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
    }
}
