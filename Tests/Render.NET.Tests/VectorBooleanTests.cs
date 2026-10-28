using NUnit.Framework;

namespace Render.NET.Tests
{
    public class VectorBooleanTests
    {
        private static VectorPath Rect(float x, float y, float w = 30, float h = 30) => new VectorPath().MoveTo(x, y).LineTo(x + w, y).LineTo(x + w, y + h).LineTo(x, y + h).Close();
        private static PixelBuffer Draw(VectorPath path, FillRule rule = FillRule.NonZero)
        {
            PixelBuffer? buffer = new PixelBuffer(100, 100);
            new RasterCanvas(buffer).FillPath(path, new Rgba32(255, 255, 255), System.Numerics.Matrix3x2.Identity, rule: rule);
            return buffer;
        }

        [TestCase(PathBooleanOperation.Union, true, true, true)]
        [TestCase(PathBooleanOperation.Intersection, false, true, false)]
        [TestCase(PathBooleanOperation.Difference, true, false, false)]
        [TestCase(PathBooleanOperation.ExclusiveOr, true, false, true)]
        public void BooleanOperationsSelectExpectedRegions(PathBooleanOperation operation, bool left, bool middle, bool right)
        {
            PixelBuffer image = Draw(Rect(10, 10).Combine(Rect(25, 10), operation));
            Assert.Multiple(() =>
            {
                Assert.That(image[15, 20].A > 0, Is.EqualTo(left));
                Assert.That(image[30, 20].A > 0, Is.EqualTo(middle));
                Assert.That(image[50, 20].A > 0, Is.EqualTo(right));
            });
        }

        [TestCase(0)]
        [TestCase(30)]
        [TestCase(50)]
        public void CoincidentTouchingAndDisjointBoundariesAreStable(int offset)
        {
            VectorPath a = Rect(10, 10), b = Rect(10 + offset, 10);
            foreach (PathBooleanOperation operation in Enum.GetValues<PathBooleanOperation>())
            {
                PixelBuffer actual = Draw(a.Combine(b, operation));
                PixelBuffer first = Draw(a), second = Draw(b);
                for (int y = 0; y < 100; y++)
                {
                    for (int x = 0; x < 100; x++)
                    {
                        bool p = first[x, y].A > 127, q = second[x, y].A > 127;
                        bool expected = operation switch
                        {
                            PathBooleanOperation.Union => p || q,
                            PathBooleanOperation.Intersection => p && q,
                            PathBooleanOperation.Difference => p && !q,
                            _ => p ^ q
                        };
                        Assert.That(actual[x, y].A > 127, Is.EqualTo(expected), $"{operation} at {x},{y}");
                    }
                }
            }
        }

        [Test]
        public void DifferenceCreatesHoleAndOffsetPreservesItsWinding()
        {
            VectorPath ring = Rect(10, 10, 80, 80).Combine(Rect(30, 30, 40, 40), PathBooleanOperation.Difference);
            Assert.That(Draw(ring)[50, 50].A, Is.Zero);
            PixelBuffer expanded = Draw(ring.Offset(5));
            Assert.That(expanded[7, 50].A, Is.EqualTo(255));
            Assert.That(expanded[33, 50].A, Is.EqualTo(255));
            Assert.That(expanded[50, 50].A, Is.Zero);
        }

        [Test]
        public void NegativeOffsetCanCollapseAndRoundCornersRemovesTips()
        {
            Assert.That(Draw(Rect(20, 20, 20, 20).Offset(-15)).Pixels.Span.ToArray().All(b => b == 0), Is.True);
            PixelBuffer rounded = Draw(Rect(10, 10, 80, 80).RoundCorners(15));
            Assert.That(rounded[11, 11].A, Is.Zero);
            Assert.That(rounded[50, 11].A, Is.EqualTo(255));
        }

        [Test]
        public void CurvedOperandsAndSelfIntersectionsHaveStableBoundaries()
        {
            VectorPath? circle = new VectorPath().MoveTo(75, 50).CubicTo(75, 64, 64, 75, 50, 75).CubicTo(36, 75, 25, 64, 25, 50).CubicTo(25, 36, 36, 25, 50, 25).CubicTo(64, 25, 75, 36, 75, 50).Close();
            PixelBuffer intersection = Draw(circle.Combine(Rect(50, 0, 50, 100), PathBooleanOperation.Intersection));
            Assert.That(intersection[60, 50].A, Is.EqualTo(255));
            Assert.That(intersection[40, 50].A, Is.Zero);
            VectorPath? bow = new VectorPath().MoveTo(10, 10).LineTo(80, 80).LineTo(10, 80).LineTo(80, 10).Close();
            Assert.DoesNotThrow(() => bow.Combine(Rect(30, 30), PathBooleanOperation.ExclusiveOr));
        }

        [Test]
        public void EvenOddInputsAndCancellationAreRespected()
        {
            VectorPath input = Rect(10, 10, 80, 80).Append(Rect(30, 30, 40, 40));
            Assert.That(Draw(input.Combine(new VectorPath(), PathBooleanOperation.Union, FillRule.EvenOdd))[50, 50].A, Is.Zero);
            Assert.Throws<OperationCanceledException>(() => input.Combine(Rect(1, 1), PathBooleanOperation.Union, cancellationToken: new CancellationToken(true)));
        }
    }
}
