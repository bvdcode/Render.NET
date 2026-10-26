using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Mesh
{
    /// <summary>Renders opaque triangle meshes on the CPU using managed .NET code.</summary>
    public static class MeshRenderer
    {
        /// <summary>Renders an automatically framed orthographic preview with a transparent background by default.</summary>
        /// <remarks>Rendering is synchronous and cancellable. The input mesh is not modified.</remarks>
        public static PixelBuffer Render(TriangleMesh mesh, MeshRenderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            options ??= new MeshRenderOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            PreparedMesh prepared = new(mesh, options, cancellationToken);
            int width = options.Width * options.Supersampling;
            int height = options.Height * options.Supersampling;
            RasterCamera camera = new(prepared, options.CameraDirection, options.CameraUp, width, height, options.Padding);
            Vector3[] normals = new Vector3[width * height];
            float[] depths = TriangleRasterizer.Draw(prepared, camera, normals, cancellationToken);
            RasterCamera? shadowCamera = null;
            float[]? shadowDepths = null;
            if (options.Shadows)
            {
                shadowCamera = new RasterCamera(prepared, StudioLighting.KeyDirection, Vector3.UnitZ,
                    options.ShadowMapSize, options.ShadowMapSize, 0.03f);
                shadowDepths = TriangleRasterizer.Draw(prepared, shadowCamera, null, cancellationToken);
            }

            StudioLighting lighting = new(options, shadowCamera, shadowDepths);
            PixelBuffer output = new(options.Width, options.Height);
            Vector3 background = new(StudioLighting.ToLinear(options.Background.R / 255f),
                StudioLighting.ToLinear(options.Background.G / 255f), StudioLighting.ToLinear(options.Background.B / 255f));
            float backgroundAlpha = options.Background.A / 255f;
            Parallel.For(0, options.Height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
            {
                Span<byte> pixels = output.Pixels.Span;
                for (int x = 0; x < options.Width; x++)
                {
                    if ((x & 63) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    Vector3 sum = Vector3.Zero;
                    float alpha = 0;
                    for (int sy = 0; sy < options.Supersampling; sy++)
                    {
                        for (int sx = 0; sx < options.Supersampling; sx++)
                        {
                            int sampleX = x * options.Supersampling + sx;
                            int sampleY = y * options.Supersampling + sy;
                            int offset = sampleY * width + sampleX;
                            float depth = depths[offset];
                            if (!float.IsFinite(depth))
                            {
                                sum += background * backgroundAlpha;
                                alpha += backgroundAlpha;
                                continue;
                            }

                            Vector3 point = camera.Unproject(sampleX + 0.5f, sampleY + 0.5f, depth);
                            float occlusion = options.AmbientOcclusion
                                ? ScreenSpaceOcclusion.Evaluate(sampleX, sampleY, point, normals[offset], camera, depths) : 1;
                            sum += lighting.Shade(point, normals[offset], camera.ViewDirection, occlusion);
                            alpha += 1;
                        }
                    }

                    int target = (y * options.Width + x) * 4;
                    if (alpha > 0)
                    {
                        // Average premultiplied linear samples, then return straight sRGB pixels.
                        Vector3 color = sum / alpha;
                        pixels[target] = StudioLighting.ToSrgbByte(color.X);
                        pixels[target + 1] = StudioLighting.ToSrgbByte(color.Y);
                        pixels[target + 2] = StudioLighting.ToSrgbByte(color.Z);
                        pixels[target + 3] = (byte)MathF.Round(alpha * 255 / (options.Supersampling * options.Supersampling));
                    }
                }
            });

            return output;
        }
    }
}
