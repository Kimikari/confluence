using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Input;
using InputButtons = Confluence.Casting.InputButtons;
using Confluence.Sim.Movement;
using Confluence.Sim.Status;

namespace Confluence.Sim.Players
{
    /// <summary>Authoritative state of one fighter in a duel.</summary>
    public sealed class DuelPlayer
    {
        internal DuelPlayer(int index, DuelPlayerSetup setup)
        {
            Index = index;
            Setup = setup;
            Stats = new StatSheet();
            if (setup.Modifiers != null)
            {
                for (int i = 0; i < setup.Modifiers.Count; i++)
                {
                    Stats.Add(setup.Modifiers[i]);
                }
            }
        }

        public int Index { get; }
        public DuelPlayerSetup Setup { get; }

        public MotorState Motor;

        public ResourcePools Resources { get; } = new ResourcePools();
        public StatSheet Stats { get; }
        public ShieldPool Shields { get; } = new ShieldPool();
        public HealingBudget Healing { get; } = new HealingBudget();
        public GuardState Guard { get; } = new GuardState();
        public MeleeState Melee { get; } = new MeleeState();
        public StatusSet Statuses { get; } = new StatusSet();
        public BoundaryTracker Boundary { get; } = new BoundaryTracker();
        public FeatTracker Feats { get; } = new FeatTracker();

        /// <summary>Main Focus or Sidearm in hand; swapping takes 0.35 s (spec L106).</summary>
        public EquipmentSlot ActiveWeapon { get; internal set; } = EquipmentSlot.MainFocus;

        /// <summary>Tick at which an equip swap completes, or -1.</summary>
        public int SwapEndTick { get; internal set; } = -1;

        /// <summary>Tick at which a visible outside-map recall ends, or -1 (spec L175).</summary>
        public int RecallEndTick { get; internal set; } = -1;

        public int SelectedSlot { get; internal set; }
        public bool Surrendered { get; internal set; }

        /// <summary>The sanitized input of the current tick.</summary>
        public PlayerInput Input { get; internal set; }

        public InputButtons PreviousButtons { get; internal set; }
        public InputButtons Pressed { get; internal set; }

        /// <summary>The player's cast machine and its host when a spell runtime is attached (casting stage), else null.</summary>
        public Casting.PlayerCaster Caster { get; internal set; }

        public bool Alive => Resources.Alive;
        public bool IsSwapping => SwapEndTick >= 0;
        public bool IsRecalling => RecallEndTick >= 0;

        public Vec3 Position => Motor.Position;
        public Vec3 Eye => Motor.Eye;
        public Capsule HitVolume => Motor.Capsule;

        public MeleeProfile CurrentMeleeProfile => Setup.MeleeProfileFor(ActiveWeapon);

        public string CurrentMeleeSource => Setup.MeleeSourceFor(ActiveWeapon);

        public bool WasPressed(InputButtons button) => (Pressed & button) != 0;

        public bool IsHeld(InputButtons button) => (Input.Buttons & button) != 0;

        public void Hash(ref StateHasher h)
        {
            Motor.Hash(ref h);
            Resources.Hash(ref h);
            Stats.Hash(ref h);
            Shields.Hash(ref h);
            Healing.Hash(ref h);
            Guard.Hash(ref h);
            Melee.Hash(ref h);
            Statuses.Hash(ref h);
            Boundary.Hash(ref h);
            Feats.Hash(ref h);
            h.Add((int)ActiveWeapon);
            h.Add(SwapEndTick);
            h.Add(RecallEndTick);
            h.Add(SelectedSlot);
            h.Add(Surrendered);
            h.Add((uint)PreviousButtons);
        }
    }
}
