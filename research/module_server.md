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

**And the sweep had to learn the other two prefixes.** `PruneOldScratchDirs` (was
`PruneOldAnswerDirs`) ran on the way IN over `coai-answers-*` only, because that was the leak an
audit had found — 1384 of them. A round takes two more empty directories: `coai-repair-*` always,
and `coai-noworkspace-*` now on the DEFAULT path. Three per round, one swept. It sweeps all three
now, and `ScratchDirsTests` was watched fail with *"coai-repair-old is a round's leftover"* before
the prefix list was widened.

**The worktree is leased either way; what varies is the LAUNCH directory.** `BuildWork` decides it:
`CodeWorkspace == "none"` (the default, `COAI_CODE_WORKSPACE`) launches every code reviewer in a
fresh temp directory, so an agentic CLI has nothing to wander into; `"worktree"` launches them in
the checkout. The server still reads the diff and the written rules from the lease in both cases,
which is why the conventions pass works with no checkout. Plan reviewers have always launched in an
empty directory and take no switch. Measured on one commit, Fast found MORE from all three hosted
models at a fraction of the tokens —
[RESULTS_findings_that_are_worth_something.md](RESULTS_findings_that_are_worth_something.md).

### What the gate found in this half (2026-09-03)

Four of the nine defects from the 2026-09-02 campaign are server-side, and all four are the same
kind of thing: a decision written correctly in one place and wrongly in another, or a value taken on
trust.

- **`LocalAsk.SeedFor`** replaced `prompt.GetHashCode()`, which .NET randomises per process — the
  seed changed on every run underneath a comment promising it did not. FNV-1a over the UTF-8 bytes,
  in unsigned arithmetic so there is no `Math.Abs(int.MinValue)` to throw. Pinned by a test that
  computes the same hash from the ALGORITHM rather than from the code, because the property —
  "the same in another process" — cannot be observed from inside one.
- **`LocalAsk.ReadResponse`** checks the root's `ValueKind`. `JsonDocument.Parse` succeeds for `[]`,
  `42`, `null` and a bare string, and `TryGetProperty` on a non-object root throws
  `InvalidOperationException`, which the `catch (JsonException)` under it does not catch: an engine
  answering an array took the round down instead of being reported unparseable.
- **`LocalRuntime.OpenAiBaseOf`** normalises the endpoint for the REVIEW, not only for the panel's
  probe. An endpoint typed without `/v1` listed its models happily and 404'd on every round.
- **`Program.AskLocalAsync`** refuses a missing schema file with exit 65 instead of substituting
  `{}`. The unconstrained request had been removed from `LocalAsk.RequestBody` and left in its
  caller.

And one from CI rather than from a model: **`Escalations.NextWait`** floors the poll at zero. The
loop tested `UtcNow < deadline` and then read the clock again to size the wait; between the two
reads the budget can go negative, and `Task.Delay` throws for that — so a `call_human` that had
merely run out of time came back as an `ArgumentOutOfRangeException`. Seen on the linux-x64 release
runner, which is the machine slow enough to lose the race.

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
`COAI_DATA_DIR/sessions`; each write goes to a scratch file of its OWN name and is then moved over
the real one, retried briefly (see *A session is saved under its own scratch name*); a torn file
reads as a fresh session rather than a locked repo. Round trail (`RoundRecord`) and pending findings ride in the same file — `status`
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

## A reviewer answers for its VENDOR, not for its runtime (2026-09-02)

`ReviewerRuntimeSelector.Named(runtime, vendorId)` is the one place a runtime is chosen by name, and
it hands the runtime the vendor's id. Every built-in runtime takes that id in its constructor,
defaulting to its own name so the bare constructor — `Default`, the tests — means what it always
did.

Before this the built-ins hard-coded `Provider`, and two things followed. Two rows on one runtime
(`claude` and `my-claude`; or `codex` beside a `local` row an older parser had turned into codex)
produced two invocations with one provider/role key, and `LiveRound`'s dictionary threw on the
duplicate before a model was reached. And a lone `my-claude` filed its usage, its findings and its
vault-key lookup under `claude` — a different row's name. `LocalRuntime` and `CustomCodexRuntime` had
always taken the id; the comment beside `RuntimeFor` even named `my-claude` as a real case, but the
fix made there was to the CHOICE of runtime, not to the name it then gave itself.

`ParseVendors` also drops a second row with an already-seen id, first wins — the extension refuses
such a list, and a hand-edited settings file is how one reaches the server.

## A local reviewer is told not to think (2026-09-02)

`PanelSettings.LocalReasoningEffort` — `COAI_LOCAL_REASONING_EFFORT`, default `none` — rides
`ReviewerSettings.ReasoningEffort` into `LocalRuntime.Build` as `--reasoning-effort`, and the shim
writes it as the OpenAI `reasoning_effort` field. `engine` (or blank) sends nothing.

