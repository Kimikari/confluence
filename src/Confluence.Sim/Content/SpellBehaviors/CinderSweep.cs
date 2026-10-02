using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.SpellBehaviors
{
    /// <summary>
    /// SPL-010 Cinder Sweep (WIZ-002 Bram Cinder): "90-degree cone; check cover per target; caster slows 15% during
    /// windup." Damage "36", thermal; "Heat 1 along the cone dries thin wetness" (one heat unit per covered cell; the
    /// drying is the reactions stage's response). Mana 15, cooldown 5 s, 0.25 s windup, and the 4 m range, which is the
    /// cone's reach, come from the record (spec L525). It is an offensive close melee spell (tags close, melee), so its
    /// direct hit is a strike a frontal guard reduces (spec L104, L535).
    /// </summary>
    [ContentBehavior("SPL-010")]
    public sealed class CinderSweep : ISpellBehavior
    {
        public static readonly ProseParameter ConeAngle = ProseParameter.HalfAngle("cone_half_angle", 90, "90-degree cone");
        public static readonly ProseParameter Damage = ProseParameter.Of("direct", 36, "36");
        public static readonly ProseParameter WindupSlow = ProseParameter.Percent("windup_slow", 15, "caster slows 15% during windup");
        public static readonly ProseParameter ConeHeat = ProseParameter.Of("cone_heat", 1, "Heat 1 along the cone");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { ConeAngle, Damage, WindupSlow, ConeHeat };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new ConeDelivery
            {
                HalfAngleDeg = ConeAngle.Float,
                Reach = form.Range,
                CoverPerTarget = true, // "check cover per target"
            };

            // A stated windup penalty replaces the quick-cast default factor (spec L92).
            form.CastingMoveFactor = 1f - WindupSlow.Float;

            PayloadSpec payload = form.Payload;
            payload.Direct = Damage.Float;
            payload.AddDeposit(DepositKind.Heat, (int)ConeHeat.Value, DepositPlacement.Footprint);
        }
    }
}
