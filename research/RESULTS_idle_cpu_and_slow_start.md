# RESULTS — an idle coai-mcp burned a core, and a slow start became a restart storm (2026-10-06)

> Measured 2026-10-06 on the owner's machine (Windows 11, 24 logical cores; WSL Ubuntu on the same host). Subject:
> `mcp-v0.43.0` (e193822a) against the fix on branch `fix/mcp-idle-cpu-slow-start`, based on a3ac5b01 and not yet
> committed when measured. Its source is pinned by content instead: git tree `0e8ed071` for `src_mcp` (from
> `git write-tree` over a temporary index, 2026-10-07); the builds measured were published from that source before one
> comment in `Program.cs` was re-wrapped — no code differs. The commit sha is added here when the branch is committed.
> Harness of every after-fix number:
> [`scripts/measure-idle-server.mjs`](../scripts/measure-idle-server.mjs). The diagnosis also used read-only `/proc`
> sampling and `dotnet-stack`, and exploratory runs with a Python twin of the harness kept in the session's scratch
> directory and NOT committed — those rows are labelled. Plan:
> [PLAN_an_idle_server_is_idle_and_starts_at_once.md](../todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md).

## What was reported

- **D1.** Seven WSL `coai-mcp` 0.43.0 instances (one stdio server per Claude Code session) each used 27–54 % of one
  core continuously, while their logs showed nothing for 10+ minutes.
- **D2.** Claude Code's 30 s connect timeout sent SIGTERM and started another: 34 starts in 10 minutes,
  `MCP server coai connection timed out after 30000ms` in many sessions. The KILLED starts logged `starting:` and then
  nothing until the SIGTERM — they never reached `consultants: wrote`. The 19–32 s from `starting:` to
  `consultants: wrote` was measured on the starts that SURVIVED (16:58–17:02Z); the qwen-era starts earlier that day
  took 3–17 s for the same span (*Was it the codex probe? No*, below).

## What was observed on the live instances (read-only)

Only `/proc` and `Get-Process` were read; no running instance was attached to, signalled or killed.

| Observation | Value |
|---|---|
| Per-thread CPU over 20 s, all seven WSL instances | 1 194–1 661 ticks (≈ 60–83 % of a core at that moment), **all of it on one `.NET TP Worker` thread**; GC threads ≈ 0 |
| Lifetime CPU / lifetime, all seven | 38–43 %, whatever each instance had done |
| The two Windows 0.41.1 instances, reported "idle at 0 %" | **20 % and 33 %** of a core over their lifetime (804 s and 519 s of CPU) — the defect is on both sides and older than 0.43.0 |
| WSL data directory | 435 `session-*.json` (14 MB), 54 escalation `*.json`, 154 consultation files |
| Windows data directory | 639 sessions (24 MB), 26 escalation `*.json` |
| stdio of a Claude Code child | sockets (`socket:[…]`), and `MCP_CONNECTION_NONBLOCKING` in its environment |

The non-blocking socket was ruled out by experiment, not by argument: a socketpair set non-blocking makes the server
read `EAGAIN` and end at once ("the MCP client closed the connection"), which is not what live instances do; a
blocking socket and a pipe both idled at 0 % on an empty data directory.

## Reproduction (exploratory — the Python twin, socketpair stdio as Claude Code gives it, WSL load 55–95)

The released 0.43.0 binary, copied, started with an isolated `COAI_DATA_DIR`:

| Data directory | `initialize` | idle CPU |
|---|---|---|
| empty | 0.05–0.33 s | 0.0 % |
| `settings.json` only (the real one) | 0.08–0.33 s | 0.0 % |
| a COPY of the whole WSL data directory | **43.5 s**, then **> 60 s** | **52.8 %** (first run) |

Bisecting the copy by directory (`sessions`+`coai.db`+`usage.jsonl` against the rest) reproduced neither half: the
cost needed BOTH sessions and escalation cards. A JIT build of `mcp-v0.43.0` on the full copy, sampled with
`dotnet-stack report` while idle, had its one busy thread here:

```
SessionStore.TryRead
Enumerable.Any
SessionStore.HoldsQuestion
EscalationRetention+<>c__DisplayClass7_0.<Sweep>b__0
Enumerable.Sum
EscalationRetention.Sweep
PanelService.SweepEscalations
ConsultationSweeper.Swept
ConsultationSweeper+<RunAsync>d__1.MoveNext
TimerQueueTimer.Fire
```

## Root causes (each checked in the code)

