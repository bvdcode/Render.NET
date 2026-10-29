using System.Numerics;
using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class StrokeCoverageTests
    {
        [TestCase(10.1f)]
        [TestCase(10.5f)]
        [TestCase(10.99f)]
        public void ThinLinesRetainTheirAreaInBothDirections(float position)
        {
            PixelBuffer vertical = new PixelBuffer(24, 24);
            RasterCanvas first = new RasterCanvas(vertical);
            first.StrokePathAntialiased(new VectorPath().MoveTo(position, 2).LineTo(position, 22),
                new Rgba32(40, 80, 120), new StrokeStyle { Width = 0.1f }, Matrix3x2.Identity);
            PixelBuffer horizontal = new PixelBuffer(24, 24);
            RasterCanvas second = new RasterCanvas(horizontal);
            second.StrokePathAntialiased(new VectorPath().MoveTo(2, position).LineTo(22, position),
                new Rgba32(40, 80, 120), new StrokeStyle { Width = 0.1f }, Matrix3x2.Identity);
            int verticalAlpha = 0;
            int horizontalAlpha = 0;
            for (int i = 0; i < 24; i++)
            {
                verticalAlpha += vertical[i, 12].A;
                horizontalAlpha += horizontal[12, i].A;
            }

            Assert.That(verticalAlpha, Is.InRange(25, 26));
            Assert.That(horizontalAlpha, Is.InRange(25, 26));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public async Task ReducedPdfGridDoesNotLoseRowsOrColumns(int supersampling)
        {
            byte[] input = PdfTestDocument.Create("0.5 w 0 G 4 0 m 4 100 l 84 0 m 84 100 l 0 4 m 200 4 l 0 84 m 200 84 l S");
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
            PixelBuffer image = PdfRenderer.Render(document.GetPage(1), new PdfRenderOptions
            {
                Dpi = 18,
                Supersampling = supersampling
            });
            Assert.That(image[0, 12].R + image[1, 12].R, Is.LessThan(500));
            Assert.That(image[20, 12].R + image[21, 12].R, Is.LessThan(500));
            Assert.That(image[12, 23].R + image[12, 24].R, Is.LessThan(500));
            Assert.That(image[12, 3].R + image[12, 4].R, Is.LessThan(500));
        }

        [Test]
        public void StrokeOverlapIsCoveredOnceAndClipIsRespected()
        {
            PixelBuffer image = new PixelBuffer(24, 24);
            RasterCanvas canvas = new RasterCanvas(image);
            canvas.ClipPath(new VectorPath().Rectangle(0, 0, 12, 24), Matrix3x2.Identity);
            VectorPath path = new VectorPath().MoveTo(2, 12.5f).LineTo(22, 12.5f).MoveTo(2, 12.5f).LineTo(22, 12.5f);
            canvas.StrokePathAntialiased(path, new Rgba32(40, 80, 120, 128), new StrokeStyle { Width = 0.5f }, Matrix3x2.Identity);
            Assert.That(image[5, 12], Is.EqualTo(new Rgba32(40, 80, 120, 64)));
            Assert.That(image[15, 12].A, Is.Zero);
        }

        [Test]
        public void StrokePaintAppliesShapeOnceAfterOverlappingContent()
        {
            PixelBuffer image = new PixelBuffer(24, 24);
            RasterCanvas canvas = new RasterCanvas(image);
            VectorPath path = new VectorPath().MoveTo(2, 12.5f).LineTo(22, 12.5f);
            canvas.PaintStrokePath(path, new StrokeStyle { Width = 0.5f }, Matrix3x2.Identity, layer =>
            {
                layer.FillPath(new VectorPath().Rectangle(0, 0, 24, 24), new Rgba32(255, 0, 0), Matrix3x2.Identity);
                layer.FillPath(new VectorPath().Rectangle(0, 0, 24, 24), new Rgba32(0, 0, 255), Matrix3x2.Identity);
            });
            Assert.That(image[10, 12], Is.EqualTo(new Rgba32(0, 0, 255, 128)));
            Assert.That(image[10, 11].A, Is.Zero);
        }
    }
}
