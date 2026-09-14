# PLAN — go to the conversation, switch between them, and start a new one

> Status: **IMPLEMENTED, 2026-09-13.** All four epics and all thirteen stories: the conversation
> store on disk (A), **CoAI: switch conversations…** (B), **CoAI: go to conversation** (C) and
> **New chat** (D). A merged as pull request #223, B as #229, C and D in #231.
>
> **What shipped differently from this plan, and why.**
>
> - **The version is 0.40.0, not the 0.39.0 story D2 names.** That number was written here before
>   0.39.0 shipped, which it did on the morning of the last day of this work.
> - **`start` does not open a conversation.** The brief written for story C3 had quietly turned the
>   plan's *New conversation for &lt;tab&gt;* row into opening one outright; two vendors refused it in
>   the plan round and were right — the chord is easy to press by accident, and opening one resolves
>   a CLI and launches a vendor process for a conversation nobody has typed into. The plan's own
>   wording is what shipped. **And the offered row is now completable**: it hands over to the ordinary
>   chat door on the tab it names, which the first implementation left inert.
> - **`Thread.passage` stopped being `readonly`**, which was true of every gesture that existed
>   before D1: a reset is the one act that makes a tab a different conversation.
> - **Two facts guard a reset, not one.** The plan said a `generation` counter checked in `oneTurn`.
>   It is bumped BEFORE the stop rather than after — so a question queued behind the running answer
>   never begins — and what a turn writes at its END is guarded on the `saveId` instead, because
>   between a reset beginning and the slate being wiped the turn in flight is still writing into the
>   OLD conversation, where its stopped line belongs. One counter would have had to pick which of the
>   two to get wrong.
> - **The page is told in a message of its own, and the sentence is not in it.** The plan put both in
>   a new `fresh` message. The state push does not mention the passage, so only a message of its own
>   can clear it — and it DOES mention the line sentences are written in, so a sentence written there
>   was wiped by the very next push a tick later.
> - **No timeout on the reset's wait**, asked for seven times across three rounds and declined each
>   time with the same evidence: the turn is bounded at 180 seconds by `DEFAULT_BUDGETS.turnMs`,
>   `stop()` settles it at once, and neither the turn chain nor the write chain can reject.
> - **`chatCommand.ts` was not split.** The plan declared the breach up front (2 889 lines against
>   800) and deferred the extraction; the gate asked twice more and it was declined twice more, with
>   the reason recorded here. It is ~3 500 lines now. **This is the plan's open tail** — see below.
> - **One quality-gate decision came with it**: the seventeen modules that import `vscode` are
>   excluded from coverage BY NAME, because no test here can execute a line of them, and the list is
>   asserted to match that set exactly.
>
> - **The last Definition-of-Done item is no longer manual.** It said the isolation guarantee was to
>   be checked by hand with a planted number on all three CLI adapters. `scripts/live-fresh.mjs` does
>   it — plant, recall (the CONTROL), dispose the session and its directory, open a new one carrying
>   nothing, recall again — and it was run: **claude, codex and agy each remembered 7431 before the
>   reset and answered NONE after it**, `per-turn` and `persistent` shapes both. Writing it found that
>   `scripts/live-chat.mjs`, the only other real-vendor check this repository has, had been broken
>   since 2026-09-11 and failing at process start for every vendor.
>
> **The open tail, for a plan of its own.** An extension-host harness — `@vscode/test-electron`, which
> `research/module_tests.md` records as this repository's largest single gap and which every one of
> these thirteen stories worked around with source-read wiring tests. Behind it: the `chatCommand.ts`
> extraction, and the coverage exclusion that comes back out the day the harness exists.
>
> Related docs: [module_extension.md](module_extension.md), [module_tests.md](module_tests.md).
>
> Kind: **feature** — four epics, thirteen stories, one branch per epic and a gate round per story
> (the line above said three stories and one pull request; the split into epics came out of the
> plan's own gate round, and epic A alone took eleven rounds). Scope: the chat's
> identity and persistence (`src_vs_code/src/chatCommand.ts`, `chatPanels.ts`, `sessionKey.ts`,
> `chatTabs.ts`, `extension.ts`), two new commands with a picker (new modules `chatStore.ts`,
> `chatStoreFile.ts`, `atomicFile.ts`, `chatGoto.ts`, `conversationPicker.ts`), the chat page's header
> (`chatPage.ts`, `chatMessages.ts`, `chatPanel.ts`), the manifest, the help in five languages, README,
> CHANGELOG, `research/module_extension.md`, `research/module_tests.md`, `research/architecture.md`.
> Origin: the operator's request of 2026-09-12, dictated and then decided point by point in one
> conversation; the decisions are quoted below in their own words.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_a_conversation_survives_a_reload.md](PLAN_a_conversation_survives_a_reload.md),
> [PLAN_chat_with_other_ais.md](PLAN_chat_with_other_ais.md),
> [PLAN_what_you_asked_is_on_disk.md](PLAN_what_you_asked_is_on_disk.md),
> [PLAN_carry_nothing_above.md](PLAN_carry_nothing_above.md),
> [PLAN_what_the_chat_has_cost.md](PLAN_what_the_chat_has_cost.md),
> [PLAN_three_chat_adapters.md](PLAN_three_chat_adapters.md).

## The symptom

The operator, 2026-09-12:

> *«у меня может быть открыто 10 вкладок клода, другие файлы и тд. и тяжело найти где был разговор.
> оно просто должна активировать (навести фокус на табу в вс код), где был разговор по этой табе клод
> кода (или другого файла). если разговора еще не было — то открыть новый. и нужно в выпадайке
> показывать список табов разговоров закрытых, чтобы я мог пересмотреть, открыть, перечитать если
> нужно. плюс допустим я работал с клодом или документом, используя коаи чат, а потом закрыл. и
> скажем через 2 дня продолжил работать — при вызове коаи чата или go to — должна подхватиться старая
> страница, где уже был разговор, а не создавать всегда новую. и добавить кнопку вверху там где
> увеличение — переоткрыть новый чат. тогда оно должно удалить старую историю и пустить все с нуля
> (чтоб даже локальные модели не держали контекст, а все стартовало с нуля).»*

What the code does today, read rather than remembered:

- **A conversation is keyed by the live `vscode.Tab` object** (`chatPanels.ts:71`, `sessionKey.ts:131`).
  That is exactly right for "focus the chat this tab already has" — `ChatPanels.open` reveals an
  existing entry (`chatPanels.ts:126-131`) — and it dies with the window. Nothing durable names the
  SOURCE a conversation was opened from: the master plan says so in one sentence, *"a tab object that
  is gone … is not the same session, and its conversation is not resurrected"*
  (`research/PLAN_chat_with_other_ais.md`, §*Which session a panel belongs to*). This plan reverses
  that sentence, deliberately, and says how.
- **Closed conversations are already kept and never read.** `chatTabs.ts` writes every conversation
  to `workspaceState['coai.chatTabs']` (`chatTabs.ts:57`), a close is not told from a reload so the
  record survives a close (`extension.ts:144-148`), and the store is cut at seven days and twenty
  records (`chatTabs.ts:69,77`). `ChatTabMemory.held()` and `forget()` have no production caller. A
  person with ten tabs a day is past twenty in two days, and the whole store is one JSON value
  rewritten on every change — the operator named the ceiling: *«workspaceState пишется в SQLite …
  VS Code начнет лагать … однажды молча дропнет или обрежет стейт»*.
- **Reopening a saved conversation exists** — `restoreConversation` (`chatCommand.ts:2079-2164`)
  renders the transcript as a CLOSED conversation, starts no process, and hands the whole transcript
  to the first new question through `carry` (`:2153`). It is reachable only from the reload
  serializer (`extension.ts:246-259`), and a restored conversation is registered under a fresh `{}`
  key (`:2163`), bound to no tab.
