# E3 s2 t5 - the codex model name and the tool set: the real preflight (d8, V35) and the red-first record (d1)

Measured and run on 2026-11-27 (epoch 3 E3 s2 t5). Facts only. **No model was called to make this file**: the real run is the preflight of the coder road and two model-free
programs of the CLI (`codex debug models`, `codex debug prompt-input`). The codex budget stays 25 of 30.

Installed release: `codex-cli 0.171.0` (`~/.codex/packages/standalone/releases/0.171.0-x86_64-unknown-linux-musl/bin/codex`). WSL2.
Code: `feat/v2-ep3-e3-roles`, the two commits of this task after the merge of `eb3a0e2c` (addendum 4).

## 1. The real preflight, no model call (d8; V35)

Harness: `src_mcp/tests/TheRealCodexCoderRunTests.cs`, test `Names_OfTheRealCodex_AreResolvedFromTheLiveList_TheToolReportsAreRecorded_AndNoModelIsCalled`
(phase `names`, skipped unless `COAI_REAL_CODEX_PHASE=names`). The process `HOME` is a temp folder for the whole test; the person's real home is read only by the product's
own sign-in recipe (the copy with the placeholder in the place of the refresh token). The sha256 of the real `~/.codex/auth.json` was taken inside the test before and after:
**equal**. Nothing else of the real home was read. No token, no environment and no file content was printed.

### 1.1 The live list, read in the unit of the job (A4-M3)

| | |
|---|---|
| command | `codex debug models` (no `--bundled`), argv exactly `debug models`, stdin empty |
| where | the real unit of the job (the property list of the launch), `CODEX_HOME` = the job's own, with the job's sign-in copy |
| working folder | `<job>/coder/codex/models-cwd`: an empty folder inside the job's vendor folder, never the worktree; it held 0 entries after the run; no folder above it, as far as the unit shows the way up (up to the folder the unit covers: on a job folder under /tmp that is the job folder itself), holds a `.git` (the product asks this before the run; a `.git` that another process makes in a shared `/tmp` above the cover is nothing the unit shows, and made the earlier form of the check fail the tests at random) |
| wall time | 0.8 s (a first run of the same harness: 3.6 s) |
| outcome | finished, 12 models. With visibility `list`: gpt-6.2-sol, gpt-6.2-astra, gpt-6.1-terra, gpt-6.1-sol, gpt-6-astra, gpt-6-sol, gpt-6-luna, gpt-5.6-sol, gpt-5.6-terra, gpt-5.6-luna |
| files of the job's `CODEX_HOME` before the read | `auth.json` |
| files the read left, not there before | **`models_cache.json`**. No `config.toml`, no `environments.toml`, nothing under `sessions` |
| the processes of the unit during the read (sampled about every 50 ms from the control group of the unit, 63 samples; the harness records for each name its first sample, its parent pid and the real path of its executable, when they could be read) | `(sh)`, `(sd-userns)`, `codex`, `lsb_release`, `cut`, `tr` (`sed` in an earlier run) |

**Item 4, under the judge's reading (part 15.12 and the ruling on `sd-userns`): met.** The harness (phase `names`) holds the rule in `ListReadProcesses.cs` and fails the real run, naming the process, when it breaks it:
- allowed at any time: `codex`, `sh`, `lsb_release`, `cut`, `sed`, `tr`; a name in brackets is the same name;
- `(sh)` and `sd-userns` (also `(sd-userns)`) only in samples taken BEFORE the first sample that shows the unit's main process as the wrapper `sh` or as `codex` (the unit start); at or after it they fail. There is no parent rule: `(sh)`, `sh` and `codex` are one pid that changes by exec;
- for `sd-userns`, when they could be read, the executable must be a program of systemd and the parent the unit's main pid; when they could not be read the time rule alone decides.
Result of the real run: the first sample that shows `sh` or `codex` is sample 8; `(sh)` and `(sd-userns)` were seen in samples 0 to 3, before it; the parent of `(sd-userns)` was the unit's main pid (3537229); its executable could not be read (the process is gone before the read); `(sh)` was read once as `/usr/lib/systemd/systemd-executor`; `codex` ran from the release binary; `lsb_release` ran from `/usr/bin/dash`, `cut` and `tr` from `/usr/lib/cargo/bin/coreutils`. **Processes that break the rule: none.** The time rule has its own tests (`AListReadProcessRuleTests`: before is allowed, after fails, the name is shown, the exe and the parent are held when readable, no parent rule). `models_cache.json` was left in the job's `CODEX_HOME`, by decision (15.12), and is not removed.

### 1.2 The three names

Each row: the name as asked, the name as resolved, the admission of the preflight in the real unit, the answer of the two tool rows, and the tool report read with the closed grammar of A4-M1.

