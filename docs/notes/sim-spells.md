# Sim casting and spell runtime — design notes

Owner paths: `src/Confluence.Sim/Casting/**`, `src/Confluence.Sim/Spells/**`, `src/Confluence.Sim/Content/**`,
`src/Confluence.Sim/Input/**`, `src/Confluence.Casting/**`, tests under `tests/Confluence.Casting.Tests/**` and
`tests/Confluence.Sim.Tests/{Casting,Spells,Content}/**`.

## Status / next steps
- In progress (resumed after an interruption). Done and compiling: unified `InputButtons` (Confluence.Casting),
  `PlayerCaster` (ICastHost), `CombatEventFeed`, `CastFrameBuilder`, `CastDuelEvent`, `SpellRuntime`, spell forms,
  `SpellContext` payload application, all delivery primitives, content registry.
- Next: SPL-009 / SPL-010 behaviors, tests for every component, determinism hash test, final notes.
