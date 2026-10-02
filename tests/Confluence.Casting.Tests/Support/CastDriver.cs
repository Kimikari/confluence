using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;

namespace Confluence.Casting.Tests.Support
{
    /// <summary>Feeds held-button frames to a <see cref="CastMachine"/> one tick at a time and records every event.</summary>
    internal sealed class CastDriver
    {
        private readonly List<CastEvent> _scratch = new List<CastEvent>();
        private InputButtons _previous;

        public CastDriver(IGrammar grammar, params CastableSpell[] spells)
            : this(grammar, null, spells, 1)
        {
        }

        private CastDriver(IGrammar grammar, CastableSpell signature, CastableSpell[] spells, int player)
        {
            Host = new FakeCastHost();
            Machine = new CastMachine(player, Host, grammar, spells, signature);
        }

        public static CastDriver WithSignature(IGrammar grammar, CastableSpell signature, params CastableSpell[] spells) =>
            new CastDriver(grammar, signature, spells, 1);

        public FakeCastHost Host { get; }
        public CastMachine Machine { get; }
        public List<CastEvent> Events { get; } = new List<CastEvent>();

        /// <summary>The tick the next <see cref="Step"/> will run.</summary>
        public int Tick { get; private set; }

        /// <summary>Events emitted by the most recent step.</summary>
        public IReadOnlyList<CastEvent> Last => _scratch;

        public CastDriver Step(InputButtons held, int select = CastInputFrame.NoSelection, IReadOnlyList<CombatEvent> combat = null)
        {
            var frame = CastInputFrame.FromHeld(Tick, _previous, held);
            frame.SelectSlot = select;
            frame.CombatEvents = combat;
            return Step(frame);
        }

        public CastDriver Step(CastInputFrame frame)
        {
            frame.Tick = Tick;
            Host.CurrentTick = Tick;
            _scratch.Clear();
            Machine.Step(frame, _scratch);
            Events.AddRange(_scratch);
            _previous = frame.Held;
            Tick++;
            return this;
        }

        /// <summary>A press and release inside one tick.</summary>
        public CastDriver Tap(InputButtons buttons, InputButtons alsoHeld = InputButtons.None)
        {
            var frame = CastInputFrame.FromHeld(Tick, _previous, alsoHeld);
            frame.Pressed |= buttons;
            frame.Released |= buttons;
            frame.SelectSlot = CastInputFrame.NoSelection;
            return Step(frame);
        }

        public CastDriver Hold(InputButtons held, int ticks)
        {
            for (int i = 0; i < ticks; i++) Step(held);
            return this;
        }

        public CastDriver Idle(int ticks) => Hold(InputButtons.None, ticks);

        /// <summary>Steps with nothing held until <paramref name="kind"/> appears (at most <paramref name="limit"/> ticks).</summary>
        public CastDriver IdleUntil(CastEventKind kind, int limit = 600, InputButtons held = InputButtons.None)
        {
            for (int i = 0; i < limit; i++)
            {
                Step(held);
                if (_scratch.Any(e => e.Kind == kind)) return this;
            }

            throw new Xunit.Sdk.XunitException("No " + kind + " within " + limit + " ticks.");
        }

        public IEnumerable<CastEvent> OfKind(CastEventKind kind) => Events.Where(e => e.Kind == kind);

        public CastEvent Single(CastEventKind kind) => Events.Single(e => e.Kind == kind);

        public bool Has(CastEventKind kind) => Events.Any(e => e.Kind == kind);

        public int TickOf(CastEventKind kind) => Single(kind).Tick;

        /// <summary>Starts a fresh duel clock region: clears recorded events (machine state is kept).</summary>
        public void ClearEvents() => Events.Clear();
    }

    internal static class TestSpells
    {
        /// <summary>A synthetic quick projectile: 20 mana, 2 s cooldown, 0.25 s windup.</summary>
        public static CastableSpell Quick(string id = "TEST-QUICK", float mana = 20f, int windupTicks = 15, bool damaging = true) =>
            new CastableSpell(id, mana, 120, windupTicks, CastWeight.Quick, damaging, isProjectile: true);

        public static CastableSpell Heavy(string id = "TEST-HEAVY", float mana = 30f, int windupTicks = 36) =>
            new CastableSpell(id, mana, 300, windupTicks, CastWeight.Heavy, true, isProjectile: true);
    }
}
