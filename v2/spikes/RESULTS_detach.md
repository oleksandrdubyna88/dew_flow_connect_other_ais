# coai v2 spikes - detached role runner, results 2026-11-23 (epoch 3 E1 s1 t1)

Facts only. No product code. Scratch files: `/tmp/claude-1000/-home-jinx-git-dew-flow-conventions/4c91ec34-3816-4f0d-83a8-d95afa710fba/scratchpad/detach/`. No real config was edited and no user setting was changed (no `loginctl`, no `wsl.exe`). All units were named `coai-detach-*` and are gone (`systemctl --user list-units 'coai-detach-*' --all` = 0 rows).

Environment: WSL2 kernel 6.18.58.1, systemd 259 (259.5), PID 1 = systemd, `systemctl --user is-system-running` = `running`, Linger=yes (spike 2 of `RESULTS_spikes_2026-11-23.md`). The shell that ran the tests was itself inside a tmux scope (`app.slice/app-tmux.slice/tmux-spawn-*.scope`).

The stand-in "runner" is `worker.sh`: it writes `<out>.start` (its pid, ppid, process group, session, cgroup), writes a heartbeat every second for 12 s, then writes `<out>.end`. The stand-in "client" starts it and then stays alive (`sleep 300`). A case passes when `<out>.end` appears after the client is dead.

## 1. The client process tree is killed (the open case of spike 2)

| Case | Launch path | What was killed | Result |
|---|---|---|---|
| A | `systemd-run --user --collect --unit <u> -p MemoryMax=300M -p MemorySwapMax=0 worker.sh` | `kill -9 -- -<pgid>`: the launcher's whole process group (the launcher is its own session leader, so group = session) | **runner survived**, wrote `.end` 12 s later. Its parent was the user manager (ppid 385), its own session and group, cgroup `app.slice/<unit>.service` |
| B | same | the client ran inside its own transient unit; `systemctl --user kill --kill-whom=all --signal=SIGKILL` killed that unit's whole cgroup (client shell + its `sleep`) | **runner survived**, `.end` written. The runner unit stayed `active running` after the client unit was gone |
| D | `setsid -f worker.sh` | `kill -9 -- -<pgid>` of the launcher's group | **runner survived**, wrote `.end`. Its cgroup was the CLIENT's tmux scope (it did not get a cgroup of its own) |
| C | `setsid -f worker.sh`, client inside its own transient unit | the client unit's whole cgroup (SIGKILL) | **runner died** (no `.end`, no process left). `setsid` leaves the runner in the client's cgroup, so a cgroup kill takes it |

Fact: only `systemd-run --user` gives the runner a cgroup of its own. `setsid -f` survives a process-group or session kill, and does not survive a kill of the client's cgroup (a client that runs in a systemd scope or service, stopped by `systemctl stop`, or an editor/terminal scope torn down).

## 2. No user manager

How a missing user manager shows (simulated by removing the variables the client uses to find the bus; the real manager stayed up):

| Variables | `systemd-run --user ... /bin/true` | `systemctl --user is-system-running` |
|---|---|---|
| unchanged | rc 0, 14 ms | rc 0 `running`, 10 ms |
| `XDG_RUNTIME_DIR` unset, bus variable set | rc 0 (the bus variable is enough) | rc 0 |
| both unset | **rc 1**, 8 ms, `Failed to connect to user scope bus via local transport: $DBUS_SESSION_BUS_ADDRESS and $XDG_RUNTIME_DIR not defined (consider using --machine=<user>@.host --user to connect to bus of other user)` | rc 1, same text |
| `XDG_RUNTIME_DIR=/nonexistent`, bus variable unset | **rc 1**, 8 ms, `Failed to connect to user scope bus via local transport: No such file or directory` | not run |

Fact: a missing user manager is a fast, clean non-zero exit (rc 1, under 10 ms) of `systemd-run` itself; no hang. The rule "try `systemd-run`; on any non-zero exit use `setsid -f`" needs no separate probe, and the answer to "which launcher ran" is recorded from what actually happened. The `setsid` path with the launcher killed is case D above (survives a group kill).

Other `systemd-run` failures measured: a second unit with a name already loaded -> rc 1, `Failed to start transient service unit: Unit <name>.service was already loaded or has a fragment file.` (so a unit name that repeats must be treated as "not launched by this path", never as success).

## 3. What a `systemd-run --user` unit does NOT inherit

A unit starts from the user MANAGER's environment, not the launcher's:

- A variable set in the launcher (`COAI_PROBE_VAR`) was **unset** in the unit.
- `PATH` in the unit was `/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/games:/usr/local/games:/snap/bin`. The launcher's `PATH` starts with `~/.local/bin`, `~/.kimi-code/bin`, `~/.nvm/versions/node/v26.12.1/bin`. So a `claude`, `codex` or `node` that lives in those folders is **not found** from the unit unless the runner restores `PATH`.
- The working directory is `$HOME` unless `--working-directory=<dir>` is given (measured: `/tmp` was honoured). `--setenv=NAME=value` works, but it puts the value in the unit's properties (`systemctl --user show`), which spec A forbids for a key.
- `HOME` and `XDG_RUNTIME_DIR` were right.

