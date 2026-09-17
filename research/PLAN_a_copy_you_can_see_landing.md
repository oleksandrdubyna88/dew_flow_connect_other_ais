# PLAN — a copy you can see landing

> Status: **IMPLEMENTED, 2026-09-16.** Scope: the chat tab's copy controls — `chatPage.ts` (one
> message arm, page state, two CSS rules), `chatMessages.ts`, `chatPanel.ts`, `chatCommand.ts`,
> `answerCopy.ts`, and the test harness in `chatPage.test.ts`.
>
> Issue: [#313](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/313).
> Related docs: [module_extension.md](module_extension.md), [module_tests.md](module_tests.md).
>
> Sibling: [#322](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/322), the
> same complaint on the panel's phrase button, shipped the same day as
> [PLAN_a_copy_that_says_so_in_green.md](PLAN_a_copy_that_says_so_in_green.md). This plan follows its
> shape deliberately, and shares its `cssRules.ts`.

## The symptom

Press **Copy block** (or **Copy the reply prompt**, or **Copy answer**) under an answer in the chat
tab. The block goes to the clipboard, a VS Code notification appears somewhere off to the side — and
**the button itself does not move**. The operator asked for an animation: a green tick that appears
to the right of the label and disappears after about a second, *"so that I understand it all
worked"*. The status-bar sentence also cannot say WHICH of several copy controls on a long answer
fired.

## The decision that differs from the request

**The tick appears when the write RESOLVED, not when the button is pressed.**

The issue asks for it *on press*. This product had already ruled otherwise and paid for the lesson:
the phrase button's first draft flipped its label in the page script on the click, a reviewer called
it **Blocking**, and was right — *a button that confirms on the press confirms just as confidently
when the clipboard was held by another program, and the person then pastes whatever was there
before.* A tick meaning "I received your click" is decoration; a tick meaning "it is on the
clipboard" is what the sentence under the request actually asks for.

The cost is a round trip of a few milliseconds. The benefit is that a refused clipboard shows **no
tick**, and the sentence that says why is then the only thing that appears.

## What shipped

| File | |
|---|---|
| `chatPage.ts` | the answer-level control gained a `data-sig` of its own and sends it; a `copied` message arm; `copiedMarks` + `copySeq` + `marksHeld` in page state; `copyKeyOf` / `paintCopied` / `forgetCopied` / `countMarks`; `COPIED_FOR_MS = 1000`; two CSS rules |
| `chatMessages.ts` | `copyAnswer` carries `sig`, bounded exactly as the block control's is |
| `chatPanel.ts` | `onCopyAnswer(id, index, sig)`; new `pushChatCopied` |
| `chatCommand.ts` | both hooks stop discarding their `CopyReport`; one `tellThePage`; one `warn` for the whole hooks object |
| `answerCopy.ts` | `acknowledgement(report, where)` — a function, because nothing in `chatCommand.ts` can be imported by a test |
| `test/chatPage.test.ts` | the harness rebuilt (below) and eleven assertions |
| `test/cssRules.ts` | three corrections, each caught by a test |

```css
.msg .copy[data-copied="1"] { opacity: 1; }
.msg .copy[data-copied="1"]::after { content: " \2713"; color: var(--vscode-charts-green); }
```

The opacity rule is not decoration: `.msg .copy` sits at `.55` unless its message is hovered, and a
tick at 55 % is the washed-out version of the one thing asked to be noticeable. `charts.green` on
this page's ground is 9.04:1 dark and 4.33:1 light — measured for #322, and `editor.background` is
what this page's `body` really is, unlike the sidebar.

## The three things the design had to get right

1. **Identity, not position.** The acknowledgement echoes back the coordinates the page sent, `sig`
   included, and is never re-derived from what the host holds now: an answer arriving between the
   press and the write resolving shifts what `index` names. The signature does that one job and
   deliberately **not** the other — it does not gate the copy, whose own staleness is still resolved
   at press time.
2. **Its own message.** Not a field on the state push, because `pushChatState` de-duplicates by
   serialised payload: press one control twice and the second payload would be identical to the first
   and dropped, so the second tick would never arrive.
3. **The mark lives in page state.** `#messages` is replaced wholesale on every push, so a mark on
   the element would die with the node an arriving answer rebuilt. It is keyed by **generation**
   rather than a deadline, so a second press takes ownership and the first press's timer finds a
   number that is no longer its own.

## The harness — the largest part of the change, and what it uncovered

`runChatPage` answered `document.querySelectorAll` with `[]` for every selector. That is the
quietest way a test can pass against a page that found nothing: the loop runs zero times, and *"the
mark did not land on the wrong control"* is true because it landed on no control at all. It now
serves selectors **by name** from a table and **throws** on one it has not been taught — the same
rule `cssRules.ts` follows by answering `undefined` rather than guessing.

Making it refuse rather than shrug found four things nobody knew:

- `button[data-zoom]` and `button[data-tone]` had both been answered with `[]` since they shipped, so
  **neither stepper's wiring had ever been exercised by anything**.
- The fake `acquireVsCodeApi()` had no `getState`, so the whole `fresh` (new-conversation) path threw
  the first time a test delivered one — that path had never been run either.
- A match over the page's own source finds every control **twice**, because the page embeds its
  regions into the script as JSON. Only what a browser would parse into the DOM counts.
- An answer carries a *Copy answer* control **twice**, in the `who` row above it and the `afterRow`
  below. They share one key, so acknowledging either ticks both — which is right, since they are one
  action on one thing. This surfaced only because a reviewer asked for an exact count.

`setTimeout` is injected and collected, so a test decides when the second is up; `secondPasses(1)`
runs only the oldest, which is what makes "the first press's timer fires while the second still has
half a second left" expressible at all.

## Deviations from the plan as reviewed

- **The tick is announced as well as drawn.** `::after` is generated content, which a screen reader
  is not obliged to announce; the marked control's accessible NAME carries it too. The plan had not
  thought about it.
- **A press takes the tick off before posting.** Press a ticked control again, have the clipboard
  refuse, and the first press's tick would otherwise stand as confirmation of a copy that did not
  happen.
- **A slate clears the marks and repaints.** A key is a position, a block and a signature of the
  text, none of which names the conversation. Clearing without repainting was a defect the test for
  it caught: the controls on screen are still the old ones until the next push.
- **`paintCopied` skips the walk when nothing is marked**, which is the overwhelmingly common case on
  a streaming push.
- **`copyKeyOf` refuses a control with no signature** rather than keying on the word `undefined`,
  which two unsignable controls would have shared.
- **The rejection is reported, not swallowed.** `coding-style.md` forbids a silently swallowed error,
  and three reviewers said so independently. One `warn` for the whole hooks object.
- **`cssRules.ts` gained three corrections**: it walks braces instead of regexing them, so
  `@keyframes` and `@media` are read rather than refused; a pseudo-element answers `false` rather
  than `undefined`, because `::after` paints a box of its own and never competes with the element;
  and specificity is ranked **per branch**, because CSS ranks a grouped selector that way — counting
  the group as one made `.msg:hover .copy, .msg:focus-within .copy, .msg .copy:focus` appear to
  outrank a rule it cannot beat, and a test asserted it.

## Evidence

RED first, naming the real symptoms: *"a copy control carries no signature, so an acknowledgement
cannot be matched to the text it was drawn for"*; *"the control the person pressed does not say the
copy landed"*; *"the acknowledgement was lost when an arriving answer rebuilt the region under it"*;
*"a slate carried the old conversation tick onto an answer nobody copied here"*.

Every guard then proved by breaking it:

| Sabotage | The failure it produced |
|---|---|
| the tick rule removed | *nothing in the chat stylesheet draws a tick beside a control that copied* |
| `acknowledgement` inverted | the seam test, on the refusal arm |
| the harness reverted to `() => []` | all three behavioural tests went red |

Whole extension suite from a cleaned `out/`: **3054 tests, 3052 pass, 0 fail, 2 skipped.**
`plan-lifecycle.mjs` clean.

## What this did NOT do

- What lands on the clipboard, the signature check that gates a block copy, and the refusal sentences
  are untouched.
- The tick does not survive a window reload.
- The panel's phrase button gets no tick: it already says *Copied* in green (#322), and two
  acknowledgements for one act is one too many.

## Open tail

`signatureOf` is computed once per message per render for the answer control, while `renderAnswer`
computes its own lazily for the block controls — two FNV passes over the same markdown. A reviewer
raised it; the cost is a char loop with no allocation against markdown tokenisation that dominates it
by orders of magnitude, so it was not worth changing `renderAnswer`'s single-string contract for. If
a measurement ever shows otherwise, having `renderAnswer` return its lazy signature is the move.

Also still open from #322, where this tail originates
([PLAN_a_copy_that_says_so_in_green.md](PLAN_a_copy_that_says_so_in_green.md)): the two pre-existing
private CSS parsers should move onto `cssRules.ts`.
Extracted 2026-09-17 into
[PLAN_one_reader_for_a_page_stylesheet.md](../todo/PLAN_one_reader_for_a_page_stylesheet.md), which
names both sites and the one that ranks rules by array index.

### The boundary with that plan

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| `cssRules.ts`, and converting the third private parser that made the round call it Blocking | **this plan** | consumes it unchanged | shipped first |
| The two survivors in `chatPage.test.ts` — `rules()` and the inline reader that ranks by array index | [PLAN_one_reader_for_a_page_stylesheet.md](../todo/PLAN_one_reader_for_a_page_stylesheet.md) | recorded them here as an open tail | after this one |
| The double `signatureOf` pass per message | neither — measured on this plan's round and declined | — | closed |

**Disjoint**: this plan's work is complete. The child adds no capability to `cssRules.ts` and touches
nothing under `src/`.
