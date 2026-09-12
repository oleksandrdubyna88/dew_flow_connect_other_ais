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
| `review_code` | `RunStageAsync` with the four code roles per provider — `Conventions` first | **no plan round reached `proceed`** |
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

## `PanelService` no longer decides what a vendor IS (2026-09-05)

`RuntimeFor`, `AuthOf` and the `--version` health probe are one-line delegations now; the decision
itself is `CoaiMcp.Runners.Reviewers.RuntimeResolution` and `VendorProbe`, and `UsageLedger` moved
into `CoaiMcp.Runners.Accounting` whole. The reason is the section below and the one before it: the
question "what is this vendor" has had two copies twice, and both times the copy that was missed was
the one that was wrong. The planned Team server asks all three questions of the same vendors, so it
would have been the third copy. The delegations stay because the tests call them here — which is
also what proved the move was a move rather than a rewrite: every existing test passed unedited.

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

> **Since 2026-09-12 the catalog a ROUND reads is `_settings.Rounds.Catalog`** — the seed
> (`shared/builtin-roles.json`, embedded in the core) composed with whatever roles the operator has
> defined, per `module_core.md`. `ChoiceFor` asks it, `UnspentPlanLenses` asks it, and the
> all-roles-off refusal names the roles it holds rather than four constants, so a person who added a
> role of their own is told to tick the box they can actually see. `RolePrompts` is keyed by PROMPT
> id rather than by role, because a role somebody defined has prompts this build never shipped and
> the file it wants is named by the prompt — which is what `ForChoice` always did underneath. The
> `PromptCatalog` is **gone** as of 2026-09-12: the twenty-five rows it held are
> `shared/builtin-roles.json`, the extension's copy is generated from that file, and the C# array had
> no second copy left to be held level with. What remains of its file is the `PromptChoice` record.

`RoleCatalog.Builtin` (in the core) holds twenty-five prompts — a universal one and five narrow lenses
for each of the four lensed roles, plus the single prompt of `Conventions`, which since 2026-09-08 is
a ROLE rather than a pass the code roles took turns hosting. The last twelve lenses
were measured before they were added (`RESULTS_focused_prompts.md`): the finding that shaped them is
that a lens written as a TASK to enact repeats itself across runs half again as often as the same
question written as a checklist, while finding the same amount.

The panel's copy and the help's copy are both GENERATED from the seed now
(`generate-builtin-roles.mjs`, `generate-help-prompts.mjs`), and each half asserts its own loader
against `shared/builtin-roles.json` rather than against the other half's source — see the seed
section of [architecture.md](architecture.md). `RoleCatalog.ForRound(role, round, chosen)` answers
one round's prompt: the panel's explicit choice first, then the role's universal one. An id that is
empty, stale or belonging to another role falls THROUGH rather than leaving a round with no prompt.

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

**The page reads it through `--log`.** `coai-mcp --log [--limit N]` prints the database as JSON and
exits: rounds with their findings and resolutions, the accepted/total counts grouped by category,
role and vendor, and the findings raised again over a standing rejection (`Store/RoundsQuery`). The
extension owns no SQLite for the same reason it owns no native module — the alternative was a
WebAssembly build in the VSIX to query a file this binary already writes. Version skew is ordinary:
a server without the flag exits 64 and the page shows what it always showed.


**A page of it, and the totals counted where the table is (2026-09-09).** `--log --paged` answers
`RoundsQuery.Read(dataDir, limit, before)`: `DefaultLimit` is 200, `MaxLimit` 1000, and both a limit
and a cursor are validated at the boundary — a limit is clamped rather than refused, an unreadable
cursor asks for the first page.

*Keyed, never `OFFSET`.* Rounds are inserted at the top of `started_utc DESC`, so an offset shifts
every later page when a round finishes mid-read, and a row is then seen twice or never. The cursor is
the PAIR `started_utc|id`, and the ordering carries the id too, because two rounds can start in the
same second.

*The list carries no findings.* It carries `FoundCount` — one subquery per row — because the page
needs the number to say whether a gate is still open, and the sentences only when somebody opens a
row. `--findings --session <id> --stage <stage> --number <n>` answers those.

*Three exit codes, because there are three answers.* **0** and a list is what the round found — an
empty list is then a clean round. **69** (EX_UNAVAILABLE) is a round the database has never heard
of, whose findings were recorded nowhere. **74** (EX_IOERR) is the database itself being unreadable,
which says nothing about the round at all and the page draws as a failed read with a retry. The code
round caught the last two sharing one number, which would have told somebody a round was never
recorded because a file was momentarily locked. `LogCliScenarioTests` runs the real binary for each.

