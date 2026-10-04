# PLAN — One model catalog: the Settings page rebuilt around models you add once

> Status: **plan only, nothing implemented yet, 2026-10-04.** The design is accepted: the clickable mockup in
> [`new_design/`](../new_design/README.md) (open `new_design/index.html`; `node new_design/check.mjs` drives it, 61
> checks). Scope: the extension's Settings page (`src_vs_code/src`), the settings it writes and how they reach
> coai-mcp, coai-mcp's runners where the design adds a capability (`src_mcp`), the Team server's review request
> (`src_server`), help in five languages, and the docs. The sidebar and the Review rounds page are out of scope.
>
> Every `file:line` below was read at `origin/main` = `e193822a` (extension 0.63.0, coai-mcp 0.43.0) on 2026-10-04.
> Line numbers move; re-read before cutting.
>
> Related docs: [architecture.md](../research/architecture.md), [PLAN_settings_page.md](../research/PLAN_settings_page.md),
> [PLAN_the_consultant_has_its_own_vendors.md](../research/PLAN_the_consultant_has_its_own_vendors.md),
> [PLAN_the_security_tab_reads_at_a_glance.md](../research/PLAN_the_security_tab_reads_at_a_glance.md).

## The goal, as the operator asked it (2026-10-04)

1. **The tool works, but it carries too much, and models are chosen in too many places.** Some features pick a
   vendor and model of their own; others borrow a reviewer row. Rebuild the Settings UI/UX from scratch — keeping
   nothing for its own sake — so that it is convenient, clear and intuitive.
2. **A new "Available models" tab is the only place models are added or removed.**
3. **Every vendor and every model can be added many times.**
4. **Each added model carries checkboxes naming the features it may be used for**, and its own options — for
   example GLM 5.3 with *low* effort for reviews and a second GLM 5.3 with *high* effort for the consultant.
5. **Each model says whether it is reached through a CLI or an API key** — whether it can read the git checkout and
   the machine, which decides what the consultant can do with it.
6. **A system prompt per model** — Default selected, the prompt empty.
7. **Effort for every vendor except Antigravity** (whose effort is part of the model name).
8. **Two columns on a wide screen, one on a narrow one; blocks side by side are one height; every page keeps text
   size and brightness;** a control's "new" tag shows only in the first week after the update that brought it.
