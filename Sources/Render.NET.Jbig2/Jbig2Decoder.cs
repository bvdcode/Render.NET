using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Render.NET.Jbig2
{
    /// <summary>Resource limits applied before allocating or decoding untrusted JBIG2 content.</summary>
    public sealed class Jbig2DecodeOptions
    {
        /// <summary>Maximum combined size of the image and global streams.</summary>
        public int MaximumEncodedBytes { get; init; } = 128 * 1024 * 1024;
        /// <summary>Maximum pixels in an individual bitmap.</summary>
        public int MaximumPixels { get; init; } = 16_000_000;
        /// <summary>Maximum cumulative bitmap and context allocations.</summary>
        public long MaximumWorkingBytes { get; init; } = 512L * 1024 * 1024;
        /// <summary>Maximum cumulative decoded or composed pixels, including intermediate images.</summary>
        public long MaximumDecodedPixels { get; init; } = 512_000_000;
        /// <summary>Maximum segment count across both streams.</summary>
        public int MaximumSegments { get; init; } = 100_000;
        /// <summary>Maximum symbols in a dictionary.</summary>
        public int MaximumSymbols { get; init; } = 1_000_000;
        /// <summary>Maximum symbol placements or integer runs in a region.</summary>
        public int MaximumInstances { get; init; } = 1_000_000;
        /// <summary>Maximum decoded page count.</summary>
        public int MaximumPages { get; init; } = 100;
        /// <summary>Optional required width, checked before allocating each page.</summary>
        public int? ExpectedWidth { get; init; }
        /// <summary>Optional required height, checked before allocating each page.</summary>
        public int? ExpectedHeight { get; init; }
    }

    /// <summary>A monochrome page. Set bits represent black pixels; rows are byte aligned.</summary>
    public sealed class Jbig2Page
    {
        private readonly Bitmap _bitmap;
        internal Jbig2Page(uint number, Bitmap bitmap)
        {
            Number = number;
            _bitmap = bitmap;
        }

        /// <summary>The page association number in the stream.</summary>
        public uint Number { get; }
        /// <summary>Width in pixels.</summary>
        public int Width => _bitmap.Width;
        /// <summary>Height in pixels.</summary>
        public int Height => _bitmap.Height;
        /// <summary>Bytes per row.</summary>
        public int Stride => _bitmap.Stride;
        /// <summary>MSB-first packed pixels. Unused low bits in the final byte are zero.</summary>
        public ReadOnlyMemory<byte> Data => _bitmap.Data;

        /// <summary>Returns whether a pixel is black.</summary>
        public bool GetPixel(int x, int y)
        {
            if ((uint)x >= Width || (uint)y >= Height)
            {
                throw new ArgumentOutOfRangeException();
            }

            return _bitmap.Get(x, y) != 0;
        }
    }

    /// <summary>Decoded pages in page-association order.</summary>
    public sealed class Jbig2Document
    {
        internal Jbig2Document(Jbig2Page[] pages)
        {
            Pages = Array.AsReadOnly(pages);
        }

        /// <summary>The decoded pages.</summary>
        public IReadOnlyList<Jbig2Page> Pages { get; }
    }

    /// <summary>Decodes JBIG2 using managed code, with optional PDF JBIG2Globals.</summary>
    public static class Jbig2Decoder
    {
        /// <summary>Decodes standalone or embedded streams. Unsupported extensions are rejected.</summary>
        public static Jbig2Document Decode(ReadOnlyMemory<byte> encoded, ReadOnlyMemory<byte> globals = default, Jbig2DecodeOptions? options = null, CancellationToken cancellationToken = default)
        {
            var budget = new DecodeBudget(options ?? new Jbig2DecodeOptions(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if ((long)encoded.Length + globals.Length > budget.Options.MaximumEncodedBytes)
            {
                throw new InvalidDataException("JBIG2 exceeds MaximumEncodedBytes.");
            }

            try
            {
                return new DocumentDecoder(budget).Decode(encoded, globals);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("JBIG2 numeric overflow.", ex);
            }
        }
    }

    internal sealed class DecodeBudget
    {
        private long _allocated;
        private long _work;
        internal DecodeBudget(Jbig2DecodeOptions options, CancellationToken cancellation)
        {
            Options = options;
            Cancellation = cancellation;
            if (options.MaximumEncodedBytes <= 0 || options.MaximumPixels <= 0 || options.MaximumWorkingBytes <= 0 || options.MaximumDecodedPixels <= 0 || options.MaximumSegments <= 0 || options.MaximumSymbols <= 0 || options.MaximumInstances <= 0 || options.MaximumPages <= 0 || options.ExpectedWidth <= 0 || options.ExpectedHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
        }

        internal Jbig2DecodeOptions Options { get; }
        internal CancellationToken Cancellation { get; }

        internal void Allocate(long bytes)
        {
            Cancellation.ThrowIfCancellationRequested();
            if (bytes < 0 || bytes > Options.MaximumWorkingBytes - _allocated)
            {
                throw new InvalidDataException("JBIG2 exceeds MaximumWorkingBytes.");
            }

            _allocated += bytes;
        }

        internal void Work(long pixels)
        {
            Cancellation.ThrowIfCancellationRequested();
            _work += pixels;
            if (_work > Options.MaximumDecodedPixels)
            {
                throw new InvalidDataException("JBIG2 decoding work limit exceeded.");
            }
        }

        internal int Count(uint value, int maximum)
        {
            if (value > maximum)
            {
                throw new InvalidDataException("JBIG2 count exceeds its configured limit.");
            }

            return (int)value;
        }

        internal byte[] Contexts(int length)
        {
            Allocate(length);
            return new byte[length];
        }
    }
}
