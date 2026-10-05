using System;
using System.Collections.Generic;
using System.Reflection;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Hosting.Bots
{
    /// <summary>
    /// What a bot legally knows when it plans a cast (spec L381): its own state and the world (geometry, props, the
    /// visible reaction cells, its own spell entities), and the opponent only as perceived through the Sim's sight query
    /// after the bot's reaction latency. Hidden opponent inputs and unseen positions are never part of it.
    /// </summary>
    public struct BotSituation
    {
        public DuelWorld World;
        public int Self;

        /// <summary>The step the next command is for.</summary>
        public int Tick;

        public BotProfile Profile;

        /// <summary>The opponent is perceived this tick (sight one reaction latency ago).</summary>
        public bool Sees;

        /// <summary>The perceived (or last remembered) opponent position, feet.</summary>
        public Vec3 Opponent;

        public Vec3 OpponentVelocity;
        public float OpponentHeight;

        /// <summary>Horizontal distance to <see cref="Opponent"/>.</summary>
        public float Distance;

        public DuelPlayer Me => World.Player(Self);
    }

    /// <summary>Where a planned cast aims while its script runs.</summary>
    public enum BotAimMode
    {
        /// <summary>The perceived chest (with lead when the profile leads), like the generic bot.</summary>
        Chest,

        /// <summary>The perceived feet (ground-targeted spells).</summary>
        Feet,

        /// <summary>A fixed world point chosen at planning time.</summary>
        Point,
    }

    /// <summary>One planned cast: the slot to select, its grammar input script, and where to aim while it runs.</summary>
    public sealed class BotCastPlan
    {
        public string SpellId;
        public int Slot;
        public BotCastScript Script;
        public BotAimMode Aim;
        public Vec3 AimPoint;
    }

    /// <summary>
    /// A wizard-specific casting brain for bots: chooses which equipped spell to cast from range and situation, gives
    /// each cast its input script through the wizard's grammar, and presses recasts and the signature. Implementations
    /// live in <c>Bots/Profiles/</c>, one per wizard, and are found by <see cref="BotSpellPlanners"/>; a new instance
    /// serves one bot for one duel. All randomness comes from the bot's own stream passed in.
    /// </summary>
    public interface IBotSpellPlanner
    {
        string WizardId { get; }

        /// <summary>A cast to start now, or false to hold fire this tick.</summary>
        bool TryPlanCast(in BotSituation situation, Rng rng, out BotCastPlan plan);

        /// <summary>
        /// Momentary buttons (Recast, Signature) to press this tick while no grammar script runs; a recast may select
        /// the slot it applies to through <paramref name="slot"/>.
        /// </summary>
        InputButtons ExtraButtons(in BotSituation situation, Rng rng, ref int slot);
    }

    /// <summary>Finds the planner of a wizard among the shipped planner classes (discovery only; no rule depends on order).</summary>
    public static class BotSpellPlanners
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, ConstructorInfo> _byWizard;

        /// <summary>A fresh planner for the wizard, or null when it has none (the generic slot cycling applies).</summary>
        public static IBotSpellPlanner For(string wizardId)
        {
            if (wizardId == null) return null;
            Dictionary<string, ConstructorInfo> map = Map();
            return map.TryGetValue(wizardId, out ConstructorInfo ctor) ? (IBotSpellPlanner)ctor.Invoke(null) : null;
        }

        private static Dictionary<string, ConstructorInfo> Map()
        {
            lock (Gate)
            {
                if (_byWizard != null) return _byWizard;
                var map = new Dictionary<string, ConstructorInfo>(StringComparer.Ordinal);
                foreach (Type type in typeof(BotSpellPlanners).Assembly.GetTypes())
                {
                    if (type.IsAbstract || !typeof(IBotSpellPlanner).IsAssignableFrom(type)) continue;
                    ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;
                    var probe = (IBotSpellPlanner)ctor.Invoke(null);
                    if (map.ContainsKey(probe.WizardId))
                    {
                        throw new InvalidOperationException("Two bot spell planners claim " + probe.WizardId + ".");
                    }

                    map.Add(probe.WizardId, ctor);
                }

                _byWizard = map;
                return map;
            }
        }
    }
}
