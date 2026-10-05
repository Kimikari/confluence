using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Sim.Casting;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// SPL-080 Seal the Spring through WIZ-010's grammar: "A 2 m-radius splash removes up to 2 reaction units from one visible
    /// hot/corrosive ground field; requires a surface impact." "Water 2 is fully consumed in neutralization; no surviving
    /// pool." 26 mana, 14 s, 0.6 s, 18 m.
    /// </summary>
    public class SealTheSpringTests
    {
        internal static HalenDuel Ready(params ActiveAugment[] augments)
        {
            HalenDuel d = HalenTestKit.With(SealTheSpring.Id, augments);
            return d;
        }

        internal static void Splash(HalenDuel d, Vec3 at)
        {
            d.AimAt(at);
            d.Cast(0);
            d.RunUntil(() => d.Tide(TideEventKind.SpringNeutralized).Any() || d.Tide(TideEventKind.SpringNoSurface).Any(), 60);
            d.Step();
        }

        [Fact]
        [Verifies("SPL-080")]
        public void It_Removes_Up_To_2_Units_From_The_Hot_Field_Under_The_Splash()
        {
            HalenDuel d = Ready();
            Footprint hot = Footprint.Circle(Vec2.Zero, 1f);
            d.Deposit(Material.Heat, 2, hot);
            d.Step();
            int before = d.Total(Material.Heat, hot);
            Assert.True(before >= 8);
            d.AimAt(Vec3.Zero);
            CastDuelEvent commit = d.Cast(0);
            Assert.Equal(26f, commit.Cast.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(14.0), d.P0.Caster.CooldownRemainingTicks(SealTheSpring.Id));
            d.RunUntil(() => d.Tide(TideEventKind.SpringNeutralized).Any(), 60);
            TideEvent splash = d.Tide(TideEventKind.SpringNeutralized).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.6), splash.Tick);
            Assert.Equal(2f, splash.Amount);
            Assert.Equal(before - 2, d.Total(Material.Heat, hot));
            // The nearest cell went first: one cell of the field lost both of its units, the rest kept theirs.
            var cells = new System.Collections.Generic.List<int>();
            d.Reactions.Grid.Rasterize(hot, cells);
            Assert.Equal(1, cells.Count(c => d.Reactions.UnitsAtCell(c).Heat == 0));
            int emptied = cells.Single(c => d.Reactions.UnitsAtCell(c).Heat == 0);
            Assert.True(Vec2.Distance(d.Reactions.Grid.CellCenter(emptied), splash.Position.XZ()) < 0.36f);
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(Vec2.Zero, 3f))); // "no surviving pool"
            Assert.Empty(d.Reactions.Volumes);                                         // and no steam from its water
        }

        [Fact]
        [Verifies("SPL-080")]
        public void It_Neutralizes_A_Corrosive_Field_Too()
        {
            HalenDuel d = Ready();
            Footprint venom = Footprint.Circle(new Vec2(0f, 1f), 1f);
            d.Deposit(Material.Venom, 1, venom);
            d.Step();
            int before = d.Total(Material.Venom, venom);
            Splash(d, new Vec3(0f, 0f, 1f));
            Assert.Equal(before - 2, d.Total(Material.Venom, venom));
        }

        [Fact]
        [Verifies("SPL-080")]
        public void Only_One_Connected_Field_Is_Touched()
        {
            HalenDuel d = Ready();
            Footprint near = Footprint.Point(new Vec2(0.75f, 0.25f));
            Footprint other = Footprint.Circle(new Vec2(-1.25f, 0.25f), 0.3f);
            d.Deposit(Material.Heat, 1, near);
            d.Deposit(Material.Heat, 2, other);
            d.Step();
            int otherBefore = d.Total(Material.Heat, other);
            Splash(d, new Vec3(0.6f, 0f, 0.25f));
            Assert.Equal(0, d.Total(Material.Heat, near));          // the nearest field gave its one unit
            Assert.Equal(otherBefore, d.Total(Material.Heat, other)); // the separate field inside the splash kept its units
            Assert.Equal(1f, d.Tide(TideEventKind.SpringNeutralized).Single().Amount);
        }

        [Fact]
        [Verifies("SPL-080")]
        public void It_Requires_A_Surface_Impact()
        {
            HalenDuel d = Ready();
            Footprint hot = Footprint.Circle(new Vec2(0f, 3f), 1f);
            d.Deposit(Material.Heat, 2, hot);
            d.Step();
            int before = d.Total(Material.Heat, hot);
            d.Halen = TestWorlds.A.Facing(0f, 35f); // the open sky: no surface within 18 m
            CastDuelEvent commit = d.Cast(0);
            d.Run(60);
            Assert.NotNull(commit);
            Assert.Single(d.Tide(TideEventKind.SpringNoSurface));
            Assert.Empty(d.Tide(TideEventKind.SpringNeutralized));
            Assert.Equal(before, d.Total(Material.Heat, hot));
            Assert.Equal(74f, d.P0.Resources.Mana, 3); // the committed cast stays spent (spec L90)
        }

        [Fact]
        [Verifies("SPL-080")]
        public void A_Wall_Impact_Splashes_The_Field_At_Its_Base()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(new[] { SealTheSpring.Id }),
                layout: HalenTestKit.Layout(SpellTestKit.Wall(50, new Vec3(0f, 0f, 0.25f), new Vec3(4f, 3f, 0.5f))));
            Footprint hot = Footprint.Circle(new Vec2(0f, -0.75f), 0.6f);
            d.Deposit(Material.Heat, 1, hot);
            d.Step();
            int before = d.Total(Material.Heat, hot);
            Splash(d, new Vec3(0f, 1.2f, 0f));
            Assert.Equal(before - 2, d.Total(Material.Heat, hot));
        }

        [Fact]
        [Verifies("SPL-080")]
        public void A_Field_Hidden_Behind_The_Struck_Wall_Is_Untouched()
        {
            // "one visible hot/corrosive ground field": a splash on the near face of a 3 m wall does not reach the field on
            // its far side, although that field lies inside the 2 m splash radius.
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(new[] { SealTheSpring.Id }),
                layout: HalenTestKit.Layout(SpellTestKit.Wall(50, new Vec3(0f, 0f, 0.25f), new Vec3(4f, 3f, 0.5f))));
            Footprint hidden = Footprint.Circle(new Vec2(0f, 1.25f), 0.5f);
            d.Deposit(Material.Heat, 2, hidden);
            d.Step();
            int before = d.Total(Material.Heat, hidden);
            Assert.True(before > 0);
            Splash(d, new Vec3(0f, 0.3f, 0f));
            Assert.Equal(before, d.Total(Material.Heat, hidden));
            Assert.Equal(0f, d.Tide(TideEventKind.SpringNeutralized).Single().Amount);

            // The same field with a visible one on the near side: only the visible one loses units.
            HalenDuel both = HalenTestKit.Duel(HalenTestKit.Halen(new[] { SealTheSpring.Id }),
                layout: HalenTestKit.Layout(SpellTestKit.Wall(50, new Vec3(0f, 0f, 0.25f), new Vec3(4f, 3f, 0.5f))));
            Footprint near = Footprint.Circle(new Vec2(0f, -1.25f), 0.5f);
            both.Deposit(Material.Heat, 2, hidden);
            both.Deposit(Material.Heat, 2, near);
            both.Step();
            int hiddenBefore = both.Total(Material.Heat, hidden);
            int nearBefore = both.Total(Material.Heat, near);
            Splash(both, new Vec3(0f, 0.3f, 0f));
            Assert.Equal(hiddenBefore, both.Total(Material.Heat, hidden));
            Assert.Equal(nearBefore - 2, both.Total(Material.Heat, near));
        }

        [Fact]
        [Verifies("SPL-080")]
        public void A_Field_The_Caster_Cannot_See_Through_Steam_Is_Not_Chosen()
        {
            // The splash lands on the ground at the field, but a 3 m deep steam bank (spec L167: 2 m of obscuring volume
            // blocks sight) hides the field from the caster's eye, so it is not a "visible" field.
            HalenDuel d = Ready();
            Footprint venom = Footprint.Circle(new Vec2(0f, 2.5f), 0.6f);
            d.Deposit(Material.Venom, 2, venom);
            d.Materials.CreateVolume(new Confluence.Reactions.Operations.VolumeSpec
            {
                Kind = Confluence.Reactions.Volumes.VolumeKind.Steam,
                Footprint = Footprint.Rectangle(new Vec2(0f, -1.5f), new Vec2(2f, 1.5f), 0f),
                BaseY = 0f,
                Height = 2f,
                LifetimeTicks = Ticks.FromSeconds(8f),
                Owner = 1,
            });
            d.Step();
            int before = d.Total(Material.Venom, venom);
            Assert.False(d.World.HasClearSight(d.P0.Eye, new Vec3(0f, 0.1f, 2.5f)));
            Splash(d, new Vec3(0f, 0f, 2.5f));
            Assert.Equal(before, d.Total(Material.Venom, venom));

            // Control: without the steam the same splash neutralizes 2 units.
            HalenDuel clear = Ready();
            clear.Deposit(Material.Venom, 2, venom);
            clear.Step();
            int clearBefore = clear.Total(Material.Venom, venom);
            Splash(clear, new Vec3(0f, 0f, 2.5f));
            Assert.Equal(clearBefore - 2, clear.Total(Material.Venom, venom));
        }
    }
}
