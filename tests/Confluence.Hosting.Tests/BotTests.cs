using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Time;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Duels;
using Confluence.Match;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Hosting.Tests
{
    /// <summary>Bots (spec L381): bounded competence, sight-only perception, and wizard input scripts.</summary>
    public sealed class BotTests
    {
        [Fact]
        public void Competence_Settings_Are_Bounded_And_Ordered()
        {
            BotProfile[] profiles = { BotProfile.For(BotCompetence.Novice), BotProfile.For(BotCompetence.Apprentice), BotProfile.For(BotCompetence.Adept), BotProfile.For(BotCompetence.Expert) };
            int floor = Ticks.FromSeconds(BotProfile.ReactionFloorSec);
            foreach (BotProfile p in profiles)
            {
                Assert.True(p.ReactionMinTicks >= floor);                // humanlike floor (L381)
                Assert.True(p.ReactionMaxTicks >= p.ReactionMinTicks);
                Assert.True(p.EffectiveAimErrorDeg >= BotProfile.MinAimErrorDeg);
                Assert.True(p.GuardChance < 1f);                          // never parries every time
                Assert.True(p.TurnRateDegPerTick > 0f);
            }

            for (int i = 1; i < profiles.Length; i++)
            {
                Assert.True(profiles[i].ReactionMinTicks <= profiles[i - 1].ReactionMinTicks);
                Assert.True(profiles[i].AimErrorDeg < profiles[i - 1].AimErrorDeg);
                Assert.True(profiles[i].GuardChance >= profiles[i - 1].GuardChance);
            }

            Assert.True(profiles[0].TelegraphTicks > 0);   // Novice telegraphs decisions
            Assert.False(profiles[0].AttemptsMastery);     // and uses normal casts
            Assert.True(profiles[1].StrafesInCombat);      // Apprentice keeps moving
            Assert.True(profiles[2].StaminaReserve >= 25f); // Adept manages stamina for a guard
            Assert.True(profiles[3].LeadsTargets);
            // Even a forced-low profile keeps the floor.
            BotProfile reckless = profiles[3].Clone();
            reckless.ReactionMinSec = 0f;
            reckless.ReactionMaxSec = 0f;
            Assert.Equal(floor, reckless.ReactionMinTicks);
            Assert.Equal(floor, reckless.ReactionMaxTicks);
        }

        [Fact]
        public void Crest_Script_Casts_Normally_Or_Hits_The_Crest_Through_The_Real_Grammar()
        {
            Assert.False(RunScript("WIZ-002", Exact(attemptMastery: false)).Mastery.IsMastered);
            Assert.True(RunScript("WIZ-002", Exact(attemptMastery: true)).Mastery.IsMastered); // crest window 0.40-0.50 s
            AssertJitteredScriptsAlwaysCommit("WIZ-002");
        }

        [Fact]
        public void Pressure_Script_Stays_In_The_Band_With_A_Vent_Through_The_Real_Grammar()
        {
            CastEvent normal = RunScript("WIZ-010", Exact(attemptMastery: false));
            Assert.False(normal.Mastery.IsMastered);
            CastEvent mastered = RunScript("WIZ-010", Exact(attemptMastery: true));
            Assert.True(mastered.Mastery.IsMastered, mastered.Mastery.Miss.ToString()); // 45-55 for the final 0.25 s
            var script = (PressureMeterScript)BotCastScript.For("WIZ-010", Exact(true), new Rng(1));
            Assert.True(script.Vents);
            Assert.Equal(InputButtons.Primary | InputButtons.ModifierA, script.ButtonsAt(PressureMeterScript.VentStartTick));
            AssertJitteredScriptsAlwaysCommit("WIZ-010");
        }

        [Fact]
        public void Bots_Never_Perceive_An_Opponent_Hidden_By_The_Sight_Query()
        {
            // An obscuring volume everywhere: the Sim's acquisition query (L167) never grants sight, so the bots must not
            // aim, cast, or strike at each other, even though the world itself knows both positions.
            MatchHostSettings settings = HostFixture.DuelSettings(HostFixture.SmallDuelSeed);
            settings.WorldFactory = new FogWorldFactory();
            MatchHost host = HostFixture.Host(settings);
            BotPlayer a = host.AddBot(0, BotProfile.For(BotCompetence.Expert));
            BotPlayer b = host.AddBot(1, BotProfile.For(BotCompetence.Expert));
            var stats = new List<BotDuelStats>();
            host.Duels.Finished += r => { stats.Add(a.Input.Stats); stats.Add(b.Input.Stats); };
            Assert.True(host.Start().Started);
            HostFixture.RunUntilPhase(host, MatchPhase.Results, 60 * 60 * 5);

            Assert.Equal(2, stats.Count);
            Assert.All(stats, s =>
            {
                Assert.Equal(0, s.TicksWithSight);
                Assert.Equal(0, s.Casts);
                Assert.Equal(0, s.MeleePresses);
                Assert.Equal(0, s.GuardsPlanned);
            });
        }

        [Fact]
        public void Bot_Inputs_Come_Only_From_The_Bot_Stream()
        {
            Assert.NotEqual(RngStreams.SeedFor(9, RngStream.Bot, 3, 1), RngStreams.SeedFor(9, RngStream.Bot, 3, BotStreams.EconomySubBase + 1));
            Assert.Equal(BotStreams.ForDuel(9, 3, 1).NextULong(), new Rng(RngStreams.SeedFor(9, RngStream.Bot, 3, 1)).NextULong());
            Assert.NotEqual(RngStreams.SeedFor(9, RngStream.Bot, 3, 1), RngStreams.SeedFor(9, RngStream.Offer, 3, 1));
        }

        private static BotProfile Exact(bool attemptMastery)
        {
            BotProfile p = BotProfile.For(BotCompetence.Expert).Clone();
            p.CastTimingJitterTicks = 0;
            p.AttemptsMastery = attemptMastery;
            return p;
        }

        private static void AssertJitteredScriptsAlwaysCommit(string wizardId)
        {
            foreach (BotCompetence c in new[] { BotCompetence.Novice, BotCompetence.Apprentice, BotCompetence.Adept, BotCompetence.Expert })
            {
                for (ulong seed = 1; seed <= 25; seed++)
                {
                    BotCastScript script = BotCastScript.For(wizardId, BotProfile.For(c), new Rng(seed));
                    Assert.Equal(CastEventKind.Committed, Run(wizardId, script).Kind);
                }
            }
        }

        private static CastEvent RunScript(string wizardId, BotProfile profile) => Run(wizardId, BotCastScript.For(wizardId, profile, new Rng(7)));

        /// <summary>Feeds a script's per-tick buttons to the wizard's real grammar and returns the commit event.</summary>
        private static CastEvent Run(string wizardId, BotCastScript script)
        {
            IGrammar grammar = wizardId == "WIZ-002" ? (IGrammar)new PressureReleaseGrammar() : new PressureMeterGrammar();
            string spellId = HostFixture.Catalogue.Wizard(wizardId).DefaultSpellIds[0];
            var host = new ScriptHost();
            var machine = new CastMachine(1, host, grammar, new[] { CastableSpell.FromDef(HostFixture.Catalogue.Spell(spellId)) });
            var events = new List<CastEvent>();
            InputButtons previous = InputButtons.None;
            for (int tick = 0; tick < 120; tick++)
            {
                InputButtons held = script.ButtonsAt(tick);
                host.CurrentTick = tick;
                CastInputFrame frame = CastInputFrame.FromHeld(tick, previous, held);
                frame.SelectSlot = CastInputFrame.NoSelection;
                machine.Step(frame, events);
                previous = held;
            }

            return events.First(e => e.Kind == CastEventKind.Committed);
        }

        private sealed class ScriptHost : ICastHost
        {
            public int CurrentTick;
            public float Mana => 100f;
            public bool IsSilenced => false;
            public bool IsHardControlled => false;
            public bool IsWarded => false;
            public bool IsActionLocked => false;
            public bool TryPayMana(float amount) => true;
            public void RefundMana(float amount) { }
            public int CooldownRemainingTicks(string spellId) => 0;
            public void StartCooldown(CastableSpell spell) { }
            public CastModifiers ModifiersFor(CastableSpell spell) => default;
        }

        private sealed class FogWorldFactory : ISimWorldFactory
        {
            public DuelWorld Create(ArenaLayout layout, DuelPlayerSetup side0, DuelPlayerSetup side1, int alcoveTicks, int combatTicks, bool keepEventLog) =>
                new DuelWorld(layout, side0, side1, new DuelWorldOptions { AlcoveTicks = alcoveTicks, CombatTicks = combatTicks, KeepEventLog = keepEventLog, Materials = new Fog() });
        }

        private sealed class Fog : IWorldMaterials
        {
            public void QueueDeposit(in WorldDeposit deposit) { }
            public void Step(IMaterialsContext context) { }
            public float ObscuringPathLength(Vec3 from, Vec3 to) => 100f;
            public SurfaceInfo QuerySurface(Vec3 feet) => SurfaceInfo.Normal;
            public void Hash(ref StateHasher hasher) { }
        }
    }
}
