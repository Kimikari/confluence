using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Casting
{
    /// <summary>
    /// A cast phase change (preparation, cue, commitment, release, channel, cancel, interrupt, recast, rejection,
    /// refund) in the world's ordered event stream, for the HUD, replays, and the results decisive sequence (spec
    /// L90-L96, L359, L419). The commitment event's id is the cast's original event id for proc budgets (spec L126).
    /// </summary>
    public sealed class CastDuelEvent : DuelEvent
    {
        public CastEvent Cast { get; internal set; }

        public CastEventKind Kind => Cast.Kind;
        public CastEventId CastId => Cast.CastId;
        public string SpellId => Cast.SpellId;

        /// <summary>Caster eye and view direction on the event's tick (visible hand pose and cue direction, spec L96).</summary>
        public Vec3 AimOrigin { get; internal set; }

        public Vec3 AimDirection { get; internal set; }

        public override DuelEventType Type => DuelEventType.Cast;

        public override string ToString() => base.ToString() + " " + Cast.Kind + " " + Cast.SpellId;
    }
}
