# coai v2 — epoch 1, epic E3 (server cleanup, renames, conventions optional, band-json, install, roles, workItem) — epic plan for the gate (generated)

Parent plan: v2/PLAN.md; architecture: v2/architecture/server.md (§1, §2, §6, §18.4, §19-§21).

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
  - "each removal story adds its rows to MigrationV2Tables with a fixture; answered/expired cards older than 30 days are archived"
  - "review_*/cons/consask accept workItem; --plan-report gives per-item gates, cons, consask, time"
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

## v2/epoch-1/e3-server-cleanup-install/stories/s8-work-item-attribution/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s8-work-item-attribution
title: "Work-item attribution: every gate, cons and consask call names its plan item"
dependsOn:
  - s3-renames
```

## v2/epoch-1/e3-server-cleanup-install/stories/s8-work-item-attribution/tasks/t1-workitem-param/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-workitem-param
title: "Optional workItem (feature-folder path of an epoch/epic/story/task) on review_plan/code/document/feature, cons, consask; recorded in RoundsDb (new schema step) and in consultation records"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_mcp/src/Tools.cs
  - src_mcp/storage
dod:
  - id: d1
    item: "old callers without workItem still work"
  - id: d2
    item: "the value is validated as a path inside the repo"
```

## v2/epoch-1/e3-server-cleanup-install/stories/s8-work-item-attribution/tasks/t2-plan-report/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-plan-report
title: "One-shot --plan-report <feature-dir> --json: per item gates run, rounds, cons and consask counts, first/last event, wall time, status"
kind: code
complexity: 5
dependsOn:
  - t1-workitem-param
parallel: false
files:
  - src_mcp/src
dod:
  - id: d1
    item: "listed in PROJECT.md"
  - id: d2
    item: "an item with no events reports zeros, not missing"
