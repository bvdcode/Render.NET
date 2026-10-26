using System.Numerics;
using System.Text;
using NUnit.Framework;
using Render.NET.Mesh;

namespace Render.NET.Tests
{
    public class ObjReaderTests
    {
        [Test]
        public async Task TriangulatesConcavePolygonsWithNegativeIndicesAndContinuations()
        {
            using MemoryStream source = Text("""
                v 0 0 0
                v 3 0 0
                v 3 3 0
                v 1 1 0
                v 0 3 0
                o concave
                g first
                f -5 -4 -3 \
                   -2 -1 # comment
                """);
            TriangleMesh mesh = await ObjReader.ReadAsync(source);
            Assert.That(mesh.TriangleCount, Is.EqualTo(3));
            float area = 0;
            for (int corner = 0; corner < mesh.TriangleIndices.Length; corner += 3)
            {
                Vector3 a = mesh.Vertices.Span[mesh.TriangleIndices.Span[corner]];
                Vector3 b = mesh.Vertices.Span[mesh.TriangleIndices.Span[corner + 1]];
                Vector3 c = mesh.Vertices.Span[mesh.TriangleIndices.Span[corner + 2]];
                float signedArea = Vector3.Cross(b - a, c - a).Z * 0.5f;
                Assert.That(signedArea, Is.GreaterThan(0));
                area += signedArea;
            }

            Assert.That(area, Is.EqualTo(6).Within(0.0001f));
            Assert.That(source.CanRead, Is.True);
        }

        [Test]
        public async Task PreservesIndexedNormalsAndHonorsSmoothingOff()
        {
            using MemoryStream source = Text("""
                v 0 0 0
                v 1 0 0
                v 0 1 0
                v 0 0 1
                vn 0 3 0
                vt 0 0
                vt 1 0
                vt 0 1
                f 1/1/1 2/2/1 3/3/1
                s off
                f 1 4 2
                """);
            TriangleMesh mesh = await ObjReader.ReadAsync(source);
            Assert.That(mesh.CornerNormals.Span[0], Is.EqualTo(Vector3.UnitY));
            Assert.That(mesh.CornerNormals.Span[3], Is.EqualTo(Vector3.UnitY));
        }

        [Test]
        public async Task ExplicitSmoothingGroupsShareNormalsOnlyWithinGroup()
        {
            using MemoryStream source = Text("""
                v 0 0 0
                v 1 0 0
                v 0 1 0
                v 0 0 1
                s 1
                f 1 2 3
                f 1 4 2
                s 2
                f 1 3 4
                """);
            TriangleMesh mesh = await ObjReader.ReadAsync(source);
            Assert.That(mesh.CornerNormals.Span[0], Is.EqualTo(Vector3.Normalize(new Vector3(0, 1, 1))));
            Assert.That(mesh.CornerNormals.Span[6], Is.EqualTo(Vector3.UnitX));
        }

        [TestCase("f 0 2 3")]
        [TestCase("f 1 2 4")]
        [TestCase("f -4 -2 -1")]
        [TestCase("f 1//1 2//1 3//1")]
        [TestCase("f 1/1 2/2 3/3")]
        [TestCase("v NaN 0 0\nf 1 2 3")]
        public void RejectsInvalidIndicesAndNonFiniteValues(string face)
        {
            using MemoryStream source = Text("v 0 0 0\nv 1 0 0\nv 0 1 0\n" + face);
            Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(source));
        }

        [Test]
        public void RejectsSelfIntersectingPolygonsAndEnforcesLimits()
        {
            Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text("v 0 0 0\nv 1 1 0\nv 0 1 0\nv 1 0 0\nf 1 2 3 4")));
            const string triangle = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\nf 1 2 3";
            Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text(triangle), new ObjReaderOptions { MaxTriangleCount = 1 }));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text(triangle), new ObjReaderOptions { MaxVertexCount = 2 }));
            Assert.ThrowsAsync<InvalidDataException>(async () => await ObjReader.ReadAsync(Text(triangle), new ObjReaderOptions { MaxInputBytes = 5 }));
        }

        [Test]
        public void SupportsCancellationAndRejectsUntessellatedSurfaces()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await ObjReader.ReadAsync(Text(""), cancellationToken: cancellation.Token));
            Assert.ThrowsAsync<NotSupportedException>(async () => await ObjReader.ReadAsync(Text("surf 0 1 0 1 1 2 3 4")));
        }

        private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));
    }
}
