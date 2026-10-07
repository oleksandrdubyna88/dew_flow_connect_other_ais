# PLAN — an idle coai-mcp uses no CPU, and answers `initialize` at once

> Status: **built on branch `fix/mcp-idle-cpu-slow-start`, not committed or merged yet, 2026-10-06** (plan gate `proceed`, 1 of 1 reviewer; tests and measurements in [RESULTS_idle_cpu_and_slow_start.md](../research/RESULTS_idle_cpu_and_slow_start.md)). Scope: `src_mcp` — the one-minute sweep's
> escalation retention, the stdio server's start (`Program.ServeAsync`), the background consultants survey; two new
> settings. No wire or file-format change for the extension.
>
> Related docs: [module_server.md](../research/module_server.md), [architecture.md](../research/architecture.md).

## Symptom (measured 2026-10-06, before any change)

**D1 — busy without activity.** In WSL, 7 `coai-mcp` 0.43.0 instances (one stdio server per Claude Code session)
each used 27–54 % of one core for as long as they lived, with no log line for 10+ minutes. Read-only `/proc` sampling
put the time on ONE `.NET TP Worker` thread per process, not on GC. Lifetime CPU / lifetime ≈ 40 % for all seven,
whatever each did. The two Windows 0.41.1 instances, reported "idle", were not: 20 % and 33 % of a core over their
lifetime (`Get-Process` TotalProcessorTime).

**D2 — slow start, restart storm.** `starting: … vault: keys loaded` → `consultants: wrote …` took 19–32 s at ~100 %
of a core. Claude Code gives a server 30 s to connect, then sends SIGTERM and starts another: 34 starts in 10 minutes.
Reproduced with the released 0.43.0 binary against an isolated COPY of the WSL data directory: `initialize` answered
after **43.5 s**, then **> 60 s** (not at all within the harness window) — and after 0.05–0.33 s against an empty
data directory.

## Root causes (each read in the code and observed in a stack)

**D1.** A JIT build of `mcp-v0.43.0`, run on that copy and sampled with `dotnet-stack`, had its one hot thread in:

`ConsultationSweeper.RunAsync` (every `ConsultationSweeper.Every` = 1 min, `ConsultationSweeper.cs:19`) →
`PanelService.SweepEscalations` (`PanelService.cs:1654`) → `EscalationRetention.Sweep` → for EVERY `escalations/*.json`
file, `isHeld(id)` is asked BEFORE the file is judged (`EscalationRetention.cs:60`) → `isHeld` is
`SessionStore.HoldsQuestion` (`PanelService.cs:103`) → which deserialises EVERY `sessions/session-*.json`
(`SessionStore.cs:591-597`) and stops only on a hold.

So one beat costs (escalation files) × (session files) full JSON parses. WSL today: 54 × 435 files (14 MB) ≈ 750 MB
of JSON per minute per instance. Windows: 26 × 639 (24 MB). It grows with use, which is why it "appeared" now: the
retention shipped on 2026-10-02 (a53c1bc1) and is in 0.41.1, 0.42.x and 0.43.0 alike. The 0.42.1 → 0.43.0 diff touches
no startup or sweep code (security-lane files and one one-shot mode only).

**D2.** `initialize` cannot be answered before `server.RunAsync` (`Program.cs:1981`), and before it `ServeAsync` awaits:

1. the vault read, `creds config <key>` with a **30 s** timeout (`Program.cs:1912`, `KeyVault.cs:105`);
2. `new PanelServiceHost(...)` (`Program.cs:1938`), which builds `PanelService`, whose constructor runs every startup
   sweep synchronously (`PanelService.cs:139-193`): orphaned rounds (all sessions), consultations + re-projection
   into SQLite, question consults, **the same escalation sweep as D1**, process tracking.

Under the CPU that seven D1 instances already burn, (2) alone took tens of seconds; (1) can take 30 s by itself. The
background consultants survey (`Program.cs:1978`) is NOT on the `initialize` path — but every start probes four CLIs
and seven sessions restarting at once probe seven times.

## What must be true when it is done

1. An idle sweep beat with nothing due reads **no** session file: the held-question set is computed only when an
   escalation file is actually due for deletion, and **at most once per sweep** — never once per file.
2. A held question is still never deleted, whatever its age (the existing guarantee, `EscalationRetentionTests`).
3. The sweep beat's interval is a setting, `COAI_SWEEP_SECONDS` (default 60, as today), clamped to 10–3 600: zero,
   a negative or an unparsable value falls back to 60, a value outside the range is clamped (plan round 1, codex: a
   zero interval would be a busy loop).
