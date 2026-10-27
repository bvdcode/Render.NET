using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Pdf
{
    /// <summary>Reads bounded PDF input without native libraries. Input streams remain open.</summary>
    public static class PdfReader
    {
        /// <summary>Opens a PDF file. Page numbers start at one.</summary>
        public static async Task<PdfDocument> ReadFileAsync(string path, PdfReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            await using var input = File.OpenRead(path);
            return await ReadAsync(input, options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reads a PDF from the stream's current position, including non-seekable streams.</summary>
        public static async Task<PdfDocument> ReadAsync(Stream input, PdfReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            options ??= new PdfReaderOptions();
            Validate(options);
            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            while (true)
            {
                int count = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                if (buffer.Length + count > options.MaximumInputBytes)
                {
                    throw new InvalidDataException("PDF input exceeds MaximumInputBytes.");
                }

                buffer.Write(chunk, 0, count);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new PdfDocument(buffer.ToArray(), options);
        }

        private static void Validate(PdfReaderOptions options)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumInputBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumOperations);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumPathCommands);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumImagePixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumImageBytes);
        }
    }
}
