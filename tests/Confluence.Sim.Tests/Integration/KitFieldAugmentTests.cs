using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Content.AugmentsA;
using Confluence.Sim.Tests.Content.Halen;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Tests.Integration
{
    /// <summary>
    /// Stage 2 merge: the augments-a Fieldcraft family on the kits' own field deliveries. WIZ-002's SPL-012 Ash Curtain is
    /// an ash cloud counted as a field (spec L128) and WIZ-010's SPL-078 Riptide Lane a stream of lane fields; both carry
    /// the "field" tag the Economy drafts these records for (spec L243). Through the shared field shape
    /// (Spells/Deliveries/FieldShapes.cs) the records edit them and price their placements like framework fields (L543).
    /// </summary>
    public sealed class KitFieldAugmentTests
    {
        private const string Curtain = "SPL-012";
        private const string Riptide = "SPL-078";

        /// <summary>Bram with Fireball, the real Ash Curtain in slot 1 and Phoenix Rush.</summary>
        private static DuelWorld Bram(IEnumerable<ActiveAugment> augments, ArenaLayout layout = null)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, augments);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-009", Curtain, "SPL-013" });
            DuelWorld w = AKit.Duel(a, null, layout);
            AKit.FaceOff(w, 14f);
            AKit.Step(w, 1);
            return w;
        }

        /// <summary>Halen with the real Riptide Lane in slot 0.</summary>
        private static HalenDuel Halen(IEnumerable<ActiveAugment> augments)
        {
            DuelPlayerSetup a = DuelPlayerSetup.FromCatalogue(AKit.Catalogue, "A", AKit.Halen,
                new[] { Riptide, "SPL-073", "SPL-074", "SPL-075", "SPL-076", "SPL-077" }, null, augments);
            DuelWorld w = AKit.Duel(a);
            AKit.FaceOff(w, 14f);
            var d = new HalenDuel(w);
            d.AimAt(w.Player(0).Position + new Vec3(0f, 0f, 6f)); // the lane starts on the floor 6 m ahead
            return d;
        }

        private static void CastCurtain(DuelWorld w, PlayerInput? aim = null)
        {
            PlayerInput input = AKit.SelectSlot(aim ?? AKit.A, 1);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, AKit.NormalHold, input);
            AKit.Step(w, 70, input);
        }

        private static AshCloudEntity Cloud(DuelWorld w) => ((SpellRuntime)w.Spells).Entities.OfType<AshCloudEntity>().Single(c => c.Alive);

        private static ObscurationVolume Volume(DuelWorld w, AshCloudEntity cloud) => ((ReactionMaterials)w.Materials).World.FindVolume(cloud.VolumeId);

        private static float Paid(DuelWorld w, string spellId) =>
            AKit.Events<ResourceEvent>(w).Where(e => e.Player == 0 && e.Change == ResourceChange.ManaSpent && e.SourceId == spellId).Select(e => e.Amount).Last();

        /// <summary>The last cast's payment, less returns through its refund ledger, plus drawbacks charged after commitment.</summary>
        private static float Net(DuelWorld w, string spellId, string recordId)
        {
            CastDuelEvent commit = AKit.Casts(w, CastEventKind.Committed).Last(c => c.SpellId == spellId);
            float refunded = AKit.Casts(w, CastEventKind.Refunded).Where(c => c.CastId == commit.CastId).Sum(c => c.Cast.Mana);
            float charged = AKit.Events<ResourceEvent>(w).Where(e => e.Player == 0 && e.Change == ResourceChange.ManaSpent && e.SourceId == recordId).Sum(e => e.Amount);
            return Paid(w, spellId) - refunded + charged;
        }

        private static int Windup(DuelWorld w, string spellId)
        {
            CastDuelEvent commit = AKit.Casts(w, CastEventKind.Committed).Last(c => c.SpellId == spellId);
            CastDuelEvent release = AKit.Casts(w, CastEventKind.Released).Single(c => c.CastId == commit.CastId);
            return release.Tick - commit.Tick;
        }

        [Theory]
        [InlineData(1, 0.2f)]
        [InlineData(2, 0.35f)]
        [Verifies("AUG-049")]
        public void Wide_Foundation_Widens_The_Ash_Cloud_And_The_Riptide_Lane_For_Two_Mana(int rank, float metres)
        {
            DuelWorld w = Bram(AKit.Aug(WideFoundation.Id, rank));
            Assert.Equal(3f + metres, ((AshCloudDelivery)w.Player(0).Caster.FormFor(Curtain).Delivery).Radius, 3); // "3 m-radius"
            CastCurtain(w);
            AshCloudEntity cloud = Cloud(w);
            Assert.Equal(3f + metres, cloud.Footprint.Radius, 3);
            Assert.NotNull(Volume(w, cloud));
            Assert.Equal(20f + WideFoundation.ExtraMana, Paid(w, Curtain), 3);

            HalenDuel d = Halen(AKit.Aug(WideFoundation.Id, rank));
            Assert.Equal(2f + 2f * metres, ((StreamDelivery)d.P0.Caster.FormFor(Riptide).Delivery).Width, 3); // "2 m-wide"
            d.Cast(0);
            d.Run(60);
            FieldEntity lane = d.Runtime.Entities.OfType<FieldEntity>().Single(f => f.SpellId == Riptide);
            Assert.Equal(1f + metres, lane.Footprint.HalfSize.X, 3); // a lane's half-width
            Assert.Equal(22f + WideFoundation.ExtraMana, Paid(d.World, Riptide), 3);
        }

        [Theory]
        [InlineData(1, 0.2f)]
        [InlineData(2, 0.35f)]
        [Verifies("AUG-049")]
        [Verifies("AUG-440")]
        public void Wide_Foundation_Widens_Both_Pool_Divide_Pools(int rank, float metres)
        {
            DuelPlayerSetup a = DuelPlayerSetup.FromCatalogue(AKit.Catalogue, "A", AKit.Halen,
                new[] { "SPL-076", "SPL-073", "SPL-074", "SPL-075", "SPL-077", "SPL-078" }, null,
                new[] { new ActiveAugment("AUG-440", 1), new ActiveAugment(WideFoundation.Id, rank) });
            DuelWorld w = AKit.Duel(a);
            AKit.FaceOff(w, 14f);
            var d = new HalenDuel(w);
            d.AimAt(w.Player(0).Position + new Vec3(0f, 0f, 6f));
            Assert.Equal(1.5f + metres, ((TwinPoolDelivery)d.P0.Caster.FormFor("SPL-076").Delivery).Radius, 3); // "two 1.5 m-radius pools"
            d.Cast(0);
            d.Run(60);
            List<FieldEntity> pools = d.Runtime.Entities.OfType<FieldEntity>().Where(f => f.SpellId == "SPL-076").ToList();
            Assert.Equal(2, pools.Count);
            Assert.All(pools, p => Assert.Equal(1.5f + metres, p.Footprint.Radius, 3));
            Assert.Equal(14f + WideFoundation.ExtraMana, Paid(w, "SPL-076"), 3);
        }

        [Theory]
        [InlineData(1, 0.5f)]
        [InlineData(2, 1f)]
        [Verifies("AUG-051")]
        public void Slow_Dissipation_Lengthens_The_Six_Second_Ash_Cloud_And_Stream(int rank, float seconds)
        {
            DuelWorld w = Bram(AKit.Aug(SlowDissipation.Id, rank));
            CastCurtain(w);
            ObscurationVolume ash = Volume(w, Cloud(w));
            Assert.Equal(AKit.Ticks(6f + seconds), ash.ExpiresTick - ash.CreatedTick); // "opaque ash cloud for 6 s"

            HalenDuel d = Halen(AKit.Aug(SlowDissipation.Id, rank));
            d.Cast(0);
            d.Run(60);
            FieldEntity lane = d.Runtime.Entities.OfType<FieldEntity>().Single(f => f.SpellId == Riptide);
            Assert.Equal(AKit.Ticks(6f + seconds), lane.Owned.ExpireTick - lane.Owned.CreatedTick); // "stream for 6 s"
        }

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-053")]
        public void Remote_Survey_Prices_Ash_Curtain_By_Where_The_Cloud_Appears(int rank, float discount)
        {
            DuelWorld far = Bram(AKit.Aug(RemoteSurvey.Id, rank));
            CastCurtain(far); // level aim: the cloud lands at the 16 m range
            Assert.InRange(Cloud(far).Ground.Z, 15.5f, 16.1f);
            Assert.Equal(20f, Paid(far, Curtain), 3);
            Assert.Equal(20f - discount, Net(far, Curtain, RemoteSurvey.Id), 3); // returned when the cloud appears beyond 8 m

            DuelWorld near = Bram(AKit.Aug(RemoteSurvey.Id, rank));
            CastCurtain(near, SpellTestKit.Aim(near, 0, near.Player(0).Position + new Vec3(0f, 0f, 2.5f)));
            Assert.True(Cloud(near).Ground.Z < 3f);
            Assert.Equal(20f + RemoteSurvey.NearMana, Paid(near, Curtain), 3); // "Fields within 3 m cost 2 extra mana."
            Assert.Equal(20f + RemoteSurvey.NearMana, Net(near, Curtain, RemoteSurvey.Id), 3);

            // A lane is placed where it starts: Riptide Lane placed 6 m ahead (its 10 m lane's middle 11 m away) is in
            // the middle band, so it is neither discounted nor charged.
            HalenDuel lane = Halen(AKit.Aug(RemoteSurvey.Id, rank));
            lane.Cast(0);
            lane.Run(60);
            FieldEntity stream = lane.Runtime.Entities.OfType<FieldEntity>().Single(f => f.SpellId == Riptide);
            Assert.InRange(stream.Center.Z, 10.5f, 11.5f);
            Assert.Equal(22f, Net(lane.World, Riptide, RemoteSurvey.Id), 3);
        }

        [Fact]
        [Verifies("AUG-065")]
        [Verifies("AUG-066")]
        public void Cover_Records_Bind_The_Ash_Cloud_And_Stream_To_The_Placement_Committed()
        {
            // Sheltered Work: aimed under a canopy 6 m ahead at commitment, turned to the open range before release: the
            // cloud is still created under the canopy (the outdoor windup cannot be dodged) and costs 10% less.
            ArenaLayout canopy = TestWorlds.Flat();
            canopy.Solids.Add(TestWorlds.Box(60, new Vec3(0f, 3f, 6f), new Vec3(4f, 0.3f, 4f), SolidKind.Platform));
            DuelWorld sheltered = Bram(AKit.Aug(ShelteredWork.Id, 2), canopy);
            TestWorlds.Place(sheltered, 0, new Vec3(0f, 0f, -6f), 0f);
            AKit.Step(sheltered, 1);
            PlayerInput under = AKit.SelectSlot(SpellTestKit.Aim(sheltered, 0, new Vec3(0f, 0f, 6f)), 1);
            AKit.Step(sheltered, 1, under);
            AKit.HoldAndRelease(sheltered, AKit.NormalHold, under);
            AKit.Step(sheltered, 70, AKit.SelectSlot(AKit.A, 1));
            Assert.InRange(Cloud(sheltered).Ground.Z, 5.5f, 6.5f);
            Assert.Equal(AKit.Ticks(0.5f), Windup(sheltered, Curtain));
            Assert.Equal(18f, Net(sheltered, Curtain, ShelteredWork.Id), 3);

            // Open Sky on the stream in the open: the 0.5 s windup is 3 ticks shorter at rank 2 (10% rounded down).
            HalenDuel open = Halen(AKit.Aug(OpenSky.Id, 2));
            open.Cast(0);
            open.Run(60);
            Assert.Equal(AKit.Ticks(0.5f) - 3, Windup(open.World, Riptide));
            Assert.Single(open.Runtime.Entities.OfType<FieldEntity>(), f => f.SpellId == Riptide);
        }
    }
}
