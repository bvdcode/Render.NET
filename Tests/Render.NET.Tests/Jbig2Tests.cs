using System.Text.Json;
using System.Security.Cryptography;
using NUnit.Framework;
using Render.NET.Jbig2;

namespace Render.NET.Tests
{
    public class Jbig2Tests
    {
        private static string Root => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Jbig2");

        public static IEnumerable<TestCaseData> Fixtures()
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "manifest.json")));
            foreach (var entry in manifest.RootElement.EnumerateArray())
            {
                yield return new TestCaseData(entry.Clone()).SetName("Jbig2_" + entry.GetProperty("name").GetString());
            }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void RecoversEveryAuthoredPixel(JsonElement entry)
        {
            string name = entry.GetProperty("name").GetString()!;
            byte[] encoded = File.ReadAllBytes(Path.Combine(Root, name + ".jb2"));
            byte[] expected = File.ReadAllBytes(Path.Combine(Root, name + ".bin"));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(encoded)), Is.EqualTo(entry.GetProperty("sha256").GetString()));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(expected)), Is.EqualTo(entry.GetProperty("pixelSha256").GetString()));
            var document = Jbig2Decoder.Decode(encoded);
            Assert.That(document.Pages.Count, Is.EqualTo(1));
            var page = document.Pages[0];
            Assert.That(page.Width, Is.EqualTo(entry.GetProperty("width").GetInt32()));
            Assert.That(page.Height, Is.EqualTo(entry.GetProperty("height").GetInt32()));
            Assert.That(page.Data.ToArray(), Is.EqualTo(File.ReadAllBytes(Path.Combine(Root, name + ".bin"))));
        }
    }
}
