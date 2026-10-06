using System;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Augments.AugmentsB
{
    /// <summary>
    /// Shared helpers of the augments-b families (Resource and recovery, Defense and commitment, Information and deception,
    /// Summon husbandry, Build covenants, Combo architectures). They reuse augments-a's helpers (<see cref="AugmentsAKit"/>,
    /// <see cref="CastFacts"/>); numbers the records leave open are named here with the spec line they interpret.
    /// </summary>
    internal static class AugmentsBKit
    {
        public static float ByRank(int rank, float rank1, float rank2) => rank >= 2 ? rank2 : rank1;

        public static int Ticks(float seconds) => Confluence.Core.Time.Ticks.FromSeconds(seconds);

        /// <summary>A quote for one of the player's equipped spells (signatures are not spells, spec L472).</summary>
        public static bool IsSpellQuote(in CastQuote quote) => AugmentsAKit.IsSpellSource(quote.SpellId);

        /// <summary>A paid commitment of one of the player's equipped spells: mana was paid (spec L90).</summary>
        public static bool IsPaidSpellCommit(DuelEvent e, int player, out CastDuelEvent commit) =>
            CastFacts.IsSpellCommit(e, player, out commit) && commit.Cast.Mana > 0f;

        /// <summary>A release of one of the player's equipped spells.</summary>
        public static bool IsSpellRelease(DuelEvent e, int player, out CastDuelEvent release)
        {
            release = e as CastDuelEvent;
            return release != null && release.Player == player && release.Kind == CastEventKind.Released && release.Cast.Slot >= 0;
        }

        /// <summary>An original direct hit (spell impact or common melee) by the attacker on its opponent.</summary>
        public static bool IsDirectHitOn(DamageEvent d, int attacker) =>
            AugmentsAKit.IsOriginalHitOn(d, attacker) && (d.Kind == DamageKind.Direct || d.Kind == DamageKind.Melee);

        /// <summary>An original direct hit the player received from its opponent (spell impact or common melee).</summary>
        public static bool IsDirectHitTaken(DamageEvent d, int player) =>
            d != null && d.Target == player && d.Attacker == 1 - player && d.Generation == 0 && d.Dealt > 0f
            && (d.Kind == DamageKind.Direct || d.Kind == DamageKind.Melee);

        /// <summary>Whether the form of the player's equipped spell carries a catalogue tag.</summary>
        public static bool SpellHasTag(DuelPlayer p, string spellId, string tag)
        {
            SpellForm form = AugmentsAKit.FormOf(p, spellId);
            return form != null && form.HasTag(tag);
        }

        /// <summary>A damaging spell (its form carries the "damaging" tag).</summary>
        public static bool IsDamagingSpell(DuelPlayer p, string spellId) => SpellHasTag(p, spellId, "damaging");

        /// <summary>A defensive spell (its structural profile carries the "defensive" tag, spec L543).</summary>
        public static bool IsDefensiveSpell(DuelPlayer p, string spellId) => SpellHasTag(p, spellId, "defensive");

        /// <summary>
        /// A drawback's stamina cost ("removes 1 additional stamina"): spends up to <paramref name="amount"/>, never below zero
        /// (spec L78); spending restarts the stamina regeneration delay like any other stamina cost.
        /// </summary>
        public static float SpendStamina(DuelWorld world, int player, float amount)
        {
            DuelPlayer p = world.Player(player);
            float spent = Math.Min(Math.Max(0f, amount), p.Resources.Stamina);
            return spent > 0f && p.Resources.TrySpendStamina(spent, world.Tick) ? spent : 0f;
        }

        /// <summary>The player's casting is in any phase (preparation, windup, channel, recovery).</summary>
        public static bool IsCasting(DuelWorld world, int player) => world.Spells != null && world.Spells.GetStatus(player).Busy;

        /// <summary>Horizontal unit direction from a point to another (zero when they coincide).</summary>
        public static Vec3 FlatDirection(Vec3 from, Vec3 to)
        {
            Vec3 d = to - from;
            d = new Vec3(d.X, 0f, d.Z);
            return d.LengthSquared() > 1e-8f ? d.Normalized() : Vec3.Zero;
        }

        public static float FlatDistance(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, 0f, a.Z - b.Z).Length();

        /// <summary>The player's eye can see a world point in normal sight (no sight-blocking solid, less than 2 m of obscurant, L167).</summary>
        public static bool InNormalSight(DuelWorld world, int viewer, Vec3 point) => world.HasClearSight(world.Player(viewer).Eye, point);

        /// <summary>The opponent's chest, the reference point for sight and sound tests against a player.</summary>
        public static Vec3 Chest(DuelPlayer p) => p.Position + new Vec3(0f, p.Motor.Height * 0.6f, 0f);

        public static int Bit(DamageChannel channel) => 1 << (int)channel;
    }

    /// <summary>
    /// The base of the augments-b hooks: an <see cref="EffectHook"/> with an owner-only information cue
    /// (<see cref="InfoCueEvent"/>) and a resource gain that goes through the framework's gain service.
    /// </summary>
    internal abstract class RecordHook : EffectHook
    {
        protected RecordHook(string sourceId) : base(sourceId)
        {
        }

        /// <summary>Emits an owner-only information cue visible for <paramref name="durationTicks"/>.</summary>
        protected InfoCueEvent EmitInfo(InfoCueKind kind, string cue, int durationTicks, Vec3? position = null, Vec3? direction = null,
            Vec3? to = null, float radius = 0f, int entityId = -1, float amount = 0f, float amount2 = 0f, DamageChannel[] channels = null)
        {
            int ticks = Math.Max(1, durationTicks);
            return World.RecordEvent(new InfoCueEvent
            {
                SourceId = SourceId,
                EffectKey = Key,
                Cue = cue,
                Kind = kind,
                HasPosition = position.HasValue,
                Position = position ?? Vec3.Zero,
                Direction = direction ?? Vec3.Zero,
                To = to ?? (position ?? Vec3.Zero),
                Radius = radius,
                EntityId = entityId,
                Amount = amount,
                Amount2 = amount2,
                Channels = channels ?? new DamageChannel[0],
                DurationTicks = ticks,
                ExpireTick = Now + ticks,
            }, Player);
        }

        /// <summary>A public inert marker both players see (spec L433 cue): see <see cref="MarkerEvent"/>.</summary>
        protected MarkerEvent EmitPublicMarker(string cue, Vec3 from, Vec3 to, Vec3 direction, int durationTicks, float amount = 0f) =>
            EmitMarker(cue, from, to, direction, durationTicks, amount);

        protected void GainStamina(float amount) => Host.System.GainResource(Player, EffectResource.Stamina, amount, SourceId);

        protected void GainMana(float amount) => Host.System.GainResource(Player, EffectResource.Mana, amount, SourceId);

        /// <summary>A triggered shield of the owner (spec L114: at most 8 s, shared 100 cap).</summary>
        protected float GrantShield(float amount, float seconds, int player = -1) =>
            World.GrantShield(player < 0 ? Player : player, SourceId, amount, AugmentsBKit.Ticks(seconds));

        /// <summary>Whether a cooldown that started at <paramref name="lastTick"/> has elapsed.</summary>
        protected bool Ready(int lastTick, float cooldownSec) => Now - lastTick >= AugmentsBKit.Ticks(cooldownSec);
    }
}
