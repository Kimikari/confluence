# Confluence

Windows PC first-person wizard dueling game. The complete specification is in `docs/confluence/` and is read-only: never edit, reformat, or regenerate those files.

## Read first
- `docs/ARCHITECTURE.md` — layout, layering, tick order, content model, networking.
- `docs/IMPLEMENTATION_PLAN.md` — stages; `docs/PROGRESS.md` — current state and next task; `docs/DECISIONS.md` — interpretations.
- `docs/CONTENT_LEDGER.md` — per-ID status (generated; do not hand-edit statuses).

## Reading the spec (1.5 MB markdown, 1.8 MB JSON — never load whole files)
- `node tools/spec.mjs toc` / `show <heading or ID>` / `lines <a> <b>` / `grep <regex>`
- `node tools/cat.mjs get <ID>`, `kit <WIZ-ID>`, `list <type> field=value ...`, `grep <regex> [type]`, `refs <ID>`, `stats`
- Precedence: build prompt requirements > global rules > catalogue entries. Record interpretations in DECISIONS.md.

## Build and test
- `dotnet build Confluence.sln` and `dotnet test Confluence.sln` (or a single `tests/Confluence.X.Tests` project).
- Libraries in `src/` target netstandard2.1 with C# 9 because Unity loads the same DLLs: no file-scoped namespaces, global usings, record structs, System.Text.Json, or UnityEngine references.
- Unity client: `client/`, Unity 6000.6.4f1. Sync core DLLs with `powershell -File tools/sync-unity.ps1` before opening or building.

## Rules for content work
- A catalogue record counts as implemented only when its specific behavior works in gameplay; verified only with a `[Verifies("ID")]` test. No empty handlers, generic substitutes, or menu-only entries.
- Global caps come from `Confluence.Core.Rules.GlobalRules`; local content may be stricter, never looser.
- Use only `Confluence.Core.Random.Rng` streams for randomness. No wall-clock time in rules.
- Update `docs/PROGRESS.md` (and regenerate the ledger) at the end of every work session.
