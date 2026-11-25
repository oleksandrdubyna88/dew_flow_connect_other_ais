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

1. 82 tests of the `Leftovers*`, `Owner*`, `Liveness*` and `OwnerClaimVectors` classes read `.agents/conventions` (the mounted shared rules and their vectors), and the coder worktree does not populate submodules on purpose (`CoderWorktree`: "Submodules are not populated here"; `RESULTS_s2_check_boundary.md` run E mounted them by hand). The error is `DirectoryNotFoundException … /.agents/conventions/tools/fixtures/owner-vectors.json` and `node` failing to import the shipped classifier.
2. 5 tests of `AKeyStoreTests`, `AKeyPoolTests` and `AKeyHardeningTests` (shared instance, setgid 2770) fail inside the unit: `chmod` answers "Operation not permitted" in the unit's private user namespace.

So with this repository's own toolchain file no coder job can be green today, whatever the model does. t3 (the real coder run on the baseline task) needs a recorded change first: either the worktree gets the conventions mount (read-only, by coai) or the toolchain names a test selection that excludes both groups, and the setgid tests skip inside a unit. The narrowed toolchain of the proof clone was a clone-only stop-gap to be able to reach "green" at all; it was not used, because the launch ended at the vendor's limit.

### Spend

0.00 USD for this launch (and the dry run's FakeCli figures are fake and not money). Paid launches so far: 1 of 2, no model turn billed. The limit of 0.10 USD was not approached.

### Also seen (not a result of this task)

`HeldFileInheritableOnLinuxTests.ATakeWhoseStreamCannotBeMade_ClosesItsDescriptor_AndLeavesTheFileFree` failed once in a whole-suite run and passed alone (see t2).
