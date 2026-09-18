# PLAN — the settings mirror says when it did not write, and keeps trying

> Status: **IMPLEMENTED, 2026-09-18.** Scope: the `mirrorSettings` driver in `extension.ts`, the new
> `mirrorSchedule.ts` it drives, and their tests.
>
> **Deviations: one, and it moved the change out of `serverSettingsSync.ts` entirely.** The plan was
> written against that file - "`reportedFor` becomes a per-condition record" - and the schedule it
> describes turned out to belong to the DRIVER rather than to the sync: `sync()` answers an outcome
> and knows nothing about attempts, while `mirrorSettings` is the thing that owns the timer. So
> `serverSettingsSync.ts` is untouched and the retry, the once-per-condition rule and the report live
> in a new `mirrorSchedule.ts`, which imports no `vscode` and is therefore RUN by its seven tests
> rather than read - the strongest consequence of the move, and not one the plan predicted.
>
> The per-condition record the plan asked for is a `Set<Retryable>` on the schedule rather than a
> field on the sync; the stand-down keeps its own once-per-VERSION guard exactly where it was, which
> is what keeps the two from sharing a counter.
>
> Defect 1 of the four in
> [PLAN_every_message_is_written_down.md](../todo/PLAN_every_message_is_written_down.md) (its S7).
>
> Related: [module_extension.md](module_extension.md).

## Who builds what

| Slice | Owner | Order |
|---|---|---|
| The ledger, the funnel, the panel count, the notifications page, the write gap | `PLAN_every_message_is_written_down.md`, S1–S5 | shipped 2026-09-17 |
| The rounds log's derived sections and its search | `PLAN_the_rounds_log_in_line.md`, S6 | shipped 2026-09-17 |
| The Help tab's diagnosed refusal — defect 3 | `PLAN_the_help_tab_diagnoses_its_refusal.md` | shipped 2026-09-18 |
| The settings mirror's silence — defect 1 | this plan | shipped 2026-09-18 |
| **A role deleted before the deletion has landed — defect 2** | **the parent plan, S7** | **next, and larger** |
| The server half and defect 4 | the parent plan, S8 | needs a `coai-mcp` release |

Disjoint: this plan touches the mirror and its driver. Defect 2 touches `rolesPanel.ts` and the
tombstone it needs; they meet only at the fact that a stood-down mirror is what makes defect 2's
window dangerous, and that meeting is a SENTENCE in both plans rather than shared code.

## The symptom, which is the incident itself

On 2026-09-16 a role called `Role2` was deleted and went on being reviewed against for ninety
minutes. The mirror had stood down — an older window declined to overwrite settings a newer build
had written — and said so **once**, as a toast nobody saw.

S1–S5 made that toast durable: it is a record now, on the notifications page, counted in the panel.
**What is still true is that four of the five ways this mirror declines to write say nothing at
all.** `sync()` answers one of five outcomes:

| Outcome | Today |
|---|---|
| `written` | the file is updated |
| `unchanged` | nothing to do |
| `stood-down` | reported once per VERSION, with *Reload Window* |
| `failed` | **silent.** The write threw, or the lock did. Two `catch` blocks, one `console` line between them |
| `busy` | **silent.** Another window held the lock |

And `busy` is retried **once**, `RETRY_AFTER_MS` later, by `mirrorSettings` in `extension.ts`. If
that retry is also busy it is dropped, and nothing fires again on its own: the configuration a
person just changed is simply not mirrored, and no surface anywhere says so. `failed` is not retried
at all.

## What this changes

**1. Every outcome that did not write reports, once per CONDITION, cleared on recovery.**

`reportedFor` is one string today, holding the version of the build that was stood down for. It
becomes a per-condition record, so a stand-down and a failed write are two conditions rather than
one counter they share — the same reasoning `reportRefusal` gives for keying a refusal on the
SETTING, and the same reasoning the notifications ledger gives for keying suppression on
`(code, subject)`. A successful write clears all of them, which it already does for the one.

Each condition gets its own `code`, so the notifications page groups them apart and the panel counts
them apart:

| Condition | `code` | What it says and what to do |
|---|---|---|
| a newer build owns the file | `settings-stood-down` *(exists)* | unchanged, including *Reload Window* |
| the file could not be written | `settings-not-mirrored` | names the path, says the server is running on older settings until it can be written |
| another window held the lock, repeatedly | `settings-mirror-busy` | says that another window is writing and that this one gave up after N attempts |

**2. The retry is a bounded back-off rather than one shot, and it is EXACTLY this:**

| Attempt | When |
|---|---|
| 1 | the change itself, immediately — it counts |
| 2 | 2 s after attempt 1 answered |
| 3 | 6 s after attempt 2 answered |
| — | report and stop, at about **8 s** |

Delays are measured from the moment an attempt ANSWERS, not from when it started, so a slow lock
does not shorten the next wait. Three attempts, the first included, and no maximum beyond the sum:
the whole schedule is over in about eight seconds. That number is the answer to two findings at
once — one asked for the contract to be precise enough to verify, and one pointed out that a
PERMANENT fault (a read-only directory) is reported late if the schedule is long. Eight seconds is
short enough that reporting on exhaustion rather than on the first blip costs nobody anything, and
it keeps the once-per-condition rule from firing on a share that hiccupped for 200 ms.