*The old shape keeps its ONE grouped findings read.* `--log` without `--paged` still fetches a whole
page's findings in a single `WHERE round_id IN (…)` query rather than one per row — the paged path
asks for none of them, and a thousand round-trips where one read would do is what the code round
caught in the first draft.

*`LoggedTotals` is two statements, one scan each* — `COUNT(*)` and conditional `SUM`s over `rounds`
and over `findings`. The first draft had eight scalar subqueries, which is five passes over one table
where conditional sums are one.

*Without `--paged` nothing changes.* The same binary answers the shape it answered yesterday,
findings inline, **and with the same default of 300** — `LegacyLimit`, because an extension too old
to send the flag is also too old to ask for a second page, so shrinking its default to 200 would
simply take a hundred rounds off the only list it can show. A paged caller whose database cannot be
read is told so with **74**; the legacy shape keeps exit 0 and an empty log, which is what it has
always answered.

*The cursor's timestamp is validated, not only its number.* `0000|1` used to parse and then compare
`0000` against `started_utc`, matching nothing — so a malformed cursor answered with an EMPTY page
instead of the first one, which is the opposite of treating it as absent.

## What a round can be asked afterwards (2026-09-08)

Two lines and one directory, added because a round that answered nothing could not be questioned:

| Written when | Line |
|---|---|
| the context is assembled | `context for review: diff 63104 bytes over 13 file(s), 0 elided; plan 4210 bytes; rules 78757 bytes` |
| the round opens | each reviewer as `codex/Architecture[architecture, 141293 bytes]` — the prompt it was actually handed |
| a reviewer answers with no findings | `… 0 finding(s), 33629 in / 83 out tokens (its answer was kept at …/empty/codex-Conventions-….txt)` |

The first says what was ASSEMBLED, the second what each reviewer RECEIVED, and they are only the
same number while nothing between them is broken — which is the state the measurement of
2026-09-08 could not establish either way. See [module_runners.md](module_runners.md),
*A reviewer that found NOTHING is evidence too*.

## The audit trail

`RoundAudit` writes what the one-line round summary cannot: the roster and the exact argv (at
Debug, so a failure can be reproduced by pasting it into a terminal), each reviewer's start, its
answer with tokens and cost, every failure as a WARNING naming the reason, and every finding with
its origin. It rides the same per-run log file as everything else.

### A round names the reviewer it could not run (2026-09-07)

Everything above reported honestly about reviewers the round **asked**. A reviewer that was enabled
and never entered the roster was reported by nothing at all, and the two lines this file writes
contradicted each other in silence:

```
[11:03:41 INF] starting: codex,gemini,local,remsoftdev-claude enabled
[11:06:00 INF] round 1 PlanReview opening: 3 reviewer(s) — codex/…, gemini/…, local/…
```

Eleven seconds apart, in one file, on 2026-09-07 — and the verdict then said *"all 3 reviewers
answered"*, which was true about what it asked. That silence is what made three other defects
invisible for a day.

`BuildWork`'s filter is now one predicate, `CanRun`, read from two directions: it decides who is
dealt work, and `ExcludedFrom(isPlanStage)` returns everyone else with the vendor's own note as the
reason. Two predicates that agree today is how three copies of the runtime decision got away with it
twice in this same file.

The stage filter runs **first**, in both directions. A vendor turned off for plans has not been lost,
and reporting it on every plan round would train a person to ignore the sentence — which is the one
thing it cannot afford, because it exists to be read the once it matters.

It reaches two surfaces from one list: `RoundAudit.Opening` writes a WARNING beside the roster line
(a separate line, so an ordinary round pays nothing for it), and `ReviewerSummary.Excluded` appends
to the sentence that already travels into `ReviewAnswer.Reviewers`, the closing audit line and the
live round record. A round with nothing to add reads exactly as it always did, and that has its own
test.

**Exclusion is not failure, and the two must not blur.** Exclusion is decided BEFORE the roster,
from `CanRun`; a timeout, a non-zero exit or a kill happens to a reviewer that entered it. They read
in one sentence and have different cures — one is a configuration, the other is a run — so a test
asserts a timed-out vendor appears in `Failures` and never in `Excluded`. And the stage filter being
first is an EXECUTION rule only: `providers` and the panel's badge report every configured reviewer
whatever stage is running, so a credential defect on a code-only vendor is visible during a plan
round, on its card.

