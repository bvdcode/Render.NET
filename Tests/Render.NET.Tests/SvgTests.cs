using System.Numerics;
using System.Text;
using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgTests
    {
        private static string Wrap(string content, string attributes = "") => $"<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' {attributes}>{content}</svg>";
        private static PixelBuffer Render(string content, string attributes = "")
        {
            SvgDocument doc = SvgReader.Read(Wrap(content, attributes));
            Assert.That(doc.UnsupportedFeatures, Is.Empty);
            return SvgRenderer.Render(doc, new SvgRenderOptions { Supersampling = 1 });
        }

        [TestCase("M10 10H90V90H10Z")]
        [TestCase("m10 10h80v80h-80z")]
        [TestCase("M10,10 90,10 90,90 10,90z")]
        [TestCase("M1e1+10L90 10 90 90 10 90z")]
        public void AbsoluteRelativeAndRepeatedCommandsPaintSameRectangle(string path)
        {
            PixelBuffer image = Render($"<path d='{path}' fill='#9acd32'/>");
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(154, 205, 50)));
            Assert.That(image[5, 50].A, Is.Zero);
        }

        [TestCase("M10 50Q30 0 50 50T90 50")]
        [TestCase("M10 50C20 0 40 0 50 50S80 100 90 50")]
        [TestCase("M10 50a40 40 0 1 1 80 0a40 40 0 1 1-80 0z")]
        public void CurvesAndArcsProduceFiniteGeometry(string data)
        {
            VectorPath path = SvgPathParser.Parse(data);
            (float X, float Y, float Width, float Height) bounds = path.GetBounds();
            Assert.That(bounds.Width, Is.GreaterThan(70));
            Assert.That(bounds.Height, Is.GreaterThan(20));
            Assert.That(path.CommandCount, Is.GreaterThan(2));
        }

        [Test]
        public void CompactArcFlagsAndRadiusCorrectionMatchEllipse()
        {
            PixelBuffer a = Render("<path d='M10 50A1 1 0 01190 50' fill='none'/>");
            Assert.That(a[50, 50].A, Is.Zero);
            PixelBuffer b = Render("<path d='M10 50a1 1 0 0180 0a1 1 0 01-80 0z'/>");
            Assert.That(b[50, 50].A, Is.EqualTo(255));
            Assert.That(b[50, 5].A, Is.Zero);
        }

        [TestCase("<rect x='10' y='10' width='80' height='80' rx='15'/>")]
        [TestCase("<circle cx='50' cy='50' r='40'/>")]
        [TestCase("<ellipse cx='50' cy='50' rx='40' ry='30'/>")]
        [TestCase("<polygon points='10,10 90,10 90,90 10,90'/>")]
        public void BasicShapesCoverCenter(string shape)
        {
            PixelBuffer image = Render(shape);
            Assert.That(image[50, 50].A, Is.EqualTo(255));
            Assert.That(image[0, 0].A, Is.Zero);
        }

        [Test]
        public void GroupOpacityAppliesOnceAtOverlappingChildren()
        {
            PixelBuffer image = Render("<g opacity='.5' fill='red'><rect width='70' height='100'/><rect x='30' width='70' height='100'/></g>");
            Assert.That(image[10, 50].A, Is.EqualTo(128));
            Assert.That(image[50, 50], Is.EqualTo(image[10, 50]));
        }

        [Test]
        public void CssSpecificityInlineAndCurrentColorInherit()
        {
            PixelBuffer image = Render("<style>rect { fill: red } .lime { fill: lime } #target { fill: blue }</style><g color='#aabbcc'><rect id='target' class='lime' width='100' height='100' style='fill:currentColor'/></g>");
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(170, 187, 204)));
        }

        [Test]
        public void TransformOrderAndUseRespectInheritedInk()
        {
            PixelBuffer image = Render("<defs><rect id='tile' width='10' height='10'/></defs><use href='#tile' x='5' fill='lime' transform='translate(20 30) scale(2)'/>");
            Assert.That(image[35, 35], Is.EqualTo(new Rgba32(0, 255, 0)));
            Assert.That(image[25, 35].A, Is.Zero);
            Assert.That(image[55, 35].A, Is.Zero);
        }

        [Test]
        public void SymbolViewBoxAndLocalClipWork()
        {
            PixelBuffer image = Render("<defs><symbol id='s' viewBox='0 0 10 10'><rect width='10' height='10'/></symbol><clipPath id='clip'><circle cx='50' cy='50' r='30'/></clipPath></defs><use href='#s' width='100' height='100' clip-path='url(#clip)'/>");
            Assert.That(image[50, 50].A, Is.EqualTo(255));
            Assert.That(image[10, 50].A, Is.Zero);
        }

        [TestCase("objectBoundingBox", "0", "1")]
        [TestCase("userSpaceOnUse", "10", "90")]
        public void LinearGradientsUseCorrectCoordinateSpace(string units, string x1, string x2)
        {
            PixelBuffer image = Render($"<defs><linearGradient id='g' gradientUnits='{units}' x1='{x1}' x2='{x2}'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect x='10' y='10' width='80' height='80' fill='url(#g)'/>");
            Assert.That(image[10, 50].R, Is.GreaterThan(250));
            Assert.That(image[89, 50].B, Is.GreaterThan(250));
            Assert.That(image[50, 50].R, Is.EqualTo(126).Within(2));
        }

        [Test]
        public void RadialGradientInheritedStopsAndObjectBoxClip()
        {
            PixelBuffer image = Render("<defs><linearGradient id='base'><stop stop-color='white'/><stop offset='100%' stop-color='black'/></linearGradient><radialGradient id='g' href='#base'/><clipPath id='c' clipPathUnits='objectBoundingBox'><rect width='.5' height='1'/></clipPath></defs><rect x='10' y='10' width='80' height='80' fill='url(#g)' clip-path='url(#c)'/>");
            Assert.That(image[49, 50].R, Is.GreaterThan(245));
            Assert.That(image[10, 50].R, Is.LessThan(5));
            Assert.That(image[60, 50].A, Is.Zero);
        }

        [Test]
        public void EvenOddFillAndDashedStroke()
        {
            PixelBuffer image = Render("<path d='M10 10H90V90H10ZM30 30H70V70H30Z' fill-rule='evenodd'/><path d='M10 50H90' fill='none' stroke='red' stroke-width='4' stroke-dasharray='10 10'/>");
            Assert.That(image[35, 40].A, Is.Zero);
            Assert.That(image[35, 50].R, Is.EqualTo(255));
            Assert.That(image[45, 50].A, Is.Zero);
        }

        [Test]
        public void ClipChildrenUnionIndependentEvenOddShapes()
        {
            PixelBuffer image = Render("<defs><clipPath id='c' clip-rule='evenodd'><rect x='10' y='10' width='50' height='70'/><rect x='40' y='10' width='50' height='70'/></clipPath></defs><rect width='100' height='100' clip-path='url(#c)'/>");
            Assert.That(image[50, 50].A, Is.EqualTo(255));
            Assert.That(image[15, 50].A, Is.EqualTo(255));
            Assert.That(image[95, 50].A, Is.Zero);
        }

        [Test]
        public void SvgPathWriterRoundTripsAllCoreCommands()
        {
            VectorPath? path = new VectorPath().MoveTo(1, 2).LineTo(4, 5).QuadraticTo(6, 7, 8, 9).CubicTo(1, 3, 5, 7, 9, 11).Close();
            string data = SvgPathWriter.Write(path);
            Assert.That(SvgPathWriter.Write(SvgPathParser.Parse(data)), Is.EqualTo(data));
        }

        [Test]
        public void ViewBoxMeetUsesCenteredLetterbox()
        {
            PixelBuffer image = Render("<rect width='100' height='50' fill='lime'/>", "viewBox='0 0 100 50'");
            Assert.That(image[50, 10].A, Is.Zero);
            Assert.That(image[50, 30].G, Is.EqualTo(255));
            Assert.That(image[50, 90].A, Is.Zero);
        }

        [Test]
        public void UnsupportedFeaturesRejectByDefault()
        {
            SvgDocument doc = SvgReader.Read(Wrap("<foreignObject>hello</foreignObject><image href='https://example.com/a.png'/><path d='M0 0H50V50Z' filter='url(#f)'/>"));
            Assert.That(doc.UnsupportedFeatures.Count, Is.GreaterThanOrEqualTo(3));
            Assert.Throws<NotSupportedException>(() => SvgRenderer.Render(doc));
            Assert.DoesNotThrow(() => SvgRenderer.Render(doc, new SvgRenderOptions { AllowPartialRendering = true }));
        }

        [TestCase("<use id='a' href='#a'/>")]
        [TestCase("<defs><linearGradient id='a' href='#b'/><linearGradient id='b' href='#a'/></defs><rect width='100' height='100' fill='url(#a)'/>")]
        public void CyclicReferencesFail(string content) => Assert.Throws<InvalidDataException>(() => SvgReader.Read(Wrap(content)));
        [TestCase("M0 0X1 2")]
        [TestCase("M0 0A10 10 0 2 0 20 20")]
        [TestCase("M0 0LNaN 3")]
        [TestCase("L0 0")]
        [TestCase("M0 0Z10 20")]
        public void MalformedPathsFail(string data) => Assert.Throws<InvalidDataException>(() => SvgPathParser.Parse(data));
        [Test]
        public async Task StreamsRemainOpenAndDocumentsRenderConcurrently()
        {
            using MemoryStream? stream = new MemoryStream(Encoding.UTF8.GetBytes(Wrap("<circle cx='50' cy='50' r='40' fill='rgba(100,150,200,.5)'/>")));
            SvgDocument doc = await SvgReader.ReadAsync(stream);
            Assert.That(stream.CanRead, Is.True);
            PixelBuffer[] images = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => SvgRenderer.Render(doc))));
            Assert.That(images.All(i => i.Pixels.Span.SequenceEqual(images[0].Pixels.Span)), Is.True);
            Assert.That(images[0][50, 50], Is.EqualTo(new Rgba32(100, 150, 200, 128)));
        }

        [Test]
        public void XmlAndGeometryBudgetsAndCancellationAreEnforced()
        {
            Assert.Throws<System.Xml.XmlException>(() => SvgReader.Read("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><svg>&x;</svg>"));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(Wrap("<rect/>"), new SvgReaderOptions { MaximumInputBytes = 20 }));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(Wrap("<g><rect/></g>"), new SvgReaderOptions { MaximumDepth = 2 }));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(Wrap("<rect width='10' height='10'/>"), new SvgReaderOptions { MaximumPathCommands = 3 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => SvgRenderer.Render(SvgReader.Read(Wrap("")), new SvgRenderOptions { MaximumSamples = 10 }));
            Assert.Throws<OperationCanceledException>(() => SvgReader.Read(Wrap(""), cancellationToken: new CancellationToken(true)));
        }

        [Test]
        public void PathTrimmingFollowsLengthAndWraps()
        {
            VectorPath? path = new VectorPath().MoveTo(0, 0).LineTo(100, 0);
            (float X, float Y, float Width, float Height) selected = path.Trim(.25f, .75f).GetBounds();
            Assert.That(selected.X, Is.EqualTo(25).Within(.01));
            Assert.That(selected.Width, Is.EqualTo(50).Within(.01));
            Assert.That(path.Trim(.8f, .2f).CommandCount, Is.EqualTo(4));
        }
    }
}
