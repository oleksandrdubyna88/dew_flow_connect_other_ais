# PLAN — the launcher owns its own deadline

> Status: **IMPLEMENTED, 2026-09-11.** Story 1.1. Scope as built:
> `src_mcp/runners/Processes/ProcessLauncher.cs` and its tests.
>
> Related docs: [module_runners.md](module_runners.md) — *The launcher's two ceilings, and why the
> write is a task*.

## What shipped differently

1. **The ceiling is in CHARACTERS, named `MaxOutputChars`**, not bytes. What is bounded is the memory
   of a .NET string, two bytes per character; 8 Mi characters IS the 16 MiB the plan named, said in the
   unit the code can count without re-encoding every chunk.
2. **The line reader had to go entirely**, which the plan did not foresee. `BeginOutputReadLine`
   delivers nothing until a newline arrives, so a ceiling checked in a callback fires only after the
   allocation it exists to prevent — codex, plan round. Replacing it removed a defect nobody had
   reported: the line reader appended a newline to every line, so **every vendor answer this product
   has ever read carried a line ending the vendor did not write.**
3. **`ProcessResult` gained `Cancelled` and `Truncated`**, both additive. A budget that ran out and a
   caller that withdrew arrive as the same `OperationCanceledException` and mean opposite things.
4. **Closing stdin on the cancellation path is of the HANDLE, not the writer.** `StreamWriter.Close()`
   throws while an async write is in flight, which is the only state it is ever called in — measured on
   the first run of the test that asked for it.
5. **The drain grace cancels rather than abandons.** An abandoned reader faults against the disposed
   process later and can still append to text the caller is already reading.

## The symptom

`ProcessLauncher.RunToCompletionAsync` writes the prompt to the child's stdin FIRST
(`ProcessLauncher.cs:138`) and creates its timeout only afterwards (`:140-144`). The write itself
(`:184`) takes no cancellation token. A pipe holds about 64 KiB; a review prompt is a shaped diff of
up to 192 KiB plus the rules, so every reviewer launch writes past the buffer — and a child that is
not reading its stdin (a sign-in prompt, a TTY check, a hung MCP handshake, a CLI that crashed
before reading) blocks that write for as long as it lives. The timeout has not started; the caller's
token is not consulted; on the Team server the job's own budget is not consulted either, so the
account's lock is held past `RunBudget` and the sweep cannot end it — the runner is inside the
launcher.

The audit measured it: `/bin/sleep 3`, a 1 MiB prompt, a 100 ms timeout and a 100 ms cancellation —
the call returned after **3 021 ms with `TimedOut = false`**. That is the pipe-backpressure case the
audit's own caveat asked for: the write blocked until the child exited on its own and the exit, not
the deadline, ended the call.

Beside it, `:99-102`: stdout and stderr accumulate in `StringBuilder`s with no ceiling. A runaway CLI
is a runaway allocation in whichever process launched it — the Team server under its
`MemoryMax`, or a developer's `coai-mcp` with none.

## The change

1. **One deadline from the start.** The linked, timed token is created BEFORE anything is written;
   the stdin write runs as its own task (`WriteAsync(text.AsMemory(), token)`), and the wait for exit
   is awaited under the same token. On timeout or cancellation the tree is killed exactly as today
   — and that kill is what breaks the pipe, so a write that ignored its token (an anonymous pipe on
   Windows does) still ends with the `IOException` `WriteStdInAsync` already treats as the child's
   decision. The write task is awaited last, after the exit, so nothing is left running when the
   method returns. `TimedOut` keeps its meaning.
2. **`ProcessRequest.MaxOutputBytes`**, default 16 MiB per stream. Past it, further lines are dropped
   and ONE marker line — `[coai: output truncated after N bytes]` — is appended, so a truncated answer
   fails to parse loudly rather than silently. The largest legitimate stream observed is a vendor's
   NDJSON of a long review, well under a megabyte; sixteen is a ceiling on a runaway, not a budget.
3. `module_runners.md`: the launcher's two ceilings, and why the write is a task.

No growth surface — this bounds two.

### What the gate's plan round changed (2026-09-10, accepted)

- **The ceiling must be enforced on BYTES, not on lines** (codex, Major). `OutputDataReceived` delivers a
  LINE, so a child that writes 200 MB with no `\n` has already been buffered by .NET's own reader before
  any callback can count it — the ceiling as first drafted would not have been reached until the
  allocation it exists to prevent had happened. So the launcher stops using `BeginOutputReadLine` and
  reads `StandardOutput`/`StandardError` as character streams into a bounded builder, killing the tree
  when either passes its ceiling. Test 4 becomes a child that writes past the ceiling **with no newline
  at all**, which is the case the line-based version could not fail on.
- **The write task is awaited defensively** (gemini, Blocking). A child that exits without reading leaves
  the write to fail — `IOException`/`EPIPE` on Unix, and on Windows a blocked anonymous-pipe write that
  only ends when the handle closes. So `StandardInput` is CLOSED as soon as the exit is observed, and the
  write task is awaited inside a `catch` for `IOException`, `ObjectDisposedException` and
  `OperationCanceledException` — the same three `WriteStdInAsync` already treats as the child's decision.
  Test 5 gains a child that exits at once while a 1 MiB prompt is being written.

## Test plan

All against the REAL launcher and a real child, because the defect is between the process and the pipe.

| # | Test | Holds |
|---|---|---|
| 1 | `ProcessLauncherTests.AChildThatNeverReadsItsStdinIsStillKilledOnTime` — a child that sleeps (`ping -n 30` · `sleep 30`), a 1 MiB `StdIn`, a 300 ms timeout: returns within seconds, `TimedOut` true, the child gone | RED today: returns when the child exits, ~30 s |
| 2 | `…IsStillCancelledByTheCaller` — same child, the caller's token cancelled at 300 ms | the second clock |
| 3 | `…AChildThatReadsEverythingStillGetsAllOfIt` — a child that echoes stdin (`find /v ""` · `cat`) with 1 MiB in: stdout equals the input | the positive companion: moving the write to a task lost nothing |
| 4 | `…OutputPastTheCeilingIsTruncatedAndSaysSo` — a child printing 1 MiB against a 64 KiB ceiling | the marker, once |
| 5 | The existing `ReviewerExecutorTests` and `ReviewLauncherTests` | every caller of the launcher behaves as before |

## Definition of Done

- [ ] Tests 1–4 written, watched fail for the real symptom, passing; test 5 unedited and green.
- [ ] A non-reading child is ended by the timeout and by the caller's token, on Windows and on Linux.
- [ ] Output is bounded per stream, with the truncation named in the stream itself.
- [ ] `module_runners.md` updated.
