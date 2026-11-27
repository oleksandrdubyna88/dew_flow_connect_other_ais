# coai v2 — plan

> Status: **plan, nothing built, 2026-11-22.** Branch `feat/v2-plan`. Built with the v2 work method itself: a feature
> folder → epochs (≤ 5 epics each, MVP first) → epics → stories → tasks, each node with a YAML file. Stories and tasks
> are written for an epic when it starts (now: epoch 1). The split was re-decided by Fable on 2026-11-22: 5 epochs, MVP epics with measurable pass criteria, 10 cornerstone stories (feature.yaml `cornerstones`; 12 since 2026-11-25, when the E3 plan gate added E3 s3 and s4).

## Plan changes

> Epoch 1 · approved 2026-11-22 · by reviewers · state: closed

- 1.1 · 2026-11-25 · owner · contract, approach — decision 40: the coder prompt has a version for each vendor, the claude text unchanged (epoch 3 E3 SPEC C7)
- 1.2 · 2026-11-26 · owner · dod — the paid caps of epoch 3 E3: s8 t3 and s8 t4 together at most 30 USD, s5 t6 at most 30 codex calls, s6 t7 at most 30 agy turns; they replace 8 calls, 12 turns, 0.10 USD and 0.50 USD
- 1.3 · 2026-11-26 · owner · scope, contract — the risks agy A1 to A9 and codex C1 to C4 are accepted; claude, codex and agy each ship as coder, reviewer and consultant, so the codex and agy main-AI rows of E3 s7 t4 are both filled
- 1.4 · 2026-11-26 · owner · scope — the instruction step of E3 s7 t3 keeps its 64 KiB limit; a short form of the rule resolver's output is a follow-up outside epoch 3
- 1.5 · 2026-11-26 · owner · contract, epics, dod — a secret in the output of the instruction step stops nothing: the job goes on and a notice `instructions-secret` names the class and the line, never the value; its tab in the extension's notifications is a new doneWhen line of epoch 4 e4-local-pages
- 1.6 · 2026-11-26 · owner · contract — WorkPack renders an outside dependency in the pack text for every scope, epoch and epic included; whole-epic text ids move, approval ids stay
- 1.7 · 2026-11-26 · owner · contract, approach — a coder's checks (epoch 3 E4, A4, D12, D13): the head's own definitions run, nothing the head changed is refused, every difference from the base is reported; restore takes packages from the internet; E3's text agrees
  (The second entry stopped the plan. The owner on counting these and reviewing epoch 3 whole, 2026-11-26: «да, пересматриваем».)
  (Epoch 1 is dated at the plan gate of the whole plan: round 2, `good_enough`, 2026-11-22, commit c24e5141. The ledger was
  opened on 2026-11-26 with these seven entries. Changes before that day are not entries: they are in the SPEC files of the
  epics and in their deviations. For epoch 3 E3 they are sections J, K and L of
  [SPEC_CHANGES.md](epoch-3/e3-roles-complete/SPEC_CHANGES.md): the judgement of s4 t6, the coder comparison and the two
  findings of the second plan round are named there as plan changes. Whether they become entries is the lead's decision.)
  (What each entry changed in E3: section M of the same file. The re-review record for epoch 2 is written by the re-review.)
- 1.8 · 2026-11-26 · owner · contract, approach, dod — a new version of a CLI never stops anything, for every vendor (claude, codex and agy alike): no version, release, hash or pin of any vendor's CLI gates, refuses, stops a job or forces a new measure, a new judgement or a doc rewrite; what was measured is history ("measured on X"); a vendor ships by its verdict
- 1.9 · 2026-11-26 · reviewer · contract, dod — re-review round of epoch 3 E3 (session 1089eaa8, proceed), finding F0: the failure record of the instruction step redacts the argv and the last lines of standard error before they are recorded or shown; a rule of records, not of the prompt
- 1.10 · 2026-11-26 · reviewer · contract, dod — the same round, finding F1: the approval id covers a pack format version, raised with the outside-dependency change of 1.6; an approval of an older format binds no coder job and the refusal asks for a new plan round; so the approval id of every scope moves once
- 1.11 · 2026-11-26 · reviewer · contract, dod — the same round, finding F3: the 30 USD cap of E3 s8 t3 and s8 t4 is enforced before each call by a reserved priced worst case, not from the ledger afterwards; the reservation is recorded beside the actual cost
  (Entry 1.8: the owner's words as the lead gave them — "a new version must never stop anything, or rewrite docs, pins and so
  on. It came out, it came out. We move on. We can write down for history which version the check was on." — and that it
  holds for every AI. It takes "only a measured release", the pins and the claude version floor out of the plan.)
  (Entries 1.9 to 1.11 are the three findings accepted in the re-review round of E3, which read the plan at c48b3a5c. One
  finding, F2, was rejected: it is the owner's decision of entry 1.4. They are recorded as entries by the lead's order. The
  rule of plan changes also says that what a re-review changes is the baseline of the next epoch; whoever opens epoch 2
  decides which reading stands. The plan stays stopped: epoch 2 and its re-review record are not written here.)
  (What these four changed in E3: sections M and N of the same file.)
- 1.12 · 2026-11-26 · reviewer · contract, dod — re-review round of epoch 3 E4 (session c0112e7a, proceed), finding F0: a re-run excuses a failure only when it exits 0 with no new failure and no execution error (E4 SPEC section F, K13, s5 t5 d5)
- 1.13 · 2026-11-26 · reviewer · contract, dod — the same round, finding F1: one precedence rule for the toolchain file across the head's transitions: the head's state wins, an added and a changed file run the head's file, a deleted file runs the head's detection, and every transition is reported (E4 SPEC A1, D15 to D17, s2 t5, s3 t3 d4)
- 1.14 · 2026-11-26 · reviewer · contract, dod — the same round, finding F2: the format step runs in its own unit; after it only the allowed tracked changes are taken, then a clean, an empty status --ignored and the restore again (E4 SPEC section D, F7, s5 t1 d5)
  (Entries 1.12 to 1.14 are the three findings accepted in the re-review round of E4. They are folded into the E4 plan on the
  branch feat/v2-ep3-e4-plan: commit 9941801b, the SPEC and the yaml files, and commit 2a4ef2ee, the bundle. That text is not
  on this branch. One finding of that round, F3, about dotnet test on this repository, was rejected: it was measured in E4
  s1; the reason is in section R of the E4 SPEC. As for 1.9 to 1.11, they are entries by the lead's order, and the plan
  stays stopped.)
- 1.15 · 2026-11-26 · owner · scope — the risks that the addenda 2 and 3 of the E3 judgement added are accepted: codex C5, C6, C7 and C9, agy A10, A11, A12 and A13 (A1 to A9 and C1 to C4 were accepted in 1.3; C8 was closed by the measure); G13 of the judgement is answered
- 1.16 · 2026-11-26 · owner · merge-conditions, contract, dod, build-order — codex and agy become launchable coders only after the must-changes of the judgements are built and green: G15 (the guard's record matched to the stream by conversation, step index and tool), the exact stream-item rule of CX-T4, the environments.toml refusal, and a refresh before the launch so that an expired copy never reaches a start (A11); not before
- 1.17 · 2026-11-26 · owner · contract — the property and behaviour checks stay: an unknown item type of the stream, a flag the help does not declare, the preflight rows, the turn checks; one of them may stop a job after a CLI updated itself; that is accepted as protection and is not a version gate (it settles what 1.8 left open)
  (Entries 1.15 to 1.17 are the owner's answer to the addenda of the judgement: sections 13 and 14 of
  v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md at commit d0dca16d on the branch feat/v2-ep3-e3-measure, which was
  not merged into this branch on that day (it is since, as a9187e76). The planner has the answer from the lead, not in the owner's own words. What they changed
  in E3, and how the lists 13.11 and 14.8 map on the task files: section O of
  [SPEC_CHANGES.md](epoch-3/e3-roles-complete/SPEC_CHANGES.md). The plan stays stopped.)

### Re-review for epoch 2

Narrowed: yes — the release pin, the agy program store and the refusal of a head that changed its own checks left the Definition of Done (entries 1.7 and 1.8), and a secret in the output of the instruction step no longer ends a job (1.5); the owner approved

| Work | Where | Verdict | Action |
|---|---|---|---|
| the plan of epoch 3 E3 after its re-review round (coai session 1089eaa8, proceed) and the owner's answers, plan commit 60f3b5a0 | feat/v2-ep3-e3-roles, pushed | fits | kept |
| the plan of epoch 3 E4 after its re-review round (coai session c0112e7a, proceed), plan commit 2a4ef2ee | feat/v2-ep3-e4-plan, pushed | fits | kept |
| the code of ledger epoch 1: 184 of its 185 commits, in 16 groups; 8 of the 184 fit only because a later commit replaced the code; the list is in epoch-3/RESULTS_epoch1_done_code_sorting.md | feat/v2-ep3-e3-roles and feat/v2-ep3-e4-plan, pushed | fits | kept |
| eabcbea1, in part: the rule that a vendor is launchable by its verdict alone (CoderVendorRow.Ships), with the parts of e8101102 and fdf8a12f that rely on it; its version half fits | feat/v2-ep3-e3-roles, pushed | cut | fixed forward in E3 s7 t1 d3 by a new commit of that branch; no revert of a pushed commit and no history rewrite |
  (The owner approved the revised plan on 2026-11-26, about 15:00 UTC, with the words «план принят», after a summary of the
  whole of epoch 3 in Russian that covered every entry of epoch 1, from the first to 1.17, both rejected findings — F2 of the
  E3 round and F3 of the E4 round — and what remains to be built in E3 and E4. The planner has this from the lead.)
  (The table holds the two plan commits the owner read from. The code that was written under epoch 1 on the branches
  feat/v2-ep3-e3-roles, feat/v2-ep3-e4-plan and feat/v2-ep3-e3-measure was not sorted by a judge for this record: no row
  says of a code commit that it fits or that it is cut. That sorting is step 5 of the rule of plan changes, and it is open.)
  (Completed later on 2026-11-26. An Opus judge, read only, sorted the code of epoch 1 at the tips 9157dc94 of E3 and
  2a4ef2ee of E4. Its file is [RESULTS_epoch1_done_code_sorting.md](epoch-3/RESULTS_epoch1_done_code_sorting.md), copied
  unchanged; the two last rows of the table are its result. Of 185 commits 184 fit, 8 of them only because a later commit
  replaced the code, and one is cut in part: eabcbea1. Its rule "the table ships by verdict" contradicts entry 1.16, which
  asks for a launchable mark. No codex or agy coder can be launched today, so the cut did no harm yet.)
  (Carried, not cut. First: the E4 branch has no eabcbea1 and still refuses a claude below 2.1.330; E4 takes the removal of
  the version floor by merging E3. Second: the E4 tip holds stale texts about refusals that its code no longer makes; the
  next E4 commit fixes them. Third, not built yet: the argv half of entry 1.9, which is E3 s7 t3 d7, and entry 1.10, which
  is E3 s3 t4 d6; on E4 the judge also names the entries 1.12 and 1.14 as open, and 1.13 as met by afb4c834.)
  (Step 2 of the rule, the halt: deviation, recorded. Four code commits were written after the stop commit 66bc4eea of
  12:52: on E3 the commits 2ca58a8c, e2fe7a75 and eabcbea1, all at 14:03, and on E4 the commit afb4c834 at 12:57, on a
  branch that does not hold the stop commit. Each implements an owner decision of the same day: the entries 1.6, 1.5, 1.8
  and 1.7, in that order. By the lead's account the owner ordered each of them in the lead's conversation, and the owner
  accepted the whole plan afterwards. The judge sorted their content with the rest.)

> Epoch 2 · approved 2026-11-26 · by owner · state: open

- 2.1 · 2026-11-27 · owner · contract, approach, dod, epics, stories, build-order — a codex name with no version (`terra`, `astra`) is the newest model of its family that the installed CLI lists, resolved when a job reaches the entry, so a new model needs no hand edit; a name that cannot be used ends the entry with the new stop word `model-unavailable`, which fails over, and not `coder-boundary`; the tool list of a codex coder passes when it holds no tool outside the known set, in place of exactly the 11 measured names (epoch 3 E3, SPEC_CHANGES section Q; new task s2 t5; a new doneWhen line of epoch 4 e4-local-pages)
  (The owner's words of 2026-11-27, as the lead gave them, in translation: "Default: the latest terra. When 6.2 or 7 comes
  out, it must pick up the latest available one by itself." The planner has them from the lead, not in the owner's own
  language. What made the decision needed: the default name `terra` is no model of the codex CLI, so the default codex coder
  could not be launched, and the row `tool-list` refused every model but `gpt-6.1-terra`; the facts are in
  epoch-3/e3-roles-complete/RESULTS_e3_real_codex_pass7.md.)
  (One cause, one entry. The owner's words name the model. The stop word and the tool rule are the lead's reading of what
  the decision needs: with the rule of exactly 11 names a new model that shows another list is refused, and "by itself" is
  then not true. If the owner or the lead reads the tool rule as a cause of its own, it is a second entry of this epoch
  and the plan stops.)
  (Count: this is the first change of ledger epoch 2. The rule allows one change: it is recorded and the work goes on. The
  plan is not stopped. The next change of this epoch is the second: it stops the plan for a whole re-review.)
  (What it changed in E3, and what it did not: section Q of
  [SPEC_CHANGES.md](epoch-3/e3-roles-complete/SPEC_CHANGES.md). The security reading of "fewer tools pass" there is the
  planner's, not a judgement addendum. Not counted here, and for the lead to decide: section P of the same file lists
  changes that the security verify made on 2026-11-27, after the approval of this epoch, and none of them is an entry.)

## Goal

Make coai one product the owner can run on every machine from one Windows VS Code, that never waits for a person, starts
in ≤ 2 s, and does the deterministic half of AI development itself (build, test, merge, PR) so the AI does only the
judgement half — faster, with quality kept.

## Owner decisions (2026-11-22, fixed)

- Shape C: the C# core (Native AOT) is simplified in place with its tests; the extension is a new thin shell on the
  shared UI kit, released as the same extension 2.0. Chat and phrases are carried as they are.
- Auto mode only: the gate never waits for a person; failures become notifications. Gate meaning kept: plan → resolve →
  code → resolve; document and feature reviews; outcomes proceed / revise / good_enough / not_reviewed.
- Names: `cons` (stuck consultant), `consask` (question consultant).
- Hub = a mode of the Team server on the ionos host, personal tokens; settings AND logs of every device; one-click update
  everywhere; nothing manual except on Windows (paste-ready bash/pwsh lines).
- VM: one shared instance for all OS users, everything shared including CLI logins.
- Zero-config install: finds claude / codex / agy, writes MCP config and hooks; vendor-neutral main AI.
- Main AI page: main AI, roles → models (orchestrator, coder, scout, important, BA), complexity 0–10 → model bands,
  Haiku task kinds, and the work method itself (MVP epics, max epics per epoch, min consults per epoch/epic) —
  configurable per device.
- AskUserQuestion hook: the first question in a session is answered by consask; later ones within 30 min reach the person.
- Coder delegation to another AI with a strict JSON contract; coai runs build/tests itself after each coder task and
  keeps the coder's CLI session until green; coai runs everything parallel that can be parallel.
- Toolchain adapters: C#, Node/JS/TS, PHP, Rust, Python, Java.
- tmux Claude sessions through the existing creds MCP; Claude mod with phrases, active consults, notifications.
- Studio (video) review mode with picture attachments; frames may go to Claude, Gemini (agy), Codex.
- Security lane deferred. Conventions optional: coai no longer ships or enforces them; a repo that mounts them still has them read (reworked later).
- Order: foundation → hub/devices/update → new features.

## Architecture

- Server half: [architecture/server.md](architecture/server.md) (incl. consultant review 1, § 16).
- Extension half: [architecture/extension.md](architecture/extension.md) (incl. consultant review 1, § 10).

## Epochs

| Epoch | Epics | Proves |
|---|---|---|
| [1 Foundation](epoch-1/epoch.yaml) | E1 gate core + live proof (MVP), E2 thin shell + joint smoke (MVP), E3 server cleanup + install + roles, E4 shell modules, E5 2.0 release | the simplified gate and the fast shell work end to end with real data |
| [2 Hub](epoch-2/epoch.yaml) | E1 hub slice (MVP), E2 VM shared instance (MVP), E3 device agent + sync, E4 update + rollout, E5 kit 0.3.0 + Devices page | one Windows window sees and controls every device |
| [3 Deterministic work](epoch-3/epoch.yaml) | E1 external-role jobs + router + failover + BA (MVP), E2 coder one task to green (MVP), E3 roles complete, E4 adapters + coder hardening, E5 work scheduler | an external role does a task that coai routes, checks and keeps green |
| [4 Operator](epoch-4/epoch.yaml) | E1 AskUserQuestion hook (MVP), E2 tmux sessions via creds (MVP), E3 Claude mod, E4 local pages, E5 Features page | the person works through coai |
| [5 Reach](epoch-5/epoch.yaml) | E1 studio attachments (MVP), E2 studio roles, E3 php/rust/python/java adapters, E4 git/PR/CI takeovers | video review through frames and every listed language |

## Growth surfaces (named in the architecture)

Hub logs (50 MB per device+stream, 2 GB total, sweeper), coder worktrees (24 h after terminal, ≤ 20 live per repo),
`coder/*` branches (merged: 14 days), runner logs (20 MB each, 30 days), hub settings revisions (last 20), slot files
(one host-level directory, released on exit).

## Test plan and Definition of Done

Per epic in its `epic.yaml` (`doneWhen`) and per task (`dod`). The v2 folder itself is the first fixture of the YAML
validator (epoch 1 E3): `validate_work` must accept it.
