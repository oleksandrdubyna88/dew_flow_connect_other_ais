# todo/ — open work

Plans for work that is **not finished**. Documentation of the system as it is lives in `research/`;
a plan moves there (`/promote-plan`) the moment its work ships. Convention:
`.claude/rules/shared/common/planning-docs.md` (mounted by epic 01).

## Currently open

| Plan | What it delivers |
|---|---|
| [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) | The master plan: the multi-model review gate — architecture, protocol, rules, decisions |
| [PLAN_epic_02_core.md](PLAN_epic_02_core.md) | Epic 2 — the pure core: finding contract, Gemini sanitizer, dedup + counting, round state machine |
| [PLAN_epic_03_runners.md](PLAN_epic_03_runners.md) | Epic 3 — reviewer runners: worktree manager, context assembly, vendor runtimes, bounded scheduler |
| [PLAN_epic_04_server.md](PLAN_epic_04_server.md) | Epic 4 — `coai-mcp`: the stdio server, review tools, creds keys, release + contract tests |
| [PLAN_epic_05_extension.md](PLAN_epic_05_extension.md) | Epic 5 — the VS Code extension: settings, install button, rounds view + escalation, CLAUDE.md snippet |
| [PLAN_epic_06_proof.md](PLAN_epic_06_proof.md) | Epic 6 — proof: scripted end-to-end in CI, one recorded real run |

Epics are ordered: each depends only on the ones above it, and 02–03 are where all the pure logic
lives — build them exactly in this order.
