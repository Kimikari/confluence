using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Net.Game.Wire;
using Confluence.Reactions.Events;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Props;
using Confluence.Sim.Spells;
using Confluence.Sim.World.Materials;

namespace Confluence.Net.Game.Server
{
    /// <summary>
    /// Converts authoritative duel events into wire events for one audience (interest management, spec L72, L391,
    /// L425, L472). The acting player receives everything about themself. The opponent and delayed spectators receive the
    /// public tells only: cast lifecycle without the open preparation's spell, refusals, refunds, or mana; heals, shields
    /// and guard rewards without amounts; no resource bookkeeping except a visible consumable use; damage events in full
    /// (they are authoritative confirmations of real hits, L349).
    /// </summary>
    public static class DuelEventFilter
    {
        /// <summary>Spectators see both players' public tells.</summary>
        public const int SpectatorView = -1;

        /// <summary>Converts one world step's events (and its reaction outcomes) for <paramref name="viewerSide"/>.</summary>
        public static void Convert(DuelWorld world, int viewerSide, List<WireDuelEvent> into)
        {
            IReadOnlyList<DuelEvent> events = world.TickEvents;
            for (int i = 0; i < events.Count; i++)
            {
                WireDuelEvent w = Convert(events[i], viewerSide);
                if (w != null) into.Add(w);
            }

            StepResult step = (world.Materials as ReactionMaterials)?.LastStep;
            if (step != null && step.Tick == world.Tick)
            {
                IReadOnlyList<ReactionEvent> reactions = step.Events;
                for (int i = 0; i < reactions.Count; i++)
                {
                    ReactionEvent r = reactions[i];
                    into.Add(new WireDuelEvent
                    {
                        Id = 0,
                        Tick = r.Tick,
                        Type = WireEventType.Reaction,
                        Kind = (byte)r.Kind,
                        Player = r.Owner == 0 || r.Owner == 1 ? r.Owner : -1,
                        PositionSet = true,
                        Position = r.Position,
                        EntityId = r.TargetId,
                        Extra = r.Amount,
                    });
                }
            }
        }

