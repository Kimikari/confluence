using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Arena;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// What an arena does by itself over the preview clock: a Confluence.Reactions world built from the layout (its
    /// natural material units and biome rules, such as the BIO-001 rainfall pulse filling the gutters, spec L335)
    /// stepped tick by tick to the preview's combat time, with the 5 s alcove phase before combat (spec L46-L47). No
    /// players or spells act on it. Scrubbing backwards replays from the first tick, which is cheap and deterministic;
    /// a long catch-up is spread over a few frames (<see cref="MaxStepsPerAdvance"/>).
    /// </summary>
    public sealed class ArenaEnvironment
    {
        public const int MaxStepsPerAdvance = 1200;

        private static readonly IReadOnlyList<ReactionBody> NoBodies = Array.Empty<ReactionBody>();

        private readonly ArenaLayout _layout;

        public ArenaEnvironment(ArenaLayout layout)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Reset();
        }

        public static int AlcoveTicks => Ticks.FromSeconds(GlobalRules.Round.AlcoveSec);

        public ReactionWorld World { get; private set; }

        /// <summary>Ticks stepped so far (the next step is this tick).</summary>
        public int Tick { get; private set; }

        /// <summary>World tick of a preview clock reading: the clock is -5 s at tick 0 and 0 s when combat starts.</summary>
        public static int TickAt(float combatTimeSec)
        {
            return Math.Max(0, AlcoveTicks + (int)Math.Round(combatTimeSec * Ticks.PerSecond));
        }

        /// <summary>Steps toward the clock reading; returns true once the world has reached it.</summary>
        public bool AdvanceTo(float combatTimeSec)
        {
            int target = TickAt(combatTimeSec);
            if (target < Tick)
            {
                Reset();
            }

            int budget = MaxStepsPerAdvance;
            while (Tick < target && budget-- > 0)
            {
                World.Step(Tick, NoBodies);
                Tick++;
            }

            return Tick >= target;
        }

        /// <summary>"Environment (Confluence.Reactions): 42 active cells; water 84 units; no volumes".</summary>
        public string Summary()
        {
            var units = new int[7];
            IReadOnlyList<int> active = World.ActiveCells;
            for (int i = 0; i < active.Count; i++)
            {
                CellUnits u = World.UnitsAtCell(active[i]);
                units[0] += u.Water;
                units[1] += u.Heat;
                units[2] += u.Frost;
                units[3] += u.Dust;
                units[4] += u.Sand;
                units[5] += u.Growth;
                units[6] += u.Venom;
            }

            string[] names = { "water", "heat", "frost", "dust", "sand", "growth", "venom" };
            var parts = new List<string>();
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] > 0)
                {
                    parts.Add(names[i] + " " + units[i].ToString(CultureInfo.InvariantCulture));
                }
            }

            int volumes = World.Volumes.Count;
            return "Environment (Confluence.Reactions): " + active.Count + " active cells"
                + (parts.Count > 0 ? "; units " + string.Join(", ", parts) : string.Empty)
                + "; " + (volumes == 0 ? "no volumes" : volumes + " volumes");
        }

        private void Reset()
        {
            World = new ReactionWorld(_layout, AlcoveTicks);
            Tick = 0;
        }
    }
}
