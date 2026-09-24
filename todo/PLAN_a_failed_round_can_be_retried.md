# PLAN — a failed round can be retried, and a round with nothing in it is never a pass

> Status: **plan only, nothing implemented yet, 2026-09-24.** Scope: the code gate's round lifecycle —
> `src_mcp/src/Server/PanelService.cs` (`OpenAsync`, `ReviewCodeAsync`, `RunStageAsync`, `status`),
> `src_mcp/core/Rounds/RoundMachine.cs`, `src_mcp/runners/Worktrees/WorktreeManager.cs`,
> `src_mcp/core/Commands/GateCommands.cs`, the gate rule in `.agents/conventions/common/coai-review-gate.md`.
> **Decisions taken 2026-09-24 by the operator: D1–D5 as recommended** (refuse an empty diff; no
> working-tree review here; `again: true`; a machine-local root for round trees; only ar1's two ideas).
>
>
> **Plan round (2026-09-24, `good_enough`, 3 of 3 reviewers, 16 findings — 14 accepted, 2 rejected):**
> the amendments are marked *(plan round)* below. The largest: the owner check is NOT a heartbeat —
> with D4's machine-local root every process that touches a round tree is on this machine, so a
> PID plus its start time is exact, and the heartbeat's thresholds, clock skew and sleep/wake (three
> findings) do not arise. Rejected: a lock pre-check that only reports (it would keep the round
> blocked) and a second deviation-tracking section (the DoD already requires rule text and
> `GateEnding` to agree).
>
> Related docs: [module_server.md](../research/module_server.md),
> [PLAN_multi_repo_and_uncommitted.md](PLAN_multi_repo_and_uncommitted.md) (working-tree review, `call_human`),
> [PLAN_an_empty_review_is_evidence_too.md](../research/PLAN_an_empty_review_is_evidence_too.md).

## The symptoms — four reports, one theme

All four are the same question asked from four sides: **after a round goes wrong, or has nothing to
review, does the gate tell the truth and let you try again?** Today it does neither.

1. **Vacuous proceed (operator, 2026-09-24).** With no committed change on the branch, `review_code`
   answers `proceed` with 0 findings. A developer who forgot to commit, or may not commit, is told
   "all clean" about code nobody read.
2. **A stuck worktree (operator, 2026-09-23).** After a failed reviewer the round's worktree stays
   behind and blocks every retry until someone cleans it up by hand in a console.
3. **Issue #490.** A worker session crashed mid-epic; after the restart the operator wanted a
   checkpoint code gate AND a final one, and the agent refused — "the code review gate is deferred".
4. **Branch `coai-ar1-addressable-rounds`** (91536a9b, 2026-09-08, never merged). If the reply to
   `review_code` is lost, the client cannot find ITS round again, and cannot tell it from another
   client's round on the same repo+branch.

## What the investigation found (read from main at ea3a5fa4; git behaviour reproduced in scratch repos)

### 1. Vacuous proceed — confirmed, and worse than reported

- **There is no empty-diff check anywhere on the path.** `ReviewCodeAsync` (`PanelService.cs:517-652`)
  refuses only "all roles off" (523-532) and "no scope" (538-542). `ContextAssembler.CollectAsync`
  (`ContextAssembler.cs:199-242`) diffs `merge-base..<sha>`; an empty list goes through
  `DiffShaper.Shape` (`DiffShaper.cs:30-71`) as `Text=""` without complaint. The only trace is a log
  line `diff 0 bytes over 0 file(s)` (`PanelService.cs:612-619`).
- **Reviewers are launched and paid for**, handed an empty "## The change" section. The prompts say
  "an empty findings list is a valid answer" (`prompts/architecture.md:29`, `conventions.md:32-33`), and
  the code default `CodeDefault = new(1, 5)` (`SessionState.cs:84`) passes even five blocking findings
  per role. `GateRule.Evaluate` → `RoundMachine.CompleteRound` (`RoundMachine.cs:183-187`) → `proceed`.
