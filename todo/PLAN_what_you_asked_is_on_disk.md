# PLAN — two ways to ask, and a button that remembers what you asked

> Status: **plan only, nothing implemented yet.** Scope: the chat tab's header and the Claude Code
> session reader — `src_vs_code/src/chatPage.ts`, `chatCommand.ts`, `chatPanel.ts`, `chatMessages.ts`,
> `claudeQuestion.ts`, `claudeSessions.ts`, and the five help catalogs.
>
> Related docs: [PLAN_the_chat_opens_anywhere.md](../research/PLAN_the_chat_opens_anywhere.md), which
> built the session reader this extends.

## Part 1 — two menu items that say what they do

### The symptom

The right-click menu offers one item, **Chat with other AI**, and what it does depends on
`coai.chatAutoSend` — a setting in another window. Asked for as two items instead:

> *"и для клода и других файлов нужно добавить по 2 пункта. 1. chat with other io default (всегда
> авто выбирает мейн и прожимает ентер) 2. chat with other io choose (только копирует в окно текст и
> ждет ентер от человека)"*

An item whose behaviour is unpredictable from its own label is one you have to remember a setting to
use. Two items, each naming its own behaviour, is what a menu is for.

### What it will do

- **Chat with other AI: default** — the model ticked as main, asked straight away.
- **Chat with other AI: choose** — the turn goes into the composer; the person changes the model or
  the prompt and presses Enter themselves.
- Neither reads `coai.chatAutoSend`. The chord and the palette keep the setting-dependent command,
  where the name promises nothing about what happens next.
- Both items appear in both menus: the Claude Code panel (`webview/context`) and an ordinary editor
  (`editor/context`).

## Part 2 — two sessions in one folder are no longer a refusal

### The symptom

> *"а если в папке с проектом у меня 2 сесии и обе задали вопрос будет работать? по айди окна разве
> не можем определить однозначно?"*

By window id, no — VS Code exposes none. Taking the question off disk therefore refused whenever two
sessions in the same folder were both waiting, because picking one would silently deliver somebody
else's question.

### What it will do

Claude Code writes `{"type":"ai-title","aiTitle":"…","sessionId":"…"}` into the session file as it
names the conversation, and **that title is exactly what VS Code shows on the tab**. Measured against
a live session before this was built: a tab reading *Подключение к scoreMeter DB* has a row saying
precisely that.

So the tab's own label joins it to its session. Everything that still cannot be told apart still
refuses: a title naming neither session, a title naming both, and no tab at all.

## Part 3 — the button that shows what you asked

### The symptom

> *"когда окно долго работает (4 ч и более) начальный вопрос исчезает, и мне приходится спрашивать
> клод над чем ты работаешь"*

A chat tab open for hours has lost the thing it is about. The question that started the work has
scrolled far above, and the window can no longer say what it was for — so the operator asks the
assistant what it is working on, which costs a turn and gets a summary rather than their own words.

The words never went anywhere. Claude Code appends every one of them to its own session file.

### What it will do

A button at the top right of the chat tab, **on the same line as the ± steppers** — asked for exactly
there: *"вот тут в начале справа на одной строке с плюсиками должна быть кнопка показать вопрос"*.

- Pressed, it opens a region **above** the conversation and **outside** the scrolling one, so the
  text stays put: *"на этот текст не должна влиять прокрутка — неважно где я, в начале или в конце"*.
- It shows what the PERSON wrote, read off the session file this tab is joined to by Part 2's title
  match.
- `‹` and `›` step through every turn they typed, oldest first — their answered choice when asked
  whether one prompt was enough: **"первое плюс стрелки «следующее»"**.
- Pressed again it folds away, over half a second rather than at once: *"добавь легкую анимацию на
  0.5 сек, что б не было резкого рывка"*.
- A tab opened from a FILE has no session behind it, so it has no button at all — not a button that
  apologises.

### The seam

The page cannot read a file. It posts `{ type: 'showAsked' }`; the host reads the session and posts
back `{ type: 'asked', asked: [...] }`.

**Its own message type, not a thin `state` push.** The page's state handler treats a capped notice
and a failure line as *gone* when a state message does not mention them, so answering through that
channel would silently clear both.

**Read on the first press only.** A session file is megabytes long and belongs to somebody else;
opening a tab is not a reason to read one. A second press folds the region and asks for nothing.

### What counts as the person speaking

`origin.kind === 'human'` on a `user` row, minus the machinery: a `tool_result`, a sidechain (a
subagent's own conversation), a meta row, and a local command's own stdout.

**A turn an extension prefilled is still theirs.** CredsForDevs writes its preamble into the composer
and the person types their question at the end of it, so the two arrive as ONE message — checked with
the operator rather than guessed: *"то что ты посчитал впрыском был мой вопрос. все ок."*

A slash command is kept and unwrapped from its `<command-name>` envelope. The tag already carries its
slash — measured on this machine's own session files, where every one reads
`<command-name>/compact</command-name>`.

## Build order

1. `humanPrompts(lines)` in `claudeQuestion.ts` — pure, over lines, no `vscode`.
2. `promptsInSession(home, cwd, caseBlind, looking)` + `titleIn(lines)` in `claudeSessions.ts`.
3. The `showAsked` command kind in `chatMessages.ts`, the `onShowAsked` hook in `chatPanel.ts`.
4. The host hook in `chatCommand.ts`, and `fromSession` threaded from the door that opened the tab.
5. The page: the button, the pinned region, the arrows, the transition.
6. The help, in all five languages, in the same commit.

## Test plan

- `humanPrompts`: order, the prefilled turn, each kind of machinery rejected by name, the slash
  command unwrapped without doubling its slash, a half-written last line skipped.
- `promptsInSession`: the right session chosen by title; a title naming none reads nothing rather
  than the newest; an empty title never looks; a folder Claude has never run in is empty, not a throw.
- The page: the button is in the header, the region is outside `#scroll`, a file-opened chat has
  neither, and a session full of markup cannot open a second script element.
- **The shipped bundle, pressed.** The button, the region, the arrows and the answering message are
  four more names a minifier can rewrite, and the rounds log has been broken exactly that way twice.
  The DOM harness the Send test built is extracted rather than copied.

## Definition of Done

- [ ] Two menu items in both menus, neither reading the auto-send setting.
- [ ] Two waiting sessions in one folder are told apart by the tab's title; the genuinely ambiguous
      cases still refuse, by name.
- [ ] The button shows what the person wrote, pinned above the scroll, with arrows and a half-second
      fold.
- [ ] A chat opened from a file has no button.
- [ ] Help updated in English, Russian, Ukrainian, German and Spanish in the same commit.
- [ ] `npm run typecheck` clean, the whole suite green, and the bundled-page test watched red before
      it was watched green.