### A role nobody asked for is a third thing again (2026-09-10)

There are now three ways a round can be smaller than the roster suggests, and each says so in its own
words because each has a different cure.

| | decided | reads as | cure |
|---|---|---|---|
| `Failures` | after the launch | a problem with the run | look at the vendor |
| `Excluded` | before the roster, from `CanRun` | a problem with the configuration | fix the credential or the runtime |
| `NotAsked` | before the roster, from the REPOSITORY | **not a problem at all** | write some rules, or nothing |

A code round in a repository with no written rules drops its **Conventions** reviewers — correctly,
because a conventions pass with nothing to judge against would invent a standard. Until 2026-09-10
the only place that was said was the server's own log, so the AI that called the gate was handed a
thinner round and no sentence explaining it. Three roles instead of four reads as a failure, or is
not noticed at all.

`ReviewerSummary.NotAsked` carries `SkippedRole(Role, Reason)` — STRUCTURED, not a finished clause,
so a caller parsing the summary gets a role it can name and the punctuation is decided once, beside
the clauses it sits next to. `Sentence` appends it LAST of the three additions, and the order carries
meaning: a deadline explains the failures, the failures explain the count, and what was never asked
for is last because it is the only one of the three that is not a problem. Its verb is its own — *was
not asked*, never *could not run*.

The list is DERIVED from the difference between the scheduled roles and the ones that survived the
filter, so the sentence a caller reads and the roles a round actually ran cannot disagree; and the
reason is one `const` that both the log line and the clause are built from, so one decision cannot
come to be described in two ways.

**Each omitted role carries the reason ITS OWN rule gave it**, and that is the code round's finding.
Mapping the difference onto a single reason works while there is one rule and tells the caller the
wrong thing with complete confidence the day there are two: a role dropped for some future cause
would be reported as having no rules to judge against. `RolesNotAsked` sits beside
`RolesWithRulesInMind` for that reason — a second rule adds a reason there, next to the filter that
produces it, rather than inheriting one from a mapping somewhere else.

**And a round whose whole roster was filtered out says so in its refusal.** That path returns before
any summary exists, so it would otherwise be refused with a sentence about vendors — sending somebody
to check a configuration that is perfectly correct. Raised twice on the code round.

**`COAI_ROLES` is how a person's own roles arrive, and since 2026-09-12 the PANEL writes it.** The
roles page (`rolesPage.ts`, see [module_extension.md](module_extension.md)) stores exactly the rows
this key carries, so nothing translates between the halves, and a prompt's text goes to
`<dataDir>/prompts/<id>.md` — the override layer `RolePrompts` has read since before roles were data.
A JSON array of rows — id, name, stage,
programmingTask, active, prompts — parsed with the reflex `COAI_VENDORS` and `COAI_PROMPTS_PER_ROUND`
have had since they shipped: JSON this build cannot read is NO custom roles rather than half of them,
so a malformed setting leaves the product running what it ships. What composition refuses row by row
joins `Unrecognised`, which the panel already shows, so a person reads why the role they wrote is not
running.

*Unreadable is not the same as absent, and the two give different answers.* `ParseRoles` returns a
`RolesSetting` — the rows, plus the reason there are none — because falling back to the shipped five
is right in both cases and saying nothing is right in only one: somebody who typed a trailing comma
would otherwise watch their roles simply not appear, and the row-by-row refusals cannot speak for
them, since the parse never reached a row. So `UnknownValues` carries a sentence for the whole
setting, beside the ones it already had for `COAI_RETRY_BACKOFF`, `COAI_ON_EXHAUSTED` and
`COAI_CODE_WORKSPACE`. That sentence carries the PARSER's own diagnosis and the position it stopped
at, counted the way an editor counts — the one part of the message a person staring at forty lines of
JSON cannot work out for themselves — with the parser's "change the reader options" dropped, since
they have a settings file and no reader to change. It is a record rather than a nullable list for
doctrine 4 and 5's reason: "not captured" and "empty" are different facts, and an expected failure is
a value carrying its reason. The value is read ONCE and threaded to both halves — the catalog is
composed from it and the complaint is written from it — unlike the neighbouring diagnostics, which
re-parse a few characters harmlessly: this one decides which roles run, and two reads could describe
two different settings. It also settles which of the two sources wins when
both have an opinion: `COAI_ROLES` is one key, so `SettingsFile.Layer` picks the environment's value
whole, before anything parses it — an unreadable environment value does NOT let the file's roles back
in, which would leave somebody running roles they had already replaced.

