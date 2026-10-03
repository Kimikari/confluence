using System.Collections.Generic;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;

namespace Confluence.Sim.Tests.Effects
{
    /// <summary>A cue action that records every activation's trigger (tests only).</summary>
    internal sealed class RecordAction : IEffectAction
    {
        public List<TriggerEvent> Fired { get; } = new List<TriggerEvent>();
        public SecondaryEvents Produces => SecondaryEvents.Cue;
        public string Describe => "record";

        public void Apply(in EffectContext context) => Fired.Add(context.Trigger);
    }

    internal static class EffectTests
    {
        /// <summary>Attaches a recording effect for a trigger to a player and returns its recorder.</summary>
        public static RecordAction Record(DuelWorld world, int player, EffectTrigger trigger, System.Action<Effect> configure = null)
        {
            var record = new RecordAction();
            var effect = new Effect("AUG-TEST", trigger).With(record);
            configure?.Invoke(effect);
            world.Effects.Host(player).Attach(effect);
            return record;
        }
    }
}