9. **Nothing the system does today may be lost.** Verified twice (§ *What is true today*, and the coverage audit in
   the mockup's Design notes § 7).

## The operator's rulings (2026-10-04)

| Question | Ruling |
|---|---|
| One catalog for every feature? | **Yes** — reviews (plan, code, document, feature), security lane, consultant, question consultant, chat, Bugz ranking. |
| A different-vendor consultant | Ideal, **never blocked** — the page says when the caller and the consultant share a vendor. |
| A different effort for one model | **A second instance**, no per-feature override. |
| A key per instance | **Yes** — two instances may share a key or use two accounts. |
| Scope | **The Settings page only.** The sidebar and the Review rounds page are not touched. |
| Tabs | **Six**: Models · Reviews · Consultants · Security lane · Chat · Setup; Reviews, Consultants and Setup have sub-tabs. |
| coai-mcp | Changes once the UI is accepted (it is). |
| Work order | Mockup → operator review → this plan → implementation. **The redesign first; the tails after.** |
| Security lane triggers | On each card under the prompt text: the signals as **two columns of checkboxes**, plus the card's **own words** (a word, a phrase, a piece of code, or a `/regex/`). |

## What is true today (verified at `e193822a`)

| Fact | Where | Consequence for this plan |
|---|---|---|
| A reviewer is a `Vendor` row: id, runtime, model, enabled, stage flags, baseUrl, remoteVendor, teamServerId, executablePath, prices, vaultKeyName, dialect, effort/thinking/reviewMinutes (api rows only). | `src_vs_code/src/vendors.ts:16` | The catalog entry is a superset of this row — the row is what migrates most directly. |
| The rows reach coai-mcp as `COAI_VENDORS` inside `<dataDir>/settings.json`. | `vendors.ts:640` (`vendorsEnv`), `settingsShape.ts:419` (`envBlock`); read by `src_mcp/src/Server/PanelSettings.cs:1139` | An older coai-mcp reads only these keys — the extension must keep writing them (D3). |
| The consultant is one definition per caller kind, with its own vendor/model/endpoint/CLI path. | `consultSettings.ts:48-59`; caller kinds `:23-28` | Replaced by a caller → instance map. |
| Consulting runtimes: codex (no endpoint), claude, antigravity, local — in BOTH halves. | `consultSettings.ts:118`; `src_mcp/runners/Consultation/ConsultantResolution.cs:17` | A consultant on an API key, or the Codex CLI with an endpoint, is NEW server work (E2). |
| Question-consultant rows: model + one prompt, at most 6 on; runtimes = consulting + api. | `qconsultSettings.ts:77`, `:92`; `QuestionResolution.cs:21` | Rows reference an instance instead of carrying a model. |
| Chat model presets carry their own runtime/model; they are NOT per side and NOT sent to the server. | `chatPresets.ts:69`; runtimes `cliChatLaunch.ts:56` | Chat models become per side with the catalog (D8). |
| The Bugz ranking model is one string `rowId/model`, filtered by the row id `local`. | `bugzView.ts:46` | A second local instance is invisible today; the catalog fixes it by construction. |
| Every model list comes from one function. | `models.ts:105` (`modelsFor`); runtimes `models.ts:35` | Reused unchanged by the Models tab. |
| Per-side overlay: the settings listed are stored per side; data folder and side are ALWAYS per side. | `settingsShape.ts:298`, `:342` | `coai.models` joins the overlay list. |
| Effort exists only for API rows; Antigravity encodes it in the model id; Claude and Codex runners pass none; local is ONE env value. | `src_mcp/core/Api/ApiRowSettings.cs:23`; `PanelSettings.cs:471`; `ClaudeRuntime.cs:63`; the codex adapter in `ReviewerRuntime.cs` | Effort per instance needs runner work (E2), measured before any default changes. |
| The Team server's review request has no effort and no system prompt. | `src_server/src/ServerJsonContext.cs:91`; `ContractVersion.cs:32` | A contract bump (E3). |
| Security signals are fixed word lists compiled into the server. | `src_mcp/core/Security/SecuritySignals.cs:37` | Editable words and a card's own words are NEW (E2). |
| In Full code mode, API and local reviewers are told they hold a checkout they cannot read. | `src_mcp/src/Server/Rounds/ReviewerPrompt.cs:89` | Fixed while touching the runners (E2, story 2.6). |
| **A second instance of a CLI vendor cannot run today.** Observed 2026-10-04 on the operator's install: coai-mcp's `providers` answers for the row `claude-2` *"'claude-2' was not found on this machine — install it with: npm install -g @anthropic-ai/claude-code"* — it looks for an executable named after the row's ID. | `RuntimeResolution.NameOf`, `src_mcp/runners/Reviewers/RuntimeResolution.cs:94` (an explicit runtime outranks the id; a row without one falls back to it) | The catalog stores the vendor kind on every instance, so the id never names a program (E1 migration fills it; E2 story 1 refuses an instance without one). RED test first in E2: a `claude-2` row with no runtime field. |
| The MCP server tab says settings apply "when your MCP client next starts it" — false since `PanelServiceHost` rebuilds on every settings-file change. | `panelView.ts:1864`; `src_mcp/src/Server/PanelServiceHost.cs:10-23` | Tail T1. |
| Export/import is format version 1 with an EXACT version check; it reads the base layer only. | `configTransfer.ts:121`, `:191` | A format version 2 with a real migration of v1 (E1). |
| The Settings page: 12 tabs registered as sections, one strip, one stylesheet shared with the sidebar. | `panelView.ts:441` (`PANEL_SECTIONS`), `:3244` (`PANEL_COMMANDS`); `panelSurface.ts:117`; `settingsPanel.ts:53` | Rebuilt as its own page module (E4); the sidebar keeps its own sections. |
| Version gates the page must keep honouring. | `apiRuntime.ts:34` (0.37.0), `apiSettings.ts:26` (0.40.0), `consultSettings.ts:438` (0.23.0), `qconsultSettings.ts:92` (0.41.0), `securityLane.ts:26` (0.41.0), and the role/gate gates (`prompts.ts:120`, `:131`; `rolesPage.ts:449`; `gateScope.ts:18`, `:41`; `commandModels.ts:185`) | One `skew(since, what)` road on the new page (E4). |
| Sizes today: `panelView.ts` 3,398 lines, `panelProvider.ts` 4,297. | — | The new page is new modules; the old sections are deleted, not edited (E10), so the two size plans shrink rather than collide. |

## The design

The accepted mockup is the specification for every screen; this section fixes what the mockup cannot show.

### The catalog entry

```ts
// src_vs_code/src/modelCatalog.ts — the one shape, written to `coai.models` and sent as COAI_MODELS.
interface ModelInstance {
  readonly id: string;              // stable; keys the vault, the ledger, history, colour — today's row id, migrated as is
  readonly name: string;            // display name, the person's
  readonly vendor: VendorKind;      // claude | codex | antigravity | dashscope | xai | deepseek | openrouter | compat | local | remote
  readonly model: string;
  readonly enabled: boolean;
  readonly features: readonly Feature[]; // plan | code | document | feature | security | consultant | qconsult | chat | bugz
  readonly effort?: string;         // the vendor's own spelling; absent = the vendor's / module's default
  readonly thinking?: boolean;      // API modules that can switch it
  readonly systemPrompt?: string;   // absent or empty = none
  readonly timeoutMinutes?: number; // absent = Limits › Reviewer timeout
  readonly connection: Connection;  // cliPath | endpoint + vaultKeyName + dialect | teamServerId + remoteVendor — by vendor kind
  readonly prices?: Prices;         // in / out / cached per 1M; never for remote
}
```

Access (CLI · reads this machine / API key / local engine / Team server) and every "Not offered" reason are DERIVED
from the vendor kind and `shared/runtime-capabilities.json` — never stored, so they cannot disagree with the server.

### Decisions (the operator may overrule any of them)

| # | Decision | Why |
|---|---|---|
| D1 | **A new setting `coai.models`; the old keys are migrated once and then DERIVED from it.** | One source of truth, and an older coai-mcp (or a downgraded extension) still reads exactly the keys it knows. |
| D2 | **Migration keeps every id** (`codex`, `codex-2`, `<server>-<vendor>`…) and is idempotent: it runs only when `coai.models` is absent, writes it, and touches no old key. | Ids key the vault, the spending ledger, round history and colours. A crash mid-migration re-runs it. |
| D3 | **Dual write until the tail.** Every catalog change also rewrites the derived legacy keys (`vendors`, `consultants`, `qconsultRows`, `chatModelPresets`, `bugzModel`, security `runs[].vendor`). For coai-mcp ≥ 0.44.0 the extension also writes `COAI_MODELS`; coai-mcp reads it first and falls back to the legacy keys. | An old coai-mcp keeps working, unchanged; the new one gets the fields the legacy keys cannot carry (effort for CLIs, system prompt, timeout). |
| D4 | **Feature availability is ONE shared file**, `shared/feature-availability.json`, read by both halves — replacing `CONSULTING_RUNTIMES`, `Answering`, `CHAT_RUNTIMES` and the Bugz filter. | Four lists in two languages is how the consultant picker came to offer choices the server refuses. |
| D5 | **The new page is built behind `coai.settingsPreview`** (off by default) until every tab is done; then it becomes the Settings page and the old sections are deleted (E10). | Half a new page beside half an old one is worse than either; a preview switch lets each epic ship and be tried. |
| D6 | **Effort defaults stay empty** (the vendor's or module's own default) for every runtime that gains effort. A default is changed only after a measurement of ≥ 3 runs per vendor on the product path. | Measured: higher effort is not better (`RESULTS_model_comparison.md`, agy Flash Low beat High); a local model with thinking on never answered. |
| D7 | **Bugz has one ranking model** — ticking it moves it. | Today's setting is one model; several ticks would be ambiguous. |
| D8 | **Chat models become per side**, like every catalog entry; chat PROMPT presets stay shared. | The catalog is per side; a chat on WSL opens a WSL CLI. |
| D9 | **Consultant over an API key** is a multi-turn API runner whose transcript coai-mcp keeps (the vendor keeps none). | The operator's own example (GLM high as consultant) needs it; prompt-only is shown as a limit, never hidden. |
| D10 | **A check of any instance** (✓ Check) is a coai-mcp one-shot mode, `--check-model <id>` — one short paid turn in a scratch folder, confirmed first. | The consultant's Check already proved the shape; a mode on the binary keeps the page and the server from disagreeing. |
| D11 | **The page's "?" texts and every help article move with the tabs**, in all five languages, in the same epic that moves a control. | A help article naming a tab that no longer exists is the stale translation the family already measured. |

### Growth surfaces

| Surface | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| `coai.models` | ≤ 64 instances (refused past it, saying why) × ≤ 1 KB, plus system prompts capped at 8 KiB each → realistically < 20 KB, worst 0.5 MB in `settings.json` | the person, per instance | written whole; a failed write keeps the old value (the existing refused-write snap-back) |
| `COAI_MODELS` in `<dataDir>/settings.json` | same as above | rewritten on every change | coai-mcp keeps the last good file (existing stamp check) |
| First-seen version per "new" control (`globalState`) | one entry per release that adds controls, ~10 bytes | entries older than 60 days are pruned on activation | none — a missing entry means "not new" |
| ✓ Check results and transcripts | one record + one transcript per instance, overwritten | removed with the instance | a Check is detached and stamped `running` before it starts; a startup sweep marks a dead owner's record `interrupted` (durable-status rule) |
| Migration backup `coai.migratedFrom` | one copy of the old keys, a few KB | Tail T5, when dual write ends | — |
| `new_design/` and its Sonar exclusion | ~6,000 lines, never shipped | E10 deletes both | — |

## Build order — ten epics, one pull request each, one gate round each

Each epic ships on its own: the old page keeps working until E10, and every coai-mcp/Team-server change is read
behind a version gate in both directions.

### E1 — The catalog as data (extension; nothing visible changes)
1. `modelCatalog.ts`: the type, parsing with "absent means the shipped value", and validation (unknown vendor,
   duplicate id, > 64 instances — each refused naming the legal values).
2. `catalogMigration.ts`: `catalogFrom(settings)` from every legacy key, ids kept; `legacyFrom(catalog)` back.
   Property tests: `legacyFrom(catalogFrom(x))` equals `x` for every fixture shape seen today (taken from the
   repo's existing settings fixtures and the operator's current settings, ids anonymised).
3. `shared/feature-availability.json` + its generator for TS and its loader for C# (the runtime-capabilities
   pattern); both halves' suites assert against it.
