# PLAN — the phrase you keep retyping, one click from the panel

> Status: **IMPLEMENTED, 2026-09-14.** All three stories shipped and merged as #250 and released as
> extension 0.42.0: `phrases.ts` with `savedRows.ts`, the CRUD tab with its command, setting and
> five-language article, and the Phrases section with the copy action.
>
> Built as three stories, each through the gate twice — six rounds, 61 findings resolved, sessions
> `4cc5cc61`, `5f5b0468` and `6822a7bb`. Every rejection carries its reason in its session.
>
> **Deviations from this document, in the order they cost something.**
>
> *The plan was rewritten once before a line was built.* The first version chased inserting the text
> into the Claude Code composer and opened with a four-observation probe. The operator read the
> findings and chose the keystroke instead, which removed the probe, the PowerShell, the Windows-only
> gate and the clipboard borrow — and turned the largest half of the plan into the section headed
> *What this plan deliberately does NOT do*. That section is the most valuable part of this file.
>
> *Two shared modules were extracted that the plan did not foresee.* `savedRows.ts` took the row
> rules out of `chatPresets.ts` so both lists share one copy — and fixed two latent defects in the
> sibling on the way: a positional id could collide with a hand-written one, and a body opening with
> a blank line produced a button with no label. `settledWrites.ts` took the one-write-at-a-time and
> settle-before-storing rules out of `rolesPanel.ts` after six plan reviewers raised
> write-per-keystroke; the plan had cited the presets tab as its precedent, and the presets tab is
> the one that writes per keystroke. Moving both made their rules unit-testable for the first time.
>
> *The tab edits the ROWS, not the `Phrase`s.* Not in the plan. `phrasesFrom` names a nameless row
> from its first line, which is right on a button and wrong in an editor.
>
> *`phrases.test.ts` grew a sibling the plan did not name* — `phrasesEdit.test.ts` — because the
> host's rules belong outside the host, and `phrasesPanel.test.ts` as planned could not exist: no
> panel host in this repository is unit-tested, and `research/module_tests.md` records why.
>
> *The section is coloured.* The plan said it would ship uncoloured like *Team servers*; a guard test
> requires every collapsible header to carry a tone, so it wears `--tone-plan`.
>
> **What the gate caught that the tests could not**, and the reason both are worth remembering:
> a generated page script containing `/["\]/g` — an unterminated character class written by a
> heredoc that ate a backslash — which the source-matching tests passed over while the whole panel
> script was dead; and a settings row saved as `{ "text": "deploy it" }` that rendered but could not
> be edited or removed, because the view invented an id the rules did not match. Three reviewers
> found each, independently. The first is why `panelPhrasesScript.test.ts` EXECUTES the script.
>
> **Open tail, small and deliberate.** No end-to-end scenario drives the real extension host: there
> is no harness for one here, which `research/module_tests.md` already records as a standing gap.
> And `coai.phrases` follows a person between machines through Settings Sync, like the sibling preset
> lists; a reviewer asked for `"scope": "machine"` and it was declined, with the reason recorded.
>
> Related docs: [PLAN_presets_above_the_composer.md](PLAN_presets_above_the_composer.md)
> — the sibling list, its storage contract, the page/host arrangement and the deviation that decides
> where a new tab must be reachable from; [module_extension.md](module_extension.md).

## The goal, in the operator's words

