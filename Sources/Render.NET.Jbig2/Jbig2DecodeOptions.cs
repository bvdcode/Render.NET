using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Render.NET.Jbig2
{
    /// <summary>Resource limits applied before allocating or decoding untrusted JBIG2 content.</summary>
    public class Jbig2DecodeOptions
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
}
