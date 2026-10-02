using System;
using System.Collections.Generic;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Economics;
using Confluence.Match;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// One live pairing: its duel world on a private arena copy (L34), the two players' input sources, and the replay
    /// being recorded. World index i is the player on spawn side i.
    /// </summary>
    public sealed class RunningDuel
    {
        private readonly IInputSource[] _sources;
        private readonly DuelSeat[] _seats;
        private readonly int[] _consumablesUsed = new int[DuelWorld.PlayerCount];

        internal RunningDuel(DuelSetup setup, HostedArena arena, DuelWorld world, MappedLoadout side0, MappedLoadout side1,
            IInputSource source0, IInputSource source1, ReplayManifest replay)
        {
            Setup = setup;
            Arena = arena;
            World = world;
            Replay = replay;
            _sources = new[] { source0, source1 };
            _seats = new[]
            {
                new DuelSeat(setup, 0, world, arena, side0, side1),
                new DuelSeat(setup, 1, world, arena, side1, side0),
            };
        }

        public DuelSetup Setup { get; }
        public int DuelId => Setup.DuelId;
        public HostedArena Arena { get; }
        public DuelWorld World { get; }

        /// <summary>Null when replay recording is off.</summary>
        public ReplayManifest Replay { get; }

        public IReadOnlyList<DuelSeat> Seats => _seats;

        public bool IsOver => World.IsOver;

        /// <summary>Consumable commitments the Sim reported per side (ResourceEvent ConsumableUsed, L212).</summary>
        public int ConsumablesUsed(int side) => _consumablesUsed[side];

        public bool Involves(int player) => Setup.Involves(player);

        public int SideOf(int player) => Setup.SideOf(player);

        internal void Begin()
        {
            _sources[0].BeginDuel(_seats[0]);
            _sources[1].BeginDuel(_seats[1]);
        }

        /// <summary>Asks both sources for their command, in side order, records them, and advances one tick.</summary>
        internal void Step()
        {
            if (World.IsOver) return;
            PlayerInput a = _sources[0].NextInput(_seats[0]);
            PlayerInput b = _sources[1].NextInput(_seats[1]);
            Replay?.RecordStep(a, b);
            World.Step(a, b);
            IReadOnlyList<DuelEvent> events = World.TickEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] is ResourceEvent r && r.Change == ResourceChange.ConsumableUsed && r.Player >= 0 && r.Player < DuelWorld.PlayerCount)
                {
                    _consumablesUsed[r.Player]++;
                }
            }
        }

        /// <summary>A surrender takes effect at the end of the next step (L53, D-108); recorded for the replay.</summary>
        internal bool Surrender(int side)
        {
            if (World.IsOver) return false;
            World.Surrender(side);
            Replay?.RecordAction(new ReplayAction { BeforeTick = World.Tick + 1, Side = side, Kind = ReplayActionKind.Surrender });
            return true;
        }

        internal void End(DuelReport report)
        {
            _sources[0].EndDuel(_seats[0], report);
            _sources[1].EndDuel(_seats[1], report);
        }

        internal static DuelSeat SeatOf(RunningDuel duel, int side) => duel._seats[side];

        internal void RequireAligned(int duelTick)
        {
            if (World.Tick > duelTick)
            {
                throw new InvalidOperationException("Duel " + DuelId + " is at tick " + World.Tick + ", ahead of the match's duel tick " + duelTick + ".");
            }
        }
    }
}
