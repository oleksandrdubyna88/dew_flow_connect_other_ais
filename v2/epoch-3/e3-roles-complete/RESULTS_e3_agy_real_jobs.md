# E3 agy coder: three real jobs through the product's road (the build of the flip)

Date: 2026-11-27. Branch `feat/v2-ep3-e3-agy`, build of the flip commit `b9df00dc` (the `launchable` mark of agy is ON in `shared/coder-boundary-releases.json`). These are PAID agy turns: **5 turns in all, of at most 6**.

## How the jobs ran

The test is `src_mcp/tests/TheRealAgyJobsTests.cs`. It runs only with `COAI_REAL_AGY_JOBS_RUN=1`, a results file and a made-up jobs folder (a skip is not a pass).

- **The product's road.** Each job is a coder job of the product's own runner (`RoleRunner`, `CoderWork`, `CoderTurns`, `CoderBoundary`): the worktree, the checks, the sign-in copy (with a refresh when the margin is missing), the guard folder, the preflight in the real unit, the unit, the turn check after every turn, the fix turn. The vendor set is the product's table (`CoderBoundaryTable.Shipped`, agy launchable by data) with the real `agy` as the CLI and the **product's own program** (`coai-mcp`, built from this checkout) as the hook handler inside the unit.
- **The route** has one model, `agy/gemini-4-argon-medium`. `COAI_FIX_ROUNDS=1`. One turn takes at most 12 minutes, one job 30.
- **The cap.** Every unit goes through one launcher that counts the coder turns (the units named `coai-coder-…-cN`) and REFUSES the 7th before any process is started. The cap was not reached.
- **The made-up repository** of each job: `src/thing.cs` says `state: red`; the task asks to make it say `state: green`; the checks (a real shell build and test, as in the other fixtures) also want a second line `checked: yes` that the task does not name, so a turn 0 that does only what the task says is red and the fix turn learns the rest from the checks.
- **Safety.** A temp HOME (the job's own) and the product's sign-in copy only. The jobs folder (`/home/jinx/coai-t3/runs/real-agy-jobs2`, outside `/tmp`) is made new for the run and deleted in a `finally` (it was gone afterwards, with the sign-in copies). The real sign-in file is only read. Its sha256 was compared **inside the shared lock, around the run** by a script outside the test, and inside the test: **equal both times; its mtime was in the same second.** No token, environment, prompt or file content is written below. The keys folder and `*.key` files were not read. No Python.
- **G15 stays literal.** A turn that asks for a tool outside `read_file`, `write_file`, `edit_file` ends the job `coder-boundary`; that would have been a valid result. It did not happen.

## What the jobs showed

| Job | Turns | End | Tool events outside the three | Turn check | Fix turn in the same conversation |
|---|---|---|---|---|---|
| 1 | 1 | `succeeded`, green | 0 | Fine | no fix turn |
| 2 | 2 | `succeeded`, green | 0 in both | Fine, Fine | yes |
| 3 | 2 | `succeeded`, green | 0 in both | Fine, Fine | yes |

- **All three jobs ended green.** No turn asked for a tool outside the three (0 of 5 turns). Every guard record was `allow`; there was no `deny` and no tool event without a record. Every turn check answered `Fine`. Every turn exited 0, in 13 to 33 seconds.
- **Jobs 2 and 3** had the red checks the fixture was built to give after turn 0 (the second line was missing), then one fix turn through `--conversation` of turn 0's conversation (`same conversation as turn 0: yes`), which wrote the missing line (one `edit_file`). Green after it.
- **Job 1** did not meet a red check: in turn 0 the model read three files (which ones was not recorded) and made one edit that satisfied both lines of the checks. That job shows turn 0 and a green end, but not a fix turn. The fix-turn path was seen in jobs 2 and 3.
- **Refreshes.** The envelope counts one refresh for each turn that needed one: 1 in job 1 (one turn) and 2 in each of jobs 2 and 3 (two turns). The sign-in copy of agy is made fresh for every turn and refreshed through `agy models` on a stale scratch copy when the real token has no margin; the real file was never changed.
- **Read once:** the model read the file it was told about first (`step 2: read_file`) and edited with `edit_file`; it never used `write_file` or a command tool.

## The turns, as recorded

## Job 1

- end: state `succeeded`, stop `none`, 35 s in all
- reason: 
- attempts: agy/gemini-4-argon-medium stop `none`
- envelope: green true, refreshes recorded 1

### Job 1, turn 0 (turn 0)

- exit 0, 29.6 s
- tool events: step 2: read_file DONE; step 3: read_file DONE; step 4: read_file DONE; step 6: edit_file DONE
- tool events outside read_file, write_file, edit_file: 0
- guard records: step 2: read_file allow; step 3: read_file allow; step 4: read_file allow; step 6: edit_file allow
- turn check: Fine
- resumed with --conversation: no

## Job 2

- end: state `succeeded`, stop `none`, 56 s in all
- reason: 
- attempts: agy/gemini-4-argon-medium stop `none`
- envelope: green true, refreshes recorded 2

### Job 2, turn 0 (turn 0)

- exit 0, 33.2 s
- tool events: step 2: read_file DONE; step 4: edit_file DONE
- tool events outside read_file, write_file, edit_file: 0
- guard records: step 2: read_file allow; step 4: edit_file allow
- turn check: Fine
- resumed with --conversation: no

### Job 2, turn 1 (fix turn)

- exit 0, 13.5 s
- tool events: step 9: edit_file DONE
- tool events outside read_file, write_file, edit_file: 0
- guard records: step 9: edit_file allow
- turn check: Fine
- resumed with --conversation: yes, same conversation as turn 0: yes

## Job 3

- end: state `succeeded`, stop `none`, 55 s in all
- reason: 
- attempts: agy/gemini-4-argon-medium stop `none`
- envelope: green true, refreshes recorded 2

### Job 3, turn 0 (turn 0)

- exit 0, 29.4 s
- tool events: step 2: read_file DONE; step 4: edit_file DONE
- tool events outside read_file, write_file, edit_file: 0
- guard records: step 2: read_file allow; step 4: edit_file allow
- turn check: Fine
- resumed with --conversation: no

### Job 3, turn 1 (fix turn)

- exit 0, 16.8 s
- tool events: step 9: edit_file DONE
- tool events outside read_file, write_file, edit_file: 0
- guard records: step 9: edit_file allow
- turn check: Fine
- resumed with --conversation: yes, same conversation as turn 0: yes

paid agy turns started in all: 5 of at most 6
the made-up jobs folder (with the sign-in copies) is removed: true
real sign-in file, measured in the test: equal
