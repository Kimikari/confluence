using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
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
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>
    /// The "Fieldcraft and terrain" shared augments in real duels: WIZ-002's Fireball (48 direct / 28 splash, solid damage
    /// 48) against enemy-created walls, catalogue props (PROP-027 Glass Jug, light, 20 durability; PROP-006 Insulated
    /// Crate, heavy, 100) and fields (SPL-012 Ash Curtain, 20 mana, under a test field behavior).
    /// </summary>
    public class FieldcraftTests
    {
        private static void Fire(DuelWorld w, PlayerInput? aim = null, int slot = 0, int flyTicks = 40)
        {
            PlayerInput input = AKit.SelectSlot(aim ?? AKit.A, slot);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, AKit.NormalHold, input);
            AKit.Step(w, flyTicks, input);
        }

        private static float ManaPaid(DuelWorld w, string spellId) =>
            AKit.Events<ResourceEvent>(w).Where(e => e.Player == 0 && e.Change == ResourceChange.ManaSpent && e.SourceId == spellId).Select(e => e.Amount).Last();

        private static int EnemyWall(DuelWorld w, float z, int owner = 1, float durability = 30f)
        {
            int id = w.AddTemporarySolid(new TemporarySolidSpec
            {
                Box = YawBox.OnFloor(new Vec3(0f, 0f, z), new Vec3(2f, 3f, 0.4f), 0f),
                Kind = SolidKind.Wall,
                BlocksSight = true,
                Durability = durability,
                OwnerPlayer = owner,
                SourceId = "SPL-TEST-WALL",
            });
            Assert.True(id >= 0);
            return id;
        }

        private static bool Destroyed(DuelWorld w, int solidId) =>
            AKit.Events<SolidEvent>(w).Any(e => e.SolidId == solidId && e.Change == SolidChange.Destroyed);

        // ----- AUG-059 Breaker's Dividend ------------------------------------------------------------------

        private static (DuelWorld w, DuelWorld control) Breakers(int rank)
        {
            DuelWorld w = AKit.With(BreakersDividend.Id, rank);
            DuelWorld control = AKit.Without();
            foreach (DuelWorld d in new[] { w, control })
            {
                AKit.FaceOff(d, 12f);
                AKit.Step(d, 1);
                Assert.True(d.Player(0).Resources.TrySpendStamina(60f, d.Tick));
            }

            return (w, control);
        }

        [Theory]
        [InlineData(1, 3f)]
        [InlineData(2, 5f)]
        [Verifies("AUG-059")]
        public void Breakers_Dividend_Grants_Stamina_When_A_Direct_Spell_Destroys_Enemy_Cover(int rank, float stamina)
        {
            AKit.AssertQuotes(BreakersDividend.Id, "Destroy enemy-created cover with a direct spell.", "Gain 3 stamina, 4 s cooldown.", "Gain 5 stamina, 4 s cooldown.", "Direct player damage decreases 2%.");
            (DuelWorld w, DuelWorld control) = Breakers(rank);
            int wall = EnemyWall(w, 4f);
            int controlWall = EnemyWall(control, 4f);
            Fire(w);
            Fire(control);
            Assert.True(Destroyed(w, wall));
            Assert.True(Destroyed(control, controlWall));
            Assert.Equal(control.Player(0).Resources.Stamina + stamina, w.Player(0).Resources.Stamina, 3);
            Assert.Single(AKit.Activations(w, BreakersDividend.Id));
        }

        [Fact]
        [Verifies("AUG-059")]
        public void Breakers_Dividend_Needs_Enemy_Cover_A_Spell_And_Respects_Its_Cooldown()
        {
            // Own cover and arena cover give nothing.
            (DuelWorld own, DuelWorld ownControl) = Breakers(2);
            EnemyWall(own, 4f, owner: 0);
            EnemyWall(ownControl, 4f, owner: 0);
            Fire(own);
            Fire(ownControl);
            Assert.Equal(ownControl.Player(0).Resources.Stamina, own.Player(0).Resources.Stamina, 3);

            // Breaking enemy cover with the weapon is not a direct spell.
            (DuelWorld melee, DuelWorld meleeControl) = Breakers(2);
            foreach (DuelWorld d in new[] { melee, meleeControl })
            {
                int wall = EnemyWall(d, 1.4f, durability: 10f);
                AKit.Strike(d);
                AKit.FinishStrike(d);
                Assert.True(Destroyed(d, wall));
            }

            Assert.Equal(meleeControl.Player(0).Resources.Stamina, melee.Player(0).Resources.Stamina, 3);
            Assert.Empty(AKit.Activations(melee, BreakersDividend.Id));

            // Two enemy walls broken within 4 s (Fireball, then a second projectile spell): one grant; a third after the
            // cooldown grants again.
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(BreakersDividend.Id, 1));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.Add("SPL-009");
            a.EquippedSpellIds.Add("SPL-014");
            DuelWorld w = AKit.Duel(a, null, null, AKit.RegistryWith(a, AKit.Spell("SPL-014", new TestBolt(32f))));
            AKit.FaceOff(w, 12f);
            int first = EnemyWall(w, 4f);
            Fire(w);
            Assert.True(Destroyed(w, first));
            int second = EnemyWall(w, 4f);
            Fire(w, slot: 1, flyTicks: 60);
            Assert.True(Destroyed(w, second));
            Assert.Single(AKit.Activations(w, BreakersDividend.Id));
            AKit.Step(w, AKit.Ticks(4.5f));
            int third = EnemyWall(w, 4f);
            Fire(w);
            Assert.True(Destroyed(w, third));
            Assert.Equal(2, AKit.Activations(w, BreakersDividend.Id).Count());
        }

        [Fact]
        [Verifies("AUG-059")]
        public void Breakers_Dividend_Drawback_Cuts_Direct_Player_Damage_Only()
        {
            DuelWorld w = AKit.With(BreakersDividend.Id, 1);
            AKit.FaceOff(w, 6f);
            Fire(w);
            Assert.Equal(48f * 0.98f, AKit.Hits(w, 1, DamageKind.Direct).Single().Resolution.Raw, 3);

            // Splash (impact on the floor beside the enemy) keeps its 28.
            DuelWorld splash = AKit.With(BreakersDividend.Id, 1);
            AKit.FaceOff(splash, 6f);
            Fire(splash, SpellTestKit.Aim(splash, 0, new Vec3(1.2f, 0f, 6f)));
            Assert.Equal(28f, AKit.Hits(splash, 1, DamageKind.Splash).Single().Resolution.Raw, 3);
        }

        // ----- AUG-062 Practical Ruin ----------------------------------------------------------------------

        private static ArenaLayout JugAt(float z, float x = 0f)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.Add(WorldDuels.Prop(1, "PROP-027", new Vec3(x, 0f, z), new Vec3(0.6f, 2f, 0.6f)));
            return layout;
        }

        /// <summary>
        /// WIZ-002 with Fireball (slot 0), Ash Curtain under a test field (slot 1, 20 mana), Cinder Sweep (slot 2, cone,
        /// 15 mana) and Phoenix Rush under a test field (slot 3, 28 mana).
        /// </summary>
        private static DuelWorld FieldDuel(IEnumerable<ActiveAugment> augments, ArenaLayout layout)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, augments);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-009", "SPL-012", "SPL-010", "SPL-013" });
            DuelWorld w = AKit.Duel(a, null, layout, AKit.RegistryWith(a,
                AKit.Spell("SPL-012", new TestField(radius: 3f, lifetimeSec: 6f)),
                AKit.Spell("SPL-013", new TestField(radius: 1f, lifetimeSec: 3f))));
            AKit.FaceOff(w, 12f);
            return w;
        }

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-062")]
        public void Practical_Ruin_Discounts_The_Next_Field_After_Breaking_A_Nearby_Prop(int rank, float discount)
        {
            AKit.AssertQuotes(PracticalRuin.Id, "Destroy a destructible prop within 4 m.", "Next field within 2 s costs 2 less mana, 4 s cooldown.", "Next field within 2 s costs 4 less mana, 4 s cooldown.", "Unspent reduction expires.");
            DuelWorld w = FieldDuel(AKit.Aug(PracticalRuin.Id, rank), JugAt(3f));
            Fire(w);
            Assert.Contains(AKit.Events<PropEvent>(w), e => e.Change == PropChange.Destroyed && e.Player == 0);
            Fire(w, slot: 1, flyTicks: 60);
            Assert.Equal(20f - discount, ManaPaid(w, "SPL-012"), 3);

            // Used once: the next field inside the same 2 s pays full price.
            int destroyedAt = AKit.Events<PropEvent>(w).First(e => e.Change == PropChange.Destroyed).Tick;
            Fire(w, slot: 3);
            Assert.Equal(28f, ManaPaid(w, "SPL-013"), 3);
            int committed = AKit.Casts(w, CastEventKind.Committed).Last().Tick;
            Assert.True(committed - destroyedAt <= AKit.Ticks(2f));
        }

        [Fact]
        [Verifies("AUG-062")]
        public void Practical_Ruin_Expires_Needs_A_Field_A_Near_Prop_And_Has_A_Cooldown()
        {
            // Unspent reduction expires after 2 s.
            DuelWorld late = FieldDuel(AKit.Aug(PracticalRuin.Id, 2), JugAt(3f));
            Fire(late);
            AKit.Step(late, AKit.Ticks(2f));
            Fire(late, slot: 1);
            Assert.Equal(20f, ManaPaid(late, "SPL-012"), 3);

            // A non-field spell (Cinder Sweep) neither uses nor spends the reduction; the field after it still has it.
            DuelWorld cone = FieldDuel(AKit.Aug(PracticalRuin.Id, 2), JugAt(3f));
            Fire(cone);
            Fire(cone, slot: 2, flyTicks: 60);
            Assert.Equal(15f, ManaPaid(cone, "SPL-010"), 3);
            Fire(cone, slot: 1);
            Assert.Equal(16f, ManaPaid(cone, "SPL-012"), 3);

            // A prop farther than 4 m.
            DuelWorld far = FieldDuel(AKit.Aug(PracticalRuin.Id, 2), JugAt(5f));
            Fire(far);
            Assert.Contains(AKit.Events<PropEvent>(far), e => e.Change == PropChange.Destroyed);
            Fire(far, slot: 1);
            Assert.Equal(20f, ManaPaid(far, "SPL-012"), 3);
        }

        [Fact]
        [Verifies("AUG-062")]
        public void Practical_Ruin_Opens_At_Most_Once_Per_4_Seconds()
        {
            // The first jug breaks to the Fireball and the field takes the discount; a second jug broken by the weapon
            // about 2.5 s later opens nothing (4 s cooldown), so the next field pays full price.
            ArenaLayout layout = JugAt(3f);
            layout.Props.Add(WorldDuels.Prop(2, "PROP-027", new Vec3(3f, 0f, 0f), new Vec3(0.6f, 2f, 0.6f)));
            DuelWorld w = FieldDuel(AKit.Aug(PracticalRuin.Id, 1), layout);
            Fire(w);
            Fire(w, slot: 1, flyTicks: 60);
            Assert.Equal(18f, ManaPaid(w, "SPL-012"), 3);
            TestWorlds.Place(w, 0, new Vec3(3f, 0f, -1.1f), 0f);
            for (int i = 0; i < 4 && AKit.Events<PropEvent>(w).Count(e => e.Change == PropChange.Destroyed) < 2; i++)
            {
                AKit.Strike(w);
                AKit.FinishStrike(w);
            }

            Assert.Equal(2, AKit.Events<PropEvent>(w).Count(e => e.Change == PropChange.Destroyed));
            int first = AKit.Events<PropEvent>(w).First(e => e.Change == PropChange.Destroyed).Tick;
            int second = AKit.Events<PropEvent>(w).Last(e => e.Change == PropChange.Destroyed).Tick;
            Assert.True(second - first < AKit.Ticks(4f));
            Fire(w, slot: 3);
            Assert.Equal(28f, ManaPaid(w, "SPL-013"), 3);
            Assert.Single(AKit.Activations(w, PracticalRuin.Id));
        }


        // ----- AUG-067 Surveyed Demolition -----------------------------------------------------------------

        private static ArenaLayout CrateAhead()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.Add(WorldDuels.Prop(1, "PROP-006", new Vec3(0f, 0f, 1.7f), new Vec3(1f, 2.4f, 1f)));
            return layout;
        }

        private static float SpellPropDamage(DuelWorld w, int afterEvent) =>
            AKit.Events<PropEvent>(w).Where(e => e.Id > afterEvent && (e.Change == PropChange.Damaged || e.Change == PropChange.Destroyed)).Sum(e => e.Amount);

        /// <summary>Strikes the crate with the focus, waits, then Fireballs it; the enemy stands beside the crate in splash range.</summary>
        private static (float cover, float splash) StrikeThenFire(DuelWorld w, bool strike, float waitSec)
        {
            AKit.Step(w, 1);
            TestWorlds.Place(w, 1, new Vec3(1.3f, 0f, 1.2f), 180f);
            if (strike)
            {
                AKit.Strike(w);
                AKit.FinishStrike(w);
                Assert.Contains(AKit.Events<MeleeEvent>(w), m => m.Change == MeleeChange.ContactedSolid);
            }

            AKit.Step(w, AKit.Ticks(waitSec));
            int mark = w.EventLog.Count > 0 ? w.EventLog.Last().Id : 0;
            Fire(w);
            float splash = AKit.Hits(w, 1, DamageKind.Splash).Select(d => d.Resolution.Raw).DefaultIfEmpty(0f).Single();
            return (SpellPropDamage(w, mark), splash);
        }

        [Theory]
        [InlineData(1, 0.15f)]
        [InlineData(2, 0.25f)]
        [Verifies("AUG-067")]
        public void Surveyed_Demolition_Raises_Spell_Damage_To_Cover_The_Weapon_Just_Damaged(int rank, float bonus)
        {
            AKit.AssertQuotes(SurveyedDemolition.Id, "already damaged by your weapon within 2 s", "Cover damage increases 15%.", "Cover damage increases 25%.", "Player damage from that spell decreases 3%.");
            DuelWorld w = AKit.With(SurveyedDemolition.Id, rank, layout: CrateAhead());
            AKit.FaceOff(w, 12f);
            (float cover, float splash) = StrikeThenFire(w, true, 0.2f);
            Assert.Equal(48f * (1f + bonus), cover, 2);
            Assert.Equal(28f * 0.97f, splash, 3); // the drawback: that spell's player damage
        }

        [Fact]
        [Verifies("AUG-067")]
        public void Surveyed_Demolition_Needs_A_Recent_Weapon_Hit()
        {
            DuelWorld fresh = AKit.With(SurveyedDemolition.Id, 2, layout: CrateAhead());
            AKit.FaceOff(fresh, 12f);
            (float cover, float splash) = StrikeThenFire(fresh, false, 0.2f);
            Assert.Equal(48f, cover, 2);
            Assert.Equal(28f, splash, 3);

            DuelWorld stale = AKit.With(SurveyedDemolition.Id, 2, layout: CrateAhead());
            AKit.FaceOff(stale, 12f);
            (float staleCover, float staleSplash) = StrikeThenFire(stale, true, 2.2f);
            Assert.Equal(48f, staleCover, 2);
            Assert.Equal(28f, staleSplash, 3);
        }

        // ----- AUG-105 Wall Reading ------------------------------------------------------------------------

        /// <summary>A wall whose face is at x = 0.4 + gap from a body standing at the origin.</summary>
        private static ArenaLayout WallBeside(float gap) =>
            SpellTestKit.FlatWith(TestWorlds.Box(50, new Vec3(0.4f + gap + 0.5f, 0f, 0f), new Vec3(1f, 3f, 4f)));

        private static (Vec3 landed, float stamina, float apex) Jump(DuelWorld w)
        {
            float before = w.Player(0).Resources.Stamina;
            float apex = w.Player(0).Position.Y;
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            float spent = before - w.Player(0).Resources.Stamina;
            for (int i = 0; i < 90; i++)
            {
                AKit.Step(w, 1);
                apex = Math.Max(apex, w.Player(0).Position.Y);
            }

            Assert.True(w.Player(0).Motor.Grounded);
            return (w.Player(0).Position, spent, apex);
        }

        private static DuelWorld WallDuel(int rank, float gap)
        {
            DuelWorld w = rank > 0 ? AKit.With(WallReading.Id, rank, layout: WallBeside(gap)) : AKit.Without(layout: WallBeside(gap));
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            TestWorlds.Place(w, 1, new Vec3(-8f, 0f, 8f), 180f);
            AKit.Step(w, 2);
            return w;
        }

        [Theory]
        [InlineData(1, 0.4f)]
        [InlineData(2, 0.7f)]
        [Verifies("AUG-105")]
        public void Wall_Reading_Adds_Lateral_Motion_Away_From_A_Near_Wall_For_Two_Stamina(int rank, float lateral)
        {
            AKit.AssertQuotes(WallReading.Id, "Jump within 0.5 m of solid wall.", "Add 0.4 m lateral motion away from wall.", "Add 0.7 m lateral motion away from wall.", "Costs 2 extra stamina; no wall flight, 5 s cooldown.");
            (Vec3 plain, float plainCost, float plainApex) = Jump(WallDuel(0, 0.3f));
            DuelWorld w = WallDuel(rank, 0.3f);
            (Vec3 landed, float cost, float apex) = Jump(w);
            Assert.Equal(plain.X - lateral, landed.X, 2);
            Assert.Equal(plain.Z, landed.Z, 2);
            Assert.Equal(plainCost + 2f, cost, 3);
            Assert.Equal(plainApex, apex, 2); // no wall flight: the arc's height is unchanged
            MarkerEvent exit = ExitMarker(w);
            Assert.Equal(landed.X, exit.To.X, 2); // the marker shows where the jump comes down
            Assert.Equal(landed.Z, exit.To.Z, 2);
            Assert.Equal(-1f, exit.Direction.X, 3); // away from the wall
            Assert.Equal(lateral, exit.Amount, 3);
        }

        private static MarkerEvent ExitMarker(DuelWorld w) =>
            AKit.Events<MarkerEvent>(w).Single(m => m.SourceId == WallReading.Id && m.Cue == WallReading.ExitCue && m.Player == 0);

        [Fact]
        [Verifies("AUG-105")]
        public void Wall_Reading_Counterplay_A_Shot_Prepared_On_The_Visible_Exit_Lands()
        {
            // Player 1 (Bram with Fireball) holds a prepared cast; player 0 wall-jumps; player 1 reads the public exit marker,
            // aims at it and releases: the shot meets player 0 where the motion ends.
            DuelWorld w = AKit.Duel(AKit.Setup("A", AKit.Bram, AKit.Aug(WallReading.Id, 2)), AKit.Setup("B", AKit.Bram), WallBeside(0.3f));
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            TestWorlds.Place(w, 1, new Vec3(-8f, 0f, 0f), 90f);
            AKit.Step(w, 2);
            PlayerInput hold = PlayerInput.Idle(90f).With(InputButtons.Primary);
            w.Step(AKit.A.With(InputButtons.Jump), hold);
            for (int i = 0; i < 3 && !AKit.Events<MarkerEvent>(w).Any(); i++) w.Step(AKit.A, hold);
            MarkerEvent exit = ExitMarker(w);
            Assert.True(exit.VisibleAt(w.Tick));
            Assert.True(exit.From.X > exit.To.X); // the opponent can read the direction of the exit

            PlayerInput aim = SpellTestKit.Aim(w, 1, exit.To + new Vec3(0f, 1.1f, 0f));
            for (int i = 0; i < 12; i++) w.Step(AKit.A, aim.With(InputButtons.Primary));
            w.Step(AKit.A, aim); // release
            for (int i = 0; i < 90 && AKit.Hits(w, 0, DamageKind.Direct).Count == 0; i++) w.Step(AKit.A, aim);
            DamageEvent hit = AKit.Hits(w, 0, DamageKind.Direct).Single();
            Assert.Equal(1, hit.Attacker);
            Assert.Equal(exit.To.X, w.Player(0).Position.X, 2);
        }

        [Fact]
        [Verifies("AUG-105")]
        public void Wall_Reading_Needs_A_Wall_Within_Half_A_Metre_And_Waits_5_Seconds()
        {
            (Vec3 far, float farCost, float _) = Jump(WallDuel(2, 0.6f));
            Assert.Equal(0f, far.X, 2);
            Assert.Equal(10f, farCost, 3);

            DuelWorld w = WallDuel(2, 0.3f);
            Jump(w);
            Vec3 start = w.Player(0).Position;
            (Vec3 again, float againCost, float _) = Jump(w); // within 5 s: an ordinary jump
            Assert.Equal(start.X, again.X, 2);
            Assert.Equal(10f, againCost, 3);

            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            AKit.Step(w, AKit.Ticks(5f));
            (Vec3 later, float laterCost, float _) = Jump(w);
            Assert.Equal(-0.7f, later.X, 2);
            Assert.Equal(12f, laterCost, 3);
            Assert.Equal(2, AKit.Activations(w, WallReading.Id).Count());
        }
    }
}