```

# Spec — s6-install (cornerstone: writes other tools' configuration)

> Written by Fable, 2026-11-22. Facts measured in a scratch HOME on WSL2 (claude 2.1.330, codex-cli 0.171.0,
> agy 1.5.14). Main-session decisions at the end.

## 0. Facts

- **Claude MCP:** `claude mcp add-json --scope user coai -` (JSON on stdin) writes top-level `mcpServers.coai` in
  `$HOME/.claude.json` (mode 0600; with `CLAUDE_CONFIG_DIR`: `$CLAUDE_CONFIG_DIR/.claude.json`); other keys stay. An
  existing name → exit 1, no change (update = `remove` + `add-json`). Malformed file → exit 1, unchanged, a `.corrupted`
  copy is written. `claude mcp get`/`list` START the server (health check) — never call them in product code.
- **Claude hooks:** no vendor command; file `~/.claude/settings.json`; exec form `{type, command, args}` in use. The
  conventions hook `ensure-user-guard.mjs` also rewrites this file at session start.
- **Codex:** `codex mcp add coai -- <path>` writes `[mcp_servers.coai]` in `${CODEX_HOME:-~/.codex}/config.toml`; it
  REPLACES an existing entry (old `env`, `startup_timeout_sec` lost) and dropped an inline comment on another server's
  line. Malformed TOML → exit 1, unchanged. `codex mcp get coai --json` reads without starting.
- **agy:** `agy mcp add|remove|list|enable|disable` ("Add or update"); the file it writes is NOT verified.
  `~/.gemini/config/mcp_config.json` exists (0 bytes); `src_mcp/runners/Reviewers/NoMcpServers.cs:157` reads it and
  `~/.gemini/settings.json`.
- **Code:** `ExecutablePaths.cs` decides "this side or the other"; the PATH walk is
  `src_mcp/runners/Processes/ExecutableResolver.cs`. The extension writes no client file today (`mcpBlock.ts`: clipboard).
  This machine's user-scope `coai` entry points at the VS Code globalStorage binary with env `COAI_DATA_DIR`, `COAI_DATA_SIDE`.
- **Not verified:** Windows; `settings.json` under `CLAUDE_CONFIG_DIR`; Claude with a corrupted file when its own backup
  exists; the shared VM.

## 1. Contract

Modes: `--setup-clients [--dry-run] [--only <cli,…>] [--binary <path>] [--remove | --restore <id|latest>]`; `--install` =
same engine (+ `--shared` → `env.COAI_DATA_DIR=/var/lib/coai`, epoch 2). Registered command = `--binary`, else the stable
path (see decision 1); links not resolved.

| Target | File (Windows: `%USERPROFILE%`) | How |
|---|---|---|
| claude MCP | `${CLAUDE_CONFIG_DIR:-$HOME}/.claude.json` → `mcpServers.coai` | vendor command, JSON on stdin |
| claude hook (s7 t2) | `${CLAUDE_CONFIG_DIR:-$HOME/.claude}/settings.json` → one own group in `hooks.SessionStart`: `{type:"command", command:<path>, args:["--roles","--format","claude-hook"], timeout:10}` | direct edit: parse, compare before write, temp + rename, keep mode, follow links |
| codex | `${CODEX_HOME:-$HOME/.codex}/config.toml` → `[mcp_servers.coai]` | `codex mcp add [--env K=V] coai -- <path>` (old env passed again) |
| agy | measured first | `agy mcp add coai <path>` |

Each side (Windows, WSL) writes only its own files.

**Ownership / idempotence:** an MCP entry is ours when its name is `coai` AND the `command` file name is `coai-mcp` /
`coai-mcp.exe`; a hook is ours when its command file name matches and `args[0]` is `--roles`. Same path → `none` (no
vendor call, no write, no backup). Other path → `update` (replace `command` only; keep `env`, `args`). Name `coai` with
another command (a wrapper, `wsl.exe`) → `refuse`.

**Backup** before the first write of a run: `<state>/install-backups/<utcStamp>-<pid>/` (state = `~/.local/state/coai`
or `%LOCALAPPDATA%\coai`; folder 0700, files 0600; never the data folder). A full copy of each file to change +
`manifest.json` `{id, binary, startedUtc, targets:[{cli, kind, file, sha256Before, before, after, state:
planned|removed|written|verified}]}` (`before`/`after` = our entry only). No change → no backup. Keep the last 10.

**Output:** one JSON object, same shape for dry and real runs: `{mode, dryRun, binary:{path,version}, host,
backup:{id,dir}|null, clients:[{cli, found, path, version, result: changed|unchanged|absent|refused|failed|unmeasured,
reason, targets:[{kind, file, action: add|update|none|remove|restore|refuse, via, before, after, shadowedBy:[]}]}],
exitCode}`. Env VALUES never printed (key names only). Logs to stderr.

**Exit codes (never 64):** 0 every found CLI in the wanted state; 65 bad arguments / unknown backup id; 69 no CLI found;
73 backup not written, nothing changed; 74 a write failed after the backup; 75 another run holds the lock; 78 one or more
CLIs refused, the others done. A dry run answers the code the real run would.

**Never:** open token files (`.credentials.json`, `auth.json`, `*oauth-token`) or print/log env values; touch other
servers' entries, `projects[*].mcpServers`, `.mcp.json`, `.vscode/mcp.json`; touch other hook groups or events (the
`Agent|Task` guard); write `env.CLAUDE_CODE_PLUGIN_DIRS`, `~/.claude/mods/agent-models*`, `permissions` or effort keys;
write a PreToolUse / AskUserQuestion hook (§17.5); call `claude mcp get`/`list` in product code.

**agy measurement (before any agy code):** fresh HOME, list every file + sha256; `agy mcp add coai-probe /bin/true`, list
again → the file and key; repeat for a second add with another path, a foreign entry, a malformed file, a 0-byte file,
`remove` twice; record exit codes; repeat on Windows; record in `shared/install-clients.json` with the agy version. Until
then agy answers `unmeasured`.

## 2. Scenarios (fake CLIs; 22 uses real CLIs)

1. Fresh HOME, CLIs present → entries and hook written, manifest present, exit 0.
2. Second run → all `none`; fake-CLI call log empty; file-tree hash equal; no new backup.
3. Foreign MCP servers (with env), foreign hooks, `CLAUDE_CODE_PLUGIN_DIRS=…/agent-models`, the `Agent|Task` guard →
   deep-equal after; the `settings.json` text diff is added lines only (fixture has non-ASCII, `<`, `1e5`).
4. Old coai entry at another path with env → one entry, new command, same env (claude, codex); hook: one handler, new path.
5. `coai` entry with a foreign command → that CLI `refused`, exit 78, the others done.
6. CLI absent → `absent`, no file made, exit 0; no CLI → 69.
7. Malformed `.claude.json` / `config.toml` / `settings.json` (or not an object) → refused BEFORE a vendor command;
   bytes unchanged; others continue; 78. A 0-byte agy file is empty, not malformed.
8. Good `.claude.json`, malformed `settings.json` → MCP done, hook refused, reported per target.
9. Two runs at once → one entry, one hook group, no corrupt file; 0, or 75 for the second.
10. `settings.json` changes between read and write → nothing written, retry; after 3 → `refused`.
11. Read-only file/folder → `refused` with the OS reason; no temp file left.
12. Killed between claude `remove` and `add-json` → the next run reads the manifest, completes with the old env.
13. Vendor command fails / times out (30 s) → `failed`, old entry put back, 74.
14. Windows: `C:\Users\A B\…\coai-mcp.exe` survives in JSON, TOML and hook args; `claude.cmd`/`codex.cmd` resolved; a
    path with `& % ^ |` refused; a Windows-spelled path in a WSL file is updated.
15. `--dry-run` → no file, no backup, no vendor call; JSON equals the real run's except `dryRun`, `backup`.
16. `--restore latest` → our keys back to `before`; a server added after the install stays; a target not equal to `after`
    is refused.
17. `--remove` → our entries and hook group gone, all else equal; a second `--remove` → `none`.
18. Symlinked `settings.json` → the link stays; its target is written.
19. `projects[<repo>].mcpServers.coai` present → reported in `shadowedBy`, unchanged.
20. `CLAUDE_CONFIG_DIR` / `CODEX_HOME` set → those files written, `$HOME` files untouched; `--shared` → `env.COAI_DATA_DIR`.
21. Unknown flag → 65. No PreToolUse hook after any run.
22. Real clean box (`COAI_REAL_CLIS=1`; "not run" is not green) → `claude mcp get coai` Connected; `codex mcp get coai
    --json` has the path.

## 3. Brief for the coder (worktree /home/jinx/git/coai-v2-e3)

First `node .agents/conventions/tools/rules.mjs check --repo .`, then `explain`/`read` for your files. Order:
1. `src_mcp/src/Install/ClientFinder.cs` (t1): reuse `ExecutableResolver` (`Resolve`, `OnPosixPath`, `IsInstalledFile`)
   and the `~/.local/bin` fallback (`AntigravityRuntime.cs:246`); version via `--version` through `IProcessLauncher`,
   10 s; inject env, file probe, launcher.
2. `Install/ClientPlan.cs` — pure: file texts + found CLIs + flags → actions (most scenarios are unit tests here).
3. `Install/InstallBackup.cs`, `Install/InstallLock.cs`.
4. `Install/ClaudeMcp.cs`, `CodexMcp.cs`, `AgyMcp.cs` (`unmeasured`); an ordered target list so s7 t2 adds `ClaudeHook.cs`.
5. `Install/SetupClientsMode.cs`: source-generated JSON context (AOT), `JsonNode`, the relaxed encoder.
6. `Program.cs`: `Startup.SetupClients`, `Startup.Install`, `Classify`, help text.
7. `.agents/PROJECT.md` mode list; `EveryModeIsClassifiedForAMissingNamedRootTests` (not a writer mode).
8. `src_mcp/tests_fakecli`: verbs `mcp add-json|add|get|remove` with a call log and steering (fail, hang).
9. `src_mcp/tests/A…Tests.cs`: one class per scenario.
Commands: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`; the MTP executable via
`node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests`; never `dotnet test`;
no Python. RED-GREEN-RED per scenario. Hard rules: each test sets HOME, USERPROFILE, CLAUDE_CONFIG_DIR, CODEX_HOME to a
temp folder, and one guard test fails if a target is under the real home; read stdout only (codex warns on stderr for a
HOME under /tmp); vendor input on stdin when possible; stdout is the one JSON object.

