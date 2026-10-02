using Confluence.Sim.Duel;

namespace Confluence.Sim.Effects
{
    public enum EffectChange
    {
        /// <summary>An effect passed its filter, condition, cooldowns, and proc budget and ran its actions.</summary>
        Activated,

        /// <summary>A cue or marker action (presentation only).</summary>
        Cue,

        /// <summary>An effect granted stamina (mana gains use the resource event).</summary>
        StaminaGained,
    }

    /// <summary>Effect activity for the HUD, replays, and the decisive sequence (spec L359, L433).</summary>
    public sealed class EffectEvent : DuelEvent
    {
        public EffectChange Change { get; internal set; }
        public string SourceId { get; internal set; }

        /// <summary>The attached effect's key (source id and attach order).</summary>
        public string EffectKey { get; internal set; }

        public EffectTrigger Trigger { get; internal set; }

        /// <summary>The duel event that raised the trigger, or 0.</summary>
        public int TriggerEventId { get; internal set; }

        public string Cue { get; internal set; }
        public float Amount { get; internal set; }

        public override DuelEventType Type => DuelEventType.Effect;
    }
}