It applies to `busy` AND to `failed`, because an unwritable directory is as transient as a held lock.

**ONE schedule at a time, superseded rather than stacked.** A settings change that arrives while a
retry is pending cancels the pending timer and starts again from attempt 1. Two reviewers found this
from opposite directions, and the half that is already safe is worth saying: each attempt calls
`sync()`, which RE-READS the configuration, so a retry can never write a stale payload over a newer
one — it writes what the settings say when it runs. What was not safe is the schedule itself: a
second timer would race the first and corrupt both attempt counters.

**When the schedule is interrupted by the window closing**, the timer dies with the host and nothing
is persisted. The next activation runs `sync()` again and re-derives everything from the file, which
is the same answer section 3 gives for the stand-down.

**A lock that THREW is not a lock that was busy.** Contention answers `busy` and takes the schedule;
an I/O error acquiring the lock is a `failed`, reported as `settings-not-mirrored` with the rest,
because from the mirror's side the difference is that one of them means another window is working
and the other means this machine is not.

**And after exhaustion, there has to be a way back.** The report offers **Try again**, which re-arms
the schedule from attempt 1. Without it, a person who fixes the permission and changes no setting
leaves the server on stale settings until the next activation — the schedule has stopped, and
nothing else fires on its own.

**3. What is NOT added, with the reason, because the parent plan's bullet asks for it.**

The parent says *"the pending mirror is persisted… written beside the settings file and derived on
load"*. **It is already derived on load and needs no second file**, and adding one would be the
write-gap regress in miniature: the condition worth persisting most — the disk refused a write — is
exactly the condition under which the persisting write also fails.

What actually survives a reload today, by construction: on activation `sync()` runs, reads the file,
and re-derives the stand-down from the stamp in it. The pending CONTENT is re-derived too — it is
the current configuration, which is what the mirror writes. What does not survive is the in-memory
suppression, so a reloaded window reports the condition again — and that is correct rather than a
gap: a new window is a new observer, the same reasoning the notifications ledger uses for minting a
fresh `run` per host.

So this plan built the reporting and the retry, and records that the persistence bullet was
answered by measurement rather than by code.

## Test plan

```bash
cd src_vs_code
npm ci        # first time, or after a rebase onto a main with new dependencies
npm test
```

`ServerSettingsSync` takes its world by injection — `read`, `write`, `readExisting`, `report`,
`critical` are all parameters — so every case below is RUN, not read. That is why this defect gets
behavioural tests where defect 3 could only get wiring ones.

| # | Test | Why it has teeth |
|---|---|---|
| 1 | A write that throws reports ONCE over repeated syncs, and a later successful write clears the condition so the next failure reports again. | The whole defect is the silence; "reports" alone would pass a build that reported on every keystroke in a settings file, which is the failure mode the once-per-condition rule exists for. |
| 2 | A stand-down and a failed write are TWO conditions: reporting one does not suppress the other. | With one shared counter they cancel, and the cancelled one is invisible — this is the eviction defect S4 already met, in a new place. |
| 3 | A lock that is busy every time reports after the last attempt and not before, and the attempts are bounded. | A test that only asserts "reports" passes a build that reports immediately, which is what the back-off exists to avoid; one that only counts attempts passes a build that never reports. |
| 4 | The retry schedule is driven by an injected clock, not by real time. | A test that sleeps is a test that is slow and flaky; the driver takes its timer the way the sync takes its writer. |
| 5 | Every new assertion watched failing first, with its message reported. | `testing.md`, mandatory. |

## Growth surface

**None.** No new file and no new record type: the conditions are notifications, which the S1–S5
ledger already stores and bounds — `suppression.ts` caps what one run may write, and these are keyed
per condition so a loop cannot mint fresh keys.

## Definition of Done

- [x] Every non-writing outcome reports, once per condition, cleared by a successful write.
- [x] The retry is bounded, backs off, and says so when it gives up.
- [x] Each assertion watched failing first, and both observations reported - three planted breaks
      turned six of the seven tests red, each naming the real symptom.
- [x] The whole suite green, and `notificationSites.test.mjs`'s population moves only with a reason
      written beside the constant - 123 to 124, the reason written where the constant is.
- [x] `research/module_extension.md` records the five outcomes and what each one now says.
- [x] Promotion, in this order (`common/planning-docs.md`): status to `IMPLEMENTED <date>` with
      deviations; links both ways; inbound references; the *Currently open* table in
      [README.md](../todo/README.md); `git mv` LAST; then `node .agents/conventions/tools/plan-lifecycle.mjs`.
- [x] Through `review_plan` and `review_code`.

## What this does NOT prove

The mirror's own behaviour is fully exercised, because the class takes its world by injection. What
no test here drives is the SERVER re-reading the file it writes, and the real editor showing the
warning. `extension · a real editor` exists on `main` now and its first scenario asserts that every
declared command is registered; a scenario that makes a settings write fail inside a real host is
buildable and is not built here.
