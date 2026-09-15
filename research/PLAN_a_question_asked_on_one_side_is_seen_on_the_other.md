# PLAN — a question asked on one side is seen on the other

> Status: **IMPLEMENTED, 2026-09-15**, with one item of its own Definition of Done still open.
> The code, the setting, the panel, the help in five languages and the tests have shipped. What has
> NOT been done is the last line of the DoD: naming the WSL store in the setting on the reporter's
> own machine, asking a question from a WSL session, and answering it from the Windows window. A
> test proves the code; only that proves the bug is gone. Recorded here rather than left implied.
>
> Scope: the escalation surface —
> `src_vs_code/src/escalationWatcher.ts`, `escalations.ts`, `dataDir.ts`, the panel's data section,
> the manifest and the help.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md),
> [PLAN_the_install_asks_where_the_data_lives.md](PLAN_the_install_asks_where_the_data_lives.md).

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

[PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md)
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

### What the plan round added

Eleven gating findings, and they turned a four-step plan into the list below. The two that would
otherwise have shipped broken:

**The temporary file must be created INSIDE the target directory.** The answer write is atomic —
temp, then rename — and `rename` across two filesystems throws `EXDEV: cross-device link not
permitted`. A temp written into this window's own directory and renamed into a WSL or NAS one fails
every time, the answer never lands, and the round that asked blocks for ever. Today's code happens to
be right (`escalationWatcher.ts:140-141` both join `dir`); the plan said only "answer into `from`",
which an implementer could satisfy while moving the temp. It is now a constraint with a test.

**A POSIX path pasted into a Windows window is silently nothing.** The reporter's WSL store is
`/home/<user>/.local/share/coai-mcp`, and that is exactly the string they will paste. Resolved by a
Windows extension host it becomes `C:\home\...`, which does not exist — and "an absent directory
contributes nothing and throws nothing" then reproduces the original symptom with no feedback at all.
**The extension does not guess a distribution.** A path beginning with `/` on `win32` is refused with
a sentence naming the shape that works (`\\wsl.localhost\<distro>\home\...`), shown in the panel
beside the directory. Guessing would mean inventing a distro name and watching a path nobody chose.

**One bad directory must not take the others down.** Watch, poll and read failures are caught PER
DIRECTORY; a directory that fails is disabled with its reason and every other one — this window's own
above all — keeps working. An unhandled watcher error on a disconnected NAS taking out the local
questions is the failure this plan would otherwise have introduced.

**Answering can fail after the question was found.** A mount can go read-only or disappear between
the modal opening and the answer being typed. The write is caught, the escalation is KEPT rather than
marked answered, and the reason is shown — so a person can retry rather than watch a round block on
an answer that never landed.

**Two windows can be offered the same question.** If both installations watch each other, both modals
appear. The answer is re-checked immediately before the write and the prompt is dismissed if one is
already there: first writer wins, and the second is told rather than overwriting.

**The list is canonicalised, not string-matched.** `C:\Data`, `c:\data\` and this window's own
directory are one place; case, trailing separators and the own-directory case are normalised out
before anything is watched twice.

**The watcher follows the setting.** `onDidChangeConfiguration` tears the list down and rebuilds it,
or the setting appears not to work until the window is reloaded.

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
| 7 | `escalationWatcher` tests | the same directory named twice — and named as `C:\Data` and `c:\data\` — is watched once, and this window's own directory named as an extra is not watched twice |
| 8 | `helpCoverage.test.ts` | passes — the new setting is described |
| 9 | `dataDir.test.ts` | a path beginning with `/` on `win32` is refused with a sentence naming the `\\wsl.localhost\<distro>\…` shape, and no distribution is guessed |
| 10 | `escalationWatcher` tests | the answer's TEMP file is written inside the target directory — the `EXDEV` regression, asserted on the path rather than on a mock |
| 11 | `escalationWatcher` tests | a directory whose read throws keeps every other directory working, this window's own included, and is reported with its reason |
| 12 | `escalationWatcher` tests | a write that fails KEEPS the escalation open and surfaces the reason rather than dropping it |
| 13 | `escalationWatcher` tests | a question answered elsewhere between the modal opening and the write is not overwritten |
| 14 | `escalationWatcher` tests | changing the setting re-reads the list without a window reload |
| 15 | `escalationWatcher` tests | **with the setting unset**: a question in this window's own directory is found, answered and not offered twice — the byte-for-byte promise, asserted rather than assumed |
| 16 | panel tests | valid, missing and unreadable extra directories are each rendered with their real status |
| 17 | help tests | the new setting's description is present in all five languages and differs from the English in each — `bodyFor` cannot see a stale translation |

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
