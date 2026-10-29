using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgFilterMemoryTests
    {
        [Test]
        public void LongFilterChainReusesStorageWithinItsBudget()
        {
            string chain = string.Concat(Enumerable.Repeat("<feColorMatrix/>", 32));
            SvgDocument document = Document(chain);
            SvgRenderOptions options = new() { Width = 1024, Height = 1024, Supersampling = 1, MaximumEffectBytes = 16 * 1024 * 1024 };
            long before = GC.GetAllocatedBytesForCurrentThread();
            PixelBuffer image = SvgRenderer.Render(document, options);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(image[512, 512], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(allocated, Is.LessThan(32L * 1024 * 1024));
        }

        [Test]
        public void NamedInputsSurviveUntilTheirLastMergeConsumer()
        {
            string chain = "<feOffset result='original'/><feFlood flood-color='blue' result='blue'/>" +
                string.Concat(Enumerable.Repeat("<feFlood flood-color='lime'/>", 16)) +
                "<feMerge><feMergeNode in='blue'/><feMergeNode in='original'/></feMerge>";
            PixelBuffer image = SvgRenderer.Render(Document(chain), new SvgRenderOptions { Supersampling = 1 });
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[5, 5], Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public void SimultaneouslyLiveInputsCannotExceedTheBudget()
        {
            string chain = string.Concat(Enumerable.Range(0, 8).Select(i => $"<feFlood result='n{i}'/>")) +
                "<feMerge>" + string.Concat(Enumerable.Range(0, 8).Select(i => $"<feMergeNode in='n{i}'/>")) + "</feMerge>";
            Assert.Throws<InvalidOperationException>(() => SvgRenderer.Render(Document(chain),
                new SvgRenderOptions { Supersampling = 1, MaximumEffectBytes = 4 * 100 * 100 * 4 }));
        }

        [TestCase("<feComposite/>", 255)]
        [TestCase("<feColorMatrix in='SourceAlpha'/>", 0)]
        public void OriginalSourceSurvivesColorOperationsWhenReferencedLater(string consumer, int red)
        {
            string chain = "<feComponentTransfer><feFuncA type='linear' slope='0'/></feComponentTransfer>" + consumer;
            PixelBuffer image = SvgRenderer.Render(Document(chain), new SvgRenderOptions { Supersampling = 1 });
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32((byte)red, 0, 0)));
        }

        [Test]
        public void ExcessiveBlurStillReportsItsDeviceSpaceLimit()
        {
            InvalidDataException? error = Assert.Throws<InvalidDataException>(() => SvgRenderer.Render(
                Document("<feGaussianBlur stdDeviation='257'/>"), new SvgRenderOptions { Supersampling = 1 }));
            Assert.That(error!.Message, Does.Contain("256 device samples"));
        }

        private static SvgDocument Document(string chain) => SvgReader.Read(
            "<svg width='100' height='100'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='100' height='100'>" +
            chain + "</filter></defs><rect x='20' y='20' width='60' height='60' fill='red' filter='url(#f)'/></svg>");
    }
}
