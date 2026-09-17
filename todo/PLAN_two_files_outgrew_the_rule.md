# PLAN — the two files that outgrew the rule

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope: `src_vs_code/src/roundsLog.ts`
> (2016 lines) and `src_vs_code/src/panelView.ts` (2850 lines), against the 800 that
> `common/coding-style.md` allows.
>
> **Every figure below was re-measured on 2026-09-17** and the ones this plan was written with were
> already stale — 1945 and 2795, from a tree several days old, with every derived line range shifted
> by the same drift. A plan whose ranges name the wrong code is a plan that cuts in the wrong place,
> and this one is nothing BUT ranges. (CodeRabbit, on the S5 pull request, which found it by
> checking.) Re-measure again before the first cut: two sessions ship into this repository.
>
> **Blocked until [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md)
> finishes S8.** The operator asked for the split on 2026-09-17, *after* all of that plan's steps —
> and the order is not politeness: S5 and S6 both change these two files, and rebasing a
> thousand-line move across them would throw away the review both halves are getting.
>
> Related: [module_extension.md](../research/module_extension.md).

## The symptom

Two files are more than twice the size the shared rule allows, and one of them is nearly four times
it:

| File | Lines | Exports | Imported by |
|---|---|---|---|
| `panelView.ts` | **2850** | 21 | **32 modules** |
| `roundsLog.ts` | **2016** | 25 | 18 modules |

`coding-style.md` names 800 as the ceiling and 200–400 as typical. Both have been over it long
enough that new work now asks whether to imitate them, which is how a rule stops being one: the
notifications page (S5 of the sibling plan) deliberately does NOT inherit their shape, and that
divergence was recorded as a question rather than answered by copying. This plan is the answer.

**Why it matters beyond the number.** A 2850-line module is one a reader opens and closes again.
The practical cost already showed up twice this month: `escapeHtml` had a **fourth private copy**
written inside `panelView.ts` because importing the real one would have been a cycle — the comment
at `panelView.ts:2297-2300` says so in as many words — and the S5 plan round had to ask whether the
new page should imitate these files at all.

## What the files are actually made of — measured, not guessed

This is the part that makes the split cheap, and it was not obvious until it was measured. **Most
of the length is not logic.**

### `roundsLog.ts` — one template literal is 41% of the file

The page builder `roundsLogHtml` (`:1180`) opens a template literal at **`:1209` that runs to
`:2015` — 807 lines**, and it divides cleanly:

| Lines | What | Size |
|---|---|---|
| 1216–1365 | `<style>` | 150 |
| 1366–1402 | the body markup | 37 |
| **1403–2013** | **`<script nonce>`** | **611** |

The rest of the file is four groups that barely reference each other:

| Group | Symbols | Roughly |
|---|---|---|
| Rows and money | `LogRow` `:38`, `SortKey` `:167`, `LogFilters` `:173`, `rowsFrom` `:205`, `newestFirst` `:243`, `mergedRows` `:261`, `chatRows` `:278`, `repoNameOf` `:633`, `money` `:692`, `cost3` `:711`, `costTitle` `:722`, `Money` `:738`, `Costed` `:741`, `LogView` `:744`, `inView` `:761`, `rowMatches` `:766` | 38–815 |
| Sections | `questionsHtml` `:816`, `usageTabHtml` `:843`, `ROUND_ONLY_FACETS` `:914`, `consultationsHtml` `:969`, `blindSpotsHtml` `:1097` | 816–1178 |
| The page | `roundsLogHtml` `:1180` and its literal | 1180–2015 |
| Embedded in the page | `askedByHtml` `:428` | — |

### `panelView.ts` — a 327-line CSS constant sits in the middle of it

| Lines | What | Size |
|---|---|---|
| **2303–2625** | **`const CSS`** | **322** |
| 397–631 | `panelHtml`'s markup and inline script | 234 |

And five groups that are already separate in everything but file position:

