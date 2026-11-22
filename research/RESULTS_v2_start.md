# RESULTS — cold start of the extension: v1 baseline and v2 shell (v2 epoch 1, E2, story S2)

> What this file is: the one place where the cold-start numbers of the ConnectOtherAIs extension are written down —
> the v1 baseline (commit `c24e5141`, the last one before the thin shell) and every later measurement of the shell.
> Design: [`v2/architecture/extension.md`](../v2/architecture/extension.md) § 2 "Measurement" and § 10 row 3.
> Harness: [`src_vs_code/scripts/run-host.mjs`](../src_vs_code/scripts/run-host.mjs) (`measure`),
> [`measureScenario.ts`](../src_vs_code/src/test/host/measureScenario.ts), the rules in
> [`coldstart-lib.mjs`](../src_vs_code/scripts/coldstart-lib.mjs), the marks in
> [`shell/measure.ts`](../src_vs_code/shell/measure.ts).
>
> **State when this was written (2026-11-22): the table at the end is EMPTY.** The harness was written on a machine
> that cannot start VS Code (no `libnspr4`, no sudo), so no number below has been measured yet. Every row comes from
> a run in CI or on the owner's machine; nothing here is a prediction written as a result.

## 1. What is measured

| Name | From | To | Limit |
|---|---|---|---|
| **M1** | module evaluation of `dist/extension.js` (mark `eval`, the first statement of the bundle) | `activate` returns (mark `activateReturn`) | at most 50 ms in every sample |
| **M2** | the driver's clock, started just before it runs `workbench.view.extension.coai` | the sidebar webview's `ready` acknowledgement reaches the host (mark `ready`) | at most **2000 ms**; the **maximum** of the samples counts |
| **M3** | the same clock | the first state handed to the sidebar webview (mark `firstData`) | none: recorded only |

Marks are wall-clock milliseconds (`performance.timeOrigin + performance.now()`) made in the extension host; the driver reads
its clock the same way in the same host, so the differences are exact to a fraction of a millisecond. The marks are readable
through the command `coai.__measure`, which exists **only** when the environment variable `COAI_MEASURE=1` is set, in every
extension mode (so a VSIX installed in Production can be measured). Its registration is part of `activate`'s registrations and
does nothing else; the entry firewall and the 16 KiB budget still hold.

Rules that make a sample count (all in `coldstart-lib.mjs`, all unit-tested):

- Every sample is its **own launch** of the editor with a **fresh** user-data directory, extensions directory, workspace and
  extension data directory, so restored view state cannot start the extension before the clock.
- The extension must **not be active** when the clock starts (`isActive === false`, asserted in the scenario and checked again
  from the record).
- A **fresh** `ready` is one marked at or after the clock. A missing acknowledgement fails the launch; it is never read as zero.
- Three samples; a launch that fails or leaves no record is a failed sample, not a smaller measurement.
- The `mark` of a sample is the first one after the clock; M1 comes from the first `eval` / `activateReturn` pair.

