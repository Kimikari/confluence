using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-075 Bubble Buckler (WIZ-010, directional defense): "Gain a forward 45-point shield for 4 s; protects a
    /// 100-degree arc; common shield cap applies." World interaction "Water 1 when broken; Heat consumes 10 shield to create
    /// a 1 m steam puff." Mana 18, cooldown 10 s, 0.2 s windup from the record (spec L525).
    /// <list type="bullet">
    /// <item>The shield joins the holder's shield pool: the shared 100 cap and the 8 s ceiling apply (spec L114). It protects
    /// only hits whose source lies within 50 degrees of the holder's facing, so its back is exposed (spec L549; counterplay
    /// "Flank, use melee from another angle").</item>
    /// <item>Heat: each original or secondary thermal hit the bubble absorbs is a heat reaction that consumes 10 more shield
    /// (if the bubble survives the hit) and makes a 1 m steam puff in front of the holder (the spec L144 3 s base
    /// lifetime). The puff is part of the cast (spec L126), owned by the caster, and counts as one of the caster's fields
    /// (spec L128, D-149).</item>
    /// <item>"Water 1 when broken": a bubble emptied before its 4 s end leaves Water 1 at its front; a timed expiry leaves
    /// nothing.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BubbleBuckler : ISpellBehavior
    {
        public const string Id = "SPL-075";

        public static readonly ProseParameter Capacity = ProseParameter.Of("shield", 45, "Gain a forward 45-point shield");
        public static readonly ProseParameter Duration = ProseParameter.Of("duration", 4, "shield for 4 s");
        public static readonly ProseParameter Arc = ProseParameter.HalfAngle("arc_half_angle", 100, "protects a 100-degree arc");
        public static readonly ProseParameter BreakWater = ProseParameter.Of("break_water", 1, "Water 1 when broken");
        public static readonly ProseParameter HeatLoss = ProseParameter.Of("heat_shield_loss", 10, "Heat consumes 10 shield");
        public static readonly ProseParameter PuffRadius = ProseParameter.Of("steam_puff_radius", 1, "1 m steam puff");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Capacity, Duration, Arc, BreakWater, HeatLoss, PuffRadius };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new BubbleShieldDelivery
            {
                Capacity = Capacity.Float,
                DurationSec = Duration.Float,
                ArcHalfDeg = Arc.Float,
                BreakWaterStrength = (int)BreakWater.Value,
                HeatShieldLoss = HeatLoss.Float,
                HeatPuffRadius = PuffRadius.Float,
                HeatPuffLifetimeSec = HalenRules.BubbleSteamLifetimeSec,
            };
        }
    }

    /// <summary>
    /// The water bubble shield of SPL-075 and its transformations: a directional shield in the holder's pool (spec L114,
    /// L549) watched by a <see cref="BubbleShieldEntity"/> for heat reactions, breaks, and the AUG-437 release recast.
    /// </summary>
    public sealed class BubbleShieldDelivery : DeliverySpec
    {
        private float _capacity;
        private float _durationSec;
        private float _arcHalfDeg;
        private int _breakWater = 1;
        private float _heatLoss;
        private float _heatPuffRadius;
        private float _heatPuffLifetimeSec;
        private float _thermalBreakPuffRadius;
        private float _thermalBreakPuffLifetimeSec;
        private ProjectileDelivery _release;
        private PayloadSpec _releasePayload;

        public override DeliveryKind Kind => DeliveryKind.Shield;

        public float Capacity { get => _capacity; set => _capacity = Set(value); }
        public float DurationSec { get => _durationSec; set => _durationSec = Set(value); }

        /// <summary>Half of the protected arc ("a 100-degree arc" is 50).</summary>
        public float ArcHalfDeg { get => _arcHalfDeg; set => _arcHalfDeg = Set(value); }

        /// <summary>Water left at the front when the bubble breaks ("Water 1 when broken").</summary>
        public int BreakWaterStrength { get => _breakWater; set => _breakWater = Set(value); }

        /// <summary>Shield a heat reaction consumes ("Heat consumes 10 shield"; AUG-438 "15 instead of 10").</summary>
        public float HeatShieldLoss { get => _heatLoss; set => _heatLoss = Set(value); }

        public float HeatPuffRadius { get => _heatPuffRadius; set => _heatPuffRadius = Set(value); }
        public float HeatPuffLifetimeSec { get => _heatPuffLifetimeSec; set => _heatPuffLifetimeSec = Set(value); }

        /// <summary>AUG-438: a thermal break turns the break water into a steam puff of this radius; 0 = none.</summary>
        public float ThermalBreakPuffRadius { get => _thermalBreakPuffRadius; set => _thermalBreakPuffRadius = Set(value); }

        public float ThermalBreakPuffLifetimeSec { get => _thermalBreakPuffLifetimeSec; set => _thermalBreakPuffLifetimeSec = Set(value); }

        /// <summary>AUG-437: the recast throws the remaining shield as this projectile; null = no recast.</summary>
        public ProjectileDelivery ReleaseProjectile { get => _release; set => _release = Set(value); }

        public PayloadSpec ReleasePayload { get => _releasePayload; set => _releasePayload = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _durationSec = Math.Min(Math.Max(0f, _durationSec), GlobalRules.Shields.MaxTimedDurationSec); // spec L114
            _capacity = Math.Min(Math.Max(0f, _capacity), GlobalRules.Shields.Cap);
            _arcHalfDeg = CMath.Clamp(_arcHalfDeg, 0f, 180f);
            _heatPuffLifetimeSec = Math.Min(Math.Max(0f, _heatPuffLifetimeSec), GlobalRules.FieldsAndSummons.MaxLifetimeSec);
            _thermalBreakPuffLifetimeSec = Math.Min(Math.Max(0f, _thermalBreakPuffLifetimeSec), GlobalRules.FieldsAndSummons.MaxLifetimeSec);
            _release?.ApplyGlobalCaps();
            CapNested(_releasePayload);
        }

        protected override void OnFreeze()
        {
            _release?.Freeze();
            _releasePayload?.Freeze();
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.Player(caster).Position;

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            // A new bubble supersedes the caster's previous one (the pool tops up the same source, spec L114).
            IReadOnlyList<SpellEntity> entities = ctx.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].Alive && entities[i] is BubbleShieldEntity old && old.Owner == cast.Caster) old.Supersede(ctx);
            }

            int ticks = Ticks.FromSeconds(DurationSec);
            float added = ctx.World.GrantShield(cast.Caster, cast.SpellId, Capacity, ticks, false, ArcHalfDeg);
            ctx.Emit(SpellEventKind.ShieldRaised, cast, ctx.Player(cast.Caster).Position, ctx.AimDirection(cast.Caster), 0, added, ticks: ticks);
            ShieldEntry entry = BubbleShieldEntity.FindEntry(ctx.Player(cast.Caster).Shields, cast.SpellId);
            if (entry == null) return; // the shared 100 cap left no room (spec L114)
            BubbleShieldEntity bubble = ctx.Spawn(new BubbleShieldEntity(cast, this, entry));
            ctx.World.AddEventObserver(bubble);
        }
    }

    /// <summary>
    /// A live SPL-075 bubble: watches its shield entry for thermal absorption (an observer that only records facts, then
    /// acts at its step, tick-order step 4), turns heat into steam, leaves its break water, and throws itself on the
    /// AUG-437 recast.
    /// </summary>
    public sealed class BubbleShieldEntity : SpellEntity, IDuelEventObserver
    {
        private readonly BubbleShieldDelivery _spec;
        private readonly int _entryId;
        private readonly int _expireTick;
        private float _lastAmount;
        private int _pendingHeat;
        private bool _thermalBreak;
        private bool _ending;

        internal BubbleShieldEntity(CastRecord cast, BubbleShieldDelivery spec, ShieldEntry entry)
            : base(cast)
        {
            _spec = spec;
            _entryId = entry.Id;
            _expireTick = entry.ExpireTick;
            _lastAmount = entry.Amount;
        }

        public BubbleShieldDelivery Spec => _spec;
        public int ExpireTick => _expireTick;

        public override bool AcceptsRecast => _spec.ReleaseProjectile != null;

        /// <summary>The bubble's shield entry in a pool, or null when it is gone.</summary>
        public static ShieldEntry FindEntry(ShieldPool pool, string sourceId)
        {
            IReadOnlyList<ShieldEntry> entries = pool.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].Reserve && string.Equals(entries[i].SourceId, sourceId, StringComparison.Ordinal)) return entries[i];
            }

            return null;
        }

        private ShieldEntry Entry(DuelPlayer holder)
        {
            IReadOnlyList<ShieldEntry> entries = holder.Shields.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id == _entryId) return entries[i];
            }

            return null;
        }

        /// <summary>Remaining bubble capacity (0 when gone).</summary>
        public float Remaining(DuelWorld world)
        {
            ShieldEntry e = Entry(world.Player(Owner));
            return e == null ? 0f : e.Amount;
        }

        public void OnEvent(DuelWorld world, DuelEvent e)
        {
            if (!Alive || _ending || !(e is DamageEvent d) || d.Target != Owner || d.Resolution == null) return;
            ShieldEntry entry = Entry(world.Player(Owner));
            float now = entry == null ? 0f : entry.Amount;
            if (now < _lastAmount - 1e-4f && IsThermal(d))
            {
                // Heat reached the bubble's water (spec L144): the reaction runs at the bubble's next step.
                if (entry != null) _pendingHeat++;
                else _thermalBreak = true;
            }

            _lastAmount = now;
        }

        private static bool IsThermal(DamageEvent d)
        {
            ChannelDamage[] channels = d.Resolution.Channels;
            if (channels == null) return false;
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i].Channel == DamageChannel.Thermal && channels[i].Mitigated > 0f) return true;
            }

            return false;
        }

        protected internal override void Step(SpellContext ctx)
        {
            DuelPlayer holder = ctx.Player(Owner);
            ShieldEntry entry = Entry(holder);
            bool reactionBreak = false;
            while (_pendingHeat > 0 && entry != null)
            {
                _pendingHeat--;
                float take = Math.Min(entry.Amount, _spec.HeatShieldLoss);
                entry.Amount -= take;
                bool emptied = entry.Amount <= 0f;
                if (emptied)
                {
                    holder.Shields.Absorb(0f); // drops the emptied entry from the pool
                    entry = null;
                    _thermalBreak = true;
                    reactionBreak = true;
                }

                // A reaction that empties the bubble with AUG-438 makes the one larger thermal-break puff instead.
                if (!(emptied && _spec.ThermalBreakPuffRadius > 0f))
                {
                    HalenRules.CreateCloud(ctx, Cast, VolumeKind.Steam, Front(holder), _spec.HeatPuffRadius, _spec.HeatPuffLifetimeSec, 1);
                }

                HalenRules.Emit(ctx, TideEventKind.BubbleHeatReaction, Cast, Front(holder), take);
            }

            _pendingHeat = 0;
            if (entry == null)
            {
                if (ctx.Tick < _expireTick) Break(ctx, holder, _thermalBreak, reactionBreak);
                End(ctx);
                return;
            }

            _lastAmount = entry.Amount;
            if (ctx.Tick >= _expireTick || !holder.Alive) End(ctx);
        }

        // "Water 1 when broken"; AUG-438 "Thermal shield break consumes Water 1 and creates 1.5 m steam puff".
        private void Break(SpellContext ctx, DuelPlayer holder, bool thermal, bool byReaction)
        {
            Vec3 front = Front(holder);
            if (thermal && _spec.ThermalBreakPuffRadius > 0f)
            {
                HalenRules.CreateCloud(ctx, Cast, VolumeKind.Steam, front, _spec.ThermalBreakPuffRadius, _spec.ThermalBreakPuffLifetimeSec, _spec.BreakWaterStrength);
            }
            else
            {
                HalenRules.DepositWater(ctx, Cast, front, _spec.BreakWaterStrength, 0f, HalenRules.ImpactWaterLifetimeSec);
            }

            HalenRules.Emit(ctx, TideEventKind.BubbleBroken, Cast, front, thermal ? 1f : 0f);
        }

        private static Vec3 Front(DuelPlayer holder) => holder.Position + CMath.ForwardFromYaw(holder.Motor.YawDeg) * HalenRules.BubbleFrontOffset;

        /// <summary>AUG-437 "Recast throws remaining shield as 20 m/s 24-damage bubble, ending guard."</summary>
        protected internal override void OnRecast(SpellContext ctx)
        {
            if (!Alive || _spec.ReleaseProjectile == null) return;
            DuelPlayer holder = ctx.Player(Owner);
            ShieldEntry entry = Entry(holder);
            float thrown = entry == null ? 0f : entry.Amount;
            if (entry != null)
            {
                entry.Amount = 0f;
                holder.Shields.Absorb(0f); // the throw ends the shield (no break water: it was released, not broken)
            }

            _ending = true;
            Vec3 origin = ctx.AimOrigin(Owner);
            Vec3 dir = ctx.AimDirection(Owner);
            float speed = _spec.ReleaseProjectile.Speed;
            ProjectileEntity bubble = ctx.Spawn(new ProjectileEntity(Cast, _spec.ReleaseProjectile, origin, dir * speed, _spec.ReleasePayload));
            ctx.Emit(SpellEventKind.ProjectileLaunched, Cast, origin, dir, bubble.Id, speed);
            HalenRules.Emit(ctx, TideEventKind.BubbleReleased, Cast, origin, thrown);
            End(ctx);
        }

        /// <summary>A newer bubble of the same caster replaced this one (same shield source).</summary>
        internal void Supersede(SpellContext ctx)
        {
            _ending = true;
            End(ctx);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_entryId);
            h.Add(_expireTick);
            h.Add(_lastAmount);
            h.Add(_pendingHeat);
            h.Add(_thermalBreak);
            h.Add(_ending);
        }
    }
}
