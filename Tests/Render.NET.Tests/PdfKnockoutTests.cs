using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfKnockoutTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task KnockoutChildrenUseInitialBackdropAndReplaceEarlierOpacity(bool isolated)
        {
            byte[] data = Group("/Half gs 1 0 0 rg 10 10 100 80 re f 0 1 0 rg 60 10 100 80 re f", isolated);
            PixelBuffer image = await Render(data);
            Rgba32 second = image[140, 50];
            Assert.That(image[80, 50], Is.EqualTo(second));
            Assert.That(second.G, Is.EqualTo(128).Within(1));
            Assert.That(second.B, Is.EqualTo(128).Within(1));
        }

        [Test]
        public async Task FullyTransparentObjectKnocksOutItsShape()
        {
            PixelBuffer image = await Render(Group("1 0 0 rg 10 10 100 80 re f /Zero gs 0 1 0 rg 60 10 100 80 re f", true));
            Assert.That(image[30, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[80, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public async Task KnockoutBlendUsesGroupBackdropRatherThanEarlierSibling()
        {
            byte[] data = Group("1 0 0 rg 10 10 100 80 re f /Multiply gs 0 1 0 rg 60 10 100 80 re f", false,
                backdrop: "0.5 0.5 0.5 rg 0 0 200 100 re f");
            PixelBuffer image = await Render(data);
            Assert.That(image[80, 50], Is.EqualTo(image[140, 50]));
            Assert.That(image[80, 50].G, Is.EqualTo(128).Within(1));
        }

        [Test]
        public void FractionalImageShapeOnlyPartiallyKnocksOutPreviousObject()
        {
            PixelBuffer? pixels = new PixelBuffer(1, 1);
            pixels.Clear(new Rgba32(0, 0, 255));
            RasterCanvas? canvas = new RasterCanvas(pixels);
            PixelBuffer? image = new PixelBuffer(1, 1);
            image[0, 0] = new Rgba32(0, 255, 0, 128);
            canvas.DrawLayer(layer =>
            {
                layer.DrawObject(c => c.FillPath(new VectorPath().Rectangle(0, 0, 1, 1), new Rgba32(255, 0, 0), System.Numerics.Matrix3x2.Identity));
                layer.DrawObject(c => c.DrawImage(image, System.Numerics.Matrix3x2.Identity));
            }, knockout: true);
            Assert.That(pixels[0, 0].R, Is.EqualTo(127).Within(1));
            Assert.That(pixels[0, 0].G, Is.EqualTo(128).Within(1));
            Assert.That(pixels[0, 0].B, Is.Zero);
        }

        [Test]
        public void ReusedElementBufferClearsEarlierShapeAndColor()
        {
            PixelBuffer? pixels = new PixelBuffer(20, 10);
            RasterCanvas? canvas = new RasterCanvas(pixels);
            canvas.DrawLayer(layer =>
            {
                layer.DrawObject(c => c.FillPath(new VectorPath().Rectangle(0, 0, 10, 10), new Rgba32(255, 0, 0), System.Numerics.Matrix3x2.Identity));
                layer.DrawObject(c => c.FillPath(new VectorPath().Rectangle(10, 0, 10, 10), new Rgba32(0, 255, 0), System.Numerics.Matrix3x2.Identity));
                layer.DrawObject(c => c.FillPath(new VectorPath().Rectangle(0, 0, 5, 5), new Rgba32(0, 0, 255, 0), System.Numerics.Matrix3x2.Identity));
            }, knockout: true);
            Assert.That(pixels[2, 2].A, Is.Zero);
            Assert.That(pixels[7, 7], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(pixels[15, 5], Is.EqualTo(new Rgba32(0, 255, 0)));
        }

        [Test]
        public void TransparentShaderKnocksOutOnlyExplicitShape()
        {
            PixelBuffer? pixels = new PixelBuffer(20, 10);
            RasterCanvas? canvas = new RasterCanvas(pixels);
            canvas.DrawLayer(layer =>
            {
                layer.DrawObject(c => c.FillPath(new VectorPath().Rectangle(0, 0, 20, 10), new Rgba32(255, 0, 0), System.Numerics.Matrix3x2.Identity));
                layer.DrawObject(c => c.DrawShader(_ => default, point => point.X < 10, System.Numerics.Matrix3x2.Identity));
            }, knockout: true);
            Assert.That(pixels[5, 5].A, Is.Zero);
            Assert.That(pixels[15, 5], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [Test]
        public void NestedTransparentGroupPreservesShapeForOuterKnockout()
        {
            PixelBuffer? pixels = new PixelBuffer(20, 10);
            RasterCanvas? canvas = new RasterCanvas(pixels);
            canvas.DrawLayer(layer =>
            {
                layer.DrawObject(c => c.FillPath(new VectorPath().Rectangle(0, 0, 20, 10), new Rgba32(255, 0, 0), System.Numerics.Matrix3x2.Identity));
                layer.DrawObject(c => c.DrawLayer(inner => inner.FillPath(new VectorPath().Rectangle(0, 0, 10, 10), default, System.Numerics.Matrix3x2.Identity)));
            }, knockout: true);
            Assert.That(pixels[5, 5].A, Is.Zero);
            Assert.That(pixels[15, 5], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        private static byte[] Group(string content, bool isolated, string backdrop = "0 0 1 rg 0 0 200 100 re f")
        {
            string resources = "/ExtGState << /Half << /ca 0.5 >> /Zero << /ca 0 >> /Multiply << /BM /Multiply >> >>";
            byte[] form = PdfTestDocument.Stream(PdfTestDocument.Bytes(content), "/Type /XObject /Subtype /Form /BBox [0 0 200 100] "
                + "/Group << /S /Transparency /K true /I " + (isolated ? "true" : "false") + " >> /Resources << " + resources + " >>");
            return PdfTestDocument.Create(backdrop + " /Group Do", resources: "/XObject << /Group 5 0 R >>", extraObjects: form);
        }

        private static async Task<PixelBuffer> Render(byte[] data)
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(data));
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
        }
    }
}
