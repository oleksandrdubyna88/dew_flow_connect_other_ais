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