The gates are then built for the roles the catalog HOLDS, so a role somebody added gets its
own `COAI_ROUNDS_<ID>`, `COAI_THRESHOLD_<ID>` and `COAI_ENABLED_<ID>` like any other — which is why a
role id is latin, starts with a letter and carries no hyphen. The shipped plan role still honours no
`COAI_ENABLED_` key, by the operator's ruling that this is code review only; a plan-stage role a
person ADDED does, because that ruling was about not turning the one shipped stage off by accident
and a stage with two roles in it has a second one to keep running.

**Two things only a custom role can fail at, and both are said out loud.** Its prompt may have no
text — a role somebody added and never wrote the prompt for — and then the round runs without it and
names it in `NotAsked`, one sentence per role however many vendors would have carried it; without
that guard the whole round died with `the prompt 'CoaiMcp.prompts.req-general.md' is not embedded in
this build`, which is a message about our csproj shown to somebody who mistyped an id. And it cannot
be sent to a Team server, which validates the name against the catalog IT was compiled with: the
vendor is named in the round's excluded list before the launch rather than after a 400, per
(vendor, role) rather than per vendor, so the same Team server goes on running the shipped roles.
Widening that server is plan 3 of this feature.

*Both guards were then wrong at their edges, and the code round said so.* **The prompt question is
about TEXT, not about a file:** `RolePrompts.Has` asked `File.Exists`, so somebody who creates the
file before writing it got a reviewer launched with an empty prompt — the exact silent-shrink the
guard exists to prevent. It now reads the override and answers on non-whitespace, and `Text` falls
back to the shipped default for an empty override rather than handing back the empty file, which for
a shipped prompt is what deleting it already means. **The prompt question is asked FIRST**, because
a role with no text has nothing to say to any vendor: asking the vendor question first told a person
whose only vendor was a Team server that the server did not know their role, which is true and not
the thing they can fix. **The vendor question is about the ROLE's provenance and is asked of the
catalog** (`catalog.ById(role)?.BuiltIn`), not of the prompt's — the two agree today only because
composition refuses a custom role a shipped prompt id, and a rule held up by another rule is one
rename away from neither. **And a role is refused once per vendor**, not once per lens it would have
been dealt: `RoundWork.Excluded` is `ExcludedRole(Provider, Role, Reason)` with the sentence rendered
at the boundary that shows it, so the round can tell it has already said this — the shape `NotAsked`
has had since it shipped.

**The plan stage takes its roster from the catalog too.** It was a hardcoded `[PlanRole]`, so a
plan-stage role a person added was composed, given `COAI_ROUNDS_<ID>` and the enable switch the
shipped plan role deliberately does not have — and then never asked anything, while the code stage
had been reading `RolesForRound` since story B1. And `review_plan` gained the refusal `review_code`
has always had: the shipped plan role honours no `COAI_ENABLED_` key, but a `COAI_ROLES` row saying
`active: false` switches it off like any other, and a round that launches no reviewer is not an empty
round — the session counts it unresolved and never lets the person retry.

*Plural plan roles then broke three things that had been true while there was one.* **A dealt lens
goes to the role the CATALOG says owns it**, and `Lens` has three outcomes rather than two: a lens
whose owner is in the round goes to that owner; a lens the catalog does not know at all still falls
back to the first role, because a stale pick must never leave a round with nothing to ask; and a lens
the catalog knows and gives to a role this round is NOT running is DROPPED, because reassigning it
has a reviewer answer somebody else's question while the round reports the wrong role as asking it.
**The unspent-lens pool is read per role**, one lens each before the first role tops the hand up to
one question per vendor — it was read from `PlanCritique` alone, so a round configured for two plan
roles dealt every question to one of them and nothing to the other. **And the deal happens WITHIN the
vendors that can carry each role**: `Assemble` groups items by their carrier set and deals each group
over its own vendors, because applying the Team-server exclusion after the deal dropped a custom role
out of the round entirely whenever its hand fell to the Team server, with a local vendor sitting
beside it able to run it. `CanCarry` is that one rule, asked before the deal and again in the leaf,
where the non-dealing fan-out still needs it to record why a vendor was not used.

