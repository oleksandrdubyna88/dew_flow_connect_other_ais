# Spec — epoch 3, E1 "External-role jobs + router + failover + work-plan schema + BA role" (MVP)

> Written by Fable, 2026-11-23, from v2/ and the server code at coai-v2-e3 14f944b8 (read-only). Two cornerstones:
> **A** (router + failover + job lease, stories s1 and s3) and **B** (work-plan schema + validate_work, story s2).
> Stories and tasks are in the YAML tree (generated from v2/_gen/v2-spec.json).

## 0. Decisions that change server.md (accepted by the main session, 2026-11-23)

1. **Job files are the authority; RoundsDb is a projection** (`src_mcp/src/Store/RoundsDb.cs`: "never the source of
   truth"). server.md §2 had put job state in RoundsDb.
2. **A job is `lost` only when its OS lock is free;** a stale heartbeat gives `stalled` (`EngineLease.cs` records why
   pid + heartbeat leases were rejected).
3. **The BA is read-only and returns strict JSON; coai writes the YAML.** No write sandbox exists before E2.
4. **`dependsOn` may cross epics** (v2 has `epoch-1/e4-shell-modules/s4-chat-carried` → `s1-shell-core` of e2); a
   siblings-only rule would fail the v2 fixture.

## A. Router + failover + job lease

**Job folder** `<dataDir>/roles/jobs/<jobId>/`: `request.json`, `job.json` (authority; `AtomicJson` under the short lock
`job.lock`), `lease`, `heartbeat`, `cancel`, `env.json`, `attempts/<n>/` (20 MB cap), `result.json`.

**States:** `queued → starting → running → succeeded | failed | cancelled | lost`; a model switch stays inside `running`.

**Lease + generation:** the runner holds `lease` open with `FileShare.None` (the `SessionClaim` shape; the kernel frees
it on death). A taker adds 1 to `generation`; every later write compares — not my generation → refuse, kill the child,
exit 75; compared again before a result, a terminal state, a repository write, or (E2, E5) a merge.

**Heartbeat** `{generation, utc, phase, attempt, childPid}` every 15 s. Older than 2 min with the lease held → `stalled`
(one notice). Lease free + not terminal + 30 s grace → the next `role_status` / `role_cancel` / start-up sweep takes the
lease, writes `lost`, kills the recorded child (`OrphanSweep` identity).

**Runner** `coai-mcp --role-run <jobId>`; first launcher that works (recorded): (1) Linux with a user manager:
`systemd-run --user --collect --unit coai-role-<jobId> -p MemoryMax=<COAI_ROLE_MEMORY_MB, default 4096>M -p
MemorySwapMax=0 <exe> --role-run <jobId>`; (2) else `setsid -f`; (3) Windows: `CreateProcess` with `DETACHED_PROCESS |
CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB`; breakaway denied → start without it, record `windows-in-job`
(no memory limit in E1). Environment through `env.json` (0600, allow-listed names, deleted after the read); no key in
argv or unit properties. The server does not track the runner as a child; the runner owns its CLI child.

**Tools:** `start_role(role, repoPath, taskFile | brief, model?, timeoutMinutes? = 30, workItem?)` →
`{jobId, state, route[], launcher}` (answers in ≤ 2 s); `role_status(jobId, maxSeconds 0–50)` → `{state, generation,
attempts:[{n, model, vendor, stop, reason, evidenceRef}], stalled, result}`; `role_cancel(jobId)` (tree stopped in 10 s).
Cap `COAI_ROLE_JOBS_MAX` (3; 2 on WSL): refuse, never queue.

**Router** (pure, in order): (1) `security: true`, kind `security` or complexity 10 → the `important` list (an explicit
model outside it is refused); (2) explicit model → it, then the role's list; (3) coder task → band model, then the role's
list; other roles → the role's list; (4) no duplicates; `VendorRouting.Refusal` applies; an empty list is a refusal
naming `COAI_ROLES_MODELS`. Router and validator share `ImportantRule`.

**Stops** (through `ConsultFailures.Classify`; nothing added by guess):

| Stop | Signals in the code today |
|---|---|
| limit | `RateLimit` phrases + status-code pattern; `RateLimit.Hopeless` (spent); Claude envelope `is_error` + `result` + `api_error_status` (`ClaudeRuntime.WhyItFailed`); Kimi shim names; `CooldownParser` |
| crash | `NonZeroExit` unrecognised, `TimedOut`, `Empty`, `Unparseable` after one repair, `ConversationDropped`, oom-kill |
| refusal | **none for a model refusal today.** `vendor-refused` is account / sign-in / CLI option. New: output arm `status: "refused"` + `reason`; a vendor field only if s3 t1 records one; no prose matching |

**Failover:** limit, refusal, crash → next model at once; only the last model waits (`RetryLadder`, inside the job
budget). A spent quota, a missing CLI or a lost sign-in skips the vendor's other models. Read-only roles restart from
turn 0 in a new session. Coder (E2): same worktree, from the last coai commit; no failover after a merge. None for
`cancelled`, `tree-changed`, `output-invalid`. List exhausted → `failed`, every attempt named.

**Notices:** per switch one Information line (`role job {JobId} ({Role}) switched {From} -> {To} after {Stop}:
{Reason}`) and one `role-model-switched` notice (subject `<jobId>/<n>`, names the vendor); also `role-job-failed`,
`role-job-lost`, `role-job-stalled`.

**RoundsDb step 26 `TheRoleJobs`** (appended): `role_jobs` (job_id, role, repo, work_item, task_file, state, generation,
launcher, times, final model, attempts, stop, reason, repair_turns, complexity, security); `role_attempts` (job_id,
ordinal, model, vendor, times, stop, reason, tokens). Retention: a terminal job folder goes after 30 days, 200 at most,
never while its lease is held; rows stay (~1 KB each).

**Scenarios:** A1 starter killed → job ends `succeeded`, a new server reads it; A2 two takers race → one wins, generation
+1 once; A3 stale generation → refused, file unchanged, exit 75; A4 holder killed → `lost`, child killed, one notice;
A5 holder stopped 3 min → `stalled`, never `lost`; A6 cancel (live job; dead runner); A7 declared refusal → model 2 from
the start; A8 recorded Claude 429 envelope → next model, no wait, same vendor skipped; A9 exit 1 / timeout / empty → next
model; A10 list exhausted; A11 two switches → 2 lines, 2 notices; A12 router vectors; A13 the three no-failover stops;
A14 unit shows both memory properties, an over-limit child → oom-kill → next model; A15 cap refusal; A16 retention.

**Brief (Sonnet):** copy the lock of `src_mcp/src/Server/SessionClaim.cs`; lease test first. Launch through
`IConsultantRuntime.Build` / `ReadHandle` (`ClaudeConsultant.cs` `--resume`, `CodexConsultant.cs` `exec resume`). Add one
FakeCli file mapping `--model` to stdout, stderr, exit, sleep. No phrase without a recorded fixture.

## B. Work-plan schema + validate_work

**`shared/work-plan.schema.json`** (2020-12; `$defs` feature, epoch, epic, story, task; the file name selects the def):
task `id ^t\d+-[a-z0-9-]+$`, `title`, `kind` (code, test, docs, measure, config, rename, translate, summarize,
changelog, simple-docs, security), `complexity` integer 0–10, `dependsOn[]`, `parallel`, `files[]`, `dod[{id ^d\d+$,
item}]` (≥ 1); optional `security` boolean, `status` (open, in-progress, done). Story `id ^s\d+-`, `title`,
`dependsOn[]`, optional `cornerstone`. An unknown key below the feature level is an error (a `security` typo must not pass).

**Vectors** `{files, limits, expect: [codes]}` — server only in E1 (the extension has no YAML reader).

**YAML reader:** YamlDotNet `RepresentationModel` behind `IYamlReader`; verify AOT publish with warnings as errors and no
`NoWarn`, then B12. Fallback: a strict subset reader (block maps/lists, `[]`, plain and quoted scalars, comments — what
`gen-v2.mjs` writes): plain `true`/`false` = boolean, plain integer = integer, quoted = string; anchors, tags, duplicate
keys, two documents, > 64 KiB → error.

**Rules** (limits from `RolesAnswer.Compute(...).WorkMethod`): (1) folder name = id for epoch, epic, story, task; feature
id free; the `epochs`/`epics` lists equal the folders. (2) epics ≤ `maxEpicsPerEpoch`; the first `mvpEpics.min..max` epics
are `mvp: true`, none after; stories ≤ `maxStoriesPerEpic` (8); tasks ≤ `maxTasksPerStory` (6); an epic may have no
stories, a story must have tasks. (3) `dependsOn`: a sibling; or the same kind in an epic this epic depends on; or
`<epic>/<story>`; unknown/ambiguous → error; a cycle → error with its path. (4) two `parallel: true` siblings: no
dependency path, no file overlap (equal or path-prefix, case-insensitive); a glob or a parallel task without files →
error. (5) `dod` not empty, ids unique. (6) kind `security` with `security: false` → error; the answer lists each task for
`important` with its reason. Notes only: feature `workMethod` differs from the device; a task holds a word of
`shared/credential-words.json` without `security: true`.

**Surfaces:** `coai-mcp --validate-work <feature-dir>`; tool `validate_work(repoPath, featureFolder)` (path checked as
`WorkItems.Check`; working tree; `IWorkFolder` lets E3 read git). Answer `{mode, featureFolder, valid, counts,
limits:{…, source: device|default, settingsRevision}, errors:[{code, path, node, message}], notes, important,
truncated}`; errors sorted, ≤ 200. Exit 0 valid, 1 invalid, 65 wrong arguments / no feature.yaml, 74 unreadable; never
64. Reader mode.

**Scenarios:** B1 the v2 folder is valid with default limits (environment cleared); B2 cap 3 + 4 epics →
`too-many-epics`, source `device`; B3 MVP count and order; B4 folder ≠ id at four levels; B5 unknown / ambiguous /
cross-epic ids, a cycle; B6 parallel overlap, dependency path, glob; B7 empty dod, duplicate dod id; B8 `complexity:
"5"`, 11, `parallel: yes`, key `secuirty`; B9 security and complexity 10 in `important`; B10 forbidden YAML features;
B11 exit codes, no data dir, tool JSON = one-shot JSON; B12 native and JIT give the same bytes on all vectors; B13 schema
file and validator tables agree.

**Brief:** vectors first, then the pure validator; copy the mode shape of `src_mcp/src/Roles/RolesMode.cs` and the folder
regexes of `src_mcp/src/PlanReportMode.cs`. Never weaken a rule to make v2 pass: report the file.

## C. BA role

Start: `start_role(role: "ba", repoPath, taskFile: <epic.yaml>)`; an epic that already has a story is refused. Input:
plan text (64 KiB cap), the epic.yaml, the epoch's other epic.yaml files, paths of the architecture files and of one
finished epic, the workMethod numbers, the kind list, the rule text; tools Read, Glob, Grep. Output
`shared/ba-output.schema.json`: `{status: ok|refused, reason, epic, stories:[{id, title, dependsOn, cornerstone,
tasks:[…]}], notes}` — no path; coai derives each path from the ids, so only `<epic>/stories/**/story.yaml` and
`task.yaml` are written. Repair loop: coai validates an overlay (disk + output); errors (≤ 40) go to the same CLI session;
≤ 2 repair turns; valid → files + `ba-manifest.json` `{path, sha256 of LF text, kind}`; invalid → `failed:
output-invalid`, nothing written, output kept in the job folder. Measure: `--role-delta <jobId> --at <commit>`:
`changedShare = (changed + deleted + added task files) / manifest task files`; pass below 0.20, at the commit after the
plan gate.

## D. Risks (Fable) and main-session decisions

1. No model-refusal signal exists (a refusal exits 0 with prose and reads as success) → the role output schema has an
   explicit `status: refused` arm; s3 t1 records real outputs. Not verified: a stop reason in the `claude -p` envelope.
2. Limits are per device: a plan valid on one device can be invalid on another → the answer names `source` and
   `settingsRevision`.
3. `RolesSettings.WorkMethodOf` resets ALL work-method numbers on one unknown key; an old binary drops new limits
   silently → fix in s2 t2 (an unknown key keeps the other keys).
4. Neither half has a JSON Schema engine or a YAML reader; B13 is the only guard against schema drift.
5. Failover can send repository text (also of a security task) to a second vendor (important: fable, then codex) → the
   notice names the vendor; the owner chose the list (frames/code may go to Claude, Gemini, Codex).
6. A Windows client job object can kill the "detached" runner; `wsl --terminate` ends every unit; E1 has no resume →
   s1 t1 measures; recorded as a limit.
7. The BA sets complexity itself (a 9 avoids the important model). **Decision:** known risk; guards are the plan gate and
   the < 20 % orchestrator-change measure; the credential-words note flags security text.
8. The `ba` default list holds only fable → no failover until a second model is set. **Decision:** default `ba` list
   becomes [fable, opus] (both claude CLI) in s2 t2.
