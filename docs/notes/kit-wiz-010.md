# WIZ-010 Halen Tide kit — design notes

Owner paths: `src/Confluence.Sim/Content/Wizards/WIZ-010/**`, `tests/Confluence.Sim.Tests/Content/WIZ-010/**`,
`src/Confluence.Hosting/Bots/Profiles/WIZ-010*`, this file.

## Status / next steps
- Done: the eight spells (SPL-073..080), the signature (Undertow Mantle) and the passive (Pressure Reserve) in
  `HalenTideKit`, all sixteen transformations (AUG-433..448) with structural deltas (`Transforms/HalenStructure.cs`),
  `[Verifies]` tests for every spell, the signature and passive, and AUG-433/434.
- In progress (this session): tests for AUG-435..448, record-quote tests for the transformations, the Economy
  structural-profile test, the end-to-end WIZ-010 duel, the bot profile.
- Finished IDs (`[ContentBehavior]` + `[Verifies]`): SPL-073, SPL-074, SPL-075, SPL-076, SPL-077, SPL-078, SPL-079,
  SPL-080, WIZ-010, AUG-433, AUG-434 (the rest carry `[ContentBehavior]` and get their tests this session).

## Layout
- `HalenTideKit.cs` — `IWizardKitBehavior`: signature form (field delivery at the caster) and passive effect.
- `HalenRules.cs` — kit-wide interpretations (constants with the text they interpret) and world helpers
  (water deposits on the ground below a point, clouds through the reaction world, water removal, events).
- `TideEvent.cs` — kit events in the duel stream (`DuelEventType.Spell`).
- `TideProjectile.cs` — the water projectile of SPL-073 / SPL-079 (banking, miss mist, rolling).
- One file per spell: `PressureJet`, `WaterHammer` (+ `WaterHammerScript`), `BubbleBuckler` (+ `BubbleShieldDelivery`,
  `BubbleShieldEntity`), `WaterPool` (+ `TwinPoolDelivery` for AUG-440), `CascadeVault` (+ `CascadeVaultScript`),
  `RiptideLane` (+ `StreamDelivery`, `StreamCurrentEntity`), `DeepglassPrison` (+ `DeepglassPrisonScript`,
  `PrisonFollowUpEntity`), `SealTheSpring` (+ `SpringSplashDelivery`, `SpringConeDelivery`).
