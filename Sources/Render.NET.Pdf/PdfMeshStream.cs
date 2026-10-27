using System;
using System.IO;
using System.Numerics;
using UglyToad.PdfPig.Graphics.Colors;

namespace Render.NET.Pdf
{
    internal sealed class PdfMeshStream
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly double[] _decode;
        private readonly int _coordinateBits;
        private readonly int _componentBits;
        private readonly int _flagBits;
        private long _bit;
        internal int Components { get; }
        internal int DataBytes => _data.Length;
        internal bool End => _bit == _data.Length * 8L;

        internal PdfMeshStream(Shading shading, PdfReaderOptions limits)
        {
            (_data, _coordinateBits, _componentBits, _flagBits, _decode) = shading switch
            {
                FreeFormGouraudShading s => (s.Data, s.BitsPerCoordinate, s.BitsPerComponent, s.BitsPerFlag, s.Decode),
                LatticeFormGouraudShading s => (s.Data, s.BitsPerCoordinate, s.BitsPerComponent, 0, s.Decode),
                CoonsPatchMeshesShading s => (s.Data, s.BitsPerCoordinate, s.BitsPerComponent, s.BitsPerFlag, s.Decode),
                TensorProductPatchMeshesShading s => (s.Data, s.BitsPerCoordinate, s.BitsPerComponent, s.BitsPerFlag, s.Decode),
                _ => throw new NotSupportedException("Unknown PDF shading type.")};
            Components = shading.Functions?.Length > 0 ? 1 : shading.ColorSpace.NumberOfColorComponents;
            Validate(limits);
        }

        private void Validate(PdfReaderOptions limits)
        {
            if (_data.Length > limits.MaximumShadingBytes || Components < 1 || Components > 32 || _decode.Length != 4 + (2 * Components))
            {
                throw new InvalidDataException("Invalid or oversized PDF mesh shading data.");
            }

            ValidateBitWidths();
            foreach (double value in _decode)
            {
                if (!double.IsFinite(value))
                {
                    throw new InvalidDataException("Non-finite PDF mesh decode range.");
                }
            }
        }

        private void ValidateBitWidths()
        {
            if (_coordinateBits is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32) || _componentBits is not (1 or 2 or 4 or 8 or 12 or 16) || _flagBits is not (0 or 2 or 4 or 8))
            {
                throw new InvalidDataException("Invalid PDF mesh shading bit widths.");
            }
        }

        internal int Flag() => (int)(Bits(_flagBits) & 3);
        internal Vector2 Point()
        {
            var point = new Vector2((float)Decoded(_coordinateBits, 0), (float)Decoded(_coordinateBits, 2));
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                throw new InvalidDataException("PDF mesh coordinate exceeds the geometry range.");
            }

            return point;
        }

        internal void Align() => _bit = checked((_bit + 7) & ~7);
        internal double[] Color()
        {
            var values = new double[Components];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Decoded(_componentBits, 4 + (2 * i));
            }

            return values;
        }

        internal MeshVertex Vertex()
        {
            var vertex = new MeshVertex(Point(), Color());
            Align();
            return vertex;
        }

        private double Decoded(int bits, int range) => _decode[range] + ((_decode[range + 1] - _decode[range]) * (Bits(bits) / ((1UL << bits) - 1d)));
        private uint Bits(int count)
        {
            if (_bit + (long)count > _data.Length * 8L)
            {
                throw new InvalidDataException("Truncated PDF mesh shading record.");
            }

            uint result = 0;
            ReadOnlySpan<byte> data = _data.Span;
            for (int i = 0; i < count; i++, _bit++)
            {
                result = (result << 1) | (uint)((data[(int)(_bit >> 3)] >> (7 - (int)(_bit & 7))) & 1);
            }

            return result;
        }
    }

    internal sealed record MeshVertex(Vector2 Position, double[] Components);
    internal sealed record MeshTriangle(MeshVertex A, MeshVertex B, MeshVertex C);
}
