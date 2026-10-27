using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfCompatibilityTests
    {
        private static readonly PdfRenderOptions Exact = new() { Dpi = 72, Supersampling = 1 };

        [Test]
        public async Task RendersJpeg2000AndJbig2ScansWithManagedCodecs()
        {
            string file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "scan-codecs.pdf");
            using PdfDocument document = await PdfReader.ReadFileAsync(file);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.That(image[20, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
            Assert.That(image[100, 50], Is.EqualTo(new Rgba32(64, 64, 64)));
            Assert.That(image[150, 50], Is.EqualTo(new Rgba32(0, 0, 0)));
            Assert.That(image[180, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task AxialGradientHonorsDomainExtendTransformAndClipping()
        {
            byte[] data = PdfTestDocument.Create("q 10 10 180 80 re W n /Shade sh Q", resources: "/Shading << /Shade 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [20 0 180 0] /Domain [2 4] /Extend [false false] /Function << /FunctionType 3 /Domain [2 4] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>] /Bounds [] /Encode [0 1] >> >>"));
            PixelBuffer image = await Render(data);
            Assert.That(image[15, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
            Assert.That(image[100, 50].R, Is.InRange(125, 128));
            Assert.That(image[100, 50].B, Is.InRange(127, 130));
            Assert.That(image[100, 5], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task RadialGradientRendersConcentricCirclesAndExtension()
        {
            byte[] data = PdfTestDocument.Create("/Shade sh", resources: "/Shading << /Shade 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [100 50 0 100 50 40] /Extend [true true] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >>"));
            PixelBuffer image = await Render(data);
            Assert.That(image[100, 50].R, Is.GreaterThan(245));
            Assert.That(image[120, 50].R, Is.InRange(120, 130));
            Assert.That(image[160, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public async Task CyrillicWithoutAnEmbeddedFontUsesBundledUnicodeOutlines()
        {
            byte[] data = PdfTestDocument.Create("BT /F 28 Tf 10 40 Td (ABCD) Tj ET", resources: "/Font << /F 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [65 /afii10017 /afii10018 /afii10019 /afii10020] >> >>"));
            using PdfDocument document = await Read(data);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            Assert.That(page.SubstitutedFonts, Does.Contain("Helvetica"));
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.That(image.Pixels.ToArray().Where((_, i) => i % 4 == 0).Count(v => v < 100), Is.GreaterThan(150));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GroupOpacityIsAppliedOnceToOverlappingChildren(bool isolated)
        {
            byte[] data = PdfTestDocument.Create("0 0 1 rg 0 0 200 100 re f /Half gs /Group Do", resources: "/ExtGState << /Half << /ca 0.5 >> >> /XObject << /Group 5 0 R >>",
                extraObjects: PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 10 10 100 80 re f 50 10 100 80 re f"),
                    "/Type /XObject /Subtype /Form /BBox [0 0 200 100] /Group << /S /Transparency /I " + (isolated ? "true" : "false") + " >>"));
            PixelBuffer image = await Render(data);
            Assert.That(image[20, 50].R, Is.InRange(127, 128));
            Assert.That(image[20, 50].B, Is.InRange(127, 128));
            Assert.That(image[70, 50], Is.EqualTo(image[20, 50]));
            Assert.That(image[180, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [TestCase("Alpha", 128)]
        [TestCase("Luminosity", 128)]
        public async Task GraphicsSoftMasksPreservePaintOrderAndActivationMatrix(string subtype, int red)
        {
            string maskPaint = subtype == "Alpha" ? "/Half gs 1 g 0 0 80 100 re f" : "0.5 g 0 0 80 100 re f";
            byte[] data = PdfTestDocument.Create("0 0 1 rg 0 0 200 100 re f q 1 0 0 1 20 0 cm /Mask gs 1 0 0 1 30 0 cm 1 0 0 rg -50 0 200 100 re f Q",
                resources: "/ExtGState << /Mask << /SMask << /S /" + subtype + " /G 5 0 R >> >> >>",
                extraObjects: PdfTestDocument.Stream(PdfTestDocument.Bytes(maskPaint), "/Type /XObject /Subtype /Form /BBox [0 0 80 100] /Resources << /ExtGState << /Half << /ca 0.5 >> >> >> /Group << /S /Transparency /CS /DeviceGray /I true >>"));
            PixelBuffer image = await Render(data);
            Assert.That(image[30, 50].R, Is.InRange(red - 1, red + 1));
            Assert.That(image[10, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
            Assert.That(image[130, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [TestCase("Yes", 0, 255)]
        [TestCase("Off", 255, 0)]
        public async Task WidgetAppearanceSelectsStateAndMapsItsBounds(string state, int red, int green)
        {
            byte[] data = PdfTestDocument.Create("0 0 1 rg 0 0 200 100 re f 0 0 1 1 re W n", "/Annots [5 0 R]", extraObjects: new[]
            {
                PdfTestDocument.Bytes("<< /Type /Annot /Subtype /Widget /FT /Btn /Rect [40 20 160 80] /AS /" + state + " /AP << /N << /Off 6 0 R /Yes 7 0 R >> >> >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 0 0 20 40 re f"), "/BBox [0 0 20 40] /Matrix [0 1 -1 0 40 0]"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes("0 1 0 rg 0 0 20 40 re f"), "/BBox [0 0 20 40] /Matrix [0 1 -1 0 40 0]")
            });
            PixelBuffer image = await Render(data);
            Assert.That(image[100, 50], Is.EqualTo(new Rgba32((byte)red, (byte)green, 0)));
            Assert.That(image[20, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public async Task FilledTextAppearanceRendersItsOwnFontResources()
        {
            byte[] data = PdfTestDocument.Create("", "/Annots [5 0 R]", extraObjects: new[]
            {
                PdfTestDocument.Bytes("<< /Subtype /Widget /FT /Tx /V (Filled) /Rect [20 20 180 80] /AP << /N 6 0 R >> >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes("0.9 g 0 0 160 60 re f 0 g BT /F 24 Tf 10 20 Td (Filled) Tj ET"),
                    "/BBox [0 0 160 60] /Resources << /Font << /F 7 0 R >> >>"),
                PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            });
            PixelBuffer image = await Render(data);
            Assert.That(image.Pixels.ToArray().Where((_, i) => i % 4 == 0).Count(v => v < 100), Is.GreaterThan(200));
        }

        [Test]
        public async Task ShadingPatternUsesItsMatrixAndClipsToTheFilledShape()
        {
            byte[] data = PdfTestDocument.Create("/Pattern cs /P scn 20 20 80 60 re f", resources: "/Pattern << /P 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /PatternType 2 /Matrix [1 0 0 1 20 0] /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 160 0] /Extend [true true] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >> >>"));
            PixelBuffer image = await Render(data);
            Assert.That(image[21, 50].R, Is.GreaterThan(250));
            Assert.That(image[99, 50].R, Is.InRange(125, 130));
            Assert.That(image[120, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [TestCase("Hue")]
        [TestCase("Saturation")]
        [TestCase("Color")]
        [TestCase("Luminosity")]
        public async Task NonSeparableBlendModesKeepTheCorrectLuminosity(string mode)
        {
            byte[] data = PdfTestDocument.Create("0.2 0.6 0.4 rg 0 0 200 100 re f /B gs 0.8 0.1 0.3 rg 0 0 100 100 re f",
                resources: "/ExtGState << /B << /BM /" + mode + " >> >>");
            PixelBuffer image = await Render(data);
            Rgba32 color = image[50, 50];
            double luminosity = (0.3 * color.R) + (0.59 * color.G) + (0.11 * color.B);
            double expected = mode == "Luminosity" ? 84.405 : 116.79;
            Assert.That(luminosity, Is.EqualTo(expected).Within(1));
            Assert.That(color, Is.Not.EqualTo(image[150, 50]));
        }

        [Test]
        public async Task Jbig2GlobalsAndChainedFiltersRoundTripTheAuthoredScanExactly()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf");
            using PdfDocument document = await PdfReader.ReadFileAsync(Path.Combine(root, "scan-globals.pdf"));
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            var source = StbImageSharp.ImageResult.FromMemory(await File.ReadAllBytesAsync(Path.Combine(root, "Scans", "source.png")),
                StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            Assert.That(image.Pixels.ToArray(), Is.EqualTo(source.Data));
        }

        [Test]
        public async Task DetachedCompatibilityPageRendersConcurrentlyWithMasksAndCyrillic()
        {
            PdfPage page;
            using (PdfDocument document = await PdfReader.ReadFileAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "compatibility.pdf")))
            {
                page = document.GetPage(1);
            }

            Assert.That(page.UnsupportedFeatures, Is.Empty);
            PixelBuffer[] images = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => PdfRenderer.Render(page, Exact))));
            Assert.That(images[0].Pixels.ToArray(), Is.EqualTo(images[1].Pixels.ToArray()));
            Assert.That(images[1].Pixels.ToArray(), Is.EqualTo(images[2].Pixels.ToArray()));
        }

        [Test]
        public async Task FilledWidgetWithoutAppearanceIsReportedIncludingInheritedValues()
        {
            byte[] data = PdfTestDocument.Create("", "/Annots [5 0 R]", extraObjects: new[]
            {
                PdfTestDocument.Bytes("<< /Subtype /Widget /Rect [20 20 180 80] /Parent 6 0 R >>"),
                PdfTestDocument.Bytes("<< /FT /Tx /V (Cannot silently omit this value) >>")
            });
            using PdfDocument document = await Read(data);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Does.Contain("Form value without an appearance stream"));
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(page, Exact));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(32)]
        public async Task InvisibleHiddenAndNoViewAnnotationsAreOmitted(int flags)
        {
            byte[] data = PdfTestDocument.Create("", "/Annots [5 0 R]", extraObjects: new[]
            {
                PdfTestDocument.Bytes("<< /Subtype /Widget /F " + flags + " /Rect [0 0 200 100] /AP << /N 6 0 R >> >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 0 0 200 100 re f"), "/BBox [0 0 200 100]")
            });
            PixelBuffer image = await Render(data);
            Assert.That(image[100, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [TestCase(0, "JPXDecode")]
        [TestCase(1, "JPXDecode")]
        [TestCase(2, "JBIG2Decode")]
        public async Task ScanDimensionsAreValidatedBeforeCompositing(int index, string filter)
        {
            using var source = UglyToad.PdfPig.PdfDocument.Open(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "scan-codecs.pdf"));
            var image = source.GetPage(1).GetImages().ElementAt(index);
            byte[] picture = PdfTestDocument.Stream(image.RawMemory.ToArray(), "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 1 /ColorSpace /DeviceGray /Filter /" + filter);
            using PdfDocument document = await Read(PdfTestDocument.Create("/I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: picture));
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        private static Task<PdfDocument> Read(byte[] bytes) => PdfReader.ReadAsync(new MemoryStream(bytes, false));
        private static async Task<PixelBuffer> Render(byte[] bytes)
        {
            using PdfDocument document = await Read(bytes);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, Exact);
        }
    }
}
