# Progress

## Current stage
Stage 0 — complete. Stage 1 — rules subsystems merged (2026-10-02). Stage 1b — integration merged into `master` (2026-10-03): the Sim runs the spell runtime over Casting, the reaction world over Reactions, props, the stat block and the effect framework in every duel; Arena navigation sees true geometry and stores prop collision; Match owns the round diameter; `Confluence.Hosting` runs whole matches (arena, economy, duel adapters, commands, replays, bots) with spells and reactions live; the Unity client has the M1 screens and an offline Laboratory. Next: Stage 2 M1 content (see "Next concrete task").

## Status per subsystem
Design notes for every subsystem live in `docs/notes/<subsystem>.md`; interpretations are DECISIONS D-033…D-126 (Stage 1) and D-127…D-182 (Stage 1b).

| Subsystem | Project | State | Tests (2026-10-03, Stage 1b merge) |
| --- | --- | --- | --- |
| Core | `src/Confluence.Core` | Stage 0 foundation; arena contract gained `PropPlacement.CollisionBox/HasCollision` | 557 passed |
| Reactions | `src/Confluence.Reactions` | Cell grid, full L165 reaction order, volumes and sight, conductivity, wind, ice, burn, damp sand, venom, prisms, gravity fields, sound, snapshots; sticky slow, force clamp, gust schedule accessor; biome rule data for BIO-001/BIO-011 | 89 passed |
| Casting | `src/Confluence.Casting` | Phase machine, cancel/interrupt/refund caps, recast, channel, cue, mastery clamps, grammar registry for all 36 wizards; executable grammars WIZ-002, WIZ-010 and the guided route; the one `InputButtons` enum; `ICastHost.TryPayCommitment`, `IsActionLocked`, `CastModifiers.CostIncrease` | 166 passed |
| Arena | `src/Confluence.Arena` | Generator, true-footprint navigation (thin solids, exact clearance), stored prop collision boxes, Match's diameter schedule, validator and fairness probe; MOD-001…072 verified | 206 passed (204 default + 2 `Category=Slow`) |
| Economy | `src/Confluence.Economy` | Loadouts, shop, predicates, dormancy, draft, feat ledger, floors, 48 worked builds | 252 passed |
| Match | `src/Confluence.Match` | Conclave and Duel flow behind `IDuelRunner`/`IMatchEconomy`/`IArenaProvider` (now implemented by Hosting) | 77 passed |
| Sim | `src/Confluence.Sim` | Sim core plus: `PlayerCaster` over `CastMachine`, `SpellRuntime` with 14 delivery primitives and resolved spell forms, `ReactionMaterials` (default materials: deposits, impacts, contact damage, clouds as fields, wet cells, surfaces, acquisition), props (bodies from stored boxes, pushes, collision damage), stat block with caps, effect framework (triggers, conditions, budgets, stacking, cast pricing, shared refund ledger), one `ContentRegistry`; content SPL-009, SPL-010, EQP-051/066/096, AUG-145/169/175, BIO-001, BIO-011 | 345 passed |
| Hosting | `src/Confluence.Hosting` | `MatchHost`, arena provider and cache/prefetch, hosted economy and `LoadoutMapper`, command gateway (idempotency, rate limits), `SimDuelRunner` over `SimWorldFactory` (spells, reactions, props, effects), replays, host messages, bots with four competence levels | 36 passed |
| Net foundation | `src/Confluence.Net` | Transport, handshake, sessions, message registry, dedupe, rate limits, serialization, buffers, clock sync, snapshot rates, spectator delay; no game messages yet | 120 passed |
| Unity client | `client/` | App shell, generated-arena preview with the Sim motor and the arena's Reactions environment, offline Laboratory (sparring duel with Sim reactions and props, training effects, full local rounds), M1 screens on view models (shop, draft, build, reveal, HUD, results), Reactions cell/volume renderer | 73 EditMode passed; player build and 7 smoke views pass |

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

### 2026-10-02 — Stage 1b launch
- Prepared on master: `PropPlacement.CollisionBox/HasCollision` in the arena contract; new `Confluence.Hosting` library and test project (referenced by Net and the ledger tool).
- Workflow `.claude/workflows/stage1b.js` (local): agents arena-fix, sim-casting-spells, sim-world-effects, hosting, unity-m1-ui in worktrees, then merge-and-integrate. If interrupted: commit leftovers in each `.claude/worktrees/*`, map worktrees to labels from their changed paths (first attempt: `wf_0cd0c382-18b-1` arena-fix, `-2` sim-casting-spells, `-3` sim-world-effects, `-4` hosting, `-5` unity-m1-ui; interrupted once and resumed in place), and rerun with `args: { inPlace: {label: path}, only: [...], priorDone: [...] }`.

