# The coder comparison: claude, codex, agy on the same task and base (2026-11-25, epoch 3 E2 s6 t3, the owner's request)

Facts only; no product code. The owner asked for the coders on claude, codex and agy, one after another, on one and the same task. Claude was run first by the product
(`RESULTS_coder_proof.md`, section "t3"). This file adds codex and agy, run by a harness that copies the product's units, and the three columns side by side.

**Subject.** Clone `coai-v2-t3-clone`, base commit `ae72656c` (branch `feat/v2-ep3-e2-coder` at `26e5024f` + one clone-only commit with the task file). Task
`v2/epoch-3/e2-coder-one-task/stories/s6-proof-and-measure/tasks/t7-worktree-paths-same/task.yaml` (`WorktreePaths.Same` compares with the case rule of the file system; four dod items).
**Harness:** `v2/epoch-3/e2-coder-one-task/comparison/` (this commit). **Machine:** WSL2 on the owner's PC, systemd 259 user manager, 14 CPUs. **Date:** 2026-11-25.
**Releases:** `codex-cli 0.171.0`, `agy 1.5.17` (a pinned copy of the program, sha256-12 `f3f384bd2c15`, equal to the installed file). **Models (owner):** codex `gpt-6.1-terra`, agy `gemini-4-argon-medium`.
**Credentials:** subscriptions only; copies of the sign-in files in a private 0700 scratch folder, deleted at the end.

## The answer first

| | claude (product) | codex `gpt-6.1-terra` | agy `gemini-4-argon-medium` |
|---|---|---|---|
| green | **yes** | **no with the shipped prompt** (2 of 3 calls: `refused`); **yes** with a prompt adapted in two places (run C) | **yes**, with the shipped prompt, unchanged |
| fix turns | 0 | 0 (run C) | 0 |
| files changed, +/- | 2, +30 -1 | 2, +23 -1 (run C) | 2, +27 -1 |
| only the two task files changed | yes | yes | yes |
| the new case test is red against the base `WorktreePaths.cs` | yes (1 failed, 3 passed in its class) | yes (1 failed, 3 passed) | yes (1 failed, 3 passed) |
| coder wall (coder process, from start to exit) | 15.5 s model time of a 584 s job (the job wall holds the checks) | 24.9 s (run C); the refusals took 10.9 s and 10.3 s | 66.6 s |
| coder model time | 15.5 s | not reported by the stream (the wall above includes the CLI's start) | 63.9 s (`duration_seconds` of the result) |
| tokens in / cached / out / reasoning | 50,181 / 37,755 / 1,921 / not reported | run C: 81,594 / 54,784 / 866 / 177 (A: 37,465 / 24,576 / 238 / 70; B: 28,209 / 20,480 / 238 / 103) | 42,839 / 153,319 (`cache_read_tokens`) / 1,601 / 0 (`thinking_tokens`) |
| cost | 0.0727 USD (a pool key) | **no figure: subscription, tokens only** | **no figure: subscription, tokens only** |
| calls / turns used | 1 turn (33 responses) | 3 of the cap of 8 calls that reach the model | 1 turn of the cap of 12 |
| checks (build / test, same code) | 34.8 s / 530.9 s | 34.9 s / 534.6 s | 35.2 s / 523.7 s |
| whole suite | green | 16,514 total, 0 failed, 346 skipped | 16,514 total, 0 failed, 346 skipped |
| coder + checks, one pass | 584 s (the job) | 24.9 + 569.5 = 594 s | 66.6 + 558.9 = 626 s |
| refused or odd | none | see "Odd things", the main one: the repository's own instructions made it refuse | the guard denied two reads under `.agents/`; the init line says `permission_mode request-review` |

Read this with the limits at the end: one task, one run per vendor (codex: three runs because two were refusals), a different unit around claude than around the other two, and an adapted prompt for codex.

## Predictions, written before the first paid call (common/measurement.md rule 4), against what happened

| Predicted | Observed |
|---|---|
| codex: green, 0-1 fix turns, 2 task files; maybe waste on `AGENTS.md`; tokens in 100k-400k | green only on the third call and only after the prompt was adapted; 0 fix turns; 2 files; the `AGENTS.md` effect was far bigger than predicted (two refusals); tokens in 28k-82k per call, below the range |
| agy: weaker than both (no list/search tool, report as plain text), 0-2 fix turns, a report slip possible | **wrong**: green on turn 0 with the shipped prompt, a correct report, tests close to the claude coder's (it reused the file's own `Link` helper) |
| checks about 35 s and 530 s | 34.9 / 534.6 and 35.2 / 523.7: as predicted |
| the case test red against the base code | yes for both |

