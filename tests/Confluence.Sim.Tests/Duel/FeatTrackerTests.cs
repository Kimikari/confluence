using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;

namespace Confluence.Sim.Tests.Duel
{
    /// <summary>Server-verified completed-duel feat counters (spec L251-L280), thresholds from Core Feats.</summary>
    public class FeatTrackerTests
    {
        [Fact]
        public void Every_Feat_Has_An_Evaluation()
        {
            var tracker = new FeatTracker();
            int count = 0;
            foreach (FeatDef feat in Feats.All)
            {
                tracker.IsMet(feat, true, 1f, 0f); // throws for an unknown feat
                count++;
            }

            Assert.Equal(24, count);
        }

        [Fact]
        public void Damage_Channels_And_Low_Health_Win()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 40f, channel: DamageChannel.Thermal));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 35f, channel: DamageChannel.Physical));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 30f, channel: DamageChannel.Arcane));
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 240f)); // player 0 drops to 20%
            FeatTracker f = w.Player(0).Feats;
            Assert.Equal(105f, f.DamageDealt, 3);
            Assert.True(f.IsMet(Feats.Get("FEAT-001"), false, 1f, 0f));
            Assert.True(f.IsMet(Feats.Get("FEAT-012"), false, 1f, 0f));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 200f));
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.True(w.Result.Won(0));
            Assert.Contains("FEAT-010", w.Result.CompletedFeats[0]);
            Assert.Contains("FEAT-016", w.Result.CompletedFeats[0]);
            Assert.DoesNotContain("FEAT-010", w.Result.CompletedFeats[1]);
        }

        [Fact]
        public void Distinct_Exclusive_Spells_Count_Once_Each()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f, sourceId: "SPL-001"));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f, sourceId: "SPL-001"));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f, sourceId: "SPL-002"));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f, DamageKind.Melee, sourceId: "EQP-031"));
            Assert.Equal(2, w.Player(0).Feats.DistinctSpellsLanded);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f, DamageKind.Splash, sourceId: "SPL-003"));
            Assert.True(w.Player(0).Feats.IsMet(Feats.Get("FEAT-002"), false, 1f, 0f));
        }

        [Fact]
        public void Long_Range_Direct_Hits_And_Spell_Then_Weapon_Combos()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 10f), 180f); // 16 m apart
            for (int i = 0; i < 3; i++)
            {
                w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f, sourceId: "SPL-001"));
                w.Step(TestWorlds.A, TestWorlds.B);
                w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f, DamageKind.Melee, sourceId: "EQP-031"));
            }

            Assert.Equal(3, w.Player(0).Feats.LongRangeDirectHits);
            Assert.Equal(3, w.Player(0).Feats.SpellWeaponCombos);
            Assert.Equal(3, w.Player(0).Feats.MeleeHits);
        }

        [Fact]
        public void Hits_Within_One_Second_After_A_Dodge()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 4.6f), 180f);
            w.Step(TestWorlds.A.Moving(0f, 1f).With(InputButtons.Dodge), TestWorlds.B); // 3 m toward the opponent
            TestWorlds.Run(w, 13, TestWorlds.A, TestWorlds.B);
            w.Step(TestWorlds.A.With(InputButtons.Melee), TestWorlds.B);
            TestWorlds.Run(w, 20, TestWorlds.A, TestWorlds.B);
            Assert.Equal(1, w.Player(0).Feats.MeleeHits);
            Assert.Equal(1, w.Player(0).Feats.HitsAfterDodge);
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f));
            Assert.Equal(1, w.Player(0).Feats.HitsAfterDodge);
        }

        [Fact]
        public void Valid_Combat_Movement_Excludes_Knockback()
        {
            DuelWorld w = TestWorlds.Duel();
            for (int leg = 0; leg < 6; leg++)
            {
                TestWorlds.RunSeconds(w, 2f, TestWorlds.A.Moving(0f, leg % 2 == 0 ? 1f : -1f), TestWorlds.B);
            }

            float walked = w.Player(0).Feats.DistanceTravelled;
            Assert.InRange(walked, 60f, 66f);
            Assert.True(w.Player(0).Feats.IsMet(Feats.Get("FEAT-005"), false, 1f, 0f));

            w.ApplyKnockback(0, 1, new Vec3(1f, 0f, 0f), 4f, "SPL-PUSH");
            TestWorlds.RunSeconds(w, 0.5f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(walked, w.Player(0).Feats.DistanceTravelled, 3);
        }

        [Fact]
        public void Shields_Healing_Covers_And_Consumables()
        {
            DuelWorld w = TestWorlds.Duel();
            w.GrantShield(1, "SPL-WARD", 70f, 400);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 150f));
            Assert.Equal(70f, w.Player(1).Feats.ShieldAbsorbed, 3);
            Assert.True(w.Player(1).Feats.IsMet(Feats.Get("FEAT-017"), false, 1f, 0f));

            w.Heal(1, 35f, "SPL-MEND");
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B);
            w.Heal(1, 35f, "SPL-MEND");
            Assert.True(w.Player(1).Feats.IsMet(Feats.Get("FEAT-018"), false, 1f, 0f));

            w.RecordConsumableUse(1, "EQP-111");
            Assert.False(w.Player(1).Feats.IsMet(Feats.Get("FEAT-016"), true, 1f, 0f));
        }

        [Fact]
        public void Mana_Commitments_Count_Toward_FEAT_013()
        {
            DuelWorld w = TestWorlds.Duel();
            Assert.True(w.TryCommitMana(0, 60f, "SPL-001"));
            Assert.False(w.TryCommitMana(0, 60f, "SPL-001")); // never without the full cost
            TestWorlds.RunSeconds(w, 7f, TestWorlds.A, TestWorlds.B);
            Assert.True(w.TryCommitMana(0, 60f, "SPL-002"));
            Assert.Equal(120f, w.Player(0).Resources.ManaCommitted, 3);
            Assert.True(w.Player(0).Feats.IsMet(Feats.Get("FEAT-013"), false, 1f, w.Player(0).Resources.ManaCommitted));
        }
    }
}
