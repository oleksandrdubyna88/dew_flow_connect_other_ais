# PLAN — a reviewer that found nothing is as inspectable as one that failed

> Status: **IMPLEMENTED, 2026-09-08.** Scope:
> `src_mcp/runners/Reviewers/ReviewerExecutor.cs`, `src_mcp/src/Server/PanelService.cs`, and the
> round audit line in `src_mcp/src/Server/RoundAudit.cs`.
>
> Related docs: [module_runners.md](module_runners.md),
> [module_server.md](module_server.md),
> [PLAN_empty_vendor_answer.md](PLAN_empty_vendor_answer.md) — which did this for the
> answer that fails to PARSE. This is the other half.

## The symptom, measured

2026-09-08, 16:12 UTC, one code round on `fix/vendor-colours-never-repeat`. From the ledger
(`usage.jsonl`) and that run's own log:

| reviewer | input | output | seconds | findings |
|---|---|---|---|---|
| codex × 4 | 33.4k–33.6k | **44–94** | 4.5–24.8 | **0** |
| gemini × 4 | 34k–42k | **38–1033** | 8.7–10.5 | **0** |
| local × 4 | 21.6k | 600–911 | 21–45 | 3 / 2 / 3 / 3 |

Eight remote reviewers answered `{"findings": []}` on a diff the local reviewer found eleven things
in. The operator asked what broke.

**It is dateable.** Across every code round since 2026-09-01, codex has produced an empty answer
**once in ~400 runs**. Four of those runs are in this one round. It is also not the build: the same
process, seven minutes later, gave codex 62.5k input and 2289/2373/2533 output over 51–66 seconds
on another branch.

**And it cannot be diagnosed, because nothing was kept.** An `ok` outcome with zero findings stores
its verdict and its token counts. The raw text the vendor returned is dropped; only a PARSE failure
is written to `unparseable/`. So the question *"did the model answer emptily, or did something
upstream hand it a prompt that deserved an empty answer"* has no artefact behind it — an hour of
reconstruction from a ledger, ending in a shrug.

**The round log cannot answer the other half either.** It writes
`rules for review: 9 file(s), 78757 bytes, 19 omitted` and says nothing whatever about the DIFF —
which is the reviewers' entire world at the code stage. Whether those eight reviewers saw the change
at all had to be inferred by subtracting the rules' byte count from a token total.

## What this changes

### 1. The round says what it sent

One line beside the rules line, at the point the context is assembled
(`PanelService.ReviewCodeAsync`, where `shaped` and `rules` are both in hand):

```
context for review: diff 63104 bytes over 13 file(s), 0 elided; plan 4210 bytes; rules 78757 bytes
```

Three numbers that are already computed and currently thrown away. `DiffShaper.Shape` returns
`ShapedDiff(Text, Elided, TotalFiles)`; the elision count matters because a partial view is exactly
the state in which a reviewer's silence means nothing.

This is what turns "did they see the change?" from an afternoon into a line.

### 2. An empty review keeps its answer

`ReviewerExecutor.RunAsync` returns `ReviewerOutcome.Ok(parsed, …)` the moment a review parses,
whatever it contains. When `parsed.Findings` is empty, the raw text is kept — the same `Keep`
mechanism the unparseable path already uses, into `empty/` rather than `unparseable/`, because the
two are different questions and a reader looking for one must not wade through the other.

An empty answer is 40–90 output tokens; a round of them is a few kilobytes. The retention question
this raises is real but not new — `unparseable/` has files from 2026-09-06 and no policy — and it is
named in *What this does NOT do* rather than solved here.

The path is not surfaced in the round reply: a round where every reviewer legitimately found
nothing is the healthy case, and a sentence about evidence files on every clean round trains people
to ignore it. It goes in the LOG line for that reviewer, which is where somebody chasing a silent
round is already looking.

## What this does NOT do

- **It does not decide why the 16:12 round was empty.** Nothing kept the evidence, so that round is
  unanswerable; this change is what makes the next one answerable. Saying otherwise would be
  inventing a cause.
- **It does not add a retention policy** for `unparseable/` or `empty/`. Both need one; that is its
  own change, and it should cover both directories with one rule.
