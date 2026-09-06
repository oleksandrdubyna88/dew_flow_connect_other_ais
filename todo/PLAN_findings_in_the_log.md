# PLAN — findings inside an expanded row of the rounds log

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/{rounds.ts, roundsLog.ts}` and
> possibly `src_mcp/src/Server/SessionStore.cs`, their tests.
>
> Related docs: [PLAN_rounds_log_view.md](../research/PLAN_rounds_log_view.md) — the page this extends;
> [module_extension.md](../research/module_extension.md), [module_server.md](../research/module_server.md).

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

- [ ] An expanded row shows the pending findings of the session's current round.
- [ ] A round without kept findings says so.
- [ ] The server-side question is answered in writing, with a number.
