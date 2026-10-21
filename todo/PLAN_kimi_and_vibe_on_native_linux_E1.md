# PLAN — E1 stories: trustworthy instruments for the native Linux host, and the admission's shared plumbing

> Status: **plan only, nothing implemented yet, 2026-10-21 — written after the E1 plan round (b54f22f5, `proceed`) and the cadence
> consult for epics 1–3 (e9a9b20e), both folded into the parent the same day.** Epic E1 of
> [PLAN_kimi_and_vibe_on_native_linux.md](PLAN_kimi_and_vibe_on_native_linux.md) (its `## Epics`). No cornerstone: E1 builds the
> instruments C1 and C2 are measured with, and the plumbing C1's spec (E2) starts from.
>
> Scope: `scripts/` (a facts script, `scripts/native/`, Vibe's M-V7 keyring arms, the Kimi probe fixes, N-K2's hard-kill pairs) and,
> in `src_mcp`, the per-runtime admission set (EMPTY), the unreadable-banner answer, a read-only keyring-session fact reader and
> `coai-mcp --host-facts`, plus its line in `.agents/PROJECT.md`. **No paid run** except the one WSL2 check of S3 (parent E1 *Risks*).
> **Nothing here changes which runtime starts on which host.**

## 1. Goal, before any solution

Before a single paid turn is spent on `ubuntu-claude`, every instrument must be trusted, and the product must have the place where an
admission will later be switched on. Today four things are wrong (parent §4 DN5, DN11, cadence consult e9a9b20e):

- the remote run can be hurt by the data it reads, runs `node` through a `PATH` nobody checked, loses `.done` when it is aborted, and its
  abort can miss a child that ignores TERM or left the group;
- there is no script that records the host's facts, and nothing in the product that states them, so N-V6 has nothing to compare;
- Vibe's probe measures today's OPEN keyring road, not the closed one E2 will ship; the Kimi probes mis-identify `kimi acp`, read
  credential bytes, fill `os/`, run two cells without an owned root, and never see an abrupt shim death;
- the admission has no per-runtime set, so E3 (Kimi) would have to wait for E2 (Vibe) to write one.

The silent failure this epic must not ship: an instrument that reports "pass" because it measured the wrong thing — a keyring road left
open, a TERM where a KILL was meant, a `kimi` not recognised, a process the abort never found.

## 2. What exists — verified at `449a20884`, 2026-10-21

| Fact | Where |
|---|---|
| The host admission: one switch, the Linux arm refuses with `Wsl2Only`; the sentences written once | `src_mcp/runners/Platform/HostAdmission.cs:22-28`, `:33-34` |
| Its two callers | `src_mcp/runners/Vibe/VibeLaunch.cs:79`, `src_mcp/runners/Kimi/KimiLaunch.cs:29` |
| The generation enum and its pure predicate; the banner read that turns "unreadable" into "" | `src_mcp/runners/Platform/WslGeneration.cs:6-11`, `:23-28`, `:44-54` |
| The hosts compute the generation only for `HostKind.Wsl` | `src_mcp/runners/Vibe/VibeHost.cs:18-23`; `src_mcp/runners/Kimi/KimiHost.cs:17-18` |
| The pins that must NOT move in E1 | `src_mcp/tests/KimiLaunchChecksTests.cs:198-201`, `:228-236`; `src_mcp/tests/VibeRuntimeTests.cs:366-368`, `:401` |
| The one-shot mode switch; `Version` and `Usage` (exit 64) as the read-only pattern; the writer modes | `src_mcp/src/Program.cs:336-380`, `:419-427`, `:323` |
| A one-shot mode in its own file with its own JSON context (Native AOT) | `src_mcp/src/Acp/KimiClearMarkerMode.cs:21` |
| The sanctioned-modes list, and the test that reads it off the switch | `.agents/PROJECT.md:53-55`; `src_mcp/tests/ARequestFaultIsNotAnOldBinaryTests.cs:52-95` |
| The process-listing seam and Linux reader | `src_mcp/runners/Kimi/ProcessListing.cs:22-26`; `src_mcp/runners/Kimi/LinuxProcessListing.cs:27`, `:32`, `:100-134` |
| The product's Kimi-identity rule | `src_mcp/runners/Kimi/ForeignKimi.cs:51-54`, `:115` |
| The shim's SIGTERM handling (what makes a TERM a CLEAN death) | `src_mcp/src/Program.cs:442-451`; `src_mcp/runners/Processes/StopSignals.cs:18-36` |
| The survivor marker's name | `src_mcp/runners/Kimi/KimiCredentialsLock.cs:135` (`<lock>.survivors.json`) |
| Vibe's built environment: exactly four names; the minimal Unix list | `src_mcp/runners/Vibe/VibeLaunch.cs:97-108`, `:115-119`; `src_mcp/runners/Processes/ProcessEnvironment.cs:90-91` |
| The probe's listing (`name` HOLDS `comm` today) | `scripts/lib/proc.mjs:15-35` (`:22`); its tests `scripts/lib/proc.test.mjs:17-22` |
| The tree-hold probe: arms, guard, `seen`, targets, the TERM kill, the arm-first loop | `scripts/probe-kimi-tree-hold.mjs:48-61`, `:131`, `:225-228`, `:235`, `:249`, `:293`, `:341-346` |
| The confinement probe: hashes of the real home; M-K3, M-K6; the "no owned root" throw | `scripts/probe-kimi-confinement.mjs:486`, `:519`, `:710`, `:757`; `:251`, `:310`; `:498-502` |
| `manifest` hashes every file | `scripts/lib/kimi.mjs:69-72` |
| The Linux built environment of the Kimi probe, without `XDG_CACHE_HOME`; the product's | `scripts/lib/kimi-home.mjs:192-201`; `src_mcp/runners/Kimi/KimiEnvironment.cs:34`, `:94` |
| Vibe's M-V7 arms; the env builder a test cuts out of the probe; the short-home maker | `scripts/probe-vibe-confinement.mjs:309-323`, `:471-482`, `:130`; `scripts/lib/vibe.test.mjs:214-227` |
| The probes' shared Vibe helpers | `scripts/lib/vibe.mjs:30`, `:40`, `:62-63`, `:77`, `:85`, `:197` |
| CI runs every `scripts/**/*.test.mjs` on `ubuntu-latest` | `.github/workflows/ci.yml:20`, `:197` |
| `.sh` files are checked out LF | `.gitattributes` (first rule) |
| Python `keyring` 25.7.0 in Vibe 2.26.1's tool environment: env before config; `fail.Keyring` raises; Vibe catches | `keyring/core.py:96-115`, `:150-155`; `keyring/backends/fail.py:21-28`; `vibe/utils/keyring.py:165-170` (parent DN10) |

## 3. Conventions every story follows

- **Tests first, RED on the assertion** — against a stub that compiles (or a script that runs) and answers the wrong value; never a
  compile error, a missing file or a setup fault. The RED message is the one written below, or one that names the same symptom.
- **Build and run** (`.agents/PROJECT.md` § Commands): `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then
  `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<Class>*"`, then
  the whole executable. **Never `dotnet test`.** Scripts: `node --test scripts/lib/*.test.mjs scripts/native/*.test.mjs`.
- **Bash tests run on Linux only** (the CI job, or WSL). On Windows each such test SKIPS with the reason in its name — never passes
  silently.
- **Break-its**: change PRODUCTION code (or the script under test), run the named test, see it red with the real symptom, restore, see
  it green; both runs go in the story's summary. Never edit, skip or disable a test to get green.
- **Sign-ins**: nothing in this epic opens, hashes, copies or prints `~/.vibe/.env` or a file under `~/.kimi-code/credentials` — fakes
  only, and the one real WSL2 turn of S3 goes through the shim's owned root.
