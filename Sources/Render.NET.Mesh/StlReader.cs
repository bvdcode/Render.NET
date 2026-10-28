using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Mesh
{
    /// <summary>Loads binary and ASCII STL geometry without taking ownership of the source stream.</summary>
    public static class StlReader
    {
        /// <summary>Reads from the stream's current position, including non-seekable streams.</summary>
        public static async Task<TriangleMesh> ReadAsync(Stream source, StlReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!source.CanRead)
            {
                throw new ArgumentException("The source stream must be readable.", nameof(source));
            }

            options ??= new StlReaderOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (source.CanSeek && source.Length - source.Position > options.MaxInputBytes)
            {
                throw new InvalidDataException("The STL input exceeds MaxInputBytes.");
            }

            await using MemoryStream input = new();
            byte[] buffer = new byte[65536];
            while (true)
            {
                int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                if (input.Length + count > options.MaxInputBytes)
                {
                    throw new InvalidDataException("The STL input exceeds MaxInputBytes.");
                }

                await input.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }

            return Parse(input.GetBuffer().AsSpan(0, checked((int)input.Length)), options, cancellationToken);
        }

        private static TriangleMesh Parse(ReadOnlySpan<byte> data, StlReaderOptions options, CancellationToken token)
        {
            if (data.Length >= 84)
            {
                uint triangleCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(80, 4));
                ulong expectedLength = 84UL + (triangleCount * 50UL);
                if (expectedLength == (ulong)data.Length)
                {
                    return ParseBinary(data, triangleCount, options, token);
                }
            }

            ReadOnlySpan<byte> text = data;
            if (text.StartsWith(new byte[] { 239, 187, 191 }))
            {
                text = text[3..];
            }

            string prefix = Encoding.UTF8.GetString(text[..Math.Min(text.Length, 256)]).TrimStart();
            if (prefix.StartsWith("solid", StringComparison.OrdinalIgnoreCase)
                && (prefix.Length == 5 || char.IsWhiteSpace(prefix[5])))
            {
                return AsciiStlParser.Parse(Encoding.UTF8.GetString(text), options, token);
            }

            throw new InvalidDataException("The input is not a complete binary or ASCII STL file.");
        }

        private static TriangleMesh ParseBinary(ReadOnlySpan<byte> data, uint count, StlReaderOptions options, CancellationToken token)
        {
            if (count == 0 || count > options.MaxTriangleCount)
            {
                throw new InvalidDataException("The STL triangle count is zero or exceeds MaxTriangleCount.");
            }

            int vertexCount = checked((int)count * 3);
            Vector3[] vertices = new Vector3[vertexCount];
            int[] indices = new int[vertexCount];
            for (int triangle = 0; triangle < (int)count; triangle++)
            {
                token.ThrowIfCancellationRequested();
                int offset = 84 + (triangle * 50) + 12;
                for (int corner = 0; corner < 3; corner++)
                {
                    ReadOnlySpan<byte> position = data.Slice(offset + (corner * 12), 12);
                    Vector3 vertex = new(
                        BinaryPrimitives.ReadSingleLittleEndian(position),
                        BinaryPrimitives.ReadSingleLittleEndian(position[4..]),
                        BinaryPrimitives.ReadSingleLittleEndian(position[8..]));
                    if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
                    {
                        throw new InvalidDataException("STL vertex coordinates must be finite.");
                    }

                    int index = (triangle * 3) + corner;
                    vertices[index] = vertex;
                    indices[index] = index;
                }
            }

            return new TriangleMesh(vertices, indices);
        }
    }
}
