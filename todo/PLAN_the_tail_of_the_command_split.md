# PLAN — the eleven defects the split was not allowed to fix

> Status: **plan only, nothing implemented yet.** Scope: `chatHooks.ts`, `chatFollow.ts`,
> `chatArchive.ts`, `chatPersist.ts`, `chatCapture.ts`, `chatTurn.ts`, `chatSessionJoin.ts` and
> `chatPage.ts` — the modules
> [PLAN_the_command_file_is_too_big.md](../research/PLAN_the_command_file_is_too_big.md) created.
>
> **Extracted from that plan when it was promoted on 2026-09-17.** Everything here is recorded in its
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
> what the host DOES. Where they meet (a fix below that wants a page-level test) this plan defers to
> that one's harness rather than adding a twelfth source-text assertion.

## The goal

Eleven defects and six analyser findings, all of them **pre-existing on `main`**, all of them read
line by line while the file was being carved up. They were found by the coai gate's code round over
the split (25 findings, 17 accepted) and by SonarCloud on the pull request.

One of them is a security defect and should not wait for the rest.

Nothing here is a regression of the split. Each item names the file and line it lives at today, so
the first thing any story does is re-read that line — the parent series moved these lines once, and
a line reference in a plan is worth what it is verified against.

---

## Story 1 — a symlink can escape the workspace (SECURITY, do this first)

