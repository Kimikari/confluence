using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-315 Boiler Funnel (Silver, SPL-014, exclusive group SPL-014-delivery). Rank 1: "Orb's steam reaction becomes 4
    /// m-long, 2 m-wide jet along selected direction, 3 s." Rank 2: "Jet 5 m long, still 3 s." Drawback: "Dry explosion
    /// 32 becomes 26; reaction consumes orb heat." Counterplay: "Dodge jet line or shoot orb."
    /// <list type="bullet">
    /// <item>When water reaches the resting orb, the reaction still consumes the orb's heat (and one water unit of its
    /// cell per heat unit) but shapes the steam as a 4 m by 2 m jet (rank 2: 5 m) from the orb along the throw's ground
    /// direction — the direction the caster selected by aiming — lasting 3 s; it is one of the caster's clouds.</item>
    /// <item>A dry orb explodes for 26; a steamed one for the base 16.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BoilerFunnel : ISpellTransform
    {
        public const string Id = "AUG-315";

        public static readonly ProseParameter Rank1Length = ProseParameter.Of("rank1_jet_length", 4, "4 m-long");
        public static readonly ProseParameter Width = ProseParameter.Of("jet_width", 2, "2 m-wide jet");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("jet_lifetime", 3, "along selected direction, 3 s");
        public static readonly ProseParameter Rank2Length = ProseParameter.Of("rank2_jet_length", 5, "Jet 5 m long");
        public static readonly ProseParameter DryExplosion = ProseParameter.Of("dry_explosion", 26, "Dry explosion 32 becomes 26");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rank1Length, Width, Lifetime, Rank2Length, DryExplosion };

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is BoilerOrbDelivery orb)) throw new InvalidOperationException(Id + " transforms Boiler Orb (SPL-014).");
            orb.JetLength = rank >= 2 ? Rank2Length.Float : Rank1Length.Float;
            orb.JetWidth = Width.Float;
            orb.JetLifetimeSec = Lifetime.Float;
            form.Payload.Splash = DryExplosion.Float;
        }
    }

    /// <summary>
    /// AUG-316 Boiler Mine (Prismatic, SPL-014, exclusive group SPL-014-delivery). Rank 1: "Orb sticks for 5 s with 35
    /// health; first entrant within 1 m triggers 0.5 s warning then 24 burst." Rank 2: "Health 45 and burst 28; lifetime 5
    /// s." Drawback: "Lose recast/manual burst; mana+4; Heat 3 total unchanged." Counterplay: "Shoot mine or escape
    /// warning."
    /// <list type="bullet">
    /// <item>The orb becomes a proximity mine: it rests 5 s with 35 health (rank 2: 45); the first opponent whose body
    /// comes within 1 m starts a visible 0.5 s warning, then it bursts for 24 (rank 2: 28) within the base 2 m. Escaping
    /// 2 m inside the warning, or shooting it down, avoids the burst. It has no recast.</item>
    /// <item>A mine is a trap: one of the caster's three fields (spec L510; structural delta: trap and field tags).</item>
    /// <item>It still carries the orb's Heat 3: water still steams it, after which its burst is the base 16.</item>
    /// <item>The cast costs 26 mana.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BoilerMine : ISpellTransform
    {
        public const string Id = "AUG-316";

        public static readonly ProseParameter Lifetime = ProseParameter.Of("mine_lifetime", 5, "Orb sticks for 5 s");
        public static readonly ProseParameter Rank1Health = ProseParameter.Of("rank1_mine_health", 35, "with 35 health");
        public static readonly ProseParameter Trigger = ProseParameter.Of("trigger_radius", 1, "first entrant within 1 m");
        public static readonly ProseParameter Warning = ProseParameter.Of("warning", 0.5, "triggers 0.5 s warning");
        public static readonly ProseParameter Rank1Burst = ProseParameter.Of("rank1_mine_burst", 24, "then 24 burst");
        public static readonly ProseParameter Rank2Health = ProseParameter.Of("rank2_mine_health", 45, "Health 45");
        public static readonly ProseParameter Rank2Burst = ProseParameter.Of("rank2_mine_burst", 28, "burst 28");
        public static readonly ProseParameter ExtraMana = ProseParameter.Of("extra_mana", 4, "mana+4");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Lifetime, Rank1Health, Trigger, Warning, Rank1Burst, Rank2Health, Rank2Burst, ExtraMana };

        static BoilerMine() => Wiz002StructuralDeltas.EnsureRegistered();

        public BoilerMine() => Wiz002StructuralDeltas.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is BoilerOrbDelivery orb)) throw new InvalidOperationException(Id + " transforms Boiler Orb (SPL-014).");
            orb.Mine = true;
            orb.LifetimeSec = Lifetime.Float;
            orb.Health = rank >= 2 ? Rank2Health.Float : Rank1Health.Float;
            orb.TriggerRadius = Trigger.Float;
            orb.WarningSec = Warning.Float;
            orb.DetonateOnRecast = false; // "Lose recast/manual burst"
            form.Recast = null;
            form.Payload.Splash = rank >= 2 ? Rank2Burst.Float : Rank1Burst.Float;
            form.Mana += ExtraMana.Float;
        }
    }
}
