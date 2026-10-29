using System.Numerics;
using NUnit.Framework;

namespace Render.NET.Tests
{
    public class RasterClipMemoryTests
    {
        [Test]
        public void CoveringRectanglePreservesAnExistingClipWithoutAllocatingAnotherMask()
        {
            PixelBuffer image = new(1024, 1024);
            RasterCanvas canvas = new(image);
            canvas.ClipPath(new VectorPath().Rectangle(10, 10, 20, 20), Matrix3x2.Identity);
            VectorPath covering = new VectorPath().Rectangle(-4096, -4096, 8192, 8192);
            long before = GC.GetAllocatedBytesForCurrentThread();
            canvas.ClipPath(covering, Matrix3x2.CreateRotation(.3f));
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(256 * 1024));
            canvas.FillPath(covering, new Rgba32(255, 0, 0), Matrix3x2.Identity);
            Assert.That(image[15, 15].A, Is.EqualTo(255));
            Assert.That(image[5, 5].A, Is.Zero);
            Assert.That(image[30, 15].A, Is.Zero);
        }

        [Test]
        public void RectangleThatMissesPixelCentersStillClipsThosePixels()
        {
            PixelBuffer image = new(4, 4);
            RasterCanvas canvas = new(image);
            canvas.ClipPath(new VectorPath().Rectangle(.6f, .6f, 4, 4), Matrix3x2.Identity);
            canvas.FillPath(new VectorPath().Rectangle(0, 0, 4, 4), new Rgba32(255, 0, 0), Matrix3x2.Identity);
            Assert.That(image[0, 0].A, Is.Zero);
            Assert.That(image[1, 1].A, Is.EqualTo(255));
        }
    }
}
