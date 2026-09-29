# PLAN — every page reads alike: the text controls everywhere, one column, and the gates apart from the consultations

> Status: **IMPLEMENTED, 2026-09-29 — S1 to S5 shipped on `feat/pages-read-alike` in one pull request, under one plan round and one code round, plus the fixes my own code review found after it. Still open after the merge: the extension release (0.60.0), which publishes and so waits for the operator; and question 1 below — whether the sidebar should carry the controls too.** What shipped differently is in § *What shipped differently*. Scope: `src_vs_code` — the pages' text
> controls, the Settings tab's typography, the Gate commands and Review roles frames, the sidebar's
> Active rounds section, the help in five languages, the READMEs and their screenshots.
>
> Related docs: [PLAN_settings_page.md](PLAN_settings_page.md) (the Settings tab this builds on),
> [module_extension.md](module_extension.md), [architecture.md](architecture.md).
>
> Revised after the plan round (coai, `good_enough`, 3/3) and my own review beside it: the `rem` sizing,
> the MCP pane's `zoom`, the render paths that must carry both values, a live CSS defect on Review roles,
> the Notifications script's handle, and run tests in place of script-text checks. What each finding
> changed is in § *What the reviews changed*.

## The goal, as the operator asked it (2026-09-29, after extension 0.59.0)

1. In the sidebar, split **Active rounds** in two: **Active gates** and **Active consultations**.
2. The **Review roles** page: limit its content column to the width Chat presets uses.
3. The **Gate commands** page: the same style as Chat presets.
4. **Every page** carries the text-size and text-brightness controls — "we have them; you forgot to
   apply them. Check again."
5. The Settings tab's **MCP server** tab: there is room now — make its text twice as large.
6. A fresh README screenshot of the sidebar and the Settings tab.

Settled by the same answer and changing nothing: the sidebar order stays (Notifications, Active …,
Phrases, Bugz); no second door into Settings; the Settings tab does not come back after a window reload
— it is opened by hand when it is needed.

## What is true today (verified 2026-09-29 on `origin/main` fb1408c3)

**The controls.** The ± text size is `zoomControl.ts` (`zoomControlHtml`, `zoomStyle`, `zoomScript`,
`ZOOM_CSS`) with its host `uiScaleHost.ts` (`applyZoomDelta`, `pushUiScaleTo`); the ± text tone is
`textTone.ts` (`toneControlHtml`, `toneStyle`, `toneScript`, `TONE_CSS`) with `textToneHost.ts`. Both
settings are global (`coai.uiScale`, `coai.textTone`). A host PUSHES the value when a page opens and when
the setting changes (`uiScaleHost.ts:34-45`) — so a page that later replaces its `webview.html` keeps the
value only if its own html is built with it. There are eleven `createWebviewPanel` call sites; the sidebar
is the one `WebviewView` (`panelProvider.ts:403`).

| Page | host | size | tone |
|---|---|---|---|
| Chat | `chatPanel.ts` | yes | yes |
| Help | `helpPanel.ts` | yes | yes |
| Review bugs | `bugzReviewPanel.ts` | yes | yes |
| Chat presets | `chatPresetsPanel.ts:112` | yes | **no** |
| Phrases | `phrasesPanel.ts:61` | yes | **no** |
| Review roles | `rolesPanel.ts:310` | yes, **broken** (below) | **no** |
| Gate commands | `commandsPanel.ts:54` | **no** | **no** |
| Notifications | `notificationsPanel.ts:113` | **no** | **no** |
| Review rounds | `roundsLogPanel.ts:125` | **no** | **no** |
| Who holds a key | `bugsKeysPanel.ts:109` | **no** | **no** |
| Settings tab | `settingsPanel.ts` + `panelProvider.ts` | **no** | **no** |

Not pages, and exempt by name: the transient html a panel shows while it loads or cannot read
(`SETTINGS_LOADING`, `settingsPage.ts:82`; the Notifications waiting and unreadable variants,
`notificationsPage.ts:162-181`, which carry no script; the chat restore and notice pages,
`chatRestore.ts:153-187`). A control on a page without a script is a dead control.

**A live defect on Review roles.** `rolesPage.ts:577-580` writes `${zoomStyle(uiScale)}` — a bare
`font-size: 13px;` — at the TOP LEVEL of the stylesheet, outside any rule. The CSS parser reads it and the
rule after it (`.zoomCtl …`) as one invalid selector and drops both, so the page's first paint has
neither its size nor the control's styling; only the host's later push sets the size. The same trap is
already documented at `chatPage.ts:788` and `textTone.ts:97-99`.