*"мне приходится постоянно писать один и те же фразы"* — the same handful of sentences, typed into
the Claude Code composer many times a day (*"раз пофикшено — делай пр. принимай. деплой. проверяй
что работает"*). They want to pick one instead of typing it.

Settled with them on 2026-09-14, and these four are the feature:

1. **A new collapsible section in the panel on the left**, holding the phrases they configured —
   *"в левом меню должны быть просто в новом пункте списки"*.
2. **Clicking a phrase copies it to the clipboard. That is all it does.**
3. **They paste it themselves** — *"вставлять буду сам уже. не проблема"*.
4. **The list is edited in a tab of its own**, like *Edit chat presets*.

## What this plan deliberately does NOT do, and why — recorded so it is not re-opened

The first draft chased the thing that sounds better: put the text straight into the Claude Code
composer on one click. It was investigated to the bottom against the installed
`anthropic.claude-code` **2.1.270**, and the findings are worth keeping even though the feature no
longer needs them:

- **No supported API accepts arbitrary text.** `claude-vscode.insertAtMention` takes no arguments —
  it builds `@path#Lstart-end` out of the active editor's selection itself. The internal channel
  that *would* carry a sentence, `sendAtMention`, is a private webview message with no public door.
- **`claude-vscode.editor.open(session, prompt)` applies its prompt only when it creates a NEW
  panel.** An already-open session is revealed with *"Session is already open. Your prompt was not
  applied — enter it manually."* So pre-filling can never reach the conversation a person is in,
  which is the only one they want.
- **`claude-vscode.focus` ("Claude Code: Focus input") is real and callable**, and it is the focus
  primitive the refuted 2026-09-09 experiment lacked. It also inserts a stray `@mention` whenever a
  text editor holds a non-empty selection.
- Which left exactly one route: the phrase on the clipboard plus a **synthetic `Ctrl+V`** through
  the WinAPI `keybd_event` scaffolding `selectionCapture.ts` already ships. Windows and WSL only,
  about a second of PowerShell, global keyboard state, and **never measured in this direction**.

**The operator weighed one keystroke against that and chose the keystroke.** That is the whole
reason this plan is small: no OS automation, no PowerShell, no platform gate, no clipboard borrow,
no phase-0 probe, and the feature behaves identically on Windows, WSL, macOS and Linux, in a Remote
window as much as a local one. `Ctrl+V` is a price a person pays once per phrase; the alternative
was a mechanism that could fail silently on somebody else's machine.

It is also the house doctrine already: the MCP config block and the `CLAUDE.md` snippet are both
*offered* on the clipboard rather than written into another program.

## The shape

### `phrases.ts` — pure, and every rule lives here

Modelled on `chatPresets.ts` line for line, because a person hand-edits `settings.json` and one bad
row must never take the list with it:

- `Phrase { id, name, text }`. No *main* tick — nothing chooses a default phrase.
- `phrasesFrom(saved): readonly Phrase[]` — a `flatMap` returning `[]` for a row that is not a
  record and for one whose **`text`** is empty after trimming. **Nothing throws.**
- **A row with a text and no name keeps its text and is given one** — the first line, cut to
  `NAME_LIMIT` with an ellipsis, which is the `nameFor` rule already at `chatPresets.ts:167`.
  Dropping it would discard something a person typed into `settings.json` by hand, and quietly
  discarding a person's writing is the one thing a boundary reader must never do.
  *(Gate finding 1, accepted.)*
- Ids repaired positionally (`phrase-1`, `phrase-2`) and de-duplicated — the `withId` shape at
  `chatPresets.ts:114-119`.
- `NAME_LIMIT = 60` on the name (`chatPresets.ts:25`), **the text never truncated**: a name has a
  width to respect and a phrase has meaning to keep.
- `__proto__` / `constructor` / `prototype` excluded structurally, by an allow-list array checked
  with `.includes`, never `in`.
- `freshPhraseRow(taken)` produces a row **the reader accepts** — a row the reader drops is
  invisible litter in `settings.json`, the defect recorded at `chatPresets.ts:312-325`.

### The sidebar section

One more entry in the section list at `panelView.ts:299-319`, rendered by the `section()` helper at
`panelView.ts:625` — a `<details>` element, so the keyboard and screen-reader behaviour is correct
for free and the collapse state is carried in `PanelState.openSections` like every other section.

- A button per phrase, labelled with its **name**, `escapeHtml`'d; `title` carries the first line of
  the text so a long phrase is identifiable on hover.
- The button posts `{ type: 'command', command: 'copyPhrase', id }` — **an id, never a copy of the
  words**. That is the `chatPromptChoice` precedent: the list keeps the text, the surface keeps the
  choice, so a phrase exists in exactly one place.
- **An empty list is not an empty box.** It says what to do and links to the tab — a section that
  renders nothing reads as broken.
- *Edit phrases…* button, `data-command="editPhrases"`.
- No `.sec-phrases` colour rule to begin with; `teamServers` already ships uncoloured, so that is a
  precedent rather than an omission. Trivial to add if the operator wants one.

**The section is not optional and the reason is on the record.** `coai.editChatPresets` shipped
registered, in no menu and named in no view, and the deviation says what happened: *"the operator
read the sidebar as the feature and could not find any of it"*. `chatSection.test.ts:130-135` now
guards that for presets; this feature gets the same test.

**And it must refresh while it is open.** Adding a phrase in the tab has to make a button appear in
a panel that is already on screen — that is the `staticKey` path (`panelView.ts:2317`), and it gets
an integration test rather than a promise, because a missing `staticKey` entry freezes a section for
the life of the panel and nothing else would notice. *(Gate finding 11, accepted.)*

### The copy action

Handled directly in the provider's `run()` switch (`panelProvider.ts:1425`), beside the cases that
already do exactly this shape (`clipboard.writeText('wsl --shutdown')` at `:1615`):

```
case 'copyPhrase':  →  phrasesFrom(config.get('phrases'))
                       → the row with that id
                         ├─ no such row → a refusal naming it, and NOTHING written
                         └─ found       → await clipboard.writeText(row.text)
                                          ├─ rejected → a refusal, and no success sentence
                                          └─ resolved → a brief confirmation
```

**All three paths are accepted gate findings (0, 3, 4, 10), and they are the substance of this
section.** `writeText` returns a `Thenable` that genuinely rejects — a clipboard held by another
process, a remote session without one — and an unawaited call would leave an unhandled rejection, no
phrase on the clipboard, and a confirmation claiming it worked. So it is **awaited**, it is
**caught**, and **a failure never prints the success sentence**. An id naming no row is the other
half: a stale button on a panel whose list was edited in another window. Neither path crashes the
provider, and neither is silent.

*(The reviewer that asked for an output-channel log was right about the substance and wrong about the
instrument: this extension deliberately creates no output channel at all — a fact established when
`PLAN_the_menu_path_opens_nothing.md` cleared it of opening one. The refusal is shown, not logged.)*

**One live status-bar message, not a queue of them.** `setStatusBarMessage` returns a `Disposable`;
the previous one is disposed before the next is set, so five phrases copied in five seconds leave
one message rather than five fighting over the bar. *(Gate finding 5, accepted — as a single live
handle rather than the debounce that was proposed, because delaying the confirmation of the one
click that matters is the wrong trade.)*

**And the button says so itself.** The page script flips the pressed button's label to *Copied* for
about a second, client-side, with no repaint — the status bar is global and easy to miss when a
person's eyes are already on the composer they are about to paste into. *(Gate finding 8,
accepted.)*

