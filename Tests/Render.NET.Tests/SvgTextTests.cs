using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgTextTests
    {
        private static PixelBuffer Render(string content, SvgReaderOptions? reader = null)
        {
            SvgDocument document = SvgReader.Read("<svg width='300' height='100'>" + content + "</svg>", reader);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            return SvgRenderer.Render(document, new SvgRenderOptions { Supersampling = 1 });
        }

        private static (int X, int Y, int Width, int Height) Ink(PixelBuffer image)
        {
            int left = image.Width, top = image.Height, right = 0, bottom = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image[x, y].A > 0)
                    {
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x + 1);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }
            }

            return (left, top, right - left, bottom - top);
        }

        [TestCase("Hello SVG!")]
        [TestCase("Привет, мир!")]
        [TestCase("éåñü")]
        public void LatinCyrillicAndCompositeGlyphsHaveVisibleOutlines(string text)
        {
            (int X, int Y, int Width, int Height) ink = Ink(Render("<text x='10' y='60' font-size='30'>" + text + "</text>"));
            Assert.That(ink.Width, Is.GreaterThan(40));
            Assert.That(ink.Height, Is.InRange(15, 45));
            Assert.That(ink.Y, Is.LessThan(50));
        }

        [Test]
        public void TextAnchorMovesCompleteMixedStyleChunk()
        {
            (int X, int Y, int Width, int Height) start = Ink(Render("<text x='150' y='60' font-size='30'>A<tspan fill='red'>V</tspan></text>"));
            (int X, int Y, int Width, int Height) end = Ink(Render("<text x='150' y='60' font-size='30' text-anchor='end'>A<tspan fill='red'>V</tspan></text>"));
            Assert.That(end.Width, Is.EqualTo(start.Width).Within(1));
            Assert.That(end.X + end.Width, Is.EqualTo(150).Within(4));
        }

        [Test]
        public void NestedRelativeFontSizesAreComputedOnce()
        {
            PixelBuffer a = Render("<text x='10' y='60' font-size='2em'><tspan><tspan>AV</tspan></tspan></text>");
            PixelBuffer b = Render("<text x='10' y='60' font-size='32'>AV</text>");
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.True);
        }

        [Test]
        public void TspanPositionAndColorAreApplied()
        {
            PixelBuffer image = Render("<text x='10' y='60' font-size='30' fill='red'>A<tspan x='100' fill='blue'>A</tspan></text>");
            Assert.That(image.Pixels.Span.ToArray().Where((_, i) => i % 4 == 0).Count(x => x > 0), Is.GreaterThan(50));
            Assert.That(image.Pixels.Span.ToArray().Where((_, i) => i % 4 == 2).Count(x => x > 0), Is.GreaterThan(50));
            Assert.That(image[75, 50].A, Is.Zero);
        }

        [Test]
        public void KerningCanBeDisabled()
        {
            (int X, int Y, int Width, int Height) kerned = Ink(Render("<text x='10' y='60' font-size='60'>AV</text>"));
            (int X, int Y, int Width, int Height) plain = Ink(Render("<text x='10' y='60' font-size='60' font-kerning='none'>AV</text>"));
            Assert.That(kerned.Width, Is.LessThan(plain.Width));
        }

        [Test]
        public void WhitespaceCollapsesAcrossSpans()
        {
            PixelBuffer a = Render("<text x='10' y='60' font-size='30'>  A <tspan>  V </tspan> </text>");
            PixelBuffer b = Render("<text x='10' y='60' font-size='30'>A V</text>");
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.True);
        }

        [Test]
        public void TextLengthScalesGlyphsAndSpacing()
        {
            (int X, int Y, int Width, int Height) ink = Ink(Render("<text x='10' y='60' font-size='30' textLength='200' lengthAdjust='spacingAndGlyphs'>AV</text>"));
            Assert.That(ink.Width, Is.InRange(180, 205));
        }

        [Test]
        public void TextPathPlacesGlyphsAlongLocalCurve()
        {
            (int X, int Y, int Width, int Height) ink = Ink(Render("<defs><path id='p' d='M20 70Q150 -20 280 70'/></defs><text font-size='25'><textPath href='#p' startOffset='20%'>Render.NET</textPath></text>"));
            Assert.That(ink.Width, Is.GreaterThan(80));
            Assert.That(ink.X, Is.GreaterThan(40));
            Assert.That(ink.Y, Is.LessThan(40));
        }

        [Test]
        public void FontResolverReceivesFamilyWeightAndStyle()
        {
            var requests = new List<SvgFontRequest>();
            Render("<text x='10' y='60' font-family='Custom' font-weight='700' font-style='italic'>A</text>", new SvgReaderOptions
            {
                FontResolver = request =>
            {
                requests.Add(request);
                return default;
            }
            });
            Assert.That(requests, Does.Contain(new SvgFontRequest("Custom", 700, true)));
        }

        [Test]
        public void TspanOpacityAppliesOnceToOverlappingGlyphs()
        {
            PixelBuffer image = Render("<text y='60' font-size='50'><tspan opacity='.5' x='20 20'>WW</tspan></text>");
            Assert.That(image.Pixels.Span.ToArray().Where((_, i) => i % 4 == 3).Max(), Is.EqualTo(128));
        }

        [Test]
        public void CssFontShorthandMatchesIndividualProperties()
        {
            PixelBuffer a = Render("<text x='10' y='60' style='font: italic bold 30px Aileron'>Render</text>");
            PixelBuffer b = Render("<text x='10' y='60' font-style='italic' font-weight='bold' font-size='30px' font-family='Aileron'>Render</text>");
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.True);
        }

        [Test]
        public void FontSizeResolvesThroughNonTextAncestors()
        {
            PixelBuffer a = Render("<g font-size='30'><text x='10' y='60' font-size='2em'>AV</text></g>");
            PixelBuffer b = Render("<text x='10' y='60' font-size='60'>AV</text>");
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.True);
        }

        [Test]
        public void OriginalEffectsFixtureRendersWithoutPartialMode()
        {
            SvgDocument document = SvgReader.Read(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Svg", "effects-preview.svg")));
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = SvgRenderer.Render(document, new SvgRenderOptions { Supersampling = 1 });
            Assert.That(image[400, 340].G, Is.GreaterThan(150));
            Assert.That(image[650, 155].R, Is.EqualTo(14));
        }

        [Test]
        public void MalformedFontFromResolverIsRejected() => Assert.Throws<InvalidDataException>(() => SvgReader.Read("<svg><text>A</text></svg>", new SvgReaderOptions { FontResolver = _ => new byte[8] }));
    }
}
