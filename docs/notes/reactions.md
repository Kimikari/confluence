# Confluence.Reactions — design notes

Authoritative physical world state of one duel (spec §4 L130-L175, arena materials L654-L688). netstandard2.1,
C# 9, depends only on Confluence.Core. Tests: `tests/Confluence.Reactions.Tests` (89 tests).

## Status / next steps
- Complete for Stage 1: every core reaction row (L142-L161), deposits and footprints (L163), fixed phase order
  (L165), sight path length (L167), volumes with owner events, body contact requests, queries, conductor and
  scheduled-source APIs, BIO-001 rain, snapshot/restore/hash. `dotnet test tests/Confluence.Reactions.Tests` passes.
- Next (Sim wiring): queue operations from spell world-interaction behaviors; pass bodies each tick; apply
  `ContactDamage` through the damage pipeline as secondary hits; apply `PropImpulse` to props; count `VolumeEvents`
  (Created/Retired with `CountsAsField`) against the three-field cap and call `RetireVolume(id, FieldLimit)` for the
  oldest; register props as conductors/prisms/gravity fields; feed `SurfaceAt` to the motor and footsteps.
- Not done (see "Gaps" below): porous walls (BIO-005, MOD-021, MOD-046), heat sinks (BIO-020), basins (BIO-024),
  multi-level cells.

## Shape of the API
- `ReactionWorld(ArenaLayout layout, int combatStartTick)` builds the grid (square around the arena diameter,
  0.5 m cells, index = z * Width + x), loads `layout.Materials` as natural permanent units (`NaturalSource` cells
  flagged, ice-flagged patches start as map tiles), registers metal/conductive solids and patches as conductors
  (ids `SolidConductorIdBase + solid.Id`, `PatchConductorIdBase + patchIndex`; insulating ones are skipped),
  reflective solids as prisms, light shafts, and the biome rule (`EnvironmentRules.Install`: BIO-001 rain sources,
  layout gust schedule with the BIO-011 strength-1 override). `ReactionWorld(CellGrid)` gives an empty world.
- Queue per tick (applied at the next `Step`, sorted canonically by server event id then content, never call
  order): `Deposit`, `Impact`, `Discharge`, `Wind`, `ApplyForce`, `Dispel`, `CreateVolume`, `Condense`,
  `RemoveUnits`, `RaiseDust`, `AddHotSurface`, `ClearVolumes`. Immediate: `RegisterConductor`, `RemoveConductor`,
  `SetConductorGrounding`, `AddScheduledSource`, `AddScheduledGust`, `AddLightShaft`, `RemoveSolid`,
  `RetireVolume`, `Prisms.Register`, `GravityFields.Register`.
- `Step(tick, bodies)` runs `GlobalRules.World.ReactionOrder` (L165), then body contact; returns a reused
  `StepResult` (events, `ContactDamage`, `PropImpulse`, `VolumeEvents`). Reactions never apply damage.
- Queries: `UnitsAt` (the "CellUnits(pos)" query), `IsWetAt`, `SurfaceAt` (kind + flags), `HasIceAt`,
  `IceDurabilityAt`, `IsChargedAt`, `IsBurningAt`, `IsDampAt`, `DustProductionAt`, `SightPathLength`,
  `BlocksAcquisition`, `Volumes`, `FindVolume`, `GetVolumesAt`, `GetOwnedFieldVolumes`, `Prisms.Trace`,
  `GravityFields.TryDeflect`, `Sound.Trace`, `Regions`, `Ledger`.
- `CaptureSnapshot()` / `RestoreSnapshot()` / `StateHash()`: dynamic state only; static arena data and Sim
  registrations are rebuilt from the same layout and registrations before restoring.

## Engine layout
- `Grid/`: `Cell` (7 unit slots with natural/temporary split, owner, source, field id, declared dps; ice, burn,
  suppression, damp, charge, venom timers), `CellGrid` (rasterization by cell center), `Footprint` (point, circle,
  rectangle/lane, segment with width, cone), `ActiveRegions` (8-connected components, cap 128).
- `ReactionWorld.*.cs`: one partial per phase (Impacts, Consumption, Electricity, Wind, Decay), plus Volumes,
  Init, Snapshot. `Hazards/Hazard` is the internal contact-damage source (cells, box, or volume shaped).
- `UnitLedger`: per-material initial/deposited/refilled/produced/consumed/removed/expired; the invariant
  `grid total == Expected(m)` is asserted every tick in tests.

## Active-region cap (L136)
Regions are the 8-connected components of active cells, so adjacent effects merge automatically. Above 128
components the smallest component (ties: lowest cell index) is merged into its nearest region (Chebyshev gap between
bounding boxes, ties: lowest index) until 128 remain. Merging only changes the representation (`ActiveRegion.Parts`
counts merged components); no cell state is ever dropped to satisfy the cap.

