using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// End-to-end WIZ-010 duels in a real DuelWorld with scripted inputs on both sides: Halen Tide casts his whole kit
    /// through the pressure-metering grammar (normal and mastered releases), his signature and passive run through the
    /// effect framework, his water meets Bram Cinder's heat through the duel's reaction world, and the duel ends.
    /// </summary>
    public class HalenEndToEndTests
    {
        private const int BramRelease = 18; // a plain crest-release cast for WIZ-002 (0.3 s hold)

        private static float Cost(HalenDuel d, string spellId) => d.P0.Caster.Loadout.FormFor(spellId).Mana;

        /// <summary>Waits until Halen is idle, the spell is off cooldown and its mana is there, then casts it.</summary>
        private static CastDuelEvent CastWhenReady(HalenDuel d, string spellId, System.Action aim, bool mastery = false)
        {
            int slot = d.P0.Setup.EquippedSpellIds.ToList().IndexOf(spellId);
            Assert.True(slot >= 0, spellId + " is not equipped");
            d.RunUntil(() => d.Machine.Phase == CastPhase.Idle && d.P0.Caster.CooldownRemainingTicks(spellId) == 0 &&
                             d.P0.Resources.Mana >= Cost(d, spellId) + 1f, 60 * 12);
            aim();
            CastDuelEvent commit = d.Cast(slot, mastery: mastery);
            Assert.True(d.World.IsOver || (commit != null && commit.Cast.SpellId == spellId), spellId + " did not commit (" + d.P1.Resources.Health + " hp, phase " + d.Machine.Phase + ")");
            return commit;
        }

        /// <summary>Bram turns toward a point and casts Fireball (slot 0) through his crest-release grammar.</summary>
        private static void BramFireball(HalenDuel d, Vec3 at)
        {
            d.RunUntil(() => d.P1.Caster.Machine.Phase == CastPhase.Idle && d.P1.Caster.CooldownRemainingTicks("SPL-009") == 0, 60 * 6);
            Vec3 dir = at - d.P1.Eye;
            d.Other = PlayerInput.Idle(CMath.YawOf(dir)).Facing(CMath.YawOf(dir), CMath.PitchOf(dir));
            for (int k = 0; k < BramRelease; k++) d.Step(InputButtons.None, InputButtons.Primary);
            d.Step();
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Halen_Duels_Bram_With_His_Default_Kit_Signature_Passive_And_Water_Heat_Combos()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(), HalenTestKit.Bram());
            Assert.Equal(new[] { "SPL-073", "SPL-074", "SPL-075", "SPL-076", "SPL-077", "SPL-078" }, d.P0.Setup.EquippedSpellIds.ToArray());
            var committed = new List<string>();

            // 1. Water Pool under Bram: he is Wet; his own Fireball into the pool makes steam (heat consumes water).
            committed.Add(CastWhenReady(d, WaterPool.Id, () => d.AimAt(d.P1.Position)).Cast.SpellId);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 40);
            d.Run(20);
            Assert.True(d.P1.Statuses.IsWet);
            BramFireball(d, d.P1.Position + new Vec3(0f, 0f, -2f));
            d.RunUntil(() => d.Reactions.Volumes.Any(v => v.Kind == VolumeKind.Steam), 90);
            Assert.Contains(d.Reactions.Volumes, v => v.Kind == VolumeKind.Steam);

            // 2. Bubble Buckler facing Bram: his next Fireball is a heat reaction on the bubble.
            d.RunUntil(() => d.P1.Caster.CooldownRemainingTicks("SPL-009") == 0, 60 * 6);
            committed.Add(CastWhenReady(d, BubbleBuckler.Id, () => d.AimAtOther()).Cast.SpellId);
            d.RunUntil(() => d.P0.Shields.Total > 0f, 30);
            BramFireball(d, d.P0.Position + new Vec3(0f, 1.1f, 0f));
            d.RunUntil(() => d.Tide(TideEventKind.BubbleHeatReaction).Any() || d.Tide(TideEventKind.BubbleBroken).Any(), 90);
            Assert.True(d.Tide(TideEventKind.BubbleHeatReaction).Any() || d.Tide(TideEventKind.BubbleBroken).Any());

            // 3. Pressure Jet (mastered: 15% less mana) hits and pushes.
            CastDuelEvent jet = CastWhenReady(d, PressureJet.Id, () => d.AimAtOther(), mastery: true);
            committed.Add(jet.Cast.SpellId);
            Assert.True(jet.Cast.Mastery.IsMastered);
            Assert.Equal(16f * 0.85f, jet.Cast.Mana, 2);
            d.RunUntil(() => d.DamageTo(1, PressureJet.Id).Any(), 60);
            Assert.Equal(32f, d.DamageTo(1, PressureJet.Id).Single().Dealt, 2);

            // 4. Riptide Lane toward Bram, then Cascade Vault into close range.
            committed.Add(CastWhenReady(d, RiptideLane.Id, () => d.AimAt(d.P0.Position + new Vec3(0f, 0f, 2f))).Cast.SpellId);
            d.RunUntil(() => d.Runtime.Entities.OfType<StreamCurrentEntity>().Any(), 60);
            d.Halen = d.Halen.Facing(0f, 0f).Moving(0f, 1f); // walk down the stream with its arrow
            d.RunUntil(() => Vec3.DistanceXZ(d.P0.Position, d.P1.Position) < 8f, 600);
            Assert.Contains(d.Tide(TideEventKind.StreamBonus), e => e.TargetPlayer == 0 && e.Amount == 1f);
            d.Halen = d.Halen.Moving(0f, 0f);
            committed.Add(CastWhenReady(d, CascadeVault.Id, () => d.AimAt(new Vec3(d.P1.Position.X, 0f, d.P1.Position.Z - 2.2f))).Cast.SpellId);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.MovementEnded).Any(), 120);
            Assert.True(Vec3.DistanceXZ(d.P0.Position, d.P1.Position) < 3.5f);

            // 5. Undertow Mantle at close range, then Water Hammer.
            d.RunUntil(() => d.Machine.Phase == CastPhase.Idle, 60);
            d.Halen = d.Halen.Facing(CMath.YawOf(d.P1.Position - d.P0.Position), 0f);
            CastDuelEvent eddy = d.Signature();
            Assert.NotNull(eddy);
            d.RunUntil(() => d.DamageTo(1, HalenTideKit.Id).Any(), 40);
            Assert.Equal(12f, d.DamageTo(1, HalenTideKit.Id).Single().Dealt, 2);
            committed.Add(CastWhenReady(d, WaterHammer.Id, () => d.AimAtOther()).Cast.SpellId);
            d.RunUntil(() => d.DamageTo(1, WaterHammer.Id).Any() || d.Tide(TideEventKind.MissRecovery).Any(), 60);
            Assert.Single(d.DamageTo(1, WaterHammer.Id));

            Assert.Equal(new[] { "SPL-073", "SPL-074", "SPL-075", "SPL-076", "SPL-077", "SPL-078" }, committed.OrderBy(s => s).ToArray());

            // 6. Finish with jets until the duel ends: Halen wins.
            for (int i = 0; i < 12 && !d.World.IsOver; i++)
            {
                CastWhenReady(d, PressureJet.Id, () => d.AimAtOther());
                d.Run(30);
            }

            d.RunUntil(() => d.World.IsOver, 120);
            Assert.True(d.World.IsOver);
            Assert.Equal(0, d.World.Result.Winner);
        }

        [Fact]
        [Verifies("WIZ-010")]
        public void Halen_Seals_Bram_s_Heat_And_Imprisons_Him_With_The_Other_Two_Spells()
        {
            var six = new[] { DeepglassPrison.Id, SealTheSpring.Id, PressureJet.Id, WaterPool.Id, BubbleBuckler.Id, RiptideLane.Id };
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(six), HalenTestKit.Bram());

            // A visible hot ground field of Bram's (a Fireball's Heat 2 is consumed at once, so the hazard is placed in the
            // reaction world directly); Seal the Spring removes 2 of its units.
            Vec3 scorch = new Vec3(0f, 0f, 0f);
            d.Deposit(Material.Heat, 2, Footprint.Circle(Vec2.Zero, 1.2f), 8f, 1);
            d.Step();
            int heat = d.Total(Material.Heat, Footprint.Circle(Vec2.Zero, 2.5f));
            Assert.True(heat >= 2);
            CastWhenReady(d, SealTheSpring.Id, () => d.AimAt(scorch));
            d.RunUntil(() => d.Tide(TideEventKind.SpringNeutralized).Any(), 60);
            d.Step();
            Assert.Equal(2f, d.Tide(TideEventKind.SpringNeutralized).Single().Amount);
            Assert.True(d.Total(Material.Heat, Footprint.Circle(Vec2.Zero, 2.5f)) <= heat - 2);

            // Deepglass Prison roots him (hard control, then immunity) and the passive pays 10 stamina for the 32 mana.
            d.P0.Resources.TrySpendStamina(30f, d.World.Tick);
            float stamina = d.P0.Resources.Stamina;
            CastWhenReady(d, DeepglassPrison.Id, () => d.AimAtOther());
            Assert.True(d.P0.Resources.Stamina >= stamina + 10f - 1e-3f);
            d.RunUntil(() => d.DamageTo(1, DeepglassPrison.Id).Any(), 90);
            Assert.True(d.P1.Statuses.IsRooted);
            d.RunUntil(() => !d.P1.Statuses.IsRooted, 60);
            d.Step();
            Assert.True(d.P1.Statuses.IsControlImmune(d.World.Tick));
            Assert.NotNull(d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id));
        }
    }
}
