using System;
using System.Collections.Generic;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// Casting grammars keyed by wizard id. Every one of the 36 wizards has a <see cref="GrammarDescriptor"/>
    /// (family, opening inputs, routes, stated normal preparation, rewards) transcribed from its catalogue record.
    /// <see cref="Create"/> returns a working grammar only where one is implemented; for any other wizard it throws
    /// <see cref="NotImplementedException"/> rather than silently casting with a substitute.
    /// </summary>
    public static class GrammarRegistry
    {
        private const InputButtons P = InputButtons.Primary;
        private const InputButtons M = InputButtons.CastModifier;
        private const InputRoute S = InputRoute.Standard;
        private const InputRoute G = InputRoute.Guided;

        private static readonly GrammarDescriptor[] Table =
        {
            // id, language (spec L468), family, opens, routes, stated normal preparation, reward(s): catalogue record text.
            D("WIZ-001", "held focus", GrammarFamily.AimPath, P, S, 0.20, MasteryReward.ProjectileSpeed, 0.12f, RewardChoice.ProjectileOrNot, MasteryReward.WindupReduction, 0.12f),
            new GrammarDescriptor("WIZ-002", "crest release", GrammarFamily.ButtonTiming, P, S, 0.20, MasteryReward.DirectDamage, PressureReleaseGrammar.RewardFraction,
                RewardChoice.DamagingOrNot, MasteryReward.ManaEfficiency, PressureReleaseGrammar.RewardFraction, executable: true),
            D("WIZ-003", "rune tracing", GrammarFamily.GestureStroke, P | M, S | InputRoute.DirectionKeys | G, 0.30, MasteryReward.ManaEfficiency, 0.15f, cameraLock: true),
            D("WIZ-004", "voice", GrammarFamily.VoiceRhythm, P, InputRoute.Voice | InputRoute.Rhythm | G, 0.75, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-005", "token syntax", GrammarFamily.KeySequence, M, S, null, MasteryReward.WindupReduction, 0.10f),
            D("WIZ-006", "parry response", GrammarFamily.CombatEvent, P, S, 0.25, MasteryReward.WindupReduction, 0.12f, combatEvent: true),
            D("WIZ-007", "instrument rhythm", GrammarFamily.VoiceRhythm, P, InputRoute.Rhythm | G, 0.50, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-008", "circuit routing", GrammarFamily.WidgetPuzzle, M, S, null, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-009", "sigil geometry", GrammarFamily.GestureStroke, M, S | InputRoute.DirectionKeys, null, MasteryReward.ProjectileSpeed, 0.10f, RewardChoice.DamagingOrNot, MasteryReward.ManaEfficiency, 0.15f, cameraLock: true),
            new GrammarDescriptor("WIZ-010", "pressure metering", GrammarFamily.HeldGauge, P | InputButtons.ModifierA, S, PressureMeterGrammar.ReleaseOpenSec,
                MasteryReward.ManaEfficiency, PressureMeterGrammar.RewardFraction, acceptedIntervalSec: PressureMeterGrammar.AcceptedIntervalSec, executable: true),
            D("WIZ-011", "phase selection", GrammarFamily.ButtonTiming, P, S, 0.30, MasteryReward.DirectDamage, 0.10f, RewardChoice.SpellFunction, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-012", "polarity alternation", GrammarFamily.ButtonTiming, InputButtons.ModifierA | InputButtons.ModifierB, S, 0.25, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-013", "anchor distance", GrammarFamily.Movement, P, S, 0.30, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-014", "footwork", GrammarFamily.Movement, P, S | G, 0.35, MasteryReward.ProjectileSpeed, 0.12f, RewardChoice.ProjectileOrNot, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-015", "melee weaving", GrammarFamily.CombatEvent, P, S, 0.25, MasteryReward.DirectDamage, 0.12f, RewardChoice.DamagingOrNot, MasteryReward.ManaEfficiency, 0.12f, combatEvent: true),
            D("WIZ-016", "stances", GrammarFamily.WidgetPuzzle, M | P, S, 0.25, MasteryReward.ProjectileSpeed, 0.10f, RewardChoice.SpellFunction, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-017", "memory", GrammarFamily.SchedulingMemory, M, S | InputRoute.StaticGuide, null, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-018", "delayed scheduling", GrammarFamily.SchedulingMemory, M | P, S, 0.30, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-019", "orbit catching", GrammarFamily.ButtonTiming, P, S | G, 0.50, MasteryReward.ProjectileSpeed, 0.12f, RewardChoice.ProjectileOrNot, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-020", "thread weaving", GrammarFamily.ButtonTiming, P, S | G, 0.40, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-021", "frequency tuning", GrammarFamily.HeldGauge, P, S, 0.30, MasteryReward.DirectDamage, 0.10f, RewardChoice.DamagingOrNot, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-022", "hand chords", GrammarFamily.KeySequence, M, S | InputRoute.SingleSelector, 0.20, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-023", "tile swaps", GrammarFamily.WidgetPuzzle, M, S | G, 0.40, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-024", "packet division", GrammarFamily.WidgetPuzzle, M, S, 0.30, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-025", "reticle painting", GrammarFamily.AimPath, P, S, 0.25, MasteryReward.DirectDamage, 0.12f, RewardChoice.DirectOrPersistent, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-026", "ratio balance", GrammarFamily.WidgetPuzzle, M, S, 0.35, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-027", "ground contact", GrammarFamily.Movement, M | P, S, 0.35, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-028", "prediction alignment", GrammarFamily.AimPath, P, S, 0.25, MasteryReward.ProjectileSpeed, 0.12f, RewardChoice.ProjectileOrNot, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-029", "velocity alignment", GrammarFamily.Movement, M | P, S, 0.30, MasteryReward.ProjectileSpeed, 0.12f, RewardChoice.ProjectileOrNot, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-030", "ritual walking", GrammarFamily.Movement, M | P, S, 0.35, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-031", "kinetic engraving", GrammarFamily.GestureStroke, M, S | G, 0.35, MasteryReward.DirectDamage, 0.12f, RewardChoice.DamagingOrNot, MasteryReward.ManaEfficiency, 0.12f),
            D("WIZ-032", "vitality pledges", GrammarFamily.ButtonTiming, P, S, 0.30, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-033", "conditional clauses", GrammarFamily.WidgetPuzzle, M, S, 0.30, MasteryReward.ManaEfficiency, 0.15f),
            D("WIZ-034", "spatial rotation", GrammarFamily.WidgetPuzzle, M, S | G, 0.35, MasteryReward.ManaEfficiency, 0.15f, acceptedIntervalSec: 0.10),
            D("WIZ-035", "event response", GrammarFamily.CombatEvent, M, S, 0.30, MasteryReward.ManaEfficiency, 0.12f, combatEvent: true),
            D("WIZ-036", "cursor shepherding", GrammarFamily.HeldGauge, M, S | G, 0.60, MasteryReward.ManaEfficiency, 0.15f),
        };

        private static readonly Dictionary<string, GrammarDescriptor> ById = BuildIndex();

        /// <summary>All 36 descriptors in wizard id order.</summary>
        public static IReadOnlyList<GrammarDescriptor> All { get; } = Array.AsReadOnly(Table);

        public static bool TryDescribe(string wizardId, out GrammarDescriptor descriptor) => ById.TryGetValue(wizardId ?? string.Empty, out descriptor);

        public static GrammarDescriptor Describe(string wizardId)
        {
            if (TryDescribe(wizardId, out var d)) return d;
            throw new KeyNotFoundException("No casting grammar descriptor for '" + wizardId + "'.");
        }

        public static bool IsExecutable(string wizardId) => TryDescribe(wizardId, out var d) && d.Executable;

        /// <summary>A new instance of the wizard's full grammar, or <see cref="NotImplementedException"/> if it has none yet.</summary>
        public static IGrammar Create(string wizardId)
        {
            var d = Describe(wizardId);
            switch (d.WizardId)
            {
                case PressureReleaseGrammar.Id: return new PressureReleaseGrammar();
                case PressureMeterGrammar.Id: return new PressureMeterGrammar();
                default:
                    throw new NotImplementedException(d.WizardId + " casting grammar '" + d.Language + "' (" + d.Family +
                        ") is not implemented; it cannot cast until it is.");
            }
        }

        /// <summary>
        /// The wizard's guided normal route (spec L470): its stated normal preparation, a normal cast, no reward.
        /// This is the accessibility/bot route, not the wizard's grammar; it throws when the record states no fixed
        /// normal preparation (for example WIZ-005, whose normal cast is the token sequence itself).
        /// </summary>
        public static GuidedNormalGrammar CreateGuided(string wizardId)
        {
            var d = Describe(wizardId);
            if (!d.NormalPreparationTicks.HasValue)
            {
                throw new NotImplementedException(d.WizardId + " '" + d.Language + "' states no fixed normal preparation, so it has no guided route.");
            }

            InputButtons opening = (d.Opens & InputButtons.Primary) != 0 ? InputButtons.Primary : d.Opens;
            return new GuidedNormalGrammar(d.WizardId, d.Family, d.NormalPreparationTicks.Value, LowestBit(opening));
        }

        private static GrammarDescriptor D(string id, string language, GrammarFamily family, InputButtons opens, InputRoute routes, double? normalPrepSec,
            MasteryReward reward, float magnitude, RewardChoice choice = RewardChoice.Single, MasteryReward alternate = MasteryReward.None,
            float alternateMagnitude = 0f, double acceptedIntervalSec = 0.0, bool cameraLock = false, bool combatEvent = false) =>
            new GrammarDescriptor(id, language, family, opens, routes, normalPrepSec, reward, magnitude, choice, alternate, alternateMagnitude,
                acceptedIntervalSec, cameraLock, combatEvent, executable: false);

        private static Dictionary<string, GrammarDescriptor> BuildIndex()
        {
            var index = new Dictionary<string, GrammarDescriptor>(StringComparer.Ordinal);
            foreach (var d in Table) index.Add(d.WizardId, d);
            return index;
        }

        private static InputButtons LowestBit(InputButtons set)
        {
            uint v = (uint)set;
            return (InputButtons)(v & (~v + 1u));
        }
    }
}
