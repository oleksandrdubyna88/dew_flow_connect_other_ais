# PLAN — the chat opens anywhere, the text has a tone, and a question can be taken

> Status: **plan only, nothing implemented yet.** Scope: the VS Code extension's chat trigger
> (`src_vs_code/src/chatCommand.ts`, `sessionKey.ts`, `chatTrigger.ts`, `package.json`), the webview
> steppers (`zoomControl.ts`, `uiScaleHost.ts`, `chatPage.ts`, `helpPage.ts`) and a new reader for
> Claude Code's own session files.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_chat_with_other_ais.md](../research/PLAN_chat_with_other_ais.md).

Three things the operator asked for in one sitting, in their words:

1. *"хочу чтоб можно было через Ctrl+Alt+A (и правой кнопкой чат) в обычных окнах тоже вызывать.
   например на md файлах, cs файлах и т.д."*
2. *"рядом с приближением добавь аналогичный контрол чтоб регулировать яркость белого шрифта
   (белее — желтее — серее)"*
3. *"и если можно — перехватывать целиком что спрашивает Клод. потому что сейчас приходится делать
   скриншоты. может правой кнопкой — взять вопросы"* — followed by the decisive observation:
   *"эти вопросы нельзя выделить просто"*.

---

## Part 1 — the chat opens from any file

### The symptom

`Ctrl+Alt+A` and the right-click item exist only inside Claude Code's own panel. On a `.md` or a
`.cs` file the keybinding is invisible and the menu item is absent, so a passage in an ordinary file
cannot be sent to a second model at all.

### Why it is shut, in three places

- `package.json` — the keybinding carries `when: activeWebviewPanelId == 'claudeVSCodePanel'`, and
  the only menu contribution is to `webview/context` with `when: webviewId == 'claudeVSCodePanel'`.
  There is no `editor/context` entry for this command.
- [sessionKey.ts:88](../src_vs_code/src/sessionKey.ts) — `sourceSession()` answers `undefined`
  unless the active tab's `viewType` is `mainThreadWebview-claudeVSCodePanel`. This is deliberate
  defence in depth, independent of the `when` clauses, and it is the real gate.
- [chatCommand.ts:97](../src_vs_code/src/chatCommand.ts) — `snapshots()` reads `viewType` off
  `tab.input`; a `TabInputText` has none, so an editor tab arrives as `viewType: ''`.

### What it will do

A capture from an ordinary editor opens a chat keyed to that editor's TAB and named after it — the
same identity rule the Claude path already keeps, so a second capture from the same file continues
the same conversation and a capture from another file opens its own.

**The passage comes from the editor directly.** The PowerShell keystroke and the clipboard borrow in
[selectionCapture.ts](../src_vs_code/src/selectionCapture.ts) exist for ONE reason, stated in that
file and measured: a foreign webview has no read API. An editor does. So the editor path reads
`editor.document.getText(editor.selection)` — instantly, with no clipboard to borrow and give back,
no 1.7-second spinner, and no synthetic keystroke that can land in the wrong window.

**With nothing selected, the passage is the WHOLE FILE.** Chosen by the operator over a refusal.

**Above a threshold it ASKS first, and that is not a second-guess of that choice.** A missed
selection in a minified bundle or a 50 MB log would otherwise become a paid turn nobody asked for,
and a notification shown after the send is too late to stop it — the gate raised this from two
vendors independently. So: under the threshold it goes, with the size said out loud; over it, a
modal names the file and its size and offers *Send all of it* or *Cancel*. A SELECTION is never
questioned, however large: that one was deliberate.

**What counts as an editor.** `vscode.window.activeTextEditor` — which an untitled buffer has (it
is a document like any other, and sending one is legitimate), and which a webview, a settings UI or
an image preview has not. No active text editor is a refusal naming that, never an empty question.
A diff view hands back whichever side has the caret, which is the side the person is reading.

### The seam

`ChatPath` gains a third value, `'editor'`. `sourceSession` gains a second accepted kind rather
than a second function: the pure decision is still "what should the caller do with the active tab",
and a caller handed two functions would have to decide which to ask.

**The manifest is part of the feature, and it is tested as one.** Every unit test here can pass
while `package.json` still gates the keybinding on `activeWebviewPanelId` and offers no
`editor/context` item — and then nothing at all happens on a `.md` file. So a test reads the
manifest: the keybinding has a clause that an ordinary editor satisfies, and the command is
contributed to `editor/context`. (codex, the plan round.)

