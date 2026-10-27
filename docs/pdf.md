# Render.NET.Pdf

`Render.NET.Pdf` interprets PDF pages with PdfPig and draws them with Render.NET's managed CPU rasterizer. JPEG decoding uses StbImageSharp, and JBIG2 uses PdfPig.Filters.Jbig2.PdfboxJbig2. These codec dependencies contain managed .NET assemblies. No PDFium, Poppler, Skia, System.Drawing, native graphics libraries, external processes, system font installation, or display server is needed by the package. Poppler is used only for development comparisons.

JPEG 2000 decoding was removed in 0.3.6: CoreJ2K's source retains JJ2000 conditions beyond its advertised BSD license. JP2 and raw JPEG 2000 images are listed in `UnsupportedFeatures`; default rendering rejects the page. Only an explicit `AllowPartialRendering` skips them. Version 0.3.5 still depends on CoreJ2K; upgrade to 0.3.6 or later to remove that dependency.

The remaining PDF runtime dependencies are PdfPig 0.1.16 (Apache-2.0 with additional component notices), StbImageSharp 2.30.16 (Unlicense OR MIT), and PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2 (Apache-2.0). Their license texts and upstream notices are included in the PDF package as `ThirdPartyNotices.txt`. The bundled fonts are public domain/CC0; their provenance is recorded separately below. Preserve the applicable licenses and notices when redistributing dependency binaries. Render.NET's own code remains MIT.

This is an initial PDF renderer, not a complete implementation of every PDF imaging feature. It reports known unsupported constructs rather than silently claiming a faithful preview.

## Read and render

```csharp
using Render.NET;
using Render.NET.Pdf;

using PdfDocument document = await PdfReader.ReadFileAsync("report.pdf",
    new PdfReaderOptions { Password = "document-password" }, cancellationToken);

Console.WriteLine(document.PageCount);
PdfPage page = document.GetPage(1, cancellationToken);

PixelBuffer preview = PdfRenderer.Render(page, new PdfRenderOptions
{
    Dpi = 144,
    MaximumWidth = 1600,
    MaximumHeight = 2000,
    Supersampling = 2,
    Background = new Rgba32(255, 255, 255)
}, cancellationToken);

await using var output = File.Create("page.png");
await PngEncoder.WriteAsync(preview, output, cancellationToken);
```

Page numbers start at **1**. `Width` and `Height` are the visible page dimensions in points (72 points per inch), after crop, rotation, and `/UserUnit`. DPI determines output size; maximum width and height only reduce it, keeping the aspect ratio. Pixel dimensions round upward. Background defaults to opaque white; use `Background = default` for transparent output.

`PdfReader.ReadAsync` reads from a stream's current position, accepts non-seekable input, and leaves the caller's stream open. `GetPage` interprets one page and copies the data it needs. The returned page can render repeatedly and concurrently, including after document disposal. Access to one document's parser is serialized; separate documents can be read independently. There is no automatic page cache.

Supersampling is 1–4, default 2. Rendering occurs at the enlarged resolution and downsamples with alpha-weighted colors into straight-alpha RGBA8. PNG output has only IHDR, IDAT, and IEND chunks.

## Support matrix

| Feature | Supported rendering |
| --- | --- |
| PDF structure | PdfPig's cross-reference tables/streams, compressed object streams, content stream arrays, and password handling. |
| Page geometry | Inherited resources, crop/media boxes, crop origins, 0/90/180/270-degree page rotation, UserUnit. |
| Paths | Lines, rectangles, cubic curves; nonzero and even-odd fills; combined fill/stroke. |
| Strokes | Affine pen transforms, hairlines, butt/round/square caps, miter/round/bevel joins, miter limits, dash arrays and phases. |
| Text | Embedded TrueType, Type 1, and CFF outlines when exposed by PdfPig; positioned strings, text matrices, rotation, spacing, fill/stroke/invisible/clip modes. No hinting. |
| Missing fonts | Bundled CC0 Aileron and Canada 1500 regular/bold/italic/bold italic substitutes, including Cyrillic and Greek, or caller-provided TrueType fonts. Original PDF advance widths are preserved. |
| Images | JPEG, JBIG2 (including indirect/compressed global symbol dictionaries and filter chains), raw/Flate/LZW/ASCII85/ASCIIHex/RunLength/CCITT data supported by PdfPig; inline images; 1/2/4/8/16-bit samples; RGB, gray, CMYK, indexed, and other color spaces exposed by PdfPig. |
| Image masks | Stencil masks, image soft masks (including different sizes), sample Decode arrays, color-key masks for sample images. |
| Forms | Nested Form XObjects, local resources, matrices, bounding boxes, graphics state save/restore. |
| Gradients | Axial and radial shadings, domains/functions, extension, bounding boxes and transforms; shading-pattern fills clipped to the painted path. |
| Annotations and widgets | Normal appearance streams, selected appearance states, local fonts/resources, transformed bounding boxes mapped into annotation rectangles, hidden/invisible/NoView flags. Existing filled-field appearances are rendered. |
| Clipping | Intersections, empty clips, text clipping at ET, graphics state restoration. |
| Transparency | Constant fill/stroke alpha, all 16 PDF blend modes, isolated and non-isolated transparency groups with opacity applied once, alpha/luminosity graphics-state soft masks with backdrop and transfer functions. |
| Color conversion | PdfPig's conversion to RGB; no color-managed ICC/proofing or CMYK print fidelity guarantee. |

