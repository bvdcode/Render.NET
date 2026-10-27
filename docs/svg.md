# SVG → PNG

`Render.NET.Svg` interprets static SVG vector scenes and renders them through the managed Render.NET canvas. Targets .NET 10; its only package dependency is `Render.NET`. It does not need a browser, display server, GPU, native graphics library or external process at runtime.

```csharp
using Render.NET;
using Render.NET.Svg;

SvgDocument document = await SvgReader.ReadFileAsync("drawing.svg", cancellationToken: token);
PixelBuffer image = SvgRenderer.Render(document, new SvgRenderOptions
{
    Width = 1024, // Supplying only width preserves intrinsic proportions.
    Supersampling = 2,
    Background = default // Transparent RGBA.
}, token);
await using Stream output = File.Create("drawing.png");
await PngEncoder.WriteAsync(image, output, token);
```

`SvgReader.Read(string)`, `ReadAsync(Stream)` and `ReadFileAsync` produce an independent scene that can be rendered concurrently after its input stream is disposed. Streams remain caller-owned. Rendering has no shared mutable canvas. `SvgPathParser.Parse` and `SvgPathWriter.Write` also convert path data independently of a document.

## Supported scene features

| Area | Support |
| --- | --- |
| Geometry | All absolute/relative SVG path commands, implicit command repetition, smooth cubic/quadratic curves, elliptical arcs with radius correction, multiple contours, rectangles with rounded corners, circles, ellipses, lines, polygons and polylines. |
| Painting | Solid CSS named/hex/RGB/RGBA colors, currentColor, inherited fill/stroke, fill/stroke opacity, atomic group opacity, nonzero/evenodd fills, affine strokes, caps/joins/miter limits, dash patterns and offsets. |
| Gradients | Linear/radial, source/focal circles, objectBoundingBox and userSpaceOnUse coordinates, stops/stop opacity, inherited gradient definitions, gradient transforms, pad/repeat/reflect, sRGB interpolation. |
| Scene | Root viewport/viewBox and preserveAspectRatio, affine transforms, groups, local use/symbol references including symbol viewBox, clipPath with local/object-box coordinates. |
| Styles | Presentation attributes, inline style, embedded CSS rules with element, class, id, universal and compound simple selectors, specificity and source order. |

Text/font layout, raster images, patterns, masks, filters, markers, SVG animation, nested SVG viewports, complex CSS selectors and external resources are **not implemented** in this first release. Other advanced SVG features can also fall outside this subset; it is not an SVG conformance claim. Unsupported elements/properties are collected in `document.UnsupportedFeatures`; rendering rejects that list by default. `AllowPartialRendering = true` explicitly opts into an incomplete preview. Scripts/actions are never executed; external references are not fetched.

Clip paths union independently filled child geometry, preserving each child's clip rule. Nested clipping inside clipPath is diagnosed rather than claimed as supported. Gradient bounds are numerical, using 0.01 user-unit flattening. Curves are flattened again at device resolution during rasterization; antialiasing uses premultiplied-alpha downsampling.

## Limits and cancellation

`SvgReaderOptions` defaults: 16 MiB encoded UTF-8 input, 100,000 XML/expanded scene elements, 1,000,000 stored path commands, depth 64. Local references reject cycles and expanded instances count against the limits. DTDs/entities and XML resource resolution are disabled. XML parsing is synchronous and bounded by input size; its inner parser is not interrupted by the cancellation token. Cancellation is checked during scene interpretation, path parsing, painting and downsampling.

`SvgRenderOptions.MaximumSamples` defaults to 16,777,216 samples **including** supersampling. Supersampling is 1–4. At most eight nested opacity/compositing layers and 64 canvas clipping saves are supported. Layer buffers add temporary memory; a sample budget is not a total process-memory limit. Run hostile inputs in an isolated worker with an external memory/time limit.

## Original reference scene

`Tests/Fixtures/Svg/vector-preview.svg` was authored for this repository with no imported artwork, fonts or external assets. At 800 × 480 with 2× supersampling, comparison with system librsvg/Cairo gives mean absolute **premultiplied RGB** error 0.1605/255; 99.609% of pixels differ by at most 8 per color channel. This synthetic comparison checks the illustrated features; it does not establish compatibility with arbitrary SVG files.

![Original SVG vector scene](images/svg-preview.png)

```bash
dotnet run --project Samples/Render.NET.Vector.Sample -c Release -- \
  drawing.svg drawing.png --width 1024 --supersampling 2
```

Parsing and arc conversion follow the [SVG 2 paths](https://www.w3.org/TR/SVG2/paths.html), [painting](https://www.w3.org/TR/SVG2/painting.html) and [coordinate systems](https://www.w3.org/TR/SVG2/coords.html) specifications. No external SVG implementation is included in the package.