## Part 2 — a stepper for the tone of the text

### The symptom

The white of the theme is the only white. On a long answer at night it is too bright, and there is
no way to warm or dim it without re-theming the whole editor.

### What it will do

A second stepper beside the zoom one, with the same shape and the same manners: `−` `+2` `+`,
one press one step, a global setting, pushed to every open page so two tabs never disagree.

**One axis.** `0` is the theme's own `--vscode-foreground`, unchanged — so somebody who never
touches the control sees exactly what they see today. Positive steps push the text AWAY from the
background and negative steps pull it towards one, passing through a warm cream on the way. On a
dark theme that is exactly the operator's "белее — желтее — серее"; on a LIGHT theme "whiter" would
mean invisible, so the positive direction there goes towards black instead. The webview body
carries the host's own `vscode-dark` / `vscode-light` / `vscode-high-contrast` class, which is how
the page knows which way is away. (gemini, the plan round, and it was right: a fixed ramp towards
white erases the text on Light+.)

**High contrast is left alone.** A person on a high-contrast theme chose their contrast; the
control still moves, but it starts from the theme's colour like everything else and the page never
pushes past pure white or pure black.

### The seam

`zoomControl.ts` is the template, not the host: a new pure module owns the tone's arithmetic and
markup, and `uiScaleHost.ts`'s pattern is followed for the setting. Both pages that render the zoom
control render this one; nothing else changes, because those are the only two pages that have a
header for it.

**The CSS must land inside the `body {}` rule.** A fragment above it silently drops the whole rule —
a trap already documented in `chatPage.ts` with a regression test that parses the rendered rule.

## Part 3 — taking the question Claude is asking

### The symptom

When Claude Code asks a question with options, the operator wants to hand that question to a second
model. Today that means a screenshot, because **the question widget is not selectable**: the
transcript above it highlights, the widget does not, so a select-all in the panel would copy
everything except the thing wanted.

### Where the text is

Claude Code writes every session as JSON-lines under
`~/.claude/projects/<the cwd with its separators replaced>/<uuid>.jsonl`. An assistant row holds
`message.content[]` with a block

```json
{ "type": "tool_use", "name": "AskUserQuestion",
  "input": { "questions": [ { "header": "…", "question": "…", "multiSelect": false,
                              "options": [ { "label": "…", "description": "…" } ] } ] } }
```

and a later row carries a `tool_result` for that block's `id` once it has been answered. Verified
against a real session file on this machine (5 183 lines, the last `AskUserQuestion` at line 5 161).

So the question, every option and every option's description are available AS TEXT — which is worth
more to a model than a screenshot of the same thing.

### What it will do

A new command, offered in the panel's right-click menu beside *Chat with other AI* and in the
command palette: it finds the session file for the open workspace, reads the last question in it,
renders it as plain text, and hands it to the chat as the passage — the same road a captured
paragraph takes, so everything downstream is unchanged.

**The PENDING question, not merely the last one.** A `tool_use` whose `id` has no `tool_result`
anywhere later in the file is a question still waiting for an answer, and that is the one the
operator wants: they are looking at it on screen. If the newest question has already been answered
the command says exactly that — *"the last question in this session was already answered"* — rather
than handing a second model a problem that is solved. Answered is offered as a fallback only when
the person asks for it explicitly, never by default. (local, three findings; and they are right
that a stale question sent as a live one is the worst outcome of this feature.)

**EVERY question in the block.** `input.questions` is an ARRAY — four at a time is ordinary — and a
rendering that takes `questions[0]` silently drops the rest. All of them, in order, each with its
header, its options and their descriptions, and a line saying when more than one answer may be
chosen. (codex, the plan round.)

**It says why when it cannot.** No session file for this folder, no question in it, a file whose
shape this build does not recognise: each is a sentence naming what was looked for and where,
never a silent no-op. The on-disk format is Anthropic's and undocumented; a build that stops
recognising it must say so rather than appear broken.

### Where the folder is, exactly

