using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET
{
    /// <summary>Writes RGBA8 PNG images using the .NET compression APIs.</summary>
    public static class PngEncoder
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly uint[] CrcTable = CreateCrcTable();

        /// <summary>Writes an image without metadata and leaves the destination stream open.</summary>
        public static async Task WriteAsync(PixelBuffer image, Stream destination, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The destination stream must be writable.", nameof(destination));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using MemoryStream compressed = new();
            await using (ZLibStream compressor = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                int stride = image.Width * 4;
                byte[] scanline = new byte[stride + 1];
                for (int y = 0; y < image.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    image.Pixels.Slice(y * stride, stride).CopyTo(scanline.AsMemory(1));
                    await compressor.WriteAsync(scanline, cancellationToken).ConfigureAwait(false);
                }
            }

            byte[] header = new byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)image.Width);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)image.Height);
            header[8] = 8;
            header[9] = 6;
            await destination.WriteAsync(Signature, cancellationToken).ConfigureAwait(false);
            await WriteChunkAsync(destination, 0x49484452, header, cancellationToken).ConfigureAwait(false);
            await WriteChunkAsync(destination, 0x49444154, compressed.GetBuffer().AsMemory(0, checked((int)compressed.Length)), cancellationToken)
                .ConfigureAwait(false);
            await WriteChunkAsync(destination, 0x49454E44, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteChunkAsync(Stream destination, uint type, ReadOnlyMemory<byte> data, CancellationToken token)
        {
            byte[] header = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), type);
            uint crc = UpdateCrc(uint.MaxValue, header.AsSpan(4));
            crc = UpdateCrc(crc, data.Span) ^ uint.MaxValue;
            byte[] checksum = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
            await destination.WriteAsync(header, token).ConfigureAwait(false);
            await destination.WriteAsync(data, token).ConfigureAwait(false);
            await destination.WriteAsync(checksum, token).ConfigureAwait(false);
        }

        private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
        {
            foreach (byte value in bytes)
            {
                crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] CreateCrcTable()
        {
            uint[] table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                uint crc = index;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320U ^ (crc >> 1);
                }

                table[index] = crc;
            }

            return table;
        }
    }
}
