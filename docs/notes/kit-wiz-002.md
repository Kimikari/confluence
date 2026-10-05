# WIZ-002 Bram Cinder kit (Stage 2, task wiz-002-kit)

Bram Cinder, the Kilnheart: crest-release grammar (`Confluence.Casting` `PressureReleaseGrammar`, D-056), signature
Kiln Vent, passive Tempered Hands, eight exclusive spells SPL-009...SPL-016 and sixteen transformations
AUG-305...AUG-320.

## Status / next steps
- Done: every record below carries `[ContentBehavior]` and `[Verifies]` tests (rank 1 and rank 2, drawbacks, caps,
  counterplay where L447 asks for one), all cast through the real grammar from per-tick input in `DuelWorld`.
- Done: WIZ-002 itself (grammar + signature + passive + all eight spells), with two end-to-end duels:
  `tests/Confluence.Sim.Tests/Content/WIZ-002/BramDuelEndToEndTests.cs` (scripted, all eight spells, both
  signatures, both passives, until one Bram dies) and `tests/Confluence.Hosting.Tests/Wiz002/Wiz002BotDuelTests.cs`
  (two Bram bots play a whole hosted Duel match).
- Done: Economy sees the structural profile of every transformation with the shipped registry populated
  (`tests/Confluence.Hosting.Tests/Wiz002/Wiz002StructuralProfileTests.cs`), exclusive groups refused by Economy and
  by the Sim resolver.
- Done: bot casting profile `src/Confluence.Hosting/Bots/Profiles/WIZ-002BramCinderPlanner.cs`.
- Next (outside this task): Hosting's `ImplementedContent.PoolIds` now lists the sixteen WIZ-002 transformations next
  to the shared proof records; the D-010 floors test filters transformations out. Whether wizard transformations
  belong in the implemented pool list at all is a hub decision.

## Finished IDs
WIZ-002, SPL-009 (re-checked on the merged framework), SPL-010 (re-checked; drying test added), SPL-011, SPL-012,
SPL-013, SPL-014, SPL-015, SPL-016, AUG-305, AUG-306, AUG-307, AUG-308, AUG-309, AUG-310, AUG-311, AUG-312, AUG-313,
AUG-314, AUG-315, AUG-316, AUG-317, AUG-318, AUG-319, AUG-320.

## Layout
- `src/Confluence.Sim/Content/Wizards/WIZ-002/`: `BramCinderKit` (WIZ-002: signature form, passive effect,
  `KilnVentDelivery`), one file per spell (`HeatHaze`, `AshCurtain`, `PhoenixRush`, `BoilerOrb`, `FlameLash`,
  `Flashforge`; SPL-009/010 stay in `Content/SpellBehaviors/`), one `*Transforms.cs` per spell with its two
  `ISpellTransform`s, `FireballTransforms`/`CinderSweepTransforms` for AUG-305...308, `HeatStripEntity` (Sweep Wake and
  Phoenix Rush trails), `KitSupport` (internal helpers), `Wiz002StructuralDeltas` (Core registry declarations).
- Tests: `tests/Confluence.Sim.Tests/Content/WIZ-002/` (`BramKit` harness: Bram vs a dummy or a test caster, real
  reaction world and catalogue props), `tests/Confluence.Hosting.Tests/Wiz002/` (Economy profile, bot profile, hosted
  bot duel).

## Structural deltas (D-012)
Registered once in Core `StructuralDeltas` when the transform behaviors are constructed (registry scan):
AUG-307 SPL-010 +field; AUG-310 SPL-011 +cone; AUG-316 SPL-014 +trap +field; AUG-317 SPL-015 -burn. The other twelve
keep their base tags. Exclusive groups come from the catalogue: SPL-009-delivery (305/306), SPL-010-delivery
(307/308), SPL-014-delivery (315/316); the other pairs have no group and may be owned together.

## Bot profile
`IBotSpellPlanner` (new, `Bots/BotSpellPlanning.cs`) lets a wizard choose spells, aim mode (chest / feet / fixed
point), recasts and the signature from a `BotSituation` that holds only legal information (own state, world, own
entities, visible reaction cells, the opponent only as perceived through `DuelWorld.CanAcquire` after the bot's
reaction latency). `BotSpellPlanners.For(wizardId)` discovers planners by reflection; `BotInputSource` consults it
(additive: without a planner the generic slot cycling is unchanged). `BramCinderPlanner`:
- every cast uses `CrestReleaseScript`; the crest is attempted with a per-competence chance (Novice 0, Apprentice
  0.15, Adept 0.5, Expert 0.8 with the default profiles), so normal casts are reliable and mastery is occasional;
