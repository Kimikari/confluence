using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// SPL-075 Bubble Buckler through WIZ-010's grammar: "Gain a forward 45-point shield for 4 s; protects a 100-degree arc;
    /// common shield cap applies." "Water 1 when broken; Heat consumes 10 shield to create a 1 m steam puff." 18 mana, 10 s,
    /// 0.2 s.
    /// </summary>
    public class BubbleBucklerTests
    {
        internal const int Slot = 2;

        internal static HalenDuel Raised(HalenDuel d = null)
        {
            d = d ?? HalenTestKit.Duel();
            d.Halen = TestWorlds.A;
            d.Cast(Slot);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ShieldRaised).Any(), 30);
            return d;
        }

        /// <summary>A hit on Halen from a point (the hit's source decides the directional arc).</summary>
        internal static DamageEvent HitFrom(HalenDuel d, Vec3 source, float amount, DamageChannel channel = DamageChannel.Physical, string id = "SPL-TEST")
        {
            DamageRequest r = DamageRequest.Single(1, 0, DamageKind.Direct, id, channel, amount).From(source);
            return d.World.ApplyDamage(r);
        }

        internal static Vec3 Front(HalenDuel d, float meters = 0.6f) => d.P0.Position + new Vec3(0f, 0f, meters);

        [Fact]
        [Verifies("SPL-075")]
        public void It_Raises_A_45_Point_Shield_For_4_Seconds_After_Its_02_Second_Windup()
        {
            HalenDuel d = HalenTestKit.Duel();
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(BubbleBuckler.Id, commit.SpellId);
            Assert.Equal(18f, commit.Cast.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(10.0), d.P0.Caster.CooldownRemainingTicks(BubbleBuckler.Id));
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ShieldRaised).Any(), 30);
            SpellEvent raised = d.SpellEvents(SpellEventKind.ShieldRaised).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.2), raised.Tick);
            ShieldEntry entry = d.P0.Shields.Entries.Single();
            Assert.Equal(45f, entry.Amount, 3);
            Assert.Equal(50f, entry.ArcHalfDeg, 3); // "protects a 100-degree arc"
            Assert.Equal(raised.Tick + Ticks.FromSeconds(4.0), entry.ExpireTick);
        }

        [Fact]
        [Verifies("SPL-075")]
        public void It_Protects_Only_The_Forward_100_Degree_Arc()
        {
            HalenDuel d = Raised();
            Vec3 p = d.P0.Position;
            Assert.Equal(0f, HitFrom(d, p + new Vec3(0f, 0f, -3f), 10f).Resolution.ShieldAbsorbed, 3); // behind: exposed back (spec L549)
            Vec3 flank60 = p + new Vec3(3f * 0.866f, 0f, 3f * 0.5f);
            Assert.Equal(0f, HitFrom(d, flank60, 10f).Resolution.ShieldAbsorbed, 3);                    // 60 degrees off the facing
            Vec3 front40 = p + new Vec3(3f * 0.643f, 0f, 3f * 0.766f);
            Assert.Equal(10f, HitFrom(d, front40, 10f).Resolution.ShieldAbsorbed, 3);                   // inside the 50 degree half arc
            Assert.Equal(30f, HitFrom(d, d.P1.Position, 30f).Resolution.ShieldAbsorbed, 3);
            Assert.Equal(5f, d.P0.Shields.Total, 3);
        }

        [Fact]
        [Verifies("SPL-075")]
        public void Melee_From_Behind_Goes_Around_It()
        {
            // Counterplay "Flank, use melee from another angle".
            HalenDuel d = Raised();
            TestWorlds.Place(d.World, 1, d.P0.Position + new Vec3(0f, 0f, -1.6f), 0f);
            d.Other = TestWorlds.Face(0f).With(InputButtons.Melee);
            d.Step();
            d.Other = TestWorlds.Face(0f);
            d.Run(20);
            DamageEvent melee = d.DamageTo(0).Single(e => e.Kind == DamageKind.Melee);
            Assert.Equal(0f, melee.Resolution.ShieldAbsorbed, 3);
            Assert.Equal(GlobalRules.Melee.FocusDamage, melee.Resolution.HealthLost, 3);
            Assert.Equal(45f, d.P0.Shields.Total, 3);
        }

        [Fact]
        [Verifies("SPL-075")]
        public void A_Timed_End_Leaves_No_Water_But_A_Break_Leaves_Water_1_At_Its_Front()
        {
            HalenDuel expired = Raised();
            expired.RunSeconds(4.1);
            Assert.Equal(0f, expired.P0.Shields.Total, 3);
            Assert.Empty(expired.Tide(TideEventKind.BubbleBroken));
            Assert.Equal(0, expired.Total(Material.Water, Footprint.Circle(Front(expired).XZ(), 2f)));

            HalenDuel broken = Raised();
            DamageEvent hit = HitFrom(broken, broken.P1.Position, 60f);
            Assert.Equal(45f, hit.Resolution.ShieldAbsorbed, 3);
            broken.Run(2);
            TideEvent ev = broken.Tide(TideEventKind.BubbleBroken).Single();
            Assert.Equal(0f, ev.Amount); // not a thermal break
            Assert.Equal(1, broken.Units(Front(broken)).Water);
            Assert.Equal(1, broken.Total(Material.Water, Footprint.Circle(Front(broken).XZ(), 2f)));
        }

        [Fact]
        [Verifies("SPL-075")]
        public void Heat_Consumes_10_Shield_Into_A_1_Meter_Steam_Puff()
        {
            HalenDuel d = Raised();
            DamageEvent hit = HitFrom(d, d.P1.Position, 20f, DamageChannel.Thermal);
            Assert.Equal(20f, hit.Resolution.ShieldAbsorbed, 3);
            d.Run(2);
            Assert.Equal(45f - 20f - 10f, d.P0.Shields.Total, 3);
            TideEvent reaction = d.Tide(TideEventKind.BubbleHeatReaction).Single();
            Assert.Equal(10f, reaction.Amount, 3);
            ObscurationVolume puff = d.Reactions.Volumes.Single();
            Assert.Equal(VolumeKind.Steam, puff.Kind);
            Assert.Equal(0, puff.Owner); // the bubble's own described reaction (spec L126), counted as Halen's field (D-149)
            puff.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, 1.4f, 2.01f); // a 1 m radius puff
            Assert.True(puff.ContainsPoint(Front(d) + new Vec3(0f, 1f, 0f)));
            Assert.Equal(Ticks.FromSeconds(GlobalRules.World.SteamLifetimeSec), puff.ExpiresTick - puff.CreatedTick);

            // A physical hit is not heat: no further reaction.
            HitFrom(d, d.P1.Position, 5f);
            d.Run(2);
            Assert.Single(d.Tide(TideEventKind.BubbleHeatReaction));
            Assert.Equal(10f, d.P0.Shields.Total, 3);
        }

        [Fact]
        [Verifies("SPL-075")]
        public void A_Heat_Reaction_That_Empties_The_Bubble_Breaks_It_With_Water_And_Steam()
        {
            HalenDuel d = Raised();
            HitFrom(d, d.P1.Position, 40f, DamageChannel.Thermal); // 5 left, the reaction takes them
            d.Run(2);
            Assert.Equal(0f, d.P0.Shields.Total, 3);
            Assert.Equal(5f, d.Tide(TideEventKind.BubbleHeatReaction).Single().Amount, 3);
            Assert.Equal(1f, d.Tide(TideEventKind.BubbleBroken).Single().Amount); // a thermal break
            Assert.Equal(VolumeKind.Steam, d.Reactions.Volumes.Single().Kind);     // the reaction's 1 m puff
            Assert.Equal(1, d.Units(Front(d)).Water);                             // and "Water 1 when broken"
        }

        [Fact]
        [Verifies("SPL-075")]
        public void An_Ended_Bubble_Stops_Watching_The_Event_Stream()
        {
            // Timed end, twice (no observer accumulates across casts).
            HalenDuel d = HalenTestKit.Duel();
            int baseline = d.World.EventObserverCount;
            Raised(d);
            Assert.Equal(baseline + 1, d.World.EventObserverCount);
            d.RunSeconds(4.2);
            Assert.Empty(d.Runtime.Entities.OfType<BubbleShieldEntity>());
            Assert.Equal(baseline, d.World.EventObserverCount);
            d.RunSeconds(6.0); // the 10 s cooldown
            d.Halen = TestWorlds.A;
            d.Cast(Slot);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ShieldRaised).Count() == 2, 30);
            Assert.Equal(baseline + 1, d.World.EventObserverCount);
            d.RunSeconds(4.2);
            Assert.Equal(baseline, d.World.EventObserverCount);

            // A break.
            HalenDuel broken = Raised();
            HitFrom(broken, broken.P1.Position, 50f);
            broken.Run(2);
            Assert.Single(broken.Tide(TideEventKind.BubbleBroken));
            Assert.Equal(baseline, broken.World.EventObserverCount);

            // An AUG-437 release.
            HalenDuel released = Raised(HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(BubbleRelease.Id, 1))));
            released.AimAtOther();
            released.Step();
            released.Recast();
            Assert.Single(released.Tide(TideEventKind.BubbleReleased));
            Assert.Equal(baseline, released.World.EventObserverCount);
        }

        [Fact]
        [Verifies("SPL-075")]
        public void The_Common_100_Point_Shield_Cap_Applies()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.World.GrantShield(0, "EQP-TEST", 80f, Ticks.FromSeconds(6.0));
            Raised(d);
            Assert.Equal(GlobalRules.Shields.Cap, d.P0.Shields.Total, 3);
            Assert.Equal(20f, d.P0.Shields.Entries.Single(e => e.SourceId == BubbleBuckler.Id).Amount, 3);
        }

        [Fact]
        [Verifies("SPL-075")]
        public void A_Real_Fireball_Splash_On_The_Bubble_Is_A_Heat_Reaction()
        {
            // Bram Cinder (player 1) splashes SPL-009 on the ground in front of Halen's raised bubble: the thermal splash comes
            // from inside the protected arc, is absorbed, and the heat consumes 10 more shield into steam.
            HalenDuel d = Raised(HalenTestKit.Duel(other: HalenTestKit.Bram()));
            Vec3 ground = new Vec3(0f, 0f, -5.7f); // the 0.15 m bolt meets the floor about 1.1 m short of this point
            Vec3 aim = ground - d.P1.Eye;
            PlayerInput bram = TestWorlds.B.Facing(CMath.YawOf(aim), CMath.PitchOf(aim));
            d.Other = bram;
            for (int i = 0; i < 15; i++) d.Step(InputButtons.None, InputButtons.Primary);
            d.Step();
            d.RunUntil(() => d.DamageTo(0, "SPL-009").Any(), 90);
            DamageEvent splash = d.DamageTo(0, "SPL-009").Single();
            Assert.Equal(DamageKind.Splash, splash.Kind);
            Assert.Equal(28f, splash.Resolution.ShieldAbsorbed, 3);
            d.Run(2);
            Assert.Equal(45f - 28f - 10f, d.P0.Shields.Total, 3);
            Assert.Single(d.Tide(TideEventKind.BubbleHeatReaction));
        }
    }
}
