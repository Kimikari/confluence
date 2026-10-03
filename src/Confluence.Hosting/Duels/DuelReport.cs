using System.Collections.Generic;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Economics;
using Confluence.Match;
using Confluence.Sim.Duel;
using MatchDuelOutcome = Confluence.Match.DuelOutcome;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// What one player's input source may use during a duel: its own identity and loadout, the opponent's final
    /// loadout (public from the alcove on, L46), the arena (public, L329), and the world. Sources must read the
    /// opponent's state only through the world's sight queries (bots: no wallhacks, L72, L381).
    /// </summary>
    public sealed class DuelSeat
    {
        internal DuelSeat(DuelSetup setup, int index, DuelWorld world, HostedArena arena, MappedLoadout own, MappedLoadout opponent)
        {
            Setup = setup;
            Index = index;
            World = world;
            Arena = arena;
            Loadout = own;
            OpponentLoadout = opponent;
        }

        public DuelSetup Setup { get; }
        public int DuelId => Setup.DuelId;

        /// <summary>World player index, equal to the spawn side.</summary>
        public int Index { get; }

        /// <summary>Match player id.</summary>
        public int Player => Index == 0 ? Setup.Side0Player : Setup.Side1Player;

        public int Opponent => Index == 0 ? Setup.Side1Player : Setup.Side0Player;

        public DuelWorld World { get; }
        public HostedArena Arena { get; }
        public MappedLoadout Loadout { get; }
        public MappedLoadout OpponentLoadout { get; }
    }

    /// <summary>One side of a finished duel for the results screen (spec L49, L359).</summary>
    public sealed class DuelSideReport
    {
        internal DuelSideReport(int player, int side, string wizardId, float healthFraction, IReadOnlyList<string> feats,
            IReadOnlyList<DuelEvent> decisiveSequence, int consumablesUsed, float damageDealt)
        {
            Player = player;
            Side = side;
            WizardId = wizardId;
            HealthFraction = healthFraction;
            Feats = feats;
            DecisiveSequence = decisiveSequence;
            ConsumablesUsed = consumablesUsed;
            DamageDealt = damageDealt;
        }

        public int Player { get; }
        public int Side { get; }
        public string WizardId { get; }
        public float HealthFraction { get; }

        /// <summary>FEAT ids met in this duel (L251-L280).</summary>
        public IReadOnlyList<string> Feats { get; }

        /// <summary>
        /// The decisive sequence (L359): incoming attacks with reaction contribution and mitigation, status control,
        /// displacement, and the final damage, over the last seconds (D-111).
        /// </summary>
        public IReadOnlyList<DuelEvent> DecisiveSequence { get; }

        public int ConsumablesUsed { get; }

        /// <summary>Post-mitigation damage dealt to the opponent (FEAT-001 counter).</summary>
        public float DamageDealt { get; }
    }

    /// <summary>A finished, non-voided duel: Match's outcome, the Sim result, both sides' reports, and the replay manifest.</summary>
    public sealed class DuelReport
    {
        internal DuelReport(DuelSetup setup, MatchDuelOutcome outcome, DuelResult simResult, int finalTick, ulong finalStateHash,
            IReadOnlyList<DuelSideReport> sides, ReplayManifest replay)
        {
            Setup = setup;
            Outcome = outcome;
            SimResult = simResult;
            FinalTick = finalTick;
            FinalStateHash = finalStateHash;
            Sides = sides;
            Replay = replay;
        }

        public DuelSetup Setup { get; }
        public MatchDuelOutcome Outcome { get; }
        public DuelResult SimResult { get; }

        /// <summary>World tick of the final step (alcove included).</summary>
        public int FinalTick { get; }

        public ulong FinalStateHash { get; }

        /// <summary>By spawn side.</summary>
        public IReadOnlyList<DuelSideReport> Sides { get; }

        /// <summary>Null when replay recording is off.</summary>
        public ReplayManifest Replay { get; }

        public DuelSideReport For(int player) => Sides[0].Player == player ? Sides[0] : Sides[1];
    }
}
