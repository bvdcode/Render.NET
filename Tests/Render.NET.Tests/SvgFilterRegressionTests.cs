using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgFilterRegressionTests
    {
        public static IEnumerable<TestCaseData> ReferenceCases()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Svg", "filter-reference.json");
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement item in fixture.RootElement.GetProperty("Cases").EnumerateArray())
            {
                yield return new TestCaseData(item.GetProperty("Svg").GetString(), item.GetProperty("Sha256").GetString())
                    .SetName("FilterMatches0318_" + item.GetProperty("Name").GetString());
            }
        }

        [TestCaseSource(nameof(ReferenceCases))]
        public void FiltersPreserveReferencePixels(string svg, string expectedHash)
        {
            SvgDocument document = SvgReader.Read(svg);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = SvgRenderer.Render(document, new SvgRenderOptions { Supersampling = 1 });
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(image.Pixels.Span)), Is.EqualTo(expectedHash));
        }

        [Test]
        public async Task CottonEffectsPreviewPreservesPixelsWithinItsTwentySecondDeadline()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Svg", "effects-preview.svg");
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
            SvgDocument document = await SvgReader.ReadFileAsync(path, cancellationToken: deadline.Token);
            PixelBuffer image = SvgRenderer.Render(document, new SvgRenderOptions { Width = 2560, Height = 1536, Supersampling = 1 }, deadline.Token);
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(image.Pixels.Span)), Is.EqualTo("5f36a95df3e378952eea5e8c0a5b061aa27d9f81586c9a80b777057752d4d30b"));
        }
    }
}
