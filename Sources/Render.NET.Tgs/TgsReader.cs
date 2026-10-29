using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Tgs
{
    /// <summary>Reads bounded TGS or Lottie JSON without loading external assets.</summary>
    public static class TgsReader
    {
        /// <summary>Reads JSON bytes or gzip TGS bytes.</summary>
        public static TgsDocument Read(ReadOnlyMemory<byte> data, TgsReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new TgsReaderOptions();
            Validate(options);
            cancellationToken.ThrowIfCancellationRequested();
            if (data.Length > options.MaximumInputBytes)
            {
                throw new InvalidDataException("TGS exceeds MaximumInputBytes.");
            }

            using MemoryStream? source = new MemoryStream(data.ToArray(), writable: false);
            if (data.Length >= 2 && data.Span[0] == 0x1f && data.Span[1] == 0x8b)
            {
                using GZipStream? gzip = new GZipStream(source, CompressionMode.Decompress);
                using MemoryStream? json = new MemoryStream();
                byte[] buffer = new byte[8192];
                int n;
                while ((n = gzip.Read(buffer)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (json.Length + n > options.MaximumJsonBytes)
                    {
                        throw new InvalidDataException("TGS exceeds MaximumJsonBytes.");
                    }

                    json.Write(buffer, 0, n);
                }

                return Parse(json.ToArray(), options, cancellationToken);
            }

            if (data.Length > options.MaximumJsonBytes)
            {
                throw new InvalidDataException("Lottie exceeds MaximumJsonBytes.");
            }

            return Parse(data, options, cancellationToken);
        }

        /// <summary>Reads bounded TGS/JSON bytes, leaving the source open.</summary>
        public static async Task<TgsDocument> ReadAsync(Stream source, TgsReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new TgsReaderOptions();
            Validate(options);
            using MemoryStream? data = new MemoryStream();
            byte[] buffer = new byte[8192];
            int n;
            while ((n = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (data.Length + n > options.MaximumInputBytes)
                {
                    throw new InvalidDataException("TGS exceeds MaximumInputBytes.");
                }

                await data.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            }

            return Read(data.ToArray(), options, cancellationToken);
        }

        /// <summary>Reads a local TGS or Lottie JSON file.</summary>
        public static async Task<TgsDocument> ReadFileAsync(string path, TgsReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            await using FileStream? source = File.OpenRead(path);
            return await ReadAsync(source, options, cancellationToken).ConfigureAwait(false);
        }

        private static TgsDocument Parse(ReadOnlyMemory<byte> data, TgsReaderOptions options, CancellationToken token)
        {
            using JsonDocument json = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = options.MaximumDepth });
            JsonElement root = json.RootElement;
            double width = TgsJson.Number(root, "w"), height = TgsJson.Number(root, "h");
            double rate = TgsJson.Number(root, "fr"), first = TgsJson.Number(root, "ip"), last = TgsJson.Number(root, "op");
            if (width <= 0 || height <= 0 || width > 1_000_000 || height > 1_000_000 || width != Math.Floor(width) || height != Math.Floor(height) || rate <= 0 || last <= first || !double.IsFinite((rate + last) - first))
            {
                throw new InvalidDataException("Invalid Lottie composition dimensions or timing.");
            }

            if (TgsJson.Get(root, "layers").ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Lottie layers must be an array.");
            }

            string[] features = new TgsFeatureCheck(options, token).Check(root);
            return new TgsDocument(root.Clone(), options, features);
        }

        private static void Validate(TgsReaderOptions o)
        {
            o.Expressions?.Validate();
            if (o.MaximumInputBytes <= 0 || o.MaximumJsonBytes <= 0 || o.MaximumItems <= 0 || o.MaximumPathCommands <= 0 || o.MaximumDepth < 1 || o.MaximumDepth > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(o));
            }
        }
    }
}