**D1.** `ConsultationSweeper` beats every minute (`ConsultationSweeper.cs:19` at 0.43.0) → `PanelService.SweepEscalations`
(`PanelService.cs:1654`) → `EscalationRetention.Sweep` asked `isHeld(id)` for EVERY card, before judging whether the
card was due (`EscalationRetention.cs:60`) → `isHeld` was `SessionStore.HoldsQuestion` (`PanelService.cs:103`), which
deserialised every session file and stopped only on a hold (`SessionStore.cs:591-597`). One beat = (cards × sessions)
parses: 54 × 435 files (14 MB) ≈ **750 MB of JSON per minute per server** in WSL. It shipped on 2026-10-02 (a53c1bc1)
and is in 0.41.1, 0.42.x and 0.43.0; it grew with the data, which is why it surfaced now.

**D2.** Before `server.RunAsync` (`Program.cs:1981` at 0.43.0) the start awaited the vault read (`creds config` with a
30 s timeout, `Program.cs:1912`, `KeyVault.cs:105`) and `new PanelServiceHost` (`Program.cs:1938`), whose
`PanelService` constructor runs every startup sweep (`PanelService.cs:139-193`) — **including the same escalation sweep
as D1**. With seven D1 instances already burning CPU, a start took 19–32 s in the logs and 30–62 s when reproduced on a copy of the data. The 0.42.1 → 0.43.0 diff touches no startup
or sweep code (security-lane files and one one-shot mode); the version change mattered only because the extension's
auto-update restarted every session at once.

## After the fix — same snapshot, both builds, the committed harness

One snapshot of the WSL data directory (438 sessions, 54 escalation cards) and the `COAI_*` environment of a live
instance; each run copies it to a fresh temp directory, so both arms start from the same bytes. Settle, then an idle
window in which nothing is asked of the server. Arms alternate.

**Windows, Native AOT** (`win-x64` publish of each build), the snapshot copied to Windows with its vault key removed so
the Windows side never asked the vault with the WSL key; settle 20 s, idle 130 s; the machine otherwise lightly loaded:

| Build | `initialize` | idle CPU (% of one core) | CPU seconds in the window |
|---|---|---|---|
| 0.43.0 | 24.2 · 27.8 · 25.9 s | 24.0 · 22.8 · 24.8 | 31.3 · 29.8 · 32.6 |
| fix | 0.51 · 0.09 · 0.16 s | **0.07 · 0.10 · 0.08** | 0.09 · 0.13 · 0.11 |

**WSL, JIT builds** (framework-dependent `linux-x64` publish of each — a Native AOT build for Linux could not be made
on the Windows side), `nice -n 19`, pipe stdio, load average 7–24 on 24 cores at the time; settle 30 s, idle 120 s:

| Build | `initialize` | idle CPU (% of one core) |
|---|---|---|
| 0.43.0 | 15.6 · 8.9 s | 36.0 · 18.1 |
| fix | 2.0 · 1.6 s | **0.48 · 0.50** |

The fix's `initialize` there is the JIT runtime starting at `nice 19`: earlier the same day, at load 55–95, BOTH builds
took 5–9 s to answer with an EMPTY data directory (exploratory, Python twin), and the old build on the full data did not
answer within 60 s.

**Prediction, recorded before the after-runs:** old ≈ 40 % idle and ≥ 30 s to `initialize` under load; fix < 2 % idle
and < 2 s. Observed: old 18–36 % idle and 9–28 s (and > 60 s at the higher load of the exploratory runs); fix
0.07–0.50 % idle and 0.09–2.0 s. The old build's `initialize` came in under the predicted 30 s on the lightly loaded
runs — the prediction was about load, and these runs had little.

## Was it the codex probe? No (measured 2026-10-07)

A second reading of the logs found that the first slow starts (16:50Z and 16:52Z, killed at 30–32 s) PREDATE the
0.43.0 binary (written 16:54Z) and coincide with the provider row changing from a qwen row to `codex` — every earlier
start that day logged `qwen enabled`. The hypothesis: probing the Node-based codex CLI made the start slow. The owner's
logs for 2026-10-06, read per start: `starting:` came 0–5 s after the process started in EVERY era (so the vault read
was quick); qwen-era servers wrote `consultants:` after 3–17 s and lived for hours; from 16:50 the starts went silent
after `starting:` until the client's kill.

Code, per release: the consultants survey is started fire-and-forget just before `server.RunAsync` in 0.42.1 and
0.43.0, and **0.41.1 has no survey at all** — in none of them is the probe in front of `initialize`.

Measurement (exploratory: a Python twin of the harness in the session's scratch directory, not committed; WSL,
`nice -n 19`, socketpair stdio, load 9→31). The RELEASED linux-x64 binaries of 0.41.1, 0.42.1 and 0.43.0 (checksums
verified), the live settings with the provider rows replaced by either ONE qwen row (an API row, nothing to launch, the
consultants routed to it) or ONE codex row (the consultants routed to it), on an empty data directory and on a fresh
snapshot of the live one (446 sessions, 54 escalation cards); two runs each:

