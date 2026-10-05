using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Props;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Shared augments of the "Fieldcraft and terrain" family that need no field spell (spec L155 cover, L171 durability,
    // L173 props, L128 fields).

    /// <summary>
    /// AUG-059 Breaker's Dividend (Gold, always). Condition "Destroy enemy-created cover with a direct spell." Rank 1 "Gain 3
    /// stamina, 4 s cooldown.", rank 2 "Gain 5 stamina, 4 s cooldown." Drawback "Direct player damage decreases 2%."
    /// When the owner's spell direct impact (a projectile or ray striking the solid, spec L171) destroys a solid the
    /// opponent created, the owner gains 3 (5) stamina within the maximum (L78), at most once per 4 s and through the proc
    /// budget (L126). The owner's direct spell hits on players deal 2% less (splash, fields and weapons are unchanged).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BreakersDividend : IAugmentBehavior
    {
        public const string Id = "AUG-059";
        public const float Rank1Stamina = 3f;   // "Gain 3 stamina, 4 s cooldown."
        public const float Rank2Stamina = 5f;   // "Gain 5 stamina, 4 s cooldown."
        public const float CooldownSec = 4f;    // "4 s cooldown"
        public const float DirectCut = 0.02f;   // "Direct player damage decreases 2%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _stamina;
            private int _spellSolid = -1;
            private int _spellSolidTick = -1;
            private bool _spellSolidEnemy;
            private int _grantEvent;
            private int _lastGrant = int.MinValue / 2;

            public Hook(float stamina) : base(Id)
            {
                _stamina = stamina;
            }

            // The spell runtime calls this at the moment a direct impact damages the solid, just before the world applies
            // it; the solid (and its creator) still exists here, so the fact is recorded for the destruction event.
            public override float AdjustSolidDamage(int solidId, float amount, string sourceId, CastEventId castId)
            {
                CollisionSolid solid = World.Collision.Find(solidId);
                _spellSolid = solidId;
                _spellSolidTick = Now;
                _spellSolidEnemy = solid != null && solid.OwnerPlayer == 1 - Player;
                return amount;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is SolidEvent s && s.Player == Player && s.Change == SolidChange.Destroyed && s.SolidId == _spellSolid
                    && s.Tick == _spellSolidTick && _spellSolidEnemy && _grantEvent == 0)
                {
                    _grantEvent = s.Id;
                }
            }

            public override void OnTick()
            {
                if (_grantEvent == 0)
                {
                    return;
                }

                int origin = _grantEvent;
                _grantEvent = 0;
                if (InCombat && Now - _lastGrant >= AugmentsAKit.Ticks(CooldownSec) && TryActivate(origin, -1, _stamina))
                {
                    _lastGrant = Now;
                    Host.System.GainResource(Player, EffectResource.Stamina, _stamina, Id);
                }
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.Kind == DamageKind.Direct && hit.IsSpellHit)
                {
                    fraction -= DirectCut;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_spellSolid);
                h.Add(_spellSolidTick);
                h.Add(_spellSolidEnemy);
                h.Add(_grantEvent);
                h.Add(_lastGrant);
            }
        }
    }

    /// <summary>
    /// AUG-062 Practical Ruin (Gold, always). Condition "Destroy a destructible prop within 4 m." Rank 1 "Next field within
    /// 2 s costs 2 less mana, 4 s cooldown.", rank 2 "Next field within 2 s costs 4 less mana, 4 s cooldown." Drawback
    /// "Unspent reduction expires." When the owner destroys a prop (spec L173) whose center is within 4 m (horizontal) of
    /// the owner's feet, the next field spell (a field delivery or a field-tagged form, L128) committed within 2 s costs 2
    /// (4) less inside the 70% floor (L124); the opening is lost after 2 s or when used, and opens at most once per 4 s.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PracticalRuin : IAugmentBehavior
    {
        public const string Id = "AUG-062";
        public const float Range = 4f;          // "within 4 m"
        public const float Rank1Mana = 2f;      // "costs 2 less mana"
        public const float Rank2Mana = 4f;      // "costs 4 less mana"
        public const float WindowSec = 2f;      // "Next field within 2 s"
        public const float CooldownSec = 4f;    // "4 s cooldown"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _mana;
            private int _destroyEvent;
            private int _openUntil = -1;
            private int _lastOpen = int.MinValue / 2;
            private bool _used;

            public Hook(float mana) : base(Id)
            {
                _mana = mana;
            }

            public bool Open => _openUntil >= 0 && Now <= _openUntil;

            public override void OnEvent(DuelEvent e)
            {
                if (e is PropEvent p && p.Player == Player && p.Change == PropChange.Destroyed && _destroyEvent == 0
                    && Vec3.DistanceXZ(Self.Position, p.Position) <= Range)
                {
                    _destroyEvent = p.Id;
                }
                else if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.Committed && Open
                    && FieldForms.IsFieldSpell(AugmentsAKit.FormOf(Self, c.SpellId)))
                {
                    _used = true;
                }
            }

            public override void OnTick()
            {
                if (_used)
                {
                    _used = false;
                    _openUntil = -1; // the reduction was spent
                }

                if (_openUntil >= 0 && Now > _openUntil)
                {
                    _openUntil = -1; // "Unspent reduction expires."
                }

                if (_destroyEvent == 0)
                {
                    return;
                }

                int origin = _destroyEvent;
                _destroyEvent = 0;
                if (InCombat && Now - _lastOpen >= AugmentsAKit.Ticks(CooldownSec) && TryActivate(origin, -1, _mana))
                {
                    _lastOpen = Now;
                    _openUntil = Now + AugmentsAKit.Ticks(WindowSec);
                }
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (Open && FieldForms.IsFieldSpell(AugmentsAKit.FormOf(Self, quote.SpellId)))
                {
                    reduction += _mana;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_destroyEvent);
                h.Add(_openUntil);
                h.Add(_lastOpen);
                h.Add(_used);
            }
        }
    }

    /// <summary>
    /// AUG-067 Surveyed Demolition (Prismatic, always). Condition "Direct spell damages a cover object already damaged by your
    /// weapon within 2 s." Rank 1 "Cover damage increases 15%.", rank 2 "Cover damage increases 25%." Drawback "Player
    /// damage from that spell decreases 3%." A destructible solid (cover, prop bodies, spell walls; spec L171) the owner's
    /// common weapon damaged takes 15% (25%) more durability damage from the owner's spell direct impact within the next
    /// 2 s; that cast's damage to players (its splash, other projectiles, later pulses) is 3% lower.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SurveyedDemolition : IAugmentBehavior
    {
        public const string Id = "AUG-067";
        public const float WindowSec = 2f;      // "within 2 s"
        public const float Rank1Cover = 0.15f;  // "Cover damage increases 15%."
        public const float Rank2Cover = 0.25f;  // "Cover damage increases 25%."
        public const float PlayerCut = 0.03f;   // "Player damage from that spell decreases 3%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Cover, Rank2Cover)));
        }

        private sealed class Hook : EffectHook
        {
            private const int MaxRemembered = 8;
            private readonly float _bonus;
            private readonly List<int> _solids = new List<int>();
            private readonly List<int> _ticks = new List<int>();
            private readonly List<CastEventId> _casts = new List<CastEventId>();
            private int _contactSolid = -1;
            private int _contactTick = -1;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is MeleeEvent m && m.Player == Player && m.Change == MeleeChange.ContactedSolid)
                {
                    _contactSolid = m.SolidId; // the strike's durability damage follows in the same call
                    _contactTick = m.Tick;
                }
                else if (e is SolidEvent s && s.Player == Player && s.Change == SolidChange.Damaged && s.SolidId == _contactSolid && s.Tick == _contactTick)
                {
                    Remember(s.SolidId, s.Tick);
                    _contactSolid = -1;
                }
            }

            private void Remember(int solidId, int tick)
            {
                int i = _solids.IndexOf(solidId);
                if (i >= 0)
                {
                    _solids.RemoveAt(i);
                    _ticks.RemoveAt(i);
                }

                _solids.Add(solidId);
                _ticks.Add(tick);
                if (_solids.Count > MaxRemembered)
                {
                    _solids.RemoveAt(0);
                    _ticks.RemoveAt(0);
                }
            }

            public override float AdjustSolidDamage(int solidId, float amount, string sourceId, CastEventId castId)
            {
                int i = _solids.IndexOf(solidId);
                if (i < 0 || Now - _ticks[i] > AugmentsAKit.Ticks(WindowSec))
                {
                    return amount;
                }

                // Recorded when the impact is applied: the rest of this cast's player damage carries the drawback.
                if (castId.IsValid && !_casts.Contains(castId))
                {
                    _casts.Add(castId);
                    if (_casts.Count > MaxRemembered) _casts.RemoveAt(0);
                }

                return amount * (1f + _bonus);
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.IsSpellHit && hit.CastId.IsValid && _casts.Contains(hit.CastId))
                {
                    fraction -= PlayerCut;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_solids.Count);
                for (int i = 0; i < _solids.Count; i++)
                {
                    h.Add(_solids[i]);
                    h.Add(_ticks[i]);
                }

                h.Add(_casts.Count);
                h.Add(_contactSolid);
                h.Add(_contactTick);
            }
        }
    }

    /// <summary>Field-spell structure tests shared by the Fieldcraft records (spec L128: fields, traps and owned areas).</summary>
    internal static class FieldForms
    {
        /// <summary>A form that creates a field: a field delivery, or a form tagged "field" (trails, deployable traps).</summary>
        public static bool IsFieldSpell(Spells.SpellForm form)
        {
            if (form == null) return false;
            if (form.Delivery is Spells.FieldDelivery) return true;
            return form.Tags != null && Contains(form.Tags, "field");
        }

        private static bool Contains(IEnumerable<string> tags, string tag)
        {
            foreach (string t in tags)
            {
                if (string.Equals(t, tag, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
