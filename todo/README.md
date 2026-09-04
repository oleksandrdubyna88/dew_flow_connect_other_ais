# todo/ — open work

Plans for work that is **not finished**. Documentation of the system as it is lives in
[../research/](../research/); a plan moves there (`/promote-plan`) the moment its work ships.
Convention: `.claude/rules/shared/common/planning-docs.md`.

## Currently open

| Plan | What it is for |
|---|---|
| [PLAN_team_server.md](PLAN_team_server.md) | One subscription per vendor for a whole company: `coai-server` on the CredsForDevs VM runs the signed-in CLIs behind Microsoft/Google sign-in restricted to the company domain; the panel gains *Team servers*, the picker offers the server's vendors and allowed models, every run is accounted to a person, and rate limits are spread across account slots. |
| [PLAN_family_ci_hardening.md](PLAN_family_ci_hardening.md) | Every dew_flow repository: formatting gates in CI, Dependabot, a PR template and semantic titles, release-please, and a branch protection that requires every check with no bypass for admins — the operator's hardening list of 2026-09-05, with the repository settings already applied. |
| [PLAN_local_db.md](PLAN_local_db.md) | A SQLite projection under the rounds log, written by the server as rounds advance: sessions, rounds, reviewers, every finding with its resolution, usage — and FTS search; the extension reads it through sql.js with no native module. Asked for on 2026-09-05: "чтоб поиск был норм и структура была". |
| [PLAN_findings_in_the_log.md](PLAN_findings_in_the_log.md) | An expanded row of the rounds log lists the findings themselves, not only how many — the one epic of the log-view plan that was not built, and the question of whether the server should keep findings per round. |
| [PLAN_multi_repo_and_uncommitted.md](PLAN_multi_repo_and_uncommitted.md) | A change that spans several repositories is reviewed as one round with one verdict, and work that is not committed yet can be reviewed at all — the working tree snapshotted into a real commit object so nothing downstream changes. |
| [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) | The local reviewer assumes a trusted Ollama on this machine. Acknowledging a non-loopback host, a key path for a served vLLM, and whether cancellation works on anything else. |
| [PLAN_provider_liveness.md](PLAN_provider_liveness.md) | `providers` still calls a vendor healthy on the strength of `--version`, which never reaches the vendor. Three states instead of two, established by a real round trip and cached. |
| [PLAN_rule_formatting.md](PLAN_rule_formatting.md) | Every reviewer in an eight-cell measurement missed the one rule written as a table row. Whether rule FORMATTING changes what a reviewer can apply, measured. |
| [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) | A render waits on its probes with nothing on screen saying so: press ⟳ where nothing answers and the old sentence sits unchanged for seconds. Extracted from the WSL plan's code round, where it was accepted as true and left as a tail. |
| [PLAN_shared_rules_reach_reviewers.md](PLAN_shared_rules_reach_reviewers.md) | The family's rules are a submodule and git puts none in a linked worktree, so every conventions pass judged diffs against an empty directory. Phases 1–2 (the worktree carries them, the budget spends on local rules first) shipped 2026-09-04; phase 3 — the gate snippet becoming a shared rule instead of a paste that goes stale — is open. |

Everything else planned so far shipped — the master plan, all six epics, the conventions pass, the
per-role gate with dealt prompts, and the escalation tail are
in [../research/](../research/) with `IMPLEMENTED` status, beside the record of the first real run.

## Promoted

| Date | Plan | What it delivered |
|---|---|---|
| 2026-09-05 | [PLAN_bench_campaign_after_the_store_fix.md](../research/PLAN_bench_campaign_after_the_store_fix.md) | The session-store fix carried real rounds: 6 of 6 under five servers on one data directory, clean on disk. Fable's judgement put the plan round at three times the useful findings of the code round for a ninth of the tokens, and the vendors at 83 / 56 / 11 % worth having. Five defects in the bench itself and three in the local model, each with a test. |
| 2026-09-05 | [PLAN_rounds_log_view.md](../research/PLAN_rounds_log_view.md) | *Show review rounds* opens a page with one sortable, filterable, searchable table over every round of every session; `rounds.md` and its five-second rewrite are gone. The sort and filter the tests exercise are the ones the page runs — their source is embedded verbatim. |
| 2026-09-04 | [PLAN_rounds_collapse_and_vendor_colour.md](../research/PLAN_rounds_collapse_and_vendor_colour.md) | *Split with Fable* fires on the checkbox alone — the reviewer list it consulted could never contain Fable, because Fable is the calling AI's own model — a round the panel opened closes itself while a round the person opened stays theirs, and every vendor name carries one stable colour from the editor's chart palette. Its own tails cost three more releases: a dead round claiming `361m 40s`, a card that offered to open into an apology for having nothing, and a triangle that repainted the world. |
| 2026-09-03 | [PLAN_engine_lease.md](../research/PLAN_engine_lease.md) | Two windows, two servers, one GPU: the card is now leased across processes by the operating system's own lock. Measured with five parallel processes — 5 of 5 answered, 4.3 s — and the measurement found what no review did: the local request carried no token ceiling, so a reasoning model spent every deadline thinking. |
| 2026-09-03 | [PLAN_round_card_detail.md](../research/PLAN_round_card_detail.md) | A finished round opens to its reviewers — status, findings, its own duration and what it read — and the disclosure survives the five-second repaint that used to close it. |
| 2026-09-03 | [PLAN_one_gpu_one_reviewer.md](../research/PLAN_one_gpu_one_reviewer.md) | Three reviewers of one round were put on one GPU and two were cancelled at 590 s while the third answered in 30 s. The cap that matters is now keyed by the engine's endpoint, the limiters outlive a round, and a 404 with a `429` in its Cloudflare ray id is no longer reported as a rate limit. |
| 2026-09-03 | [PLAN_server_version_per_side.md](../research/PLAN_server_version_per_side.md) | The Server section describes the side the panel runs on rather than a record VS Code shares between a local and a WSL window: "installed" is the disk, the version is the binary's own new `--version`, a binary that cannot answer says so and offers the update, and the release fails when its binary misreports its tag. Its own gate refuted the key it was told to use, then the encoding of the key that replaced it. |
| 2026-09-03 | [PLAN_wsl_local_engine.md](../research/PLAN_wsl_local_engine.md) | A local reviewer that works from WSL, or says exactly why it cannot: the round carries a cure instead of only a refusal, the panel names an engine answering on the Windows side, and `⇄` writes and unwrites `networkingMode=mirrored`. Its own gate removed the gateway probe the first draft had, and found two defects in the implementation. |
