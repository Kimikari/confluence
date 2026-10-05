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

        /// <summary>Mana reductions (in mana) that apply only to a mastered or only to a normal cast (EffectHook.AdjustMasteryPrice).</summary>
        public void MasteryPrice(int player, in CastQuote quote, out float mastered, out float normal)
        {
            mastered = 0f;
            normal = 0f;
            _hosts[player].AdjustMasteryPriceHooks(quote, ref mastered, ref normal);
        }

        /// <summary>A released projectile's launch speed after the caster's hooks.</summary>
        public float AdjustProjectileSpeed(Spells.CastRecord cast, float speed)
        {
            if (cast == null || cast.Caster < 0 || cast.Caster >= DuelWorld.PlayerCount)
            {
                return speed;
            }

            return _hosts[cast.Caster].AdjustProjectileSpeedHooks(cast, speed);
        }

        /// <summary>
        /// A field cast's placement bound at commitment by the caster's hooks (EffectHook.BindFieldPlacement): replaces the
        /// release-time ground point and direction when a hook binds it.
        /// </summary>
        public void BindFieldPlacement(Spells.CastRecord cast, ref Core.Geometry.Vec3 ground, ref Core.Geometry.Vec3 direction)
        {
            if (cast == null || cast.Caster < 0 || cast.Caster >= DuelWorld.PlayerCount)
            {
                return;
            }

            if (_hosts[cast.Caster].BindFieldPlacementHooks(cast, out Core.Geometry.Vec3 g, out Core.Geometry.Vec3 d))
            {
                ground = g;
                direction = d;
            }
        }

        /// <summary>A drawback's after-commitment mana charge: at most the mana the player has (spec L78).</summary>
        internal float ChargeMana(int player, float amount, string sourceId)
        {
            Players.DuelPlayer p = World.Player(player);
            float spent = System.Math.Min(System.Math.Max(0f, amount), p.Resources.Mana);
            if (!(spent > 0f) || !p.Resources.TrySpendMana(spent, World.Tick, false))
            {
                return 0f;
            }

            World.RecordEvent(new ResourceEvent { Change = ResourceChange.ManaSpent, Amount = spent, SourceId = sourceId }, player);
            return spent;
        }

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