- **"Start a new conversation" is wired end to end and does nothing.** The page renders the button
  inside the Team-server cap notice (`chatPage.ts:528-537`), posts `restart` (`chatPage.ts:1340`), the
  parser has the arm (`chatMessages.ts:104,351`), the dispatcher the case (`chatPanel.ts:355`), the
  hook the signature (`chatPanel.ts:93`) — and the host body is `onRestart: () => undefined`
  (`chatCommand.ts:1867`).
- **A Claude Code tab already resolves its own durable identity and throws it away.** `pinSession`
  (`chatCommand.ts:350-368`) joins the tab's title to the session file under
  `~/.claude/projects/<folder>/<uuid>.jsonl` (`claudeSessions.ts:329`), refusing when two sessions
  share a name, and keeps the FILE on the thread (`Thread.sessionFile`, `chatCommand.ts:210`). The
  reload record does not carry it, on the stated ground that a home-directory PATH does not belong in
  workspace state (`chatCommand.ts:2140-2142`). The session's UUID carries no such objection.
- **"Local models hold context" means the CLI process.** `claude` and `agy` keep the conversation
  inside a long-lived child; `codex` resumes a thread by id, and that id survives a plain `dispose`
  (`cliChatSession.ts`, `killChild` leaves `sessionId` in place so stop-and-continue works); a Team
  server keeps nothing (`research/PLAN_three_chat_adapters.md`, §the measured table). There is no
  Ollama on the chat path at all. So "the model forgets everything" is: a new session OBJECT, not a
  killed process.

## The decisions, as the operator made them

Seven questions were put; every answer below is theirs, and the design does not reopen them.

1. **Two commands, not one "smart" one.** *«Контекстно-зависимый комбайн … это ловушка. Пользователь
   нажимает хоткей, ожидая найти старый диалог, а ему молча создается новый пустой чат.»*
   **CoAI: go to conversation** finds or restores the conversation of the active tab.
   **CoAI: switch conversations…** is the global picker. Nothing is ever created silently: when the
   active tab has no conversation, live or saved, the first command opens the picker with a
   preselected first row *New conversation for <tab>* — one Enter creates it, and it says so.
2. **The picker lists open AND closed conversations**, as one registry. An *Open* section (live tabs,
   with the model and the turn count; choosing one reveals it) and a *Recent* section sorted by the
   last update, not by creation. A trash button on every closed row, and `Alt+Delete` on the active
   row. Search matches the title, the model and the last line of the conversation.
3. **File storage, not the memento, and not the server's database.** *«Однозначно File Storage …
   с TTL 60–90 дней»*, and: *«обеспечь атомарную запись (.tmp + rename) и держи метаданные в кэше,
   чтобы диск не хрустел на поиске».* Transcripts are one JSON file per conversation under the coai
   data directory, written beside and renamed over; a small metadata file per conversation is what
   the picker reads, cached in memory. Ninety days. Bound to the current workspace by default, with a
   toggle for every workspace. The server's `coai.db` was considered and refused: the extension has
   no SQLite driver and reads that database only by spawning `coai-mcp --log`; a write path would be
   a native module in the extension or a one-shot server mode spawned on every turn, and both fail
   the moment the server is not installed.
4. **Ambiguity is always a choice, never a guess.** *«При неоднозначности — ВСЕГДА показывать выбор
   (QuickPick).»* Two saved conversations that could belong to the active tab open the picker
   narrowed to those, each row carrying the date, the workspace folder, and the message count. The
   house rule in `claudeSessions.ts` (two sessions with one title is a refusal) is kept and extended
   to the store.
5. **New chat is a clean slate that ARCHIVES.** The quoted passage is cleared — *«Застрявшая цитата
   из прошлого диалога … создает фантомный контекст»*. The old conversation is not deleted; it moves
   to the *Recent* section under its own id, and real deletion is only the explicit trash button.
   **No confirmation dialog** — *«Модальные окна на каждое базовое действие превращают софт в
   пытку»* — because the old conversation is two clicks away.
6. **Hotkeys, with strict `when` clauses.** `Ctrl+Alt+G` (`Cmd+Alt+G` on macOS) for *go to*,
   `Ctrl+Shift+Alt+G` (`Cmd+Shift+Alt+G`) for *switch*. `Ctrl+Alt+E` was proposed and withdrawn:
   `Ctrl+Alt` is `AltGr` on Windows and `AltGr+E` types `€` on the layouts in use here. Both commands
   are also in the two right-click menus and in the palette.
7. **From a tab that is neither a Claude panel nor a file** — a terminal, the Output pane, the chat
   tab itself — both commands fall back to the picker. *«ошибка или "тишина" — признак сырого софта.»*

## What the plan gate changed, 2026-09-12

One round, **all three reviewers answered** (`codex`, `gemini`, `local`), 18 findings, 14 gating
against a threshold of 6, verdict `good_enough` — the plan stage's round budget here is one, so
*good enough* IS the pass and the instruction with it is to apply what is true and reject the rest
with reasons. **16 accepted, 2 rejected.**

What the round bought, and it earned its keep: three vendors found the two-file commit hole
independently (a record renamed, its metadata not), which the first draft did not have; the
cross-workspace binding of a file URI, which would have attached another project's conversation to
this tab in silence; the lost update between two windows holding one conversation; a store that
cannot be read being reported as a store with nothing in it; a *New chat* that switched records
before the old turn was provably finished; and an `untitled:` document losing its conversation the
moment it was saved. Every one of them is now a paragraph above and a test below.

**Rejected, with the reasons recorded in the gate:**

- *"`CARRY_EVERYTHING` may leave a phantom context"* — wrong about the code. It is `0`, and it is an
  INDEX into the transcript (`chatCarry.ts:19`), not a flag meaning "copy everything from the
  previous session"; what is sent is `thread.carry`, and an ordinary turn sends the question alone
  when that array is empty. The real hazard in the same area — a turn in flight resolving into the
  new conversation — was raised by another reviewer and is accepted.
- *"add a confirmation dialog to New chat"* — the operator refused one by name and gave the reason.
  The risk underneath it is real and is taken without a modal: the page says the conversation was
  archived and where to find it.

## What it will do

### The identity: a durable SOURCE beside the live key

A conversation gets a second identity that outlives the window:

```ts
export type ConversationSource =
  | { readonly kind: 'claude'; readonly sessionId: string }   // the UUID of the session file
  | { readonly kind: 'file'; readonly uri: string }           // the document, `Uri.toString()`
  | { readonly kind: 'none' };                                // a restored record with no door, or a migrated one
```

- **A Claude tab's source is the session UUID**, the basename of the file `pinSession` already
  resolves. The id is written to the store the moment the pin lands, so a tab whose title Claude has
  since refined still names the same conversation — *a FILE does not move*
  (`claudeSessions.ts:319-327`). What is stored is the UUID, never the path: the objection at
  `chatCommand.ts:2140-2142` was to a home-directory path in workspace state, and a UUID names no
  directory.
- **A file tab's source is its URI.** `snapshots()` (`chatCommand.ts:414-432`) gains the tab input's
  `uri` beside the `scheme` it already reads; `sessionKey.ts` is not changed — the live key stays the
  tab object and the label re-key rules stay as they are.
- **Matching a tab to a saved conversation goes through the source and nothing else.** A record whose
  source is `none` — everything written before this plan — is never matched to a tab; it is found in
  the picker, by a person. A title is never a key, for the reason `chatPanels.ts:8-16` records.
- **And through the WORKSPACE as well.** *Go to* considers only records whose `workspace` is this
  window's. Two reviewers found the same hole independently: a file opened from another project —
  `project-A/src/main.ts` while the window is `project-B` — would otherwise silently attach
  `project-A`'s conversation to this tab, and the same URI or the same Claude session recorded in two
  workspaces would produce an ambiguity picker for something that is not ambiguous. Another
  workspace's conversation is reachable, deliberately, through the picker's *all workspaces* toggle,
  where a person can see which project a row belongs to.
