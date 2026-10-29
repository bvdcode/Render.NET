using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfRgbCompositeTests
    {
        [TestCase("/OP true /op true /OPM 0")]
        [TestCase("/OP true /op true /OPM 1")]
        [TestCase("/OP true /op false /OPM 1")]
        [TestCase("/OP false /op true /OPM 1")]
        public async Task OverprintControlsDoNotRejectRgbCompositeOutput(string state)
        {
            const string content = "1 0 0 0 k 0 0 200 100 re f q /GS gs "
                + "0 0 0 1 k 20 20 60 60 re f 0 0 0 0 k 100 20 60 60 re f "
                + "8 w 0 0 0 1 K 10 10 m 190 90 l S Q";
            PixelBuffer expected = await Render(content, "/OP false /op false /OPM 0");
            PixelBuffer actual = await Render(content, state);
            Assert.That(actual.Pixels.Span.SequenceEqual(expected.Pixels.Span), Is.True);
            Assert.That(actual[140, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
            Assert.That(actual[5, 50], Is.Not.EqualTo(actual[140, 50]));
        }

        [Test]
        public async Task OverprintDoesNotRejectImagesOrShadings()
        {
            byte[] picture = PdfTestDocument.Stream(new byte[] { 255, 0, 0 }, "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8");
            byte[] shading = PdfTestDocument.Bytes("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [100 0 200 0] /Extend [true true] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 1 0] /C1 [0 0 1] /N 1 >> >>");
            byte[] input = PdfTestDocument.Create("q /GS gs 80 0 0 80 0 0 cm /I Do Q q /GS gs 100 0 100 100 re W n /S sh Q",
                resources: "/ExtGState << /GS << /OP true /op true /OPM 1 >> >> /XObject << /I 5 0 R >> /Shading << /S 6 0 R >>",
                extraObjects: new[] { picture, shading });
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = PdfRenderer.Render(page, new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
            Assert.That(image[40, 60], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[180, 50].B, Is.GreaterThan(image[180, 50].G));
        }

        private static async Task<PixelBuffer> Render(string content, string state)
        {
            byte[] input = PdfTestDocument.Create(content, resources: "/ExtGState << /GS << " + state + " >> >>");
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
        }
    }
}
