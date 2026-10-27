# SVG CPU benchmarks

Render.NET.Svg 0.3.19 fixes the effects-preview timeout reproduced in Cotton. On the original `effects-preview.svg`, 2560 × 1536 pixels, supersampling 1, the first render fell from **19.25 s to 4.20 s**. Five optimized calls completed in 2.62–4.20 s with a 20-second cancellation token, including the first call. The output's 15,728,640 RGBA bytes match 0.3.18 exactly: SHA-256 `5f36a95df3e378952eea5e8c0a5b061aa27d9f81586c9a80b777057752d4d30b`.

In a separate five-call comparison, median render time fell from **15.26 s to 2.78 s** (about 5.5×); [all Cotton samples](benchmarks/svg-2026-10-27/cotton-effects.json). These are local measurements, not a maximum-latency guarantee. The regression suite renders that actual scene at Cotton's resolution and deadline, and also checks 352 fixed 0.3.18 image hashes across blur/morphology color spaces, edge modes, radii and transformations.

## Fresh document to RGBA

Median milliseconds for parsing a fresh SVG string and rendering 512 × 512 transparent RGBA, supersampling 1. Each phase discards 20 warmups, then records 11 samples with alternating engine order. Skia includes straight-alpha pixel readback. No input/output disk access or GPU is timed.

| Original synthetic scene | Render.NET 0.3.18 | Render.NET 0.3.19 | Svg.Skia + SkiaSharp, in 0.3.19 run |
| --- | ---: | ---: | ---: |
| Vector shapes | 9.60 | 12.05 | 4.33 |
| Gaussian σ=2, sRGB | 74.58 | 51.94 | 8.41 |
| Gaussian σ=8, sRGB | 108.18 | 25.41 | 8.44 |
| Drop shadow σ=8, sRGB | 98.47 | 38.78 | 27.04 |
| Morphology dilate radius=4 | 150.57 | 21.60 | 348.50 |
| Color matrix | 23.85 | 22.55 | 13.06 |
| Blur + color + blend | 71.62 | 44.42 | 25.44 |
| Turbulence, four octaves | 108.70 | 111.03 | 25.22 |
| Gaussian σ=8, linearRGB | 78.70 | 73.95 | 34.18 |

Skia remains faster on most of these effects, especially turbulence and blur. This change targets the measured timeout and exact filter execution; it does not establish general parity with Skia. Vector drawing, turbulence and color matrices were not optimized. Small or inconsistent changes in those rows show the variability of these local runs. LinearRGB conversion remains a significant cost.

Morphology's Skia result applies to **fresh parsed pictures**. Repeated rendering of the same Skia picture can reuse filter/image caches and is substantially faster. It would be misleading to use that row as a claim about every Skia workload. Similarly, this table does not describe cold process startup, PDF, TGS or GPU rendering.

## Output and environment

All nine optimized Render.NET comparison images match 0.3.18 byte for byte. Against Skia, mean absolute error in premultiplied RGBA ranges from 0.069 to 1.647 on a 0–255 scale; 97.1–100% of pixels have every channel within 8. These scene-specific differences are not an SVG conformance result. [Per-scene quality measurements](benchmarks/svg-2026-10-27/quality.json).

Hardware: Intel Core i5-12450H, Ubuntu 24.04.5, .NET 10.0.13, Release builds, default tiered JIT/PGO. The shared host exposes 12 logical CPUs with an 11-CPU quota; `DOTNET_PROCESSOR_COUNT=4` limits managed worker selection. No CPU affinity or exclusive host was used; other workloads and JIT timing affect absolute values. Skia may select its own native thread count. Reference packages: Svg.Skia 5.2.3, SkiaSharp 4.153.1 and SkiaSharp.NativeAssets.Linux.NoDependencies 4.153.1.

Raw samples include both `fresh_rgba` and `total_png`: [0.3.18](benchmarks/svg-2026-10-27/0.3.18.jsonl), [0.3.19](benchmarks/svg-2026-10-27/0.3.19.jsonl). PNG uses each encoder's default compression and is deliberately kept out of the raster table. Managed allocation counters exclude native Skia memory and cannot compare total memory consumption.

## Reproduction and implementation

[Benchmark source, original inputs and commands](../Benchmarks/Render.NET.Svg.Benchmarks/README.md). The benchmark is outside the product solution; its Skia dependencies are not shipped in any Render.NET package. All included scenes are original, without downloaded artwork.

For the timeout regression itself:

```bash
DOTNET_PROCESSOR_COUNT=4 dotnet test Sources/Render.NET.sln -c Release \
  --filter FullyQualifiedName~CottonEffectsPreviewPreservesPixelsWithinItsTwentySecondDeadline
```

Axis-aligned Gaussian passes read and convert one padded scanline per worker, preserve the exact kernel and summation order, and skip windows containing only transparent pixels. Morphology uses prefix/suffix extrema blocks, reducing each axis to linear time instead of scanning every radius window. Worker scratch storage is accounted against `MaximumEffectBytes`. Reflected axes, sRGB/linearRGB, premultiplied alpha and blur edge modes are preserved. Arbitrarily rotated/skewed filter axes retain the general sampling path. Integer-coordinate samples avoid redundant bilinear interpolation. No native graphics or new runtime package dependency was added.
