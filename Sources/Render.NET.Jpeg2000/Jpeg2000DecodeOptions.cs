using System;
using System.IO;
using System.Threading;

namespace Render.NET.Jpeg2000
{
    /// <summary>Bounds input, image size, packet count and cumulative decoder allocations.</summary>
    public class Jpeg2000DecodeOptions
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
}
