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
