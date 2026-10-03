using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A self shield (spec L114): capacity for at most 8 s within the shared 100 cap, topping up compatible capacity of
    /// the same source; a directional shield protects only the arc either side of the holder's facing (SPL-075
    /// "protects a 100-degree arc"), exposing its back (spec L549).
    /// </summary>
    public sealed class ShieldDelivery : DeliverySpec
    {
        private float _capacity;
        private float _durationSec;
        private float _arcHalfDeg;

        public override DeliveryKind Kind => DeliveryKind.Shield;

        public float Capacity { get => _capacity; set => _capacity = Set(value); }

        /// <summary>Seconds; at most 8 (spec L114 "cast and triggered shields expire after at most 8 seconds").</summary>
        public float DurationSec { get => _durationSec; set => _durationSec = Set(value); }

        /// <summary>Half of the protected arc ("100-degree arc" is 50); 0 protects all round.</summary>
        public float ArcHalfDeg { get => _arcHalfDeg; set => _arcHalfDeg = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _durationSec = Math.Min(_durationSec, GlobalRules.Shields.MaxTimedDurationSec);
            _capacity = Math.Min(Math.Max(0f, _capacity), GlobalRules.Shields.Cap);
            _arcHalfDeg = CMath.Clamp(_arcHalfDeg, 0f, 180f);
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.Player(caster).Position;

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            float added = ctx.World.GrantShield(cast.Caster, cast.SpellId, Capacity, Ticks.FromSeconds(DurationSec), false, ArcHalfDeg);
            ctx.Emit(SpellEventKind.ShieldRaised, cast, ctx.Player(cast.Caster).Position, ctx.AimDirection(cast.Caster), 0, added,
                ticks: Ticks.FromSeconds(DurationSec));
        }
    }

    /// <summary>
    /// A timed personal buff: stat modifiers through the player's stat sheet (its global caps apply, spec L112, L124)
    /// and a reduction of incoming projectile damage (SPL-011 "reduces incoming projectile damage 12%"), which the
    /// pipeline combines with the other passive reductions inside the 45% cap (spec L112).
    /// </summary>
    public sealed class BuffDelivery : DeliverySpec
    {
        private readonly List<StatModifier> _modifiers = new List<StatModifier>();
        private float _durationSec;
        private float _projectileReduction;

        public override DeliveryKind Kind => DeliveryKind.Buff;

        public float DurationSec { get => _durationSec; set => _durationSec = Set(value); }

        /// <summary>Fraction of incoming projectile damage removed while the buff lasts.</summary>
        public float ProjectileDamageReduction { get => _projectileReduction; set => _projectileReduction = Set(value); }

        /// <summary>Stat modifiers held while the buff lasts ("movement -15%" is MovementBonus -0.15).</summary>
        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        public void AddModifier(StatKind stat, float value, DamageChannel channel = DamageChannel.None)
        {
            Set(0);
            _modifiers.Add(new StatModifier(stat, value, null, channel));
        }

        internal override void ApplyGlobalCaps()
        {
            if (!(_durationSec > 0f)) throw new InvalidOperationException("A buff needs a positive duration.");
            _projectileReduction = CMath.Clamp(_projectileReduction, 0f, GlobalRules.Damage.PassiveReductionCap);
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.Player(caster).Position;

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            BuffEntity buff = ctx.Spawn(new BuffEntity(cast, this, ctx.Tick + Ticks.FromSeconds(DurationSec)));
            buff.Apply(ctx);
        }
    }

    /// <summary>A live buff on its caster (see <see cref="BuffDelivery"/>).</summary>
    public sealed class BuffEntity : SpellEntity
    {
        private readonly BuffDelivery _spec;
        private readonly int _endTick;
        private string _sourceKey;

        internal BuffEntity(CastRecord cast, BuffDelivery spec, int endTick)
            : base(cast)
        {
            _spec = spec;
            _endTick = endTick;
        }

        public BuffDelivery Spec => _spec;
        public int EndTick => _endTick;

        internal void Apply(SpellContext ctx)
        {
            // A per-cast source key keeps overlapping casts of the same spell separately removable.
            _sourceKey = SpellId + "#" + Cast.CastId.Sequence.ToString(CultureInfo.InvariantCulture);
            StatSheet stats = ctx.Player(Owner).Stats;
            for (int i = 0; i < _spec.Modifiers.Count; i++)
            {
                StatModifier m = _spec.Modifiers[i];
                stats.Add(m.Stat, m.Value, _sourceKey, m.Channel);
            }

            ctx.Emit(SpellEventKind.BuffStarted, Cast, ctx.Player(Owner).Position, default, Id, ticks: _endTick - ctx.Tick);
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (ctx.Tick >= _endTick || !ctx.Player(Owner).Alive) End(ctx);
        }

        protected override void OnEnded(SpellContext ctx)
        {
            if (_sourceKey != null) ctx.Player(Owner).Stats.RemoveSource(_sourceKey);
            ctx.Emit(SpellEventKind.BuffEnded, Cast, ctx.Player(Owner).Position, default, Id);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_endTick);
            h.Add(_sourceKey);
        }
    }
}
