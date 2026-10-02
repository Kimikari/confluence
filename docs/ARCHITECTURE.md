# Confluence architecture

Authoritative rules live in engine-agnostic C# libraries. The .NET server and the Unity client run the same compiled code. Unity renders the results and collects input; it never decides combat, economy, or arena outcomes.

## Layout

```
docs/confluence/            supplied specification (read-only; never edit)
docs/                       ARCHITECTURE, IMPLEMENTATION_PLAN, PROGRESS, DECISIONS, CONTENT_LEDGER, SPEC_INDEX
tools/spec.mjs, cat.mjs     bounded spec / catalogue queries (Node 24)
src/Confluence.Core         netstandard2.1, no deps: Json, Geometry, Random, Time, Content (catalogue model, predicates,
                            validator, structural deltas), Rules (global constants, reaction order, schedules,
                            feats, voice lexicon), Arena (layout contract)
src/Confluence.Reactions    -> Core. 0.5 m cell grid, material units/flags, fixed-order reaction engine, obscuration
                            volumes, sight occlusion, conductivity, wind, ice/growth/sand/venom rules, safety caps
src/Confluence.Casting      -> Core. Cast phase machine (preparation/commitment/release/recovery), cancel/interrupt/
                            refund/recast rules, IGrammar per wizard, grammar input validation, mastery results
src/Confluence.Arena        -> Core. Seeded generator: size schedule, biome/module/prop assembly, mirrored skeleton,
                            validation (reachability, sightlines, cover ratio, parity, contraction), fallbacks
src/Confluence.Economy      -> Core. Loadout + slots, equipment shop, predicate evaluator, structural spell profile,
                            dormancy resolver, augment draft sampler/rerolls/ranks/replacement, feat ledger
src/Confluence.Sim          -> Core, Reactions, Casting. DuelWorld: collision, character motor (shared with client
                            prediction), resources, damage pipeline, statuses, guard/melee/dodge, spell runtime,
                            content behaviors (Spells/, Transforms/, Augments/, Equipment/, Wizards/), triggers and
                            proc budgets, boundary, duel result, feat events, lag-compensation history
src/Confluence.Match        -> Core, Economy, Arena. Conclave/Duel round flow, phase timers, pairing, byes, Resolve,
                            bracket, income, placements. Runs duels through IDuelRunner (Sim adapter wired by server)
src/Confluence.Net          -> all libs + LiteNetLib. Protocol, serialization, server host, client session
                            (prediction, reconciliation, interpolation)
src/Confluence.Server       net10.0 console: authoritative match server
src/Confluence.HeadlessClient net10.0 console: scripted/bot client for automated network validation
tests/Confluence.*.Tests    net10.0 xUnit, one project per library
client/                     Unity 6000.6.4f1 project. Assets/Plugins/Confluence/*.dll synced from src builds
```

Libraries target `netstandard2.1` with `LangVersion 9` so Unity compiles against the same DLLs. Do not use C# 10+ features (file-scoped namespaces, global usings, record structs), `System.Text.Json`, or any UnityEngine type in `src/`.

## Conventions

- Units: meters, seconds, degrees in data; radians internally only where stated. Y is up, Unity-style left-handed axes (+Z forward, +X right). Yaw 0 faces +Z; positive yaw turns right (clockwise seen from above).
- Time: 60 authoritative ticks per second (`Ticks.PerSecond`). Durations from content convert to ticks with `Ticks.FromSeconds` (round to nearest, minimum 1 for positive durations). Gameplay timers are integer ticks.
- IDs: catalogue string IDs (`WIZ-002`, `SPL-009`, `EQP-001`, `AUG-305`, `BIO-001`, `MOD-001`, `PROP-001`, `BLD-001`, `FEAT-001`) are the stable keys everywhere, including network messages, logs, ledger, and tests.
- Randomness: only `Confluence.Core.Random.Rng` with explicitly derived streams (`RngStreams.Offer`, `Arena`, `Cosmetic`, `Bot`). Never `System.Random`, `Guid`, or wall-clock time in rules.
- Global caps live in `Confluence.Core.Rules.GlobalRules` with spec line references. Content code never hard-codes a global cap; local values may only be stricter.
- Every rule decision that interprets ambiguous text gets an entry in `docs/DECISIONS.md`.