- **An `untitled:` document changes its URI when it is saved.** A conversation opened from an unsaved
  buffer would then be unreachable by *go to* for the file it became. While a live conversation holds
  an `untitled:` source the host listens on `workspace.onDidSaveTextDocument` and rewrites the source
  to the new `file:` URI; `workspace.onDidRenameFiles` does the same for a file that is moved. A
  record whose source no longer names anything is not repaired — it stays in the picker, found by a
  person.
- **Two Claude sessions, one title** (`sessionFileIn` answers `several`): *go to* opens the picker
  narrowed to the saved conversations whose session ids are among the candidates, plus the *New*
  row. `pinnable` (`claudeSessions.ts:269`) already decides what is unambiguous; it is reused
  unchanged.

### The store on disk

`<coaiDataDir>/chat-conversations/` (`dataDir.ts:16` — beside `chat-usage.jsonl` and
`chat-doors.jsonl`, the two ledgers the local-DB reader plan already knows about), two files per
conversation:

| File | Holds | Read by |
|---|---|---|
| `<id>.json` | the whole record: `version`, `rev`, `id`, `title`, `passage`, `modelId`, `messages`, `fromSession`, `carryFrom`, `source`, `workspace`, `createdAt`, `updatedAt`, `closedAt?` — the `SavedTab` of `chatTabs.ts:16-54` plus the new fields | the serializer, *go to*, a picker choice |
| `<id>.meta.json` | `id`, `rev`, `title`, `modelId`, `turns`, `lastLine` (≤ 120 characters), `source`, `workspace`, `updatedAt`, `closedAt?` — about 300 bytes | the picker, through the cache |

- **Every write is atomic.** `writeAtomically` in `chatOrphans.ts:97-101` — write beside, rename
  over, *a rename within one directory is atomic on both filesystems this ships to* — is extracted
  into `atomicFile.ts` (async, `fs/promises`) and called from both places. The reuse rule's move 2:
  a private helper needed by a second caller is extracted, not copied.
- **`rev` is what makes TWO atomic writes one commit.** Three reviewers, from three vendors, found
  the same hole: a rename of the record followed by a crash leaves the metadata describing the
  previous turn, and a picker row then shows a stale last line or points at something that cannot be
  opened. So every save increments `rev` and writes **the record first, the metadata second**, both
  carrying it. A metadata file whose `rev` is lower than its record's is stale by construction, and
  the reader regenerates it from the record rather than believing it; the activation sweep does the
  same pass over the whole directory. **Deletion goes the other way** — metadata first, then the
  record — so a crash mid-delete leaves a file nobody can see rather than a row nobody can open, and
  the sweep collects the remainder.
- **Two windows writing ONE conversation cannot lose each other's turns.** The per-file rename makes
  a file whole; it does nothing about two windows that both reopened the same record, which is
  reachable (two windows on one workspace, *go to* pressed in each). So a write is a
  compare-and-swap: the writer holds the `rev` it last read, and a record on disk carrying anything
  else means somebody else has been in this conversation. The write is then **refused, not
  overwritten** — this window re-mints its conversation under a NEW id, keeps the transcript it
  holds, says so in the page (*this conversation was continued in another window; this tab is now a
  copy*), and writes there. Nothing is lost on either side.

  **What that rests on, corrected by A2's rounds.** The first draft of this paragraph said "there is
  no lock to leak" and named the rename as the atomic operation. A code round showed that reading the
  `rev` and then renaming is a check-then-act, not a swap: two windows both holding baseline 5 both
  observe 5, both choose 6, and the second silently replaces the first. So the transition to a
  revision is **claimed** — one `<id>.lock` per conversation, created exclusively (`wx`), which is
  the one operation a filesystem offers as an atomic test-and-set — and the disk is probed only under
  the claim. Every mutating operation takes it: a save, a delete, and the metadata regeneration a
  read performs. Release is fenced by a token, a claim older than thirty seconds is broken once so a
  killed writer cannot wedge a conversation for ever, and the residual window that leaves is stated
  in the module's own header rather than in this plan.

- **The vocabulary a save answers in**, because A3 and B3 are written against it: `ok` · `partial`
  (the record committed, its index did not — advance the baseline, the index self-heals on the next
  read) · `refused` (somebody else is in this conversation, with the revision on disk) ·
  `incompatible` (the record on disk is torn or of a version this build does not know; **nothing is
  written**, so an older build cannot replace a newer one after a downgrade) · `failed` (a disk that
  would not answer, with a sentence a person can read and no path in it — the path goes to the
  console). A conversation deleted in one window cannot be resurrected by another window saving the
  copy it still holds: an absent record under a non-zero baseline is a refusal, not a creation.
- **A store that cannot be READ is unavailable, not empty.** `ENOENT` on the directory is an ordinary
  state — nobody has chatted yet — and reads as an empty store. `EACCES`, a full disk, a directory
  where a file should be: those mean history EXISTS and could not be read, and answering "no
  conversations" would be this module lying about the world (the coding-style rule: absent is not
  zero). The store then reports `unavailable` with the reason; the picker says so instead of showing
  an empty *Recent*; *go to* still reveals live panels and names the failure rather than offering to
  create a second conversation for a tab that may already have one; and nothing is deleted or swept
  while the store cannot be read.
- **A damaged record is dropped, never repaired; a wrong `version` is discarded, never guessed at** —
  the rules `chatTabs.ts:59-66, 102-117` already state, applied to two files instead of one key.
- **The cache is built at activation, in the background, and refreshed against the DIRECTORY.** Not
  on the picker's hot path: at 30 conversations a day over 90 days the listing is thousands of files,
  and a picker that opens in a second is a picker nobody uses. A refresh re-reads metadata whose
  `mtime` is at or after the last load (at, not after — a network or FAT filesystem's timestamp
  resolution is coarse enough to hide a same-second write) **and reconciles the full filename set**,
  because a conversation another window trashed leaves no newer file to notice and would otherwise
  sit in this window's picker until it was chosen and failed. Choosing a row re-reads its record and
  says plainly if it has gone. The *Recent* section renders the newest 100; the filter runs over the
  whole cache, so typing finds the older ones.
- **Migration, once, and idempotent by id.** At activation the legacy `coai.chatTabs` value is read
  with `tabsFrom` (`chatTabs.ts:110`); each record is written to the directory **only when no
  `<id>.json` is there**, with `source: {kind:'none'}`, `rev: 1` and `workspace` = this window's
  first folder; the memento key is emptied only once every record is confirmed present. A crash
  halfway leaves the key populated, and the next activation re-runs and skips what it finds — the id
  is the record's own, so a re-run can neither duplicate nor overwrite. `tabsFrom`, `isTab` and
  `reloadedNote` stay in `chatTabs.ts` for that; `ChatTabMemory`, `KEEP_FOR_MS`, `KEEP_TABS` and the
  memento write path go.
- **The serializer reads the store** (`extension.ts:246-259`): the page's `setState({id})`
  (`chatPage.ts:904`) is unchanged, so a window reloaded across this upgrade finds its migrated
  record.
- **`show()` writes through the store** (`chatCommand.ts:534-550`), with its by-reference dedupe
  guard exactly as it is — the regex in `aConversationSurvivesAReload.test.ts:273-277` pins that text
  and is right to.
- **What `workspace` is.** The first workspace folder's `fsPath`, or the empty string for a window
  with none — the same answer `whereToLook()` gives (`chatCommand.ts:291-296`), so a conversation is
  filed where its Claude session is.

### CoAI: go to conversation — `coai.goToConversation`

`chatGoto.ts` is the decision, pure, over what the host hands it; `chatCommand.ts` gains only the
`vscode` half. The decision, in order:

1. `matchedSource(panels)` (`chatCommand.ts:1233-1242`) says `existing` → **reveal** that entry. This
   is the ten-tabs case and it costs one call.
