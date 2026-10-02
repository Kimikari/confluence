namespace Confluence.Casting
{
    /// <summary>
    /// Authoritative gameplay events that event-triggered grammars respond to (spec L472, L482: "Parry and melee
    /// grammars need a genuine combat event"; L478 movement grammars need real travel). These are produced by the
    /// simulation, never by client input, so a client cannot forge a parry or a hit.
    /// </summary>
    public enum CombatEventKind : byte
    {
        /// <summary>Own guard blocked a hit inside the perfect window (spec L104); WIZ-006 "successful common parry".</summary>
        PerfectGuard = 0,

        /// <summary>Own guard blocked a hit outside the perfect window.</summary>
        GuardBlock = 1,

        /// <summary>Own common melee strike damaged the opponent (spec L102); WIZ-015 hit confirm.</summary>
        MeleeHit = 2,

        /// <summary>Own guard stopped an opponent's melee strike; WIZ-015 "successful melee parry".</summary>
        MeleeParry = 3,

        /// <summary>Own common melee action completed its windup (WIZ-015 follow-up; cancelled windups do not count).</summary>
        MeleeWindupCompleted = 4,

        /// <summary>Own common melee recovery ended (WIZ-035 declared event).</summary>
        MeleeRecoveryEnded = 5,

        /// <summary>Own dodge finished its travel (spec L82).</summary>
        DodgeCompleted = 6,

        /// <summary>Own body landed on supporting ground after being airborne (WIZ-035).</summary>
        Landed = 7,

        /// <summary>One of the caster's spell cooldowns became ready; <see cref="CombatEvent.SpellId"/> names it (WIZ-035).</summary>
        CooldownReady = 8,

        /// <summary>The caster took damage (amount in <see cref="CombatEvent.Value"/>).</summary>
        DamageTaken = 9,

        /// <summary>The caster was moved by a hostile force or knockback (WIZ-030: knocked across marks never qualifies).</summary>
        ForcedDisplacement = 10,

        /// <summary>The caster teleported (WIZ-030: teleporting across marks never qualifies).</summary>
        Teleported = 11,

        /// <summary>The caster's movement was stopped by terrain collision (WIZ-014: invalidates only the reward).</summary>
        TerrainCollision = 12,
    }

    /// <summary>One authoritative combat event delivered to the caster's grammar on the tick it happened.</summary>
    public struct CombatEvent
    {
        public CombatEventKind Kind;
        public int Tick;

        /// <summary>Related spell for <see cref="CombatEventKind.CooldownReady"/>; otherwise null.</summary>
        public string SpellId;

        /// <summary>Kind-specific magnitude (damage amount, displacement meters); 0 when unused.</summary>
        public float Value;

        public CombatEvent(CombatEventKind kind, int tick, string spellId = null, float value = 0f)
        {
            Kind = kind;
            Tick = tick;
            SpellId = spellId;
            Value = value;
        }
    }
}
