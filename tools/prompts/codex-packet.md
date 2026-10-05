Follow AGENTS.md. You are implementing a packet of catalogue records in this worktree.

Implement only the records listed under "Codex task" in docs/notes/{LABEL}.md, and only inside the paths listed there. Do not edit existing files outside those paths. If a record needs a framework primitive that does not exist, skip that record and list it in gaps.

Build: dotnet build Confluence.sln --no-restore --disable-build-servers
Test:  dotnet test Confluence.sln --no-restore --filter Category!=Slow
If tests cannot run inside this sandbox, say exactly what failed in "tests" and make sure the build is clean.

Do not run git commit: the sandbox keeps .git read-only and the operator commits after you exit.
Keep docs/notes/{LABEL}.md current: "Status / next steps", the finished IDs, and an "Interpretations" list.

Final answer fields: branch and worktree (git rev-parse --abbrev-ref HEAD and --show-toplevel), commit as an empty string, api (what you added), tests (commands run and results), decisions (your interpretations), contentImplemented (IDs now carrying [ContentBehavior]), crossEdits (shared files you touched), gaps.
