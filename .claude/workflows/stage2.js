export const meta = {
  name: 'stage2-m1-content-and-protocol',
  description: 'M1 content (Bram and Halen kits, shared augments, equipment, props and biomes) plus the network game protocol; each branch adversarially verified and fixed, then merged and integrated',
  phases: [
    { title: 'Implement', detail: 'seven agents in isolated worktrees' },
    { title: 'Verify', detail: 'adversarial check of each branch against catalogue text' },
    { title: 'Fix', detail: 'apply verified findings in place' },
    { title: 'Merge', detail: 'merge, integration (standard match over UDP), docs, ledger' },
  ],
}

const REPO = 'C:\\Users\\Kimi\\Desktop\\Confluence'
const UNITY = 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.6.4f1\\Editor\\Unity.exe'
// Resume controls:
//  args.inPlace  { label: worktreePath }           implement continues inside an existing worktree
//  args.impl     { label: { branch, worktree, commit } }  implement already finished: skip to verify
//  args.fixed    { label: { branch, worktree, commit } }  verify+fix already finished: skip to merge
//  args.only     [labels]                          restrict which items run (others must be in args.fixed for merge)
const A = args || {}
const IN_PLACE = A.inPlace || {}
const IMPL = A.impl || {}
const FIXED = A.fixed || {}
const ONLY = A.only || null

const COMMON = `
You are working on "Confluence", a Windows PC first-person wizard dueling game. Git repo: ${REPO}.
Stages 0, 1 and 1b are merged on master: Core, Reactions, Casting, Arena, Economy, Match, Sim (spell runtime, reactions, props, stats, effect framework, content registry), Hosting (MatchHost, adapters, bots), Net foundation, Unity client (Laboratory, M1 screens). 1848 tests pass. Read first: CLAUDE.md, docs/ARCHITECTURE.md, docs/PROGRESS.md (Known issues, Next task), and the notes for every area you touch or call: docs/notes/sim-spells.md, sim-world.md, sim-core.md, casting.md, reactions.md, hosting.md, economy.md (and net.md / match.md / arena.md / client.md when relevant). Search docs/DECISIONS.md when you hit an interpretation question; follow existing decisions.
Spec: docs/confluence/confluence-design.md and docs/confluence/confluence-catalogue.json — NEVER read whole, NEVER modify. Query: node tools/spec.mjs toc | show <heading or ID> | lines <a> <b> | grep <re>; node tools/cat.mjs get <ID...> | kit <WIZ> | list <type> field=value --fields a,b | grep <re> [type] | refs <ID>. Core rules: spec lines 1-717; cite spec lines in comments.
Constraints:
- src/ libraries: netstandard2.1, C# 9 (Unity loads them): block-scoped namespaces, explicit usings, no records/init/record structs, no System.Text.Json, no UnityEngine in src/. Nullable disabled. Deterministic: only Core Rng streams, integer ticks, stable iteration order.
- Global caps from Confluence.Core.Rules.GlobalRules; local content may only be stricter. Precedence: build requirements > global rules > catalogue record.
- CONTENT RULE (most important): a catalogue record counts as implemented only when its SPECIFIC behavior works in real gameplay (DuelWorld/MatchHost) and is validated. Put [ContentBehavior("ID")] on the implementing class only then; give it [Verifies("ID")] tests that exercise rank 1 AND rank 2 (augments), the drawback, the caps it touches, and its counterplay where the spec requires a counterplay scenario (L447: every hard-control, concealment, summon, movement, high-damage and multi-item transformation). Use the record's numeric JSON fields; write prose-only parameters with the quoted source phrase and a test that the number appears in the record text (D-011). No stubs, generic substitutes, or menu-only entries. If something cannot be completed, leave the record unmarked and list it in gaps.
- Reuse the framework (SpellFormBuilder/deliveries/payloads/ISpellTransform, effect framework triggers/actions/stacking/proc budget, StatBlock caps, ContentRegistry). When a generic primitive is missing, add it in a NEW, clearly named file inside the framework folder (other content agents work concurrently; keep edits to existing shared framework files minimal and additive, and list them in crossEdits).
- Transformations that change structural tags (e.g. projectile -> trap/field, stationary -> mobile) register Core StructuralDeltas the way the framework does (see docs/notes/sim-spells.md); exclusive groups are enforced.
- RESILIENCE: usage limits can interrupt you at any time. Commit WIP early and often on your branch ('WIP:' prefix), at least after each record or small group passes its tests. Keep docs/notes/<your-area>.md current with 'Status / next steps' and the list of finished IDs.
- Do not edit docs/DECISIONS.md or docs/PROGRESS.md; put interpretations in your report and notes.
- Finish: dotnet build Confluence.sln 0 errors; your tests and the projects you touched pass; final commit (message ends with: Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>). Return the structured report, including your worktree path (git rev-parse --show-toplevel).
`
const REPORT = {
  type: 'object',
  properties: {
    branch: { type: 'string' }, worktree: { type: 'string' }, commit: { type: 'string' },
    api: { type: 'string' }, tests: { type: 'string' },
    decisions: { type: 'array', items: { type: 'string' } },
    contentImplemented: { type: 'array', items: { type: 'string' }, description: 'IDs now carrying [ContentBehavior]' },
    crossEdits: { type: 'array', items: { type: 'string' } },
    gaps: { type: 'array', items: { type: 'string' } },
  },
  required: ['branch', 'worktree', 'commit', 'tests', 'decisions', 'contentImplemented', 'gaps'],
}

