using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfRendererTests
    {
        private static readonly PdfRenderOptions Exact = new() { Dpi = 72, Supersampling = 1 };

        [Test]
        public async Task PreservesPaintingOrderAndClipStateAcrossPages()
        {
            using PdfDocument document = await Fixture("graphics.pdf");
            Assert.That(document.PageCount, Is.EqualTo(2));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[20, 180], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[50, 150], Is.EqualTo(new Rgba32(0, 0, 255)));
            Assert.That(image[120, 170], Is.EqualTo(new Rgba32(0, 255, 0)));
            Assert.That(image[105, 170], Is.EqualTo(new Rgba32(255, 255, 255)));
            PixelBuffer second = PdfRenderer.Render(document.GetPage(2), Exact);
            Assert.That(second[30, 30].R, Is.InRange(127, 128));
            Assert.That(second[30, 30].B, Is.InRange(127, 128));
            Assert.That(second[170, 30], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public async Task EmbeddedAndStandardFontsProduceOutlinesWithoutSystemFonts()
        {
            using PdfDocument document = await Fixture("text.pdf");
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            Assert.That(page.SubstitutedFonts, Is.EqualTo(new[] { "Helvetica" }));
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.That(Ink(image, 20, 25, 440, 35), Is.GreaterThan(1500));
            Assert.That(Ink(image, 20, 225, 420, 30), Is.GreaterThan(800));
        }

        [Test]
        public async Task JpegInlineFlateAndSoftMaskedImagesRenderWithCorrectPlacement()
        {
            using PdfDocument document = await Fixture("images.pdf");
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[20, 130].R, Is.GreaterThan(245));
            Assert.That(image[75, 130].B, Is.GreaterThan(245));
            Assert.That(image[20, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[150, 130].R, Is.InRange(81, 83));
            Assert.That(image[150, 130].G, Is.InRange(114, 116));
            Assert.That(image[150, 130].B, Is.InRange(152, 155));
        }

        [TestCase(0, 160, 80, 10, 70)]
        [TestCase(90, 80, 160, 10, 10)]
        [TestCase(180, 160, 80, 150, 10)]
        [TestCase(270, 80, 160, 70, 150)]
        public async Task AppliesCropOriginAndPageRotation(int rotation, int width, int height, int x, int y)
        {
            byte[] bytes = PdfTestDocument.Create("1 0 0 rg 20 10 20 20 re f", "/CropBox [20 10 180 90] /Rotate " + rotation);
            using PdfDocument document = await Read(bytes);
            PdfPage page = document.GetPage(1);
            Assert.That(page.Width, Is.EqualTo(width));
            Assert.That(page.Height, Is.EqualTo(height));
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.That(image[x, y], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [Test]
        public async Task AppliesUserUnitToGeometryAndPhysicalPageSize()
        {
            using PdfDocument document = await Read(PdfTestDocument.Create("1 0 0 rg 0 0 10 10 re f", "/UserUnit 2"));
            PdfPage page = document.GetPage(1);
            Assert.That(page.Width, Is.EqualTo(400));
            Assert.That(page.Height, Is.EqualTo(200));
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.That(image[15, 185], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[25, 185], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task EvenOddHolesAndEmptyClipAreRespected()
        {
            using PdfDocument document = await Read(PdfTestDocument.Create("1 0 0 rg 10 10 80 80 re 30 30 40 40 re f* q 0 0 0 0 re W n 0 0 1 rg 0 0 200 100 re f Q"));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[20, 20], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task FormsHaveTheirOwnTransformClipAndGraphicsState()
        {
            byte[] form = PdfTestDocument.Stream(PdfTestDocument.Bytes("0 0 1 rg -100 -100 300 300 re f"), "/Type /XObject /Subtype /Form /BBox [0 0 20 20] /Matrix [1 0 0 1 50 20]");
            using PdfDocument document = await Read(PdfTestDocument.Create("/Model Do 1 0 0 rg 0 0 10 10 re f", resources: "/XObject << /Model 5 0 R >>", extraObjects: form));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[55, 75], Is.EqualTo(new Rgba32(0, 0, 255)));
            Assert.That(image[45, 75], Is.EqualTo(new Rgba32(255, 255, 255)));
            Assert.That(image[5, 95], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [Test]
        public async Task TextClipAppliesAtEndTextAndPreservesGlyphHoles()
        {
            byte[] font = PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            using PdfDocument document = await Read(PdfTestDocument.Create("BT /F1 80 Tf 7 Tr 20 10 Td (O) Tj ET 0 0 1 rg 0 0 200 100 re f",
                resources: "/Font << /F1 5 0 R >>", extraObjects: font));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[30, 55].B, Is.EqualTo(255));
            Assert.That(image[50, 55], Is.EqualTo(new Rgba32(255, 255, 255)));
            Assert.That(Ink(image, 10, 5, 100, 90), Is.GreaterThan(500));
            Assert.That(image[150, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task StencilAndPackedGrayscaleSamplesRespectDecodeArrays()
        {
            byte[] mask = PdfTestDocument.Stream(new byte[] { 0b01010101 }, "/Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true /BitsPerComponent 1 /Decode [1 0]");
            using PdfDocument document = await Read(PdfTestDocument.Create("1 0 0 rg q 160 0 0 20 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: mask));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[10, 90], Is.EqualTo(new Rgba32(255, 255, 255)));
            Assert.That(image[30, 90], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [Test]
        public async Task UnsupportedFeaturesRequireExplicitPartialRendering()
        {
            using PdfDocument document = await Fixture("unsupported.pdf");
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Not.Empty);
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(page, Exact));
            Assert.DoesNotThrow(() => PdfRenderer.Render(page, Exact with { AllowPartialRendering = true }));
        }

        [Test]
        public async Task DetachedPagesAreDeterministicAndCanRenderAfterDisposal()
        {
            PdfPage page;
            using (PdfDocument document = await Fixture("preview.pdf"))
            {
                page = document.GetPage(1);
            }

            PixelBuffer[] images = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => PdfRenderer.Render(page, Exact with { MaximumWidth = 180 }))));
            Assert.That(images[0].Pixels.ToArray(), Is.EqualTo(images[1].Pixels.ToArray()));
            Assert.That(images[1].Pixels.ToArray(), Is.EqualTo(images[2].Pixels.ToArray()));
        }

        [Test]
        public async Task ResolutionFitsProportionallyAndTransparentAntialiasingUsesStraightAlpha()
        {
            using PdfDocument document = await Read(PdfTestDocument.Create("1 0 0 rg 10.25 10.25 40 40 re f"));
            PdfPage page = document.GetPage(1);
            PixelBuffer small = PdfRenderer.Render(page, Exact with { Dpi = 144, MaximumWidth = 100, MaximumHeight = 100 });
            Assert.That((small.Width, small.Height), Is.EqualTo((100, 50)));
            PixelBuffer alpha = PdfRenderer.Render(page, Exact with { Background = default, Supersampling = 4 });
            Assert.That(alpha[10, 89].R, Is.EqualTo(255));
            Assert.That(alpha[10, 89].A, Is.InRange(1, 254));
            Assert.That(alpha[0, 0], Is.EqualTo(default(Rgba32)));
        }

        [Test]
        public async Task RejectsInputOperationImageAndSampleLimits()
        {
            byte[] data = PdfTestDocument.Create("0 0 m 10 10 l S");
            await Assert.ThrowsAsync<InvalidDataException>(async () => await Read(data, new PdfReaderOptions { MaximumInputBytes = data.Length - 1 }));
            using PdfDocument limited = await Read(data, new PdfReaderOptions { MaximumOperations = 1 });
            Assert.Throws<InvalidDataException>(() => limited.GetPage(1));
            using PdfDocument document = await Read(data);
            PdfPage page = document.GetPage(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderer.Render(page, Exact with { MaximumSamples = 10 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderer.Render(page, Exact with { Dpi = double.NaN }));
            using PdfDocument images = await PdfReader.ReadFileAsync(FixturePath("images.pdf"), new PdfReaderOptions { MaximumImagePixels = 1 });
            Assert.Throws<InvalidDataException>(() => images.GetPage(1));
        }

        [Test]
        public async Task PageIndexesAndCancellationAreChecked()
        {
            using PdfDocument document = await Fixture("graphics.pdf");
            Assert.Throws<ArgumentOutOfRangeException>(() => document.GetPage(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => document.GetPage(3));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => document.GetPage(1, cancellation.Token));
            Assert.Throws<OperationCanceledException>(() => PdfRenderer.Render(document.GetPage(1), Exact, cancellation.Token));
            using var input = File.OpenRead(FixturePath("graphics.pdf"));
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await PdfReader.ReadAsync(input, cancellationToken: cancellation.Token));
            Assert.That(input.CanRead, Is.True);
        }

        [Test]
        public void FixtureChecksumsMatchCommittedManifest()
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(FixturePath("manifest.json")));
            foreach (JsonElement fixture in manifest.RootElement.GetProperty("files").EnumerateArray())
            {
                byte[] data = File.ReadAllBytes(FixturePath(fixture.GetProperty("file").GetString()!));
                Assert.That(data.Length, Is.EqualTo(fixture.GetProperty("bytes").GetInt32()));
                Assert.That(Convert.ToHexStringLower(SHA256.HashData(data)), Is.EqualTo(fixture.GetProperty("sha256").GetString()));
            }
        }

        [Test]
        public async Task ReadsPasswordProtectedDocument()
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(FixturePath("encrypted.pdf"), new PdfReaderOptions { Password = "render-test" });
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[20, 80], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [Test]
        public async Task ReadsCurrentStreamPositionAndNonSeekableInputWithoutClosingEither()
        {
            byte[] data = PdfTestDocument.Create("1 0 0 rg 0 0 10 10 re f");
            using var input = new MemoryStream();
            input.Write(new byte[17]);
            input.Write(data);
            input.Position = 17;
            using (PdfDocument document = await PdfReader.ReadAsync(input))
            {
                Assert.That(document.PageCount, Is.EqualTo(1));
            }

            Assert.That(input.CanRead, Is.True);
            input.Position = 17;
            using var nonSeekable = new StlReaderTests.NonSeekableStream(input);
            using PdfDocument second = await PdfReader.ReadAsync(nonSeekable);
            Assert.That(second.PageCount, Is.EqualTo(1));
            Assert.That(nonSeekable.CanRead, Is.True);
        }

        [Test]
        public async Task CallerCanResolveANonEmbeddedFont()
        {
            int calls = 0;
            string fontFile = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "../../../../../Sources/Render.NET.Pdf/Fonts/Aileron-Regular.ttf"));
            var options = new PdfReaderOptions
            {
                FontResolver = name =>
                {
                    Assert.That(name, Is.EqualTo("Helvetica"));
                    calls++;
                    return File.ReadAllBytes(fontFile);
                }
            };
            using PdfDocument document = await PdfReader.ReadFileAsync(FixturePath("text.pdf"), options);
            PdfPage page = document.GetPage(1);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(page.UnsupportedFeatures, Is.Empty);
        }

        [TestCase(2, "0 0 0 0 10 10 cm 0 0 1 1 re f")]
        [TestCase(1, "0 0 m 10 10 l S")]
        public async Task StoredPathBudgetIncludesPaintedGeometry(int limit, string content)
        {
            using PdfDocument document = await Read(PdfTestDocument.Create(content), new PdfReaderOptions { MaximumPathCommands = limit });
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task PackedIndexedSamplesAndColorKeyMasksAreDecoded()
        {
            byte[] picture = PdfTestDocument.Stream(new byte[] { 0b01010000 }, "/Type /XObject /Subtype /Image /Width 4 /Height 1 /ColorSpace [/Indexed /DeviceRGB 1 <FF00000000FF>] /BitsPerComponent 1 /Mask [0 0]");
            using PdfDocument document = await Read(PdfTestDocument.Create("q 160 0 0 20 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: picture));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[10, 90], Is.EqualTo(new Rgba32(255, 255, 255)));
            Assert.That(image[50, 90], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public async Task SixteenBitGrayscaleUsesBigEndianSamples()
        {
            byte[] picture = PdfTestDocument.Stream(new byte[] { 0, 0, 128, 0, 255, 255 }, "/Type /XObject /Subtype /Image /Width 3 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 16");
            using PdfDocument document = await Read(PdfTestDocument.Create("q 150 0 0 20 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: picture));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), Exact);
            Assert.That(image[10, 90], Is.EqualTo(new Rgba32(0, 0, 0)));
            Assert.That(image[60, 90].R, Is.EqualTo(128));
            Assert.That(image[110, 90], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        private static int Ink(PixelBuffer image, int x, int y, int width, int height)
        {
            int count = 0;
            for (int row = y; row < y + height; row++)
            {
                for (int column = x; column < x + width; column++)
                {
                    if (image[column, row].R < 200)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static string FixturePath(string file) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", file);
        private static Task<PdfDocument> Fixture(string file) => PdfReader.ReadFileAsync(FixturePath(file));
        private static Task<PdfDocument> Read(byte[] bytes, PdfReaderOptions? options = null) => PdfReader.ReadAsync(new MemoryStream(bytes, false), options);
    }
}
