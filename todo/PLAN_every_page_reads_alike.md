# PLAN — every page reads alike: the text controls everywhere, one column, and the gates apart from the consultations

> Status: **plan only, nothing implemented yet, 2026-09-29.** Scope: `src_vs_code` — the pages' text
> controls, the Settings tab's typography, the Gate commands and Review roles frames, the sidebar's
> Active rounds section, the help in five languages, the READMEs and their screenshots.
>
> Related docs: [PLAN_settings_page.md](../research/PLAN_settings_page.md) (the Settings tab this builds on),
> [module_extension.md](../research/module_extension.md), [architecture.md](../research/architecture.md).

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
settings are global (`coai.uiScale`, `coai.textTone`) and pushed to every open page. Who carries what:

| Page | host | size | tone |
|---|---|---|---|
| Chat | `chatPanel.ts` | yes | yes |
| Help | `helpPanel.ts` | yes | yes |
| Review bugs | `bugzReviewPanel.ts` | yes | yes |
| Chat presets | `chatPresetsPanel.ts:112` | yes | **no** |
| Phrases | `phrasesPanel.ts:61` | yes | **no** |
| Review roles | `rolesPanel.ts:310` | yes | **no** |
| Gate commands | `commandsPanel.ts:54` | **no** | **no** |
| Notifications | `notificationsPanel.ts:113` | **no** | **no** |
| Review rounds | `roundsLogPanel.ts:125` | **no** | **no** |
| Who holds a key | `bugsKeysPanel.ts:109` | **no** | **no** |
| Settings tab | `settingsPanel.ts` + `panelProvider.ts` | **no** | **no** |

There is no shared wrapper: each of the three complete pages imports the four page pieces and the two
host pieces on its own, and each page's message parser has its own `zoom` case (`rolesPage.ts:165`).

**The widths.** Chat presets and Phrases put `max-width: 900px; margin: 0 auto` on `body`
(`chatPresetsPage.ts:199`, `phrasesPage.ts:132`). Review roles has no width (`rolesPage.ts:581`); Gate
commands has neither a width nor a background, and its inputs are the browser's own — the white boxes
in the operator's screenshot (`commandsPage.ts:151`).

**The Settings tab's type.** It is drawn from the sidebar's stylesheet (`pageDocument`,
`panelView.ts:479`), which holds **22** `font-size: <n>px` declarations — 11px notes, 10px badges
(`panelView.ts:2720-3017`). A text-size control scales `em`; it cannot reach a pixel size. So a zoom
added to the Settings tab would scale the headings and leave the notes where they are, and those same
11px notes are why the MCP server tab reads small.

**The sidebar.** One registry entry, `rounds` / *Active rounds*, draws two live regions:
`liveRegion('rounds') + liveRegion('consultations')` (`panelView.ts:387`). Each region already says
its own empty state (`panelView.ts:2547`, `consultations.ts:208`).

## Decisions (the operator may overrule any of them)

