using System;
using Confluence.Casting;
using Confluence.Casting.Tests.Support;

namespace Confluence.Casting.Tests.Machine
{
    public class PrimitiveAndSpellDataTests
    {
        [Fact]
        public void Tick_Windows_Are_Closed_Intervals()
        {
            var w = TickWindow.Centered(0.45, 0.100);
            Assert.Equal(24, w.Open);
            Assert.Equal(30, w.Close);
            Assert.Equal(7, w.Length);
            Assert.True(w.Contains(24) && w.Contains(30));
            Assert.False(w.Contains(23) || w.Contains(31));
            Assert.Equal(-2, w.Offset(22));
            Assert.Equal(3, w.Offset(33));
            Assert.Equal(new TickWindow(25, 35), TickWindow.Within(0.50, 0.08)); // 0.42-0.58 s
            Assert.Throws<ArgumentException>(() => new TickWindow(5, 4));
        }

        [Fact]
        public void Rate_Limited_Toggle_Changes_At_Most_Once_Per_Interval()
        {
            var t = new RateLimitedToggle(6);
            Assert.True(t.Sample(0, true, 1));
            Assert.False(t.Sample(1, false, 1));   // too soon: deferred
            Assert.True(t.Effective);
            Assert.False(t.Sample(5, false, 0));
            Assert.True(t.Sample(6, false, 0));    // the deferred change lands once the interval passed
            Assert.False(t.Effective);
            Assert.False(t.Sample(7, true, 1));
            Assert.False(t.Sample(8, false, 1));   // undone before the interval: dropped
            Assert.False(t.Sample(12, false, 0));
            Assert.False(t.Effective);
            Assert.Equal(3, t.ThrottledChanges);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimitedToggle(0));
        }

        [Fact]
        public void Catalogue_Spells_Get_Timing_From_Their_Records()
        {
            var fireball = TestCatalogue.Spell("SPL-009");
            Assert.Equal(18f, fireball.Mana);
            Assert.Equal(300, fireball.CooldownTicks);       // 5 s
            Assert.Equal(21, fireball.WindupTicks);          // 0.35 s
            Assert.Equal(CastWeight.Quick, fireball.Weight);
            Assert.Equal(12, fireball.RecoveryTicks);
            Assert.True(fireball.IsDamaging && fireball.IsProjectile);
            Assert.True(fireball.IsSprintCompatible);
            Assert.Equal(0, fireball.KitIndex);
            Assert.Equal("WIZ-002", fireball.WizardId);

            Assert.Equal(CastWeight.MajorFieldOrSummon, TestCatalogue.Spell("SPL-012").Weight); // Ash Curtain field
            Assert.Equal(27, TestCatalogue.Spell("SPL-012").RecoveryTicks);
            Assert.Equal(CastWeight.Control, TestCatalogue.Spell("SPL-079").Weight);            // Deepglass root
            Assert.Equal(CastWeight.Heavy, TestCatalogue.Spell("SPL-014").Weight);              // 0.45 s orb
            Assert.Equal(0.70f, TestCatalogue.Spell("SPL-014").MoveSpeedFactor);
            Assert.Equal(CastWeight.Quick, TestCatalogue.Spell("SPL-075").Weight);              // 0.20 s buckler
            Assert.False(TestCatalogue.Spell("SPL-075").IsDamaging);
            Assert.Equal(7, TestCatalogue.Spell("SPL-080").KitIndex);
        }

        [Fact]
        public void Every_Catalogue_Spell_Converts_Within_Global_Limits()
        {
            foreach (var def in TestCatalogue.Real.Spells)
            {
                var s = CastableSpell.FromDef(def);
                Assert.InRange(s.KitIndex, 0, 7);
                Assert.True(s.RecoveryTicks >= 12, def.Id);
                Assert.InRange(s.WindupTicks, 0, 60);
                Assert.False(s.HasInterruptRefund, def.Id); // no base record carries the tag
                Assert.Equal(def.IsDamaging, s.IsDamaging);
            }
        }

        [Fact]
        public void Record_Recovery_Can_Only_Be_Longer_Than_The_Default()
        {
            var quick = TestSpells.Quick();
            Assert.Equal(30, quick.WithRecoveryTicks(30).RecoveryTicks);
            Assert.Throws<ArgumentOutOfRangeException>(() => quick.WithRecoveryTicks(11));
            Assert.Equal(21, quick.WithWeight(CastWeight.Heavy).RecoveryTicks);
            Assert.False(quick.WithChannel(30).IsSprintCompatible); // sprint disallows channeled beams (spec L80)
        }

        [Fact]
        public void Grammar_Parameters_Are_Sorted_And_Immutable()
        {
            var p = GrammarParameters.Empty.With("target", 5).With("alpha", 1).With("target", 6);
            Assert.Equal(2, p.Count);
            Assert.Equal("alpha", p.KeyAt(0));
            Assert.Equal(6.0, p.Get("target", 0));
            Assert.Equal(0, GrammarParameters.Empty.Count);
            Assert.False(p.TryGet("missing", out _));
        }

        [Fact]
        public void Cast_Event_Ids_Compare_And_Pack()
        {
            var a = new CastEventId(2, 7);
            Assert.True(a.IsValid);
            Assert.False(CastEventId.None.IsValid);
            Assert.Equal((2L << 32) | 7L, a.Packed);
            Assert.True(new CastEventId(2, 8).CompareTo(a) > 0);
            Assert.Equal(a, new CastEventId(2, 7));
        }
    }
}
