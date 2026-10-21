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
