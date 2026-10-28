using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Render.NET.Jbig2
{
    /// <summary>Decodes JBIG2 using managed code, with optional PDF JBIG2Globals.</summary>
    public static class Jbig2Decoder
    {
        /// <summary>Decodes standalone or embedded streams. Unsupported extensions are rejected.</summary>
        public static Jbig2Document Decode(ReadOnlyMemory<byte> encoded, ReadOnlyMemory<byte> globals = default, Jbig2DecodeOptions? options = null, CancellationToken cancellationToken = default)
        {
            DecodeBudget? budget = new DecodeBudget(options ?? new Jbig2DecodeOptions(), cancellationToken);
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
                System.Diagnostics.Trace.TraceError("{0}", ex);
                throw new InvalidDataException("JBIG2 numeric overflow.", ex);
            }
        }
    }
}
