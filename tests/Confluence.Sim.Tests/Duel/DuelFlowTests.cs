using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Status;

namespace Confluence.Sim.Tests.Duel
{
    /// <summary>Alcove ward, resolution, falls and recall, determinism, and events (spec L46-L53, L175, L359, L427).</summary>
    public class DuelFlowTests
    {
        [Fact]
        public void Alcove_Ward_Prevents_Damage_Casting_And_Combat_Actions_And_Confines_Movement()
        {
            DuelWorld w = TestWorlds.Duel(alcoveTicks: 300);
            Assert.Equal(DuelPhase.Alcove, w.Phase);
            Assert.Null(w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 50f)));
            Assert.False(w.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "SPL-A", 0, 30)).Succeeded);
            Assert.Equal(CastBlockReason.AlcoveWard, w.CastPermissionsFor(w.Player(0)).Reason);

            PlayerInput run = TestWorlds.A.Moving(0.3f, 1f).With(InputButtons.Melee | InputButtons.Guard);
            TestWorlds.Run(w, 299, run, TestWorlds.B);
            YawBox alcove = w.AlcoveOf(0);
            Vec3 local = alcove.ToLocal(w.Player(0).Position);
            Assert.InRange(local.Z, -alcove.HalfExtents.Z, alcove.HalfExtents.Z);
            Assert.InRange(local.X, -alcove.HalfExtents.X, alcove.HalfExtents.X);
            Assert.False(w.Player(0).Melee.Busy);
            Assert.False(w.Player(0).Guard.Active);
            Assert.Equal(300f, w.Player(1).Resources.Health);

            w.Step(run, TestWorlds.B);
            Assert.Equal(DuelPhase.Alcove, w.Phase);
            w.Step(TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            Assert.Equal(DuelPhase.Combat, w.Phase);
            Assert.Contains(w.TickEvents, e => e is PhaseEvent p && p.Phase == DuelPhase.Combat);
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            Assert.True(w.Player(0).Position.Z > alcove.Center.Z + alcove.HalfExtents.Z); // the alcove opened
        }

        [Fact]
        public void Timer_Judges_By_Health_Fraction()
        {
            DuelWorld w = TestWorlds.Duel(combatTicks: 120);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 3f)); // one percentage point
            TestWorlds.Run(w, 200, TestWorlds.A, TestWorlds.B);
            Assert.True(w.IsOver);
            Assert.Equal(120, w.Tick);
            Assert.Equal(DuelEndReason.Timer, w.Result.Reason);
            Assert.True(w.Result.Won(0));
        }

        [Fact]
        public void A_Difference_Below_Half_A_Percentage_Point_Is_A_Draw()
        {
            DuelWorld w = TestWorlds.Duel(combatTicks: 60);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 1.4f)); // 0.47 points
            Assert.Equal(-1, w.TimerJudgement(out float diff));
            Assert.Equal(1.4f / 300f, diff, 5);
            TestWorlds.Run(w, 100, TestWorlds.A, TestWorlds.B);
            Assert.True(w.Result.IsDraw);
            Assert.Equal(DuelEndReason.Timer, w.Result.Reason);
        }

        [Fact]
        public void Judgement_Uses_Each_Players_Own_Maximum_And_Ignores_Shields()
        {
            DuelPlayerSetup big = TestWorlds.Setup("A");
            big.Modifiers.Add(new StatModifier(StatKind.MaxHealthBonus, 150f, "AUG-VIGOR"));
            DuelWorld w = TestWorlds.Duel(a: big);
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 10f)); // 440 / 450 = 97.8%
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 10f)); // 290 / 300 = 96.7%
            w.GrantShield(1, "SPL-WARD", 100f, 400);
            Assert.Equal(0, w.TimerJudgement(out _));
        }

        [Fact]
        public void Simultaneous_Lethal_Outcomes_In_One_Step_Are_A_Draw()
        {
            var spells = new ScriptedSpells(world =>
            {
                if (world.Tick == 5)
                {
                    world.ApplyDamage(TestWorlds.Hit(world, 0, 1, 400f));
                    world.ApplyDamage(TestWorlds.Hit(world, 1, 0, 400f));
                }
            });
            DuelWorld w = TestWorlds.Duel(spells: spells);
            TestWorlds.Run(w, 20, TestWorlds.A, TestWorlds.B);
            Assert.True(w.IsOver);
            Assert.Equal(5, w.Tick);
            Assert.True(w.Result.IsDraw);
            Assert.Equal(DuelEndReason.SimultaneousDeath, w.Result.Reason);
        }

        [Fact]
        public void Death_In_Different_Steps_Has_A_Winner()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 400f));
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.True(w.Result.Won(0));
            Assert.Equal(DuelEndReason.Death, w.Result.Reason);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.Equal(1, w.Tick); // a finished duel no longer advances
        }

        [Fact]
        public void Surrender_Is_A_Normal_Loss()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 200f));
            w.Surrender(1);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.True(w.Result.Won(0));
            Assert.Equal(DuelEndReason.Surrender, w.Result.Reason);
        }

        [Fact]
        public void Falls_Above_3_Meters_Deal_10_Per_Meter()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Solids.Add(TestWorlds.Box(2, new Vec3(0f, 0f, 0f), new Vec3(4f, 6f, 4f), SolidKind.Platform));
            DuelWorld w = TestWorlds.Duel(layout);
            TestWorlds.Place(w, 0, new Vec3(0f, 6f, 0f), 0f);
            TestWorlds.RunSeconds(w, 2f, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            DamageEvent fall = null;
            foreach (DuelEvent e in w.EventLog)
            {
                fall = e is DamageEvent d && d.Kind == DamageKind.Fall ? d : fall;
            }

            Assert.NotNull(fall);
            Assert.Equal(30f, fall.Resolution.HealthLost, 1);
            Assert.Equal(50f, GlobalRules.Terrain.FallDamage(20f));
            Assert.Equal(0f, GlobalRules.Terrain.FallDamage(3f));
        }

        [Fact]
        public void Falling_Out_Of_The_Map_Recalls_To_The_Nearest_Platform_With_A_40_Health_Penalty()
        {
            ArenaLayout layout = TestWorlds.Flat(boundaryRadius: 30f, floorHalf: 5f);
            DuelWorld w = TestWorlds.Duel(layout);
            TestWorlds.Place(w, 0, new Vec3(3f, 0f, 0f), 90f);
            PlayerInput walkOff = TestWorlds.Face(90f).Moving(0f, 1f);
            int recallTick = -1;
            for (int i = 0; i < 300 && recallTick < 0; i++)
            {
                w.Step(walkOff, TestWorlds.B);
                if (w.Player(0).IsRecalling)
                {
                    recallTick = w.Tick;
                }
            }

            Assert.True(recallTick > 0);
            Assert.Equal(260f, w.Player(0).Resources.Health, 3);
            Vec3 landing = w.Player(0).Position;
            Assert.InRange(landing.X, 4f, 4.61f); // the nearest floor point, inset by the capsule radius
            Assert.Equal(0f, landing.Y, 3);

            TestWorlds.Run(w, 29, walkOff, TestWorlds.B);
            Assert.True(w.Player(0).IsRecalling);
            Assert.Equal(landing.X, w.Player(0).Position.X, 4); // the visible recall holds the player for 0.5 s
            w.Step(walkOff, TestWorlds.B);
            Assert.False(w.Player(0).IsRecalling);
            DamageEvent penalty = Array.Find(ToArray(w), e => e is DamageEvent d && d.Kind == DamageKind.Recall) as DamageEvent;
            Assert.Equal(-1, penalty.Attacker);
            Assert.False(DamageRequest.Single(-1, 0, DamageKind.Recall, DamageSources.Recall, DamageChannel.None, 40f).CanTriggerEffects);
        }

        [Fact]
        public void Identical_Inputs_Produce_Identical_State_Hashes_Every_Tick()
        {
            DuelWorld a = DeterminismWorld();
            DuelWorld b = DeterminismWorld();
            DuelWorld c = DeterminismWorld();
            var rng = new Rng(SeedHash.Of("determinism"));
            bool diverged = false;
            for (int t = 0; t < 900 && !a.IsOver; t++)
            {
                PlayerInput i0 = RandomInput(rng, 0f);
                PlayerInput i1 = RandomInput(rng, 180f);
                a.Step(i0, i1);
                b.Step(i0, i1);
                if (t == 450)
                {
                    i0.YawDeg += 1f;
                }

                c.Step(i0, i1);
                Assert.Equal(a.StateHash(), b.StateHash());
                diverged |= a.StateHash() != c.StateHash();
            }

            Assert.True(diverged);
            Assert.Equal(a.EventLog.Count, b.EventLog.Count);
        }

        [Fact]
        public void Event_Ids_Are_Sequential_And_The_Decisive_Sequence_Explains_The_Death()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 1.6f), 180f);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 290f, DamageKind.Environment));
            w.GrantShield(1, "SPL-WARD", 5f, 300);
            w.Step(TestWorlds.A.With(InputButtons.Melee), TestWorlds.B);
            TestWorlds.Run(w, 60, TestWorlds.A, TestWorlds.B);
            Assert.True(w.Result.Won(0));

            for (int i = 1; i < w.EventLog.Count; i++)
            {
                Assert.Equal(w.EventLog[i - 1].Id + 1, w.EventLog[i].Id);
                Assert.True(w.EventLog[i - 1].Tick <= w.EventLog[i].Tick);
            }

            var sequence = w.Result.DecisiveSequences[1];
            DamageEvent lethal = null;
            foreach (DuelEvent e in sequence)
            {
                lethal = e is DamageEvent d && d.Lethal ? d : lethal;
            }

            Assert.NotNull(lethal);
            Assert.Equal(DamageKind.Melee, lethal.Kind);
            Assert.Equal(5f, lethal.Resolution.ShieldAbsorbed, 3);
            Assert.IsType<DuelEndEvent>(sequence[sequence.Count - 1]);
            Assert.Contains("FEAT-001", w.Result.CompletedFeats[0]);
            Assert.Contains("FEAT-016", w.Result.CompletedFeats[0]);
            Assert.DoesNotContain("FEAT-004", w.Result.CompletedFeats[0]); // one melee hit of three
        }

        private static DuelEvent[] ToArray(DuelWorld w)
        {
            var array = new DuelEvent[w.EventLog.Count];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = w.EventLog[i];
            }

            return array;
        }

        private static DuelWorld DeterminismWorld()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Solids.Add(TestWorlds.Box(2, new Vec3(2f, 0f, 0f), new Vec3(1f, 2f, 1f), SolidKind.Pillar, 30f));
            layout.Solids.Add(TestWorlds.Ramp(3, new Vec3(-4f, 0f, 0f), 1.5f, 2f, 1.5f, 90f));
            return TestWorlds.Duel(layout, alcoveTicks: 30);
        }

        private static PlayerInput RandomInput(Rng rng, float baseYaw)
        {
            var input = new PlayerInput
            {
                MoveX = rng.Range(-1f, 1f),
                MoveY = rng.Range(-1f, 1f),
                YawDeg = baseYaw + rng.Range(-60f, 60f),
                PitchDeg = rng.Range(-20f, 20f),
                SelectedSlot = rng.NextInt(0, 6),
            };
            InputButtons[] buttons = { InputButtons.Sprint, InputButtons.Jump, InputButtons.Crouch, InputButtons.Dodge, InputButtons.Melee, InputButtons.Guard, InputButtons.SwapFocus };
            float[] chance = { 0.4f, 0.05f, 0.1f, 0.03f, 0.05f, 0.05f, 0.01f };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (rng.Chance(chance[i]))
                {
                    input.Buttons |= buttons[i];
                }
            }

            return input;
        }

        /// <summary>A spell runtime stand-in that runs a script at tick-order step 4.</summary>
        private sealed class ScriptedSpells : ISpellRuntime
        {
            private readonly Action<DuelWorld> _deliveries;
            private DuelWorld _world;

            public ScriptedSpells(Action<DuelWorld> deliveries)
            {
                _deliveries = deliveries;
            }

            public void Attach(DuelWorld world) => _world = world;

            public void UpdateCasting(int player, in PlayerInput input, in CastPermissions permissions)
            {
            }

            public void AdvanceDeliveries() => _deliveries(_world);

            public CastingStatus GetStatus(int player) => CastingStatus.Idle;

            public bool Interrupt(int player, InterruptCause cause) => false;

            public void Hash(ref StateHasher hasher)
            {
            }
        }
    }
}
