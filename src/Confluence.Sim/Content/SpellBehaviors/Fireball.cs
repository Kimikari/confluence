using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.SpellBehaviors
{
    /// <summary>
    /// SPL-009 Fireball (WIZ-002 Bram Cinder): "24 m/s projectile; 1.8 m splash; direct and splash cannot both damage
    /// one target." Damage "48 direct OR 28 splash", thermal; "Heat 2 at impact consumes water and makes a bounded steam
    /// patch" (the steam is the reactions stage's response to the deposit). Mana 18, cooldown 5 s, 0.35 s windup, range
    /// 30 m come from the record (spec L525). The direct hit is a projectile hit, so a frontal guard reduces it (spec
    /// L104); the splash is area damage and is not guarded.
    /// </summary>
    [ContentBehavior("SPL-009")]
    public sealed class Fireball : ISpellBehavior
    {
        public static readonly ProseParameter Speed = ProseParameter.Of("speed", 24, "24 m/s projectile");
        public static readonly ProseParameter SplashRadius = ProseParameter.Of("splash_radius", 1.8, "1.8 m splash");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 48, "48 direct");
        public static readonly ProseParameter Splash = ProseParameter.Of("splash", 28, "28 splash");
        public static readonly ProseParameter ImpactHeat = ProseParameter.Of("impact_heat", 2, "Heat 2 at impact");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Speed, SplashRadius, Direct, Splash, ImpactHeat };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            // The record's range is the projectile's maximum travel (spec L525).
            form.Delivery = new ProjectileDelivery { Speed = Speed.Float, MaxRange = form.Range };

            PayloadSpec payload = form.Payload;
            payload.Direct = Direct.Float;
            payload.Splash = Splash.Float;
            payload.SplashRadius = SplashRadius.Float;
            payload.SplashExcludesDirect = true; // "direct and splash cannot both damage one target"
            payload.AddDeposit(DepositKind.Heat, (int)ImpactHeat.Value, DepositPlacement.Impact);
        }
    }
}