4. Per-side overlay: `coai.models` joins `OVERLAID_SETTINGS`; every catalog write goes through
   `sideConfig.saveSetting` (no direct `config.update(…, Global)` — the defect class this page had six times).
5. `envBlock` writes the derived legacy keys always and `COAI_MODELS` for coai-mcp ≥ 0.44.0.
6. Export/import format v2 carries `models`; v1 files are MIGRATED on import (the first real migration), never refused.

### E2 — coai-mcp reads the catalog and gains the new capabilities (release mcp 0.44.0)
1. `PanelSettings` reads `COAI_MODELS` first, legacy keys otherwise; an unknown vendor kind is REFUSED by name,
   never mapped to codex.
2. Per-instance timeout for every runtime; system prompt per instance, prepended by every runner (review, consult,
   question consult); `EveryAdapterRecordsWhatItLaunchedTests` extended to pin both.
3. Effort: codex `-c model_reasoning_effort=<level>`, claude's effort flag (verify the CLI's flag and levels before
   writing — a measured cell, not documentation), local per instance (`LocalAsk`), remote passes it (E3). Defaults
   stay empty (D6).
4. Consultant on an API key and on the Codex CLI with an endpoint (D9); `ConsultantResolution.Consulting` and the TS
   mirror replaced by the shared file (D4).
