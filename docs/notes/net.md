# Confluence.Net foundation

Transport and protocol primitives over LiteNetLib 2.1.4 (Stage 1), and the Stage 3 game protocol, server, client session
library, and headless client (see "Stage 3" below). The foundation layer only knows bytes, sessions, and generic
buffers and depends on Confluence.Core and LiteNetLib only; the `Game` folder adds Hosting, Sim, Arena and Match.

Spec: §14 (L417-L436), reconnect window (L55), spectator delay (L391), security (L429), unsupported-version
state (L427), snapshot rate (L421, DECISIONS D-005).

## Layout (`src/Confluence.Net`)

| Folder | Contents |
| --- | --- |
| `NetRules.cs` | Protocol version, rules version string, and every transport tuning number (spec-derived ones forward to `GlobalRules.Network` / `GlobalRules.Match`). |
| `Serialization/` | `NetWriter` / `NetReader` (little-endian, LEB128 varints, zigzag, strict UTF-8, finite floats, capped counts; violations throw `NetReadException`), `Quantization` (1/1024 m positions bounded to +-512 m, 16-bit yaw and pitch, generic range quantizer), `DeltaCoding` (`ChangeMask`, int / forward / scaled deltas). |
| `Protocol/` | `NetChannel` lanes and delivery methods, `MessageType<T>` definitions (id, channel, direction, rate limit), `MessageRegistry<TSender>` dispatch with checks, `Handshake` messages (`HandshakeRequest`, `ConnectionRejection`, `Welcome`, `DisconnectNotice`, `ClockPing/Pong`), `RulesetFingerprint` (SHA-256), `ReconnectToken` + token sources. |
| `Transport/` | `ServerTransport`, `ClientTransport`, `SessionDirectory` / `PlayerSession`, `TransportSettings`, `TransportCounters`, `INetClock` (`SystemNetClock`, `ManualNetClock`, `OffsetNetClock`), `ITickClock`. |
| `Requests/` | Request/response with per-player request ids: `RequestType<TReq,TResp>`, `RequestClient`, `RequestServer`, `RequestLedger` (dedupe window). |
| `RateLimiting/` | Integer `TokenBucket`, per-sender per-key `RateLimiter` with counters. |
| `Sync/` | `ClockSync`, `InputSendBuffer` / `InputReceiveBuffer` / `InputPacket`, `SnapshotBuffer<T>` + `SnapshotHeader` + `Reconciliation.Replay`, `SnapshotRateSelector`, `SpectatorDelayBuffer<T>`. |
| `Diagnostics/` | `UdpLinkSimulator`: seeded UDP relay adding latency, jitter, loss, duplication, outages. |

## Design

- **Threading.** Both transports run LiteNetLib in manual mode (`StartInManualMode`, `ManualUpdate`); all socket
  I/O, library timers, events, and handlers happen inside `Poll()` / `Flush()` on the caller's thread.
  `TransportSettings.ManualMode = false` switches to LiteNetLib's own update thread, but events are still only
  raised from `PollEvents` (UnsyncedEvents = false).
- **Lanes.** One LiteNetLib channel per `NetChannel`: Control, Match, Shop, Draft, Events (reliable ordered,
  independent so a slow shop transaction never stalls combat events), Bulk (reliable unordered), Input and
  Snapshot (sequenced), Time (unreliable). A message type is bound to one lane; arriving on another lane is a
  violation. Unreliable packets carry no lane number in LiteNetLib, so unreliable delivery alone means Time.
- **Framing.** `[varint message id][body]`. Ids below 64 are reserved for protocol messages (`ProtocolMessages`:
  Welcome 1, ClockPing 2, ClockPong 3; requests use their own ids). Every body is decoded fully and must end
  exactly (`ExpectEnd`) before its handler runs, so a handler never sees a partial message.
- **Dispatch checks** (`MessageRegistry.Dispatch`): unknown id, wrong direction, wrong lane, rate limit (server
  side, per sender and message id), malformed body, handler exception. Each result is counted globally and per
  session (`TransportCounters`); `MalformedKickThreshold` violations (default 64) close the connection without
  resume. Handler exceptions are contained (optionally rethrown from `Poll` for tests).
