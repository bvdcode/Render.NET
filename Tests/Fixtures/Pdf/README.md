These PDF files were authored in this repository as regression cases. They contain our own text, vector geometry, synthetic images, and embedded public-domain Aileron font subsets. No third-party document or model content was copied.

`generate.py` recreates the six deterministic PDFs with ReportLab and Pillow. Rebuilding fixtures is optional; .NET tests read the committed files without network access or Python dependencies. `manifest.json` records their SHA-256 checksums and sizes.

The Aileron font source and CC0 declaration are recorded in `Sources/Render.NET.Pdf/Fonts/provenance.json`.
