# Coder write boundary — security judgement (2026-11-23)

Judge: Fable (security judgement role), read-only, no model call. Input: `RESULTS_coder_write_boundary.md`
(claude 2.1.330, flag set R11), the s2 task files, server.md §10/§16/§19/§20/§22, the launch and toolchain code.
Two `systemd-run --user` probes were run on throwaway scratch folders. The recommended property list below was
NOT yet run with a real `dotnet build` or the test suite — t4 measures it.

Result: the t3, t4 and t5 task files were amended by the orchestrator from this judgement (same commit).

## Must-change list

1. **The measured WSL property set (RESULTS OS check, `ReadOnlyPaths=<main>` + writable `<main>/.git`) is not a
   boundary for the checks.** Measured inside exactly that set:
   - `systemd-run --user touch <main>/via_dbus` from inside the unit succeeded (user D-Bus socket visible → a check
     can start a new unit with no limits).
   - `/mnt/c/Windows/System32/cmd.exe /c echo` ran (WSL interop reaches the Windows host).
   - `git update-ref` of another branch and writes to `<main>/.git/` and `<main>/.git/worktrees/<id>/` succeeded.
   - The unit received `SSH_AUTH_SOCK`, `DBUS_SESSION_BUS_ADDRESS`, `GPG_AGENT_INFO`, `XDG_RUNTIME_DIR` from the user
     manager, not from coai.
   - Good: `/proc/<outside pid>/root/...` was refused; `uid_map` showed a user namespace (`PrivateUsers` implied).
2. **Close the `.git` write hole for the checks.** Build and test do not commit; coai commits outside the unit. With
   `<main>/.git` writable a check can change submodule hooks/config under `.git/modules`, the `commondir`, `gitdir`,
   `index`, `HEAD` of other registered worktrees, every ref and the object store. Make all of `<main>` read-only.
3. **Harden coai's own git commands on a coder worktree.** `GitHistory.cs:319-320` runs git with cwd = the path, no
   `-c` options, the whole inherited environment. A check can replace the worktree's `.git` pointer file. So: read
   the base toolchain from the main repository path (not through the worktree, `ChecksAtBase.cs:21,37-58`); bind
   `<worktree>/.git` read-only during checks and compare before each git command; pass
   `-c core.hooksPath=/dev/null -c core.fsmonitor=false`; cwd = worktree root; never `git submodule update` there.
4. **t4 d2 (`boundary: cwd-only`) and d3 (refuse with no effective OS boundary) contradicted each other.** Decided:
   refuse.
5. **Test the environment from INSIDE the unit.** `CheckRunner.cs:92-99` sets a minimal list on the process it
   starts, which under `systemd-run` is `systemd-run`; the unit gets the manager's environment. Use `env -i` inside.
6. **Keep timeout and kill.** Under `systemd-run` the tree belongs to the user manager. Named unit, `RuntimeMaxSec`,
   `systemctl --user stop` on timeout/cancel, a test that no process stays.
7. **A preflight self-test decides "effective"**, not the presence of `systemd-run` (sandbox options can fail open,
   same class as R7). Any wrong answer refuses the job (plan gate).
8. **t3: measure the open items before the argv is frozen** (about 6 haiku calls).
9. **t5 d1 could not be true as written**: FakeCli does not obey `--restricted`; the stop must come from an OS layer.

## 1. R11 — verdict: yes as the base, gaps to measure, three additions

Keep R11 exactly as measured. Not measured, each matters:
- Project hooks at the base commit: this repository commits `.claude/settings.json` with SessionStart/PreToolUse
  hooks (`node .claude/hooks/*.mjs`). "`--restricted` ignores project settings" comes only from `claude --help`. A
  check in round 1 could write a hook that claude runs on the resumed turn, outside any OS boundary. Measure fresh
  and resumed with a canary outside.
- A main checkout under `/home/jinx/git` (the user setting adds that folder to the working directories).
- Glob and Grep outside the worktree (a read outside leaks through the commit and the PR).
- Write of a NEW file through a directory symlink (a check or the base commit can make symlinks).
- The tool list: assert the init line `tools` and `mcp_servers` arrays exactly (five tools, no MCP server; this
  account has claude.ai connectors).
