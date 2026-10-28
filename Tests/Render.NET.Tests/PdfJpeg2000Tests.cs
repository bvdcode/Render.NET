using System.Buffers.Binary;
using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfJpeg2000Tests
    {
        private static string Root => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Jpeg2000");

        [TestCase("RGB-63x45.jp2", 63, 45, "/DeviceRGB", 3, 8)]
        [TestCase("L-63x45.jp2", 63, 45, "/DeviceGray", 1, 8)]
        [TestCase("I16-63x45.jp2", 63, 45, "/DeviceGray", 1, 16)]
        [TestCase("raw.j2k", 63, 45, "/DeviceRGB", 3, 8)]
        [TestCase("four-component.j2k", 95, 77, "/DeviceCMYK", 4, 8)]
        [TestCase("RGB-63x45.jp2", 63, 45, "", 3, 8)]
        public async Task PdfColorsMatchIndependentlyDecodedSamples(string file, int width, int height, string colorSpace, int components, int bits)
        {
            PixelBuffer image = await Render(file, width, height, colorSpace);
            byte[] raw = await File.ReadAllBytesAsync(Path.Combine(Root, Path.GetFileNameWithoutExtension(file) + ".raw"));
            foreach ((int, int) point in new[]
            {
                (3, 7),
                (21, 15),
                (41, 25)
            }

            )
            {
                int start = ((point.Item2 * width) + point.Item1) * components * (bits / 8);
                double[]? values = new double[components];
                for (int c = 0; c < components; c++)
                {
                    values[c] = bits == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(start + (c * 2), 2)) / 65535.0 : raw[start + c] / 255.0;
                }

                double r = values[0], g = values[components == 1 ? 0 : 1], b = values[components == 1 ? 0 : 2];
                if (components == 4)
                {
                    r = (1 - r) * (1 - values[3]);
                    g = (1 - g) * (1 - values[3]);
                    b = (1 - b) * (1 - values[3]);
                }

                Rgba32 actual = image[point.Item1, point.Item2];
                Assert.That(actual.R, Is.EqualTo((int)Math.Round(r * 255)).Within(1));
                Assert.That(actual.G, Is.EqualTo((int)Math.Round(g * 255)).Within(1));
                Assert.That(actual.B, Is.EqualTo((int)Math.Round(b * 255)).Within(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task EmbeddedOpacityHonorsSMaskInData(int mode)
        {
            const int x = 21, y = 15, width = 63, height = 45;
            PixelBuffer image = await Render("RGBA-63x45.jp2", width, height, "/DeviceRGB", "/SMaskInData " + mode);
            byte[] raw = await File.ReadAllBytesAsync(Path.Combine(Root, "RGBA-63x45.raw"));
            int offset = ((y * width) + x) * 4;
            double alpha = mode == 0 ? 1 : raw[offset + 3] / 255.0;
            double red = raw[offset] / 255.0;
            if (mode == 2)
            {
                red = Math.Min(1, red / alpha);
            }

            Assert.That(image[x, y].R, Is.EqualTo((int)Math.Round((((red * alpha) + 1) - alpha) * 255)).Within(1));
        }

        [Test]
        public async Task UnsupportedJpeg2000ExtensionsAreReportedByThePdfReader()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(Root, "raw.j2k"));
            int offset = 4 + BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(4));
            byte[] changed = encoded[..offset].Concat(new byte[] { 255, 0x5f, 0, 2 }).Concat(encoded[offset..]).ToArray();
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Document(changed, 63, 45, "/DeviceRGB")));
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures.Any(feature => feature.Contains("progression order changes")), Is.True);
            Assert.Throws<NotSupportedException>(() => PdfRenderer.Render(page));
        }

        private static async Task<PixelBuffer> Render(string file, int width, int height, string space, string additional = "")
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Document(await File.ReadAllBytesAsync(Path.Combine(Root, file)), width, height, space, additional)));
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
        }

        private static byte[] Document(byte[] encoded, int width, int height, string space, string additional = "")
        {
            string dictionary = "/Type /XObject /Subtype /Image /Width " + width + " /Height " + height + " /Filter /JPXDecode " + additional;
            if (space.Length != 0)
            {
                dictionary += " /ColorSpace " + space;
            }

            return PdfTestDocument.Create(width + " 0 0 " + height + " 0 " + (100 - height) + " cm /I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: PdfTestDocument.Stream(encoded, dictionary));
        }
    }
}
