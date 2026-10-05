using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>
    /// WIZ-002's passive, Tempered Hands: "taking thermal damage grants a visible 20-point shield for 2 s, once per 8 s.
    /// It provides no thermal immunity and respects the shared shield cap."
    /// </summary>
    public class TemperedHandsTests
    {
        private static DuelWorld Duel() =>
            BramKit.Duel(BramKit.Bram(), opponent: BramKit.Caster(
                BramKit.Bolt("SPL-T80", 30f, DamageChannel.Thermal),
                BramKit.Bolt("SPL-T81", 30f, DamageChannel.Physical)));

        private static void Hit(DuelWorld w, int slot)
        {
            int before = BramKit.DamageTo(w, 0).Count;
            BramKit.OpponentFires(w, slot, BramKit.Chest(w, 0));
            BramKit.RunUntil(w, () => BramKit.DamageTo(w, 0).Count > before, 40);
            BramKit.Run(w, 1);
        }

        private static ShieldEvent[] Gains(DuelWorld w) =>
            SpellTestKit.Events<ShieldEvent>(w).Where(s => s.Player == 0 && s.Change == ShieldChange.Gained && s.SourceId == "WIZ-002").ToArray();

        [Fact]
        [Verifies("WIZ-002")]
        public void Tempered_Hands_Grants_A_Visible_20_Point_Shield_For_2_Seconds_After_Thermal_Damage()
        {
            DuelWorld w = Duel();
            Hit(w, 0);
            // "It provides no thermal immunity": the triggering hit lands in full on health, the shield comes after.
            DamageEvent first = BramKit.DamageTo(w, 0, "SPL-T80").Single();
            Assert.Equal(30f, first.Dealt, 3);
            Assert.Equal(w.Player(0).Resources.MaxHealth - 30f, w.Player(0).Resources.Health, 3);

            ShieldEvent gained = Gains(w).Single(); // "visible": a shield event every client sees
            Assert.Equal(20f, gained.Amount, 3);
            Assert.Equal(20f, w.Player(0).Shields.Total, 3);
            Assert.True(gained.Tick >= first.Tick);

            BramKit.RunUntil(w, () => w.Player(0).Shields.Total == 0f, Ticks.FromSeconds(3.0));
            ShieldEvent expired = SpellTestKit.Events<ShieldEvent>(w).Single(s => s.Player == 0 && s.Change == ShieldChange.Expired);
            Assert.InRange(expired.Tick - gained.Tick, Ticks.FromSeconds(2.0) - 1, Ticks.FromSeconds(2.0) + 1);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Tempered_Hands_Shield_Absorbs_The_Next_Hit_And_Thermal_Damage_Still_Lands_Afterwards()
        {
            DuelWorld w = Duel();
            Hit(w, 0);
            float health = w.Player(0).Resources.Health;
            Hit(w, 1); // a physical bolt inside the 2 s: 20 absorbed, 10 to health
            Assert.Equal(health - 10f, w.Player(0).Resources.Health, 3);
            Assert.Equal(0f, w.Player(0).Shields.Total, 3);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Tempered_Hands_Triggers_Once_Per_8_Seconds()
        {
            DuelWorld w = Duel();
            Hit(w, 0);
            int first = Gains(w).Single().Tick;
            BramKit.Run(w, Ticks.FromSeconds(3.0));
            Hit(w, 0); // inside the 8 s internal cooldown
            Assert.Single(Gains(w));
            BramKit.RunUntil(w, () => w.Tick >= first + Ticks.FromSeconds(8.0) + 1, Ticks.FromSeconds(8.0));
            Hit(w, 0);
            Assert.Equal(2, Gains(w).Length);
            Assert.True(Gains(w)[1].Tick - first >= Ticks.FromSeconds(8.0));
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Tempered_Hands_Ignores_Nonthermal_Damage()
        {
            DuelWorld w = Duel();
            Hit(w, 1);
            Assert.Empty(Gains(w));
            Assert.Equal(0f, w.Player(0).Shields.Total, 3);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void Tempered_Hands_Respects_The_Shared_100_Shield_Cap()
        {
            DuelWorld w = Duel();
            w.GrantShield(0, "TEST-SHIELD", 90f, Ticks.FromSeconds(6.0));
            Hit(w, 0);
            // The bolt is absorbed by the existing shield (30 of 90), then the passive tops the pool up to at most 100.
            Assert.Equal(GlobalRules.Shields.Cap, 100f);
            Assert.Equal(20f, Gains(w).Single().Amount, 3);
            Assert.Equal(80f, w.Player(0).Shields.Total, 3);

            DuelWorld full = Duel();
            full.GrantShield(0, "TEST-SHIELD", 100f, Ticks.FromSeconds(6.0));
            full.GrantShield(0, "TEST-RESERVE", 100f, 0, true);
            Hit(full, 0);
            Assert.True(full.Player(0).Shields.Total <= GlobalRules.Shields.Cap + 1e-3f);
            Assert.All(Gains(full), g => Assert.True(g.Amount <= 30f + 1e-3f)); // only what the absorbed 30 freed
        }
    }
}
