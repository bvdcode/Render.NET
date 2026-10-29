using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsStickerFrameTests
    {
        private static string Root => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers");
        private static IEnumerable<string> StickerFiles => Directory.EnumerateFiles(Root, "*.tgs").Select(path => Path.GetFileName(path)).Order();

        [TestCaseSource(nameof(StickerFiles))]
        public async Task DefaultPlaybackCoversCompositionTimeline(string name)
        {
            TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(Root, name));
            VerifyTimeline(document, name);
        }

        [TestCase("hue-saturation-controls.tgs")]
        [TestCase("roughen-edge-controls.tgs")]
        [TestCase("bounce-path-expression.tgs")]
        [TestCase("controller-expressions.tgs")]
        [TestCase("trimmed-translucent-groups.tgs")]
        [TestCase("set-matte.tgs")]
        [TestCase("legacy-gaussian-blur.tgs")]
        [TestCase("hidden-video-layer.tgs")]
        [TestCase("layer-controls.tgs")]
        [TestCase("translucent-groups.tgs")]
        [TestCase("elastic-expressions.tgs")]
        [TestCase("elastic-controls.tgs")]
        [TestCase("empty-puppet-mesh.tgs")]
        [TestCase("trimmed-groups-blur.tgs")]
        public async Task StrictFramesCoverCompositionTimeline(string name)
        {
            TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(Root, name), new TgsReaderOptions { Expressions = new TgsExpressionOptions() });
            VerifyTimeline(document, name);
        }

        private static void VerifyTimeline(TgsDocument document, string name)
        {
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            foreach (double progress in new[] { 0, .25, .5, .75, .999 })
            {
                double frame = document.InPoint + ((document.OutPoint - document.InPoint) * progress);
                TgsFrame rendered = TgsRenderer.RenderFrameDetailed(document, frame, new SvgRenderOptions { Width = 128, Height = 128, Supersampling = 1 });
                Assert.That(rendered.Warnings, Is.Empty, $"{name}, frame {frame}");
                Assert.That(rendered.Image.Width, Is.EqualTo(128));
            }
        }
    }
}
