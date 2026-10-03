using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Content;
using Confluence.Sim.Tests.Effects;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Integration
{
    /// <summary>
    /// The merged stages working together in one duel: WIZ-002's real grammar casts SPL-009 through the spell runtime,
    /// its deposits and impacts reach the duel's own reaction world (the default <c>ReactionMaterials</c>), and the
    /// reaction results come back through acquisition, the damage pipeline, and the effect framework.
    /// </summary>
    public class SpellReactionIntegrationTests
    {
        // A Water Pool-like natural patch (SPL-076 "3 m-radius shallow pool ... Water 2") around the arena center.
        private static ArenaLayout PoolLayout()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(0f, 0f), 3f, 3f, water: 2));
            return layout;
        }

        private static Vec3 Chest(DuelWorld w, int player) => w.Player(player).Position + new Vec3(0f, w.Player(player).Motor.Height * 0.6f, 0f);

        private static SpellEvent CastFireballAt(DuelWorld w, Vec3 groundPoint, PlayerInput? other = null)
        {
            PlayerInput aim = SpellTestKit.Aim(w, 0, groundPoint);
            ContentTestKit.HoldAndRelease(w, 15, aim, other); // a normal WIZ-002 release (no crest)
            SpellTestKit.RunUntil(w, () => SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileImpact).Any(), 120, aim, other);
            return SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileImpact).Single();
        }

        [Fact]
        [Verifies("SPL-009")]
        public void Fireball_Into_A_Water_Patch_Forms_Steam_That_Blocks_Acquisition_Through_Two_Meters_Of_Path()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy(PoolLayout());
            ReactionWorld reactions = WorldDuels.Reactions(w);
            Assert.True(w.CanAcquire(0, 1));
            Assert.True(w.CanAcquire(1, 0));

            SpellEvent impact = CastFireballAt(w, Vec3.Zero);
            CastDuelEvent commit = ContentTestKit.LastCommit(w);
            Assert.Equal(TestWorlds.FloorId, impact.SolidId);

            // Spec L144, L163: the heavy impact's Heat 2 (and the 1 m ring of Heat 1) consumes water into one steam cloud
            // owned by the caster's committed source, rising 2 m from the pool surface; steam deals no damage by itself.
            ObscurationVolume steam = Assert.Single(reactions.Volumes);
            Assert.Equal(VolumeKind.Steam, steam.Kind);
            Assert.Equal(0, steam.Owner);
            Assert.Equal(commit.Id, steam.SourceEventId);
            Assert.Equal(0f, steam.BaseY, 3);
            Assert.Equal(GlobalRules.World.SteamRise, steam.Height, 3);
            Assert.True(steam.ColumnCount > 1, "a bounded steam patch, not a single cell");
            Assert.Equal(0, reactions.UnitsAt(impact.Position).Water);  // Water 2 - Heat 2
            Assert.Equal(0, reactions.UnitsAt(impact.Position).Heat);
            Assert.Equal(1, reactions.UnitsAt(impact.Position + new Vec3(0f, 0f, 0.75f)).Water); // ring: Water 2 - Heat 1
            Assert.Equal(2, reactions.UnitsAt(new Vec3(0f, 0f, 2.5f)).Water); // outside the ring the pool is untouched
            Assert.Empty(SpellTestKit.Events<DamageEvent>(w));

            // Spec L167: two meters of cumulative path through the cloud block target acquisition both ways.
            Vec3 eye0 = w.Player(0).Eye;
            Assert.True(w.Materials.ObscuringPathLength(eye0, Chest(w, 1)) >= GlobalRules.World.SteamSightBlockPathLength - 1e-3f);
            Assert.False(w.CanAcquire(0, 1));
            Assert.False(w.CanAcquire(1, 0));

            // A sight line through a shorter chord of the same cloud stays clear: the rule is path length, not presence.
            Vec3 chordFrom = new Vec3(0.75f, 1.4f, -6f);
            Vec3 chordTo = new Vec3(0.75f, 1.4f, 6f);
            float chord = w.Materials.ObscuringPathLength(chordFrom, chordTo);
            Assert.InRange(chord, 0.25f, GlobalRules.World.SteamSightBlockPathLength - 0.25f);
            Assert.True(w.HasClearSight(chordFrom, chordTo));

            // The cloud's 3 s base lifetime ends and acquisition returns.
            int expires = steam.ExpiresTick;
            Assert.Equal(Ticks.FromSeconds(GlobalRules.World.SteamLifetimeSec), expires - steam.CreatedTick);
            SpellTestKit.RunUntil(w, () => reactions.Volumes.Count == 0, expires - w.Tick + 5);
            Assert.Empty(reactions.Volumes);
            Assert.True(w.CanAcquire(0, 1));
        }

        [Fact]
        public void Reaction_Contact_Damage_From_A_Real_Cast_Triggers_No_Procs_And_Takes_No_Item_Bonuses()
        {
            // A map ice patch under the target (BIO-004 style). The Fireball's 48 direct damage shatters the struck tile
            // (spec L147: 20+ direct damage) and its 1 m shard cone hits the target, who also takes the 28 splash.
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.IcePatch(new Vec2(0f, -0.25f), 1.5f, 1.5f));
            DuelWorld w = ContentTestKit.BramVersusDummy(layout);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 0.2f), 180f);
            w.Player(0).Stats.Add(StatKind.SpellDamageBonus, 0.10f, "EQP-TEST"); // outgoing spell damage +10%
            w.Player(0).Stats.Add(StatKind.Lifesteal, 0.10f, "EQP-TEST");
            RecordAction taken = EffectTests.Record(w, 1, EffectTrigger.DamageTaken);
            RecordAction dealt = EffectTests.Record(w, 0, EffectTrigger.DirectHitDealt);

            SpellEvent impact = CastFireballAt(w, Vec3.Zero, TestWorlds.Face(180f));
            CastDuelEvent commit = ContentTestKit.LastCommit(w);
            SpellTestKit.Run(w, 2, TestWorlds.A, TestWorlds.Face(180f)); // effects dispatch at the end of the tick

            DamageEvent splash = SpellTestKit.DamageTo(w, 1).Single(d => d.Kind == DamageKind.Splash);
            DamageEvent shards = SpellTestKit.DamageTo(w, 1).Single(d => d.Kind == DamageKind.Environment);
            Assert.Equal(28f * 1.10f, splash.Dealt, 2);                      // the original hit takes the item bonus
            Assert.Equal(GlobalRules.World.ShardDamage, shards.Dealt, 3);    // the reaction does not (spec L126, L672)
            Assert.Equal(0, shards.Attacker);                                // attributed to the caster's source (L670)
            Assert.Equal("SPL-009", shards.SourceId);
            Assert.Equal(commit.Id, shards.OriginEventId);
            Assert.Equal(ReactionTag.Shatter, shards.Reaction);
            Assert.True(impact.Tick <= shards.Tick);

            // Only the splash raised a trigger: reaction contact damage raises none and grants no lifesteal.
            TriggerEvent t = Assert.Single(taken.Fired);
            Assert.Equal(splash.Id, t.EventId);
            Assert.Empty(dealt.Fired); // splash is not a direct hit, and the shards are not hits at all
            Assert.Empty(SpellTestKit.Events<HealEvent>(w));
        }
    }
}
