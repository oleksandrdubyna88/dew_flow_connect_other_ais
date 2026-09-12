# PLAN — the summed time says it is a sum, and names an average and a longest

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/usage.ts` (one field on
> `VendorTotals`), `src_vs_code/src/panelView.ts` (`usageRegion`'s totals line) and the tests for
> both.
>
> Issue [#116](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/116): *"at the
> bottom we show the sum of worked time. 1. it needs to say this is all agents added up. 2. show the
> average. 3. show the largest by agent."*

## The symptom

The spending tab of the rounds log page ends with one line
(`usageRegion`, `src_vs_code/src/panelView.ts:1347`):

```
All vendors: 1.2M tokens · $4.05 · 2.0 h
```

`2.0 h` is the sum of every reviewer run in the window across every vendor, and nothing on the line
says so. Read beside the per-vendor cards above it — each of which already ends in
`38 s total · 12 s average` — it reads as though it might be elapsed wall-clock time for the window,
which it is not: three reviewers running in parallel for ten minutes contribute thirty minutes here.
The operator asked for the three things that make it unambiguous: say it is a sum over agents, give
the average, and name the agent with the most.

## What must be true when this is done

1. The line says the time is **all agents added together** — in words, not by implication.
2. It shows an **average**. The average is per RUN, not per agent: the per-vendor cards already say
   `x average` meaning per run, and two averages on one page meaning different things is worse than
   no average at all.
3. It names the **agent with the most time** and how much — `codex the longest at 1.1 h`. The agent's
   own name, because "the largest by agent" is a question about which one.
4. Every number stays derived from the rows already on screen: the same window, the same
   forget-marks. No new data source, no second pass over the ledger.
5. The money and token halves of the line are untouched.

## The change

- `usage.ts` — `VendorTotals` already carries `runs` and `seconds` per vendor, so the average needs
  no new field. **`totalsByVendor` gains nothing.** The accumulator in `usageRegion`
  (`panelView.ts:1333`) gains `runs: t.runs + r.runs`, and the longest agent is
  `rows.reduce((most, r) => (r.seconds > most.seconds ? r : most))` over the same `rows` — which is
  also where its NAME comes from.
- `panelView.ts:1347` — the line becomes, for example:

  ```
  All vendors: 1.2M tokens · $4.05 · 2.0 h summed across 2 vendors · 24 s average per run · codex longest at 1.1 h
  ```

  **"vendors", not "agents".** The line already opens `All vendors:` and every card above it is a
  vendor; the operator's word for the same thing is *агент*, and using both on one line would invent
  a distinction this data does not have (raised on the plan round). A row IS the unit — `codex`,
  `local`, `remsoftdev-claude` — and a reviewer run belongs to exactly one.
- **The longest clause is computed conditionally, not computed and then hidden**:
  `rows.length > 1 ? rows.reduce(…) : null`. That is what the single-vendor case asks for, and it
  also removes the empty-array reduce entirely rather than relying on an early return elsewhere in
  the function staying where it is (raised on the plan round by two vendors).
- **A tie is resolved deterministically and says so in a comment.** `r.seconds > most.seconds` keeps
  the FIRST row of equal value, and `totalsByVendor` sorts busiest-by-tokens first, so the same data
  always names the same vendor. Not a policy anybody would guess — hence a test.
- The average divides by the summed `runs` with an explicit `runs === 0` guard. It cannot be zero for
  a vendor that has a row, and the guard costs one expression against a page that would otherwise
  render `Infinity`.
- `research/module_extension.md` — the spending tab's paragraph.
- `CHANGELOG.md` under `## Unreleased`; the release commit for this batch names the version.
- This plan promoted to `research/` in the branch's last commit.

## Test plan (RED first)

| # | Test (`src_vs_code/src/test/recentAndForget.test.ts`, beside the other `usageTabHtml` tests) | RED symptom expected |
|---|---|---|
| 1 | *the total says it is a sum over agents, and gives an average per run* — with two vendors of known runs and seconds, the line contains the summed duration described as agent time in total, and the average equals sum ÷ runs | the line is `All vendors: … · 2.0 h` with no words and no average |
| 2 | *the longest agent is named with its own time* — the vendor with the most seconds appears by name with its duration, and the OTHER vendor does not appear as the longest | no such clause |
| 3 | *one vendor is not "the longest"* — with a single vendor in the window the line carries the total and the average and **no** longest clause. Proved with teeth rather than assumed: the clause is made unconditional on purpose and this test watched go red, then restored. Raised on the plan round — a test whose expected symptom is "green before and after" cannot catch the regression it names | the clause appears for the only vendor |
| 4 | *a tie names the same vendor every time* — two vendors with identical seconds render the same name on repeated calls, and it is the first in the page's own row order | (pins a policy nobody would guess) |
| 5 | *every number on the line comes from the rows the cards come from* — the summed seconds equal the sum of the per-card durations, so a filter applied upstream (a forget-mark, the window) cannot reach the cards and miss the total. Raised on the plan round: the forget-marks are applied by `panelProvider` before this function is called, so a unit test cannot set one — what it CAN pin is that there is one source | (structural; the guarantee the finding is really about) |
| 6 | the existing assertion that the totals line carries tokens and money is unchanged | green before and after |

There is **no existing test asserting the `All vendors:` line at all**, so tests 1 and 2 are new
coverage rather than an edit to an existing guarantee.

Run, from a checkout with the extension's dependencies installed (`npm ci` in `src_vs_code`, Node 22
as CI uses): `cd src_vs_code && npm test` — the whole suite; the region is rendered into the rounds
log page, so `roundsLogPage.test.ts` and `bundledPage.test.ts` read it too.

## Definition of Done

- [ ] Tests 1 and 2 written first and watched fail; then green; then red again with the fix reverted.
- [ ] `npm test` green in the worktree; the count reported in the pull request.
- [ ] The diff through the `coai` code round, every finding resolved.
- [ ] `research/module_extension.md` and `CHANGELOG.md` updated as named above.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
