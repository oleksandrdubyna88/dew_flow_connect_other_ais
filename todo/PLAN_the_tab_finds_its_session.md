# PLAN — the tab finds its session, or offers the choice

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/claudeSessions.ts`,
> `claudeQuestion.ts`, `chatCommand.ts`, `chatGoto.ts`, `conversationChoice.ts`,
> `conversationPicker.ts`, `chatPage.ts`, and one new pure module for a session picker's rows.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_what_you_asked_is_on_disk.md](../research/PLAN_what_you_asked_is_on_disk.md),
> [PLAN_go_to_conversation.md](../research/PLAN_go_to_conversation.md).

## The symptom

The Asked region, on the operator's screen, under a chat tab called *coai 7 issues*:

> No session in `C:\Users\strug\.claude\projects\d--rsd-ClaudeRag` is called “coai 7 issues” —
> Claude Code names a conversation once it has one.

The session was open in the editor group beside it while that sentence was on screen. In their
words: *«сесия точно есть»*, and then the part that names the mechanism — *«я переименовал вручную,
оно изменило, а потом через пару минут вернуло старые названия»*.

They are right on both counts, and the refusal is wrong twice over: the session exists, and this
build cannot see the name it is wearing.

## What was measured

2026-09-16, this machine, `~/.claude/projects/d--rsd-ClaudeRag`: 101 session files, 1.2 GB.

| | sessions |
|---|---|
| carry a `custom-title` and **no** `ai-title` — invisible to the lookup today | **4** |
| carry both, and the last of each **disagree** | 5 |
| carry no title row of any kind — nothing on disk to match | 5 |
| carry more than one distinct `custom-title` — renamed, then reverted | 2 |
| distinct titles over the whole folder | 162 |
| **titles claimed by more than one session** | **0** |

The last row is the licence for the rule this plan adopts: matching against **every** name a session
has ever carried creates no ambiguity at all on the largest real folder available.

**The revert, in the file.** `942e84d6-….jsonl`, three consecutive `custom-title` rows:

```
1274  {"type":"custom-title","customTitle":"/feature-dev:feature-dev …выбери 7 самых простых…","sessionId":"942e84d6…"}
1281  {"type":"custom-title","sessionId":"942e84d6…","customTitle":"coai 7 issues"}
1285  {"type":"custom-title","customTitle":"/feature-dev:feature-dev …выбери 7 самых простых…","sessionId":"942e84d6…"}
```

Note the **key order**: the rename at 1281 spells `sessionId` before `customTitle`, the two around it
spell it after. Two writers, and the one that runs on every turn re-asserts a title the person had
already replaced. So *"the last title"* is not a reliable answer in either direction — and
`titleOf` (`claudeSessions.ts:469-481`) keeps exactly that.

**Cost, for the record.** Reading every title in that folder — 1.2 GB, streamed — takes **5.2 s warm**.
That is what the Asked button already does on a press that has no pin, because `titleOf` reads each
file to EOF for its last title. Resolving by session id instead is one `stat`.

## Why it happens — three separate causes

1. **Only one of the two title rows is read.** `titleFrom` (`claudeQuestion.ts:274-286`) parses
   `ai-title` and nothing else; `custom-title` appears nowhere in this repository. The second reader
   has the same blindness by its own copy of the rule — `AskedSet.title` (`claudeQuestion.ts:56`) is
   filled at `:157` from `ai-title` alone, so *Take the question* mis-joins for the same sessions.
   The two readers must change together; they have drifted apart before, and
   `claudeSessions.ts:191-199` is the comment left by the last time they did.
2. **The identity already in the store is never used.** A pinned conversation keeps
   `source: {kind:'claude', sessionId}` (`chatStore.ts:113`), and that id **is** the file name.
   `restoreConversation` nevertheless sets `sessionFile: ''` (`chatCommand.ts:3520`) and the next
   press walks the folder **by name** (`resolveAndPin`, `:535`). So a conversation whose file we can
   name exactly is hunted by a string that another program is rewriting underneath us.
3. **A name is not a stable identifier, and for some sessions there is no name on disk at all.**
   Five of 101 have no title row; their tab is named from the first prompt, and that name is written
   nowhere. No lookup can ever find those, which is what the fallback exists for.

## What must be true when this is done

1. A tab whose Claude Code conversation was **renamed by hand** finds its session — under the new
   name and under every name it previously had.
2. A tab whose conversation carries a `custom-title` and no `ai-title` finds its session.
3. A conversation that was pinned once finds its session **by id**, without reading a single
   transcript, and regardless of what it is called now.
4. *Take the question* joins tab to session by the same rule as the Asked button — one rule, not two.
5. When the name matches **nothing**, the refusal is not a dead end: it offers to choose the session,
   from a list filtered as you type, with the name that was looked for already in the filter box.
6. When the name matches **several**, the same choice is offered instead of the bare refusal.
7. A session chosen that way is **kept**: the conversation records it, and a reload finds it by id.
8. The *go to conversation* path stops claiming *"the folder did not answer"* when the folder answered
   perfectly well and nothing was called that, and a narrowed pick in a multi-root window stops
   rendering an empty list.

## Build order

### Story 1 — every name a session ever had *(pure, `claudeSessions.ts` + `claudeQuestion.ts`)*

- `customTitleFrom(line)` beside `titleFrom(line)` in `claudeQuestion.ts`, same shape, same guard.
- `titleOf(file)` becomes `namesOf(file): Promise<readonly string[]>` — every `ai-title` and every
  `custom-title` the file carries, in order, deduplicated, empties dropped. One pass, so the read
  costs what it costs today.
- `namesTheSame(title, looking)` is unchanged; `theOneCalled` (`:390`) matches a file when **any** of
  its names is the same name. The `several` refusal keeps its wording and its precedence.
- `AskedSet.title: string` becomes `AskedSet.names: readonly string[]`, filled from both row types in
  `lastAsked`; `waitingIn` (`:140`) matches on the set. The field's doc comment carries the measured
  reason, because it is the paragraph that made the single string look sufficient.

**Refused alternative, recorded here so it is not re-proposed:** deriving the title Claude Code shows
for a session that has **no** title row (it is the first prompt, cut at 200 characters, with the
slash-command envelope unwrapped — measured on `942e84d6`). It is another program's undocumented
derivation, and a decoder that drifts binds a tab to the wrong conversation silently. Those five
sessions are the picker's job, not a guess's.

### Story 2 — identity before the name *(`claudeSessions.ts` + `chatCommand.ts`)*

- `sessionFileOf(home, cwd, caseBlind, sessionId): Promise<Found>` — `projectsRoot` + `projectDirIn`
  + `<sessionId>.jsonl`, `access`-checked, returning the same `Found` union with its own refusal when
  the file is not there. No transcript is read.
- `resolveAndPin` tries the id from `thread.source` first and falls back to the name walk. The path is
  still never persisted — it is recomputed from an id that already lives in the store, which is the
  rule `chatCommand.ts:3517-3519` states and this keeps.
- A resolution that succeeds on the Asked path now writes `source`/`workspace` and queues the save,
  as `pinSession` does. Today it does not, so the same walk is repeated after every reload.

### Story 3 — the sessions can be listed *(pure)*

- `sessionsIn(home, cwd, caseBlind): Promise<readonly SessionCard[] | ReadFailure>`, where a card is
  `{file, sessionId, names, at}`. `sessionFiles` already computes the `mtime` at `:199-205` and throws
  it away at `:208`; `theOneCalled` already builds the candidate list at `:390-396` and drops it.
  This is an extraction of code that runs today, per the widen-then-extract order in `reuse-first.md`.
- A new pure module decides the rows — title, a *when* like the conversation picker's *"just now" /
  "yesterday"*, the id as the tie-break for two sessions of one name — and the widget builds none of
  its own, which is the shape `conversationPicker.ts` established and
  `conversationPickerWiring.test.ts:127-136` enforces.

### Story 4 — the refusal has a door, and the choice is kept *(`chatPage.ts` + `chatCommand.ts`)*

- The Asked region gains one button, rendered **only** for the two refusals a choice can answer
  (nothing of that name, several of that name). Pressing it posts a new page→host message; the host
  opens a native QuickPick with `matchOnDescription`/`matchOnDetail` and `value` pre-filled with the
  name that was looked for — de-ellipsised, since a tab's name is what VS Code truncated.
- No new command in `package.json`: the door is where the dead end is, and a manifest command would
  pull in a help article and five translations (`helpCoverage.test.ts:95-112`) for a control that is
  only ever reachable from one sentence.
- Choosing a session pins it **durably**: `thread.sessionFile`, then `source` + `workspace` through
  the existing pair-writer, then `keepQueued` — the operator's decision of 2026-09-16, taken over a
  once-only reveal, so a reload never asks twice. This is the first deliberate re-origin of a
  conversation after creation (`chatSource.ts:60-64` warns that stored sources are otherwise never
  repaired); it is a person's explicit choice, and the QuickPick's title says which tab it binds.

### Story 5 — the two wrong sentences *(`chatGoto.ts` + `conversationChoice.ts` + `conversationPicker.ts`)*

- A folder that answered and matched nothing currently becomes
  `{kind:'unreadable', reason:'this tab’s Claude sessions could not be read'}` (`chatGoto.ts:279`) and
  is rendered as *"the folder did not answer just now"* (`conversationChoice.ts:97-98`). It answered.
  A fifth `Narrowing` member with its own sentence; both switches are exhaustive by name, so the
  compiler enumerates the work.
- `pickerRows` filters on `one.workspace === input.workspace` (`conversationPicker.ts:153`) against the
  window's **first** root, so a narrowed pick for a tab under a second root renders empty — and the
  globe that would widen it is suppressed for narrowed lists (`conversationPickerCommand.ts:251`).
  Compare with the `sameRoot` rule that exists for exactly this (`chatSource.ts:140`).

## Test plan

RED first, every one of them, per `common/testing.md`. The suite is `node:test` over real temp
directories; the fixtures are the `titled` / `said` builders in `claudeAsked.test.ts:47-54`, plus a new
`renamed(name)` builder for a `custom-title` row.

| # | The guarantee | Red against today's code because |
|---|---|---|
| 1 | a renamed conversation is found by its new name | no `custom-title` is read at all |
| 2 | a conversation renamed and then reverted is found by **both** names | only the last title is kept |
| 3 | a session with a `custom-title` and no `ai-title` is found | `titleOf` returns empty |
| 4 | two sessions sharing one historical name are still **refused**, not picked | — (must stay green; it is the rule this loosening could break) |
| 5 | *Take the question* finds the question in a renamed conversation | `AskedSet.title` is `ai-title` only |
| 6 | a conversation with a stored id finds its file though every title differs | the name walk is the only path |
| 7 | resolving by id reads no transcript | — (assert on a folder whose files would fail to parse) |
| 8 | `sessionsIn` lists every session with its names and its time, newest first | it does not exist |
| 9 | the rows carry the name, the time and the id, and the widget builds none | it does not exist |
| 10 | the picker's filter box opens holding the de-ellipsised name looked for | it does not exist |
| 11 | a chosen session is written to the record, and a restored conversation finds it | the Asked path writes no source |
| 12 | the Asked region shows the choice button **only** for the two answerable refusals | it does not exist — asserted by RUNNING the page (`rolesPageHarness`), per `.agents/PROJECT.md:87-97` |
| 13 | a folder that answered and matched nothing says so, and does not claim it was unreadable | the sentence is wrong today |
| 14 | a narrowed pick for a tab under the second root is not empty | it is |

Then the whole suite: `npm test` in `src_vs_code` (`node --test` over `out/test/*.test.js`, after
`npm run clean && tsc`), and both observations — the red message and the green — reported in the
summary rather than "tests pass".

## Definition of Done

- [ ] Every test above written first, observed red with the real symptom, then green.
- [ ] `npm test` green in `src_vs_code`, with the number reported.
- [ ] `npm run typecheck` clean — `tsc` emits despite type errors, so the exit status is read.
- [ ] The two readers of a session's name changed together, and a test pins that they agree.
- [ ] No new command and no new setting in `package.json`, so no help article falls due; if one is
      added after all, all five languages change in the same commit (`helpCoverage.test.ts:236-285`).
- [ ] `research/module_extension.md` — the tab↔session join (`:1398-1421`) rewritten to the new rule,
      the id-first path and the picker; `research/module_tests.md` gains the flow rows.
- [ ] `CHANGELOG.md` entry in the house voice, naming the symptom a person would recognise.
- [ ] The coai gate: a plan round to `proceed`, then a code round, both resolved finding by finding.
- [ ] This plan promoted to `research/` with `IMPLEMENTED <date>` and its deviations recorded.
