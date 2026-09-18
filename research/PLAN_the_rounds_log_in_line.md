# PLAN — S6: the rounds log brought into line

> Status: **IMPLEMENTED, 2026-09-17.** Scope: `src_vs_code/src/roundsLog.ts` and its tests.
>
> **Deviations, and the one that matters.** This plan's first draft said there are THREE
> `<section id="tab-*">` and that the tab strip's five tabs map onto them. There are FOUR, and the
> fourth is the table — which answers to two tabs and is toggled by a line of its own. The invariant
> test, written before any code, went red naming it. The design changed as a result: the derived
> loop runs BEFORE the table's line rather than replacing it. Everything else shipped as written.
>
> The code round then added two things this plan did not ask for: a section marked and nothing else,
> spliced into the page the harness runs, because every real section is reachable by a literal id
> too and the other tests would have passed against a dead query; and a marker-vs-tab agreement in
> the invariant, because a section marked `audit-log` while the strip says `auditLog` is a section
> nothing can ever show. The scan also parses attributes in any order and ids with hyphens — the
> first version matched neither, and the HARNESS had the same hole, which is why the marker-only
> test failed the first time it ran.
>
> Related: [PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md)
> (this is its S6), [module_extension.md](module_extension.md),
> [module_tests.md](module_tests.md).

## Who builds what

| Slice | Owner | Order |
|---|---|---|
| The ledger, the funnel, the panel count, the notifications page, the durable write gap | [PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md), S1–S5 | shipped 2026-09-17 |
| **The rounds log's tab handler and its search** | **this plan (S6)** | **after S5, before S7** |
| Three extension-side defects, including `helpPanel.ts` | the sibling plan, S7 | after this |
| The server half of the ledger | the sibling plan, S8 | after S7; needs a `coai-mcp` release |
| SPLITTING `roundsLog.ts` and `panelView.ts` | `todo/PLAN_two_files_outgrew_the_rule.md` — written on the S5 branch, so it is NAMED here rather than linked until that reaches `main` | blocked until S8 |

Disjoint by construction: this plan changes two things inside `roundsLog.ts` and adds tests. It does
not move a line of it, because the file's SIZE belongs to the split plan and a thousand-line move
rebased across this work would throw away the review both are getting.

## Why this exists

On 2026-09-16 a deleted role called `Role2` went on being reviewed against for ninety minutes,
because the one warning about it was a toast nobody saw. S1–S5 built what answers that. The
notifications page was deliberately written NOT inheriting two habits of the older rounds log, and
the operator asked on 2026-09-16 for the older page to be brought into line rather than left as the
counter-example. Two changes and no others.

## What the sibling plan said, and what is actually there

Its S6 paragraph said the handler "derives its sections from `[data-section]` instead of naming four
ids literally (`roundsLog.ts:1585-1588`)" and that `rowMatches`' haystack is at `:820`. Measured
2026-09-17:

| The paragraph said | What is there |
|---|---|
| the handler at `:1585-1588` | `:1782-1784` |
| `rowMatches`' haystack at `:820` | `:807` |
| "four ids literally" | THREE ids in the hide loop — and **four** `<section id="tab-*">`, the fourth being the table |

**The first draft of THIS plan got the last row wrong in the other direction**, and it is worth
recording because of how it was caught. It claimed only three sections exist and that the strip's
five tabs map onto them; the invariant test written before any code found `<section id="tab-rounds">`
at `:1417` and went red naming it. The fourth section is the TABLE, which answers to two tabs
(`rounds` and `conversations`) and is toggled by a line of its own that also clears the round-only
facets. A plan measured from a grep, corrected by a test written before the code.

## Change 1 — the handler derives its sections

Every one of the four sections carries `data-section="<tab>"`, and the handler hides each one whose
marker is not the chosen tab.

**The derived loop runs BEFORE the table's own line, which follows and wins.** The general rule gets
the table wrong — it answers to two tabs — so the exception has to run last. The other way round the
table would vanish the moment somebody chose `conversations`, and the page would be blank.