| asked | resolved | admission | rows | `tool-list` | `no-project-tools` | the tool report (`codex debug prompt-input`, flags of the launch) |
|---|---|---|---|---|---|---|
| `terra` (the default) | `gpt-6.1-terra` (newest of its family with visibility `list`) | **Admitted**, 2.9 s | 28 of 28 held | holds 11 of the 11 known names, none outside, the same in the worktree and in the stand-in project | holds | read whole; 11 names; none absent |
| `gpt-5.6-terra` (a full name) | `gpt-5.6-terra` (as given, never replaced by a newer one) | **Admitted**, 2.5 s | 28 of 28 held | holds **9** of the 11 known names, none outside; the model does not have `functions.request_user_input_async` and `clock__curr_time` | holds | read whole; 9 names; those two absent |
| `astra` (a family) | `gpt-6.2-astra` | **Refused**, 2.5 s | 27 of 28 held; red: `tool-list` | holds **1 name outside the known set of 11: `functions.send_message_to_user_async`** (the read was whole, 12 names) | holds | read whole; 12 names; 1 outside |

What the third row means for the product, said plainly: with the rule of entry 2.2 a coder whose family is `astra` resolves to `gpt-6.2-astra`, which shows a tool nobody measured.
The entry ends `model-unavailable` at its first launch, the notice `coder-tool-unknown` names the tool, and the job goes to the next entry. So **today `astra` cannot be a coder on
codex 0.171.0** until a person looks at `functions.send_message_to_user_async` (what it reaches, where it runs) and the judgement is extended by an addendum (A4-M4). `terra` and
`gpt-5.6-terra` work. This is a finding for the lead and the owner; it is not a fault of the code.

The report of `gpt-5.6-terra` is the first recorded report that shows which two tools it lacks (the open point J37 of the judgement).

### 1.3 The two recorded reports (the fixtures of d5)

`src_mcp/tests/fixtures/codex-tool-reports/gpt-6.1-terra.json` and `gpt-5.6-terra.json`: only the `additional_tools` item of the report, **byte for byte as the CLI printed it**
(the harness compares the raw text of the saved item with the raw text of the item it was cut from: equal for both). No token, no path of the home. Both are read whole by
the closed rule: every `###` line of the description of `functions.exec` is ``### `name` `` and nothing else, `functions.exec` is there once, and no other item holds a `tools` member.

## 2. The red-first record (d1)

**Base:** commit `62c6878c` (the plan stop), the code with the exact list of 11 names and no family rule. A second worktree of that commit was made; into it were copied the test
infrastructure (`CodexVerbs.cs`, `CoderRig.cs`, `CodexBed.cs`, the two fixture folders) and the three classes of d2 to d6 that use only what the base has
(`ACodexNamesNeedNoEditTests`, `ACodexModelNameIsResolvedAtJobStartTests`, `ACodexCoderPreflightTests`), plus one file of extension members that lets those tests read the
members the base does not have (they answer "not there" on the base). Run through the MTP executable under the shared lock.
`ACodexModelListTests` (83 tests) calls the new product types (`CodexModelShape`, `CodexModelList`, `CodexToolList.Read` and `Judge`): on the base it does not compile, which is
its red; it is green on the branch.

### 2.1 Counts

| class | tests | red on the base | green on the base (guards) |
|---|---|---|---|
| `ACodexNamesNeedNoEditTests` | 20 | 4 | 16 |
| `ACodexModelNameIsResolvedAtJobStartTests` | 37 | 26 | 11 |
| `ACodexCoderPreflightTests` | 66 | 15 | 51 |
| `ACodexModelListTests` | 83 | does not compile (new API) | - |

(The job class was 31 red in the first run of the base; five of its tests were guards that were written with the family names alone as keys of the model map, so the fake could
not answer on the base for a wrong reason. They now carry both keys, and run green on the base: `V32_AnItemOfTheStream...` three rows, `V32_TheRolloutCheck...`,
`V33_ANewToolOnAFixTurn...`.)

### 2.2 Red on the base, each for its own reason

| the test says | the base does |
|---|---|
| `terra` is resolved to `gpt-6.1-terra`; the argv holds `-m gpt-6.1-terra` (V30 rows of the job class) | the argv holds `-m terra` |
| `gpt-6.3-terra`, `gpt-7-astra`, `gpt-6.10-sol` need no edit of the file; a shaped name written on the important list is routed (V31) | they are refused `route-invalid` ("not in the list of names known for the codex CLI") |
| a tool list of 11, 10, 9 and 1 names passes, and the row names the absent known tools (V33 `fewer`, `nine`, `one`, and 11) | a list of 9 of the 11 is refused: 2 names are missing |
| a list that cannot be read, or holds no name, in both reads is `model-unavailable` and fails over (V33, V32) | the job ends `coder-boundary`, where no boundary broke, and the next entry is not tried |
| a tool outside the known set, before the first launch, is `model-unavailable`, the next entry is launched, one notice `coder-tool-unknown` for each refused entry (V33) | the job ends `coder-boundary`; no notice |
| the list of models is read in an empty folder (V30), and a unit refused, a write stopped and a unit that does not stop during that read end the job `coder-boundary` (V32) | no list is read at all |
| a family with no model, a full name the list does not hold, an unreadable list, the deadline, an answer over the limit: `model-unavailable`, zero launches (V32) | no list is read; the CLI is asked for `terra` |
| the recorded report of `gpt-5.6-terra` (9 names) is admitted; the nested inventory in another form is not whole (V36) | the 9 names are refused; and a list of 4 known names read from text in another form is the one the base refuses only because it is not the 11 |
| a name that only the worktree read holds is in none of the places (V34, A4-M2); an allowed-characters name with the shape of a secret is not shown | the base shows no name at all, so the rows that ask for the counts and the shown names are red |

