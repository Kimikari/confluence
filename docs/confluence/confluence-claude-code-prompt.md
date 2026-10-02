Build Confluence, the complete Windows PC 3D first-person fantasy wizard dueling game specified in these local files:

- docs/confluence/confluence-design.md — game rules, behavior, architecture requirements, and acceptance criteria.
- docs/confluence/confluence-catalogue.json — structured catalogue, stable IDs, spell ownership, equipment, augments, prerequisites, and arena content.
- docs/confluence/confluence-design.html — optional searchable reading copy of the same design.

The complete design is the end-state target. Deliver it through successive playable, tested stages, preserving the full feature and content backlog: 36 wizards, 288 exclusive spells, 120 shared equipment definitions, 864 free augments, and all specified modes and arena content. This is a development project spanning multiple sessions. Start implementing during this session after the initial inspection and plan.

First inspect the repository, its instructions, installed engine, toolchain, and existing work. Continue the existing engine and conventions. If this is a new project, select an appropriate native 3D engine for this specification, document the reasoning and version, and establish the project. Consult official documentation for the selected version when needed.

Manage the large specification deliberately. Read the core rules first, index all chapters and catalogue IDs, and retrieve relevant sections and records in bounded chunks for each task. Use scripts to query the JSON instead of dumping it into context. Preserve the supplied specification files. Record ambiguities and decisions; use these explicit requirements first, then the design's global rules, then individual catalogue definitions. Ask only about decisions that materially affect the product and cannot be resolved from the specification.

Required gameplay invariants:
1. Combat is first-person, with meaningful melee and ranged play, distinct wizard casting grammars, movement, defense, and execution skill. Voice casting has the specified equivalent accessible input alternatives.
2. Every unique spell belongs exclusively to its assigned wizard. All equipment is shared across wizards and purchased with match currency.
3. All ability upgrades and transformations are free augments. Each draft presents five eligible cards, each with one independent reroll, and the player selects one card total. Equipment shopping is a separate money-based system.
4. Implement the complete structured augment conditions, dependency chains, ranks, acquisition versus ongoing requirements, dormancy, rarity rules, and fallback behavior. The server validates offers, rerolls, purchases, and selections.
5. Environmental interactions change gameplay: for example, fire and water create steam that actually obstructs sight. Implement the bounded material/reaction rules and safety caps defined in the design.
6. Each round uses a new procedural arena seed and variable boundary size. All simultaneous fighting pairs receive matching initial arena geometry, with fair routes for both close and long-range combat.

Create a maintainable, data-driven gameplay architecture with stable IDs and typed executable effects. Convert descriptive catalogue behaviors into functioning triggers, targeting, costs, timing, modifiers, and state transitions. A record counts as implemented only when its specific behavior works in gameplay and has appropriate validation. Listing a record in a menu, an empty handler, or a generic substitute does not count. Share implementation primitives while preserving each wizard's grammar and each content entry's specified differences.

Build an authoritative multiplayer server from the foundation, including replicated combat and world state, input validation, the specified prediction/reconciliation, and round progression. Keep client-only presentation separate from authoritative outcomes.

Maintain concise CLAUDE.md instructions plus docs/IMPLEMENTATION_PLAN.md, docs/PROGRESS.md, docs/DECISIONS.md, and a content coverage ledger keyed by catalogue ID. Track unstarted, implemented, and verified content separately, along with missing assets, known issues, and next steps. Temporary art and audio are acceptable during early stages when clearly tracked; final presentation and polish remain in the completion criteria.

For the first playable milestone, implement two contrasting wizards with distinct casting inputs and close/ranged capabilities, first-person movement and defense, real damage and resources, a fire-water-steam interaction, procedural arena generation, and a complete shop → five-card augment draft → networked duel → reward → next-round loop. Use actual compatible catalogue entries and record exactly which ones are supported. Keep this milestone in the same architecture that will support the complete game.

Build and run the project. Validate with an authoritative server and two real client instances when the toolchain is available, then add focused tests for reaction rules, content ownership, purchases, augment eligibility/rerolls/dormancy, round transitions, and procedural arena fairness. Fix failures before expanding the next stage. Treat numerical balance values as initial tuning targets requiring playtesting.

Continue through the implementation plan while this session permits. At the end, update the durable progress files and report what runs, exact launch instructions, tests actually executed, remaining work, and the next concrete task. If a dependency blocks execution, identify the exact missing tool or action and continue independent work. Never report unexecuted tests, temporary assets, or unimplemented catalogue content as finished.

Begin now by inspecting the project and specification, writing the staged implementation plan, and implementing the first foundation tasks.
