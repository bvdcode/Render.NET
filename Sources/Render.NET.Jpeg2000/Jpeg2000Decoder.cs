using System;
using System.IO;
using System.Threading;

namespace Render.NET.Jpeg2000
{
    /// <summary>Decodes JPEG 2000 Part 1 into integer component samples using managed code.</summary>
    public static class Jpeg2000Decoder
    {
        /// <summary>Decodes a JP2 container or raw J2K codestream. Unsupported extensions are rejected.</summary>
        public static Jpeg2000Image Decode(ReadOnlyMemory<byte> encoded, Jpeg2000DecodeOptions? options = null, CancellationToken cancellationToken = default)
        {
            DecodeBudget? budget = new DecodeBudget(options ?? new Jpeg2000DecodeOptions(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded.Length > budget.Options.MaximumEncodedBytes)
            {
                throw new InvalidDataException("JPEG 2000 exceeds MaximumEncodedBytes.");
            }

            (ReadOnlyMemory<byte> Stream, ContainerInfo Info) container = Jp2Container.Read(encoded);
            return new CodestreamDecoder(container.Stream, container.Info, budget).Decode();
        }
    }
}