- **Handshake.** The connect request carries `HandshakeRequest`: magic, protocol version, ruleset fingerprint,
  rules version string, client build id, display name, reconnect token. The server checks in order: protocol
  version (read from the frozen magic + version preamble, so other layouts still get UnsupportedProtocol), full
  decode, ruleset fingerprint, required build, display name, then token (resume) or capacity (new).
  Rejections carry `ConnectionRejection` with the server's protocol, rules version, and fingerprint so the client
  can show the unsupported-version state (L427); `IsVersionMismatch` covers protocol and ruleset mismatches.
  On acceptance the server sends `Welcome` (player id, token, resumed flag, tick rate, snapshot rate, reconnect
  window, server tick). The client holds non-control traffic that overtakes the welcome on other lanes and
  dispatches it right after the welcome.
- **Ruleset fingerprint.** SHA-256 over a domain tag, the rules version string (`NetRules.RulesVersion`, bump
  on any rule-code change), and the catalogue bytes with a leading BOM and all CR bytes removed (immune to
  line-ending conversion; JSON cannot contain a raw CR inside a string, so content changes always change it).
- **Sessions and reconnect.** A `PlayerSession` (player id, display name, 128-bit token) outlives connections.
  After a drop the session is resumable for `ReconnectWindowMs` (90 s, L55, inclusive bound) measured on the
  injectable `INetClock`; after that it expires (`SessionExpired`), and the token is rejected with
  `ReconnectExpired`. Resuming while the old connection is still alive takes the session over (old connection
  closed with `SessionTakenOver`). Kicks retire the session unless `allowResume`. Expired sessions free their
  request ledger and rate buckets. Capacity counts connected plus resumable sessions.
  `ClientTransport.AutoReconnect` retries every `ReconnectRetryMs` inside the window; pending requests are
  resent on resume (the server ledger dedupes them) and failed when the identity is lost.
- **Requests.** Client-assigned increasing request ids per player. The server's `RequestLedger` keeps the framed
  responses of the last 64 ids: a duplicate gets the original response bytes without rerunning the handler
  (L429 duplicate purchases); ids below the window are answered `Stale`; rate-limited requests are answered
  `RateLimited` and not recorded, so a later retry with the same id may run. Handler exceptions answer
  `ServerError` and are recorded (never rerun).
- **Inputs.** `InputSendBuffer` numbers inputs from 1 and sends each packet with the newest input plus up to
  `InputRedundancy` (4) older unacknowledged ones. `InputReceiveBuffer` dedupes, orders, consumes one per server
  tick, repeats the last input for up to 6 ticks when starved (then neutral), never lets the client bank more
  than 12 ticks ahead (trimmed on accept by jumping the cursor, so a forged far-future sequence costs O(pending)),
  and exposes `LastProcessedSequence` for the snapshot header ack. Late inputs whose
  sequence was already substituted are dropped.
- **Snapshots.** `SnapshotHeader` (server tick, last processed input, optional baseline tick). `SnapshotBuffer<T>`
  stores snapshots by tick (out-of-order inserts, duplicates and stale ones rejected) and samples a render tick
  between bracketing snapshots (alpha), clamping before the oldest / after the newest. `ClockSync.InterpolationTick`
  gives the render tick 100 ms behind the estimated server tick. `Reconciliation.Replay` reapplies the
  unacknowledged inputs over an authoritative state.
- **Clock sync.** NTP-style samples from ClockPing/ClockPong (fast pings for the first second, then 1 Hz). The
  offset estimate uses the minimum-RTT sample of a 16-sample window, snaps when off by more than 250 ms, and
  otherwise eases by 25% per sample. `RecommendedInputLeadTicks` suggests how far ahead inputs should be stamped.
- **Snapshot rate.** `SnapshotRateSelector` per client: 30 Hz by default, 20 Hz after 500 ms of congestion (windowed
  loss >= 5%, reliable backlog >= 32, or RTT >= 80 ms above the 10 s RTT floor), back to 30 Hz after 3 s clear.
  `ShouldSend(tick, rate)` spreads sends evenly over 60 Hz ticks. Feed it with `ServerTransport.GetLinkStats`.
- **Rate limits** (`NetRules`): rerolls burst 3 then 1 / 250 ms, transactions burst 5 then 1 / 200 ms, chat
  burst 4 then 1 / s, clock pings burst 8 then 1 / 50 ms. Numbers are tuning (the spec only requires limits).
