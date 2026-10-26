<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/Sources/Render.NET/packageIcon.png" alt="Render.NET" width="160" />
</p>

# Render.NET

[![NuGet](https://img.shields.io/nuget/v/Render.NET.svg)](https://www.nuget.org/packages/Render.NET/)
[![Build](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml/badge.svg)](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml)
[![License](https://img.shields.io/github/license/bvdcode/Render.NET)](https://github.com/bvdcode/Render.NET/blob/main/LICENSE)

A .NET rendering library under development.

The initial `Render.NET` and `Render.NET.Mesh` packages establish the library structure. They contain no public rendering or model loading APIs yet.

## Packages

| Package | Scope | Status |
| --- | --- | --- |
| [Render.NET](https://www.nuget.org/packages/Render.NET/) | Pixel buffers, drawing, transparency, and antialiasing. | Initial placeholder, targeting .NET 10. |
| [Render.NET.Mesh](https://www.nuget.org/packages/Render.NET.Mesh/) | STL, OBJ, and 3MF; camera, materials, lighting, and triangle rendering. | Initial placeholder, targeting .NET 10; depends on Render.NET. |
| Render.NET.Pdf | PDF reading, fonts, and drawing command execution. | Planned. |

## Installation

```bash
dotnet add package Render.NET
dotnet add package Render.NET.Mesh
```

`Render.NET` currently adds an empty assembly with no runtime dependencies. `Render.NET.Mesh` adds an empty assembly and a dependency on `Render.NET`.

## Mesh rendering goals

`Render.NET.Mesh` is intended for server-side 3D model previews implemented in managed C#, without external renderer processes, native graphics packages, or a windowing system.

The planned responsibilities are:

- `Render.NET`: pixel buffers, rasterization, transparency, and antialiasing.
- `Render.NET.Mesh`: model loading, scene geometry, camera framing, materials, lighting, and projection onto the pixel buffer.

The first rendering milestone is an STL preview with automatic camera framing, depth testing, lighting, shadows, and antialiasing. OBJ and 3MF support will extend the same rendering pipeline. Visual quality and performance will be evaluated against reference model previews.

## Build

Requires the .NET 10 SDK.

```bash
dotnet restore Sources/Render.NET.sln
dotnet build Sources/Render.NET.sln --configuration Release --no-restore
dotnet pack Sources/Render.NET.sln --configuration Release --no-build --output artifacts/packages
```

The NuGet package includes the project icon, this README, and the MIT license. Debug symbols are distributed in a separate `.snupkg` file.

## Releases

GitHub Actions builds and packs changes on `main` and pull requests. Successful builds on `main` publish to NuGet.org using Trusted Publishing and create a GitHub release with package assets.

Versions are calculated by GitVersion. The initial version is configured in [GitVersion.yml](GitVersion.yml).

## License

[MIT](LICENSE). Copyright © 2026 Vadim Belov.
