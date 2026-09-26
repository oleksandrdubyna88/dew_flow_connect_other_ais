# PLAN — coai finds the `creds` CLI where CredsForDevs put it, reads its settings the way every other setting is read, and says exactly where it looked

> Status: **plan only, nothing implemented yet, 2026-09-26.** Plan gate passed (`proceed`, 3 of 3 reviewers — §9).
> Scope: `src_mcp/src/Server/KeyVault.cs`, the two places that construct it in `src_mcp/src/Program.cs`, a new pure
> locator, `src_vs_code/src/settingsShape.ts` + `package.json` (one setting), tests, and `research/module_server.md`.
>
> **The other half of a cross-repository change.** CredsForDevs issue #159; its plan is
> `dew_flow_creds_for_devs · todo/PLAN_creds_cli_reachable_from_every_caller.md`, which owns the contract this plan
> relies on (its §6, *the stable location*). Each plan names the other.

## 1. The symptom

After **Enable Code Access…** in CredsForDevs, an agent found that `coai` could not read the vault key on the owner's
Windows machine. `creds.exe` was installed, at `globalStorage/remsoftdev.creds-for-devs/bin/creds.exe`, and `coai`
said:

> the `creds` CLI is not installed on this machine

It is installed. It is not on the PATH of the process that asked.

## 2. What the code does — verified 2026-09-26

- **The executable is always the literal `"creds"`.** `KeyVault(IProcessLauncher launcher, string executable = "creds")`
  (`src_mcp/src/Server/KeyVault.cs:28`). Both constructions take the default: the `--providers` one-shot
  (`Program.cs:516`) and `ServeAsync` (`Program.cs:1722`). The parameter is a seam nobody fills.
- **Resolution is the process's own PATH.** `ExecutableResolver.Resolve` (`src_mcp/runners/Processes/ExecutableResolver.cs:29-69`)
  walks `PATH` on Windows and returns the bare name when nothing matches. `Process.Start` then throws `Win32Exception`,
  and `KeyVault.cs:49-52` turns that into *"not installed"*. That exception also covers "exists but cannot be
  launched", so two different failures produce one false sentence.
- **The PATH is whatever the MCP client had when it started this server.** `ProcessRequest.InheritsEnvironment`
  defaults to true (`ProcessLauncher.cs:97`, `:231-242`). An MCP server started by VS Code or Claude Code inherits
  their environment at *their* start, and never sees a PATH change made later.
- **CredsForDevs never puts `creds` on PATH by design** — its `binaryInstaller.ts:28-31`. This repository recorded
  that fact while designing this very dependency: `research/PLAN_connect_other_ais.md:85` says *"release asset →
  extension storage, never onto PATH"*, next to `:81`'s `creds config <key>`. The two were never reconciled.
- **A live defect in the same two lines (found by the own review, §9): `COAI_CREDS_KEY` bypasses the settings
  layer.**
  - Every other setting is read through `configuration`, the merged reader `SettingsFile.Layer` returns
    (`SettingsFile.cs:49-58`: the environment first, then `<dataDir>/settings.json`), via
    `PanelSettings.FromEnvironment(configuration)` (`Program.cs:512`, `:1714`).
  - The vault key alone is read with a raw `Environment.GetEnvironmentVariable(KeyVault.KeyVariable)`
    (`Program.cs:516-517`, `:1722`).
  - The extension writes `COAI_CREDS_KEY` into that same `settings.json` (`serverSettingsFile.ts:92-104`, from
    `envBlock()` at `settingsShape.ts:443` / `:565-566`). The server never reads that copy.
  - **So a key set in the panel does nothing** unless it is also pasted into the MCP client's `env` block and the
    client restarted. That is the scenario `ServerSettingsSync` exists to remove, and plausibly part of what the owner
    hit in #159.
  - Nothing tests it: every `KeyVaultTests` case passes the key as an explicit string.
