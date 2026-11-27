# coai v2 — plan

> Status: **plan, nothing built, 2026-11-22.** Branch `feat/v2-plan`. Built with the v2 work method itself: a feature
> folder → epochs (≤ 5 epics each, MVP first) → epics → stories → tasks, each node with a YAML file. Stories and tasks
> are written for an epic when it starts (now: epoch 1). The split was re-decided by Fable on 2026-11-22: 5 epochs, MVP epics with measurable pass criteria, 10 cornerstone stories (feature.yaml `cornerstones`; 12 since 2026-11-25, when the E3 plan gate added E3 s3 and s4).
> **Stopped for re-review, 2026-11-27** (the ledger below, entry 2.2): the plan is stopped for re-review after the second change of ledger epoch 2. No new code on any epic of this plan, and no merge of its work, until epoch 3 of the ledger is open. The revision of the whole plan is written (the record "Re-review for epoch 3" below; section S of the E3 SPEC_CHANGES); the plan round and the owner's approval are not done.

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

> Epoch 2 · approved 2026-11-26 · by owner · state: stopped

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
- 2.2 · 2026-11-27 · owner · contract, dod — a new codex model that offers a tool outside the known set does not end the job: the entry ends `model-unavailable`, the job goes to the next model, and the notice `coder-tool-unknown` names the tool; this replaces the `coder-boundary` that entry 2.1 wrote for this case; two reads of the tool list that differ stay `coder-boundary` (epoch 3 E3, SPEC_CHANGES sections Q and R; s2 t5 d4 to d6)
  (The owner's words of 2026-11-27, later the same day, as the lead gave them, in translation: "Move to the next model." It
  answers a question that the planner put after entry 2.1: a new model with a tool outside the known set ended the job, and
  the planner asked if it should fail over. The planner has the words from the lead, not in the owner's own language.)
  (This second entry stopped the plan. It is a change and not a part of entry 2.1: entry 2.1 was accepted and pushed with
  `coder-boundary` for this case, in commit ae855982, and a later decision changed that contract. An owner's request counts
  like any other change.)
  (Three more answers came with it. None is an entry, and the reason is said for each, so that nobody reads it as a way to
  keep the count low: the count is two with or without them, and the plan is stopped. First: "I accept", for risk 14 of
  the E3 SPEC. A risk that the owner accepts changes no scope, goal, Definition of Done, merge condition, epic, story, build
  order, contract or approach: entry 2.1 already launches the newest model, and nothing waited for this word. It is unlike
  the entries 1.3 and 1.15 of epoch 1, where a vendor could ship only after the owner's word. Second: the turn of an agy coder
  is cut at 30 minutes, not 45. The plan names no number: s6 t3 d3 of E3 says that the deadline fits under the life of a
  token, about one hour, and 30 minutes fits as 45 did. The number 45 is in the code, and the security verify left it to the
  owner as question (b) of its pass 5. So it is an answer inside the plan as written. Third: question 3 of section N of the
  E4 SPEC, "yes". That question was open in the plan, with the words that s5 t6 of E4 starts after the answer. If the lead
  or the owner reads one of the three as a change, it is one more entry of this stopped epoch.)
  (What the re-review must cover, by the rule of plan changes. One consultation with the entries 2.1 and 2.2 quoted. The
  WHOLE plan read again and revised against what is known now: the goal, the Definition of Done, the merge conditions, the
  epics and the build order, not a patch of the two entries. An Opus judge sorts every commit made since the approval of
  this epoch, commit 6125f2ce, on every branch that carries work of this plan, as fits or cut, in the
  re-review record for epoch 3. What contradicts the revised plan is cut by a new commit, never by a history rewrite. Then a
  plan round, and the owner's approval if the plan got narrower. The planner's view: it did. The tool list no longer has to
  be the exact 11 names, and a tool outside the set no longer ends a job. The open points that the re-review must decide
  are listed in section R of [SPEC_CHANGES.md](epoch-3/e3-roles-complete/SPEC_CHANGES.md).)
  (During the stop: no new code on any epic of this plan and no merge of a pull request that carries its work. Allowed: the
  re-review, its reverts, work outside the plan, and an incident fix that the owner orders.)

### Re-review for epoch 3

Narrowed: yes — the tool list of a codex coder no longer has to be the exact 11 names (entry 2.1), and a tool outside the known set no longer ends a job (entry 2.2); the planner's reading of SPEC_CHANGES R2, copied by the judge; the owner approves

| Work | Where | Verdict | Action |
|---|---|---|---|
| the work since the approval of epoch 2 on E3: 53 commits (47 and 6 merges), `9157dc94` to `62c6878c`, with `39229214` of feat/v2-ep3-e2-coder and the commits of feat/v2-ep3-e3-agy; one row each in RESULTS_epoch2_done_code_sorting.md | feat/v2-ep3-e3-roles, pushed | fits | kept |
| the work since the approval of epoch 2 on E4: 12 commits (10 and 2 merges), `c1cbd7c5` to `9897dc2b` | feat/v2-ep3-e4-plan, pushed | fits | kept |
| `62685e2b`, in part: a codex tool outside the known set ends the job `coder-boundary` (`CodexToolList.UnknownToolFailsOver = false`) with ONE `coder-tool-unknown` notice for the job; that is entry 2.1, which entry 2.2 replaced | feat/v2-ep3-e3-roles, local, never pushed | cut | local: may be dropped; or a new commit on top, before it is pushed, sets the constant to true, gives one notice for each refused entry and adds the tests of s2 t5 d4 to d6 |
| `62685e2b`, the rest: E3 s2 t5 under entry 2.1 (the families, the shape rule, `model-unavailable`, the known set, the tool name in the notice) | feat/v2-ep3-e3-roles, local, never pushed | fits | kept as local work; it goes on only when epoch 3 is open |
| `dee4b684`: E4 s5 t6, this repository's `testFiles` and `testFor` (the owner's answer to question 3 of E4 SPEC N) | wip/e4-s5-t6-plan-stop-20261127, local, never pushed | fits | kept as local work; it goes on only when epoch 3 is open |
  (Step 2 of the rule, the halt: deviation, recorded. Three commits were made after the stop commit `62c6878c` of 16:08:31.
  On E4, `9897dc2b` at 16:08:44, pushed: plan text only, the owner's answer to question 3 of E4 SPEC N; its message names
  the stop; it is on a branch that does not hold the stop commit. On E3, `62685e2b` at 16:10:38, local: product code of s2 t5,
  committed "at the plan stop"; R3 says the rest of s2 t5 waits. On E4, `dee4b684` at 16:18:34, local branch: the file and
  tests of E4 s5 t6, which R3 names as waiting; the same diff is also uncommitted in the E4 worktree, written at 16:12:25.)
  (Seen, not sorted, for the planner: `02fb2b36` at 16:18:13 on fix/claude-haiku-alias-in-plan-mode, pushed, one commit on
  origin/main. It sends the claude alias haiku as its full id in plan mode. It fixes what the paid proof of E3 s8 t3 found
  (the alias ran Sonnet 5.5). It is on the main line, not on a branch of this plan. The halt allows work outside the plan.
  If the planner counts it as work of this plan, it is a fourth deviation.)
  (The security verify fixes are sorted as fits by their must-change or should of the verify. Whether the changes of
  SPEC_CHANGES section P are ledger entries is an open point of section R; this sorting does not decide it.)
  (Carried, not cut. First: `src_mcp/src/Tools.cs:583` still says that neither codex nor agy is launchable today; the flips
  `85134aec` and `b9df00dc` did not change it, and s7 t4 d3 asks the tool text to name the vendors that can be launched; one
  line, after the stop. Second: `AgyCoderVendor.TurnDeadline` is 45 minutes; the owner's 30 minutes waits for the end of the
  stop (R3). Third: the default codex name `terra` still cannot be launched; s2 t5 is the fix. Fourth: the proof of E3 s8 t3
  ran on Sonnet 5.5 and not on haiku, because the CLI maps the alias in plan mode; s8 t3 d1 asks haiku. Fifth: the 5 paid
  agy turns of `4466732e`: passes 7 and 8 of the verify recommended them and left their number to the owner; the record holds
  no owner's word on the number; no cap was broken, because the 30 USD of entry 1.11 names s8 t3 and t4 only.)
  (The Narrowed line, the table and the four notes above are the record of the Opus judge, read only, moved here word for
  word on 2026-11-27. Its file is [RESULTS_epoch2_done_code_sorting.md](epoch-3/RESULTS_epoch2_done_code_sorting.md),
  copied unchanged, with one row for each of the 67 commits. Of 67 commits 66 fit and one, local and never pushed, is cut
  in part. No pushed commit is cut. The judge read the plan at 62c6878c, before the revision.)
  (The planner on the points that the judge left to the planner or to the owner, with facts that the lead gave on
  2026-11-27. First: the commit 02fb2b36 is on a branch from main, for the released product, and the owner ordered it. It
  is work outside the plan, which the halt allows. It is not a deviation. Second: the 5 paid agy turns of 4466732e. The
  owner's cap for the agy measures is 30 turns (2026-11-26; entry 1.2), 16 of the 30 are used, and the lead chose the
  number inside that cap. The open point is closed. Third: the three deviations of the halt stand as the judge wrote
  them. 9897dc2b is plan text. 62685e2b and dee4b684 are local, are not pushed, and go on only when epoch 3 is open.)
  (The cut. The part of 62685e2b that ends a job coder-boundary for a tool outside the known set, and its text of ONE
  notice for the job, contradict entry 2.2. Action: fix forward by a new commit on top of it, when epoch 3 is open and
  before any push. No pushed history is touched. Against the revision the same commit also lacks four rules that are
  not built yet, which is not a cut: the lookup of every codex name that is not a family, one launch of a model in a
  job, the tool inventory read whole, and the terminal rule after a turn; they are items 5, 6, 8 and 12 of section S4.)
  (The revision, step 4 of the rule: commit c59b3a51, in section S of
  [SPEC_CHANGES.md](epoch-3/e3-roles-complete/SPEC_CHANGES.md). The goal, the Definition of Done, the merge conditions, the
  epics and the build order were read again; each of the nine open points of section R has its disposition; the three
  answers of the owner that are no entries are folded in. What the revision changes is the baseline of epoch 3 and is
  not an entry. The consultation of step 3: id aee9490c9c834b51a284184a36a53427, one turn, advice only; each of its
  points was checked by reading the code before it was used. The bundle for the plan round: commit c730151a.)
  (What the whole reading found for the parts of this file. The goal holds. The owner's decisions of 2026-11-22 hold. The
  five epochs and the 24 epics hold: none is added, removed or moved; one doneWhen line of epoch 4 e4-local-pages came with
  entry 2.1. The build order of the epochs holds. Inside epoch 3 E3 the build order has one new step, the task s2 t5 after
  the story s5. The status paragraph of this file still says "plan, nothing built, 2026-11-22": the planner did not read
  the epochs 1, 2, 4 and 5 against their code, so it is not rewritten.)
  (A deviation of the count, recorded. Two changes of the security verify of 2026-11-27 are contract changes by the rule,
  and they were not written as entries on their day: after a turn a host-read entry that is not a plain file, and a turn
  check that takes more than 30 seconds, end a job coder-boundary; and the stop word checks-changed-tree got a second
  meaning. They are in section P of the same file. With them counted, the stop was due at entry 2.1. They are part of the
  baseline of epoch 3. Whether they become entries is the lead's decision.)
  (Carried into epoch 3, not cut, from the judge's list and from the lead. The tool text at src_mcp/src/Tools.cs:583
  still says that codex and agy are not launchable: one line, E3 s7 t4 d3. The turn of an agy coder is still cut at 45
  minutes in the code: E3 s6 t3 d3 now names 30. The default codex name terra cannot be launched until E3 s2 t5 is built.
  The proof of E3 s8 t3 ran on Sonnet and not on haiku: the fix of the alias is 02fb2b36 on the main line, and s8 t3 d1
  is met only by a run after it. Three findings of the local E4 commit dee4b684 on the selection of tests are in
  section S of the E4 SPEC, on feat/v2-ep3-e4-plan, commit 3995acc6: the revision corrected one task text and added two
  dod items to E4 s5 t6.)
  (Not done when this was written: the plan round, and the owner's approval, which a narrower plan needs. The line of
  epoch 3 is not written, and the plan stays stopped.)

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
