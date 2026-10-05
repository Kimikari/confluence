# Confluence netcode research report

Date: 2026-10-04. Brief: `docs/research/netcode-prompt.md`.

The subject is the paused Stage 3 net-protocol branch `wf_ac402310-4a4-4`, head `16795e0` (2026-10-03, "WIP: salvage
uncommitted work after manual stop (may not compile)"). Code paths below are relative to that worktree
(`.claude/worktrees/wf_ac402310-4a4-4/`). Spec lines are from `docs/confluence` (§14 is L417-L436).

Readers: the developer, the net-protocol verifier, and the Stage 4 Unity client agent.

How this was checked:
- The code was read, not built or run. Byte sizes and timing figures that come from reading encoders are
  estimates and are marked that way.
- I re-checked the main code claims myself against the branch: `ClampViewTick`, `RebaseEvents`, `StepInputs`,
  `RecommendedInputLeadTicks`, `ConsumeNext`/`TrimSurplus`, `SendSnapshot`, `ForViewer`, `ClientArenas`,
  `GameServer.Seed`, `Thread.Sleep(1)`, and the absence of MTU settings.
- LiteNetLib 2.1.4 constants were read by reflection over the NuGet assembly on 2026-10-04: PossibleMtu
  [1024,1164,1392,1404,1424,1432], InitialMtu 1024, MtuDiscovery off, ChanneledHeaderSize 4, FragmentHeaderSize 6,
  DefaultWindowSize 64, MaxFragmentsInWindow 32.

---

## 1. Summary (ranked by risk)

1. **High.** Most of the 150 ms rewind is used up by the fixed 100 ms interpolation delay. The rewind is about
   one-way delay + server input queue + 6..8 ticks, clamped at 9. With an empty queue, shots are fully favored
   only up to about 35-65 ms RTT, and the queue only grows during a session. Make the interpolation delay
   adaptive, control the queue (§2.1.1) and budget the rewind explicitly (§2.2.1).
2. **High.** The server's input slip only grows. Each starvation adds one tick of delay for the rest of the
   session, the client cannot see it, and `TrimSurplus` throws away taps and syllables. Close the loop and merge
   one-shot data on trims (§2.1.1).
3. **High.** The client chooses its `ViewTick` freely within the last 9 ticks, so "backtrack" cheats work. Bound it
   by a latency-based expectation and require it to be monotonic (§2.2.3).
4. **High.** A snapshot that does not fit in a datagram is sent reliably on Bulk, which is a cliff under load. Use
   a per-snapshot byte budget with priority-ordered partial deltas (§2.4.1).
5. **High.** An invisible opponent's exact position, velocity and aim are still sent. Cull them on the server
   using `DuelWorld.IsVisibleTo` (§2.6.1).
6. **High.** The match seed defaults to 1, and the shop and draft streams derive from it with a non-cryptographic
   hash. Use a CSPRNG seed and derive hidden streams from a keyed secret (§2.6.2).
7. **High.** If the arena layout hash does not match, the client is left with no prediction and no geometry, and
   Mono-vs-.NET equality is never tested. Add an authoritative layout download and a cross-runtime golden test
   (§2.7.1).
8. **Medium.** LiteNetLib 2.1.4 manual-mode receive bug #616: an empty datagram, or a packet rejected by a packet
   layer, can stall receiving. Pin a fixed commit before running a public server or adding an encryption layer
   (§2.8.1, §2.6.7).
9. **Medium.** Cosmetic reaction events share the reliable ordered Events lane with damage confirmations. Split
   the lanes and piggyback unacked critical events in snapshots (§2.5.1).
10. **Medium.** Grammar event times can be back-dated by up to 150 ms, and network slip stretches syllable
    cadence. Tighten the decode bound and rebase events in the client's tick frame (§2.1.2).
11. **Medium.** The opponent's exact health is stripped from snapshots but can be rebuilt from `DamageEvent`
    amounts. Decide the policy and enforce it on every channel (§2.6.3).
12. **Medium.** Your own cast's movement slow is not predicted, and the 30/20 Hz switch trusts LiteNetLib's spiky
    RTT. Predict the slow from the predicted cast machine (§2.3.1) and measure RTT and loss at the application
    level (§2.8.4).

---

## 2. Topics

### 2.1 Input buffering, jitter and timing

#### 2.1.1 Close the input-timing loop; never lose one-shot input on trims (risk: high)

**Good practice.**
- Overwatch: the server keeps a small input buffer, and the client adjusts its simulation rate (slightly faster
  after loss to rebuild the buffer, then back) to keep that buffer small.
- VALORANT: the server targets about half a frame of buffered movement and the client about one frame.
- Unreal: movement is validated against server timestamps, not client clocks.

**Branch now.**
- `src/Confluence.Net/Sync/InputCommands.cs` `ConsumeNext` (~L368-400) consumes by sequence, not by stamped tick.
  A starved tick returns `Repeated` with sequence 0 and does not advance `_nextSequence`. When the late input
  arrives, it and every later input run one tick later than stamped, for the rest of the session.
- Nothing shrinks the queue until `TrimSurplus` (~L412) fires at `InputMaxBufferedTicks = 12`
  (`NetRules.cs` L53, 200 ms). It then skips commands and keeps only `_lastKnown`, so `Tapped` edges and
  `GrammarEvents` of the skipped commands are lost.
- `NetworkInputSource.NextInput` overwrites `input.Tick = stepTick`, so the slip is never measured.
- The client's lead (`GameClient.StepInputs`, `src/Confluence.Net/Game/Client/GameClient.cs` ~L247-268) is open
  loop: `ClockSync.RecommendedInputLeadTicks` = ceil((SRTT/2 + 2·RTTVAR)·0.06) + 1
  (`src/Confluence.Net/Sync/ClockSync.cs` L130-137).
- The server never schedules a command by its stamped tick: `InputReceiveBuffer.Accept` starts `_nextSequence`
  at the first sequence it sees, and each step consumes the next sequence. So the lead value itself does not set
  when a command runs; only changes to it after the duel starts matter. A lead increase sends a burst of commands
  that permanently deepens the server queue. A lead decrease causes repeated stand-ins but never shrinks the
  queue. Together with starvation slip, the queue depth only ratchets upward until `TrimSurplus` at 12 ticks.
  This queue depth is the dominant variable term in the rewind (§2.2.1).

**Change.**
1. On the client, after each snapshot compute slip = `snapshot.Tick - LocalTickOf(LastProcessedInput)`. The
   client already stores the stamp tick of every sequence, so no protocol change is needed.
2. Drive the lead with a control loop: dilate local input production by about ±2-5% to hold slip at 0 and the
   server queue near 1 tick plus jitter.
3. On the server, if pending inputs stay above target for more than about 0.5 s, drop the surplus. Before
   discarding commands (both here and in `TrimSurplus`), merge their `Tapped` and `GrammarEvents` into the next
   consumed command.
4. Optional: add a signed "input slack" byte to the snapshot header for diagnostics.
5. Count slip in the rewind budget (§2.2.1).

**Risks.** Permanent added latency after any hiccup. Lost syllables or taps break casts. The exact Overwatch
dilation figures are UNCERTAIN; they come from a secondary summary.

**Sources.**
- Ford, GDC 2017 (netcode section).
- Edgegap summary, 2026-03-17 (secondary; figures UNCERTAIN).
- Riot, "Peeking VALORANT's netcode", 2020-07-28.
- UE 5.8 Character Movement Component networking docs (undated).

#### 2.1.2 Grammar event timing: tight bounds, client tick frame (risk: medium)

**Good practice.**
- Accept client-claimed times only within a small bounded window (Source does this for command ticks).
- Server-side timing must be small and stable. Riot notes that roughly 10 ms advantages matter to skilled
  players.

**Branch now.**
- `src/Confluence.Net/Game/Wire/InputCodec.cs` L30 `MaxEventTickOffset = 600`: event offsets of ±600 ticks from
  their command are accepted.
- `LagCompensation.RebaseEvents` (`src/Confluence.Net/Game/Server/NetworkInputSource.cs` L39-57) keeps
  `e.Tick - clientTick`, adds it to `stepTick`, and clamps to [stepTick-9, stepTick].
- Two consequences:
  - A modified client can stamp a parry, response or rhythm input up to 150 ms earlier than it was sent, after
    seeing a tell.
  - Because of the slip in §2.1.1, a starvation between two commands stretches the server-measured interval
    between their syllables. A rhythm window can then fail because of the network.
- `BeatCadence` (`src/Confluence.Casting/Grammars/Families/BeatCadence.cs`) grades on these event times.

**Change.** Two findings are combined here; they are compatible.
1. At decode, bound each event offset relative to its own command per event kind, not with one rule:
   - `RhythmTap` and button-like kinds: [-1, 0] ticks (sub-tick position plus one tick of slack).
   - `Syllable` and `StrokeSample`: an honest client stamps a syllable at its onset, and the recognizer finishes
     after that (spec L486: "Recognition distinguishes syllable identity and timing"), so the event can be
     several ticks older than the carrying command. Stroke samples batched per frame at 120 fps can also predate
     it. Allow a back-dating bound no larger than the existing rewind clamp (stepTick-9, `RebaseEvents` L49), or
     a calibrated recognizer-latency bound that is documented and equal for every client (L490: "a better
     microphone never grants a wider combat acceptance window").
   - Clamp and count out-of-range offsets instead of throwing. A decode failure is Malformed and counts toward
     `MalformedKickThreshold` (64, `NetRules.cs` L67), so honest jitter must never become a protocol violation.
2. When placing events, rebase them as `clamp(clientEventTick + sessionOffset, max(prevConsumedStep, stepTick-9),
   stepTick)`. `sessionOffset` is a slowly tracked per-client offset (for example the median of
   stepTick - clientTick). Require event ticks to be non-decreasing.
3. Add a test: a 3-tick starvation between syllables must not change the measured cadence.
4. Log per-player timing-error distributions for cadence, beat, parry and response grammars. These feed the
   enforcement model of spec L425, since the server cannot prove a human produced the input.

**Sources.**
- Riot, 2020-07-28.
- Valve `player_lagcompensation.cpp` (file last changed 2025-02-18).
- Spec L490 ("locally timestamped and server-validated under the game's bounded latency compensation").

### 2.2 Lag compensation (hitscan, rays, cones, arcs, melee)

#### 2.2.1 Budget the 150 ms rewind; adaptive interpolation delay (risk: high)

**Good practice.**
- Source computes the rewind as outgoing latency plus the interpolation (lerp) time, clamped to `sv_maxunlag`.
- Riot keeps buffering as small as possible, because every buffered frame adds to peeker's advantage.
- Gaffer sizes the interpolation buffer to survive about two lost packets plus some jitter.

**Branch now.**
- `NetRules.InterpolationDelayMs = 100` (`NetRules.cs` L39) is fixed, so `RenderTick` = estimate - 6 ticks.
- Inputs are stamped at floor(estimate) + lead, but the server ignores the stamp: it consumes one command per
  step by sequence and overwrites `input.Tick = stepTick` (`NetworkInputSource.cs` L138, §2.1.1). A command
  therefore runs roughly when it arrives, plus whatever is already queued.
- `ClampViewTick` clamps the view to stepTick - 9 (`SimRules.LagCompensation.MaxRewindTicks`).
- So the effective rewind is stepTick - view ≈ one-way delay + server queue depth (q) + 6..8 ticks, clamped at 9
  (estimate from reading the code):
  - On a clean LAN link with an empty queue it is about 7-8 ticks.
  - With q = 0, shots are fully favored while one-way + q ≤ about 1-2 ticks, that is up to about 35-65 ms RTT.
  - At 100 ms RTT (one-way about 3 ticks) and q = 0 it is short by about 1-2 ticks (17-33 ms). At 5.5 m/s walking
    speed that is about 0.09-0.18 m.
  - Each tick of q adds one tick of shortfall. q only ratchets upward (starvation slip and lead increases) until
    `TrimSurplus` at 12 ticks, so q, not the lead value, is the dominant term. The fix belongs in the queue/slip
    control loop of §2.1.1.
- `NetworkInputSource.ClampedViewTicks` counts clamps, but nothing reports the count.

**Change.**
1. Make the interpolation delay adaptive:
   - Measure lateness = `EstimateServerTick(arrival) - snapshot.Tick`, using its p95 over about 2 s.
   - Set delay = p95 + headroom, clamped to about 50-100 ms. This does not follow Gaffer: his rule is enough delay
     to lose two packets in a row and still have something to interpolate towards (about 3x the send interval at
     2-5% loss). One interval of headroom (33 ms at 30 Hz, 50 ms at 20 Hz) survives zero lost snapshots.
   - Trade-off, to be decided in §5: either keep at least 2 intervals of headroom when measured snapshot loss is
     above about 1% (at 20 Hz that is 100 ms plus jitter, which conflicts with the 150 ms cap; Gaffer's 3 intervals
     would be 150 ms alone), or accept one interval and define the starved behavior as an accepted cost: a short
     extrapolation limit (for example 1 interval), and the held tick reported to lag compensation (§2.2.2).
     Without that, clients hit `SampleKind.Starved` (`SnapshotBuffer.cs` L198) on every lost snapshot, exactly on
     the congested links that trigger 20 Hz.
   - Change it by time-scaling the render clock a few percent. Never jump.
2. Log a per-client rewind breakdown: one-way delay, server queue depth, interpolation and clamped ticks.
3. Document the RTT up to which shots are fully favored. With adaptive delay, and the server queue held near 1 tick by §2.1.1, this should reach about 80-100 ms.
   Above it, clamp and show the high-ping indicator (spec L423).

**Risks.** Read strictly, the spec's 150 ms includes the interpolation delay (see §5). Overwatch's reported cap of
about 220 ms comes only from a secondary source (UNCERTAIN).

**Sources.**
- Valve `player_lagcompensation.cpp` (file last changed 2025-02-18).
- Riot, 2020-07-28.
- Gaffer, "Snapshot Interpolation", 2014-11-30.
- Edgegap, 2026-03-17 (secondary).

#### 2.2.2 Report the tick actually displayed, with a sub-tick fraction; interpolate rewound capsules (risk: medium)

**Good practice.**
- Source `BacktrackPlayer` computes `frac` between two records and lerps origin, angles and bounds.
- Photon Fusion `SubtickAccuracy` queries the interpolated state with the exact interpolation factor seen when
  the input was polled.

**Branch now.**
- `GameClient.StepInputs` L263-264 sets `ViewTick = floor(duel.RenderTick)` from the previous Update.
  `ClientDuel.UpdateRender` lerps the opponent between snapshots.
- When `SnapshotBuffer` reports `Starved`, the client holds the newest snapshot, but `ViewTick` still claims
  `RenderTick`.
- `HitVolumeHistory.TryGet` (`src/Confluence.Sim/Duel/HitVolumeHistory.cs`) returns whole-tick capsules only.
- The error is up to 1 tick (16.7 ms, about 0.09 m walking), plus the whole gap while starved.

**Change.**
- Send fixed-point view = tick·256 + alpha, taken from the `SnapshotSample` the opponent was actually drawn with.
  When starved or before the oldest snapshot, report the held tick.
- Add `HitVolumeHistory.TryGetInterpolated(player, tick, alpha)`.
- Stage 4: sample the view from the frame that was rendered when the input was sampled.

**Sources.**
- Valve `player_lagcompensation.cpp` (file last changed 2025-02-18).
- Photon Fusion 2 lag compensation docs (undated, accessed 2026-10-04).

#### 2.2.3 Abuse limits on client-claimed view ticks (risk: high)

**Good practice.**
- Source ignores the client's tick if it differs from its latency-based target by more than 0.2 s, and uses the
  target instead.
- Mirror: "Lag Compensator checks this object's latency and then rewinds target's history by that time". The
  page does not say whether that latency is server-measured or client-reported (UNCERTAIN); Source's behavior
  above is the main support for replacing client claims.
- Riot caps the rewind so a high-latency player cannot kill someone already behind cover.
- The Valve wiki rule: command time = server time - packet latency - client interpolation.

**Branch now.**
- `ClampViewTick` (`NetworkInputSource.cs` L27-37) accepts any value in [stepTick-9, min(stepTick-1,
  NewestShownTick)]. `NewestShownTick` is the newest snapshot sent, not received (`GameServer.cs` L811).
- The codec allows view lags up to 600 ticks before clamping (`InputCodec.cs` L27).
- A modified client can choose, per command, whichever of the last 9 ticks gives a hit.
- The same `ViewTick` is used by:
  - melee: `ResolveStrike` (`src/Confluence.Sim/Duel/DuelWorld.cs` ~L573) reads it on every active strike tick;
  - rays, cones and arcs, through `SpellContext.TargetVolume`.
- Stand-in commands (repeated or lost) keep the old `ViewTick`.

**Change.**
1. Compute `expectedView = arrivalTick - RTT/2 - interpTicks`. The interpolation ticks are the client's declared
   delay, bounded to a legal range.
2. Accept the client's view only within ±2 ticks plus measured jitter of that expectation. Otherwise use
   `expectedView`. This is much tighter than Source's 200 ms.
3. Require view ticks to be non-decreasing per client, allowing a 1-tick step back. Reset on reconnect and duel
   start.
4. Keep the existing hard bounds.
5. Give stand-in commands no lag compensation.
6. Log replaced claims, and a per-player histogram of claimed minus expected rewind. A player who always sits at
   the clamp is a review signal.

**Sources.**
- Valve `player_lagcompensation.cpp` (file last changed 2025-02-18).
- Valve wiki "Lag Compensation" and "Source Multiplayer Networking" (undated; HTTP 403 to automated fetch, so
  content came from the SDK code and search excerpts).
- Mirror lag compensation docs (undated, mentions "As of February 2024"; accessed 2026-10-04).
- Riot, 2020-07-28.

#### 2.2.4 Never rewind across teleports, recalls or alcove placement (risk: medium)

**Good practice.** Source drops history ("lost track") when a record jumps more than
`sv_lagcompensation_teleport_dist`, and only uses records marked `LC_ALIVE`.

**Branch now.**
- `HitVolumeHistory.Record` and `TryGet` store only capsules.
- `DuelWorld.TryTeleport` (~L1258) moves a player up to 10 m (spec L124), and `BeginRecall` moves them to a
  platform. The old capsule stays hittable for up to 150 ms.
- `IsTargetable` is checked only at the present.

**Change.**
- Store a per-tick discontinuity flag, set by teleport, recall, alcove placement, death and respawn, and an
  alive/targetable bit.
- `TryGet` returns the earliest tick after the newest discontinuity that is no newer than the requested tick.
- Sim test: teleport 10 m, then hitscan with a 5-tick-old view must miss the old position.

**Sources.** Valve `player_lagcompensation.cpp` (file last changed 2025-02-18).

#### 2.2.5 Document the favor-the-shooter / favor-the-target split (risk: low)

**Good practice.**
- Rewinding trades "shot after reaching cover" for shooter accuracy (Gambetta).
- Overwatch favors the shooter, with exceptions. The exact exceptions were not verified (UNCERTAIN).

**Branch now.** Already consistent with spec L423:
- `RaycastOpponent` and `ResolveStrike` (`DuelWorld.cs` ~L1515-1550, ~L573-615) test against the present
  `CollisionWorld` and only swap in the rewound capsule.
- `SpellContext.TargetVolume(lagCompensated)` is false for projectiles, fields and waves.
- Guard and facing are evaluated in `ApplyDamage` at the present.
- `tests/Confluence.Sim.Tests/Duel/LagCompensationTests.cs` covers history depth, rewind and present solids
  (`Solids_Are_Tested_As_They_Are_Now`), but not guard or facing, and not the Net-side clamp.

**Change.** Write the policy into `docs/notes/net.md`:
- Rewind only the opponent's capsule pose.
- Evaluate everything else at the present: walls, props, fields, cells, projectiles, shooter origin and aim, the
  target's guard, its 120° facing arc, perfect-guard timing, statuses, alive and recall.
- Beyond the cap, clamp (the shooter must lead).

Add tests: a guard raised after the view tick reduces damage; a projectile uses present volumes.

**Sources.**
- Gambetta, "Lag Compensation" (undated, accessed 2026-10-04).
- Overwatch "Let's Talk Netcode" developer update video, 2016-04-05.
- Kotaku, 2016-04-05.

#### 2.2.6 Defender reaction budget for windups and telegraphs (risk: medium)

**Good practice.**
- Interpolation delay is added latency for everything remote (Gaffer 2014).
- Riot measured peeker's advantage at about 141 ms at baseline and reduced it by minimizing buffers.

**Branch now.**
- Remote bodies are drawn 100 ms behind. `ClientDuel.OnEvents` (~L366) applies events immediately.
- There is no presentation-timing policy and no reaction-time metric.
- Example: a 0.18 s butt-strike windup (spec L102) leaves roughly 180 - 100 - (33..67) ms to react, which is
  below human reaction time. The 120 ms perfect guard (L104) is shorter still.

**Change.**
1. The adaptive delay from §2.2.1 helps directly.
2. Design option (UNCERTAIN, no primary source; needs playtesting): show threat telegraphs (melee Started, cast
   windup) when the reliable event arrives, while the body stays on the interpolated timeline.
3. Carry windup state in snapshots as well, so a lost reliable packet does not delay the telegraph. A
   head-of-line stall lasts about one RTT.

This conflicts with the general rule in §2.5.2 that remote cues play on the interpolation clock. Decide
explicitly (§5).

**Sources.** Riot, 2020-07-28; Gaffer, 2014-11-30.

### 2.3 Prediction: what to predict and how to present it

#### 2.3.1 Predict your own motor modifiers from the predicted cast machine (risk: medium)

**Good practice.**
- Unreal `SavedMoves` store per-move state and replay it after a `ClientAdjustPosition` correction.
- Overwatch predicts and replays abilities with a deterministic shared simulation.

**Branch now.**
- `MotorPredictor.Step` (`src/Confluence.Net/Game/Client/MotorPredictor.cs`) reads `CastSpeedFactor`, `Locks`,
  `SpeedMultiplier` and `Surface` from the latest `PrivateEntry`.
- `ClientDuel.Produce` steps the motor before the grammar, and `Reconcile` (`ClientDuel.cs` L227) replays every
  unacknowledged command with a single context.
- So your own cast slows are predicted at full speed for about RTT plus one snapshot interval, then corrected.
  That is tens of cm per cast start or end (estimate).

**Change.**
- Feed the `GrammarPredictor`'s predicted casting status into each predicted motor step: movement factor,
  channel sprint suppression, guard-release dodge lock, and melee and swap locks.
- Store that context per tick in the sent-input ring and replay with it. Correct it from the authoritative
  context only for the acknowledged tick.
- Do not predict opponent- or world-caused effects: slows, stuns, knockback, reaction surfaces, boundary damage.

**Sources.**
- UE 5.8 CMC networking docs (undated).
- Ford, GDC 2017.

#### 2.3.2 Correction smoothing and 120 fps presentation (risk: medium)

**Good practice.**
- Interpolate between the previous and current state with alpha = accumulator/dt (Gaffer 2004).
- Decay the visual error offset per frame, about 0.95 for small errors and 0.85 for large ones, blended by
  magnitude (Gaffer 2015).
- Unreal smooths corrections, uses a `MAXPOSITIONERRORSQUARED` tolerance and rate-limits client corrections.

**Branch now.**
- `ClientDuel` keeps only the latest `Predicted` state. `RenderPosition = Predicted.Position + CorrectionOffset`.
- The offset decays with a 50 ms half-life (`ClientDuel.cs` L36, L328) and snaps above 3 m (L33).
- A 60 Hz prediction shown at 120 fps therefore draws each tick twice, which judders.
- The headless client prints only `MaxCorrection` (`src/Confluence.HeadlessClient/Program.cs` ~L111).

**Change.**
1. Keep the previous and current `MotorState` and lerp them by sub-tick alpha.
2. Apply the correction offset only to the rendered body and camera, never to the aim or shot origin.
3. Use magnitude-dependent decay. Snap or fast-blend when the correction coincides with a server displacement,
   teleport or recall event.
4. Report corrections per second and p95 correction size.

**Sources.**
- Gaffer, "Fix Your Timestep", 2004-06-10.
- Gaffer, "State Synchronization", 2015-01-05.
- UE 5.8 CMC docs (undated).

#### 2.3.3 Projectiles cosmetic-predicted; areas, reactions and statuses authoritative (risk: medium)

**Good practice.**
- Halo: Reach is said to throw the grenade locally while asking the host to throw it too (UNCERTAIN: no source
  describing this was found; the UCL summary does not mention grenades, and the GDC 2011 talk was not
  transcribed).
- Netcode for GameObjects "action anticipation": play non-gameplay animation and VFX on input immediately, and
  wait for the server for gameplay effects. NGO has no built-in server rewind.

**Branch now.**
- Your own projectiles appear only through `ClientDuel.TryGetEntityRenderPosition` at `RenderTick`. That is
  about one-way delay + server queue + 100 ms + one frame after release, roughly 140-170 ms at 60 ms RTT (estimate).
- Damage is shown only from authoritative events, and statuses only from snapshots. Both are correct.

**Change.**
- Spawn a local cosmetic projectile from the predicted muzzle, tagged with (cast id, local tick). Hand it over to
  the server entity when that arrives. Destroy it after about RTT + 100 ms if unconfirmed.
- Never show an impact, damage number, reaction cell or status before the server's event.
- For area spells, predict only placement preview, reticle, and your own windup animation and sound.
- Opponent projectiles and areas stay on the interpolated timeline.

**Sources.**
- Aldridge, "I Shot You First", GDC 2011.
- NGO 2.11 "Dealing with latency" (undated).

#### 2.3.4 Rollback-capable cast machine instead of rebuild-on-mismatch (risk: medium)

**Branch now.**
- `GrammarPredictor` (`src/Confluence.Net/Game/Client/GrammarPredictor.cs`) compares only busy vs idle at
  `LocalTickOf(ack)`.
- After 3 disagreeing snapshots it calls `Rebuild()`, which creates an idle machine and drops the in-progress
  sequence, even though `PrivateEntry` carries `MachinePhase`, `MachineSpellId`, `MachinePhaseEndTick` and the
  grammar UI state.

**Change.**
- Give `CastMachine` (Confluence.Casting) Save/Restore, or send enough state in `PrivateEntry`. Then reconcile
  casting like motion: restore at the snapshot tick and replay the unacked commands with their grammar events.
- Compare phase, spell id and phase end tick.
- While a rollback is impossible, show the authoritative `GrammarUiState`.
- Show mana spent and cooldown start as "pending" until the Cast event confirms them.
- Never predict mastery outcomes, refunds, or grammars triggered by server combat events.

**Sources.** Ford, GDC 2017; NGO 2.11 docs (undated).

### 2.4 Snapshots: budget, baselines, deltas, reaction world

#### 2.4.1 Per-snapshot byte budget with priority-ordered partial deltas (risk: high)

**Good practice.**
- Tribes ghost manager, and the Gaffer priority accumulator: entries that do not fit carry their priority to the
  next packet.
- Netcode for Entities clamps snapshots to the MTU and sends partial snapshots by importance.
- Halo: Reach (UNCERTAIN): Aldridge GDC 2011, as summarized by UCL: reliable and unreliable message classes allow
  traffic prioritization. The talk itself was not transcribed.
- Reliable resends deliver stale data: "When the resent packet finally arrives, you receive this stale, out of
  date information that you don't even care about!" (Gaffer, "UDP vs. TCP", 2008).
- If time-critical data must be fragmented, fragment it unreliably, and only in the 2-4 fragment range (Gaffer,
  "Packet Fragmentation and Reassembly", 2016).

**Branch now.**
- `GameServer.SendSnapshot` (`src/Confluence.Net/Game/Server/GameServer.cs` L791-812) encodes the full keyed
  delta. If the body exceeds `MaxUnreliablePayload - 24` (about 996 B at MTU 1024), it sends the same body as
  `SnapshotReliable` on Bulk (ReliableUnordered, fragmented).
- `SnapshotCodec.EncodeBody` writes every changed entry. There is no budget or priority.
- Estimate: the fixed part (PrivateEntry about 240 B, two PlayerEntry at 40-80 B) is about 350-450 B. About 100
  changed cells, or one moving large volume, overflows the limit.

**Change.**
1. Set a hard budget: `GetMaxSinglePacketSize(Sequenced)` minus header, about 990 B.
2. Pass 1 (mandatory): header, both players, private state, all removals, all solid changes (prediction collision
   depends on them), entity spawns.
3. Pass 2 (priority accumulator until the budget is used): entity updates, volumes, props, cells. Deferred entries
   stay dirty and keep their priority.
4. Use reliable Bulk only for a true keyframe that cannot fit. §2.4.3 makes that rare.
5. Alternatives from the LiteNetLib finding: a second sequenced message for cells and volumes with its own
   baseline ack, or 2-4 unreliable fragments reassembled by tick.
6. Later: a per-client bytes-per-second token bucket inside the 30/20 Hz selector.

**Sources.**
- Gaffer, "State Synchronization", 2015-01-05.
- Gaffer, "Snapshot Compression", 2015-01-04.
- Gaffer, "Packet Fragmentation and Reassembly", 2016-09-06 (2-4 fragment guidance only).
- Gaffer, "UDP vs. TCP", 2008-10-01 (stale reliable resends).
- Tribes networking model, c. 1999 (year uncertain).
- Netcode for Entities 1.5 ghost-snapshots and optimizations pages (undated).
- Aldridge, GDC 2011.
- LiteNetLib 2.1.4 reflection check, 2026-10-04.

#### 2.4.2 Baseline = what the client actually holds; ack bitfields (risk: medium)

**Good practice.** Quake 3, Source and Gaffer delta against the newest acknowledged snapshot. Gaffer's
sequence/ack/ack_bits pattern makes loss visible and can confirm piggybacked messages.

**Branch now.**
- `GameServer.Stream` (~L1160-1203) keeps 64 sent frames. `Remember(frame)` stores the projected full frame, which
  is correct only because every delta is complete today.
- The client acks a single tick, `AckTick => Latest.Tick` (`ClientDuel.cs` L157, sent at L301).
- Congestion loss comes from LiteNetLib statistics. In 2.1.4 `PacketLoss` counts inbound sequenced gaps
  (`SequencedChannel.ProcessPacket` calls `Peer.Statistics.AddPacketLoss(relative - 1)`) as well as reliable
  losses, so the branch's `LossWindow` (`ServerTransport.cs` L584-614) divides a mixed-direction loss count by
  outbound `PacketsSent` (see §2.8.4).

**Change.**
- With partial snapshots, `Remember` must store the baseline plus the applied upserts and removals.
- Add a 32-bit received-snapshots bitfield next to `AckSnapshotTick` in `DuelInputMessage`.
- Keep a history of at least 64 frames.
- When the acked baseline expires, fall back to the layout baseline (§2.4.3), not to an empty frame.

**Sources.**
- Fabien Sanglard, "Quake 3 network model", 2012-06-30.
- Valve wiki "Source Multiplayer Networking" (undated).
- Gaffer, "Reliable Ordered Messages", 2016-09-15.
- Gaffer, 2015-01-04.
- `SequencedChannel.cs` at tag 2.1.4 (loss counting).

#### 2.4.3 Implicit layout baseline for first snapshots, resumes and expiry (risk: medium)

**Branch now.**
- `EncodeBody(frame, null, own)` writes everything.
- `FrameCapture.CaptureCells` (`src/Confluence.Net/Game/Snapshots/FrameCapture.cs`) includes the natural layout
  material cells. Every prop (up to 48) is written with a string id.
- On resume, `ResendState` calls `ResetBaseline()` (`GameServer.cs` ~L573-605), so the next snapshot is full and
  usually reliable.

**Change.**
- Define a deterministic frame -1 that both ends build from public data: layout material cells as
  `ReactionWorld` loads them, initial props, and no extras. The client already builds `CellGrid.ForArena(layout)`
  in `AttachLayout`.
- Encode keyframes against it.

**Sources.**
- Gaffer, 2015-01-04 (encode against the initial state until the first ack).
- Quake 3 (zeroed dummy snapshot), 2012-06-30.
- Valve wiki (undated).

#### 2.4.4 Shrink fixed cost: field-group ChangeMasks and catalogue ordinals (risk: medium)

**Branch now.**
- `PlayerEntry` and `PrivateEntry` (`src/Confluence.Net/Game/Snapshots/DuelFrame.cs`) are written in full in every
  snapshot. This includes the motor as raw floats (about 95 B) and the full `MotorTuning` (about 41 B).
- Ids are UTF-8 strings up to 64 B (`WireIO.WriteId`).
- `EntityEntry.SameAs` compares all fields, so a moving projectile resends kind, owner, spell and radius.
- `ChangeMask` (`src/Confluence.Net/Serialization/DeltaCoding.cs`) is unused.

**Change.**
- Use per-group ChangeMasks. Send the motor always (exact floats). Send vitals, cooldowns and grammar UI when
  changed. Send `MotorTuning` and the status list only when they change.
- Replace string ids with varint catalogue ordinals. The ruleset fingerprint guarantees identical catalogues.
- Split immutable entity fields (sent only when the key is new) from moving fields. Use predictive delta only
  where motion is predictable.
- Estimated saving: 150-250 B per snapshot.

**Sources.**
- Gaffer, 2015-01-04.
- Tribes, c. 1999.
- Netcode for Entities 1.6 optimizations (undated).
- UE 5.8 `FFastArraySerializer` API (undated).

#### 2.4.5 Reaction grid as prioritized dirty tiles (risk: medium)

**Branch now.**
- `FrameCapture.CaptureCells` scans every cell of a 48x48 to 96x96 grid each capture, with six queries per cell.
- `CellEntry.WriteBody` (`src/Confluence.Net/Game/Snapshots/FrameEntries.cs`) costs about 4 B per changed cell
  (estimate).
- `KeyedSection.ReadDelta` rebuilds a `SortedDictionary` of all baseline cells for every snapshot, which allocates
  heavily for Unity.
- There is no priority.
- `MotorPredictor` (~L96-100) uses only the `PrivateEntry` surface and never reads cells.

**Change.**
- Encoding: 8x8-cell tiles (4 m x 4 m). Per changed tile send the tile index, a 64-bit changed-cell bitmap, then
  per cell a 7-bit material mask, 3 bits per present material (units 0..4, spec L136) and 5 state bits. That is
  about 2 B per cell, and less for bursts.
- Priority:
  - Tiles within about 4-6 m of the receiver go every snapshot.
  - Tiles with Ice, Burning, Charged or Wet bits rank above pure unit-count changes.
  - Far tiles may wait.
- Capture from the dirty set or `ActiveRegions` that Reactions already keeps, not a full scan.
- Do not move to client-side deterministic simulation of the reaction world now (cross-runtime floats are
  unproven, §2.7).

**Sources.**
- Gaffer, 2015-01-05.
- UE 5.8 `FFastArraySerializer` (undated).
- Netcode for Entities 1.5 ghost-snapshots (undated).
- Factorio FFF-188, 2017-04-28 (the lockstep alternative).

#### 2.4.6 Separate volume motion from volume shape (risk: medium)

**Branch now.**
- `VolumeEntry.SameAs` compares offsets, extent, expiry and the full bitmap, so wind drift (up to 3 m/s per
  `docs/notes/reactions.md`) resends `WriteBlob(Bits)` every snapshot.
- `WireLimits.MaxVolumeBitmapBytes = 4096`. A volume covering a 96x96 grid is 1152 B, which is already over one
  datagram.

**Change.**
- Split the entry into ChangeMask groups: motion (offset plus drift velocity for extrapolation), extent,
  flags/expiry, and shape.
- Send the shape only on change, as an RLE-coded XOR against the baseline.
- Cap shape bytes per snapshot and stream large shapes over several snapshots.
- Rank volumes near the line between the players above cells, because steam blocks targeting (L167).

**Sources.** Tribes, c. 1999; Gaffer, 2015-01-04.

#### 2.4.7 Delta-encode the spectator stream (risk: low)

**Branch now.**
- `OnDuelStepped` (~L752-779) encodes a full frame with no baseline at `SpectatorRateHz = 10`
  (`GameServer.cs` L67).
- `ReleaseSpectators` sends it on Bulk (ReliableUnordered) to every eligible spectator. Frames are usually
  multi-fragment, and only 32 fragments can be in flight per window.

**Change.**
- Encode each released frame once per duel as a delta against the previous released frame.
- Use an ordered lane, or include the baseline tick.
- Send a keyframe against the layout baseline when a spectator joins or switches duels.

**Sources.** Gaffer, 2015-01-04; LiteNetLib 2.1.4 constants (2026-10-04).

### 2.5 Events vs snapshot state

#### 2.5.1 Split critical and cosmetic events; flush at snapshot cadence; piggyback (risk: medium)

**Good practice.**
- Tribes offers four delivery classes: non-guaranteed, guaranteed, most-recent-state, guaranteed-quickest.
- Halo: Reach sends unreliable events with prioritization.
- Quake 3 resends reliable commands in every packet until acked (Gaffer 2016 uses the same pattern).

**Branch now.**
- `FlushEvents` (`GameServer.cs` L814-834) sends a `DuelEventsMessage` on the ReliableOrdered Events lane every
  tick that has events.
- `DuelEventFilter.Convert` (`src/Confluence.Net/Game/Server/DuelEventFilter.cs`) appends every `ReactionEvent`
  (Id 0) to both players.
- Consequences:
  - A lost cosmetic packet delays later damage confirmations by at least one resend delay. LiteNetLib's resend
    delay is 25 + 2.1 x RTT ms, about 235 ms at 100 ms RTT.
  - The backlog inflates `ReliableQueued`, which wrongly drops snapshots to 20 Hz.

**Change.**
- Critical events stay reliable ordered: casts and telegraphs, damage confirmations, DuelEnd, field and solid
  creation and destruction.
- Cosmetic events go on the snapshot path or unreliably, or are derived from state.
- Batch events at snapshot cadence in the same flush, so LiteNetLib merges them into the snapshot's datagram.
- Copy unacked critical events into each snapshot by event id until a snapshot ack (§2.4.2) covers them. The
  client dedupes by id.
- Cap events per tick. A reliable channel has a 64-packet window.

**Sources.**
- Tribes, c. 1999.
- UCL summary of Halo: Reach, 2011-05-13.
- Aldridge, GDC 2011.
- Gaffer, 2016-09-15.
- Quake 3, 2012-06-30.
- LiteNetLib `LiteNetPeer.cs` (master, accessed 2026-10-04).

#### 2.5.2 Snapshots own persistent state; events are one-shot cues on the right timeline (risk: medium)

**Branch now.**
- `ClientDuel.OnEvents` (~L366-390) raises `EventReceived` on arrival, with no scheduling by tick.
- Several events duplicate state: StatusEvent vs `PlayerEntry.Statuses`, FieldEvent vs `OwnedEntry`, SolidEvent
  vs `SolidEntry`, PropEvent vs `PropEntry`.

**Change.**
- Render statuses, fields, solids, props, cells, volumes, health and phase only from snapshots.
- Events drive only one-shot cues: hit markers, numbers, sounds, VFX.
- Queue remote and world events until the interpolation render tick reaches `event.Tick`. Play the local
  player's own confirmations on arrival.
- This is engineering judgment consistent with Halo: Reach (UNCERTAIN; no primary source specifies it). See the
  telegraph exception in §2.2.6.

**Sources.** UCL Halo: Reach summary, 2011-05-13; Tribes, c. 1999.

### 2.6 Hidden information and security

#### 2.6.1 Server-side visibility culling (risk: high)

**Good practice.** VALORANT fog of war withholds enemy state until it is needed. It uses a PVS plus velocity x
look-ahead box expansion so peeks do not pop in, and costs under 2% of server frame time.

**Branch now.**
- `FrameCapture.ForViewer` (`FrameCapture.cs` L60-67) strips only the opponent's vitals. `CapturePlayer` (~L106)
  still sends Position, Velocity, Yaw, Pitch, cast phase and statuses with just `PlayerBits.Invisible` set.
- `DuelWorld.IsVisibleTo` (`DuelWorld.cs` L1486, the 4 m silhouette rule of spec L120) is never called by the
  net layer.
- `DuelEventFilter` sends `MovementEvent` positions to both sides.
- The headless `ViewPilot` (`Game/Headless/ViewPilot.cs` ~L260) has to skip the opponent by checking the bit.

**Change.**
- When `IsVisibleTo(viewer, other)` is false, send a Hidden opponent entry with no pose (or a frozen last-visible
  pose plus a flag), keeping only the bits the spec makes public.
- Filter positions in events too (`MovementEvent`, and `AimOrigin` while the caster is hidden).
- Send footsteps (L120 keeps them audible) as coarse audio cues.
- Reveal slightly before 4 m, using velocity x a look-ahead longer than the RTT.
- Optional, behind a flag: freeze the opponent when the sight path through steam is at least 2 m or crosses a
  sight-blocking solid (L167, L349). Measure pop-in.
- Lag compensation is unaffected, because it runs on the server.

**Sources.** Riot, "Demolishing wallhacks with VALORANT's fog of war", 2020-04-14; Riot, 2020-07-28.

#### 2.6.2 Unpredictable hidden RNG streams (risk: high)

**Good practice.** The 1999 online poker break showed that a seeded deterministic shuffle with a guessable seed
exposes every hidden card. Derive secrets with a keyed one-way function (HMAC, RFC 2104).

**Branch now.**
- `GameServer.Seed` defaults to 1 (`GameServer.cs` L49). `Confluence.Server` `--seed` also defaults to 1
  (`src/Confluence.Server/Program.cs` L146).
- Shop and draft offers derive from `MatchSeed` (`src/Confluence.Hosting/Economics/HostedEconomy.cs`).
- The arena seed comes from `RngStreams.SeedFor(matchSeed, Arena, ...)` (a SplitMix/FNV `SeedHash`, not
  cryptographic) and is sent to clients as `RecipeSeed`.
- With a guessable seed, a client running the shared economy code can rebuild every player's offers.
  `EconomyViews` correctly sends offers only to their owner, but the seed undoes that. Whether a random 64-bit
  seed can be inverted from `RecipeSeed` is UNCERTAIN.

**Change.**
- Use a CSPRNG secret seed (64-bit, preferably 256-bit) outside Laboratory, Custom and replay modes. Refuse
  hand-picked seeds elsewhere.
- Derive Offer, Draft, Bot and Pairing streams as HMAC-SHA256(secret, matchId‖stream‖round‖sub).
- Derive Arena from a separate public root.
- Publish the secret only in post-match replay manifests.
- Test that no server-to-client message carries a value derived from the secret.

**Sources.**
- Arkin et al., "How We Learned to Cheat at Online Poker", 1999.
- RFC 2104, February 1997.

#### 2.6.3 Opponent health: decide, then enforce everywhere (risk: medium)

**Branch now.**
- `PlayerEntry.WithoutVitals` (`DuelFrame.cs`) hides the opponent's health, max health and shield.
- But `DuelEventFilter` sends `DamageEvent` HealthLost and ShieldAbsorbed in full to both sides, and
  `DuelEndEvent` carries exact health fractions. Max health is known from the loadout, so the hiding is not
  effective.
- Intent is UNCERTAIN. The "equal/ahead/behind" indicator (L53) suggests hidden.

**Change.**
- Record the decision in DECISIONS.
- If hidden: the attacker gets a hit confirmation with at most a coarse bucket, and exact fractions move to the
  post-duel report.
- If public: remove `WithoutVitals`.
- Add a leak test (§2.6.6).

**Sources.** Riot, 2020-04-14 (withhold until needed).

#### 2.6.4 Spectator-delay side channels (risk: low)

**Branch now.**
- `GameServer.RouteMatchEvent` sends `DuelResolvedEvent` (with Health0/Health1) and Standings to all seats
  immediately, including live duelists.
- The same duel's spectator frames are held 15 s (L391).
- Whether real-time standings are intended is UNCERTAIN (L49, L391).

**Change.**
- Hold other pairs' results and standings until the 15 s release, or until the recipient's own duel ends.
- Confirm that the release uses match ticks, so a server stall cannot shorten the delay.
- Keep `--accelerated` out of ranked.

**Sources.** Riot, 2020-04-14.

#### 2.6.5 Clients send intents only: lock it in with tests (risk: low)

**Branch now.** Sound.
- `InputCodec` reads only axes, yaw and pitch, defined button bits, a range-checked slot, view lag and grammar
  events. Combat events are never read from clients.
- `NetworkInputSource` strips taps and events from stand-ins.
- Purchases go through `RequestLedger` dedupe.
- `Stream.Acknowledge` accepts only ticks that were sent.

**Change.**
- Add a reflection test over every ClientToServer message and request type, rejecting authority fields
  (positions, outcomes, resources).
- Add a property test: any byte-valid `DuelInputMessage` keeps server state inside the motor envelope (spec L429).

**Sources.** Valve wiki "Source Multiplayer Networking" (undated); Ford, GDC 2017.

#### 2.6.6 Automated hidden-information leak tests (risk: medium)

**Branch now.**
- `DuelEventFilter` filters by audience and fails closed for unknown event types. Spectating is refused while in
  a live duel. `SpectatorDelayBuffer` enforces at least 15 s. `EconomyViews` sends to the owner only.
- Tests: `SpectatorDelayBufferTests`, plus one `EndToEndTests` case with no who-received-what assertions.

**Change.** Generalize the `GameServer.Sent` hook to every SendTo, Broadcast and spectator send. Run 8-seat
matches and assert, per recipient, that it never receives:
- another seat's shop or draft state;
- another pair's frames or events while the recipient is in a live duel;
- spectator frames less than 15 s old;
- the opponent's private entry, mana, cooldowns, grammar UI or prepared spell;
- an invisible opponent's position;
- opponent health beyond the chosen policy.

Run these tests on every PR.

**Sources.** Riot, 2020-04-14.

#### 2.6.7 Transport hardening before any public server (risk: medium)

**Good practice.**
- netcode.io: encrypted connect tokens carrying per-session keys, AEAD packet encryption, and responses to
  unauthenticated requests no larger than the request (anti-amplification).
- LiteNetLib ships only `Crc32cLayer` and `XorEncryptLayer`. XOR is obfuscation, not security.

**Branch now.**
- `ServerTransport` disables unconnected messages. The handshake checks protocol, fingerprint, build and name.
  Reconnect tokens come from a CSPRNG (`Protocol/ReconnectToken.cs`).
- There is no authentication, no packet layer and no per-IP request limit.
- The 128-bit token travels in plain text, so an on-path attacker can take over the session.
- `DispatchOutcome.IsViolation` (`Protocol/MessageRegistry.cs`) excludes RateLimited and HandlerFault, so floods
  are never kicked.
- The input rate limit allows a burst of 480 and one message per 2 ms, about 8x the need.

**Change.**
1. Backend-issued connect tokens checked in `OnConnectionRequest`.
2. AEAD (ChaCha20-Poly1305 or AES-GCM) in a custom `PacketLayerBase`, with sequence-number nonces and a replay
   window. Whether these APIs are available under Unity Mono/IL2CPP for netstandard2.1 is UNCERTAIN. This step
  depends on the #616 pin (§2.8.1) or `ManualMode = false`: on 2.1.4 in manual mode, every packet the layer
  rejects (Size = 0) ends the receive loop early and is never returned to the pool, so a forged-packet flood
  would stall receiving and create GC garbage. Without the pin, the AEAD layer adds a denial-of-service path.
3. Per-IP connect rate limit before decoding, and responses no larger than requests (`ConnectionRejection` can
   carry 200 chars plus fingerprints today).
4. Count sustained RateLimited and HandlerFault toward a kick threshold. Tighten the input limit to about 2x.
5. Log account id, endpoint and violations per session (L429).

**Sources.**
- netcode 1.02 STANDARD.md (accessed 2026-10-04).
- LiteNetLib README and Layers (release 2.1.4, 2026-05-19).
- LiteNetLib issue #13, opened 2017-01-06.

#### 2.6.8 Fuzz every decoder (risk: low)

**Branch now.**
- Decoding is strict: `NetReader` uses strict varints, finite floats and capped counts, and `ExpectEnd` is
  enforced.
- Tests are hand-written (`tests/Confluence.Net.Tests/Transport/MalformedPacketTests.cs`,
  `Game/WireMessageTests.cs`). There is no fuzzing.

**Change.**
- Add SharpFuzz harnesses for the handshake, `DuelInputMessage`, command and spectate requests, and the
  client-side snapshot and frame decoders.
- Assert that only `NetReadException` is thrown and that allocations stay bounded.
- Seed the fuzzer with soak traffic. Run briefly in CI and longer nightly.

**Sources.** SharpFuzz v2.2.0, 2025-01-11.

### 2.7 Determinism: .NET 10 server vs Unity Mono client

#### 2.7.1 Arena rebuild: authoritative fallback plus a golden cross-runtime test (risk: high)

**Good practice and evidence.**
- sin and cos are not defined by IEEE, and FMA and compiler settings change results (randomascii 2013; Gaffer
  2010).
- `Math.Sin` calls the C runtime and may differ by OS or architecture (Microsoft Learn, updated 2026-07-01).
- Unity states that CoreCLR results "may be a little different" from Mono's JIT (2026-03-26).
- Mono historically did float32 math in 64-bit (2018). Whether Unity's Mono fork does today is UNCERTAIN.

**Branch now.**
- Arena generation uses float transcendentals:
  - `src/Confluence.Arena/Generation/BoundaryBuilder.cs:41` (MathF.Cos);
  - `src/Confluence.Arena/Navigation/ArenaNav.cs:179` (MathF.Tan);
  - `src/Confluence.Core/Geometry/CMath.cs:165-168` (Math.Sin/Cos).
- `ArenaLayoutHash` hashes the exact float bits (`ArenaLayoutHash.cs:172`).
- On a mismatch, `ClientArenas` (`src/Confluence.Net/Game/Client/ClientArenas.cs` L46-54) only increments
  `HashMismatches` and returns null. `AttachLayout` (`ClientDuel.cs` L161) then never creates the
  `MotorPredictor` or `CellGrid`, and the player gets no clear message.
- Unity tests compare Unity with Unity only (`client/Assets/Tests/Editor/ArenaPresentationTests.cs:194`).
- All networked tests use net10 headless clients.

**Change.**
1. Send the generated layout on Bulk when the hash mismatches, or always send it and use local generation only
   as a cache check.
2. Add a golden test: .NET writes recipe → hash for a few hundred seeds x rounds x biomes. A Unity EditMode test
   and a Mono `-batchmode` player must match. Rerun on every RulesVersion bump and Unity upgrade.
3. Show "downloading arena" or the unsupported-version state (L427) on mismatch.
4. Longer term: snap generator outputs to the wire grid (1/1024 m, table-driven integer-degree yaws) before any
   branching decision.

**Sources.**
- Unity, "Path to CoreCLR 2026 upgrade guide", 2026-03-26.
- Microsoft Learn `Math.Sin` (updated 2026-07-01).
- tirania.org, 2018-04-11.
- randomascii, 2013-07-16.
- Gaffer, "Floating Point Determinism", 2010-02-24.

#### 2.7.2 Bit-exact misprediction metric and a Mono-vs-.NET soak (risk: medium)

**Branch now.**
- Wire motor state is exact (`DuelFrame.cs:296-314` `WriteMotor` writes raw float bits).
- Inputs are canonicalized before prediction (`ClientDuel.cs:273`, `Wire/InputCodec.cs:108-119`).
- `Reconcile` counts only corrections over 1 cm (`ClientDuel.cs` L243-259), so float drift goes unseen.
- The client uses the Mono backend (`docs/notes/client.md:23, :66`).

**Change.**
- Compare the stored prediction for acked sequence N with the authoritative state bit for bit. Count mismatches
  not caused by server-only events, and log the first differing field.
- Add a CI job: a Unity Mono `-batchmode` bot plays the .NET server through `UdpLinkSimulator`.
- Re-feed `ReplayManifest` input tracks to the motor inside Unity and compare per-tick hashes.
- Re-baseline when moving to CoreCLR: experimental in Unity 6.7 LTS, and Mono is removed in 6.8. IL2CPP would be
  a third float behavior.

**Sources.** Unity, 2026-03-26; Ford, GDC 2017; tirania.org, 2018-04-11.

#### 2.7.3 Checkpoint hashes in replays (risk: medium)

**Good practice.** Factorio's test mode CRCs the whole map every tick and diffs the first divergent tick. Riot
(League of Legends) notes that only the first divergence matters.

**Branch now.**
- `ReplayManifest` stores input tracks and one `FinalStateHash` (`src/Confluence.Hosting/Duels/ReplayManifest.cs`
  L179-184).
- `DuelReplayer` compares only at the end (`DuelReplayer.cs` L92-100).
- `DuelWorld.StateHash` (L255) and `ReactionWorld.StateHash` (`ReactionWorld.Snapshot.cs:135`) already exist.

**Change.**
- Record `StateHash()` every 60 ticks, plus sub-hashes for motors, reactions and props.
- The replayer stops at the first mismatch and dumps both states.
- A client replay viewer should play back recorded snapshots and events. If it re-simulates, it must verify the
  checkpoints and show the unsupported-version state on mismatch (L427).

**Sources.**
- Factorio FFF-47, 2014-08-15.
- Riot, "Determinism in League of Legends: fixing divergences", 2018-05-01.
- Photon Quantum fixed-point docs (undated; the checksum-interval detail is UNCERTAIN because the page was
  blocked).

#### 2.7.4 Banned-API analyzer and value-level fingerprint (risk: medium)

**Branch now.**
- Direct MathF and Math calls appear in sim code, for example:
  - `src/Confluence.Reactions/Queries/GravityFields.cs:204`;
  - `src/Confluence.Reactions/Grid/Footprint.cs:132`;
  - `src/Confluence.Sim/Spells/Deliveries/SummonDelivery.cs:198`.
- There are 74 Sort calls. The sampled ones sort by unique ids.
- Formatting is already runtime-independent (`src/Confluence.Core/Json/DoubleFormatter.cs`), but parsing uses
  `double.TryParse` (`JsonParser.cs:315`).
- `RulesetFingerprint` hashes catalogue bytes only (`src/Confluence.Net/Protocol/RulesetFingerprint.cs`).
- Seeds use `SeedHash`, not `string.GetHashCode`, which is good.

**Change.**
- Add BannedApiAnalyzers to Core, Sim, Reactions, Arena and Casting. Ban:
  - MathF/Math transcendentals outside `CMath`. This ban is useful only once `CMath` itself is
    runtime-independent: today it calls `Math.Sin`/`Math.Cos` (`CMath.cs:165-168`, §2.7.1), which "calls into
    the underlying C runtime" and may differ by OS or architecture (Microsoft Learn). Replace them with
    fixed-order float polynomial or table-driven sin/cos/tan that do not rely on FMA contraction, and add a
    golden-value test of `CMath` outputs on .NET and Unity Mono. Until then the authoritative-layout fallback
    of §2.7.1 is the real safety net;
  - `System.Random`;
  - `DateTime.Now`, `Environment.TickCount`, `Stopwatch`;
  - `string.GetHashCode`;
  - Parallel and Task in sim code.
- Require total-order comparators. Sort is unstable and runtimes may differ on ties (UNCERTAIN in practice).
- Iterate sim collections in id order.
- Clamp before float→int casts. .NET 9+ saturates on x64; Mono likely does not (UNCERTAIN).
- Fold a hash of the parsed numeric bit patterns into `RulesetFingerprint`.

**Sources.**
- BannedApiAnalyzers help (living doc, accessed 2026-10-04).
- .NET blog, floating-point parsing and formatting changes, 2019-03-07.
- Microsoft Learn, .NET 9 FP-to-integer saturation change (2024).
- Microsoft Learn `Math.Sin` (2026-07-01).

### 2.8 LiteNetLib 2.1.4 specifics

#### 2.8.1 Manual-mode receive bug #616 (risk: medium)

**Branch now.**
- Both transports use `StartInManualMode` and `PollEvents` (`ServerTransport.cs:153, :170`;
  `ClientTransport.cs:244, :324`).
- In 2.1.4, `ManualReceive` (`LiteNetManager.Socket.cs:99-112`) exits its loop when `ReceiveFrom` returns 0.
  - On Linux and Mono/macOS, an empty datagram at the head of the queue stalls receiving permanently.
  - On Windows the effect is milder (UNCERTAIN; probably one datagram per poll during a flood).
  - Any internet host can trigger it.
  - The issue names a second trigger: when a `PacketLayerBase` rejects a packet by setting Size = 0, the loop
    also exits early and the packet is never returned to the pool. This matters for the AEAD layer of §2.6.7.
- Calling PollEvents then ManualUpdate is correct, because in manual mode PollEvents does the receiving.

**Change.**
- Pin LiteNetLib master at or after `d41569e0` (2026-09-28, "Fix #616"), or the first release that contains it.
  2.1.4 is still the latest release.
- Until then: run with `ManualMode = false`, or carry the four-line patch from the issue.
- Regression tests:
  - send a zero-length datagram, then a valid connect, and assert it is accepted within one Poll;
  - with a packet layer installed, send N forged datagrams the layer rejects, then a valid one, and assert the
    valid one is received in the same Poll and the packet pool does not grow.

**Sources.**
- LiteNetLib issue #616 (opened 2026-09-26, closed 2026-09-28).
- LiteNetLib releases page (2.1.4, 2026-05-19).
- `LiteNetManager.Socket.cs` at tag 2.1.4.

#### 2.8.2 Use LiteNetLib source in Unity and test the client's transport build (risk: low)

**Branch now.**
- `tools/sync-unity.ps1:7-10, :38` copies the netstandard2.1 DLL (`docs/notes/client.md:57-62`).
- The server and tests load the net8.0 build, so the client's socket code paths are never exercised by tests.

**Change.**
- Use the LiteNetLib source folder with its asmdef, pinned to the same commit as the server. This enables
  `PausedSocketFix` and the Unity `#ifdef`s.
- Add a smoke test: Unity Mono player against the .NET server on loopback.
- Watch the tracker. There were 6 releases between 2026-02-02 and 2026-05-19 with API moves, and fixes land on
  master between releases (for example #612, 2026-06-18).

**Sources.**
- LiteNetLib README (master, accessed 2026-10-04): "Always use library sources or OpenUPM package instead of
  precompiled DLL files".
- `PausedSocketFix.cs` at tag 2.1.4.
- LiteNetLib releases page.
- Issue #612, 2026-06-18.

#### 2.8.3 MTU: 1024 is the design budget (risk: low)

**Branch now.**
- `ServerTransport.cs:83-92` and `ClientTransport` set neither `MtuDiscovery` nor `MtuOverride`. The MTU is 1024,
  confirmed for 2.1.4.
- Sequenced and unreliable sends over the limit throw `TooBigPacketException` (`LiteNetPeer.cs:636-640`). The
  branch guards against it (`ServerTransport.SendFramed`, `Counters.RecordOversized`).

**Change.**
- Keep discovery off. The library warns that "some routers can break MTU detection".
- Budget from `GetMaxSinglePacketSize(Sequenced)`: 1020 B, so about 990 B of body.
- If raising the MTU, `MtuOverride = 1164` (PossibleMtu[1]) still fits QUIC's 1200 B minimum. Keep the budget at
  or below about 1150 B.
- Never fragment per-tick state reliably (§2.4.1).

**Sources.**
- LiteNetLib 2.1.4 XML docs and reflection (2026-10-04).
- `NetConstants.cs` (2.1.4 and master).
- `LiteNetManager.cs` at tag 2.1.4.
- LiteNetLib issue #66 (date not checked).
- RFC 9000, May 2021.

#### 2.8.4 Drive the 30/20 Hz switch from application RTT and loss (risk: medium)

**Branch now.**
- `GetLinkStats` uses `peer.RoundTripTime` (`ServerTransport.cs:266`) and `Statistics.PacketLoss / PacketsSent`
  (L584-614). The selector trips at RTT 80 ms above the floor (`NetRules.cs` L106;
  `Sync/SnapshotRateSelector.cs:56-59`).
- In 2.1.4, `RoundTripTime` is a running mean reset every 3 x PingInterval. A lost pong records the whole
  interval, about 1000 ms at the branch's PingInterval, as one sample (`LiteNetPeer.cs:1283-1295`). That alone
  can drop a healthy link to 20 Hz for about 3 s.
- `PacketLoss` mixes outbound reliable resends with inbound sequenced gaps (`ReliableChannel.cs:254`,
  `SequencedChannel.cs:91`).

**Change.**
- Stamp the send time of each snapshot and compute RTT from the snapshot acks in input packets.
- Loss = the share of snapshots not acked within about 500 ms. Use a windowed median or minimum.
- Keep `NetStatistics` for diagnostics only.

**Sources.**
- `LiteNetPeer.cs` and `SequencedChannel.cs` at tag 2.1.4 (2026-05-19).
- Gaffer, "Reliability, Ordering and Congestion Avoidance over UDP", 2008-10-20.

#### 2.8.5 Lane mapping: keep it and document the channel rules (risk: low)

**Branch now.**
- Nine lanes (`src/Confluence.Net/Protocol/NetChannel.cs`), with `ChannelsCount = 9` (`ServerTransport.cs:85`):
  - ReliableOrdered: Control, Match, Shop, Draft, Events;
  - ReliableUnordered: Bulk;
  - Sequenced: Input, Snapshot;
  - Unreliable: Time.
- Input redundancy is 4.

**Change.**
- Document that each reliable channel has a 64-packet window. Cap events per tick and test a burst at 10% loss.
- Note in `docs/notes/net.md` that the branch must stay on the full `NetManager`/`NetPeer`. LiteNetLib 2.0's
  simplified `LiteNetManager` has one channel per delivery method.

**Sources.** LiteNetLib 2.0.0 release notes, 2026-02-02; `NetConstants.cs` at tag 2.1.4.

#### 2.8.6 Windows timer resolution on the dedicated server (risk: low)

**Branch now.**
- `GameServerRunner.Run` calls `Thread.Sleep(1)` when the next tick is more than 3 ms away
  (`src/Confluence.Net/Game/Server/GameServerRunner.cs:90`). Nothing calls `timeBeginPeriod`.
- In manual mode all packet I/O happens in Poll/Flush (`Transport/TransportSettings.cs`), so oversleep bunches
  ticks and sends.
- The size of the oversleep at the default resolution is UNCERTAIN (commonly about 15.6 ms). Measure it.

**Change.**
- On Windows, call `timeBeginPeriod(1)` at startup, or use a high-resolution waitable timer.
- Record tick-interval and snapshot-interval histograms.

**Sources.** Microsoft Learn `timeBeginPeriod` (updated 2024-02-22): per-process since Windows 10 2004.

### 2.9 Testing and metrics

#### 2.9.1 Realistic link simulator and named profiles (risk: medium)

**Branch now.**
- `src/Confluence.Net/Diagnostics/UdpLinkSimulator.cs` supports fixed latency, uniform jitter, independent loss,
  duplication and blackhole.
- It already supports per-direction conditions (`NetworkConditions.ClientToServer` and `ServerToClient`,
  `UdpLinkSimulator.cs` L31-37), so asymmetric links are possible today.
- It has no burst loss, bandwidth cap, queueing or spikes.
- It is used only in transport tests (`tests/Confluence.Net.Tests/Transport/LossyLinkTests.cs`,
  `CongestionTests.cs`, `Sync/ClockSyncTests.cs`). `GameRig` already accepts `NetworkConditions` and creates a
  `UdpLinkSimulator` (`TestSupport/GameRig.cs` L34-42), but no game test (`EndToEndTests`) passes any.

**Change.**
- Add:
  - Gilbert-Elliott burst loss;
  - heavy-tailed delay with 200-1000 ms spikes;
  - a bandwidth cap with a bounded queue, so the selector's RTT-inflation and backlog triggers actually fire;
  - scripted timelines.
- Define named profiles in one place (using the existing per-direction conditions where a profile is asymmetric): Unity presets (fiber 10/1/0%, broadband 32/12/2%, congested 50/50/1%), plus
  100/150 ms with 5-10% loss, plus 500 and 1000 ms edge cases.
- For multi-process runs use clumsy on Windows and tc-netem on Linux CI.
- Unity's guidance: delay-only testing is unrealistic, and simulation does not replace real-network playtests.
- The game-level tests of §2.9.2 and §2.9.3 then only need to pass these named profiles to the existing
  `GameRig` constructor.

**Sources.**
- tc-netem(8) man page (dated 2011-11-25, updated 2026-09-09).
- Unity Multiplayer Tools 2.2 Network Simulator (accessed 2026-10-04).
- NGO 2.6 "Testing with artificial conditions" (accessed 2026-10-04).
- clumsy 0.3, 2023-10-21.

#### 2.9.2 Automated matches with quality assertions; nightly soak (risk: medium)

**Branch now.**
- `tests/Confluence.Net.Tests/Game/EndToEndTests.cs` has one perfect-link Laboratory duel that asserts only that
  the match finished.
- `GameRig` (`TestSupport/GameRig.cs`) runs on a `ManualNetClock`.
- The server and headless client executables support multi-process runs (`--max-seconds`, `--log`, `--bots`),
  but nothing scripts them.

**Change.**
1. PR tier, virtual clock: a duel per profile using all input routes (rhythm, voice, direction keys). Assert:
   - the match finishes;
   - zero faults and violations;
   - MaxCorrection and corrections per minute stay under limits;
   - NeutralTicks and LostSubstituted stay within per-profile limits;
   - the reliable-fallback ratio stays low;
   - 20 Hz appears only on congested profiles;
   - disconnect plus resume within 90 s works, and expiry after 90 s is handled.
2. Nightly, real time: the server plus 2-8 headless clients for hours under clumsy or netem. Track tick-duration
   p50/p99/max, Stalls, GC, memory, table sizes and tick-interval jitter.
3. Weekly: the Unity client against headless opponents on real remote machines.

**Sources.**
- Riot, "Determinism in League of Legends: fixing divergences", 2018-05-01.
- Riot, "VALORANT's 128-tick servers", 2020-08-31.
- Microsoft Learn `timeBeginPeriod` (2024-02-22).
- NGO 2.6 testing docs.

#### 2.9.3 Lag-compensation tests under impairment (risk: low)

**Branch now.** No test references `ClampViewTick` or `RebaseEvents` (checked; only build output matches). The
e2e UDP duel reports only max correction.

**Change.**
- Unit tests for the clamp (bounds, expected window, monotonicity, stand-ins) and for displayed-tick reporting.
- E2E test through `UdpLinkSimulator` at RTT 0/40/80/120/200 ms with 5 ms jitter and 2% loss. A headless shooter
  aims exactly at the interpolated capsule. Assert about 100% hits up to the documented favored RTT.
- Report clamps, slip, Starved samples and corrections per second.
- Assert that no syllable or tap is lost on trims.
- Export results per region (L423).

**Sources.** Valve `player_lagcompensation.cpp` (`sv_unlag_debug`; file last changed 2025-02-18); Riot, 2020-07-28.

#### 2.9.4 Snapshot size measurement and a worst-case reaction duel (risk: medium)

**Branch now.**
- `tests/Confluence.Net.Tests/Game/SnapshotCodecTests.cs` checks that delta < full / 3 and that an unchanged world
  is under 150 B.
- The counters exist (`SnapshotsSent`, `ReliableSnapshotsSent`, `DeltaSnapshotsSent`, `MissingBaselines`), but no
  byte metrics are reported.

**Change.**
- Per-section byte histograms, baseline age and event latency.
- A scripted L421 worst case: 12 spell slots, 6 fields, 4 summons, 128 cell regions, 48 props, plus a large
  drifting steam volume. Run at 100-150 ms RTT with 2-5% loss.
- CI gates: p99 fits the budget; zero reliable fallbacks outside keyframes; bounded event delay.

**Sources.**
- Gaffer, 2015-01-04.
- UCL Halo: Reach summary, 2011-05-13.

#### 2.9.5 One per-session metrics record (risk: medium)

**Branch now.** Counters are scattered and mostly unreported:
- `Transport/TransportCounters.cs`;
- the `InputReceiveBuffer` stats;
- `ClampedViewTicks`;
- the `GameServer` snapshot counters;
- `ClientDuel` Corrections and MaxCorrection;
- `GameServerRunner.Stalls`.

There is no tick timing.

**Change.** At duel and match end, write one JSON line per session containing:
- Transport: RTT min/avg/p95, jitter, loss, time at 20 Hz, bytes per second, backlog peak.
- Snapshots: delta ratio, fallback ratio, average and max bytes.
- Inputs: all `InputReceiveBuffer` counters, ClampedViewTicks, claimed vs expected rewind.
- Security: violations by kind, rate limiting, faults, rejections, takeovers.
- Server: tick p99, catch-up ticks, Stalls.
- Client: corrections, buffer underruns, extrapolation time.

Use this record for test pass/fail and for the per-region analytics required by L423.

**Sources.** Riot, 2020-08-31; Riot, 2020-07-28.

#### 2.9.6 Replay-based regression (risk: medium)

**Branch now.**
- Hosting has `ReplayManifest`, `DuelReplayer` and
  `tests/Confluence.Hosting.Tests/FullMatchTests.cs` `Replay_Manifest_Reproduces_The_Duel_Final_State_Hash`.
- The net layer never persists manifests from networked play.

**Change.**
1. `GameServer` writes each duel's manifest, with the inputs as actually consumed after `NetworkInputSource`.
2. Checkpoint hashes (§2.7.3).
3. A golden corpus per wizard grammar, reactions, props and lag-compensated hits, replayed on every PR. A
   fingerprint change forces a deliberate re-record.
4. Replay the same corpus in a Unity player (Mono and IL2CPP).
5. A Factorio-style "heavy mode" test: save and restore every tick to expose state missed by serialization
   (reconnect and migration, L427).

**Sources.**
- Riot, 2018-05-01.
- Gaffer, 2010-02-24.
- Factorio wiki "Desynchronization" (last edited 2025-03-03).

---

## 3. Checklist for the net-protocol verifier

Each item is a pass/fail check against the branch, or against the fixes above.

**Inputs and timing**
- [ ] After a 3-tick input starvation, the measured slip (server step minus client stamp) returns to its target
      within 1 s and does not stay +3.
- [ ] `TrimSurplus` and any surplus drop preserve `Tapped` edges and `GrammarEvents` of skipped commands. Test:
      bank 20 commands each carrying a syllable; every syllable reaches the Sim.
- [ ] Grammar event offsets are bounded per kind (today ±600 for all, `InputCodec.cs` L30): [-1, 0] ticks for
      `RhythmTap` and button-like kinds; for `Syllable` and `StrokeSample` no more back-dating than the rewind
      clamp (9 ticks) or a documented recognizer-latency bound equal for every client. Out-of-range offsets are
      clamped and counted, not treated as Malformed. Test: a syllable stamped 5 ticks before its command is
      accepted and does not count toward `MalformedKickThreshold`.
- [ ] Two syllables 20 ticks apart on the client are 20 ticks apart on the server, even with a 3-tick starvation
      between their commands.

**Lag compensation**
- [ ] `ClampViewTick` unit tests: requested ≤ 0 gives 0; it never exceeds min(stepTick-1, newest shown); it is
      never older than stepTick-9.
- [ ] A client claiming the maximum 9-tick rewind on a 5 ms link is replaced by the expected view (±2 ticks plus
      jitter), and the replacement is counted.
- [ ] View ticks that go backward by more than 1 tick are rejected or replaced. The rule resets on reconnect and
      duel start.
- [ ] Repeated and lost stand-in commands get no lag compensation.
- [ ] Teleport 10 m, then hitscan with a 5-tick-old view, misses the old position. The same holds for recall and
      alcove placement.
- [ ] A guard raised after the shooter's view tick reduces damage. A projectile uses present volumes. A present
      wall blocks a rewound target (existing test).
- [ ] The rewind breakdown (one-way delay, server queue depth, interpolation, clamped) is logged per client. `ClampedViewTicks` is
      reported.
- [ ] E2E through `UdpLinkSimulator` (0/40/80/120/200 ms RTT, 5 ms jitter, 2% loss): the hit rate is about 100%
      up to the documented favored RTT.

**Snapshots and events**
- [ ] With the L421 worst case at 100-150 ms RTT and 2-5% loss, p99 snapshot body ≤ budget (about 990 B), and
      reliable fallbacks happen only for keyframes.
- [ ] Snapshots never exceed `GetMaxSinglePacketSize(Sequenced)` and never trigger `RecordOversized`.
- [ ] With partial snapshots, the remembered baseline equals what the client decoded (round-trip test).
- [ ] First, resumed and expired-baseline snapshots are encoded against the layout baseline.
- [ ] An unchanged `MotorTuning` and status list cost 0 bytes in a delta. Ids are ordinals.
- [ ] A drifting steam volume does not resend its bitmap while its columns are unchanged.
- [ ] Cell capture cost is proportional to dirty cells, not grid size (benchmark at 96x96).
- [ ] Reaction (cosmetic) events are not on the ReliableOrdered Events lane. Damage confirmations are not
      delayed by a lost cosmetic packet (10% loss test).
- [ ] Unacked critical events are repeated in snapshots and deduped by id on the client.
- [ ] The snapshot ack carries a received bitfield. Snapshot loss is measured from it.
- [ ] Spectator frames are deltas against the previous released frame, with a keyframe on join.

**Hidden information and security**
- [ ] A frame sent to a duelist contains no position, velocity or aim for an opponent that `IsVisibleTo` reports
      hidden. Events carry no exact position for a hidden player.
- [ ] A production server refuses `--seed` defaults outside Laboratory/Custom. The seed comes from a CSPRNG.
      Offer, Draft, Bot and Pairing streams are HMAC-derived. No server-to-client message carries a
      secret-derived value.
- [ ] Opponent health cannot be reconstructed from everything one client received, or the policy says it is
      public (DECISIONS entry exists).
- [ ] Recording all sends during 8-seat matches (leak test) shows: no other seat's shop or draft; no other pair's
      frames or events during a live duel; spectator frames ≥ 15 s old; no opponent PrivateEntry; results and
      standings follow the chosen delay policy.
- [ ] A reflection test fails if a ClientToServer message gains position, outcome or resource fields.
- [ ] A property test: random valid `DuelInputMessage`s keep server state inside the motor envelope.
- [ ] Sustained RateLimited and HandlerFault outcomes lead to a kick. The input rate limit is about 2x the need.
- [ ] Fuzz harnesses for every decoder throw only `NetReadException` with bounded allocation.

**LiteNetLib and runtime**
- [ ] LiteNetLib is pinned at or after `d41569e0` (or `ManualMode = false`). Zero-length datagram then connect is
      accepted within one Poll. N layer-rejected forged datagrams then a valid one: the valid one is received in
      the same Poll and the packet pool does not grow.
- [ ] The congestion selector does not drop to 20 Hz when a single pong is lost on a clean link.
- [ ] Tick-interval histogram on Windows: p99 is within about 2 ms of 16.67 ms (timer resolution fixed).
- [ ] A burst of more than 64 Events packets under 10% loss does not stall combat events beyond a set bound.
- [ ] Replay manifests are written for networked duels, carry checkpoint hashes every 60 ticks, and the replayer
      reports the first divergent tick.
- [ ] A golden arena-hash file generated by .NET matches in Unity EditMode and in a Mono `-batchmode` player.
- [ ] A per-session metrics JSON line is written at duel and match end.

## 4. Checklist for the Stage 4 Unity client

- [ ] Use LiteNetLib **source** (asmdef) pinned to the server's commit, not the NuGet DLL (`tools/sync-unity.ps1`).
- [ ] On an arena hash mismatch, show "downloading arena" or the unsupported-version state (L427). Never run a duel
      without a layout. Request the authoritative layout.
- [ ] The golden cross-runtime arena test and the motor replay-hash test pass in EditMode and in a Mono player
      build.
- [ ] Present the predicted player by lerping the previous and current predicted `MotorState` with sub-tick alpha.
      No 60 Hz judder at 120 fps.
- [ ] Apply the correction offset to the rendered body and camera only, never to the aim or shot origin. Use
      magnitude-dependent decay. Snap or fast-blend on server displacement, teleport or recall.
- [ ] Sample `ViewTick` (fixed-point tick + alpha) from the `SnapshotSample` the opponent was drawn with, in the
      same frame the input is sampled. Report the held tick when starved.
- [ ] Render the remote opponent, remote projectiles and areas on the interpolated timeline. Support an adaptive
      interpolation delay, changed by time-scaling, never by jumping.
- [ ] Your own projectile: a cosmetic local spawn at release, handed over to the server entity on confirmation,
      destroyed after about RTT + 100 ms if unconfirmed.
- [ ] Never show impacts as hits, damage numbers, reaction cells, statuses or opponent health before
      authoritative data arrives.
- [ ] Area spells: predict only placement preview, reticle, and your own windup animation and sound.
- [ ] Render persistent state (statuses, fields, solids, props, cells, volumes, health, phase) only from snapshots.
      Use events only as one-shot cues.
- [ ] Schedule remote and world event cues at their tick on the interpolation clock. Play your own confirmations
      on arrival. Telegraph timing follows the §2.2.6 decision.
- [ ] Show mana spent and cooldown start as "pending" until the Cast event confirms them. Show the authoritative
      grammar UI whenever the predicted cast machine disagrees.
- [ ] Treat a hidden or invisible opponent as absent (do not extrapolate). Play footstep cues from coarse
      positions.
- [ ] Show the high-ping indicator when the expected rewind exceeds the cap (L423).
- [ ] Keep allocation low when decoding snapshots. Avoid per-snapshot `SortedDictionary` rebuilds of the whole
      cell set.
- [ ] Report client metrics: bit-exact mispredictions, corrections per second, p95 correction size, buffer
      underruns, extrapolation time.
- [ ] A Unity Mono `-batchmode` bot can play the .NET server through `UdpLinkSimulator` (CI soak).

## 5. Open questions and uncertain claims

**Design decisions needed**
- Does the spec's 150 ms rewind (L423) include the interpolation delay? Read strictly, yes, and the branch
  implements it that way. With the rewind ≈ one-way delay + server queue + 6..8 ticks (§2.2.1), the brief's
  100 ms interpolation leaves about 1-2 ticks (17-33 ms) for one-way delay plus queue, about 35-65 ms RTT with an
  empty queue. A DECISIONS entry and a target favored RTT per region are needed.
- What are the minimum and maximum adaptive interpolation delays at 20 Hz (50 ms interval) under the 150 ms cap?
  Gaffer's two-lost-packets headroom (§2.2.1) needs at least 100 ms plus jitter at 20 Hz, which leaves almost no
  rewind for latency. Decide between that headroom and accepting starved samples (short extrapolation, held tick
  reported) as a cost.
- Should teleports and blinks break lag compensation (favor the target, Source-style)?
- Trades: does a hitscan command for an earlier tick count if the shooter died on the server before it was
  processed? Not verified on the branch.
- Should threat telegraphs be shown on event arrival, not on the interpolated timeline? This needs playtesting; no
  primary source was found. It conflicts with the general "remote cues on the interpolation clock" rule in §2.5.2.
- Is the opponent's exact health hidden during a duel? No spec line or DECISIONS entry says so.
- Do any spell entities or owned fields (traps, hidden deployables) need hiding? `CaptureEntities` and
  `CaptureOwned` send all of them to both duelists.
- `DuelStartMessage`'s `DuelSideWire` includes the opponent's `EnabledActiveId` and `EquipmentSlots`, which
  `MatchFlow.VisibleOpponentLoadout` does not expose. Is the enabled active public at duel start?
- Should live duelists see other pairs' results and standings in real time (L49, L391)?
- How precise should an invisible player's footstep cue be (L120)?
- Should prediction sample surface from replicated cells near the player, rather than the single `PrivateEntry`
  surface? The answer decides how fresh local cell tiles must be.
- What is the downstream byte budget per player and per spectator (for example 256 kbps)? The spec gives only
  rates (L421).
- Should cosmetic reaction events feed replays (L419)? If so, record them on the server, not reliably to clients.
- Which auth backend will issue connect tokens (Steam, EOS, custom)?
- Will the dedicated server run on Windows or Linux? This affects §2.8.1 and §2.8.6.
- Which Unity runtime will ship: Mono, CoreCLR (experimental in 6.7, the only option in 6.8) or IL2CPP? The golden
  tests need a fixed target.
- Should a client replay viewer re-simulate at all (L427)?

**Uncertain claims**
- Byte sizes (PrivateEntry about 240 B, fixed part 350-450 B, cells about 4 B, savings 150-250 B) are estimates
  from reading the encoders. The branch was not built or run, and its head commit says it "may not compile".
- Overwatch specifics: the about 220 ms cutoff, the exceptions to favor-the-shooter, and the dilation numbers come
  only from a secondary summary (Edgegap, 2026-03-17). The 2016 video and the GDC 2017 talk were not
  transcribed.
- Halo: Reach grenade prediction: no source describing it was found (the UCL summary does not mention it). The
  Halo prioritization point is a paraphrase of the UCL summary, not a quotation from the talk.
- Valve wiki pages returned HTTP 403. Source claims rely on SDK code and search excerpts.
- Photon Quantum's checksum-interval desync check: the page could not be opened.
- LiteNetLib: whether reliable merging packs several messages into one window slot.
- The Windows effect of #616 (probably one datagram per poll during a flood).
- Whether Unity 6.6 Mono runs float32 in single precision. Whether Mono's JIT saturates float→int casts. Whether
  Sort tie order differs in practice between runtimes.
- Whether a random 64-bit match seed can be recovered from the published arena seed.
- Whether the AEAD APIs work in Unity 6000.6 Mono/IL2CPP for netstandard2.1.
- Actual `Thread.Sleep(1)` overshoot on the target hardware.
- Real projects on LiteNetLib: FishNet's default transport Tugboat (docs undated) and Project Fika, an Escape from
  Tarkov SPT co-op mod (accessed 2026-10-04). No primary source was found for a shipped competitive FPS on
  LiteNetLib 2.x at scale.

## 6. Sources

Dates are publication or last-update dates where known. "Accessed" means no date was shown.

**Engine source and documentation**
- Valve, `source-sdk-2013/src/game/server/player_lagcompensation.cpp`,
  https://raw.githubusercontent.com/ValveSoftware/source-sdk-2013/master/src/game/server/player_lagcompensation.cpp
  (file last changed 2025-02-18).
- Valve Developer Community, "Source Multiplayer Networking",
  https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking (undated; 403 to fetch).
- Valve Developer Community, "Lag Compensation", https://developer.valvesoftware.com/wiki/Lag_Compensation (undated;
  403 to fetch).
- Epic, "Understanding networked movement in the Character Movement Component",
  https://dev.epicgames.com/documentation/unreal-engine/understanding-networked-movement-in-the-character-movement-component-for-unreal-engine
  (UE 5.8, undated).
- Epic, `FFastArraySerializer` API,
  https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/NetCore/FFastArraySerializer (UE 5.8, undated).
- Photon, Fusion lag compensation,
  https://doc.photonengine.com/fusion/current/manual/advanced/lag-compensation (undated, accessed 2026-10-04).
- Photon, Quantum fixed point, https://doc.photonengine.com/quantum/v3/manual/quantum-ecs/fixed-point (undated,
  accessed 2026-10-04).
- Mirror, lag compensation, https://mirror-networking.gitbook.io/docs/manual/general/lag-compensation (undated,
  accessed 2026-10-04).