Consequence for the design: `env.json` must carry the allow-listed names the CLIs need **including `PATH`** (and `HOME`, the data-directory variable, the `COAI_*` names), and the unit gets `--working-directory`. A runner that did not restore `PATH` would fail every role start with "CLI not found" on a normal developer machine.

## 4. Other unit facts used by the design

- Start cost: `systemd-run --user` returned in about 14 ms (the 2 s answer limit of `start_role` is not at risk).
- A live unit shows both limits: `systemctl --user show <unit> -p MemoryMax -p MemorySwapMax` = `MemoryMax=314572800` and `MemorySwapMax=0` for `-p MemoryMax=300M -p MemorySwapMax=0`; `KillMode=control-group`; `MainPID` is the runner.
- A unit started with `--collect` leaves no record after it ends (`status` answers not-found), so the result of a runner must come from the job files, never from `systemctl`. A unit killed from outside without `--collect` ends `ActiveState=failed Result=signal ExecMainStatus=9` and stays listed until `reset-failed`.
- `KillMode=control-group` (the default): stopping the unit kills every process in its cgroup, the CLI child included.

- **An oom-kill stops the whole unit unless `OOMPolicy=continue` is set** (measured at the end of s1 t3, 2026-11-23). A parent that starts a memory hog as its child, in a unit with `-p MemoryMax=300M -p MemorySwapMax=0`: with the default policy the kernel killed the hog at about 280 MB AND systemd stopped the unit — the parent wrote no result (no `exit=` file, unit inactive). With `-p OOMPolicy=continue` added, only the hog died: the parent survived, saw exit code 137 (SIGKILL) and wrote it. A runner whose CLI child is oom-killed must survive to fail over (spec A14), so the launcher passes `OOMPolicy=continue`. Spec A named only the two memory properties; this is a third.
- **A managed .NET child never reaches the kernel's kill.** The runtime reads the cgroup limit and caps its GC heap at three quarters of it: a managed hog in the same unit ended with an `OutOfMemoryException` (exit 97 in the test stand-in), not SIGKILL. A vendor CLI that is a node or native program is killed by the kernel; a .NET one dies of its own exception. Both are a crash of the child; only the exit code differs.

## 5. Not measured here

- **Windows, client job object.** No Windows host is reachable from this run. Case "a client in a job object with KILL_ON_JOB_CLOSE kills a `DETACHED_PROCESS` child" is **not measured here**; `CREATE_BREAKAWAY_FROM_JOB` and the `windows-in-job` fallback of spec A stay as designed, untested. The launcher's Windows branch ships behind that gap and must be measured on a Windows machine before anyone relies on it.
- **`wsl --terminate` / WSL shutdown.** **Not measured here**: it ends the whole distro, which would stop this run, the other coders and every unit. A terminated distro stops its user manager, so it ends every `systemd-run` unit (stated by design, not measured); spec decision D6 (no resume in E1) stands.
- **Linger=no** (a user manager that stops at the last logout): not measured; changing the account's linger setting is outside this task. With Linger=no the units would end with the manager.
- Real memory pressure from other processes while a unit is over its limit; `CPUQuota` / `TasksMax`; a `systemd-run --user` call from a cron or non-login context beyond the bus-variable cases of section 2.
- macOS: no systemd; the launcher falls to `setsid` (not measured on macOS).

## 6. The launcher order of spec A: confirmed, with two corrections

Order confirmed: (1) Linux with a user manager: `systemd-run --user --collect --unit coai-role-<jobId> -p MemoryMax=<MB>M -p MemorySwapMax=0 ...`; (2) otherwise `setsid -f`; (3) Windows `CreateProcess` with `DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB`, falling back without breakaway (`windows-in-job`, unmeasured, see 5).

Corrections / additions to record in the code:

0. **Add `-p OOMPolicy=continue`** next to the two memory properties (section 4, last two bullets).

1. **Choose by trying, not by probing**: run `systemd-run`; any non-zero exit (no bus: rc 1 in under 10 ms; duplicate unit: rc 1) goes to `setsid -f`. The launcher name recorded is the one that worked.
2. **The unit needs `--working-directory` and an environment restored from `env.json` including `PATH`** (section 3). Without it the unit cannot find `claude`/`codex` on this machine.
3. **`setsid` is weaker than the unit** (section 1, case C): it survives a group or session kill but not a cgroup kill of the client's scope. The job record says `launcher: setsid`, and a client that is stopped as a unit must not expect the role job to outlive it.
4. The result of a `--collect` unit cannot be read from systemd; the job files are the only record (already decision 1 of spec section 0).
