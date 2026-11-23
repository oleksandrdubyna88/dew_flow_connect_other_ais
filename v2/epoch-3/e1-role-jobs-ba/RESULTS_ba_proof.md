# Results — epoch 3 E1 s5, the live proof of the BA role

> Written 2026-11-23 by Claude Sonnet 5.5 (story s5-live-proof), at branch `feat/v2-ep3-e1-role-jobs`, head `3150d7de` plus the acceptance commit.
> **State: t2 is done. t1 is NOT complete: one real run was paid and was thrown away by a fault of the person who ran it (section 2). Nothing was written to `v2/epoch-3/e2-coder-one-task/stories/`.**

## 1. t2 — acceptance on the real binary (FakeCli only, no paid call)

`src_mcp/tests/TheRoleJobsEpicAcceptanceTests.cs`: a real `coai-mcp` on real stdio, the real detached runner (`setsid` launcher), the real BA role (plan check, repair loop, atomic publish);
FakeCli is the only double and answers every CLI by model from one map.

| Claim | Test | Result |
|---|---|---|
| refusal, limit, crash each go to the next model: 3 switches, 3 log lines, 3 notices | `D1_ARefusal_ALimit_AndACrash_EachGoToTheNextModel_...` | passed. Route fable, opus, sonnet, haiku (all claude): attempts `refusal`, `limit`, `crash`, then success. Exactly 3 `role-model-switched` notices (subjects `<jobId>/1..3`), exactly 3 runner log lines `switched A -> B after <stop>`, 0 `role-job-failed`, the plan of the 4th model written (3 task files and `ba-manifest.json`). |
| the MCP server is killed in mid-job; the job ends succeeded | `D2_TheMcpServerIsKilledInTheMiddleOfAnAttempt_...` | passed. Server 1 killed (whole tree) while the 2nd model sleeps 6 s; server 2 reads the same job: `succeeded`, `generation` 1, attempts `crash`, success, files written, no `role-job-lost` notice. |

