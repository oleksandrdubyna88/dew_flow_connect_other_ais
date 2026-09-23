# RESULTS — what the gate costs a task, measured on one machine's own rounds

> Issue #167, 2026-09-23. The question: *"Claude Code has become very slow — tasks take one to two
> hours, some four and more. Is it this plugin?"* Measured on the operator's own rounds database, not
> on a bench. Subject: coai-mcp as installed on this machine (0.32.0), the rounds it recorded
> 2026-09-16 → 2026-09-23. Repository at `9129b355`. The queries are below, whole, so the numbers can
> be re-run against any data directory.

## The prediction, and where it came from

The issue's own guess: the plugin *"adds a couple of minutes"* to a task. That guess is the prediction
this measures against. It was not written down before the numbers were read — the issue is the only
record of it — so read the comparison with that in mind.

## What was measured

`<dataDir>/coai.db`, opened read-only, the 222 rounds started from 2026-09-16 up to the moment of reading on 2026-09-23 (re-run with the upper bound `< '2026-09-24'` to get the same set). Every row below is **per round**; p50 is `statistics.median`, p90 is the value at index `int(0.9 × n)` of the sorted list:

| | p50 | p90 | n |
|---|---|---|---|
| a plan round, reviewers running (`started_utc → completed_utc`) | **1.4 min** | 2.5 min | 104 |
| a code round, reviewers running | **3.8 min** | 7.0 min | 115 |
| the caller deciding one round's findings (`completed_utc → last resolved_utc`, rounds under 4 h) | **1.7 min** | 11.7 min | 219 |
| the caller BUILDING a story (a session's plan round ends → its code round starts) | **18 min** | 86 min | 92 |
| reviewers per round | 3 | 12 (max) | 222 |

Per day, the same window:

| day | rounds | round minutes (wall-clock, summed) | gate sessions (≈ stories) |
|---|---|---|---|
| 2026-09-16 | 69 | 201 | 36 |
| 2026-09-17 | 74 | 245 | 36 |
| 2026-09-18 | 21 | 53 | 12 |
| 2026-09-21 | 26 | 68 | 11 |
| 2026-09-22 | 18 | 44 | 8 |
| 2026-09-23 | 14 | 37 | 8 |

Over the 15 calling AI sessions with two or more rounds, rounds were open (wall-clock, reviewers running
in parallel inside them) for **10.7 h of 450.9 h of session span (2 %)**, and the caller was deciding
findings for 32.0 h (7 % — an over-count: a round left unresolved overnight counts its night). Neither
figure is inside the other.

## What it says

1. **The prediction holds per ROUND and fails per TASK.** Assuming one plan round and one code round
   per story — the shape the old order produced — a story costs the gate a plan round (1.4 min), a code
   round (3.8 min) and two decisions (2 × 1.7 ≈ 3.4 min): **roughly 7–9 minutes a story**, a rough
   estimate from medians, against an 18-minute median to build it. "A couple of minutes" is right for one
   round.
2. **The multiplier is the number of stories, and — this is the mechanism, not a measured cause — it
   was set by the gate's own order.** Until issue
   #131 the split order said *"After EVERY story: call review_code … and commit"*. A gate session is
   keyed by branch and ends at its code round, so every story became a branch, a plan document, a plan
   round, a code round and their decisions. 36 gate sessions a day on 2026-09-16/17 is that order being
   followed. A task cut into 4 epics × 4 stories would pay that sixteen times: about **two hours of
   gate** before counting the branch, plan and rebase ceremony around each story, which the database
   does not see. How much of a one-to-four-hour task that was is NOT measured here: the database links
   no gate session to the task it belonged to.
3. **The split was also too eager.** The same order's size verdict called 106 of the repository's 187
   plans "epics" (measured in `PLAN_the_split_is_sized_and_gated_once.md`), so the sixteen was the
   common case rather than the large one.
4. **The drop from 2026-09-18 on is NOT the per-epic instruction**, which the agent's notes date
   2026-09-22 — four days later. It fits fewer parallel lanes and different work on those days; only
   2026-09-22 and 2026-09-23 fall after the instruction, and two days are no comparison. (Plan round of
   this record, gemini.)

## What was done about it

Issue #131 (`research/PLAN_the_split_is_sized_and_gated_once.md`): the split order no longer gates a
story; the choice is **one gate per epic** (default) or **one gate for the whole task**, each epic one
commit; plans are sized from build steps and length, "never more" than the owner's numbers; and every
round's orders are written down, so the next time a task is slow the log says what it was told to do.
At 4 epics that same task pays the gate four times, not sixteen.

Nothing else in the gate is the cost: a reviewer runs for minutes, rounds run their reviewers in
parallel, and the deciding is the caller's.

## Disposition of issue #167

**Close it as addressed by #131**, with the unmeasured half named: the gate's own cost per round is
minutes, the order that multiplied it per story is gone, and with one gate per epic a 4-epic task pays
it four times rather than sixteen. If tasks are still hours long after #131 ships, the next measurement
is the caller's time OUTSIDE rounds (below), which this database cannot see — that would be a new issue
with a different instrument, not this one reopened.

## What this does NOT settle

- **The caller's own time outside rounds** — writing plans, rebasing, running suites, waiting on pull
  requests (the *Work autonomously* order asks for a ~5-minute wait per pull request) — is not in this
  database. It is the larger share of a slow task and it is not measured here.
- **Queue wait** before a reviewer starts is inside `started_utc → completed_utc` and not separable.
- **One machine, one week, one operator's work.** No other installation is represented.
- The per-day drop is observational: the instruction to gate per epic, the number of lanes and the kind
  of work all changed over the week.

## The queries

Python 3, `sqlite3`, read-only (`file:<dataDir>/coai.db?mode=ro`):

```sql
-- every round in the window, with its session
SELECT r.session_id, r.stage, r.number, r.started_utc, r.completed_utc,
       (SELECT MAX(f.resolved_utc) FROM findings f WHERE f.round_id = r.id) AS decided,
       (SELECT COUNT(*) FROM reviewers v WHERE v.round_id = r.id)          AS reviewers,
       r.caller
FROM rounds r
WHERE r.started_utc >= '2026-09-16';
```

- round minutes = `completed_utc − started_utc`; deciding = `decided − completed_utc` (kept when under
  four hours); building = the first `CodeReview` start of a session − the last `PlanReview` end of the
  same session (kept when positive and under a day).
- gate sessions per day = distinct `session_id` per `started_utc` day; round minutes per day = the sum of
  `completed_utc − started_utc` over that day's rounds (wall-clock, not reviewer-seconds); per caller =
  rounds with a non-empty `caller` grouped by it, callers with fewer than two rounds dropped, span =
  first start → last decision (or last completion when nothing was decided). Rows whose timestamps do
  not parse are skipped. Add `AND r.started_utc < '2026-09-24'` to fix the window.