5. Security lane: signal words from settings (shipped words when absent), a card's own words and `/regex/` with a
   bounded, non-backtracking matcher and a timeout; refused patterns are reported by name.
6. `ReviewerPrompt` tells each reviewer what IT holds (checkout or diff), per runtime, not per round
   (`ReviewerPrompt.cs:89`). RED test first.
7. `--check-model <id>` one-shot mode (D10). **Added to the one-shot list in `.agents/PROJECT.md`** in the same
   change, exit codes per that rule (never 64 for a known mode).

### E3 — The Team server carries effort and a system prompt (contract v2)
1. `ReviewRequestDto` gains `Effort` and `SystemPrompt`; `ContractVersion.Current = 2`.
2. Measured against the OLD other side both ways: a v1 client against a v2 server and a v2 client against a v1
   server, each with a sentence the person sees (never a silent drop). Deploy is manual and needs the operator's
   go-ahead (task-lifecycle § 3).

### E4 — The new Settings page shell (behind `coai.settingsPreview`)
1. A page module of its own (pure page + thin host, the roles-page pattern), reusing `pageDocument`'s CSP and nonce,
   `tabStrip` + `tabKeys` (roving tabindex), `selectSearch`, the busy marks, focus restore and the refused-write
   snap-back — none of it rewritten.
