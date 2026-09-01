# module: server — coai-mcp, the protocol holder

> `src_mcp/src` — the host. Identity `connect-other-ais` on the wire, `coai` as the client's
> config key (which is what prefixes the tools: `mcp__coai__review_plan`). Built by hand on the
> `ModelContextProtocol` SDK — the hosted default logs to stdout, and stdout carries JSON-RPC.

## The seven tools

| Tool | Backed by | Refuses when |
|---|---|---|
| `providers` | `PanelService.ProvidersAsync` — CLI probe + vault state | never; it reports |
| `open` | `OpenAsync` — resolve branch, prune worktrees, load-or-create session | repo/branch unresolvable |
| `review_plan` | `RunStageAsync` with one `PlanCritique` per provider | no session; round awaiting resolve |
| `review_code` | `RunStageAsync` with the three code roles per provider | **no plan round reached `proceed`** |
| `resolve` | `ResolveAsync` — reasoned decisions by finding index | bad index; reject without a reason |
| `status` | persisted session + round trail | no session |
| `ask_human` | `Escalations` — a question FILE the extension watches | only an empty question; otherwise it WAITS the budget, then answers `no_answer_yet` telling the model to ask in the chat |

## Flow of one stage

`RunStageAsync`: load session → `RoundMachine.Begin*` (refusal = the answer) → resolve SHA → ONE
worktree lease → build work (schema file, role prompt + contract + context; repair prompt = same +
"ONLY the JSON") → `BoundedScheduler` → merge → `GateRule` → `RoundMachine.CompleteRound` → persist
(`PersistedSession.Pending` = what `resolve` indices point into) → `ReviewAnswer` with an
`instruction` sentence for the main AI. The lease disposes in `finally` — a thrown stage leaves no
worktree.

## Escalation — reaching a person without a port

`ask_human` writes `escalations/<id>.json` into the data directory the extension already reads for
the rounds view, then polls for `<id>.answer.json` beside it. The round's still-gating findings ride
with the question, because a person deciding "ship anyway?" should not have to go looking for what
gates.

A malformed, half-written or empty answer file is **not** an answer — the wait continues; unblocking
a round on nothing is the failure this guards against. The budget (`COAI_ESCALATION_MINUTES`, 30 by
default; `COAI_ESCALATION_SECONDS` wins when set) ends in `no_answer_yet` with the instruction to ask
in the chat — the family's `remote-ask` fallback — and the question file **stays open**.

## Configuration and keys

Environment until the extension arrives: `COAI_PROVIDERS`, `COAI_MODEL_*`, `COAI_EXE_*`,
`COAI_MAX_ROUNDS`, `COAI_GATE_THRESHOLD`, `COAI_ON_EXHAUSTED`, `COAI_MAX_CONCURRENCY`,
`COAI_MAX_PER_PROVIDER`, `COAI_REVIEWER_TIMEOUT_MINUTES`, `COAI_DATA_DIR`, `COAI_LOG_LEVEL`, and
`COAI_CREDS_KEY` — the CredsForDevs config-entry key. `KeyVault` runs `creds config <key>` once at
startup; missing binary / no key / 401 / malformed body are named per-vendor unavailabilities in
`providers`, never crashes, never partial applies, never logged values.

## Persistence

`SessionStore`: one JSON file per session key (SHA-256-prefixed name) under
`COAI_DATA_DIR/sessions`; temp+move writes; a torn file reads as a fresh session rather than a
locked repo. Round trail (`RoundRecord`) and pending findings ride in the same file — `status`
survives a server restart, per the durable-status rule.

### The round is written before it runs, not after (`LiveRound`)

A `RoundRecord` is persisted the moment the fan-out is built — `status: running`, `startedUtc`, the
owning `RunnerPid`, and one `ReviewerState` per reviewer at `queued` — and rewritten as the
scheduler reports each reviewer moving to `running`, `done` (with its finding count) or `failed`
(with the reason). The finished record replaces it with the verdict and the round's `tokensIn`,
`tokensOut` and `costUsd`.

Why it matters: a code round takes minutes, and while it was only written at the END the panel
could not tell "six reviewers are working" from "nothing has ever run here". That is the
durable-status rule pointed at our own slowest operation.

`SweepOrphanedRounds` runs once in the `PanelService` constructor and flips a `running` round whose
`RunnerPid` is no longer alive to `interrupted` — a crashed round must never read as running
forever. The pid check is what keeps a SECOND server sharing this data directory from declaring the
first one's live round dead.

## Verification that matters

- `McpContractTests` speak real JSON-RPC over real stdio to the built binary — and via
  `COAI_CONTRACT_EXE` to the PUBLISHED one; the release workflow runs exactly that as its smoke.
- `PanelServiceTests` run the full loop (plan rounds → gate → code rounds → done) against the
  vendor-mode fake CLI: dedup across providers, the standing-rejection discount, restart survival,
  and the six-launch fan-out with three distinct role prompts, all observed.