| Group | Symbols |
|---|---|
| State | `PanelState` `:64`, `PanelFocus` `:257` |
| Repaint policy | `SAVE_AFTER_MS` `:271`, `REPAINT_HOLD_MS` `:282`, `withholdsRepaint` `:293`, `OPEN_BY_DEFAULT` `:304`, `staticKey` `:2734` |
| The page | `panelHtml` `:337`, `hoverFor` `:756`, `serverSentence` `:1221` |
| Live regions | `ChatLedgers` `:1974`, `usageRegion` `:2091`, `roundsBody` `:2135`, `roundKey` `:2162`, `statusMark` `:2184`, `liveRegions` `:2254` |
| Commands | `PANEL_COMMANDS` `:2652`, `PanelCommand` `:2711`, `VSCODE_COMMAND_FOR` `:2721`, `isPanelCommand` `:2730` |
| Help | `help` `:2288` and the `HELP` map behind it |

**There is already a precedent inside the file.** `escapeHtml` was moved out to `escapeHtml.ts` and
re-exported from here (`:2297-2300`), for the reason its comment gives: a cycle, and the fourth
private copy that grew because of it. This plan does the same thing five more times.

## What ships

### `roundsLog.ts` → five modules

| New module | Takes | Why it is a unit |
|---|---|---|
| `roundsLogStyle.ts` | the 149-line `<style>` | a string; no logic can hide in it |
| `roundsLogScript.ts` | the 610-line `<script>` | the largest single thing in the file, and the one nobody reads while looking for logic |
| `roundsLogRows.ts` | rows, money, filtering | the data half — it imports no markup and nothing markup-shaped imports it |
| `roundsLogSections.ts` | the four section builders | markup, but not the page |
| `roundsLog.ts` | `roundsLogHtml`, and re-exports | stays the door every caller already knows |

### `panelView.ts` → six modules

| New module | Takes | Why |
|---|---|---|
| `panelStyle.ts` | `const CSS`, 322 lines | the single biggest cut, and pure string |
| `panelHelp.ts` | `HELP` and `help` | prose, changed for its own reasons |
| `panelCommands.ts` | the four command symbols | a closed list the extension registers against |
| `panelRegions.ts` | the live-region builders | what repaints without a rebuild — the thing S5 adds a fourth of |
| `panelRepaint.ts` | the repaint policy and `staticKey` | the rules about WHEN, away from the markup about what |
| `panelView.ts` | `PanelState`, `panelHtml`, and re-exports | stays the door |

**Every original module keeps re-exporting everything it exported**, so none of the 50 importing
modules changes a line. That is not laziness: it is what makes each step reviewable on its own, and
it was proved on `pageTables.ts` in the sibling plan — 18 importers, zero touched.

## The hazards, each of which has bitten this repository

1. **Sonar counts a moved line as NEW code.** A pure refactor once took the quality gate to security
   rating **D**, on a rule fifteen older sites already broke, because extracting a helper into a new
   file made its moved lines "new". The fix is a **scoped** `sonar.issue.ignore.multicriteria` in
   `.github/workflows/sonarcloud.yml`, never `NOSONAR` in the source. Expect it on every step and
   budget for it.
2. **Two functions are embedded into the page by their SOURCE TEXT.** `compareRows` and `rowMatches`
   are pasted in with `.toString()`, and `roundsLog.test.ts:197-198` asserts the exact strings
   `var compareRows = ` + source. Moving such a function between modules is safe — types are erased
   and the emitted source is identical. **Extracting a helper out of one is not**: it would
   reference a name the page does not have, and the page would fail at runtime while every test
   passed. `askedByHtml` (`:427`) is under the same rule and `roundsLog.test.ts:583` asserts it.
3. **A new module that imports `vscode` must be added to `sonar.coverage.exclusions`**, or
   `sonarExclusions.test.ts` fails. None of the eleven modules above should need to — if one does,
   that is a sign the cut was made in the wrong place.
4. **`panelView.test.ts:422` scans the rendered html** for `data-section="([a-z]+) open"`. A module
   split does not touch markup, so this must stay green throughout; if it goes red, something moved
   that was not meant to.
