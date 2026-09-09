# PLAN — the composer stays put, grows with the question, and the page scrolls once

> Status: **plan only, nothing implemented yet.** Kind: **bug** (four of them, one layout). Scope:
> the chat tab's page — `src_vs_code/src/chatPage.ts` (`chatBody`, `chatStyle`, `chatScript`).
> Origin: [BUGS_2026-09-09.md](BUGS_2026-09-09.md), entries 3, 9, 11 and the decision in 23.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_chat_with_other_ais.md](../research/PLAN_chat_with_other_ais.md).

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
  (`chatPage.ts:200-207`) — never a second path. Later, [PLAN_presets_above_the_composer.md](PLAN_presets_above_the_composer.md)
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
[PLAN_a_turn_nobody_can_stop.md](PLAN_a_turn_nobody_can_stop.md),
[PLAN_provider_then_model.md](PLAN_provider_then_model.md) and
[PLAN_presets_above_the_composer.md](PLAN_presets_above_the_composer.md) all queue behind it. It is
the skeleton they build on, so it goes FIRST in the chat-page lane.
