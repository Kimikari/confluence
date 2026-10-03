using Confluence.Casting;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Movement;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// The call sites of <see cref="EffectHook"/>s (see EffectHooks.cs): the world asks here at the exact moment a rule
    /// computes a value, and the dispatch point ticks every hook before the tick's triggers fire.
    /// </summary>
    public sealed partial class EffectSystem
    {
        private void TickHooks()
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                _hosts[i].TickHooks();
            }
        }

        /// <summary>Tick-order step 2: the player's motor command after its hooks (dodge, jump, sprint costs, slows).</summary>
        public void AdjustMotor(int player, ref MotorCommand command) => _hosts[player].AdjustMotorHooks(ref command);

        /// <summary>Strike begin: the player's common melee profile after its hooks.</summary>
        public MeleeProfile AdjustStrike(int player, MeleeProfile profile) => _hosts[player].AdjustStrikeHooks(profile);

        /// <summary>Windup and recovery changes the player's hooks request for a cast being committed.</summary>
        public CastTimingAdjust CastTiming(int player, in CastQuote quote)
        {
            var timing = new CastTimingAdjust();
            _hosts[player].AdjustCastTimingHooks(quote, ref timing);
            return timing;
        }

        /// <summary>
        /// Durability damage the attacker's spell deals to a destructible solid after the attacker's hooks (cover and prop
        /// damage bonuses). Players other than 0 and 1 (the world) are unchanged.
        /// </summary>
        public float AdjustSolidDamage(int attacker, int solidId, float amount, string sourceId, CastEventId castId)
        {
            if (attacker < 0 || attacker >= DuelWorld.PlayerCount)
            {
                return amount;
            }

            return _hosts[attacker].AdjustSolidDamageHooks(solidId, amount, sourceId, castId);
        }
    }
}
