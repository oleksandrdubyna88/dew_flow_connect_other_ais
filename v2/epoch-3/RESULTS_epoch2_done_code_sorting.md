# Ledger epoch 2: sorting the done work (plan-change-control, step 5)

Judge: Opus, read only, 2026-11-27. No edit, commit, push or build in the repositories.

Plan read: `v2/PLAN.md` at `62c6878c` (the ledger: epoch 1 closed, epoch 2 with entries 2.1 and 2.2, state stopped; the
record `### Re-review for epoch 2`); `v2/epoch-3/e3-roles-complete/SPEC_CHANGES.md` sections M to R; the E3 task files at
`62c6878c`; `RESULTS_e3_s7_t3_security_verify.md` passes 1 to 10 (P7.9, P8.11, P10.10 for the flips); the E4 task titles and
`RESULTS_e4_s5_security_verify.md` at `9897dc2b`; `v2/epoch-3/RESULTS_epoch1_done_code_sorting.md` for the shape.
Rule: `common.plan-change-control` (sha256 d890c388…), read in full through the resolver of the repository.

Approval of epoch 2: `6125f2ce`, 2026-11-26 14:48:34 +0100. The stop: `62c6878c`, 2026-11-27 16:08:31 +0100.

Scope, after `git fetch` (remote tips checked again with `git ls-remote` at the end; unchanged):

- `feat/v2-ep3-e3-roles`: `git log 6125f2ce..origin/feat/v2-ep3-e3-roles` = **53 commits** (47 + 6 merges). They hold
  `39229214` of `feat/v2-ep3-e2-coder` (merged by `9852b959`) and every commit of `feat/v2-ep3-e3-agy` (tip `fe7b5a64` is on E3).
  Plus the LOCAL commit `62685e2b`. It stands on `62c6878c`, not on `ae855982`.
- `feat/v2-ep3-e4-plan`: `origin/feat/v2-ep3-e3-roles..origin/feat/v2-ep3-e4-plan` holds 49 commits, but only **12** are after
  the approval (10 + 2 merges, `c1cbd7c5` to `9897dc2b`). The other 37 are from 2026-11-25 and 2026-11-26 before 14:48 and were
  sorted in the epoch 1 record. Plus the LOCAL commit `dee4b684` on the local branch `wip/e4-s5-t6-plan-stop-20261127`.
- No commit after the approval on `feat/v2-ep3-e3-measure` (tip `d0dca16d`, 13:30, merged as `a9187e76` before `6125f2ce`),
  `feat/v2-ep3-e1-role-jobs`, `feat/v2-ep3-e2-s3`, `feat/v2-ep3-e2-s7-keypool`, `feat/v2-ep3-e2-t4`. No pull request of this plan
  was merged.
- Each merge was read with `git show --remerge-diff`: every conflict resolution only joins the two sides. No merge adds content.

Sorted: **67 commits**. **66 fit. 1 is cut in part** (`62685e2b`, local). **3 commits were made during the stop.**

---

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

---

## The sorting, one table per branch

The `Serves` column names the approved task (story and task of E3 or E4, with its dod items), the owner decision (ledger
entry) or the security verify finding (pass and number). "M" is a must-change, "S" a should. A should of a verify is a
verify finding, so it is not a cut; it is named as a should.

### feat/v2-ep3-e3-roles (53, pushed)

