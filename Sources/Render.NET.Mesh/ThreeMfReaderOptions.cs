using System;

namespace Render.NET.Mesh
{
    /// <summary>Resource limits for 3MF ZIP/XML loading and component expansion.</summary>
    public class ThreeMfReaderOptions
    {
        /// <summary>Gets the compressed input byte limit.</summary>
        public int MaxInputBytes { get; init; } = 128 * 1024 * 1024;
        /// <summary>Gets the total declared and read uncompressed byte limit.</summary>
        public int MaxUncompressedBytes { get; init; } = 256 * 1024 * 1024;
        /// <summary>Gets the maximum uncompressed size of one part.</summary>
        public int MaxPartBytes { get; init; } = 64 * 1024 * 1024;
        /// <summary>Gets the maximum embedded thumbnail size.</summary>
        public int MaxThumbnailBytes { get; init; } = 16 * 1024 * 1024;
        /// <summary>Gets the maximum ZIP entry count.</summary>
        public int MaxArchiveEntries { get; init; } = 4096;
        /// <summary>Gets the maximum object resource count across model parts.</summary>
        public int MaxObjectCount { get; init; } = 10_000;
        /// <summary>Gets the maximum stored and expanded vertex count.</summary>
        public int MaxVertexCount { get; init; } = 3_000_000;
        /// <summary>Gets the maximum stored and expanded triangle count.</summary>
        public int MaxTriangleCount { get; init; } = 1_000_000;
        /// <summary>Gets the maximum component nesting depth.</summary>
        public int MaxComponentDepth { get; init; } = 64;
        /// <summary>Gets the maximum number of visited instances during assembly expansion.</summary>
        public int MaxInstanceCount { get; init; } = 100_000;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxUncompressedBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPartBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxThumbnailBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxArchiveEntries);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxObjectCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxVertexCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTriangleCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxTriangleCount, Array.MaxLength / 3);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxComponentDepth);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxComponentDepth, 256);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInstanceCount);
        }
    }
}
