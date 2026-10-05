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

        /// <summary>The perceived opponent was visibly winding up or releasing a cast (spec L96).</summary>
        public bool OpponentCasting;

        /// <summary>The bot is pressing in for melee (profiles keep to close-range options or hold fire).</summary>
        public bool Rushing;

        /// <summary>The opponent's wizard (public at selection, spec L292).</summary>
        public string OpponentWizardId;

        public DuelPlayer Me => World.Player(Self);
    }

    /// <summary>Where a planned cast aims while its script runs and through its windup until the spell releases.</summary>
    public enum BotAimMode
    {
        /// <summary>The perceived chest (with lead when the profile leads), like the generic bot.</summary>
        Chest,

        /// <summary>The perceived feet (ground-targeted spells).</summary>
        Feet,

        /// <summary>A fixed world point chosen at planning time (<see cref="BotCastPlan.AimPoint"/>).</summary>
        Point,
    }

    /// <summary>
    /// One planned cast: the slot to select (or the signature), its grammar input script, where to aim, and an optional
    /// follow-up recast.
    /// </summary>
    public sealed class BotCastPlan
    {
        /// <summary>The spell id (diagnostics and tests; the wizard id for the signature).</summary>
        public string SpellId { get; set; }

        /// <summary>The equipped slot to cast; ignored for the signature.</summary>
        public int Slot { get; set; }

        /// <summary>The signature technique (its own binding, no grammar, spec L472).</summary>
        public bool Signature { get; set; }

        /// <summary>
        /// The input script, judged by the real grammar (spec L425). Null: the bot uses a <see cref="SignatureScript"/>
        /// for the signature and the wizard's generic script otherwise.
        /// </summary>
        public BotCastScript Script { get; set; }

        public BotAimMode Aim { get; set; }

        public Vec3 AimPoint { get; set; }

        /// <summary>Whether this cast tries the grammar's mastery timing (the script still decides if it lands).</summary>
        public bool AttemptMastery { get; set; }

        /// <summary>Ticks after the script ends to press the spell-recast binding once; negative = never.</summary>
        public int RecastAfterTicks { get; set; } = -1;

        /// <summary>A short reason (diagnostics).</summary>
        public string Reason { get; set; }
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

        /// <summary>A cast to start now (with its script, or a signature press), or false to hold fire for now.</summary>
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
                Type[] types = typeof(BotSpellPlanners).Assembly.GetTypes();
                Array.Sort(types, (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
                foreach (Type type in types)
                {
                    if (type.IsAbstract || type.IsInterface || !typeof(IBotSpellPlanner).IsAssignableFrom(type)) continue;
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

    /// <summary>A one-tick press of the signature binding (signatures have no grammar, spec L472).</summary>
    public sealed class SignatureScript : BotCastScript
    {
        public SignatureScript()
        {
            ReleaseTick = 1;
        }

        public override InputButtons ButtonsAt(int k) => k == 0 ? InputButtons.Signature : InputButtons.None;
    }
}
