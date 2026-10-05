Follow AGENTS.md (or CLAUDE.md if this checkout has no AGENTS.md). You are an independent second-vendor reviewer. Do not modify any files.

Review the changes on this branch against master: start with `git diff master...HEAD --stat`, then read the diff and the code it touches.

For every class this diff marks [ContentBehavior("ID")]:
- run `node tools/cat.mjs get ID` and compare the record text with the code;
- check numbers (rank 1 vs rank 2, drawbacks, durations, radii, costs, cooldowns);
- check behavior that silently differs from the record text;
- check global caps from Confluence.Core.Rules.GlobalRules that are bypassed (cost floor, refunds, proc budget, mastery clamps, shield/heal/movement/CDR caps, hard-control immunity);
- check structural deltas and exclusive groups for tag-changing transformations;
- check that its [Verifies] tests exercise DuelWorld or MatchHost rather than only constructing objects or asserting tautologies;
- check the counterplay scenario where spec line 447 requires one.

For networking code: hidden-information leaks (other pairs' duels, private shop or draft data, an opponent's private grammar UI), the server trusting client claims (damage, hits, events, resources), missing validation or rate limits, and prediction/reconciliation correctness.

Do not run dotnet build, dotnet test or Unity: this sandbox cannot. Reading files, git read commands and `node tools/*.mjs` are fine.

Report only real, evidenced defects, each with the record text or spec line and the code location. List every ID you checked in checkedIds.
