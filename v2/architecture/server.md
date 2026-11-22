# coai v2 — server half (architecture blueprint)

> Status: **blueprint, nothing built, 2026-11-22.** Written by an Opus architect from the code (read-only), report +
> addendum; key references re-checked by the main session. Consultants review it before the plan is written.

## 1. Component map

**Stays, simplified in place:** round core (`core/Rounds/RoundMachine.cs` Proceed :29, Revise :32, GoodEnough :45,
NotReviewed :48), `core/Findings/FindingSchema.cs`, Context/Feature, `runners/*` (Worktrees, Reviewers, Snapshots, Kimi,
Vibe, Acp), `SqliteMigrator`, RoundsDb, `RolePrompts.cs` (overrides first → "one prompt per role, variants are
overrides"), the agy hook (`AntigravityReadOnly.cs`). Team server stays and gains a hub mode.

**Delete:**

| What | Where |
|---|---|
| onExhausted policy + ladder | `StagePolicy`/`EscalationStep` SessionState.cs:9-37; `PanelConfig.OnExhausted/Autonomous` :87-130; `EscalationsUsed`, `HumanGate`, `HoldQuestions`, `RequestQuestions`; `PolicyOf` PanelSettings.cs:1092; `ContinueAnyway`, `CallHuman`, `Escalated`, `Ladder`, `GateHeld` in RoundMachine.cs |
| Held gates, call_human, cards | `RoundEngine.ApplyAnyHumanDecision`; `Escalations.cs` (move `AtomicJson`, `ScratchDirectory` out); `EscalationRetention.cs`; `SessionHolds.cs`; `AskHumanService.cs`; `AskUnderAuto.cs`; `EscalationBudget`; `LegacyHold.cs` kept only for the migration, deleted in 2.1 |
| consask modes, phase rule, consultId, free batches | `AskGateDesk.cs`, `QuestionPhaseStore.cs`, `StageRelease.cs`, `Mode`/`FreeBatches` in QuestionConsultSettings.cs |
| Cadence/risk orders | `Server/Cadence/*`, `core/Cadence/*`, `CadenceArgs` in Tools.cs |
| Operator commands | `CommandsSetting.cs`, `CommandModelsSetting.cs`, `core/Commands/*`, `shared/command-models.json`, flags PanelSettings.cs:1042-1045 |
| Prompts-per-round picker | `PromptsPerRound`, `DealPlan/CodeLenses` PanelSettings.cs:1068-1070 |
| Security lane (removed, not dormant; git keeps history) | `SecurityLane*.cs`, `CheckSecurityMode.cs`, `SecurityPromptTextReadMode.cs`, `PanelConfig.SecurityLane`, `core/Security/*`, `shared/security-lane.json`, `shared/security-prompt-text-vectors.json` |
| Conventions dependency | the mount `runners/Context/RuleFiles.cs:131-137` and conventions tiers in `StageRules.cs`. **Keep** `RuleFiles.InstructionFiles` and repo-local rule folders (the repository's own rules). coai enforces no change-control, epochs ledger or cadence |

**New:** `core/Work/` (WorkPlan model + validator), `core/Roles/` (RoleTable, ComplexityBands),
`core/Coder/CoderReportSchema.cs`, `core/Toolchain/`; `src/Server/Coder/`, `src/Server/Work/` (WorkRunner, WorkGraph,
SlotBroker, Integrator), `src/Hooks/AskHookMode.cs` + `QuestionWindowStore.cs`, `src/Hub/` (HubAgent, HubSettingsCache,
LogOutbox), `src/Install/` (InstallMode, UpdateMode), `runners/Toolchain/`; `src_server/src/Hub/` (HubEndpoints,
HubStore, HubTokens, HubSweeper); `shared/work-plan.schema.json`, `work-plan-vectors.json`, `work-method.md`,
`role-defaults.json`, `coder-report.schema.json`, `toolchains.json`, `toolchain.schema.json`, `hub-contract.json`.

## 2. MCP tool surface

| Tool | Parameters | Note |
|---|---|---|
| providers | — | + capability line per row (attachments) |
| open | repoPath, branch, callerModel? | unchanged |
| review_plan | repoPath, branch, planText? \| committedPlan? \| featureFolder?, expectedCommit? | cadence args removed |
| review_code | repoPath, branch, baseRef, planText, again? | cadence args removed |
| review_document | …, attachments? | |
| review_feature | repoPath, baseRef, lessons, planPath+epics or featureFolder, again?, callerModel?, head? | |
| resolve | repoPath, branch, decisions, document?, feature? | `humanDecision` removed |
| status | repoPath, branch, document?, feature? | |
| cons / cons_close | was `consult` / `close_consult` | kind/plan/epics removed |
| consask | was `ask_consultants` | answers only, no gate |
| roles | — | main AI, role→model, complexity bands, Haiku kinds, work method, settings revision |
| start_coder / coder_status / coder_cancel | taskFile, dod, baseSha?, model?, timeoutMinutes? / jobId, maxSeconds 0–50 | |
| start_work / work_status / work_cancel | featureFolder, epoch, only?, maxParallel? / workId, maxSeconds | |
| validate_work | repoPath, featureFolder | |

No `ask_human`. Old clients: in 2.0.x `consult`, `close_consult`, `ask_consultants` are hidden aliases (note "renamed");
`ask_human` answers `ask_in_conversation` + "removed in v2"; aliases removed in 2.1. The SDK drops undeclared arguments
without an error (Tools.cs:236 comment), so removed parameters do not break old callers. `COAI_CONSULT_*` /
`COAI_QCONSULT_*` read as fallbacks for `COAI_CONS_*` / `COAI_CONSASK_*`.

`roles` → `{mainAi:{vendor,cli,version}, roles:{orchestrator,coder,scout,important,ba:{model,vendor}},
complexityBands:[{min,max,model,vendor}], haikuKinds:[…], workMethod, settingsRevision}`; Claude defaults (opus,
sonnet, sonnet, fable, fable; bands 0-2 haiku, 3-6 sonnet, 7-8 opus, 9-10 fable) in `shared/role-defaults.json` per
main-AI vendor; codex/agy rows from measured model lists only.

**Coder delegation:** worktree `git worktree add -b coder/<jobId> <root>/coai-coder-<jobId> <baseSha>`; model = explicit
→ band by task complexity → coder role default; vendor routing (a Claude model always via `claude`); write access to the
worktree only; a detached `coai-mcp --coder-run <jobId>` owns the job (`systemd-run --user --collect`, else setsid
double-fork; Windows DETACHED_PROCESS) because a stdio server dies with its client; state + heartbeat in RoundsDb
`coder_jobs` (heartbeat > 2 min = lost). Envelope built by the server: jobId, state, baseSha, headSha, branch, exitCode,
logRef, schemaValid, filesChanged (from `git diff --name-status` only), checks, fixTurns, green, flaky. Report validated
against `coder-report.schema.json` (one repair turn). FilesystemSnapshot of the main checkout before/after: a change
there fails the job. Growth: worktree removed 24 h after terminal; merged `coder/*` deleted after 14 days, unmerged
kept and listed; ≤ 20 live coder worktrees per repo (refuse, never evict); logs ≤ 20 MB each, 30 days.

**One-shot modes** (PROJECT.md list must be updated; 64 = unknown mode only): `--ask-hook` (always exit 0),
`--roles [--format json|claude-hook]`, `--validate-work`, `--install [--dry-run] [--shared]`, `--self-update`,
`--hub-enroll`, `--hub-sync`, `--coder-run`, `--work-run`, `--migrate-v2 [--apply]`, `--validate-toolchain`; from the
extension half: `--setup-clients`, `--band-json`, `--prompts-json`, `--commands-json`, `--plan-tree`. Server:
`coai-server --hub-issue-owner-token`. Removed: `--cadence`, `--security-prompt-text`, `--check-security` (now answer 64).

## 3. Question interception (`--ask-hook`)

Installer writes a Claude PreToolUse hook, matcher `AskUserQuestion`, timeout 120 s; internal deadline = timeout − 15 s.
Window state `<dataDir>/ask-hook/<sha256(session_id)>.json` `{state, reservedUtc, windowEndsUtc}` under an exclusive
lock (exactly one call wins). Window open (now < reserved + 30 min) → print nothing (native widget reaches the person).
Else reserve and run all consask rows in parallel; each returns `{answers:[{index,labels[],why}]}`, labels exactly the
offered ones; strict majority per question (multiSelect: same set); no majority or all rows failed → print nothing
(fail-open). stdout = exactly one JSON object `hookSpecificOutput{hookEventName:"PreToolUse", permissionDecision:"allow",
updatedInput:{questions, answers:{<question text>:"A, B"}}}`. Each answer is also a notification pushed to the hub.
Spike first (does the widget skip; partial answer sets). Codex/agy: no documented equivalent → out of scope for 2.0.

## 4. Hub (Team server, `COAI_HUB_ENABLED=1`, on the ionos host)

Endpoints under `/api/hub`: `POST /enroll-tokens` (owner; single-use 24 h; returns bash + pwsh lines), `POST /enroll`
(UUID, hostname, os, osUser, distro, scope user|vm-shared, version → deviceToken), `GET /devices`,
`DELETE /devices/{id}`, `GET/PUT /devices/{id}/settings` (ETag `"r<N>"`, PUT needs If-Match: 428 missing, 412 stale),
`POST /devices/{id}/applied`, `POST /devices/{id}/health`, `POST /devices/{id}/logs` (gzip NDJSON ≤ 1 MiB, idempotent
on device+stream+seq, > 5 MB/h → 429), `GET /devices/{id}/logs?stream&limit&before` (keyset, opaque `nextBefore`),
`GET/PUT /releases/desired`, `POST /rollout`.
Auth: new branch in `Auth.AuthenticateAnyAsync` (Auth.cs:129): `coai_pat_…` (owner) and `coai_dev_…` (device), 256-bit,
stored as SHA-256; owner token printed once by `--hub-issue-owner-token`; hub routes accept only hub tokens; a device
token reaches only its own device. Store `hub.db` (SqliteMigrator): devices, settings (last 20 revisions), applied,
health (latest + 7 days), logs, enroll_tokens, releases. Retention: per device+stream 50 MB / 30 days (errors 90),
whole hub 2 GB oldest-first, device unseen 90 days → stale, logs dropped, settings kept. `HubSweeper` hourly
(SessionSweeper pattern), deletes only rows it read and judged.
Device side: `<dataDir>/hub/state.json` (installationId, token 0600, or 0660 group coai when shared),
`settings.cache.json`, `cursors.json`; one agent per data folder by flock lease, polls 60 s; `--hub-sync` from a systemd
user timer when no MCP session runs. Single writer: when hub-managed only the agent writes `settings.json`; a foreign
edit is put back + error notification. Offline: cache applied, revision not reported. Logs: new RoundsDb rows after
the cursor, the notification ledger, Serilog Error+ into `hub/outbox/errors.ndjson`.

## 5. VM shared instance

Shared data folder + shared binary, **no daemon** (N stdio processes already share one data folder safely: WAL with
busy_timeout before WAL, claim files, RepositoryLock). `/opt/coai/current/coai-mcp` (root 0755); `/var/lib/coai`
(group `coai`, 2770 setgid; binary sets umask 0007 so db/-wal/-shm stay group-writable); shared vendor homes
`/var/lib/coai/homes/{claude,codex,agy}` via HOME/CODEX_HOME/CLAUDE_CONFIG_DIR (one login for all users — owner's
choice). `--install --shared` (sudo once, from the pasted line) adds interactive users to `coai` and points each
user's MCP config at the shared binary with `COAI_DATA_DIR=/var/lib/coai`. Registers once as `scope: vm-shared`.

## 6. Install and update

`--install`: finds CLIs (`ExecutablePaths.cs`; claude, codex, agy); MCP config via vendor commands (`claude mcp add-json
--scope user`, `codex mcp add`; agy path to be measured); merges into `~/.claude/settings.json` the PreToolUse
AskUserQuestion → `--ask-hook` and SessionStart → `--roles --format claude-hook` hooks (backup first, idempotent by
command path); for codex/agy one line in their global instructions ("call coai `roles` at task start").
Enrol line: `curl -fsSL https://<hub>/hub/install.sh | COAI_ENROLL=<token> sh -s -- [--shared]` (pwsh `iwr … | iex`):
download the release asset named in `releases/desired`, check sha256, unpack to `~/.local/share/coai/versions/<v>` (or
`/opt/coai/versions/<v>`), `--hub-enroll`, `--install`. Update: agent downloads, checks sha256, smoke `--version`,
swaps `current` (Linux `rename(2)`; Windows junction under the lease); keeps the last 2 versions. Windows is the only
manual step (unsigned package; SAC may block).

## 7. Migration (`--migrate-v2`, automatic at first 2.0 serve, lease + `migrations/v2.done`, idempotent, dry run)

Held sessions → hold cleared, `AwaitingResolve` + `AdvanceOnResolve`, note "good_enough — converted from a held gate by
v2" (the AI still resolves every finding). Dropped session members land in `Unknown`, scrubbed at save. Active
escalation cards → `withdrawn_v2`, moved to `escalations/archive/`; `qphase/` and cadence store archived (append-only
data); `shared/data-inventory.json` updated. Removed settings keys → `settings.v1-removed.json`; renamed keys
rewritten. Historical `call_human`/`escalated` verdicts stay readable; new schema step adds `coder_jobs`, `work_runs`,
`work_tasks`.

## 8. Work method and YAML contract

**Owner 2026-11-22: the method is configurable, per device, on the Main AI page** — a `workMethod` block in the device
document: `mvpEpics {min:1,max:2}`, `maxEpicsPerEpoch: 5`, `maxStoriesPerEpic`, `maxTasksPerStory`, `minConsultsPerEpoch`,
`minConsultsPerEpic`, complexity scale 0–10 and bands. The validator and the instructions read these values, never
constants; `shared/work-method.md` is the default text with placeholders. v2 itself is built this way.

`shared/work-method.md` (MVP in 1–2 epics first, then polish; ≤ 5 epics per epoch; more = new epoch) served as MCP
server instructions, an MCP prompt and `roles.workMethod`. `shared/work-plan.schema.json` (JSON Schema 2020-12) +
`work-plan-vectors.json` (both halves run them). Folders: `<feature>/feature.yaml`, `epoch-N/epoch.yaml`,
`<epic>/epic.yaml`, `stories/<story>/story.yaml`, `tasks/<task>/task.yaml`; free `*.md` allowed beside each.
Task: id, title, kind (enum incl. Haiku kinds), complexity 0–10, dependsOn[], parallel, files[], dod[{id,item}].
Parsed by YamlDotNet RepresentationModel (AOT-safe; spike under TreatWarningsAsErrors; JSON-in-YAML fallback).
Validator rules: folder name = id; ≤ 5 epics per epoch; 1–2 `mvp:true` epics first; every dependsOn resolves, no cycles;
parallel siblings have no dependency path and no overlapping `files`; dod not empty. `review_plan(featureFolder)` reads
from git at the branch tip, validates first (invalid → refused, no round spent), packs in topological order (≤ 256 KiB).
`review_feature(featureFolder)` takes epics from the YAML. `start_coder` picks the model by the task's band.

## 9. Parallel work scheduler

Today `BoundedScheduler.cs` has a global cap (3), a per-provider cap (2), a shared-resource cap and a rate-limit retry
ladder (PanelSettings.cs:1013-1015) — per process, so N sessions start N×3 CLIs. v2: `src/Server/Work/`.
- Graph from the validated YAML; edges from dependsOn (an epic/story dependsOn expands to all its tasks); two tasks run
  together only if both `parallel` and proven independent; ready tasks by longest remaining path, then complexity.
- Host-wide slots (flock files in `<dataDir>/slots/`, cover all OS users on a shared VM): vendor slots (the existing
  caps, now cross-process; reviewers, coders, consask rows), heavy slots (coder CLIs, builds/tests;
  `COAI_HEAVY_SLOTS=auto` = clamp(1, 3, min(cores/4, (MemAvailable − 4 GB)/3 GB)), ceiling **2 on WSL**), memory
  admission (adapter `memoryMb` + 2 GB reserve; Linux heavy jobs under `systemd-run --user -p MemoryMax=<est×1.5>`),
  `{jobs}` = 4 alone, 2 when ≥ 2 heavy jobs run.
- Integration branch `work/<feature>/epoch-N`; task worktrees from its head on `coder/<jobId>`; a task is done when green
  AND its code gate ended proceed/good_enough/not_reviewed; Integrator merges `--no-ff`; a conflict → one rebase turn in
  the same coder session (counts against the cap). A failed task blocks only its dependants; the run ends `partial` +
  notification. Detached `--work-run <workId>`; RoundsDb `work_runs`, `work_tasks`.

## 10. Build and test by coai, coder session kept

Per task: coder turn → coai applies the formatter (commit `style: format (coai)`, no AI turn) → coai runs build, then
tests selected from changed files, recording argv/exit/duration/log → on failure a digest goes to the SAME CLI session
(claude `--resume <id>` ClaudeConsultant.cs:112; codex `exec resume <thread>` with `-c sandbox_mode=workspace-write`,
started in the worktree, never `--ephemeral` — CodexConsultant.cs:14-22; agy `--conversation <id>`) → when green, one
full test pass over affected projects → code gate. Digest `[{step,file,line,code,message}]` ≤ 40 items/16 KB from
machine output (JUnit/TRX, cargo JSON, MSBuild lines, tsc), raw stdout only as fallback. Caps: `COAI_FIX_ROUNDS=3`,
stop when the same failure fingerprints repeat twice, 45 min wall clock; a test failing once is re-run once, passing →
`flaky`, not sent to the AI. `checks: "not_configured"` is never green. The scheduler starts `review_code` itself
(scope from task.yaml + DoD) → `awaiting_resolve`; the main AI resolves; accepted findings → fix turn in the same coder
session → build/test → round with `again:true`.

## 11. Deterministic takeovers

Stack + affected-project detection (manifest walk at base SHA); build/test/lint/format via adapters; test selection;
diff stats (`DiffShaper`, `RoundSize`); YAML + toolchain validation; commit, branch names, PR creation (`gh pr create`,
body from task.yaml + envelope); CI wait (checks must EXIST then pass, deadline; conflicting PR = no CI; job cancelled
at ~15 min with zero steps = no runner → re-run once); coverage on changed lines (cobertura/lcov, optional); dependency
audit (JSON, only where installed); manifest/lockfile drift finding; secret scan on the diff
(`shared/credential-words.json`); `git merge-tree` dry run before integration; worktree/branch cleanup; changelog
commit collection (prose = a Haiku task); epic/size counters (validator).

## 12. Toolchain adapters

`core/Toolchain/IToolchainAdapter` (pure) + `runners/Toolchain/` (processes): `Detect(tree)`, `Commands(project)`
(restore, build, test, testFor(files), lint, format, formatCheck, audit, coverage; argv with `{jobs}` `{files}`),
`Parse(step, output|report)`, `SelectTests`, `MemoryMb`. Defaults are data in `shared/toolchains.json`; C# only for
parsers and selectors; argv, no shell unless the repo says so; minimal environment; no secret in argv.

| Stack | Detected by | Defaults | Machine output / test selection |
|---|---|---|---|
| C# | `*.slnx`/`*.sln`/`*.csproj` | `dotnet restore`; `dotnet build -c Debug -m:{jobs}`; `dotnet test` (MTP needs repo config); `dotnet format --verify-no-changes`; `dotnet list package --vulnerable --include-transitive` | MSBuild lines, TRX; reverse ProjectReference graph |
| Node/JS/TS | `package.json` (lockfile → npm/pnpm/yarn/bun) | `npm ci`; `npm run build --if-present`; `npx tsc --noEmit`; `npm test`; `npm run lint --if-present`; `prettier --check` if a devDependency; `npm audit --json` | jest/vitest JSON; `--findRelatedTests` / `vitest related` |
| PHP | `composer.json` | `composer install -n`; `php -l`; phpunit/pest `--log-junit`; phpstan if present; pint/php-cs-fixer check; `composer audit --format=json` | JUnit; `src/X.php` → `tests/**/XTest.php` |
| Rust | `Cargo.toml` (workspaces) | `cargo build -j{jobs} --message-format=json`; `cargo test`; `cargo clippy -- -D warnings`; `cargo fmt --check`; `cargo audit --json` if installed | cargo JSON; `-p <member>` |
| Python | `pyproject.toml`/`requirements*.txt`/`setup.cfg` (uv/poetry/coai-owned `.venv`, never system pip) | `pytest -q --junitxml`; `ruff check`; `ruff format --check`/black; mypy if configured; pip-audit if installed | JUnit; `pkg/mod.py` → `test_mod.py` |
| Java | `pom.xml`, `build.gradle(.kts)` (wrapper first) | `mvn -B -T{jobs} verify` / `./gradlew build`; `test`; spotless if configured | Surefire XML; `:module:test` / `-pl` |

Repo override `.coai/toolchain.yaml` (checked against `shared/toolchain.schema.json`; wins over detection; no file and
no detection = no checks = never green):

```yaml
version: 1
projects:
  - path: src_mcp
    adapter: dotnet
    build: [dotnet, build, app.slnx, -c, Debug, "-m:{jobs}"]
    test:  [./src_mcp/tests/bin/Debug/net10.0/App.Tests]
    memoryMb: 3000
  - path: src_vs_code
    adapter: node
skip: [audit, coverage]
timeouts: {build: 15m, test: 30m}
```

## 13. Studio attachments

`attachments[{path,mime,label}]` on review_document: ≤ 8 files, 20 MB each, 50 MB total; paths confined by
`DocumentReader.Canonical`; mime allow-list; staged into round scratch. `shared/runtime-capabilities.json` gains
`attachments.{image,video}` measured/unmeasured: claude (Read), agy (view_file), codex (`-i`, one measurement owed).
A provider that cannot read them is skipped with a `SkippedRole` note and shown in `providers`.

## 14. Build order (server half; epochs of ≤ 5 epics)

| Epic | Depends on | Done when |
|---|---|---|
| E1 Gate simplification + migration (MVP) | — | only proceed/good_enough/not_reviewed; deleted classes gone; tests green; `--migrate-v2` converts every fixture format exactly once |
| E2 Tool surface, renames, conventions removal, YAML contract | E1 | new tools + aliases; `validate_work`, `review_plan(featureFolder)`; no `.agents/conventions` read |
| E3 `--install` + `roles` + SessionStart hook | E2 | clean WSL box: one command → working Claude, Codex, agy MCP setup; `roles` in a new session |
| E4 Hub server mode | — (parallel with E1–E3) | endpoints + `http/` contracts, tokens, 412/428, sweeper caps; deployed on ionos |
| E5 Device agent + VM shared instance | E3, E4 | two VM users + WSL on the hub; settings round-trip with offline cache; logs flow |
| E6 Update | E5 | desired version moves every device; swap survives a kill; rollback works |
| E7 Toolchain adapters (MVP dotnet + node) + deterministic checks | E2 | golden detection/argv on fixture repos; parsers on recorded real output; override file |
| E8 Coder delegation + fix loop | E7 | session resume (claude/codex/agy); caps + no-progress stop; envelope `checks`; full pass before the gate |
| E9 Work scheduler | E8 | graph, host-wide slots, memory admission, integration branch, conflict rebase turn, clean `partial` |
| E10 `--ask-hook` (spike first) | E2 | real widget answered once; window passes to the person; fails open |
| E11 Attachments + php/rust/python/java adapters + git/PR/CI takeovers | E7 | each stack's fixture repo green end to end; CI wait follows "checks exist" |

## 15. Risks and their tests

1. A converted held session advances twice or never — fixtures of each released format migrated twice (reuse
   `HoldFromBeforeAutoFixture`): one advance, note present, no active card.
2. The hook corrupts stdout, exits 2 or overruns — real binary with FakeCli rows (hang, disagree, good): exit 0 always,
   empty stdout or one JSON object, wall time < timeout − 10 s; 10 parallel calls for one session → exactly one answered.
3. SQLite across OS users (WAL -shm permissions) — CI container, two users in group coai at umask 022, concurrent
   writers: both write, all files group-writable.
4. Two settings writers / lying cache — stale If-Match → 412; foreign edit put back + notification; hub down → cache
   applied, revision not reported.
5. Coder escapes its worktree or reports false results — FakeCli writes outside / edits the main checkout / claims
   `met:true`: filesChanged from git only, job fails on main-checkout change, bad report → `schemaValid:false`.
6. RAM overload — fake meminfo + fake heavy jobs, two scheduler processes on one data folder: never above the cap; an
   over-limit job killed by MemoryMax while others survive.
7. Wrong session resumed — FakeCli records argv/cwd: codex `exec resume <id>` from the worktree with the override,
   claude `--resume`, agy `--conversation`; no fix turn starts a new session.
8. Loop never ends — repeating failure stops after 2 turns `no_progress`; always red stops at `COAI_FIX_ROUNDS`; flaky
   re-run once, never reaches the AI.
9. Parallel tasks merge wrongly — two independent fixture tasks merge cleanly; a planted conflict → exactly one rebase turn.
10. Detection guesses wrong — six fixture repos + one monorepo: golden projects/commands; nothing detected →
    `not_configured`, never green.

**Open measurements:** partial answers in the hook; YamlDotNet under AOT; agy MCP config path; codex attachments;
codex/agy model names for role-defaults; `codex exec resume --output-schema`; MTP under default `dotnet test`;
`systemd-run --user` under this WSL distro.

## 16. Consultant review 1 (cons, codex gpt-6.2-astra, 2026-11-22) — decisions

Each point was checked in the code by the main session before it was decided.

| # | Finding | Checked | Decision |
|---|---|---|---|
| 1 | A coder branch has no approved plan: `review_code` loads the session by repo+branch (PanelService.cs:790) and refuses without `PlanProceeded` (RoundMachine.cs:230) | yes | **Accept.** E8: a task session binds to the approved plan revision of its feature (plan proceed is inherited by `coder/*` and `work/*` branches of that feature); test: two sibling tasks resolve findings independently |
| 2 | The hook gives the second question (and a tie / all-consultants-failed) to the person — "auto mode hands control to a person" | yes | **Reject.** The owner's explicit rule (item 7): first question answered by consask, later ones in 30 min go to the person. Auto mode means the GATE never waits for a person; the CLI's own questions after the first are the owner's chosen design. The `ask_human` alias reply stays. Keep: move the real-client spike before E3 installs the hook |
| 3 | A personal-token-only hub cannot boot: `Startup.Guard` throws with no Entra/Google/Local (Startup.cs:32, Program.cs:103); auth middleware is global | yes | **Accept.** Hub route group with its own authentication and startup condition (hub mode alone boots); tests: device token refused on Team routes and on another device; Team session refused on hub routes |
| 4 | The scheduler can merge two disjoint, each-green tasks whose combination fails; a conflict rebase changes the green SHA | — | **Accept.** Affected checks run on the candidate merge SHA before the integration branch advances |
| 5 | Detached worker: a green task strands at `awaiting_resolve` when the main AI disconnects; a paused worker can be marked lost while its child lives | — | **Accept.** Pending resolution persisted and listed by `work_status`; a job lease with a generation number is required before publishing a result or merging |
| 6 | Shared VM update: a user agent cannot rename in root-owned `/opt/coai`; existing db/WAL files keep old permissions | yes (design) | **Accept.** Binaries live in `/var/lib/coai/versions/<v>` with `current` there (group coai, setgid), no root needed after the first install; `--install --shared` repairs ownership/mode of existing db, -wal, -shm; test with a second UID |
| 7 | Slots are per data folder, and the RAM check races | — | **Accept.** One host-level slot directory (`/var/lib/coai/slots` or `~/.local/state/coai/slots`, not per data root); estimated bytes reserved atomically with the slot; test with two roots and no systemd user manager |
| 8 | The main-checkout snapshot cannot tell the coder's write from a person's edit (FilesystemInvariant.cs:19) | — | **Accept.** Write boundary enforced at launch (sandbox flags / cwd / read-only mounts) for the coder and its build commands; the snapshot stays as diagnostic evidence only |
| 9 | Detection at the base SHA misses a project the task adds; `git diff --name-status` without both SHAs is empty after a commit; this repo needs its MTP executable | — | **Accept.** Changes = `git diff --name-status <baseSha> <headSha>`; detect on both trees; this repository is an E7 fixture that must run a real test |
| 10 | Removing the conventions tier leaves `SubmodulePopulator` (populates every submodule, SubmodulePopulator.cs:29) and the missing-mount notice (RuleFiles.cs:360) | yes | **Accept.** E2 excludes the conventions mount from population and notices; ordinary submodules and repo-local rules stay |
| 11 | Acceptance wording: E1 must keep `revise`; YAML limits come from the device config; "2 GB" must say retained bytes vs file size (SQLite keeps freed pages) | — | **Accept.** E1 done-when names revise; validator reads configured limits (test: cap 3 with a 4-epic fixture); hub sweep measures retained bytes and db+WAL size (incremental vacuum) |
| 12 | Deleting the legacy-hold reader in 2.1 strands a direct v1 → 2.1 upgrade | — | **Accept.** The legacy reader stays until no supported upgrade path skips 2.0 |

Sound as written (consultant): the codex resume shape (CodexConsultant.cs:108, :26); the held-session migration fixture (LegacyHoldTests.cs:94).
