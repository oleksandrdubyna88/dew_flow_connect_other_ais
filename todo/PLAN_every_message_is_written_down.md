# PLAN — every message coai raises is written down, counted, and readable afterwards

> Status: **plan only, nothing implemented yet, 2026-09-16.** Through **two** plan rounds of this
> repository's own gate (16 then 21 gating findings against a threshold of 6; 40 of 45 accepted),
> four internal reviews and two consultations — the second round found things the first did not, and
> none of it contradicted the first round's fixes. What changed and why is in *What the first draft
> got wrong* and in the sections it points at. Scope: a notifications ledger and page in `src_vs_code`, one
> funnel in front of every `window.show*Message` call site, a live panel section under *Active rounds*,
> and — in its own phase, with its own file — the `src_mcp` half. Four defects found while inventorying
> are fixed with it.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_server.md](../research/module_server.md), [module_tests.md](../research/module_tests.md),
> [architecture.md](../research/architecture.md).
> Boundaries with five sibling plans are in *Who builds what*.

## The symptom

On 2026-09-15 at 18:56 a test role called `Role2` entered the round roster. On 2026-09-16 at 19:05:39
it was deleted from the panel — correctly: `coai.roles` lost the row at that second, and the WSL
side's `settings.json` was rewritten without it five minutes later. On the Windows side, eleven code
rounds between those two dates each ran **five** roles instead of four and each recorded:

```
round 1 runs 5 role(s): Conventions, Architecture, SecurityReliability, UxDxPerformance, Role2
round 1 CodeReview proceed: … all 12 reviewers answered; Role2 was not asked:
  its prompt 'role2-general' has no text — write it at …\prompts\role2-general.md
```

The last of them ran at **18:35 on 2026-09-16**, and `%LOCALAPPDATA%\coai-mcp\settings.json` was last
written at **18:27:19** — thirty-eight minutes before the deletion, and never again after it.

The deletion was not lost. The **mirror** into the server's settings file was. The windows that could
have rewritten it were running extension 0.46.0 against a file stamped `COAI_WRITTEN_BY: 0.47.0`
(0.47.0 was installed at 18:27:06 and one window reloaded onto it), so
`wouldOverwriteANewerBuild` ([serverSettingsSync.ts:212-253](../src_vs_code/src/serverSettingsSync.ts))
stood every write down — which is exactly what that guard is for, and it is right.

It is also not silent: it raises a warning with a **Reload Window** button
([extension.ts:776-788](../src_vs_code/src/extension.ts)). It raises it **once per window per
version**. Nobody saw it. For the next hour and a half every code round on that machine was gated
against settings nobody could see were stale, and the only evidence was a clause in a round summary
that reads like a note about a role rather than a note about the machine.

### The general defect

**A message a person misses is a message that never happened**, and this product has no second place
to look. There are **109** `window.show*Message` call sites in `src_vs_code/src` outside the tests
(68 warning, 27 information, 14 error — counted, and the count is mechanised by S1) and **no durable
record of any of them**: `createOutputChannel` appears **0** times, there is no log file, and no
telemetry ([src_vs_code/README.md:316](../src_vs_code/README.md) — *"Nothing is sent to the authors of
this extension. There is no telemetry."*). The codebase already says it out loud at
[roundsLog.ts:1832-1834](../src_vs_code/src/roundsLog.ts) — *"the only diagnostic the extension side can
leave is a `console.warn` in the extension host, which nobody opens. Four findings across both remote
vendors and three roles said so on the code round."*

Four findings said so, and the answer at the time was a console line. This plan is the answer.

## What the first draft got wrong

Recorded rather than quietly fixed, because three of these are mistakes about **this repository's own
written record**, and a reader who does not know that will make them again.

| # | The first draft said | What the record says |
|---|---|---|
| 1 | One ledger both halves append to, *"an established shape here"* | The opposite. `architecture.md:206-218`: chat rows inside `usage.jsonl` were **rejected on the gate's plan round** — *"its two writers release on different days… a format shared by two programs that ship separately is a contract nobody wrote down"*. `module_extension.md:5507` records the **same refusal a second time**, for `chat-doors.jsonl`: *"the gate refused that twice, independently."* The established shape is **two files and one in-memory merge** (`mergedRows`). → **Two ledgers.** |
| 2 | A collapsed record carrying `count`, updated in place | Impossible on this primitive, and the harness the draft cited as proof says so: `scripts/measure-append.mjs:10-15` — the guarantee is about `O_APPEND` and *"that is TRUE of a read-modify-write, and of a positional write"* (i.e. they tear). → **Append-only always; count at READ time.** |
| 3 | A 32 MB ceiling that drops the oldest half, *"as the operator already ruled"* | That ruling rejects this mechanism by name. `chatUsageFile.ts:49-56`: *"trimming by age or count bounds the file but answers 'what did I spend last year' WRONGLY rather than not at all… If a bound is ever wanted, the roll-up is the only version of it that does not lie."* → **No trim.** |
| 4 | A 60-second collapse window | Keys on a **clock**; the mechanism it replaces keys on a **state change**. `serverSettingsSync.ts:247` reports once per version and **resets on a successful write** (`:171`) because *"the situation is over; a stand-down after this is news, not a repeat"*. A clock window folds a fault that recurs in 40 s and separates one that recurs in an hour — backwards. → **Suppress while the condition holds; reset on recovery.** |
| 5 | The repeat key was the rendered `title` | `title` carries versions, paths, role names and round numbers. The Role2 complaint carries a round number, so eleven rounds mint eleven keys: **the mechanism built for this incident would not have caught this incident.** → **A stable `code`.** |
| 6 | *"The ledger is already read at activation"* | No JSONL ledger is read during `activate()` at all; both readers are lazy behind a `size:mtimeMs` cache in `panelProvider.ts:507-557`. `activate` ([extension.ts:67](../src_vs_code/src/extension.ts)) is synchronous and returns `void`. → **Nothing is read on the activation path.** |
| 7 | 109 sites · a class table summing to 113 · *"95 events"* | 109 is right (verified). The table over-counts by four and 95 is the sum of its first four rows, confirmations included; 109 − 16 = **93**. → **The split is derived by a script, and one number is published.** |
| 8 | A manual "measure against the old half before shipping" | `testing.md` calls a live cross-implementation check MANDATORY and forbids hand reconciliation as the fix — and the harness exists: `npm run test:seam` → `scripts/run-seam.mjs`, watched failing on the `remoteVendor` defect. → **A seam leg.** |

Two things the reviews confirmed rather than refuted, worth keeping: the funnel's no-throw/no-recurse
contract holds, and the class taxonomy (by what the person must do, not by severity) is right.

## What exists today

### The extension: 109 call sites, six classes

Distinguished by **what the person is supposed to do**, which is what a log has to sort on. The
counts below are the inventory's; **S1 replaces them with a derived number** (see *The number is
derived*), because a completeness promise may not rest on a hand-typed total.

| Class | What it is |
|---|---|
| **Refusal** | "you asked, and it will not happen; here is why" |
| **Failure** | an operation that started and broke |
| **Confirmation** | a modal that gates a destructive or costly act (16 of the 109) |
| **Outcome** | "it happened, and here is what to do next" |
| **Offer** | a notification whose whole purpose is its button |
| **Stand-down** | this build deliberately did nothing |

**There were seven, and `Progress` is not one of them.** The inventory counted `withProgress` as a
class, and the plan round was right that it cannot be: `withProgress` is a **different API**, the
funnel wraps `show*Message`, and S1's counter counts `show*Message` — so a Progress "class" could
never receive a record while the structural test went on reporting complete coverage. A taxonomy with
an unreachable member, in a plan whose whole thesis is completeness. Operator ruling, 2026-09-16:
**removed.** The 7 `withProgress` sites are **out of scope**, stated here rather than silently
uncovered; their failures already arrive as Refusal or Failure afterwards, which is where a person
would look for them anyway.

