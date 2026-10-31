These PDF files were authored in this repository as regression cases. They contain original text, vector geometry, synthetic images, and embedded public-domain Aileron font subsets.

.NET tests read the committed files offline after package restore. `manifest.json` records SHA-256 checksums and sizes.

`patterns.pdf` has nine panels covering colored and uncolored patterns, rotation, pattern strokes, transparency, backdrop-sensitive blending, nested forms, images and image masks.

`compatibility.pdf` covers unembedded Cyrillic through a Type 0 Unicode font, axial/radial gradients, a luminosity soft mask, isolated group opacity, and a filled widget appearance. `scan-codecs.pdf` contains synthetic JP2, raw JPEG 2000 and MMR JBIG2 images. `scan-globals.pdf` uses an indirect decode-parameter dictionary, an ASCIIHex/JBIG2 filter chain, and a Flate-compressed global symbol table.

`Scans/source.png` is an original scan made from repeated text drawn with CC0 Aileron. Its JBIG2 inputs were encoded with `agl/jbig2enc`, commit `4553595aa094cad7d55c4efc9657ea31db23c742`, using `jbig2 -s -p -b output source.png`.

The Aileron and Canada 1500 font sources and CC0 declarations are recorded in `Sources/Render.NET.Pdf/Fonts/provenance.json`.

`Jpeg/` contains original constant 8-by-8 blocks: gray, RGB, YCbCr, CMYK with and without an Adobe marker, and YCCK. Tests exercise PDF color conversion, Adobe marker precedence, filter parameters, Decode arrays, color-key masks and component-count validation.
