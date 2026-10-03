using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Casting
{
    /// <summary>
    /// The cast frame each tick: the shared input flags, edges, selection, aim, body state, and the combat events the
    /// server generates for event-driven grammars (spec L472, L482). Clients send no combat events at all.
    /// </summary>
    public class CastFrameAndCombatEventTests
    {
        private static (DuelWorld world, TapGrammar grammar) World()
        {
            var grammar = new TapGrammar(1000);
            SpellFormBuilder b = SpellTestKit.Form();
            b.Delivery = new ProjectileDelivery { Speed = 30f, MaxRange = 30f };
            b.Payload.Direct = 5f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(grammar, b.Build()));
            return (w, grammar);
        }

        private static List<CombatEvent> Seen(TapGrammar g) =>
            g.Frames.Where(f => f.CombatEvents != null).SelectMany(f => f.CombatEvents).ToList();

        [Fact]
        public void Movement_Axes_Become_Movement_Key_Bits_And_Taps_Count_As_Press_And_Release()
        {
            var (w, g) = World();
            PlayerInput input = TestWorlds.A.Moving(-1f, 1f);
            input.Tapped = InputButtons.CastModifier;
            w.Step(input, TestWorlds.B);
            CastInputFrame f = g.Frames.Last();
            Assert.True(f.IsHeld(InputButtons.MoveForward | InputButtons.MoveLeft));
            Assert.False(f.IsHeld(InputButtons.MoveRight));
            Assert.True(f.WasPressed(InputButtons.MoveForward));
            Assert.True(f.WasPressed(InputButtons.CastModifier) && f.WasReleased(InputButtons.CastModifier));
            Assert.False(f.IsHeld(InputButtons.CastModifier));
            Assert.Equal(w.Player(0).Position, f.Position);
            Assert.Equal(0f, f.AimYawDeg);

            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.True(g.Frames.Last().WasReleased(InputButtons.MoveForward));
        }

        [Fact]
        public void Damage_Taken_And_A_Perfect_Guard_Reach_The_Defenders_Next_Frame()
        {
            var (w, g) = World();
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 10f));
            w.Step(TestWorlds.A, TestWorlds.B);
            CombatEvent taken = Seen(g).Single(e => e.Kind == CombatEventKind.DamageTaken);
            Assert.Equal(10f, taken.Value);
            Assert.Equal(w.Tick - 1, taken.Tick); // emitted after the previous casting step

            w.Step(TestWorlds.A.With(InputButtons.Guard), TestWorlds.B);
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 10f));
            w.Step(TestWorlds.A.With(InputButtons.Guard), TestWorlds.B);
            Assert.Contains(Seen(g), e => e.Kind == CombatEventKind.PerfectGuard);
        }

        [Fact]
        public void Dodge_Completion_And_Landing_Are_Generated_From_Body_State()
        {
            var (w, g) = World();
            w.Step(TestWorlds.A.Moving(1f, 0f).With(InputButtons.Dodge), TestWorlds.B);
            SpellTestKit.Run(w, 20);
            Assert.Single(Seen(g), e => e.Kind == CombatEventKind.DodgeCompleted);

            w.Step(TestWorlds.A.With(InputButtons.Jump), TestWorlds.B);
            SpellTestKit.RunUntil(w, () => w.Player(0).Motor.Grounded, 120);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.Single(Seen(g), e => e.Kind == CombatEventKind.Landed);
        }

        [Fact]
        public void A_Common_Melee_Hit_Its_Windup_And_Recovery_Feed_The_Attackers_Grammar()
        {
            var (w, g) = World();
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -5f), 180f);
            w.Step(TestWorlds.A.With(InputButtons.Melee), TestWorlds.B);
            SpellTestKit.Run(w, 60);
            List<CombatEvent> seen = Seen(g);
            Assert.Single(seen, e => e.Kind == CombatEventKind.MeleeWindupCompleted);
            Assert.Single(seen, e => e.Kind == CombatEventKind.MeleeHit);
            Assert.Single(seen, e => e.Kind == CombatEventKind.MeleeRecoveryEnded);
            Assert.True(seen.FindIndex(e => e.Kind == CombatEventKind.MeleeWindupCompleted) < seen.FindIndex(e => e.Kind == CombatEventKind.MeleeRecoveryEnded));
        }

        [Fact]
        public void A_Guarded_Melee_Strike_Is_A_Melee_Parry_For_The_Defender()
        {
            var grammar = new TapGrammar(1000);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(), SpellTestKit.Loadout(grammar));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -5f), 180f);
            w.Step(TestWorlds.A.With(InputButtons.Melee), TestWorlds.B.With(InputButtons.Guard));
            SpellTestKit.Run(w, 30, TestWorlds.A, TestWorlds.B.With(InputButtons.Guard));
            Assert.Contains(Seen(grammar), e => e.Kind == CombatEventKind.MeleeParry);
        }

        [Fact]
        public void Cooldown_Ready_Names_The_Spell_And_Knockback_Is_Forced_Displacement()
        {
            var (w, g) = World();
            SpellTestKit.Cast(w);
            w.Step(TestWorlds.A.With(InputButtons.Dodge), TestWorlds.B); // cancel the long preparation
            w.Player(0).Caster.StartCooldown(SpellTestKit.Machine(w, 0).SpellInSlot(0));
            SpellTestKit.Run(w, 70);
            CombatEvent ready = Seen(g).Single(e => e.Kind == CombatEventKind.CooldownReady);
            Assert.Equal("SPL-T01", ready.SpellId);

            w.ApplyKnockback(0, 1, Vec3.Back, 2f, "SPL-PUSH");
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.Contains(Seen(g), e => e.Kind == CombatEventKind.ForcedDisplacement && e.Value == 2f);
        }

        [Fact]
        public void The_Grammar_Observes_Every_Tick_And_Combat_Events_Are_Delivered_Once()
        {
            var (w, g) = World();
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 5f));
            SpellTestKit.Run(w, 5);
            Assert.Equal(5, g.Frames.Count);
            Assert.Single(Seen(g), e => e.Kind == CombatEventKind.DamageTaken);
        }
    }
}