- The test that pins today's sentence is `src_mcp/tests/KeyVaultTests.cs:93-102` (`MissingCredsBinary_IsNamed`,
  asserting `"not installed"`).

## 3. What changes on the CredsForDevs side (for context — not built here)

The paired plan adds an opt-in **Add `creds` to PATH**, which puts the binary at a **stable, published location**:

- Windows: `%LOCALAPPDATA%\Programs\creds\creds.exe`
- Linux / macOS: `~/.local/bin/creds`

This is the same location the published one-liners already use. The paired plan also makes WSL work through a Linux
`creds` that relays to Windows. Even so, `coai-mcp` started by an editor that was running *before* the PATH change
still cannot see it until that editor restarts, and a person who declines the PATH offer never has it on PATH at all.
Hence this plan.

## 4. Design

### 4.1 Both values come from `configuration` — `KeyVault.ForThisMachine(launcher, configuration)`

- **One factory for both construction sites** (`Program.cs:516`, `:1722`), taking the same `configuration` that
  `PanelSettings.FromEnvironment` takes. It reads `COAI_CREDS_KEY` **and** the new `COAI_CREDS_EXE` through it.
  The panel's setting now works (fixes the live defect in §2), and the one-shot and the server cannot resolve
  differently.
- **Precedence is `SettingsFile.Layer`'s, and it is stated rather than implied** (gate finding 1). A non-empty
  process environment variable wins, which means an MCP client's `env` block or the person's shell. Otherwise the
  value in `settings.json` applies, which the panel writes. `providers` names the source that won:
  `COAI_CREDS_EXE from the MCP client's environment` or `… from the panel's settings`. So the case "set in both,
  different" is visible instead of surprising.
- **`ForThisMachine` never throws** (gate finding 7). Every outcome is a `VaultKeys` value, so `--providers` keeps
  its JSON-on-stdout contract and its exit code exactly as today; any sentence for a person goes to stderr, per
  `SettingsFile.cs:45-47`. A test runs `--providers` with no `creds` anywhere and asserts both.

### 4.2 One pure locator — new `src_mcp/src/Server/CredsLocator.cs`

`CredsLocator.Resolve(explicitPath, isWindows, pathVariable, localAppData, home, probe) → CredsLocation`, where `probe`
is an injected `path → Absent | NotExecutable | Executable`. On POSIX `NotExecutable` means no execute bit for this
user; on Windows it means a directory or a file without an executable extension. The rungs, **in this fixed order**:

| Rung | Source | On a miss |
|---|---|---|
| 1. explicit | `COAI_CREDS_EXE` (from `configuration`, §4.1) | **stop and name it** — `COAI_CREDS_EXE (from the panel's settings) points at <path>, which does not exist` / `…which is not executable`. An explicit path is the person's decision, the same rule `ExecutableResolver.cs:36-40` applies; falling through would run a binary they did not choose. |
| 2. PATH | Windows: `ExecutableResolver.Resolve("creds", …)`, reused and not re-implemented. POSIX: a PATH walk of its own, because `ExecutableResolver` passes non-Windows names through untouched (`:36`), which **skips entries that are not executable, as a shell does** (gate finding 6) | next rung |
| 3. the stable location | the paired plan's §6: `%LOCALAPPDATA%\Programs\creds\creds.exe` / `$HOME/.local/bin/creds` — **only if executable** | not found |

- **Never `globalStorage/…`** — a deliberate exclusion, tested. That path depends on the editor build (Code,
  Insiders, VSCodium, Cursor), the profile and portable mode, and it is CredsForDevs' private storage, not a contract.
  The owner's machine today is served by rung 1 at once, and by rung 3 once the paired plan ships.
- **The result says which rung answered and what it checked**:
  `CredsLocation(string? Path, string Rung, string Source, IReadOnlyList<(string Place, string Result)> Checked)`.

### 4.3 Three failures, three sentences — `KeyVault.cs:39-52`

**Lookup failure and launch failure are different outcomes** (gate findings 3 and 5):