## 4. Risks (Fable) and main-session decisions

1. Unstable command path (`versions/<v>/coai-mcp` disappears after two updates). **Decision:** register the stable path —
   in epoch 1 the extension's installed binary path; from epoch 2 the hub `current` link.
2. Two coai binaries on one side rewrite the entry to themselves. **Decision:** an installer rewrites the entry only when
   the registered binary is missing or is its own older path; otherwise it reports `shadowedBy`.
3. Codex replaces the full entry → read the old env and pass it again (contract).
4. A Claude update is two writes → the manifest states make it resumable (scenario 12).
5. Whole-file restore of `.claude.json` is unsafe (live state) → restore by key only (scenario 16).
6. Shared VM: `.claude.json` 0600 blocks a second user. **Decision:** `--shared` is NOT in epoch 1; it waits for the
   epoch 2 E2 spike.
7. `settings.json` has a second writer (the conventions SessionStart hook) and a project-scope `coai` entry wins over the
   user entry → compare-before-write + retry (scenario 10); `shadowedBy` (scenario 19).
8. No uninstall in the plan → `--remove`/`--restore` are in this contract.
9. The codex/agy "global instructions" line has no task. **Decision:** moved to s7 with marker lines.
10. t1 names the wrong class; CI has no real CLIs → scenario 22 runs on the owner's box and is recorded.
11. Default `System.Text.Json` escaping would rewrite `settings.json` → relaxed encoder (scenario 3).