2. Six tabs with sub-tabs; the open tab and sub-tab remembered by the host; `coai.openSettings` maps every OLD tab id
   to its new place (deep links in help and in server messages keep working).
3. Layout: two columns from 1100 px, one below; side-by-side blocks one height (CSS subgrid for cards).
4. Shared helpers: `help(key)` from `HELP`; `skew(since, what)` — every `*_SINCE` gate on the page goes through it;
   `newTag(controlId)` from the first-seen record; one confirm dialog.

### E5 — Models
Cards, the add dialog grouped by where a model runs, duplicate, remove (listing every reference; the last plan or code
model cannot leave), on/off, the three filter rows (use, effort, vendor) plus access and text, the CLI's
▶ open / ⤓ install / ⟳ update, coai-mcp's verdict, where a list came from and ask again, the API "runs it at", the
local-engine warning and WSL fix, per-card "this coai-mcp ignores…" notes. Everything as in the mockup's Models tab.

### E6 — Reviews
Stages; **Roles & prompts** replacing the Review roles page, with ONE switch per role (migrating `roleEnabled` and the
catalog's `active`, D-row in E1's migration), deletion's confirmation and reserved ids, a role off until it has a
question; Prompts per round; The gate; **Commands** replacing the Gate commands page; Limits.

### E7 — Consultants
The consultant per caller from instances ticked "Consultant" (same-vendor shown, never refused; stranded picks shown);
health per side, the paid Check confirmed, agy's allow rule; the question consultant's rows referencing instances,
prompts, folders and limits.

### E8 — Security lane and Chat
Pairs from instances ticked "Security lane"; on each card the signals in two columns, own words, prompt text in place;
the routing table with editable words; Try it — the matcher is the SAME code as the server's, through
`--check-security` or a shared module, so the page cannot disagree with a round. Chat from instances ticked "Chat",
per side (D8); prompt presets inline.

### E9 — Setup
Vendor keys counting every instance's key; the CLI table; Team servers with every state; the MCP server's states and
clients; the data folder's Change and Move flows rebuilt on today's `dataCommands.ts` logic (unchanged), with the
"moved from" record surviving a reload; This side with the export note.

