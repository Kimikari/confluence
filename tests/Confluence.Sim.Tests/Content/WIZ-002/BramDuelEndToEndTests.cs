using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>
    /// A scripted duel of two Bram Cinders in one DuelWorld: player 0 with the default six (SPL-009 ... SPL-014), player 1
    /// with Flame Lash and Flashforge in place of Fireball and Cinder Sweep, so all eight spells, both signatures and both
    /// passives run through real per-tick input and the crest-release grammar until one Bram falls.
    /// </summary>
    public class BramDuelEndToEndTests
    {
        private static PlayerInput AimA(DuelWorld w, Vec3 p, int slot) => SpellTestKit.Aim(w, 0, p).Selecting(slot);

        private static PlayerInput AimB(DuelWorld w, Vec3 p, int slot) => SpellTestKit.Aim(w, 1, p).Selecting(slot);

        private static Vec3 Chest(DuelWorld w, int p) => w.Player(p).Position + new Vec3(0f, 1.1f, 0f);

        private static Vec3 Feet(DuelWorld w, int p) => w.Player(p).Position;

        private static void CastA(DuelWorld w, int slot, Vec3 at, int hold = BramKit.NormalHold)
        {
            PlayerInput a = AimA(w, at, slot);
            for (int i = 0; i < hold; i++) w.Step(a.With(InputButtons.Primary), TestWorlds.B);
            for (int i = 0; i < 70; i++) w.Step(a, TestWorlds.B);
        }

        private static void CastB(DuelWorld w, int slot, Vec3 at, int hold = BramKit.NormalHold)
        {
            PlayerInput b = AimB(w, at, slot);
            for (int i = 0; i < hold; i++) w.Step(TestWorlds.A, b.With(InputButtons.Primary));
            for (int i = 0; i < 70; i++) w.Step(TestWorlds.A, b);
        }

        private static bool Committed(DuelWorld w, int player, string spell) =>
            BramKit.Casts(w, CastEventKind.Committed, player).Any(c => c.Cast.SpellId == spell);

        private static bool Damaged(DuelWorld w, int target, string spell) => BramKit.DamageTo(w, target, spell).Any(d => d.Dealt > 0f);

        [Fact]
        [Verifies("WIZ-002")]
        public void Two_Brams_Duel_With_All_Eight_Spells_Both_Signatures_And_Both_Passives()
        {
            DuelPlayerSetup late = DuelPlayerSetup.FromCatalogue(BramKit.Catalogue, "B", "WIZ-002", BramKit.LateKit);
            DuelWorld w = BramKit.Duel(BramKit.Bram(), other: late);
            Assert.IsType<PressureReleaseGrammar>(w.Player(0).Caster.Loadout.Grammar);
            Assert.IsType<PressureReleaseGrammar>(w.Player(1).Caster.Loadout.Grammar);
            BramKit.Run(w, 2);

            // 10 m apart: A's Fireball (crest) lands; B's Tempered Hands answers the thermal hit with its 20 shield.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 4f), 180f);
            CastA(w, 0, Chest(w, 1), BramKit.CrestHold);
            Assert.True(Damaged(w, 1, "SPL-009"));
            Assert.True(BramKit.Casts(w, CastEventKind.Committed, 0).Single(c => c.Cast.SpellId == "SPL-009").Cast.Mastery.IsMastered);
            Assert.Contains(SpellTestKit.Events<ShieldEvent>(w), s => s.Player == 1 && s.SourceId == "WIZ-002" && s.Change == ShieldChange.Gained);

            // B hazes (a self buff), A throws a Boiler Orb under B and detonates it by recast.
            CastB(w, 2, Chest(w, 0));
            Assert.Contains(BramKit.Spell(w, SpellEventKind.BuffStarted), e => e.SpellId == "SPL-011");
            CastA(w, 5, Feet(w, 1) + new Vec3(0f, 0f, -0.8f));
            BoilerOrbEntity orb = BramKit.Entity<BoilerOrbEntity>(w);
            Assert.NotNull(orb);
            BramKit.RunUntil(w, () => orb.Settled, 60);
            BramKit.Recast(w, 5);
            BramKit.Run(w, 30);
            Assert.True(Damaged(w, 1, "SPL-014"));

            // Close range: A sweeps, B lashes and smashes Flashforge at A's feet; A answers with Kiln Vent.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -3f), 180f);
            BramKit.Run(w, 60);
            CastA(w, 1, Chest(w, 1));
            Assert.True(Damaged(w, 1, "SPL-010"));
            CastB(w, 0, Chest(w, 0));
            Assert.True(Damaged(w, 0, "SPL-015"));
            Assert.Contains(SpellTestKit.Events<ShieldEvent>(w), s => s.Player == 0 && s.SourceId == "WIZ-002" && s.Change == ShieldChange.Gained);
            CastB(w, 1, Feet(w, 0));
            Assert.True(Damaged(w, 0, "SPL-016"));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -3f), 180f);
            BramKit.Signature(w, AimA(w, Chest(w, 1), 0));
            BramKit.Run(w, 40);
            Assert.True(Damaged(w, 1, "WIZ-002"));

            // B vents back, then A rushes through an 8.5 m gap onto B and hides behind an Ash Curtain.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -3.5f), 180f);
            w.Step(TestWorlds.A, AimB(w, Chest(w, 0), 0).With(InputButtons.Signature));
            BramKit.Run(w, 40);
            Assert.True(Damaged(w, 0, "WIZ-002"));
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -6f), 0f);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 2.5f), 180f);
            BramKit.Run(w, 30);
            CastA(w, 4, Feet(w, 1));
            Assert.True(Committed(w, 0, "SPL-013"));
            Assert.True(Damaged(w, 1, "SPL-013"));
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -6f), 0f);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 6f), 180f);
            BramKit.Run(w, 30);
            CastA(w, 3, new Vec3(0f, 0f, 0f));
            Assert.True(Committed(w, 0, "SPL-012"));
            Assert.False(w.CanAcquire(1, 0)); // the opaque ash cloud stands between them

            // Every one of the eight spells was cast through the crest grammar, and both signatures ran.
            foreach (string spell in new[] { "SPL-009", "SPL-010", "SPL-012", "SPL-013", "SPL-014" }) Assert.True(Committed(w, 0, spell), spell);
            foreach (string spell in new[] { "SPL-011", "SPL-015", "SPL-016" }) Assert.True(Committed(w, 1, spell), spell);
            Assert.True(Committed(w, 0, "WIZ-002") && Committed(w, 1, "WIZ-002"));

            // Fight it out with Fireballs until B falls: the duel resolves for Bram A.
            TestWorlds.Place(w, 1, new Vec3(3f, 0f, 4f), 180f);
            BramKit.Run(w, Ticks.FromSeconds(6.5)); // the ash clears
            for (int i = 0; i < 40 && w.Phase != DuelPhase.Ended; i++)
            {
                BramKit.RunUntil(w, () => w.Player(0).Caster.CooldownRemainingTicks("SPL-009") == 0 && BramKit.Mana(w) >= 18f, Ticks.FromSeconds(10.0));
                if (w.Phase == DuelPhase.Ended) break;
                CastA(w, 0, Chest(w, 1), i % 2 == 0 ? BramKit.CrestHold : BramKit.NormalHold);
            }

            Assert.Equal(DuelPhase.Ended, w.Phase);
            Assert.Equal(0, w.Result.Winner);
            Assert.False(w.Player(1).Alive);
        }
    }
}
