# PLAN — the chat command file is five times the ceiling

> Status: **IMPLEMENTED, 2026-09-17.** `src_vs_code/src/chatCommand.ts` went from **4 183 lines to
> 543**, under the 800 the coding-style rule allows, in fifteen modules and seven commits. No
> behaviour change anywhere in the series, and that is measured rather than asserted.
>
> Scope: `chatCommand.ts` and the eleven test files that read its SOURCE TEXT.
>
> Related: [../todo/PLAN_the_page_tests_run_the_page.md](../todo/PLAN_the_page_tests_run_the_page.md),
> the other standing tech-debt plan in this area;
> [PLAN_the_tab_finds_its_session.md](PLAN_the_tab_finds_its_session.md), whose epic A added three of
> the functions that made this file worth splitting.

## The measurement that opened it

`.agents/conventions/common/coding-style.md:23` — *"200–400 lines typical, 800 max"*, and line 24
says how: *"split by extracting a named unit with its own responsibility, never a `partial` (or its
equivalent) to duck the limit"*.

| module | lines | over the ceiling by |
|---|---|---|
| **`chatCommand.ts`** | **4 183** | **5.2×** |
| `panelProvider.ts` | 3 095 | 3.9× |
| `panelView.ts` | 2 754 | 3.4× |
| `chatPage.ts` | 2 243 | 2.8× |
| `roundsLog.ts` | 1 989 | 2.5× |
| `extension.ts` | 1 215 | 1.5× |
| `chatStoreFile.ts` | 1 019 | 1.3× |

It also carried 53 import statements, 20 exports, and one function — `conversationHooks` — that was
326 lines on its own. This plan took the first row only; the rest are still open.

## What shipped

Fifteen modules, in six tiers, each tier one commit:

| tier | module | lines | what it owns | `vscode` |
|---|---|---|---|---|
| 1 | `chatThread.ts` | 300 | the `Thread` type and the `threads` registry | **no** |
| 1 | `chatHost.ts` | 76 | the three handles a window binds once: the memento, the store, the heartbeat | **no** |
| 2 | `chatRoots.ts` | 117 | where a conversation is looked for, which root it is filed under, path↔uri both ways | yes |
| 2 | `chatConfig.ts` | 239 | what the chat reads out of the settings, and the host it reads discoveries on | yes |
| 2 | `chatCapture.ts` | 210 | what this side reads out of the editor, the tabs and the clipboard | yes |
| 3 | `chatPersist.ts` | 191 | the store write queue, and what its answer does to the tab | **no** |
| 3 | `chatShow.ts` | 188 | what the page is told, and the five facts every caller needs first | yes |
| 4 | `chatSessionJoin.ts` | 332 | joining a tab to the Claude Code session behind it | yes |
| 4 | `chatRegistry.ts` | 150 | what this window holds open; revealing and rebinding | **no** |
| 4 | `chatFollow.ts` | 169 | a conversation following the file it was opened from | **no** |
| 5 | `chatArchive.ts` | 312 | New chat: ending, filing, the clean slate | yes |
| 5 | `chatLaunch.ts` | 324 | starting a vendor process; switching the model | yes |
| 5 | `chatTurn.ts` | 466 | one turn, and the gestures that are turns under another name | yes |
| 6 | `chatHooks.ts` | 670 | everything a page can ask of the host; the composer writers | yes |
| 6 | `chatConversationRestore.ts` | 249 | how a reloaded tab comes back | yes |
| — | `chatCommand.ts` | **543** | the entry points: the command, the two doors, `newConversation` | yes |

**Five of the fifteen need no editor at all.** They sit outside `sonar.coverage.exclusions` and can
gain real tests; every line that stayed behind is still covered only by reading its own source. That
was the second reason for doing this and the one a line count does not show.

Two are over the 400 the rule calls typical, and each says why in its own header as the rule asks:
`chatHooks.ts` (670 — `conversationHooks` is one object literal built in one place) and `chatTurn.ts`
(466 — `oneTurn` is one sequence). Both are well under the 800 ceiling.

## The order is the plan, and it was decided by cycles

The single thing worth carrying out of this work: **the order is not a preference, it is forced.**

1. **`chatThread` and `chatHost` first.** Every module still to come takes a `Thread` or reads
   `store`, `memory` or `pulse`. Left in the command file, each later extraction imports them back —
   a cycle, fifteen times over.
2. **`chatPersist` before `chatSessionJoin`.** `adoptFound` calls `keepQueued`.
3. **`chatShow` before the archive, the launch, the turn and the hooks.** All four call `show`.
4. **`vendorFor` belongs to `chatConfig`, not `chatLaunch`.** `show` reads `pairOf`, `pairOf` calls
   `vendorFor`, and `switchModel` calls `show`. Putting it with the launch closes that loop. Its
   header says so, so nobody moves it back.
5. **`chatFollow` is cut before `chatRegistry`**, because they interleave in the old file and the
   registry's two halves are only contiguous once the follower is out.

Four functions travelled with a module rather than staying, each named in its commit:
`chatReadsThisSide` (its only reader moved), `closedSession` (a reset and a reload want the same
stub), `emptyTempDir` (it is where a vendor process runs), and `asUri` (the inverse of `fsPathOf`,
which `chatRoots` already owned).

## What proves nothing changed

Four independent checks, run at every tier:

1. **The collected test NAMES**, dumped, sorted and `diff`ed against a baseline measured by stashing
   that tier on the same commit. **3 261 names, identical at every tier.** The count alone is not
   enough: it survives a test being renamed out of one file and into another.
