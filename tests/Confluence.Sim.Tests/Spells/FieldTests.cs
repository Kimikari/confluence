using System.Linq;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Combat;
using Confluence.Sim.Fields;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>Fields: footprints, enter/tick/exit payloads, neutrality, the three-field limit, lifetime caps, and deposits.</summary>
    public class FieldTests
    {
        private static SpellForm Field(string id = "SPL-T30", float lifetime = 4f, bool affectsOwner = true, PlacementMode placement = PlacementMode.AtCaster,
            FieldShape shape = FieldShape.Circle, float radius = 3f, float tick = 5f, DepositKind? deposit = null)
        {
            SpellFormBuilder b = SpellTestKit.Form(id, 5f, 0.1f, 0f, 12f, DamageChannel.Corrosive, "field");
            var field = new FieldDelivery
            {
                Shape = shape,
                Radius = radius,
                Length = 10f,
                Width = 2f,
                Placement = placement,
                LifetimeSec = lifetime,
                TickIntervalSec = 0.5f,
                AffectsOwner = affectsOwner,
                TickPayload = new PayloadSpec { Direct = tick, Channel = DamageChannel.Corrosive },
                EnterPayload = new PayloadSpec(),
            };
            field.EnterPayload.AddStatus(StatusKind.Slow, 1f, 0.2f);
            b.Delivery = field;
            if (deposit.HasValue) b.Payload.AddDeposit(deposit.Value, 1, DepositPlacement.Footprint);
            return b.Build();
        }

        [Fact]
        public void A_Field_Ticks_Everyone_Inside_Including_Its_Owner_And_Applies_Its_Enter_Payload()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Field()));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -4f), 180f);
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, Ticks.FromSeconds(1.0));
            Assert.Equal(3, SpellTestKit.DamageTo(w, 1).Count); // ticks at 0, 0.5 and 1.0 s
            Assert.Equal(3, SpellTestKit.DamageTo(w, 0).Count); // ownership grants no immunity (spec L128)
            Assert.All(SpellTestKit.DamageTo(w, 1), d => Assert.Equal(DamageKind.Splash, d.Kind)); // area contact, never guarded
            Assert.Contains(w.EventLog.OfType<StatusEvent>(), e => e.Player == 1 && e.Kind == StatusKind.Slow && e.Change == StatusChange.Applied);
            Assert.Equal(2, SpellTestKit.SpellEvents(w, SpellEventKind.FieldEntered).Count());
        }

        [Fact]
        public void A_Bound_Field_Spares_Its_Owner_And_Leaving_Stops_The_Ticks()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Field(affectsOwner: false)));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -4f), 180f);
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 20);
            Assert.Empty(SpellTestKit.DamageTo(w, 0));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 4f), 180f);
            SpellTestKit.Run(w, 60);
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.FieldExited));
            Assert.Equal(1, SpellTestKit.DamageTo(w, 1).Count);
        }

        [Fact]
        public void A_Lane_Covers_Its_Length_Along_The_Aim()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Field(shape: FieldShape.Lane, affectsOwner: false)));
            TestWorlds.Place(w, 1, new Vec3(0.5f, 0f, 2f), 180f); // 8 m along, inside the 2 m width
            SpellTestKit.Cast(w);
            Assert.True(SpellTestKit.Runtime(w).Entities.OfType<FieldEntity>().Single().IsInside(1));
            DuelWorld beside = SpellTestKit.Duel(SpellTestKit.Loadout(Field(shape: FieldShape.Lane, affectsOwner: false)));
            TestWorlds.Place(beside, 1, new Vec3(1.6f, 0f, 2f), 180f);
            SpellTestKit.Cast(beside);
            Assert.False(SpellTestKit.Runtime(beside).Entities.OfType<FieldEntity>().Single().IsInside(1));
        }

        [Fact]
        public void A_Rectangle_Is_Centered_On_Its_Placement_And_A_Ring_Keeps_Its_Center_Safe()
        {
            DuelWorld rect = SpellTestKit.Duel(SpellTestKit.Loadout(Field(shape: FieldShape.Rectangle, affectsOwner: false)));
            TestWorlds.Place(rect, 1, new Vec3(0.5f, 0f, -2f), 180f); // 4 m ahead of the center, inside the 10 m length
            SpellTestKit.Cast(rect);
            Assert.True(SpellTestKit.Runtime(rect).Entities.OfType<FieldEntity>().Single().IsInside(1));
            Assert.False(SpellTestKit.Runtime(rect).Entities.OfType<FieldEntity>().Single().Contains(new Vec3(0f, 0f, 0f)));

            SpellFormBuilder b = SpellTestKit.Form("SPL-T35", 5f, 0.1f, 0f, 12f, DamageChannel.Thermal, "field");
            b.Delivery = new FieldDelivery { Radius = 3f, InnerRadius = 1.5f, Placement = PlacementMode.AtCaster, LifetimeSec = 4f };
            DuelWorld ring = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            TestWorlds.Place(ring, 1, new Vec3(0f, 0f, -4f), 180f);
            SpellTestKit.Cast(ring);
            FieldEntity field = SpellTestKit.Runtime(ring).Entities.OfType<FieldEntity>().Single();
            Assert.False(field.IsInside(0)); // the caster stands in the safe center
            Assert.True(field.IsInside(1));
        }

        [Fact]
        public void A_Fourth_Field_Retires_The_Owners_Oldest()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Field("SPL-T31"), Field("SPL-T32"), Field("SPL-T33"), Field("SPL-T34")));
            for (int slot = 0; slot < 4; slot++)
            {
                SpellTestKit.Run(w, 30, TestWorlds.A.Selecting(slot)); // past the 0.45 s field recovery (spec L92)
                SpellTestKit.Cast(w, TestWorlds.A.Selecting(slot));
            }

            SpellTestKit.Run(w, 2);
            Assert.Equal(3, w.Owned.Count(0, OwnedEntityKind.Field)); // spec L128
            FieldEvent retired = w.EventLog.OfType<FieldEvent>().Single(e => e.Change == FieldChange.Retired);
            Assert.Equal("SPL-T31", retired.DefinitionId);
            Assert.DoesNotContain(SpellTestKit.Runtime(w).Entities.OfType<FieldEntity>(), f => f.SpellId == "SPL-T31");
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.FieldEnded), e => e.SpellId == "SPL-T31");
        }

        [Fact]
        public void Lifetime_Is_Capped_At_Eight_Seconds()
        {
            SpellForm form = Field(lifetime: 12f);
            Assert.Equal(GlobalRules.FieldsAndSummons.MaxLifetimeSec, ((FieldDelivery)form.Delivery).LifetimeSec);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(form));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, Ticks.FromSeconds(8.0) + 2);
            Assert.Empty(SpellTestKit.Runtime(w).Entities.OfType<FieldEntity>());
            Assert.Contains(w.EventLog.OfType<FieldEvent>(), e => e.Change == FieldChange.Expired);
        }

        [Fact]
        public void Creation_Deposits_Cover_The_Footprint_For_The_Fields_Lifetime()
        {
            var materials = new RecordingMaterials();
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Field(placement: PlacementMode.AtAimPoint, deposit: DepositKind.Water)), materials: materials);
            SpellTestKit.Cast(w, SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, 0f)));
            WorldDeposit d = materials.Deposits.Single();
            OwnedEntity owned = w.Owned.Entities.Single();
            Assert.Equal(DepositKind.Water, d.Kind);
            Assert.Equal(FootprintShape.Circle, d.Footprint.Shape);
            Assert.Equal(3f, d.Footprint.Radius);
            Assert.Equal(owned.Id, d.FieldId);
            Assert.Equal(Ticks.FromSeconds(4.0), d.LifetimeTicks);
            Assert.Equal(0f, d.Footprint.A.X, 2);
            Assert.Equal(0f, d.Footprint.A.Y, 1);
        }
    }
}