const KIT = (wiz, name, spells, augs, extra) => `${COMMON}
TASK ${wiz.toLowerCase()}-kit: complete ${wiz} ${name}'s full kit. You own src/Confluence.Sim/Content/Wizards/${wiz}/** (create; move or reuse existing ${wiz} spell behaviors here only if trivial — otherwise leave existing files where they are and add new ones), tests/Confluence.Sim.Tests/Content/${wiz}/**, src/Confluence.Hosting/Bots/Profiles/${wiz}* (a bot casting profile), docs/notes/kit-${wiz.toLowerCase()}.md.
Start with: node tools/cat.mjs kit ${wiz} (wizard record, its 8 spells, its 16 transformations), spec 'Spell transformation resolution' (504-517), 'Exclusive spell rules and transformation profiles' (519-553), §5 'Spell transformations and compatibility' (282-289), 'Telegraph and counterplay' (547-551), and the grammar notes (docs/notes/casting.md).
Deliver:
1. Every one of ${wiz}'s spells (${spells}) fully implemented per its record (damage expression, channel, mana, cooldown, cast_time, range, effect text, world_interaction via Reactions, counterplay-relevant telegraphs), cast through ${wiz}'s real grammar in tests. ${extra}
2. The signature technique and the passive exactly per the wizard record (cooldown, mana, windup, geometry, caps, field/summon allocations), attached through the effect framework.
3. All 16 transformations (${augs}): rank 1 and rank 2 behaviors, drawbacks, exclusive groups, structural deltas, recasts, counterplay; each a ISpellTransform (or equivalent) with [ContentBehavior] and [Verifies] tests. Verify Economy sees each transformation's structural profile correctly (a test through Confluence.Economy's eligibility/structural profile with the registry populated).
4. Mark the wizard ${wiz} itself [ContentBehavior] only when its grammar, signature, passive and all 8 spells work in gameplay; add an end-to-end [Verifies("${wiz}")] duel test (two bots or scripted inputs in DuelWorld/MatchHost).
5. Bot profile: per-spell input scripts that cast each spell through the grammar (normal casts reliably, mastery attempts sometimes per competence), sensible spell choice by range and situation (e.g. use steam/water/heat combos where the kit supports them), using only legal information (sight queries; no reading hidden opponent inputs).`

