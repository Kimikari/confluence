# Confluence.Net foundation

Transport and protocol primitives over LiteNetLib 2.1.4. Game messages (inputs, snapshots, shop and draft
transactions) arrive in Stage 3 once Sim and Match exist; this layer only knows bytes, sessions, and generic
buffers. Depends on Confluence.Core and LiteNetLib only.

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
  rules version string, client build id, display name, reconnect token. The server checks in order: decode,
  protocol version, ruleset fingerprint, required build, display name, then token (resume) or capacity (new).
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
  than 12 ticks ahead, and exposes `LastProcessedSequence` for the snapshot header ack. Late inputs whose
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

- Status: all components implemented and tested; `dotnet test tests/Confluence.Net.Tests` passes.
- Stage 3: define game messages (ids >= 64) for inputs (`InputPacketMessage` with a game input codec), snapshots
  (header + delta-coded entity state against the acked baseline), match flow, shop and draft requests with the
  `NetRules` rate limits; drive a `SnapshotRateSelector` per session from `GetLinkStats`; set
  `ServerTransport.TickClock` to the sim clock; feed spectators through `SpectatorDelayBuffer`.
- Lag compensation (150 ms rewind, L423) belongs to Sim and uses the clock estimate and input acks from here.