### 2026-10-03 — Stage 1b merge and integration
- The Stage 1b workflow was interrupted by a usage limit before the merge step; it was resumed with nothing merged yet.
- Merged with `--no-ff` in order: arena-fix (`worktree-wf_0cd0c382-18b-1`), sim-casting-spells (-2), sim-world-effects (-3), hosting (-4), unity-m1-ui (-5). Textual conflicts only in `DuelWorld.cs` and `DuelEvents.cs` (both Sim branches appended `DuelEventType` values and the later-stage hooks); both sides kept.
- Reconciled the duplicate designs of the two Sim branches (D-165): one set of behavior contracts and one `ContentRegistry` in `Confluence.Sim.Content` (`ContentBehaviorIndex`, `IEffectContentSource` and `Effects/ContentContracts.cs` removed; proof content moved to `Content/Augments`, `Content/Equipment`, `Content/Biomes`); `EffectHost` implements `IEffectHost`; wizard passives attach through the effect framework.
- Wired the stages together: commitments paid and priced through the effect framework (`ICastHost.TryPayCommitment`, `CastModifiers.CostIncrease`), `CastReleased` raised by the spell runtime, one refund ledger per cast (D-144); `QueueDeposit` honours declared footprints, lifetimes, field ids and the heavy-projectile ring (D-138, D-153); projectile impacts reach Reactions (`IWorldMaterials.QueueImpact`: ice, brittle and combustible cover); Hosting's `SimWorldFactory` builds duels with the spell runtime, reactions, catalogue props and content (D-176); replays keep `Tapped` and grammar events (D-175).
- Integration bugs found and fixed: wizards without a grammar route crashed duel creation (WIZ-005 in the 8-bot Conclave; D-147); `InputTrack` dropped `Tapped`/`GrammarEvents`; the client still used the removed `ArenaSchedule.ScheduledRoundCapacity`; the client drew Sim prop bodies twice and never hid destroyed props (`ArenaRenderer.SyncProps`); Laboratory duels had no catalogue (props anchored and indestructible); walk mode and the sparring layout ignored prop collision (`CollisionWorld.FromLayoutWithProps`).
- Integration tests added: Sim `Integration/SpellReactionIntegrationTests` (Fireball through WIZ-002's grammar into a Water Pool-like patch forms steam that blocks acquisition through 2 m of path, `[Verifies("SPL-009")]`; Fireball shards on map ice are proc-free environment damage; effect refunds share the cast's 25% cap), Sim `PropSystemTests` preview-collision test, Hosting `IntegrationTests` (the six proof records shape a duel built from mapped Economy loadouts, with `[Verifies]`; generated prop collision boxes become Sim solids), the 8-bot Conclave now asserts SPL-009/SPL-010 commits and damage, heat in every duel's reaction world and the installed biome rule, and a client EditMode test for prop placeholders.
- Consolidated the reports into DECISIONS D-127…D-182; updated ARCHITECTURE (Sim layout, content model, cast pricing, tick order) and the subsystem notes.

### 2026-10-03 — Stage 2 + 3 launch
- Accepted merge decision D-138 (Fireball deposits the heavy-projectile ring so its steam patch can reach the 2 m sight-blocking path).
- Workflow `.claude/workflows/stage2.js` (local): implement -> adversarial verify -> fix pipeline for bram-kit, halen-kit, augments-a, augments-b, equipment, props-biomes, net-protocol (worktrees under `.claude/worktrees/`), then merge-and-integrate. Running in two batches (batch 1: bram-kit `wf_ac402310-4a4-1`, halen-kit `-2`, augments-a `-3`, net-protocol `-4`; batch 2: augments-b, equipment, props-biomes). Resume with `args.inPlace` (implement in an existing worktree), `args.impl` (skip to verify), `args.fixed` (skip to merge), `args.only`.

## What runs
- `node tools/spec.mjs toc`, `node tools/cat.mjs stats`.
- `dotnet build Confluence.sln`, `dotnet test Confluence.sln`, `powershell -ExecutionPolicy Bypass -File tools/ledger.ps1`.
- Fast iteration: `dotnet test Confluence.sln --filter "Category!=Slow"` (about 2 min; the Arena slow seed batches add about 7 min). Hosting tests take about 1 min (whole bot matches).
- Unity client: `powershell -ExecutionPolicy Bypass -File tools/build-client.ps1 -Tests -Smoke` (sync, EditMode tests, development player into `out/client/`, seven smoke views into `out/smoke/`). Needs the project closed in any open editor.

