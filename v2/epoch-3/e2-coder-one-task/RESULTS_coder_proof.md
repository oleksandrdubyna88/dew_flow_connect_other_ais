# Epic e2-coder-one-task, story s6 — the proof: t2 (epic acceptance, no paid call) and t5 (the pool-key proof)

This file holds numbers, ids, hashes and command results only. It quotes no key, no token, no environment and no file content of the key folder.

## t2 — epic acceptance on the real `coai-mcp`, FakeCli the only double (2026-11-25, head f19a7fcb plus the new test)

Test: `src_mcp/tests/TheCoderEpicAcceptanceTests.cs` (2 tests, in the serial `fakecli-env` collection; skipped on Windows and where there is no systemd user manager). It starts the real `coai-mcp` binary of the build over real stdio (`COAI_ROLE_LAUNCHER=setsid`: the detached runner is its own process), with the real OS layer (a `systemd-run --user` unit for the coder and one per check step) and the real gate. FakeCli stands in for claude (the coder) and for codex and gemini (the reviewers); no model is called.

| dod | What the run proves | Result |
|---|---|---|
| d1 | `open` + `review_plan` (committed plan file) + `resolve` on branch `feature` end at stage CodeReview with verdict `proceed`. `start_role` role `coder` on the fixture task runs turn 0 (red), ONE fix turn (green): state `succeeded`, `green` true, `fixTurns` 1. Then `open` + `review_code` on `coder/<jobId>` (base ref `feature`, no scope sent) answers `proceed`; `resolve`; `status` of that branch says `planProceeded` true and `inherited.jobId` = the job. After the server is stopped the session file of `coder/<jobId>` holds exactly one round, stage CodeReview (no plan round on that branch), and the one of `feature` exactly one, stage PlanReview. | pass |
| d2 | The coder's list names `codex/gpt-5.5` and `claude/sonnet`; the route answered by `start_role` is `claude/sonnet` only and every attempt's vendor is `claude`. FakeCli recorded 2 launches in the worktree; exactly 1 carries `--resume` (the fix turn, with the session id of turn 0). The envelope's `filesChanged` equals `git diff --name-status <baseSha> <headSha>` (compared as sets of status and path; the diff is not empty). The main checkout is unchanged: same HEAD, same `git status --porcelain --ignored` before and after, the only new ref is `refs/heads/coder/<jobId>`, and the envelope's `mainCheckout.changed` is false. | pass |

Runs: the two tests alone, 2 of 2 passed (10.9 s). The whole suite on this machine (low priority, another agent running): 16,459 tests, 16,302 passed, 156 skipped, 1 failed, 14 m 41 s. The one failure, `HeldFileInheritableOnLinuxTests.ATakeWhoseStreamCannotBeMade_ClosesItsDescriptor_AndLeavesTheFileFree` (a lock probe saw a descriptor held), passed when its class ran alone (6 of 6); it is in the files this change does not touch and it is recorded here as a flake of a full run, not investigated. `dotnet format whitespace --verify-no-changes` on the new file: clean.

What the assertions would see if the behaviour were deleted: without the plan round on `feature` the `review_code` on the coder's branch is refused (`PlanBindingTests`, same fixture); with no fix turn `fixTurns` is 0 and the launch count 1; a coder that wrote the main checkout would change the `--ignored` status and the envelope's `mainCheckout`.

## t5 — the pool-key proof with the real CLI (2026-11-25): NOT green; the one real launch was answered with the account's spend limit

