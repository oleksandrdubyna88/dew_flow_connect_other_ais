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
