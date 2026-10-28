using System.Buffers.Binary;
using NUnit.Framework;
using Render.NET.Jbig2;

namespace Render.NET.Tests
{
    public class Jbig2ValidationTests
    {
        private static string Root => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Jbig2");

        private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Root, name + ".jb2"));
        [Test]
        public void AppliesAllResourceLimitsAndCancellation()
        {
            byte[] data = Read("text-h0-mmr0-t0-c1-x0");
            foreach (Jbig2DecodeOptions options in new Jbig2DecodeOptions[]
            {
                new()
                {
                    MaximumEncodedBytes = data.Length - 1
                },
                new()
                {
                    MaximumPixels = 100
                },
                new()
                {
                    MaximumWorkingBytes = 1024
                },
                new()
                {
                    MaximumDecodedPixels = 10
                },
                new()
                {
                    MaximumSegments = 2
                },
                new()
                {
                    MaximumSymbols = 2
                },
                new()
                {
                    MaximumInstances = 2
                },
                new()
                {
                    ExpectedWidth = 1
                },
                new()
                {
                    ExpectedHeight = 1
                }
            }

            )
            {
                Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data, options: options));
            }

            Assert.Throws<OperationCanceledException>(() => Jbig2Decoder.Decode(data, cancellationToken: new CancellationToken(true)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Jbig2Decoder.Decode(data, options: new() { MaximumPages = 0 }));
        }

        [Test]
        public void DecodesEmbeddedGlobalsWithoutLosingAnyPixels()
        {
            string scans = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "Scans");
            byte[] data = File.ReadAllBytes(Path.Combine(scans, "page.jbig2")), globals = File.ReadAllBytes(Path.Combine(scans, "globals.jbig2"));
            Jbig2Page? page = Jbig2Decoder.Decode(data, globals).Pages[0];
            Assert.That((page.Width, page.Height), Is.EqualTo((320, 120)));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data, globals, new() { MaximumEncodedBytes = data.Length + globals.Length - 1 }));
        }

        [Test]
        public void DecodesMultiplePagesWithLargePageAssociationsAndChecksThePageLimit()
        {
            List<byte[]>? parts = Parts(Read("mmr-odd"));
            byte[] data = Segment(1, 48, 1, parts[0]).Concat(Segment(2, 38, 1, parts[1])).Concat(Segment(3, 48, 300, parts[0])).Concat(Segment(4, 38, 300, parts[1])).ToArray();
            Jbig2Document? result = Jbig2Decoder.Decode(data);
            Assert.That(result.Pages.Select(p => p.Number), Is.EqualTo(new uint[] { 1, 300 }));
            Assert.That(result.Pages[1].Data.ToArray(), Is.EqualTo(File.ReadAllBytes(Path.Combine(Root, "mmr-odd.bin"))));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data, options: new() { MaximumPages = 1 }));
            Jbig2Page? page = result.Pages[0];
            for (int y = 0; y < page.Height; y++)
            {
                for (int x = 0; x < page.Width; x++)
                {
                    Assert.That(page.GetPixel(x, y), Is.EqualTo((page.Data.Span[(y * page.Stride) + (x >> 3)] & (128 >> (x & 7))) != 0));
                }
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => page.GetPixel(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => page.GetPixel(0, page.Height));
        }

        [Test]
        public void RejectsTruncatedArithmeticAndMmrSegments()
        {
            foreach (string name in new[]
            {
                "generic-t0-tp0-at0",
                "mmr-noise",
                "text-refined-h1-t0",
                "custom-huffman"
            }

            )
            {
                byte[] data = Read(name);
                foreach (int length in new[]
                {
                    0,
                    1,
                    8,
                    9,
                    10,
                    20,
                    data.Length / 2,
                    data.Length - 1
                }

                )
                {
                    Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data.AsMemory(0, length)), name + " " + length);
                }
            }

            List<byte[]>? parts = Parts(Read("generic-t0-tp0-at0"));
            byte[] truncated = Segment(1, 48, 1, parts[0]).Concat(Segment(2, 38, 1, parts[1][..^2])).ToArray();
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(truncated));
        }

        [Test]
        public void RejectsDuplicateMissingCrossPageAndForwardReferences()
        {
            List<byte[]>? parts = Parts(Read("mmr-odd"));
            byte[] page = Segment(1, 48, 1, parts[0]);
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(page.Concat(page).ToArray()));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(Segment(1, 38, 1, parts[1])));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(page.Concat(Segment(3, 38, 1, parts[1], 2)).ToArray()));
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(page.Concat(Segment(3, 38, 1, parts[1], 4)).ToArray()));
            byte[] other = Segment(2, 48, 2, parts[0]);
            Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(page.Concat(other).Concat(Segment(3, 38, 1, parts[1], 2)).ToArray()));
        }

        [Test]
        public void RejectsUnsupportedNecessaryExtensionsAndExtendedTemplates()
        {
            List<byte[]>? parts = Parts(Read("generic-t0-tp0-at0"));
            byte[] page = Segment(1, 48, 1, parts[0]);
            Assert.Throws<NotSupportedException>(() => Jbig2Decoder.Decode(page.Concat(Segment(2, 62, 0, new byte[] { 128, 0, 0, 0 })).ToArray()));
            parts[1][17] |= 16;
            Assert.Throws<NotSupportedException>(() => Jbig2Decoder.Decode(page.Concat(Segment(2, 38, 1, parts[1])).ToArray()));
        }

        [Test]
        public void IndependentDecodesAreThreadSafe()
        {
            byte[] data = Read("text-refined-h1-t0"), expected = File.ReadAllBytes(Path.Combine(Root, "text-refined-h1-t0.bin"));
            Parallel.For(0, 100, _ => Assert.That(Jbig2Decoder.Decode(data).Pages[0].Data.ToArray(), Is.EqualTo(expected)));
        }

        [Test]
        public void MalformedMutationCorpusHasOnlyControlledFailures()
        {
            Random? random = new Random(7281);
            Jbig2DecodeOptions? options = new Jbig2DecodeOptions
            {
                MaximumPixels = 10_000,
                MaximumDecodedPixels = 50_000,
                MaximumWorkingBytes = 2_000_000,
                MaximumSymbols = 1000,
                MaximumInstances = 1000,
                MaximumSegments = 100
            };
            foreach (string name in new[]
            {
                "generic-t0-tp0-at0",
                "text-h0-mmr0-t0-c1-x0",
                "text-refined-h1-t0",
                "mmr-noise",
                "halftone-mmr0-t0",
                "custom-huffman"
            }

            )
            {
                byte[] original = Read(name);
                for (int i = 0; i < 100; i++)
                {
                    byte[] changed = (byte[])original.Clone();
                    int position = random.Next(changed.Length);
                    changed[position] ^= (byte)random.Next(1, 256);
                    try
                    {
                        Jbig2Decoder.Decode(changed, options: options);
                    }
                    catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
                    {
                    }
                }
            }
        }

        private static List<byte[]> Parts(byte[] data)
        {
            var result = new List<byte[]>();
            int offset = 9;
            while (offset < data.Length)
            {
                int flags = data[offset + 4], count = data[offset + 5] >> 5;
                int header = 6 + count + ((flags & 64) == 0 ? 1 : 4) + 4;
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + header - 4)));
                result.Add(data.AsSpan(offset + header, length).ToArray());
                offset += header + length;
            }

            return result;
        }

        private static byte[] Segment(uint number, int type, uint page, byte[] data, params uint[] references)
        {
            using MemoryStream? stream = new MemoryStream();
            void U32(uint value)
            {
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
                stream.Write(buffer);
            }

            U32(number);
            stream.WriteByte((byte)(type | (page > 255 ? 64 : 0)));
            stream.WriteByte((byte)(references.Length << 5));
            foreach (uint reference in references)
            {
                stream.WriteByte((byte)reference);
            }

            if (page > 255)
            {
                U32(page);
            }
            else
            {
                stream.WriteByte((byte)page);
            }

            U32((uint)data.Length);
            stream.Write(data);
            return stream.ToArray();
        }
    }
}
