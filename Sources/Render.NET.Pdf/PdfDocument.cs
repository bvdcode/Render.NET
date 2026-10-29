using System;
using System.Threading;
using UglyToad.PdfPig;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Render.NET.Pdf
{
    /// <summary>An open PDF document. Access to its parser is serialized; detached pages can render concurrently.</summary>
    public class PdfDocument : IDisposable
    {
        private readonly PigDocument _document;
        private readonly RenderParsingOptions _options;
        private readonly object _sync = new object();
        private bool _disposed;

        /// <summary>Gets the number of pages.</summary>
        public int PageCount { get; }

        internal PdfDocument(byte[] data, PdfReaderOptions options)
        {
            _options = new RenderParsingOptions(options);
            _document = PigDocument.Open(data, _options);
            _options.OptionalContent = new PdfOptionalContent(_document.Structure.Catalog.CatalogDictionary, _document.Structure.TokenScanner);
            _document.AddPageFactory<PdfPage, PdfPageFactory>();
            PageCount = _document.NumberOfPages;
        }

        /// <summary>Interprets one page, preserving graphics operation order and copying all required drawing data.</summary>
        public PdfPage GetPage(int pageNumber, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, PageCount);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                _options.CancellationToken = cancellationToken;
                try
                {
                    return _document.GetPage<PdfPage>(pageNumber);
                }
                finally
                {
                    _options.CancellationToken = default;
                }
            }
        }

        /// <summary>Closes the parser. Already interpreted pages remain usable.</summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    _document.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}
