# PLAN — a phrase is the same colour in the editor and on its button, and its boxes say what they are

> Status: **IMPLEMENTED, 2026-09-15.** Kind: **feature** (three asks, one
> surface pair). Scope: `src_vs_code/src/phrases.ts` (one pure allocator),
> `src_vs_code/src/phrasesPage.ts` (the stripe and the field labels),
> `src_vs_code/src/panelView.ts` (the stripe on the sidebar buttons), and their tests. Origin:
> [issue #295](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/295) — the
> operator: *(1) the list entries need coloured stripes on the left, different colours; (2) the same
> colours on the stripes beside the corresponding buttons; (3) put field names on the edit form —
> it is not clear which box is which (title, value).*
>
> Related docs: [module_extension.md](module_extension.md), [module_tests.md](module_tests.md).
>
> ## Deviations — what shipped differently, and why
>
> 1. **Three of the plan round's objections were MEASURED rather than argued, and all three were
>    refuted.** They were serious enough to have changed the design, so the measurements are the
>    record: *"only seven hues, so eight phrases collide"* — 8 ids give **8 distinct colours**, 12
>    give **12**, and the first repeat is the thirteenth, because the anchored vendor slots are
>    offered LAST rather than withheld; *"adding a phrase re-sorts the list and shuffles the
>    colours"* — adding a seventh moved **0 of 6**, and a late-sorting id moved **0**; *"the add
>    flow builds rows client-side, so new ones render unlabelled"* — `phrasesPage.ts` contains no
>    `createElement`, `innerHTML` or `appendChild` at all, and the host rewrites the whole document.
>    Had any been true, the design would have had to change; taking them on trust would have meant
>    writing a second allocator this repository already argues against.
> 2. **The half of that finding which was right became a test.** The editor and the sidebar build
>    allocators from their own state and are separate webviews on their own clocks, so they can be
>    one phrase apart — there is now a test that renders them exactly that way and asserts the
>    shared phrase's colour is unchanged.
> 3. **The multi-phrase assertion was added** (codex, the plan round): three ids and one cross-surface
>    phrase would have stayed green for a page that painted every row the same fallback, which is the
>    reported defect. An eight-phrase render now asserts every row's colour is distinct.
> 4. **`PHRASE_FALLBACK_COLOUR` was extracted** so the stylesheet's fallback and the module that
>    decides colours name one value rather than two copies of a token.
>
> **Checked, and not done:** that any of it is visible. There is no layout engine in this suite, so a
> label hidden by CSS or an edge of zero width would not be caught here — the `for`/`id` pairing,
> the declarations and the inline values are the whole of the evidence.

## The symptom

**Edit phrases** is a stack of identical boxes. Every one has the same left edge —
`phrasesPage.ts:127` gives `.phrase` a single `border-left-color: var(--vscode-textLink-foreground)`
for all of them — so with six phrases the page is six identical rectangles and the only way to find
one is to read it.

The same six are buttons in the sidebar's **Phrases** section (`panelView.ts:717`), and nothing
visually connects a button to the box that defines it. Press the wrong one and you find out from the
clipboard.

And the edit form has **no labels at all** — verified: `grep -c '<label'` in `phrasesPage.ts`
returns **0**. The only cues are two `placeholder` attributes (`phrasesPage.ts:115`, `:118`), and a
placeholder vanishes the moment the box has content, which is the normal state of a saved phrase.

## What already exists, and what does not

**There is an allocator, and it is the only one.** `vendorPalette(ids)` (`vendorColour.ts`) hands
each id in a list its own colour and promises no two in that list collide — it sorts
deterministically, and its docblock is an argument for why such a promise can only be made over a
LIST rather than one item at a time. That argument is the same one this issue needs.

**There is no category to colour by.** `Phrase` is `{ id, name, text }` (`phrases.ts`) and nothing
else; the stored row carries unknown keys verbatim, so there is no hidden grouping either. The
colour must therefore come from the row's **identity**, not from a kind — which is precisely what a
list-wide allocator does.

**There is no labelled-field helper to reuse.** `panelView.ts` has a `labelled()` but it is bound to
the `HelpKey` catalog and private to that module; the roles page labels its role fields but not its
prompt fields. So the labels here are plain `<label for=…>`, which is also what a screen reader
needs and what the roles page's own labelled fields already are.

## What must be true when it is done

1. Each entry in **Edit phrases** has a left edge in its own colour; no two phrases in one list share
   one (up to the palette's size).
2. A phrase's button in the sidebar carries **the same colour** as its box in the editor.
3. The colour is stable for a phrase across a repaint and across the two surfaces — it is a function
   of the id list, not of position in the DOM.
4. Both boxes on the edit form carry a **visible label**, associated with the control (`for`/`id`), so
   it is both readable and announced.
5. Nothing about what a phrase IS or DOES changes: the id, the name, the text, the clipboard payload,
   the Remove button, the add flow and the save-as-you-type are untouched.
6. An empty list renders exactly as it does now.

## The design

**`phrases.ts`** — one new exported pure function, beside the type it colours:

```ts
/** A colour per phrase, decided over the whole LIST so two of them cannot collide. */
export function phraseColours(ids: readonly string[]): (id: string) => string
```

It delegates to `vendorPalette(ids)`. **Reuse, not a second allocator**: writing a hash here would be
a second implementation of a capability that already exists, and the file that owns it spends thirty
lines explaining why a per-item hash is the wrong shape. Two notes, recorded rather than discovered
later:

- The five ANCHORED ids in that palette (`codex`, `antigravity`, `gemini`, `claude`, `local`) are
  offered **last**, not withheld — so a short phrase list draws from the seven unanchored hues first
  and a longer one reaches the anchors rather than repeating. **Measured**: 8 ids → 8 distinct
  colours, 12 → 12, 13 → 12. That is why a phrase and a vendor will not usually share a colour, and
  why "up to the palette's size" means twelve rather than seven. *(The first draft of this bullet
  said the slots were held back, which the plan round read — correctly — as meaning a collision at
  eight. The behaviour was then measured rather than argued about; see deviation 1.)*
- A phrase id is `phrase-<base36>` or `phrase-<n>` (`savedRows.ts`), so **no phrase id can ever be an
  anchored vendor name** — the two lists cannot interfere.

**`phrasesPage.ts`**

- `PhraseRowView` gains nothing; the page builds the allocator once from `state.rows.map(r => r.id)`
  and passes each row its colour, the same shape `reviewersBody` uses.
- `phraseRow` emits `<div class="phrase" data-id="…" style="border-left-color:…">`.
- `.phrase` (`phrasesPage.ts:127`) keeps its border and its 3px width and keeps a fallback colour;
  only the per-row hue moves inline.
- The two controls become labelled:
  ```html
  <label for="phrase-name-…">Name</label>   <input id="phrase-name-…" data-field="name" …>
  <label for="phrase-text-…">What it copies</label> <textarea id="phrase-text-…" data-field="text" …>
  ```
  "Name" and "What it copies" rather than "title/value": the operator named the two boxes by what
  they are in the data, and these say what they are to the person — the first is the button's label,
  the second is what lands on the clipboard.

**`panelView.ts`**

- `phrasesBody` builds `phraseColours(phrases.map(p => p.id))` and gives each button
  `style="border-left-color:…"`.
- `.phrases .run` (`panelView.ts:2301`) gains the 3px left border and a fallback, so the inline hue
  has something to paint.

### The repaint key already covers this

`staticKey` includes `state.phrases?.map(p => [p.id, p.name, hoverFor(p)])` (`panelView.ts:2664`).
The colour is derived from the id LIST, and the ids are in that key, so adding or removing a phrase
already forces the full repaint that re-allocates the colours. **Nothing needs to change there** —
checked rather than assumed, because a colour that did not repaint would be a stale colour.

## What this does NOT do

- It does not touch `phrasesEdit.ts`, `phraseCopy.ts`, the storage shape or the clipboard path.
- It does not add a category or a kind to `Phrase`. The colour is identity, not meaning — and the
  name is still written on every button and in every box, so colour is never the only signal.
- It does not colour the chat presets, which have their own stripe idiom on a page whose test forbids
  the inline form used here. A second surface is a second decision.

## Growth budget

Nothing is created, stored, spawned or cached. Two allocator constructions per repaint over a list
whose realistic size is tens — the same call the Reviewers section already makes. No growth surface.

## Build order

1. **RED** — the tests below, watched failing. *(Written before the implementation this time: the
   previous plan in this series recorded a deviation for doing it the other way round.)*
2. `phraseColours` in `phrases.ts`.
3. The stripe and the labels in `phrasesPage.ts`.
4. The stripe on the sidebar buttons in `panelView.ts`, and the CSS both need.
5. **GREEN** — the same tests, then the whole suite.
6. `research/module_extension.md` and `research/module_tests.md`; `CHANGELOG.md` under 0.47.0,
   naming issue #295. All BEFORE the `git mv` of the promotion.
7. `plan-lifecycle.mjs` and `pin-check.mjs`, before the final commit.

## Test plan

Homes: `src_vs_code/src/test/phrasesPage.test.ts` and `src_vs_code/src/test/phrasesSection.test.ts`.

1. **`phraseColours` is tested by calling it** — three ids get three different colours, and the same
   id gets the same colour from a second allocator built over the same list. Both halves: "all
   different" alone passes for an allocator that is unstable between calls.
2. `'a phrase is the same colour in the editor and on its button'` — the **cross-surface** assertion,
   and the one that guards the feature. It renders `phrasesHtml` AND `panelHtml` over the same
   phrases and asserts the colour for a given id is the same string in both. This is the assertion
   that fails if either surface builds its own list, which is the defect the allocator exists to
   prevent, and it is the lesson from this series' previous plan: a per-surface test can be green
   while the two surfaces disagree.
3. `'each entry keeps its frame'` — the `.phrase` rule still carries its border and its 3px left
   width, asserted from the page's own stylesheet. Without it the inline hue paints nothing; the same
   for `.phrases .run` in the panel's stylesheet.
4. `'both boxes say what they are'` — a `<label for=…>` exists for each control, the `for` matches the
   control's `id`, and the visible text is non-empty. The `for`/`id` pairing is the half that makes it
   a label rather than a caption.
5. `'nothing about a phrase changed'` — `data-field="name"`, `data-field="text"`, `data-remove`,
   `data-id` and the values survive; an empty list still renders its existing empty-state line.
6. **Teeth, by reverting**: remove the inline stripe and watch 2 fail; restore, then remove the
   `.phrase` border rule and watch 3 fail; restore, then drop one label and watch 4 fail. Each revert
   targets a different production line and must redden a different test.
7. Whole suite: `cd src_vs_code && npm test`. Baseline on this branch's base: 2960 tests, 2959 pass,
   1 skipped, 0 fail.

## Definition of Done

- [x] The tests were written first and watched RED — "two phrases share a colour: var(--vscode-textLink-foreground)" three times over, "the name box has no label", "phrase-1 has no colour of its own".
- [x] Every entry has its own colour — asserted over eight rows, not three; the same phrase is the same colour in both surfaces, including when they are one phrase apart.
- [x] Both edit-form controls carry a label whose `for` names a control `id` that exists.
- [x] No id, name, text or hook changed; the empty state is unchanged.
- [x] Whole suite green: 2964 tests, 2963 pass, 1 skipped, 0 fail.
- [x] `research/module_extension.md`, `research/module_tests.md` and `CHANGELOG.md` updated.
- [x] `plan-lifecycle.mjs` and `pin-check.mjs` clean, run before the `git mv` and again before the final commit.
- [x] Indexed — the `research/README.md` row on promotion.

## Acceptance — the gate on both sides

1. `review_plan` over this document before the first line of code; `resolve` every finding.
2. RED test per defect, then the whole suite — never one file alone.
3. **Rebase onto `origin/main` immediately before the code round**, then `review_code` with this
   document as the scope and `baseRef` `origin/main`; `resolve` every finding.
4. Pull request only after the code round is resolved.
5. Five minutes after opening: read CI and the automated reviewer, verify each comment against the
   code and the rules, fix or answer, resolve the threads, merge by rebase.
6. Promotion inverts the checklist's order — every edit while the file is still in `todo/`, the
   `git mv` LAST, `git add` the destination, one commit — with the DoD ticked and the deviations
   recorded.

Specific to this plan: no new command and no new setting, so `helpCoverage.test.ts` has nothing to
demand. `settingsAreDeclared.test.ts` scans the panel for global `data-setting` controls; the phrase
buttons carry `data-command`, not `data-setting`, so it stays quiet.
