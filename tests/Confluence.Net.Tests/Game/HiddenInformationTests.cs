using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Duels;
using Confluence.Match;
using Confluence.Net.Game;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Serialization;
using Confluence.Net.Tests.TestSupport;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using Xunit.Abstractions;
using CastingButtons = Confluence.Casting.InputButtons;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// An invisible opponent beyond the 4 m silhouette (spec L120) is hidden on every channel the viewer receives
    /// (research §2.6.1): its snapshot entry is the frozen last-seen pose, its owner-attached entities are absent and carry
    /// no position anyway, and its events carry no position near it and no direction. The boundary is exactly the
    /// authoritative visibility rule, applied before serialization.
    /// </summary>
    public sealed class HiddenInformationTests
    {
        private readonly ITestOutputHelper _output;

        public HiddenInformationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static DuelWorld World()
        {
            var recipe = new ArenaRecipe(11UL, 1, 24, "BIO-001", new string[0]);
            ArenaLayout layout = GameRig.Generator.Generate(recipe.ToGeneratorRequest(GameRig.Catalogue)).Layout;
            var factory = new SimWorldFactory(GameRig.Catalogue);
            return factory.Create(layout, new DuelPlayerSetup { PlayerId = "A", WizardId = "WIZ-002" },
                new DuelPlayerSetup { PlayerId = "B", WizardId = "WIZ-010" }, 0, 5400, true);
        }

        private static void Veil(DuelWorld world, int side) =>
            world.ApplyStatus(side, StatusApplication.Of(StatusKind.Invisibility, "TEST-VEIL", side, 90));

        [Fact]
        public void The_Live_Pose_Is_Replicated_Exactly_Inside_The_4_Meter_Silhouette_And_Never_Beyond()
        {
            DuelWorld world = World();
            Vec3 at = world.Player(0).Position;
            Veil(world, 1);
            float radius = GlobalRules.Statuses.InvisibilitySilhouetteRadius;
            Assert.Equal(4f, radius); // spec L120 "retains a nearby silhouette inside 4 meters"

            // Stationary at 4.2 m and 4.5 m: no coordinates (the earlier 1 m + closing-speed margin sent both).
            foreach (float d in new[] { 4.2f, 4.5f, 9f })
            {
                world.Player(1).Motor = MotorState.At(at + new Vec3(d, 0f, 0f), 0f);
                Assert.False(FrameCapture.ReplicatesTo(world, 0, 1), d + " m");
            }

            // Sprinting towards the viewer just outside: still hidden (no closing-speed look-ahead).
            MotorState sprint = MotorState.At(at + new Vec3(4.1f, 0f, 0f), 270f);
            sprint.Velocity = new Vec3(-GlobalRules.Movement.SprintSpeed, 0f, 0f);
            world.Player(1).Motor = sprint;
            Assert.False(FrameCapture.ReplicatesTo(world, 0, 1));

            world.Player(1).Motor = MotorState.At(at + new Vec3(3.9f, 0f, 0f), 0f);
            Assert.True(FrameCapture.ReplicatesTo(world, 0, 1));
            Assert.True(FrameCapture.ReplicatesTo(world, 1, 0)); // the viewer is not invisible
        }

        [Fact]
        public void A_Hidden_Players_Frame_Entry_Is_The_Frozen_Last_Seen_Pose_And_Its_Movement_Events_Have_No_Position()
        {
            DuelWorld world = World();
            Vec3 at = world.Player(0).Position;
            world.Player(1).Motor = MotorState.At(at + new Vec3(3.5f, 0f, 0f), 0f);
            world.Step(PlayerInput.Idle(0f), PlayerInput.Idle(0f));
            Veil(world, 1);
            Assert.True(FrameCapture.ReplicatesTo(world, 0, 1));
            DuelFrame seen = FrameCapture.Capture(world, 1, 0);
            PlayerEntry lastSeen = seen.Players[1];

            // Walk away beyond the silhouette and jump: the viewer gets the old pose, no motion, and a jump without a place.
            Vec3 inward = world.Layout.Center - at;
            inward = new Vec3(inward.X, 0f, inward.Z) * (1f / Math.Max(0.01f, (float)Math.Sqrt(inward.X * inward.X + inward.Z * inward.Z)));
            world.Player(1).Motor = MotorState.At(at + inward * 9f, 90f);
            world.Player(1).Motor.Grounded = true;
            var jump = PlayerInput.Idle(90f);
            jump.Buttons = CastingButtons.Jump;
            world.Step(PlayerInput.Idle(0f), jump);
            Assert.False(FrameCapture.ReplicatesTo(world, 0, 1), "distance " + Vec3.Distance(world.Player(0).Position, world.Player(1).Position) +
                ", invisible " + world.Player(1).Statuses.IsInvisible + ", phase " + world.Phase);
            DuelFrame truth = FrameCapture.Capture(world, 1, 0);
            DuelFrame view = FrameCapture.ForViewer(truth, 0, false, lastSeen);
            PlayerEntry hidden = view.Players[1];
            Assert.True(hidden.Has(PlayerBits.Hidden));
            Assert.Equal(lastSeen.Position, hidden.Position);
            Assert.NotEqual(truth.Players[1].Position, hidden.Position);
            Assert.True(hidden.Velocity.X == 0 && hidden.Velocity.Y == 0 && hidden.Velocity.Z == 0);
            Assert.Empty(hidden.Statuses);
            Assert.Equal(-1, hidden.HealthDeci);

            var events = new List<WireDuelEvent>();
            DuelEventFilter.Convert(world, 0, events, 1, null);
            WireDuelEvent jumped = events.FirstOrDefault(e => e.Type == WireEventType.Movement && e.Player == 1);
            Assert.NotNull(jumped);
            Assert.False(jumped.PositionSet);
            Assert.False(jumped.DirectionSet);

            // The same step seen by the jumping player itself, or with the opponent visible, keeps the position.
            var own = new List<WireDuelEvent>();
            DuelEventFilter.Convert(world, 1, own, -1, null);
            Assert.True(own.First(e => e.Type == WireEventType.Movement && e.Player == 1).PositionSet);
        }

        [Fact]
        public void Owner_Attached_Entities_Carry_No_Position_And_A_Hidden_Owners_Are_Not_Sent()
        {
            var truth = new DuelFrame { DuelId = 1, Tick = 10 };
            truth.Players[0] = new PlayerEntry();
            truth.Players[1] = new PlayerEntry();
            var p = new QuantizedVec3(1000, 0, 2000);
            truth.Entities.Add(new EntityEntry(1, EntityKind.Projectile, 1, "SPL-A", p, default, 10, 0));
            truth.Entities.Add(new EntityEntry(2, EntityKind.Buff, 1, "SPL-B", default, default, 0, 99));
            truth.Entities.Add(new EntityEntry(3, EntityKind.Movement, 0, "SPL-C", default, default, 0, 0));
            truth.Entities.Add(new EntityEntry(4, EntityKind.Field, 1, "SPL-D", p, default, 200, 0));
            truth.Entities.Add(new EntityEntry(5, EntityKind.Other, 1, "SPL-E", default, default, 0, 0));

            DuelFrame hidden = FrameCapture.ForViewer(truth, 0, false, null);
            Assert.Equal(new[] { 1, 3, 4 }, hidden.Entities.Select(e => e.Key).ToArray());
            Assert.Equal(5, truth.Entities.Count); // the truth (and the other viewer's frame) is unchanged
            DuelFrame visible = FrameCapture.ForViewer(truth, 0, true, null);
            Assert.Equal(5, visible.Entities.Count);
            Assert.True(EntityEntry.IsOwnerAttached(EntityKind.Buff) && EntityEntry.IsOwnerAttached(EntityKind.Movement) && EntityEntry.IsOwnerAttached(EntityKind.Other));
            Assert.False(EntityEntry.IsOwnerAttached(EntityKind.Projectile) || EntityEntry.IsOwnerAttached(EntityKind.Field) || EntityEntry.IsOwnerAttached(EntityKind.Tether));
        }

        /// <summary>
        /// A networked duel where one player is kept invisible: every frame and event the other client receives while it is
        /// hidden carries nothing that locates it.
        /// </summary>
        [Fact]
        public void An_Invisible_Opponent_Beyond_4_Meters_Is_Never_Located_By_Any_Frame_Or_Event_The_Viewer_Receives()
        {
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Duel,
                PlayerCount = 2,
                Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
                Laboratory = true,
            };
            using (var rig = new GameRig(settings))
            {
                HeadlessPlayer viewer = rig.AddPlayer("Ash", "WIZ-010", seed: 1);
                HeadlessPlayer veiled = rig.AddPlayer("Brook", "WIZ-002", seed: 2);

                viewer.Client.InputProvider = (duel, tick) => PlayerInput.Idle(duel.HasPrediction ? duel.Predicted.YawDeg : 0f); // a passive viewer
                DuelWorld world = null;
                int castSlot = 0;

                // The veiled player keeps preparing its first spell (hold, release) while walking, so preparation,
                // cue, cancel and commit events of a hidden caster are produced (spec L120: invisibility breaks at commit).
                veiled.Client.InputProvider = (duel, tick) =>
                {
                    PlayerInput input = veiled.Pilot.NextInput(duel, tick);
                    input.SelectedSlot = castSlot;
                    input.Buttons = tick % 90 < 40 ? input.Buttons | CastingButtons.Primary : input.Buttons & ~CastingButtons.Primary;
                    return input;
                };
                int viewerSide = -1, hiddenSide = -1;
                int hiddenEvents = 0, hiddenCastEvents = 0, hiddenSteps = 0, visibleSteps = 0, hiddenFrames = 0;
                var failures = new List<string>();

                rig.Server.Sent += (seat, type, message) =>
                {
                    if (world == null || seat != viewer.Client.View.Seat || !(message is DuelEventsMessage batch)) return;
                    if (world.IsVisibleTo(viewerSide, hiddenSide)) return;
                    Vec3 truth = world.Player(hiddenSide).Position;
                    foreach (WireDuelEvent e in batch.Events)
                    {
                        bool about = e.Player == hiddenSide || (e.Type == WireEventType.Damage && e.Target == hiddenSide);
                        if (!about) continue;
                        hiddenEvents++;
                        if (e.Type == WireEventType.Cast) hiddenCastEvents++;
                        if (e.PositionSet && (e.Type == WireEventType.Cast || e.Type == WireEventType.Movement ||
                            Vec3.Distance(e.Position, truth) <= DuelEventFilter.ConcealRadiusMeters))
                        {
                            failures.Add("t" + world.Tick + " " + e.Type + "/" + e.Kind + " at " + e.Position + " (hidden at " + truth + ")");
                        }

                        if (e.DirectionSet && e.Player == hiddenSide) failures.Add("t" + world.Tick + " " + e.Type + "/" + e.Kind + " carries a direction");
                    }
                };

                PlayerEntry frozen = null;
                int frozenTick = -1;
                bool done = rig.RunUntil(() =>
                {
                    if (world == null && rig.Server.Host != null && rig.Server.Host.Duels.Running.Count > 0)
                    {
                        RunningDuel duel = rig.Server.Host.Duels.Running[0];
                        world = duel.World;
                        viewerSide = duel.Seats[0].Player == viewer.Client.View.Seat ? 0 : 1;
                        hiddenSide = 1 - viewerSide;
                        var machine = world.Player(hiddenSide).Caster.Machine;
                        for (int s = machine.SlotCount - 1; s >= 0; s--)
                            if (machine.SpellInSlot(s) != null) castSlot = s;
                    }

                    if (world == null) return false;
                    if (world.Phase == DuelPhase.Combat && world.Player(hiddenSide).Alive && !world.Player(hiddenSide).Statuses.IsInvisible) Veil(world, hiddenSide);
                    if (world.Phase == DuelPhase.Combat)
                    {
                        if (world.IsVisibleTo(viewerSide, hiddenSide)) visibleSteps++;
                        else hiddenSteps++;
                    }

                    DuelFrame latest = viewer.Client.Duel?.Latest;
                    if (latest != null && latest.DuelId == viewer.Client.Duel.DuelId && latest.Tick != frozenTick)
                    {
                        int opponent = 1 - viewer.Client.Duel.Side;
                        PlayerEntry o = latest.Players[opponent];
                        if (o.Has(PlayerBits.Hidden))
                        {
                            hiddenFrames++;
                            if (o.Velocity.X != 0 || o.Velocity.Y != 0 || o.Velocity.Z != 0 || o.CastPhase != 0 || o.Statuses.Count != 0 || o.HealthDeci != -1)
                            {
                                failures.Add("frame " + latest.Tick + ": hidden entry carries motion or state");
                            }

                            if (frozen != null && !frozen.Position.Equals(o.Position)) failures.Add("frame " + latest.Tick + ": hidden pose moved");
                            frozen = o;
                            foreach (EntityEntry e in latest.Entities)
                            {
                                if (e.Owner == opponent && EntityEntry.IsOwnerAttached(e.Kind)) failures.Add("frame " + latest.Tick + ": owner-attached entity " + e.Key);
                            }
                        }
                        else
                        {
                            frozen = null;
                        }

                        foreach (EntityEntry e in latest.Entities)
                        {
                            if (EntityEntry.IsOwnerAttached(e.Kind) && !e.Position.Equals(default(QuantizedVec3))) failures.Add("frame " + latest.Tick + ": attached entity with a position");
                        }

                        frozenTick = latest.Tick;
                    }

                    return viewer.Client.View.Results.Count >= 1 || hiddenSteps > 60 * 40;
                }, 60 * 60 * 20);

                _output.WriteLine("hidden steps " + hiddenSteps + ", visible steps " + visibleSteps + ", hidden frames " + hiddenFrames +
                                  ", hidden-player events " + hiddenEvents + " (casts " + hiddenCastEvents + ")");
                foreach (string f in failures.Take(20)) _output.WriteLine(f);
                Assert.True(done, "the duel did not run");
                Assert.True(hiddenSteps > 300, "the opponent was hardly hidden");
                Assert.True(hiddenFrames > 30, "the viewer never received a hidden frame");
                Assert.True(hiddenEvents > 0, "no events about the hidden player were observed");
                Assert.True(hiddenCastEvents > 0, "the hidden player never prepared a spell");
                Assert.Empty(failures);
            }
        }
    }
}
