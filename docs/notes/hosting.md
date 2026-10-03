# Confluence.Hosting — authoritative match host (no transport)

Owner paths: `src/Confluence.Hosting/**`, `tests/Confluence.Hosting.Tests/**`. Spec: §2 (L30-L73), §5 shop/draft
(L191-L249), §14 (L417-L436), bots L381.

## Layout
- `MatchHost.cs` — owns a `ConclaveMatch` or `DuelMatch` plus the three Match seams below. `Start()` runs the D-010 floor
  check, `Tick()` is one authoritative 60 Hz step (economy agents in player order, then optionally every running duel
  in parallel, then `MatchFlow.Tick()`, which polls the duels). `Submit(PlayerCommand)` is the command entry point.
  `SetInputSource`, `SubmitInput`, `AddBot`, `SetConnected` manage seats. `Outbox` is the outgoing queue for Net;
  `DuelStarted`/`DuelStepped` are the snapshot hooks. `Fingerprint` = match rules + Laboratory overrides + content
  policy (L71); `CountsForRating` is false whenever an override is active.
- `LaboratoryOverrides.cs` — `BypassEconomyFloors` and `PoolsUseWholeCatalogue`; canonical text goes into the host
  fingerprint; any override makes the match Laboratory-labeled (L69).
- `Content/` — `ImplementedContent` (the `[ContentBehavior]` registry over shipped rule assemblies) and
  `HostContentPolicy` (biomes/props for the generator, EQP/AUG pools for shop and draft, lobby allow-list, log notes).
- `Arenas/` — `ArenaProvider : IArenaProvider` (Match diameter forced into `ArenaRequest.Diameter`, Match's per-pass
  arena seed as the generator match seed, cached by every generator input), `HostedArena` (recipe, layout hash,
  per-duel copies, nav graph), `ArenaPreviewInfo` (public preview: diameter, biome, environment rule, modules, props
  with icon category, spawns, material descriptions; no hidden data), `MaterialDescriptions` (L329), `ArenaRecipe`,
  `ArenaCache` (thread-safe, shared across hosts/tests), `ArenaPrefetcher` (background generation of upcoming rounds).
- `Economics/` — `HostedEconomy : IMatchEconomy` (per-player `PlayerEconomy`, per-phase `EquipmentShop` and
  `AugmentDraft`, round snapshot, floors) and `LoadoutMapper` (Economy loadout -> Sim `DuelPlayerSetup`).
- `Commands/` — `PlayerCommand` (factories per kind, canonical text), `CommandGateway` (validation, idempotency ledger,
  rate-limit hook, dispatch to the economy objects / duel runner), `CommandResult` + `CommandCode`,
  `ICommandRateLimiter` (`TickRateLimiter`, `UnlimitedCommands`).
- `Duels/` — `SimDuelRunner : IDuelRunner`, `RunningDuel`, `DuelSeat`, `IInputSource` (`RemoteInputSource`,
  `IdleInputSource`), `ISimWorldFactory` (`SimWorldFactory` over a catalogue), `DuelOutcomeMapper`, `DuelReport`/
  `DuelSideReport` (feats, decisive sequence, consumables, damage), `ReplayManifest` + `InputTrack`, `DuelReplayer`,
  `HostedDuelLoadouts`.
- `Messages/HostMessages.cs` — `HostOutbox` and messages: `MatchEventMessage`, `ArenaPreviewMessage`,
  `ShopStateMessage`/`DraftStateMessage` (private), `CommandResultMessage` (private), `DuelReportMessage`,
  `HostNoticeMessage`. Audience is a player id or `HostMessage.Everyone`.
- `EconomyTurn.cs` — `IEconomyAgent` + `EconomyTurn` (an agent's private view and `Submit` through the gateway).
- `Bots/` — `BotProfile`/`BotCompetence` (four settings, L381), `BotInputSource` (duel inputs), `BotNavigator`
  (ArenaNav paths), `BotCastScript` (per-wizard press/hold/release scripts), `BotEconomyAgent`, `BotPlayer`, `BotStreams`.

## Key rules and interpretations
- Match owns the round diameter (decision from the task): the provider passes Match's diameter as
  `Arena.Generation.ArenaRequest.Diameter` and Match's recorded pass seed (`ArenaRequest.ArenaSeed`, Arena stream by
  round/attempt or the Custom seed rotation) as the generator's `MatchSeed`; `ArenaSchedule.DiameterForRound` is never
  consulted. Since the Stage 1b arena fix the two derivations agree on every round
  (`Match_Owns_The_Round_Diameter_In_Both_Modes`).
