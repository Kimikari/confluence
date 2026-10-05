using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Time;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Bots.Profiles;
using Confluence.Reactions;
using Confluence.Sim.Casting;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.World.Materials;
using CastEventKind = Confluence.Casting.CastEventKind;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Hosting.Tests.Wiz002
{
    /// <summary>
    /// The WIZ-002 bot casting profile (<see cref="BramCinderPlanner"/>): spell choice by range and situation from legal
    /// information only, per-spell scripts through the real crest-release grammar, mastery attempts by competence, the
    /// Boiler Orb recast and the Kiln Vent signature.
    /// </summary>
    public sealed class Wiz002BotProfileTests
    {
        private static readonly string[] DefaultKit = { "SPL-009", "SPL-010", "SPL-011", "SPL-012", "SPL-013", "SPL-014" };
        private static readonly string[] LateKit = { "SPL-015", "SPL-016", "SPL-011", "SPL-012", "SPL-013", "SPL-014" };

        // ---- a flat duel of two Brams -----------------------------------------------------------------------------

        private static ArenaLayout Flat(params PropPlacement[] props)
        {
            var layout = new ArenaLayout { Seed = 1UL, Diameter = 40, Center = Vec3.Zero, Boundary = BoundaryContour.Circle(Vec3.Zero, 18f) };
            layout.Solids.Add(new Solid
            {
                Id = 9000, Kind = SolidKind.Floor, Box = YawBox.OnFloor(new Vec3(0f, -1f, 0f), new Vec3(40f, 1f, 40f), 0f),
                Anchored = true, BlocksSight = true, Walkable = true,
            });
            layout.Spawns = new[]
            {
                new SpawnPoint { Side = 0, Position = new Vec3(0f, 0f, -6f), YawDeg = 0f, Alcove = new YawBox(new Vec3(0f, 1.5f, -6f), new Vec3(2f, 1.5f, 2f), 0f) },
                new SpawnPoint { Side = 1, Position = new Vec3(0f, 0f, 6f), YawDeg = 180f, Alcove = new YawBox(new Vec3(0f, 1.5f, 6f), new Vec3(2f, 1.5f, 2f), 180f) },
            };
            layout.Props.AddRange(props);
            return layout;
        }

        private static DuelWorld Duel(string[] kit = null, ArenaLayout layout = null)
        {
            var options = new DuelWorldOptions
            {
                AlcoveTicks = 0,
                SpellRuntime = SpellRuntime.FromCatalogue(HostFixture.Catalogue),
                Catalogue = HostFixture.Catalogue,
            };
            DuelPlayerSetup a = DuelPlayerSetup.FromCatalogue(HostFixture.Catalogue, "A", "WIZ-002", kit ?? DefaultKit);
            DuelPlayerSetup b = DuelPlayerSetup.FromCatalogue(HostFixture.Catalogue, "B", "WIZ-002", DefaultKit);
            var w = new DuelWorld(layout ?? Flat(), a, b, options);
            w.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            return w;
        }

        private static void Place(DuelWorld w, int player, Vec3 feet, float yaw)
        {
            DuelPlayer p = w.Player(player);
            p.Motor = MotorState.At(feet, yaw);
            p.Motor.Grounded = true;
        }

        /// <summary>Bram at (0,0,-6) facing +Z; the opponent <paramref name="distance"/> ahead.</summary>
        private static BotSituation Situation(DuelWorld w, float distance, BotProfile profile = null, bool sees = true)
        {
            Place(w, 0, new Vec3(0f, 0f, -6f), 0f);
            Place(w, 1, new Vec3(0f, 0f, -6f + distance), 180f);
            return new BotSituation
            {
                World = w, Self = 0, Tick = w.Tick + 1, Profile = profile ?? BotProfile.For(BotCompetence.Expert), Sees = sees,
                Opponent = w.Player(1).Position, OpponentHeight = w.Player(1).Motor.Height, Distance = distance,
            };
        }

        private static HashSet<string> Choices(Func<DuelWorld> world, float distance, Action<DuelWorld> setup = null, BotProfile profile = null)
        {
            var chosen = new HashSet<string>(StringComparer.Ordinal);
            for (ulong seed = 1; seed <= 60; seed++)
            {
                DuelWorld w = world();
                BotSituation s = Situation(w, distance, profile);
                setup?.Invoke(w);
                if (new BramCinderPlanner().TryPlanCast(s, new Rng(seed), out BotCastPlan plan)) chosen.Add(plan.SpellId);
            }

            return chosen;
        }

        // ---- discovery and legality --------------------------------------------------------------------------------

        [Fact]
        public void The_WIZ_002_Profile_Is_Found_For_Bram_Only()
        {
            Assert.IsType<BramCinderPlanner>(BotSpellPlanners.For("WIZ-002"));
            Assert.NotSame(BotSpellPlanners.For("WIZ-002"), BotSpellPlanners.For("WIZ-002")); // one per bot and duel
            Assert.Null(BotSpellPlanners.For(null));
            Assert.Null(BotSpellPlanners.For("WIZ-001"));
        }

        [Fact]
        public void Without_Sight_The_Profile_Neither_Casts_Nor_Recasts_Nor_Vents()
        {
            DuelWorld w = Duel();
            BotSituation s = Situation(w, 3f, sees: false);
            var planner = new BramCinderPlanner();
            for (ulong seed = 1; seed <= 20; seed++) Assert.False(planner.TryPlanCast(s, new Rng(seed), out _));
            int slot = 0;
            Assert.Equal(InputButtons.None, planner.ExtraButtons(s, new Rng(1), ref slot));
        }

        // ---- spell choice by range and situation --------------------------------------------------------------------

        [Fact]
        public void Close_Range_Uses_The_Cone_Spells()
        {
            Assert.Equal(new[] { "SPL-010" }, Choices(() => Duel(), 3f).ToArray());
            HashSet<string> late = Choices(() => Duel(LateKit), 3f);
            Assert.Subset(new HashSet<string> { "SPL-015", "SPL-016" }, late);
            Assert.Equal(2, late.Count);
            Assert.Equal(new[] { "SPL-015" }, Choices(() => Duel(LateKit), 5.5f).ToArray()); // beyond Flashforge's reach
        }

        [Fact]
        public void Flashforge_Is_Always_Chosen_When_A_Metal_Prop_Stands_By_The_Opponent()
        {
            // PROP-001 Copper Barrel (metal) beside the opponent 3 m ahead.
            var barrel = new PropPlacement { Id = 1, PropId = "PROP-001", Position = new Vec3(1f, 0f, -3f), CollisionBox = YawBox.OnFloor(new Vec3(1f, 0f, -3f), new Vec3(0.8f, 1f, 0.8f), 0f), HasCollision = true };
            Assert.Equal(new[] { "SPL-016" }, Choices(() => Duel(LateKit, Flat(barrel)), 3f).ToArray());
        }

        [Fact]
        public void Mid_And_Long_Range_Use_Rush_Orb_And_Fireball()
        {
            Assert.Contains("SPL-013", Choices(() => Duel(), 9f));
            HashSet<string> far = Choices(() => Duel(), 14f);
            Assert.Contains("SPL-014", far);
            Assert.Contains("SPL-009", far);
            Assert.DoesNotContain("SPL-010", far);
            Assert.DoesNotContain("SPL-013", far);
        }

        [Fact]
        public void The_Orb_Is_Never_Thrown_Onto_Wet_Ground()
        {
            Func<DuelWorld> pool = () =>
            {
                ArenaLayout layout = Flat();
                layout.Materials.Add(new MaterialPatch { Center = new Vec2(0f, 8f), HalfSize = new Vec2(2f, 2f), Water = 2, NaturalSource = true });
                return Duel(null, layout);
            };
            Assert.True(((ReactionMaterials)pool().Materials).World.IsWetAt(new Vec3(0f, 0f, 8f)));
            HashSet<string> wet = Choices(pool, 14f);
            Assert.DoesNotContain("SPL-014", wet);
            Assert.Contains("SPL-009", wet);
        }

        [Fact]
        public void A_Wet_Bram_Dries_Off_With_Heat_Haze_And_A_Low_Bram_Hides_Behind_Ash_Curtain()
        {
            HashSet<string> wet = Choices(() => Duel(), 9f, w => w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST", -1, Ticks.FromSeconds(5.0))));
            Assert.Equal(new[] { "SPL-011" }, wet.ToArray());

            DuelWorld low = Duel();
            BotSituation s = Situation(low, 9f);
            low.Player(0).Resources.LoseHealth(low.Player(0).Resources.MaxHealth * 0.7f);
            Assert.True(new BramCinderPlanner().TryPlanCast(s, new Rng(3), out BotCastPlan plan));
            Assert.Equal("SPL-012", plan.SpellId);
            Assert.Equal(BotAimMode.Point, plan.Aim);
            Assert.InRange(Vec3.Distance(plan.AimPoint, new Vec3(0f, 0f, -2f)), 0f, 0.6f); // between the two, 4 m ahead
        }

        // ---- scripts through the real grammar, mastery by competence -----------------------------------------------

        public static IEnumerable<object[]> PlanCases()
        {
            yield return new object[] { "SPL-010", false, 3f };
            yield return new object[] { "SPL-009", false, 14f };
            yield return new object[] { "SPL-014", false, 14f };
            yield return new object[] { "SPL-013", false, 9f };
            yield return new object[] { "SPL-015", true, 5.5f };
            yield return new object[] { "SPL-016", true, 3f };
        }

        [Theory]
        [MemberData(nameof(PlanCases))]
        public void Every_Planned_Cast_Commits_Its_Spell_Through_The_Crest_Grammar(string spellId, bool late, float distance)
        {
            int found = 0;
            for (ulong seed = 1; seed <= 60 && found < 3; seed++)
            {
                DuelWorld w = Duel(late ? LateKit : null);
                BotSituation s = Situation(w, distance);
                if (!new BramCinderPlanner().TryPlanCast(s, new Rng(seed), out BotCastPlan plan) || plan.SpellId != spellId) continue;
                found++;
                Assert.Equal(w.Player(0).Setup.EquippedSpellIds.IndexOf(spellId), plan.Slot);
                CastEvent commit = RunPlan(w, plan);
                Assert.Equal(spellId, commit.SpellId);
                Assert.Equal(plan.Script.ReleaseTick >= 24 && plan.Script.ReleaseTick <= 30, commit.Mastery.IsMastered);
            }

            Assert.True(found > 0, spellId + " was never planned");
        }

        [Fact]
        public void Self_And_Ground_Spells_Commit_Too()
        {
            DuelWorld w = Duel();
            BotSituation s = Situation(w, 9f);
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST", -1, Ticks.FromSeconds(5.0)));
            Assert.True(new BramCinderPlanner().TryPlanCast(s, new Rng(1), out BotCastPlan haze));
            Assert.Equal("SPL-011", RunPlan(w, haze).SpellId);
            Assert.False(w.Player(0).Statuses.IsWet);

            DuelWorld low = Duel();
            BotSituation t = Situation(low, 9f);
            low.Player(0).Resources.LoseHealth(low.Player(0).Resources.MaxHealth * 0.7f);
            Assert.True(new BramCinderPlanner().TryPlanCast(t, new Rng(1), out BotCastPlan ash));
            Assert.Equal("SPL-012", RunPlan(low, ash).SpellId);
        }

        [Fact]
        public void Novices_Cast_Normally_And_Better_Bots_Sometimes_Attempt_The_Crest()
        {
            Assert.Equal(0f, BramCinderPlanner.MasteryAttemptChance(BotProfile.For(BotCompetence.Novice)));
            Assert.True(BramCinderPlanner.MasteryAttemptChance(BotProfile.For(BotCompetence.Apprentice)) > 0f);
            Assert.True(BramCinderPlanner.MasteryAttemptChance(BotProfile.For(BotCompetence.Expert)) >
                BramCinderPlanner.MasteryAttemptChance(BotProfile.For(BotCompetence.Adept)));
            Assert.True(BramCinderPlanner.MasteryAttemptChance(BotProfile.For(BotCompetence.Expert)) < 1f); // sometimes, not always

            int mastered = 0, normal = 0;
            BotProfile exact = BotProfile.For(BotCompetence.Expert).Clone();
            exact.CastTimingJitterTicks = 0;
            for (ulong seed = 1; seed <= 30; seed++)
            {
                DuelWorld w = Duel();
                if (!new BramCinderPlanner().TryPlanCast(Situation(w, 14f, exact), new Rng(seed), out BotCastPlan plan)) continue;
                CastEvent commit = RunPlan(w, plan);
                if (commit.Mastery.IsMastered) mastered++;
                else normal++;
            }

            Assert.True(mastered > 0 && normal > 0, mastered + " mastered, " + normal + " normal");

            // A Novice never attempts the crest: it releases around the normal 0.3 s (its timing jitter may still drift).
            var novice = new BramCinderPlanner();
            for (ulong seed = 1; seed <= 30; seed++)
            {
                DuelWorld w = Duel();
                if (!novice.TryPlanCast(Situation(w, 14f, BotProfile.For(BotCompetence.Novice)), new Rng(seed), out BotCastPlan plan)) continue;
                Assert.InRange(plan.Script.ReleaseTick, CrestReleaseScript.NormalReleaseTick - 6, CrestReleaseScript.NormalReleaseTick + 6);
                Assert.Equal(CastEventKind.Committed, RunPlanEvent(w, plan));
            }

            Assert.Equal(0, novice.MasteryAttempts);
        }

        // ---- recasts and the signature -------------------------------------------------------------------------------

        [Fact]
        public void The_Orb_Is_Detonated_By_Recast_Once_The_Opponent_Is_Near_It()
        {
            DuelWorld w = Duel();
            BotSituation s = Situation(w, 8f);
            int orbSlot = w.Player(0).Setup.EquippedSpellIds.IndexOf("SPL-014");
            BotCastPlan plan = null;
            for (ulong seed = 1; plan == null || plan.SpellId != "SPL-014"; seed++)
            {
                DuelWorld v = Duel();
                s = Situation(v, 8f);
                new BramCinderPlanner().TryPlanCast(s, new Rng(seed), out plan);
                w = v;
            }

            RunPlan(w, plan);
            BoilerOrbEntity orb = null;
            for (int i = 0; i < 90 && (orb == null || !orb.Settled); i++)
            {
                w.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
                orb = ((SpellRuntime)w.Spells).Entities.OfType<BoilerOrbEntity>().FirstOrDefault();
            }

            Assert.NotNull(orb);
            Assert.True(orb.Settled);
            var planner = new BramCinderPlanner();
            int slot = 0;
            BotSituation far = Situation(w, 14f);
            Assert.Equal(InputButtons.None, planner.ExtraButtons(far, new Rng(1), ref slot) & InputButtons.Recast);

            Place(w, 1, new Vec3(orb.Position.X + 1f, 0f, orb.Position.Z), 180f);
            BotSituation near = far;
            near.Opponent = w.Player(1).Position;
            near.Distance = Vec2.Distance(w.Player(0).Position.XZ(), near.Opponent.XZ());
            Assert.Equal(InputButtons.Recast, planner.ExtraButtons(near, new Rng(1), ref slot));
            Assert.Equal(orbSlot, slot);

            w.Step(PlayerInput.Idle(0f).Selecting(slot).With(InputButtons.Recast), PlayerInput.Idle(180f));
            for (int i = 0; i < 5; i++) w.Step(PlayerInput.Idle(0f).Selecting(slot), PlayerInput.Idle(180f));
            Assert.Contains(w.EventLog.OfType<DamageEvent>(), d => d.Target == 1 && d.SourceId == "SPL-014" && d.Dealt > 0f);
        }

        [Fact]
        public void Kiln_Vent_Is_Pressed_Only_With_The_Opponent_In_The_Cone_And_The_Signature_Ready()
        {
            DuelWorld w = Duel();
            var planner = new BramCinderPlanner();
            int slot = 0;
            Assert.Equal(InputButtons.Signature, planner.ExtraButtons(Situation(w, 3f), new Rng(1), ref slot));
            Assert.Equal(InputButtons.None, planner.ExtraButtons(Situation(w, 6f), new Rng(1), ref slot));

            BotSituation behind = Situation(w, 3f);
            Place(w, 0, new Vec3(0f, 0f, -6f), 180f); // facing away
            Assert.Equal(InputButtons.None, planner.ExtraButtons(behind, new Rng(1), ref slot));

            // Pressed for real: the vent commits, hits for 28 thermal, and is not offered again during its cooldown.
            Place(w, 0, new Vec3(0f, 0f, -6f), 0f);
            BotSituation s = Situation(w, 3f);
            w.Step(PlayerInput.Idle(0f).With(planner.ExtraButtons(s, new Rng(1), ref slot)), PlayerInput.Idle(180f));
            for (int i = 0; i < 30; i++) w.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            Assert.Contains(w.EventLog.OfType<DamageEvent>(), d => d.Target == 1 && d.SourceId == "WIZ-002" && Math.Abs(d.Dealt - 28f) < 1e-3f);
            Assert.Equal(InputButtons.None, planner.ExtraButtons(Situation(w, 3f), new Rng(1), ref slot));
        }

        private static CastEventKind RunPlanEvent(DuelWorld w, BotCastPlan plan) => RunPlan(w, plan).Kind;

        /// <summary>Feeds a plan's script (aim and slot as the bot would) into the duel and returns the commit event.</summary>
        private static CastEvent RunPlan(DuelWorld w, BotCastPlan plan)
        {
            DuelPlayer me = w.Player(0);
            Vec3 target = plan.Aim == BotAimMode.Point ? plan.AimPoint
                : plan.Aim == BotAimMode.Feet ? w.Player(1).Position
                : w.Player(1).Position + new Vec3(0f, w.Player(1).Motor.Height * 0.6f, 0f);
            Vec3 aim = target - me.Eye;
            PlayerInput input = PlayerInput.Idle(0f).Facing(CMath.YawOf(aim), CMath.PitchOf(aim)).Selecting(plan.Slot);
            int start = w.EventLog.Count;
            for (int k = 0; k <= plan.Script.ReleaseTick + 40; k++)
            {
                w.Step(input.With(plan.Script.ButtonsAt(k)), PlayerInput.Idle(180f));
                CastDuelEvent commit = w.EventLog.Skip(start).OfType<CastDuelEvent>().FirstOrDefault(c => c.Player == 0 && c.Cast.Kind == CastEventKind.Committed);
                if (commit != null)
                {
                    for (int i = 0; i < 50; i++) w.Step(input, PlayerInput.Idle(180f));
                    return commit.Cast;
                }
            }

            throw new InvalidOperationException(plan.SpellId + " never committed");
        }
    }
}
