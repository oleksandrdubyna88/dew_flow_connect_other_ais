# todo/ — open work

Plans for work that is **not finished**. Documentation of the system as it is lives in
[../research/](../research/); a plan moves there (`/promote-plan`) the moment its work ships.
Convention: `.claude/rules/shared/common/planning-docs.md`.

## Currently open

| Plan | What it is for |
|---|---|
| [PLAN_multi_repo_and_uncommitted.md](PLAN_multi_repo_and_uncommitted.md) | A change that spans several repositories is reviewed as one round with one verdict, and work that is not committed yet can be reviewed at all — the working tree snapshotted into a real commit object so nothing downstream changes. |
| [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) | The local reviewer assumes a trusted Ollama on this machine. Acknowledging a non-loopback host, a key path for a served vLLM, and whether cancellation works on anything else. |
| [PLAN_provider_liveness.md](PLAN_provider_liveness.md) | `providers` still calls a vendor healthy on the strength of `--version`, which never reaches the vendor. Three states instead of two, established by a real round trip and cached. |
| [PLAN_rule_formatting.md](PLAN_rule_formatting.md) | Every reviewer in an eight-cell measurement missed the one rule written as a table row. Whether rule FORMATTING changes what a reviewer can apply, measured. |
| [PLAN_server_version_per_side.md](PLAN_server_version_per_side.md) | The Server section reports a remembered version that is shared by the Windows and the WSL side of one machine, while the binary is per side — so WSL claims 0.12.2, runs 0.12.1, and can never be updated from the panel. "Installed" becomes the disk, the version becomes the binary's own `--version`. |
| [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) | A render waits on its probes with nothing on screen saying so: press ⟳ where nothing answers and the old sentence sits unchanged for seconds. Extracted from the WSL plan's code round, where it was accepted as true and left as a tail. |

Everything else planned so far shipped — the master plan, all six epics, the conventions pass, the
per-role gate with dealt prompts, and the escalation tail are
in [../research/](../research/) with `IMPLEMENTED` status, beside the record of the first real run.

## Promoted

| Date | Plan | What it delivered |
|---|---|---|
| 2026-09-03 | [PLAN_wsl_local_engine.md](../research/PLAN_wsl_local_engine.md) | A local reviewer that works from WSL, or says exactly why it cannot: the round carries a cure instead of only a refusal, the panel names an engine answering on the Windows side, and `⇄` writes and unwrites `networkingMode=mirrored`. Its own gate removed the gateway probe the first draft had, and found two defects in the implementation. |
