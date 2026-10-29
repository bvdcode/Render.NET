using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace Render.NET.Tests
{
    public class TgsStickerFixtureTests
    {
        [Test]
        public void StickerInputsMatchTheirRecordedDigests()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
            JsonElement files = manifest.RootElement.GetProperty("files");
            Assert.That(files.GetArrayLength(), Is.EqualTo(15));
            foreach (JsonElement file in files.EnumerateArray())
            {
                string path = Path.Combine(root, file.GetProperty("file").GetString()!);
                string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
                Assert.That(hash, Is.EqualTo(file.GetProperty("sha256").GetString()), path);
            }
        }
    }
}
