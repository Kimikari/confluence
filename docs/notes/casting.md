# Confluence.Casting design notes

Cast phases, cancellation/interrupt/refund/recast rules, mastery normalization, and the per-wizard casting
grammar framework. Library: `src/Confluence.Casting` (netstandard2.1, depends only on Confluence.Core).
Tests: `tests/Confluence.Casting.Tests` (`dotnet test tests/Confluence.Casting.Tests`).

## Status / next steps
- Done: input frame and `InputButtons`, `CastableSpell` (from `SpellDef`), mastery types and clamps, refund
  ledger, `CastMachine`, grammar interfaces, `GuidedNormalGrammar`, WIZ-002 `PressureReleaseGrammar`,
  WIZ-010 `PressureMeterGrammar`, 36-wizard `GrammarRegistry`, and one reusable primitive per family
  (`Grammars/Families/`). Last verified: `dotnet build Confluence.sln` 0 errors / 0 warnings;
  `dotnet test tests/Confluence.Casting.Tests` 141 passed, 0 failed.
- Next: implement the other 34 grammars on top of the family primitives (each is a `GrammarBase` subclass plus
  a `case` in `GrammarRegistry.Create` and `executable: true` on its descriptor). Sim must implement
  `ICastHost`, build `CastInputFrame`s (combat events are server-generated only), act on `CastEvent`s, end sprint
  while `CastMachine.SuppressesSprint`, apply `MoveSpeedFactor`, and call `EndRecastWindow` when a recastable
  effect ends. Net should send `InputButtons` + `SelectSlot` + `GrammarInputEvent`s per tick.

## Layout
- `Input/` — `InputButtons` ([Flags], shared with Sim/Net), `CastInputFrame` (one per tick: held/pressed/released,
  slot selection, aim, authoritative position/velocity/grounded, client `GrammarInputEvent`s with sub-tick
  timestamps, server `CombatEvent`s).
- `Spells/` — `CastableSpell` (id, mana, cooldown/windup/recovery ticks, `CastWeight`, move factor, stationary,
  channel max/pulse, interrupt-refund tag, anticipatory cue, damaging/projectile, `RecastRule`,
  `GrammarParameters`). `FromDef` builds one from a catalogue `SpellDef`.
- `Machine/` — `CastMachine`, `ICastHost`, `CastEvent`/`CastEventKind`, `CastEventId` (player, sequence),
  `RefundLedger`, `CastPhase` (+ cancel/interrupt/channel-end/reject reasons), `PendingRecast`.
- `Mastery/` — `MasteryResult` (exactly one reward or an exact `MissReason`), `MasteryClamp`, `CastModifiers`.
- `Grammars/` — `IGrammar`, `GrammarBase`, `GrammarStep`, `GrammarContext`, `GrammarUiState`, `GrammarFamily`,
  `InputRoute`, `GrammarDescriptor`, `GrammarRegistry`, the three executable grammars, and `Families/` primitives.
- `CastingRules` — local constants the spec leaves open (each cites its line); `TickWindow`, `RateLimitedToggle`.

## Cast machine (spec L86-L96, L470-L472)
Per-tick order in `CastMachine.Step`: grammar `Observe`; recast expiry; forced interruption from host status;
slot selection; common-action cancellation; phase timers (release, channel pulses, recovery end); Recast input;
signature activation; opening a preparation; advancing the preparation.

- Idle -> Preparation: the grammar's `WantsToBegin` (e.g. primary pressed). Refused (with a `Rejected` event) when
  warded, silenced, hard-controlled, re-cast locked, or the spell is cooling down; not opened when a common action
  is pressed the same tick or guard is held. `PreparationStarted` (+ `CueStarted` if the spell needs a cue).
- Preparation -> commitment: grammar returns `Commit`/`AutoCommit` with its single `MasteryResult`. Mana is paid via
  `ICastHost.TryPayMana` (cost after `MasteryClamp.PaidCost`, 70% floor), cooldown starts, `Committed` is emitted
  with the cast id and mastery. Not enough mana or a cooldown at that moment cancels for free.
