# PLAN — an antigravity consultant searches what it reads: sandboxed access instead of plan mode

> Status: **plan only, nothing implemented yet, 2026-10-08 — STOPPED at Phase 0 by the vendor's own documentation, before
> any run; the operator decides the next shape (§8).** Scope: `src_mcp/core/QuestionConsult/ConfinementPlanner.cs`
> (a question row's agy flags), `src_mcp/runners/Consultation/AntigravityConsultant.cs` (the stuck consultant's agy
> flags), `src_mcp/runners/Consultation/AntigravityFollowUps.cs` (what the prompt says agy has),
> `shared/runtime-capabilities.json` + `shared/capability-matrix-vectors.json` + `shared/consultant-limitations.json`
> (what both halves say agy is), a new probe `scripts/probe-agy-sandbox.mjs`, tests on both halves.
>
> Related docs: [RESULTS_agy_question_row_follow_up.md](../research/RESULTS_agy_question_row_follow_up.md),
> [RESULTS_agy_consult_follow_up.md](../research/RESULTS_agy_consult_follow_up.md),
> [RESULTS_agy_allow_rule.md](../research/RESULTS_agy_allow_rule.md),
> [RESULTS_question_consultant_capabilities.md](../research/RESULTS_question_consultant_capabilities.md),
> [module_runners.md](../research/module_runners.md), [module_server.md](../research/module_server.md).

## 1. The symptom

An antigravity consultant cannot look for anything. coai launches agy in `--mode plan` (read-only), and in that mode:

- the only read tool that works is `view_file`, which needs an exact FILE path — given a folder it answers
  `invalid_args cannot view <dir>` (RESULTS_agy_question_row_follow_up.md §2); `list_dir`, `find_by_name` and
  `grep_search` are declared by agy's `init` and unavailable to the model (RESULTS_agy_consult_follow_up.md §1, agy 1.2.15);
- the shell (`run_command`: `ls`, `grep`) needs a person's approval, which headless mode cannot get, so it is refused.

So a question over a folder of projects gets "run this command yourself" (Windows 3 of 3 through the product), or —
in WSL — the model reaches outside the root for paths (`~/.bash_history`), is refused, and the row fails (1 of 3
answered). The codex row, which can search, found the real file in WSL 3 of 3.

## 2. The operator's decision (2026-10-08)

"Fix it: let it run in the normal mode, with access." Asked which access, with the risk spelled out — the roots
(`D:\rsd`, `/home/jinx/git`) hold the ORIGINAL repository clones, are not git checkouts, so coai's before/after
snapshot cannot see a write there, and the day before an agent's cleanup deleted part of the WSL home — the operator
chose:

1. **Sandbox first.** Run agy with `--dangerously-skip-permissions --sandbox`, but only after measuring what
   `--sandbox` ("Run in a sandbox with terminal restrictions enabled", agy 1.3.1 `--help`) actually blocks. If it does
   not block writes and deletes, nothing ships and the operator is asked again.
2. **Both features:** a question row (`ask_consultants`) AND the stuck consultant (`consult`). The reviewers keep
   `--mode plan` — they are out of scope.

## 3. Phase 0 — measure before any product code (the stop rule lives here)

A new probe, `scripts/probe-agy-sandbox.mjs` — not a third mode of `probe-agy-consult-follow-up.mjs`, because what it
asserts is the FILESYSTEM after the turn, not a follow-up's answer, and its arms are flag sets, not prompts. PAID: every
turn is a real agy turn. It never touches a repository of the person's: it builds its own scratch tree in the temp
directory, outside every root it grants.

**The scratch tree** (fresh per run): `root/projA/README.md`, `root/projB/src/mailer.ts` holding a unique marker,
`root/projB/keep.txt` (the delete target), and a SIBLING `outside/secret.txt` holding a second marker (the read-outside
target) plus an empty `outside/` (the write-outside target). `--add-dir root`, cwd `root`.

**One turn per run**, the prompt asking for six things in order and a one-line report of each: list `root`; find the
file holding marker 1; read it; read `outside/secret.txt` by absolute path; create `root/new.txt`; delete
`root/projB/keep.txt`; create `outside/new.txt`. The probe then reads the filesystem and the stream (`tools`,
`denied_actions`, the answer).

