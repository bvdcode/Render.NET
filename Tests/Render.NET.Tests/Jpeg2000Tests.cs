using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Jpeg2000;

namespace Render.NET.Tests
{
    public class Jpeg2000Tests
    {
        private static string Root => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Jpeg2000");

        public static IEnumerable<TestCaseData> Fixtures()
        {
            using JsonDocument? manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                yield return new TestCaseData(entry.Clone()).SetName("Jpeg2000_" + entry.GetProperty("name").GetString());
            }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void MatchesIndependentOpenJpegComponentSamples(JsonElement entry)
        {
            string name = entry.GetProperty("name").GetString()!;
            byte[] encoded = File.ReadAllBytes(Path.Combine(Root, entry.GetProperty("file").GetString()!));
            byte[] expected = File.ReadAllBytes(Path.Combine(Root, name + ".raw"));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(encoded)), Is.EqualTo(entry.GetProperty("sha256").GetString()));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(expected)), Is.EqualTo(entry.GetProperty("rawSha256").GetString()));
            Jpeg2000Image image = Jpeg2000Decoder.Decode(encoded);
            Assert.That(image.Width, Is.EqualTo(entry.GetProperty("width").GetInt32()));
            Assert.That(image.Height, Is.EqualTo(entry.GetProperty("height").GetInt32()));
            Assert.That(image.ComponentCount, Is.EqualTo(entry.GetProperty("components").GetInt32()));
            int bits = entry.GetProperty("bits").GetInt32();
            bool signed = entry.GetProperty("signed").GetBoolean();
            Assert.That(image.GetComponent(0).BitDepth, Is.EqualTo(bits));
            Assert.That(image.GetComponent(0).IsSigned, Is.EqualTo(signed));
            int maximumError = 0;
            int offset = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    for (int c = 0; c < image.ComponentCount; c++)
                    {
                        int value = bits == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(offset, 2)) : expected[offset];
                        if (signed && value >= 1 << (bits - 1))
                        {
                            value -= 1 << bits;
                        }

                        maximumError = Math.Max(maximumError, Math.Abs(image.GetSample(x, y, c) - value));
                        offset += bits / 8;
                    }
                }
            }

            Assert.That(offset, Is.EqualTo(expected.Length));
            Assert.That(maximumError, Is.LessThanOrEqualTo(entry.GetProperty("maximumError").GetInt32()));
        }

        [Test]
        public void RejectsInputPixelMemoryPacketAndDimensionLimits()
        {
            byte[] data = Read("tiles.jp2");
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, new() { MaximumEncodedBytes = data.Length - 1 }));
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, new() { MaximumPixels = 10 }));
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, new() { MaximumWorkingBytes = 128 }));
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, new() { MaximumPackets = 30 }));
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, new() { ExpectedWidth = 1 }));
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, new() { ExpectedHeight = 1 }));
            Assert.Throws<OperationCanceledException>(() => Jpeg2000Decoder.Decode(data, cancellationToken: new CancellationToken(true)));
        }

        [TestCase(0x5f)]
        [TestCase(0x60)]
        [TestCase(0x61)]
        public void UnsupportedPacketHeadersAreExplicitlyRejected(int marker)
        {
            byte[] data = Read("raw.j2k");
            int start = 4 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2));
            byte[] changed = data[..start].Concat(new byte[] { 255, (byte)marker, 0, 2 }).Concat(data[start..]).ToArray();
            Assert.Throws<NotSupportedException>(() => Jpeg2000Decoder.Decode(changed));
        }

        [Test]
        public void RejectsTruncatedFilesAndInconsistentContainerDimensions()
        {
            byte[] data = Read("RGB-63x45.jp2");
            foreach (int length in new[]
            {
                0,
                1,
                7,
                12,
                data.Length / 2,
                data.Length - 1
            }

            )
            {
                Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data.AsMemory(0, length)), "Length " + length);
            }

            int header = Find(data, "ihdr"u8);
            data[header + 7] ^= 1;
            Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data));
        }

        [Test]
        public async Task DecoderHasNoSharedMutableState()
        {
            byte[] data = Read("style-63.j2k");
            int[][] samples = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Jpeg2000Decoder.Decode(data).GetComponent(0).Samples.ToArray())));
            foreach (int[] sample in samples)
            {
                Assert.That(sample, Is.EqualTo(samples[0]));
            }
        }

        private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(Root, file));
        private static int Find(byte[] data, ReadOnlySpan<byte> value) => data.AsSpan().IndexOf(value);
    }
}
