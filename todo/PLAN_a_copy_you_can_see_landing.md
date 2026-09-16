# PLAN — a copy you can see landing

> Status: **plan only, nothing implemented yet.** Scope: the chat tab's copy controls —
> `src_vs_code/src/chatPage.ts` (one region message, one rule), `chatPanel.ts` and `chatCommand.ts`
> (the acknowledgement's return path), and `chatPage.test.ts` (the harness gap named below).
>
> Issue: [#313](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/313).
> Related docs: [module_extension.md](../research/module_extension.md).
>
> Sibling: [#322](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/322), the
> same complaint on the *panel's* phrase button, shipped 2026-09-16 as
> [PLAN_a_copy_that_says_so_in_green.md](../research/PLAN_a_copy_that_says_so_in_green.md). This plan
> deliberately follows its shape.

## The symptom

Press **Copy block** (or **Copy the reply prompt**, or **Copy answer**) under an answer in the chat
tab. The block goes to the clipboard, a VS Code notification appears somewhere off to the side — and
**the button itself does not move**. The operator asked for an animation: a green tick that appears
to the right of the label and disappears after about a second, *"so that I understand it all worked"*.

Today the control posts to the host and the page forgets about it
(`chatPage.ts:1752-1767`). The only feedback is `answerCopy.ts`'s sentence, shown through the status
bar by `copyText.ts` — which is real, but it is not where the person is looking, and it says nothing
about *which* of the several copy controls on a long answer was the one that fired.

## What is there today

| | |
|---|---|
| `renderAnswer.ts:170-179` | `copyRow` draws `<button class="copy blockCopy" data-block="N" data-at="M" data-sig="…">` under every fenced block and blockquote; the label is `COPY_BLOCK` or `COPY_REPLY` (`:69-70`) |
| `chatPage.ts:484` | the message-level control, `<button class="copy" data-copy="M">`, labelled `COPY_ANSWER` (`:430`) |
| `chatPage.ts:1738-1770` | one delegated `click` listener on `#messages`, posting `copyAnswer` or `copyBlock` |
| `chatPage.ts:905-906` | `.msg .copy` — a link-coloured, borderless button at `opacity: .55`, rising to `1` on hover or focus |
| `chatPage.ts:896` | `.blockRow` is `justify-content: flex-end`, so a block control sits at the right edge |
| `chatPanel.ts:365-371` | the dispatch into `onCopyAnswer` / `onCopyBlock` |
| `chatCommand.ts:3199-3224` | the hooks. Both build a `CopyDecision` and call `answerCopier.copy(…)` — and both **discard the result with `void`** |
| `chatPanel.ts:285-289` | `RevealablePanel.post`, the host→page channel, already used by `pushUiScaleTo` and `pushTextToneTo` |
| `chatPage.ts:1840-1900` | the page's message handler, which already takes three of its OWN message types (`asked`, `note`, `fresh`) beside the state push |

So both halves of the road exist. What is missing is that the hooks throw away the one fact worth
showing, and the page has no rule for a state nothing sets.

## The decision that differs from the request, and why

**The tick appears when the write RESOLVED, not when the button is pressed.**

The issue says *при нажатии* — on press. This product has already ruled otherwise, and for a reason
it paid for: `panelProvider.ts:2002` and the account in `research/module_extension.md` record that
the phrase button's first draft flipped its label in the page script on the click, a reviewer called
it **Blocking**, and was right — *a button that confirms on the press confirms just as confidently
when the clipboard was held by another program, and the person then pastes whatever was there
before.* A tick that means "I received your click" is decoration; a tick that means "it is on the
clipboard" is the thing being asked for, because the sentence under the request is *"so that I
understand it all worked"*.

The cost is a round trip of a few milliseconds. The benefit is that a refused clipboard shows **no
tick** — and the existing sentence, which already says why, is then the only thing that appears.

## Design

### 1. The hooks stop discarding the report

`answerCopier.copy()` answers a `CopyReport` (`copyText.ts:29-41`) whose `copied` is true **only when
the write resolved** — `copyText.ts:30` says so in as many words: *"A page flips a control to Copied
on this and nothing else."* The two hooks in `chatCommand.ts` await it and, on `copied`, post back:

```ts
{ type: 'copied', index: <message>, block: <ordinal | undefined> }
```

`block` absent means the message-level control. Nothing else changes about the copy path — the text
that lands on the clipboard is still the host's own stored markdown, checked against the signature.

### 2. The page marks the control, and a rule paints the mark

The page finds the button by **comparing its dataset**, never by interpolating a value into a
selector — the panel's four-reviewer lesson, adopted here although these values are our own numbers,
because the next value someone adds may not be.

```
.msg .copy[data-copied="1"]::after { content: ' ✓'; color: var(--vscode-charts-green); }
.msg .copy[data-copied="1"] { opacity: 1; }
```

The second rule is not decoration: `.msg .copy` sits at `opacity: .55` unless the message is hovered,
and a tick at 55 % is the washed-out version of the thing that was asked to be noticeable.

`--vscode-charts-green` on this page's ground is **9.04:1** dark and **4.33:1** light — measured for
#322 against `editor.background`, which is what this page's `body` really is (`chatPage.ts:750`),
unlike the sidebar. The tick is also a SHAPE, so colour is not carrying the meaning alone.

`COPIED_FOR_MS = 1000`, matching the panel and matching the request.

### 3. The timer must survive the region being rebuilt

`#messages` is replaced wholesale on every state push. A `setTimeout` that clears the mark can
therefore fire against a node that is no longer in the document — and, worse, a push that lands
*during* the second re-renders the button without the mark, so the tick vanishes early.

Both are accepted rather than fought, and the plan says so out loud: the mark lives only in the DOM,
a rebuild loses it, and the timeout clears the node it captured whether or not that node is still
attached (harmless either way). Carrying it through the host state would mean a transient per-press
fact travelling on a channel `pushChatState` **de-duplicates by serialised payload**
(`chatPanel.ts`), which would drop the second identical acknowledgement entirely. A tick that
occasionally ends early during an arriving answer is a smaller wrong than one that sometimes never
appears.

### 4. No `@keyframes`

A fade was considered and dropped. The request is *appears and disappears*; an at-rule would also
force the flat-rule test parser (`cssRules.ts`, shipped with #322) to grow at-rule support for one
flourish.

## The harness gap — the largest part of this change

`chatPage.test.ts`'s `runChatPage` harness models `document.querySelectorAll` as **`() => []`**
(`:363`, `:480`). So a page that finds its button with `querySelectorAll('[data-copy]')` is handed an
empty list, the loop does nothing, and a test asserting "the tick was NOT put on the wrong button"
passes while the tick was put on nothing at all. **This is the exact failure this family has already
recorded** — give the fixture every value the script reads, or *"nothing was painted"* passes without
painting.

So the harness gains a real `querySelectorAll`, built from the rendered markup the same way
`getElementById` already is (`:431-433` already parses attributes into `dataset`), and it
**throws** on a selector it cannot serve rather than answering `[]`. An unserved selector must be a
loud failure, never an empty result — the sibling lesson to `cssRules.ts`'s `undefined`.

## Build order

1. **Harness first**, with a test that proves it: `querySelectorAll('[data-copy]')` over a rendered
   page returns the controls that are really there, and an unmodelled selector throws.
2. **RED test**: press a block control, deliver the host's `copied` acknowledgement, assert the tick
   mark lands on that control and on no other; assert the tick does NOT appear when no
   acknowledgement arrives (the refused-clipboard case); assert it clears on the timer.
3. The page: the message arm, the dataset match, the mark, the timer.
4. The rule.
5. The hooks: await the report, post on `copied`. A test at the `chatPanel` seam that the message is
   posted only for a report whose `copied` is true.
6. **Teeth**: break each guard and watch it go red — remove the rule, invert the `copied` condition,
   and revert the harness's `querySelectorAll` to `() => []` (which must redden the new tests, and if
   it does not, the tests are the decoration this step exists to find).

## Test plan

```bash
cd src_vs_code && rm -rf out && npm run compile && node scripts/run-tests.mjs
node .agents/conventions/tools/plan-lifecycle.mjs
```

No behavioural assertion over page source text — `.agents/PROJECT.md:73-83`, `.coderabbit.yaml:115-126`,
operator ruling 2026-09-14. The page is run; the stylesheet is parsed with `cssRules.ts`.

## What this plan does NOT do

- It does not touch what lands on the clipboard, the signature check, or the refusal sentences.
- It does not give the tick to the panel's phrase button — that surface says *Copied* in green
  already (#322) and a tick as well would be two acknowledgements for one act.
- It does not make the acknowledgement survive a reload or an arriving answer; see §3.

## Definition of Done

- [ ] A RED test observed failing with a message naming the real symptom, and passing after.
- [ ] Reverting the harness's `querySelectorAll` to `() => []` reddens the new tests.
- [ ] No tick on a refused clipboard, proved by a test rather than by reading the code.
- [ ] Colours from the theme; no hex; no `@keyframes`.
- [ ] Whole extension suite green from a cleaned `out/`; `plan-lifecycle.mjs` clean.
- [ ] `research/module_extension.md` records the acknowledgement and the harness change.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>`, both READMEs updated.