### E10 — The switch-over, docs and release
1. `coai.settingsPreview` removed; the new page IS Settings; the twelve old section builders and their commands
   deleted (the size plans' targets shrink — see *Boundaries*).
2. Help in five languages, `research/module_extension.md`, `research/module_server.md`, `architecture.md`, CHANGELOG,
   POST_DEPLOY (one item: "a migrated install opens on Models with every old reviewer, consultant and chat model").
3. `new_design/` and its Sonar exclusion deleted.
4. Release: mcp 0.44.0 first (E2), the Team server deploy when the operator says (E3), then the extension.

## Test plan

- **Every epic: RED first** for each behaviour, then GREEN; the extension suite (`npm test`), `test:contract`,
  `test:seam`, the C# suites as MTP executables — never `dotnet test`.
- **Migration (E1):** fixture round-trips; idempotence (run twice = once); a crash between write and mark re-runs
  cleanly; ids, prices, keys, endpoints, CLI paths and remote fields survive; the env block for an unchanged setup is
  byte-identical to today's.
- **Skew (E1–E3):** new extension + mcp 0.43.0 (legacy keys only, new controls show "ignored" notes); new mcp + old
  extension (legacy keys read); Team server v1 ⇄ v2 both directions — each with the sentence the person sees.
- **Pages (E4–E9): tested by RUNNING them** (`bundledPage.test.ts`, the page harnesses) — no new behavioural
  assertion over page source text (PROJECT.md). The mockup's `check.mjs` flows are the acceptance list: every one of
  its 61 checks has a counterpart page test.
- **Layout:** `scripts/render-page.mjs` screenshots of every tab at 1920 and 900 px, dark and light, read before
  each PR; equal heights asserted structurally (the subgrid rule), the pictures attached to the PR.
- **Runners (E2):** each runtime's argv/body pinned with effort, system prompt and timeout; the matcher's timeout and
  refused patterns; `--check-model` exit codes.
- **The real editor harness** (`npm run test:host`) for opening the page, a deep link, and a write that survives a
  reload.

## Boundaries with other plans

| Item | This plan | The other plan's part |
|---|---|---|
| Splitting `panelProvider.ts` | Removes the twelve settings sections' handlers in E10 instead of moving them | [PLAN_the_panel_provider_is_too_big.md](PLAN_the_panel_provider_is_too_big.md) keeps its sidebar clusters; its settings clusters (vendors, Team servers, local engines, prices) are superseded — **this plan goes first** |
| Splitting `panelView.ts` / `roundsLog.ts` | The settings half of `panelView.ts` is deleted in E10 | [PLAN_two_files_outgrew_the_rule.md](PLAN_two_files_outgrew_the_rule.md) keeps the sidebar half and `roundsLog.ts` |
| Local effort in the panel | E2/E5 expose it per instance | [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) keeps per-origin acknowledgement, vLLM keys, 401 reading, `num_ctx` refusal |
| A model's liveness | E5 shows coai-mcp's verdict and ✓ Check | [PLAN_provider_liveness.md](PLAN_provider_liveness.md) owns the three liveness states and their cache; E5 draws them |
| How a probe says it is working | E5 uses it | [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) owns it |
| Arrow keys on tab strips | E4 consumes `tabKeys` | [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) steps 2–4 |
| The chat presets page writing on every keystroke | E8 replaces the page | [PLAN_chat_presets_write_every_keystroke.md](PLAN_chat_presets_write_every_keystroke.md) is superseded once E8 lands |
| Which vendor answers which caller by default | E7 keeps the shipped map byte-identical | [PLAN_consultant_defaults_from_phase_0.md](PLAN_consultant_defaults_from_phase_0.md) owns the measurement |
| The Security lane's calibration | none | [PLAN_security_lane_calibration_tail.md](PLAN_security_lane_calibration_tail.md) |

Disjoint from everything else in `todo/`. Each plan in the table gets the same row, pointing back here.

## Tails — after the redesign

| # | Tail | Why it waits |
|---|---|---|
| T1 | The MCP server tab's sentence "reads them when your MCP client next starts it" (`panelView.ts:1864`) is false since `PanelServiceHost` rebuilds on every settings-file change | E9 writes the true sentence on the new page; T1 fixes the old page only if E10 slips |
| T2 | **No security check triggers on `crypto`** — the signal is detected, no preset runs on it | A catalog change and a measurement; the routing table makes it visible first |
| T3 | Export writes the shared settings only; a side's own models are not in the file | Export v2 (E1) carries the catalog of the side that exports; per-side export of the REST is a separate decision |
| T4 | The mockup's own files (`app.js`, `reviews.js`, `setup.js`) are over 800 lines | The mockup is deleted in E10; the product modules are born under the limit |
| T5 | Dual write (D3) ends, `coai.migratedFrom` is dropped | Once coai-mcp < 0.44.0 is no longer supported |

## Definition of Done

- [ ] `coai.models` is the only place a model is added, edited or removed; every feature picks from it.
- [ ] A migrated install shows every reviewer, consultant, question-consultant row, chat model, Bugz model and
      security pair it had, with the same ids, keys, prices and paths — proved by fixtures and on the operator's
      own install.
- [ ] coai-mcp 0.43.0 keeps working with the new extension (legacy keys), and the page says which controls it ignores.
- [ ] Effort per instance reaches every runtime but Antigravity; system prompt and timeout reach every runner; the
      Team server carries both at contract v2 — each pinned by a test, each measured against the old other side.
- [ ] Every control and state the coverage audit listed (the mockup's Design notes § 7) exists on the new page.
- [ ] Two columns from 1100 px, one below; side-by-side blocks one height; size and brightness on every page; "new"
      for 7 days — checked in screenshots at both widths, both themes.
- [ ] Help, `research/`, CHANGELOG and POST_DEPLOY describe the new page; `new_design/` and its Sonar exclusion gone.
- [ ] Every epic went through a review round (the coai gate, or the operator's own-reviewer substitute while its
      vendors are out of quota — said in the PR) before its pull request; every reviewer thread resolved.
- [ ] Releases cut from tags after merge; post-deploy checks run against the installed builds.
