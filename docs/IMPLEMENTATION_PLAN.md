# Implementation plan

The end state is the complete game in `docs/confluence/`: 36 wizards, 288 exclusive spells, 120 shared equipment definitions, 864 free augments, 24 biomes, 72 modules, 48 props, 48 worked builds, all modes, accessibility alternatives, and final presentation. Stages deliver playable, tested increments inside one architecture (`docs/ARCHITECTURE.md`). A stage is complete only when its tests pass and `docs/PROGRESS.md` records what was actually executed.

## Stage 0 — Foundation and tooling
- Spec and catalogue query tools (`tools/spec.mjs`, `tools/cat.mjs`), spec index, durable docs.
- `Confluence.Core`: JSON parser, math and geometry, deterministic RNG streams, tick time, typed catalogue model and loader, predicate AST, catalogue validator (counts, ID intervals, ownership, dangling references, tiers, slots), global rules constants, feats, round and economy schedules, arena layout contract, structural-delta registry.
- Solution, test projects, ledger generator.

## Stage 1 — Rules subsystems (parallel, each with focused tests)
- **Reactions**: cell grid (units 0–4, flags, ≤128 regions), the full §4 reaction table in fixed order, steam/ash/mist/dust volumes with path-length sight blocking, connected-water and metal conductivity, wind translation and dispersal, ice tiles, growth ignition, damp sand, venom dilution, energy conservation caps.
- **Casting**: phase machine, cancellation and interrupt refund rules, recast state, mastery normalization and clamps, grammar framework with server-side input validation; grammars for WIZ-002 (pressure release) and WIZ-010 (pressure metering).
- **Arena**: diameter schedule, biome/module/prop assembly into a mirrored route skeleton, collision layout, material cells, props, spawns, central floor, boundary contour and contraction, validators (reachability, route redundancy, path lengths, sightlines, cover ratio, parity, contraction), eight-seed retry and fallback layouts, large seed-batch tests.
- **Economy**: loadout slots and Unique groups, issued gear, active-item choice, shop offers/rerolls/purchase/sell/placeholders, every predicate leaf, structural profile, dormancy topological resolution, draft sampler with guarantees, per-card rerolls, rank 2, replacement, feat ledger, availability floors, worked-build check (each BLD-001…048 loadout keeps its listed augments active under dormancy resolution, and BLD-037…048 meet their demonstrated eligibility; spec L643, L693, DECISIONS D-032).
- **Match**: Conclave and Duel flow with phase timers, income and winner bonus, tier unlocks, draft schedule, pairing cost minimization, byes, Resolve loss, elimination, bracket with seeded byes and draw replays, placements with tie means, through an `IDuelRunner` interface.
- **Sim core**: collision world and character motor (walk, sprint, jump, crouch, dodge, air steering, fall damage, recall), resources and regeneration, damage pipeline, shields, healing budget, statuses and hard-control immunity, guard and perfect guard, common melee profiles, field/summon limits with oldest retirement, proc budgets, boundary damage, duel resolution, feat event tracking.

## Stage 2 — First playable content (Milestone M1)
- Wizards WIZ-002 Bram Cinder (Heat, pressure release) and WIZ-010 Halen Tide (Water, pressure metering): grammars, 16 spells (SPL-009…016, SPL-073…080), signatures, passives.
- All 32 of their transformations (AUG-305…320, AUG-433…448).
- Enough shared augments to keep ≥10 implemented legal alternatives per rarity for both wizards (unconditional Silver/Gold/Prismatic first).
- Enough equipment for valid shops (tier 1 first).
- Biomes, modules, and props required by the generator for M1 arenas, including water sources (BIO-001 Raincourt).
- Bots that use real inputs, so headless duels exercise the full loop.

## Stage 3 — Networking
- Protocol and serialization, authoritative server host running a Conclave or Duel, headless client, input commands with redundancy, snapshots, reliable events, prediction and reconciliation, interpolation, 150 ms lag compensation, reconnect window, rate limits.
- Validation: server plus two headless clients complete a match; then server plus two Unity client instances.

## Stage 4 — Unity client M1
- Boot, connect, wizard select, shop, five-card draft with per-card rerolls, alcove, duel, results, next round.
- Procedural arena rendering from the layout, first-person controller using shared prediction, spell/field/steam presentation that matches authoritative volumes, HUD (health/shield, mana, stamina, six slots, signature, active, consumable, statuses, timer, boundary), temporary art and audio tracked in PROGRESS.
- **M1 exit**: server plus two real client instances play shop → draft → networked duel → reward → next round, with the focused tests listed in the build prompt passing.

## Stage 5 — Content waves (repeat until the catalogue is complete)
- Wizards in waves of four: grammar, eight spells, signature, passive, sixteen transformations, a counterplay scenario test per hard-control/concealment/summon/movement/high-damage transform.
- Shared augments by family (12 × 24), equipment by category, all biomes/modules/props, worked builds as integration tests.

## Stage 6 — Modes and competition
- Duel best of seven with replays and half wins, Unranked/Ranked Conclave, Ranked Duel ratings, Laboratory, Trials, Custom Conclave with settings fingerprint, Versus constructs (four bot competence levels plus labeled demonstration bot), spectator delay, replays from seed plus event stream, reconnect.

## Stage 7 — Accessibility and input
- Remapping with conflict detection, controller, voice casting (finite syllable lexicon, local processing) with an equivalent rhythm route, gesture normalization and direction-key tracing, hold/toggle, one-handed modes, subtitles, palettes, reduced motion and flash, FOV 80–110, scalable HUD, text-to-speech menus.

## Stage 8 — Interface completeness
- Home, codex with locked-prerequisite display, collection, build workbench and inspector (raw versus effective caps), reaction garden, training classrooms per grammar, death explanation, replay viewer, saved build import and export.

## Stage 9 — Presentation and performance
- Final art, animation, audio, VFX readability, cosmetics catalogue, localization pass, and the measured performance budget (60 ticks, 120 fps target).

## Stage 10 — Release gate
- Every catalogue ID verified in the ledger, full-match server stability, reconnect, accessibility parity measurements, and the performance budget.