- **Only COMMITTED objects are reviewed.** `WorktreeManager.ResolveShaAsync` (`:40-46`) is
  `rev-parse <branch>^{commit}`. Three shapes reach an empty diff: (a) the branch equals or is behind
  `baseRef`; (b) the change is uncommitted; (c) only `DiffExclusions.Default` paths changed
  (`ContextAssembler.cs:12-30` — lock files, `bin/obj/dist/out/artifacts`, `*.min.*`, `*.map`).
  Binaries are NOT dropped — they are named (`DiffShaper.cs:36-39`).
  **And a silent sub-case (b2):** committed changes plus an uncommitted tail — the diff is not empty,
  the tail is never reviewed, and the answer says nothing about it.
- **The session then burns.** `resolve "[]"` with nothing pending → `Finish` (`PanelService.cs:2443-2446`)
  → `Stage = Done`; the real code on that branch can never be reviewed (see 3).
- **The product's own orders lead into it.** `GateCommands.GateEnding` (`GateCommands.cs:160-177`,
  #131) tells an epic to run "ONE review_code … with the previous epic's commit as baseRef … and
  commit the epic as ONE commit" — review BEFORE commit, which is shape (a).
- The only emptiness check in the product is the consultant's
  (`ConsultationService.cs:638-646`); `ContextAssembler.CollectWorkingTreeAsync` (`:110-127`) already
  reads the working tree without touching the index. `PLAN_multi_repo_and_uncommitted.md:136` already
  specifies "Clean tree — refused before any reviewer launches" inside a plan that is `call_human`.

### 2. The stuck worktree — confirmed, by two different mechanisms

Round trees live at a DETERMINISTIC path `DataDir/worktrees/coai-wt-{sessionId}-r{round}`
(`WorktreeManager.cs:51`, root at `PanelService.cs:64`), where `round = RoundsRunThisStage + 1`
(`PanelService.cs:1201-1211`) — so a retry of a failed round reuses the same path, and every counter
reset (escalation `RoundMachine.cs:253`, a human's Continue `:235`, a stage change `:325`) reuses `-r1`.

- **Literally locked (reproduced).** A `git worktree add` killed half-way — the 2-minute git budget
  (`WorktreeManager.cs:123-127`, killed by `ProcessLauncher.cs:348-363`) or coai-mcp dying — leaves
  `.git/worktrees/coai-wt-…/locked` = `initializing`. `PruneOursAsync` cannot clear it: `remove --force`
  refuses a locked tree (exit ignored at `:95`), `prune` skips it, the directory is deleted and the
  registration stays. Every later `add` at that path fails for ever:
  `fatal: '…' is a missing but locked worktree; use 'add -f -f' to override, or 'unlock' and 'prune'`.
- **Held open (Windows, reproduced).** A file held inside the tree (a Full-mode reviewer's child,
  Defender, an indexer) makes `worktree remove --force` exit 255; git drops the registration but the
  directory stays; the retry fails `fatal: '…' already exists` — exactly the two failed rounds in
  `research/RESULTS_bench_campaign_0_17_1.md:84-97,252`.
- **A cleanup failure MASKS the round.** `RemoveAsync` throws (`:78-85`) from the lease's
  `DisposeAsync` in `RunStageAsync`'s `await using`. After a successful round the state is already
  saved (`Pending`, `AwaitingResolve`, `PanelService.cs:1378-1390`), the answer at `:1430` is lost to
  the catch at `:1442`, and the caller gets `{"error":"git worktree remove: …"}` instead of its
  findings — then `review_code` refuses `Unresolved` and `status` cannot return the pending findings
  (`SessionAnswerFor`, `:2689-2708`). If the round body threw first, C# replaces that exception with
  the Dispose one and the real cause is gone.
- **Cleanup runs only on `open`, and hits other sessions.** `PruneOursAsync` (`:91-109`) is called
  only from `OpenAsync` (`:353-359`); its final `Directory.Delete` over EVERY `coai-wt-*` under the
  shared root is unguarded — an `IOException` escapes `open` itself (only `WorktreeException` is
  caught at `:356`). It also removes the RUNNING trees of other sessions: every Claude window runs its
  own coai-mcp on one data dir, and the `coai-review-` prefix+root guard (`ReviewWorktrees.cs:26-38`)
  was never given to round trees. The review-tree lessons in `module_server.md:2507-2533` (a leftover
  "wedged permanently", a 10-minute checkout budget, a machine-local root) were not carried over.
- Untested: the `initializing` lock, a held file, Dispose masking a verdict, `open` failing on
  `IOException`, `open` removing another session's live lease (`WorktreeManagerTests.cs` covers
  the three happy cleanups only).

