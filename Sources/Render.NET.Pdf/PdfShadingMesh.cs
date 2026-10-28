using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;

namespace Render.NET.Pdf
{
    internal class PdfShadingMesh
    {
        internal Shading Shading { get; }
        internal List<MeshTriangle> Triangles { get; } = new();
        internal List<PdfMeshPatch> Patches { get; } = new();
        internal int MaximumTriangles { get; }
        internal long StorageBytes => _stream.DataBytes + ((long)Triangles.Count * (128 + (24 * _stream.Components)))
            + ((long)Patches.Count * (256 + (32 * _stream.Components)));
        private readonly PdfMeshStream _stream;
        private readonly CancellationToken _token;

        private PdfShadingMesh(Shading shading, PdfReaderOptions limits, CancellationToken token)
        {
            Shading = shading;
            _stream = new PdfMeshStream(shading, limits);
            MaximumTriangles = Math.Min(limits.MaximumPathCommands / 3, limits.MaximumShadingBytes / (128 + (24 * _stream.Components)));
            _token = token;
        }

        internal static PdfShadingMesh Read(Shading shading, PdfReaderOptions limits, CancellationToken token)
        {
            PdfShadingMesh? mesh = new PdfShadingMesh(shading, limits, token);
            switch (shading)
            {
                case FreeFormGouraudShading:
                    mesh.ReadTriangles();
                    break;
                case LatticeFormGouraudShading lattice:
                    mesh.ReadLattice(lattice.VerticesPerRow);
                    break;
                default:
                    mesh.ReadPatches(shading is TensorProductPatchMeshesShading);
                    break;
            }

            return mesh;
        }

        private void ReadTriangles()
        {
            MeshTriangle? previous = null;
            while (!_stream.End)
            {
                _token.ThrowIfCancellationRequested();
                int flag = _stream.Flag();
                MeshVertex next = _stream.Vertex();
                MeshTriangle triangle;
                if (flag == 0)
                {
                    _stream.Flag();
                    MeshVertex second = _stream.Vertex();
                    _stream.Flag();
                    triangle = new MeshTriangle(next, second, _stream.Vertex());
                }
                else if (previous is not null && flag is 1 or 2)
                {
                    triangle = new MeshTriangle(flag == 1 ? previous.B : previous.A, previous.C, next);
                }
                else
                {
                    throw new InvalidDataException("Invalid PDF mesh triangle continuation.");
                }

                AddTriangle(Triangles, triangle, MaximumTriangles);
                previous = triangle;
            }
        }

        private void ReadLattice(int columns)
        {
            if (columns < 2 || columns > MaximumTriangles)
            {
                throw new InvalidDataException("Invalid PDF mesh lattice width.");
            }

            MeshVertex[] previous = ReadRow(columns);
            int rows = 1;
            while (!_stream.End)
            {
                MeshVertex[] current = ReadRow(columns);
                for (int i = 0; i < columns - 1; i++)
                {
                    AddTriangle(Triangles, new MeshTriangle(previous[i], previous[i + 1], current[i]), MaximumTriangles);
                    AddTriangle(Triangles, new MeshTriangle(previous[i + 1], current[i], current[i + 1]), MaximumTriangles);
                }

                previous = current;
                rows++;
            }

            if (rows < 2)
            {
                throw new InvalidDataException("PDF mesh lattice requires at least two rows.");
            }
        }

        private MeshVertex[] ReadRow(int count)
        {
            MeshVertex[]? row = new MeshVertex[count];
            for (int i = 0; i < count; i++)
            {
                _token.ThrowIfCancellationRequested();
                row[i] = _stream.Vertex();
            }

            return row;
        }

        private void ReadPatches(bool tensor)
        {
            PdfMeshPatch? previous = null;
            while (!_stream.End)
            {
                _token.ThrowIfCancellationRequested();
                int flag = _stream.Flag();
                Vector2[]? points = new Vector2[tensor ? 16 : 12];
                double[][]? colors = new double[4][];
                int first = 0;
                if (flag != 0)
                {
                    if (previous is null)
                    {
                        throw new InvalidDataException("PDF patch continuation without a preceding patch.");
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        points[i] = previous.Points[((flag * 3) + i) % 12];
                    }

                    colors[0] = previous.Colors[flag];
                    colors[1] = previous.Colors[(flag + 1) % 4];
                    first = 4;
                }

                for (int i = first; i < points.Length; i++)
                {
                    points[i] = _stream.Point();
                }

                for (int i = first == 0 ? 0 : 2; i < 4; i++)
                {
                    colors[i] = _stream.Color();
                }

                _stream.Align();
                previous = new PdfMeshPatch(points, colors);
                Patches.Add(previous);
                if (Patches.Count > MaximumTriangles / 2)
                {
                    throw new InvalidDataException("PDF mesh exceeds the shading geometry budget.");
                }
            }
        }

        internal static void AddTriangle(List<MeshTriangle> output, MeshTriangle triangle, int limit)
        {
            if (output.Count >= limit)
            {
                throw new InvalidDataException("PDF mesh exceeds the shading geometry budget.");
            }

            output.Add(triangle);
        }
    }
}
