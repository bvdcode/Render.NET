using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET.Mesh
{
    internal class PreparedMesh
    {
        internal Vector3[] Positions { get; }
        internal Vector3[] Normals { get; }
        internal int[] Indices { get; }

        internal PreparedMesh(TriangleMesh source, MeshRenderOptions options, CancellationToken token)
        {
            Indices = source.IndexData;
            Vector3 minimum = new(float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity);
            foreach (int index in Indices)
            {
                minimum = Vector3.Min(minimum, source.VertexData[index]);
                maximum = Vector3.Max(maximum, source.VertexData[index]);
            }

            // Double intermediates avoid overflow and preserve tiny STL coordinate scales.
            double centerX = ((double)minimum.X + maximum.X) * 0.5;
            double centerY = ((double)minimum.Y + maximum.Y) * 0.5;
            double centerZ = ((double)minimum.Z + maximum.Z) * 0.5;
            double extent = Math.Max((double)maximum.X - minimum.X,
                Math.Max((double)maximum.Y - minimum.Y, (double)maximum.Z - minimum.Z));
            if (extent == 0)
            {
                throw new ArgumentException("The mesh has no spatial extent.", nameof(source));
            }

            Positions = new Vector3[source.VertexData.Length];
            for (int index = 0; index < Positions.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                Vector3 vertex = source.VertexData[index];
                Positions[index] = new Vector3((float)((vertex.X - centerX) * 2 / extent),
                    (float)((vertex.Y - centerY) * 2 / extent), (float)((vertex.Z - centerZ) * 2 / extent));
            }

            Vector3[] faceNormals = new Vector3[source.TriangleCount];
            Vector3[] faceAreas = new Vector3[source.TriangleCount];
            Dictionary<Vector3, List<int>>? adjacency = options.SmoothNormals ? new() : null;
            bool hasSurface = false;
            for (int face = 0; face < faceNormals.Length; face++)
            {
                token.ThrowIfCancellationRequested();
                int corner = face * 3;
                Vector3 a = Positions[Indices[corner]];
                Vector3 b = Positions[Indices[corner + 1]];
                Vector3 c = Positions[Indices[corner + 2]];
                Vector3 area = Vector3.Cross(b - a, c - a);
                if (area.LengthSquared() < 1e-24f)
                {
                    continue;
                }

                hasSurface = true;
                faceAreas[face] = area;
                faceNormals[face] = Vector3.Normalize(area);
                if (adjacency != null)
                {
                    AddFace(adjacency, a, face);
                    AddFace(adjacency, b, face);
                    AddFace(adjacency, c, face);
                }
            }

            if (!hasSurface)
            {
                throw new ArgumentException("The mesh contains no nondegenerate triangles.", nameof(source));
            }

            Normals = new Vector3[Indices.Length];
            float cosine = MathF.Cos(options.CreaseAngle * MathF.PI / 180);
            for (int corner = 0; corner < Indices.Length; corner++)
            {
                if ((corner & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                Vector3 normal = faceNormals[corner / 3];
                Normals[corner] = normal;
                if (normal == Vector3.Zero || adjacency == null)
                {
                    continue;
                }

                List<int> neighbors = adjacency[Positions[Indices[corner]]];
                // Bound smoothing work for pathological meshes with thousands of faces sharing a point.
                if (neighbors.Count > 64)
                {
                    continue;
                }

                Vector3 sum = Vector3.Zero;
                foreach (int neighbor in neighbors)
                {
                    if (Vector3.Dot(normal, faceNormals[neighbor]) >= cosine - 0.000001f)
                    {
                        sum += faceAreas[neighbor];
                    }
                }

                if (sum.LengthSquared() > 1e-24f)
                {
                    Normals[corner] = Vector3.Normalize(sum);
                }
            }
        }

        private static void AddFace(Dictionary<Vector3, List<int>> adjacency, Vector3 vertex, int face)
        {
            if (!adjacency.TryGetValue(vertex, out List<int>? faces))
            {
                faces = new List<int>(6);
                adjacency.Add(vertex, faces);
            }

            faces.Add(face);
        }
    }
}