## Tests executed
- 2026-10-03 (Stage 1b merge), `powershell -ExecutionPolicy Bypass -File tools/ledger.ps1` (runs `dotnet test Confluence.sln`, slow Arena batches included): 1848 passed, 0 failed, 0 skipped.
  - Confluence.Core.Tests 557, Confluence.Reactions.Tests 89, Confluence.Casting.Tests 166, Confluence.Arena.Tests 206, Confluence.Economy.Tests 252, Confluence.Match.Tests 77, Confluence.Sim.Tests 345, Confluence.Net.Tests 120, Confluence.Hosting.Tests 36.
- 2026-10-03 (Stage 1b merge), `powershell -ExecutionPolicy Bypass -File tools/build-client.ps1 -Tests -Smoke` with all nine netstandard2.1 libraries (Hosting and Net included) plus LiteNetLib synced: EditMode 73 passed, 0 failed; development player built; all seven smoke views exit 0 and wrote `out/smoke/{arena,sandbox,shop,draft,hud,reveal,results}.png` (checked by eye).
- Ledger (same run): 1500 catalogue records; 82 verified (MOD-001…MOD-072, SPL-009, SPL-010, EQP-051, EQP-066, EQP-096, AUG-145, AUG-169, AUG-175, BIO-001, BIO-011), 0 implemented without verification, 1418 unstarted. 171 test methods carry `[Verifies]` in 9 test assemblies. WIZ-002 and WIZ-010 have `[Verifies]` grammar tests but no `[ContentBehavior]` kit class yet (signature and passive missing), so they stay unstarted.

