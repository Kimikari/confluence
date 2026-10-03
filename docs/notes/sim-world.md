# Sim world, props, stats, effects (Confluence.Sim) — design notes

Stage 1b task "sim-world-effects". Owner paths: `src/Confluence.Sim/World/Materials/**`, `src/Confluence.Sim/Effects/**`,
`src/Confluence.Sim/Stats/**`, `src/Confluence.Sim/Props/**`, `src/Confluence.Reactions/**`,
`tests/Confluence.Sim.Tests/{World,Effects,Stats,Props}/**`, this file. The concurrent agent "sim-casting-spells" owns
`src/Confluence.Sim/{Casting,Spells,Content,Input}/**` and Confluence.Casting.

## Status / next steps
- Done (Stage 1b, merged 2026-10-03): everything in the task. Verified records: EQP-051, EQP-096, EQP-066, AUG-145,
  AUG-169, AUG-175, BIO-001, BIO-011 (all `[ContentBehavior]` in Confluence.Sim with `[Verifies]` tests in a real
  `DuelWorld`; the six proof records are also verified end to end through Hosting's `LoadoutMapper`).
- Merged with the spell runtime (see "Binding to Confluence.Sim.Content"): the spell runtime pays commitments through
  `Effects.TryCommitPricedCast` (next-cast bonuses priced by `PriceCast`), raises `CastReleased`, and effect refunds
  of a commitment share the cast machine's refund ledger; `QueueDeposit` reads the declared footprint, lifetime,
  field id and the heavy-projectile ring; projectile impacts reach Reactions through `IWorldMaterials.QueueImpact`
  (struck ice and brittle cover shatter, combustible solids take impact heat).
- Open: reaction snapshots do not include the Sim-side links (cloud fields, source ids, cleared-cloud feat keys) —
  re-derive or serialize them before Net restores a `ReactionWorld`; prop conductors/prisms (metal and reflective
  props) are registered by prop behaviors (IPropBehavior, later); props never fall or rotate; nothing consumes dash
  charges yet (the stat is capped at 2); `ReactionWorld.StateHash()` captures a full snapshot per call (fine for tests
  and periodic checks, too heavy for a per-tick server hash if that is ever wanted); only projectile deliveries report
  impacts (cone, arc, wave and ray impacts on ice or brittle cover are not reported yet).

## Layout
- `World/Materials/ReactionMaterials.cs` — `IWorldMaterials` + `IMaterialsBinding` over `ReactionWorld`; the default
  materials of every `DuelWorld`. `SurfaceMapping.cs` (Reactions surface -> motor surface), `MaterialRules.cs` (open
  numbers, reaction tags, FEAT-006/011 families). The biome behaviors (BIO-001, BIO-011) moved to
  `Content/Biomes/BiomeRuleBehaviors.cs` at the merge.
- `Props/` — `PropBody`, `PropSystem` (step 6), `PropEvent`, `PropRules`.
- `Stats/StatBlock.cs` — raw/effective values and caps (`Players.StatSheet` derives from it so existing callers keep
  working; `StatKind`, `StatModifier`, `StatLine` stay in `Players/StatSheet.cs`).
- `Effects/` — `EffectTrigger` (+ `TriggerEvent`, `EffectFilter`), `EffectConditions`, `EffectActions`, `Effect`
  (+ `ConditionalModifier`), `CastPricing`, `EffectHost` (per player, implements `Content.IEffectHost`),
  `EffectSystem` (per duel), `EffectEvent`. The proof records live in `Content/Augments/SilverAugments.cs` and
  `Content/Equipment/TierOneEquipment.cs`.

## DuelWorld wiring (shared file, additive)
- Options: `Content` (`ContentRegistry`, default `ContentRegistry.Default`), `Catalogue` (props, biomes).
  Default materials: `new ReactionMaterials()`.
- Constructor: players -> `PropSystem.FromLayout` -> `EffectSystem` + `AttachLoadouts` (modifiers before the alcove
  restores pools) -> alcove placement -> `IMaterialsBinding.Attach` -> `Effects.BeginRound()` (round-start triggers)
  -> spell runtime attach.
- Step: `Effects.CarryOver()` before the tick's events are cleared; `Props.Step()` after step 6 owned entities;
  `Effects.Dispatch()` at the end of step 7. `StateHash` folds `Props` and `Effects` (materials already hashed:
  `ReactionWorld.StateHash()` plus the cloud links).
- `ApplyDamage` calls `Effects.ModifyOutgoing` before the pipeline; `DamageSolid` reports to `Props.OnSolidDamaged`;
  `TryDash`/`TryTeleport` read the stat block's distance bonuses; new `RecordEvent<T>` and `RemoveOwnedEntity`.
- Fix (sim-core bug): DoT pulse damage cleared the shared status scratch list, so a status that expired on a pulse
  tick never emitted its Expired event; expiry now uses its own list.
- Also shared: `CollisionWorld.MoveSolid` + `CollisionSolid.MoveTo` (moving props); `DuelEventType.Prop/Effect`.

## Binding to Confluence.Sim.Content (resolved at the merge)
- One set of behavior contracts in `Content/ContentBehaviors.cs`: `ISpellBehavior`, `ISpellTransform`,
  `IWizardKitBehavior`, `IAugmentBehavior`, `IEquipmentBehavior`, `IBiomeRuleBehavior`, `IPropBehavior`, plus the
  attachment surface `IEffectHost` and `ISimEffect`. `Effects.EffectHost` implements `IEffectHost`; `Effect` and
  `ConditionalModifier` implement `ISimEffect`. `Effects/ContentContracts.cs` was deleted.
- One registry: `Content.ContentRegistry` (reflection scan; `Default`, `Empty`, `FromBehaviors` for explicit test
  instances). `ContentBehaviorIndex` and `IEffectContentSource` were deleted; `DuelWorldOptions.Content` and
  `EffectSystem.AttachLoadouts` take the registry, which also attaches the wizard passive
  (`IWizardKitBehavior.AttachPassive`) before equipment and augments.

## Interpretations (for DECISIONS consolidation; spec line refs)
Reactions in the duel
- Reaction contact damage is `DamageKind.Environment` (generation 0) with the reaction owner as attacker for
  attribution and feats only; it never triggers on-hit effects, lifesteal, procs, or hit triggers (L126, L672). Its
  source id is the originating spell when the caller recorded it (`RecordSource`), else `REACTION:<kind>`. The owner is
  not exempt (L670).
- Player clouds that count as fields become owned field entities `CLOUD:<VolumeKind>` (neutral, 8 s cap; the volume's
  own lifetime ends them first). Spell fields and clouds share the three-field cap; the oldest of either retires; a
  retired cloud entity retires its volume (`FieldLimit`); a cloud that ends in the reaction world removes its entity
  (Expired / Dispelled event). If no field is retirable the new cloud is retired at once.
- Wet from cells (L118, L175): a grounded player whose feet are within the step height (0.35 m) of a liquid-water cell's
  surface gets `Wet` (`RULE:WET-CELL`, no source player); it lingers 1–1.25 s after leaving (re-applied 4 times a
  second while standing). Ice is not wet. Rain never wets anyone directly (BIO-001).
- Surfaces for the motor (L175), by flags not by the Reactions display kind: ice > sticky growth (declared slow, capped
  by the strongest-slow rule at 30%) > damp sand > wet stone (footsteps only) > normal. Burning, charged, venom cells are
  contact hazards only. A charged ice tile is ice. Feet more than 0.35 m from the cell surface (platforms) are normal.
- Obscuring path length reports at least 2 m whenever the reaction world says the path blocks (its 1e-4 tolerance), so
  `DuelWorld.HasClearSight` and `ReactionWorld.BlocksAcquisition` always agree (L167). Steam never blocks raycasts.
- Generic `WorldDeposit` translation: material kinds -> deposits (strength clamped 1..4, instant residue); electricity ->
  discharge without declared reaction damage; wind -> gust (circle area); force -> push every prop whose center is in
  the footprint (along the direction or away from the center); arcane -> dispel; light, shadow, sound, gravity carry no
  cell units.
- FEAT-006 families: steam, freeze, melt, shatter, wet-charge, conduct, ground, wind (translate/disperse/clear),
  extinguish, ignite, damp-sand, prop-force, dilute, condense, dispel. FEAT-011 counts each hostile or neutral cloud once
  per clearing player (disperse, clear, dispel, condense events).
- Biomes: the Sim behaviors (BIO-001 `RaincourtRainRule`, BIO-011 `WindmillYardGustRule`) install the rule into the
  duel's reaction world, built with `installEnvironment: false`; a biome without a behavior gets its announced gusts
  under the global wind rule (strength 1 moves no cloud). The Reactions classes `RaincourtRain`/`WindmillYardGusts` hold
  the rule data and no longer carry `[ContentBehavior]` (static classes cannot satisfy a behavior registry).

Props (L155, L171-L173, L662)
- Mass class and durability from the catalogue record; without a record a prop is anchored and indestructible.
  `PropPlacement.HasCollision` false: no body (it can still take durability damage through `PropSystem.Damage`).
- A body is a dynamic `Cover` solid (blocks movement and sight, walkable top) with the prop's durability, so melee and
  spells damage it through `DamageSolid`; destruction removes collision in the same update and emits
  `PropEvent.Destroyed`.
- Pushes come from the reaction world's clamped impulse (distance by class 4/2/0.5 m, speed <= 8 m/s). A pushed prop
  slides at that constant speed on its support; it stops when the distance is spent, at a solid (box overlap with
  0.02 m resting tolerance), at an unsupported edge (support within 0.35 m below its center — props never fall), or on
  reaching a player. A new push replaces the current motion. Anchored or bodiless props only shake.
- Collision damage: 5/15/25 by class when the relative speed along the push (prop speed minus the player's velocity
  component) is at least 3 m/s (D-024 threshold), at most once per second per prop and player, `DamageKind.Prop`,
  physical, attacker = the pushing player (no exemption for the owner, no credit for self-hits), never lifesteal or item
  effects; FEAT-021 counts it.

Stats (L78-L124, L175, L355, L579)
- Every stat is additive in its own unit; effective values apply the caps; `InspectAll` gives base, raw, effective,
  cap and a capped flag for every stat (the -15% resistance floor counts as capped).
- Walk, sprint and all-gait bonuses share the +20% cap per gait. Max health 300 + bonuses <= 450; max mana/stamina have
  no cap (static capacity, L579). Mana regeneration is the 10/s base times (1 + percent) plus flat, at most 14/s
  ("within +40% cap" in EQP-066 is this ceiling). Dodge distance never exceeds the 8 m dash cap; dodge and guard
  cooldowns never fall below 0.5 s; the 0.8 s guard hold is a ceiling (modifiers only shorten it, to at least the 0.12 s
  perfect window). Melee modifiers change the active profile (windup >= 1 tick, cycle >= windup + live strike).
- Outgoing damage stats (spell, weapon) multiply original spell/melee hits before the pipeline; form-specific spell
  modifiers belong to the spell form pipeline, not here.

Effects (L126, L433, L573-L582)
- One dispatch per tick at the end of step 7, fixed order: carried-over events of the previous tick, this tick's duel
  events in emission order, spell-runtime raises in call order, polled changes per player (sprint, selection, melee
  miss, surface, volume), periodic. Gameplay triggers fire only in combat; RoundStart fires once at alcove placement
  (duel construction), CombatStart when the alcoves open. Conditional modifiers are re-evaluated at the same point (one
  tick latency).
- Hit triggers come only from generation-0 Direct/Splash/Melee damage (DirectHitDealt/Taken: Direct and Melee only);
  DamageTaken also from status pulses and falls; nothing from environment, prop, boundary, recall, or secondary damage.
  A split hit is one trigger carrying every channel.
- Every activation passes the proc budget (0.5 s and once per original hit event, L126), keyed by the effect's budget
  group (default: the attached effect); non-hit triggers use their event id (or a synthetic id) as origin, so only the
  interval limits them. Effects never fire on what effects produce (the one secondary generation).
- Stacking: Refresh (one instance, duration restarts), Stack (up to MaxStacks, at the maximum the oldest is
  refreshed), Replace, KeepExisting. Pending next-cast/next-hit bonuses: Refresh/Replace keep the newest per effect,
  Stack keeps up to MaxStacks, KeepExisting ignores while pending.
- Reserve shields only from RoundStart effects (L114, L579); triggered shields are clamped to 8 s by the shield pool.
- Cast price (L124): stat cost reduction + pending bonus reductions together at most 30% of the base cost; drawback
  increases add after the floor. A paid cast consumes every active matching next-cast bonus. Refunds: at most 25% of
  the recorded payment (L90, L124), keyed by the payment's resource event id; for a spell commitment the cast machine's
  refund ledger decides (one shared 25% cap with interrupt refunds, each refund effect once per cast). The machine
  prices commitments: `PlayerCaster.ModifiersFor` turns `PriceCast` into one reduction fraction (floored at 70% with
  mastery efficiency) plus a flat `CastModifiers.CostIncrease` added after the floor.
- Next-hit bonuses multiply the raw parts (with the damage stats) and add flat damage to the first part; consumed by the
  first matching original hit; secondary damage neither uses nor consumes them.
- Rank 2 of a shared augment replaces the rank 1 amount; drawbacks apply at both ranks.
</content>
