using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-307 Sweep Wake (Silver, SPL-010, exclusive group SPL-010-delivery). Rank 1: "Sweep leaves 1 m-wide neutral Heat
    /// 1 strip for 2 s, 4 damage/s." Rank 2: "Strip lasts 3 s and 6/s." Drawback: "Direct 36 becomes 30; field allocation
    /// required." Counterplay: "Step out or cool strip."
    /// <list type="bullet">
    /// <item>The cone hits for 30. Its wake is a 1 m-wide strip along the chord the sweep traces between the cone's two
    /// edges at full reach (it never lies under the caster), Heat 1 per covered cell for its lifetime.</item>
    /// <item>The strip is one of the caster's three fields (structural delta: the spell gains the field tag, so it is a
    /// major field cast, spec L92, L128) and neutral: 4 thermal per second (rank 2: 6 for 3 s) to anyone on a hot cell,
    /// owner included (spec L496). Water or frost that consumes its heat cools it.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SweepWake : ISpellTransform
    {
        public const string Id = "AUG-307";

        public static readonly ProseParameter Width = ProseParameter.Of("strip_width", 1, "Sweep leaves 1 m-wide");
        public static readonly ProseParameter Heat = ProseParameter.Of("strip_heat", 1, "neutral Heat 1 strip");
        public static readonly ProseParameter Rank1Lifetime = ProseParameter.Of("rank1_strip_lifetime", 2, "strip for 2 s");
        public static readonly ProseParameter Rank1Dps = ProseParameter.Of("rank1_strip_dps", 4, "4 damage/s");
        public static readonly ProseParameter Rank2Lifetime = ProseParameter.Of("rank2_strip_lifetime", 3, "Strip lasts 3 s");
        public static readonly ProseParameter Rank2Dps = ProseParameter.Of("rank2_strip_dps", 6, "3 s and 6/s");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 30, "Direct 36 becomes 30");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Width, Heat, Rank1Lifetime, Rank1Dps, Rank2Lifetime, Rank2Dps, Direct };

        static SweepWake() => Wiz002StructuralDeltas.EnsureRegistered();

        public SweepWake() => Wiz002StructuralDeltas.EnsureRegistered();

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is ConeDelivery)) throw new InvalidOperationException(Id + " transforms a cone delivery (SPL-010).");
            form.Payload.Direct = Direct.Float;
            var contact = new PayloadSpec { Channel = form.Channel, Direct = rank >= 2 ? Rank2Dps.Float : Rank1Dps.Float, GuardEligible = false };
            form.Script = new SweepWakeScript(Width.Float, (int)Heat.Value, rank >= 2 ? Rank2Lifetime.Float : Rank1Lifetime.Float, contact);
        }
    }

    /// <summary>Lays Sweep Wake's strip when the cone resolves (see <see cref="SweepWake"/>).</summary>
    public sealed class SweepWakeScript : SpellScript
    {
        internal SweepWakeScript(float width, int heat, float lifetimeSec, PayloadSpec contact)
        {
            Width = width;
            Heat = heat;
            LifetimeSec = lifetimeSec;
            Contact = contact;
        }

        public float Width { get; }
        public int Heat { get; }
        public float LifetimeSec { get; }
        public PayloadSpec Contact { get; }

        /// <summary>The wake's footprint: the chord between the cone's edges at full reach, a strip of the given width.</summary>
        public static Footprint StripFootprint(Vec3 feet, Vec3 aim, float reach, float halfAngleDeg, float width, out Vec3 center, out float halfLength)
        {
            Vec3 dir = KitSupport.Flat(aim, Vec3.Forward);
            float along = reach * (float)Math.Cos(CMath.DegToRad(halfAngleDeg));
            halfLength = reach * (float)Math.Sin(CMath.DegToRad(halfAngleDeg));
            center = feet + dir * Math.Max(width * 0.5f, along);
            return Footprint.Rectangle(center.XZ(), new Vec2(halfLength, width * 0.5f), CMath.YawOf(dir));
        }

        public override void OnReleased(SpellContext ctx, CastRecord cast)
        {
            if (!(cast.Form.Delivery is ConeDelivery cone)) return;
            DuelPlayer p = ctx.Player(cast.Caster);
            Vec3 aim = cast.Form.AimLock == AimLock.AtCommit ? cast.CommitDirection : ctx.AimDirection(cast.Caster);
            Footprint strip = StripFootprint(p.Position, aim, cone.ReachFor(cast.Form), cone.HalfAngleDeg, Width, out Vec3 center, out float halfLength);
            float bound = (float)Math.Sqrt(halfLength * halfLength + Width * Width * 0.25f);
            HeatStripEntity.Create(ctx, cast, strip, center, bound, Heat, LifetimeSec, Contact);
        }
    }

    /// <summary>
    /// AUG-308 Forked Sweep (Prismatic, SPL-010, exclusive group SPL-010-delivery). Rank 1: "Cone becomes two 45-degree
    /// cones with a 30-degree center gap, damage 36 once." Rank 2: "Choose gap direction before release, damage 36
    /// unchanged." Drawback: "Reach 4 becomes 3.5 m; cast_time+0.1 s." Counterplay: "Stand in announced gap or flank."
    /// <list type="bullet">
    /// <item>Two 45-degree lobes either side of a 30-degree gap along the aim, 3.5 m reach, 0.35 s windup; a target is
    /// struck at most once for 36 (per-target cover and frontal guard as SPL-010); Heat 1 over both lobes.</item>
    /// <item>The gap is announced at commitment (a marker along its direction for the windup), and the fork's direction
    /// is fixed then.</item>
    /// <item>Rank 2: holding Modifier A or Modifier B when the cast commits turns the gap 37.5 degrees to the left or right
    /// (half a gap plus half a lobe), so a lobe rather than the gap lies on the aim; neither keeps it centered.</item>
    /// </list>
    /// No structural delta: it stays a cone.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ForkedSweep : ISpellTransform
    {
        public const string Id = "AUG-308";

        public static readonly ProseParameter LobeAngle = ProseParameter.HalfAngle("lobe_half_angle", 45, "two 45-degree cones");
        public static readonly ProseParameter GapAngle = ProseParameter.HalfAngle("gap_half_angle", 30, "a 30-degree center gap");
        public static readonly ProseParameter Damage = ProseParameter.Of("direct", 36, "damage 36 once");
        public static readonly ProseParameter Reach = ProseParameter.Of("reach", 3.5, "Reach 4 becomes 3.5 m");
        public static readonly ProseParameter ExtraCastTime = ProseParameter.Of("extra_cast_time", 0.1, "cast_time+0.1 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { LobeAngle, GapAngle, Damage, Reach, ExtraCastTime };

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is ConeDelivery source)) throw new InvalidOperationException(Id + " transforms a cone delivery (SPL-010).");
            form.Payload.Direct = Damage.Float;
            form.Range = Reach.Float;
            form.CastTimeSec += ExtraCastTime.Float;
            form.AimLock = AimLock.AtCommit; // the announced gap is fixed when the cast commits
            form.Delivery = new ForkedConeDelivery
            {
                LobeHalfAngleDeg = LobeAngle.Float,
                GapHalfAngleDeg = GapAngle.Float,
                Reach = Reach.Float,
                CoverPerTarget = source.CoverPerTarget,
                GapChoice = rank >= 2,
            };
            form.Script = ForkScript.Instance;
        }
    }

    /// <summary>Records the AUG-308 rank 2 gap choice and announces the gap at commitment.</summary>
    internal sealed class ForkScript : SpellScript
    {
        public const int GapLeft = 1;
        public const int GapRight = 2;

        public static readonly ForkScript Instance = new ForkScript();

        public override void OnCommitted(SpellContext ctx, CastRecord cast)
        {
            if (!(cast.Form.Delivery is ForkedConeDelivery fork)) return;
            DuelPlayer p = ctx.Player(cast.Caster);
            if (fork.GapChoice)
            {
                if (p.Input.Has(InputButtons.ModifierA)) cast.Choice = GapLeft;
                else if (p.Input.Has(InputButtons.ModifierB)) cast.Choice = GapRight;
            }

            float gapYaw = fork.GapYaw(cast, p);
            ctx.Emit(SpellEventKind.Marker, cast, p.Position, CMath.ForwardFromYaw(gapYaw), 0, fork.Reach, ticks: cast.Form.Castable.WindupTicks);
        }
    }

    /// <summary>The forked cone of AUG-308: two lobes either side of a gap, one hit per target (see <see cref="ForkedSweep"/>).</summary>
    public sealed class ForkedConeDelivery : DeliverySpec
    {
        private const float MaxHeightAbove = 2.5f;

        private float _lobeHalfAngleDeg;
        private float _gapHalfAngleDeg;
        private float _reach;
        private bool _coverPerTarget = true;
        private bool _gapChoice;

        public override DeliveryKind Kind => DeliveryKind.Cone;

        public float LobeHalfAngleDeg { get => _lobeHalfAngleDeg; set => _lobeHalfAngleDeg = Set(value); }
        public float GapHalfAngleDeg { get => _gapHalfAngleDeg; set => _gapHalfAngleDeg = Set(value); }
        public float Reach { get => _reach; set => _reach = Set(value); }
        public bool CoverPerTarget { get => _coverPerTarget; set => _coverPerTarget = Set(value); }

        /// <summary>Rank 2: the gap direction may be chosen at commitment.</summary>
        public bool GapChoice { get => _gapChoice; set => _gapChoice = Set(value); }

        /// <summary>Degrees between the gap's center and a lobe's center.</summary>
        public float LobeOffsetDeg => GapHalfAngleDeg + LobeHalfAngleDeg;

        internal override void ApplyGlobalCaps()
        {
            _lobeHalfAngleDeg = CMath.Clamp(_lobeHalfAngleDeg, 0f, 90f);
            _gapHalfAngleDeg = CMath.Clamp(_gapHalfAngleDeg, 0f, 90f);
            _reach = Math.Max(0f, _reach);
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.Player(caster).Position;

        /// <summary>The gap's yaw for a cast: the committed aim, turned by a rank 2 choice.</summary>
        public float GapYaw(CastRecord cast, DuelPlayer caster)
        {
            Vec3 dir = KitSupport.Flat(cast.CommitDirection, CMath.ForwardFromYaw(caster.Motor.YawDeg));
            float yaw = CMath.YawOf(dir);
            if (!GapChoice) return yaw;
            if (cast.Choice == ForkScript.GapLeft) return CMath.WrapAngle(yaw - LobeOffsetDeg);
            if (cast.Choice == ForkScript.GapRight) return CMath.WrapAngle(yaw + LobeOffsetDeg);
            return yaw;
        }

        /// <summary>Whether a body lies in a lobe centered on a yaw.</summary>
        public bool InLobe(Vec3 apexFeet, float lobeYawDeg, Capsule body)
        {
            Vec3 center = body.Center;
            var flat = new Vec3(center.X - apexFeet.X, 0f, center.Z - apexFeet.Z);
            float distance = flat.Length();
            if (distance - body.Radius > Reach) return false;
            float bottom = body.A.Y - body.Radius;
            if (bottom > apexFeet.Y + MaxHeightAbove || body.B.Y + body.Radius < apexFeet.Y - 0.5f) return false;
            if (distance <= body.Radius) return true;
            float angular = CMath.RadToDeg((float)Math.Asin(Math.Min(1.0, body.Radius / distance)));
            return Math.Abs(CMath.DeltaAngle(lobeYawDeg, CMath.YawOf(flat))) <= LobeHalfAngleDeg + angular;
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            int caster = cast.Caster;
            DuelPlayer p = ctx.Player(caster);
            Vec3 feet = p.Position;
            Vec3 focus = ctx.FocusOrigin(caster);
            float gap = GapYaw(cast, p);
            float left = CMath.WrapAngle(gap - LobeOffsetDeg);
            float right = CMath.WrapAngle(gap + LobeOffsetDeg);
            ctx.Emit(SpellEventKind.ConeReleased, cast, feet, CMath.ForwardFromYaw(gap), 0, Reach);
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == caster || !ctx.IsTargetable(i)) continue;
                Capsule body = ctx.TargetVolume(caster, i, true);
                if (!InLobe(feet, left, body) && !InLobe(feet, right, body)) continue; // "damage 36 once"
                if (CoverPerTarget && ctx.IsCovered(focus, body)) continue;
                Vec3 toTarget = body.Center - focus;
                ctx.ApplyToPlayer(cast, cast.Form.Payload, i, PayloadHit.Direct(focus, new Vec3(toTarget.X, 0f, toTarget.Z)));
            }

            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(caster);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                var sphere = new Capsule(b.BodyCenter, b.BodyCenter, b.BodyRadius);
                if ((InLobe(feet, left, sphere) || InLobe(feet, right, sphere)) && (!CoverPerTarget || ctx.HasClearPath(focus, b.BodyCenter)))
                {
                    ctx.ApplyToBody(cast, cast.Form.Payload, b, CastHitKinds.Direct);
                }
            }

            // The record's Heat 1 along the cone covers both lobes (spec L163).
            ctx.EmitDeposits(cast, cast.Form.Payload, DepositPlacement.Footprint, feet, CMath.ForwardFromYaw(left),
                Footprint.Cone(feet.XZ(), CMath.ForwardFromYaw(left).XZ(), Reach, LobeHalfAngleDeg));
            ctx.EmitDeposits(cast, cast.Form.Payload, DepositPlacement.Footprint, feet, CMath.ForwardFromYaw(right),
                Footprint.Cone(feet.XZ(), CMath.ForwardFromYaw(right).XZ(), Reach, LobeHalfAngleDeg));
        }
    }
}