## Missing assets (temporary art/audio in use)
All client art is temporary and procedural (details and locations in `docs/notes/client.md`, "Temporary assets"):
- Procedural grayscale surface patterns per material kind; ward bars, boundary hazard stripes, dashed final-floor ring (`Arena/PatternTextures`).
- Prop category glyph billboards and primitive prop bodies sized by mass (`Arena/PropPlaceholder`; they now follow the Sim's prop bodies).
- Fighter bodies (side-colored capsules with a visor and focus), the first-person focus cylinder, guard band and melee sweep sectors (`Presentation/FighterView`, `OfflineDuel`, `ShapeMeshes`).
- Field discs and summon spheres in owner colors; reaction cell tiles and volume hulls (`Presentation/OwnedEntityViews`, `Presentation/ReactionViews`); temporary solids as patterned boxes.
- Damage numbers, status text badges, HUD bars and slot cells, shop/draft/build/reveal/results cards (`Resources/UI/Confluence.uss` on Unity's default runtime theme; no fonts, icons or card art).
- Arena base materials (`Resources/Materials/*.mat`, URP Lit/Unlit copies) and Unity's default procedural skybox.
- No spell delivery visuals (projectiles, beams, cones) yet.
- No audio exists yet: music, effects and voice volumes are stored but not routed to a mixer; only master volume drives `AudioListener.volume`.

## Known issues
Resolved in Stage 1b: Arena navigation thin-solid blindness (D-127), prop collision in the Sim (D-130, D-155), the double round-diameter derivation (D-131), the input-flag conflict (D-132).

Open:
- **Repeated simple type names** in different namespaces (they compile and load fine; adapters use aliases): `InterruptCause` (Casting/Sim), `SurfaceKind` and `SurfaceInfo` (Reactions/Sim), `DuelOutcome` and `DuelEndReason` (Match/Sim), `ArenaRequest` (Arena/Match), `RejectReason` (Casting/Net), `RequestLedger` (Economy/Net).
- **Standard rules still refuse to start** (D-010 floors): only six EQP/AUG records are implemented. Every full-match test runs with the Laboratory overrides `bypass-economy-floors` + `pools-whole-catalogue`. Standard arenas now use only BIO-001 and BIO-011 (the registered biomes, D-167).
- Arena: the true-geometry fix raised fallback releases to 1.25% overall in the 500-seed census (master 0.45%; 48 m 2.8%, 24 m 1.6%) because first-attempt validity fell (24/32/40/48 m: 44.0/46.5/73.5/37.1%); every release, fallbacks included, is still fair. The biggest lever is D-066's 48 m landmark (58.1% validity without it), but MOD-045/MOD-046 (about 2 per 2000 arenas) need to become placeable first. Lanes over 28 m the filler cannot break, contraction pockets at 32 m and 48 m, and 24 m cover in BIO-006/013/005 cause the rest. MOD-005/008/010 are still placed rarely.
- Sim spells: WIZ-002 and WIZ-010 have no `IWizardKitBehavior` (signature, passive); WIZ-010 has no implemented spell, so Halen bots cast nothing. No `TerrainCollision` combat event (WIZ-014); ray reflection only off `Reflective` solids (Reactions prisms should register an `IRayReflector`); directional shields follow facing (no fixed-yaw option); summons have no move commands; deployables lack "arms on impact"; waves follow the caster's ground height; only projectile deliveries report impacts to Reactions.
- Sim world: reaction snapshots omit the Sim-side links (cloud fields, source ids, cleared-cloud feat keys), so Net must serialize or re-derive them before restoring; no prop behaviors (metal/reflective props are not conductors or prisms); props never fall or rotate; nothing consumes dash charges; `ReactionWorld.StateHash()` takes a full snapshot (too heavy per tick).
- Hosting: no Net wiring yet (drain `HostOutbox`, route by audience, feed `SubmitInput`, map command codes); bot range and aggression are tuned for melee and should be re-tuned for spells; no standard-rules end-to-end match test until the floors pass.
- Unity client: the Laboratory duel has no spell runtime and spell deliveries have no renderer yet; reaction tiles on raised surfaces are drawn at floor height; the narrated decisive sequence, damage-direction indicator, shop comparison and pinning, remapping and gamepad are not done; the client's `LoadoutDuelSetup` duplicates Hosting's `LoadoutMapper`; Unity logs a harmless "ICU data not available" warning.

Carried from Stage 1:
- Reactions: porous walls storing water (BIO-005, MOD-021, MOD-046), heat sinks (BIO-020), basins (BIO-024), dust urns (BIO-022) and acoustic plates (BIO-009) are not implemented; spell-specific conversions (SPL-188 mist to steam, SPL-012 ash collapse, SPL-010/011 drying) are left to those spells. Stacked surfaces share one 2D cell. Snapshot format version 1 has no migration.
- Casting: only WIZ-002, WIZ-010 and the guided route execute; 34 wizards have no grammar (wizards without a guided route cannot cast at all, D-147). Family primitives are unused; no base record carries `interrupt_refund`; stationary, recovery and recast rules are not parsed from record text.
- Economy: Core's `StructuralDeltas` registry is still empty (D-012, D-030); re-run floors, worked builds and witnesses once Stage 5 registers deltas. One trajectory mode per loadout (L581) is not enforced.
- Match: rating (L387), matchmaking (L389), bye practice range (L61), spectating permissions, observers, pause and adjudication (L395), reconnect penalties (L393) are not modeled. `MatchRules` holds spec-open constants that may belong in `GlobalRules`.
- Sim: FEAT-002, 009, 013, 014, 019, 022, 023 and FEAT-015 (summons) still lack producers in shipped content; opposable continuous pulls (L116) and fracture stages (L171) are not modeled; the motor's depenetration is untuned for very thin geometry and very high speeds.
- Net: no game messages yet; lag compensation (L423), prediction against the Sim motor, and the server and headless client hosts are Stage 3. LiteNetLib's own update thread is barely tested.
- The supplied catalogue produces six validator warnings for conditions without a base-kit witness (DECISIONS D-030).

## Next concrete task
Stage 2 — M1 content, on the Stage 1b runtime:
1. WIZ-002 Bram Cinder and WIZ-010 Halen Tide kits: the remaining 14 base spells (SPL-011…016, SPL-073…080) as `ISpellBehavior`s on the delivery primitives, with `[Verifies]` tests through each wizard's real grammar; record-specific scripts where the primitives are not enough.
2. Their signatures and passives as `IWizardKitBehavior` (Kiln Vent, Tempered Hands, and Halen's), attached through the effect framework.
3. The 32 wizard transformations of those 16 spells (`ISpellTransform`, stage order L286/L510, rank 2 L541, structural deltas D-012 in Core's registry).
4. Enough shared augments and equipment to satisfy the D-010 floors (ten legal augments per rarity, a fillable shop), so standard rules start without Laboratory overrides; then add a standard-rules end-to-end match test.
5. BIO-001 Raincourt end to end in M1 matches (already a Sim behavior) and whatever generator content M1 needs; re-tune bots for spell play.
6. Then Stage 3 — the game protocol: Net messages for inputs (with `Tapped` and grammar events), delta snapshots (including reaction-world state and its Sim-side links), match flow, shop and draft requests over `MatchHost`; server and headless client hosts; client prediction and reconciliation.
