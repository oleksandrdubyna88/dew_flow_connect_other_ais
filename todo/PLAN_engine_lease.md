# PLAN — the card is leased across processes, and a queued reviewer says how long it will wait

> Status: **plan only, nothing implemented yet, 2026-09-03.** Scope:
> `src_mcp/runners/Reviewers/EngineLease.cs` (new), `src_mcp/src/Program.cs` (`--ask-local`),
> `src_mcp/runners/Reviewers/{LocalAsk,LocalRuntime,BoundedScheduler}.cs`,
> `src_mcp/src/Server/{PanelService,LiveRound}.cs`, `src_vs_code/src/rounds.ts`.
>
> Related docs: [module_server.md](../research/module_server.md),
> [PLAN_one_gpu_one_reviewer.md](../research/PLAN_one_gpu_one_reviewer.md).

## The symptom this is the second half of

`mcp-v0.12.4` serialises the reviewers of one server against one engine, and its own record says what
it does not cover: **two MCP clients each running their own `coai-mcp` are not serialised by it.** That
is not a hypothetical here — this machine runs several Claude sessions at once, each starting its own
server, and the measured failure they produce is the one already recorded: three requests on one card
turned a 30-second reviewer into two that were cancelled at 590 s.

There is a second, softer failure beside it. A queued reviewer says `queued` and nothing else, so a
person watching a round has no idea whether the wait is ten seconds or ten minutes — and the server
knows, because every reviewer's duration is now recorded.

## What must be true when this is done

1. **Five concurrent processes asking one local engine do not fail.** Slower is expected and correct;
   a crash, a hang, or a reviewer reported as failed is not.
2. **The lease is held by the process that is actually talking to the engine** — the `--ask-local`
   shim — because that is the only place every local reviewer of every server passes through.
3. **A dead holder does not block the card.** A killed process must not leave a lease nobody can take;
   liveness is proved by pid, the way the round sweep already proves it.
4. **A waiting reviewer says what it is waiting for**, on stderr while it waits and in its failure
   sentence if the wait outlives the deadline — "waited 4m for the engine, then had 6m to think" is a
   different problem from "the engine is slower than the deadline".
5. **A queued reviewer carries an estimate**: how many are ahead of it on that engine and roughly how
   long that is, from the durations already recorded rather than from a guess.
6. **The estimate is honest about being one.** With no history there is no estimate, and the line says
   the number of reviewers ahead without inventing a time.
7. **Nothing changes for a hosted vendor**, and nothing changes on a machine with no local reviewer.

## Constraints

- **No daemon and no service.** The family's `gpu-lease` rule covers a machine-wide lease with a
  daemon; a marketplace extension cannot depend on another repository's process. This is a lock FILE
  under the user's local application data, and it must work on Windows, Linux and macOS.
- `FileStream.Lock` is not portable and `FileShare.None` is not enforced on Unix, so the protocol is
  an atomic `CreateNew` plus a heartbeat plus pid liveness — the classic lock file, not an OS lock.
- The shim is a short-lived process per reviewer, so the lease must be released on every exit path,
  including a kill: a stale lease is recovered by the NEXT waiter rather than by the dead holder.
- The in-process cap from 0.12.4 stays. It is what keeps the queue orderly and observable inside one
  round; the lease is what makes it true across processes.
- The estimate reads history the server already writes (`usage.jsonl` and the per-reviewer durations),
  not a new store.
- A wait must not silently consume the reviewer's whole deadline without saying so.

## Build order

1. **RED first:** a test that starts N shims against a fake engine and asserts they never overlap and
   all exit zero — it fails today, where nothing coordinates them.
2. **`EngineLease`**: `AcquireAsync(engineKey, ct)` → `IAsyncDisposable`. Atomic create, pid +
   started + heartbeat inside, a waiter file per queued process, stale recovery by pid liveness and a
   heartbeat older than the grace period, jittered retry so two waiters do not race the same delete.
3. **The shim takes it** around the HTTP call only — not around the file reads — and reports the wait
   on stderr every thirty seconds.
4. **The failure sentence splits**: waited-for-the-engine and engine-was-slow are two messages, with
   the two numbers in both.
5. **The estimate**: `EngineLease.Ahead(engineKey)` (waiters + holder) and a rolling average per
   engine written by each holder as it releases; `PanelService` puts "about N min, 2 ahead" into the
   queued reviewer's note, and the panel renders a note for a queued reviewer as it already does for a
   failed one.
6. **Measure it**: five parallel `--ask-local` runs against the real engine on this machine, reported
   as a table — start, wait, work, exit code — with the conclusion stated either way.
7. Docs: `module_server.md`, the help article, and the plan promoted with what the measurement showed.

## Test plan

`src_mcp`:

- N leases against one key never overlap, and every one is acquired (no starvation);
- a lease whose holder pid is dead is taken by the next waiter rather than blocking it;
- a lease whose heartbeat is stale but whose pid is alive is NOT stolen — that is a slow generation,
  not a dead process;
- two different engine keys do not wait for each other;
- `Ahead` counts the holder and the waiters, and drops a waiter whose process is gone;
- the estimate is empty with no history and a duration with it.

By hand, on this machine: five concurrent shims against the real Ollama, reported as a table.

## Definition of Done

- [ ] Five parallel calls: all exit zero, none overlap on the card, the table is in the summary.
- [ ] A killed holder is recovered by the next waiter within the grace period.
- [ ] A queued reviewer's note says how many are ahead and roughly how long.
- [ ] The two waiting failures read differently from the slow-engine one.
- [ ] `CoaiMcp.Tests.exe` and `npm test` pass.
- [ ] `module_server.md` and the help article are updated; the plan is promoted.
