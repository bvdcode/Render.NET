These 70 synthetic images were authored for this repository and are covered by its MIT license. Pixel patterns use arithmetic formulas; no third-party sample images or implementation sources are included.

`manifest.json` records dimensions, sample precision, component count, signedness, allowed maximum error, and SHA-256 hashes of both encoded files and expected interleaved samples. `.raw` files contain unsigned bytes (signed values use two's complement) or little-endian unsigned 16-bit samples. Subsampled components are expanded by nearest-neighbour lookup for comparison. Encoded files retain their JP2/J2K headers, which are part of the format.

Expected pixels were decoded independently with OpenJPEG 2.5.4. All reversible cases match exactly. The three irreversible/modified-quantization cases allow a maximum difference of one sample level. This is regression coverage, not a complete JPEG 2000 conformance suite.

Coverage includes 1-pixel dimensions, odd sizes, gray/RGB/RGBA/16-bit samples, signed samples, subsampling, four components, image/tile origins, tiles, all five progression orders, quality layers, precincts, component coding/quantization overrides, scalar-derived quantization, ROI maxshift, tile-parts, SOP/EPH, and every Part 1 code-block style flag (including combinations).

.NET tests read the committed fixtures offline after package restore. OpenJPEG is an independent reference tool and is not a NuGet package dependency.
