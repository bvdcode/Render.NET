<p align="center">
  <img src="https://raw.githubusercontent.com/bvdcode/Render.NET/main/Sources/Render.NET/packageIcon.png" alt="Render.NET" width="160" />
</p>

# Render.NET

[![NuGet](https://img.shields.io/nuget/v/Render.NET.svg)](https://www.nuget.org/packages/Render.NET/)
[![Build](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml/badge.svg)](https://github.com/bvdcode/Render.NET/actions/workflows/publish-release.yml)
[![License](https://img.shields.io/github/license/bvdcode/Render.NET)](https://github.com/bvdcode/Render.NET/blob/main/LICENSE)

A .NET rendering library under development.

The first `Render.NET` package is an empty placeholder. It contains no public rendering APIs yet.

## Packages

| Package | Scope | Status |
| --- | --- | --- |
| [Render.NET](https://www.nuget.org/packages/Render.NET/) | Pixel buffers, drawing, transparency, and antialiasing. | Initial placeholder, targeting .NET 10. |
| Render.NET.Mesh | STL, OBJ, and 3MF; camera, lighting, and triangle rendering. | Planned. |
| Render.NET.Pdf | PDF reading, fonts, and drawing command execution. | Planned. |

## Installation

```bash
dotnet add package Render.NET
```

The package currently adds an empty assembly with no runtime dependencies.

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