const BRAM = KIT('WIZ-002', 'Bram Cinder (Heat, pressure release)', 'SPL-009 and SPL-010 already exist and are verified; implement SPL-011 Heat Haze, SPL-012 Ash Curtain, SPL-013 Phoenix Rush, SPL-014 Boiler Orb, SPL-015 Flame Lash, SPL-016 Flashforge and re-check 009/010 against the merged framework', 'AUG-305..AUG-320', 'Boiler Orb needs a deployable with recast detonation and its water-reaction reduction; Ash Curtain is a concealment volume that wind displaces and water collapses; Phoenix Rush leaves a Heat 1 damaging trail field; Flashforge makes intact metal props red-hot.')
const HALEN = KIT('WIZ-010', 'Halen Tide (Water, pressure metering)', 'SPL-073 Pressure Jet, SPL-074 Water Hammer, SPL-075 Bubble Buckler, SPL-076 Water Pool, SPL-077 Cascade Vault, SPL-078 Riptide Lane, SPL-079 Deepglass Prison, SPL-080 Seal the Spring', 'AUG-433..AUG-448', 'Water Pool must create a connected shallow pool field that Heat turns into steam, Frost freezes and electricity stays within; Bubble Buckler is a directional 45-point shield where heat consumes 10 shield into a 1 m steam puff; Deepglass Prison roots within the global hard-control rules and immunity; Riptide Lane gives +15% only when moving with its arrow (movement cap 20%).')

const AUG_FAMILIES = (label, fams) => `${COMMON}
TASK ${label}: shared (non-transformation) augments of these families: ${fams}. You own src/Confluence.Sim/Content/Augments/${label}/** and tests/Confluence.Sim.Tests/Content/Augments/${label}/**, docs/notes/${label}.md. Another agent handles the other six families concurrently.
List your records: node -e script over docs/confluence/confluence-catalogue.json filtering augments with empty target_spell_ids and family in your set; print id, rarity, eligibility, condition, rank1, rank2, drawback (compactly). Read spec 'Free augments and eligibility specification' (592-653), §5 augment schedule and eligibility (214-249), 'Global modifier and trigger rules' (122-129).
Priority order: (1) EVERY unconditional augment ('always' eligibility) in your families — these keep the design floor of ten legal alternatives per rarity (spec L233, L596) for every wizard; (2) conditional augments whose predicates can be satisfied by WIZ-002 Bram Cinder or WIZ-010 Halen Tide loadouts (Heat/Water kits, projectile/field/melee/cone/shield/movement tags, common equipment) — these make M1 drafts richer; (3) any others that are cheap with the framework. Each needs [ContentBehavior] + [Verifies] tests for rank 1, rank 2 (replaces rank 1 per its text), drawback, caps (cost floor, refund cap, proc budget, mastery clamps, movement/CDR/shield/heal caps), and the trigger condition's edge cases. Information/deception augments emit owner-only cue events (direction-only, no through-wall data, server-side visibility rules) — implement those as Sim events with the stated timing and visibility, and test the information limits. Augments that reference summons/cover/props work with the generic summon/prop/cover systems; test with real or synthetic summons/props.
End with a test that, with your records plus the existing ones registered, reports the per-rarity count of legal implemented augments for WIZ-002 and WIZ-010 default loadouts (through Economy's floor API); report the numbers.`

const AUG_A = AUG_FAMILIES('augments-a', "'Casting mastery', 'Projectile and beam shaping', 'Fieldcraft and terrain', 'Melee and guard', 'Movement arts', 'World reactions'")
const AUG_B = AUG_FAMILIES('augments-b', "'Resource and recovery', 'Defense and commitment', 'Information and deception', 'Summon husbandry', 'Build covenants', 'Combo architectures'")

