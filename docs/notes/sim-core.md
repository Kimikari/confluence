# Sim core (Confluence.Sim) — design notes

Owner paths: `src/Confluence.Sim/**`, `tests/Confluence.Sim.Tests/**`. Spells, reactions and casting plug in later
through seams; this stage has no dependency on `Confluence.Reactions` or `Confluence.Casting` types.

## Layout
- `Input/PlayerInput.cs` — `InputButtons` flags and the `PlayerInput` command (one small file; the casting stage
  defines its own flags and the next stage unifies them).
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

## Status / next steps
See the bottom of this file (kept current by each session).

### Status
- Session 1: all components written and compiling (commits 1128728, 8749069).
- Session 2: motor fixes (fall height measured between support contact points; step-up checks the touched
  surface height so a 0.5 m block cannot pass as a step); motor tests pass. Writing tests for the remaining
  components (world queries, resources, damage pipeline, healing, statuses, guard, melee, fields, procs, stats,
  boundary, duel flow, determinism, lag compensation, feats).
- Next: finish tests, fix what they reveal, final commit and report.
