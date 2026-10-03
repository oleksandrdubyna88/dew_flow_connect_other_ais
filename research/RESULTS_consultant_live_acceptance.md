# RESULTS — the consultant, live, through the product path (Windows and WSL)

> Measured 2026-10-03 for E5.5 of [PLAN_the_consultant_works_on_every_vendor.md](PLAN_the_consultant_works_on_every_vendor.md).
> Subject: this branch's own `coai-mcp` — Windows: a copy of the Debug build of `feat/consultant-works-everywhere-e5`
> (`cfadb967`); WSL2 Ubuntu: the same commit built inside WSL from a `git archive` (`dotnet build src_mcp/src`,
> SDK 10.0.112). The operator's saved consultant settings were not changed: a claude consultant was given to ONE
> process through its environment (`COAI_CONSULTANTS` with only the `other` caller switched to `claude`).

## `--consultants` (no model calls)

| side | caller → vendor (runtime, model) | CLI | standing | extras |
|---|---|---|---|---|
| Windows | claude → antigravity (gemini-3.1-pro-high) | agy 1.2.16, own auth | default-deny | — |
| Windows | codex → gemini (antigravity, gemini-3.8-flash-medium) | agy 1.2.16 | default-deny | — |
| Windows | gemini, other → codex (gpt-6-luna) | codex-cli 0.156.1 | unconfined | — |
| WSL | claude → antigravity | agy 1.2.15, own auth | default-deny | `agy.settingsPath` `/home/<user>/.gemini/antigravity-cli/settings.json`, the `command(git grep)` snippet and its not-read-only warning |
| WSL | gemini, other → codex | codex-cli 0.154.0 | unconfined | — |

Exit 0 on both sides, `heartbeatStaleAfterSeconds: 60`, `side` `windows` / `wsl`. The snippet appears on the WSL
antigravity rows and on no Windows row, as P0 decided.

## `--check-consultant` (paid, one turn each)

| side | consultant | result | seconds |
|---|---|---|---|
| Windows | antigravity · gemini-3.1-pro-high (the claude row) | `failed`, `failureKind: quota` — `AGY_ERROR … RESOURCE_EXHAUSTED (code 429) … Individual quota reached … Resets in 87h8m53s`; cure "wait for the vendor's quota window to reset, or pick a consultant on another vendor"; transcript kept under `unparseable/consultations/` | 6.0 |
| Windows | claude 2.1.258 (env override, caller `other`) | `answered`, `confinement: restricted`, `markerRead: true`, `canary: denied-by-cli` (`deniedActions: ["Read"]` — claude's own record names the canary path), 20 815 in / 237 out | 7.8 |
| WSL | claude 2.1.197 (env override, caller `other`) | `answered`, `confinement: no-restricted` (this version has no `--restricted`; its `--help` was read and the flag dropped), `markerRead: true`, `canary: denied-by-cli` (`Read`), 19 127 in / 583 out | 14.6 |

## What it shows

1. Both one-shot modes work end to end on both sides of the seam, with this side's facts and the per-platform
   limitation rows the panel renders.
2. **The claude confinement holds through the product path**: on Windows with `--restricted`, and on WSL 2.1.197
   without it, the canary read was refused by the CLI itself — observed confinement for those two turns, not a model
   declining.
3. A spent antigravity quota is classified `quota` with its cure, live — the shape the operator's original report
   could not tell from a sharper-problem failure.

## Not settled here, and why

- **The antigravity follow-up through the product path was not observed live.** The account's weekly quota was
  spent on 2026-10-03 (≈ 87 h to reset; the machine's gate rounds share it). It is measured directly against agy
  (6 of 6, [RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md)) and covered by scenario tests on the
  real 2026-10-02 stream. Two consultations that answered on the branch build during this work (`435b1b25`,
  `faa596bf`) did not need a follow-up — the model did not reach for the shell those times — while the RELEASED binary
  returned empty three times in a row on a denied `ls` the same afternoon. To do: one forced-`run_command`
  consultation through `consult` once the quota resets.
- **codex was not checked live**: its account is rate-limited until 2026-10-08.
