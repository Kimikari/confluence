# WIZ-010 Halen Tide kit — design notes

Owner paths: `src/Confluence.Sim/Content/Wizards/WIZ-010/**`, `tests/Confluence.Sim.Tests/Content/WIZ-010/**`,
`src/Confluence.Hosting/Bots/Profiles/WIZ-010*`, this file.

## Status / next steps
- Complete. The eight spells (SPL-073..080), the signature (Undertow Mantle) and the passive (Pressure Reserve) in
  `HalenTideKit`, all sixteen transformations (AUG-433..448) with structural deltas (`Transforms/HalenStructure.cs`),
  and the WIZ-010 bot casting profile. Every ID below carries `[ContentBehavior]` and `[Verifies]` tests (rank 1 and
  rank 2, drawback, caps, counterplay), cast through WIZ-010's real pressure-metering grammar in a DuelWorld.
- Finished IDs: WIZ-010, SPL-073, SPL-074, SPL-075, SPL-076, SPL-077, SPL-078, SPL-079, SPL-080, AUG-433, AUG-434,
  AUG-435, AUG-436, AUG-437, AUG-438, AUG-439, AUG-440, AUG-441, AUG-442, AUG-443, AUG-444, AUG-445, AUG-446, AUG-447,
  AUG-448.
- End to end: `HalenEndToEndTests` (scripted Halen vs Bram in a DuelWorld: whole default kit, mastered jet, signature,
  passive, pool + Fireball steam, Fireball on the bubble, stream walk, vault, hammer, Halen wins; a second loadout seals
  a hot field and imprisons Bram). `tests/Confluence.Hosting.Tests/WIZ-010/`: Economy structural profile with the
  registry populated (all 16, plus a real eligibility change from AUG-437 and AUG-436), bot scripts for every spell
  and the signature through the grammar, planner choices, and hosted Halen-vs-Halen bot duels on MatchHost.
- Next steps (optional): the generic `BotInputSource` movement keeps bots rushing / out of sight much of a duel, so
  hosted bots cast Halen's mobility spells (vault, stream) rarely; a movement-aware planner hook could improve that.

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
- AUG-445 rolling: the wall check sweeps level at the higher of the current and next ground height, so a rise within
  the step height is stepped onto and a drop is rolled off; a face the bubble is leaving (normal along its travel)
  never stops it. A 1 m crate stops it (counterplay "block rolling route"); a flying bubble passes over the crate.
- AUG-441: the landing puddle is measured from the landing tick; the launch water keeps its own 3 s.
- AUG-443: the straight 10 m line of the unforked stream is dry; each lane is its own field; a body gets +10% from at
  most one lane.

## Bot casting profile (`src/Confluence.Hosting/Bots/Profiles/WIZ-010HalenTideBotPlanner.cs`)
- `HalenTideBotPlanner : IBotKitPlanner` (found by reflection through the new `Bots/BotKitPlanner.cs`). Choices, in
  order: Bubble Buckler against a seen opponent's visible windup at 4 m+ (AUG-437: recast 0.75 s later throws it back);
  Seal the Spring on visible heat/venom within 6 m of the feet; Water Pool on heat near the opponent (steam screen);
  signature within 2.2 m; Water Hammer within 3.2 m; Deepglass Prison 5-17 m (60%); Pressure Jet 2.5-19 m; Riptide Lane
  toward the opponent at 9-20 m; Cascade Vault toward them at 8 m+; Water Pool at their feet. While rushing only the
  eddy and the hammer. Mana margin 2 over the cost.
- Legal information only (`BotKitView`): the opponent's position, velocity and visible windup as perceived through the
  Sim's sight query one reaction latency ago; own resources and cooldowns; visible ground materials (with an eye-ray
  visibility check for ground targets).
- Scripts: `PressureMeterScript` (the grammar judges). Mastery attempts by competence: Novice 0, Apprentice 15%,
  Adept 50%, Expert 75%; the timing jitter still makes some attempts cast normally.

## Edits outside the owned paths (minimal, additive)
- `src/Confluence.Casting/Machine/CastMachine.cs`: `ExtendRecovery(castId, ticks, now)` (SPL-074 miss recovery).
- NEW `src/Confluence.Hosting/Bots/BotKitPlanner.cs`: `IBotKitPlanner`, `BotCastPlan`, `BotKitView`, `BotAimKind`,
  `BotKitPlanners` (reflection registry, one planner per wizard id), `SignatureScript`.
- `src/Confluence.Hosting/Bots/BotInputSource.cs`: when the wizard has a kit planner, the planner chooses slot, aim
  (chest / opponent feet / point) and an optional recast; the Observation records a visible windup; rushes no longer
  block planner casts (the planner restricts itself to close range). Wizards without a planner behave as before.
- `tests/Confluence.Hosting.Tests/EconomyAdapterTests.cs`: the floor test ignores wizard-kit transformations when it
  compares the implemented pools with the shared proof records (every kit branch adds its transformations to the pools).
- NEW `tests/Confluence.Hosting.Tests/WIZ-010/` (Economy and bot tests; Sim.Tests does not reference Economy).

## Gaps
- None for the WIZ-010 records. Note for the hub: SPL-009 Fireball's Heat 2 is consumed at impact (no persistent hot
  ground), so the Seal the Spring end-to-end duel places its hot field directly in the reaction world.
