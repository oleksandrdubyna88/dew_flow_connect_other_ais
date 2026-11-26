# The E2 follow-ups — security verify of three commits (2026-11-26, epoch 3 E2)

Judge: Fable (security judgement role). Read-only for product code: no edit, no build, no test run, no model call, no CLI run.
Branch `feat/v2-ep3-e2-coder`, head `43de4629`. Commits judged: `0dc0f94c` (check unit without the coder's folder), `f288fff8` (retention),
`8e0a2d9b` (HeldFile descriptor). Docs compared: `research/module_server.md`, `research/module_runners.md`,
`v2/epoch-3/e2-coder-one-task/RESULTS_coder_proof.md` at `43de4629`.

Read: the three diffs with their tests; `src_mcp/runners/Toolchain/{BoundaryUnit,BoundaryJob,BoundaryPreflight,BoundaryFolders,CheckBoundary,CoderUnit,BoundaryHost}.cs`;
`src_mcp/src/Server/RoleJobs/{CoderJobFolders,CoderWork,CoderOsLayer,RoleJobRetention,RoleJobStore,RoleLease,RoleJobIds}.cs`;
`src_mcp/runners/Files/{GitScratch,LinuxHeldFile,HeldFile,RealPath,Junctions}.cs`; `src_mcp/runners/Worktrees/{CoderWorktree,ReviewTreeRoot}.cs`;
`KEY_POOL_SPEC.md`; the E3 judgement `RESULTS_codex_agy_write_boundary_judgement.md` (`feat/v2-ep3-e3-roles`).

Run by the judge: 8 `systemd-run --user` units (two of them only sleep) on scratch folders with made-up files, systemd 259.5, kernel 6.18 (WSL2). Section 2.
The scratch folders are removed and no probe unit is left. Not read: any key file, any environment.

Rules: `rules.mjs check` answered `resolved`; of the 36 selected rules only `common.security` was read (this file was written outside the repository).
The AI that commits this file loads the rest for its own change.

## 1. Verdicts

| Commit | Verdict |
|---|---|
| `0dc0f94c` check unit without the coder's folder | **must-change: 2 (M1, M2), then ready.** The mechanism itself holds on every path that was tried (section 2). M2 was there before this commit |
| `f288fff8` retention | **READY.** No must-change. 5 should |
| `8e0a2d9b` HeldFile descriptor | **READY.** No must-change. 3 should |

The author's two stated gaps (the build step's host network; other coder jobs' folders) are **acceptable for E2 as recorded gaps, on the condition that M1 and M2 are in** (section 3.3).

## 2. What was measured

Each unit had the property list of `BoundaryUnit.Properties` (isolation, hardening, `PrivateUsers=yes`), without the two `/tmp` binds: the scratch folder is under `/tmp`.

| # | Question | Answer |
|---|---|---|
| P1 | The product's list (`TemporaryFileSystem=<job>:ro`, then the four binds), private network | The coder's folder is not there. Its config is not read and not written. No file, no folder can be made in the job folder; the bound `home` cannot be moved away. `home` and `nuget-packages` are writable. A file link and a folder link in the WORKTREE that point at the coder's folder: nothing behind them. A link made in the check's HOME that points at `<job>/coder`: nothing behind it. `CapEff` is 0; `uid_map` is `1000 1000 1`. `umount` of the cover: refused; also from a nested user namespace (`unshare -Urm`: umount of the cover, umount of the `home` bind, remount read-write — all refused; the probe does not tell a refused `unshare` from a locked mount) |
| P2 | The same with the four binds BEFORE the tmpfs in the list, host network | The same answers. The order in the list does not matter to the manager |
| P3 | `/proc` of a process in ANOTHER unit of the same person (`PrivateUsers=yes`, the coder's folder bound), and of a process outside any unit | Refused for both: `root` (and the coder's config through it), `cwd`, `environ`, `exe`, the target of each descriptor (read link and open), `mem`, `maps`. Readable for both: `cmdline`, `mountinfo`, the list of descriptor NUMBERS. **`kill -0` of the other unit's process: allowed** |
| P4 | A file in ANOTHER job's coder folder beside this one; a stand-in keys folder; both outside `/home` | Both **read** |
| P5 | The job folder SPELLED through a link that lies under a hidden folder (a `TemporaryFileSystem` stand-in for `/home`), its real path outside it | The spelled coder path is "not there" — the question the preflight row asks — and the coder's config is **read at its real path**. A write there is refused. With the job folder spelled by its real path: not read |
| P6 | Three candidate fixes in one unit: `TemporaryFileSystem=<coder-jobs>:ro` (the folder that holds every job) with the four binds; `InaccessiblePaths=-<keys>`; `PrivatePIDs=yes` | The check folders are writable, the coder's folder and the other job's file are not there, the keys folder is not read, 4 processes are visible and the signal to a process outside is refused |

By reading, not measured: the coder's unit ends before the checks start (`CoderFixLoop`, one unit for each turn, `EnsureGoneAsync` after each); the claude coder has no shell tool
(`ClaudeConsultant.cs:156`); project hooks in the worktree do not run under `--restricted` (H1, H2 of `RESULTS_coder_write_boundary.md`). So the one open line of the E3 judgement
(section 4 point 3: a file planted by a check in the coder's HOME) is closed by this commit: a check cannot write that home.

## 3. `0dc0f94c` — a check unit does not see the coder's folder

### 3.1 What holds

- Read and write of `<job>/coder` from a check: closed in both network states, for the build step and the test step (one property list, `BoundaryUnit.cs:34-54`), P1 and P2.
- Links in the worktree, a link in the check's own folders, `/proc` of the coder's unit and of host processes: no way to the folder (P1, P3).
- The order of `TemporaryFileSystem=` and `BindPaths=`: no effect (P2). Sources of the binds, the `/tmp` and `/var/tmp` binds included, are taken from the host's tree, not from under the cover.
- The preflight is fail-closed for what it asks: a missing row is a row that did not hold (`BoundaryPreflight.cs:252-259`), the folder is made first (`CheckBoundary.cs:102`),
  the argument count and the submodule shift are right (`$8`, `shift 8`), and the degrade (the whole job folder bound again) is in the theory.
- Bind sites, by `grep` of the four property names over `src_mcp/src` and `src_mcp/runners`: two, `BoundaryUnit.Properties` and `CoderUnit.Properties`. No third.

### 3.2 Must-change

**M1. A job folder that is spelled through a link is covered at the wrong place, and the row says "ok".**

- Where: `src_mcp/src/Server/RoleJobs/CoderOsLayer.cs:55-56` and `CoderWork.cs:117,122` (the job folder is a spelling, never resolved; the worktree and the main checkout are,
  `CoderTree.cs:45-46`); `src_mcp/runners/Toolchain/BoundaryJob.cs:143-155` (`Problem()` judges the spelling only); `BoundaryUnit.cs:69`; `BoundaryPreflight.cs:142-145` (the row asks the spelled path only).
- Scenario: `COAI_CODER_JOBS=/home/u/jobs` where `jobs` is a link to `/data/jobs`; or `~/.local/share` (or `XDG_DATA_HOME`) is a link to another disk; or the home itself is a link.
  Inside the unit `/home` is an empty tmpfs, so the link is not there: the manager makes the cover and the binds at the SPELLED path inside that tmpfs. The real `/data/jobs/<id>/coder` is under
  no cover, and `ProtectSystem=strict` only makes it read-only. The build step (host network) reads the coder's home — transcripts, CLI config — at the real path and can send it.
  `coder-folder-hidden` answers "ok". Measured: P5. Not on this machine today (the folder is its own real path).
- Smallest fix: admission refuses a job folder that is not its own real path — after `PrepareFolders`, `RealPath.Resolve(job.JobRoot).Path` must equal `job.JobRoot` (one rule for `CheckBoundary.AdmitAsync`
  and `CoderBoundary`, e.g. in a `BoundaryJob.OnDiskProblem()` beside `MountsOnDiskProblem`). Or resolve the coder-jobs folder once in `CoderWork.LayerForAsync`. One test: a bed whose job folder lies behind a link is refused.

**M2. The key pool's folder, the keys file and the person's sign-in are not hidden from a CHECK unit, and no check row asks.** (Not made by this commit. It answers the question "key material, every path".)

- Where: `BoundaryUnit.cs:34-54` has no `InaccessiblePaths=` for `job.KeysFolder` / `job.KeysFile`; only the keyed coder's unit has it (`CoderUnit.cs:44-45`, `:64`).
  `BoundaryPreflight.cs:43-44` and `:202`: no keys row and no sign-in row in the check script (the fragments exist for the coder, `:168-178`). `CheckBoundary.cs:78` does not ask `KeysFolderProblem()`.
  The spec says otherwise: `KEY_POOL_SPEC.md:225` — "not visible in ANY boundary unit, coder or check … A preflight row reads it from inside and must fail".
  This is the shape of `common/security.md`: a measure applied at some of its sites.
- Scenario: the data folder is outside `/home`, `/root`, `/mnt` and `/run/user` (`/var/lib/coai` — the example of `CoderUnit.cs:41` — `/srv`, `/opt`), or `COAI_CLAUDE_KEYS_FILE` names a file there,
  or coai runs as an account whose home is there. The build step of any coder job runs code the coder wrote, as the same user, with the host network: it reads every pool key
  (and that home's `.claude/.credentials.json`) and sends them. Mechanism measured: P4. On the default layout (data folder and home under `/home`, or the data folder under `/mnt`) it does not happen.
- Smallest fix: (a) one shared list — today's `CoderUnit.KeysHidden` without the `BindSignIn` condition — also in `BoundaryUnit.Properties`; (b) `CheckBoundary.EarlyRefusal` also asks `job.KeysFolderProblem()`;
  (c) two rows in the check script from the existing fragments: `keys-folder-hidden`, and a read of `$rh/.claude/.credentials.json` that must fail; each with its degrade in the theory. Measured: P6.

### 3.3 The stated gaps

- **The build step has the host network.** Acceptable for E2: it is measured as needed (`NU1900`) and recorded in every step's result. With M1 and M2 in, the step can read nothing of the coder or of the pool.
  What stays: every file outside the hidden paths (read-only), the host's loopback (J6 of the E3 judgement), and signals (S1 below). The real fix is two steps — a restore with the network, a build without — a later story, not E2.
- **Other coder jobs' folders are readable when the coder-jobs folder is outside `/home`.** For E2 they hold the transcripts and CLI config of other claude jobs, of other repositories too; no sign-in (it is bound only in the coder's unit)
  and no key (the scan of the proof run found none in the coder jobs folder). Acceptable for E2 as a recorded gap, because the default folder is under `/home`. It stays **S2 (must) of the E3 judgement** before a codex or agy
  sign-in copy lies in a coder folder. The fix is one property and it is measured (P6): cover the coder-jobs folder itself (`TemporaryFileSystem=<parent of JobRoot>:ro`, refused when that parent is the root of the file system),
  in the check unit (`BoundaryUnit.cs:69`) and in the coder's unit (`CoderUnit.cs:60`). It touches the line M1 touches, so doing it now costs little. `CoderOsLayer.cs:52-53` ("another job's home must not be readable from this job's unit") is true only under `/home` until then.

### 3.4 Should

- **S1. A check can signal every process of the person** (P3: `kill -0` of another unit's process is allowed; the same holds for the runner, the user manager, an editor), and it reads every `cmdline`. One `kill -9 -1` in a test ends them.
  Not from this commit. `PrivatePIDs=yes` closes it (P6; usable on systemd 259, C7 of `KEY_POOL_SPEC.md`); it needs systemd 257, so it wants a preflight row and a recorded answer for an older manager.
- **S2.** The row answers "ok" for an empty `$coderdir` (`BoundaryPreflight.cs:143`). It cannot be empty today; make an empty one "bad".
- **S3.** `module_server.md` ("The tmpfs comes first and the folders after it: a bind under a tmpfs that is mounted later would be hidden by it") is not what the manager does (P2), and
  `ASubmoduleMountInTheUnitsTests` asserts that order as if it had an effect. Say that the order does not matter (measured, systemd 259), or drop the assertion's reason.
- **S4.** "wherever it lies" (`module_server.md`, the same paragraph) and "proves it from inside" (`RESULTS_coder_proof.md`, follow-up 2) are true only for a job folder that is its own real path, until M1.
- **S5.** Old words in the code: `BoundaryJob.cs:67` ("a unit binds the job folder writable"), `BoundaryPreflight.cs:11` ("six items … and four more": five now).

## 4. `f288fff8` — retention

### 4.1 What holds (by reading)

- **No traversal.** A job id is `^[a-z0-9][a-z0-9-]{0,39}\z` (`CoderWorktree.cs:52`), checked in `RemoveOf` and in `Named` before any path is made; the two names removed are `<coder-jobs>/<id>` and `<id>.mounts`, nothing is searched.
  A worktree's folder is `coai-coder-<id>`, so `<root>/<id>` is never a worktree even when both roots are one folder. A role job id has a second and 32 random bits, so two stores on one machine do not meet.
- **Links.** A link in the place of the folder is left (`CoderJobFolders.cs:46-49`); a link inside is unlinked by its own name and never entered (`GitScratch.cs:55-71`, `:101-115`). `LinkTargetLostException` is an `IOException`, so it is caught.
- **No writer under the delete.** No unit can write `<coder-jobs>` or `<id>.mounts` (never bound writable; hidden or read-only at the host path), so nothing inside a unit can put a link where a folder was.
  The job folder's own contents are written by the job's units only, and those are gone when the record is terminal and the lease is free.
- **A live job.** The lease is a `flock` held by the runner's process for its whole life; `TryTake` needs it and a record that is not over (`RoleJobStore.cs:108-152`). So no second runner exists while the first is in its `finally`,
  and the retention skips a record whose lease is held (`RoleJobRetention.cs:50`).
- **A fenced runner.** `StillWriter` is `RoleLease.IsCurrent` (`RoleLease.cs:60`): false when the generation moved, the job is over, or the record cannot be read — the mounts are then left. The safe side in each case.

### 4.2 Should

- **R1. A job folder that could not be removed is never tried again.** `RoleJobRetention.cs:35-41` removes the record first and the folders after; no record is left to find them by. `CoderJobFolders.cs:13` and the class remark say
  "stays and is tried again". The coder's home (transcripts) then outlives the 30 days in silence. Code the coder wrote can cause it: a folder of mode 000 in the check's HOME, or a link there to a folder whose names change (R2).
  Fix: remove the coder folders first, inside `Remove` under the held lease, and keep the record while a folder stays.
- **R2. `GitScratch.Unlink` lists the TARGET of every link, before and after** (`GitScratch.cs:125-126`, `:148-149`, `:166`). It was written for a Kimi home's own link. Here the links are made by check code:
  a link to `/proc` loses a name between the two listings every time, so the delete stops there for ever; a link to a dropped share under `/mnt` or to a very large folder makes the retention wait, and the retention runs inside every job start
  (`RoleJobService.cs:92`). On Linux every rooted path but `/net`, `/misc`, `/Network` counts as local (`Junctions.cs:152-153`). A job folder wants a plain unlink with no read of the target.
- **R3.** Words against code: `module_server.md` ("a link in the place of a folder is unlinked") and the test name `AMountsFolderThatIsALink_IsUnlinked_…`; the code leaves the link, and the test asserts that. The code is right.
- **R4.** The folders are looked for under the coder-jobs folder of the CALLER's settings. A data folder shared by two machines: machine A removes the record of a job that ran on B, and B's folders stay for ever.
  The same after `COAI_CODER_JOBS` was changed. A note in the doc, or the folder's path and host in the record.
- **R5.** Nothing asks whether the job's units are gone before its folder goes. Safe today by distance only: a unit ends at its step's deadline plus the backstop, and a record goes after 30 days or 200 newer jobs.
- Also: an exception of `CoderSubmodules.ProvideAsync` leaves a half-made mounts folder until the retention (`CoderWork.cs:70` is before the `try`). Harmless.

## 5. `8e0a2d9b` — the HeldFile descriptor

### 5.1 What holds (by reading)

- `open` with `O_CLOEXEC` (`0x80000`, right for x64 and arm64 Linux), `flock`, the stream, then `fcntl(F_SETFD, 0)` (`LinuxHeldFile.cs:44-74`). A child that another thread starts before the `fcntl` does not get the descriptor after its `exec`.
- Every failure closes the descriptor once: a refused `flock` by `close` (`:86`); a stream that cannot be made by the handle's dispose (`:78`); a failed `fcntl` by the stream's dispose, which owns the handle (`:69`).
- After a take that succeeded every child inherits, which is what the tree hold wants. A child started by another thread between the stream and the `fcntl` does not inherit; the caller has not been answered yet, so it is no child of the hold.
- The role lease is not this take (`HeldFile.TryHold`, not inheritable): the retention of section 4 does not depend on it.
- `fcntl` is variadic in C and is declared here with three fixed integers. That is right for the Linux x64 and arm64 calling conventions (this class is Linux only); `open` in the same file already has that shape.

### 5.2 Should

- **H1. A short window stays.** A child that is between its `fork` and its `exec` holds a copy of the descriptor whatever its flag. If the take fails in that moment, the `flock` lives until the child's `exec`.
  It is far shorter than before and cannot be closed from here. `module_server.md` already says the flake was not reproduced; add that this window is left, so a third failure is not read as a new cause at once.
- **H2.** `Held` catches three exception types (`:76`). A stream seam that throws another one leaves the descriptor, and its `flock`, to the finalizer. The product's seam throws only those three. A `catch` that disposes and rethrows costs one line.
- **H3.** Old words in the code: `LinuxHeldFile.cs:8` and `:38`, `HeldFile.cs:33` still say "open without `O_CLOEXEC`". `module_runners.md` (the table row) is right.

## 6. Docs against code

| Doc | Says | Code or measure |
|---|---|---|
| `module_server.md`, the check unit paragraph | the tmpfs must come first | no effect (P2) — S3 |
| the same | "wherever it lies" | not for a job folder behind a link (P5) — M1 |
| `RESULTS_coder_proof.md`, follow-up 2 | the row "proves it from inside" | it proves the spelled path — M1 |
| `module_server.md`, the retention paragraph | a link in the place of a folder is unlinked | it is left — R3 |
| `CoderJobFolders.cs:13` | a folder that cannot be removed "is tried again" | not the job folder at the retention — R1 |
| `KEY_POOL_SPEC.md:225` | the keys folder is hidden in a check unit, with a row | neither is there — M2 |
| `module_runners.md`, the native take row | close-on-exec until the take succeeded | equal |
| `module_server.md`, the HeldFile paragraph | a hypothesis, the flake not reproduced | equal; H1 adds the window that is left |
| `module_server.md`, "Not changed" | the build network; other jobs' folders | equal to the code; judged in 3.3 |

## 7. Not measured, and not checked by this judge

- No test of the product was run and nothing was built. The author's tests were read, not seen green.
- The units of section 2 are replicas made by hand from `BoundaryUnit.Properties`, without the `/tmp` and `/var/tmp` binds. P5 used a `TemporaryFileSystem` stand-in for `/home`, not a link under the real home.
- A hard link from a unit into a folder the retention deletes: judged impossible by reading (`link(2)` across two bind mounts is `EXDEV`), not tried.
- The window of H1 was not reproduced.
- Systemd older than 259 was not tried for P2 or P6.
- What the claude CLI itself writes into the coder's home was not looked at again (the proof run's key scan is the evidence in hand).