2. The active tab is eligible (a Claude panel or a `file`/`untitled` document) and has no live
   conversation → resolve its source: for Claude, `findSession(label)` (`chatCommand.ts:311`) →
   `pinnable` → the UUID; for a file, the URI. Exactly one saved record **of this workspace** carries
   that source → **reopen it bound to this tab**: `restoreConversation` widened to take an optional
   `key` and `label` (and an optional panel, as today), so the reopened conversation is registered
   under the tab object and every later door lands in it.

   **Two ways this must not produce a second copy of one conversation, both raised by the gate.**
   The key may already be taken — step 1 has just said it is not, but a restore is asynchronous and
   the world can move — so the registration goes through `panels.open(key, label, factory)`
   (`chatPanels.ts:126-139`), which reveals what is there and never calls the factory; nothing is
   built for a tab that already holds a conversation. And the RECORD may already be open under
   another key, in this window, from a picker choice made a minute ago: the registry is asked by
   `saveId` first, and an open one is revealed where it is rather than opened again. One record is
   live in one panel, or in none.
3. Several candidates (an ambiguous Claude title, or — impossible by construction, but checked —
   two records with one source) → **the picker, narrowed** to those, *New* row first.
4. Nothing saved for this tab → **the picker with *New conversation for <label>* preselected**. Enter
   opens an empty conversation named after the tab through `newConversation`
   (`chatCommand.ts:1519`) with an empty passage and an empty composer; the presets rows are drawn,
   nothing is pressed for the person. No clipboard is borrowed and no `Ctrl+C` is synthesised —
   *go to* never captures.
5. The active tab is neither → **the full picker** (decision 7).

The door is recorded first, as `goto`, at the command handler (`extension.ts:194-237` shape,
`noteChatDoor` `chatCommand.ts:2355`).

### CoAI: switch conversations… — `coai.switchConversations`

`conversationPicker.ts` builds the items, pure; the `vscode` half is a `createQuickPick`, which is
a stated deviation from the house style (six `showQuickPick` sites, no `createQuickPick`): item
buttons, a title button and a stay-open delete need the instance.

- **Sections** through `QuickPickItemKind.Separator`: *Open* (from the registry and the threads — an
  `openConversations(panels)` accessor exported by `chatCommand.ts`, because `ChatPanels.known()`
  returns keys and labels only and the `saveId` lives on the private `Thread`), then *Recent*
  (the cache minus the open ids), newest `updatedAt` first.
- **A row**: label = title; description = `model · N turns · <ago> · <folder name>`; detail = the
  last line. `matchOnDescription` and `matchOnDetail` on, so typing a model name or a word from the
  last answer filters.
- **Choose**: an open row reveals; a closed row reopens under `{}` — or under the active tab when the
  picker was opened from *go to* and the row's source is that tab's.
- **Forget**: a `$(trash)` item button on every closed row, and `coai.forgetPickedConversation` bound
  to `Alt+Delete` with `when: inQuickOpen && coai.conversationsPickerOpen` — a context key set while
  the picker is open and cleared on hide. Both remove the two files and the cache entry and
  **rebuild `items` without hiding** (`keepScrollPosition = true`). Open rows carry no trash.
- **Scope**: a `$(globe)` title button toggles *this workspace* ↔ *all workspaces*; the title says
  which; rebuilding keeps the picker open. The default is this workspace.
- **Ambiguity from *go to*** is the same picker with `items` narrowed and a title saying why:
  *2 saved conversations could belong to "<label>" — which one?*

The door is recorded as `switch`. `Door` (`chatDoors.ts:30`) gains `goto` and `switch`; `asking()`
(`:45`) counts neither; `DOORS` (`:33`) lists both, in registration order. **The recording is
best-effort and says so**: `appendLine` never rejects and logs what it could not write
(`jsonlLedger.ts:37-52`), which is the policy `chatUsageFile.ts` already states — a ledger that
cannot be written costs the record and never the person's command. The gate was right that the first
draft's Definition of Done over-promised durability; it now claims what the code does.

### New chat — the button in the header, and the capped notice's button

- **Markup**: a `New chat` button in the `<header>` (`chatPage.ts:825-828`), after the two ± controls
  and BEFORE the *Asked* button, so `.asked`'s `margin-left: auto` (`chatPage.ts:603`) still pushes
  *Asked* to the right edge. Same `.asked` styling class. It posts the existing
  `{type:'command', command:'restart'}` — the capped notice's button (`chatPage.ts:1336-1342`) and
  this one are the same gesture with the same words, so one host implementation serves both.
- **Host** — `onRestart` (`chatCommand.ts:1867`) becomes `freshStart(entry)`, and its ORDER is the
  part the gate spent three findings on. Nothing is switched until the old conversation is provably
  finished:
  1. **The old turn is ended and WAITED FOR.** `thread.session.stop()`, then `await thread.turns` —
     the per-conversation chain that already serialises turns (`chatCommand.ts:243`) — so the answer
     in flight has landed in the OLD transcript and been written down before anything is cleared. A
     stop that merely stops waiting would let an answer arrive afterwards, and it would arrive in a
     conversation that had already been archived.
  2. **A generation guard, because waiting is not enough.** The thread gains a `generation` counter,
     bumped here; `oneTurn` reads it before it appends and drops a result whose generation has
     changed. A bridge message can land a tick late — the same reason `turn` exists for a stop
     (`chatCommand.ts:213-222`) — and a late answer must be discarded, never attached to the new
     `saveId`.
  3. **The session is disposed, and only then is its directory released.** `await` the disposal, then
     `home.release()`: a CLI writing on its way out into a temp directory that has already been
     removed throws where nobody is listening. The release is best-effort and logged.
  4. **If any of that fails, the reset does not happen.** The old conversation stays live, the page
     says the previous session could not be ended and names the reason, and nothing is archived. A
     half-performed reset that reports success is the one outcome worse than no reset.
  5. The old record is **closed, not deleted**: `closedAt` and a fresh `rev` are stamped on its two
     files. It appears in *Recent* under its old id.
  6. The thread is given a **new `saveId`**, and: `messages = []`, `carry = []`, `carryFrom = 0`,
     `asked = 0` (the Team-server cap opens again), `spend = []`, the attached picture dropped
     (`forgetPicture`, `chatCommand.ts:1943`), `passage = ''`,
     `savedMessages`/`savedModelId`/`savedCarryFrom` cleared so the first push writes the new record.
     `turn` is NOT reset — it exists so a late stop names the turn it means.
  7. The **session is not replaced**: the thread takes the dead `closed` stub of
     `restoreConversation` (`chatCommand.ts:2092-2096`, extracted as `closedSession(note)`) and
     `reopen = true`. The first question runs `reopened` (`chatCommand.ts:601-633`) exactly as after
     a reload — a NEW `CliChatSession` in a new temp directory — and `carry` is empty, so it is
     handed nothing. This is what makes `codex` forget: its thread id is instance state of the
     session object, and the object is gone. A tab nobody speaks to again spawns nothing, the reload
     plan's rule.
  8. The page is told: a new host→page message `{type:'fresh', id}` clears `#passage`, calls
     `vscode.setState({id})` with the new id so a reload maps the tab to the new record, and shows a
     **non-modal line** — *the previous conversation was archived; find it in CoAI: switch
     conversations…*. Then the ordinary state push draws the empty transcript. A new message type
     rather than a `state` field, for the reason `showAsked`/`asked` has one: the state handler
     treats what a state message does not mention as gone
     (`research/PLAN_what_you_asked_is_on_disk.md`, §the seam).
