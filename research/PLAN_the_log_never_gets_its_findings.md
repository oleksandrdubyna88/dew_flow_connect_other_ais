# PLAN — the rounds log shows no findings, because a fan-out copies an array per line

> Status: **IMPLEMENTED, 2026-09-09.** Scope: `src_vs_code/src/processLauncher.ts`
> (`replayingFan`, the line splitter), `versionProbe.ts` (`capture`), and the tests for both.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_rounds_log_view.md](PLAN_rounds_log_view.md).

## The symptom

The operator, 2026-09-09, of the Review rounds page:

> the logs have not changed, there is no accepted/rejected, there is no list of what was found, and
> the day/week/month buttons on the chart still do not switch

*(translated from the operator's Russian; this repository's documentation is English.)*

Three complaints, **one cause**, measured end to end on the real installation.

## What is actually happening

The data is all there. `coai-mcp --log --limit 300`, run by hand, answers in **368 ms** with 4.9 MB:
233 rounds, 222 carrying findings, 232 carrying decisions. 207 of them join a session round by
`roundKeyOf`, so the panel would have something to show for the great majority of its rows.

The page can show it too: `foundHtml` renders every finding with its severity, its file and whether
it was accepted or rejected, and `decided(row)` puts the counts in the Status cell. Neither has ever
had anything to draw.

Driving the **compiled extension code** against the real data:

```
readLog gave rounds: 0          ← the same binary that answers 233 by hand
rows produced      : 378
rows carrying found: 0
rows with decided  : 0
```

`capture` is what answers nothing:

```
capture exit code : -1
capture chars     : 0
```

And the launcher underneath it is fine — it delivers everything:

```
0.15s   first stdout chunk
19.84s  onExit fired, code 0 | chars 4923815
```

**All 4.9 MB arrive in a moment; the exit event arrives 19.7 seconds later.** `capture`'s cap is
8 seconds, so it fires first — and its timeout path answers `(-1, '')`, throwing away the 4.9 MB it
is already holding. `readLog` maps a non-zero code to `EMPTY_LOG`, and the page draws a log with no
findings and no decisions.

### Why the exit is 19.7 seconds late

`replayingFan`, in `processLauncher.ts`:

```ts
emit(value: T): void {
  if (!opened) {
    held = [...held, value];   // the whole array, copied, per event
    return;
  }
```

While nobody has subscribed, every emitted value copies the entire held array — **O(n²)**. Every
launched process feeds a line splitter whose lines go to the `lines` fan, and `capture` subscribes
only to `onStdout`, `onExit` and `onError` — never `onLine`. So for `--log`, ~130 000 lines are each
appended by copying a list that is already 130 000 long. That is the nineteen seconds; `close` cannot
fire until the last `data` handler returns.

### And the third complaint is the same thing

Clicking Today/Week/Month/Year posts `usageWindow`, which reaches `refreshRoundsLog` — and that
`await`s `panel.roundsLog()` **before pushing anything**. Every click that misses the ten-second
cache waits eight seconds for the timeout before the page is told anything at all. The button is not
broken; it is behind the same stall.

## What must be true when it is done

1. A process that writes many lines does not cost quadratic time in the launcher, whether or not
   anybody has subscribed to its lines.
2. `capture` answers with the output it collected when its deadline passes. A slow success must not
   be indistinguishable from a spawn failure, which is what `(-1, '')` makes it.
3. The two failures are still told apart: a spawn that never started and a call that ran out of time
   are different things, and a caller must be able to say which.
4. `readLog` returns the rounds for a database of this size, within its own budget.
5. The Review rounds page shows, for a round the database knows: the accepted/rejected counts in the
   Status cell, and the findings under the reviewers.
6. The usage window buttons answer without waiting on the database read.

## The change

- **`processLauncher.ts` — `replayingFan` holds in O(1).** An internal array appended with `push`
  rather than rebuilt with a spread. The values it hands out stay copies; what changes is that the
  buffer is not rebuilt per event. This is the immutability rule applied where it costs a quadratic:
  `coding-style.md` forbids mutating what a caller can see, and this array is private to the closure.
- **`processLauncher.ts` — nothing is split into lines until somebody asks for them.** `capture` and
  every other stdout-only caller then pay nothing for a facility they do not use.
- **`versionProbe.ts` — `capture`'s deadline answers with what it has**, and distinguishes "timed
  out with output" from "never started". The version probe's own callers are unaffected: a banner
  that has not arrived in the cap is still an empty answer.