### 2.3 Green on the base: the guards, and the mutation of the new code that turns each red

Each was run with its mutation applied alone to the committed code, built, run, and the mutation reverted (`git checkout`). The result is the run of that one test (or class of rows).

| guard (a test that is green on the base) | mutation of the new code | result |
|---|---|---|
| `V36_Guard_TheNestedInventoryInAnotherForm_IsNeverAdmitted_WhateverTheWordOfTheStop` (an inventory in another form is not admitted) | `CodexToolList.Read` takes any list that is not empty as whole (`new ToolRead(names, names.Count > 0)`) | red (1 of 1) |
| `V33_AnOutsideNameInOneReadOnly_IsTwoReadsThatDiffer_TheBoundary_NeverModelUnavailable` (two reads that differ end the job as the boundary) | the two reads are not compared (the branch of `Judge` that compares them can never be taken) | red (1 of 1) |
| `V32_AnItemOfTheStreamThatNobodyMeasured_...` (3 rows: `web_search`, `collab_tool_call`, an unmeasured type) and `V32_TheRolloutCheck_...`: zero launches of the next entry | the boundary stop of a turn is mapped to `model-unavailable` (`ReadAsync` gives the stop of the turn check the word `model-unavailable`) | red (3 of 3 rows run) |
| `V33_ANewToolOnAFixTurn_...` (and `V33_ANewToolOnTheRepairTurn_...`): a new tool on a later launch ends the job | the mapping of the first launch is used for every launch (`StopOfLaunch` defaults to `firstLaunchOfTheEntry: true`) | red (1 of 1) |
| `AClaudeId_IsNeverAcceptedByItsShape_AndTheAgyListDoesNotChange` (5 rows) | the shape rule is asked for every vendor (the claude branch and the list branch both accept the codex shape) | red (2 of 5 rows: the shaped codex name for claude and for agy) |
| `TheImportantList_IsComparedByTheWrittenName_NotByWhatAFamilyResolvesTo` | the name is resolved before the list is compared (`Named` takes a shaped name for its family) | red (1 of 1) |
| `AProgramThatPrintsHostileText_CannotWriteIntoARow_AndNoRowPrintsAToken` (a secret-shaped name made of allowed characters reaches no sentence) | the redactor is not asked (`CanBeShown` keeps only the character rule) | red (1 of 1) |

The mutation of the second row was first written as `else if (false)`, which the compiler refuses as unreachable code (the build failed and the test ran on the old binary and
passed): it was rerun as a mutation that builds, and that run is the one in the table.

## 3. The suite

Full MCP suite through the MTP executable, under the shared lock, on the tree of the second commit, before its last test-only fix: **17993 tests, 17829 passed, 161 skipped,
3 failed**: the two rows of `ACoderFailsOverAcrossVendorsTests.D1_...` (a test of mine that read a launched name where it wanted the written one; fixed, and the class then ran
40 of 40) and `ARunThatDiesIsRecordedTests.AServerStoppedByASignal...(signal: "HUP")`, the known load flake. Run alone, that flake failed as well (1 of 3 rows of its test).
The full-suite figure before this task was 17818; the 175 tests more are those of this task.

The route tests that named `codex/terra` or `codex/astra` and changed, and why: with the family rule the CLI is launched with the name the family resolves to
(`gpt-6.1-terra`, `gpt-6.2-astra`), and FakeCli answers by that name. The route entries, the attempt records (`model` is the name as asked) and the notices still say `terra` and
`astra`. Changed: `ACodexCoderFixTurnsTests`, `ACoderFailsOverAcrossVendorsTests`, `ACoderFixLoopThroughTheSeamTests`, `ARoleFailsOverTests`, `AToolchainFileOfTheWorktreeTests`,
`AScoutAndJudgeScenariosTests`, `AUnitWrittenEntriesTests`, `APassTenShouldsTests`, `TheRolesCompleteEpicAcceptanceTests`, `AJudgeRunTests`, `ACodexCoderBoundaryTests`: the model-map
keys and the launched-model expectations, nothing else; none deleted.
