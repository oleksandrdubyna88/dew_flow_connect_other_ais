# PLAN — who on the team is spending it

> Status: **plan only, nothing implemented yet.** Scope: the *Company* view of a Team server's
> spending, in the extension's spending tab.
>
> Extracted from story 3.3 of [PLAN_team_server.md](PLAN_team_server.md) when epic 3 shipped: the
> per-server totals and the *Company* toggle are built and tested; the per-PERSON breakdown behind
> that toggle is not, and half-building it would have put a control on screen that answers a question
> it cannot show.
>
> Related docs: [module_team_server.md](../research/module_team_server.md),
> [module_extension.md](../research/module_extension.md).

## The symptom

An admin can already switch the spending tab to **Company** and see what the whole team spent on a
Team server, vendor by vendor. What they cannot see is **who**. The number is the one an operator
looks at when a subscription is close to its ceiling, and "the company used 40M tokens on codex" does
not answer the question that number raises, which is always "doing what, and by whom".

The data is already on the wire. `GET /api/usage?scope=company` answers

```
{window, vendors:[…], people:[{email, name?, vendors:[{vendor, tokensIn, tokensOut, runs, failed, seconds, costUsd?}]}]}
```

and `Usage` in `src_vs_code/src/teamServerApi.ts` declares only the `vendors` half. The server side
shipped in story 2.4 (`src_server/src/Usage/UsageEndpoints.cs`) and needs no change.

## What must be true when it is done

1. An admin who switches to **Company** sees a list of people, each with their own vendor totals,
   ordered by what they spent.
2. A non-admin never sees it, because the server refuses `scope=company` for them with `403` — the
   control is already gated on `catalog.isAdmin`, and that gate stays the client half of a decision
   the server owns.
3. A person with no runs in the window is absent rather than shown as a zero row.
4. Searching narrows the list by email or name, case-insensitively, and searching for nothing shows
   everybody.
5. An email that differs in case from the ledger's is ONE person, not two. Story 2.4 already
   established this on the server; the client must not undo it by grouping on the raw string.

## Build order

1. Widen `Usage` with `people?: readonly PersonUsage[]` (optional: a `scope=me` answer has none), and
   a `PersonUsage` record. Tests over a captured `scope=company` body.
2. A pure `peopleBlock(people, filter, shortNumber)` in `teamServerView.ts`, beside
   `teamUsageBlock` — same bar markup, one sub-block per person.
3. The search box: a `data-command="teamUsagePerson"` input, added to `PANEL_COMMANDS` and the
   provider's switch (the exhaustiveness check will insist), with the current filter in `staticKey`
   so it survives a repaint.
4. Render it under the vendor totals when `usageScope === 'company'`.

## Test plan

RED first. The pure block and the filter are unit-tested with no server; the parse is tested against
a body the real server produced. One test asserts that `Alice@Example.com` and `alice@example.com`
are one row, because that is the defect this will otherwise ship with.

## Definition of Done

- [ ] An admin sees who spent what; a non-admin sees no control at all.
- [ ] The search narrows by email and by name, and clearing it shows everybody.
- [ ] Two spellings of one email are one person.
- [ ] `research/module_team_server.md` records it and this plan is promoted.
