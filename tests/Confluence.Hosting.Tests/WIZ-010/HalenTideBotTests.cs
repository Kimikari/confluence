using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Duels;
using Confluence.Match;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.World.Materials;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Hosting.Tests.HalenTide
{
    /// <summary>The WIZ-010 bot casting profile: per-spell scripts through the real grammar, choices, and a hosted duel.</summary>
    public sealed class HalenTideBotTests
    {
        private static readonly string[] SixA = { "SPL-073", "SPL-074", "SPL-075", "SPL-076", "SPL-077", "SPL-078" };
        private static readonly string[] SixB = { "SPL-079", "SPL-080", "SPL-073", "SPL-074", "SPL-075", "SPL-076" };

        private static HalenTideBotPlanner Planner => (HalenTideBotPlanner)BotKitPlanners.For("WIZ-010");

        private static ArenaLayout Flat()
        {
            var layout = new ArenaLayout { Seed = 7UL, Diameter = 40, Center = Vec3.Zero, Boundary = BoundaryContour.Circle(Vec3.Zero, 18f) };
            layout.Solids.Add(new Solid { Id = 1, Kind = SolidKind.Floor, Box = YawBox.OnFloor(new Vec3(0f, -1f, 0f), new Vec3(40f, 1f, 40f), 0f), Anchored = true, BlocksSight = true, Walkable = true });
            layout.Spawns = new[]
            {
                new SpawnPoint { Side = 0, Position = new Vec3(0f, 0f, -6f), YawDeg = 0f, Alcove = new YawBox(new Vec3(0f, 1.5f, -6f), new Vec3(2f, 1.5f, 2f), 0f) },
                new SpawnPoint { Side = 1, Position = new Vec3(0f, 0f, 6f), YawDeg = 180f, Alcove = new YawBox(new Vec3(0f, 1.5f, 6f), new Vec3(2f, 1.5f, 2f), 180f) },
            };
            return layout;
        }

        private static DuelWorld World(string[] spells, string other = "WIZ-002")
        {
            DuelPlayerSetup halen = DuelPlayerSetup.FromCatalogue(HostFixture.Catalogue, "H", "WIZ-010", spells);
            DuelPlayerSetup rival = DuelPlayerSetup.FromCatalogue(HostFixture.Catalogue, "R", other);
            return new SimWorldFactory(HostFixture.Catalogue).Create(Flat(), halen, rival, 0, 90 * Ticks.PerSecond, true);
        }

        private static void Place(DuelWorld w, int player, Vec3 feet, float yaw)
        {
            w.Player(player).Motor = MotorState.At(feet, yaw);
            w.Player(player).Motor.Grounded = true;
        }

        private static BotKitView View(DuelWorld w, BotCompetence competence = BotCompetence.Expert, bool casting = false)
        {
            DuelPlayer me = w.Player(0);
            DuelPlayer them = w.Player(1);
            return new BotKitView
            {
                World = w, Self = 0, Me = me, Tick = w.Tick + 1, Profile = BotProfile.For(competence), Sees = w.CanAcquire(0, 1),
                OpponentPosition = them.Position, OpponentVelocity = them.Motor.Velocity, Distance = Vec2.Distance(me.Position.XZ(), them.Position.XZ()),
                OpponentCasting = casting, OpponentWizardId = them.Setup.WizardId,
            };
        }

        /// <summary>Runs a plan's script for player 0 with the planner's aim; returns the commitment (or null).</summary>
        private static CastDuelEvent RunPlan(DuelWorld w, BotCastPlan plan, BotCastScript script)
        {
            DuelPlayer me = w.Player(0);
            Vec3 target = plan.Aim == BotAimKind.Point ? plan.Point : plan.Aim == BotAimKind.OpponentFeet ? w.Player(1).Position : w.Player(1).Position + new Vec3(0f, 1.1f, 0f);
            Vec3 aim = target - me.Eye;
            PlayerInput look = PlayerInput.Idle(CMath.YawOf(aim)).Facing(CMath.YawOf(aim), CMath.PitchOf(aim)).Selecting(plan.Slot);
            int before = w.EventLog.Count;
            for (int k = 0; k <= script.ReleaseTick + 1; k++) w.Step(look.With(script.ButtonsAt(k)), PlayerInput.Idle(180f));
            return w.EventLog.Skip(before).OfType<CastDuelEvent>().FirstOrDefault(e => e.Player == 0 && e.Cast.Kind == CastEventKind.Committed);
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Every_Spell_And_The_Signature_Cast_Through_The_Bot_Scripts_In_A_Real_Duel()
        {
            foreach (string[] six in new[] { SixA, SixB })
            {
                foreach (string spell in six)
                {
                    foreach (BotCompetence c in new[] { BotCompetence.Novice, BotCompetence.Apprentice, BotCompetence.Adept, BotCompetence.Expert })
                    {
                        for (ulong seed = 1; seed <= 4; seed++)
                        {
                            DuelWorld w = World(six);
                            var plan = new BotCastPlan { Slot = HalenTideBotPlanner.SlotOf(w.Player(0), spell), SpellId = spell, Aim = BotAimKind.Chest, AttemptMastery = seed % 2 == 0 };
                            if (spell == "SPL-077") { plan.Aim = BotAimKind.Point; plan.Point = new Vec3(0f, 0f, -2f); }
                            if (spell == "SPL-080") { plan.Aim = BotAimKind.Point; plan.Point = new Vec3(0f, 0f, 0f); }
                            if (spell == "SPL-076" || spell == "SPL-078") plan.Aim = BotAimKind.OpponentFeet;
                            BotCastScript script = Planner.ScriptFor(plan, BotProfile.For(c), new Rng(seed));
                            CastDuelEvent commit = RunPlan(w, plan, script);
                            Assert.True(commit != null && commit.Cast.SpellId == spell, spell + " " + c + " seed " + seed + " did not commit");
                        }
                    }

                    // An exact mastery attempt masters it (15% less mana, the wizard record).
                    BotProfile exact = BotProfile.For(BotCompetence.Expert).Clone();
                    exact.CastTimingJitterTicks = 0;
                    DuelWorld m = World(six);
                    var mastered = new BotCastPlan { Slot = HalenTideBotPlanner.SlotOf(m.Player(0), spell), SpellId = spell, Aim = BotAimKind.Chest, AttemptMastery = true };
                    if (spell == "SPL-077") { mastered.Aim = BotAimKind.Point; mastered.Point = new Vec3(0f, 0f, -2f); }
                    if (spell == "SPL-080") { mastered.Aim = BotAimKind.Point; mastered.Point = Vec3.Zero; }
                    CastDuelEvent masteredCommit = RunPlan(m, mastered, Planner.ScriptFor(mastered, exact, new Rng(3)));
                    Assert.True(masteredCommit.Cast.Mastery.IsMastered, spell + " " + masteredCommit.Cast.Mastery.Miss);
                    Assert.Equal(m.Player(0).Caster.FormFor(spell).Mana * 0.85f, masteredCommit.Cast.Mana, 2);
                }
            }

            // The signature: a one-tick press of its own binding.
            DuelWorld s = World(SixA);
            Place(s, 1, new Vec3(0f, 0f, -4.5f), 180f);
            BotCastPlan sig = Planner.Choose(View(s), new Rng(1));
            Assert.True(sig.Signature);
            CastDuelEvent eddy = RunPlan(s, sig, Planner.ScriptFor(sig, BotProfile.For(BotCompetence.Novice), new Rng(1)));
            Assert.Equal("WIZ-010", eddy.Cast.SpellId);
        }

        [Fact]
        public void Novices_Never_Attempt_Mastery_And_Experts_Do_Only_Sometimes()
        {
            Assert.Equal(0f, HalenTideBotPlanner.MasteryChance(BotCompetence.Novice));
            Assert.True(HalenTideBotPlanner.MasteryChance(BotCompetence.Expert) < 1f);
            DuelWorld w = World(SixA);
            var rng = new Rng(11);
            int attempts = 0;
            for (int i = 0; i < 200; i++)
            {
                BotCastPlan p = Planner.Choose(View(w, BotCompetence.Expert), rng);
                if (p.AttemptMastery) attempts++;
                Assert.False(Planner.Choose(View(w, BotCompetence.Novice), rng).AttemptMastery);
            }

            Assert.InRange(attempts, 100, 190);
        }

        [Fact]
        public void The_Planner_Chooses_By_Range_And_Situation()
        {
            var rng = new Rng(5);

            // Close: the eddy, then (signature spent) the hammer.
            DuelWorld close = World(SixA);
            Place(close, 1, new Vec3(0f, 0f, -4f), 180f);
            Assert.True(Planner.Choose(View(close), rng).Signature);
            Place(close, 1, new Vec3(0f, 0f, -3.1f), 180f);
            Assert.Equal("SPL-074", Planner.Choose(View(close), rng).SpellId);

            // A visible windup at range: the buckler.
            DuelWorld windup = World(SixA);
            Assert.Equal("SPL-075", Planner.Choose(View(windup, casting: true), rng).SpellId);

            // Mid range: the jet (with Deepglass Prison equipped, sometimes the prison).
            DuelWorld mid = World(SixA);
            Assert.Equal("SPL-073", Planner.Choose(View(mid), rng).SpellId);
            DuelWorld midB = World(SixB);
            var picks = new HashSet<string>();
            for (int i = 0; i < 40; i++) picks.Add(Planner.Choose(View(midB), rng).SpellId);
            Assert.Contains("SPL-079", picks);
            Assert.Contains("SPL-073", picks);

            // Far beyond the jet: a stream toward them, aimed at the ground in front of the bot.
            DuelWorld far = World(SixA);
            Place(far, 1, new Vec3(0f, 0f, 14f), 180f);
            BotCastPlan stream = Planner.Choose(View(far), rng);
            Assert.Equal("SPL-078", stream.SpellId);
            Assert.Equal(BotAimKind.Point, stream.Aim);
            Assert.True(stream.Point.Z > -6f && stream.Point.Z < -3f);

            // Heat at the bot's feet: Seal the Spring on it; heat by the opponent: the steam screen with Water Pool.
            DuelWorld hazard = World(SixB);
            Deposit(hazard, Material.Heat, new Vec2(0.25f, -4.75f));
            BotCastPlan seal = Planner.Choose(View(hazard), rng);
            Assert.Equal("SPL-080", seal.SpellId);
            Assert.True(Vec3.DistanceXZ(seal.Point, new Vec3(0.25f, 0f, -4.75f)) < 0.6f);

            DuelWorld steam = World(SixA);
            Deposit(steam, Material.Heat, new Vec2(0.25f, 4.25f));
            BotCastPlan pool = Planner.Choose(View(steam), rng);
            Assert.Equal("SPL-076", pool.SpellId);
            Assert.Equal(BotAimKind.Point, pool.Aim);

            // Unseen or out of mana: hold fire.
            DuelWorld broke = World(SixA);
            broke.Player(0).Resources.TrySpendMana(broke.Player(0).Resources.Mana, broke.Tick, false);
            Assert.Null(Planner.Choose(View(broke), rng));
            BotKitView blind = View(World(SixA));
            blind.Sees = false;
            Assert.Null(Planner.Choose(blind, rng));
        }

        private static void Deposit(DuelWorld w, Material material, Vec2 at)
        {
            ((ReactionMaterials)w.Materials).World.Deposit(new MaterialDeposit
            {
                Material = material, Strength = 2, Footprint = Footprint.Point(at), SurfaceY = 0f, LifetimeTicks = 6 * Ticks.PerSecond, Owner = 1, SourceEventId = 0,
            });
            w.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Hosted_Bot_Duels_Cast_Halen_s_Kit_Through_The_Planner()
        {
            // Halen against Halen on three small arenas: every committed cast is a real input script judged by the grammar.
            var cast = new List<string>();
            int mastered = 0;
            int duels = 0;
            ulong start = 100;
            for (int round = 0; round < 3; round++)
            {
                ulong seed = HostFixture.SeedWithFirstDiameter(MatchMode.Duel, 24, start);
                start = seed + 1;
                MatchHostSettings settings = HostFixture.DuelSettings(seed, wizard0: "WIZ-010", wizard1: "WIZ-010");
                var factory = new RecordingFactory(new SimWorldFactory(HostFixture.Catalogue));
                settings.WorldFactory = factory;
                MatchHost host = HostFixture.Host(settings);
                host.AddBot(0, BotProfile.For(BotCompetence.Expert));
                host.AddBot(1, BotProfile.For(BotCompetence.Adept));
                Assert.True(host.Start().Started);
                for (int i = 0; i < 60 * 60 * 6 && host.Flow.Phase != MatchPhase.Results; i++)
                {
                    host.Tick();
                    DuelWorld w = factory.Last;
                    if (w == null) continue;
                    foreach (DuelEvent e in w.TickEvents)
                    {
                        if (!(e is CastDuelEvent c) || c.Cast.Kind != CastEventKind.Committed) continue;
                        cast.Add(c.Cast.SpellId);
                        if (c.Cast.Mastery.IsMastered) mastered++;
                    }
                }

                Assert.Equal(MatchPhase.Results, host.Flow.Phase);
                duels += factory.Count;
            }

            List<string> distinct = cast.Distinct().OrderBy(s => s).ToList();
            Assert.True(distinct.Count >= 3, "Halen's bots cast only " + string.Join(",", cast) + " in " + duels + " duels");
            Assert.All(distinct, id => Assert.True(id == "WIZ-010" || HostFixture.Catalogue.Wizard("WIZ-010").SpellIds.Contains(id), id));
            Assert.True(mastered > 0, "no mastered cast in " + cast.Count);
            Assert.True(mastered < cast.Count, "every cast mastered");
        }


        private sealed class RecordingFactory : ISimWorldFactory
        {
            private readonly ISimWorldFactory _inner;

            public RecordingFactory(ISimWorldFactory inner)
            {
                _inner = inner;
            }

            public DuelWorld Last { get; private set; }

            public int Count { get; private set; }

            public DuelWorld Create(ArenaLayout layout, DuelPlayerSetup side0, DuelPlayerSetup side1, int alcoveTicks, int combatTicks, bool keepEventLog)
            {
                DuelWorld w = _inner.Create(layout, side0, side1, alcoveTicks, combatTicks, keepEventLog);
                Last = w;
                Count++;
                return w;
            }
        }
    }
}