What a "ready" is: the sidebar's script posts `{ type: 'ready' }` as the last statement of its page script, after the page is built and
every handler is bound (a unit test runs the page script and asserts it, `theSidebarSaysItIsReady.test.ts`). The host marks `ready`
when that message arrives from the sidebar; that is the moment M2 ends. For the legacy sidebar this post already existed (the busy
mark's handshake), so no page change was needed for S2.

## 2. How to run it

**Linux and Windows CI** run it on every pull request (`.github/workflows/ci.yml`, job `measure`, required): Linux under `xvfb-run`,
Windows with no display. The numbers are in the job log and in the `cold-start-<os>` artifact. Locally, on a machine where a VS
Code starts (needs the libraries VS Code needs; under WSL `libnspr4` and a display or `xvfb-run`):

```
cd src_vs_code
npm ci
xvfb-run -a npm run measure:host -- --json /tmp/cold-start.json --row --label "my machine, ubuntu 24.04"
```

`--row` appends one row to the table below (date, commit, label, place, platform, M1 max, M2 and M3 min / median / max, verdict);
`--samples N` changes the number of launches; the exit code is 0 for a pass and 1 for a failure.

**The v1 baseline (task t0).** Version 1 has no marks, so the harness is laid over the old commit. In GitHub: Actions → `ci` →
*Run workflow* → `measure_ref` = `c24e5141` (job `measure-ref`; only runs on a manual dispatch). It checks out the ref beside the
current tree, runs `node src_vs_code/scripts/coldstart-overlay.mjs measure-target` (copies the launcher, the scenario and the recorder
over the old tree and applies `scripts/measure-v1.patch`: the old extension gets the recorder, `coai.__measure`, `activateReturn`, the
sidebar's `ready` and its first data, and nothing else), compiles it and measures it with `--record-only` — no limits, the extension
may already be active, the result says so. The log has the row; copy it into the table (label `v1 baseline …`). The same steps by hand:

```
git worktree add ../coai-v1 c24e5141
node src_vs_code/scripts/coldstart-overlay.mjs ../coai-v1
cd ../coai-v1/src_vs_code && npm ci && npm run compile
xvfb-run -a node scripts/run-host.mjs measure --record-only --json /tmp/v1.json --label "v1 baseline, my machine"
```

Done for t0 when the table has a v1 row from **Linux with three samples**.

**Remote-WSL (task t3), on the owner's Windows machine, by hand.** CI cannot reach it. From a PowerShell in `src_vs_code`
(after `npm ci`, with `code` and `node` on PATH, the *WSL* extension installed, and a VSIX from `npm run package`):

```
.\scripts\measure-remote-wsl.ps1 -Distro Ubuntu -Vsix ..\connect-other-ais-<version>.vsix -OutDir C:\measure -StopServers -AppendRow -Label "remote-wsl, Windows 11, Ubuntu 24.04"
```

It builds a tiny driver VSIX (`scripts/measure-driver`), then for each of three samples makes a fresh `--user-data-dir`, a fresh
workspace folder and extension data directory inside the distro, installs both VSIX with `code --remote wsl+<distro>`, launches the
window with `COAI_MEASURE=1` (passed through `WSLENV`), lets the driver run the same steps as `measureScenario.ts` inside the remote
extension host, reads its record back over `\\wsl.localhost`, and applies the same rule. The result names the extension-host location:
`location` is `wsl` when the extension host ran in WSL (the script fails otherwise), and every sample records the platform and the
path the extension was loaded from. **Read the notes at the top of the script before the first run:** the Windows `code` takes
`--extensions-dir` for its own side only (the VSIX are installed into the distro's own `~/.vscode-server/extensions` and removed after),
and `COAI_MEASURE=1` reaches a remote server only if the server starts after the variable is set (`-StopServers` ends running VS Code
servers in that distro). The script was written without being run; its driver and the verdict are unit-tested, its install and launch
steps are not, and the first run may need a correction. Say so in the row's label if it did.

## 3. Prediction, written before any run (`common.measurement` § 4)

- **v2 shell, Linux CI:** M1 well under 50 ms (the entry registers 37 commands, one view provider, one serializer and one URI handler,
  and reads one environment variable); M2 under 2000 ms in all three samples. In S2 the sidebar is still the **legacy** panel behind the
  shell, so M2 still includes loading the 4.3 MB `legacy` module and running the old `activate()` once: M2 should therefore be close
  to the v1 sidebar's own time, **not** the final v2 figure. The reason to measure now is to have the harness proven and a first row; the
  later stories (the sidebar on the kit, E2 S4 and after) are what is expected to bring M2 down.
- **v1 baseline:** M2 larger than v2's by about the cost of what v1 does at start-up (watchers, the settings mirror, the `--features`
  spawn); M1 has no limit and is recorded only as a pair of marks around the old `activate()`.
- If a v2 row shows M2 *above* the v1 row, the shell has made the start slower and that is a finding, not a harness note.

## 4. Conditions, and what a row does not settle (`common.measurement` § 3)

- A row licenses a sentence about **one machine, one VS Code build, three cold launches, headless** (Linux under `xvfb`) — not about a
  person's laptop with other extensions and a warm disk. The editor is the one `@vscode/test-electron` downloads (the latest stable on
  the run's day); the version is in the JSON (`vscodeVersion`). The launch passes `--disable-extensions`, so no other extension competes.
- **The clock starts in the extension host as soon as the test runner is loaded**, while the workbench may still be settling; a row can
  therefore read a little high compared with a person pressing the icon after the window has been idle. The Remote-WSL driver waits for
  `onStartupFinished` and 1.5 s before its clock, so its numbers and CI's are not the same experiment; do not subtract one from the other.
- **v1 activates itself at start-up** (`onStartupFinished`), so its sample is usually already active when the clock starts: v1's M2 is
  "command → ready ack with the extension already running" or, when activation was still pending, includes it — the result's
  `startedActive` says which, per sample. That is the honest meaning of "same harness", and it makes the v1 figure a **lower bound** of what
  a cold v1 user waits.
- M3 is recorded for the sidebar's *first state handed to the webview* (the old panel builds its whole page at once, so M3 can come before
  M2 on the legacy sidebar; it is not a promise of ordering).
- Host launches could not be run where this was written (no `libnspr4`, no sudo); CI's `measure` job is the first run of the scenario
  against a real editor, and a first red there that names the scenario or the launcher is a bug in this harness to fix, not a regression.

## 5. Results

One row per measurement, newest last. Rows are appended by `--row` (and by hand for the v1 baseline); the table must stay the **last**
thing in this file so an appended line lands in it.

| date | commit | label | host location | platform | M1 max ms | M2 min / median / max ms | M3 min / median / max ms | verdict |
|---|---|---|---|---|---|---|---|---|
