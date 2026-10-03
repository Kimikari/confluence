# Sim core (Confluence.Sim) — design notes

Owner paths: `src/Confluence.Sim/**`, `tests/Confluence.Sim.Tests/**`. Spells, reactions and casting plug in later
through seams; this stage has no dependency on `Confluence.Reactions` or `Confluence.Casting` types.

## Layout
- `Input/PlayerInput.cs` — the `PlayerInput` command. Its buttons use the one shared flag enum
  `Confluence.Casting.InputButtons` (unified in Stage 1b; Sim files alias it).
- `SimRules.cs` — numbers the spec leaves open that only the Sim needs (each cites the spec line it interprets).
- `Hashing/StateHasher.cs` — order-dependent 64-bit hash of authoritative state.
- `World/` — `CollisionWorld` (static layout solids + dynamic temporary solids with durability), queries
  (raycast, sphere sweep, capsule overlap, sight), `IWorldMaterials` hook (step 5) with `NullWorldMaterials`.
- `Movement/` — `CharacterMotor` pure step function shared with client prediction (`MotorState`, `MotorCommand`,
  `MotorReport`).
- `Players/` — `DuelPlayerSetup`, `ResourcePools`, `StatSheet` (raw vs effective capped values), `DuelPlayer`.
- `Combat/` — `DamageRequest`, `DamagePipeline` (spec L112 order), `ShieldPool`, `HealingBudget`, `GuardState`,
  `MeleeState`/`MeleeProfiles`, `ProcBudget`, `ChainTargeting`.
- `Status/` — `StatusRules` registry, `StatusSet` (hard-control cap and immunity, slows, DoT refresh, dispel).
- `Fields/` — `OwnedEntityRegistry` (fields <= 3, summons <= 2, lifetime <= 8 s, oldest retirement).
- `Duel/` — `DuelWorld` (tick order), events, `BoundaryTracker`, `FeatTracker`, `HitVolumeHistory`
  (lag compensation), `ISpellRuntime` seam, `DecisiveSequence`, duel result.

## Tick order (docs/ARCHITECTURE.md)
1. Inputs sanitized; pressed edges; slot selection (no swap windup); focus swap requests; surrender.
2. Motor per player (walk/sprint/jump/crouch/dodge, displacement and dashes clamped to the boundary, alcove
   confinement); fall damage, recall, edge impacts; hit-volume history recorded.
3. Guard, melee (strikes resolve here), swap progression; `ISpellRuntime.UpdateCasting` per player.
4. `ISpellRuntime.AdvanceDeliveries`.
5. `IWorldMaterials.Step` (reactions); queued contact damage applied in request order.
6. Fields, summons, temporary solids lifetimes; field contact behaviors.
7. Statuses and DoT, resource regeneration, shield expiry.
8. Boundary damage (bypasses all mitigation; triggers nothing).
9. Death, simultaneous-death draw, timer judgement.

## Seams for the next stage
- `ISpellRuntime` (DuelWorldOptions.SpellRuntime): `Attach`, `UpdateCasting(player, input, CastPermissions)` at
  step 3, `AdvanceDeliveries()` at step 4, `GetStatus(player)` -> `CastingStatus` (movement factor, channeling,
  busy, recovery), `Interrupt(player, cause)` (true = a committed cast was interrupted, feeds FEAT-019), `Hash`.
  Everything a spell does goes through public `DuelWorld` APIs: `ApplyDamage`, `Heal`, `GrantShield`,
  `ApplyStatus`, `Dispel`, `ApplyKnockback`, `TryDash`, `TryTeleport`, `CreateOwnedEntity`, `DamageSummon`,
  `DispelOwnedEntity`, `AddTemporarySolid` (-1 = refund, spec L171), `DamageSolid`, `QueueDeposit`,
  `TryCommitMana`, `RefundMana` (cap with `StatSheet.CapRefund`), `RecordMasteryCast`, `RecordConsumableUse`,
  `NotifyOffensiveAction` (breaks invisibility), `RaycastOpponent` (lag-compensated), `CanAcquire`, `Procs`.
- `IWorldMaterials` (DuelWorldOptions.Materials): `QueueDeposit`, `Step(IMaterialsContext)` at step 5
  (`RequestContactDamage`, `RecordReaction`, `RecordCloudCleared`), `ObscuringPathLength` (>= 2 m blocks
  acquisition, L167), `QuerySurface` (ice drift, sticky slow), `Hash`. Default `NullWorldMaterials`.
