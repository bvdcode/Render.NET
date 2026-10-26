using System;
using System.Numerics;

namespace Render.NET.Mesh
{
    /// <summary>Owns a copy of finite vertex positions and triangle indices.</summary>
    public class TriangleMesh
    {
        internal Vector3[] VertexData { get; }
        internal int[] IndexData { get; }
        internal Vector3[] NormalData { get; }

        /// <summary>Gets the model-space vertex positions.</summary>
        public ReadOnlyMemory<Vector3> Vertices => VertexData;

        /// <summary>Gets indices grouped into consecutive triples.</summary>
        public ReadOnlyMemory<int> TriangleIndices => IndexData;

        /// <summary>Gets the number of triangles.</summary>
        public int TriangleCount => IndexData.Length / 3;

        /// <summary>Gets optional normals per triangle corner. Zero entries use generated normals.</summary>
        public ReadOnlyMemory<Vector3> CornerNormals => NormalData;

        /// <summary>Copies and validates indexed triangle geometry.</summary>
        public TriangleMesh(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> triangleIndices)
            : this(vertices, triangleIndices, ReadOnlySpan<Vector3>.Empty)
        {
        }

        /// <summary>Copies indexed geometry and optional per-corner normals. Normal count must be zero or match the index count.</summary>
        public TriangleMesh(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> triangleIndices, ReadOnlySpan<Vector3> cornerNormals)
        {
            ValidateNormals(cornerNormals, triangleIndices.Length);
            ValidateVertices(vertices, triangleIndices);
            ValidateIndices(triangleIndices, vertices.Length);

            VertexData = vertices.ToArray();
            IndexData = triangleIndices.ToArray();
            NormalData = cornerNormals.ToArray();
        }

        private static void ValidateNormals(ReadOnlySpan<Vector3> cornerNormals, int indexCount)
        {
            if (!cornerNormals.IsEmpty && cornerNormals.Length != indexCount)
            {
                throw new ArgumentException("Corner normals must match the triangle index count.", nameof(cornerNormals));
            }

            foreach (Vector3 normal in cornerNormals)
            {
                if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z))
                {
                    throw new ArgumentException("Normals must be finite.", nameof(cornerNormals));
                }
            }
        }

        private static void ValidateVertices(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> triangleIndices)
        {
            if (vertices.IsEmpty || triangleIndices.IsEmpty || triangleIndices.Length % 3 != 0)
            {
                throw new ArgumentException("A mesh must contain vertices and complete triangle index triples.");
            }

            foreach (Vector3 vertex in vertices)
            {
                if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
                {
                    throw new ArgumentException("Vertex coordinates must be finite.", nameof(vertices));
                }
            }
        }

        private static void ValidateIndices(ReadOnlySpan<int> triangleIndices, int vertexCount)
        {
            foreach (int index in triangleIndices)
            {
                if ((uint)index >= (uint)vertexCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(triangleIndices), "A triangle index is outside the vertex array.");
                }
            }
        }
    }
}
