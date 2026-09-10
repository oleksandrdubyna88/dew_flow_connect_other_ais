# PLAN — the usage page reads the window, not the history

> Status: **plan only, nothing implemented yet, 2026-09-10.** Scope: `src_server/src/Usage/UsageReader.cs`.
> Finding 10 of [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md) — the
> server half. The extension's own read of its LOCAL ledger (`src_vs_code/src/panelProvider.ts`,
> `readUsage`) has the same shape over a file that is one developer's and small; it is named here and
> not built here.
>
> Related docs: [module_team_server.md](../research/module_team_server.md) — story 2.4.

## The symptom

`UsageReader.Read` (`UsageReader.cs:65-90`) reads `usage.jsonl` from its first byte, parses every line
as JSON, and only then asks whether the line is inside the window (`:84`). The cost of *today* is the
cost of the whole history, and `scope=me` filters after that again. Every open panel asks a Team
server about once a minute.

The audit measured it on a synthetic file, warm: 1 000 old lines, 4–5 ms; 100 000 old lines,
**284–303 ms** — for an answer of zero rows both times. A local microbenchmark, not production
latency, and the slope is the point: linear in the company's history, multiplied by the number of
open panels. The class remark — *"a window of one day never allocates a year"* — is true of memory and
reads as if it were true of time.

## The change

Read from the END. The ledger is appended by one process as jobs finish, so `at` is monotonic up to a
clock step. `UsageReader` walks the file backwards in 64 KiB chunks, splitting on `\n` (a byte that
UTF-8 never places inside a multi-byte sequence), accumulating across as MANY chunks as one line needs
— a record carrying a long vendor sentence can exceed 64 KiB, and a reader that assumes a line spans at
most two chunks reports it as `Unreadable` rather than reading it (gemini, plan round, accepted) — and stops
the first time a line is older than the window's start by more than `UsageReader.OutOfOrderTolerance`
— one hour. A line appended out of order by more than that is a clock that jumped an hour, and what
that would cost is one row missing from one report, which the remark beside the constant says. Lines
inside the window are collected and handed back in file order; a torn last line is still one
`Unreadable`, exactly as now.

`UsageScan` gains `BytesRead`, so a test can assert the shape of the cost rather than a millisecond
figure that depends on the machine.

No new growth surface; nothing is cached, nothing is held between requests.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `UsageReaderTests.AWindowAtTheEndOfALongHistoryReadsOnlyItsOwnTail` — 100 000 lines a year old and three today; three rows back, `BytesRead` under a tenth of the file | the whole point. RED today: `BytesRead` is the file |
| 2 | `UsageReaderTests.ALineStraddlingAChunkBoundaryIsWhole` and `…ALineLongerThanSeveralChunksIsWhole` — 200 KiB in one record | the accumulator, past the two-chunk assumption |
| 3 | `UsageReaderTests.ALineOutOfOrderWithinToleranceIsStillFound` / `…BeyondToleranceIsNot` | the stop rule, both sides |
| 4 | `UsageReaderTests.ATornLastLineIsStillOneUnreadable`, `AnEmptyFileAnswersEmpty` | what the forward reader already promised |
| 5 | The existing `UsageTests` — endpoint and totals | unchanged answers over the small files they write |

## Definition of Done

- [ ] Tests 1–4 written, watched fail for the real symptom, passing; test 5 unedited and green.
- [ ] `module_team_server.md` story 2.4 records the read direction, the tolerance and what it trades.
- [ ] The extension's local read is named as a tail in the extension branch's plan, not left implied.
