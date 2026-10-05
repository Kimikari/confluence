# Confluence — rules for every agent (Claude, Codex, Gemini)

Windows PC first-person wizard dueling game. The complete specification is in `docs/confluence/` and is read-only: never edit, reformat, or regenerate those files.

## Read first
- `docs/ARCHITECTURE.md` — layout, layering, tick order, content model, networking.
- `docs/IMPLEMENTATION_PLAN.md` — stages; `docs/PROGRESS.md` — current state and next task; `docs/DECISIONS.md` — interpretations.
- `docs/CONTENT_LEDGER.md` — per-ID status (generated; do not hand-edit statuses).
- `docs/notes/<area>.md` — design notes and status for each subsystem you touch or call.

## Reading the spec (1.5 MB markdown, 1.8 MB JSON — never load whole files)
- `node tools/spec.mjs toc` / `show <heading or ID>` / `lines <a> <b>` / `grep <regex>`
- `node tools/cat.mjs get <ID>`, `kit <WIZ-ID>`, `list <type> field=value ...`, `grep <regex> [type]`, `refs <ID>`, `stats`
- Precedence: build prompt requirements > global rules > catalogue entries. Search `docs/DECISIONS.md` for existing interpretations and follow them; cite spec lines in comments.

## Build and test
- `dotnet build Confluence.sln` and `dotnet test Confluence.sln` (or a single `tests/Confluence.X.Tests` project). Fast loop: `dotnet test Confluence.sln --filter "Category!=Slow"`.
- Script execution is disabled by default on this machine: run every `tools/*.ps1` as `powershell -ExecutionPolicy Bypass -File tools/<script>.ps1`.
- Ledger: `tools/ledger.ps1` (runs the full `dotnet test`, slow batches included, and regenerates the ledger).
- Libraries in `src/` (nine netstandard2.1 projects) use C# 9 because Unity loads the same DLLs. Server, HeadlessClient, the ledger tool and tests are net10.0.
- Unity client: `client/`, Unity 6000.6.4f1 (URP). `tools/sync-unity.ps1` syncs core DLLs; `tools/build-client.ps1 -Tests -Smoke` syncs, runs EditMode tests, builds the development player into `out/client/` and writes smoke views to `out/smoke/`. It needs the project closed in any open Editor.

## Who writes shared records
- The hub (UChicago Claude Code), in the main checkout on `master` only: `docs/DECISIONS.md`, `docs/PROGRESS.md`, `docs/CONTENT_LEDGER.md`, `docs/content-ledger.json`. At the end of every hub session: update PROGRESS, run `tools/ledger.ps1`, and `git push origin --all`.
- Everyone else (workflow agents, Codex, personal Claude; any worktree or branch): never edit those four files. Keep `docs/notes/<area>.md` current with "Status / next steps", the finished IDs, and an "Interpretations" list (the hub turns these into DECISIONS entries at merge).
- Only the hub merges to `master`.

## Worker constraints
- `src/` libraries: netstandard2.1, C# 9: block-scoped namespaces, explicit usings, no records/init/record structs, no file-scoped namespaces or global usings, no System.Text.Json, no UnityEngine. Nullable disabled.
- Deterministic: only `Confluence.Core.Random.Rng` streams, integer ticks, stable iteration order. No wall-clock time in rules.
- Global caps come from `Confluence.Core.Rules.GlobalRules`; local content may be stricter, never looser.
- CONTENT RULE: a catalogue record counts as implemented only when its specific behavior works in real gameplay (DuelWorld/MatchHost). Only then put `[ContentBehavior("ID")]` on the implementing class, with `[Verifies("ID")]` tests that exercise rank 1 and rank 2 (augments), the drawback, the caps it touches, and its counterplay where the spec requires one (L447). Use the record's numeric JSON fields; for prose-only parameters quote the source phrase and test that the number appears in the record text (D-011). No stubs, generic substitutes, empty handlers or menu-only entries. If a record cannot be finished, leave it unmarked and list it in gaps.
- Transformations that change structural tags register Core `StructuralDeltas` the way the framework does (see `docs/notes/sim-spells.md`); exclusive groups are enforced.
- Reuse the framework (SpellFormBuilder, deliveries, payloads, ISpellTransform, effect framework, StatBlock caps, ContentRegistry). A missing generic primitive goes in a NEW, clearly named file in the framework folder; keep edits to existing shared files minimal and additive and list them as cross-edits.

## Working alongside other agents
- Work only in your assigned worktree, owned paths and owned catalogue IDs. One writer per worktree. Two branches claiming the same ID make `ContentRegistry` throw after merge.
- Claude agents: commit `WIP:` after each record or small group passes its tests (usage limits can stop you at any time).
- Codex (sandboxed): do not run `git commit` — the sandbox keeps `.git` read-only; the operator commits after you exit. Keep your notes file current instead.
- Unity: Codex and personal-Claude workers never run Unity.exe, `tools/build-client.ps1` or the Unity CLI. Claude workflow agents that own `client/` may, but only one Unity process may run on this PC at a time.
- Reviews compare against `master`. Build or test failures inside a read-only review sandbox are not findings; `dotnet test` in a writable checkout decides.
- Every commit carries a provenance trailer: `Made-with: uchicago-claude`, `Made-with: personal-claude` or `Made-with: codex`. Claude commits put it directly above their `Co-Authored-By:` line.
- Never run `agy` or other Google tools — ask the user. No API keys in environment variables. Official clients only; never proxy a login.

## Ask the user before
- Changing `GlobalRules` or the spec precedence. Never edit `docs/confluence/`.
- A decision that contradicts catalogue or spec text (normal interpretations go to DECISIONS as usual).
- Real changes to `client/Packages/` or `client/ProjectSettings/` (discard line-ending-only re-serialization), the Unity version, or asmdefs.
- Changes to `AGENTS.md`, `CLAUDE.md`, `.mcp.json` or `.gitignore`. Additive changes to `tools/` and new stage scripts in `.claude/workflows/` are fine when the stage plan lists them.
- Adding any third-party or AI-generated asset (log it in `docs/ai-assets.md`: asset, tool, date, prompt, plan, licence).

## Codex helpers (run from the main checkout)
- Review a branch: `powershell -ExecutionPolicy Bypass -File tools/codex-review.ps1 -Worktree <path> -Label <label>` → `out/reviews/<label>.json`.
- Implement a fresh packet: `powershell -ExecutionPolicy Bypass -File tools/codex-packet.ps1 -Label <label>` (the records are listed under "Codex task" in `docs/notes/<label>.md`, committed on master first) → worktree `..\Confluence-wt\codex-<label>`, handoff `out/handoff/<label>.json`.