- Windup = intrinsic `cast_time` (a windup reward shortens only that, within caps); `Released` at its end (same
  tick for instant spells). A recastable spell opens its `PendingRecast`. Channeled spells pulse every
  `ChannelPulseTicks` up to `ChannelMaxTicks` (<= 3 s); primary or a common action ends the channel on any tick
  and the remaining pulses stop. Then Recovery (0.20/0.35/0.45 s defaults or the record's longer value) -> Idle.
- Cancel before commitment: no mana, exposure counted in `TotalExposedPreparationTicks`, 0.15 s (9 tick) re-cast
  lock. Changing the selected slot mid-preparation cancels (no swap windup).
- Interrupt after commitment (silence, hard control, `Interrupt(...)`): mana stays spent, cooldown runs; 25% refund
  only with the interrupt-refund tag. Voluntary cancel after commitment (common action during windup): no refund.
  All refunds for a cast go through `RefundLedger`: shared 25% cap, each effect key once per cast.
- Recast (T): only the selected slot's pending state; refused while preparing/winding up/channeling or when
  silenced/controlled/warded; reuses the original `CastEventId` and mastery; costs `RecastRule.ExtraMana` only;
  adds the rule's recovery. Default window is the original effect's lifetime (Sim calls `EndRecastWindow`).
- Anticipatory cue (spec L96): for >70 direct damage, hard control or teleport, a commit requested earlier than
  `prepStart + 0.25 s - windup` is held inside (free-to-cancel) preparation, so release is never less than
  0.25 s after the cue start and nothing is secretly delayed after commitment.
- Signatures (spec L472): activation commits immediately with `MissReason.NotApplicable`, listed windup, no grammar.
- Cast event ids are `(player, sequence)` and increment for every opened attempt, cancelled ones included.

## Grammar framework
`IGrammar` instances live for the whole duel (state can span casts). The machine calls `Observe` every tick,
`WantsToBegin`/`Begin` when idle, `Advance` during preparation (including the opening tick) and `End` once.
Grammars read only `CastInputFrame`, so server (authoritative) and client (prediction/HUD) produce identical
results; `GrammarUiState` carries gauge, windows, bands, progress, queued commit and the exact `ReleaseNow` miss.
Input faster than an accepted interval is throttled by `RateLimitedToggle` and reported as `TooFastToggling`.

Families (spec L468 language list; `GrammarRegistry` holds a descriptor for all 36 wizards):

| Family | Wizards | Primitive(s) |
|---|---|---|
| ButtonTiming | 002, 011, 012, 019, 020, 032 | `TickWindow`, `PressureReleaseGrammar` pattern |
| HeldGauge | 010, 021, 036 | integer gauge + `RateLimitedToggle` (`PressureMeterGrammar` pattern) |
| KeySequence | 005, 022 | `TokenSequence`, `ChordTracker` |
| GestureStroke | 003, 009, 031 | `StrokeGeometry` (resample, normalize scale/rotation, 8-way segments, direction keys) |
| VoiceRhythm | 004, 007 | `BeatPattern`/`BeatScorer` (voice and rhythm routes share windows) |
| CombatEvent | 006, 015, 035 | `CombatEventWindow`, `ArmedReward` |
| Movement | 013, 014, 027, 029, 030 | `MovementTracker` (real travel, legs, declared velocity, forced-move exclusion) |
| WidgetPuzzle | 008, 016, 023, 024, 026, 033, 034 | `TokenSequence` + per-spell `GrammarParameters` |
| SchedulingMemory | 017, 018 | `TokenSequence` (motif), `ArmedReward`-style pending release |
| AimPath | 001, 025, 028 | `AimTracker` (origin aim, steadiness cone, ordered checkpoints, sockets) |

`GrammarRegistry.Create(id)` returns WIZ-002 / WIZ-010 grammars and throws `NotImplementedException` for the
others; `CreateGuided(id)` returns the guided normal route (stated normal preparation, never a reward) and throws
for records with no fixed normal preparation (WIZ-005, -008, -009, -017).

## WIZ-002 Bram Cinder, crest release
Hold primary; gauge rises linearly to the crest at 0.45 s (tick 27) and falls at the same rate.
- Release at >= 0.20 s (tick 12) commits normally. Release before 0.20 s queues a normal cast committing at tick 12
  (WIZ-001's roster rule), cancellable for free while queued.
- Still holding at 0.60 s (tick 36) auto-vents a normal cast (`AutoCommit`, `MissReason.AutoVented`); no misfire.
  A release observed on tick 36 itself is a player release (late), not an auto-vent.
- Crest window: closed interval [0.40 s, 0.50 s] = ticks 24-30 of hold. Inside: +12% direct damage on damaging
  spells, or 12% less mana on nondamaging ones. Outside: `EarlyRelease`/`LateRelease` with signed tick error.

## WIZ-010 Halen Tide, pressure metering
Integer pressure (x60 so per-tick rates are exact): starts at 30 on the press, +60/s while primary is held,
-50/s while Modifier A (vent) is effective. Pure holding enters 45-55 at 0.25 s and leaves at 0.42 s, so a
0.25 s continuous in-band run needs at least one well-timed vent of at least 0.1 s (e.g. vent at 50 for 6 ticks,
then keep compressing and release before 55).
- Release window 0.30-0.90 s (ticks 18-54). A release before tick 18 queues a normal cast at tick 18.
- Any release inside the safe 20-80 band casts normally. Leaving the band vents automatically; 0.30 s later the
  normal cast auto-commits (`LeftSafeBand`), still cancellable at no cost during the vent.
- Still holding at 0.90 s auto-vents a normal cast immediately (`HeldPastWindow`).
- Mastery: pressure within 45-55 on every tick of the final 15 ticks before release: 15% less mana.
- Vent toggles obey the 0.1 s (6 tick) accepted interval via `RateLimitedToggle`; a spammer gets exactly what an
  ideal 0.1 s toggler gets, and throttled input inside the qualifying run reports `TooFastToggling`.

## Interpretations (spec line references)
- L92 quick vs heavy has no threshold: intrinsic windup <= 0.35 s is quick (0.20 s recovery, 85% move); longer is
  heavy (0.35 s, 70%). Tags `summon`/`field` are major (0.45 s); `root/silence/stun/slow/pull` are control.
- L92 "movement while casting": factor applies in Windup and Channel; preparation and recovery are full speed
  because grammars keep movement live during preparation (L474, WIZ-005/-013/-029).
- L80 sprint: `SuppressesSprint` is true for non-quick or channeled casts from preparation through channel; the Sim
  ends/refuses sprint rather than refusing the cast.
- L90 cancellation: guard, dodge and common melee presses are the cancelling common actions (L474 no wizard
  overrides them); guard held blocks opening. Every pre-commit cancel (including selection change, silence, ward)
  applies the 0.15 s lock.
- L90/L124 refunds: one shared 25% cap per cast; each refund effect key fires once per cast. The interrupt refund
  is 25% of the amount actually paid (after reductions). Tag name `interrupt_refund` (no base record has it yet).
- L94 recast: refused (not queued) while another cast is in preparation/windup/channel; a new cast of the same slot
  replaces its old pending recast.
- L96 cue: hard control = tags root/silence/stun; teleport = tag `teleport`; damage threshold from the record's
  largest damage number. Implemented as a held commit inside preparation (never an extra post-commit delay).
- L116 hard control interrupts committed casts; silence too; ward only blocks/cancels preparation.
- L478-L480 caps: efficiency rewards + mastery-linked equipment efficiency clamp at 20%; damage/projectile speed/
  windup at 15% per category; paid cost never below 70% (L124). Windup reductions round down and leave >= 1 tick.
- L470 guided route: tap opens a fixed preparation equal to the record's stated normal preparation; always normal.
- L365 voice: a syllable below the confidence threshold keeps its slot as unrecognized and blocks commitment.
- L369/L490 strokes: resample evenly first (polling-rate independent), then normalize centroid, rotation (only if
  the rune allows) and bounding-box scale; direction keys are compared by 8-way segment structure.
- WIZ-001 "2-degree cone" read as every sample of the final window within 2 degrees of the release aim.
