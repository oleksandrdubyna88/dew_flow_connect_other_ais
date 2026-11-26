# Predictions for the measure of E3 s5 t6 (codex) and s6 t7 (agy), written 2026-11-26 BEFORE the first paid call

Rule: `common.measurement` section 4. Written down first, reported against the observation in the results sections of
`v2/spikes/RESULTS_codex_coder.md` and `v2/spikes/RESULTS_agy_coder.md`. Already done before this file (model-free, no vendor request):
`codex --version`, `codex features list`, `codex debug prompt-input` with the CX1 flags and a dozen switches, `codex doctor --json`.
Those found by reading the binary's own text and trying it: `-c agents.enabled=false` removes the six `collaboration.*` tools from the prompt-input,
and `codex doctor --json` prints the permission profile; so the predictions below do not cover those two.

## codex 0.171.0 (caps: 30 model calls)

- d1: a `spawn_agent` child inherits the parent's sandbox: B2, B6, B10, B11 each refused as for the parent; the child lists the same tools. The stream
  names the call with an item of its own (not `command_execution`), so coai can refuse a turn that used it. `agents.enabled=false` also leaves a
  model with no `spawn_agent` in a paid turn.
- d2: the JavaScript of `functions.exec` has no file, environment, process or network API of its own: only the nested tools. It runs in a process of
  the CLI side (a code-mode host), not inside the bwrap sandbox.
- d3: with `CODEX_HOME` outside `/tmp` there is no "Refusing to create helper binaries" warning; helpers are made under `CODEX_HOME/tmp/arg0-linux`;
  files written into `.codex/` of the worktree after turn 0 do not run and do not widen the limit.
- d4: a copy with `exp` far away makes no refresh POST whatever `last_refresh` says (0 POSTs). A copy with `exp` in 60 s and the override at
  127.0.0.1:9 ends `Your access token could not be refreshed. Please log out and sign in again.`, exit 1, and the text names no vendor address.
  A placeholder in place of the refresh token answers `exec` and `exec resume` (the refresh is never due).
- d5: `codex doctor --json` with the job's `CODEX_HOME` names the profile `coai` and a restricted filesystem and network sandbox.
