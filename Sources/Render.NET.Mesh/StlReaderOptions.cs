using System;

namespace Render.NET.Mesh
{
    /// <summary>Limits the resources used to import STL files.</summary>
    public class StlReaderOptions
    {
        /// <summary>Gets the maximum input length in bytes. Defaults to 128 MiB.</summary>
        public int MaxInputBytes { get; init; } = 128 * 1024 * 1024;

        /// <summary>Gets the maximum triangle count. Defaults to one million.</summary>
        public int MaxTriangleCount { get; init; } = 1_000_000;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInputBytes);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxInputBytes, Array.MaxLength);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTriangleCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxTriangleCount, Array.MaxLength / 3);
        }
    }
}
