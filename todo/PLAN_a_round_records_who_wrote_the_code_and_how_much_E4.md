# PLAN — E4 stories: attribution — windows, the association ladder, ambiguous totals, contributors

> Status: **plan only, nothing implemented yet, 2026-10-14.** Epic E4 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> Scope: `src_mcp/core/Authors/` (new, pure) and `src_mcp/tests/` (new test classes). No schema step, no
> call site, no I/O — those are E5's. Related docs: [module_server.md](../research/module_server.md),
> [PLAN_local_db.md](../research/PLAN_local_db.md).

## 0. What this epic delivers, and what it does not

`RoundAttribution` turns ONE caller session's observation (E3's `ObservedSession`) plus the gate session's
round journal into, per round: associated activity per window and agent, the round's **ambiguous** totals,
what was credited **elsewhere**, the round's authors **state**, and — derived — the **contributors** to the
change under review (§6 of the parent). It reads no file, starts no process, reads no clock and stores
nothing; E5.S4 calls it before the round is saved (`RoundEngine.cs:483-491`, saved at `:586`), E5.S1/E5.S2 store
what it returns, E6 reads the stored result, E7.S2 runs it again over old rounds.

**Home: `src_mcp/core/Authors/`, namespace `CoaiMcp.Core.Authors` — a deliberate deviation** from the epic's
"a new file beside `CallerTranscript.cs`" (`src/Store`). "Pure, no I/O" is only *enforced* in the core:
`ArchitectureTests.cs:17-24` fails the build when `CoaiMcp.Core` references a process or network assembly,
and the core is where the gate's other pure rules live (`core/Gate/GateRule.cs`). In `src/Store` the purity
would be a habit. The parent records this deviation on promotion.

**What E4 assumes about E3** (by story id):

| E3 delivers | E4 needs from it |
|---|---|
| E3.S4: the reader's value, `ObservedSession` | one record per `message.id` with ONE instant (E3's rule), its agent, observed `model`, `requested`, `effort` (absent = unknown) and the four token terms |
| E3.S4: the counting rules (tokens once per `message.id`, launches once per `agentId`) | each agent's FIRST-LINE instant, its parent (main via `toolUseId`, or `parentAgentId`), `agentType`, the meta's requested alias |
| E3.S3: the write test (`WriteTarget`) | **each write as an instant plus its path** — not only a boolean per (agent, repository); see defect 1 in §7, settled by parent §16 D4 |
| E3.S4: the four states | the session-level state and every `partial` reason, with the instant it concerns where there is one (an `Agent` call without a transcript has one; a torn last line does not) |

If E3's `ObservedSession` lives in the core, E4.S1 consumes it directly and adds only what is missing. If it
lives in `src/Store`, E4.S1's input records are the core contract and ONE mapping in `src/Store` (E5's call
site or E3's reader) fills them — never a second reader.

## 1. The stories

| Story | Title | Depends on | Model |
|---|---|---|---|
| E4.S1 | The contract: what attribution reads, and which repository a path is in | E3.S4 (`ObservedSession`), E3.S3 (`WriteTarget`) | brief: opus (this document) · code: sonnet · review: opus |
| **E4.S2** | **Windows and the association ladder** — `Cornerstone — spec by Fable` | E4.S1 | spec: **fable** · code: sonnet · review: opus |
| E4.S3 | Rows, totals and the round's state | E4.S2 | brief: opus · code: sonnet · review: opus |
| E4.S4 | Contributors to the change under review | E4.S3 | brief: opus · code: sonnet · review: opus |
| E4.S5 | The acceptance cases over E1's real fixtures | E4.S3, E4.S4, E3.S2 + E3.S4 (the reader), E1.S5 (fixtures) | brief: opus · code: sonnet · review: opus |

