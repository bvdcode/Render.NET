using System.IO.Compression;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using Render.NET.Mesh;

namespace Render.NET.Tests
{
    public class ThreeMfReaderTests
    {
        private const string Triangle = """
            <object id="1" name="triangle"><mesh><vertices>
              <vertex x="0" y="0" z="0"/><vertex x="1" y="0" z="0"/><vertex x="0" y="1" z="0"/>
            </vertices><triangles><triangle v1="0" v2="1" v3="2"/></triangles></mesh></object>
            """;

        [Test]
        public async Task BuildsAllInstancesAndExcludesUnusedResources()
        {
            string resources = Triangle + Triangle.Replace("id=\"1\"", "id=\"9\"") + """
                <object id="2"><components><component objectid="1" transform="1 0 0 0 1 0 0 0 1 2 0 0"/></components></object>
                """;
            using MemoryStream package = Package(Model(resources,
                "<item objectid=\"2\" transform=\"2 0 0 0 2 0 0 0 2 3 0 0\"/><item objectid=\"1\"/>"));
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(package);
            Assert.That(document.Objects.Count, Is.EqualTo(3));
            Assert.That(document.BuildItems.Count, Is.EqualTo(2));
            TriangleMesh mesh = document.CreateBuildMesh();
            Assert.That(mesh.TriangleCount, Is.EqualTo(2));
            Assert.That(mesh.Vertices.Span[0], Is.EqualTo(new Vector3(7, 0, 0)));
            Assert.That(mesh.Vertices.Span[1], Is.EqualTo(new Vector3(9, 0, 0)));
            Assert.That(document.CreateBuildItemMesh(0).TriangleCount, Is.EqualTo(1));
            Assert.That(document.CreateObjectMesh(1).Vertices.Span[0], Is.EqualTo(Vector3.Zero));
            Assert.That(package.CanRead, Is.True);
        }

        [Test]
        public async Task ConvertsUnitsAndPreservesWindingThroughMirrors()
        {
            using MemoryStream package = Package(Model(Triangle, "<item objectid=\"1\" transform=\"-1 0 0 0 1 0 0 0 1 2 0 0\"/>", "inch"));
            TriangleMesh mesh = (await ThreeMfReader.ReadAsync(package)).CreateBuildMesh();
            Assert.That(mesh.Vertices.Span[0].X, Is.EqualTo(50.8f).Within(0.0001));
            Vector3 a = mesh.Vertices.Span[mesh.TriangleIndices.Span[0]];
            Vector3 b = mesh.Vertices.Span[mesh.TriangleIndices.Span[1]];
            Vector3 c = mesh.Vertices.Span[mesh.TriangleIndices.Span[2]];
            Assert.That(Vector3.Cross(b - a, c - a).Z, Is.GreaterThan(0));
        }