- `PlayerInput` lives in `Input/PlayerInput.cs`; the flags are `Confluence.Casting.InputButtons` (bits 0-18 fixed,
  `SwapFocus` = bit 18 for the L106 equip swap). `ViewTick` drives lag compensation.

## Interpretations (for DECISIONS consolidation; spec line refs)
- Motor numbers left open live in `SimRules.Motor` (L80, L175, L312): 30.5 degree walkable tolerance, ground snap =
  step height, sprint needs forward input, ice acceleration factor 0.2 (traction lerps back to 1), kill plane 6 m
  below the lowest walkable top, terminal fall speed 30 m/s.
- Fall height is measured between ground contact points (peak support height to landing support), so rolling off
  a ledge does not shave the drop; step-up checks the touched surface height (L175, L312).
- Fall damage is physical (L175): resistances/shields apply, guard does not. Recall penalty and edge impact are
  rule damage (bypass mitigation, trigger nothing; L51, L175).
- Hostile knockback is the hard-control status `Displaced` (L116): at most 0.75 s, blocked by control immunity
  (damage still lands), interrupts casts, not dispellable. Default knockback speed 12 m/s (4 m in 1/3 s).
- Hard control shares one continuous 0.75 s window across overlapping sources; immunity starts when the last hard
  control ends (L116).
- Edge impact (5) only for hostile knockback; self dashes stop at the edge without damage (L51).
- Melee (L100-L102): strike volume live for 3 ticks after the windup, 3 samples per tick, origin 0.35 m below the
  eye; thrusts (butt strike, spear) straight, light blades +-35 degrees, heavy +-45 degrees around the committed aim;
  reach is from the attacker's center line to the target capsule; solids contacted once per attack take the
  profile damage as durability; a stun cancels a strike still winding up. Weapon tags heavy/spear/light_blade pick
  the profile; an empty slot is the issued plain focus.
- Guard release window (L104) blocks dodge, melee, and casting for 0.25 s. Perfect window = 7 ticks.
- Healing "per second" is a rolling 60-tick window; lifesteal also counts toward the 35/s window (L114).
- Compatible shield capacity = same source id and reserve flag (L114).
- DoT pulses every 0.5 s (`SimRules.Status.DotIntervalSec`); refresh keeps max expiry and max intensity (L118).
- Surface slows (sticky growth) join the strongest-slow rule and its 30% cap (L116, L175).
- Proc budget requires both the 0.5 s interval and an unused (origin hit, target) pair (L126).
- Boundary escalation resets after 60 consecutive ticks inside; FEAT-024 counts a reset after boundary damage once
  contraction has started (L51, L278).
- Timer draw threshold 0.005 on health fractions with each player's own maximum (L53); both surrendering = draw.
- Alcove (L46): movement confined to the alcove footprint; guard/melee/swap/damage/status/mana commits refused;
  jump and dodge allowed (stamina spent there is not restored again at combat start).
- Wall formed under a player (L171): raised onto its top if the lift is <= 0.5 m and headroom is clear; otherwise
  `AddTemporarySolid` fails (-1) so the caller refunds.
- Decisive sequence = last 5 s of events relevant to the player (L359).
- Feats: FEAT-005 counts self-propelled horizontal movement during combat (not knockback); FEAT-008 any original
  hit within 1 s of a dodge start; FEAT-022 a spell hit followed by a melee hit within 1 s (each spell hit once);
  FEAT-023 a player dispelling their own hostile statuses; FEAT-019 stun/silence/displacement that interrupts.

## Status / next steps
- Done: all ten components (157 tests at Stage 1). Stage 1b added the spell runtime (`docs/notes/sim-spells.md`) and
  the world, props, stats and effects (`docs/notes/sim-world.md`); after the merge the project has 344 tests.
- Feats not yet fed by any producer (counters and hooks exist): FEAT-002, 006, 009, 011, 013, 014, 019, 020, 021,
  022, 023 (see `FeatTracker.AwaitingLaterStages`); FEAT-015 needs summon creators.
- Next stage: implement `ISpellRuntime` over Confluence.Casting, `IWorldMaterials` over Confluence.Reactions,
  unify input flags, add props (moving-prop collision damage L173 with `DamageKind.Prop`), and feed the feats above.