- Stdout purity is a test: verbose logging on, every stdout line must parse as JSON.

## Prompts are a catalog, resolved per round

`PromptCatalog` (in the core) holds twelve prompts — a universal one and two narrow lenses for each
of the four roles. `PromptCatalog.ForRound(role, round, chosen, rotating)` answers one round's
prompt: the panel's explicit choice first, then the rotation (universal, then each lens in turn),
then the universal one. An id that is empty, stale or belonging to another role falls THROUGH
rather than leaving a round with no prompt.

`RolePrompts` serves any catalog entry with the same override-first layering the role defaults
already had: a file under `<dataDir>/prompts/<id>.md` wins while it exists, the embedded copy
otherwise. The extension mirrors the catalog so the panel can draw before any server has started,
and a test holds the two lists together — that promise was written as a comment before the test
existed, which is exactly how mirrored lists begin to drift.

## Settings apply to the next round, not the next restart

`PanelServiceHost` stats the panel's settings file on every tool call and rebuilds `PanelService`
when it has changed. Settings used to be read once at startup, which made every change in the
sidebar silently ineffective until the MCP client was restarted — a gap invisible from both ends,
because the panel saves instantly and says so. Environment variables still outrank the file.

## The spending ledger

`UsageLedger` appends one JSON line per reviewer to `<dataDir>/usage.jsonl`: vendor, model, role,
stage, seconds, tokens, cost and outcome. It is separate from the session files on purpose —
sessions are rewritten as rounds advance and hold one repo+branch, while "what has this cost me
this month" spans every session and must outlive all of them. Failed reviewers are recorded too,
and recording never throws: a ledger that can fail a review is worse than one with a gap in it.

## The audit trail

`RoundAudit` writes what the one-line round summary cannot: the roster and the exact argv (at
Debug, so a failure can be reproduced by pasting it into a terminal), each reviewer's start, its
answer with tokens and cost, every failure as a WARNING naming the reason, and every finding with
its origin. It rides the same per-run log file as everything else.

### A `call_human` verdict reaches the person (2026-09-01)

`RoundMachine` can end a round with `call_human`, and that verdict is returned to the calling AI —
which then decides whether to pass it on. It did not, twice in one day, and the operator watched a
panel that said *No ConnectOtherAIs review is waiting on an answer* while a gate sat blocked.

`PanelService.NotifyIfAPersonMustDecide` now writes an escalation file for that verdict, in the
same shape `ask_human` uses, so the panel shows and answers it identically. It does not block: the
round is already over.

**`Escalations.Notify` creates the directory first.** It did not, and its `catch (IOException)`
swallows `DirectoryNotFoundException` along with everything else — so on a machine where nobody had
used `ask_human` yet, the notice that exists to end this silence was itself silent.

### The health probe cannot report a closed door as healthy

`ProbeAsync` consults `VendorDiagnosis.ForRuntime` BEFORE launching `--version`, because a retired
CLI answers `--version` from disk. See the retirement table in
[module_runners.md](module_runners.md).

### A code round is never handed a bare diff (2026-09-01)

`review_code` took `planText` as an ordinary argument and an empty one was accepted in silence, so
the reviewers' job could quietly narrow from *is this what was asked for* to *is this diff
reasonable*. Those are different questions and the second is the cheap one: a change can be well
written, well tested, and solve the wrong problem — a diff-only review approves it, because on its
own terms nothing is wrong with it. It is also the only way an ABSENCE becomes visible: a diff shows
what is there, and only a scope makes the unhandled case or the missing test show up as missing.

Three parts:

- `CodeScope` (`core/Rounds/CodeScope.cs`) — the floor and the refusal text. The floor is 200
  characters and the honesty about it is in the code: this cannot measure whether a scope is GOOD,
  only whether one was written. "fix the update button" passes any is-it-empty check and tells a
  reviewer nothing.
- `PersistedSession.PlanText` — the scope the plan stage agreed on is KEPT, so the code stage reuses
  it and the caller is not asked for it twice. Asking twice is how a caller ends up sending nothing.
- `PanelService.ReviewCodeAsync` refuses before any worktree, launcher or token — but only once the
  stage itself is reachable. "The plan stage has not passed" is the more useful sentence for a caller
  who skipped it, and telling them to send a scope for a round that could not have run either way
  sends them to fix the wrong thing.

**No floor at the plan stage, deliberately.** A three-line plan is a BAD plan and saying so is the
reviewers' job — refusing it at the gate does their work for them and takes away the one round that
would have explained why.

The rule this implements, including how to review an EXISTING commit (scope from the intent, commit
as `branch`, its parent as `baseRef`): [.claude/rules/common/review-gate.md](../.claude/rules/common/review-gate.md).