- **Network simulation.** LiteNetLib's `SimulatePacketLoss` / `SimulateLatency` are compiled only into DEBUG
  builds of the library; the release NuGet build ignores them (verified), so `UdpLinkSimulator` impairs traffic
  at the socket level with a seeded `Rng`. Tests route clients through it on 127.0.0.1.
- **Spectators.** `SpectatorDelayBuffer<T>` holds items until they are at least the spectator delay old, never
  less than 15 s while the match is active (L391).

## Tests (`tests/Confluence.Net.Tests`)

Real UDP loopback on 127.0.0.1 with ephemeral ports (`TestSupport/LoopbackRig`), polled with timeouts, never
fixed sleeps beyond 1 ms pump steps. Reconnect windows use `OffsetNetClock` on the server to skip time.
Pure units: serialization, quantization, delta coding, token buckets, rate selector, fingerprint, spectator
delay, input and snapshot buffers, clock sync.

## Status / next steps

- Status (2026-10-02): all Stage 1 components implemented; `dotnet test tests/Confluence.Net.Tests` passes 120 tests
  in about 3 s.
- Stage 3: define game messages (ids >= 64) for inputs (`InputPacketMessage` with a game input codec), snapshots
  (header + delta-coded entity state against the acked baseline), match flow, shop and draft requests with the
  `NetRules` rate limits; drive a `SnapshotRateSelector` per session from `GetLinkStats`; set
  `ServerTransport.TickClock` to the sim clock; feed spectators through `SpectatorDelayBuffer`.
- Lag compensation (150 ms rewind, L423) belongs to Sim and uses the clock estimate and input acks from here.


# Stage 3: the network game (`src/Confluence.Net/Game`, `Confluence.Server`, `Confluence.HeadlessClient`)

The game layer depends on Hosting (MatchHost, command API, bots), Sim (DuelWorld, motor, cast machine), Arena
(regeneration, layout hash), and Match. Spec: §14 (L417-L436), §2 (L30-L73), HUD/shop (L345-L355), spectators (L391).
Research applied: `docs/research/netcode.md` (checklist status below).

## Layout

| Folder / file | Contents |
| --- | --- |
| `Game/GameProtocol.cs` | Every game message type (ids 64-98) and request type with lane, direction, rate limit. |
| `Game/Wire/` | Message bodies: lobby (`LobbyMessages`), match flow (`MatchMessages`), shop/draft (`EconomyMessages`), duel (`DuelMessages`), inputs (`InputCodec`: `PlayerInputCodec`, `DuelInputMessage`), arena layout download (`LayoutCodec`, `ArenaLayoutRequest/Message`), helpers and limits (`WireIO`, `WireLimits`). |
| `Game/Snapshots/` | `DuelFrame` (players, private entry, keyed sections), `FrameEntries` (entity, owned, volume, cell, prop, solid entries; `KeyedSection` diff/apply/write/read), `FrameCapture` (server extraction and per-audience projection, visibility), `SnapshotCodec` (body layout, byte budget, field-group deltas). |
| `Game/Server/` | `GameServer` (lobby, match host, routing, interest management, snapshots, events, spectators, layout download, metrics), `GameServerRunner` (real-time pacing), `NetworkInputSource` + `LagCompensation` (input timing, view-tick rules), `DuelEventFilter` (per-audience events), `EconomyViews` (private shop/draft views), `SnapshotLinkMonitor` (application RTT and snapshot loss), `SessionMetrics` (JSON line). |
| `Game/Client/` | `GameClient` (session, join, commands, inputs, handlers), `ClientDuel` (snapshots, prediction, reconciliation, interpolation, events), `ClientTiming` (`InterpolationClock`, `InputLeadControl`), `MotorPredictor`, `GrammarPredictor`, `ClientArenas`, `ClientViews` (view models: `ClientMatchView`, `HudView`, `SpectatorView`). |
| `Game/Headless/` | `HeadlessPlayer` (client + economy + pilot), `HeadlessEconomy` (shop/draft heuristics), `ViewPilot` (duel bot over the replicated view, reusing Hosting `BotProfile`, `BotCastScript`, `BotNavigator`, Bot RNG stream). |
| `src/Confluence.Server` | Console host: CLI, logs, real-time pacing, 1 ms Windows timer, clean shutdown. |
| `src/Confluence.HeadlessClient` | Console headless player for multi-process runs. |

