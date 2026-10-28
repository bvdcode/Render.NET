using System;
using System.IO;
using System.Threading;

namespace Render.NET.Jpeg2000
{
    internal class DecodeBudget
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
