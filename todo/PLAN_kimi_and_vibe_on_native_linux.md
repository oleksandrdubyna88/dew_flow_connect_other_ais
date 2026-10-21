# PLAN — Vibe and Kimi on a native Linux host: measure there first, then admit each one that passes

> Status: **plan only, nothing implemented yet, 2026-10-21 — measurement first; no admission ships before its cells.** Scope: the
> probes in `scripts/` (three small fixes and one read-only facts script), the measurement on the owner's native Ubuntu host
> `ubuntu-claude` (SSH), then the per-runtime admission in `src_mcp/runners/Platform/HostAdmission.cs`, the extension's host notes and
> help, `shared/consultant-limitations.json`, and the RESULTS documents.
>
> Parent (implemented): [PLAN_kimi_and_vibe_on_linux.md](../research/PLAN_kimi_and_vibe_on_linux.md) — DL1 refused a native Linux host
> "until one native-Linux run of L-V9 is recorded" (§2 item 1, DL1, §5 row L-V10, and its open tail). Evidence it rests on:
> [RESULTS_vibe_confinement.md](../research/RESULTS_vibe_confinement.md) §30–§31, [RESULTS_kimi_confinement.md](../research/RESULTS_kimi_confinement.md)
> §29–§30. Module docs: [module_runners.md](../research/module_runners.md), [module_extension.md](../research/module_extension.md).

## 1. The symptom

The owner works over VS Code Remote-SSH on a native Ubuntu host. coai-mcp 0.62.0 runs there (the extension installed it) and runs
rounds, but every Vibe row and every Kimi row is refused before anything starts:

- the server: `HostAdmission.Refusal` answers `HostKind.Linux => Wsl2Only(runtime, "it is measured on WSL2 and not yet on a native Linux host", …)`
  (`src_mcp/runners/Platform/HostAdmission.cs:26`), reached from `VibeLaunch.HostRefusal` (`src_mcp/runners/Vibe/VibeLaunch.cs:79`) and
  `KimiLaunch.HostRefusal` (`src_mcp/runners/Kimi/KimiLaunch.cs:29`, called first in `Prepare`, `KimiLaunch.cs:205`);
- the card: *"coai-mcp refuses a kimi row on a native Linux host for now — Kimi is measured on WSL2 only"*
  (`src_vs_code/src/runtimeSince.ts:152-153`), and the same for vibe.

The refusal was right when it was written: every Linux cell was WSL2. `VibeLaunch.cs:72-78` names what was never measured on a native
host — *"a desktop keyring Vibe may use instead of `.env`, other filesystems, other packaging"*.

**The owner's order (2026-10-21): measure on the native host first, then admit. Paid runs are approved.**

The host, checked by the lead over `ssh ubuntu-claude` on 2026-10-21: Ubuntu, kernel `7.0.0-30-generic` (no Microsoft in the banner, so
`HostKind.Linux`), user `root`, node and git present, `vibe 2.26.1` at `/usr/local/bin/vibe` and signed in (`~/.vibe` exists), Kimi NOT
installed (no `kimi`, no `~/.kimi-code`), the VS Code server's `PATH` without `/root/.local/bin`.

## 2. The goal — what is true when this plan is done

1. Every WSL2 cell that the WSL2 admission rested on, and that can differ on a native host, is re-run on `ubuntu-claude` and recorded —
   Vibe now, Kimi after the owner installs it and signs in himself.
2. A runtime whose native cells all pass is admitted on `HostKind.Linux`; a runtime that is not measured, or failed a cell, keeps
   today's refusal sentence. The admission is **per runtime** — Vibe can be admitted while Kimi is still refused.
3. An unreadable kernel banner is never admitted as "native Linux" (§4 DN6).
4. The card, the install note, the help in five languages and the limitation rows say what the side's coai-mcp actually does.
5. Nothing of the person's sign-ins is read, printed, copied or written by any step of this plan.

## 3. What the WSL2 admission rested on, and what a native host needs

Read from the parent plan §3/§5 and the RESULTS sections. "Re-run" means the cell can differ on a native host; "does not apply" says why.

### Vibe (the WSL2 row is `confined` on L-V9, 3 of 3 — RESULTS_vibe §31)

