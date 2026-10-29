using System.Text;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsSavedValueTests
    {
        [TestCase("while(true){}")]
        [TestCase("this is not valid JavaScript")]
        [TestCase("$bm_rt=thisComp.layer('missing').position;")]
        public void DefaultPlaybackDoesNotCompileOrExecuteExpressions(string expression)
        {
            string json = """
                {"w":100,"h":100,"fr":30,"ip":0,"op":60,"layers":[{"ty":4,
                "ks":{"p":{"k":[20,50],"x":EXPRESSION}},"shapes":[
                {"ty":"rc","p":{"k":[0,0]},"s":{"k":[8,8]}},{"ty":"fl","c":{"k":[1,0,0]}}]}]}
                """.Replace("EXPRESSION", System.Text.Json.JsonSerializer.Serialize(expression));
            TgsDocument document = Read(json);
            PixelBuffer image = TgsRenderer.RenderFrame(document, 0, Options());
            Assert.That(image[20, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [TestCase(0, 20, 255, 0)]
        [TestCase(15, 50, 128, 128)]
        [TestCase(30, 80, 0, 255)]
        public void SavedPositionAndColorKeyframesRemainAnimated(double frame, int x, int red, int blue)
        {
            PixelBuffer image = TgsRenderer.RenderFrame(AnimatedDocument(), frame, Options());
            Assert.That(image[x, 50], Is.EqualTo(new Rgba32((byte)red, 0, (byte)blue)));
            Assert.That(image[x + 10, 50].A, Is.Zero);
        }

        [Test]
        public void FrameSequenceUsesSavedKeyframes()
        {
            TgsFrame[] frames = TgsRenderer.RenderFrames(AnimatedDocument(),
                new TgsAnimationOptions { FrameRate = 1 }, Options()).ToArray();
            Assert.That(frames.Select(frame => frame.SourceFrame), Is.EqualTo(new[] { 0d, 30 }));
            Assert.That(frames[0].Image[20, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(frames[1].Image[80, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
            Assert.That(frames.All(frame => frame.Warnings.Count == 0), Is.True);
        }

        [TestCase(45)]
        [TestCase(90)]
        [TestCase(135)]
        [TestCase(179.82)]
        public async Task MissingExpressionControllerDoesNotPreventSavedColorPlayback(double frame)
        {
            TgsDocument document = await ReadSticker();
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            TgsFrame rendered = TgsRenderer.RenderFrameDetailed(document, frame, new SvgRenderOptions { Width = 128, Height = 128 });
            Assert.That(rendered.Warnings, Is.Empty);
            Assert.That(Enumerable.Range(0, 128).Any(y => Enumerable.Range(0, 128).Any(x =>
                rendered.Image[x, y] is { R: > 240, G: < 10, B: < 10, A: > 240 })), Is.True);
        }

        [Test]
        public async Task SavedAnimationRetainsItsTransparentOpeningFrame()
        {
            TgsDocument document = await ReadSticker();
            PixelBuffer image = TgsRenderer.RenderFrame(document, 0, new SvgRenderOptions { Width = 128, Height = 128 });
            Assert.That(image.Pixels.ToArray().Where((_, i) => i % 4 == 3).All(alpha => alpha == 0), Is.True);
        }

        private static TgsDocument AnimatedDocument() => Read("""
            {"w":100,"h":100,"fr":30,"ip":0,"op":60,"layers":[{"ty":4,
            "ks":{"p":{"a":1,"k":[{"t":0,"s":[20,50]},{"t":30,"s":[80,50]}],"x":"while(true){}"}},"shapes":[
            {"ty":"rc","p":{"k":[0,0]},"s":{"k":[8,8]}},
            {"ty":"fl","c":{"a":1,"k":[{"t":0,"s":[1,0,0]},{"t":30,"s":[0,0,1]}],"x":"$bm_rt=thisComp.layer('missing').color;"}}]}]}
            """);

        private static TgsDocument Read(string json)
        {
            TgsDocument document = TgsReader.Read(Encoding.UTF8.GetBytes(json));
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            return document;
        }

        private static SvgRenderOptions Options() => new() { Width = 100, Height = 100, Supersampling = 1 };
        private static Task<TgsDocument> ReadSticker() => TgsReader.ReadFileAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers", "missing-color-controller.tgs"));
    }
}