## Change 2 — the search reaches what the row SAYS

`rowMatches` joins `row.answered` into its haystack. That is the sentence the **Reviewers** column
renders — "all 3 reviewers answered", or the one model a conversation was with — and it is set on
BOTH kinds of row (`:344` for a conversation, `:475` for a round), so nothing is conditional.
Without it, typing a role's name does not find the rounds whose Reviewers cell says it, which is the
incident this whole plan is about: a role nobody could find.

## Growth surface

**None.** No new file, no new record, no new column, nothing retained. One attribute per section in
markup that is rebuilt on every render, and one more string in an in-memory haystack.
`common/planning-docs.md` asks for this line from any plan that creates a growth surface; this one
says so rather than leaving a reader to check.

## Test plan

```bash
cd src_vs_code
npm ci        # first time, or after a rebase onto a main with new dependencies
npm test
```

Node 22 on Windows, the version `package.json` engines names. `npm test` cleans `out/` first, which
matters: a stale `out/` runs both names after a rename and inflates the count.

| # | Test | Why it has teeth |
|---|---|---|
| 1 | Choosing a tab shows its section and hides the others, **in both directions** — and the page ASKED for `[data-section]`. | The harness keys a section stub by its own id, so the first three assertions pass against the three literal ids too. The recorded selector is what makes it about DERIVING. |
| 2 | The table survives the `conversations` tab and goes when a real section is chosen. | This is the ordering. With the loop after the table's line, the table is hidden for `conversations` and the page is blank. |
| 3 | Every `<section id="tab-*">` carries a matching `data-section`. | Deriving only helps while the marker is there; a section added without one is invisible again and test 1 would stay green through it. |
| 4 | A search for a phrase only in the Reviewers cell finds its row, with a control that a real miss still misses. | The existing search tests use a subject or a branch, which were already in the haystack. |
| 5 | Every existing rounds-log test passes. | Stated as a condition, not as evidence — which is why 1–4 exist. |

## Definition of Done

- [ ] Both changes land with a RED-first test each, and both observations — the failure message and
      the pass — are reported.
- [ ] The whole suite is green.
- [ ] Any existing assertion that had to change is named in the summary, with why, and with the
      statement that it was not weakened.
- [ ] The stale references in [PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md)
      are corrected in the same change, and its S6 paragraph points here.
- [ ] `research/module_extension.md` records that the rounds log derives its sections and that its
      search reaches the Reviewers sentence; `research/module_tests.md` gains the new tests and says
      what they do not prove.
- [ ] **Promotion, in this order** (`common/planning-docs.md`): rewrite the status line to
      `IMPLEMENTED <date>` with the deviations; fix the relative links in both directions; update the
      inbound references in the sibling plan and in any `.cs`/`.ts` comment; update the *Currently
      open* table in [README.md](../todo/README.md); `git mv` the file to `research/` LAST; then run
      `node .agents/conventions/tools/plan-lifecycle.mjs`.
- [ ] The plan went through `review_plan` and the code through `review_code`.

## What this does NOT prove

No test here drives the real extension. A webview page is tested by RUNNING its script against a
shim built from its own markup, and that shim throws on a selector it does not model — which is what
these four tests do, and it is not the same as pressing a tab in an editor.

**And the reason given for that at the time has since expired, which is worth recording rather than
quietly leaving in place.** A reviewer asked for scenario flows through the installed extension and
was told there was no harness to add them to: `research/module_tests.md` had called the absence of an
extension-host harness its largest single gap. That was true when this plan was written and stopped
being true hours later — `a real editor runs the extension, for the first time` landed on `main` the
same day, and CI now runs `extension · a real editor` over the shipped extension in
`@vscode/test-electron`. Its first scenario asserts that every command the manifest declares is
really registered.

So the honest statement now is about COST rather than possibility: driving the rounds log's tabs
through a real editor is buildable, it is not built here, and the harness's own rule is that a
scenario earns its seconds only where a value cannot reach. Whether a tab press belongs there is a
question for whoever adds the second scenario.