4. `initialize` (and `tools/list`) are answered without waiting for the vault read, the service build or the startup
   sweeps; the first TOOL call waits for them, so a tool still sees a fully built service.
5. A failure of that background start is logged, and every tool call then answers with that error rather than hanging.
6. The background consultants survey is shared across instances on one data directory: at most ONE survey per
   window slot (`COAI_CONSULTANTS_REUSE_SECONDS`, default 300, range 30–86 400) per build version per settings-file
   stamp. The claim is one `FileMode.CreateNew` of a file whose NAME carries all three
   (`consultants.survey.<hash>.claim`), so nothing is ever taken over: a new slot, an upgrade or a settings change is
   a new name, and two starts racing for one name are decided by the create alone (plan round 1, codex: a takeover of a
   stale claim could not be fenced). Residual, stated in the code: two starts straddling a slot boundary can both
   survey — at most two, never seven. A cancelled survey deletes its own claim, so a later start in the slot surveys.
   Claims of other slots are deleted best-effort at each claim.
7. Measured after the fix, on the same data copy: idle CPU ~0 %, `initialize` well under 1 s on an unloaded
   machine and inside the 30 s budget under the WSL load.

## Constraints

- No wire, file-format or settings-file change the extension must learn: `consultants.json` keeps its shape; the two
  settings are environment/settings keys read with `IntVar` like the others (unknown values fall back).
- The one-shot modes and stdout purity are untouched (`.agents/PROJECT.md`).
- No public constructor signature that existing tests use changes except `EscalationRetention`'s held-question input.
- The reload path (`PanelServiceHost.Current` on a settings change) keeps today's behaviour; it gets cheaper through
  D1 and is otherwise out of scope.
- Commit identity is the repository's; no version or tag is moved.

## Design

**D1.** `EscalationRetention(Escalations, Func<IReadOnlySet<string>> heldQuestions, warn)`. `SweepOne` decides first
which files the ending of the question would delete; only when that is non-empty does it ask the sweep's held set,
taken lazily ONCE per `Sweep` call. `SessionStore.HeldQuestionIds()` reads every session once and returns the union of
`HoldQuestions` and `RequestQuestions`. `HoldsQuestion` stays for its other callers (if any; else removed).
`ConsultationSweeper.RunAsync` takes the interval from `PanelSettings.SweepEvery`.

**D2.** `ServeAsync` keeps the cheap synchronous steps (logger, settings layering, startup notes) and moves
"read the vault → log `starting:` → build the host" into ONE background task. Tools and the sweeper reach the
service through `IPanelServiceSource` (implemented by `PanelServiceHost`, and by a `StartingHost` that waits for that
task). The transport starts immediately. The consultants survey keeps running in the background, behind the claim.

## Growth surfaces

- `consultations/health/consultants.survey.<hash>.claim` — empty-ish files (< 100 bytes), one per window slot that
  had a start. Every claim deletes the claims of other slots, so at rest there is one, at a boundary two. A crashed
  survey leaves its claim, which the next slot's claim deletes.

## Build order

1. RED tests (below), observed failing for the real symptom.
2. D1 code; tests green. 3. D2 code; tests green. 4. Survey claim; tests green.
5. Whole suite (Windows, Debug and Release; WSL `nice -n 19`), family checks.
6. Measurements after the fix with the same harness and data copy → `research/RESULTS_idle_cpu_and_slow_start.md`.
7. Docs: `module_server.md`, `architecture.md` where the start sequence is described, `module_tests.md`.

## Test plan

- `EscalationRetentionTests`: an idle sweep over many YOUNG files asks the held set **0** times; a sweep with several
  due files asks it **once**; held stays kept (existing tests, adapted to the set). RED today: asked once per file.
- `SessionStore` held-set test: holds and requests from several sessions, torn file skipped.
- Settings tests: `COAI_SWEEP_SECONDS` / `COAI_CONSULTANTS_REUSE_SECONDS` default, value, invalid.
- A real-binary stdio test: `creds` on the server's PATH is the fake CLI sleeping 20 s and the codex probe sleeps too;
  `initialize` must answer within 10 s; a `providers` call afterwards answers once the vault read ends. RED today.
- Survey claim tests (the claim decision is a pure unit over a directory, a clock and the stamps): same slot → one
  claim wins of many concurrent tries; next slot / other version / settings changed → a new claim; cancelled →
  released; other slots' claims removed. The interval setting: 0, negative, junk, 5, 99 999.
