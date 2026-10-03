using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-313 Rush Return (Gold, SPL-013, no exclusive group). Rank 1: "Dash leaves 25-health departure ember for 3 s;
    /// recast dashes back up to 6 m along clear ground route." Rank 2: "Ember lasts 4 s; return up to 7 m." Drawback:
    /// "Initial burst 30 becomes 22; at most 2 dash charges total." Counterplay: "Destroy ember or block return route."
    /// <list type="bullet">
    /// <item>The dash leaves a visible 25-health ember at its start (a destructible anchor, spec L512, L527). While it
    /// stands, the spell recast dashes the caster back toward it along the ground, up to 6 m (rank 2: 7 m); geometry on
    /// the route stops the dash (spec L124 motor), and breaking the ember cancels the return. A recast during the
    /// outbound dash waits for it to end. The return has no burst and lays no trail.</item>
    /// <item>The cast dashes at most twice (outbound and return; spec L124 two dash charges); the endpoint burst is 22.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RushReturn : ISpellTransform
    {
        public const string Id = "AUG-313";

        public static readonly ProseParameter EmberHealth = ProseParameter.Of("ember_health", 25, "25-health departure ember");
        public static readonly ProseParameter Rank1Lifetime = ProseParameter.Of("rank1_ember_lifetime", 3, "departure ember for 3 s");
        public static readonly ProseParameter Rank1Return = ProseParameter.Of("rank1_return", 6, "dashes back up to 6 m");
        public static readonly ProseParameter Rank2Lifetime = ProseParameter.Of("rank2_ember_lifetime", 4, "Ember lasts 4 s");
        public static readonly ProseParameter Rank2Return = ProseParameter.Of("rank2_return", 7, "return up to 7 m");
        public static readonly ProseParameter Burst = ProseParameter.Of("endpoint_burst", 22, "Initial burst 30 becomes 22");
        public static readonly ProseParameter DashCharges = ProseParameter.Of("dash_charges", 2, "at most 2 dash charges total");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { EmberHealth, Rank1Lifetime, Rank1Return, Rank2Lifetime, Rank2Return, Burst, DashCharges };

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is RushDelivery rush)) throw new InvalidOperationException(Id + " transforms Phoenix Rush (SPL-013).");
            rush.ReturnDistance = rank >= 2 ? Rank2Return.Float : Rank1Return.Float;
            rush.EmberLifetimeSec = rank >= 2 ? Rank2Lifetime.Float : Rank1Lifetime.Float;
            rush.EmberHealth = EmberHealth.Float;
            form.Payload.Splash = Burst.Float;

            // The outbound dash and the one return are the cast's two dashes ("at most 2 dash charges total").
            int returns = (int)DashCharges.Value - 1;
            form.Recast = new RecastRule((form.Recast?.MaxRecasts ?? 0) + returns);
        }
    }

    /// <summary>
    /// AUG-314 Rush Brake (Prismatic, SPL-013, no exclusive group). Rank 1: "Recast during dash stops early and emits 1.2 m
    /// burst of 24 damage once." Rank 2: "Brake burst 28; full endpoint burst remains 30." Drawback: "Mana+5; braking
    /// never grants another dash or refund." Counterplay: "Avoid bright endpoint/brake tell."
    /// <list type="bullet">
    /// <item>A recast while the dash is still travelling halts the caster there and bursts once for 24 thermal within
    /// 1.2 m (rank 2: 28) instead of the endpoint burst; the trail covers the shortened path. Each burst is a visible
    /// event. The brake is a separate payload of the cast (no grammar reward, spec L480).</item>
    /// <item>The cast costs 33 mana; braking never refunds or grants another dash. With AUG-313 the cast takes two
    /// recasts: the brake during the dash, then the return.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RushBrake : ISpellTransform
    {
        public const string Id = "AUG-314";

        public static readonly ProseParameter Radius = ProseParameter.Of("brake_radius", 1.2, "emits 1.2 m burst");
        public static readonly ProseParameter Rank1Burst = ProseParameter.Of("rank1_brake_burst", 24, "burst of 24 damage once");
        public static readonly ProseParameter Rank2Burst = ProseParameter.Of("rank2_brake_burst", 28, "Brake burst 28");
        public static readonly ProseParameter ExtraMana = ProseParameter.Of("extra_mana", 5, "Mana+5");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Rank1Burst, Rank2Burst, ExtraMana };

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is RushDelivery rush)) throw new InvalidOperationException(Id + " transforms Phoenix Rush (SPL-013).");
            rush.Brake = true;
            rush.BrakeRadius = Radius.Float;
            rush.BrakeBurst = new PayloadSpec { Channel = form.Channel, Splash = rank >= 2 ? Rank2Burst.Float : Rank1Burst.Float, SplashRadius = Radius.Float, GuardEligible = false };
            form.Mana += ExtraMana.Float;
            form.Recast = new RecastRule((form.Recast?.MaxRecasts ?? 0) + 1);
        }
    }
}
