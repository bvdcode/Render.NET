using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgImageTests
    {
        private static IEnumerable<TestCaseData> PngCases()
        {
            foreach (int color in new[]
            {
                0,
                2,
                3,
                4,
                6
            }

            )
            {
                int[] depths = color switch
                {
                    0 => new[]
                    {
                        1,
                        2,
                        4,
                        8,
                        16
                    },
                    3 => new[]
                    {
                        1,
                        2,
                        4,
                        8
                    },
                    _ => new[]
                    {
                        8,
                        16
                    }
                };
                foreach (int depth in depths)
                {
                    foreach (int interlace in new[]
                    {
                        0,
                        1
                    }

                    )
                    {
                        yield return new TestCaseData(color, depth, interlace);
                    }
                }
            }
        }

        private static string Svg(byte[] png) => "<svg width='19' height='17'><image width='19' height='17' href='data:image/png;base64," + Convert.ToBase64String(png) + "'/></svg>";
        private static byte[] Png(int color = 6, int depth = 8, int adam = 0) => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Svg", "Png", $"color{color}-depth{depth}-adam{adam}.png"));
        [TestCaseSource(nameof(PngCases))]
        public void EmbeddedPngDecodesColorDepthFiltersAndAdam7(int color, int depth, int interlace)
        {
            PixelBuffer image = SvgRenderer.Render(SvgReader.Read(Svg(Png(color, depth, interlace))), new SvgRenderOptions { Supersampling = 1 });
            for (int y = 0; y < 17; y++)
            {
                for (int x = 0; x < 19; x++)
                {
                    byte v = (x + y) % 2 == 1 ? (byte)255 : (byte)0;
                    Rgba32 expected = color switch
                    {
                        0 => new Rgba32(v, v, v),
                        4 => new Rgba32(v, v, v, (byte)(255 - v)),
                        _ => new Rgba32(v, (byte)(255 - v), v)
                    };
                    if (expected.A == 0)
                    {
                        expected = default;
                    }

                    Assert.That(image[x, y], Is.EqualTo(expected), $"{x},{y}");
                }
            }
        }

        [Test]
        public void InvalidCrcTruncationAndPixelBudgetFail()
        {
            byte[] png = Png();
            png[29] ^= 1;
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(Svg(png)));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(Svg(Png()[..40])));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(Svg(Png()), new SvgReaderOptions { MaximumImagePixels = 10 }));
        }

        [Test]
        public void ResolverImageIsCopiedAndExternalResourcesAreExplicit()
        {
            PixelBuffer? input = new PixelBuffer(2, 2);
            input.Clear(new Rgba32(0, 255, 0));
            const string xml = "<svg width='20' height='20'><image width='20' height='20' href='custom:image'/></svg>";
            SvgDocument document = SvgReader.Read(xml, new SvgReaderOptions { ImageResolver = _ => input });
            input.Clear(default);
            Assert.That(SvgRenderer.Render(document)[10, 10], Is.EqualTo(new Rgba32(0, 255, 0)));
            Assert.That(SvgReader.Read(xml).UnsupportedFeatures, Is.Not.Empty);
        }
    }
}