**A session carries its GATES; the catalog belongs to the server that is running.** `PanelConfig` is
persisted inside `SessionState`, and when it gained a `Catalog` the whole thing rode along into every
session file — twenty-five prompts and their prose, and a resumed session read back with the catalog
it was OPENED with, so a role edited today would not reach a session opened yesterday. The property
is `[JsonIgnore]`, and `SessionStore` reattaches the live catalog on the way in. That matters
quietly: `PanelConfig.For(Stage)` asks the catalog which roles a stage HAS, so without the
reattachment a resumed session would take its stage budget from the shipped roles alone and miss the
rounds a person's own role was given. A file written before `config` existed deserialises with a
null one and gets the shipped defaults, which is what it was always running on.

**A role's name reaches a PATH, so it is made safe where the path is built.** `FileSafe.Part`
replaces every character the platform refuses, and the answer file, the local engine's prompt file,
the remote job file and the kept evidence all go through it. Composition already refuses an id that
is not `^[A-Za-z][A-Za-z0-9_]*$`, so nothing shaped like a path should reach an adapter at all —
this is the second lock, and it is here because the two are far apart: the rule lives in the core,
the file name is built in a vendor adapter, and a caller assembling an invocation by hand passes
neither. It was a private helper inside `ReviewerExecutor` whose own remark predicted this: *"`Role`
is an enum today, so nothing observed has ever carried a separator. That is a fact about today's
callers rather than about this function, and it is the callers that change."* Measured on the way
in: a role named `../../../escaped` put the prompt file in the system temp directory instead of the
round's own, and the guard is what stops it.

**The role travels as a string, and since 2026-09-12 there is nothing else for it to be.** Three
reviewers once asked for `ReviewRole` here instead, quoting the rule against primitive obsession;
the answer then was the ring — the enum lived in `CoaiMcp.Runners` and `ReviewerSummary` lives in
the core, which references nothing. The answer now is that the enum is gone. It was a closed list of
five names that every consumer immediately called `.ToString()` on, and a closed list is exactly what
a person defining their own role has to open. `Failures` carries `provider/Role: reason` as it
always did. `StageRun.MakeWork` returns `RoundWork(Reviewers, NotAsked)` rather
than a bare list, because the decision is made where the roles are chosen and the sentence is written
where the round ends, and nothing carried the fact across that gap before.

The end-to-end fixture is exactly this case — a repository with no rule files — so the journey is
asserted where it actually happens, including on a round that ALSO failed: a failure must not swallow
the skip, which is the one path an implementation written for the happy case would have lost.

`RunStageAsync` gained an explicit `isPlanStage` rather than deriving it from `needsWorktree`. That
derivation happens to be right today, and this file already records what deriving the stage cost
twice — `planPrompts is { Count: > 0 }` is empty on an ordinary plan round, and reading the roles
works only because no code round carries `PlanCritique`.

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
as `branch`, its parent as `baseRef`): [.claude/rules/common/review-gate.md](../.agents/rules/common/review-gate.md).

### The gate is split per stage, and code round 1 judges the written rules (2026-09-01)

**`PanelConfig` is two `StageGate`s.** One threshold for both stages was wrong in a way only use
revealed: a plan is a document, so two findings still open is a lot of doubt about a page of text; a
diff is hundreds of lines across a dozen files, and three open there is an ordinary Tuesday. The
number that made the plan gate strict made the code gate a permanent `call_human` — measured on this
product's own rounds, where the plan stage passed at two and the code stage never passed at all.
`PanelConfig.For(Stage)` is the only way to read them, so no call site picks a stage by hand, and the
legacy `COAI_MAX_ROUNDS` / `COAI_GATE_THRESHOLD` become the value for BOTH stages rather than being
dropped.

**A ROUND has a deadline too ([PLAN_a_round_has_a_deadline_too.md](PLAN_a_round_has_a_deadline_too.md), 2026-09-08).** A reviewer was bounded and a round was not — and the
round is what a person watches, so one could run for a long time while every reviewer inside it
behaved. `RunStageAsync` links a `CancellationTokenSource` for the round's own budget: whichever
fires first wins, so a person cancelling still cancels and the deadline cannot outlive its caller.
The scheduler already treats cancellation as a per-reviewer outcome rather than an exception, which
is what makes it small.

**Its default is DERIVED, and that is the design rather than a convenience.** `RoundBudget.For`
takes the reviewer timeout, the number of reviewers and the machine cap, and returns the waves that
division needs — twelve reviewers at a cap of three is four waves, forty minutes at the shipped
settings. Shipping a fixed number instead would cancel healthy rounds on any machine with more
vendors than whoever chose it, and the failure would read exactly like a bug in the gate. A floor of
one full wave is asserted over six shapes. `COAI_ROUND_TIMEOUT_MINUTES` overrides it; zero means
derive, which is why it is read with `CountVar` rather than `IntVar`.

