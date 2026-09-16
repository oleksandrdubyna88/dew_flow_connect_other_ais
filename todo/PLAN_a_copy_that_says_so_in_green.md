# PLAN — a copy that says so in green

> Status: **plan only, nothing implemented yet.** Scope: the panel's *Phrases* section — one CSS rule
> in `src_vs_code/src/panelView.ts` and the test that pins it to the attribute the script writes.
>
> Issue: [#322](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/322).
> Related docs: [module_extension.md](../research/module_extension.md).

## The symptom

Press a phrase button in the sidebar. It copies, and for one second its label changes from the
phrase's name to *Copied*, then changes back. The operator's words: «при копировании появляется
копиед. и потом меняется на название. это хорошо. но это слово копед должно быть зеленого цвета.
что б было видно. сейчас тяжело заметить».

The acknowledgement is real and correct — it arrives only after the clipboard write **resolved**
([panelView.ts:551-556](../src_vs_code/src/panelView.ts#L551-L556)) — but nothing about it looks
different. One short word in the same weight and the same colour, inside a row of buttons that all
carry short words, in a section that may hold a dozen of them. It is easy to press a phrase and not
know whether anything happened.

## What is there today

The flip is entirely in the panel's own page script:

- [panelView.ts:560-573](../src_vs_code/src/panelView.ts#L560-L573) — the `copied` message handler.
  It finds the button by comparing `dataset.id` (never by building a selector out of a person's
  string — that was four reviewers' finding), then:
  - `pressed.dataset.said = '1'`, which is the guard against acknowledging one button twice;
  - `pressed.textContent = 'Copied'`;
  - after `COPIED_FOR_MS` the label is restored and `data-said` is deleted.
- [panelView.ts:721](../src_vs_code/src/panelView.ts#L721) — `COPIED_FOR_MS = 1000`.
- [panelView.ts:754](../src_vs_code/src/panelView.ts#L754) — the button's markup:
  `class="run phrase" data-command="copyPhrase" data-id="…"`, with the phrase's colour inline on
  `border-left-color`.
- [panelView.ts:2379-2383](../src_vs_code/src/panelView.ts#L2379-L2383) — the generic `button` rule:
  filled, `--vscode-button-background` under `--vscode-button-foreground`.
- [panelView.ts:2352](../src_vs_code/src/panelView.ts#L2352) — `.phrases .run`, which supplies the
  3 px left edge the inline hue paints.

**So the state already exists in the DOM and nothing paints it.** `data-said="1"` is written and
removed on exactly the right clock, by code that is already tested
([panelPhrasesScript.test.ts](../src_vs_code/src/test/panelPhrasesScript.test.ts)). This change adds
the missing half — a rule keyed on that attribute — and no script change at all.

## The decision: green TEXT, on the panel's own background

`--vscode-charts-green` is the house green: `panelView.ts:2417`, `:2434`, `:2477`, `:2485`, and
`panelView.test.ts:470` states the rule as *"green from the theme, not a hex of ours"*.

Painting green **text** straight onto the filled button is the one thing not to do: `#89D185` on
`#0E639C` is about 3.7:1, which is a colour you can argue about rather than see. Two readable shapes
were considered:

| | What it looks like | Why not / why |
|---|---|---|
| Green **fill**, `--vscode-editor-background` text (the `.badge.running` shape, `panelView.ts:2485`) | the whole button turns green for a second | unmissable, but the word itself is not green, and the operator asked for the word |
| **Chosen:** green text on `--vscode-editor-background`, weight 600 | the button drops its blue fill for a second and the word *Copied* stands in green | literally what was asked, ~9:1 against the panel's own background, and it reads as a state rather than as a second button |

The left edge keeps its phrase colour throughout, so the button never stops being identifiable as
that phrase.

## Build order

1. **RED test** in `src_vs_code/src/test/panelPhrasesScript.test.ts` — the file that already RUNS the
   panel script. It asserts the pair, not one half:
   - running the script, a confirmed copy leaves `dataset.said === '1'` on the pressed button and
     nothing on the others, and the tick clears it (this half passes today — it is the anchor);
   - the shipped stylesheet contains a rule whose selector carries `[data-said="1"]` **and** whose
     body declares `var(--vscode-charts-green)`.

   Pinning both halves in one test is deliberate: a rule keyed on a class the script does not write
   is a dead rule that a text-matching test would call green. (`common/testing.md` — a structural
   assertion must pin the whole condition.)

2. **The rule**, beside `.phrases .run` at `panelView.ts:2352`:

   ```css
   .phrases .run[data-said="1"] {
     background: var(--vscode-editor-background);
     color: var(--vscode-charts-green);
     font-weight: 600;
   }
   ```

3. **Green**: run the suite, report the red message and the green one.

## Test plan

```bash
cd src_vs_code && npm run compile && node scripts/run-tests.mjs
```

`out/` is removed first — a rename leaves both names behind and the count silently inflates.

- New: the selector↔attribute pair above.
- Unchanged and must stay green: the five existing tests in `panelPhrasesScript.test.ts`
  (the script parses; the pressed button says *Copied*; the label comes back; an id holding a quote
  is still found; a message naming another phrase changes nothing).

## What this plan does NOT do

- It does not touch `copyText.ts`, `phraseCopy.ts` or the host round trip. The acknowledgement's
  timing is already correct and is not the complaint.
- It does not give the chat's own copy controls the same treatment — that is
  [#313](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/313), a different
  surface with a different control, planned separately.

## Reuse note

Two CSS-rule parsers already exist in the test tree — `rules`/`ruleFor`
(`src_vs_code/src/test/chatPage.test.ts:255-267`) and `rulesFor`
(`src_vs_code/src/test/vendorClassIsNotACard.test.ts:67`) — and **neither is exported**; both are
local to their file. The assertion here is a different question (does the selector match the
attribute the script writes?), so this plan writes the narrow assertion where the script is already
run rather than extracting a third caller's worth of shared helper. If a fourth CSS assertion
appears, extracting one `cssRules` test helper is the right move and should take all three callers.

## Definition of Done

- [ ] The RED test was observed failing with a message naming the real symptom — no rule paints the
      copied state — before the rule existed.
- [ ] The rule is in place; the same test passes; the whole extension suite is green.
- [ ] The green is a theme variable, never a hex.
- [ ] No script change: `data-said` is written and cleared exactly as it is today.
- [ ] `research/module_extension.md` mentions the acknowledgement's appearance if it describes the
      phrases section.
- [ ] Plan promoted to `research/` with `IMPLEMENTED <date>` in the same task, `todo/README.md`
      updated, `plan-lifecycle.mjs` green.