const EQUIP = `${COMMON}
TASK equipment: shared equipment for M1 shops. You own src/Confluence.Sim/Content/Equipment/** and tests/Confluence.Sim.Tests/Content/Equipment/**, docs/notes/equipment.md (the three proof items EQP-051/066/096 already exist; re-check them).
Read spec 'Shared equipment rules' (554-591), §5 currency/shop/slots (191-213), §3 melee profiles (98-107), and the records: node tools/cat.mjs list equipment tier=1 ; list equipment tier=2.
Implement ALL tier 1 (EQP ids with tier 1: wands, staffs, weapons with their melee profile changes, armor, clothing, boots, trinkets, consumables EQP-111..113) and as many tier 2 as possible (target: all 46; actives use the single enabled active-item key with their mana/stamina/cooldown/charges; consumables spend one charge on commitment with their use times; trajectory-mode items like EQP-007 Forked Hazel respect the one-trajectory-mode rule and wizard-transformation priority, L545/L581). Unique groups, slot rules and dormant actives come from Economy; your behaviors attach effects through IEquipmentBehavior/IEffectHost with StatBlock caps (resistances 35%, passive 45%, movement +20%, regen cap, shield cap, healing budgets). Each item: [ContentBehavior] + [Verifies] tests for the effect, the drawback, caps, and activation/charges where relevant, in a real DuelWorld via Hosting's LoadoutMapper where practical.
End with a test reporting the implemented count per tier and that a round-1 Conclave shop (600 Crowns) can show three distinct offers plus three distinct rerolls from implemented items without Laboratory overrides.`

const PROPS = `${COMMON}
TASK props-biomes: interactive props and biome rules used by M1 arenas. You own src/Confluence.Sim/Content/Props/**, src/Confluence.Sim/Content/Biomes/**, tests/Confluence.Sim.Tests/Content/{Props,Biomes}/**, docs/notes/props-biomes.md; small additive Reactions API additions are allowed (list them).
Read spec §4 (130-180), 'Arena materials and generation catalogue' (654-688), prop and biome records (node tools/cat.mjs get PROP-007 ...; list biomes --fields id,name,materials,environment_rule), docs/notes/reactions.md (what is not yet implemented: porous walls, heat sinks, basins, dust urns, acoustic plates), docs/notes/sim-world.md (prop substrate) and docs/notes/arena.md (where biome features and props are placed).
Implement as IPropBehavior with [ContentBehavior] + [Verifies] (behavior in a real DuelWorld with Reactions: units, timings, damage, caps, one-use limits, mass/velocity rules): at least PROP-007 Water Urn, PROP-008 Porous Block, PROP-009 Sealed Basin, PROP-010 Ice Cask, PROP-011 Rain Catcher, PROP-012 Drain Grate, PROP-013 Briar Bundle, PROP-014 Canvas Screen, PROP-015 Resin Pot, PROP-016 Timber Stack, PROP-017 Dry Leaf Bed, PROP-025 Rolling Boulder, PROP-027 Glass Jug, PROP-029 Sandbag, PROP-037 Brittle Crystal, PROP-047 Insulation Tile; then more if cheap (conductive/optical/acoustic ones). Biome rules as IBiomeRuleBehavior with end-to-end duel tests (BIO-001 and BIO-011 already exist; re-check them): at least BIO-002 Ember Orchard, BIO-004 Rime Foundry, BIO-007 Silt Basin, BIO-013 Briar Greenhouse, BIO-015 Ash Amphitheatre, BIO-019 Blackwater Dock, BIO-020 Sunforge Gallery, BIO-024 Wellspring Forum; then more if cheap. Ensure the arena generator, given the registry's availability, produces valid arenas with only implemented props/biomes (>= its MinPropDefinitions) for all four sizes (test a batch through Hosting's arena provider), and report fallback rates.`