`copyPhrase` needs **no VS Code command**: it is a panel command handled in the switch, so it goes in
`PANEL_COMMANDS` (`panelView.ts:2261`) and **not** in `VSCODE_COMMAND_FOR` (`:2307`). Only
`editPhrases` is a real command, the way `editChatPresets` is at `panelProvider.ts:1499`.

### The CRUD tab — the arrangement is fixed, not chosen

`phrasesPage.ts` (pure) + `phrasesPanel.ts` (thin host), the pair used three times already;
`chatPresetsPanel.ts` says in as many words: do not invent a fourth arrangement.

- Page: a `PhraseCommand` union (`edit | add | remove | zoom | ignore`), a `FIELDS` allow-list,
  `phraseEdit(message)` mapping a webview message to meaning, `editedRows` returning **the same
  array reference** when nothing would change so the host can skip a write that fires a
  configuration event saying nothing, `editRepaints` false for typing (a repaint on a keystroke
  moves the caret), `escapeHtml` on every person-typed value, CSP with a per-render nonce.
- **Edits commit on `input`; it is the REPAINT that is suppressed while typing, never the write.**
  That is what `chatPresetsPanel` already means by *saved as you type, re-rendered only when the
  shape changes* — so pressing *Add* or *Remove*, or closing the tab mid-word, cannot discard a
  half-typed phrase. *(Gate finding 2, accepted.)*