5. **The bundle and minifier tests** run over the built page. They are the second guard on hazard 2.
6. **A parallel session ships into `main` regularly.** A thousand-line move conflicts with
   everything. Rebase before each step, not once at the start.

## Build order — smallest risk first, and measured after each

Each step is its own commit and the whole suite runs after it. **No step is allowed to change
behaviour**; if a test needs editing, the step is wrong.

1. `panelStyle.ts` — 322 lines of CSS out. Nothing can break but the stylesheet.
2. `roundsLogStyle.ts` — 149 lines, same shape.
3. `roundsLogScript.ts` — 610 lines. The largest win and the first one that touches the embedding;
   hazards 2 and 5 apply, and `roundsLog.test.ts` is the guard.
4. `panelHelp.ts`, `panelCommands.ts` — leaves, no dependants inside the file.
5. `panelRepaint.ts`, `panelRegions.ts` — the live-region half. Do it **after** S5 has added its
   fourth region, so the move carries the finished set.
6. `roundsLogRows.ts`, `roundsLogSections.ts` — the last and most tangled cut.

After each step: `wc -l` on both files, recorded in the commit. The number going down is the point.

## Test plan

There are no new behaviours, so there are no new behavioural tests. The test plan is the guard:

| # | What | Why |
|---|---|---|
| 1 | **The whole suite passes unchanged after every step.** Not "passes" — *unchanged*: no test edited, no assertion relaxed. | A refactor that needs a test changed is not a refactor. |
| 2 | `roundsLog.test.ts`'s `var compareRows = <source>` and `var askedByHtml = <source>` assertions. | Hazard 2, and the only thing standing between a moved function and a page that fails silently. |
| 3 | The bundle and minifier tests. | Hazard 5. |
| 4 | `sonarExclusions.test.ts`. | Hazard 3. |
| 5 | **A line count assertion**, added in step 1 and tightened as the work proceeds: no module in `src_vs_code/src` over 800 lines. | Otherwise this plan is done when somebody says it is, and the files grow back. It also stops the next file reaching 1900 unnoticed. |
| 6 | SonarCloud's new-code rating on each PR. | Hazard 1, which is invisible locally. |

## Definition of Done

- [ ] No module in `src_vs_code/src` exceeds 800 lines, and a test says so.
- [ ] `roundsLog.ts` and `panelView.ts` still export everything they exported; no importing module
      changed.
- [ ] Every step is its own commit, with the two line counts in its message.
- [ ] The whole suite passed unchanged at every step — no test edited to accommodate a move.
- [ ] The `.toString()`-embedded functions still match their asserted source exactly.
- [ ] Any Sonar new-code finding caused by a move is answered with a scoped ignore in the workflow,
      never with `NOSONAR`.
- [ ] `research/module_extension.md` describes the new shape.

## The boundary with the notifications plan

> Reciprocal of the *Who builds what* table in
> [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md), which is
> MANDATORY on both sides — a boundary named once is not a boundary.

**That plan goes FIRST, all of it.** Its S5 adds a fourth live region to `panelView.ts` and a new
page; its S6 changes `roundsLog.ts`'s tab handler and `rowMatches`' haystack. Both land in the
files this plan moves, and a move rebased across them loses the review they are getting.

**This plan owns the file sizes; that one owns what the files DO.** The notifications page is built
at under 400 lines a module from its first commit precisely so that it is not part of this backlog —
it is the shape this plan is moving the other two towards, which is also why it was allowed to
diverge from them rather than imitate them.

## What this plan does NOT do

- **It changes no behaviour.** Not one sentence of markup, not one comparison, not one default.
- **It does not split `roundsLog.ts`'s page into two pages**, or `panelView.ts`'s panel into two
  panels. The product is untouched; only where the source lives changes.
- **It does not rewrite the neighbouring code it moves.** `reuse-first.md` is explicit that
  rewriting code you were not asked to change turns a small diff into one nobody can review. A
  badly-written function moves as it is, and is named in the summary if it should be redone.
