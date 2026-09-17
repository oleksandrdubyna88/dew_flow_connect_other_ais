# PLAN — the eleven defects the split was not allowed to fix

> Status: **plan only, nothing implemented yet, 2026-09-17.**
>
> **Scope — every module a story below MODIFIES, not only the ones it quotes.** Nine of them came
> out of [PLAN_the_command_file_is_too_big.md](../research/PLAN_the_command_file_is_too_big.md):
> `chatHooks.ts`, `chatFollow.ts`, `chatArchive.ts`, `chatLaunch.ts`, `chatTurn.ts`,
> `chatPersist.ts`, `chatCapture.ts`, `chatSessionJoin.ts`, `chatThread.ts`. Three predate it and
> are reached anyway: `chatStoreWrite.ts` (story 3 changes `nextAfterSave`, which lives there rather
> than in `chatPersist`), `chatPage.ts` and `chatModels.ts` (story 11 moves a type out from under
> both). One is new: `chatContracts.ts`.
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
> Related: [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) — the other
> standing tech-debt plan over the same files. It governs how the page is TESTED; this one governs
> what the host DOES. They meet in story 6, which needs a scenario in that plan's running-page
> harness rather than a twelfth source-text assertion.

## The goal

Eleven defects and six analyser findings, all of them **pre-existing on `main`**, all of them read
line by line while the file was being carved up. They were found by the coai gate's code round over
the split (25 findings, 17 accepted) and by SonarCloud on the pull request.

One of them is a security defect and should not wait for the rest.

Nothing here is a regression of the split. Each item names the file and line it lives at today, so
the first thing any story does is re-read that line — the parent series moved these lines once, and
a line reference in a plan is worth what it is verified against.

**This plan went through its own gate round on 2026-09-17** — three reviewers, 23 findings, 18
accepted and 5 rejected with measurements. What they changed is marked in place, because a plan that
hides its own revision is a plan whose reasoning cannot be checked. The largest change is the build
order: it was **wrong**, and in a way the first draft had already written down without noticing.

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
kills the process tree through the shared launcher rather than being logged and left. Both errors are
reported, not one swallowed by the other (`coding-style.md`: never silently swallow errors).

**The test.** A thread whose `session.dispose` throws. Red: `home.release` was not called and the
child is still alive. Green: the release ran, the tree was killed, and both facts are asserted — the
second is the one a reordering fix would fail.

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

**Three things the gate added, and the first is why "temp-and-swap" is not a fix by itself:**

1. **The swap must be atomic, which means same-filesystem.** A rename is atomic only when source and
   destination share a filesystem. Writing the temporary file into a temp directory and renaming it
   into the picture directory is a **copy** across a boundary, which can fail halfway. The temporary
   file goes in the **destination directory**, under a neighbouring name. *(local, the plan round.)*
2. **The stored reference moves before the old file goes.** If the thread's in-memory and persisted
   state still names the old picture after it is deleted, every later read is a file-not-found — and a
   crash between the two leaves a conversation pointing at nothing. Record the new path first, delete
   second. *(gemini, the plan round.)*
3. **An interrupted attempt leaves an orphan, and somebody owns it.** A process killed between the
   write and the swap leaves a temporary file for every attempt. A bounded sweep of abandoned
   temporaries runs where the pictures are already managed — the per-conversation directory whose
   lifetime `pictureDir` already makes a contract — with a stated retention. *(codex, the plan
   round.)*

**The test.** A write that fails: red, the old image is gone; green, it is still attached and the
failure is reported. A rename that fails after the write: green, the old image is still attached and
no orphan survives the sweep. A crash between the write and the swap, simulated: the stored reference
still names a file that exists.

---

## Story 5 — a folder rename refiles conversations one at a time