| Outcome | Sentence (shape) |
|---|---|
| not found | `the creds CLI was not found — checked COAI_CREDS_EXE (unset), PATH (23 directories), C:\Users\…\Programs\creds\creds.exe (absent).` then the remedy (§4.4) |
| explicit path unusable | as rung 1 above; no fallback |
| found, but would not start | `creds was found at <path> (<rung>) but could not be started: <OS error>` — the `Win32Exception` message, never "not installed" |
| found at rung 3 | works; `providers` says `vault: creds found at <path> (not on PATH)`, so the person learns why it worked |

The startup log line (`Program.cs:1724-1726`) carries the rung and the source. No key value is ever logged; the path
is not a secret.

### 4.4 The remedy names more than one road (gate finding 8)

A `coai-mcp` started by Claude Code in a terminal, by Codex, or headless on Linux may have no VS Code at all. The
not-found sentence ends with two lines, chosen by OS:

- **VS Code:** `CredsForDevs → Install… → Install creds → Add to PATH, then restart this MCP client`.
- **Elsewhere:** Windows gets the PowerShell one-liner route (*Copy install command for another machine…*); Linux gets
  `curl … install.sh | sh` (CredsForDevs `install.sh`). macOS has no published build yet (`install.sh` refuses it),
  and the sentence says so rather than offering a command that fails.

In every case: *or set `COAI_CREDS_EXE` / `coai.credsPath` to its full path.*

### 4.5 The setting — `src_vs_code/src/settingsShape.ts`

- **`coai.credsPath`** (string, default empty), wired exactly as `credsKey` is on the extension side:
  `settingsShape.ts:123`, `:307`, `:337`, `:420`, and into `COAI_CREDS_EXE` next to `:565-566`. It therefore reaches
  both the pasted MCP block and `settings.json`, and the server now reads the latter (§4.1). Plus its `package.json`
  contribution beside `coai.credsKey` (`src_vs_code/package.json:352`).
- **Why not `COAI_EXE_CREDS`:** `COAI_EXE_<PROVIDER>` is derived from a *provider id* (`PanelSettings.cs:969`,
  `:981-983`). A provider could be named `creds`, and the vault reader is not a provider. The name pairs with
  `COAI_CREDS_KEY` instead, which is what it travels with.

### 4.6 WSL

`coai-mcp` running **inside** WSL resolves the **Linux** `creds` (rungs 2–3: PATH, then `~/.local/bin/creds`). That
binary relays to Windows through the paired plan's pointer file. Nothing WSL-specific is needed here, and none is
added. The paired plan's probe answers whether a non-login process in the distribution finds it.

## 5. Build order

1. **RED — the live defect:** `ForThisMachine` over a `configuration` whose environment is empty and whose
   `settings.json` carries `COAI_CREDS_KEY` must attempt the read with that key. It fails today; `Program.cs` never
   consults the file for it.
2. **RED:** `creds` exists only at the stable location and PATH is empty; it must be found. Fails today with
   *"not installed"*.
3. **RED:** a missing binary names every place it checked. This replaces the `"not installed"` assertion at
   `KeyVaultTests.cs:101`; its guarantee (*a missing binary is named*) stays and gets sharper.
4. **RED:** a found-but-unlaunchable binary (on POSIX a file without `+x`; on Windows a directory named `creds.exe`)
   is reported as *could not be started*, never *not installed*.
5. `CredsLocator` + `CredsLocatorTests`: every rung, `isWindows` both ways on one runner (the lesson written into
   `ExecutableResolver.cs:48-51`), a `probe` that records every path asked (never `globalStorage`).
6. `KeyVault.ForThisMachine(launcher, configuration)`; both call sites use it. Steps 1–4 go GREEN.
7. `--providers` with no `creds` anywhere: stdout is still valid JSON, the exit code is unchanged, and the sentence is
   on stderr.
