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

## agy 1.5.18 (s6 t7 after the owner's word of 2026-11-26: the installed release, at most 30 turns), written before the first paid turn

Already observed before this text, model-free (stdin closed, no model turn): the init line is printed with no message and names `permission_mode=request-review` for no `--mode`, `plan`, `accept-edits`, `auto` (with the fall-back warning) and an unknown mode.
Order of the rows by security weight: (1) the s4 t5 rows B1-B15 fresh and resumed on this release together with d2 (B13 resumed, late files) - the guard and the agent are what hold; (2) d9/G14 the mode (AG-M3/AG-M4); (3) d4 G10 the sign-in without a refresh token; (4) d3 the guard folder read-only and the stream beside the hook; (5) d5 G9 the program from a read-only copy; (6) d6 G11 the proxy. Rows that do not fit in 30 turns are written as not measured.
- d2: no worktree hook, agent or rule-as-code runs on a fresh, a resumed or a late-files turn; the guard sees and decides every tool call; the rule TEXT of the worktree is followed (A7). All B rows hold as on 1.5.17.
- d9: the value of `permission_mode` does not differ between modes (observed), so AG-M3 cannot enter; with a command tool and `--mode accept-edits` a resumed turn denies the command (AG-M4 enters).
- d3: a turn works with the guard folder read-only (agy writes to its home, not to its cwd); a tool event's id is the id the hook saw; a denied tool is a failed step with the hook's reason; the stream names the mode only in the init line.
- d4: a copy with a valid access token and no refresh token works for one turn and for `models`; an expired one fails at start with a sign-in text and a non-zero exit; the refresh-token fingerprint stays `5a286b2d` after every refresh.
- d5: a read-only copy of the program runs; it may try to write its update check into the home; it cannot replace itself.
- d6: `models` and a turn work behind a proxy relay if agy honours the proxy variables for every connection; a connection that does not use the proxy fails in the no-network unit.