### 3. Issue #490 — the session model allows exactly one code round

Not a server refusal on that day — the logs show none. The worker agent had settled "1 gate per epic
= one plan round + one code round" and refused a checkpoint round because running it would end the
session and forbid the final one. It is a real limit:

- `CodeDefault = new(1, 5)`: **one** code round; a first code round is always terminal (`revise` is
  unreachable), and `Resolve` then sets `Done` (`RoundMachine.cs:326`; reply "The code stage is
  complete. This session is done.", `PanelService.cs:2581`).
- `BeginCodeRound` refuses `Done` with "this session is complete; open a new one"
  (`RoundMachine.cs:139`) — but `open` is idempotent (`PanelService.cs:338-380`, `Load ?? new`), so
  **that door does not exist** for a branch. Documents have one (`newReview`, `RoundMachine.cs:127-130`).
- Agents already work around it: on 2026-09-22/23 ten branches `review/claude-cli-freshness-e1s1…e4s4`
  were cut for ONE feature purely to get fresh sessions.
- The rule text never says a code round closes the session: `coai-review-gate.md` step 5 says "same
  `resolve` duty, same loop" where no loop exists; `GateEnding` says "ONE review_code".
- A LOST reply after the round saved `AwaitingResolve` is a second dead end: `resolve` needs a
  decision per finding index, and `status` returns counts only (`ServerJsonContext.cs:53-61`). One such
  session is sitting now: `b76e3924` (orchestrator `fix/host-cli-step-sudo`, 23 pending since 18:57Z).
- A crash MID-round is safe on main: state is saved only at the end, and `SweepOrphanedRounds`
  (`SessionStore.cs:434-470`) marks the dead round `interrupted`.

### 4. `coai-ar1-addressable-rounds` — the problem is real, the branch is not mergeable

Nothing of it is on main (`RoundLocator`, `SessionClaim`, `reserve_round`/`run_round`/`round_status`,
`RoundsDb` reservations + a `session_commits` outbox, attestation). It is 1178 commits behind, its
schema steps collide with main's (`Schema.cs:31-36` — it would need 15+), it predates document
sessions and the three-part key, and it makes the database mandatory where main keeps it a
best-effort projection (`PanelService.cs:2537-2544`). **It does not fix #490**: `run_round` still goes
through `BeginCodeRound` and meets `Done`. What it adds that matters: reading back a lost answer, and
one mutating call per session at a time.

## Decisions (the operator's — all five taken as recommended on 2026-09-24)

| # | Question | Recommendation |
|---|---|---|
| D1 | An empty diff: **refuse** (no round, no session change, zero launches) or record a **`nothing_to_review`** verdict? | **Refuse**, with a sentence that says WHICH empty: branch = base, N uncommitted files, or only excluded paths (named). The product's rule is "a stage nobody serves is a refusal". |
| D2 | Review the **working tree** automatically (Part 1 of `PLAN_multi_repo_and_uncommitted.md`)? | **Not here.** It is that plan's `call_human`; this plan only refuses and says what was not reviewed. |
| D3 | More than one code round per session (#490): **B** `review_code(again: true)` re-opens `Done` → `CodeReview` only if HEAD moved; **C** `interim: true` checkpoint rounds that never close the stage; **D** `open(fresh: true)` archives the session | **B.** Smallest change, keeps `open` idempotent, the HEAD-moved guard stops the tenth-round loop. |
| D4 | Round trees' storage root: keep `DataDir/worktrees` (may be a NAS) or move to a **machine-local** root like `ReviewTreeRoot.Default`? | **Machine-local.** A linked worktree on a network drive is the review-tree lesson already paid for. |
| D5 | `ar1`'s `reserve_round`/`run_round`/`round_status` — needed now (several clients on one repo+branch)? | **Not now.** Take its two ideas that fix today's dead ends (S3b, S4); keep the branch as reference. |

## Stories (one epic; one gate session for the whole epic, per the operator's ruling)

**S1 — nothing to review is said, never passed.** *(D1, D2)*
- After `CollectAsync` (`PanelService.cs:548`), `collected.Files.Count == 0` throws `ContextException`
  (already caught at `:1442`; no state change, no round recorded, the lease is released). Better: run
  the check BEFORE `worktree add` so no tree is made for nothing.
- The refusal names the case: branch equals or is behind `baseRef`; N uncommitted files (reuse
  `CollectWorkingTreeAsync` when `branch` is the checkout's HEAD); only excluded paths changed (list
  them from a numstat without excludes).
- (b2) A `proceed` over a branch whose checkout has an uncommitted tail says so: "N uncommitted file(s)
  were NOT reviewed".
- `GateEnding` orders "commit, THEN review_code" for every split mode.
- `review_code`'s tool text says it reviews COMMITTED changes only.
- *(plan round)* An unresolvable `baseRef` (missing, not fetched, shallow) is its own refusal naming
  the ref, never an unhandled exception and never "empty". The working tree is consulted only when
  the checkout at `repoPath` has HEAD at the SAME sha the round resolved; otherwise nothing is said
  about uncommitted files rather than something wrong.

**S2 — a round's worktree can never block the next attempt.** *(D4)*
- `WorktreeLease.DisposeAsync` never throws: a failed removal becomes a notice and a deferred cleanup,
  so the verdict and findings reach the caller and a body exception is never replaced.
- A path per ATTEMPT (`coai-wt-{sid}-r{N}-{attempt8}`), so no leftover shares a retry's path.
- Resilient removal, strictly for `coai-wt-` under our root: `worktree unlock`, `remove -f -f`, a short
  retry for Defender, then rename to `coai-wt-trash-*` for the next sweep.
- A sweep before EVERY round, of THIS session's leftovers only; `open` removes another session's tree
  only when its owner is dead (an owner marker with a heartbeat — a pid is wrong on a NAS). Every
  per-directory failure is caught; `open` never fails on `IOException`.
- `worktree add` gets the review tree's 10-minute budget; the error quotes git's `fatal:` line, not
  "Preparing worktree".
- *(plan round)* **The owner is a PID plus its process start time**, written as a marker beside each
  tree — exact because the root is machine-local (D4), and a paused owner (a debugger, a sleeping
  laptop) is still alive, so it is never reaped. No heartbeat, no staleness threshold.
- *(plan round)* **A half-made registration is found by path**: every `git worktree list --porcelain`
  entry under OUR root with the `coai-wt-` prefix whose owner is not alive — `locked`/`prunable`
  included — is unlocked, removed `-f -f` and pruned. Tested by leaving `locked = initializing`.
- *(plan round)* **The root is `ReviewTreeRoot`'s machine-local resolution with its own leaf**
  (`round-trees`), one function for both, never `Path.GetTempPath()` guessed separately.
- *(plan round)* **Trash is bounded**: every sweep retries every `coai-wt-trash-*`; one still held after
  24 h is named in a notice once. Nothing grows silently.

**S3 — a gate can be run again (#490).** *(D3)*
- a. `review_code(again: true)` on a `Done` code session reopens `CodeReview` with a fresh round count,
  keeping `PlanProceeded`, `PlanText` and `Rejections` — refused when HEAD has not moved since the last
  code round; `HumanGate` and `Unresolved` still come first. The `Done` message names this door.
- *(plan round)* The empty-diff check (S1) runs BEFORE `again` changes any state: HEAD that moved only
  by excluded paths is refused as "nothing reviewable since round N", and the session stays `Done`.
  Every `again` refusal says which of three it is — no new commit since round N (with its sha), a
  round awaiting `resolve`, or a human gate — and what to do next.
- b. `status` returns the PENDING findings of a round awaiting `resolve`, so a lost reply is no longer
  a blind decision (taken from ar1's read-back idea).
- c. The gate rule (conventions `coai-review-gate.md`, snippet v6) and `GateEnding`: "the code gate is
  a stage — at least one round at the end; `again` for a checkpoint or after a crash; a code round
  closes the session". A conventions change is a pin cascade — batched.

**S4 — one mutating call per session.** *(D5)*
- Port ar1's `SessionClaim` onto today's `RunStageAsync`, the three-part key, beside `.turn`: once
  several code rounds exist per session, two concurrent `review_code` calls on one session must not
  both run. Its tests (`OneSessionOneMutatingCallTests`, `OpenTakesTheClaimFirstTests`) are the model.
- Out of scope: `reserve_round`/`run_round`/`round_status`, the outbox, attestation — a separate plan
  if several clients per repo+branch become real.

## Build order

1. Decisions D1–D5.
2. S1 (smallest, removes the false pass) → S2 (unblocks retries) → S3a+b → S4 → S3c (conventions
   cascade last, batched with any other rule change).
3. One `review_plan` for the epic after the decisions; one `review_code` after S4 (S3c's conventions
   change is its own PR in the conventions repo).

## Test plan (RED first, each watched failing for the stated reason)

| Story | Test | RED today |
|---|---|---|
| S1 | `EndToEndTests`: `review_code` with branch == base and a clean script → refused, 0 launches, session unchanged | `proceed` with 0 findings |
| S1 | same, with a file changed but uncommitted in the checkout → refusal names 1 uncommitted file | `proceed` |
| S1 | `ContextAssemblerTests`: only `package-lock.json` changed → refusal lists it | `proceed` |
| S1 | a `proceed` over committed work plus an uncommitted tail names the tail | silent |
| S2 | a `.git/worktrees/<name>/locked` = `initializing` left at the round's path → the next round runs | `missing but locked` |
| S2 | a file held open (`FileShare.None`) during Dispose → the round's findings are returned, a notice recorded | `{"error":"git worktree remove…"}` |
| S2 | two `WorktreeManager`s on one root: `open` of one leaves the other's live lease | the live tree is removed |
| S2 | an `IOException` in the sweep → `open` still succeeds | `open` throws |
| S3 | `Done` + HEAD moved + `again: true` → a code round runs; HEAD unchanged → refused with the reason | "open a new one" |
| S3 | `status` while `AwaitingResolve` returns the pending findings | counts only |
| S4 | two concurrent `review_code` on one session → one runs, one is refused by the claim | both run |

The whole `CoaiMcp.Tests.exe`, `CoaiServer.Tests.exe`, `CoaiBugs.Tests.exe`, `CoaiBench.Tests.exe` and
the extension's `npm test` before every PR — never `dotnet test`.

## Definition of Done

- [x] D1–D5 answered and recorded here (2026-09-24, as recommended).
- [ ] Every RED test in the table watched failing for the stated reason, then green; break-it checks
      done with compiling code.
- [ ] No `review_code` can answer `proceed` over an empty diff; the refusal says which empty.
- [ ] A failed round's worktree never blocks the next attempt and never masks a verdict; `open` never
      removes a live tree of another session.
- [ ] A code session can run a second round after new commits; a lost reply can be recovered via `status`.
- [ ] `research/module_server.md` updated; the gate rule text (snippet v6) and `GateEnding` agree.
- [ ] This plan promoted to `research/` with what shipped differently.
