using System;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Spells
{
    /// <summary>How a target was damaged by a cast, for exclusivity rules (spec L525).</summary>
    [Flags]
    public enum CastHitKinds
    {
        None = 0,
        Direct = 1 << 0,
        Splash = 1 << 1,
        Area = 1 << 2,
    }

    /// <summary>
    /// One committed cast while its effects live: the resolved form, the cast identity every hit carries, the single
    /// mastery result applied once (spec L508: "a source spell and all its delayed/split portions receive one
    /// casting-mastery result"), the aim at commitment, and per-target accounting for direct/splash exclusivity and
    /// payload caps (spec L525). Recasts reuse the same record (spec L94).
    /// </summary>
    public sealed class CastRecord
    {
        private readonly CastHitKinds[] _hitKinds = new CastHitKinds[DuelWorld.PlayerCount];
        private readonly float[] _requested = new float[DuelWorld.PlayerCount];

        /// <summary>Created by the runtime at commitment (public for tools and tests that resolve payloads directly).</summary>
        public CastRecord(SpellForm form, int caster, int slot, CastEventId castId, MasteryResult mastery, float linkedAttribute,
            int commitTick, int originEventId, Vec3 aimOrigin, Vec3 aimDirection)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            Caster = caster;
            Slot = slot;
            CastId = castId;
            Mastery = mastery;
            CommitTick = commitTick;
            OriginEventId = originEventId;
            CommitOrigin = aimOrigin;
            CommitDirection = aimDirection;
            ReleaseTick = -1;

            // Mastery and same-category linked bonuses clamp together at 15% (spec L124, L480).
            float applied = MasteryClamp.Combined(mastery, linkedAttribute);
            DirectDamageMultiplier = mastery.Reward == MasteryReward.DirectDamage ? 1f + applied : 1f;
            ProjectileSpeedMultiplier = mastery.Reward == MasteryReward.ProjectileSpeed ? 1f + applied : 1f;
        }

        public SpellForm Form { get; }
        public string SpellId => Form.SpellId;
        public int Caster { get; }

        /// <summary>Equipped slot 0-5, or -1 for the signature.</summary>
        public int Slot { get; }

        public CastEventId CastId { get; }
        public MasteryResult Mastery { get; }
        public int CommitTick { get; }

        /// <summary>Release tick (the effect spawned), or -1 before release.</summary>
        public int ReleaseTick { get; internal set; }

        /// <summary>Event id of the commitment: the original event every hit and proc of this cast shares (spec L126).</summary>
        public int OriginEventId { get; }

        public Vec3 CommitOrigin { get; }
        public Vec3 CommitDirection { get; }

        /// <summary>Multiplier on the primary payload's direct damage only (spec L480).</summary>
        public float DirectDamageMultiplier { get; }

        public float ProjectileSpeedMultiplier { get; }

        /// <summary>Live delivery entities spawned by this cast (projectiles, fields, deployables, summons...).</summary>
        public int LiveEntities { get; internal set; }

        /// <summary>The release resolved or spawned its effect; with no live entities left the effect has ended.</summary>
        public bool Released => ReleaseTick >= 0;

        public bool EffectEnded => Released && LiveEntities == 0;

        /// <summary>Kinds of damage this cast has dealt to a player.</summary>
        public CastHitKinds HitKindsOn(int player) => player >= 0 && player < _hitKinds.Length ? _hitKinds[player] : CastHitKinds.None;

        public bool HitDirectly(int player) => (HitKindsOn(player) & CastHitKinds.Direct) != 0;

        /// <summary>Damage this cast has requested against a player (before mitigation), for "max N per target" caps.</summary>
        public float RequestedOn(int player) => player >= 0 && player < _requested.Length ? _requested[player] : 0f;

        internal void Record(int player, CastHitKinds kind, float requested)
        {
            if (player < 0 || player >= _hitKinds.Length) return;
            _hitKinds[player] |= kind;
            _requested[player] += Math.Max(0f, requested);
        }

        public void Hash(ref StateHasher h)
        {
            h.Add((ulong)CastId.Packed);
            h.Add(Form.SpellId);
            h.Add(ReleaseTick);
            h.Add(LiveEntities);
            for (int i = 0; i < _hitKinds.Length; i++)
            {
                h.Add((int)_hitKinds[i]);
                h.Add(_requested[i]);
            }
        }

        public override string ToString() => CastId + " " + SpellId;
    }
}
