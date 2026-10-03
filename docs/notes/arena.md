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
- A solid or prop whose layout image was added first adopts the exact negation of its partner's footprint (center,
  rotation, ramp direction), so containment, segment, and distance tests give bit-identical answers on mirrored
  inputs; yaw a + 180 would otherwise recompute sine and cosine with its own rounding.
- Clearance is a distance to a real boundary point (exact to obstacle footprints within 1.5 m, propagated beyond);
  when the graph is its own image, each node takes the smaller of its and its image's value.
- Mirrored spawn distances therefore agree exactly (the probe tolerance is 0.01 m; validator parity 0.05 m).

## Navigation geometry (true size, however thin)

- Nodes: one per standable surface per 0.5 m column (column center inside a walkable footprint, standing headroom).
- Every walk, jump, and drop edge is tested as a segment against the colliders binned in the columns it crosses:
  the capsule's feet rise linearly along the edge, and a footprint obstructs it where the solid reaches above the
  feet by more than its allowance (0.05 m for obstacles, the 0.35 m step for walkable surfaces) while its
  underside is below head height. A 0.04 m plate between two column centers cuts the edge. Each collider is tested
  once per segment, after a bounds prefilter and a per-column reach filter (open floor skips the test).
- Clearance: exact horizontal distance to the obstructing part of every footprint within 1.5 m (ramps obstruct only
  where their surface rises past the allowance), plus bisected surface-edge crossings; propagated outward.
- Props block through `PropPlacement.CollisionBox` (written by `LayoutWriter` from `PropShapes`); props are never
  supports or sight blockers here (D-068).
- Consequence for generation: the grid admits a capsule route through a gap only when it is at least about 1.3 m
  (capsule 0.8 m plus the lattice), so generated gates and gaps are either joined (overlapping) or at least 1.6 m
  (`BulwarkGateClear`), and gaps of 0.8-1.3 m read as closed (conservative).

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
   along every ray from the center that meets nothing tall within 14.5 m; inner-row gates are 1.6 m clear between
   the facing corners (bisected on the exact footprints), and the outer piece covers the gate's arc plus 1.2 m.
   Skeleton lane posts: posts outside the zones on any lane with 12 m or more inside the zones; a post joins a
   bulwark piece or keeps 1.6 m from it, never a narrower slot.
6. Before the bulwarks: required elevation pairs in 24/32 m; one landmark pair in 48 m (a topology, elevation, or
   approach module of at least 8 x 4 m, taking the topology budget; the big 8-10 m pieces find no room later).
7. Biome rule features (`BiomeFeatures`) are placed next, in mirrored pairs, then module islands by category
   (Elevation, Cover, Approach, Topology, Material) with 1.5 m gaps, then contour outcrops.
8. Terrain filler: lane breakers (spurs grown from one obstacle or free posts) for every lane over 26.5 m, rocks in
   open floor, and attached rocks (at most one per obstacle; lane breakers two) until cover reaches about 20.5%.
   Each stage keeps the skeleton guarantees or withdraws its culprit rocks (binary search, at most 3); withdrawn
   footprints are refused for the rest of the attempt, so a later pass never re-places them. Route-zone lane
   breakers (each a navigation rebuild) are limited to 12 per attempt.
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

- Default run (`dotnet test tests/Confluence.Arena.Tests --filter "Category!=Slow"`, about 1.8 min): module
  authoring and per-module `[Verifies]`, navigation, lanes and contour, generator (determinism, schedule, filters,
  streams), biome dressing, props, all 96 fallbacks through the probe, and a 576-arena fairness batch
  (24 biomes x 4 sizes x 6 seeds); thin-solid navigation (`ThinSolidNavigationTests`), stored prop collision boxes,
  and the Match round-diameter equivalence (`RoundDiameterTests`, the test project references Confluence.Match).