## Protocol

Lanes (`NetChannel`, ten LiteNetLib channels on the full `NetManager`/`NetPeer`; each reliable channel has a 64-packet
window): Control, Match, Shop, Draft, Events (reliable ordered), Bulk (reliable unordered), Input, Snapshot,
Cosmetic (sequenced), Time (unreliable). `NetRules.ProtocolVersion` = 2.

| Id | Name | Lane | Dir | Purpose |
| --- | --- | --- | --- | --- |
| 64/65 | lobby.join | Match | req | name (handshake), wizard limited to implemented kits (Laboratory: executable grammars too), input route |
| 66 | lobby.state | Match | S→C | seats, wizard choices, status, Laboratory label, settings fingerprint (L71), notice |
| 67 | match.settings | Match | S→C | seat, host and match fingerprints, timings, pace (accelerated label) |
| 68 | match.phase | Match | S→C | phase, round, bracket, attempt, start/end tick (timer) |
| 69 | match.reveal | Match | S→C | opponent, side, Resolve of both, arena id, opponent's completed previous-round loadout (never live purchases) |
| 70 | match.arena | Match | S→C | arena recipe, layout hash, public preview (spawns, modules, props, shapes, material notes) |
| 71 | match.final_loadouts | Match | S→C | own and opponent's visible loadout at the alcove |
| 72 | match.standings | Match | S→C | Resolve, wins, losses, eliminations, connection |
| 73 | match.duel_result | Match | S→C | winner, reason, health fractions (other pairs' held for live duelists) |
| 74 | match.end | Match | S→C | placements, champion, rating flag |
| 75 | match.notice | Match | S→C | host notices |
| 76 / 77 | shop.state / draft.state | Shop / Draft | S→C | the receiver's private shop or draft only |
| 78-85 | shop.command, shop.reroll, draft.command, draft.reroll | Shop / Draft | req | Hosting command API; idempotent ids; rerolls burst 3 then 1/250 ms, transactions burst 5 then 1/200 ms |
| 86/87 | duel.surrender | Match | req | a normal loss (L53) |
| 88 | duel.start | Match | S→C | duel id, side, sides' public setups, alcove/combat ticks, match tick of duel tick 0 |
| 89 | duel.input | Input | C→S | newest command plus 4 redundant ones; snapshot ack, received count, declared interpolation ticks |
| 90 / 91 | duel.snapshot / duel.snapshot_reliable | Snapshot / Bulk | S→C | header, input queue depth and starvation count, budgeted body; reliable only for over-budget keyframes and the final state |
| 92 | duel.events | Events | S→C | authoritative events per audience (casts, telegraphs, damage confirmations, statuses, fields, deaths) |
| 93 | duel.report | Events | S→C | post-duel report (feats, decisive sequence) |
| 94 / 95 | spectate.request / spectate.frame | Match / Bulk | C→S / S→C | delayed public frames (>= 15 s, L391) for players outside a live duel |
| 96 | duel.cosmetic | Cosmetic | S→C | reaction cues at snapshot cadence (never delays damage confirmations) |
| 97 / 98 | match.arena_layout_request / match.arena_layout | Match / Bulk | C→S / S→C | exact deflated layout of an announced arena when regeneration does not match the hash |

## Snapshots

- Body: duel id, match tick, phase, combat tick, both players, the receiver's private entry, then keyed sections:
  mandatory entities, owned fields, solids (the prediction collision mirror depends on solids); budgeted props,
  obscuration volumes, reaction cells. Deltas are against the newest snapshot tick the client acknowledged that the
  server still remembers (64 frames); the remembered frame is exactly what the client decoded.
- Field-group deltas (research §2.4.4): players (pose, velocity, view, bits, cast, melee, vitals, statuses), the private
  entry (motor, vitals, slots, machine+grammar, motor context, tuning, misc), entities (position delta, velocity,
  identity, extra), owned (position, identity, expiry, health), volumes (offset, extent, shape: a drifting steam volume
  never resends its bitmap), props (position, yaw, durability, flags, definition). Unchanged groups cost nothing.
- Cell capture (research §2.4.5, `CellCapture`, one per live duel): refreshes only `ReactionWorld.ActiveCells` plus the
  cells it reported last time, with a full scan on the first capture and every 30 captures. Exact because every
  non-empty cell is active (`Cell.IsActive`) and the active list is rebuilt at the end of each Reactions step; the full
  scan stays as `FrameCapture.CaptureCells` (the reference). Measured on real duels: 1.2-2.3 ms full scan vs 0.13-0.31 ms
  incremental per capture (156-399 cells queried instead of 9216 on a 96x96 grid).
- Byte budget (research §2.4.1): `MaxUnreliablePayload - 24` (about 990 B at LiteNetLib's 1024 MTU). Changed budgeted
  entries go in priority order (distance from the receiver, minus 2 m per earlier deferral, 4 m bonus for sight-blocking
  volumes, 2 m for cells with gameplay state bits); deferred ones stay different from the baseline and are offered again.
  Keyframes are partial too (a resume streams its cells over a few snapshots). Only a mandatory part over the budget is
  sent reliably (a worst-case keyframe: 16 deliveries, 6 fields, players, private ≈ 1.3 KB). The L421 worst case fits
  every delta (`SnapshotBudgetTests`); in play the mean body is about 140-150 B.
- Rate: 30 Hz, 20 Hz under congestion (D-115) from application signals (research §2.8.4): median round trip of
  snapshot acks and snapshot loss from the client's received counter over a sliding 4 s window (one lost snapshot of a
  1 s window at 20-30 Hz is already 3-5%); the transport's reliable backlog stays a signal.
- Interpolation: adaptive delay = p95 snapshot lateness (one-way delay plus jitter) + one snapshot interval, clamped to
  50-250 ms and slewed at most 5% of elapsed time (research §2.2.1). On good links it settles at 50-80 ms. A starved
  frame holds the newest snapshot and reports the held tick as the view (§2.2.2).

## Inputs and timing

- `PlayerInputCodec`: tick delta, axes 8 bit, yaw/pitch 16 bit, unified `Casting.InputButtons` held and tapped by value,
  slot, view tick as a lag, grammar events (kind, offset, sub-tick, value, confidence, stroke x/y). Clients predict with
  `Canonicalize`d inputs (exactly what the server decodes).
- Server buffer (`InputReceiveBuffer` via `NetworkInputSource`): one command per step by sequence, dedupe, loss
  substitution, repeat up to 6 ticks then neutral, hard trim at 12 ticks, sustained-surplus cut back to 4 after 0.5 s.
  Skipped commands fold their taps (and buttons held only in them) and grammar events into the next consumed command.
- Closed-loop lead (research §2.1.1): snapshots report the receiver's server queue depth and starvation count; every 8
  snapshots the client compares the smallest queue of the last 15 with the 1-tick target and produces one command less
  (deep queue) or, after starvation with an empty queue in two evaluations, one more. A 2-3 tick hiccup is gone in under
  a second (`InputTimingTests`).
- Grammar events: per-kind back-dating bound at accept (taps, traces, widget picks, stroke ends: 1 tick; syllables and
  stroke samples: the 9-tick rewind bound, equal for every client, L490), clamped and counted, never malformed. Placed at
  client tick + session offset (median of the last 32 step-minus-stamp samples), clamped to [max(previous event,
  step - 9), step], non-decreasing; syllable spacing survives a starvation.

## Lag compensation (spec L423)

- Rewind only the opponent's hit capsule (Sim `HitVolumeHistory`, 9 ticks); everything else is evaluated at the
  present: walls, props, fields, cells, projectiles, shooter origin and aim, the target's guard and facing arc,
  perfect-guard timing, statuses, alive and recall. Beyond the cap the rewind is clamped (the shooter must lead).
- View claim validation: expected view = arrival tick - one-way delay (application RTT / 2) - the client's declared
  interpolation ticks; a claim further than 2 ticks plus measured jitter is replaced by the expectation; views may step
  back by at most 1 tick (rule applied before the hard clamp; reset at duel start and reconnect); repeated or lost
  stand-ins get no lag compensation; hard bounds of `ClampViewTick` last.
- A rewind never crosses a teleport, recall or alcove placement: `HitVolumeHistory` marks a discontinuity when a volume
  moves more than 1.5 m in one tick (cross-edit, see below).
- Measured (virtual clock, GameRig): the rewind is about one-way + interpolation + queue + 1 tick, so shots are fully
  favored up to about 60-70 ms RTT; at 130 ms+ RTT every rewind is clamped at 150 ms (`GameSessionTests` metrics).
- Per-session metrics record the breakdown: claims, accepted, replaced, clamped, backward, stand-ins, mean and max
  rewind, mean one-way, interpolation and queue ticks.

## Interest management and hidden information

- Each client receives only its own duel's snapshots, events, cosmetic cues, duel start, and report; only its own
  shop, draft, and private entry. Spectator frames only outside a live duel, at least 15 s old (L391); other pairs'
  results and standings wait until the recipient's duel ends or the spectator delay passed (research §2.6.4).
- Visibility culling (research §2.6.1): an opponent invisible beyond the 4 m silhouette (DuelWorld.IsVisibleTo, L120)
  is sent as `PlayerBits.Hidden` with the last pose the viewer saw, frozen (no motion, action state, statuses, or
  vitals); the live pose is sent again 1 m plus a quarter second of closing speed before the silhouette radius;
  movement events of a hidden player carry no position. `ClientDuel.OpponentVisible` applies the 4 m rule for drawing.
- Opponent health (research §2.6.3): the exact value is never sent during a duel. Damage events carry amounts only to the
  attacker (its hit confirmation, L349) and the target; damage from anything else reaches the third party without
  amounts; heals, shields, guard rewards stay without amounts; the judgement indicator (L53) and results (L49) are the
  public health channels. Health can be bounded from one's own confirmed hits, as on any damage-number HUD.
- Seeds (research §2.6.2): the match seed defaults to a cryptographic random value; `--seed` needs `--lab` or
  `--allow-fixed-seed`. The arena recipe seed is sent (clients regenerate arenas) and is derived from the match seed
  through SeedHash (not cryptographic); HMAC-derived hidden streams need a Hosting change (gap).
- Clients send intents only (reflection test over every client-to-server body: no positions, vectors, motor states,
  resources, outcomes).

## Server (`Confluence.Server`)

`Confluence.Server [--port 7777] [--bind 0.0.0.0] [--mode duel|conclave] [--players n] [--bots n] [--bot-competence c]
[--seed n --lab|--allow-fixed-seed] [--lab] [--accelerated x] [--spectator-delay s] [--catalogue path] [--log file]
[--threaded-transport] [--max-seconds n] [--stay]`. 60 ticks/s real time (`GameServerRunner`: catch-up at most 8 ticks,
resync after 250 ms behind, tick lateness percentiles logged at stop), the labeled accelerated mode multiplies the pace
(inputs then use `GameProtocol.InputLimitAt`). Reconnect within 90 s resumes the seat (catch-up of settings, phase,
standings, arena, reveal, shop/draft, duel start; baselines restart). Ctrl+C or the time limit shut down cleanly with a
notice. Exit code 2 when the match was refused (standard rules still fail the D-010 floors without `--lab`).

## Client session (`GameClient`)

Connection and auto-reconnect, clock sync, lobby join, commands as idempotent requests, view models
(`ClientMatchView`, `HudView.From(duel)`, `SpectatorView`), per-tick inputs from `InputProvider` with redundancy,
snapshot decode against held baselines, prediction of the own motor with `Sim.CharacterMotor` (`MotorPredictor`: the
server's motor command inputs from the private entry, the predicted own cast's movement factor and sprint lock,
research §2.3.1), reconciliation (replay of unacknowledged commands with their stored cast status after every
snapshot), sub-tick presentation and magnitude-dependent correction easing (§2.3.2; the offset is for the body and
camera only), interpolation of the opponent and entities, predicted grammar overlay (`GrammarPredictor`, rebuilt when
the authoritative phase keeps disagreeing), arena regeneration with hash check and the exact-layout download fallback.

## Headless client (`Confluence.HeadlessClient`)

`Confluence.HeadlessClient [--host 127.0.0.1] [--port 7777] [--name n] [--wizard WIZ-nnn] [--route r] [--competence c]
[--seed n] [--catalogue path] [--max-seconds n] [--status-seconds n] [--no-spectate]`. Exit 0 when the match ended.

## Tests (`tests/Confluence.Net.Tests/Game`)

- `WireMessageTests`, `SnapshotCodecTests`, `SnapshotBudgetTests`, `DecoderFuzzTests`: round trips, malformed bodies,
  prefixes, padding, field-group deltas, budget and partial-frame consistency, L421 worst case, mutation fuzzing.
- `InputTimingTests`: trims and surplus cuts keep syllables and taps, lead control, event bounds, syllable spacing,
  view-tick rules, teleport discontinuity.
- `InterestManagementTests`: a 4-seat Conclave (2 headless clients + 2 bots) over UDP with every send recorded:
  no other pair's duel messages, own shop/draft only (byte-compared to the recipient's view), spectator frames only
  outside live duels and >= 15 s old, no early other-pair results, no third-party damage amounts, no opponent vitals;
  client-to-server bodies carry intents only.
