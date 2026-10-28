using System.Buffers.Binary;
using System.IO.Compression;
using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgFontContainerTests
    {
        private static byte[] Font()
        {
            return File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Fonts", "Canada1500-Regular.ttf"));
        }

        private static PixelBuffer Render(byte[] bytes)
        {
            SvgDocument? doc = SvgReader.Read("<svg width='300' height='70'><text x='5' y='50' font-size='35' font-family='Test'>AV Привет!</text></svg>", new SvgReaderOptions { FontResolver = _ => bytes });
            Assert.That(doc.UnsupportedFeatures, Is.Empty);
            return SvgRenderer.Render(doc, new SvgRenderOptions { Supersampling = 1 });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RawAndCompressedWoffMatchOriginalTrueType(bool compress)
        {
            byte[] ttf = Font();
            int count = BinaryPrimitives.ReadUInt16BigEndian(ttf.AsSpan(4));
            byte[] header = new byte[44 + (count * 20)];
            "wOFF"u8.CopyTo(header);
            ttf.AsSpan(0, 4).CopyTo(header.AsSpan(4));
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(12), (ushort)count);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), (uint)ttf.Length);
            using MemoryStream? output = new MemoryStream();
            output.Write(header);
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> record = ttf.AsSpan(12 + (i * 16), 16);
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]), length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                byte[] payload = ttf.AsSpan(offset, length).ToArray();
                if (compress)
                {
                    using MemoryStream? encoded = new MemoryStream();
                    using (ZLibStream? zlib = new ZLibStream(encoded, CompressionLevel.Optimal, true))
                        zlib.Write(payload);
                    if (encoded.Length < payload.Length)
                    {
                        payload = encoded.ToArray();
                    }
                }

                Span<byte> entry = header.AsSpan(44 + (i * 20), 20);
                record[..4].CopyTo(entry);
                BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)output.Position);
                BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)payload.Length);
                BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)length);
                record.Slice(4, 4).CopyTo(entry[16..]);
                output.Write(payload);
                while (output.Position % 4 != 0)
                {
                    output.WriteByte(0);
                }
            }

            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)output.Length);
            output.Position = 0;
            output.Write(header);
            Assert.That(Render(output.ToArray()).Pixels.Span.SequenceEqual(Render(ttf).Pixels.Span), Is.True);
        }
    }
}