| Commit | Message head | Sort | Serves |
|---|---|---|---|
| 9157dc94 | docs(v2): the bundle names the owner's acceptance (6125f2ce) | fits | wording repair after the approval: a repaired reference and "not merged yet" gone (not a change by the rule) |
| 0f9813ee | docs(v2): the re-review record for epoch 2 is completed with the judge's sorting | fits | step 5 of the epoch 2 re-review: the epoch 1 judge's file, copied unchanged |
| 494fc0d9 | feat(mcp): the pack format version is the first line of the approval id | fits | entry 1.10 (F1), E3 s3 t4 d6 |
| e1774e78 | feat(mcp): the record of the instruction step writes its argv and stderr tail through the redactor | fits | entry 1.9 (F0), E3 s7 t3 d7 |
| 6ebdbf42 | feat(mcp): a coder vendor is launchable only when its verdict is ship AND its row carries the mark launchable | fits | entry 1.16, E3 s7 t1 d3 and d5; it is the fix forward of the epoch 2 cut of `eabcbea1` |
| 514aa1c1 | feat(mcp): the sign-in of a coder vendor is a copy made for the job | fits | E3 s7 t6 d1 to d6 (judgement S3, S4, K3); the notice `sign-in-file-changed` is d4 |
| 7a37d719 | test(mcp): the scan of every place that parses a CLI release or says update the CLI | fits | entry 1.8, E3 s7 t1 d6 |
| 3c6f209a | docs(v2): E3 s7 t6 security verify, pass 4 | fits | the record of the security verify |
| 78f11b88 | feat(mcp): the agy coder's capability asks only what the installed CLI declares | fits | E3 s6 t1 d1, d2, d4 (V6, entry 1.8). Read: the release is only recorded; no compare of versions |
| f2eef963 | fix(mcp): fourth security verify of s7 t6 — M8, M9, M10, C3 | fits | verify pass 4: M8, M9, M10 and condition C3 |
| 03965413 | feat(mcp): CodexWriteCapability | fits | E3 s5 t1 d1, d2 (V6, entry 1.8). Read: the release is only recorded |
| a2e4b9a1 | feat(mcp): the agy coder's write guard | fits | E3 s6 t2 d1 to d5 (V15, G2 to G5, G15 of entry 1.16) |
| f999bc1c | Merge origin/feat/v2-ep3-e3-roles (pass-4 fixes) | fits | a merge that brings only sorted commits |
| f29df8a6 | fix(mcp): the guard folder's sweep deletes through the shared deleter | fits | E3 s6 t2; the repository's scan of product deleters asked it |
| 367ed703 | feat(mcp): the codex coder launch — CodexCoderVendor | fits | E3 s5 t2 d1 to d5, s5 t1 d3, s7 t6 d5 (the mark of a printed copy token), C3 item 2. Read: the consultant's coder branch is the road of s5 t2; the reviewer argv is unchanged |
| b3278f1f | feat(mcp): the agy coder - the launch, the preflight rows, the check of every turn's stream | fits | E3 s6 t3, s6 t4, s6 t5 (G15, entry 1.16). The 45-minute turn cap was inside s6 t3 d3 when it was written (carried: the owner's 30 minutes waits, R3) |
| 4bfde22a | test(mcp): the mark launchable is off for agy in the product's table | fits | E3 s7 t1 d5, s8 t6 d4 (the mark stays off until the flip) |
| ee1271b7 | Merge origin/feat/v2-ep3-e3-roles (the codex coder launch) into the agy coder | fits | a merge that brings only sorted commits |
| 46b78dcc | feat(mcp): the codex coder's preflight | fits | E3 s5 t3 d1 to d5 (V9, V10); the exemption lines follow the test-leftovers rule |
| 20fb7b6f | feat(mcp): the codex turn check and the fix turns | fits | E3 s5 t4 d1 to d4 (CX-S1, CX-T4, CX-T5, decision 40) |
| 424222fc | test(mcp): boundary scenarios with FakeCli as the codex coder | fits | E3 s5 t5 d1 to d3 and the test of d4 (V7, V8) |
| c7f2e259 | fix(mcp): pass 5 of the agy coder - M11 to M14 | fits | verify pass 5: M11, M12, M13, M14 |
| 209ec300 | Merge origin/feat/v2-ep3-e3-agy (pass 5) into the codex coder | fits | a merge that brings only sorted commits |
| 39229214 | fix(mcp): the host never writes a worktree marker through a link | fits | verify pass 6: M15 (made on feat/v2-ep3-e2-coder, brought in by 9852b959) |
| 9852b959 | Merge origin/feat/v2-ep3-e2-coder into feat/v2-ep3-e3-roles | fits | a merge that brings only 39229214 |
| 2bb2fbe4 | fix(mcp): pass 6 of the agy coder - M11 leg b, R-M15 | fits | verify pass 6: M11 (b), R-M15 |
| 62617190 | fix(mcp): pass 6 of the codex coder - M16, M17, M11a, and S39 to S49 | fits | verify pass 6: M16, M17, M11 (a); the shoulds S39, S40, S42 to S47, S49 |
| c438f290 | Merge origin/feat/v2-ep3-e3-agy into feat/v2-ep3-e3-roles | fits | a merge that brings only sorted commits |
| 0c9538e7 | test(mcp): the codex rig scratch follows the per-vendor job temp | fits | verify pass 6: R-M15 and the read arm of M17 |
| 7fc54373 | test(mcp): the list the flip rests on holds the must-changes of passes 6 and 7 | fits | verify pass 7: S57 |
| 1500d721 | test(mcp): the real codex through the coder road - a gated harness; real preflight 28 of 28, 0 calls | fits | verify P7.9, codex owed item 1 (one real preflight, 0 paid calls) |
| f9b72f8a | fix(mcp): pass 7 of the agy coder - M18 | fits | verify pass 7: M18; the gated real-run test is for P7.9, agy owed item 2 (0 paid calls) |
| 1221e2f6 | feat(mcp): gpt-6.1-terra is a measured codex name | fits | it let the 2 calls of P7.9 run on the model whose tool list section 13.3 measured. No task or owner word asked it when it was made (04:26, before entry 2.1). It fits because entry 2.1 accepts the same name (SPEC_CHANGES Q1, rule (c) and item 5), and the key `measured` is history (entry 1.8). It is redundant after s2 t5, not wrong |
| 19894ccb | docs(results): real codex through the coder road - two paid calls with gpt-6.1-terra | fits | verify P7.9: "recommended, 2 paid calls (7 of 30 left)", inside the 30 codex calls of entry 1.2 |
| 85134aec | feat(mcp): the flip - the codex coder is launchable | fits | E3 s5 t5 d4, entry 1.16; P7.9 owed items 1 to 3 were done (1500d721, 7fc54373, this commit) |
| aa177f5d | fix(mcp): the agy sign-in row reads an expiry with a plus offset; the gated real-run test; the results of the two real runs | fits | P7.9 agy owed item 2 and the recommended real preflight (0 paid calls); the fix is a defect of E3 s6 t4 that the real run found |
| e0b07cd2 | test(mcp): the tests that held the product route to claude alone now say what the flip made true | fits | E3 s7 t4 d2, s8 t1 d1 (the tests follow the codex flip) |
| 0cef12ec | Merge origin/feat/v2-ep3-e3-roles into feat/v2-ep3-e3-agy | fits | a merge that brings only sorted commits |
| 7d118804 | fix(mcp): pass 8 of the agy coder - M19, S55, S61, S62 | fits | verify pass 8: M19; the shoulds S55, S61, S62 |
| b9df00dc | feat(mcp): the agy flip - the mark launchable of the agy row is on | fits | E3 s8 t6 d4, entry 1.16; P8.11 owed items 1 and 2. The gated real-jobs harness is for the turns P8.11 recommended |
| 4466732e | docs(results): three real agy jobs through the product's road - 5 paid turns | fits | verify P8.11: "recommended … 3 jobs (about 6 turns)". Open point (carried): the verify left the number to the owner, and the record holds no owner's word |
| 5c6cfbe1 | fix(mcp): pass 9 of the agy coder - M20, S66 to S69 | fits | verify pass 9: M20; the shoulds S66 to S69. Read: the changes in core/Work are the M20 road for the toolchain file |
| b98eaeff | fix(mcp): pass 10 shoulds - S74, S73, S72, S26, R-S7, S54, S77, S69 part 1, M20 in both flip lists | fits | verify P10.10, "should be done before E3 is merged": S72 to S75, S77, S26, R-S7, S54 |
| 45306ef8 | docs: S36, S71, R-S10, S75 | fits | verify P10.10: S36, S71, R-S10, S75. It writes SPEC_CHANGES section P (whether P holds plan changes is open point R) |
| fe7b5a64 | docs(results): the security verify of E3, passes 5 to 10 | fits | the record of the security verify |
| f1ce790f | test(mcp): E3 s8 t2 - the epic acceptance on the real coai-mcp over stdio with FakeCli | fits | E3 s8 t2 d1 to d4 |
| 63d05b89 | test(mcp): E3 s8 t6 - boundary scenarios with FakeCli as the agy coder | fits | E3 s8 t6 d1 to d3 (V7, V8) |
| d999dbc5 | docs: E3 s8 t5 - the roles, review_plan over a folder, the codex and agy coder | fits | E3 s8 t5 d1 to d3 (d2 says that no CLI version gates anything, entry 1.8) |
| f5ab92eb | docs(plans): E3 s8 todo | fits | E3 s8: the record of how the dod was read |
| ddab5102 | test(mcp): the comment of the fake's steering-path helper names no vendor | fits | E3 s7 t2 d4 (the seam test holds CoderVerbs.cs vendor-neutral) |
| ae855982 | docs(v2): epoch 3 E3 - ledger entry 2.1 | fits | entry 2.1 (the owner): the first change of epoch 2, allowed and recorded |
| 0916fe69 | docs(plans): E3 s8 t3 - the real scout job and the real judge job, 0.1714 USD | fits | E3 s8 t3 d1 to d3 (entries 1.2 and 1.11: the reservation before each call). Carried: the alias ran Sonnet 5.5, not haiku |
| 62c6878c | docs(v2): the plan is stopped for re-review - ledger entry 2.2 | fits | entry 2.2 (the owner); step 1 of the rule: the stop in one commit |
| 62685e2b | wip(mcp): s2 t5 at the plan stop of 2026-11-27 (not pushed) — LOCAL | cut, in part | E3 s2 t5 (entry 2.1) fits. Cut: `CodexToolList.UnknownToolFailsOver = false` (a tool outside the set ends the job `coder-boundary`) and the notice text "ONE notice for the job"; entry 2.2 asks `model-unavailable`, the next model, and one notice for each refused entry. **Deviation**: committed at 16:10:38, after the stop |

