using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Grid;
using Confluence.Sim.Combat;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.World
{
    /// <summary>Material or force a delivery deposits into the world (spec L136, L163).</summary>
    public enum DepositKind
    {
        Water,
        Heat,
        Frost,
        Dust,
        Sand,
        Growth,
        Electricity,
        Wind,
        Force,
        Venom,
        Light,
        Shadow,
        Sound,
        Gravity,
        Arcane,
    }

    /// <summary>
    /// A queued world deposit from tick-order step 4 (spec L163 default deposits: quick projectile one unit, field
    /// one unit per covered cell, heavy projectile two at impact and one within 1 m). Consumed by the reactions
    /// stage at step 5.
    /// </summary>
    public struct WorldDeposit
    {
        public DepositKind Kind;
        public int Strength;
        public Vec3 Position;

        /// <summary>Footprint radius in meters (0 = the impact cell only).</summary>
        public float Radius;

        /// <summary>Unit ground-plane direction for wind and force (X, Y = world Z).</summary>
        public Vec2 Direction;

        /// <summary>Player whose committed source owns the resulting bookkeeping allocation (spec L670), or -1.</summary>
        public int SourcePlayer;

        public string SourceId;

        /// <summary>Original cast or hit event for attribution and proc budgets.</summary>
        public int OriginEventId;

        // ----- spell runtime additions: declared footprints (spec L136, L163) --------------------------------

        /// <summary>
        /// The spell's declared ground footprint (cone, lane, segment, circle, point) when <see cref="HasFootprint"/>;
        /// otherwise the footprint is the circle of <see cref="Radius"/> around <see cref="Position"/> (0 = its cell).
        /// </summary>
        public Footprint Footprint;

        public bool HasFootprint;

        /// <summary>Ticks the deposited units persist; 0 = instant residue that reacts this step (spec L163).</summary>
        public int LifetimeTicks;

        /// <summary>Owned field (OwnedEntityRegistry id) the material belongs to, so a dispel can consume it (spec L161); 0 none.</summary>
        public int FieldId;
    }

    /// <summary>Kind of ground surface under a character (spec L175).</summary>
    public enum SurfaceKind
    {
        Normal,
        /// <summary>Slippery ice: reduced braking and more lateral drift, never control loss.</summary>
        Ice,
        /// <summary>Wet stone: reaction eligibility and footstep sound only, no movement penalty.</summary>
        Wet,
        /// <summary>Damp sand: stabilized footing (spec L154).</summary>
        DampSand,
        /// <summary>Sticky growth: slows within the global 30 percent limit.</summary>
        Sticky,
    }

    /// <summary>Surface answer for the motor: traction and any surface slow (spec L175).</summary>
    public struct SurfaceInfo
    {
        public SurfaceKind Kind;

        /// <summary>Slow fraction from the surface (sticky growth); joins the strongest-slow rule (spec L116).</summary>
        public float SlowFraction;

        public static SurfaceInfo Normal => new SurfaceInfo { Kind = SurfaceKind.Normal };

        public bool Slippery => Kind == SurfaceKind.Ice;
    }

    /// <summary>Reaction contact damage requested by the materials step, applied by the world in request order.</summary>
    public struct ContactDamageRequest
    {
        public int Target;

        /// <summary>Player whose committed source owns the reaction (attribution only; no proc benefits, spec L672).</summary>
        public int SourcePlayer;

        public string SourceId;
        public DamageChannel Channel;
        public float Amount;
        public int OriginEventId;

        /// <summary>The reaction family, for feats such as FEAT-020 (conductivity).</summary>
        public ReactionTag Reaction;
    }

    /// <summary>What the materials step may read and request during tick-order step 5.</summary>
    public interface IMaterialsContext
    {
        int Tick { get; }
        int PlayerCount { get; }
        Vec3 PlayerFeet(int player);
        bool PlayerGrounded(int player);
        CollisionWorld Collision { get; }

        void RequestContactDamage(ContactDamageRequest request);

        /// <summary>Records a distinct world reaction produced by a player's source (FEAT-006).</summary>
        void RecordReaction(int player, string reactionId);

        /// <summary>Records a hostile or neutral cloud cleared by a player (FEAT-011).</summary>
        void RecordCloudCleared(int player);
    }

    /// <summary>
    /// Seam to the reactions stage (tick-order step 5, spec L136-L167). The Sim queues deposits during step 4,
    /// calls <see cref="Step"/> once per tick, and asks for obscuration path length and ground surfaces. The
    /// default <see cref="NullWorldMaterials"/> has no materials.
    /// </summary>
    public interface IWorldMaterials
    {
        void QueueDeposit(in WorldDeposit deposit);

        /// <summary>Processes reactions in the fixed order (spec L165) and requests contact damage.</summary>
        void Step(IMaterialsContext context);

        /// <summary>Cumulative path length through obscuring volumes between two points (spec L167).</summary>
        float ObscuringPathLength(Vec3 from, Vec3 to);

        /// <summary>Ground surface at a character's feet (ice cells, wet stone, sticky growth).</summary>
        SurfaceInfo QuerySurface(Vec3 feet);

        void Hash(ref StateHasher hasher);
    }

    /// <summary>No world materials: deposits are dropped, nothing obscures, every surface is normal.</summary>
    public sealed class NullWorldMaterials : IWorldMaterials
    {
        public void QueueDeposit(in WorldDeposit deposit)
        {
        }

        public void Step(IMaterialsContext context)
        {
        }

        public float ObscuringPathLength(Vec3 from, Vec3 to) => 0f;

        public SurfaceInfo QuerySurface(Vec3 feet) => SurfaceInfo.Normal;

        public void Hash(ref StateHasher hasher)
        {
        }
    }
}
