using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsTests
    {
        private const string Identity = "\"ks\":{\"p\":{\"k\":[0,0]},\"s\":{\"k\":[100,100]},\"o\":{\"k\":100}}";
        private const string Rectangle = "{\"ty\":\"rc\",\"p\":{\"k\":[0,0]},\"s\":{\"k\":[20,20]},\"r\":{\"k\":0}}";
        private const string Fill = "{\"ty\":\"fl\",\"c\":{\"k\":[0.6,0.9,0.1]},\"o\":{\"k\":100}}";
        private static string Wrap(string layers, string assets = "[]", string extra = "") => "{\"v\":\"5.12.2\",\"w\":100,\"h\":100,\"fr\":30,\"ip\":0,\"op\":60,\"assets\":" + assets + ",\"layers\":[" + layers + "]" + extra + "}";
        private static string Layer(string shape, string? transform = null, string extra = "") => "{\"ty\":4," + (transform ?? Identity) + ",\"shapes\":[" + shape + "]" + extra + "}";
        private static TgsDocument Read(string json) => TgsReader.Read(Encoding.UTF8.GetBytes(json));
        private static PixelBuffer Render(string json, double frame = 0)
        {
            TgsDocument doc = Read(json);
            Assert.That(doc.UnsupportedFeatures, Is.Empty);
            return TgsRenderer.RenderFrame(doc, frame, new SvgRenderOptions { Supersampling = 1 });
        }

        [Test]
        public void StaticShapesAndLayerPositionProduceSelectedFrame()
        {
            PixelBuffer image = Render(Wrap(Layer(Rectangle + "," + Fill, Identity.Replace("[0,0]", "[50,50]"))));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(153, 230, 26)));
            Assert.That(image[10, 10].A, Is.Zero);
        }

        [TestCase(0, 20)]
        [TestCase(15, 50)]
        [TestCase(30, 80)]
        [TestCase(45, 80)]
        public void LinearKeyframesInterpolateAndHoldFinalValue(double frame, int x)
        {
            string transform = "\"ks\":{\"p\":{\"a\":1,\"k\":[{\"t\":0,\"s\":[20,50],\"e\":[80,50]},{\"t\":30,\"s\":[80,50]}]}}";
            PixelBuffer image = Render(Wrap(Layer(Rectangle + "," + Fill, transform)), frame);
            Assert.That(image[x, 50].A, Is.EqualTo(255));
            Assert.That(image[Math.Max(0, x - 15), 50].A, Is.Zero);
        }

        [Test]
        public void HoldKeyframesDoNotInterpolate()
        {
            string transform = "\"ks\":{\"p\":{\"a\":1,\"k\":[{\"t\":0,\"h\":1,\"s\":[20,50]},{\"t\":30,\"s\":[80,50]}]}}";
            PixelBuffer image = Render(Wrap(Layer(Rectangle + "," + Fill, transform)), 15);
            Assert.That(image[20, 50].A, Is.EqualTo(255));
            Assert.That(image[50, 50].A, Is.Zero);
        }

        [Test]
        public void TemporalEasingAndSplitPositionAreEvaluated()
        {
            string transform = "\"ks\":{\"p\":{\"s\":true,\"x\":{\"a\":1,\"k\":[{\"t\":0,\"s\":[20],\"e\":[80],\"o\":{\"x\":[.33],\"y\":[0]},\"i\":{\"x\":[.66],\"y\":[0]}},{\"t\":30,\"s\":[80]}]},\"y\":{\"k\":50}}}".Replace("[.33]", "[0.33]").Replace("[.66]", "[0.66]");
            PixelBuffer image = Render(Wrap(Layer(Rectangle + "," + Fill, transform)), 15);
            Assert.That(image[28, 50].A, Is.EqualTo(255));
            Assert.That(image[55, 50].A, Is.Zero);
        }

        [Test]
        public void SpatialBezierMotionUsesArcLength()
        {
            string transform = "\"ks\":{\"p\":{\"a\":1,\"k\":[{\"t\":0,\"s\":[20,50],\"e\":[80,50],\"to\":[0,-40],\"ti\":[0,-40]},{\"t\":30,\"s\":[80,50]}]}}";
            PixelBuffer image = Render(Wrap(Layer(Rectangle + "," + Fill, transform)), 15);
            Assert.That(image[50, 20].A, Is.EqualTo(255));
            Assert.That(image[50, 50].A, Is.Zero);
        }

        [Test]
        public void ParentTransformsApplyWithoutInheritingParentOpacity()
        {
            string parent = "{\"ty\":3,\"ind\":1,\"ks\":{\"p\":{\"k\":[50,50]},\"o\":{\"k\":0}}}";
            PixelBuffer image = Render(Wrap(Layer(Rectangle + "," + Fill, extra: ",\"parent\":1") + "," + parent));
            Assert.That(image[50, 50].A, Is.EqualTo(255));
        }

        [Test]
        public void PrecompositionTimingAndClipAreApplied()
        {
            string asset = "[{\"id\":\"child\",\"w\":20,\"h\":20,\"layers\":[" + Layer(Rectangle + "," + Fill, Identity.Replace("[0,0]", "[15,15]"), ",\"ip\":0,\"op\":20") + "]}]";
            string parent = "{\"ty\":0,\"refId\":\"child\",\"st\":10,\"sr\":2,\"w\":20,\"h\":20," + Identity + "}";
            PixelBuffer image = Render(Wrap(parent, asset), 20);
            Assert.That(image[15, 15].A, Is.EqualTo(255));
            Assert.That(image[23, 15].A, Is.Zero);
            Assert.That(Render(Wrap(parent, asset), 55)[15, 15].A, Is.Zero);
        }

        [Test]
        public void GroupOpacityAppliesOnceAcrossShapes()
        {
            string group = "{\"ty\":\"gr\",\"it\":[" + Rectangle + "," + Fill + ",{\"ty\":\"tr\",\"p\":{\"k\":[50,50]},\"o\":{\"k\":50}}]}";
            Assert.That(Render(Wrap(Layer(group)))[50, 50].A, Is.EqualTo(128));
        }

        [Test]
        public void MorphPathsInterpolateVerticesAndTangents()
        {
            string Path(int x) => "{\"v\":[[" + x + ",20],[" + (x + 20) + ",20],[" + (x + 20) + ",40],[" + x + ",40]],\"i\":[[0,0],[0,0],[0,0],[0,0]],\"o\":[[0,0],[0,0],[0,0],[0,0]],\"c\":true}";
            string shape = "{\"ty\":\"sh\",\"ks\":{\"a\":1,\"k\":[{\"t\":0,\"s\":[" + Path(0) + "],\"e\":[" + Path(60) + "]},{\"t\":30,\"s\":[" + Path(60) + "]}]}}";
            PixelBuffer image = Render(Wrap(Layer(shape + "," + Fill)), 15);
            Assert.That(image[40, 30].A, Is.EqualTo(255));
            Assert.That(image[10, 30].A, Is.Zero);
        }

        [Test]
        public void TrimPathsSelectVisibleStrokeLength()
        {
            string shape = "{\"ty\":\"sh\",\"ks\":{\"k\":{\"v\":[[10,50],[90,50]],\"i\":[[0,0],[0,0]],\"o\":[[0,0],[0,0]],\"c\":false}}}";
            string stroke = "{\"ty\":\"st\",\"c\":{\"k\":[1,0,0]},\"w\":{\"k\":4}}";
            string trim = "{\"ty\":\"tm\",\"s\":{\"k\":25},\"e\":{\"k\":75},\"m\":1}";
            PixelBuffer image = Render(Wrap(Layer(shape + "," + stroke + "," + trim)));
            Assert.That(image[50, 50].R, Is.EqualTo(255));
            Assert.That(image[15, 50].A, Is.Zero);
            Assert.That(image[85, 50].A, Is.Zero);
        }

        [Test]
        public void GradientsUseAnimatedStopsAndOpacityStops()
        {
            string shape = "{\"ty\":\"rc\",\"p\":{\"k\":[50,50]},\"s\":{\"k\":[100,100]}}";
            string gradient = "{\"ty\":\"gf\",\"t\":1,\"s\":{\"k\":[0,0]},\"e\":{\"k\":[100,0]},\"g\":{\"p\":2,\"k\":{\"k\":[0,1,0,0,1,0,0,1,0,1,1,0]}}}";
            PixelBuffer image = Render(Wrap(Layer(shape + "," + gradient)));
            Assert.That(image[1, 50].R, Is.GreaterThan(245));
            Assert.That(image[98, 50].B, Is.GreaterThan(245));
            Assert.That(image[1, 50].A, Is.GreaterThan(245));
            Assert.That(image[98, 50].A, Is.LessThan(10));
        }

        [Test]
        public async Task GzipAndJsonRenderIdenticallyAfterSourceDisposal()
        {
            string json = Wrap(Layer(Rectangle + "," + Fill, Identity.Replace("[0,0]", "[50,50]")));
            using MemoryStream? compressed = new MemoryStream();
            using (GZipStream? gzip = new GZipStream(compressed, CompressionLevel.Optimal, true))
            {
                gzip.Write(Encoding.UTF8.GetBytes(json));
            }

            compressed.Position = 0;
            TgsDocument doc = await TgsReader.ReadAsync(compressed);
            Assert.That(compressed.CanRead, Is.True);
            await compressed.DisposeAsync();
            PixelBuffer a = TgsRenderer.RenderFrame(doc, 0), b = TgsRenderer.RenderAtTime(Read(json), 0);
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.True);
        }

        [Test]
        public void UnsupportedFeaturesAreExplicitAndPartialIsOptIn()
        {
            TgsDocument doc = Read(Wrap("{\"ty\":5,\"ddd\":1,\"tt\":99,\"ef\":[{}]}"));
            Assert.That(doc.UnsupportedFeatures.Count, Is.GreaterThanOrEqualTo(4));
            Assert.Throws<NotSupportedException>(() => TgsRenderer.RenderFrame(doc, 0));
            Assert.DoesNotThrow(() => TgsRenderer.RenderFrame(doc, 0, new SvgRenderOptions { AllowPartialRendering = true }));
        }

        [Test]
        public void CyclicParentsAndAssetsFail()
        {
            TgsDocument parents = Read(Wrap(Layer(Rectangle + "," + Fill, extra: ",\"ind\":1,\"parent\":1")));
            Assert.Throws<InvalidDataException>(() => TgsRenderer.RenderFrame(parents, 0));
            string layer = "{\"ty\":0,\"refId\":\"cycle\"}";
            TgsDocument assets = Read(Wrap(layer, "[{\"id\":\"cycle\",\"layers\":[" + layer + "]}]"));
            Assert.Throws<InvalidDataException>(() => TgsRenderer.RenderFrame(assets, 0));
        }

        [Test]
        public async Task OriginalFixtureRendersDistinctFramesConcurrentlyFromJsonAndTgs()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "orbit.tgs");
            TgsDocument compressed = await TgsReader.ReadFileAsync(path);
            TgsDocument plain = await TgsReader.ReadFileAsync(Path.ChangeExtension(path, ".json"));
            Assert.That(compressed.UnsupportedFeatures, Is.Empty);
            PixelBuffer[] frames = await Task.WhenAll(new[] { 0, 30, 60, 90 }.Select(i => Task.Run(() => TgsRenderer.RenderFrame(compressed, i))));
            Assert.That(frames[0].Pixels.Span.SequenceEqual(frames[1].Pixels.Span), Is.False);
            Assert.That(frames[1].Pixels.Span.SequenceEqual(TgsRenderer.RenderFrame(plain, 30).Pixels.Span), Is.True);
            Assert.That(frames.All(x => x.Width == 512 && x.Height == 512), Is.True);
        }

        [Test]
        public void DecompressionAndFrameGeometryHaveSeparateBudgets()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Wrap(Layer(Rectangle + "," + Fill)));
            using MemoryStream? encoded = new MemoryStream();
            using (GZipStream? gzip = new GZipStream(encoded, CompressionLevel.Optimal, true))
            {
                gzip.Write(bytes);
            }

            Assert.Throws<InvalidDataException>(() => TgsReader.Read(encoded.ToArray(), new TgsReaderOptions { MaximumJsonBytes = 20 }));
            TgsDocument doc = TgsReader.Read(bytes, new TgsReaderOptions { MaximumPathCommands = 2 });
            Assert.Throws<InvalidDataException>(() => TgsRenderer.RenderFrame(doc, 0));
        }

        [Test]
        public void InvalidTimingLimitsAndCancellationFail()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Wrap(""));
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(bytes, new TgsReaderOptions { MaximumInputBytes = 10 }));
            Assert.Throws<InvalidDataException>(() => TgsReader.Read(bytes, new TgsReaderOptions { MaximumItems = 2 }));
            Assert.Throws<OperationCanceledException>(() => TgsReader.Read(bytes, cancellationToken: new CancellationToken(true)));
            TgsDocument doc = Read(Wrap(""));
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrame(doc, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrame(doc, double.NaN));
            Assert.Throws<InvalidDataException>(() => Read(Wrap("", extra: ",\"fr\":0")));
        }
    }
}
