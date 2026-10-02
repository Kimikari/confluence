# Confluence.Arena — design notes

Seeded, server-authoritative arena generation producing `Confluence.Core.Arena.ArenaLayout` (spec §7 L302-L335,
L169-L176, arena catalogue L654-L688).

## Layout of the code

| Area | Files | Role |
| --- | --- | --- |
| Rules | `ArenaRules.cs`, `ArenaMaterials.cs`, `MirrorMath.cs` | Local numbers and measurement conventions (each cites the spec line it reads), material names and flags, half-turn symmetry helpers. |
| Modules | `Modules/*.cs`, `Modules/Blueprints/*.cs` | One `ModuleBlueprint` per catalogue module (72). `ModuleBuilder` reads footprint, height, materials, and durability text; `ModuleLibrary` builds and checks them. |
| Navigation | `Navigation/ArenaNav.cs`, `NavQuery.cs`, `NodeHeap.cs` | Layered 0.5 m walkable grid with heights, clearance, walk/jump/drop edges, shortest paths, reverse reachability, and line of sight. Shared by validators and bots. |
| Generation | `Generation/*.cs` | `ArenaGenerator` (pipeline, retries, fallback), `ArenaAssembler` (one attempt), `SpawnBastion`, `BoundaryBuilder`, `LayoutWriter`, `SiteFinder`, `SizeProfile`, `ArenaSchedule`, `ArenaContent`. |
| Validation | `Validation/ArenaValidator.cs`, `ArenaSurvey.cs`, `LaneMeter.cs` | Every §7 check with coded failures and metrics. |
| Biomes, props | `Biomes/BiomeFeatures.cs`, `Props/PropShapes.cs` | Geometry each biome rule needs; physical footprint of each prop. |
| Hash | `ArenaLayoutHash.cs` | 64-bit fingerprint of a layout for determinism tests and replay records. |

## Symmetry and coordinates

Functional symmetry is a half turn about the center: side 0 spawns on -Z, side 1 on +Z, and every element at (x, z)
with yaw a has an image at (-x, -z) with yaw a + 180 (both players get the same left and right). The generator
authors side 0 (z < 0, plus x < 0 near the axis) and `LayoutWriter` writes each element with its image and MirrorId.

## Skeleton (per attempt)

1. Contour: even harmonics only (r(a) = r(a+180)), area rescaled to the tier target.
2. Floor slab, center module at the origin (yaw chosen so the X-axis lane stays clear). Center modules whose
   geometry is all floor get a pair of low cover modules on the final floor as soft cover that retires at 85 s.
3. Spawn bastions (`SpawnBastion`): 5 x 3 m alcove, back wall, chevron screen whose wings lean 20 degrees toward the
   center. Exits past each wing tip. The wing length is tuned on the bare skeleton so useful central cover is 2.0-3.5 s
   away at walk speed and the spawn path stays in its band.
4. Keep-out zones (no islands, filler, or blocking props): bastion core, tip-turning spaces, crossover strip, firing
   lane zone (|z| <= 1.75 along X), center module plus 1 m.
5. Lane-end blocks on the X axis (designated lane about 13 m beyond 24 m; contour-bounded in 24 m), with noses that
   close the strip between block and center module. Bulwarks: staggered wall rows just outside the zones, raised
   along every ray from the center that meets nothing tall within 14.5 m. Skeleton lane posts: posts outside the
   zones on any lane with 12 m or more inside the zones.
6. Biome rule features (`BiomeFeatures`) are placed next, in mirrored pairs, then module islands by category
   (Topology, Elevation, Approach, Material, Cover) with 1.5 m gaps, then contour outcrops.
7. Terrain filler: lane breakers (spurs grown from one obstacle or free posts) for every lane over 26.5 m, rocks in
   open floor, and attached rocks (at most one per obstacle; lane breakers two) until cover reaches about 20.5%.
8. Props (3-8 definitions, one mirrored pair each), contraction repair (removes filler rocks or props nearest a
   stranded cell), standardized light shafts, BIO-011 gusts.

## Validation (fail-fast in the pipeline, full in tools/tests)

Contour, mirror parity, spawns and sightline, reachability and pockets, spawn path band, central reach, route width
and two disjoint approaches, lanes (longest 12-28 m, designated >= 12 m, two approaches reach the lane corridor),
cover 18-30% and usable area within 10%, elevation, natural sources <= 8, props, biome features, clear final floor,
contraction every 2.5 s from 55 s to 85 s, representative 3 m wall placements along approaches, single cover piece
inflation, destruction (all destructibles removed, shutters open, props gone).

## Status / next steps

- Validity per attempt (3 seeds x 24 biomes): 24 m ~89%, 32 m ~79%, 40 m ~47%, 48 m ~22%. With 8 seeds then the
  fallback, measured fallback rates were 0 / 0 / 0 / 5% (5 match seeds x 24 biomes per size).
- 48 m arenas fail mostly on contraction pockets and lanes around 28-30 m; next improvement would be smarter
  contraction-aware filler placement and lane breakers inside skeleton zones.
- Dev harness (not committed): a console app referencing the project, with `render` (PNG), batch, `pipeline`, and
  `fallback` modes, was used for tuning.