- **What the guarantee IS, stated no wider than it is verified.** The next question reaches a model
  that was never told the old conversation — a new session object, a new process, an empty carry.
  That is checked with a planted number on **all three** CLI adapters, not only `codex`. What it is
  NOT: a promise that the old CLI's process tree is gone on every platform. On Windows the launcher's
  `taskkill /T` ends the tree; on WSL and Linux a grandchild can outlive its parent, which is
  [PLAN_closing_a_chat_ends_its_whole_tree.md](../todo/PLAN_closing_a_chat_ends_its_whole_tree.md)'s
  territory — declined, with the orphan ledger collecting such a process at the next activation. An
  orphan holds its own context and nothing speaks to it; it is a process leak, not a memory leak
  between conversations, and this plan says which of the two it fixes.
- **The title stays**; the tab is still the conversation *of* that tab. The model stays. No dialog —
  the operator refused one by name, and the archived conversation is two clicks away in the picker;
  the line in step 8 is what a dialog would have said, without a keystroke to dismiss.

### The seam, in one place

| Direction | Message | New |
|---|---|---|
| page → host | `{type:'command', command:'restart'}` | no — reused by the header button |
| host → page | `{type:'fresh', id}` | yes — clear the passage, `setState` the new id |
| command → host | `coai.goToConversation`, `coai.switchConversations`, `coai.forgetPickedConversation` | yes |

## What already exists and is REUSED

| Need | Reused | Widened |
|---|---|---|
| reveal the live conversation of a tab | `ChatPanels.open` (`chatPanels.ts:126`) via `matchedSource` | — |
| reopen a saved conversation, closed, carrying on the first question | `restoreConversation` (`chatCommand.ts:2079`) | optional `key`/`label`; `panel` already optional in `createChatPanel` (`chatPanel.ts:193`) |
| the durable Claude identity | `findSession` → `pinnable` → `Thread.sessionFile` (`chatCommand.ts:311, 350`) | the UUID is written to the store when the pin lands |
| the eligibility of a tab | `sourceSession` with `isClaudeSessionTab` / `isOrdinaryEditorTab` (`sessionKey.ts:131, 86, 112`) | — |
| the record shape and its validation | `SavedTab`, `isTab`, `tabsFrom`, `reloadedNote` (`chatTabs.ts`) | four optional fields; the memento class retires |
| atomic file write | `writeAtomically` (`chatOrphans.ts:97-101`) | extracted to `atomicFile.ts`, both callers |
| where files live | `coaiDataDir()` (`dataDir.ts:16`) | — |
| a missing file vs an unreadable one | the `ENOENT` rule of `readLedger` (`jsonlLedger.ts:80-94`) | the same distinction in the store reader |
| the restart gesture end to end | `restart` in `chatMessages.ts:104,351`, `chatPanel.ts:355`, `chatPage.ts:1336-1342` | one host body |
| the dead session for a conversation with no process | the `closed` stub (`chatCommand.ts:2092-2096`) | extracted as `closedSession(note)` |
| counting invocations | `noteChatDoor` / `chatDoors.ts` | two `Door` values |
| the picker's row style | `{label, detail, payload}` literals (`panelProvider.ts:2096`, `escalationWatcher.ts:109`) | `createQuickPick`, for buttons |
| the help article | `chat-with-other-ai` (`helpContent.ts:225-246`) and its four translations | new paragraphs, five languages, one commit |

Reuse rejected, and why: writing transcripts into `coai.db` (no driver in the extension; the server
may be absent); a title-keyed lookup of saved conversations (the namesake defect the registry exists
to prevent); one shared `index.json` (a cross-process read-modify-write with no atomic operation
under it).

## What grows, and who retires it

| What | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| `<coaiDataDir>/chat-conversations/*.json` | a transcript is bounded in practice by the carry budget's order of magnitude — 20–60 KB; at 30 conversations a day × 90 days ≈ 2 700 files ≈ 80 MB worst case, ≈ 5 a day ≈ 13 MB typical. **The whole record is rewritten on every save**, which a code round questioned and which is deliberate: one file replaced atomically is what keeps a reader from ever seeing half a conversation, and the carry budget is what keeps the file small enough for that to be free. If a transcript ever stops being bounded, an append-and-compact log is the tail to take | the activation sweep: `updatedAt` older than **90 days** deletes both files; the trash button deletes one on request. The sweep does NOT run while the store reads `unavailable` — a directory that would not answer must not be interpreted as a directory full of expired things | a `.tmp` beside a record is a crash mid-write; the sweep removes any `.tmp` older than an hour, and a reader never sees it because it was never renamed |
| `<coaiDataDir>/chat-conversations/*.lock` | one per conversation being written, ~80 B, alive for the length of two file writes | its own writer, in a `finally`; a claim older than thirty seconds is broken by the next writer, and B1's sweep collects one whose owner died before either | a lock left by a killed writer is what the stale window exists for; the residual race it leaves is stated in `chatStoreLock.ts`'s header rather than here |
| `*.meta.json` | ≈ 300 B each, the same count | with its record; a metadata file whose record is gone is removed by the same sweep | a `rev` lower than its record's means the pair was interrupted: the reader regenerates it from the record rather than trusting it |
| the metadata cache in memory | one entry per meta file, ≈ 300 B; at 2 700 records ≈ 800 KB, bounded by the sweep | built in the background at activation, refreshed against the directory's filename set and by `mtime` | a failed refresh keeps the last good cache and logs; a record deleted by another window leaves the cache on the next reconcile |
| `workspaceState['coai.chatTabs']` | today ≤ 20 records — **and A4 removed that bound without adding one**: the activation-time prune went AND the week-and-twenty cut `remembered()` made on each write went with it (its code round: while the memento is still written, a legacy record whose store write failed could otherwise be dropped by an ordinary write before the next activation's migration read it). The memento is written only until the migration has SEALED it in the window, so its size is bounded by the number of conversations spoken in during that window. The STORE it feeds has no retention until story B1's sweep lands — until then `chat-conversations/` grows by one record per conversation and is retired by nothing but the trash button, which is B4 | emptied by the migration once every record is confirmed on disk AND indexed (per id, never by listing the store), the memento SEALED (its writer unbound, its queue drained) and re-read unchanged immediately before the clear, and the store asked once more that it is still there | a migration interrupted after some files and before the key is emptied re-runs; the two copies are compared by TRANSCRIPT, never by id — the store's copy a prefix of the memento's (the dual write was best-effort) is replaced by it as the disk record with the memento's words over it, an equal or longer store copy stands, diverged copies keep both (the memento's under `<id>-m`, found and compared before anything is written on a re-run); a `partial` save, a `busy`, a `refused`, an `incompatible` file or a `failed` disk leaves the key for the next activation and the writer resumes; a damaged record is quarantined under `chat-conversations/quarantine/` rather than dropped; a store that is `unavailable` — at the start or again just before the clear — migrates nothing further and the memento stays in charge, dual write and all |

### B1's sweep may not delete what somebody is still holding

Written into the plan after B1's gate round, where all three vendors converged. The first design had
the sweep run at activation, delete a record whose `updatedAt` was over ninety days old, and protect
open conversations by skipping the ids THIS window has open. Every part of that is wrong in a way
that loses a person's words.

- **The directory is shared by every window.** A window-scoped check protects nothing: window A
  leaves a conversation open and untouched, window B activates, sees no such id among its own tabs,
  and deletes it. So liveness is announced rather than assumed — each window keeps a small heartbeat
  file naming the conversations it holds open, refreshed while it lives, and the sweep skips every id
  a heartbeat younger than its window names. It is the shape the orphan ledger already uses for
  vendor processes, and a heartbeat nobody has refreshed is collected by the same sweep.
- **`updatedAt` cannot be the liveness signal**, which is why the heartbeat exists at all: A4 made a
  reload deliberately NOT a use, so a tab open for three months has an old timestamp and is a
  legitimate target by age alone.
- **Expiry is re-checked under the lock.** Observing a record as expired and then deleting it is the
  same check-then-act that cost this feature two rounds in epic A: a save can land in between, and
  the delete would then destroy it. The store gains one operation that takes the lock, re-reads, and
  deletes only if the record is still expired and unchanged.
