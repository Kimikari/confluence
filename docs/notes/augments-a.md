# augments-a: shared augments of six families

Families: Casting mastery, Projectile and beam shaping, Fieldcraft and terrain, Melee and guard, Movement arts, World
reactions (144 records with empty `target_spell_ids`). Code: `src/Confluence.Sim/Content/Augments/augments-a/`; tests:
`tests/Confluence.Sim.Tests/Content/Augments/augments-a/`.

## Status / next steps

- Done with `[ContentBehavior]` + `[Verifies]` tests (75 records):
  - Casting mastery: AUG-001, 005, 006, 007, 009, 012, 013, 014, 017..021.
  - Projectile and beam shaping: AUG-025..028, 030..033, 038, 039, 041..044.
  - Fieldcraft and terrain: AUG-049..055, 059, 060, 062, 065..068, 070.
  - Melee and guard: AUG-073..080, 082..090, 092.
  - Movement arts: AUG-097..105, 107, 108, 110, 115, 117.
  - World reactions: AUG-125.
- Every unconditional ("always") record of the six families is done except AUG-109 (see gaps).
- Review round 1 (Claude verifier + Codex) fixed: mastery-linked damage/speed clamp once per cast (AUG-001, 006, 007,
  009, 013, 018, 020); gait bonuses/penalties summed under one +20% cap (AUG-041, 076, 084, 101, 107, 110, 117);
  AUG-031 judged from the caster's feet; AUG-043/AUG-105 public geometry markers (`MarkerEvent`), AUG-105 counterplay
  test; AUG-012 judged at release; AUG-080 needs payment inside 0.5 s; AUG-082 boosts a release-tick impact; field
  placement prices (AUG-053, 054, 060, 065, 066) settle against the created field, cover records bind the placement;
  full hook state hashing.
- Draft floor (`DraftFloorTests`, Economy `EconomyFloors.CheckDraftFloor`, default loadouts, `ContentRegistry.Default`
  availability, Conclave and Duel): WIZ-002 Silver 33 / Gold 17 / Prismatic 16; WIZ-010 Silver 32 / Gold 17 /
  Prismatic 16 (floor 10 each). The test also prints the augments-a records that are legal at that loadout but not
  implemented: WIZ-002 AUG-121, AUG-109, AUG-129; WIZ-010 AUG-128, AUG-109.