### feat/v2-ep3-e4-plan (12 after the approval, pushed)

| Commit | Message head | Sort | Serves |
|---|---|---|---|
| c1cbd7c5 | docs(mcp): E4 - the text that still described refusals the code no longer makes now says 'reported, never refused' | fits | the carried item of the epoch 2 record ("the next E4 commit fixes them"); entry 1.7. Comments and one JSON comment only |
| 385e95c4 | feat(mcp): E4 s4 - failure digests from recorded real output | fits | E4 s4 t1 to t5 |
| 46290f86 | feat(mcp): E4 s5 - the formatter turn, selection level two, the rule and the one clean re-run | fits | E4 s5 t1 to t5, with entry 1.12 (F0: the re-run must be clean) and entry 1.14 (F2: the format step in its own unit) |
| 1e4e3cbe | merge: origin/feat/v2-ep3-e3-roles (6ebdbf42) into the E4 branch | fits | a merge that brings only sorted commits; it carries the removal of the claude version floor into E4 (the carried item of the epoch 2 record, entry 1.8) |
| 3e235fe1 | docs(plans): E4 s5 security verify results | fits | the record of the E4 s5 security verify |
| 23e25947 | fix(mcp): E4 s5 security verify - M1 to M4, S1, S3, S4, S5, S8 | fits | E4 s5 verify: M1 to M4; the shoulds S1, S3, S4, S5, S8; four test fixes that the E3 merge needed |
| fcb17946 | docs(plans): E4 s5 security verify pass 2 | fits | the record of the E4 s5 security verify |
| a15eef5e | fix(mcp): E4 s5 security verify pass 2 - M5, S9 to S12; the node stem note in SPEC E | fits | E4 s5 verify pass 2: M5; S9 to S12. The two SPEC lines say what the stem rule already does; no plan change |
| 359e4285 | Merge origin/feat/v2-ep3-e3-roles into feat/v2-ep3-e4-plan | fits | a merge that brings only sorted commits |
| 1202937b | fix(coder): the join of E3 and E4 — the toolchain-file look runs before git clean | fits | E3 verify pass 9 M20, kept true on the merged code (the clean removed the FIFO before the look) |
| bab8e473 | test(coder): S74 follows the head's own definitions | fits | E3 verify pass 10 S74, under E4's rule that the head's own definitions run (entry 1.7) |
| 9897dc2b | docs(v2): epoch 3 E4 - the owner answered question 3 of section N | fits | the owner's answer to an open question of the E4 plan (not an entry, SPEC_CHANGES R). Plan text only. **Deviation**: committed at 16:08:44, 13 seconds after the stop, on a branch without the stop commit |
| dee4b684 | wip: E4 s5 t6 at the plan stop of 2026-11-27 (not pushed) — LOCAL, branch wip/e4-s5-t6-plan-stop-20261127 | fits | E4 s5 t6 d1 and the owner's answer to question 3. **Deviation**: committed at 16:18:34, after the stop; R3 says s5 t6 waits |

