# Progress

## Current stage
Stage 0 — Foundation and tooling (in progress).

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

## What runs
- `node tools/spec.mjs toc`, `node tools/cat.mjs stats`.
- `dotnet build Confluence.sln`, `dotnet test Confluence.sln`, `powershell -ExecutionPolicy Bypass -File tools/ledger.ps1`.

## Tests executed
- 2026-10-01: `dotnet test Confluence.sln`: 557 passed, 0 failed (Confluence.Core.Tests). The other test projects contain no tests yet.

## Missing assets (temporary art/audio in use)
- None yet; the client has not been created.

## Known issues
- The supplied catalogue produces six validator warnings for conditions without a base-kit witness (DECISIONS D-030). Re-check them once Stage 5 registers structural deltas.
- Worked builds are not yet checked against their own loadouts: each listed augment active, and BLD-037…048 meeting their eligibility. This needs the Economy predicate evaluator (IMPLEMENTATION_PLAN Stage 1 Economy).

## Next concrete task
- Stage 1 subsystems on top of Core (Reactions, Casting, Arena, Economy, Match, Sim core).
