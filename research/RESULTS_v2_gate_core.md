# RESULTS — v2 E1 live proof: real gates, migration on copies of real data, outcome shares (2026-11-22)

> Story `s6-live-proof` of coai v2 epoch 1, E1 (gate simplify). This is a MEASUREMENT: no feature code was written.
> Question: after E1.S1 (four outcomes) and E1.S4 (`--migrate-v2`), does the gate run end to end on a real reviewer
> without ever waiting for a person, and does the migration do what its plan says on real data?

## 0. Conditions

| | |
|---|---|
| Machine | WSL2 (Ubuntu), Linux 6.18.58.1-microsoft-standard-WSL2, 15 GB RAM, .NET SDK 10.0.114 |
| Binary | `src_mcp/src/bin/Debug/net10.0/coai-mcp`, built with `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4` (0 warnings, 0 errors, 37 s). Branch `feat/v2-e1-gate-simplify`, head `d331eb67` (E1.S1 four outcomes + E1.S4 `--migrate-v2`). `--version` says `0.0.0` (a dev build carries no release number). |
| Reviewer vendors (t1) | the person's own settings, copied: `codex-4` (runtime codex, model `gpt-6.1-terra`, codex-cli 0.171.0, own sign-in) is the ONLY row ticked for plan and code. `codex-3` and `antigravity-2` (antigravity 1.5.14) are ticked for feature reviews only and were not asked. |
| Policy rows in that settings file | `COAI_AUTONOMOUS=true`, plan critique 2 rounds, architecture 2 rounds; no `COAI_ON_EXHAUSTED` |
| Dates (UTC) | 2026-11-22. Gates 19:47:00 to 19:49:25. Data copy taken about 19:50 to 20:00. Migration runs 20:00 to 20:06. |
| Scratch | everything under the session scratchpad; removed at the end (private data). The real folders were only read. |

How a dev binary is run: the binary is the `coai-mcp` apphost in the build folder; the data folder is set with `COAI_DATA_DIR` and
`COAI_DATA_SIDE` (`PanelSettings.DataDirectoryFor`). The person's live setup (`~/.claude.json`) points at
`COAI_DATA_DIR=/mnt/v/connectOtherAis`, `COAI_DATA_SIDE=wsl-ubuntu`.

A mistake to record: the first `--providers` call of the build check ran WITHOUT `COAI_DATA_DIR`, so it read the default WSL folder
`~/.local/share/coai-mcp` (a stale folder, last written 2026-10-27; its `settings.json` is unchanged, 33 bytes, dated 2026-10-25). That mode only reads.

## 1. t1 — three real plan-then-code gates (E1 binary, real reviewer, no person)

Driven by a small node script over stdio JSON-RPC: `initialize`, `open`, `review_plan` (planText), `resolve`, `review_code` (baseRef `main`, planText as scope),
`resolve`, `status`. Data folder: a scratch copy that held ONLY the person's `settings.json` (side `wsl-ubuntu`). The scratch repo has three
branches off one empty-ish `main`, each one commit with about 40 lines of code and a test file that passes: (1) JS `mergeIntervals`,
(2) Python `LruCache` with a time to live, (3) JS `parseCsvLine`. Every finding was recorded as `reject` with the reason "measurement run on a scratch
change: the finding is recorded, the code is not edited" (nothing was fixed, so nothing was accepted).

| Gate | Stage | Verdict | Reviewers answered / asked | Findings (severity) | Gating / threshold | Reviewer time | Wall time of the call | Tokens in / out |
|---|---|---|---|---|---|---|---|---|
| 1 mergeIntervals | plan | proceed | 1 / 1 | 1 (Major) | 1 / 6 | 13.3 s | 15.4 s | 17047 / 280 |
| 1 mergeIntervals | code | proceed | 3 / 3 (+ Conventions not asked) | 0 | 0 / 5 | 4.7, 4.8, 4.2 s | 9.6 s | 53286 / 181 |
| 2 LruCache | plan | proceed | 1 / 1 | 3 (Major x3) | 3 / 6 | 12.1 s | 14.1 s | 17055 / 481 |
| 2 LruCache | code | proceed | 3 / 3 | 5 (Major x4, Minor x1) | 4 / 5 | 7.9, 13.2, 9.2 s | 18.0 s | 52709 / 957 |
| 3 parseCsvLine | plan | proceed | 1 / 1 | 2 (Major, Minor) | 1 / 6 | 10.5 s | 12.2 s | 16118 / 383 |
| 3 parseCsvLine | code | proceed | 3 / 3 | 1 (Minor) | 0 / 5 | 6.7, 6.9, 9.7 s | 17.4 s | 53160 / 645 |

