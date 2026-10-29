using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsPixelRegressionTests
    {
        private static string Root => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers");

        public static IEnumerable<TestCaseData> ReferenceFrames()
        {
            using JsonDocument reference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "render-reference.json")));
            foreach (JsonElement frame in reference.RootElement.EnumerateArray())
            {
                yield return new TestCaseData(frame.GetProperty("file").GetString(), frame.GetProperty("size").GetInt32(),
                    frame.GetProperty("frame").GetDouble(), frame.GetProperty("sha256").GetString());
            }
        }

        [TestCaseSource(nameof(ReferenceFrames))]
        public async Task StickerPreservesReferencePixels(string file, int size, double frame, string hash)
        {
            TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(Root, file));
            PixelBuffer image = TgsRenderer.RenderFrame(document, frame, new SvgRenderOptions { Width = size, Height = size, Supersampling = 1 });
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(image.Pixels.Span)), Is.EqualTo(hash));
        }

        [TestCase("roughen-edge-controls.tgs", "858cac3f3e9f81e5ffeff835019af203a2b0783f84642417cbb1cb511431f410")]
        [TestCase("trimmed-translucent-groups.tgs", "a49ddb25058bcd92348bbb11a6fac352c8aca2860c6aa43015323ffe6dc953ae")]
        [TestCase("trimmed-groups-blur.tgs", "bb35454ed59c72350dc82b53c01b8b6f4971dd7a6d87f9b2e247996afb5f4e3b")]
        public async Task LargeEffectsPreservePixelsWithinTheDefaultBufferBudget(string file, string hash)
        {
            TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(Root, file));
            double frame = document.InPoint + ((document.OutPoint - document.InPoint) * .5);
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(120));
            PixelBuffer image = TgsRenderer.RenderFrame(document, frame,
                new SvgRenderOptions { Width = 2560, Height = 2560, Supersampling = 1 }, deadline.Token);
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(image.Pixels.Span)), Is.EqualTo(hash));
        }
    }
}