- `GameSessionTests`: a purchase resent with the same request id is charged once; rerolls rate-limited to burst 3;
  commands on the wrong request type refused; prediction converges and view claims stay honest at 40/130/230 ms RTT with
  0-5% loss; a 7 s outage resumes both seats and the match continues; a networked duel over a lossy link replays to the
  same final state hash.
- `ArenaDownloadTests`: layout codec round trip bit for bit; a client without local generation plays on the
  downloaded layout; unannounced arenas are never sent.
- `EndToEndTests`: two headless clients and an in-process server on UDP, accelerated virtual clock, Laboratory
  overrides (pools are still short): shop, draft, alcove, combat, results every round, purchases, drafts, readies,
  reports, placements, and the L193 Crowns (600, then 500 plus 100 for winning the previous round; no bonus after a
  draw or a replay round). Simultaneous lethal outcomes replay the round (attempt 1, straight to the alcove), so rounds
  are distinct round numbers while results and reports count duels; Duel drafts only in rounds 1-6 (L225).
- `CellCaptureTests`: incremental cell capture equals the full scan on every capture of two networked duels and of 4000
  ticks of random Reactions operations (fields, projectiles, impacts, discharges, removals, dust; captured every tick
  and every second tick; cells appear, change and vanish).
- `InputEnvelopeTests`: property test, random valid movement commands (axes, extreme yaw/pitch, held and tapped
  movement buttons, slots) keep both authoritative motors finite, inside the arena, under the dodge/sprint speed and
  the D-024 jump rise; no violations, rejected messages, or oversized packets.
