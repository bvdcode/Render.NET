using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;

namespace Render.NET.Tests
{
    public class PngEncoderTests
    {
        [Test]
        public async Task WritesDecodableRgbaWithValidChecksumsAndNoMetadata()
        {
            PixelBuffer image = new(2, 2);
            image[0, 0] = new Rgba32(255, 0, 0);
            image[1, 0] = new Rgba32(0, 255, 0, 128);
            image[0, 1] = new Rgba32(0, 0, 255, 0);
            image[1, 1] = new Rgba32(31, 47, 63, 79);
            using MemoryStream destination = new();
            await PngEncoder.WriteAsync(image, destination);
            Assert.That(destination.CanWrite, Is.True);
            byte[] png = destination.ToArray();
            Assert.That(png[..8], Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            List<string> chunks = [];
            using MemoryStream compressed = new();
            for (int offset = 8; offset < png.Length;)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
                string type = Encoding.ASCII.GetString(png, offset + 4, 4);
                chunks.Add(type);
                uint checksum = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
                Assert.That(Crc(png.AsSpan(offset + 4, length + 4)), Is.EqualTo(checksum), type);
                if (type == "IHDR")
                {
                    Assert.That(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset + 8)), Is.EqualTo(2));
                    Assert.That(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset + 12)), Is.EqualTo(2));
                    Assert.That(png[offset + 16], Is.EqualTo(8));
                    Assert.That(png[offset + 17], Is.EqualTo(6));
                }
                else if (type == "IDAT")
                {
                    compressed.Write(png, offset + 8, length);
                }

                offset += length + 12;
            }

            Assert.That(chunks, Is.EqualTo(new[] { "IHDR", "IDAT", "IEND" }));
            compressed.Position = 0;
            using ZLibStream decoder = new(compressed, CompressionMode.Decompress);
            using MemoryStream raw = new();
            await decoder.CopyToAsync(raw);
            Assert.That(raw.ToArray(), Is.EqualTo(new byte[] { 0, 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 0, 255, 0, 31, 47, 63, 79 }));
        }

        [Test]
        public void RejectsImpossibleDimensionsWithoutOverflow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(int.MaxValue, int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(1, -1));
        }

        [Test]
        public void HonorsCancellationBeforeWriting()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            using MemoryStream stream = new();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await PngEncoder.WriteAsync(new PixelBuffer(1, 1), stream, cancellation.Token));
            Assert.That(stream.Length, Is.Zero);
        }

        private static uint Crc(ReadOnlySpan<byte> data)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320U;
                }
            }

            return crc ^ uint.MaxValue;
        }
    }
}