| WSL2 cell | Script | What it proves | Native host |
|---|---|---|---|
| L-V1 | `probe-vibe-confinement.mjs --cells M-V7` (arms a, a2, b) | a fresh owned home is signed out (free, a known wording); a home with the `.env` LINK is signed in | **re-run** — this is the keyring question: on a desktop Linux a keyring could sign a fresh home in by another road |
| L-V2 / L-V3 | `--cells M-V6`, `--cells M-V1` | read inside the cwd served, outside denied; `write_file`, `bash`, `run_typescript` denied, no file | **re-run** — the confinement claim itself |
| L-V4 | `--cells M-V5` | `--resume` keeps the session (the two-turn consult uses it) | **re-run** — cheap, and part of L-V9 |
| L-V7 | `vibe-home-manifest.mjs` | the `linux` home-manifest entry: 12 pinned hashes | **re-run without `--write`** — node calls both WSL and native `linux`, so ONE entry serves both; if the plugin bytes differ here, every native launch of a KNOWN 2.26.1 is refused (`ByHash`, `src_mcp/runners/Vibe/VibeHome.cs:216-221`) |
| L-V6b | `dotenv-name-vectors.mjs --check` | python-dotenv in Vibe's own environment binds what DL2's grammar expects (48 vectors) | **re-run, free** — this host's uv tool environment may carry another python-dotenv |
| L-V5 | (scout) | no Windows path budget | does not apply — `VibePathBudget.ForThisProcess` is `None` off Windows (`VibePathBudget.cs:38`); the kernel limits are the same |
| L-V-home | (scout) | `HOME` redirect harmless | does not apply — not adopted (DL13), no product path uses it |
| L-V6 | — | the names in the owner's `.env` | does not apply — never run, by rule (the `.env` is never read) |
| L-V8 | (E1 script) | Vibe does not load a `.env` from its working folder | does not apply — a property of Vibe 2.26.1's Python code (`GLOBAL_ENV_FILE` only, §30.8), the same package on every Linux; re-run only if the host's Vibe is another version |

### Kimi (the WSL2 admission rested on RESULTS_kimi §29, plus L-N1/L-N2/L-N3 in §30)

