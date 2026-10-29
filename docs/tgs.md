# TGS / Lottie → selected-frame PNG

`Render.NET.Tgs` reads gzip-compressed Telegram `.tgs` and uncompressed Lottie JSON, evaluates one selected frame and renders its vector scene through `Render.NET.Svg` and the managed raster canvas. No native animation engine, browser or external renderer is used at runtime. The new packages add no third-party runtime dependencies.

```csharp
using Render.NET;
using Render.NET.Svg;
using Render.NET.Tgs;

TgsDocument animation = await TgsReader.ReadFileAsync("sticker.tgs", cancellationToken: token);
var options = new SvgRenderOptions { Width = 512, Supersampling = 2 };
PixelBuffer frame = TgsRenderer.RenderFrame(animation, frame: 30, options, token);
// Or elapsed seconds from the composition's first frame:
// PixelBuffer frame = TgsRenderer.RenderAtTime(animation, seconds: 0.5, options, token);

await using Stream output = File.Create("frame.png");
await PngEncoder.WriteAsync(frame, output, token);
```

Frame numbers are absolute, allow fractional values and must be in `[InPoint, OutPoint)`. Elapsed time starts at the composition's in-point. `Width`, `Height`, `FrameRate`, `InPoint`, `OutPoint` and `Duration` expose composition metadata. Output size, transparent/background color, antialiasing and sample limits use `SvgRenderOptions`. There is no full-animation export API in this stage.

The document owns an immutable copy of its JSON and can be rendered concurrently after the source stream is disposed. `Read`, `ReadAsync(Stream)` and `ReadFileAsync` accept both formats; gzip is detected from its signature. Streams remain caller-owned.

## Supported subset

| Area | Support |
| --- | --- |
| Animation | Static/animated scalar, vector and color properties; hold/linear/cubic temporal easing including per-component handles; split positions; cubic spatial motion sampled by arc length; matching-topology Bezier path morphs. |
| Layers | Shape, solid, null and precomposition layers; reverse paint order; in/out points, parent transforms, anchor/position/scale/rotation/skew, atomic opacity and automatic path orientation; precomp start/stretch and time remapping; precomp viewport clipping. |
| Shapes | Bezier paths with relative tangents, ellipses, rounded rectangles, rounded polygon/star geometry and fractional star point counts, nested groups, inherited outer styles and atomic opacity across translucent styled subgroups. |
| Paint | Solid fills/strokes, nonzero/evenodd, line width/caps/joins/miter limits, animated dashes, linear/radial gradients with color/opacity stops and radial highlight. |
| Modifiers | Parallel/sequential trim paths with wrapping offset; repeaters with transforms and opacity; merge/add/subtract/intersect/exclude paths; round corners, offset paths, Pucker & Bloat and sharp/smooth Zig Zag. |
| Masks/mattes | Multiple additive/subtractive/intersection/difference/lighten/darken masks; inverse masks, animated opacity/expansion/feather; alpha/luma track mattes and inverse mattes, including explicit matte layer selection; alpha Set Matte effects with hidden source layers and inversion. |
| Effects/compositing | All 18 Lottie layer blend modes; grouped Tint, Fill, Stroke, Tritone, Levels, Drop Shadow, Gaussian Blur (including the legacy export) and Transform effects with animated parameters. |

Expressions, slots, text/image layers and external assets, 3D, effect IDs outside the documented list, and Twist remain outside this implementation. Morphs with different vertex counts are rejected. Simultaneous trims reach separately styled subgroups. Sequential trims split across interleaved style scopes or separately styled subgroups are diagnosed. Set Matte channel extraction and alpha replacement remain unsupported. Fractional stars and flattened path offsets may differ numerically between players. This is a managed vector preview implementation, not full After Effects/Lottie conformance.

`animation.UnsupportedFeatures` reports features detectable while reading. Frame-dependent unsupported combinations can also throw while evaluating the selected scene. Rendering rejects known unsupported features by default; `AllowPartialRendering = true` opts into a potentially incomplete preview. Expressions/scripts are never executed and external assets are never fetched.

Motion paths use a 64-segment arc-length lookup. Trim paths flatten curves at 0.1 user-unit tolerance. SVG/raster curve and gradient approximations also apply. Numerical differences from another animation player are expected around curved edges and trimmed endpoints.

## Limits and verification

Reader defaults: 16 MiB encoded input, 32 MiB decoded JSON, 500,000 JSON values/expanded scene items, depth 64 and 1,000,000 generated path commands. Gzip output is checked incrementally; parent/precomp cycles fail. Geometry expansion, painting and reads check cancellation. JSON parsing is synchronous and bounded by decoded size; it cannot be interrupted midway. SVG canvas/output limits also apply. Repeaters allow up to 4,096 copies, Zig Zag up to 4,096 ridges per source segment; boolean geometry allows 16,384 flattened edges and two million candidate intersections. Effect buffers use `SvgRenderOptions.MaximumEffectBytes`. These bounds do not replace an isolated worker's process memory/time limits for hostile input.

`Tests/Fixtures/Tgs` contains an original two-second orbit scene as JSON and deterministic gzip TGS, its standard-library generator and a SHA-256 manifest. It uses no imported stickers, fonts or artwork and is covered by the repository MIT license.

At 512 × 512 with 2× supersampling, frames 0/30/60/90 were compared with **lottie-web 5.13.0's SVG renderer in headless Chrome**. Mean absolute premultiplied RGB error is 0.1440–0.1889/255; 99.246–99.448% of pixels differ by at most 8 in every color channel. The same JSON was used by both renderers. The reference player/browser are development tools only; they are not dependencies or shipped code. This one synthetic animation is regression evidence, not broad format conformance.

![Selected frame 30 of the original TGS scene](images/tgs-frame-30.png)

```bash
dotnet run --project Samples/Render.NET.Vector.Sample -c Release -- \
  sticker.tgs frame.png --frame 30 --width 512
# Or --time 0.5. Optional: --opaque, --allow-partial.
```

Implementation follows the [Lottie community format specification](https://lottie.github.io/lottie-spec/latest/), including its property interpolation and shape rendering model.

## Effects and modifier regression scenes

`effects.json`/`.tgs` add repeaters, rounded corners, Pucker & Bloat, multiple masks, an inverse alpha matte and drop shadow. `geometry.json`/`.tgs` cover rounded stars/polygons and sharp/smooth Zig Zag on a cubic curve. The original scenes and deterministic standard-library generator `generate_effects.py` are committed with SHA-256 manifests. Reference comparison uses lottie-web 5.13.0 SVG in Chrome with an expanded filter region; no player code or third-party artwork is shipped. Mask feather has separate pixel tests because the reference SVG player does not implement it.

![TGS effects and modifiers](images/tgs-effects.png)

Merge/offset operations use our own managed planar edge arrangement in `VectorPath.Combine`, `Offset` and `RoundCorners`; they do not depend on a third-party geometry library. Rounded corners/offsets flatten curves at 0.05 user-unit tolerance; boolean operations default to 0.1. Geometry budgets and cancellation also apply to these operations.

## Sticker regression inputs

`Tests/Fixtures/Tgs/Stickers` contains 15 compressed Telegram sticker files and a SHA-256 manifest. The regression tests cover hidden video layers, translucent groups, inherited simultaneous trim paths, parameter-only controllers, identity hue/saturation and empty puppet effects, legacy Gaussian Blur and alpha Set Matte. Active expressions and Roughen Edges remain open cases.
