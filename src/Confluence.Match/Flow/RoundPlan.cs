using System.Collections.Generic;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>One duel inside a pass.</summary>
    internal sealed class Fight
    {
        public Fight(int duelId, SeatedPair seats)
        {
            DuelId = duelId;
            Seats = seats;
            StartedTick = -1;
            ResolvedTick = -1;
        }

        public int DuelId { get; }
        public SeatedPair Seats { get; }
        public DuelSetup Setup { get; set; }
        public DuelOutcome Outcome { get; set; }
        public int StartedTick { get; set; }
        public int ResolvedTick { get; set; }
        public bool IsResolved => Outcome != null;
    }

    /// <summary>A set of simultaneous fights sharing one arena: a round's scheduled fights, a draw replay, or a void rerun.</summary>
    internal sealed class FightPass
    {
        public FightPass(PassKind kind, PassKind origin, int attempt, ArenaPreview arena, List<Fight> fights)
        {
            Kind = kind;
            Origin = origin;
            Attempt = attempt;
            Arena = arena;
            Fights = fights;
        }

        public PassKind Kind { get; }

        /// <summary>
        /// The pass this one stands for: its own kind, or for a void rerun the kind of the voided pass (Original or
        /// DrawReplay), so a rerun of a replay still earns no winner bonus.
        /// </summary>
        public PassKind Origin { get; }

        public int Attempt { get; }
        public ArenaPreview Arena { get; }
        public List<Fight> Fights { get; }

        /// <summary>Set when the pass was voided (L427); its outcomes are discarded.</summary>
        public bool Voided { get; set; }

        /// <summary>A draw replay, or a rerun of one: wins earn no bonus (L193) and the pass carries no byes or income.</summary>
        public bool IsDrawReplay => Origin == PassKind.DrawReplay;

        /// <summary>True when this pass carries the round's byes and shop income (everything except draw replays).</summary>
        public bool CarriesRound => !IsDrawReplay;

        public Fight FightOf(int player)
        {
            foreach (var f in Fights)
                if (f.Seats.Contains(player)) return f;
            return null;
        }
    }

    /// <summary>Everything decided when a round is planned: pairings with sides, byes, who shops, and the draft stage.</summary>
    internal sealed class RoundPlan
    {
        public RoundPlan(int round, int bracketRound, ulong roundSeed, List<SeatedPair> pairs, List<int> byes, List<int> activePlayers)
        {
            Round = round;
            BracketRound = bracketRound;
            RoundSeed = roundSeed;
            Pairs = pairs;
            Byes = byes;
            ActivePlayers = activePlayers;
        }

        public int Round { get; }

        /// <summary>1-3 for Conclave bracket rounds, otherwise 0.</summary>
        public int BracketRound { get; }

        /// <summary>Recorded round seed (Pairing stream, sub 0) for pairing, bye, and side ties (L59, L61).</summary>
        public ulong RoundSeed { get; }

        public List<SeatedPair> Pairs { get; }

        /// <summary>Regular byes, or bracket byes and walkovers.</summary>
        public List<int> Byes { get; }

        /// <summary>Contenders this round: they shop, draft, and finalize loadouts (L45, L61).</summary>
        public List<int> ActivePlayers { get; }

        public bool HasDraft { get; set; }
        public Rarity DraftRarity { get; set; }
        public bool IsFirstShop { get; set; }

        /// <summary>Shop opening per player, for the results screen.</summary>
        public Dictionary<int, ShopOpening> Shops { get; } = new Dictionary<int, ShopOpening>();

        public List<FightPass> Passes { get; } = new List<FightPass>();

        /// <summary>Pairing diagnostics for regular rounds (null otherwise).</summary>
        public PairingSolution Pairing { get; set; }

        public FightPass CurrentPass => Passes.Count == 0 ? null : Passes[Passes.Count - 1];
    }
}
