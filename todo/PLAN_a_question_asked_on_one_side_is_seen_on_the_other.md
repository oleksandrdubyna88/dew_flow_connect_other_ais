# PLAN — a question asked on one side is seen on the other

> Status: **plan only, nothing implemented yet, 2026-09-15.** Scope: the escalation surface —
> `src_vs_code/src/escalationWatcher.ts`, `escalations.ts`, `dataDir.ts`, the panel's data section,
> the manifest and the help.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](../research/PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md),
> [PLAN_the_install_asks_where_the_data_lives.md](../research/PLAN_the_install_asks_where_the_data_lives.md).

## The symptom

The operator, 2026-09-15: *"и на всл почему то не захватываются вопросы, хотя на виндовс все ок."*

A Claude Code session running **inside WSL** reaches a `call_human` verdict. The question is written,
the round blocks on it, and the modal never appears. The same thing in a Windows session works. Same
account, same extension, and nothing errors anywhere.

## What the code does today — verified 2026-09-15 on `5eb8ea4e`

**The question is captured. Nothing is watching the store it lands in.**

Two live stores were confirmed on the reporter's machine, both written the same day:

| | path | `coai.db` |
|---|---|---|
| Windows | `%LOCALAPPDATA%\coai-mcp` | 25.8 MB |
| WSL | `/home/<user>/.local/share/coai-mcp` | 8.3 MB |

`coaiDataDir()` (`dataDir.ts`) resolves `COAI_DATA_DIR ?? (LOCALAPPDATA ?? $HOME/.local/share) +
/coai-mcp`, then appends `COAI_DATA_SIDE` when one is named. Both were unset, so each platform took
its own default — and those are not two names for one place, they are two filesystems.

The escalation lives inside whatever that resolves to, on both halves:

- the server writes `Path.Combine(dataDir, "escalations")` — `src_mcp/src/Server/Escalations.cs:65`
- the extension watches `vscode.Uri.joinPath(this.dataDir, 'escalations')` —
  `escalationWatcher.ts:44`, reading at `:152` and answering at `:139`

The extension host runs on Windows, so it globs the Windows store; a `coai-mcp` spawned by a WSL
session writes into the WSL store. Neither half is wrong on its own terms. They are looking at
different disks.

## What is already decided, and must not be undone

[PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](../research/PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md)
put a merge question and answered it: **no merge.** `COAI_DATA_SIDE` exists so two installations can
share one location without writing the same SQLite file, and its test asserts the
two-sides-one-database case explicitly.

**That ruling is about the STORE, and this plan does not touch it.** A database has two writers and
must not be shared. An escalation is one JSON file per question, written once by the side that asked
and answered once by the person at the window — and there is one of that person however many sides
they run. Cross-side visibility was never considered when the partition was drawn; `escalations/`
simply inherited it. This plan fills that gap and leaves the partition alone.

The boundary is written into the older plan too, per the both-sides rule in `planning-docs.md`.

## What this plan does

**The extension watches additional escalation directories, named by the person.**

A new setting, `coai.alsoWatchDataDirectories` — a list of data directories belonging to OTHER
installations. The watcher watches its own, always, plus each of those. A question remembers which
directory it came from, and its answer is written back beside it.

### Why this shape, and not the two alternatives

**Not "merge the stores".** Ruled out by the plan that owns that decision.

**Not "point both installations at one root and watch the sibling sides".** This was the first draft
and it is the wrong trade for this problem. It works only if both installations share a root, which on
the reporter's machine means one side's `coai.db` moves onto the other's filesystem — a SQLite
database on a `drvfs`/`\\wsl.localhost` mount, which is the one thing a per-side partition was
invented to avoid having to think about. **Sharing the question surface does not require sharing the
store, and this plan is the version that shares only the question surface**: JSON files, written once
and read once, with no database anywhere near the boundary. It also works on the DEFAULT roots, which
is the configuration the reporter actually has and the sibling-side version could not help at all.

