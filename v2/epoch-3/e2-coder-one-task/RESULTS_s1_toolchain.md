# Epic e2-coder-one-task, story s1-toolchain-file — results

Recorded 2026-11-23 on the WSL2 box (24 threads, `/tmp` is a 7.9 GB tmpfs). Commits: `9d993bd9` t1, `80d3d7c4` t2, `3984a964` t3, `71d394bf` t4.

## t4 d2 — the check runner on a clean checkout of this repository

`AThisRepositoryToolchainFileTests.ARecordedRunOnACleanCheckout` (set `COAI_RECORD_CLEAN_CHECKOUT_RUN` to a folder for the logs; it is skipped otherwise). It clones HEAD with `git clone --no-hardlinks`, mounts the rules
submodule and runs `npm ci --ignore-scripts --prefix .agents/conventions` as `ci.yml` does, then runs `ChecksAtBase` at the clone's HEAD: the toolchain file is read at the base commit, and its two commands run from the clone's root.

| Run | Where the clone lived | Build step | Test step (`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests`) | State |
|---|---|---|---|---|
| 1 | `/tmp` (tmpfs, 7.9 GB) | exit 0, 29 s | exit 2 — 15595 tests, **41 failed**, 161 skipped, 6 m 47 s | failed |
| 2 | `~/.cache/coai-record-tmp` (disk, a temp root of 33 characters) | exit 0, 30 s | exit 2 — 15595 tests, **2 failed**, 8 m 13 s | failed |
| 3 | `~/.ct` (disk, a temp root of 14 characters) | exit 0, 1 m 45 s | exit 0 — 15595 tests, **0 failed**, 15434 passed, 161 skipped, 8 m 07 s | **green** |

Run 3 is the record: the build argv was `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then the executable alone; 9 m 53 s end to end.

What runs 1 and 2 say — both are about WHERE the checks run, not about the toolchain file or the runner, and both matter to s2 (the coder worktree):

- Run 1: the product's snapshot refuses a volume with less than 5 GiB free (`the volume holding … has 4,649,889,792 bytes free; after the 16 bytes of this snapshot it would have …, under the floor of 5,368,709,120`).
  A clone with its build output, plus the parent run's own folder, left a 7.9 GB tmpfs at 4.6 GB. The 41 failures are all Kimi/Vibe/WSL2 snapshot tests.
- Run 2: two tests build a path or a Unix socket name from the temp root (`The path '…/shown.sock' is of an invalid length for use with domain sockets … between 1 and 108 characters`; the Windows 259-character budget of a Vibe home).
  A temp root of 33 characters is too deep for them once the run's own folders are added.
- So a coder worktree must live on a real disk under a SHORT path, with the temp root short as well. s2-t2 should say so where it places the worktree.

## What the first three tasks were measured with

Unit and scenario tests are in `src_mcp/tests/` (`AToolchainFileTests`, `AToolchainSchemaAgreesWithTheReaderTests`, `ACheckRunnerTests`, `AChecksAtTheBaseShaTests`, `AFailureDigestTests`, `AThisRepositoryToolchainFileTests`). The fixtures of t3 are real
recorded output of this repository (`src_mcp/tests/fixtures/toolchain/`): a `dotnet build` of the core project with two deliberate compile errors in a scratch file (MSBuild prints each error twice; the digest names it once), a
`dotnet build` of a project file that is not there, and the `CoaiMcp.Tests` executable run on a scratch class with one passing test, two failing ones and a theory with one failing case. Two edits were made to the recordings: the checkout path became
`/home/dev/coai` and the line naming the run's temporary folder was dropped. The scratch files were never committed.

## Findings the epic should carry

- **Flow lists.** `server.md` § 12 writes its example with flow lists (`build: [dotnet, build, …]`, `skip: [audit, coverage]`, `timeouts: {build: 15m, test: 30m}`). The strict YAML subset the product ships refuses a non-empty flow list and every flow map by name (`yaml-forbidden`),
  so the file is written one item to a line; the example in § 12 is not a file this reader accepts. `shared/toolchain.schema.json` says so in its `$comment`.
- **`not_configured` has a neighbour.** A file that is there and wrong, or a base commit that cannot be read, is `toolchain_refused` — not `not_configured`, which would read an outage as a repository with nothing to check — and neither is green. A file whose projects name no command is `not_configured`.
- **A coder cannot change its own checks.** `ChecksAtBase` reads the file at the base SHA and runs it in the worktree; a change the coder makes (committed or not, added, edited or removed) is reported in `CheckResult.ToolchainChange` and in the note, and does not run for that job. A worktree whose only difference is line endings is `Unchanged`.
