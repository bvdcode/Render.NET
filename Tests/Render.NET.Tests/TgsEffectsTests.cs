using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsEffectsTests
    {
        private const string Red = "{\"ty\":\"fl\",\"c\":{\"k\":[1,0,0]},\"o\":{\"k\":100}}";
        private static string Rect(int x, int y, int w = 30, int h = 30) => $$$$"""{"ty":"rc","p":{"k":[{{{{x}}}},{{{{y}}}}]},"s":{"k":[{{{{w}}}},{{{{h}}}}]}}""";
        private static string Layer(string shapes, string extra = "") => "{\"ty\":4,\"ks\":{},\"shapes\":[" + shapes + "]" + extra + "}";
        private static string Effect(int type, params object? [] values) => ",\"ef\":" + JsonSerializer.Serialize(new[] { new { ty = type, ef = values.Select(k => new { v = new { k } }).ToArray() } });
        private static string Mask(string mode, bool invert = false, int opacity = 100, int x = 20, int w = 40, int feather = 0) => $$$$"""{"mode":"{{{{mode}}}}","inv":{{{{invert.ToString().ToLowerInvariant()}}}},"o":{"k":{{{{opacity}}}}},"f":{"k":[{{{{feather}}}},{{{{feather}}}}]},"pt":{"k":{"v":[[{{{{x}}}},20],[{{{{x + w}}}},20],[{{{{x + w}}}},80],[{{{{x}}}},80]],"i":[[0,0],[0,0],[0,0],[0,0]],"o":[[0,0],[0,0],[0,0],[0,0]],"c":true}}}""";
        private static PixelBuffer Render(string layers, double frame = 0)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"v\":\"5.13.0\",\"w\":100,\"h\":100,\"fr\":30,\"ip\":0,\"op\":60,\"layers\":[" + layers + "]}");
            TgsDocument document = TgsReader.Read(bytes);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            return TgsRenderer.RenderFrame(document, frame, new SvgRenderOptions { Supersampling = 1 });
        }

        [TestCase("a", true, true, true)]
        [TestCase("s", true, false, false)]
        [TestCase("i", false, true, false)]
        [TestCase("f", true, false, true)]
        public void MultipleMasksCombineTheirCoverage(string mode, bool left, bool center, bool right)
        {
            PixelBuffer image = Render(Layer(Rect(50, 50, 100, 100) + "," + Red, ",\"masksProperties\":[" + Mask("a") + "," + Mask(mode, x: 40) + "]"));
            Assert.That(image[30, 50].A > 127, Is.EqualTo(left));
            Assert.That(image[50, 50].A > 127, Is.EqualTo(center));
            Assert.That(image[70, 50].A > 127, Is.EqualTo(right));
        }

        [Test]
        public void MaskInversionOpacityAndFeatherAffectAlpha()
        {
            string content = Rect(50, 50, 100, 100) + "," + Red;
            PixelBuffer inverse = Render(Layer(content, ",\"masksProperties\":[" + Mask("a", true) + "]"));
            Assert.That(inverse[50, 50].A, Is.Zero);
            Assert.That(inverse[10, 50].A, Is.EqualTo(255));
            PixelBuffer partial = Render(Layer(content, ",\"masksProperties\":[" + Mask("a", opacity: 50) + "]"));
            Assert.That(partial[50, 50].A, Is.EqualTo(128).Within(1));
            PixelBuffer feather = Render(Layer(content, ",\"masksProperties\":[" + Mask("a", feather: 8) + "]"));
            Assert.That(feather[19, 50].A, Is.InRange(30, 150));
        }

        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public void AlphaAndLumaTrackMattesCanBeInverted(int mode, bool center)
        {
            string matte = Layer(Rect(50, 50), ",\"ind\":1").Replace("\"shapes\":[", "\"shapes\":[");
            matte = Layer(Rect(50, 50) + ",{\"ty\":\"fl\",\"c\":{\"k\":[1,1,1]}}");
            PixelBuffer image = Render(matte + "," + Layer(Rect(50, 50, 100, 100) + "," + Red, ",\"tt\":" + mode));
            Assert.That(image[50, 50].A > 127, Is.EqualTo(center));
            Assert.That(image[10, 10].A > 127, Is.EqualTo(!center));
            Assert.That(image[50, 50].R, Is.EqualTo(center ? 255 : 0));
        }

        [Test]
        public void RepeaterMovesCopiesAndInterpolatesOpacity()
        {
            string repeat = "{\"ty\":\"rp\",\"c\":{\"k\":3},\"o\":{\"k\":0},\"tr\":{\"p\":{\"k\":[30,0]},\"so\":{\"k\":100},\"eo\":{\"k\":20}}}";
            PixelBuffer image = Render(Layer(Rect(15, 50, 10, 10) + "," + Red + "," + repeat));
            Assert.That(image[15, 50].A, Is.EqualTo(255));
            Assert.That(image[45, 50].A, Is.EqualTo(153).Within(1));
            Assert.That(image[75, 50].A, Is.EqualTo(51).Within(1));
        }

        [TestCase(2, true, true, true)]
        [TestCase(3, false, false, true)]
        [TestCase(4, false, true, false)]
        [TestCase(5, true, false, true)]
        public void MergePathsSupportsSetOperations(int mode, bool left, bool center, bool right)
        {
            PixelBuffer image = Render(Layer(Rect(30, 50, 40, 40) + "," + Rect(50, 50, 40, 40) + "," + Red + "," + "{\"ty\":\"mm\",\"mm\":" + mode + "}"));
            Assert.That(image[20, 50].A > 127, Is.EqualTo(left));
            Assert.That(image[40, 50].A > 127, Is.EqualTo(center));
            Assert.That(image[60, 50].A > 127, Is.EqualTo(right));
        }

        [TestCase("rd", "r", 15)]
        [TestCase("op", "a", -8)]
        [TestCase("pb", "a", 50)]
        public void CornerOffsetAndPuckerModifiersChangeGeometry(string type, string parameter, int amount)
        {
            PixelBuffer image = Render(Layer(Rect(50, 50, 60, 60) + "," + Red + ",{\"ty\":\"" + type + "\",\"" + parameter + "\":{\"k\":" + amount + "}}"));
            Assert.That(image[21, 21].A, Is.Zero);
            Assert.That(image[50, 50].A, Is.EqualTo(255));
        }

        [Test]
        public void FillEffectPreservesSourceAlphaAndChangesColor()
        {
            string effect = ",\"ef\":[{\"ty\":21,\"ef\":[{},{},{\"v\":{\"k\":[0,1,0]}},{},{},{},{\"v\":{\"k\":0.5}}]}]";
            PixelBuffer image = Render(Layer(Rect(50, 50) + "," + Red, effect));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(0, 255, 0, 128)));
            Assert.That(image[5, 5].A, Is.Zero);
        }

        [Test]
        public void TintAndTritoneUseMeanLinearLightIntensity()
        {
            PixelBuffer tint = Render(Layer(Rect(50, 50) + "," + Red, Effect(20, new[] { 0, 0, 0 }, new[] { 1, 1, 1 }, 100)));
            Assert.That(tint[50, 50].R, Is.InRange(155, 157));
            Assert.That(tint[50, 50].G, Is.EqualTo(tint[50, 50].R));
            PixelBuffer tritone = Render(Layer(Rect(50, 50) + "," + Red, Effect(23, new[] { 1, 1, 1 }, new[] { 0, 0, 1 }, new[] { 0, 0, 0 })));
            Assert.That(tritone[50, 50].B, Is.EqualTo(255));
            Assert.That(tritone[50, 50].R, Is.InRange(55, 60));
            Assert.That(tritone[5, 5].A, Is.Zero);
        }

        [Test]
        public void LevelsCanInvertOneChannelWithoutChangingAlpha()
        {
            var values = new object? [36];
            values[10] = 1;
            values[11] = 0;
            PixelBuffer image = Render(Layer(Rect(50, 50) + "," + Red, Effect(24, values)));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(0, 0, 0)));
            Assert.That(image[5, 5].A, Is.Zero);
        }

        [Test]
        public void TransformEffectMovesTheCompleteLayerAndAppliesOpacityOnce()
        {
            PixelBuffer image = Render(Layer(Rect(50, 50) + "," + Red, Effect(35, new[] { 0, 0 }, new[] { 20, 0 }, 1, 100, 100, 0, 0, 0, 50)));
            Assert.That(image[70, 50], Is.EqualTo(new Rgba32(255, 0, 0, 128)));
            Assert.That(image[40, 50].A, Is.Zero);
        }

        [TestCase(2, 0, 255)]
        [TestCase(3, 0, 255)]
        public void StrokeEffectSupportsTransparentAndRevealPaintStyles(int style, int center, int edge)
        {
            string masks = ",\"masksProperties\":[" + Mask("n") + "]";
            PixelBuffer image = Render(Layer(Rect(50, 50, 100, 100) + "," + Red, masks + Effect(22, 1, 0, 0, new[] { 0, 0, 1 }, 3, 0, 1, 0, 100, 0, style)));
            Assert.That(image[50, 50].A, Is.EqualTo(center));
            Assert.That(image[20, 50].A, Is.EqualTo(edge));
            Assert.That(image[20, 50].B, Is.EqualTo(style == 2 ? 255 : 0));
        }

        [Test]
        public void BlurRunsAfterLayerMasks()
        {
            string effect = Effect(29, 10, 1, 0);
            PixelBuffer image = Render(Layer(Rect(50, 50, 100, 100) + "," + Red, ",\"masksProperties\":[" + Mask("a") + "]" + effect));
            Assert.That(image[18, 50].A, Is.InRange(30, 100));
        }

        [TestCase(15, 50)]
        [TestCase(45, 80)]
        public void AutomaticOrientationFollowsAnimatedPosition(double frame, int y)
        {
            string layer = Layer(Rect(0, 0, 30, 6) + "," + Red, ",\"ao\":1").Replace("\"ks\":{}", "\"ks\":{\"p\":{\"k\":[{\"t\":0,\"s\":[50,20],\"e\":[50,80]},{\"t\":30,\"s\":[50,80]}]}}");
            PixelBuffer image = Render(layer, frame);
            Assert.That(image[50, y + 10].A, Is.EqualTo(255));
            Assert.That(image[60, y].A, Is.Zero);
        }

        [Test]
        public async Task OriginalEffectsAndGeometryFixturesRenderFromJsonAndTgsConcurrently()
        {
            foreach (string name in new[]
            {
                "effects",
                "geometry"
            }

            )
            {
                string folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs");
                TgsDocument json = await TgsReader.ReadFileAsync(Path.Combine(folder, name + ".json"));
                TgsDocument tgs = await TgsReader.ReadFileAsync(Path.Combine(folder, name + ".tgs"));
                Assert.That(json.UnsupportedFeatures, Is.Empty);
                PixelBuffer[] frames = await Task.WhenAll(new[] { json, tgs }.Select(document => Task.Run(() => TgsRenderer.RenderFrame(document, 0))));
                Assert.That(frames[0].Pixels.Span.SequenceEqual(frames[1].Pixels.Span), Is.True);
            }
        }

        [Test]
        public void GaussianBlurEffectSpreadsColorAndKeepsTransparentExterior()
        {
            string effect = ",\"ef\":[{\"ty\":29,\"ef\":[{\"v\":{\"k\":10}},{\"v\":{\"k\":1}},{\"v\":{\"k\":0}}]}]";
            PixelBuffer image = Render(Layer(Rect(50, 50) + "," + Red, effect));
            Assert.That(image[33, 50].A, Is.InRange(25, 100));
            Assert.That(image[50, 50].R, Is.EqualTo(255));
            Assert.That(image[5, 5].A, Is.Zero);
        }
    }
}