- **An orphaned TRANSCRIPT is quarantined, never deleted.** It is the only rule here that would
  destroy the sole copy of somebody's words rather than a derived or expired thing — and a record is
  legitimately alone for the instant between A2's two writes, so an unbounded rule deletes
  conversations that are being created. It is bounded by a grace period AND set aside rather than
  removed.
- **One window sweeps, not six.** A marker in the directory says when the last sweep ran; a window
  that finds a recent one does nothing. Six windows opening together is the ordinary case on this
  machine.
- **The index is published after the sweep, atomically**, or the picker can hold a row for a record
  the sweep is removing.
- **Staleness is `mtime >= last load`, with the size beside it** — not `>`. Two writes inside one
  filesystem timestamp granularity are real, and a clock that steps backwards is real; the filename
  set catches an added or removed file regardless, so this decides only whether to re-read one that
  is still there.
- **The sweep does not break locks.** A2 already owns that rule, with its residual stated in its own
  header, and a second lock-breaking path with a different window would be two answers to one
  question. The sweep collects only a lock far older than A2's own window, and through the store.

### B3's picker must not reopen what ANOTHER window has open

Written into the plan after B3/B4's gate round, where all three vendors converged on it and it had
already been flagged in the round's own brief as a known deferral — which is what made the deferral
indefensible rather than a judgement call.

A row was open-or-closed. A conversation open in another VS Code window is neither: this window
cannot reveal that tab, because no extension can raise a window it is not running in, so the row was
drawn as CLOSED and pressing it read the record back and opened a second tab onto it. One record,
two tabs, two writers — the store's compare-and-swap catches the collision and forks the conversation
under a new id, so the product causes the exact accident that swap exists to catch, and tells the
person about it afterwards.

- **A row is `here`, `elsewhere` or `closed`**, not a boolean, so the compiler asks at every use.
  `elsewhere` is declined with a sentence saying where it is, and cannot be forgotten either — the
  window holding it would write the files straight back, which is the rule that already protected an
  open tab here.
- **The signal already exists.** B1's heartbeat names what each window holds, and the index's refresh
  already surveys the directory that holds them, so this costs no second listing.
- **This window's own heartbeat is left out.** It is written at most once a minute, so it goes on
  naming a conversation whose tab closed seconds ago; believing it would have this window refuse to
  reopen its own conversations.
- **Liveness is judged when the question is asked**, not when the survey ran, or a picker left open
  goes on holding another window's conversations hostage after that window has gone quiet.
- **A reveal is still tried first, whatever the row says.** The registry can move between the list
  being drawn and the row being pressed, and refusing on a heartbeat read seconds ago would refuse a
  tab that is right here.

### B4's forget sets the transcript aside, because the operator asked for an archive and no dialog

The same round. The operator's decisions were *archive, not destroy* and no confirmation dialog in
the way of a basic action — *«Модальные окна на каждое базовое действие превращают софт в пытку»* —
and the first build honoured only the second: the trash unlinked both files. `Alt+Delete` aimed at
the wrong row of a list somebody is filtering is one keystroke, and what it took was the sole copy of
a conversation.

The metadata is removed, so the row goes at once and nothing lists it again, and the transcript is
RENAMED into the quarantine the store already keeps, under a dated name the existing sweep rule
retires at the same ninety days. Recoverable by hand until then, nothing accumulating for ever, and
no dialog. `retireIfExpired` stays the path that really deletes: quarantining what has just aged out
would simply keep it another ninety days.

**And the keybinding needed a macOS form.** The focus in a QuickPick is always in its filter box, and
on macOS `Alt+Delete` is the system's own delete-word-forward — a person editing what they had typed
would have forgotten a conversation instead of a word. `Cmd+Delete` is that platform's move-to-trash,
which is now literally what the command does.

### A4's migration compares TRANSCRIPTS, because the store's copy may be behind

Written into the plan after A4's own gate round, where codex and gemini found the same defect
independently and were right. The first design skipped any id the store already held, on the reasoning
that the dual write had been filling it for a version and a record on disk was therefore newer.

**It is not.** A3's store write is best-effort — it can answer `busy`, `failed` or `refused` — and the
memento went on being the source of truth regardless. So the memento can hold eight turns while the
store holds the same conversation at five, and skipping on the id and then emptying the key deletes
three turns of somebody's conversation permanently, with every test green. The comparison is
therefore the same containment the fork rule already uses: the store's record is a prefix of the
memento's, or absent, and the memento is written; they are equal and nothing is done; the store has
what the memento does not and the store is left alone.

Four more things the same round settled, each of which was a way to lose a conversation quietly:
only an `ok` save counts as present (a `partial` leaves a record nothing can list); an `incompatible`
record never counts, because a record this build cannot read is not proof the conversation is safe; a
damaged memento entry is quarantined rather than dropped, which would delete it silently, or counted
as missing, which would wedge the migration into re-running for ever; and the memento is re-read
immediately before the clear, because another window can write into it while this one migrates.

And two about the moment the cut-over happens: `show` keeps writing both copies until the migration
has actually SUCCEEDED in this window — the first design stopped unconditionally, so a window that
could not reach the store preserved the memento and then wrote the person's next edits nowhere — and
the reload serializer waits for the migration before it answers, because VS Code calls it during
activation and an unmigrated conversation reads as absent, which disposes a tab the person had open.

**A4 removes a bound before B1 adds one, and that gap is deliberate rather than overlooked.** The
memento's retention was a sweep at activation cutting the store to seven days and twenty records;
when the memento is emptied, that sweep goes with it, and the store's own — ninety days, no count cap
— is story B1. Between the two, the conversations directory grows without anything retiring it. The
window is one story long and the growth in it is bounded by how much a person can say in that time,
which the table above puts at tens of megabytes a year; the reason it is written down rather than
left is that "we will add the sweep next" is exactly the sentence a growth surface is introduced
under. **B1 does not ship later than A4 by more than one working session, and if it does, this plan
is wrong and the sweep moves into A4.**
| `chat-doors.jsonl` | +110 B per invocation of the two new doors | kept forever by the ruling recorded in `chatDoorsFile.ts:14-20` | already handled |

## Boundaries with other plans — named on both sides

| Item | This plan | The other |
|---|---|---|
| A conversation is handed to a model that never heard it after a **press** | reopening from the store is a press (*go to*, a picker row) and goes through `carriedFrom` on the first question, the sixth of the counted handovers | [PLAN_a_dead_process_could_carry_the_conversation_too.md](../todo/PLAN_a_dead_process_could_carry_the_conversation_too.md) owns the case where NO press happened — a crash — and stays gated on its measurement. Nothing here re-sends anything without a press. |
| Right-click items in Claude's panel | adds two items to `webview/context` and `editor/context` | [PLAN_the_menu_path_opens_nothing.md](../todo/PLAN_the_menu_path_opens_nothing.md) still owes one human press with the probe extension disabled; the new items are pressed in the same session and the result recorded there |
| A disposed chat session leaves a grandchild on WSL/Linux | *New chat* disposes a session per press — more often than a tab close | [PLAN_closing_a_chat_ends_its_whole_tree.md](../todo/PLAN_closing_a_chat_ends_its_whole_tree.md) is DECLINED with a re-open trigger, *"a chat routinely held in a WSL or Linux window"*; this plan does not change the launcher and names itself as one more reason to re-open that one if the operator's chats move off Windows |
| The rounds-log page reads the local database | conversation files carry `id`, `title`, `modelId`, `updatedAt` — the same `conversation` id the turn ledger records | [PLAN_local_db_reader.md](PLAN_local_db_reader.md) may ingest them later; this plan writes nothing into `coai.db` |
| Translations that exist but are out of date pass the suite | all five languages of the chat article change in the same commit, with the new sentences | [PLAN_a_stale_translation_is_invisible.md](../todo/PLAN_a_stale_translation_is_invisible.md) is what would make that checkable; still open |

