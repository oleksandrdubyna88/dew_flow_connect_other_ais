# PLAN — E4 stories: attribution — windows, the association ladder, ambiguous totals, contributors

> Status: **code built, code gate `good_enough`, 2026-10-19 — S1–S5 implemented on `feat/round-authors-e4` (pull request #862, not merged; the coai code gate ran one round, 13 of 16 reviewers, 4 findings accepted and fixed or recorded, §12); Fable reviewed the code on 2026-10-19 and ACCEPTED every deviation of §12, and its wording changes are applied in place below; §12 is the implementation record — its deviations and the two E7 confirmations. Plan 2026-10-14, revised 2026-10-16 and 2026-10-17.** Epic E4 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> **Revised 2026-10-16 against main** (`59463d73`: E1 #828, E2 #829 and the plan-gate fix #837/#841 shipped since the
> first text; E3 is not built). Every `path:line` re-verified — the drift is §8.1, and the S2 text keeps the numbers Fable
> wrote, mapped there. §2 says that instants are an ORDER (E1's fixtures run on a synthetic rank clock) and that a row's
> model is the model that ANSWERED (RESULTS Q2). S1 gains the Q7/Q8 rules. S3 gains the answered-model rule. S4's card
> reference moves. S5 is rewritten against the twenty shipped fixtures `c01`–`c20`: it names which test uses which case,
> and what no fixture can show. §8.3 lists the edits other documents need. §9 lists what contradicted Fable's S2 spec;
> **Fable amended S2 the same day** (§S2.2a decides F1–F5 and E5's §8 items) and §9 stays as the record of the questions.
> §10 is the risk-consultation brief owed before E4's code round. (Implemented since: §12.)
>
> **Plan gate 2026-10-16** (round 1 `proceed`, on `c17fbc0c`): the risk (4/4.2) and cadence (4–6) consultations were
> taken and closed; findings for S1/S3 are applied in place, and what touches S2 is §11, for Fable.
>
> **§11 resolved by Fable, 2026-10-16** (`§S2.2b`, G1–G12; every anchor re-verified on `origin/main` `ce9cd298`, whose
> `src_mcp` is byte-identical to `59463d73`). Three rules are new: a completion ranks only when its round is `done`
> (G1), `PriorEnd` and the claimants read only **settled** peer rounds (G2), and a write is evidence only when its result was
> written before the round completed — live and backfilled alike (G3) — so placement, ownership AND association never
> move after completion; a stored ownership that was decided while a peer was still running is marked `partial: peer
> running` and E7's backfill owns its repair (G3). S3 keeps the state and the totals per window (G9); completion rank stays
> and the caller-level shared total is rejected with reasons (G11). The cross-epic consequences for E3/E5/E6/E7 and the
> parent are listed at the end of §11; no other document is edited here.
>
> **Judge review 2026-10-17** (an independent Opus judge over `7c3bf469` and E5 `f4dcc003`: 1 blocker, 2 high, 8 medium,
> 10 low; the report is the coordinator's). Every point touching E4 is resolved in §11 *Resolutions II*, each verified in
> the code first; none rejected. The cornerstone choice **B1**: the ONE `ObservedSession → AttributionInput` mapping is
> `src_mcp/src/Store/AttributionMapping.cs`, **built by E4.S5** (which already depends on E3.S4) and CALLED by E5.S4 and
> E7.S2 — so E4 reaches Done before E5. Also: a round is **final** when `done` or `interrupted` (M7, the `peer running`
> repair trigger); E7 has TWO exceptions to "equal reader → keep" (L10); `WindowAuthors` is final (L1); an unobserved
> round's `UnobservedLaunches` is `-1` (M1); no round that is skipped, interrupted or running ever gets a snapshot or a
> state, live or backfilled (M6). The E5 side of the same review is in the E5 document (its §10 item 11, §11.4).
>
> Scope: `src_mcp/core/Authors/` (new, pure) and `src_mcp/tests/` (new test classes). No schema step, no
> call site, no I/O — those are E5's. Related docs: [module_server.md](../research/module_server.md),
> [PLAN_local_db.md](../research/PLAN_local_db.md).

## 0. What this epic delivers, and what it does not

`RoundAttribution` turns ONE caller session's observation (E3's `ObservedSession`) plus the gate session's
round journal into, per round: associated activity per window and agent, the round's **ambiguous** totals,
what was credited **elsewhere**, the round's authors **state**, and — derived — the **contributors** to the
change under review (§6 of the parent). It reads no file, starts no process, reads no clock and stores
nothing; E5.S4 calls it before the round is saved (`RoundEngine.cs:485-496`, saved at `:591`), E5.S1/E5.S2 store
what it returns, E6 reads the stored result, E7.S2 runs it again over old rounds.

**Home: `src_mcp/core/Authors/`, namespace `CoaiMcp.Core.Authors` — a deliberate deviation** from the epic's
"a new file beside `CallerTranscript.cs`" (`src/Store`). "Pure, no I/O" is only *enforced* in the core:
`ArchitectureTests.cs:17-24` fails the build when `CoaiMcp.Core` references a process or network assembly,
and the core is where the gate's other pure rules live (`core/Gate/GateRule.cs`). In `src/Store` the purity
would be a habit. The parent records this deviation on promotion.

**What E4 assumes about E3** (by story id):

| E3 delivers | E4 needs from it |
|---|---|
| E3.S4: the reader's value, `ObservedSession` | one record per `message.id` with ONE instant — its earliest line (E3 S4-8; one id's lines span up to 2.01 h, RESULTS Q4) — its agent, observed `model` (the model that answered), `requested`, `effort` (absent = unknown) and the four token terms (the per-term maximum over ALL the id's lines, parent §16 D8) |
| E3.S4: the counting rules (tokens once per `message.id`, launches once per `agentId`) | each agent's FIRST-LINE instant, its parent (main via `toolUseId`, or `parentAgentId`), `agentType`, the meta's requested alias |
| E3.S3: the write test (`WriteTarget`) | **each write as an instant plus its path** — not only a boolean per (agent, repository); see defect 1 in §7, settled by parent §16 D4 — **and, since §S2.2b G3, the instant its `tool_result` was written** (`AnsweredUtc`): the round's completion is the evidence horizon for writes, and E4 cannot apply it without that stamp |
| E3.S4: the four states | the session-level state and every `partial` reason. **E3's reasons carry NO instant** (`ObservationReason(Code, Count)`, E3 `:231`): every one of them reaches E4 as a `StateReason` with `At = null` and marks every window, EXCEPT the two transcript-missing codes, whose instants ride on E3's `UnobservedLaunches` and which are therefore never mapped as reasons (§S2.2b G4, G5) |

E3's `ObservedSession` lives in `src/Store` (E3 `:191`, `:712`), so E4.S1's input records are the core contract and
**ONE mapping in `src/Store` fills them: `src_mcp/src/Store/AttributionMapping.cs`, built by E4.S5** (B1, 2026-10-17 —
it was "E5's call site or E3's reader", which left E4.S5 without a unit to call and E5 built after E4). It is a pure
static class — `Observation(ObservedSession) → CallerObservation`, `Timeline(gateSessionId, repoPath, OpenedUtc,
IReadOnlyList<RoundRecord>, Func<RoundRecord, string> callerOf) → GateTimeline`, `Universe(gate, peers, observation)`
(decision 6) — that decides nothing and is CALLED by E5.S4 (step 5) and E7.S2; never a second reader, never a second
mapping. S5 specifies and tests it.

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
- **Instants are an order, not a time** (added 2026-10-16). Every rule in this epic COMPARES instants (half-open
  windows, minimum, maximum, rank) and none subtracts them — no tolerance, no duration, no "within N minutes". E1's
  fixtures depend on this: their sanitiser redraws every instant as a rank, `2000-01-01T00:00:00Z` plus one second per
  distinct real instant, with ties kept and `rounds.json` on the same clock; it also redraws every usage count as its
  rank among the fixture's values at that path (`allowlist.json` notes; `scripts/lib/transcript-sanitise.mjs:21-22`). So
  no test may assert a duration, a date or a token magnitude read from a fixture. The S1 builder's base
  `2026-10-14T12:00:00Z` is synthetic as well, and S5 adds the fact that attribution is unchanged when every instant is
  mapped through a strictly increasing function.
- **The model is the one that ANSWERED** (added 2026-10-16, RESULTS Q2). A meta names the REQUEST, and it often names
  nothing: metas with `model` are 100 % only on 2.1.283–2.1.285 (Windows) and 2.1.300 (both sides); on the other
  versions they are 70–98 % on Windows and as low as 32 % on WSL. Also, a session can be answered by another model than
  it requested: an Opus 5.5 → Opus 4.8 `model_refusal_fallback`, scope `session`, was seen on 2026-10-16, after which the
  stream's `message.model` changed and the request, the metas and the totals did not. So every count here is grouped on
  `ObservedMessage.Model` (`message.model` of the agent's own assistant lines), and `Requested` is carried beside it and
  never used in its place. `ObservedAgent.Requested` is for display only; no rule reads it to fill a model.
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

**Scope (verified 2026-10-14; re-verified against main `59463d73`, 2026-10-16).**

| Where | What |
|---|---|
| `src_mcp/core/Authors/AttributionInput.cs` (new) | the input records below |
| `src_mcp/core/Authors/Repositories.cs` (new) | `RepositoryOf(path, universe)` — longest containing root, or none |
| `src_mcp/src/Store/AgentLog.cs:304-317` | `InRepo` + `Normalise`: the separator-aware, case-insensitive containment test that exists today. **Reuse, don't copy**: move the rule into `Repositories` as a public static and have `AgentLog.InRepo` call it (reuse-first move 2: extract the shared half). `AgentLog`'s behaviour is unchanged (§3 of the parent) |
| `src_mcp/src/Server/SessionStore.cs:78-92,104,193,266,278` | `RoundRecord(Stage, Number, …)`, `Status`, `StartedUtc`, `Sha`, `Caller`; `PersistedSession.Rounds`, `OpenedUtc` — the sources of `GateTimeline`, mapped by E5, not here |
| `src_mcp/core/Rounds/SessionState.cs:382-386` | `SessionState.SessionId`, `RepoPath` — the gate session's identity |
| `src_mcp/src/Server/RoundNumber.cs:24-26` | "the session's rounds as persisted — every stage, in the order they ran": the journal `GateTimeline.Rounds` mirrors |
| `src_mcp/tests/AttributionScenario.cs` (new) | the builder |
| `src_mcp/tests/ArchitectureTests.cs:17-24`, `tests/ProductionSources.cs:38` | the precedent and the scanner the new purity test uses |

**The records** (names binding; members may gain fields when Fable's S2 spec needs them):

```csharp
namespace CoaiMcp.Core.Authors;

public readonly record struct AgentKey(string Id)            // "main" or the agentId; E5 maps "main" to the parent's empty column in one place
{ public const string MainId = "main"; public static AgentKey Main => new(MainId);
  [JsonIgnore] public bool IsMain => Id == MainId; }         // 2026-10-16 (E5 §8.3): a serialised key has exactly one member, `id` — an S1 fact asserts it

public readonly record struct TokenTerms(long Input, long Output, long CacheCreation, long CacheRead);

public sealed record ObservedMessage(AgentKey Agent, string MessageId, DateTimeOffset At,
    string Model, string Requested, string Effort, TokenTerms Tokens);   // Effort "" → Effort.Unknown at the boundary
public sealed record ObservedAgent(AgentKey Key, AgentKey Parent, string AgentType,
    string Requested, DateTimeOffset? FirstLineAt, bool TranscriptPresent);              // FirstLineAt null ONLY when TranscriptPresent is false and E3 parsed no launching call (§S2.2b G4); a present agent without one is a Validate() violation
public enum WriteKind { Checkout, OutsideAnyCheckout, Unresolved }                          // 1:1 with E3.S3's WriteTargetKind
public sealed record ObservedWrite(AgentKey Agent, DateTimeOffset At, DateTimeOffset AnsweredAt, WriteKind Kind, string Where); // At = its MESSAGE's instant (E3 S4-8); AnsweredAt = the instant of the `tool_result` line that answered it (E3's AnsweredUtc, §S2.2b G3) — the horizon §S2.4 tests. Kind/Where = E3 Target.Kind / Target.Where (a normalised root KEY / directory / empty). No Path: E3's ObservedWrite carries none, and no rule reads one (plan gate 2026-10-16)
public sealed record UnobservedLaunch(AgentKey Parent, string ToolUseId, DateTimeOffset? At, string Why); // E3's UnobservedLaunches, 1:1 — the ONLY source of unobserved-launch items and of their reasons (§S2.2b G4); At null → every window (as a StateReason with no instant)
public sealed record StateReason(string Why, DateTimeOffset? At);                       // At null = the whole session. Every E3 reason arrives with At = null (E3 reasons carry no instant, §S2.2b G5)
public sealed record ObservationState(string Kind, ImmutableArray<StateReason> Reasons); // observed | partial | unobserved

public sealed record CallerObservation(string CallerSessionId, ObservationState State,
    ImmutableArray<ObservedMessage> Messages, ImmutableArray<ObservedAgent> Agents,
    ImmutableArray<ObservedWrite> Writes, ImmutableArray<UnobservedLaunch> Unobserved);

public sealed record GateRound(string Stage, int Number, DateTimeOffset? StartedUtc,
    string Status, string Verdict, string CallerSessionId,
    DateTimeOffset? CompletedUtc);                                                       // E4.S2's rank key — READ ONLY WHEN Status == done (§S2.2b G1: a running row carries a stamp too); null, or any other status, ranks last
public sealed record GateTimeline(string GateSessionId, string RepoPath,
    DateTimeOffset? OpenedUtc, ImmutableArray<GateRound> Rounds);                        // Rounds in journal order

public sealed record AttributionInput(CallerObservation Caller, GateTimeline Gate,
    ImmutableArray<GateTimeline> Peers, ImmutableArray<string> Repositories,
    string Stage, int Number,                                                              // the round being attributed
    bool PeersKnown);                                                                      // false → partial: prior windows unknown, Peers empty
```

**Mapping from E3** (E3.S4's contract, done by the ONE mapping `AttributionMapping`, which E4.S5 builds and E5.S4 / E7.S2
call — B1): `ObservedAgent.FirstLineAt =
LaunchedUtc ?? CalledUtc` — null only for a meta without a transcript whose launching call E3 did not parse (such an
agent is inert, §S2.2b G4: its launch is the `UnobservedLaunch` E3 emits for it); `TranscriptPresent` as is;
`UnobservedLaunch` is E3's `UnobservedLaunches`, one for one; `ObservedWrite.AnsweredAt` is E3's `AnsweredUtc` (the
`tool_result` line's instant — a member E3 owes, §S2.2b G3); `GateRound.CompletedUtc` is `UtcInstant.From(RoundRecord.CompletedUtc)`
when `Status == done` and null otherwise (the core applies the same rule again, §S2.3 *Tiling*); every E3 `ObservationReason`
except `agent call without transcript` and `meta without transcript` becomes a `StateReason(Code, At: null)` (§S2.2b G5);
`Effort` `"unknown"` as is; `TokenTerms` from `TokenUsage` term by term; **`main` is never listed in `Agents`** (E3 lists it
with no launch; every rule reads `main` as implicit — `AttributionMapping.Observation`, Fable 2026-10-19). Nothing in the mapping
decides anything.

`Peers` are the OTHER gate sessions this caller session served (E5 finds them in the store by
`rounds.authors_session`, else `rounds.caller` when it is a UUID — §S2.8 is the rule; *aligned 2026-10-16, E5 §8.5*);
`Repositories` is the universe of repository roots membership is decided against — which
roots go into it is S2's decision (S2 decision 6); S1 only implements the lookup.

**`Repositories.RepositoryOf(string path, IReadOnlyList<string> universe)`** → the LONGEST root in the
universe that equals or contains `path`, or `""`. Rule: both sides through `Normalise` (backslash → slash,
trailing slash trimmed); equality or `StartsWith(root + "/")`, `OrdinalIgnoreCase` — exactly
`AgentLog.cs:311-314`, now in one place. Longest wins, so a worktree nested inside another checkout
(`D:/rsd/x/.claude/worktrees/y`) is `y`, not `x`. Relative paths and `""` → `""` (never resolved against a
working directory, which would be I/O). A WSL `/mnt/d/…` path and a `D:\…` root are NOT equated — no
translation is guessed; E5 passes the universe in the spelling the transcript uses, and S5 checks it. Two universe roots
that are equal under `Normalise` + `OrdinalIgnoreCase` (the gate's `repo_path` as typed, `D:/x`, and E3's lower-cased
`d:/x`) are ONE root: `RepositoryOf` keeps the first in ordinal order, so a tie never depends on input order (plan gate
2026-10-16; fact `TwoSpellingsOfOneRoot_AreOneRoot`).

**What the paths look like, and the rules that follow** (RESULTS Q8, added 2026-10-16). Every write path E1 measured is
absolute on both sides. On Windows, 90 % use backslashes, separators are mixed inside one path, and the drive letter is
kept as typed (lowercase on 219 subagent writes). WSL uses `/` only. A write under the session's `cwd` was 0 % on Windows
(where it could be decided) and 5 % on WSL. So:

- Separators are folded and case is ignored ordinally, on both sides of the test — `D:\x/y\z.cs`, `d:/X/y/z.cs` and
  `D:/x/y/z.cs` are one path. E3's `WriteTarget.Where` comes lower-cased (`RepositoryIdentity.Normalised`,
  `runners/Context/RepositoryIdentity.cs:24`) and the gate's `repo_path` comes as typed. `OrdinalIgnoreCase` makes the two
  agree, for non-ASCII folder names too (S1 has a test with one).
- A path or a universe root that has a `.` or `..` segment is in NO repository: `RepositoryOf` returns `""`, so it is
  never evidence. Nothing is resolved, because resolving needs the disk or a working directory. **Checked:** Q8 measured
  absoluteness, separators and drive case. It did NOT measure dot segments, so how often they occur is unknown, and this
  rule makes them cost evidence, never a wrong attribution. `Contains` stays exactly the rule moved from `AgentLog`
  (`AgentLog` keeps its behaviour); the dot-segment refusal lives in `RepositoryOf` only.
- No `cwd` is an input. Q8 is why: a subagent's `cwd` is the session's folder, and its writes land somewhere else.

**The scenario builder** (`AttributionScenario`, tests only): a fluent value that builds an `AttributionInput`
from short statements — `.Opened(t)`, `.Round("CodeReview", 1, startedAt: t)`,
`.Main(at: t, model: "claude-opus-5-5", tokens: …)`, `.Agent("a1", parent: main, launchedAt: t, requested: "sonnet")`,
`.Message("a1", at, model, effort, requested: …)` (a requested value apart from the model, so a fallback or an empty
meta alias can be built), `.Write("a1", at, path, answeredAt: at)` (a `Checkout` write whose `Where` is `path`; `answeredAt`
defaults to `at`, and a test moves it past a completion to plant a late result — §S2.2b G3; `.WriteOutside` and
`.WriteUnresolved` build the other two kinds), `.MissingChild(parent, at, why)`,
`.Peer(timeline)`. Every instant is ONE fixed base `2026-10-14T12:00:00Z` plus minutes, never `now`
(testing.md, *A test that reads the clock expires*). Message ids are generated deterministically (`m-0001`, …).
S1 adds `AttributionInput.Validate()` returning the list of contract violations (a message whose agent is not
declared, a write by an undeclared agent, an agent declared twice (added in the code and accepted by Fable, 2026-10-19), rounds out of journal order, a `(Stage, Number)` not in
`Gate.Rounds`, the caller's `CallerSessionId` differing from the attributed round's, a `MessageId` twice — and, added
2026-10-16, a `(Stage, Number)` listed twice in one journal (E1's `replay-same-round-twice` fixture, `c13`, has exactly
that; tiled, the copy would open an empty window and the round would be looked up twice), and a parent chain that does not
end at `main` or at an empty parent (a loop; Q7 measured nesting to depth 3, and S2's rule 5 climbs the chain). An agent
whose `Parent` is empty — E3's `ParentKey = ""` for an agent file without a meta, S4-6 — or names an agent that is not
declared is NOT a violation: it has no parent, and it propagates nothing). Added by §S2.2b (2026-10-16): an agent with
`TranscriptPresent = true` and `FirstLineAt = null` (E3 gives every present file its first stamped line, `LaunchedUtc`; a
file without one is E3's to name — §11 cross-epic list); a write whose `AnsweredAt` precedes its `At` (a result cannot
precede its call's message); and the attributed `(Stage, Number)` not being **recorded and settled** — `Stage ∈ {PlanReview,
CodeReview}`, `Verdict ≠ skipped`, `Status == done` (a running round has no completion to be the horizon of §S2.4, and a
skipped or interrupted round opens no window; neither E5.S4 nor E7 calls `Assign` for one — such a round has no
snapshot and an empty `authors_state`, *not recorded*, live and backfilled alike; M6, 2026-10-17). The builder asserts the list is empty — so no later test can pass on a fixture the code quietly declines
(testing.md, *A fixture the code REJECTS proves nothing*). `RoundAttribution.Assign` throws `ArgumentException` naming the
first violation when `Validate()` is not empty; E5.S4 maps that to `unobserved: invalid input`.

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
| `APathBelongsToTheLongestRepositoryThatHoldsItTests` | `AFileUnderTheRoot_IsInIt` / `TheRootItself_IsInIt` / `ASiblingWithTheRootAsPrefix_IsNot` (`/repo-two` vs `/repo`) / `BackslashesAndATrailingSlash_DoNotMatter` / `CaseDoesNotMatter` / `ANestedWorktree_BelongsToTheInnerRoot` / `ARelativeOrEmptyPath_IsInNone` / `AWslSpellingIsNotTranslated`; added 2026-10-16 (Q8): `MixedSeparatorsInsideOnePath_DoNotMatter` (`D:\x/y\z.cs` under `D:/x`) / `ALowercaseDriveAgainstAnUppercaseRoot_IsInIt` (`d:\x\a.cs` under `D:/x`, and the reverse) / `ANonAsciiFolderMatchesAcrossCase` (`d:/проект/a.cs` under `D:/Проект`) / `ADotSegment_IsInNone` (`D:/x/../y/a.cs` and `D:/x/./a.cs` against root `D:/x` → `""`; a root `D:/x/..` matches nothing); added by the plan gate: `TwoSpellingsOfOneRoot_AreOneRoot` (universe `[d:/x, D:/x]` in both orders → the same root) | the exact root or `""` |
| `AgentLogTests` (existing) | unchanged | `InRepo`'s behaviour kept through the move |
| `TheAttributionInputSaysWhatIsWrongWithItTests` | one fact per violation above (the caller-mismatch, the duplicate `MessageId`, the round listed twice and the looping parent chain included; added by §S2.2b: `APresentAgentWithoutAFirstLine_IsAViolation`, `AResultBeforeItsCall_IsAViolation`, `AttributingARunningRound_IsAViolation`, `AttributingASkippedOrInterruptedRound_IsAViolation`), plus `AWellFormedScenario_HasNoViolations`, `AnAgentWithNoParentOrAnUndeclaredOne_IsNoViolation` and `AMetaWithoutATranscriptAndNoCallInstant_IsNoViolation` (the positive ones) | the violation list |
| `ARoundStartFromASessionFileIsReadAsUtcTests` | `AnUnspecifiedKind_KeepsItsHourAndGetsZeroOffset`; `TheDefaultInstant_IsNotRecorded` | `UtcInstant.From` |
| `TheAttributionCoreTouchesNoClockAndNoDiskTests` | `NoAuthorsSourceNamesTheClockTheDiskOrTheEnvironment` — reads `src_mcp/core/Authors/*.cs` through `ProductionSources` and asserts none contains `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `TimeProvider`, `TimeZoneInfo.Local`, `Stopwatch`, `Process.`, `File.`, `Directory.`, `Environment.`, `Guid.NewGuid`, `new Random` (the clock list widened by the 2026-10-16 plan gate); and `TheScannerSeesTheAuthorsFolder` — at least one file was read (testing.md, *A structural test that matches nothing passes forever*) | the source text |

**Break-its.** (a) Drop the `+ "/"` from `Contains` → `ASiblingWithTheRootAsPrefix_IsNot` red. (b) Return the
FIRST containing root instead of the longest → `ANestedWorktree_BelongsToTheInnerRoot` red. (c) Add
`DateTime.UtcNow` to any `Authors` file → the purity test red. (d) Implement `UtcInstant.From` with
`value.ToUniversalTime()` → `AnUnspecifiedKind_KeepsItsHourAndGetsZeroOffset` red on any machine not at
+00:00. It cannot go red on a UTC machine; the test's summary says so, and the break-it is watched on this
operator's machine (not at +00:00). (e) Remove the dot-segment refusal from `RepositoryOf` →
`ADotSegment_IsInNone` red: `D:/x/../y/a.cs` reads as inside `D:/x`. (f) Drop the duplicate-round rule →
`ARoundListedTwice_IsAViolation` red. (g) Replace `DateTime.UtcNow` by `DateTime.Today` in plant (c) → the purity test
red.

**RED against the shells** (plan gate 2026-10-16). A fact that expects `""` passes against a `RepositoryOf` shell that
returns `""`. So the shell returns its `path` argument unchanged; then `ASiblingWithTheRootAsPrefix_IsNot`,
`ARelativeOrEmptyPath_IsInNone` and `ADotSegment_IsInNone` fail on their assertion. `AWellFormedScenario_HasNoViolations`
and `AnAgentWithNoParentOrAnUndeclaredOne_IsNoViolation` are positives: a `Validate()` shell that returns no violations
passes them, and they are proven by the negative facts beside them, as the summary says.

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
>
> **Amended by Fable, 2026-10-16**, in place, against §9 F1–F5 and E5's §8, and consistent with the same day's E3.S4
> amendment (`feat/round-authors-e3` `165de3bc`): decision 7 is narrowed (F1) and its horizon is stated for a write's
> MESSAGE (F2); refusing a duplicated round is confirmed (F3); `partial` copies E3's REASONS only, and E3 no longer has
> `meta without model` (F4); the skip sentence is corrected (F5); the E4-side answers to E5's seven open items are in
> §S2.2a and applied in S1, S3 and S4; the anchors §8.1 lists are corrected in this story's text.
>
> **Amended again by Fable, 2026-10-16**, against §11 G1–G12 (the plan gate, the own review and the two consultations):
> §S2.2b holds the twelve decisions; §S2.3 gains *settled* rounds, the settled-prefix `PriorEnd`, settled-only claimants
> and the `peer running` reason; §S2.4 gains the write horizon (`AnsweredAt` before the completion, live and backfilled
> alike); decision 7's limitation (ii) is withdrawn; decision 9 counts a missing transcript once; §S2.1 and decision 1
> agree on blind rounds; the four stale anchors are corrected in place. Every `path:line` below is on `origin/main`
> `ce9cd298` (= `59463d73` for `src_mcp`).

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
own — is `WhatTheCallerWasDoing`, `src_mcp/src/Server/Rounds/RoundEngine.cs:898-907` (on main `59463d73`; was
`:893-902`): a window from the previous round's `CompletedUtc` (`:900`), floored at `OpenedUtc` (`:901`). The journal is every stage in the order it ran
(`src_mcp/src/Server/RoundNumber.cs:25`); `RoundRecord.StartedUtc` is `SessionStore.cs:92`, stamped by `LiveRound.cs:279`
for a round that ran and by `RoundSkips.cs:105` for a skip; `PersistedSession.OpenedUtc` is `SessionStore.cs:278` (the record
`:266`; *was cited `:248`, corrected 2026-10-16, G12e*). A skip
is `Verdict = skipped`, `Status = done`, with a sha (`RoundSkips.cs:102-110`); a repeated skip moves its
`CompletedUtc`, its `Repeats` and its `Sha` (`RoundSkips.cs:97`, one `with`) and never its `StartedUtc` — and a skip opens
no window, so nothing here depends on which of the three moved (*corrected 2026-10-16, F5; the text said "only its
`CompletedUtc`"*). An interrupted round is `Status = interrupted`, written by the startup sweep `SweepOrphanedRounds`
(`SessionStore.cs:608-631`, the rewrite at `:626` — *was cited `:594-597`, corrected 2026-10-16, G12e*; it is `IsOrphaned`,
`:718-719`, that selects a `running` row whose pid is dead) and never reaches
the call site E5 adds right AFTER the record's `with` block (`RoundEngine.cs:485-496`) — one statement before the save
(`:591`); an initializer cannot name the record it is building (L2, 2026-10-17; §8.1). The round's completion
stamp, `RoundRecord.CompletedUtc` (`SessionStore.cs:84`), is what §S2.3 ranks claims by — **but only on a `done` round**
(§S2.2b G1): the stamp is POSITIONAL, so every record carries one — the running row `LiveRound.Persist` saves on each
progress event goes through `Record()` (`LiveRound.cs:256`), whose sixth argument is `DateTime.UtcNow` (`:276`); `Finish`
overwrites it with the completion (`:235-237`) and the sweep with the sweep's own time (`SessionStore.cs:626`). So "a running
peer has no completion" is false on disk, and the rule reads the status, not the stamp. The horizon of §S2.4 is the same
stamp of THIS round: `Finish` stamps it (`:237`) before E5's step runs, one statement after the `with` block (`RoundEngine.cs:485-496`), so
the completion precedes the live read by construction. The containment rule is S1's
`Repositories.Contains` / `RepositoryOf` (moved from `AgentLog.cs:304-317`).

### S2.1 The model in one paragraph

A gate session's **recorded** rounds are those that will carry an authors snapshot: `Stage ∈ {PlanReview, CodeReview}`,
`Verdict ≠ skipped`, `Status ≠ interrupted`. Each recorded round owns one **window** `[previous recorded round's start,
own start)`; the first recorded round also owns the **`before-open`** window `[prior end, open)`. Everything else in the
journal — skips, interrupted rounds, feature and document rounds — opens no window, so
its stretch lands in the next recorded round (parent §16 D1, D2) and nothing is ever lost to a round that records no
authors. A recorded round with no `StartedUtc` (a row older than the field) is different: it is **blind** (decision 1) —
it is a recorded round and so a tiling boundary, but the boundary's position is unknown, so its own window and the next
recorded round's window are `unobserved: no round start` and place nothing; nothing of theirs is re-tiled into a
neighbour, because a window with one unknown edge would over-credit it (*2026-10-16, G8: this paragraph said such a
round "opens no window"; decision 1 is the rule*). A recorded round is **settled** when it is `done`; a running round
tiles its own gate but ranks last, claims nothing and bounds no `PriorEnd` (§S2.2b G1, G2). A message is placed by its
one instant into the one window of this gate that contains it. Its **association**
is the first rung of the ladder that holds for its agent in that window (§S2.4). Whether this gate **owns** it, or a
peer gate session of the same caller already did, is decided by **completion rank** (§S2.3): the settled window whose
round completed first claims, and a claim can only ever be made from what the store already held at that moment — which
is what makes the live record and a later backfill agree. Writes are evidence and never contested: a write into this
repository is `wrote here` here and `wrote elsewhere` from every other gate — and a write is evidence for a round only
when its result was written before that round completed, which is what the live read saw and what a backfill reproduces
(§S2.2b G3).

### S2.2 The ten decisions, closed

| # | Decision | Candidate | Reason |
|---|---|---|---|
| 1 | **Boundaries.** A window opens for every recorded round (the predicate above) and for nothing else. A recorded round whose `StartedUtc` is null (older than the field) is **blind**: its window has no edge, it is `unobserved: no round start`, and so is the next recorded round, whose window would start at the blind one's unknown start; the round after that is tiled normally. `OpenedUtc` null → the first recorded round is `unobserved: no open time`. An open stamped after the first round's start is clipped to that start (the before-open window ends there, the round-1 window is empty). Feature and document rounds open no window — E5 §3 gives them no snapshot, so a window of theirs would lose its stretch. | kept, extended | D1/D2 settle skips and interruptions; the blind rule keeps the candidate because a window with one unknown edge over-credits the next round with no symptom — the one error this epic exists to avoid — while `unobserved` is counted as excluded by E6. |
| 2 | **Several callers.** Round N reads its OWN caller's transcript over the whole window. If the previous recorded round's `CallerSessionId` differs → the round window is `partial: caller changed`; if it is empty (an old row, a `repo:` caller) → `partial: prior caller unknown`. The input must carry that caller: `Caller.CallerSessionId == Gate.Rounds[this].CallerSessionId` is a `Validate()` rule. | kept (E4/6) | the other caller's activity after N−1 started is in a transcript this round did not read; the window is a floor and says so. |
| 3 | **Overlap of two gate sessions.** Every window of the gate and of every peer served by this caller is ranked by `(round.CompletedUtc, gate session id ordinal, kind: before-open before round)`; a completion counts ONLY when its round is `done` (*2026-10-16, G1: the running row on disk carries a stamp too*), and a null or non-`done` completion ranks last. A message in this round's window whose own association is OWNING (`wrote here`, `child of`, `same-session`, `ambiguous`) is **`claimed by peer`** when a **settled** peer window containing its instant ranks BEFORE this round's window and the agent's association in THAT window (by the peer's repository, the peer's window evidence as the peer saw it — §S2.4's horizon) is owning. A peer window whose round has another caller, or is not settled, is never a claimant (*G2*); a not-settled same-caller peer window that overlaps this round's window makes it `partial: peer running`, because the ownership decided now is provisional until that round settles (*G3*). | **replaced** (was: earliest-closing by start) | a claim must be decidable from what the store held at the claiming round's own live time. The store learns of rounds in completion order (`Projection.Write`, `RoundEngine.cs:612` on main — was `:607`, §8.1 — is synchronous at completion); earliest-closing-by-start is not: a peer round that STARTED earlier but completes later, or dies interrupted, is invisible or mutable at this round's live time, so its claim would differ between live and backfill. Under completion rank the later-completing round always defers to the earlier one it can see. |
| 4 | **An agent that wrote into two repositories in one window** (here and elsewhere, by this gate's own window). Its association is `ambiguous` with rung `two repositories`; it gets a `wrote here` ROW whose `write_calls` are its writes into this repository and whose turns and tokens are ZERO; its messages are owned once — by this gate (into the ambiguous totals) or by the peer that ranks first (`claimed by peer`); it counts one ambiguous AGENT in every gate whose window sees two repositories. Launches follow the messages. | kept, made exact | D5: counted ambiguous in each candidate, tokens never twice; the row carries the authorship evidence (write calls) so S4 lists it as a contributor of each repository it wrote into without a token being added twice. |
| 5 | **Rule 2.** Only a SUBAGENT parent propagates, through `ObservedAgent.Parent` where `!Parent.IsMain`, recursively up a chain of subagents, and only when the parent is `wrote here` or `child of` **in the same window**. `main`'s association never propagates. | kept (E4/4) | `toolUseId` makes main the parent of every depth-1 agent, so a main that wrote here would associate every scout of every other repository. |
| 6 | **Universe.** E5 passes `Repositories` = the gate's `repo_path` ∪ every peer's `repo_path` ∪ every `Checkout` root E3 resolved for a write in this observation (`WriteTarget.Where`). the write's root is `RepositoryOf(write.Where, universe)` for `Kind = Checkout` (the longest containing root, so a submodule or nested worktree resolves to its own root, not its superproject) and for `Unresolved` with a non-empty `Where` (a removed worktree's directory — the root only if some universe root contains it), and `""` for `OutsideAnyCheckout` — the mapping is tested for each kind, including a submodule root, a nested worktree and an unresolved write; `""` is `outside` and is never evidence. A root is `here` when `Contains(gate.RepoPath, root)`; a **destination** (rule 4) is the gate or peer `repo_path` containing a root, else the root itself, so a submodule and its superproject are one destination. | settled (D3) | — |
| 7 | **Evidence horizon.** This round's windows end at its start, so its own ladder and ownership see only ITEMS whose instant is before the start (D6) — and an item's instant is its MESSAGE's first line (E3 S4-8): a message, its launches and its writes are one unit and land in one window, even when the write's own line is stamped after the start (*2026-10-16, F2 (a)*). **A write is evidence for a round only when its `tool_result` was written before that round's completion** (`AnsweredAt < CompletedUtc`, *2026-10-16, G3*) — for this gate's own ladder the horizon is THIS round's completion, for a claimant's ladder it is the CLAIMANT round's completion, so a peer's claim is evaluated on the peer's own window and on exactly the writes the peer's own live read could have seen. The rule is applied live too, where it drops at most a result stamped in the moments between the stamp and the read — the price of a live run and a backfill that compute the same thing. **Lines at or after this round's COMPLETION therefore never change which items are placed, who owns them or how they are associated.** They can change ONE counted value, and that is the stated limitation of this epic (*narrowed 2026-10-16, F1; limitation (ii) withdrawn by G3*): the token terms of a message whose lines were still being written at the completion — E3 takes the per-term maximum over the lines it saw (E3 S4-2; one id's lines span up to 2.01 h, RESULTS Q4). It is a live-versus-backfill difference, never a backfill-versus-backfill one (E7's idempotence check is unaffected). | **refined** (D6), **narrowed** (F1), **made literal** (G3) | truncating the input at this round's start would evaluate a peer's claim on less evidence than the peer itself had: in the overlap of two windows an agent that wrote into the peer's repository only after this round's start reads `elsewhere` to the peer and owning to us — one message in two rows. Exactness across gates needs the peer's evidence as the peer saw it. |
| 8 | **Edges.** Half-open `[From, To)`; a message at exactly a round's start is in the NEXT window. A launch is the agent's `FirstLineAt`, in that window only; main has no launch; a resumed agent launches nothing again. Association is per (agent, window); decision 4 is the only case where an agent's messages and its row part company. | kept | — |
| 9 | **A missing child.** Every `UnobservedLaunch` — E3 emits one for an `Agent` call without a transcript AND one for a meta without one (E3 step 8a, `:912-917`) — is an **unobserved launch** of the window containing its instant: that window is `partial: <its Why>`, it gets no row, no launch placement and no tokens — never a zero. Other windows stay `observed`. An `ObservedAgent` with `TranscriptPresent = false` is the SAME meta seen from the agent list (E3 `:880-881`): it stays declared (a child may name it as parent) but is **inert** — no launch, no item, no reason, its `FirstLineAt` read by no rule (*2026-10-16, G4; the text turned both records into items and counted `c05` three times*). | kept, counted once (G4) | a row with zero tokens reads as an observed zero; one missing transcript is one loss. |
| 10 | **Clocks.** Transcript instants are the client's clock, round stamps the server's; compared in UTC with no tolerance. Stated limitation: two rounds of two gates completing within the same instant and projected in the other order can make a live record differ from the backfill in which of them owns a contested message; the backfill is the authority and a second backfill changes nothing — made true for the stored rows by G3: a round whose ownership was decided while a same-caller peer round was still running is `partial: peer running`, and E7 recomputes exactly those rows once the peer has settled (§S2.2b G3). | kept, one limitation added | a tolerance is a guess. |

### S2.2a Fable's decisions on §9 and on E5's §8 (2026-10-16)

One line each: the decision, its reason, and the evidence. "E3" means the E3.S4 amendment of the same day
(`feat/round-authors-e3` `165de3bc`), whose rules this story copies rather than restates.

| # | Decision | Reason | Evidence |
|---|---|---|---|
| §9 F1 | **Accepted, narrowed as decision 7 now reads**: placement and ownership never change after completion; a straddling message's tokens and a write answered after the live read are the two stated limitations. `LinesAtOrAfterCompletionChangeNothing` keeps its builder form (one instant per message); S5's replay facts are written for the narrower claim. *Later the same day (§S2.2b G3) the second limitation was withdrawn: a write answered after the completion is not evidence, live or backfilled, so only the straddling tokens remain* | the per-term maximum (D8) and "no result → no write" (D23) are E3's rules and both read lines after the start; a tolerance or a second read would be a guess | RESULTS Q4 (span max 2.01 h); E3 S4-2, S4-7 |
| §9 F2 | **(a): keep E3 S4-8.** A write's instant is its message's first line; decision 7's horizon applies to the ITEM, whose instant that is | a turn is the unit of work: its message, launches and writes belong to one window and one association. (b) would place a write in a window where its agent has no placement, which the ladder cannot express, and would split one turn over two rounds. The cost is stated: a write executed after round N's start can be credited to N although its bytes land in N+1's diff; E2's size comes from git and is unaffected | c13 L4 (`Write` at `:04`, round start `:03`, message began `:01`), c15 L4 (`:07` / `:06` / `:04`), c16 L4 (`:06` / `:05` / `:03`) — all into `scratch`, so no cell changes today |
| §9 F3 | **Confirmed: refuse, never merge.** `Validate()` names the duplicate; `Assign` throws; E5 records `unobserved: invalid input`. S5's `c13` row stands as written (the replay fact runs over ONE copy) | the journal is appended once per round (`RoundNumber.cs:25`); a duplicate is a store defect, and merging "identical" copies would hide it while a non-identical pair has no right answer. E7's idempotence is proven by running the backfill twice, not by this fixture | `c13/rounds.json`: round 1 listed twice, byte-identical; S1 §216-223 |
| §9 F4 | **S2 copies E3's REASONS verbatim — the `const`s of E3's `ObservationReasons` — and never a diagnostics count.** E3 moved `meta without model` to `Diagnostics.MetasWithoutModel` (E3 S4-17), so it is no E3 reason and no E4 state; §8.3 item 2 is closed | a reason is for a LOSS or an uncertain counted value; a meta that named no model lost nothing — the answering model is on the agent's own lines | RESULTS Q2 (32–98 % of metas carry `model`); `c04` and `c07` now read `observed` in E3 |
| §9 F5 | **Confirmed and corrected** in the scope paragraph: a repeated skip moves `CompletedUtc`, `Repeats` and `Sha`; `StartedUtc` does not move; a skip opens no window | one `with` expression | `src_mcp/src/Server/Rounds/RoundSkips.cs:97` |
| E5 §8.1 | **`AuthorRow` and `ActivityTotals` are FINAL as S3 prints them**; E5 persists them as they are inside its own `AuthorsSnapshot`, and `RoundAuthors.WritesOutsideTheGateCheckout` (checkout roots) stays in memory. A later change to either record is an E4 document edit announced to E5 before E5.S1 is coded | one shape, two owners would drift; roots never reach a file | E5 §8 item 1; S3's record block |
| E5 §8.2 | **`AgentKey.MainId` stays `"main"` in the core; E5 maps it to the parent's empty `agent` column in ONE place** | an empty string as a dictionary key and an ordering key is a smell; the column spelling is the parent's and E6's | E5 §8 item 2 |
| E5 §8.3 | **`[JsonIgnore]` on `AgentKey.IsMain`** (S1's block below), with an S1 fact that a serialised `AgentKey` has exactly one member, `id` | a computed property that serialises into every row is a stored copy of a rule | E5 §8 item 3; S1 `:143` |
| E5 §8.4 | **The card is dropped from S4's consumers**: contributors are the report's (E6); the card (E5.S6) shows this round's stored rows only | computing the union in TypeScript would be a second implementation of S4 | E5 §3; S4's consumers sentence |
| E5 §8.5 | **§S2.8 is the rule**: peers are found by `rounds.authors_session`, else `rounds.caller` when it is a UUID; S1's sentence is aligned | S1's sentence predates §S2.8 | S1 *Peers* paragraph |
| E5 §8.6 | **No reason contains `", "`**, and S3 proves it: `NoReasonContainsTheJoiner` over `AttributionReasons`, E3's `ObservationReasons` constants and `NoReaderFor(vendor)` (the vendor matches `^[a-z][a-z-]{0,19}$`, E3.S2). E4 reasons carry NO counts (distinct, first-seen order), so E5's `IsWellFormed` need not admit E3's `×N` | E5 joins them with the same two characters | S2.3 *States*; E3 §3 |
| E5 §8.7 | **Accepted**; the anchors in this story's text are corrected here (`RoundEngine.cs:898-907`/`:900`/`:901`, `:485-496`/`:591`, `:612`) and §8.1 stays the record | nothing in the logic depends on them | §8.1 |

Two facts from the E3 amendment this story must know and needs no rule for: E3's `FirstLineUtc` is the main file's
first stamped line in FILE order, not a minimum (six fixtures start with a line stamped after the next one) — it is no
edge here, because the `before-open` window is unbounded below and `PriorEnd` is a read bound; and E3's new diagnostics
(`MetasWithoutModel`, `ValuesOffShape`, `MessagesDroppedAtBudget`) never become states.

### S2.2b Fable's decisions on §11 (2026-10-16)

One row per item: the decision, its reason, the evidence, and where the rule now lives. "E3" is the E3 document at
`feat/round-authors-e3` `fd668f62` (byte-identical to the branch tip `48ba4e5c`); "E5" is `feat/round-authors-e5`
`bffbcc7b`; "E7" is `feat/round-authors-e7`; code is `origin/main` `ce9cd298`.

| # | Decision | Reason | Evidence | Where |
|---|---|---|---|---|
| G1 | **A completion ranks only on a `done` round.** `Windows.Of` sets `Window.CompletedUtc = r.Status == done ? r.CompletedUtc : null`; E5's mapping applies the same rule (belt and braces); a running peer that carries a stamp ranks last and claims nothing | the stamp is positional and every row has one: the running row goes through `Record()`, whose sixth argument is `DateTime.UtcNow`; `Finish` and the sweep overwrite it. A rule that read the stamp would rank a running peer by the time of its last progress tick | `LiveRound.cs:256`, `:269-276`; `:235-237`; `SessionStore.cs:84`, `:626`; E5 `:65` says a running peer "is in the file with no completion" — false, listed for E5 | §S2.3 *Tiling*, *Rank*; fact `ARunningPeerWithAStamp_RanksLast` |
| G2 | **`PriorEnd` is the end of the SETTLED PREFIX, and only settled windows claim.** Walk the peer's recorded rounds in journal order while each is settled (`done`), non-blind, of this caller, started at or before the gate's open, and whose window ranks BEFORE this gate's before-open window (it completed before this gate's first recorded round completed — *the fifth condition, Fable 2026-10-19: a peer round that completes after this round never bounds it; if it did, the bound would become an ownership rule*); `PriorEnd` = the last such round's start, null when the walk stops at once. `ClaimantsOf` admits settled windows only | the equality "owned set with `PriorEnd` = owned set without it" needs every message before `PriorEnd` to lie in a window that WILL claim it; a running round's window may vanish (interrupted), a blind one places nothing, another caller's claims nothing — so the walk stops at the first of them instead of skipping it (an orphaned `running` row can sit before a `done` one in a journal: the sweep runs at startup, `IsOrphaned` `SessionStore.cs:718-719`) | risk consultation turn 1 item 2 (A starts `+60` running, B opens `+100`, message `+30`); case 7 | §S2.3 *Prior end*, *Claimants*; case 8c; fact `APriorEndStopsAtTheFirstUnsettledPeerRound` |
| G3 | **The owner of stored uniqueness is E7's backfill; E4 makes the two hazards decidable.** (i) *Late-answered write:* a write is evidence for a round only when `AnsweredAt < that round's CompletedUtc` — THIS round's completion for the own ladder, the CLAIMANT round's for a claimant's ladder — applied live and backfilled alike, so the live view is reproducible exactly and the hazard is gone (`ObservedWrite.AnsweredAt`, a member E3 owes). (ii) *Concurrent completion:* a window overlapped by a not-settled same-caller peer window is `partial: peer running` — ownership was decided from an unsettled store and is PROVISIONAL; E5 stores it as it is and never repairs another gate's rows live (it holds no claim on that session); E7 recomputes exactly the rows whose reasons include `peer running` once no overlapping round of a named peer is still `running` (done or interrupted — *final*, M7) — one of the TWO exceptions to "equal reader → keep" (the other is E5's lost-snapshot recompute, E5 §10 item 11; L10) — and writes the result under that session's claim. (iii) The assignment names the peer gates it saw (`PeerGates`, ids only) so a backfill can tell a round whose peer set grew (a peer whose projection failed, `Projection.Write` is best-effort) and recompute it too | uniqueness of STORED rows is a property across gate sessions, which only a process that sees them all after they settled can hold; a live cross-gate repair would write another session's file mid-round without its claim. Without a marker the backfill cannot tell a provisional row from a final one, and the no-overwrite rule would freeze the duplicate | `SessionClaim.cs:32-38` (the key is repo + branch + document + feature, never the caller); `LiveRound.cs:237` (stamp) → `RoundEngine.cs:485-496` (the record; E5's step is the statement after it) → `:591` (save) → `:612` (projection); `Projection.cs:24-38`; parent §9 ("never overwrites an `observed`/`partial` row produced by a newer or equal reader version"); E7 `:165-174` rule (2) | §S2.3 *States*, *Claimants*; §S2.4 *WriteRung*; §S2.5 `PeerGates`, `RoundAttribution.Version`; §11 cross-epic list (E3, E5, E7, parent) |
| G4 | **One missing transcript is one item and one reason.** Only `UnobservedLaunch`es make unobserved-launch items and `partial: <why>`; an `ObservedAgent` with `TranscriptPresent = false` is inert (declared, no launch, no item, no reason; `FirstLineAt` nullable and read by no rule). `c05` → 2 | E3 emits BOTH records for one meta without a file: the key goes into `Agents` with `TranscriptPresent = false`, and step 8a emits `UnobservedLaunch(ParentKey, ToolUseId, CalledUtc, MetaWithoutTranscript)`; the reason text lives on the launch, not on the agent | E3 `:206-210`, `:880-881`, `:916-917`; E3 test 20 (`:1049`): `UnobservedLaunches.Count == 2` on `c05`; test 22 (`:1051`) | decision 9; §S2.3 *What is placed*, *States*; S1's record comment; fact `AMetaWithoutATranscript_IsOneUnobservedLaunchNotTwo` |
| G5 | **Every E3 reason arrives with `At = null` and marks every window of the round.** The mapping turns each `ObservationReason` into `StateReason(Code, null)` — except `agent call without transcript` and `meta without transcript`, which are not mapped as reasons at all: their instants ride on the `UnobservedLaunch`es (G4), so a missing child marks only ITS window and decision 9's "other windows stay observed" holds. S3's state table is corrected: the "read budget hit before the window's end" row is gone — a read budget marks every window | `ObservationReason(Code, Count)` has no instant; placing a reason by guessing an instant would be inference. A whole-session reason (torn middle line, read budget, inconsistent transcript) IS about every window — nothing is lost by saying so | E3 `:231`; E3 step 10 (`:924-926`): reasons are counts ordered by code | §0 table; §S2.3 *States*; S3 *State*; fact `AnE3ReasonWithoutAnInstant_MarksEveryWindow` |
| G6 | **`AssignEvery` runs over the SETTLED rounds of the observation's caller only**, in every timeline; a round of another caller or a running round is skipped (both would fail `Validate()`); the `PeersKnown = false` scenario of case 7 is outside identity 2 by design (it duplicates and says so). `Validate()` refuses attributing a round that is not recorded and settled | identity 2 is a statement about what THIS caller's transcript lets the settled rounds own; another caller's rounds read another transcript | §S2.5 identity 2; S1 `Validate()`; case 8b (B C1 by `c9`), *Caller changed* | §S2.5; §S2.9 item 2 |
| G7 | **The token identity, restated over message ids**: over `AssignEvery`, every message id placed in some window of some settled round of this caller is either owned exactly once (its tokens counted once) or owned by nobody and labelled `wrote elsewhere` in EVERY assignment that places it; so Σ owned tokens = Σ tokens of the ids owned somewhere. `cx +10` (owned by A, `wrote elsewhere` in B) is counted once | a message `wrote elsewhere` from one gate is normally `wrote here` from another — subtracting it was wrong whenever it was owned | §S2.10 `NoMessageIsOwnedTwiceTests` | §S2.5 identity 2 |
| G8 | **§S2.1 now says what decision 1 says**: a recorded round with no `StartedUtc` is blind, it and the next recorded round are `unobserved: no round start`, nothing is re-tiled into a neighbour | one rule, two texts | §S2.1 vs decision 1 | §S2.1 |
| G9 | **Per-window state AND totals are kept.** `RoundAuthors` gains `Windows: ImmutableArray<WindowAuthors>` — one per window of the round, empty windows included, each with its `State`, `Reasons`, `Ambiguous`, `Elsewhere`, `ClaimedByPeer`, `UnobservedLaunches`; the round-level members stay as the roll-up (the WORST state, reasons distinct in window order, totals summed — `Unknown` when any window is `unobserved`). `AuthorRow` and `ActivityTotals` are unchanged (final); `RoundAuthors` was never declared final, and the addition is announced to E5 (snapshot ≈ +300 B a round) | E6 leaves `before-open` out by default and then cannot know the state or the ambiguous share of the windows it keeps: "before-open partial, round observed" and the reverse store the same round state. Both windows of a first round are blind together (open and start are their shared edges); a blind round has no counted window (*corrected 2026-10-19, Fable: the text said a blind before-open left the round window counted, which cannot arise — §12 deviation 2*) | cadence consultation item 2; E6 D2/D3 (`feat/round-authors-e6` `:74-75`) | S3 *The records*, *State*; §S2.5 last paragraph |
| G10 | **The agent counts are per total, not a partition; turns, launches and tokens are.** `Ambiguous.Agents` = distinct agents of owned `ambiguous` items ∪ agents with `AgentAssociation(ambiguous, two repositories)` in the window (decision 4: a two-repositories agent is ambiguous in every gate that saw two repositories, whatever owns its messages); `Elsewhere.Agents` / `ClaimedByPeer.Agents` = distinct agents of those items. The two-repositories row has `Model = Requested = Effort = "unknown"`: it carries authorship evidence (write calls), not a model claim; its models are in the ambiguous totals, which carry none (§8.3 item 7). S3's sentence becomes "the two `unknown` rows are the launch-only row and the two-repositories row". `BothGatesCountOneAmbiguousAgent` moves to S3 | S2 makes no totals; a count rule belongs where the counting is | §S2.6 *Two repositories, one agent*; S3 *Grain* | S3 *Grain*, *Agent counts*; `ATwoRepositoryAgentKeepsItsWritesAndAddsNoTokenTests` |
| G11 | **Completion rank stays; the caller-level shared total is rejected.** With rank, the first-completed settled window owns contested activity; `claimed by peer` names the allocation on the other side; E6 states the convention in one sentence and never reads the allocation as evidence of a repository or a model. *Rejected:* a shared total that no gate owns. Reasons: (1) it needs an entity no store has — rounds are per gate session, `sessions` per repo + branch; a caller-level aggregate is a new table, a new writer under no claim and a new backfill target, carried by E5, E6 and E7; (2) it is not decidable live — a shared total is known only when every gate of the caller has settled, so the card could never show it and the report would wait for the backfill (parent §8: it does not); (3) ambiguous activity is already kept out of repository and model conclusions (parent §6 "never guessed"), so the allocation decides a number in a column no conclusion reads — what matters is once (identity 2) and labelled (`claimed by peer`, and E6 can read "seen by two gates" per round as `ClaimedByPeer`); (4) rank is the one rule that keeps live = backfill for ownership (D24), which a shared total would have to solve again for its own writes. Cost accepted and stated: the first-completed gate's `Ambiguous` totals carry activity that may equally belong to the other gate | the consultant called rank "defensible as an accounting convention but no repository evidence" — both halves are right, and the second is already how E6 treats `ambiguous` | risk consultation turn 1; parent §16 D24; E6 `:74-75`, `:496` | decision 3; §11 cross-epic list (E6) |
| G12 | **Small text points.** (a) one ordering word: states are ordered `observed < partial < unobserved` by badness and the WORST wins, in §S2.3 and §S2.5 (E6 D3 uses the same word); (b) claimant containment is `P.Contains(t)` (a null `From` is unbounded); (c) the `SessionRung` destination set is built over EVERY gate in the input — `input.Gate` and every `input.Peers[*]` — whichever gate the ladder runs for, so a peer's ladder sees this gate's `repo_path` as a destination; (d) `NoInputMemberIsACwd` and `ABashCallIsNotAWrite` are positives: a shell cannot fail them, and their break-its (§S2.11 rows 4 and the `ABashCallIsNotAWrite` plant) are what prove them; (e) the four anchors are corrected in place (`SessionStore.cs:278`, `:608-631`/`:626`, `RoundEngine.cs:900` twice) and §11's E3 anchors with them (`:880-881`, `:912-917`, `:231` on the E3 document named above) | — | own review; §8.1 | §S2.3, §S2.4, §S2.5, §S2.10, §S2.11, the scope paragraph |

### S2.3 Windows and ownership — the exact rules

**Recorded** (`Windows.IsRecorded(GateRound r)`): `r.Stage is "PlanReview" or "CodeReview"` ∧ `r.Verdict != "skipped"` ∧
`r.Status != "interrupted"`. The spellings are `Stage` enum names (`core/Rounds/SessionState.cs:264-267`),
`RoundRecord.Skipped` and `RoundRecord.Interrupted` (`SessionStore.cs:88,121`). **Settled** (`Windows.IsSettled(r)`, *2026-10-16,
G1/G2*): `IsRecorded(r)` ∧ `r.Status == "done"` (`RoundRecord.Done`, `SessionStore.cs:87`). A recorded round that is not
settled is `running` (`:86`): it tiles its own gate's journal like any recorded round, but it ranks last, claims nothing and
bounds no `PriorEnd`. The round being attributed is always settled (`Validate()`, S1). **Final** (*2026-10-17, M7*):
`r.Status is "done" or "interrupted"` — a round that will never change again; `running` is neither settled nor final.
Settled is the CLAIM condition (an interrupted round opens no window and claims nothing); final is the REPAIR condition:
E7 recomputes a `peer running` row once every overlapping round of every gate it names is final — an interrupted peer
round never becomes settled, and waiting for that would leave the row provisional for ever (case 8b).

**Tiling** (`Windows.Of(GateTimeline g, DateTimeOffset? priorEnd) → ImmutableArray<Window>`), over the recorded rounds
`r_1 … r_n` in journal order:

```
Window(Gate, Kind, Stage, Number, From, To, CallerSessionId, CompletedUtc, Blind)
  before-open  : Kind "before-open", Stage/Number/Caller/Completed of r_1, From = priorEnd (null = unbounded), To = min(open, r_1.start)
  round k = 1  : Kind "round",       From = min(open, r_1.start),                             To = r_1.start
  round k > 1  : Kind "round",       From = r_{k-1}.start,                                    To = r_k.start
  Blind        : r_k.start is null, or (k > 1 and r_{k-1}.start is null), or (k = 1 and open is null)
  Completed    : r_k.Status == "done" ? r_k.CompletedUtc : null        -- G1: a running row's stamp is its last progress tick, never a rank
```

A blind window has `From = To = null` and takes part in nothing. `Validate()` (S1) already refuses a journal whose
recorded starts decrease; equal starts give an empty window, which is legal.

**Prior end** (`Windows.PriorEnd(gate, peers, caller)`, *rewritten 2026-10-16, G2*): for each peer whose `RepoPath` is
`Normalise`-equal to the gate's, walk its recorded rounds in journal order while the round is settled, non-blind, has
`CallerSessionId == caller`, `StartedUtc ≤ open`, **and its window ranks BEFORE this gate's before-open window** (it completed
before this gate's first recorded round completed — `Windows.Precedes`; *the fifth condition, added by Fable 2026-10-19,
§12 deviation 1*), and take the LAST such round's start — the end of the peer's **settled prefix**; the walk stops (does not
skip) at the first round that fails any of the five, because a running round's window may vanish when it is interrupted, a
blind window places nothing, another caller's window claims nothing, and a window that ranks after this gate's claims
nothing from it either — a message in any of them would be owned by nobody with the bound and by this gate without it. A
peer round that completes after this round never bounds it: if it did, the bound would become an ownership rule
(`Windows.cs` `PriorEnd`, `SettledPrefixEnd`, `ClaimsBefore`). `PriorEnd` is the maximum
over the peers; null when every walk stops at once, or when `PeersKnown` is false. It is a READ bound, not an ownership
rule: §S2.6 case 7 proves the owned set is the same with and without it, and case 8c proves the walk must stop.

**Rank** (`Windows.Rank(Window w)`): the tuple `(w.CompletedUtc ?? MaxValue, w.Gate, w.Kind == "before-open" ? 0 : 1)`,
compared ordinally on the gate id — `w.CompletedUtc` being the tiling's `Completed`, so a not-settled window ranks last
whatever stamp its row carries. `Windows.Precedes(a, b)` is `Rank(a) < Rank(b)`.

**Claimants** of this round's window `W` (`Ownership.ClaimantsOf(W, peerWindows, caller)`): every peer window `P`
with `P.CallerSessionId == caller`, not blind, **settled** (`P.CompletedUtc` not null, *G2*), `Precedes(P, W)`, and
`P ∩ W ≠ ∅`, in rank order. A not-settled same-caller peer window with `P ∩ W ≠ ∅` is no claimant but marks `W`
`partial: peer running` (*States*, below; *G3*).

**Ownership** of one item (a message or a launch) of agent `g` at instant `t` in `W`
(`Ownership.Of(item, W, claimants, ladder)`):

1. `own = ladder(g, W, gate)`. If `own == wrote elsewhere` → the item is **`wrote elsewhere`** (not owned; the root is
   kept on the placement). Claimants are not consulted: elsewhere is this gate's own evidence.
2. Otherwise, for each claimant `P` in rank order with `P.Contains(t)` (*G12b: a null `From` is unbounded*): if
   `ladder(g, P, P's gate)` is owning → the item is **`claimed by peer`** (`OwnerGate = P.Gate`). Stop at the first. The
   placement keeps this gate's OWN rung; `OwnerGate` names the peer (*Fable 2026-10-19; `Ownership.Of`*).
3. Otherwise the item is owned here with association `own`, rung as the ladder says.

**Owning** associations: `wrote here`, `child of`, `same-session`, `ambiguous`. Non-owning: `wrote elsewhere`,
`claimed by peer`.

**What is placed.** Every `ObservedMessage` whose `At` lies in a non-blind window of THIS round, as a `Placement`;
every subagent with `TranscriptPresent = true` whose `FirstLineAt` lies in one, as a `LaunchPlacement` (an agent with
`TranscriptPresent = false` is inert — *G4*: its launch is the `UnobservedLaunch` E3 emitted for it); every `ObservedWrite`
whose `At` lies in one **and whose `AnsweredAt` is before THIS round's `CompletedUtc`** (*G3*: a result written after the
completion was not in the live read and is not evidence in the backfill either), as a `WritePlacement`
with its root and `here | elsewhere | outside`; every `UnobservedLaunch` whose `At` lies in one. Nothing outside the
round's windows is placed, which is the evidence cutoff: a line stamped at or after this round's start is in no window
of this round. **When the observation is `unobserved`, nothing is placed and every window is `unobserved`** (*Fable
2026-10-19; `AssignRun.Build`, `WindowStates`*).

**States** (`WindowState(Window, Kind, Reasons)`): the kinds are ordered `observed < partial < unobserved` by badness and
the **worst wins** (*G12a: one word, here, in §S2.5 and in S3 — E6 D3's word*), reasons accumulated and
distinct in first-seen order: `Caller.State.Kind == unobserved` → every window `unobserved` with its reasons; a blind
window → `unobserved: no round start` (`no open time` when it is the open that is missing); `PeersKnown == false` →
`partial: prior windows unknown` on every window; an `UnobservedLaunch` with `At` in the window → `partial: <its Why>`
(one with `At == null` → every window); a not-settled same-caller peer window `P`, not blind, with `P ∩ W ≠ ∅` →
`partial: peer running` on `W` (*G3*: the ownership decided for `W` is provisional until that round is FINAL — done or
interrupted, M7 — and E7 recomputes exactly these); a `StateReason` of the observation — every E3 reason arrives with `At == null` (*G5*) →
`partial: <Why>` on every window (the `At`-inside-the-window branch stays for a reason that one day carries an
instant); the round window whose previous recorded round has another
caller → `partial: caller changed` / `partial: prior caller unknown`; none of these → `observed`. Reason words are a
closed set: `AttributionReasons` holds `NoRoundStart = "no round start"`, `NoOpenTime = "no open time"`,
`PriorWindowsUnknown = "prior windows unknown"`, `CallerChanged = "caller changed"`,
`PriorCallerUnknown = "prior caller unknown"`, `PeerRunning = "peer running"` (*added 2026-10-16, G3*); every other reason is E3's constant, copied verbatim, never composed —
a `const` of E3's `ObservationReasons`, which since 2026-10-16 holds no `meta without model` (that is an E3 diagnostics
count, F4); a diagnostics count of E3's is never a state here. No reason of either set contains `", "` (E5 joins them;
S3's `NoReasonContainsTheJoiner` proves it).

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
| roots = distinct `RepositoryOf(write.Where, input.Repositories) (by Kind, decision 6)` over `write.Agent == g`, `w.Contains(write.At)`, **`write.AnsweredAt < horizon(w)`** (*G3*), root ≠ `""` | here = roots with `Contains(gate.RepoPath, root)`; elsewhere = the rest | here ≠ ∅, elsewhere = ∅ → **`wrote here`** · here ≠ ∅, elsewhere ≠ ∅ → **`ambiguous`**, rung `two repositories` · here = ∅, elsewhere ≠ ∅ → **`wrote elsewhere`** (roots kept) · roots = ∅ → next rung |
| `ChildRung` | `p = input.Agents[g].Parent`; `p` declared, `!p.IsMain`, and `Of(p, w, gate)` ∈ {`wrote here`, `child of`} | **`child of`** · else next rung |
| `SessionRung` | `destinations(w)` = distinct destination of every write by ANY agent in `w` (the same `Contains` and horizon tests as the `WriteRung`) with root ≠ `""`, where destination = the longest of the `RepoPath`s of EVERY gate in the input — `input.Gate` and every `input.Peers[*]`, whichever gate the ladder runs for (*G12c*) — that `Contains` the root, else the root | `destinations == {gate.RepoPath}` → **`same-session`** · otherwise (none, another, or several) → **`ambiguous`**, rung `no evidence` |

**The horizon** (*2026-10-16, G3*): `horizon(w)` is the completion of the round whose window `w` is — THIS round's
`CompletedUtc` when the ladder runs for this gate's own window, the CLAIMANT round's `CompletedUtc` when it runs for a
claimant's window (a claimant is settled, so it has one). A write answered at or after the horizon did not exist for that
round's live read and is not evidence for it in a backfill either; applied live, the test drops at most a result stamped
between the stamp (`LiveRound.cs:237`) and the read (E5's step, the statement after `RoundEngine.cs:485-496`), which is the price of a
live run and a backfill that agree exactly. The same filtered write set feeds the `WritePlacement`s and S3's `write_calls`,
so a row's write calls and its association come from one evidence set.

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
    ImmutableArray<AgentAssociation> Agents, ImmutableArray<WindowState> States, DateTimeOffset? ReadFrom,
    ImmutableArray<string> PeerGates);   // 2026-10-16, G3 (iii): the gate session id of every peer timeline in the input, ordinal — ids only, so a backfill can tell a round whose peer set grew

public static class RoundAttribution { public const int Version = 1; /* bumped by ANY change to a rule of §S2.3–§S2.4 or of S3/S4 (2026-10-16; E5 §7 item 19, E7 §10 item 3 asked E4 to say so before S2 is coded) */ … }
```

Every array is ordered ordinally — windows by `(Kind, Stage, Number)`, placements by `(Window, At, Agent.Id, MessageId)`,
agents by `(Window, Agent.Id)` — so equal inputs give equal outputs byte for byte, and a permuted input gives the same
output. `WindowAssignment` is what S3 aggregates: rows from owning placements (`Association` ∈ owning, except
`ambiguous`, which goes to the ambiguous totals), the two-repositories row from `AgentAssociation` + `Writes`,
`Elsewhere` totals from `wrote elsewhere`, a new **`ClaimedByPeer`** total from `claimed by peer` (S3 gains it), the
state and the totals PER WINDOW from `States` and the placements (*G9*: S3 keeps them per window; the round's state is
the WORST over its windows, reasons concatenated and distinct), and `PeerGates` carried through.

**Identity 1 — local completeness** (`WindowAssignment.LocalCompleteness(input) → violations`): for each non-blind
window `w` of the assignment, the set of `message.id`s with `w.Contains(message.At)` in the input EQUALS the set of
`Messages[*].MessageId` with `Window == w`, and each appears once; every placement's `At` satisfies
`Window.Contains(At)`. Likewise for subagent launches (`FirstLineAt`, over `TranscriptPresent = true` agents) and for
writes (over the writes with `AnsweredAt` before this round's completion — *G3*).

**Identity 2 — global uniqueness** (`AttributionScenario.AssignEvery()` — a TEST-SIDE helper on the S1 builder, not a
member of `RoundAttribution` (L9, 2026-10-17); it runs the same `Assign`: it builds an `AttributionInput` for every **settled**, non-blind round
**of the observation's caller** in every timeline, with the others as peers — a round of another caller or a running
round is skipped, since `Validate()` refuses both (*G6*); the `PeersKnown = false` variant of case 7 is outside this
identity by design): over all those assignments, the multiset of `MessageId` from placements
whose `Association` is owning is a SET — no id owned twice — and the same for launches by `Agent.Id`. **The token
identity** (*restated 2026-10-16, G7*): every message id placed in some window of some assignment is either owned
exactly once, or owned by nobody and labelled `wrote elsewhere` in EVERY assignment that places it (a `claimed by peer`
label always points at an assignment that owns the id, because a claimant's ladder was owning); so Σ over the
assignments of owned tokens = Σ tokens of the ids owned somewhere — `cx +10` of case 1 (owned by A, `wrote elsewhere`
in B) is counted once. Tokens are therefore never added twice without any further rule.

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
`RoundEngine.cs:900` (*was cited `:895`; corrected 2026-10-16, G12e*) would have started C2 at `+75`. Tokens: 100 in C1,
300 in C2; one launch.

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
into `y`, answered at `+122`) changes B's ladder for cx to `two repositories` and the owner of cx `+75` from A to B's
ambiguous totals: the peer's evidence is honoured as the peer saw it (decision 7), and `AssignEvery` still owns `+75`
once. **The horizon** (*2026-10-16, G3*), on the same scenario: (a) cx's write into `x` at `+75` with `answeredAt +131`
(after A C2's completion `+130`) is not evidence — cx `+75` reads `ambiguous · no evidence` in A C2 (the window's only
destination is `y`, cy's write), not `wrote here` (*corrected 2026-10-19, Fable; §12 deviation 3 — the text said
`same-session`*), and
`Writes` has no placement for it, identically when the input is the "live" one (lines after `+130` absent) and the
"backfill" one (present); (b) in the two-step fact, the `+122` write answered at `+129` (after B C1's completion `+128`)
leaves B's ladder for cx at `wrote elsewhere` — B's live read could not have seen the result, and the backfill must not
see it either; (c) the same write answered at `+127` counts, as the fact above says.

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
owned by A C2 as `ambiguous`; B's window is still a tiling boundary for B. And **a peer still running** (B C1
`Status = running` — on disk it CARRIES a stamp, the time of its last progress tick, say `+105`; the tiling ignores it,
G1): B C1 ranks last, A C2 owns s2 `+80` and is `partial: peer running` (G3: B's window `[+30,+100)` overlaps A C2's
`[+60,+120)` and B is not settled); marking B C1 `interrupted` afterwards gives the same OWNED set for A C2 and the
state `observed` — the backfill of A C2 keeps every row and drops the provisional reason (E7's trigger is FINAL, not
settled, so an interrupted peer round releases the repair — M7); marking it `done @110`
instead gives s2 `+80` to B (`claimed by peer (B)`), which is what E7's recomputation of the `peer running` row
records. `AssignEvery` over the running variant covers A's rounds only (G6) and owns `+80` once.

**Case 8c — the prior end stops at an unsettled peer round** (G2; the risk consultation's scenario). Gate A on `x`,
open `+0`, `C1 @60`, `Status = running`; gate G on `x` (another branch), open `+100`, `C1 @120→140`; caller `c0`; main
message `+30`, a write into `x` at `+30`. For G C1: A's settled prefix is empty (its first recorded round is not
settled), so `PriorEnd` is null and G's before-open is `[∅, +100)`: G owns `+30` `wrote here`, and the window is `partial:
peer running` (A C1's `[+0, +60)` overlaps it). With `.WithoutPriorEnd()` the owned set is identical — the equality case
7 claims holds BECAUSE the walk stopped. Under the old rule (`PriorEnd` = max start over recorded rounds = `+60`) G's
before-open would have been `[+60, +100)`, `+30` owned by nobody with the bound and by G without it. Then A C1
`interrupted` → nothing changes for G but the state (`observed`); A C1 `done @150` → A ranks after G (`+150 > +140`),
so by *Prior end*'s fifth condition it does not bound G's before-open (`PriorEnd` stays null), G keeps `+30`, and A C1's own
assignment labels `+30` `claimed by peer (G)` (fact `WhenThatPeerRoundSettlesAfterG_GKeepsTheMessageAndAClaimsNothing`).

**Case 8d — a settled peer round after an unsettled one.** Case 8c with A `C1 @60→70` done and A `C2 @80` running,
G open `+100`: the walk takes C1 (start `+60`) and stops at C2, so `PriorEnd = +60`; a message at `+70` is in A C2's
window `[+60, +80)`, which is not settled → G's before-open `[+60, +100)` owns it and is `partial: peer running`; a
message at `+50` is in A C1's window, settled, ranked `+70 < +140` → G reads from `+60` and never sees it, and
`.WithoutPriorEnd()` labels it `claimed by peer (A)` — the owned set is equal either way.

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
with `write_calls = 1`, `turns = 0`, tokens 0, `Model = Requested = Effort = "unknown"` (G10). B: the three messages are
`claimed by peer (A)`; B's ambiguous totals count 1 agent (from the `AgentAssociation`, S3's agent-count rule — G10) and 0
tokens, and B's `ClaimedByPeer` totals count 1 agent, 3 turns, 300 tokens; S3's row for B has `write_calls = 1`, zero
turns. Tokens once; w2 a contributor of both.

### S2.7 What S1's contract gains (members, not renames)

| Record | Member | Why |
|---|---|---|
| `GateRound` | `DateTimeOffset? CompletedUtc` | the rank key of §S2.3; E5 maps `RoundRecord.CompletedUtc` through `UtcInstant.From` **when `Status == done`, else null** (G1); the tiling applies the same rule, so a stamp on a running row ranks nothing either way |
| `AttributionInput` | `bool PeersKnown` | false when E5's store lookup could not answer (absent, locked, pre-step database) → `partial: prior windows unknown`; `Peers` is then empty |
| `ObservedAgent` | `FirstLineAt` becomes `DateTimeOffset?` (*2026-10-16, G4*) | null only for a `TranscriptPresent = false` agent whose launching call E3 did not parse; such an agent is inert — its launch is the `UnobservedLaunch` E3 emits — so no rule reads the member; a present agent with null is a violation |
| `ObservedWrite` | `DateTimeOffset AnsweredAt` (*2026-10-16, G3*) | the `tool_result` line's instant — E3's `AnsweredUtc`, a member E3 owes (§11 cross-epic list); the horizon of §S2.4 |
| `WindowAssignment` | `ImmutableArray<string> PeerGates` (*G3 (iii)*) | the peer gate session ids the assignment saw; S3 carries it into `RoundAuthors`, E5 into the snapshot, E7 compares |
| `RoundAttribution` | `const int Version = 1` | E4's own rule version beside E3's `ReaderVersion` (E5 §7 item 19, E7 §10 item 3); E5 stores it |
| `AttributionInput.Validate()` | five more rules: `Caller.CallerSessionId == Gate.Rounds[(Stage, Number)].CallerSessionId`; no `MessageId` twice; a `TranscriptPresent = true` agent with `FirstLineAt = null`; `AnsweredAt < At` on a write; the attributed round not recorded-and-settled (G4, G3, G6) | decision 2; E3 guarantees once per session and S2 relies on it; the horizon needs a completion |

`Assign` throws `ArgumentException` naming the first violation when `Validate()` is not empty; E5 maps it to
`unobserved: invalid input` (a reason E5 adds to its own vocabulary — reported to E5, not decided here).

### S2.8 What E5 must hand over (reported to E5.S4, not built here)

1. **Peers on EVERY round**, not only the first (E5 §3 said first only): gate sessions other than this one holding a round
   whose `rounds.authors_session` — else `rounds.caller`, when it is a UUID (L4: one spelling, §S2.8 item 1 and E4.S1 agree) —
   equals this caller, each as a `GateTimeline` — `OpenedUtc` from the peer's
   SESSION FILE (`PersistedSession.OpenedUtc`, never `sessions.opened_utc`, parent §6), its journal from the file,
   `CallerSessionId` per round from `rounds.authors_session`, else `rounds.caller` when it is a UUID, else `""`.
   `PeersKnown = false` when that read fails. One read-only connection, the `GateHistoryQuery.cs:77` shape.
2. **`ReadFrom` before the read**: `RoundAttribution.ReadFrom(input-without-observation)` → E3's `fromUtc`.
3. **The universe** of decision 6, from E3's `WriteTarget.Where` roots plus the repo paths.
4. The gate's own `GateRound.CallerSessionId` for earlier rounds from the same columns; the round being attributed
   carries the id E3's ladder resolved.
5. *(2026-10-16, §S2.2b)* **`CompletedUtc` by status** (G1): `GateRound.CompletedUtc` is filled only for a `done` row — E5
   §3's sentence "a peer round still `running` is in the file with no completion" is false on disk (`LiveRound.cs:256`,
   `:276`) and must go. **`AnsweredAt`** on every write, from E3's `AnsweredUtc` (G3). **Only settled rounds are attributed**:
   `Assign` is called for a `done` `PlanReview`/`CodeReview` round with `Verdict ≠ skipped` and for nothing else — a
   skipped, interrupted, running, feature or document round has NO snapshot and an EMPTY `authors_state` (*not recorded*),
   live and in E7's backfill alike; E7 computes nothing for it (M6, 2026-10-17 — the text said E7 writes its own reason).
   **`partial: peer running` is stored as it is** and never repaired live — the
   repair is E7's (G3). **`RoundAttribution.Version`** is stored beside `authors_reader` (a column of step 24, or E5's
   choice; E7's "equal reader → keep" compares both). **`PeerGates`** goes into the snapshot (ids only). **Per-window
   `WindowAuthors`** (G9) goes into the snapshot; the columns stay the parent's.
6. *(2026-10-17, judge review)* **The mapping is `AttributionMapping`** (B1, E4.S5): E5.S4 step 5 and E7.S2 call its
   `Observation`, `Timeline` and `Universe` and write no mapping of their own. **The gate timeline must hold the round being
   attributed** (M4): at the live call site `session.Rounds` does not yet — the save appends the record
   (`RoundEngine.cs:594`, `Rounds = [.. session.Rounds, record]`), and `LiveRound.Persist` saves its OWN `_session`
   (`LiveRound.cs:257`) — so E5 hands `Timeline` `[.. session.Rounds, record]`; `Validate()` refuses a `(Stage, Number)` that
   is not in `Gate.Rounds`, and would otherwise make every live round `unobserved: invalid input`. Earlier rounds'
   `CallerSessionId` come from the file's own snapshots (`Authors.Session`, rounds after E5), else `rounds.authors_session`,
   else a UUID `rounds.caller`, else `""` (→ `partial: prior caller unknown`, decision 2) — `RoundRecord.Caller` is a
   `CallerDeclaration(Vendor, Client, ClientVersion, Model)` (`CallerDeclaration.cs:34-38`) and holds no session id, and
   `rounds.caller` is `RoundCommands.CallerFor` (`RoundCommands.cs:34-35`: the stated id, else `repo:<path>`).

### S2.9 The coder's brief (Sonnet)

1. Create `WindowAssignment.cs` with the records and the two vocabularies of §S2.5, `AttributionReasons` of §S2.3, and
   `Windows`, `AssociationLadder`, `Ownership`, `RoundAttribution` as EMPTY shells returning `default`/throwing
   `NotImplementedException` — so the tests below fail on their assertions, not on a missing type.
2. Extend the S1 builder: `.Round(stage, n, startedAt, completedAt, caller: "c0", status: "done")` (`completedAt` is
   the stamp on disk; `status: "running"` keeps the stamp and makes the round unsettled — G1), `.Peer(timeline)`,
   `.PeersUnknown()`, `.WithoutPriorEnd()`, `.MissingChild(parent, at, why)`, `.MetaWithoutTranscript("m2", parent, at)`
   (emits BOTH records E3 does: the `UnobservedLaunch(parent, …, at, "meta without transcript")` and the inert
   `ObservedAgent(TranscriptPresent = false, FirstLineAt = at)` — G4), `.Reason(why)` (an E3 reason, `At = null` — G5),
   `.Write(agent, at, path, answeredAt:)` (G3), `.Repositories(...)`,
   and `AssignEvery()` returning the assignment of every SETTLED round of the scenario's caller over the scenario's gate
   and peers (G6). The builder keeps the `Validate()`-is-empty assertion on every build.
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
| `TheEarlierCompletedWindowClaimsTests` | case 1 end to end: `cxIsHereInAAndElsewhereInB`; `cyIsTheMirror`; `s1IsAmbiguousInExactlyOneWindow`; `s2IsClaimedByPeerInAAndOwnedByB`; `s3IsSameSessionInAAndAbsentFromB`; `AnElsewhereMessageIsLabelledElsewhereNotClaimed` (cy 90 in A C2); `APeerWindowOfAnotherCaller_ClaimsNothing` (case 8b); `APeerStillRunning_ClaimsNothing_AndItsLaterFateChangesOnlyTheState` (case 8b: the owned set is equal whether it is later interrupted; `done @110` moves s2 `+80` to B); added by §S2.2b: `ARunningPeerWithAStamp_RanksLast` (G1: B C1 `running` with stamp `+105` ranks after A C2's `+130`); `AWindowOverlappedByARunningPeer_IsPartialPeerRunning` and `ASettledOrAnotherCallersPeer_IsNotPeerRunning` (G3); `TiesBreakByGateIdOrdinal` | ownership labels wrong / duplicated |
| `NoMessageIsOwnedTwiceTests` | `[Theory]` over every scenario (the `PeersUnknown` variant of case 7 excluded by design — G6) through `AssignEvery`, which covers the SETTLED rounds of the scenario's caller: `GlobalUniqueness_HoldsOverEveryGateAndPeer` (identity 2); `TokensAreTheSumOfOwnedMessagesOnce` (*restated 2026-10-16, G7*: every message id placed in some assignment is owned exactly once, or owned by none and `wrote elsewhere` in every assignment that places it; Σ owned tokens = Σ tokens of the ids owned somewhere — cx `+10` of case 1 counted once); `AssignEverySkipsRunningRoundsAndOtherCallers` (case 8b's two variants produce A's assignments only) | duplicates found |
| `ABackgroundAgentStraddlesARoundTests` | case 2: `ItsMessagesSplitByInstant`; `TheReviewTimeWorkIsInRoundNsWindow_NotInNone`; `ItLaunchesOnce_InTheWindowOfItsFirstLine`; `TheResumedAgentLaunchesNothingAgain` (case 4); `AResumedAgentIsAssociatedPerWindow` (case 4: here in C1, same-session in C3) | launches 2 / message count 1 |
| `AModelSwitchKeepsEachMessagesModelTests` | case 3: three placements, models verbatim, one agent | models blank |
| `AMissingChildMakesItsWindowPartialTests` | case 5: `TheWindowHoldingTheCallIsPartialNamingEach`; `TheOtherWindowStaysObserved`; `TheChildHasNoRowNoLaunchAndNoTokens`; `TheParentsOwnAssignmentIsUnchanged`; `AMetaWithoutATranscript_IsOneUnobservedLaunchNotTwo` (G4: case 5 yields exactly two unobserved items and two reasons, the inert agent adds none); `AnE3ReasonWithoutAnInstant_MarksEveryWindow` (G5: `.Reason("read budget")` → both windows `partial: read budget`) | state `observed` / three items |
| `BeforeOpenWorkBelongsToTheFirstRoundOnlyTests` | case 7: `WorkBeforeOpen_IsTheBeforeOpenWindowOfRoundOne`; `RoundTwo_HasNoBeforeOpenWindow`; `ASecondGateOnTheSameRepo_StartsAtThePriorEnd`; `ThePriorEndIsAReadBoundNotAnOwnershipRule` (owned set equal with `.WithoutPriorEnd()`); added by §S2.2b: `APriorEndStopsAtTheFirstUnsettledPeerRound` (case 8c: `PriorEnd` null, `+30` owned by G either way, `partial: peer running`) and `ASettledPrefixEndsBeforeTheRunningRound` (case 8d: `PriorEnd = +60`, owned sets equal); `PeersUnknown_IsPartialPriorWindowsUnknown` | before-open absent / owned sets differ silently |
| `TheCallerOfTheRoundIsTheCallerReadTests` | `ACallerChange_IsPartialCallerChanged`; `AnEmptyPriorCaller_IsPartialPriorCallerUnknown`; `AnObservationOfAnotherCaller_IsAContractViolation` | state `observed` / no exception |
| `TwoRepositoriesOneAgentTests` | decision 4's scenario: `ItsMessagesAreAmbiguousInTheFirstCompletedWindow`; `TheOtherGateSeesThemClaimedByPeer`; `BothGatesEmitItsAgentAssociationAsAmbiguousTwoRepositories` (the agent COUNT is S3's — G10 moved `BothGatesCountOneAmbiguousAgent` there); `BothGatesKeepItsWriteCallsOnAnAgentAssociation` | tokens in both |
| `AssignIsStableUnderReplayTests` | case 6: `TwiceIsEqualByValueAndText`; `AnExtendedJournalChangesNothing`; `ALaterPeerRoundChangesNothing`; `ANewPeerGateChangesNothing`; `LinesAtOrAfterCompletionChangeNothing`; `LinesBetweenStartAndCompletionMatterOnlyThroughAPeerThatEndsThere` (the two-step fact of case 6); added by §S2.2b (G3): `AWriteAnsweredAfterTheCompletion_IsNotEvidence_LiveOrBackfilled` (case 6 (a): cx `+75` reads `ambiguous · no evidence` in A C2 — the window's only destination is `y`, cy's write — not `wrote here`; no `WritePlacement`; live input ≡ backfill input); `APeersLadderSeesOnlyWritesAnsweredBeforeThePeerCompleted` (case 6 (b)/(c)); `APermutedInputGivesTheSameOutput` | inequality / ordering differs |
| `ReadFromIsTheEarliestLineAssignNeedsTests` | `ANonFirstRound_ReadsFromThePreviousRecordedStart`; `AClaimantStartingEarlier_LowersIt`; `AFirstRoundWithNoPriorEnd_ReadsFromTheFirstLine` (null); `DroppingEveryLineBeforeReadFrom_ChangesNothing` | wrong bound |
| `EvidenceNotInferenceTests` | `AnAgentTypeNamedCoder_AssociatesNothing`; `ALaunchInsideTheWindow_AssociatesNothing`; `AnAgentWhoseOnlyWritesAreOutside_HasNoEvidence`; `NoInputMemberIsACwd` (reflection over the S1 records: no property named `Cwd`) | association `wrote here`. `NoInputMemberIsACwd` and `ABashCallIsNotAWrite` (above) are POSITIVES: a shell passes them, and their break-its — §S2.11's `Cwd` plant and the `ABashCallIsNotAWrite` plant — are what prove them (*G12d*) |

### S2.11 Break-its — planted, watched red, restored

| Plant (exact mutation) | Red test, with its symptom |
|---|---|
| In `Windows.Of`, `From = r_{k-1}.CompletedUtc` (`RoundEngine.cs:900`'s rule; *was cited `:895`, G12e*) | `TheReviewTimeWorkIsInRoundNsWindow_NotInNone`: bg `+65`, `+70` in no window — local completeness also red |
| In `Windows.Of`, `Completed = r_k.CompletedUtc` regardless of `Status` (*G1*) | `ARunningPeerWithAStamp_RanksLast`: B C1's `+105` ranks before A C2's `+130` and claims s2 `+80` |
| `PriorEnd` = max start over every recorded round (the old rule; *G2*) | `APriorEndStopsAtTheFirstUnsettledPeerRound`: `ReadFrom = +60` where null is expected and `+30` is owned by nobody while `.WithoutPriorEnd()` owns it |
| Drop `AnsweredAt < horizon(w)` from the `WriteRung` (*G3*) | `AWriteAnsweredAfterTheCompletion_IsNotEvidence_LiveOrBackfilled`: cx `+75` reads `wrote here` on the backfill input and `ambiguous · no evidence` on the live one (*was `same-session`, corrected with case 6 (a), 2026-10-19*) — live ≠ backfill |
| Drop the `peer running` branch from *States* (*G3*) | `AWindowOverlappedByARunningPeer_IsPartialPeerRunning`: `observed` |
| Make a `TranscriptPresent = false` agent an unobserved item too (*G4*, the text before §S2.2b) | `AMetaWithoutATranscript_IsOneUnobservedLaunchNotTwo`: three items and a repeated reason on case 5 |
| Admit a shell write (`Bash` with a path-shaped argument) as an `ObservedWrite` in the builder | `ABashCallIsNotAWrite`: the input's write set is not empty (*G12d: the plant that proves the positive*) |
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
two-repositories row (§S2.5), S4 about `OwnerGate`; E5.S4 is told §S2.8 (item 5 included); the parent records in §16 that
decision 3 was replaced (completion rank) and decision 7 refined and, on 2026-10-16, narrowed and then made literal
(D6's note: "placement, ownership and association never change after completion — a write is evidence only when its
result was written before the round completed, live and backfilled alike; a straddling message's tokens are the one
stated limitation; a write's instant is its message's first line"), and that D24 ranks SETTLED rounds only and a
provisional ownership is `partial: peer running`, repaired by E7 (G1–G3); `CoaiMcp.Tests.exe` green.

**Unsettled, by design:** the frequency of the clock race of decision 10 (two completions in one instant) is E1's to
measure if it ever shows; E5 chooses whether a peer timeline is read from the session file or the database for a
`running` round — S2 ranks a not-settled round last either way (G1), from the status, never from the stamp.

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
"delete-then-insert per round" is `RecordReviewers`, `RoundsDb.cs:928-954` — E5's, cited so the grain here fits it.

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

public sealed record WindowAuthors(                                 // 2026-10-16, Fable (§S2.2b G9): one per window of the round, empty windows included
    string Window,              // "before-open" | "round"
    string State, ImmutableArray<string> Reasons,                   // this window's own state (§S2.3 States)
    ActivityTotals Ambiguous, ActivityTotals Elsewhere, ActivityTotals ClaimedByPeer, int UnobservedLaunches);

public sealed record RoundAuthors(
    string Stage, int Number,   // added by the 2026-10-16 plan gate: S4's NotSeen and FixersOf need them for a round with no rows; announced to E5
    string CallerSessionId, string State, ImmutableArray<string> Reasons,   // the roll-up: the WORST window state; reasons distinct, in window order
    ImmutableArray<AuthorRow> Rows,
    ActivityTotals Ambiguous, ActivityTotals Elsewhere, ActivityTotals ClaimedByPeer, int UnobservedLaunches,   // the roll-up: sums — except UnobservedLaunches, which counts each launch ONCE (one with no instant marks every window but is one item; §12 item 10); Unknown (and UnobservedLaunches = -1, M1) when any window is unobserved
    ImmutableArray<WindowAuthors> Windows,                            // G9: the per-window state and totals E6 needs when it leaves before-open out
    ImmutableArray<string> PeerGates,                                 // G3 (iii): carried from WindowAssignment; ids only
    ImmutableArray<CheckoutWrites> WritesOutsideTheGateCheckout);   // gate finding 0, below

public sealed record CheckoutWrites(string Root, int Calls);   // in memory only — E5 stores counts, never the root
```

*(2026-10-16, Fable — E5 §8 items 1 and 6.)* **`AuthorRow`, `ActivityTotals` and — since 2026-10-17 (L1) — `WindowAuthors`
are final**: E5's `AuthorsSnapshot` persists them as they are, so a member change in any of the three is an E4 document
edit announced to E5 before E5.S1 is coded. `RoundAuthors` was not declared final, and §S2.2b adds two members to it — `Windows` (G9) and `PeerGates` (G3) — which
E5's snapshot must carry (≈ +300 B a round for two windows; the columns of step 24 are unchanged); this is announced in
§11's cross-epic list.
`Reasons` carries no counts and is joined by E5 with `", "`; S3 adds the fact `NoReasonContainsTheJoiner` over
`AttributionReasons`, every `const` of E3's `ObservationReasons` (reflection, as E3.S5 does) and
`ObservationReasons.NoReaderFor(v)` for every `CallerDeclaration` vendor.

`WritesOutsideTheGateCheckout` (plan gate finding 0, parent §7): for every owned `wrote here` write placement whose
checkout root is NOT `Gate.RepoPath` itself (a submodule or a nested worktree under it), the root and its call count,
ordered by root, ordinal. "Itself" is `Repositories.Normalise` on both sides and `OrdinalIgnoreCase` — E3's roots are
lower-cased and `repo_path` is as typed, so an ordinal `!=` would list every write into the gate checkout (plan gate
2026-10-16; the fact below has a lower-cased root of the gate checkout). Association is unchanged by it — those writes are `wrote here` — but their lines are not in
the gate checkout's diff. E5.S4 turns it into `submodule_writes` and `uncovered_writes`; the root never leaves memory.

`ClaimedByPeer` totals the items E4.S2 labels `claimed by peer` — owned by an earlier-completed window of a peer gate
session; like `Elsewhere`, they make no row and no contributor here.

**Grain.** One row per (window, agent, requested, model, effort, association) — `requested` added by the 2026-10-16
risk consultation: two requests answered by one model are two rows, so the request is never one placement's value
chosen for all. Per AGENT, not per family, on purpose: S4
deduplicates contributors by agent, and the card's "3 Sonnet coders" counts distinct agents — a straddling
coder writes in a window where it launched 0, so `launches` cannot stand in for "agents" (§7, defect 8).
`Turns` = distinct `message.id`s on the row. `Launches` = 1 on the row holding the agent's first message in the
window of its `FirstLineAt`, else 0; an agent launched in a window where it sent no assistant message gets a
row with zero turns and tokens and `Model = "unknown"`, so its launch is not lost. `WriteCalls` = the agent's
writes into THIS repository inside the window. Ambiguous and elsewhere activity get NO rows — only totals
(§6: "no row") — with ONE exception, E4.S2 decision 4: an agent whose association is `ambiguous` with rung `two
repositories` gets a `wrote here` row built from its `AgentAssociation` and the window's `Writes`: `WriteCalls` = its
writes into this repository, `Turns`, `Launches` and every token term ZERO (its messages are in the ambiguous totals or
claimed by a peer), and `Model = Requested = Effort = "unknown"` (*2026-10-16, G10*: the row carries authorship
evidence, not a model claim; the agent's models are on its placements, which the ambiguous totals do not keep). Rows are
ordered by (window, stage, number, who, agent id, requested, model, effort, association),
ordinal — equal inputs give equal outputs, byte for byte.

**Agent counts** (*2026-10-16, G10*). Turns, launches and the token terms partition the window's items across rows and the
three totals; the `Agents` counts do NOT partition — each total counts the distinct agents of ITS items, and one agent can
be in two counts. `Ambiguous.Agents` = distinct agents of owned `ambiguous` placements and launches ∪ every agent whose
`AgentAssociation` in the window is `ambiguous · two repositories` (decision 4: such an agent is ambiguous in every gate
that saw two repositories, whatever owns its messages — so gate B of §S2.6 *Two repositories, one agent* counts 1 agent
with 0 tokens); `Elsewhere.Agents` = distinct agents of `wrote elsewhere` items; `ClaimedByPeer.Agents` = distinct agents
of `claimed by peer` items. **`Agents` is counted per WINDOW; the round total is the sum over the windows** (an agent active
in two windows counts in both). Distinct agents across windows are S4's job (*Fable 2026-10-19; `RoundAuthorsBuilder.Total`*).

**`Model` and `Requested` on a row** (added 2026-10-16, §2 and RESULTS Q2). A row's `Model` is the placements'
`ObservedMessage.Model`, the model that answered. Its `Requested` is the placements' `Requested`, which for a meta with
no alias is E3's fallback (the line's `requestedModel`). The two `"unknown"` rows are the launch-only row above and the
two-repositories row (G10). Neither
value is ever taken from `ObservedAgent.Requested`. So a session that fell back from the model it asked for shows the
model that answered, and keeps the request beside it, for E6 to show where they differ. An agent whose only assistant line
is synthetic (E3 S4-11; fixture `c08`) has no message: if it was launched in the window, it is a launch-only row when it
is owned with a row association, and otherwise it is a launch in the ambiguous totals.

**State** — per WINDOW first (`WindowAuthors`, *G9*), then the round's roll-up: the WORST window state (`observed <
partial < unobserved`, *G12a*), reasons distinct in window order, totals summed and `Unknown` when any window is
`unobserved`.

| Observation / window | `State` | `Reasons` | Totals |
|---|---|---|---|
| E3's state `unobserved: <why>` | `unobserved` on every window | `[why]` | no rows; `Ambiguous`, `Elsewhere` and `ClaimedByPeer` = `ActivityTotals.Unknown`, `UnobservedLaunches = -1` (M1, 2026-10-17: a count nothing measured is unknown, never 0), `WritesOutsideTheGateCheckout = []` — E5 writes `-1` for the two write counts it derives from it |
| an unobserved launch with an instant inside the window (a missing child — `agent call without transcript`, `meta without transcript`; *G4/G5*: these are the only reasons that carry an instant) | `partial` | each, in order | as counted — a floor |
| every E3 reason (a torn MIDDLE line, a read budget, an inconsistent transcript, …; a torn LAST line is not a reason at all, parent §16 D22) — none carries an instant (*G5*) | `partial` | for every window | as counted |
| a not-settled same-caller peer round whose window overlaps this one (*G3*) | `partial` | `peer running` | as counted — provisional until E7 recomputes |
| S2's window-level unobserved (no round start) | `unobserved` | `no round start` | `Unknown` and `UnobservedLaunches = -1` for that window; both windows of a first round are blind together (open and start are their shared edges), and a blind round has no counted window; the round roll-up is `Unknown`/`-1` (M1) (*corrected 2026-10-19, Fable*) |
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
| `AWriteIntoANestedCheckoutIsHereButOutsideTheGateCheckoutTests` | a write into a submodule root and one into a nested worktree under `Gate.RepoPath`: both `wrote here` on the agent's row, both listed in `WritesOutsideTheGateCheckout` with their call counts; a write into `Gate.RepoPath` itself is not, also when its root arrives lower-cased (`d:/x` against `D:/x`) |
| `AmbiguousAndElsewhereActivityHasNoRowsTests` | they appear only in the totals; `Agents` counts distinct agents; `claimed by peer` items only in `ClaimedByPeer` |
| `ATwoRepositoryAgentKeepsItsWritesAndAddsNoTokenTests` | the agent of E4.S2 decision 4 has one `wrote here` row with its write calls into this repository and zero turns, launches and tokens, `Model = Requested = Effort = "unknown"`; its messages are counted once, in `Ambiguous` or `ClaimedByPeer`; `BothGatesCountOneAmbiguousAgent` (moved from S2 by G10: A and B each count 1 in `Ambiguous.Agents`, B also 1 in `ClaimedByPeer.Agents`) |
| `EveryWindowAccountsForEveryMessageTests` | local completeness over ROWS: Σ row turns + ambiguous turns + elsewhere turns + claimed-by-peer turns = the window's distinct message ids, and the same per token term — over every S2 acceptance scenario, through one `[Theory]` fed by the builder; `TheAgentCountsAreNotAPartition` (G10: a two-repositories agent claimed by a peer is in two agent counts while its turns are in one) |
| `TheAuthorsStateSaysWhatWasNotSeenTests` | each row of the state table, including `AnObservedSessionWithNoSubagents_IsObservedZero`, `AnUnobservedRound_NeverReadsAsZero`, `AReadBudgetMarksEveryWindow` (G5) and `APeerRunningWindow_IsPartialPeerRunning` (G3) |
| `EveryWindowKeepsItsOwnStateAndTotalsTests` (G9) | `Windows` has one entry per window of the round, the empty before-open of a first round included; `BeforeOpenPartialRoundObserved_AndTheReverse_AreDifferent` (the round roll-up is `partial` in both, the per-window states differ); `ABlindFirstRound_IsUnknownInEveryWindowAndInTheRollUp` (both windows of a first round are blind together, so every window and the round roll-up are `unobserved`/`Unknown`; *was `ABlindBeforeOpenLeavesTheRoundWindowCounted`, a case that cannot arise — Fable 2026-10-19, §12 deviation 2*); Σ per-window totals = the round totals when no window is unobserved |
| `NoEffortIsUnknownNotHighTests` | absent effort → `unknown` on the row |
| `TheModelOnARowIsTheOneThatAnsweredTests` (added 2026-10-16) | `ASessionAnsweredByAFallbackModel_CountsTheAnsweringModel` (main requests `claude-opus-5-5[1m]`, its messages carry `claude-opus-4-8` → the row's `Model` is `claude-opus-4-8`, `Requested` is `claude-opus-5-5[1m]`); `AMetaWithNoAlias_NeverBlanksTheModel` (an agent whose `ObservedAgent.Requested` is `""` → `Model` from its messages, `Requested` from theirs); `ASwitchMidWindowAfterAFallback_IsTwoRows` |
| `TheSameRoundBuiltTwiceIsIdenticalTests` | `Build` twice → equal by value AND equal `ToString()`; the input's message order permuted → identical output |
| `TwoRequestsAnsweredByOneModelAreTwoRowsTests` (risk consultation 2026-10-16) | one agent, two messages on `claude-sonnet-5-5`, requested `sonnet` and `claude-sonnet-5-5` → two rows, each with its own `Requested` |
| `AnOutputRecordHasOnlyItsNamedStringMembersTests` (plan gate 2026-10-16) | reflection over `AuthorRow`, `ActivityTotals`, `RoundAuthors`, `CheckoutWrites`, and S4's `RoundRef`, `Contributor`, `NotSeen`, `ContributorSet`: the set of `string` and `ImmutableArray<string>` members EQUALS a named allowlist (`AuthorRow`: `Window`, `Stage`, `Who`, `AgentType`, `Requested`, `Model`, `Effort`, `Association`; `CheckoutWrites.Root` is the one path, in memory only). A new string member fails by name before any data can carry text; S5's sentinel test is the data-side half |

**Break-its.** (a) Count `Launches` on every row of the agent → `ALaunchIsCountedOnceAndNeverLostTests` red.
(b) Emit a row for ambiguous activity → `AmbiguousAndElsewhereActivityHasNoRowsTests` red, and completeness
double-counts. (c) Default effort to `"high"` → `NoEffortIsUnknownNotHighTests` red. (d) Return zero totals for
an unobserved round → `AnUnobservedRound_NeverReadsAsZero` red. (e) Drop the final `OrderBy` → the permuted-input
fact red. (f) Take a row's `Model` from `ObservedAgent.Requested` when it is non-empty →
`ASessionAnsweredByAFallbackModel_CountsTheAnsweringModel` red for subagents (main's twin: from the message's
`Requested`). (g) Drop `requested` from the grain → `TwoRequestsAnsweredByOneModelAreTwoRowsTests` red: one row. (h) Add
`string Description` to `AuthorRow` → `AnOutputRecordHasOnlyItsNamedStringMembersTests` red, naming the member. (i) Compare
the gate root ordinally in `WritesOutsideTheGateCheckout` → `AWriteIntoANestedCheckout…` red: the lower-cased gate root is
listed. (j) Collapse `Windows` to the round state (store the roll-up in every entry) →
`BeforeOpenPartialRoundObserved_AndTheReverse_AreDifferent` red (G9). (k) Count `Ambiguous.Agents` from owned items only
→ `BothGatesCountOneAmbiguousAgent` red: B counts 0 (G10).

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
author is a code author — E6's cohorts choose (§7, defect 9). Consumer: the report's *coder mix* (§8, E6) only. The
card line (E5.S6, beside `calledBy`, `src_vs_code/src/rounds.ts:168`) shows THIS round's stored rows and never the
contributor union — computing it in TypeScript would be a second implementation of this story (*2026-10-16, E5 §8.4;
the card was listed as a consumer before*).

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
  round contributes and is listed too (its contributors are a floor). `NotSeen.Why` reads `<state>: <reasons>`, the reasons
  joined by `", "` (*Fable 2026-10-19; `Contributors.cs` `Why`*).
- A contributor's `Models`, `Requested` and `Efforts` may contain `unknown`: the launch-only row and the two-repositories
  row carry it (*Fable 2026-10-19*).
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

**Scope (rewritten 2026-10-16 against the fixtures E1 shipped in #828).** Tests only, in `src_mcp/tests/`. The inputs are
the twenty folders `src_mcp/tests/fixtures/caller-transcripts/c01`–`c20`. Their case names are in `manifest.json`, the
folders are short because no tracked path may be longer than 160 characters, they are copied to the output by
`CoaiMcp.Tests.csproj:36`, and they are kept text-free by `ACallerTranscriptFixtureCarriesNoTextTests`. Each folder has one
`rounds.json` in a CLOSED schema: `callerSessionId`; `gateSessions[]` with `id`, `repoPath` (a fixture root), `openedUtc`
and `rounds[]` with `number`, `startedUtc`, `completedUtc`, `status` and `headSha`. There is no stage, no verdict and no
caller per round. E1 wrote no expectation files: the manifest holds counts only. So the cells below are a hand-read of
the fixtures (2026-10-16), and they go into the test as data (measurement.md rule 2: the hand-read IS the check). The
reader is E3's `CallerTranscript`, which is not on main yet. The materialiser is E3.S4's
`CallerTranscriptFixture.Materialise`, with the two requirements of §8.3 item 6 (closed by E3.S4's 2026-10-16 amendment).

**The ONE mapping is built here** (B1, 2026-10-17). New `src_mcp/src/Store/AttributionMapping.cs`, a pure `internal static`
class beside E3's `ObservedSession.cs` (it is the only place that sees both E3's `src/Store` records and E4's core records;
E3 cannot build it — E4's types do not exist when E3 ships — and E5 is built after E4, so E4.S5, which already depends on
E3.S4, builds it and is its first caller; E5.S4 step 5 and E7.S2 are the other two, and neither writes a mapping of its own):

- `Observation(ObservedSession s) → CallerObservation`: `FirstLineAt = LaunchedUtc ?? CalledUtc` (null only for a
  `TranscriptPresent = false` agent, G4); `TranscriptPresent` as is; `UnobservedLaunches` one for one; `ObservedWrite.At` =
  E3's `Utc`, `AnsweredAt` = E3's `AnsweredUtc` (G3), `Kind`/`Where` = `Target.Kind`/`Target.Where`; `Effort` and
  `Requested`/`Model` as is; `TokenTerms` term by term; `State.Kind` as E3's word; every `ObservationReason` → `StateReason(Code,
  At: null)` EXCEPT `agent call without transcript` and `meta without transcript`, which are never mapped as reasons (G5,
  L8 — their instants ride on the launches). Every `DateTime` through `UtcInstant.From`.
- `Timeline(string gateSessionId, string repoPath, DateTime openedUtc, IReadOnlyList<RoundRecord> rounds, Func<RoundRecord,
  string> callerOf) → GateTimeline`: journal order kept; `StartedUtc`/`OpenedUtc` through `UtcInstant.From` (default → null);
  `CompletedUtc` = `UtcInstant.From(CompletedUtc)` ONLY when `Status == RoundRecord.Done`, else null (G1); `Stage`, `Number`,
  `Status`, `Verdict` verbatim; `CallerSessionId = callerOf(round)` — the caller rule is the CALLER's (E5 §S2.8 item 6 order;
  the fixture loader's constant id here). The ROUNDS list is the caller's too: E5 passes `[.. session.Rounds, record]` (M4).
- `Universe(GateTimeline gate, ImmutableArray<GateTimeline> peers, CallerObservation o) → ImmutableArray<string>`: decision 6
  — the gate's and every peer's `RepoPath` ∪ every `Checkout` root in `o.Writes`, in the spelling each arrives in (no
  WSL ↔ Windows translation, L6; `RepositoryOf` folds case and separators), distinct under `Normalise` + `OrdinalIgnoreCase`,
  ordinal order.

Nothing in it decides anything, and `TheMappingDecidesNothingTests` (one class, in `src_mcp/tests/`) proves each rule on
built `ObservedSession`s and `RoundRecord`s: `ARunningRoundsStamp_IsNotItsCompletion` (a `running` record with a stamp →
`CompletedUtc == null`; `done` keeps it); `TheTwoTranscriptMissingCodes_AreNeverStateReasons` (L8: an `ObservedSession` whose
`State.Reasons` holds both codes and `read budget` → one `StateReason("read budget", null)`); `EveryOtherReasonArrivesWithoutAnInstant`;
`AMetaWithoutAFileAndNoCall_HasNoFirstLine` and `APresentAgent_HasOne`; `AnsweredAtIsTheResultLinesInstant`;
`TheUniverseKeepsEachSpellingAndDropsNoRoot` (L6); `TheJournalOrderIsKept`. Break-its: map `CompletedUtc` without the status
check → the first red; map every reason → the second red (two extra `StateReason`s). The S5 `[Theory]` rows below run
through `Observation` + `Timeline`, so a field spelling E3 changes fails here by name — the reason S5 exists.

**Rules every S5 test follows.**

- **The clock is a rank clock** (§2). Assert order relations and the cells below. Never assert a duration, a date or a
  token magnitude. The reader's `now` (E3 S4-9's day-gate span) is a fixed instant on `2000-01-01`, never
  `TimeProvider.System`: a real clock would make the gate span 26 years of days.
- **`rounds.json` → `GateTimeline`** goes through a test-side loader, `FixtureRounds`, because `rounds.json` is a fixture
  format and not E5's session file. Every round gets `Stage = "CodeReview"` (E1's export holds code rounds only) and
  `Verdict = ""`. `Status` and `CompletedUtc` are copied as given. `CallerSessionId` is the file's `callerSessionId` on
  every round. Every other gate session of the file is a peer, and `PeersKnown = true` — the file is taken as the
  caller's complete set of gate sessions, and this assumption is stated in the class summary. The loader decides nothing
  else. The universe is E5's rule (S2 decision 6) over the materialised roots.
- **The positive first** (testing.md, *A fixture the code REJECTS proves nothing*). Before any attribution assertion,
  assert that E3's reader returned the expected state and a non-zero message count (except `c10`), and that every
  answered write of the case whose fixture path lies under `repo-a` or `repo-b` resolves to a `Checkout` on THIS host.
  CI runs this suite on Linux, macOS and Windows (`.github/workflows/ci.yml:58,282,317`). A host that reads a re-pointed
  path differently must fail by case name, never as a wrong cell.
- **One test-side extension, labelled.** `c01` and `c14` have activity after the start of their only round. To show the
  NEXT window on real data, the test adds a round 2 in memory: its start is one synthetic second after the case's last
  instant, and its completion is one second after that. No fixture file changes, and the guard is not touched.
- Map the reader's `ObservedSession` to `AttributionInput` through `AttributionMapping` (§0; built in THIS story, B1),
  never through a mapping written in the test. `FixtureRounds` builds `RoundRecord`s from `rounds.json` and hands them to
  `AttributionMapping.Timeline`, so the production rule (`CompletedUtc` only on `done`) runs on the data too.

**Which test uses which case** (`H` = `AttributionHoldsOnTheRecordedSessionsTests`, one `[Theory]` row per case; `I` =
the identities; `R` = replay; every case also feeds `NoSentinelLeavesAttributionTests` and the order fact). "Ambiguous"
below means `ambiguous · no evidence` unless the row says otherwise.

| Case | Name (manifest) | Test | Cells (hand-read) | What it cannot show — and where that is tested |
|---|---|---|---|---|
| `c01` | background-agent-across-round-start | H, I, R | Round 1's window holds main's one message and two of the background agent's messages, and 1 launch. No write: all ambiguous (agents 2, launches 1, turns 3). One message has lines on both sides of the start: it is placed by its FIRST line, and its tokens are the maximum over every line, after the start included (D8). With the extension, round 2 holds the agent's two later messages and 0 launches | a write (the agent only reads). Builder: S2 case 2 |
| `c02`, `c03` | message-as-several-lines, message-last-line-below-max | H, I | Round 1 holds main's one message only. The agent's first line comes after the start: no launch and no message of the agent in round 1 (S2 decision 8: a launch requested before the start whose first line follows it belongs to the next window) | the token rule itself is E3's; its message lies after round 1's start |
| `c04` | meta-without-model | H, I | Round 1 holds main 1 + the agent 1 message, 1 launch, all ambiguous. The agent's `Requested` is the line's (the meta has no alias) and its `Model` is `claude-sonnet-5-5`. The window state is `observed`: a meta without `model` is E3's `Diagnostics.MetasWithoutModel` count, never a reason (E3 S4-17; §S2.2a F4) | a row: there is no write, so the model shows only on placements |
| `c05` | missing-child-transcripts | H, I | Round 1 is `partial: agent call without transcript, meta without transcript`. There are two unobserved launches at main's message instant (all three `Agent` calls sit in one message; E3 S4-8), 1 launch (the agent whose file exists), and no tokens and no launch placement for the missing two | — |
| `c06` | model-switch-in-window | H, I | Main has two messages on `claude-opus-5-5`, then `claude-fable-5-1`: two placements, each carrying its own model. The only write goes into `scratch` → outside → ambiguous, and no row | the model switch on a ROW: the ambiguous totals carry no model (§8.3 item 7). Builder: S2 case 3, S3 `ARowIsOne…`. This is also the shape a `model_refusal_fallback` takes |
| `c07` | nested-agents | H, I | Main's message comes before open → `before-open`. Round 1: 2 launches (= the manifest's `agentFiles`, not its 3 `agentToolUses`). The depth-2 agent's parent is the depth-1 agent, not main. The depth-2 meta has no alias → its `Requested` is from its lines, its `Model` is `claude-sonnet-5-5`. No write → no `child of`; all ambiguous | `child of` on data: no fixture has a subagent parent that writes. Builder: S2 `OnlyASubagentParentPropagatesTests` |
| `c08` | no-effort-lines | H, I | The agent's only assistant line is synthetic → no message (E3 S4-11). Its first line follows the start → no launch in round 1. Round 1 = main's 1 message, ambiguous | `effort = unknown`: no real line lacks effort (the manifest says so). Builder: S3 `NoEffortIsUnknownNotHighTests` |
| `c09` | no-evidence-two-repositories | H, I | One main message, whose lines run past both starts, is inside gate A's AND gate B's round windows. There is no write, so it is ambiguous in both. A completes first → A owns it (ambiguous turns 1), and B labels it `claimed by peer (A)`. Global uniqueness: it is owned once | — |
| `c10` | persistence-off | H | `unobserved: transcript not found`: no placements, no rows, `ActivityTotals.Unknown`, contributors empty, the round in `NotSeen` | — |
| `c11` | refused-agent-call | H, I | The refused call (error result, no meta) is no launch and no unobserved launch. Round 1 is `observed`, with 0 launches | — |
| `c12` | registry-and-variable-disagree | H | Whatever state E3.S2 resolves, carried through unchanged | E4 has no cell of its own here |
| `c13` | replay-same-round-twice | H, R | The journal lists round 1 twice: `Validate()` names the duplicate (S1). Round 1 attributed twice from a journal with ONE copy → equal by value and by text. `AssignEvery` owns main's one message once | — |
| `c14` | resumed-agent | H, I, R | Round 1: the agent `wrote here` (1 write call into `repo-a`, 11 turns before the start, 1 launch), and main `same-session` (destinations {`repo-a`}). Lines after the start are not placed. With the extension, round 2 holds the agent's later messages with 0 launches, ambiguous (no write in that window): one launch over both windows | the same agent `wrote here` → `same-session` across windows. Builder: S2 case 4 |
| `c15` | second-gate-session-same-caller | H, I | Gate 2's `before-open` starts at `PriorEnd` = gate 1's round start and holds nothing. Main's one message is in gate 2's round window, ambiguous (its write goes into `scratch`). Gate 1 owns nothing. The owned set is equal with `.WithoutPriorEnd()` | — |
| `c16` | skipped-round-between-recorded | H, I | The journal is 1, 3 (round 2 is ABSENT): round 3's window = [round 1's start, round 3's start) and holds main's message | a SKIP record (`Verdict = skipped`): `rounds.json` has no verdict (§8.3 item 5). Builder: S2 `ASkippedRound_OpensNoWindow` |
| `c17` | two-repositories-one-session | H, I | *Re-read 2026-10-19 after E1's re-cut (#851, results kept), `RecordedCells.cs` `c17`.* Both writes are answered, so main wrote into `repo-a` AND `repo-b` in one window: `ambiguous · two repositories` in both gates. Gate A completes first and owns the 3 messages in its ambiguous totals (1 agent, 0 launches, 3 turns); gate B labels all 3 `claimed by peer` (claimed turns 3) and counts 1 ambiguous agent with 0 turns. Each gate keeps one `wrote here` row for main with 1 write call, 0 turns, 0 launches and `Model = Effort = unknown`. Each message is owned once | — |
| `c18` | work-before-open | H, I | The `before-open` window: main `wrote here` (1 write call, 1 turn). The round window is empty. Contributors are none with `withBeforeOpen: false` and main with `true` | — |
| `c19` | write-into-submodule-and-scratchpad | H, I | *Re-read 2026-10-19 after E1's re-cut (#851, results kept), `RecordedCells.cs` `c19`.* Both writes are answered. The submodule write resolves to a checkout under `repo-a`: main `wrote here` (1 turn, 1 write call), and the write is listed once in `WritesOutsideTheGateCheckout`. The scratchpad write is outside (D3) and is no evidence. Ambiguous totals are zero | — |
| `c20` | writes-only-elsewhere | H, I | The only write goes into `scratch` → outside (D3), NOT elsewhere. Main is ambiguous, and the `Elsewhere` totals are zero | The name says "elsewhere", but under D3 a scratchpad is outside. `wrote elsewhere` on data is `c17`'s gate B |

**No fixture shows these** (builder only, through S2's and S3's tests): `child of`; a skip record; an interrupted round;
a caller change; a blind round; `effort = unknown`; a model switch on a row; `claimed by peer` where the peer's ladder is
`wrote here`. (*2026-10-19: `ambiguous · two repositories` and a submodule write left this list — `c17` and `c19` show them
since E1's re-cut.*)

**The coder's brief (Sonnet).**

1. `FixtureRounds` (the loader above) and the `[Theory]` data: one row per case of the table, with its cells as data.
2. `AttributionHoldsOnTheRecordedSessionsTests`: the positive half, then `Assign` → `Build` → `Contributors.Of`, then the
   case's cells (rows, totals, state, contributors).
3. `TheRecordedSessionsKeepBothIdentitiesTests`: local completeness and global uniqueness (E4.S2 §S2.5, whose
   association list includes `claimed by peer`) over every case through the real reader. `c09`, `c15` and `c17` are the
   cases with more than one gate session.
4. `AttributionOfARecordedRoundIsStableUnderReplayTests`:
   - `c01` and `c14`: round 1 attributed from the transcript truncated at round 1's COMPLETION equals round 1 attributed
     from the full transcript — placements, owners, states and tokens — the live record against the backfill.
   - `c01` truncated at round 1's START: the placed message ids and their owners are equal, but the straddling message's
     tokens are LOWER. This fact pins D8 (tokens over every line of the id) beside D6 (placement by the first line); see
     §9 F1.
   - `c13`: the replay above.
5. `NoSentinelLeavesAttributionTests`: serialise every `RoundAuthors` and `ContributorSet` produced above (`ToString()`,
   and E5's JSON context if one exists by then), and assert that E1's sentinel `COAI-FIXTURE-SENTINEL` occurs nowhere.
   Add the positive half: the sentinel IS present in the fixture bytes the test read, so a fixture without sentinels
   cannot pass vacuously.
6. `AttributionDependsOnlyOnTheOrderOfInstantsTests`: every S2 builder scenario, with each instant replaced by its rank on
   the fixtures' synthetic clock (as E1's sanitiser does), gives the same placements, owners, states and rows, apart
   from the instants themselves. This is why rank-clock fixtures are valid inputs (§2).
7. A case whose path spelling the universe does not match (a WSL `/mnt/…` path against a Windows root) fails by case
   name. Do NOT add a translation; report it to the reviewer as a finding for E5's universe. (Every shipped fixture is
   `side: windows`; RESULTS *What E1 leaves open* says why no WSL session could be published.)

**Break-its.** (a) Swap two cases' cells in the theory data → those rows go red by case name. (b) Add a `Description`
member to `AuthorRow`, filled from the agent's meta → the negative half of the sentinel test goes red, with the positive
half green (proof that the sentinel was there to leak). (c) Make the replay compare tokens after truncating `c01` at the
START → red on the straddling message's tokens. Truncate before that message's first line → red on placements.
(d) Materialise without folding separators on a non-Windows host → the `Checkout` precondition goes red by case name, not
a cell. (e) Give `Window.Contains` a one-minute tolerance → the order fact goes red.

**Done when.** One theory row per fixture, all green. Both identities and the replay facts are green on the real
fixtures. The sentinel test is green with its positive half. The order fact is green. Every break-it was watched red.
`CoaiMcp.Tests.exe` is green on Windows and in CI on Linux and macOS. The epic's summary says that every cell is a
hand-read.

**Dependencies.** E4.S3 and E4.S4; E3.S2 + E3.S4 (the reader, its `ObservedSession` in `src/Store`, and its materialiser,
with §8.3 item 6) — the mapping to `AttributionInput` is THIS story's `AttributionMapping` (B1), so E4 reaches Done with
no E5 story built; E1.S5's fixtures (shipped, #828) — `c17` and `c19` give their full cells since E1's re-cut (#851, results
kept), which reached this branch through E3; §8.3 item 4 is closed.
**Model:** brief by opus (this document) · code: sonnet · review: opus.

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
- [ ] (added 2026-10-16) Every S5 cell is backed by its `c01`–`c20` case, and the cells are named as hand-reads; no test
      asserts a duration, a date or a token magnitude read from a fixture; the order fact is green.
- [x] (added 2026-10-16) Fable answered every item of §9 before S2's code, and each answer is in S2 or in the parent's §16.
      *(Done 2026-10-16: §S2.2a.)*
- [x] (added 2026-10-16) The risk consultation of §10 was taken, verified and closed (`close_consult`) before the epic's
      code round, and what it changed is recorded. *(Done 2026-10-16: §10's note, §11.)*
- [ ] (added 2026-10-17, B1) `src_mcp/src/Store/AttributionMapping.cs` exists, built by S5, with `TheMappingDecidesNothingTests`
      green; it is the one `ObservedSession → AttributionInput` mapping, and E5.S4 and E7.S2 call it.
- [x] (added 2026-10-17) Every point of the judge review of `7c3bf469` that touches E4 is resolved in §11 *Resolutions II*.
- [x] (added 2026-10-16, plan gate) Fable answered every item of §11 before S2's code. *(Done 2026-10-16: §S2.2b G1–G12;
      the cross-epic consequences are listed in §11 and are owed to E3, E5, E6, E7 and the parent before their code rounds.)*

## 7. Defects found in the parent plan while splitting (not fixed here)

> **Settled 2026-10-14** in the parent's §16: 1 → D4; 2 → D3; 3 and 5 → D5; 7 → D1; 9 → D2; 10 and 13 → D6; 11 → D20;
> 12 → D18; 4 (only subagent parents propagate), 6 (each round reads its own caller) and 8 (an opaque `agent` key on
> every `round_authors` row) accepted as resolved here — E4.S2's spec may still replace 4 and 6 with a reason.

1. **Writes have no instant.** §5 keeps writes "as a boolean per (agent, repository)", but §6 associates per
   agent IN A WINDOW, and step 24's `write_calls` (step 23 was taken by the plan-gate E2, 2026-10-16) is a count. E3 must expose each write's instant and path.
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
8. **No agent identity in step 24** (step 23 was taken by the plan-gate E2, 2026-10-16). Contributors as a union over stored rows need an agent key, and "3 Sonnet
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

## 8. Against E1's measurements and main (revised 2026-10-16)

### 8.1 Reference drift — the lines to read on main `59463d73`

The S2 text is Fable's. Fable's 2026-10-16 amendment corrected its `RoundEngine.cs` anchors in S2's scope and decision 3
(§S2.2a, E5 §8.7); the four S2 citations that still carried the old numbers — `SessionStore.cs:248` and `:594-597` in S2's
scope, `RoundEngine.cs:895` in case 2 and in §S2.11's first plant — were corrected in place by §S2.2b (G12e) the same day,
each with a "*was cited*" note, so this table is now the record and no text reads against it. Re-verified on
`origin/main` `ce9cd298` (release server 0.13.0, #808): `src_mcp` is byte-identical to `59463d73`, so every number below
holds there. The other stories are corrected in place.

| Cited (where) | On main | What is there |
|---|---|---|
| `RoundEngine.cs:483-491`, saved at `:586` (§0, S2 scope) | the `with` block `:485-496`; saved at `:591` | the record E5 adds the snapshot to, and its save |
| `RoundEngine.cs:607`, `Projection.Write` (S2.2 decision 3) | `:612` (`RecordRound` at `:614`; the block ends at `:644`); `:607` is now `audit.Closing` | the synchronous projection at completion — decision 3's premise holds |
| `RoundEngine.cs:893-902`, `:895`, `:896` (S2 scope, case 2, S2.11) | `WhatTheCallerWasDoing` `:898-907`; the previous round's `CompletedUtc` `:900`; floored at `OpenedUtc` `:901`; the transcripts folder `:902`; the slice `:906` | the `agent_log` window this epic does not reuse |
| `SessionStore.cs:248` (S2 scope) | `PersistedSession.OpenedUtc` `:278` (the record `:266`) | — |
| `SessionStore.cs:594-597` (S2 scope), `:578-597` (§7 item 7) | `SweepOrphanedRounds` `:608-631`; the rewrite to `interrupted` `:626` | the sweep also stamps `CompletedUtc` with the SWEEP's time; an interrupted round opens no window, so that stamp ranks nothing |
| `RoundSkips.cs:95-99`, "a repeated skip moves only its `CompletedUtc`" (S2 scope) | `:95-99`: it moves `CompletedUtc`, `Repeats` and `Sha` (`:97`); `StartedUtc` does not move | no effect on the windows (a skip opens none) — §9 F5 |
| `RoundsDb.cs:923-945` (S3) | `RecordReviewers` `:928-954` | corrected in S3 |
| `rounds.ts:156` (S4) | `calledBy` `:168` | corrected in S4 |
| unchanged | `AgentLog.cs:304-317` (`InRepo` 304-315, `Normalise` 317); `ArchitectureTests.cs:17-24`; `SessionStore.cs:78-92, 84, 88, 92, 104, 121, 193`; `SessionState.cs:264-267, 382-386`; `RoundNumber.cs:24-26`; `LiveRound.cs:279`; `RoundSkips.cs:102-110, 105`; `GateHistoryQuery.cs:77`; `ProductionSources.cs:38`; `CoaiMcp.Tests.csproj:36` | — |

E3 is not built: `CallerTranscript.cs`, `ObservedSession.cs` and `WriteEvidence.cs` are not on main, so §0's E3 contract
is E3's document, not code. Schema step 23 is the plan gate's (`Schema.cs:655`), and the authors schema is step 24, as
§7 items 1 and 8 already say.

### 8.2 What E1 measured, and what this epic does with it

| RESULTS | Measured | Here |
|---|---|---|
| Q2 | metas with `model`: 100 % only on 2.1.283–2.1.285 (Windows) and 2.1.300; otherwise 70–98 % on Windows and as low as 32 % on WSL. `effort` on ≥ 99.5 % of assistant lines | §2: counts are grouped on the model that answered. S3 has the rule and its test: an empty meta alias never blanks a model. Such a meta is `observed`, counted in E3's `Diagnostics.MetasWithoutModel` (E3 S4-17; §8.3 item 2, closed) |
| Q4 | one `message.id`'s lines span p50 1.7–1.9 s, p99 50–52 s, max 2.01 h; the instant used for attribution is the id's FIRST line | placement by the first line (E3 S4-8) agrees with the windows. But a placed message's tokens include its lines after the start (D8), and a write on a late line gets the first line's instant (§9 F1, F2). S5's replay facts are written for this |
| Q7 | Windows: 587 `Agent` tool uses against 778 metas, and the 191 extra are exactly the nested metas (depth 2–3). WSL: 844 against 857, 17 nested, 4 tool uses with no meta (refused launches) | launches come from `ObservedAgent`, one per `agentId`, depth 1 and nested alike — never from a count of `Agent` tool uses (S5 `c07`: 2 launches, 3 tool uses). Rule 5 climbs subagent parents recursively. S1 refuses a looping chain, and an empty parent propagates nothing. A refused launch (error result, no meta) reaches E4 as NOTHING: no launch, no unobserved launch, and the window stays `observed` (`c11`). An `Agent` call with no meta whose result is not an error is E3's `agent call without transcript` → `partial` (`c05`) |
| Q8 | every write path is absolute. Windows: backslashes 90 %, mixed separators, drive case as typed. WSL: `/` only. Writes under `cwd`: 0 % / 5 % | S1's normalisation rules; no `cwd` anywhere (S2.4's break-it keeps it so) |
| the sanitiser | instants → ranks on `2000-01-01`, ties kept, `rounds.json` on the same clock; usage counts → ranks per path | §2 *instants are an order*; S5's rules and its order fact |
| the three-round check | each of the six sampled windows held several models or several write roots | expect most activity to be `ambiguous` — §10's first question |

### 8.3 Edits other documents need (not made here — this task edits only this document)

> *2026-10-16, Fable:* items 2, 3 and 6 are **closed** by the E3.S4 amendment (`meta without model` is a count; `StraddleGuard`
> is 3 h on an exact byte gate; the materialiser puts `.git` on `repo-*`/`other`, never `scratch`, folds separators on a
> non-Windows host, and takes the submodule marker from a manifest `checkouts` list E1 owes). Item 4 is **owed by E1** as E3
> §8 item 1 (re-cut c12/c17/c19 with results kept; manifest `checkouts`). Items 1, 5 and 7 stand.
>
> *2026-10-19:* item 4 is **closed** — E1 re-cut `c12`, `c17` and `c19` with the results kept and the manifest `checkouts`
> lists (#851), and the re-cut reached this branch through E3; S5's `c17` and `c19` rows carry the full cells.
>
> *2026-10-16, Fable, §S2.2b:* the edits G1–G12 require of E3, E5, E6, E7 and the parent are listed ONCE, in §11's
> *Cross-epic consequences*, and are not repeated here.

1. **The parent**: the line references of §8.1, in its §6, §13, §16 (D1, D14, D17) and the E4/E5 scopes
   (`RoundEngine.cs:483-491`, `:586`, `:605-625` → the projection block `:612-644`, `:893-902`, `:897` → `:902`;
   `SessionStore.cs:248`, `:578-597`, `:586-600`; `RoundsDb.cs:923-945`; `rounds.ts:156`); and Fable's answers to §9,
   now in §S2.2a (and §S2.12's D6 note).
2. **E3 (S4's reasons) and the parent's §5**: `partial: meta without model`. Q2 shows that a meta without `model` is what
   a launch that names no model normally writes — on some versions, up to 68 % of metas. By E3's own principle ("a reason
   is recorded when something was LOST or a COUNTED VALUE is uncertain"), nothing is lost: the request was "no model", and
   the model that answered is on the lines. As written, the reason would mark most rounds `partial` and empty the word —
   the argument D22 made for torn tails. Proposal: make it a diagnostics count. E4 copies E3's reasons verbatim (S2.3
   *States*), so E4's states follow what E3 decides.
3. **E3 S4-9**: `StraddleGuard = 1 h` rests on "one message never spans an hour". Q4 measured 3 ids per side over 1 h,
   the longest 2.01 h. A message that starts before midnight and runs past a bound set early on the next day is then
   counted from a later line, at a wrong instant.
4. *(Closed 2026-10-19 — E1's re-cut, #851; the text below is the record of the request.)* **E1's fixtures `c17` and
   `c19`**: their write calls have no `tool_result`. In `c17` the `Edit` into `repo-b` is the
   file's last line, and `c19` has no result line at all. Under D23 (E3 S4-7) these are not writes, so `c17` does not show
   `ambiguous · two repositories`, and `c19` shows neither the submodule nor the scratchpad. E3's worked-example row for
   `write-into-submodule-and-scratchpad` expects writes that S4-7 drops. The fix: re-cut with the window past the results,
   or a `derived:` edit that adds them (E1.S3's tool), then the guard's pinned prose and counts.
5. **E1's `rounds.json`** has no `stage`, no `verdict` and no caller per round (a closed schema in the guard). So no data
   holds a skip record, a plan round, an interrupted round or a caller change; `c16` shows a numbering GAP, not a skip.
   Widening the schema is a privacy decision in the guard.
6. **E3.S4's materialiser** (`CallerTranscriptFixture.Materialise`) "creates one directory with a `.git` marker per
   fixture root". E4 needs a plan instead: a `.git` for `repo-a` and `repo-b`; NONE for `other` and `scratch` (else the
   scratchpad is a checkout and D3's `outside` is never exercised); and for `c19`, a `.git` FILE at the submodule root the
   write was moved into. Also, on a non-Windows host the materialiser must fold the separators after the re-pointed
   prefix: `D:\fixture\repo-a\p1\p2.md`, re-pointed with its backslashes kept, is ONE file name on Linux, so its directory
   is the temp root and the write reads `outside`. CI runs the suite on all three systems.
7. **The parent's §6 acceptance case "a model switch inside one window"**: on `c06` the switch is in the ambiguous
   totals (main writes only into `scratch`), and those carry no model, so the stored record cannot show it there. S2 case
   3 and S3's rows show it (builder), and so do the placements on `c06`.

## 9. For Fable to confirm

These are the places where E1's measurements or main contradicted the S2 spec, or where it was silent on something the
shipped fixtures contain. Each item says what, why, and what changes if the answer is yes.

> **Decided 2026-10-16 by Fable** — §S2.2a holds the decision, reason and evidence per item (F1 narrowed decision 7; F2
> chose (a); F3 confirmed; F4 follows E3's S4-17; F5 corrected). This section stays as the record of the questions.

**F1 — decision 7's last sentence: "Lines at or after this round's COMPLETION never change the result."** It is true
for placement and ownership. It is NOT true for tokens: a message's tokens are the per-term maximum over ALL its lines
(D8), and one id's lines span up to 2.01 h (Q4), so a message still being written at the completion has fewer lines in
the live read than in a backfill (E3 states this limitation as S4-2). The same holds for a write whose `tool_result`
arrives after the live read: live, it is unanswered and therefore not a write (S4-7); in a backfill it is a write, and an
association can change. Proposal: "never change which items are placed or who owns them; the token terms of a message
still being written at the completion, and a write answered after it, are the stated limitation of E3 S4-2/S4-7".
`LinesAtOrAfterCompletionChangeNothing` keeps its builder form (a builder message has one instant), and S5's replay facts
are written for the narrower claim (S5 brief 4).

**F2 — a write's instant is its MESSAGE's first line (E3 S4-8), not its own line.** Decision 7 says that this round's
ladder "sees only lines stamped before the start" (D6). With S4-8, a write on a line AFTER the start, in a message that
began before it, is evidence for this round. `c13`, `c15` and `c16` have exactly this shape: the `Write` line comes after
the round's start, and its message began before it. Their targets are `scratch`, so no cell changes today. Choose:
(a) keep S4-8 and say in decision 7 that the horizon applies to a write's message, not to the write's own line; or
(b) E3 stamps a write by its own line, so a write after the start goes into the next window, where its message may not
be — and the ladder then sees a write in a window where the agent has no placement. (a) keeps a message and its writes
together; (b) keeps D6 literal. Both keep the two identities.

**F3 — a round listed twice in one journal.** `AssignEvery` and identity 2 assume that each recorded `(Stage, Number)` is
unique. `c13` lists round 1 twice; tiled, the copy opens an empty window and the round is looked up twice. S1 now refuses
it in `Validate()`, so `Assign` throws and E5 records `unobserved: invalid input`. Confirm that S2 needs nothing more, and
that refusing (not merging identical copies) is the right answer.

**F4 — a state whose reason lost nothing.** S2.3 *States* copies every E3 reason as `partial: <why>`. If E3 keeps `meta
without model` as `partial` (§8.3 item 2), most windows of most rounds will be `partial` for a value that was observed.
Confirm whether S2 copies every E3 reason or only those E3 marks as a loss. The fix belongs to E3; if E3 makes it, S2's
text does not change.

**F5 — a fact in S2's scope paragraph.** "A repeated skip moves only its `CompletedUtc` (`:95-99`)": it also moves
`Repeats` and `Sha` (`RoundSkips.cs:97`). `StartedUtc` does not move, and a skip opens no window, so no rule changes.
Confirm, and correct the wording when S2 is next edited.

## 10. Risk consultation brief (owed before E4's code round)

The cadence named risk item **4/4.2** — epic 4, story E4.S2: *attribution decides what every number means*. Under
`coai-consultant.md` trigger 7 this is a consultation of its own: `kind: risk`, `plan:
todo/PLAN_a_round_records_who_wrote_the_code_and_how_much.md`, `epics: "4/4.2"` (the piece as the cadence named it). It is taken before E4's code round and
closed with `close_consult` after the advice is verified. Files to name: this document (§S2.2–§S2.6, §8, §9), the
parent's §6 and §16, and `research/RESULTS_caller_transcripts_on_both_sides.md`. The questions:

1. **Who owns ambiguous activity that two gate sessions both see?** Completion rank (D24) gives it to the
   earliest-completed window. All six windows E1 sampled held several models or several write roots, so ambiguous
   activity will be common, and its owner decides which gate's totals carry it. Is a rank rule defensible here, or
   should overlapping no-evidence activity go to a shared total that no gate owns?
2. **The evidence horizon inside one message.** One message's lines span up to 2.01 h. Its instant is its first line
   (placement), its tokens are the maximum over all its lines (D8), and its writes take the first line's instant (E3
   S4-8). *Decided 2026-10-16 (§S2.2a F1, F2): placement and ownership never move after completion; a straddling
   message's tokens and a write answered after the live read are the two stated limitations; a turn is the unit, so a
   write after the start stays with its message. §S2.2b G3 later withdrew the second limitation: a write answered after
   the completion is not evidence, live or backfilled.* The question for the consultant is narrower: is "the turn is the
   unit" the right horizon for a number that means "the work done before round N started", given that such a write's
   bytes land in round N+1's diff?
3. **`same-session` and the cheap-model case.** An agent with no write is credited to this repository when exactly one
   destination was written in its window. E1's sampled windows ran 32–3 825 minutes and held 1–6 write roots. Can this
   credit a no-write scout to the strong model's repository, or move a fixer's tokens onto the original author, in the
   consultant's round-3 case? And is `child of` through subagent parents only (main never propagates) right when most
   coders are depth-1 children of main?
4. **What `partial` should mean.** *Decided 2026-10-16 (E3 S4-17, §S2.2a F4): only when an item or a counted value is
   missing or uncertain; a meta with no `model` is a diagnostics count.* The open half: is "a round's state is the
   weakest of its windows" the right roll-up for the report, when the before-open window of a first round is the one
   most often `partial`?
5. **Requested or answered.** The owner asks about a POLICY ("coders moved to Sonnet"), and a policy is a request.
   Attribution counts the model that ANSWERED: a session can fall back (Opus 5.5 → Opus 4.8, scope session, seen
   2026-10-16), and metas often name no request at all. Is storing both and grouping on the answering model the right
   default for E6, and what must the report show where the two differ?

> *Taken 2026-10-16* (consultation `2d5ed45d…`, closed `solved`) together with the 4–6 cadence consultation (`95d43c15…`,
> closed `solved`). What each changed is in §11: G1–G3, G9 and G11 come from them, and the `requested` grain was applied
> in S3. *Fable resolved all of §11 the same day — §S2.2b.*
> On the five questions: completion rank is defensible as an accounting convention but is no repository evidence, and a
> caller-level shared total is the alternative (open, G11); `same-session` is inference, and main not propagating is
> sound; the weakest-window roll-up is honest, but a report that leaves out `before-open` needs the per-window states
> (G9); grouping on the answering model is right, and the report needs requested → answered counts (E6).

## 11. For Fable — from the 2026-10-16 plan gate, the own review and the two consultations

> **Resolved by Fable, 2026-10-16 — §S2.2b holds the decision, reason, evidence and the place of every rule.** The table
> below is the record of the questions as the plan gate put them; the *Resolutions* table after it points each one at its
> text, and the *Cross-epic consequences* list is what the other documents owe. Every item was checked against code or a
> document; the evidence is named. Items that touch only S1, S3 or S4 were applied in place, marked "plan gate 2026-10-16".
> The E3 anchors in rows G4 and G5 were written against `165de3bc`; on the E3 document at `fd668f62` (= branch tip
> `48ba4e5c`) they are `:206-210`/`:880-881`/`:912-917` (G4) and `:231` (G5) — the resolutions cite those.

| # | What is wrong | Evidence | Proposed fix |
|---|---|---|---|
| G1 | **A running peer is not `CompletedUtc = null`.** Decision 3, case 8b and §S2.7 rank a running peer last, but on disk the running record carries a completion stamp | `LiveRound.cs:256` saves the running row through `Record()`, which stamps `CompletedUtc = DateTime.UtcNow` (`:276`); `RoundRecord.CompletedUtc` is positional (`SessionStore.cs:84`) | the E5 mapping gives `GateRound.CompletedUtc` only when `Status == done`, else null; a builder fact with a running peer that HAS a stamp |
| G2 | **`PriorEnd` is not only a read bound when a peer is not done.** A running peer round's start counts (`recorded` excludes only skipped/interrupted). If that round is then interrupted, a message before `PriorEnd` is owned by nobody, but with `.WithoutPriorEnd()` it is owned — so case 7's equality claim fails | §S2.3 *Prior end*; risk consultation, turn 1 item 2 (A starts `+60`, running; B opens `+100`, completes `+140`; message at `+30`) | `PriorEnd` and `ClaimantsOf` read only `done` peer rounds; that scenario as a fact |
| G3 | **What is STORED can be owned twice.** The identities hold over `AssignEvery`, which recomputes. Live snapshots are frozen: (a) `Finish` stamps completion before attribution, save and projection, and `SessionClaim` locks repo + branch, not the caller, so two gates of one caller can each read the other as running; (b) a write answered after A's live read makes B recompute A's ladder as `wrote elsewhere`, and B owns what A already stored. Parent §9's backfill does not overwrite an `observed`/`partial` row of an equal reader version, so nothing repairs it. Decisions 7 and 10 say "placement and ownership never change after completion" and "the backfill is the authority"; both are false for the stored rows | `LiveRound.cs:233-237`, `RoundEngine.cs:485/591/612`, `SessionClaim.cs:32-44`, parent §9; both consultations | name an owner (E5) for uniqueness of stored ownership: provisional ownership plus reconciliation, or caller-level coordination; E5 tests the persisted interleaving, the late-result case and a failed projection |
| G4 | **A meta without a transcript is counted twice.** E3 step 8a emits `UnobservedLaunch(…, MetaWithoutTranscript)` AND keeps `ObservedAgent(TranscriptPresent = false)`; decision 9 and *What is placed* turn both into items, so `c05` gets 3 unobserved launches, not 2. Also, `partial: <its Why>` has no source for the agent: `ObservedAgent` has no `Why` | amended E3 (`165de3bc`) steps 8a/`:777`, `:811`; its `c05` row: `UnobservedLaunches.Count == 2` | only `UnobservedLaunches` make items and reasons; a `TranscriptPresent = false` agent gets no launch and no item; the builder's `.MetaWithoutTranscript` emits both records, as E3 does |
| G5 | **E3's reasons have no instant.** `ObservationReason(Code, Count)`; `StateReason.At` cannot be filled, and `At = null` marks every window `partial`, against decision 9 ("other windows stay observed"), case 5's C2 and S3's "a read budget hit before the window's end" | amended E3 `:227` | the mapping drops the two transcript-missing codes (G4's `UnobservedLaunches` carry them, with instants) and maps the rest with `At = null`; say so in §0 |
| G6 | **`AssignEvery` throws on another caller's round.** One observation, every round: case 8b (B C1 by `c9`) and *Caller changed* fail `Validate()`. The `PeersUnknown` variant of case 7 duplicates on purpose | §S2.5 identity 2; S1 `Validate()` caller rule | `AssignEvery` skips rounds of another caller; the `PeersKnown = false` scenarios are outside the identity-2 theory |
| G7 | **The token identity is wrong** (gate finding, accepted). `TokensAreTheSumOfOwnedMessagesOnce` subtracts `wrote elsewhere`, but cx `+10` in case 1 is owned by A AND labelled `wrote elsewhere` by B | §S2.10 row `NoMessageIsOwnedTwiceTests` | over message ids: tokens counted once when some assignment owns the id; excluded only when no assignment owns it and it is `wrote elsewhere` |
| G8 | **Blind rounds, two texts.** §S2.1 says a round with no `StartedUtc` "opens no window, so its stretch lands in the next recorded round"; decision 1 makes it blind and the next round blind too | §S2.1 vs §S2.2 decision 1 | §S2.1 says what decision 1 says |
| G9 | **Per-window state is lost.** S3 collapses `WindowState` into one round state, and `AuthorRow`/`ActivityTotals` are announced final. E6 leaves `before-open` out by default, so it cannot compute the state of the windows it includes ("before-open partial, round observed" and the reverse store the same) | §S2.5 `States`; S3 *State*; cadence consultation item 2 | S3 carries the state and reasons per window (empty windows too) before E5.S1 is coded; announce it to E5 |
| G10 | **Decision 4's ambiguous agent in B.** "B's ambiguous totals count 1 agent and 0 tokens", but every w2 message in B is `claimed by peer`, and S3 has no rule that counts that agent; `BothGatesCountOneAmbiguousAgent` sits in S2, which makes no totals. The two-repositories row has no `Model`/`Effort`, and S3 says the only `unknown` model is the launch-only row | §S2.6 *Two repositories, one agent*; S3 *Grain* | a rule for the agent count from `AgentAssociation`; the fact moves to S3; that row's `Model` is `unknown` (or its placements') and S3 says so |
| G11 | **Open choice (not a defect):** ambiguous activity seen by two gates goes by completion rank; the risk consultant prefers a caller-level shared total that no gate owns, at the cost of one more aggregate. With rank, E6 labels the allocation and keeps it out of repository and model conclusions | risk consultation, turn 1 | Fable chooses; E6 states it |
| G12 | **Small text points.** (a) §S2.3 says "strongest wins" and §S2.5 "the weakest over its windows" for one ordering — use one word. (b) Claimants test `P.From ≤ t < P.To`; a null `From` needs `P.Contains(t)`. (c) When the ladder runs for a peer's window, its destination set must include THIS gate's `repo_path`. (d) `NoInputMemberIsACwd` and `ABashCallIsNotAWrite` cannot fail against the shells; say that their break-its prove them. (e) Four anchors are stale: `SessionStore.cs:248` → `:278`, `:594-597` → `:608-631` (`:626`), `RoundEngine.cs:895` → `:900` (case 2, §S2.11) | own review; §8.1 | edit when S2 is next amended |

### Resolutions (Fable, 2026-10-16) — one line each; the full row is in §S2.2b

| # | Resolved as | Where the rule lives |
|---|---|---|
| G1 | a completion ranks only on a `done` round — the tiling nulls it otherwise, the E5 mapping too; verified `LiveRound.cs:256` → `:276` (stamp on the running row), `:235-237` (`Finish`), `SessionStore.cs:84`, `:626` | §S2.3 *Tiling*, *Rank*; §S2.7; §S2.8 item 5; fact `ARunningPeerWithAStamp_RanksLast` |
| G2 | `PriorEnd` = the end of each peer's SETTLED PREFIX (the walk stops at the first unsettled, blind, other-caller or post-open round); claimants are settled windows only | §S2.3 *Prior end*, *Claimants*; cases 8c, 8d; facts `APriorEndStopsAtTheFirstUnsettledPeerRound`, `ASettledPrefixEndsBeforeTheRunningRound` |
| G3 | owner of stored uniqueness: **E7's backfill**. E4 makes it decidable: a write is evidence only when `AnsweredAt < the round's completion` (own ladder: this round; claimant's ladder: the claimant's round), live and backfilled alike — the late-answered write cannot differ; a window overlapped by a not-settled same-caller peer round is `partial: peer running` — provisional, E5 stores it unchanged, E7 recomputes exactly these once the overlapping peer rounds are final (one of two exceptions to "equal reader → keep", L10/M7); `PeerGates` names the peers seen so a grown peer set (a failed projection) is recomputed too. Decision 7's limitation (ii) withdrawn; decision 10's "the backfill is the authority" made true | decision 3, 7, 10; §S2.3 *States*; §S2.4 *The horizon*; §S2.5 `PeerGates`, `Version`; case 6 (a)–(c), 8b; facts `AWriteAnsweredAfterTheCompletion_IsNotEvidence_LiveOrBackfilled`, `APeersLadderSeesOnlyWritesAnsweredBeforeThePeerCompleted`, `AWindowOverlappedByARunningPeer_IsPartialPeerRunning` |
| G4 | only `UnobservedLaunch`es make items and reasons; a `TranscriptPresent = false` agent is inert (declared, no launch, no item, no reason; `FirstLineAt` nullable); `c05` → 2. E3 owns: one `UnobservedLaunch(…, MetaWithoutTranscript)` per meta without a file (E3 `:880-881`, `:916-917`) | decision 9; §S2.3 *What is placed*, *States*; S1 records and mapping; fact `AMetaWithoutATranscript_IsOneUnobservedLaunchNotTwo` |
| G5 | every E3 reason → `StateReason(Code, At: null)` → every window `partial`; the two transcript-missing codes are never mapped as reasons (their instants ride on the launches); S3's state table corrected | §0 table; S1 mapping; §S2.3 *States*; S3 *State*; fact `AnE3ReasonWithoutAnInstant_MarksEveryWindow` |
| G6 | `AssignEvery` covers the settled rounds of the observation's caller only; `PeersKnown = false` scenarios are outside identity 2; `Validate()` refuses attributing a round that is not recorded and settled | §S2.5 identity 2; S1 `Validate()`; §S2.9 item 2; fact `AssignEverySkipsRunningRoundsAndOtherCallers` |
| G7 | the token identity over message ids: owned once, or owned by none and `wrote elsewhere` wherever placed; Σ owned tokens = Σ tokens of the ids owned somewhere | §S2.5 identity 2; `TokensAreTheSumOfOwnedMessagesOnce` |
| G8 | §S2.1 rewritten to decision 1: a round with no `StartedUtc` is blind, it and the next are `unobserved: no round start`, nothing re-tiled | §S2.1 |
| G9 | per-window state AND totals kept: `RoundAuthors.Windows: ImmutableArray<WindowAuthors>`; round-level = roll-up (worst state, summed totals); announced to E5 | S3 *The records*, *State*; `EveryWindowKeepsItsOwnStateAndTotalsTests` |
| G10 | agent counts per total, not a partition (`Ambiguous.Agents` includes every `two repositories` association); the fact moves to S3; the two-repositories row is `Model = Requested = Effort = "unknown"` | S3 *Grain*, *Agent counts*; `ATwoRepositoryAgentKeepsItsWritesAndAddsNoTokenTests` |
| G11 | **completion rank kept; caller-level shared total rejected** (no store entity, not decidable live, decides a number no conclusion reads, rank is what keeps live = backfill); E6 states the allocation in one sentence and never reads it as repository or model evidence | §S2.2b G11; decision 3; cross-epic list (E6) |
| G12 | (a) "worst wins", one word; (b) `P.Contains(t)`; (c) destinations over every gate in the input; (d) the two positives are proven by their break-its; (e) the four anchors corrected in place, the §11 E3 anchors re-cited | §S2.3, §S2.4, §S2.5, §S2.10, §S2.11, the S2 scope paragraph |

### Resolutions II — the judge review of 2026-10-17 (every point checked in the code first; none rejected)

| # | Point | Checked against | Resolved as (E4 side) |
|---|---|---|---|
| B1 | E4.S5 needed a mapping only E5.S4 built, after E4 | E3 `:191`, `:712` (`ObservedSession` in `src/Store`); E4 §0 and S5 as they stood | **`src_mcp/src/Store/AttributionMapping.cs`, built by E4.S5**, called by E5.S4 step 5 and E7.S2: `Observation`, `Timeline`, `Universe`, each a rule-free copy, with `TheMappingDecidesNothingTests`. Reason: it is the one place that sees E3's `src/Store` records and E4's core records; E3 cannot build it (E4's types do not exist yet), E5 is built after E4, and E4.S5 already depends on E3.S4 — so E4 reaches Done before E5. §0, S1 *Mapping*, S5, §S2.8 item 6, DoD |
| H1/H2/M2/M7 | E7's rule order (E5 §10 item 11) | E7 `:167-174`; `SessionStore.cs:626`, `:718-719` | E5's to write (its §10 item 11); E4's part: **final** = `done` or `interrupted` (M7), the `peer running` trigger reads it (§S2.3 *Settled/Final*, *States*, case 8b, G3 row) |
| M1 | an unobserved round's `UnobservedLaunches` undefined | S3 *State* | `UnobservedLaunches = -1` whenever the window or the round is `unobserved`; E5 derives `-1` for its two write counts (S3 records and table) |
| M4 | `session.Rounds` lacks the round at the call site | `RoundEngine.cs:594`, `LiveRound.cs:257`; `CallerDeclaration.cs:34-38`, `RoundCommands.cs:34-35` | §S2.8 item 6: E5 passes `[.. session.Rounds, record]`; earlier rounds' callers from the file's snapshots, then `authors_session`, then a UUID `rounds.caller`, then `""` |
| M6 | two owners for skipped/interrupted rounds | E5 §3, E7 `:178` | one sentence: no snapshot, empty `authors_state`, live and backfilled; E7 computes nothing for them; `interrupted` = `RoundRecord.Interrupted`, the sweep's word (S1 `Validate()` text, §S2.8 item 5) |
| L1 | `WindowAuthors` not declared final | S3 | declared final beside `AuthorRow`, `ActivityTotals` |
| L2 | "inside the `with` block" | `RoundEngine.cs:485-496`, `:591` (an initializer cannot name its record) | "one statement after the block, before the save" in the scope paragraph, §S2.4 *The horizon*, G3's evidence |
| L4 | two spellings of the peer key | §S2.8 item 1 vs S1 | `authors_session`, else a UUID `rounds.caller` — everywhere |
| L6 | the universe's spelling only in S1 | S1 `:205-206` | `AttributionMapping.Universe` states it and `TheUniverseKeepsEachSpellingAndDropsNoRoot` tests it |
| L8 | no test for the G5 mapping rule | S1 mapping sentence | `TheTwoTranscriptMissingCodes_AreNeverStateReasons` in S5 (the mapping's home) |
| L9 | `AssignEvery` had two homes | §S2.5, §S2.9 | `AttributionScenario.AssignEvery()`, test-side only |
| L10 | "the ONE exception" | E5 §10 item 11 (b) | "one of two exceptions" in G3's row and the E7 list; the second is E5's lost-snapshot recompute |
| M3, M5, M8, L3, L5, L7 | E5-only points | — | resolved in the E5 document (its §11.4); nothing in E4 changes |

### Cross-epic consequences (owed by the other documents; nothing here edits them)

- **E3** (`feat/round-authors-e3`): (1) `ObservedWrite` gains **`AnsweredUtc`** — the instant of the `tool_result` line that
  answered the call (the line's own stamp; under S4-8 the `user` line's message instant when it has one — E3 spells the
  rule) — and the mapping fills E4's `AnsweredAt` from it (G3); without it E4 cannot apply the horizon and the
  late-answered-write hazard returns. (2) Confirm that every PRESENT agent file yields a `LaunchedUtc` (its first stamped
  line), or name the state of a present file with no stamped line — E4's `Validate()` refuses a present agent with no
  `FirstLineAt` (G4), and `transcript empty` (S4-10) is main-file only today. (3) Nothing else: E4 reads the two
  transcript-missing codes from `UnobservedLaunches` only, never from `State.Reasons`, exactly as E3 emits them (G4, G5).
- **E5** (`feat/round-authors-e5`): (1) §3's sentence "a peer round still `running` is in the file with no completion and
  ranks last" (`:65`) is false on disk — the mapping fills `GateRound.CompletedUtc` only for `Status == done` (G1; the core
  applies the rule too). (2) `Assign` is called for SETTLED `PlanReview`/`CodeReview` rounds only (G6); an `ArgumentException`
  stays `unobserved: invalid input`. (3) `partial: peer running` is stored as it is and never repaired live (G3); E5 writes
  no other gate's rows. (4) The snapshot carries `RoundAuthors.Windows` (per-window state and totals, G9) and `PeerGates`
  (ids only, G3) — `AuthorRow` and `ActivityTotals` unchanged; §14's growth row gains ≈ +300 B a round. (5) Store
  `RoundAttribution.Version` beside `authors_reader` (E5 §7 item 19 asked E4 to say so before S2 is coded — said). (6) The
  `IsWellFormed` vocabulary admits `peer running`; no E4 reason contains `", "` still holds. *Applied in the E5 document on
  2026-10-17 (its §11.2), together with the judge review's E5 points (B1, M1–M8, L3, L5–L7 — its §10 item 11, §11.4).*
- **E6** (`feat/round-authors-e6`): (1) state in one sentence that contested activity is allocated to the first-completed
  gate (completion rank) and labelled `claimed by peer` on the other side, and never read the allocation as evidence of a
  repository or a model (G11). (2) D2/D3 may read the per-window state and totals (`Windows`) when `before-open` is left
  out, instead of the round roll-up (G9) — if the report reads only columns, E5 must project the round window's state, or
  E6 states the limit. (3) A round whose state carries `peer running` is provisional: count it as `partial` (D3) and say
  that E7's backfill finalises it. (4) The two-repositories row's `unknown` model is a row with write calls and no model
  claim — the coder mix must not read it as a model (G10).
- **E7** (`feat/round-authors-e7`): (1) one of TWO exceptions to "a non-empty state written by a reader ≥ the current one
  is kept" (`BackfillDecision` rule (2); the other is E5's lost-snapshot recompute): a row whose reasons include `peer
  running` is **recomputed** once every overlapping round of every gate in its `PeerGates` is FINAL (done or interrupted,
  M7), and so is a row whose caller now has a settled gate session that is NOT in its `PeerGates` (a failed projection,
  G3; never for a healed snapshot, whose peer set is unknown); the result is written under that session's claim, and a
  second run then changes nothing. (2) "Equal reader" compares E3's `ReaderVersion` AND `RoundAttribution.Version`. (3)
  Non-recorded or unsettled rounds are never handed to `Assign` and get NO state (M6). (4) E7.S2 calls
  `AttributionMapping` (B1) and E5's `AuthorPeers`, and stops reading `before-open` from its own database join. **The
  complete, ordered list of E7 changes is E5 §10 item 11** (written 2026-10-17 for the E7 gate to apply).
- **The parent**: §6's *Evidence cutoff* and §16 D6/D24 as §S2.12 states them — a write is evidence only when its result
  was written before the round completed, live and backfilled alike (D6); D24 ranks SETTLED rounds only and a provisional
  ownership is `partial: peer running`, repaired by E7; §9's "never overwrites an `observed`/`partial` row produced by a
  newer or equal reader version" gains the `peer running` exception; §6's `before-open` definition says "the end of the
  last SETTLED window … of the same caller" (G2).

## 12. Implementation record (2026-10-19)

Built on `feat/round-authors-e4` after merging `feat/round-authors-e3` (merge, not rebase). One commit per story. Every test was watched RED on its
assertion against a shell that returned empty values, and every break-it of S1, S2, S3, S4 and S5 (and of `AttributionMapping`) was planted, watched red by
the test named for it, restored, rebuilt and watched green. S5's acceptance tests run over finished code, so their red is their break-its.

**E3 facts verified in the code** (the E4 anchors that moved): `ObservedSession` `src_mcp/src/Store/ObservedSession.cs:11`, `ReaderVersion = 1` `:22`, `ObservedAgent` `:33`,
`ObservedMessage` `:49`, `ObservedWrite(AgentKey, Utc, AnsweredUtc, Tool, Target)` `:63`, `UnobservedLaunch` `:66`, `ReadDiagnostics` `:69` (with `AgentFilesEmpty`),
`ObservationReason` `:98`, `ObservationReasons` `:134`; main is listed with `LaunchedUtc = null` and `TranscriptPresent = true` (`TranscriptLaunches.cs:101`) — the mapping leaves it out of
`Agents`; there is one `UnobservedLaunch` per meta without a file (`TranscriptLaunches.cs:167-171`) and one per `Agent` call without a meta (`:194`); `WriteTarget.Where` is a normalised KEY
(`RepositoryIdentity.Normalised`), compared to the gate's `repo_path` by `Repositories` (separators folded, case ignored) and never resolved. E4's own anchors on this tree:
`AgentLog.InRepo` is now one line, `AgentLog.cs:228`, calling `Repositories.Contains`; `WhatTheCallerWasDoing` is `RoundEngine.cs:899`; the save appends the record at `RoundEngine.cs:595` (*was cited `SessionStore.cs:595`; corrected 2026-10-19, re-verified after the merges of E3 `24cced19a` and main `bb279e3db`*).

**Deviations from the text above, each the smallest change that keeps the intent.** None changes a type E5 consumes: `RoundAuthors`, `WindowAuthors`, `PeerGates`,
`RoundAttribution.Version`, `StateReason` and the settled and final rules are as specified.

**Fable's code review, 2026-10-19** (over `595d1e991`, against §S2): every deviation below and every addition of item 8 is
ACCEPTED, and no code change was required. What E5 and E7 rely on is unchanged and verified (`RoundAuthors`, `WindowAuthors`
final, `PeerGates`, `Version = 1`, `StateReason`, settled and final, `PeerStillRuns`, `PeersKnown = false`; `main` is never
listed in `Agents`). Fable's wording changes are applied in place above — §S2.2b G2 and G9, §S2.3 *Prior end*, *Ownership*
and *What is placed*, §S2.6 cases 6 (a) and 8c, §S2.10, §S2.11, S1's `Validate()` list and *Mapping*, S3's *Agent counts*,
state table and test row, S4's rules, S5's `c17`/`c19` rows and the list of what no fixture shows, and §8.3 item 4 (closed).
Items 4 and 5 below need no §S2 change.

1. **The prior end counts only peer rounds that complete BEFORE this gate's own first round** (§S2.3 *Prior end*, case 8c). As written, the walk takes a settled prefix whatever its
   completion. With case 8c's "A C1 `done @150`" (A ranks after G's +140) the bound would be +60, G's before-open window would be `[+60, +100)`, and G would lose +30 to A — so the bound would
   be an OWNERSHIP rule, against case 7's equality and against case 8c's own sentence "G keeps +30, A labels it claimed by peer (G)". The rule now also requires that the peer round's window
   precedes this gate's before-open window by rank; the sentence is a test (`WhenThatPeerRoundSettlesAfterG_GKeepsTheMessageAndAClaimsNothing`). Every other claim of cases 7, 8c and 8d holds as written.
2. **The fact "a blind before-open, a counted round window" cannot arise** (S3 `ABlindBeforeOpenLeavesTheRoundWindowCounted`, G9). Both windows of the first round depend on the same two instants (open and
   start), so they are blind together. The fact became `ABlindFirstRound_IsUnknownInEveryWindowAndInTheRollUp`. The per-window design is unchanged.
3. **Case 6 (a): the late-answered write reads `ambiguous · no evidence`, not `same-session`.** On case 1's A C2 the window also holds cy's write into y, so its destinations are {y}. With
   cx's +75 write dropped by the horizon, cx has no evidence of its own. What the text means — the write is not evidence, live or backfilled, and no write placement exists — is what the test asserts.
4. **`ANewPeerGateChangesNothing` compares everything but `PeerGates`**, which gains the new peer by design (§S2.2b G3 iii): the field exists so a backfill sees the peer set grow.
5. **A record holding an `ImmutableArray` compares and prints the array by reference**, so "equal by value and by `ToString()`" is judged on the serialised JSON (and on `BeEquivalentTo`). The sentinel
   test serialises too. No type changed.
6. **S5's cells for `c17` and `c19` differ from the 2026-10-16 hand-read**, because E1 re-cut both fixtures (results kept) and they reached this branch through E3. `c17` now SHOWS
   `ambiguous · two repositories` (A owns the three messages, B labels them `claimed by peer`, each gate keeps a `wrote here` row with one write call). `c19` shows the submodule write as
   `wrote here`, listed in `WritesOutsideTheGateCheckout`, and the scratchpad write as outside. Both were re-read from the fixture lines on 2026-10-19.
7. **`FixtureTruncation`** (test-side, static) cuts a materialised fixture's files at an instant for the live-record-against-backfill facts. It is not a method of `CallerTranscriptFixture`, so E3.S5's
   mutation-helper census is untouched. **Break-it S5 (d)** (materialise without folding separators on a non-Windows host) cannot be planted on Windows; the precondition (`CheckoutWrites` per case) is the guard.
8. **Additions the spec did not list.** `Validate()` also refuses an agent declared twice. `PeersKnown = false` makes `Assign` read NO peer (so `PeerGates` is empty and every window says
   `prior windows unknown`, whatever `Peers` holds). An unobserved caller places nothing, and its windows are `unobserved`. `RoundAttribution.PeerStillRuns(input)` exists (E7 item 9, below).
   The round-level `Agents` count of a total is the SUM of the windows' counts (§S3 "totals summed"), so an agent in two windows counts in both. `NotSeen.Why` reads `<state>: <reasons joined by ", ">`.
   A contributor's `Models`, `Requested` and `Efforts` include `unknown` when a launch-only or two-repositories row carries it. A `claimed by peer` placement keeps this gate's own ladder rung.

**The judge's review, 2026-10-19** (Opus, after Fable's; four findings, each fixed by a Sonnet coder in its own commit, RED then GREEN):

9. **Cyclomatic complexity ≤ 4 is a build error now, not a habit.** Measured with Roslyn's CA1502 (threshold file `src_mcp/CodeMetricsConfig.txt`, `CA1502: 4`; a
   lambda counts toward the method that holds it), ten methods were over the bound: the `AssignRun` constructor and `Build`, `AssociationLadder.ChildRung`,
   `AttributionContract.Climbs`, `Repositories.IsAbsolute`, `WindowStates.CallerChanged`, `Windows.ClaimsBefore`, `First`, `Compare` and `BlindReason`. `.editorconfig`
   now sets CA1502 to `error` for `src_mcp/core/Authors/*.cs` and `src_mcp/src/Store/AttributionMapping.cs`, beside the security lane. RED was the build failing on
   those methods; GREEN is a refactor with no behaviour change (`ae65d3abb`).
10. **The round counts an unobserved launch with no instant ONCE** (S3's "the roll-up: sums" is narrowed for this one member). Such a launch marks every window,
   and each window's `UnobservedLaunches` still counts it; but summing the windows counted it twice in a first round (before-open + round), so one missing
   transcript read as two, against decision 9 (G4). The round's `UnobservedLaunches` is now the placed launches plus the launches with no instant
   (`RoundAuthorsBuilder.UnobservedLaunchesOf`). RED `AnUnobservedLaunchWithNoInstant_IsCountedOnceInTheRound`: expected 1, found 2 (`6fe57e5d7`). The other
   totals are still the sum of the windows.
11. **One place per rule** (reuse-first). `Repositories.SameRoot` replaces three copies of the same-root test (`Windows`, `AssociationLadder`, `WindowSlice`);
   `ObservationKinds.Badness` replaces two copies; the stage words are `nameof(Stage.PlanReview)` and `nameof(Stage.CodeReview)`; and `AttributionMapping` reads
   every message and write instant through `UtcInstant.Of` (the rule `UtcInstant.From` now calls) instead of its own `SpecifyKind`. RED: a Local-kind instant
   on this +02:00 machine (`2684ab8d1`). E3 parses stamps as UTC, so no stored value changes.
12. **The purity scan also forbids a working-directory lookup** (`Path.GetFullPath`, `CurrentDirectory`), which is S1's rule "never resolved against a working
   directory". A planted `GetFullPath` turned it red (`c210f9318`).

**The coai code gate, 2026-10-19** (session `a9d8b6b8`, code round 1 over `71e15e392`, `baseRef` a parentless commit whose tree is E3
`24cced19a` merged with main `bb279e3db`, so the reviewers saw E4 alone): verdict **`good_enough`** (gating 17 against a threshold of 5,
the stage's one round spent), **13 of 16 reviewers** answered — the three Kimi reviewers of Architecture, SecurityReliability and
UxDxPerformance returned empty answers. 23 findings: **4 accepted, 19 rejected**, each rejection checked in the code and its reason
recorded with `resolve`. The rejections in short: the ladder never makes a two-repositories row for a gate the agent did not write
into (`WriteRung`); `Climbs` already reports every cycle; the dot-segment refusal is in `RepositoryOf`, which is the only road E4 uses;
E3 never emits a default instant; equal sort keys are equal records; the performance findings assumed every window of every round
(an assignment holds one or two) or many gate sessions (a handful). Accepted:

13. **Each write's home row is found once per window** (finding 3; `dcf327428`): `WindowSlice.WriteCallsByRow` builds the row → write
   calls map once, instead of a scan of every write and every message per row. Same rule, no behaviour change; the E4 classes and the
   full suite are green.
14. **The worked examples are listed once** (finding 12; `18733ae50`): `AttributionCases` holds one `(name, factory)` table that both
   `Names` and `Named` read, so a case cannot be added to one and missed by every identity theory (105 theory rows before and after).
15. **`Assign` throws `ArgumentException` on a contract violation** (finding 10, recorded, not changed): csharp doctrine §5 says expected
   failures are values. A violation here is not an expected answer — it is E5's or E7's programming error against the S1 contract, and
   §S2.7 requires the throw; E5 maps it to `unobserved: invalid input`. `Validate()` is the value-returning form for a caller that wants
   to ask first.
16. **The closed vocabularies stay `string` constants** (findings 11 and 0, recorded, not changed): `Association`, `Rung`, `WindowKind`,
   `WriteWhere`, `ObservationKinds` and `GateRound.Stage/Status/Verdict` are the exact words E5 stores and the session file holds
   (doctrine §3's serialisation exception); every comparison is against the one constant class, and an unknown stage legitimately
   opens no window. `WriteKind` is an enum because it is E3's enum copied one for one and is never stored.

**The two confirmations E7 asked for (E7 §10 items 8 and 9):**

- **Item 9 — "a named peer round still runs" is §S2.3's window overlap.** It is the same test that sets `partial: peer running`: a window of THIS caller, not blind, not settled (a `running` round;
  an interrupted one opens no window), sharing an instant with one of this round's non-blind windows. `RoundAttribution.PeerStillRuns(AttributionInput)` exposes it, so E7 uses E4's predicate and not its
  own. `APeerStillRunningIsTheWindowOverlapTests` pins it (overlapping: true; the non-overlapping A C3: false; another caller: false; interrupted: false) and shows that predicate and state agree on every worked example.
- **Item 8 — `PeersKnown` on `AttributionInput` means "the lookup of the caller's other gate sessions completed".** True: `Peers` is the complete set (E4.S5's fixture loader sets it true). False: `Peers`
  is not read — `Assign` names no peer in `PeerGates`, claims nothing, marks every window `partial: prior windows unknown`, and `PeerStillRuns` is false — so an incomplete lookup proves no growth and E7 defers a
  `peer running` row. E5's `AuthorPeers.Find` sets it false when a peer's session file did not load or the deadline cut the walk.