- **`extension.ts` — the usage window does not wait on the database.** The push that answers a button
  is the one the person is waiting for; the findings can arrive on the tick behind it.

## Constraints

- **No change to what the page renders.** `foundHtml` and `decided` are correct and have been
  waiting for data; this plan delivers it and touches neither.
- **No change to `coai-mcp`.** The server answers correctly and quickly.
- The launcher is shared by the chat sessions, the vendor probes and the installer. Its behaviour for
  every existing caller must be unchanged apart from being faster.
- `replayingFan`'s replay contract stays: a listener that subscribes late still receives everything
  emitted before it.

## Test plan

- **RED first:** a launcher test that a child writing 50 000 lines with no `onLine` subscriber
  completes within a second or two. It fails today, and the failure is the wall-clock time.
- `capture` with a deadline shorter than a child that has already written output answers with that
  output, and says it timed out.
- A spawn that never starts still answers empty, and says that instead.
- The replay contract: values emitted before the first `on` are still delivered to it, in order.
- Both suites in full.

## Definition of Done

- [x] A many-line process is linear in the launcher. **130 000 emits: 64.09 s before, 0.01 s after.**
- [ ] ~~A timed-out capture carries its output~~ — **withdrawn by the plan round**, and deliberately not built: truncated JSON either throws in `parseLog` or parses as half a record, and `readLog` discards a non-zero code regardless. See *Deviations*.
- [x] `readLog` against the real database returns its rounds — measured: **0.20 s, 235 rounds** against 0 before.
- [x] The page shows the counts and the findings — **212 of 380 rows** carry both, where all had carried neither.
- [x] Documentation: `module_extension.md`, this plan promoted, `research/README.md`, CHANGELOG, version 0.31.16.
- [x] The suite green: **1003 tests, 1002 pass, 0 fail, 1 skipped.**

## It is a regression, and it is datable to the minute

The operator said the page worked yesterday morning, and it did. Two commits the same evening:

| when | commit | what it did |
|---|---|---|
| 2026-09-08 18:15 | `bd26390` refactor(extension): one spawn site | routed `capture` — and so `readLog` — through the shared launcher for the first time |
| 2026-09-08 18:29 | `4b86945` fix(chat): what twelve reviewers found in the launcher | gave `replayingFan` its `held = [...held, value]` |

Before 18:15 `capture` spawned its own child, with no line splitter and no fan, and the log page was
fine. Neither commit is wrong on its own — consolidating on one launcher is right, and an immutable
append reads like the house style. Together they put a quadratic on a path that carries 4.9 MB.

## What shipped differently — three quarters of the plan was dropped

The plan proposed four changes. **One shipped**, and the other three were withdrawn during the plan
round, which is the round doing its job.

The measurement that decided it: 130 000 emits cost **64.09 s** through `held = [...held, value]` and
**0.01 s** through `held.push(value)`. With the copy gone, `close` arrives promptly and everything
downstream follows — so:

- **`capture` is unchanged.** The plan had its deadline answer with whatever output it held. A
  reviewer pointed out that truncated JSON either throws in `parseLog` or, worse, parses as half a
  record — and that `readLog` maps any non-zero code to `EMPTY_LOG` anyway, so the change would have
  fixed nothing while adding a way to accept corrupt data. `(-1, '')` remains the one honest answer
  for "no complete result".
- **Line splitting is not made lazy.** Deferring the split until a subscriber arrives either loses
  the chunks that came first or needs a second buffer carrying chunk-boundary state. At 0.01 s there
  is nothing to defer.
- **The usage window is not decoupled from the database read.** It only looked like a separate defect
  because the read took eight seconds; at 0.22 s the button is answered by the refresh that already
  exists, in order, with no second path to race.

Measured after, against the real installation: `readLog` **0.22 s**, 234 rounds, and **211 of 379
rows** carrying both their findings and their accepted/rejected counts — where every one of them had
carried neither.

## The open tail

- **`capture`'s deadline does not kill the child.** When it fires, the promise resolves and the
  process is left running with its listeners attached. It fires far less often now — this call no
  longer reaches it at all — but a genuinely slow child still leaks. Raised on the plan round and
  deliberately left out of a one-line fix; it is a question about the launcher's process lifetime.
- The `lines` fan holds what nobody reads: 130 000 lines cost **12.5 MB** for a 4.7 MB payload, freed
  when the handle is. Bounded by one short-lived child, so not a leak — but if a caller ever holds a
  long-running child whose lines nobody reads, that is where to look.
