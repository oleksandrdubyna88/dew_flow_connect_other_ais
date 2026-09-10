> Status: **IMPLEMENTED, 2026-09-09** (PR #168). Kind: **feature** — six of the operator's reports
> that are one rendering pass. Scope: `src_vs_code/src/renderAnswer.ts` (new),
> `chatPage.ts`, `chatMessages.ts`, `chatPanel.ts`, `chatCommand.ts`.
> Origin: [../todo/BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entries 4, 5, 6, 7, 8, 18.
>
> ### What shipped differently, and what it cost
>
> **The dependency question went the other way, and the plan round is why.** This plan leaned toward
> widening `bodyHtml` (`helpPage.ts`) to keep the extension's advertised zero runtime dependencies.
> Both reviewers refused it with the same argument: *escape first, mark up second* as a REGEX
> pipeline breaks on a fence containing entities — escaping the string first turns `<` into `&lt;`
> inside the code the reader is shown — and cannot count nesting at all. So `marked` tokenizes and
> the emitting is ours, and the line in `module_extension.md` that advertised no dependencies now
> says one, with the reason beside it.
>
> **`renderAnswer` is not pure in the way this plan promised, and could not be.** It said the
> renderer would decide whether a file reference resolves, and a synchronous pure function cannot ask
> the filesystem anything. gemini caught it on the plan round: the renderer emits a `data-file`
> anchor and the HOST resolves on click. That is better than what was planned — the page never
> carries an `href` at all, so there is nothing for a `javascript:` URL to be.
>
> **The hostile-input file earned its place on its first run.** It found GFM autolinking a bare
> address in prose — the defect this family shipped once already, from the other direction — and a
> weakness in its own assertions: they matched the WORD `onerror`, which flags escaped text that
> merely mentions one and would miss a hostile tag nobody had thought of. They assert on the tag
> names that came out, against the allow-list.
>
> **Three defects the code round found in the implementation.** A workspace containment check written
> as `startsWith(root)`, which admits `/w/app-secret` when the root is `/w/app`; a renderer that
> demanded a dot in a filename where the host's own check did not, so `Dockerfile` and `LICENSE` were
> never offered as links by a page whose host would have opened them; and a copy control at
> `opacity: 0`, invisible to a keyboard and a screen reader while still sitting in the tab order.
>
> **A hazard of this file's shape, three times in one plan:** a backtick in a comment INSIDE a
> template literal ends the literal, and the build then fails on a line of prose. It is a red test
> now.
>
> **Nine of twenty-seven code-round findings were a stale base** read as deletions — `main` moved
> three times while this was being written. Rebasing immediately before the round, not at the start
> of the branch, is the answer.
>
> **The open tail:** copy during a streamed answer. Turns are append-only so an index is stable
> today, but a partial answer becomes copyable mid-flight the moment streaming lands — recorded as a
> DoD item on [../todo/PLAN_an_answer_as_it_arrives.md](PLAN_an_answer_as_it_arrives.md).

## The goal

The conversation reads the way Gemini's app reads: markdown rendered (headings, numbered and
bulleted lists, nesting, code in a code face), links blue and clickable, the two speakers told
apart by colour and by SIDE, the end of each answer visible at a glance, an answer copyable as the
markdown it arrived as, and all of it bright and large enough to read.

Today `chatMessagesHtml` (`chatPage.ts:77-90`) emits `escapeHtml(message.text)` under
`white-space: pre-wrap` (`chatPage.ts:131`) — the raw characters, wrapped — and the whole styling of
a message is four lines (`chatPage.ts:129-132`), where the only difference between the roles is
`opacity: .85`.

## What is already there

- **`bodyHtml` in `helpPage.ts:72`** — paragraphs, `- ` bullets, `**bold**` — and, in its doc
  comment, the safety rule this page needs verbatim: **escape first, mark up second**, so nothing
  in the text can open a tag. Its INPUT is the catalog this repository wrote; a chat answer is
  arbitrary text from another vendor's model. The rule transfers; the trust does not.
- **The theme's link colour** — `var(--vscode-textLink-foreground)` /
  `--vscode-textLink-activeForeground` — already used at `panelView.ts:1363-1366` and
  `roundsLog.ts:781`. Never a hex.
- **The vendor palette** — `vendorPalette` in `vendorColour.ts`, one vendor one colour everywhere;
  and the decision, already shipped for the reviewer cards, that colour is an EDGE, not a filled
  box (`research/PLAN_reviewer_card_colours.md`).
- **The zoom** — `coai.uiScale` with ± in the header (`zoomControl.ts`, `chatPage.ts:174`). This
  plan changes the DEFAULT at scale 0; it adds no second knob.
- **The clipboard through the host** — `vscode.env.clipboard.writeText` at `chatCommand.ts:308`.
  The copy button goes through the bridge to that, not through `navigator.clipboard` in the page.

## The renderer — a decision the gate should see

Two ways; the recommendation is the second.

1. **Widen `bodyHtml`** with headings, ordered lists, nesting, code spans and fences, `---`. Keeps
   zero dependencies; will be wrong about nested lists and tables the day a model emits them, and
   every construct added is another escape rule to get right by hand.
2. **`marked`, bundled by esbuild, pinned, with a CUSTOM RENDERER** — raw HTML passthrough OFF (so
   `<script>` in an answer is text), every emitted tag from a fixed allow-list, and `href`
   through a scheme allow-list (`https:`, `http:`, and the internal file form) — anything else
   renders as plain text. It runs in the HOST as a pure `string → string` function, which is what
   makes it testable with `node:test` against a file of hostile inputs. This is the extension's
   **first runtime dependency** (`package.json` has only devDependencies) — say so in the PR.

Either way: **no autolinking of bare URLs** (the family has shipped that defect once), no
`<img>`, no `javascript:`/`data:`/`vbscript:` ever reaching an attribute.

## The shape

- **Rendering.** `renderAnswer(markdown): string` — a new pure module; `chatMessagesHtml` calls it
  for `model` messages and keeps `escapeHtml` + `pre-wrap` for `you` messages (what the person
  typed is not markdown until they say so).
- **Links.** `<a>` with the theme colour. `http(s)` → `postMessage({ type: 'command', command:
  'openExternal', url })` → `vscode.env.openExternal` (precedent `panelProvider.ts:856`); a file
  reference (`path#L12` or `path:12`) → `openFile` → `showTextDocument` at that line, resolved
  against the workspace folder; **what does not resolve stays plain text**, never a dead blue link.
  The webview loads nothing itself: `localResourceRoots: []` (`chatPanel.ts:99`) stays.
- **Sides and colours.** `.msg.you` right-aligned (caption too) in one fixed non-vendor colour;
  `.msg.model` left, its EDGE in the answering model's vendor colour — the current tab model's
  until [PLAN_who_said_it_and_what_it_cost.md](../todo/PLAN_who_said_it_and_what_it_cost.md) puts a model
  on every message, then per message. Both keep a max width so a long line does not span the tab.
- **The end of an answer.** A rule after every `model` message — `<hr class="end">` styled from the
  palette. The operator asked for a row of asterisks; a rendered rule is what a row of asterisks
  BECOMES under markdown (a thematic break), it does not wrap in a narrow tab, and the copy
  button gives the answer as a unit. **Decided 2026-09-09 by the operator: the rule, not the
  characters** — nothing is appended to the copied text either.
- **Copy.** A small control on each `model` message → `postMessage({ command: 'copy', index })` →
  host copies `ChatMessage.text` (the markdown SOURCE, not the rendered text).
- **Bright and large.** `body` on `var(--vscode-editor-foreground)` (not `--vscode-foreground`,
  the chrome's, `chatPage.ts:125`); the three opacity dims on message bodies gone
  (`chatPage.ts:128-132`); `line-height: 1.55`; a base size above the UI's 13 px at scale 0; a
  reading column (max-width in `ch`) for prose, with code blocks scrolling inside their own box.

## Build order

1. RED tests (below) — the hostile-input file first; it is the one that must fail loudly.
2. The renderer module (decision 2 unless the gate overturns it), with its allow-lists.
3. `chatMessagesHtml`: render model messages; sides, colours, the end rule; the copy control.
4. Links: the two bridge commands and the host handlers; the resolve-or-plain rule.
5. `chatStyle`: colour, size, leading, column.
6. GREEN; whole suite; a manual pass with a real long answer from each vendor, screenshots
   beside Gemini's, recorded in the promotion.

## Test plan

- `renderAnswer.test.ts` (new): headings, ordered/unordered/nested lists, code span, fenced block,
  `---`, links; and the HOSTILE file — `<script>`, `<img onerror>`, `javascript:` and `data:`
  hrefs, an unclosed fence, a `](` inside a code span, a bare URL — each asserted to come out as
  text or a dropped attribute, never markup. This file is the plan's safety, keep it growing.
- `chatPage.test.ts`: `.msg.you` right / `.msg.model` left classes; the end rule after model
  messages only; the copy control on model messages only; `you` text is NOT rendered as markdown.
- `chatWiring.test.ts`: `copy` copies the source text of the right message; `openExternal` is
  called for `https` and NOT for `javascript:`; `openFile` resolves inside the workspace and
  refuses `..`.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/an-answer-reads-like-a-document`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   goal, and the diff `main...feat/an-answer-reads-like-a-document` — three dots; the two-dot moving-base trap is recorded in
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

**Specific to this plan:** the help's chat article gains a paragraph (what is rendered, that links open in the editor or the browser, the copy control); `README.md` screenshot if one exists; `module_extension.md` gains the renderer module and the two bridge commands; the new dependency is named in `package.json` and in the CHANGELOG; no new setting.

## Definition of Done

- [ ] A model answer renders as a document; a person's message stays plain.
- [ ] The hostile-input test file exists and every case is text, not markup.
- [ ] Links are the theme's blue; `http(s)` opens outside, a resolvable file opens at its line, everything else is plain text.
- [ ] `You` right in its colour, the model left with its vendor's edge; an end rule after each answer (decided 2026-09-09 in place of the asterisks).
- [ ] Copy yields the markdown source.
- [ ] Editor foreground, no opacity dims on bodies, leading and a reading column at scale 0.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

**Owns `chatMessagesHtml` and `chatStyle` in `chatPage.ts`, plus the new renderer module.** Queues
behind [PLAN_the_composer_stays_put.md](../research/PLAN_the_composer_stays_put.md) in the chat-page lane (same
style block). The renderer module and its hostile-input tests can be written BEFORE the lane
reaches this plan — that half has no conflicts at all.
