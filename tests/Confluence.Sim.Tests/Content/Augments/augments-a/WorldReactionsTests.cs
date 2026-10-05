using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>The "World reactions" shared augments in real duels with reaction materials and catalogue props.</summary>
    public class WorldReactionsTests
    {
        private static ArenaLayout CrateAt(float z)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.Add(WorldDuels.Prop(1, "PROP-006", new Vec3(0f, 0f, z), new Vec3(1f, 2.4f, 1f)));
            return layout;
        }

        private static void Fire(DuelWorld w, int slot = 0, PlayerInput? aim = null)
        {
            PlayerInput input = AKit.SelectSlot(aim ?? AKit.A, slot);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, AKit.NormalHold, input);
            AKit.Step(w, 40, input);
        }

        private static float PropDamage(DuelWorld w) =>
            AKit.Events<PropEvent>(w).Where(e => e.Change == PropChange.Damaged || e.Change == PropChange.Destroyed).Sum(e => e.Amount);

        // ----- AUG-125 Dry Kiln ----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.10f)]
        [InlineData(2, 0.16f)]
        [Verifies("AUG-125")]
        public void Dry_Kiln_Raises_Heat_Damage_To_A_Dry_Prop(int rank, float bonus)
        {
            AKit.AssertQuotes(DryKiln.Id, "Your Heat effect hits a dry destructible.", "Prop damage increases 10%.", "Prop damage increases 16%.", "Direct player damage decreases 2%.");
            DuelWorld w = AKit.With(DryKiln.Id, rank, layout: CrateAt(4f));
            AKit.FaceOff(w, 12f);
            Fire(w);
            Assert.Equal(48f * (1f + bonus), PropDamage(w), 2);
        }

        [Fact]
        [Verifies("AUG-125")]
        public void Dry_Kiln_Skips_Wet_Props_And_Non_Heat_Spells()
        {
            // A crate standing in water is not dry.
            DuelWorld wet = AKit.With(DryKiln.Id, 2, layout: CrateAt(4f));
            AKit.FaceOff(wet, 12f);
            wet.QueueDeposit(new WorldDeposit { Kind = DepositKind.Water, Strength = 2, Position = new Vec3(0f, 0f, 4f), Radius = 1.5f, LifetimeTicks = AKit.Ticks(6f), SourcePlayer = 1, SourceId = "SPL-TEST-WATER" });
            AKit.Step(wet, 2);
            Assert.True(((Confluence.Sim.World.Materials.ReactionMaterials)wet.Materials).World.IsWetAt(new Vec3(0f, 0f, 4f)));
            Fire(wet);
            Assert.Equal(48f, PropDamage(wet), 2);

            // A Water (physical) projectile on a dry crate.
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(DryKiln.Id, 2));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.Add("SPL-073");
            DuelWorld water = AKit.Duel(a, null, CrateAt(4f), AKit.RegistryWith(a, AKit.Spell("SPL-073", new TestBolt(32f))));
            AKit.FaceOff(water, 12f);
            Fire(water);
            Assert.Equal(32f, PropDamage(water), 2);
        }

        [Fact]
        [Verifies("AUG-125")]
        public void Dry_Kiln_Drawback_Cuts_Direct_Player_Damage_Only()
        {
            DuelWorld w = AKit.With(DryKiln.Id, 1);
            AKit.FaceOff(w, 6f);
            Fire(w);
            Assert.Equal(48f * 0.98f, AKit.Hits(w, 1, DamageKind.Direct).Single().Resolution.Raw, 3);

            DuelWorld splash = AKit.With(DryKiln.Id, 1);
            AKit.FaceOff(splash, 6f);
            Fire(splash, aim: SpellTestKit.Aim(splash, 0, new Vec3(1.2f, 0f, 6f)));
            Assert.Equal(28f, AKit.Hits(splash, 1, DamageKind.Splash).Single().Resolution.Raw, 3);
        }
    }
}