- Unity, NGO 2.11 "Dealing with latency",
  https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.11/manual/learn/dealing-with-latency.html
  (undated).
- Unity, NGO 2.6 "Testing with artificial conditions",
  https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.6/manual/tutorials/testing/testing_with_artificial_conditions.html
  (accessed 2026-10-04).
- Unity, Netcode for Entities 1.5 ghost snapshots,
  https://docs.unity3d.com/Packages/com.unity.netcode@1.5/manual/ghost-snapshots.html (undated).
- Unity, Netcode for Entities 1.5 optimizations,
  https://docs.unity3d.com/Packages/com.unity.netcode@1.5/manual/optimizations.html (undated).
- Unity, Netcode for Entities 1.6 optimizations,
  https://docs.unity3d.com/Packages/com.unity.netcode@1.6/manual/optimizations.html (undated).
- Unity, Multiplayer Tools 2.2 Network Simulator,
  https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/network-simulator.html (accessed
  2026-10-04).
- Unity, "Path to CoreCLR 2026 upgrade guide", https://discussions.unity.com/t/path-to-coreclr-2026-upgrade-guide/1714279
  (2026-03-26).

**LiteNetLib**
- README and repository, https://github.com/RevenantX/LiteNetLib (master, accessed 2026-10-04).
- Releases, https://github.com/RevenantX/LiteNetLib/releases (2.0.0 on 2026-02-02 through 2.1.4 on 2026-05-19).
- 2.0.0 release notes, https://github.com/RevenantX/LiteNetLib/releases/tag/2.0.0 (2026-02-02).
- Tag 2.1.4 source files (2026-05-19): `NetConstants.cs`, `LiteNetManager.cs`, `LiteNetManager.Socket.cs`,
  `LiteNetPeer.cs`, `SequencedChannel.cs`, `PausedSocketFix.cs`, under
  https://github.com/RevenantX/LiteNetLib/blob/2.1.4/LiteNetLib/.
