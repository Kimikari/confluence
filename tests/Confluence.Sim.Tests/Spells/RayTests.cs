using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>Hitscan rays and beams: width, solid stops, lag compensation, reflection, and shared beam pulses.</summary>
    public class RayTests
    {
        private static SpellForm Ray(float width = 0.1f, float direct = 10f, bool reflects = true, float channelSec = 0f, float pulseSec = 0.25f)
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T10", 10f, 0.5f, 0f, 30f, DamageChannel.Arcane);
            b.Delivery = new RayDelivery { Width = width, Reflects = reflects };
            b.Payload.Direct = direct;
            b.ChannelSec = channelSec;
            b.ChannelPulseSec = pulseSec;
            return b.Build();
        }

        [Fact]
        public void A_Ray_Resolves_At_Release_And_Stops_At_The_First_Solid()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Ray()));
            SpellTestKit.Cast(w);
            Assert.Equal(10f, SpellTestKit.HealthLost(w, 1), 3); // same tick as the release
            SpellEvent fired = SpellTestKit.SpellEvents(w, SpellEventKind.RayFired).Single();
            Assert.Equal(1, fired.TargetPlayer);

            DuelWorld blocked = SpellTestKit.Duel(SpellTestKit.Loadout(Ray()),
                layout: SpellTestKit.FlatWith(SpellTestKit.Wall(60, new Vec3(0f, 0f, 0f), new Vec3(3f, 2.5f, 0.2f))));
            SpellTestKit.Cast(blocked);
            Assert.Equal(0f, SpellTestKit.HealthLost(blocked, 1));
            Assert.Equal(5.9f, SpellTestKit.SpellEvents(blocked, SpellEventKind.RayFired).Single().Amount, 2);
        }

        [Fact]
        public void The_Declared_Width_Widens_The_Hit_Test()
        {
            // The target stands 0.55 m beside the aim line: a thin ray misses, a 0.4 m-wide ray hits.
            DuelWorld thin = SpellTestKit.Duel(SpellTestKit.Loadout(Ray(width: 0.1f)));
            TestWorlds.Place(thin, 1, new Vec3(0.55f, 0f, 6f), 180f);
            SpellTestKit.Cast(thin);
            Assert.Equal(0f, SpellTestKit.HealthLost(thin, 1));

            DuelWorld wide = SpellTestKit.Duel(SpellTestKit.Loadout(Ray(width: 0.4f)));
            TestWorlds.Place(wide, 1, new Vec3(0.55f, 0f, 6f), 180f);
            SpellTestKit.Cast(wide);
            Assert.Equal(10f, SpellTestKit.HealthLost(wide, 1), 3);
        }

        [Fact]
        public void Lag_Compensation_Rewinds_The_Target_To_The_Viewed_Tick_But_Never_Beyond_150_ms()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Ray()), SpellTestKit.Loadout());
            PlayerInput strafe = TestWorlds.B.Moving(-1f, 0f); // facing -Z, so "left" is +X
            SpellTestKit.Run(w, 40, TestWorlds.A, strafe);
            int seenTick = w.Tick - 6;
            Assert.True(w.History.TryGet(1, seenTick, out Capsule seen, out _));
            Vec3 seenChest = seen.Center;

            PlayerInput shot = SpellTestKit.Aim(w, 0, seenChest);
            PlayerInput late = shot;
            late.ViewTick = 0;
            DuelWorld control = w; // aiming where the target was, without a view tick, misses its present body
            control.Step(late.With(InputButtons.Primary), strafe);
            Assert.Equal(0f, SpellTestKit.HealthLost(control, 1));

            DuelWorld v = SpellTestKit.Duel(SpellTestKit.Loadout(Ray()), SpellTestKit.Loadout());
            SpellTestKit.Run(v, 40, TestWorlds.A, strafe);
            Assert.True(v.History.TryGet(1, v.Tick - 5, out Capsule viewed, out _));
            PlayerInput lagged = SpellTestKit.Aim(v, 0, viewed.Center);
            lagged.ViewTick = v.Tick - 5; // the client was drawing the opponent ~83 ms in the past
            v.Step(lagged.With(InputButtons.Primary), strafe);
            Assert.Equal(10f, SpellTestKit.HealthLost(v, 1), 3);

            DuelWorld old = SpellTestKit.Duel(SpellTestKit.Loadout(Ray()), SpellTestKit.Loadout());
            SpellTestKit.Run(old, 20, TestWorlds.A, strafe);
            Vec3 stale = old.Player(1).HitVolume.Center;
            int staleTick = old.Tick;
            SpellTestKit.Run(old, 20, TestWorlds.A, strafe);
            PlayerInput tooOld = SpellTestKit.Aim(old, 0, stale);
            tooOld.ViewTick = staleTick; // 333 ms: clamped to the 150 ms window (spec L423)
            old.Step(tooOld.With(InputButtons.Primary), strafe);
            Assert.Equal(0f, SpellTestKit.HealthLost(old, 1));
        }

        [Fact]
        public void A_Reflective_Solid_Bounces_The_Ray_Once_With_Seventy_Percent_Damage()
        {
            // A mirror at 45 degrees in front of the caster turns the ray toward -X, where the target stands.
            Solid mirror = SpellTestKit.Wall(70, new Vec3(0f, 0f, 0f), new Vec3(3f, 2.5f, 0.2f), 45f, MaterialFlags.Reflective);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Ray(direct: 20f)), layout: SpellTestKit.FlatWith(mirror));
            TestWorlds.Place(w, 1, new Vec3(-6f, 0f, -0.2f), 90f);
            SpellTestKit.Cast(w);
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.RayReflected));
            Assert.Equal(14f, SpellTestKit.HealthLost(w, 1), 3); // spec L527: one bounce, 70% retained

            DuelWorld never = SpellTestKit.Duel(SpellTestKit.Loadout(Ray(direct: 20f, reflects: false)), layout: SpellTestKit.FlatWith(mirror));
            TestWorlds.Place(never, 1, new Vec3(-6f, 0f, -0.2f), 90f);
            SpellTestKit.Cast(never);
            Assert.Equal(0f, SpellTestKit.HealthLost(never, 1));
        }

        [Fact]
        public void A_Beam_Fires_On_Every_Channel_Pulse_And_All_Pulses_Share_The_Cast_Event()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Ray(direct: 4f, channelSec: 1f, pulseSec: 0.25f)));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 70);
            var hits = SpellTestKit.DamageTo(w, 1);
            Assert.Equal(4, hits.Count);
            Assert.Single(hits.Select(h => h.OriginEventId).Distinct()); // no independent proc cascades (spec L126)
            Assert.Equal(16f, SpellTestKit.HealthLost(w, 1), 3);
        }

        [Fact]
        public void Stopping_A_Channel_Stops_Its_Remaining_Pulses()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Ray(direct: 4f, channelSec: 2f, pulseSec: 0.25f)));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 31);
            w.Step(TestWorlds.A.With(InputButtons.Primary), TestWorlds.B); // spec L92: cancel on any step
            SpellTestKit.Run(w, 120);
            Assert.Equal(2, SpellTestKit.DamageTo(w, 1).Count);
        }
    }
}