## The prompt

- The product builds the coder's turn-0 prompt in `src_mcp/src/Server/RoleJobs/CoderPrompt.cs` from `src_mcp/src/Roles/prompts/coder.md` and `shared/coder-report.schema.json` (one pass over `{{name}}`).
  `comparison/render-prompt.mjs` renders it the same way. Result: 6,204 bytes, **sha256 `9f9af1ea63d1de0d2b292ccbbc6d38f8f06cf7eb6c1bb1b359f0c7ff3075e993`**, file `comparison/prompts/turn0.verbatim.txt`
  (also `~/coai-t3/cmp3-notes/prompt-rendered.txt`). It is **byte-identical to the first user message of the real claude job's own session** (`cmp` equal), so claude and the comparison were given the same text.
  The template has no base-sha placeholder; the base is the worktree.
- Read back that it reached the model: codex's rollout file holds a user message containing the whole text (6,204 characters); agy's `transcript_full.jsonl` holds it, and the sha256 of the text inside the stdin line is the one above.
- **Codex run A:** the verbatim prompt. **Run B** (`prompts/turn0.codex-vocab.txt`, sha256 `ff468b6e75bbfa79bc74a53a76ce8422de04ee6ef8e8f07e8867cbb362d327b6`): only the one bullet that names claude's tools
  ("You may use the tools Read, Glob, Grep, Edit and Write. You have no shell...") replaced by codex's own vocabulary (read with read-only shell commands, change with `apply_patch`, no build, test or git). **Run C**
  (`prompts/turn0.codex-vocab-precedence.txt`, sha256 `90c49e03d7dd2ee7cf21ff958c12149ae719815a4788382d1d10652207180eac`): B plus one bullet saying that an instruction file of the repository that demands a command
  before work does not apply to this job. `diff` of each against the verbatim file is in the repository. **Agy ran the verbatim prompt.**
- Output shape: codex takes `--output-schema`; the API refuses the repository's schema (top-level `allOf`, `RESULTS_codex_coder.md`), so `comparison/coder-report.flat.schema.json` (all four members required, absent ones `null`) was used for the flag only;
  the prompt text still embeds the repository's own schema. Codex's answers carry `"reason":null,"notes":null`, which the repository's strict reader would not accept as written (the flat/strict reconciliation is open item X8 of the judgement); the content is otherwise the schema's. Agy has no schema flag; its answer is plain JSON text and had no stray key.
- Fix-turn digest: no fix turn was needed, so none was sent. The shape the product would send is `CoderPrompt.Fix` (the failure list `[{step,file,line,code,message}]`, at most 3 turns); it was not exercised here.

## The units