Known unsupported rendering features include JPEG 2000 (JPXDecode), function-based and mesh shadings (types 1 and 4–7), tiling patterns, pattern strokes, shading-pattern graphics-state overrides, Type 3 glyph programs, optional-content visibility, knockout transparency groups, overprint, annotation NoZoom/NoRotate flags, and JPEG Decode arrays/color-key masks. Form values without a normal appearance stream are reported, including inherited values; the renderer does not generate missing appearances. JBIG2 segments with unknown lengths and striped pages with unknown height are reported as unsupported. Missing glyphs are reported. Documents can contain additional producer-specific features not recognized by this implementation; a clean feature list is not a general PDF conformance guarantee.

Hyperlink actions, scripts, attachments, and interactive forms are not executed. Existing annotation and form appearances are painted after page content. Actions and field calculations are not run. Ordinary links without appearance streams do not add a generated border or highlight.

```csharp
foreach (string feature in page.UnsupportedFeatures)
    Console.WriteLine(feature);

// Explicitly opt into a potentially incomplete preview.
PixelBuffer partial = PdfRenderer.Render(page,
    new PdfRenderOptions { AllowPartialRendering = true });
```

The default throws `NotSupportedException` when `UnsupportedFeatures` is nonempty. Partial rendering skips unsupported image codecs and shadings and approximates other reported operations; it does not make them supported. Malformed documents and exhausted limits still fail.

## Fonts

Embedded font outlines are used directly. Standard PDF fonts are often named without embedding their outlines. The package includes four [Aileron](https://dotcolon.net/font/aileron/) faces, dedicated to the public domain under CC0 by Sora Sagano, and four [Canada 1500](https://typodermicfonts.com/canada1500/) faces released under CC0 by Raymond Larabie. Aileron remains the Latin substitute; Canada 1500 supplies missing Cyrillic, Greek, and other covered Unicode glyphs. Glyph coverage is checked against the cmap; a missing-glyph box is not accepted as a successful substitute. Their source URLs and SHA-256 checksums are recorded in `Sources/Render.NET.Pdf/Fonts/provenance.json`. No third-party copyrighted document content is included in the PDF fixtures.

Aileron is a sans-serif substitute, including when a missing font is serif or monospaced. Glyph appearance can therefore differ from the original even though PDF advance widths are preserved. `page.SubstitutedFonts` lists font names that required replacement. A bundled font cannot cover every Unicode character; missing outlines are reported as unsupported.

Supply a matching TrueType face when the exact typeface matters:

```csharp
byte[] replacement = await File.ReadAllBytesAsync("MyFont.ttf", cancellationToken);
using PdfDocument document = await PdfReader.ReadFileAsync("report.pdf",
    new PdfReaderOptions
    {
        FontResolver = pdfFontName => pdfFontName.Contains("MyFont")
            ? replacement : ReadOnlyMemory<byte>.Empty
    }, cancellationToken);
```

The resolver is called only when PdfPig cannot provide an embedded outline. Returning empty memory selects the bundled substitute. Font files remain caller-owned input.

## Limits and cancellation

Defaults: encoded PDF input 128 MiB; executed graphics operations 1,000,000 per page including forms; stored vector commands 1,000,000 including glyphs; a single decoded image 16,777,216 pixels; total decoded image storage 128 MiB per page; graphics state nesting 64; compositing layers and nested soft-mask interpretation eight. Core paths also cap flattened geometry at 1,000,000 segments and dashed strokes at 100,000 pieces per contour.

Transparency groups and soft masks allocate temporary canvas-sized buffers. Non-isolated layers also retain the backdrop and group coverage. The eight-layer bound limits nesting; `MaximumSamples` is the size of a single canvas, not a total peak-memory cap.

The output budget defaults to 16,777,216 samples, **including supersampling**. For example, 2× antialiasing uses four samples per output pixel. Adjust `MaximumSamples` deliberately when rendering large pages.

Reader limits are configurable through `PdfReaderOptions`; output limits through `PdfRenderOptions`. Cancellation is checked during input reads, operation/glyph/image interpretation, scanline painting, curve flattening, dashed strokes, and downsampling. PdfPig's internal parsing/decompression, JBIG2 decoding, and StbImageSharp's JPEG decode are synchronous and cannot be interrupted midway by this token. Input, interpreted-geometry, and decoded-image limits do not bound every temporary parser/decoder allocation. Use an isolated worker with external time/memory limits for untrusted documents.

## Command-line example

```bash
dotnet run --project Samples/Render.NET.Pdf.Sample -c Release -- \
  report.pdf page.png --page 2 --dpi 144 --width 1600 --supersampling 2
```

Optional flags: `--transparent` and `--allow-partial`. The CLI writes feature warnings to stderr and reports page count, output dimensions, elapsed time, and substituted fonts.

## Verification fixtures

`Tests/Fixtures/Pdf` contains nine documents authored for this repository: vector graphics/clipping and alpha, embedded and substituted text, JPEG/inline/soft-masked images, a complete typeset preview, deliberately unsupported optional content, a password-protected document, codec scans (including explicitly unsupported JPEG 2000 images), a JBIG2 scan with global symbols, and a compatibility page with unembedded Cyrillic, gradients, a luminosity mask, group opacity and a filled widget. Their deterministic generator, original synthetic scan, encoded scan inputs, and SHA-256 manifest are committed. The JBIG2 global-symbol round trip is tested pixel-for-pixel against the original synthetic scan. Tests run offline after package restore; Python, ReportLab, Pillow, and Poppler are development tools only and are not runtime package dependencies.

The preview was also rendered with Poppler at 96 DPI and inspected side by side. Both outputs are 720 × 854. On this fixture, mean absolute RGB channel error is 1.54/255 and 96.78% of pixels differ by at most 8 in every channel. This is one regression comparison, not a broad claim of PDF rendering equivalence. Font hinting and edge coverage differ.

![Cyrillic, gradients, transparency and form appearances](images/pdf-compatibility.png)
