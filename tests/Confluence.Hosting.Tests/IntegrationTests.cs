using System.Linq;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using Confluence.Hosting.Duels;
using Confluence.Hosting.Economics;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.World;
using CastEventKind = Confluence.Casting.CastEventKind;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Hosting.Tests
{
    /// <summary>
    /// The Stage 1b stages meeting in one authoritative duel built the way the host builds it: an Economy loadout mapped
    /// by <see cref="LoadoutMapper"/>, a generated arena, and <see cref="SimWorldFactory"/> (spell runtime, reaction
    /// world, props, effects).
    /// </summary>
    public sealed class IntegrationTests
    {
        private static Catalogue Cat => HostFixture.Catalogue;
        private static readonly IStructuralChangeSource NoChanges = new StructuralChangeTable();

        private static ArenaLayout Arena(ulong seed, int diameter = 24) =>
            HostFixture.Generator.Generate(new ArenaRequest { MatchSeed = seed, Round = 1, Diameter = diameter }).Layout;

        private static void Buy(Loadout loadout, string id, EquipmentSlot slot)
        {
            EquipmentDef def = Cat.Equipment(id);
            Assert.Equal(TransactionCode.Success, loadout.AddItem(def, def.Price, Placement.In(slot), out _));
        }

        private static void Draft(Loadout loadout, string id)
        {
            AcquisitionResult result = AugmentAcquisition.AcquireNew(loadout, Cat.Augment(id), new RoundInfo(MatchMode.Conclave, 1), null, null, NoChanges);
            Assert.True(result.Succeeded, id + " " + result.Code);
        }

        [Fact]
        [Verifies("EQP-051")]
        [Verifies("EQP-096")]
        [Verifies("EQP-066")]
        [Verifies("AUG-145")]
        [Verifies("AUG-169")]
        [Verifies("AUG-175")]
        public void The_Six_Proof_Records_Shape_A_Real_Duel_Started_From_Mapped_Economy_Loadouts()
        {
            // Bram buys the three tier-1 proof items and drafts the three Silver proof augments (all rank 1).
            var bram = new Loadout(Cat, "WIZ-002");
            Buy(bram, "EQP-051", EquipmentSlot.Armor);     // Training Cuirass: +15 health; sprint +1 stamina/s
            Buy(bram, "EQP-096", EquipmentSlot.TrinketA);  // Pocket Reservoir: +8 mana; -4 stamina
            Buy(bram, "EQP-066", EquipmentSlot.Clothing);  // Apprentice Mantle: +8% mana regeneration; -8 health
            Draft(bram, "AUG-145");                        // Large Vessel: +8 mana; -4 stamina
            Draft(bram, "AUG-169");                        // Healthy Habit: +12 health; -3 mana
            Draft(bram, "AUG-175");                        // Opening Ward: 12 reserve shield at round start; -5 health
            var halen = new Loadout(Cat, "WIZ-010");

            MappedLoadout side0 = LoadoutMapper.Map(bram, "P0", 0);
            MappedLoadout side1 = LoadoutMapper.Map(halen, "P1", 1);
            Assert.Equal(new[] { "AUG-145", "AUG-169", "AUG-175" }, side0.ActiveAugments.Select(a => a.Id).OrderBy(x => x));
            Assert.Equal(new[] { "EQP-051", "EQP-066", "EQP-096" }, side0.Setup.Equipment.Select(e => e.Id).OrderBy(x => x));

            int alcove = Ticks.FromSeconds(GlobalRules.Round.AlcoveSec);
            var factory = new SimWorldFactory(Cat);
            DuelWorld world = factory.Create(Arena(HostFixture.SmallDuelSeed), side0.Setup, side1.Setup, alcove, Ticks.FromSeconds(GlobalRules.Round.CombatMaxSec), true);
            DuelPlayer p = world.Player(0);
            DuelPlayer plain = world.Player(1);

            // Static capacity, effects and drawbacks together, filled at round start (spec L114, L579).
            Assert.Equal(GlobalRules.Resources.BaseHealth + 15f - 8f + 12f - 5f, p.Resources.MaxHealth, 3);
            Assert.Equal(p.Resources.MaxHealth, p.Resources.Health, 3);
            Assert.Equal(GlobalRules.Resources.BaseMana + 8f + 8f - 3f, p.Resources.MaxMana, 3);
            Assert.Equal(p.Resources.MaxMana, p.Resources.Mana, 3);
            Assert.Equal(GlobalRules.Resources.BaseStamina - 4f - 4f, p.Resources.MaxStamina, 3);
            Assert.Equal(GlobalRules.Resources.ManaRegenPerSecond * 1.08f, p.Stats.ManaRegenPerSecond, 3);
            Assert.Equal(GlobalRules.Movement.SprintStaminaPerSecond + 1f, p.Stats.SprintStaminaPerSecond, 3);
            ShieldEntry ward = Assert.Single(p.Shields.Entries);
            Assert.True(ward.Reserve);
            Assert.Equal("AUG-175", ward.SourceId);
            Assert.Equal(12f, ward.Amount, 3);

            // The unequipped player keeps the base numbers.
            Assert.Equal(GlobalRules.Resources.BaseHealth, plain.Resources.MaxHealth, 3);
            Assert.Equal(GlobalRules.Resources.BaseMana, plain.Resources.MaxMana, 3);
            Assert.Empty(plain.Shields.Entries);

            // Into combat: a real Fireball commitment through WIZ-002's grammar pays 18 from the larger pool, and the
            // Mantle's regeneration refills it at 10.8/s once the 1.5 s delay has passed (spec L78).
            for (int i = 0; i <= alcove; i++) world.Step(PlayerInput.Idle(p.Motor.YawDeg), PlayerInput.Idle(plain.Motor.YawDeg));
            Assert.Equal(DuelPhase.Combat, world.Phase);
            PlayerInput hold = PlayerInput.Idle(p.Motor.YawDeg).With(InputButtons.Primary);
            for (int i = 0; i < 15; i++) world.Step(hold, PlayerInput.Idle(plain.Motor.YawDeg));
            world.Step(PlayerInput.Idle(p.Motor.YawDeg), PlayerInput.Idle(plain.Motor.YawDeg));
            CastDuelEvent commit = world.EventLog.OfType<CastDuelEvent>().Single(c => c.Cast.Kind == CastEventKind.Committed);
            Assert.Equal("SPL-009", commit.Cast.SpellId);
            Assert.Equal(18f, commit.Cast.Mana, 3);               // the record's 18 mana, paid in full at commitment
            float afterCommit = p.Resources.MaxMana - 18f;
            Assert.Equal(afterCommit, p.Resources.Mana, 3);       // still inside the 1.5 s regeneration delay
            int idleTicks = Ticks.FromSeconds(GlobalRules.Resources.ManaRegenDelaySec + 1f) - 1;
            for (int i = 0; i < idleTicks; i++) world.Step(PlayerInput.Idle(p.Motor.YawDeg), PlayerInput.Idle(plain.Motor.YawDeg));
            Assert.InRange(p.Resources.Mana, afterCommit + 10.8f - 0.3f, afterCommit + 10.8f + 0.3f);

            // The Opening Ward absorbs the first 12 of a hit; the rest reaches the larger health pool.
            float health = p.Resources.Health;
            world.ApplyDamage(DamageRequest.Single(1, 0, DamageKind.Direct, "SPL-TEST", DamageChannel.Physical, 20f).From(plain.Position));
            Assert.Equal(0f, p.Shields.Total, 3);
            Assert.Equal(health - 8f, p.Resources.Health, 3);
        }

        [Fact]
        public void Generated_Prop_Collision_Boxes_Become_Sim_Solids_That_Block_Movement_And_Sight()
        {
            ulong seed = Enumerable.Range(1, 40).Select(k => (ulong)(7919 * k))
                .First(s => Arena(s).Props.Any(prop => prop.HasCollision));
            ArenaLayout layout = Arena(seed);
            DuelWorld world = new SimWorldFactory(Cat).Create(layout.Clone(), DuelPlayerSetup.FromCatalogue(Cat, "P0", "WIZ-002"),
                DuelPlayerSetup.FromCatalogue(Cat, "P1", "WIZ-010"), 0, 600, false);

            foreach (PropPlacement placement in layout.Props)
            {
                var body = world.Props.Find(placement.Id);
                Assert.NotNull(body);
                if (!placement.HasCollision)
                {
                    Assert.Null(body.Solid); // floor-level props have no body (Arena's HasCollision false)
                    continue;
                }

                // The Sim collides with exactly the box the Arena generator stored and validated (spec L171-L173).
                Assert.NotNull(body.Solid);
                Assert.Equal(placement.CollisionBox.Center, body.Solid.Box.Center);
                Assert.Equal(placement.CollisionBox.HalfExtents, body.Solid.Box.HalfExtents);
                Assert.Equal(placement.CollisionBox.YawDeg, body.Solid.Box.YawDeg, 4);
                Assert.True(world.Props.IsPropSolid(body.Solid.Id));

                // A horizontal line through the box center is blocked by it.
                YawBox box = placement.CollisionBox;
                Vec3 center = box.Center;
                float reach = box.HalfExtents.X + box.HalfExtents.Z + 1f;
                Vec3 from = center - new Vec3(reach, 0f, 0f);
                Vec3 to = center + new Vec3(reach, 0f, 0f);
                Assert.False(world.Collision.HasLineOfSight(from, to), placement.PropId + " at " + center);
            }
        }
    }
}