## Content model

`Catalogue.Load(path)` parses the supplied JSON into immutable definitions (`WizardDef`, `SpellDef`, `EquipmentDef`, `AugmentDef`, `BiomeDef`, `ModuleDef`, `PropDef`, `BuildDef`). Eligibility JSON parses into a typed `Predicate` AST covering every leaf in the predicate grammar. `CatalogueValidator` checks counts, ID intervals, ownership (WIZ-n owns SPL-(8n-7)…SPL-8n), dangling references, tiers, slots, rarity, and predicate references. It also confirms that every condition has an acquisition witness (one wizard and legal loadout) and fits the real draft schedule of both modes (DECISIONS D-030, D-032).

Descriptive prose becomes executable behavior in `Confluence.Sim` through registries keyed by catalogue ID:

- **Spells** — a `SpellBehavior` builds a typed `SpellForm` from the base `SpellDef` (mana, cooldown, cast time, range come from the catalogue; prose parameters such as projectile speed, splash radius, and world units are written explicitly with a comment quoting the source text). Custom logic such as detonation recasts goes in an `ISpellScript`.
- **Transformations** — an `ISpellTransform` edits the `SpellForm` for rank 1/2 in the spec's application order: base → delivery/trajectory → payload/reaction → equipment → mastery → global caps and proc budgets. Structural tag changes (for example projectile → trap) are declared once in `Core.Content.StructuralDeltas` and read by both Economy eligibility and Sim.
- **Shared augments, equipment, signatures, passives** — typed effects with trigger, condition, magnitude, channel, target filter, budget group, stacking rule, and permitted secondary events (spec §14 data model).

A record is **implemented** when its registry entry exists and is wired into gameplay. It is **verified** when a test tagged `[Verifies("ID")]` exercises its specific behavior. `docs/CONTENT_LEDGER.md` is generated from the registries and test attributes; never edit statuses by hand. Draft and shop pools include only implemented records (`IContentAvailability`). The server refuses to start a match whose available pools break a design floor, such as ten legal augments per rarity.

## Duel simulation tick order

1. Validate and apply inputs (rate limits, resource checks).
2. Character movement, dashes, and displacement (clamped to the legal boundary).
3. Guard, melee, dodge, and cast phase progression; releases spawn deliveries.
4. Deliveries advance; hits resolve direct impacts through the damage pipeline; world deposits are queued.
5. Reactions in fixed order: direct impacts → heat-water / heat-ice / frost-water consumption → electricity connectivity → wind movement → decay.
6. Fields, summons, props, and volumes update (contact effects, lifetimes, oldest-owned retirement).
7. Statuses and DoT, resource regeneration, shield expiry.
8. Boundary damage (bypasses all mitigation; never triggers items or augments).
9. Death, draw, and timer resolution. Simultaneous lethal outcomes in one step are draws.

Damage resolves in this order: valid hit and guard angle → precision multiplier (≤1.25) → guard → channel resistance (cap 35%, floor −15%) → other passive reductions (combined cap 45%) → shields (earliest-expiring first) → health.

## Networking

The server owns all state listed in spec §14. Clients send input commands per tick, each with a sequence number and redundant recent inputs. The server sends snapshots 20–30 times per second carrying the last processed input sequence, plus reliable events (casts, damage, reactions, phase changes). The local client predicts its own movement and preparation using `Sim.CharacterMotor` and replays unacknowledged inputs after each snapshot. Remote entities interpolate about 100 ms behind. Lag compensation rewinds player hit volumes by at most 150 ms; fields, props, and projectiles stay authoritative at impact time. Match, shop, and draft actions are reliable request/response messages validated atomically by the server.