**Four pages re-render.** Gate commands (`commandsPanel.ts:66`, `:76`, `:83-89`), Notifications
(`notificationsPanel.ts:218`), Who holds a key (`bugsKeysPanel.ts:454`) and Review rounds
(`roundsLogPanel.ts:140-142`) replace `webview.html` repeatedly; none of their builders takes a size or a
tone today.

**The Notifications script** names its handle `api`, which may be null (`notificationsPage.ts:228`); the
shared fragments call `vscode.postMessage` (`zoomControl.ts:66`, `textTone.ts:130`). Its host's message
branch (`notificationsPanel.ts:145-156`) has no size or tone case.

**The widths.** Chat presets and Phrases put `max-width: 900px; margin: 0 auto` on `body`
(`chatPresetsPage.ts:199`, `phrasesPage.ts:132`). Review roles has no width (`rolesPage.ts:581`); Gate
commands has neither a width nor a background, and its inputs are the browser's own — the white boxes in
the operator's screenshot (`commandsPage.ts:151`). The two pages' shared rules are interleaved with their
own and differ in detail (`input, select, textarea` against `input, textarea`), so a byte-for-byte
extraction is not possible.

**The Settings tab's type.** It is drawn from the sidebar's stylesheet (`pageDocument`,
`panelView.ts:479`), which holds **22** `font-size: <n>px` declarations — 11px notes, 10px badges
(`panelView.ts:2720-3027`) — plus a fixed 14px `.help` circle with a 14px line height (`:2876-2878`) and
two flex bases in `rem` (`:2755`, `:2757`) that resolve against the browser's 16px root. The MCP server
tab is built of `.hint`, `.stale`, `button.link` and `.status` (`panelView.ts:1640-1679`), the 11px
classes. The shared `button { width: 100%; margin: 6px 0 0 }` (`:2864-2868`) would stretch the controls'
buttons into bars.

**The sidebar.** One registry entry, `rounds` / *Active rounds*, draws two live regions:
`liveRegion('rounds') + liveRegion('consultations')` (`panelView.ts:387`). Each region has its own empty
sentence (`panelView.ts:2547`, `consultations.ts:208`). Which sections are open is held in memory,
accepts any id and starts EMPTY (`panelProvider.ts:290`, `:905-908`; `OPEN_BY_DEFAULT = []`,
`panelView.ts:339`). Section headings are coloured by id (`panelView.ts:2739-2782`; `.sec-rounds` at
`:2778`).

## Decisions (the operator may overrule any of them)

| # | Decision | Why |
|---|---|---|
| D1 | The two sections are **Active gates** (id `rounds`, which keeps its colour) and **Active consultations** (a new id `consultations`, with a heading colour of its own), in that order where Active rounds was. The new id needs no migration, and it starts closed like every section. | The operator wrote "active gates / active consultan…"; the cards are consultations. The cadence lines stay with the gates: they say where the gate stands. |
| D2 | The **sidebar** gets neither control. | It is a view, not a page: VS Code's own zoom sizes it, and its title bar is already full. Asked at the end, not guessed. |
| D3 | The column is **900px**, Chat presets' value. | The operator: "1000, or whatever Chat presets has". |
| D4 | **Twice as large** is `zoom: 2` on the MCP server pane. | Everything in the shared sheet is sized from the root (D5), which a parent's `font-size` does not reach — `2em` would double only the unsized text and leave the 11px notes, the very text that reads small. `zoom` scales every length in the pane, `rem` included; the webview is Chromium. |
| D5 | Every pixel size in the shared stylesheet that text depends on becomes `calc(<n>rem / 13)` — the 22 font sizes, the `.help` circle, the two `rem` flex bases (`6rem` → `calc(96rem / 13)`) — and the root is set explicitly: `html { font-size: var(--vscode-font-size) }` in the sidebar, the size control's value on the Settings tab (its script moves `documentElement` as well as `body`). | `rem`, not `em`: `em` resolves against the PARENT, so a converted note inside a converted row would shrink twice. `rem` resolves against the root alone. At the default 13px the sidebar renders exactly as today. |
| D6 | Chat, Help and Review bugs keep their own parsers and hosts. `textControlFrom` is the parser for the eight pages that take the unit; it is not called "the one parser". | They already handle both messages; rewiring three correct pages is change for its own sake. The census runs them with the rest. |
| D7 | The screenshots are rendered, not photographed: the real page html, with demo state, in headless Chrome, under a Dark Modern token set. | No screen capture of a running editor is reachable from here. The PR says so. |

