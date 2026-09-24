# PLAN — the retry sits outside the failure, and the failure wraps

> Status: **IMPLEMENTED, 2026-09-24.** Deviations: the wrap declarations sit on `.failure` itself, not on
> `.failure .said` — the note the page script writes into the same region uses the box without the span,
> and it is wrapped in the row too (a test pins it, red with the old line); the measurement compares the
> text against the box's CONTENT edge and keeps the button on the page, and a page it cannot read is a
> FAILED case rather than a crash (our own review). Measured: nine of nine held; nine of nine failed on
> main's markup.
>
> Scope: the chat page's failure line in `src_vs_code`
> (`chatPage.ts` `chatFailureHtml` and its CSS). Issue #346.
>
> Related docs: [module_extension.md](module_extension.md), [architecture.md](architecture.md).

## The symptom

Issue #346, with a screenshot: in a chat tab the red failure box and its **Try again** button run into
each other, and a long failure text runs past the box's right edge instead of wrapping. The operator's
ask: *move Try again out of the red box, to its right; the text must wrap, not leave the box.*

## Why (read from the code)

- The button is INSIDE the red box: `chatFailureHtml` (`src_vs_code/src/chatPage.ts:734-747`) returns
  `<div class="failure"><span class="said">…</span><button class="retry" data-retry=…>Try again</button></div>`.
- The box is a flex row (`chatPage.ts:972`), the text is `flex: 1 1 auto; min-width: 0` (`:976`) — enough to
  shrink, not to break a long unbroken token: failure texts carry paths, URLs and ids, which have no
  space to wrap at, so they overflow the box. `white-space` is the default, so a failure's own line
  breaks collapse too.

## The design

1. **Two boxes, not one.** `chatFailureHtml` returns
   `<div class="failureRow"><div class="failure"><span class="said">…</span></div>{Try again}</div>`: the
   red border belongs to the text alone, and the button is its sibling to the right, outside it. With
   no retry the row holds the box alone. `.failureRow` is the flex row (`display: flex; gap: 12px; align-items:
   flex-start`), `.failure` grows (`flex: 1 1 auto; min-width: 0`), `.retry` keeps `flex: 0 0 auto`.
2. **The text wraps inside its box.** `.failure .said` gets `overflow-wrap: anywhere` (a long path or id
   breaks rather than overflows) and `white-space: pre-wrap` (a failure's own line breaks are kept).
3. **Nothing else moves.** The retry listener is delegated to `#failure` and matches `[data-retry]`
   (`chatPage.ts:2009-2020`), so the button still works wherever it sits inside the region — which is
   exactly what the existing *"the retry control is still live after its region has been rewritten"*
   test guards; the host still builds the markup through the one builder both render paths use — and that
   is checked, not assumed: no other source writes `class="failure"` (local, the plan round).

## Build order

1. Tests first (`chatPage.test.ts`): the rendered failure has the button OUTSIDE the `.failure` element and
   inside the row; no row button without a retry; the parsed stylesheet (`cssRules.ts`) gives `.failure
   .said` `overflow-wrap: anywhere` and `white-space: pre-wrap`, and `.failureRow` `display: flex`. Red.
2. The markup and the CSS. Green. Every new assertion proved by breaking it.
3. Docs: `research/module_extension.md` (the chat page), CHANGELOG `## Unreleased`,
   `research/module_tests.md` — and what is NOT covered: no layout engine, so the absence of an overlap
   on screen is asserted through structure and declarations, not measured.

## The measurement (the plan round asked for the effect, not only the declarations)

No test in this repository has a layout engine, so a script renders the SHIPPED failure markup and the
page's own stylesheet in headless Microsoft Edge at several widths — with a long path, a long URL and a
multi-line failure — and reads `getBoundingClientRect` back: the text box's right edge inside the red
box's, the button's left edge right of the box's, no overlap. Kept as `scripts/measure-failure-layout.mjs`,
reported in the PR as observed. `bundledPage.test.ts` additionally asserts the button sits OUTSIDE the
red box in the markup the shipped bundle renders (local and codex, the plan round).

## Test plan

- `cd src_vs_code && npm test`, `npm run lint`. The bundled page test (`bundledPage.test.ts`) still presses
  *Try again* in the shipped bundle.

## Definition of Done

- [x] *Try again* is outside the red box, to its right.
- [x] A long failure text wraps inside the box, and keeps its own line breaks.
- [x] The retry still works after its region is rewritten, and in the shipped bundle.
- [x] Tests red first; `npm test` and lint green; docs updated; this plan promoted to `research/`.
