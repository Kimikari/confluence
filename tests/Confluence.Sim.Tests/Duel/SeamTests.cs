using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Input;
using InputButtons = Confluence.Casting.InputButtons;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Duel
{
    /// <summary>The spell (steps 3-4) and materials (step 5) seams the next stage connects.</summary>
    public class SeamTests
    {
        [Fact]
        public void Tick_Order_Calls_Casting_Then_Deliveries_Then_Materials()
        {
            var log = new List<string>();
            var spells = new FakeSpells(log);
            var materials = new FakeMaterials(log);
            DuelWorld w = TestWorlds.Duel(spells: spells, materials: materials);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.Equal(new[] { "attach", "cast0", "cast1", "deliveries", "materials" }, log.ToArray());
            Assert.True(spells.LastPermissions[0].CanCast);
        }

        [Fact]
        public void Casting_Permissions_Follow_The_Ward_And_Statuses()
        {
            var spells = new FakeSpells(null);
            DuelWorld w = TestWorlds.Duel(alcoveTicks: 2, spells: spells);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.False(spells.LastPermissions[0].CanCast);
            Assert.Equal(CastBlockReason.AlcoveWard, spells.LastPermissions[0].Reason);
            TestWorlds.Run(w, 2, TestWorlds.A, TestWorlds.B);
            Assert.True(spells.LastPermissions[0].CanCast);

            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Silence, "SPL-HUSH", 1, 30));
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.False(spells.LastPermissions[0].CanCast);
            Assert.False(spells.LastPermissions[0].CanUseSignature);
            Assert.True(spells.LastPermissions[0].CanUseItems);
            Assert.Equal(CastBlockReason.Silenced, spells.LastPermissions[0].Reason);
        }

        [Fact]
        public void Casting_Movement_Factor_Limits_Walk_Speed_And_Channels_Block_Sprint_And_Regeneration()
        {
            var spells = new FakeSpells(null);
            DuelWorld w = TestWorlds.Duel(spells: spells);
            w.TryCommitMana(0, 30f, "SPL-TEST");
            spells.Status[0] = new CastingStatus { MovementFactor = GlobalRules.Casting.MoveSpeedFactorHeavyCast, Channeling = true, Busy = true };
            TestWorlds.RunSeconds(w, 3f, TestWorlds.A.Moving(0f, 1f).With(InputButtons.Sprint), TestWorlds.B);
            Assert.Equal(3.85f, w.Player(0).Motor.Velocity.LengthXZ(), 3);
            Assert.False(w.Player(0).Motor.Sprinting);
            Assert.Equal(70f, w.Player(0).Resources.Mana, 3); // the channel counts as spending
            spells.Status[0] = new CastingStatus { MovementFactor = 1f, Busy = true, InRecovery = true };
            w.Step(TestWorlds.A.With(InputButtons.Melee), TestWorlds.B);
            Assert.False(w.Player(0).Melee.Busy); // cast recovery blocks common melee (spec L92); other phases yield to it (L474)
        }

        [Fact]
        public void Sprinting_Forbids_Channels()
        {
            var spells = new FakeSpells(null);
            DuelWorld w = TestWorlds.Duel(spells: spells);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.True(spells.LastPermissions[0].CanChannel);
            w.Step(TestWorlds.A.Moving(0f, 1f).With(InputButtons.Sprint), TestWorlds.B);
            Assert.True(spells.LastPermissions[0].CanCast);
            Assert.False(spells.LastPermissions[0].CanChannel);
        }

        [Fact]
        public void Stun_And_Displacement_Interrupt_The_Cast_And_Credit_The_Interrupter()
        {
            var spells = new FakeSpells(null) { InterruptSucceeds = true };
            DuelWorld w = TestWorlds.Duel(spells: spells);
            w.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "SPL-STUN", 0, 30));
            Assert.Equal(InterruptCause.Stun, spells.Interrupts[0].Value);
            Assert.Equal(1, w.Player(0).Feats.Interrupts);

            DuelWorld v = TestWorlds.Duel(spells: spells);
            v.ApplyKnockback(1, 0, new Vec3(0f, 0f, 1f), 2f, "SPL-PUSH");
            Assert.Equal(InterruptCause.Displacement, spells.Interrupts[spells.Interrupts.Count - 1].Value);
            Assert.Equal(1, v.Player(0).Feats.Interrupts);
        }

        [Fact]
        public void Materials_Contact_Damage_Is_Environment_Damage_In_Request_Order()
        {
            var materials = new FakeMaterials(null);
            materials.Requests.Add(new ContactDamageRequest { Target = 1, SourcePlayer = 0, SourceId = "SPL-ZAP", Channel = DamageChannel.Electrical, Amount = 12f, Reaction = ReactionTag.Conductivity });
            materials.Requests.Add(new ContactDamageRequest { Target = 1, SourcePlayer = 0, SourceId = "SPL-ZAP", Channel = DamageChannel.Electrical, Amount = 8f, Reaction = ReactionTag.Conductivity });
            DuelWorld w = TestWorlds.Duel(materials: materials);
            TestWorlds.StartGuard(w); // guard does not stop reaction contact damage
            Assert.Equal(280f, w.Player(1).Resources.Health, 3);
            var amounts = new List<float>();
            foreach (DuelEvent e in w.TickEvents)
            {
                if (e is DamageEvent d)
                {
                    Assert.Equal(DamageKind.Environment, d.Kind);
                    Assert.Equal(GuardOutcome.None, d.Resolution.Guard);
                    amounts.Add(d.Resolution.HealthLost);
                }
            }

            Assert.Equal(new[] { 12f, 8f }, amounts.ToArray());
            Assert.Equal(20f, w.Player(0).Feats.ConductivityDamage, 3);
        }

        [Fact]
        public void Obscuring_Volumes_Block_Acquisition_At_2_Meters()
        {
            var materials = new FakeMaterials(null) { Obscuring = 1.9f };
            DuelWorld w = TestWorlds.Duel(materials: materials);
            Assert.True(w.CanAcquire(0, 1));
            materials.Obscuring = GlobalRules.World.SteamSightBlockPathLength;
            Assert.False(w.CanAcquire(0, 1));
        }

        [Fact]
        public void Slippery_Surfaces_From_Materials_Make_Players_Drift()
        {
            var materials = new FakeMaterials(null);
            DuelWorld normal = TestWorlds.Duel();
            DuelWorld icy = TestWorlds.Duel(materials: materials);
            TestWorlds.RunSeconds(normal, 1f, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            TestWorlds.RunSeconds(icy, 1f, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            materials.Surface = new SurfaceInfo { Kind = SurfaceKind.Ice };
            float z0 = normal.Player(0).Position.Z;
            TestWorlds.RunSeconds(normal, 1f, TestWorlds.A, TestWorlds.B);
            TestWorlds.RunSeconds(icy, 1f, TestWorlds.A, TestWorlds.B);
            Assert.True(icy.Player(0).Position.Z - z0 > 3f * (normal.Player(0).Position.Z - z0));
        }

        [Fact]
        public void Surface_Slows_Join_The_Strongest_Slow_Rule()
        {
            var materials = new FakeMaterials(null) { Surface = new SurfaceInfo { Kind = SurfaceKind.Sticky, SlowFraction = 0.5f } };
            DuelWorld w = TestWorlds.Duel(materials: materials);
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Slow, "SPL-ICE", 1, 120, 0.1f));
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            Assert.Equal(GlobalRules.Movement.WalkSpeed * 0.7f, w.Player(0).Motor.Velocity.LengthXZ(), 3);
        }

        [Fact]
        public void Deposits_Reach_The_Materials_Stage()
        {
            var materials = new FakeMaterials(null);
            DuelWorld w = TestWorlds.Duel(materials: materials);
            w.QueueDeposit(new WorldDeposit { Kind = DepositKind.Water, Strength = 2, Position = Vec3.Zero, SourcePlayer = 0, SourceId = "SPL-RAIN" });
            Assert.Single(materials.Deposits);
            Assert.Equal(2, materials.Deposits[0].Strength);
        }

        private sealed class FakeSpells : ISpellRuntime
        {
            private readonly List<string> _log;

            public FakeSpells(List<string> log)
            {
                _log = log;
                Status[0] = CastingStatus.Idle;
                Status[1] = CastingStatus.Idle;
            }

            public CastingStatus[] Status { get; } = new CastingStatus[2];
            public CastPermissions[] LastPermissions { get; } = new CastPermissions[2];
            public List<InterruptCause?> Interrupts { get; } = new List<InterruptCause?>();
            public bool InterruptSucceeds { get; set; }

            public void Attach(DuelWorld world) => _log?.Add("attach");

            public void UpdateCasting(int player, in PlayerInput input, in CastPermissions permissions)
            {
                LastPermissions[player] = permissions;
                _log?.Add("cast" + player);
            }

            public void AdvanceDeliveries() => _log?.Add("deliveries");

            public CastingStatus GetStatus(int player) => Status[player];

            public bool Interrupt(int player, InterruptCause cause)
            {
                Interrupts.Add(cause);
                return InterruptSucceeds && cause != InterruptCause.DuelEnded;
            }

            public void Hash(ref StateHasher hasher)
            {
            }
        }

        private sealed class FakeMaterials : IWorldMaterials
        {
            private readonly List<string> _log;

            public FakeMaterials(List<string> log)
            {
                _log = log;
            }

            public List<ContactDamageRequest> Requests { get; } = new List<ContactDamageRequest>();
            public List<WorldDeposit> Deposits { get; } = new List<WorldDeposit>();
            public float Obscuring { get; set; }
            public SurfaceInfo Surface { get; set; } = SurfaceInfo.Normal;

            public void QueueDeposit(in WorldDeposit deposit) => Deposits.Add(deposit);

            public void Step(IMaterialsContext context)
            {
                _log?.Add("materials");
                foreach (ContactDamageRequest r in Requests)
                {
                    context.RequestContactDamage(r);
                }

                Requests.Clear();
            }

            public float ObscuringPathLength(Vec3 from, Vec3 to) => Obscuring;

            public SurfaceInfo QuerySurface(Vec3 feet) => Surface;

            public void Hash(ref StateHasher hasher)
            {
            }
        }
    }
}