- Not done (gaps), with the reason:
  - AUG-121 Steam Scholar ("Steam radius increases 0.2 m", "Steam lifetime decreases 0.5 s"), AUG-129 Ash Garden (inert
    ash patch), AUG-128 Salted Soil ("Remove 0.25 extra Sand strength"), AUG-122/123/124/126/127/130/132..144: they edit
    Reactions volumes and cell units, whose setters are internal to `Confluence.Reactions` (and its 0.5 m columns and
    integer units cannot express 0.2 m / 0.25 strength). Needs a Reactions-side tuning seam (e.g. a per-source volume
    radius/lifetime adjustment and fractional removal budget) before these can be honest.
  - AUG-109 Wet Route: unmarked (class and tests removed). The record reduces shallow water's "movement penalty", but
    the rules give shallow water none (spec L175 "Wet stone ... no movement penalty by itself"; the Water Pool record
    "no automatic slow"). The earlier version reduced any slow (including enemy spell slows) on a wet surface, which
    the record never states. Needs a rules-side shallow-water penalty (or a DECISIONS entry defining one) first.
  - Field-requirement records (AUG-049..055, 060, 065, 066, 068, 070) are verified in DuelWorld with test field
    behaviors (`ShapedField`) registered under SPL-012/SPL-013 (the catalogue supplies their mana, range and windup).
    Re-verified at the Stage 2 merge against the real kit fields (`tests/Confluence.Sim.Tests/Integration/
    KitFieldAugmentTests.cs`: Ash Curtain cloud, Riptide Lane stream, Pool Divide pools) and in the hosted Laboratory
    match (Bram's AUG-053/065 price his ash clouds). Multi-spell records (AUG-020 etc.) use `TestBolt` under
    SPL-015/016/073.
  - Stage 2 merge (2026-10-05): the projectile and Fieldcraft families recognised only the framework
    `ProjectileDelivery`/`FieldDelivery`, so they were inert on Halen's Pressure Jet and Deepglass Prison, Bram's
    transformed Fireball, Ash Curtain and Riptide Lane although the Economy drafted them for those spells' tags. They now
    read the shared shapes (`Spells/Deliveries/ProjectileShapes.cs`, `FieldShapes.cs`); a lane's placement distance is
    measured from its start (the footprint's center had been used, its midpoint). Fields left behind by other
    deliveries (AUG-307 heat strip, AUG-316 Boiler Orb trap, WIZ-010 puddles) are still skipped.
  - AUG-114 Dash Brake: the drawback "Dash costs 2 extra stamina when applicable" belongs to stamina-paid dashes
    (EQP-094 Traveler's Burst), which have no implementation or cost hook yet.
  - Beam records (AUG-008, 034..037, 046) need a damaging channel spell; AUG-010, 011, 022..024, 029, 040, 045, 047, 048,
    056..058, 061, 063, 064, 069, 071, 072, 081, 091, 093..096, 106, 111..113, 116, 118..120 are not started.
- Next: a Reactions tuning seam for the World reactions family; AUG-061 (owned-entity lifetime extension), AUG-111,
  AUG-119, AUG-024 are cheap with the existing hooks.

## Framework additions (shared files, additive) — list in crossEdits at merge

- Review round 1 additions: `Effects/HookAggregates.cs` (new: `MasteryLinked` one-clamp-per-cast math, `GaitAdjust`
  and the summed gait totals), `Effects/MarkerEvent.cs` (new: a public inert marker with From/To/Direction/duration);
  `EffectHook` gained `LinkedProjectileSpeed`, `LinkedDirectDamage`, `AdjustGait`, `BindFieldPlacement`,
  `EmitMarker`, `ChargeMana`; `EffectHost.Hooks`/`EffectSystem.Hooks` aggregate them (`ChargeMana`,
  `BindFieldPlacement`); one additive line in `Spells/Deliveries/FieldDelivery.cs` Release (bound placement); a doc
  comment in `Casting/Mastery/MasteryClamp.cs`.

- `Effects/EffectHooks.cs`, `EffectHost.Hooks.cs`, `EffectSystem.Hooks.cs` (new): `EffectHook`, a stateful per-player
  content hook (OnEvent records facts; OnTick acts through `TryActivate` and the proc budget; adjust points for the
  motor command, strike profile, cast price, mastery-dependent price, cast timing, projectile launch speed, outgoing
  hits, and spell durability damage to solids).
- `Content/AugmentFormStages.cs` (new): `IAugmentFormStage`, a shared augment's edit of each equipped spell form after
  transformations, before caps (spec L543), ascending id order.
- Casting `CastModifiers`/`MasteryClamp`: mastered-only and normal-only cost reductions; windup and recovery timing
  modifiers (`TimedWindupTicks`, `TimedRecoveryTicks`); the cast machine stores the committed recovery.
  `MasteryClamp.RewardedCastEfficiency`: mastered-only cost reductions are mastery-linked efficiency and clamp with a
  mana-efficiency reward at 20% (spec L480); `CastMachine` uses it.
- `Spells/Deliveries/ProjectileDelivery.cs`: `ProjectileEntity.ScaleSpeed` (internal; AUG-030 mid-flight speed).
- One-line hook calls: DuelWorld (motor command, strike begin), EffectSystem (price, outgoing), PlayerCaster
  (modifiers), ProjectileDelivery (launch speed), SpellContext (solid damage), SpellFormResolver/SpellLoadout
  (form stages), ResourcePools.LoseStamina.
- `tests/Confluence.Sim.Tests/Confluence.Sim.Tests.csproj`: references Confluence.Economy (draft floor test).
- `tests/Confluence.Hosting.Tests/EconomyAdapterTests.cs`: the proof-pool assertion is a subset check (content stages
  add records to the pools).

## Helpers in this folder

- `AugmentsAKit` (walking, gait multipliers under the +20% cap, hit classification, `NearVerticalSolid` wall/cover
  probe: 16 horizontal rays at 0.9 m, faces within 30 degrees of vertical), `CastFacts` (mastery-linked damage/speed
  clamps), `FieldGeometry` (live field footprints, predicted footprint of a field cast, signed edge distance, overlap,
  overhead cover), `FieldStages`, `FieldForms`, `ProjectileForms`.

## Interpretations

- "Walking" = grounded, not sprinting, not displaced, horizontal speed at least 1 m/s (AugmentsAKit.WalkingMinSpeed);
  "stationary" = grounded, undisplaced, below 0.1 m/s (AUG-006); "walking sideways" = walking with more of the
  horizontal velocity across the facing than along it (AUG-103).
- Health thresholds ("below 90 health") are absolute points.
- Conditions completing after commitment (e.g. "stays grounded through release") return the decrease through the
  cast's refund ledger (25% cap, once).
- AUG-039 "landing recovery": the base rules give landing no recovery; the first 0.25 s after landing (L104 window).
- "Preparation decreases X%" / "longer preparation" (AUG-055, 065, 066) = the cast's windup after commitment, as for
  AUG-005 (the grammar phase is player-timed).
- Field records: a field is a field delivery (circle, lane, rectangle); "radius" is a circle's radius or a lane's
  half-width; "initial direct damage" is the first-contact (enter) payload; placement distance is measured from the
  caster's feet to the placement point (a circle's or rectangle's center, a lane's start); fields of either player and player clouds count as "another field"; "exactly one
  living persistent field" counts the owner's field entities (spell fields and clouds).
- "Direct player damage decreases 2%" (AUG-059, 125) = the owner's direct spell hits on players (splash, fields and
  weapons unchanged). "Airborne-target damage" (AUG-030) = the owner's spell hits on an airborne target.
- "Enemy-created cover" (AUG-059) = any destructible solid the opponent created; "cover object" (AUG-067) = any
  destructible solid (props, cover, spell walls); "dry destructible" (AUG-125) = a destructible prop whose cell holds no
  liquid water; "Heat effect" = a form with school Heat / heat tag / thermal channel.
- AUG-062 "within 4 m": horizontal distance from the owner's feet to the prop's base center.
- AUG-105 "lateral motion": added over 0.1 s right after takeoff as a self displacement (motor collision, legal
  boundary), then the jump's horizontal velocity is restored, so exactly the stated distance is added; no extra height.
- AUG-082 "Second weapon hit before projectile resets setup": a second weapon hit inside the 1 s window cancels the
  setup; the next hit starts a new one. The projectile counts when it is released inside the 1 s.
- AUG-088 "within 2 m of solid cover": a vertical solid face within 2 m of the body (the wall/cover probe).
- AUG-070: the range bonus applies to aimed field forms whose range is at least 10 m; the lifetime drawback to every
  field form of the owner.
- AUG-117: the 2 (4) stamina is spread evenly over the first 30 ticks of a sprint begun with the cooldown ready; the
  5 s cooldown runs from that sprint's start.
- Records whose draft requirement is equipment, a feat, or another augment behave the same in the duel; Economy's
  eligibility and dormancy (L602-L646) decide when they are active.
- Mastery-linked records = the Casting mastery family (all tagged "mastery"): their direct-damage and projectile-speed
  bonuses are summed over every record on the cast and clamped once with the cast's own reward at 15% (L480); other
  damage/speed edits (AUG-025/026/103 etc.) stay outside that clamp.
- One reward per cast (L124 "never both") limits the grammar reward itself; mastery-linked records of a different
  category (AUG-005 windup, AUG-014 mana, AUG-020 damage on a mana-rewarded cast) still apply under the full combat
  caps (L480 "Different statistical categories still obey the full combat caps"). The strict reading would make AUG-005
  inert for all ten wizards it lists (none has a windup reward).
- Gait: every record's walk/sprint request is summed; bonuses join the stat block's gait bonus under the +20% cap,
  penalties (drawbacks) apply after the cap so they are never absorbed by it.
- AUG-031 "release ... at least 1 m above enemy feet" = the caster's feet at least 1 m above the enemy's feet at release
  (a ledge or near the jump apex); from the eye every level-ground release would qualify.
- AUG-043's trail and AUG-105's exit are public `MarkerEvent`s (authoritative events both clients receive): the trail
  runs from the projectile's launch point to its end with the launch direction; the exit from the takeoff point to the
  shove's end with its direction, visible until about the landing.
- AUG-012 is judged at release (L90-L92): the 2/4 mana return through the cast's refund ledger.
- AUG-080 "paid spell within 0.5 s": the commitment (payment, L90) must fall within 0.5 s of the perfect guard (a
  WIZ-002 tap fits after the guard's release window).
- AUG-082: a projectile that hits on its release tick is boosted when the record's own 0.5 s budget is open; the
  activation is recorded at the dispatch point that tick.
- Field placement prices (AUG-053, 054, 060, 065, 066) settle against the field actually created: drawback increases
  are charged at commitment from the predicted placement and corrected at creation (returned, or charged after the
  fact up to the mana the caster has); decreases are returned through the refund ledger when the created field
  qualifies (25% cap, as for every condition completed after commitment). AUG-065/066 also set the windup from the
  placement, so an aimed field of a caster with either record is created at the committed placement and direction.
  AtCaster fields are not bound (their price still settles).
