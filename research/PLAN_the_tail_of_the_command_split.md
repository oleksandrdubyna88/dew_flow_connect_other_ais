# PLAN — the eleven defects the split was not allowed to fix

> Status: **IMPLEMENTED, 2026-09-18** — thirteen stories and seven SonarCloud findings, with
> **three stories partial by decision**. Stories 4, 5 and 8 each shipped their defect fix and each
> left a named half unbuilt, on a measurement recorded beside it: story 4’s picture RETENTION,
> story 5’s resumable rename JOB, story 8’s budget on `await thread.writes`. All three are in
> *Still open* with their reasons — and saying so here rather than only there is the point, because
> this plan's own opening lesson is that a named gap in a shipped plan reads as done to everybody
> who was not in the room. The first draft of this line said “all thirteen stories shipped” and two
> reviewers refused it. Deviations are in *What shipped differently*; the tail is in *Still open*.
>
> **Scope — every module a story below MODIFIES, not only the ones it quotes.** Nine of them came
> out of [PLAN_the_command_file_is_too_big.md](PLAN_the_command_file_is_too_big.md):
> `chatHooks.ts`, `chatFollow.ts`, `chatArchive.ts`, `chatLaunch.ts`, `chatTurn.ts`,
> `chatPersist.ts`, `chatCapture.ts`, `chatSessionJoin.ts`, `chatThread.ts`. Three predate it and
> are reached anyway: `chatStoreWrite.ts` (story 3 changes `nextAfterSave`, which lives there rather
> than in `chatPersist`), `chatPage.ts` and `chatModels.ts` (story 11 moves a type out from under
> both). One is new: `chatContracts.ts`. The second round added two more that are EXTENDED rather
> than fixed: `atomicFile.ts` (story 4 teaches `writeFileAtomically` to take bytes) and
> `chatStoreSweep.ts` (story 4 was to widen the sweep's reach to `pictures/<id>` — **it did not,
> and that half is in *Still open***; measured, that tree is persistent data rather than the
> store's, so it needs a retention policy rather than a wider sweep). **Story 13 reaches
> outside `src` altogether** — `src_vs_code/package.json` (a dev dependency and a test script),
> `.github/workflows/ci.yml` (a job and an `xvfb-run`), a new suite, and
> `research/module_tests.md`, whose twelve “not covered” rows are what it exists to start closing.
>
> **The first draft listed eight and missed four**, which is worth leaving on the record rather than
> quietly correcting: it named the module each story's *Where* line quotes and forgot the ones the
> story's own text goes on to touch — `chatLaunch.ts` is in story 2's “check all four while here”,
> and `chatModels.ts` is the entire point of story 11. A scope line built from headings describes
> the reading, not the work. (CodeRabbit on PR #356 named `chatLaunch.ts`; re-reading each story
> against what it edits found the other three.)
>
> **Extracted from the parent plan when it was promoted on 2026-09-17.** Everything here is recorded in its
> tail rather than ticked, and a named gap in a shipped plan reads as done to everybody who was not
> there — which is the whole reason `planning-docs.md` asks for this document.
>
> **The boundary with the parent, named here as well as there.** The parent was a series that **may
> not change behaviour**, and that constraint is what proves it: 3 388 body lines matched verbatim
> against the original, in 33 contiguous runs, with zero residue. Every item below **does** change
> behaviour. Fixing one inside that series would have destroyed its only proof, and a diff that moves
> 3 388 lines and fixes eleven things is a diff nobody can review. So the constraint created this
> backlog deliberately; it is not an oversight.
>
> Related: [PLAN_the_page_tests_run_the_page.md](../todo/PLAN_the_page_tests_run_the_page.md) — the other
> standing tech-debt plan over the same files. It governs how the WEBVIEW PAGE is tested; this one
> governs what the host DOES, and story 13 builds the harness for the host.
>
> **The two plans do NOT meet, and an earlier draft of this line said they did.** Story 6's refusal is
> drawn by a native `createQuickPick`, not by a page script, so that plan's harness is the wrong
> surface for it — established by reading `conversationPickerCommand.ts:197`, after two rounds of
> review had sent story 6 there. Neither plan blocks the other.

## The goal

Eleven defects and six analyser findings, all of them **pre-existing on `main`**, all of them read
line by line while the file was being carved up. They were found by the coai gate's code round over
the split (25 findings, 17 accepted) and by SonarCloud on the pull request.

One of them is a security defect and should not wait for the rest.

Nothing here is a regression of the split. Each item names the file and line it lives at today, so
the first thing any story does is re-read that line — the parent series moved these lines once, and
a line reference in a plan is worth what it is verified against.

**This plan has been through three gate rounds and one consultation, all on 2026-09-17**, and what
they changed is marked in place — a plan that hides its own revision is a plan whose reasoning cannot
be checked.

| | reviewers | findings | accepted | rejected |
|---|---|---|---|---|
| round 1 | 3 | 23 | 18 | 5 |
| round 2 | 3 | 17 | 11 | 6 |
| round 3 | 3 | 17 | 14 | 3 |

**Round 1's largest change was the build order**: it was wrong, and in a way the first draft had
already written down without noticing.

**Round 2's accepted findings pushed three stories from “small fix” into “design a persistence or
cancellation protocol” — and the consultation that followed showed that most of those protocols are
already in this repository.** That is the single most useful thing either round produced, because the
gate cannot tell you what you already own; it reads the plan, not the tree. Reuse found: `abreast`
(bounded concurrency), `atomicFile` (write-beside-and-rename with a collision-proof temporary name),
`DEBRIS_AGE_MS` (the retention constant), `ChatStoreFile.refile` (per-record replay safety),
`ChatSession.stop` (vendor-aware cancellation), `Thread.generation` (the epoch). What is left as
genuinely new is one persistence surface, in story 5, and it is named there as such.

**The consultation also cost me two of my own conclusions, and found a twelfth defect.** Both are
recorded where they belong — story 6 and story 12 — rather than smoothed over here.

**A thirteenth story was added after all of that, by the operator's decision.** Story 6's dead end —
a native `createQuickPick` that nothing in this repository can drive — is one of TWELVE rows in
`research/module_tests.md` ending “not covered … needs an extension host”. Twelve honest admissions
is a pattern, not an omission, and the operator's call was to build the harness **in this plan**
rather than in a document of its own, because story 6 cannot finish without it.

**Round 3 then read the whole thing again and refused six assertions this plan was making about its
own work.** The pattern in them is one thing: *a sentence describing an outcome was standing where a
change belongs.* “The sweep's reach is widened” named no mechanism; “a visible refusal” named no
surface; “the session was asked to stop” was a test that passes while the process it should have
killed is still running; story 5 named the thirty-second lock expiry as a problem and then did not
solve it; story 13 required a harness that must fail loudly and did not say through what; and the
Sonar table promised six findings while locating five. Each is now a mechanism rather than an
outcome, and the security sweep round 3 asked for **found two things this plan did not know** — one
already-hardened site worth copying, and a second containment implementation nobody had noticed.

---

## Story 1 — a symlink can escape the workspace (SECURITY, do this first)

**Where:** [chatHooks.ts:581-605](../src_vs_code/src/chatHooks.ts#L581-L605), `openWorkspaceFile`,
reached from the page at [chatHooks.ts:482](../src_vs_code/src/chatHooks.ts#L482).

**The symptom.** A model's answer can carry a link, and following it opens a file. The containment
check is `isInside(folder.uri.path, target.path)` — **lexical only**, a comparison over the path as
written. Three lines later `vscode.workspace.fs.stat` runs and then `vscode.window.showTextDocument`,
and **both follow links**. So a workspace containing `docs/secrets -> /home/me/.ssh` passes the check
(the path as written is inside the folder) and opens the file outside it. The input is a model's
output, which is the definition of untrusted.

**Why it is real rather than theoretical.** The repository already treats this exact shape as a
threat one module over — and `claudeSessions.ts:960` is not just a realpath, it is the whole pattern
this story should copy:

```ts
export function staysInside(dir: string, file: string, real: { readonly dir: string; readonly file: string }): boolean {
  return under(dir, file) && under(real.dir, real.file);
}
```

Both pairs, the written one **and** the canonical one. Beside it, `realOf` (`claudeSessions.ts:972`)
returns `undefined` rather than the path when `fs.realpath` throws, with a header recording that it
used to fail open and that the gate caught it **twice**: *"a security check answering 'yes' because
it could not run"*. Two containment checks over model-reachable paths in one extension, one hardened
by two gate rounds and one never asked the question.

**The fix.** Reuse that pattern rather than writing a second one (`reuse-first.md`: a second
implementation of a capability is a defect from the moment it compiles). Canonicalise both the folder
and the target, and require containment on **both** the written pair and the canonical pair. A target
that cannot be canonicalised is **refused, not guessed** — and in this loop "refused" means `continue`
to the next workspace folder, ending at the existing `refuse(...)`, never a fall-through to the open.

**Three traps to name, because each of them is how this fix gets written wrong:**

1. **Compare like with like.** `folder.uri.path` is a *URI* path (`/d:/rsd/x`, forward slashes) and
   `fs.realpath` returns an *OS* path (`d:\rsd\x`). Feed one of each to a separator-bounded check and
   it refuses everything on Windows — a security fix that looks like it works because nothing opens.
   Both sides come from `fsPath`, or both from `uri.path`, never one of each. `chatRoots.ts` already
   owns `fsPathOf`/`asUri` for exactly this.
2. **Do not hand-roll the boundary.** `isInside` (`chatMessages.ts:250`) already bounds at a
   separator — `root.endsWith('/') ? root : root + '/'` — so `/ws` does not contain `/ws-secret`. A
   canonical comparison written with a bare `startsWith` reintroduces the sibling-directory escape
   that `isInside` was written to close. *(gemini, the plan round.)*
3. **The race is not closed, and the plan says so rather than implying it is.** `showTextDocument`
   re-opens **by path**, so between the canonical check and the open a local writer can replace the
   file or one of its parent components. VS Code's API takes a `Uri`, not a file descriptor, so there
   is no no-follow handle to hand it and the race cannot be closed from here. What it **is** bounded
   by is stated instead: the attacker must already be able to write inside the workspace, which is a
   strictly smaller set than "anything a model can put in a link" — which is the set the fix removes.
   The check runs immediately before the open, with nothing awaited in between. *(codex, the plan
   round; recorded as a residual risk rather than fixed, because the API does not admit a fix.)*

**The test (RED first).** A temporary workspace with a symlink pointing outside it, and a request to
open through that link. Red on today's code: the outside file is opened. Green after: refused, with
the reason. Then three more, because a containment check that refuses everything passes the first:

- a real file inside the workspace opens;
- a symlink that stays **inside** the workspace opens;
- a sibling directory (`/ws-secret` beside `/ws`) is refused — trap 2, asserted rather than assumed.

### Every model-reachable path-opening site, swept before one line changes

*(codex, round 3: “the symlink test can pass for `openWorkspaceFile` while another model-controlled
path still uses lexical `isInside`”. The sweep was run while writing this, and it changed the
answer — one of the three sites it found is already hardened, and copying ITS pattern is cheaper
than inventing one.)*

| site | reachable from a model? | state |
|---|---|---|
| `chatHooks.ts:610`, `openWorkspaceFile` | **yes** — a link in an answer | **the defect.** Lexical `isInside`, then `stat` and `showTextDocument`, both following links |
| `chatPanel.ts:368`, `openLink` → `vscode.env.openExternal` | **yes** — a link in an answer | **already clean, and it is the pattern to copy.** `chatMessages.ts:380` refuses at the MESSAGE BOUNDARY: `/^https?:\/\/[^\s]+$/i.test(url) ? { kind: 'openLink', url } : IGNORE`, with a header saying it *“validates rather than trusts”*. Nothing but `http`/`https` ever becomes an `openLink` |
| `dataCommands.ts:628`, a private `isInside` | no — a person choosing a data directory | **not a hole, but a SECOND implementation** of the containment rule, with its own case-folding and separator logic. Injected at `dataCommands.ts:450` as a parameter, which is why a grep for `isInside(` misses the call |
| `escalationWatcher.ts:314`, `installer.ts:101` and `:162` | no — targets the extension computes itself | out of scope, recorded so nobody sweeps them twice |

**Two things this changed.** First, `openLink` shows where the check BELONGS: at the boundary where
an untrusted message becomes a command, refusing by allowlist rather than inspecting later. Story 1
cannot move entirely there — a workspace path is legitimate and only its TARGET is in question — but
the refusal should be as early and as total. Second, `dataCommands.ts:628` means this repository has
**two** containment implementations that can drift. Unifying them is NOT this story (it is
person-driven and a different threat model), and it is written down here so the next reader finds it
rather than discovering it during the next incident.

**What the story leaves behind:** a structural check that every `showTextDocument`/`fs.stat` reached
from a page command goes through the one canonicalising road — with the legitimate match it already
has, so the check cannot pass by matching nothing. The sweep above is a measurement taken once; the
check is what keeps it true.

**Ship it on its own.** One file, one function, its own pull request, ahead of everything else here.

---

## Story 2 — closing a chat can leak a vendor process

**Where:** [chatHooks.ts:430-431](../src_vs_code/src/chatHooks.ts#L430-L431).

```ts
thread?.session.dispose();
thread?.home.release();
```

**The symptom.** Two cleanups, one statement each, no guard between them. If `dispose` throws, the
`release` never runs: a vendor process and its temporary directory outlive the tab that owned them,
and nothing says so. The same pair appears at `chatArchive.ts:235/242`, `chatLaunch.ts:284/285` and
`chatTurn.ts:111/112` — **check all four while here**, and fix them the same way.

**A `try`/`finally` is not the whole fix, and this is the correction the gate made.** Ordering the
two cleanups so both run does not terminate anything: if `dispose` throws while the child process is
alive, `release` then deletes — or fails to delete — a temporary directory **the process is still
using**. The stated symptom, a leaked vendor process, survives a fix that only reorders the
statements. *(codex, the plan round.)*

**The fix.** Each cleanup gets its own `try`, the release in a `finally`, **and** a failed dispose
escalates through the SESSION rather than through a pid — `ChatSession.stop()`, for the reason story 8
gives at length: only the session knows whether stopping means killing a local process, cancelling at
a remote vendor, or telling a Team server to drop the job. An earlier draft of this line said “kills
the process tree through the shared launcher”, which was the same wrong layer story 8 was corrected
for; it is fixed here rather than left as two stories disagreeing. Both errors are reported, not one
swallowed by the other (`coding-style.md`: never silently swallow errors).

**The test, and the gate rewrote it twice.** A thread whose `session.dispose` throws. Red:
`home.release` was not called and the child is still alive.

**“The session was asked to stop” is NOT the green condition, and an earlier draft said it was.** A
test that observes the CALL passes while the child outlives the tab — the leak this story exists to
close. Worse, the `finally` can then release the temporary directory while a forked grandchild is
still using it. *(codex, round 3.)* Green is therefore observed on the PROCESS: a real parent-child
tree, and after the cleanup neither is alive.

**Residual, stated because story 8 states it and these two must not disagree:** `ChatSession.stop()`
does not report confirmed termination, and for a REMOTE session there is nothing local to observe.
The local path awaits termination before releasing; the remote path records that cancellation was
requested and not confirmed, and says so rather than implying a kill.

---

## Story 3 — `keepOnDisk` allocates the whole transcript on every save

**Where:** [chatPersist.ts:83-88](../src_vs_code/src/chatPersist.ts#L83-L88), again at
[chatPersist.ts:100](../src_vs_code/src/chatPersist.ts#L100).

**The symptom.** The comment on line 83 reads *"MAPPED ONLY WHEN IT IS ASKED FOR"*. Five lines below,
`nextAfterSave(outcome, baseline, ours: readonly string[] = [])` takes an **array**, and the call
passes `ours()`. The function-shaped `ours` gives the appearance of laziness without the effect: a
10 000-message conversation allocates a 10 000-element array on **every successful save**, for a
comparison only a refusal reads.

**The correction this carries.** The gate reported this as *"`nextAfterSave` has a type mismatch"*
and that was rejected with a measurement — `ours()` returns exactly the array the parameter takes,
and the typecheck is clean. **The defect is the comment describing behaviour the call does not
have**, which is worse than a wrong type: a wrong type is caught by a compiler, and a comment that
lies is carried forward by every reader.

**The fix.** `nextAfterSave` takes the **supplier** and calls it in the one branch that needs it.
Three call sites, including `ours0(thread)` at `chatPersist.ts:177`.

**The test.** A save whose outcome is the success branch, with a counting supplier. Red: called.
Green: not called — and called exactly once on the refusal branch, so the fix cannot be "never call
it".

---

## Story 4 — replacing an image destroys the old one first

**Where:** [chatHooks.ts:643-656](../src_vs_code/src/chatHooks.ts#L643-L656), `attachPicture`, which
calls `forgetPicture` (`chatHooks.ts:623`) before writing the replacement.

**The symptom.** A full or unwritable disk leaves the conversation with **no image and nothing to
retry from** — the failure destroys the state it was meant to replace.

> **SHIPPED 2026-09-17 — the destructive order reversed — and the sweep half is NOT done, with a
> finding behind that.** `forgetPicture`'s own comment said its files live in *“a temp directory the
> tab's own close sweeps”*. Measured: `pictureDir` is `coaiDataDir()/pictures/<id>` — persistent data,
> not temp — it has exactly ONE caller, and **nothing anywhere removes it**. One directory per
> conversation that ever held a picture, kept for ever. The comment is corrected in place.
>
> The retention that would fix it is not a widening of the store sweep, because this is not the
> store's tree: it is a new policy with a real decision in it — when is a conversation's pictures
> directory collectable? When the conversation is deleted, or after an age? Who runs it? That is its
> own work and is recorded here rather than invented in a story about a destructive replace. **Third
> comment in this plan found describing behaviour the code does not have** (with story 3's laziness
> and story 2's escalation), which is now a pattern rather than three accidents.

**Almost all of this is already written, and the consultation is what found that.** The gate asked
for a same-filesystem temp-and-rename, a deterministic temporary name, and an age-bounded sweep. All
three exist:

| asked for | already in the repository |
|---|---|
| write beside the destination, rename, retry a transient failure, clean up on failure | `atomicFile.ts:120`, `writeFileAtomically` |
| a deterministic temporary name that does not collide | `atomicFile.ts:56`, `besideName` — `<destination>.<pid>.<sequence>.tmp` |
| “how old is debris rather than a write in flight” | `chatStoreSweep.ts:66`, `DEBRIS_AGE_MS`, one hour, already a named constant |

**So the fix is to EXTEND one helper, not to design a protocol.** `writeFileAtomically(path, text:
string)` takes a string and a picture is bytes, so it takes `string | Uint8Array`; the write goes
through it; the old attachment is retained until the new one has landed; and the sweep's reach is
widened to `pictures/<id>`. A fixed
temporary name would be a **regression** — `besideName` carries the pid and a sequence precisely so
two windows writing at once do not collide, and the gate's “deterministic name” is already satisfied
in a stronger form.

**“The sweep's reach is widened” was an assertion, and the gate was right to refuse it.** *(local,
round 3.)* The store sweep surveys the conversation-store root and its designated subdirectories,
and `pictures/<id>` is not one of them — so the debris a failed replacement leaves is collected by
nothing, and the sentence above was describing an outcome rather than a change. The story says HOW:
the picture directory joins the surveyed set, matched by the same `.tmp` rule and the same
`DEBRIS_AGE_MS`, and **the test asserts the survey REACHES it** rather than asserting the constant
exists. A widening that is only a sentence is a widening that never ran.

**One accepted finding shrank when it was checked, and that is recorded rather than quietly
dropped.** A reviewer required that *“the stored reference moves before the old file goes”*, and it
was accepted. **There is no stored reference.** `recordOf` (`chatPersist.ts:49`) writes version, rev,
id, title, passage, modelId, messages, fromSession, carryFrom, source, workspace, createdAt,
updatedAt — and no attachment; `grep -rn "attached\|picture" chatStoreFile.ts chatPersist.ts`
returns nothing. The picture lives only on the in-memory `Thread`. Honouring that finding literally
would mean **adding attachment persistence**, which is new functionality inside a plan whose whole
premise is fixing defects. The problem the finding is really about is kept — a failed replacement
must not destroy the image that was there — and the crash-consistency half is dropped with this
measurement beside it. *(Accepting a finding commits you to the PROBLEM, not to the fix somebody
proposed for it.)*

**The test.** A write that fails: red, the old image is gone; green, it is still attached and the
failure is reported. A rename that fails after the write: green, the old image is still attached.
Then the sweep: an aged `.tmp` under `pictures/<id>` is removed and one younger than `DEBRIS_AGE_MS`
is left alone — the second half matters, because a sweep that deletes a write in flight is a worse
defect than the debris it collects.

---

## Story 5 — a folder rename refiles conversations one at a time

**Where:** [chatFollow.ts:45](../src_vs_code/src/chatFollow.ts#L45), `followRenames`, over the walk
at [chatFollow.ts:71](../src_vs_code/src/chatFollow.ts#L71).

**The symptom.** Strictly sequential, each record with up to five 200 ms retries. Ten thousand
records is **hours**, with nothing on screen and a stale picker until the final refresh.

> **MEASURED 2026-09-17, and “hours” is wrong by two orders of magnitude.** Against a real store on
> this disk: **5.76 ms per refile**, 200 records in 1 152 ms. Ten thousand SEQUENTIALLY is therefore
> about **58 seconds**, not hours. The plan's figure assumed every record hitting the five-try,
> 200 ms retry path — which happens only when another window holds every lock at once, not when a
> folder is renamed.
>
> **So the lease apparatus is out of proportion and is NOT built.** A persisted job with a schema,
> state transitions, a heartbeat shorter than a thirty-second expiry, a fencing token and a recovery
> owner is the right answer for work that runs for hours. For a job of about a minute — ten seconds
> with the pool — it is a storage surface, a migration and a class of bug bought to solve a problem
> the measurement says is not there. What shipped is `abreast` at width 8 and the notice stories 6
> and 7 added.
>
> **What is left open, honestly:** a rename interrupted mid-way still leaves records split, and
> nothing revisits them. The exposure is now ten seconds rather than hours, which is why it is
> recorded rather than engineered around. If the store ever grows an order of magnitude, re-measure
> BEFORE building the lease — that is the whole lesson of this note.
>
> **And making the loop concurrent reintroduced a fixed defect, which a test caught.** Hoisting
> `heldConversationIds` out of the per-record job is the snapshot an earlier code round removed: a
> conversation that CLOSES mid-run is no longer followed by its thread, so a stale snapshot has its
> rename followed by neither half. `chatSourceWiring` refused it within the minute.

**The pool already exists — do not write a second one.** `abreast(jobs, width)` (`abreast.ts:14`)
runs jobs a few at a time and returns results in the jobs' own order. Its own header records that it
was written inside the migration, found inline in the store's listing, and **moved here rather than
copied a third time** — the reuse rule's second move, already performed. Story 5's “bounded
concurrency, a small fixed width, not unbounded `Promise.all`” was this plan specifying a helper the
repository had already factored out.

**And replay safety is already there too, one layer down.** `ChatStoreFile.refile()`
(`chatStoreFile.ts:465`) claims the conversation, re-reads it, checks its source against `was`, and
commits the transcript before the metadata. Refiling A→B twice therefore leaves B alone rather than
corrupting it, and a `partial` outcome means the transcript committed while the index did not — which
the next `read()` repairs. **Idempotence per record is not this story's to build.**

**Progress that moves while it works, not at the end.** An indicator that only updates when the batch
finishes is the same silence in a different colour: report every N records. *(local, the plan round.)*

**And it must be resumable, which is the finding that changes the shape of the story.** A rename
interrupted after 4 000 of 10 000 records leaves the rest in the old location **and the rename event
is gone**. Nothing revisits them, so the conversations stay split between two folders and the picker
stays wrong for ever — a worse end state than the slow one this story is fixing. The work is
therefore a **persisted, idempotent job**: it survives a restart, it resumes rather than restarting,
and a partial failure is reported rather than dropped. Reconciliation at the next startup or the next
follow is what makes "resume" true rather than hoped for. *(codex, the plan round.)*

**What is genuinely missing, once the two halves above are subtracted:** a persisted rename INTENT,
per-item progress beside it, and worker OWNERSHIP. The existing locks cover an individual mutation
and **expire after thirty seconds** — they are not renewable job leases, so a batch that outlives one
cannot rely on it, and a startup reconciliation overlapping a live `follow` could otherwise claim the
same record twice. The sweep's daily marker must not be borrowed as that ownership either: its
read-then-write claim can race. **This is the one new persistence surface in the whole plan**, and it
is new because nothing here already does it — not because it sounded thorough.

**And naming the thirty seconds is not the same as solving them — two reviewers said so
independently, and they were right.** *(gemini and codex, round 3.)* The paragraph above states the
expiry as a reason the existing locks cannot be used, then specifies ownership without saying how it
is HELD across a batch that runs for hours. Unsolved, an active job either loses its claim at thirty
seconds or a recovery worker takes records out from under it. So the job carries, explicitly:

| | what it is |
|---|---|
| **schema** | the intent (from, to), the record list, a per-item state, and a monotonic **fencing token** |
| **states** | `pending → claimed → done`, each transition durable before the filesystem move it authorises |
| **ownership** | a lease with a **heartbeat shorter than its expiry**, renewed while work continues |
| **a lost lease** | the worker STOPS at the next item rather than finishing the batch — a worker that ignores a lost lease is the duplicate-write path the lease exists to close |
| **fencing** | a move carrying a token older than the record's own is refused, so a paused worker that wakes up after its lease expired cannot write |
| **recovery** | a stranded job is reclaimed by the next startup or the next `follow`, named as the owner, with the age at which a job is abandoned rather than resumed |
| **size and retention** | how large the job state can get for 10 000 records, when a `done` job is deleted, and who deletes it |

**The test that decides this is not the happy batch.** It is two workers: a recovery pass started
while a live `follow` holds the lease, asserting the second one refuses rather than races; and a
worker whose lease is expired out from under it mid-batch, asserting its next move is refused by the
fencing token rather than applied.

**The test.** A store of N records with a counting clock: the pure scheduling half is extracted and
tested as a value (`coding-style.md`: extract a named unit), so the width, the bound and the progress
cadence are asserted without an editor. Then the interrupted case — a job killed at 40 % and
restarted — asserting every record ends up refiled exactly once.

---

## Story 6 — `index.refresh()` is not guarded

**Where:** [chatFollow.ts:97](../src_vs_code/src/chatFollow.ts#L97).

**The symptom.** The refresh runs after records have been refiled. If it throws, the picker keeps
offering names that have **moved** — the data is correct and the surface is not. The outer catch only
logs, so the person sees a working picker full of paths that no longer exist.

**The fix.** Its own guard, and on failure a visible refusal rather than a silent log — the records
moved, so the state on screen is known-wrong and must say so.

**Through WHAT, named rather than assumed.** *(local, round 3, and the objection was fair: “a visible
refusal” can hide a whole UI component.)* It is not a new surface. `main` already routes every
message through a notifications funnel — the ledger whose call sites `notificationSites.test.mjs`
counts and ratchets downward — and this refusal joins it like the rest. No new component, no picker
redesign, and **a new direct `vscode.window.show*` call would fail that ratchet**, which is the check
that keeps this story from growing a surface of its own.

**The test, and this is where two rounds of review and one consultation all landed somewhere
different.** A unit test over the callback passes while nobody is actually told, which leaves exactly
the stale picker the story claims to fix.

The second round said: use the running-page harness of
[PLAN_the_page_tests_run_the_page.md](../todo/PLAN_the_page_tests_run_the_page.md). Then the consultation
found that a harness already exists here — `src/test/bundledPage.test.ts`, 24 tests that bundle the
page with esbuild, **minify** it and execute it against a stub DOM, including a case that presses Send
and asserts exactly one turn goes out. So the dependency looked dissolved.

**It is not, and the reason is worth more than the conclusion was.** That harness runs WEBVIEW page
scripts, and this picker is not one. `conversationPickerCommand.ts:197` calls
`vscode.window.createQuickPick<Item>()` — a NATIVE control, and the file's own header records that it
is the repository's first `createQuickPick` against six `showQuickPick` sites. (Measured on
2026-09-17: still the ONLY `createQuickPick` call in `src`, and the `showQuickPick` count has since
risen to **seven** — quoted here as the header states it, corrected beside it, because a number in
prose is stale the day after it is typed.) Pushing a refusal into
the bundled page would prove the page renders a message; it would prove nothing about the path from
`chatFollow` through a native QuickPick, which is the path that is broken.

**So the honest statement is:** this story's visible half needs an extension-host harness. It is
**not** blocked on the page-tests plan — that plan governs a different surface.

**And that is why this plan grew a thirteenth story rather than an excuse.** The first draft of this
paragraph ended by writing the limit down and shipping around it: the guard, the classification, a
unit test that the refusal is RAISED, and a note that nobody can prove it is SEEN. That is an honest
note and a permanent one — `research/module_tests.md` already carries twelve of them. **Story 13
builds the harness**, and story 6's scenario is its first and only conversion, watched red against
this very defect before the guard exists. Story 6 therefore ships with its visible half proved, and
is sequenced after story 13 for that reason.

---

## Story 7 — `follow` cannot tell a busy store from a fatal one

**Where:** [chatFollow.ts:118](../src_vs_code/src/chatFollow.ts#L118).

**The symptom.** A permission failure is retried five times and then reported exactly as a lock is.
A lock clears; a permission does not, so the retries are a second of waiting that was never going to
help, and the person is told nothing either way.

> **MEASURED 2026-09-17, and this story's premise was wrong too.** “A permission failure is retried
> five times” — it is not. `chatStoreLock.ts:179` returns `false` only on `EEXIST` and THROWS for
> anything else, so a permission error becomes `failed`, and `follow`'s `failed` arm returns at once
> without entering the retry. The classification this story was written to add **already exists, one
> layer down**, and was designed there deliberately.
>
> What IS true is the second half: *“the person is told nothing either way”*. That is story 6's
> mechanism, on the same file, so the two shipped as ONE change on 2026-09-17 — `followReport` and a
> guard around `index.refresh()`. Three of this plan's premises have now been wrong on measurement
> (stories 7, 8 twice), and all three were wrong the same way: they described a missing DISTINCTION
> where what was missing was a missing VOICE.

**Read it beside the rejection it survived.** The gate over the split claimed *"`follow`'s retry can
hang the extension host"* and that was rejected with a measurement — five tries at 200 ms is one
second, in a detached call nothing awaits, and a test pins the bound. That rejection stands. **The
defect is not the duration, it is the classification**: the retry loop treats every failure as
transient.

**The fix.** Classify the failure. A contention error retries; anything else fails at once and says
what happened — **and says what to do about it**. "Permission denied" names a state; a person needs
the path and the next move, or the message is a dead end they answer by reloading the window.
*(local, the plan round.)*

**The test.** Two stores — one refusing with a lock, one with a permission error. Red: identical
behaviour, five attempts each. Green: one attempt for the permission, five for the lock, two
different messages, and the permission message names the path.

---

## Story 8 — a reset can wait forever

**Where:** [chatArchive.ts:213-229](../src_vs_code/src/chatArchive.ts#L213-L229), `ended`.

**The symptom.** `await thread.turns` and `await thread.writes` with no timeout. One stuck turn — a
vendor process that has not exited — leaves *Ending the previous conversation…* on screen
indefinitely, with no way out but reloading the window.

> **MEASURED 2026-09-17, while implementing it, and half this story's premise was wrong.** Three
> things were checked in the code rather than taken from the round that raised them:
> 
> | claimed | measured |
> |---|---|
> | the abandoned turn is never stopped | **`ended()` already calls `thread.session.stop()`**, as its first act |
> | `await thread.turns` can wait for ever | **both session kinds bound a turn** — `cliChatSession` has an injectable turn budget, `remoteChatSession` a `budgets.turnMs` deadline |
> | `await thread.writes` can wait for ever | **true.** `grep` for `setTimeout`/`AbortSignal`/`timeout` across `chatStoreFile.ts`, `atomicFile.ts`, `chatPersist.ts` and `chatStoreWrite.ts` finds ONE, a retry pause. Nothing bounds a write, and the data directory can be a NAS |
> 
> So parts 1 and 2 of the fix below were already there, and what remains is the WRITE path — which is
> also where the consultation found the missing fence. Part 3 shipped on 2026-09-17; the budget on
> `await thread.writes` is still open, and is deliberately NOT bolted on: abandoning that wait is how
> the archive saves against a revision the disk does not hold, so it needs its own design rather than
> a number.

**A budget alone makes the UI honest and the process worse, which two reviewers found independently.**
When the wait expires and `ended` returns, the turn it stopped waiting for is **still running**: the
vendor process keeps its memory and its handles, and its late completion can still write — into a
thread that has since been replaced, which is a corruption the original hang could not produce.
*(gemini and codex, the plan round.)*

**“Kill the process tree through the shared launcher” was the wrong primitive at the wrong layer, and
the consultation is what caught it.** `chatArchive.ts` has no business reaching for a pid: a session
may be a local CLI, a remote vendor keeping the conversation in its own store, or a Team server
holding no conversation at all, and only the session knows which. `ChatSession.stop()`
(`chatSession.ts:93`) is **already** that contract, and its header spells out all three cases — a
process that HOLDS the conversation is killed and reports `contextLost` so the caller re-sends the
transcript; a vendor that keeps it resumes by id and loses nothing; a Team server is only told to drop
the job. Story 8 calls `stop()`.

**The fix, in three parts, none of them optional:**

1. A budget on each wait, and a terminal state when it expires that names which one did not finish —
   CLAUDE.md §8: never stick on an in-flight state.
2. The abandoned turn is stopped through **`ChatSession.stop()`**, not through a pid. **Residual,
   stated rather than implied:** `stop()` does not report CONFIRMED termination, so the plan claims
   the turn was ASKED to stop and no more. A kill that silently failed must not read as a kill that
   worked — that is the same shape as the security check that answered “yes” because it could not run.
3. Any completion arriving after the budget is **refused rather than applied**, and the plan now names
   exactly which path is unfenced. `chatTurn.ts` already carries TWO fences, a generation check and a
   `sameSlate(mySlate, thread.saveId)` check. **`chatPersist.ts` carries none** — `grep -n
   "sameSlate\|generation" chatPersist.ts` returns nothing on the write path. `keepQueued` chains
   `keepOnDisk` onto `thread.writes`, which reads `thread.saveId` at execution time, and `settle()`
   (`chatPersist.ts:111`) then assigns `thread.rev = next.rev` to whatever thread it is holding. So an
   old save resolving after a replacement stamps the REPLACEMENT with the old save's revision. Fence
   the queued execution and the post-await effect, and leave `forkOnDisk`'s own legitimate `saveId`
   change (`chatPersist.ts:145`) alone. *(the consultation, verified by reading both paths.)*

**And the person is told when the stop could not be confirmed.** *(gemini, round 3.)* Part 3 drops
late completions silently, and part 2 cannot confirm termination — together those would leave a
conversation in a terminal state while a vendor process may still be running and being billed for.
The terminal state therefore distinguishes *ended* from *ended, and the previous turn was asked to
stop but did not confirm*, through the same notifications funnel story 6 uses.

**The test.** A thread whose `turns` never settles. Red: `ended` never returns. Green: it returns
within the budget with a reason naming the turn, and `stop()` was called on the session. Then the one
that decides: hold an old `store.save()` pending, replace the conversation, resolve the old save with
`{kind:'ok', rev:7}` — red, the replacement's `rev` becomes 7; green, it is untouched and the stale
outcome is dropped.

---

## Story 12 — an abandoned turn repaints the conversation that replaced it

> Numbered twelve because it was found last; placed here because it is story 8's mechanism seen from
> the other end, and the two are one change.

**Where:** [chatTurn.ts:443-460](../src_vs_code/src/chatTurn.ts#L443-L460).

**Found by the consultation, after I had inspected this exact function and said it was fine.** That
is the whole reason it is written down this way: I read to line 452, saw the `sameSlate` guard and the
comment recording that reviewers designed it at an earlier plan round, and reported to the consultant
that the fencing was already there. It answered that I had stopped five lines early. It was right.

**The symptom.** When a turn resolves after a reset has replaced the conversation, two mutations reach
the NEW thread before the stale branch returns:

```ts
thread.running = false;                       // 443 — before the check
if (!sameSlate(mySlate, thread.saveId)) {
  console.warn(...);
  show(entry, false, '');                     // 457 — inside the stale branch
  return;
}
```

`thread` is the replacement by then. Line 443 clears the replacement's `running` flag, and line 457
paints the replacement idle — so a conversation that is mid-answer can be shown as finished by a turn
belonging to a conversation the person already discarded.

**The comment above it is accurate about what it covers and silent about this.** It says *“Nothing is
recorded anywhere, ledger included”*, and that is true: the ledger write is below the return. The
guard was built to stop a stale answer being RECORDED, and it does. Nobody asked whether the two
statements around it also touch the new conversation.

**The fix.** The identity check moves ABOVE the `running` mutation, and the stale branch returns
without repainting. A turn that no longer owns the thread touches nothing on it.

**The test.** Start a replacement turn, then resolve the abandoned one. Red: the replacement's
`running` is false and its page was told the turn ended. Green: both untouched, and the warning is
still on the console — the existing behaviour that is correct must survive, or the fix has traded one
silence for another.

---

## Story 9 — New chat drops its progress indicator too early

**Where:** [chatArchive.ts:95](../src_vs_code/src/chatArchive.ts#L95) (`freshStart`) against
[chatArchive.ts:164-186](../src_vs_code/src/chatArchive.ts#L164-L186) (`publish`, which awaits
`thread.writes` at line 186).

**The symptom.** The progress scope closes after archiving, while `publish` is still awaiting the
disk write. A slow store therefore shows no terminal state at all: the indicator is gone and the new
conversation has not appeared.

**The fix.** The scope spans the write too — and closes in a `finally`, because extending a scope
over an operation that can **reject** is how an indicator gets stuck open instead of closing early.
A failed save ends in a visible failure, not an unhandled rejection. *(codex, the plan round.)*

**The test.** A store whose save is slow: red, the scope closed before the record existed; green, it
closes after, and the order is pinned. Then a store whose save **rejects**: the scope closes and the
failure is on screen.

---

## Story 10 — the keyboard capture probes the host before showing anything

**Where:** [chatCapture.ts:98](../src_vs_code/src/chatCapture.ts#L98), `await windowsReach()` —
defined at [hostSide.ts:101](../src_vs_code/src/hostSide.ts#L101).

**The symptom.** About a second in a remote window, with nothing on screen for it. A second press is
the natural response, and it starts a second capture.

**Showing progress earlier does not stop the second capture, and that was the whole justification.**
Two presses arriving while `windowsReach` is awaiting both pass the entry point and both start a
capture. The indicator makes the wait legible; it does not make the path single-entry. *(local and
codex, the plan round — the finding that keeps this story from shipping as decoration.)*

> **SHIPPED 2026-09-17, and it grew one item.** The latch is `oneAtATime` in its own module rather
> than a flag in `chatCapture`, because this repository already has THREE hand-rolled latches —
> `chatGotoCommand`, `chatStoreCache`, `bugzReviewPanel` — and a fourth written in place would be a
> fourth `finally` to get wrong. As a value its rules are asserted: refused not queued, taken
> synchronously before the first await, released on a throw. **Converting those three to it is new
> open work**, recorded here rather than done in passing (`reuse-first.md`: name it, propose it,
> ask).

**The fix.** An **in-progress latch set before the first `await`**, so a second press is refused or
queued rather than racing; progress shown before the probe; and both the latch and the scope cleared
in a `finally` on every outcome, including a throw.

**The test.** A slow probe: red, nothing was shown before the await; green, it was. Then two presses
during one probe: red, two captures; green, one, and the second press said why.

---

## Story 11 — `ChatModelChoice` is the whole of an import cycle

**Where:** [chatPage.ts:81-86](../src_vs_code/src/chatPage.ts#L81-L86) — a four-field interface with
no dependencies of its own:

```ts
/** A model the picker may offer. `remote` models say what they cannot do. */
export interface ChatModelChoice {
  readonly id: string;
  readonly label: string;
  readonly caption: string;
}
```

**This is bigger than the gate over the split thought it was, and the measurement is the reason to do
it.** The finding was accepted there as *"`ChatModelChoice` couples config to the page — true and
pre-existing"*, and filed as tidiness. It is not tidiness. Three production modules import it out of
the 2 243-line page renderer:

| importer | line | what else it takes from `chatPage` |
|---|---|---|
| `chatConfig.ts` | 19 | nothing |
| `chatModels.ts` | **1** | **nothing** |
| `chatThread.ts` | 3 | `ChatMessage` |

`chatModels.ts:1` is the finding. `importCycles.test.mjs` freezes nine cycles as a ratchet, and
`chatModels ↔ chatPage` is one of them — `chatPage.ts:6` imports `ChatProvider` back. **That one
import of a four-field interface is the entire return edge.** Move the interface and the cycle is
gone; nothing else in `chatModels` reaches the page at all.

`chatConfig.ts:19` is then free for the same move, and `chatThread.ts` — one of the two modules in
the whole split that need no editor — stops compiling a 2 243-line renderer in to learn what three
readonly strings are. It still needs `ChatMessage`, so **take that too**, or the module is left
importing the page for one type instead of two.

**The fix.** A neutral contracts module (`chatContracts.ts`) holding `ChatModelChoice` and
`ChatMessage`; the page, the configuration, the model list and the thread all import from it.

**What "done" means here, because "move a type" is where a refactor gets left half-finished**
*(local, the plan round):*

- [x] `ChatModelChoice` lives in `chatContracts.ts` and nowhere else. **`ChatMessage` deliberately
      did NOT move**, and the deviation is recorded in `chatContracts.ts`'s own header: it has FIVE
      importers rather than the one this table assumed, and moving it breaks no cycle, because none of
      the five is imported back by the page. It buys tidiness rather than a ratchet drop, so it is its
      own change. It is listed in *Still open*.
- [x] Every importer updated — **eight**, not the four listed here: `chatPage.ts`, `chatConfig.ts`,
      `chatModels.ts`, `chatThread.ts`, `chatPanel.ts`, and three test files. All take it from
      `./chatContracts`; none takes it from `./chatPage`.
- [x] `src/test/chatPage.test.ts` — which imports `ChatModelChoice` from `../chatPage` — updated
      too; the tests are importers like any other.
- [x] No re-export left behind in `chatPage.ts`. A convenience re-export keeps the edge and the
      cycle, and the ratchet would then be lowered by a commit that changed nothing.
- [x] A search returns no import of `ChatModelChoice` from `./chatPage` anywhere — verified
      2026-09-18. `ChatMessage` is still declared in `chatPage.ts:75`, per the deviation above.

**The test.** `importCycles.test.mjs` is already the check, and it is a ratchet that may only fall:
`KNOWN` goes from nine entries to eight, and `chatModels ↔ chatPage` is deleted from the list in the
same commit that makes it untrue. That is an assertion, not a claim — the test fails if the cycle is
still there and it fails if the entry is removed while the cycle survives.

**Sequenced last because it is the only item here that is not a defect** — nothing misbehaves today.
It is also the only one whose benefit a test states out loud.

---

## Story 13 — the extension-host harness, which twelve rows of the test map are waiting for

> Added on 2026-09-17, after story 6 ran into it. It is in THIS plan rather than a plan of its own
> by the operator's decision — story 6 cannot be finished without it and a second document would
> put the dependency in a place neither plan owns.

**Where:** `src_vs_code/package.json`, `.github/workflows/ci.yml`, and a new suite.

**The gap is already written down, and `research/module_tests.md:175` is where:**

> *“There is no extension-host harness here yet: `@vscode/test-electron` downloads a VS Code build
> and runs a suite inside it, which no workflow does today. **This is the largest single gap in the
> repository.**”*

It is not a lone note. **Fourteen places in that file mention an extension host, and twelve of them
are table rows ending “NOT covered … which is the row below”** — a picker nobody has driven, a
command's six arms checked by reading its source, a rename event simulated rather than raised, two
hosts contending for one settings lock proved by interleaving a fake. Every one of those rows is an
honest admission, and twelve of them is a pattern rather than an omission.

**What story 6 hit, and why it is what finally justifies this.** Story 6 must show a refusal when
`index.refresh()` throws. The refusal is drawn by `vscode.window.createQuickPick<Item>()` at
`conversationPickerCommand.ts:197` — measured, the **only** `createQuickPick` call in `src`, beside
seven `showQuickPick` calls. A native control cannot be reached by `bundledPage.test.ts`, which runs
webview scripts, and it cannot be reached by a unit test, which sees the decision and not the
drawing. There is no third option: either the host runs, or story 6 ships a guard whose visible half
is unproven.

### The design decisions, each with its reason

1. **`@vscode/test-electron`, NOT `@vscode/test-cli`.** The CLI wrapper is the friendlier package and
   it brings **mocha**. This repository runs `node --test` across **211 TypeScript test files and six
   `.mjs` ones**, and a second test framework beside that is precisely the duplicate the reuse rule
   calls a defect from the moment it compiles — two runners, two reporters, two ways to filter, two
   places CI has to read a failure from. `test-electron` is the lower-level package: it downloads a
   build, launches it, and runs **whatever runner you hand it**. It costs a `runTests` entry point
   and buys keeping one runner.
2. **It runs in its own `node --test` invocation.** The rule this repository learned on 2026-09-17
   and wrote into `theBundleLoads.test.mjs`: a test that drives the build gets an invocation to
   itself, because `node --test` parallelises files and a build rewrites the tree others are reading.
   A test that launches a whole editor is that rule's larger case — and the guard already there
   detects a build-runner by its source, so a new suite spawning `npm`/`code` is caught by it rather
   than by a flake weeks later.
3. **CI needs a display, and today has none.** Both jobs are `runs-on: ubuntu-latest` and `grep -rn
   "xvfb" .github/` returns **nothing**. A headless VS Code on Linux needs `xvfb-run`; that is one
   line, and it is named here because an unnamed prerequisite is how this lands as a red pipeline
   nobody can read.
4. **It is a SEPARATE job, and it is not required to merge — at first.** An extension-host suite
   downloads an editor and launches it: it is slow and it can be flaky for reasons that have nothing
   to do with the change under review. Made required on day one it becomes a tax on every pull
   request, and the pressure is then to weaken it. It runs, it reports, and it is promoted to
   required only after it has been **green on twenty consecutive runs of `main`** — a number, so the
   promotion is a measurement rather than a mood.

> **SHIPPED 2026-09-17, and its first scenario is NOT story 6's — which is a deviation, so here is
> why.** This story says the first conversion is story 6's, watched red before story 6's guard
> exists. Writing it that way means committing a RED test: the guard is in group 4 and this is group
> 3, so the scenario would sit failing in `main` until it landed. This repository does not ship red
> tests, and a scenario that is expected to fail teaches everyone to ignore the job.
>
> So the first scenario is the gap row's own subject instead — **the extension activates and every
> command its manifest declares is really registered** — and the harness earns its keep the way this
> repository proves everything else: by being broken. A phantom command added to the manifest fails
> the run with *“declared in the manifest and never registered, so the menu item does nothing”* and a
> non-zero exit. **Story 6's scenario lands WITH story 6**, where it can go red and green in one
> change.
>
> **What the launch cost, because the plan asked for the entrypoint and this is what it actually
> takes.** A run started from a terminal INSIDE VS Code inherits `ELECTRON_RUN_AS_NODE=1`; the child
> `Code.exe` then behaves as plain Node, runs the first argument as a script, and rejects the rest
> with Node's own `bad option:` wording — a message that names VS Code's binary and says nothing
> about the variable. Two wrong diagnoses were made before the wording gave it away. The launcher
> strips it and nine `VSCODE_*` siblings, and says so in its header.
>
> Also learned: the editor is **1 GB on disk** per version, so `.vscode-test/` is gitignored — it was
> sitting untracked and would have been committed.

### The entrypoint, because “use test-electron” is not a specification

*(codex, round 3: with no launcher named, “on a headless CI runner the editor can fail to launch or
the scenario can be skipped while the optional job remains green” — which is the exact outcome this
story says twice that it must avoid, and did not say how.)*

| | what the story must pin down |
|---|---|
| the script | one npm script, its own `node --test` invocation, never inside the existing batch |
| the launcher | a `runTests` entry point handing `test-electron` the extension path, the workspace fixture and the runner |
| the runner inside the host | `node:test` programmatically, so there is ONE runner in this repository and no mocha |
| discovery | an explicit list of scenario files, not a glob — a glob that matches nothing is a pass |
| the workspace | a temporary folder created per run and deleted after, never the developer's own |
| timeouts | one for the editor launching and a separate one for the scenario, so “VS Code never started” and “the test hung” are different failures with different messages |
| cleanup | the host is killed on every exit path, including a throw, so a failed run leaves no editor behind |
| **the exit code** | **non-zero when the host does not start, when no scenario is discovered, and when a scenario is skipped.** Zero means the scenario RAN |

That last row is the whole story. A harness that exits zero because it found nothing to run is the
green tick over nothing this plan keeps naming.

### The scope discipline, which matters more than the harness

**This story builds the harness and converts ONE row: story 6's.** It does not convert the other
eleven. Two reasons, and the second is the one that will be argued with:

- A harness with twelve scenarios written before any of them has ever caught anything is twelve
  guesses about what is worth driving. One scenario, chosen because a story needed it, is evidence.
- **An extension-host test is not automatically better than the value test beside it.** This
  repository's architecture is decisions-as-values with a thin host layer, and
  `sonarExclusions.test.ts` asserts the host half stays a **small minority** — measured today,
  **33 of 196 modules in `src` import `vscode` directly** (35 cannot load without one, counting
  transitively). If the harness becomes the place to test decisions, that architecture erodes and the
  suite gets slower for nothing. The harness is for what a value CANNOT answer: a control being
  drawn, an event being raised, two hosts contending. Anything a pure function can answer stays a
  pure function.

### The test, which is the harness proving itself

A harness whose first scenario passes on arrival has demonstrated nothing. The first scenario is
story 6's, **and it is watched going red against the unfixed code** — `index.refresh()` rejects, and
the picker on screen still offers the moved names. Then the guard lands and it goes green. That
sequence is the deliverable; the harness is what makes it possible.

Second, and it is the one that stops this being decoration: **the harness is reverted and the
scenario must fail to run at all.** A suite that silently skips when the editor cannot start is worse
than no suite — it is a green tick over nothing, which is the exact shape of the CodeRabbit check
that reported `pass` while rate-limited during this very series.

### What this story also fixes, because it is measuring the same thing

`research/module_tests.md` states **“Seventeen of the hundred and thirty-five modules in `src`”**.
Measured today: `ls *.ts | wc -l` is **196**, and `grep -l "from 'vscode'" *.ts | wc -l` is **33**.
The prose is two counts stale in both numbers — the same defect the parent plan already fixed once
for the Sonar exclusion list, where tooling now measures it instead of a typed number. The row that
describes the gap is rewritten when the gap changes, and the counts beside it become measured rather
than typed.

---

## The six SonarCloud findings on moved lines

The quality gate **passed** on PR #351 — 100 % coverage on new code, no hotspots, no duplication.
Eleven issues were reported and the interesting thing is which:

- **Two were introduced by the split and are already fixed** (`chatLaunch.ts` imported
  `./chatSession` on two lines).
- **Three are the `export let` bindings in `chatHost.ts`**, kept deliberately, with the reason in
  that module's own header: the mutability IS the capability, it is confined to one file with three
  setters, and the alternative changes every call site in fifteen modules to buy the same guarantee.
  **These are not in scope here.** Reopening them means arguing with that paragraph, not with Sonar.
- **Six are pre-existing lines Sonar counts as new because they moved into a new file** — the trap
  this repository has recorded before. They are the scope of this section:

| where | what |
|---|---|
| [chatTurn.ts:219](../src_vs_code/src/chatTurn.ts#L219), `oneTurn` | cognitive complexity 17 against the 15 allowed. Unchanged from `main`. The seam that reduces it is the one the module header already names as the honest next move — take it, rather than splitting the function to satisfy a number. |
| [chatHooks.ts](../src_vs_code/src/chatHooks.ts) | two places that read better as an optional chain. |
| [chatHooks.ts:657](../src_vs_code/src/chatHooks.ts#L657) | `pictureDir(entry.id.toString())` — stringifies as `[object Object]` if anything ever puts it in a template. ~~It does not today; the defect is that nothing stops it.~~ **It DID today** — `ChatEntry.id` is typed `object`, so `toString()` returned `"[object Object]"` for every conversation there has ever been, and every one of their pictures went into one directory where the same turn number overwrote the same file. The row above is kept as written because it is the record of what was believed; this was the only one of the six that was not cosmetic. |
| [chatSessionJoin.ts:88-90](../src_vs_code/src/chatSessionJoin.ts#L88-L90), `resolveAndPin` | the FIRST nested ternary: `own ?? (pinnable(…) ? … : undefined)`. |
| [chatSessionJoin.ts:268-269](../src_vs_code/src/chatSessionJoin.ts#L268-L269) | **the second one, which every earlier draft left unlocated** — `? sessionSourceOf(sessionIdOf(one?.kind === 'one' ? one.file : '')) : kind === 'claude' ? { kind: 'none' } : sourceOfFile(…)`. |

**That last row is a finding the gate earned.** *(codex, round 3.)* The Definition of Done required
six findings closed while this table located five and said “one of the two” about the sixth — a
completion condition nobody could check. It was found by reading `chatSessionJoin.ts` rather than by
re-reading the report.

**The two optional-chain sites in `chatHooks.ts` are deliberately NOT given lines here.** They are
read off the SonarCloud report for the pull request at the time the work is done: four stories
rewrite that file before this group runs, so a line typed today names a different expression by then,
and guessing one is how the wrong thing gets changed.

> **How it went, 2026-09-18.** The instinct was right — the lines HAD moved — but the report it
> pointed at was the wrong one (see the correction below). Both turned out to be the same
> expression, once per copy control: `said === undefined || said.role !== 'model'`, which is
> exactly `said?.role !== 'model'`. Equivalence measured rather than argued: removing the narrowing
> makes `tsc` report `TS18048: 'said' is possibly 'undefined'` at the `said.text` beneath it, and
> restoring it gives exit 0.
>
> **Who closed which.** PR #386 closed four — the `[object Object]` picture directory, which was not
> cosmetic at all, and the nested ternaries in `chatSessionJoin.ts`. PR #387 closed these two, plus
> a **seventh** this table never had: `typescript:S3863`, `'./chatPanel'` imported twice in the same
> file — a leftover of the command split itself. The other ~575 open Sonar issues in this project,
> several of them `S6582` in `claudeCli.ts`, `panelProvider.ts` and `chatPage.ts`, were never in
> this plan's scope and are not claimed by it.
>
> **And the cleanup grew a test, which is the part worth keeping.** The expression stood twice,
> refusal sentence and all, inside `conversationHooks` — which imports `vscode` and therefore runs
> under no test here. The guard it is part of (*is the message at this index still the answer this
> control was pressed on?*) had been added by an earlier code round for a real race and was
> asserted by **nothing**. It is `stillAnswering` in the vscode-free `answerCopy.ts` now, with
> `theAnswerControlCopies` and `theBlockControlCopies` carrying the two controls’ deliberately
> different resolution moments — a shape the gate corrected twice: first for testing the helper
> while leaving the moments unasserted, then for making the two moments interchangeable, where
> swapping them at the call sites would have compiled silently. Swapping them is now `TS2554` at
> both sites, measured by performing the swap. Nine cases behind it; the wiring itself — that each
> hook calls its own — is still only provable in a real editor, and is listed in *Still open*.
>
> A Sonar cleanup billed as *"worth one afternoon"* was the ninth time in this plan that checking a
> premise changed the work.

**What proves the six are gone is not this table** — and it is **not** the SonarCloud comment on
the pull request either, which is what this paragraph used to say. That was wrong, and it nearly
cost the last two findings.

> **Corrected 2026-09-18, by measurement.** A PR's Sonar comment speaks about NEW code. Both
> `chatHooks.ts` sites were pre-existing lines on `main`, so #382, #383 and #385 each reported
> *"0 New issues / 0 Accepted issues"* while both findings sat OPEN the whole time — and two greps
> for the rule's usual shapes found nothing, because neither guessed the form the expression
> actually had. The conclusion nearly drawn was that four rewrites of the file had taken the
> findings with them.
>
> What answers the question is the **project issues API**, which names rule, file and line for
> issues in any state rather than only new ones:
>
> ```bash
> # rules= filters SERVER-side, which is what keeps this honest: the project had 577 open issues
> # and ps caps at 500, so an unfiltered first page can hide the target and read as "closed".
> curl -s "https://sonarcloud.io/api/issues/search\
>   ?componentKeys=<projectKey>&resolved=false&rules=typescript:S6582&ps=500"
> ```
>
> **Check `total` against the number of issues returned before believing an empty result.** The
> unfiltered form first used here returned 500 of 577 and found both targets by luck; the document
> gate caught that before it became the next reader’s false negative. The `files=` and `components=`
> parameters are ignored on this endpoint — filter by rule server-side, or paginate.
>
> **The closure, measured 2026-09-18 against `main` after #387 merged**, with that command:
>
> | rule | `total` | returned | in `chatHooks.ts` |
> |---|---|---|---|
> | `typescript:S6582` | 17 | 17 | **none** |
> | `typescript:S3863` | 15 | 15 | **none** |
>
> `total` equals `returned` in both, so nothing is hiding on a second page — which is the check
> this paragraph exists to demand of itself. The same query before the fix listed `chatHooks.ts`
> lines 531 and 543 for `S6582` and 24 and 28 for `S3863`. The seventeen and fifteen that remain
> are in other files and were never in this plan’s six.
>
> One call, filtered to the rule, named both — `chatHooks.ts` lines 531 and 543, `typescript:S6582`,
> `OPEN`. (Its `files=` / `components=` filters are ignored on this endpoint; fetch and filter
> client-side.)

The table says which six to expect; the **API** says whether they went.

None is a behaviour change and none was introduced by the split. **They are worth one afternoon
together**, as one commit, last — not earlier, because `oneTurn` and `attachPicture` are rewritten by
stories above and a complexity fix landed first would be rewritten twice.

---

## Build order

**The first draft had this wrong, and it had already written down why.** It put the refusals (stories
2, 6, 7) before the progress-and-error surface (stories 8, 9, 10), and then said in its own text that
*"the groups are not reorderable, because group 3 changes the progress handling that group 2's
refusals post into"* — which is an argument for the opposite order than the one it gave. Landing a
refusal against a surface that is then rewritten means every per-PR test before it was green about a
contract that no longer holds. **The surface goes first.** *(codex, the plan round; the single most
useful finding of the round, because it is a contradiction the document contained rather than a risk
somebody imagined.)*

1. **Story 1** — security. Its own pull request, first, alone.
2. **Stories 8, 12, 9, 10** — the progress-and-error surface. Stories 8 and 12 are **one change**: a
   turn abandoned by a reset must neither write into the replacement (8) nor repaint it (12), and both
   are the same identity check applied at two points. Then a scope that closes in a `finally`, then a
   single-entry latch. Everything after this posts into the surface these four settle.
3. **Story 13** — the extension-host harness. *(Shipped differently: its first scenario is NOT
   story 6's, because story 6's could not land here without checking in a failing test. It shipped
   asserting that the extension activates and registers all twenty-two manifest commands. See the
   callout in story 13.)* As written, with story 6's scenario as its first and only
   conversion, watched red before story 6's guard exists. Its own pull request: it touches CI and a
   dependency and nothing else here does.
4. **Stories 2, 6, 7** — the three failures that are currently invisible, now written against a
   surface that has settled, and with story 6's visible half now provable.
5. **Stories 3, 4, 5** — the three data-shaped changes (a supplier, an atomic replace with a sweep, a
   resumable job). **One pull request each**: story 4 and story 5 both grew a persistence contract at
   the gate and neither is a small diff any more.
6. **Story 11**, then **the six Sonar findings**, in that order and one pull request.

Stories inside a group are independent. The groups are not.

## Test plan

**Every story is RED first**, per `.agents/conventions/common/testing.md` — the test is written, run
against the unfixed code, and the failure message is confirmed to describe the real symptom rather
than a setup error. Both observations go in the summary.

That is the point of this plan being separate. The parent series proved *nothing changed*, by
construction, and could not test any of this. Here each item is a behaviour change, so each gets the
test the parent could not write.

**Five stories now have a second test that a naive fix would pass**, and they are what the two
rounds and the consultation earned:

| story | the first test | the one that DECIDES |
|---|---|---|
| 2 | `home.release` ran | the session was asked to stop, so the child does not outlive the tab |
| 8 | `ended` returned within its budget | an old save resolving afterwards leaves the replacement's `rev` untouched |
| 12 | the warning is still on the console | the replacement's `running` and its page are untouched |
| 10 | progress was shown before the await | two presses during one probe make ONE capture |
| 4 | the old image survives a failed write | it survives a failed RENAME, and the sweep spares a `.tmp` younger than `DEBRIS_AGE_MS` |

The first test only ever proves the obvious half. **Story 4's second test changed at the
consultation** — it used to assert that a persisted reference survived a crash, and there is no
persisted reference, so it would have passed by doing nothing at all.

Two constraints from the parent carry over:

- **`chatHooks.ts` is 670 lines** and four stories touch it. Every fix that wants a pure half must
  **extract a named unit** (`coding-style.md:23-24`), never widen the file. If it crosses 800, the
  extraction is the work.
- **No new source-text assertions.** `.coderabbit.yaml` and `.agents/PROJECT.md` already forbid them,
  and [PLAN_the_page_tests_run_the_page.md](../todo/PLAN_the_page_tests_run_the_page.md) is the backlog for
  the 224 that exist. A test here that reads a file's text instead of running it is a twelfth.

The whole suite runs before each pull request — `npm test` (**3 377** on 2026-09-17, and rising
under a parallel line of work, so read it rather than quoting this) plus the pre-run (37 in one
batch, then `theBundleLoads.test.mjs` alone, 39 in all) plus the family checks, which `npm test` does
**not** include.

**The bundle test runs alone on purpose, and a case in it asserts that it still does.** It shells out
to `npm run bundle`, whose `prebundle` hook invalidates `src/generated/gateRule.ts` before verifying
the pinned conventions — measured absent for 2 047 ms on one bundle here. `node --test` runs its files
in parallel processes, so beside a test that walks `src/` that window is an `ENOENT` at random. Any
new test that drives the build gets its own invocation.

**A green suite is not evidence until the artefacts are fresh.** *(codex, round 3, and this
repository has already been bitten by it: after a rename, a stale `out/` ran BOTH names and the count
silently inflated.)* TypeScript can emit despite errors and a blocked build can leave yesterday's
JavaScript in place, so `npm test` reports green against the old implementation while the changed
source is broken. Every recorded suite result in this plan is taken from a run that began with a
cleaned `out/`, and the compiler's exit status is authoritative over the runner's.

### What each story proves, and what it does NOT

*(codex, round 3: unit tests can pass while the command wiring, the host state or the UI is wrong for
the sequence a person actually performs. The answer is not to convert everything — see story 13's
scope discipline — it is to say per story what is left unproven, so nobody reads a green suite as a
guarantee it never made.)*

| story | proved as a value | proved end to end | NOT proved, and the risk retained |
|---|---|---|---|
| 1 | containment, all four cases | — | that the refusal REACHES the person; the open is host-side |
| 2 | the release ran, the tree died | — | a real vendor CLI's own fork behaviour under a thrown dispose |
| 3 | the supplier is not called on success | n/a | nothing — this one is a pure function |
| 4 | write, rename and sweep | — | a genuinely full disk, which no test here creates |
| 5 | width, bound, cadence, two workers | — | ten thousand real records; the tests use a counting clock |
| 6 | the refusal is raised | **yes, story 13's scenario** | — |
| 7 | two failures classified apart | — | that the message is legible to somebody who did not write it |
| 8 | budget, `stop()`, the write fence | — | that a remote vendor really stopped; `stop()` cannot confirm |
| 9 | the scope spans the write, and rejects | — | the indicator as VS Code actually draws it |
| 10 | one capture from two presses | — | a real keypress reaching the command |
| 11 | the cycle is gone | n/a | nothing — the ratchet is the proof |
| 12 | the replacement is untouched | — | a real turn resolving after a real reset |

**Ten of the twelve rows have a gap in the third column, and that is the honest state of this
repository** rather than a failure of this plan: `research/module_tests.md` carries twelve rows
saying the same thing. Story 13 closes one. Each later story adds its own row there, with its reason,
rather than leaving the map to say nothing about work that has shipped.

## Definition of Done

Every box below is ticked against something that was run, and where a number exists it is the number
rather than the word.

- [x] Story 1 shipped **first and alone**, with the escape reproduced red and refused green, and the
      sibling-directory case (`/ws-secret` beside `/ws`) asserted rather than assumed — five cases in
      `symlinkEscape.test.ts`.
- [x] Story 1's residual race is **written into the code's own header**, beside `insideReally`.
- [x] Stories 2–12 shipped in the five groups above, each with its own RED observation recorded, and
      **stories 4, 5 and 8 partial by decision** — see *Still open* for the half each left and the
      measurement behind it. Every defect the story named was fixed; what was not built in each case
      is a second piece of work the measurement said not to buy yet. And
      each of the **five** second tests present and observed failing against a naive fix — stories
      2, 4, 8, 10 and 12. The count said four until the document gate counted the table.
- [x] Story 8's fence proved on the WRITE path, not only the turn path.
- [x] Story 12's fix leaves the console warning in place.
- [x] Nothing was re-implemented that the repository already has. Story 4 EXTENDED `atomicFile`,
      story 5 USED `abreast` and `refile`, story 8 CALLED `ChatSession.stop`. Where the Sonar group
      added `stillAnswering`, `theAnswerControlCopies` and `theBlockControlCopies`, it widened
      `answerCopy.ts` — the vscode-free module that already held `answerToCopy` and `blockToCopy` and
      already had a test file — rather than starting anything new.
- [x] Story 13's harness was proved twice.
- [x] Story 13 converted exactly ONE row of `research/module_tests.md`; the other eleven still say
      what they do not cover.
- [x] `@vscode/test-cli` was NOT added — zero occurrences in `src_vs_code/package.json`; the
      repository still has one test runner.
- [x] The extension-host job is not required to merge (`continue-on-error: true`), and the promotion
      rule — **twenty consecutive green runs of `main`** — is written in the workflow beside the job,
      not only here. It passed in 39 s on the last pull request of the series.
- [x] The stale counts in `research/module_tests.md` are gone — zero occurrences of “seventeen of the
      hundred and thirty-five”; tooling measures it now.
- [x] The six Sonar findings on moved lines closed — four in #386, two in #387, plus a **seventh**
      (`S3863`) found beside them. `chatHost.ts`'s three `export let` reports left standing with their
      reason. **And the method of checking was itself corrected**: a PR's Sonar comment reports on NEW
      code and said “0 New issues” three times while two findings were open; the project issues API
      is what answers the question.
- [x] `importCycles.test.mjs`'s `KNOWN` ratchet is **eight entries** — counted — and
      `chatModels ↔ chatPage` is not among them, with no re-export left behind (story 11).
- [x] No module this plan touched crossed 800 lines: `chatHooks.ts` **732** (it shrank),
      `answerCopy.ts` **215**, and every module extracted in the series is well under. The eight files
      that were already over the ceiling are listed in *Still open* and were out of scope by this
      plan's own statement.
- [x] No new source-text assertion was added — the nine cases behind the copy guard assert behaviour,
      including “both controls refuse in the same words”, which was written that way deliberately
      instead of counting a string literal in the source.
- [x] The coai gate ran on each pull request — `review_plan` to `proceed` (or `good_enough` with every
      finding resolved), then `review_code`. The last round: 12 reviewers, 10 findings, 2 accepted,
      8 rejected with reasons, verdict `proceed`.
- [x] **Every touched module's documentation updated, not just two files.** The mapping, per pull
      request: stories 1, 2, 4, 12 → `module_extension.md`; stories 3, 5 → the store's own module
      doc; story 13 → `module_tests.md`, whose gap row it rewrites; the Sonar group → both; every
      story → its row in `module_tests.md`'s flow table, including the “NOT proved” column.
- [x] `research/architecture.md` and its Mermaid diagrams regenerated where cross-module interaction
      changed.
- [x] **The boundary table exists in BOTH directions** (below), and the other two plans gained their
      half in the same change.
- [x] This plan promoted to `research/` with its deviations recorded — *What shipped differently* and
      *Still open*, both below.

## The boundary with the two plans beside this one

*(codex, round 3: a reader starting from either of the other plans sees no statement of which stories
this one owns, so they can rebuild the same work or modify the same surface under contradictory
assumptions. `planning-docs.md` asks for a boundary to be written into the OLDER document too — this
plan had a pointer in the parent and nothing in the page-tests plan.)*

| item | owned by | the other part | order | disjoint? |
|---|---|---|---|---|
| the fifteen modules, and that they changed nothing | `PLAN_the_command_file_is_too_big.md` (IMPLEMENTED) | this plan fixes the defects that series was forbidden to touch | parent first, done | yes |
| how the WEBVIEW PAGE is tested; the 224 source-text assertions | `PLAN_the_page_tests_run_the_page.md` | this plan adds no page tests and converts none | independent | **yes — and an earlier draft wrongly said story 6 depended on it** |
| the EXTENSION-HOST harness | **this plan, story 13** | neither other plan owns it; `module_tests.md:175` records the gap | before story 6 | yes |
| the twelve “needs an extension host” rows | `module_tests.md` records them; this plan converts ONE | the other eleven stay, each with its reason | after story 13 exists | yes |

**Both other documents gain their half of this table in the same change**, or the boundary is legible
from one direction only — which is how the same work gets built twice.

## What the gate rejected, and on what measurement

Eleven rejections across two rounds, each on something checkable rather than on taste. Rejecting is
not rudeness to a reviewer: an accepted finding rewrites the plan, so accepting a wrong one hands the
next round fresh text to object to and the count never falls.

### Round two — six, and two of them were about a different document

**Two findings quoted text this plan does not contain.** They described a promotion sequence —
*“the `git mv` goes LAST”*, an edit-then-move ordering, a `git add` that might fail — and analysed how
it breaks. Measured: `grep -c` over this file returns **0** for `git mv`, **0** for `goes last`
(case-insensitive), **0** for `git add` and `git commit`. Promotion appears once, as a Definition-of-
Done checkbox. The reviewer critiqued `planning-docs.md` or the promote-plan skill and attributed it
here. A finding about the failure mode of a procedure a document does not contain cannot be applied
to it, however sound the reasoning is about the procedure.

**Two more were the same finding, byte for byte, from one provider** — *“story 11 risks breaking
imports if the new module is not created first”*, twice. Both rejected for one reason: a missing
export or a misplaced module is caught by `tsc`, which `npm test` runs before any suite. A plan step
instructing you to verify that a file you were just told to create exists is ceremony a compiler
already performs.

**One was re-raised from round one unchanged** — the `importCycles` ratchet described as a false
positive. The tool's own rule is that a reasoned rejection is discounted unless a reviewer brings a
genuinely new argument, and none came. A ratchet going red when the cycle is removed without its
`KNOWN` entry is the mechanism, not a malfunction.

**One asserted a mechanism it could not locate** — that story 9's indicator might not be visible
because “the UI update is not synchronised with the scope closure”. Story 9's defect and its fix are
both about WHEN the scope closes relative to `await thread.writes`, and the claim names no call, no
file and no API where a different desynchronisation would occur. Accepting it would commit the story
to a problem nobody can point at.

### Round one — five

- *"Story 11 risks a circular dependency if the test file is updated separately."* —
  `importCycles.test.mjs` is in this repository and the plan already requires the `KNOWN` entry to be
  deleted in the commit that makes it untrue. The failure described is what a ratchet is **for**: it
  fails if the cycle survives, and it fails if the entry is removed while the cycle survives.
- *"Story 8's timeout may be too short for a slow CI environment; make it configurable per
  environment."* — `ended` runs in the extension host on a person's machine; nothing in CI awaits
  `thread.turns`. The real gap beside this — what the expiry does to the operation it abandons — was
  **accepted twice** and is now the substance of story 8.
- *"Story 8's terminal state forces a window reload; add retry/cancel."* — inverted. The forced reload
  is what happens **today**, because `ended` awaits two promises that may never settle; the terminal
  state is what removes it.
- *"Story 2's `release` may hang on a network timeout."* — `home.release()` releases a **local**
  temporary directory made by `emptyTempDir` in `chatLaunch.ts`. There is no network in that path.
  The real leak beside it was accepted as the process-tree finding.
- *"Story 9's indicator might disappear before the write completes."* — that is the defect story 9
  removes, restated as a risk of removing it. The genuinely uncovered case, a write that **rejects**
  after the scope was extended, was accepted and is in the story.

## What shipped differently

The plan was written before any of it was built, and nine of its premises turned out to be false when
the code was read. Eight of those made the work **smaller**, which is worth recording as plainly as
the one that made it bigger — an estimate that is wrong in one direction eight times out of nine is
not an estimate, it is a habit of assuming the defect is there because the plan says so.

| the plan said | the code said |
|---|---|
| story 7 needs a classification written | it already had one |
| story 8 is a story | two thirds of it was already done |
| story 2 has four wrong sites | one of the four was already correct |
| story 5's rename takes hours | 58 seconds, measured |
| story 4 must fix an encoding bug (`'utf8'` mangles a Buffer write) | **there was no bug.** The claim was the plan's own, measured false: the branch written for it was removed and the test's stated reason corrected. Nothing in the code was wrong; the plan was. |
| four comments describe the behaviour | they described behaviour the code does not have |
| Sonar's `[object Object]` is latent, *"it does not today"* | it did today: **every** conversation's pictures were in one directory, because `ChatEntry.id` is typed `object` |
| a PR's SonarCloud comment proves a finding closed | it speaks about NEW code — three PRs said *"0 New issues"* while two findings sat OPEN on `main` |
| the six Sonar findings are *"worth one afternoon"* | they were, until the last two turned out to sit in front of a guard no test could reach |

**Three stories are PARTIAL by decision** — 4, 5 and 8 — each having shipped its defect fix and
left a named half unbuilt on a measurement. The table in *Still open* says which half and why. It
is repeated in both places on purpose: the first version of this record said “all thirteen stories
shipped” and put the unbuilt halves only in the open list, which is exactly the shape this plan
opens by criticising in its own parent — *a named gap in a shipped plan reads as done to everybody
who was not in the room*. Two reviewers refused it independently.

Three deviations of shape rather than of fact:

- **Story 13's harness shipped with a scenario that is not story 6's**, which the story's own record
  explains: the first scenario had to be one that could be watched failing for a reason belonging to
  the harness rather than to the feature, so it asserts that the extension activates and registers all
  twenty-two manifest commands.
- **The Sonar group grew a thirteenth story's worth of test.** It was planned as one commit of
  cosmetic edits. Two of the six sat inside `conversationHooks`, which imports `vscode` and therefore
  runs under nothing here, in front of a race guard an earlier code round had added and that was
  asserted by no test at all. Extracting it was the only way to satisfy `testing.md` §2, and the gate
  then corrected the extraction twice — once for leaving the two controls' resolution moments
  unasserted, once for making those moments interchangeable, where swapping them at the call sites
  would have compiled in silence.
- **A seventh Sonar finding was closed that the table never had** — `typescript:S3863`,
  `'./chatPanel'` imported twice in `chatHooks.ts`, a leftover of the command split itself.

## Still open

Recorded here rather than left implied, and none of it is claimed by this plan.

**Three stories are partial by decision**, and the document gate was right that calling them
“shipped” without saying so was the same defect this plan opens by describing. Each shipped its
defect fix; each left a named half unbuilt, on a measurement:

| story | what shipped | what did NOT, and why |
|---|---|---|
| **4** — a picture deleted before its replacement is written | the destructive order reversed, atomically | **Retention for the `pictures/<id>` tree.** Measured: it is `coaiDataDir()/pictures/<id>`, persistent data rather than temp, with nothing anywhere removing it — so the fix is not a wider store sweep but a new policy with a real decision in it (collectable when the conversation is deleted, or after an age? run by whom?). Not invented inside a story about a destructive replace. |
| **5** — a folder rename refiles records one at a time | `abreast` at width 8, plus stories 6 and 7’s notice | **The persisted, resumable rename job** — schema, heartbeat, fencing token, recovery owner. Measured at 58 seconds for the whole job, ten with the pool, against the “hours” the plan assumed. A rename interrupted mid-way still leaves records split and nothing revisits them; the exposure is ten seconds rather than hours, which is why it is recorded rather than engineered around. **Re-measure BEFORE building the lease if the store grows an order of magnitude** — that is the whole lesson. |
| **8** — a reset can wait for ever | the fence, on the turn path and the write path | **A budget on `await thread.writes`.** Deliberately not bolted on: abandoning that wait is how a save is lost, so it needs its own design rather than a number. An archive reset can still hang indefinitely on a slow or remote store. |

And the rest:

- **A second containment implementation, `dataCommands.ts:628`.** A private `isInside` with its own
  case-folding and separator logic, injected as a parameter at `dataCommands.ts:450` — which is why a
  grep for `isInside(` misses the call. Story 1 established that it is **not a hole** (the path comes
  from a person choosing a data directory, not from a model’s answer), so it was correctly out of that
  story’s scope — but two implementations of one containment rule is drift waiting to happen, and it
  belongs in this list rather than only in story 1’s sweep table. *(gemini, the document round.)*
- **The copy controls' WIRING.** That `onCopyAnswer` calls `theAnswerControlCopies` and `onCopyBlock`
  calls `theBlockControlCopies` is proven by no test. Swapping the two is a compile error now
  (`TS2554`, measured), so it cannot happen by accident — but a type is not a test, and only a real
  press in a real editor closes it. That is story 13's harness's ground.
- **A retention policy for the pictures tree.** One directory per conversation makes a sweep possible;
  nothing sweeps. The old header claimed the tab's closing removed it, which was the fourth false
  comment found here.
- **A budget for `await thread.writes`.** Deliberately not bolted on, for the reason recorded at the
  story: abandoning that wait is how a save is lost.
- **`chatGotoCommand`, `chatStoreCache` and `bugzReviewPanel` still hold their own latches** where
  `oneAtATime` would do.
- **`ChatMessage` still lives in `chatPage.ts`.** Moving it breaks no cycle — measured, five
  importers, none imported back by the page — so it buys tidiness rather than a ratchet drop and was
  left as its own change.
- **The extension-host job’s PROMOTION to required.** The rule is twenty consecutive green runs of
  `main`. When this was written nobody was counting and no figure was given, because none had
  been measured — *(codex, the document round)*. **A machine counts it now**, 2026-09-18:
  `src_vs_code/scripts/host-job-streak.mjs` asks the Actions API for this job's conclusion in
  each run of `main`, newest first, and the CI job prints the streak into its own summary on
  every run, including the run that just broke one. First measured answer: **13 of 20**. It
  REPORTS rather than gates — promotion means editing branch protection, which is the
  operator's to do; what has changed is that the number can no longer be unknown. The counter
  treats a run still in flight as ENDING the streak rather than skipping it, so it can never
  read twenty while the newest run is failing.
- **The eleven remaining rows of `research/module_tests.md`** that still end *"NOT covered … needs
  an extension host"*. Story 13 converted exactly one, on purpose: a harness that grew twelve
  scenarios before one of them caught anything has been built on guesses.
- **The eight files over the 800-line ceiling**, below.

## Footnotes from the parent's tail, resolved rather than carried

Three items in the parent's tail were re-measured while writing this and are **not open work**:

- *"Three test files carry their own copy of `bodyOf`"* — done in the series itself;
  `src/test/sourceReading.ts` is the shared helper. One private copy remains in
  `noticesDoNotBlock.test.ts`, which reads a different thing.
- *"`chatFreshWiring.test.ts` has mixed line endings"* — measured: the committed blob is uniformly
  CRLF with one bare CR, on `main` and on the branch alike, and the series' diff to it is 53/19
  rather than 300/300. It is a **hazard note for whoever edits it next** (edit a line at a time; read
  `git diff --numstat` before believing the edit), not a defect.
- *"The prose beside the Sonar exclusion list states a count"* — `scripts/`-side tooling measures it
  now instead of trusting a typed number.

**Still open and deliberately NOT in this plan:** the ceiling. Measured in `src_vs_code/src` on
2026-09-17, after the split landed:

| file | lines | over 800 by |
|---|---|---|
| `panelProvider.ts` | 3 762 | 4.7× |
| `panelView.ts` | 2 797 | 3.5× |
| `chatPage.ts` | 2 243 | 2.8× |
| `roundsLog.ts` | 2 061 | 2.6× |
| `extension.ts` | 1 332 | 1.7× |
| `claudeSessions.ts` | 1 152 | 1.4× |
| `chatStoreFile.ts` | 1 019 | 1.3× |
| `dataCommands.ts` | 808 | 1.01× |

**Eight, not the six the parent's table implied, and every shared row has grown** — `panelProvider.ts`
was 3 095 when that plan opened and is 3 762 now, `roundsLog.ts` 1 989 against 2 061, `extension.ts`
1 215 against 1 332. Two files crossed the line in the meantime (`claudeSessions.ts`,
`dataCommands.ts`, the latter by eight lines). The parent's figures were measured when it opened and
are quoted here only to show the direction: **the ceiling is being crossed faster than it is being
walked back**, and one 4 183-line file returning to 543 did not change that.

**Re-measured one day later, 2026-09-18**, because a claim about a direction can be checked and a
day is the shortest honest interval: `panelProvider.ts` **3 762 → 4 022** (+260), `chatPage.ts`
2 243 → 2 350, `panelView.ts` 2 797 → 2 855, `extension.ts` 1 332 → 1 370, `claudeSessions.ts`
1 152 → 1 188. One fell — `roundsLog.ts` 2 061 → 2 032 — and two were untouched. Net **+470 lines
above the ceiling in twenty-four hours**, against the 3 640 that one deliberate split removed over
a week. The direction held.

Each is its own split, on the parent plan's model, one file at a time — and the parent is the record
of what one costs: thirteen commits, two whole-series failures, and six checks built to prove that
nothing moved.
