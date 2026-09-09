# PLAN — the composer stays put, grows with the question, and the page scrolls once

> Status: **IMPLEMENTED, 2026-09-09** (PR #161). All four stories. Kind: **bug** (four of them, one
> layout). Scope: the chat tab's page — `src_vs_code/src/chatPage.ts`.
> Origin: [../todo/BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entries 3, 9, 11 and the decision
> in 23.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_chat_with_other_ais.md](PLAN_chat_with_other_ais.md).
>
> ### What shipped differently, and what it cost
>
> **The build order was swapped, and it was an improvement.** The scroll rule landed BEFORE the
> composer's growth: a footer whose height changes needs the at-bottom rule to already exist, and a
> pure function that has not been written cannot be called. Fable made that call in the split.
>
> **Three defects were found that no report contained.**
> 1. *The page's `body` rule had never applied.* The stylesheet opened with the zoom as a bare
>    `font-size: 13px;` outside any rule; a CSS parser appends tokens to a prelude until `{` and `;`
>    does not end one, so the selector became `font-size: 13px; body` and the whole rule was dropped
>    — no margin, no padding, no font, no background, and the chosen text size never applied until
>    something unrelated pushed it. Confirmed against a real parser before it was believed.
> 2. *Nothing locked the composer between posting a turn and the host's answer* — a window one Enter
>    wide, into a pipe that carries one turn.
> 3. *A repeat push counted as an insertion*, so a retry scrolled a reader for content already in
>    front of them.
>
> **Telling the page's own scroll from the reader's took three shapes.** A flag cleared on a later
> frame (stays raised until that frame comes); a position kept indefinitely (turns the bottom into a
> magic pixel — a reader who scrolls away and comes BACK to it reads as the page for the rest of the
> tab's life); and finally a one-shot arming consumed by the event it causes. Only the third is
> right, and the first two were each caught by a test rather than by review.
>
> **A regression this branch introduced and the gate caught:** requiring a string before writing meant
> a state that OMITS `cappedHtml` or `failureHtml` left the old notice on screen, where the protocol
> has always read "not mentioned" as "gone".
>
> **The gate ran six rounds** — a plan round and a code round per story pair — and its findings
> changed the design four times: the `field-sizing` feature detection (the manifest declares support
> back to VS Code 1.85, whose engine has never heard of the property, so a page that assumed would
> have worked on the machine it was written on and silently never grown anywhere else); the
> cancellable deferred follow; the fonts-settled correction dying with the follow it belongs to; and
> a cancelled follow handing over the jump control, without which an answer arrives and nobody is
> told. On the last round codex was out of budget — eight of twelve reviewers answered.
>
> **A correction to this plan's own record:** it claimed nothing in this repository executed the chat
> page's script. `bundledPage.test.ts:310` had, since before the branch; the grep that missed it
> looked for `chatPageHtml`, and that test reaches the page through `bundledChatPage()`. Every
> `typeof` guard here is load-bearing twice over — the old engine, and that test.
>
> **The open tail:** the manual check the test plan describes (a 60-line passage, the page landing on
> the last message, no yank while scrolled up, the control appearing, the box growing and shrinking)
> has NOT been run by a person. Everything above is proved by tests and by a bundled, minified page
> driven through a real push; none of it is proved by an eye.
>
> Test names shipped in the repository's own idiom — prose in lower case — rather than the PascalCase
> this plan wrote.

## The symptoms — four, and they are one layout

1. **The composer scrolls away.** The textarea and the `Enter sends · Shift+Enter for a new line`
   hint are the last two elements of a normal document flow (`chatPage.ts:181-182`), so they
   scroll with the conversation. And **only Enter sends** — there is no button.
2. **Two scrollbars.** The captured passage has its own — `.passage { max-height: 180px;
   overflow-y: auto }` (`chatPage.ts:128`) — beside the page's. The file's own header says why it
   was capped (`chatPage.ts:19-24`): *"a fifty-line selection would otherwise push the composer off
   the screen on open"*. **Pinning the composer retires that reason.**
3. **The composer does not grow.** `textarea { min-height: 64px }` (`chatPage.ts:141`) and
   `rows="3"`: a floor, no ceiling, no growth. With the send mode the operator chose (*Never —
   always let me press Enter*) a long multi-line question is the normal case.
4. **Nobody decided where the page looks**, and three separate fixes would each decide it their
   own way. Decided on 2026-09-09 (entry 23), and this plan implements the decision once.

## The decided scroll rule (entry 23 — do not re-decide it)

1. **On open, the tab looks at the LAST message.** The passage stays above, reached by scrolling up.
2. **A new answer scrolls the page to itself ONLY if the reader was already at the bottom.**
   "At the bottom" means within a line or two of slack, never to the pixel — and it is measured
   BEFORE the new content is inserted, or it always reads "not at the bottom".
3. **A reader who is scrolled up gets a *jump to the newest* control.**

Consequences: when the composer grows, a reader at the bottom STAYS at the bottom; a stopped or
failed turn renders under the same rule as an answer. One rule, not one per outcome.

## The shape

- `body` becomes a flex column filling the viewport: a scrolling region (`header`, `.passage`,
  `#messages`, `#thinking`, `#capped`) and a **pinned footer** (`#pickerBox`, the textarea, a Send
  button, the hint). Only the region scrolls; the page never does.
- The passage loses `max-height` and `overflow-y` and keeps its left border and spacing — it is
  still "the text being discussed", not the first thing anybody said.
- The textarea: `field-sizing: content; max-height: 30vh` — Chromium-only, which is fine in a
  page that only renders in VS Code's own webview; it shrinks back when text is deleted or sent.
  If `field-sizing` proves unsupported on the operator's build, the fallback is the usual
  `input` handler (`height = auto`, then `min(scrollHeight, 30vh)`). The 30 % is of the VIEWPORT.
- The footer's height changes, so the scrolling region is laid out against it (flex), never a
  constant.
- **A Send button at the right-hand end of the composer.** It obeys the same lock as the textarea
  (`locked = state.running || state.capped`, `chatPage.ts:172`) and calls the one `send()`
  (`chatPage.ts:200-207`) — never a second path. Later, [PLAN_presets_above_the_composer.md](../todo/PLAN_presets_above_the_composer.md)
  gives this button a second caption; build it as a button, not an icon, so a caption fits.
- The scroll rule as a **pure function** — `shouldFollow(scrollTop, clientHeight, scrollHeight,
  slack)` — in the page script, with its source embedded in the test the way `roundsLog.ts` embeds
  its sort (`roundsLog.test.ts`): the tests exercise the code the page runs.

## Build order

1. RED tests (below), watched failing.
2. Flex layout + pinned footer + Send button. Passage cap removed **in the same commit**, never
   before — removing it first reintroduces exactly the defect the comment at `chatPage.ts:19-24`
   records.
3. `field-sizing` growth with the 30vh ceiling; verify shrink-back.
4. The scroll rule: `shouldFollow`, the on-open scroll to the last message, the *jump to newest*
   control shown only when a message landed out of view.
5. Rewrite the header comment at `chatPage.ts:19-24`: the passage is still shown for the
   stale-clipboard reason; it is no longer capped, and why.

## Test plan (`chatPage.test.ts`, 16 tests there today)

- `TheComposerIsInAPinnedFooter_NotInTheDocumentFlow` — structure of `chatBody`.
- `ThePassageHasNoInnerScroll` — `chatStyle` carries no `max-height`/`overflow-y` on `.passage`.
- `TheSendButtonIsLockedExactlyWhenTheTextareaIs` — both states, from one `locked`.
- `TheSendButtonAndEnterShareOneSend` — the script has one `send` definition and two callers.
- `TheComposerCeilingIsThirtyPercentOfTheViewport` — `30vh` present, `min-height` kept.
- `shouldFollow` table: at bottom → true; one pixel short → true (slack); a screen up → false;
  measured before insertion (the function takes numbers, so the test IS the rule).
- Manual, recorded in the promotion: open a chat with a 60-line passage — the page lands on the
  last message; scroll up, ask; the answer does not yank; the jump control appears.

## Stories — the split, and the gate's thirteen findings placed

> Split on 2026-09-09 by Fable (the gate's own command routes the split decision to it), then
> checked against the repository. **One swap from the build order above, and it is an improvement:
> the scroll rule lands BEFORE the composer's growth.** A footer whose height changes needs the
> at-bottom rule to already exist — findings 5 and 11 are about exactly that — and a pure function
> that has not been written cannot be called.

Four stories, in order, one commit and one `review_code` each. Every one leaves `npm test` green
and the page usable on its own.

**One correction to the split — and one correction to that correction, which was mine:**

- There is no `thinkingRegion` helper. `chatPanel.ts:208-214` builds every pushed region by
  calling the same exported functions, so no markup is sliced and no opening tag is load-bearing.
- **The split's constraint about the fake DOM is CORRECT and my first reading of it was not.**
  `bundledPage.test.ts:310` — *'the chat page script the bundle produces parses and runs'* — has
  executed the bundled, minified chat script against a stub DOM since before this branch, and that
  stub supplies exactly `document`, `window` and `acquireVsCodeApi`: no `requestAnimationFrame`, no
  `CSS`, no `ResizeObserver`. I had grepped the test folder for `chatPageHtml` and that test reaches
  the page through `bundledChatPage()` instead, so it did not appear and I wrote that nothing ran
  the chat script. **Every `typeof` guard stories 2 and 4 add is therefore load-bearing twice over**
  — the older-Chromium host of finding 1, and an existing test that fails on a bare reference.

**Found while reading for story 1, and not in any list: the page's `body` rule never applied.**
`chatStyle` opened with `${zoomStyle(uiScale)}` — a bare `font-size: 13px;` outside any rule, and
CSS has no such thing at the top level. A parser consuming a qualified rule appends tokens to the
prelude until `{`, and `;` does not end one, so the selector became `font-size: 13px; body` and the
whole rule was dropped: no `margin: 0`, no `padding`, no font-family, no background, and the chosen
text size never applied until something unrelated pushed it. Confirmed against a real parser
(esbuild reads exactly that as the selector). Fixed first, with two RED tests, because story 1's
flex layout goes on `body` and would have been dropped with it.

### Story 1 — one scrolling region, a pinned footer, a Send button

`chatBody`: `<main id="scroll">` wraps header, passage, `#failure`, `#messages`, `#thinking`,
`#capped`; `<footer id="composer">` after it holds `#pickerBox`, a `.compose` row of the textarea
and `<button type="button" id="send">`, and the hint. `chatStyle`: `html, body { height: 100% }`;
body becomes a flex column with `overflow: hidden` and no padding; `#scroll { flex: 1 1 auto;
min-height: 0; overflow-y: auto }` — the `min-height: 0` IS finding 8, without it the child refuses
to shrink and the page keeps two scroll surfaces; `#composer { flex: 0 0 auto }`. `.passage` loses
`max-height`/`overflow-y` in this same commit. `chatScript`: `send()` hands focus back; a click
listener on `#send` calls the one `send()`; the Send button's `disabled` is set from the textarea's
in the same block — one lock, never two. The header comment at lines 19-24 is rewritten.

**Findings: 8, 12, 13.** **Expensive if wrong: yes** — it is the skeleton four queued plans build
on, the CSS cannot be unit-tested, and a missing `min-height: 0` reproduces the exact symptom while
every test stays green.

### Story 2 — the scroll rule

New exports `FOLLOW_SLACK_PX = 48` and `shouldFollow(scrollTop, clientHeight, scrollHeight, slack)`
— finding 4's concrete boundary: follow iff `scrollHeight - (scrollTop + clientHeight) <= slack`;
48 follows, 49 does not; overscroll, nothing-to-scroll and a non-finite input all follow (a page
that cannot measure is a page whose reader has not scrolled). Embedded into the script by
`toString()` and bound by assignment — the `roundsLog.ts:850` idiom, because the minifier renames
declarations. In the `state` handler the snapshot is the FIRST statement, before any `innerHTML`
write, and the scroll is applied in `afterLayout` (`requestAnimationFrame`, `setTimeout` where it
does not exist): findings 3 and 6, true by construction because every region arrives through that
one handler. On open, `landOnNewest()` runs immediately, again after layout, and once more after
`document.fonts.ready` where it exists — finding 10's lifecycle point.

**Findings: 3, 4, 6, 10.** **Expensive if wrong: yes** — a wrong ordering silently turns rule 2
into "never follow", and only the real webview shows it.

### Story 3 — jump to newest

`<button type="button" id="jump" hidden>` inside the footer, `position: absolute` out of flow so
showing it changes neither the footer's height nor `#scroll`'s `clientHeight` — finding 2. Shown
only when `!follow && wrote`; a click scrolls to the newest, hides itself and returns focus; a
`scroll` listener retires it once `shouldFollow` reads true — finding 7's missing lifecycle.

**Findings: 2, 7.** **Expensive if wrong: no** — a defect here is a missing or lingering button.

### Story 4 — the composer grows, shrinks back, and moves nobody

`textarea` gains `max-height: 30vh; field-sizing: content; overflow-y: auto`, keeping
`min-height: 64px`; the 30 % lives in that one rule for both paths. Feature detection is finding 1:
`CSS.supports('field-sizing', 'content')`, and where it is false an `input` handler sets
`height = 'auto'` then `scrollHeight + 'px'`, with the CSS ceiling and inner scroll unchanged —
called on input, after `send()` clears the box, and after a pushed draft, so shrink-back is a call
rather than a hope (finding 9). A `ResizeObserver` on `#composer`, where it exists, re-pins a
reader who WAS at the bottom when the footer's height changes — findings 11 and 5; the flag is what
the reader was before the resize, which is the only measurement that survives it. Documentation,
CHANGELOG and the recorded manual check close the branch here.

**Findings: 1, 5, 9, 11.** **Expensive if wrong: yes** — the detection cannot be observed from a
test on the host that matters, and the re-pinning interacts with story 2's ordering.

### The test harness

Stories 2-4 need the script RUN, not read. `runChatPage(state)` follows `runPage()` in
`theLogLosesItsFirstPush.test.ts:179`: slice the script out of `chatPageHtml`, execute it with
`new Function('document', 'window', 'acquireVsCodeApi', …)`, fake elements recording listeners,
`focused`, `style`, `value`, `disabled`, `hidden` and the three scroll numbers; return
`{ seen, posted, fire, deliver, fireFrames }`. It is the first test in this repository to execute
the chat page's script at all.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`fix/the-composer-stays-put`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   symptom, and the diff `main...fix/the-composer-stays-put` — three dots; the two-dot moving-base trap is recorded in
   `todo/PLAN_the_gate_diffs_from_a_moving_base.md`. `resolve`; repeat until `proceed`.
4. **Documentation.** `research/module_extension.md` says what the code now does;
   `research/architecture.md` if a cross-module seam moved.
5. **Help.** Every new command and setting has an article in `helpContent.ts` —
   `helpCoverage.test.ts` fails the build otherwise; a lagging translation is marked as such.
6. **README and CHANGELOG.** `src_vs_code/README.md` if what a person sees changed;
   `src_vs_code/CHANGELOG.md` in the prose the file already uses — the sentence a person reads,
   not the commit subject.
7. **Manifest.** `package.json` contributions (settings, commands, keybindings, menus), and the
   version the release line expects (see the `chore(release)` history).
8. **Family checks.** `node .claude/rules/shared/tools/plan-lifecycle.mjs` and `pin-check.mjs`
   clean.
9. **Promote.** On merge, `/promote-plan` this file to `research/` with `IMPLEMENTED <date>` and
   every deviation recorded — what shipped differently is the most valuable line of the record.

**Specific to this plan:** no manifest or help change; `module_extension.md`'s chat-tab section gains the layout (pinned footer, one scrolling region) and the scroll rule verbatim; CHANGELOG in the person's words. The test-plan's manual check is recorded in the promotion, with the passage length used.

## Definition of Done

- [ ] The composer, the Send button, the picker row and the hint are pinned; the page has one scrolling region and one scrollbar.
- [ ] The passage is uncapped and still visibly "the text being discussed".
- [ ] The composer grows with its text to 30 % of the viewport, then scrolls inside; it shrinks back.
- [ ] The scroll rule of entry 23 is implemented once, as a tested pure function, and the *jump to newest* control exists.
- [ ] The header comment at `chatPage.ts:19-24` tells the truth again.
- [ ] `npm test` green; the acceptance ritual complete; the plan promoted on merge.

## Parallelism

**Owns `chatPage.ts` — the whole file.** Nothing else that touches `chatPage.ts` may run beside it:
[PLAN_an_answer_reads_like_a_document.md](PLAN_an_answer_reads_like_a_document.md),
[PLAN_a_turn_nobody_can_stop.md](../todo/PLAN_a_turn_nobody_can_stop.md),
[PLAN_provider_then_model.md](../todo/PLAN_provider_then_model.md) and
[PLAN_presets_above_the_composer.md](../todo/PLAN_presets_above_the_composer.md) all queue behind it. It is
the skeleton they build on, so it goes FIRST in the chat-page lane.
