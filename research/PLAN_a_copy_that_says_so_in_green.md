# PLAN — a copy that says so in green

> Status: **IMPLEMENTED, 2026-09-16.** Scope: the panel's *Phrases* section — one CSS rule in
> `src_vs_code/src/panelView.ts`, a shared CSS-cascade test helper, and the assertion that pins the
> rule to the attribute the script writes.
>
> Issue: [#322](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/322).
> Related docs: [module_extension.md](module_extension.md).
>
> Both rounds are recorded at the foot: what the plan round changed before a line was written, and
> what the code round changed after — including the one real defect it caught.

## The symptom

Press a phrase button in the sidebar. It copies, and for one second its label changes from the
phrase's name to *Copied*, then changes back. The operator's report, translated: *"when you copy, it
says Copied, and then it changes back to the name — that is good. But that word Copied should be
green, so it can be seen. Right now it is hard to notice."* (The original is on the issue.)

The acknowledgement was real and correct — it arrives only after the clipboard write **resolved**
(`panelView.ts:551-556`) — but nothing about it looked different. One short word in the same weight
and the same colour, inside a row of buttons that all carry short words, in a section that may hold a
dozen of them. It was easy to press a phrase and not know whether anything had happened.

## What was there already

The flip is entirely in the panel's own page script, and none of it changed:

- `panelView.ts:560-573` — the `copied` message handler. It finds the button by comparing
  `dataset.id` (never by building a selector out of a person's string — that was four reviewers'
  finding), then sets `pressed.dataset.said = '1'`, writes the label, and after `COPIED_FOR_MS`
  restores the label and deletes the attribute.
- `panelView.ts:721` — `COPIED_FOR_MS = 1000`.
- `panelView.ts:754` — the markup: `class="run phrase" data-command="copyPhrase" data-id="…"`, inside
  `<div class="phrases">`.
- `panelView.ts:2379-2384` — the generic `button` rule (filled) and `button:hover`.

**So the state already existed in the DOM and nothing painted it.** This change added the missing
half and no script change at all.

## The decision, and the measurement behind it

`--vscode-charts-green` is the house green (`panelView.ts:2417`, `:2434`, `:2477`, `:2485`;
`panelView.test.ts:470` states it as *"green from the theme, not a hex of ours"*).

The plan round asked — twice, from two vendors — for the contrast claim to be verified rather than
asserted, and it was right to: the first draft's figures were guessed. Measured by reading the
colour-registry defaults out of the installed VS Code
(`resources/app/out/vs/workbench/workbench.desktop.main.js`) and the grounds out of
`extensions/theme-defaults/themes/*.json`, then computing WCAG contrast:

| Foreground | Ground | Ratio | |
|---|---|---|---|
| `charts.green` `#89D185` | **sidebar**, dark `#181818` | **9.74:1** | AA |
| `charts.green` `#388A34` | **sidebar**, light `#F8F8F8` | **4.07:1** | AA-large |
| `charts.green` `#89D185` | registry dark `#252526` | 8.40:1 | AA |
| `charts.green` `#388A34` | registry light `#F3F3F3` | 3.90:1 | AA-large |
| `charts.green` `#89D185` | hc-dark `#000000` | 11.52:1 | AA |
| `charts.green` `#374e06` | hc-light `#FFFFFF` | 9.31:1 | AA |
| *the shape this plan rejects* | | | |
| `charts.green` `#89D185` | **button bg**, dark `#0078D4` | **2.48:1** | fails |
| `charts.green` `#388A34` | **button bg**, light `#005FB8` | **1.46:1** | fails |
| *the token a reviewer proposed instead* | | | |
| `testing.iconPassed` `#73c991` | editor bg, dark `#1F1F1F` | 8.24:1 | AA |
| `testing.iconPassed` `#73c991` | editor bg, **light** `#FFFFFF` | **2.00:1** | fails badly |

Four things follow:

1. **Green text must not go on the filled button.** At 2.48:1 and 1.46:1 that is not a colour anyone
   sees; the first draft's guess of "about 3.7:1" was optimistic by a wide margin.
2. **The background goes to `transparent`, not to a colour of its own.** This is the code round's
   correction and the one real defect it caught. The panel is a SIDEBAR view whose body is already
   transparent, so the ground here is `sideBar.background` — `#181818` where the editor is `#1F1F1F`
   — and the first implementation's `background: var(--vscode-editor-background)` would have painted a
   lighter rectangle onto that ground for exactly one second. Every figure in the table above is
   against the real ground, which is also why the light theme reads 4.07 rather than the 4.33 the
   plan round was given.
3. **`--vscode-charts-green` stays.** `testing.iconPassed`, suggested as the more semantically apt
   token, defines one pale green for BOTH modes and lands at 2.00:1 on a white ground — half what the
   house token manages. `debugIcon.startForeground` carries identical values to `charts.green`, and
   `editorGutter.addedBackground` is lighter still. There is no better green foreground token.
4. **The supported requirement, stated honestly:** ≥4.5:1 in the default dark and both high-contrast
   themes; ≥3.9:1, i.e. AA-large, in the default light theme, which is this token's own ceiling
   against its own ground. The three `charts.green`-on-background controls already shipped in this
   panel sit at the same ratio; closing it would need a colour of our own, which the conventions
   forbid.

### The rule

```css
.phrases .run[data-said="1"] { background: transparent; color: var(--vscode-charts-green); font-weight: 600; }
```

Specificity `(0,3,1)` — two classes, one attribute, one element — against `button:hover`'s `(0,1,1)`
and `.phrases .run`'s `(0,2,0)`. It therefore wins the background back from the hover state a mouse
is still sitting in; the test asserts that rather than trusting the arithmetic. The left edge keeps
its phrase colour throughout, so the button never stops being identifiable as that phrase.

## How it was tested

**No substring assertion over the page's source text.** `.agents/PROJECT.md:73-83` and
`.coderabbit.yaml:115-126` refuse a new behavioural one (operator ruling, 2026-09-14), and this
change has a program to run and a stylesheet to parse. Four things are pinned, and they are pinned
**to each other**:

| | Asserted | What it catches |
|---|---|---|
| a | Running the panel's own script, a confirmed copy leaves `dataset.said === '1'` on the pressed button and nothing on the others, and the tick clears it | the attribute the rule keys on stops being written |
| b | The *rendered* markup puts that button, with class `run`, inside `<div class="phrases">` | the selector's two class hops stop describing a real element |
| c | The stylesheet, **parsed into rules and matched against that element**, yields exactly one green painter, `.phrases .run[data-said="1"]` | the rule is missing, renamed, or keyed on something the script never writes |
| d | Of every rule that could also paint that button, this one has the highest specificity — later on a tie | a `:hover`, `:focus-visible` or longhand `background-color` rule silently wins |

(b) is what makes (c) more than a string match: `.never[data-said="1"]` satisfies a substring check
and matches nothing, which was the plan round's sharpest finding.

**Every guard was proved by breaking it**, not by reasoning about it:

| Sabotage | The failure it produced |
|---|---|
| the rule absent (its original state) | *nothing in the panel stylesheet paints the copied state green* — expected `['.phrases .run[data-said="1"]']`, actual `[]` |
| a later `.phrases .run.phrase[data-said="1"]:hover { color: … }` | *another rule outranks the acknowledgement* — named it |
| a later `.phrases .run.phrase[data-said="1"] { background-color: … }` | the same, which is what the longhand widening bought |
| an `@media` block anywhere in the sheet | *the stylesheet did not parse as flat rules, so every verdict over it is unsound* |

## What shipped

| File | |
|---|---|
| `src_vs_code/src/panelView.ts` | the rule, with the measurement in its comment |
| `src_vs_code/src/test/cssRules.ts` | **new** — the shared CSS-cascade test helper: flat-rule parser with a proof that the sheet was flat, specificity, a selector matcher that answers `true`/`false`/`undefined` and never guesses, and the painter/ranking pair |
| `src_vs_code/src/test/panelPhrasesScript.test.ts` | the four-part assertion, on that helper |
| `research/module_extension.md` | the appearance and the measurement, beside the existing account of this button |

## Deviations from the plan as reviewed

- **The background changed from `var(--vscode-editor-background)` to `transparent`**, for the reason
  in point 2 above. The plan round's contrast table was therefore measured against the wrong ground
  and every light-theme figure moved by about 0.26.
- **The CSS helpers were extracted into `cssRules.ts` rather than written inside the test.** The plan
  argued for keeping them local; the code round called that Blocking under the reuse rule and was
  right. The two pre-existing private parsers (`chatPage.test.ts`'s `rules`/`ruleFor`,
  `vendorClassIsNotACard.test.ts`'s `rulesFor`) were **not** migrated onto it — that is proposed, not
  done, because rewriting neighbouring tests uninvited turns a one-rule change into a diff nobody
  asked to review.
- **`paints()` counts `background-` longhands**, which the plan did not think of; a rule setting
  `background-color` alone restores a fill just as completely as the shorthand.
- **The parser proves the sheet is flat** instead of assuming it, after a reviewer pointed out that a
  nested block slides every following rule out of alignment while each still looks like a rule.

## What the rounds changed

**The plan round** (session `c1770c33`, 3 reviewers, 10 findings, 8 accepted) moved the lifecycle
check into the build sequence, made `rm -rf out` part of the command rather than a remark, replaced
the source-text assertion with the run-the-page pin, required the selector to be proved against a
real button, required the cascade to be asserted rather than argued, and required the contrast claim
to be measured — which changed two numbers and refuted the alternative token proposed alongside it.
Two findings were rejected with reasons: both asked for screenshot comparison or for each theme's
computed colours to be tested, and there is no browser, renderer or theme host in `node --test`; a
test hardcoding each theme's tokens would assert a transcription of them rather than the theme.

**The code round** (12 reviewers, verdict `proceed`) found the sidebar-versus-editor ground, the
longhand gap, the flat-parse assumption, and the reuse violation — all four are above. It also asked
five times over, correctly, for this plan to be promoted rather than left claiming nothing had been
built.

## Definition of Done

- [x] The RED test was observed failing with a message naming the real symptom before the rule
      existed, and passing after.
- [x] Every guard's teeth were checked by breaking it, not by reasoning about it.
- [x] The whole extension suite is green from a cleaned `out/`.
- [x] `plan-lifecycle.mjs` is green, run as part of the sequence.
- [x] The green is a theme variable, never a hex.
- [x] No script change: `data-said` is written and cleared exactly as it was.
- [x] `research/module_extension.md` records the appearance and the measurement.
- [x] Promoted to `research/` with its deviations, `todo/README.md` updated.

## Open tail

The two pre-existing private CSS parsers should move onto `cssRules.ts`. Not done here, deliberately;
it wants its own change, and the helper was written to take them.