It is the default because of a measurement: the same request to Gemma4 26B answered once in 171 s
and once filled 64k of context with reasoning and returned nothing after 1056 s. The escape was found
first in `dew_flow_rag_qln` (`AiRuntimeOptions.ReasoningEffort`, 2026-08-11): on Ollama's OpenAI
route `think:false` and `chat_template_kwargs` are ignored and `"low"` still burns the budget; only
`"none"` returns `finish_reason: stop`. This repository's own probes reproduced all three. What
thinking is WORTH on a review — four of eight planted defects when it finished — against a reviewer
that always finishes is the measurement recorded in `RESULTS_model_comparison.md`.

## One list of runtime names, because two hand-written ones both forgot the same entry (2026-09-02)

`ReviewerRuntimeSelector.RuntimeNames` is the set a configured vendor's `runtime` is validated
against, and it lives beside the runtime classes because that is where a vendor is actually added.

It exists because the set was written out by hand twice and both copies omitted `local`. The
extension's copy (`RUNTIMES` in `vendors.ts`) made every saved local reviewer come back as a codex
one. The server's copy (`PanelSettings.RuntimeOf`) did the same thing one layer deeper, and it was
worse: a local vendor parsed as `codex` still carries its base URL, which is the shape that means
"custom OpenAI endpoint, needs a vault key". `AuthOf` answered `unavailable`, `BuildWork` drops
unavailable vendors, and the round opened with **zero reviewers** — while `providers`, which has its
own local arm, reported the vendor as healthy.

That combination is the worst available: a panel saying the reviewer is configured and fine, and
every round quietly running without it. Neither copy was reported by anything; both were found by
running a local model against the hosted models' baseline.

`AuthOf` is now pure, internal and asks `RuntimeNameOf` rather than re-reading the base URL — the
third reader of those two fields became the third caller of one answer. Pure because the round that
would have caught it needs a model, a machine and seventeen minutes, and a decision that expensive to
observe has to be observable another way.

## `call_human` stops the review (2026-09-02)

The round budget used to decide only what a finished round was CALLED. `BeginPlanRound` and
`BeginCodeRound` refused an unresolved previous round and a wrong stage, and asked nothing about how
many rounds had been spent; the budget was read in `CompleteRound`, to choose between `revise` and
`call_human`. And `Resolve` cleared `HumanGate` unconditionally — so the AI reopened the gate it had
just been stopped by simply by doing the next thing the protocol asks of it.

The loop that produced: round, `call_human`, resolve, round, `call_human`… A stage on a three-round
budget reached round **ten** on a colleague's machine, every round after the third a full panel of
reviewers. Its own summary is the argument: rounds 1–3 real, 4–9 "progressively narrower crash
windows", round 10 introduced a bug.

Three changes, all small, none of them new vocabulary:

- `BeginPlanRound` / `BeginCodeRound` refuse while `HumanGate` is set, with a sentence naming every
  way out — a refusal with no door is a stall.
- `Resolve` clears `HumanGate` only for `humanSaysProceed`.
- `RoundMachine.ApplyHumanDecision` is what a person's answer does to the state, and
  `PanelService.ApplyAnyHumanDecision` reads it from the escalation file immediately before a round
  would begin. Reading it at the last moment means the person can answer during the wait and the
  next attempt simply works — no restart, no polling.

`HumanDecision` moved from `Server` to `Core.Rounds` for this: the state machine acts on it now, so
it is part of the machine rather than a label the server puts on an answer file. The three answers
are unchanged and were always described this way to the person — `continue` and `fix` grant a FRESH
set of rounds, `discuss` advances nothing.

Only the `human` policy raises the gate. `continue_anyway` and `good_enough` advance on resolve, and
a gate over them would break a configuration whose whole point is not to stop; a test pins that.

## Prompts are a catalog, resolved per round

`PromptCatalog` (in the core) holds twenty-five prompts — a universal one and five narrow lenses for
each of the four roles, plus the conventions pass the three code roles share. The last twelve lenses
were measured before they were added (`RESULTS_focused_prompts.md`): the finding that shaped them is
that a lens written as a TASK to enact repeats itself across runs half again as often as the same
question written as a checklist, while finding the same amount.

The panel's copy in `src_vs_code/src/prompts.ts` and the help's copy in `helpPrompts.ts` are both
held to this file by tests, and the help's copy is now GENERATED
(`src_vs_code/scripts/generate-help-prompts.mjs`) rather than maintained by hand — it takes its
order from `prompts.ts` so a lens cannot be ordered differently in the two mirrors, and it refuses
when the catalog and the prompts folder disagree. `PromptCatalog.ForRound(role, round, chosen, rotating)` answers one round's
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

## The rounds database (`coai.db`, 2026-09-05)

