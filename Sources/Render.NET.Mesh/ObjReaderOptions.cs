namespace Render.NET.Mesh
{
    /// <summary>Bounds OBJ input size, geometry allocation, and polygon triangulation work.</summary>
    public class ObjReaderOptions
    {
        /// <summary>Gets the maximum source length in bytes.</summary>
        public int MaxInputBytes { get; init; } = 128 * 1024 * 1024;

        /// <summary>Gets the maximum number of position, texture, or normal records of each kind.</summary>
        public int MaxVertexCount { get; init; } = 3_000_000;

        /// <summary>Gets the maximum number of triangles after polygon triangulation.</summary>
        public int MaxTriangleCount { get; init; } = 1_000_000;

        /// <summary>Gets the maximum number of corners in one polygon.</summary>
        public int MaxFaceVertexCount { get; init; } = 4096;

        internal void Validate()
        {
            System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxVertexCount);
            System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTriangleCount);
            System.ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxTriangleCount, System.Array.MaxLength / 3);
            System.ArgumentOutOfRangeException.ThrowIfLessThan(MaxFaceVertexCount, 3);
            System.ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxFaceVertexCount, 4096);
        }
    }
}
