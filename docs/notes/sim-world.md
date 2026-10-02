# Sim world, props, stats, effects (Confluence.Sim) — design notes

Stage 1b task "sim-world-effects". Owner paths: `src/Confluence.Sim/World/Materials/**`, `src/Confluence.Sim/Effects/**`,
`src/Confluence.Sim/Stats/**`, `src/Confluence.Sim/Props/**`, `src/Confluence.Reactions/**`,
`tests/Confluence.Sim.Tests/{World,Effects,Stats,Props}/**`, this file. The concurrent agent "sim-casting-spells" owns
`src/Confluence.Sim/{Casting,Spells,Content,Input}/**` and Confluence.Casting.

## Status / next steps
- Done (WIP commits): Reactions sticky slow + force clamp + gust accessor; StatBlock with raw/effective values and
  caps; motor, resources, guard, melee read the StatBlock.
- In progress: ReactionMaterials (IWorldMaterials over ReactionWorld), props, effect framework, proof content, tests.
</content>
</invoke>