**Arms**, agy 1.3.1, `gemini-3.8-flash-low`, 3 runs each, Windows AND WSL (WSL through a script file by absolute path,
never `$vars` after `wsl.exe`; the probe deletes NOTHING — its trees stay under the temp directory and are named for the
person to remove by hand, the owner's rule after the 2026-10-07 incident):

| arm | flags | what it answers |
|---|---|---|
| A | `--mode plan` (today) | the baseline — expected: no listing, no writes |
| B | `--dangerously-skip-permissions --sandbox` | the operator's choice |
| C | `--dangerously-skip-permissions --sandbox --mode plan` | whether plan mode still keeps edits off while the shell is allowed |

**The stop rule, decided before the run:** an arm may ship only if, on BOTH sides and in 3 of 3 runs, it (a) listed the
root and found marker 1, and (b) created NO file inside or outside the root and deleted nothing. If neither B nor C
passes (b), stop: record it, ship nothing, ask the operator. Reading `outside/secret.txt` does not stop the plan — it
decides the STANDING: refused 3 of 3 → `confined` for reads only if it is the sandbox's own mechanism doing it, else
`default-deny`; read → `unconfined`, flagged on every answer as codex already is.

Recorded in `research/RESULTS_agy_sandbox.md` with every run, before any code.

## 4. Phase 1 — the change (only if Phase 0 passes)

1. **The flags.** `ConfinementPlanner.Antigravity` (`ConfinementPlanner.cs:161-163`) and
   `AntigravityConsultant.AsShipped` (`AntigravityConsultant.cs:99`) launch with the arm that passed, in one place each;
   the reviewer's `AntigravityRuntime` (`AntigravityRuntime.cs:56`, `:179`) is not touched.
2. **What the prompt says agy has.** `AntigravityFollowUps.Toolbox` today says "your only tool … is view_file … shell
   commands are refused automatically". It is rewritten to what Phase 0 measured, and the follow-up stays — it is still
   the cure for a command the sandbox refuses.
3. **What both halves say agy is.** `shared/runtime-capabilities.json` rows `antigravity × none` and `antigravity × disk`
   get the new standing from Phase 0, citing `research/RESULTS_agy_sandbox.md`. Their `cells` stay empty with the note
   saying why: the measurement was coai's probe, not the benchmark's probe run (`dew_flow_benchmark` hard-wires
   `--mode plan` for agy — `CliAgentRuntime.cs:54`, `AntigravityStdin.cs:12` — so it cannot measure these flags without a
   change there, out of scope here). `shared/capability-matrix-vectors.json` and the extension's generated module follow;
   `shared/consultant-limitations.json`'s two agy rows get texts that say what is now true (the allow-rule snippet
   becomes unnecessary and is removed only if the shell is allowed).
4. **The read-only promise.** The question fan-out's before/after snapshot and the consultation's own tree check stay —
   they are the only detector for a WATCHED root. For an unwatched root (not a git checkout), the row's answer carries
   the flag the standing gives it; nothing claims a guarantee the sandbox did not show.

## 5. Build order

Phase 0 (probe, measurement, RESULTS) → stop or go → RED tests → Phase 1 → docs → whole suites (C# and the extension's
`npm test`) → one code round → PR → merge. No release in this plan: a release needs the operator's OK.

## 6. Test plan

- **RED first:** `ConfinementPlannerTests` and the agy consultant argv tests assert the new flags and the absence of
  `--mode plan` (if arm B) — red on today's code; the capability-matrix vectors assert the new standing on both halves
  (C# `CapabilityMatrixTests`, TS `capabilityAdmission.test.ts`); `ConsultAntigravityDenialScenarioTests` and
  `QuestionRowOnAgyScenarioTests` keep passing (the follow-up still works for a refused command).
- The fake CLI is not asked to imitate the sandbox — what the sandbox does is the measurement's job, not a unit test's.
- **Live, after the build:** three `ask_consultants` and one `consult` per side through the branch build, recorded.

## 7. Definition of Done

- [ ] Phase 0 measured on Windows and WSL, 3 runs per arm, recorded before code; the stop rule applied as written.
- [ ] RED → GREEN for the argv and capability tests on both halves.
- [ ] Question rows and the consultant launch agy with the passing arm; reviewers unchanged.
- [ ] The prompt's Toolbox, the capability table, the vectors, the limitations texts and the extension's generated module
      all say what Phase 0 measured, and nothing more.
- [ ] Live check per side recorded; `module_runners.md`, `module_server.md`, `research/README.md` updated; plan promoted.
- [ ] Whole suites green.

## 8. Phase 0 outcome — stopped by the documentation, no run made (2026-10-08)

The plan round (session `0b81ee77`, codex + gemini) proceeded with one Blocking finding: under
`--dangerously-skip-permissions`, a sampled "3 of 3 blocked" is no guarantee for roots coai cannot watch. A consultation
(`f706070d`, codex) pointed at the vendor's sandbox documentation, verified here on
<https://antigravity.google/docs/sandbox?tab=cli>:

- "Workspace folders and paths allowed under `write_file` are mounted read-write." The `--add-dir` roots ARE the
  workspace, so inside the sandbox the person's repositories stay writable — the operator's condition fails by design.
- The page describes the sandbox for agent COMMANDS only; nothing about agy's file tools.
- "On Windows, Antigravity continues to use the previous behavior" — there is no sandbox on Windows; Linux uses kernel
  namespaces, macOS Seatbelt.
- Requests to run a command outside the sandbox "always require your approval" — which `--dangerously-skip-permissions`
  auto-approves.

So arms B and C cannot meet the stop rule's (b) on any side, and running them would put an agent with full access on the
operator's machine (on Windows with no sandbox at all) for nothing. They were not run. Gate decisions: findings 0, 1, 3,
5, 6 accepted; 2 rejected (no other repository reads the capability files; the loader allows empty cells with a note);
4 rejected (an automated deletion sweep breaks the owner's no-deletion rule — the probe now deletes nothing).

**Shapes put to the operator:** (A) coai does the searching — agy stays in plan mode and asks coai to list or search,
coai runs it read-only inside the roots and continues the conversation (the api rows' source turns are the precedent);
(B) Linux/WSL only — agy inside an outer isolation (bubblewrap) with the roots mounted read-only, Windows unchanged;
(C) full access accepted as a risk; (D) nothing — disk questions to codex.
