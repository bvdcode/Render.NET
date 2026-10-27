The benchmark compares fresh SVG parsing and CPU rendering with Svg.Skia/SkiaSharp. It is deliberately outside the product solution: Skia and its native Linux assets are development-only references. Render.NET.Svg keeps its managed-only runtime dependency graph.

`Inputs` contains nine original synthetic scenes covered by this repository's MIT license. They share geometry and isolate vector drawing, Gaussian blur, shadow, morphology, color matrix, a filter chain and turbulence. Both sRGB and linearRGB blur are included. No downloaded artwork is bundled.

```bash
DOTNET_PROCESSOR_COUNT=4 dotnet run -c Release \
  --project Benchmarks/Render.NET.Svg.Benchmarks -- \
  Benchmarks/Render.NET.Svg.Benchmarks/Inputs artifacts/svg-bench 512 11 20
```

To compare a published baseline without changing source files:

```bash
dotnet build Benchmarks/Render.NET.Svg.Benchmarks -c Release \
  -p:RenderNetVersion=0.3.18 -o artifacts/svg-bench-baseline-bin
DOTNET_PROCESSOR_COUNT=4 dotnet artifacts/svg-bench-baseline-bin/Render.NET.Svg.Benchmarks.dll \
  Benchmarks/Render.NET.Svg.Benchmarks/Inputs artifacts/svg-bench-baseline 512 11 20
```

Each phase discards 20 warmups and records 11 samples with alternating engine order. Both engines parse a fresh SVG string for every sample, allocate output and render transparent 512 × 512 pixels. `fresh_rgba` includes Skia's straight-alpha readback; `total_png` uses each encoder's default compression into memory. Neither phase includes input/output file I/O or a GPU. These are warmed-process fresh-document latencies, not process startup timings or repeated cached-picture measurements. Managed allocation counters exclude Skia's native memory and cannot compare total memory consumption. The program saves both engines' PNGs outside the timed interval, using Render.NET's encoder for these comparison images.

Results and limits: [SVG benchmark](../../docs/svg-benchmarks.md).