- `EventLatencyTests` (Slow): 40 ms one-way, 10% loss, a real-time paced 20 s combat window: authoritative events
  arrive with p50 about 3 ticks, p99 <= 40 and max <= 90 ticks (the accelerated rig cannot measure this: LiteNetLib
  stamps reliable resends with wall time, hence `GameRig.PaceRealTime`).
- `Transport/LossyLinkTests`: a 200-packet Events burst (over three 64-packet windows) at 10% loss drains in well
  under 2 s (measured 0.27-0.64 s).
- `Transport/DatagramRobustnessTests`: empty and junk datagrams never stall a connect (LiteNetLib #616), manual and
  threaded modes.

## Research checklist status (docs/research/netcode.md §3)

Done: input slip control and starvation recovery; one-shot merge on trims; per-kind event bounds (clamped, counted);
syllable spacing; ClampViewTick tests; expected-view replacement; backward views; stand-ins without lag compensation;
teleport/recall/placement discontinuity; rewind breakdown in metrics; snapshot budget with no over-budget deltas and
reliable fallback only for over-budget keyframes; partial-frame baseline round trip; unchanged tuning and statuses cost
0 bytes; drifting volumes keep their bitmap; cosmetic events off the Events lane; visibility culling; CSPRNG default seed
and refused fixed seeds; opponent health policy (above); leak test; intents-only reflection test; flood kick and input
limit about 2x need; decoder fuzzing (mutation-based); datagram robustness on Windows; congestion signals from
application RTT (median, a lost pong cannot trip it) and snapshot loss; 1 ms Windows timer and tick lateness stats;
per-session metrics JSON line; networked replay reproduces the final state hash; authoritative layout download;
cell capture proportional to reacting cells (benchmarked at 96x96); motor-envelope property test; no oversized
packets; Events burst beyond the window and damage confirmations under 10% loss within a bound.

Not done, with reasons:
- Catalogue ordinals instead of string ids: ids now travel only when their field group changes, so the saving is small;
  ordinals need a shared id table keyed by the ruleset fingerprint (a later wire-format step).
- Implicit layout baseline (frame -1): partial keyframes stream the layout cells within the budget instead; a true
  frame -1 needs the Reactions initial load reproduced bit for bit on the client.
- Received-snapshot ack bitfield and critical events repeated in snapshots: loss is measured from the cumulative
  received counter; critical events stay on the reliable ordered Events lane, which no longer carries cosmetic traffic.
- Spectator delta stream: spectator frames stay full at 10 Hz on Bulk (they are 15 s delayed and not time-critical).
- HMAC-derived Offer/Draft/Bot/Pairing streams: Hosting derives them from the match seed (a Hosting change).
- LiteNetLib pinned at or after d41569e0: 2.1.4 is still the latest NuGet release; the regression test passes on Windows,
  and `--threaded-transport` avoids manual-mode receive on Linux/Mono hosts.
- Replay files with checkpoint hashes: MatchHost records manifests in memory (the networked replay test uses them); a
  file writer and periodic checkpoints belong to Hosting.
- Golden arena hashes in Unity Mono: Stage 4 (the download fallback makes a mismatch non-fatal meanwhile).
- Hit-rate e2e per RTT with a shooter aiming at the interpolated capsule: the view pilot does not aim hitscan; the claim
  acceptance per RTT is tested instead. Guard/projectile present-state checks are Sim behavior (DuelWorld), not changed.
- Bit-exact misprediction metric, rollback-capable cast machine, cosmetic projectile prediction, event scheduling on the
  interpolation timeline: client presentation work for Stage 4.
- Transport authentication/AEAD, per-IP connect limits: before any public server (needs the auth backend decision).

## Interpretations (for DECISIONS at merge)

1. The 150 ms rewind (L423) is the whole stepTick - viewTick distance, interpolation included; shots are fully favored
   up to about 60-70 ms RTT and clamped beyond (the high-ping state is analyzed through the metrics record).
2. Adaptive interpolation delay 50-250 ms (ARCHITECTURE "about 100 ms" is the typical value, not a hard cap); one
   snapshot interval of headroom; starved frames report the held tick.
3. Opponent health is hidden during a duel (policy above); results show both health fractions afterwards (L49).
4. Live duelists receive other pairs' results and standings only after their own duel or 15 s (L391 "restricts
   player-to-live-player" information).
5. Invisible opponents beyond the silhouette are culled server-side; the early reveal margin is 1 m plus 0.25 s of
   closing speed.
6. D-115's 5% loss threshold is measured on snapshot loss over a sliding 4 s window.
7. Input queue target 1 tick, surplus cut above 4 ticks after 0.5 s, input rate limit burst 32 then 1 per 8 ms, flood
   kick after 2000 rate-limited or faulting messages (tuning).
8. Grammar event back-dating: 1 tick for taps/traces/widgets/stroke ends, 9 ticks (the rewind bound) for syllables and
   stroke samples.
9. Standard matches use a cryptographic random match seed; fixed seeds are for Laboratory, custom hosts, and tests.
10. A client that cannot regenerate an arena exactly downloads the server's layout; only a failed download is the
    unsupported-version state (L427).

## Cross-edits outside Net

- `src/Confluence.Sim/Duel/HitVolumeHistory.cs` (additive): discontinuity tracking (`DiscontinuityDistance`,
  `DiscontinuityTick`), `TryGet` never returns a tick before the newest jump.

## Status / next steps

- Status (2026-10-05, after resume): Stage 3 complete on this branch, including the incremental cell capture and the
  remaining research-checklist tests (motor envelope, event latency under loss, Events burst). `dotnet test
  tests/Confluence.Net.Tests` passes (count in the report). Real-time smoke: server + two headless client processes
  over UDP (commands in the report).
- Next: the view pilot rarely finds its opponent on arenas larger than 24 m (pilots often end by time or boundary);
  catalogue ordinals; received-snapshot ack bitfield; spectator delta stream; HMAC-derived hidden streams (Hosting);
  Unity client integration (Stage 4: `GameClient` + `HudView` + `ClientMatchView`, golden arena hashes in Mono).
