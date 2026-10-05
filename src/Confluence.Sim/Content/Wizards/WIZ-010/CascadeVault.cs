using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-077 Cascade Vault (WIZ-010, vertical movement): "Launch self toward a visible landing point up to 6 m away and 3
    /// m higher; no flight or invulnerability." World interaction "Water 1 at launch and landing, combined source cap Water
    /// 2." Mana 24, cooldown 13 s, 0.3 s windup and the 6 m range from the record (spec L525).
    /// <list type="bullet">
    /// <item>A leap (framework movement delivery) toward the ground point the aim sees within 6 m horizontally, at most 3 m
    /// above the launch; the landing is fixed at release and the arc is a normal ballistic one, so it can be tracked and
    /// the landing attacked (counterplay). Dash caps apply (8 m, spec L124); the body halts on landing (D-141). It grants
    /// no flight and no invulnerability: hits and statuses land during the arc.</item>
    /// <item>Water 1 at the launch cell when the leap starts and Water 1 at the landing cell; one cast adds at most Water 2
    /// to any cell (both deposits in one cell give exactly 2). A refused leap (no legal landing) leaves no water.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CascadeVault : ISpellBehavior
    {
        public const string Id = "SPL-077";

        public static readonly ProseParameter Rise = ProseParameter.Of("max_rise", 3, "3 m higher");
        public static readonly ProseParameter LaunchWater = ProseParameter.Of("launch_water", 1, "Water 1 at launch and landing");
        public static readonly ProseParameter SourceCap = ProseParameter.Of("source_cap", 2, "combined source cap Water 2");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rise, LaunchWater, SourceCap };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            // "up to 6 m away" is the record's range (spec L525).
            form.Delivery = new MovementDelivery { Mode = DeliveryKind.Leap, Distance = form.Range, MaxRise = Rise.Float };
            form.Payload.AddDeposit(DepositKind.Water, (int)LaunchWater.Value, DepositPlacement.Impact, 0f, HalenRules.ImpactWaterLifetimeSec);
            form.Script = new CascadeVaultScript { LaunchWater = (int)LaunchWater.Value, SourceCap = (int)SourceCap.Value };
        }
    }

    /// <summary>SPL-077's launch water and the AUG-442 midair brake recast.</summary>
    public sealed class CascadeVaultScript : SpellScript
    {
        public int LaunchWater { get; set; } = 1;

        /// <summary>"combined source cap Water 2": no cell receives more than this from one cast.</summary>
        public int SourceCap { get; set; } = 2;

        /// <summary>AUG-442: meters the recast may pull the landing back along the arc; 0 = no brake.</summary>
        public float BrakeMeters { get; set; }

        /// <summary>AUG-441: the landing leaves a puddle field of this radius instead of the landing cell's water; 0 = none.</summary>
        public float LandingPuddleRadius { get; set; }

        public float LandingPuddleLifetimeSec { get; set; }

        public int LandingPuddleWater { get; set; } = 1;

        public override void OnReleased(SpellContext ctx, CastRecord cast)
        {
            // Only a leap that actually launched leaves launch water (a refused leap spawns no movement entity).
            if (cast.LiveEntities <= 0) return;
            int strength = Math.Min(LaunchWater, SourceCap);
            HalenRules.DepositWater(ctx, cast, ctx.Player(cast.Caster).Position, strength, 0f, HalenRules.ImpactWaterLifetimeSec);
            if (LandingPuddleRadius > 0f) ctx.Spawn(new VaultLandingEntity(cast, this));
        }

        /// <summary>
        /// AUG-442 "Recast midair shortens landing point along same arc up to 2 m ... never extends leap": the remaining
        /// horizontal travel shrinks by up to the brake distance over the same remaining airtime, so the vertical arc and
        /// the landing time are unchanged and the landing moves back toward the launch. Grounded or finished leaps ignore
        /// it. No invulnerability or extra jump is granted.
        /// </summary>
        public override void OnRecast(SpellContext ctx, CastRecord cast)
        {
            if (!(BrakeMeters > 0f)) return;
            DuelPlayer p = ctx.Player(cast.Caster);
            if (p.Motor.Grounded || !p.Motor.IsDisplaced || p.Motor.DisplaceKind != DisplacementKind.Dash) return;
            Vec3 v = p.Motor.DisplaceVelocity;
            float speed = v.LengthXZ();
            int ticks = p.Motor.DisplaceTicks;
            float remaining = speed * ticks * Ticks.Dt;
            if (!(remaining > 0f)) return;
            float cut = Math.Min(BrakeMeters, remaining);
            float kept = remaining - cut;
            p.Motor.DisplaceVelocity = v * (kept / remaining);
            Vec3 flat = new Vec3(v.X, 0f, v.Z).NormalizedOr(Vec3.Zero);
            Vec3 landing = HalenRules.Ground(ctx.Collision, p.Position + flat * kept);
            HalenRules.Emit(ctx, TideEventKind.VaultBraked, cast, landing, cut);
        }
    }

    /// <summary>
    /// AUG-441: waits for the cast's leap to land (its movement entity ends, which is when the framework places landing
    /// deposits) and then creates the landing puddle as a gameplay field (<see cref="TidePuddleEntity"/>, spec L510).
    /// Spawned after the leap's movement entity, so it steps after it and sees the landing on the landing tick.
    /// </summary>
    public sealed class VaultLandingEntity : SpellEntity
    {
        private readonly CascadeVaultScript _script;

        internal VaultLandingEntity(CastRecord cast, CascadeVaultScript script)
            : base(cast)
        {
            _script = script;
        }

        protected internal override void Step(SpellContext ctx)
        {
            IReadOnlyList<SpellEntity> entities = ctx.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].Alive && entities[i] is MovementEntity && entities[i].Cast == Cast) return; // still in the air
            }

            int water = Math.Min(_script.LandingPuddleWater, _script.SourceCap);
            TidePuddleEntity.Create(ctx, Cast, ctx.Player(Owner).Position, water, _script.LandingPuddleRadius, _script.LandingPuddleLifetimeSec);
            End(ctx);
        }

        protected override void HashState(ref Confluence.Sim.Hashing.StateHasher h)
        {
        }
    }
}
