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
    /// <summary>Where a planned cast looks while its script runs.</summary>
    public enum BotAimKind
    {
        /// <summary>The perceived opponent's chest (the default bot aim).</summary>
        Chest,

        /// <summary>The perceived opponent's feet (ground-placed spells).</summary>
        OpponentFeet,

        /// <summary>A fixed world point (<see cref="BotCastPlan.Point"/>), for example a ground point or a hazard.</summary>
        Point,
    }

    /// <summary>One cast a kit planner chose: which slot (or the signature), where to look, and an optional recast.</summary>
    public sealed class BotCastPlan
    {
        /// <summary>The equipped slot to cast; ignored for the signature.</summary>
        public int Slot { get; set; }

        /// <summary>The signature technique (its own binding, no grammar, spec L472).</summary>
        public bool Signature { get; set; }

        /// <summary>The spell id (diagnostics and tests).</summary>
        public string SpellId { get; set; }

        public BotAimKind Aim { get; set; }

        public Vec3 Point { get; set; }

        /// <summary>Whether this cast tries the grammar's mastery timing (the script still decides if it lands).</summary>
        public bool AttemptMastery { get; set; }

        /// <summary>Ticks after the release to press the spell-recast binding once; negative = never.</summary>
        public int RecastAfterTicks { get; set; } = -1;

        /// <summary>A short reason (diagnostics).</summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// What a kit planner may know when it chooses (spec L381: only legal information). The opponent's position and
    /// velocity are what the bot perceived through the Sim's sight query one reaction latency ago; everything else is the
    /// bot's own state or public, visible world state (ground materials, telegraphed windups of a seen opponent).
    /// </summary>
    public struct BotKitView
    {
        public DuelWorld World;
        public int Self;
        public DuelPlayer Me;
        public int Tick;
        public BotProfile Profile;

        /// <summary>The opponent was seen at the perceived observation.</summary>
        public bool Sees;
        public Vec3 OpponentPosition;
        public Vec3 OpponentVelocity;
        public float Distance;

        /// <summary>The perceived opponent was visibly winding up or releasing a cast.</summary>
        public bool OpponentCasting;

        /// <summary>The bot is pressing in for melee (planners should keep to close-range options).</summary>
        public bool Rushing;

        /// <summary>The opponent's wizard (public at selection, spec L292).</summary>
        public string OpponentWizardId;
    }

    /// <summary>
    /// A wizard's casting brain for bots: chooses a spell by range and situation and returns the input script for it. The
    /// script is still judged by the real grammar (spec L425). Planners are found by reflection in the Hosting assembly
    /// (one per wizard id, parameterless constructor), so each kit adds its own file without editing a shared table.
    /// </summary>
    public interface IBotKitPlanner
    {
        string WizardId { get; }

        /// <summary>The next cast, or null to hold fire for now.</summary>
        BotCastPlan Choose(in BotKitView view, Rng rng);

        /// <summary>The input script for a chosen plan.</summary>
        BotCastScript ScriptFor(BotCastPlan plan, BotProfile profile, Rng rng);
    }

    /// <summary>The registered kit planners, by wizard id.</summary>
    public static class BotKitPlanners
    {
        private static readonly Lazy<Dictionary<string, IBotKitPlanner>> All = new Lazy<Dictionary<string, IBotKitPlanner>>(Scan);

        /// <summary>The planner of a wizard, or null (the bot then uses its generic slot rotation).</summary>
        public static IBotKitPlanner For(string wizardId) =>
            wizardId != null && All.Value.TryGetValue(wizardId, out IBotKitPlanner planner) ? planner : null;

        private static Dictionary<string, IBotKitPlanner> Scan()
        {
            var found = new Dictionary<string, IBotKitPlanner>(StringComparer.Ordinal);
            Type[] types = typeof(IBotKitPlanner).GetTypeInfo().Assembly.GetTypes();
            Array.Sort(types, (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            foreach (Type t in types)
            {
                if (t.IsAbstract || t.IsInterface || !typeof(IBotKitPlanner).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
                var planner = (IBotKitPlanner)Activator.CreateInstance(t);
                if (found.ContainsKey(planner.WizardId)) throw new InvalidOperationException("Two bot planners for " + planner.WizardId + ".");
                found.Add(planner.WizardId, planner);
            }

            return found;
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
