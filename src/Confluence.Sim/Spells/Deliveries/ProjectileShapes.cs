using Confluence.Core.Geometry;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// The numbers every projectile delivery shares (spec L525-L527: speed, footprint radius, range, gravity, count),
    /// whether it is the framework <see cref="ProjectileDelivery"/> or a kit's own projectile delivery (WIZ-002's
    /// HeatProjectileDelivery for the Fireball transformations, WIZ-010's TideProjectileDelivery for Pressure Jet and
    /// Deepglass Prison). Shared augments and effects judge and edit projectiles through this interface, so a kit
    /// projectile receives the same projectile-family records the Economy offers for its "projectile" tag (spec L243,
    /// L543: general augments edit the resolved structure). Setters are guarded by the delivery's freeze like any
    /// <see cref="DeliverySpec"/> property.
    /// </summary>
    public interface IProjectileDelivery
    {
        /// <summary>Launch speed (m/s) before mastery and release-time hooks.</summary>
        float Speed { get; set; }

        /// <summary>Footprint radius (m).</summary>
        float Radius { get; set; }

        /// <summary>Maximum travel (m, spec L525).</summary>
        float MaxRange { get; set; }

        /// <summary>Downward acceleration (m/s²); 0 for straight flight.</summary>
        float Gravity { get; }

        /// <summary>Projectiles released per cast.</summary>
        int Count { get; }
    }

    /// <summary>A projectile in flight from any <see cref="IProjectileDelivery"/> (entity-level shared effects, snapshots).</summary>
    public interface IProjectileBody
    {
        int Id { get; }
        int Owner { get; }
        bool Alive { get; }
        Vec3 Position { get; }
        Vec3 Velocity { get; }

        /// <summary>The flying body's radius (its delivery's footprint radius).</summary>
        float Radius { get; }
    }

    /// <summary>In-flight speed changes by content (for example AUG-030 Ground Skimmer); rules code only.</summary>
    internal interface IProjectileSpeedScale
    {
        /// <summary>Multiplies the current flight speed (never below zero).</summary>
        void ScaleSpeed(float factor);
    }
}
