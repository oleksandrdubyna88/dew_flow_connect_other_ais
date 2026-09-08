# PLAN — a reviewer that found nothing is as inspectable as one that failed

> Status: **plan only, nothing implemented yet.** Scope:
> `src_mcp/runners/Reviewers/ReviewerExecutor.cs`, `src_mcp/src/Server/PanelService.cs`, and the
> round audit line in `src_mcp/src/Server/RoundAudit.cs`.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md),
> [PLAN_empty_vendor_answer.md](../research/PLAN_empty_vendor_answer.md) — which did this for the
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
| 3 | `TheEmptyAnswerIsKeptSeparatelyFromTheUnparseableOne` | Two questions, two directories: a reader after one is not handed the other |
| 4 | `KeepingEvidenceNeverFailsARound` | Already true of the unparseable path (`catch (IOException) => null`) and must stay true of this one |
| 5 | `TheContextLineNamesTheDiffAndTheRules` | The log line carries diff bytes, file count, elided count and rules bytes |
| 6 | `AnElidedDiffSaysSo` | The number that decides whether a reviewer's silence is evidence at all |

`src_mcp/tests` already drives `ReviewerExecutor` with a fake launcher and asserts on kept files;
these go beside those.

## Definition of Done

- [ ] A zero-finding review's raw answer is kept, in its own directory, named in that reviewer's log line.
- [ ] The round logs what it sent: diff bytes, files, elided, plan bytes, rules bytes.
- [ ] Tests 1–6 written, watched fail, and passing.
- [ ] `research/module_runners.md` records what is kept and what is not, and why an empty answer is
      not an error.
- [ ] `research/RESULTS_*` or this plan carries the 2026-09-08 measurement, so the next person
      reading a silent round has the numbers.
