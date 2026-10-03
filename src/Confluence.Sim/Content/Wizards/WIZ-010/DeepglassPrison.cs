using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-079 Deepglass Prison (WIZ-010, brief containment): "22 m/s bubble projectile roots a hit target for 0.6 s; then
    /// applies 20% slow for 1.5 s." Damage "26", physical; world interaction "Water 2 at impact; root obeys global
    /// immunity; cannot float or imprison indefinitely." Mana 32, cooldown 17 s, 0.65 s windup, 18 m range (spec L525).
    /// <list type="bullet">
    /// <item>A direct hit roots for 0.6 s under the hard-control rules (spec L116): a target with control immunity takes
    /// the damage but no root, and nothing is queued; after the root the 2 s immunity starts. Root only stops movement
    /// (D-134): the target is never lifted ("cannot float") and the root never exceeds the 0.75 s cap.</item>
    /// <item>"then applies 20% slow for 1.5 s": the slow starts when the root ends (at once when immunity refused the root),
    /// inside the 30% and 3 s slow caps.</item>
    /// <item>A hard-control spell: the anticipatory cue of spec L96 telegraphs the aimed point before release (counterplay
    /// "Dodge the bubble").</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class DeepglassPrison : ISpellBehavior
    {
        public const string Id = "SPL-079";

        public static readonly ProseParameter Speed = ProseParameter.Of("speed", 22, "22 m/s bubble projectile");
        public static readonly ProseParameter Root = ProseParameter.Of("root", 0.6, "roots a hit target for 0.6 s");
        public static readonly ProseParameter Slow = ProseParameter.Percent("slow", 20, "applies 20% slow for 1.5 s");
        public static readonly ProseParameter SlowDuration = ProseParameter.Of("slow_duration", 1.5, "applies 20% slow for 1.5 s");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 26, "26");
        public static readonly ProseParameter ImpactWater = ProseParameter.Of("impact_water", 2, "Water 2 at impact");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Speed, Root, Slow, SlowDuration, Direct, ImpactWater };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new TideProjectileDelivery { Speed = Speed.Float, MaxRange = form.Range };

            PayloadSpec payload = form.Payload;
            payload.Direct = Direct.Float;
            payload.AddStatus(StatusKind.Root, Root.Float, 0f, StatusTrigger.DirectHitOnly);
            payload.AddDeposit(DepositKind.Water, (int)ImpactWater.Value, DepositPlacement.Impact, 0f, HalenRules.ImpactWaterLifetimeSec);
            form.Script = new DeepglassPrisonScript { SlowFraction = Slow.Float, SlowSec = SlowDuration.Float };
        }
    }

    /// <summary>SPL-079's after-root rules: the slow (or AUG-446's puddle) once the root ends.</summary>
    public sealed class DeepglassPrisonScript : SpellScript
    {
        /// <summary>Slow applied when the root ends; 0 = none (AUG-446 "Post-root slow removed").</summary>
        public float SlowFraction { get; set; }

        public float SlowSec { get; set; }

        /// <summary>AUG-446: a puddle at the impact once the root ends; 0 = none.</summary>
        public float PuddleRadius { get; set; }

        public float PuddleLifetimeSec { get; set; }

        public int PuddleWater { get; set; } = 1;

        public override void OnPlayerHit(SpellContext ctx, CastRecord cast, int target, DamageEvent hit)
        {
            if (hit.Kind != DamageKind.Direct) return;
            DuelPlayer victim = ctx.Player(target);
            StatusInstance root = victim.Statuses.Find(StatusKind.Root, cast.SpellId);
            bool rooted = root != null && root.AppliedTick == ctx.Tick && root.SourcePlayer == cast.Caster;
            int endTick = rooted ? root.ExpireTick : ctx.Tick;
            ctx.Spawn(new PrisonFollowUpEntity(cast, this, target, victim.Position, rooted ? root.Id : -1, endTick));
        }
    }

    /// <summary>Waits for the SPL-079 root to end (expiry, dispel, or death) and then runs the after-root rule once.</summary>
    public sealed class PrisonFollowUpEntity : SpellEntity
    {
        private readonly DeepglassPrisonScript _script;
        private readonly int _target;
        private readonly Vec3 _impact;
        private readonly int _rootId;
        private readonly int _endTick;

        internal PrisonFollowUpEntity(CastRecord cast, DeepglassPrisonScript script, int target, Vec3 impact, int rootId, int endTick)
            : base(cast)
        {
            _script = script;
            _target = target;
            _impact = impact;
            _rootId = rootId;
            _endTick = endTick;
        }

        public int Target => _target;
        public bool RootApplied => _rootId >= 0;

        protected internal override void Step(SpellContext ctx)
        {
            DuelPlayer victim = ctx.Player(_target);
            if (_rootId >= 0 && ctx.Tick < _endTick && RootStillActive(victim)) return;
            Fire(ctx, victim);
            End(ctx);
        }

        private bool RootStillActive(DuelPlayer victim)
        {
            IReadOnlyList<StatusInstance> active = victim.Statuses.Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Id == _rootId) return true;
            }

            return false;
        }

        private void Fire(SpellContext ctx, DuelPlayer victim)
        {
            if (_script.SlowFraction > 0f && _script.SlowSec > 0f && victim.Alive)
            {
                StatusApplication slow = StatusApplication.Of(StatusKind.Slow, Cast.SpellId, Cast.Caster, Ticks.FromSeconds(_script.SlowSec), _script.SlowFraction);
                slow.Channel = Cast.Form.Channel;
                slow.OriginEventId = Cast.OriginEventId;
                ctx.World.ApplyStatus(_target, slow);
            }

            if (_script.PuddleRadius > 0f)
            {
                HalenRules.DepositWater(ctx, Cast, _impact, _script.PuddleWater, _script.PuddleRadius, _script.PuddleLifetimeSec);
            }

            HalenRules.Emit(ctx, TideEventKind.PrisonReleased, Cast, _impact, RootApplied ? 1f : 0f, _target);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_target);
            h.Add(_impact);
            h.Add(_rootId);
            h.Add(_endTick);
        }
    }
}
