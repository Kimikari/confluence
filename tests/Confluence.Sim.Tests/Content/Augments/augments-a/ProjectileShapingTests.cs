using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>A projectile test behavior with explicit shape numbers under a real record id.</summary>
    internal sealed class ShapedBolt : ISpellBehavior
    {
        public float Direct = 30f;
        public float Speed = 30f;
        public float Radius = SpellRules.DefaultProjectileRadius;
        public float Gravity;
        public int Count = 1;
        public float LifetimeSec;

        public IReadOnlyList<ProseParameter> Parameters => Array.Empty<ProseParameter>();

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new ProjectileDelivery
            {
                Speed = Speed,
                MaxRange = Math.Max(10f, form.Range),
                Radius = Radius,
                Gravity = Gravity,
                Count = Count,
                SpreadDeg = Count > 1 ? 10f : 0f,
                LifetimeSec = LifetimeSec,
            };
            form.Payload.Direct = Direct;
            if (!form.Tags.Contains("damaging")) form.Tags.Add("damaging");
        }
    }

    /// <summary>A wizard transformation stand-in (registered under a real transformation id) that changes nothing visible.</summary>
    internal sealed class MarkerTransform : ISpellTransform
    {
        public TransformStage Stage => TransformStage.Payload;
        public IReadOnlyList<ProseParameter> Parameters => Array.Empty<ProseParameter>();
        public void Apply(SpellFormBuilder form, int rank)
        {
        }
    }

    /// <summary>A shared-augment stand-in whose hook shortens every recovery by a number of ticks (to reach a floor).</summary>
    internal sealed class RecoveryCut : IAugmentBehavior
    {
        private readonly int _ticks;

        public RecoveryCut(int ticks)
        {
            _ticks = ticks;
        }

        public void Attach(IEffectHost host, int rank) => host.Attach(new Hook(_ticks));

        private sealed class Hook : EffectHook
        {
            private readonly int _ticks;

            public Hook(int ticks) : base("AUG-TEST-RECOVERY")
            {
                _ticks = ticks;
            }

            public override void AdjustCastTiming(in CastQuote quote, ref CastTimingAdjust timing) => timing.RecoveryDeltaTicks -= _ticks;
        }
    }

    /// <summary>The "Projectile and beam shaping" shared augments on WIZ-002's Fireball (24 m/s, 48 direct, radius 0.15 m).</summary>
    public class ProjectileShapingTests
    {
        private static SpellForm Form(DuelWorld w, string spellId = "SPL-009") => w.Player(0).Caster.FormFor(spellId);

        private static ProjectileDelivery Shot(DuelWorld w, string spellId = "SPL-009") => (ProjectileDelivery)Form(w, spellId).Delivery;

        private static float LaunchSpeed(DuelWorld w, int index = 0) =>
            AKit.Events<SpellEvent>(w).Where(e => e.Kind == SpellEventKind.ProjectileLaunched && e.Player == 0).ElementAt(index).Amount;

        private static DamageEvent DirectHit(DuelWorld w) => AKit.Hits(w, 1, DamageKind.Direct).Single();

        /// <summary>Casts the selected slot with a normal release and lets it fly.</summary>
        private static void Fire(DuelWorld w, PlayerInput? aim = null, int slot = 0, int flyTicks = 60, PlayerInput? b = null)
        {
            PlayerInput input = AKit.SelectSlot(aim ?? AKit.A, slot);
            AKit.Step(w, 1, input, b);
            AKit.HoldAndRelease(w, AKit.NormalHold, input, b);
            AKit.Step(w, flyTicks, input, b);
        }

        /// <summary>Player 0 (WIZ-002) with the listed spells under test behaviors, and the augments.</summary>
        private static DuelWorld WithSpells(IEnumerable<ActiveAugment> augments, ArenaLayout layout, params KeyValuePair<string, object>[] spells)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, augments);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(spells.Select(s => s.Key));
            return AKit.Duel(a, null, layout, AKit.RegistryWith(a, spells));
        }

        // ----- AUG-025 Fast Release ------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.08f)]
        [InlineData(2, 0.12f)]
        [Verifies("AUG-025")]
        public void Fast_Release_Speeds_A_Straight_Projectile_And_Shortens_Its_Travel(int rank, float bonus)
        {
            AKit.AssertQuotes(FastRelease.Id, "Cast a straight direct projectile.", "Projectile speed increases 8%.", "Projectile speed increases 12%.", "Projectile lifetime falls 8%, reducing maximum travel range.");
            DuelWorld w = AKit.With(FastRelease.Id, rank);
            Assert.Equal(24f * (1f + bonus), Shot(w).Speed, 3);
            Assert.Equal(30f * 0.92f, Shot(w).MaxRange, 3);
            AKit.FaceOff(w, 6f);
            Fire(w);
            Assert.Equal(24f * (1f + bonus), LaunchSpeed(w), 3);
            Assert.Equal(48f, DirectHit(w).Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-025")]
        public void Fast_Release_Drawback_A_Target_At_The_End_Of_Fireball_Range_Is_Out_Of_Reach()
        {
            foreach (bool withAugment in new[] { false, true })
            {
                DuelWorld w = withAugment ? AKit.With(FastRelease.Id, 2) : AKit.Without();
                TestWorlds.Place(w, 0, new Vec3(0f, 0f, -14f), 0f);
                TestWorlds.Place(w, 1, new Vec3(0f, 0f, 15f), 180f); // body surface 28.6 m away: inside 30 m, beyond 27.6 m
                Fire(w, flyTicks: 120);
                Assert.Equal(withAugment ? 0 : 1, AKit.Hits(w, 1, DamageKind.Direct).Count);
            }
        }

        [Fact]
        [Verifies("AUG-025")]
        public void Fast_Release_Leaves_Lobbed_Projectiles_And_Cones_Unchanged_And_Shortens_A_Lifetime()
        {
            DuelWorld w = WithSpells(AKit.Aug(FastRelease.Id, 2), null,
                AKit.Spell("SPL-015", new ShapedBolt { Speed = 20f, Gravity = 9f }),
                AKit.Spell("SPL-016", new ShapedBolt { Speed = 20f, LifetimeSec = 2f }));
            Assert.Equal(20f, Shot(w, "SPL-015").Speed, 3); // a lob is not a straight projectile
            Assert.Equal(20f * 1.12f, Shot(w, "SPL-016").Speed, 3);
            Assert.Equal(2f * 0.92f, Shot(w, "SPL-016").LifetimeSec, 3);

            DuelWorld bram = AKit.With(FastRelease.Id, 2);
            Assert.IsNotType<ProjectileDelivery>(Form(bram, "SPL-010").Delivery); // Cinder Sweep is a cone
            Assert.Equal(36f, Form(bram, "SPL-010").Payload.Direct, 3);
        }

        // ----- AUG-026 Heavy Core --------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-026")]
        public void Heavy_Core_Raises_Direct_Damage_And_Slows_The_Projectile(int rank, float bonus)
        {
            AKit.AssertQuotes(HeavyCore.Id, "Cast a direct projectile.", "Direct damage increases 5%.", "Direct damage increases 8%.", "Projectile speed decreases 8%.");
            DuelWorld w = AKit.With(HeavyCore.Id, rank);
            AKit.FaceOff(w, 6f);
            Fire(w);
            Assert.Equal(48f * (1f + bonus), DirectHit(w).Resolution.Raw, 3);
            Assert.Equal(24f * 0.92f, LaunchSpeed(w), 3);
            Assert.Equal(28f, Form(w).Payload.Splash, 3); // splash is not direct damage
        }

        [Fact]
        [Verifies("AUG-026")]
        public void Heavy_Core_Applies_Before_Mastery_And_Ignores_Cones()
        {
            DuelWorld w = AKit.With(HeavyCore.Id, 2);
            AKit.FaceOff(w, 6f);
            AKit.HoldAndRelease(w, AKit.CrestHold); // the crest reward (+12%) multiplies the augmented structure (spec L543)
            AKit.Step(w, 60);
            Assert.Equal(48f * 1.08f * 1.12f, DirectHit(w).Resolution.Raw, 2);
            Assert.Equal(36f, Form(w, "SPL-010").Payload.Direct, 3);
        }

        // ----- AUG-027 Broad Tip ---------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.02f)]
        [InlineData(2, 0.04f)]
        [Verifies("AUG-027")]
        public void Broad_Tip_Widens_A_Narrow_Single_Projectile_At_A_Damage_Cost(int rank, float growth)
        {
            AKit.AssertQuotes(BroadTip.Id, "single direct projectile with radius below 0.3 m", "Radius increases 0.02 m.", "Radius increases 0.04 m.", "Direct damage decreases 5%; never enlarges beams or fields.");
            DuelWorld w = AKit.With(BroadTip.Id, rank);
            Assert.Equal(0.15f + growth, Shot(w).Radius, 4);

            // A shot passing 0.4 + 0.15 + growth/2 from the enemy's axis grazes the wider projectile only.
            float offset = GlobalRules.Movement.CapsuleRadius + 0.15f + growth * 0.5f;
            foreach (bool withAugment in new[] { false, true })
            {
                DuelWorld g = withAugment ? AKit.With(BroadTip.Id, rank) : AKit.Without();
                TestWorlds.Place(g, 0, Vec3.Zero, 0f);
                TestWorlds.Place(g, 1, new Vec3(offset, 0f, 8f), 180f);
                Fire(g, SpellTestKit.Aim(g, 0, new Vec3(0f, 1.1f, 8f)));
                List<DamageEvent> hits = AKit.Hits(g, 1, DamageKind.Direct);
                Assert.Equal(withAugment ? 1 : 0, hits.Count);
                if (withAugment) Assert.Equal(48f * 0.95f, hits[0].Resolution.Raw, 3);
            }
        }

        [Fact]
        [Verifies("AUG-027")]
        public void Broad_Tip_Skips_Wide_Multiple_And_Non_Projectile_Spells()
        {
            DuelWorld w = WithSpells(AKit.Aug(BroadTip.Id, 2), null,
                AKit.Spell("SPL-015", new ShapedBolt { Radius = 0.3f }),
                AKit.Spell("SPL-016", new ShapedBolt { Count = 2 }));
            Assert.Equal(0.3f, Shot(w, "SPL-015").Radius, 4);
            Assert.Equal(30f, Form(w, "SPL-015").Payload.Direct, 3);
            Assert.Equal(0.15f, Shot(w, "SPL-016").Radius, 4);
            Assert.Equal(30f, Form(w, "SPL-016").Payload.Direct, 3);
            DuelWorld bram = AKit.With(BroadTip.Id, 2);
            Assert.Equal(36f, Form(bram, "SPL-010").Payload.Direct, 3); // the cone is never enlarged or cut
        }

        // ----- AUG-028 Fine Tip ----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.09f)]
        [Verifies("AUG-028")]
        public void Fine_Tip_Raises_Damage_And_Narrows_The_Projectile(int rank, float bonus)
        {
            AKit.AssertQuotes(FineTip.Id, "radius above 0.08 m", "Direct damage increases 6%.", "Direct damage increases 9%.", "Radius decreases 15%, minimum 0.08 m.");
            DuelWorld w = AKit.With(FineTip.Id, rank);
            Assert.Equal(0.15f * 0.85f, Shot(w).Radius, 4);
            AKit.FaceOff(w, 6f);
            Fire(w);
            Assert.Equal(48f * (1f + bonus), DirectHit(w).Resolution.Raw, 3);

            // The narrower projectile misses a graze the plain Fireball still lands.
            DuelWorld graze = AKit.With(FineTip.Id, rank);
            TestWorlds.Place(graze, 0, Vec3.Zero, 0f);
            TestWorlds.Place(graze, 1, new Vec3(GlobalRules.Movement.CapsuleRadius + 0.14f, 0f, 8f), 180f);
            Fire(graze, SpellTestKit.Aim(graze, 0, new Vec3(0f, 1.1f, 8f)));
            Assert.Empty(AKit.Hits(graze, 1, DamageKind.Direct));
            DuelWorld control = AKit.Without();
            TestWorlds.Place(control, 0, Vec3.Zero, 0f);
            TestWorlds.Place(control, 1, new Vec3(GlobalRules.Movement.CapsuleRadius + 0.14f, 0f, 8f), 180f);
            Fire(control, SpellTestKit.Aim(control, 0, new Vec3(0f, 1.1f, 8f)));
            Assert.Single(AKit.Hits(control, 1, DamageKind.Direct));
        }

        [Fact]
        [Verifies("AUG-028")]
        public void Fine_Tip_Never_Narrows_Below_008_m_And_Skips_Projectiles_At_The_Minimum()
        {
            DuelWorld w = WithSpells(AKit.Aug(FineTip.Id, 2), null,
                AKit.Spell("SPL-015", new ShapedBolt { Radius = 0.09f }),
                AKit.Spell("SPL-016", new ShapedBolt { Radius = 0.08f }));
            Assert.Equal(0.08f, Shot(w, "SPL-015").Radius, 4); // 0.0765 clamps to the minimum
            Assert.Equal(30f * 1.09f, Form(w, "SPL-015").Payload.Direct, 3);
            Assert.Equal(0.08f, Shot(w, "SPL-016").Radius, 4); // not above 0.08 m: no edit
            Assert.Equal(30f, Form(w, "SPL-016").Payload.Direct, 3);
        }

        // ----- AUG-031 High Arc ----------------------------------------------------------------------------

        private static ArenaLayout CrateBetween(params Solid[] extra)
        {
            ArenaLayout layout = SpellTestKit.FlatWith(extra);
            layout.Props.Add(WorldDuels.Prop(1, "PROP-006", new Vec3(0f, 0f, 4f), new Vec3(1f, 2f, 1f)));
            return layout;
        }

        private static float PropDamage(DuelWorld w) =>
            AKit.Events<PropEvent>(w).Where(e => e.Change == PropChange.Damaged || e.Change == PropChange.Destroyed).Sum(e => e.Amount);

        [Theory]
        [InlineData(1, 0.12f)]
        [InlineData(2, 0.20f)]
        [Verifies("AUG-031")]
        public void High_Arc_Raises_Prop_Damage_Of_A_Release_Above_The_Enemy(int rank, float bonus)
        {
            AKit.AssertQuotes(HighArc.Id, "at least 1 m above enemy feet", "Prop damage increases 12%.", "Prop damage increases 20%.", "Player damage decreases 3%.");
            float plain = PropDamageFromLedge(null, 0);
            Assert.True(plain > 0f);
            Assert.Equal(plain * (1f + bonus), PropDamageFromLedge(HighArc.Id, rank), 2);
        }

        /// <summary>A 1.2 m ledge under player 0 at the origin (feet 1.2 m above the enemy's on the floor).</summary>
        private static Solid Ledge(float height = 1.2f) => TestWorlds.Box(51, Vec3.Zero, new Vec3(2f, height, 2f), SolidKind.Platform);

        /// <summary>Player 0 on the ledge fires down at the crate 4 m ahead; the enemy stands on the floor 8 m away.</summary>
        private static float PropDamageFromLedge(string augment, int rank)
        {
            DuelWorld w = augment != null ? AKit.With(augment, rank, layout: CrateBetween(Ledge())) : AKit.Without(layout: CrateBetween(Ledge()));
            TestWorlds.Place(w, 0, new Vec3(0f, 1.2f, 0f), 0f);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 8f), 180f);
            AKit.Step(w, 2);
            Assert.True(w.Player(0).Position.Y - w.Player(1).Position.Y >= HighArc.MinHeight);
            Fire(w, SpellTestKit.Aim(w, 0, new Vec3(0f, 1f, 4f)));
            return PropDamage(w);
        }

        [Fact]
        [Verifies("AUG-031")]
        public void High_Arc_Gives_Nothing_To_A_Level_Ground_Release()
        {
            // Level ground: the release point (eye) is 1.6 m above both players' feet, but the release is not elevated.
            DuelWorld control = AKit.Without(layout: CrateBetween());
            AKit.FaceOff(control, 8f);
            Fire(control);
            DuelWorld w = AKit.With(HighArc.Id, 2, layout: CrateBetween());
            AKit.FaceOff(w, 8f);
            Fire(w);
            Assert.True(PropDamage(control) > 0f);
            Assert.Equal(PropDamage(control), PropDamage(w), 3);

            DuelWorld duel = AKit.With(HighArc.Id, 2);
            AKit.FaceOff(duel, 6f);
            Fire(duel);
            Assert.Equal(48f, DirectHit(duel).Resolution.Raw, 3); // no drawback either
        }

        [Fact]
        [Verifies("AUG-031")]
        public void High_Arc_Drawback_Cuts_Player_Damage_And_A_Raised_Enemy_Gets_Neither()
        {
            DuelWorld w = AKit.With(HighArc.Id, 2, layout: SpellTestKit.FlatWith(Ledge()));
            TestWorlds.Place(w, 0, new Vec3(0f, 1.2f, 0f), 0f);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 6f), 180f);
            AKit.Step(w, 2);
            Fire(w, SpellTestKit.AimAt(w, 0, 1));
            Assert.Equal(48f * 0.97f, DirectHit(w).Resolution.Raw, 3);

            // The enemy stands on a 0.7 m platform: the ledge is only 0.5 m above its feet.
            Solid platform = TestWorlds.Box(50, new Vec3(0f, 0f, 6f), new Vec3(2f, 0.7f, 2f), SolidKind.Platform);
            DuelWorld raised = AKit.With(HighArc.Id, 2, layout: SpellTestKit.FlatWith(Ledge(), platform));
            TestWorlds.Place(raised, 0, new Vec3(0f, 1.2f, 0f), 0f);
            TestWorlds.Place(raised, 1, new Vec3(0f, 0.7f, 6f), 180f);
            AKit.Step(raised, 2);
            Assert.True(raised.Player(0).Position.Y - raised.Player(1).Position.Y < HighArc.MinHeight);
            Fire(raised, SpellTestKit.AimAt(raised, 0, 1));
            Assert.Equal(48f, DirectHit(raised).Resolution.Raw, 3);
        }

        // ----- AUG-038 First Contact -----------------------------------------------------------------------

        private static DuelWorld EnemyAt(string id, int rank, float health)
        {
            DuelWorld w = AKit.With(id, rank);
            AKit.FaceOff(w, 6f);
            AKit.Step(w, 1);
            if (health < 300f) w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 300f - health, sourceId: "ENV-TEST"));
            AKit.Step(w, 1);
            return w;
        }

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-038")]
        public void First_Contact_Boosts_A_Projectile_Hit_On_A_Full_Health_Enemy(int rank, float bonus)
        {
            AKit.AssertQuotes(FirstContact.Id, "hits an enemy with full health", "Damage increases 5%.", "Damage increases 8%.", "Damage against enemies below 75 health decreases 3%.");
            DuelWorld w = EnemyAt(FirstContact.Id, rank, 300f);
            Fire(w);
            Assert.Equal(48f * (1f + bonus), DirectHit(w).Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-038")]
        public void First_Contact_Drawback_Below_75_Health_And_Nothing_In_Between_Or_For_Cones()
        {
            DuelWorld low = EnemyAt(FirstContact.Id, 2, 70f);
            Fire(low);
            Assert.Equal(48f * 0.97f, AKit.Hits(low, 1, DamageKind.Direct).Last().Resolution.Raw, 3);

            DuelWorld mid = EnemyAt(FirstContact.Id, 2, 200f);
            Fire(mid);
            Assert.Equal(48f, AKit.Hits(mid, 1, DamageKind.Direct).Last().Resolution.Raw, 3);

            DuelWorld cone = AKit.With(FirstContact.Id, 2);
            AKit.FaceOff(cone, 2.5f);
            Fire(cone, slot: 1); // Cinder Sweep at full health: not a projectile
            Assert.Equal(36f, AKit.Hits(cone, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
        }

        // ----- AUG-039 Last Crossing -----------------------------------------------------------------------

        /// <summary>Ticks from pressing primary (slot 0 selected) to the Fireball's hit on a standing enemy 6 m away.</summary>
        private static int PressToHit()
        {
            DuelWorld w = AKit.Without();
            AKit.FaceOff(w, 6f);
            int press = w.Tick + 1;
            AKit.HoldAndRelease(w, AKit.NormalHold);
            AKit.Step(w, 60);
            return DirectHit(w).Tick - press;
        }

        private static int JumpToLanding()
        {
            DuelWorld w = AKit.Without();
            AKit.Step(w, 1, null, AKit.B.With(InputButtons.Jump));
            int jump = w.Tick;
            AKit.Step(w, 90);
            return AKit.Events<MovementEvent>(w).First(m => m.Player == 1 && m.Change == MovementChange.Landed).Tick - jump;
        }

        /// <summary>The enemy jumps so that the Fireball strikes it <paramref name="afterLanding"/> ticks after its landing.</summary>
        private static DuelWorld HitAfterLanding(int rank, int afterLanding)
        {
            int toHit = PressToHit();
            int toLand = JumpToLanding();
            DuelWorld w = AKit.With(LastCrossing.Id, rank);
            AKit.FaceOff(w, 6f);
            int pressTick = w.Tick + 1 + 60;
            int jumpTick = pressTick + toHit - afterLanding - toLand;
            Assert.True(jumpTick > w.Tick);
            int end = pressTick + toHit + 30;
            while (w.Tick < end)
            {
                int next = w.Tick + 1;
                PlayerInput a = next >= pressTick && next < pressTick + AKit.NormalHold ? AKit.A.With(InputButtons.Primary) : AKit.A;
                PlayerInput b = next == jumpTick ? AKit.B.With(InputButtons.Jump) : AKit.B;
                w.Step(a, b);
            }

            int landed = AKit.Events<MovementEvent>(w).First(m => m.Player == 1 && m.Change == MovementChange.Landed).Tick;
            Assert.Equal(landed + afterLanding, DirectHit(w).Tick);
            return w;
        }

        [Theory]
        [InlineData(1, 2, 0.06f)]
        [InlineData(2, 2, 0.09f)]
        [InlineData(2, 15, 0.09f)]
        [Verifies("AUG-039")]
        public void Last_Crossing_Boosts_A_Hit_During_Landing_Recovery(int rank, int afterLanding, float bonus)
        {
            AKit.AssertQuotes(LastCrossing.Id, "hits during enemy landing recovery", "Damage increases 6%.", "Damage increases 9%.", "Projectiles hitting grounded walking enemies deal 3% less damage.");
            DuelWorld w = HitAfterLanding(rank, afterLanding);
            Assert.Equal(48f * (1f + bonus), DirectHit(w).Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-039")]
        public void Last_Crossing_Gives_Nothing_After_The_Recovery_And_Cuts_Hits_On_Walking_Enemies()
        {
            DuelWorld late = HitAfterLanding(2, 30);
            Assert.Equal(48f, DirectHit(late).Resolution.Raw, 3);

            DuelWorld walking = AKit.With(LastCrossing.Id, 2);
            AKit.FaceOff(walking, 8f);
            PlayerInput walk = AKit.Walk(180f, 0f, -1f); // the enemy backs away, still facing the caster
            Fire(walking, b: walk);
            Assert.Equal(48f * 0.97f, DirectHit(walking).Resolution.Raw, 3);

            DuelWorld standing = AKit.With(LastCrossing.Id, 2);
            AKit.FaceOff(standing, 6f);
            Fire(standing);
            Assert.Equal(48f, DirectHit(standing).Resolution.Raw, 3);
        }

        // ----- AUG-041 Prepared Patience -------------------------------------------------------------------

        /// <summary>WIZ-002 with Boiler Orb (SPL-014: 22 mana, 0.45 s) as a test bolt and Fireball (0.35 s).</summary>
        private static DuelWorld Patience(int rank, bool withAugment = true, float costReduction = 0f)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, withAugment ? AKit.Aug(PreparedPatience.Id, rank) : null);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-014", "SPL-009" });
            if (costReduction > 0f) a.Modifiers.Add(new StatModifier(StatKind.CostReduction, costReduction, "EQP-TEST"));
            return AKit.Duel(a, null, null, AKit.RegistryWith(a, AKit.Spell("SPL-014", new TestBolt(32f, 18f))));
        }

        private static float Refunded(DuelWorld w) => AKit.Casts(w, CastEventKind.Refunded).Sum(c => c.Cast.Mana);

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-041")]
        public void Prepared_Patience_Returns_Part_Of_A_Long_Grounded_Cast(int rank, float fraction)
        {
            AKit.AssertQuotes(PreparedPatience.Id, "at least 0.4 s declared windup and caster stays grounded through release", "Cost decreases 6% mana.", "Cost decreases 10% mana.", "Walking speed during this preparation decreases 5%.");
            DuelWorld w = Patience(rank);
            Fire(w);
            Assert.Equal(22f, AKit.Casts(w, CastEventKind.Committed).Single().Cast.Mana, 3);
            Assert.Equal(22f * fraction, Refunded(w), 3);
            Assert.Single(AKit.Activations(w, PreparedPatience.Id));
        }

        [Fact]
        [Verifies("AUG-041")]
        public void Prepared_Patience_Needs_The_Ground_And_A_04_s_Windup()
        {
            DuelWorld jump = Patience(2);
            PlayerInput input = AKit.SelectSlot(AKit.A, 0);
            AKit.Step(jump, 1, input);
            AKit.HoldAndRelease(jump, AKit.NormalHold, input);
            AKit.Step(jump, 1, input.With(InputButtons.Jump)); // leaves the ground during the windup
            AKit.Step(jump, 60, input);
            Assert.Single(AKit.Casts(jump, CastEventKind.Released));
            Assert.Equal(0f, Refunded(jump), 3);

            DuelWorld fireball = Patience(2);
            Fire(fireball, slot: 1); // 0.35 s windup
            Assert.Single(AKit.Casts(fireball, CastEventKind.Released));
            Assert.Equal(0f, Refunded(fireball), 3);
        }

        [Fact]
        [Verifies("AUG-041")]
        public void Prepared_Patience_Drawback_Slows_Walking_During_The_Windup_Only()
        {
            float SpeedDuringWindup(DuelWorld w)
            {
                PlayerInput walk = AKit.SelectSlot(AKit.Walk(0f), 0);
                AKit.Step(w, 30, walk);
                AKit.HoldAndRelease(w, AKit.NormalHold, walk);
                AKit.Step(w, 10, walk); // inside the 0.45 s windup
                Assert.Equal(CastPhase.Windup, w.Player(0).Caster.Machine.Phase);
                return AKit.Speed(w, 0);
            }

            float with = SpeedDuringWindup(Patience(2));
            float without = SpeedDuringWindup(Patience(2, withAugment: false));
            Assert.Equal(without * 0.95f, with, 2);

            DuelWorld idle = Patience(2);
            AKit.Step(idle, 40, AKit.Walk(0f));
            Assert.Equal(GlobalRules.Movement.WalkSpeed, AKit.Speed(idle, 0), 2); // no penalty outside the preparation
        }

        [Fact]
        [Verifies("AUG-041")]
        public void Prepared_Patience_Refund_Shares_The_25_Percent_Ledger_After_The_Cost_Floor()
        {
            DuelWorld floor = Patience(2, costReduction: 0.30f);
            Fire(floor);
            Assert.Equal(22f * 0.7f, AKit.Casts(floor, CastEventKind.Committed).Single().Cast.Mana, 3);
            Assert.Equal(2.2f, Refunded(floor), 3); // 10% of the listed cost, under 25% of the 15.4 payment

            // Another refund that already used the cast's 25% leaves nothing for this one.
            DuelWorld spent = Patience(2);
            PlayerInput input = AKit.SelectSlot(AKit.A, 0);
            AKit.Step(spent, 1, input);
            AKit.HoldAndRelease(spent, AKit.NormalHold, input);
            ResourceEvent payment = AKit.Events<ResourceEvent>(spent).Last(r => r.Player == 0 && r.Change == ResourceChange.ManaSpent);
            spent.Effects.RefundCast(0, payment.Id, 22f, "EQP-TEST");
            AKit.Step(spent, 60, input);
            Assert.Equal(22f * GlobalRules.Casting.RefundCapFraction, Refunded(spent), 3);
        }

        // ----- AUG-042 Quick Recovery ----------------------------------------------------------------------

        private static int RecoveryTicks(DuelWorld w, int slot)
        {
            PlayerInput input = AKit.SelectSlot(AKit.A, slot);
            AKit.FaceOff(w, 3f);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, AKit.NormalHold, input);
            for (int i = 0; i < 120 && AKit.Casts(w, CastEventKind.Released).Count() == 0; i++) AKit.Step(w, 1, input);
            int released = AKit.Casts(w, CastEventKind.Released).Single().Tick;
            for (int i = 0; i < 120 && w.Player(0).Caster.Machine.Phase != CastPhase.Idle; i++) AKit.Step(w, 1, input);
            return w.Tick - released;
        }

        [Theory]
        [InlineData(1, 3)]
        [InlineData(2, 6)]
        [Verifies("AUG-042")]
        public void Quick_Recovery_Shortens_The_Recovery_Of_A_Fast_Spell(int rank, int saved)
        {
            AKit.AssertQuotes(QuickRecovery.Id, "no more than 0.3 s windup releases", "Post-release recovery decreases 0.05 s, minimum 0.1 s.", "Post-release recovery decreases 0.1 s, minimum 0.1 s.", "That spell direct damage decreases 5%; cooldown unchanged.");
            int plain = RecoveryTicks(AKit.Without(), 1); // Cinder Sweep, 0.25 s windup
            DuelWorld w = AKit.With(QuickRecovery.Id, rank);
            Assert.Equal(plain - saved, RecoveryTicks(w, 1));
            Assert.Equal(36f * 0.95f, AKit.Hits(w, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
            Assert.Equal(AKit.Ticks(5f), w.Player(0).Caster.CooldownRemainingTicks("SPL-010") + (w.Tick - AKit.Casts(w, CastEventKind.Committed).Single().Tick));
        }

        [Fact]
        [Verifies("AUG-042")]
        public void Quick_Recovery_Skips_Slower_Spells_And_Keeps_The_01_s_Minimum()
        {
            int plainFireball = RecoveryTicks(AKit.Without(), 0);
            DuelWorld fireball = AKit.With(QuickRecovery.Id, 2);
            Assert.Equal(plainFireball, RecoveryTicks(fireball, 0)); // 0.35 s windup
            Assert.Equal(48f, Form(fireball).Payload.Direct, 3);

            // With a second 0.1 s recovery cut the shared minimum holds at 0.1 s.
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, new[] { new ActiveAugment(QuickRecovery.Id, 2), new ActiveAugment("AUG-146", 1) });
            ContentRegistry registry = AKit.Registry(new[] { QuickRecovery.Id }, new[] { new KeyValuePair<string, object>("AUG-146", new RecoveryCut(AKit.Ticks(0.1f))) });
            DuelWorld both = AKit.Duel(a, null, null, registry);
            Assert.Equal(AKit.Ticks(QuickRecovery.MinRecoverySec), RecoveryTicks(both, 1));
        }

        // ----- AUG-043 Visible Afterimage ------------------------------------------------------------------

        private static IEnumerable<EffectEvent> Trails(DuelWorld w) =>
            AKit.Events<EffectEvent>(w).Where(e => e.SourceId == VisibleAfterimage.Id && e.Change == EffectChange.Cue && e.Cue == VisibleAfterimage.TrailCue);

        /// <summary>Fireball past an enemy standing <paramref name="offset"/> m to the side, then Cinder Sweep at once.</summary>
        private static DuelWorld NearMiss(int rank, float offset, IEnumerable<ActiveAugment> augments = null, ContentRegistry registry = null, float costReduction = 0f)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, augments ?? AKit.Aug(VisibleAfterimage.Id, rank));
            if (costReduction > 0f) a.Modifiers.Add(new StatModifier(StatKind.CostReduction, costReduction, "EQP-TEST"));
            DuelWorld w = AKit.Duel(a, null, null, registry);
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -10f), 0f);
            TestWorlds.Place(w, 1, new Vec3(offset, 0f, -2f), 180f);
            AKit.HoldAndRelease(w, AKit.NormalHold);
            for (int i = 0; i < 200 && !AKit.Events<SpellEvent>(w).Any(e => e.Player == 0 && (e.Kind == SpellEventKind.ProjectileExpired || e.Kind == SpellEventKind.ProjectileImpact)); i++) AKit.Step(w, 1);
            AKit.Step(w, 1);
            PlayerInput cinder = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(w, 1, cinder);
            AKit.HoldAndRelease(w, AKit.NormalHold, cinder);
            AKit.Step(w, 30, cinder);
            return w;
        }

        private static float CinderPaid(DuelWorld w) => AKit.Casts(w, CastEventKind.Committed).Single(c => c.SpellId == "SPL-010").Cast.Mana;

        [Theory]
        [InlineData(1, 0.5f, 2f)]
        [InlineData(2, 0.75f, 3f)]
        [Verifies("AUG-043")]
        public void Visible_Afterimage_A_Near_Miss_Leaves_A_Trail_And_Discounts_The_Next_Different_Spell(int rank, float trailSec, float discount)
        {
            AKit.AssertQuotes(VisibleAfterimage.Id, "untransformed projectile misses an enemy within 1 m", "inert glowing path for 0.5 s", "inert glowing path for 0.75 s", "costs 2 less mana", "costs 3 less mana", "next different spell within 1 s", "4 s cooldown");
            DuelWorld w = NearMiss(rank, 1.2f); // 0.65 m between the surfaces
            Assert.Empty(AKit.Hits(w, 1, DamageKind.Direct));
            EffectEvent trail = Trails(w).Single();
            Assert.Equal(trailSec, trail.Amount, 3);
            Assert.Equal(15f - discount, CinderPaid(w), 3);
        }

        [Fact]
        [Verifies("AUG-043")]
        public void Visible_Afterimage_Needs_A_Miss_Within_1_m_Of_An_Untransformed_Projectile()
        {
            DuelWorld far = NearMiss(2, 2.0f); // 1.45 m between the surfaces
            Assert.Empty(Trails(far));
            Assert.Equal(15f, CinderPaid(far), 3);

            DuelWorld hit = NearMiss(2, 0f);
            Assert.Single(AKit.Hits(hit, 1, DamageKind.Direct));
            Assert.Empty(Trails(hit));
            Assert.Equal(15f, CinderPaid(hit), 3);

            // A Fireball changed by a wizard transformation (AUG-305 targets SPL-009) does not qualify.
            var augments = new[] { new ActiveAugment(VisibleAfterimage.Id, 2), new ActiveAugment("AUG-305", 1) };
            ContentRegistry registry = AKit.Registry(new[] { VisibleAfterimage.Id }, new[] { new KeyValuePair<string, object>("AUG-305", new MarkerTransform()) });
            DuelWorld transformed = NearMiss(2, 1.2f, augments, registry);
            Assert.Contains("AUG-305", transformed.Player(0).Caster.FormFor("SPL-009").AppliedTransforms);
            Assert.Empty(Trails(transformed));
        }

        [Fact]
        [Verifies("AUG-043")]
        public void Visible_Afterimage_Discount_Stays_Inside_The_Cost_Floor_And_Expires_After_1_s()
        {
            DuelWorld floor = NearMiss(2, 1.2f, costReduction: 0.30f);
            Assert.Single(Trails(floor));
            Assert.Equal(15f * 0.7f, CinderPaid(floor), 3);

            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(VisibleAfterimage.Id, 2));
            DuelWorld late = AKit.Duel(a);
            TestWorlds.Place(late, 0, new Vec3(0f, 0f, -10f), 0f);
            TestWorlds.Place(late, 1, new Vec3(1.2f, 0f, -2f), 180f);
            AKit.HoldAndRelease(late, AKit.NormalHold);
            AKit.Step(late, 200);
            Assert.Single(Trails(late));
            PlayerInput cinder = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(late, 1, cinder);
            AKit.HoldAndRelease(late, AKit.NormalHold, cinder);
            Assert.Equal(15f, CinderPaid(late), 3);
        }

        [Fact]
        [Verifies("AUG-043")]
        public void Visible_Afterimage_Fires_At_Most_Once_Per_4_s()
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(VisibleAfterimage.Id, 2));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-015", "SPL-016" });
            DuelWorld w = AKit.Duel(a, null, null, AKit.RegistryWith(a, AKit.Spell("SPL-015", new TestBolt(20f)), AKit.Spell("SPL-016", new TestBolt(20f))));
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -10f), 0f);
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, -2f), 180f);
            Fire(w, slot: 0, flyTicks: 90);
            Fire(w, slot: 1, flyTicks: 90);
            Assert.Equal(2, AKit.Events<SpellEvent>(w).Count(e => e.Player == 0 && e.Kind == SpellEventKind.ProjectileExpired));
            Assert.True(w.Tick < AKit.Ticks(4f) + 10);
            Assert.Single(Trails(w));
        }

        // ----- AUG-044 Narrow Verdict ----------------------------------------------------------------------

        /// <summary>Two walls at z = 3 leaving an opening of the given width on the line between the players.</summary>
        private static ArenaLayout Opening(float width) => SpellTestKit.FlatWith(
            SpellTestKit.Wall(60, new Vec3(-(width * 0.5f + 1f), 0f, 3f), new Vec3(2f, 3f, 0.3f)),
            SpellTestKit.Wall(61, new Vec3(width * 0.5f + 1f, 0f, 3f), new Vec3(2f, 3f, 0.3f)));

        [Theory]
        [InlineData(1, 0.07f)]
        [InlineData(2, 0.11f)]
        [Verifies("AUG-044")]
        public void Narrow_Verdict_Boosts_A_Hit_Through_An_Opening_Narrower_Than_1_m(int rank, float bonus)
        {
            AKit.AssertQuotes(NarrowVerdict.Id, "visible through a gap narrower than 1 m", "Damage increases 7%.", "Damage increases 11%.", "Projectile radius decreases 10%; no bonus through concealment.");
            DuelWorld w = AKit.With(NarrowVerdict.Id, rank, layout: Opening(0.8f));
            Assert.Equal(0.15f * 0.9f, Shot(w).Radius, 4);
            AKit.FaceOff(w, 6f);
            Fire(w);
            Assert.Equal(48f * (1f + bonus), DirectHit(w).Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-044")]
        public void Narrow_Verdict_Gives_Nothing_In_The_Open_Through_A_Wide_Gap_Or_Into_Concealment()
        {
            DuelWorld open = AKit.With(NarrowVerdict.Id, 2);
            AKit.FaceOff(open, 6f);
            Fire(open);
            Assert.Equal(48f, DirectHit(open).Resolution.Raw, 3);

            DuelWorld wide = AKit.With(NarrowVerdict.Id, 2, layout: Opening(1.4f));
            AKit.FaceOff(wide, 6f);
            Fire(wide);
            Assert.Equal(48f, DirectHit(wide).Resolution.Raw, 3);

            DuelWorld steam = AKit.With(NarrowVerdict.Id, 2, layout: Opening(0.8f));
            AKit.FaceOff(steam, 6f);
            WorldDuels.Reactions(steam).CreateVolume(new VolumeSpec { Kind = VolumeKind.Steam, Footprint = Footprint.Circle(new Vec2(0f, 4.5f), 1.5f), BaseY = 0f, LifetimeTicks = AKit.Ticks(8f) });
            AKit.Step(steam, 1);
            Assert.False(steam.CanAcquire(0, 1));
            Fire(steam);
            Assert.Equal(48f, DirectHit(steam).Resolution.Raw, 3);
        }
    }
}
