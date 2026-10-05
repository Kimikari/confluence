using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Reactions;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Hosting.Bots
{
    /// <summary>
    /// The WIZ-010 Halen Tide bot casting profile (spec L381). Halen likes mid and close range and paths through water
    /// (wizard record "range_preference"). Each decision picks one spell by range and situation, using only legal
    /// information (<see cref="BotKitView"/>): the opponent as perceived through the sight query, the bot's own resources
    /// and cooldowns, and visible ground materials.
    /// <list type="number">
    /// <item>A seen opponent visibly winding up at 4 m or more: SPL-075 Bubble Buckler toward them (with AUG-437 the
    /// recast throws the remaining shield back).</item>
    /// <item>Visible heat or corrosion within 6 m of the bot's feet: SPL-080 Seal the Spring on the nearest cell.</item>
    /// <item>Steam combo: heat on the ground near the opponent: SPL-076 Water Pool on it (heat turns the water to a
    /// sight-blocking steam patch, spec L144).</item>
    /// <item>Within 2.2 m: the signature Undertow Mantle (2 m eddy); within 3.2 m: SPL-074 Water Hammer.</item>
    /// <item>5-17 m: SPL-079 Deepglass Prison (sometimes); otherwise SPL-073 Pressure Jet up to 19 m.</item>
    /// <item>Far (9-20 m): SPL-078 Riptide Lane toward the opponent (the bot then walks with its arrow) or SPL-077 Cascade
    /// Vault toward them; SPL-076 Water Pool at their feet as a fallback (Wet, spec L136).</item>
    /// </list>
    /// Scripts are the pressure-metering grammar's <see cref="PressureMeterScript"/>: normal casts reliably, and a mastery
    /// attempt only sometimes, by competence (Novice never). The grammar judges every attempt (spec L425).
    /// </summary>
    public sealed class HalenTideBotPlanner : IBotKitPlanner
    {
        public const string Id = "WIZ-010";
        public const string PressureJet = "SPL-073";
        public const string WaterHammer = "SPL-074";
        public const string BubbleBuckler = "SPL-075";
        public const string WaterPool = "SPL-076";
        public const string CascadeVault = "SPL-077";
        public const string RiptideLane = "SPL-078";
        public const string DeepglassPrison = "SPL-079";
        public const string SealTheSpring = "SPL-080";

        /// <summary>Mana kept above a spell's cost so a guard-and-run plan is never starved (decision: 2 mana).</summary>
        public const float ManaMargin = 2f;

        public const float EddyReach = 2.2f;   // the 2 m eddy plus a little body radius
        public const float HammerReach = 3.2f; // SPL-074 range 3 m plus a body radius
        public const float HazardScanRadius = 6f;
        public const float SteamComboRadius = 3f;

        public string WizardId => Id;

        /// <summary>The chance a cast tries the mastery timing, by competence ("mastery attempts sometimes").</summary>
        public static float MasteryChance(BotCompetence competence)
        {
            switch (competence)
            {
                case BotCompetence.Apprentice: return 0.15f;
                case BotCompetence.Adept: return 0.5f;
                case BotCompetence.Expert: return 0.75f;
                default: return 0f;
            }
        }

        public BotCastPlan Choose(in BotKitView v, Rng rng)
        {
            if (!v.Sees || v.Me == null || v.Me.Caster == null) return null;
            DuelPlayer me = v.Me;
            float d = v.Distance;
            BotCastPlan plan = null;

            if (v.OpponentCasting && d >= 4f && Ready(me, BubbleBuckler))
            {
                plan = Plan(me, BubbleBuckler, BotAimKind.Chest, default, "shield the visible windup");
                if (me.Caster.FormFor(BubbleBuckler).HasTag("recast")) plan.RecastAfterTicks = 45; // AUG-437: throw it back
            }
            else if (Ready(me, SealTheSpring) && NearestMaterial(v.World, me.Position, HazardScanRadius, true, true, out Vec3 hazard) &&
                     Visible(v.World, me, hazard))
            {
                plan = Plan(me, SealTheSpring, BotAimKind.Point, hazard, "seal the hazard at my feet");
            }
            else if (Ready(me, WaterPool) && d <= 17f && NearestMaterial(v.World, v.OpponentPosition, SteamComboRadius, true, false, out Vec3 hot) &&
                     Visible(v.World, me, hot))
            {
                plan = Plan(me, WaterPool, BotAimKind.Point, hot, "water on their heat: a steam screen");
            }
            else if (d <= EddyReach && SignatureReady(me))
            {
                plan = new BotCastPlan { Signature = true, SpellId = Id, Aim = BotAimKind.Chest, Reason = "undertow at close range" };
            }
            else if (d <= HammerReach && Ready(me, WaterHammer))
            {
                plan = Plan(me, WaterHammer, BotAimKind.Chest, default, "hammer in reach");
            }
            else if (d >= 5f && d <= 17f && Ready(me, DeepglassPrison) && rng.Chance(0.6f))
            {
                plan = Plan(me, DeepglassPrison, BotAimKind.Chest, default, "imprison at mid range");
            }
            else if (d >= 2.5f && d <= 19f && Ready(me, PressureJet))
            {
                plan = Plan(me, PressureJet, BotAimKind.Chest, default, "jet");
            }
            else if (d >= 9f && d <= 20f && Ready(me, RiptideLane))
            {
                plan = Plan(me, RiptideLane, BotAimKind.Point, Toward(me.Position, v.OpponentPosition, 2.5f), "a stream toward them");
            }
            else if (d >= 8f && Ready(me, CascadeVault))
            {
                float reach = Math.Min(me.Caster.FormFor(CascadeVault).Range - 0.3f, d - 3f);
                plan = Plan(me, CascadeVault, BotAimKind.Point, Toward(me.Position, v.OpponentPosition, reach), "vault in");
            }
            else if (d >= 3f && d <= 17f && Ready(me, WaterPool))
            {
                plan = Plan(me, WaterPool, BotAimKind.OpponentFeet, default, "pool at their feet");
            }

            if (plan != null && !plan.Signature) plan.AttemptMastery = rng.Chance(MasteryChance(v.Profile != null ? v.Profile.Competence : BotCompetence.Novice));
            return plan;
        }

        public BotCastScript ScriptFor(BotCastPlan plan, BotProfile profile, Rng rng)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Signature) return new SignatureScript();
            BotProfile p = profile.Clone();
            p.AttemptsMastery = plan.AttemptMastery;
            return new PressureMeterScript(p, rng);
        }

        // ---- legal queries ----------------------------------------------------------------------------------------

        /// <summary>The slot of an equipped spell, or -1.</summary>
        public static int SlotOf(DuelPlayer me, string spellId)
        {
            IReadOnlyList<string> spells = me.Setup.EquippedSpellIds;
            for (int i = 0; i < spells.Count && i < PlayerInput.SlotCount; i++)
            {
                if (spells[i] == spellId) return i;
            }

            return -1;
        }

        /// <summary>Equipped, off cooldown, affordable (own state only).</summary>
        public static bool Ready(DuelPlayer me, string spellId)
        {
            if (SlotOf(me, spellId) < 0) return false;
            SpellForm form = me.Caster.FormFor(spellId);
            return form != null && me.Caster.CooldownRemainingTicks(spellId) == 0 && me.Resources.Mana >= form.Mana + ManaMargin;
        }

        public static bool SignatureReady(DuelPlayer me)
        {
            SpellForm sig = me.Caster.Loadout.Signature;
            return sig != null && me.Caster.CooldownRemainingTicks(Id) == 0 && me.Resources.Mana >= sig.Mana + ManaMargin;
        }

        /// <summary>
        /// The nearest ground point within a radius whose cell visibly holds heat (and, when asked, venom), scanned on a
        /// 0.5 m lattice in a stable order.
        /// </summary>
        public static bool NearestMaterial(DuelWorld world, Vec3 around, float radius, bool heat, bool venom, out Vec3 point)
        {
            point = around;
            ReactionWorld reactions = (world.Materials as ReactionMaterials)?.World;
            if (reactions == null) return false;
            float best = float.MaxValue;
            const float step = 0.5f;
            int n = (int)Math.Ceiling(radius / step);
            for (int ix = -n; ix <= n; ix++)
            {
                for (int iz = -n; iz <= n; iz++)
                {
                    float dx = ix * step;
                    float dz = iz * step;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > radius * radius || d2 >= best) continue;
                    var p = new Vec3(around.X + dx, around.Y, around.Z + dz);
                    CellUnits u = reactions.UnitsAt(p);
                    if ((heat && u.Heat > 0) || (venom && u.Venom > 0))
                    {
                        best = d2;
                        point = p;
                    }
                }
            }

            return best < float.MaxValue;
        }

        /// <summary>The bot's eye sees the point (no solid in between): a seal or pool needs a visible surface.</summary>
        public static bool Visible(DuelWorld world, DuelPlayer me, Vec3 point)
        {
            Vec3 to = point + new Vec3(0f, 0.05f, 0f) - me.Eye;
            float length = to.Length();
            if (length < 1e-3f) return true;
            return !world.Collision.Raycast(me.Eye, to / length, length - 0.1f, SolidMask.Movement, out _);
        }

        private static Vec3 Toward(Vec3 from, Vec3 to, float meters)
        {
            Vec3 flat = new Vec3(to.X - from.X, 0f, to.Z - from.Z);
            float length = flat.Length();
            if (length < 1e-3f) return from;
            return new Vec3(from.X + flat.X / length * meters, from.Y, from.Z + flat.Z / length * meters);
        }

        private static BotCastPlan Plan(DuelPlayer me, string spellId, BotAimKind aim, Vec3 point, string reason) =>
            new BotCastPlan { Slot = SlotOf(me, spellId), SpellId = spellId, Aim = aim, Point = point, Reason = reason };
    }
}
