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
  shell/measure.ts          perf marks; read through `coai.__measure`, registered only when COAI_MEASURE=1 (any extension mode)
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
- Order: the sidebar host posts its real menu at once (section headers, each "loading…" inside until the live page replaces it); the webview posts `menuReady` when the headers are shown and usable; the old code, chat and data follow on timers after it. Only
  after the first `menuReady`: install (version check, `--setup-clients`), then migrate, each on `setTimeout(0)`.

Measurement: reuse `scripts/run-host.mjs` (bundles first :85/:117; real VS Code via @vscode/test-electron with own
workspace, data dir and `--user-data-dir` :110-113/:130; poison env removed :50; two launches today :141/:147). Add a
launch `measure` (`node scripts/run-host.mjs measure`: `COAI_MEASURE=1`, `out/test/host/measureScenario.js`, one editor launch per sample with a fresh profile): run `workbench.view.extension.coai`, read
the marks through `coai.__measure` (S2 built this; results in `research/RESULTS_v2_start.md`). M1 = first statement of `activate` → `activate` return (≤ 50 ms own time; the host's delay from module evaluation to the `activate` call is `hostDelay`, recorded, no limit); M2 = command → `menuReady`: the webview says the REAL sidebar menu (every section header, none inert) is shown and usable (≤ 2000 ms; owner rule 2026-11-23 — until then it ended at the first `ready`, which a skeleton can send). 3 cold launches, the maximum counts. Linux CI (xvfb), Windows CI; Remote-WSL by
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
