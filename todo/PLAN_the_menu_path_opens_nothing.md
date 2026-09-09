# PLAN — the menu path opens the console instead of a tab

> Status: **plan only, nothing implemented yet — and step 1 may close it without code.** Kind:
> **bug (unconfirmed)**. Scope: the context-menu trigger — `src_vs_code/src/chatCommand.ts`,
> `chatTrigger.ts`, `package.json` (`menus.webview/context`). Origin:
> [BUGS_2026-09-09.md](BUGS_2026-09-09.md), entry 10.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_chat_with_other_ais.md](../research/PLAN_chat_with_other_ais.md).

## The symptom

Copy, then right-click in the Claude Code panel: what appears is the Output console, not a chat
tab. The keybinding on the same selection opens the tab. **Both must open the tab.**

## What the operator's own screenshot proves, and what it does not

The `COAI probe` output channel shows the capture SUCCEEDING — `path : MENU (read the clipboard
as-is)`, `RESULT : took 1322 chars from the clipboard in 3 ms`, and a `captured :` line holding
the right text. So capture is not the failure.

But the context menu in that screenshot holds **two** items that grab a selection:

| Item | Keys | Whose |
|---|---|---|
| `Chat with other AI` | `Ctrl+Alt+A` | **ours** — the one `webview/context` entry in `package.json`, `when: webviewId == 'claudeVSCodePanel'` |
| `COAI probe: grab the selection` | `Ctrl+Shift+Alt+G` | **not in this repository at all** — not in `package.json`, not in `src_vs_code/src`; a probe extension installed alongside, whose output channel is the `COAI probe` in the screenshot |

Writing to an output channel is what a probe is for. So the first question is which item was
pressed, and the plan is built to answer it before it fixes anything.

## Build order

1. **Triage — no code.** Disable the probe extension; reproduce with `Chat with other AI` alone,
   from the menu, with `chatAutoSend` at the operator's `never`. Record the outcome here.
   - **Opens a tab (filled, waiting for Enter):** no defect in this product. Close this plan as
     *not a bug* in its status line, promote it as a record, and recommend the probe be
     uninstalled or renamed so the two items cannot sit side by side again.
   - **No tab:** a real defect in the menu path. Continue.
2. **RED test** — `chatWiring.test.ts` already exercises the trigger paths: add
   `TheMenuTrigger_OpensATab_EvenWhenItDoesNotSend`. The menu path is DESIGNED not to send under
   `keyboard`/`never` (`sendsImmediately`, `chatSettings.ts:88-96`) — "fills a tab and waits" is
   still a tab. Watch it fail with the real symptom (no panel created), not a setup error.
3. Trace `chatCommand.ts` from the menu entry through `fromMenu` to the panel open; fix where the
   menu branch diverges from the keybinding branch. Prefer removing the divergence over adding a
   special case: both triggers should reach ONE "open the tab with this passage" step.
4. GREEN; whole suite.

## Test plan

- `chatWiring.test.ts`: menu trigger + `never` → a panel is created and its composer holds the
  passage; menu trigger + `always` → a panel is created and a turn is sent; keybinding unchanged.
- Manual with the probe disabled AND enabled, both recorded.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`fix/the-menu-path-opens-nothing`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   symptom, and the diff `main...fix/the-menu-path-opens-nothing` — three dots; the two-dot moving-base trap is recorded in
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

**Specific to this plan:** if step 1 finds no defect, the ritual reduces to the record: this file promoted with its finding and no code round. If it is a defect: no manifest or help change; `module_extension.md`'s trigger description gains the one-step rule.

## Definition of Done

- [ ] Step 1's outcome is written in this file, with the probe disabled.
- [ ] If a defect: the RED test exists, was watched failing, passes; both triggers reach one open step.
- [ ] The operator has been told, in one sentence, what to do about the probe.
- [ ] `npm test` green; the acceptance ritual (or the record) complete.

## Parallelism

Owns `chatCommand.ts` (trigger branch) and `chatTrigger.ts`. **Does not touch `chatPage.ts`.**
Runs beside the chat-page lane. Conflicts with [PLAN_a_turn_nobody_can_stop.md](PLAN_a_turn_nobody_can_stop.md)
on `chatCommand.ts` only if step 1 finds a defect — sequence them.