- Memory: can the coder write the auto-memory folder under `~/.claude/projects/`, shared with the main checkout?
- A resume with fewer flags: the handle must be bound to the job and the worktree.

Additions: minimal environment allow-list for the coder launch (the consultant today inherits the whole
environment, `ProcessLauncher.cs:99`, `ClaudeConsultant.cs:148-160`); capability check for `--permission-prompts`
and `acceptEdits` plus a version floor 2.1.330 recorded in the job envelope; candidates to measure then add:
`--disable-slash-commands`, `--safe-mode` (also drops CLAUDE.md → conventions go in the prompt). `--bare` is not
usable with a subscription sign-in.

Git: the coder cannot write `<main>/.git` or the `.git` pointer (R11g). It can write `.gitattributes`,
`.gitmodules`, `.githooks/`, `.husky/` — these run code only when trusted config points at them; this repository's
local and global config have no `core.hooksPath`, filter or fsmonitor key, so no path today; must-change 3 covers
other repositories.

## 2. Check commands — verdict: the measured boundary is not enough

Recommended unit (started on this machine: worktree write ok; main and `.git` read-only; `git status` exit 0;
`/run/user` and `/mnt` empty; `/run/WSL` denied; HOME an empty read-only tmpfs; only `lo`; `dotnet --version` ran).

Must-have: `ProtectSystem=strict`, `ProtectHome=tmpfs`, `TemporaryFileSystem=/run/user`, `TemporaryFileSystem=/mnt`,
`InaccessiblePaths=-/run/WSL`, `BindPaths=<worktree>` as the only writable source path, `BindReadOnlyPaths=<main>`
(no write hole) and `BindReadOnlyPaths=<worktree>/.git`, `BindPaths=<job temp>:/tmp` and `:/var/tmp` on a real
disk, `TemporaryFileSystem=/dev/shm`, a per-job writable HOME (`HOME`, `DOTNET_CLI_HOME`), `NoNewPrivileges=yes`,
`PrivateUsers=yes`, named unit + `RuntimeMaxSec` + `MemoryMax` + `MemorySwapMax=0`, `env -i` allow-list inside, and
the preflight (write to main fails; write to `.git` fails; `systemd-run --user true` fails; a Windows `.exe` in the
worktree does not run; HOME is not the real HOME; worktree write works).

Caches: `~/.nuget/packages` READ-only (`BindReadOnlyPaths` + `NUGET_FALLBACK_PACKAGES`) with a per-job writable
`NUGET_PACKAGES`; a per-job `npm_config_cache`; `~/.nvm` read-only.

`PrivateNetwork=yes`: rated must-have for the test step; a package restore then needs a separate declared step with
network. Measure this repository's suite under it first.

Nice-to-have: `RestrictSUIDSGID`, `LockPersonality`, `PrivatePIDs`, `ProtectKernelTunables`, `ProtectControlGroups`,
`TasksMax`, a system call filter.

Refuse the job when there is no user manager, the preflight fails, or the main checkout or worktree cannot be bound.

## 3. Windows — verdict: refuse coder jobs on native Windows in this epic

No OS boundary and no write probe exist there; decision 8 makes the snapshot evidence only, so `cwd-only` is no
boundary. A Windows user can run the job in WSL. Same for macOS and Linux with no user manager unless the preflight
passes. A later `cwd-only` mode needs an owner decision and a person-set device switch, and must never say "bounded".
(Morning question for the owner; the safe default holds until then.)

## 4. DoD changes (applied)

- t3: environment allow-list test; needed flags + version floor; handle bound to job and worktree; open
  measurements as d0; `ClaudeCapability.cs` in files; write launch as a third case of the `LaunchConfinement` union so
  `acceptEdits` cannot reach a consult or question launch.
- t4: property set + preflight replace cwd-only; environment read inside the unit; timeout stops the unit; result
  records boundary, properties, network state; `CheckRunner.cs`, `ChecksAtBase.cs`, `GitHistory.cs` in files. The
  formatter is NOT in scope of t4 (`CheckStep` has build and test only; formatting is s4's format-commit-check).
- t5: the claude coder process runs inside the R12-shape OS layer (option a); scenarios for the D-Bus escape, the
  `.git` write, the HOME write, and the refused job on a platform with no boundary.
