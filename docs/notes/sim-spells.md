# Sim casting and spell runtime — design notes

Owner paths: `src/Confluence.Sim/Casting/**`, `src/Confluence.Sim/Spells/**`, `src/Confluence.Sim/Content/**`,
`src/Confluence.Sim/Input/**`, `src/Confluence.Casting/**`, tests under `tests/Confluence.Casting.Tests/**` and
`tests/Confluence.Sim.Tests/{Casting,Spells,Content}/**`.

## Status / next steps
- Done: one input-flag enum; Sim casting integration (`PlayerCaster`, `CastFrameBuilder`, `CombatEventFeed`,
  `CastDuelEvent`); `SpellRuntime : ISpellRuntime`; spell forms with transformations, structural deltas, caps and
  prose parameters; 14 delivery primitives; content registry and behavior interfaces; SPL-009 Fireball and SPL-010
  Cinder Sweep with `[ContentBehavior]` and `[Verifies]` tests through WIZ-002's real grammar.
- Tests: `dotnet test tests/Confluence.Sim.Tests` 265 passed (157 sim-core + 108 new);
  `dotnet test tests/Confluence.Casting.Tests` 166 passed (141 + 25 input, action-lock and digest tests).
- Next: the remaining WIZ-002 / WIZ-010 spells on these primitives; WIZ-002 signature (Kiln Vent) and passive
  (Tempered Hands) once the effects framework implements `IEffectHost`; record-specific scripts (recast follow-ups
  such as SPL-008, conditional riders); `TerrainCollision` combat events; reflection hooks from Reactions prisms.

## Layout
- `Input/PlayerInput.cs` — the per-tick command. Buttons use `Confluence.Casting.InputButtons` (the only flag enum;
  bits 0-18 pinned by `InputButtonsTests`, Net serializes by value). `MovementKeysFromAxes` derives W/A/S/D bits.
