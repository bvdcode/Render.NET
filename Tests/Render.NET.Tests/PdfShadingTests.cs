using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfShadingTests
    {
        private static readonly PdfRenderOptions Exact = new() { Dpi = 72, Supersampling = 1 };

        [Test]
        public async Task FunctionShadingUsesBothInputsAndItsOwnMatrixDomainAndPageClip()
        {
            string function = "/ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] /Matrix [160 0 0 60 20 20] "
                + "/Function 6 0 R /BBox [10 10 180 80]";
            byte[] calculator = PdfTestDocument.Stream(PdfTestDocument.Bytes("{ 0 }"),
                "/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]");
            PixelBuffer image = await Render(Page(PdfTestDocument.Bytes("<< " + function + " >>"), extra: new[] { calculator }));
            Assert.That(At(image, 100, 50).R, Is.EqualTo(128).Within(3));
            Assert.That(At(image, 100, 50).G, Is.EqualTo(128).Within(3));
            Assert.That(At(image, 100, 50).B, Is.Zero);
            Assert.That(At(image, 5, 50), Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(12)]
        [TestCase(16)]
        [TestCase(24)]
        [TestCase(32)]
        public async Task FreeTrianglesDecodeEveryCoordinateBitWidthAndInterpolate(int bits)
        {
            Bits? writer = new Bits();
            Vertex(writer, 0, 0, 0, new[] { 1d, 0, 0 }, bits);
            Vertex(writer, 3, 1, 0, new[] { 0d, 1, 0 }, bits);
            Vertex(writer, 3, 0, 1, new[] { 0d, 0, 1 }, bits);
            PixelBuffer image = await Render(Page(Mesh(4, writer.Data(), coordinateBits: bits)));
            Rgba32 sample = At(image, 50, 25);
            Assert.That(sample.R, Is.EqualTo(125).Within(3));
            Assert.That(sample.G, Is.EqualTo(64).Within(3));
            Assert.That(sample.B, Is.EqualTo(65).Within(3));
            Assert.That(At(image, 190, 90), Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(12)]
        [TestCase(16)]
        public async Task LatticeSupportsEveryComponentBitWidth(int bits)
        {
            Bits? writer = new Bits();
            foreach ((double, double) p in new[] { (0d, 0d), (1d, 0d), (0d, 1d), (1d, 1d) })
            {
                Vertex(writer, 0, p.Item1, p.Item2, new[] { p.Item1, p.Item2, 0d }, componentBits: bits, flags: 0);
            }

            PixelBuffer image = await Render(Page(Mesh(5, writer.Data(), componentBits: bits, entries: "/VerticesPerRow 2")));
            Rgba32 sample = At(image, 100, 50);
            Assert.That(sample.R, Is.EqualTo(128).Within(3));
            Assert.That(sample.G, Is.EqualTo(129).Within(3));
            Assert.That(sample.B, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(2)]
        public async Task TriangleContinuationReusesTheSpecifiedPreviousEdge(int flag)
        {
            Bits? writer = new Bits();
            Vertex(writer, 0, 0, 0, new[] { 1d, 0, 0 });
            Vertex(writer, 0, 1, 0, new[] { 1d, 0, 0 });
            Vertex(writer, 0, 0, 1, new[] { 1d, 0, 0 });
            Vertex(writer, flag, 1, 1, new[] { 1d, 0, 0 });
            PixelBuffer image = await Render(Page(Mesh(4, writer.Data())));
            Assert.That(At(image, flag == 1 ? 160 : 50, 80), Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [TestCase(6)]
        [TestCase(7)]
        public async Task RectangularBezierPatchesPreserveCornerColors(int type)
        {
            PixelBuffer image = await Render(Page(Mesh(type, Patch(type))));
            Assert.That(At(image, 2, 2).R, Is.GreaterThan(240));
            Assert.That(At(image, 2, 97).G, Is.GreaterThan(240));
            Assert.That(At(image, 197, 97).B, Is.GreaterThan(240));
            Assert.That(At(image, 197, 2).R, Is.GreaterThan(240));
            Rgba32 middle = At(image, 100, 50);
            Assert.That(middle.R, Is.EqualTo(128).Within(3));
            Assert.That(middle.G, Is.EqualTo(128).Within(3));
            Assert.That(middle.B, Is.EqualTo(64).Within(3));
        }

        [TestCase(6, 1)]
        [TestCase(6, 2)]
        [TestCase(6, 3)]
        [TestCase(7, 1)]
        [TestCase(7, 2)]
        [TestCase(7, 3)]
        public async Task PatchContinuationsMatchExplicitIndependentPatches(int type, int flag)
        {
            double[][] points = PatchPoints(type);
            double[][] colors = PatchColors();
            double[][] next = PatchPoints(type);
            double[][] nextColors = PatchColors();
            for (int i = 0; i < 4; i++)
            {
                next[i] = points[((flag * 3) + i) % 12];
            }

            nextColors[0] = colors[flag];
            nextColors[1] = colors[(flag + 1) % 4];
            for (int i = 4; i < next.Length; i++)
            {
                next[i] = new[] { next[i][0] + 0.15, next[i][1] + 0.1 };
            }

            Bits? continued = new Bits();
            WritePatch(continued, points, colors, 0);
            WritePatch(continued, next, nextColors, flag);
            Bits? independent = new Bits();
            WritePatch(independent, points, colors, 0);
            WritePatch(independent, next, nextColors, 0);
            PixelBuffer actual = await Render(Page(Mesh(type, continued.Data())));
            PixelBuffer expected = await Render(Page(Mesh(type, independent.Data())));
            Assert.That(actual.Pixels.ToArray(), Is.EqualTo(expected.Pixels.ToArray()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BackgroundAppliesToShadingPatternsAndIsIgnoredBySh(bool pattern)
        {
            byte[] shader = PdfTestDocument.Bytes("<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] /Matrix [50 0 0 50 0 0] "
                + "/Background [0 1 0] /Function 6 0 R >>");
            byte[] function = PdfTestDocument.Stream(PdfTestDocument.Bytes("{ pop pop 1 0 0 }"),
                "/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]");
            byte[] data = pattern ? PdfTestDocument.Create("/Pattern cs /P scn 0 0 200 100 re f",
                resources: "/Pattern << /P 7 0 R >>", extraObjects: new[] { shader, function, PdfTestDocument.Bytes("<< /PatternType 2 /Shading 5 0 R >>") })
                : Page(shader, extra: new[] { function });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 25, 25), Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(At(image, 100, 25), Is.EqualTo(pattern ? new Rgba32(0, 255, 0) : new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task MeshFunctionsRunAfterParameterInterpolation()
        {
            Bits? writer = new Bits();
            Vertex(writer, 0, 0, 0, new[] { 0d });
            Vertex(writer, 0, 1, 0, new[] { 1d });
            Vertex(writer, 0, 0, 1, new[] { 1d });
            string function = "/Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 0 0] /N 2 >>";
            PixelBuffer image = await Render(Page(Mesh(4, writer.Data(), decode: "0 200 0 100 0 1", entries: function)));
            Assert.That(At(image, 50, 25).R, Is.EqualTo(66).Within(3));
            Assert.That(At(image, 50, 25).G, Is.Zero);
        }

        [Test]
        public async Task MeshOpacityAppliesOnceAtSharedEdgesAndOverlappingTriangles()
        {
            Bits? writer = new Bits();
            for (int i = 0; i < 2; i++)
            {
                Vertex(writer, 0, 0, 0, new[] { 1d, 0, 0 });
                Vertex(writer, 0, 1, 0, new[] { 1d, 0, 0 });
                Vertex(writer, 0, 0, 1, new[] { 1d, 0, 0 });
            }

            PixelBuffer image = await Render(Page(Mesh(4, writer.Data()), paint: "/Half gs /S sh", resources: "/ExtGState << /Half << /ca 0.5 >> >>"));
            Assert.That(At(image, 50, 25).G, Is.EqualTo(128).Within(1));
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public async Task TruncatedMeshRecordsFailInsteadOfPartialRendering(int type)
        {
            byte[] stream = type >= 6 ? Patch(type)[..^1] : new byte[] { 0, 0 };
            using PdfDocument document = await Read(Page(Mesh(type, stream, entries: type == 5 ? "/VerticesPerRow 2" : "")));
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task MeshGeometryBudgetIsEnforced()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Page(Mesh(6, Patch(6)))), new PdfReaderOptions { MaximumPathCommands = 10 });
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task ShadingStreamBudgetIsEnforcedDuringPageInterpretation()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Page(Mesh(6, Patch(6)))), new PdfReaderOptions { MaximumShadingBytes = 1 });
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task AuthoredShadingFixtureRendersAfterDisposalAndConcurrently()
        {
            string file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "shadings.pdf");
            PdfPage page;
            using (PdfDocument document = await PdfReader.ReadFileAsync(file))
            {
                page = document.GetPage(1);
                Assert.That(page.UnsupportedFeatures, Is.Empty);
            }

            PixelBuffer[] images = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => PdfRenderer.Render(page, Exact))));
            Assert.That(images.All(image => image.Pixels.Span.SequenceEqual(images[0].Pixels.Span)), Is.True);
        }

        private static byte[] Patch(int type)
        {
            Bits? writer = new Bits();
            WritePatch(writer, PatchPoints(type), PatchColors(), 0);
            return writer.Data();
        }

        private static double[][] PatchPoints(int type)
        {
            double[][] points =
            {
                new[] { 0d, 0 }, new[] { 0d, 1d / 3 }, new[] { 0d, 2d / 3 }, new[] { 0d, 1 },
                new[] { 1d / 3, 1 }, new[] { 2d / 3, 1 }, new[] { 1d, 1 }, new[] { 1d, 2d / 3 },
                new[] { 1d, 1d / 3 }, new[] { 1d, 0 }, new[] { 2d / 3, 0 }, new[] { 1d / 3, 0 },
                new[] { 1d / 3, 1d / 3 }, new[] { 1d / 3, 2d / 3 }, new[] { 2d / 3, 2d / 3 }, new[] { 2d / 3, 1d / 3 }
            };
            return points.Take(type == 6 ? 12 : 16).ToArray();
        }

        private static double[][] PatchColors() => new[] { new[] { 1d, 0, 0 }, new[] { 0d, 1, 0 }, new[] { 0d, 0, 1 }, new[] { 1d, 1, 0 } };

        private static void WritePatch(Bits writer, double[][] points, double[][] colors, int flag)
        {
            writer.Add((uint)flag, 2);
            foreach (double[] point in points.Skip(flag == 0 ? 0 : 4))
            {
                writer.Sample(point[0], 8);
                writer.Sample(point[1], 8);
            }

            foreach (double[] color in colors.Skip(flag == 0 ? 0 : 2))
            {
                foreach (double channel in color)
                {
                    writer.Sample(channel, 8);
                }
            }

            writer.Align();
        }

        private static void Vertex(Bits writer, int flag, double x, double y, double[] color, int coordinateBits = 8, int componentBits = 8, int flags = 2)
        {
            writer.Add((uint)flag, flags);
            writer.Sample(x, coordinateBits);
            writer.Sample(y, coordinateBits);
            foreach (double c in color)
            {
                writer.Sample(c, componentBits);
            }

            writer.Align();
        }

        private static byte[] Mesh(int type, byte[] bytes, int coordinateBits = 8, int componentBits = 8,
            string decode = "0 200 0 100 0 1 0 1 0 1", string entries = "")
            => PdfTestDocument.Stream(bytes, $"/ShadingType {type} /ColorSpace /DeviceRGB /BitsPerCoordinate {coordinateBits} "
                + $"/BitsPerComponent {componentBits} /BitsPerFlag 2 /Decode [{decode}] {entries}");
        private static byte[] Page(byte[] shading, string paint = "/S sh", string resources = "", byte[][]? extra = null)
            => PdfTestDocument.Create(paint, resources: "/Shading << /S 5 0 R >> " + resources,
                extraObjects: new[] { shading }.Concat(extra ?? Array.Empty<byte[]>()).ToArray());
        private static Rgba32 At(PixelBuffer image, int x, int y) => image[x, image.Height - 1 - y];
        private static async Task<PdfDocument> Read(byte[] data) => await PdfReader.ReadAsync(new MemoryStream(data));
        private static async Task<PixelBuffer> Render(byte[] data)
        {
            using PdfDocument document = await Read(data);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, Exact);
        }

        private class Bits
        {
            private readonly List<byte> _bytes = new();
            private int _bit;
            internal void Sample(double value, int bits) => Add((uint)Math.Round(value * ((1UL << bits) - 1)), bits);
            internal void Add(uint value, int count)
            {
                for (int i = count - 1; i >= 0; i--, _bit++)
                {
                    if ((_bit & 7) == 0)
                    {
                        _bytes.Add(0);
                    }

                    _bytes[^1] |= (byte)(((value >> i) & 1) << (7 - (_bit & 7)));
                }
            }

            internal void Align() => _bit = (_bit + 7) & ~7;
            internal byte[] Data() => _bytes.ToArray();
        }
    }
}
