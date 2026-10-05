# Deep Research prompt: Confluence netcode

Run this by hand in ChatGPT Deep Research or Gemini Deep Research (it uses those subscriptions, not Claude quota).
Save the full report, with sources and dates, as `docs/research/netcode.md` on master. Then tell the hub, so the
net-protocol verifier and the Stage 4 client agent check the branch against it.

---

I am building an authoritative-server, first-person 1v1 wizard dueling game (Windows PC; C# simulation in
netstandard2.1 shared by a .NET 10 server and a Unity 6 client). Networking runs on LiteNetLib 2.1.4 in manual
update mode. Current design:
- the server simulates at 60 ticks/s in integer ticks with a deterministic simulation (seeded RNG streams; no
  wall-clock time in rules);
- clients send per-tick input commands (movement, a unified buttons enum, and spell "grammar" events such as
  syllables and gestures) with redundancy; the server repeats the last input for up to 6 ticks when starved and
  never lets a client bank more than 12 ticks ahead;
- the server sends snapshots at 30 Hz (20 Hz under congestion) with keyed delta compression against an
  acknowledged baseline, plus reliable ordered event lanes (casts, telegraphs, damage confirmations, reactions);
- clients interpolate remote entities about 100 ms behind the estimated server tick, predict their own movement
  with the shared character motor, and reconcile by replaying unacknowledged inputs after each snapshot, with
  smoothed corrections;
- the design requires hitscan and melee validation with lag compensation that rewinds at most 150 ms of player
  hit volumes only;
- a cell-based "reaction world" (fire, water, steam, ice, electricity spreading over a 2D grid with volumes)
  must replicate as changed cells plus volumes;
- hidden information must never leave the server (the other pairs' duels, private shop and draft state, an
  opponent's private spell-preparation UI).

Research, with primary sources (engine docs, conference talks such as GDC, papers, well-known open-source
netcode) and the date of each source:
1. Best current practice for client prediction and server reconciliation in competitive first-person games:
   input buffering and jitter handling, smoothing of corrections, and what to predict versus not predict
   (projectiles, area spells, status effects, cast stages).
2. Lag compensation for hitscan and melee: rewind window, which state to rewind, favor-the-shooter versus
   favor-the-target trade-offs, and abuse limits.
3. Snapshot delta compression and interest management at 20-30 Hz: baselines and acknowledgements, priority
   and bandwidth budgets, and how to replicate a large changing grid (the reaction world) efficiently.
4. Deterministic simulation pitfalls when the same C# code runs on .NET and Unity/Mono (floating point, iteration
   order, serialization), and how teams test for desyncs.
5. LiteNetLib specifics: channels and delivery methods, MTU and fragmentation, manual mode, known pitfalls, and
   real games or projects using it at this scale.
6. Testing strategies: simulated latency, loss and jitter, automated two-client matches, and metrics to watch.

For each topic, give concrete recommendations for the design above, the risks of the current choices, and
anything it is missing. Mark uncertain claims as uncertain.