## Interpretations (for DECISIONS.md consolidation)
- L144 steam: each formation consumes 1 heat + 1 water; a cell repeats the reaction while both remain, so Heat 2 on
  Water 2 steams two units in one column. One cloud per (committing owner, source event) per step; an unmoved cloud
  from the same source keeps growing (a lingering trail uses one field slot). "2 meter rise" = cloud height 2 m above
  the surface. The last direct source (higher server event id) of the heat or water owns the cloud (L670).
- L145 ice: a tile forms only on a cell without ice; leftover frost stays as units. When the 5 s tile expires its
  frozen water is gone (it was consumed). Map ice tiles (BIO-004) do not expire by time and melt into natural water.
- L145/L147: hits below 20 direct damage wear the 45 durability; reaching 0 breaks the tile without shards. A hit of
  20 or more breaks the struck tile and emits one shard cone (1 m, 10 physical, each body once). Cone half angle 45°
  (no angle in spec); a vertical strike sprays all around. Brittle solids (MOD-047, BIO-021) emit the same cone.
- L146 melt water inherits the tile's remaining lifetime (natural tiles give natural water).
- L148: patch = BFS over 4-connected liquid-water cells from the contact (nearest seeds first), bounded by 12 cells
  and 6 m from the contact point; ice, dry cells and intact insulation (PROP-047: flooded at Water 3) break it. Each
  body takes the declared reaction damage on its first contact with the 0.6 s charge (grounded bodies only, feet
  within the 0.35 m step height of the surface, so a jump avoids it). Local text may shorten, never lengthen, it.
- L149: glow before release = 0.25 s (PROP-001); contact area = 1 m around the energized part, which is the first
  three branches within 6 m of the contact. A strike within 0.25 m of a conductor touches it.
- L150: a grounded conductor absorbs a branch (direct strike, or a wet patch whose cell lies within 0.5 m of it);
  default capacity 1; at capacity it overloads and is ungrounded for 4 s (PROP-002, BIO-012), then resets.
- L151 / D-023: translation needs strength > 1 (or a stated local override with its distance bound); combined gusts
  move a volume at most 3 m/s; strength >= 2 removes the columns within 2 m of the leading face once per gust and
  volume. Shadow concealment is not wind-movable by default.
- L152: water on a burning cell consumes 1 water and 1 heat (if present), ends the burn, and blocks re-ignition 2 s.
- L153: ignition consumes the 2 heat; damage is the growth's declared dps (falling back to the igniting heat's), one
  hit per second per body; after 2 s the growth is consumed. Combustible solids ignite once (one fuel layer).
- L154: each water unit dampens one sand/dust unit for 3 s (the water is bound); dust production = sand + dust −
  damp. Response "Dry it with bounded heat": one heat unit dries one damp unit once no liquid water remains.
- L155/L173: impulses clamp distance by mass class and speed to 8 m/s; collision caps 5/15/25; anchored only shakes.
- L156: hearing transmission 0.5 per ordinary solid, 0.75 through absorbent (PROP-033); sonic damage passes only on a
  clear path; nearest destructible crossed solid is reported for shaking. Volumes never affect audio (L167).
- L157: prisms reflect along their fixed face normal, once, at 70%; a reflected ray or a back-face hit is blocked.
- L158: exposure to a light shaft or bright-light volume removes one extra tick of shadow lifetime per tick.
- L159: soluble venom patches dilute to 50% when any of their cells holds liquid water; the venom never spreads into
  the water. Soluble venom clouds over water dilute the same way.
- L160: each field bends a physical projectile once on entry, by at most its stated angle, total ≤ 20° per
  projectile; magical projectiles and beams are not bent.
- L161: a dispel consumes the oldest removable layer touching its area (ground field layer or volume; ties: ground
  first, lowest id). Natural units and solids are never removed.
- L163: deposits merge in a cell under the 4-unit cap; merged temporary units share the latest expiry; clipped
  units are recorded. Player material and volumes last at most 8 s (L128). Lifetime 0 = instant residue.
- L167: path length = union of the segment's intervals inside sight-blocking volumes (overlaps count once);
  blocking at ≥ 2 m with a 1e-4 m float tolerance. Steam never wets, damages, intercepts, or mutes.
- Contact cadence: per-second hazards hit a body at most once per second, each hit one second of the rate.
- Cells are 2D: a cell has one surface height (layout patch height, or the first deposit's height while active).

## Gaps / follow-ups
- Porous walls storing and releasing water (BIO-005, MOD-021, MOD-046), heat sinks (BIO-020), basins releasing water
  per impact (BIO-024), dust urns (BIO-022), and prop-specific behaviors are not implemented here; most can be built
  in Sim from `Deposit`/`RemoveUnits`/`CreateVolume`, porous walls need a solid water store.
- BIO-001 rain is fully encoded but needs Sim wiring (biome selection, preview panel), so it carries `[Verifies]`
  but no `[ContentBehavior]`.
- Stacked surfaces (a platform above a floor) share one 2D cell.
