using System;
using System.IO;
using System.Threading;

namespace Render.NET.Jpeg2000
{
    /// <summary>Bounds input, image size, packet count and cumulative decoder allocations.</summary>
    public sealed class Jpeg2000DecodeOptions
    {
        /// <summary>Gets or sets the maximum encoded input size; default 128 MiB.</summary>
        public int MaximumEncodedBytes { get; init; } = 128 * 1024 * 1024;
        /// <summary>Gets or sets the maximum reference-grid pixel count; default 16,777,216.</summary>
        public int MaximumPixels { get; init; } = 16_777_216;
        /// <summary>Gets or sets the cumulative working allocation budget; default 512 MiB.</summary>
        public long MaximumWorkingBytes { get; init; } = 512L * 1024 * 1024;
        /// <summary>Gets or sets the maximum packet count; default 1,000,000.</summary>
        public int MaximumPackets { get; init; } = 1_000_000;
        /// <summary>Gets or sets an optional required width, validated before sample buffers are allocated.</summary>
        public int? ExpectedWidth { get; init; }
        /// <summary>Gets or sets an optional required height, validated before sample buffers are allocated.</summary>
        public int? ExpectedHeight { get; init; }
    }

    internal sealed class DecodeBudget
    {
        private long _allocated;
        private long _packets;
        internal Jpeg2000DecodeOptions Options { get; }
        internal CancellationToken Cancellation { get; }

        internal DecodeBudget(Jpeg2000DecodeOptions options, CancellationToken cancellation)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumPixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumEncodedBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumWorkingBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumPackets);
            if (options.ExpectedWidth <= 0 || options.ExpectedHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            Options = options;
            Cancellation = cancellation;
        }

        internal void Packets(long count)
        {
            if (count > Options.MaximumPackets - _packets)
            {
                throw new InvalidDataException("JPEG 2000 exceeds MaximumPackets.");
            }

            _packets += count;
        }

        internal void Reserve(long bytes)
        {
            Cancellation.ThrowIfCancellationRequested();
            if (bytes < 0 || bytes > Options.MaximumWorkingBytes - _allocated)
            {
                throw new InvalidDataException("JPEG 2000 exceeds MaximumWorkingBytes.");
            }

            _allocated += bytes;
        }

        internal void Pixels(long width, long height, bool referenceGrid = false)
        {
            if (referenceGrid && ((Options.ExpectedWidth.HasValue && width != Options.ExpectedWidth.Value) || (Options.ExpectedHeight.HasValue && height != Options.ExpectedHeight.Value)))
            {
                throw new InvalidDataException("JPEG 2000 dimensions do not match expected dimensions.");
            }

            if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue || width * height > Options.MaximumPixels)
            {
                throw new InvalidDataException("JPEG 2000 exceeds MaximumPixels or has invalid dimensions.");
            }
        }
    }
}
