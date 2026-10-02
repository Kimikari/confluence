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