| WSL2 cell | Script | What it proves | Native host |
|---|---|---|---|
| L-K1 | (scout's `kt2.mjs`, session scratch) | `kimi --version`/`acp --help` in the built environment; `rg` found, no `bin/` in the owned home | **re-run** through the shipped shim (§4 DN2) |
| L-K2 | (scout) | signed in through the credentials SYMLINK; a refresh lands in the person's folder by temp + rename; link and modes kept | **re-run** through the shipped shim |
| L-K3 | (scout) | `HOME` → `os/` alone; Kimi's native cache moved by `XDG_CACHE_HOME`, `os/` stays empty | **re-run** — the product's cure (DL11) on a native filesystem |
| L-K4 / L-K6 | (scout) | `kimi acp`'s tree: `rg` in its own session survives a pid or group kill briefly; only the agent holds the lock fd; `comm` is `MainThread` | **re-run** with the REAL Kimi under the shipped shim's subreaper — on a native kernel an orphan goes to pid 1 or a `systemd --user` subreaper, not to WSL's `Relay` |
| L-K7 | (scout) | write arms refused 3 of 3; `session/load` works | write arms **re-run**; `session/load` does not apply — the shipped Kimi row never calls it (no `session/load` anywhere in `src_mcp/runners` or `src_mcp/src`) |
| L-N1, L-N2, L-N3 | `CoaiMcp.Tests` in the `ubuntu-latest` CI job | .NET's inheritable hold (native `LinuxHeldFile`), the subreaper teardown, the survivor marker | **already native** — GitHub's ubuntu runner is a native Linux VM, not WSL; green in CI run 39028293182 (RESULTS_kimi §30). Not re-run |
| L-K5, the L-K4 fake-agent harness | (scout) | generic `flock` and process-group kernel behaviour | does not apply — kernel facts, already covered natively by L-N1/L-N2 in CI |
| M-K12 (the real `~/.agents` canary) | — | — | does not apply — never run without the owner's yes, and not asked for |
| M-K14 (the refresh race) | — | — | unchanged — its own plan, it gates nothing here |

## 4. Decisions

- **DN1 — Admission is per runtime.** `HostAdmission.Refusal(runtime, …)` already takes the runtime's name
  (`HostAdmission.cs:22`). The `HostKind.Linux` arm becomes: admitted when the runtime is in ONE named set of runtimes measured on a
  native Linux host, else today's `Wsl2Only` sentence, unchanged. The set starts with the runtime(s) whose §5 cells passed. No second
  copy of the sentence, no flag in settings.

- **DN2 — Kimi is measured only through probe paths that pass the shim its owned root, so the 2026-10-20 gap cannot bite.** The known
  gap: `probe-kimi-confinement.mjs`'s legacy cells M-K1–M-K6 have no owned root (`--home`, `--persons-bin`, `--locks`): its own header
  says so (`scripts/probe-kimi-confinement.mjs:1-9`), and a shim arm without one throws *"no owned root"* at launch
  (`:498-502`). So:
  - the sign-in, tree, lock and teardown cells run through **`probe-kimi-tree-hold.mjs`**, which already makes an owned root
    (`makeOwnedHome`) and passes `--home <root> --persons-bin <bin> --locks <scratch>/locks` to the shim (`scripts/probe-kimi-tree-hold.mjs:205-213`);
  - the inventory cell runs through the D19 cell **M-K11c**, which already launches on an owned home (`scripts/lib/kimi-d19.mjs:207-232`);
  - the write arms (M-K3) and the read boundary (M-K6) are the only legacy cells needed, and **P1 re-plumbs exactly those two** onto an
    owned root, their `kimi -p` controls onto an owned home in the built environment — never the person's real home;
  - M-K1, M-K2, M-K4, M-K5 are not run here.

- **DN3 — The shim under test is the INSTALLED coai-mcp 0.62.0 on that host.** It is the product as the owner runs it (Kimi on Linux
  shipped in 0.61.0, so it holds the subreaper, the pidfd teardown, the native hold and the survivor marker). The `--ask-acp` mode does
  not ask `HostAdmission` (only the server's `KimiLaunch.Prepare` does, `KimiLaunch.cs:203-210`), so the shim runs on the native host
  today. Its path, `--version` and sha256 are recorded. No .NET SDK is needed on the host.

- **DN4 — The keyring question is answered by names and by a signed-out launch, never by reading a secret.** On Windows Vibe keeps the
  key in the OS keyring (service `ai.mistral.vibe`, user `MISTRAL_API_KEY`, RESULTS_vibe §12). What matters for coai is not where the
  person keeps it but whether a coai launch can reach it by any road coai does not control. So: (a) free facts — `~/.vibe/.env` by
  `lstat` only (type, size, mode, owner, mtime), the keyring-related package NAMES in Vibe's tool environment, the backend CLASS NAME
  `keyring.get_keyring()` picks under the built environment (never `get_password`), whether a D-Bus session address is set (yes/no);
  (b) the decisive cell — M-V7 arm a (a fresh owned home, no `.env` link, the built environment) must end with the *Missing
  MISTRAL_API_KEY* wording 3 of 3, and arm a2 (`VIBE_TEST_DISABLE_KEYRING=1`) the same. If arm a is SIGNED IN, a keyring reaches coai's
  launches on this host: that is a stop (§7), not an admission.

- **DN5 — Three probe gaps are fixed before any paid run (P1), each red first.** Found while writing this plan, verified against the code:
  1. **The Kimi guards and kill targets match `comm`, and `kimi acp`'s `comm` is `MainThread`** (L-K6). `lib/proc.mjs` reads only
     `pid`, `ppid` and `comm` (`scripts/lib/proc.mjs:17-38`); `probe-kimi-tree-hold.mjs`'s "no other kimi" guard filters
     `/^kimi(\.exe)?$/` on that name (`:128-137`), its kill target too (`:235`, `:293`). On Linux the guard misses every running ACP
     agent, and the `killed` arm never finds the `kimi` it waits for. Fix: the listing also reads the `exe` link's basename (with
     `" (deleted)"` stripped) and `argv[0]`'s basename, and one `isKimi` matches any of the three — the product's DL10 rule.
  2. **`probe-kimi-confinement.mjs` has no "no other kimi" guard at all, and hashes the person's credential files.** Every launch hashes
     every file of `~/.kimi-code` (`manifest(REAL_HOME)`, `scripts/probe-kimi-confinement.mjs:486`, `:519`; `manifest` hashes each file,
     `scripts/lib/kimi.mjs:70-72`) — that READS the bytes of `credentials/*.json`. Fix: `credentials/` is recorded by `lstat` (names,
     sizes, modes, mtimes, the slot's hex redacted), never opened; and the `isKimi` guard runs before every launch.
  3. **The probe's Linux built environment lacks `XDG_CACHE_HOME`.** DL11 put it into the product (`src_mcp/runners/Kimi/KimiEnvironment.cs:34`,
     `:94`), but `lib/kimi-home.mjs`'s `unixBuiltEnv` (`scripts/lib/kimi-home.mjs:192-201`) never got it, so a direct `kimi -p` control
     or `--version` of the probe fills `os/` — a probe artefact that would look like a product failure. Fix: the same cache folder
     beside `os/` as `KimiPlaces.Cache`.
  And one read-only script: `scripts/probe-native-linux-host.mjs` (the facts of §5 N-H1, N-V0, N-K0), reusing `lib/vibe.mjs`
  (`vibePython`, `homeListing`, `personsVibeHome`), `lib/kimi.mjs` and `lib/proc.mjs`. Vibe's probe needs none of the three fixes.

- **DN6 — An unreadable kernel banner is never "native Linux" for an admission.** Today an unreadable `/proc/version` reads as empty
  (`ProcVersion.Read`, `src_mcp/runners/Platform/WslGeneration.cs:44-54`), `NamesWsl` says no, and the host is `HostKind.Linux`
  (`HostKind.cs:56-59`) — harmless while Linux is refused. Once Linux is admitted, a WSL1 kernel whose banner could not be read would be
  admitted. So the admission asks for a READ banner: the generation gains an "unreadable" answer on Linux, and that host keeps a
  refusal with its own sentence (*"the kernel banner /proc/version could not be read, so coai cannot tell a native Linux host from WSL1"*).
  The extension has the same fallback (`src_vs_code/src/hostSide.ts:170-195`: unreadable → `linux`, "a refusal, never a promise"); it must
  stay a refusal there too once `linux` runs Vibe.

- **DN7 — The thresholds.** A confinement claim is 3 of 3 (the repository's rule, as L-V9 and the Windows cells). A teardown or lock
  cell is 3 of 3 per arm. An inventory is one run, compared with §29's list. Facts are one reading.

- **DN8 — The extension learns a second "since" per runtime.** `ON_LINUX_SINCE` answers "first server that runs it on WSL2"
  (`src_vs_code/src/runtimeSince.ts:46-72`). A new `ON_NATIVE_LINUX_SINCE` holds only admitted runtimes — `vibe` at the next `src_mcp`
  minor after 0.62.0 (0.63.0 at the earliest; the DL14 rule: whoever merges second bumps to the next minor, and the release keeps it
  level with the tag). `HOST_NOTES.linux` (`runtimeSince.ts:150-157`) becomes per runtime: an admitted runtime says "update" on an
  older server and nothing on a new one; a refused runtime keeps today's sentence at any version.

- **DN9 — Where the numbers go.** `RESULTS_vibe_confinement.md` gains §32 *Vibe 2.26.1 on a native Linux host*;
  `RESULTS_kimi_confinement.md` gains §33 (§31 is the spent-quota capture step of PR #900, §32 the Kimi 2.2.0 re-measurement of PR #898; take the next free number if either is renumbered) *Kimi Code on a native Linux host*. Each opens with the machine (banner, distribution,
  filesystems, the binaries' paths and versions), predictions written before the runs, and ends with spend and "what this does not
  show". Paths are written with `~`; the credential slot's hex, session ids and user names are redacted.

## 5. The cells

### Host facts (free, `scripts/probe-native-linux-host.mjs`, one reading)

| Cell | What is recorded |
|---|---|
| N-H1 | `/proc/version` (must name no Microsoft/WSL) and `/etc/os-release` `PRETTY_NAME`; glibc version (`pidfd_open` wrapper ≥ 2.36, else the raw syscall — DL9 F4); node version; the uid; `fs.protected_symlinks`; the mount TYPE (from `/proc/self/mountinfo`) of `/tmp`, `/root`, `/root/.local/share` (coai's Vibe homes and `kimi-locks` root, `KimiCredentialsLock.cs:88`, `VibeHomes.cs:10-11`), `/root/.vibe`, and `/root/.kimi-code/credentials` when present (a network type is refused by the product — DL6/Q7); the VS Code server's `PATH` (the `PATH=` line of its environment only, nothing else of it); where `vibe`, `kimi`, `rg`, `fd`, `flock` resolve, in the product's own order (DL5); the installed coai-mcp's path, `--version`, sha256 |
| N-V0 | `~/.vibe/.env` by `lstat` (type, size, mode, owner uid, mtime — never opened); the NAMES in `~/.vibe`; Vibe's tool Python and python-dotenv version; the installed distributions whose name holds `keyring`, `keyrings`, `secretstorage`, `jeepney` or `dbus` (names and versions); `keyring.get_keyring()`'s class name under the built environment (`env -i` + MinimalUnix + a fresh `VIBE_HOME`), never `get_password`; `DBUS_SESSION_BUS_ADDRESS` set in the SSH session, yes or no |
| N-K0 | (after the owner's install) `kimi --version` in the built environment; `kimi acp --help`; where `rg`/`fd` were found (`KimiTools` order: the person's bin, `PATH`, `~/.local/bin`); the credentials folder's NAMES (hex redacted), modes and owner; no file under it is opened |

### Vibe — ready now (`vibe 2.26.1` is signed in on the host)

| Cell | Command (from the copied checkout, §6) | Pass | Paid turns |
|---|---|---|---|
| N-V1 = L-V1 | `probe-vibe-confinement.mjs --vibe <DL5's vibe> --cells M-V7 --repeats 3` | arms a and a2: exit 1, *Missing MISTRAL_API_KEY*, free, 3 of 3 each (DN4); every signed-in home keeps `.env` as a link to `~/.vibe/.env` | 3 (arm b is paid on Linux, RESULTS_vibe §31.5) |
| N-V2 = L-V2/L-V3 | `--cells M-V6,M-V1 --repeats 3` | read inside served, outside denied 3 of 3; `write_file`, `bash`, `run_typescript` denied, no new file, 3 of 3 per tool | 12 |
| N-V3 = L-V4 | `--cells M-V5 --repeats 3` | the same session and the code word 3 of 3 | 6 |
| N-V4 = L-V7 | `vibe-home-manifest.mjs --vibe <…> --scratch <…>` (no `--write`) | no unknown path, no drift; the 12 `pinned` path→sha256 pairs EQUAL the shipped `linux` entry of `shared/vibe-home-manifest.json` (compared by a small script file kept beside the record; `measured` and `seen` sizes may differ) | 2 tiny |
| N-V5 = L-V6b | `dotenv-name-vectors.mjs --check` | 48 vectors, no drift | 0 |

Around every phase: `~/.vibe` listed (names, types, sizes, modes, mtimes) before and after — identical; `~/.vibe/.env` `stat` identical.
**Vibe spend: about 23 paid turns; cap 30.**

### Kimi — after the owner installs Kimi and runs `kimi login` himself on `ubuntu-claude`

| Cell | Command (from the copied checkout, §6; `--shim <installed coai-mcp>`) | Pass | Turns |
|---|---|---|---|
| N-K1 = L-K1 + L-K2 + L-K3 | `probe-kimi-tree-hold.mjs --arms normal,normal-tools --repeats 3` | answered 6 of 6 (signed in through the symlink; no subreaper, pidfd or "unreadable listing" refusal from the shim as root); both lock files Free ≤ 2 s after the shim's exit; the credentials folder keeps its names, `700`/`600`, the link not replaced; Grep/Glob served by the host's `rg`/`fd`, no `bin/` in the owned home; `os/` EMPTY after every launch; the report's `survivors` empty | 6 |
| N-K2 = L-K4/L-K6 + DL9 | `probe-kimi-tree-hold.mjs --arms killed,normal,killed-rg,normal --repeats 3` | per killed arm: the lock Held while `kimi` lives, Free ≤ 2 s after its exit; no descendant alive 2 s after `kimi`'s exit; where the orphan went is recorded (pid 1 or a `systemd --user` reaper); the `normal` right after it answers, or is Busy naming a survivor and then clears — never Busy for good; no `*.survivors.json` left under `<scratch>/locks` at the end | 12 |
| N-K3 = L-K7 writes | `probe-kimi-confinement.mjs --cells M-K3 --repeats 3` (after P1) | Write, Edit, Bash refused through the shim, no file, no outside canary — 3 of 3 per tool; each `-p` control (owned home) wrote once | 12 |
| N-K4 = read boundary | `probe-kimi-confinement.mjs --cells M-K6 --repeats 3` (after P1) | Read inside served, absolute outside refused, 3 of 3. The Grep/Glob arm is RECORDED, not a gate — the limitation row already says Grep and Glob read anywhere | 6 |
| N-K5 = L-K3 inventory | `probe-kimi-confinement.mjs --cells M-K11c --repeats 1` | what lands in the owned home is the same families as §29 (`cache/query-store`, `device_id`, `logs`, `oauth/<slot>`, `sessions/…`, `session_index.jsonl`); a new name stops Kimi (§7) | 2 |

Around every launch: the credentials folder by `lstat` only; the rest of `~/.kimi-code` listed (names, sizes, modes, mtimes) — identical
but for Kimi's own temp-and-rename of the slot file. **Kimi spend: about 38 turns; cap 50.**

## 6. The procedure on `ubuntu-claude`

**What is copied.** Not a checkout of the branch — only what the probes import: `git archive <commit> scripts shared .github/scripts/lib`
(the Vibe probe imports `../.github/scripts/lib/resolved.mjs`; the manifest and dotenv scripts read `shared/`). `<commit>` is this
branch's commit with P1 in it, recorded in the RESULTS header. Before copying, `node --test scripts/lib/*.test.mjs` is green on Windows.

**Where.** ONE folder per series, made with mode `700`: `/tmp/testsToDelete-native-<vibe|kimi>-<yyyymmdd>-<6 hex>/`, holding
`checkout/` (the archive, extracted), `tmp/` (the probes' `TMPDIR`), `run/` (the run scripts), `logs/`, and one `--scratch` folder per
phase. Every probe gets `--scratch` inside it, so no `mkdtemp` default lands anywhere else.

**How each probe runs.** The lead writes one run script per phase on Windows, copies it into `run/` with `scp`, and starts it:

```bash
ssh ubuntu-claude "nohup setsid bash /tmp/testsToDelete-native-vibe-20261021-a1b2c3/run/n-v2.sh \
  > /tmp/testsToDelete-native-vibe-20261021-a1b2c3/logs/n-v2.log 2>&1 < /dev/null &"
```

Each run script: `set -eu`; `cd <folder>/checkout`; then `env -i HOME=/root USER=root LOGNAME=root SHELL=/bin/bash LANG=C.UTF-8
TMPDIR=<folder>/tmp PATH=<the VS Code server's PATH from N-H1> node scripts/<probe> … --scratch <folder>/<phase>` (so the probe sees
the environment the product's server has, not the login shell's); the last line `touch <folder>/logs/<phase>.done`. `nohup setsid` keeps
a turn alive if the SSH link drops. The lead polls with `ssh ubuntu-claude test -f <…>.done` and reads only the log and the probe's
tables. The full outputs stay on the host until the record is written; what comes back by `scp` is the tables, the JSON rows and
`steps.json` — never a home folder.

**What is recorded.** Each cell's table into the RESULTS section (DN9), the predictions first, the commit, the shim's sha256, the
spend, and every deviation from this procedure.

**The kimi rule (one kimi per side).** Before each Kimi phase the lead asks the owner (`ask_user`) to close any `kimi` he has open on
`ubuntu-claude`; the probes' own guard (P1) waits up to 10 minutes for another `kimi` and skips the run if one stays. Never two Kimi
phases at once; no Remote-SSH round with a Kimi row while a phase runs.

**Never** — any of these ends the series and is reported:

- never open, read, print, copy, hash or write `~/.vibe/.env` (only `lstat`/`stat`); never open a file under `~/.kimi-code/credentials`
  (names, sizes, modes, mtimes only);
- never run `kimi login`, `kimi logout` or `vibe --setup` — the sign-ins are the owner's;
- never print another process's environment — only the `PATH=` line of the VS Code server;
- never end a process by name (`pkill`, `killall`); only a pid the probe started, by its own handle;
- never `chmod`, `chown` or move the person's files; never install anything (the owner installs Kimi, `rg` and `fd`);
- never `--allow-agents-canary`, never M-K11b or any arm that runs on the person's real `~/.kimi-code`;
- never delete outside the series folder.

**Clean-up, by its own path only.** After the record is written and reviewed: (1) list every symlink in the folder
(`find <folder> -type l -printf '%p -> %l\n'`) into the log; (2) `stat` `~/.vibe/.env` and list `~/.kimi-code/credentials`;
(3) `rm -rf -- '/tmp/testsToDelete-native-vibe-20261021-a1b2c3'` — the literal path typed out, never a variable, never a glob (GNU `rm -r`
unlinks a symlink, it never follows it); (4) the `stat` and listing again — identical. Then `ls -d /tmp/testsToDelete-native-*` shows
nothing of this series.

## 7. Stop rules

A failing cell keeps its runtime refused, and the failure is recorded with its numbers. Specifically:

- a confinement cell below 3 of 3 → that runtime stays refused;
- `~/.vibe` changes, or `~/.kimi-code` changes outside Kimi's own temp-and-rename of the slot file, or a sign-in link is replaced or
  re-targeted → stop that series at once;
- **N-V1 arm a is signed in** (a keyring reaches coai's launch) → stop Vibe; the owner decides (DN4);
- **`~/.vibe/.env` is absent** (the sign-in lives in a keyring) → stop Vibe before any paid run; the owner decides whether to make a
  `.env` sign-in himself — the lead never runs `vibe --setup`;
- **N-V4: a pinned hash differs, or an unknown path** → Vibe stays refused; one `linux` entry cannot serve both hosts, and a per-host
  entry is a new decision for the owner;
- **any Kimi launch exits 77 ("not signed in")** → stop the Kimi series at once, do not retry; the owner signs in again himself (a run
  of launches has signed the owner out before);
- another `kimi` still runs after 10 minutes → that run is skipped, never killed;
- a survivor after a NORMAL teardown, a lock still Held 2 s after `kimi`'s exit, or a Busy that never clears → Kimi stays refused;
- the Kimi on the host is not 2.2.0 and a cell fails → recorded with both facts; Kimi stays refused, and the owner decides whether to
  re-run that cell on WSL2 with the same version to tell host from version;
- the spend cap (Vibe 30, Kimi 50) is reached → stop and ask.

## 8. The code change after the measurement

Only for a runtime whose cells all passed. If only Vibe passed, only Vibe changes; Kimi's texts stay.

- **Server.** `HostAdmission` (`src_mcp/runners/Platform/HostAdmission.cs:3-37`): the `HostKind.Linux` arm per DN1, the unreadable
  banner per DN6 (`WslGenerations`, `WslGeneration.cs:20-34`; `VibeHost.cs:21-23` and `KimiHost.cs:18` compute the generation for
  `HostKind.Linux` too). The remarks at `HostAdmission.cs:3-11` and `VibeLaunch.cs:72-78` say what is measured where, naming
  RESULTS §32 (Kimi: §33). Nothing below the admission changes: the link, the homes, the locks, the mount rule and the teardown already
  take `HostKind.Linux or HostKind.Wsl` (`VibeSignIn.cs:29-32`, `VibeRuntime.cs:110`, `Junctions.cs:145-229`, `HostKind.cs:53`).
- **Limitation rows.** `shared/consultant-limitations.json` — the `vibe`/`linux` row (`:251-257`, `standing: unmeasured`) becomes
  `confined`, worded like the `wsl` row (`:262`) and naming §32; the `kimi`/`linux` row (`:277-283`) the same from §33 when Kimi passes.
- **Extension.** `runtimeSince.ts` (DN8); the Vibe hint (`src_vs_code/src/vendors.ts:297-302`) and the Kimi hint (`:314-319`); the
  install notes `VIBE_NOTE_LINUX` (`src_vs_code/src/vendorTerminal.ts:511-513`) and `KIMI_NATIVE_LINUX` (`src_vs_code/src/kimiLinuxLine.ts:133-134`);
  the unreadable-banner warning (`hostSide.ts:186-195`, DN6); the help in five languages — the *Add a model* paragraph
  (`helpContent.ts:115`, `helpDe.ts:87`, `helpEs.ts:87`, `helpRu.ts:87`, `helpUk.ts:31`).
- **Docs.** `research/module_runners.md` (the Vibe reviewer section at `:3043`, *Kimi on WSL2* at `:3958` and its diagram at `:3968`);
  `research/module_extension.md` (the host notes); the parent's open tail (`research/PLAN_kimi_and_vibe_on_linux.md:22`) gets a line
  pointing here.

## 9. Scenarios (each with its RED)

Probe fixes (P1) — `node --test scripts/lib/*.test.mjs`:

- **S-N1.** *Given* a fake `/proc` entry with `comm` `MainThread`, `exe` → `/root/.kimi-code/bin/kimi` and `argv[0]` `…/kimi`, *then*
  the listing calls it Kimi; an `exe` ending in `" (deleted)"` too; another uid's unreadable `exe` falls back to `argv[0]`. RED today:
  `"a running kimi acp (comm MainThread) was not seen as Kimi — the guard would let a second refresher start"`.
- **S-N2.** *Given* a fake home with `credentials/kimi-code-env-x.json`, *then* the confinement probe's record of the person's home never
  opens a file under `credentials/` (the hashing function is injected and must never be called for those paths). RED today:
  `"manifest() hashed credentials/kimi-code-env-x.json — a credential file's bytes were read"`.
- **S-N3.** *Given* the Linux built environment of `lib/kimi-home.mjs`, *then* it names `XDG_CACHE_HOME` beside `os/`, as
  `KimiEnvironment` does. RED today: `"the Linux built environment has no XDG_CACHE_HOME — a direct kimi launch fills os/ (DL11)"`.
- **S-N4.** *Given* M-K3's and M-K6's arms, *then* every shim arm carries an owned root and every `-p` control an owned home with
  `owned: true`. RED today: `"M-K3 Write has no owned root — the shim refuses it (E4.3), and the control would run on the real home"`.

Server (P3/P6) — `CoaiMcp.Tests`:

- **S-N5.** *Given* `HostKind.Linux` with a read, non-WSL banner, *then* a Vibe row starts, through a fake `vibe`, and the home holds
  the `.env` link (the native twin of `AVibeLaunchOnWslLinksTheSignInTests`). RED: the flipped S-V17 pins — `VibeRuntimeTests.cs:363-368`
  and `VibeLaunchControlTests.cs:192-201` now expect a start: `"a native Linux host should start a Vibe row now that it is measured
  (RESULTS_vibe §32), but it was refused: Vibe runs on WSL2 only…"`.
- **S-N6.** *Given* the admission matrix runtime × {Windows, WSL2, WSL1, native Linux, native Linux with an unreadable banner, macOS},
  *then* only the measured cells start, and Kimi on native Linux keeps its sentence until N-K passes (`KimiLaunchChecksTests.cs:196-201`,
  `KimiLaunchControlTests.cs:81-83`, `AKimiRowRunsThroughTheServiceOnWsl2Tests.cs:192-204` unchanged until then). RED: Vibe's native
  cell, as S-N5.
- **S-N7 (teeth for DN6).** *Given* native Linux with an UNREADABLE banner, *then* refused with the banner sentence. This test is green
  against today's code (everything on Linux is refused), so its teeth are proven the testing rule's way: apply the admission arm
  alone, watch it go red — `"an unreadable /proc/version was admitted as native Linux — a WSL1 kernel would start Vibe"` — then add the
  banner rule and watch it pass. Both observations go in the summary.
- **S-N8.** The limitation row for `vibe`/`linux` is `confined` and names §32; the loader test reads it.

Extension (P3/P6) — `npm test` in `src_vs_code`:

- **S-N9.** On `linux` with a server ≥ `ON_NATIVE_LINUX_SINCE.vibe` the Vibe card shows no host note; older shows "update"; Kimi still
  shows the refusal. RED: `vibeOnLinux.test.ts:116-122` ("refused at EVERY server version") flipped.
- **S-N10.** An unreadable banner shows a Vibe row as refused even though `linux` runs Vibe (`hostKindLogsAnUnreadableBanner.test.ts`).
  RED once `linux` runs Vibe: `"an unreadable banner promised a Vibe row"`.
- **S-N11.** The hint, the install note and the five help languages say Vibe runs on a native Linux host from that version
  (`vibeOnLinux.test.ts:186-195`, `:213-217`, `:302-309`, `:382-386` updated); `runtimeSinceIsAhead.test.ts` guards the new table.

## 10. Build order

1. **P1 — probe fixes and the facts script** (DN5; S-N1–S-N4 red, then green). The facts script and anything Vibe needs go first; the
   three Kimi fixes may land after the Vibe series has started — they gate only P5.
2. **P2 — the Vibe series** on `ubuntu-claude` (N-H1, N-V0, N-V1–N-V5), §6 and §7; RESULTS_vibe §32 written.
3. **P3 — Vibe admitted on native Linux** if every Vibe cell passed (§8; S-N5–S-N11). Release in the next `src_mcp` minor with an
   extension minor. If a cell failed: no code, the record only.
4. **P4 — the owner** installs Kimi on `ubuntu-claude` with the panel's install button (DL12's line — `rg`/`fd` by `apt-get`, then
   Moonshot's script) and runs `kimi login` himself. This also presses DL12's install button live on a fresh machine, an item of the
   parent's open tail.
5. **P5 — the Kimi series** (N-K0–N-K5); RESULTS_kimi §33 written.
6. **P6 — Kimi admitted** if every Kimi cell passed (the same §8 change for Kimi). Released with P3 if both are ready, else in the
   next minor.
7. **P7 — the live check and promotion.** After each release, on `ubuntu-claude` over Remote-SSH: the owner presses *Update*, the
   providers answer shows the row enabled with no refusal, and one live plan round gets the row's answer. Then `/promote-plan`; the
   parent's open tail updated.

## 11. Test plan

- `node --test scripts/lib/*.test.mjs` — S-N1–S-N4 red before P1's fix, green after (red messages recorded).
- `dotnet build src_mcp` then `CoaiMcp.Tests --filter-class "*Vibe*"`, `"*Kimi*"`, `"*HostAdmission*"` — S-N5–S-N8; then the whole suite.
- `npm test` in `src_vs_code` — S-N9–S-N11, and the whole suite.
- CI: all jobs green; the `ubuntu-latest` job keeps running L-N1/L-N2/L-N3 (native kernel).
- The measurement itself is the test of the claim: §5's pass column, recorded in RESULTS.
- `node .agents/conventions/tools/plan-lifecycle.mjs` clean after the plan, after each step, and at promotion.

## 12. Growth

The only thing this plan makes that grows is the series folder on the host. Projected: Kimi's native cache about 2 MB per owned root
(RESULTS_kimi §29, L-K3) × about 40 roots ≈ 80 MB; Vibe homes, exports and logs under 50 MB; total under 150 MB, in `/tmp`. Who
retires it: the lead, at the end of each series, by its own literal path (§6). Interrupted: a run that dies leaves its folder; the next
session lists `/tmp/testsToDelete-native-*` and removes each by its own path the same way. The product gains no new file, table or
process — the admission is a branch in a pure function.

## 13. Risks

- **The Kimi sign-in on the host is new and rotating.** A run of launches has signed the owner out before (exit 77). Mitigation: the
  stop rule, one kimi per side, and ~38 turns spread over phases, not one burst.
- **A second device's `kimi login` with the same Kimi account** may affect the owner's WSL2 sign-in. Not known; the owner logs in, so
  he decides. Nothing here reads either sign-in.
- **Root everywhere.** The host runs everything as `root`: the foreign-Kimi listing judges every root process (kernel threads, systemd),
  and the `0700`/owner checks all see uid 0. Predicted harmless (root reads every `/proc/<pid>/exe`); N-K1's "no unreadable-listing
  refusal" is the check.
- **The VS Code server's PATH** lacks `/root/.local/bin`; the probes run with that PATH on purpose, so a tool found only there is found
  the product's way (DL5) or not at all — what the product would do.
- **A different Kimi version** than WSL2's 2.2.0 mixes "host" with "version" in a failure (stop rule).
- **One `linux` manifest entry for two hosts** — N-V4 is the check; a mismatch is a stop, not a silent warn-and-run.

## 14. Owner prerequisites and questions

- Paid runs: approved 2026-10-21 (Vibe ≈ 23 turns, cap 30; Kimi ≈ 38 turns, cap 50).
- P4: the owner installs Kimi with the panel button and runs `kimi login` on `ubuntu-claude`; closes his own `kimi` before each Kimi phase.
- **Q1** — if `~/.vibe/.env` turns out absent (the sign-in in a keyring), may the owner run `vibe --setup` there to make a `.env`, or
  does Vibe stay refused on that host? (Default: stop and ask; the lead never runs it.)
- **Q2** — if N-V4 shows other plugin hashes on native, a per-host `linux` entry or warn-and-run? (Default: Vibe stays refused until decided.)

## Definition of Done

- [ ] P1: S-N1–S-N4 observed red with the messages above, then green; the facts script never opens a sign-in file.
- [ ] N-H1, N-V0 and every Vibe cell recorded in RESULTS_vibe §32 with predictions, the commit, the spend; `~/.vibe` unchanged.
- [ ] Vibe admitted on native Linux ONLY if every Vibe cell passed; else the failure recorded and nothing admitted.
- [ ] N-K0 and every Kimi cell recorded in RESULTS_kimi §33 (after the owner's install and sign-in); Kimi admitted only if all passed.
- [ ] An unreadable banner is refused (S-N7, teeth proven), on the server and in the extension.
- [ ] Limitation rows, hints, install notes, help in five languages and `ON_NATIVE_LINUX_SINCE` match what the server does.
- [ ] `module_runners.md`, `module_extension.md` and the parent's open tail updated.
- [ ] The series folders removed by their own paths; the person's sign-ins `stat`-identical before and after.
- [ ] A live round on `ubuntu-claude` through the released coai-mcp got the admitted row's answer.
- [ ] `plan-lifecycle.mjs` clean; the plan promoted with `/promote-plan`.

## The lead's answers (2026-10-21)

- **Q1 — the Vibe sign-in is in a keyring, not `~/.vibe/.env`:** the default stands. The series STOPS, the finding is
  recorded, and the owner is asked. The lead never runs `vibe --setup` and never writes `~/.vibe`.
- **Q2 — the native plugin hashes differ from WSL's:** the default stands. Vibe stays refused on the native host until a
  separate decision; the difference is recorded with both hash sets.
- **Who builds P1:** a Sonnet coder, red first, reviewed by an Opus judge, in this plan's single code gate. The Vibe series
  (P2) starts as soon as the facts script is merged into this branch; the Kimi probe fixes may land after it.
- **`ON_NATIVE_LINUX_SINCE`:** the version is the next minor of coai-mcp at the time of merge (0.63.0 today), with the
  second-merger rule: whoever merges second re-checks it against the changelog.
- **Section numbers:** Kimi's native-Linux record is §33 (see DN9); Vibe's is §32 of its own file.