Totals per gate (plan call + code call, `resolve` took 0.0 s): gate 1 25.8 s, gate 2 33.2 s, gate 3 30.4 s. 12 findings in all. No round used a second round.

"Reviewers" on the code stage are three ROLE reviewers (Architecture, SecurityReliability, UxDxPerformance), all on the one vendor row `codex-4`; the
Conventions role was skipped by the server ("this repository has no written rules to judge against"), which is the server's own rule for a rules-free repo.

Waits for a person: **0 in all three gates.** What was checked: every call returned at once after the reviewers, `status` after each gate says `stage: Done`,
`awaitingResolve: false`, `pending: []`; the server sent no notification or server-initiated message on stdout (`events.json` empty in every run), no
non-JSON line reached stdout, no `escalations/` folder or card was created in the scratch data folder, and no verdict other than `proceed` came back.
`ask_human` was never called. The only places the words "call_human" / "ask_human" appear in the replies are the operator `commands` texts of the person's settings
(for instance "ASK THE CONSULTANTS FIRST ... call ask_human only when the answers do not settle it"), sent back with every plan and code reply. They are advice
to the calling model, not a gate state — but they still describe a person-asking flow in a v2 reply, which a later E1/E2 story may want to reword.

### Not measured in t1, and why

- **`revise`, `good_enough`, `not_reviewed` live.** All six rounds were `proceed`: the three small changes never crossed their thresholds (6 for the plan, 5 for the code), and a vendor
  outage cannot be ordered. The three other outcomes were not re-checked by tests in this story; they show up only in v1 history (section 3), where `good_enough` and `not_reviewed` come from `policy auto`. So "only the four outcomes" is confirmed here as "nothing outside `proceed` appeared and nothing outside the four can be produced"; it is not a live sample of all four.
- **One vendor.** The person's plan/code roster is a single reviewer row, so no cross-vendor merge, de-duplication or partial-answer case was exercised.
- **Cost in money.** The replies carry tokens only ("no cost reported").
- The reviewers judged tiny scratch code; their finding quality was not rated, only counted.

## 2. t2 — `--migrate-v2` on COPIES of every real data folder

