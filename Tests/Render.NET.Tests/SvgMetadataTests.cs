using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgMetadataTests
    {
        private const string SvgNamespace = "http://www.w3.org/2000/svg";
        private const string Rectangle = "<rect id='tile' width='32' height='32' fill='lime'/>";
        private static PixelBuffer Render(string content, string attributes = "")
        {
            SvgDocument document = SvgReader.Read($"<svg xmlns='{SvgNamespace}' xmlns:m='urn:editor' width='32' height='32' {attributes}>{content}</svg>");
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            return SvgRenderer.Render(document, new SvgRenderOptions { Supersampling = 1 });
        }

        [Test]
        public async Task EditorMetadataFixtureRendersWithoutPartialRendering()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures/Svg/editor-metadata.svg");
            SvgDocument document = await SvgReader.ReadFileAsync(path);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = SvgRenderer.Render(document);
            Assert.That(image[8, 8], Is.EqualTo(new Rgba32(183, 239, 32)));
            Assert.That(image[16, 16], Is.EqualTo(new Rgba32(16, 33, 47)));
            Assert.That(image[0, 0].A, Is.Zero);
        }

        [TestCase("http://www.inkscape.org/namespaces/inkscape")]
        [TestCase("http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd")]
        [TestCase("urn:other-editor")]
        public void ForeignNamespaceSubtreesDoNotPaintIndexIdsOrApplyStyles(string ns)
        {
            string foreign = $"<editor:namedview xmlns:editor='{ns}' id='tile'><style>rect {{fill:red}}</style><path id='tile' d='invalid'/><rect width='32' height='32' fill='red'/></editor:namedview>";
            PixelBuffer image = Render(foreign + Rectangle);
            Assert.That(image[16, 16], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [TestCase("title")]
        [TestCase("desc")]
        [TestCase("metadata")]
        [TestCase("namedview")]
        public void InertMetadataDoesNotApplyStylesOrRegisterIds(string name)
        {
            PixelBuffer image = Render($"<{name} id='tile'><style>rect {{fill:red}}</style><rect id='tile' width='32' height='32'/></{name}>" + Rectangle);
            Assert.That(image[16, 16], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [Test]
        public void NamespacedPresentationAttributesAndNamespaceDeclarationsAreInert()
        {
            PixelBuffer image = Render("<rect width='32' height='32' fill='lime' m:fill='red' m:stroke-width='invalid' m:filter='invalid' m:marker-start='invalid'/>", "xmlns:filter='urn:filter' xmlns:mask='urn:mask'");
            Assert.That(image[16, 16], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [Test]
        public void NamespacedGradientAttributesStopsAndStylesCannotOverrideSvgPaint()
        {
            PixelBuffer image = Render("<m:style>rect {fill:red}</m:style><defs><linearGradient id='g' x1='0' m:x1='invalid'><m:stop offset='0' stop-color='red'/><stop stop-color='blue' m:stop-color='red'/></linearGradient></defs><rect width='32' height='32' fill='url(#g)'/>");
            Assert.That(image[16, 16], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public void ForeignFilterPrimitivesAreNotInterpretedAsSvgOperations()
        {
            PixelBuffer image = Render("<defs><filter id='f'><m:feFlood flood-color='red'/><feOffset dx='0'/></filter></defs><rect width='32' height='32' fill='lime' filter='url(#f)'/>");
            Assert.That(image[16, 16], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [Test]
        public void ForeignTextSpansAndTheirTextAreIgnored()
        {
            PixelBuffer actual = Render("<text x='1' y='24' font-size='20'>A<m:tspan>unpainted</m:tspan>B</text>");
            PixelBuffer expected = Render("<text x='1' y='24' font-size='20'>AB</text>");
            Assert.That(actual.Pixels.Span.SequenceEqual(expected.Pixels.Span), Is.True);
        }

        [Test]
        public void SvgPrefixAndXlinkReferencesStillRender()
        {
            string xml = $"<s:svg xmlns:s='{SvgNamespace}' xmlns:editor='urn:editor' xmlns:xlink='http://www.w3.org/1999/xlink' width='32' height='32'><editor:namedview/><s:defs><s:rect id='r' width='32' height='32' fill='lime'/></s:defs><s:use xlink:href='#r'/></s:svg>";
            SvgDocument document = SvgReader.Read(xml);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            Assert.That(SvgRenderer.Render(document)[16, 16], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [TestCase("<foreignObject><m:metadata/></foreignObject>")]
        [TestCase("<unsupported/>")]
        [TestCase("<rect width='32' height='32' vector-effect='non-scaling-stroke'/>")]
        public void UnsupportedSvgContentStillRejectsByDefault(string content)
        {
            SvgDocument document = SvgReader.Read($"<svg xmlns='{SvgNamespace}' xmlns:m='urn:editor' width='32' height='32'>{content}</svg>");
            Assert.That(document.UnsupportedFeatures, Is.Not.Empty);
            Assert.Throws<NotSupportedException>(() => SvgRenderer.Render(document));
        }

        [Test]
        public void IgnoredSubtreesStillCountAgainstInputBudgets()
        {
            string elements = string.Concat(Enumerable.Repeat("<m:item/>", 20));
            string xml = $"<svg xmlns='{SvgNamespace}' xmlns:m='urn:editor'><m:namedview>{elements}</m:namedview></svg>";
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(xml, new SvgReaderOptions { MaximumElements = 10 }));
            string deep = $"<svg xmlns='{SvgNamespace}' xmlns:m='urn:editor'><m:namedview><m:a><m:b/></m:a></m:namedview></svg>";
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(deep, new SvgReaderOptions { MaximumDepth = 3 }));
        }

        [Test]
        public void ForeignNamespaceRootIsNotAnSvgDocument()
        {
            Assert.Throws<InvalidDataException>(() => SvgReader.Read("<m:svg xmlns:m='urn:editor'/>"));
        }
    }
}
