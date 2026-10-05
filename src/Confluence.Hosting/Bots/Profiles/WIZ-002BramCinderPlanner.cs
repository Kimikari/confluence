using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Reactions;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Spells;
using Confluence.Sim.World.Materials;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Hosting.Bots.Profiles
{
    /// <summary>
    /// The bot casting profile of WIZ-002 Bram Cinder ("Mid and close range; release timing rewards deliberate peeks").
    /// Every spell goes through the crest-release grammar with <see cref="CrestReleaseScript"/>: a normal release
    /// (about 0.3 s) always commits; a crest attempt (0.45 s, the marked 100 ms window) is made only sometimes, by
    /// competence (<see cref="MasteryAttemptChance"/>), and lands only as often as the bot's timing jitter allows.
    /// Spell choice follows range and situation, using only legal information (<see cref="BotSituation"/>):
    /// <list type="bullet">
    /// <item>close (cone reach): Kiln Vent (signature) when the opponent is inside 3.5 m in front; Cinder Sweep (4 m cone);
    /// Flashforge (2 m smash under the opponent, preferred when an intact metal prop stands within its area);</item>
    /// <item>close-mid: Flame Lash (6 m arc), preferred when the opponent stands on dry growth (its Heat 1 ignites it);</item>
    /// <item>mid: Phoenix Rush to close an 8-10 m gap (its endpoint burst lands on arrival), or away when low and pressed;</item>
    /// <item>mid-long: Boiler Orb under the opponent unless the ground there is wet (water steams the orb and halves the
    /// explosion), detonated by recast once the perceived opponent is within its explosion radius; Fireball otherwise;</item>
    /// <item>defensive: Heat Haze when Bram is wet (its heat dries him) or under ranged pressure; Ash Curtain between
    /// the two when low on health, never on wet ground (water collapses the ash).</item>
    /// </list>
    /// </summary>
    public sealed class BramCinderPlanner : IBotSpellPlanner
    {
        public const string Id = "WIZ-002";

        public const string Fireball = "SPL-009";
        public const string CinderSweep = "SPL-010";
        public const string HeatHaze = "SPL-011";
        public const string AshCurtain = "SPL-012";
        public const string PhoenixRush = "SPL-013";
        public const string BoilerOrb = "SPL-014";
        public const string FlameLash = "SPL-015";
        public const string Flashforge = "SPL-016";

        /// <summary>Kiln Vent's 4 m cone, minus a margin for the body and the reaction latency.</summary>
        public const float VentDistance = 3.5f;
        public const float VentAngleDeg = 30f;
        public const float SweepDistance = 3.6f;
        public const float FlashforgeDistance = 4.5f;
        public const float LashDistance = 5.8f;
        public const float RushMin = 7.5f;
        public const float RushMax = 10f;
        public const float OrbMin = 5f;
        public const float OrbMax = 20f;
        public const float FireballMin = 4f;
        public const float FireballMax = 28f;

        private const float LowHealth = 0.4f;
        private const float EscapeHealth = 0.3f;

        public string WizardId => Id;

        /// <summary>Diagnostics: casts planned per spell id, recasts and signature presses.</summary>
        public Dictionary<string, int> Planned { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Recasts { get; private set; }
        public int Vents { get; private set; }
        public int MasteryAttempts { get; private set; }

        /// <summary>How often a cast tries the crest window, by competence (Novice "uses normal casts", L381).</summary>
        public static float MasteryAttemptChance(BotProfile profile)
        {
            if (profile == null) return 0f;
            switch (profile.Competence)
            {
                case BotCompetence.Novice:
                    return 0f;
                case BotCompetence.Apprentice:
                    return profile.AttemptsMastery ? 0.4f : 0.15f;
                case BotCompetence.Adept:
                    return profile.AttemptsMastery ? 0.5f : 0.2f;
                default:
                    return profile.AttemptsMastery ? 0.8f : 0.25f;
            }
        }

        public bool TryPlanCast(in BotSituation s, Rng rng, out BotCastPlan plan)
        {
            plan = null;
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            DuelPlayer me = s.Me;
            // Bram holds his grammar casts while pressing in for melee (his signature still fires, see ExtraButtons).
            if (!s.Sees || s.Rushing || me.Caster == null) return false;
            float health = me.Resources.MaxHealth > 0f ? me.Resources.Health / me.Resources.MaxHealth : 0f;
            float d = s.Distance;
            Vec3 toward = Flat(s.Opponent - me.Position);

            // Defensive first: drying off and sight-breaking.
            if (me.Statuses.IsWet && Ready(s, HeatHaze))
            {
                return Plan(s, rng, HeatHaze, BotAimMode.Chest, default, out plan);
            }

            if (health < LowHealth && d >= 4f && d <= 16f && Ready(s, AshCurtain))
            {
                Vec3 between = me.Position + toward * Math.Min(4f, d * 0.5f);
                if (!IsWet(s, between)) return Plan(s, rng, AshCurtain, BotAimMode.Point, between, out plan);
            }

            if (health < EscapeHealth && d < 4f && Ready(s, PhoenixRush))
            {
                Vec3 away = me.Position - toward * 8f;
                return Plan(s, rng, PhoenixRush, BotAimMode.Point, away, out plan);
            }

            // Close range.
            if (d <= FlashforgeDistance && Ready(s, Flashforge) && (MetalPropNear(s, s.Opponent, 2f) || rng.Chance(0.35f)))
            {
                return Plan(s, rng, Flashforge, BotAimMode.Feet, default, out plan);
            }

            if (d <= SweepDistance && Ready(s, CinderSweep))
            {
                return Plan(s, rng, CinderSweep, BotAimMode.Chest, default, out plan);
            }

            if (d <= LashDistance && Ready(s, FlameLash) && (d > SweepDistance || OnGrowth(s, s.Opponent) || rng.Chance(0.5f)))
            {
                return Plan(s, rng, FlameLash, BotAimMode.Chest, default, out plan);
            }

            // Mid range.
            if (d >= RushMin && d <= RushMax && health >= 0.5f && Ready(s, PhoenixRush) && rng.Chance(0.5f))
            {
                return Plan(s, rng, PhoenixRush, BotAimMode.Feet, default, out plan);
            }

            if (d >= OrbMin && d <= OrbMax && Ready(s, BoilerOrb) && !OwnsOrb(s) && !IsWet(s, s.Opponent) && rng.Chance(0.6f))
            {
                return Plan(s, rng, BoilerOrb, BotAimMode.Feet, default, out plan);
            }

            if (d >= 8f && Ready(s, HeatHaze) && rng.Chance(0.15f))
            {
                return Plan(s, rng, HeatHaze, BotAimMode.Chest, default, out plan);
            }

            if (d >= FireballMin && d <= FireballMax && Ready(s, Fireball))
            {
                return Plan(s, rng, Fireball, BotAimMode.Chest, default, out plan);
            }

            return false;
        }

        public InputButtons ExtraButtons(in BotSituation s, Rng rng, ref int slot)
        {
            DuelPlayer me = s.Me;
            if (me.Caster == null || s.World.Spells.GetStatus(s.Self).Busy) return InputButtons.None;

            // Boiler Orb: detonate by recast once the perceived opponent is inside the explosion radius of the settled orb.
            if (s.Sees)
            {
                BoilerOrbEntity orb = OwnOrb(s);
                if (orb != null && orb.IsIntact && !orb.Warning && orb.Cast.Form.Delivery is BoilerOrbDelivery delivery && delivery.DetonateOnRecast)
                {
                    float reach = delivery.ExplosionRadius + 0.2f;
                    if (Vec3.Distance(orb.Position, s.Opponent + new Vec3(0f, 0.9f, 0f)) <= reach + 0.9f &&
                        Vec2.Distance(orb.Position.XZ(), s.Opponent.XZ()) <= reach)
                    {
                        int orbSlot = SlotOf(me, BoilerOrb);
                        if (orbSlot >= 0)
                        {
                            slot = orbSlot;
                            Recasts++;
                            return InputButtons.Recast;
                        }
                    }
                }
            }

            // Kiln Vent: the opponent inside the cone in front, signature ready and affordable.
            SpellForm vent = me.Caster.Loadout.Signature;
            if (s.Sees && vent != null && s.Distance <= VentDistance && me.Caster.CooldownRemainingTicks(vent.SpellId) == 0 &&
                me.Resources.Mana >= vent.Castable.Mana && Facing(me, s.Opponent) <= VentAngleDeg)
            {
                Vents++;
                return InputButtons.Signature;
            }

            return InputButtons.None;
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        private bool Plan(in BotSituation s, Rng rng, string spellId, BotAimMode aim, Vec3 point, out BotCastPlan plan)
        {
            BotProfile profile = s.Profile.Clone();
            profile.AttemptsMastery = rng.Chance(MasteryAttemptChance(s.Profile));
            if (profile.AttemptsMastery) MasteryAttempts++;
            plan = new BotCastPlan
            {
                SpellId = spellId,
                Slot = SlotOf(s.Me, spellId),
                Script = new CrestReleaseScript(profile, rng),
                Aim = aim,
                AimPoint = point,
            };
            Planned.TryGetValue(spellId, out int n);
            Planned[spellId] = n + 1;
            return true;
        }

        /// <summary>Equipped in a selectable slot, off cooldown, affordable, and with a behavior in this build.</summary>
        private static bool Ready(in BotSituation s, string spellId)
        {
            DuelPlayer me = s.Me;
            int slot = SlotOf(me, spellId);
            if (slot < 0) return false;
            SpellForm form = me.Caster.FormFor(spellId);
            return form != null && me.Caster.CooldownRemainingTicks(spellId) == 0 && me.Resources.Mana >= form.Castable.Mana;
        }

        private static int SlotOf(DuelPlayer me, string spellId)
        {
            IReadOnlyList<string> ids = me.Setup.EquippedSpellIds;
            for (int i = 0; i < ids.Count && i < PlayerInputSlots; i++)
            {
                if (string.Equals(ids[i], spellId, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        private static int PlayerInputSlots => Confluence.Sim.Input.PlayerInput.SlotCount;

        private static BoilerOrbEntity OwnOrb(in BotSituation s)
        {
            if (!(s.World.Spells is SpellRuntime runtime)) return null;
            IReadOnlyList<SpellEntity> entities = runtime.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] is BoilerOrbEntity orb && orb.Owner == s.Self && orb.Alive) return orb;
            }

            return null;
        }

        private static bool OwnsOrb(in BotSituation s) => OwnOrb(s) != null;

        /// <summary>Visible liquid water in the cell under a point (spec L118); false without a reaction world.</summary>
        private static bool IsWet(in BotSituation s, Vec3 point) =>
            s.World.Materials is ReactionMaterials m && m.World != null && m.World.IsWetAt(point);

        private static bool OnGrowth(in BotSituation s, Vec3 point) =>
            s.World.Materials is ReactionMaterials m && m.World != null && m.World.UnitsAt(point).Growth > 0 && !m.World.IsWetAt(point);

        private static bool MetalPropNear(in BotSituation s, Vec3 point, float radius)
        {
            Catalogue catalogue = s.World.Catalogue;
            if (catalogue == null || s.World.Props == null) return false;
            IReadOnlyList<PropBody> props = s.World.Props.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropBody prop = props[i];
                if (prop.Destroyed || Vec2.Distance(prop.Box.Center.XZ(), point.XZ()) > radius + 0.5f) continue;
                if (catalogue.TryGetProp(prop.DefinitionId, out PropDef def) && def.HasTag("metal")) return true;
            }

            return false;
        }

        private static float Facing(DuelPlayer me, Vec3 target)
        {
            float yaw = CMath.YawOf(target - me.Position);
            return Math.Abs(CMath.WrapAngle(yaw - me.Motor.YawDeg));
        }

        private static Vec3 Flat(Vec3 v)
        {
            var flat = new Vec3(v.X, 0f, v.Z);
            return flat.LengthSquared() > 1e-8f ? flat.Normalized() : Vec3.Forward;
        }
    }
}
