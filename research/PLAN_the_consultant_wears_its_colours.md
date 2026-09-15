# PLAN — each Consultant row is a framed group in its client's own colour

> Status: **IMPLEMENTED, 2026-09-15.** Kind: **feature** (a small one). Scope:
> `src_vs_code/src/consultantView.ts` (the row's frame and one pure colour function),
> `src_vs_code/src/panelView.ts` (the palette handed in, and one CSS rule), and their tests. Origin:
> [issue #291](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/291) — the
> operator, comparing the Consultant section with Reviewers: *(1) what belongs to claude (codex,
> gemini, another client) should be taken into a frame — a group; (2) give it a coloured stripe on
> the left, and the colour must match the one that provider has in the other sections.*
>
> Related docs: [module_extension.md](module_extension.md), [module_tests.md](module_tests.md),
> [PLAN_vendor_colours_never_repeat.md](PLAN_vendor_colours_never_repeat.md).
>
> ## Deviations — what shipped differently, and why
>
> 1. **The integration assertion is the one that guards the feature, and the plan did not have it.**
>    Every test of `callerColour` can be green while `panelHtml` never hands the palette to
>    `consultantBody` — the section then renders neutral edges in VS Code under a fully green suite.
>    `panelView.test.ts` now asserts the RENDERED page shows one colour for `codex` on its card and
>    on its caller row. **Verified by deletion**: removing the palette from the call reddened only
>    that test, every `callerColour` test staying green — which is what proved the plan's test list
>    insufficient rather than merely thin. (gemini, the plan round.)
> 2. **The plan's "absent means neutral is what a panel with no vendors should draw anyway" was
>    wrong, and the sentence is the deviation rather than the code.** The round pointed out that
>    `panelView` passes `vendorPalette([])` unconditionally, which still answers for the anchored
>    ids — so a zero-reviewer panel shows claude purple, not neutral. That is CORRECT and is what an
>    anchor is for: the consultant picks from the catalogue and is independent of the reviewer list.
>    The fallback stays for a caller that genuinely has no palette; the claim about what a
>    zero-vendor panel draws was removed.
> 3. **The hint comparison was added** (codex, the plan round): the row test pinned `data-setting`,
>    `data-caller` and `id` but not the guidance text, so a refactor could have dropped a hint and
>    passed. The hints are now compared whole against an unframed render.
> 4. **The RED was observed by reverting, not before writing the code.** The tests and the
>    implementation were written together and went green on their first run, which is not the order
>    the rule asks for. Corrected the way the rule prescribes for a fix that landed first: the stripe
>    was removed and the tests watched fail — *"claude's row carries no edge colour of its own"*,
>    *"codex is a different colour in Consultant than on its reviewer card"* — then restored, then
>    the wiring deleted separately to confirm which test catches that. Recorded rather than papered
>    over.
>
> **Checked, and not done:** that the frame is visible. Nothing in this repository has a layout
> engine — the page harness executes a page's script against a DOM shim with no CSS — so the
> declaration, the class and the inline colour are what is checkable, and that is the whole of the
> evidence. The rendered result in a real VS Code webview was NOT observed.

## The symptom

**REVIEWERS** draws one card per vendor: a bordered box with a 3px left edge in that vendor's
colour, and the same colour on its name in *Active rounds* and in the rounds log — so a vendor can
be followed from its settings to its running round without reading.

**CONSULTANT** draws one row per CALLER — *Claude Code asks…*, *Codex asks…*, *Gemini asks…*,
*Another client asks…* — and they are four flat `.field` blocks. There is no frame, nothing groups a
caller's controls visibly, and there is no colour anywhere. Verified: the row's only wrapper is
`<div class="field consultant-row" data-caller="…">` (`consultantView.ts:328`), and **`.consultant-row`
has no CSS rule at all** — grepped across `src_vs_code/src`, the string appears once, at that emit.
It is a data hook, not a style hook.

The only consultant-specific rule in the whole panel is the section heading's tone,
`panelView.ts:2237`.

## What makes this small

Both halves already exist, and the second one is the reason this is worth doing rather than merely
possible.

**The frame.** `.role` (`panelView.ts:2405`) is exactly the shape asked for, and its comment says
why it is an edge rather than a filled box: *"A left edge rather than a filled box: it marks the role
at a glance without turning the settings panel into four coloured slabs, and it survives a light
theme unchanged."* `.vendor` (`panelView.ts:2304-2308`) is the same shape with the colour supplied
inline, because it is computed per vendor rather than named by a class.

**The colour.** `vendorPalette(configuredIds)` (`vendorColour.ts:180-200`) is the single allocator,
and **three of the four caller ids are ANCHORED in it** — `vendorColour.ts:90-96` pins
`codex: 0` (Blue), `gemini: 4` (Green), `claude: 6` (Purple), and an anchor's slot is held even when
that vendor is not configured (`ANCHOR_SLOTS`, offered last by `candidates()`). So `claude`, `codex`
and `gemini` get *the colour they already have everywhere else* for free, and the cross-view promise
the palette exists for is kept rather than approximated.

## The one real decision: what `other` wears

`other` is **not** anchored. Passed to the palette it would hash into a slot computed against the
empty set and **can collide with a configured reviewer's colour** — `vendorColour.ts:26-29` accepts
that for a passing stray, but here it would be a permanent fixture of the section.

**The operator's ruling, 2026-09-15: a neutral grey, not a palette colour.** `other` is
*"Another client"* — not a vendor — so it should not look like one, and nothing can then collide with
it. It takes `var(--vscode-widget-border)`, the same token `.role-code` already uses for the same
"this is a container, not an entity" job.

The two rejected alternatives, recorded so they are not re-proposed: anchoring `other` as a sixth
reserved slot would consume a palette colour and **move the colours of currently unanchored
reviewers**, which `vendorColour.test.ts:119-130` pins; hashing it is the collision above.

## What must be true when it is done

1. Each of the four Consultant rows is a framed box — a border, and a 3px coloured left edge.
2. `claude`, `codex` and `gemini` wear **the same colour they wear on their reviewer card**, coming
   from the same allocator rather than from a second list.
3. `other` wears a neutral edge and no palette colour.
4. The palette is built from the **configured reviewer ids**, exactly as every other caller of it is
   — never from the four caller kinds, which would be a second list and a second assignment.
5. Nothing about the rows' controls, ids, `data-setting`/`data-caller` hooks or hints changes.
6. The colour is never the only signal: each row already names its caller in words, and keeps doing so.

## The design

**`consultantView.ts`**

- `ConsultantViewState` (`consultantView.ts:27`) gains:
  ```ts
  /** The reviewers' own palette, so a caller wears the colour its vendor has everywhere else. */
  readonly colour?: VendorPalette;
  ```
  Optional, because every existing test constructs this state and none of them cares; absent means
  every row falls back to the neutral edge. **That fallback is for a caller with no palette, not for
  a panel with no vendors** — the panel always passes one, including when `state.vendors` is empty,
  and the anchored ids still answer there. A zero-reviewer panel therefore shows `claude` in purple,
  which is correct: the consultant picks from the CATALOGUE and is independent of the reviewer list,
  and an anchor exists precisely so a vendor's colour does not depend on being configured. (The
  first draft of this paragraph claimed neutral edges were "what a panel with no vendors should draw
  anyway"; the plan round caught that the code does the opposite, and the code is right.)
- A new exported pure function, tested by calling it:
  ```ts
  /** The stripe for a caller: its vendor's own colour, or a neutral edge for "another client". */
  export function callerColour(callerId: string, colour: VendorPalette | undefined): string
  ```
  `other` → `var(--vscode-widget-border)`; anything else → `colour?.(callerId)` or the same neutral
  when no palette was handed in.
- `row()` (`consultantView.ts:325`) emits
  `<div class="field consultant-row" data-caller="…" style="border-left-color:${…}">` — the inline
  style is the established idiom for a computed colour (`panelView.ts:979`) and is CSP-legal, since
  the page declares `style-src 'unsafe-inline'` (`panelView.ts:342`).

**`panelView.ts`**

- The `consultantBody(...)` call (`panelView.ts:322`) passes
  `colour: vendorPalette(state.vendors.map((v) => v.id))` — the same expression `reviewersBody` uses
  at `:788`, which is what makes requirement 2 true by construction rather than by coincidence.
- One CSS rule, modelled on `.role` and carrying the fallback width and colour:
  ```css
  .consultant-row { border: 1px solid var(--vscode-widget-border); border-left: 3px solid var(--vscode-widget-border);
                    border-radius: 3px; padding: 6px 8px 2px; margin: 0 0 8px; }
  ```

## What this does NOT do

- It does not touch `consultantWrite.ts`, the settings shape, or any `data-setting` hook — nothing
  about what the rows DO changes.
- It does not touch `vendorColour.ts`. No anchor is added and no palette entry moves; this is a new
  consumer of the existing allocator, not a change to it.
- It does not colour the live consultation cards above the section (`consultations.ts`), which reuse
  the rounds `.round`/`.badge` classes. A second surface is a second decision.
- It does not touch the `.ts-*` classes of the Team-server rows, which also have no CSS — noted while
  reading, left alone, and written down here so it is not mistaken for something this change broke.

## Growth budget

Nothing is created, stored, spawned or cached. One extra `vendorPalette(...)` construction per
repaint over a list of at most a few dozen ids — the same call the section above it already makes.
No growth surface.

## Build order

1. **RED** — the tests below, watched failing.
2. `callerColour` and the `colour` field in `consultantView.ts`.
3. The frame in `row()`, the palette at the call site, the CSS rule.
4. **GREEN** — the same tests, then the whole suite.
5. `research/module_extension.md` and `research/module_tests.md`; `CHANGELOG.md` under 0.47.0,
   naming issue #291. All before the `git mv` of the promotion.
6. `plan-lifecycle.mjs` and `pin-check.mjs`, before the final commit.

## Test plan

Home: `src_vs_code/src/test/consultant.test.ts`, whose discipline is to assert the decided VALUE
rather than the markup (`consultantView.ts:6-12`) — which is exactly what a colour decision is.

1. **`callerColour` is tested by calling it**, per caller id: `claude`, `codex` and `gemini` each
   return what `vendorPalette(configured)` returns for that same id — asserted against the palette,
   not against a hard-coded hex, so the test cannot drift from the allocator.
2. `'another client is not dressed as a vendor'` — `other` returns the neutral token and **not**
   whatever the palette would give it. Both halves: a test asserting only "it is the neutral token"
   would pass if the palette happened to return that token.
3. `'a caller wears the same colour as the reviewer card of the same name'` — the value from
   `callerColour('codex', palette)` equals the value the reviewer card is built with for `codex` in
   `panelHtml`. This is the cross-view promise, and it is the assertion that fails if anybody builds
   a second palette from the caller kinds.
4. `'no palette means a neutral edge, not a crash and not a colour'` — `colour` absent.
5. `'every row is framed'` — the rendered section contains four `consultant-row` divs each carrying a
   `border-left-color`, and the CSS carries the `.consultant-row` rule with its border. Both, because
   an inline colour with no rule is a colour on an unframed box.
6. `'the rows still do what they did'` — the `data-setting`, `data-caller` and `id` hooks of a row are
   unchanged, so the frame cannot have been bought by rewriting the controls.
7. Whole suite: `cd src_vs_code && npm test`. Baseline on this branch's base: 2954 tests, 2953 pass,
   1 skipped, 0 fail.

## Definition of Done

- [x] The RED was observed by reverting the stripe and the wiring separately — "claude’s row carries no edge colour of its own", "codex is a different colour in Consultant than on its reviewer card". See deviation 4: this was done after the fact, not before.
- [x] Four framed rows; `claude`/`codex`/`gemini` in their own colours, `other` neutral.
- [x] The palette is built from the configured reviewer ids, and the cross-view equality is asserted over the RENDERED page, not only over the function.
- [x] No control, hook or hint changed — hooks pinned per row, hints compared whole against an unframed render.
- [x] Whole suite green: 2953 tests, 2952 pass, 1 skipped, 0 fail.
- [x] `research/module_extension.md`, `research/module_tests.md` and `CHANGELOG.md` updated.
- [x] `plan-lifecycle.mjs` and `pin-check.mjs` clean, run before the final commit.
- [x] Indexed — the `research/README.md` row on promotion.

## Acceptance — the gate on both sides

1. `review_plan` over this document before the first line of code; `resolve` every finding.
2. RED test per defect, then the whole suite — never one file alone.
3. **Rebase onto `origin/main` immediately before the code round**, then `review_code` with this
   document as the scope and `baseRef` `origin/main`; `resolve` every finding.
4. Pull request only after the code round is resolved; at most three open on this repository.
5. Five minutes after opening: read CI and the automated reviewer, verify each comment against the
   code and the rules, fix or answer, resolve the threads, merge by rebase.
6. Promotion inverts the checklist's order — every edit while the file is still in `todo/`, the
   `git mv` LAST, `git add` the destination, one commit — with the Definition of Done ticked and the
   deviations recorded.

Specific to this plan: no new command and no new setting, so `helpCoverage.test.ts` has nothing to
demand. `settingsAreDeclared.test.ts` scans the rendered panel for global `data-setting` controls;
the new wrapper carries none and the existing ones keep their `data-caller`, so it stays quiet — but
it is the test that would catch a frame built by moving a control.
