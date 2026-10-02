using System;
using Confluence.Core.Time;

namespace Confluence.Casting.Grammars
{
    /// <summary>How a grammar chooses between its stated rewards for the selected spell.</summary>
    public enum RewardChoice : byte
    {
        /// <summary>One reward for every spell.</summary>
        Single = 0,

        /// <summary>Primary reward on damaging spells, alternate on nondamaging ones (e.g. WIZ-002).</summary>
        DamagingOrNot,

        /// <summary>Primary reward on projectiles, alternate otherwise (e.g. WIZ-014).</summary>
        ProjectileOrNot,

        /// <summary>Primary reward on direct spells, alternate on persistent fields and summons (WIZ-025).</summary>
        DirectOrPersistent,

        /// <summary>Chosen by the spell's fixed function tags (WIZ-011 sectors, WIZ-016 stances).</summary>
        SpellFunction,
    }

    /// <summary>
    /// Static facts about one wizard's casting language, taken from its catalogue record (casting_mode,
    /// input_sequence, normal_cast, mastery) and the language list in spec L468. Every wizard has a descriptor;
    /// only grammars marked <see cref="Executable"/> can be created.
    /// </summary>
    public sealed class GrammarDescriptor
    {
        public GrammarDescriptor(string wizardId, string language, GrammarFamily family, InputButtons opens, InputRoute routes,
            double? normalPreparationSec, MasteryReward reward, float magnitude, RewardChoice choice = RewardChoice.Single,
            MasteryReward alternateReward = MasteryReward.None, float alternateMagnitude = 0f, double acceptedIntervalSec = 0.0,
            bool cameraLock = false, bool requiresCombatEvent = false, bool executable = false)
        {
            if (string.IsNullOrEmpty(wizardId)) throw new ArgumentException("Wizard id is required.", nameof(wizardId));
            WizardId = wizardId;
            Language = language;
            Family = family;
            Opens = opens;
            Routes = routes;
            NormalPreparationSec = normalPreparationSec;
            Reward = reward;
            Magnitude = magnitude;
            Choice = choice;
            AlternateReward = alternateReward;
            AlternateMagnitude = alternateMagnitude;
            AcceptedIntervalSec = acceptedIntervalSec;
            CameraLock = cameraLock;
            RequiresCombatEvent = requiresCombatEvent;
            Executable = executable;
        }

        public string WizardId { get; }

        /// <summary>The casting language's name in spec L468 ("crest release", "pressure metering").</summary>
        public string Language { get; }

        public GrammarFamily Family { get; }

        /// <summary>Logical inputs that open preparation (spec L474: class controls act only once a preparation is open).</summary>
        public InputButtons Opens { get; }

        /// <summary>Accepted input routes, including equivalent accessibility routes (spec L365-L369, L490).</summary>
        public InputRoute Routes { get; }

        /// <summary>The record's guided or minimum normal preparation, when it states one (seconds).</summary>
        public double? NormalPreparationSec { get; }

        public int? NormalPreparationTicks => NormalPreparationSec.HasValue ? Ticks.FromSeconds(NormalPreparationSec.Value) : (int?)null;

        /// <summary>The primary mastery reward and its fraction (normally 10-15%, spec L478).</summary>
        public MasteryReward Reward { get; }
        public float Magnitude { get; }

        public RewardChoice Choice { get; }

        /// <summary>The second reward offered by <see cref="Choice"/>, if any (one reward per cast regardless).</summary>
        public MasteryReward AlternateReward { get; }
        public float AlternateMagnitude { get; }

        /// <summary>Stated accepted input interval (spec L478); 0 when the record states none.</summary>
        public double AcceptedIntervalSec { get; }

        /// <summary>A rune or vertex pane holds the camera while its modifier is held (spec L474).</summary>
        public bool CameraLock { get; }

        /// <summary>Mastery needs a genuine combat event (spec L482).</summary>
        public bool RequiresCombatEvent { get; }

        /// <summary>A full implementation exists in this library.</summary>
        public bool Executable { get; }

        public override string ToString() => WizardId + " " + Language + " (" + Family + ")";
    }
}