`Store/RoundsDb` writes a SQLite projection beside the sessions: `sessions`, `rounds`, `reviewers`
and — the reason it exists — `findings`, every one with its severity, file, line, title, why, fix,
the vendor that raised it, the role it wore, and **what the caller decided about it and why**.
Search is FTS5 over `title/why/fix/file`, kept in step by triggers.

**Why.** A session file records that codex produced four findings. It does not record the findings:
their text went into the reply to the calling agent, and for the rejected ones into the standing
rejection list. Everything else was gone when the round closed, so the log page could only show
counts and "every finding that ever mentioned FileShare" had no answer on this machine.

**What it is for.** Finding the blind spots in an AI's own reasoning (operator, 2026-09-05). So a
round also carries the scope the caller stated, the commit the reviewers read, which caller it was,
and how it closed the gate — `accepted` and `rejected` counts, `-1` until it closes one. An
**accepted** finding is by definition something the caller had not seen and then agreed was worth
having: that is the blind-spot corpus. A **rejection** is a disagreement, and one a later round
raises again is flagged `re_raised` — the gate discounts those, and a disagreement the caller keeps
defending is the more interesting kind.

`rounds.agent_log` holds what the caller was DOING in the stretch this round closes: a trimmed slice
of its own CLI transcript (`~/.claude/projects/**/*.jsonl`) between the previous round and this
one — the operator's framing, "session opened 13:00, plan review 13:39, so that stretch is the plan
round's". `Store/AgentLog` reads it shared and read-only, keeps instant/kind/first 600 characters,
names a tool call rather than quoting its arguments, caps at 400 entries or 256 KB, and says inside
the slice when it had to cut. It never leaves the machine.

**What the gate changed about it.** Its own two rounds over this diff took nine findings: the
transcript slice keeps to ONE session (entries working in the repo or under it; failing that, the
busiest transcript in the window — sweeping every project into this repository's database was a real
objection from two security reviewers); a line is skipped by a day scan before it is parsed and the
first entry past the window ends the file; a decision follows the DEFECT rather than the ordinal it
had in one reply; the opening instant is recorded on the session rather than read from the file's
creation time, which a save-by-move destroys on Windows and Linux never had; `COAI_AGENT_LOG_DIR`
points the reader at another CLI's transcripts; and `Open` catches anything at all, because a
migration step throwing something unlisted must not take down a review it only records.

**Shape decisions.**

- A **projection, never the source of truth**. The session files are unchanged and still drive every
  round; every write here is best-effort (`PanelService.Project`) — a database that cannot be
  written must never take down a round somebody is waiting for.
- **Opened per write, not held.** A round takes minutes and produces two or three writes; a held
  connection buys nothing and costs a file handle five servers would fight over. `Pooling=False`
  for the same reason — a pooled connection keeps the handle after `Dispose`, which turned nine
  unrelated tests red on their own cleanup.
