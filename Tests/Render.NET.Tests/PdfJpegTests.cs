using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfJpegTests
    {
        [TestCase("cmyk.jpg", "/DeviceCMYK", 32, 0, 255, 255, 255)]
        [TestCase("cmyk.jpg", "/DeviceCMYK", 32, 1, 255, 0, 0)]
        [TestCase("cmyk.jpg", "/DeviceCMYK", 32, 2, 0, 255, 255)]
        [TestCase("cmyk.jpg", "/DeviceCMYK", 32, 3, 0, 0, 0)]
        [TestCase("cmyk-no-marker.jpg", "/DeviceCMYK", 32, 0, 255, 255, 255)]
        [TestCase("cmyk-no-marker.jpg", "/DeviceCMYK", 32, 1, 255, 0, 0)]
        [TestCase("ycck.jpg", "/DeviceCMYK", 24, 0, 255, 255, 255)]
        [TestCase("ycck.jpg", "/DeviceCMYK", 24, 1, 0, 0, 0)]
        [TestCase("ycck.jpg", "/DeviceCMYK", 24, 2, 0, 0, 0)]
        [TestCase("rgb.jpg", "/DeviceRGB", 24, 0, 255, 0, 0)]
        [TestCase("rgb.jpg", "/DeviceRGB", 24, 1, 0, 255, 0)]
        [TestCase("rgb.jpg", "/DeviceRGB", 24, 2, 0, 0, 255)]
        [TestCase("ycbcr.jpg", "/DeviceRGB", 24, 1, 128, 128, 128)]
        [TestCase("gray.jpg", "/DeviceGray", 24, 1, 128, 128, 128)]
        public async Task PreservesDecodedColorComponents(string fixture, string space, int width, int block, int red, int green, int blue)
        {
            PixelBuffer image = await Render(fixture, space, width);
            Assert.That(image[(block * 8) + 4, 96], Is.EqualTo(new Rgba32((byte)red, (byte)green, (byte)blue)));
        }

        [TestCase("rgb.jpg", "/DeviceRGB", "/DecodeParms << /ColorTransform 1 >>", 255, 0, 0)]
        [TestCase("ycck.jpg", "/DeviceCMYK", "/DecodeParms << /ColorTransform 0 >>", 255, 255, 255)]
        [TestCase("ycbcr.jpg", "/DeviceRGB", "/DecodeParms << /ColorTransform 0 >>", 0, 128, 128)]
        public async Task AdobeMarkerOverridesDictionaryTransform(string fixture, string space, string parameters, int red, int green, int blue)
        {
            PixelBuffer image = await Render(fixture, space, 24, parameters);
            Assert.That(image[4, 96], Is.EqualTo(new Rgba32((byte)red, (byte)green, (byte)blue)));
        }

        [Test]
        public async Task DecodeArraysAndColorKeyMasksUseSourceSamples()
        {
            PixelBuffer inverted = await Render("cmyk.jpg", "/DeviceCMYK", 32, "/Decode [1 0 1 0 1 0 1 0]");
            Assert.That(inverted[4, 96], Is.EqualTo(new Rgba32(0, 0, 0)));
            PixelBuffer masked = await Render("rgb.jpg", "/DeviceRGB", 24, "/Mask [255 255 0 0 0 0]",
                "0 0 1 rg 0 0 200 100 re f ");
            Assert.That(masked[4, 96], Is.EqualTo(new Rgba32(0, 0, 255)));
            Assert.That(masked[12, 96], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [Test]
        public async Task RejectsMismatchedComponents()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(await Document("cmyk.jpg", "/DeviceRGB", 32, "", "")));
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task ReadsTransformFromIndirectChainedFilterParameters()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "Jpeg", "ycbcr.jpg"));
            byte[] ascii = PdfTestDocument.Bytes(Convert.ToHexString(jpeg) + ">");
            byte[] picture = PdfTestDocument.Stream(ascii, "/Type /XObject /Subtype /Image /Width 24 /Height 8 "
                + "/BitsPerComponent 8 /ColorSpace /DeviceRGB /Filter [/ASCIIHexDecode /DCTDecode] /DecodeParms [null 6 0 R]");
            byte[] input = PdfTestDocument.Create("q 24 0 0 8 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>",
                extraObjects: new[] { picture, PdfTestDocument.Bytes("<< /ColorTransform 0 >>") });
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
            Assert.That(image[4, 96], Is.EqualTo(new Rgba32(0, 128, 128)));
        }

        private static async Task<PixelBuffer> Render(string fixture, string space, int width, string parameters = "", string background = "")
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(await Document(fixture, space, width, parameters, background)));
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
        }

        private static async Task<byte[]> Document(string fixture, string space, int width, string parameters, string background)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "Jpeg", fixture));
            byte[] image = PdfTestDocument.Stream(jpeg, "/Type /XObject /Subtype /Image /Width " + width
                + " /Height 8 /BitsPerComponent 8 /ColorSpace " + space + " /Filter /DCTDecode " + parameters);
            return PdfTestDocument.Create(background + "q " + width + " 0 0 8 0 0 cm /I Do Q", resources: "/XObject << /I 5 0 R >>", extraObjects: new[] { image });
        }
    }
}