The directory name is the workspace path with every `\`, `/` and `:` replaced by `-`, case
preserved as the cwd gave it — `D:\rsd\ClaudeRag` becomes `D--rsd-ClaudeRag`. Since case is the
one part that is not ours to predict, the lookup matches case-INSENSITIVELY among the directories
that are there, and when nothing matches it falls back to reading a row's own `cwd` field, which
every row carries. A test pins both a Windows path and a POSIX one against fixtures. (codex.)

**Whose home.** The extension host's. In a WSL or Remote-SSH window the extension host and Claude
Code run on the same side, so the same home is the right one — but when they do not, nothing is
found, and the refusal NAMES the directory it looked in so the difference is visible rather than
mysterious. (codex.)

**Every filesystem failure is named.** Enumerating, stat-ing, opening, reading, decoding, and a file
that disappears between being chosen and being read: each is caught and each says which operation
failed. A command that throws into the void looks exactly like a command that does nothing.

### The honest limit

This reads the newest session file of the open folder. With two Claude Code tabs open on one folder
it can name the wrong one, and the plan does not pretend otherwise: the rendered text carries the
session's own id and its timestamp so a person can see which conversation they got.

**A file being appended to is the ordinary case**, and it needs no watcher: the reader takes a
snapshot, splits on newlines, and a half-written last line simply fails to parse and is skipped like
any other malformed row. The question it is looking for was written when the assistant's turn ended,
which is before the person could be looking at it.

---

## Build order

1. **Part 2 first** — it touches nothing the other two touch, and it is the one whose shape is
   already proven by the control beside it.
2. **Part 1** — the trigger, the pure gate, the editor read.
3. **Part 3** — the session reader, which is new ground and benefits from the trigger work being
   settled first.

Each part is a commit. All three are one PR, one gate round, one release.

## Test plan

Every part's decision is pure, and that is deliberate — a decision inside a module that imports
`vscode` is a decision no unit test can reach, which this codebase learned with a gate finding.

**Part 1**
- `sourceSession` accepts an ordinary editor tab and reports it as its own kind; a Claude tab still
  reports as it does today; an empty group is still nothing.
- A second capture from the same editor tab resolves to the SAME conversation; from another file, a
  different one.
- The passage decision: a selection is the passage; an empty selection is the whole document; an
  editor with no document at all is a refusal, not an empty question.
- `triggerPlan` routes an editor invocation to the editor path whichever door it came through.

**Part 2**
- The pure scale: 0 is the theme variable untouched; clamping at both ends; a step is one step.
- The rendered CSS lands INSIDE the `body {}` rule (parsed, as the zoom test does, not matched as a
  substring).
- The wire: a tone message decodes with the same delta discipline `zoomOf` keeps, and garbage
  decodes to zero rather than through.
- The control is on both pages, and the two steppers do not collide on one `data-` attribute.

**Part 1, the manifest**
- The keybinding carries a clause an ordinary editor satisfies, and the command is contributed to
  `editor/context`. Without this the whole part can be green and invisible.
- The whole-file threshold: under it goes, over it asks first, and a selection is never questioned.

**Part 2**
- The direction follows the theme: on a dark body a positive step moves towards white, on a light
  body towards black, and zero is the theme's own colour on both.
- A page opened AFTER a change starts at the changed value, and a page already open is pushed it —
  the fan-out the zoom control keeps, tested rather than assumed. (codex.)

**Part 3**
- The PENDING question wins: a `tool_use` with no `tool_result` for its id. An answered last question
  is reported as answered rather than handed over.
- EVERY question in the array is rendered, in order, with every option and description; a
  `multiSelect` question says that more than one may be chosen.
- Malformed lines are skipped rather than throwing — a half-written last line is the ordinary case
  for a file something else is appending to.
- The directory name: a Windows path and a POSIX one both resolve to the right fixture directory.
- The refusals: no folder, no file, no question, and a read that fails — each names what it looked
  for and which operation failed.

## Definition of Done

- [ ] The three parts are built, each with tests written RED first and watched fail for the real
      reason before the fix.
- [ ] `npm run typecheck` and the whole suite are green — every test, not only the new ones.
- [ ] `research/module_extension.md` records all three, and `todo/README.md` is updated.
- [ ] The help corpus mentions the new setting in all FIVE languages, in the same commit — a stale
      translation is invisible to the coverage test, which only catches a MISSING one.
- [ ] `helpCoverage.test.ts`'s `SETTING_ALIAS` carries the new setting, or the build is red.
- [ ] The coai gate: `review_plan` → resolve → `review_code` → resolve.
- [ ] One PR, checks green, CodeRabbit read and answered, merged, released and tagged.