const NET = `${COMMON}
TASK net-protocol (Stage 3): the real network game. You own src/Confluence.Net/**, src/Confluence.Server/**, src/Confluence.HeadlessClient/**, tests/Confluence.Net.Tests/**, docs/notes/net.md; small additive hooks in Hosting/Sim for snapshot extraction are allowed (list them).
Read spec §14 (417-436), §2 (30-73), §8 HUD/shop (345-355), §11 spectator rules (391), docs/notes/net.md, hosting.md, sim-core.md, sim-spells.md, sim-world.md, reactions.md (snapshot gaps), client.md.
Build:
1. Game protocol on the Net foundation: lobby join (name, wizard choice limited to wizards whose kits are implemented, input alternative), match settings and fingerprint, phase/timer messages, round reveal (opponent, arena preview metadata, Resolve, opponent's completed previous-round loadout; never live purchases), private shop/draft state and command requests/responses (idempotent request ids, rate limits) mapped to Hosting's command API, ready, surrender, results, match end. Per-tick input commands (PlayerInput with the unified Casting.InputButtons, grammar events like syllable/gesture data, redundancy) and server snapshots at 20-30 Hz (players' motor/resources/shields/statuses/cast and grammar public state/cooldowns, spell entities, fields, reaction volumes and changed cells (delta), props, boundary, timer) plus reliable duel events (casts, telegraphs, damage confirmations only for real hits, reactions, deaths). Interest management: a client receives only its own duel; hidden information never leaves the server (opponent's private grammar UI, shop/draft, unseen positions beyond what the rules allow is a later concern — at least never send other pairs' duels to live players; eliminated players get delayed spectator data per L391).
2. Server (src/Confluence.Server): console host running MatchHost at 60 ticks/s with real-time pacing (and an explicit, labeled accelerated mode for automated tests), CLI args (port, mode Conclave/Duel, player count, bots to fill, seed, Laboratory overrides flag), logs, reconnect within 90 s, clean shutdown.
3. Client session library (in Confluence.Net, usable by Unity and the headless client): connection, clock sync, input sending, snapshot buffering and interpolation for remote entities (~100 ms), local prediction of the own player's movement with Sim.CharacterMotor and reconciliation (replay unacknowledged inputs after each snapshot; smooth corrections), local grammar preparation UI prediction with authoritative commit/outcomes, view-model state for shop/draft/HUD/results the Unity UI can bind to.
4. Headless client (src/Confluence.HeadlessClient): CLI that connects, picks a wizard, shops/drafts with simple heuristics, and plays duels with a bot driving inputs from its replicated view (reuse Hosting bot logic over the client's view where possible).
5. Lag compensation per L423 for hitscan/melee validation (rewind <= 150 ms of player hit volumes only).
Tests: protocol round-trips and malformed-message rejection; prediction/reconciliation converges under simulated latency/loss; interest management (no other pairs' data, no private shop data of others); idempotent shop commands over the wire; and an automated end-to-end test: a real server process (or in-process server on UDP) plus TWO real headless client instances complete a Duel (or short Conclave) through shop -> draft -> duel -> reward -> next round using accelerated time and Laboratory overrides if pools are still short (state which). Also run, once, a real-time smoke: server + two headless client processes over UDP for at least one full round, and report the exact commands and outcome.`

const ITEMS = [
  ['bram-kit', BRAM], ['halen-kit', HALEN], ['augments-a', AUG_A], ['augments-b', AUG_B],
  ['equipment', EQUIP], ['props-biomes', PROPS], ['net-protocol', NET],
]
const inPlaceNote = (label) => IN_PLACE[label] ? `
CONTINUING AN INTERRUPTED ATTEMPT IN PLACE: your worktree already exists at ${IN_PLACE[label]} (NOT your shell's starting directory). Run EVERY command from there (start each Bash command with: cd "${IN_PLACE[label]}" && ...) and edit only under that path. It holds your previous attempt's WIP commits (the last may be a broken salvage). Do NOT start over: read your docs/notes file there, 'git log --stat master..HEAD', and the code; get it compiling; finish the remaining work. Never touch the main checkout at ${REPO}.
` : ''

const FINDINGS = {
  type: 'object',
  properties: {
    findings: { type: 'array', items: { type: 'object', properties: {
      id: { type: 'string', description: 'catalogue ID or area' }, severity: { type: 'string', enum: ['high', 'medium', 'low'] },
      problem: { type: 'string' }, evidence: { type: 'string' }, fix: { type: 'string' } },
      required: ['id', 'severity', 'problem', 'evidence', 'fix'] } },
    checkedIds: { type: 'array', items: { type: 'string' } },
  },
  required: ['findings', 'checkedIds'],
}

