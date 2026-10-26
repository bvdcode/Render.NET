<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/Sources/Render.NET/packageIcon.png" alt="Render.NET" width="160" />
</p>

# Render.NET

[![NuGet](https://img.shields.io/nuget/v/Render.NET.Mesh.svg)](https://www.nuget.org/packages/Render.NET.Mesh/)
[![Build](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml/badge.svg)](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml)
[![License](https://img.shields.io/github/license/bvdcode/Render.NET)](https://github.com/bvdcode/Render.NET/blob/main/LICENSE)

Render STL models to transparent PNG previews in managed C#. Runs on the CPU using .NET 10 and the base class library, without a native graphics dependency, GPU, display server, or external renderer process.

<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/docs/images/stl-preview.png" alt="A torus knot loaded from STL and rendered with Render.NET.Mesh" width="512" />
</p>

## Packages

| Package | Available now |
| --- | --- |
| [Render.NET](https://www.nuget.org/packages/Render.NET/) | RGBA8 pixel buffers, sRGB colors with straight alpha, and metadata-free PNG encoding. |
| [Render.NET.Mesh](https://www.nuget.org/packages/Render.NET.Mesh/) | Binary and ASCII STL loading, indexed triangle meshes, and studio-lit orthographic previews. Depends on Render.NET. |
| Render.NET.Pdf | Planned. |

## STL to PNG

```bash
dotnet add package Render.NET.Mesh
```

```csharp
using Render.NET;
using Render.NET.Mesh;

await using var source = File.OpenRead("model.stl");
TriangleMesh mesh = await StlReader.ReadAsync(source);

PixelBuffer image = MeshRenderer.Render(mesh, new MeshRenderOptions
{
    Width = 768,
    Height = 768,
    Supersampling = 2
});

await using var destination = File.Create("preview.png");
await PngEncoder.WriteAsync(image, destination);
```

The camera automatically frames the complete model with padding. Rendering includes depth testing, area-weighted smooth normals with a configurable crease angle, three studio lights, a roughness/metallic material, filtered self-shadows, screen-space ambient occlusion, tone mapping, and supersampling. Lighting and downsampling use linear color; output pixels are straight-alpha sRGB. The default background is transparent.

`MeshRenderOptions` controls the camera direction and up vector, dimensions, padding, surface color, roughness, metallic fraction, exposure, background, smoothing, shadows, and occlusion. Model coordinates are treated as Z-up by default. Camera direction points **from the model toward the camera**. Coordinates are normalized internally so preview framing does not depend on STL units.

```csharp
using System.Numerics;

var options = new MeshRenderOptions
{
    CameraDirection = new Vector3(1.6f, -2.4f, 1.5f),
    CameraUp = Vector3.UnitZ,
    BaseColor = new Vector3(0.55f, 0.65f, 0.88f), // sRGB components, 0–1
    Roughness = 0.32f,
    Metallic = 0.15f,
    CreaseAngle = 45,
    Shadows = true,
    ShadowMapSize = 1024,
    ShadowSoftness = 0.02f,
    AmbientOcclusion = true
};
```

## Streams and limits

`StlReader.ReadAsync` reads from the stream's current position, supports non-seekable streams, and leaves the source open. It accepts ASCII STL with invariant-culture numbers, an optional UTF-8 BOM, and multiple solids. Binary detection uses the declared triangle count and exact file length, including binary files whose header begins with `solid`. Truncated files, malformed facets, and non-finite coordinates are rejected. Stored facet normals are recomputed from geometry.

The loader defaults to a 128 MiB input limit and one million triangles. Configure these limits for your application:

```csharp
TriangleMesh mesh = await StlReader.ReadAsync(source, new StlReaderOptions
{
    MaxInputBytes = 64 * 1024 * 1024,
    MaxTriangleCount = 500_000
}, cancellationToken);

PixelBuffer image = MeshRenderer.Render(mesh, options, cancellationToken);
await PngEncoder.WriteAsync(image, destination, cancellationToken);
```

Rendering is synchronous and cancellable. Supersampling supports 1–4 samples per axis; the internal raster is limited to 16,777,216 samples. Geometry and raster buffers are held in memory, so services should also limit concurrent renders. PNG encoding is asynchronous, leaves the destination open, and writes only IHDR, IDAT, and IEND chunks.

## Sample

Render an existing model:

```bash
dotnet run --project Samples/Render.NET.Mesh.Sample -c Release -- model.stl preview.png 768
```

Reproduce the image above from a generated 20,480-triangle torus knot. The sample writes `preview.stl`, loads it through `StlReader`, and renders `preview.png`:

```bash
dotnet run --project Samples/Render.NET.Mesh.Sample -c Release -- --demo preview.png 768
```

The command reports load, render, and total PNG generation times. Ctrl+C cancels the operation.

## Current scope

This is an initial renderer for static previews, with opaque surfaces, an orthographic camera, and a single material. STL does not provide a standard scene orientation or material system; camera and color can be configured by the caller. Crease smoothing joins vertices at identical positions and retains flat normals at unusually high-valence vertices to bound processing work.

OBJ and 3MF loading, textures, perspective projection, transparent materials, ground planes, and PDF rendering are planned. Filtered shadow maps and screen-space occlusion are approximations. Visual and performance parity with F3D has not been established across a representative model collection.

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet restore Sources/Render.NET.sln
dotnet build Sources/Render.NET.sln --configuration Release --no-restore
dotnet test Sources/Render.NET.sln --configuration Release --no-build --no-restore
dotnet pack Sources/Render.NET.sln --configuration Release --no-build --output artifacts/packages
```

Tests cover binary/ASCII STL, malformed inputs, stream limits, cancellation, camera framing across coordinate scales, depth ordering, transparent antialiasing, shadows, ambient occlusion, and PNG decoding/checksums.

GitHub Actions builds, tests, and packs changes on `main` and pull requests. Successful builds on `main` publish both packages to NuGet.org using Trusted Publishing and create a GitHub release. Versions are calculated by GitVersion.

Each NuGet package includes the project icon, this README, XML API documentation, and the MIT license. Debug symbols are distributed in separate `.snupkg` files.

## License

[MIT](LICENSE). Copyright © 2026 Vadim Belov.
