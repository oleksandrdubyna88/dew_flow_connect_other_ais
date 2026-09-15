# PLAN — a question with a long path in it wraps instead of scrolling the conversation sideways

> Status: **IMPLEMENTED, 2026-09-15.** Kind: **bug**. Scope: three CSS rules in
> `src_vs_code/src/chatPage.ts` and their tests. Origin:
> [issue #299](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/299) —
> *"должен быть врап текста, а не прокрутка вправо"*.
>
> Related docs: [module_extension.md](module_extension.md), [module_tests.md](module_tests.md).
>
> ## Deviations — what shipped differently, and why
>
> 1. **`anywhere`, not `break-word`.** The draft chose `break-word` to match three siblings on the
>    same page. The plan round pointed out the one way the keywords differ — `break-word` does not
>    shrink a box's intrinsic min-content width, `anywhere` does — so `break-word` leaves the
>    scrollbar wherever a box is sized BY its content. **The round's stated mechanism did not apply
>    here** (`#scroll` is a flex item of a *column* container, so its automatic minimum is on height;
>    `.msg` is a plain block), and it was taken anyway because it is free, because `panelView.ts`
>    already settled on it for this symptom, and because it ends the question rather than leaving it
>    to be re-derived.
> 2. **A third rule was added**, which the plan did not have:
>    `.msg .what pre, .msg .what table { overflow-wrap: normal; }`. `overflow-wrap` is inherited, and
>    the table is `display: block; overflow-x: auto` with cells that *do* wrap — the inherited wrap
>    would have re-flowed its columns and removed the horizontal scroll the rule exists for.
> 3. **The third test was described as a guard that would be green from the start. It was not** — it
>    went red with *"there is no rule for .msg .what pre, .msg .what table"*, because the exclusion it
>    asserts is new code. Half red test, half regression guard; the plan now says so rather than
>    claiming a guard was "watched failing".
>
> **Checked, and not done:** that text actually wraps. The page harness executes a page's script
> against a DOM shim with **no layout engine**, so `scrollWidth` and computed styles are numbers the
> test sets — there is nothing in this repository that can observe wrapping or the absence of a
> scrollbar. What was NOT checked: the rendered result in a real VS Code webview; the declarations
> are asserted and the behaviour is inferred from them. Recorded as an uncovered flow in
> `module_tests.md`.

## The symptom

The prompt shown above an answer runs off the right-hand edge and the conversation gets a horizontal
scrollbar. It should wrap.

## The cause — two rules that wrap only at spaces, in a box that then scrolls

`white-space: pre-wrap` preserves newlines and wraps **only at existing break opportunities**, which
in practice means spaces. A run with no space in it — a Windows path, a URL, a stack frame, a
base64 blob, an id like `preset-mtwxbymp-4` — has no break opportunity at all, so it cannot wrap and
it overflows its box.

Two rules in `chatPage.ts` are in that state:

- **`chatPage.ts:844`** — `.msg .what { color: var(--coai-read); line-height: 1.55; }`, the body of
  every message, with `.msg.you .what` adding `white-space: pre-wrap` at **`chatPage.ts:862`**.
- **`chatPage.ts:825`** — `.passage { … white-space: pre-wrap; opacity: .85; }`, the captured code a
  question is asked about.

The overflow becomes a horizontal SCROLLBAR rather than just spilling, because of the layout two
rules above: `body { … overflow: hidden }` (`chatPage.ts:750`) means the page itself cannot scroll,
and `#scroll { flex: 1 1 auto; min-height: 0; overflow-y: auto; … }` (`chatPage.ts:755`) sets only
the Y axis. Per CSS, `overflow-x: visible` **computes to `auto`** when the other axis is not
`visible` — so `#scroll` becomes horizontally scrollable and the whole column slides sideways.

**The question is the widest thing on the page**, which is why it is what got reported: a `you`
bubble is not what the person typed, it is the composed prompt — the instruction, the language line,
the `--- the text ---` fence and the **whole captured passage verbatim**, which is usually code.

## Why this is a one-property fix and not a design

**Three siblings on this very page already carry the answer**, and they are the well-written
neighbours this should follow rather than invent past:

| Rule | file:line | has |
|---|---|---|
| `.askedText` | `chatPage.ts:823` | `white-space: pre-wrap; overflow-wrap: break-word;` |
| `#say` (the composer) | `chatPage.ts:926` | `overflow-wrap: break-word;` |
| `#ghost` | `chatPage.ts:930` | `white-space: pre-wrap; overflow-wrap: break-word;` |

The sidebar solved the same defect the same way: `.round .line` (`panelView.ts`) is
`white-space: normal; overflow-wrap: anywhere`.

**`anywhere`, not `break-word` — changed by the plan round.** The draft chose `break-word` to match
the three siblings above. The round pointed out that the two keywords differ in one way that matters:
`break-word` does **not** affect a box's intrinsic **min-content** width, and `anywhere` does. In a
context where a box is sized BY its content — a flex or grid item with `min-width: auto`, a float, a
table cell — a long unbreakable run keeps the box as wide as the run, and the scrollbar survives the
fix.

The round's stated mechanism does not actually apply here, and it is worth writing down why rather
than accepting it silently: `#scroll` is a flex ITEM of `body`, whose `flex-direction` is `column`,
so its automatic minimum size is on the MAIN axis — height, which is exactly why the existing comment
at `chatPage.ts:751-754` sets `min-height: 0` and says so. Nothing between `#scroll` and
`.msg .what` is a flex container: `.msg` (`chatPage.ts:828`) is a plain block with a `max-width`. In
block layout `break-word` would have wrapped.

`anywhere` is taken anyway, because it is free, because it is what the sibling surface already uses
for this exact symptom, and because it makes the question moot instead of an argument that has to be
re-derived by the next reader of this rule.

## What must NOT change

- **`.msg .what pre` keeps scrolling.** `chatPage.ts:871` is
  `.msg .what pre { … overflow-x: auto; … }` with the reason written above it: *"Its own box, and it
  scrolls inside it: a long line of code must not widen the page."* That is a recorded decision and
  this change must not quietly reverse it. It will not: `overflow-wrap` is inherited, but it has no
  effect where `white-space: pre` forbids wrapping at all, so a `<pre>` keeps its own scrollbar.
  **The test asserts this rather than assuming it.**
- `.msg .what table` (`chatPage.ts:880`) scrolls for the same reason and is untouched.
- The fold (`.msg.long .what`, `chatPage.ts:851`), the sides, the colours and the max-width measure.

## What must be true when it is done

1. A question containing a long unbreakable run wraps inside its bubble.
2. The captured passage wraps the same way.
3. A fenced code block inside an ANSWER still scrolls in its own box rather than wrapping.
4. The conversation has no horizontal scrollbar as a result of ordinary text.
5. Nothing else about the page's layout, folding or colours changes.

## The design

Two rules gain `overflow-wrap: anywhere`, and a third rule is ADDED to take it back where it must
not apply:

- `chatPage.ts:844`, `.msg .what` — the shared rule, so the ANSWER gets it too. A model's answer can
  carry a long URL in a paragraph and would overflow identically; fixing only the `you` side would
  leave the same defect behind under a different role, which is the "a decision applied at SOME of
  its sites" failure `reuse-first` names.
- `chatPage.ts:825`, `.passage`.
- **New: `.msg .what pre, .msg .what table { overflow-wrap: normal; }`**

### Why the third rule exists (plan round, codex)

`overflow-wrap` is an **inherited** property, and the two boxes that scroll on purpose are inside
`.msg .what`:

- `.msg .what pre` (`chatPage.ts:871`) — safe by accident, because `white-space: pre` forbids
  wrapping and `overflow-wrap` has nothing to act on. Safe by accident is not safe: the day somebody
  sets `white-space: pre-wrap` on it, the code starts breaking mid-token and the recorded decision is
  gone with no test failing.
- `.msg .what table` (`chatPage.ts:880`) is `display: block; overflow-x: auto` — a real scrolling
  box whose cells **do** wrap. Inheriting `anywhere` into `th`/`td` would break long cell contents,
  re-flow the column widths and quietly remove the horizontal scroll the rule was written for.

So the exclusion is declared rather than reasoned about, and asserted.

## Growth budget

Nothing is created, stored, spawned or cached. No growth surface.

## Build order

1. **RED** — the three tests below, watched failing.
2. The two properties.
3. **GREEN** — the same tests, then the whole suite.
4. `research/module_extension.md` — prose only. **No diagram changes.** Its three Mermaid figures are
   a settings-flow chart (`:900`), the adapter seam (`:1575`) and the conversation's state machine
   (`:1744`); none depicts page layout or a scrollbar, so there is nothing in them this change can
   make stale. Said explicitly because the plan round asked for it either way.
5. `research/module_tests.md` — the uncovered flow above.
6. `CHANGELOG.md` under the 0.47.0 heading, naming issue #299, in the house format (a bold lead
   sentence, then what changed and why).

