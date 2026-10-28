using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Mesh
{
    internal class ObjParser(ObjReaderOptions options)
    {
        private readonly List<Vector3> _positions = [], _normals = [];
        private readonly List<Corner> _corners = [];
        private readonly List<int> _smoothing = [];
        private readonly Dictionary<string, int> _groups = new(StringComparer.Ordinal);
        private int _textureCount;
        private int _group = -1;

        internal async Task<TriangleMesh> ReadAsync(StreamReader reader, CancellationToken token)
        {
            int lineNumber = 0;
            string pending = "";
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is string line)
            {
                token.ThrowIfCancellationRequested();
                lineNumber++;
                int comment = line.IndexOf('#');
                line = (comment < 0 ? line : line[..comment]).Trim();
                if (line.EndsWith('\\'))
                {
                    pending += line[..^1] + " ";
                    continue;
                }

                line = pending + line;
                pending = "";
                string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 0)
                {
                    continue;
                }

                try
                {
                    ParseLine(fields, token);
                }
                catch (InvalidDataException exception)
                {
                    System.Diagnostics.Trace.TraceError("{0}", exception);
                    throw new InvalidDataException($"Invalid OBJ at line {lineNumber}: {exception.Message}", exception);
                }
            }

            Require(pending.Length == 0 && _corners.Count > 0);
            return Build(_positions, _normals, _corners, _smoothing, token);
        }

        private void ParseLine(string[] fields, CancellationToken token)
        {
            switch (fields[0])
            {
                case "v":
                    ReadPosition(fields);
                    break;
                case "vn":
                    Require(fields.Length == 4 && _normals.Count < options.MaxVertexCount);
                    _normals.Add(GeometryMath.Normalize(Vector(fields)));
                    break;
                case "vt":
                    ReadTextureCoordinate(fields);
                    break;
                case "s":
                    ReadSmoothingGroup(fields);
                    break;
                case "f":
                    ReadFace(fields, token);
                    break;
                case "curv":
                case "curv2":
                case "surf":
                    throw new NotSupportedException("Free-form OBJ curves and surfaces must be tessellated before loading.");
            }
        }

        private void ReadPosition(string[] fields)
        {
            Require(fields.Length >= 4 && _positions.Count < options.MaxVertexCount);
            Vector3 position = Vector(fields);
            if (fields.Length == 5)
            {
                float weight = Number(fields[4]);
                Require(weight != 0);
                position /= weight;
                Require(float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z));
            }

            _positions.Add(position);
        }

        private void ReadTextureCoordinate(string[] fields)
        {
            Require(fields.Length is >= 2 and <= 4 && _textureCount < options.MaxVertexCount);
            for (int index = 1; index < fields.Length; index++)
            {
                Number(fields[index]);
            }

            _textureCount++;
        }

        private void ReadSmoothingGroup(string[] fields)
        {
            Require(fields.Length == 2);
            if (fields[1] is "off" or "0")
            {
                _group = 0;
            }
            else if (!_groups.TryGetValue(fields[1], out _group))
            {
                _group = _groups.Count + 1;
                _groups.Add(fields[1], _group);
            }
        }

        private void ReadFace(string[] fields, CancellationToken token)
        {
            Require(fields.Length >= 4 && fields.Length - 1 <= options.MaxFaceVertexCount);
            Require((((long)_corners.Count / 3) + fields.Length) - 3 <= options.MaxTriangleCount);
            Corner[] polygon = new Corner[fields.Length - 1];
            Vector3[] points = new Vector3[polygon.Length];
            for (int index = 0; index < polygon.Length; index++)
            {
                Corner corner = ReadCorner(fields[index + 1]);
                polygon[index] = corner;
                points[index] = _positions[corner.Vertex];
            }

            foreach (int index in PolygonTriangulator.Triangulate(points, token))
            {
                _corners.Add(polygon[index]);
                _smoothing.Add(_group);
            }
        }

        private Corner ReadCorner(string value)
        {
            string[] parts = value.Split('/');
            Require(parts.Length <= 3);
            int vertex = Index(parts[0], _positions.Count);
            if (parts.Length >= 2 && parts[1].Length > 0)
            {
                Index(parts[1], _textureCount);
            }

            int normal = parts.Length == 3 && parts[2].Length > 0 ? Index(parts[2], _normals.Count) : -1;
            return new Corner(vertex, normal);
        }

        private static TriangleMesh Build(List<Vector3> vertices, List<Vector3> sourceNormals, List<Corner> corners, List<int> smoothing, CancellationToken token)
        {
            int[] indices = new int[corners.Count];
            Vector3[] outputNormals = new Vector3[corners.Count];
            Dictionary<(int Vertex, int Group), Vector3> sums = [];
            for (int corner = 0; corner < corners.Count; corner += 3)
            {
                token.ThrowIfCancellationRequested();
                Vector3 face = GeometryMath.FaceNormal(vertices[corners[corner].Vertex], vertices[corners[corner + 1].Vertex], vertices[corners[corner + 2].Vertex]);
                for (int offset = 0; offset < 3; offset++)
                {
                    int current = corner + offset;
                    Corner item = corners[current];
                    indices[current] = item.Vertex;
                    if (item.Normal >= 0)
                    {
                        outputNormals[current] = sourceNormals[item.Normal];
                    }
                    else if (smoothing[current] == 0)
                    {
                        outputNormals[current] = face;
                    }

                    if (smoothing[current] > 0)
                    {
                        (int Vertex, int) key = (item.Vertex, smoothing[current]);
                        sums.TryGetValue(key, out Vector3 sum);
                        sums[key] = sum + face;
                    }
                }
            }

            for (int index = 0; index < corners.Count; index++)
            {
                if (corners[index].Normal < 0 && smoothing[index] > 0)
                {
                    outputNormals[index] = GeometryMath.Normalize(sums[(corners[index].Vertex, smoothing[index])]);
                }
            }

            return new TriangleMesh(vertices.ToArray(), indices, outputNormals);
        }

        private static int Index(string value, int count)
        {
            Require(int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index != 0);
            long resolved = index > 0 ? (long)index - 1 : (long)count + index;
            Require(resolved >= 0 && resolved < count);
            return (int)resolved;
        }

        private static Vector3 Vector(string[] fields) => new(Number(fields[1]), Number(fields[2]), Number(fields[3]));

        private static float Number(string value)
        {
            Require(float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) && float.IsFinite(result));
            return result;
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Malformed geometry, out-of-range index, or configured limit exceeded.");
            }
        }

        private readonly record struct Corner(int Vertex, int Normal);
    }
}
