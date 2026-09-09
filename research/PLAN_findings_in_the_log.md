# PLAN — findings inside an expanded row of the rounds log

> Status: **IMPLEMENTED, 2026-09-09.** Scope as built: `src_mcp/src/Store/RoundsQuery.cs`,
> `src_vs_code/src/{roundsDbRead.ts, roundsLog.ts}`, their tests.
>
> Related docs: [PLAN_rounds_log_view.md](PLAN_rounds_log_view.md) — the page this extends;
> [module_extension.md](module_extension.md), [module_server.md](module_server.md).

## Deviations — it shipped through a different door than it was written for

This plan was written against the SESSION FILES: read `pending` out of the current round's JSON and
render it. That is not what shipped, and the reason is its own gate. Findings 1 and 3 said `pending`
belongs to the CURRENT round and is EMPTIED by `resolve`, so a row would show two findings under a
header saying four, or none at all — the source could not answer the question the plan asked of it.
Finding 4 demanded a number for the size of keeping findings per round.

The answer to both was [PLAN_local_db.md](PLAN_local_db.md): the server keeps every finding, its
resolution and its reason in `coai.db`, and `RoundsQuery` reads them back per round. So
`LogRow.found` is fed by the DATABASE, `SessionFile.pending` was never read, and no session file grew
at all. The renderer is `foundHtml` in `roundsLog.ts`, under the expanded row, exactly where this
plan wanted it.

It then took one more fix to be visible to anybody:
[PLAN_the_log_never_gets_its_findings.md](PLAN_the_log_never_gets_its_findings.md). The list was
built, correct, and thrown away by a launcher that took nineteen seconds to close.

## Open tail — handed to another plan, not dropped

DoD item 2, *"a round without kept findings says so"*, is NOT built: a round recorded before the
database existed renders an empty area, and an empty area reads as "clean". Its own gate named this
as finding 6 — *"not kept" needs its own visible element* — and it is still true. It is taken over by
[todo/PLAN_the_log_asks_for_a_page.md](../todo/PLAN_the_log_asks_for_a_page.md), which has to answer
it anyway: once findings are fetched when a row is opened, *not yet asked for* becomes a fourth state
that also looks like a blank.

## The gap

The rounds log ([PLAN_rounds_log_view.md](../research/PLAN_rounds_log_view.md), shipped 2026-09-05) expands
a row to its reviewers — provider, role, status, how many findings, how long, what it read. It does not
show the findings themselves. Reading "local/SecurityReliability — done (4 findings, 26 s)" and wanting to
know WHICH four still means opening the session file, or the resolve step's answer, by hand.

## What is known

- The server's session file carries `pending` — the unresolved findings of the CURRENT round, each with
  severity, category, file, line, title, why, fix and the providers that raised it. Resolved findings of
  earlier rounds are not kept per round today; a round record has counts, not findings.
- `parseSession` ([rounds.ts](../src_vs_code/src/rounds.ts)) types `SessionFile` without `pending`; the
  extension has never read it.
- The escalation file ([escalations.ts](../src_vs_code/src/escalations.ts)) already carries
  `openFindings` in the same shape, and the page renders those under an open question — one renderer
  exists to reuse.

## What must be true when this is done

1. An expanded row of the newest round of a session lists its pending findings: severity, category,
   `file:line`, title, and who raised it — the same shape the question block uses.
2. A round whose findings the file does not carry says so ("findings are not kept for this round")
   rather than showing an empty list that reads as "none".
3. If keeping every round's findings requires the SERVER to write them into the round record, that is a
   server change with its own tests, and this plan says so before touching the file format: a record
   that grows without bound is a session file that stops being small.

## What the gate found, 2026-09-06 (three reviewers, six findings taken)

1. **`pending` belongs to the CURRENT round**, so exposing it from any row would show round B's
   findings under round A and contradict A's own count. Until findings are kept per round, only the
   newest round may render them.
2. **Absent, empty and non-empty are THREE states** and the plan collapsed the first two: a clean
   round would be told its findings were "not kept". Model it as a tri-state and assert all three.
3. **Resolving empties `pending`** — so an expanded row would show two findings under a header that
   says four, or none at all. Either the round record keeps them, or the section is labelled
   *unresolved* and explains the difference. This is the defect that would have shipped.
4. **The size decision needs a budget**, not a look: name the worst case (findings per round times the
   length of a why/fix pair) and the threshold, so the answer is a number.
5. **That decision moves to step 1.** If it says the findings belong in the round record, steps 1 and
   2 have built a row model against the wrong source.
6. **"Not kept" needs its own visible element.** A model returning undefined and a renderer that draws
   nothing for undefined produce a blank that reads as "clean".

## Build order

1. RED: `rowsFrom` exposes `findingsList` for a round whose session carries `pending`, and an honest
   absence for one that does not.
2. `SessionFile.pending?` in `rounds.ts`; the page's `detail()` renders the list through the same markup
   as the question block.
3. Decide, with a measurement of session-file sizes on this machine, whether per-round findings belong
   in the record; if so, a server plan.

## Test plan

`npm test`: the row model with and without `pending`; escaping of a finding's title and file; the
"not kept" wording. Manual: expand the newest row after a round with findings.

## Definition of Done

- [x] An expanded row shows the round's findings — from the database, not from `pending`.
- [ ] ~~A round without kept findings says so~~ — **not built**, handed to
      [todo/PLAN_the_log_asks_for_a_page.md](../todo/PLAN_the_log_asks_for_a_page.md). See *Open tail*.
- [x] The server-side question is answered in writing: findings live in `coai.db`, so the session
      file does not grow at all. See *Deviations*.