## Test plan

Home: `src_vs_code/src/test/chatPage.test.ts`, which already parses the stylesheet into rules —
`rules()` at `:255` and `ruleFor()` at `:267` — and asserts on one rule at a time (`:1326` does
exactly this for `.msg .what`). There is **no `overflow-wrap` assertion anywhere in that file
today**, so all three are new ground.

The round was right that a guard cannot be "watched failing", and that a build order claiming it was
is one nobody followed. **Observed, once the tests were actually run: all three go RED**, and the
third for a reason worth writing down — it asserts a rule that does not exist yet
(`there is no rule for .msg .what pre, .msg .what table`), because the exclusion is NEW code. Its
`overflow-x: auto` half was green before and after; only its new half could fail. So it is half a
red test and half a guard, and the honest statement is that sentence rather than either label.

RED (both fail against today's stylesheet):

1. `'a question with no spaces in it wraps instead of widening the page'` —
   `ruleFor(css, '.msg .what')` matches `/overflow-wrap: anywhere/`.
2. `'the captured passage wraps too'` — the same for `ruleFor(css, '.passage')`.

NEW RULE + GUARD (its first half is new code and goes red; its second half is what stops a later
change reversing the recorded decision):

3. `'the boxes that scroll on purpose do not inherit the wrap'` —
   `ruleFor(css, '.msg .what pre, .msg .what table')` matches `/overflow-wrap: normal/`; and, pinning
   the whole condition rather than half of it, `ruleFor(css, '.msg .what pre')` still matches
   `/overflow-x: auto/` **and does not match `/white-space: pre-wrap/`**. The last clause is the one
   the round asked for: a `pre` could keep `overflow-x: auto` while its `white-space` changed, and
   the code would start wrapping with every other assertion still green.

Running them alone, before the whole suite:
`node --test out/test/chatPage.test.js` (after `npm run compile`). The suite has no per-test filter
flag — `scripts/run-tests.mjs` hands every `out/test/*.test.js` to `node --test` — so one FILE is the
unit of isolation here.

4. Whole suite: `cd src_vs_code && npm test`. Baseline on this branch's base: 2944 tests, 2943 pass,
   1 skipped, 0 fail.

### What these tests do NOT prove, said here rather than implied

No test in this repository can observe that text actually wraps. The page harness
(`bundledPage.test.ts`) executes the page's SCRIPT against a hand-written DOM shim; it has **no
layout engine**, so `scrollWidth`, `clientWidth` and computed styles are numbers the test itself set.
A test asserting "no horizontal overflow" against that shim would assert something the shim decided
and would LOOK like evidence while proving less than the declaration does. Observing the real thing
needs a browser — a new toolchain dependency, out of scope for a two-property CSS fix. Recorded as an
uncovered flow in `research/module_tests.md`.

Source-text assertion is legitimate here for the reason the ruling itself gives: a stylesheet
declaration is data, not a program, and there is no branch for a substring to miss. `rules()` refuses
a sheet containing `@media`/`@supports` (`chatPage.test.ts:256`), which is the case that would make
reading-order ambiguous, and the exclusion rule above means the override is declared rather than left
to specificity. This is the same thing `chatPage.test.ts:1263-1272` already does.

## Definition of Done

- [x] All three assertions were watched RED first, naming the real symptoms — and the third for a reason the plan did not predict: the rule it asserts did not exist yet.
- [x] `.msg .what` and `.passage` wrap; `.msg .what pre, .msg .what table` explicitly do not, and both keep their own horizontal scroll.
- [x] Whole suite green: 2947 tests, 2946 pass, 1 skipped, 0 fail.
- [x] `research/module_extension.md`, `research/module_tests.md` and `CHANGELOG.md` updated.
- [x] `plan-lifecycle.mjs` and `pin-check.mjs` clean.
- [x] Indexed — the `todo/README.md` row while it was open, and the `research/README.md` row on promotion.

## Acceptance — the gate on both sides

1. `review_plan` over this document before the first line of code; `resolve` every finding.
2. RED test per defect, then the whole suite.
3. **Rebase onto `origin/main` immediately before the code round, and record the SHA.** `baseRef` is
   `origin/main`, which is only the right diff if the branch is sitting on its tip — main moves under
   a branch here several times an afternoon, and a round run from a stale base reads main's newer work
   as this change's deletions. (Raised in the plan round; it has cost this repository real findings
   more than once.) Then `review_code` with this document as the scope; `resolve` every finding.
4. Pull request only after the code round is resolved; at most three open on this repository.
5. Five minutes after opening: read CI and CodeRabbit, verify each comment against the code and the
   rules, fix or answer, resolve the threads, merge by rebase.
6. Promotion inverts the checklist's order — every edit while the file is still in `todo/`, the
   `git mv` last, `git add` the destination, one commit.

Specific to this plan: no new command and no new setting, so `helpCoverage.test.ts` has nothing to
demand.
