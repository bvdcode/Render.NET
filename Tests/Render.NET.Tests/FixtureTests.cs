using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Mesh;

namespace Render.NET.Tests
{
    public class FixtureTests
    {
        private static readonly string Root = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

        [Test]
        public void DownloadedFixturesMatchPinnedChecksums()
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string path = Path.Combine(Root, entry.GetProperty("file").GetString()!);
                Assert.That(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), Is.EqualTo(entry.GetProperty("sha256").GetString()), path);
            }
        }

        [TestCase("spot_triangulated.obj")]
        [TestCase("spot_quadrangulated.obj")]
        [TestCase("bob_tri.obj")]
        [TestCase("benchy.stl")]
        [TestCase("benchy.3mf")]
        [TestCase("dog.3mf")]
        [TestCase("mug.3mf")]
        [TestCase("mech.3mf")]
        public async Task LoadsAndRendersDownloadedModel(string name)
        {
            TriangleMesh mesh = await MeshReader.ReadFileAsync(Path.Combine(Root, name));
            Assert.That(mesh.TriangleCount, Is.GreaterThan(0));
            PixelBuffer image = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 96, Height = 96, ShadowMapSize = 128 });
            int covered = image.Pixels.ToArray().Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0);
            Assert.That(covered, Is.GreaterThan(30), name);
            using MemoryStream png = new();
            await PngEncoder.WriteAsync(image, png);
            Assert.That(png.Length, Is.GreaterThan(100));
        }

        [TestCase("mug.3mf", 2, 2290)]
        [TestCase("mech.3mf", 66, 19116)]
        [TestCase("dog.3mf", 28, 35256)]
        [TestCase("benchy.3mf", 1, 225154)]
        public async Task SelectsEveryInstanceInDownloadedBuild(string name, int instances, int triangles)
        {
            await using FileStream source = File.OpenRead(Path.Combine(Root, name));
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(source);
            Assert.That(document.BuildItems.Count, Is.EqualTo(instances));
            Assert.That(document.CreateBuildMesh().TriangleCount, Is.EqualTo(triangles));
            int selected = Enumerable.Range(0, instances).Sum(index => document.CreateBuildItemMesh(index).TriangleCount);
            Assert.That(selected, Is.EqualTo(triangles));
        }

        [TestCase("spot_quadrangulated.obj", "obj")]
        [TestCase("mug.3mf", "3mf")]
        public async Task ReadsNewFormatsFromCurrentPositionWithoutSeeking(string name, string extension)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(Root, name));
            using MemoryStream inner = new(new byte[7].Concat(bytes).ToArray());
            inner.Position = 7;
            using StlReaderTests.NonSeekableStream source = new(inner);
            TriangleMesh mesh = await MeshReader.ReadAsync(source, extension);
            Assert.That(mesh.TriangleCount, Is.GreaterThan(0));
            Assert.That(inner.CanRead, Is.True);
        }

    }
}