- **A rejected `config().update` is visible.** A read-only or externally locked settings file makes
  the write fail; the tab keeps what the person typed on screen and says the save failed, rather
  than repainting from a store that never took it and losing the edit silently.
  *(Gate finding 9, accepted.)*
- Host: one reused panel, `enableFindWidget: true` (`chatPresetsPanel.ts:217`; a test discovers
  every `createWebviewPanel(` call), `zoomControl.ts`, and writes through
  `config().update(key, value, Global)` as `chatPresetsPanel.ts:84` does.
- A large text box for the phrase — that is the point of the tab over `settings.json`.

### Storage — one setting, three decisions taken deliberately

```jsonc
"coai.phrases": { "type": "array", "default": [],
                  "items": { "type": "object",
                             "properties": { "id": …, "name": …, "text": … } },
                  "markdownDescription": "… a row needs a text; a row with no name is given one from
                                          its first line; a malformed row is dropped rather than
                                          taking the list with it." }
```

1. **Person-level, like the chat presets.** NOT in `OVERLAID_SETTINGS`
   (`settingsShape.ts:298`), NOT in `envBlock`, NOT mirrored to `coai-mcp` or a Team server —
   *"a setting that travels where it is not read is a setting that will one day be read by
   accident"* (`chatPresets.ts:20`).
2. **Therefore it does not go on `CoaiSettings`.** A test asserts every section `settingsFrom` reads
   is in `OVERLAID_SETTINGS`, so a field there would be *forced* per-side. It gets its own reader
   module and its own `PanelState` field, filled in `render()` straight from the configuration the
   way `state.chat` is.
3. **`staticKey` must see it** (`panelView.ts:2317`), or the section is frozen for the life of the
   panel — the bug that comment exists to prevent, and now the subject of its own test.

## Build order — two epics, four stories, each reviewed and committed before the next

| Epic | Story | What lands |
|---|---|---|
| **A — the list and its editor** | A1 | `phrases.ts`, pure, with its tests |
| | A2 | `phrasesPage.ts` + `phrasesPanel.ts` + `coai.editPhrases` + the manifest setting |
| **B — the panel** | B1 | The sidebar section, `copyPhrase`, `PANEL_COMMANDS`, `PanelState`, `render()`, `staticKey` |
| | B2 | Help in five languages, README, CHANGELOG, `module_extension.md`, the version bump |

The gate's own heuristic suggested 2–4 epics of 2–4 stories from a 214-line plan; that count was
measured against a plan whose largest half — the OS automation — the operator then removed. Four
stories is what is actually here, and the heuristic invites saying so.

## Test plan

| File | What it pins |
|---|---|
| `phrases.test.ts` (new) | a row with no text is dropped and the rest survive; **a row with a text and no name is KEPT and named from its first line**; ids repaired and unique; the name capped at 60 and **the text never truncated**; `__proto__` is not a field; `freshPhraseRow` produces a row the reader accepts; a hand-broken value yields `[]` rather than a throw |
| `phrasesPage.test.ts` (new) | one row per phrase; the editor is large; add and remove are present; a phrase containing `<script>` and `"` is escaped; typing does not repaint but add and remove do |
| `phrasesPanel.test.ts` (new) | an edit commits on `input`; **a rejected `config().update` leaves the typed text on screen and reports the failure** |
| `phrasesSection.test.ts` (new) | a button per phrase, labelled with the name; the button carries the **id**, not the text; an empty list renders the sentence and the link, not a blank box; there is a way into the tab (the `chatSection.test.ts:130-135` shape) |
| `phrasesCopy.test.ts` (new) | a known id writes that phrase's text to a fake clipboard; **a rejected clipboard write produces a refusal and NO success confirmation**; **an id naming no row writes nothing and refuses**; consecutive copies leave one live status message |
| `phrasesLiveRepaint.test.ts` (new) | **changing `coai.phrases` re-renders an already-open panel** — the `staticKey` path |
| existing wiring suites | `liveRepaint.test.ts` (every `data-command` declared), `install.test.ts` (every `VSCODE_COMMAND_FOR` id registered), `settingsAreDeclared.test.ts` (an undeclared setting is silently never persisted), `helpCoverage.test.ts` (the command and the setting described, in all five languages) |