2. **`npm test`** — 3 244 tests, 3 242 pass, 0 fail, 2 skipped, plus a 27-test pre-run. It runs
   `clean && tsc` and stops on a non-zero exit, so stale output cannot be what answered.
3. **`npm run bundle`** — esbuild resolving the whole graph from `extension.ts` is the only check
   here that a cycle or an unresolvable import fails.
4. **`scripts/prove-move.mjs`** — every body line of every new module must appear VERBATIM in
   `origin/main`'s `chatCommand.ts`. **3 388 lines checked, zero residue**, with only the `export`
   keyword forgiven.

Roughly sixty source-reading assertions followed moved functions, and **every one was watched going
red against the old file** before being accepted. Several fixed-width slices became `bodyOf` reads
that end at the function rather than at a character count.

## Two failures worth more than the refactor

### The first attempt silently reverted somebody else's shipped work

It was built on a base **28 commits stale**. In between, a parallel effort landed a notifications
ledger that converted essentially every `vscode.window.show*` call in these files to `notify(...)`,
and added `notificationSites.test.mjs` to ratchet that count downward. The stale branch carried
**108 direct call sites** where current main has **4**.

Nothing caught it. The suite was green, the typecheck clean, the bundle linked, and the coai gate
returned `proceed` — all against the stale base, where the ratchet did not exist.

The mechanism is not carelessness. An extraction DELETES a region and re-adds it in a new file; a
rebase resolves delete-versus-modify in favour of the delete, so main's edits to those lines vanish
with no conflict and nothing to review. This repository had already been bitten once:
`ec4b15be feat(notifications): the rebase re-opened the hole, and the ratchet said so`.

**What came of it:** the series was redone from current main, and `scripts/prove-move.mjs` exists so
the next person finds this in seconds rather than in a review. Its decision half is tested on
fixtures, and every fixture is the guard being wrong in a specific way.

### Two assertions got WEAKER by moving, and passed

The memento is written by the page push AND by the fork. While both were in one file, matching
`memory?.remember(` meant what it said; asserted over the concatenation of two modules it passes
when either one satisfies it. Pointing the read back at the old file left it **green** — which is
exactly why that check is done rather than assumed. Both are a COUNT of two now.

The same shape appeared three more times, in guards that are about several places at once rather
than one: the one-queue count, the handover count (`EVERY handover goes through the mark`) and the
memory-rules guard. Each reads a **named list** of the modules the command file was split into, and
each tier adds to that list. Named rather than globbed, because a glob sweeps in modules that carry
the word without doing the thing, and is then wrong in the quiet direction.

## What none of this can see

**Nothing in this repository runs the extension.** A moved callback or a changed initialisation
order could make opening a chat, switching a model or archiving a conversation throw at runtime with
every check above green. The bundle proves the graph links, not that it behaves. Three reviewers
named this at the plan round and it is **not closed** by this work. The honest mitigations are a
manual pass over those gestures in a fresh window before release, and the activation harness that
[module_tests.md](module_tests.md) records as this repository's largest gap.

## Definition of Done

- [x] `chatCommand.ts` under 800 lines: **543**.
- [x] Every new module under 400, or its own header says why not (`chatHooks`, `chatTurn`).
- [x] Collected test NAMES identical per tier, against a baseline measured on the same commit.
- [x] `npm run bundle` succeeds per tier.
- [x] Every followed source-reading assertion watched going red against the old file.
- [x] Every moved line proved verbatim against the pre-split original — 3 388 lines, 0 residue.
- [x] `sonar.coverage.exclusions` names every `vscode`-importing module and only those, with the
      prose count beside it measured rather than typed: **32 of 187**.
- [x] [module_extension.md](module_extension.md) and [module_tests.md](module_tests.md) updated.

`research/architecture.md` is deliberately unchanged: its module map is feature-level, and it never
named this file.

## Tail — noticed while moving, left for their own work

- **`keepOnDisk` maps the whole transcript on every save while its own comment says it does not.**
  The comment reads *"MAPPED ONLY WHEN IT IS ASKED FOR"*, and three lines below,
  `nextAfterSave(outcome, baseline, ours: readonly string[])` takes an ARRAY and the call passes
  `ours()`. The function-shaped `ours` gives the appearance of laziness without the effect. Found by
  codex at the code round of the first attempt, pre-existing on main, and a real defect: a
  10 000-message conversation allocates a 10 000-element array on every successful save for a
  comparison that only a refusal needs. The fix is small — take the supplier, call it in the one
  branch — but it is a behaviour change and belongs to its own commit.
- **`chatFreshWiring.test.ts` has mixed line endings**, mostly LF with a stray CRLF comment. Reading
  it as text and writing it back rewrote all 300 lines for a twelve-line change, and so did `sed -i`.
  Edit it a line at a time and read `git diff --numstat` before believing the edit.
- **Three test files now carry their own copy of `bodyOf`.** A fourth is the point at which it wants
  a shared helper.
- **`chatHooks.ts` is 670 lines.** The reduction that would help is giving whole callbacks pure
  halves a test can reach — a behaviour change, its own plan.
- **`ChatModelChoice` lives in `chatPage.ts`**, so `chatConfig` depends on the page renderer for a
  type. Raised at the gate; a neutral contracts module would be tidier and is not worth a
  no-behaviour-change series.
- **The prose beside the Sonar exclusion list states a count**, so every module added has to correct
  it. It was two revisions stale before this began. `scripts/`-side tooling now measures it rather
  than trusting a typed number.
- **Seven modules are still over the ceiling**, listed in the table at the top. One file at a time.
