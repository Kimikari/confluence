# Decisions and ambiguity log

Precedence: (1) the explicit build requirements in `docs/confluence/confluence-claude-code-prompt.md`, (2) the design's global rules (`confluence-design.md` §1–16 and the rule chapters), (3) individual catalogue definitions (`confluence-catalogue.json`). JSON is canonical for record data; the Markdown mirrors it and is canonical for rule prose. Spec line references use `node tools/spec.mjs`.

## Engine and toolchain

**D-001 Engine: Unity 6000.6.4f1.** Installed toolchain (2026-10-01): Unity 6000.4.5f1 (no build targets installed) and 6000.6.4f1 (Windows Mono and IL2CPP player plus Windows Dedicated Server modules), .NET SDK 10.0.401, Node 24, Visual Studio, Git. No Python, Godot, or Unreal. Unity 6 is the user's existing engine convention (Arcane Duel). 6000.6.4f1 is the only install that can build Windows players. Its manual confirms Roslyn with C# 9.0, .NET Standard 2.1 as the default API level, and support for netstandard2.1 managed plug-ins. The license is activated through Unity Hub.

**D-002 Engine-agnostic authoritative core.** Rules are C# `netstandard2.1` libraries (C# 9) with no Unity dependency. The authoritative server is a .NET 10 console app, not a Unity dedicated-server build. Reasons: the server must own every outcome (§14), client prediction must run identical movement code, tests must run quickly through `dotnet test` without an editor, and replays need rule-level determinism. Unity consumes the same compiled DLLs, synced by `tools/sync-unity.ps1`.

**D-003 Custom kinematic collision instead of Unity physics.** Arena gameplay geometry is validated boxes, yaw-rotated boxes, and ramps (§4, §7). The core provides capsule-vs-solid character movement, raycasts, and sphere sweeps, so the server and client prediction share exact behavior. Unity colliders are presentation only.

**D-004 Transport: LiteNetLib (MIT) over UDP.** It provides reliable-ordered and unreliable-sequenced channels from pure C#, so the identical DLL runs in .NET 10 and Unity Mono/IL2CPP.

**D-005 Tick model.** 60 fixed ticks per second (§14). Durations are stored as integer ticks. Snapshots go out at 30 Hz by default and drop to 20 Hz under congestion.

**D-006 JSON parsing in-house.** A small recursive-descent parser in Core avoids divergence between Newtonsoft, System.Text.Json, and Unity JsonUtility. The supplied catalogue is loaded unchanged at runtime.

**D-007 Tests: xUnit on net10.0**, one test project per library. Content verification tests carry `[Verifies("ID")]` so the ledger can be generated.

## Content implementation policy

**D-010 Availability gating.** Draft and shop pools contain only records whose behavior is implemented, so a menu entry is never an empty handler. The full catalogue remains loaded and validated, and the codex/Laboratory can display unimplemented records labeled as such. Match start asserts the design floors (ten legal augments per rarity per wizard and loadout; enough equipment for three offers plus rerolls). If a floor fails, the server reports the gap instead of silently shrinking the rules.

**D-011 Prose parameters.** Numeric fields present in the JSON (mana, cooldown, cast_time, range, tier, durability) are read from the catalogue. Parameters that exist only in prose (speeds, radii, units, durations) are written in the behavior code with the quoted source phrase. A test asserts each declared number appears in the record's text.

**D-012 Structural deltas.** A transformation's change to structural spell tags (for example projectile → trap/field) is declared once in `Core.Content.StructuralDeltas` and used by both Economy eligibility and Sim. Without a declaration, a transformation keeps the base spell's structural tags.

## Rule interpretations

**D-020 Equipment offer draw.** "Drawn from unlocked affordable tiers, then uniformly among legal definitions in a tier" (§5) is interpreted as: choose uniformly among unlocked tiers whose price ≤ current balance and that contain at least one legal, implemented, non-visible definition; then choose uniformly within that tier.

**D-021 Duplicate predicate children.** Some `any` lists repeat identical leaves (for example AUG-142 lists `school:heat` four times). They are evaluated as written; duplicates are harmless and are not "fixed" in the catalogue.

**D-022 Feats are rule data.** FEAT-001…FEAT-024 come from the §5 table, not the JSON. They live in `Core.Rules.Feats` so predicate validation can confirm referenced feat IDs exist.

**D-023 Wind moves clouds only above strength 1.** Spec L151: "A directional force above strength 1 translates the volume at up to 3 meters per second; strength 2 disperses its front 2 meter slice." Catalogue strengths are integers, so translation needs strength 2 or more (`GlobalRules.World.WindTranslatesClouds`). Seventeen records carry a strength-1 wind or force; only SPL-105 ("Wind 1 displaces smoke/steam 2 m"), SPL-110 ("Wind 1 scatters nearby mist within 1 m once"), and BIO-011 ("gust moves clouds up to 2 m ... force strength 1") say they move clouds. Those are binding local prose effects (L300) implemented by their own behaviors with their stated bounds; they do not loosen the global rule. The other strength-1 sources (SPL-089, -090, -093, -096, -113, -115, -117, -225, -226, -227, -229, -230, AUG-497, AUG-506) never move steam or dust.