**Short answer.** One real coder job was started with the published binary and a pool key. The real `claude` CLI (haiku) accepted the key and the vendor answered HTTP 429 "individual spend limit … weekly limit resets Nov 28, 4am (Europe/Paris)". No model turn ran: **spend 0.00 USD** (the CLI's envelope says `total_cost_usd` 0; the ledger line has `costUsd` null, tokens 0). The job ended `failed`, stop `limit`, never green. No second paid run was started. Every scan has 0 hits and the person's `.credentials.json` is byte-equal with the same mtime. The unit's properties were NOT captured (the unit lived about 3 s and my watcher missed it); see below.

### What was run

- Binary: `/home/jinx/coai-t3/bin/coai-mcp` (`--version` says `2.1.0-keys.1`, built from f19a7fcb). Data folder: `/home/jinx/coai-t3/data` and no other. Person sign-in switch: off (`--keys list` says so). Driver: a node script that speaks stdio like a client (`start_role`, then `role_status`) and gives the server a small environment (PATH, HOME, XDG_RUNTIME_DIR, DBUS address, COAI_DATA_DIR); nothing of any environment was printed. Launcher: `systemd-run` (the product's road).
- Settings written to the data folder's `settings.json`: the coder's list is `claude/haiku` only; `COAI_FIX_ROUNDS` 1. The route answered by `start_role`: `claude/haiku`.
- Repository of the job: a clone made for this proof, `/home/jinx/git/coai-v2-proof-clone` (branch `proof`, three commits above 37d82b6d, none of them in this repository). It holds (1) one task file `v2/epoch-3/e2-coder-one-task/stories/s6-proof-and-measure/tasks/t6-status-line/task.yaml`, valid for `validate_work` (kind simple-docs, complexity 1), whose one dod says the Status line of `KEY_POOL_SPEC.md` ("spec only, nothing built") must say the pool is built and merged on 2026-11-25 — the small honest change, one line of one doc — and (2) a narrowed toolchain: the test step runs one class (`--filter-class CoaiMcp.Tests.TheV2FolderIsAValidPlanTests`), because the whole suite is red in a coder worktree (next section). The main repository was not touched during the job.
- Job id **r20261125145201-10543769**, 2026-11-25 14:52:01–14:52:06 UTC. The coder's branch `coder/r20261125145201-10543769` is in the clone at the base commit (2ff0da04, no commit: the model never answered). Nothing of it is committed to this repository.

### Before and after

| | before | after |
|---|---|---|
| `--keys list` k-48f7cfb0c731 (claude-2) | launches 0, no `lastUsedUtc`, no cooldown | launches 1, `lastUsedUtc` 2026-11-25T14:52:03Z, limitStops 1, cooldown until 2026-11-26T14:52:06Z |
| `--keys list` k-2b88f1669d85 (200usd) | launches 0, no cooldown | launches 0, cooldown until 2026-11-26T14:52:06Z (the same instant) |
| `.credentials.json` | sha256 `dd06fa35bc36259aa7c9eac3d722680965044f9368002166eea134d51213dbf7`, mtime 2026-11-25 14:24:15.767769116 +0100 (1795613055), 1959 bytes | the same hash, the same mtime, the same size |

Where the key id is seen: the ledger line of `usage.jsonl` (`kind` role, provider claude, model haiku, role coder, `keyId` k-48f7cfb0c731, tokens 0, cost null, outcome `exit`); the attempt in `job.json` and in `role_status` (`attempts[0].keyId` k-48f7cfb0c731, stop `limit`); the log line "switched key 1 of 3 after a limit on k-48f7cfb0c731". The job's reason says `no usable claude key (0 usable, 2 cooling, …); the earliest is free at 2026-11-26T14:52:06Z`, and one notice `claude-keys-exhausted` was written.

### Scans (`coai-mcp --keys scan --path <folder>`), run after the job, every one "no hit", 0 files not readable

data folder (17 files); log folder (2); job folder `roles/jobs/<id>` (6); the coder worktree (4,335); the job's coder folder `coder-jobs/<id>` (7), its `coder/home` (7: `.claude.json`, `policy-limits*`, a backup, the session `.jsonl`, one plugin store file), `coder/tmp` and `coder/vartmp` (0); the scratch folder holding the driver's output, the server's stderr and the unit captures (14). The job has no `attempts/<n>/claude-home` (the coder's home is `coder-jobs/<id>/coder/home`). `coder/home` holds no `.credentials.json`: the person's sign-in file was not bound or copied there.

### What this proves, and what it does not

- Proved with the real CLI 2.1.332: the pool key reaches the child as the sign-in (the vendor knew the account and its spend limit, so the key was accepted), a real vendor limit is read as `limit`, the ledger and the attempt carry the key id, `lastUsedUtc`, `launches` and `limitStops` moved, the person's sign-in file was not touched, no scanned place holds a key.
- NOT proved: a green job (no model turn ran), the second key taking a later job (both keys cool until tomorrow), and the unit's properties: the watcher found the unit's `-pre` preflight unit three seconds after it appeared and the coder unit was gone by then (`--collect`), so `systemctl --user show` answered defaults. Open: capture the properties in the first 0.2 s, or have coai write them into the job record.
- A finding in the pool, for the owner: both keys carry the same `account` label (from the import), and a limit on one put BOTH in cooldown to the same instant, so the "key switch" of the spec (run the same model on the next key) found no usable key and ended the job at once. If the key labelled 200usd is another account, it must be imported with its own `account` value, else a limit on one blocks the other for 24 hours. The message says the limit is an "individual spend limit" with a weekly reset on 2026-11-28 04:00 Paris; raising it is for the account's admin.

### The whole suite is red in a coder worktree (a dry run, no paid call, found it before the paid run)

A dry run through the real pipeline with FakeCli (the built binary, its own throw-away data folder, `claude/haiku` route, the person sign-in switch on, FakeCli standing in for claude) on a branch of the same clone, with this repository's own `.coai/toolchain.yaml`: the build step passed (host network, about 35 s); the test step ran in the unit, MemoryPeak 2.19 GB of a 6000 MB `MemoryMax`, and ended **red: 16,151 tests, 88 failed, 342 skipped** after about 11 minutes. The failures are of two kinds, both of the environment, none of the model:

1. 83 tests of the `Leftovers*`, `Owner*`, `Liveness*` and `OwnerClaimVectors` classes read `.agents/conventions` (the mounted shared rules and their vectors), and the coder worktree does not populate submodules on purpose (`CoderWorktree`: "Submodules are not populated here"; `RESULTS_s2_check_boundary.md` run E mounted them by hand). The error is `DirectoryNotFoundException … /.agents/conventions/tools/fixtures/owner-vectors.json` and `node` failing to import the shipped classifier.
2. 5 tests of `AKeyStoreTests`, `AKeyPoolTests` and `AKeyHardeningTests` (shared instance, setgid 2770) fail inside the unit: `chmod` answers "Operation not permitted" in the unit's private user namespace.

So with this repository's own toolchain file no coder job can be green today, whatever the model does. t3 (the real coder run on the baseline task) needs a recorded change first: either the worktree gets the conventions mount (read-only, by coai) or the toolchain names a test selection that excludes both groups, and the setgid tests skip inside a unit. The narrowed toolchain of the proof clone was a clone-only stop-gap to be able to reach "green" at all; it was not used, because the launch ended at the vendor's limit.

### Spend

0.00 USD for this launch (and the dry run's FakeCli figures are fake and not money). Paid launches so far: 1 of 2, no model turn billed. The limit of 0.10 USD was not approached.

### Also seen (not a result of this task)

`HeldFileInheritableOnLinuxTests.ATakeWhoseStreamCannotBeMade_ClosesItsDescriptor_AndLeavesTheFileFree` failed once in a whole-suite run and passed alone (see t2).

## The recorded change: submodules and the setgid tests in a coder job (2026-11-25, branch `feat/v2-ep3-e2-coder`, for s6 t3's second run)

The dry run above showed that no coder job on this repository could be green. The change makes the two causes disappear without touching the boundary beyond ONE read-only bind, and without a network.

**What changed.**
1. *The 83 convention-bound tests* (`.agents/conventions` is a submodule; a coder worktree has an empty folder there). coai now provides the submodule's content AT THE BASE COMMIT, offline (`CoderSubmodules`): `.gitmodules` and the gitlinks are read from the base commit with `git config --blob` and `git ls-tree` in the main checkout (never its working copy), the submodule's own git folder is found with `git rev-parse --git-path modules/<name>` (this repository's submodule is named `.claude/rules/shared`, and in a linked-worktree main checkout it is under `.git/worktrees/<name>/modules`), and the pinned tree is written (`read-tree` into an index file of its own, `checkout-index` into `<job folder>.mounts/m<n>`; hooks, fsmonitor, sparse checkout, global and system git config all off; every path of the tree validated, a link in it refuses the mount). The folder is a SIBLING of the job folder, because a unit binds the job folder writable. Every check unit and the coder's unit bind it READ-ONLY onto the worktree's empty gitlink folder: one more property, `BindReadOnlyPaths=<source>:<target>`. Nothing is put in the worktree. A job with a mount has a `submodules-readonly` preflight row (a write into the mount, and one at its source's host path, must be refused), a job with none has exactly E2's rows. `BoundaryJob.Problem` refuses a mount whose source is not under the mounts folder or whose target is not under the worktree (and not `.git`), and the admission refuses a target or source that is a link.
2. *A coder's diff may not touch `.gitmodules` or a submodule's path* (`CoderCommits`): refused before anything is staged when the folder of a gitlink is no longer empty (git itself ignores files written there), and after staging for a changed `.gitmodules` or a deleted gitlink; the job fails `coder-commit`, naming the path, nothing committed.
3. *The 5 key-store shared-instance tests* skip when this process cannot give a folder the setgid bit, decided by trying the chmod and reading the mode back (`SetgidSupport.TryApply`), not by a variable. Inside a check unit the chmod is refused ("Operation not permitted"); on the machines that can, they run.
4. *The unit record.* Every coder launch writes `attempts/<n>/unit-<part>.json`: the properties the unit was started with (names and values; Environment-like ones by name only, `(withheld)`), the network state with its reason, the preflight rows and the submodule mounts. The pool-key proof (t5) could not read the unit's properties because the unit is gone in seconds; now the record has them.

**Red, then green.** The new tests (42: `ACoderSubmodulesTests`, `ASubmoduleMountInTheUnitsTests`, `ACoderCommitProtectsTheSubmoduleTests`, `ACoderUnitIsRecordedTests`, `TheSetgidProbeTests`; real git, real systemd units) were run on the stubs first: 31 failed and 11 passed (the 11 are the tests of behaviour that must NOT change: no mount means no row, an ordinary commit, the probe's own cases). After the implementation: 42 of 42 passed. Two of the tests were wrong and were corrected from the run, not the code: a degrade that dropped a property's value but left its `-p` (the unit then did not start, which refused the job for the wrong row), and one that took away a property no row depends on on this machine.

**The unit's property list against E2's, element by element** (`ASubmoduleMountInTheUnitsTests`, the first two tests; the lists are those of `RESULTS_s2_check_boundary.md` t4 for the checks and t5 for the coder): with no mount the list is E2's, not one element more or less, in E2's order; with one mount it is E2's list with exactly one more element, `BindReadOnlyPaths=<source>:<target>`, placed after the worktree's `.git` entry, and the source appears in no writable bind. The dry run's record (`unit-c1.json`) shows the same on a real job: the coder's list is E2's plus that one line.

**The dry run, through the real pipeline** (the built `coai-mcp` over stdio, the detached runner, the real units, FakeCli as the only double, a clone of this branch with the submodule initialised offline, this repository's own `.coai/toolchain.yaml`, scratch data folders, `COAI_FIX_ROUNDS=0` so a red round is not repeated). Result: job `succeeded`, `green` true, `checksState` green, `fixTurns` 0, one round. Build step 52.6 s (exit 0). Test step 565.8 s, exit 0: **16,508 tests, 0 failed, 346 skipped** (before the change, on an older head: 16,151 tests, 88 failed, 342 skipped). The 83 convention tests passed, the 5 setgid tests skipped. The coder's commit holds only the file the fake wrote; the mount shows in no diff. The preflight's `submodules-readonly` row held.
Three runs came before it, and what they found is part of the result: (a) a coder root under `/tmp` is on a tmpfs with less than the product's 5 GiB free-space floor, which made 31 more tests red (the Kimi and Vibe snapshot tests) — the root must be on a real disk, as `CoderWorktree` says; (b) the first run with the submodule on the real disk had 76 red, 71 of them "node could not be started": the dry run's stand-in `HOME` has no `~/.nvm`, and the unit gets node only when coai's `PATH` names an entry under the real home's `.nvm` (the product binds that folder read-only); with the stand-in home linked to `~/.nvm` the same tests passed; (c) the same run found two faults of this change itself, fixed before the green run: a raw recursive delete in `CoderSubmodules` (the repository's `TempDirsAreSwept` allows only `GitScratch.DeleteEvenIfReadOnly`) and a `"/tmp"` literal in a test (the leftovers guard).

**Road B, measured once (bind the main checkout's own `<main>/.agents/conventions` read-only), only to record why A is safer.** On the real main checkout of this repository: its working copy of the submodule is on `c598a11b`, while the base this branch pins is `cd19c173` — a unit would read RULES AND TEST VECTORS OF A DIFFERENT COMMIT than the base says, and which one changes with whatever the person has checked out, or has edited, in that folder. The folder's `.git` file points into the main checkout's `.git/modules` through a relative path (inside the unit it resolves to nothing, `fatal: not a git repository`, so a git command there answers nothing useful; and the main checkout's whole `.git` is already readable there, which A does not widen). B does show `node_modules` (untracked, ignored), which A never holds — nothing in the suite needed it (the 83 pass without), but a repository whose tests need an installed dependency inside a submodule would need a decision. A write into the mount was refused in both. A is the only road in which what the unit reads is a function of the base commit alone.

**Not done / open.**
- Nothing removes `<job folder>.mounts` (nor the job folder, nor the worktree): the retention sweep for coder jobs does not exist yet; the mounts folder shares that lifecycle.
- A submodule that holds a symbolic link, is not initialised in the main checkout, or whose pinned commit is not in its object store is not provided; the job goes on and the log and the unit record say so (`note`). Then its tests are red as before.
- A job started on a machine where the units cannot see node (no `~/.nvm` on the person's real `PATH`) still has the node-bound convention tests red; that is a property of the machine's toolchain, not of this change.
- Checks before the push, on the final tree: `dotnet format whitespace --verify-no-changes` on the three projects touched (clean); the whole mcp suite outside a unit, low priority: 16,512 tests, 0 failed, 156 skipped, 11 m 59 s; `dotnet publish src_mcp/src/CoaiMcp.csproj -c Release -r linux-x64 -warnaserror` (native AOT) succeeded, 1 m 37 s; `testLeftovers.test.mjs` 8 of 8; `adapter-check` OK; `plan-lifecycle` clean.

## t3 — the real coder job (claude/sonnet) on the baseline task (2026-11-25): GREEN, 0 fix turns, 0.0727 USD

**Short answer.** One paid coder job, started after the recorded change above and the cooldown fix (26e5024f), went green on the first turn: 2 files, +30 −1, the whole solution build and the whole test executable green in the check unit. The main AI used **45,446 fresh tokens** (baseline 323,072), **815 s active** (baseline 894 s) and accepted **1 finding** (baseline 3; code rounds 1 against 2). All three numbers are below the baseline. One paid run of the two allowed was used.

### What was run

- Binary `/home/jinx/coai-t3/bin/coai-mcp` `2.1.0-keys.2` (AOT of 26e5024f). Data folder `/home/jinx/coai-t3/data`; person sign-in switch off; settings: coder list `claude/sonnet` only, `COAI_FIX_ROUNDS` 3. Coder root and coder jobs folder on the real disk (`/home/jinx/coai-t3/coder-root`, `/home/jinx/coai-t3/coder-jobs`). Launcher `systemd-run`. Driver: a stdio client with a small environment (PATH with the nvm node, HOME, the user bus, the coai folders) and a cost guard that cancels the job at 3.5 USD (owner cap 4 USD); it did not fire.
- Repository: a clone of `feat/v2-ep3-e2-coder` at 26e5024f, `/home/jinx/git/coai-v2-t3-clone`, its submodule initialised offline, plus one clone-only commit ae72656c with the task file `stories/s6-proof-and-measure/tasks/t7-worktree-paths-same/task.yaml` (kind code, complexity 4, four dod items taken from BASELINE.md section 3). Nothing of the clone is committed here. The same base and task file are for the codex and agy runs of the comparison.
- Job **r20261125200645-562c2de4**, 20:06:45–20:16:32 UTC (584 s wall). Route `claude/sonnet`, one attempt, key k-2b88f1669d85 (200usd; claude-2 is still in its own cooldown). Envelope: `schemaValid` true, `fixTurns` 0, `green` true, `checksState` green; build 34.8 s, test step 530.9 s. `mainCheckout.changed` false.

### The coder's work

- `WorktreePaths.Same` takes the comparison from one private property: `OrdinalIgnoreCase` on Windows and macOS, `Ordinal` otherwise, the same rule as `WorktreeManager`'s guard. Two new tests: two real folders `Repo` and `repo` are not Same (skips on Windows and macOS); one folder is Same with a symbolic link to it and with a trailing separator.
- Checked by the main AI: the case test is RED against the base's `WorktreePaths.cs` (1 failed, 3 passed in its class) and green with the fix. The coder's own report says it ran no test; the check unit ran them.
- The coder's usage (ledger line): 15.5 s, tokens in 50,181 (cached 37,755), out 1,921, **0.0727 USD**. Paid in total by this task: 0.0727 USD.

### The gate (session e390ae84 of the 2.0 server, on the clone's coder branch)

| Round | Verdict | Reviewers | Findings | Accepted / rejected | Gate tokens (in / out) |
|---|---|---|---|---|---|
| plan 1 (the task file) | proceed | 1 of 1 | 3 (2 major, 1 minor) | 0 / 3 | 39,946 / 491 |
| code 1 | proceed | 4 of 4 | 1 (blocking: the module doc rule) | 1 / 0 | 161,713 / 573 |

The plan findings were rejected with reasons: a per-volume case probe is out of scope and would make `Same` disagree with the guard; Windows and macOS keep the old comparison; the link case is already in the second test. The accepted code finding (the knowledge-base rule: a code change updates its module doc) was done by the main AI as commit c8669198 on branch `t3-main-ai` of the clone (one paragraph of `research/module_server.md`). The task file had said "only the two files", which was the cause of the finding.

### The three numbers (BASELINE.md section 1, the same reader)

| Number | t3 | Baseline |
|---|---|---|
| (a) fresh tokens | **45,446** = input 72 + cache creation 29,952 + output 15,422 (33 responses) | 323,072 |
| (a) cache read | 5,389,757 | 12,619,098 |
| (b) active time | **815 s** | 894 s |
| (b) elapsed | 23.9 min (19:55:32 to 20:19:24 UTC) | 222.2 min |
| (c) accepted findings | **1** (plan 0, code 1) | 3 (plan 1, code 2) |

Window: from the main AI's write of the task file to the end of the code gate. The second window from the job's start only: 29,245 fresh tokens, 759 s active. The 584 s of the job's run are inside the active time: the main AI's events around it were less than 10 minutes apart.

### Limits, said plainly

- One task, one run; the noise between equal runs was 11–16 %. The token gap (7×) is far outside it; the active-time gap (9 %) is inside it.
- The task was easier for the main AI than the baseline's: BASELINE.md section 3 had already found the cause and named the files, and the baseline's window includes finding a cause from a failed remote run. The end is the code gate, not a merge on main (the baseline's elapsed time ends at `mergedAt`).
- The main AI here is the same model (`claude-opus-5-5`); its effort level was not set for this window.
- The ledger line of the coder's turn has an empty `job` field (the key id is there); the job id is found by time and key. A todo for the ledger.
- Not repeated here: the key scan after the job (`--keys scan`): data folder 30 files, job folder 10, coder worktree 7,275, coder jobs folder 411, the driver's folder 16 — every one "no hit". The coder's home holds no `.credentials.json`; the person's sign-in file was last changed at 18:21 UTC, before the job.

## The four follow-ups of the t3 run (2026-11-25, branch `feat/v2-ep3-e2-coder`, one commit each)

1. **The ledger's `job` field** — done. The coder's line (and a fix turn's) now carries the job id; test `EveryLedgerLine_OfARoleJob_CarriesTheJobsId`, seen red (`""`) then green.
2. **The check unit sees the coder's folder** — done for the claude coder (the E3 judgement's finding for E2). The check unit gets an empty read-only tmpfs over the job folder and binds only `home`, `nuget-packages`, `npm-cache` and `dotnet-home` on top; a new preflight row `coder-folder-hidden` proves it from inside before each job. Still open: the build step's host network; the other coder jobs' folders are visible read-only when the coder-jobs folder is outside `/home`. Described in `research/module_server.md`.
3. **The HeldFile flake** — a product cause found by reading, not by reproducing: the descriptor of an inheritable take was not close-on-exec during the take, so a process started by another test's thread meanwhile could keep the flock after a failed take. Fixed (close-on-exec until the take succeeded); the old test has not been seen to fail again, which is not proof. If it fails a third time, the cause is another one.
4. **The retention** — the plan only said "the retention sweep" (`EPIC_BUNDLE.md`, `RESULTS_coder_proof.md` above), without a rule. Implemented: the mounts of a coder job go when its work ends (not when a runner that lost the job ends); the coder job's own folder and mounts go with its record at the 30 days / 200 jobs of `RoleJobRetention`. **Not decided and not done:** what ends a coder WORKTREE and its `coder/<jobId>` branch (they hold the commit); a person's decision.

### After the security verify of the four follow-ups (2026-11-26, `RESULTS_e2_followups_security_verify.md`)

Done: M1 (a job folder must be its own real path; refused in both admissions), M2 (the key folder, the keys file and the person's sign-in file are hidden from the check unit, with rows `keys-folder-hidden` and `sign-in-not-in-unit`, and `KeysFolderProblem()` asked), S2 of the E3 judgement (the coder-jobs folder is covered in both units when it can be), the retention removes a coder job's folders BEFORE its record and unlinks links without listing their targets, the stale words and the docs errors, the empty coder folder row. **Still open** (todo items for E2 or its successors):
- The build step still has the host network (restore with the network, build without: a later story).
- S1, `PrivatePIDs=yes` in the check unit, was tried and taken out: the step's command becomes PID 1 of its namespace, which ignores a SIGTERM it has no handler for, so `ACheckBoundaryStopTests` (timeout, cancel) found the unit still running and the stop waited for `TimeoutStopSec`; the step's recorded pids are also numbers of another namespace. To do: a short stop timeout and the pids read from outside, or an init wrapper; then a preflight row and an answer for systemd older than 257.
- When the coder-jobs folder cannot be covered (it holds the worktree, the main checkout or the coder root, or is `/tmp`), only this job's folder is covered and another job's folder is as visible as it was.
- Nothing asks whether a job's units are gone before its folder is removed (R5): safe by distance only (30 days or 200 newer jobs).
- An exception of `CoderSubmodules.ProvideAsync` leaves a half-made mounts folder until the job's retention.
- Where a job's folders were made is not in its record (R4): two machines on one data folder, or a changed `COAI_CODER_JOBS`, leave them.
- What ends a coder worktree and its `coder/<jobId>` branch.
- The one window of the HeldFile fix (a child between its fork and its exec).

Second pass of the verify (M3, done): the key folder, the keys file and the person's home are resolved once where they are made, so a data folder or a home spelled through a link is hidden at its real path and the rows read the real path; the keys row no longer expands a glob character in a key path; a folder of mode 000 made by a check no longer keeps its job's record. Still open from that pass: S1 can be redone with `KillSignal=SIGKILL` on the unit (a stop is then immediate) and the step's pids read from outside; the fall-back cover (see above) wants, for E3, a refusal or a canary row.
- A task with files under `src_vs_code` is not checked by E2's toolchain file: it has no Node (extension) project until E4 adds one, measured first (the E2 code gate's finding 3, rejected for E2).


## t5 — the pool-key proof, second paid run (2026-11-26): GREEN, haiku, 0.0024 USD

**Short answer.** One real coder job (claude haiku) on a small doc task ended `succeeded`, `green` true, `fixTurns` 0, `schemaValid` true, on the pool key **k-2b88f1669d85** (200usd), with the person's sign-in untouched. Spend **0.00236 USD** (38,092 tokens in, 1,430 out, 31,435 of them cached; the cost guard was 0.08 USD). This is the second paid run of the two allowed; the first (above) was answered with the account's spend limit, and the change recorded since is the whole E2 follow-up chain (the ledger job id, the check unit without the coder's folder and with the keys hidden, the retention, M1/M2/M3). The other key (claude-2, k-48f7cfb0c731) is still under the vendor's weekly limit (resets 2026-11-28 04:00 Paris), so **the second job with the other key was not run** (d3): the vendor would have refused it, and the pool, as it should, did not offer it (`not usable now`, cooldown until 2026-11-26T14:52:06Z).

### What was run

- Binary `/home/jinx/coai-t3/bin/coai-mcp` **2.1.0-keys.3**, the native-AOT publish of head 964f037a (project `src_mcp/src/CoaiMcp.csproj`, Release, linux-x64, `-warnaserror`, Version 2.1.0-keys.3, exit 0, under the shared lock); the previous binary is kept as `coai-mcp.keys2`. Data folder `/home/jinx/coai-t3/data` only; settings: coder list `claude/haiku` only, `COAI_FIX_ROUNDS` 3 (the t3 settings are in `settings.t3.json`); person sign-in switch off. Driver `/home/jinx/coai-t3/t5run/t5-driver.sh` (the t3 driver with the guard at 0.08 USD, this task, a small environment for the server, no environment printed).
- Repository: the t3 clone `/home/jinx/git/coai-v2-t3-clone`, branch `t5-main-ai`: one clone-only commit aa3c8c66 on 964f037a with the task `…/tasks/t6-status-line/task.yaml` (kind simple-docs: the Status line of `KEY_POOL_SPEC.md` says the pool is built and merged) and a narrowed test step (the whole solution is built, one test class is run). Never pushed.
- Job **r20261126012559-2183e184**, 01:25:59 UTC, 106 s. The coder's commit 042af4bf on `coder/r20261126012559-2183e184`: one file, `KEY_POOL_SPEC.md`, 1 line changed (the Status line now says "built and merged into the E2 coder branch on 2026-11-25").

### d1 — the key is in the ledger line and the attempt; the unit has no bind of the sign-in

- The ledger line (`usage.jsonl`): kind role, claude, haiku, role coder, outcome ok, **`keyId` k-2b88f1669d85, `job` r20261126012559-2183e184** (the `job` field is filled now: the first of the four follow-ups). The attempt (`job.json` and `role_status`): `attempts[0].keyId` k-2b88f1669d85, stop empty.
- The coder's unit (`attempts/1/unit-c1.json`, 30 properties): `BindReadOnlyPaths` of the main checkout, the coder root, the version folder of the claude CLI and the resolver file; `BindPaths` of the worktree and of the coder's folder and its two temp folders; **no property names `.credentials.json`** (checked by a search of every name and value); `InaccessiblePaths=-/home/jinx/coai-t3/data/keys`; and **`TemporaryFileSystem=/home/jinx/coai-t3/coder-jobs:ro`** (the cover over the folder that holds every job's folder). Its preflight: all 17 rows held, among them `sign-in-not-in-unit`, `keys-folder-hidden` and `submodules-readonly`.
- The keys' counters (`--keys list --json`; the account's e-mail is not copied here):

| | before | after |
|---|---|---|
| k-2b88f1669d85 (200usd) | launches 1 (the t3 job), no cooldown | **launches 2**, **lastUsedUtc 2026-11-26T01:26:01Z**, limitStops 0, no cooldown |
| k-48f7cfb0c731 (claude-2) | launches 1, lastUsedUtc 2026-11-25T14:52:03Z, cooldown until 2026-11-26T14:52:06Z | unchanged |

### d2 — the person's sign-in file and the scans

`~/.claude/.credentials.json`: sha256 (first 12 hex) **515a95ac49dc**, 1,959 bytes, mtime 2026-11-25 22:20:38 (+0100) before and after, the same. `coai-mcp --keys scan --path` after the job: the data folder (43 files) no hit; the log folder (6 files) no hit; the worktree (7,302 files) no hit; the job's folder under coder-jobs (87 files, the coder's home among them) no hit; the driver's folder (18 files) no hit.

### The checks ran in tonight's check unit

The journal of the user manager shows the units `coai-check-r20261126012559-2183e184-pre-host`, `-pre-none`, `-s1` (the build, 1 min 26 s, 3.2 GB peak) and `-s2` (the test class: 3 tests, 0 failed): the preflight of both network states, then the two steps, from the binary made at 964f037a — so with the cover over the coder-jobs folder (here `/home/jinx/coai-t3/coder-jobs`, which holds no worktree, so the cover is that folder and not the fall-back), only `home`, `nuget-packages`, `npm-cache` and `dotnet-home` of the job's folder bound, the key folder hidden and the sign-in's real path hidden. The check unit's property list and rows are not written into the job's record (only the coder's are, in `unit-c1.json`); that they held is shown by the admission, which a failed row would have refused before the build. That is the one thing this run does not show as a recorded list: a todo to keep the check unit's record beside the coder's.

### Limits

One task, one doc line, one key, haiku. The vendor's real CLI accepted a pool key on stdin and never saw the person's sign-in; the proof does not say what a different vendor error mid-job would do (the first t5 run saw the spend limit and the pool cooled the key as designed).
