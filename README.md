<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/Sources/Render.NET/packageIcon.png" alt="Render.NET" width="160" />
</p>

# Render.NET

[![NuGet](https://img.shields.io/nuget/v/Render.NET.Mesh.svg)](https://www.nuget.org/packages/Render.NET.Mesh/)
[![Build](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml/badge.svg)](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml)
[![CodeFactor](https://www.codefactor.io/repository/github/bvdcode/Render.NET/badge)](https://www.codefactor.io/repository/github/bvdcode/Render.NET)
[![License](https://img.shields.io/github/license/bvdcode/Render.NET)](https://github.com/bvdcode/Render.NET/blob/main/LICENSE)

Render STL, OBJ, 3MF, and PDF files to PNG previews in managed C#. Runs on the CPU using .NET 10, without a native graphics dependency, GPU, display server, or external renderer process. Core, Mesh, Jpeg2000 and Jbig2 use the base class library; Pdf uses our JPEG 2000 and JBIG2 decoders, the managed PdfPig reader and StbImageSharp JPEG decoder.

<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/docs/images/stl-preview.png" alt="A torus knot loaded from STL and rendered with Render.NET.Mesh" width="512" />
</p>

## Packages

| Package | Available now |
| --- | --- |
| [Render.NET](https://www.nuget.org/packages/Render.NET/) | RGBA8 pixels, vector paths, fills and strokes, clipping, affine images, alpha compositing, and metadata-free PNG encoding. |
| [Render.NET.Mesh](https://www.nuget.org/packages/Render.NET.Mesh/) | STL, OBJ, and 3MF geometry, explicit assembly selection, indexed triangle meshes, and studio-lit orthographic previews. Depends on Render.NET. |
| [Render.NET.Pdf](https://www.nuget.org/packages/Render.NET.Pdf/) | PDF page interpretation and CPU rendering: font outlines, vector graphics, raster images, clipping, transparency, crop boxes, and rotation. Includes Cyrillic fallback fonts, gradients, transparency groups, soft masks, annotation/form appearances and JBIG2 scans. JPEG 2000 scans use our own managed decoder. |
| [Render.NET.Jpeg2000](https://www.nuget.org/packages/Render.NET.Jpeg2000/) | Original managed JPEG 2000 Part 1 decoder: JP2/J2K, lossless/lossy wavelets, 1–16-bit component samples, tiles and subsampling. No runtime dependencies. [Supported subset and API](docs/jpeg2000.md). |
| [Render.NET.Jbig2](https://www.nuget.org/packages/Render.NET.Jbig2/) | Original managed monochrome JBIG2 decoder: MMR, arithmetic/Huffman symbols, globals, refinement, and halftones. No runtime dependencies. [Supported subset and API](docs/jbig2.md). |

<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/docs/images/mech-managed.png" alt="A 66-part 3MF robot rendered from geometry" width="320" />
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/docs/images/benchy-lime.png" alt="3DBenchy rendered from STL with a caller-supplied lime color" width="320" />
</p>

## PDF to PNG

```bash
dotnet add package Render.NET.Pdf
```

```csharp
using Render.NET;
using Render.NET.Pdf;

using PdfDocument document = await PdfReader.ReadFileAsync("document.pdf");
PdfPage page = document.GetPage(1); // One-based, out of document.PageCount pages.
PixelBuffer image = PdfRenderer.Render(page, new PdfRenderOptions
{
    Dpi = 144,
    MaximumWidth = 1600,
    Supersampling = 2
});

await using var destination = File.Create("page.png");
await PngEncoder.WriteAsync(image, destination);
```

<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/docs/images/pdf-preview.png" alt="A PDF page with embedded fonts, rounded vector shapes, a chart, and text rendered by Render.NET.Pdf" width="420" />
</p>

PDF is a large format: this first renderer supports paths, embedded font outlines, substituted fonts, images, forms, clipping, constant alpha, and separable blend modes. Unsupported features are listed on `page.UnsupportedFeatures` and **rendering rejects them by default**. Shadings, patterns, Type 3 glyphs, annotation appearances, and transparency groups are not yet rendered faithfully. See [the PDF guide](https://github.com/bvdcode/Render.NET/blob/main/docs/pdf.md) for the support matrix, fonts, limits, and CLI example.

## Mesh performance

**512 × 512 CPU previews in 186–537 ms inside a running .NET process**, including model loading and PNG/file writing, on the five models below. This is the repeated-call scenario used by a service such as a thumbnail worker.

For a separate comparison of **new processes**, both columns below include startup, loading, rendering, PNG encoding, and file writing:

| Model | Triangles | Render.NET.Mesh | F3D 4.0 |
| --- | ---: | ---: | ---: |
| Spot (OBJ) | 5,856 | **509 ms** | 641 ms |
| Mug (3MF) | 2,290 | **527 ms** | 671 ms |
| Robot, 66 build items (3MF) | 19,116 | **978 ms** | 1,235 ms |
| Dog, 28 build items (3MF) | 35,256 | 848 ms | **745 ms** |
| 3DBenchy (STL) | 225,706 | 932 ms | **808 ms** |

Medians of five runs, measured October 26, 2026, on an Intel Core i5-12450H under Linux with .NET 10. Quality profile: Render.NET uses 2× supersampling, studio lighting, shadows, and ambient occlusion; F3D uses FXAA, ambient occlusion, and tone mapping. F3D runs with Mesa llvmpipe software OpenGL and a new Xvfb display per invocation, whose startup time is included. Filesystem caches are warm. The images and algorithms differ, and these results do not measure GPU rendering or a persistent F3D service.

See [the full benchmark](https://github.com/bvdcode/Render.NET/blob/main/docs/benchmarks.md) for 1024-pixel output, the fast profile, repeated-call timings, comparison images, raw samples, and reproduction commands.

## Mesh to PNG

```bash
dotnet add package Render.NET.Mesh
```

```csharp
using Render.NET;
using Render.NET.Mesh;

TriangleMesh mesh = await MeshReader.ReadFileAsync("model.3mf"); // Also .stl and .obj

PixelBuffer image = MeshRenderer.Render(mesh, new MeshRenderOptions
{
    Width = 768,
    Height = 768,
    Supersampling = 2,
    BaseColor = new System.Numerics.Vector3(163 / 255f, 230 / 255f, 53 / 255f) // Lime #A3E635
});

await using var destination = File.Create("preview.png");
await PngEncoder.WriteAsync(image, destination);
```

The camera automatically frames the complete model with padding. Rendering includes depth testing, area-weighted smooth normals with a configurable crease angle, three studio lights, a roughness/metallic material, filtered self-shadows, screen-space ambient occlusion, tone mapping, and supersampling. Lighting and downsampling use linear color; output pixels are straight-alpha sRGB. The default background is transparent.

`MeshRenderOptions` controls model rotation, camera direction and up vector, dimensions, padding, surface color, roughness, metallic fraction, exposure, background, smoothing, shadows, and occlusion. Model coordinates are treated as Z-up by default. Camera direction points **from the model toward the camera**. Coordinates are normalized internally so preview framing does not depend on source units. Rotation is applied around the model's bounding-box center before automatic framing.

```csharp
using System.Numerics;

var options = new MeshRenderOptions
{
    CameraDirection = new Vector3(1.6f, -2.4f, 1.5f),
    CameraUp = Vector3.UnitZ,
    ModelRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4),
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

## Formats and 3MF selection

| Format | Geometry behavior |
| --- | --- |
| STL | Binary and ASCII, including multiple ASCII solids. Facet normals are recomputed. |
| OBJ | Positive and negative position/UV/normal indices, triangles and concave planar polygons, line continuations, authored normals, and smoothing groups. All objects and groups are combined. |
| 3MF | Core meshes, all root build instances, nested components, transforms, units converted to millimeters, mirrored winding, and Production Extension references to other model parts. |

OBJ material libraries, texture images, UV shading, and vertex colors are not loaded. Free-form curves and surfaces must be tessellated first. Authored `vn` normals and explicit smoothing groups take precedence over generated crease normals; set `UseSourceNormals = false` to regenerate them. OBJ files without a smoothing declaration use the renderer's crease smoothing.

A 3MF package may contain many resources and positioned instances. `MeshReader` renders **every item in the root build**, preserving placement and excluding unused resources. It never guesses which object is the main one and never substitutes an embedded image. An empty build is an error when creating the build mesh; resources can still be selected explicitly.

```csharp
await using var source = File.OpenRead("project.3mf");
ThreeMfDocument document = await ThreeMfReader.ReadAsync(source);

TriangleMesh entireBuild = document.CreateBuildMesh();
TriangleMesh firstInstance = document.CreateBuildItemMesh(0); // Zero-based, keeps placement

// IDs belong to a model part, not to the whole ZIP. Inspect document.Objects
// and document.BuildItems to present an application-specific selection.
ThreeMfObject selected = document.Objects[0];
TriangleMesh resource = document.CreateObjectMesh(selected.Id, selected.ModelPart);
```

The package thumbnail is a separate, explicit operation. It may show an entire project, another camera angle, or an outdated scene, so it cannot identify a selected model:

```csharp
await using var source = File.OpenRead("project.3mf");
ThreeMfThumbnail? thumbnail = await ThreeMfReader.ReadThumbnailAsync(source);
if (thumbnail is not null)
{
    await File.WriteAllBytesAsync("embedded-preview" +
        (thumbnail.ContentType == "image/png" ? ".png" : ".jpg"), thumbnail.Data.ToArray());
}
```

Thumbnail extraction does not parse geometry. It returns original encoded bytes, including original metadata, and checks the PNG/JPEG signature without decoding the image. Newly rendered PNGs contain no metadata. Source materials and the Materials and Properties Extension are deliberately replaced by the caller's preview material, including when that extension is marked required. Per-object thumbnails and alternative Production representations are unsupported; unknown required extensions fail explicitly. This library loads preview geometry and does not validate printability or implement the entire 3MF specification.

## Streams and limits

`StlReader.ReadAsync` reads from the stream's current position, supports non-seekable streams, and leaves the source open. It accepts ASCII STL with invariant-culture numbers, an optional UTF-8 BOM, and multiple solids. Binary detection uses the declared triangle count and exact file length, including binary files whose header begins with `solid`. Truncated files, malformed facets, and non-finite coordinates are rejected. Stored facet normals are recomputed from geometry.

All format readers read from the current stream position, support non-seekable streams, leave the source open, and accept cancellation. Loaders default to a 128 MiB input limit and one million triangles. Configure format-specific limits for your application:

```csharp
TriangleMesh mesh = await StlReader.ReadAsync(source, new StlReaderOptions
{
    MaxInputBytes = 64 * 1024 * 1024,
    MaxTriangleCount = 500_000
}, cancellationToken);

PixelBuffer image = MeshRenderer.Render(mesh, options, cancellationToken);
await PngEncoder.WriteAsync(image, destination, cancellationToken);
```

`ObjReaderOptions` also bounds vertex and polygon sizes. `ThreeMfReaderOptions` bounds ZIP entries, compressed/decompressed bytes, part and thumbnail sizes, objects, component depth, and expanded instances/geometry. Its default uncompressed budget is 256 MiB, with 64 MiB per part and a component depth of 64. ZIP parts stay in memory; paths cannot resolve outside the package, external model references and XML DTDs are rejected. These are resource limits, not a process-level memory or CPU sandbox.

Rendering is synchronous and cancellable. Supersampling supports 1–4 samples per axis; the internal raster is limited to 16,777,216 samples. Geometry and raster buffers are held in memory, so services should also limit concurrent renders. PNG encoding is asynchronous, leaves the destination open, and writes only IHDR, IDAT, and IEND chunks.

## Sample

Render an existing STL, OBJ, or 3MF model:

```bash
dotnet run --project Samples/Render.NET.Mesh.Sample -c Release -- model.3mf preview.png 768
```

Reproduce the image above from a generated 20,480-triangle torus knot. The sample writes `preview.stl`, loads it through `StlReader`, and renders `preview.png`:

```bash
dotnet run --project Samples/Render.NET.Mesh.Sample -c Release -- --demo preview.png 768
```

The command reports load, render, and total PNG generation times. Ctrl+C cancels the operation.

## Current scope

This is an initial renderer for static previews, with opaque surfaces, an orthographic camera, and a single material. STL does not provide a standard scene orientation or material system; camera and color can be configured by the caller. Crease smoothing joins vertices at identical positions and retains flat normals at unusually high-valence vertices to bound processing work.

Textures, perspective projection, transparent materials, ground planes, animation, and PDF rendering are not implemented. Filtered shadow maps and screen-space occlusion are approximations. See the [F3D comparison](https://github.com/bvdcode/Render.NET/blob/main/docs/benchmarks.md) for measured timings, matching inputs, images, and the limits of that comparison.

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet restore Sources/Render.NET.sln
dotnet build Sources/Render.NET.sln --configuration Release --no-restore
dotnet test Sources/Render.NET.sln --configuration Release --no-build --no-restore
dotnet pack Sources/Render.NET.sln --configuration Release --no-build --output artifacts/packages
```

Tests cover STL, OBJ triangulation/normals, 3MF build selection and cross-part components, malformed inputs, limits, cancellation, camera rotation/framing, depth ordering, transparent antialiasing, shadows, occlusion, and PNG decoding/checksums. The repository includes [eight public-domain model files](https://github.com/bvdcode/Render.NET/tree/main/Tests/Fixtures), with source declarations and SHA-256 checks. Tests run offline after package restore. Fixtures and benchmarks are excluded from NuGet packages.

GitHub Actions builds, tests, and packs changes on `main` and pull requests. Successful builds on `main` publish both packages to NuGet.org using Trusted Publishing and create a GitHub release with package summaries and all commits since the previous release. GitVersion increments the patch version on `main` from release tags; commit messages cannot trigger minor or major bumps. Minor and major releases require an explicit versioning decision.

Each NuGet package includes the project icon, this README, XML API documentation, and the MIT license. Debug symbols are distributed in separate `.snupkg` files.

## License

[MIT](LICENSE). Copyright © 2026 Vadim Belov.
