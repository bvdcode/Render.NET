# Managed JBIG2 decoding

`Render.NET.Jbig2` is an original MIT-licensed C# decoder for .NET 10. It uses the base class library and has no package dependencies, native libraries, subprocesses, system fonts, or platform graphics dependencies. `Render.NET.Pdf` uses it automatically for `/JBIG2Decode`, including indirect, compressed `/JBIG2Globals` and preceding filters such as FlateDecode.

```csharp
using Render.NET.Jbig2;

Jbig2Document document = Jbig2Decoder.Decode(File.ReadAllBytes("scan.jb2"));
Jbig2Page page = document.Pages[0];
Console.WriteLine($"{page.Width} × {page.Height}; black pixel: {page.GetPixel(10, 10)}");
ReadOnlyMemory<byte> packed = page.Data;
```

For PDF image streams, pass the decoded global stream as the second argument:

```csharp
Jbig2Document document = Jbig2Decoder.Decode(imageStream, globalsStream,
    new Jbig2DecodeOptions
    {
        MaximumPages = 1,
        ExpectedWidth = pdfImageWidth,
        ExpectedHeight = pdfImageHeight
    }, cancellationToken);
```

Output pages are ordered by their original page association number (`Number`). Each row has `Stride = (Width + 7) / 8` bytes. Pixels are MSB first: **1 means black, 0 means white**. Unused low bits in the final byte are zero. This differs from PDF DeviceGray sample polarity; the PDF adapter performs that conversion before applying Decode arrays, image masks and colors.

## Implemented

- Standalone sequential and random-access organization; embedded streams and separate global segments; multiple pages and 32-bit page associations.
- Generic arithmetic bitmap templates 0–3, adaptive pixels, typical row prediction and skip masks.
- MMR/T.6 two-dimensional coding, terminating and makeup runs, optional EOFB when the payload length is known, required EOFB between halftone planes.
- Arithmetic and Huffman symbol dictionaries, raw or MMR collective bitmaps, imported/exported symbols and retained bitmap contexts.
- Text strips, all reference corners and transposed coordinates; per-instance refinement; refinement/aggregate dictionaries.
- Both refinement templates, reference bitmaps or the current page, and typical pixel prediction.
- Pattern dictionaries and halftone regions, Gray-coded planes and clipped pattern composition.
- All 15 standard Huffman tables and transmitted custom tables.

## Explicit limits

This release implements a tested subset of the monochrome T.88 baseline, rather than claiming full conformance to every profile or later amendment. Unknown-length segments, unknown page heights, color/extended templates, necessary extension segments and the optional T.6 uncompressed extension are rejected. An unsupported image remains subject to the PDF reader's existing strict/partial-render policy; the decoder does not silently omit its content.

`Jbig2DecodeOptions` limits combined encoded bytes, individual bitmap pixels, cumulative allocations, cumulative decoded/composed pixels, segments, symbols, instances and pages. Expected dimensions are checked before page allocation. Decoding checks cancellation throughout bitmap rows, composition and segment processing. The allocation budget is conservative and includes intermediate bitmaps and contexts; it is not a measured process working set. Limits and malformed input raise `InvalidDataException`; unsupported features raise `NotSupportedException`.

## Provenance and verification

Implemented from the algorithms and normative numerical tables in [ITU-T T.88](https://www.itu.int/rec/T-REC-T.88-200002-S/en) (2000, including Annexes A, B, C and E) and [ITU-T T.6](https://www.itu.int/rec/T-REC-T.6-198811-I/en) (1988). The arithmetic probability states are shared as an internal linked source with our JPEG 2000 package; both assemblies remain standalone. No third-party decoder implementation source was copied or translated.

The repository contains 98 authored fixtures and their expected packed pixels, including an original development-only arithmetic encoder. Every fixture was independently decoded with PDFBox through its public API and matched the expected pixels exactly. The test suite also checks real independent-encoder PDF globals, all configured resource limits, cancellation, concurrent decoding and 600 deterministic malformed mutations. See [fixture provenance and independent comparisons](../Tests/Fixtures/Jbig2/README.md).

Independent decoders are used only during development. They are not shipped in the package and are not test-project dependencies. Existing PdfPig and StbImageSharp dependencies of `Render.NET.Pdf` retain their own notices; replacing JBIG2 does not remove those separate dependencies.
