using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class VectorFixtureTests
    {
        [TestCase("Svg")]
        [TestCase("Tgs")]
        public void OriginalFixturesMatchTheirRecordedDigests(string kind)
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", kind);
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.GetProperty("files").EnumerateArray())
            {
                string path = Path.Combine(root, entry.GetProperty("file").GetString()!);
                Assert.That(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), Is.EqualTo(entry.GetProperty("sha256").GetString()), path);
            }
        }

        [Test]
        public async Task OriginalSvgFixtureIsAnIndependentRenderableScene()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Svg", "vector-preview.svg");
            SvgDocument doc = await SvgReader.ReadFileAsync(path);
            Assert.That(doc.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = SvgRenderer.Render(doc);
            Assert.That((image.Width, image.Height), Is.EqualTo((800, 480)));
            Assert.That(image[50, 80].G, Is.GreaterThan(200));
        }
    }
}