## The design

- **`textControls.ts`** (page side, no `vscode`): `textControlsHtml(size, tone)`, `textControlsStyle(size,
  tone)` (declarations for INSIDE a `body` rule), `TEXT_CONTROLS_CSS`, `textControlsScript(handle =
  'vscode')` — the handle NAMED, because Notifications calls its handle `api` — and `textControlFrom(raw)`,
  which clamps the delta to one step as the pages do today. `TextSettings { size, tone }` for the two
  builders that take positional arguments.
- **`textControlsHost.ts`**: `pushTextControlsTo(webview): Disposable` (one disposable for both pushes)
  and `applyTextControl(control)`.
- **Every render path carries both values.** States that already carry `uiScale` gain an optional
  `textTone` (Help's convention: absent is the theme's own); Gate commands' and Notifications' states gain
  both; `roundsLogHtml` and `usersPageHtml` take a trailing `TextSettings`. Each host reads the two
  settings at every `webview.html =` site, not only at creation.
- **`formPageStyle.ts`**: Chat presets' frame and fields as named pieces — `FRAME` (body 900px centred,
  editor background, header row, `h1`/`h2`, `.lead`), `FIELDS` (themed `input, select, textarea`, `button`,
  `button.remove`), `CARD` (`.preset`, left rule). Chat presets and Phrases take them with their own rules
  kept after; Gate commands takes all three (each command a card, its monospace `textarea` rule kept AFTER
  the fields so `font: inherit` does not reset it; `.command`, `.stale`, `.badge`, `.marker`, `.note`
  kept); Review roles takes `FRAME`. The size and tone go inside the `body` rule on every one of them.
