using System;
using System.Numerics;

namespace Render.NET.Mesh
{
    /// <summary>Owns a copy of finite vertex positions and triangle indices.</summary>
    public class TriangleMesh
    {
        internal Vector3[] VertexData { get; }
        internal int[] IndexData { get; }

        /// <summary>Gets the model-space vertex positions.</summary>
        public ReadOnlyMemory<Vector3> Vertices => VertexData;

        /// <summary>Gets indices grouped into consecutive triples.</summary>
        public ReadOnlyMemory<int> TriangleIndices => IndexData;

        /// <summary>Gets the number of triangles.</summary>
        public int TriangleCount => IndexData.Length / 3;

        /// <summary>Copies and validates indexed triangle geometry.</summary>
        public TriangleMesh(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> triangleIndices)
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

            foreach (int index in triangleIndices)
            {
                if ((uint)index >= (uint)vertices.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(triangleIndices), "A triangle index is outside the vertex array.");
                }
            }

            VertexData = vertices.ToArray();
            IndexData = triangleIndices.ToArray();
        }
    }
}