| # | Decision | Why |
|---|---|---|
| D1 | The two sections are titled **Active gates** and **Active consultations**, in that order, where Active rounds was. The first keeps the id `rounds` (so a person's "open" state for it survives); the second is a new id, `consultations`, open by default like its neighbour. | The operator wrote "active gates / active consultan…"; the cards are consultations. The cadence lines stay with the gates: they say where the gate stands. |
| D2 | The **sidebar** gets neither control. | It is a view, not a page: VS Code's own zoom sizes it, and its title bar is already full. A question for the operator, not a guess — asked at the end. |
| D3 | The column is **900px**, Chat presets' value. | The operator: "1000, or whatever Chat presets has". |
| D4 | **Twice as large** is literal: the MCP server pane is `font-size: 2em`, on top of the page's size control. | It is what was asked, and it composes with the control rather than fighting it. |
| D5 | Pixel font sizes in the shared stylesheet become `calc(<n>em / 13)`. | At the default 13px the sidebar renders exactly as today; the Settings tab's size control then reaches every line. (A person who set VS Code's own font size away from 13 sees the sidebar's small print follow it, which is what that setting asks for.) |
| D6 | The three complete pages (Chat, Help, Review bugs) are not rewired. | They already call the same primitives the new unit composes; rewiring them would change three pages that are right. |
| D7 | The screenshots are rendered, not photographed: the real page html, with demo state, in headless Chrome, under a Dark Modern token set. | No screen capture of a running editor is reachable from here. The PR says so. |

## The design

- **`textControls.ts`** (page side, no `vscode`): `textControlsHtml(size, tone)` — the two existing
  controls side by side; `textControlsStyle(size, tone)`; `TEXT_CONTROLS_CSS` (`ZOOM_CSS` + `TONE_CSS`);
  `textControlsScript()` (`zoomScript()` + `toneScript()`); and `textControlFrom(raw)` — the ONE parser
  of a `zoom`/`tone` message, clamping the delta to one step as the pages do today.
- **`textControlsHost.ts`** (host): `pushTextControlsTo(webview): Disposable` (both pushes, one
  disposable) and `applyTextControl(control)`.
- Each page's own parser asks `textControlFrom` first; each host pushes both and applies both.
- **`formPageStyle.ts`**: the Chat presets frame lifted out — `body` (900px, centred, editor background,
  the size and tone), the header row, `h1`/`h2`, the themed `input`/`select`/`textarea`/`button`, and the
  card (`.preset`, left rule). Chat presets and Phrases use it byte-for-byte as they render today; Gate
  commands takes it whole (each command a card); Review roles takes the frame only.
- **The Settings tab**: `PanelState` gains `uiScale` and `textTone` (read with the rest of the
  configuration), `settingsHtml` draws the two controls above the tab strip and roots the body in them;
  the host pushes both to the tab and the dispatcher applies `zoom`/`tone` from it. The paint key is
  `settingsBody`, which holds neither value, so a press never repaints — the push moves it.
- **The sidebar**: the registry entry splits in two (D1); nothing else moves.

## Build order — five stories, one branch, one gate

### S1 — One unit for the two controls, on every page

- RED first: a census over every page renderer (a table of the eleven pages above, each rendered with its
  existing test fixture) asserting `button[data-zoom]` AND `button[data-tone]`, and a script that handles
  both `uiScale` and `textTone`. It must fail on today's eight.
- `textControls.ts`, `textControlsHost.ts`; the eight pages and hosts take them.
- Pixel sizes on those pages that the control could not reach: `bugsKeysPage.ts` (six), and the two
  bodies rooted in `var(--vscode-font-size)` (`roundsLog.ts:1358`, `notificationsPageStyle.ts:13`).

### S2 — The Settings tab reads like a page

- RED first: no `font-size: <n>px` in the shared panel stylesheet (22 today).
- D5's conversion; the two controls on the Settings tab (design above); D4's MCP server size.

### S3 — Gate commands and Review roles take the Chat presets frame

- `formPageStyle.ts`, extracted so that Chat presets' and Phrases' html is byte-identical before and after
  (asserted against a copy of today's output).
- Gate commands: the frame, the themed fields, a card per command. Review roles: the frame.

### S4 — Active gates and Active consultations

- The registry split; the sidebar renders five sections in order, each live region exactly once.
- Every sentence that says *Active rounds*: the help in all five languages (five mentions each), the
  READMEs, the code comments that name it.

### S5 — The screenshots, and the documents

- `scripts/render-page.mjs` (renders a page module's html with a theme token set and demo state for
  Chrome); a sidebar image and a Settings tab image under `assets/`, placed in both READMEs.
- `research/module_extension.md` (the shared text controls, the frame module, the two sections).

## Test plan

| Story | Test | What it would see if the behaviour were deleted |
|---|---|---|
| S1 | the page census (both controls, both messages); `textControlFrom` table (zoom, tone, a delta of 7 clamped to 1, junk refused); each converted page's parser maps `tone`; a page RUN in the DOM shim posts `{type:'tone'}` on a press | a page with no brightness control; a tone press that does nothing |
| S2 | no pixel font size in the shared stylesheet; the Settings page renders both controls and its script applies both pushes; the MCP server pane is `2em`; a zoom press does not change `settingsKey` | the Settings tab's notes not following the size; a press that reloads the tab |
| S3 | Chat presets and Phrases html identical to before; Gate commands and Review roles carry the frame (900px) and the themed fields | white boxes; a page that runs to the window's edge |
| S4 | the sidebar's sections and their order; each region exactly once; no help article in any language says *Active rounds* | cards drawn twice, or not at all; a help sentence naming a section that is gone |
| S5 | the renderer runs in CI-free mode only (a script, not a test); the images are looked at | — |

Layout is checked by rendering the pages in headless Chrome before and after (the DOM shims have no
layout); the PR says which images were looked at. Before the code round: `tsc`, `npm test`,
`npm run lint`, `npm run test:host`, `plan-lifecycle.mjs`, `pin-check.mjs`.

## Growth surfaces

None that grows: two small modules, two images in `assets/`. No setting, file, cache or process.

## Questions for the operator (the plan proceeds on the answer in brackets)

1. Should the **sidebar** carry the two controls too? **[no — D2]**
2. "Active consultan…" — **Active consultations**? **[yes — D1]**

## Boundaries

| Plan | This plan owns | That plan keeps | Order |
|---|---|---|---|
| [PLAN_the_sidebar_pays_only_for_what_it_shows.md](PLAN_the_sidebar_pays_only_for_what_it_shows.md) | the section registry's split of *rounds* into two entries | what a render gathers per surface | independent; that plan reads the registry as it finds it |
| [PLAN_two_files_outgrew_the_rule.md](PLAN_two_files_outgrew_the_rule.md) | the shared stylesheet's font sizes (a value change inside the CSS constant) | moving the CSS out of `panelView.ts` | this first; that move then re-measures |

Disjoint otherwise.

## Definition of Done

- [ ] All eleven pages carry both controls, and a press on either reaches every open page.
- [ ] The Settings tab's text all follows the size control; the MCP server tab is twice the size.
- [ ] Gate commands and Review roles sit in the 900px column; Gate commands' fields are themed.
- [ ] The sidebar shows Active gates and Active consultations; no sentence anywhere says Active rounds.
- [ ] The README shows the sidebar and the Settings tab as they are now.
- [ ] `research/module_extension.md` describes the text-controls unit and the frame module.
- [ ] One plan round and one code round; verdicts in the PR.
- [ ] Extension released after the merge — with the operator's go-ahead.
