# PLAN — the prompt box forgets what was typed

> Status: **IMPLEMENTED, 2026-09-09.** Kind: **bug**. Shipped in extension 0.31.20, pull request #156.
> Scope as built: `src_vs_code/src/panelView.ts`, `panelProvider.ts`, `extension.ts`. Origin:
> [BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entry 2.
>
> Related docs: [module_extension.md](module_extension.md) — *A third answer beside repaint and
> patch: WITHHOLD* — and [PLAN_chat_with_other_ais.md](PLAN_chat_with_other_ais.md).

## What shipped differently — starting with the measurement, which refuted this plan

**Step 1 was supposed to find which repaint killed the draft. It found that this document's central
premise was wrong.** The plan below says the obvious fix makes things worse, because every save is a
`config.update`, every update fires `onDidChangeConfiguration`, and the listener repaints — so the box
would lose focus after every character. It cannot. `render()` assigns `webview.html` only when
`staticKey` moves, and `staticKey`'s eleven fields are `settings`, `vendors`, `codexModels`,
`localEngines`, `server`, `side`, `latestServerVersion`, `usageWindow`, `openSections`, `teamServers`
and `usageScope`. `state.chat` is not one of them, and `state.settings` is `CoaiSettings`, which the
chat keys are deliberately not part of. A chat setting cannot repaint the panel — now an assertion, so
adding `chat` to the key fails with that sentence instead of making the box flicker.

**What did kill it** is the same reading from the other side: `render()` runs unconditionally every
five seconds from the escalation tick and recomputes all eleven, four of which move with nobody
touching the panel — the local-engine probe, the server's own `--version`, the published release, the
Team-server catalog — while `settings` moves whenever any other control is written from any window.

**So half two changed shape.** Instead of teaching the configuration listener to recognise its own
write, the panel gained a THIRD answer beside repaint and patch: **withhold**. A full `webview.html`
assignment is held back while a `[data-setting]` control has focus, the live regions keep patching,
and the key is not recorded while a paint is withheld, so the next render after focus leaves paints
what this one could not. That covers all five repaint causes rather than one.

Everything else the implementation carries, and the code round is why:

- **The hold is capped at 30 seconds from the start of the editing SESSION**, renewed by neither a
  keystroke nor a tab to the next control — the code round found the second half, and it is the same
  defect twice. `focusout` is not guaranteed either: switch application mid-sentence and the panel
  would never learn the box was abandoned.
- **Nothing is lost by the paint that ends the hold.** The value was written 400 ms after the last
  keystroke, and the paint carries `PanelState.focus` — `{ id, start, end }`, where the id is
  `setting|vendor|role`, because a role-keyed control and a vendor-keyed one both carry
  `data-setting="rounds"`. The caret rides on the debounced write that already happens.
- **Writes queue and a render awaits the queue** until it is stable. Blur → `change` → write →
  focusout → render is a race the render can win, re-stamping the box from the value being replaced.
- **One write per change, not two.** `change` compares with the value a control held when it gained
  FOCUS, not with the value last sent.
- **Disposal forgets the editing state**, because closing the sidebar fires no `focusout`.
- **The tests EXECUTE the page's script** against a fake document rather than scanning its text — the
  plan round refused a scan, correctly: a script can hold an `input` listener, a debounce and a focus
  message while posting the wrong key, a stale value or the wrong order.

## The open tail

1. **A `config.update` that REJECTS** — an unwritable `settings.json` — leaves the typed value in the
   document and nowhere else, and the paint that eventually lands replaces it. The person is told by
   the notification `save` already raises. Surviving it properly means the host keeping a shadow copy
   of what every dirty control holds: a second source of truth for a control's value, which is the
   drift this module has already paid for twice. Named rather than built.
2. **This release shipped without its CHANGELOG entry.** The command that bumped the version and wrote
   the note did both in one `node -e "…"`, and the note was a template literal — bash ran its
   backticks as command substitution, so that edit matched nothing while the version bump beside it
   landed. Silent, partial, and past review before anyone noticed, because nothing reads the
   CHANGELOG. Restored in 0.31.21.

## The symptom

Type an instruction into *What to ask about the selection* in the sidebar (`поясни` in the
operator's screenshot). It is gone again — the box comes back holding what it held before. The
setting it is meant to write, `coai.chatPrompt`, is what every chat turn opens with
(`chatSettings.ts:69`), so a prompt that does not save is a feature that cannot be configured.

## What is already known — and what is NOT, which is why step 1 is a measurement

Verified in the code, none of it the cause on its own:

- The textarea posts through the shared `[data-setting]` listener, `panelView.ts:202-213`, on
  **`change`** — which a textarea fires at BLUR, not per keystroke.
- The host writes it as a `plain` setting: `panelProvider.ts:918` (`write`) →
  `settingsShape.ts:149` (`settingWrite`) → `panelProvider.ts:1053` (`save`), which is
  `config.update(key, value, Global)` in a try/catch that surfaces failure as an error toast.
- `coai.chatPrompt` IS declared (`package.json:445`), so the update has a registered key.
- `chatPrompt` is **not** in `OVERLAID_SETTINGS` (`settingsShape.ts:222-226`), so the per-side
  overlay is not involved; the value goes to `settings.json`.

**The suspect.** Something reassigns the panel's HTML while the box still has focus — the typed
text dies with the old DOM before `change` ever fires. The live tick patches only two regions
(`panelView.ts:216-232`), so the tick itself is innocent; but every other `render()` — a CLI
status probe finishing, a price table arriving, a configuration change from anywhere — reassigns
`webview.html` wholesale, and a keystroke never survives that.

**Do not fix blind.** Step 1 establishes which assignment lands between focus and blur; the fix
depends on it, and there is a trap either way (below).

## The trap the obvious fix walks into

The obvious fix is "save on `input`, not on `change`". Alone it makes things WORSE: every save is a
`config.update`, every update fires `onDidChangeConfiguration`, and the listener re-renders the
panel — so the box would lose focus after **every character**. The fix therefore has two halves,
and the second is mandatory:

1. The value is posted as it is typed (debounced — one write per pause, not per key), so a repaint
   can no longer lose more than the last fraction of a second.
2. **A repaint caused by the panel's own write must not rebuild the DOM under a focused control.**
   Either the configuration listener recognises its own write (a token, or comparing the value it
   just wrote) and patches instead of reassigning, or the render path preserves the focused
   element's draft and selection across the reassignment. The first is cleaner; the second is a
   safety net worth having for the probes that repaint on their own schedule.

## Build order

1. **Measure.** With a temporary log line on every `webview.html` assignment (timestamp + who
   asked), type into the box and watch which one lands. Record what was seen in this file.
2. **RED test** — the guarantee, not the mechanism: `TextTypedIntoThePromptBox_IsWrittenToSettings`
   and `ARepaintWhileTheBoxHasFocus_KeepsWhatWasTyped`. The panel's page source is what the tests
   exercise (as the rounds log's sort tests do — embedded verbatim, `roundsLog.test.ts`), plus the
   host-side `write` path with a fake configuration. Watch them fail with the real symptom.
3. Post on `input`, debounced, in the `[data-setting]` script for textareas only — selects and
   checkboxes keep `change`; a dropdown must not save half-chosen.
4. The self-write guard in the configuration listener (registered in `activate`, per the comment at
   `panelProvider.ts:265-269`) — a write the panel made itself does not repaint it.
5. GREEN. Then the whole suite.

## Test plan

- `chatSection.test.ts` (the panel's chat section already has tests there): the two guarantees
  above, RED first.
- A host-side test: a `setting` message for `chatPrompt` reaches `config.update('chatPrompt', …,
  Global)` exactly once per debounce window, with the last value.
- A test that a `chatLanguage` select still saves on `change` and NOT on input — the fix must not
  widen to controls where it is wrong.
- Manual: type, wait five seconds (the live tick), type more, click away, reload the window: the
  text is in `settings.json` and back in the box.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`fix/the-prompt-box-forgets`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   symptom, and the diff `main...fix/the-prompt-box-forgets` — three dots; the two-dot moving-base trap is recorded in
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

**Specific to this plan:** no manifest or help change (no new setting); `module_extension.md`'s description of how the panel saves a setting gains the debounce and the self-write guard; CHANGELOG line in the person's words — *the prompt you type into the sidebar now stays typed*.

## Definition of Done

- [ ] The measurement in step 1 is recorded here: which repaint was killing the draft.
- [ ] Both RED tests exist, were watched failing with the real symptom, and pass.
- [ ] A textarea in the panel saves as it is typed (debounced); selects and checkboxes are unchanged.
- [ ] A write the panel made itself does not rebuild the panel's DOM.
- [ ] `npm test` green; the acceptance ritual above complete; the plan promoted on merge.

## Parallelism

Owns `panelView.ts` (the script block and the chat section) and `panelProvider.ts` (`write`,
`save`, the configuration listener). **Does not touch `chatPage.ts`.** Runs in parallel with
anything that does; conflicts with [PLAN_presets_above_the_composer.md](../todo/PLAN_presets_above_the_composer.md)
on the chat section of the panel — land this first, it is small.
