using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Time;
using Confluence.Match;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Sim.Duel;

namespace Confluence.Net.Game.Client
{
    /// <summary>
    /// Everything the match screens bind to (Unity view models read it; the headless client acts on it): lobby,
    /// settings, the current phase and its timer, the reveal and arena preview, the private shop and draft, final
    /// loadouts, standings, duel results and reports, the match end, notices, and the last command outcome. All of it is
    /// what the server chose to send this player; nothing here is inferred.
    /// </summary>
    public sealed class ClientMatchView
    {
        public LobbyStateMessage Lobby { get; internal set; }
        public int Seat { get; internal set; } = -1;
        public JoinCode LastJoinCode { get; internal set; }
        public string LastJoinDetail { get; internal set; }
        public MatchSettingsMessage Settings { get; internal set; }
        public PhaseMessage Phase { get; internal set; }
        public RoundRevealMessage Reveal { get; internal set; }
        public ArenaMessage Arena { get; internal set; }
        public Dictionary<string, ArenaMessage> Arenas { get; } = new Dictionary<string, ArenaMessage>();
        public FinalLoadoutsMessage FinalLoadouts { get; internal set; }
        public StandingsMessage Standings { get; internal set; }
        public ShopStateMessage Shop { get; internal set; }
        public DraftStateMessage Draft { get; internal set; }
        public List<DuelResultMessage> Results { get; } = new List<DuelResultMessage>();
        public List<DuelReportMessage> Reports { get; } = new List<DuelReportMessage>();
        public MatchEndMessage End { get; internal set; }
        public List<string> Notices { get; } = new List<string>();
        public List<PhaseMessage> PhaseHistory { get; } = new List<PhaseMessage>();

        public CommandResponse LastCommand { get; internal set; }
        public int CommandsSent { get; internal set; }
        public int CommandsSucceeded { get; internal set; }
        public int CommandsRejected { get; internal set; }
        public int CommandsRateLimited { get; internal set; }
        public int DuplicateResponses { get; internal set; }

        public MatchPhase CurrentPhase => Phase != null ? Phase.Phase : MatchPhase.Lobby;

        public bool IsLaboratory => Settings != null ? Settings.LaboratoryLabel.Length > 0 : (Lobby != null && Lobby.LaboratoryLabel.Length > 0);

        /// <summary>Seconds left in the current phase given the server tick estimate, or -1 when open-ended.</summary>
        public float PhaseSecondsRemaining(double serverTick)
        {
            if (Phase == null || Phase.EndTick < 0) return -1f;
            return (float)Math.Max(0, Phase.EndTick - serverTick) / Ticks.PerSecond;
        }

        public StandingWire StandingOf(int seat)
        {
            if (Standings == null) return null;
            foreach (StandingWire s in Standings.Seats)
                if (s.Seat == seat) return s;
            return null;
        }

        public bool IsEliminated => Seat >= 0 && StandingOf(Seat) != null && StandingOf(Seat).Eliminated;
    }

    /// <summary>The in-combat HUD (spec L347): own vitals, resources, slots and cooldowns, statuses, timer, judgement, grammar.</summary>
    public struct HudView
    {
        public float Health;
        public float MaxHealth;
        public float Shield;
        public float Mana;
        public float MaxMana;
        public float Stamina;
        public float MaxStamina;
        public float HealingRemaining;
        public int SelectedSlot;
        public string SelectedSpellId;
        public float[] CooldownSeconds;
        public string[] SlotSpellIds;
        public List<StatusWire> Statuses;
        public float TimerSeconds;
        public DuelPhase Phase;

        /// <summary>1 ahead, 0 equal, -1 behind, -2 hidden (spec L53: final 15 s).</summary>
        public int Judgement;

        public bool OutsideBoundary;

        /// <summary>The predicted grammar overlay when a local prediction runs, else the server's.</summary>
        public GrammarUiState Grammar;

        public CastPhase CastPhase;

        public static HudView From(ClientDuel duel)
        {
            var hud = new HudView { Judgement = -2 };
            if (duel == null || duel.LatestPrivate == null) return hud;
            PrivateEntry p = duel.LatestPrivate;
            hud.Health = p.Health;
            hud.MaxHealth = p.MaxHealth;
            hud.Shield = p.Shield;
            hud.Mana = p.Mana;
            hud.MaxMana = p.MaxMana;
            hud.Stamina = p.Stamina;
            hud.MaxStamina = p.MaxStamina;
            hud.HealingRemaining = p.HealingRemaining;
            hud.SelectedSlot = p.SelectedSlot;
            hud.SlotSpellIds = duel.Own.SpellIds.ToArray();
            hud.SelectedSpellId = p.SelectedSlot < hud.SlotSpellIds.Length ? hud.SlotSpellIds[p.SelectedSlot] : null;
            hud.CooldownSeconds = new float[p.CooldownTicks.Length];
            for (int i = 0; i < p.CooldownTicks.Length; i++) hud.CooldownSeconds[i] = p.CooldownTicks[i] / (float)Ticks.PerSecond;
            hud.Statuses = duel.Latest != null ? duel.Latest.Players[duel.Side].Statuses : new List<StatusWire>();
            hud.TimerSeconds = duel.TimerSeconds;
            hud.Phase = duel.Phase;
            hud.Judgement = p.Judgement;
            hud.OutsideBoundary = p.BoundaryOutsideTicks > 0;
            bool predicted = duel.Grammar.Available && (duel.Grammar.Phase != CastPhase.Idle || !p.Grammar.Active);
            hud.Grammar = predicted ? duel.Grammar.Ui : p.Grammar;
            hud.CastPhase = predicted ? duel.Grammar.Phase : (CastPhase)p.MachinePhase;
            return hud;
        }
    }

    /// <summary>Delayed spectator view of other duels (spec L49, L391): public frames and events only.</summary>
    public sealed class SpectatorView
    {
        public Dictionary<int, DuelFrame> LatestByDuel { get; } = new Dictionary<int, DuelFrame>();
        public Dictionary<int, int> SeatsOfDuel0 { get; } = new Dictionary<int, int>();
        public Dictionary<int, int> SeatsOfDuel1 { get; } = new Dictionary<int, int>();
        public List<WireDuelEvent> Events { get; } = new List<WireDuelEvent>();
        public int FramesReceived { get; internal set; }

        /// <summary>Match tick at which each frame arrived minus the frame's own match tick (the observed delay).</summary>
        public int MinObservedDelayTicks { get; internal set; } = int.MaxValue;
    }
}