const results = await pipeline(
  ITEMS.filter(([label]) => (!ONLY || ONLY.includes(label)) && !FIXED[label]),
  // 1. implement
  async ([label, prompt]) => {
    if (IMPL[label]) return { label, ...IMPL[label], reused: true }
    const r = await agent(inPlaceNote(label) + prompt,
      IN_PLACE[label] ? { label: `impl:${label}`, phase: 'Implement', schema: REPORT }
                      : { label: `impl:${label}`, phase: 'Implement', isolation: 'worktree', schema: REPORT })
    if (!r) throw new Error(`implement failed: ${label}`)
    return { label, ...r }
  },
  // 2. verify (read-only, adversarial)
  async (impl) => {
    const v = await agent(`${COMMON}
YOU ARE AN ADVERSARIAL VERIFIER. Do not modify files. Work read-only in the worktree at ${impl.worktree} (cd there for every command).
Branch ${impl.branch} (area ${impl.label}) claims these records are implemented: ${JSON.stringify(impl.contentImplemented || [])}.
For EVERY claimed ID (if more than 40, check all high-risk ones — transformations, actives, information/deception, multi-step triggers — and a random-looking spread of the rest, at least 40): read the record (node tools/cat.mjs get ID) and the implementing code and its tests. Look for: wrong or missing numbers (rank 1 vs rank 2, drawbacks, durations, radii, costs), behavior that silently differs from the text, global caps bypassed (cost floor, refunds, proc budget, secondary-damage rules, mastery clamps, shield/heal/movement/CDR caps, hard-control immunity), structural deltas wrong or missing, exclusive groups not enforced, tests that only construct objects or assert tautologies instead of exercising gameplay, missing counterplay scenario where required (L447), nondeterminism, and anything a [ContentBehavior] claim overstates. For the net-protocol area instead check: hidden-information leaks (other pairs' duels, private shop/draft data, opponent private grammar UI), server trusting client claims (damage, hits, combat events, resources), missing validation/rate limits, prediction/reconciliation correctness, and whether the claimed end-to-end tests really run two client instances over UDP. Run the relevant tests (dotnet test with --filter where useful) to confirm they pass and to probe suspected bugs (you may write throwaway tests in a temp folder outside the repo).
Report only real, evidenced defects (spec line / record text / code location / failing probe).`,
      { label: `verify:${impl.label}`, phase: 'Verify', schema: FINDINGS })
    return { impl, verify: v || { findings: [], checkedIds: [], error: 'verifier failed' } }
  },
  // 3. fix in place
  async ({ impl, verify }) => {
    if (!verify.findings || verify.findings.length === 0) return { label: impl.label, branch: impl.branch, worktree: impl.worktree, commit: impl.commit, impl, verify, fix: 'no findings' }
    const f = await agent(`${COMMON}
WORK IN PLACE in the worktree at ${impl.worktree} (cd there for every command; never touch the main checkout). Branch ${impl.branch}, area ${impl.label}.
An adversarial verifier reported these findings. For each: confirm it (verifiers can be wrong), then fix it with tests, or — if the behavior cannot be completed now — remove that record's [ContentBehavior] claim and record it in gaps. Keep everything building and passing. Commit (WIP commits during long work; final message ends with: Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>).
FINDINGS: ${JSON.stringify(verify.findings, null, 1)}
Return the structured report (final state of the branch: contentImplemented must list only records that still carry [ContentBehavior]).`,
      { label: `fix:${impl.label}`, phase: 'Fix', schema: REPORT })
    if (!f) throw new Error(`fix failed: ${impl.label}`)
    return { label: impl.label, branch: f.branch || impl.branch, worktree: impl.worktree, commit: f.commit, impl, verify, fix: f }
  },
)

