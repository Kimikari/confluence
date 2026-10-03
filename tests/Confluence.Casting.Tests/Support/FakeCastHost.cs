using System.Collections.Generic;
using Confluence.Casting;

namespace Confluence.Casting.Tests.Support
{
    /// <summary>Minimal authoritative caster state for machine tests.</summary>
    internal sealed class FakeCastHost : ICastHost
    {
        private readonly Dictionary<string, int> _cooldownReadyAt = new Dictionary<string, int>();

        public int CurrentTick;
        public float ManaPool = 100f;
        public float TotalPaid;
        public float TotalRefunded;
        public int Payments;
        public CastModifiers Modifiers;

        public float Mana => ManaPool;
        public bool IsSilenced { get; set; }
        public bool IsHardControlled { get; set; }
        public bool IsWarded { get; set; }
        public bool IsActionLocked { get; set; }

        public bool TryPayMana(float amount)
        {
            if (amount > ManaPool) return false;
            ManaPool -= amount;
            TotalPaid += amount;
            Payments++;
            return true;
        }

        public bool TryPayCommitment(CastableSpell spell, float amount) => TryPayMana(amount);

        public void RefundMana(float amount)
        {
            ManaPool += amount;
            TotalRefunded += amount;
        }

        public int CooldownRemainingTicks(string spellId) =>
            _cooldownReadyAt.TryGetValue(spellId, out int ready) && ready > CurrentTick ? ready - CurrentTick : 0;

        public void StartCooldown(CastableSpell spell) => _cooldownReadyAt[spell.Id] = CurrentTick + spell.CooldownTicks;

        public void ClearCooldowns() => _cooldownReadyAt.Clear();

        public CastModifiers ModifiersFor(CastableSpell spell) => Modifiers;
    }
}
