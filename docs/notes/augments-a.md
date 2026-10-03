# augments-a: shared augments of six families

Families: Casting mastery, Projectile and beam shaping, Fieldcraft and terrain, Melee and guard, Movement arts, World
reactions (144 records with empty `target_spell_ids`). Code: `src/Confluence.Sim/Content/Augments/augments-a/`; tests:
`tests/Confluence.Sim.Tests/Content/Augments/augments-a/`.

## Status / next steps

- Done with tests (ContentBehavior + Verifies): Melee and guard (AUG-073..078, 080, 083..087, 089, 090, 092),
  Movement arts (AUG-097..102, 104, 107..110, 115), Casting mastery (AUG-001, 005, 009, 012, 013, 017..020).
- Projectile and beam shaping: code for AUG-025..028, 031, 038, 039, 041..044 in ProjectileShaping.cs; tests in progress.
- Next: remaining unconditional records (AUG-059, 062, 067, 105), then Bram/Halen-satisfiable conditionals
  (field, Heat/Water reactions, projectile with movement/terrain equipment), then the floor-count test.

## Framework additions (shared files, additive)

- `Effects/EffectHooks.cs`, `EffectHost.Hooks.cs`, `EffectSystem.Hooks.cs` (new): `EffectHook`, a stateful per-player
  content hook (OnEvent records facts; OnTick acts through `TryActivate` and the proc budget; pure adjust points for the
  motor command, strike profile, cast price, mastery-dependent price, cast timing, projectile launch speed, outgoing
  hits, and spell durability damage to solids).
- `Content/AugmentFormStages.cs` (new): `IAugmentFormStage`, a shared augment's edit of each equipped spell form after
  transformations, before caps (spec L543), ascending id order.
- Casting `CastModifiers`/`MasteryClamp`: mastered-only and normal-only cost reductions; windup and recovery timing
  modifiers (`TimedWindupTicks`, `TimedRecoveryTicks`); the cast machine stores the committed recovery.
- One-line hook calls: DuelWorld (motor command, strike begin), EffectSystem (price, outgoing), PlayerCaster
  (modifiers), ProjectileDelivery (launch speed), SpellContext (solid damage), SpellFormResolver/SpellLoadout
  (form stages), ResourcePools.LoseStamina.

## Interpretations

- "Walking" = grounded, not sprinting, not displaced, horizontal speed at least 1 m/s (AugmentsAKit.WalkingMinSpeed).
- Health thresholds ("below 90 health") are absolute points.
- Conditions completing after commitment (e.g. "stays grounded through release") return the decrease through the
  cast's refund ledger (25% cap, once).
- AUG-039 "landing recovery": the base rules give landing no recovery; the first 0.25 s after landing (L104 window).