- Master source (accessed 2026-10-04):
  https://raw.githubusercontent.com/RevenantX/LiteNetLib/master/LiteNetLib/NetConstants.cs and
  https://raw.githubusercontent.com/RevenantX/LiteNetLib/master/LiteNetLib/LiteNetPeer.cs.
- Issue #616, https://github.com/RevenantX/LiteNetLib/issues/616 (2026-09-26, closed 2026-09-28; fix commit
  `d41569e0`).
- Issue #612, https://github.com/RevenantX/LiteNetLib/issues/612 (2026-06-18).
- Issue #66, https://github.com/RevenantX/LiteNetLib/issues/66 (date not checked).
- Issue #13, https://github.com/RevenantX/LiteNetLib/issues/13 (2017-01-06).
- LiteNetLib 2.1.4 NuGet assembly and XML docs, constants read by reflection (checked 2026-10-04).
- FishNet Tugboat transport, https://fish-networking.gitbook.io/docs/fishnet-building-blocks/transports/tugboat
  (undated).
- Project Fika, https://github.com/project-fika/Fika-Plugin (accessed 2026-10-04).

**Talks and studio articles**
- Tim Ford, "Overwatch Gameplay Architecture and Netcode", GDC 2017,
  https://www.gdcvault.com/play/1024001/-Overwatch-Gameplay-Architecture-and.
