using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>A field test behavior with an explicit shape under a real field record id (mana, range, windup from the record).</summary>
    internal sealed class ShapedField : ISpellBehavior
    {
        public FieldShape Shape = FieldShape.Circle;
        public float Radius = 3f;
        public float Length = 8f;
        public float Width = 2f;
        public float LifetimeSec = 6f;
        public float EnterDamage;
        public PlacementMode Placement = PlacementMode.AtAimPoint;

        public IReadOnlyList<ProseParameter> Parameters => Array.Empty<ProseParameter>();

        public void Build(SpellFormBuilder form)
        {
            var field = new FieldDelivery { Shape = Shape, Radius = Radius, Length = Length, Width = Width, LifetimeSec = LifetimeSec, Placement = Placement };
            if (EnterDamage > 0f)
            {
                field.EnterPayload = new PayloadSpec { Direct = EnterDamage, Channel = DamageChannel.Thermal };
            }

            form.Delivery = field;
        }
    }

    /// <summary>
    /// The field-requirement "Fieldcraft and terrain" shared augments on WIZ-002 with SPL-012 Ash Curtain (20 mana, 16 m,
    /// 0.5 s windup) and SPL-013 Phoenix Rush (28 mana, 0.35 s) under field test behaviors, plus Fireball (18 mana).
    /// </summary>
    public class FieldcraftFieldTests
    {
        private const string Curtain = "SPL-012";
        private const string Rush = "SPL-013";

        private static DuelWorld Duel(IEnumerable<ActiveAugment> augments, ShapedField curtain, ShapedField rush = null, ArenaLayout layout = null)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, augments);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-009", Curtain, Rush });
            DuelWorld w = AKit.Duel(a, null, layout, AKit.RegistryWith(a,
                AKit.Spell(Curtain, curtain ?? new ShapedField()),
                AKit.Spell(Rush, rush ?? new ShapedField { Radius = 1.5f, LifetimeSec = 3f, Placement = PlacementMode.AtCaster })));
            AKit.FaceOff(w, 14f);
            AKit.Step(w, 1);
            return w;
        }

        private static DuelWorld Duel(string id, int rank, ShapedField curtain = null, ShapedField rush = null, ArenaLayout layout = null) =>
            Duel(id == null ? null : AKit.Aug(id, rank), curtain, rush, layout);

        private static FieldDelivery FieldForm(DuelWorld w, string spellId = Curtain) => (FieldDelivery)w.Player(0).Caster.FormFor(spellId).Delivery;

        /// <summary>Casts a slot with a normal release and waits until the cast is over.</summary>
        private static void Cast(DuelWorld w, int slot, PlayerInput? aim = null, int waitTicks = 70)
        {
            PlayerInput input = AKit.SelectSlot(aim ?? AKit.A, slot);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, AKit.NormalHold, input);
            AKit.Step(w, waitTicks, input);
        }

        private static float Paid(DuelWorld w, string spellId) =>
            AKit.Events<ResourceEvent>(w).Where(e => e.Player == 0 && e.Change == ResourceChange.ManaSpent && e.SourceId == spellId).Select(e => e.Amount).Last();

        /// <summary>
        /// What the last cast of the spell finally cost: its payment, less the decrease returned when its field appeared
        /// (the refund ledger), plus any drawback charged after commitment under the record's id.
        /// </summary>
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

        private static List<FieldEntity> Fields(DuelWorld w) =>
            ((SpellRuntime)w.Spells).Entities.OfType<FieldEntity>().Where(f => f.Owned != null && f.Owned.Active).ToList();

        /// <summary>Aim player 0 at a floor point straight ahead at this distance.</summary>
        private static PlayerInput AimAhead(DuelWorld w, float distance) => SpellTestKit.Aim(w, 0, w.Player(0).Position + new Vec3(0f, 0f, distance));

        // ----- AUG-049 Wide Foundation ---------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.2f)]
        [InlineData(2, 0.35f)]
        [Verifies("AUG-049")]
        public void Wide_Foundation_Widens_Non_Damaging_Fields_For_Two_Mana(int rank, float metres)
        {
            AKit.AssertQuotes(WideFoundation.Id, "Create a non-damaging field.", "Radius increases 0.2 m.", "Radius increases 0.35 m.", "Paid mana cost increases 2.");
            DuelWorld w = Duel(WideFoundation.Id, rank, rush: new ShapedField { Shape = FieldShape.Lane, Length = 8f, Width = 2f, Placement = PlacementMode.AtCaster });
            Assert.Equal(3f + metres, FieldForm(w).Radius, 4);
            Assert.Equal(2f + 2f * metres, FieldForm(w, Rush).Width, 4); // a lane's radius is its half-width
            Cast(w, 1);
            Assert.Equal(22f, Paid(w, Curtain), 3);
            Assert.Equal(3f + metres, ((Confluence.Reactions.Grid.Footprint)Fields(w).Single().Footprint).Radius, 4);
        }

        [Fact]
        [Verifies("AUG-049")]
        public void Wide_Foundation_Leaves_Damaging_Fields_Alone()
        {
            DuelWorld w = Duel(WideFoundation.Id, 2, new ShapedField { EnterDamage = 20f });
            Assert.Equal(3f, FieldForm(w).Radius, 4);
            Cast(w, 1);
            Assert.Equal(20f, Paid(w, Curtain), 3);
        }

        // ----- AUG-050 Compact Hazard ----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-050")]
        public void Compact_Hazard_Raises_First_Contact_Damage_And_Shrinks_The_Field(int rank, float bonus)
        {
            AKit.AssertQuotes(CompactHazard.Id, "Create a damaging field.", "Initial direct damage increases 5%.", "Initial direct damage increases 8%.", "Radius decreases 10%.");
            DuelWorld w = Duel(CompactHazard.Id, rank, new ShapedField { EnterDamage = 20f });
            Assert.Equal(2.7f, FieldForm(w).Radius, 4);
            Assert.Equal(20f * (1f + bonus), FieldForm(w).EnterPayload.Direct, 4);

            // In play: the enemy's feet stand 2.85 m from the placement (inside 3 m, outside 2.7 m), then it walks in.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 8f + 2.85f), 180f);
            Cast(w, 1, AimAhead(w, 8f), waitTicks: 40);
            Assert.InRange(Fields(w).Single().Center.Z, 7.9f, 8.1f);
            Assert.DoesNotContain(AKit.Hits(w, 1), h => h.SourceId == Curtain);
            AKit.Step(w, 20, null, AKit.Walk(180f));
            Assert.Equal(20f * (1f + bonus), AKit.Hits(w, 1).Single(h => h.SourceId == Curtain).Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-050")]
        public void Compact_Hazard_Ignores_Non_Damaging_Fields()
        {
            DuelWorld w = Duel(CompactHazard.Id, 2);
            Assert.Equal(3f, FieldForm(w).Radius, 4);
            Assert.Null(FieldForm(w).EnterPayload);
        }

        // ----- AUG-051 Slow Dissipation --------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.5f)]
        [InlineData(2, 1f)]
        [Verifies("AUG-051")]
        public void Slow_Dissipation_Lengthens_Short_Fields_And_Cuts_First_Contact(int rank, float extra)
        {
            AKit.AssertQuotes(SlowDissipation.Id, "normal lifetime at most 6 s", "Lifetime increases 0.5 s.", "Lifetime increases 1 s.", "Initial direct damage decreases 5%; all lifetimes remain <=8 s.");
            DuelWorld w = Duel(SlowDissipation.Id, rank, new ShapedField { EnterDamage = 20f, LifetimeSec = 6f });
            Assert.Equal(6f + extra, FieldForm(w).LifetimeSec, 4);
            Assert.Equal(19f, FieldForm(w).EnterPayload.Direct, 4);
            Cast(w, 1, waitTicks: 40);
            OwnedEntity field = Fields(w).Single().Owned;
            Assert.Equal(AKit.Ticks(6f + extra), field.ExpireTick - field.CreatedTick);
        }

        [Fact]
        [Verifies("AUG-051")]
        public void Slow_Dissipation_Skips_Long_Fields_And_Never_Passes_8_Seconds()
        {
            DuelWorld w = Duel(SlowDissipation.Id, 2, new ShapedField { LifetimeSec = 7.5f });
            Assert.Equal(7.5f, FieldForm(w).LifetimeSec, 4);
            Assert.True(FieldForm(w, Rush).LifetimeSec <= GlobalRules.FieldsAndSummons.MaxLifetimeSec);
            Assert.Equal(4f, FieldForm(w, Rush).LifetimeSec, 4); // 3 s + 1 s
            Assert.Equal(8f, GlobalRules.FieldsAndSummons.MaxLifetimeSec);
        }

        // ----- AUG-052 Quick Dissipation -------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-052")]
        public void Quick_Dissipation_Trades_Lifetime_For_First_Contact_Damage(int rank, float bonus)
        {
            AKit.AssertQuotes(QuickDissipation.Id, "Create a field.", "Initial direct damage increases 6%.", "Initial direct damage increases 10%.", "Lifetime decreases 1 s, minimum 1 s.");
            DuelWorld w = Duel(QuickDissipation.Id, rank, new ShapedField { EnterDamage = 20f, LifetimeSec = 6f },
                new ShapedField { Radius = 1.5f, LifetimeSec = 1.5f, Placement = PlacementMode.AtCaster });
            Assert.Equal(5f, FieldForm(w).LifetimeSec, 4);
            Assert.Equal(20f * (1f + bonus), FieldForm(w).EnterPayload.Direct, 4);
            Assert.Equal(1f, FieldForm(w, Rush).LifetimeSec, 4); // 1.5 s - 1 s stops at the 1 s minimum
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 16f), 180f);
            Cast(w, 1, waitTicks: 40);
            Assert.Equal(20f * (1f + bonus), AKit.Hits(w, 1).Single().Resolution.Raw, 3);
            OwnedEntity field = Fields(w).Single().Owned;
            Assert.Equal(AKit.Ticks(5f), field.ExpireTick - field.CreatedTick);
        }

        [Fact]
        [Verifies("AUG-052")]
        public void Quick_Dissipation_Never_Lengthens_A_Field_Already_Under_1_Second()
        {
            DuelWorld w = Duel(QuickDissipation.Id, 1, rush: new ShapedField { Radius = 1.5f, LifetimeSec = 0.8f, Placement = PlacementMode.AtCaster });
            Assert.Equal(0.8f, FieldForm(w, Rush).LifetimeSec, 4);
        }

        // ----- AUG-053 Remote Survey / AUG-054 Home Circle -------------------------------------------------

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-053")]
        public void Remote_Survey_Discounts_Fields_Placed_Beyond_8_Metres(int rank, float discount)
        {
            AKit.AssertQuotes(RemoteSurvey.Id, "Place a field beyond 8 m.", "Cost decreases 2 mana.", "Cost decreases 4 mana.", "Fields within 3 m cost 2 extra mana.");
            DuelWorld w = Duel(RemoteSurvey.Id, rank);
            Cast(w, 1); // level aim: the field lands at the 16 m range
            Assert.InRange(Fields(w).Single().Center.Z, 15.5f, 16.1f);
            Assert.Equal(20f, Paid(w, Curtain), 3);                       // paid in full at commitment (spec L90)
            Assert.Equal(20f - discount, Net(w, Curtain, RemoteSurvey.Id), 3); // returned when the field appears beyond 8 m
        }

        /// <summary>Commits the Curtain with one aim, then turns to another during its 0.5 s windup.</summary>
        private static void CastThenReaim(DuelWorld w, PlayerInput commitAim, PlayerInput releaseAim)
        {
            PlayerInput first = AKit.SelectSlot(commitAim, 1);
            AKit.Step(w, 1, first);
            AKit.HoldAndRelease(w, AKit.NormalHold, first);
            Assert.Equal(CastPhase.Windup, w.Player(0).Caster.Machine.Phase);
            AKit.Step(w, 70, AKit.SelectSlot(releaseAim, 1));
        }

        [Fact]
        [Verifies("AUG-053")]
        public void Remote_Survey_Settles_Against_The_Field_Created_When_The_Aim_Changes_During_The_Windup()
        {
            // Aimed far at commitment, turned to 2.5 m before release: no decrease, and the near drawback is charged.
            DuelWorld near = Duel(RemoteSurvey.Id, 2);
            CastThenReaim(near, AKit.A, AimAhead(near, 2.5f));
            Assert.True(Fields(near).Single().Center.Z < 3f);
            Assert.Equal(20f, Paid(near, Curtain), 3);
            Assert.Equal(22f, Net(near, Curtain, RemoteSurvey.Id), 3);

            // Aimed near at commitment (charged 22), turned far before release: the 2 come back with the 4 decrease, inside
            // the cast's 25% refund cap (spec L124): 6 requested, 5.5 returned.
            DuelWorld far = Duel(RemoteSurvey.Id, 2);
            CastThenReaim(far, AimAhead(far, 2.5f), AKit.A);
            Assert.True(Fields(far).Single().Center.Z > 8f);
            Assert.Equal(22f, Paid(far, Curtain), 3);
            Assert.Equal(22f - 22f * GlobalRules.Casting.RefundCapFraction, Net(far, Curtain, RemoteSurvey.Id), 3);
        }

        [Fact]
        [Verifies("AUG-053")]
        public void Remote_Survey_Charges_Near_Fields_And_Ignores_The_Middle_Band()
        {
            DuelWorld near = Duel(RemoteSurvey.Id, 2);
            Cast(near, 1, AimAhead(near, 2.5f));
            Assert.True(Fields(near).Single().Center.Z < 3f);
            Assert.Equal(22f, Paid(near, Curtain), 3);

            DuelWorld mid = Duel(RemoteSurvey.Id, 2);
            Cast(mid, 1, AimAhead(mid, 6f));
            Assert.Equal(20f, Paid(mid, Curtain), 3);

            DuelWorld bolt = Duel(RemoteSurvey.Id, 2);
            Cast(bolt, 0);
            Assert.Equal(18f, Paid(bolt, "SPL-009"), 3); // not a field
        }

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-054")]
        public void Home_Circle_Discounts_Fields_Within_2_Metres(int rank, float discount)
        {
            AKit.AssertQuotes(HomeCircle.Id, "Place a field within 2 m of self.", "Cost decreases 2 mana.", "Cost decreases 4 mana.", "Fields beyond 10 m cost 2 extra mana.");
            DuelWorld w = Duel(HomeCircle.Id, rank);
            Cast(w, 2); // Rush: centered on the caster
            Assert.Equal(28f - discount, Net(w, Rush, HomeCircle.Id), 3);

            DuelWorld aimed = Duel(HomeCircle.Id, rank);
            Cast(aimed, 1, AimAhead(aimed, 1.8f));
            Assert.Equal(20f - discount, Net(aimed, Curtain, HomeCircle.Id), 3);
        }

        [Fact]
        [Verifies("AUG-054")]
        public void Home_Circle_Charges_Fields_Beyond_10_Metres()
        {
            DuelWorld far = Duel(HomeCircle.Id, 1);
            Cast(far, 1);
            Assert.Equal(22f, Paid(far, Curtain), 3);

            DuelWorld mid = Duel(HomeCircle.Id, 1);
            Cast(mid, 1, AimAhead(mid, 6f));
            Assert.Equal(20f, Paid(mid, Curtain), 3);
            Assert.Equal(20f, Net(mid, Curtain, HomeCircle.Id), 3);

            // Aimed at 1.8 m at commitment, turned to the 16 m range before release: the far drawback, no decrease.
            DuelWorld turned = Duel(HomeCircle.Id, 2);
            CastThenReaim(turned, AimAhead(turned, 1.8f), AKit.A);
            Assert.True(Fields(turned).Single().Center.Z > 10f);
            Assert.Equal(22f, Net(turned, Curtain, HomeCircle.Id), 3);
        }

        // ----- AUG-055 Edge Casting ------------------------------------------------------------------------

        /// <summary>Lays a Rush field (1.5 m, on the caster), moves the caster to a distance from its center, casts the Curtain.</summary>
        private static DuelWorld EdgeCast(int rank, float fromCenter, bool airborne = false)
        {
            DuelWorld w = Duel(rank > 0 ? EdgeCasting.Id : null, rank, rush: new ShapedField { Radius = 1.5f, LifetimeSec = 6f, Placement = PlacementMode.AtCaster });
            Cast(w, 2);
            Assert.Single(Fields(w));
            TestWorlds.Place(w, 0, new Vec3(fromCenter, 0f, 0f), 0f);
            AKit.Step(w, 2);
            PlayerInput input = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(w, 1, input);
            if (airborne)
            {
                AKit.Step(w, 1, input.With(InputButtons.Jump));
                AKit.Step(w, 3, input);
                Assert.False(w.Player(0).Motor.Grounded);
            }

            AKit.HoldAndRelease(w, AKit.NormalHold, input);
            AKit.Step(w, 60, input);
            return w;
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [Verifies("AUG-055")]
        public void Edge_Casting_Shortens_A_Spell_Begun_At_A_Field_Edge(int rank, int ticksSaved)
        {
            AKit.AssertQuotes(EdgeCasting.Id, "Begin a spell while within 0.5 m of a field edge.", "Preparation decreases 5%.", "Preparation decreases 8%.", "Must be grounded; field center grants no bonus.");
            Assert.Equal(AKit.Ticks(0.5f), Windup(EdgeCast(0, 1.8f), Curtain));
            Assert.Equal(AKit.Ticks(0.5f) - ticksSaved, Windup(EdgeCast(rank, 1.8f), Curtain)); // 30 ticks less 5% (8%), rounded down
            Assert.Equal(AKit.Ticks(0.5f) - ticksSaved, Windup(EdgeCast(rank, 1.2f), Curtain)); // just inside the edge
        }

        [Fact]
        [Verifies("AUG-055")]
        public void Edge_Casting_Gives_Nothing_At_The_Center_Far_Away_Or_Airborne()
        {
            Assert.Equal(AKit.Ticks(0.5f), Windup(EdgeCast(2, 0.3f), Curtain));
            Assert.Equal(AKit.Ticks(0.5f), Windup(EdgeCast(2, 2.3f), Curtain));
            Assert.Equal(AKit.Ticks(0.5f), Windup(EdgeCast(2, 1.8f, airborne: true), Curtain));
        }

        // ----- AUG-060 Safe Corridor -----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-060")]
        public void Safe_Corridor_Discounts_A_Field_Clear_Of_Other_Fields(int rank, float cut)
        {
            AKit.AssertQuotes(SafeCorridor.Id, "created without overlapping another field", "Cost decreases 5% mana.", "Cost decreases 8% mana.", "Overlapping fields cost 2 additional mana.");
            DuelWorld w = Duel(SafeCorridor.Id, rank);
            Cast(w, 1);
            Assert.Equal(20f * (1f - cut), Net(w, Curtain, SafeCorridor.Id), 3);
            Cast(w, 2); // the Rush field on the caster, 16 m from the curtain
            Assert.Equal(28f * (1f - cut), Net(w, Rush, SafeCorridor.Id), 3);
        }

        [Fact]
        [Verifies("AUG-060")]
        public void Safe_Corridor_A_Field_Appearing_Under_The_Placement_During_The_Windup_Counts()
        {
            DuelWorld w = Duel(SafeCorridor.Id, 2);
            PlayerInput input = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, AKit.NormalHold, input); // clear at commitment: no drawback charged
            Assert.Equal(CastPhase.Windup, w.Player(0).Caster.Machine.Phase);
            w.CreateOwnedEntity(new OwnedEntitySpec { Kind = OwnedEntityKind.Field, Owner = 1, DefinitionId = "SPL-TEST-ENEMY", LifetimeTicks = AKit.Ticks(6f), Position = new Vec3(0f, 0f, 16f), Radius = 1f });
            AKit.Step(w, 70, input);
            Assert.Equal(20f, Paid(w, Curtain), 3);
            Assert.Equal(22f, Net(w, Curtain, SafeCorridor.Id), 3); // created overlapping: no decrease, 2 more

            // The reverse: the enemy field under the placement dissolves during the windup.
            DuelWorld gone = Duel(SafeCorridor.Id, 2);
            OwnedEntity enemy = gone.CreateOwnedEntity(new OwnedEntitySpec { Kind = OwnedEntityKind.Field, Owner = 1, DefinitionId = "SPL-TEST-ENEMY", LifetimeTicks = AKit.Ticks(0.6f), Position = new Vec3(0f, 0f, 16f), Radius = 1f });
            Cast(gone, 1);
            Assert.False(enemy.Active);
            Assert.Equal(22f, Paid(gone, Curtain), 3);
            Assert.Equal(20f * 0.92f, Net(gone, Curtain, SafeCorridor.Id), 3); // the 2 come back with the 8% decrease
        }

        [Fact]
        [Verifies("AUG-060")]
        public void Safe_Corridor_Charges_Overlap_With_Any_Field_Including_The_Enemys()
        {
            DuelWorld w = Duel(SafeCorridor.Id, 1);
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, 14f), 0f);
            AKit.Step(w, 1);
            Cast(w, 1, AimAhead(w, 3f)); // curtain (3 m radius) centered 3 m ahead
            Cast(w, 2); // rush field (1.5 m) on the caster overlaps it
            Assert.Equal(30f, Paid(w, Rush), 3);

            // An enemy field under the placement also counts.
            DuelWorld enemy = Duel(SafeCorridor.Id, 1);
            enemy.CreateOwnedEntity(new OwnedEntitySpec { Kind = OwnedEntityKind.Field, Owner = 1, DefinitionId = "SPL-TEST-ENEMY", LifetimeTicks = AKit.Ticks(6f), Position = new Vec3(0f, 0f, 16f), Radius = 1f });
            Cast(enemy, 1);
            Assert.Equal(22f, Paid(enemy, Curtain), 3);
        }

        // ----- AUG-065 Open Sky / AUG-066 Sheltered Work ---------------------------------------------------

        private static ArenaLayout Canopy()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Solids.Add(TestWorlds.Box(60, new Vec3(0f, 3f, 0f), new Vec3(4f, 0.3f, 4f), SolidKind.Platform));
            return layout;
        }

        private static ShapedField OnCaster => new ShapedField { Radius = 1.5f, LifetimeSec = 3f, Placement = PlacementMode.AtCaster };

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 3)]
        [Verifies("AUG-065")]
        public void Open_Sky_Shortens_Fields_Created_In_The_Open(int rank, int ticksSaved)
        {
            AKit.AssertQuotes(OpenSky.Id, "Create a field under no overhead cover.", "Preparation decreases 6%.", "Preparation decreases 10%.", "Creation under a ceiling costs 2 extra mana.");
            DuelWorld w = Duel(OpenSky.Id, rank, OnCaster);
            Cast(w, 1);
            Assert.Equal(AKit.Ticks(0.5f) - ticksSaved, Windup(w, Curtain)); // 30 ticks less 6% (10%), rounded down
            Assert.Equal(20f, Paid(w, Curtain), 3);
        }

        [Fact]
        [Verifies("AUG-065")]
        public void Open_Sky_Charges_A_Field_Under_A_Ceiling()
        {
            DuelWorld w = Duel(OpenSky.Id, 2, OnCaster, layout: Canopy());
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            AKit.Step(w, 1);
            Cast(w, 1);
            Assert.Equal(AKit.Ticks(0.5f), Windup(w, Curtain));
            Assert.Equal(22f, Paid(w, Curtain), 3);
        }

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-066")]
        public void Sheltered_Work_Discounts_Fields_Under_Overhead_Cover(int rank, float cut)
        {
            AKit.AssertQuotes(ShelteredWork.Id, "Create a field under overhead cover.", "Cost decreases 6% mana.", "Cost decreases 10% mana.", "Field creation outdoors has 0.05 s longer preparation.");
            DuelWorld w = Duel(ShelteredWork.Id, rank, OnCaster, layout: Canopy());
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            AKit.Step(w, 1);
            Cast(w, 1);
            Assert.Equal(20f * (1f - cut), Net(w, Curtain, ShelteredWork.Id), 3);
            Assert.Equal(AKit.Ticks(0.5f), Windup(w, Curtain));
        }

        /// <summary>The flat layout with a canopy over a spot 6 m ahead of player 0 (whose feet stay in the open).</summary>
        private static ArenaLayout CanopyAhead()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Solids.Add(TestWorlds.Box(60, new Vec3(0f, 3f, 6f), new Vec3(4f, 0.3f, 4f), SolidKind.Platform));
            return layout;
        }

        [Fact]
        [Verifies("AUG-066")]
        [Verifies("AUG-065")]
        public void Field_Placement_Is_Bound_At_Commitment_When_Cover_Sets_The_Windup()
        {
            // Sheltered Work: aimed under the canopy at commitment (normal windup), turned to the open 16 m range before
            // release: the field is still created under the canopy, so the longer outdoor windup cannot be dodged.
            DuelWorld sheltered = Duel(ShelteredWork.Id, 2, layout: CanopyAhead());
            TestWorlds.Place(sheltered, 0, new Vec3(0f, 0f, -6f), 0f);
            AKit.Step(sheltered, 1);
            CastThenReaim(sheltered, AimAhead(sheltered, 12f), AKit.A);
            FieldEntity field = Fields(sheltered).Single();
            Assert.InRange(field.Center.Z, 5.5f, 6.5f);
            Assert.Equal(AKit.Ticks(0.5f), Windup(sheltered, Curtain));
            Assert.Equal(18f, Net(sheltered, Curtain, ShelteredWork.Id), 3);

            // Open Sky: aimed at the open range at commitment (shorter windup), turned under the canopy: created in the open.
            DuelWorld open = Duel(OpenSky.Id, 2, layout: CanopyAhead());
            TestWorlds.Place(open, 0, new Vec3(0f, 0f, -6f), 0f);
            AKit.Step(open, 1);
            CastThenReaim(open, AKit.A, AimAhead(open, 12f));
            Assert.True(Fields(open).Single().Center.Z > 9f);
            Assert.Equal(AKit.Ticks(0.5f) - 3, Windup(open, Curtain));
            Assert.Equal(20f, Net(open, Curtain, OpenSky.Id), 3);

            // Without the records, the same turn moves the field with the aim.
            DuelWorld control = Duel((string)null, 0, layout: CanopyAhead());
            TestWorlds.Place(control, 0, new Vec3(0f, 0f, -6f), 0f);
            AKit.Step(control, 1);
            CastThenReaim(control, AimAhead(control, 12f), AKit.A);
            Assert.True(Fields(control).Single().Center.Z > 9f);
        }

        [Fact]
        [Verifies("AUG-066")]
        public void Sheltered_Work_Lengthens_Fields_Created_Outdoors()
        {
            DuelWorld w = Duel(ShelteredWork.Id, 2, OnCaster);
            Cast(w, 1);
            Assert.Equal(AKit.Ticks(0.5f) + AKit.Ticks(0.05f), Windup(w, Curtain));
            Assert.Equal(20f, Paid(w, Curtain), 3);
            Cast(w, 0);
            Assert.Equal(AKit.Ticks(0.35f), Windup(w, "SPL-009")); // not a field
        }

        // ----- AUG-068 One Landmark ------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-068")]
        public void One_Landmark_Discounts_Spells_Cast_Inside_The_Only_Field(int rank, float cut)
        {
            AKit.AssertQuotes(OneLandmark.Id, "exactly one living persistent field", "Spells cast inside it cost 6% less mana.", "Spells cast inside it cost 10% less mana.", "Creating a second field removes benefit; field allegiance remains neutral.");
            DuelWorld w = Duel(OneLandmark.Id, rank, rush: new ShapedField { Radius = 2f, LifetimeSec = 8f, Placement = PlacementMode.AtCaster });
            Cast(w, 2);
            Assert.Equal(28f, Paid(w, Rush), 3); // no field yet when it was paid
            Cast(w, 0);
            Assert.Equal(18f * (1f - cut), Paid(w, "SPL-009"), 3);
        }

        [Fact]
        [Verifies("AUG-068")]
        public void One_Landmark_Ends_With_A_Second_Field_Or_Outside_It()
        {
            DuelWorld outside = Duel(OneLandmark.Id, 2, rush: new ShapedField { Radius = 2f, LifetimeSec = 8f, Placement = PlacementMode.AtCaster });
            Cast(outside, 2);
            TestWorlds.Place(outside, 0, new Vec3(3f, 0f, 0f), 0f);
            AKit.Step(outside, 1);
            Cast(outside, 0);
            Assert.Equal(18f, Paid(outside, "SPL-009"), 3);

            DuelWorld two = Duel(OneLandmark.Id, 2, rush: new ShapedField { Radius = 2f, LifetimeSec = 8f, Placement = PlacementMode.AtCaster });
            Cast(two, 2);
            Cast(two, 1); // the second field (curtain at 16 m)
            Assert.Equal(18f, Paid(two, Curtain), 3); // paid while inside the only field: 20 less 10%
            Assert.Equal(2, Fields(two).Count);
            Cast(two, 0);
            Assert.Equal(18f, Paid(two, "SPL-009"), 3);
        }
    }
}