What is disjoint: nothing here touches the review gate, the server, the Team-server protocol or the
turn ledger's shape.

## Build order — four epics, thirteen stories

One branch, one pull request. **Every story is its own commit, its own code round** (`branch` = that
commit, `baseRef` = its parent, so the diff is three dots by construction), and green on its own:
`npm test` passes at the end of every story, never only at the end of an epic. A story that is not
reviewed, documented, tested and committed is not finished.

The split was made by Fable against the tree rather than from this document, and it changed three
things this plan had wrong — recorded here because the plan is the scope every round reads:

- **`chatOrphans.remember` is synchronous on purpose** (`chatOrphans.ts:113`): the ledger is written
  before a child can be orphaned. "One async helper called from both places" is not implementable;
  `atomicFile.ts` exports a sync form for the ledger and an async form for the store.
- **The memento does not move to disk in one commit.** Dual write first (the file store filled
  beside the memento, which stays the source of truth), cut-over second. The single swap would have
  asked one reviewer to judge *does every write reach the store* and *is nothing lost when the key is
  emptied* in one diff, and the second question is the only one in this plan that can destroy a
  person's history.
- **`switch` is built before `go to`.** *Go to* ends in the picker in three of its five branches and
  reuses the picker's own widening of `restoreConversation`; the picker needs none of the identity
  work.

Two facts that force placement, also from that read: `Thread.saveId` is `readonly` and becomes
mutable in A3, its first need; and every door's handler must call something as `x(chatPanels, …)`,
because the wiring test's marker for "the work" is the literal `(chatPanels` (`chatWiring.test.ts:402`).

### EPIC A — the conversation store on disk

Everything else reads or writes it, and it holds the only migration.

| Story | What it lands | Tests |
|---|---|---|
| **A1** *Atomic write, and the store's pure shapes* | `atomicFile.ts` (sync + async), `chatOrphans.ts` calling it, `chatStore.ts`: the record, `ConversationSource`, `isRecord`, `metaOf`, `isStale` by `rev`, `expired`, `fromLegacy` | `atomicFile.test.ts`, `chatStore.test.ts` — round trip, a record without the new fields, wrong `version`, damaged dropped, meta derivation, 90 days on a pinned clock, the legacy mapping |
| **A2** *The file protocol* ⚠ | `chatStoreFile.ts` + `chatStoreLock.ts`: `save(record, expectedRev)` → ok \| partial \| refused \| incompatible \| failed, `read`, `forget`, `listMeta`, `state()` — record then meta under one `rev`, claimed by an exclusive-create lock per conversation, delete meta then record, stale meta regenerated under the claim, `ENOENT` empty vs `unavailable(reason)` | `chatStoreFile.test.ts` and `chatStoreLock.test.ts` against a real directory — order, torn `.tmp`, reconciliation, interrupted delete, a refused stale writer, racers on one lock, a lock broken at the stale boundary, the downgrade case, missing vs unreadable |
| **A3** *Dual write, and the re-key message* ⚠ | `show()` writes the store beside the memento; `Thread.rev`, `saveId` mutable; a refused write re-mints under a new id, keeps the transcript, and tells the page. **Does NOT decide which workspace a conversation is filed under** — it uses the first root, and the boundary is named in C1's row, which is where the source that answers it properly arrives | reload-suite source guards, `chatPage.test.ts` for the re-key, a store test that both transcripts survive a fork |
| **A4** *Cut-over: read from the store, import, retire the memento* ⚠⚠ | the serializer reads the store, **waiting for the migration first and falling back to the memento while its key is non-empty**; the memento imported where the store's record is **not already a superset of it** — compared by TRANSCRIPT, never by id; only an `ok` save counts as present, and `incompatible` never does; a damaged record is quarantined rather than dropped or left to wedge the run; the key re-read and emptied once, after every record is confirmed; `show` keeps dual-writing **until the migration actually succeeds in this window**; `ChatTabMemory` and the `KEEP_*` constants go; `incompatible` and `unavailable` each restore a DEFINED tab that says what happened rather than an undisposed blank one | `chatStoreImport.test.ts`: the newer-memento case, the interrupted run, an unavailable store, a quarantined record, a concurrent write before the clear; the reload suite re-pointed, and both blocked-tab states |

### EPIC B — CoAI: switch conversations…

