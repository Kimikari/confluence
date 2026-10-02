# Confluence.Match — match structure and round flow

Owner paths: `src/Confluence.Match/**`, `tests/Confluence.Match.Tests/**`. Spec: §2 (L30-L73), L193-L229, L387-L395, L427.

## Layout
- `Flow/MatchFlow.cs` — abstract tick-driven state machine shared by Conclave and Duel. `Start()` leaves the lobby and
  settles zero-length phases on the start tick; `Tick()` advances one authoritative step (60/s): polls duels, then
  completes every phase whose timer or condition is met (`SettlePhases`). Phases: Lobby -> [Reveal -> Shop -> Draft (draft
  rounds only) -> Alcove -> Combat -> Results] per pass -> Finished. Replay and void-rerun passes skip Shop and Draft.
- `Conclave/ConclaveMatch.cs` — Resolve, regular byes (L61), complete-enumeration pairing (L59), elimination after the
  round, bracket after round 12 (L38). Internal ctor with a shortened regular schedule is a test seam only.
- `Conclave/ConclaveSettings.cs` — Ranked/Unranked fixed rules, Custom settings (2-8 players, shop timer, starting Crowns,
  spectator delay, allowed content, arena seed rotation), canonical text and 16-hex fingerprint (L71).
- `Duel/DuelMatch.cs` — best of seven in half wins, draw replays, half win after two replays, decider round 8 (L65).
- `Bracket/Bracket.cs` — seeding comparator, standard slot order, power-of-two byes for top seeds, walkovers.
- `Pairing/` — `PairingSolver` (lexicographic `PairingCost`, all (n-1)!! matchings, round-seed tie-break),
  `ByeSelector`, `SideAssigner`.
- `Seams/` — `IDuelRunner` (+ `DuelSetup`, `DuelOutcome`, `DuelJudgement`), `IMatchEconomy` (+ `ShopOpening`,
  `DraftOpening`, `PublicLoadout`, `CompletedDuelRecord`), `IArenaProvider` (+ `ArenaRequest`, `ArenaPreview`).
- `Events/` — append-only `MatchEventLog` with canonical text + fingerprint, spectator `DelayedView`.
- `Results/` — `MatchPlacement` (tied blocks, mean position), `MatchResult`, `RoundResult`/`RoundResultEntry`.

## Key rules and interpretations
- Reveal has no stated length (L44); it is 0 s and its information stays visible through the shop (`MatchRules.RevealSec`).
- Early ready (L45): every active (non-eliminated) player who shops this round, including bye players, must be ready.
  Players disconnected past the 90 s reconnect window count as ready; players within the window do not.
- Income (L193): 600 (or custom starting Crowns) before the first shop, 500 before every later regular or bracket shop,
  +100 when the player won a non-replay, non-voided fight in the previous round. Draws, byes, bracket byes/walkovers,
  and replay wins give no bonus. Replay passes have no shop and no income.
- Draws in regular rounds remove ResolveLoss(round) from both (L36). Eliminations are applied once the round's last
  duel resolves; everybody reaching 0 in the same round shares the block of positions below the survivors.
- Bracket: seeded by Resolve, combat wins, fewer regular byes, lobby seed (then player id). Size is the next power of
  two; empty slots go to the highest seeds through the standard slot order. Bracket losers of the same bracket round
  share a placement block. A draw replays once (new arena seed, same diameter, no shop, frozen loadouts); a second draw
  eliminates both into a shared block, so the next round's opponent advances by walkover. A shared final finish leaves no
  champion (`MatchResult.Champion == NoPlayer`).
- Duel: a round that draws three times (original + two replays) scores a half win each; at 3.5-3.5 after round 7 a
  decider (round 8) is played; a drawn decider replays once and a second draw records a match draw.
- Void (L427): `VoidCurrentPass` (also triggered when a duel misses the combat cap plus 1 s grace) aborts duels and
  reruns the same seats with a new arena seed, no shop, and no counted replay.
- Numbers with no global home live in `MatchRules`: reveal 0 s, 1 s outcome grace after the combat cap, custom shop
  timer 5-300 s, custom starting Crowns 0-100000, spectator delay 15-600 s.
- Pairing cost is lexicographic over sums: immediate-rematch pairs, recent-repeat pairs (previous two opponents; an
  immediate rematch is also a repeat), total |Resolve difference|, total |combat-win difference|. Ties: uniform pick
  among optimal matchings with the recorded round seed (Pairing stream, sub 0).
- Arena: one `ArenaRequest` per pass; every pair of a pass shares that arena (L34). Diameters come from
  `Schedules.ArenaDiameterSequence` over 15 (Conclave) or 8 (Duel) scheduled rounds; replays keep the round's diameter.

## Status / next steps
- Status: complete for Stage 1. `dotnet test tests/Confluence.Match.Tests` passes (77 tests): pairing/bye/side units,
  bracket units, round transitions (timers, early ready, income, drafts, reveal), full 8-player Conclave simulations
  (Resolve schedule, eliminations, tied placements, early end, odd-count byes, pairing optimality, sides), bracket flow
  (full 12-round entry, top-seed byes, draw replay, shared finish, walkover, no drafts/tier 4), Duel BO7, settings and
  fingerprint, determinism and event stream, reconnect window and void reruns.
- Reaching the bracket through all 12 regular rounds is rare by design (30 Resolve of scheduled losses); bracket tests use
  the internal short-schedule constructor plus one searched script that survives the full schedule.
- Next: server adapters implementing IDuelRunner (Sim), IMatchEconomy (Economy), IArenaProvider (Arena); Conclave/Duel
  rating from MatchResult placements (L387); between-duel pause requests and observer slots (L395); bye practice range (L61).