- **Model roles (the operator's):** Opus writes each story's brief and reviews its diff; Sonnet codes, red first. No Fable spec: E1 has
  no cornerstone.
- `node .agents/conventions/tools/plan-lifecycle.mjs` clean after every story.

## 4. Stories

Order: the two that block the Vibe series come first (the facts script and safe remote execution — S1; the shared plumbing with
`--host-facts` that N-V6 reads — S2), then the probe arms (S3: Vibe's keyring arms first, then the Kimi fixes). E1 merges as one pull
request; the Vibe series starts after it.

### E1.S1 — The host is read, and run on, safely (facts script + `scripts/native/`)

**Goal.** One read-only script records the host's facts by its OWN raw reading; four small bash scripts make a series folder, run a
phase, abort it and remove it — with no host data in any script's text, node by its absolute path, `.done` on every exit, and an abort
that ends every owned process and nothing else.

**Files.**

- NEW `scripts/probe-native-linux-host.mjs` — N-H1, N-V0, N-K0 (parent §5). Reuses `lib/vibe.mjs` (`minimalEnv` `:40`, `builtEnv`
  `:62-63`, `vibePython` `:77`, `personsVibeHome` `:85`, `homeListing` `:197`), `lib/kimi.mjs` and `lib/proc.mjs` (`linuxProcesses`
  `:26-35`). Its pure core goes in NEW `scripts/lib/host-facts.mjs` (the probe runs on import and cannot be imported by a test, the reason
  `vibe.test.mjs:214` gives): the `lstat`-only sign-in record with an INJECTED reader, the raw keyring-session reading (the variable's
  presence, `$XDG_RUNTIME_DIR/bus` else `/run/user/<uid>/bus`, a `gnome-keyring-daemon`/`kwalletd*`/`keepassxc` in a listing), the
  banner reading, and the `--host-facts` recorder (exit 64 → "no such mode"). N-V0's two `keyring.get_keyring()` class-name readings
  (built environment, and built environment + `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`) run Vibe's tool Python with a
  one-line `-c` program that prints `type(keyring.get_keyring())`'s module and name and nothing else — never `get_password`.
- NEW `scripts/native/make-series.sh` — checks its own place (folder matches
  `^/tmp/testsToDelete-native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}$`, owned by root, mode `700`, not a symlink); makes `checkout/ tmp/ run/
  logs/ bin/`; writes `SERIES_PATH`; writes `run/path.txt` (the VS Code server's `PATH=` line only, from the server process whose `exe` is
  under `~/.vscode-server/`; none → exit 69 "Remote-SSH must be connected"); audits that `PATH` — an empty or relative entry, or a
  directory that is world-writable or not owned by uid 0 BEFORE the directory a used tool (`node`, `vibe`, `kimi`, `rg`, `fd`) resolves
  to, refuses with exit 77 naming the entry; resolves node once, `realpath`, into `run/node.txt`.
- NEW `scripts/native/run-template.sh` — the template of parent §6, verbatim in shape: `set -eu`; `export PATH=/usr/bin:/bin`; the one
  `SERIES_PATH` literal; `exec` to the log; `finish` on `EXIT`, `exit 143/130/129` on `TERM`/`INT`/`HUP`; `<phase>.handle` = pid, pgid
  (field 5), start time (field 22, read after the LAST `)` of `/proc/$$/stat`); `NODE` and `HOST_PATH` read as data; the probe started in
  the background under `env -i …` and `wait`ed.
- NEW `scripts/lib/native-run.mjs` — the Windows half, pure: `renderRunScript({ series, phase, probe, args })` fills the template and
  REFUSES a series or phase that fails its pattern, or a probe argument outside `^[A-Za-z0-9_.,=/:@-]+$` (they are the lead's literals,
  but they are written into a root-run script, so they are checked like data); `launcherArgv(series, phase)` returns the argument array of
  §6, every program by absolute path; `abortArgv`, `removeArgv` the same.
- NEW `scripts/native/abort.sh <phase>` — parent §6 steps 1–5: phase pattern; the handle's start-time check (leader alive → equal; leader
  gone → every group member started at or after it); the owned-set snapshot BEFORE any signal (group members by `/proc/*/stat` field 5,
  plus descendants of the leader and of each member by parent pid, each as pid + start time); `kill -TERM -- -<pgid>` and TERM each owned
  pid outside the group; after 10 s, while `kill -0 -- -<pgid>` succeeds or a snapshotted (pid, start time) lives, a second snapshot
  (union) and KILL; prints what it signalled and what survived to stdout.
- NEW `scripts/native/remove-series.sh <folder>` — parent §6 clean-up steps 1–5, all output to stdout, never into the folder.
- NEW `scripts/native/native.test.mjs` (bash, Linux) and `scripts/lib/native-run.test.mjs`, `scripts/lib/host-facts.test.mjs` (pure,
  everywhere).

**Scenarios.**

- **S-N15** (parent §9), `scripts/native/native.test.mjs`, each case in a `mkdtemp` folder renamed to match the pattern under the test's
  own temp root (the pattern check is given that root through one environment variable the TEST sets — the shipped scripts default to
  `/tmp`):
  - a rendered run script over a dummy probe that exits 3 → `.exit` holds `3`, `.done` exists, `.handle` holds three integers and its
    pid equals its pgid. RED (template with `trap finish EXIT` alone, the pre-round shape): `"a run script killed mid-probe wrote no .done
    — the poll would wait for ever"` on the TERM case;
  - the same with `kill -TERM`, `-INT`, `-HUP` of the group mid-run → `.done` each time, `.exit` 143/130/129;
  - dummy workloads: the probe starts a child that `trap '' TERM`s and sleeps, and a child that runs `setsid sleep 600`; an unrelated
    SENTINEL `sleep 600` started by the test itself. `abort.sh` → every owned process gone (checked by pid + start time), the sentinel
    alive. RED (an abort that escalates while `/proc/<leader>` exists, the pre-consult shape): `"the abort left the TERM-resistant child
    alive — it escalated on the leader, not the group"`; RED (no snapshot): `"the abort left the setsid child alive — it left the group
    and nothing else found it"`;
  - a `.handle` whose start time is rewritten → `abort.sh` signals nothing and says so; the dummy still lives (then the test ends it by
    its own handle);
  - `path.txt` holding `/usr/bin:/bin;$(touch /tmp/x)` and a backtick → the rendered script's text contains none of its characters
    beyond those of the literal template, and no `/tmp/x` appears;
  - `make-series.sh` with a `PATH` holding an empty entry, `.`, or a mode-`777` directory before node's → exit 77 naming the entry; a
    clean `PATH` → `node.txt` holds an absolute path and the run script runs THAT path (a decoy `node` earlier on the host `PATH`, in a
    root-owned folder the test made, is never run — it would write a marker);
  - `remove-series.sh` with a `SERIES_PATH` naming another folder → refused, nothing removed; a good one → the folder gone AND step 5's
    line (the `stat` after the `rm`) in its stdout — the rejected finding 3 of the E1 plan round, checked.
- **S-N19** (parent §9), `scripts/lib/host-facts.test.mjs`: the fake home's `.env` and credential file appear with their `lstat` facts and
  the injected reader is never called for either. RED (stub reading every listed file): `"the facts record opened ~/.vibe/.env — a
  sign-in's bytes were read (§2 item 5)"`; the raw keyring reading from fakes; exit 64 → `"no such mode"`.
- `scripts/lib/native-run.test.mjs`: a phase `n-v2;rm` or a series `/tmp/../root` is refused by `renderRunScript` and by `launcherArgv`
  alike; the launcher's every program is absolute. RED (a renderer that only checks the series): `"renderRunScript wrote phase
  'n-v2;rm' into a root-run script"`.

**Break-its.** (B1) drop `TERM` from the trap line → S-N15's TERM case red with the `.done` message; (B2) make `abort.sh` escalate on
`/proc/<pid>` → the TERM-resistant case red; (B3) skip the snapshot → the `setsid` case red; (B4) make the facts recorder `readFileSync`
the `.env` → S-N19 red; (B5) let `make-series.sh` accept a relative `PATH` entry → its case red.

**Docs.** `research/module_tests.md` — a section for the native scripts' suites (what each case pins, which run on Linux only, the
skip-with-reason rule); nothing in `module_runners.md` (no product code here).

**Done when.** S-N15, S-N19 and the renderer's cases observed red with the messages above, then green, on the Linux CI job and in WSL;
the bash tests skip by name on Windows; B1–B5 run both ways and recorded; Opus review has no open finding.

**Roles.** Opus brief (the template and abort steps are fixed by parent §6 — the brief adds the test root seam and the dummy
workloads); Sonnet codes; Opus reviews, reading every bash line for word splitting and an unquoted expansion.