- Blizzard, Overwatch "Let's Talk Netcode" developer update, https://www.youtube.com/watch?v=vTH2ZPgYujQ (2016-04-05).
- Kotaku, https://kotaku.com/to-explain-netcode-properly-you-need-props-1769244398 (2016-04-05).
- Edgegap, Overwatch netcode deep dive (secondary),
  https://edgegap.com/blog/game-backend-deep-dive-overwatch-2016-netcode-architecture-rollback (2026-03-17).
- David Aldridge, "I Shot You First: Networking the Gameplay of Halo: Reach", GDC 2011,
  https://www.gdcvault.com/play/1014345/I-Shot-You-First-Networking.
- UCL summary of the Halo: Reach talk, https://ng.cs.ucl.ac.uk/2011/05/13/halo-reach/ (2011-05-13).
- Riot (deWet, Straily), "Peeking into VALORANT's netcode", https://www.riotgames.com/en/news/peeking-valorants-netcode
  (2020-07-28).
- Riot (Chamberlain), "Demolishing wallhacks with VALORANT's fog of war",
  https://www.riotgames.com/en/news/demolishing-wallhacks-valorants-fog-war (2020-04-14).
- Riot (Randall), "VALORANT's 128-tick servers", https://www.riotgames.com/en/news/valorants-128-tick-servers
  (2020-08-31).
