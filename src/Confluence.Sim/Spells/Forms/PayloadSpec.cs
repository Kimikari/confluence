using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Spells
{
    /// <summary>Which way a payload's push moves its target (spec L173: at most 4 m per effect).</summary>
    public enum PushDirection
    {
        /// <summary>Away from the impact or caster.</summary>
        AwayFromSource,

        /// <summary>Along the delivery's travel or facing direction (cones, waves, palms).</summary>
        AlongDelivery,

        /// <summary>Toward the caster (pulls, tethers).</summary>
        TowardCaster,
    }

    /// <summary>Which hits of a payload apply a status.</summary>
    public enum StatusTrigger
    {
        /// <summary>Every damaging contact (direct, splash, field contact).</summary>
        AnyHit,

        /// <summary>Only the direct hit.</summary>
        DirectHitOnly,
    }

    /// <summary>A named status a payload applies (spec L116-L120; global caps apply in the status set).</summary>
    public sealed class StatusSpec : SpellSpec
    {
        private StatusKind _kind;
        private float _durationSec;
        private float _intensity;
        private DamageChannel _channel;
        private StatusTrigger _trigger;

        public StatusKind Kind { get => _kind; set => _kind = Set(value); }

        public float DurationSec { get => _durationSec; set => _durationSec = Set(value); }

        /// <summary>DoT damage per second, or slow fraction.</summary>
        public float Intensity { get => _intensity; set => _intensity = Set(value); }

        public DamageChannel Channel { get => _channel; set => _channel = Set(value); }

        public StatusTrigger Trigger { get => _trigger; set => _trigger = Set(value); }

        public int DurationTicks => Ticks.FromSeconds(DurationSec);

        public bool IsHardControl => StatusRules.IsHardControl(Kind);

        /// <summary>Clamps to the global status caps (spec L116): hard control 0.75 s, silence 0.6 s, slows 30% and 3 s.</summary>
        internal void ApplyGlobalCaps()
        {
            StatusRule rule = StatusRules.Get(Kind);
            if (rule.MaxTicks != int.MaxValue) _durationSec = Math.Min(_durationSec, rule.MaxTicks / (float)Ticks.PerSecond);
            if (rule.MaxIntensity < float.MaxValue) _intensity = Math.Min(_intensity, rule.MaxIntensity);
        }
    }

    /// <summary>Where a payload's world deposit lands (spec L136, L163).</summary>
    public enum DepositPlacement
    {
        /// <summary>At the impact point: its cell, or a circle of <see cref="DepositSpec.Radius"/>.</summary>
        Impact,

        /// <summary>Over the delivery's own ground footprint (a cone, a lane, a field area).</summary>
        Footprint,

        /// <summary>Along the travelled path (dash trails), <see cref="DepositSpec.Radius"/> as half width.</summary>
        Path,

        /// <summary>At the caster's feet.</summary>
        Caster,
    }

    /// <summary>A world deposit the payload emits through the materials seam (element, units, footprint, lifetime).</summary>
    public sealed class DepositSpec : SpellSpec
    {
        private DepositKind _kind;
        private int _strength = 1;
        private DepositPlacement _placement;
        private float _radius;
        private float _lifetimeSec;

        public DepositKind Kind { get => _kind; set => _kind = Set(value); }

        /// <summary>Units per covered cell; adding strength never exceeds four per cell (spec L163).</summary>
        public int Strength { get => _strength; set => _strength = Set(value); }

        public DepositPlacement Placement { get => _placement; set => _placement = Set(value); }

        public float Radius { get => _radius; set => _radius = Set(value); }

        /// <summary>How long the units persist (0: instant residue that reacts this step).</summary>
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        internal void ApplyGlobalCaps()
        {
            _strength = Math.Max(0, Math.Min(_strength, GlobalRules.World.MaxUnitsPerCell));
            _lifetimeSec = Math.Max(0f, Math.Min(_lifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec)); // spec L128
        }
    }

    /// <summary>
    /// What one hit of a delivery does: per-channel damage (direct, splash, with exclusivity and per-target caps),
    /// guard and precision rules, push or pull, statuses, and world deposits (spec L110-L120, L163, L525).
    /// </summary>
    public sealed class PayloadSpec : SpellSpec
    {
        private readonly List<StatusSpec> _statuses = new List<StatusSpec>();
        private readonly List<DepositSpec> _deposits = new List<DepositSpec>();
        private DamageChannel _channel;
        private DamageChannel _splitChannel;
        private float _splitFraction;
        private float _direct;
        private float _splash;
        private float _splashRadius;
        private bool _splashExcludesDirect;
        private bool _splashHitsCaster;
        private bool _splashBlockedByCover = true;
        private float _maxPerTarget;
        private bool _guardEligible = true;
        private float _precisionMultiplier = 1f;
        private float _push;
        private PushDirection _pushDirection;
        private float _solidDamage = -1f;

        public DamageChannel Channel { get => _channel; set => _channel = Set(value); }

        /// <summary>A second channel for a split attack (spec L110, L112: each channel resolves separately and rejoins as one hit).</summary>
        public DamageChannel SplitChannel { get => _splitChannel; set => _splitChannel = Set(value); }

        /// <summary>Fraction of every damage amount dealt in <see cref="SplitChannel"/> (0 = a single channel).</summary>
        public float SplitFraction { get => _splitFraction; set => _splitFraction = Set(value); }

        /// <summary>The primary payload's direct damage; mastery direct-damage rewards apply to it only (spec L480).</summary>
        public float Direct { get => _direct; set => _direct = Set(value); }

        /// <summary>Area damage around an impact (not a direct hit; never guarded, spec L104).</summary>
        public float Splash { get => _splash; set => _splash = Set(value); }

        public float SplashRadius { get => _splashRadius; set => _splashRadius = Set(value); }

        /// <summary>"Direct and splash cannot both damage one target" (spec L525): a directly hit target takes no splash.</summary>
        public bool SplashExcludesDirect { get => _splashExcludesDirect; set => _splashExcludesDirect = Set(value); }

        /// <summary>Whether the caster's own splash can damage them (default false; fields have their own rule).</summary>
        public bool SplashHitsCaster { get => _splashHitsCaster; set => _splashHitsCaster = Set(value); }

        /// <summary>
        /// Solid cover between the impact and a target stops its splash (SPL-168 "cover blocks blast"; neither sound nor
        /// reveal grants hits through solid cover, spec L120). Default true.
        /// </summary>
        public bool SplashBlockedByCover { get => _splashBlockedByCover; set => _splashBlockedByCover = Set(value); }

        /// <summary>Total damage one target may take from this cast ("max 40"), 0 = no cap.</summary>
        public float MaxPerTarget { get => _maxPerTarget; set => _maxPerTarget = Set(value); }

        /// <summary>Direct hits a frontal guard can reduce: weapon and physical projectile damage (spec L104).</summary>
        public bool GuardEligible { get => _guardEligible; set => _guardEligible = Set(value); }

        /// <summary>Stated weak-point multiplier for precision-tagged spells, at most 1.25 (spec L110); 1 = none.</summary>
        public float PrecisionMultiplier { get => _precisionMultiplier; set => _precisionMultiplier = Set(value); }

        /// <summary>Push (positive) distance in meters, at most 4 m per effect (spec L173).</summary>
        public float Push { get => _push; set => _push = Set(value); }

        public PushDirection PushDirection { get => _pushDirection; set => _pushDirection = Set(value); }

        /// <summary>Durability damage a direct impact deals to destructible solids (spec L171); default the direct damage.</summary>
        public float SolidDamage { get => _solidDamage < 0f ? _direct : _solidDamage; set => _solidDamage = Set(value); }

        public IReadOnlyList<StatusSpec> Statuses => _statuses;

        public IReadOnlyList<DepositSpec> Deposits => _deposits;

        public bool IsDamaging => Direct > 0f || Splash > 0f;

        public bool AppliesHardControl
        {
            get
            {
                for (int i = 0; i < _statuses.Count; i++)
                    if (_statuses[i].IsHardControl) return true;
                return false;
            }
        }

        public StatusSpec AddStatus(StatusKind kind, float durationSec, float intensity = 0f, StatusTrigger trigger = StatusTrigger.AnyHit)
        {
            Set(0);
            var s = new StatusSpec { Kind = kind, DurationSec = durationSec, Intensity = intensity, Trigger = trigger, Channel = Channel };
            _statuses.Add(s);
            return s;
        }

        public DepositSpec AddDeposit(DepositKind kind, int strength, DepositPlacement placement, float radius = 0f, float lifetimeSec = 0f)
        {
            Set(0);
            var d = new DepositSpec { Kind = kind, Strength = strength, Placement = placement, Radius = radius, LifetimeSec = lifetimeSec };
            _deposits.Add(d);
            return d;
        }

        public void ClearStatuses()
        {
            Set(0);
            _statuses.Clear();
        }

        public void ClearDeposits()
        {
            Set(0);
            _deposits.Clear();
        }

        internal void ApplyGlobalCaps()
        {
            _push = Math.Max(0f, Math.Min(_push, GlobalRules.Terrain.PlayerKnockbackCap)); // spec L173
            _precisionMultiplier = Math.Max(1f, Math.Min(_precisionMultiplier, GlobalRules.Damage.PrecisionMultiplierMax)); // spec L110
            _splitFraction = Math.Max(0f, Math.Min(_splitFraction, 1f));
            for (int i = 0; i < _statuses.Count; i++) _statuses[i].ApplyGlobalCaps();
            for (int i = 0; i < _deposits.Count; i++) _deposits[i].ApplyGlobalCaps();
        }

        protected override void OnFreeze()
        {
            for (int i = 0; i < _statuses.Count; i++) _statuses[i].Freeze();
            for (int i = 0; i < _deposits.Count; i++) _deposits[i].Freeze();
        }
    }
}