**Status — E1.S1 coded 2026-10-21 on `feat/native-linux-e1` (not merged; the Opus review and the epic's code gate are still open).**
Built as written: `scripts/probe-native-linux-host.mjs` and its pure core `scripts/lib/host-facts.mjs`; `scripts/native/`
(`make-series.sh`, `run-template.sh`, `abort.sh`, `remove-series.sh`); `scripts/lib/native-run.mjs`; the suites `native.test.mjs`,
`native-run.test.mjs`, `host-facts.test.mjs` (what each pins: `research/module_tests.md`, *The native-Linux host scripts*). What the
build found, and what it did differently:

- **Bash 5 runs an `EXIT` trap when a fatal TERM/INT/HUP kills the shell** (seen on the Linux CI job, bash 5.2, and in bash 5.3), so
  the premise of the E1 plan round's finding 2 — "bash does not run an `EXIT` trap when a signal with its default action kills it" —
  is wrong, and B1's expected red (`wrote no .done`) cannot occur: a template with `trap finish EXIT` alone still writes `.done`. What
  the signal traps buy is the exit CODE: the trap runs with the status of the last command, not 128+signal, so without
  `trap 'exit 143' TERM …` a TERMed run would be recorded as `.exit` 0 — success. The cases assert `.exit` 143 / 130 / 129, and the
  B1 TEETH case asserts the mutant does not record 143. The traps stay.
- **`nohup` leaves SIGHUP ignored on entry and bash then cannot trap it** (the Linux job printed *the HUP was IGNORED*): the
  launcher of §6 (`nohup setsid bash …`) never runs `trap 'exit 129' HUP`; an abort ends such a run by TERM, which does work. The
  launcher keeps `nohup` — it is what keeps a phase alive when the SSH link drops — and the HUP case of S-N15 starts the script
  WITHOUT it. A second case pins the guarantee under `nohup` (a HUP never ends the run without `.done`).
- **Node resets every signal disposition at start-up**, so a dummy probe left alone dies of a group TERM/INT/HUP and the script ends
  through `wait`; in idle mode the dummy now swallows the three, so only the script's own traps can write `.exit`.
- **The break-its B1, B2, B3 and B5 are kept as tests** (TEETH): a mutation of a COPY of the script's text, in the test, asserting
  the condition the matching case would print as its failure; a mutation that no longer applies fails loudly. This replaces
  editing the production script and pushing a red commit (a deliberately weakened root-run script was not pushed).
- **No `<phase>.pid` file.** The handle (pid, pgid, start time) replaced it in the cadence consult; nothing reads a bare pid.
- **"Owned by root" is "owned by the user running the script"** (`id -u`) for the series folder, and the PATH audit trusts uid 0 *or*
  that user. On the host the user is root, so the rule is the plan's; unprivileged (the CI runner, WSL) it is the only owner a test
  can make. The foreign-owner arm of the audit is therefore not exercised by a test (a mode-777 folder, an empty entry, `.` and a
  relative entry are).
- **Two test seams, both environment variables read by the shipped scripts with the plan's values as defaults:**
  `COAI_NATIVE_SERIES_ROOT` (default `/tmp`) and `COAI_NATIVE_ABORT_GRACE` (default `10` seconds). `renderRunScript` and the argv
  builders take the same root as an option.
- **The abort tests the group's existence from `/proc`, not from `kill -0 -- -<pgid>`, and does not count a zombie as alive.** The
  decision is the same (the group, not the leader), but a zombie-only group answers `kill -0` for as long as nobody reaps it, which
  would make every abort wait out its grace period (the test's own `spawnSync` is such a case: it blocks the reaper). It polls every
  0.2 s and escalates at the grace deadline only if the group or the owned set still lives; it never signals a group member twice (a
  second TERM can cut the run script's own `EXIT` trap short, and that trap writes `.done`).
- **`path.txt` holds the PATH value alone** (the template says `PATH="$HOST_PATH"`), not the `PATH=` line.
- **`make-series.sh` lives in the series folder's root** (copied there by `scp` before it runs, as §6 says); `abort.sh` and
  `remove-series.sh` live in `run/` and find the series from their own place.
- **The argv builders take the host as an option** (`{ host }`, a pattern-checked name the caller supplies); no host name or address
  appears in any file here. `mkdirArgv`, `makeSeriesArgv` and `doneArgv` join `launcherArgv`, `abortArgv`, `removeArgv` — the other
  three remote steps of §6, so none is composed by hand.
- **The shape of a series folder is one literal** carried by `native-run.mjs` and the three scripts; a pure case fails if they differ.
- **No shared bash helper** for the pattern check: each script is copied to the host alone and run by its path, and a sourced file
  would be one more file a root-run step has to trust. The three copies are the same few lines and are pinned by that case.
- **`probe-native-linux-host.mjs` reads `comm` or `name` from `lib/proc.mjs`'s listing** (`p.comm ?? p.name`), so it works before and
  after E1.S3 renames the field; it does not touch `proc.mjs`.

#### Code gate and judge fixes (2026-10-21) — native scripts

The code gate (verdict good_enough) and the Opus judge read E1.S1's scripts and the facts script again. This block records what changed in
`scripts/native/*`, `scripts/lib/native-run.mjs`, `scripts/lib/host-facts.mjs` and `scripts/probe-native-linux-host.mjs`, with the red
observation of each. The bash cases run ONLY on the Linux CI job (`build · test · family checks`, step *Probe script tests*) and skip by
name on Windows; their red messages were read there, from a commit that held the new cases and the old scripts (`12f241e52`). The pure
cases were seen red locally. Where an item turned out to be already right, or not reproducible, the row says so.

| Item | What the code did | Fix | Red message (before) and break-it |
|---|---|---|---|
| **G3** | The finding cites `make-series.sh:~150`, `[ -z $NODE ] \|\| [ ! -x $NODE ]`. **No such line exists at `d794dc602`**: the script is 110 lines, and every `$NODE_CANDIDATE`, `$NODE_ABS` and the run script's `"$NODE"` were already quoted. | No code change. A case now makes a node directory WITH A SPACE in its name and proves it end to end: `make-series.sh` exits 0 with `node.txt` the absolute realpath, and a run script given the spaced path runs exactly that node. It guards the behaviour. | Green from the start — there was nothing to break |
| **G4** | An EMPTY entry's text was "an empty entry (the current folder)". The finding's second half ("only entries before the folder a used tool resolves to are audited") is what the code already did: `LIMIT` is the largest index at which any used tool resolves, and the loop stops there. | The text is "an empty entry (= the current directory)" — it matters now that a MISSING entry is refused (J6) and must not be what an empty one reads as. Cases: an empty entry is refused 77 and the message does not say "missing"; a missing and two empty entries AFTER node's folder are ignored. | `The input did not match the regular expression /PATH entry 0 '' is an empty entry \(= the current directory\)/u. Input: "refused: PATH entry 0 '' is an empty entry (the current folder)"`; the cut-off case was green at once |
| **G13** (security) | `stat -c` without `-L` read the symlink's OWN mode, which is always 777: **every symlinked entry was refused as "world-writable (mode 777)"**, merged-usr `/bin` included, and the folder that holds the link was never looked at. | `entry_problem` judges a symlinked entry twice and never refuses it for being one: its realpath target (root's or the user's, not world- or group-writable) AND the folder that holds the link (same rule — whoever can write that folder can repoint the link). Messages: `is a link whose folder <dir> is world-writable (mode 777)`, `is a link to <real>, which is …`. | `a symlink in a safe folder with a safe target was refused: refused: PATH entry 0 …/link is world-writable (mode 777) — 77 !== 0`; the mode-777-folder case refused with the wrong reason (`…/link is world-writable (mode 777)` instead of `is a link whose folder … is world-writable`). TEETH G13 kept in the test: without the folder check the mutant accepts a link in a mode-777 folder |
| **J6** | `git` and `python3` were not in the tool list; a missing absolute entry before the cut-off was accepted; no group-writable check. | `for TOOL in node vibe kimi rg fd git python3`; `missing (no such directory)` is refused; `group-writable (mode 775, group N)` is refused unless the group is gid 0. **Consequence for a host:** a Debian-style `/usr/local/bin` (mode 2775, group `staff`) now refuses; the owner's Ubuntu host has it 755 root:root. | `expected: 77 actual: 0` for the group-writable folder and for the world-writable folder before `git` / `python3`. TEETH J6: the old tool list accepts |
| **G5** | The finding names `${PIDS[@]}`-style expansions; `abort.sh` has none. What it has are associative arrays declared and never assigned: on **bash 5.3** (Ubuntu 26's, which `ubuntu-latest` migrates to) a bare `declare -A T` leaves `${#T[@]}` "unbound variable" under `set -u` — and `OWNED` CAN be empty (every member of the group may end between the listing and the snapshot), so `echo "owned set of ${#OWNED[@]}"` would abort the abort. (The bare `"${!T[@]}"` is fine on bash 4.4–5.3; a `${#T[@]}` count guard, tried first, was itself the 5.3 failure.) | `OWNED=(); OWNED_GROUP=()` right after the declaration; every key listing goes through one `keys_of`. | The static case was red (`OWNED and OWNED_GROUP are declared but never assigned`). **No deterministic runtime red**: the empty set is a race. The Linux case runs the script's own declarations and `keys_of` over an empty table under `set -eu`; it is red on bash 5.3 if the assignment is removed |
| **J3** | `abort.sh` acted on a handle whose pid was not its group id: a live leader with a matching start time plus ANOTHER group's id snapshotted and signalled that whole group; a live leader that no longer led the group was signalled too. `group_members` kept a member older than the leader (a reused group id). | Exit 4, nothing signalled, unless `PID == PGID` (and `PGID` is not 0 or 1); a live leader's current pgrp must equal the recorded one; `group_members` keeps only members that started at or after the handle's start (`group_members_all` is the unfiltered list the "leader gone, an older member" refusal needs). | `expected: 4 actual: 0` for the pid != pgid handle (the bystander's group was ended) and for the non-leader; a dead leader's handle naming another live group is refused too; the `PGID <= 1` and "leader gone, an older member" cases were already right and green at once. TEETH J3 (shown with a DEAD leader, because for a live one the new "leads the named group" check refuses on its own): without the `PID != PGID` check the mutant kills the bystander group |
| **J5** | `renderRunScript` took any probe argument matching the safe pattern, including `--scratch` (it moves the probe out of the series), `--allow-agents-canary` and `--allow-live-survivor` (arms the owner allows on the Windows machine only). | The three names, bare or `--name=…`, are refused with an exception; `--scratch-keep` and the like are not. (Pure — runs everywhere.) | `Missing expected exception (Error): renderRunScript wrote the argument --scratch into a root-run script`; break-it: matching only the bare name lets `--scratch=/tmp/elsewhere` through, red; restored |
| **G8** | The facts probe was silent for the seconds each slow step takes. | `collectHostFacts` takes an optional `progress(line)`; the probe writes `[native-facts] …` to STDERR before the kernel-banner reading, `coai-mcp`, Vibe's distributions, both keyring-class readings, `kimi --version` and `kimi acp --help`. stdout stays the one JSON record (the Linux run case still parses it, and asserts the stderr line). | `short lines, one per slow step:` (the sink was never called) |
| **G21** | `host-facts.test.mjs` pinned `DAEMON_PREFIXES` to a hand-copied list. | The cases are the shared vector file (next row); the copy is gone. | — |
| **J8, keyring daemon** | The facts script counted any name that starts with `keepassxc` (`keepassxc-proxy`, the browser helper) or `gnome-keyring`; the product (`KeyringSessionFacts.cs:56-57`) counts `gnome-keyring-daemon`, `gnome-keyring-d`, exactly `keepassxc`, and `kwalletd*`. | **One definition, two independent readings.** `shared/keyring-daemon-vectors.json` lists process names and whether each counts. `host-facts.mjs` (exact names plus the `kwalletd` prefix) is held to it by `host-facts.test.mjs`; the product by `KeyringSessionFactsTests.ANameIsADaemon_ExactlyWhenTheSharedDefinitionSaysSo_AsNameAndAsComm`. Changing the file turns BOTH sides red until both follow; changing one side's rule turns only that side red. | JS before: `keepassxc-proxy (as comm) is not a keyring daemon in the shared definition` (actual `['keepassxc-proxy']`). Break-its both ways: JS with `keepassxc` as a prefix → only the JS case red; C# `IsDaemon` with `StartsWith("keepassxc")` → only the C# theory (`name: "keepassxc-proxy", daemon: False`) and the old lookalike case red; both restored |
| **J8, fail backend** | `host-facts.mjs` spelled `keyring.backends.fail.Keyring` again. | It imports `KEYRING_FAIL_BACKEND` from `vibe.mjs`; a source case fails if the literal reappears. | `host-facts.mjs re-spells the fail backend instead of importing KEYRING_FAIL_BACKEND` |
| **J8, slot-hex redaction** | `redactSlot` exists three times (`host-facts.mjs`, `kimi.mjs`, `kimi-schedule.mjs`). | **Not done in this change.** The Kimi-probe coder exports one helper from `scripts/lib/kimi.mjs`; until it exists `host-facts.mjs` keeps its own `redactSlot` (its test imports it). Switching is one import once it lands. | — |
| **G23** | `attempt(() => fsx.lstat(path), null)` made EVERY error "absent": an `EACCES` on `~/.vibe/.env` read as `exists: false`; a failed `readdir` read as an empty folder. | Only `ENOENT` is absent. Any other error is `{ path, state: 'unreadable', code }` (no `exists` key at all); a folder that cannot be listed is `entries` / `names` = `{ state: 'unreadable', code }`, never `[]`. `ENOENT` on a listing (the folder left between the stat and the listing) stays `[]`. | `an EACCES on lstat was recorded as something else` (actual `{ exists: false }`); `a credentials folder that failed to list was recorded as holding nothing` (actual `[]`) |
| **N-K0 (J2's facts-script part)** | `kimi --version` and `kimi acp --help` ran with no "no other kimi" guard, and in a bare `k0-home` with no profile — so no `XDG_CACHE_HOME`: Kimi extracts ~2 MB of native files per launch into the real `~/.cache`. | The probe's `main()` (now `async`) waits with `waitForNoOtherKimi('N-K0', …)` before collecting and passes `kimiClear`; `false` records `k0: { asked: false, skipped: "another kimi was running …" }` and starts nothing. N-K0's environment comes from `ownedRoot(<scratch>/k0)` (injected; kimi-home.mjs's helper in the probe) with `home` = its `home/` and `profile` = its `os/`: HOME is the empty `os/`, `KIMI_CODE_HOME` the root's `home/`, `XDG_CACHE_HOME` its `cache/`. `collectHostFacts` stays synchronous. | `the owned root is <scratch>/k0` (actual `[]`); `kimi was started although another kimi was running`. Break-its: guard off → the skip case red; `profile` dropped → the owned-environment case red; restored |

What the fixes do **not** change: the exit codes (77, 78, 69, 65, 4, 3), the series shape, the record's existing field names (`state:
'unreadable'` and `skipped` are additions), or any product behaviour — `KeyringSessionFacts.cs` is untouched (its test file gained the
shared-vector theory).

### E1.S2 — The admission's shared plumbing and `coai-mcp --host-facts`, with both runtimes still refused

**Goal.** `HostAdmission` asks a per-runtime set on native Linux (EMPTY), the kernel banner can answer "unreadable", the product states
the host's facts in one read-only JSON line — and every existing admission answer stays byte-identical.

**Files.**

- `src_mcp/runners/Platform/HostAdmission.cs:12-38` — a `MeasuredOnNativeLinux` set (empty, case-insensitive names as `Refusal`
  receives them, `"Vibe"`/`"Kimi"`), and an internal overload of `Refusal` taking the set so a test can inject one. The `HostKind.Linux`
  arm: NOT in the set → today's `Wsl2Only(runtime, "it is measured on WSL2 and not yet on a native Linux host", "")`, unchanged (`:26`);
  in the set and the banner unreadable → the DN6 sentence, written once beside the others: *"{runtime} is refused on this Linux host —
  the kernel banner /proc/version could not be read, so coai cannot tell a native Linux host from WSL1 (DN6 of
  todo/PLAN_kimi_and_vibe_on_native_linux.md)"*; in the set and read → admitted. The remarks (`:3-11`) say the set is empty until a
  runtime's native cells pass.
- `src_mcp/runners/Platform/WslGeneration.cs:6-11`, `:36-55` — `WslGeneration.Unreadable`, produced ONLY by `WslGenerations.Current` on
  Linux when `/proc/version` is absent, throws, or is empty; `WslGenerations.Of(text)` is unchanged, so `shared/wsl-banner-vectors.json`
  and the extension's parity test do not move. `ProcVersion` keeps one read and returns whether it read.
- `src_mcp/runners/Vibe/VibeHost.cs:18-23`, `src_mcp/runners/Kimi/KimiHost.cs:17-18` — the generation is computed for `HostKind.Linux`
  too (still `None` for Windows and macOS).
- NEW `src_mcp/runners/Platform/KeyringSessionFacts.cs` — `KeyringSessionFacts.Of(Func<string, string?> environment, Func<string, bool>
  isSocket, IProcessList listing, int uid)`: the three facts of parent DN10, each with its own value; an unreadable listing is
  `cannot tell`. It decides nothing; no admission reads it.
- NEW `src_mcp/src/HostFactsMode.cs` (the `KimiClearMarkerMode.cs:21` shape, with its own source-generated JSON context — Native AOT):
  prints ONE line — `{"server":"coai-mcp <version>","hostKind":"linux","banner":{"read":true,"text":"…","generation":"None"},
  "admission":{"Vibe":"<sentence or empty>","Kimi":"<sentence or empty>"},"keyringSession":{"busAddressSet":false,"busSocket":false,
  "daemons":[],"listing":"read"},"decides":"admission only; keyringSession is recorded, never read by the admission"}` — and exits 0. It
  opens no home, no lock, no network, no data folder; it is NOT a writer mode (`Program.cs:323` unchanged).
- `src_mcp/src/Program.cs:43` (the `Startup` enum), `:336-380` (`"--host-facts" => Startup.HostFacts`), the dispatch beside
  `Startup.Version` (`:419-423`).
- `.agents/PROJECT.md:53-55` — `--host-facts` joins the sanctioned list after `--kimi-clear-marker` (E1 plan round, findings 1 and 6).
- NEW `src_mcp/tests/HostAdmissionOnNativeLinuxTests.cs`, `src_mcp/tests/HostFactsModeTests.cs`, `src_mcp/tests/KeyringSessionFactsTests.cs`.

**Scenarios.**

- **S-N16.** Injected set {Vibe}, `HostKind.Linux`, generation `None` (read, not WSL) → Vibe admitted, Kimi gets today's `Wsl2Only`
  sentence; the product's set → both get it. RED (the overload ignores its set): `"an injected measured set {Vibe} did not admit a native
  Linux host with a read banner — the set is not consulted"`.
- **S-N7** (teeth, the testing rule's way). Injected set {Vibe}, generation `Unreadable` → the DN6 sentence. Apply the set alone first
  and watch it red: `"an unreadable /proc/version was admitted as native Linux — a WSL1 kernel would start Vibe"`; then the banner rule,
  green. And with the PRODUCT's set, `Unreadable` → today's `Wsl2Only` sentence, byte for byte (membership is asked first).
- **The pins stay**: `KimiLaunchChecksTests.cs:198-201`, `:228-236` and `VibeRuntimeTests.cs:366-368`, `:401` green WITHOUT an edit —
  the summary says so; a needed edit there is a behaviour change E1 must not make.
- **S-N14.** `HostFactsModeTests`: the mode prints exactly one line that parses, with every field above, and exits 0; it creates nothing
  under a temp `COAI_DATA_DIR` (asserted empty) and touches no home; on Linux (the CI runner is a native VM) the REAL line says
  `"hostKind":"linux"`, `"read":true` and both admissions non-empty. RED (`--host-facts` absent from the switch): exit 64 —
  `"coai-mcp --host-facts is not a mode — N-V6 has no product reading to set beside the facts script's own (DN10: two readings)"`.
- **The list.** `ARequestFaultIsNotAnOldBinaryTests.TheSanctionedModesAreTheOnesTheDocumentNames` (`:52-71`) is the RED for the document:
  with the switch line added and `PROJECT.md` not yet edited it fails with *"`--host-facts` is a one-shot mode this binary HAS, and a mode
  the document does not name is a mode whose exit-code contract nobody agreed to"*; the `PROJECT.md` line turns it green.
- `KeyringSessionFactsTests`: each fact alone present → only that field true; an unreadable listing → `cannot tell`; `XDG_RUNTIME_DIR`
  unset → `/run/user/<uid>/bus` asked. RED (a stub that answers all-false): `"a set DBUS_SESSION_BUS_ADDRESS was recorded as unset"`.

**Break-its.** (B6) make the Linux arm check the banner BEFORE membership → the "product's set, Unreadable" case red (an answer changed
in E1); (B7) put Vibe into the product's set → `VibeRuntimeTests.cs:366-368` red (*"…not yet on a native Linux host"* missing) — the
pin that keeps E1 from admitting; (B8) make `WslGenerations.Of("")` answer `Unreadable` → the shared-vector test
(`KimiLaunchChecksTests.cs:228-236`) red; (B9) make `--host-facts` write its line through the log file instead of stdout → S-N14 red.

**Docs.** `research/module_runners.md` — the DL1 paragraph (`:3979`) gains the set (empty), the `Unreadable` generation, the fact
reader and `--host-facts`; the *Kimi on WSL2* diagram (`:3964`) gains the set check before the generation. `research/module_tests.md` —
the three new test classes.

**Done when.** S-N7, S-N14, S-N16 and the list test observed red, then green; B6–B9 both ways; the whole `CoaiMcp.Tests` executable
green with NO existing admission test edited; `coai-mcp --host-facts` run once on the `ubuntu-latest` job (inside S-N14) and its line
in the summary; Opus review has no open finding.

**Roles.** Opus brief (it fixes the sentence text, the field names and the order of checks — membership first); Sonnet codes; Opus
reviews. C1's Fable spec (E2) reads this story's code as its first input and may amend it in E2.

**Status, 2026-10-21 — built (branch `feat/native-linux-e1-s2`), awaiting the Opus review and the epic's code gate.** S-N7, S-N14, S-N16, the list test and the
keyring-fact cases were each observed red on an assertion, then green; B6–B9 ran both ways; `research/module_runners.md` (the native-Linux paragraph under DL1 and the
diagram) and `research/module_tests.md` (a section) carry the story. Deviations from the text above:

- **One existing test edited, not an admission pin.** `VibeLaunchRulesTests.TheSharedBannerVectors_Loaded_AndCoverEveryGeneration` (`VibeRuntimeTests.cs`) compared the vectors' generations with
  `Enum.GetValues<WslGeneration>()`, which the new `Unreadable` member made red; it now excludes `Unreadable` ("no banner text can answer it") — the claim it pins (none, WSL1, WSL2 each have a
  vector) is unchanged. The story said `WslGenerations.Of(text)` unchanged keeps the vectors still; the enum member was the part it did not foresee.
- **The rules-budget pin moved by 16.** Naming `--host-facts` in `.agents/PROJECT.md` (the list test demands it) lengthens the base: `StageRulesTests.AtNinetyKilobytes_TheBaseAndTheTier_SpendExactlyTheirPinnedBytes` was re-baselined (base 17 941, pair 80 002, 9 998 left of 90 000), as the two earlier mode entries did; the comments that quote the leftover follow. Whoever merges after this and also touches the base re-measures it.
- **`EveryModeIsClassifiedForAMissingNamedRootTests` gained one row** (`Startup.HostFacts`, "never reaches the data directory"): the story did not name the table, and a new `Startup` value is red until classified.
- **The report type lives in `runners/Platform/HostFactsReport.cs`**, not in `src/`: `ProcVersion` and `BannerReading` are internal to `CoaiMcp.Runners`, which shows its internals to the tests only. `src/HostFactsMode.cs`
  holds the mode and its source-generated JSON context (the plan's file), and only serialises.
- **`WslGenerations.ForHost(kind)`** is a new one-place rule (WSL and native Linux read the kernel) replacing three copies — `VibeHost`, `KimiHost` and `VibeRuntime`'s internal constructor (the story named the first two);
  `FileStatus.IsSocket` (one line) serves the bus-socket test; non-Linux hosts report `"listing":"not linux"` for the keyring facts (the story's two values were `read` and `cannot tell`).
- **Banner text** in the line is trimmed of its trailing newline; the JSON is written with relaxed escaping so the sentences read as text.
- **Break-its (all red with the real symptom, then restored):** B6 (banner before membership) → *WithTheProductsSet_AnUnreadableBanner_GetsTodaysWsl2OnlySentence*; B7 (Vibe in the product's set) → `VibeLaunchRulesTests.ANativeLinuxHost_IsRefused_WithTheWsl2OnlySentence`
  (`Expected … "" to contain "DL1"`) and the banner-vector theories, plus *TheProductsSet_AdmitsNeitherRuntime_…*; B8 (`Of("")` = Unreadable) → `KimiLaunchChecksTests.OnlyAWsl2Banner_StartsKimi(id: "empty-banner")` and
  `VibeLaunchRulesTests.OnlyAWsl2Banner_StartsVibe(id: "empty-banner")`; B9 (line to a file) → *--host-facts printed nothing on stdout*.

### E1.S3 — Every probe arm measures the product's road (Vibe's keyring arms, then the Kimi fixes and N-K2's pairs)

**Goal.** Vibe's M-V7 measures the closed keyring road E2 ships and proves it against a keyring that IS available; the Kimi probes
identify `kimi` the product's way, never read a credential's bytes, keep `os/` empty, run M-K3/M-K6 on owned roots, and see an abrupt
shim death followed at once by the next launch.

**Files — Vibe half first (it blocks N-V1).**

- `scripts/lib/vibe.mjs` — NEW exported pure `keyringArm(arm, platform, { scratch })`: on `linux`, arms a and a2 add
  `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`; arm a3 adds it AND a `HOME` pointing at `<arm scratch>/fake-home`; a3c the same
  fake `HOME` without the variable; on `win32` every arm is today's. NEW `writeProbeKeyring(fakeHome, markerFile)`: writes
  `fake-home/.config/python_keyring/keyringrc.cfg` (`[backend]` `default-keyring = probe_marker.MarkerKeyring`, `keyring-path =
  <fake-home>/keyring-backend`) and `keyring-backend/probe_marker.py`, whose `MarkerKeyring` (priority 1) appends the SERVICE name only to
  `markerFile` on every call and returns `None` — so the arm is free, and no key exists anywhere. Everything stays inside the arm's
  scratch.
- `scripts/probe-vibe-confinement.mjs:309-323` — M-V7's arms become a, a2, a3, a3c, b, built through `keyringArm`; the row records
  whether the marker file exists after each run (a3: must not; a3c: must). The header comment (`:35`) names the new arms.
- `scripts/lib/vibe.test.mjs` — the S-N17 cases.

**Files — Kimi half.**

- `scripts/lib/proc.mjs:15-35` — `parseProcStat` returns `{ pid, ppid, comm }`; `linuxProcesses` returns `{ pid, ppid, comm, name }`,
  `name` read as `LinuxProcessListing` does (`LinuxProcessListing.cs:100-134`): the `exe` link's basename with ONE trailing
  `" (deleted)"` stripped, else `argv[0]`'s basename from `cmdline`; gone between reads → skipped. NEW exported `isKimi(entry)` = name ∈
  {`kimi`, `kimi.exe`} case-insensitive OR `comm === 'kimi'` (`ForeignKimi.cs:51-54`, `:115`). The two existing `parseProcStat`
  assertions (`proc.test.mjs:17-22`) rename `name` to `comm` — a field rename, said in the summary, no assertion weakened.
- `scripts/probe-kimi-tree-hold.mjs` — the guard (`:131`), `seen` (`:225-228`, now keeping `{ comm, name }`), both targets (`:235`,
  `:293`) call `isKimi` (and an `rg` target its own `name`); the Windows listing (`:95-103`) fills `name` from the image name. NEW arm
  `killed-hard` (`ARMS`, `:48-61`): the same trigger as `killed`, then `process.kill(child.pid, 'SIGKILL')` instead of `child.kill()`
  (`:249`); before the signal it lists `<scratch>/locks/*.survivors.json` and records `markerAtKill` (name and mtime only; the row says
  "not evidence" when none). After every killed arm, each descendant still alive is recorded with its NEW parent: pid, `comm`, `exe`
  basename. The loop (`:341-346`) builds a SCHEDULE first — a pure exported function in NEW `scripts/lib/kimi-schedule.mjs` — in which each
  killed arm's repetition is a pair `<arm>-<n>` then `<arm>-<n>-next` (a `normal` launch), repeat-outer, nothing between a kill and its
  next; the `-next` row records the marker before it, the shim's report, and the marker after it.
- `scripts/probe-kimi-confinement.mjs:486`, `:519`, `:710`, `:757` and `scripts/lib/kimi.mjs:69-72` — `manifest(dir, { skip, hash })`:
  `credentials/` is `lstat` only (names with the slot's hex redacted, sizes, modes, mtimes), `hash` injected; the `isKimi` guard (the tree
  probe's 10-minute wait, shared through `lib/proc.mjs`) runs before every launch.
- `scripts/probe-kimi-confinement.mjs:251`, `:310` — M-K3's and M-K6's shim arms take an owned root (`makeOwnedHome`, as
  `probe-kimi-tree-hold.mjs:207` does), their `-p` controls an owned home with `owned: true` in the built environment.
- `scripts/lib/kimi-home.mjs:192-201` — `unixBuiltEnv` adds `XDG_CACHE_HOME` = the cache folder beside `os/`, as `KimiEnvironment.cs:34`,
  `:94` does.

**Scenarios.**

- **S-N17.** `keyringArm` for `linux`: a and a2 carry the variable; a3 carries it and the fake `HOME`; a3c the fake `HOME` only; `win32`
  unchanged; `writeProbeKeyring` writes only under the fake home and its module never returns a value. RED today:
  `"M-V7 arm a on Linux carries no PYTHON_KEYRING_BACKEND — it measures today's open keyring road, not the launch E2 ships (DN10)"`.
- **S-N17 live check (free, Linux).** In WSL with Vibe installed, arms a3 and a3c run ONCE each, signed out by construction (a fresh
  home, no `.env` link): a3c's marker file exists, a3's does not, both end *Missing MISTRAL_API_KEY*. This is the first reading of the
  "to be proven" claim of DN10, on WSL2 — recorded, not counted for the native host; N-V1 is that.
- **S-N13 / S-N1** (`proc.test.mjs`): over a fake `/proc`, the product's own cases by name — in `src_mcp/tests/ForeignKimiTests.cs`:
  `ATuiWhoseCommIsKimi_IsKimiByItsSecondWitness` (`:238`), `ADeletedKimiBinary_IsStillKimi_AndAnUnrelatedZombie_IsSkipped` (`:298`),
  `AProcessThatLeftBetweenTheListingAndTheRead_IsSkipped_AndOnlyOneDeletedSuffixIsStripped` (`:313`); in
  `src_mcp/tests/LinuxHiddenExeTests.cs`: `AHiddenExe_WhoseCmdlineNamesSystemd_IsNotKimi_AndDoesNotMakeTheListingUnreadable` (`:27`),
  `AHiddenExe_WhoseCmdlineNamesKimi_IsKimi_ByItsArgvZero` (`:49`), `AHiddenExe_WhoseCommIsKimi_IsKimi_AlthoughArgvZeroSaysNode` (`:60`),
  `ADeletedSuffixIsStrippedFromTheExeOnly_NeverFromArgvZero` (`:101`); `MainThread` alone is NOT Kimi. RED today: `"a running kimi acp (comm MainThread) was not seen as Kimi — the
  guard would let a second refresher start"`.
- **S-N2** (`kimi.test.mjs`): a fake home with `credentials/kimi-code-env-x.json` → the injected `hash` is never called for it. RED today:
  `"manifest() hashed credentials/kimi-code-env-x.json — a credential file's bytes were read"`.
- **S-N3** (`kimi-home.test.mjs`): the Linux built environment names `XDG_CACHE_HOME` beside `os/`. RED today: `"the Linux built
  environment has no XDG_CACHE_HOME — a direct kimi launch fills os/ (DL11)"`.
- **S-N4** (`kimi-d19.test.mjs` or a new `kimi-arms.test.mjs`, reading the arm table through an exported builder): every M-K3/M-K6 shim
  arm has an owned root, every control an owned home with `owned: true`. RED today: `"M-K3 Write has no owned root — the shim refuses it
  (E4.3), and the control would run on the real home"`.
- **S-N18** (`kimi-schedule.test.mjs`): `--arms killed,killed-hard,killed-rg --repeats 3` → nine pairs, each kill followed at once by its
  `-next`; `killed-hard`'s signal is `SIGKILL`, `killed`'s `SIGTERM`; a row builder given no marker at the kill says "not evidence"; given
  a survivor's new parent it records pid, `comm` and `exe` basename. RED today (the arm-first loop): `"the schedule ran killed-1, killed-2
  and killed-3 before any normal launch — the next launch's reconcile was never observed right after a kill"`.

**Break-its.** (B10) `isKimi` by `comm` alone → S-N13 red with the `MainThread` message; (B11) drop the `skip` → S-N2 red; (B12) remove
`XDG_CACHE_HOME` → S-N3 red; (B13) make `killed-hard` call `child.kill()` → S-N18's signal case red; (B14) build the schedule arm-first →
S-N18 red; (B15) let a3 omit the variable → S-N17 red.

**One paid turn.** After the `XDG_CACHE_HOME` fix, `probe-kimi-tree-hold.mjs --arms normal --repeats 1` on WSL2 (parent E1 *Risks*): it
answers, `os/` stays empty, both locks Free. The owner's standing approval covers it; it is recorded in the summary with its spend.

**Docs.** `research/module_tests.md` — the rows for `proc.test.mjs` (`:4417`) and the tree-hold probe (`:4225`) gain `isKimi`, the pairs
and `killed-hard`; the Vibe probe's M-V7 row gains a3/a3c. `research/RESULTS_vibe_confinement.md` is NOT written here (it is E2's §32);
the S-N17 live check's two lines go in the story summary and are carried into §32's "before the host" paragraph by E2.

**Done when.** S-N1–S-N4, S-N13, S-N17, S-N18 observed red with the messages above, then green, on Windows and the Linux CI job; the
S-N17 live check and the one WSL2 Kimi turn recorded; B10–B15 both ways; Opus review has no open finding. C2's Fable spec (E3) is later
checked against `killed-hard` and the pairs before N-K2 runs.

**Roles.** Opus brief (two briefs if the coder prefers: the Vibe half, then the Kimi half — one pull request either way); Sonnet codes;
Opus reviews, with the parity cases read side by side with the product's tests.

**Status — CODED 2026-10-21 (Sonnet coder; branch `feat/native-linux-e1-s3`); Opus review and the code gate still to run; the lead's two live steps NOT run.**
Every scenario of this story was watched RED against a stub that compiles, then GREEN, and every break-it was run both ways (node tests only; the
numbers are in `research/module_tests.md`, section *E1.S3*). Nothing ran `kimi`, `vibe` or Python; no WSL, no remote host, no paid turn.

| Scenario | Where | Red message observed | Break-it |
|---|---|---|---|
| S-N1 / S-N13 | `scripts/lib/proc.test.mjs` (the product's own cases by name) | *a running kimi acp (comm MainThread) was not seen as Kimi — the guard would let a second refresher start* | B10 red, restored |
| S-N2 | `scripts/lib/kimi.test.mjs` | *manifest() hashed credentials/kimi-code-env-x.json — a credential file's bytes were read* | B11 red, restored |
| S-N3 | `scripts/lib/kimi-home.test.mjs` | *the Linux built environment has no XDG_CACHE_HOME — a direct kimi launch fills os/ (DL11)* | B12 red, restored |
| S-N4 | `scripts/lib/kimi-arms.test.mjs` (arms moved to `lib/kimi-arms.mjs`) | *M-K3 Write has no owned root — the shim refuses it (E4.3), and the control would run on the real home* | dropped `ownedSpec`: red, restored |
| S-N17 | `scripts/lib/vibe.test.mjs` | *M-V7 arm a on Linux carries no PYTHON_KEYRING_BACKEND — it measures today's open keyring road, not the launch E2 ships (DN10)* | B15 red, restored |
| S-N18 | `scripts/lib/kimi-schedule.test.mjs` | *the schedule ran killed-1, killed-2 and killed-3 before any normal launch — the next launch's reconcile was never observed right after a kill* | B13 and B14 red, restored |

**Deviations (what shipped differently from the story's text).**

1. `linuxProcesses(proc, reads, { uid })` gained a reads seam (an in-memory `/proc` in tests) and, with the product's name rule, the product's other listing rules: another account's process and a
   zombie are skipped, and a same-account process identified by neither `exe` nor `cmdline` THROWS naming the pid (the product makes the listing unreadable there; a guard must not read "cannot tell" as
   "no kimi"). The listing test over a real folder now writes `status` and `cmdline` files into its fake `/proc` (a process with no readable name is skipped, as in the product); the two
   `parseProcStat` assertions changed only the field name `name` to `comm`.
2. `listProcesses` (with its Windows PowerShell reader) and the 10-minute wait (`waitForNoOtherKimi`, clock/sleep/listing injected) moved from the tree-hold probe into `lib/proc.mjs`, so the
   confinement probe shares them; a guard that gives up stops the confinement run (`StopProbe`) instead of launching around the other kimi.
3. `manifest`: `credentials/` is ALWAYS lstat-only (not an opt-in the caller can forget); `skip(rel)` names further lstat-only paths; the call sites use the safe default. Recorded names are
   `credentials/kimi-code-env-<hex>.json` with the hex redacted, values `lstat:<size>:<mode>:<mtime>`.
4. `ownedRoot(root, { platform })` also makes the 0700 `cache/` folder on a non-Windows host (as `KimiHome.MakeCache` does) and returns `cache`; the tree-hold probe uses `ownedRoot` instead of
   `mkdir os`. The existing Linux-built-environment assertion lists the third extra name (`XDG_CACHE_HOME`) — strengthened, not weakened.
5. M-K3 and M-K6 moved out of `probe-kimi-confinement.mjs` into `lib/kimi-arms.mjs` (`ownedShimArms`) so a test can read the table; each arm (control or not) also carries `profile` = `<root>/os`.
   The other legacy cells that go through the shim without an owned root (M-K2, M-K4, M-K7 …) have the same gap and are NOT touched — outside S-N4's text; named here for Opus.
6. All killed arms signal with `process.kill(child.pid, KILL_ARMS[arm].signal)` (`killed` stays SIGTERM = the old `child.kill()`); the signal table lives in `lib/kimi-schedule.mjs`.
   The orphan record is taken 300 ms after the signal, beside the lock watch (so the lock timings are not delayed).
7. M-V7: on Linux arm b is unchanged (the story lists a and a2 only); a3/a3c do not run on Windows. Added, to make the free live check possible without the paid arm b: `--arms a3,a3c` on
   `probe-vibe-confinement.mjs` (`armWanted` in `lib/vibe.mjs`, tested).
8. `watchAfterKill` waits until the orphaned `kimi` is gone before the `-next` launch of `killed-hard`, and the "no other kimi" guard also waits for it, so a plain `-next` launch never meets a LIVE
   survivor — it exercises the reconcile of an abandoned marker whose survivor is dead. The live variant was asked for and is built as the follow-up below (`killed-hard-live`).

**Follow-up 1 (the lead's request, same day) — N-K2's LIVE-survivor arm `killed-hard-live`.** The cornerstone C2 is that a survivor keeps the next launch blocked and only its
confirmed disappearance clears the marker (`KimiSurvivorGate`). The arm is a TRIPLE (`lib/kimi-schedule.mjs`): `killed-hard-live-<n>` (SIGKILL; it does NOT wait for the orphan to end), then
`-next` — a normal launch started while the killed arm's OWN `kimi` (the "survivor") still runs — then `-after`, a third launch once the survivor has ended (watched by pid and start time),
which must reconcile and clear. The probe's "no other kimi" wait is bypassed for exactly that survivor: `waitForNoOtherKimi({ except: [{ pid, startTime }] })` compares pid AND the start time
(field 22 of `/proc/<pid>/stat`) at the moment of the check — a reused pid is a stranger, an unreadable start time excepts nothing, and the bypass is never by name; every other kimi still blocks. Recorded
in the `-next` row: the marker before and after, the refusal text (the shim's `failure`/`reason`, hex redacted), the survivor and its NEW parent, whether it lived before and after, and a verdict
(`liveVerdict`: *as expected: refused Busy naming the survivor, nothing started, the marker stands, the survivor untouched*, or `UNEXPECTED: …` saying which part differed, or *not evidence* when the survivor had
already ended). The killed run's owned home is disposed only after the survivor has ended (by `-after`). RED: *the schedule has no live-survivor pair — the next launch never starts while the killed
arm's own orphan lives* (`kimi-schedule.test.mjs`); break-it, the bypass by name instead of pid and start time: *a kimi that is not the orphan was let through — two refreshers could race*
(`proc.test.mjs`), both restored.
**This arm runs ONLY on a host where the owner allowed the window**: if the product's gate failed, two refreshers of one rotating token would sign the person out. It is therefore not in the default
arm list and is refused unless `--allow-live-survivor` is passed (and off Linux). Note for the live run, from reading `KimiSurvivorGate`: when the shim that wrote the marker is gone, the gate's
reconcile calls `ReconcileSeams.Terminate` (an end by identity through a pidfd) before it confirms — so the product may END the survivor and clear, rather than refuse. The probe does not assume
either; the verdict says `UNEXPECTED: the survivor no longer ran…` and the numbers are recorded, which is the answer N-K2 needs.

**Follow-up 2 (the lead's request, same day) — the Kimi 2.2.0 window's arms all run on owned roots.** M-K4 and M-K12 moved into `lib/kimi-arms.mjs` beside M-K3 and M-K6 (M-K12 out of `lib/kimi-d19.mjs`).
Every arm of the four cells carries `requireOwned`, an owned `<root>/home` and `owned: true`; the launch refuses one that lost it (`ownedRefusal`, before the "no other kimi" wait and before anything
starts) with *this arm has no owned root … it is refused, not run on the real KIMI_CODE_HOME*. RED per cell (`kimi-arms.test.mjs`): *M-K4 Bash run_in_background has no owned root — the shim refuses it
(E4.3), and the control would run on the real home*, then the same for M-K12 and *M-K3 Write (run 1) was not refused without an owned root*; break-it (`ownedRefusal` always empty): red, restored.
M-K12's arms changed shape because the shim always redirects the OS home (so an arm that leaves it real was refused by name, `shimRefusal`): (a) the real OS home now runs through the small client
(`kind: 'acp'`) on the owned home; (b) the redirected OS home runs through the shim, whose `<root>/os` is the empty folder; the control is `-p` on an owned home with the real OS home (it used to run on
the person's own KIMI_CODE_HOME). The canary stays in the person's REAL `~/.agents`, written and removed by exact paths only (`makeAgentsCanary` / `removeAgentsCanary`: `rmSync` on named files,
`rmdirSync` — never recursive — on named folders, a pre-existing `AGENTS.md` refused); a new test proves a stranger's file inside the canary folder survives and the removal fails loudly (break-it: a
recursive delete: red, restored). Arms (a) and the control leave the OS home real on purpose — `HOME` stays real because Kimi finds `~/.agents` through it, and that is what the arm measures. (First written: *so Kimi's native-file extraction lands in the person's real `~/.cache` there* — wrong; the cache folder is not the question, and since J2 below they name the owned root's `cache/`.)

**Code gate and judge fixes (2026-10-21) — the Kimi probes' items.** The code gate (verdict *good enough*) and the Opus judge (*not ready: one blocker*) were accepted in full; this block is the
Kimi-probe half (the host-facts script, `scripts/native/*` and `HostFactsMode.cs` are other hands'). Each behaviour fix was watched RED, fixed, GREEN, broken again on purpose and restored.

- **J1 (the blocker) — an owned launch no longer reads a refresh token.** `probe-kimi-confinement.mjs` built `new CredentialGuard(REAL_HOME)` at start and called `afterRealRun` after every owned
  launch; the guard's fingerprint parses every `credentials/*.json` and hashes the `refresh_token`. Now `lib/kimi.mjs` `CredentialWatch` watches by `credentialShape` (lstat: name, inode, size, mtime; nothing
  opened) for every launch; the content-reading `CredentialGuard` is built LAZILY by `content()`, only for a copied-credentials (legacy) cell, and `copiedHome()` refuses such a cell unless
  `--allow-copied-credentials` is passed (the series template never passes it; the refusal is an arm row *setup failed: this cell copies the credentials of the person …*, before any byte is copied). The S-N2
  twin injects the reader: *the credential reader was called for the real home — a refresh token was read by an owned launch* (RED, 3 calls: construction and two launches; and *Expected 0, actual 1*
  for the lazy case); break-it (build the guard in the constructor): red, restored. Rotation is unknown without reading (`rotates: null`), and says so in the record.
- **J2 (with G0 and G24) — no Kimi launch writes into the person's real `~/.cache`.** `unixBuiltEnv` named `XDG_CACHE_HOME` only with an OS-home redirect (`kimi-home.test.mjs` pinned *no profile → no
  variable*, now flipped). It now names `<root>/cache` whenever `home` is an owned `<root>/home`, profile or not, and takes an explicit `cacheDir` (G0; default derivation unchanged). That covers M-K12 arm (a)
  and its `-p` control, N-K5's and M-K11c's `kimi --version` (`versionLaunch` builds `builtEnv({ home })`). RED: *a launch on an owned home with the real OS home writes the native files of Kimi into the
  real ~/.cache of the person (actual undefined, expected '/r/cache')*, *the arm has no owned XDG_CACHE_HOME — Kimi extracts into the real ~/.cache* (M-K12 (a) and the control, HOME still real), and for
  `cacheDir` *actual '/r/cache', expected '/elsewhere/cache'*; break-its (home derivation removed; `cacheDir` ignored): red, restored. **Does this change what M-K12 measures? No.** The arm asks whether Kimi finds
  `~/.agents` through `HOME`, and `HOME` is still the real one; only the cache folder moved. (The corrected sentence replaces *it is what the arm measures* in Follow-up 2 above, which was wrong about the cache.)
  Not done here: N-K0's `kimi --version` / `kimi acp --help` in the host-facts script, and its missing "no other kimi" guard — `scripts/lib/host-facts.mjs` is the other coder's.
- **J4** — `makeAgentsCanary` writes both files with `{ flag: 'wx' }`; a file the owner made after the start check is never overwritten, and the skill it had already written is taken back (its own paths only).
  RED: *Missing expected exception* (the canary overwrote `AGENTS.md`); break-it (flag removed): red, restored.
- **J7** — the survivor's start time is read when a descendant is FIRST listed (`sample()` in `probe-kimi-tree-hold.mjs`) and must still match at the kill: `survivorsOf` in `lib/kimi-schedule.mjs` drops a pid
  that now has another start time and keeps an unidentified one with `startTime: ''` (so `liveSurvivor` never picks it). RED: *pid 200 now has another start time - a different process … actual [[100,'T1'],[200,'T9'],[300,'T3']]*;
  break-it (start time never compared): red, restored.
- **G10** — the D19 cells (M-K11, M-K13, M-K9e, M-K7b) build their specs through `ownedSpecFor` in `lib/kimi-arms.mjs` like every other arm, so `requireOwned` and `ownedRefusal` judge them too; M-K13's two `-p` arms
  keep the REAL OS home (`realOsHome`, `profile: ''`) as before. RED: *M-K11 owned home: junction + admitted, OK: not marked requireOwned*; break-it (`requireOwned` dropped): red, restored.
- **G18** — `armWanted(arm, filter)` treats a missing filter as "every arm". RED: *TypeError: Cannot read properties of undefined (reading 'length')*.
- **G19** — `lockState` (`lib/proc.mjs`) takes an injected `flock`/`spawn`. The finding said exit 69 became `Unknown:75`; it does not — 69 already reads `Unknown:69` and a test now pins it. What the new test did find
  is a flock killed by the 10 s timeout, which read `Unknown:undefined`; it now reads `Unknown:SIGTERM` (a spawn error by its code, nothing at all as `Unknown:no-exit`). RED: *the timeout killed flock: that is a signal,
  not "undefined" — actual 'Unknown:undefined'*; break-it: red, restored. (The lead should confirm this was the intended defect.)
- **G22 — the Kimi identity contract.** `shared/kimi-identity-vectors.json` (process by `exe` / hidden-exe `argv` and `comm` → the name the listing reads → `isKimi`; 15 vectors) is read by `proc.test.mjs`
  (`isKimi` over `linuxProcesses`) AND by the new `src_mcp/tests/KimiIdentityVectorTests.cs` (`ForeignKimiWatch.IsKimi` over `LinuxProcessListing`). Break-it, one side at a time: comm compared ignoring case in the
  probe → only *comm-is-case-sensitive: the probe's isKimi disagrees with shared/kimi-identity-vectors.json* (node) red; the same change in `ForeignKimi.cs` → only the C# theory red; both restored.
- **J8 (the slot-hex redaction only)** — one `redactSlot` exported from `lib/kimi.mjs` replaces the copies in `manifest` and `lib/kimi-schedule.mjs` (`host-facts.mjs` imports it, the other coder's change). Pinned by
  a test; break-it (`{9,}`): red, restored. The `FAIL_BACKEND` import and the keyring DAEMON rule are not mine.

**Left for the lead (not run by the coder, by instruction).**

- *The S-N17 live check (free, WSL, Vibe installed):* `node scripts/probe-vibe-confinement.mjs --vibe "$(command -v vibe)" --cells M-V7 --arms a3,a3c --repeats 1` — expect a3c's `keyringMarker=yes`,
  a3's `keyringMarker=no`, both ending *Missing MISTRAL_API_KEY*. The two lines go into this summary and are carried into `RESULTS_vibe_confinement.md` §32 by E2.
- *The one paid WSL2 Kimi turn (after the `XDG_CACHE_HOME` fix; build the shim first):* `node scripts/probe-kimi-tree-hold.mjs --arms normal --repeats 1` — expect it answers, `os/` stays empty
  (the row's `homeAfter` and the owned root's `os/`), both locks Free; record its spend.
- *The live-survivor arm (only where the owner allowed the window; one sign-in, two possible refreshers if the gate failed):* `node scripts/probe-kimi-tree-hold.mjs --arms killed-hard-live --repeats 1 --allow-live-survivor`.
- *The 2.2.0 window (one paid window):* `node scripts/probe-kimi-confinement.mjs --cells M-K3,M-K4,M-K6,M-K12 --allow-agents-canary`.
- *CI:* the new tests run on the Linux job (`node --test "scripts/**/*.test.mjs"`); no dotnet test was touched.

### Code gate and judge fixes (2026-10-21)

- **G2 (`HostFactsMode` hand-rolls JSON) — already true at `d794dc602`:** the mode serialises through the source-generated `HostFactsJsonContext` (relaxed escaping, no hand-written escaper ever existed in its history); `HostFactsModeTests.AValueWithAQuoteABackslashANewlineAndNonAscii_RoundTripsThroughJsonDocument_OnOneLine` pins it (break-it: the default encoder and a renamed field both turn it red).


## 5. Build order

1. E1.S1 (the facts script and `scripts/native/`) — nothing depends on it inside E1, and it is the first thing the Vibe series uses.
2. E1.S2 (the plumbing and `--host-facts`) — independent of S1; S1's facts recorder already handles a binary without the mode.
3. E1.S3 — the Vibe half, then the Kimi half.
4. One pull request; the code gate (`review_code`) on the whole; then merge. The Vibe series (E2) starts after the merge.

## 6. Test plan — the parent's rows this epic owns

| Parent row | Here |
|---|---|
| S-N1–S-N4, S-N13 | E1.S3, `node --test scripts/lib/*.test.mjs` |
| S-N7, S-N14, S-N16 | E1.S2, `CoaiMcp.Tests` (filters `*HostAdmission*`, `*HostFacts*`, `*KeyringSession*`), then the whole executable |
| S-N15 | E1.S1, `node --test scripts/native/*.test.mjs` — Linux CI job and WSL; skips by name on Windows |
| S-N17, S-N18 | E1.S3, `node --test scripts/lib/*.test.mjs` |
| S-N19 | E1.S1, `node --test scripts/lib/*.test.mjs` |
| the list test | E1.S2, `ARequestFaultIsNotAnOldBinaryTests` |
| CI | every job green; `.github/workflows/ci.yml:197` already runs the new `scripts/**` tests |

## 7. Growth

Nothing that grows: the scripts write only under a test's own temp folder (removed in `after`) or, on the host, under the series folder
the parent §12 accounts for. `--host-facts` writes nothing.

## 8. Definition of Done — epic E1

- [ ] E1.S1: S-N15, S-N19 red then green; B1–B5; the bash tests run on the Linux CI job and skip by name on Windows.
- [ ] E1.S2: S-N7, S-N14, S-N16 and the list test red then green; B6–B9; no existing admission test edited; `--host-facts` in `.agents/PROJECT.md`.
- [ ] E1.S3: S-N1–S-N4, S-N13, S-N17, S-N18 red then green; B10–B15; the S-N17 live check and the one WSL2 Kimi turn recorded.
- [ ] No sign-in file opened by any test or script of this epic.
- [ ] `research/module_runners.md` and `research/module_tests.md` updated.
- [ ] The Opus review has no open finding; the code gate reached `proceed` or `good_enough`.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean; this document moves to `research/` with the parent.
