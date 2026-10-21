# PLAN — Vibe and Kimi on a native Linux host: measure there first, then admit each one that passes

> Status: **plan only, nothing implemented yet, 2026-10-21 — the plan gate passed (coai 942810c1, `good_enough`, its ten accepted
> findings folded below), the consult answered (634d4907, 3 of 3), the E1 plan round passed (b54f22f5, `proceed`, folded the same
> day), the cadence consult for epics 1–3 answered (e9a9b20e, folded the same day: the keyring road is CLOSED, not guessed), and the
> work is split into three epics (E1 → E2, E1 → E3, at the bottom; E1's stories are
> [PLAN_kimi_and_vibe_on_native_linux_E1.md](PLAN_kimi_and_vibe_on_native_linux_E1.md)); measurement first; no admission ships
> before its cells.** Scope: the probes in `scripts/` (three small Kimi fixes, Vibe's keyring arms, the N-K2 hard-kill pairs, one
> read-only facts script, the safe remote run scripts), the shared admission plumbing with both runtimes still refused and a
> read-only `coai-mcp --host-facts` (E1), the measurement on the owner's native Ubuntu host `ubuntu-claude` (SSH), then the
> per-runtime admission in `src_mcp/runners/Platform/HostAdmission.cs`, Vibe's closed keyring road (DN10), the extension's host notes
> and help, `shared/consultant-limitations.json`, and the RESULTS documents.
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
   today's refusal sentence. The admission is **per runtime** — Vibe can be admitted while Kimi is still refused. For Vibe, coai's
   launch on a native Linux host also **closes the keyring road** (DN10): its launch environment forces Python `keyring`'s failing
   backend, so the only sign-in a coai launch can reach is the linked `.env` — the one road the cells measured, whatever keyring the
   host has.
3. An unreadable kernel banner is never admitted as "native Linux" (§4 DN6).
4. The card, the install note, the help in five languages and the limitation rows say what the side's coai-mcp actually does.
5. **The probes never inspect, hash, copy or print the contents of a sign-in** — `~/.vibe/.env` and every file under
   `~/.kimi-code/credentials/` are seen by `lstat`/`stat` only, by every step of this plan (gate finding 0). The **runtime** does
   read them, because that is how it authenticates: in the paid cells **N-V1 arm b, N-V2, N-V3** Vibe reads `~/.vibe/.env` through
   the owned home's link, and in **N-K1–N-K5** Kimi reads `~/.kimi-code/credentials/<slot>.json` through the owned root's symlink and
   may refresh it by temp-and-rename. Nothing of what they read reaches a log, a table, a RESULTS section or this branch.

## 3. What the WSL2 admission rested on, and what a native host needs

Read from the parent plan §3/§5 and the RESULTS sections. "Re-run" means the cell can differ on a native host; "does not apply" says why.

### Vibe (the WSL2 row is `confined` on L-V9, 3 of 3 — RESULTS_vibe §31)

| WSL2 cell | Script | What it proves | Native host |
|---|---|---|---|
| L-V1 | `probe-vibe-confinement.mjs --cells M-V7` (arms a, a2, b) | a fresh owned home is signed out (free, a known wording); a home with the `.env` LINK is signed in | **re-run, with the forced keyring backend (DN10) and the new arms a3/a3c** — this is the keyring question: on a desktop Linux a keyring could sign a fresh home in by another road, and coai closes that road |
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
  native Linux host, else today's `Wsl2Only` sentence, unchanged. No second copy of the sentence, no flag in settings. **The set is
  built in E1 and is EMPTY there** (cadence consult e9a9b20e, decision 4): membership is asked FIRST, so with an empty set every
  native-Linux answer is byte-identical to today's, and the DN6 banner sentence is reachable only by a test that injects a set. E2
  adds Vibe to the set; E3 adds Kimi. Each flip is one entry plus its texts, never a second admission.

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
  `keyring.get_keyring()` picks under the built environment AND under the built environment plus the forced backend of DN10 (which must
  answer `keyring.backends.fail.Keyring`) — never `get_password` — and whether a D-Bus session address is set (yes/no);
  (b) the decisive cell — M-V7 arm a (a fresh owned home, no `.env` link, the built environment WITH the forced backend) must end with
  the *Missing MISTRAL_API_KEY* wording 3 of 3, arm a2 (`VIBE_TEST_DISABLE_KEYRING=1`) the same, and the new arms a3/a3c prove the forced
  backend beats a keyring that IS available (DN10). If arm a is SIGNED IN, a keyring reaches coai's launches on this host even with the
  road closed: that is a stop (§7), not an admission.

- **DN5 — Three probe gaps are fixed before any paid run (P1), each red first.** Found while writing this plan, verified against the code:
  1. **The Kimi guards and kill targets match `comm`, and `kimi acp`'s `comm` is `MainThread`** (L-K6). `lib/proc.mjs` reads only
     `pid`, `ppid` and `comm` (`scripts/lib/proc.mjs:15-35`); `probe-kimi-tree-hold.mjs`'s "no other kimi" guard filters
     `/^kimi(\.exe)?$/` on that name (`:131`), its kill target too (`:235`, `:293`), and the descendant names it remembers (`:227`) are
     `comm` as well. On Linux the guard misses every running ACP agent, and the `killed` arm never finds the `kimi` it waits for.
     **The product already has the rule, and the probes reuse it rather than guess a second one** (reuse-first; gate task 2):
     `ForeignKimiWatch.IsKimi` (`src_mcp/runners/Kimi/ForeignKimi.cs:51-54`, `:115` — re-verified 2026-10-21 at 449a20884) is *name ∈ {`kimi`, `kimi.exe`}, case-insensitive,
     OR `comm == "kimi"`*, where the name is what `LinuxProcessListing` reads (`src_mcp/runners/Kimi/LinuxProcessListing.cs:100-134`):
     the `exe` link's basename with ONE trailing `" (deleted)"` stripped, else — when `exe` is denied — the basename of `argv[0]` from
     `cmdline`; a process that left between reads is skipped. Fix: **port that one rule into `scripts/lib/proc.mjs`** — `linuxProcesses`
     returns `{ pid, ppid, comm, name }` with `name` read the product's way (today the field called `name` HOLDS `comm`,
     `scripts/lib/proc.mjs:22`, so the port renames it and every reader of `.name` is checked), and one exported `isKimi(entry)` is the only Kimi test the
     probes have: the guard (`:131`), the `seen` map (`:227`), both kill targets (`:235`, `:293`). Parity is pinned by
     `scripts/lib/proc.test.mjs`, whose cases are the product's own, by name: `ATuiWhoseCommIsKimi_IsKimiByItsSecondWitness`,
     `ADeletedKimiBinary_IsStillKimi…`, `…OnlyOneDeletedSuffixIsStripped` (`src_mcp/tests/ForeignKimiTests.cs:238`, `:298`, `:313`, in
     that order — re-verified 2026-10-21), and `…WhoseCmdlineNamesSystemd_IsNotKimi…`, `AHiddenExe_WhoseCmdlineNamesKimi_IsKimi_ByItsArgvZero`,
     `…WhoseCommIsKimi_IsKimi_AlthoughArgvZeroSaysNode`, `ADeletedSuffixIsStrippedFromTheExeOnly_NeverFromArgvZero`
     (`src_mcp/tests/LinuxHiddenExeTests.cs:27`, `:49`, `:60`, `:101`, in that order). Ported, not called: the product's listing is internal to the shim (no CLI prints it), and the probe also
     lists on Windows, where the product's listing is a different reader. A `comm` of `MainThread` alone is NOT Kimi under this rule
     either — the executable is the witness, exactly as in the product.
  2. **`probe-kimi-confinement.mjs` has no "no other kimi" guard at all, and hashes the person's credential files.** Every launch hashes
     every file of `~/.kimi-code` (`manifest(REAL_HOME)`, `scripts/probe-kimi-confinement.mjs:486`, `:519`, and around the whole run at `:710`, `:757`; `manifest` hashes each file,
     `scripts/lib/kimi.mjs:70-72`) — that READS the bytes of `credentials/*.json`, which §2 item 5 forbids (gate finding 0). Fix:
     `manifest(dir, { skip })` takes a filter and the home record passes `credentials/`; for that folder the record is `lstat` only
     (names with the slot's hex redacted, sizes, modes, mtimes), `fileHash` is injected and the test (S-N2) proves it is never called
     for a path under it; and the `isKimi` guard of item 1 runs before every launch.
  3. **The probe's Linux built environment lacks `XDG_CACHE_HOME`.** DL11 put it into the product (`src_mcp/runners/Kimi/KimiEnvironment.cs:34`,
     `:94`), but `lib/kimi-home.mjs`'s `unixBuiltEnv` (`scripts/lib/kimi-home.mjs:192-201`) never got it, so a direct `kimi -p` control
     or `--version` of the probe fills `os/` — a probe artefact that would look like a product failure. Fix: the same cache folder
     beside `os/` as `KimiPlaces.Cache`.
  4. **N-K2 must see an abrupt death, and the next launch right after it** (cadence consult e9a9b20e, decision 2). The `killed` arm
     ends the shim with `child.kill()` (`scripts/probe-kimi-tree-hold.mjs:249`) — SIGTERM — and the `--ask-acp` shim handles SIGTERM:
     `Program.cs:442-451` registers it through `StopSignals.Into` (`src_mcp/runners/Processes/StopSignals.cs:18-36`) and its `finally`
     ends the agent's tree. So a clean next launch proves nothing about an ABANDONED survivor marker. Fix: a `killed-hard` arm that
     sends SIGKILL, counts only when a `*.survivors.json` (`KimiCredentialsLock.MarkerPathFor`, `src_mcp/runners/Kimi/KimiCredentialsLock.cs:135`)
     was seen in `<scratch>/locks` at the moment of the kill, and records each orphan's NEW PARENT (pid, `comm`, the `exe` basename),
     not only whether it lives. And the repeat loop (`:341-346`) runs arm-first, so `killed-1..3` run before any `normal`: every killed
     arm's repetition becomes a PAIR — the kill and the immediately following normal launch, one row, nothing between them — which is
     where `KimiSurvivorGate`'s reconcile is observed.
  5. **Vibe's M-V7 arms measure the launch E2 ships** (cadence consult e9a9b20e, decision 1). On Linux arms a and a2 carry the forced
     backend of DN10, and two new arms, a3 and a3c, give Vibe a keyring that IS available (§5 N-V1). Today's arm setup
     (`scripts/probe-vibe-confinement.mjs:309-323`) carries neither.
  And one read-only script: `scripts/probe-native-linux-host.mjs` (the facts of §5 N-H1, N-V0, N-K0), reusing `lib/vibe.mjs`
  (`vibePython`, `homeListing`, `personsVibeHome`), `lib/kimi.mjs` and `lib/proc.mjs`. Vibe's probe needs item 5 only; items 1–4 gate
  only the Kimi series.