Copies with `cp -a` into the scratchpad; nothing excluded (the largest, `logs/`, is 6.1 MB, and `coai.db` was copied as one file while the person's servers were running; no `-wal` file existed at that moment).
Per folder, in order: `--migrate-v2` (dry run), `--migrate-v2 --apply`, `--migrate-v2 --apply` again, each with `COAI_DATA_DIR` / `COAI_DATA_SIDE` set to the copy. All 9 runs exit 0.

| Data folder (copy of) | Size | Dry run: sessions / cards / stores / settingsRemoved / settingsRenamed / unreadable / deferred | Apply 1 (same counts?) | Apply 2 (changes) | answeredHolds |
|---|---|---|---|---|---|
| `/mnt/v/connectOtherAis`, side `wsl-ubuntu` — the folder the person's MCP server really runs on; 581 session files, `coai.db` 50 MB | 113 MB | 1 / 0 / 2 / 0 / 0 / 0 / 0 (status `plan`) | 1 / 0 / 2 / 0 / 0 / 0 / 0 (status `done`) — **equal** | 0 / 0 / 0 / 0 / 0 / 0 / 0 — **0 changes** | 2 (one session: a hold a person had answered `continue` and one answered `fix`; the session is left as a held gate, converted) |
| WSL `~/.local/share/coai-mcp` (default folder, stale since 2026-10-27, no database) | 72 KB | 0 / 0 / 0 / 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 / 0 / 0 / 0 — **equal** | 0 — **0 changes** | 0 |
| Windows `/mnt/c/Users/strug/AppData/Local/coai-mcp` (read from WSL, flat folder, no side, no database) | 64 KB | 0 / 0 / 0 / **1** / 0 / 0 / 0 (`COAI_ON_EXHAUSTED` removed from `settings.json`) | same — **equal** | 0 / 0 / 0 / 0 / 0 / 0 / 0 — **0 changes** | 0 |

Steps of the real-folder run: `convert` the one held session; `archive` `qphase/` and `cadence/` to `migrations/v1-archive/`. After apply 1 the folder holds `migrations/v2.done`
(`schema 1`, `tablesHash dc1a70ece90e4147`), `migrations/v2.journal` (6 lines) and a `claims/migrate-v2.lease`. Apply 2 wrote nothing new.

Side checks on the real-folder copy, against a copy made just before the migration: only the converted session file, the two archived stores, `migrations/` and the lease differ; `coai.db`
differs in bytes (52,264,960 to 52,408,320, page count 12,760 to 12,795, free pages 6 to 41) but has the SAME table list and the SAME row counts (rounds 1245, findings 6094, sessions 550, reviewers 6267,
consultations 192, round_changes 2934). I did not find out which step touched the file (opening the database in WAL mode is the likeliest). The 1 session out of 581 that needed a conversion is the only
v1 hold in the folder: a `humanGate` was set in 1 session, and 7 `escalations/` cards exist (4 answered).

### Not measured in t2

- The apply of a folder whose sessions are held and UNanswered, or whose objects are unreadable: this data has none (`unreadable 0`, `deferred 0`), so the `75` exit and the no-marker path were not seen on real data.
- A v1 `coai-mcp` still alive on the folder during the migration (the person's servers were running on the original; the copies had none).
- Folders of other machines and of the team server, and the folders `AI_history/` and `wsl-care-archive-livegate-20261010T0831Z/` that sit beside `wsl-ubuntu` on V: (not examined; I took them for archives, not live coai side folders).
- The automatic first-start migration of a v2 SERVER (as opposed to the one-shot `--migrate-v2`): not run here.

## 3. t3 — share of rounds that ended with a person versus the auto outcomes

Database: the PRISTINE copy of the real `coai.db` (taken before any migration; the migrated copy gives identical counts). The window is 2026-10-25 20:17 UTC to 2026-11-22 19:54 UTC, 1245 rounds (472 plan, 653 code, 114 document, 6 feature).
Rounds of this history carry notes such as "policy auto: 7 finding(s) still gate after 2 round(s) — good enough", so auto outcomes already exist inside the v1 history (I did not check which build wrote each round). The verdict `escalated` is not a value of
`rounds.verdict` (nor of any other column) in this database: a person-asking card (`ask_human`, `escalations/`) is a file next to a round, not a round outcome. The query therefore counts `call_human`, and the cards are reported beside it.

```sql
-- node:sqlite, read-only, on the pristine coai.db copy
select verdict, count(*) n from rounds group by verdict order by n desc;

select round(100.0*sum(verdict='call_human')/count(*),2)                    as call_human_pct,
       round(100.0*sum(verdict in ('good_enough','not_reviewed'))/count(*),2) as auto_pct,
       round(100.0*sum(verdict='proceed')/count(*),2)                       as proceed_pct,
       round(100.0*sum(verdict='revise')/count(*),2)                        as revise_pct,
       count(*)                                                             as total
from rounds;

select stage, verdict, count(*) n, min(substr(started_utc,1,10)) first, max(substr(started_utc,1,10)) last
from rounds where verdict in ('call_human','good_enough','not_reviewed') group by stage, verdict;
```

| Verdict (v1 history, 1245 rounds) | Rounds | Share |
|---|---|---|
| proceed | 1170 | 93.98 % |
| revise | 51 | 4.10 % |
| good_enough (auto, policy auto cap) | 20 | 1.61 % |
| not_reviewed (auto, nobody answered) | 1 | 0.08 % |
| **good_enough + not_reviewed** | **21** | **1.69 %** |
| **call_human** | **3** | **0.24 %** |

The 3 `call_human` rounds: two FeatureReview round 2 (2026-11-10, 2026-11-11) and one PlanReview round 1 (2026-11-18). The 21 auto rounds: good_enough in CodeReview 9, PlanReview 6, DocumentReview 5; not_reviewed 1 (PlanReview, 2026-11-20).
Cards outside the rounds table: 7 `ask_human` cards (4 answered) among 581 sessions.

The v2 side measured live (section 1): 6 rounds, 6 `proceed`, 0 `call_human`, 0 `good_enough`, 0 `not_reviewed`, 0 cards. Shares: call_human 0 %, auto 0 %.

Read the two columns with care: the v1 history is 1245 rounds of real work (large plans and diffs, up to 13 gating findings), the v2 sample is 6 rounds of tiny changes. The honest finding is on the
call_human column only: v1 history under `policy auto` already stopped for a person in 0.24 % of rounds (3 of 1245), none of them later than 2026-11-18, and the E1 binary produced no stop in 6 of 6.
A v2 share for `good_enough` and `not_reviewed` on real work needs real use of an E1 server; nothing here says it will stay at 0.

## 4. What this proves, and what it does not

Proved: the E1 binary builds clean; it runs `open` to `review_plan` to `resolve` to `review_code` to `resolve` against a real reviewer in 25 to 33 s per gate with no person asked;
`--migrate-v2` on copies of all three real data folders gives dry-run counts equal to apply counts, and a second apply changes 0 (with the 2 answered holds reported).

Not proved: all four outcomes on a live reviewer (only `proceed`); a second vendor; a hold left unanswered; a coexisting v1 process; the first-start auto migration.
