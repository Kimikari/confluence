using System;
using System.Collections.Generic;

namespace Confluence.Match.Tests.Fakes
{
    /// <summary>Scripted result of one fake duel.</summary>
    internal sealed class DuelScript
    {
        private DuelScript(int winner, DuelEndReason reason, int combatTick, bool neverResolves, string[] winnerFeats)
        {
            Winner = winner;
            Reason = reason;
            CombatTick = combatTick;
            NeverResolves = neverResolves;
            WinnerFeats = winnerFeats ?? new string[0];
        }

        public int Winner { get; }
        public DuelEndReason Reason { get; }

        /// <summary>Combat ticks after the alcoves open at which the duel resolves.</summary>
        public int CombatTick { get; }

        public bool NeverResolves { get; }
        public string[] WinnerFeats { get; }

        public static DuelScript Win(int winner, int combatTick = 60, params string[] feats) =>
            new DuelScript(winner, DuelEndReason.Death, combatTick, false, feats);

        public static DuelScript Draw(int combatTick = 60) => new DuelScript(MatchRules.NoPlayer, DuelEndReason.SimultaneousLethal, combatTick, false, null);

        public static DuelScript Never() => new DuelScript(MatchRules.NoPlayer, DuelEndReason.Time, 0, true, null);

        public DuelOutcome Build(DuelSetup setup)
        {
            if (Winner == MatchRules.NoPlayer)
            {
                return DuelOutcome.Draw(setup.DuelId, setup.Side0Player, setup.Side1Player, Reason, CombatTick, 0f, 0f);
            }

            return DuelOutcome.Win(setup.DuelId, Winner, setup.OpponentOf(Winner), Reason, CombatTick, 0.4f, 0f, WinnerFeats);
        }
    }

    /// <summary>Duel runner that resolves each duel at its script's combat tick and records every call.</summary>
    internal sealed class FakeDuelRunner : IDuelRunner
    {
        private readonly Dictionary<int, (DuelSetup Setup, DuelScript Script)> _running = new Dictionary<int, (DuelSetup, DuelScript)>();

        public FakeDuelRunner(Func<DuelSetup, DuelScript> decide)
        {
            Decide = decide ?? throw new ArgumentNullException(nameof(decide));
        }

        public Func<DuelSetup, DuelScript> Decide { get; set; }
        public List<DuelSetup> Started { get; } = new List<DuelSetup>();
        public List<int> Aborted { get; } = new List<int>();
        public int Polls { get; private set; }

        public void StartDuel(DuelSetup setup)
        {
            Assert.False(_running.ContainsKey(setup.DuelId), "duel ids must be unique");
            Started.Add(setup);
            _running[setup.DuelId] = (setup, Decide(setup));
        }

        public bool Poll(int duelId, int duelTick, out DuelOutcome outcome)
        {
            Polls++;
            var (setup, script) = _running[duelId];
            Assert.False(Aborted.Contains(duelId), "aborted duels are never polled");
            if (script.NeverResolves || duelTick < setup.AlcoveTicks + script.CombatTick)
            {
                outcome = null;
                return false;
            }

            outcome = script.Build(setup);
            return true;
        }

        public void AbortDuel(int duelId) => Aborted.Add(duelId);

        /// <summary>The higher strength wins; equal strengths draw.</summary>
        public static Func<DuelSetup, DuelScript> ByStrength(params int[] strength) => setup =>
        {
            int a = setup.Side0Player, b = setup.Side1Player;
            if (strength[a] == strength[b]) return DuelScript.Draw();
            return DuelScript.Win(strength[a] > strength[b] ? a : b);
        };

        /// <summary>The lower player id always wins.</summary>
        public static DuelScript LowerIdWins(DuelSetup setup) => DuelScript.Win(Math.Min(setup.Side0Player, setup.Side1Player));
    }
}
