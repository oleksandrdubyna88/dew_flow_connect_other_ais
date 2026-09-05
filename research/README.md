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
| [module_bench.md](module_bench.md) | the measurement bench: drives the installed server over a corpus, records without judging, Fable judges after; the three things it got wrong about itself |
| [PLAN_epic_05_extension.md](PLAN_epic_05_extension.md) | IMPLEMENTED 2026-08-31 — the VS Code extension: settings, install button, rounds view, snippet |
| [module_extension.md](module_extension.md) | The extension module: commands, the install path, what it deliberately does not do |
| [PLAN_epic_06_proof.md](PLAN_epic_06_proof.md) | IMPLEMENTED 2026-08-31 — proof: the scripted end-to-end in CI, and the recorded real run |
| [RESULTS_first_real_run.md](RESULTS_first_real_run.md) | The first real run: six attempts, eight defects, and what the reviewers found |
| [PLAN_escalation_loopback.md](PLAN_escalation_loopback.md) | IMPLEMENTED 2026-08-31 — reaching a person from the server through files, with the modal, status bar and open-questions list |
| [PLAN_conventions_pass.md](PLAN_conventions_pass.md) | IMPLEMENTED 2026-09-01 — the gate reads the project's own written rules and spends code round 1 on nothing else; rounds and threshold split per stage |
| [RESULTS_conventions_prompt.md](RESULTS_conventions_prompt.md) | Three conventions prompts × two vendors plus a variance control: indistinguishable, nothing invented, and all eight missed the rule written as a table |
| [PLAN_per_role_gate_and_dealt_prompts.md](PLAN_per_role_gate_and_dealt_prompts.md) | IMPLEMENTED 2026-09-01 — rounds and a threshold per ROLE, prompts dealt across vendors on request, the fourth exhausted-rounds answer, and the translator removed |
| [RESULTS_predelivery_campaign.md](RESULTS_predelivery_campaign.md) | Eleven real runs over two plans and two commits under seven settings combinations — what held, what was never exercised, and the panel-versus-server defect it found |
| [PLAN_cli_update_button.md](PLAN_cli_update_button.md) | IMPLEMENTED 2026-09-01 — an update button per reviewer CLI that says by its colour whether there is anything to update, and colour on every collapsible header |
| [PLAN_snippet_version.md](PLAN_snippet_version.md) | IMPLEMENTED 2026-09-01 — the pasted CLAUDE.md snippet carries a version, the panel reads it back, and a hash guard makes forgetting to bump it a red build |
| [PLAN_local_models.md](PLAN_local_models.md) | IMPLEMENTED 2026-09-02 — a model on this machine as a third reviewer: engine discovery, the direct call that is not the Codex CLI, real tokens and a null cost |
| [RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md) | The universal role prompts, measured against planted defects — what the wording is worth |
| [RESULTS_model_comparison.md](RESULTS_model_comparison.md) | Sixteen model/effort combinations on plan review against planted defects, twice each: which models to pick and what they cost |
| [RESULTS_model_comparison_code.md](RESULTS_model_comparison_code.md) | The same models on CODE review — throughput measured, judgement not, and the three cells an exhausted quota took |
| [RESULTS_focused_prompts.md](RESULTS_focused_prompts.md) | Twelve narrow lenses × three prompt SHAPES × two runs: a task to enact repeats itself half again as often as a question list, and five of twelve picks are decided rather than noise |
| [RESULTS_focused_vs_universal.md](RESULTS_focused_vs_universal.md) | The lenses against the broad prompt on one real change with four known-open defects: per round the universal one is not behind, the overlap is one to three findings, and both arms missed half the known truth |
| [RESULTS_local_models_128k.md](RESULTS_local_models_128k.md) | Two models on this machine at 128k, plan and code, against the hosted baseline: five of eight twice, ~25k tokens per reviewer, and why 128k is headroom rather than a requirement |
| [RESULTS_five_plans_five_models.md](RESULTS_five_plans_five_models.md) | Five real plans and their five commits across five models on the operator's own settings: 31 of 32 defects agreed on by two models or more, all four known-open findings re-found, and the dedup rule that sees almost none of it |
| [RECORD_2026_09_02_what_the_measuring_found.md](RECORD_2026_09_02_what_the_measuring_found.md) | One day consolidated: eleven campaigns, the eight product defects they found, the two left open, and the four measurement errors that would each have shipped a false number |
| [RESULTS_findings_that_are_worth_something.md](RESULTS_findings_that_are_worth_something.md) | Thirty-five findings judged one at a time by reading the code they name: the count ranking inverts, five real unknown defects surface, and the most-agreed finding is the least valuable |
| [PLAN_commands_and_autonomy.md](PLAN_commands_and_autonomy.md) | IMPLEMENTED 2026-09-03 — the gate hands back ORDERS from three operator switches: work autonomously, split the plan into epics and stories, and give the split and the risky stories to Fable |
| [PLAN_engine_lease.md](PLAN_engine_lease.md) | IMPLEMENTED 2026-09-03 — one caller on a local engine across every process, with the operating system holding the lock; measured at five parallel processes, 5 of 5 answered |
| [PLAN_rounds_log_view.md](PLAN_rounds_log_view.md) | IMPLEMENTED 2026-09-05 — the rounds log is a page with a sortable, filterable, searchable table; `rounds.md` retired |
| [PLAN_rounds_collapse_and_vendor_colour.md](PLAN_rounds_collapse_and_vendor_colour.md) | IMPLEMENTED 2026-09-04 — *Split with Fable* fires on the checkbox alone, a finished round the panel opened closes itself while the person's own choice survives, and a vendor name carries one stable colour everywhere it appears |
| [PLAN_round_card_detail.md](PLAN_round_card_detail.md) | IMPLEMENTED 2026-09-03 — a round opens to its reviewers, each with its own duration, and the open set survives the five-second patch |
| [PLAN_one_gpu_one_reviewer.md](PLAN_one_gpu_one_reviewer.md) | IMPLEMENTED 2026-09-03 — one local engine serves one reviewer at a time, keyed by its endpoint, and a status code is matched as a code rather than as three digits in a request id |
| [PLAN_server_version_per_side.md](PLAN_server_version_per_side.md) | IMPLEMENTED 2026-09-03 — the Server section describes the side the panel runs on: "installed" is the disk, the version is the binary's own new `--version`, and a release whose binary misreports its tag fails |
| [PLAN_wsl_local_engine.md](PLAN_wsl_local_engine.md) | IMPLEMENTED 2026-09-03 — a local reviewer that works from WSL or says exactly why it cannot: a cure in the round, an engine named on the Windows side, and a button that writes and unwrites mirrored networking |
| [RESULTS_commands_campaign.md](RESULTS_commands_campaign.md) | 66 calls over eleven real plans, three arms, two models: the Fable and autonomy orders separate 22/22 against 0/22, no piece of a split ever proposes epics of its own, and the split metric over-calls four times out of five |
| [PLAN_bench_campaign_after_the_store_fix.md](PLAN_bench_campaign_after_the_store_fix.md) | IMPLEMENTED 2026-09-05 — the campaign that made the session-store fix carry real rounds, sequentially and under five windows |
| [RESULTS_bench_campaign_0_17_1.md](RESULTS_bench_campaign_0_17_1.md) | the store-fix campaign on server 0.17.1: sequential and five-window runs, Fable's judgement, the two ways the local model fails, and five things the campaign found wrong in its own instrument |
