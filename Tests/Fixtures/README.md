# Public-domain regression models

Only public-domain / CC0 model data is stored here. No attribution-required or copyleft assets are included. `manifest.json` records download URLs, the publishers' public-domain declarations, archive members where applicable, byte lengths, and SHA-256 checksums. The original model bytes are unchanged.

- `spot_*.obj` and `bob_tri.obj`: [Keenan Crane's model repository](https://www.cs.cmu.edu/~kmcrane/Projects/ModelRepository/), which explicitly dedicates these models and associated data to CC0. Includes triangles, quads, and texture/normal indices.
- `benchy.stl` and `benchy.3mf`: the original [3DBenchy repository](https://github.com/CreativeTools/3DBenchy). The owner [released 3DBenchy into the public domain in February 2025](https://www.3dbenchy.com/3dbenchy-sets-sail-into-the-public-domain/). Both have over 225,000 triangles.
- `mug.3mf`, `dog.3mf`, and `mech.3mf`: original Innerscene models, explicitly published as public domain / CC0 with no credit required. Their individual declaration pages are in the manifest. These exercise multiple positioned build items and the Materials and Properties Extension; preview rendering replaces source materials with the caller's color.

Additional malformed files, concave polygons, cross-part Production components, transformations, ambiguous IDs, and thumbnail cases are constructed inside the unit tests. No downloaded code is used by the library. Test assets are excluded from the NuGet packages.