- **It does not treat an empty round as a failure.** A reviewer that finds nothing has answered.
  Turning silence into an error would break every clean round in the repository.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `AReviewWithNoFindings_KeepsWhatTheVendorActuallySaid` | The defect: the raw answer of a zero-finding review survives the round |
| 2 | `AReviewWithFindings_KeepsNothing` | The other side — the healthy case writes no files, so the directory means what it says |
| 3 | `TheEmptyAnswerIsKeptSeparatelyFromTheUnparseableOne` | Two questions, two directories |
| 4 | `AKeptAnswerNamesTheReviewerThatGaveIt` | The name is the index — twelve files, and one belongs to codex/Architecture |
| 5 | `KeepingEvidenceNeverFailsARound` | A blocked path leaves the outcome `Ok` with no evidence, never an exception |
| 6 | `AKeptAnswerIsWholeOrAbsent_NeverHalfWritten` | No `.writing` sibling survives a successful keep |
| 7 | `ARoundThatFoundNothing_SaysWhatItSent_AndKeepsWhatItWasTold` | **The scenario test.** A whole plan round and code round in which every reviewer answers empty: both `context for review:` lines, each reviewer's prompt size in the opening line, and the eight answers on disk. Catalogued in `research/module_tests.md` |

The first draft of this table named two unit tests for the context line (`TheContextLineNamesTheDiffAndTheRules`,
`AnElidedDiffSaysSo`) and they were not written. The code round caught the mismatch, and it was right
to: those two would have asserted a format string against itself. What the line has to be trusted
about is that it describes a round that really ran — three classes joining up — which is a scenario,
not a unit. Test 7 replaces both, and its teeth were checked by breaking the line's own text and
watching it go red.

## Definition of Done

- [x] A zero-finding review's raw answer is kept, in its own directory, named in that reviewer's log line.
- [x] The round logs what it sent: diff bytes, files, elided, plan bytes, rules bytes — at BOTH stages.
- [x] Each reviewer's own prompt size is logged, so the line describes a payload rather than an intention.
- [x] Tests 1–7 written, watched fail, and passing.
- [x] `research/module_runners.md` and `research/module_server.md` record what is kept and what is not.
- [x] The 2026-09-08 measurement is recorded here, so the next person reading a silent round has the numbers.

## What shipped differently, and what the gate changed

Fifteen reviewers over a plan round and a code round.

- **The plan's own test table was wrong.** It named two unit tests for the context line that were
  never written, and gemini caught the mismatch. They would have asserted a format string against
  itself; what the line must be trusted about is that it describes a round that REALLY RAN — three
  classes joining up — so a scenario test replaced both, and `research/module_tests.md` names it.
- **`PromptBytes` defaulted to zero**, which is a measurement that was never taken rendering as a
  number. `0 bytes` in an audit line is a claim that a reviewer was sent an empty prompt, and the
  field exists to be believed on exactly that. It is `int?` now, and the audit omits it when absent.
  Raised twice, by codex, from two different roles.
- **The catch set was too narrow.** `IOException` and `UnauthorizedAccessException` covered the
  obvious cases; `SecurityException`, `ArgumentException` and `NotSupportedException` were not
  caught, so a policy or an invalid path could still turn a review the vendor answered perfectly
  into a failed round. Four reviewers, independently.
- **A failed write left its `.writing` sibling behind** — an artefact in an evidence directory that
  is neither an answer nor an absence, accumulating one per failure. Deleted on the failure path now.
- **Nothing said why evidence was not kept.** Returning null silently means an empty directory later
  reads as "no round was ever silent here". The executor takes a note callback, and the panel logs it.
- **The context line existed only for CODE rounds.** A plan round has no diff and no rules, and now
  says so in the same shape — a person reading a silent round looks in one place either way.
- **A per-file cap and a filename sanitiser** were added: the answers this collects are a few hundred
  bytes, and both guards exist for the case that is not that.

Rejected, with reasons recorded in the session: three findings claiming `File.Move` sits outside the
try (it does not), one claiming `TestContext.Current` implies the VSTest runner (it is xUnit v3's own
API, and the suite runs as an executable), one asking to rename a method to itself, and the
suggestion to make the evidence write asynchronous — 4 KB per round, once per reviewer, at the end
of a run measured in seconds to minutes.
