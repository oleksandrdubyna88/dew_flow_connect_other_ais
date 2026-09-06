# PLAN — an empty vendor answer says why, instead of saying it was empty

> Status: **IMPLEMENTED, 2026-09-06.** An empty answer now carries the vendor's own first stderr line,
> capped at 240 characters with the evidence path still appended; whitespace-only stderr and a
> transcript with no stderr section both keep the original sentence, which is then the whole truth.
>
> Deviations. The classification question was answered by KEEPING `Unparseable`: a refusal that printed
> a sentence has produced an answer we could not use, the repair attempt is still worth one launch (its
> prompt now names the refused-tool case explicitly), and splitting the outcome would have changed what
> every caller counts. Reviewers at the plan gate asked for that rule to be stated rather than left to
> a remark — it is stated here.
>
> One defect was found later, by this product's own gate, and fixed the same day: the evidence was
> chosen by SIZE (`Longer`), so a first attempt that returned a large malformed answer beat a repair
> that returned nothing plus a permission refusal — and the refusal was the whole point. The complaint
> now comes from whichever launch explained itself, starting with the repair; the KEEP path still saves
> the longer transcript on purpose, because a broken envelope leaves a zero-byte file.
>
> Related docs: [module_runners.md](module_runners.md).

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