- Riot (Hoskinson), "Determinism in League of Legends: fixing divergences",
  https://www.riotgames.com/en/news/determinism-league-legends-fixing-divergences (2018-05-01).
- Frohnmayer and Gift, "The TRIBES Engine Networking Model", https://archive.org/details/tribes-networking-model
  (c. 1999; year uncertain).
- Fabien Sanglard, "Quake 3 network model", https://fabiensanglard.net/quake3/network.php (2012-06-30).
- Factorio Friday Facts #47, https://www.factorio.com/blog/post/fff-47 (2014-08-15).
- Factorio Friday Facts #188, https://factorio.com/blog/post/fff-188 (2017-04-28).
- Factorio wiki, "Desynchronization", https://wiki.factorio.com/Desynchronization (last edited 2025-03-03).

**Articles and blogs**
- Gaffer On Games:
  - "Fix Your Timestep!", https://gafferongames.com/post/fix_your_timestep/ (2004-06-10).
  - "UDP vs. TCP", https://gafferongames.com/post/udp_vs_tcp/ (2008-10-01).
  - "Reliability, Ordering and Congestion Avoidance over UDP",
    https://gafferongames.com/post/reliability_ordering_and_congestion_avoidance_over_udp/ (2008-10-20).
  - "Floating Point Determinism", https://gafferongames.com/post/floating_point_determinism/ (2010-02-24).
  - "Snapshot Interpolation", https://gafferongames.com/post/snapshot_interpolation/ (2014-11-30).
  - "Snapshot Compression", https://gafferongames.com/post/snapshot_compression/ (2015-01-04).
  - "State Synchronization", https://gafferongames.com/post/state_synchronization/ (2015-01-05).
  - "Packet Fragmentation and Reassembly", https://gafferongames.com/post/packet_fragmentation_and_reassembly/
    (2016-09-06).
  - "Reliable Ordered Messages", https://gafferongames.com/post/reliable_ordered_messages/ (2016-09-15).
