using System.Xml;
using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgDoctypeTests
    {
        [TestCase("<!DOCTYPE svg>")]
        [TestCase("<!DOCTYPE svg PUBLIC '-//W3C//DTD SVG 1.1//EN' 'http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd'>")]
        [TestCase("<!DOCTYPE svg SYSTEM 'file:///unavailable.dtd'>")]
        public void DoctypeDoesNotRequireLoadingADtd(string declaration)
        {
            SvgDocument document = SvgReader.Read(declaration + "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='lime'/></svg>");
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            Assert.That(SvgRenderer.Render(document)[4, 4], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [TestCase("<!ENTITY text 'expanded'>")]
        [TestCase("<!ENTITY text SYSTEM 'file:///unavailable.txt'>")]
        public void DeclaredEntitiesAreNotExpanded(string entity)
        {
            Assert.Throws<XmlException>(() => SvgReader.Read("<!DOCTYPE svg [" + entity + "]><svg><text>&text;</text></svg>"));
        }

        [Test]
        public async Task OpenIconicFontDefinitionsDoNotCreateVisibleSceneContent()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Svg", "OpenIconic", "open-iconic.svg");
            SvgDocument document = await SvgReader.ReadFileAsync(path);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = SvgRenderer.Render(document);
            Assert.That((image.Width, image.Height), Is.EqualTo((300, 150)));
            Assert.That(image.Pixels.ToArray(), Is.All.Zero);
        }
    }
}
