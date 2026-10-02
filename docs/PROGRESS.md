# Progress

## Current stage
Stage 0 — complete. Stage 1 — rules subsystems complete and merged into `master` (2026-10-02): Reactions, Casting, Arena, Economy, Match, Sim core, Net foundation, Unity client foundation. Each library is tested on its own; they are not yet wired to each other. Next: Stage 1b (see "Next concrete task").

## Stage 1 status per subsystem
Design notes for every subsystem live in `docs/notes/<subsystem>.md`; interpretations are DECISIONS D-033…D-126.

| Subsystem | Project | State | Tests (observed at merge) |
| --- | --- | --- | --- |
| Core | `src/Confluence.Core` | Stage 0 foundation, unchanged in Stage 1 | 557 passed |
| Reactions | `src/Confluence.Reactions` | Cell grid, full L165 reaction order, volumes and sight, conductivity, wind, ice, burn, damp sand, venom, prisms, gravity fields, sound, snapshots; BIO-001 rain and BIO-011 gust schedule encoded | 89 passed |
| Casting | `src/Confluence.Casting` | Phase machine, cancel/interrupt/refund caps, re-cast lock, recast, channel, cue, mastery clamps, grammar registry for all 36 wizards; executable grammars WIZ-002, WIZ-010 and the guided normal route | 141 passed |
| Arena | `src/Confluence.Arena` | Generator (diameter/biome schedule, 72 module blueprints, props, biome geometry, mirrored assembly, 8 retries, 96 prevalidated fallbacks), layered navigation, full validator and fairness probe; MOD-001…072 verified | 196 passed (194 default + 2 `Category=Slow`; 7 min 53 s in the full run) |
| Economy | `src/Confluence.Economy` | Loadouts, shop, every predicate leaf, two-profile rule, dormancy, draft with guarantees/rerolls/rank 2/replacement, feat ledger, floors, all 48 worked builds replayed | 252 passed |
| Match | `src/Confluence.Match` | Conclave and Duel flow, timers, income, pairing, byes, sides, Resolve, elimination, bracket, placements, settings fingerprint, events; behind `IDuelRunner`/`IMatchEconomy`/`IArenaProvider` seams (scripted fakes in tests) | 77 passed |
| Sim core | `src/Confluence.Sim` | `DuelWorld` tick order, collision world, character motor, resources, damage pipeline, shields, healing budget, statuses, guard, melee, swap, owned-entity caps, proc budget, boundary, alcove, recall, lag-comp rewind, timer judgement, feat tracking; spells and materials are seams (`ISpellRuntime`, `IWorldMaterials`, null defaults) | 157 passed |
| Net foundation | `src/Confluence.Net` | LiteNetLib transport, handshake with ruleset fingerprint, sessions and 90 s reconnect, message registry, request dedupe, rate limits, serialization and quantization, input/snapshot buffers, clock sync, snapshot rate selection, spectator delay, UDP link simulator; no game messages yet | 120 passed |
| Unity client | `client/` | App shell and state machine, catalogue service, arena renderer with readable material patterns, boundary/ward/final-cover clock, preview cameras, settings, build and smoke scripts | 33 EditMode passed; player build and smoke run pass |

## Session log
### 2026-10-01 — session 1
- Inspected the repository (new; only `docs/confluence/` spec files) and the toolchain (see DECISIONS D-001).
- Added `tools/spec.mjs`, `tools/cat.mjs`, and `docs/SPEC_INDEX.md`.
- Wrote ARCHITECTURE, IMPLEMENTATION_PLAN, DECISIONS, and CLAUDE.md.

### 2026-10-01 — Core review fixes
- Rules: wind translates clouds only above strength 1 (D-023); fixed reaction order (`GlobalRules.World.ReactionOrder`, spec L165); movement and traversal constants (D-024); roster difficulty range; typed feat thresholds (`FeatDef.Count/Amount/WindowSec/MinRangeMeters/HealthFractionBelow/RequiresWin`); WIZ-004 voice lexicon (`Rules.VoiceLexicon`); seeded arena diameter sequence (D-028); `Schedules.PredicateRound` for round_at_least in Duel (D-029).
- Arena: `MaterialPatch` stores Sand and Growth units (D-025). `BoundaryContour.ClampInside` bisects for the farthest legal point instead of falling back to the center.
- Geometry: `SphereSweepVsYawBox` takes its normal from the primitive it hit, which is exact at radius 0; new exact `Intersections.SphereSweepVsRamp`.
- Json: `JsonWriter.FormatNumber` computes the shortest round-trip text itself, so .NET and Unity/Mono give identical output.
- Validator: transformation gates must be compulsory; nonzero augment costs, out-of-range difficulty, damaging spells without a channel, and stale channel tags are errors; capacity checks for every count leaf; per-wizard acquisition witnesses (D-030) and draft-schedule feasibility (D-032); exclusive groups inside worked builds; compound predicates count all/any roots only (D-031).

