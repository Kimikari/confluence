using System.Linq;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Content;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>The state hash covers casting and live deliveries, and identical inputs give identical worlds (spec L427).</summary>
    public class SpellDeterminismTests
    {
        private static SpellLoadout Mixed()
        {
            SpellFormBuilder orb = SpellTestKit.Form("SPL-T80", 10f, 0.5f, 0.2f);
            orb.Delivery = new ProjectileDelivery { Speed = 14f, MaxRange = 30f, Gravity = 2f };
            orb.Payload.Direct = 9f;
            orb.Payload.Splash = 5f;
            orb.Payload.SplashRadius = 2f;

            SpellFormBuilder field = SpellTestKit.Form("SPL-T81", 10f, 0.5f, 0f, 12f, DamageChannel.Corrosive, "field");
            field.Delivery = new FieldDelivery { Radius = 2.5f, LifetimeSec = 3f, TickPayload = new PayloadSpec { Direct = 3f, Channel = DamageChannel.Corrosive } };

            SpellFormBuilder wave = SpellTestKit.Form("SPL-T82", 10f, 0.5f, 0f, 12f, DamageChannel.Physical);
            wave.Delivery = new WaveDelivery { Speed = 12f, Width = 2f };
            wave.Payload.Direct = 4f;
            wave.Payload.Push = 1f;
            return SpellTestKit.Loadout(orb.Build(), field.Build(), wave.Build());
        }

        private static PlayerInput Script(int tick, int player)
        {
            PlayerInput input = player == 0 ? TestWorlds.A : TestWorlds.B;
            input = input.Moving((tick / 25) % 2 == 0 ? 0.6f : -0.6f, 0.2f).Facing((player == 0 ? 0f : 180f) + (tick % 40) * 0.1f, 0f);
            input.SelectedSlot = (tick / 45) % 3;
            if (tick % 45 == 20) input = input.With(InputButtons.Primary);
            if (player == 1 && tick % 70 == 10) input = input.With(InputButtons.Jump);
            input.Tick = tick;
            return input;
        }

        [Fact]
        public void Identical_Inputs_Give_Identical_Hashes_With_Spells_In_Flight()
        {
            DuelWorld a = SpellTestKit.Duel(Mixed(), Mixed());
            DuelWorld b = SpellTestKit.Duel(Mixed(), Mixed());
            int withEntities = 0;
            for (int t = 1; t <= 400; t++)
            {
                a.Step(Script(t, 0), Script(t, 1));
                b.Step(Script(t, 0), Script(t, 1));
                Assert.Equal(a.StateHash(), b.StateHash());
                if (SpellTestKit.Runtime(a).Entities.Count > 0) withEntities++;
            }

            Assert.True(withEntities > 50);
            Assert.Contains(a.EventLog.OfType<DamageEvent>(), d => d.SourceId != null && d.SourceId.StartsWith("SPL-T8"));
            Assert.Equal(a.EventLog.Count, b.EventLog.Count);
        }

        [Fact]
        public void The_Hash_Changes_With_Delivery_State()
        {
            DuelWorld a = SpellTestKit.Duel(Mixed());
            DuelWorld b = SpellTestKit.Duel(Mixed());
            SpellTestKit.Cast(a);
            SpellTestKit.Cast(b);
            SpellTestKit.Run(a, 10);
            SpellTestKit.Run(b, 10);
            Assert.Equal(a.StateHash(), b.StateHash());
            ulong inFlight = a.StateHash();
            SpellTestKit.Run(a, 1);
            SpellTestKit.Run(b, 1);
            Assert.NotEqual(inFlight, a.StateHash());

            // The same inputs except one aim offset: the projectile differs, so the hash must differ.
            DuelWorld c = SpellTestKit.Duel(Mixed());
            SpellTestKit.Cast(c, TestWorlds.Face(1f));
            SpellTestKit.Run(c, 10, TestWorlds.Face(1f));
            Assert.NotEqual(a.StateHash(), c.StateHash());
        }

        [Fact]
        public void Real_Content_Duels_Replay_Identically()
        {
            DuelWorld a = ContentTestKit.BramVersusDummy();
            DuelWorld b = ContentTestKit.BramVersusDummy();
            for (int round = 0; round < 3; round++)
            {
                foreach (DuelWorld w in new[] { a, b })
                {
                    PlayerInput bram = TestWorlds.A.Selecting(round % 2); // Fireball, then Cinder Sweep, then Fireball on cooldown
                    ContentTestKit.HoldAndRelease(w, 20 + round * 3, bram);
                    SpellTestKit.Run(w, 100, bram.Moving(0.5f, 0f), TestWorlds.B.Moving(-0.5f, 0f));
                }

                Assert.Equal(a.StateHash(), b.StateHash());
            }

            Assert.True(SpellTestKit.CastEvents(a, Confluence.Casting.CastEventKind.Committed).Count() >= 2);
            Assert.Equal(a.EventLog.Count, b.EventLog.Count);
        }
    }
}