8. `coai.credsPath` → `COAI_CREDS_EXE`, and the settings-shape tests that guard the env mapping.
9. Docs:
   - `research/module_server.md` §Configuration and keys (`:576-580`): the new variable, the precedence and the three
     rungs, and the fact that `COAI_CREDS_KEY` is now layered like every other setting;
   - `research/architecture.md:29,34`;
   - a dated note under `research/PLAN_connect_other_ais.md:85`, recording that the "never onto PATH" fact is now
     reconciled, and how;
   - the extension `CHANGELOG` (the panel's key now works without pasting).

## 6. Test plan

| Guarantee | Test | Red today? |
|---|---|---|
| A key set only in `settings.json` reaches the vault read | `KeyVaultTests` over `ForThisMachine` (step 1) | **yes** |
| The environment beats `settings.json`, and `providers` names the winner | `KeyVaultTests` | new |
| Found at the stable location when PATH has nothing | `KeyVaultTests` | yes |
| A missing binary names every rung it checked | `KeyVaultTests` (replaces `:93-102`) | yes |
| Found but unlaunchable → *could not be started* | `KeyVaultTests` | yes |
| An explicit path that is absent or not executable stops there | `CredsLocatorTests` | new |
| PATH beats the stable location; explicit beats both; a non-executable PATH entry is skipped | `CredsLocatorTests` | new |
| Never probes `globalStorage` | `CredsLocatorTests` — a `probe` that records every path asked | new |
| `--providers` keeps its stdout JSON and exit code with `creds` absent | a one-shot test | new |
| `coai.credsPath` reaches `COAI_CREDS_EXE` | the settings-to-env tests beside `settingsShape.ts:565` | new |
| End to end, with the paired plan | owner's machine: `coai providers` reports the vault available, with its rung and source | recorded at promotion |

## 7. Growth budget

None. One setting, one environment variable, no stored state.

## 8. Definition of Done

- [ ] Steps 1–4 watched failing first; the failure messages and the passes reported.
- [ ] The build is clean, the `src_mcp` tests are green, and the extension suite is green.
- [ ] `module_server.md`, `architecture.md`, the `PLAN_connect_other_ais.md` note and the `CHANGELOG` updated.
- [ ] The paired CredsForDevs plan names this one, and the stable-location paths match character for character.
- [ ] A `review_code` round ran on `feat/159-coai-finds-creds`; every finding resolved; verdicts and reviewer counts
      reported.
- [ ] The plan-lifecycle check passes; promoted with deviations when it ships.

## 9. Review record

**Plan gate, 2026-09-26** — session `fde0a94d`, branch `feat/159-coai-finds-creds`, one round, verdict `proceed`,
**all 3 reviewers answered** (codex, gemini, local). 9 findings: **6 accepted, 3 rejected.**

| # | Finding (reviewer) | Decision |
|---|---|---|
| 0 | add `globalStorage` as a fourth rung (local) | rejected — not a contract; varies by build, profile and portable mode; the exclusion is tested and rung 1 covers today's machine |
| 1 | setting vs environment precedence undefined (local) | accepted — §4.1 |
| 2 | `ExecutableResolver` does not walk PATH on Linux (local) | rejected — already §4.2 rung 2 |
| 3 | an existing but non-executable explicit path (local) | accepted — §4.2 / §4.3 |
| 4 | rung order unclear (local) | rejected — the table is the order, stated as fixed and tested |
| 5 | lookup failure and launch failure conflated (codex) | accepted — §4.3 |
| 6 | no executable-bit check on POSIX (gemini) | accepted — §4.2 `probe` |
| 7 | `--providers` contract when `creds` is absent (gemini) | accepted — §4.1, step 7 |
| 8 | the remedy assumes VS Code (gemini) | accepted — §4.4 |

**Own review, 2026-09-26:** **High** — `COAI_CREDS_KEY` (and the proposed `COAI_CREDS_EXE`, wired the same way) is
read from the raw environment, bypassing `SettingsFile.Layer`, so the panel's key never reaches the server. This is a
live defect; it is now §2's last item, §4.1 and build step 1.