        /// <summary>One event for one viewer, or null when the viewer may not know it.</summary>
        public static WireDuelEvent Convert(DuelEvent e, int viewerSide)
        {
            bool own = viewerSide >= 0 && e.Player == viewerSide;
            var w = new WireDuelEvent { Id = e.Id, Tick = e.Tick, Type = (WireEventType)(byte)e.Type, Player = e.Player };
            switch (e)
            {
                case PhaseEvent phase:
                    w.Kind = (byte)phase.Phase;
                    return w;
                case DamageEvent damage:
                    w.Player = damage.Attacker;
                    w.Target = damage.Target;
                    w.Kind = (byte)damage.Kind;
                    w.SourceId = damage.SourceId;
                    w.Amount = damage.Resolution != null ? damage.Resolution.HealthLost : 0f;
                    w.Amount2 = damage.Resolution != null ? damage.Resolution.ShieldAbsorbed : 0f;
                    w.Extra = (damage.Resolution != null ? (int)damage.Resolution.Guard : 0) | (damage.Lethal ? 1 << 8 : 0) | ((int)damage.Reaction << 9);
                    w.EntityId = damage.CastId.Sequence;
                    w.PositionSet = true;
                    w.Position = damage.TargetPosition;
                    return w;
                case HealEvent heal:
                    w.SourceId = heal.SourceId;
                    if (own || viewerSide == SpectatorView)
                    {
                        w.Amount = heal.Restored;
                        w.Amount2 = heal.BudgetRemaining;
                    }

                    w.Kind = (byte)(heal.Lifesteal ? 1 : 0);
                    return w;
                case ShieldEvent shield:
                    w.Kind = (byte)shield.Change;
                    w.SourceId = shield.SourceId;
                    if (own)
                    {
                        w.Amount = shield.Amount;
                        w.Amount2 = shield.TotalAfter;
                    }

                    return w;
                case StatusEvent status:
                    w.Kind = (byte)status.Change;
                    w.EntityId = (int)status.Kind;
                    w.SourceId = status.DefinitionId;
                    w.Target = status.SourcePlayer;
                    w.Extra = status.ExpireTick;
                    w.Amount = status.Intensity;
                    return w;
                case GuardEvent guard:
                    w.Kind = (byte)guard.Change;
                    w.EntityId = guard.BlockedEventId;
                    if (own) w.Amount = guard.ManaGained;
                    return w;
                case MeleeEvent melee:
                    w.Kind = (byte)melee.Change;
                    w.SourceId = melee.SourceId;
                    w.Target = melee.TargetPlayer;
                    w.EntityId = melee.SolidId >= 0 ? melee.SolidId : 0;
                    w.Extra = melee.AttackSerial;
                    w.Text = melee.ProfileName;
                    return w;
                case MovementEvent move:
                    w.Kind = (byte)move.Change;
                    w.PositionSet = true;
                    w.Position = move.Position;
                    w.Amount = move.Amount;
                    w.SourceId = move.SourceId;
                    return w;
                case FieldEvent field:
                    w.Kind = (byte)field.Change;
                    w.EntityId = field.EntityId;
                    w.SourceId = field.DefinitionId;
                    w.Extra = (int)field.EntityKind;
                    w.Target = field.Destroyer;
                    return w;
                case SolidEvent solid:
                    w.Kind = (byte)solid.Change;
                    w.EntityId = solid.SolidId;
                    w.Amount = solid.Amount;
                    w.Amount2 = solid.DurabilityAfter;
                    return w;
                case BoundaryEvent boundary:
                    w.Kind = (byte)boundary.Change;
                    return w;
                case ResourceEvent resource:
                    // Mana bookkeeping is private; a consumable's use is a visible action.
                    if (!own && resource.Change != ResourceChange.ConsumableUsed) return null;
                    w.Kind = (byte)resource.Change;
                    w.SourceId = resource.SourceId;
                    if (own) w.Amount = resource.Amount;
                    return w;
                case DuelEndEvent end:
                    w.Kind = (byte)end.Outcome;
                    w.Target = end.Winner;
                    w.Extra = (int)end.Reason;
                    w.Amount = end.HealthFraction0;
                    w.Amount2 = end.HealthFraction1;
                    return w;
                case CastDuelEvent cast:
                    return ConvertCast(cast, w, own);
                case SpellEvent spell:
                    w.Kind = (byte)spell.Kind;
                    w.SourceId = spell.SpellId;
                    w.EntityId = spell.EntityId;
                    w.PositionSet = true;
                    w.Position = spell.Position;
                    w.DirectionSet = true;
                    w.Direction = spell.Direction;
                    w.Amount = spell.Amount;
                    w.Target = spell.TargetPlayer;
                    w.Extra = spell.Ticks;
                    return w;
                case PropEvent prop:
                    w.Kind = (byte)prop.Change;
                    w.EntityId = prop.PropId;
                    w.SourceId = prop.DefinitionId;
                    w.PositionSet = true;
                    w.Position = prop.Position;
                    w.Amount = prop.Amount;
                    w.Amount2 = prop.DurabilityAfter;
                    w.Target = prop.TargetPlayer;
                    return w;
                case EffectEvent effect:
                    w.Kind = (byte)effect.Change;
                    w.SourceId = effect.SourceId;
                    w.Text = effect.Cue;
                    if (own) w.Amount = effect.Amount;
                    return w;
                default:
                    return own ? w : null;
            }
        }

        private static WireDuelEvent ConvertCast(CastDuelEvent cast, WireDuelEvent w, bool own)
        {
            CastEventKind kind = cast.Kind;
            if (!own)
            {
                switch (kind)
                {
                    case CastEventKind.Rejected:
                    case CastEventKind.Refunded:
                    case CastEventKind.RecastWindowOpened:
                    case CastEventKind.RecastWindowClosed:
                        return null; // private bookkeeping of the caster's inputs and mana
                }
            }

            w.Kind = (byte)kind;
            // The open preparation's spell stays private until commitment (spec L472); the hand pose is the public tell.
            bool spellPublic = own || (kind != CastEventKind.PreparationStarted && kind != CastEventKind.PreparationCancelled);
            w.SourceId = spellPublic ? cast.SpellId : null;
            w.EntityId = cast.CastId.Sequence;
            w.PositionSet = true;
            w.Position = cast.AimOrigin;
            w.DirectionSet = true;
            w.Direction = cast.AimDirection;
            w.Extra = cast.Cast.Ticks;
            if (own)
            {
                w.Amount = cast.Cast.Mana;
                w.Target = cast.Cast.Slot;
                w.Text = kind == CastEventKind.Rejected ? cast.Cast.Reject.ToString() : null;
                w.Amount2 = kind == CastEventKind.Committed && cast.Cast.Mastery.IsMastered ? 1f : 0f;
            }

            return w;
        }
    }
}