---

## The cuts, and the smallest action that removes each

1. **`62685e2b`, in part** (E3, local, never pushed). The part: `src_mcp/runners/Consultation/CodexToolList.cs`,
   `internal const bool UnknownToolFailsOver = false;` and its remarks ("refuses the launch (`coder-boundary`)"), and in
   `src_mcp/core/Notices/ServerNoticeCodes.cs` the summary of `CoderToolUnknown` ("ONE notice for the job"). Entry 2.2 asks:
   the entry ends `model-unavailable`, the job goes to the next model, and one notice `coder-tool-unknown` for each refused
   entry; two reads that differ stay `coder-boundary`.
   Smallest action: the commit is local, so the rule allows to drop it. If it is kept as the base of s2 t5: when epoch 3 is
   open, ONE new commit on top of it, before any push, sets the constant to `true`, changes the remarks and the notice
   summary, and adds the tests of s2 t5 d4 to d6. No pushed history is touched either way.

No pushed commit is cut. No merged pull request is affected.

## Deviations (commits made during the stop, after `62c6878c` at 16:08:31 +0100)

1. **`9897dc2b`**, E4, pushed, 16:08:44. Plan text only (the owner's answer to question 3 of E4 SPEC N). The message names
   the stop. The branch does not hold the stop commit. Sort: fits.
2. **`62685e2b`**, E3, local, 16:10:38. Product code and tests of E3 s2 t5, 31 files. R3 says the rest of s2 t5 waits. Sort:
   cut in part (above).
3. **`dee4b684`**, E4, local branch `wip/e4-s5-t6-plan-stop-20261127`, 16:18:34. `.coai/toolchain.yaml` and one test class
   of E4 s5 t6. R3 says s5 t6 waits. Sort: fits. The same diff (same patch id) is also uncommitted in
   `/home/jinx/git/coai-v2-ep3e4`, files written at 16:12:25.

Seen and not sorted (outside the branches of this plan; for the planner): **`02fb2b36`**, `fix/claude-haiku-alias-in-plan-mode`,
pushed, 16:18:13, one commit on `origin/main`: the claude alias haiku is sent as its full id in plan mode. It fixes what the
paid proof of E3 s8 t3 found. The halt allows work outside the plan. If the planner counts it as work of this plan, it is a
fourth deviation.

Note: another session worked in `/home/jinx/git/coai-v2-ep3e4` while this sorting ran (its index changed between two reads).
The judge did not touch it.