The cost is honest and stated: a manifest setting, and therefore a described setting in the help, in
all five languages (`helpCoverage.test.ts`, `HELP_LANGUAGES`).

### The three pieces

**A question remembers where it came from.** `Escalation` gains `from`, the directory it was read out
of, and the answer goes to `escalation.from` rather than to `this.dataDir`. Without it a question from
another installation would be answered into this one's folder, where the server that asked never
looks — and the round would block for ever, having been answered.

**A watched directory that is not there is not an error.** A WSL distribution that is shut down, a NAS
that is unmounted, a path with a typo: each simply contributes no questions. The existing 5 s poll is
what makes a `\\wsl.localhost` path work at all, because "a file created by another process on a
network or virtualised path does not always raise a watcher event" — its own comment, and the reason
it is not to be removed.

**The panel says what is being watched, and what it could not read.** *Where this window keeps its
data* already reports the resolved directory and which layer answered. It gains the extra directories
and, for each, whether it is readable — so a typo is learned from the panel rather than from a round
that never unblocks.

## Build order

1. **`escalations.ts`** — `Escalation` carries `from`.
2. **`escalationWatcher.ts`** — take a LIST of directories; watch and poll each; answer into `from`.
3. **The manifest + `alsoWatchDataDirectories()` in `dataDir.ts`** — read, trim, drop empties.
4. **The panel's data section** — name each extra directory and whether it is readable.
5. **The help** — the setting described, in all five languages, in the same commit as the English.

Each step is a commit. 1–3 are the fix; 4 and 5 are what make it usable and legal.

## Test plan

`node:test`, `cd src_vs_code && npm test`. RED first for every behavioural item, and the failure
message must name the real symptom.

| # | Test | Guarantee |
|---|---|---|
| 1 | `escalations.test.ts` | a parsed escalation carries the directory it was read from |
| 2 | `dataDir.test.ts` | the setting is trimmed, empties dropped, and an unset setting yields an empty list — the default installation watches exactly what it watches today |
| 3 | `escalationWatcher` tests | a question in an EXTRA directory is reported as open |
| 4 | `escalationWatcher` tests | answering it writes `<id>.answer.json` into THAT directory, not into this window's |
| 5 | `escalationWatcher` tests | a question already answered beside itself is not offered again |
| 6 | `escalationWatcher` tests | an extra directory that does not exist contributes nothing and throws nothing |
| 7 | `escalationWatcher` tests | the same directory named twice is watched once |
| 8 | `helpCoverage.test.ts` | passes — the new setting is described |

## Constraints

- **The stores are not merged and no database is opened across the boundary.** Only `escalations/`.
- With the setting unset, behaviour is byte-for-byte what it is today.
- The answer write stays ATOMIC (temp then rename) — the invariant the server's poll depends on.
- The 5 s poll stays; it is what makes a virtualised path work.
- A new manifest setting means a help description in five languages, and an English article edited
  without its four translations is a STALE translation, which `bodyFor` cannot see. Same commit.
- `research/module_extension.md` updated, and the boundary named in the older plan.

## Definition of Done

- [ ] RED observed and reported for every behavioural test, with the real symptom in the message
- [ ] `cd src_vs_code && npm test` green, whole suite, count reported
- [ ] `npm run typecheck` clean
- [ ] The coai gate: `review_plan` → `resolve` → implement → `review_code` → `resolve`, rebased on
      `origin/main` immediately before the code round
- [ ] `research/module_extension.md` updated, and the boundary named in the older plan
- [ ] `src_vs_code/CHANGELOG.md` in the prose the file already uses
- [ ] `todo/README.md` row committed WITH this file
- [ ] `plan-lifecycle` and `pin-check` clean
- [ ] **Verified on the reporter's own machine**: the WSL store named in the setting, a question asked
      from a WSL session, the modal appearing in the Windows window, and the answer landing in the WSL
      store. A test proves the code; only this proves the bug is gone.
