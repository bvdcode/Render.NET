using System;
using System.Threading;
using UglyToad.PdfPig;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Render.NET.Pdf
{
    internal class RenderParsingOptions : ParsingOptions
    {
        internal PdfReaderOptions Limits { get; }
        internal CancellationToken CancellationToken { get; set; }
        internal PdfOptionalContent OptionalContent { get; set; } = null!;

        internal RenderParsingOptions(PdfReaderOptions options)
        {
            Limits = options;
            Password = options.Password;
            UseLenientParsing = false;
            ClipPaths = false;
        }
    }
}
