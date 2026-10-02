using System;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Players
{
    /// <summary>
    /// Health, mana, and stamina of one player for one duel (spec L78): 300 / 100 / 100 restored every duel, no
    /// passive health regeneration, mana 10/s after 1.5 s without spending (channels count as spending, capped at
    /// 14/s after modifiers), stamina 20/s after 1 s without spending. Nothing is spent without the full cost.
    /// </summary>
    public sealed class ResourcePools
    {
        private const int NeverSpent = int.MinValue / 2;

        public static int ManaRegenDelayTicks => Ticks.FromSeconds(GlobalRules.Resources.ManaRegenDelaySec);

        public static int StaminaRegenDelayTicks => Ticks.FromSeconds(GlobalRules.Resources.StaminaRegenDelaySec);

        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public float Mana { get; private set; }
        public float MaxMana { get; private set; } = GlobalRules.Resources.BaseMana;
        public float Stamina { get; private set; }
        public float MaxStamina { get; private set; } = GlobalRules.Resources.BaseStamina;

        public int LastManaSpendTick { get; private set; } = NeverSpent;
        public int LastStaminaSpendTick { get; private set; } = NeverSpent;

        /// <summary>Mana paid through legal commitments this duel (FEAT-013).</summary>
        public float ManaCommitted { get; private set; }

        /// <summary>Lowest health fraction reached this duel (FEAT-010).</summary>
        public float LowestHealthFraction { get; private set; } = 1f;

        public float HealthFraction => MaxHealth > 0f ? Health / MaxHealth : 0f;

        public bool Alive => Health > 0f;

        /// <summary>Restores every pool to full for a new duel (spec L46, L78).</summary>
        public void RestoreForDuel(float maxHealth) => RestoreForDuel(maxHealth, GlobalRules.Resources.BaseMana, GlobalRules.Resources.BaseStamina);

        /// <summary>Restores every pool to full for a new duel with the player's modified maxima (spec L46, L78, L579).</summary>
        public void RestoreForDuel(float maxHealth, float maxMana, float maxStamina)
        {
            MaxHealth = Math.Min(Math.Max(1f, maxHealth), GlobalRules.Resources.MaxHealthCap);
            MaxMana = Math.Max(1f, maxMana);
            MaxStamina = Math.Max(1f, maxStamina);
            Health = MaxHealth;
            Mana = MaxMana;
            Stamina = MaxStamina;
            LastManaSpendTick = NeverSpent;
            LastStaminaSpendTick = NeverSpent;
            ManaCommitted = 0f;
            LowestHealthFraction = 1f;
        }

        public bool CanAffordMana(float cost) => !(cost > Mana);

        public bool CanAffordStamina(float cost) => !(cost > Stamina);

        /// <summary>Pays a mana cost only when the full amount is available (spec L78).</summary>
        public bool TrySpendMana(float cost, int tick, bool commitment)
        {
            if (!(cost >= 0f) || cost > Mana)
            {
                return false;
            }

            Mana -= cost;
            LastManaSpendTick = tick;
            if (commitment)
            {
                ManaCommitted += cost;
            }

            return true;
        }

        /// <summary>Pays a stamina cost only when the full amount is available (spec L78).</summary>
        public bool TrySpendStamina(float cost, int tick)
        {
            if (!(cost >= 0f) || cost > Stamina)
            {
                return false;
            }

            if (cost > 0f)
            {
                Stamina -= cost;
                LastStaminaSpendTick = tick;
            }

            return true;
        }

        /// <summary>Marks a continuous channel this tick: it counts as spending mana (spec L78).</summary>
        public void MarkChanneling(int tick) => LastManaSpendTick = tick;

        public float GainMana(float amount)
        {
            if (!(amount > 0f))
            {
                return 0f;
            }

            float gained = Math.Min(amount, MaxMana - Mana);
            Mana += gained;
            return gained;
        }

        public float GainStamina(float amount)
        {
            if (!(amount > 0f))
            {
                return 0f;
            }

            float gained = Math.Min(amount, MaxStamina - Stamina);
            Stamina += gained;
            return gained;
        }

        /// <summary>Removes health (after mitigation). Health may go to or below zero; death resolves at step 9.</summary>
        public void LoseHealth(float amount)
        {
            if (!(amount > 0f))
            {
                return;
            }

            Health -= amount;
            float fraction = HealthFraction;
            if (fraction < LowestHealthFraction)
            {
                LowestHealthFraction = Math.Max(0f, fraction);
            }
        }

        /// <summary>Adds health up to the maximum; callers apply the healing budget first. Returns the amount restored.</summary>
        public float RestoreHealth(float amount)
        {
            if (!(amount > 0f) || Health <= 0f)
            {
                return 0f;
            }

            float restored = Math.Min(amount, MaxHealth - Health);
            Health += restored;
            return restored;
        }

        /// <summary>Changes maximum health within the 450 cap, keeping current health at most the new maximum.</summary>
        public void SetMaxHealth(float maxHealth)
        {
            MaxHealth = Math.Min(Math.Max(1f, maxHealth), GlobalRules.Resources.MaxHealthCap);
            Health = Math.Min(Health, MaxHealth);
        }

        /// <summary>Changes maximum mana (static capacity, spec L579), keeping current mana at most the new maximum.</summary>
        public void SetMaxMana(float maxMana)
        {
            MaxMana = Math.Max(1f, maxMana);
            Mana = Math.Min(Mana, MaxMana);
        }

        /// <summary>Changes maximum stamina, keeping current stamina at most the new maximum.</summary>
        public void SetMaxStamina(float maxStamina)
        {
            MaxStamina = Math.Max(1f, maxStamina);
            Stamina = Math.Min(Stamina, MaxStamina);
        }

        /// <summary>Per-tick regeneration (tick-order step 7). No passive health regeneration exists (spec L78).</summary>
        public void Regenerate(int tick, float manaRegenPerSecond)
        {
            Regenerate(tick, manaRegenPerSecond, GlobalRules.Resources.StaminaRegenPerSecond, ManaRegenDelayTicks, StaminaRegenDelayTicks);
        }

        /// <summary>
        /// Per-tick regeneration with the player's modified rates and delays (spec L78): mana at most 14/s after
        /// modifiers.
        /// </summary>
        public void Regenerate(int tick, float manaRegenPerSecond, float staminaRegenPerSecond, int manaDelayTicks, int staminaDelayTicks)
        {
            if (tick - LastManaSpendTick >= manaDelayTicks)
            {
                float rate = Math.Min(manaRegenPerSecond, GlobalRules.Resources.ManaRegenCapPerSecond);
                Mana = Math.Min(MaxMana, Mana + rate / Ticks.PerSecond);
            }

            if (tick - LastStaminaSpendTick >= staminaDelayTicks)
            {
                Stamina = Math.Min(MaxStamina, Stamina + Math.Max(0f, staminaRegenPerSecond) / Ticks.PerSecond);
            }
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(Health);
            h.Add(MaxHealth);
            h.Add(Mana);
            h.Add(Stamina);
            h.Add(MaxMana);
            h.Add(MaxStamina);
            h.Add(LastManaSpendTick);
            h.Add(LastStaminaSpendTick);
            h.Add(ManaCommitted);
            h.Add(LowestHealthFraction);
        }
    }
}
