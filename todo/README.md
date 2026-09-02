# todo/ — open work

Plans for work that is **not finished**. Documentation of the system as it is lives in
[../research/](../research/); a plan moves there (`/promote-plan`) the moment its work ships.
Convention: `.claude/rules/shared/common/planning-docs.md`.

## Currently open

| Plan | What it is for |
|---|---|
| [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) | The local reviewer assumes a trusted Ollama on this machine. Acknowledging a non-loopback host, a key path for a served vLLM, and whether cancellation works on anything else. |
| [PLAN_provider_liveness.md](PLAN_provider_liveness.md) | `providers` still calls a vendor healthy on the strength of `--version`, which never reaches the vendor. Three states instead of two, established by a real round trip and cached. |
| [PLAN_rule_formatting.md](PLAN_rule_formatting.md) | Every reviewer in an eight-cell measurement missed the one rule written as a table row. Whether rule FORMATTING changes what a reviewer can apply, measured. |

Everything else planned so far shipped — the master plan, all six epics, the conventions pass, the
per-role gate with dealt prompts, and the escalation tail are
in [../research/](../research/) with `IMPLEMENTED` status, beside the record of the first real run.