**The summary says the ROUND ran out, not that its reviewers failed.** `ReviewerSummary.EndedByDeadline`
is set only when the round clock fired and the caller's token did not — inferring it from cancelled
reviewers would call it a deadline the moment a person cancels a round themselves.

**The defaults are the panel's, and that is enforced (2026-09-08).** They are `PlanDefault = (1, 6)`
and `CodeDefault = (1, 5)` — one round everywhere, six open findings tolerated on a plan and five on
a diff. One round because the second and third re-raise what the first found rather than finding
more; the thresholds are high because the earlier ones sat where a real change could not pass, and a
gate that blocks everything is a gate people route around.

The numbers must equal the extension's `DEFAULTS`, and the requirement is structural rather than
tidy: `envBlock` writes a `COAI_ROUNDS_*` key only where the value DIFFERS from the panel's default,
so a pristine configuration sends none and this fallback is what runs. They diverged for one day —
the panel displayed one round, the server ran three, and the release that moved the panel's numbers
was named for making them the ones that run. `panelServerDefaultsAgreement.test.ts` READS these two
constants out of `SessionState.cs` rather than transcribing them, and a second test asserts that a
default panel writes no gate key at all.

**A round writes down which MODEL each reviewer was launched with (2026-09-08).** `ReviewerState`
carries it and the rounds log renders it beside the role — a round that named its vendor and its role
but not its model could not answer the question people actually ask about a slow or a weak reviewer,
which is how one came to be investigated by reading the spending ledger instead
([RESULTS_reviewer_input_sizes.md](RESULTS_reviewer_input_sizes.md)). The field is trailing and
defaulted, so sessions already on disk stay valid and simply name none.

**It is what was ASKED for, not necessarily what answered.** A Team server picks the model for the
account it claims and reports none back, and an escalation can run a stronger one. Closing that gap
needs a field on `ReviewStatusDto` and is written up as
[PLAN_the_log_names_the_model.md](../todo/PLAN_the_log_names_the_model.md); until then the log shows
the client's side and the docstring says so rather than letting the number imply more than it knows.

**The vendors are offered in a shuffled order (2026-09-08).** `BuildWork` builds a round
vendor-major and the scheduler starts one task per row against one machine-wide semaphore, which
hands slots out in the order they were asked for — so the list's order is the order reviewers reach
a Team server, and every client ships the same vendor list. The providers are shuffled with the
round's own seed (`StableSeed(sessionId, round)`, the seed the prompt deal already uses), so two
sessions differ while one session replays. **Replay means the same session, the same round AND the
same runnable set**: the shuffle is applied to the providers that pass `Serves(stage)` and `CanRun`,
so a vendor whose health flips changes the shuffle's INPUT rather than only its order. And only the
FIRST start is deterministic — the machine's slots that are free when a round opens are taken in
list order, while who gets a released slot afterwards is `SemaphoreSlim`'s business. It SPREADS the load rather than guaranteeing distinct
orders: with two vendors there are two possible orders and half of all client pairs still collide.
Nothing on the server changes — `JobStore.TryClaim` is FIFO by design, and a fair queue fed in a
biased order is fixed at the feeding end.

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

**A role can also be switched OFF entirely (2026-09-08).** `RoleGate` carries `Enabled`, defaulted
to `true` positionally so that every construction site that predates the switch keeps meaning what it
meant and "absent means on" holds by construction — which matters at three boundaries at once: an
older panel driving a newer server, a stored settings record written before the key existed, and a
role nobody has ever touched. `EnabledRolesOf(stage)` is the named member both `RolesForRound` and
`For(Stage)` read, so a role that is off lends the stage neither its rounds nor its threshold; with
every code role off it answers empty and `For(Stage)` is `(0, 0)` rather than an exception out of
`Max`. `ReviewCodeAsync` refuses that round before the scope check and before any worktree, naming
the four boxes and the env variable — because a round no reviewer answered is counted UNRESOLVED, so
it would sit open for ever and the next call would be refused for the wrong reason.

The env key is `COAI_ENABLED_<ROLE>`, and it is read by `NotSwitchedOff` rather than by the ordinary
`Flag` helper: only the four spellings of false disable a role, and absent, empty, `no`, a typo and a
shell-mangled value all leave the reviewer working. The asymmetry is deliberate — a role wrongly on
costs one extra pass, a role wrongly off is a review nobody performed with nothing saying so. The
plan role is never disabled: the boundary refuses it rather than trusting that nobody writes the key.

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
