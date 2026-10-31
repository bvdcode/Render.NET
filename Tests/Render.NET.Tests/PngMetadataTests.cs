using System.Buffers.Binary;
using NUnit.Framework;
using Render.NET.DevelopmentTools;

namespace Render.NET.Tests
{
    public class PngMetadataTests
    {
        [Test]
        public async Task RemovesTextAndRetainsPixelChunks()
        {
            string path = Path.GetTempFileName();
            try
            {
                byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", [1]), .. Chunk("tEXt", [2]), .. Chunk("tRNS", [3]), .. Chunk("IDAT", [4]), .. Chunk("IEND", [])];
                await File.WriteAllBytesAsync(path, png);
                await PngMetadata.StripAsync(path);
                byte[] actual = await File.ReadAllBytesAsync(path);
                byte[] expected = [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", [1]), .. Chunk("tRNS", [3]), .. Chunk("IDAT", [4]), .. Chunk("IEND", [])];
                Assert.That(actual, Is.EqualTo(expected));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public async Task TruncatedChunkIsRejectedWithoutOverwritingFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 1];
                await File.WriteAllBytesAsync(path, png);
                await Assert.ThatAsync(async () => await PngMetadata.StripAsync(path), Throws.TypeOf<InvalidDataException>());
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(png));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static byte[] Chunk(string type, byte[] data)
        {
            byte[] chunk = new byte[data.Length + 12];
            BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
            data.CopyTo(chunk, 8);
            return chunk;
        }
    }
}
