These PDF files were authored in this repository as regression cases. They contain our own text, vector geometry, synthetic images, and embedded public-domain Aileron font subsets. No third-party document or model content was copied.

`generate.py` recreates the nine deterministic PDFs with ReportLab and Pillow. Rebuilding fixtures is optional; .NET tests read the committed files without network access or Python dependencies. `manifest.json` records their SHA-256 checksums and sizes.

`compatibility.pdf` covers unembedded Cyrillic through a Type 0 Unicode font, axial/radial gradients, a luminosity soft mask, isolated group opacity, and a filled widget appearance. `scan-codecs.pdf` contains synthetic JP2, raw JPEG 2000 and MMR JBIG2 images. All three formats are rendered with managed decoders; JP2 and raw JPEG 2000 use our original Render.NET.Jpeg2000 project. These fixture images were generated with Pillow, not CoreJ2K. `scan-globals.pdf` uses an indirect decode-parameter dictionary, an ASCIIHex/JBIG2 filter chain, and a Flate-compressed global symbol table.

`Scans/source.png` is our own scan made from repeated text drawn with CC0 Aileron. Its JBIG2 inputs were encoded with `agl/jbig2enc` (development tool only), commit `4553595aa094cad7d55c4efc9657ea31db23c742`, using `jbig2 -s -p -b output source.png`. `Scans/regenerate.py` recreates the input files when that optional tool is installed. No encoder source or third-party sample image is distributed.

The Aileron and Canada 1500 font sources and CC0 declarations are recorded in `Sources/Render.NET.Pdf/Fonts/provenance.json`.
