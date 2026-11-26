# Epic e2-coder-one-task, story s2-write-boundary, task t4 — the check boundary, measured

Recorded 2026-11-23 on the WSL2 box (14 threads, 15 GiB RAM, systemd 259 user manager with Linger, `/dev/sdd` real disk). Code: `CheckBoundary` (`src_mcp/runners/Toolchain/`), commits `c621b0eb` (d1-d4), `93bc8d42` (d5), `fcac290d`, `2598f90c` (d6).

## What runs

A check step of a coder job runs as `systemd-run --user --wait --pipe --collect --quiet --unit coai-check-<job>-s<n> --working-directory <worktree> -p … -- /usr/bin/env -i <allow-list> <argv>`.

Properties (the order they are given; `W` the worktree, `M` the main checkout, `J` the job folder):

```
ProtectSystem=strict                      ProtectHome=tmpfs
TemporaryFileSystem=/run/user             TemporaryFileSystem=/mnt             TemporaryFileSystem=/dev/shm
InaccessiblePaths=-/run/WSL
BindPaths=W                               BindReadOnlyPaths=M                  BindReadOnlyPaths=W/.git
BindPaths=J                               BindPaths=J/tmp:/tmp                 BindPaths=J/vartmp:/var/tmp
BindReadOnlyPaths=~/.nuget/packages       BindReadOnlyPaths=~/.nvm             (each only if it exists / node is found there)
BindReadOnlyPaths=<resolver file>         (host-network steps only; WSL: /mnt/wsl/resolv.conf)
NoNewPrivileges=yes     PrivateUsers=yes     RestrictSUIDSGID=yes     LockPersonality=yes
ProtectKernelTunables=yes     ProtectControlGroups=yes     Nice=10
PrivateNetwork=yes|no     RuntimeMaxSec=<step timeout + 60>     MemoryMax=<n>M     MemorySwapMax=0
```

