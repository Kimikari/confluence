using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-441 Cascade Landing (Gold, SPL-077, no exclusive group). Rank 1 "Landing creates 1 m puddle for 3 s, Water 1, no
    /// damage." Rank 2 "Puddle radius 1.5 m for 4 s." Drawback "Leap range 6 becomes 5 m." The landing's Water 1 becomes a
    /// puddle around the landing point (the launch cell keeps its Water 1; one cast still adds at most Water 2 to a cell).
    /// Counterplay "Attack landing or exploit conductivity": the landing is fixed and the puddle conducts (spec L148).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CascadeLanding : ISpellTransform
    {
        public const string Id = "AUG-441";

        public static readonly ProseParameter Radius = ProseParameter.Of("puddle_radius", 1, "Landing creates 1 m puddle for 3 s");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("puddle_lifetime", 3, "puddle for 3 s");
        public static readonly ProseParameter Water = ProseParameter.Of("puddle_water", 1, "Water 1, no damage");
        public static readonly ProseParameter RadiusRank2 = ProseParameter.Of("puddle_radius_rank2", 1.5, "Puddle radius 1.5 m for 4 s");
        public static readonly ProseParameter LifetimeRank2 = ProseParameter.Of("puddle_lifetime_rank2", 4, "Puddle radius 1.5 m for 4 s");
        public static readonly ProseParameter Range = ProseParameter.Of("leap_range", 5, "Leap range 6 becomes 5 m");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Lifetime, Water, RadiusRank2, LifetimeRank2, Range };

        static CascadeLanding() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            MovementDelivery leap = HalenStructure.Delivery<MovementDelivery>(form, Id);
            if (!(form.Script is CascadeVaultScript script)) throw new InvalidOperationException(Id + " needs SPL-077's script.");
            form.Range = Range.Float;
            leap.Distance = Range.Float;
            // The landing's Water 1 becomes the puddle: a gameplay field of its own (spec L128, L510), created by the
            // script at the landing, so the plain landing deposit is removed (one landing deposit, never two).
            for (int i = 0; i < form.Payload.Deposits.Count; i++)
            {
                DepositSpec d = form.Payload.Deposits[i];
                if (d.Kind == DepositKind.Water && d.Placement == DepositPlacement.Impact) d.Strength = 0;
            }

            script.LandingPuddleWater = (int)Water.Value;
            script.LandingPuddleRadius = rank >= 2 ? RadiusRank2.Float : Radius.Float;
            script.LandingPuddleLifetimeSec = rank >= 2 ? LifetimeRank2.Float : Lifetime.Float;
        }
    }

    /// <summary>
    /// AUG-442 Cascade Brake (Prismatic, SPL-077, no exclusive group). Rank 1 "Recast midair shortens landing point along
    /// same arc up to 2 m." Rank 2 "Shortening up to 3 m, never extends leap." Drawback "Mana +4; no invulnerability or extra
    /// jump." The spell-recast binding while airborne pulls the landing back toward the launch along the arc (same airtime,
    /// same vertical arc), never past the original landing; the recast is visible as a pending state and a telegraphed new
    /// landing (spec L549). Counterplay "Track arc and delayed adjustment".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CascadeBrake : ISpellTransform
    {
        public const string Id = "AUG-442";

        public static readonly ProseParameter Brake = ProseParameter.Of("brake", 2, "shortens landing point along same arc up to 2 m");
        public static readonly ProseParameter BrakeRank2 = ProseParameter.Of("brake_rank2", 3, "Shortening up to 3 m, never extends leap");
        public static readonly ProseParameter ManaIncrease = ProseParameter.Of("mana_increase", 4, "Mana +4");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Brake, BrakeRank2, ManaIncrease };

        static CascadeBrake() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Script is CascadeVaultScript script)) throw new InvalidOperationException(Id + " needs SPL-077's script.");
            form.Mana += ManaIncrease.Float;
            form.Recast = RecastRule.Default; // once while the leap lasts, no further payment (spec L94)
            script.BrakeMeters = rank >= 2 ? BrakeRank2.Float : Brake.Float;
        }
    }
}
