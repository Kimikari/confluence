using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Shared augments of the "Casting mastery" family (spec L86 normal cast, L468-L482 mastery normalization). An
    // "advanced variation" is a cast that earned its wizard grammar's mastery reward (MasteryResult.IsMastered); a
    // "normal" cast is a paid exclusive spell whose grammar result is normal. Mastery-linked bonuses clamp with the
    // cast's own reward in its category at 15% (attributes) or 20% (efficiency) (L124, L480).

    /// <summary>Cast facts the mastery records read: commitments with their mastery, payments, releases, ends.</summary>
    internal static class CastFacts
    {
        /// <summary>A commitment of one of the player's equipped spells (slot 0-5; signatures are not spells, L472).</summary>
        public static bool IsSpellCommit(DuelEvent e, int player, out CastDuelEvent commit)
        {
            commit = e as CastDuelEvent;
            return commit != null && commit.Player == player && commit.Kind == CastEventKind.Committed && commit.Cast.Slot >= 0;
        }

        /// <summary>A failed optional variation: a normal result from the wizard grammar (not a guided or grammar-less cast).</summary>
        public static bool IsFailedVariation(in MasteryResult m) =>
            !m.IsMastered && m.Miss != MissReason.Guided && m.Miss != MissReason.NotApplicable && m.Miss != MissReason.None;

        /// <summary>The form is a direct projectile: a projectile delivery whose primary payload has direct damage.</summary>
        public static bool IsDirectProjectile(SpellForm form) =>
            form != null && form.Delivery is ProjectileDelivery && form.Payload != null && form.Payload.Direct > 0f;

        // Mastery-linked damage and projectile-speed bonuses are reported through EffectHook.LinkedDirectDamage and
        // EffectHook.LinkedProjectileSpeed; the effect host sums them over all records and clamps the total once with the
        // cast's own reward at 15% (Effects/HookAggregates.cs MasteryLinked, spec L480).
    }

    /// <summary>Records which resource event paid each of the player's commitments (refunds name the payment, L90).</summary>
    internal sealed class PaymentLog
    {
        private readonly Dictionary<CastEventId, int> _byCast = new Dictionary<CastEventId, int>();
        private readonly Queue<CastEventId> _order = new Queue<CastEventId>();
        private int _lastPayment;

        public void Observe(DuelEvent e, int player)
        {
            if (e is ResourceEvent r && r.Player == player && r.Change == ResourceChange.ManaSpent && AugmentsAKit.IsSpellSource(r.SourceId)) // a commitment payment, not a drawback charge
            {
                _lastPayment = r.Id;
            }
            else if (e is CastDuelEvent c && c.Player == player && c.Kind == CastEventKind.Committed && _lastPayment != 0)
            {
                _byCast[c.CastId] = _lastPayment;
                _order.Enqueue(c.CastId);
                _lastPayment = 0;
                while (_order.Count > 16) _byCast.Remove(_order.Dequeue());
            }
        }

        public int PaymentOf(CastEventId cast) => _byCast.TryGetValue(cast, out int id) ? id : 0;

        public void Hash(ref StateHasher h)
        {
            h.Add(_lastPayment);
            h.Add(_order.Count);
            foreach (CastEventId id in _order) // the queue's order is the commit order
            {
                h.Add((ulong)id.Packed);
                h.Add(PaymentOf(id));
            }
        }
    }

    /// <summary>
    /// AUG-012 Perfect Landing (Gold, always). Condition "Release a normal paid spell 0.1-0.25 s after landing." Rank 1
    /// "Cost decreases 2 mana.", rank 2 "Cost decreases 4 mana." Drawback "Landing outside the window grants nothing."
    /// The condition is judged at the cast's release (spec L90-L92 distinguish commitment, when the cost is paid, from
    /// release): an equipped spell with a normal (unrewarded) grammar result whose release falls 0.1-0.25 s after the
    /// owner's last landing has its cost decreased by 2 (4) mana after the fact, returned through the cast's refund ledger
    /// (25% of the payment, once, L124). One landing qualifies one release. Mastered casts, unpaid casts, and releases
    /// outside the window get nothing.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PerfectLanding : IAugmentBehavior
    {
        public const string Id = "AUG-012";
        public const float Rank1Mana = 2f;          // "Cost decreases 2 mana."
        public const float Rank2Mana = 4f;          // "Cost decreases 4 mana."
        public const float WindowStartSec = 0.1f;   // "0.1-0.25 s after landing"
        public const float WindowEndSec = 0.25f;

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana)));
        }

        /// <summary>Whether a release <paramref name="sinceLanding"/> ticks after a landing is inside the window.</summary>
        public static bool InWindow(int sinceLanding) =>
            sinceLanding >= AugmentsAKit.Ticks(WindowStartSec) && sinceLanding <= AugmentsAKit.Ticks(WindowEndSec);

        private sealed class Hook : EffectHook
        {
            private readonly float _mana;
            private readonly PaymentLog _payments = new PaymentLog();
            private int _landTick = int.MinValue / 2;
            private CastDuelEvent _release;

            public Hook(float mana) : base(Id)
            {
                _mana = mana;
            }

            public override void OnEvent(DuelEvent e)
            {
                _payments.Observe(e, Player);
                if (e is MovementEvent m && m.Player == Player && m.Change == MovementChange.Landed)
                {
                    _landTick = m.Tick;
                }
                else if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.Released && c.Cast.Slot >= 0
                    && !c.Cast.Mastery.IsMastered && InWindow(c.Tick - _landTick))
                {
                    _release = c;
                    _landTick = int.MinValue / 2; // one landing qualifies one release
                }
            }

            public override void OnTick()
            {
                if (_release == null)
                {
                    return;
                }

                CastDuelEvent c = _release;
                _release = null;
                int payment = _payments.PaymentOf(c.CastId);
                if (InCombat && payment != 0 && TryActivate(c.Id, -1, _mana))
                {
                    World.Effects.RefundCast(Player, payment, _mana, Key); // the cast's 25% refund ledger (L90, L124)
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_landTick);
                h.Add(_release != null ? _release.Id : 0);
                _payments.Hash(ref h);
            }
        }
    }

    /// <summary>
    /// AUG-019 Pressure Examination (Prismatic, always). Condition "Complete a paid advanced variation below 90 health."
    /// Rank 1 "Gain 7 shield for 1 s, 5 s cooldown.", rank 2 "Gain 12 shield for 1 s, 5 s cooldown." Drawback "No bonus
    /// for normal casts; shield follows 100 cap." Committing a mastered spell while below 90 health grants a 1 s shield
    /// inside the shared 100 cap (spec L114).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PressureExamination : IAugmentBehavior
    {
        public const string Id = "AUG-019";
        public const float HealthThreshold = 90f;  // "below 90 health"
        public const float Rank1Shield = 7f;       // "Gain 7 shield for 1 s, 5 s cooldown."
        public const float Rank2Shield = 12f;      // "Gain 12 shield for 1 s, 5 s cooldown."
        public const float ShieldSec = 1f;
        public const float CooldownSec = 5f;

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Shield, Rank2Shield)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _shield;
            private CastDuelEvent _commit;
            private int _lastActivation = int.MinValue / 2;

            public Hook(float shield) : base(Id)
            {
                _shield = shield;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (CastFacts.IsSpellCommit(e, Player, out CastDuelEvent c) && c.Cast.Mastery.IsMastered && AugmentsAKit.HealthBelow(Self, HealthThreshold))
                {
                    _commit = c;
                }
            }

            public override void OnTick()
            {
                if (_commit == null)
                {
                    return;
                }

                CastDuelEvent c = _commit;
                _commit = null;
                if (InCombat && Now - _lastActivation >= AugmentsAKit.Ticks(CooldownSec) && TryActivate(c.Id, -1, _shield))
                {
                    _lastActivation = Now;
                    World.GrantShield(Player, Id, _shield, AugmentsAKit.Ticks(ShieldSec)); // the 100 cap applies (L114)
                }
            }

            public override void Hash(ref StateHasher h) => h.Add(_lastActivation);
        }
    }

    /// <summary>
    /// AUG-020 Magnum Opus (Prismatic, always). Condition "Hit with advanced variations of 4 different spells within 6 s."
    /// Rank 1 "Fourth cast gains 8% direct damage.", rank 2 "Fourth cast gains 12% direct damage." Drawback "A miss resets
    /// progress; total mastery damage/speed bonus capped at 15%." Hits by mastered casts of distinct spells count while
    /// they fall inside 6 s; when three are counted, the direct damage of a fourth distinct spell's mastered cast gains
    /// 8% (12%), and the total mastery damage improvement of that cast stays at most 15% (spec L480). A released mastered
    /// cast that ends without damaging the enemy resets the progress, and the fourth hit completes it.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class MagnumOpus : IAugmentBehavior
    {
        public const string Id = "AUG-020";
        public const float Rank1Bonus = 0.08f; // "Fourth cast gains 8% direct damage."
        public const float Rank2Bonus = 0.12f; // "Fourth cast gains 12% direct damage."
        public const float WindowSec = 6f;     // "within 6 s"
        public const int SpellsNeeded = 4;     // "4 different spells"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private readonly List<Tracked> _casts = new List<Tracked>();
            private readonly List<Counted> _progress = new List<Counted>();
            private bool _completed;
            private int _completedOrigin;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            /// <summary>Distinct spells counted toward the opus (inside the 6 s window).</summary>
            public int Progress => _progress.Count;

            public override void OnEvent(DuelEvent e)
            {
                if (CastFacts.IsSpellCommit(e, Player, out CastDuelEvent commit))
                {
                    // Only attacks can hit or miss: a mastered buff or shield neither counts nor resets.
                    SpellForm form = AugmentsAKit.FormOf(Self, commit.SpellId);
                    if (commit.Cast.Mastery.IsMastered && form != null && form.Payload != null && form.Payload.IsDamaging)
                    {
                        _casts.Add(new Tracked { Cast = commit.CastId, SpellId = commit.SpellId });
                    }

                    return;
                }

                if (e is CastDuelEvent c && c.Player == Player)
                {
                    Tracked t = Find(c.CastId);
                    if (t == null) return;
                    if (c.Kind == CastEventKind.Released) t.Released = true;
                    else if (c.Kind == CastEventKind.Interrupted || c.Kind == CastEventKind.CommittedCastCancelled) _casts.Remove(t); // never released: no hit, no miss
                    return;
                }

                if (e is DamageEvent d && AugmentsAKit.IsOriginalHitOn(d, Player) && (d.Kind == DamageKind.Direct || d.Kind == DamageKind.Splash))
                {
                    Tracked t = Find(d.CastId);
                    if (t == null || t.Hit) return;
                    t.Hit = true;
                    Expire(d.Tick);
                    if (!Counts(t.SpellId))
                    {
                        _progress.Add(new Counted { SpellId = t.SpellId, Tick = d.Tick });
                    }

                    if (_progress.Count >= SpellsNeeded)
                    {
                        _completed = true; // the fourth distinct spell hit: the opus completes
                        _completedOrigin = d.Id;
                    }
                }
            }

            public override void OnTick()
            {
                if (_completed)
                {
                    _completed = false;
                    _progress.Clear();
                    TryActivate(_completedOrigin, 1 - Player);
                }

                for (int i = _casts.Count - 1; i >= 0; i--)
                {
                    Tracked t = _casts[i];
                    if (t.Hit && AugmentsAKit.FindCast(World, t.Cast) == null)
                    {
                        _casts.RemoveAt(i);
                    }
                    else if (t.Released && !t.Hit && AugmentsAKit.FindCast(World, t.Cast) == null)
                    {
                        _casts.RemoveAt(i);
                        _progress.Clear(); // "A miss resets progress."
                    }
                }

                Expire(Now);
            }

            // Mastery-linked ("total mastery damage/speed bonus capped at 15%"): summed with the other linked bonuses and
            // clamped once with the cast's reward (L480).
            public override float LinkedDirectDamage(DamageRequest hit, CastRecord cast)
            {
                if (hit.Kind != DamageKind.Direct || !hit.IsSpellHit)
                {
                    return 0f;
                }

                Tracked t = Find(hit.CastId);
                if (t == null || Counts(t.SpellId))
                {
                    return 0f;
                }

                int earliest = int.MaxValue;
                for (int i = 0; i < _progress.Count; i++) earliest = Math.Min(earliest, _progress[i].Tick);
                return _progress.Count == SpellsNeeded - 1 && Now - earliest <= AugmentsAKit.Ticks(WindowSec) ? _bonus : 0f;
            }

            private void Expire(int now)
            {
                // Hits count while the whole set fits in 6 s: drop the oldest until it does.
                _progress.RemoveAll(p => now - p.Tick > AugmentsAKit.Ticks(WindowSec));
            }

            private bool Counts(string spellId)
            {
                for (int i = 0; i < _progress.Count; i++)
                    if (string.Equals(_progress[i].SpellId, spellId, StringComparison.Ordinal)) return true;
                return false;
            }

            private Tracked Find(CastEventId id)
            {
                if (!id.IsValid) return null;
                for (int i = 0; i < _casts.Count; i++)
                    if (_casts[i].Cast == id) return _casts[i];
                return null;
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_casts.Count);
                for (int i = 0; i < _casts.Count; i++)
                {
                    h.Add((ulong)_casts[i].Cast.Packed);
                    h.Add(_casts[i].SpellId);
                    h.Add(_casts[i].Released);
                    h.Add(_casts[i].Hit);
                }

                h.Add(_completed);
                h.Add(_completedOrigin);
                h.Add(_progress.Count);
                for (int i = 0; i < _progress.Count; i++)
                {
                    h.Add(_progress[i].SpellId);
                    h.Add(_progress[i].Tick);
                }
            }

            private sealed class Tracked
            {
                public CastEventId Cast;
                public string SpellId;
                public bool Released;
                public bool Hit;
            }

            private struct Counted
            {
                public string SpellId;
                public int Tick;
            }
        }
    }

    /// <summary>
    /// AUG-001 Steady Hand (Silver, projectile spell equipped). Condition "Release a direct projectile after keeping aim
    /// within a 1-degree cone for 0.3 s." Rank 1 "Gain 6% projectile speed.", rank 2 "Gain 10% projectile speed."
    /// Drawback "Sprinting resets preparation." A direct projectile released after the owner's aim stayed within 1 degree
    /// of its release aim for the previous 0.3 s, without sprinting, flies 6% (10%) faster; on a cast whose mastery
    /// reward is projectile speed it clamps with it at 15% (spec L480).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SteadyHand : IAugmentBehavior
    {
        public const string Id = "AUG-001";
        public const float Rank1Speed = 0.06f;  // "Gain 6% projectile speed."
        public const float Rank2Speed = 0.10f;  // "Gain 10% projectile speed."
        public const float ConeDeg = 1f;        // "within a 1-degree cone"
        public const float SteadySec = 0.3f;    // "for 0.3 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _speed;
            private readonly AimHistory _aim = new AimHistory(AugmentsAKit.Ticks(SteadySec));

            public Hook(float speed) : base(Id)
            {
                _speed = speed;
            }

            public override void OnTick() => _aim.Record(Self, Self.Motor.Sprinting);

            // Mastery-linked: summed with the other linked bonuses and clamped once with the cast's reward (L480).
            public override float LinkedProjectileSpeed(CastRecord cast) =>
                CastFacts.IsDirectProjectile(cast.Form) && _aim.SteadyWithin(Self, ConeDeg) ? _speed : 0f;

            public override void Hash(ref StateHasher h) => _aim.Hash(ref h);
        }
    }

    /// <summary>The player's aim over the last ticks (dispatch samples), for steadiness and tracking conditions.</summary>
    internal sealed class AimHistory
    {
        private readonly Vec3[] _dirs;
        private readonly bool[] _sprint;
        private int _count;
        private int _next;

        public AimHistory(int ticks)
        {
            _dirs = new Vec3[Math.Max(1, ticks)];
            _sprint = new bool[_dirs.Length];
        }

        public void Record(DuelPlayer p, bool sprinting)
        {
            _dirs[_next] = CMath.ForwardFromYawPitch(p.Motor.YawDeg, p.Motor.PitchDeg);
            _sprint[_next] = sprinting;
            _next = (_next + 1) % _dirs.Length;
            _count = Math.Min(_count + 1, _dirs.Length);
        }

        /// <summary>The full window was recorded, nobody sprinted in it, and every sample is within the cone of the current aim.</summary>
        public bool SteadyWithin(DuelPlayer p, float coneDeg)
        {
            if (_count < _dirs.Length || p.Motor.Sprinting)
            {
                return false;
            }

            Vec3 now = CMath.ForwardFromYawPitch(p.Motor.YawDeg, p.Motor.PitchDeg);
            float cos = (float)Math.Cos(coneDeg * Math.PI / 180.0) - 1e-6f;
            for (int i = 0; i < _count; i++)
            {
                if (_sprint[i] || Vec3.Dot(_dirs[i], now) < cos) return false;
            }

            return true;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_count);
            h.Add(_next);
            for (int i = 0; i < _dirs.Length; i++)
            {
                h.Add(_dirs[i]);
                h.Add(_sprint[i]);
            }
        }
    }

    /// <summary>
    /// AUG-005 On the Beat (Silver, WIZ-002/004/007/010/011/018/019/020/021/035). Condition "Complete optional timing
    /// variation within the wizard's published timing window." Rank 1 "Gain 6% preparation speed for that cast.", rank 2
    /// "Gain 10% preparation speed for that cast." Drawback "Window remains fixed; benefit does not shorten the telegraph
    /// floor." A cast that earns its timing mastery reward has its intrinsic windup (the preparation after the grammar,
    /// spec L525) 6% (10%) shorter, rounding down to whole ticks; the grammar's own window is unchanged and the 0.25 s
    /// anticipatory cue still holds before release (L96).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class OnTheBeat : IAugmentBehavior
    {
        public const string Id = "AUG-005";
        public const float Rank1Speed = 0.06f; // "Gain 6% preparation speed for that cast."
        public const float Rank2Speed = 0.10f; // "Gain 10% preparation speed for that cast."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _speed;

            public Hook(float speed) : base(Id)
            {
                _speed = speed;
            }

            public override void AdjustCastTiming(in CastQuote quote, ref CastTimingAdjust timing)
            {
                Confluence.Casting.CastableSpell spell = Self.Caster != null ? Self.Caster.Machine.CurrentSpell : null;
                if (spell != null && !spell.IsSignature)
                {
                    timing.MasteredWindupReduction += _speed;
                }
            }
        }
    }

    /// <summary>
    /// AUG-017 Recovered Rhythm (Prismatic, WIZ-002/004/007/010/011/018/019/020/021/035). Condition "Fail one optional timing
    /// variation, then succeed on a different spell within 4 s." Rank 1 "Successful cast refunds 2 mana.", rank 2
    /// "Successful cast refunds 4 mana." Drawback "Intentional failure still pays normal cast costs and cooldown." A cast
    /// whose grammar variation failed (a normal result) arms 4 s in which a mastered cast of a different spell refunds 2
    /// (4) mana through that cast's refund ledger (25% of its payment, once, spec L90, L124). The failed cast pays and
    /// cools down normally.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RecoveredRhythm : IAugmentBehavior
    {
        public const string Id = "AUG-017";
        public const float Rank1Refund = 2f; // "Successful cast refunds 2 mana."
        public const float Rank2Refund = 4f; // "Successful cast refunds 4 mana."
        public const float WindowSec = 4f;   // "within 4 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Refund, Rank2Refund)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _refund;
            private readonly PaymentLog _payments = new PaymentLog();
            private string _failedSpell;
            private int _failedTick = int.MinValue / 2;
            private CastDuelEvent _success;

            public Hook(float refund) : base(Id)
            {
                _refund = refund;
            }

            public override void OnEvent(DuelEvent e)
            {
                _payments.Observe(e, Player);
                if (!CastFacts.IsSpellCommit(e, Player, out CastDuelEvent c))
                {
                    return;
                }

                if (c.Cast.Mastery.IsMastered)
                {
                    if (_failedSpell != null && c.Tick - _failedTick <= AugmentsAKit.Ticks(WindowSec) && !string.Equals(c.SpellId, _failedSpell, StringComparison.Ordinal))
                    {
                        _success = c;
                        _failedSpell = null;
                    }
                }
                else if (CastFacts.IsFailedVariation(c.Cast.Mastery))
                {
                    _failedSpell = c.SpellId;
                    _failedTick = c.Tick;
                }
            }

            public override void OnTick()
            {
                if (_success == null)
                {
                    return;
                }

                CastDuelEvent c = _success;
                _success = null;
                int payment = _payments.PaymentOf(c.CastId);
                if (InCombat && payment != 0 && TryActivate(c.Id, -1, _refund))
                {
                    World.Effects.RefundCast(Player, payment, _refund, Key); // the cast's 25% ledger (L90, L124)
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_failedSpell);
                h.Add(_failedTick);
                h.Add(_success != null ? _success.Id : 0);
                _payments.Hash(ref h);
            }
        }
    }

    /// <summary>
    /// AUG-009 Needle Discipline (Gold, projectile spell equipped). Condition "A direct projectile hits within 0.2 m of a
    /// head weak point." Rank 1 "Gain 5% direct damage, within 1.25 precision cap.", rank 2 "Gain 8% direct damage,
    /// within 1.25 precision cap." Drawback "Non-precision projectile damage decreases 3%." The head weak point is the top
    /// 0.3 m of the struck body (spec L110, D-145 deliveries); a direct projectile impact within 0.2 m of it gains 5% (8%),
    /// with any precision multiplier the total stays at most 1.25 (L110). Other direct projectile hits deal 3% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class NeedleDiscipline : IAugmentBehavior
    {
        public const string Id = "AUG-009";
        public const float Rank1Bonus = 0.05f;    // "Gain 5% direct damage, within 1.25 precision cap."
        public const float Rank2Bonus = 0.08f;    // "Gain 8% direct damage, within 1.25 precision cap."
        public const float NearHeadMeters = 0.2f; // "within 0.2 m of a head weak point"
        public const float HeadZoneMeters = 0.3f; // the weak point: the top 0.3 m (ProjectileDelivery)
        public const float OffHeadPenalty = 0.03f; // "Non-precision projectile damage decreases 3%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private CastEventId _impactCast = CastEventId.None;
            private Vec3 _impactPoint;
            private int _impactTick = -1;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is SpellEvent s && s.Player == Player && s.Kind == SpellEventKind.ProjectileImpact && s.TargetPlayer == 1 - Player)
                {
                    _impactCast = s.CastId;
                    _impactPoint = s.Position;
                    _impactTick = s.Tick;
                }
            }

            /// <summary>The owner's projectile impact this tick: true near the head, false elsewhere, null for other hits.</summary>
            private bool? NearHead(DamageRequest hit)
            {
                if (hit.Kind != DamageKind.Direct || !hit.IsSpellHit || hit.CastId != _impactCast || _impactTick != Now)
                {
                    return null; // only the direct impact of the owner's projectile this tick
                }

                Capsule body = World.Player(hit.Target).HitVolume;
                float headBottom = body.B.Y + body.Radius - HeadZoneMeters;
                return _impactPoint.Y >= headBottom - NearHeadMeters;
            }

            // The bonus is mastery-linked (Casting mastery): summed and clamped once with the cast's reward at 15% (L480).
            public override float LinkedDirectDamage(DamageRequest hit, CastRecord cast)
            {
                if (NearHead(hit) != true) return 0f;
                float allowed = _bonus;
                if (hit.PrecisionTagged && hit.PrecisionMultiplier > 1f)
                {
                    allowed = Math.Min(allowed, GlobalRules.Damage.PrecisionMultiplierMax / Math.Min(hit.PrecisionMultiplier, GlobalRules.Damage.PrecisionMultiplierMax) - 1f);
                }

                return Math.Max(0f, allowed);
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (NearHead(hit) == false)
                {
                    fraction -= OffHeadPenalty; // the drawback is not a mastery bonus
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add((ulong)_impactCast.Packed);
                h.Add(_impactTick);
                h.Add(_impactPoint);
            }
        }
    }

    /// <summary>
    /// AUG-013 Patient Cursor (Gold, projectile spell equipped). Condition "Track visible enemy within 2-degree aim cone
    /// for 0.6 s before release." Rank 1 "Next direct projectile gains 5% damage.", rank 2 "Next direct projectile gains
    /// 8% damage." Drawback "Cover or loss of aim resets preparation." While the enemy is acquirable (visible and in clear
    /// sight, spec L120, L167) and the owner's aim stays within 2 degrees of the enemy's chest, tracking builds up; a
    /// direct projectile released after 0.6 s of it deals 5% (8%) more with its direct hits. Losing sight (cover, steam)
    /// or the aim resets the tracking, and the release uses it.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PatientCursor : IAugmentBehavior
    {
        public const string Id = "AUG-013";
        public const float Rank1Bonus = 0.05f; // "Next direct projectile gains 5% damage."
        public const float Rank2Bonus = 0.08f; // "Next direct projectile gains 8% damage."
        public const float ConeDeg = 2f;       // "within 2-degree aim cone"
        public const float TrackSec = 0.6f;    // "for 0.6 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private int _tracked;
            private CastEventId _boosted = CastEventId.None;
            private bool _released;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.Released && c.Cast.Slot >= 0
                    && CastFacts.IsDirectProjectile(AugmentsAKit.FormOf(Self, c.SpellId)))
                {
                    if (_tracked >= AugmentsAKit.Ticks(TrackSec) && OnTarget())
                    {
                        _boosted = c.CastId;
                    }

                    _released = true;
                }
            }

            public override void OnTick()
            {
                if (_released)
                {
                    _released = false;
                    _tracked = 0; // the release uses the tracking
                    if (_boosted.IsValid && !TryActivate())
                    {
                        _boosted = CastEventId.None;
                    }

                    return;
                }

                _tracked = InCombat && OnTarget() ? _tracked + 1 : 0;
            }

            private bool OnTarget()
            {
                int enemy = 1 - Player;
                if (!World.Player(enemy).Alive || !World.CanAcquire(Player, enemy))
                {
                    return false;
                }

                DuelPlayer t = World.Player(enemy);
                Vec3 toChest = (t.Position + new Vec3(0f, t.Motor.Height * 0.6f, 0f)) - Self.Eye;
                Vec3 aim = CMath.ForwardFromYawPitch(Self.Motor.YawDeg, Self.Motor.PitchDeg);
                return toChest.LengthSquared() > 1e-6f && Vec3.Dot(aim, toChest.Normalized()) >= (float)Math.Cos(ConeDeg * Math.PI / 180.0) - 1e-6f;
            }

            // A Casting mastery record: mastery-linked, summed and clamped once with the cast's reward at 15% (L480).
            public override float LinkedDirectDamage(DamageRequest hit, CastRecord cast) =>
                _boosted.IsValid && hit.CastId == _boosted && hit.Kind == DamageKind.Direct && hit.IsSpellHit ? _bonus : 0f;

            public override void Hash(ref StateHasher h)
            {
                h.Add(_tracked);
                h.Add((ulong)_boosted.Packed);
            }
        }
    }

    /// <summary>
    /// AUG-018 Deliberate Repetition (Prismatic, projectile spell equipped). Condition "Complete advanced variations on the
    /// same spell twice, at least 1 s apart." Rank 1 "Second variation gains 7% projectile speed.", rank 2 "Second variation
    /// gains 11% projectile speed." Drawback "A different spell resets progress." A mastered cast of a spell followed, at
    /// least 1 s later, by another mastered cast of the same spell makes the second one's projectiles 7% (11%) faster
    /// (clamping with a projectile-speed reward at 15%, spec L480); committing a different spell in between resets it.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class DeliberateRepetition : IAugmentBehavior
    {
        public const string Id = "AUG-018";
        public const float Rank1Speed = 0.07f; // "Second variation gains 7% projectile speed."
        public const float Rank2Speed = 0.11f; // "Second variation gains 11% projectile speed."
        public const float ApartSec = 1f;      // "at least 1 s apart"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _speed;
            private string _firstSpell;
            private int _firstTick;
            private CastEventId _second = CastEventId.None;
            private bool _activate;

            public Hook(float speed) : base(Id)
            {
                _speed = speed;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (!CastFacts.IsSpellCommit(e, Player, out CastDuelEvent c))
                {
                    return;
                }

                bool same = _firstSpell != null && string.Equals(c.SpellId, _firstSpell, StringComparison.Ordinal);
                if (!same)
                {
                    _firstSpell = c.Cast.Mastery.IsMastered ? c.SpellId : null; // a different spell resets (and may start anew)
                    _firstTick = c.Tick;
                    return;
                }

                if (c.Cast.Mastery.IsMastered && c.Tick - _firstTick >= AugmentsAKit.Ticks(ApartSec))
                {
                    _second = c.CastId;
                    _activate = true;
                    _firstSpell = null;
                }
            }

            public override void OnTick()
            {
                if (_activate)
                {
                    _activate = false;
                    if (!TryActivate()) _second = CastEventId.None;
                }
            }

            // Mastery-linked: summed with the other linked bonuses and clamped once with the cast's reward (L480).
            public override float LinkedProjectileSpeed(CastRecord cast) =>
                _second.IsValid && cast.CastId == _second ? _speed : 0f;

            public override void Hash(ref StateHasher h)
            {
                h.Add(_firstSpell);
                h.Add(_firstTick);
                h.Add((ulong)_second.Packed);
            }
        }
    }
}
