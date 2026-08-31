# research/ — the system as it is

Documentation of shipped behaviour: [architecture.md](architecture.md) is the entry point; each
`module_*.md` deep-dives one module. Implemented plans are promoted here from `todo/` with an
`IMPLEMENTED <date>` status line — none yet.

| Document | What it covers |
|---|---|
| [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) | IMPLEMENTED 2026-08-31 — the master plan: architecture, protocol, the counting rule, every decision and its reason |
| [architecture.md](architecture.md) | System overview, container diagram, module map, cross-cutting decisions |
| [PLAN_epic_01_foundation.md](PLAN_epic_01_foundation.md) | IMPLEMENTED 2026-08-31 — repository foundation: conventions mount, solution + logging skeleton, CI |
| [PLAN_epic_02_core.md](PLAN_epic_02_core.md) | IMPLEMENTED 2026-08-31 — the pure core: findings, sanitizer, dedup + counting, round state machine |
| [module_core.md](module_core.md) | The core module: entities, flow, the decisions a reader needs |
| [PLAN_epic_03_runners.md](PLAN_epic_03_runners.md) | IMPLEMENTED 2026-08-31 — reviewer runners: worktrees, context assembly, vendor argvs, bounded scheduler |
| [module_runners.md](module_runners.md) | The runners module: process seam, worktree lifecycle, the five failure modes |
| [PLAN_epic_04_server.md](PLAN_epic_04_server.md) | IMPLEMENTED 2026-08-31 — coai-mcp: the stdio server, review tools, vault keys, release line |
| [module_server.md](module_server.md) | The server module: the seven tools, the stage flow, persistence, what is verified |
| [PLAN_epic_05_extension.md](PLAN_epic_05_extension.md) | IMPLEMENTED 2026-08-31 — the VS Code extension: settings, install button, rounds view, snippet (loopback deferred) |
| [module_extension.md](module_extension.md) | The extension module: commands, the install path, what it deliberately does not do |
| [PLAN_epic_06_proof.md](PLAN_epic_06_proof.md) | IMPLEMENTED 2026-08-31 — proof: the scripted end-to-end in CI, and the recorded real run |
| [RESULTS_first_real_run.md](RESULTS_first_real_run.md) | The first real run: six attempts, eight defects, and what the reviewers found |