- **The Settings tab**: `PanelState` gains `uiScale` and `textTone`; `settingsHtml` draws the two controls
  above the tab strip — outside `settingsBody`, so they are not in the paint key and a press never
  repaints — and roots `html`/`body` in them; `SETTINGS_CSS` resets the controls' buttons (`width: auto;
  margin: 0`); the host pushes both; the dispatcher gains `zoom`/`tone` branches in `receive`.
- **The sidebar**: the registry entry splits in two (D1); a heading colour for `.sec-consultations`.

## Build order — five stories, one branch, one gate

### S1 — One unit for the two controls, on every page

- **The shims first**, each widening with its own test: per-node click listeners (the size and tone
  scripts bind each button), `style.setProperty` recorded, `document.documentElement.style`, in
  `test/rolesPageHarness.ts` — the one shared shim — and wherever `panelPageHarness.ts` must follow.
- **RED first — the census, as a run test**: every page's html rendered through its real builder, its own
  script RUN in the shim with the four control buttons in the document. Each must (a) draw both controls,
  (b) post `{type:'zoom'}` and `{type:'tone'}` when they are pressed, (c) apply a pushed `uiScale` and a
  pushed `textTone` to the body. Fails today on eight of the ten it renders (Chat: its own tests).
- **RED first — the Review roles defect**: parsed with `test/cssRules.ts`, the roles page's `body` rule
  carries the font size and the `.zoomCtl` rule exists. Fails today.
- `textControls.ts`, `textControlsHost.ts`; the eight pages, hosts and parsers take them (`rolesEdit.ts`'s
  `kind` unions gain `'tone'`); every render path carries both values; Notifications draws the controls
  only on the variant that has a script, and its host gains the branch.
- Pixel and root sizes on those pages the control could not reach: `bugsKeysPage.ts` (six), and the two
  bodies rooted in `var(--vscode-font-size)` (`roundsLog.ts:1358`, `notificationsPageStyle.ts:13`).
- A host census: every file that creates a webview panel pushes both settings (structural — every host
  imports `vscode` — paired with a known-instance test naming the eleven hosts, so a scan that stopped
  matching cannot pass empty).

### S2 — The Settings tab reads like a page

- RED first: no `font-size: <n>px` left in the shared panel stylesheet (22 today).
- D5 (`rem`, the root); the two controls on the Settings tab; the buttons' reset; D4.
- The MCP server pane's notes resolve at twice the body's size — asserted over the PARSED stylesheet
  (`cssRules.ts`): `.sec-server` carries `zoom: 2` and the notes inside it carry no size that escapes it.
  Rendered at the largest step (+5) in Chrome to see that nothing clips.

### S3 — Gate commands and Review roles take the Chat presets frame

- `formPageStyle.ts`; Chat presets' and Phrases' parsed rule sets are unchanged from their S1 output
  (the baseline is taken after S1, which adds the tone control to both).
- Gate commands: the frame, the fields, a card per command. Review roles: the frame.

### S4 — Active gates and Active consultations

- The registry split, the heading colour; the sidebar renders five sections in order, each live region
  exactly once. `activeRounds.test.ts:295`, which asserts the old title, moves to the new ones.
- Every LIVE sentence that says *Active rounds*: the help in all five languages — including the article
  title *Active rounds: what is running right now* (`helpContent.ts:390`) and its four translations, and
  the sidebar sentence at `:508` — the READMEs, and the code comments. History is not rewritten:
  `CHANGELOG.md` and the implemented `research/PLAN_*.md` records keep the name they had.

### S5 — The screenshots, and the documents

- `scripts/render-page.mjs` (a page module's html, with a theme token set and demo state, for Chrome);
  a sidebar image and a Settings tab image under `assets/`, placed in both READMEs.
- `research/module_extension.md` (the text-controls unit, the frame module, the two sections).

## Test plan

| Story | Test | What it would see if the behaviour were deleted |
|---|---|---|
| S1 | the shims' own tests; the page census, RUN (draws both, posts both, applies both pushes); the Review roles body rule and `.zoomCtl` rule, parsed; `textControlFrom` table (zoom, tone, a delta of 7 clamped to 1, junk refused); each converted page's parser maps `tone`; a re-render of Gate commands, Notifications, Review rounds and Who holds a key still carries the size and tone; the host census | a page with no brightness control; a press that does nothing; a size lost on the next redraw |
| S2 | no px font size in the shared sheet; the Settings page RUN (draws both, applies both, moves the root); the MCP pane's `zoom` over the parsed sheet; a size press does not change `settingsKey` | the notes not following the size; a press that reloads the tab; buttons drawn as bars |
| S3 | Chat presets' and Phrases' parsed rules unchanged; Gate commands and Review roles carry `FRAME` (900px) and Gate commands the themed fields, parsed | white boxes; a page that runs to the window's edge |
| S4 | the sidebar's sections and order; each region exactly once; the new heading colour; no LIVE help article in any language says *Active rounds*, and every language names both new sections | cards drawn twice or not at all; a help sentence naming a section that is gone |
| S5 | a script, not a test; the images are looked at | — |

Layout is checked by rendering the pages in headless Chrome before and after (the DOM shims have no
layout); the PR says which images were looked at. Before the code round: `npm run compile`, `npm test`,
`npm run lint`, `npm run test:host`, `plan-lifecycle.mjs`, `pin-check.mjs`.

## What the reviews changed

| Finding | From | What changed |
|---|---|---|
| `em` compounds through nested sizes | coai plan round (gemini) | D5 is `rem` with an explicit root |
| a new section id and its state | coai plan round (gemini) | verified in-memory and id-agnostic; D1 corrected — every section starts closed |
| every open page must follow a press | coai plan round (codex) | the host census; each host pushes both |
| help deletions would pass | coai plan round (codex) | every language must NAME both new sections |
| D4 and D5 cancel: `rem` escapes a `2em` pane | my review | D4 is `zoom: 2` |
| four pages lose the values on a redraw | my review | every render path carries both |
| Review roles' size rule sits outside any rule | my review | a RED test and the fix |
| the Notifications handle is `api` | my review | the script takes its handle's name |
| full-width buttons on the Settings tab | my review | the reset in `SETTINGS_CSS` |
| run tests cannot pass in today's shims; a script-text check is refused (`.agents/PROJECT.md`, *a webview page is tested by RUNNING it*) | my review | the shims widen first; the census runs every page |
| a byte-for-byte extraction is not possible | my review | parsed rule sets, baseline after S1 |
| history names Active rounds | my review | the DoD covers live text only |
| the `.help` circle and the `rem` bases | my review | converted with the fonts |
| transient pages; "one parser" | my review | exempt by name; D6 reworded |

Rejected, with the reasons in the round's record: a press silently failing through the paint key (the
push moves the page, not the key), a structural check that a function is called (a run test exists), and
localization bundles (there are none; UI names stay English in every translation).

## What shipped differently (recorded at promotion, 2026-09-29)

| Story | As planned | As shipped |
|---|---|---|
| S1 | the census over all eleven pages | ten pages rendered and RUN; the Chat page's two controls are asserted by its own tests (`chatPage.test.ts`), because its state is large enough that a copy of its fixture here would be the duplication the census exists to prevent |
| S1 | the size and tone read into each page's state | read when each page's html is BUILT on the two hosts whose state is gathered before awaits — the Settings tab (`panelProvider.pageFor`) and Review roles — found by my own review: a press during a render in flight was otherwise drawn over by the old value, with an unchanged paint key |
| S1 | hosts push both and apply both | also: Phrases and Review roles take a press OUT of their own write queue first (`appliedTextControl`), as the other four raw-message hosts do — in the queue it flushed a half-typed edit and a failed save read as the page's own |
| S1 | — | a step that rounds to nothing is not a press (`oneStep`) |
| S2 | the 22 font sizes, the `?` circle, the two `rem` bases | also the Settings tab's two 64px number boxes, which clipped a price at a large size |
| S2 | — | the Notifications title's margin moved to the header that now holds it; its waiting and unreadable pages (no script, so no push) are drawn in the chosen size |
| S3 | Chat presets' and Phrases' parsed rules unchanged, as a test | checked ONCE, by a script, against the build before the change and recorded in the S3 commit: Chat presets identical as a set, Phrases gaining only `select` in the field rule and an `h2` rule, neither of which it draws. Not a lasting test: its baseline would have been a frozen copy of the very rules the module now owns |
| S3 | Review roles takes the frame | its 900px column landed in S1, with the size defect's fix, and S3 moved it onto `formBodyCss` |
| S5 | `scripts/render-page.mjs` | as planned, and `browserLayout.mjs` widened with `screenshot()` rather than a second helper; it removes the old picture before rendering and checks the browser's exit, so a failed render is never reported as one |
| all | — | the host wiring (every host imports `vscode`) is pinned structurally — the host census over blanked source, the Settings dispatcher's early return, the raw-message hosts' order — each paired with a known-instance list |

The real-editor scenarios ran 15 of 15. The coai code round ran over the five stories; the review fixes after it were not re-gated, by the rule of one code round per piece of work.

## Growth surfaces

Two small modules and two PNG screenshots in `assets/`, each kept under 300 KB as the existing ones are.
No setting, file, cache or process.

## Questions for the operator (the plan proceeds on the answer in brackets)

1. Should the **sidebar** carry the two controls too? **[no — D2]**
2. "Active consultan…" — **Active consultations**? **[yes — D1]**

## Boundaries

| Plan | This plan owns | That plan keeps | Order |
|---|---|---|---|
| [PLAN_the_sidebar_pays_only_for_what_it_shows.md](../todo/PLAN_the_sidebar_pays_only_for_what_it_shows.md) | the section registry's split of *rounds* into two entries | what a render gathers per surface | independent; that plan reads the registry as it finds it |
| [PLAN_two_files_outgrew_the_rule.md](../todo/PLAN_two_files_outgrew_the_rule.md) | the shared stylesheet's sizes (a value change inside the CSS constant) | moving the CSS out of `panelView.ts` | this first; that move then re-measures |
| [PLAN_the_page_tests_run_the_page.md](../todo/PLAN_the_page_tests_run_the_page.md) | the NEW census and the shim widenings it needs | converting the existing source-text assertions to run tests | independent; the widened shim is there for it to use |
| [PLAN_the_panel_provider_is_too_big.md](../todo/PLAN_the_panel_provider_is_too_big.md) | two `zoom`/`tone` branches in `PanelProvider.receive` | every extraction cluster | independent; whichever lands second rebases |

Disjoint otherwise.

## Definition of Done

- [x] Every page — the eleven, the transient ones exempt by name — carries both controls, applies both
      pushes, and keeps them across its own redraws.
- [x] The Settings tab's text all follows the size control; the MCP server tab is twice the size.
- [x] Gate commands and Review roles sit in the 900px column; Gate commands' fields are themed.
- [x] The sidebar shows Active gates and Active consultations; no LIVE sentence (UI, help ×5, READMEs,
      code comments) says Active rounds.
- [x] The README shows the sidebar and the Settings tab as they are now.
- [x] `research/module_extension.md` describes the text-controls unit and the frame module.
- [x] One plan round and one code round; verdicts in the PR.
- [ ] **Open:** extension released after the merge — with the operator's go-ahead.