- WAL, for the five-window case.
- `Microsoft.Data.Sqlite.Core` plus a chosen `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5, not the
  all-in-one package: that one pins 2.1.11, whose native lib carries GHSA-2m69-gcr7-jv3q, and this
  repository builds advisories as errors. **Native AOT publishes clean with it** — measured
  2026-09-05: 17.7 MB, zero IL or trim warnings.

The extension does not read it yet; the log page still flattens the session files. That half is
[todo/PLAN_local_db_reader.md](../todo/PLAN_local_db_reader.md).

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

### The gate is split per stage, and code round 1 judges the written rules (2026-09-01)

**`PanelConfig` is two `StageGate`s.** One threshold for both stages was wrong in a way only use
revealed: a plan is a document, so two findings still open is a lot of doubt about a page of text; a
diff is hundreds of lines across a dozen files, and three open there is an ordinary Tuesday. The
number that made the plan gate strict made the code gate a permanent `call_human` — measured on this
product's own rounds, where the plan stage passed at two and the code stage never passed at all.
Defaults: plan 3 rounds / 2 findings, code 3 / 3. `PanelConfig.For(Stage)` is the only way to read
them, so no call site picks a stage by hand, and the legacy `COAI_MAX_ROUNDS` /
`COAI_GATE_THRESHOLD` become the value for BOTH stages rather than being dropped.

**The reviewers are shown the project's own rules.** `RuleFiles.Collect` (in `runners/Context`) reads
`CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.github/copilot-instructions.md`, `.claude/rules/**` and
`.cursor/rules/**` from the WORKTREE — the rules as of the commit under review, not as of this
afternoon. Instruction files first (they are the entry points and they survive a tight budget), 40 KB
total, whole files only, and **what the budget cut is NAMED in the prompt**: a reviewer told nothing
about what it was not shown would report compliance with rules it never saw, which turns an absence
of evidence into a clean bill of health. A repo with no rules gets a sentence saying so, because a
conventions pass with nothing to judge against would invent a standard.

**And in this family the rules are a SUBMODULE, which for eight days meant they were not there at
all.** `dew_flow_conventions` mounts at `.claude/rules/shared` in six consumers — 26 files, 208 455
bytes — and git does not populate submodules in a linked worktree. Measured 2026-09-04 on
`dew_flow_creds_for_devs`, whose `.claude/rules/` holds nothing but the mount: the round's
`shared/` was empty, so every conventions pass there judged a diff against `CLAUDE.md` alone. The
worktree now populates its own submodules from the PARENT checkout's copies
(`runners/Worktrees/SubmodulePopulator.cs`) — offline, pinned to the reviewed commit, 1.49 s against
2.45 s for the network form. Three consequences worth knowing:

- **A mount that did not materialise is named**, in `RuleBundle.MissingMounts` and in the rendered
  block. Zero files plus zero omissions used to be indistinguishable from a repository with no
  rules, which is the same false clean bill of health one directory deeper.
- **The repository's OWN rule folders are read before the mount**, so the 40 KB budget is spent
  first on the rules a diff here can break; the family's are the same in six checkouts and are what
  the budget drops. Alphabetical order decided this before, and a local `workflows/` sorts after
  `shared/`.
- **A rules repository's housekeeping is not a rule** — its `todo/`, `settings/`, `tools/` fixtures
  and its own `README.md` / instruction files — and the exclusion is scoped to the mount, because a
  repository is entitled to its own `.claude/rules/todo/`.

**Round 1 of every code role is the conventions pass** (`prompts/conventions.md`), when rules exist
and the person has not chosen otherwise. Three reviewers already cover architecture, security and
performance, each with its own taste; the one thing none of them did is hold the change to the
standard the project WROTE DOWN — which is the standard its human authors are held to, so the two
halves were being judged differently by construction. Before this, three rounds on this product's own
commits referenced a project rule zero times.

The prompt was chosen by measurement and the measurement decided nothing:
[RESULTS_conventions_prompt.md](RESULTS_conventions_prompt.md).

### A `call_human` answer reaches the machine

The notice is written by a round that then RETURNS, so nothing polls for its answer the way
`AskAsync` does — the panel wrote `<id>.answer.json` and no code on either side ever read it. A
person could decide, watch the card disappear, and have changed nothing, which is a worse dead end
than never being asked because it looks like it worked.

`Escalations.DecisionFor(sessionId)` now reads it, and the answer carries one of THREE decisions
rather than prose: `continue` (another set of rounds, nothing changed), `fix` (stop, act on the
findings, then review again) and `discuss` (stop and talk to the person). `resolve` resets the
stage's round count for the first two — the person's doing, not the AI's — and `status` reports the
decision so a resumed conversation LEARNS of it rather than being told.

**None of the three advances a stage over open findings.** A human override meaning "ignore all
this" would be an off switch on the gate, and it is deliberately not offered.

### The gate is per ROLE, and the prompts can be dealt (2026-09-01)

**`PanelConfig` holds a `RoleGate` per role.** Per stage before this, and one number for both before
that; each step was the same discovery, that a budget shared by things which are not alike forces the
cheapest of them to pay for the most expensive. Architecture may be worth two passes with different
lenses while performance is worth one. `For(string role)` is the only way to read a role's numbers;
`For(Stage)` answers the widest of the stage's roles, because the stage counts rounds once and a role
simply stops taking part when its own budget is spent (`RolesForRound`).

Two consequences worth naming:

- **A finding is counted against the threshold of the role that raised it.** `Finding.Role` is stamped
  in `PanelService` — the only place holding both the invocation and its answer — and `GateRule`
  groups by it. Passing is EVERY role at or under its own threshold, not one total being small
  enough. `GateResult.OverThreshold` names the roles with work left.
- **A round revises for the budget of the roles that are actually over.** Not the stage's widest: a
  role with one round that is still over cannot run again, so revising for its sake would loop until
  the widest role ran out, asking nothing new of anybody.
- A threshold of **zero** now survives the server. `IntVar` required a positive number, which is
  right for rounds and wrong for a threshold: the panel had always accepted zero and had a test
  saying so, and the server silently substituted its own default — the two halves disagreeing about a
  number a person had deliberately set to nothing.

**Dealing the prompts (`PromptDeal`) is opt-in, off by default, and that default is the point.** With
it off — the shipped behaviour — every vendor answers every question and `FindingDedup` merges what
they agree on, which is the strongest signal this product produces. With it on the round's items are
dealt one per vendor: every lens gets asked once at half the launches, and that agreement is gone.
Two switches, because a plan has three lenses for one role and a code round has three roles.

The deal is seeded from `StableSeed(sessionId, round)` — FNV, not `string.GetHashCode`, which is
randomised per process and would deal a different hand on a restart while the log named a seed nobody
could reuse. The plan stage additionally spends each lens once: `PersistedSession.UsedPrompts` records
what a round asked, so two vendors cover the pool in two rounds instead of both being asked the
universal question.

### The translator is gone

It existed because a `call_human` question was prose an AI had written and the person answered in
their own words. The escalation is three buttons: the question is one fixed English sentence and the
answer is a choice. `runners/Translation`, `ITranslator`, `TranslationPrompt`, the `Translator` and
`Language` settings and `COAI_TRANSLATOR_*` / `COAI_LANGUAGE` are all removed. A subprocess per
escalation that can time out, refuse, or answer in the wrong language was a moving part earning
nothing. The help's own five languages are untouched — that is the reading side, not the reviewers'.

### Rotation is gone, because only one half of the product had it (2026-09-01)

`PromptCatalog.ForRound` took a `rotating` flag: with no explicit pick, spend round 1 on the
universal question and each later round on a different lens. It came from `COAI_ROTATE_PROMPTS`, and
when the Prompts and Gate sections were merged the extension stopped writing that variable — so
nothing a person could touch turned it on.

The panel, meanwhile, passed its DEAL switch into the mirror function's `rotating` slot. Two
different ideas sharing one argument: ticking *Deal the lenses across vendors* made the picker show
`arch-boundaries` for round 2 of Architecture, while the server ran `architecture`. Found by cell 9
of the pre-delivery campaign, not by reading.

Both halves lost the branch. `ForRound(role, round, chosen, hasRules)` now resolves exactly three
ways — an explicit pick, the conventions pass in round 1 of a code role with rules present, or that
role's universal prompt — and `panelServerPromptAgreement.test.ts` asserts the panel agrees for
every role and round. `COAI_ROTATE_PROMPTS` survives as the legacy alias for the two dealing
switches, which is where anybody who set it wanted to end up.

Removing it cost nothing measurable: rotation was measured WORSE than asking the universal question
twice — 17 distinct findings against 25 over two code rounds, for less money
([RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md) §3). Two different lenses on one
change are still available by picking them on two rounds.

### `--version`, and why a server needed one (2026-09-03)

`Classify` has a fourth mode: `--version` / `-v` / `version` prints one line — `coai-mcp 0.12.3` —
on **stdout** and exits 0. Stdout is sanctioned here for the same reason as `--help`: this mode
never speaks the protocol, so the stream belongs to a person's terminal.

It exists because the EXTENSION could not tell what it had installed. Its panel remembered the
number it had downloaded in `globalState`, which VS Code shares between a local window and a remote
one while the binary itself is per side — so a WSL side running 0.12.1 was told by its own panel that
0.12.2 was installed and that there was nothing to update
([module_extension.md](module_extension.md), *The Server section is about one SIDE of a machine*).
A binary that can state its own version ends that class of question: the panel asks the file it is
about to describe.

**Where the number comes from.** The assembly's informational version, cut at the FIRST `+` —
whatever a build server stamps after it is build metadata, not something anyone can compare.
`<Version>` is pinned to `0.0.0` in `CoaiMcp.csproj` so an unstamped local build reads as OLDER than
every release; the SDK's default `1.0.0` would have read as newer than every published version and
suppressed the extension's update button for ever. The release passes the tag's version over it
(`dotnet publish -p:Version=$VERSION`), and the smoke step **fails the release** when the published
binary's `--version` disagrees with its tag — a stamping step that silently stops working would put
the original lie back one release later, where nobody would look for it.

Verified on a real Native AOT binary (the attribute survives ILC): `--version` → `coai-mcp 0.12.3`,
exit 0, while a near-miss like `--ver` still exits 64 with the usage line on stderr.

### One local engine serves one reviewer, and a code is matched as a code (2026-09-03)

**Two rounds reported fewer reviewers than they asked for, and both sentences pointed at the wrong
thing.** Measured from this server's own log.

*The card.* A code round started `local/Architecture` at 16:04:26 and it answered in **30.6 s**; it
started `local/SecurityReliability` at 16:04:33 and `local/UxDxPerformance` at 16:04:35, and both were
cancelled at **590 s** having produced nothing. The engine was up, loaded and answering — to three
requests of one round at once, because `COAI_MAX_PER_PROVIDER=3` is a reasonable number for a hosted
vendor's fleet and the wrong number for one GPU. Each got a third of the card.

So the cap that matters is keyed by the **engine**, not the vendor: `ReviewerInvocation.SharedResource`
carries it, `LocalRuntime` sets it to `EngineKey(endpoint)` — canonicalised, because
`…/v1` and `…/v1/` are one card — and `COAI_LOCAL_CONCURRENCY` (default **1**) is its cap. Two vendors
pointed at one Ollama share it; two engines on two ports do not; a hosted vendor holds none.

Three things the gate corrected in it, each of which was a defect in its own right:

| what | why it mattered |
|---|---|
| The limiters now live as long as the **scheduler** | they were built inside `RunAllAsync`, so two rounds in one server built two sets and a cap of three allowed six on the machine the docstring says it bounds |
| Widest lock first — machine, vendor, **engine last** | taking the engine first let a local reviewer hold the card while blocked on a machine slot filled by hosted vendors: the GPU idle and locked, every other local reviewer waiting for it |
| A cancelled wait is **reported**, not thrown | `WaitAsync(ct)` threw out of the fan-out, `Task.WhenAll` propagated it, and a round cancelled with five reviewers finished reported none of them. The test for that found the same hole on the RUNNING path |

The deadline sentence changed with it. It used to read *"did not answer within the round's deadline:
The request was canceled due to the configured HttpClient.Timeout"* — which describes an engine that
is down, and sent readers to check a healthy port. It now says how long it waited of what it was
given, that the engine is up and slower than the deadline, and the cures in the order worth trying;
and a cancellation is only called *too slow* when the deadline is what expired, never when the round
was abandoned.

*The 404.* A codex reviewer was reported as **"rate limited (after one retry)"** when the vendor had
answered `unexpected status 404 Not Found … cf-ray: a3…`. `429` and `503` were in
`RateLimit.Phrases` as bare substrings of stdout and stderr — and a Cloudflare ray id is hexadecimal,
a token count is a number, a duration in milliseconds is a number. The person was told to wait for a
quota that was never hit, and the reviewer was retried against a route that answers 404.

A status code is now matched as a code, in the four shapes vendors actually print — `HTTP 429`,
`status: 503`, `429 Too Many Requests`, `503 (Service) Unavailable` — and in no other, because the
first attempt at this regex also accepted `code`, `status_code`, `error` and a bare `rate`, which is
the same class of guess it was replacing. `cf-ray: a3f4291e…`, `prompt_tokens: 429` and `4290ms` are
not rate limits. The 404 comes back as what it is: a non-zero exit carrying the vendor's own line,
once, unretried.

**Out of scope, and stated rather than implied:** this is a guarantee per SERVER PROCESS. Two MCP
clients each running a `coai-mcp`, or another program on the same card, are not serialised by it — for
that, the family's `gpu-lease` rule is the mechanism, and it lives outside this product because a
marketplace extension cannot depend on another repository's daemon.

### Each reviewer's own duration is recorded (2026-09-03)

`ReviewerState.Seconds`, filled in `LiveRound.Report` from `ReviewerProgress.Elapsed` — which the
scheduler has always measured with a stopwatch around the run and which this boundary was throwing
away. A round's own total cannot answer "which of the nine": measured the same day, a code round took
11m 2s across nine reviewers, and the two that spent 590 s each were indistinguishable in that number
from the seven that took under a minute.

Written only when the elapsed time is greater than zero, so a later progress line — a "running"
report, which carries none — cannot erase the number of a reviewer that has already finished. The
field defaults to zero, because a session file written by an older server has no such field and must
still read.

The panel renders it per reviewer inside the round's disclosure
([module_extension.md](module_extension.md)).

### The card is leased across processes, and a queued reviewer says how long (2026-09-03)

`mcp-v0.12.4` serialised the reviewers of ONE server against one engine and said in its own record
what it did not cover: two MCP clients, each with a `coai-mcp` of its own. That is the normal state of
this machine — several Claude windows at once — so the second half is `EngineLease`, taken by the
`--ask-local` shim, which is the one place every local reviewer of every server passes through.

**The lock is the operating system's, not a protocol of ours.** A lock file held with
`FileShare.None` is exclusive between .NET processes on Windows and on Unix, and the kernel releases
it when the holder dies — kill, crash or power cut. The first design was a pid, a heartbeat and rules
for stealing a stale lease; this change's own gate took it apart, and it was right to:

| what the gate named | why it cannot happen now |
|---|---|
| a reused pid makes a dead holder look alive | no pid is recorded or consulted |
| a partial write leaves unreadable metadata on the kill path | nothing is written to be read back |
| two waiters race the same delete | there is no delete to race; the kernel releases the handle |
| a hung-but-alive holder is indistinguishable from a slow one | it is the same thing, and both end when the waiter's deadline does |

**Waiting is counted with the same mechanism.** A waiter holds its own file while it queues, so
"how many are ahead" is "how many of these files are locked"; a waiter that was killed leaves a file
nobody holds, which is deleted rather than counted. One liveness rule in the class, not two.

**The wait is inside the reviewer's deadline, not beside it.** The shim computes an absolute
`untilUtc`, waits for the card against it, and gives the HTTP call only what is LEFT. A queue that
quietly ate a reviewer's budget and then reported a slow engine would be a lie about which half was
slow, so there are two sentences: the engine was busy for the whole deadline and the question was
never asked, or the engine had the question and did not finish.

**The estimate.** Each holder appends `model<TAB>seconds` to a history file while it still holds the
lease — the same exclusion that protects the engine protects its history — and a queued reviewer's
note is built from the count of callers ahead and the average of the last twenty runs of THAT model.
Three samples before it says a time at all: two runs is not a rate, and the count alone is always
true. Per model, because one average over a ten-second check and a five-hundred-second analysis is an
estimate of neither — the gate's finding, and the reason `ReviewerInvocation` now carries the model.

The note reaches the panel through `ReviewerProgress.Note` → `ReviewerState.Note`, and the round card
renders it for a queued reviewer exactly as it does for a failed one:
`local/Architecture — queued (2 ahead on this engine, about 4 min)`.

### The gate can give ORDERS, and three switches decide which (2026-09-03)

The gate answers one question — are these findings gating, may you proceed — and the AI that called
it decides everything else: whether to split the work, when to interrupt the person, which model to
use for what. Three of those are the OPERATOR's decisions, and the panel is where the operator sits.
So a round's reply can now carry **commands**: short imperative instructions, with a preamble saying
they come from a person and outrank the caller's own defaults.

| switch | what the command says |
|---|---|
| Work autonomously | a question that does not block is written down and asked at the END, all together; one that does block is asked at once — but only after gathering every other blocking question, so the person is interrupted once |
| Split the plan | 2-4 epics, each 2-4 logically complete stories, and after EVERY story: `review_code`, resolve, fix, document, test, commit — then the next |
| Split with Fable | the split itself on Fable at its highest version; ordinary stories on Opus; payments, security, architecture and data migration back on Fable |

Everything is off by default, and an empty command list is exactly the behaviour of every release
before this one.

**Three rules the commands keep, each because a review round found the case:**

- **The split command belongs to the PLAN stage only.** A code round has a diff and no plan, so a
  split verdict computed there would be a number invented from source. Raised twice.
- **The Fable order is the switch and nothing else (corrected 2026-09-04).** It used to be withheld
  unless a Fable REVIEWER was configured, on the reasoning that a command must never name a model
  this machine has not got. Sound reasoning, wrong premise: Fable is not a reviewer here — it is a
  model of the AI that CALLED us, which already has it. Nobody configures Fable as a vendor in this
  panel and nobody should, so the check was false on every real machine and the switch was inert.
  Confirmed on the operator's own: `providers` answers codex, gemini, local. `FableAvailable` and the
  two helpers behind it are gone rather than left as a flag with one constant caller.
- **The autonomy command does not tell you to re-read epics that do not exist.** With the split
  switch off it says "re-read the whole plan" instead.

**A reader could kill a round, and the catch written for it looked past the exception (2026-09-04).**
Six code rounds died with `Access to the path is denied`. One died on the FINAL save, with every
reviewer answered and the verdict decided: the findings were in memory and all of it was thrown away
because a file could not be renamed. Three separate faults, found in this order.

1. **The scratch name was fixed.** `Save` wrote `<session>.json.tmp` — one path — and
   `LiveRound.Persist` is called from the progress callback of every reviewer, so a nine-reviewer
   round had nine writers racing for one temporary file. It is now named per write.
2. **A reader forbade writing.** This is the cause nobody looks for and the one that mattered most:
   `File.ReadAllText` opens with `FileShare.Read`, and five `coai-mcp` processes were alive on this
   machine — one per VS Code window — each polling the sessions directory. A writer's `File.Move`
   therefore landed on a file somebody was merely LOOKING at. Reads now open
   `ReadWrite | Delete`; `Delete` belongs in the set because on Windows a rename over an open file is
   a delete of that file, and a reader permitting writes but not deletes still blocks the move.
3. **It failed as `UnauthorizedAccessException`, which is not an `IOException`** — so
   `LiveRound.Persist`'s `catch (IOException)`, written for exactly this case, walked straight past
   it and took the round down. The store now throws a named `SessionStoreException` and every caller
   states its own policy: **a repaint may be lost** (the next progress event writes again), **the
   record of a finished round is best-effort** and the answer goes back regardless, and **every other
   save still throws**, because those are the state the protocol runs on.

**Sharing was not enough, and the measurement is what said so.** With the reader sharing and the
rename retried ten times over half a second, four readers in a hot loop still starved the writer in
two runs of three. Retrying harder is a hope with a bigger budget, not a mechanism. Readers and
writers now take **turns** — `SessionTurn`, an OS lock file per session, the same shape as
`EngineLease`, released by the kernel even when a process is killed. The turn is held only for the
rename, never for serialising JSON: a writer that held it while formatting would keep every reader
waiting on work they do not need. A turn that cannot be taken is not fatal on its own — the reader
answers "no session" as it always did for an unreadable file, and the writer goes on to fail loudly
at the move; blocking a round on a lock file would be a worse failure than the one being fixed.

The lock file is deliberately NOT named `session-*.json`, so the orphan sweep's own enumeration
cannot pick it up.

**The order to split is given ONCE, and it is keyed by the CALLER (2026-09-04).** Raised by the
operator before it could happen: a plan is split into epics, each epic comes back for its own plan
review — which is the right thing to do — and a gate with no memory tells each one to split into
epics. Epics of epics, with no floor.

The memory cannot live on our session, and the reason is worth stating because it is not obvious.
Our session is repo+branch and its plan stage happens exactly once: after a plan proceeds the stage
advances, and `BeginPlanRound` refuses a second plan round on it outright. So an epic can only come
back as a **different session, on its own branch** — invisible to anything our session remembers.

What crosses those sessions is the AI itself, and Claude Code hands us its identity for free:
`CLAUDE_CODE_SESSION_ID` is exported to every child it spawns, and an MCP server on stdio is one of
those children. `CallerIdentity` reads it, with `COAI_CALLER_SESSION` as an override for a client that
has no id of its own. `CallerSessions` then **claims** that caller's one order: one file under the
data directory, opened `FileShare.None`, with the stamp read, the decision taken and the replacement
written **without the handle ever being released**. Two servers share that directory as a matter of
course — one per MCP client on this machine — and a read followed by a write lets both of them issue
the order (codex, plan round; 8 of 8 claimed it before the fix). The first fix used `CreateNew` and
deleted an expired claim first, and three reviewers in the code round independently found the hole in
that: the second process's delete removes the claim the first has just written, and both return true.
No ordering of delete-then-create closes it; holding the file does. The claim **fails open**: a store
that cannot be written gives the order and logs a warning,
because failing closed would silently disable the feature and a duplicate costs one repeated
instruction while silence costs every instruction.

A client that names no session at all falls back to the **checkout**, not to our session. Our session
is repo+branch and an epic arrives on its own branch, so a session-keyed fallback would call every
epic a fresh caller and re-order the split on each of them — the exact loop this exists to stop
(gemini, Blocking, same round; the test reproduces it word for word before the fix). The price is
stated rather than hidden: an anonymous client starting a second, unrelated task in the same checkout
within a day is told it is a piece, which is the cheaper of the two errors and one the piece's own
order invites it to contradict.

A caller that already holds a claim is a caller already inside a split, and is told so:

> This plan is a PIECE of a split that is already under way, so do NOT split it again: build it as
> one unit, review its diff through this gate, fix, document, test and commit. If it is genuinely too
> big for one unit, say so in your summary…

The verdict is not recomputed for a piece, the Fable command — which is about performing the split —
is not issued with it, and the autonomy command is, because when to interrupt a person has nothing to
do with splitting. The memory expires after a day: a Claude session long enough to span one is a
session doing more than one task, and the second task is owed its own split order.

Measured on the real corpus rather than asserted — 66 calls over 11 plans, two models,
[`research/RESULTS_commands_campaign.md`](RESULTS_commands_campaign.md).

**Whether to split is measured, and says so.** `PlanShapeReader` counts the plan's lines, the
numbered items under its build-order heading, the distinct files it names and the top-level
directories it touches; `PlanShape.Verdict` is two-axis — epics when big AND broad
(`lines > 300 && (steps ≥ 6 || areas ≥ 4)`, or 14+ files), stories when `steps ≥ 4 || lines > 100`,
otherwise as it is. The command carries those numbers with the verdict and says out loud that it is a
heuristic the AI may disagree with in writing.

The rule was fitted to this repository's own 23 plans (median 120 lines, 4 steps, 6 files, 2 areas;
max 554 / 9 / 28 / 5) and to the one case the corpus can answer for: the master plan that actually
became six epics is 440 lines across 5 areas with **no build order at all**, so a step count alone
misses it, while a 230-line plan with 9 steps shipped whole in a day. Size alone was refuted by the
data before the rule was written.

**The switches are live.** `PanelServiceHost` already rebuilt the service when the settings file's
write time or length changed; `SettingsAreLiveTests` now states it as a requirement rather than a
convenience — a switch ticked a second before a call governs that call, in both directions, and
creating a file where there was none counts as a change.

## The autonomy order is six instructions (2026-09-05)

`COAI_AUTONOMOUS` used to hand back one sentence — work autonomously, batch the questions. The
operator, over the checkbox: *"эта галочка должна говорить не просто работать автономно, а давать чёткие
инструкции"*. `GateCommands.AutonomyCommand` now spells out what autonomous means, and
`AutonomyIsAnInstructionTests` holds each order: a red-green-red test for every bug; documentation,
README, manifest and module docs updated with every change; ALL the tests before a release; the
repository's release or pull-request process followed, with a pull request's automatic comments read
five minutes later; an automatic deploy verified against dev, stage or test with its logs read; the
code re-read against the repository's rules; and the assistant saying that it is autonomous and what it
is writing right now. The question-batching rule is unchanged.
