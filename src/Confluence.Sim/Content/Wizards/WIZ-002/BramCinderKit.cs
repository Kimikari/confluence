using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// WIZ-002 Bram Cinder, the Kilnheart: the crest-release grammar (Confluence.Casting <c>PressureReleaseGrammar</c>,
    /// D-056), the signature technique and the passive. The eight exclusive spells are their own behaviors (SPL-009 ...
    /// SPL-016) and the sixteen transformations AUG-305 ... AUG-320 theirs.
    /// <para>
    /// Signature, "Kiln Vent — 14 s cooldown, 25 mana, 0.35 s windup. Project a visible 4 m cone that deals 28 thermal
    /// damage and pushes enemies 1.5 m. It contributes Heat strength 1 to affected world props; it does not create a
    /// persistent fire field." The cone's angle is unstated; it uses the cone primitive's default 90 degrees (Bram's own
    /// cone language, SPL-010). Pushing enemies is hostile displacement, which is hard control (spec L116), so the vent
    /// always shows the 0.25 s anticipatory cue (spec L96) and is blocked by control immunity (its damage still lands).
    /// Its hits are direct cone hits a frontal guard reduces, push included (spec L104). Props whose body lies in the
    /// cone receive one heat unit on the cells under them as instant residue; the cone itself leaves no deposit.
    /// </para>
    /// <para>
    /// Passive, "Tempered Hands — taking thermal damage grants a visible 20-point shield for 2 s, once per 8 s. It
    /// provides no thermal immunity and respects the shared shield cap." A DamageTaken effect filtered to the thermal
    /// channel with an 8 s internal cooldown, attached through the effect framework (spec L472, L126 budgets): the
    /// shield arrives after the triggering hit resolved (it never absorbs that hit), is visible as a shield event, lasts
    /// 2 s and shares the 100 cap (spec L114). Reaction and other secondary damage raise no triggers (spec L126, L672),
    /// and Bram's own thermal damage to himself does not charge it (no reward from self-damage, spec L114, L651).
    /// </para>
    /// </summary>
    [ContentBehavior(KitSupport.WizardId)]
    public sealed class BramCinderKit : IWizardKitBehavior
    {
        public static readonly ProseParameter SignatureCooldown = ProseParameter.Of("signature_cooldown", 14, "Kiln Vent — 14 s cooldown");
        public static readonly ProseParameter SignatureMana = ProseParameter.Of("signature_mana", 25, "25 mana");
        public static readonly ProseParameter SignatureWindup = ProseParameter.Of("signature_windup", 0.35, "0.35 s windup");
        public static readonly ProseParameter ConeReach = ProseParameter.Of("cone_reach", 4, "Project a visible 4 m cone");
        public static readonly ProseParameter ConeDamage = ProseParameter.Of("cone_damage", 28, "deals 28 thermal damage");
        public static readonly ProseParameter ConePush = ProseParameter.Of("cone_push", 1.5, "pushes enemies 1.5 m");
        public static readonly ProseParameter PropHeat = ProseParameter.Of("prop_heat", 1, "Heat strength 1 to affected world props");
        public static readonly ProseParameter PassiveShield = ProseParameter.Of("passive_shield", 20, "grants a visible 20-point shield for 2 s");
        public static readonly ProseParameter PassiveDuration = ProseParameter.Of("passive_shield_duration", 2, "20-point shield for 2 s");
        public static readonly ProseParameter PassiveCooldown = ProseParameter.Of("passive_cooldown", 8, "once per 8 s");

        public const float ConeHalfAngleDeg = 45f; // unstated: the cone primitive's default 90-degree cone

        private static readonly IReadOnlyList<ProseParameter> All = new[]
        {
            SignatureCooldown, SignatureMana, SignatureWindup, ConeReach, ConeDamage, ConePush, PropHeat, PassiveShield, PassiveDuration, PassiveCooldown,
        };

        /// <summary>The passive's label in the build inspector.</summary>
        public const string PassiveLabel = "Tempered Hands";

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void BuildSignature(SpellFormBuilder form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            form.Mana = SignatureMana.Float;
            form.CooldownSec = SignatureCooldown.Float;
            form.CastTimeSec = SignatureWindup.Float;
            form.Range = ConeReach.Float;
            form.Channel = DamageChannel.Thermal;
            form.Tags.AddRange(new[] { "cone", "damaging", "channel:thermal", "close", "heat", "school:heat" });
            PayloadSpec payload = form.Payload;
            payload.Channel = DamageChannel.Thermal;
            payload.Direct = ConeDamage.Float;
            payload.Push = ConePush.Float;
            payload.PushDirection = PushDirection.AwayFromSource;
            form.Delivery = new KilnVentDelivery { Reach = ConeReach.Float, HalfAngleDeg = ConeHalfAngleDeg, PropHeat = (int)PropHeat.Value };
        }

        public void AttachPassive(IEffectHost host)
        {
            ContentArgs.Require(host);
            var temperedHands = new Effect(KitSupport.WizardId, EffectTrigger.DamageTaken)
            {
                Label = PassiveLabel,
                InternalCooldownTicks = Ticks.FromSeconds(PassiveCooldown.Value),
                Condition = FromAnotherSource.Instance,
            };
            temperedHands.Filter.Channel = DamageChannel.Thermal;
            temperedHands.Actions.Add(new GrantShieldAction(PassiveShield.Float, Ticks.FromSeconds(PassiveDuration.Value)));
            host.Attach(temperedHands);
        }

        /// <summary>The thermal damage came from someone other than the wizard (no reward for self-damage).</summary>
        private sealed class FromAnotherSource : IEffectCondition
        {
            public static readonly FromAnotherSource Instance = new FromAnotherSource();

            public string Describe => "damage from another source";

            public bool Holds(in EffectContext context) => context.Trigger.Other != context.Host.Player;
        }
    }

    /// <summary>
    /// The Kiln Vent cone (see <see cref="BramCinderKit"/>): resolved at the end of its windup from the caster's feet
    /// along the aim; per-target cover from the focus; lag-compensated like other instant cones (spec L423).
    /// </summary>
    public sealed class KilnVentDelivery : DeliverySpec
    {
        private const float MaxHeightAbove = 2.5f;

        private float _reach;
        private float _halfAngleDeg;
        private int _propHeat;

        public override DeliveryKind Kind => DeliveryKind.Cone;

        public float Reach { get => _reach; set => _reach = Set(value); }
        public float HalfAngleDeg { get => _halfAngleDeg; set => _halfAngleDeg = Set(value); }

        /// <summary>Heat units each affected prop contributes to the cells under it.</summary>
        public int PropHeat { get => _propHeat; set => _propHeat = Set(value); }

        /// <summary>Pushing enemies is hostile displacement, hard control: the 0.25 s cue always shows (spec L96, L116).</summary>
        public override bool RequiresCue => true;

        internal override void ApplyGlobalCaps()
        {
            _halfAngleDeg = CMath.Clamp(_halfAngleDeg, 0f, 180f);
            _reach = Math.Max(0f, _reach);
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.Player(caster).Position;

        /// <summary>Whether a body lies in the cone from the apex (feet plane, horizontal angle, reach, height band).</summary>
        public bool Contains(Vec3 apexFeet, float facingYawDeg, Capsule body)
        {
            Vec3 center = body.Center;
            var flat = new Vec3(center.X - apexFeet.X, 0f, center.Z - apexFeet.Z);
            float distance = flat.Length();
            if (distance - body.Radius > Reach) return false;
            float bottom = body.A.Y - body.Radius;
            if (bottom > apexFeet.Y + MaxHeightAbove || body.B.Y + body.Radius < apexFeet.Y - 0.5f) return false;
            if (distance <= body.Radius) return true;
            float angular = CMath.RadToDeg((float)Math.Asin(Math.Min(1.0, body.Radius / distance)));
            return Math.Abs(CMath.DeltaAngle(facingYawDeg, CMath.YawOf(flat))) <= HalfAngleDeg + angular;
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            int caster = cast.Caster;
            DuelPlayer p = ctx.Player(caster);
            Vec3 feet = p.Position;
            Vec3 focus = ctx.FocusOrigin(caster);
            Vec3 dir = KitSupport.Flat(AimDirection(ctx, cast), CMath.ForwardFromYaw(p.Motor.YawDeg));
            float yaw = CMath.YawOf(dir);
            ctx.Emit(SpellEventKind.ConeReleased, cast, feet, dir, 0, Reach);

            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == caster || !ctx.IsTargetable(i)) continue;
                Capsule body = ctx.TargetVolume(caster, i, true);
                if (!Contains(feet, yaw, body) || ctx.IsCovered(focus, body)) continue;
                Vec3 toTarget = body.Center - focus;
                ctx.ApplyToPlayer(cast, cast.Form.Payload, i, PayloadHit.Direct(focus, new Vec3(toTarget.X, 0f, toTarget.Z)));
            }

            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(caster);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                if (Contains(feet, yaw, new Capsule(b.BodyCenter, b.BodyCenter, b.BodyRadius)) && ctx.HasClearPath(focus, b.BodyCenter))
                {
                    ctx.ApplyToBody(cast, cast.Form.Payload, b, CastHitKinds.Direct);
                }
            }

            // "It contributes Heat strength 1 to affected world props": the cells under each prop in the cone; the cone
            // itself deposits nothing ("it does not create a persistent fire field").
            IReadOnlyList<PropBody> props = ctx.World.Props.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropBody prop = props[i];
                if (prop.Destroyed) continue;
                YawBox box = prop.Box;
                float radius = Math.Max(0.05f, Math.Min(box.HalfExtents.X, box.HalfExtents.Z));
                var probe = new Capsule(new Vec3(box.Center.X, box.Bottom + radius, box.Center.Z), new Vec3(box.Center.X, Math.Max(box.Bottom + radius, box.Top - radius), box.Center.Z), radius);
                if (!Contains(feet, yaw, probe)) continue;
                Footprint under = Footprint.Rectangle(box.Center.XZ(), new Vec2(box.HalfExtents.X, box.HalfExtents.Z), box.YawDeg);
                KitSupport.Deposit(ctx, cast, DepositKind.Heat, PropHeat, new Vec3(box.Center.X, box.Bottom, box.Center.Z), under);
                ctx.Emit(SpellEventKind.Heated, cast, box.Center, dir, prop.Id, 0f);
            }
        }
    }
}
