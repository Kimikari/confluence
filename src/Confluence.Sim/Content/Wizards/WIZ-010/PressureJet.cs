using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-073 Pressure Jet (WIZ-010 Halen Tide, ranged push): "35 m/s stream projectile pushes hit target 1.5 m;
    /// collision push damage is never added." Damage "32", physical; "Water 1 wets the first impact surface". Mana 16,
    /// cooldown 5 s, 0.25 s windup and the 20 m range (the stream's maximum travel) come from the record (spec L525).
    /// <list type="bullet">
    /// <item>The push runs along the stream (guard reduces it with the damage, spec L104) and adds no damage of its own:
    /// the duel never adds collision damage to a pushed player; only the global boundary edge rule (spec L51) can apply.</item>
    /// <item>The narrow line (counterplay "Sidestep its narrow line") is the default projectile footprint; solid cover stops
    /// it ("anchor behind cover").</item>
    /// <item>Water 1 lands on the first surface it strikes (the ground under a struck body, a wall's base cell, or the
    /// floor) and stays for <see cref="HalenRules.ImpactWaterLifetimeSec"/>.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PressureJet : ISpellBehavior
    {
        public const string Id = "SPL-073";

        public static readonly ProseParameter Speed = ProseParameter.Of("speed", 35, "35 m/s stream projectile");
        public static readonly ProseParameter Push = ProseParameter.Of("push", 1.5, "pushes hit target 1.5 m");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 32, "32");
        public static readonly ProseParameter ImpactWater = ProseParameter.Of("impact_water", 1, "Water 1 wets the first impact surface");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Speed, Push, Direct, ImpactWater };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new TideProjectileDelivery { Speed = Speed.Float, MaxRange = form.Range };

            PayloadSpec payload = form.Payload;
            payload.Direct = Direct.Float;
            payload.Push = Push.Float;
            payload.PushDirection = PushDirection.AlongDelivery;
            payload.AddDeposit(DepositKind.Water, (int)ImpactWater.Value, DepositPlacement.Impact, 0f, HalenRules.ImpactWaterLifetimeSec);
        }
    }
}
