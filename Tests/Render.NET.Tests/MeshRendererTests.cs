using System.Numerics;
using NUnit.Framework;
using Render.NET.Mesh;

namespace Render.NET.Tests
{
    public class MeshRendererTests
    {
        [Test]
        public async Task RendersStlStreamToPngEndToEnd()
        {
            using MemoryStream source = new(StlReaderTests.BinaryTriangle());
            TriangleMesh mesh = await StlReader.ReadAsync(source);
            PixelBuffer image = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 64, Height = 64, ShadowMapSize = 64 });
            using MemoryStream destination = new();
            await PngEncoder.WriteAsync(image, destination);
            Assert.That(image.Pixels.ToArray().Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0), Is.GreaterThan(200));
            Assert.That(destination.Length, Is.GreaterThan(100));
        }

        [Test]
        public void DepthTestShowsNearestSurfaceRegardlessOfTriangleOrder()
        {
            Vector3[] vertices = [new(-1, -1, 1), new(1, -1, 1), new(0, 1, 1),
                new(-1, -1, -1), new(1, -1, -0.5f), new(0, 1, -1)];
            MeshRenderOptions options = new()
            {
                Width = 64, Height = 64, Supersampling = 1, CameraDirection = Vector3.UnitZ,
                CameraUp = Vector3.UnitY, Shadows = false, AmbientOcclusion = false
            };
            PixelBuffer first = MeshRenderer.Render(new TriangleMesh(vertices, [0, 1, 2, 3, 4, 5]), options);
            PixelBuffer reversed = MeshRenderer.Render(new TriangleMesh(vertices, [3, 4, 5, 0, 1, 2]), options);
            PixelBuffer frontOnly = MeshRenderer.Render(new TriangleMesh(vertices.AsSpan(0, 3), [0, 1, 2]), options);
            Assert.That(first.Pixels.ToArray(), Is.EqualTo(reversed.Pixels.ToArray()));
            Assert.That(first[32, 32], Is.EqualTo(frontOnly[32, 32]));
            Assert.That(first[32, 32].A, Is.EqualTo(255));
        }

        [Test]
        public void FramingHandlesAspectRatioAndVerySmallOrLargeCoordinates()
        {
            Vector3[] vertices = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 1)];
            MeshRenderOptions options = new() { Width = 120, Height = 60, Shadows = false, AmbientOcclusion = false };
            byte[] expected = MeshRenderer.Render(new TriangleMesh(vertices, [0, 1, 2]), options).Pixels.ToArray();
            foreach (float scale in new[] { MathF.Pow(2, -90), MathF.Pow(2, 90) })
            {
                Vector3[] transformed = vertices.Select(vertex => vertex * scale + new Vector3(4 * scale)).ToArray();
                PixelBuffer image = MeshRenderer.Render(new TriangleMesh(transformed, [0, 1, 2]), options);
                Assert.That(image.Pixels.ToArray(), Is.EqualTo(expected));
            }

            int[] covered = Enumerable.Range(0, expected.Length / 4).Where(index => expected[index * 4 + 3] > 0).ToArray();
            Assert.That(covered.Length, Is.GreaterThan(100));
            Assert.That(covered.Min(index => index % 120), Is.GreaterThan(0));
            Assert.That(covered.Max(index => index % 120), Is.LessThan(119));
            Assert.That(covered.Min(index => index / 120), Is.GreaterThan(0));
            Assert.That(covered.Max(index => index / 120), Is.LessThan(59));
        }

        [Test]
        public void SupersamplingProducesTransparentEdgesWithoutDarkFringes()
        {
            TriangleMesh triangle = new([new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)], [0, 1, 2]);
            PixelBuffer image = MeshRenderer.Render(triangle, new MeshRenderOptions
            {
                Width = 65, Height = 65, Supersampling = 4, CameraDirection = Vector3.UnitZ,
                CameraUp = Vector3.UnitY, Shadows = false, AmbientOcclusion = false
            });
            Rgba32 interior = image[32, 32];
            int edges = 0;
            foreach (int index in Enumerable.Range(0, image.Width * image.Height))
            {
                Rgba32 pixel = image[index % image.Width, index / image.Width];
                if (pixel.A is > 0 and < 255)
                {
                    edges++;
                    Assert.That(pixel.R, Is.EqualTo(interior.R).Within(1));
                    Assert.That(pixel.G, Is.EqualTo(interior.G).Within(1));
                    Assert.That(pixel.B, Is.EqualTo(interior.B).Within(1));
                }
            }

            Assert.That(edges, Is.GreaterThan(20));
            Assert.That(image[0, 0].A, Is.Zero);
        }

        [Test]
        public void CompositesOverRequestedBackground()
        {
            Rgba32 background = new(21, 43, 65, 128);
            PixelBuffer image = MeshRenderer.Render(Cube(), new MeshRenderOptions
            {
                Width = 48, Height = 48, Background = background, Shadows = false, AmbientOcclusion = false
            });
            Assert.That(image[0, 0], Is.EqualTo(background));
            Assert.That(image[24, 24].A, Is.EqualTo(255));
        }

        [Test]
        public void ShadowsAndOcclusionDarkenRecesses()
        {
            TriangleMesh mesh = CubeWithFloor();
            PixelBuffer plain = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 96, Height = 96, Shadows = false, AmbientOcclusion = false });
            PixelBuffer shadowed = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 96, Height = 96, ShadowMapSize = 256, AmbientOcclusion = false });
            PixelBuffer occluded = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = 96, Height = 96, Shadows = false });
            Assert.That(DarkerPixels(plain, shadowed), Is.GreaterThan(20));
            Assert.That(DarkerPixels(plain, occluded), Is.GreaterThan(5));
        }

        [Test]
        public void RejectsInvalidGeometryAndUnsafeRenderOptions()
        {
            Assert.Throws<ArgumentException>(() => new TriangleMesh([new(float.NaN, 0, 0)], [0, 0, 0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TriangleMesh([Vector3.Zero], [0, 1, 2]));
            TriangleMesh collapsed = new([Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2], [0, 1, 2]);
            Assert.Throws<ArgumentException>(() => MeshRenderer.Render(collapsed));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { Width = int.MaxValue, Height = int.MaxValue }));
            Assert.Throws<ArgumentException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { CameraDirection = Vector3.UnitZ, CameraUp = Vector3.UnitZ }));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { Exposure = float.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(Cube(), new MeshRenderOptions { Supersampling = 0 }));
        }

        [Test]
        public void MeshOwnsItsInputAndRenderingHonorsCancellation()
        {
            Vector3[] vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
            int[] indices = [0, 1, 2];
            TriangleMesh mesh = new(vertices, indices);
            vertices[1] = Vector3.Zero;
            indices[2] = 0;
            Assert.That(mesh.Vertices.Span[1], Is.EqualTo(Vector3.UnitX));
            Assert.That(mesh.TriangleIndices.Span[2], Is.EqualTo(2));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => MeshRenderer.Render(mesh, cancellationToken: cancellation.Token));
        }

        [Test]
        public void IgnoresDegenerateFacesMixedWithValidGeometry()
        {
            Vector3[] vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
            TriangleMesh valid = new(vertices, [0, 1, 2]);
            TriangleMesh mixed = new(vertices, [0, 1, 2, 0, 0, 1]);
            MeshRenderOptions options = new() { Width = 48, Height = 48, ShadowMapSize = 64 };
            Assert.That(MeshRenderer.Render(mixed, options).Pixels.ToArray(),
                Is.EqualTo(MeshRenderer.Render(valid, options).Pixels.ToArray()));
        }

        [Test]
        public void ModelRotationMatchesRotatedGeometryAndSourceNormalsCanBeOverridden()
        {
            Vector3[] vertices = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)];
            Vector3[] normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ];
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
            TriangleMesh source = new(vertices, [0, 1, 2], normals);
            TriangleMesh rotated = new(vertices.Select(vertex => Vector3.Transform(vertex, rotation)).ToArray(), [0, 1, 2],
                normals.Select(normal => Vector3.Transform(normal, rotation)).ToArray());
            PixelBuffer actual = MeshRenderer.Render(source, new MeshRenderOptions { Width = 64, Height = 64, Shadows = false, AmbientOcclusion = false, ModelRotation = rotation });
            PixelBuffer expected = MeshRenderer.Render(rotated, new MeshRenderOptions { Width = 64, Height = 64, Shadows = false, AmbientOcclusion = false });
            Assert.That(actual.Pixels.ToArray(), Is.EqualTo(expected.Pixels.ToArray()));

            TriangleMesh unusualNormals = new(vertices, [0, 1, 2], [Vector3.UnitX, Vector3.UnitX, Vector3.UnitX]);
            MeshRenderOptions generated = new() { Width = 64, Height = 64, Shadows = false, AmbientOcclusion = false, UseSourceNormals = false };
            Assert.That(MeshRenderer.Render(unusualNormals, generated).Pixels.ToArray(), Is.EqualTo(MeshRenderer.Render(source, generated).Pixels.ToArray()));
            Assert.Throws<ArgumentException>(() => MeshRenderer.Render(source, new MeshRenderOptions { ModelRotation = new Quaternion() }));
        }

        private static int DarkerPixels(PixelBuffer reference, PixelBuffer shaded)
        {
            int count = 0;
            for (int y = 0; y < reference.Height; y++)
            {
                for (int x = 0; x < reference.Width; x++)
                {
                    Rgba32 a = reference[x, y];
                    Rgba32 b = shaded[x, y];
                    if (a.A == 255 && b.R + b.G + b.B < a.R + a.G + a.B - 3)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static TriangleMesh Cube() => new(
            [new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0),
             new(-0.5f, -0.5f, 1), new(0.5f, -0.5f, 1), new(0.5f, 0.5f, 1), new(-0.5f, 0.5f, 1)],
            [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7]);

        private static TriangleMesh CubeWithFloor()
        {
            TriangleMesh cube = Cube();
            Vector3[] vertices = cube.Vertices.ToArray().Concat(new Vector3[] { new(-2, -2, 0), new(2, -2, 0), new(2, 2, 0), new(-2, 2, 0) }).ToArray();
            int[] indices = cube.TriangleIndices.ToArray().Concat(new[] { 8, 9, 10, 8, 10, 11 }).ToArray();
            return new TriangleMesh(vertices, indices);
        }
    }
}