- **DN6 — An unreadable kernel banner is never "native Linux" for an admission.** Today an unreadable `/proc/version` reads as empty
  (`ProcVersion.Read`, `src_mcp/runners/Platform/WslGeneration.cs:44-54`), `NamesWsl` says no, and the host is `HostKind.Linux`
  (`HostKind.cs:56-59`) — harmless while Linux is refused. Once Linux is admitted, a WSL1 kernel whose banner could not be read would be
  admitted. So the admission asks for a READ banner: the generation gains an "unreadable" answer on Linux, and that host keeps a
  refusal with its own sentence (*"the kernel banner /proc/version could not be read, so coai cannot tell a native Linux host from WSL1"*).
  The extension has the same fallback (`src_vs_code/src/hostSide.ts:170-195`: unreadable → `linux`, "a refusal, never a promise"); it must
  stay a refusal there too once `linux` runs Vibe. **Split by the cadence consult (e9a9b20e, decision 4):** the server half — the
  unreadable answer (`ProcVersion.Read` must tell "empty" from "could not read", `WslGeneration.cs:44-54`; `VibeHost.cs:18-23` and
  `KimiHost.cs:17-18` compute it for `HostKind.Linux` too) and its sentence — is E1's plumbing, asked AFTER set membership (DN1), so it
  changes no answer while the set is empty; the extension half is E2's.

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