- Glenn Fiedler, netcode 1.02 standard, https://github.com/mas-bandwidth/netcode/blob/main/STANDARD.md (accessed
  2026-10-04).
- Gabriel Gambetta, "Lag Compensation", https://www.gabrielgambetta.com/lag-compensation.html (undated, accessed
  2026-10-04).
- Bruce Dawson, "Floating-Point Determinism", https://randomascii.wordpress.com/2013/07/16/floating-point-determinism/
  (2013-07-16).
- Miguel de Icaza, Mono float32, https://tirania.org/blog/archive/2018/Apr-11.html (2018-04-11).
- Arkin, Hill, Marks, Schmid, Walls, McGraw, "How We Learned to Cheat at Online Poker",
  https://www.developer.com/tech/article.php/616221/How-We-Learned-to-Cheat-at-Online-Poker-A-Study-in-Software-Security.htm
  (1999).

**Microsoft and .NET**
- `Math.Sin`, https://learn.microsoft.com/en-us/dotnet/api/system.math.sin (updated 2026-07-01).
- .NET 9 FP-to-integer conversion change, https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer
  (2024).
- "Floating-point parsing and formatting improvements in .NET Core 3.0",
  https://devblogs.microsoft.com/dotnet/floating-point-parsing-and-formatting-improvements-in-net-core-3-0/
  (2019-03-07).
- `timeBeginPeriod`, https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod (updated
  2024-02-22).
- BannedApiAnalyzers help,
  https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md
  (living doc, accessed 2026-10-04).

**Standards and tools**
- RFC 2104, HMAC, https://www.rfc-editor.org/rfc/rfc2104 (February 1997).
- RFC 9000, QUIC, https://www.rfc-editor.org/rfc/rfc9000 (May 2021).
- tc-netem(8), https://man7.org/linux/man-pages/man8/tc-netem.8.html (dated 2011-11-25, updated 2026-09-09).
- clumsy 0.3, https://github.com/jagt/clumsy (2023-10-21).
- SharpFuzz v2.2.0, https://github.com/Metalnem/sharpfuzz (2025-01-11).
