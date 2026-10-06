using System;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Props
{
    /// <summary>How a direct hit reached a prop.</summary>
    public enum PropHitSource
    {
        /// <summary>A delivery's direct impact on the prop's body (projectiles, rays: <see cref="WorldImpact"/>).</summary>
        Spell,

        /// <summary>A melee strike that contacted the prop's body.</summary>
        Melee,

        /// <summary>A delivery's ground impact on a floor-level prop's footprint (grates, tiles, beds have no body).</summary>
        Ground,
    }

    /// <summary>A direct hit on one prop (spec L147 "an impact of at least 20 direct damage", L171 durability).</summary>
    public struct PropHit
    {
        public PropHitSource Source;

        /// <summary>The striking player, or -1.</summary>
        public int Attacker;

        /// <summary>The impact's direct damage before any mitigation (the reaction threshold value, L147).</summary>
        public float DirectDamage;

        public Vec3 Point;

        /// <summary>Travel direction of the impact (a shard cone opens along it).</summary>
        public Vec3 Direction;

        public int OriginEventId;
        public string SourceId;
    }

    /// <summary>
    /// Material or force from one world deposit that touches a prop (spec L163 "Reaction strength values are part of
    /// the spell's world-interaction field"): the strongest per-cell strength among the deposit's cells within
    /// <see cref="PropInteractionRules.ContactReach"/> of the prop's contact footprint (a heavy projectile's impact cell
    /// carries its full strength, its ring the adjacent strength).
    /// </summary>
    public struct PropMaterialContact
    {
        public DepositKind Kind;
        public int Strength;

        /// <summary>Owning player of the deposit (L670 "the last direct source that commits the reaction"), or -1.</summary>
        public int Owner;

        public int OriginEventId;
        public string SourceId;

        /// <summary>Center of the touching cell with the strongest units, at the deposit's surface height.</summary>
        public Vec3 Point;

        /// <summary>Ground-plane direction of the deposit (force, wind), or zero.</summary>
        public Vec3 Direction;

        /// <summary>The deposit as queued.</summary>
        public WorldDeposit Deposit;
    }

    /// <summary>Numbers of the prop interaction substrate (each interprets the cited line; see docs/notes/props-biomes.md).</summary>
    public static class PropInteractionRules
    {
        /// <summary>
        /// A deposit cell touches a prop when its center lies within one cell (0.5 m) of the prop's footprint (spec L136
        /// "0.5 meter interaction cells"): a hit on a prop's face deposits in the cell at or beside the face.
        /// </summary>
        public const float ContactReach = GlobalRules.World.CellSize;

        /// <summary>A deposit or ground impact more than this far above or below the prop's box does not touch it.</summary>
        public const float VerticalReach = GlobalRules.Movement.StepHeight + GlobalRules.World.CellSize;

        /// <summary>Spec L147 "An impact of at least 20 direct damage" (the shard and release threshold of props).</summary>
        public const float ImpactThreshold = GlobalRules.World.ShatterMinDirectDamage;

        /// <summary>Spec L153 "At least two heat units ignite" (Heat 2 in the prop records).</summary>
        public const int IgniteHeat = GlobalRules.World.GrowthIgniteMinHeat;
    }

    /// <summary>A prop's own interaction outcome, perceptible to both players (spec L682 visual standards, L447).</summary>
    public enum PropInteraction
    {
        Absorbed,
        Released,
        Melted,
        Shattered,
        Ignited,
        Extinguished,
        BurnedOut,
        LayerRemoved,
        Collected,
        Drained,
        DustRaised,
        Flooded,
        Insulating,
        Suppressed,
    }

    /// <summary>
    /// A prop interaction event in the duel's ordered stream (spec L433 PropState). Public: both players perceive props
    /// (L682 "Water edges show cell connectivity... Combustible growth visibly exhausts its fuel").
    /// </summary>
    public sealed class PropInteractionEvent : DuelEvent, IPublicDuelCue
    {
        public PropInteraction Kind { get; internal set; }

        /// <summary>Placement id in the arena layout.</summary>
        public int PropId { get; internal set; }

        /// <summary>Catalogue id (PROP-xxx).</summary>
        public string DefinitionId { get; internal set; }

        public Vec3 Position { get; internal set; }

        /// <summary>Kind-specific amount (units released or stored, damage per second, layers left).</summary>
        public float Amount { get; internal set; }

        public int TargetPlayer { get; internal set; } = -1;

        public override DuelEventType Type => DuelEventType.Prop;

        string IPublicDuelCue.CueSourceId => DefinitionId;

        int IPublicDuelCue.CueKind => (int)Kind;

        Vec3 IPublicDuelCue.CuePosition => Position;

        float IPublicDuelCue.CueAmount => Amount;

        int IPublicDuelCue.CueTarget => TargetPlayer;

        public override string ToString() => base.ToString() + " " + Kind + " " + DefinitionId + "#" + PropId;
    }

    /// <summary>
    /// Per-duel state of one placed prop's catalogue behavior (IPropBehavior creates one per placement and attaches it
    /// with <see cref="PropSystem.Attach"/>). The prop system calls it, in layout order, when material or a hit touches
    /// the prop, when its durability changes, when it collides or stops while moving, after each reaction step, and once
    /// per tick at step 6 during combat. Everything it queues goes through the duel's ordinary seams (world deposits,
    /// reaction requests, prop durability), so caps, ownership and the canonical reaction order apply unchanged.
    /// </summary>
    public abstract class PropController
    {
        internal bool Busy;

        public PropBody Body { get; internal set; }

        public DuelWorld World { get; internal set; }

        /// <summary>The catalogue record (mass, durability, effect text).</summary>
        public PropDef Definition { get; internal set; }

        public int PropId => Body.Id;

        public string DefinitionId => Body.DefinitionId;

        /// <summary>The duel's reaction materials, or null in a duel without them.</summary>
        public ReactionMaterials Materials => World?.Materials as ReactionMaterials;

        public ReactionWorld Reactions => Materials?.World;

        /// <summary>
        /// Footprint used for material contact and ground hits: the body box, or the record's stated area for a floor-level
        /// prop without a body (override it: grates, tiles, and beds state their size).
        /// </summary>
        public virtual YawBox ContactBox => Body.Box;

        /// <summary>Whether material and hits still reach the prop (default: until it is destroyed).</summary>
        public virtual bool Responsive => !Body.Destroyed;

        /// <summary>Called once when attached (after the duel's materials and biome rule are installed).</summary>
        public virtual void OnAttached()
        {
        }

        public virtual void OnHit(in PropHit hit)
        {
        }

        public virtual void OnMaterial(in PropMaterialContact contact)
        {
        }

        /// <summary>Durability damage was applied (spells, melee, burns); <paramref name="destroyed"/> when it broke now.</summary>
        public virtual void OnDamaged(float amount, int attacker, bool destroyed)
        {
        }

        /// <summary>The moving prop struck a player (<paramref name="damage"/> 0 below the impact threshold or inside the 1 s window).</summary>
        public virtual void OnCollided(int target, float damage, float speed)
        {
        }

        /// <summary>The moving prop was stopped by a solid at <paramref name="speed"/>.</summary>
        public virtual void OnStoppedBySolid(float speed)
        {
        }

        /// <summary>The reaction world finished its step (events such as refill pulses are in the result).</summary>
        public virtual void OnReactionStep(StepResult result)
        {
        }

        /// <summary>Tick-order step 6, during combat, after prop motion.</summary>
        public virtual void Step()
        {
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(PropId);
            HashState(ref h);
        }

        protected virtual void HashState(ref StateHasher h)
        {
        }

        // ----- helpers ---------------------------------------------------------------------------------------

        /// <summary>Records a public interaction event.</summary>
        protected PropInteractionEvent Emit(PropInteraction kind, int player, float amount, Vec3? at = null, int target = -1)
        {
            return World.RecordEvent(new PropInteractionEvent
            {
                Kind = kind,
                PropId = PropId,
                DefinitionId = DefinitionId,
                Position = at ?? Body.Position,
                Amount = amount,
                TargetPlayer = target,
            }, player >= 0 && player < DuelWorld.PlayerCount ? player : -1);
        }

        /// <summary>
        /// Removes the prop through its own durability (it burned through, melted, or shattered): collision and sight change
        /// in the same update (spec L171). The attacker is credited as for any destroyed cover. Returns true when it broke.
        /// </summary>
        protected bool Consume(int attacker)
        {
            return World.Props.Retire(Body, attacker);
        }

        /// <summary>Durability damage to this prop (a floor prop struck on its footprint, a burn). Returns true when it broke.</summary>
        protected bool TakeDamage(float amount, int attacker)
        {
            return World.Props.Damage(PropId, amount, attacker);
        }

        /// <summary>Queues a world deposit owned by <paramref name="owner"/> (or the map), attributed to this prop.</summary>
        protected void Deposit(DepositKind kind, int strength, Footprint footprint, float surfaceY, int lifetimeTicks, int owner, int originEventId)
        {
            World.QueueDeposit(new WorldDeposit
            {
                Kind = kind,
                Strength = strength,
                Position = new Vec3(footprint.Anchor.X, surfaceY, footprint.Anchor.Y),
                Footprint = footprint,
                HasFootprint = true,
                LifetimeTicks = lifetimeTicks,
                SourcePlayer = owner,
                SourceId = DefinitionId,
                OriginEventId = originEventId,
            });
        }

        /// <summary>The floor point just outside the prop's footprint on the side facing <paramref name="toward"/>.</summary>
        protected Vec3 OutletToward(Vec3 toward, float clearance)
        {
            YawBox box = ContactBox;
            Vec3 local = box.DirectionToLocal(new Vec3(toward.X - box.Center.X, 0f, toward.Z - box.Center.Z));
            Vec3 axis;
            float extent;
            if (Math.Abs(local.X) * box.HalfExtents.Z >= Math.Abs(local.Z) * box.HalfExtents.X)
            {
                axis = local.X >= 0f ? box.Right : -box.Right;
                extent = box.HalfExtents.X;
            }
            else
            {
                axis = local.Z >= 0f ? box.Forward : -box.Forward;
                extent = box.HalfExtents.Z;
            }

            if (local.X == 0f && local.Z == 0f)
            {
                axis = box.Forward;
                extent = box.HalfExtents.Z;
            }

            return new Vec3(box.Center.X, box.Bottom, box.Center.Z) + axis * (extent + clearance);
        }
    }
}
