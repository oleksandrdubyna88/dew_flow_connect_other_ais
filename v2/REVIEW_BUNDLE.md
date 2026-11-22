# coai v2 — plan review bundle (generated from v2/; do not edit by hand)

# coai v2 — plan

> Status: **plan, nothing built, 2026-11-22.** Branch `feat/v2-plan`. Built with the v2 work method itself: a feature
> folder → epochs (≤ 5 epics each, MVP first) → epics → stories → tasks, each node with a YAML file. Stories and tasks
> are written for an epic when it starts (now: epoch 1). The split was re-decided by Fable on 2026-11-22: 5 epochs, MVP epics with measurable pass criteria, 10 cornerstone stories (feature.yaml `cornerstones`).

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
--scope user`, `codex mcp add`; agy path to be measured); merges into `~/.claude/settings.json` the SessionStart →
`--roles --format claude-hook` hook (backup first, idempotent by command path); the PreToolUse AskUserQuestion →
`--ask-hook` hook only from epoch 3 E4 and only after its spike passed (§ 17.5); for codex/agy one line in their global
instructions ("call coai `roles` at task start").
Enrol line (amended by § 17.1 and § 17.3 — download, verify the hub-shown sha256, then run; key single-use, not in
history): originally `curl -fsSL https://<hub>/hub/install.sh | COAI_ENROLL=<token> sh -s -- [--shared]`:
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
Validator rules: folder name = id for epoch, epic, story and task nodes (the feature id is free, § 17.7); ≤ 5 epics per epoch; 1–2 `mvp:true` epics first; every dependsOn resolves, no cycles;
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

## 17. Plan gate round 1 (review_plan, 2026-11-22) — accepted fixes

1. **Enrolment secret not left in history or environment.** The enrol key is single-use, consumed at `/enroll`, valid
   30 minutes, and the hub page shows it only once. The bash line starts with a space (`HISTCONTROL=ignorespace`) and
   passes the key on stdin to the verified script (`… | sh -s` reads it from a here-string, never `export`); the pwsh line
   reads it with `Read-Host -AsSecureString` prompt-free from the clipboard helper and removes the variable after use.
   The installer never writes the key to a log; after use it is worthless.
2. **Migration survives interruption.** `--migrate-v2` keeps a step journal `migrations/v2.journal` (one line per
   object: session id / card id / settings key, with state `planned → done`); every step is idempotent (a card already in
   `archive/` is skipped; a session already `AwaitingResolve` with the note is skipped). Test: kill and restart at each
   boundary before the completion marker; the end state equals one clean run.
