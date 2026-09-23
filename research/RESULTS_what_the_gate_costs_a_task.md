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

`<dataDir>/coai.db`, opened read-only, 222 rounds started on or after 2026-09-16:

| | p50 | p90 | n |
|---|---|---|---|
| a plan round, reviewers running (`started_utc → completed_utc`) | **1.4 min** | 2.5 min | 104 |
| a code round, reviewers running | **3.8 min** | 7.0 min | 115 |
| the caller deciding the findings (`completed_utc → last resolved_utc`, rounds under 4 h) | **1.7 min** | 11.7 min | 219 |
| the caller BUILDING a story (a session's plan round ends → its code round starts) | **18 min** | 86 min | 92 |
| reviewers per round | 3 | 12 (max) | 222 |

Per day, the same window:

| day | rounds | reviewer-minutes | gate sessions (≈ stories) |
|---|---|---|---|
| 2026-09-16 | 69 | 201 | 36 |
| 2026-09-17 | 74 | 245 | 36 |
| 2026-09-18 | 21 | 53 | 12 |
| 2026-09-21 | 26 | 68 | 11 |
| 2026-09-22 | 18 | 44 | 8 |
| 2026-09-23 | 14 | 37 | 8 |

Over the 15 calling AI sessions with two or more rounds, reviewers were running for **10.7 h of 450.9 h
of session span (2 %)**, and the caller was deciding findings for 32.0 h (7 % — an over-count: a round
left unresolved overnight counts its night).

## What it says

1. **The prediction holds per ROUND and fails per TASK.** One story costs the gate about a plan round
   (1.4 min), a code round (3.8 min) and two sets of decisions (≈ 3.4 min): **roughly 7–9 minutes a
   story**, against an 18-minute median to build it. "A couple of minutes" is right for one round.
2. **The multiplier is the number of stories, and it was set by the gate's own order.** Until issue
   #131 the split order said *"After EVERY story: call review_code … and commit"*. A gate session is
   keyed by branch and ends at its code round, so every story became a branch, a plan document, a plan
   round, a code round and their decisions. 36 gate sessions a day on 2026-09-16/17 is that order being
   followed. A task cut into 4 epics × 4 stories paid that sixteen times: about **two hours of gate**
   before counting the branch, plan and rebase ceremony around each story, which the database does not
   see.
3. **The split was also too eager.** The same order's size verdict called 106 of the repository's 187
   plans "epics" (measured in `PLAN_the_split_is_sized_and_gated_once.md`), so the sixteen was the
   common case rather than the large one.
4. **The drop from 2026-09-18 on** coincides with the operator's instruction to gate once per EPIC
   (2026-09-22 in the agent's own notes; fewer parallel lanes also ran those days) — consistent with the
   mechanism, not a controlled comparison.

## What was done about it

Issue #131 (`research/PLAN_the_split_is_sized_and_gated_once.md`): the split order no longer gates a
story; the choice is **one gate per epic** (default) or **one gate for the whole task**, each epic one
commit; plans are sized from build steps and length, "never more" than the owner's numbers; and every
round's orders are written down, so the next time a task is slow the log says what it was told to do.
At 4 epics that same task pays the gate four times, not sixteen.

Nothing else in the gate is the cost: a reviewer runs for minutes, rounds run their reviewers in
parallel, and the deciding is the caller's.

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
- gate sessions per day = distinct `session_id` per `started_utc` day; per caller = rounds grouped by
  `caller`, span = first start → last decision.
