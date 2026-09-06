# PLAN — an empty vendor answer says why, instead of saying it was empty

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/runners/Reviewers/ReviewerExecutor.cs`
> and its tests.
>
> Related docs: [module_runners.md](../research/module_runners.md).

## The symptom, observed rather than imagined

On 2026-09-06, the code round for the Team server's story 2.4 ran nine reviewers and **three of them
failed identically**:

```
gemini/Architecture:        unparseable: the vendor returned an empty answer after one repair attempt
gemini/SecurityReliability: unparseable: the vendor returned an empty answer after one repair attempt
gemini/UxDxPerformance:     unparseable: the vendor returned an empty answer after one repair attempt
```

A third of the round produced nothing, and the sentence a person is given says only that the answer
was empty — not *why*. So the obvious next move is to run it again, which reproduces it, which is how
an afternoon goes.

The reason is usually IN the CLI's stderr — a denied permission, a model that is not on the account, a
sign-in that has lapsed. The evidence file keeps it
(`%LOCALAPPDATA%/coai-mcp/unparseable/gemini-Architecture-*.txt`), so the information is not lost; it
is simply not where the person looking at the failure is looking.

## Where it comes from

`ReviewerExecutor` classifies a run that exits **zero** with **empty stdout** as `Unparseable` with a
fixed reason. When the CLI has also written a complaint to stderr, that complaint is what the person
needs and it is dropped from the reason.

Raised by codex on that same round, against `ReviewerExecutor.cs:254`, while reviewing an unrelated
change — which is why it is written down here rather than folded into a server story it has nothing
to do with.

## What to do

1. Reproduce it deliberately: run `agy` with a permission it does not have, capture the real stderr,
   and pin that text in a test. Observed phrases, never imagined ones — the discipline
   `RateLimit.Phrases` and `CooldownParser` already follow.
2. When stdout is empty and stderr is not, the reason becomes the vendor's own words, capped the way
   `StdErrTail` already caps them, with the evidence path still appended.
3. When BOTH are empty, keep the current sentence — it is then the whole truth.
4. Check whether this class of failure should be `NotStarted` rather than `Unparseable`: a CLI that
   refused before it began has not produced an unparseable answer, it has produced none, and the two
   lead a reader in different directions.

## Test plan

RED first. A fake CLI that exits 0, writes nothing to stdout and a known complaint to stderr; assert
the complaint reaches the outcome's reason. Then the both-empty case keeps today's sentence. Then the
whole `CoaiMcp.Tests` suite, because this file is on every reviewer's path.

## Definition of Done

- [ ] A vendor that fails with an explanation shows the explanation, not "empty answer".
- [ ] The pinned stderr text is one somebody captured.
- [ ] The both-empty case is unchanged.
- [ ] The `Unparseable`-versus-`NotStarted` question is answered in the code's remarks either way.
