# PLAN — the vault key reaches `creds` on stdin, never on its command line

> Status: **IMPLEMENTED, 2026-10-10** (released as an `mcp` release after creds `cli`, § 6; deviations § 8). Scope: `src_mcp/src/Server/KeyVault.cs`, its tests
> (`src_mcp/tests/KeyVaultTests.cs`, `src_mcp/tests/TheVaultIsFoundWhereCredsForDevsInstallsItTests.cs`), and the
> documents that describe the read (`.agents/PROJECT.md`, `research/architecture.md`, `research/module_server.md`).
>
> **Crosses a repository boundary.** This is the coai half of `dew_flow_creds_for_devs ·
> research/PLAN_config_key_off_the_command_line.md` (implemented there 2026-10-10, PR #202): the `creds` CLI now refuses a
> config key given as an argument and reads it from stdin (`creds config -`) or `CREDSFORDEVS_KEY`. The two halves ship
> in that order — creds `cli` first, then this — see § 6.
>
> Boundary with [PLAN_coai_finds_creds_where_it_is.md](../todo/PLAN_coai_finds_creds_where_it_is.md): that plan decides WHERE
> `creds` is found (PATH, the extension's folder, `COAI_CREDS_EXE`); this one decides only HOW the key is handed to
> whatever was found. Disjoint; either can land first.

## 1. The symptom

`KeyVault` starts `creds config <key>` with the vault's config access key as an argument
(`src_mcp/src/Server/KeyVault.cs:103`, `new ProcessRequest(candidate, ["config", configKey], …)`). A command line is not
private: inside WSL `/proc/<pid>/cmdline` is readable by every user (procfs has no `hidepid` by default), and on Windows
every process of the same user reads `Win32_Process.CommandLine`. The key is long-lived — it unlocks every vendor key the
gate uses. Observed 2026-10-10 on the owner's machine while verifying the creds half: a process-table sample inside WSL
caught a live `creds config <key>` command line carrying a real key (the sample was deleted at once). This repository's
own non-negotiable says *"No secret ever reaches argv"* (`.agents/PROJECT.md`, the vault bullet) — and the same bullet
then names `creds config <key>`.

## 2. What exists today — verified 2026-10-10

- `KeyVault.RunFirstInstalledAsync` (`KeyVault.cs:96-116`) tries `executable` then each fallback; only a
  `Win32Exception` (could not be started) moves on, and any started CLI's answer is final — "asking a second copy would
  turn one refusal into two reads of a person's vault".
- `ProcessRequest.StdIn` (`src_mcp/runners/Processes/ProcessLauncher.cs:24`) is written to the child's stdin and then
  closed; it is already how every long input travels, and the audit records only its LENGTH (`RoundAudit.cs:67`).
- The creds CLI from its next release prints `config-key-stdin` in `--help` (creds `CommandLine.ConfigStdinMarker`, its
  value pinned by a test there) and refuses `config <key>` with exit 96. An older CLI has no marker and reads the key
  ONLY from argv.
- `tests_fakecli` already answers a bare `--help` from `FAKECLI_HELP_STDOUT` and records each launch's argv and stdin
  under `FAKECLI_RECORD_DIR` — the seams the tests below need exist.

## 3. Design

1. **Probe, then read.** For each candidate, in the existing order: run `candidate --help` (bounded: 10 s, output
   capped at 64 Ki characters, stdin closed). A `Win32Exception` is "not installed here" and moves to the next candidate,
   exactly as today.
2. **A CLI that started is an answer.** Its help without `config-key-stdin` ends the read with `VaultKeys.None("the
   creds CLI is too old to take the config key on stdin (its --help does not name config-key-stdin) — update the creds
   CLI; the key is never passed as an argument")`. A probe that timed out or exited non-zero ends it too, with its OWN
   sentence (plan round 1: "update" is the wrong cure for a hung or broken binary) — "the creds CLI did not answer
   --help within 10 s" / "the creds CLI's --help exited N". **No fallback to the argument form**, and no second
   candidate: the person's machine has a stale or broken CLI, and saying which is the fix.
3. Otherwise `ProcessRequest(candidate, ["config", "-"]) { StdIn = key + "\n", Timeout = 30 s }` — the existing
   timeout, exit and parse handling unchanged.
4. Every sentence is a constant or names only the binary; none contains the key. The marker lives in one constant,
   `KeyVault.StdinMarker = "config-key-stdin"`, with a comment naming the creds constant it mirrors.
5. Documents: `.agents/PROJECT.md` (the vault bullet), `research/architecture.md` (the C4 relation), and
   `research/module_server.md` (the two places that say `creds config <key>`) describe `creds config -` with the key on
   stdin and the probe.

## 4. Build order and tests (RED first)

- **S1 RED** with `tests_fakecli`, `FAKECLI_RECORD_DIR` set: (a) a CLI whose help carries the marker → exactly two
  launches, `--help` and `config -`, the second with stdin = key + `\n`, and NO launch's argv contains the key;
  (b) help without the marker → `None` naming "update the creds CLI", exactly one launch (`--help`), the key in no argv
  and no stdin; (c) help that exits non-zero, and help that sleeps past a shortened probe bound → the same refusal;
  (d) a missing first candidate with a working fallback → the fallback is probed and read (the existing "not started →
  next" rule); (e) an OLD CLI first and a NEW one as fallback → refused, the fallback is never started; (f) every
  `Unavailability` sentence on every path is swept for the fake key, with a positive control.
- **S2 GREEN**: implement § 3; update the existing tests whose fake CLI answered `--help` with the vendor JSON.
- **S3** documents (§ 3.5).

All tests use fake key values only. Test runs: `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe` after
`dotnet build`; before the release, every test of the repository (mcp, server, extension `npm test`) and the family checks.

## 5. What grows

Nothing persistent. One extra short-lived `creds --help` per vault read, and a vault read happens once per server
start (`StartingHost`) and once per `--providers`/`--probe-api`/consultant check.

## 6. Release order

creds `cli` (with the marker) is released first. Then this ships as an `mcp` release. A machine that updates `coai-mcp`
before `creds` loses its vault keys with a sentence saying "update the creds CLI" — never a leak; a machine that
updates `creds` before `coai-mcp` loses them until this ships, because the new CLI refuses the argument form.

**Recovery, and why there is no compatibility window** (plan round 1). Keeping the two compatible would mean a CLI that
still accepts the key as an argument — the leak itself — so the owner chose to refuse it at once. The window is kept
short instead: this ships in the same working session as the creds release, and both releases are announced together.
During it, keyless vendors keep working and `providers` names the cause; the recovery is to update the lagging half
(`coai-mcp` through the extension's *Install the MCP server…*, or `creds` through CredsForDevs' *Install `creds`
(terminal CLI)…*) and restart the MCP client — the vault is read once per server start.

## 7. Definition of Done

- [ ] No `ProcessRequest` built by `KeyVault` carries the config key in `Arguments`; it travels in `StdIn` only.
- [ ] An old CLI is refused with "update the creds CLI", never given the key in any form.
- [ ] "Not started → next candidate" and "started → its answer" both still hold, pinned by tests.
- [ ] No sentence on any path contains the key (sweep test with a positive control).
- [ ] `.agents/PROJECT.md`, `research/architecture.md`, `research/module_server.md` updated.
- [ ] Plan and code rounds of the review gate passed; promoted on completion; released as `mcp` after creds `cli`.

## 8. What shipped, and how it differs

Built as § 3 says, with three changes from the code round and our own review:

1. **The read's outcome is a closed record hierarchy** — `Answered` / `Refused` / `NoneStarted` — rather than a null
   result and an empty refusal standing in for two of the three states.
2. **"Not started" means at the PROBE.** A CLI that answered `--help` and then could not be started for the read is
   that CLI's answer, with its own sentence ("could not be started for the read — was it removed or replaced just
   now?"); the first cut caught that exception around both launches and asked the next place, ending in "not
   installed".
3. **A cancelled probe says cancelled**, not "hung or broken", and the timeout/exit sentences are culture-invariant.

Tests beyond § 4: the request shapes are pinned (probe: `--help`, empty stdin, 10 s, 64 Ki; read: `config -`, key
plus newline on stdin, 30 s); a marker that appears only on stderr is not a marker; the marker's VALUE is pinned on
this side as it is on the creds side; every `FAKECLI_*` variable is reset around the vault classes, because the probe
made several more of them matter. The fakes of `ProbeApiModeTests` and `TheServerAnswersInitializeAtOnceTests` answer
`--help` as a current creds CLI.

**Rejected in the code round, with the reason:** a release check that runs the REAL creds CLI. CI here has no creds
binary and should not acquire another product's release artefact; the contract is pinned by value on both sides
instead, and the released CLI's `--help` is checked for the marker by hand before this ships (§ 6).
