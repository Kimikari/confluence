using System;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Hosting.Duels;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Tests.TestSupport;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Property test (research §3, hidden information and security): random but valid movement commands over the wire
    /// never move the authoritative players outside the motor envelope (walk/sprint/dodge speeds, D-024 jump, the arena),
    /// and never count as protocol violations. Clients send intents only, so this is what a modified client can reach.
    /// </summary>
    public sealed class InputEnvelopeTests
    {
        private readonly ITestOutputHelper _output;

        public InputEnvelopeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // Everything a player can hold or tap without casting (casts, melee and items move players through their own
        // records' rules, which the Sim tests cover).
        private static readonly InputButtons[] MovementButtons =
        {
            InputButtons.Sprint, InputButtons.Jump, InputButtons.Crouch, InputButtons.Dodge, InputButtons.Guard,
            InputButtons.MoveForward, InputButtons.MoveBack, InputButtons.MoveLeft, InputButtons.MoveRight, InputButtons.SwapFocus,
            InputButtons.CastModifier, InputButtons.ModifierA, InputButtons.ModifierB,
        };

        private static Func<ClientDuel, int, PlayerInput> RandomInputs(ulong seed)
        {
            var rng = new Rng(seed);
            return (duel, tick) =>
            {
                var input = PlayerInput.Idle(0f);
                input.MoveX = rng.NextInt(5) == 0 ? (rng.NextBool() ? 1f : -1f) : rng.NextFloat() * 2f - 1f;
                input.MoveY = rng.NextInt(5) == 0 ? (rng.NextBool() ? 1f : -1f) : rng.NextFloat() * 2f - 1f;
                input.YawDeg = rng.NextFloat() * 720f - 360f;
                input.PitchDeg = rng.NextFloat() * 180f - 90f;
                InputButtons held = InputButtons.None, tapped = InputButtons.None;
                foreach (InputButtons b in MovementButtons)
                {
                    if (rng.NextInt(3) == 0) held |= b;
                    if (rng.NextInt(6) == 0) tapped |= b;
                }

                input.Buttons = held;
                input.Tapped = tapped;
                input.SelectedSlot = rng.NextInt(PlayerInput.SlotCount + 1);
                return input;
            };
        }

        [Fact]
        public void Random_Valid_Movement_Commands_Stay_Inside_The_Motor_Envelope()
        {
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Duel,
                PlayerCount = 2,
                Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
                Laboratory = true,
                ArenaPrefetchRounds = 1,
            };
            using (var rig = new GameRig(settings))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                a.Client.InputProvider = RandomInputs(101);
                b.Client.InputProvider = RandomInputs(202);

                // Dodge covers 3 m in 0.22 s (L82); sprint is 7 m/s (L80); 25% headroom for ground acceleration overshoot.
                float maxHorizontal = Math.Max(GlobalRules.Movement.SprintSpeed, GlobalRules.Movement.DodgeDistance / GlobalRules.Movement.DodgeDurationSec) * 1.25f;
                float maxHorizontalSeen = 0f, maxRise = 0f;
                int combatTicks = 0;
                bool done = rig.RunUntil(() =>
                {
                    if (rig.Server.Host == null) return false;
                    foreach (RunningDuel duel in rig.Server.Host.Duels.Running)
                    {
                        DuelWorld world = duel.World;
                        if (world.Phase == DuelPhase.Combat) combatTicks++;
                        float radius = world.Layout.Diameter * 0.5f + 1f;
                        for (int i = 0; i < DuelWorld.PlayerCount; i++)
                        {
                            DuelPlayer p = world.Player(i);
                            Vec3 pos = p.Motor.Position, vel = p.Motor.Velocity;
                            Assert.True(Finite(pos) && Finite(vel), "non-finite motor state at tick " + world.Tick);
                            float h = (float)Math.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
                            maxHorizontalSeen = Math.Max(maxHorizontalSeen, h);
                            Assert.True(h <= maxHorizontal, "horizontal speed " + h + " at tick " + world.Tick);
                            Vec3 off = pos - world.Layout.Center;
                            Assert.True(Math.Sqrt(off.X * off.X + off.Z * off.Z) <= radius, "left the arena at tick " + world.Tick + ": " + pos);
                            maxRise = Math.Max(maxRise, vel.Y);
                            Assert.True(vel.Y <= (float)Math.Sqrt(2f * GlobalRules.Movement.Gravity * GlobalRules.Movement.JumpApexHeight) * 1.05f + 0.01f,
                                "rising at " + vel.Y + " m/s at tick " + world.Tick);
                        }
                    }

                    return a.Client.View.Results.Count >= 1 && b.Client.View.Results.Count >= 1;
                }, 60 * 60 * 20); // includes the wait for the background arena generation before the match starts

                foreach (string line in rig.Log.Skip(Math.Max(0, rig.Log.Count - 40))) _output.WriteLine(line);
                foreach (string m in rig.Server.Metrics) _output.WriteLine(m);
                _output.WriteLine("rig ticks " + rig.Ticks + ", host tick " + rig.Server.Host.CurrentTick + ", phase " + a.Client.View.CurrentPhase + ", running " + rig.Server.Host.Duels.Running.Count
                    + ", finished " + rig.Server.Host.IsFinished + ", status " + rig.Server.Status + ", results " + a.Client.View.Results.Count + "/" + b.Client.View.Results.Count + ", end " + (a.Client.View.End != null));
                _output.WriteLine(a.Summary());
                _output.WriteLine("combat ticks " + combatTicks + ", max horizontal " + maxHorizontalSeen.ToString("0.00") + " m/s of " + maxHorizontal.ToString("0.00") +
                                  ", max rise " + maxRise.ToString("0.00") + " m/s");
                Assert.True(done, "the first duel did not finish");
                Assert.True(combatTicks > 600);
                Assert.True(maxHorizontalSeen > GlobalRules.Movement.WalkSpeed, "the random commands never moved the players");
                Assert.Equal(0, rig.Server.Transport.Counters.OversizedDrops);
                foreach (HeadlessPlayer p in new[] { a, b }) Assert.Equal(0, p.Client.RejectedMessages);
                foreach (string m in rig.Server.Metrics) Assert.Contains("\"violations\":0", m);
            }
        }

        private static bool Finite(Vec3 v) => !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
    }
}
