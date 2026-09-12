# PLAN — two ways to ask, and a button that remembers what you asked

> Status: **IMPLEMENTED, 2026-09-12.** All three parts shipped together. Scope: the chat tab's header
> and the Claude Code session reader — `src_vs_code/src/chatPage.ts`, `chatCommand.ts`, `chatPanel.ts`,
> `chatMessages.ts`, `chatTabs.ts`, `claudeQuestion.ts`, `claudeSessions.ts`, and the five help catalogs.
>
> Related docs: [PLAN_the_chat_opens_anywhere.md](PLAN_the_chat_opens_anywhere.md), which built the
> session reader this extends; [module_extension.md](module_extension.md) documents what shipped.

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

**Read on every OPEN, never on load.** A session file is megabytes long and belongs to somebody
else, so opening a tab is not a reason to read one — but the window this exists for is four hours old
and still being typed into, and a list read once would be missing everything said since. Folding it
away asks for nothing. Where the arrows had got to is kept when a re-read merely finds more.

**Every outcome is named.** No session file, no folder open, two sessions sharing this tab's name,
and a conversation nobody has spoken in yet look identical from an empty region, and that region is
the only place a person is looking. `promptsInSession` answers `said` / `none` / `several`, each
refusal carrying its own sentence.

**Two sessions with one title is a refusal, never a pick.** `waitingIn` has always refused namesakes;
this agrees with it. Handing over somebody else's conversation silently is the worst thing the title
join can do, and choosing between namesakes is exactly that.

**What crosses the bridge is bounded** — the earliest 200 turns, each cut at 8 000 characters and
saying where it was cut. A day-long session is hundreds of turns and some of them are whole files.

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
- `promptsInSession`: the right session chosen by title; two sharing a title refused by count; a
  title naming none, an empty title, a folder Claude has never run in and a session nobody has
  spoken in each answered with their own sentence; the payload bounded at both ends.
- The page: the button is in the header, the region is outside `#scroll`, a file-opened chat has
  neither, a session full of markup cannot open a second script element, and the fold is `.5s`.
- The bundle, pressed twice open: it asks again on the second open, keeps the arrow's place when the
  re-read merely found more, stops at both ends, and paints a refusal rather than an empty box.
- **The shipped bundle, pressed.** The button, the region, the arrows and the answering message are
  four more names a minifier can rewrite, and the rounds log has been broken exactly that way twice.
  The DOM harness the Send test built is extracted rather than copied.

## Definition of Done — every line of it met on 2026-09-12

- [x] Two menu items in both menus, neither reading the auto-send setting.
- [x] Two waiting sessions in one folder are told apart by the tab's title; the genuinely ambiguous
      cases still refuse, by name.
- [x] The button shows what the person wrote, pinned above the scroll, with arrows and a half-second
      fold.
- [x] A chat opened from a file has no button.
- [x] Help updated in English, Russian, Ukrainian, German and Spanish in the same commit.
- [x] `npm run typecheck` clean, the whole suite green, and the bundled-page test watched red before
      it was watched green.

## What shipped differently

Everything above shipped. Five things are not in the plan, and every one of them came from the gate.

**The whole read is streamed, and stops.** The plan said nothing about how the file would be read,
and the first implementation read every candidate session file into a string and split it into an
array of lines — to compare a title. Five reviewers across two vendors measured the same shape at
10× and called it seconds of a blocked extension host. `promptsInSession` now makes two passes with
`node:readline`: titles only over every candidate, then prompts over the one that matched, stopping
at `MOST_PROMPTS`. `humanPrompts(lines)` was replaced by `humanSaid(line)` for it; the array wrapper
was deleted rather than kept, because a function its own tests keep alive is a second implementation
waiting to drift.

**Two workspace ROOTS holding same-titled sessions was the same bug one level up.** `onShowAsked`
returned on the first folder that answered, so a tab was shown whichever root VS Code listed first.
`oneAnswerFrom` is now a pure decision over every folder's answer: exactly one is an answer, two are
a refusal that says so, none gives the first reason.

**The seam carries a sequence number.** Opening, folding and opening again starts a second read while
the first is still running, and the slower one landing last would replace what was just asked for.
The host counts presses per conversation; the page ignores anything older than the newest it has seen.

**A record that does not say which door it came through is NOT a session — the plan said the
opposite, and it was wrong.** The plan argued that an absent `fromSession` should read as `true` so a
long-running session tab keeps its button across the upgrade, and the plan round's finding on it was
rejected with that reasoning. The code round came back with the case that settles it: a file chat
called `README.md`, restored in a folder holding a session Claude happened to name `README.md`, would
show that session's words inside the file's tab. Handing over another conversation silently is the one
outcome this entire join exists to prevent, and it outranks a button missing from stored tabs until
they are opened again. Absent now reads as `false`.

**Every text block of a turn is joined, and the command envelope is anchored.** A prefilled preamble
and the person's own question can be two blocks of one message, and returning at the first cut their
words in half. `<command-name>` is unwrapped only at the start of a message, since the same tags
mid-sentence are somebody quoting them.

**A tab pins its session FILE.** Not in the plan at all, and the second code round was right to ask
for it: the plan joined a tab to its session by title and then never revisited the question, but a
title MOVES — Claude Code refines it and the tab follows — so a name captured at open stops matching
by the afternoon. The file does not move. `pinSession` resolves it in the background as the tab
opens and keeps it; only ever when exactly one session across every root matches.

## The open tail

- The button finds nothing until Claude Code has NAMED the conversation — before the first `ai-title`
  row there is nothing to join to. It says so rather than going quiet. There is no better join
  available from an extension: VS Code exposes no window id, and this product does not launch Claude
  Code, so it has no channel to that webview. Revisit if Anthropic ever puts the session id on the tab.
- A RELOAD loses the pin, and the first press after one resolves by a name that may by then have
  moved on. Putting the path in `SavedTab` would mean a home-directory path in workspace state, which
  was judged the worse trade; a session id would be better if one were ever available on the tab.
- The earliest 200 turns are what crosses. A session with more than that cannot be paged further from
  the page today; nobody has asked to.