**Where:** [chatFollow.ts:45](../src_vs_code/src/chatFollow.ts#L45), `followRenames`, over the walk
at [chatFollow.ts:71](../src_vs_code/src/chatFollow.ts#L71).

**The symptom.** Strictly sequential, each record with up to five 200 ms retries. Ten thousand
records is **hours**, with nothing on screen and a stale picker until the final refresh.

**The fix.** Bounded concurrency — a small fixed width, not unbounded `Promise.all`, because the
retries exist for a store that is already contended.

**Progress that moves while it works, not at the end.** An indicator that only updates when the batch
finishes is the same silence in a different colour: report every N records. *(local, the plan round.)*

**And it must be resumable, which is the finding that changes the shape of the story.** A rename
interrupted after 4 000 of 10 000 records leaves the rest in the old location **and the rename event
is gone**. Nothing revisits them, so the conversations stay split between two folders and the picker
stays wrong for ever — a worse end state than the slow one this story is fixing. The work is
therefore a **persisted, idempotent job**: it survives a restart, it resumes rather than restarting,
and a partial failure is reported rather than dropped. Reconciliation at the next startup or the next
follow is what makes "resume" true rather than hoped for. *(codex, the plan round.)*

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

**The test, and this is the one the gate made harder.** A unit test over the callback can pass while
the bundled page renders nothing, which leaves exactly the stale picker the story claims to fix — a
test that proves the call was made, not that anybody was told. So this story needs a scenario in the
**running-page harness** of
[PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md): reject `index.refresh`,
assert the rendered refusal and the stale-state message, and catalogue the flow in
`research/module_tests.md`. *(codex, the plan round.)* **This is the one dependency this plan has on
another plan** — if that harness is not ready, the story waits rather than settling for the weaker
test.

---

## Story 7 — `follow` cannot tell a busy store from a fatal one

**Where:** [chatFollow.ts:118](../src_vs_code/src/chatFollow.ts#L118).

**The symptom.** A permission failure is retried five times and then reported exactly as a lock is.
A lock clears; a permission does not, so the retries are a second of waiting that was never going to
help, and the person is told nothing either way.

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

**A budget alone makes the UI honest and the process worse, which two reviewers found independently.**
When the wait expires and `ended` returns, the turn it stopped waiting for is **still running**: the
vendor process keeps its memory and its handles, and its late completion can still write — into a
thread that has since been replaced, which is a corruption the original hang could not produce.
*(gemini and codex, the plan round.)*

**The fix, in three parts, none of them optional:**

1. A budget on each wait, and a terminal state when it expires that names which one did not finish —
   CLAUDE.md §8: never stick on an in-flight state.
2. The abandoned operation is **cancelled and its process tree killed**, through the shared launcher,
   not left to run.
3. Any completion that arrives after the budget is **refused rather than applied**, so a late turn
   cannot write into the conversation that replaced it. Where a kill is not possible, the turn is
   quarantined for the existing orphan sweep, which is machinery this repository already has.

**The test.** A thread whose `turns` never settles. Red: `ended` never returns. Green: it returns
within the budget with a reason naming the turn, **the process was killed**, and a completion
delivered afterwards changes nothing in the replacement thread. The last two are what a
timeout-only fix fails.

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

- [ ] `ChatModelChoice` and `ChatMessage` live in `chatContracts.ts` and nowhere else.
- [ ] All four importers updated: `chatPage.ts`, `chatConfig.ts`, `chatModels.ts`, `chatThread.ts`.
- [ ] `src/test/chatPage.test.ts:8` — which imports `ChatModelChoice` from `../chatPage` — updated
      too; the tests are importers like any other.
- [ ] No re-export left behind in `chatPage.ts`. A convenience re-export keeps the edge and the
      cycle, and the ratchet would then be lowered by a commit that changed nothing.
- [ ] A search for both names returns no import from `./chatPage` anywhere.

**The test.** `importCycles.test.mjs` is already the check, and it is a ratchet that may only fall:
`KNOWN` goes from nine entries to eight, and `chatModels ↔ chatPage` is deleted from the list in the
same commit that makes it untrue. That is an assertion, not a claim — the test fails if the cycle is
still there and it fails if the entry is removed while the cycle survives.

**Sequenced last because it is the only item here that is not a defect** — nothing misbehaves today.
It is also the only one whose benefit a test states out loud.

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
| [chatHooks.ts:657](../src_vs_code/src/chatHooks.ts#L657) | `pictureDir(entry.id.toString())` — stringifies as `[object Object]` if anything ever puts it in a template. It does not today; the defect is that nothing stops it. |
| [chatSessionJoin.ts:88-90](../src_vs_code/src/chatSessionJoin.ts#L88-L90), `resolveAndPin` | a nested ternary (`own ?? (pinnable(…) ? … : undefined)`), one of the two Sonar reports there. |

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
2. **Stories 8, 9, 10** — the progress-and-error surface: a budget that also stops what it abandons, a
   scope that closes in a `finally`, a single-entry latch. Everything after this posts into it.
3. **Stories 2, 6, 7** — the three failures that are currently invisible, now written against a
   surface that has settled. Story 6 additionally waits on the running-page harness.
4. **Stories 3, 4, 5** — the three data-shaped changes (a supplier, an atomic replace with a sweep, a
   resumable job). **One pull request each**: story 4 and story 5 both grew a persistence contract at
   the gate and neither is a small diff any more.
5. **Story 11**, then **the six Sonar findings**, in that order and one pull request.

Stories inside a group are independent. The groups are not.

## Test plan

**Every story is RED first**, per `.agents/conventions/common/testing.md` — the test is written, run
against the unfixed code, and the failure message is confirmed to describe the real symptom rather
than a setup error. Both observations go in the summary.

That is the point of this plan being separate. The parent series proved *nothing changed*, by
construction, and could not test any of this. Here each item is a behaviour change, so each gets the
test the parent could not write.

**Four stories now have a second test that a naive fix would pass**, and they are the ones the gate
earned: story 2 (the tree was killed, not just the release ordered), story 8 (a late completion
changes nothing), story 10 (two presses make one capture), story 4 (the stored reference survives a
crash between the write and the swap). Where a story has such a test, **it is the one that decides**
— the first test only proves the obvious half.

Two constraints from the parent carry over:

- **`chatHooks.ts` is 670 lines** and four stories touch it. Every fix that wants a pure half must
  **extract a named unit** (`coding-style.md:23-24`), never widen the file. If it crosses 800, the
  extraction is the work.
- **No new source-text assertions.** `.coderabbit.yaml` and `.agents/PROJECT.md` already forbid them,
  and [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) is the backlog for
  the 224 that exist. A test here that reads a file's text instead of running it is a twelfth.

The whole suite runs before each pull request — `npm test` (3 343 tests) plus the pre-run (37 in one
batch, then `theBundleLoads.test.mjs` alone, 39 in all) plus the family checks, which `npm test` does
**not** include.

**The bundle test runs alone on purpose, and a case in it asserts that it still does.** It shells out
to `npm run bundle`, whose `prebundle` hook invalidates `src/generated/gateRule.ts` before verifying
the pinned conventions — measured absent for 2 047 ms on one bundle here. `node --test` runs its files
in parallel processes, so beside a test that walks `src/` that window is an `ENOENT` at random. Any
new test that drives the build gets its own invocation.

## Definition of Done

- [ ] Story 1 shipped **first and alone**, with the escape reproduced red and refused green, and the
      sibling-directory case (`/ws-secret` beside `/ws`) asserted rather than assumed.
- [ ] Story 1's residual race is **written into the code's own header**, not only into this plan — the
      next reader must find the reason, as `realOf` does one module over.
- [ ] Stories 2–11 shipped in the five groups above, each with its own RED observation recorded, and
      each of the four second tests present and observed failing against a naive fix.
- [ ] The six Sonar findings on moved lines closed, and `chatHost.ts`'s three `export let` reports
      left standing with their reason.
- [ ] `importCycles.test.mjs`'s `KNOWN` ratchet is **eight entries, not nine** — `chatModels ↔
      chatPage` deleted in the commit that makes it untrue, with no re-export left behind (story 11).
- [ ] No module crossed 800 lines; any that approached it was extracted rather than widened.
- [ ] No new source-text assertion was added.
- [ ] The coai gate ran on each pull request — `review_plan` to `proceed`, then `review_code`.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated with every change.
- [ ] This plan promoted to `research/` when the last group lands, with its deviations recorded.

## What the gate rejected, and on what measurement

Five of the 23 findings were rejected in round one rather than accepted to be agreeable — an accepted
finding rewrites the plan, so accepting a wrong one makes the next round worse, not better.

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

Each is its own split, on the parent plan's model, one file at a time — and the parent is the record
of what one costs: thirteen commits, two whole-series failures, and six checks built to prove that
nothing moved.
