# PLAN — a copy that says so in green

> Status: **plan only, nothing implemented yet.** Scope: the panel's *Phrases* section — one CSS rule
> in `src_vs_code/src/panelView.ts` and the test that pins it to the attribute the script writes.
>
> Issue: [#322](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/322).
> Related docs: [module_extension.md](../research/module_extension.md).
>
> Revised after its plan round (session `c1770c33`, 3 reviewers, 10 findings, 8 accepted). What the
> round changed is recorded in *What the gate changed* at the foot.

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
  `border-left-color`, inside `<div class="phrases">`.
- [panelView.ts:2379-2383](../src_vs_code/src/panelView.ts#L2379-L2383) — the generic `button` rule:
  filled, `--vscode-button-background` under `--vscode-button-foreground`. `button:hover` at `:2384`
  changes the background only.
- [panelView.ts:2352](../src_vs_code/src/panelView.ts#L2352) — `.phrases .run`, which supplies the
  3 px left edge the inline hue paints.

**So the state already exists in the DOM and nothing paints it.** `data-said="1"` is written and
removed on exactly the right clock, by code that is already tested
([panelPhrasesScript.test.ts](../src_vs_code/src/test/panelPhrasesScript.test.ts)). This change adds
the missing half — a rule keyed on that attribute — and no script change at all.

## The decision, and the measurement behind it

`--vscode-charts-green` is the house green: `panelView.ts:2417`, `:2434`, `:2477`, `:2485`, and
`panelView.test.ts:470` states the rule as *"green from the theme, not a hex of ours"*.

The plan round asked — twice, from two vendors — for the contrast claim to be verified rather than
asserted, and it was right to: the first draft's figures were guessed. **Measured**, by reading the
colour-registry defaults out of the installed VS Code
(`resources/app/out/vs/workbench/workbench.desktop.main.js`) and the backgrounds out of
`extensions/theme-defaults/themes/*.json`, then computing WCAG 2.x contrast:

| Foreground | Ground | Ratio | |
|---|---|---|---|
| `charts.green` `#89D185` | editor bg, dark `#1F1F1F` | **9.04:1** | AA |
| `charts.green` `#388A34` | editor bg, light `#FFFFFF` | **4.33:1** | AA-large; 0.17 short of AA-normal |
| `charts.green` `#89D185` | editor bg, hc-dark `#000000` | **11.52:1** | AA |
| `charts.green` `#374e06` | editor bg, hc-light `#FFFFFF` | **9.31:1** | AA |
| *the shape this plan rejects* | | | |
| `charts.green` `#89D185` | **button bg**, dark `#0078D4` | **2.48:1** | fails |
| `charts.green` `#388A34` | **button bg**, light `#005FB8` | **1.46:1** | fails |
| *the token a reviewer proposed instead* | | | |
| `testing.iconPassed` `#73c991` | editor bg, dark `#1F1F1F` | 8.24:1 | AA |
| `testing.iconPassed` `#73c991` | editor bg, **light** `#FFFFFF` | **2.00:1** | fails badly |

Three things follow, and the first two are the whole decision:

1. **Green text must not go on the filled button.** At 2.48:1 and 1.46:1 that is not a colour anyone
   sees; the first draft's guess of "about 3.7:1" was optimistic by a wide margin. The rule therefore
   drops the blue fill for the second it is on, putting the word on the panel's own ground.
2. **`--vscode-charts-green` stays.** `testing.iconPassed`, which a reviewer suggested as the more
   semantically apt token, defines one pale green for BOTH dark and light and so lands at 2.00:1 on
   a white ground — half of what the house token manages. `debugIcon.startForeground` carries the
   identical values to `charts.green`, and `editorGutter.addedBackground` is lighter still. There is
   no better green foreground token available.
3. **The supported requirement, stated honestly:** ≥4.5:1 (AA-normal) in the default dark and both
   high-contrast themes, and ≥4.3:1 in the default light theme, which is this token's own ceiling
   against its own ground. The three `charts.green`-on-background usages already shipped in this same
   panel (`:2434` `.upd.has-update`, `:2477` `.mark-done`, and the run control at `:2417`) sit at
   exactly the same light-theme ratio; this change does not introduce the shortfall and cannot fix it
   without a hex of our own, which the conventions forbid.

### The rule

```css
.phrases .run[data-said="1"] {
  background: var(--vscode-editor-background);
  color: var(--vscode-charts-green);
  font-weight: 600;
}
```

Specificity `(0,3,1)` — two classes, one attribute, one element — against `button:hover`'s `(0,1,1)`
and `.phrases .run`'s `(0,2,0)`. It therefore wins the background back from the hover state a mouse
is still sitting in, which is the pseudo-class trap a reviewer raised; the test asserts that rather
than trusting the arithmetic. The left edge keeps its phrase colour throughout, so the button never
stops being identifiable as that phrase.

## Build order

1. **RED test** in `src_vs_code/src/test/panelPhrasesScript.test.ts` — the file that already RUNS the
   panel script. **No substring assertion over the page's source text**: `.agents/PROJECT.md:73-83`
   and `.coderabbit.yaml:115-126` refuse a new behavioural one (operator ruling, 2026-09-14), and
   this change has a program to run. Four things are pinned, and they are pinned to each other:

   | # | Asserted | What it catches |
   |---|---|---|
   | a | Running the script, a confirmed copy leaves `dataset.said === '1'` on the pressed button, nothing on the others, and the tick clears it | the attribute the rule keys on stops being written |
   | b | The *rendered* markup puts that button, with class `run`, inside `<div class="phrases">` | the selector's two class hops stop describing a real element |
   | c | The stylesheet, **parsed into rules**, holds a rule whose selector is exactly `.phrases .run[data-said="1"]` and whose body declares `color: var(--vscode-charts-green)` | the rule is missing, renamed, or keyed on something the script never writes |
   | d | Of every rule in the sheet that declares `color` or `background` and whose selector could also match that button, this one has the highest specificity — and where specificity ties, it comes last | a later `:hover`, `:focus` or `.run` rule silently wins |

   (b) is what makes (c) more than a string match: a rule naming `.never[data-said="1"]` satisfies a
   substring check and matches nothing, which is exactly the hole a reviewer opened. (d) is what the
   arithmetic above must not be trusted for. The selector matcher used by (d) **refuses** any
   construct it does not understand rather than returning a match — the failure mode where an
   unparseable selector quietly matches the first element is one this family has already paid for.

   Ask what each would SEE if the rule were deleted: (c) goes red immediately, (d) finds no rule to
   rank. Both are checked by deleting it, not by reasoning about it.

2. **The rule**, beside `.phrases .run` at `panelView.ts:2352`.

3. **Green**, then the checks that are part of the sequence rather than an afterthought:

   ```bash
   cd src_vs_code && rm -rf out && npm run compile && node scripts/run-tests.mjs
   node .agents/conventions/tools/plan-lifecycle.mjs
   ```

   `out/` is removed **by the command, not by remembering** — a rename leaves both names behind and
   the count silently inflates, which has cost this family a wrong test count before.

4. **Prove the test has teeth**: remove the rule, watch the test go red with a message naming the
   real symptom, restore it, watch it go green. Report both observations.

## Test plan

- New: the four-part assertion above, in `panelPhrasesScript.test.ts`.
- Unchanged and must stay green: the five existing tests in that file (the script parses; the pressed
  button says *Copied*; the label comes back; an id holding a quote is still found; a message naming
  another phrase changes nothing), and the whole extension suite.

## What this plan does NOT do

- It does not touch `copyText.ts`, `phraseCopy.ts` or the host round trip. The acknowledgement's
  timing is already correct and is not the complaint.
- It does not give the chat's own copy controls the same treatment — that is
  [#313](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/313), a different
  surface with a different control, planned separately.
- It does not raise the light theme's 4.33:1 to AA-normal. That is the token's ceiling, it is shared
  with three controls already shipped in this panel, and closing it needs a colour of our own.

## Reuse note

Two CSS-rule parsers already exist in the test tree — `rules`/`ruleFor`
(`src_vs_code/src/test/chatPage.test.ts:255-267`) and `rulesFor`
(`src_vs_code/src/test/vendorClassIsNotACard.test.ts:67`) — and **neither is exported**; both are
local to their file, and neither computes specificity or matches a selector against an element, which
is what (d) needs. The parser here is therefore written where the script is already run. If a fourth
CSS assertion appears, extracting one test helper is the right move and should take all three
callers.

## What the gate changed

Accepted, and in this document: the lifecycle check moved into the build sequence (step 3); `rm -rf
out` became part of the command rather than a remark (step 3); the source-text assertion was replaced
by the run-the-page four-part pin (step 1), which the repository's own ruling required and the first
draft broke; the selector must be proved to target a real phrase button (1b); the cascade and the
`:hover` overlap are asserted rather than argued (1d); and the contrast claim was measured across
four themes instead of estimated for one — which changed two numbers and refuted the alternative
token that was proposed.

Rejected, with reasons recorded in the round: two findings asking for screenshot comparison and for
each theme's computed colours to be tested. There is no browser, renderer or theme host in
`node --test`, so neither is buildable; a test hardcoding each theme's tokens would assert my
transcription of them rather than the theme. The buildable half of both — measure the real token
values and state the supported requirement — is done above.

## Definition of Done

- [ ] The RED test was observed failing with a message naming the real symptom — no rule paints the
      copied state — before the rule existed, and observed passing after.
- [ ] The rule's teeth were checked by deleting it, not by reasoning about it.
- [ ] The whole extension suite is green, from a cleaned `out/`.
- [ ] `plan-lifecycle.mjs` is green, run as part of the sequence.
- [ ] The green is a theme variable, never a hex.
- [ ] No script change: `data-said` is written and cleared exactly as it is today.
- [ ] `research/module_extension.md` mentions the acknowledgement's appearance if it describes the
      phrases section.
- [ ] Plan promoted to `research/` with `IMPLEMENTED <date>` in the same task, `todo/README.md`
      updated.