const finished = results.filter(Boolean)
const all = Object.entries(FIXED).map(([label, v]) => ({ label, ...v })).concat(finished)
const missing = ITEMS.map(i => i[0]).filter(l => !all.find(x => x.label === l))
if (missing.length) {
  log(`NOT FINISHED: ${missing.join(', ')} — merge skipped; resume with args.inPlace/impl/fixed`)
  return { finished: all.map(x => ({ label: x.label, branch: x.branch, worktree: x.worktree, commit: x.commit })), missing }
}

phase('Merge')
const summary = all.map(x => ({
  label: x.label, branch: x.branch, worktree: x.worktree, commit: x.commit,
  contentImplemented: (x.fix && x.fix.contentImplemented) || (x.impl && x.impl.contentImplemented) || x.contentImplemented || [],
  decisions: [].concat((x.impl && x.impl.decisions) || [], (x.fix && x.fix.decisions) || [], x.decisions || []),
  gaps: (x.fix && x.fix.gaps) || (x.impl && x.impl.gaps) || x.gaps || [],
  crossEdits: [].concat((x.impl && x.impl.crossEdits) || [], (x.fix && x.fix.crossEdits) || []),
  verifierFindings: x.verify ? x.verify.findings.length : 'n/a',
}))
const merge = await agent(`${COMMON}
YOU WORK DIRECTLY IN THE MAIN CHECKOUT at ${REPO} on branch master (not a worktree).
TASK merge-and-integrate Stage 2 + 3.
1. Merge with --no-ff in order: equipment, augments-a, augments-b, props-biomes, bram-kit, halen-kit, net-protocol. Resolve conflicts in shared framework files so every branch's behavior survives (several agents added generic primitives concurrently: dedupe equivalent ones into one implementation and update callers).
2. Integration (write these as tests; fix what they expose):
 - Content floors: with the merged registry, Economy's floor check passes WITHOUT Laboratory overrides for WIZ-002 and WIZ-010 at default loadouts in every rarity (>= 10 legal implemented alternatives), and round-1 shops fill with implemented items. Lobbies offer only wizards whose kits are [ContentBehavior]-implemented.
 - A standard (non-Laboratory) Duel match WIZ-002 vs WIZ-010 bots through MatchHost completes all rounds with real shops, drafts on the scheduled rounds, transformations and shared augments drafted and active in duels, dormancy applied at finalization, reactions (steam from Fireball into Water Pool) occurring in at least one duel.
 - Server + two headless clients over UDP complete at least one full round loop (shop -> draft -> duel -> reward -> next round) with standard rules where pools allow (state precisely what overrides, if any, remain).
 - The ledger counts match the [ContentBehavior] claims.
3. dotnet build Confluence.sln 0 errors; dotnet test Confluence.sln all pass (run Arena slow batches once at the end). Sync and rebuild the Unity client (powershell -ExecutionPolicy Bypass -File tools/build-client.ps1 -Tests -Smoke) and check screenshots; fix client breakage from API changes.
4. Docs: append all decisions to docs/DECISIONS.md (continue after the highest D-number), update docs/PROGRESS.md (Stage 2/3 status, exact test counts, ledger summary, known issues incl. remaining gaps, temporary assets, next concrete task = Stage 4: Unity client networked M1 — connect the UI and Laboratory to the Net client session, predicted first-person play, spell/field/steam presentation, then validation with server + two real Unity client instances), docs/ARCHITECTURE.md where changed, regenerate the ledger (powershell -ExecutionPolicy Bypass -File tools/ledger.ps1).
5. Commit (WIP during long work; final message ends with: Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>), then remove the merged worktrees (git worktree remove --force <path>) and delete their branches.
BRANCHES AND REPORTS: ${JSON.stringify(summary, null, 1)}
Return: merge outcome, conflicts resolved, integration results, exact test counts per project, ledger summary, remaining issues.`, { label: 'merge', phase: 'Merge' })
return { branches: summary, merge }
