using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Render.NET.Jbig2
{
    internal class DecodeBudget
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
