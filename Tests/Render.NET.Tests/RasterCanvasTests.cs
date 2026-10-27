using System.Numerics;
using NUnit.Framework;

namespace Render.NET.Tests
{
    public class RasterCanvasTests
    {
        [Test]
        public void FillRulesDistinguishNestedContoursAndClipRestores()
        {
            var image = new PixelBuffer(50, 50);
            var canvas = new RasterCanvas(image);
            var path = new VectorPath().Rectangle(0, 0, 40, 40).Rectangle(10, 10, 20, 20);
            canvas.FillPath(path, new Rgba32(255, 0, 0), Matrix3x2.Identity, FillRule.EvenOdd);
            Assert.That(image[20, 20].A, Is.Zero);
            canvas.Save();
            canvas.ClipPath(new VectorPath(), Matrix3x2.Identity);
            canvas.FillPath(path, new Rgba32(0, 0, 255), Matrix3x2.Identity);
            Assert.That(image[5, 5], Is.EqualTo(new Rgba32(255, 0, 0)));
            canvas.Restore();
            canvas.FillPath(path, new Rgba32(0, 0, 255), Matrix3x2.Identity);
            Assert.That(image[20, 20], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [TestCase(StrokeCap.Butt, false)]
        [TestCase(StrokeCap.Round, true)]
        [TestCase(StrokeCap.Square, true)]
        public void StrokeCapsExtendOnlyWhenRequested(StrokeCap cap, bool extends)
        {
            var image = new PixelBuffer(50, 50);
            var canvas = new RasterCanvas(image);
            canvas.StrokePath(new VectorPath().MoveTo(10, 20).LineTo(35, 20), new Rgba32(255, 0, 0), new StrokeStyle { Width = 10, Cap = cap }, Matrix3x2.Identity);
            Assert.That(image[7, 20].A > 0, Is.EqualTo(extends));
            Assert.That(image[20, 20].A, Is.EqualTo(255));
        }

        [Test]
        public void AffineStrokeAndDashLengthsFollowUserSpace()
        {
            var image = new PixelBuffer(100, 50);
            var canvas = new RasterCanvas(image);
            canvas.StrokePath(new VectorPath().MoveTo(5, 10).LineTo(40, 10), new Rgba32(255, 0, 0),
                new StrokeStyle { Width = 4, Dashes = new float[] { 5, 5 } }, Matrix3x2.CreateScale(2, 3));
            Assert.That(image[12, 30].A, Is.EqualTo(255));
            Assert.That(image[25, 30].A, Is.Zero);
            Assert.That(image[12, 35].A, Is.EqualTo(255));
            Assert.That(image[12, 37].A, Is.Zero);
        }

        [Test]
        public void AlphaCompositingAndMultiplyUseStraightAlpha()
        {
            var image = new PixelBuffer(10, 10);
            var canvas = new RasterCanvas(image);
            var path = new VectorPath().Rectangle(0, 0, 10, 10);
            canvas.FillPath(path, new Rgba32(255, 0, 0, 128), Matrix3x2.Identity);
            Assert.That(image[5, 5], Is.EqualTo(new Rgba32(255, 0, 0, 128)));
            canvas.FillPath(path, new Rgba32(0, 0, 255, 128), Matrix3x2.Identity);
            Assert.That(image[5, 5].A, Is.InRange(191, 192));
            Assert.That(image[5, 5].R, Is.InRange(84, 86));
            Assert.That(image[5, 5].B, Is.InRange(169, 171));
            image.Clear(new Rgba32(200, 100, 50));
            canvas.FillPath(path, new Rgba32(128, 255, 0), Matrix3x2.Identity, blendMode: ColorBlendMode.Multiply);
            Assert.That(image[5, 5], Is.EqualTo(new Rgba32(100, 100, 0)));
        }
    }
}
