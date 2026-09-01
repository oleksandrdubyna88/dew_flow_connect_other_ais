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
| [PLAN_epic_05_extension.md](PLAN_epic_05_extension.md) | IMPLEMENTED 2026-08-31 — the VS Code extension: settings, install button, rounds view, snippet |
| [module_extension.md](module_extension.md) | The extension module: commands, the install path, what it deliberately does not do |
| [PLAN_epic_06_proof.md](PLAN_epic_06_proof.md) | IMPLEMENTED 2026-08-31 — proof: the scripted end-to-end in CI, and the recorded real run |
| [RESULTS_first_real_run.md](RESULTS_first_real_run.md) | The first real run: six attempts, eight defects, and what the reviewers found |
| [PLAN_escalation_loopback.md](PLAN_escalation_loopback.md) | IMPLEMENTED 2026-08-31 — reaching a person from the server through files, with the modal, status bar and open-questions list |
| [PLAN_conventions_pass.md](PLAN_conventions_pass.md) | IMPLEMENTED 2026-09-01 — the gate reads the project's own written rules and spends code round 1 on nothing else; rounds and threshold split per stage |
| [RESULTS_conventions_prompt.md](RESULTS_conventions_prompt.md) | Three conventions prompts × two vendors plus a variance control: indistinguishable, nothing invented, and all eight missed the rule written as a table |
| [PLAN_per_role_gate_and_dealt_prompts.md](PLAN_per_role_gate_and_dealt_prompts.md) | IMPLEMENTED 2026-09-01 — rounds and a threshold per ROLE, prompts dealt across vendors on request, the fourth exhausted-rounds answer, and the translator removed |
| [RESULTS_predelivery_campaign.md](RESULTS_predelivery_campaign.md) | Eleven real runs over two plans and two commits under seven settings combinations — what held, what was never exercised, and the panel-versus-server defect it found |