| Data | Release | `initialize`, qwen row | `initialize`, codex row | probe done after `initialize` |
|---|---|---|---|---|
| empty | 0.41.1 | 0.6 · 1.6 s | 0.6 · 0.7 s | (no probe) |
| empty | 0.42.1 | 2.1 · 3.9 s | 0.5 · 2.0 s | +0.1–0.2 s |
| empty | 0.43.0 | 0.4 · 0.9 s | 0.9 · 1.4 s | +0.0–0.3 s |
| snapshot | 0.41.1 | 12.1 · 30.4 s | 13.3 · 30.7 s | (no probe) |
| snapshot | 0.42.1 | 30.5 · 16.0 s | 21.1 · 21.4 s | +0.1–0.4 s |
| snapshot | 0.43.0 | 50.2 · 12.0 s | 55.9 · 7.8 s | +0.1–0.6 s |

On the snapshot the process itself spent 7–30 s of CPU before it answered, in every cell. The bare probes at that load:
`codex --version` 0.10–0.48 s, `agy --version` 1.4–1.7 s.

**Conclusion, no wider than these runs:** the provider row does not decide the start; the DATA does, in every release
including the one without a probe — it is the startup sweeps of D2 (and the CPU the D1 beats of the other instances
take). What the 16:50 coincidence most plausibly was, and was NOT measured: a settings write (the provider switch)
makes every running server rebuild its service on the next call (`settings reloaded` — the same constructor and its
sweeps, inside the call), and a burst of restarts on a machine those servers already saturated. Nine live 0.43.0
instances measured the next morning still sat at 10–14 % of a core each, at a load of about 10.

## What this does not settle

- The WSL numbers are JIT, not the AOT binary that ships; the Windows AOT numbers are the shipped form on another OS.
- The WSL after-runs had a light load (7–24); the storm itself happened at 55–95, where only exploratory runs exist.
- One machine, one data directory. A data directory with thousands of sessions will make the STARTUP sweep (now in the
  background) longer; it no longer blocks `initialize`, but the first tool call waits for it.
- The other per-beat work — the consultations sweep (≈150 records), the question-consult sweep (which reads its
  records twice) — was not optimised: with it, the measured idle cost is the 0.07–0.50 % above.
- Seven simultaneous starts were not reproduced as a storm; the survey claim's one-per-window behaviour is shown by
  `ConsultantsSurveyClaimTests` (16 parallel takes, one winner), not by a live storm.

## For wsl_care — how to see "busy without activity"

Per MCP server process (`coai-mcp`, one per editor session). Every signal is read from `/proc` and the server's own log;
nothing attaches to the process.

1. **Idle CPU, over a window.** Read `utime`+`stime` (fields 14 and 15) of `/proc/<pid>/stat` twice, W seconds apart,
   divide the tick difference by `CLK_TCK × W`, and compare with the same window's log: above ~2 % of a core while the
   log gained no line in that window is "busy without activity". A window of 60 s or more covers one sweep beat
   (`COAI_SWEEP_SECONDS`, 60 by default). Do NOT use the lifetime average `(utime+stime)/elapsed` for this — it stays
   high long after a busy period ends; it is useful only as a second, slower signal. Before the fix every instance sat
   at 20–54 % with no log line for 10+ minutes.
2. **The hot thread.** Per thread, the same two fields of `/proc/<pid>/task/<tid>/stat`, over the same window; the
   thread's NAME is `/proc/<pid>/task/<tid>/comm` (a name only — it holds no counters). The defect's shape was one
   `.NET TP Worker` thread holding nearly all of the window's ticks, the GC threads near zero. Pool threads come and go,
   so read the task list at both ends of the window and compare only the threads present in both.
3. **Start latency.** What the client waits for is the answer to `initialize`, and the server does not log it. From
   the server side, two proxies: the log file's name is the process start (`logs/<UTC day>/coai-mcp-<HH-mm-ss>-<pid>.log`);
   before the fix, a start that had not logged `consultants: wrote` (0.42.x/0.43.0) or any line after `starting:` by
   ~30 s was one the client was about to kill. After the fix `starting:` and `consultants:` come from background work
   and say nothing about `initialize`; there, use the client's side — Claude Code's
   `MCP server coai connection timed out after …` — or signal 4.
4. **Restart churn.** A log whose only lines are `starting:` and `SIGTERM asked this server to stop` about 30 s after the
   file's start time is a client timeout kill; count them per 10 minutes — 34 was the storm. A `SIGINT` a few seconds
   after the start is an ordinary editor stop, not churn.
5. **Count.** Instances per data directory (`COAI_DATA_DIR`, or the default `~/.local/share/coai-mcp`); seven to nine
   were normal for this owner. Several servers each above the idle threshold at once is the case that saturated WSL.

## Reproduce

```bash
# a COPY of a data directory (never point a measured server at the live one), and a live instance's COAI_* lines
node scripts/measure-idle-server.mjs <coai-mcp binary> <snapshot dir> --env <env file> --settle 20 --idle 130
```
