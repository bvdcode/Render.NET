using System.Text;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsGroupTests
    {
        private const string Rectangle = "{\"ty\":\"rc\",\"p\":{\"k\":[50,50]},\"s\":{\"k\":[80,80]}}";
        private const string Fill = "{\"ty\":\"fl\",\"c\":{\"k\":[1,0,0]}}";
        private const string Stroke = "{\"ty\":\"st\",\"c\":{\"k\":[0,0,1]},\"w\":{\"k\":4}}";

        [Test]
        public void ParentPaintsRemainInsideTheSubgroupOpacity()
        {
            string group = "{\"ty\":\"gr\",\"it\":[" + Rectangle + "," + Fill + ",{\"ty\":\"tr\",\"o\":{\"k\":50}}]}";
            PixelBuffer image = Render(group + "," + Stroke);
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(255, 0, 0, 128)));
            Assert.That(image[9, 50], Is.EqualTo(new Rgba32(0, 0, 255, 128)));
        }

        [Test]
        public void ZeroOpacitySubgroupsDoNotLeakIntoParentPaints()
        {
            string group = "{\"ty\":\"gr\",\"it\":[" + Rectangle + ",{\"ty\":\"tr\",\"o\":{\"k\":0}}]}";
            Assert.That(Render(group + "," + Fill).Pixels.ToArray(), Is.All.Zero);
        }

        [Test]
        public void SimultaneousTrimReachesIndependentlyStyledSubgroups()
        {
            string path = "{\"ty\":\"sh\",\"ks\":{\"k\":{\"v\":[[10,50],[90,50]],\"i\":[[0,0],[0,0]],\"o\":[[0,0],[0,0]],\"c\":false}}}";
            string group = "{\"ty\":\"gr\",\"it\":[" + path + "," + Stroke + ",{\"ty\":\"tr\"}]}";
            PixelBuffer image = Render(group + ",{\"ty\":\"tm\",\"m\":1,\"s\":{\"k\":0},\"e\":{\"k\":50}}");
            Assert.That(image[25, 50].A, Is.EqualTo(255));
            Assert.That(image[75, 50].A, Is.Zero);
        }

        [Test]
        public async Task HiddenUnsupportedVideoLayerDoesNotRejectTheVisibleShapes()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers", "hidden-video-layer.tgs");
            TgsDocument document = await TgsReader.ReadFileAsync(path);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = TgsRenderer.RenderFrame(document, 20, new SvgRenderOptions { Supersampling = 1 });
            Assert.That(image.Pixels.ToArray().Where((_, i) => i % 4 == 3).Any(alpha => alpha > 0), Is.True);
        }

        [TestCase("translucent-groups.tgs", 0)]
        [TestCase("controller-expressions.tgs", 60)]
        [TestCase("elastic-controls.tgs", 60)]
        [TestCase("empty-puppet-mesh.tgs", 0)]
        [TestCase("hue-saturation-controls.tgs", 60)]
        [TestCase("layer-controls.tgs", 60)]
        [TestCase("legacy-gaussian-blur.tgs", 60)]
        [TestCase("set-matte.tgs", 60)]
        [TestCase("trimmed-translucent-groups.tgs", 60)]
        [TestCase("trimmed-groups-blur.tgs", 45)]
        public async Task StickerGroupScopesRenderWithoutPartialRendering(string name, double frame)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers", name);
            TgsDocument document = await TgsReader.ReadFileAsync(path);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = TgsRenderer.RenderFrame(document, frame, new SvgRenderOptions { Supersampling = 1 });
            Assert.That(image.Pixels.ToArray().Where((_, i) => i % 4 == 3).Any(alpha => alpha > 0), Is.True);
        }

        private static PixelBuffer Render(string shapes)
        {
            string json = "{\"w\":100,\"h\":100,\"fr\":30,\"ip\":0,\"op\":60,\"layers\":[{\"ty\":4,\"shapes\":[" + shapes + "]}]}";
            TgsDocument document = TgsReader.Read(Encoding.UTF8.GetBytes(json));
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            return TgsRenderer.RenderFrame(document, 0, new SvgRenderOptions { Supersampling = 1 });
        }
    }
}
