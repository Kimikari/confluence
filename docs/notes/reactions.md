# Confluence.Reactions — design notes

Authoritative physical world state of one duel (spec §4 L130-L175, arena materials L654-L688). Netstandard2.1, C# 9,
depends only on Confluence.Core.

## Status / next steps
- Engine, snapshot, and environment hooks compile. Tests in `tests/Confluence.Reactions.Tests` (see below).
- Next: Sim wiring (queue ops from spell behaviors, pass bodies, apply ContactDamage/PropImpulse, count VolumeEvents
  against the three-field cap and call `RetireVolume(id, FieldLimit)`).

## Shape of the API
- `ReactionWorld(ArenaLayout layout, int combatStartTick)` builds the grid (square around the arena diameter, 0.5 m
  cells, index = z * Width + x), loads `layout.Materials` as natural permanent units, registers metal/conductive solids
  and patches as conductors (ids `SolidConductorIdBase + solid.Id`, `PatchConductorIdBase + patchIndex`), reflective
  solids as prisms, light shafts, and the biome rule (`EnvironmentRules.Install`).
- Queue per tick: `Deposit`, `Impact`, `Discharge`, `Wind`, `ApplyForce`, `Dispel`, `CreateVolume`, `Condense`,
  `RemoveUnits`, `RaiseDust`, `AddHotSurface`, `ClearVolumes`. Then `Step(tick, bodies)` once.
- `Step` runs `GlobalRules.World.ReactionOrder` (L165) then body contact; returns `StepResult` (events, ContactDamage,
  PropImpulse, VolumeEvents). Reactions never apply damage.
- Queries: `UnitsAt`, `IsWetAt`, `SurfaceAt`, `IceDurabilityAt`, `IsChargedAt`, `IsBurningAt`, `DustProductionAt`,
  `SightPathLength`, `BlocksAcquisition`, `Volumes`, `GetOwnedFieldVolumes`, `Prisms.Trace`, `GravityFields.TryDeflect`,
  `Sound.Trace`, `Regions`, `Ledger`.
- `CaptureSnapshot()` / `RestoreSnapshot()` / `StateHash()`.

## Key interpretations (to consolidate into DECISIONS.md)
See the final report of the Reactions session; each is also commented at the code site with its spec line.