### 2026-10-02 — Stage 1 launch
- Stage 1 ran as a workflow of eight parallel agents (Reactions, Casting, Arena, Economy, Match, Sim core, Net foundation, Unity client foundation), each in its own git worktree, followed by a merge agent. Script: `.claude/workflows/stage1a.js` (local, gitignored).
- Two attempts were interrupted by account usage limits; all work survived as WIP commits on the worktree branches and was resumed in place.

### 2026-10-02 — Stage 1 merge
- Merged with `--no-ff` in order: reactions (`worktree-wf_74e4af8e-68e-1`), casting (-2), arena (-3), economy (-4), match (-5), sim-core (-6), net (-7), unity-client (-8). No conflicts: the branches own disjoint paths. No integration code changes were needed; `dotnet build Confluence.sln` gives 0 warnings and 0 errors.
- Checked for cross-assembly problems: no two assemblies define the same full type name (safe for the ledger tool and Unity's single plugin folder). Several simple names repeat in different namespaces (listed under Known issues).
- Consolidated 94 subsystem interpretations into DECISIONS D-033…D-126.
- Ran merge-time integration probes (scratch programs, not committed): generated arenas loaded into `DuelWorld` and `ReactionWorld` and stepped 900 ticks without errors; Sim walkers followed Arena navigation paths from spawn to center (38/48 reached it; the rest exposed the navigation issue below).
- Rebuilt the Unity client against the merged libraries (EditMode tests, player build, smoke run all pass). Unity re-serialized some URP and ProjectSettings assets with line-ending-only changes; those were discarded.
- Regenerated the ledger and removed the merged worktrees and branches.

## What runs
- `node tools/spec.mjs toc`, `node tools/cat.mjs stats`.
- `dotnet build Confluence.sln`, `dotnet test Confluence.sln`, `powershell -ExecutionPolicy Bypass -File tools/ledger.ps1`.
- Arena only, fast: `dotnet test tests/Confluence.Arena.Tests --filter "Category!=Slow"` (about 2 min); the full solution run includes the 6 min slow seed batches.
- Unity client: `powershell -ExecutionPolicy Bypass -File tools/build-client.ps1 -Tests -Smoke` (sync, EditMode tests, development player into `out/client/`, smoke run). Needs the project closed in any open editor.

## Tests executed
- 2026-10-02 (Stage 1 merge), `powershell -ExecutionPolicy Bypass -File tools/ledger.ps1` (runs `dotnet test Confluence.sln`): 1589 passed, 0 failed, 0 skipped.
  - Confluence.Core.Tests 557, Confluence.Reactions.Tests 89, Confluence.Casting.Tests 141, Confluence.Arena.Tests 196, Confluence.Economy.Tests 252, Confluence.Match.Tests 77, Confluence.Sim.Tests 157, Confluence.Net.Tests 120.
- 2026-10-02 (Stage 1 merge), `powershell -ExecutionPolicy Bypass -File tools/build-client.ps1 -Tests -Smoke` with all eight merged Confluence DLLs plus LiteNetLib synced into `client/Assets/Plugins/Confluence/`: EditMode 33 passed, 0 failed; `[Build] RESULT Succeeded errors=0 warnings=0` (62 s); smoke run exit 0 with `out/client-shot.png` (checked by eye: Raincourt sample arena, materials legend, boundary, final ring).
- Ledger: 1500 catalogue records; 72 verified (MOD-001…MOD-072), 1428 unstarted. 144 test methods carry `[Verifies]` in 8 test assemblies. `[Verifies]` tests also exist for BIO-001, BIO-004, BIO-007, BIO-011 (Reactions) and WIZ-002, WIZ-010 (Casting grammars), but those records have no `[ContentBehavior]` class yet because their full gameplay wiring is missing, so they stay unstarted.

## Missing assets (temporary art/audio in use)
All client art is temporary and procedural (details in `docs/notes/client.md`, "Temporary assets"):
- Procedural grayscale surface patterns per material kind (`Arena/PatternTextures.Surface`).
- Ward bars, boundary hazard stripes, dashed final-floor ring (`PatternTextures.Ward/Boundary/FinalRing`).
- Prop category glyph billboards and primitive prop bodies sized by mass (`PatternTextures.Glyph`, `Arena/PropPlaceholder`).
- Arena base materials (`Resources/Materials/*.mat`, URP Lit/Unlit copies).
- Unity's default procedural skybox.
- UI: Unity's default runtime theme plus `Resources/UI/Confluence.uss`.
- Debug fixtures to replace with real systems: `Arena/SampleLayouts` (hand-built layout; use the Arena generator) and `Cameras/PreviewWalker` (use the Sim `CharacterMotor`).
- No audio exists yet: music, effects and voice volumes are stored but not routed to a mixer; only master volume drives `AudioListener.volume`.

## Known issues
Integration findings from the merge (fix in Stage 1b):
- **Arena navigation cannot see thin solids.** `ArenaNav` builds nodes by sampling 0.5 m column centers, and walk edges between neighbouring columns do not test the segment. A non-walkable solid thinner than a cell whose footprint holds no column center is invisible: the graph walks through it and clearance ignores it. In 24 generated arenas (4 sizes × match seeds 7919·k, k = 1…6, round 1), 92 invisible solids reach above 1.05 m (mostly 2–2.5 m thin walls, acoustic plates, and metal or wood cover 0.15–0.3 m thick) and 50 lower ones; 18 of 24 arenas had at least one of the taller cases. Reachability, route width, spawn-path length, contraction and central-reach checks can therefore pass through walls the Sim motor treats as solid. Sight checks use exact geometry and are unaffected. Example: seed 7919, 24 m: the 0.4 m-thick, 1 m-high MOD-065 center cover at z = ±3 stops a Sim walker on a navigation path. Fix in Arena: test walk edges against collider footprints (or rasterize footprints conservatively), then re-run the fairness batches.
- **Props have no collision in the Sim.** Arena validates routes with props as obstacles (`PropShapes.TryGetCollisionBox`, in `Confluence.Arena`), but `CollisionWorld.FromLayout` reads only `layout.Solids`, and Sim depends only on Core. The prop collision shape must move to Core or be added by the server adapter.
- **The round diameter is computed twice.** Match draws its sequence from `RngStreams.For(seed, Arena, 0)` (15 Conclave / 8 Duel rounds); `ArenaSchedule.DiameterForRound` draws from sub 1 with 16 rounds, so the two disagree. The `IArenaProvider` adapter must pass Match's diameter (`ArenaRequest.Diameter`) and the arena seed; pick one owner.
- **Input flags conflict.** `Confluence.Casting.InputButtons` and `Confluence.Sim.Input.InputButtons` agree on bits 0–13, but bit 14 is `MoveForward` in Casting and `SwapFocus` in Sim (Casting also has Move* bits 15–17). Unify them into one enum before Net serializes inputs.
- **Repeated simple type names** in different namespaces (they compile and load fine, but adapters need aliases): `InterruptCause` (Casting/Sim), `SurfaceKind` and `SurfaceInfo` (Reactions/Sim, with different members), `DuelOutcome` and `DuelEndReason` (Match/Sim), `ArenaRequest` (Arena/Match), `RejectReason` (Casting/Net), `RequestLedger` (Economy/Net).
- Resolved by the merge: Sim's final-center cover retirement reads `Solid.RetiresAtFinal`, which the Arena generator sets on center-module cover (spec L678).

Carried from the subsystem reports:
- Reactions: porous walls storing water (BIO-005, MOD-021, MOD-046), heat sinks (BIO-020), basins (BIO-024), dust urns (BIO-022) and acoustic plates (BIO-009) are not implemented; spell-specific conversions (SPL-188 mist to steam, SPL-012 ash collapse, SPL-010/011 drying) are left to the spell runtime. Sim registrations (conductors, prisms, gravity fields, runtime light shafts) are not in reaction snapshots and must be re-applied before `RestoreSnapshot`. Stacked surfaces share one 2D cell. Snapshot format version 1 has no migration.
- Casting: only WIZ-002, WIZ-010 and the guided route execute; `GrammarRegistry.Create` throws for the other 34 wizards. Family primitives are unit-tested but not used by any grammar yet; their tolerances are placeholders. No base record carries `interrupt_refund`. Stationary requirements, longer recovery and recast rules are not parsed from record text. Hard-control and teleport detection rely on tag names.
- Arena: per-attempt validity is about 53% / 54% / 75% / 40% (24/32/40/48 m); retries and fallbacks cover it (0–1.4% fallback releases). Navigation build is the hot path (about 5 of 8 ms in clearance at 48 m; mean generation 0.6–4 s in Release). MOD-005, MOD-008 and MOD-010 are placed rarely. Biome rules and prop behaviors are geometry only; no biome or prop carries `[ContentBehavior]`.
- Economy: Core's `StructuralDeltas` registry is still empty (D-012, D-030), so re-run floors, worked builds and witnesses once Stage 5 registers deltas. One trajectory mode per loadout (L581) is not enforced (belongs to Casting/Sim).
- Match: server adapters (`IDuelRunner` over Sim, `IMatchEconomy` over Economy, `IArenaProvider` over Arena) are not written. Rating (L387), matchmaking (L389), bye practice range (L61), spectating permissions beyond `MatchEventLog.DelayedView`, observers, pause requests and adjudication (L395), reconnect penalties (L393) are not modeled. Only one full-length test reaches the bracket under the real Resolve schedule. `MatchRules` holds spec-open constants that may belong in `GlobalRules` (RevealSec 0, OutcomeGraceSec 1, custom shop 5–300 s, starting Crowns ≤ 100000, spectator delay ≤ 600 s).
- Sim: nothing produces inputs for FEAT-002, 006, 009, 011, 013, 014, 019, 020, 021, 022, 023 yet (FEAT-015 also needs summons). Moving props and their 5/15/25 collision damage (L173), opposable continuous pulls (L116), and fracture stages (L171) are not modeled. The motor's iterative depenetration is untuned for very thin geometry and very high speeds.
- Net: no game messages yet (inputs, delta snapshots, match flow, shop and draft requests); the server host must run a `SnapshotRateSelector` per session. Lag compensation (L423), prediction against the Sim motor, and the server and headless client hosts are Stage 3. LiteNetLib's own update thread (`ManualMode = false`) is barely tested.
- Unity client: Connecting through Results screens and Training are placeholders; `ArenaRenderer` has no incremental update for dynamic arena state (destruction, cell changes, moved props); remapping, gamepad and the other L371 accessibility options are Stage 7. Unity re-serializes `DefaultVolumeProfile.asset` and some URP assets after editor sessions (harmless diffs).
- The supplied catalogue produces six validator warnings for conditions without a base-kit witness (DECISIONS D-030). Re-check them once Stage 5 registers structural deltas.

## Next concrete task
Stage 1b — connect the subsystems, then start M1 content:
1. Fix the integration findings above: Arena navigation thin-solid blindness (then re-run the fairness batches), prop collision in the Sim, one owner for the round diameter, one shared input-flag enum.
2. Connect Sim to Casting: implement `ISpellRuntime` over `CastMachine` with an `ICastHost` on the Sim player (mana all-or-nothing, cooldowns, silence/hard control/ward), build `CastInputFrame`s with server-generated combat events, apply `MoveSpeedFactor` and `SuppressesSprint`, call `EndRecastWindow`, and count channels as spending for mana regeneration (L78).
3. Connect Sim to Reactions: implement `IWorldMaterials` over `ReactionWorld` (deposits, contact damage through `DuelWorld.ApplyDamage`, `ObscuringPathLength` from `SightPathLength`, surface mapping between the two `SurfaceKind` enums, volume events counted against field limits), and install biome rules (BIO-001 rain, BIO-011 gusts) so those records can get `[ContentBehavior]`.
4. Spell runtime framework: projectile, field, summon and terrain deliveries through the `DuelWorld` APIs, structural deltas (D-012), and data-driven spell parameters (D-011).
5. Server adapters for Match (`IDuelRunner`, `IMatchEconomy`, `IArenaProvider`) so a headless Conclave can run end to end.
6. Then Stage 2 (M1 content): WIZ-002 Bram Cinder and WIZ-010 Halen Tide with their 16 spells, signatures, passives and 32 transformations, enough shared augments and equipment for the floors, BIO-001 and the generator content M1 needs.