**D-024 Movement and traversal constants the spec leaves open.** Chosen once in `GlobalRules.Movement` / `GlobalRules.Terrain` so the Sim motor, client prediction, and the arena validator share them:
- Gravity 18 m/s² (the Laboratory may edit it, L69). Common jump apex 1.1 m, so launch speed sqrt(2gh) ≈ 6.29 m/s and level airtime ≈ 0.70 s. The airtime outlasts the 0.6 s wet charge that "jump the short pulse" counters (L148); the apex reaches the one-meter ledges and terraces of MOD-030 and MOD-066 and stays far below the 3 m fall-damage threshold (L175).
- Ground acceleration 50 m/s² (about 0.11 s to walk speed). Air acceleration 8 m/s²: over one jump the horizontal velocity changes by at most about 5.6 m/s, enough to stop a 5.5 m/s run but not to reverse it ("Air steering is moderate and cannot reverse horizontal momentum immediately", L80).
- Crouch speed 2.75 m/s (half of walk). Capsule radius 0.4 m, height 1.8 m standing and 1.2 m crouched, eye height 1.6 m and 1.0 m. Two players fit side by side within the 2.5 m minimum route width (L312).
- Step height 0.35 m; standardized stair risers stay below it (L312 "stairs and collision edges are standardized").
- Traversable routes contain gaps of at most 1.5 m and ledges of at most 1.0 m (L312 "Traversable gaps require only the common jump"; MOD-033 uses a one-meter gap). A walking jump covers about 3.8 m, so the margin absorbs late take-offs and latency.
- Moving props deal their 5/15/25 collision damage (L173 "above their threshold") only at a relative impact speed above 3 m/s, for every mass class.

**D-025 Cells carry sand and growth units.** L136 lists water, heat, frost, and dust units, but L154 compares equal water and sand units, L153 ignites growth with at least two heat units, and the catalogue states quantities (BIO-007 "Two 2 m beds start with Sand 2 Water 1", BIO-002 "Heat 1 and Growth 1", EQP-118 "Growth strength 1"; Sand and Growth 1-3 appear 41 times). Cells and `Arena.MaterialPatch` therefore also store Sand and Growth unit counts, capped at four like every strength (L163). `MaterialFlags.Sand` and `MaterialFlags.Growth` still mark the material type. The generator, Reactions, preview, and replays read the same counts.

**D-026 Contraction easing.** L48 fixes only the start (55 s) and end (85 s). Each boundary radius moves toward the final radius along a smoothstep in time (`BoundaryContour.ContractionProgress`). The edge starts and stops without a jump in speed, so the announced start reads clearly and the final floor settles. Its peak speed is 1.5 times the linear rate: about 1.0 m/s for a 48 m arena (24 m to 4 m over 30 s), far below walk speed.

**D-027 The final floor is 8 m across.** L48 "the validated central 8 meter duel space" and L678 "the anchored eight-meter floor" give a diameter, so the final radius is 4 m (`GlobalRules.Boundary.FinalFloorDiameter`). MOD-066 places "a level eight-meter final floor" inside a 10 × 10 m center module, which a 16 m floor could not fit.

**D-028 Arena diameter sequence.** L306: rounds form consecutive sets of four scheduled rounds (1-4, 5-8, 9-12, then bracket rounds 13 onward). Each set uses every diameter once, in an order drawn uniformly from the match's Arena stream. No 24 m round may be followed directly by a 48 m round, inside a set or across a set boundary. "24 then 48" is read as ordered, so 48 then 24 is allowed. Under this reading a legal order always exists; the "unless needed to preserve the four-round set" clause stays as a fallback that never triggers. A tied-round replay keeps its round's diameter. See `Schedules.ArenaDiameterSequence`.

**D-029 round_at_least uses Conclave round numbers.** The thresholds follow the Conclave draft schedule: AUG-006 round 3 is the second Silver draft, and AUG-021 round 9 is the first Prismatic draft. Duel compresses the same six drafts into rounds 1-6 (L225), so a literal comparison would make both augments impossible to draft in Duel. Evaluators compare against `Schedules.PredicateRound`: Conclave rounds pass through unchanged, and Duel round r maps to 2r − 1, the Conclave round holding the same draft. Feat thresholds count actual completed duels in either mode (`Schedules.CompletedDuelsBefore`), because a feat records events, not a schedule position.

**D-030 Known witness gaps in the supplied catalogue.** L643 requires every alternative branch of a condition to have a concrete acquisition witness. With no structural deltas declared yet, the validator warns about six records. AUG-056 needs an Earth spell that also carries terrain, and no base spell is both. AUG-063, AUG-130, AUG-134, AUG-226, and AUG-239 each have a spell alternative that no single kit can meet together with the rest of the condition: Growth field with terrain, Growth or Venom field with Water, and summon with Frost or terrain. In each of those five, the equipment alternative (EQP-118, EQP-116, or EQP-117) remains a working witness. These are warnings, not errors, because a transformation's structural delta (D-012) may supply the tag later, and the draft sampler only offers eligible records. Re-check them when Stage 5 registers deltas.

**D-031 Compound acquisition predicates are all/any roots.** L596 states 121 compound predicates. The supplied shared augments have exactly 109 `all` plus 12 `any` roots. The three `not` roots (AUG-241, AUG-245, AUG-246) negate a single leaf and are not compound.

**D-032 Acquisition-plan validation.** `CatalogueValidator` checks each augment against the actual draft schedule of both modes (L600, L643). A new definition takes one free choice at a draft of exactly its rarity, and a rank 2 takes a later choice. Compulsory parents come first, recursively, and round and feat thresholds must hold at the drafting round. `any` branches and enumerated-ID count leaves are alternatives; unrestricted count leaves need unnamed parents. A child occupies one of the six slots, so count leaves can require at most five parents. `equipped_tag` counts are bounded by a matching of Unique groups to the eight equipment slots (L210). Satisfiability is checked per wizard: one wizard must own the named spells and transformations and carry the required spell profiles in one kit. Searches that exceed their budget are treated as satisfiable, so the check reports no false errors. A worked build's own loadout is checked against its listed augments in the Economy stage (see IMPLEMENTATION_PLAN), because that needs the production predicate evaluator and structural profiles.