- One arena per pass, shared by every pair (L34); each duel gets `HostedArena.CreateCopy()` so runtime changes never
  leak between pairs. Arena id is `arena:r<round>.a<attempt>:<seed hex>`.
- Arena availability: a record type without any `[ContentBehavior]` class falls back to an explicit set
  (`MatchHostSettings.ConfiguredBiomes/ConfiguredProps`, default every catalogue record, D-069) and logs it; as soon as
  any record of a type is registered, only registered records of that type are used. After the Stage 1b merge BIO-001
  and BIO-011 are registered, so standard arenas use exactly those two biomes; props still use the configured set.
- Lobby allow-list (L71) restricts only the record types it names; unnamed types stay unrestricted.
- Floors (D-010, D-082): `Start()` refuses with `RefusedEconomyFloors` when the implemented pools break the
  ten-per-rarity draft floor or cannot fill the shop. `LaboratoryOverrides.BypassEconomyFloors` starts anyway (gaps
  logged and announced via `HostNoticeMessage`). Today only the six proof records (EQP-051/066/096, AUG-145/169/175)
  have behavior classes, so standard rules still refuse; match tests use `bypass-economy-floors` +
  `pools-whole-catalogue` (`HostFixture.WholeCatalogue`).
- Economy streams: shops on the Offer stream and drafts on the Draft stream keyed by scheduled round and lobby slot
  (Economy's own constructors, L435, D-084). Match supplies income + winner bonus (`ShopOpening.Income`), credited on
  `OpenShop`. The draft freezes the loadout while open (Economy's `AugmentDraft`, L227); `CloseDraft` times out an
  open draft (chooses nothing, L229). Dormancy is re-resolved at shop close, draft close, and `FinalizeLoadout` (L249).
- `FinalizeLoadout` keeps a clone as the round snapshot; every pass of the round (replays, void reruns) maps the same
  snapshot (L38, L65). Consumable charges are spent on the live loadout only after a completed (non-voided) duel
  reports `ResourceEvent.ConsumableUsed` (L212, L427).
- Loadout mapping: reserve items stay behind (L210); a slot holding its issued item is left empty in the Sim setup
  (the Sim treats an empty Main Focus/Sidearm as the plain issued focus, L571, L202); spent consumables are not sent;
  the enabled active is sent only from a slot that can activate (never the Sidearm, L577); only active (non-dormant)
  augments are sent, with ranks. The Sim setup has no charge count; one charge is one use (L212), so presence suffices.
- Commands (L429): validated by host state (running, not eliminated, phase open, arguments) and then executed by the
  Economy objects, which recheck money, offers, rerolls, slots, Unique groups and eligibility atomically. Idempotent by
  request id per player for the whole match, failures included; same id + different canonical command =
  `RequestIdConflict` (D-073). Rate limiting runs first; refused requests are not recorded, so the same id may be
  retried (D-116). `TickRateLimiter`: rerolls burst 3 then 1 per 15 ticks; other economy transactions burst 5 then 1
  per 12 ticks; ready and surrender unlimited.
- Draft select carries the replace target (`augmentSlot`) and `confirmDormancy`; Economy refuses a selection whose
  dependents would go dormant unless confirmed (L231, L645). Selecting or passing ends the player's draft (L229).
- Surrender goes to `DuelWorld.Surrender` and takes effect at the end of the next step (D-108); it is recorded as a
  replay action. Both surrendering in one step is a Sim draw, reported to Match as `SimultaneousLethal` (Match accepts
  draws only for simultaneous lethal or time).
- Duel timing: the world is created with Match's `AlcoveTicks` (5 s, L46) and `CombatMaxTicks` (90 s, L47) and runs its
  own duel flow; world tick k == Match's duel tick k, so Match's combat clock and the Sim's agree exactly. `Poll` steps
  the world up to the duel tick Match asks for; with `ParallelDuels` the host pre-steps all running duels with
  `Parallel.For` (results identical; snapshot hooks then fire in start order).
- Name collisions: `MatchDuelOutcome = Confluence.Match.DuelOutcome`, `MatchDuelEndReason`/`SimDuelEndReason`,
  `GeneratorRequest`/`MatchArenaRequest` aliases.
- Disconnection (L55): `SetConnected` forwards to `MatchFlow.SetConnected`; a `RemoteInputSource` then sends neutral
  input facing the last view direction. Nothing pauses; the absent player keeps the slot and loses normally.
- Replays (L419, L427, L433): `ReplayManifest` = ruleset fingerprint, match seed/mode/duel/round/attempt/pass kind,
  arena recipe + layout hash, spawn sides, both setups (copies), content ids, phase lengths, per-tick inputs
  (`InputTrack`, run-length encoded; a run advances Tick and Sequence by 0 or 1 per step), between-step actions, final
  tick + state hash. `DuelReplayer` regenerates the arena from the recipe (or takes a layout), verifies the layout hash,
  refuses a ruleset mismatch ("unsupported version"), and compares the final hash.
- Bots (L381): perception only through `DuelWorld.CanAcquire` (no wallhacks), delayed by the bot's reaction latency;
  reactions are never below 0.20 s; aim error never below 0.5 deg and grows with target lateral speed; guard and dodge
  are chances. Novice telegraphs and casts plainly; Apprentice strafes; Adept/Expert keep a 25 stamina guard reserve,
  lead targets and attempt mastery timings. Cast scripts: WIZ-002 holds ~0.3 s (mastery: crest window 0.40-0.50 s);
  WIZ-010 holds 24 ticks (mastery: hold to 51, vent with ModifierA for 6 ticks, release at tick 33 staying in the
  45-55 band); others hold-and-release 0.3 s. Bot RNG: `RngStream.Bot`, duel stream (duelId, player), economy stream
  (round, 65536 + player). Bots shop and draft through the command gateway with the same rate limits as clients.
- Arena prefetch uses the thread pool, but the cache key holds every generator input and released arenas never depend
  on what was prefetched, so results are deterministic regardless of timing.

## Coupling points with the Sim
- `SimWorldFactory(Catalogue, ContentRegistry)` builds the full authoritative duel: `SpellRuntime.FromCatalogue`, the
  Sim's default `ReactionMaterials`, catalogue props and biomes, registered passives/equipment/augments.
  `MatchHost.WorldFactory` is the one every duel of a match uses; `DuelReplayer` takes it (the generator overload
  defaults to a new factory over the generator's catalogue; the layout overload requires one).
- Sim: `LoadoutMapper`
  (`DuelPlayerSetup` fields), `DuelOutcomeMapper` (Sim `DuelResult`/`DuelEndReason`), `RunningDuel.Step`
  (`ResourceEvent.ConsumableUsed`), `BotInputSource` (`DuelWorld.CanAcquire`, `TickEvents`, `MeleeEvent`,
  `GuardEvent`, `MovementEvent`, `DuelPlayer.Motor/Guard/Melee/Resources`), `ReplayManifest.CloneSetup`
  (copy every new `DuelPlayerSetup` list field here) and `InputTrack.SameControls` (compare every new `PlayerInput`
  field here, or replays drop it).
- Match: the three seams only (`IArenaProvider`, `IMatchEconomy`, `IDuelRunner`) plus `MatchFlow` events/players.
- Economy: `HostedEconomy` and `CommandGateway` dispatch.

## Status / next steps
- Done: everything in the Stage 1b hosting task, merged 2026-10-03 with the spell runtime and the world effects. 36
  tests (8-bot Conclave end to end with spells and reactions live, Duel BO7, serial/parallel determinism, replay hash,
  command validation/idempotency/rerolls/draft end, arena sharing and diameter cross-check, floors refusal and
  Laboratory bypass fingerprint, loadout mapping, disconnects, bot perception and cast scripts, the six proof records
  in a mapped duel, generated prop collision boxes as Sim solids).
- Bots now fight with real spells (WIZ-002 Fireball and Cinder Sweep deal damage; WIZ-010 has no implemented spell yet).
  Re-tune bot range and aggression for spell play in Stage 2 (Fireball range 30 m, Cinder Sweep 4 m cone).
- `InputTrack` keeps `PlayerInput.Tapped` and `GrammarEvents` (a command with grammar events never joins a run, its
  list is copied). Re-check `SameControls` and `CloneSetup` whenever `PlayerInput` or `DuelPlayerSetup` gain fields.
- When enough EQP/AUG/PRP behavior classes land, standard matches start without overrides; `HostContentPolicy`
  switches per type automatically. Add a floor-passing standard-rules end-to-end test then.
- Net integration: drain `HostOutbox` after each `Tick`, route by `Audience`, feed `SubmitInput` from the receive
  buffer, map `CommandResult` codes to wire codes. Snapshot writing hooks onto `DuelStepped`.
- Debug-build test time is dominated by arena generation (0.3-8 s per layout); fixtures pick small-diameter seeds and
  share an `ArenaCache`.