Honest limits of t2: the limit is a 503 high-demand throttle, not the recorded spend-limit envelope (a spent limit also skips the vendor's other models; see the test remarks). All four models are claude, so the test proves
the switching, not a change of vendor (the in-process `ARoleFailsOverTests` cover codex). The runner is started with `setsid`, which hands it the FakeCli variables by inheritance; the `systemd-run` road is proved by
`ARoleToolsOverStdioTests` with the probe role and by the t1 run (launcher `systemd-run`). Skipped on Windows.

Tool docs: `ARCHITECTURE.md` (section "Role jobs and the BA") and `README.md` (section "Roles that run as jobs") name `start_role`, `role_status`, `role_cancel`, `validate_work` and `--role-delta`. The `start_role` tool
description in `Tools.cs` said "today: probe" and now names `ba` too.

Whole MCP suite (MTP executable `CoaiMcp.Tests`, never `dotnet test`): 15 391 tests, 15 236 passed, 154 skipped, **1 failed** in the full run (7 min 13 s):
`HeldFileInheritableOnLinuxTests.ATakeWhoseStreamCannotBeMade_ClosesItsDescriptor_AndLeavesTheFileFree` (a descriptor-leak probe). The test is unrelated to this change; it passed 2 of 2 when run alone straight after
(6 of 6 tests of its class), so it looks like a flake caused by another process on the machine (another coder was building at the same time) — not proved.

## 2. t1 — the real BA run

Setup: scratch data dir (`.../scratchpad/ba-proof/data`, not a real data folder), `COAI_ROLES_MODELS` ba = [fable, opus] on the claude CLI, `COAI_ROLE_CLAUDE_CLI` = the real CLI, launcher `systemd-run`,
job started through the real binary over stdio (`start_role role: "ba"`, `taskFile: v2/epoch-3/e2-coder-one-task/epic.yaml`, then `role_status` until the end). The epic had no stories (checked before the start).

### Run 1 (paid) — job `r20261123033208-5932170c`

| Item | Value |
|---|---|
| route | `claude/fable`, `claude/opus` (launcher `systemd-run`) |
| attempts | 1: `fable`, stop `tree-changed`; `opus` was never tried (`tree-changed` is a no-failover stop) |
| end state | `failed`, stop `tree-changed` |
| wall time | 183.9 s from `start_role` to the end (ledger: 182.8 s) |
| tokens | 384 472 in (296 058 cached), 19 312 out |
| cost | **2.2462 USD** (the CLI envelope's `total_cost_usd`, in the usage ledger of the scratch folder) |
| files written | none |
| validate_work on the written folder | not applicable: no folder was written |

**Why it failed — our own fault.** While the model worked I edited and added files in this working tree (`ARCHITECTURE.md`, `README.md`, `src_mcp/src/Tools.cs`, the new acceptance test). The role runner compares the
tree around every attempt, saw the change, withheld the answer and ended the job. This is the guard working as designed (a read-only role must not run on a tree that changes), and it is a lesson for the proof:
**do not touch the worktree while a BA job runs.** It is not a defect of the BA or of the prompt.

### What the thrown-away answer was worth (free check)

The model's answer is kept in the job folder (`attempts/1/turn-0.txt`, 23 909 bytes). I replayed it, at no cost, through the same `BaJudge` the job uses (a temporary test, not committed): **verdict `Valid`,
`Accepted = true`** — a valid plan at turn 0, zero repair turns (7 stories, 24 tasks). So run 1 would have been a pass of d1 with 0 repair turns had the tree stayed still.
This is a replay of the checker over a stored answer, **not** a published BA result, and it is not counted as the proof.

### Run 2 — NOT done, on purpose

The rule of this story: at most two real runs, the second only after a recorded prompt change. No prompt change is justified (the answer was valid at turn 0), so a second run would have been a plain retry that the
rule does not allow, and I did not take that decision for the owner. Cost of one more run is expected to be about the same as run 1 (about 2.2 USD on fable).

**To finish t1** (one paid run, cost about 2.2 USD): from a clean tree, with nobody editing the worktree, run the driver from the scratchpad
`node .../scratchpad/ba-proof/drive.mjs <scratch data dir> ba <worktree> v2/epoch-3/e2-coder-one-task/epic.yaml - <log>` (it sets the route and the CLI), then `validate_work` on `v2`, commit the written stories as
`docs(v2): epoch-3 e2 stories written by the BA role (live proof)`, and run `coai-mcp --role-delta <jobId> --at HEAD`.

## 3. Orchestrator measure (d2)

Not taken. There is no BA job that succeeded and no manifest, so `--role-delta` has nothing to read. The real measure (`changedShare` below 0.20) is taken only after the plan gate, at the commit the gate passed at;
right after the commit of the written stories it must read 0.00. **Pending.**

## 4. Spend

| Call | USD |
|---|---|
| BA run 1 (fable, claude CLI) | 2.2462 |
| any other paid call | 0 (no exploratory claude/codex call; the free probe job and the FakeCli suites cost nothing) |
| **Total** | **2.2462** |

## 5. Honest limits of this proof

1. d1 and d2 of t1 are open: no published BA plan yet, so no `validate_work` run on a written folder and no `--role-delta` number.
2. The only evidence that the real model's plan is valid is the free replay of one stored answer through the checker; it is one sample, from one model (fable), at one prompt.
3. A running BA job is sensitive to any change in the worktree, also by the owner's own editor or another agent's build in the same tree; in practice the BA needs a tree nobody touches for about 3 minutes.
4. `role_status` shows `attempts` only when an attempt ends; during an attempt `attempts` is empty, so a client cannot see which model is running except from `model` and the heartbeat.
5. The acceptance suite uses one vendor and a throttle for the limit (section 1).

## Run 2 (2026-11-23, main session, untouched tree) — the live proof

- Job `r20261123034558-89577cf7`, role `ba`, route claude/fable → claude/opus, launcher systemd-run, scratch data dir.
- **succeeded** on attempt 1 (fable): **6 stories, 23 tasks, 0 repair turns**, wall 157.9 s, **1.725 USD** (CLI envelope).
- Files written only under `v2/epoch-3/e2-coder-one-task/stories/` (+ `ba-manifest.json`), committed as `22c9c8d6`.
- `--validate-work v2`: valid — 5 epochs, 24 epics, 28 stories, 83 tasks, 0 errors, 7 notes.
- `--role-delta … --at HEAD` right after the commit: changedShare 0.00 (0 of 23). The pass criterion (< 0.20 after the
  orchestrator's review) is measured after that epic's plan gate — pending.
- Total spend of this story: 2.2462 (run 1, lost to a tree change) + 1.7251 (run 2) = 3.97 USD.

## The orchestrator measure (after the epoch-3 E2 plan gate, 2026-11-23)

- The plan gate on the BA-written e2 stories returned `proceed` with 4 findings, all accepted; the orchestrator made
  them as one added DoD item in each of 4 task files (s1/t2, s2/t4, s4/t1, s5/t1), commit `9a8bc821`.
- `--role-delta r20261123034558-89577cf7 --at 9a8bc821`: 23 manifest tasks, 4 changed, 0 deleted, 0 added →
  **changedShare 0.1739 — pass (below 0.20)**. The epic's BA criterion is met.
- Usability finding: `--at HEAD` resolves HEAD in the job's own repository path (here the epoch-3 E1 worktree), not the
  caller's; pass the exact commit. Follow-up: say so in the tool description, or resolve against the caller's tree.
