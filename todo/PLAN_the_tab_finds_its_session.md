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

## The gate's plan round, 2026-09-16

Three reviewers, sixteen findings, verdict `good_enough` with the rounds exhausted. **Thirteen were
accepted and are requirements below**; three were rejected with reasons recorded in the session:
the atomicity of a plan promotion's `git mv` (not a property of this feature — `plan-lifecycle.mjs`
already fails CI on a half-filed plan); what the tab displays if the conversation is renamed *after*
it is pinned (a coai conversation's title has never mirrored Claude Code's, and after A2 the join is
an id, so a later rename means nothing); and a precedence rule between `ai-title` and
`custom-title` for MATCHING (there is none to have — a session answers to both, and picking one
would re-create the defect being fixed).

The two that changed the design rather than adding to it:

- **A current name beats a former one** (gemini). Matching any historical name lets a session that
  *used* to be called *Refactor X* outrank the one called that today. So: a session whose LATEST
  name matches answers first; former names answer only when no session currently bears the name.
  Ambiguity inside a tier is refused exactly as it is now.
- **The five titleless sessions would render as blank rows** (codex) — and they are the picker's
  whole reason to exist. The enumerating pass already parses every line, so it keeps the first thing
  the person said and labels the row with it. That is a LABEL, not a match: the refused alternative
  below is about matching, and it stays refused.

## Build order — three epics, eight stories

Each story is reviewed by the gate on its own diff (`review_code`), resolved, documented, tested and
committed before the next one starts. Epic A alone fixes the operator's bug and could ship by itself.

### EPIC A — the tab finds its session

- **A1 — a renamed conversation is found under both its names, and a current name beats a former one.**
  `customTitleFrom` beside `titleFrom`; one `NameCollector` both readers feed, so they cannot drift;
  `titleOf` becomes `namesOf(file): Promise<SessionNames>` with `{current, former}`; `namedAmong`
  carries the two-tier rule and is used by `theOneCalled` **and** `waitingIn`, which is how *Take the
  question* stops having its own copy of the rule. `AskedSet.title` becomes `AskedSet.names`.
- **A2 — a pinned conversation finds its file by id, reads no transcript, and never trusts a stored
  path.** `sessionFileOf(home, cwd, caseBlind, sessionId)`: the id must match the UUID shape
  (`isSessionId`, exported from `chatSource.ts` so it is stated once) **and** the resolved path must
  stay beneath the project directory before anything is touched. `access` only, never a read.
  `resolveAndPin` takes the thread: id first, the name walk when there is no id or its file is gone,
  and one `adoptFound` helper both it and `pinSession` call. Only `source` + `workspace` reach the
  record; the path stays in memory and is recomputed.
- **A3 — the lookup names its budget.** `ScanBudget {most, withinMs}` with numbers measured here,
  on **both** readers — `waitingQuestion` is the one that reads each file WHOLE (`:547`), so it needs
  it more than the Asked button does. A cut that found nothing says so and never reports silence:
  *"Only the newest 250 of 1 010 sessions in … were read in 10 s, and none of them is called X."*

### EPIC B — the refusal has a door — **DEFERRED 2026-09-16, extracted to its own plan**

After epic A the lookup answers under every name a session has worn and, for a pinned conversation,
by the session id itself — so the picker is needed for the **5 sessions in 101** that carry no title
row at all, and for a name two sessions genuinely share. The operator chose to wait until the refusal
is met again on a live session rather than build for it now. The whole design, and the trigger that
brings it back, are in
[PLAN_the_sessions_can_be_offered.md](PLAN_the_sessions_can_be_offered.md).

### EPIC C — the two wrong sentences

- **C1 — a folder that answered and matched nothing says so, not that it could not be read.**
  `GotoAsked` gains the fact that separates a failed walk from an answered one (today `ambiguous`
  folds both), `Narrowing` gains a fifth member, and `narrowedTitle` gains its sentence.
- **C2 — a narrowed pick shows the rows it was given, under whichever root they are filed.**
  A narrowed list is an answer `goto` already chose, so it is not filtered by the window's first
  workspace — which also empties a `cross root` pick today, a second site of the same defect.

**Refused alternative, recorded so it is not re-proposed:** deriving the title Claude Code shows for a
session that has **no** title row (it is the first prompt, cut at 200 characters, with the
slash-command envelope unwrapped — measured on `942e84d6`) and MATCHING on it. It is another
program's undocumented derivation, and a decoder that drifts binds a tab to the wrong conversation
silently. B1 labels such a session with what was said in it; a label is shown to a person who then
chooses, which is the opposite of a silent guess.

## The boundary with [the picker plan](PLAN_the_sessions_can_be_offered.md)

| Item | Built by | The other plan's part |
|---|---|---|
| Reading a session's names (`namesOf`, `collectNames`, `namedAmong`) | **this plan**, epic A — shipped | uses it; the picker's rows show `latest` and make `former` findable by typing |
| Resolving by session id (`sessionFileOf`, `isSessionId`, the containment checks) | **this plan**, epic A — shipped | uses it to re-check a chosen session at the press, and after a reload |
| `adoptFound` — the one road that writes a session onto a record | **this plan**, epic A — shipped | calls it with the folder the chosen card came from |
| The scan budget (`ScanBudget`, `ScanCut`, the size cap) | **this plan**, epic A — shipped | passes it to `sessionsIn` and renders the cut as a notice row |
| `Found.why` (`unmatched` against `unreadable`) | **this plan**, epic C — shipped | the picker's door opens for `unmatched` and for `several`, never for `unreadable` |
| The two *go to* sentences and the narrowed-list filter | **this plan**, epic C — shipped | nothing; they are independent of the picker |
| **Listing a folder's sessions** (`sessionsIn`, `SessionCard`, `claudeSessionFiles.ts`) | the picker plan — NOT BUILT | this plan only MATCHES; it never enumerates |
| **The rows, the prefill and the QuickPick** | the picker plan — NOT BUILT | this plan has none |
| **The door in the Asked region, and keeping the choice** | the picker plan — NOT BUILT | this plan pins in memory and writes only what a complete walk found |
| **The end-to-end scenario test** of page → choice → reload | the picker plan — NOT BUILT | this plan's tests stop at the module boundary, which is what leaves that gap |

**Order:** this plan first, and it has shipped. Everything the picker needs from it — the names, the id
path, the budget, `adoptFound`, `Found.why` — exists now. The picker adds only enumeration and a way to
ask; it changes nothing this plan decided.

**Disjoint:** the table is complete, not a sample. Nothing else is shared between the two.

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