| Story | What it lands | Tests |
|---|---|---|
| **B1** *Housekeeping and the index* ⚠ | `chatStoreSweep.ts` (90 days, `.tmp` over an hour, orphan meta, stale meta, **an orphaned TRANSCRIPT with no metadata beside it** — left by a crash between A2's two deletes, invisible to the listing and holding its id against reuse — and a quarantined `incompatible` record, which A2 deliberately writes nothing over) and `chatStoreCache.ts` (build in the background at activation, `refresh()` reconciling the filename set and `mtime ≥ last load`, `bySource`, `entries(scope)`); neither runs while the store is `unavailable`; a stale `<id>.lock` older than its window is collected here too | sweep and cache tests on a real directory with a pinned clock |
| **B2** *The picker's rows, pure* | `conversationPicker.ts` — *Open* then *Recent*, ordering, row text, the workspace filter, open ids excluded, newest 100 rendered, an `unavailable` notice row | `conversationPicker.test.ts` |
| **B3** *The command* | `conversationPickerCommand.ts` (`createQuickPick`), `openConversations(panels)`, `restoreConversation` with `panel` optional — its first caller without one; `Door` gains `switch`; manifest, help ×5, README, CHANGELOG | `chatWiring.test.ts` (derived door, a gone record reported not restored), `chatDoors.test.ts`, `helpCoverage` |
| **B4** *Forget and scope* | the trash button and `coai.forgetPickedConversation` on `Alt+Delete` scoped by `inQuickOpen && coai.conversationsPickerOpen`, rebuilding without hiding; the all-workspaces toggle | the wiring test learns that a keybinding scoped to our own picker is NOT a door, and says why |

### EPIC C — CoAI: go to conversation

| Story | What it lands | Tests |
|---|---|---|
| **C1** *A durable source, written and followed* | `snapshots()` carries the URI; the Claude UUID written when the pin lands — explicitly, because the `show()` dedupe guard compares messages, model and mark only, so a source change alone would never reach disk through it; `onDidSaveTextDocument` / `onDidRenameFiles` follow a source. **And the WORKSPACE a record is filed under**, which A3 deferred here: three reviewers found that a multi-root window files every conversation under its first root, so a chat opened in the second root is invisible to a picker filtered on the second root. The right answer needs the source — the root that CONTAINS the conversation's file, or the root whose Claude session it came from — which is exactly what this story lands, so it is this story that stops using `whereToLook()[0]` | `chatGoto.test.ts` for derivation and following; a test that a conversation in the second root of a multi-root window is filed under that root; wiring guards for the two listeners |
| **C2** *The decision, pure* | `chatGoto.ts`: `reveal \| reopen \| pick(narrowed) \| pickAll \| nameFailure`, over tab kind × live match × this workspace's candidates × session ambiguity × store state; the *New conversation for <label>* row | `chatGoto.test.ts` exhaustively; `conversationPicker.test.ts` for the New row and narrowing |
| **C3** *The door* ⚠ | `chatGotoCommand.ts`; `restoreConversation` takes `key`/`label` and registers through `panels.open(key, label, factory)` with `createChatPanel` moved INSIDE the factory; a record already live under another key is revealed by `saveId`; `Door` gains `goto`; manifest, help ×5, README, CHANGELOG | wiring guards for all three; `chatDoors.test.ts`; `helpCoverage` |

### EPIC D — new chat

| Story | What it lands | Tests |
|---|---|---|
| **D1** *The reset, behind the existing gesture* ⚠ | `freshStart` in the order above — stop, `await thread.turns`, a `generation` checked in `oneTurn` before an answer is appended, `await` disposal then `home.release()`, a failure leaving the old conversation live; `closedSession` extracted; `chatFresh.ts` pure for what is cleared and what is kept | `chatFresh.test.ts`; wiring guards for the order, the generation check and the failure branch; `chatPage.test.ts` |
| **D2** *The header button, and the release* | the `New chat` button with its OWN id (`wireCapped` finds `#restart` by id, and two elements cannot share one); help ×5, README, CHANGELOG, `package.json` 0.39.0, and the research docs' final pass | `chatPage.test.ts` for header order; `bundledPage.test.ts` pressing the shipped, minified button |

⚠ marks a story where being wrong is expensive — persistence, a migration, a sweep that deletes by
clock, or an in-flight answer that could be mis-filed. ⚠⚠ is the migration itself: a wrong
"everything is present" check empties the only copy of every conversation a person has. Those are
implemented by the stronger model, per the operator's standing orders, and the model used is named
per story in the final summary.

## Test plan

- **Pure modules are tested as values**; the `vscode` halves are read as source, the way every chat
  test here does it — a new `registerCommand` without its `noteChatDoor`, or a serializer path that
  skips `createChatPanel` (`theTabWearsAnIcon.test.ts:103-108`), goes red.
- **Every fixture goes through the real validator**: a saved record is built with the store's own
  writer and read back with its reader, never typed as JSON and cast.
- **Time is an argument** — the sweep, `updatedAt`, `closedAt` and the picker's *ago* take a clock,
  and every test pins it; nothing reads `Date.now()` in a test.
- **The shipped bundle is pressed** for the header button, as it is for *Send* and *Carry nothing
  above*.
- **The migration is tested against a real directory** with a memento fake, including the
  interrupted case: half the records written, the key still populated, the next activation writing
  only what is missing and emptying the key exactly once.
- **What the gate found gets a test each**, because a finding without one is a paragraph: a record
  renamed with its metadata left behind is reconciled by `rev` on the next read; a delete interrupted
  after the metadata leaves no row; a second writer holding a stale `rev` forks instead of
  overwriting, and both transcripts survive; an `EACCES` directory reads as `unavailable` and neither
  empties the picker nor sweeps; a file deleted by another window leaves this window's cache on the
  next reconcile; an `untitled:` source follows its document through a save; a workspace that does
  not match is not matched by *go to*; a late answer whose generation has changed is dropped rather
  than appended; a `freshStart` whose disposal fails leaves the old conversation live and says so.
- A manual pass in a running window, recorded in the pull request: ten tabs, *go to* from three of
  them; close one, reload, *go to* finds it; *switch* filters by a model name; trash a row and the
  picker stays open; and *New chat* on **each of the three CLI adapters** — a question that plants a
  number, *New chat*, then a question asking for it back, which the model must not know.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/go-to-conversation`) and its own pull request**, and the
PR is accepted only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` per story with the scope = that story's section above
   plus the *Definition of Done*, `branch` = the story's commit and `baseRef` = its parent — three
   dots by construction; the moving-base trap is recorded in
   `research/PLAN_the_gate_diffs_from_a_moving_base.md`. `resolve`; repeat until `proceed`.
4. **Documentation.** `research/module_extension.md` says what the code now does — the store, the
   two commands, the header button, and its `## Commands` table (`:1800-1807`), which lists none of
   the chat doors today; `research/module_tests.md` gains the rows; `research/architecture.md` names
   the conversation store beside the two ledgers.
5. **Help.** Every new command has its sentences in `helpContent.ts` — `helpCoverage.test.ts`
   fails the build otherwise — and in `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts`, in the
   same commit.
6. **README and CHANGELOG.** `src_vs_code/README.md` — the chat section gains the two commands and
   the button; `src_vs_code/CHANGELOG.md` under *Unreleased*, in the prose the file already uses.
7. **Manifest.** `package.json` — three commands, two menus, four keybindings, and the version the
   release line expects (`0.39.0`: a feature).
8. **Family checks.** `node .agents/conventions/tools/plan-lifecycle.mjs` and `pin-check.mjs`
   clean; `rules check` resolved.
9. **Promote.** On merge, `/promote-plan` this file to `research/` with `IMPLEMENTED <date>` and
   every deviation recorded — what shipped differently is the most valuable line of the record. Cut
   `extension-v0.39.0` on `main` after the merge; a version bumped in a manifest is not a release.

## Definition of Done

- [ ] With a live conversation for the active tab, `Ctrl+Alt+G` focuses it — no picker, no capture.
- [ ] With a closed conversation for the active tab — a Claude session by its UUID, a file by its
      URI — `Ctrl+Alt+G` reopens it bound to that tab, transcript shown, nothing running, and the
      first question carries it; two days and a window reload later included.
- [ ] With nothing for the active tab, the picker opens with *New conversation for <tab>* first;
      nothing is created until Enter.
- [ ] Two candidates for one tab open the picker narrowed to them, with date, folder and message
      count; nothing is picked for the person.
- [ ] `Ctrl+Shift+Alt+G` lists open and closed conversations, newest update first, filtered by title,
      model or last line; a row reveals or reopens; the trash button and `Alt+Delete` remove a closed
      one and the picker stays open; the scope toggle spans workspaces.
- [ ] From a terminal or an Output tab both commands open the picker.
- [ ] Transcripts live under `<coaiDataDir>/chat-conversations/`, written `.tmp`-then-rename, record
      before metadata and both carrying one `rev`; swept at 90 days; the picker never reads a
      transcript file; legacy memento records are migrated once, idempotently by id.
- [ ] An interrupted save, an interrupted delete, a stale second writer, an unreadable directory and
      a file another window removed each behave as this plan says — one test apiece.
- [ ] *Go to* never matches a record from another workspace, and never opens a second copy of a
      conversation that is already live.
- [ ] *New chat* in the header and *Start a new conversation* in the cap notice: the running turn
      ended and waited for, a late answer dropped, the session disposed before its directory is
      released, a failure leaving the old conversation live and saying so; then passage cleared,
      transcript emptied, running cost reset, the old conversation in *Recent*, a non-modal line
      saying where it went, no dialog — and the next question reaches a model that knows nothing of
      the previous ones, verified with a planted number on all three CLI adapters.
- [ ] Both new commands write a `chat-doors.jsonl` line before anything can refuse them, on the
      ledger's stated best-effort terms — a ledger that cannot be written costs the record, never the
      command.
- [ ] Help in five languages, README, CHANGELOG, manifest, `module_extension.md`, `module_tests.md`,
      `architecture.md` all say what the code does.
- [ ] `npm test` green with the observed numbers in the pull request; every code round's verdict and
      reviewer count reported; promoted on merge; `extension-v0.39.0` tagged.

## Parallelism

Owns `chatCommand.ts` (the store write in `show`, `restoreConversation`'s signature, `onRestart`,
`snapshots`, the new accessors), `chatTabs.ts`, `chatOrphans.ts` (one call), `chatPage.ts` (the
header and the `fresh` handler), `chatMessages.ts` (nothing, if `restart` is reused as planned),
`chatPanel.ts` (the `fresh` push helper), `extension.ts` (registration, the serializer, the
migration), `package.json`, the five help files, and the new modules. **Does not touch**
`sessionKey.ts`, `chatPanels.ts`, the adapters, `cliChatSession.ts`, or anything under `src_mcp`.
`chatCommand.ts` is over the file-size rule already (2 889 lines against 800 when this plan was
written, 3 361 with epic C landed); every new decision goes into a new module, and the extraction
named in the reload plan's open tail is not taken here. The first code round on C3 asked for the
split again and it was declined again for the same reason: a breach that predates this plan by 2 889
lines is not one a story can honestly close, and moving a hundred lines out would buy a number
rather than a structure. The pure halves DID move, to `chatGoto.ts`, where they are testable.
