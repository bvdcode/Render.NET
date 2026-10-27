These 70 synthetic images were authored for this repository. Pixel patterns are arithmetic formulas in `generate.py` and `oracle.cpp`; no third-party sample image, encoder implementation, or decoder implementation is distributed here. The fixtures and our generator/adapter are covered by the repository MIT license.

`manifest.json` records dimensions, sample precision, component count, signedness, allowed maximum error, and SHA-256 hashes of both encoded files and expected interleaved samples. `.raw` files contain unsigned bytes (signed values use two's complement) or little-endian unsigned 16-bit samples. Subsampled components are expanded by nearest-neighbour lookup for comparison. Encoded files retain their JP2/J2K headers, which are part of the format.

Expected pixels were decoded independently with OpenJPEG 2.5.4 through Pillow 12.3.0 and our small C API adapter. All reversible cases match exactly. The three irreversible/modified-quantization cases allow a maximum difference of one sample level. This is regression coverage, not a complete JPEG 2000 conformance suite.

Coverage includes 1-pixel dimensions, odd sizes, gray/RGB/RGBA/16-bit samples, signed samples, subsampling, four components, image/tile origins, tiles, all five progression orders, quality layers, precincts, component coding/quantization overrides, scalar-derived quantization, ROI maxshift, tile-parts, SOP/EPH, and every Part 1 code-block style flag (including combinations).

.NET tests read committed files offline. Regeneration is optional and requires Pillow with JPEG 2000 enabled and an OpenJPEG development installation. Build our adapter against that installation, then run:

```sh
mkdir -p artifacts
c++ -std=c++17 -O2 oracle.cpp $(pkg-config --cflags --libs libopenjp2) -o artifacts/jpeg2000-oracle
python3 generate.py --oracle artifacts/jpeg2000-oracle
```

The adapter is only a fixture-generation tool. No OpenJPEG header, source, binary, or runtime dependency is included in the NuGet packages. Encoder versions can change compressed bytes; review manifest changes when regenerating.
