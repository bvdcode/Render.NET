using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfType3Tests
    {
        private static readonly PdfRenderOptions Exact = new() { Dpi = 72, Supersampling = 1 };
        private static readonly Rgba32 Red = new(255, 0, 0);
        private static readonly Rgba32 Green = new(0, 255, 0);
        private static readonly Rgba32 White = new(255, 255, 255);
        private const string Rectangle = "0 0 400 600 re f";
        private const string Text = "BT /F 100 Tf 10 10 Td (A) Tj ET";

        [Test]
        public async Task ColoredGlyphsUseTheirProgramsAndRestoreCallerColor()
        {
            PixelBuffer image = await Render(Page("600 0 d0 0 1 0 rg " + Rectangle,
                "1 0 0 rg " + Text + " 100 10 40 60 re f"));
            Assert.That(At(image, 20, 20), Is.EqualTo(Green));
            Assert.That(At(image, 110, 20), Is.EqualTo(Red));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public async Task TextModesDoNotAlterType3PaintingOrClipLaterObjects(int mode)
        {
            string content = $"1 0 0 rg BT /F 100 Tf {mode} Tr 10 10 Td (A) Tj ET 100 10 40 60 re f";
            PixelBuffer image = await Render(Page("600 0 0 0 400 600 d1 " + Rectangle, content));
            PixelBuffer reference = await Render(PdfTestDocument.Create("1 0 0 rg 10 10 40 60 re f 100 10 40 60 re f"));
            Assert.That(image.Pixels.ToArray(), Is.EqualTo(reference.Pixels.ToArray()));
        }

        [Test]
        public async Task InvisibleGlyphStillAdvancesWithoutExecutingItsProgram()
        {
            PixelBuffer image = await Render(Page("600 0 d0 1 0 0 rg " + Rectangle,
                "BT /F 100 Tf 10 10 Td 3 Tr (A) Tj 0 Tr (A) Tj ET"));
            Assert.That(At(image, 20, 20), Is.EqualTo(White));
            Assert.That(At(image, 80, 20), Is.EqualTo(Red));
        }

        [TestCase("0 1 0 rg")]
        [TestCase("/DeviceRGB cs 0 1 0 sc")]
        [TestCase("/DeviceRGB cs 0 1 0 scn")]
        [TestCase("/DeviceGray cs 0.5 g")]
        [TestCase("1 0 1 0 k")]
        [TestCase("/DoesNotExist cs /DoesNotExist sh")]
        public async Task StencilColorOperatorsAreIgnored(string color)
        {
            PixelBuffer image = await Render(Page("600 0 0 0 400 600 d1 " + color + " " + Rectangle, "1 0 0 rg " + Text));
            Assert.That(At(image, 20, 20), Is.EqualTo(Red));
        }

        [Test]
        public async Task StencilUsesSeparateInheritedFillAndStrokeColors()
        {
            byte[] data = Page("600 0 0 0 600 800 d1 0 1 0 RG 100 w 1 J 0 0 300 600 re f 450 100 m 450 600 l S",
                "1 0 0 rg 0 0 1 RG " + Text);
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 20, 20), Is.EqualTo(Red));
            Assert.That(At(image, 55, 30), Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [TestCase("0.001 0 0 0.001 0 0", "1 0 0 1 10 10", 100, 100, 0)]
        [TestCase("0.001 0.0003 -0.0002 0.001 0.1 0", "1 0 0 1 20 10", 60, 130, 7)]
        [TestCase("0 0.001 -0.001 0 0 0", "1 0 0 1 150 10", 90, 80, 0)]
        public async Task FontAndTextMatricesMatchEquivalentVectorGeometry(string fontMatrix, string textMatrix, int size, int scale, int rise)
        {
            string text = FormattableString.Invariant($"BT /F {size} Tf {scale} Tz {rise} Ts {textMatrix} Tm (A) Tj ET");
            PixelBuffer actual = await Render(Page("600 0 d0 1 0 0 rg " + Rectangle, text, matrix: fontMatrix));
            Matrix3x2 transform = Matrix(fontMatrix) * new Matrix3x2(size * scale / 100f, 0, 0, size, 0, rise) * Matrix(textMatrix);
            string cm = FormattableString.Invariant($"{transform.M11:R} {transform.M12:R} {transform.M21:R} {transform.M22:R} {transform.M31:R} {transform.M32:R} cm");
            PixelBuffer expected = await Render(PdfTestDocument.Create("q " + cm + " 1 0 0 rg " + Rectangle + " Q"));
            Assert.That(actual.Pixels.ToArray(), Is.EqualTo(expected.Pixels.ToArray()));
        }

        [Test]
        public async Task CharacterSpacingAndTextAdjustmentsUseFontWidths()
        {
            PixelBuffer image = await Render(Page("600 0 d0 " + Rectangle,
                "1 0 0 rg BT /F 50 Tf 5 Tc 10 10 Td [(A) 200 (AA)] TJ ET"));
            foreach (int x in new[] { 15, 40, 75 })
            {
                Assert.That(At(image, x, 20), Is.EqualTo(Red));
            }

            Assert.That(At(image, 32, 20), Is.EqualTo(White));
            Assert.That(At(image, 62, 20), Is.EqualTo(White));
        }

        [Test]
        public async Task NestedGlyphsRestoreTextMatricesAndOuterPendingPath()
        {
            byte[] font = Font(resources: "/Resources << /Font << /Inner 7 0 R >> >>");
            byte[] inner = Font(procedure: 8);
            byte[] data = PdfTestDocument.Create("1 0 0 rg 0 90 m BT /F 50 Tf 10 10 Td (AA) Tj ET 200 90 l 2 w S",
                resources: "/Font << /F 5 0 R >>", extraObjects: new[]
                {
                    font, Procedure("600 0 d0 BT /Inner 1000 Tf 0 0 Td (A) Tj ET"), inner, Procedure("600 0 d0 " + Rectangle)
                });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 15, 20), Is.EqualTo(Red));
            Assert.That(At(image, 45, 20), Is.EqualTo(Red));
            Assert.That(At(image, 100, 90), Is.EqualTo(new Rgba32(0, 0, 0)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GlyphImageResourcesResolveLocallyOrFromThePage(bool local)
        {
            string glyphResources = local ? "/Resources << /XObject << /Im 7 0 R >> >>" : "";
            string pageResources = local ? "/XObject << /Im 8 0 R >>" : "/XObject << /Im 7 0 R >>";
            byte[] data = Page("600 0 d0 q 400 0 0 600 0 0 cm /Im Do Q", Text, glyphResources: glyphResources,
                resources: pageResources, extra: new[] { Image(new byte[] { 255, 0, 0 }), Image(new byte[] { 0, 255, 0 }) });
            Assert.That(At(await Render(data), 20, 20), Is.EqualTo(Red));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingFontResourcesFallBackToPageEvenInsideAForm(bool inherited)
        {
            byte[] form = PdfTestDocument.Stream(PdfTestDocument.Bytes(Text),
                "/Type /XObject /Subtype /Form /BBox [0 0 200 100] /Resources << /Font << /F 5 0 R >> /XObject << /Im 8 0 R >> >>");
            const string resources = "/Resources << /Font << /F 5 0 R >> /XObject << /Im 7 0 R /Form 9 0 R >> >>";
            byte[] data = PdfTestDocument.Write(new List<byte[]>
            {
                PdfTestDocument.Bytes("<< /Type /Catalog /Pages 2 0 R >>"),
                PdfTestDocument.Bytes("<< /Type /Pages /Count 1 /Kids [3 0 R] " + (inherited ? resources : "") + " >>"),
                PdfTestDocument.Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents 4 0 R " + (inherited ? "" : resources) + " >>"),
                Procedure("/Form Do"), Font(), Procedure("600 0 d0 q 400 0 0 600 0 0 cm /Im Do Q"),
                Image(new byte[] { 255, 0, 0 }), Image(new byte[] { 0, 255, 0 }), form
            });
            Assert.That(At(await Render(data), 20, 20), Is.EqualTo(Red));
        }

        [Test]
        public async Task StencilImageMaskUsesInheritedColorAndRestoresClip()
        {
            byte[] mask = PdfTestDocument.Stream(new byte[] { 0 }, "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ImageMask true /BitsPerComponent 1");
            byte[] data = Page("600 0 0 0 400 600 d1 q 400 0 0 600 0 0 cm /Im Do Q",
                "1 0 0 rg " + Text + " 100 10 40 60 re f", resources: "/XObject << /Im 7 0 R >>", extra: new[] { mask });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 20, 20), Is.EqualTo(Red));
            Assert.That(At(image, 110, 20), Is.EqualTo(Red));
        }

        [Test]
        public async Task StencilRejectsOrdinaryColorImages()
        {
            byte[] data = Page("600 0 0 0 400 600 d1 /Im Do", resources: "/XObject << /Im 7 0 R >>",
                extra: new[] { Image(new byte[] { 255, 0, 0 }) });
            await Reject(data, "color image");
        }

        [Test]
        public async Task GlyphInheritsOpacityAndSoftMask()
        {
            string gs = "<< /ca 0.5 /SMask << /S /Alpha /G 8 0 R >> >>";
            byte[] mask = PdfTestDocument.Stream(PdfTestDocument.Bytes("1 g 0 0 30 100 re f"),
                "/Type /XObject /Subtype /Form /BBox [0 0 200 100] /Group << /S /Transparency >> /Resources << >>");
            byte[] data = Page("600 0 d0 " + Rectangle, "1 0 0 rg /GS gs " + Text,
                resources: "/ExtGState << /GS 7 0 R >>", extra: new[] { PdfTestDocument.Bytes(gs), mask });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 20, 20).G, Is.EqualTo(128).Within(1));
            Assert.That(At(image, 40, 20), Is.EqualTo(White));
        }

        [Test]
        public async Task MissingCharProcIsEmptyAndDoesNotUseFallbackFonts()
        {
            string font = System.Text.Encoding.Latin1.GetString(Font()).Replace("/LastChar 65 /Widths [600]", "/LastChar 66 /Widths [600 700]")
                .Replace("/Differences [65 /A]", "/Differences [65 /A /Missing]");
            byte[] data = PdfTestDocument.Create("BT /F 100 Tf 10 10 Td (BA) Tj ET", resources: "/Font << /F 5 0 R >>",
                extraObjects: new[] { PdfTestDocument.Bytes(font), Procedure("600 0 d0 " + Rectangle) });
            using PdfDocument document = await Read(data);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            Assert.That(page.SubstitutedFonts, Is.Empty);
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.That(At(image, 20, 20), Is.EqualTo(White));
            Assert.That(At(image, 90, 20), Is.EqualTo(new Rgba32(0, 0, 0)));
        }

        [Test]
        public async Task FontMatrixTranslationDoesNotChangeAdvanceWidth()
        {
            PixelBuffer actual = await Render(Page("600 0 d0 " + Rectangle,
                "1 0 0 rg BT /F 100 Tf 10 10 Td (AA) Tj ET", matrix: "0.001 0 0 0.001 0.1 0"));
            PixelBuffer expected = await Render(PdfTestDocument.Create("1 0 0 rg 20 10 40 60 re f 80 10 40 60 re f"));
            Assert.That(actual.Pixels.ToArray(), Is.EqualTo(expected.Pixels.ToArray()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InheritedPatternGridStaysAnchoredToPageAcrossGlyphs(bool mask)
        {
            byte[] tile = PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 0 0 5 10 re f"),
                "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 2 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << >>");
            string paint = mask ? "q 400 0 0 600 0 0 cm /Im Do Q" : Rectangle;
            byte[] maskImage = PdfTestDocument.Stream(new byte[] { 0 }, "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ImageMask true /BitsPerComponent 1");
            byte[] data = Page("600 0 0 0 400 600 d1 " + paint,
                "/Pattern cs /P scn BT /F 100 Tf 13 10 Td (AA) Tj ET", resources: "/Pattern << /P 7 0 R >> /XObject << /Im 8 0 R >>",
                extra: new[] { tile, maskImage });
            byte[] reference = PdfTestDocument.Create("/Pattern cs /P scn 13 10 40 60 re f 73 10 40 60 re f",
                resources: "/Pattern << /P 5 0 R >>", extraObjects: tile);
            Assert.That((await Render(data)).Pixels.ToArray(), Is.EqualTo((await Render(reference)).Pixels.ToArray()));
        }

        [Test]
        public async Task CompressedCharProcIsDecodedWithItsFilter()
        {
            using var compressed = new MemoryStream();
            using (var zip = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            {
                zip.Write(PdfTestDocument.Bytes("600 0 d0 1 0 0 rg " + Rectangle));
            }

            byte[] data = PdfTestDocument.Create(Text, resources: "/Font << /F 5 0 R >>",
                extraObjects: new[] { Font(), PdfTestDocument.Stream(compressed.ToArray(), "/Filter /FlateDecode") });
            Assert.That(At(await Render(data), 20, 20), Is.EqualTo(Red));
        }

        [TestCase("")]
        [TestCase("0 0 400 600 re f")]
        [TestCase("600 1 d0 0 0 400 600 re f")]
        [TestCase("600 1 0 0 400 600 d1 0 0 400 600 re f")]
        [TestCase("600 0 400 0 0 600 d1")]
        [TestCase("600 0 d0 600 0 d0")]
        public async Task MalformedGlyphProgramsFail(string program)
            => await Reject(Page(program), "Type 3");

        [TestCase("600 0 d0")]
        [TestCase("600 0 0 0 400 600 d1")]
        public async Task GlyphMetricsOutsideCharProcsFail(string program)
            => await Reject(PdfTestDocument.Create(program), "outside");

        [Test]
        public async Task DirectRecursiveGlyphFailsWithBoundedError()
            => await Reject(Page("600 0 d0 BT /F 1000 Tf (A) Tj ET"), "Recursive Type 3");

        [Test]
        public async Task IndirectRecursionThroughAFormFailsWithBoundedError()
        {
            byte[] form = PdfTestDocument.Stream(PdfTestDocument.Bytes("BT /F 1000 Tf (A) Tj ET"),
                "/Type /XObject /Subtype /Form /BBox [0 0 600 600]");
            await Reject(Page("600 0 d0 /Form Do", resources: "/XObject << /Form 7 0 R >>", extra: new[] { form }), "Recursive Type 3");
        }

        [Test]
        public async Task MetricsInsideAnOrdinaryNestedFormAreRejected()
        {
            byte[] form = PdfTestDocument.Stream(PdfTestDocument.Bytes("600 0 d0 " + Rectangle),
                "/Type /XObject /Subtype /Form /BBox [0 0 600 600]");
            await Reject(Page("600 0 d0 /Form Do", resources: "/XObject << /Form 7 0 R >>", extra: new[] { form }), "outside");
        }

        [Test]
        public async Task ExtendedGraphicsStateFontIsRestoredAfterNestedGlyphText()
        {
            byte[] data = PdfTestDocument.Create("/Outer gs BT 10 10 Td (A) Tj ET BT 10 50 Td (A) Tj ET",
                resources: "/ExtGState << /Outer 10 0 R >>", extraObjects: new[]
                {
                    Font(resources: "/Resources << /ExtGState << /Inner 7 0 R >> >>"),
                    Procedure("600 0 d0 1 0 0 rg " + Rectangle + " /Inner gs BT (A) Tj ET"),
                    PdfTestDocument.Bytes("<< /Font [8 0 R 1000] >>"), Font(procedure: 9),
                    Procedure("600 0 d0 0 0 1 rg 0 0 200 600 re f"), PdfTestDocument.Bytes("<< /Font [5 0 R 50] >>")
                });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 25, 20), Is.EqualTo(Red));
            Assert.That(At(image, 25, 60), Is.EqualTo(Red));
        }

        [Test]
        public async Task InheritedAlphaMatchesEquivalentVectorOperations()
        {
            const string body = "1 0 0 rg 0 0 400 600 re f 0 1 0 rg 200 0 400 600 re f";
            byte[] gs = PdfTestDocument.Bytes("<< /ca 0.5 >>");
            PixelBuffer actual = await Render(Page("600 0 d0 " + body, "/GS gs " + Text,
                resources: "/ExtGState << /GS 7 0 R >>", extra: new[] { gs }));
            PixelBuffer expected = await Render(PdfTestDocument.Create("/GS gs q 0.1 0 0 0.1 10 10 cm " + body + " Q",
                resources: "/ExtGState << /GS 5 0 R >>", extraObjects: gs));
            Assert.That(actual.Pixels.ToArray(), Is.EqualTo(expected.Pixels.ToArray()));
        }

        [Test]
        public async Task AuthoredFixtureRendersColoredStencilNestedAndImageGlyphs()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "type3.pdf");
            using PdfDocument document = await PdfReader.ReadFileAsync(path);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            Assert.That(page.SubstitutedFonts, Is.EqualTo(new[] { "Helvetica" }));
            PixelBuffer image = PdfRenderer.Render(page, new PdfRenderOptions { Dpi = 96, Supersampling = 2 });
            Assert.That((image.Width, image.Height), Is.EqualTo((800, 587)));
            Assert.That(image[155, 220].G, Is.GreaterThan(200));
            Assert.That(image[100, 520].G, Is.GreaterThan(150));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task GlyphOperationsAndGeometryCountAgainstPageLimits(bool operations)
        {
            using PdfDocument document = await Read(Page("600 0 d0 " + Rectangle, "BT /F 10 Tf (AAAAAAAAAA) Tj ET"),
                operations ? new PdfReaderOptions { MaximumOperations = 20 } : new PdfReaderOptions { MaximumPathCommands = 20 });
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task CapturedGlyphsRenderConcurrentlyAfterDocumentDisposal()
        {
            PdfPage page;
            using (PdfDocument document = await Read(Page("600 0 d0 1 0 0 rg " + Rectangle)))
            {
                page = document.GetPage(1);
            }

            PixelBuffer[] images = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => PdfRenderer.Render(page, Exact))));
            Assert.That(images.All(image => image.Pixels.Span.SequenceEqual(images[0].Pixels.Span)), Is.True);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => PdfRenderer.Render(page, Exact, cancellation.Token));
        }

        private static byte[] Page(string program, string content = Text, string matrix = "0.001 0 0 0.001 0 0",
            string glyphResources = "", string resources = "", byte[][]? extra = null)
            => PdfTestDocument.Create(content, resources: "/Font << /F 5 0 R >> " + resources,
                extraObjects: new[] { Font(matrix: matrix, resources: glyphResources), Procedure(program) }.Concat(extra ?? Array.Empty<byte[]>()).ToArray());

        private static byte[] Font(int procedure = 6, string matrix = "0.001 0 0 0.001 0 0", string resources = "")
            => PdfTestDocument.Bytes($"<< /Type /Font /Subtype /Type3 /Name /OwnType3 /FontBBox [0 0 0 0] /FontMatrix [{matrix}] "
                + $"/CharProcs << /A {procedure} 0 R >> /Encoding << /Type /Encoding /Differences [65 /A] >> /FirstChar 65 /LastChar 65 /Widths [600] {resources} >>");

        private static byte[] Procedure(string text) => PdfTestDocument.Stream(PdfTestDocument.Bytes(text));
        private static byte[] Image(byte[] rgb) => PdfTestDocument.Stream(rgb,
            "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8");
        private static Rgba32 At(PixelBuffer image, int x, int y) => image[x, image.Height - 1 - y];

        private static Matrix3x2 Matrix(string values)
        {
            float[] n = values.Split(' ').Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            return new Matrix3x2(n[0], n[1], n[2], n[3], n[4], n[5]);
        }

        private static async Task<PdfDocument> Read(byte[] bytes, PdfReaderOptions? options = null)
            => await PdfReader.ReadAsync(new MemoryStream(bytes), options);

        private static async Task<PixelBuffer> Render(byte[] bytes)
        {
            using PdfDocument document = await Read(bytes);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, Exact);
        }

        private static async Task Reject(byte[] bytes, string message)
        {
            using PdfDocument document = await Read(bytes);
            var error = Assert.Throws<InvalidDataException>(() => document.GetPage(1));
            Assert.That(error!.Message, Does.Contain(message));
        }
    }
}