- close: Kiln Vent (<= 3.5 m, within 30 degrees of facing), Cinder Sweep (<= 3.6 m), Flashforge (<= 4.5 m; always
  when an intact metal prop is within 2 m of the opponent), Flame Lash (<= 5.8 m; preferred over dry growth);
- mid: Phoenix Rush on a 7.5-10 m gap at >= 50% health; away from a close opponent at < 30% health;
- mid-long: Boiler Orb under the opponent (5-20 m) unless the ground there is wet (water halves the explosion), then
  a recast once the perceived opponent is inside the explosion radius of the settled orb; Fireball 4-28 m;
- defensive: Heat Haze when wet (its heat dries Bram) or occasionally at range; Ash Curtain between the two below
  40% health, never onto wet ground (water collapses ash).

## Interpretations (for the hub's DECISIONS)
- Kiln Vent's cone angle is unstated: the cone primitive's default 90 degrees (Bram's own cone language, SPL-010).
  Its push is hostile displacement, i.e. hard control (L116): it always shows the 0.25 s cue (L96) and control
  immunity blocks the push but not the damage. It is a direct cone hit a frontal guard halves (L104). "Heat strength
  1 to affected world props" = one instant heat unit on the cells under each prop body inside the cone; the cone
  itself deposits nothing and creates no field (the steam a heated wet prop cell makes is an ordinary reaction).
- Signatures have no grammar puzzle (L472), so Kiln Vent never takes a crest reward.
- Tempered Hands: a DamageTaken effect filtered to the thermal channel with an 8 s internal cooldown; the shield
  follows the triggering hit (never absorbs it), lasts 2 s, shares the 100 cap; Bram's own thermal damage does not
  charge it; reaction/secondary damage raises no triggers (L126, L672).
- Heat Haze's 12% applies to spell projectile damage only and joins the 45% passive cap; it dries the caster's Wet
  once at release.
- Ash Curtain is a reaction-world ash volume counted as one of the caster's fields; liquid water under any part
  consumes it; wind moves it by the global wind rule.
- Phoenix Rush: direction fixed at commitment and telegraphed as a path marker; the endpoint burst is unguarded area
  damage carrying the cast's mastery result; the trail is one neutral field (owner burns too).
- Boiler Orb: a deployable (no field allocation); water under the resting orb steams its Heat 3 at once and drops
  the explosion to 16; shot down at 25 health it never explodes. Boiler Mine (AUG-316) becomes a trap/field.
- Flame Lash: offensive melee spell (guard reduces, D-135); growth ignites at two heat units (L153), so the lash's
  Heat 1 ignites growth that already holds a heat unit ("dry growth").
- Flashforge: the nearest intact catalogue-tagged metal prop in the area turns red-hot (one field, owner included);
  destroying it or water/frost under it ends the heat early.
- AUG-310 Haze Reservoir and AUG-320 Forge Vacuum cone/pull angles unstated: cone default 90 degrees; the vacuum pulls
  loose steam at the wind speed cap (3 m/s, L151).
- AUG-318 Lash Steam "wind-clear": a strength-2 gust disperses the touched front 2 m slice (D-023), i.e. the whole
  1-1.5 m strip.

## Cross-edits (shared files outside the owned paths)
- `src/Confluence.Sim/Spells/Runtime/CastRecord.cs` (+`Choice`), `SpellScript.cs` (+`OnCommitted`),
  `SpellRuntime.cs` (calls `OnCommitted`, registers the listener forwarder), new `SpellEventListeners.cs`
  (`ISpellEventListener`), `src/Confluence.Sim/Spells/SpellEvents.cs` (six appended `SpellEventKind` values).
- `src/Confluence.Hosting/Bots/BotInputSource.cs` (planner hook), new `src/Confluence.Hosting/Bots/BotSpellPlanning.cs`.
- Tests: `tests/Confluence.Sim.Tests/Content/ContentRegistryTests.cs`, `tests/Confluence.Sim.Tests/Spells/ProjectileTests.cs`
  (expectations for an implemented WIZ-002 kit), `tests/Confluence.Hosting.Tests/EconomyAdapterTests.cs` (pool ids
  minus wizard transformations), new `tests/Confluence.Hosting.Tests/Wiz002/`.
