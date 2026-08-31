# research/ — the system as it is

Documentation of shipped behaviour: [architecture.md](architecture.md) is the entry point; each
`module_*.md` deep-dives one module. Implemented plans are promoted here from `todo/` with an
`IMPLEMENTED <date>` status line — none yet.

| Document | What it covers |
|---|---|
| [architecture.md](architecture.md) | System overview, container diagram, module map, cross-cutting decisions |
| [PLAN_epic_01_foundation.md](PLAN_epic_01_foundation.md) | IMPLEMENTED 2026-08-31 — repository foundation: conventions mount, solution + logging skeleton, CI |
| [PLAN_epic_02_core.md](PLAN_epic_02_core.md) | IMPLEMENTED 2026-08-31 — the pure core: findings, sanitizer, dedup + counting, round state machine |
| [module_core.md](module_core.md) | The core module: entities, flow, the decisions a reader needs |
| [PLAN_epic_03_runners.md](PLAN_epic_03_runners.md) | IMPLEMENTED 2026-08-31 — reviewer runners: worktrees, context assembly, vendor argvs, bounded scheduler |
| [module_runners.md](module_runners.md) | The runners module: process seam, worktree lifecycle, the five failure modes |