- Break-it: revert each production line the behaviour depends on, watch the matching test go red.

## Deviations, as built (2026-10-06)

What shipped differently from the design above — most of it from the own reviews (one of the plan, two of the code),
which ran beside the gate:

| Planned | Built | Why |
|---|---|---|
| `Func<IReadOnlySet<string>>`, `SessionStore.HeldQuestionIds()` | `Func<HeldQuestions>` — `HeldQuestions(Ids, Complete)` from `SessionStore.HeldQuestions()`, cached by `SessionHolds` behind a stat of the sessions directory | a question held past its seven days is due on EVERY beat, so "once per sweep" alone still re-read every session each minute (own plan review) |
| a torn session file is "skipped" | the set is `Complete = false` and every due card is kept that beat (fail closed) | an unreadable session — torn, or busy under another writer — may hold the id; the old per-id read treated it as holding nothing (own plan review) |
| `PanelSettings.SweepEvery` via `IntVar` | a `ServerPace` record (`PanelSettings.Pace`) in its own file, still read through `IntVar`, then clamped | `PanelSettings.cs` is far past the 800-line ceiling; one line there instead of two properties and two parses |
| a failed start: "every tool call then answers with that error" | the transport closes and the process ends as a crash (exit 70, recorded), exactly as when the start ran before serving; the failure is its own `ServerStartFailed` | a server that cannot build its service is useless, and the old behaviour was the crash; a start failing with an `IOException` would otherwise have been read as "the client closed the connection" and exited 0 (own code review) |
| the start under `stopping` | its own `startStop`, linked to the signal and cancelled when serving ends however it ends; ONE bounded wait of 1 s for the start and the survey together | a client closing stdin sends no signal, and two sequential 5 s waits outlasted the signal's 3 s grace, so the notices would not drain (own code review) |
| tools reach the service synchronously | `IPanelServiceSource.CurrentAsync()`, awaited by every tool | a call during the start must not hold a pool thread that the start itself needs (own plan review) |
| claim identity = version + settings stamp | version, side, settings stamp and the client's `COAI_*` environment, minus `*_KEY`/`*_TOKEN`/`*_SECRET`/`*_PASSWORD` and `COAI_CALLER_*` | two clients with different environments get different surveys; no secret in a hash on disk; a per-run caller variable would make every start its own survey (own code review) |
| claims of other slots deleted at each claim | claims older than two windows deleted at each take | deleting a SAME-slot claim of another identity let a third start of that identity survey again |
| — | `Take` decides by the create alone; writing the claim's id and pruning happen after and can no longer turn our claim into "another server's" | own code review: an `IOException` there matched the "already exists" filter, and nobody would have surveyed for the slot |
| a real-binary stdio test "with the codex probe sleeping too" | the fake CLI copied in as `creds` on the server's PATH and named as the codex executable, both sleeping 20 s | the vault read is what blocked `initialize`; the probe never did, so it alone could not be RED |

Not built, with what was checked: an end-to-end test of a FAILING start through the real binary. Checked: making
`sessions`, `consultations`, `running`, `escalations` or `question-consults` a file instead of a directory — the start
succeeds in every case (each store guards its directory), and no other input makes it throw without a test hook in the
shipped binary, which was not added. The failure road is covered at the `StartingHost` unit
(`AStartThatFailsWithAnIoError_IsReportedAsAFailedStart_NotAsAnIoError`) and the success road end to end.

**Open tail.** A settings write (the panel's provider switch at 16:50Z on 2026-10-06 was one) makes every running server rebuild its `PanelService` on the next call (`PanelServiceHost.Current`), and that constructor still runs the startup sweeps — orphaned rounds over every session file, the consultation re-projection — inside the call, under the host's lock. D1 makes the escalation part cheap; the rest was left as it was (the plan's constraint: the reload path keeps today's behaviour). Measured on 2026-10-07: the provider row itself does not change the start ([RESULTS](../research/RESULTS_idle_cpu_and_slow_start.md), *Was it the codex probe? No*).

## Definition of Done

- [ ] Both RED observations and both GREEN observations recorded.
- [ ] Whole suite green in the shipped configuration; family checks green.
- [ ] Idle CPU and `initialize` latency measured after the fix on the same data copy and recorded with conditions.
- [ ] Module docs and architecture updated; this plan promoted with its deviations.