**Check unit (the product's, replicated).** `unitlib.sh` `check_props` is `BoundaryUnit.Properties` of `feat/v2-ep3-e2-coder`, element by element: the six isolation properties, `BindPaths=<worktree>`, `BindReadOnlyPaths=<main checkout>`,
`BindReadOnlyPaths=<worktree>/.git`, the submodule mount `BindReadOnlyPaths=<job>.mounts/m0:<worktree>/.agents/conventions` (the clone's module store at the pinned `cd19c173`; `diff -r` against the claude job's mount: identical),
`BindPaths=<job folder>`, `<job>/tmp:/tmp`, `<job>/vartmp:/var/tmp`, `BindReadOnlyPaths=~/.nuget/packages`, `BindReadOnlyPaths=~/.nvm` (node for the tests, read-only, on `PATH`), the resolver file for a host-network step,
the eight hardening properties (`NoNewPrivileges`, `PrivateUsers`, `RestrictSUIDSGID`, `LockPersonality`, `ProtectKernelTunables`, `ProtectControlGroups`, `Nice=10`, `LimitCORE=0`), `PrivateNetwork` (build `no`, test `yes`, the product's defaults),
`RuntimeMaxSec` (step timeout + 60 s: 960 / 1860), `MemoryMax=6000M`, `MemorySwapMax=0`. The environment is `env -i` with the allow-list of `BoundaryUnit.Environment` plus the dotnet switches of `CheckRunner`. The home is never bound; the full lists are in
`~/coai-t3/cmp3-notes/out/check-unit-props.txt`. Commands: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:2`, then `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests` (the toolchain file read at the base commit).
The tree and HEAD are compared before and after (the product's `tree_changed` rule): unchanged both times.

**Coder unit (codex and agy).** `coder-unit.sh`: E2's `CoderUnit` list plus what the E3 s4 measure found these two need: the six isolation properties; `TemporaryFileSystem=<main checkout>` + `BindReadOnlyPaths=<main>/.git` and
`TemporaryFileSystem=<coder root>` + `BindPaths=<worktree>` + `BindReadOnlyPaths=<worktree>/.git` (the main checkout and the other worktrees are HIDDEN, not read-only: E2's claude unit binds them read-only); the same submodule mount;
`BindPaths=<job>/coder`; `TemporaryFileSystem=/tmp` (the shape under which codex's vendor sandbox starts); `<job>/coder/vartmp:/var/tmp`; the vendor program folder read-only; the vendor home (a COPY of the sign-in, in the scratch folder) bound
rw at `<job>/coder/home/.codex` or `.gemini`; the resolver file; the eight hardening properties; `PrivateNetwork=no` (the CLI needs its vendor); `RuntimeMaxSec=960`, `MemoryMax=6000M`, `MemorySwapMax=0`. `env -i` with `PATH HOME TMPDIR LANG TERM NO_COLOR GIT_TERMINAL_PROMPT`
(+ `CODEX_HOME` and `CODEX_REFRESH_TOKEN_URL_OVERRIDE=http://127.0.0.1:9/oauth/token` for codex: a refresh could not reach the vendor). No `~/.nvm`, no check folders in it. Lists: `~/coai-t3/cmp3-notes/out/coder-unit-props-codex-t0C.txt` and `...-agy-t0.txt`.
Codex argv (flag set CX1, all `--disable` names of `RESULTS_codex_coder.md` section 2, `approval_policy="never"`, `shell_environment_policy.inherit="core"`, `web_search="disabled"`, the permission profile in the job's `config.toml`): `~/coai-t3/cmp3-notes/out/cx_t0C.argv`.
Model-free tool list inside the unit (`codex debug prompt-input`): the measured one of s4 (`functions.exec`, six `collaboration.*`, nested `apply_patch`, `exec_command`, ...). Codex effort `low`: the model's own default (`gpt-6.1-terra` supports low..ultra), pinned in the config.
Agy argv (AG1): `agy --print= --input-format stream-json --output-format stream-json --mode accept-edits --agent coai-coder --model gemini-4-argon-medium --add-dir <worktree>`; the working directory is a read-only guard folder with `.agents/agents/coai-coder.md`
(tools `view_file write_to_file replace_file_content multi_replace_file_content`, `commandExecutionPolicy: off`) and a fail-closed `PreToolUse` hook (`setup-agy.sh`: one explicit decision for every input; allow only `read_file`/`write_file`/`edit_file` whose resolved
`Path` lies in the worktree and not under `.git`, `.agents`, `.codex`; a record of each decision). The guard is not coai's `--agy-hook` binary of the judgement's G2: it is the s4 shell guard made fail-closed. The program is a pinned copy bound read-only.
The claude coder's own unit record is `unit-c1.json` of the real job (E2's list, plus the one submodule line).

## The probe before every checks run, and the sign-in files

Before each of the checks rounds (codex round 0 twice, see below; agy round 0) `comparison/probe-hidden.sh` ran two units with the check unit's properties (private network and host network) and `stat`ed 17 paths: the scratch folder and its `copies` and `out`,
the codex copy's `auth.json`, the agy copy's token file and guard `hooks.json`, `~/.codex`, `~/.codex/auth.json`, `~/.gemini`, the real agy token, `~/.claude`, `~/.config`, the product data folder, and the four paths where the vendor homes and guard are mounted in the job
folder (`coder/home/.codex/auth.json`, `config.toml`, `coder/home/.gemini/.../antigravity-oauth-token`, `coder/guard/.agents/hooks.json`). **Every stat failed in both network states**; the controls held (the worktree is visible, node runs); the three
mountpoint folders in the job folder are empty (the copies are in the scratch folder, which is under `/tmp`, replaced inside a check unit by the job's own `/tmp`); `/mnt` holds 0 entries (private) or 1 (host: the resolver). Outputs: `~/coai-t3/cmp3-notes/out/probe-*.txt`.
No round was run with a path visible.

Real sign-in files (sha256 first 12 hex, size, mtime), before and after all runs, `diff` empty:
`~/.codex/auth.json` `c295529f444c` 4166 B 2026-11-24 14:30:51 CET; `~/.gemini/antigravity-cli/antigravity-oauth-token` `709cbe3b6e0d` 571 B 2026-11-25 19:56:21 CET (written by the owner's own agy before this task); `~/.gemini/jetski-standalone-oauth-token` `fa2956aeb9a8` 650 B 2026-11-19.
The codex copy was byte-equal to the real file at the end (no refresh happened); the agy copy refreshed (access fingerprint `ba8b36ad` -> `f5261252`), its refresh-token fingerprint stayed `5a286b2d`, equal to the real file's, as s4 measured.
`codex --version` and `agy --version` were run with the real HOME once, before the "before" snapshot; no other command of either vendor touched a real home or a real vendor folder.

## Odd things, each with where it was seen

1. **Codex refused the shipped prompt, twice (runs A and B).** `coder.md` says "You may use the tools Read, Glob, Grep, Edit and Write. You have no shell." Codex has none of those names. Run A (verbatim): no file was read; its answer was
   `status: refused`, "no file-reading tool is available ... and the task prohibits shell commands" (37,465 in / 238 out). Run B (that bullet in codex's vocabulary): it read `.agents/conventions/ENTRY.md` and `.agents/PROJECT.md` with `cat`, then refused:
   the repository's `AGENTS.md` makes the rule resolver mandatory before any edit ("do not change affected files until it is restored") and the prompt forbids commands (28,209 in / 238 out). Run C (plus the precedence bullet) did the work. So with this repository's `AGENTS.md`, the shipped
   prompt does not produce a codex coder; the claude coder (same `AGENTS.md` via `CLAUDE.md`) ignored it. That is a property of the prompt and the repository, not of the model's ability, and it is a finding for the main AI. All three codex streams are in `~/coai-t3/cmp3-notes/codex-{A,B,C}-t0.jsonl`.
2. **Agy's guard denied two reads**: `.agents/conventions/ENTRY.md` and `.agents/PROJECT.md` (the guard denies every path under `.agents`, as G3 of the judgement says); the model went on without them. Two other reads were of files that do not exist (it guessed names).
   The hook log has one record for each decision (`out/agy_t0_hooklog.tsv`): 13 tool calls (11 read_file, 2 edit_file), 11 allowed, 2 denied, no other tool.
3. **Agy's init event names `permission_mode: request-review`** although `--mode accept-edits` was on the argv. The edits went through (the hook allowed them). What the init field means was not investigated; the judgement's G6 asks for the mode to be asserted from the stream, and this field does not say `accept-edits`.
4. **Agy's token counts**: `cache_read_tokens` 153,319 is larger than `input_tokens` 42,839 and `total_tokens` is 44,440 (= in + out), so cache reads are not in the total. Codex's `cached_input_tokens` is a part of its `input_tokens`. The two are not the same quantity: do not subtract.
5. **Codex left empty `.codex`, `.aws` directories in the worktree** (the vendor sandbox's masks; untracked, empty, `git status` does not show them), as s4 found. Agy left none.
6. **A tool-count difference in what the coders read.** Codex read the repository's rules files and the README (`cat`) before editing; agy read `WorktreeManager.cs`, `RealPath.cs` and `WorktreeManagerTests.cs` (and re-read its own two files). Neither ran a build or test (codex: no command but `cat`; agy: no command tool). Codex's report claims `d4 met: false` ("the existing tests were not run, as instructed"), more cautious than the claude and agy reports.
7. **A harness fault, mine:** the first checks run for codex had a missing `--` in the script and the build step exited 127 in 0.1 s ("`build`: No such file"). It ran no code of the task; the folder is kept as `out/checks-codex-r0-aborted-script-bug`, the probe of that start also passed, and the round was run again. The codex checks figures above are from the second start.

## The test the coders wrote, against the base code

For each vendor a second worktree at the base `ae72656c` got only the vendor's test file from the vendor's commit (the base `WorktreePaths.cs`); the check-unit replica built it and ran the test class
(`CoaiMcp.Tests.AWorktreePathIsComparedByItsRealPathTests`, `redcheck.sh`): codex **4 tests, 1 failed, 3 passed** (`FoldersDifferingOnlyByCase_AreDifferentOnLinux` fails); agy **4 tests, 1 failed, 3 passed**
(`TwoRealFoldersThatDifferOnlyByCase_AreNotTheSamePlaceOnLinux` fails). Same as the main AI's check of the claude test. The tests were read by hand: codex's second test checks the trailing separator only; agy's checks a trailing separator against a symbolic link (the helper `Link` of the file, which skips where links cannot be made);
both skip with a reason on Windows and macOS. The production change is the same rule in all three (case-sensitive on Linux, ignoring case on Windows and macOS); codex inlined the condition, agy and claude made a private property.

## Commits and where things are

- Codex worktree `~/coai-t3/cmp3/coder-root/coai-coder-cmp3-codex`, branch `codex-t3`, commit `7003265f68f98ea025f97a2ff867fc4aa0161d60` (message `coder cmp3-codex: t7-worktree-paths-same`), one commit on `ae72656c`.
- Agy worktree `~/coai-t3/cmp3/coder-root/coai-coder-cmp3-agy`, branch `agy-t3`, commit `1e814de11871ae661d4c6b09c333bdd5bb308998` (`coder cmp3-agy: t7-worktree-paths-same`), one commit on `ae72656c`.
- Both branches exist only in the clone (`coai-v2-t3-clone`), which was not pushed anywhere. Streams, hook log, ledger, check logs, property lists and the probe outputs: `~/coai-t3/cmp3-notes/` (working notes in `NOTES.md`); the scratch folder is lost at a WSL restart.
- The claude column is copied from `RESULTS_coder_proof.md` section "t3" (job `r20261125200645-562c2de4`, commit `9cb6221a`).

## Cleanup

Deleted at the end: both sign-in copies, the two vendor homes (with the sessions and the model cache the CLIs wrote there), the guard folder, the scratch home, the pinned agy program copy, the two red-check worktrees and their branches and job folders.
After it: no file named `auth.json`, `antigravity-oauth-token` or `models_cache.json` under the scratch folder, the harness folder or the notes folder; a search of the notes and scratch folders for token key names finds nothing; the three mountpoint folders in the job folders are empty. The two coder worktrees and their branches stay as evidence.
The real sign-in files were not changed (above). No coai gate was run; nothing was merged or pushed from the clone.

## What this does not settle

- One task of difficulty 4 whose cause and files were already named, one run per vendor: no variance, no claim that one vendor is better. Claude's 15.5 s, codex's 24.9 s and agy's 63.9 s are single observations.
- Codex is green only with a prompt changed in two places (tools bullet, precedence bullet). It says nothing about what the codex coder would do with a prompt written for it, and the precedence bullet tells the model to disregard part of the repository's instructions, which a product would have to decide on purpose.
- The claude run was inside the product's own E2 coder unit (main checkout read-only, not hidden); codex and agy ran in the E3 s4 shape (hidden), started by this harness, not by coai; the preflight rows of the product were not run for them (only the probe above). Network, memory and the property lists are otherwise the same.
- "Coder wall" is the coder process for codex and agy, and the model's own figure for claude; the claude job wall (584 s) includes its checks. Reasoning tokens are reported by codex only.
- Two quantities are not comparable across vendors: token counts (each vendor counts caches differently, point 4) and cost (a pool key against two subscriptions).
- The codex report's `null` members and agy's lack of a schema flag were not run through the repository's strict reader.
- A fix turn, a resumed conversation or thread, and the guard's behaviour under a real red round were not exercised: nothing went red.
- The sign-in copies were visible to the coder units by design (they are the vendor's home); the judgement's accepted risks A1 and A2 are unchanged by this run.
