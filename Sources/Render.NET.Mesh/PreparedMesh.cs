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
            Quaternion rotation = Quaternion.Normalize(options.ModelRotation);
            Positions = NormalizePositions(source, rotation, token);
            var (faceNormals, faceAreas, adjacency) = CalculateFaces(source, options.SmoothNormals, token);
            Normals = CreateCornerNormals(faceNormals, faceAreas, adjacency, options.CreaseAngle, token);
            if (options.UseSourceNormals && source.NormalData.Length > 0)
            {
                ApplySourceNormals(source.NormalData, rotation, token);
            }
        }

        private static Vector3[] NormalizePositions(TriangleMesh source, Quaternion rotation, CancellationToken token)
        {
            Vector3 minimum = new(float.PositiveInfinity);
            Vector3 maximum = new(float.NegativeInfinity);
            foreach (int index in source.IndexData)
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

            Vector3[] positions = new Vector3[source.VertexData.Length];
            for (int index = 0; index < positions.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                Vector3 vertex = source.VertexData[index];
                positions[index] = new Vector3((float)(((vertex.X - centerX) * 2) / extent),
                    (float)(((vertex.Y - centerY) * 2) / extent), (float)(((vertex.Z - centerZ) * 2) / extent));
                positions[index] = Vector3.Transform(positions[index], rotation);
            }

            return positions;
        }

        private (Vector3[] Normals, Vector3[] Areas, Dictionary<Vector3, List<int>>? Adjacency) CalculateFaces(
            TriangleMesh source, bool smoothNormals, CancellationToken token)
        {
            Vector3[] faceNormals = new Vector3[source.TriangleCount];
            Vector3[] faceAreas = new Vector3[source.TriangleCount];
            Dictionary<Vector3, List<int>>? adjacency = smoothNormals ? new() : null;
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
                if (adjacency is not null)
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

            return (faceNormals, faceAreas, adjacency);
        }

        private Vector3[] CreateCornerNormals(Vector3[] faceNormals, Vector3[] faceAreas,
            Dictionary<Vector3, List<int>>? adjacency, float creaseAngle, CancellationToken token)
        {
            Vector3[] normals = new Vector3[Indices.Length];
            float cosine = MathF.Cos((creaseAngle * MathF.PI) / 180);
            for (int corner = 0; corner < Indices.Length; corner++)
            {
                if ((corner & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                Vector3 normal = faceNormals[corner / 3];
                normals[corner] = normal;
                if (normal == Vector3.Zero || adjacency is null)
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
                    normals[corner] = Vector3.Normalize(sum);
                }
            }

            return normals;
        }

        private void ApplySourceNormals(Vector3[] sourceNormals, Quaternion rotation, CancellationToken token)
        {
            for (int corner = 0; corner < Normals.Length; corner++)
            {
                if ((corner & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                if (sourceNormals[corner] != Vector3.Zero && Normals[corner] != Vector3.Zero)
                {
                    Normals[corner] = Vector3.Transform(GeometryMath.Normalize(sourceNormals[corner]), rotation);
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