        [Test]
        public async Task ThumbnailIsExplicitAndIndependentOfGeometry()
        {
            byte[] thumbnail = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];
            using MemoryStream package = Package("not valid XML", thumbnail: thumbnail);
            ThreeMfThumbnail? image = await ThreeMfReader.ReadThumbnailAsync(package);
            Assert.That(image?.ContentType, Is.EqualTo("image/png"));
            Assert.That(image?.Data.ToArray(), Is.EqualTo(thumbnail));
            package.Position = 0;
            _ = Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(package));
            using MemoryStream absent = Package(Model(Triangle, "<item objectid=\"1\"/>"));
            Assert.That(await ThreeMfReader.ReadThumbnailAsync(absent), Is.Null);
        }

        [TestCase("<component objectid=\"2\"/>")]
        [TestCase("<component objectid=\"99\"/>")]
        public void RejectsCyclesAndMissingObjects(string component)
        {
            using MemoryStream package = Package(Model(Triangle + "<object id=\"2\"><components>" + component + "</components></object>", "<item objectid=\"2\"/>"));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(package));
        }

        [TestCase("<item objectid=\"1\" transform=\"1 2 3\"/>")]
        [TestCase("<item objectid=\"1\" transform=\"0 0 0 0 0 0 0 0 0 0 0 0\"/>")]
        [TestCase("<item objectid=\"1\" transform=\"NaN 0 0 0 1 0 0 0 1 0 0 0\"/>")]
        [TestCase("<item objectid=\"99\"/>")]
        public void RejectsInvalidBuild(string build)
        {
            using MemoryStream package = Package(Model(Triangle, build));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(package));
        }

        [Test]
        public void RejectsDuplicateIdsIndicesAndUnknownRequiredExtensions()
        {
            using MemoryStream duplicate = Package(Model(Triangle + Triangle, "<item objectid=\"1\"/>"));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(duplicate));
            using MemoryStream badIndex = Package(Model(Triangle.Replace("v3=\"2\"", "v3=\"99\""), "<item objectid=\"1\"/>"));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(badIndex));
            using MemoryStream extension = Package(Model(Triangle, "<item objectid=\"1\"/>").Replace("<model ", "<model xmlns:x=\"urn:unknown\" requiredextensions=\"x\" "));
            Assert.ThrowsAsync<NotSupportedException>(async () => await ThreeMfReader.ReadAsync(extension));
        }

        [Test]
        public void RejectsExternalPathsEscapesAndXmlEntities()
        {
            foreach (string target in new[] { "../../external.model", "https://example.com/model", "%2e%2e/model" })
            {
                using MemoryStream package = Package(Model(Triangle, "<item objectid=\"1\"/>"), target);
                Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(package));
            }

            using MemoryStream entity = Package("<!DOCTYPE model [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>" + Model(Triangle, "<item objectid=\"1\"/>"));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(entity));
        }

        [Test]
        public async Task LimitsCompressedDecompressedAndExpandedGeometryAndSupportsCancellation()
        {
            byte[] bytes = Package(Model(Triangle, "<item objectid=\"1\"/><item objectid=\"1\"/>")).ToArray();
            _ = Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(new MemoryStream(bytes), new ThreeMfReaderOptions { MaxInputBytes = 5 }));
            _ = Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(new MemoryStream(bytes), new ThreeMfReaderOptions { MaxUncompressedBytes = 32 }));
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(new MemoryStream(bytes), new ThreeMfReaderOptions { MaxTriangleCount = 1 });
            Assert.Throws<InvalidDataException>(() => document.CreateBuildMesh());
            Assert.That(document.CreateBuildItemMesh(0).TriangleCount, Is.EqualTo(1));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            _ = Assert.ThrowsAsync<OperationCanceledException>(async () => await ThreeMfReader.ReadAsync(new MemoryStream(bytes), cancellationToken: cancellation.Token));
            Assert.Throws<OperationCanceledException>(() => document.CreateBuildMesh(cancellation.Token));
        }

        [Test]
        public void RejectsDeepComponentsEvenWhenChildrenWerePreviouslyValidated()
        {
            string resources = Triangle;
            for (int id = 2; id <= 8; id++)
            {
                resources += $"<object id=\"{id}\"><components><component objectid=\"{id - 1}\"/></components></object>";
            }

            using MemoryStream package = Package(Model(resources, "<item objectid=\"8\"/>"));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(package, new ThreeMfReaderOptions { MaxComponentDepth = 4 }));
        }

        [Test]
        public async Task CrossPartObjectSelectionAndThumbnailLimitsAreExplicit()
        {
            using MemoryStream source = ProductionPackage();
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(source);
            Assert.That(document.CreateObjectMesh(1, "/3d/objects/partA.model").TriangleCount, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => document.CreateObjectMesh(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => document.CreateBuildItemMesh(2));
            using MemoryStream oversized = Package(Model(Triangle, "<item objectid=\"1\"/>"), thumbnail: new byte[32]);
            _ = Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadThumbnailAsync(oversized, new ThreeMfReaderOptions { MaxThumbnailBytes = 16 }));
        }

        [Test]
        public async Task ProductionExtensionKeepsPartIdsSeparateAndIgnoresChildBuilds()
        {
            using MemoryStream source = ProductionPackage();
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(source);
            Assert.That(document.Objects.Count, Is.EqualTo(3));
            Assert.That(document.Objects.Count(item => item.Id == 1), Is.EqualTo(2));
            TriangleMesh mesh = document.CreateBuildMesh();
            Assert.That(mesh.TriangleCount, Is.EqualTo(2));
            Assert.That(mesh.Vertices.ToArray().Max(vertex => vertex.X), Is.EqualTo(21));
        }

        [Test]
        public async Task EmptyBuildRequiresExplicitResourceSelection()
        {
            using MemoryStream package = Package(Model(Triangle, ""));
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(package);
            Assert.That(document.BuildItems, Is.Empty);
            Assert.Throws<InvalidDataException>(() => document.CreateBuildMesh());
            Assert.That(document.CreateObjectMesh(1).TriangleCount, Is.EqualTo(1));
        }

        [Test]
        public void RejectsUndeclaredCrossPartReferencesAndAlternativeRepresentations()
        {
            string model = Model(Triangle, "<item objectid=\"1\" p:path=\"/3D/model.model\"/>")
                .Replace("<model ", "<model xmlns:p=\"http://schemas.microsoft.com/3dmanufacturing/production/2015/06\" ");
            using MemoryStream missingRelationship = Package(model);
            Assert.ThrowsAsync<InvalidDataException>(async () => await ThreeMfReader.ReadAsync(missingRelationship));
            using MemoryStream alternative = Package(model.Replace("<resources>", "<resources><a:alternatives xmlns:a=\"http://schemas.microsoft.com/3dmanufacturing/production/alternatives/2021/04\"><a:alternative/></a:alternatives>"));
            Assert.ThrowsAsync<NotSupportedException>(async () => await ThreeMfReader.ReadAsync(alternative));
        }

        private static string Model(string resources, string build, string unit = "millimeter") =>
            $"<model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" unit=\"{unit}\"><resources>{resources}</resources><build>{build}</build></model>";

        private static MemoryStream ProductionPackage()
        {
            string model = Model("""
                <object id="2"><components>
                  <component objectid="1" p:path="/3D/Objects/partA.model"/>
                  <component objectid="1" p:path="/3D/Objects/partB.model" transform="1 0 0 0 1 0 0 0 1 20 0 0"/>
                </components></object>
                """, "<item objectid=\"2\"/>").Replace("<model ", "<model xmlns:p=\"http://schemas.microsoft.com/3dmanufacturing/production/2015/06\" requiredextensions=\"p\" ");
            return Package(model, parts: new Dictionary<string, string>
            {
                ["3D/Objects/partA.model"] = Model(Triangle, "<item objectid=\"999\"/>"),
                ["3D/Objects/partB.model"] = Model(Triangle, ""),
                ["3D/_rels/model.model.rels"] = """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="a" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel" Target="Objects/partA.model"/>
                      <Relationship Id="b" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel" Target="Objects/partB.model"/>
                    </Relationships>
                    """
            });
        }

        private static MemoryStream Package(string model, string target = "/3D/model.model", byte[]? thumbnail = null, Dictionary<string, string>? parts = null)
        {
            MemoryStream bytes = new();
            using (ZipArchive archive = new(bytes, ZipArchiveMode.Create, true))
            {
                string thumb = thumbnail == null ? "" : "<Relationship Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail\" Target=\"/Metadata/preview.png\" Id=\"thumb\"/>";
                using (StreamWriter writer = new(archive.CreateEntry("_rels/.rels").Open(), Encoding.UTF8))
                {
                    writer.Write($"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"{target}\" Id=\"model\"/>{thumb}</Relationships>");
                }

                using (StreamWriter writer = new(archive.CreateEntry("3D/model.model").Open(), Encoding.UTF8))
                {
                    writer.Write(model);
                }

                foreach (var part in parts ?? [])
                {
                    using StreamWriter writer = new(archive.CreateEntry(part.Key).Open(), Encoding.UTF8);
                    writer.Write(part.Value);
                }

                if (thumbnail != null)
                {
                    using Stream image = archive.CreateEntry("Metadata/preview.png").Open();
                    image.Write(thumbnail);
                }
            }

            bytes.Position = 0;
            return bytes;
        }
    }
}