3. **Bootstrap verified before it runs.** The pasted line downloads to a file and checks a SHA-256 printed on the hub
   page (trust anchor = the owner's authenticated session on the hub page, not the downloaded script), then runs it:
   `curl -fsSLo /tmp/coai-install.sh <url> && echo "<sha256>  /tmp/coai-install.sh" | sha256sum -c - && sh /tmp/coai-install.sh`
   (pwsh: `Invoke-WebRequest -OutFile`, `Get-FileHash` compare, then run). The script then verifies the release asset by
   its own pinned sha256 from `releases/desired`.
4. **No merge of an unreviewed SHA.** When a rebase or conflict resolution changes the reviewed content (tree diff of the
   reviewed SHA vs the candidate, restricted to the task's files, is not empty), the code gate runs again on the
   candidate (`again:true`) and the task advances only after its findings are resolved; checks also run on that SHA.
5. **AskUserQuestion hook only after its spike.** `--install` in epoch 1 writes the MCP config and the SessionStart
   `roles` hook only. The AskUserQuestion hook is written by the installer from epoch 3 E4, and only when the spike
   passed for the installed Claude Code version (a recorded capability); otherwise it stays off and the notification says
   why.
6. **Local outbox bounded.** `hub/outbox/` holds at most 20 MB per stream; on overflow the oldest lines are dropped and
   one `dropped: N` line is kept; delivered lines are removed after the hub acknowledges their seq; restart-safe
   (append + cursor file, fsync on rotate). Test: hub down, 30 MB of errors → outbox ≤ 20 MB + one dropped line;
   hub back → delivered, outbox empty.
7. **Folder-name rule scoped.** The rule "folder name = id" applies to epoch, epic, story and task nodes. The feature
   node's id is free (the folder is wherever the plan lives — here `v2/`, id `coai-v2`). The v2 folder is a validator
   acceptance fixture.

## 18. Plan gate round 2 (good_enough, 2026-11-22) — accepted fixes

1. **Enrolment line, final shape.** The pasted line holds no key: it downloads the script to a file, checks the
   hub-shown sha256, and runs it from the file; the script asks for the key with a hidden prompt read from `/dev/tty`
   (pwsh: `Read-Host -AsSecureString`). stdin stays free. Tested by running the exact pasted lines in bash and PowerShell.
2. **Detached work resolves itself.** A `--work-run` has its own resolver: the orchestrator-role model, started as a CLI
   turn by coai with the findings, the task scope and the rule "accept or reject each with a reason". Retry 2 times;
   a resolver that fails or times out (10 min) records the task `good_enough` with the open findings named and a
   notification. The main AI may still resolve first if connected. Test: the initiating AI disconnects before findings
   arrive; the run completes.
3. **Re-review on any unreviewed change.** The candidate SHA is compared with the reviewed SHA over the WHOLE tree, not
   the task's files; any content the code round did not see re-runs the gate (§ 17.4 widened).
4. **Conventions become optional, not removed.** coai v2 stops generating, shipping and enforcing the shared conventions
   (no snippet, no ledger/epoch/cadence enforcement). A repository that still mounts `.agents/conventions` keeps them
   read as its own instructions (the mount tier and `SubmodulePopulator` stay); epoch 1 E1 S5 changes from "remove the
   mount" to "stop shipping and enforcing". This repository keeps working with its current AGENTS.md until the
   conventions are reworked.
5. **Epoch 1 migration is local only.** It writes `<dataDir>/device-settings.json` with a local revision and the hash of
   what it migrated. Epoch 2 E2 defines and tests the first sync: create with `If-None-Match: *`; if a hub document
   exists (412), and the local document is unchanged since migration, take the hub copy; if both changed, keep both,
   apply the hub copy, save the local one as `device-settings.conflict-<utc>.json` and notify.
6. **Video review = frames.** coai accepts images only. The Studio extracts frames (it already cuts seam frames with
   ffmpeg) and sends them as attachments; epoch 4 proves "video review through extracted frames", with limits (≤ 8
   frames, png/jpg/webp) and the failure behaviour of an unreadable frame (skipped with a note).
7. **Old/new compatibility before 2.0.** Epoch 1 changes no Team-server wire field. Before the 2.0 release (epoch 1 E5):
   the `http/` contract suite runs live against the deployed Team server (0.16.x), new client ↔ old server and old
   client (extension 0.89.x) ↔ new mcp are checked; the hub (epoch 2) is a new route group that old clients never call.

## 19. External roles (owner, 2026-11-22)

"External roles" = roles relative to the main AI. Every role except the orchestrator is an MCP tool: **coder** (writes
code in its worktree — § 2 coder delegation + § 10 fix loop), **scout** (read-only research → strict JSON
`{facts:[{claim, evidence:[file:line]}], files:[]}`), **BA** (splits a plan into epochs/epics/stories/tasks; writes only
the feature folder; the result must pass `validate_work`), **important/judge** (Fable by default; review, design
decision, cornerstone spec; read-only; strict JSON verdict/findings). One tool family: `start_role(role, taskFile |
brief, repoPath, model?, timeoutMinutes?)` / `role_status(jobId, maxSeconds)` / `role_cancel(jobId)`; the role decides
the model (roles table / complexity band), the permissions and the output schema; `start_coder` is the coder case (no
separate tool). Placement in the epochs: decided with the Fable split review.

## 20. Important / responsible role and vendor failover (owner, 2026-11-22)

- The **important** role always runs the strongest model, selectable per device (e.g. `fable`, codex `astra`). The router
  sends it every task with `complexity: 10` and every security task (`security: true` in task.yaml, or kind `security`:
  crypto, authN/authZ, secrets).
- **Per-role failover:** each role has an ordered model list (e.g. important: fable → codex astra). A run that stops with
  a refusal, a spend/rate limit or an error is re-run on the next model in the list; the switch is recorded in the job
  envelope (`attempts: [{model, vendor, stop, reason}]`), logged, and raised as a notification. The other vendor's own
  policy applies to it; nothing is hidden. A list that is exhausted ends the task `failed` with every attempt named.

# coai v2 — extension half (architecture blueprint)

> Status: **blueprint, nothing built, 2026-11-22.** Written by an Opus architect from the code (read-only); key references
> re-checked by the main session. Consultants review it before the plan is written.

## 1. Layout

The package stays `src_vs_code/` (same extension id, released as 2.0). The new shell lives beside the old code; carried
modules are imported in place from `src/`, never copied.

```
src_vs_code/
  shell/entry.ts            -> dist/extension.js  (registration only; imports vscode + shell/lazy.ts + shell/ids.ts)
  shell/lazy.ts             require(join(__dirname,'modules',id+'.js')) once, memoised; timing marks
  shell/ids.ts              command / view / panel ids (constants)
  shell/measure.ts          perf marks, exported only in ExtensionMode.Test
  shell/modules/
    sidebar/  -> dist/modules/sidebar.js   coai.panel: failures, active cons/consask, last rounds, page links
    pages/    -> dist/modules/pages.js     one WebviewPanel per page id, reveal-if-open, write queue, revisions
    hub/      -> dist/modules/hub.js       hub client (token in SecretStorage, ETag/If-Match), shared/hub-contract.json
    chat/     -> dist/modules/chat.js      chat registration moved out of extension.ts; old chat*.ts imported as is
    sessions/ -> dist/modules/sessions.js  TreeView coai.sessions, creds API client, reconnect, image paste
    install/  -> dist/modules/install.js   binary install/update + `coai --setup-clients`
    migrate/  -> dist/modules/migrate.js   one-time v1 -> 2.0 migration
  pages/  sidebar.ts devices.ts mainAi.ts prompts.ts phrases.ts rounds.ts features.ts -> dist/pages/<id>.js (one IIFE each)
          kit.css (the kit's components.css, emitted once)
  scripts/bundle-entries.json   one list of bundles for build and tests (wsl_care scripts/bundle.mjs:61-71 pattern)
  scripts/entry-firewall.mjs    esbuild metafile: allow-list + byte budgets
mods/coai-band/                 Claude mod (§6)
```

CommonJS, one esbuild entry per module (the extension host loads CommonJS; ESM splitting not used). Duplicate code
costs disk, not start time.

## 2. Activation contract and measurement

Today: `activationEvents: ["onStartupFinished"]` (package.json:1480), one bundle (package.json:1440), and `activate()`
(extension.ts:134-939) starts three directory watchers (:174-181, started :369/:387/:395), the settings mirror
(:428-447), the `--features` spawn (:467), catalog/onExhausted/chat-memento migrations (:451, :472, :525), the Bugz key
offer (:477), the price book (:482), role-deletion sweep (:491), chat orphan reconcile (:587), housekeeping (:559) and
the update check (`offerUpdate` :931, network at :1891).

v2:
- Activation events: `onView:coai.panel`, `onView:coai.sessions`, implicit command events, `onWebviewPanel:coaiChat`,
  `onWebviewPanel:coai.page`, `onUri`. No `onStartupFinished`.
- `activate()` registers only: commands as thunks (`lazy('chat').then(m => m.x(...))`), view/tree providers whose
  `resolve` loads the module, panel serializers. No fs, spawn, network, globalState writes, getConfiguration, timers.
  `entryPurity.test.ts` runs the entry against a recording `vscode` fake and fails on any other call.
- Firewall: ESLint `no-restricted-imports` on shell/entry.ts, lazy.ts, ids.ts (only `vscode`, `node:path`, siblings);
  `entry-firewall.mjs` fails on any entry input outside the allow-list or reaching child_process/fs/https/net/src/**.
- Byte budgets (minified, CI): `dist/extension.js` ≤ 16 KiB; `dist/modules/sidebar.js` ≤ 96 KiB;
  `dist/pages/sidebar.js` + `kit.css` ≤ 128 KiB.
- Order: the sidebar host posts a skeleton at once; the webview posts `ready` after first paint; data follows. Only
  after the first `ready`: install (version check, `--setup-clients`), then migrate, each on `setTimeout(0)`.

Measurement: reuse `scripts/run-host.mjs` (bundles first :85/:117; real VS Code via @vscode/test-electron with own
workspace, data dir and `--user-data-dir` :110-113/:130; poison env removed :50; two launches today :141/:147). Add a
third launch `measure` (`COAI_MEASURE=1`, `out/test/host/measureScenario.js`): run `workbench.view.extension.coai`, read
the test-mode exports. M1 = module evaluation → `activate` return (≤ 50 ms own time); M2 = command → webview `ready`
acknowledgement (≤ 2000 ms). 3 cold launches, the maximum counts. Linux CI (xvfb), Windows CI; Remote-WSL by
`scripts/measure-remote-wsl.ps1` on the owner's Windows (VSIX + a tiny driver VSIX in a fresh `--extensions-dir`,
`code --remote wsl+<distro>`), result committed to `research/RESULTS_v2_start.md`.

Background work after v2: escalation watcher removed; consultation/question watchers replaced by sidebar/mod polling
while visible (5 s, stop when hidden); settings mirror removed (the device binary pulls from the hub); update check →
"update everywhere" + post-`ready` install step; `--features` and price book → server or lazy page read; old
migrations → one 2.0 migration; chat orphan sweep + housekeeping → `modules/chat`, on first chat load; pathWatch,
cliPathKeep, sidesSeen, newTags removed.

## 3. Pages (all on the kit, tested by running them; chat is the exception)

Kit gaps (checked): only 14 tags (dom.ts:19) and events click/keydown/input (dom.ts:30) — no form fields;
`createDataTable` filters by text only (dataTable.ts:117). Kit 0.3.0, its own release, each component with a story,
a fakeDom test and an axe test:
- K1 DOM contract: add `select`, `option`, `textarea`, `label`, event `change` (CHANGELOG note: consumers widen harnesses).
- K2 `createChipFilter`: facet chips with counts, OR inside a group, AND across; `DataTableView.setPredicate(fn)`.
- K3 `createTreeTable`: treegrid, expand/collapse, arrow keys, columns, group totals, lane mark.
- K4 `createFormFields` (text, number, select, toggle, textarea, validation) + `createSaveBar` (Save/Revert, "changed elsewhere").
- K5 `createEditableRows`: schema rows, add/remove/reorder.
- K6 `createTextViewer`: preformatted text, Copy, override mode (textarea), one-line copy field.

| Page | Data source | Kit | New kit |
|---|---|---|---|
| Sidebar | local `--log`, `--band-json`; hub failures | StatusMark, DataTable | — |
| Devices | hub `GET /devices`, per-device document (revision, If-Match), per-device logs | Tabs, DataTable | K3 tree, K4/K5 editor, K6 enrolment line |
| Main AI | device document: `mainAi`, `roles{orchestrator,coder,scout,important,ba}`, `complexityModels{vendor:[{min,max,model}]}`, `haikuTaskKinds[]` | Tabs | K4, K5 (0–10 covered, no overlap; server re-checks) |
| Prompts & Commands | `--prompts-json`, `--commands-json` + chat presets + `contributes.commands`; overrides → device document `promptOverrides` | Tabs, DataTable | K2 facets (gates, review, documents, cons, consask, chat, roles, coder, overridden), K6 |
| Phrases | `coai.phrases` until migrated, then device document | — | K5 |
| Rounds & notifications | `--log`, `--findings` (roundsDbRead.ts) + hub logs; notification ledger | Tabs, DataTable | K2 |
| Features (new) | `--plan-tree <feature-dir> --json` (server parses/validates YAML; the extension never parses YAML); watched while visible | StatusMark | K3 epoch→epic→story→task (status, complexity, model, parallel lane, depends-on), K2, K6 DoD |

Enrolment: single-use key, 30 min, shown once; two copy lines that download, verify the hub-shown sha256, then run (server.md § 17.1, § 17.3).
"Update everywhere": `POST /hub/rollout`, per-device progress until every device reports the new version.

Conventions dropped: remove `claudeSnippet.ts` (imports generated conventions rule texts, lines 1-4) and its readers;
remove `prepare-gate.mjs`'s conventions sources (:7, :19). The work method reaches the AI through the server's MCP
instructions and the start-of-task hook; the extension shows it only on the Features page.

## 4. Old → new mapping

Carried as is (tests keep running): chat (`chatPanel.ts`, `chatPage.ts`, `chatCommand.ts`, `chatPanels.ts`,
`chatHost.ts`, `chatStoreFile.ts`, `chatStoreImport.ts`, `chatStoreHousekeeping.ts`, `chatRestorePanel.ts`,
`chatOrphans.ts`, `chatHooks.ts`, `chatImage.ts`, `chatPresets*.ts`, `chatReturnCommand.ts`, `chatGotoCommand.ts`,
`conversationPickerCommand.ts`, `terminalHost.ts`, `skipShellHost.ts`) — extension.ts:316-320, :496-599, :701-911 cut
into `shell/modules/chat/register.ts`, no logic change; remote reader (`claudeQuestion.ts`, `remoteSsh.ts`,
`remoteScript.ts`, `remoteFrame.ts`, `remoteSessionHost.ts`); install/data (`installer.ts`, `coaiInstall.ts`,
`atomicFile.ts`, `dataDir.ts`); logic halves (`notify.ts`, `notifications*.ts`, `phrases.ts`, `phrasesEdit.ts`,
`roundsDbRead.ts`, `roundsDb.ts`, `roundsExport.ts`, `roundsCsv.ts`).

Rebuilt on the kit: `panelProvider.ts`, `catalogPage*.ts` + `settingsPanel.ts` (→ Main AI + device editor),
phrases, notifications, rounds log pages.

Dropped: `escalationWatcher.ts`, `consultationWatcher.ts`, `questionConsultWatcher.ts`, `serverSettingsSync.ts`,
`mirrorSchedule.ts`, `settingsLock.ts`, `catalogMigration*.ts`, `onExhaustedMigration*.ts`, `roleDeletion*.ts`,
`claudeSnippet.ts`, `snippetInWorkspace.ts`, `mcpBlock.ts`, `pathWatchHost.ts`, `cliPathKeepHost.ts`, `sidesSeen.ts`,
`newTags.ts`, `priceBook.ts`, `editorRedirects.ts`, `configTransferCommands.ts`, `dataCommands.ts`,
`reviewTreesCommand.ts`. Parked (in tree, not in the 2.0 bundle): `bugz*`, `bugsKeys*`, `bugsAdmin*`, `teamServerTab.ts`.

## 5. tmux sessions module and the creds API

> **Owner decision 2026-11-22 (supersedes the new-API proposal below):** use the creds **MCP that already exists**
> (`creds_list`, `creds_folders`, `creds_create`, `creds_create_folder`, `creds_open_terminal`, `creds_run`, …). The owner
> grants coai create rights on the sessions folder. coai acts as an MCP client of the creds server (or the main AI calls
> the tools); no new creds command API is built. What below still holds: tree, reconnect rules, image-paste flow; the
> paste upload and `tmux list-sessions` go through `creds_run`/`creds_exec` on the host entry, so secrets stay in creds.
> Open check: whether a creds terminal's process exit code reaches `onDidCloseTerminal` (needed for reconnect).

creds is a UI extension (`extensionKind: ["ui"]`, creds package.json:49) and owns Windows ssh, the vault and terminals
(terminalManager.ts:33-61); its `activate` returns `Promise<void>` (creds extension.ts:121) — no API today. The API is
**commands with JSON arguments/results** (they cross extension hosts; an exports object does not).

creds API v1: `creds.api.version`; `creds.api.listHosts` → `{entryId, host, user}`; `creds.api.listTmux({entryId})`
(fixed read-only `tmux list-sessions -F`, BatchMode); `creds.api.upsertSession({hostEntryId, session, resumeId?})`
(folder `<host>`, entry `<session>`, command composed from checked fields only, name `^[A-Za-z0-9._-]{1,64}$`, never
free text); `creds.api.openSession({entryId, mode})` — the terminal's process IS ssh, so its exit code is real;
`creds.api.pasteClipboardImage({entryId})` → `{remotePath}` (PowerShell clipboard read, ssh stdin,
`umask 077; mkdir -p ~/.cache/coai-paste; cat > …`). creds asks one consent per host the first time; a caller can never
pass a raw command.

coai owns: TreeView `coai.sessions` (VM → OS user → session; ssh only when a node opens); create/attach (resume id from
`readRemoteSession`); reconnect on `onDidCloseTerminal` when the name matches `Creds: <host> / <session>`, reason
Process, code 255 → `openSession` attach-only, backoff 1-2-4-8-16-30-60 s, max 10, reset after 30 s alive; code 0
never retries; `coai.pasteImageToSession` (ctrl+alt+shift+v, terminalFocus) → `sendText(remotePath, false)`.
The creds work is its own epic in the creds repo, released before the sessions epic.

## 6. Claude mod

`mods/coai-band/` in the coai repo, agent-models structure (`.claude-plugin/plugin.json`, `hooks/register.tsx`,
`types/index.d.ts`, tests). 5 s timer from `session.start` (agent-models pattern, register.tsx:264-311); each tick
`coai --band-json` → `{phrases[], consultations[], failures[]}`; `ui.render` AbovePrompt: phrase buttons
(`$.prompt.fill`), active cons/consask rows, open failures in red; always `next(e)`. Installed by
`coai --setup-clients` into `~/.claude/mods/coai-band` and APPENDED to `env.CLAUDE_CODE_PLUGIN_DIRS` (the settings
value replaces the shell's — agent-models-decide.mjs:249 — so the agent-models entry must stay). Windows unverified.

## 7. Settings migration v1 → 2.0

Once, after the first sidebar `ready`; idempotent (`coai.v2.migrated` = version + input hash); reads only the user
scope of the 68 `coai.*` keys. Backup to `globalStorage/v1-settings-backup.json`; build the device document (catalog
rows → models, roles, consultant → cons, question consultant → consask, gate/limits → gates, phrases; chat presets stay
in the chat store); hub set → `PUT` as revision 1, else `<dataDir>/device-settings.json`, pushed later. v1 keys are
never deleted (rollback to 1.x works).

## 8. Build order (extension half)

| Epic | Depends on | Done when |
|---|---|---|
| E1 Shell foundation: entry, lazy loader, firewall, budgets, measure launch, sidebar on the kit, chat carried, install + `--setup-clients` | server `--setup-clients`, `--band-json` | budgets green; M1/M2 in budget on Linux + Windows CI and Remote-WSL (recorded); chat host scenarios pass |
| E2 Kit 0.3.0 + local pages: Prompts & Commands, Phrases, Main AI, Rounds, migration | kit release; `--prompts-json`, `--commands-json` | stories + tests per component; pages tested by running; migration fixture passes |
| E3 Hub pages: Devices, enrolment, update everywhere, Features | hub API, `--plan-tree` | two-device contract test vs hub fake; 412 test; Features renders the server's errors |
| E4 Sessions: tree, reconnect, image paste | creds API v1 | host scenario with fake ssh (255 then 0); paste test |
| E5 Claude mod + cut-over + 2.0 release | E1-E4 | mod tests green; one real WSL session shows the band |

## 9. Risks and their tests

1. Start time creeps back — entry firewall + budgets + measure launch on Linux/Windows CI (fails > 2000 ms).
2. Lazy activation breaks restore or loses failures — host scenario: reload with an open chat tab; failure notice
   shown after the view opens. Accepted trade-off: a window that never opens coai shows no toast (mod + hub show it).
3. Migration loses data / blocks rollback — fixture with all 68 keys: mapping, backup, unchanged v1 config, idempotent.
4. Two windows write one device document — hub fake answers 412; page reloads, "changed elsewhere", no overwrite.
5. Cross-host command API or reconnect storm — contract test via `executeCommand` with a fake creds; backoff timings,
   no retry on 0, max 10; paste test (umask 077, path under $HOME, `sendText(..., false)`).

New server one-shots this half needs: `--setup-clients`, `--band-json`, `--prompts-json`, `--commands-json`,
`--plan-tree` (each added to PROJECT.md's list), and `shared/hub-contract.json` with vectors.

## 10. Consultant review 1 (cons, codex gpt-6.2-astra, 2026-11-22) — decisions

Checked by the main session in the code before deciding (sideSettings.ts:17-24, creds terminalManager.ts:44-56,
creds UseTools.cs:88-100, notify.ts:45-52).

| # | Finding | Decision |
|---|---|---|
| 1 | Migration reads only the 68 user settings, but per-side values live in `globalState` overlays (sideSettings.ts:19) → Windows and WSL would both get the shared catalog; "PUT as revision 1" needs a first-write protocol (If-Match / 428 / 412, lost reply, a later edit must survive a retry) | **Accept.** Back up user settings AND side overlays; resolve each device's effective values through the existing reader; keep the pending migration until the hub acknowledges; first write uses `If-None-Match: *`; first fixture = two sides with different catalogs; device ownership is settled before E2 writes migration code |
| 2 | Sessions: creds opens a shell and types ssh into it, so a dropped ssh may not close the terminal; `creds_open_terminal` returns only "opened"; `creds_exec` has no stdin channel for the image upload | **Accept.** The existing-MCP decision stays. E4 starts with one real Windows/WSL spike: disconnect an attached session and record the terminal event + identity; round-trip a small PNG through the chosen MCP path and compare its hash. Reconnect and paste are unproven until it passes; a small creds change (ssh as the terminal process, a stdin/upload verb) is planned only if the spike needs it |
| 3 | Remote-WSL timing: an installed VSIX runs in Production, marks are exported only in Test; run-host.mjs shares one profile across launches, so restored view state can activate coai before the clock | **Accept.** Each sample gets a fresh profile and data dir; assert `isActive === false`; the clock starts in the driver before dispatch; a fresh `ready` ack is required; the WSL driver reads marks through a measure command available when `COAI_MEASURE=1` (any mode); record the extension-host location. M2 = painted usable shell ≤ 2000 ms (hard); M3 = first local data, recorded |
| 4 | The chat extraction misses initialization (data root, side models, remote sessions, presets — extension.ts:143, :616) and teardown (heartbeat, queued usage writes — :953); shell and copied block would both register chat commands and the `coaiChat` serializer (:903) | **Accept.** The shell registers once; its thunks share one lazy chat instance; `deactivate` forwards to it when loaded. Test: cold chat command and a restored legacy tab without ever opening the sidebar, custom data root, side-specific model, close right after a turn |
| 5 | Chat presets come from VS Code configuration (chatPresetsHost.ts:52); phrases are person-level and verbatim (phrases.ts:12) — not device-document data | **Accept.** Presets and phrases keep their current sources (person-level, unchanged features); the mod gets phrases through `--band-json` reading the same source; tests: a preset edit affects the next chat; switching device keeps the same phrase text |
| 6 | Contracts disagree: Main AI lacks the `workMethod` editor; enrolment 15 min vs 24 h; Features calls a local `--plan-tree` for a folder that may live on another device | **Accept.** `shared/hub-contract.json` fixes: enrolment key single-use, 30 min, shown once (server.md § 17.1); `workMethod` block edited on Main AI; a remote feature tree is fetched through the hub (device agent runs `--plan-tree` on request) — E3 demonstrates one Windows window opening a remote-only feature and following a rollout through the real agent + updater, not only a hub fake |
| 7 | The metafile firewall must check `outputs[*].imports` too (an external builtin shows only there); separately bundled modules duplicate module-local state (notify.ts suppressor) | **Accept.** Firewall checks inputs AND output imports; shared mutable services (notify, ledger, data root) live in one runtime module that other bundles reach through the shell, never bundled twice |
| 8 | Kit additions: the strict DOM also limits attributes/properties; test the shipped page bundle (labelled toggle edit + Save, chip+text filter after refresh, keyboard expansion with focus kept). Mod: keep inherited entries on first write, platform delimiter, second setup a no-op, both install orders with agent-models | **Accept** as acceptance tests of E2 and E5 |

Sound as written (consultant): lazy command/view activation, registration-only `activate()`, separate host and page
budgets, running the minified page in tests; E1 first, real host-measurement and creds checks before their gates.

# The work tree (YAML)

## v2/epoch-1/e1-gate-simplify/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e1-gate-simplify
title: "Gate core: four outcomes, v1 data migrated, live proof"
mvp: true
half: server
dependsOn: []
parallelWith:
  - e2-thin-shell
doneWhen:
  - "only proceed, revise, good_enough and not_reviewed come out of a round"
  - "the deleted classes are gone and the MCP test executable is green"
  - "--migrate-v2 converts every released fixture exactly once; an interrupted run ends equal to one clean run"
  - "3 real plan→code gates end with 0 person waits; migration on copies of real data: dry-run = apply, second run 0; outcome shares recorded"
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s1-verdicts-and-holds
title: "Remove the policy enum, ladder, held gates, cards and ask_human"
dependsOn: []
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/tasks/t1-policy-ladder/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-policy-ladder
title: "Remove StagePolicy/EscalationStep, PanelConfig.OnExhausted/Autonomous, PolicyOf and the ContinueAnyway/CallHuman/Escalated/Ladder/GateHeld verdicts; keep Proceed/Revise/GoodEnough/NotReviewed"
kind: code
complexity: 7
dependsOn: []
parallel: false
files:
  - src_mcp/core/Rounds/SessionState.cs
  - src_mcp/core/Rounds/RoundMachine.cs
  - src_mcp/src/Server/PanelSettings.cs
dod:
  - id: d1
    item: "a round can end only in the four kept outcomes"
  - id: d2
    item: "an over-threshold round with budget left returns revise"
  - id: d3
    item: "the solution builds with warnings as errors"
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/tasks/t2-holds-cards-askhuman/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-holds-cards-askhuman
title: "Remove RoundEngine.ApplyAnyHumanDecision, Escalations (move AtomicJson and ScratchDirectory out), EscalationRetention, SessionHolds, AskHumanService, AskUnderAuto, EscalationBudget; ask_human becomes an alias reply"
kind: code
complexity: 6
dependsOn:
  - t1-policy-ladder
parallel: false
files:
  - src_mcp/src/Server/Rounds/RoundEngine.cs
  - src_mcp/src/Server/Escalations.cs
  - src_mcp/src/Server/AskHumanService.cs
  - src_mcp/src/Server/AskUnderAuto.cs
  - src_mcp/src/Tools.cs
dod:
  - id: d1
    item: "no code path waits for a person"
  - id: d2
    item: "ask_human returns status ask_in_conversation with note 'removed in v2'"
```

## v2/epoch-1/e1-gate-simplify/stories/s1-verdicts-and-holds/tasks/t3-verdict-tests/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-verdict-tests
title: "Delete tests of removed behaviour; add RoundMachine tests for the four outcomes"
kind: test
complexity: 5
dependsOn:
  - t2-holds-cards-askhuman
parallel: false
files:
  - src_mcp/tests
dod:
  - id: d1
    item: "the MCP test executable is green"
  - id: d2
    item: "each kept outcome has a test that fails if its branch is deleted"
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-migrate-v2
title: "--migrate-v2: held sessions, cards, stores, settings keys"
dependsOn:
  - s1-verdicts-and-holds
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/tasks/t1-migrate-mode/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-migrate-mode
title: "One-shot --migrate-v2 [--apply], automatic at first 2.0 serve under a lease with marker migrations/v2.done; legacy-hold reader kept"
kind: code
complexity: 7
dependsOn: []
parallel: false
files:
  - src_mcp/src/Program.cs
  - src_mcp/src/Server/Migrations
dod:
  - id: d1
    item: "a held session becomes AwaitingResolve + AdvanceOnResolve with the converted note"
  - id: d2
    item: "active cards move to escalations/archive as withdrawn_v2; qphase and cadence stores archived, not deleted"
  - id: d3
    item: "removed settings keys land in settings.v1-removed.json"
  - id: d4
    item: "a step journal migrations/v2.journal makes every step idempotent across partial writes"
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/tasks/t2-migrate-fixtures/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-migrate-fixtures
title: "Fixtures of each released session format migrated twice: one advance, note present, no active card; kill and restart at each step boundary"
kind: test
complexity: 5
dependsOn:
  - t1-migrate-mode
parallel: false
files:
  - src_mcp/tests
dod:
  - id: d1
    item: "second run changes nothing"
```

## v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/tasks/t3-migrate-docs/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-migrate-docs
title: "PROJECT.md one-shot list and shared/data-inventory.json updated for the added and removed modes"
kind: docs
complexity: 2
dependsOn:
  - t1-migrate-mode
parallel: true
files:
  - .agents/PROJECT.md
  - shared/data-inventory.json
dod:
  - id: d1
    item: "every one-shot mode in Program.cs is listed and no removed one is"
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-live-proof
title: "Live proof: real plan→code gates and migration on copies of real data"
dependsOn:
  - s1-verdicts-and-holds
  - s4-migrate-v2
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/tasks/t1-live-gates/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-live-gates
title: "Run 3 real plan→code gates on the E1 binary; record 0 person waits and only the four outcomes"
kind: measure
complexity: 4
dependsOn: []
parallel: false
files:
  - research/RESULTS_v2_gate_core.md
dod:
  - id: d1
    item: "3 real gates recorded with verdicts and reviewer counts"
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/tasks/t2-migrate-copies/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-migrate-copies
title: "--migrate-v2 on a COPY of each real data folder: dry-run counts = apply counts; second run 0 changes"
kind: measure
complexity: 5
dependsOn: []
parallel: true
files:
  - research/RESULTS_v2_gate_core.md
dod:
  - id: d1
    item: "counts recorded per data folder"
```

## v2/epoch-1/e1-gate-simplify/stories/s6-live-proof/tasks/t3-outcome-share/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-outcome-share
title: "Share of good_enough/not_reviewed vs the v1 call_human share from RoundsDb"
kind: measure
complexity: 3
dependsOn:
  - t1-live-gates
parallel: false
files:
  - research/RESULTS_v2_gate_core.md
dod:
  - id: d1
    item: "both shares recorded with the query used"
```

## v2/epoch-1/e2-thin-shell/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e2-thin-shell
title: "Thin extension shell, ≤ 2 s start, chat carried"
mvp: true
half: extension
dependsOn: []
parallelWith:
  - e1-gate-simplify
doneWhen:
  - "no onStartupFinished; activate() only registers (entryPurity test)"
  - "entry firewall checks inputs and output imports; byte budgets green"
  - "M1 ≤ 50 ms; M2 maximum of 3 fresh samples ≤ 2000 ms on Linux CI, Windows CI and Remote-WSL"
  - "v1 baseline and per-step marks of the old activate() recorded"
  - "joint smoke with the E1 binary passes; settings-mirror check done"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s1-shell-core
title: "Entry, lazy loader, ids, per-module bundles, firewall, budgets"
dependsOn: []
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t1-entry-lazy/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-entry-lazy
title: "shell/entry.ts, lazy.ts, ids.ts; activation events onView/onWebviewPanel/onUri and implicit command events"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_vs_code/shell
  - src_vs_code/package.json
dod:
  - id: d1
    item: "activate() makes only register* and subscriptions.push calls"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t2-bundles/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-bundles
title: "scripts/bundle-entries.json and one CommonJS esbuild entry per module and page"
kind: code
complexity: 5
dependsOn:
  - t1-entry-lazy
parallel: false
files:
  - src_vs_code/scripts
  - src_vs_code/package.json
dod:
  - id: d1
    item: "dist/extension.js and dist/modules/*.js are emitted; the packaged VSIX contains every chunk"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t3-firewall-budgets/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-firewall-budgets
title: "entry-firewall.mjs (metafile inputs and outputs[*].imports) + byte budgets + ESLint no-restricted-imports on the shell"
kind: code
complexity: 5
dependsOn:
  - t2-bundles
parallel: false
files:
  - src_vs_code/scripts/entry-firewall.mjs
  - src_vs_code/eslint.config.mjs
dod:
  - id: d1
    item: "a planted require('node:fs') in the entry fails CI"
```

## v2/epoch-1/e2-thin-shell/stories/s1-shell-core/tasks/t4-entry-purity/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-entry-purity
title: "entryPurity.test.ts runs the entry against a recording vscode fake"
kind: test
complexity: 4
dependsOn:
  - t1-entry-lazy
parallel: true
files:
  - src_vs_code/src/test/entryPurity.test.ts
dod:
  - id: d1
    item: "the test fails on any call other than register* or subscriptions.push"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s2-measure
title: "Cold-start measurement M1/M2/M3"
dependsOn:
  - s1-shell-core
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t0-v1-baseline/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t0-v1-baseline
title: "Measure the v1 cold start (same harness) and record it as the baseline"
kind: measure
complexity: 3
dependsOn: []
parallel: true
files:
  - research/RESULTS_v2_start.md
dod:
  - id: d1
    item: "v1 M2 recorded on Linux with three samples"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t1-measure-launch/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-measure-launch
title: "run-host.mjs measure launch: fresh profile and data dir per sample, isActive false asserted, driver clock, fresh ready ack, M1/M2/M3"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_vs_code/scripts/run-host.mjs
  - src_vs_code/src/test/host/measureScenario.ts
  - src_vs_code/shell/measure.ts
dod:
  - id: d1
    item: "a planted 3 s sleep in activation fails the launch"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t2-ci-jobs/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-ci-jobs
title: "CI jobs: measure on ubuntu (xvfb) and windows-latest"
kind: config
complexity: 4
dependsOn:
  - t1-measure-launch
parallel: false
files:
  - .github/workflows/ci.yml
dod:
  - id: d1
    item: "both jobs required; a missing ack fails the job"
```

## v2/epoch-1/e2-thin-shell/stories/s2-measure/tasks/t3-remote-wsl/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-remote-wsl
title: "scripts/measure-remote-wsl.ps1 + driver VSIX; marks readable with COAI_MEASURE=1 in any mode"
kind: code
complexity: 5
dependsOn:
  - t1-measure-launch
parallel: true
files:
  - src_vs_code/scripts/measure-remote-wsl.ps1
dod:
  - id: d1
    item: "the result names the extension-host location"
```

## v2/epoch-1/e2-thin-shell/stories/s6-joint-smoke/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-joint-smoke
title: "Joint smoke: shell VSIX + E1 binary"
dependsOn:
  - s2-measure
```

## v2/epoch-1/e2-thin-shell/stories/s6-joint-smoke/tasks/t1-joint-round/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-joint-round
title: "Install the shell VSIX with the E1 binary; one real round shown; no hold/card path runs"
kind: measure
complexity: 4
dependsOn: []
parallel: false
files:
  - research/RESULTS_v2_start.md
dod:
  - id: d1
    item: "round shown in the sidebar; no card or hold code reached"
```

## v2/epoch-1/e2-thin-shell/stories/s6-joint-smoke/tasks/t2-settings-mirror/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-settings-mirror
title: "Settings mirror under lazy activation: a window that never opens coai still gives the server current settings, or the gap is closed"
kind: test
complexity: 5
dependsOn: []
parallel: true
files:
  - src_vs_code/src/test/host
dod:
  - id: d1
    item: "result recorded; a gap is fixed or the server reads settings without the mirror"
```

## v2/epoch-1/e3-server-cleanup-install/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e3-server-cleanup-install
title: "Server cleanup, renames, conventions optional, --band-json, install, roles"
half: server
dependsOn:
  - e1-gate-simplify
parallelWith:
  - e4-shell-modules
doneWhen:
  - "cons, cons_close, consask served; old names answer as aliases"
  - "coai ships and enforces no conventions; a repo that mounts them still has them read"
  - "roles returns main AI, ORDERED model lists per role, complexity bands, Haiku kinds, work method"
  - "--install on a clean WSL box gives a working Claude + Codex (+ agy) MCP setup with the SessionStart roles hook; idempotent; NO AskUserQuestion hook"
  - "--band-json answers phrases, consultations, failures"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s2-remove-modes/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s2-remove-modes
title: "Remove consask modes, cadence, operator commands, prompts-per-round, security lane"
dependsOn: []
```

## v2/epoch-1/e3-server-cleanup-install/stories/s2-remove-modes/tasks/t1-consask-modes/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-consask-modes
title: "Remove AskGateDesk, QuestionPhaseStore, StageRelease, QuestionConsultSettings Mode/FreeBatches"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_mcp/core/QuestionConsult
  - src_mcp/src/Server/QuestionConsult
dod:
  - id: d1
    item: "consask answers with no phase or consultId logic"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s2-remove-modes/tasks/t2-cadence/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-cadence
title: "Remove Server/Cadence, core/Cadence, CadenceArgs and the --cadence one-shot"
kind: code
complexity: 4
dependsOn:
  - t1-consask-modes
parallel: false
files:
  - src_mcp/src/Server/Cadence
  - src_mcp/core/Cadence
  - src_mcp/src/Tools.cs
  - src_mcp/src/Program.cs
dod:
  - id: d1
    item: "--cadence exits 64 (unknown mode)"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s2-remove-modes/tasks/t3-commands-picker/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-commands-picker
title: "Remove CommandsSetting, CommandModelsSetting, core/Commands, shared/command-models.json, the operator flags and PromptsPerRound/DealPlan/CodeLenses"
kind: code
complexity: 5
dependsOn:
  - t2-cadence
parallel: false
files:
  - src_mcp/src/Server/CommandsSetting.cs
  - src_mcp/core/Commands
  - shared/command-models.json
  - src_mcp/src/Server/PanelSettings.cs
dod:
  - id: d1
    item: "one prompt per role by default; variants only as overrides"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s2-remove-modes/tasks/t4-security-lane/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-security-lane
title: "Remove the security lane (SecurityLane*, CheckSecurityMode, SecurityPromptTextReadMode, core/Security, shared/security-lane*.json)"
kind: code
complexity: 4
dependsOn:
  - t3-commands-picker
parallel: false
files:
  - src_mcp/src/Server
  - src_mcp/core/Security
  - shared/security-lane.json
dod:
  - id: d1
    item: "--check-security and --security-prompt-text exit 64"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s3-renames/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s3-renames
title: "cons / cons_close / consask with aliases and settings fallbacks"
dependsOn:
  - s2-remove-modes
```

## v2/epoch-1/e3-server-cleanup-install/stories/s3-renames/tasks/t1-tool-renames/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-tool-renames
title: "Serve cons, cons_close, consask; keep consult, close_consult, ask_consultants as hidden aliases with a 'renamed' note"
kind: code
complexity: 4
dependsOn: []
parallel: false
files:
  - src_mcp/src/Tools.cs
  - src_mcp/src/Prompts.cs
dod:
  - id: d1
    item: "tools/list shows the new names only; old names still answer"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s3-renames/tasks/t2-settings-fallbacks/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-settings-fallbacks
title: "Read COAI_CONS_* / COAI_CONSASK_*, falling back to COAI_CONSULT_* / COAI_QCONSULT_*"
kind: code
complexity: 3
dependsOn:
  - t1-tool-renames
parallel: false
files:
  - src_mcp/src/Server/PanelSettings.cs
  - src_mcp/src/Server/QuestionConsult/QuestionConsultSettings.cs
dod:
  - id: d1
    item: "the new key wins when both are set"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s3-renames/tasks/t3-text-renames/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-text-renames
title: "Rename consultant / question consultant to cons / consask in tool descriptions, notices and prompt texts"
kind: rename
complexity: 2
dependsOn:
  - t2-settings-fallbacks
parallel: false
files:
  - src_mcp/src
  - shared
dod:
  - id: d1
    item: "no user-visible text says 'question consultant'"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s4-band-json/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-band-json
title: "--band-json one-shot"
dependsOn: []
```

## v2/epoch-1/e3-server-cleanup-install/stories/s4-band-json/tasks/t1-band-json/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-band-json
title: "Server one-shot --band-json: phrases, open consultations, open failures from the local data folder"
kind: code
complexity: 4
dependsOn: []
parallel: true
files:
  - src_mcp/src/Program.cs
  - src_mcp/src/BandJsonMode.cs
dod:
  - id: d1
    item: "exit 0 with one JSON object; an unknown flag never exits 64"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s5-conventions-out/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s5-conventions-out
title: "Conventions optional: stop shipping and enforcing them"
dependsOn: []
```

## v2/epoch-1/e3-server-cleanup-install/stories/s5-conventions-out/tasks/t1-stop-enforcing/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-stop-enforcing
title: "Remove conventions enforcement from the server (ledger/epoch/cadence checks, rule-version notices); keep reading a mounted .agents/conventions as the repo own instructions"
kind: code
complexity: 4
dependsOn: []
parallel: true
files:
  - src_mcp/runners/Context/StageRules.cs
  - src_mcp/src/Server
dod:
  - id: d1
    item: "a repo without the mount gets no missing-rules notice"
  - id: d2
    item: "a repo with the mount still has its rules read"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s5-conventions-out/tasks/t2-fixture/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-fixture
title: "Fixture: repo with and without the conventions mount plus an ordinary submodule"
kind: test
complexity: 3
dependsOn:
  - t1-stop-enforcing
parallel: false
files:
  - src_mcp/tests
dod:
  - id: d1
    item: "both cases tested; ordinary submodule populates"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s6-install/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-install
title: "--install and --setup-clients: find CLIs, write MCP config (cornerstone)"
dependsOn:
  - s3-renames
```

## v2/epoch-1/e3-server-cleanup-install/stories/s6-install/tasks/t1-find-clis/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-find-clis
title: "Find claude, codex, agy (ExecutablePaths); report versions"
kind: code
complexity: 4
dependsOn: []
parallel: false
files:
  - src_mcp/src/Install
dod:
  - id: d1
    item: "absent CLIs reported, not guessed"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s6-install/tasks/t2-write-config/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-write-config
title: "Write MCP config through vendor commands (claude mcp add-json --scope user, codex mcp add); agy path measured first; backup + idempotent by command path"
kind: code
complexity: 7
dependsOn:
  - t1-find-clis
parallel: false
files:
  - src_mcp/src/Install
dod:
  - id: d1
    item: "second run changes nothing"
  - id: d2
    item: "a backup exists before the first write"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s6-install/tasks/t3-clean-box/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-clean-box
title: "Clean WSL box test (fresh HOME): one command → working MCP setup"
kind: test
complexity: 5
dependsOn:
  - t2-write-config
parallel: false
files:
  - src_mcp/tests
dod:
  - id: d1
    item: "claude and codex list the coai server"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s7-roles-hook/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s7-roles-hook
title: "roles tool + SessionStart hook"
dependsOn:
  - s6-install
```

## v2/epoch-1/e3-server-cleanup-install/stories/s7-roles-hook/tasks/t1-roles-tool/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-roles-tool
title: "MCP tool roles + one-shot --roles [--format json|claude-hook]; shared/role-defaults.json with ordered model lists per role and main-AI vendor"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_mcp/src
  - shared/role-defaults.json
dod:
  - id: d1
    item: "codex/agy rows only from measured model lists"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s7-roles-hook/tasks/t2-sessionstart/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-sessionstart
title: "Installer merges the SessionStart → --roles --format claude-hook hook"
kind: code
complexity: 4
dependsOn:
  - t1-roles-tool
parallel: false
files:
  - src_mcp/src/Install
dod:
  - id: d1
    item: "a new Claude session shows the role table"
```

## v2/epoch-1/e4-shell-modules/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e4-shell-modules
title: "Shell modules: sidebar, chat carried, install after ready, dead v1 out"
half: extension
dependsOn:
  - e2-thin-shell
doneWhen:
  - "sidebar polls only while visible"
  - "all chat host scenarios pass"
  - "M2 still ≤ 2000 ms"
  - "no escalation or cadence control or watcher in the bundle"
```

## v2/epoch-1/e4-shell-modules/stories/s3-sidebar/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s3-sidebar
title: "Sidebar on the kit: failures, active cons/consask, last rounds"
dependsOn: []
```

## v2/epoch-1/e4-shell-modules/stories/s3-sidebar/tasks/t2-sidebar-module/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-sidebar-module
title: "shell/modules/sidebar + pages/sidebar.ts on kit 0.2: skeleton first, ready after paint, data after ready"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_vs_code/shell/modules/sidebar
  - src_vs_code/pages/sidebar.ts
  - src_vs_code/package.json
dod:
  - id: d1
    item: "the sidebar polls only while visible"
```

## v2/epoch-1/e4-shell-modules/stories/s3-sidebar/tasks/t3-sidebar-page-test/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-sidebar-page-test
title: "Bundled page test of the sidebar (runs the minified page against a DOM shim)"
kind: test
complexity: 4
dependsOn:
  - t2-sidebar-module
parallel: false
files:
  - src_vs_code/src/test
dod:
  - id: d1
    item: "deleting the ready post makes the test fail"
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-chat-carried
title: "Chat carried unchanged: one registration, one lazy instance, init and teardown"
dependsOn:
  - s1-shell-core
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/tasks/t1-shared-runtime/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-shared-runtime
title: "One runtime module for notify, ledger and data root; other bundles reach it through the shell"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_vs_code/shell/modules/runtime
dod:
  - id: d1
    item: "notify state exists once per host"
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/tasks/t2-chat-register/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-chat-register
title: "shell/modules/chat/register.ts: commands and coaiChat serializer registered once by the shell; data root, side models, remote sessions, presets initialised; deactivate forwards heartbeat stop and usage drain"
kind: code
complexity: 7
dependsOn:
  - t1-shared-runtime
parallel: false
files:
  - src_vs_code/shell/modules/chat
  - src_vs_code/src/extension.ts
dod:
  - id: d1
    item: "no chat logic changed (diff touches registration only)"
```

## v2/epoch-1/e4-shell-modules/stories/s4-chat-carried/tasks/t3-chat-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-chat-scenarios
title: "Host scenarios: cold chat command and restored legacy tab without the sidebar, custom data root, side model, close right after a turn"
kind: test
complexity: 5
dependsOn:
  - t2-chat-register
parallel: false
files:
  - src_vs_code/src/test/host
dod:
  - id: d1
    item: "all existing chat host scenarios pass"
```

## v2/epoch-1/e4-shell-modules/stories/s5-install-module/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s5-install-module
title: "Binary install/update moved after the first ready"
dependsOn:
  - s3-sidebar
```

## v2/epoch-1/e4-shell-modules/stories/s5-install-module/tasks/t1-install-after-ready/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-install-after-ready
title: "shell/modules/install carries installer.ts/coaiInstall.ts; runs only after the first sidebar ready or first command"
kind: code
complexity: 4
dependsOn: []
parallel: false
files:
  - src_vs_code/shell/modules/install
dod:
  - id: d1
    item: "M2 does not include install work"
```

## v2/epoch-1/e4-shell-modules/stories/s6-dead-v1-out/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-dead-v1-out
title: "Dead v1 watchers and controls out of the bundle"
dependsOn:
  - s4-chat-carried
```

## v2/epoch-1/e4-shell-modules/stories/s6-dead-v1-out/tasks/t1-remove-watchers/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-remove-watchers
title: "Remove escalation/consultation/question watchers, cadence and hold controls from the extension"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_vs_code/src
dod:
  - id: d1
    item: "no escalation or cadence control or watcher in the bundle"
```

## v2/epoch-1/e4-shell-modules/stories/s6-dead-v1-out/tasks/t2-recheck-m2/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-recheck-m2
title: "Re-run the measure launch; M2 still ≤ 2000 ms"
kind: measure
complexity: 2
dependsOn:
  - t1-remove-watchers
parallel: false
files:
  - research/RESULTS_v2_start.md
dod:
  - id: d1
    item: "new M2 recorded"
```

## v2/epoch-1/e5-release-2-0/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e5-release-2-0
title: "Cut-over: dropped code out of the bundle, 2.0 release, post-deploy check"
half: both
dependsOn:
  - e3-server-cleanup-install
  - e4-shell-modules
doneWhen:
  - "VSIX inspected: new shell, budgets, no dropped modules"
  - "mcp + extension released; the owner's machines run 2.0"
  - "a real plan → code round passes on 2.0"
  - "http/ contract suite live against the deployed Team server; new client vs old server and old extension vs new mcp checked"
```

## v2/epoch-1/epoch.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: epoch-1
title: Foundation
proves: "the simplified gate and the new shell work end to end on this machine"
dependsOn: []
epics:
  - e1-gate-simplify
  - e2-thin-shell
  - e3-server-cleanup-install
  - e4-shell-modules
  - e5-release-2-0
```

## v2/epoch-2/e1-hub-slice/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e1-hub-slice
title: "Hub slice: boots alone, enrol, device document, applied, one log stream, --hub-sync"
mvp: true
half: both
dependsOn: []
doneWhen:
  - "a setting changed from the Windows window is reported applied by a VM user in ≤ 90 s"
  - "stale write = 412; a device token is refused on another device and on Team routes"
  - "hub mode boots alone (own auth, own startup condition)"
  - "endpoints + http/ contracts; 412/428; device token limited to its device; Team session refused on hub routes"
  - "sweeper measures retained bytes and db+WAL size"
  - "deployed on the ionos host"
```

## v2/epoch-2/e2-vm-shared/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e2-vm-shared
title: "VM shared instance"
mvp: true
half: server
dependsOn: []
doneWhen:
  - "UID B runs claude, codex and agy after UID A logged in, also after a token refresh by the other UID"
  - "two UIDs write SQLite at the same time"
  - "shared /var/lib/coai with group coai; existing db/WAL permissions repaired"
```

## v2/epoch-2/e3-device-agent-sync/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e3-device-agent-sync
title: "Device agent and sync: lease, cache, single writer, outbox, sweeper, v1 → device-document migration, first sync"
half: both
dependsOn:
  - e1-hub-slice
  - e2-vm-shared
doneWhen:
  - "two VM users and WSL appear on the hub from pasted lines"
  - "settings round-trip with offline cache; single writer; foreign edit put back"
  - "the pasted line downloads, checks the hub-shown sha256, then runs; the single-use key is not left in history or environment"
  - "local outbox ≤ 20 MB per stream with a dropped-N line; delivered lines removed after ack"
  - "first sync: If-None-Match create; 412 with unchanged local → take hub; both changed → apply hub, keep local as conflict file, notify"
  - "the exact pasted bash and PowerShell lines tested: no key in the line, hidden prompt from /dev/tty, script run from the verified file"
  - "migration: user settings AND side overlays backed up, effective values per side, idempotent, v1 keys untouched"
  - "the legacy settings mirror is off on a hub-managed device"
```

## v2/epoch-2/e4-update-rollout/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e4-update-rollout
title: "Update everywhere and rollout to all devices"
half: both
dependsOn:
  - e3-device-agent-sync
doneWhen:
  - "a desired version moves every device; swap survives a kill; last 2 versions kept; rollback works"
  - "shared VM swap needs no root after the first install"
  - "2 VMs (shared), WSL and Windows enrolled; one rollout followed to the end"
```

## v2/epoch-2/e5-kit-devices-page/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e5-kit-devices-page
title: "Kit 0.3.0 + Devices page (roles → model lists, work method)"
half: extension
dependsOn:
  - e1-hub-slice
doneWhen:
  - "kit 0.3.0 released with K1-K6, each with a story, fakeDom and axe test"
  - "Devices: tree VM → user / WSL / Windows, per-device settings and logs, enrolment lines, update everywhere with progress"
  - "412 handled: reload, changed elsewhere, no overwrite"
```

## v2/epoch-2/epoch.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: epoch-2
title: "Hub, devices, update everywhere"
proves: "one Windows window sees and controls every device"
dependsOn:
  - epoch-1
epics:
  - e1-hub-slice
  - e2-vm-shared
  - e3-device-agent-sync
  - e4-update-rollout
  - e5-kit-devices-page
```

## v2/epoch-3/e1-role-jobs-ba/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e1-role-jobs-ba
title: "External-role jobs + router + failover + work-plan schema + BA role"
mvp: true
half: server
dependsOn: []
doneWhen:
  - "start_role/role_status/role_cancel; detached job with lease + generation"
  - "BA writes the stories and tasks of the next real v2 epic; validate_work passes in ≤ 2 repair turns; the orchestrator changes < 20 % of the task files"
  - "FakeCli refusal / limit / crash → next model; one log line and one notification per switch"
  - "work-plan schema with the security flag; validate_work; workMethod limits from the device config"
  - "complexity 10 or security → the important model list"
```

## v2/epoch-3/e2-coder-one-task/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e2-coder-one-task
title: "Coder: one task to green on this repository"
mvp: true
half: server
dependsOn:
  - e1-role-jobs-ba
doneWhen:
  - "coder is one role case; this repository through .coai/toolchain.yaml; claude only"
  - "green in ≤ 3 fix turns; main-AI tokens and wall time below the baseline; accepted gate findings not higher"
  - "task session inherits the feature plan proceed; write boundary at launch"
```

## v2/epoch-3/e3-roles-complete/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e3-roles-complete
title: "Roles complete: scout, important/judge, codex + agy rows, review_plan(featureFolder)"
half: server
dependsOn:
  - e1-role-jobs-ba
doneWhen:
  - "scout and judge strict JSON outputs"
  - "codex and agy coder sessions resumed"
  - "review_plan(featureFolder) validates first, packs in topological order"
```

## v2/epoch-3/e4-adapters-coder-hardening/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e4-adapters-coder-hardening
title: "Adapters dotnet + node + coder hardening"
half: server
dependsOn:
  - e2-coder-one-task
doneWhen:
  - "golden detection and argv on fixture repos (both trees, base..head diff)"
  - "parsers tested on recorded real output"
  - ".coai/toolchain.yaml override; not_configured is never green"
  - "this repository is a fixture that runs a real test"
  - "same session resumed (claude, codex, agy); caps and no-progress stop; flaky re-run"
  - "write boundary at launch; task session inherits the feature's plan proceed"
  - "job lease with generation; survives an MCP client restart"
```

## v2/epoch-3/e5-work-scheduler/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e5-work-scheduler
title: "Parallel work scheduler"
half: server
dependsOn:
  - e2-coder-one-task
  - e4-adapters-coder-hardening
doneWhen:
  - "story 1 = two tasks in parallel; go on only if speedup ≥ 1.3× and peak RAM stays in the WSL limit"
  - "DAG from YAML; host-level slots with atomic RAM reservation; WSL ceiling 2"
  - "checks on the candidate merge SHA before the integration branch advances"
  - "pending resolutions persisted; a run ends partial cleanly"
  - "any content the code round did not see (whole-tree compare) re-runs the code gate on the candidate SHA"
  - "detached run resolves itself (orchestrator-model resolver, 2 retries, 10 min → good_enough + notification); completes when the initiating AI disconnects"
```

## v2/epoch-3/epoch.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: epoch-3
title: "Deterministic work"
proves: "an external role does a task that coai routes, checks and keeps green"
dependsOn:
  - epoch-1
epics:
  - e1-role-jobs-ba
  - e2-coder-one-task
  - e3-roles-complete
  - e4-adapters-coder-hardening
  - e5-work-scheduler
```

## v2/epoch-4/e1-ask-hook/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e1-ask-hook
title: "AskUserQuestion hook (spike = story 1)"
mvp: true
half: server
dependsOn: []
doneWhen:
  - "spike on the installed Claude Code: updatedInput.answers skips the widget"
  - "first question answered by consask; later ones in 30 min reach the person; fails open"
  - "exit 0 always; one JSON object or nothing; one winner for parallel calls"
  - "the installer writes the hook only when the spike passed for the installed Claude Code version"
```

## v2/epoch-4/e2-sessions-creds/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e2-sessions-creds
title: "tmux sessions through the creds MCP (spike = story 1)"
mvp: true
half: extension
dependsOn: []
doneWhen:
  - "real Windows/WSL spike: disconnect event recorded; PNG round-trip hash matches"
  - "tree VM → user → session; create/attach/resume; reconnect backoff; no retry on 0"
```

## v2/epoch-4/e3-claude-mod/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e3-claude-mod
title: "Claude mod: phrases, active consults, failures"
half: extension
dependsOn: []
doneWhen:
  - "mod: phrases, active consults, failures; appends to CLAUDE_CODE_PLUGIN_DIRS in both install orders"
```

## v2/epoch-4/e4-local-pages/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e4-local-pages
title: "Local pages: Prompts & Commands, Phrases, Rounds (kit 0.3.0)"
half: extension
dependsOn: []
doneWhen:
  - "each page tested by running its shipped bundle"
  - "prompt overrides and facets (gates, review, documents, cons, consask, chat, roles, coder)"
```

## v2/epoch-4/e5-features-page/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e5-features-page
title: "Features page (needs epoch-3 e1)"
half: extension
dependsOn: []
doneWhen:
  - "epoch → epic → story → task tree with status, complexity, model, parallel lanes; a remote-only feature through the hub"
```

## v2/epoch-4/epoch.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: epoch-4
title: Operator
proves: "the person works through coai: questions answered, sessions one click away, everything visible"
dependsOn:
  - epoch-1
epics:
  - e1-ask-hook
  - e2-sessions-creds
  - e3-claude-mod
  - e4-local-pages
  - e5-features-page
```

## v2/epoch-5/e1-attachments/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e1-attachments
title: "Picture attachments on review_document"
mvp: true
half: server
dependsOn: []
doneWhen:
  - "codex attachment measurement recorded"
  - "an image round runs on claude, agy and codex (if measured); others skipped with a note"
  - "frames extracted by the Studio (≤ 8, png/jpg/webp); an unreadable frame is skipped with a note"
```

## v2/epoch-5/e2-studio-roles/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e2-studio-roles
title: "Studio review roles in jinx_studio (seam first)"
half: both
dependsOn:
  - e1-attachments
doneWhen:
  - "text-only seam role measured on the known defects; visual seam role on the known seam plus clean pairs"
  - "findings written to scene_issue as consultant, idempotent"
```

## v2/epoch-5/e3-more-adapters/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e3-more-adapters
title: "php, rust, python, java adapters"
half: server
dependsOn: []
doneWhen:
  - "each stack's fixture repo goes green end to end"
```

## v2/epoch-5/e4-git-pr-ci/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e4-git-pr-ci
title: "Git/PR/CI takeovers"
half: server
dependsOn: []
doneWhen:
  - "PR body from task.yaml + envelope; CI wait: checks must exist then pass; no-runner re-run once; conflicting PR reported"
```

## v2/epoch-5/epoch.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: epoch-5
title: "Reach: studio and every listed language"
proves: "video review through extracted frames, and C#/Node/PHP/Rust/Python/Java all work"
dependsOn:
  - epoch-3
epics:
  - e1-attachments
  - e2-studio-roles
  - e3-more-adapters
  - e4-git-pr-ci
```

## v2/feature.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: coai-v2
title: "coai v2 — one product on every machine, auto only, ≤ 2 s start, deterministic half done by coai"
status: planned
plan: PLAN.md
architecture:
  - architecture/server.md
  - architecture/extension.md
workMethod:
  mvpEpics:
    min: 1
    max: 2
  maxEpicsPerEpoch: 5
  minConsultsPerEpoch: 1
  minConsultsPerEpic: 0
  complexityScale: "0-10"
epochs:
  - epoch-1
  - epoch-2
  - epoch-3
  - epoch-4
  - epoch-5
taskSchema:
  security: "boolean flag; true routes the task to the important role"
  roles: "ordered model lists per role"
cornerstones:
  - epoch-1/e1/s4-migrate-v2
  - epoch-1/e3/s6-install
  - "epoch-2/e1 hub auth + device-document contract"
  - "epoch-2/e2 shared vendor logins + file permissions"
  - "epoch-2/e3 first sync + settings migration, single writer"
  - "epoch-2/e4 binary swap"
  - "epoch-3/e1 router + failover + job lease"
  - "epoch-3/e1 work-plan schema"
  - "epoch-3/e2 plan binding + envelope/report schema"
  - "epoch-3/e5 host slots + merge on candidate SHA"
```