- **DN10 — The keyring road is CLOSED by coai's launch, not guessed from the host** (gate finding 6, accepted; REWRITTEN by the cadence
  consult e9a9b20e, decision 1, verified by the lead). The measured host is headless (`root` over SSH, most likely no session bus), so a
  pass there says nothing about a native Linux host WITH a desktop keyring, where Vibe's `keyring` package could sign a fresh owned home in
  by a road coai does not control (DN4). The first answer (gate 942810c1) was to detect a desktop keyring session at run time and refuse
  on one. That guessed: three signs (a bus address, a bus socket, a daemon's name) stand in for "a keyring backend Vibe would use", and
  KeePassXC's Secret Service, a `keyringrc.cfg`, or a backend with no daemon are not among them. **Decision: coai closes the road
  instead.** On a native Linux host (`HostKind.Linux`) coai's Vibe launch environment sets
  `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`, so no keyring backend — a desktop Secret Service, KWallet, KeePassXC, a
  `keyringrc.cfg` in the real `HOME` — is reachable, and the sign-in can only come through the linked `.env`, the road the cells measure.
  - **Why it works — read from the source, on the lead's machine** (Python `keyring` 25.7.0, the version in Vibe 2.26.1's own uv tool
    environment, `%APPDATA%\uv\tools\mistral-vibe\Lib\site-packages`; the host's own version is recorded by N-V0): Vibe reads its key
    only through `keyring.get_password` on Linux (`vibe/utils/keyring.py:78-98`; macOS alone goes to `/usr/bin/security`, `:24-25`) — the
    road RESULTS_vibe_confinement.md:238 names. `keyring` picks its backend as `load_env() or load_config() or max(available)`
    (`keyring/core.py:96-115`); `load_env` reads `os.environ['PYTHON_KEYRING_BACKEND']` (`keyring/core.py:150-155`), BEFORE the config file
    `keyringrc.cfg` under `$XDG_CONFIG_HOME/python_keyring` or `$HOME/.config/python_keyring` (`keyring/util/platform_.py:27-34`,
    `keyring/core.py:158-159`, `:168-193`). `fail.Keyring.get_password` raises `NoKeyringError` (`keyring/backends/fail.py:21-28`), a
    `KeyringError` (`keyring/errors.py:25`), which Vibe catches and turns into "no key" (`vibe/utils/keyring.py:165-170`). The package's own
    README names the environment variable as the way to force a backend ("Disabling Keyring", `keyring-25.7.0.dist-info/METADATA:309-332`,
    which uses `null.Keyring`; `fail.Keyring` is chosen here
    because it raises on every call, so a later Vibe that stopped catching would fail loudly rather than read nothing quietly). One side
    effect, harmless here: Vibe's MCP OAuth refuses to run on a `fail.Keyring` backend (`vibe/core/auth/mcp_oauth.py:280-285`), and an
    owned home has no MCP servers (D4, D8b).
  - **Still to be proven, and marked so until it runs:** that the environment variable beats a keyring that IS available, in the real
    Vibe on the host. N-V1's new arms a3/a3c are that proof (§5): a probe-owned backend named by a `keyringrc.cfg` under the probe's own
    fake `HOME`, consulted when the variable is absent (a3c, the teeth) and never consulted when it is set (a3).
  - **What the product changes (E2, which owns it):** `VibeLaunch.Built` (`src_mcp/runners/Vibe/VibeLaunch.cs:97-108`) gains the fifth
    built name on `HostKind.Linux` only, and `EnvironmentRefusal`/`CarriesTheThree` (`:86-89`, `:115-119`, which today demand exactly
    four names) admit it there and only there; a `PYTHON_KEYRING_BACKEND` in the SERVER's own environment never passes through — the
    built environment inherits only `ProcessEnvironment.Minimal` (`src_mcp/runners/Processes/ProcessEnvironment.cs:90-98`), which does not
    name it. **WSL2 launches are unchanged by this plan**: they are admitted as they were measured (RESULTS_vibe §31), without the
    variable; closing the road on WSL2 too would be a change to a measured launch and is not made here.
  - **The keyring-session facts stay RECORDED facts, no longer a condition.** `DBUS_SESSION_BUS_ADDRESS` set, a bus socket at
    `$XDG_RUNTIME_DIR/bus` (or `/run/user/<uid>/bus` when the variable is unset), a `gnome-keyring-daemon`/`kwalletd*`/`keepassxc` in the
    listing: N-V0 and N-V6 record them, and `coai-mcp --host-facts` (E1) prints them beside the host kind, the banner and each runtime's
    admission answer. They decide nothing: the admission no longer reads them, and an unreadable listing is recorded as "cannot tell".
  - **Two readings, on purpose** (E1 plan round b54f22f5, finding 4, accepted): the facts script takes its OWN raw reading (it reads
    `/proc/version`, the variables, the socket and `/proc` itself) AND records `--host-facts`. That is not a re-implementation of a
    decision — there is no decision left in those facts — it is an independent second reading, which is the only way a disagreement can
    be SEEN: one implementation read twice could only ever agree with itself.
  - **Kimi** has no keyring road (its sign-in is a file coai links), so its arm asks only DN1, DN6 and DN12. The limitation row and the
    help say what the admission does: *"on a native Linux host coai turns Vibe's keyring off, so it signs in only through `~/.vibe/.env`"*.

- **DN11 — The remote run is executed safely, root or not** (gate findings 1, 3, 5, 8, accepted). The series runs as `root`, so a
  shell mistake is a mistake with every right. The rules, each in §6: the host's `PATH` is never interpolated into a script — it
  travels as a data file the script reads into a quoted variable; every launcher is an argument array, never a composed command
  string, and **every remote step is a script FILE copied by `scp` and run by its path** — never an inline `bash -c` string through
  SSH, because `ssh` joins its arguments with spaces into one string the remote shell parses again (cadence consult e9a9b20e,
  decision 3); every argument that crosses SSH has passed its pattern first; every run script traps `EXIT TERM INT HUP` so `<phase>.done`
  and `<phase>.exit` are written on an abort too (E1 plan round b54f22f5, finding 2); the series path is written ONCE into
  `<folder>/SERIES_PATH` and read from there by every later step, cleanup included; **node is resolved to its ABSOLUTE path once** and
  every root-run step runs that path, never `node` through `PATH`, and every `PATH` entry a root-run step resolves a tool through is
  checked (finding 5); and **an abort is a lifecycle, not a pid file** (decision 3): each run script records its pid, its process group
  and its start time (`/proc/<pid>/stat` field 22), the abort checks them before any signal (a reused pid reads as gone), signals the
  GROUP, also signals the owned processes that left the group (a child that called `setsid`), and escalates to KILL while the GROUP
  still exists (`kill -0 -- -<pgid>`) — not while the leader does, because the leader may be gone while a child that ignores TERM
  survives. A poll has a deadline (§7).

- **DN12 — Kimi's version is an admission precondition** (gate finding 9, accepted). The WSL2 record is Kimi 2.2.0 (RESULTS_kimi §32).
  N-K0 records `kimi --version` on the native host FIRST; the admission remarks (`HostAdmission.cs`), the `kimi`/`linux` limitation row
  and RESULTS §33 all NAME the measured version. A pass on another version than 2.2.0 is recorded as *"measured with Kimi X on a native
  Linux host; the WSL2 record is 2.2.0"* in those three places, and the owner decides whether one of the two is re-run on the other version
  before Kimi is admitted (§7). No run-time version probe is added — a `kimi --version` is a launch, and a launch is what the admission
  guards; the version is a precondition of the DECISION, written where the decision is written.

- **DN13 — The live check runs on a locally built binary BEFORE any release, and a release has a rollback** (gate finding 7, accepted).
  P7's live round does not wait for a tag: the admission branch is published as the release does (`dotnet publish` linux-x64, Native AOT,
  on the lead's WSL2 — the host has no SDK, DN3), copied by `scp` into the series folder with its sha256 recorded, and the extension on
  `ubuntu-claude` is pointed at it (the server-path setting) for one live plan round; only a green round lets the release be tagged. If the
  live check fails AFTER a release, the rollback is: revert the admission arm (DN1's set loses the runtime; the forced keyring backend
  of DN10 stays — it only closes a road) in a patch release of `src_mcp`, and bump `ON_NATIVE_LINUX_SINCE.<runtime>` in the extension PAST the bad minor so no
  card promises what the server no longer does — the same DL14 rule read backwards.

## 5. The cells

### Host facts (free, `scripts/probe-native-linux-host.mjs`, one reading)

| Cell | What is recorded |
|---|---|
| N-H1 | `/proc/version` (must name no Microsoft/WSL) and `/etc/os-release` `PRETTY_NAME`; glibc version (`pidfd_open` wrapper ≥ 2.36, else the raw syscall — DL9 F4); node version; the uid; `fs.protected_symlinks`; the mount TYPE (from `/proc/self/mountinfo`) of `/tmp`, `/root`, `/root/.local/share` (coai's Vibe homes and `kimi-locks` root, `KimiCredentialsLock.cs:88`, `VibeHomes.cs:10-11`), `/root/.vibe`, and `/root/.kimi-code/credentials` when present (a network type is refused by the product — DL6/Q7); the VS Code server's `PATH` (the `PATH=` line of its environment only, nothing else of it); **node's ABSOLUTE path, resolved ONCE through that `PATH` by `make-series.sh` (`run/node.txt`), which every root-run step then runs by that path (DN11)**; every entry of that `PATH` with its owner and mode — an empty or relative entry, or a world-writable or non-root-owned directory BEFORE the directory a used tool resolves to, refuses the series; where `vibe`, `kimi`, `rg`, `fd`, `flock` resolve, in the product's own order (DL5); the absolute paths of `/bin/bash`, `/usr/bin/nohup`, `/usr/bin/setsid` the launcher names; the installed coai-mcp's path, `--version`, sha256, and its `--host-facts` line (0.62.0 has none: exit 64 is recorded as "no such mode", never as a fact) |
| N-V0 | `~/.vibe/.env` by `lstat` (type, size, mode, owner uid, mtime — never opened); the NAMES in `~/.vibe`; Vibe's tool Python and python-dotenv version; the installed distributions whose name holds `keyring`, `keyrings`, `secretstorage`, `jeepney` or `dbus` (names and versions); `keyring.get_keyring()`'s class name under the built environment (`env -i` + MinimalUnix + a fresh `VIBE_HOME`) AND under the same plus `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring` (must answer `keyring.backends.fail.Keyring` — DN10's free pre-check), never `get_password`; whether a `keyringrc.cfg` exists under `~/.config/python_keyring` (`lstat` only); the keyring-session facts by the script's OWN raw reading (DN10): `DBUS_SESSION_BUS_ADDRESS` set in the SSH session and in the VS Code server's environment (the name's presence only, never its value), a socket at `$XDG_RUNTIME_DIR/bus` or `/run/user/0/bus`, a `gnome-keyring-daemon`/`kwalletd*`/`keepassxc` in `/proc` |
| N-K0 | (after the owner's install; the FIRST Kimi cell, a precondition — DN12) `kimi --version` in the built environment, compared with the WSL2 record's 2.2.0 and named in §33 and in the admission; `kimi acp --help`; where `rg`/`fd` were found (`KimiTools` order: the person's bin, `PATH`, `~/.local/bin`); the credentials folder's NAMES (hex redacted), modes and owner; no file under it is opened |

### Vibe — ready now (`vibe 2.26.1` is signed in on the host)

| Cell | Command (from the copied checkout, §6) | Pass | Paid turns |
|---|---|---|---|
| N-V1 = L-V1 | `probe-vibe-confinement.mjs --vibe <DL5's vibe> --cells M-V7 --repeats 3` | arms a and a2 (on Linux both WITH the forced backend of DN10): exit 1, *Missing MISTRAL_API_KEY*, free, 3 of 3 each (DN4) — even when N-V0 shows a keyring present; **arm a3** (a fresh home, no `.env`, `HOME` → the probe's own fake home whose `.config/python_keyring/keyringrc.cfg` names a probe-owned backend that IS available, plus the forced backend): *Missing MISTRAL_API_KEY* 3 of 3 AND the backend's marker file never written; **arm a3c**, its teeth (the same fake home, NO forced backend): the marker file written 3 of 3 — else a3 proved nothing and the probe is fixed before N-V1 counts; the probe's backend returns no key, so a3c is free too; every signed-in home keeps `.env` as a link to `~/.vibe/.env` | 3 (arm b is paid on Linux, RESULTS_vibe §31.5) |
| N-V2 = L-V2/L-V3 | `--cells M-V6,M-V1 --repeats 3` | read inside served, outside denied 3 of 3; `write_file`, `bash`, `run_typescript` denied, no new file, 3 of 3 per tool | 12 |
| N-V3 = L-V4 | `--cells M-V5 --repeats 3` | the same session and the code word 3 of 3 | 6 |
| N-V4 = L-V7 | `vibe-home-manifest.mjs --vibe <…> --scratch <…>` (no `--write`) | no unknown path, no drift; the 12 `pinned` path→sha256 pairs EQUAL the shipped `linux` entry of `shared/vibe-home-manifest.json` (compared by a small script file kept beside the record; `measured` and `seen` sizes may differ) | 2 tiny |
| N-V5 = L-V6b | `dotenv-name-vectors.mjs --check` | 48 vectors, no drift | 0 |
| N-V6 = the host read twice (DN10) | `coai-mcp --host-facts` of the branch's LOCALLY BUILT binary (§6), with the facts script's own raw reading beside it — two independent readings | host kind `linux`, banner read, and each runtime's admission answer as the branch means it; the keyring-session facts AGREE with the raw reading (they are recorded, they decide nothing) | 0 |

Around every phase: `~/.vibe` listed (names, types, sizes, modes, mtimes) before and after — identical; `~/.vibe/.env` `stat` identical.
**Vibe spend: about 23 paid turns; cap 30.**

### Kimi — after the owner installs Kimi and runs `kimi login` himself on `ubuntu-claude`

| Cell | Command (from the copied checkout, §6; `--shim <installed coai-mcp>`) | Pass | Turns |
|---|---|---|---|
| N-K1 = L-K1 + L-K2 + L-K3 | `probe-kimi-tree-hold.mjs --arms normal,normal-tools --repeats 3` | answered 6 of 6 (signed in through the symlink; no subreaper, pidfd or "unreadable listing" refusal from the shim as root); both lock files Free ≤ 2 s after the shim's exit; the credentials folder keeps its names, `700`/`600`, the link not replaced; Grep/Glob served by the host's `rg`/`fd`, no `bin/` in the owned home; `os/` EMPTY after every launch; the report's `survivors` empty | 6 |
| N-K2 = L-K4/L-K6 + DL9 | `probe-kimi-tree-hold.mjs --arms killed,killed-hard,killed-rg --repeats 3` — each repetition a PAIR: the kill, then the very next launch, a normal one (DN5 item 4) | per killed arm: the lock Held while `kimi` lives, Free ≤ 2 s after its exit; no descendant alive 2 s after `kimi`'s exit; each orphan's NEW PARENT recorded (pid, `comm`, `exe` basename — pid 1 or a `systemd --user` reaper), not only its liveness; **`killed-hard` (SIGKILL) counts only with a `*.survivors.json` seen at the kill**; the pair's normal launch answers, or is Busy naming a survivor and then clears — never Busy for good — and the marker it found is gone after it; no `*.survivors.json` left under `<scratch>/locks` at the end | 18 |
| N-K3 = L-K7 writes | `probe-kimi-confinement.mjs --cells M-K3 --repeats 3` (after P1) | Write, Edit, Bash refused through the shim, no file, no outside canary — 3 of 3 per tool; each `-p` control (owned home) wrote once | 12 |
| N-K4 = read boundary | `probe-kimi-confinement.mjs --cells M-K6 --repeats 3` (after P1) | Read inside served, absolute outside refused, 3 of 3. The Grep/Glob arm is RECORDED, not a gate — the limitation row already says Grep and Glob read anywhere | 6 |
| N-K5 = L-K3 inventory | `probe-kimi-confinement.mjs --cells M-K11c --repeats 1` | what lands in the owned home is the same families as §29 (`cache/query-store`, `device_id`, `logs`, `oauth/<slot>`, `sessions/…`, `session_index.jsonl`); a new name stops Kimi (§7) | 2 |

Around every launch: the credentials folder by `lstat` only; the rest of `~/.kimi-code` listed (names, sizes, modes, mtimes) — identical
but for Kimi's own temp-and-rename of the slot file. **Kimi spend: about 44 turns (N-K2's pairs added 6); cap 50.**

## 6. The procedure on `ubuntu-claude`

**What is copied.** Not a checkout of the branch — only what the probes import: `git archive <commit> scripts shared .github/scripts/lib`
(the Vibe probe imports `../.github/scripts/lib/resolved.mjs`; the manifest and dotenv scripts read `shared/`). `<commit>` is this
branch's commit with P1 in it, recorded in the RESULTS header. Before copying, `node --test scripts/lib/*.test.mjs` is green on Windows.

**Where.** ONE folder per series, made with mode `700`: `/tmp/testsToDelete-native-<vibe|kimi>-<yyyymmdd>-<6 hex>/`, holding
`checkout/` (the archive, extracted), `tmp/` (the probes' `TMPDIR`), `run/` (the run scripts), `logs/`, and one `--scratch` folder per
phase. Every probe gets `--scratch` inside it, so no `mkdtemp` default lands anywhere else.

The folder is made by ONE script, `scripts/native/make-series.sh`. The lead names the series on Windows, checks the name against the
pattern below, makes the bare folder (`['ssh', 'ubuntu-claude', '--', '/bin/mkdir', '-m', '700', '--', '<folder>']` — no `-p`, so an
existing folder fails), copies the script into it by `scp`, and runs it by path. It checks its own place (its folder matches the pattern,
is owned by root, mode `700`, not a symlink), makes the subfolders, writes the path into `<folder>/SERIES_PATH` — the only place the path
is ever written; every later step reads it from there (DN11) — and bootstraps the two data files every run script reads: `run/path.txt`
(the VS Code server's `PATH=` line, read from the environment of the server's process — its `exe` under `~/.vscode-server/` — and
nothing else of it; no server running → it refuses: Remote-SSH must be connected) and `run/node.txt` (node resolved ONCE through that
`PATH`, `realpath`'d, after the `PATH` check of N-H1 passed). The facts script then records both. `tmp/` is mode `700`
like the rest: every process here runs as `root` and none drops privileges, so a world-writable `1777` folder would only let another
account plant files in the probe tree (gate finding 2, rejected); a probe that cannot write its `TMPDIR` fails with `EACCES`, and §7
records that as a failed run.

**How each probe runs (DN11).** The lead writes one run script per phase on Windows from a template, copies it into `run/` with `scp`,
and starts it BY ITS PATH — never as an inline `bash -c` string through SSH (`ssh` re-joins its arguments into one string the remote
shell parses). The launcher is an **argument array** on the Windows side — `['ssh', 'ubuntu-claude', '--', '/usr/bin/nohup',
'/usr/bin/setsid', '/bin/bash', '--', '<folder>/run/n-v2.sh']` — after `<folder>` has passed
`^/tmp/testsToDelete-native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}$` and the phase `^[a-z0-9-]{1,32}$`; every program is named by its absolute
path, and the script takes no argument and composes no command from anything it reads. Inside, in this order:

```bash
set -eu
export PATH=/usr/bin:/bin                                                     # the script's OWN helpers; the host PATH below is the probe's only
SERIES="$(cat /tmp/testsToDelete-native-vibe-20261021-a1b2c3/SERIES_PATH)"   # the one literal, read once; must equal this script's folder's parent
PHASE=n-v2
exec > "$SERIES/logs/$PHASE.log" 2>&1 < /dev/null
finish() { rc=$?; echo "$rc" > "$SERIES/logs/$PHASE.exit"; touch "$SERIES/logs/$PHASE.done"; }
trap finish EXIT
trap 'exit 143' TERM; trap 'exit 130' INT; trap 'exit 129' HUP              # a signal becomes an exit, so `finish` runs on an abort too
stat="$(cat /proc/$$/stat)"; set -- ${stat##*\) }                             # the fields after comm: $3 = pgrp (field 5), ${20} = start time (field 22)
echo "$$ $3 ${20}" > "$SERIES/logs/$PHASE.handle"                             # setsid made $$ the group leader; the abort checks all three
NODE="$(cat "$SERIES/run/node.txt")"                                          # node's ABSOLUTE path, resolved once by make-series.sh (N-H1)
HOST_PATH="$(cat "$SERIES/run/path.txt")"                                     # the VS Code server's PATH line from N-H1, one line, as DATA
cd "$SERIES/checkout"
env -i HOME=/root USER=root LOGNAME=root SHELL=/bin/bash LANG=C.UTF-8 TMPDIR="$SERIES/tmp" PATH="$HOST_PATH" \
  "$NODE" scripts/<probe> … --scratch "$SERIES/$PHASE" &
wait "$!"                                                                     # bash defers a trap while a FOREGROUND child runs; `wait` does not
```

So the probe sees the environment the product's server has, not the login shell's, and the host's `PATH` is never interpolated into
script text (gate finding 1); `.done` and `.exit` are written on EVERY exit, a crash, a `set -e` stop and a TERM, INT or HUP included
(finding 3; E1 plan round finding 2); the handle is on disk (finding 8). `nohup setsid` keeps a turn alive if the SSH link drops. `.done`
means "the run script ended", never "every process it started ended" — that is the abort's question, asked of the group.

**Polling, with a deadline.** The lead polls `['ssh', 'ubuntu-claude', '--', '/usr/bin/test', '-f', '<folder>/logs/<phase>.done']` and the
log's size. The phase's budget is its turns × the probe's deadline (240 s) + 10 minutes. A `.done` → read `.exit` and the tables, and
check the group is gone (below). No `.done`, the log unchanged for 15 minutes AND the group gone → a failed run (§7), recorded with the
log's tail. No `.done` and the group alive past the budget → **the abort**, `['ssh', 'ubuntu-claude', '--', '/bin/bash', '--',
'<folder>/run/abort.sh', '<phase>']` (cadence consult e9a9b20e, decision 3):

1. read `<phase>.handle` (pid, pgid, start time); if the pid lives, its `/proc/<pid>/stat` start time must equal the recorded one, else
   the pid was reused and nothing is signalled; if the leader is gone, the group may still live (a pgid number is not reused while any
   member holds it), and every member must have started at or after the recorded start time, else nothing is signalled;
2. snapshot the OWNED set before any signal: every process whose group is `<pgid>`, plus every descendant (by parent pid) of the leader
   and of each member — which catches a child that called `setsid` and left the group while its parent lives — each as (pid, start time);
3. `kill -TERM -- -<pgid>`, and TERM each owned pid outside the group;
4. after 10 s, while the GROUP exists (`kill -0 -- -<pgid>`) — not merely the leader — or any snapshotted (pid, start time) still lives,
   snapshot again (union) and `kill -KILL` the group and each such pid;
5. print what was signalled and what, if anything, survived. Nothing is ever ended by name (finding 8).

What it cannot find, said once: a process that left the group AND lost its parent before the snapshot (a double-forked daemon) is
reparented away from the tree. No probe here daemonizes; Kimi's `rg` runs in its own session but under a live `kimi`, inside the shim's
subreaper (L-K4). A cgroup would catch even that, and is not used: the GitHub runner that tests these scripts (S-N15) runs them unprivileged,
and a PID namespace would hide the owner's `kimi` from the probes' guard.

The lead reads only the log and the probe's tables. The full outputs stay on the host until the record is written; what comes back by
`scp` is the tables, the JSON rows and `steps.json` — never a home folder.

**The local binary (DN13, N-V6 and P7).** `dotnet publish` of this branch's `src_mcp` for linux-x64 on the lead's WSL2, copied by `scp` to
`<folder>/bin/coai-mcp`, mode `700`, its sha256 in the record. It serves `--host-facts` in N-V6 and the live round in P7; the measurement
shim of the Kimi cells stays the INSTALLED 0.62.0 (DN3).

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

**Clean-up, by the recorded path only** (DN11, gate finding 5 — the earlier "retype the literal" rule is withdrawn: a retyped path is a
second copy that can differ from the one the series used). After the record is written and reviewed, the copy of
`scripts/native/remove-series.sh` in `<folder>/run/` is run by its path — `['ssh', 'ubuntu-claude', '--', '/bin/bash', '--',
'<folder>/run/remove-series.sh', '<folder>']` — and does, in order: (1) reads `<folder>/SERIES_PATH`, refuses unless it matches
`^/tmp/testsToDelete-native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}$` AND equals both the folder it was given and the folder it was read from
(`realpath`) — a `SERIES_PATH` that says anything else ends the script before any `rm`; (2) lists every symlink in the folder
(`find "$SERIES" -type l -printf '%p -> %l\n'`); (3) `stat` `~/.vibe/.env` and lists `~/.kimi-code/credentials`; (4) `rm -rf -- "$SERIES"`
— the checked variable, never a glob (GNU `rm -r` unlinks a symlink, it never follows it); (5) the `stat` and listing again — identical.
Everything it says goes to its STDOUT, which the lead's `ssh` captures — never into the folder it removes. Then
`ls -d /tmp/testsToDelete-native-*` shows nothing of this series. **It removes its own file in step 4, and step 5 still runs** (E1 plan
round b54f22f5, finding 3, rejected): bash reads a script through a descriptor it holds open, and on Linux an unlinked file stays readable
through a descriptor already held, so the rest of the script is still read; S-N15's Linux test asserts step 5's output after the removal.

## 7. Stop rules

A failing cell keeps its runtime refused, and the failure is recorded with its numbers. Specifically:

- a confinement cell below 3 of 3 → that runtime stays refused;
- `~/.vibe` changes, or `~/.kimi-code` changes outside Kimi's own temp-and-rename of the slot file, or a sign-in link is replaced or
  re-targeted → stop that series at once;
- **N-V1 arm a or a3 is signed in, or a3's marker file is written** (a keyring reaches coai's launch with the road closed) → stop Vibe;
  the owner decides (DN4, DN10); **a3c writes no marker** → the arm proved nothing: the probe is fixed and N-V1 re-run, never counted;
- **`~/.vibe/.env` is absent** (the sign-in lives in a keyring) → stop Vibe before any paid run; the owner decides whether to make a
  `.env` sign-in himself — the lead never runs `vibe --setup`;
- **N-V4: a pinned hash differs, or an unknown path** → Vibe stays refused; one `linux` entry cannot serve both hosts, and a per-host
  entry is a new decision for the owner;
- **any Kimi launch exits 77 ("not signed in")** → stop the Kimi series at once, do not retry; the owner signs in again himself (a run
  of launches has signed the owner out before);
- another `kimi` still runs after 10 minutes → that run is skipped, never killed;
- a survivor after a NORMAL teardown, a lock still Held 2 s after `kimi`'s exit, or a Busy that never clears → Kimi stays refused;
- the Kimi on the host is not 2.2.0 (N-K0, DN12) → recorded FIRST, before any paid Kimi cell; a later pass names the measured version
  in §33, the admission remarks and the limitation row, and the owner decides whether one cell is re-run on the other version before
  Kimi is admitted; a failure on that version → recorded with both facts; Kimi stays refused, and the owner decides whether to re-run
  that cell on WSL2 with the same version to tell host from version;
- **N-K5 shows a file family in the owned home that is not in §29's list** (`cache/query-store`, `device_id`, `logs`, `oauth/<slot>`,
  `sessions/…`, `session_index.jsonl`) → Kimi stays refused; the new name is recorded (path shape, size, whether it is written once or
  per turn), never its contents; the owner decides what it is and whether it is allowed (gate finding 10);
- **a run with no `.done`, a log unchanged for 15 minutes and no live group** (§6 polling) → a failed run: the log's tail and the `.exit`
  file, if any, are recorded; the cell is not retried blind — the cause is named first; a group alive past the phase's budget is aborted
  (§6) and counted the same way; an owned process that survives the abort's KILL → the series stops and the owner is told;
- **N-K2: a `killed-hard` repetition with no `*.survivors.json` seen at the kill** → that repetition is not evidence about an abandoned
  marker; it is recorded and repeated once, and a second miss stops the Kimi series (the instrument, not Kimi, is in question);
- a probe that cannot write its `TMPDIR` or `--scratch` (`EACCES`, `ENOSPC`) → a failed run, recorded; the folder's mode is checked,
  never widened;
- **the product's `--host-facts` and the facts script's own reading disagree** (N-V6) on the host kind or whether the banner was read →
  stop Vibe before any admission: the admission reads those two, so it would be wrong the same way; a disagreement on a keyring-session
  fact → recorded as a defect of the reader and fixed before E2 merges, not a stop — those facts decide nothing (DN10);
- the spend cap (Vibe 30, Kimi 50) is reached → stop and ask.

## 8. The code change after the measurement

Only for a runtime whose cells all passed. If only Vibe passed, only Vibe changes; Kimi's texts stay.

- **Server.** `HostAdmission` (`src_mcp/runners/Platform/HostAdmission.cs:3-37`): the runtime enters DN1's set (the arm itself, and the
  unreadable banner per DN6 — `WslGenerations`, `WslGeneration.cs:20-34`; `VibeHost.cs:18-23` and `KimiHost.cs:17-18` computing the
  generation for `HostKind.Linux` too — are E1's). The remarks at `HostAdmission.cs:3-11` and `VibeLaunch.cs:72-78` say what is measured where, naming
  RESULTS §32 (Kimi: §33). The set, the banner rule and `--host-facts` are already there from E1 (DN1, DN6); E2 and E3 each add one
  entry. **Vibe's native-Linux launch closes the keyring road (DN10)** — `VibeLaunch.Built` carries
  `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring` on `HostKind.Linux` only, and `EnvironmentRefusal` admits that fifth name there
  and only there; WSL2 launches are unchanged. `HostAdmission.Refusal` has exactly two callers, `KimiLaunch.HostRefusal` (`src_mcp/runners/Kimi/KimiLaunch.cs:29`)
  and `VibeLaunch.HostRefusal` (`src_mcp/runners/Vibe/VibeLaunch.cs:79`), so no other runtime can receive a native-Linux sentence
  (gate finding 4, verified and rejected). Nothing below the admission changes: the link, the homes, the locks, the mount rule and the
  teardown already take `HostKind.Linux or HostKind.Wsl` (`VibeSignIn.cs:29-32`, `VibeRuntime.cs:110`, `Junctions.cs:145-229`,
  `HostKind.cs:53`).
- **Limitation rows.** `shared/consultant-limitations.json` — the `vibe`/`linux` row (`:251-257`, `standing: unmeasured`) becomes
  `confined`, worded like the `wsl` row (`:262`) and naming §32, and saying that **on a native Linux host coai turns Vibe's keyring off,
  so it signs in only through `~/.vibe/.env`** (DN10); the `kimi`/`linux` row (`:277-283`) the same from §33 when Kimi passes, naming the measured Kimi version (DN12).
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
- **S-N17 (E1; Vibe's keyring arms, DN5 item 5).** *Given* M-V7's arms built for `linux`, *then* arms a and a2 carry
  `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`; arm a3 carries it AND a `HOME` whose `.config/python_keyring/keyringrc.cfg`
  names the probe's marker backend (written only inside the arm's scratch); arm a3c is a3 without the variable; on `win32` nothing
  changes. RED today: `"M-V7 arm a on Linux carries no PYTHON_KEYRING_BACKEND — it measures today's open keyring road, not the launch E2
  ships (DN10)"`.
- **S-N18 (E1; N-K2's pairs, DN5 item 4).** *Given* `--arms killed,killed-hard,killed-rg --repeats 3`, *then* the schedule is PAIRS — each
  killed launch immediately followed by its normal launch, nothing between; `killed-hard` sends SIGKILL and `killed` SIGTERM; a
  `killed-hard` row with no marker seen at the kill says "not evidence"; every row names each orphan's new parent. RED today: `"the
  schedule ran killed-1, killed-2 and killed-3 before any normal launch — the next launch's reconcile was never observed right after a
  kill"`.

- **S-N19 (E1; the facts script, DN10's raw reading).** *Given* a fake home holding `~/.vibe/.env` and
  `~/.kimi-code/credentials/kimi-code-env-x.json`, *then* the facts record holds their `lstat` facts and the injected file reader is never
  called for either; *given* a fake `/proc/version`, environment, bus socket and `/proc` listing, *then* the raw reading reports each
  keyring-session fact by itself, without asking `coai-mcp`; *given* a `coai-mcp` that exits 64 for `--host-facts`, *then* the record
  says "no such mode", never an empty fact. RED against a stub that reads every file it lists: `"the facts record opened ~/.vibe/.env —
  a sign-in's bytes were read (§2 item 5)"`.

Server, E1 (the shared plumbing, both runtimes still refused) — `CoaiMcp.Tests`:

- **S-N7 (teeth for DN6; moved into E1).** *Given* a measured set INJECTED as {Vibe} and native Linux with an UNREADABLE banner, *then*
  refused with the banner sentence; *given* the product's own set (empty in E1), *then* every native-Linux answer is byte-identical to
  today's (`KimiLaunchChecksTests.cs:198-201`, `VibeRuntimeTests.cs:366-368` unchanged). Its teeth the testing rule's way: apply the set
  alone, watch the unreadable case go red — `"an unreadable /proc/version was admitted as native Linux — a WSL1 kernel would start Vibe"`
  — then add the banner rule and watch it pass. Both observations go in the summary.
- **S-N16 (the per-runtime set).** *Given* an injected set {Vibe} and a read, non-WSL banner, *then* Vibe is admitted and Kimi keeps the
  `Wsl2Only` sentence; *given* the product's set, both keep it. RED against a stub that ignores the set: `"an injected measured set
  {Vibe} did not admit a native Linux host with a read banner — the set is not consulted"`.
- **S-N14 (`--host-facts`).** *Given* the server started with `--host-facts`, *then* it prints one JSON line (host kind, banner read,
  each runtime's admission answer, the keyring-session facts as RECORDED facts with no verdict) and exits 0 without touching a home, a
  lock or the network; on the Linux CI runner — a native Linux VM — the real line says `linux`, banner read, both runtimes refused. RED:
  `"coai-mcp --host-facts is not a mode — N-V6 has no product reading to set beside the facts script's own (DN10: two readings)"`.

Server (P3/P6, E2/E3) — `CoaiMcp.Tests`:

- **S-N5.** *Given* `HostKind.Linux` with a read, non-WSL banner, *then* a Vibe row starts, through a fake `vibe`, and the home holds
  the `.env` link (the native twin of `AVibeLaunchOnWslLinksTheSignInTests`). RED: the flipped S-V17 pins — `VibeRuntimeTests.cs:363-368`
  and `VibeLaunchControlTests.cs:192-201` now expect a start: `"a native Linux host should start a Vibe row now that it is measured
  (RESULTS_vibe §32), but it was refused: Vibe runs on WSL2 only…"`.
- **S-N6.** *Given* the admission matrix runtime × {Windows, WSL2, WSL1, native Linux, native Linux with an unreadable banner, macOS},
  *then* only the measured cells start, and Kimi on native Linux keeps its sentence until N-K passes (`KimiLaunchChecksTests.cs:196-201`,
  `KimiLaunchControlTests.cs:81-83`, `AKimiRowRunsThroughTheServiceOnWsl2Tests.cs:192-204` unchanged until then). RED: Vibe's native
  cell, as S-N5.
- **S-N8.** The limitation row for `vibe`/`linux` is `confined`, names §32 and says coai turns Vibe's keyring off on a native Linux host
  (the only sign-in is the linked `.env`); the loader test reads it.
- **S-N12 (E2 only — DN10, the closed road; E1 plan round b54f22f5, finding 0: it left P1/E1).** *Given* `HostKind.Linux`, *then*
  `VibeLaunch.Built` carries `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring` and `EnvironmentRefusal` admits it; *given* WSL2 or
  Windows, the built environment is today's four names and a launch carrying the fifth is REFUSED; *given* a server whose own environment
  sets `PYTHON_KEYRING_BACKEND` to anything, the launch carries coai's value, never the inherited one. Kimi's launch never carries it.
  RED: `"a native Linux Vibe launch carries no PYTHON_KEYRING_BACKEND — the host's keyring could sign a fresh owned home in (DN4)"`.
- **S-N13 (parity, TASK 2).** `scripts/lib/proc.test.mjs`: *given* fake `/proc` entries built from the product's own cases (DN5 item 1
  names them), *then* `isKimi` answers exactly as `ForeignKimiWatch.IsKimi` does for each — `MainThread` with `exe` → `…/kimi` is Kimi;
  `" (deleted)"` stripped once from `exe` only; a hidden `exe` with `argv[0]` `…/kimi` is Kimi; `comm` `kimi` with `argv[0]` `node` is
  Kimi; `systemd` is not. RED today (`linuxProcesses` has no `name` field): `"a running kimi acp (comm MainThread) was not seen as Kimi
  — the guard would let a second refresher start"` (this is S-N1's RED; S-N1 becomes the first case of this test).
- **S-N15 (E1; the run scripts, `node --test scripts/native/*.test.mjs`, on the Linux CI job).** *Given* a run script made from the
  template over dummy workloads — a child that ignores TERM, a child that calls `setsid` — and an unrelated SENTINEL process of the same
  user, *then* `.handle`, `.exit` and `.done` exist after a normal exit AND after a TERM, an INT and a HUP of its group; after the abort
  every OWNED process is gone (the TERM-resistant one by the KILL that the GROUP's existence triggered, the `setsid` one through the
  snapshot) and the sentinel lives; an abort whose recorded start time does not match signals nothing; the script's text holds no
  character of the `path.txt` content and runs node by `node.txt`'s absolute path; `make-series.sh` refuses a `PATH` with an empty or
  relative entry, or a world-writable directory before node's; `remove-series.sh` refuses a `SERIES_PATH` that does not match the pattern
  or names another folder, and — removing its own file — still prints step 5. RED: `"a run script killed mid-probe wrote no .done — the
  poll would wait for ever"` and `"the abort left the TERM-resistant child alive — it escalated on the leader, not the group"`.

Extension (P3/P6) — `npm test` in `src_vs_code`:

- **S-N9.** On `linux` with a server ≥ `ON_NATIVE_LINUX_SINCE.vibe` the Vibe card shows no host note; older shows "update"; Kimi still
  shows the refusal. RED: `vibeOnLinux.test.ts:116-122` ("refused at EVERY server version") flipped.
- **S-N10.** An unreadable banner shows a Vibe row as refused even though `linux` runs Vibe (`hostKindLogsAnUnreadableBanner.test.ts`).
  RED once `linux` runs Vibe: `"an unreadable banner promised a Vibe row"`.
- **S-N11.** The hint, the install note and the five help languages say Vibe runs on a native Linux host from that version
  (`vibeOnLinux.test.ts:186-195`, `:213-217`, `:302-309`, `:382-386` updated); `runtimeSinceIsAhead.test.ts` guards the new table.

## 10. Build order

1. **P1 — the instruments and the shared plumbing** (DN5, DN11; DN1's set and DN6's server half with both runtimes still refused;
   `--host-facts`; S-N1–S-N4, S-N7, S-N13–S-N19 red, then green). The facts script and the run scripts go first, then the shared
   plumbing with `--host-facts`, then the probe arms (Vibe's keyring arms before the Kimi fixes, which gate only P5). **This is epic E1;
   its stories are [PLAN_kimi_and_vibe_on_native_linux_E1.md](PLAN_kimi_and_vibe_on_native_linux_E1.md).**
2. **P2 — the Vibe series** on `ubuntu-claude` (N-H1, N-V0, N-V1–N-V6), §6 and §7; RESULTS_vibe §32 written.
3. **P3 — Vibe admitted on native Linux, with its keyring road closed** if every Vibe cell passed (§8, DN10; S-N5, S-N6, S-N8–S-N12). Then
   **P7's live round on the locally built binary (DN13)**, and only after it is green: release in the next `src_mcp` minor with an
   extension minor. If a cell failed: no code, the record only. **P2, P3 and the Vibe half of P7 are epic E2.**
4. **P4 — the owner** installs Kimi on `ubuntu-claude` with the panel's install button (DL12's line — `rg`/`fd` by `apt-get`, then
   Moonshot's script) and runs `kimi login` himself. This also presses DL12's install button live on a fresh machine, an item of the
   parent's open tail.
5. **P5 — the Kimi series** (N-K0–N-K5) **in the risk order the consult named** (634d4907): N-K0 (version, instruments) → N-K1
   (normal launch and tree hold) → N-K2 (the killed agent — by TERM and by KILL — descendant teardown, and the NEXT launch's recovery
   through `KimiSurvivorGate`, in pairs) → N-K3/N-K4 (the write and read boundaries) → N-K5 (inventory); RESULTS_kimi §33 written.
6. **P6 — Kimi admitted** if every Kimi cell passed (the same §8 change for Kimi, naming the measured version — DN12). Then its own
   P7 live round on a local binary; released with P3 if both are ready, else in the next minor. **P4–P6 and the Kimi half of P7 are
   epic E3.**
7. **P7 — the live check, twice, then promotion (DN13).** *Before* a release: on `ubuntu-claude` over Remote-SSH, the extension's
   server path pointed at the locally built binary of the admission branch, the providers answer shows the row enabled with no
   refusal, and one live plan round gets the row's answer — a red here is a code change, not a release. *After* the release: the owner
   presses *Update*, the same two checks pass on the released build — a red here is the rollback line of DN13. Then `/promote-plan`;
   the parent's open tail updated.

## 11. Test plan

- `node --test scripts/lib/*.test.mjs` — S-N1–S-N4, S-N13, S-N17–S-N19 red before P1's fix, green after (red messages recorded);
  `node --test scripts/native/*.test.mjs` — S-N15, on the Linux CI job (the scripts are bash; CI's `node --test "scripts/**/*.test.mjs"`
  step, `.github/workflows/ci.yml:197`, already picks a new folder up).
- `coai-mcp --host-facts` — S-N14 in `CoaiMcp.Tests`, and one real reading on the `ubuntu-latest` job compared with N-V6's shape.
- `dotnet build src_mcp` then `CoaiMcp.Tests --filter-class "*Vibe*"`, `"*Kimi*"`, `"*HostAdmission*"` — S-N7, S-N16 (E1); S-N5, S-N6,
  S-N8, S-N12 (E2/E3); then the whole suite.
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
  stop rule, one kimi per side, and ~44 turns spread over phases, not one burst.
- **A second device's `kimi login` with the same Kimi account** may affect the owner's WSL2 sign-in. Not known; the owner logs in, so
  he decides. Nothing here reads either sign-in.
- **Root everywhere.** The host runs everything as `root`: the foreign-Kimi listing judges every root process (kernel threads, systemd),
  and the `0700`/owner checks all see uid 0. Predicted harmless (root reads every `/proc/<pid>/exe`); N-K1's "no unreadable-listing
  refusal" is the check.
- **The VS Code server's PATH** lacks `/root/.local/bin`; the probes run with that PATH on purpose, so a tool found only there is found
  the product's way (DL5) or not at all — what the product would do.
- **A different Kimi version** than WSL2's 2.2.0 mixes "host" with "version" in a failure (stop rule).
- **One `linux` manifest entry for two hosts** — N-V4 is the check; a mismatch is a stop, not a silent warn-and-run.
- **The measured host is headless; the admission must not outrun it.** A pass as `root` over SSH with no session bus says nothing
  about a desktop Linux. DN10 closes the road instead of guessing the host: coai's launch forces a failing keyring backend, N-V1's a3/a3c
  prove the variable beats a backend that IS available, and S-N12 pins the launch. What it still rests on: `keyring`'s environment-first
  order (read in 25.7.0's source, proven on the host by a3) and Vibe reaching its key on Linux only through `keyring`
  (`vibe/utils/keyring.py` in 2.26.1) — a later Vibe that read a keyring another way would need this re-measured, which the version
  warning already asks for.
- **The host's `PATH` is attacker-shaped data** (a root-run script, a line read from another process's environment). DN11 keeps it
  out of script text; S-N15 checks the script holds none of it.
- **A dead run with no `.done`** would be polled for ever; a `.done` written by a trap and a poll with a deadline (§6, §7) end it.
- **An abort that misses a process** — a child that ignores TERM, or one that left the group with `setsid`. The abort escalates on the
  GROUP, signals the owned set it snapshotted before any signal, and checks pid + start time; S-N15's dummy workloads are the test. A
  double-forked daemon would still escape (§6 says so); none of the probes makes one.
- **`node` or a tool resolved through a planted `PATH` entry**, run as root. node is resolved once to an absolute path and every `PATH`
  entry before a used tool is checked (N-H1); the run scripts' own helpers come from a fixed `/usr/bin:/bin`.
- **A cleanup aimed by a retyped path** can miss or hit the wrong folder; `SERIES_PATH` + the pattern + `realpath` equality (§6).

## 14. Owner prerequisites and questions

- Paid runs: approved 2026-10-21 (Vibe ≈ 23 turns, cap 30; Kimi ≈ 38 turns then, ≈ 44 with N-K2's pairs, cap 50 unchanged).
- P4: the owner installs Kimi with the panel button and runs `kimi login` on `ubuntu-claude`; closes his own `kimi` before each Kimi phase.
- **Q1** — if `~/.vibe/.env` turns out absent (the sign-in in a keyring), may the owner run `vibe --setup` there to make a `.env`, or
  does Vibe stay refused on that host? (Default: stop and ask; the lead never runs it.)
- **Q2** — if N-V4 shows other plugin hashes on native, a per-host `linux` entry or warn-and-run? (Default: Vibe stays refused until decided.)

## Definition of Done

- [ ] P1 (E1): S-N1–S-N4, S-N7 and S-N13–S-N19 observed red with the messages above, then green; the facts script never opens a sign-in
      file and takes its own raw reading; `isKimi` is the product's rule, ported once; the run scripts carry no host data in their text,
      run node by its absolute path, and abort by group, snapshot and start time; both runtimes still refused on every host.
- [ ] A native-Linux Vibe launch carries the forced keyring backend (S-N12, E2), N-V1's a3 and a3c both held, and every text says coai
      turns Vibe's keyring off there; WSL2 launches unchanged.
- [ ] A live round on the LOCALLY BUILT binary answered before each release was tagged (DN13); the rollback line is in the release notes.
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
  (P2) starts as soon as the facts script is merged into this branch; the Kimi probe fixes may land after it. *(Superseded in part by
  the cadence consult e9a9b20e: the series starts after E1 is merged WHOLE — the facts script, the run scripts, the shared plumbing with
  `--host-facts`, Vibe's keyring arms, and the Kimi probe fixes with them, because E1 is one pull request.)*
- **`ON_NATIVE_LINUX_SINCE`:** the version is the next minor of coai-mcp at the time of merge (0.63.0 today), with the
  second-merger rule: whoever merges second re-checks it against the changelog.
- **Section numbers:** Kimi's native-Linux record is §33 (see DN9); Vibe's is §32 of its own file.

## The plan gate (942810c1, 2026-10-21)

Verdict `good_enough`. Every finding, its decision, and where it was folded. "Accepted" changed the plan's text; "rejected" is
recorded here with its reason and changed nothing but a sentence that now states the verified fact.

| # | Finding | Decision | Folded into |
|---|---|---|---|
| 0 | §2/§6 promised "sign-ins are never read", but the runtime reads them to authenticate; and the confinement probe hashes `~/.kimi-code`, credentials included | **accepted** | §2 item 5 rewritten: the PROBES never inspect, hash, copy or print credential contents; the RUNTIME reads them in the named paid cells (N-V1 arm b, N-V2, N-V3; N-K1–N-K5). DN5 item 2: `manifest` takes a `skip` filter, `credentials/` is `lstat` only, `fileHash` injected and proven uncalled (S-N2) |
| 1 | The host's `PATH` interpolated into a root-run script | **accepted** | DN11; §6 run script: `path.txt` read into a quoted variable, launchers as argument arrays, the folder validated by pattern; S-N15 |
| 2 | Make `tmp/` a `1777` TMPDIR | **rejected** — every process runs as `root` and none drops privileges, so no mode is needed for a permission; `1777` would let other accounts plant files in the probe tree; an `EACCES` is a failed run §7 records | §6 (the folder paragraph), §7 (the `TMPDIR` stop rule) |
| 3 | A run script that dies writes no `.done` | **accepted** | DN11; §6: `trap … EXIT` writes `<phase>.exit` and `<phase>.done` on every exit; S-N15 |
| 4 | Other runtimes could receive the WSL2-only sentence | **rejected** — verified: `HostAdmission.Refusal` has exactly two callers, `KimiLaunch.HostRefusal` (`src_mcp/runners/Kimi/KimiLaunch.cs:29`) and `VibeLaunch.HostRefusal` (`src_mcp/runners/Vibe/VibeLaunch.cs:79`) | §8 server bullet states the fact |
| 5 | Cleanup by a retyped literal path | **accepted** | DN11; §6: `make-series.sh` writes `<folder>/SERIES_PATH` once; `remove-series.sh` reads it, checks the pattern and `realpath` equality, then removes; the "literal typed out" rule withdrawn; §13 |
| 6 | **The keyring condition**: the measured host is headless; a pass there must not admit a desktop Linux | **accepted** | DN10 (the run-time condition, one implementation `DesktopKeyringSession` + `--host-facts`); §2 item 2; §5 N-V6; §7 (disagreement stop); §8 (the arm, the limitation row and help wording "native Linux without a desktop keyring session"); S-N12, S-N14; E2's cornerstone C1. **Superseded by the cadence consult e9a9b20e, decision 1**: the road is closed by the launch, and the session facts are only recorded (DN10) |
| 7 | The live round should run before a release, on a local binary; and a rollback is needed | **accepted** | DN13; §6 (the local binary); §10 P3, P6, P7 (twice: before on the local binary, after on the release); the rollback: revert the admission arm in a patch release and bump `ON_NATIVE_LINUX_SINCE` past the bad minor |
| 8 | Abort by pid and group, never by name; the poll needs a deadline | **accepted** | DN11; §6: `<phase>.pid`, abort = `kill -TERM -- -<pid>` after the pid is checked to still name the script, `-KILL` 10 s later; polling budget and the "no log growth, no live pid, no `.done`" rule; §7 (failed run); S-N15. **Widened by the cadence consult e9a9b20e, decision 3**: `<phase>.handle` (pid, pgid, start time), escalation on the group, the owned-set snapshot, `abort.sh` run by path (§6) |
| 9 | Kimi's version is an admission precondition | **accepted** | DN12; §5 N-K0 is the FIRST Kimi cell and names the version; §7 (the version rule rewritten); §8 limitation row names the measured version; §10 P5 order |
| 10 | §7 lacked the stop rule for a new file family in N-K5 | **accepted** | §7: a family not in §29's list → Kimi stays refused, the name recorded (never the contents), the owner decides |

**Reuse folded at the same time (gate task 2).** The product's Kimi-identity rule (`ForeignKimiWatch.IsKimi` + `LinuxProcessListing`'s
name reading) is ported ONCE into `scripts/lib/proc.mjs` with a parity test over the product's own cases — DN5 item 1, S-N13. The
probes' "is another kimi running" guard and the tree-hold kill targets call that one `isKimi`; no second guess.

**The consult (634d4907, 3 of 3)** favoured measure-then-admit per runtime, Vibe before Kimi, probe trust first; it named the boundary
between measured evidence and permission to launch with the owner's credentials as the plan's cornerstone, and for Kimi the
survivor/teardown lifecycle through the NEXT launch (`src_mcp/runners/Kimi/KimiSurvivorGate.cs`). The epics below follow that.

## The E1 plan round (b54f22f5, 2026-10-21)

Verdict `proceed`. Seven findings; six accepted, one rejected. Where each was folded:

| # | Finding | Decision | Folded into |
|---|---|---|---|
| 0 | S-N12 (Vibe admitted, the keyring condition) was listed under P1/E1, but it tests an ADMISSION, which E1 must not make | **accepted** — S-N12 leaves P1/E1 and belongs to P3/E2 only (and was then rewritten by the cadence consult below) | §9 S-N12 ("E2 only"); §10 P1; E1's *Owns*; the DoD |
| 1 + 6 | A new one-shot mode must be sanctioned where this repository sanctions them: `.agents/PROJECT.md` lists every mode that may write to stdout and exit (`.agents/PROJECT.md:53-55`, the line ending `--ask-acp`, `--kimi-clear-marker` and `--agy-hook`), and `--host-facts` was not in it | **accepted** — `--host-facts` joins that list, after `--kimi-clear-marker`, in E1 (P1); it is read-only, never a writer mode, and as a mode it KNOWS it never answers 64 | E1's *Scope*; the E1 stories (E1.S2) |
| 2 | The run script trapped `EXIT` only, and bash does not run an `EXIT` trap when a signal with its default action kills it, so an abort left no `.exit`/`.done` | **accepted** — the trap covers `EXIT TERM INT HUP` (each signal becomes an `exit`, which runs the one `finish`), and the probe runs as a background job under `wait`, because bash defers a trap while a foreground child runs | DN11; §6 (the template); S-N15 |
| 3 | `remove-series.sh` lives in the folder it deletes, so step 5 (the `stat` after the `rm`) would never run | **rejected** — bash reads a script through a descriptor it already holds, and on Linux an unlinked file stays readable through that descriptor, so the rest of the script is still read and step 5 runs. S-N15's Linux test asserts step 5's output after the removal, so the claim is checked rather than argued. (The script's output goes to its stdout, never into the folder it removes — that part was a real gap and is fixed in §6.) | §6 (clean-up); S-N15 |
| 4 | DN10 said the facts script records `--host-facts` "rather than re-implementing the check", while N-V6 called for "the facts script's own raw reading beside it" — the two said opposite things | **accepted** — said plainly now: the facts script DOES take its own raw reading, because an independent reading is what makes a disagreement detectable; that is not a re-implementation of a decision (after the cadence consult the keyring facts decide nothing at all) | DN10 (*Two readings, on purpose*); §5 N-V0, N-V6; §7 |
| 5 | Root-run steps ran `node` through `PATH` — a world-writable or planted directory early in that `PATH` would run a stranger's `node` as root | **accepted** — N-H1 resolves node's ABSOLUTE path once (`make-series.sh` → `run/node.txt`) and every root-run step runs that path; every `PATH` entry before the directory a used tool resolves to is checked (an empty or relative entry, a world-writable or non-root-owned directory refuses the series); the run scripts' own helpers come from a fixed `/usr/bin:/bin`, and the launcher names `/bin/bash`, `/usr/bin/nohup`, `/usr/bin/setsid` | DN11; §5 N-H1; §6; S-N15; §13 |

## The cadence consult for epics 1–3 (e9a9b20e, 2026-10-21)

The consult on the cadence of epics 1–3, folded before E1's stories were written. Four decisions, each verified by the lead before it
was folded:

1. **THE KEYRING ROAD IS CLOSED, NOT GUESSED.** Verified: Vibe reads its key through the Python `keyring` package
   (`vibe/utils/keyring.py`; research/RESULTS_vibe_confinement.md:238), and `keyring` takes `PYTHON_KEYRING_BACKEND` from the environment
   before its config file. On a native Linux host coai's Vibe launch environment sets
   `PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring`, so no keyring backend is reachable and the sign-in can only come through the
   linked `.env`. The session-bus and keyring-daemon facts stay RECORDED facts (N-V0, N-V6, `--host-facts`), no longer the admission's
   condition. N-V1 runs WITH the forced backend, plus the new arms a3/a3c on a deliberately available backend. WSL2 launches are
   unchanged in this plan. The precedence claim is cited from `keyring` 25.7.0's source (DN10) and stays marked "to be proven by arm
   a3" until it runs. → DN4, DN5 item 5, DN10, DN13, §2 item 2, §5 N-V0/N-V1/N-V6, §7, §8, §9 S-N8/S-N12/S-N17, §13, C1, E1/E2.
2. **N-K2 must exercise abrupt death.** The probe's `child.kill()` (`scripts/probe-kimi-tree-hold.mjs:249`) is SIGTERM, which the shim
   handles and tears down on — **correction of the consult's citation:** the consult pointed at `src_mcp/src/Program.cs:2017`, which is the
   SIGTERM registration of `RefusingServeAsync`, the server that runs while the data folder is missing; the `--ask-acp` shim's is
   `Program.cs:442-451` through `StopSignals.Into` (`src_mcp/runners/Processes/StopSignals.cs:18-36`). The conclusion stands: a clean next
   launch after a TERM proves nothing about an abandoned marker. A `killed-hard` arm (SIGKILL) that counts only with the marker seen at
   the kill, killed→normal PAIRS instead of the arm-first loop (`:341-346`), and each orphan's new parent recorded. → DN5 item 4, §5
   N-K2, §7, §9 S-N18, E1.
3. **The remote abort is a lifecycle, not a pid file.** Escalate to KILL on the GROUP's existence (`kill -0 -- -<pgid>`), not the
   leader's; the run script records pid, pgid and start time (`/proc/<pid>/stat` field 22) and the abort checks them before signalling;
   every remote step is a copied script run by path, never an inline `bash -c` string through SSH. S-N15 gains a TERM-resistant child, a
   `setsid` child and an unrelated sentinel. **One addition by the lead:** a `setsid` child is outside the group, so escalating on the
   group cannot reach it — the abort also snapshots the owned processes (group members plus their descendants by parent pid, each with
   its start time) before the first signal and signals that set too; a double-forked daemon would still escape, and §6 says so. → DN11,
   §6, §7, §9 S-N15, §13.
4. **Dependency fix.** The shared per-runtime admission plumbing — the Linux arm of `HostAdmission` taking a per-runtime admitted set,
   the unreadable-banner refusal (DN6, server half), the `--host-facts` mode — moves into E1 with BOTH runtimes still refused, so no
   answer changes in E1 and E3 (Kimi) depends on E1, not on E2's outcome. E2 flips Vibe on and owns the forced keyring backend; E3 flips
   Kimi on. → DN1, DN6, §8, §9 S-N7/S-N14/S-N16, §10, C1, E1/E2/E3.

## Epics

Three epics, not two: E1 has no paid run and gates both series, and E2 and E3 cannot share a timeline — E3 waits on the owner's
install and sign-in (P4), E2 is ready today, and the order "Vibe before Kimi" is the consult's and the owner's. Folding E1 into E2 would
make the Kimi probe fixes ride a Vibe release; folding E2 into E3 would hold a measured Vibe hostage to a Kimi that is not installed.
Since the cadence consult (e9a9b20e, decision 4) the graph is **E1 → E2 and E1 → E3**: the shared admission plumbing is E1's, so neither
series epic waits on the other's outcome.

**Model roles (the operator's):** Fable writes the spec of each cornerstone; Opus writes the briefs and reviews every PR; Sonnet codes.
Scouts are Sonnet, ⌈1.5N⌉ per epic.

**Cornerstones (two over the whole plan, no more):**

- **C1 — the admission and the launch it permits**: the boundary between measured evidence and permission to launch with the owner's
  credentials. The per-runtime measured set and the read banner (DN1, DN6 — plumbed in E1 with the set empty), the CLOSED keyring road
  in Vibe's native-Linux launch environment (DN10 — the variable, the fifth built name admitted on that host only, never inherited from
  the server), and the measured Kimi version named where the decision is (DN12). Owned by E2, whose Fable spec reads E1's plumbing as
  its first input and may amend it in E2 (E1 merges nothing that admits); E3 reuses it and adds only the Kimi entry and the version.
- **C2 — Kimi's survivor and teardown lifecycle, including the NEXT launch**: `KimiSurvivorGate` reconcile → terminate by identity →
  confirm gone → clear or Busy (`src_mcp/runners/Kimi/KimiSurvivorGate.cs:57-72`), measured natively by N-K2 with the real Kimi under the
  installed shim's subreaper, and the probe's own kill-and-watch halves that must not mis-identify the agent (DN5 item 1). Owned by E3.

### E1 — Measurement foundation

- **Goal.** Every probe this plan runs on `ubuntu-claude` is trustworthy before a paid turn is spent: it identifies Kimi the product's
  way, never reads a credential's bytes, builds the product's environment (Vibe's closed keyring road included), sees an abrupt death,
  and its remote execution cannot be hurt by the data it reads or lose a process it started. And the admission's shared plumbing is in
  place with both runtimes still refused, so E2 and E3 each flip one entry.
- **Scope.** Stories in [PLAN_kimi_and_vibe_on_native_linux_E1.md](PLAN_kimi_and_vibe_on_native_linux_E1.md). `scripts/probe-native-linux-host.mjs`
  (N-H1, N-V0, N-K0, its own raw reading); `scripts/native/` (`make-series.sh` with the node and `PATH` checks, the run-script template,
  `abort.sh`, `remove-series.sh`, their tests); `HostAdmission`'s per-runtime set (EMPTY), `WslGeneration`'s unreadable answer and its
  sentence, a read-only keyring-session fact reader, `coai-mcp --host-facts`, and its line in `.agents/PROJECT.md`'s list of sanctioned
  one-shot modes; Vibe's M-V7 keyring arms (`probe-vibe-confinement.mjs`, `lib/vibe.mjs`); `scripts/lib/proc.mjs` (the ported `isKimi` +
  `name`), `scripts/lib/proc.test.mjs` (parity), `probe-kimi-tree-hold.mjs`'s four call sites and its `killed-hard` pairs,
  `probe-kimi-confinement.mjs` (the guard, the `credentials/` filter, M-K3/M-K6 on owned roots), `lib/kimi-home.mjs` (`XDG_CACHE_HOME`).
- **Owns.** §4 DN1's set (empty), DN2, DN5, DN6's server half, DN10's recorded facts and two readings, DN11; §5 host facts; §6; §9
  S-N1–S-N4, S-N7, S-N13–S-N19; §10 P1; §11's first two lines.
- **Depends on.** Nothing.
- **Done when.** S-N1–S-N4, S-N7 and S-N13–S-N19 observed red with their messages, then green; `node --test scripts/lib/*.test.mjs
  scripts/native/*.test.mjs` green on Windows (the bash tests skip there, saying so) and on the Linux CI job; `--host-facts` prints its
  line on the ubuntu runner and is in `.agents/PROJECT.md`'s list; `plan-lifecycle.mjs` clean; the Opus review has no open finding;
  **nothing here changes which runtime starts on which host** — every existing admission pin unchanged.
- **Cornerstone areas.** None owned — E1 builds the instruments C1 and C2 are measured with, and the plumbing C1's spec starts from.
  C2's spec (E3) is checked against E1's `killed-hard` pairs before N-K2 runs; a gap there is a probe change in E3, before the paid cell.
- **Risks.** A ported rule that drifts from the product (the parity test is the tie; its cases are the product's by name); a run script
  that still composes a command from host data (S-N15's "no character of `path.txt`" check); an abort that misses an owned process
  (S-N15's dummy workloads); the plumbing changing an answer (S-N7 and the unchanged pins); the `XDG_CACHE_HOME` fix changing a WSL2
  probe's behaviour (one `probe-kimi-tree-hold.mjs --arms normal --repeats 1` on WSL2 after the fix — a single paid turn, recorded,
  the owner's standing approval covers it).

### E2 — Vibe measured, then conditionally admitted

- **Goal.** Vibe 2.26.1 is measured on `ubuntu-claude` (N-V1–N-V6) with the keyring road closed as the product will close it, and —
  only on a full pass — admitted on a native Linux host, its launch there carrying the forced keyring backend, with the help, the card and
  the limitation row saying exactly that; released after a live round on a local binary, with a rollback line.
- **Scope.** The Vibe series (P2) and RESULTS_vibe §32; Vibe enters DN1's set; the forced keyring backend in `VibeLaunch.Built` and
  `EnvironmentRefusal` on `HostKind.Linux` only (DN10); the `vibe`/`linux` limitation row; `runtimeSince.ts` (`ON_NATIVE_LINUX_SINCE.vibe`,
  per-runtime `HOST_NOTES.linux`), the Vibe hint, `VIBE_NOTE_LINUX`, the unreadable-banner refusal in `hostSide.ts` (DN6's extension
  half), the help in five languages; `module_runners.md`, `module_extension.md`; P7 for Vibe (local binary, then the release).
- **Owns.** §3 Vibe; §4 DN4, DN6's extension half, DN7, DN8, DN9 (Vibe), DN10's forced backend, DN13; §5 Vibe cells; §7 Vibe rules; §8
  (Vibe); §9 S-N5, S-N6 (Vibe), S-N8–S-N12; §10 P2, P3, P7 (Vibe); §14 Q1, Q2.
- **Depends on.** E1 merged (the facts script, the run scripts, the plumbing with `--host-facts`, Vibe's keyring arms); the owner's
  approved spend (cap 30).
- **Done when.** §32 holds N-H1, N-V0, N-V1–N-V6 with predictions, commit, shim sha256, spend, `~/.vibe` `stat`-identical, and arms a3/a3c
  recorded as the proof of the closed road; either every cell passed and S-N5, S-N6, S-N8–S-N12 are green (S-N12's teeth shown red first)
  and the live round on the local binary answered, then the release and the post-release round — or a cell failed and the record alone
  shipped with nothing admitted; WSL2's Vibe launch unchanged (its pins untouched); the series folder removed by `remove-series.sh`;
  `plan-lifecycle.mjs` clean.
- **Cornerstone areas.** **C1** (Fable spec: the admission's inputs — the measured set, the read banner, the closed keyring road in the
  launch it permits, the refusal sentences — and the rule that absence of a fact is a refusal, never an admission; it starts from E1's
  plumbing).
- **Risks.** N-V1 arm a or a3 signed in, a3's marker written, or a3c's not written (stop / fix the probe); `.env` absent (stop; Q1);
  plugin hashes differ (stop; Q2); `--host-facts` disagreeing with the raw reading on host kind or banner (stop); a fifth built name that
  leaks onto WSL2 or Windows, or is inherited from the server (S-N12); the admission read as "native Linux" anywhere in the texts without
  the keyring sentence (S-N8, S-N11 pin the words); a release before the local-binary round (P7's order is the gate).

### E3 — Kimi measured, then conditionally admitted

- **Goal.** After the owner installs Kimi and signs in on `ubuntu-claude`, Kimi is measured in the risk order (instruments → normal
  launch and tree hold → killed agent, descendant teardown and the NEXT launch's recovery → write/read boundaries → inventory), and — only
  on a full pass — admitted on native Linux for the measured version, with its own live round, release and rollback.
- **Scope.** P4 (the owner; DL12's install button pressed live), the Kimi series (P5) and RESULTS_kimi §33; Kimi enters DN1's set (E1's
  plumbing — DN1, DN6 — plus DN12's version in the remarks; no keyring read); the `kimi`/`linux` limitation row;
  `ON_NATIVE_LINUX_SINCE.kimi`, the Kimi hint, `KIMI_NATIVE_LINUX`, the help; `module_runners.md` (*Kimi on WSL2* and its diagram); P7 for
  Kimi; the parent's open tail.
- **Owns.** §3 Kimi; §4 DN3, DN9 (Kimi), DN12; §5 Kimi cells; §6 the kimi rule; §7 Kimi rules (exit 77, survivors, Busy, version, the
  new-family rule, the `killed-hard` marker rule); §8 (Kimi); §9 S-N6 (Kimi); §10 P4–P6, P7 (Kimi); §13's first three risks; §14's P4 line.
- **Depends on.** E1 merged (the Kimi probe fixes — DN5 items 1–4 — the run scripts, and the shared admission plumbing); P4 done by the
  owner; the owner's approved spend (cap 50). **Not on E2** (cadence consult e9a9b20e, decision 4): the set E3 adds Kimi to is E1's, so
  a Vibe that failed its cells, or is still being measured, holds nothing here. If E2 is mid-flight, whichever merges second rebases onto
  the other's set entry and texts (the DL14 second-merger rule).
- **Done when.** §33 holds N-K0–N-K5 in that order with the measured version named, predictions, the installed shim's sha256, spend,
  `~/.kimi-code` listing-identical but for the slot's temp-and-rename; N-K2 records each orphan's new parent and, per pair, that the
  next launch reconciled the marker the kill left; either every cell passed, S-N6's Kimi cells flipped green, the live round on the local
  binary answered, the release and the post-release round — or a cell failed and the record alone shipped; the series folder removed;
  the plan promoted with `/promote-plan` once E2 is done too; `plan-lifecycle.mjs` clean.
- **Cornerstone areas.** **C2** (Fable spec: what N-K2 must observe of `KimiSurvivorGate` natively — the marker written, the orphan's
  new parent, the reconcile on the next launch, Busy that clears — and what the probe may never do to get it: no kill by name, no second
  identity rule). It is checked against E1's `killed-hard` pairs before N-K2 runs. C1 is reused, not re-spec'd.
- **Risks.** The rotating sign-in (exit 77 stops the series); a second device's login touching the WSL2 sign-in (the owner decides);
  a Kimi version other than 2.2.0 (DN12; the owner decides which host or version to re-run); root everywhere making the foreign-Kimi
  listing judge every process (N-K1's "no unreadable-listing refusal" is the check); the native kernel's reaper being pid 1 rather
  than WSL's `Relay` (N-K2 records it, L-N2 already covers the subreaper natively); a new file family (stop rule).