Environment, set by `env -i` INSIDE the unit: `PATH` (system folders, plus the `~/.nvm` folders of coai's own PATH), `HOME`, `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `npm_config_cache` (all under `J`), `NUGET_FALLBACK_PACKAGES` (the read-only shared folder), `TMPDIR=/tmp`, `LANG`, `TERM=dumb`, `GIT_TERMINAL_PROMPT=0`, and the step's own switches (`NO_COLOR`, `DOTNET_NOLOGO`, `DOTNET_CLI_TELEMETRY_OPTOUT`, `MSBUILDDISABLENODEREUSE`).

## Preflight on this machine (real HOME, real `/mnt`, run E below)

Every row held, build unit (host network) and test unit (private network): `user-manager` (systemd-run --user started the preflight unit), `worktree-write`, `main-readonly` (the main checkout is there and a write was refused), `git-readonly` (the `.git` pointer and the main `.git` folder refused), `no-user-manager-inside`, `windows-exe` (a copy of `whoami.exe` in the worktree did not run; run in an ordinary user unit it prints the Windows user name, so the program is a good one), `home-is-the-jobs` (the real HOME cannot be written), `tmp-is-the-jobs`, `network-state` (`lo` only for the test unit), `no-sockets-no-interop`, `clean-environment`.

Findings of the first runs, all fixed:

- `systemd-run … true` from inside succeeds when the unit has the manager's environment and `/run/user`; both `env -i` and `TemporaryFileSystem=/run/user` stop it.
- `InaccessiblePaths=-/run/WSL` is the property that stops WSL interop (without it the `.exe` in the worktree ran in the same unit; `/mnt` hidden and `PrivateUsers` alone did not stop it).
- `PrivateUsers=yes` is IMPLIED by the other mount-namespace properties in a user unit (`/proc/self/uid_map` shows one user with or without it), so no test can go red by removing it. It stays: it is on the must-have list.
- `/etc/resolv.conf` is a link to `/mnt/wsl/resolv.conf`; hiding `/mnt` left a host-network step with no DNS. That one file is bound back read-only for host-network steps, and the preflight allows exactly it under `/mnt`.
- A `find /mnt` of three levels took 65 s on `/mnt/c`; the preflight looks two levels deep.

## d6 — this repository's build and whole suite through `CheckBoundary`

The recorded test is `AThisRepositoryUnderTheBoundaryTests.ARecordedRunUnderTheBoundary` (skipped unless `COAI_RECORD_BOUNDARY_RUN` names a new folder). It clones HEAD to `~/.cbm/m` (`--no-hardlinks`), mounts `.agents/conventions` (`git submodule update --init --depth 1`), makes the coder worktree `~/.cbm/c/coai-coder-m1` with `CoderWorktree`, mounts the rules and runs `npm ci --ignore-scripts` there as `ci.yml` does, then runs `ChecksAtBase.RunCoderJobAsync` (base toolchain from the main path, preflight, the steps in units, `MemoryMax=8000M`, `-m:4`). A real disk, a root of 15 characters.

| Run | Commit measured | Build step | Test step | Result |
|---|---|---|---|---|
| A | `93bc8d42` | network none: **exit 1, 8.0 s** — 30 × `NU1900` (`Unable to load the service index for source https://api.nuget.org/v3/index.json`), warnings are errors | not run | failed |
| B | `93bc8d42` | network host, no DNS: exit 1, 7.7 s, the same 30 errors | not run | failed — found the DNS gap above |
| C | `93bc8d42` | network host: exit 0, 32.9 s | network none: exit 2, 8 m 26 s, 15,730 tests, **3 failed**, 198 skipped | failed — two of this task's own tests started units with no user manager to start them on (they now skip), one leftovers-inventory row |
| D | `fcac290d` | host: exit 0, 33.5 s | none: exit 2, 7 m 47 s, 15,733 tests, **1 failed**, 202 skipped | failed — `NoProductionCodeResolvesALinkTargetByItself`: `File.ResolveLinkTarget` in `BoundaryHost` (now the product's resolver) |
| **E** | `2598f90c` | host: **exit 0, 34.3 s** | **none: exit 0, 8 m 14 s, 15,734 tests, 0 failed, 15,531 passed, 203 skipped** | **green**, 8 m 49 s end to end |

Reading:

- **The whole suite passes under `PrivateNetwork=yes`** (run E: the unit's only interface was `lo`). The test step takes 8 m 14 s inside the boundary against 8 m 07 s outside it (s1, run 3): no measurable cost. s1 run 3 skipped 161 outside the boundary on a smaller suite; the rest of the 203 are tests that need a user manager and find none inside the unit (not counted exactly).
- **The build does NOT pass under `PrivateNetwork=yes`**: `TreatWarningsAsErrors` turns the unreachable NuGet vulnerability audit into an error even though every package is in the read-only fallback folder (run A: the restore line itself says `Restored … (in 155 ms)`). The toolchain file is the repository's, and a coder cannot change it, so coai does not add `-p:NuGetAudit=false` to it. The build runs on the host network, in the same unit: it has no credential in its environment, no home, no bus and no folder to write but its own.
- **Defaults set from this** (`NetworkChoice.DefaultBuild`, `DefaultTest`, `BoundaryJob.ForCoder`): build `host`, reason "the build restores and audits NuGet packages online (NU1900 is an error here); with only a loopback device it failed in 8 s"; test `none`, reason "the whole suite of this repository passed with only a loopback device". Both reasons are in every step's `BoundaryRecord.NetworkReason`.
- A repository whose own suite needs the network would declare it on its job (`BoundaryJob.TestNetwork`) and the result says `host`.

Not measured: a cold NuGet cache (the shared folder was warm; the per-job `NUGET_PACKAGES` was empty, so every package came from the fallback folder), peak memory of the unit (8000 MiB was enough; the 6000 MiB default of `BoundaryJob` was not tried against the suite), a Linux host that is not WSL, and macOS / native Windows (refused before any process starts; a test pins that).

---

# Task t5 — the claude coder process inside the OS layer, measured

Recorded 2026-11-23 on the same WSL2 box. Code: `CoderBoundary`, `CoderUnit`, `CoderProgram`, `CoderTurns`, `CoderTurnOutcome` (`src_mcp/runners/Toolchain/`), commits of epoch 3 E2 s2 t5 (d1 to d4). The layer is option (a) of `v2/spikes/RESULTS_coder_write_boundary_judgement.md` § 4: the CLI process itself runs in a named `systemd-run --user` unit of the checks' family. It shares `BoundaryUnit` (isolation, hardening, limits, the one `systemd-run` wrapper), `BoundaryPreflight` (the same row fragments), `UnitControl` and `BoundaryFolders` (extracted from `CheckBoundary` without a change of behaviour: the 39 tests of the check boundary pass after the extraction).

## The coder's properties (`W` worktree, `M` main checkout, `R` coder root = the folder that holds `W`, `C` the job's `coder/` folder)

```
ProtectSystem=strict   ProtectHome=tmpfs   TemporaryFileSystem=/run/user  /mnt  /dev/shm   InaccessiblePaths=-/run/WSL     (same as the checks)
BindReadOnlyPaths=M    BindReadOnlyPaths=R   BindPaths=W   BindReadOnlyPaths=W/.git
BindPaths=C            BindPaths=C/tmp:/tmp  BindPaths=C/vartmp:/var/tmp                 (the coder's HOME is C/home)
BindReadOnlyPaths=<real home>/.claude/.credentials.json:C/home/.claude/.credentials.json (only if the file exists)
BindReadOnlyPaths=<folder of the program>      (only if it is not under /usr /bin /sbin /lib /lib64 /opt)
BindReadOnlyPaths=<resolver file>              (WSL: /mnt/wsl/resolv.conf)
NoNewPrivileges=yes  PrivateUsers=yes  RestrictSUIDSGID=yes  LockPersonality=yes  ProtectKernelTunables=yes  ProtectControlGroups=yes  Nice=10
PrivateNetwork=no      RuntimeMaxSec=<deadline + 60>   MemoryMax=<n>M   MemorySwapMax=0
```

Environment, set by `env -i` INSIDE the unit: `PATH` (system folders only), `HOME=C/home`, `TMPDIR=/tmp`, `LANG`, `TERM=dumb`, `NO_COLOR`, `GIT_TERMINAL_PROMPT=0` — and, by NAME (`--setenv=NAME`, copied from the client, never in an argv), the row's own `ANTHROPIC_API_KEY` and the proxy, certificate and time-zone names coai itself has. The program is exec'd by its REAL path (`~/.local/bin/claude` is a link into the hidden home).

## Why each bind of the real home is what it is — measured with the real CLI (claude 2.1.330, `--model haiku`, the R11 flag set)

| # | What | Result |
|---|---|---|
| 1 | Outside any unit, `HOME` = a scratch folder holding a COPY of `~/.claude.json` and of `~/.claude/.credentials.json` (the copy of a token that had 1.6 h left, so no refresh could happen) | answered `OK`, **0.00074 USD**. What the CLI wrote under that HOME: `.claude.json` (rewritten, with a backup in `.claude/backups/`), `.claude/cache/model-catalog/*.json`, `.claude/plugins/store/*.json`, `.claude/policy-limits.json` (+ `.signature.json`, `.stamp.json`), `.claude/projects/<slug>/<session>.jsonl`, `.claude/sessions/` (a file made and removed). `.credentials.json` was not written. |
| 2 | Inside a unit with the properties above, the sign-in file bound read-only into an EMPTY coder home, **no resolver file** | **hung until the 100 s limit** (client killed, the unit stopped by name afterwards): the CLI started, signed in and wrote its session file, then could not resolve its vendor. `getent hosts api.anthropic.com` printed nothing in the same unit. `/etc/resolv.conf` is a link to `/mnt/wsl/resolv.conf` and `TemporaryFileSystem=/mnt` hides it — the same finding as run B of t4. No result line, so no cost figure (the CLI had no answer to bill). |
| 3 | The same unit plus `BindReadOnlyPaths=/mnt/wsl/resolv.conf` | answered `OK`, **0.00060 USD**. Everything new was inside the coder's home and its `/tmp`; the real `~/.claude`, `~/.claude.json` and `settings.json` were never read or written, and the bind of the sign-in file was unchanged afterwards. |

Total: **3 `claude -p --model haiku` calls, 0.00134 USD reported** (the limit was 3 calls and 0.10 USD). The unit of calls 2 and 3 was written by hand from the property list above, not produced by the code; the code's own argv was never given to the real CLI (tests start the fake CLI only).

So the decision is: **a home of the coder's own, plus ONE file of the real home, read-only.**

- `~/.claude/.credentials.json` — needed: it is the sign-in. Read-only, because the CLI did not write it in a call with a valid token, and a writable bind would let a compromised CLI replace it. NOT measured: what the CLI does when the access token has EXPIRED inside the unit. It would try to refresh and could not save the new token; whether the vendor rotates the refresh token then (which would end the person's sign-in outside) was not tested, on purpose — a test of it would spend the real token. **Open item for the owner**: until it is measured, a coder job started with a token that is close to its end is a risk to the person's sign-in; a key on the row (`ANTHROPIC_API_KEY`) has no such risk.
- `~/.claude.json` — NOT bound. The CLI made its own in the coder's home and answered; the person's one is a 170 KB history of projects and settings the coder has no use for, and a writable bind would let it be rewritten.
- `~/.claude/settings.json`, hooks, skills, plugins, `projects/` (the memory folders), `history.jsonl` — NOT bound. R11 runs with `--restricted`, which ignores user settings; with no home to read them from the CLI cannot load a hook the person set, and cannot write one that would run in the person's own sessions. The cost is that the coder sees no user skill, plugin or memory; that is the point.
- What the CLI writes lands in `C/home` and `C/tmp` and goes away with the job folder. The transcript of a coder turn (`C/home/.claude/projects/<slug>/<session>.jsonl`) is therefore NOT in the person's `~/.claude/projects`: a `--resume` of a coder session needs the SAME coder home, so the job folder must outlive the turns of the job.
- The CLI's own folder (`~/.local/share/claude/versions/`) — bound read-only (the program's folder). Nothing is written there.
- The host network and the resolver file — needed; the network is the one property that differs from the checks, and is recorded in every result as `host` with its reason.

## What the layer does, read off real units (tests)

`ACoderBoundaryTests` (8), `AFixtureCheckThatEscapesIsStoppedTests` (5), `ACoderJobWithNoBoundaryIsRefusedTests` (23), `ACoderJobRecordsAChangeOfTheMainCheckoutAsEvidenceOnlyTests` (4): a fake coder (`write-files`) that writes the main checkout, its `.git`, a file beside the worktree, another job's worktree and the `.git` pointer is stopped by the OS on every one (EROFS), its worktree write lands, the main checkout is byte-equal (hashed with its `.git`), and the outcome is `WriteStopped` — never green — naming each path. The real home is a stand-in temp folder (the seam `BoundaryHost.RealHome`); because the unit replaces `/tmp`, the stand-in is unreachable whatever `ProtectHome` says, so a control starts a unit the preflight would refuse with the stand-in bound in, and the same attempts then land.

Limits, stated: the OS layer stops WRITES; a read of any file the unit can see is not stopped (a read-only bind does not stop reads, R12 d). The word "stopped" in an outcome comes from the coder's own stderr carrying the OS's `Read-only file system` line (the fake prints it with the path); the real CLI reports a failed tool inside its transcript, not on stderr, so for the real CLI the outcome will not say "stopped" — the checkout is still unwritten, because the OS refused, and the worktree diff coai publishes cannot hold a write that never happened. Reading the transcript in the coder's home for `EROFS` is a possible later addition; its format was not measured here.
