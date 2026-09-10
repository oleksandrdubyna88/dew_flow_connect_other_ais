# PLAN — presets above the composer: named prompts, named models, and a re-ask

> Status: **IMPLEMENTED, 2026-09-10** (PRs #177, #180, #181). All four stories. Kind: **feature** —
> the largest in the batch. Origin: [../todo/BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entries
> 12, 21b and 24; entry 1 was withdrawn in favour of this plan.
>
> ### What shipped differently
>
> **The sidebar box was not turned into a picker.** The plan's step 5 said *What to ask about the
> selection* would become a dropdown of preset names plus a link into the tab. It was left as it is:
> the buttons above the composer and the tab both reached the person first, and a third surface for
> the same list is a third place to keep in step. Entry 1's withdrawal still stands — the box is not
> being widened either — but the box itself is unchanged, and that is a deviation rather than a
> completion.
>
> **The migration reads the old prompt on every read, not once.** The plan said "on first read";
> what shipped is *only when there are no presets at all*, which is the same thing where it matters
> and stronger where it does not: somebody who deletes every preset gets their original prompt back
> rather than an empty list, and somebody who has any list is never given it again.
>
> **A structural guard from another branch shaped the code twice.** `the first question after a
> restore carries the whole transcript` reads the first 2500 characters of `restoreConversation`, and
> this plan's fields pushed the carry past it — twice. The second time, trimming a comment would have
> made the guard about distance rather than about ordering, so the page-state construction became its
> own function instead.
>
> ### The open tail
>
> The re-ask is offered whenever the model differs from the one that gave the last answer. It does
> not distinguish *the person switched deliberately* from *the model was switched for them* — the
> "continue with a local model" path after a cap is the case — and that is worth a look if anyone
> reports being offered a re-ask they did not ask for.

## The goal, in the operator's order

1. **Several prompts, saved**, each with a NAME; one ticked as the MAIN one — used when the trigger
   sends by itself (`sendsImmediately`, `chatSettings.ts:88-96`).
2. **Several models, saved**, each with a NAME and an optional STARTING PROMPT.
3. **Two rows of buttons above the composer** — one of prompts, one of models — on the row where
   the model picker sits (`chatPickerHtml`, `chatPage.ts:93-107`). Decided 2026-09-09: TWO rows,
   not one merged list of presets; they are two kinds of decision — *what shall answer* and *what
   shall be asked* — and one button that sets both silently cannot be predicted from its name.
4. **CRUD in a tab of its own** — the list, edit of title AND text, a LARGE prompt box. Two
   sections in one tab.
5. **The sidebar box becomes a picker** (a dropdown of names plus *Edit presets…* into the tab) —
   which is why entry 1's "35 % wider" was withdrawn.
6. **Empty Enter after a model switch = ask the OTHER model the same thing**, carrying the
   conversation minus the last answer (entry 24).

## What is already there

- **The shape of a named prompt** — `PromptChoice { id, role, label, purpose }` in `prompts.ts`;
  chosen by `choosePrompt` (`panelProvider.ts:887`). That catalog is shipped-and-overridden, not
  person-created; this is its sibling, and follows its naming.
- **A tab of its own** — twice: `roundsLog.ts` + `roundsLogPanel.ts`, `helpPage.ts` + `helpPanel.ts`.
  A page module and a thin panel host. Do not invent a third arrangement.
- **The re-ask machinery** — `carriedTurn(said, question, language, budget, fenceId)`
  (`chatPrompt.ts:172`) and its call at `chatCommand.ts:225-226`. Entry 24 is one specific pair of
  arguments: `said` = the messages minus the last model answer; `question` = the person's last
  question, verbatim. `send()` already refuses an empty box (`chatPage.ts:204`), so the gesture is
  free to take.
- **Colour** — `vendorPalette`; and the shipped decision that colour is an edge, not a box.

## Storage — and the one migration that must not be got wrong

- `coai.chatPromptPresets`: `{ id, name, text, main }[]`.
- `coai.chatModelPresets`: `{ id, name, provider, model, server?, startingPrompt? }[]`.
- **The single `coai.chatPrompt` a person already typed becomes the first prompt preset, ticked
  MAIN** — on first read, never dropped, never left as a fifth setting nothing reads. `chatPrompt`
  itself stays declared for one release, read only by the migration, then removed with a
  CHANGELOG line.
- Keep the chat settings' two existing decisions: NOT in `OVERLAID_SETTINGS`
  (`settingsShape.ts:222`) and NOT mirrored to the server (`chatSettings.ts:11-16` says why).
- Validation at the boundary: a preset with an empty name or empty text is dropped with a logged
  reason; exactly one `main` (the first wins if several).

## Colours — DECIDED 2026-09-09

The operator asked for one colour on the model row and another on the prompt row, unobtrusive but
legible, and then delegated the choice (*"выбери сам, потом изменим если что"*). Decided:

- **Model buttons: an EDGE (left border, the reviewer-card precedent) in the button's own vendor
  colour from `vendorPalette`.** A flat single colour would have taught the opposite of the rule
  every other surface teaches — one vendor, one colour, everywhere — and a model button is a
  vendor's button.
- **Prompt buttons: one neutral accent that is NOT in the palette** —
  `var(--vscode-textLink-foreground)` as the edge, the same token links wear — so a prompt can
  never be mistaken for a vendor. Same edge treatment, one colour for the whole row.
- Both rows: no filled boxes, the button background stays the theme's `--vscode-button-secondaryBackground`;
  the edge is the only colour. "Unobtrusive" is the edge; "legible" is the label in the ordinary
  foreground.

Cheap to change — it is a border colour, not a data shape — which is why the choice was safe to
take now.

## The shape

- `chatPresets.ts` (pure): read/validate/migrate both lists; `mainPrompt()`; `presetById()`.
- `chatPresetsPage.ts` + `chatPresetsPanel.ts`: the CRUD tab — two lists, add/edit/delete, the
  big editor (a textarea with `field-sizing: content`, min 12 rows), a *main* tick on prompts.
  Command `coai.editChatPresets` (manifest + help article + a *Edit presets…* link in the panel).
- The tab's picker row: prompt buttons (click → the composer's text becomes that prompt, or the
  turn opens with it), model buttons (click → switch, as the picker does today).
- The sidebar: *What to ask about the selection* becomes a select of prompt names + the link.
- **Re-ask:** in `chatCommand.ts`'s send path, `text === '' && modelChangedSinceLastAnswer` →
  `carriedTurn(messages minus the last model answer, lastQuestion, …)`. The Send button reads
  *Re-ask · <model>* in that state (caption over the bridge); empty Enter with NO model change does
  nothing, as today. The rejected answer STAYS on screen, excluded only from what is sent. A
  Team server spends one of `REMOTE_TURNS`, said in the caption. If the last message is the
  person's own question (a failed or stopped turn), the re-ask is simply that question again.

## Build order

1. RED tests (below).
2. `chatPresets.ts` with the migration — pure, tested first.
3. The CRUD tab (page + panel + command + help article).
4. The two button rows in the tab; the sidebar picker.
5. The re-ask branch and the Send caption.
6. GREEN; whole suite; manual: migrate a real `settings.json`, create three prompts and two
   models, re-ask from gemini to another model, recorded.

## Test plan

- `chatPresets.test.ts` (new): migration of a legacy `chatPrompt`; validation drops junk with a
  reason; one main; ids unique.
- `chatPresetsPage.test.ts` (new): both lists render; the editor is large; the main tick is
  exclusive.
- `chatPage.test.ts`: two rows; a button per preset; the Send caption in the re-ask state.
- `chatWiring.test.ts`: `EmptyEnterAfterAModelSwitch_ReAsksWithoutTheLastAnswer` — the fake session
  receives `carriedTurn` of messages minus the last model turn, question = the last `you` text;
  `EmptyEnterWithoutASwitch_SendsNothing`.
- `helpCoverage.test.ts` passes with the new command and settings.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/presets-above-the-composer`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   goal, and the diff `main...feat/presets-above-the-composer` — three dots; the two-dot moving-base trap is recorded in
   `research/PLAN_the_gate_diffs_from_a_moving_base.md`. `resolve`; repeat until `proceed`.
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

**Specific to this plan:** TWO NEW SETTINGS and ONE NEW COMMAND → manifest entries, help articles (helpCoverage), README (the presets tab is a person-facing feature — a paragraph and, if the README carries screenshots, one); `module_extension.md` gains the presets module, the page/panel pair, the re-ask rule; CHANGELOG in the person's words, including the migration sentence and the colour decision taken.

## Definition of Done

- [ ] Prompts and models are saved lists with names; one prompt is main; the legacy `chatPrompt` migrated into it.
- [ ] Two button rows above the composer; the sidebar is a picker plus a link into the CRUD tab.
- [ ] The CRUD tab lists, adds, edits (title and text, large editor) and deletes, for both kinds.
- [ ] Empty Enter after a model switch re-asks with the conversation minus the last answer; the old answer stays on screen; nothing is sent when nothing changed.
- [ ] The colours are as decided above: vendor edge on model buttons, the link-colour edge on prompt buttons, no filled boxes.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

**Last in the chat-page lane** — depends on the composer plan and the provider plan. Its pure half
(`chatPresets.ts`, migration, tests) and the CRUD tab (new files) have no conflicts and can be built
ahead; its `panelView.ts` half conflicts with
[PLAN_the_prompt_box_forgets.md](PLAN_the_prompt_box_forgets.md) — that one lands first.
