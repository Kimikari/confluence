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

Navigation is exactly symmetric on such a layout (`ArenaNav.IsHalfTurnSymmetric`, required by the validator):
- Search ties break on a mirror-equivariant node rank (canonical column min(c, mirror c), layer, which of the pair),
  so Dijkstra parents, clearance propagation, and `FindNode` ties (points on cell edges) resolve to mirrored nodes.
- Clearance is a propagated distance to real surface edges (an over-estimate of the true clearance); when the graph
  is its own image, each node takes the smaller of its and its image's estimate. Float rounding in rotated boxes
  (yaw a vs a + 180) and propagation order otherwise left mirrored cells up to 0.5 m apart.
- Mirrored spawn distances therefore agree exactly (the probe tolerance is 0.01 m; validator parity 0.05 m).

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
6. Before the bulwarks: required elevation pairs in 24/32 m; one landmark pair in 48 m (a topology, elevation, or
   approach module of at least 8 x 4 m, taking the topology budget; the big 8-10 m pieces find no room later).
7. Biome rule features (`BiomeFeatures`) are placed next, in mirrored pairs, then module islands by category
   (Elevation, Cover, Approach, Topology, Material) with 1.5 m gaps, then contour outcrops.
8. Terrain filler: lane breakers (spurs grown from one obstacle or free posts) for every lane over 26.5 m, rocks in
   open floor, and attached rocks (at most one per obstacle; lane breakers two) until cover reaches about 20.5%.
9. Props (3-8 definitions, one mirrored pair each), contraction repair, standardized light shafts, BIO-011 gusts.

Contraction repair: for the first stranded cell, try (nearest first) removing a filler rock or prop pair, then
trimming 0.75 m off a rock face toward the cell. A change is kept only if the cell regains its route at the failing
time and no lane through the freed footprint exceeds 28 m (a lower-bound lane check on the current navigation skips
most rebuilds). When no single change rescues the cell, the nearest lane-safe rock removal is applied anyway (two
rocks may pinch one gap) and the search repeats, up to 12 times.

## Validation (fail-fast in the pipeline, full in tools/tests)

Contour, mirror parity (geometry and the navigation graph), spawns and sightline, reachability and pockets, spawn
path band, central reach, route width and two disjoint approaches, lanes (longest 12-28 m, designated >= 12 m, two
approaches reach the lane corridor), cover 18-30% and usable area within 10%, elevation, natural sources <= 8,
props, biome features, clear final floor, contraction (every 2.5 s from 55 s to 85 s; an interval in which a cell
is first cut off while still outside the 2 m edge band is re-sampled at 0.3125 s), representative 3 m wall
placements along approaches, single cover piece inflation, destruction (all destructibles removed, shutters open,
props gone).

## Tests

- Default run (`dotnet test tests/Confluence.Arena.Tests --filter "Category!=Slow"`, about 1.6 min): module
  authoring and per-module `[Verifies]`, navigation, lanes and contour, generator (determinism, schedule, filters,
  streams), biome dressing, props, all 96 fallbacks through the probe, and a 576-arena fairness batch
  (24 biomes x 4 sizes x 6 seeds).
- Slow (`--filter Category=Slow`): 960-arena batch (24 x 4 x 10 further seeds) and 500 scheduled seeds per size.
- `FairnessProbe` re-checks every released layout independently (contour, mirror pairs, props, sightline, metric
  ranges, per-node mirrored distances, route width, contraction at times offset from the validator's samples).
- The library builds optimized in every configuration (`<Optimize>true</Optimize>`) and the test host uses server
  GC: generation does hundreds of navigation builds per arena.

## Status / next steps

- Per-attempt validity (3 seeds x 24 biomes): 24 m ~53%, 32 m ~54%, 40 m ~75%, 48 m ~40%. Released through Generate
  with 8 seeds then the fallback: 576-arena batch fallback 0 / 0 / 0 / 1.4% by size; an 1800-arena census 0.33%
  overall. Every one of the 72 modules is placed in that census (rarest: MOD-008 and MOD-010, 8 times, 48 m only);
  the 500-seed slow batch asserts that every module appears.
- Main rejection causes: 24 m cover under 18% (open floor runs out) and composition; 32 m contraction and lanes just
  over 28 m; 40-48 m spawn path outside the band and contraction pockets behind lane breakers. Better filler
  placement (no sub-capsule gaps between rocks, contraction-aware lane breakers) would cut retries and test time.
- `ComputeClearance` dominates navigation builds (about 5 of 8 ms at 48 m); a faster exact-equivalent queue or
  incremental rebuilds after single-rock changes are the next performance levers.
- Dev harness (not committed): a console app referencing the project with batch, failure-histogram, PNG render, and
  fallback modes was used for tuning.
