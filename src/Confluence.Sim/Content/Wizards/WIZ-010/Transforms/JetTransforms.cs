using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-433 Jet Bank (Silver, SPL-073, no exclusive group). Rank 1 "Jet may bank once from a Wet wall at 24 damage."
    /// Rank 2 "Bank damage 28; normal push remains 1.5 m." Drawback "Direct damage 32 becomes 28; consumes Water 1 on wall."
    /// A trajectory branch (spec L286): the jet reflects once off a wall whose base cell holds liquid water, consuming one
    /// water unit there; the banked shot deals the bank damage with the jet's normal push, is stopped by solids and
    /// acquires nothing it does not touch (spec L512). Counterplay "Dry bank surface or dodge reflected path": without
    /// water at the wall (heat dried it, or it was never wet) the jet simply impacts.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class JetBank : ISpellTransform
    {
        public const string Id = "AUG-433";

        public static readonly ProseParameter BankDamage = ProseParameter.Of("bank_damage", 24, "Jet may bank once from a Wet wall at 24 damage.");
        public static readonly ProseParameter BankDamageRank2 = ProseParameter.Of("bank_damage_rank2", 28, "Bank damage 28");
        public static readonly ProseParameter BankPush = ProseParameter.Of("bank_push", 1.5, "normal push remains 1.5 m");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 28, "Direct damage 32 becomes 28");
        public static readonly ProseParameter WallWater = ProseParameter.Of("wall_water", 1, "consumes Water 1 on wall");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { BankDamage, BankDamageRank2, BankPush, Direct, WallWater };

        static JetBank() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            TideProjectileDelivery jet = HalenStructure.Delivery<TideProjectileDelivery>(form, Id);
            form.Payload.Direct = Direct.Float;
            jet.BanksOffWetWalls = true;
            jet.BankWaterCost = (int)WallWater.Value;
            var bank = new PayloadSpec
            {
                Channel = form.Payload.Channel,
                Direct = rank >= 2 ? BankDamageRank2.Float : BankDamage.Float,
                Push = form.Payload.Push, // the jet's normal push ("normal push remains 1.5 m")
                PushDirection = form.Payload.PushDirection,
            };
            HalenStructure.CopyDeposits(form.Payload, bank);
            jet.BankPayload = bank;
        }
    }

    /// <summary>
    /// AUG-434 Jet Mist (Gold, SPL-073, no exclusive group). Rank 1 "Missed shot creates 1 m mist cloud for 1.5 s at
    /// impact." Rank 2 "Cloud 1.5 m for 2 s." Drawback "Push 1.5 becomes 1 m; no Wet from mist." A reaction branch: a shot
    /// that strikes a solid without hitting the opponent leaves a sight-blocking mist cloud (spec L167) owned by the caster
    /// (one of the caster's fields, spec L128, D-149). Mist never makes anyone Wet. Counterplay "Listen or disperse
    /// cloud": mist blocks no sound and wind of strength 2 disperses it (spec L151).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class JetMist : ISpellTransform
    {
        public const string Id = "AUG-434";

        public static readonly ProseParameter Radius = ProseParameter.Of("mist_radius", 1, "Missed shot creates 1 m mist cloud");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("mist_lifetime", 1.5, "for 1.5 s at impact");
        public static readonly ProseParameter RadiusRank2 = ProseParameter.Of("mist_radius_rank2", 1.5, "Cloud 1.5 m");
        public static readonly ProseParameter LifetimeRank2 = ProseParameter.Of("mist_lifetime_rank2", 2, "for 2 s");
        public static readonly ProseParameter Push = ProseParameter.Of("push", 1, "Push 1.5 becomes 1 m");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Lifetime, RadiusRank2, LifetimeRank2, Push };

        static JetMist() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            TideProjectileDelivery jet = HalenStructure.Delivery<TideProjectileDelivery>(form, Id);
            jet.MissCloudRadius = rank >= 2 ? RadiusRank2.Float : Radius.Float;
            jet.MissCloudLifetimeSec = rank >= 2 ? LifetimeRank2.Float : Lifetime.Float;

            // The jet's push drops to 1 m; with Jet Bank the banked shot's "normal push" is that push too (applied later
            // in the spec L286 order, so this drawback wins the conflict).
            form.Payload.Push = Push.Float;
            if (jet.BankPayload != null) jet.BankPayload.Push = Push.Float;
        }
    }
}
