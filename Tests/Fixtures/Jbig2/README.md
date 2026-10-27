# Authored JBIG2 fixtures

All images, bitstreams, expected `.bin` pixels, fixture generators and verification scripts in this directory were authored for Render.NET and are covered by the repository MIT license. There are no downloaded model images, scans, third-party implementation sources or native binaries here.

`regenerate.py`, `encoding.py` and `extra_cases.py` implement development-only encoders from ITU-T T.88 (2000) Annexes A/B/E. Normative arithmetic states and Huffman table data are read from the repository's table definitions. Pillow/libtiff independently produces the MMR payloads. Expected pixels are authored directly before encoding, rather than captured from the Render.NET decoder. Every manifest entry records both encoded and expected-pixel SHA-256 values.

Run with Python and Pillow installed:

```sh
python Tests/Fixtures/Jbig2/regenerate.py
python Tests/Fixtures/Jbig2/verify_with_poppler.py
```

Independent verification performed on 2026-10-27:

- PDFBox via `PdfPig.Filters.Jbig2.PdfboxJbig2` 0.1.16.2, public decode API only: **98/98 exact pixel matches**. This package is a development comparison only and is absent from all repository project dependencies.
- Poppler `pdfimages` 24.02.0: **93/98 exact matches**. Its decoder disagrees on four typical-refinement cases and retained bitmap contexts. The authored stream and this implementation follow T.88 §6.3.5.6 (including the zero-valued neighbors outside the bitmap) and §7.4.2.2 / Annex E context retention; PDFBox matches these cases. The verification script reports these named differences explicitly and fails on any additional mismatch.
- Artifex jbig2dec 0.20 was also tried through its public C API outside the repository. It reports intermediate generic regions as unimplemented and differs on several refinement and retained-context cases; it is not treated as an authority for those features.

The independent checks are useful differential evidence, not a claim of complete T.88 conformance. The supported subset and explicit unsupported features are described in [the codec documentation](../../../docs/jbig2.md).

`Fixtures/Pdf/Scans` also contains an original scan encoded by the independent `jbig2enc` tool, with a global symbol dictionary. Existing PDF integration tests check exact pixels, indirect globals, compressed globals and chained filters. Those development tools are not runtime dependencies.