Run: `cd src_vs_code && npm test` — the whole suite, not the one file.

## Acceptance — one PR, and the gate on both sides of it

Ships as its own branch (`feat/phrases-into-the-claude-box`) and its own pull request, accepted only
when the whole ritual has run — not when the code works.

1. **Gate before the first line of code.** Done 2026-09-14: session `4cc5cc61`, `good_enough`, twelve
   findings resolved, ten folded into this document.
2. **Tests first.** A RED test per behaviour, watched failing with the real symptom, then GREEN, then
   a check that the test really fails without the fix.
3. **`review_code` after EVERY story**, not once at the end: scope = that story's section of this
   plan plus the goal, `branch`/`baseRef` three dots by construction — the two-dot moving-base trap
   is [PLAN_the_gate_diffs_from_a_moving_base.md](PLAN_the_gate_diffs_from_a_moving_base.md).
   Resolve every finding, fix what was accepted, update the docs and the tests, commit. A story that
   is not reviewed, documented, tested and committed is not finished.
4. **Documentation.** `research/module_extension.md`; `research/architecture.md` only if a seam moved.
5. **Help.** An article in `helpContent.ts` plus `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts`,
   and a `SETTING_ALIAS` entry for the setting — **the same commit**, or `helpCoverage.test.ts` is red.
6. **README and CHANGELOG**, in the prose a person reads.
7. **Manifest.** One command, one configuration entry, and the version the release line expects
   (a feature: a minor bump).
8. **Family checks.** `plan-lifecycle.mjs` and `pin-check.mjs` clean.
9. **Promote.** On merge `/promote-plan` to `research/` with `IMPLEMENTED <date>` and every deviation
   recorded.

## Never

- **Never put the words in two places.** The section and the tab both work from `coai.phrases`; a
  button carries an id.
- **Never mirror phrases to `coai-mcp` or a Team server.**
- **Never let a refusal be silent** — a failed clipboard write, an id naming no row, a rejected
  settings update and an empty list each say what happened.
- **Never print the success sentence on a path that did not succeed.**
- **Never render a person's phrase unescaped**, in the section or in the tab.
- **Never reach for the synthetic keystroke** without re-opening this plan's second section first.

## Definition of Done

- [ ] A `Phrases` section in the panel lists the configured phrases as buttons, and pressing one puts that phrase on the clipboard.
- [ ] A failed clipboard write, and an id naming no row, each refuse in words and neither claims success.
- [ ] Consecutive copies leave one live status message, and the pressed button confirms itself locally.
- [ ] An empty list explains itself and offers the way into the tab.
- [ ] *Edit phrases…* opens a tab that adds, edits and deletes, with a large text box; typing is never lost to a structural action, and a rejected save is visible.
- [ ] Adding a phrase in the tab makes it appear in a panel that is already open.
- [ ] The list survives a hand-edited `settings.json`: a row with no text is dropped, a row with no name is kept and named, the rest work, nothing throws.
- [ ] A phrase containing HTML is rendered as text, in both surfaces.
- [ ] `npm test` green; help in five languages; README, CHANGELOG and `module_extension.md` updated; every story reviewed through the gate and committed.