- `Casting/` — `PlayerCaster` (the player's `CastMachine` + `ICastHost`), `CastFrameBuilder` (PlayerInput -> 
  `CastInputFrame`), `CombatEventFeed` (`IDuelEventObserver` turning world events into `CombatEvent`s),
  `CastDuelEvent` (every `CastEvent` in the world's event stream).
- `Spells/Forms/` — `SpellForm`/`SpellFormBuilder`, `SpellSpec` (freeze guard), `DeliverySpec`, `PayloadSpec`
  (+ `StatusSpec`, `DepositSpec`), `ProseParameter` (D-011), `SpellFormResolver` + `ActiveTransform`.
- `Spells/Runtime/` — `SpellRuntime`, `SpellContext` (aim, targets, payload application, deposits), `CastRecord`
  (one committed cast), `SpellEntity`/`ISpellBody`, `SweepQuery`, `SpellLoadout` (+ `FixedLoadouts`,
  `CatalogueLoadouts`), `SpellScript`.
- `Spells/Deliveries/` — Projectile, Ray (+ `IRayReflector`), Cone, Arc, Field, Shield, Buff, Movement (dash, leap,
  teleport), Deployable, Summon, Wave, Tether. `Spells/SpellRules.cs` holds spec-open defaults; `SpellEvents.cs`.
- `Content/` — `ContentRegistry` (reflection scan, `Availability` as `IContentAvailability`), behavior interfaces
  (`ISpellBehavior`, `ISpellTransform`, `IWizardKitBehavior`, `IAugmentBehavior`, `IEquipmentBehavior`,
  `IBiomeRuleBehavior`, `IPropBehavior`, minimal `IEffectHost`/`ISimEffect`), `SpellBehaviors/` (SPL-009, SPL-010).

## Where it runs in the tick
- Step 3 (`UpdateCasting`, per player after guard/swap/melee): permissions to the host; recall interrupts; combat
  events drained (world events emitted before this point this tick, plus everything after the previous casting
  step) and polled state events; the frame is built; the machine steps; each `CastEvent` is recorded as a
  `CastDuelEvent` and acted on: `CueStarted` -> `SpellEvent.Telegraph` with the predicted target; `Committed` ->
  `CastRecord` (origin event = the commit event id), invisibility broken (L120), FEAT-014; `Released` -> delivery
  `Release`; `ChannelPulse`/`ChannelEnded` -> delivery hooks; `Recast` -> the cast's entities with
  `AcceptsRecast`, then the script.
- Step 4 (`AdvanceDeliveries`): every live entity in creation order; dead ones removed; casts swept.
- Step 6: fields run their contact rules through `IOwnedEntityBehavior.OnTick`; the registry expires/retires.
- Lifetime recast windows (no explicit window) close when the cast's last entity ends (`EndRecastWindow`, L94).

## Casting host (`PlayerCaster : ICastHost`)
- `TryPayMana` -> `DuelWorld.TryCommitMana` (all or nothing, combat only, FEAT-013, `ResourceEvent`); the paid cost
  is `MasteryClamp.PaidCost` with `StatSheet.CostReduction` (30% cap, 70% floor, L124). `RefundMana` ->
  `DuelWorld.RefundMana`; refunds are capped per cast by the machine's `RefundLedger` (25%, once per key).
- Cooldowns per spell id start at commitment: `StatSheet.EffectiveCooldownSec` (25% CDR cap, 0.5 s floor, L124).
- `IsSilenced` = silence; `IsHardControlled` = stun or hostile displacement (root only stops movement);
  `IsWarded` = alcove phase; `IsActionLocked` from `CastPermissions.Reason` (dead, duel ended, recall, guard
  release window, melee, swap).
- Status to the world: `MovementFactor` = machine factor (record-stated factors via `CastingMoveFactor`), `Channeling`
  (world marks mana spending, L78), `SuppressesSprint` (motor locks sprint), `Busy` (any phase: blocks swaps),
  `InRecovery` (blocks melee and swaps).
- Combat events (server-generated only): PerfectGuard, GuardBlock (GuardEvent), MeleeHit (MeleeEvent), MeleeParry
  (guarded melee DamageEvent on the defender), DamageTaken (value = dealt), ForcedDisplacement (knockback, edge
  impact), Teleported; polled: DodgeCompleted, Landed (airborne -> grounded), MeleeWindupCompleted,
  MeleeRecoveryEnded, CooldownReady. Each frame owns a copy of its events.

## Spell forms
- `SpellFormBuilder.FromDefinition` seeds mana, cooldown, cast time, range, channel and tags from the record;
  the behavior adds delivery, payload and prose parameters (each a `ProseParameter` with its quoted phrase; the
  registry test asserts the quote is in the record text and contains the number, D-011).
- `SpellFormResolver.Resolve`: base behavior; then transformations ordered by `TransformStage` (Delivery, Payload,
  Equipment) and ascending augment id inside a stage (L286, L510); a `StructuralDeltas` declaration replaces the
  tags before the transform runs (D-012); two transforms sharing a non-empty exclusive group (or the same augment
  twice) throw (L286 "cannot silently coexist"); `Build` applies global caps and freezes every spec.
- Caps at build: channel 3 s (L92), push 4 m (L173), precision 1.25 (L110), status durations/intensities (L116),
  deposit units 4 per cell (L163), field/summon/trap lifetime 8 s (L128), shields 8 s and 100 (L114), dash 8 m and
  teleport 10 m (L124), tether pull 4 m / reel 8 m.
- The cast data (`CastableSpell`) is derived from the final form: weight by tags and windup, stated recovery,
  stationary, stated move factor, channel and pulse, recast rule, interrupt-refund tag, and the anticipatory cue
  (primary direct x weak-point multiplier above 70, a hard-control status, or a teleport delivery; L96).
- Mastery is applied per cast by `CastRecord`: `DirectDamageMultiplier` on the primary payload's direct damage only
  (L480), `ProjectileSpeedMultiplier`; windup and efficiency are applied by the machine. Linked bonuses clamp with
  the reward in its category (15% attribute, 20% efficiency).

## Payload application (`SpellContext.ApplyToPlayer`)
- Direct hits -> `DamageKind.Direct`, guardable when the payload is (L104); splash and field/trail contact ->
  `DamageKind.Splash`, never guarded. Every request carries the spell id, the `CastEventId`, the cast's original
  event (commit event id, so beam pulses and splits share proc budgets, L126) and the caster-target range.
- Exclusivity: with `SplashExcludesDirect`, a target hit directly takes no splash from the cast and vice versa (L525).
  `MaxPerTarget` caps the requested damage per target across the whole cast (all projectiles, pulses, parallel rays).
- Push: with damage, `DamageRequest.Knockback` so guard reduces it (L104); without damage, `ApplyKnockback` directly.
  Hostile displacement is hard control (immunity blocks it, damage still lands, L116).
- Statuses through `ApplyStatus` (caps, immunity, interrupts); deposits through `QueueDeposit` (`WorldDeposit` with
  element, units, footprint, lifetime, owner, source id, original event, owning field id).
- Split channels: `SplitChannel`/`SplitFraction` build a two-part `DamageRequest` (one hit event, L112).
- A target's live buffs add `ExtraPassiveReduction` against spell projectiles (pipeline keeps the 45% cap).

## Deliveries (defaults in `SpellRules` unless the record states a value)
| Delivery | Behavior |
|---|---|
| Projectile | From the eye along the aim (release aim, or commit aim with `AimLock.AtCommit`); sphere sweep vs solids, present player capsules, hostile bodies; gravity; max range and/or lifetime; ground bounces with damage scale; optional burst on expiry; several projectiles share caps; never lag-compensated. Default radius 0.15 m. |
| Ray / beam | Instant from the eye; center line stops at solids; width widens the body test; lag-compensated (view tick, clamped to 9 ticks); channeled forms fire every pulse; `IRayReflector` hooks (default: `MaterialFlags.Reflective` solids) give one bounce at 70% (L157, L527); parallel rays. |
| Cone | Apex at the caster's feet, horizontal angle around the aim yaw, reach, height band; a body counts when any part of its width is inside the angle; per-target cover = both chest and head lines blocked from the focus; footprint deposits as `Footprint.Cone`. |
| Arc | Spell melee: nine sample lines across the arc (or one thrust) from the focus, each stopped by solids, widened by half the strike width; one hit per target; lag-compensated. |
| Field | Circle (optional safe inner radius), lane (from the placement point along the aim) or rectangle (centered); owned entity (three per player, oldest retires); enter/tick/exit payloads as area hits; owner included unless `AffectsOwner` is false; creation deposits over the footprint with the field id and lifetime. |
| Shield | `GrantShield` with capacity, duration (<= 8 s) and optional arc (directional; follows facing). |
| Buff | Stat modifiers under a per-cast source key (removed on end) and incoming projectile reduction. |
| Movement | Dash (forward, backward, movement keys) via `TryDash` (motor collision, boundary clamp); leap = dash plus a vertical launch timed to land on the ground point (rise capped); teleport via `TryTeleport` (cue required, refused inside collision). Contact riders (once per target, optional stop), endpoint burst, path deposits. The body halts at the end: no momentum past the stated distance. |
| Deployable | Thrown (sticks on first solid within range, else drops at range end), placed, or at the caster; lifetime; optional destructible body; proximity trigger with arming delay and visible warning; recast detonation; counts as a field (trap) by default. |
| Summon | Owned summon (two per player, 8 s) with a destructible body (`DamageSummon`, FEAT-015); stationary, orbit, or back-and-forth route (stops at geometry); fires a dodgeable projectile at a visible target in clear sight within reach (L527). |
| Wave | Front split into 0.5 m segments advancing along the ground; each stops at an obstacle at 0.4 m; touches each target once (push once); feet higher than 1 m above the wave avoid it; lane deposit at the end. |
| Tether | Travelling line or instant; pull = hostile displacement toward the caster (capped, blocked by immunity); reel = self dash toward the touched solid (capped, stopped by geometry). |

## Interpretations (for DECISIONS consolidation)
- L474 / D-049: a common melee press during preparation, windup or channel performs the strike and the spell yields
  (cancel before commitment, voluntary cancel after); only cast recovery blocks common melee. Swaps stay blocked in
  every cast phase. (`DuelWorld.StepMelee` now checks `CastingStatus.InRecovery`.)
- L116: casting is prevented by stun and hostile displacement; root does not prevent casting.
- L175: the visible recall interrupts a committed cast (`InterruptCause.Recalled`) and cancels a preparation.
- L104 / L535: spell direct hits are guardable by default (projectiles and offensive melee spells such as SPL-010);
  splash and field contact are not. A record can clear `GuardEligible` (sound without a physical projectile).
- L525 exclusivity applies across the whole cast (all its projectiles and pulses), not per projectile.
- L163: an explicit "Heat N at impact" deposits N in the impact cell only; the heavy-projectile default does not add
  the adjacent ring when the record states the strength. Deposits at a body hit use the projectile's contact point.
- L96: the telegraph event is emitted at cue start with the target predicted from the live aim; the machine holds
  the commit so release is at least 0.25 s after the cue starts. The destination of an AtRelease teleport stays
  live during the cue.
- L92 / L124: spell dashes and leaps stop the body when they end (no carried momentum), so a stated distance is
  never exceeded; a hostile knockback during a dash is left alone.
- L94: a recast window without an explicit length closes when the cast's last live entity ends; an explicit window
  (e.g. "recast within 1 s") outlives the effect.
- L126: every hit of a cast carries the commit event as its original event, so beam ticks and split portions share
  one proc identity.
- L527: summons and spell bodies are spheres for hit tests; spell projectiles of the body's owner pass through it.
- Defaults (`SpellRules`): projectile radius 0.15 m; ray width 0.1 m; arc 9 samples; wave segment 0.5 m, probe
  0.4 m, max target height 1 m; deployable radius 0.25 m; dash speed 26 m/s when no duration is stated; leap 0.8 s
  when no duration is stated; summon attack interval 1 s; cover samples at 60% height and the head.
- SPL-010 "caster slows 15% during windup" is the stated move factor 0.85 (replaces the quick-cast default; it is the
  same number here). Its reach is the record's range (4 m).

## Edits outside the owned paths (minimal, additive)
- `Duel/DuelWorld.cs`: `using` alias for the shared `InputButtons`; sprint lock includes `SuppressesSprint`;
  `DamageEvent.CastId`; directional `GrantShield` overload; `RecordEvent` and `AddEventObserver` (observers see every
  emitted event); `StepMelee` checks `InRecovery` instead of `Busy`.
- `Duel/ISpellRuntime.cs`: `CastingStatus.SuppressesSprint`; Busy/InRecovery doc comments.
- `Duel/DuelEvents.cs`: `DuelEventType.Cast`, `DuelEventType.Spell`, `DamageEvent.CastId`.
- `Duel/IDuelEventObserver.cs` (new), `Duel/DuelResult.cs`: decisive sequence includes the opponent's commit/recast
  events and the player's interrupted casts.
- `Players/DuelPlayer.cs`: `Caster` property (set by the runtime).
- `Combat/DamageRequest.cs`: `CastId`, `ExtraPassiveReduction`; `Combat/DamagePipeline.cs`: the conditional passive
  reduction joins the 45% cap, and shields receive the hit's origin for directional capacity;
  `Combat/ShieldPool.cs`: directional shields (`ShieldContact`, arc per entry).
- `World/IWorldMaterials.cs`: `WorldDeposit` gains `Footprint`, `HasFootprint`, `LifetimeTicks`, `FieldId`.
- `tests/Confluence.Sim.Tests/Duel/SeamTests.cs`: the melee assertion now uses `InRecovery`; other Sim tests only
  gained the `InputButtons` alias.

## Gaps
- No `TerrainCollision` combat event yet (WIZ-014); no `IEffectHost` implementation (effects stage), so wizard
  passives, augments and equipment do not attach; signatures resolve only when an `IWizardKitBehavior` exists.
- Directional shields follow current facing; "fixed after casting" shields (SPL-131, SPL-195) need a fixed-yaw
  option. Summons have no move commands, chasing or decoy logic; deployables have no "arms after an impact"
  trigger (SPL-276) or player-chosen movement (SPL-024).
- Reflection is limited to `MaterialFlags.Reflective` solids; Reactions' `Prisms.Trace` should register an
  `IRayReflector`. Waves follow the caster's ground height (no slope following).