- `Transforms/` — one file per spell pair, plus `HalenStructure` (structural delta table, registered by every
  transformation's static constructor, so any process that builds the content registry sees them).

## Interpretations (for DECISIONS consolidation)
- Impact water without a stated duration (SPL-073, 074, 075 break, 077, 079) lasts 3 s (`HalenRules.ImpactWaterLifetimeSec`):
  a zero-lifetime residue could never be the "Wet wall" AUG-433 banks from; 3 s is the kit's shortest authored puddle.
- SPL-073 / SPL-079 impact water lands on the ground below the impact point (cells are 2D, spec L136).
- SPL-074: a miss adds 0.3 s to the heavy-cast recovery (0.4 s windup, D-047) through `CastMachine.ExtendRecovery`
  (recovery may only grow, spec L92). Its spray is one cell under the struck target or at the struck solid.
- SPL-075: the bubble is a directional entry (50 degree half arc) in the shared shield pool (100 cap, 8 s). A thermal hit
  it absorbs is one heat reaction: 10 more shield is consumed at the bubble's next step and a 1 m steam puff (3 s,
  spec L144) forms 0.6 m in front of the holder. A bubble emptied before its 4 s end leaves Water 1 at its front; a timed
  end leaves nothing. Puffs are owned clouds (count as the caster's fields, D-149).
- SPL-076: neutral (affects its owner), Water 2 over the 3 m disc for the field's lifetime; wetness, steam, freezing and
  conduction come from the reaction world's own rules (D-036, D-150).
- SPL-077: a framework leap (dash caps, halts on landing) with Water 1 at the launch cell (only if the leap launched) and
  Water 1 at the landing; both in one cell give exactly the "combined source cap Water 2".
- SPL-078: a lane field from the aimed ground point along the aim. "Moving with its arrow" = grounded, standing in the
  stream's liquid water, horizontal speed >= 0.5 m/s, travel within 45 degrees of the arrow. The bonus is a
  `StatKind.MovementBonus` modifier under a per-cast, per-player key, so the 20% movement cap holds.
- SPL-079: root 0.6 s through `ApplyStatus` (immunity refuses it; 2 s immunity after it); the slow starts when the root
  ends, or at once when immunity refused the root. A hard-control spell: the L96 anticipatory cue telegraphs it.
- SPL-080: the impact is the aim's first solid within 18 m (none: the spent cast does nothing). "One ... field" = the
  4-connected hot/venom patch nearest the impact inside the 2 m splash; units nearest first, heat before venom. No water
  is deposited.
- Signature: neutral eddy (owner affected), 12 arcane on first entry per body per cast (never guarded, area contact),
  15% slow re-applied every 0.25 s for 0.3 s while inside; Water 1 over the eddy for its 3 s.
- Passive: `EffectTrigger.CastCommitted` with `MinAmount` 30 on the amount actually paid; the condition requires the
  payment to be the current committed SPL- cast's paid mana, so recast surcharges and other costs never count; refunds
  are gains and never trigger it; 6 s internal cooldown.
- Transformations (stage order L286/L510; rank 2 replaces only the stated numbers):
  - AUG-433: bank once off a wall whose base cell (or the cell in front of the face) holds liquid water; consumes 1 unit
    there; banked payload 24 / 28 with the jet's current push; structural `ricochet`.
  - AUG-434: a shot that strikes a solid without hitting the opponent leaves a mist cloud (owned, counts as a field);
    the push drops to 1 m and, applied after AUG-433 in stage order, also governs the banked shot.
  - AUG-435: a direct hit leaves a puddle (radius, lifetime as stated) under the target instead of the one-cell spray.
  - AUG-436: framework cone (half angle 37.5 / 45 degrees, 3 m reach, per-target cover), 40 / 44 damage, no push,
    +4 mana, +0.1 s windup; structural +cone, -push.
  - AUG-437: recast (spell-recast binding, no payment) throws the remaining shield as a 20 m/s, 20 m physical projectile
    of 24 / 30; shield 35; structural +projectile, damaging, channel:physical, ranged, recast.
  - AUG-438: a thermal break (thermal hit or own heat reaction emptying it) turns the break water into a 1.5 m / 1 s
    (2 m / 1.5 s) steam puff; each heat reaction consumes 15.
  - AUG-439: one rectangular field 8 (10) x 2 m centered on the aimed point, Water 2.
  - AUG-440: two 1.5 (2) m pools, Water 1 each, 3 m dry gap between edges (middle of "2-4 m"), each its own field.
  - AUG-441: the landing deposit becomes a 1 (1.5) m puddle for 3 (4) s; range 5 m.
  - AUG-442: recast while airborne shrinks the remaining horizontal travel by up to 2 (3) m over the same airtime;
    +4 mana; structural +recast.
  - AUG-443: two lanes 30 degrees either side of the aim from one junction, 6 (8) m, +10%, Water 2 divided (1 each).
  - AUG-444: recast (5 mana) reverses the arrow once; rank 2 may instead turn it 90 degrees toward the caster's aim;
    length x0.8 (10 -> 8 m); structural +recast.
  - AUG-445: the bubble rolls on the ground at 16 (20) m/s, climbing at most the step height; bodies whose feet clear
    its top are untouched; damage 22; structural +grounded.
  - AUG-446: after the root ends (or at once when refused) a Water 1 puddle 1 (1.5) m for 2 (3) s at the impact
    instead of the slow; no control of its own.
  - AUG-447: ground cone from the feet, 6 (8) m, framework 90 degree default, one heat/venom unit per cell, +3 mana;
    structural +cone.
  - AUG-448: where heat is neutralized a 1.5 m / 2 s (2 m / 3 s) steam cloud forms; without heat in the plan corrosion
    is untouched.

## Edits outside the owned paths (minimal, additive)
- `src/Confluence.Casting/Machine/CastMachine.cs`: `ExtendRecovery(castId, ticks, now)` (SPL-074 miss recovery).

## Gaps
- (filled in at the end of the session)