- Slow (`--filter Category=Slow`): 960-arena batch (24 x 4 x 10 further seeds) and 500 scheduled seeds per size.
- `FairnessProbe` re-checks every released layout independently (contour, mirror pairs, props, sightline, metric
  ranges, per-node mirrored distances, route width, contraction at times offset from the validator's samples).
- The library builds optimized in every configuration (`<Optimize>true</Optimize>`) and the test host uses server
  GC: generation does hundreds of navigation builds per arena.

## Status / next steps

Stage 1b arena-fix (done):
- Navigation sees thin solids (see Navigation geometry). Over 24 released arenas (4 sizes x match seeds 7919 k) no
  walk edge crosses a solid or prop box (594 tall solids were invisible before). `ThinSolidNavigationTests`: walls
  at 8 yaws x 5 offsets x 3 thicknesses, low cover and hanging plates, capsule routes around thin cover, props
  through the stored box, exact symmetry with mirrored thin solids, and every released biome sample checked edge by
  edge every 5 mm and node clearance against the true footprints.
- `PropPlacement.CollisionBox` / `HasCollision` are written by `LayoutWriter.AddPropPair` through
  `PropShapes.WriteCollision` (PropShapes stays the source of the shapes). Navigation, the stage witness check, the
  validator (route-width blocking, and a "collision box differs from its shape" props failure), the layout hash,
  and the probe read the stored box; the Sim can read the same Core field.
- Round diameter: Match owns it (one balanced sequence per match from `RngStreams.For(seed, Arena, 0)`, 15 Conclave /
  8 Duel rounds). `ArenaSchedule.DiameterSequence` and `DiameterForRound` repeat that draw exactly (stream round 0,
  sub 0; each four-round set is one draw, so a round's diameter does not depend on the length asked for). The
  hosting adapter passes Match's diameter as `ArenaRequest.Diameter`; unforced requests agree anyway.
  `RoundDiameterTests` pins both modes against the real Match classes (200 seeds each).
- Navigation build time is back to the pre-fix level (48 m about 7.5-8.5 ms per build, interleaved runs against the
  old build): each collider is tested once per walk segment after a bounds prefilter, the clearance center check is
  hoisted, the edge bisection stops once it cannot lower a node's clearance, and seeded nodes are popped from one
  sorted array beside the heap. All four are exact (a 160-arena combined layout hash is unchanged by them).
- Generator retuned for the true geometry: refused rocks (a withdrawn culprit is never re-placed; passes used to
  re-place and withdraw the same rock until the pass limit), skeleton posts may join a bulwark piece (the salvaged
  rule refused joins too), 1.6 m clear bulwark gates, spawn distance 40 m 10.5 -> 10.25 m and 48 m 11.5 -> 11.25 m
  (the old 40 m geometry left sub-capsule slots between the wing tip, the nose, and MOD-070-style center cover and
  forced a 33.56 m spawn path over the 33.5 m band in many seeds).

Fairness batches (every released arena passes the probe; worst mirrored-distance error 0.000 m):

| Batch | 24 m | 32 m | 40 m | 48 m | Time |
| --- | --- | --- | --- | --- | --- |
| 576 (fast) fallback | 0.7% | 0% | 0% | 2.1% | 106 s in the test (default run 1.8 min, 204 tests) |
| 960 (slow) fallback | 1.7% | 1.3% | 0% | 2.5% | 137 s |
| 500 per size (slow) fallback | 1.6% | 0.6% | 0% | 2.8% | 289 s (slow run 7.1 min, 2 tests) |
| Before the work: 576 fallback | 0% | 0% | 0% | 1.4% | default run about 1.6 min |
| Before the work: 500 per size (harness census) | 4 | 0 | 0 | 5 fallbacks | |

- First-attempt validity, 480 seeds per size (seed base 7A000000): 24 m 44.0% (47.9% before), 32 m 46.5% (57.1%),
  40 m 73.5% (73.5%), 48 m 37.1% (47.9%). Per-attempt CPU time equals the old generator's (profiled at 48 m: 2.6 s
  per attempt in both); the extra attempts per arena make large-arena batches slower (48 m census of 200 seeds:
  59 s vs 32 s wall, mostly the fallback tail).
- MOD-005 / 008 / 010: placed only as 48 m landmarks or 40 m topology (500-seed census: 34 / 8 / 7 arenas, before
  27 / 12 / 8). Not changed.

Next steps (measured, not done):
- The 48 m landmark (D-066) is the main 48 m cost: first-attempt validity is 58.1% without it and 46.7% when only
  half of the attempts place it (500-seed census fallbacks 25 -> 13, 48 m mean seeds 2.98 -> 2.13). Most landmark
  modules leave open floor nothing tall can stand on (decks, bowls, low cover), so lanes over them stay open. Half
  the attempts loses coverage of rare modules, though: MOD-045 and MOD-046 (material modules with solid pieces,
  placed last) already appear only 1-2 times per 2000 arenas, because 160 random poses rarely fit in the crowded
  floor; a fine-lattice scan for material modules with pieces (as cover modules get) raised MOD-044/046/047 but not
  MOD-045. Make those placeable first, then amend D-066.
- Other remaining causes: lanes over 28 m that the filler cannot break (most candidate points lie in keep-out zones;
  route-zone breakers fail the second disjoint approach about 89% of the time), contraction pockets (32 m: wing tip,
  nose, and center-module cover; 48 m: inner and outer bulwark rows less than 1.3 m apart near the lane-end blocks),
  24 m cover in BIO-006/013/005. The true grid needs passages of at least about 1.3 m, so every gap the generator
  makes should be joined or at least 1.6 m.
- Tried and rejected (lower validity or no gain): contraction-guarded filler stages, a no-slot rule for attached
  rocks, FillerGap 1.35, slot-free bulwark pieces, a 1 m beam test for open bulwark rays, wing tip extent 5.0/6.0,
  row spacing 3.0/3.25, 32 m spawn distance 9.5/10.0, witness-first route-zone breakers, a route-zone budget of 24
  (+1-2% validity for about +10% time), StageCulprits 6, 20 contraction repairs.
- Dev harness (not committed, session scratchpad): batch, census, failure histograms and dumps, PNG render, stage
  notes, profiling with dotnet-trace.