Build order: S1 → S2 → S3 → S4 → S5; S1 may be coded while Fable writes S2's spec. One branch, one commit for
the epic (the parent's `## Epics` rule); the coai gate once for the epic.

**Cornerstone ledger.** E4 marks ONE story (E4.S2). The parent's ledger reads `Cornerstones: 3 of 3 — E2.S1,
E3.S4, E4.S2`, and the parent's E4 links this file (parent §16 D21).

**Why S2 and only S2.** The epic's cornerstone area is "the window tiling and the association ladder", and
the two cannot be specified apart: whether a message is ambiguous depends on which window it fell into, and
which window may own it depends on what the other gate sessions of the same caller already claimed. Every
number the report prints is a sum over what S2 assigns; an error there has no symptom (a doubled ambiguous
total looks like a busy session). S3 and S4 are arithmetic over S2's assignment, S1 is a type and a prefix
test, S5 runs the cases end to end — each is caught by S2's identities if it is wrong.

## 2. Conventions every story in this epic follows

- **Pure.** No `File`, `Directory`, `Environment`, `Process`, `DateTime.Now`/`UtcNow`, `TimeProvider`,
  `Guid.NewGuid`, `Random`, or hash-order-dependent output. Every instant is an argument. S1 adds a
  structural test that holds this for the `Authors` folder.
- **UTC instants as `DateTimeOffset` with zero offset** (utc-timestamps.md rule 1). A `RoundRecord`
  `DateTime` arriving with `Kind.Unspecified` from a session file is the UTC value it was written as —
  `DateTime.SpecifyKind(value, DateTimeKind.Utc)`, never `ToUniversalTime()`, which would move it by the
  reading machine's offset. `default(DateTime)` means *not recorded* and maps to `null`, never to an instant.
- **Unknown is a value, never 0 and never a default.** Effort absent → `Effort.Unknown` (the constant
  `"unknown"`), not `high`, not the session's. A token total that could not be read is not a zero. This is the
  benchmark's convention, carried over not referenced (`dew_flow_benchmark` at `4dc98df`:
  `src/Bench.Domain/Sessions/SessionTrace.cs` — `MutationEvidence.NotChecked` is "never a synonym for
  `Unchanged`"; `ToolTaxonomy.cs` — an unknown tool "is never guessed into" a class). Its shell-command
  heuristic is NOT carried over: `Bash`/`PowerShell` are never write evidence here.
- **No text.** E4 holds paths in memory only to test membership; no output record carries a path, a prompt,
  a description, a tool input or a message. Output identities are: the gate's `repo_path` (already stored),
  the caller session id (already stored as `rounds.caller`), and opaque agent ids.
- **Tests**: xUnit v3 + FluentAssertions in `src_mcp/tests/`, one class per guarantee, named as a sentence
  (`RoundNumberTests.cs` is the shape: a private builder, one `[Fact]` per rule, a `<summary>` that says the
  rule). Built and run as:

  ```bash
  dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
  node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*Attribution*"
  node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
  ```

  Never `dotnet test`. Each story's new tests are watched RED (behaviour absent, failing on the assertion —
  not on a missing type or a builder that the code rejects) before the code, and every break-it listed is
  planted, watched red with the named symptom, and restored (testing.md, *When the fix already landed*).
- **Files** stay under 400 lines; one type family per file.

---

## E4.S1 — The contract: what attribution reads, and which repository a path is in

**Goal.** The input records of attribution, one pure repository-membership function, a scenario builder every
later test uses, and a structural test that keeps the `Authors` folder pure. No attribution logic.

**Scope (verified 2026-10-14).**

| Where | What |
|---|---|
| `src_mcp/core/Authors/AttributionInput.cs` (new) | the input records below |
| `src_mcp/core/Authors/Repositories.cs` (new) | `RepositoryOf(path, universe)` — longest containing root, or none |
| `src_mcp/src/Store/AgentLog.cs:304-317` | `InRepo` + `Normalise`: the separator-aware, case-insensitive containment test that exists today. **Reuse, don't copy**: move the rule into `Repositories` as a public static and have `AgentLog.InRepo` call it (reuse-first move 2: extract the shared half). `AgentLog`'s behaviour is unchanged (§3 of the parent) |
| `src_mcp/src/Server/SessionStore.cs:78-92,104,193,236,248` | `RoundRecord(Stage, Number, …)`, `Status`, `StartedUtc`, `Sha`, `Caller`; `PersistedSession.Rounds`, `OpenedUtc` — the sources of `GateTimeline`, mapped by E5, not here |
| `src_mcp/core/Rounds/SessionState.cs:382-386` | `SessionState.SessionId`, `RepoPath` — the gate session's identity |
| `src_mcp/src/Server/RoundNumber.cs:24-26` | "the session's rounds as persisted — every stage, in the order they ran": the journal `GateTimeline.Rounds` mirrors |
| `src_mcp/tests/AttributionScenario.cs` (new) | the builder |
| `src_mcp/tests/ArchitectureTests.cs:17-24`, `tests/ProductionSources.cs:38` | the precedent and the scanner the new purity test uses |

**The records** (names binding; members may gain fields when Fable's S2 spec needs them):

```csharp
namespace CoaiMcp.Core.Authors;

public readonly record struct AgentKey(string Id)            // "main" or the agentId
{ public const string MainId = "main"; public static AgentKey Main => new(MainId); public bool IsMain => Id == MainId; }

public readonly record struct TokenTerms(long Input, long Output, long CacheCreation, long CacheRead);

public sealed record ObservedMessage(AgentKey Agent, string MessageId, DateTimeOffset At,
    string Model, string Requested, string Effort, TokenTerms Tokens);   // Effort "" → Effort.Unknown at the boundary
public sealed record ObservedAgent(AgentKey Key, AgentKey Parent, string AgentType,
    string Requested, DateTimeOffset FirstLineAt, bool TranscriptPresent);
public enum WriteKind { Checkout, OutsideAnyCheckout, Unresolved }                          // 1:1 with E3.S3's WriteTargetKind
public sealed record ObservedWrite(AgentKey Agent, DateTimeOffset At, string Path, WriteKind Kind, string Where); // E3 ObservedWrite.Target mapped field for field: Kind = Target.Kind, Where = Target.Where (a normalised root KEY / directory / empty); Path = the tool's path
public sealed record UnobservedLaunch(AgentKey Parent, string ToolUseId, DateTimeOffset? At, string Why); // E3's UnobservedLaunches, 1:1; At null → every window (as a StateReason with no instant)
public sealed record StateReason(string Why, DateTimeOffset? At);                       // At null = the whole session
public sealed record ObservationState(string Kind, ImmutableArray<StateReason> Reasons); // observed | partial | unobserved

public sealed record CallerObservation(string CallerSessionId, ObservationState State,
    ImmutableArray<ObservedMessage> Messages, ImmutableArray<ObservedAgent> Agents,
    ImmutableArray<ObservedWrite> Writes, ImmutableArray<UnobservedLaunch> Unobserved);

public sealed record GateRound(string Stage, int Number, DateTimeOffset? StartedUtc,
    string Status, string Verdict, string CallerSessionId,
    DateTimeOffset? CompletedUtc);                                                       // E4.S2's rank key; null ranks last
public sealed record GateTimeline(string GateSessionId, string RepoPath,
    DateTimeOffset? OpenedUtc, ImmutableArray<GateRound> Rounds);                        // Rounds in journal order

public sealed record AttributionInput(CallerObservation Caller, GateTimeline Gate,
    ImmutableArray<GateTimeline> Peers, ImmutableArray<string> Repositories,
    string Stage, int Number,                                                              // the round being attributed
    bool PeersKnown);                                                                      // false → partial: prior windows unknown, Peers empty
```

**Mapping from E3** (E3.S4's contract, done by the ONE mapping E5.S4 owns): `ObservedAgent.FirstLineAt =
LaunchedUtc ?? CalledUtc` — so a meta without a transcript is placed at the instant of the `Agent` call that launched
it; `TranscriptPresent` as is; `UnobservedLaunch` is E3's `UnobservedLaunches`, one for one; `Effort` `"unknown"` as
is; `TokenTerms` from `TokenUsage` term by term. Nothing in the mapping decides anything.

`Peers` are the OTHER gate sessions this caller session served (E5 finds them in the store by
`rounds.caller`); `Repositories` is the universe of repository roots membership is decided against — which
roots go into it is S2's decision (S2 decision 6); S1 only implements the lookup.

**`Repositories.RepositoryOf(string path, IReadOnlyList<string> universe)`** → the LONGEST root in the
universe that equals or contains `path`, or `""`. Rule: both sides through `Normalise` (backslash → slash,
trailing slash trimmed); equality or `StartsWith(root + "/")`, `OrdinalIgnoreCase` — exactly
`AgentLog.cs:311-314`, now in one place. Longest wins, so a worktree nested inside another checkout
(`D:/rsd/x/.claude/worktrees/y`) is `y`, not `x`. Relative paths and `""` → `""` (never resolved against a
working directory, which would be I/O). A WSL `/mnt/d/…` path and a `D:\…` root are NOT equated — no
translation is guessed; E5 passes the universe in the spelling the transcript uses, and S5 checks it.

**The scenario builder** (`AttributionScenario`, tests only): a fluent value that builds an `AttributionInput`
from short statements — `.Opened(t)`, `.Round("CodeReview", 1, startedAt: t)`,
`.Main(at: t, model: "claude-opus-5-5", tokens: …)`, `.Agent("a1", parent: main, launchedAt: t, requested: "sonnet")`,
`.Message("a1", at, model, effort)`, `.Write("a1", at, path)`, `.MissingChild(parent, at, why)`,
`.Peer(timeline)`. Every instant is ONE fixed base `2026-10-14T12:00:00Z` plus minutes, never `now`
(testing.md, *A test that reads the clock expires*). Message ids are generated deterministically (`m-0001`, …).
S1 adds `AttributionInput.Validate()` returning the list of contract violations (a message whose agent is not
declared, a write by an undeclared agent, rounds out of journal order, a `(Stage, Number)` not in
`Gate.Rounds`, the caller's `CallerSessionId` differing from the attributed round's, a `MessageId` twice), and the
builder asserts it is empty — so no later test can pass on a fixture the code quietly declines (testing.md, *A fixture
the code REJECTS proves nothing*). `RoundAttribution.Assign` throws `ArgumentException` naming the first violation when
`Validate()` is not empty; E5.S4 maps that to `unobserved: invalid input`.

**The coder's brief (Sonnet).**

1. Write the tests below; build; watch them fail on assertions (create the types as empty shells first so the
   failure is the assertion, not a compile error).
2. Create `core/Authors/AttributionInput.cs` with the records; `Effort.Unknown = "unknown"` and the
   `ObservationState` kinds as constants in the same file. `Validate()` is a pure method on `AttributionInput`.
   Add `UtcInstant.From(DateTime) → DateTimeOffset?` (`default` → `null`, `SpecifyKind(Utc)` otherwise) for
   E5's mapping of `RoundRecord.StartedUtc` and `OpenedUtc`.
3. Create `core/Authors/Repositories.cs`: `Normalise`, `Contains(root, path)` (moved from `AgentLog.cs:304-317`)
   and `RepositoryOf`. Change `AgentLog.InRepo` to call `Repositories.Contains` — no other change to `AgentLog`.
   Run `AgentLogTests` before and after; it stays green.
4. Create `tests/AttributionScenario.cs`.
5. Add the purity test (below).
6. Build, run the filter, run the whole suite.

**Test scenarios.**

| Class | Facts | Asserts |
|---|---|---|
| `APathBelongsToTheLongestRepositoryThatHoldsItTests` | `AFileUnderTheRoot_IsInIt` / `TheRootItself_IsInIt` / `ASiblingWithTheRootAsPrefix_IsNot` (`/repo-two` vs `/repo`) / `BackslashesAndATrailingSlash_DoNotMatter` / `CaseDoesNotMatter` / `ANestedWorktree_BelongsToTheInnerRoot` / `ARelativeOrEmptyPath_IsInNone` / `AWslSpellingIsNotTranslated` | the exact root or `""` |
| `AgentLogTests` (existing) | unchanged | `InRepo`'s behaviour kept through the move |
| `TheAttributionInputSaysWhatIsWrongWithItTests` | one fact per violation above (the caller-mismatch and the duplicate `MessageId` included), plus `AWellFormedScenario_HasNoViolations` (the positive one) | the violation list |
| `ARoundStartFromASessionFileIsReadAsUtcTests` | `AnUnspecifiedKind_KeepsItsHourAndGetsZeroOffset`; `TheDefaultInstant_IsNotRecorded` | `UtcInstant.From` |
| `TheAttributionCoreTouchesNoClockAndNoDiskTests` | `NoAuthorsSourceNamesTheClockTheDiskOrTheEnvironment` — reads `src_mcp/core/Authors/*.cs` through `ProductionSources` and asserts none contains `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `TimeProvider`, `File.`, `Directory.`, `Environment.`, `Guid.NewGuid`, `new Random`; and `TheScannerSeesTheAuthorsFolder` — at least one file was read (testing.md, *A structural test that matches nothing passes forever*) | the source text |

**Break-its.** (a) Drop the `+ "/"` from `Contains` → `ASiblingWithTheRootAsPrefix_IsNot` red. (b) Return the
FIRST containing root instead of the longest → `ANestedWorktree_BelongsToTheInnerRoot` red. (c) Add
`DateTime.UtcNow` to any `Authors` file → the purity test red. (d) Implement `UtcInstant.From` with
`value.ToUniversalTime()` → `AnUnspecifiedKind_KeepsItsHourAndGetsZeroOffset` red on any machine not at
+00:00. It cannot go red on a UTC machine; the test's summary says so, and the break-it is watched on this
operator's machine (not at +00:00).

**Done when.** The records, `Repositories` and the builder exist; `AgentLog` calls the moved rule and its
tests are green; every fact above is green and every break-it was watched red; `CoaiMcp.Tests.exe` green.

**Dependencies.** E3.S4, which defines `ObservedSession` (for the field list; if it has not landed, S1
defines the core records and E3.S4 maps onto them), and E3.S3 (`WriteTarget`). **Model:** brief by opus (this document) · code:
sonnet · review: opus.

---

## E4.S2 — Windows and the association ladder · `Cornerstone — spec by Fable`

> **Spec written by Fable, 2026-10-14.** It replaces the candidates Opus listed; §S2.2 records, per decision, which
> candidate was taken and which was replaced, with the reason. Everything below is binding for the Sonnet coder; where
> it needs a member the S1 records do not yet have, §S2.7 names it.

**Goal.** One pure function, `RoundAttribution.Assign(AttributionInput) → WindowAssignment`, that (a) tiles the gate
session's journal into half-open windows — `before-open` for the first recorded round, then `round (stage, N)` from
round N−1's **start** to round N's start — and (b) places every message, launch and write of the caller session that
falls into THIS round's windows exactly once, with one association each: `wrote here`, `child of`, `same-session`,
`ambiguous`, `wrote elsewhere` or `claimed by peer`. The output keeps `message.id`s, agent keys, instants, model,
effort and tokens: S3 sums it, and the two identities (§S2.5) are checked on it.

**Scope (verified 2026-10-14).** New, all under `src_mcp/core/Authors/`, each < 400 lines, cyclomatic complexity ≤ 4 per
function: `WindowAssignment.cs` (the output records and the two closed vocabularies), `Windows.cs` (tiling and
ranking), `AssociationLadder.cs` (the four rungs), `Ownership.cs` (claimants), `RoundAttribution.cs` (`Assign`,
`ReadFrom`). Tests in `src_mcp/tests/`. The rule this replaces for attribution — not for `agent_log`, which keeps its
own — is `WhatTheCallerWasDoing`, `src_mcp/src/Server/Rounds/RoundEngine.cs:893-902`: a window from the previous
round's `CompletedUtc` (`:895`), floored at `OpenedUtc` (`:896`). The journal is every stage in the order it ran
(`src_mcp/src/Server/RoundNumber.cs:25`); `RoundRecord.StartedUtc` is `SessionStore.cs:92`, stamped by `LiveRound.cs:279`
for a round that ran and by `RoundSkips.cs:105` for a skip; `PersistedSession.OpenedUtc` is `SessionStore.cs:248`. A skip
is `Verdict = skipped`, `Status = done`, with a sha (`RoundSkips.cs:102-110`); a repeated skip moves only its
`CompletedUtc` (`:95-99`). An interrupted round is `Status = interrupted` (`SessionStore.cs:594-597`) and never reaches
the call site E5 adds inside the record's `with` block (`RoundEngine.cs:483-491`, saved at `:586`). The round's completion
stamp, `RoundRecord.CompletedUtc` (`SessionStore.cs:84`), is what §S2.3 ranks claims by. The containment rule is S1's
`Repositories.Contains` / `RepositoryOf` (moved from `AgentLog.cs:304-317`).

### S2.1 The model in one paragraph

A gate session's **recorded** rounds are those that will carry an authors snapshot: `Stage ∈ {PlanReview, CodeReview}`,
`Verdict ≠ skipped`, `Status ≠ interrupted`. Each recorded round owns one **window** `[previous recorded round's start,
own start)`; the first recorded round also owns the **`before-open`** window `[prior end, open)`. Everything else in the
journal — skips, interrupted rounds, feature and document rounds, rounds older than `StartedUtc` — opens no window, so
its stretch lands in the next recorded round (parent §16 D1, D2) and nothing is ever lost to a round that records no
authors. A message is placed by its one instant into the one window of this gate that contains it. Its **association**
is the first rung of the ladder that holds for its agent in that window (§S2.4). Whether this gate **owns** it, or a
peer gate session of the same caller already did, is decided by **completion rank** (§S2.3): the window whose round
completed first claims, and a claim can only ever be made from what the store already held at that moment — which is
what makes the live record and a later backfill agree. Writes are evidence and never contested: a write into this
repository is `wrote here` here and `wrote elsewhere` from every other gate.

### S2.2 The ten decisions, closed

| # | Decision | Candidate | Reason |
|---|---|---|---|
| 1 | **Boundaries.** A window opens for every recorded round (the predicate above) and for nothing else. A recorded round whose `StartedUtc` is null (older than the field) is **blind**: its window has no edge, it is `unobserved: no round start`, and so is the next recorded round, whose window would start at the blind one's unknown start; the round after that is tiled normally. `OpenedUtc` null → the first recorded round is `unobserved: no open time`. An open stamped after the first round's start is clipped to that start (the before-open window ends there, the round-1 window is empty). Feature and document rounds open no window — E5 §3 gives them no snapshot, so a window of theirs would lose its stretch. | kept, extended | D1/D2 settle skips and interruptions; the blind rule keeps the candidate because a window with one unknown edge over-credits the next round with no symptom — the one error this epic exists to avoid — while `unobserved` is counted as excluded by E6. |
| 2 | **Several callers.** Round N reads its OWN caller's transcript over the whole window. If the previous recorded round's `CallerSessionId` differs → the round window is `partial: caller changed`; if it is empty (an old row, a `repo:` caller) → `partial: prior caller unknown`. The input must carry that caller: `Caller.CallerSessionId == Gate.Rounds[this].CallerSessionId` is a `Validate()` rule. | kept (E4/6) | the other caller's activity after N−1 started is in a transcript this round did not read; the window is a floor and says so. |
| 3 | **Overlap of two gate sessions.** Every window of the gate and of every peer served by this caller is ranked by `(round.CompletedUtc, gate session id ordinal, kind: before-open before round)`; a null completion ranks last. A message in this round's window whose own association is OWNING (`wrote here`, `child of`, `same-session`, `ambiguous`) is **`claimed by peer`** when a peer window containing its instant ranks BEFORE this round's window and the agent's association in THAT window (by the peer's repository, the peer's window evidence) is owning. A peer window whose round has another caller is never a claimant. | **replaced** (was: earliest-closing by start) | a claim must be decidable from what the store held at the claiming round's own live time. The store learns of rounds in completion order (`Projection.Write`, `RoundEngine.cs:607`, is synchronous at completion); earliest-closing-by-start is not: a peer round that STARTED earlier but completes later, or dies interrupted, is invisible or mutable at this round's live time, so its claim would differ between live and backfill. Under completion rank the later-completing round always defers to the earlier one it can see. |
| 4 | **An agent that wrote into two repositories in one window** (here and elsewhere, by this gate's own window). Its association is `ambiguous` with rung `two repositories`; it gets a `wrote here` ROW whose `write_calls` are its writes into this repository and whose turns and tokens are ZERO; its messages are owned once — by this gate (into the ambiguous totals) or by the peer that ranks first (`claimed by peer`); it counts one ambiguous AGENT in every gate whose window sees two repositories. Launches follow the messages. | kept, made exact | D5: counted ambiguous in each candidate, tokens never twice; the row carries the authorship evidence (write calls) so S4 lists it as a contributor of each repository it wrote into without a token being added twice. |
| 5 | **Rule 2.** Only a SUBAGENT parent propagates, through `ObservedAgent.Parent` where `!Parent.IsMain`, recursively up a chain of subagents, and only when the parent is `wrote here` or `child of` **in the same window**. `main`'s association never propagates. | kept (E4/4) | `toolUseId` makes main the parent of every depth-1 agent, so a main that wrote here would associate every scout of every other repository. |
| 6 | **Universe.** E5 passes `Repositories` = the gate's `repo_path` ∪ every peer's `repo_path` ∪ every `Checkout` root E3 resolved for a write in this observation (`WriteTarget.Where`). the write's root is `RepositoryOf(write.Where, universe)` for `Kind = Checkout` (the longest containing root, so a submodule or nested worktree resolves to its own root, not its superproject) and for `Unresolved` with a non-empty `Where` (a removed worktree's directory — the root only if some universe root contains it), and `""` for `OutsideAnyCheckout` — the mapping is tested for each kind, including a submodule root, a nested worktree and an unresolved write; `""` is `outside` and is never evidence. A root is `here` when `Contains(gate.RepoPath, root)`; a **destination** (rule 4) is the gate or peer `repo_path` containing a root, else the root itself, so a submodule and its superproject are one destination. | settled (D3) | — |
| 7 | **Evidence horizon.** This round's windows end at its start, so its own ladder and ownership see only lines stamped before the start (D6). A PEER's claim is evaluated on the peer's own window, which may end after this round's start but always before the peer's completion, hence before this round's live read. Lines at or after this round's COMPLETION never change the result. | **refined** (D6) | truncating the input at this round's start would evaluate a peer's claim on less evidence than the peer itself had: in the overlap of two windows an agent that wrote into the peer's repository only after this round's start reads `elsewhere` to the peer and owning to us — one message in two rows. Exactness across gates needs the peer's evidence as the peer saw it. |
| 8 | **Edges.** Half-open `[From, To)`; a message at exactly a round's start is in the NEXT window. A launch is the agent's `FirstLineAt`, in that window only; main has no launch; a resumed agent launches nothing again. Association is per (agent, window); decision 4 is the only case where an agent's messages and its row part company. | kept | — |
| 9 | **A missing child.** An `UnobservedLaunch` (an `Agent` call without a transcript) and an `ObservedAgent` with `TranscriptPresent = false` (a meta without one) are both **unobserved launches** of the window containing their instant: that window is `partial: <why>`, they get no row, no launch placement and no tokens — never a zero. Other windows stay `observed`. | kept, widened to the meta case | a row with zero tokens reads as an observed zero. |
| 10 | **Clocks.** Transcript instants are the client's clock, round stamps the server's; compared in UTC with no tolerance. Stated limitation: two rounds of two gates completing within the same instant and projected in the other order can make a live record differ from the backfill in which of them owns a contested message; the backfill is the authority and a second backfill changes nothing. | kept, one limitation added | a tolerance is a guess. |

### S2.3 Windows and ownership — the exact rules

**Recorded** (`Windows.IsRecorded(GateRound r)`): `r.Stage is "PlanReview" or "CodeReview"` ∧ `r.Verdict != "skipped"` ∧
`r.Status != "interrupted"`. The spellings are `Stage` enum names (`core/Rounds/SessionState.cs:264-267`),
`RoundRecord.Skipped` and `RoundRecord.Interrupted` (`SessionStore.cs:88,121`).

**Tiling** (`Windows.Of(GateTimeline g, DateTimeOffset? priorEnd) → ImmutableArray<Window>`), over the recorded rounds
`r_1 … r_n` in journal order:

```
Window(Gate, Kind, Stage, Number, From, To, CallerSessionId, CompletedUtc, Blind)
  before-open  : Kind "before-open", Stage/Number/Caller/Completed of r_1, From = priorEnd (null = unbounded), To = min(open, r_1.start)
  round k = 1  : Kind "round",       From = min(open, r_1.start),                             To = r_1.start
  round k > 1  : Kind "round",       From = r_{k-1}.start,                                    To = r_k.start
  Blind        : r_k.start is null, or (k > 1 and r_{k-1}.start is null), or (k = 1 and open is null)
```

A blind window has `From = To = null` and takes part in nothing. `Validate()` (S1) already refuses a journal whose
recorded starts decrease; equal starts give an empty window, which is legal.

**Prior end** (`Windows.PriorEnd(gate, peers, caller)`): the maximum `StartedUtc` over the recorded, non-blind rounds
of every peer whose `RepoPath` is `Normalise`-equal to the gate's, whose `CallerSessionId == caller`, and whose
`StartedUtc ≤ open`; null when there is none, or when `PeersKnown` is false. It is a READ bound, not an ownership
rule: §S2.6 case 7 proves the owned set is the same with and without it.

**Rank** (`Windows.Rank(Window w)`): the tuple `(w.CompletedUtc ?? MaxValue, w.Gate, w.Kind == "before-open" ? 0 : 1)`,
compared ordinally on the gate id. `Windows.Precedes(a, b)` is `Rank(a) < Rank(b)`.

**Claimants** of this round's window `W` (`Ownership.ClaimantsOf(W, peerWindows, caller)`): every peer window `P`
with `P.CallerSessionId == caller`, not blind, `Precedes(P, W)`, and `P ∩ W ≠ ∅`, in rank order.

**Ownership** of one item (a message or a launch) of agent `g` at instant `t` in `W`
(`Ownership.Of(item, W, claimants, ladder)`):

1. `own = ladder(g, W, gate)`. If `own == wrote elsewhere` → the item is **`wrote elsewhere`** (not owned; the root is
   kept on the placement). Claimants are not consulted: elsewhere is this gate's own evidence.
2. Otherwise, for each claimant `P` in rank order with `P.From ≤ t < P.To`: if `ladder(g, P, P's gate)` is owning →
   the item is **`claimed by peer`** (`OwnerGate = P.Gate`). Stop at the first.
3. Otherwise the item is owned here with association `own`, rung as the ladder says.

**Owning** associations: `wrote here`, `child of`, `same-session`, `ambiguous`. Non-owning: `wrote elsewhere`,
`claimed by peer`.

**What is placed.** Every `ObservedMessage` whose `At` lies in a non-blind window of THIS round, as a `Placement`;
every subagent whose `FirstLineAt` lies in one, as a `LaunchPlacement` — except agents with `TranscriptPresent = false`,
which become `UnobservedLaunch` items instead; every `ObservedWrite` whose `At` lies in one, as a `WritePlacement`
with its root and `here | elsewhere | outside`; every `UnobservedLaunch` whose `At` lies in one. Nothing outside the
round's windows is placed, which is the evidence cutoff: a line stamped at or after this round's start is in no window
of this round.

**States** (`WindowState(Window, Kind, Reasons)`), in this order of strength, strongest wins, reasons accumulated and
distinct in first-seen order: `Caller.State.Kind == unobserved` → every window `unobserved` with its reasons; a blind
window → `unobserved: no round start` (`no open time` when it is the open that is missing); `PeersKnown == false` →
`partial: prior windows unknown` on every window; an `UnobservedLaunch` or a `TranscriptPresent = false` agent in the
window → `partial: <its Why>`; a `StateReason` of the observation with `At` inside the window → `partial: <Why>`;
one with `At == null` → `partial: <Why>` on every window; the round window whose previous recorded round has another
caller → `partial: caller changed` / `partial: prior caller unknown`; none of these → `observed`. Reason words are a
closed set: `AttributionReasons` holds `NoRoundStart = "no round start"`, `NoOpenTime = "no open time"`,
`PriorWindowsUnknown = "prior windows unknown"`, `CallerChanged = "caller changed"`,
`PriorCallerUnknown = "prior caller unknown"`; every other reason is E3's constant, copied verbatim, never composed.

**`ReadFrom(AttributionInput) → DateTimeOffset?`** — the lower bound E5 hands E3's reader BEFORE reading (parent §16 D7),
computable from timelines alone: the minimum `From` over this round's non-blind windows and over their claimants
(`Windows` + `Ownership.ClaimantsOf`); null when any of them is unbounded. E5 calls it first, reads from it, then calls
`Assign`; the backfill does the same. A line before `ReadFrom` is in no window and no claimant, so not reading it
changes nothing — that is a test.

### S2.4 The ladder — `AssociationLadder.Of(AgentKey g, Window w, GateTimeline gate, AttributionInput input)`

Evaluated per (agent, window, gate); memoised per call of `Assign` (a plain dictionary, filled in a deterministic
order — no hash-order output). Four private statics, one rung each, complexity ≤ 4:

| Rung | `WriteRung` — the agent's own writes in `w` | Result |
|---|---|---|
| roots = distinct `RepositoryOf(write.Where, input.Repositories) (by Kind, decision 6)` over `write.Agent == g`, `w.From ≤ write.At < w.To`, root ≠ `""` | here = roots with `Contains(gate.RepoPath, root)`; elsewhere = the rest | here ≠ ∅, elsewhere = ∅ → **`wrote here`** · here ≠ ∅, elsewhere ≠ ∅ → **`ambiguous`**, rung `two repositories` · here = ∅, elsewhere ≠ ∅ → **`wrote elsewhere`** (roots kept) · roots = ∅ → next rung |
| `ChildRung` | `p = input.Agents[g].Parent`; `p` declared, `!p.IsMain`, and `Of(p, w, gate)` ∈ {`wrote here`, `child of`} | **`child of`** · else next rung |
| `SessionRung` | `destinations(w)` = distinct destination of every write by ANY agent in `w` with root ≠ `""`, where destination = the longest of {gate.RepoPath} ∪ peers' RepoPaths that `Contains` the root, else the root | `destinations == {gate.RepoPath}` → **`same-session`** · otherwise (none, another, or several) → **`ambiguous`**, rung `no evidence` |

`main` is an agent like any other (`who = main` is S3's word for `AgentKey.IsMain`): it can be `wrote here`, two
repositories, `wrote elsewhere`, `same-session` or `ambiguous`; it has no parent and no launch. A write with root `""`
(outside) is dropped before every rung. `cwd`, `AgentType`, `Requested`, launch time and the parent's `cwd` are read by
no rung — the input has no `cwd` to read, and the break-it in §S2.9 keeps it that way.

### S2.5 The output and the two identities

```csharp
namespace CoaiMcp.Core.Authors;

public static class Association   // closed vocabulary
{ public const string WroteHere = "wrote here", ChildOf = "child of", SameSession = "same-session",
    Ambiguous = "ambiguous", WroteElsewhere = "wrote elsewhere", ClaimedByPeer = "claimed by peer";
  public static bool IsOwning(string a) => a is WroteHere or ChildOf or SameSession or Ambiguous; }

public static class Rung { public const string Write = "write", TwoRepositories = "two repositories",
    Child = "child", Session = "session", NoEvidence = "no evidence"; }

public sealed record Window(string Gate, string Kind, string Stage, int Number, DateTimeOffset? From, DateTimeOffset? To,
    string CallerSessionId, DateTimeOffset? CompletedUtc, bool Blind)
{ public bool Contains(DateTimeOffset t) => !Blind && (From is null || From <= t) && t < To; }

public sealed record Placement(string MessageId, AgentKey Agent, DateTimeOffset At, Window Window,
    string Association, string Rung, string OwnerGate,            // OwnerGate: this gate, the peer, or "" when wrote elsewhere
    string Model, string Requested, string Effort, TokenTerms Tokens);
public sealed record LaunchPlacement(AgentKey Agent, DateTimeOffset At, Window Window, string Association, string Rung, string OwnerGate);
public sealed record WritePlacement(AgentKey Agent, DateTimeOffset At, Window Window, string Root, string Where); // here | elsewhere | outside
public sealed record UnobservedLaunchPlacement(AgentKey Parent, DateTimeOffset At, Window Window, string Why);
public sealed record AgentAssociation(AgentKey Agent, Window Window, string Association, string Rung, ImmutableArray<string> ElsewhereRoots);
public sealed record WindowState(Window Window, string Kind, ImmutableArray<string> Reasons);   // observed | partial | unobserved

public sealed record WindowAssignment(string Gate, string Stage, int Number, string CallerSessionId,
    ImmutableArray<Window> Windows, ImmutableArray<Placement> Messages, ImmutableArray<LaunchPlacement> Launches,
    ImmutableArray<WritePlacement> Writes, ImmutableArray<UnobservedLaunchPlacement> Unobserved,
    ImmutableArray<AgentAssociation> Agents, ImmutableArray<WindowState> States, DateTimeOffset? ReadFrom);
```

Every array is ordered ordinally — windows by `(Kind, Stage, Number)`, placements by `(Window, At, Agent.Id, MessageId)`,
agents by `(Window, Agent.Id)` — so equal inputs give equal outputs byte for byte, and a permuted input gives the same
output. `WindowAssignment` is what S3 aggregates: rows from owning placements (`Association` ∈ owning, except
`ambiguous`, which goes to the ambiguous totals), the two-repositories row from `AgentAssociation` + `Writes`,
`Elsewhere` totals from `wrote elsewhere`, a new **`ClaimedByPeer`** total from `claimed by peer` (S3 gains it), the
state from `States` (a round's state is the weakest over its windows; reasons concatenated, distinct).

**Identity 1 — local completeness** (`WindowAssignment.LocalCompleteness(input) → violations`): for each non-blind
window `w` of the assignment, the set of `message.id`s with `w.Contains(message.At)` in the input EQUALS the set of
`Messages[*].MessageId` with `Window == w`, and each appears once; every placement's `At` satisfies
`Window.Contains(At)`. Likewise for subagent launches (`FirstLineAt`, `TranscriptPresent`) and writes.

**Identity 2 — global uniqueness** (`RoundAttribution.AssignEvery(gate, peers, observation, repositories, peersKnown)`
— a test-side helper over the same `Assign`: it builds an `AttributionInput` for every recorded, non-blind round of
every timeline, with the others as peers): over all those assignments, the multiset of `MessageId` from placements
whose `Association` is owning is a SET — no id owned twice — and the same for launches by `Agent.Id`. Tokens are
therefore never added twice without any further rule.

Both are `[Theory]` facts over every scenario of §S2.6, through the S1 builder, and S5 runs them over E1's fixtures.

### S2.6 Worked examples — one per acceptance case

Notation: `+m` is `2026-10-14T12:00:00Z` plus `m` minutes (the S1 builder's base). `x` = `D:/x`, `y` = `D:/y`. A round is
`C1 @60→70`: code round 1 started `+60`, completed `+70`. Every message has 100 output tokens unless said otherwise.
"Owns" lists the owning placements; a window's totals follow from them.

**Case 1 — two repositories in one caller session** (criteria 1, 8-different-repo, 9, 10).

Inputs: gate **A** on `x`, open `+0`, `C1 @60→70`, `C2 @120→130`, `C3 @180→190`. Peer **B** on `y`, open `+30`, `C1 @100→110`.
Caller `c0` serves both. Agents: `main`; `cx` (sonnet, parent main) first line `+8`, writes `x/a.cs` at `+10`, `+40`, `+75`,
`+130`, messages `+10 +40 +50 +75 +130`; `cy` (sonnet, parent main) first line `+33`, writes `y/b.cs` at `+35`, `+90`,
messages `+35 +90`; `s1` (opus scout, parent main, no writes) message `+45`; `s2` (no writes) message `+80`; `s3` (no
writes) message `+140`; `main` messages `−5`, `+20` (no writes by main). Universe = `{x, y}`.

| Window (rank) | Contains | Own ladder | Claimants ranked before | Owns / labels |
|---|---|---|---|---|
| A before-open `[∅,+0)` (`+70`,A,0) | main −5 | no writes, destinations ∅ → ambiguous | B before-open ranks `+110` — after | **owns** main −5 `ambiguous` |
| A C1 `[+0,+60)` (`+70`,A,1) | cx 10 40 50 · cy 35 · s1 45 · main 20 | cx {x} → wrote here · cy {y} → wrote elsewhere · s1, main: destinations {x,y} → ambiguous | none (B C1 ranks `+110`) | **owns** cx ×3 `wrote here` (write_calls 2), s1 + main `ambiguous`; cy 35 `wrote elsewhere (y)` |
| A C2 `[+60,+120)` (`+130`,A,1) | cx 75 · cy 90 · s2 80 | cx wrote here · cy wrote elsewhere · s2 destinations {x,y} → ambiguous | B C1 `[+30,+100)` (`+110`) contains 75, 80, 90 | cx 75: B's ladder for cx in B C1 = roots {x} → elsewhere → not owning → **A owns** `wrote here` (write_calls 1) · s2 80: B's ladder = ambiguous (owning) → `claimed by peer (B)` · cy 90: own = elsewhere → `wrote elsewhere (y)` |
| A C3 `[+120,+180)` (`+190`,A,1) | cx 130 · s3 140 | cx wrote here · s3: destinations {x} → same-session | none containing them | **owns** cx 130 `wrote here`, s3 `same-session` |
| B before-open `[∅,+30)` (`+110`,B,0) | main −5 · cx 10 · main 20 | main: ambiguous · cx: roots {x} → wrote elsewhere | A before-open (`+70`) contains −5; A C1 (`+70`) contains 10, 20 | main −5 → `claimed by peer (A)` · cx 10 → `wrote elsewhere (x)` · main 20 → `claimed by peer (A)` |
| B C1 `[+30,+100)` (`+110`,B,1) | cy 35 90 · cx 40 50 75 · s1 45 · s2 80 | cy {y} → wrote here · cx {x} → wrote elsewhere · s1, s2: destinations {x,y} → ambiguous | A C1 (`+70`) contains 35–50; A C2 (`+130`) ranks after | **owns** cy ×2 `wrote here` (write_calls 2), s2 80 `ambiguous`; s1 45 → A C1's ladder ambiguous → `claimed by peer (A)`; cx 40 50 75 `wrote elsewhere (x)` |

Launches: cx `+8` → A C1 (`wrote here`); cy `+33` → B C1 (B's own window; in A C1 the ladder says elsewhere → A labels it
`wrote elsewhere`, B owns it); s1 — the builder gives scouts a first line one minute before their message, so s1 `+44`
is owned by A C1 as `ambiguous`, s2 `+79` by B C1, s3 `+139` by A C3 `same-session`. Global uniqueness: every one of
the twelve message ids and five launches is owned exactly once (A: −5, 10, 20, 40, 45, 50, 75, 130, 140; B: 35, 80,
90). Criterion 1 line by line: cx `wrote here` in A, `wrote elsewhere` in B; cy the mirror; s1 ambiguous in exactly one
window (A C1); s3 `same-session` in A C3 and absent from B.

**Case 2 — background work across rounds** (criterion 2). Gate A on `x`, open `+0`, `C1 @60→75`, `C2 @120→130`; no
peers. `main` writes `x/p.md` at `+20` and `x/q.cs` at `+100`; `bg` (opus, background, parent main) first line `+50`,
messages `+55`, `+65`, `+70`, `+110`, no writes.

| Window | bg's placements | Ladder | Launch |
|---|---|---|---|
| C1 `[+0,+60)` | `+55` | no writes; destinations {x} → `same-session` | **1**, at `+50` |
| C2 `[+60,+120)` | `+65 +70 +110` | `same-session` | 0 |

`+65` and `+70` ran while C1 was being reviewed (`+60→75`); they are in C2's window, not in none — the rule
`RoundEngine.cs:895` would have started C2 at `+75`. Tokens: 100 in C1, 300 in C2; one launch.

**Case 3 — a model switch inside one window** (criterion 3). One window `[+0,+60)`; `main` messages `+10` and `+20` on
`claude-opus-5-5`, `+30` on `claude-sonnet-5`, a write into `x` at `+10`. Three placements, all `wrote here`, carrying
their own `Model`; S3 groups them into two rows for one `main`. S2 asserts the placements' models verbatim.

**Case 4 — a resumed agent** (criterion 4). Gate A, `C1 @60→70`, `C2 @120→125`, `C3 @180→190`. `r1` first line `+10`,
messages `+15 +20`, then `+130 +140`; writes `x` at `+15` only; `main` writes `x` at `+125`.

| Window | r1 | Launch |
|---|---|---|
| C1 `[+0,+60)` | `+15 +20` → `wrote here` | 1 (at `+10`) |
| C2 `[+60,+120)` | nothing | 0 |
| C3 `[+120,+180)` | `+130 +140` → no writes, parent main does not propagate, destinations {x} → `same-session` | 0 |

**Case 5 — missing child transcripts** (criterion 5). Gate A, `C1 @60→70`, `C2 @120→130`. `UnobservedLaunch(main, +30,
"agent call without transcript")`; `ObservedAgent m2 (parent main, FirstLineAt +40, TranscriptPresent false)`; `main`
writes `x` at `+20` and `+100`, messages `+20`, `+100`.

| Window | State | Unobserved | main |
|---|---|---|---|
| C1 `[+0,+60)` | `partial: agent call without transcript, meta without transcript` | two items, no tokens, no launch placement | `+20` `wrote here`, unchanged |
| C2 `[+60,+120)` | `observed` | none | `+100` `wrote here` |

**Case 6 — replay** (criterion 6). Over case 1's A C2: `Assign(input)` twice → equal by value and equal `ToString()`.
Then each extension leaves the assignment EQUAL: the journal extended by A `C3`; peer B extended by `C2 @150→160`
(ranks after A C2); a new peer gate D opened at `+65` with `C1 @200→210`; transcript lines appended at and after `+130`
(A C2's completion). Lines in `[+120,+130)` are also inert here because no claimant window ends inside that interval; the
test adds one — peer B's `C1` moved to `@125→128` — and asserts that THEN the appended line at `+122` (a `cx` write
into `y`) changes B's ladder for cx to `two repositories` and the owner of cx `+75` from A to B's ambiguous totals: the
peer's evidence is honoured as the peer saw it (decision 7), and `AssignEvery` still owns `+75` once.

**Case 7 — before-open work, and a second gate session on the same repo_path** (criteria 7, 8-same-repo). Gate A on `x`,
open `+30`, `C1 @60→70`; `main` messages `−5`, `+10` (writes `x` at `+10`), `+40` (writes `x` at `+45`, message `+45`).
Second gate **G** on `x` (another branch), open `+200`, `C1 @250→260`; `main` messages `+100` (write `x`), `+220` (write `x`).

| Assignment | Window | Owns |
|---|---|---|
| A C1 | before-open `[∅,+30)` | main −5, +10 → `wrote here` (write at +10) |
| A C1 | round `[+30,+60)` | main +40, +45 → `wrote here` |
| G C1 | before-open `[+60,+200)` — `PriorEnd` = A C1's start | main +100 → `wrote here`; nothing of A's |
| G C1 | round `[+200,+250)` | main +220 → `wrote here` |

With `PriorEnd` forced to null (the builder's `.WithoutPriorEnd()`), G's before-open is `[∅,+200)`: main −5, +10, +40,
+45 are then inside it, A's windows rank first (`+70` < `+260`), their ladder is owning, so they are `claimed by peer (A)`
— the OWNED set of G is identical. That is the test that `PriorEnd` is a read bound only. With `PeersKnown = false`, G's
before-open owns them AND is `partial: prior windows unknown` — duplication is named, never silent.

**Case 8b — a peer round by another caller is no claimant.** Case 1 with B C1's `CallerSessionId = "c9"`: s2 `+80` is
owned by A C2 as `ambiguous`; B's window is still a tiling boundary for B. And **a peer still running** (B C1's
`CompletedUtc = null`): B C1 ranks last, A C2 owns s2 `+80`; marking B C1 `interrupted` afterwards gives the same
assignment for A C2 — the live record and the backfill agree whatever the peer's fate.

**Caller changed** (decision 2). Gate A, `C1 @60→70` by `c0`, `C2 @120→130` by `c1`; `Assign` for C2 with `c1`'s
observation: window `[+60,+120)`, state `partial: caller changed`, placements from `c1`'s messages only. C1's
`CallerSessionId = ""` → `partial: prior caller unknown`.

**Blind rounds** (decision 1). Journal: `C1` start null (an old row), `C2 @120→130`, `C3 @180→190`. C1 and C2 →
`unobserved: no round start`, no placements, `ReadFrom` null-safe; C3 → window `[+120,+180)`, `observed`. A skipped round
`S @90` between C1 `@60` and C2 `@120` opens no window: C2's window is `[+60,+120)`.

**Two repositories, one agent** (decision 4). Gate A on `x`, peer B on `y`, both `C1`: A `@60→70`, B `@60→80`, both open
`+0`. `w2` writes `x` at `+10` and `y` at `+20`, messages `+10 +20 +30`. In A C1 and in B C1 the ladder says
`ambiguous · two repositories`. A ranks first (`+70`): A owns `+10 +20 +30` into its ambiguous totals (3 turns, 300
tokens, 1 agent) and emits `AgentAssociation(w2, A C1, ambiguous, two repositories)`; S3 makes from it a `wrote here` row
with `write_calls = 1`, `turns = 0`, tokens 0. B: the three messages are `claimed by peer (A)`; B's ambiguous totals count
1 agent and 0 tokens; S3's row for B has `write_calls = 1`, zero turns. Tokens once; w2 a contributor of both.

### S2.7 What S1's contract gains (members, not renames)

| Record | Member | Why |
|---|---|---|
| `GateRound` | `DateTimeOffset? CompletedUtc` | the rank key of §S2.3; E5 maps `RoundRecord.CompletedUtc` through `UtcInstant.From` (default → null, which ranks last) |
| `AttributionInput` | `bool PeersKnown` | false when E5's store lookup could not answer (absent, locked, pre-step database) → `partial: prior windows unknown`; `Peers` is then empty |
| `ObservedAgent` | (no new member) `FirstLineAt` of a `TranscriptPresent = false` agent is the instant of the `Agent` call that launched it | decision 9 needs an instant to place the unobserved launch |
| `AttributionInput.Validate()` | two more rules: `Caller.CallerSessionId == Gate.Rounds[(Stage, Number)].CallerSessionId`; no `MessageId` twice | decision 2; E3 guarantees once per session and S2 relies on it |

`Assign` throws `ArgumentException` naming the first violation when `Validate()` is not empty; E5 maps it to
`unobserved: invalid input` (a reason E5 adds to its own vocabulary — reported to E5, not decided here).

### S2.8 What E5 must hand over (reported to E5.S4, not built here)

1. **Peers on EVERY round**, not only the first (E5 §3 said first only): gate sessions other than this one holding a round
   whose `rounds.caller` or `authors_session` equals this caller, each as a `GateTimeline` — `OpenedUtc` from the peer's
   SESSION FILE (`PersistedSession.OpenedUtc`, never `sessions.opened_utc`, parent §6), its journal from the file,
   `CallerSessionId` per round from `rounds.authors_session`, else `rounds.caller` when it is a UUID, else `""`.
   `PeersKnown = false` when that read fails. One read-only connection, the `GateHistoryQuery.cs:77` shape.
2. **`ReadFrom` before the read**: `RoundAttribution.ReadFrom(input-without-observation)` → E3's `fromUtc`.
3. **The universe** of decision 6, from E3's `WriteTarget.Where` roots plus the repo paths.
4. The gate's own `GateRound.CallerSessionId` for earlier rounds from the same columns; the round being attributed
   carries the id E3's ladder resolved.

### S2.9 The coder's brief (Sonnet)

1. Create `WindowAssignment.cs` with the records and the two vocabularies of §S2.5, `AttributionReasons` of §S2.3, and
   `Windows`, `AssociationLadder`, `Ownership`, `RoundAttribution` as EMPTY shells returning `default`/throwing
   `NotImplementedException` — so the tests below fail on their assertions, not on a missing type.
2. Extend the S1 builder: `.Round(stage, n, startedAt, completedAt, caller: "c0")`, `.Peer(timeline)`, `.PeersUnknown()`,
   `.WithoutPriorEnd()`, `.MissingChild(parent, at, why)`, `.MetaWithoutTranscript("m2", parent, at)`, `.Repositories(...)`,
   and `AssignEvery()` returning every round's assignment over the scenario's gate and peers. The builder keeps the
   `Validate()`-is-empty assertion on every build.
3. Write every test of §S2.10 against the shells; build; run the filter; watch each fail on its assertion. Record the
   failure messages for the summary.
4. Implement in this order, running the filter after each: `Windows` (tiling, `PriorEnd`, `Rank`, `Precedes`),
   `AssociationLadder` (four rungs, memoised), `Ownership` (`ClaimantsOf`, `Of`), `RoundAttribution.Assign`
   (placements, launches, writes, unobserved, agents, states, ordering) and `ReadFrom`, then
   `WindowAssignment.LocalCompleteness`.
5. Every function ≤ 4 cyclomatic complexity: one rung per method, one state source per method, LINQ over explicit
   loops where it keeps a method to one decision. `OrderBy(..., StringComparer.Ordinal)` on every key. No `DateTime`,
   `Now`, `File`, `Environment` (S1's purity test watches the folder).
6. Plant each break-it of §S2.11, watch the named test go red with the named symptom, restore, and record both.
7. Build, run `--filter-class "*Attribution*"`, run the whole suite.

### S2.10 Tests — one class per guarantee, RED first

Each fact is written so its RED failure names the behaviour, not a null (assert on the collection's contents through
FluentAssertions `BeEquivalentTo`/`ContainSingle`, never on `Should().NotBeNull()` alone).

| Class | Facts | RED expectation against the shells |
|---|---|---|
| `AWindowRunsFromThePreviousRecordedRoundsStartTests` | `TheFirstRoundsWindow_StartsAtOpen`; `RoundN_StartsAtRoundNMinusOnesStart_NotItsCompletion` (case 2's windows); `ASkippedRound_OpensNoWindow`; `AnInterruptedRound_OpensNoWindow`; `AFeatureRound_OpensNoWindow`; `PlanAndCodeRounds_TileOneTimeline` (plan 1 @30, code 1 @60: code 1's window is `[+30,+60)`); `AnOpenAfterTheFirstStart_IsClippedToIt` | windows empty / wrong `From` |
| `ABlindRoundIsUnobservedNotGuessedTests` | `ARoundWithNoStart_IsUnobservedNoRoundStart`; `TheRoundAfterIt_IsUnobservedToo`; `TheOneAfterThat_IsTiledNormally`; `NoOpenTime_IsUnobservedNoOpenTime`; `ABlindWindowPlacesNothing` | state `observed`, placements present |
| `EveryMessageFallsIntoExactlyOneWindowTests` | `[Theory]` over every §S2.6 scenario: `LocalCompleteness_HoldsForEveryWindow` (identity 1); `AMessageAtExactlyARoundsStart_IsInTheNextWindow`; `AMessageAfterThisRoundsStart_IsNotPlaced` | violations non-empty |
| `TheLadderReadsWritesFirstTests` | `AWriteUnderRepoPath_IsWroteHere`; `AWriteIntoASubmoduleUnderRepoPath_IsWroteHere`; `AWriteIntoAnotherCheckout_IsWroteElsewhere_WithItsRoot`; `AWriteOutsideEveryCheckout_IsNoEvidence` (→ falls to rule 4); `WritesHereAndElsewhere_AreAmbiguousTwoRepositories`; `ABashCallIsNotAWrite` (the builder has no way to add one — the fact asserts the input record set has no shell write and the agent is `no evidence`) | association `""` |
| `OnlyASubagentParentPropagatesTests` | `AChildOfAWritingSubagent_IsChildOf`; `AGrandchild_IsChildOf_ThroughTheChain`; `AChildOfMain_IsNotChildOf_EvenWhenMainWroteHere` (the scout for `y` in case 1 is ambiguous, not here); `TheParentMustBeAssociatedInTheSameWindow` (parent wrote here in C1 only, child's messages in C2 → rule 4) | `child of` missing / present wrongly |
| `SameSessionNeedsExactlyOneDestinationTests` | `OneDestinationAndItIsThisOne_IsSameSession` (case 1, s3); `NoWriteAtAll_IsAmbiguous`; `TwoDestinations_IsAmbiguous` (s1); `ASubmoduleAndItsSuperproject_AreOneDestination`; `AnotherCheckoutOnly_IsAmbiguousNotElsewhere` | association wrong |
| `TheEarlierCompletedWindowClaimsTests` | case 1 end to end: `cxIsHereInAAndElsewhereInB`; `cyIsTheMirror`; `s1IsAmbiguousInExactlyOneWindow`; `s2IsClaimedByPeerInAAndOwnedByB`; `s3IsSameSessionInAAndAbsentFromB`; `AnElsewhereMessageIsLabelledElsewhereNotClaimed` (cy 90 in A C2); `APeerWindowOfAnotherCaller_ClaimsNothing` (case 8b); `APeerStillRunning_ClaimsNothing_AndItsLaterFateChangesNothing` (case 8b); `TiesBreakByGateIdOrdinal` | ownership labels wrong / duplicated |
| `NoMessageIsOwnedTwiceTests` | `[Theory]` over every scenario through `AssignEvery`: `GlobalUniqueness_HoldsOverEveryGateAndPeer` (identity 2); `TokensAreTheSumOfOwnedMessagesOnce` (Σ over all assignments of owned tokens = Σ of the input's tokens that lie in some window, minus `wrote elsewhere`... exactly: every message id in some window is owned once or labelled `wrote elsewhere` in every window containing it) | duplicates found |
| `ABackgroundAgentStraddlesARoundTests` | case 2: `ItsMessagesSplitByInstant`; `TheReviewTimeWorkIsInRoundNsWindow_NotInNone`; `ItLaunchesOnce_InTheWindowOfItsFirstLine`; `TheResumedAgentLaunchesNothingAgain` (case 4); `AResumedAgentIsAssociatedPerWindow` (case 4: here in C1, same-session in C3) | launches 2 / message count 1 |
| `AModelSwitchKeepsEachMessagesModelTests` | case 3: three placements, models verbatim, one agent | models blank |
| `AMissingChildMakesItsWindowPartialTests` | case 5: `TheWindowHoldingTheCallIsPartialNamingEach`; `TheOtherWindowStaysObserved`; `TheChildHasNoRowNoLaunchAndNoTokens`; `TheParentsOwnAssignmentIsUnchanged`; `AReasonWithoutAnInstant_MarksEveryWindow` | state `observed` |
| `BeforeOpenWorkBelongsToTheFirstRoundOnlyTests` | case 7: `WorkBeforeOpen_IsTheBeforeOpenWindowOfRoundOne`; `RoundTwo_HasNoBeforeOpenWindow`; `ASecondGateOnTheSameRepo_StartsAtThePriorEnd`; `ThePriorEndIsAReadBoundNotAnOwnershipRule` (owned set equal with `.WithoutPriorEnd()`); `PeersUnknown_IsPartialPriorWindowsUnknown` | before-open absent / owned sets differ silently |
| `TheCallerOfTheRoundIsTheCallerReadTests` | `ACallerChange_IsPartialCallerChanged`; `AnEmptyPriorCaller_IsPartialPriorCallerUnknown`; `AnObservationOfAnotherCaller_IsAContractViolation` | state `observed` / no exception |
| `TwoRepositoriesOneAgentTests` | decision 4's scenario: `ItsMessagesAreAmbiguousInTheFirstCompletedWindow`; `TheOtherGateSeesThemClaimedByPeer`; `BothGatesCountOneAmbiguousAgent`; `BothGatesKeepItsWriteCallsOnAnAgentAssociation` | tokens in both |
| `AssignIsStableUnderReplayTests` | case 6: `TwiceIsEqualByValueAndText`; `AnExtendedJournalChangesNothing`; `ALaterPeerRoundChangesNothing`; `ANewPeerGateChangesNothing`; `LinesAtOrAfterCompletionChangeNothing`; `LinesBetweenStartAndCompletionMatterOnlyThroughAPeerThatEndsThere` (the two-step fact of case 6); `APermutedInputGivesTheSameOutput` | inequality / ordering differs |
| `ReadFromIsTheEarliestLineAssignNeedsTests` | `ANonFirstRound_ReadsFromThePreviousRecordedStart`; `AClaimantStartingEarlier_LowersIt`; `AFirstRoundWithNoPriorEnd_ReadsFromTheFirstLine` (null); `DroppingEveryLineBeforeReadFrom_ChangesNothing` | wrong bound |
| `EvidenceNotInferenceTests` | `AnAgentTypeNamedCoder_AssociatesNothing`; `ALaunchInsideTheWindow_AssociatesNothing`; `AnAgentWhoseOnlyWritesAreOutside_HasNoEvidence`; `NoInputMemberIsACwd` (reflection over the S1 records: no property named `Cwd`) | association `wrote here` |

### S2.11 Break-its — planted, watched red, restored

| Plant (exact mutation) | Red test, with its symptom |
|---|---|
| In `Windows.Of`, `From = r_{k-1}.CompletedUtc` (`RoundEngine.cs:895`'s rule) | `TheReviewTimeWorkIsInRoundNsWindow_NotInNone`: bg `+65`, `+70` in no window — local completeness also red |
| `Window.Contains`: `t <= To` | `AMessageAtExactlyARoundsStart_IsInTheNextWindow` and `GlobalUniqueness`: one id in two windows |
| Count a launch in every window the agent has a message in | `ItLaunchesOnce_InTheWindowOfItsFirstLine`: 2 launches; `TheResumedAgentLaunchesNothingAgain`: 2 |
| Add `string Cwd` to `ObservedAgent` and make `SessionRung` return `same-session` when `Contains(gate.RepoPath, Cwd)` | `NoInputMemberIsACwd` red by name, and `AChildOfMain_IsNotChildOf_EvenWhenMainWroteHere`: the scout for `y` reads `same-session` in A |
| `SessionRung`: `destinations.Contains(gate.RepoPath)` instead of equality with the singleton | `TwoDestinations_IsAmbiguous`: s1 `same-session` in A AND in B — `GlobalUniqueness` red |
| `ChildRung`: drop `!p.IsMain` | `AChildOfMain_IsNotChildOf_EvenWhenMainWroteHere`: a scout for `y` is `child of` in `x` |
| Place messages with `At >= own start` (drop the window's `To` check) | `AMessageAfterThisRoundsStart_IsNotPlaced`; `LinesAtOrAfterCompletionChangeNothing`: live ≠ backfill |
| Give a `TranscriptPresent = false` agent a `LaunchPlacement` with zero tokens | `TheChildHasNoRowNoLaunchAndNoTokens`; the window reads `observed` when the reason is dropped with it |
| `Rank` by `StartedUtc` instead of `CompletedUtc` | `APeerStillRunning_ClaimsNothing_AndItsLaterFateChangesNothing`: the running peer claims live, its interrupted twin does not |
| In `Ownership.Of`, consult claimants before the own ladder's `wrote elsewhere` | `AnElsewhereMessageIsLabelledElsewhereNotClaimed`: cy `+90` reads `claimed by peer` |
| Drop `PriorEnd` from `ReadFrom` only | `ASecondGateOnTheSameRepo_StartsAtThePriorEnd`: `ReadFrom` null where `+60` is expected; owned set unchanged (the second fact stays green — the plant proves the bound is separable) |
| Drop the final ordinal `OrderBy` | `APermutedInputGivesTheSameOutput` |

### S2.12 Done when

Every fact of §S2.10 is green and was first seen red on its assertion; every break-it of §S2.11 was seen red with its
symptom and restored; identities 1 and 2 hold over every §S2.6 scenario through the builder; `ReadFrom` exists for
E5; the S1 members of §S2.7 are added and `Validate()` extended; S3's document is told about `ClaimedByPeer` and the
two-repositories row (§S2.5), S4 about `OwnerGate`; E5.S4 is told §S2.8; the parent records in §16 that decision 3 was
replaced (completion rank) and decision 7 refined; `CoaiMcp.Tests.exe` green.

**Unsettled, by design:** the frequency of the clock race of decision 10 (two completions in one instant) is E1's to
measure if it ever shows; E5 chooses whether a peer timeline is read from the session file or the database for a
`running` round — S2 ranks a null completion last either way.

**Dependencies.** E4.S1 (with §S2.7's members). **Model:** spec: **fable** (this text) · code: sonnet · review: opus.

---

## E4.S3 — Rows, totals and the round's state

**Goal.** Aggregate S2's assignment for one round into what E5 stores: per-agent rows, the round's ambiguous
and elsewhere totals, the unobserved launches, and the authors state — with local completeness held as a test
over the rows, not only over the assignment.

**Scope (verified 2026-10-14).** New `src_mcp/core/Authors/RoundAuthors.cs` (records) and
`RoundAuthorsBuilder.cs` (the pure aggregation). The columns E5 will store are §11 step 4's: `who`,
`requested`, `model`, `effort`, `agent_type`, `association`, `window`, `launches`, `turns`, the four token
terms, `write_calls`; `rounds.authors_state`, `authors_session`, `authors_reader`, the ambiguous totals
(parent, lines 301-303). The state words are §5's (parent, lines 150-153). The precedent for
"delete-then-insert per round" is `RoundsDb.cs:923-945` — E5's, cited so the grain here fits it.

**The records.**

```csharp
public sealed record AuthorRow(
    string Window,              // "before-open" | "round"
    string Stage, int Number,   // the round the window belongs to
    string Who,                 // "main" | "subagent"
    AgentKey Agent,             // opaque; S4 deduplicates on (caller session, agent)
    string AgentType, string Requested, string Model, string Effort,
    string Association,         // "wrote here" | "child of" | "same-session"
    int Launches, int Turns, TokenTerms Tokens, int WriteCalls);

public sealed record ActivityTotals(int Agents, int Launches, int Turns, TokenTerms Tokens)
{ public static ActivityTotals Unknown { get; } = new(-1, -1, -1, new(-1, -1, -1, -1)); }

public sealed record RoundAuthors(
    string CallerSessionId, string State, ImmutableArray<string> Reasons,
    ImmutableArray<AuthorRow> Rows,
    ActivityTotals Ambiguous, ActivityTotals Elsewhere, ActivityTotals ClaimedByPeer, int UnobservedLaunches,
    ImmutableArray<CheckoutWrites> WritesOutsideTheGateCheckout);   // gate finding 0, below

public sealed record CheckoutWrites(string Root, int Calls);   // in memory only — E5 stores counts, never the root
```

`WritesOutsideTheGateCheckout` (plan gate finding 0, parent §7): for every owned `wrote here` write placement whose
checkout root is NOT `Gate.RepoPath` itself (a submodule or a nested worktree under it), the root and its call count,
ordered by root, ordinal. Association is unchanged by it — those writes are `wrote here` — but their lines are not in
the gate checkout's diff. E5.S4 turns it into `submodule_writes` and `uncovered_writes`; the root never leaves memory.

`ClaimedByPeer` totals the items E4.S2 labels `claimed by peer` — owned by an earlier-completed window of a peer gate
session; like `Elsewhere`, they make no row and no contributor here.

**Grain.** One row per (window, agent, model, effort, association). Per AGENT, not per family, on purpose: S4
deduplicates contributors by agent, and the card's "3 Sonnet coders" counts distinct agents — a straddling
coder writes in a window where it launched 0, so `launches` cannot stand in for "agents" (§7, defect 8).
`Turns` = distinct `message.id`s on the row. `Launches` = 1 on the row holding the agent's first message in the
window of its `FirstLineAt`, else 0; an agent launched in a window where it sent no assistant message gets a
row with zero turns and tokens and `Model = "unknown"`, so its launch is not lost. `WriteCalls` = the agent's
writes into THIS repository inside the window. Ambiguous and elsewhere activity get NO rows — only totals
(§6: "no row") — with ONE exception, E4.S2 decision 4: an agent whose association is `ambiguous` with rung `two
repositories` gets a `wrote here` row built from its `AgentAssociation` and the window's `Writes`: `WriteCalls` = its
writes into this repository, `Turns`, `Launches` and every token term ZERO (its messages are in the ambiguous totals or
claimed by a peer). Rows are ordered by (window, stage, number, who, agent id, model, effort, association),
ordinal — equal inputs give equal outputs, byte for byte.

**State.**

| Observation / window | `State` | `Reasons` | Totals |
|---|---|---|---|
| E3's state `unobserved: <why>` | `unobserved` | `[why]` | no rows; `Ambiguous` and `Elsewhere` = `ActivityTotals.Unknown`, never zeros |
| a reason with an instant inside the window (a missing child, a read budget hit before the window's end) | `partial` | each, in order | as counted — a floor |
| a reason with no instant (a torn MIDDLE line; a torn LAST line is not a reason at all, parent §16 D22) | `partial` | for every window | as counted |
| S2's window-level unobserved (no round start) | `unobserved` | `no round start` | `Unknown` |
| none of the above, with or without subagents | `observed` | `[]` | as counted — an observed session with no subagents is an observed ZERO |

If E3 or E5 already carries an unknown-count value type (a `Captured`/`Figure` analogue), use it instead of
the `-1` sentinel and record the choice (reuse-first).

**The coder's brief (Sonnet).**

1. Write the tests below against S2's `WindowAssignment`, built through the S1 builder; watch them fail on
   assertions.
2. Implement `RoundAuthorsBuilder.Build(AttributionInput input, WindowAssignment assignment) → RoundAuthors`
   as a pure static: group, sum, order — `OrderBy(…, StringComparer.Ordinal)` on every key, nothing that
   depends on dictionary or hash order.
3. Effort `""` → `Effort.Unknown` at this boundary if S2 did not already; never `high`.
4. Build, run the filter, run the whole suite.

**Test scenarios.**

| Class | Facts |
|---|---|
| `ARowIsOneAgentModelAndEffortInOneWindowTests` | a model switch gives two rows for main; an effort switch gives two rows; tokens are exact sums; a message E3 folded from three lines contributes once |
| `ALaunchIsCountedOnceAndNeverLostTests` | a launch lands on its first-message row; a straddler's later window shows `Launches = 0`; an agent launched with no assistant message in its first window has a zero-token row carrying the launch; Σ `Launches` over every window of a session = the number of distinct launched agents |
| `AWriteIntoANestedCheckoutIsHereButOutsideTheGateCheckoutTests` | a write into a submodule root and one into a nested worktree under `Gate.RepoPath`: both `wrote here` on the agent's row, both listed in `WritesOutsideTheGateCheckout` with their call counts; a write into `Gate.RepoPath` itself is not |
| `AmbiguousAndElsewhereActivityHasNoRowsTests` | they appear only in the totals; `Agents` counts distinct agents; `claimed by peer` items only in `ClaimedByPeer` |
| `ATwoRepositoryAgentKeepsItsWritesAndAddsNoTokenTests` | the agent of E4.S2 decision 4 has one `wrote here` row with its write calls into this repository and zero turns, launches and tokens; its messages are counted once, in `Ambiguous` or `ClaimedByPeer` |
| `EveryWindowAccountsForEveryMessageTests` | local completeness over ROWS: Σ row turns + ambiguous turns + elsewhere turns + claimed-by-peer turns = the window's distinct message ids, and the same per token term — over every S2 acceptance scenario, through one `[Theory]` fed by the builder |
| `TheAuthorsStateSaysWhatWasNotSeenTests` | each row of the state table, including `AnObservedSessionWithNoSubagents_IsObservedZero` and `AnUnobservedRound_NeverReadsAsZero` |
| `NoEffortIsUnknownNotHighTests` | absent effort → `unknown` on the row |
| `TheSameRoundBuiltTwiceIsIdenticalTests` | `Build` twice → equal by value AND equal `ToString()`; the input's message order permuted → identical output |

**Break-its.** (a) Count `Launches` on every row of the agent → `ALaunchIsCountedOnceAndNeverLostTests` red.
(b) Emit a row for ambiguous activity → `AmbiguousAndElsewhereActivityHasNoRowsTests` red, and completeness
double-counts. (c) Default effort to `"high"` → `NoEffortIsUnknownNotHighTests` red. (d) Return zero totals for
an unobserved round → `AnUnobservedRound_NeverReadsAsZero` red. (e) Drop the final `OrderBy` → the permuted-input
fact red.

**Done when.** Every fact green, every break-it watched red; local completeness holds over all S2 scenarios;
`CoaiMcp.Tests.exe` green.

**Dependencies.** E4.S2 (its `WindowAssignment` shape). **Model:** brief by opus (this document) · code:
sonnet · review: opus.

---

## E4.S4 — Contributors to the change under review

**Goal.** A pure function that answers "who contributed to the change round N reviewed": the union of the
associated activity of the gate session's windows up to and including round N, each contributor tagged with
the rounds it was active in. Derived from `RoundAuthors`, never stored a second time (§6). A corrective round
lists the original authors AND the fixers.

**Scope (verified 2026-10-14).** New `src_mcp/core/Authors/Contributors.cs`. Input: the gate session's
`RoundAuthors` for rounds ≤ N (S3's output, or E6's re-reading of the stored rows into the same records), in
journal order. The journal is every stage (`RoundNumber.cs:24-26`), so windows of plan rounds are in the
union; this story TAGS each contributor with (stage, number) and decides nothing about whether a plan round's
author is a code author — E6's cohorts choose (§7, defect 9). Consumers: the report's *coder mix* (§8, E6) and
the card line's counts (E5, `src_vs_code/src/rounds.ts:156`).

```csharp
public sealed record RoundRef(string Stage, int Number, string Window);

public sealed record Contributor(
    string CallerSessionId, AgentKey Agent, string Who, string AgentType,
    ImmutableArray<string> Models, ImmutableArray<string> Requested, ImmutableArray<string> Efforts,
    ImmutableArray<string> Associations, ImmutableArray<RoundRef> ActiveIn, int WriteCalls);

public sealed record NotSeen(string Stage, int Number, string Why);
public sealed record ContributorSet(ImmutableArray<Contributor> Items, ImmutableArray<NotSeen> NotSeen);

public static class Contributors
{
    public static ContributorSet Of(IReadOnlyList<RoundAuthors> upToN, bool withBeforeOpen);
    public static ContributorSet FixersOf(IReadOnlyList<RoundAuthors> upToN, string stage, int number, bool withBeforeOpen);
}
```

Rules:

- Identity is **(caller session, agent)**: two caller sessions' `main` are two contributors; one agent in two
  windows is one contributor active in both.
- Only rows are read: ambiguous, elsewhere and claimed-by-peer totals never make a contributor (§6, "never guessed").
  S4 may read E4.S2's `OwnerGate` on a `claimed by peer` placement to say WHICH peer gate holds that activity — a
  pointer for the report, never a contributor of this gate.
- `withBeforeOpen = false` drops `before-open` entries from `ActiveIn` and drops a contributor whose only entry
  was one (§8: excluded unless asked).
- `FixersOf(…, N)` = contributors with an `ActiveIn` entry for round N's own window, when round N is not the
  gate session's first CODE round — the set E2's `delta_*` sizes.
- An `unobserved` round in `upToN` contributes nothing and is listed in `NotSeen` with its reason; a `partial`
  round contributes and is listed too (its contributors are a floor).
- Output ordered by (caller session, `main` first, then agent id), ordinal; inner arrays ordinal and distinct.

**The coder's brief (Sonnet).** Tests first (below), watched red; then `Contributors.cs` as two pure statics
over S3's records; ordinal ordering everywhere; build; filter; whole suite.

**Test scenarios.**

| Class | Facts |
|---|---|
| `ACheapModelFixingRoundThreeKeepsTheOriginalAuthorsTests` | an Opus coder writes here in code round 1's window, a Sonnet fixer in code round 3's: contributors of round 1 = {the Opus coder (+ main if it wrote)}; of round 3 = {the Opus coder (round 1), the Sonnet fixer (round 3)}; `FixersOf(round 3)` = {the Sonnet fixer}; the Opus coder's `ActiveIn` does NOT include round 3 — the cumulative diff is never relabelled the fixer's (the consultant's case, parent §6) |
| `AnAgentInTwoWindowsIsOneContributorTests` | a straddler / a resumed agent appears once with both windows in `ActiveIn` |
| `TwoCallerSessionsAreTwoMainsTests` | the same `AgentKey.Main` under two caller sessions → two contributors |
| `AmbiguousActivityNeverMakesAContributorTests` | a round whose only activity is ambiguous → empty `Items`; the round is not in `NotSeen` (it was seen) |
| `BeforeOpenWorkIsLeftOutUnlessAskedTests` | both settings; a contributor active before open and later keeps its later windows |
| `AnUnobservedRoundIsNamedNotSilentTests` | `NotSeen` lists it with its reason; `Items` still holds the other rounds' contributors |
| `ThePlanRoundsWindowsAreTaggedNotDroppedTests` | a plan-stage author is present with `("PlanReview", n, "round")` in `ActiveIn` |
| `ContributorsAreOrderedTheSameWayEveryTimeTests` | permuted input row order → equal output |

**Break-its.** (a) Key identity on `AgentKey` alone → `TwoCallerSessionsAreTwoMainsTests` red. (b) Credit every
contributor to every round up to N → the cheap-model case red: the Opus coder shows round 3. (c) Turn ambiguous
totals into an anonymous contributor → `AmbiguousActivityNeverMakesAContributorTests` red. (d) Skip unobserved
rounds silently → `AnUnobservedRoundIsNamedNotSilentTests` red.

**Done when.** Every fact green, every break-it watched red; `CoaiMcp.Tests.exe` green.

**Dependencies.** E4.S3. **Model:** brief by opus (this document) · code: sonnet · review: opus.

---

## E4.S5 — The acceptance cases over E1's real fixtures

**Goal.** The guarantees of S2–S4, proven on the sanitised transcripts E1 cut from real sessions, through E3's
real reader — so a disagreement between what E3 emits and what E4 assumes (field spellings, instants, the
`toolUseId` join, a WSL path) fails here and not in the report.

**Scope (verified 2026-10-14).** Tests only, in `src_mcp/tests/`. Fixtures under `src_mcp/tests/fixtures/`
(the folder exists; E1 adds one sub-folder per case — §11 step 0: model switch, repeated message records, a
resumed agent, a background agent completing after a round starts, nested agents, two repositories in one
session, persistence off), each with the gate timeline(s) it was cut against (E1's done-when: one fixture per
§6 acceptance case). The reader: E3's `CallerTranscript` (`src_mcp/src/Store/CallerTranscript.cs`, new in E3).
Precedent for shared fixture access: `tests/SharedFixtures.cs`.

**The coder's brief (Sonnet).**

1. `AttributionHoldsOnTheRecordedSessionsTests`, one `[Theory]` row per fixture folder. For each:
   - **The positive first** (testing.md, *A fixture the code REJECTS proves nothing*): assert E3's reader
     returned the state the fixture declares and a non-zero message count, before any attribution assertion.
     A fixture the reader declines fails HERE, by name.
   - Map the reader's `ObservedSession` to `AttributionInput` through the ONE mapping E3/E5 own (§0) — never a
     mapping written in the test.
   - Run `Assign` → `Build` → `Contributors.Of` and assert the exact cells of the fixture's expectation file
     (rows, totals, state, contributors). Where E1 wrote none, write them by hand-reading the fixture and say so
     in the summary (measurement.md rule 2: the hand-read IS the check).
2. `TheRecordedSessionsKeepBothIdentitiesTests`: local completeness and global uniqueness (E4.S2 §S2.5, whose
   association list includes `claimed by peer`) over every fixture, through the real reader.
3. `AttributionOfARecordedRoundIsStableUnderReplayTests`: for the background-agent fixture, attributing round N
   from the transcript truncated at round N's completion equals attributing it from the full transcript — the
   live record against the backfill (S2 decision 7).
4. `NoSentinelLeavesAttributionTests`: serialise every `RoundAuthors` and `ContributorSet` produced above
   (`ToString()`, and E5's JSON context if one exists by then) and assert E1's sentinel occurs nowhere; plus the
   positive half — the sentinel IS present in the fixture bytes the test read, so a fixture without sentinels
   cannot pass vacuously.
5. A fixture whose path spelling the universe does not match (a WSL `/mnt/…` path against a Windows root) fails
   by fixture name; do NOT add a translation — report it to the reviewer as a finding for E5's universe.

**Test scenarios.** The seven acceptance cases — two repositories in one caller session, background work across
rounds, a model switch, a resumed agent, missing child transcripts, replay without double counting, before-open
work — plus a second gate session of the same caller and persistence off (`unobserved: transcript not found` →
`State == unobserved`, no rows, `Unknown` totals).

**Break-its.** (a) Swap two fixtures' expectation files → those theory rows go red by fixture name (each row
reads its own expectation). (b) Add a `Description` member to `AuthorRow` filled from the agent's meta → the
sentinel test's negative half red (with the positive half green, proving the sentinel was there to leak).
(c) Truncate the background fixture at round N's START instead of its completion → still equal (the horizon is
the start); truncate it one minute before the start → red.

**Done when.** One theory row per E1 fixture, all green; both identities and the replay fact green on the real
fixtures; the sentinel test green with its positive half; every break-it watched red; `CoaiMcp.Tests.exe` green;
any expectation written by hand-reading is named in the epic's summary.

**Dependencies.** E4.S3 and E4.S4; E3.S2 + E3.S4 (the reader) and the mapping to `AttributionInput`; E1.S5's fixtures
and their `rounds.json` files. **Model:** brief by opus (this document) · code: sonnet · review: opus.

---

## 6. Definition of Done (epic)

- [ ] `src_mcp/core/Authors/` holds the contract, the membership rule (moved, not copied, from
      `AgentLog.cs:304-317`), `RoundAttribution`, `RoundAuthorsBuilder` and `Contributors`; the purity test passes.
- [ ] Fable's S2 spec closed all ten decisions; each is a test; every S2 acceptance criterion passes and every
      break-it was watched red.
- [ ] Local completeness and global uniqueness hold over every builder scenario AND every E1 fixture.
- [ ] Unknown is never 0 or `high`; an unobserved round never reads as zero; nothing textual leaves E4.
- [ ] The parent's ledger line names `E4.S2`, its E4 links this document, `todo/README.md` has this document's
      row, and the parent records the home deviation (§0) and which candidate each S2 decision took.
- [ ] `CoaiMcp.Tests.exe` green; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.

## 7. Defects found in the parent plan while splitting (not fixed here)

> **Settled 2026-10-14** in the parent's §16: 1 → D4; 2 → D3; 3 and 5 → D5; 7 → D1; 9 → D2; 10 and 13 → D6; 11 → D20;
> 12 → D18; 4 (only subagent parents propagate), 6 (each round reads its own caller) and 8 (an opaque `agent` key on
> every `round_authors` row) accepted as resolved here — E4.S2's spec may still replace 4 and 6 with a reason.

1. **Writes have no instant.** §5 keeps writes "as a boolean per (agent, repository)", but §6 associates per
   agent IN A WINDOW, and step 23's `write_calls` is a count. E3 must expose each write's instant and path.
2. **The repository universe is undefined.** A write outside `repo_path` into a memory folder, a scratchpad or
   temp is "another repository" by §6's wording, which makes rule 4's "exactly ONE repository" fail for nearly
   every main session (S2 decision 6).
3. **Duplication across gate sessions.** The tiling is exact per gate session, but two gate sessions served by
   one caller session overlap in time, and `before-open` from "the transcript's first line" re-covers every
   earlier window of a gate session on another `repo_path`: no-evidence activity lands in two rounds' ambiguous
   totals, against "never duplicated across rounds or repositories" (S2 decision 3).
4. **Rule 2 through `main`.** `toolUseId` makes main the parent of every depth-1 agent, so "child of an
   associated agent" associates every non-writing subagent with main's repository and duplicates them when main
   wrote into two (S2 decision 5).
5. **An agent writing into two repositories** is `wrote here` for both (rule 1 precedes rule 3): its tokens are
   counted twice (S2 decision 4).
6. **Several caller sessions per gate session** (§4) — §6's windows read one transcript; which one, and what
   happens to the other's activity, is not said (S2 decision 2).
7. **Boundaries.** Skipped rounds are recorded with a start a repeat does not move (`RoundSkips.cs:95-105`),
   interrupted rounds are swept without passing the attribution call site (`SessionStore.cs:578-597`), and old
   rounds have no `StartedUtc`; §6 does not say whether they start windows, and activity in a window whose round
   records no authors is lost (S2 decision 1).
8. **No agent identity in step 23.** Contributors as a union over stored rows need an agent key, and "3 Sonnet
   coders" needs distinct agents — `launches` is 0 for a straddler in its writing window. The listed columns
   carry neither; E4.S3 emits per-agent rows, so E5 must store an opaque agent key (or an `agents` count per
   aggregated row). At per-agent grain §14's "≤ 10 rows a round" may be low.
9. **Stages.** The journal tiles across plan and code rounds (`RoundNumber.cs:24-26`); §6 does not say whether a
   plan round's window contributes to a code round's contributors. E4.S4 tags, E6 decides.
10. **Evidence horizon.** The live record runs at completion with post-start lines in the transcript; without a
    rule (S2 decision 7) the live record and the backfill of one round differ — and §9's "a second run changes no
    totals" compares backfill with backfill, so it would not catch it.
11. **Home.** The epic places `RoundAttribution` "beside `CallerTranscript.cs`" (`src/Store`) and calls it pure;
    purity is enforced only in `core` (`ArchitectureTests.cs:17-24`) — this document moves it (§0).
12. **The write-tool list** (§5: `Edit`, `Write`, `MultiEdit`, `NotebookEdit`) omits writes the benchmark's
    taxonomy at `4dc98df` counts (`Artifact`, the family's MCP edit-plan verb `rt_apply_edit_plan`) — an agent
    writing only through those has no evidence. E3's to decide; noted because it moves association.
13. **Clocks.** Window edges compare the client's transcript clock with the server's `StartedUtc`; a Windows/WSL
    skew moves activity across a boundary. Not addressed; S2 decision 10 states it.
