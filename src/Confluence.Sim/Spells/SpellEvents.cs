using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Spells
{
    /// <summary>Lifecycle of spell deliveries, for the HUD, replays, and audio/visual cues (spec L96, L419, L549).</summary>
    public enum SpellEventKind
    {
        /// <summary>The anticipatory cue's predicted target or teleport destination (spec L96).</summary>
        Telegraph,

        ProjectileLaunched,
        ProjectileBounced,

        /// <summary>A projectile struck a body or a solid (<see cref="SpellEvent.TargetPlayer"/> or <see cref="SpellEvent.SolidId"/>).</summary>
        ProjectileImpact,

        /// <summary>A projectile reached its maximum range or lifetime without striking anything.</summary>
        ProjectileExpired,

        /// <summary>A hitscan ray or one beam pulse resolved; <see cref="SpellEvent.Amount"/> is its length.</summary>
        RayFired,

        RayReflected,
        ConeReleased,
        ArcReleased,
        FieldCreated,
        FieldEntered,
        FieldExited,
        FieldEnded,
        ShieldRaised,
        BuffStarted,
        BuffEnded,

        /// <summary>A dash, leap, or teleport began (dash and leap) or completed (teleport).</summary>
        MovementStarted,

        MovementEnded,

        /// <summary>A movement spell found no legal route or destination; the cast stays spent (spec L90).</summary>
        MovementRefused,

        DeployablePlaced,
        DeployableArmed,

        /// <summary>A trap's visible warning started; <see cref="SpellEvent.Ticks"/> until it bursts.</summary>
        DeployableWarning,

        DeployableDetonated,
        DeployableDestroyed,
        DeployableExpired,
        SummonCreated,
        SummonAttack,
        SummonEnded,
        WaveLaunched,
        WaveSegmentBlocked,
        WaveEnded,
        TetherLaunched,
        TetherAttached,
        TetherEnded,

        // ----- appended by the WIZ-002 kit (values above are unchanged) ---------------------------------------

        /// <summary>An area burst resolved at <see cref="SpellEvent.Position"/>; <see cref="SpellEvent.Amount"/> is its radius.</summary>
        AreaBurst,

        /// <summary>A visible counterplay marker (casting marker, bounce mark, marked arc) at a position and direction.</summary>
        Marker,

        /// <summary>A prop or construct became hot; <see cref="SpellEvent.Amount"/> is its contact damage per second.</summary>
        Heated,

        /// <summary>A hot prop or construct was cooled or lost its anchor before its lifetime ended.</summary>
        Cooled,

        /// <summary>A spell's own heat met water and became steam (premature steaming, steam strips, puffs, jets).</summary>
        Steamed,

        /// <summary>A spell moved an existing cloud (manual pushes and pulls); <see cref="SpellEvent.Amount"/> is the distance.</summary>
        CloudMoved,
    }

    /// <summary>One spell delivery event in the world's ordered event stream.</summary>
    public sealed class SpellEvent : DuelEvent
    {
        public SpellEventKind Kind { get; internal set; }
        public string SpellId { get; internal set; }
        public CastEventId CastId { get; internal set; }

        /// <summary>Runtime id of the delivery entity (projectile, field, deployable, summon, wave, tether), or 0.</summary>
        public int EntityId { get; internal set; }

        public Vec3 Position { get; internal set; }
        public Vec3 Direction { get; internal set; }

        /// <summary>Kind-specific magnitude (ray length, shield capacity, distance moved).</summary>
        public float Amount { get; internal set; }

        public int TargetPlayer { get; internal set; } = -1;
        public int SolidId { get; internal set; } = -1;

        /// <summary>Kind-specific duration in ticks (warning length, field lifetime).</summary>
        public int Ticks { get; internal set; }

        public override DuelEventType Type => DuelEventType.Spell;

        public override string ToString() => base.ToString() + " " + Kind + " " + SpellId;
    }
}