Three surfaces already exist and must not be duplicated by the log: the escalation **status-bar item**
(`escalationWatcher.ts:65`), the transient **status-bar line** (`copyText.ts`), and the chat page's
in-page **refusal line** (`chatPage.ts:1854`, `data.refusal` — the product's only true banner is
`save-failed` in `phrasesPage.ts:183-189`, and this is not it). Some chat refusals therefore already
appear twice; S1's counter names which, rather than this plan asserting a number it cannot derive.

### Two classes that do not exist at all

- **Vendor rate limits, on the EXTENSION side only.**
  [remoteChatSession.ts:280](../src_vs_code/src/remoteChatSession.ts) handles HTTP 429 by backing off
  in a loop: a shared account holding a turn for minutes produces no toast, no console line and no
  record. **The server's half is already covered and must not be re-plumbed**: the ladder reports each
  wait (`BoundedScheduler.cs:431-434` — *"Said out loud, because the ladder can hold a reviewer for
  minutes and 'running' reads exactly like a model that is thinking"*), it reaches `LiveRound.Report`
  and is persisted into the session file. The first draft claimed "nothing escapes it", which is
  refuted by that code and contradicted this plan's own server table.
- **Reviewer failures.** They live in the server's database and session files; the extension notifies
  nothing about them.

Neither becomes a new class: by what the person must do they are **Failures**, distinguished by
`source`.

### The invisible half

**85** bare `catch {}` sites (96 if the swallowing `.catch(() => …)` form is counted), and at most
**103** console calls in total — of which the console-*only* sites are a subset. Most are honest
"nothing yet" guards. These are not:

| Where | What is lost |
|---|---|
| `serverSettingsSync.ts:173` | the server settings file could not be **written**; nobody is told |
| `serverSettingsSync.ts:180` | the critical section itself failed |
| `serverSettingsSync.ts:225` | `readExisting()` threw → **stands the write down**, with no version to report and therefore no notification at all |
| `serverSettingsSync.ts:229` | the file is `unreadable` → stands down silently |
| `extension.ts:544` | `sync()` returned `'busy'`; one retry, and if that is busy too the configuration change is **dropped** |
| `teamServerAuth.ts:138` | `chmod 0700` on the token directory failed — a security-relevant failure nobody hears about |
| `escalationWatcher.ts:120` | a directory could not be watched (WSL down, NAS unmounted) → silently poll-only |
| `chatStoreLock.ts:218/252` | a lock older than **30 s** (`LOCK_STALE_MS`, `:78`) was **broken**; a mutation **outlived its lock** |

The first **four** are the same module the Role2 incident ran through — only one of its five
stand-down paths speaks. The fifth, `extension.ts:544`, is neither a bare `catch` nor a console line:
it is a dropped retry, which is worse.

### The server: everything, and almost none of it persisted

| Class | Persisted where | Reaches the panel? |
|---|---|---|
| Refusal to the calling AI (`{"error": "…"}`) | nowhere — **43 call sites** across the two `Error` helpers (25 in `PanelService`, 18 in `ConsultationService`); only 5 of them also write a log line | **no** |
| Reviewer failure / timeout / cancel | Serilog · `reviewers.note` · session JSON · `usage.jsonl` | yes, via the session file |
| Unparseable / empty output | evidence file under `unparseable/` or `empty/` | sentence only |
| `Unrecognised` (unreadable settings, dropped roles) | Serilog, once at startup | **on the wire, dropped by the parser** |
| `StorageNotes` (data-dir problems) | Serilog, once at startup | **no** |
| Team-server 401/403, rate limits | folded into a reviewer note | sentence only |
| Unhandled exception in a round | Serilog (Error + stack) | sentence to the AI only |
| **The process crashing** | **nowhere** | **no** |
| Git / worktree failure | no | **no** |

There is **no person-facing "notice" type on the server to extend**. The closest is
`PanelSettings.Unrecognised` — a list of plain English sentences — and it is the shape to generalise
from: a sentence written to be acted on, never a code.

### Three things already paid for and thrown away

1. `Unrecognised` and `VaultNote` are serialised into the `--providers` answer
   (`ProvidersAnswer`, `ServerJsonContext.cs:16-29` — `VaultNote` at `:19`, `Unrecognised` at `:29`)
   and the extension's parser reads only `{provider, auth, note}` and drops the rest
   ([providers.ts:87-99](../src_vs_code/src/providers.ts)).
2. `reviewers.note` is written into `coai.db` (`RoundsDb.RecordReviewers`) and **no query reads it
   back**.
3. `StorageNotes` is computed and logged and reaches no wire at all.

Surfacing (1) is about three lines of TypeScript. **It would not have caught Role2** — that role was
accepted, not dropped, and its complaint was a round-time skip — but it is the cheapest honest
improvement in the inventory.

## Decisions taken (operator, 2026-09-16)

| Question | Answer |
|---|---|
| What is a "type"? | **The classes above**, by what the person must do — seven as chosen, six once `Progress` was removed for being unreachable. Not severity: 68 of 109 sites are `warning`, and that bucket mixes refusals, failures and modal questions. |
| What does the counter count? | **Unread since the page was last opened.** An all-time total stops meaning anything within a week. |
| Which half first? | **Both, server in its own phase and its own file.** |
| Which defects are fixed here? | **All four.** |
| Is a climbing repeat count allowed to stay silent? | **No** — silence there is camouflage for a loop. See *E. Suppression and storms*. |
| Is the rounds log brought into line? | **Yes**, S6, two named changes only. |

Taken without asking, and open to correction:

- **English only**, as every toast is today.
- **The two sides are not merged.** Each side's data directory keeps its own ledgers, and the page
  **names the directory it is reading** — a log that does not say whose it is would repeat the
  2026-09-16 confusion exactly.
- **No new one-shot CLI mode**, and none is needed: the server writes a file, it does not answer a query.
- **Recording is additive.** No message stops being a toast.

## What this builds

### A. One funnel — `notify.ts`

A module that **records, then shows**. Every event site goes through it.

```ts
const chosen = await notify({
  class: 'stand-down',
  source: 'serverSettingsSync',
  code: 'settings-stood-down',        // a STRING LITERAL — the key, never prose
  title: 'The server settings were left alone',
  detail: 'They were written by version 0.47.0, newer than this window's 0.46.0.',
  cure: 'Reload the window (Developer: Reload Window).',
  action: 'Reload Window',
});
```

Six obligations, each of which a review found missing from a draft before this one:

1. **Two entry points, because awaiting a toast inside a lock is a deadlock.** `notify()` awaits the
   **append** and returns — the toast is fired and never awaited. `notifyAndAsk()` awaits the person's
   choice and returns it, and is called **only from outside any critical section**.

   This is not a nicety. `show*Message` with an action button does not resolve until the person
   clicks, and the second draft wrote `const chosen = await notify(...)` — so a stand-down raised
   inside `serverSettingsSync`'s critical section (`:180`) would hold that section for as long as the
   toast sat on screen, every later `sync()` would answer `'busy'`, and the single dropped retry of
   defect 1 would fire. **The mechanism built against the Role2 incident would have reproduced the
   Role2 incident.** Only the eight Offer sites, which exist for their button, take `notifyAndAsk`.
2. **The append is awaited for EVERY class, never only for three.** The second draft narrowed the
   guarantee to `failure`/`stand-down`/`storm`; the plan round was right that this makes "records,
   then shows" false for the rest — an information toast shown while its append is still queued, on a
   host killed a moment later, is a message the person saw and the ledger never had. An append is
   microseconds; there is nothing to buy by skipping it. What is never awaited is the **toast**.
3. **`code` is the key, and it is a string literal.** Variables live in `title`/`detail`. This bounds
   the key space to the number of call sites, which is what bounds the maps in *E*.
4. **It never throws into its caller** — `appendLine` guarantees it
   ([jsonlLedger.ts:37](../src_vs_code/src/jsonlLedger.ts)).
5. **The ledger must tell the funnel when a write failed**, which today it cannot: `appendLine`
   catches internally ([jsonlLedger.ts:44-48](../src_vs_code/src/jsonlLedger.ts)) and hands the caller
   a resolved promise, so a funnel built on it as it stands would increment nothing and the gap
   counter of obligation 6 would always read zero. S1 widens it to report the outcome — still never
   throwing — and the chat ledgers keep ignoring the answer exactly as they do now.
6. **It never notifies about its own failure**, and it does not stay quiet about it either. A write
   failure keeps its `console.error` *and* increments a counter the panel section and the page render
   as **"N records could not be written since HH:MM"**, and emits one gap record on the next
   successful append. A notification about a failed notification is an infinite regress; a **state**
   is not. On a full disk the first draft's design produced a tidy, complete-looking history missing
   exactly the period the machine was in trouble — the failure this plan exists to end, committed by
   this plan.

The funnel is enforced by a structural test, not by discipline (see *Test plan*).

### B. Two ledgers, merged at read time

| File | Written by | Read by |
|---|---|---|
| `notifications.jsonl` | the extension | the extension |
| `server-notices.jsonl` | `coai-mcp` (S8) | the extension |

Beside each other in the same data directory, merged in memory the way `mergedRows` already merges
`usage.jsonl` and `chat-usage.jsonl`. **This is the repository's answer, twice given**: two programs
that ship on different days must not share a format. It costs one extra read and lets either half move
alone — which matters here more than anywhere, because this machine's halves demonstrably ship out of
step.

Both are built on the house primitive: `appendLine` / `readLedger`
([jsonlLedger.ts](../src_vs_code/src/jsonlLedger.ts)). The multi-process safety of the **append** is
measured — `scripts/measure-append.mjs`, `npm run measure:append`, 8 processes × 1000 records
including 60 KB lines, zero torn lines (2026-09-09, Windows 11, NTFS, node 22).

**The conditions were part of the claim, and two of them did not hold — so S1 measured them.** The
harness called `appendFileSync` while the production writer is the promise-based `appendFile`: the
same `'a'` mode, a different function, and a plan may not treat a measurement of one as a measurement
of the other. And the result was about a local NTFS disk, while the data directory is relocatable and
has been a NAS share on this machine, where SMB does not guarantee atomic append in general.

**Both measured, 2026-09-16** (`npm run measure:append`, results and conditions in the harness header
and in `jsonlLedger.ts`):

| writer | shape | where | result |
|---|---|---|---|
| `appendFile` (production) | 8 × 1000, 116 MB | local NTFS | 8000 of 8000, **0 torn** |
| `appendFile` (production) | 4 × 200, 11.6 MB | SMB share | 800 of 800, **0 torn** |

So the append holds for the call this code actually makes, and it holds over this machine's NAS. The
sanctioned escape if it ever tears elsewhere is unchanged and unused: one ledger per process
(`notifications-<pid>.jsonl`, the `chatOrphans.ts` precedent) with a glob in the reader. `--dir=` is
how the next person asks the question of their own filesystem.

**Append-only, absolutely.** One record per written occurrence. Nothing is ever updated, truncated or
rewritten. That single rule is what makes byte-offset reads exact (C), what keeps the measured
guarantee applicable, and what removes every lock this design would otherwise need.

The record — `utc`, `class`, `source`, `code` required, everything else optional so a later field is
additive (the `vendor?` precedent):

```jsonc
{ "utc": "2026-09-16T17:05:39.812Z", "class": "stand-down", "source": "serverSettingsSync",
  "code": "settings-stood-down",
  "title": "The server settings were left alone",
  "detail": "They were written by version 0.47.0, newer than this window's 0.46.0.",
  "cure": "Reload the window (Developer: Reload Window).",
  "action": "Reload Window", "pid": 37308, "run": "a1c9…", "subject": "", "seq": 10,
  "repo": "…", "branch": "…", "session": "…", "provider": "…", "role": "…" }
```

- `run` is an id minted once per host start; `pid`s are reused, so a pid cannot identify a process
  lifetime and two unrelated incidents would merge into one row.
- `subject` is the resource this occurrence is about, empty where a `code` can only ever be about one
  thing (see *E*).
- `seq` is **this occurrence's ordinal for `(code, subject)` within this run — diagnostic only.**
  Because v1 records every occurrence as its own row, **counting is counting rows**, and nothing a
  reader shows is derived from `seq`. That matters: any in-memory counter can be evicted or lost, and
  a count computed from one would then be wrong on screen. A count computed from rows cannot be.
- **Times are UTC**, ISO-8601, from an injected clock — never `new Date()` inside the module, per
  `utc-timestamps.md` and the `chatDoors.ts:90` precedent. Local time exists only in the page.
- **File order is not time order.** Interleaved writers and per-process clocks mean the last line is
  not the newest record. Nothing may treat position as age.
- **Redaction happens at the line serialiser, over every string field, iterated from the type** —
  not on the one field somebody remembered. `security.md` names "a measure applied at SOME of its
  sites" as the defect this family keeps repeating. It reuses `CREDENTIAL_WORDS`
  (`consultantWrite.ts:233`), this repository's already-reviewed credential word list, and
  `displayable()` (`serverSettingsSync.ts:268`) for making foreign text fit. Patterns are bounded and
  non-backtracking; `title` is capped at ~200 chars and `detail` at ~4 KB with an explicit
  `…(truncated)`, because `detail` can carry an HTTP body or a megabyte stack and a regex over that is
  a hung extension host.
- **Both files join `DATA_TO_MOVE`** ([dataDir.ts:450](../src_vs_code/src/dataDir.ts)) and
  `shared/data-inventory.json`; a test already enforces the pair. Records written *during* a
  data-directory move strand in the old folder — pre-existing for `chat-usage.jsonl`, likelier here,
  and stated rather than discovered.

### C. The unread watermark is an APPEND-ONLY acknowledgement

`notifications-seen.jsonl` — beside the ledgers in the same data directory, and in `DATA_TO_MOVE` with
them: one line per "this window read through offset X of ledger Y", and the effective watermark is a
**map keyed by ledger**, `watermark[ledger] = max(offsets for that ledger)`. Not one scalar across
both: `notifications.jsonl` is the busier file by far, and a single maximum would apply its 5000-byte
offset to a `server-notices.jsonl` that is 300 bytes long, permanently swallowing every server notice
below it. Not a file that is rewritten, either.

The first draft had it as a whole-file write, then as a temp+rename component-wise max. Both lose
updates, and the second one is the subtler mistake: **temp+rename is atomic but it is not mutual
exclusion.** Two windows read the old offsets, each computes its own maximum, and they rename in
reverse order — the later rename wins with the smaller value, the watermark moves backwards, and
records somebody has already read come back as unread. A component-wise max only helps if the
read-compute-write is serialised, and there is no lock here worth taking.

An append-only acknowledgement has no read-modify-write in it at all, so there is nothing to lose: two
windows append two lines and the maximum is the maximum. It is the same rule as the ledgers
themselves, which is the point — one storage discipline in this design, not two.

An offset, not a timestamp, because a timestamp is not safe here: `utc` comes from each writer's own
clock, and a WSL distro after a resume or a server started before an NTP step writes a record
*earlier* than a watermark another process already set — a record that is then **never counted, ever**,
while sitting visibly on the page. An offset on an append-only file is monotone by construction.

It is written **only after a complete snapshot has been read and rendered**, never on open, so a
transient read error cannot mark everything read. It advances **only through the last complete
newline**, because a snapshot can end halfway through a line another process is still writing.

**It acknowledges only the range actually loaded.** The page shows the newest N, not the file — so
acknowledging the ledger's *end* offset would claim the person read 5000 records when 3000 were
rendered, and the 2000 they never saw would leave the unread count without ever appearing. The
acknowledgement carries the **start and end offsets of the contiguous range that was rendered**, and
the unread count is what lies outside every acknowledged range. A person who wants the older ones
counted as read scrolls to them.

**A missing file is not an error, and a corrupt one is not zero.** No file means a first run: no
acknowledgement, everything unread, which is correct. An unparseable line is dropped and the previous
maximum stands, which errs toward "unread" — the safe direction. A file that exists and cannot be read
at all shows **"not read yet"** (the Bugz precedent), never 0 and never everything.

One machine, one person: a window that opens the page clears the count for every window, and that is a
stated decision, not an accident.

### D. The panel section is a LIVE REGION

A fourth region beside `questions`, `rounds` and `consultations`
(`liveRegions`, `panelView.ts:2215-2224`), patched by innerHTML comparison — **not** an entry in
`staticKey`. The first draft had this the other way round with the live region as a contingency; the
reviews showed it is both safer and *less* code.

Why it matters: `EscalationWatcher` polls every 5000 ms and calls `onChanged` **unconditionally**
(`escalationWatcher.ts:90`, `:149`), so every window renders every 5 seconds regardless. Putting a
moving count into `staticKey` therefore means a full `webview.html` reassignment — and under a
repeating fault, which is the case this feature exists for, that is a full rebuild every 5 seconds in
every window. `withholdsRepaint` does not save it: it is capped absolutely at 30 s
(`panelView.ts:263`, `:274`), it only tracks `[data-setting]` elements so a person **dragging to
select an error message out of this very section** holds no focus at all, and a rebuild drops the
sidebar scroll position — which `panelView.ts:541` already says in as many words.

The section itself: inserted after the *Active rounds* line at
[panelView.ts:368](../src_vs_code/src/panelView.ts), helper at
[panelView.ts:767](../src_vs_code/src/panelView.ts), **id `notifications`, lowercase** because
`panelView.test.ts:415-424` scans `/data-section="([a-z]+)" open/`. Counts and one button; the counts
line follows the Bugz precedent (`bugzView.ts:71-97`), including a distinct sentence for "nothing yet"
and for "not read yet". The button is `data-command="showNotifications"`, declared in `PANEL_COMMANDS`
([panelView.ts:2612](../src_vs_code/src/panelView.ts)) and mapped in `VSCODE_COMMAND_FOR` — a declared
command with no case is a compile error, which is the point.

The count refresh is **coalesced and never invoked synchronously from `notify`'s own stack**:
`panelProvider` already raises a warning from the render path, and render → notify → render is a loop.

### E. Suppression and storms — keyed on state, not on a clock

The problem is real: 109 sites, and one repeating fault can write a million lines. The first draft
bounded it with a 60-second window, which is the wrong axis. The mechanism already in this repository
keys on **the situation**, not the clock — `serverSettingsSync.ts:247` reports once per version and
`:171` **clears that** on a successful write, *"the situation is over; a stand-down after this is
news, not a repeat."*

So, per `(code, subject)`, per run:

1. **Every occurrence is recorded, exactly, up to a ceiling of 1000 per run.** No sampling in v1. The
   first draft's decade back-off (write the 1st, 10th, 100th…) was rejected on consultation for a
   reason that is easy to check and impossible to work around: **10 occurrences and 99 occurrences
   produce an identical ledger.** A sampled ledger can only ever answer "at least N", and nothing in
   the design said so — it claimed a reader could "see exactly how far it went", which is false. At the
   ceiling one record says *"further repeats of this suppressed"* and writing stops until recovery.
   Exact below the bound, explicitly truncated above it, never ambiguous in between. 1000 records is
   400 KB, which is the whole cost of not lying.
2. **Sampling is a measured decision, not a guess.** S1's counter measures the real rate; if 1000 per
   run turns out to be reached in ordinary use, a back-off is added **then**, and when it is, the
   record carries a lower-bound marker and readers take the **maximum** ordinal of a run, never the sum
   of its milestones — summing `seq: 1, 10` gives 11 for what was 10.
3. **A run is identified, because a `pid` is not.** Pids are reused, so each host mints a `run` id at
   activation and the record carries it. Without it, two unrelated incidents months apart group into
   one row and nothing can tell them apart.
   **The run id lives in memory and a restart mints a new one, so the ceiling is per run and resets on
   restart — by design, and stated because two reviewers asked.** A fault that survives a restart
   begins a fresh run of records, which is the honest reading: a new host is a new observer, and a
   ceiling that persisted across restarts would need durable state whose loss is itself a defect. The
   cost is bounded and visible — one more run's worth of records for a fault nobody fixed — and the
   page shows the runs separately, so it reads as "it happened again after a restart" rather than as
   one number silently growing.
4. **The budget protects the disk, never the point.** The run-wide bound of *The growth budget* applies
   **only to repeating records** — second and later occurrences of a `(code, subject)` already seen in
   this run. A first occurrence is never suppressed by it, and `failure` and `stand-down` records keep a
   reserved allowance of their own. Otherwise a churn loop across subjects spends the budget and the
   settings refusal that arrives afterwards is dropped — the log protecting storage at the cost of the
   one record it existed for. Operator ruling, 2026-09-16.
4. **The key carries a subject, because one call site can fail for two things at once.** A `code` alone
   is the call site; two simultaneous failures from it — two Team servers, two prompt files — must not
   share a counter, and recovering one must not reset the other. `subject` is the resource, absent
   where there is only one.
5. **Recovery resets.** A caller that knows its condition ended says so (`notifyResolved(code, subject)`);
   the next occurrence is a **new** first occurrence and is written. That is what makes "it came back"
   visible, and what a clock window destroys.
6. **A storm record at 100 and at the ceiling** — class `storm`, its own tab, written by whichever
   process crosses it. It names what is repeating, since when, the rate, and the cure. Its own `title`
   carries **no number**, or it would mint a fresh key per occurrence and be uncollapsible itself; the
   rate lives in `detail`.

**The once-only bound is a READ-time invariant, and the DoD says so.** N windows each cross the
threshold independently, so the file may hold up to N storm records per `(code, subject)` per
threshold; there is no atomic test-and-set on an append-only file, and the repository's own lock is
listed in this plan as a defect source. The page and the section **group by `(code, subject, threshold)`
and show one row.** The first draft's "exactly one, never a third" could only have passed in a
single-process fixture.

**Counting happens at read time, by counting rows** — group by **`(run, code, subject)`** first, then
present the runs together under one `(code, subject)` row. Never from `seq`, and never from an
in-memory counter: a map can be evicted and a process can die, and a number on screen that a reader
cannot re-derive from the file is a number that will be wrong one day.

Grouping by run before totalling is what keeps the number honest when several windows hit the same
fault at once. Three windows × 1000 records is not "3000 repeats" of one incident and must not read as
one; it is three runs that each hit their ceiling, and the row says so — **"1000+ in each of 3 runs"**,
never a bare 3000, and never a bare 1000 either. A run that hit its ceiling is shown as "1000+", never
as a number it is not. `ratePerMin` is **derived, never
stored**, and is **absent** (`—`) for a single occurrence or a zero-length window — `coding-style.md`:
a measurement that could not be taken must not render as `0`, and blanks sort last in the comparator
both ways. Otherwise every singleton would sort to the top of the one column the storm feature exists
for.

**The in-memory state** — the per-`(code, subject)` counter — is bounded by an LRU of 512 entries,
because `code` is finite (a string literal per call site) and `subject` is not. Losing it on a crash,
or to eviction, costs at most one extra "first occurrence" line. It is **never** the source of truth
for anything a person reads, which is why eviction cannot corrupt a count — and why the ceiling it
enforces needs the run-wide budget beside it, since eviction would otherwise reset it.

### F. The page

`coai.showNotifications` → `notificationsPage.ts` (pure) + `notificationsPanel.ts` (host).

- **A tab per class**, on the roles-page pattern: the markup at
  [rolesPage.ts:407](../src_vs_code/src/rolesPage.ts) and `:440-457` (`role="tablist"`/`tab`/
  `tabpanel`, `aria-controls`, `aria-selected`), the handler at `:555-575`, with the hidden sections
  **derived from `[data-section]`** rather than named literally.
- **Columns**: When · Class · Source · What happened · What to do · Repeats · Rate · Where. Sorting on
  every data column, blanks last both ways, sticky header — the `COLUMNS` shape at
  [roundsLog.ts:879](../src_vs_code/src/roundsLog.ts).
- **Pagination at 200**, the same 200: `PAGE_SIZE` at
  [roundsLog.ts:1100](../src_vs_code/src/roundsLog.ts) carries the operator's ruling in its docstring.
  Filter → sort → slice in that order ([roundsLog.ts:1473-1479](../src_vs_code/src/roundsLog.ts)), and
  every filter, search or sort change returns to page 1.
- **Filters**: search, a source facet derived from the rows, and a `datetime-local` range. The search
  haystack **includes the message text** — a log whose search cannot find the sentence on screen is
  the trap `roundsLog.ts:820` set once already.
- **The page loads the newest N (a few thousand), not the file — and does not read the file to find
  them.** "Load the newest N" is not a bound if getting them means parsing everything first. The
  reader seeks to the end and walks **backwards in byte windows** (64 KB at a time), keeping complete
  lines, until it has N or reaches the start; append-only is what makes that exact, and it is the
  third thing the no-rewrite rule buys. An explicit line says older records are in the file, and the
  acknowledgement covers only what was loaded (see *C*).
- **It names the data directory it is reading.**
- Reuse (`reuse-first.md` step 2): `PAGE_SIZE` and `compareRows` are **extracted** into a shared
  module. Two mechanics to get right — `compareRows` ([roundsLog.ts:679](../src_vs_code/src/roundsLog.ts))
  is typed on `LogRow`/`SortKey` and must be genericised; and `PAGE_SIZE` is interpolated into the page
  **script text** (`roundsLog.ts:1331`), so the shared module must survive that `.toString()`-into-script
  path — the minifier hazard this repository has shipped broken twice. `roundsLog.ts` is the only
  module that paginates or sorts, so the enumeration is complete.
- The UTC→local range conversion goes into the **same** extraction: two pages that must agree about
  "is this instant in range" is exactly `utc-timestamps.md`'s one-place-per-comparison rule.
- `zoomControl.ts`, and `jsonForScript` + the apostrophe-escaping `escapeHtml` (`webviewHtml.ts:113`,
  `:163`), are taken as they are. `zoomControl` is its own module, not part of `webviewHtml.ts`.
- **File budget**: `notificationsPage.ts` stays under 400 lines by splitting rows, filters and the tab
  strip into their own units. It deliberately does **not** inherit `roundsLog.ts`'s shape, which is
  far past `coding-style.md`'s 800-line ceiling. Whether *that* file should be split is a question for
  the operator, asked in *Open questions* rather than answered by imitation.

### G. The server half (S8, its own file)

`coai-mcp` appends to `server-notices.jsonl` with the same record shape. Three functions are the whole
instrumentation surface:

| Site | Covers |
|---|---|
| `PanelService.Error` ([PanelService.cs:2629](../src_mcp/src/Server/PanelService.cs)) **and `ConsultationService.Error` (`Consultation/ConsultationService.cs:728`)** | every refusal returned to the calling AI. **Two edits, not one**: there are two byte-identical private copies of this helper, and the consultation one has 18 call sites of its own, so instrumenting only the first would leave every consultation refusal out of the ledger while the plan claimed full coverage |
| `ReviewerSummaryFactory.Describe` ([BoundedScheduler.cs:489](../src_mcp/runners/Reviewers/BoundedScheduler.cs)) + `LiveRound.Report` ([LiveRound.cs:65](../src_mcp/src/Server/LiveRound.cs)) | reviewer failures, timeouts, rate limits, unparseable output |
| `PanelService`'s constructor sweeps (`:88-130`) + startup | `Unrecognised`, `StorageNotes`, swept orphans, killed children |

Writes are **best-effort**, through the decision `Projection.Write` already embodies: a notice must
never take a round down. The .NET append is **not the system call the harness measured**, so S8
re-runs `measure:append` with the .NET writer as one of the processes before it ships — or, since the
files are separate, simply accepts that each file has one writer per process family.

**The .NET writer redacts too, and that is a security requirement rather than a symmetry.** The
redaction and the length caps of *B* are described around the TypeScript serialiser; "the same record
shape" does not carry them. A server refusal is exactly where a bearer token, a signed URL or an HTTP
body arrives — 43 call sites of it — so S8 ships a .NET serialiser applying the **same all-string-field
redaction and the same caps**, and the seam vectors include secret-bearing records written through
**both** writers with an assertion on the persisted bytes. A measure applied to one of two writers is
`security.md`'s named defect, and this is the writer nearer the secrets.

**Its growth is bounded before its first write, not measured after it.** `PanelService.Error` and
`ConsultationService.Error` cover every refusal to the calling AI, and an automated caller can produce
them steadily — so "measure it for a week" is not a budget. Conservative calculation at this
installation's stated volumes: a round asks ≤ 15 reviewers, a refusal per reviewer is the pathological
case, and at 20 rounds a day that is ≤ 300 records/day ≈ 44 MB/year at 400 B — the same order as the
extension's, with the same "kept for ever" decision and the same roll-up as the only sanctioned bound.
The week of measurement then **revises** a number that already exists.

**Which resolver the server uses for the path must be named, not assumed.**
`SettingsFile.DataDirFrom` (`src_mcp/src/Server/SettingsFile.cs:74-75`) is a bare `COAI_DATA_DIR` read
with no side and no trim, while `PanelSettings.ResolveDataDir` (`PanelSettings.cs:513`) applies both —
so on a side-partitioned install the two halves would disagree about where the file is and neither
would say so. S8 uses `ResolveDataDir`, pinned by `shared/data-side-vectors.json`, and is ordered
after [PLAN_the_settings_file_ignores_the_side.md](PLAN_the_settings_file_ignores_the_side.md).

### H. The gap the log cannot see by itself

Three holes, all in shutdown, all closed here because a log that loses records around a crash is a log
that lies in exactly the situation it exists for:

- **`flushLedgers` is a snapshot, not a drain.** [jsonlLedger.ts:62](../src_vs_code/src/jsonlLedger.ts)
  returns the *current* `queue`, and every append reassigns it. Anything raised after that line in
  `deactivate` is never awaited. Four lines: `let seen; do { seen = queue; await seen; } while (queue !== seen);`
- **Notifications get their own append chain.** One shared chain (`jsonlLedger.ts:27`) is justified on
  "an append is microseconds"; on a NAS it is tens of milliseconds, and a queued storm would delay a
  chat-turn record behind it and make `deactivate` wait for the backlog — after which VS Code kills the
  host and the tail is lost anyway. `flushLedgers` awaits both chains.
- **A killed process leaves no record at all.** SIGKILL, OOM, power loss: no `catch`, no `deactivate`.
  Both halves write a **run-start marker** and clear it on clean exit; the next start finds a stale
  marker and appends an unclean-exit record. The server already has the machinery — `running/{pid}.json`
  and `ProcessTracking.Sweep` — and this is the same shape, including the part that matters most:
  **one marker per run, never a singleton.** Two windows share one data directory; a singleton marker
  cleared by window A's clean exit erases the evidence that window B was killed, and the crash the
  whole mechanism exists to record is the one that disappears. Each marker is named for its `run`, is
  cleared only by its own owner and only **after** that run's ledgers have drained, and startup sweeps
  **every** stale marker it finds rather than the first.
- **The drain has a ceiling.** `deactivate` cannot wait forever — VS Code kills a host that does — so
  the drain-to-quiescence loop is bounded, and on expiry it records how many records it could not
  flush rather than blocking the window from closing.

## The number is derived

The completeness promise may not rest on a typed number. S1 adds `scripts/count-notifications.mjs`,
which enumerates the call sites and their classes mechanically and prints one table; the plan, the
`todo/README.md` row and the DoD quote **that** number, and a test fails when the count changes
without the table being regenerated. Test 1's scan exempts `src/test/**` **by construction** — two
`showWarningMessage` occurrences live in `src/test/chatGotoWiring.test.ts` (`:203`, `:218`) inside
structural assertions, and a scan that reports the tests enforcing the same discipline is a scan
nobody will keep.

## The growth budget

Required before the first write, per `planning-docs.md`. Every surface, including the ones the first
draft missed.

| Surface | Projected | Who retires it | Interrupted |
|---|---|---|---|
| `notifications.jsonl` | ~400 B/record. The rate is **not measured** — see *Open questions*; at a hand-estimated 20–50/day that is 3–7 MB/year | **Nobody: kept for ever. This is a NEW decision for this file, taken here.** The 2026-09-10 ruling on `chat-usage.jsonl` is the precedent for the *shape* — keep rather than trim, and its docstring refuses trimming by count by name — but a ruling about spending history does not settle retention for notifications, and citing it as if it did would be the third time this plan leant on a record for something it does not cover. **If a bound is ever wanted it is a roll-up**, and the record shape already carries what one needs | Nothing to interrupt: append-only, no rewrite, no compaction |
| `server-notices.jsonl` (S8) | `PanelService.Error` covers every refusal to the calling AI, so its rate is **not the extension's**; S8 measures it over a week before any sampling is considered for it | same | same |
| `notifications-seen.jsonl` | one ~80 B line per page-open; a few hundred a year | kept; compactable at any time by taking the maximum, since it is append-only like everything else | nothing to interrupt — a torn last line is dropped and the previous maximum stands, which errs toward "unread" |
| Per-`(code, subject)` counters (memory) | `code` is finite — a string literal per call site — but **`subject` is not**: a path or a server name is unbounded, so the map is an **LRU capped at 512 entries**. Eviction is safe for *counts* (those are counted from rows, never from this map) but **not** for the ceiling — see the row below | process exit | nothing a reader sees is derived from it |
| `storm` records | at most 2 per `(code, subject)` per run | — | — |
| Records per repeating fault | **Two bounds, because one is evictable.** A per-`(code, subject)` ceiling of 1000 per run, *and* a **run-wide budget of 5000 REPEATING records** that no eviction can reset — otherwise a loop churning 512 distinct subjects evicts its own counter and writes for ever. At either bound one record says which was hit, and writing of that kind stops until recovery | — | resets per run, by design (see *E*) |
| Reserved capacity | The run-wide budget counts **only repeats** — second and later occurrences of a `(code, subject)` already seen this run. **A first occurrence is never suppressed**, and `failure` and `stand-down` keep a reserved allowance besides. A budget that could starve the settings refusal arriving after a churn loop would protect the disk at the cost of the record the log exists for | — | — |
| Run markers | one small file per run, both halves | its own run, after its ledgers drain; startup sweeps **every** stale one | a stale marker IS the record — that is its job |
| The page's row set | newest N only, never the file | — | — |
| The defect-1 retry's record rate | bounded by reset-on-recovery + back-off, **not** one record per 5 s tick | — | — |

**Nothing is read on the activation path.** `activate` is synchronous and returns `void`
([extension.ts:67](../src_vs_code/src/extension.ts)), and no JSONL ledger is read there today — both
existing readers are lazy behind a `size:mtimeMs` cache (`panelProvider.ts:507-557`). The counts are
computed on first render behind that same stamped cache, and the ledger module gets the cache (today
it lives in `panelProvider`, so a new ledger would inherit none of it) plus an incremental
tail-from-offset read, which append-only makes exact.

## The four defects

Each starts with a failing test that reproduces the symptom, per `testing.md` — the order is fixed,
never fix-then-test.

### 1. The stand-down is silent and does not heal itself

The Role2 incident. Five stand-down paths, one of which speaks; a `'busy'` outcome retried once and
then dropped.

- Every outcome of `sync()` other than `written`/`unchanged` becomes a record with its cure —
  including the four silent `catch` branches — **once per condition, reset on recovery**, reusing the
  existing `reportedFor` semantics rather than inventing a second suppressor.
- **The pending mirror is persisted, retried with back-off, and bounded.** A flag in memory dies with
  the window, which is the incident again. It is written beside the settings file and derived on load;
  the retry has a back-off and a ceiling; on exhaustion it reports and stops rather than ticking
  forever.
- RED test: a sync whose existing file names a newer build produces one record naming the version and
  the cure, not one per tick; a second sync after the build catches up writes the file and clears the
  condition.

### 2. Deleting a role deletes its text before the deletion has landed

[rolesPanel.ts:320-321](../src_vs_code/src/rolesPanel.ts) writes the rows and **then** deletes the
prompt files. When the row write does not reach the server the result is a role that exists with a
prompt that has no text, complaining once per round for ever
([PanelService.cs:1876](../src_mcp/src/Server/PanelService.cs)).

- The prompt files are deleted **only after the mirror reports `written` or `unchanged`**. On a
  stand-down the text stays and a record says why.
- Deletion becomes a **resumable cleanup with a tombstone**, and **the order is the whole of it**: the
  tombstone is written and durable **before** the row is touched. Written after, it buys nothing — a
  process that dies between the row write and the tombstone leaves the next start with no evidence
  that anything is half-done, the prompt files orphaned and the id free again, which is the state this
  defect is about. Every step is **idempotent**, startup **sweeps** tombstones and finishes them, and
  the RED tests kill the process between each pair of steps.
- The unit is the row, the prompt files and the four role-keyed settings (`coai.rounds`,
  `coai.thresholds`, `coai.roleEnabled`, `coai.promptsPerRound`). **The id is not reusable until the
  tombstone clears** — `rolesEdit.ts:78` generates from the current rows only, so today the next role
  called `Role2` inherits a stranger's budget.
- **A tombstone that cannot clear must not become a life sentence.** In this plan's own scenario the
  mirror stays stood down until somebody reloads a window, so the tombstone can outlive every reload
  and bar the person from ever recreating a role of that name. The roles page therefore **shows**
  stranded tombstones with the reason, and offers *Finish the deletion anyway* — which completes the
  local cleanup and releases the id, saying plainly that the server may still carry the row until its
  settings catch up.
- The per-role `RoleGate` inside session files is left alone: sessions are history, `PanelConfig.Catalog`
  is `[JsonIgnore]` and the live catalog is reattached on load. **Verified on the incident's own data** —
  the gate for `Role2` is still there and is not what kept it running.
- RED test: a delete whose setting write is refused leaves the prompt file on disk; a delete that lands
  prunes all four records; an interrupted delete resumes.

### 3. `helpPanel.ts:29` bypasses the settings-refusal machinery

[helpPanel.ts:29](../src_vs_code/src/helpPanel.ts) shows `'That setting could not be saved: ' +
String(reason)`. Every other settings-write caller routes through `reportRefusal`
(`sideConfig.ts:175`), which diagnoses a stale window and offers **Reload Window**. The one message
whose cure is a single click arrives from the Help tab as a raw errno.

RED test: a refused write from the Help tab produces the same diagnosed sentence and the same
**Reload Window** action as every other settings caller — red today, because it produces neither.

### 4. `ServeAsync` catches two exception types, and the process dies unlogged for every other

[Program.cs:1307](../src_mcp/src/Program.cs) is `catch (Exception e) when (e is IOException or
ObjectDisposedException)`. Anything else leaves `Main` and kills the process. Verified: **`Main`
(`Program.cs:182-243`) has no `try`, `catch` or `finally` at all, and `Log.CloseAndFlush` appears 0
times in the entire `src_mcp` tree** — which `logging-serilog.md` requires in as many words
(*"Failures during startup must still be logged"*). The class of event "coai crashed" is recorded
nowhere.

Wrap the host, log the stack, flush, exit non-zero. **And the half a catch cannot reach**: an
externally killed process runs no handler, so the run-start marker of *H* is what records it, on the
next start.

RED test: an exception of a third type leaving the host is logged, flushed and exits non-zero —
asserted on the wrapper; and a stale run marker produces an unclean-exit record on the next start.

## Who builds what

A boundary named on one side is not a boundary (`planning-docs.md`, MANDATORY). The reciprocal line
goes into each plan below in the same task.

| Item | This plan | The sibling |
|---|---|---|
| `rowMatches`' haystack (`roundsLog.ts:820`) | S6 adds the summary sentence to it | [PLAN_the_log_searches_the_findings.md](PLAN_the_log_searches_the_findings.md) queries `findings_fts`; its point 5 keeps the row-text filter. **It cites this function at a different line — reconcile by opening the file before either lands, and this plan goes first** |
| The rounds log's tab ARIA | not started here | [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) owns it; S6 changes only which ids the handler derives |
| Page tests that run the page | the new page ships this way from the start | [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) owns the backlog of existing source-text assertions |
| Notification text in five languages | not done | [PLAN_a_stale_translation_is_invisible.md](PLAN_a_stale_translation_is_invisible.md) owns staleness; translation is out of scope until it lands |
| The Team server's refusal vocabulary | not touched | [PLAN_refusals_that_explain_themselves.md](PLAN_refusals_that_explain_themselves.md) owns `src_server` |
| Where the server resolves its data dir | S8 depends on it | [PLAN_the_settings_file_ignores_the_side.md](PLAN_the_settings_file_ignores_the_side.md) goes first |

## Build order

**S1 — the record, the two ledgers, the derived count.** `notifications.ts` (shape, line, defensive
parse, redaction at the serialiser) · `notificationsFile.ts` · `DATA_TO_MOVE` +
`shared/data-inventory.json` · `scripts/count-notifications.mjs` · the `flushLedgers` drain fix and the
second append chain · the stamped cache and tail-from-offset read · `measure:append` re-run against a
UNC path, outcome in the docstring.

**S2 — the funnel.** `notify.ts` with all five obligations; every event site routed; the 16
confirmations record **both the asking and the answer** — `chatDoorsFile.ts` records the door before
anything that can refuse, *"because those are invocations too"*, and that was the **blocking finding**
on its plan round. The structural test and its companion land with this step.

**S3 — surface what is already on the wire.** `providers.ts` keeps `unrecognised[]` and `vaultNote`;
each sentence becomes a notification. (Moved after S2: the first draft had it first, where the funnel
it needs does not exist yet.)

**S4 — suppression and storms.** The per-`(code, subject)` ceiling, the run-wide budget, the LRU,
`notifyResolved`, the `storm` class, and the read-time grouping that counts rows. Ahead of the UI,
because a loop must be recorded whether or not anybody has the page open.

**S5 — the panel section AND the page, together.** They cannot ship apart: the section's whole content
is a button that opens the page, and a button that opens nothing is worse than no section. This step is
the fourth live region, the watermark, the coalesced refresh, the `package.json` command, the two page
modules, registration, the shared `PAGE_SIZE` / `compareRows` / range-conversion extraction, tabs,
sorting, paging and filters — **and the surface for S2's "N records could not be written" counter**,
which S2 records with nowhere to show it until here. The page panel must carry `enableFindWidget: true`
as a **top-level literal** and subscribe to `onDidDispose`; **three** structural scans discover panels
and will find it — `panelsAreSearchable.test.ts`, `theLogRefusesToOpen.test.ts` and
`theTabWearsAnIcon.test.ts`.

**S6 — the rounds log brought into line** (operator, 2026-09-16). Two changes and no others: the tab
handler derives its sections from `[data-section]` instead of naming four ids literally
([roundsLog.ts:1585-1588](../src_vs_code/src/roundsLog.ts)); and `rowMatches`' haystack
([roundsLog.ts:820](../src_vs_code/src/roundsLog.ts)) — which joins `subject`, `branch`, `repoPath`,
`repoName` and the per-reviewer lines and leaves out `answered` — gains the summary sentence, so that
typing `Role2` finds the rounds whose Reviewers cell says `Role2`. Its existing tests must stay green,
unedited — **and that is not evidence, so each change gets a RED-first test of its own**: a newly
introduced `data-section` is hidden by the handler (green today only because four ids are hard-coded,
so it fails first), and a search for a phrase that appears only in the summary sentence matches. A
regression restoring either behaviour would leave every existing test green.

**S7 — the three extension-side defects** (1, 2 and 3), each RED first. 3 is independent of everything
above.

**S8 — the server half, and defect 4 with it.** The instrumentation points (both `Error` helpers),
`ResolveDataDir`, the run-start marker, the seam leg — and the `try/catch/finally` around the host.
Defect 4 lives here rather than in S7 because it changes the `coai-mcp` binary and its
externally-killed half depends on the run marker this step builds; splitting them would put half a
guarantee in a step that ships without the other half. Needs a `coai-mcp` release.

## Test plan

Runner: `cd src_vs_code && npm test` (`node:test` + `node:assert/strict`, compiled to `out/`; the
compile cleans `out/` first, which matters — a stale `out/` runs both the old and the new name after a
rename and inflates the count). C#: `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe` — **never**
`dotnet test`.

| # | Test | Why it has teeth |
|---|---|---|
| 1 | **The funnel is the only door**, plus **a companion asserting the scan still finds the sanctioned call inside `notify.ts`**. Tests exempted by construction. | `testing.md`: a structural test that matches nothing passes for ever. One test is the prohibition; the scan needs the second or a reformat silently disables the plan's only enforcement. |
| 2 | **The seam.** A leg in `scripts/run-seam.mjs`: the **real** `coai-mcp` writes a notice, the extension's **real** parser reads it back; plus a shared vector file for the record shape — **including secret-bearing vectors written through BOTH writers, asserting on the persisted bytes**. | `testing.md` makes a live cross-implementation check MANDATORY for a contract with two implementations and forbids hand reconciliation as the fix. This harness was watched failing on `remoteVendor`. The secret vectors are there because redaction on one of two writers is `security.md`'s named defect, and the server is the writer nearer the secrets. |
| 2a | **No lock is held across a toast.** A stand-down raised from inside `serverSettingsSync`'s critical section returns before the person answers, and a `sync()` issued while the toast is still on screen does **not** answer `'busy'`. | The second draft's `await notify(...)` would have held that section for as long as the toast sat there and re-created the incident this plan is about. |
| 2b | **Every class is appended before it is shown**, not only the three the second draft named: an information toast whose host dies immediately after still has its row. | "Records, then shows" is the whole thesis; narrowing it to three classes made it false for the rest. |
| 3 | **Two writers, one ledger.** Two processes cross the storm threshold for one `(code, subject)`; the page shows **one** row, not two. And the counting test the consultation named: 10 occurrences and 99 occurrences must **not** produce an identical ledger. | The once-only bound is a read-time invariant; a single-process fixture would pass while production did not. The second half is what killed the sampled design, and it is the test that keeps it dead. |
| 3a | **Eviction cannot corrupt a count or lift a bound.** Three occurrences for subject A, 512 other subjects to evict A's entry, two more for A: the page shows **five**. Then a loop churning distinct subjects hits the **run-wide budget** and stops, rather than resetting its ceiling by eviction. | Both were defects in this plan's own second draft: a count taken from the evictable map would have read three, and a per-key ceiling alone is reset by the eviction the LRU exists to do. |
| 3b | **Two acknowledgements at once cannot move the watermark backwards.** Window A reads to offset 100, B to 200; both acknowledge, in either order; the effective watermark is 200. | A rewritten watermark file loses this update whichever way it is written, which is why the acknowledgement is append-only. |
| 4 | **The page RUNS.** Stub-DOM harness (`roundsLogPaging.test.ts:80-140`): 200 rows, Older/Newer and their bounds, a filter returning to page 1, a tab press hiding the other sections. `hitNested` for any control inside a row. | New behavioural assertions over page **source text** are refused here (operator ruling, 2026-09-14). A test without `hitNested` stayed green with its own `return` deleted. |
| 5 | **The page as it ships** — a `bundledPage.test.ts` arm, including the extracted `PAGE_SIZE`/`compareRows` surviving the `.toString()`-into-script path. | The minifier has broken this page twice. |
| 6 | **Redaction over every string field, iterated from the type** — credentials, bearer headers, `https://user:pass@host`, signed URLs, CLI stderr — plus the length caps. | Naming the fields in the test repeats a list the code also holds, and misses the field added next. |
| 7 | **Recovery resets.** A condition that ends and recurs writes a new first occurrence; one that persists does not. | This is the behaviour a clock window destroys, and the reason the window was removed. |
| 8 | **The ledger never throws, never recurses, and does not hide its own failure**: a failed append leaves the caller's work intact, raises no notification, and surfaces as "N records could not be written". | The regress has a disk error at the bottom of it; the silence has the plan's own thesis at the bottom of it. |
| 9 | **Fixtures built from today's local noon**, or the harness clicks *all dates* first; and the same instant filters identically on both pages. | A fixture pinned to a literal date is green the day it is written and red every day after. |
| 10 | **The four RED tests**, each watched failing against the unfixed code with the real symptom, then passing. Both observations reported. | `testing.md`, mandatory. |
| 11 | `DATA_TO_MOVE` / `data-inventory.json`, and the **three** structural scans that discover panels — `panelsAreSearchable`, `theLogRefusesToOpen`, `theTabWearsAnIcon`. | They pick the new page up automatically; they only have to pass. Calling them "two" would have left the tab-icon scan unaccounted for. |
| 12 | **A restart is a new run**, and the page says so: a fault that survives one shows as two runs, the ceiling starts again, and no threshold re-fires inside a run. | Two reviewers asked whether the ceiling persists. It does not, deliberately; an untested "deliberately" is indistinguishable from an oversight. |
| 13 | **Acknowledging a newest-N page leaves the older records unread.** 5000 records, a page that loads 3000, one acknowledgement: the unread count is 2000, not 0. | The bounded view and the watermark are two features that quietly cancel each other, and only a test over both catches it. |
| 14 | **Each run's marker is its own.** Two runs share a data directory, one is killed, the other exits cleanly: the next start still records the crash. | A singleton marker erases exactly the crash the mechanism exists for, and a single-window fixture never sees it. |
| 15 | **Role deletion survives a kill between every pair of steps** — tombstone, row, prompt files, each of the four settings — and the id stays reserved until the sweep finishes it. Plus: a tombstone stranded by a permanent stand-down can be finished by hand from the roles page. | The ordering is the fix; a test that only kills at the end would pass on the broken order. |
| 16 | **The published count cannot drift.** `scripts/count-notifications.mjs` writes a checked-in inventory file; a test regenerates it and fails when it differs, and the plan, the README row and the DoD quote that file. | Otherwise the script says 110 while three documents say 109 and every test is green. |

## What this plan deliberately does not do

- **Does not translate anything.** English, as every toast is today.
- **Does not merge the two sides' stores.** It makes the side visible instead.
- **Does not share one file between the two halves** — the repository refused that twice.
- **Does not trim, compact or rewrite the ledger.** Append-only is the whole design.
- **Does not ship a metrics exporter.** The rate is observable locally — a sortable column, the panel
  counts, and a Serilog line from S8. `src_vs_code/README.md:316` promises nothing leaves the machine,
  and changing that is a product decision, not a detail of this plan.
- **Does not add a one-shot CLI mode.**
- **Does not change which messages are shown.**
- **Does not touch the rounds log beyond S6's two changes**, or `src_server` at all.

## Open questions

1. **The event rate.** The growth projection's 20–50/day is a hand estimate with no measurement behind
   it — which `planning-docs.md` asks for by name ("a number, computed, not 'small'"). S1's counter
   makes it measurable; the projection is revised after a week, and sampling is considered then — if
   the measurement shows it is needed at all — never now.
2. **The 100 storm threshold and the 1000 ceiling.** Chosen, not measured. The first real storm says
   whether the alert fires early enough and whether the ceiling is ever reached in ordinary use.
3. **`roundsLog.ts` is far past the 800-line ceiling** and the new page deliberately does not inherit
   its shape. Should it be split — a separate task, not this one?
4. ~~**Whether `O_APPEND` holds on this machine's NAS path.**~~ **Answered, 2026-09-16**: it does —
   4 × 200 records over SMB, 800 of 800, 0 torn, with the production writer. See *B*. The per-process
   escape stays written down and unused.

## Definition of Done

- [ ] Two ledgers exist, merged at read time; neither half needs the other to ship; both are in
      `DATA_TO_MOVE` and `shared/data-inventory.json`; the growth budget is in the module that owns it.
- [ ] Every event site goes through `notify.ts`; the append is awaited for **every** class before its
      toast, and **no caller ever awaits a toast inside a lock** — the action is fetched by the separate
      `notifyAndAsk` entry point, used only outside critical sections. The confirmations record both the
      asking and the answer; the structural test refuses a new direct call **and** its companion proves
      the scan still matches.
- [ ] `Progress` is **not** a ledger class, `withProgress` is stated as out of scope, and no test
      claims coverage of it.
- [ ] The ledger reports a failed write to its caller, so the "N records could not be written" counter
      can be more than decoration.
- [ ] The watermark is a map **per ledger**, acknowledges only the range actually rendered, and treats a
      missing file as a first run rather than an error.
- [ ] Every run has its own marker; a clean exit clears only its own; startup sweeps them all.
- [ ] Role deletion writes its tombstone **before** the row, resumes after a kill at any step, and a
      tombstone stranded by a permanent stand-down can be finished from the roles page.
- [ ] The `.NET` writer applies the same redaction and caps as the TypeScript one, proven by
      secret-bearing seam vectors through both.
- [ ] The run-wide budget counts only repeats; a first occurrence and the reserved `failure`/`stand-down`
      allowance are never suppressed by it.
- [ ] `server-notices.jsonl` has a computed projected size before its first write, not a promise to
      measure later.
- [ ] `code` is a string literal at every call site, enforced by the same scan.
- [ ] The panel's last section is a **live region** showing per-class unread counts, and the counts
      survive a reload; opening the page appends an acknowledgement only after a complete read, and two
      windows doing it at once cannot move the watermark backwards.
- [ ] The page has a tab per class with full ARIA, sorts every data column, paginates at the shared
      `PAGE_SIZE`, filters by search/source/date over a haystack that includes the message, loads the
      newest N rather than the file, and names the data directory it reads.
- [ ] A repeating fault is recorded exactly up to the ceiling and explicitly truncated beyond it, never
      ambiguously in between; recovery resets; two processes crossing a threshold produce **one row**.
- [ ] The rate is derived, and absent rather than zero when it cannot be measured.
- [ ] Redaction runs at the serialiser over every string field, iterated from the type.
- [ ] The seam leg runs the real server against the real parser and was watched failing before it passed.
- [ ] `flushLedgers` drains to quiescence; notifications have their own chain; a killed process is
      recorded by its run marker on the next start.
- [ ] The rounds log derives its tab sections from `[data-section]` and its search finds the summary
      sentence — with its existing tests green and unedited.
- [ ] All four defects fixed, each with its RED failure message and its GREEN pass reported.
- [ ] The published site count comes from a **checked-in inventory** the script regenerates, and a
      test fails when the two differ — not a number retyped into three documents.
- [ ] `npm test`, `npm run test:seam` and `CoaiMcp.Tests.exe` green.
- [ ] `research/module_extension.md`, `research/module_server.md` and **`research/module_tests.md`**
      updated — the last gains the notifications page row and says what it does not prove — and
      `research/architecture.md` gains the ledger under *The one interface neither container owns*.
- [ ] The reciprocal boundary line is in all six sibling plans, not only in this one.
- [ ] The `Currently open` table in [README.md](README.md) carries this plan.
- [ ] **Promotion waits for S8.** The second draft said promote on S7 and extract S8, which
      contradicted every S8 line still in this DoD: a plan promoted with unchecked promises and no
      handoff is worse than one that waits. The gate's own command for this round is *do not split this
      again*, so it does not get split — it is promoted when S8 has shipped and the server release is
      out. If S8 is ever abandoned rather than delayed, THEN it is extracted, and its DoD lines leave
      with it.
- [ ] The plan went through **`review_plan`**, and the code through `review_code` after it. Not
      `review_document`: that gate has no code round after it by definition, and `review_code` refuses
      until a PLAN round has reached `proceed`, so a document round here would leave the code gate
      locked and have to be paid for twice.