**Where:** [chatHooks.ts:581-589](../src_vs_code/src/chatHooks.ts#L581-L589), `openWorkspaceFile`,
reached from the page at [chatHooks.ts:482](../src_vs_code/src/chatHooks.ts#L482).

**The symptom.** A model's answer can carry a link, and following it opens a file. The containment
check is `isInside(folder.uri.path, target.path)` — **lexical only**, a string comparison over the
path as written. Two lines later `stat` and `showTextDocument` are called, and **both follow
symlinks**. So a workspace containing `docs/secrets -> /home/me/.ssh` passes the check (the path as
written is inside the folder) and opens the file outside it. The input is a model's output, which is
the definition of untrusted.

**Why it is real rather than theoretical.** The repository already treats this exact shape as a
threat one module over: `claudeSessions.ts:974` canonicalises with `await fs.realpath(target)`
before it compares. Two containment checks over model-reachable paths, one hardened and one not.

**The fix.** The one `claudeSessions` already uses: resolve **both** sides with `fs.realpath` and
compare the canonical forms, then open the canonical target — never the path as written, or the
check and the open are answering about two different files (a TOCTOU the realpath does not close on
its own). A target that cannot be canonicalised (it does not exist) is refused, not guessed.

**The test (RED first).** A temporary workspace with a symlink pointing outside it, and a request to
open through that link. Red on today's code: the outside file's content comes back. Green after: the
refusal, with the reason. Then the honest case — a real file inside the workspace, and a symlink
that stays inside — because a containment check that refuses everything also passes the first test.

**Ship it on its own.** One file, one function, its own pull request, ahead of stories 2–11.

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

**The fix.** Each cleanup gets its own `try`, with the release in a `finally`. Neither may prevent
the other; a throw from either is logged rather than swallowed (`coding-style.md`: never silently
swallow errors).

**The test.** A thread whose `session.dispose` throws. Red: `home.release` was not called. Green: it
was, and the throw was reported.

---

## Story 3 — `keepOnDisk` allocates the whole transcript on every save

**Where:** [chatPersist.ts:83-88](../src_vs_code/src/chatPersist.ts#L83-L88), again at
[chatPersist.ts:100](../src_vs_code/src/chatPersist.ts#L100).

**The symptom.** The comment on line 83 reads *"MAPPED ONLY WHEN IT IS ASKED FOR"*. Five lines below,
`nextAfterSave(outcome, baseline, ours: readonly string[])` takes an **array**, and the call passes
`ours()`. The function-shaped `ours` gives the appearance of laziness without the effect: a
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

**Where:** [chatHooks.ts:643-656](../src_vs_code/src/chatHooks.ts#L643-L656), `attachPicture`.

**The symptom.** `attachPicture` calls `forgetPicture(thread)` before writing the replacement. A full
or unwritable disk therefore leaves the conversation with **no image and nothing to retry from** —
the failure destroys the state it was meant to replace.

**The fix.** Write the new picture to a temporary name, swap it into place, then delete the old one.
A failure at any point leaves the previous image intact.

**The test.** A write that fails. Red: the old image is gone. Green: it is still attached, and the
failure is reported.

---

## Story 5 — a folder rename refiles conversations one at a time

**Where:** [chatFollow.ts:45](../src_vs_code/src/chatFollow.ts#L45), `followRenames`, over the walk
at [chatFollow.ts:71](../src_vs_code/src/chatFollow.ts#L71).

**The symptom.** Strictly sequential, each record with up to five 200 ms retries. Ten thousand
records is **hours**, with nothing on screen and a stale picker until the final refresh.

**The fix.** Bounded concurrency — a small fixed width, not unbounded `Promise.all`, because the
retries exist for a store that is already contended — and a progress indicator for the duration.

**The test.** A store of N records with a counting clock: the pure scheduling half is extracted and
tested as a value (`coding-style.md`: extract a named unit), so the width and the bound are asserted
without an editor.

---

## Story 6 — `index.refresh()` is not guarded

**Where:** [chatFollow.ts:97](../src_vs_code/src/chatFollow.ts#L97).

**The symptom.** The refresh runs after records have been refiled. If it throws, the picker keeps
offering names that have **moved** — the data is correct and the surface is not. The outer catch only
logs, so the person sees a working picker full of paths that no longer exist.

**The fix.** Its own guard, and on failure a visible refusal rather than a silent log — the records
moved, so the state on screen is known-wrong and must say so.

**The test.** A refresh that throws. Red: nothing is reported. Green: the refusal names what happened
and the records are still correctly refiled.

---

## Story 7 — `follow` cannot tell a busy store from a fatal one

**Where:** [chatFollow.ts:118](../src_vs_code/src/chatFollow.ts#L118).

**The symptom.** A permission failure is retried five times and then reported exactly as a lock is.
A lock clears; a permission does not, so the retries are a second of waiting that was never going to
help, and the person is told nothing either way.

**Read it beside the rejection it survived.** The gate claimed *"`follow`'s retry can hang the
extension host"* and that was rejected with a measurement — five tries at 200 ms is one second, in a
detached call nothing awaits, and a test pins the bound. That rejection stands. **The defect is not
the duration, it is the classification**: the retry loop treats every failure as transient.

**The fix.** Classify the failure. A contention error retries; anything else fails at once and says
what happened.

**The test.** Two stores — one refusing with a lock, one with a permission error. Red: identical
behaviour, five attempts each. Green: one attempt for the permission, five for the lock, and two
different messages.

---

## Story 8 — a reset can wait forever

**Where:** [chatArchive.ts:213-229](../src_vs_code/src/chatArchive.ts#L213-L229), `ended`.

**The symptom.** `await thread.turns` and `await thread.writes` with no timeout. One stuck turn — a
vendor process that has not exited — leaves *Ending the previous conversation…* on screen
indefinitely, with no way out but reloading the window.

**The fix.** A budget on each wait, and a terminal state when it expires that says which one did not
finish. This is CLAUDE.md §8 in miniature: never stick on an in-flight state.

**The test.** A thread whose `turns` never settles. Red: `ended` never returns. Green: it returns
within the budget with a reason naming the turn.

---

## Story 9 — New chat drops its progress indicator too early

**Where:** [chatArchive.ts:95](../src_vs_code/src/chatArchive.ts#L95) (`freshStart`) against
[chatArchive.ts:164-186](../src_vs_code/src/chatArchive.ts#L164-L186) (`publish`, which awaits
`thread.writes` at line 186).

**The symptom.** The progress scope closes after archiving, while `publish` is still awaiting the
disk write. A slow store therefore shows no terminal state at all: the indicator is gone and the new
conversation has not appeared.

**The fix.** The scope spans the write too. Same rule as story 8.

**The test.** A store whose save is slow. Red: the scope closed before the record existed. Green: it
closes after, and the order is pinned.

---

## Story 10 — the keyboard capture probes the host before showing anything

**Where:** [chatCapture.ts:98](../src_vs_code/src/chatCapture.ts#L98), `await windowsReach()` —
defined at [hostSide.ts:101](../src_vs_code/src/hostSide.ts#L101).

**The symptom.** About a second in a remote window, with nothing on screen for it. A second press is
the natural response, and it starts a second capture.

**The fix.** Progress first, probe second.

**The test.** A slow probe. Red: nothing was shown before the await. Green: it was.

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

**This is bigger than the gate thought it was, and the measurement is the reason to do it.** The
finding was accepted as *"`ChatModelChoice` couples config to the page — true and pre-existing"*, and
filed as tidiness. It is not tidiness. Three production modules import it out of the 2 243-line page
renderer:

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
together**, as one commit, after stories 1–11 — not before, because two of them touch functions those
stories rewrite.

---

## Build order

The order is by consequence, not by module:

1. **Story 1** — security, its own pull request, first, alone.
2. **Stories 2, 6, 7** — the three places a failure is currently invisible. One PR: they are all
   `chatFollow`/`chatHooks` error handling and share their test shape.
3. **Stories 8, 9, 10** — the three stuck-or-silent surfaces. One PR; all three are CLAUDE.md §8.
4. **Stories 3, 4, 5** — the three that need a data-shaped change (a supplier, a temp-and-swap, a
   bounded width). One PR each, because each has a real failure mode to test.
5. **Story 11**, then **the six Sonar findings**. One PR.

Stories within a group are independent; the groups are not reorderable, because group 3 changes the
progress handling that group 2's refusals post into.

## Test plan

**Every story is RED first**, per `.agents/conventions/common/testing.md` — the test is written,
run against the unfixed code, and the failure message is confirmed to describe the real symptom
rather than a setup error. Both observations go in the summary.

That is the point of this plan being separate. The parent series proved *nothing changed*, by
construction, and could not test any of this. Here each item is a behaviour change, so each gets the
test the parent could not write.

Two constraints from the parent carry over:

- **`chatHooks.ts` is 670 lines** and six stories touch it. Every fix that wants a pure half must
  **extract a named unit** (`coding-style.md:23-24`), never widen the file. If it crosses 800, the
  extraction is the work.
- **No new source-text assertions.** `.coderabbit.yaml` and `.agents/PROJECT.md` already forbid them,
  and [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) is the backlog for
  the 224 that exist. A test here that reads a file's text instead of running it is a twelfth.

The whole suite runs before each pull request — `npm test` (3 343 tests) plus the six-suite pre-run
(38) plus the family checks, which `npm test` does **not** include.

## Definition of Done

- [ ] Story 1 shipped **first and alone**, with the escape reproduced red and refused green.
- [ ] Stories 2–11 shipped in the five groups above, each with its own RED observation recorded.
- [ ] The six Sonar findings on moved lines closed, and `chatHost.ts`'s three `export let` reports
      left standing with their reason.
- [ ] `importCycles.test.mjs`'s `KNOWN` ratchet is **eight entries, not nine** — `chatModels ↔
      chatPage` deleted in the commit that makes it untrue (story 11).
- [ ] No module crossed 800 lines; any that approached it was extracted rather than widened.
- [ ] No new source-text assertion was added.
- [ ] The coai gate ran on each pull request — `review_plan` to `proceed`, then `review_code`.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated with every change.
- [ ] This plan promoted to `research/` when the last group lands, with its deviations recorded.

## Footnotes from the parent's tail, resolved rather than carried

Three items in the parent's tail were checked while writing this and are **not open work**:

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
