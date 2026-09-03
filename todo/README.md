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
| [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) | A render waits on its probes with nothing on screen saying so: press ⟳ where nothing answers and the old sentence sits unchanged for seconds. Extracted from the WSL plan's code round, where it was accepted as true and left as a tail. |

Everything else planned so far shipped — the master plan, all six epics, the conventions pass, the
per-role gate with dealt prompts, and the escalation tail are
in [../research/](../research/) with `IMPLEMENTED` status, beside the record of the first real run.

## Promoted

| Date | Plan | What it delivered |
|---|---|---|
| 2026-09-03 | [PLAN_engine_lease.md](../research/PLAN_engine_lease.md) | Two windows, two servers, one GPU: the card is now leased across processes by the operating system's own lock. Measured with five parallel processes — 5 of 5 answered, 4.3 s — and the measurement found what no review did: the local request carried no token ceiling, so a reasoning model spent every deadline thinking. |
| 2026-09-03 | [PLAN_round_card_detail.md](../research/PLAN_round_card_detail.md) | A finished round opens to its reviewers — status, findings, its own duration and what it read — and the disclosure survives the five-second repaint that used to close it. |
| 2026-09-03 | [PLAN_one_gpu_one_reviewer.md](../research/PLAN_one_gpu_one_reviewer.md) | Three reviewers of one round were put on one GPU and two were cancelled at 590 s while the third answered in 30 s. The cap that matters is now keyed by the engine's endpoint, the limiters outlive a round, and a 404 with a `429` in its Cloudflare ray id is no longer reported as a rate limit. |
| 2026-09-03 | [PLAN_server_version_per_side.md](../research/PLAN_server_version_per_side.md) | The Server section describes the side the panel runs on rather than a record VS Code shares between a local and a WSL window: "installed" is the disk, the version is the binary's own new `--version`, a binary that cannot answer says so and offers the update, and the release fails when its binary misreports its tag. Its own gate refuted the key it was told to use, then the encoding of the key that replaced it. |
| 2026-09-03 | [PLAN_wsl_local_engine.md](../research/PLAN_wsl_local_engine.md) | A local reviewer that works from WSL, or says exactly why it cannot: the round carries a cure instead of only a refusal, the panel names an engine answering on the Windows side, and `⇄` writes and unwrites `networkingMode=mirrored`. Its own gate removed the gateway probe the first draft had, and found two defects in the implementation. |
