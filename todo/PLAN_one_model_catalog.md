# PLAN — One model catalog: the Settings page rebuilt around models you add once

> Status: **in progress, 2026-10-07 — E1 merged (PR #681); E2 merged (PR #686; its release, the Team server deploy and
> the measured live calls wait on the operator); E3 merged (PR #687); E4 merged (PR #688, its consultations and code
> gate passed); E5 open — R7 first, then extract-before-delete, with the rollout order as its own milestone.** The design is accepted: the clickable mockup in
> [`new_design/`](../new_design/README.md) (open `new_design/index.html`; `node new_design/check.mjs` drives it, 61
> checks). Scope: the extension's Settings page (`src_vs_code/src`), the settings it writes and how they reach
> coai-mcp, coai-mcp's runners where the design adds a capability (`src_mcp`), the Team server's review request
> (`src_server`), help in five languages, and the docs. The sidebar and the Review rounds page are out of scope, with
> ONE stated exception: the sidebar's Bugz ranking picker reads the catalog (E5.1).
>
> Reviewed 2026-10-04 before any code: the coai plan round (session `652ef774`, 2 reviewers, `good_enough`, 15
> findings, all accepted) and two of the session's own reviewers (an architecture critic and a fact-checker). What
> they changed is in *What the review changed* at the end. Every `file:line` was read at `origin/main` = `e193822a`
> (extension 0.63.0, coai-mcp 0.43.0). Line numbers move; re-read before cutting.
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
9. **Nothing the system does today may be lost** — the mockup's Design notes § 7 is the audited list.

## The operator's rulings (2026-10-04)

| Question | Ruling |
|---|---|
| One catalog for every feature? | **Yes** — reviews (plan, code, document, feature), security lane, consultant, question consultant, chat, Bugz ranking. |
| A different-vendor consultant | Ideal, **never blocked** — the page says when the caller and the consultant share a vendor. |
| A different effort for one model | **A second instance**, no per-feature override. |
| A key per instance | **Yes** — two instances may share a key or use two accounts. |
| Scope | **The Settings page only.** The sidebar and the Review rounds page are not touched (one stated exception above). |
| Tabs | **Six**: Models · Reviews · Consultants · Security lane · Chat · Setup; Reviews, Consultants and Setup have sub-tabs. |
| coai-mcp | Changes once the UI is accepted (it is). |
| Work order | Mockup → operator review → this plan → implementation. **The redesign first; the tails after.** |
| Security lane triggers | On each card under the prompt text: the signals as **two columns of checkboxes**, plus the card's **own words** (a word, a phrase, a piece of code, or a `/regex/`). |
| Review | The coai gate (back since 2026-10-04 evening), with the session's own reviewers alongside. |

## What is true today (verified at `e193822a`)

| Fact | Where | Consequence for this plan |
|---|---|---|
| A reviewer is a `Vendor` row: id, runtime, model, enabled, plan/code/document/feature flags (`document` is THREE-way: absent means "follows plan, but NO for a Team server"), baseUrl, remoteVendor, teamServerId, executablePath, prices, vaultKeyName, dialect, effort/thinking/reviewMinutes (api rows only). | `src_vs_code/src/vendors.ts:16-135`, three-way `:34-47` | **This row already IS an instance list** (`codex`, `codex-2`…). It is widened, not replaced (D1). |
| The rows reach coai-mcp as `COAI_VENDORS` inside `<dataDir>/settings.json`; process env outranks the file key by key. | `vendors.ts:640`; `settingsShape.ts:419`; `PanelSettings.cs:1139`; `SettingsFile.cs:57` | The wire stays these keys (D3) — no new key, so no new precedence rule. |
| **coai-mcp ignores JSON fields it does not know** — no `UnmappedMemberHandling` anywhere in `src_mcp` or `src_server`. | `src_mcp/src/Server/SettingsJsonContext.cs:30` (`VendorDto`) | A widened row is read by 0.43.0 as today's row: old servers keep working without a second wire. |
| The consultant is one definition per caller kind; a definition with `runtime: ''` is a *reference* to a reviewer row (resolution (a) row with that id, (b) a runtime of that name, (c) refused); an ABSENT caller means the shipped pair. | `consultSettings.ts:48-59`, `:139-144`; resolver `ConsultantResolver.cs:76` | Storage becomes references to rows; the wire carries resolved definitions (every server ≥ 0.23.0 reads them). "Unset → shipped" stays absent, so a pristine install still writes nothing. |
| Consulting runtimes: codex (no endpoint), claude, antigravity, local — in BOTH halves. | `consultSettings.ts:118`; `ConsultantResolution.cs:17`, codex-endpoint arm `:30` | A consultant on an API key or through the Codex CLI with an endpoint is NEW server work (E2.3). |
| Question-consultant rows have their own id, ONE prompt, enabled; at most 6 on; a row with no runtime borrows the reviewer row of that name. | `qconsultSettings.ts:30-41`, `:77`, `:92`; `QuestionAdmission.cs:134-135`; `QuestionResolution.cs:21` | Rows keep their id, prompt and switch and refer to an instance; several rows may refer to one instance. |
| Chat model presets carry their own runtime/model, `main` and `startingPrompt`; they are NOT per side and NOT sent to the server. | `chatPresets.ts:69-94`; `cliChatLaunch.ts:56`; absent from `settingsShape.ts:298-323` | Chat models become rows ticked Chat (per side, D8); the old preset key is frozen, never rewritten, so a downgrade still has it. |
| The Bugz ranking model is one string `rowId/model`, and both halves match the ROW ID `local`. | `bugzView.ts:46`; `panelView.ts:529-534`; `src_mcp/…/RankingModels.cs:41` | A second local instance is refused by 0.43.0 — fixed by runtime in E2.1, said as "ignored" before. The picker lives in the SIDEBAR (E5.1). |
| Every model list comes from one function; the runtimes include the retired `gemini`. | `models.ts:105`, `:35` | Reused unchanged; `gemini` rows still parse (they are migrated to antigravity as today, `vendors.ts:429-441`). |
| Per-side overlay stores whole values per key; data folder and side are ALWAYS per side; reads merge WORKSPACE values. | `settingsShape.ts:298`, `:342`, `:356-359`; `sideConfig.ts:30`, `:80` | **A cloned repository's `.vscode/settings.json` can set `coai.vendors` today** — and with system prompts, endpoints and regexes it would steer more. E1.1 reads model-bearing keys from the user layer and the side overlay only. |
| Effort exists only for API rows; local is ONE env value (`COAI_LOCAL_REASONING_EFFORT`) though `LocalRuntime` already passes a per-call value; Antigravity encodes effort in the model id; Claude and Codex pass none. | `ApiRowSettings.cs:23`; `PanelSettings.cs:354`, `:899`; `LocalRuntime.cs:152`; `AntigravityRuntime.cs:21-23`; `ClaudeRuntime.cs:63-94`; codex `ReviewerRuntime.cs:336-366` | Effort per instance is plumbing for local, new flags for codex and claude (E2.2), measured before any default changes. |
| The Team server's review request has no effort, system prompt or timeout; a v1 server SILENTLY ignores unknown fields and serves a client claiming a newer contract. | `src_server/src/ServerJsonContext.cs:91-98`; `ContractVersion.cs:41`, `:93` | Contract v2 (E2.5); the CLIENT reads `X-Coai-Contract` and says when a v1 server would drop a field. |
| Security signals are fixed word lists; a NonBacktracking regex with a 1 s timeout already exists for SQL shapes. | `SecuritySignals.cs:32-55` | Editable words and a card's own words are NEW (E2.4), on the same engine. |
| In Full code mode, API and local reviewers are told they hold a checkout they cannot read: the material is decided once per ROUND. | sentence `ReviewerPrompt.cs:89`; decision `RosterBuilder.cs:181-183` | Fixed per runtime in `RosterBuilder` (E2.1). |
| **The health probe of a second instance of a CLI vendor runs the ROW ID as the program.** Observed 2026-10-04: `providers` reports `claude-2` *"was not found — install it with npm …"* while the same row answered the plan round. | `VendorProbe.cs:143` → `IReviewerRuntime.DefaultExecutable => Provider` (`ReviewerRuntime.cs:226`); only Antigravity overrides it (`AntigravityRuntime.cs:126`); launches use the literal CLI (`ClaudeRuntime.cs:72`) | Every second instance looks broken in `providers`, the consultant health check and the Team server catalog. RED test first in E2.1: a `claude-2` row WITH `runtime: claude` probes `claude`. |
| The MCP server tab says settings apply "when your MCP client next starts it" — false since `PanelServiceHost` rebuilds on every settings-file change. | `panelView.ts:1864`; `PanelServiceHost.cs:5-23` | Tail T1; the new page says the true sentence. |
| Export/import is format version 1 with an EXACT check; it reads the base layer only and strips `executablePath`. | `configTransfer.ts:15`, `:27`, `:121`, `:191` | Format v2 with v1 migrated on import (E1.5). |
| An older extension refuses to overwrite a settings file stamped by a newer one. | `serverSettingsSync.ts:241-280` | A downgraded extension's edits never reach coai-mcp — said in POST_DEPLOY's downgrade path, tested. |
| The Settings page: 12 sections on one strip, the sidebar's whole document (`pageDocument` bundles the sidebar CSS and script with the CSP/nonce). | `panelView.ts:441-474`, `:566`, `:3244`; `panelSurface.ts:117`; `settingsPanel.ts:53` | The new page EXTRACTS the CSP/nonce part rather than reusing `pageDocument` whole (E3.1). |
| Version gates on the page. | `apiRuntime.ts:34` (0.37.0), `apiSettings.ts:26` (0.40.0), `featureGate.ts:28` (0.39.0), `consultSettings.ts:438` (0.23.0), `qconsultSettings.ts:92` (0.41.0), `securityLane.ts:26` (0.41.0), `prompts.ts:120` (0.18.10), `:131` (0.18.13), `rolesPage.ts:449` (0.19.0), `gateScope.ts:18` (0.33.0), `:41` (0.34.0), `commandModels.ts:185` (0.33.0) | One `skew(since, what)` road (E3.1). Each new capability gets its OWN constant (E2), not one "0.44.0". |
| Screenshots: `render-page.mjs` is Dark Modern only and knows the existing pages. | `src_vs_code/scripts/render-page.mjs:11-13`, `:125-157` | E3.1 adds the new page and a light theme. |
| Sizes: `panelView.ts` 3,398 lines, `panelProvider.ts` 4,297. | — | The new page is new modules; the old sections and pages are deleted in E5. |

## The design

The accepted mockup is the specification for every screen; this section fixes what the mockup cannot show.

### D1 — The catalog IS `coai.vendors`, widened

The reuse-first answer to the question "where do instances live": in the list that already holds them. A row gains:

| Field | Meaning | Absent means |
|---|---|---|
| `name` | The display name, the person's | the id |
| `uses` | The non-review features: `security`, `consultant`, `qconsult`, `chat`, `bugz` | none |
| `effort` | Every runtime but Antigravity, in the vendor's own spelling, validated against the shared levels (D4) | the vendor's or module's default |
| `systemPrompt` | ≤ 8 KiB | none |
| `timeoutMinutes` | Non-API rows (API rows keep `reviewMinutes`, whose absence is the module's calibrated value) | Limits › Reviewer timeout |
| `chatStartingPrompt` | What the composer opens with for this model | none |

The review features stay the existing flags — `plan`, `code`, `feature`, and the three-way `document` — so nothing
that reads a row today changes meaning. The page's "Use for" ticks are those flags plus `uses`. `vaultKeyName` stays
the key's name (absent = the id), so two instances share a key by naming it, Duplicate copies it, and removing an
instance never deletes a vault entry (coai never writes the vault).

**What crosses.** `vendorsEnv` writes a WHITELIST of fields (`vendorsWire.ts`, `vendorsEnv`), never the stored row, so none of the
new fields reaches `COAI_VENDORS` in E1. A row that reviews no stage (`plan`, `code` false, `document` off, no `feature`)
and whose `uses` do not include `security` is LEFT OUT of `COAI_VENDORS`: its consumers receive it resolved
(`COAI_CONSULTANTS`, `COAI_QCONSULT_ROWS`) or never cross at all (chat, Bugz). That keeps the env block of a migrated
setup byte-identical (plan-round finding 7, session `ac973900`). The prompt fields (`systemPrompt`,
`chatStartingPrompt`, both ≤ 8 KiB) never travel in an environment variable — 64 rows × 8 KiB is past Windows' 32,767-
character limit for one variable; E2.2 carries them through the settings file with a total-size budget refused by
validation (findings 4, 9).

### D2 — Everything else refers to a row by id

| Feature | Stored | On the wire (unchanged keys) |
|---|---|---|
| Consultant | per caller: a reference `{ vendor: <row id>, runtime: '' }`; ABSENT = the shipped pair | `COAI_CONSULTANTS` with the reference RESOLVED to a definition (every server ≥ 0.23.0), byte-for-byte what the stored definition wrote before |
| Question consultant | rows keep `id`, `prompt`, `enabled`; `vendor` = row id, `runtime: ''` | `COAI_QCONSULT_ROWS` resolved the same way (≥ 0.41.0) |
| Security lane | `runs[].vendor` = row id (already) | unchanged |
| Bugz | `bugzModel` = `<row id>/<model>` (already) | unchanged; the row-id-`local` match becomes a runtime match (E2.1) |
| Chat | `coai.chatModel` = row id (the key exists); the instances are rows with `uses: chat` | extension only |

### D3 — One write road, one migration, no dual store

- **Every write of a model-bearing setting goes through `sideConfig.saveSetting`** — including the OLD page's
  consultant, question-consultant and chat writes, switched in E1.4 to write rows and references. The two blocking
  findings (the old page writing keys the catalog never sees; a later catalog write silently reverting them)
  disappear because there is nothing to keep in sync: one list, one road.
- **Migration** (E1.3) runs once per settings LAYER (the user layer and each side overlay, each on its own), only
  where a definition is not yet a reference. Deterministic ids, never random: a consultant definition becomes row
  `consult-<caller>`, a question row's own definition `ask-<rowId>`, a chat preset `chat-<presetId>`, a Bugz model
  that differs from its row's model `bugz-<rowId>`. **Merge rule:** a definition joins an EXISTING row only when
  runtime, model, baseUrl, executablePath, dialect and vaultKeyName are all equal; otherwise it gets its own row. An id
  clash takes the next free `-N` (the existing `freeVendorId`, `vendors.ts:503`) and the fixture says so. The new row
  keeps the definition's key by setting `vaultKeyName` to the name it was stored under, so its vault entry and
  ledger name do not move. Running it twice changes nothing.
- **Migrated rows review nothing.** Every row the migration adds is written with `plan: false`, `code: false`,
  `document: false` and no `feature`, so neither this build nor an older one runs it in a round; the old page hides a
  row that reviews nothing and has a `uses` (finding 7).
- **Backup and restore:** before the first write, every key the migration writes in that layer — `vendors`,
  `consultants`, `qconsultRows` (absence recorded as absence; `chatModel` and `bugzModel` join when their sources
  move, E4.3 and E2.1) — is copied to that layer's own
  `migratedFrom` (the user layer's `coai.migratedFrom`; a side's overlay entry `migratedFrom`). Written ONCE, never
  overwritten by a later run. A command *ConnectOtherAIs: Restore settings from before the catalog* names the layers it
  will change, puts every key back exactly, and marks the layer `restored` so activation does not migrate it again until
  the person asks (findings 0, 1, 8). Kept until one release after the switch-over (T5).
- **Order and interruption:** backup → rows → references → the layer's marker `catalogMigration: "migrated"`
  (`"restored"` after a restore), written last. A restore writes in the reverse order — references, then rows, then the
  marker — so a stopped restore never strands a reference. A rerun finds an already-added row by its exact launch
  fields (the merge rule), preferring the deterministic id among the matches, so an interrupted run is
  finished, not duplicated. A layer that cannot be written (refused write, unreadable overlay) is skipped and reported,
  never blocks activation. A migration that would pass 64 rows writes nothing in that layer and says how many it needed
  (findings 2, 6, 11). Two windows activating at once both reach the same final state, because every write is a
  deterministic function of the backup.
- **A reference to a missing row** (deleted on the old page, or a half-run migration) keeps today's behaviour: both
  halves already name it unavailable, which says more than leaving it out would (finding 11; recorded as built in E1.3).
- **Workspace values.** The seven model-bearing keys (`securityLane` and `chatModelPresets` included) are read through ONE reader (user layer + overlay, via `inspect()`);
  a scan test refuses any other read of them. A workspace or folder value is ignored, and a one-time notice names the
  key and the file and offers to copy it to the user layer (findings 3, 10).
- **Downgrade, measured on the code:** the current build parses rows through `vendorsFrom` (`vendors.ts:332-376`),
  which keeps only typed fields, and writes the parsed list back (`panelProvider.ts:1859`), so an older build's first
  reviewer edit drops `name`, `uses`, `effort` (non-API), `systemPrompt`, `chatStartingPrompt`. Every feature still runs
  the same model: references are by id and the legacy (a) resolution ignores `enabled` (`consultSettings.ts:338-361`).
  On the next activation of this build, a row referenced by a consultant, question or chat setting regains that `uses`,
  and a `chat-*` row regains its starting prompt from the frozen `chatModelPresets`; names and system prompts are lost
  and POST_DEPLOY says so. No second store (finding 12).

### D4 — One shared file says what each runtime can do

`shared/feature-availability.json` — generated for TS, loaded by C#, both suites asserting against it (the
`runtime-capabilities.json` pattern) — holds: which runtimes serve which feature (replacing `CONSULTING_RUNTIMES`,
`Answering`, `CHAT_RUNTIMES`, `RANKING_VENDORS`), and the legal effort levels per runtime and API model. A write of an
effort the file does not list is refused naming the legal values.

### D5 — The new page is built behind a preview switch

`coai.settingsPreview` (user scope, never overlaid) makes *Open Settings* open the NEW page instead of the old one —
never both, so two webviews never write the same keys at once. The preview is offered only from E3 (Models) on.

### D6 — Effort defaults stay empty

The vendor's or module's own default, for every runtime that gains effort, until a measurement of ≥ 3 runs per vendor
on the product path says otherwise (`RESULTS_model_comparison.md`: agy Flash Low beat High; a local model with thinking
on never answered). Each runtime's flag is pinned in argv AND observed taking effect once, recorded beside the code.

### D7–D11

| # | Decision | Why |
|---|---|---|
| D7 | **Bugz has one ranking model** — ticking it moves it. | Today's setting is one model. |
| D8 | **Chat models become per side**; chat PROMPT presets stay shared. A model's effort and system prompt apply to chat too (chat runs in the extension). | The catalog is per side; a chat on WSL opens a WSL CLI. |
| D9 | **A consultant on an API key** is a multi-turn API runner; coai-mcp keeps its transcript (the vendor keeps none), retired with the consultation. | The operator's own example (GLM high as consultant). |
| D10 | **✓ Check of any instance** is a coai-mcp one-shot mode `--check-model <id>`, built on `ConsultCheckState`'s durable record (exclusive open, owner = pid + process start time, `already-checking`, a startup sweep, never settled from another side, the config hash stamped so an edit mid-check is not credited), one at a time per instance, a hard timeout that kills the process tree, the scratch folder removed in `finally`, confirmed first as a paid turn. | The consultant's Check proved the shape; generalising it is reuse, not a second mechanism. |
| D12 | **A thinking switch only where the model has one** (the operator, 2026-10-05: "for every model, check whether a thinking mode is available — then give an on/off switch"). Every card asks its runtime's answer, the way effort does (D4): `shared/feature-availability.json` gains a `thinking` row per runtime with a source — `probe` for api rows and local engines (the module's or engine's `thinkingSwitchable`, judged per MODEL, so a model the module was not measured on shows no switch), `unmeasured` where nobody has shown a flag (codex), `none` where the runtime has no such switch (claude — its depth is `--effort`; antigravity). The card draws the on/off switch only when the answer says switchable; otherwise one line saying why ("thinking cannot be switched off for this model", or "not measured for codex yet"). The value crosses only to a binary that lists it in `--features`. | The api card has drawn exactly this since S3.8 (`apiSettingsView.thinkingControl`); the rule widens to every runtime rather than a second mechanism. |
| D11 | **Help moves with the tabs**, in all five languages, in the epic that moves a control; `coai.openSettings` maps every OLD tab id to its new place. | A help article naming a tab that no longer exists is the stale translation already measured. |

### What the legacy wire cannot carry, and what the page says

| Catalog fact | coai-mcp 0.43.0 sees | The page says (until 0.44.0 is installed) |
|---|---|---|
| Effort on a CLI row, a system prompt, a non-API timeout | the row without them | "this coai-mcp ignores: its effort, its system prompt, its own time limit" on the card |
| A consultant on an API key or through the Codex CLI with an endpoint | a definition it refuses by name | on the Consultants tab, "needs coai-mcp 0.44.0" |
| A Bugz model on a second local instance | refused (row id ≠ `local`) | "needs coai-mcp 0.44.0" on the card |
| Editable signal words, a card's own words | the shipped words only | on the routing table and the card |
| A model used only for the consultant, question consultant, chat or Bugz | nothing — the row is left out of `COAI_VENDORS`; its consumer receives it resolved | nothing; it is correct |

### Growth surfaces

| Surface | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| Rows in `coai.vendors` | ≤ 64 (refused past it), each ≤ 1 KB + a system prompt ≤ 8 KiB → realistic < 20 KB, worst ~0.6 MB of `settings.json` | the person | written whole by `saveSetting`; a refused write snaps back |
| Migration backup `coai.migratedFrom` | one copy per layer, a few KB | T5 | the migration is idempotent; the backup is written BEFORE the rewrite |
| First-seen version per "new" control (`globalState`) | one entry per release with new controls, ~10 bytes | pruned after 60 days on activation | — |
| ✓ Check records, transcripts, scratch folders | one record + one transcript per instance, overwritten; a scratch folder only while a check runs | removed with the instance; scratch removed in `finally` and swept at startup | D10's sweep ends a dead owner's record as `interrupted` |
| API-consultant transcripts | ~2–20 KB per turn × ≤ 5 turns × ≤ 10 consultations per session ≈ ≤ 1 MB per session | deleted when the consultation is closed or idle-closed; anything older than 14 days swept on start | the existing consultation sweep marks it interrupted |
| `new_design/` and its Sonar exclusion | ~6,000 lines, never shipped | E5.3 | — |

## Build order — five epics, one pull request and one gate round each

Each epic ships on its own; the old page works until E5. Every coai-mcp/Team-server change is read behind its own
version gate in both directions.

### Epic 1 — The instance list grows into the catalog (extension + shared; nothing visible changes)
1. **Widen the row** (D1): the new fields, parsing with "absent means the shipped value", validation (> 64 rows,
   prompt > 8 KiB, unknown effort — each refused naming the legal values), and model-bearing keys read from the user
   layer and the side overlay only, never a workspace or folder value. RED test: a workspace `.vscode/settings.json`
   carrying `coai.vendors` changes nothing.
2. **`shared/feature-availability.json`** (D4) with its TS generator and C# loader; the four runtime lists in the
   extension replaced by it (the C# side follows in E2.1). **Frozen to today's lists in E1**: until E2.1 reads the file,
   coai-mcp keeps its own copies, so the existing TS-against-C# mirror tests stay and compare the file to them
   (finding 13).
3. **The per-layer migration** (D3): deterministic ids, the merge rule, the clash rule, backup once, the marker last,
   the restore command, the 64-row check, an unwritable layer skipped. Fixtures: today's shapes from the repo's
   settings fixtures and the operator's own settings (anonymised) — overlapping reviewer, consultant, question and chat
   entries, a forked side whose overlay predates migration, a `gemini` row, a codex row with an endpoint; migrate twice
   = once; a run stopped after each write and rerun reaches the same state; restore → reload → still restored;
   downgrade (fields stripped as `vendorsFrom` strips them) → rerun → nothing duplicated, `uses` regained.
   **As built (2026-10-04), two sources moved to the epics that change their readers:** chat presets go to E4.3 —
   saved conversations refer to a preset by id (`chatConfig.ts:237`, `chatHooks.ts:229`), so a row `chat-<id>` would
   move a resumed conversation onto another model; the Bugz model goes to E2.1 — ranking accepts only the row id
   `local` on both halves (`RANKING_VENDORS`, `RankingModels`), so a `bugz-local` row would be refused until the
   runtime match lands. E1.3 moves consultant and question-consultant definitions. A consultant never becomes a
   reference equal to its caller's shipped pair (that caller would drop off the wire). A reference to a missing row
   keeps today's behaviour — both halves already name it unavailable — rather than being dropped from the env block.
4. **One write road**: the old page's consultant, question-consultant and chat writes switched to rows + references
   through `saveSetting`; the env block resolves references to definitions. RED test per surface: an edit on the old
   page after migration changes the env block coai-mcp reads. The env block for an unchanged, migrated setup is
   byte-identical to today's (the three-way `document` kept, pristine still writes nothing), measured on the fixture
   WITH overlapping consultant, question, chat and Bugz entries, not a reviewer-only one; the seam test compares
   0.43.0's `--providers` reviewer set before and after migration.
   **As built (2026-10-04):** the old page keeps writing definitions (its pickers resolve the reference first), and
   `catalogEdit.foldedWrite` in `PanelProvider.save` folds each write into the catalog before it lands — an entry
   that referred to a row only it uses rewrites that row in place, so an edit never forks or orphans one; a shared
   row is never rewritten. The old page hides a catalog-only row (`shownOnTheOldPage`), and its three reviewer-list
   writes that went around the side overlay now go through `save`. The seam's tenth leg proves the byte-identical
   settings file and the unchanged reviewer set against the real binary.
5. **Export/import v2** (rows + references; `executablePath` stripped as today); a v1 file is migrated on import and
   REPLACES the current models only after a confirmation that names the row count and every row whose vault key this
   machine does not hold; the current models are backed up first and the restore command puts them back (finding 5).
   **As built (2026-10-04):** the backup is an export of the current setup to `<dataDir>/config-backups/` (newest ten
   kept), named in the confirmation and put back by *Import config* — the restore command keeps one meaning, "before
   the catalog", rather than two. Vault key names come from the panel's last `--providers` answer and are said as
   not known yet when nobody has asked. A v1 file's definitions are moved into rows by the migration that runs on
   the configuration change the import makes.

### Epic 2 — coai-mcp and the Team server honour the new fields (release mcp 0.44.0; Team server contract v2)
1. **Probe and routing fixes**: the probe runs the runtime's CLI, not the row id (RED: `claude-2` with
   `runtime: claude`); Bugz ranking matches the runtime, not the id `local`; an unknown runtime refused by name; the
   material told to each reviewer decided per runtime in `RosterBuilder.cs:181-183`; the C# side reads
   `feature-availability.json`. **Carries the Bugz move from E1.3**: once both halves match the runtime, a Bugz model
   that differs from its row's model becomes row `bugz-<rowId>` (`uses: [bugz]`) by the same pure migration.
2. **Per-row system prompt, timeout and effort in every runner** (review, consult, question consult): the prompt
   delivered inside the prompt body (stdin or the prompt file the runner already uses), NEVER argv, never logged
   (length and hash only — a test asserts no launch record contains it); refused by name past a runtime's limit.
   Effort: codex `-c model_reasoning_effort=…`; claude's flag verified against the installed CLI first; local per call
   (`LocalRuntime.cs:152` already takes it); levels from D4. Each pinned in argv/body AND observed once. Gates
   `SYSTEM_PROMPT_SINCE`, `TIMEOUT_SINCE`, `CLI_EFFORT_SINCE`.
3. **A consultant on an API key and through the Codex CLI with an endpoint** (D9): the shared list replaces
   `ConsultantResolution.Consulting`; transcripts kept and retired as in *Growth surfaces*. Gate `API_CONSULTANT_SINCE`.
4. **Security words, a card's own words, and two one-shot modes**: words and patterns read from the lane setting
   (shipped words when absent); patterns run on `RegexOptions.NonBacktracking` with a match timeout, ≤ 32 patterns of
   ≤ 200 characters, a refused pattern reported by name. RED tests with catastrophic patterns over a 1 MB diff: the
   round completes and the pattern is reported. `--check-security <text>` (what "Try it" calls) and `--check-model <id>`
   (D10) — **both added to the one-shot list in `.agents/PROJECT.md`**, never exit 64. Gate `SECURITY_WORDS_SINCE`.
5. **Team server contract v2**: `ReviewRequestDto` gains `Effort`, `SystemPrompt` and `TimeoutSeconds` is honoured per
   model; a client system prompt is accepted only when the operator's switch allows it (off by default), capped, never
   logged; the operator can cap effort. The client reads `X-Coai-Contract` and, against a v1 server, says which fields
   that server drops. Measured both ways. Deploy is manual and needs the operator's go-ahead.

**As revised by epic 2's plan round (coai session `589a145b`, 2026-10-04, good_enough, 19 findings accepted)** — these
override the stories above where they differ:

- **Capability, not version numbers.** coai-mcp gains a one-shot `--features` that lists the row fields and modes it
  accepts (`systemPrompt`, `timeoutMinutes`, `cliEffort`, `apiConsultant`, `securityWords`, `checkModel`,
  `checkSecurity`); the extension writes a field only when the installed binary lists it, and says on the card which
  fields this binary ignores. A seam leg per field fails when the extension would send a field the built binary does
  not list. No `*_SINCE` constant guesses the release number before release-please cuts it. (f1, f8)
- **Effort.** A codex effort stays in the settings but is NOT sent while codex is `unmeasured`, and the card says
  "not applied"; an E1-shaped settings file with a codex effort still yields a round with codex in it. The probe
  records whether `claude --help` lists `--effort`; a claude row with an effort on a CLI without it is refused by name
  for that reviewer only ("the installed claude CLI does not take --effort"), never the whole round. A refused field
  degrades one reviewer, reported, never aborts a round. (f5, f10, f11)
- **Sent is not applied.** Per runtime, the measurement that shows the effect is named and recorded beside the code: a
  system prompt that forces a marker token in the answer (one recorded real call per runtime), the effort level as
  the CLI/API reports it in its usage or metadata where it does; where no effect can be observed, the docs and the card
  say "sent", not "applied". (f12)
- **The system prompt's leak paths.** Delivered in the prompt body after the product's own reviewer instruction (which
  it cannot replace: the output schema and read-only rules come after it again), never argv. A canary test runs a round
  through success, timeout, CLI failure and API error and greps stderr, the log files, the rounds database and the
  run records; a prompt file is deleted in `finally`. Only length and hash are recorded. (f0, f2)
- **Team server.** The operator caps timeout and effort (clamped, said in the response); a client system prompt is
  appended after the server's immutable instruction, recorded as client-supplied (hash only), stored nowhere. With the
  operator switch off (the default) the server accepts the request, drops the field, and reports the drop in the
  response — shown by the client exactly as a v1 server's drops are. A dropped field is a visible per-round note in the
  round log, never silence. (f1, f9, f13)
- **Security words and patterns.** Each pattern is compiled once when the setting is validated (the extension asks
  `--check-security --validate` through stdin), per pattern, so a construct NonBacktracking refuses (lookaround,
  backreference) or a slow construction is refused by name at save time; a pattern refused at round time marks the
  lane DEGRADED in the verdict rather than quietly scanning with fewer signals; a total scan budget bounds 32 patterns
  × a 1 MB diff. `--check-security` reads its text and the patterns being edited from stdin (never argv: no token on
  a command line, no 32 K limit); a bad argument exits 65, never 64; the extension falls back on 64 (an older binary).
  (f4, f6, f14, f17)
- **API-consultant transcripts — growth.** Under `<dataDir>/consultations/<id>/transcript.jsonl`, owner-only mode; ≤ 1 MB
  per consultation — the next turn past it is REFUSED by name, never truncated; total ≤ 50 MB, a new consultation
  refused when full; swept (closed or idle consultations, and anything older than 14 days) on every coai-mcp start and
  by `--close-consult`; an interrupted turn is ended `interrupted` by the existing consultation sweep. (f3, f15)
- **Bugz migration** reuses `catalogMigration.migrateLayer` (backup once, deterministic id, clash `-N`, marker last,
  idempotent, restore); history keys are untouched. (f7, f16)
- **`--check-model` widens `--check-consultant`** rather than standing beside it: one `ConsultCheckState`-based check
  for any row. (f18)
- **Scope.** Five stories, two release lines and a manual deploy: built as one epic per the operator's rule, released
  as mcp 0.44.0 with whatever stories have landed — the `--features` list, not a version, tells the extension which.

#### E2.1 as built (branch `feat/catalog-e2`)

- **Probe.** `ClaudeRuntime`, `CodexRuntime`, `GeminiRuntime` name their CLI in `DefaultExecutable`; launch and probe
  both use it, so a `claude-2` row probes `claude` (`ARowIdIsNotAProgramTests`).
- **Material per reviewer.** `IReviewerRuntime.ReadsTheCheckout` (false for api, local, remote) and
  `ReviewerMaterial.For` decide per reviewer in `RosterBuilder`; only a reviewer that can read a checkout is told it has
  one (`AReviewerIsToldOnlyWhatItCanReadTests`). `CodeWorkspaceTests`' repair-launch test had pinned the old lie on a
  local reviewer; it now asserts the guarantee on a CLI reviewer and the absence on a local one.
- **The shared file.** `core/Catalog/FeatureAvailability.cs` embeds `shared/feature-availability.json`
  (`FeatureAvailabilitySeed` in `CoreJsonContext`), refuses a feature list that names no runtime or an undeclared one,
  and `ConsultantResolution.Consulting` IS `FeatureAvailability.Builtin.Consultant` — RED first on "equal but a copy".
- **Unknown runtime — a reversed decision.** Both halves turned an unknown runtime into `codex` ("a name from a newer
  panel still launches something"); what it launched was the Codex CLI on the person's own account, the shape `api`
  was once the example of. The server now KEEPS the name (`PanelSettings.RuntimeOf`), `RuntimeResolution.NameOf`
  answers it before the base-URL arm, `For` no longer falls back to the row id when a runtime was named (a row `claude`
  with runtime `llama.cpp` ran the claude CLI), and the probe and the round's exclusion both say
  `RuntimeResolution.NoAdapterFor` — the runtime by name and the runtimes this build runs. Two tests that pinned the
  old coercion were rewritten. **Not done here:** the extension's `vendorsFrom` still coerces an unknown runtime to
  `codex` when an OLDER panel reads a newer one's rows (and would re-save it so); that is an extension change, tail T6.
- **Bugz ranking by runtime.** `RankingModels.IsAllowed(model, runtime)` / `Refusal(model, runtime)`; `--collect-bugs`
  takes `--runtime` (the catalog row's runtime, which the extension resolves — Bugz never crosses in `COAI_VENDORS`, so
  the collector cannot look it up). Without it the row id stands in, so `local/<model>` from a terminal or an older
  extension means what it did. A row CALLED `local` on a cloud runtime is now refused. The extension passes
  `--runtime` once `--features` lists it (E2.2), and the Bugz migration (`bugz-<rowId>`) follows that.

#### E2.2 as built so far

- **`--features`** (`Server/FeaturesMode.cs`): `{"features":[...]}`, an entry added in the commit that makes it true;
  today `bugzRuntime`. The extension reads it once per binary file (`binaryFeatures.ts`; 64, a failure or a bad answer
  is no features) and seam leg 11 fails when the extension knows a capability the build does not list.
- **Bugz by runtime, end to end**: the picker offers every `local`-runtime instance when the binary ranks by runtime,
  and `--collect-bugs` gets `--runtime` only then (`bugzView.collectArgs`).
- **The Bugz migration** (the E1.3 carry): `migrateLayer(layer, { bugzByRuntime })` moves a Bugz model that differs
  from its row's into `bugz-<rowId>` — only for a binary that ranks by runtime. Deviation: Bugz's own row is rewritten
  IN PLACE on a later pick and a pick through it moves nothing (the picker writes on every pick; the plain rule left an
  orphan per pick). The backup gains `bugzModel` only when it is moved, added to an epic-1 backup, never overwriting.
- **A prompt file does not outlive its launch**: `ReviewerInvocation.TempFiles`, deleted by the scheduler in a `finally`.
- **The system prompt**: in the body after the product's instruction and before the contract; never argv; refused past
  8192 bytes by name; redacted from the child's output before anything is recorded (the canary found the leak: a
  failing CLI's stderr became the round's reason). Deviation: NOT sent in a Team server row's body — story 5's field.
  The extension sends it only when `--features` lists `systemPrompt`. "Sent is not applied" still owes its one recorded
  real call per runtime (a marker the answer must carry).
- **A CLI row's timeout** (`timeoutMinutes`, 1–1440): its launch timeout in place of the round's; an api row keeps
  `reviewMinutes`. Listed in `--features`; the extension sends it only then.
- **A CLI row's effort** (`cliEffort`): claude's level as `--effort` (verified on claude 2.1.289), a local row's per call,
  a codex row's kept and not sent (unmeasured); a level a runtime does not take leaves that reviewer out by name.
  Deviation: no `--help` probe per launch — an older claude refuses the flag itself, and that failure is named
  ("the installed claude CLI does not take --effort") for that reviewer only.
- Still open in E2.2: "sent is not applied" — one recorded real call per runtime showing the system prompt's effect
  (a marker the answer must carry) and the effort as the CLI reports it; until then the docs and card say "sent".

#### E2.3 as built (branch `feat/catalog-e2`)

- **A codex row on an endpoint consults** with its provider on every turn (the reviewer's `-c` overrides, the key in
  the endpoint's variable) — it was refused because the consultant dropped them. **An api row consults** through
  `ApiConsultant`, widened from question rows to stuck consultations (one completion per turn, `WeRemember`, 32 KB).
- The key reaches only those two kinds (`TakesAKey`); an api consultant runs with its module's effective settings.
  `--features` lists `apiConsultant`; the shared file lists `api` among the consulting runtimes.
- **Deviation — no separate transcript file.** The plan's `<dataDir>/consultations/<id>/transcript.jsonl` with its own
  1 MB / 50 MB budget was written before reading the store: a consultant that keeps no conversation already has one —
  the consultation record stores every turn's problem and advice, carries them into the next prompt under the frozen
  carry budget, and is swept with the consultation (idle → lapsed, retention → deleted, a dead owner → interrupted).
  A second copy of the conversation would have been a second thing to keep in step. Bounded by the turn cap and the
  completion ceiling, a record stays far under 1 MB.
- Open: a measured real consultation on an endpoint row and on an api row (one each), which is also what closes the
  "consults when its provider overrides are measured" note this replaced.

#### E2.4 as built so far (branch `feat/catalog-e2`)

- **Editable words and a card's own words**, on one table (`SignalTable`): a signal's words replace its shipped words
  (its shipped pattern stays); a card's words are a signal only that card is triggered by. Patterns compile once with
  NonBacktracking and a 1 s timeout; refused by name for lookaround, backreferences, > 200 characters, > 32 in all.
  A pattern that times out leaves its file's detection incomplete. Read from `COAI_SECURITY_LANE` (`signals`, a
  prompt's `words`); refusals and unknown signals are complaints, never a refused lane.
- **`--check-security [--validate]`**, on stdin, 65 for a bad request; in PROJECT.md's one-shot list.
- The extension sends the words only when `--features` lists `securityWords`.
- Deviation: the "total scan budget" is the engine's linear time plus the per-file match timeout, not a separate
  budget — NonBacktracking bounds each pattern by the input it reads, and the detector already caps a file at 256 K.
- **`--check-model`** (D10): the consultant check of any catalog row, the row read on stdin (a row that reviews nothing
  is on no wire the server reads), its record `model-<id>`; the lock-and-record half is ONE method both checks use.
  A row that cannot consult is unavailable by name, nothing launched. Deviation: the panel's ✓ Check button that
  calls it is E3's (the Models card).

#### E2.5 as designed (before building; the story text left delivery and storage open)

- **Contract 2.** `ReviewRequestDto` gains `effort` and `systemPrompt`; `ContractVersion.Current` becomes 2. The client
  composes the whole prompt and the server relays it, so the server's part is to decide what it TAKES:
- **The system prompt** is taken only when the operator switch `Coai:AcceptClientSystemPrompt` is on (off by default),
  at most 8192 UTF-8 bytes, and inserted before the client prompt's own `## The finding contract` heading — the contract
  stays last; a prompt without that heading has the field DROPPED, never guessed at. The job store is in memory only,
  so it is on no disk; the record carries its SHA-256, never the text; no log line names it.
- **Effort** is applied only where the vendor's runtime LISTS levels in `shared/feature-availability.json` (claude
  today) and the level is one of them; past the operator's `Coai:MaxEffort` it is CLAMPED to it. Elsewhere (codex,
  unmeasured) it is dropped with its reason.
- **The answer says so.** The accepted response gains `dropped` and `clamped` — field and reason each. The client turns
  them into a per-reviewer note in the round; against a contract-1 server it reports every new field as dropped.
- **Timeout** keeps today's bounds and refusal (deviation from "clamped"): turning the refusal into a clamp would
  change what a contract-1 client asked for without it knowing.
- The client sends the system prompt in a file beside the prompt file (`--system-prompt-file`), never argv, and both
  are in `TempFiles`. Deploying the server stays the operator's decision.

#### E2.5 as built (branch `feat/catalog-e2`)

- As designed, server side: `ClientOptions.Take` (src_server `Jobs/ClientOptions.cs`), `JobRecord.Effort` /
  `SystemPromptSha`, `Coai:AcceptClientSystemPrompt` / `Coai:MaxEffort`, `ContractVersion.Current = 2`, the
  fingerprint covering the taken effort. The section text moved to core (`PersonInstruction`), so the server and
  `ReviewerPrompt` place the SAME sentence.
- Client side: `--effort` / `--system-prompt-file` on the remote launch; the not-applied sentence rides the shim's
  usage line into `ReviewerOutcome.Ok.NotApplied` and the reviewer's progress note.
- Deviations: remote's effort row is `probe` (judged by the server) instead of a new source name — the panel keeps
  the row and sends the effort, which is what `probe` already means. The extension's `CONTRACT_VERSION` moved to 2
  with the C# client; `SERVER_CONTRACT_REQUIRED` stays 1 (the panel reads nothing new).
- Not done here: deploying the contract-2 server (the operator's decision), and the measured live call that shows
  an effort APPLIED on the server's claude (T8).

#### Epic 2 code round (coai session 589a145b, 2026-10-05, proceed)

- 6 of 8 qwen reviewers answered; 16 findings, 15 accepted and fixed (RED first), 1 rejected: deriving `--features`
  by reflection (Native AOT; seam leg 11 and the per-feature tests already guard the list).
- Fixed: `--check-security` arguments, bounded stdin, lane shape and `detectionIncomplete`; a timed-out pattern runs once
  per classification; `EffortFor` gives other runtimes no effort; `TakesItsOwnTimeout`; `CodexConsultant` takes only a
  `CodexRuntime`; `PlacedIn` never returns an empty prompt; the instruction computed once; doc and message fixes.
- **Owed, not closed:** the cadence consultation for epics 1-3 and the two risk consultations for epic 2 — consulting is
  switched off in this installation (`COAI_CONSULT_ENABLED`), so none could be sent. They are never closed as abandoned;
  they run when the operator turns the consultant back on.

### Epic 3 — The new Settings page: the shell and Models (behind the preview switch)
1. **The shell**: a page module of its own (pure page + thin host); the CSP/nonce extracted from `pageDocument` and
   shared; `tabStrip` + `tabKeys`, `selectSearch`, the busy marks, focus restore and the refused-write snap-back reused;
   six tabs with sub-tabs, remembered by the host; the deep-link map (D11); two columns from 1100 px with
   side-by-side blocks one height (CSS subgrid); `help(key)`, `skew(since, what)`, `newTag(controlId)` and one confirm
   dialog; `coai.settingsPreview` (D5); `render-page.mjs` gains the page and a light theme.
2. **Models — cards and editing**: every card part of the mockup — the thinking switch drawn only where the model has one
   (D12); add (grouped by where a model runs), duplicate
   (copies `vaultKeyName`), remove (lists every reference; the last switched-on plan or code model cannot leave), on/off,
   the filter rows.
3. **Models — the world-facing parts**: the CLI's ▶ open / ⤓ install / ⟳ update, coai-mcp's verdict, where a list came
   from and "ask again", the API "runs it at", the off-machine endpoint warning and the WSL fix, ✓ Check (D10), the
   per-card "this coai-mcp ignores…" note.

#### E3 as designed (before building; from the mockup, the plan and the code that is there)

**The host — one Settings panel, two pages.** The new page is NOT a second webview: when `coai.settingsPreview` is on
(user scope, never overlaid; declared in `package.json`), the Settings slot `PanelProvider` already paints renders the
new page instead of the old one (`pageFor`). So "never both" (D5) is structural, and the new page inherits, unchanged,
everything the slot has: the full `PanelState` (the `--providers` report, CLI status, the local engine, Team servers,
`--features`), the message protocol (`setting`, `command`, `focus`, `ready`, `tab`), the write queue, the busy marks,
focus restore, the refused-write snap-back and `RenderTracker`. The page is built on `pageDocument` with its own
`extra.css`/`extra.script`, so its CSP and shared wiring are the old page's. Toggling the switch repaints the open panel.
`panelProvider.ts` (4 332 lines) gains only the branch and a delegation to new modules; nothing else is added to it.

**E3.1 The shell** (new modules, each under 400 lines: `catalogPage.ts` the document, `catalogShell.ts` tabs/help/skew/
newTag/confirm, `catalogPageScript.ts` the client script, `catalogCss.ts`):
- Six tabs with the mockup's sub-tabs — Models; Reviews (Stages, Roles & prompts, Prompts per round, The gate,
  Commands, Limits); Consultants (Consultant, Question consultant); Security lane; Chat; Setup (Vendor keys, Team
  servers, MCP server, This side). `tabStrip` + `tabKeysScript` for both levels. The tab is held by the host as today;
  the sub-tab and the Models filters live in the webview's `getState/setState`, which survives a repaint.
- Until E4 builds them, the five other tabs show one line: "Still on the current Settings page" with a button that
  switches the preview off (the old page then paints in the same panel).
- **The deep-link map (D11):** `OLD_TAB_PLACES` maps every old section id (reviewers, chat, consultant,
  questionconsultant, securityLane, prompts, gate, limits, keys, teamServers, side, server) to a new tab and sub-tab;
  `coai.openSettings(id)` accepts an old id or a new `tab/sub`. A test fails when an old id has no place.
- `help(key)` over the existing `HELP` table (English tooltips; the five-language articles are E5.2).
  `skew(feature, what)` — ONE road, by CAPABILITY, not a version: what the installed coai-mcp's `--features` lists
  (`FeaturesCache.known()`), "not installed" when there is none. `newTag(controlId)`: a table of control → the
  extension version that brought it; the host stamps the first-run time of each version in `globalState` (one entry per
  version, pruned after 60 days); the tag shows for 7 days after that. One `confirm` dialog for everything (Remove
  included), danger-styled when the action loses something.
- Layout from the mockup: cards two columns from 1100 px, card parts on a CSS subgrid (side-by-side cards one height);
  text size and tone through the existing `textControls` (the census of pages grows to 11).
- The webview nonce comes from `crypto.randomBytes` in one helper (`webviewNonce.ts`) used by every panel —
  `panelProvider.ts` used `Math.random`.
- `render-page.mjs` renders the new page and gains a light theme.

**E3.2 Models — cards and editing** (`modelsTab.ts`, `modelCard.ts`, `addModelDialog.ts`):
- One card per catalog row (`Vendor`): name (inline), id, runtime, model, on/off, the review stages through the
  existing `stageBox`/`featureBox` (so `document`'s three-way absent/true/false and remote's "not offered" are the old
  page's rules), `uses` ticks for the other features (consultant, qconsult, security, chat, bugz) with D4's availability
  reasons merged into one line, effort (D4 levels per runtime; a remote row offers its server vendor's runtime levels),
  system prompt (default/custom, 8 KiB counted in bytes), timeout (EMPTY means Limits › Reviewer timeout — never a
  fake 10), prices, connection (path, endpoint, vault key, dialect, Team server fixed after adding).
- **Thinking (D12):** `shared/feature-availability.json` gains `thinking` rows — `probe` for `api` (the module's
  `thinkingSwitchable`, per model, as `apiSettingsView.thinkingControl` already draws it), `none` for claude (its depth
  is `--effort`) and antigravity, `unmeasured` for codex, local and remote. The switch is drawn only for `probe` rows
  whose report says switchable; every other card says why in one line. Generated for both halves like the effort rows.
- Add grouped by where a model runs (a CLI here, an API key, this machine's GPU, a Team server — disabled with the
  reason when none is signed in), ids from `freeVendorId`; duplicate (copies everything incl. `vaultKeyName`, new id,
  "(copy)"); remove asks through the one confirm and lists every reference (consultant callers, question-consultant
  rows, security pairs, the chat model, Bugz); the last switched-on plan or code model cannot be switched off, unticked
  or removed (the old page's `removeVendor` rule, widened). Bugz has one model (D7): ticking it moves it, said once.
- Filters: search, access, "show switched-off", and the three chip rows (used for, effort, vendor) with counts.
- **Writes are validated on the way in**, for both pages: `catalogRefusal` (64 rows, 8 KiB) and `effortRefusal` were
  called only by the migration; the vendor write path now refuses by name and the control snaps back. RED first.

**E3.3 Models — the world-facing parts** (`modelCardWorld.ts`), each calling what the old card already calls:
- The CLI's ▶ open / ⤓ install / ⟳ update — the existing `runVendor`/`installVendorCli`/`updateVendorCli` commands
  by name (the shared message protocol reaches them unchanged); the CLI badge from `cliStatus`.
- coai-mcp's verdict from the `--providers` report (`cannotRun`), kept apart from the last ✓ Check.
- Where a list came from and "ask again" (`modelsProvenance`, `reprobeLocal`, `listEndpointModels`).
- An api row's "runs it at" (`apiSettingsView.runsWith`); the off-machine endpoint warning (`remoteWarning`) and the
  WSL fix (`fixWslNetwork`).
- **✓ Check (D10)** through `coai-mcp --check-model` (built in E2.4), the row on stdin, confirmed first as one paid
  turn; its status is the durable record `model-<id>` the binary keeps — read on load, "checking…" while it runs,
  never stuck after a crash (the binary's own sweep) — by widening `ConsultantHealthHost` from a caller kind to a key.
- The per-card "this coai-mcp ignores: …" note through `skew` — by capability (`systemPrompt`, `timeoutMinutes`,
  `cliEffort`, `bugzRuntime`), and on a remote card what a contract-1 Team server drops (E2.5).

**Mockup choices this design does NOT take, and why:** help by text (the plan's `help(key)`); one global "days since
update" (`newTag(controlId)`); one `PLANNED` version for every gate (capabilities); a ✓ Check with no confirmation
(D10); thinking only on api cards (D12); no sub-tab in deep links (D11); a 10-minute default timeout (D1: empty means
the round's); a separate remove dialog (one confirm); the remove text "the id is not reused" — `freeVendorId` reuses a
freed id today, so the dialog does not promise it (tail T9). Mockup-only, not built: the theme picker, the days and
coai-mcp pickers, Reset demo data, the Design notes tab, localStorage, toasts in place of real actions.

**Build order:** E3.1 (shell + preview switch + deep links + nonce) → E3.2 (cards, editing, validation, thinking rows)
→ E3.3 (world-facing parts, ✓ Check). One branch, one code gate at the end of the epic.

**Test plan:** every view through the real page script in the DOM shim (`panelPageHarness`), never source text: the
six tabs and sub-tabs, arrow keys, the deep-link map for every old id, the switch repainting the panel, help keys
attached, `newTag` on day 0 and gone on day 7, `skew` by capability. Models: each card part, add/duplicate/remove with
references, the last-model lock, filters and counts, the thinking switch only where switchable, a refused write
snapping back (64 rows, 8 KiB, an effort the runtime does not take). World parts: each command reaches its existing
handler; ✓ Check confirms, runs `--check-model` with the row on stdin, shows the durable state after a reload.
Guards that move: the page census (10 → 11), `settingsAreDeclared`, `sonarExclusions`, `bundledPage`. A headless-Chrome
layout check (two columns at 1920, one at 900, side-by-side cards one height, light theme) through `render-page.mjs`.

**Definition of done:** the preview page shows the shell and a complete Models tab; every write is validated; the old
page is unchanged with the preview off; all suites, lint, the seam and the layout check green; module docs updated.

**As revised by epic 3's plan round (coai session `ebf28ac3`, 2026-10-05, proceed, 5 findings accepted):**

- **A switch never loses a draft.** The repaint that swaps the pages goes through the slot's edit hold, and a pending
  debounced draft is saved before the other page paints; a test types into a textarea, flips the switch, and finds
  the text saved.
- **The row cap is checked only where a row is ADDED** (add, duplicate): an edit of a catalog already past 64 rows (a
  hand-edited settings file) is never refused for the count, so the page can always be used to get back under it.
  A row's own fields (8 KiB prompt, effort) are checked on every write of that row.
- **The generator is named:** `src_vs_code/scripts/generate-feature-availability.mjs` gains the `thinking` rows; its
  `--check` (byte for byte, already in CI) fails when the generated file drifts; C# `FeatureAvailability` reads them.
- **No "ignores" note before the binary has answered.** `skew` draws nothing while `--features` has not settled (a
  cold start, a timeout); "not installed" only when there is no binary at all.
- **Every panel moves to `webviewNonce.ts`** in E3.1, not only the new page.

#### E3 as built (branch `feat/catalog-e3`, PR #687)

- **E3.1** as designed, plus: the old page's header offers "Try the new Settings page"; the new page's header "Use the
  current page". `newTag` keys on a first-seen record (`newTags.ts`), so no version is guessed.
- **E3.2** as designed. Deviations: the add flow is the existing picker, GROUPED by where a model runs, not an in-page
  form (one add path for both pages); a use is a command (`toggleUse`), since it is one entry of a list; the new
  card's remove is `removeModel` (the page already asked — `removeVendor` would ask a second time). Ticking Bugz writes
  `coai.bugzModel` as well, so the tick takes effect today.
- **E3.3** as designed. The ✓ Check is asked by the host's modal (the Consultant tab's question), not the page's
  dialog, so a paid turn is confirmed in one place for both.
- Not here: the Models card's usage badge (runs, failures, cost per row) — the ledger reads by row id already; drawing
  it is E4's with the Reviews tab. Tail T9 (a freed id is reused) stands.

#### Epic 3 code round (coai session `ebf28ac3`, 2026-10-05, proceed)

- 4 of 4 reviewers answered; 10 findings, 9 accepted and fixed (RED first where there is behaviour), 1 rejected:
  splitting a base stylesheet out of the old page's now (E5.1 removes the old page and moves what stays).
- Fixed: removing the Bugz row clears `coai.bugzModel`; a model check whose row is gone spawns nothing; the
  "new" list matches the `newTag` calls (and the marks are drawn on effort and the system prompt); each page keeps
  its own position (`HeldTabs`); one lock sentence; the add's choices in a function and its cap checked again after
  the picker; the page body built once; the features read once.
- **Owed, not closed:** the cadence consultation for epics 1-3 and the risk consultations for epics 2 and 3 —
  consulting is switched off in this installation (`COAI_CONSULT_ENABLED`).

### Epic 4 — The feature tabs use the catalog
1. **Reviews**: Stages; Roles & prompts (replacing `rolesPage.ts`) with ONE switch per role — `roleEnabled` and the
   catalog's `active` merged, `COAI_ROLES` still written for servers under `ROLE_SWITCH_SINCE`; deletion's confirmation
   and reserved ids; a role off until it has a question; Prompts per round; The gate; Commands (replacing
   `commandsPage.ts`); Limits.
2. **Consultants**: callers refer to instances ticked Consultant (absent = shipped pair; same vendor shown, never
   refused; stranded picks shown); health per side; the paid Check confirmed; agy's allow rule; the question
   consultant's rows, prompts, folders and limits.
3. **Security lane and Chat**: pairs from instances ticked Security lane; on each card the signals in two columns, own
   words, prompt text in place; the routing table with editable words; Try it through `--check-security` (never a
   JavaScript copy of the matcher). Chat from instances ticked Chat, per side, the starting text and the model's effort
   and system prompt applied; prompt presets inline (replacing `chatPresetsPage.ts`). **Carries the chat move from
   E1.3**: each model preset becomes a row with `uses: [chat]` and its `chatStartingPrompt`, and every stored
   conversation's preset id is mapped to that row in the same change, so a resumed conversation keeps its model.
4. **Setup**: keys counted across every row; the CLI table; Team servers with every state; the MCP server's states and
   clients (another program's config is READ only, never written, and no secret from it is shown); the data folder's
   Change and Move flows on today's `dataCommands.ts` logic, the "moved from" record surviving a reload; This side with
   the export note.

#### E4 as designed (before building; from the mockup, the plan and the code that is there)

**Approach — the old builders first, in place; then the catalog.** Every old Settings section already draws its
settings through the panel's write path; the new page is painted in the same slot. So E4 first draws each section's
builder in the sub-tab that owns it (exported from `panelView.ts`, never copied), then moves each tab onto the catalog
and folds in the three separate pages. One branch; a PR per story or two; one code gate at the end of the epic.

**Fixed first (epic 3's missed row):** the panel's `vendors` is the current page's reviewers (`shownOnTheOldPage`), so the
new Models tab never drew a row that reviews nothing — a migrated consultant. The state carries `catalogRows` (every
row); the new page reads it. Done, RED first.

- **E4.1 Every tab in its place.** reviews/stages and reviews/prompts ← `promptsBody` split into its two halves (the role
  switches, rounds, thresholds, lenses and workspace; the round pickers) — the same controls, each drawn once on the
  page; reviews/gate ← `gateBody` (its commands link opens reviews/commands); reviews/limits ← `limitsBody`;
  consultants/consultant ← `consultantSection`; consultants/qconsult ← `questionConsultantSection`; security ←
  `securityLaneBody` (and `securityLaneScript` joins the page's script); chat ← `chatBody`; setup/keys ← `keysBody` over
  every row; setup/team ← `teamServersBody`; setup/mcp ← `serverBody` (its zoom rule matched to the new pane); setup/side
  ← `sideBody`. Each feature tab shows the "used by" strip of the rows ticked for it, with "change on Models", which
  opens Models filtered by that use (the page's own filter state). After E4.1 only Roles & prompts and Commands still
  say where they are. **Built 2026-10-05** (`catalogSections.ts`; the split is one parameter, `promptsBody(state,
  half)`; every setting control of the current page writes the same on both pages — 515 swept).
- **E4.2 Consultants and Security from the catalog.** A caller's picker lists the rows ticked Consultant (absent = the
  shipped pair, D2; the same vendor as the caller shown, never refused; a stranded pick shown and named, never cleared —
  D3); the question consultant's rows pick from rows ticked it; the security pairs from rows ticked Security lane. "Try
  it" sends the sample to `coai-mcp --check-security` on stdin (E2.4) — never a JavaScript copy of the matcher. **Built
  2026-10-05** (`consultantPicks.ts`, `catalogPicks.ts`, `securityTry.ts`). Deviation: a consultant pick is written WITHOUT
  the current page's fold, which would drop the caller's old row (E1.4); found on the way, the Question consultant
  section resolved its rows against the reviewers only, so a migrated row could not be switched on — fixed, RED first.
- **E4.3 Roles & prompts in the page** (replacing `rolesPage.ts`): its content drawn by the panel, its edits as
  namespaced commands into a host module both the old roles panel and the panel call (`rolesHost.ts`, extracted from
  `rolesPanel.ts`) until E5 deletes the page. ONE switch per role — `roleEnabled` and the catalog's `active` merged,
  `COAI_ROLES` still written for servers under `ROLE_SWITCH_SINCE`; deletion confirmed, ids reserved (the existing
  `roleDeletions`); a role stays off until it has a question; a name is asked by the host's input box (a webview has no
  `prompt()`). The prompt editors do not carry `data-prompt` (the shared script reads that as a round pick). **The
  command shape** (plan round 1): one exported command type per host module — `roles.<verb>` / `commands.<verb>`, each
  verb declared once with its arguments, so a new command is one declaration both callers see; the panel dispatches by
  the prefix; the host answers with a refusal sentence (empty = done), which the panel shows through its existing
  refusal path, so an edit that did not land is said, never silently read back; catalog keys are written inside
  `inCatalogTurn`. **Built 2026-10-05** (`rolesHost.ts`, `rolesEmbed.ts`, `rolesSwitch.ts`). Deviations: the command
  shape is the Review roles tab's OWN message carried as `{ type: 'roles', edit }` and parsed by its own `roleEdit` — one
  type for both pages, not a second set of verbs; a refusal is the roles' existing notification; the stages are headed
  groups, not a second tab strip; `roleEnabled` follows `active` only after the catalog write landed; a role's name is
  typed in place, as on the tab, so no input box was needed.
- **E4.4 Commands in the page** (replacing `commandsPage.ts`), the same way (`commandsHost.ts`). **Built 2026-10-05**
  (`commandsHost.ts`, `commandsEmbed.ts`); deviation: the blocks carry `data-cmd-*` attributes on the new page, because
  the page also draws the roles, whose wiring reads `data-field`, `data-remove` and `data-restore`.
- **E4.5 Setup.** Keys counted across every row; the CLI table; Team servers with their contract (E2.5); the MCP
  server's clients — whether each registers coai-mcp, READ from its config file and never written, no other entry or
  secret shown (a pure file read: no client program is launched, so there is no process to time out); the data
  folder's Change and Move on `dataCommands.ts`, and the "moved from" record (`coai.lastDataMove`) shown after a reload
  with "Delete the old folder". **Built 2026-10-05** (`setupTab.ts`, `mcpClientsRead.ts`; the Team servers' contract
  note moved with its section in E4.1).
- **E4.6 Chat** (the risky piece): rows ticked Chat, per side; a row's `chatStartingPrompt` and its effort and system
  prompt applied (D8); prompt presets inline, through the panel's save (ending the presets page's direct writes). **The
  move**: each model preset becomes a row `chat-<id>` with `uses: [chat]` and its starting text, through the epic 1
  migration (`placeAll`/`newRow`, backup once, rows, references, the marker last, restore). Corrected in plan round 1 —
  what holds a PRESET id today: `coai.chatModel`; a conversation record's `providerId` and `chosenId` (its `modelId` is
  the model NAME, which the move does not change — except a legacy record whose `modelId` equals a preset id); legacy
  `SavedTab.modelId`; the spend ledger's turn and door lines' `provider`; and its forget marks, keyed by provider +
  model. Only `coai.chatModel` is a setting and is rewritten with the rows. Every FILE reference is mapped on READ
  through the preset → row table the migration stores in settings (`chatPresetRows`, in the same backup) — a record
  or ledger line is never rewritten, so an interrupted move has nothing half-written outside the settings, and a rerun
  finds its rows by their fields. `coai.chatModelPresets` itself is kept as it was until E5, so an older extension
  still reads its presets; coai-mcp never sees a chat-only row (a row that reviews nothing is left out of
  `COAI_VENDORS`, E1.4) and does not serve chat.

  **E4.6 as designed, from a trace of the chat code (2026-10-05) — it corrects the bullet above.**
  - *What really holds a preset id on disk:* `coai.chatModel`; the `provider` field of `chat-usage.jsonl` and
    `chat-doors.jsonl` (and, for an old line with no `vendor`, the forget mark keyed through it); and
    `coai.chatModelPresets` itself. A conversation record holds only the MODEL id (`chatStore.ts:94-126`); its
    `providerId`/`chosenId` are in memory only, and a restored conversation finds its provider again by that model id
    (`legacyPick`) — so the move changes nothing a record holds, and the record row of the remap goes.
  - *Three stories, the move first:* **E4.6a the move** — **E4.6b the Chat tab** (rows ticked Chat per side, the prompt
    presets inline) — **E4.6c effort and system prompt applied to a chat launch** (D8; today a chat launch reads
    neither).
  - *E4.6a, the move.* A step of the epic 1 migration, run in every layer and again whenever `chatModelPresets` or
    `chatModel` changes (a config import can bring old presets back): each preset the reader sees (`savedModels`, so a
    positional `preset-N` id is the one the chat used) that the stored table `chatPresetRows` (preset id → row id) does
    not hold yet becomes its OWN row — never joined to an existing one, because `sameLaunch` ignores the Team server and
    two presets' starting texts would collapse — with id `chat-<normalised id>` (`freeVendorId`), `uses: [chat]`,
    `name`, `chatStartingPrompt`, the launch fields, `vaultKeyName` = the old preset id (the vault key keeps its name),
    and an explicit `remoteVendor` for a Team-server preset (its server vendor came from an id prefix `chat-` would
    break). Written in epic 1's order — backup (`chatModelPresets` and `chatModel` join `BACKED_UP`), rows, the table,
    `coai.chatModel` remapped (to the MAIN preset's row when one is marked main — the catalog has no "main"), the marker.
    Idempotent: a preset already in the table is skipped. The 64-row cap refuses the step, said, as it refuses a layer.
    A layer that keeps its own `vendors` (a side overlay) gets the chat rows too, read from the user layer's presets.
  - *Then chat reads the rows.* `savedModels` answers from the rows ticked Chat (row → the preset shape the chat code
    already takes; `chatRunSpec` carries the row's `vaultKeyName`), so every chat path keeps working with row ids. The
    ledgers are mapped on READ: an old line's `provider` through `chatPresetRows`, so old and new lines of one model
    are one spend row. The presets page stays reachable until E5 but writes rows through the panel's save.
  - *Test plan, E4.6a:* the step as a pure function — a preset becomes its row with every field and the vault key
    name; a positional id; a mixed-case id; a remote preset keeps its server vendor; MAIN moves `chatModel`; a second
    run changes nothing; an imported preset moves alone; the cap refuses; backup and restore round-trip — and the
    reads: a ledger line under an old id lands in the row's spend row; a conversation restored by model id opens on
    the moved row.

  **E4.6a revised by an independent design review (2026-10-06)** — the consultant is still switched off, so a reviewer
  agent checked the design against the code (the risk consultation stays owed and runs before the merge). It changes:
  - *The identity is a stored RECORD, never the row's key name.* `vaultKeyName` cannot be it: a deleted row would come
    back on the next run, `vendorsFrom` lower-cases it (`vendors.ts:451-455`) so a mixed-case preset id would be moved
    again on every run up to the cap, a positional `preset-N` shifts when an earlier preset is removed, Duplicate
    copies it (`catalogCommands.ts:110`), and the current page's save rewrites it (`catalogLaunch.ts:43-53`). The
    record `chatPresetsMoved` — `{ presetId, runtime, model, name, rowId }` per moved preset, a DECLARED setting,
    overlaid per side like `vendors` — is what a run skips by; the fingerprint catches a positional shift (a preset
    whose id is recorded but whose fingerprint differs is a different preset and is moved as such). A deleted row
    stays deleted. `vaultKeyName` is still set to the old id when it is `normaliseId`-clean (the vault key keeps its
    name) and left off otherwise; an id that normalises to nothing gets `chat-model`.
  - *No dual store, ever:* E4.6a ships TOGETHER with the presets page reading and writing rows (its model half), and a
    scan test refuses any write to `chatModelPresets` outside the move's restore. The chat reads the rows ticked Chat,
    plus — only before a layer's first move, after a cap refusal, or in a restored layer — the presets the record
    lacks. `chatModelPresets` joins `MIGRATION_TRIGGERS` (an import, a hand edit); `chatModel` does not.
  - *Per side:* `chatModel` and `chatModelName` join `OVERLAID_SETTINGS` (D8: chat models are per side), so a row id
    that differs between a side's `vendors` and the user layer's never leaves the chat opening on the first provider.
    The step writes an overlay's chat rows only when that overlay keeps its own `vendors`.
  - *MAIN:* the run that moves the main preset writes `chatModel` = its row AND `chatModelName` = its model (both backed
    up) — a stale `chatModelName` would otherwise open a different model (`chatModels.ts:488-494`).
  - *A downgrade:* an older build drops `uses` and keeps `vaultKeyName`; `repairUses` gives `chat` back to a row the
    record names (it already repairs what a reference names).
  - *A resumed conversation:* the record gains an optional `providerId` (absent = today; no format bump, as `access`
    was added) and is resolved through the record, so two rows that offer one model name cannot swap a conversation's
    model, system prompt or effort. A legacy record whose `modelId` is a preset id is mapped through the record too.
  - *The ledger needs no row map:* spend rows and forget marks key on the vendor (runtime) and the model
    (`chatSpendRows.ts:220-227`); old lines resolve their vendor through presets ∪ rows, and after E5 through the backup.

  **E4.6a built 2026-10-06** (`chatPresetMove.ts`, `catalogChatStep.ts`, `chatCatalogModels.ts`, `chatModelEdits.ts`;
  wired in `catalogMigration.ts`, `catalogMigrationHost.ts`, `chatConfig.ts`, `chatPresetsPanel.ts`, `chatStore.ts`,
  `chatPersist.ts`, `chatConversationRestore.ts`). As revised above, with these differences:
  - *The record is the only thing the move writes outside the rows.* `chatModelPresets` is never written by the move,
    so the restore has no presets to put back; `BACKED_UP` gains `chatPresetsMoved`, `chatModel` and `chatModelName`.
  - *The "no dual store" scan* (`noSecondPresetStore.test.ts`) pins which modules name the presets setting and the
    presets page's two writes: the edit of a preset the move has not taken (`chatModelEdits.onPreset`) and the prune
    of dead rows no surface can show — the latter was not in the plan and is kept, because a dead row is never moved.
  - *The settings' chat picker reads the rows too* (`chatModelsReading`, shared with `savedModels`): it listed the raw
    presets at first, so after the move it showed the chat's own row as one that "cannot answer a chat" — found while
    designing E4.6b, fixed with a RED test (`thePanelListsTheMovedRows.test.ts`).
  - *The chat reads this side.* `chatRead(config)` (`readerFor(side)`, bound at activation by `bindChatSide`) is what
    every chat path reads its settings through; `theChatReadsThisSide.test.ts` refuses `chatSettingsFrom(userLayer(`.
  - *A resume* goes through `resumedPickFor` → `resumedPick`: the recorded row while it is offered, else `legacyPick`
    of the saved model value after `movedTo` maps an old preset id. `savedPick` (`coai.chatModel`) maps through the
    record the same way. `recordFrom` drops a malformed `providerId` and keeps the conversation.
  - *An import cycle avoided:* `catalogChatStep` declares the part of a layer it reads (`ChatLayer`) and the writes it
    makes (`ChatWrite`) instead of importing them from `catalogMigration`, which calls it.

  **E4.6b as designed (2026-10-06), from the mockup's Chat page (`new_design/lanes.js` `chatPage`) and the way E4.3/E4.4
  folded their pages in.** The Chat place stops drawing the current page's section and draws its own (`SPLIT['chat']`):
  - *Which model a chat opens on* — one radio per model the chat can answer with (`chatProvidersFromPresets` over
    `state.chat.models`, the panel's discoveries in it, as `chatBody` builds it today — extracted, not copied), its
    name and an *Opens with* box (the row's `chatStartingPrompt`). The checked radio is `coai.chatModel`, or the preset
    ticked main while nothing is saved. A saved choice that no longer resolves is drawn checked and disabled with
    the reason (the stranded rule the current page keeps), and every switched-on row the chat cannot speak to is
    named with its reason (`list.refused`). Per side: the note "saved for this side" when `perSideSettings` is on.
    Picking a radio is the presets page's `main` edit, so `coai.chatModel` is set and a stale `chatModelName` cleared.
    No *Add a model* here: models are added on Models (tick Chat).
  - *Sending* — the three fields the current section has (what to ask, answer in, who presses send), from ONE
    builder both pages call (`chatSendingFields`, extracted from `chatBody`), written through `data-setting`.
  - *Prompt presets* — inline: the presets page's own prompt block (`chatPresetsPage.promptRow`, its attribute names
    passed in, as `commandsPage.customBlock` takes them, because the roles' wiring reads `data-field`/`data-remove`),
    with *Add a prompt*.
  - *One editing core* — the presets page's writes move, unchanged in effect, from `chatPresetsPanel.ts` into
    `chatPresetsHost.ts` (`queueChatPresetEdit`, `flushChatPresetEdits`, `onChatPresetsRedraw`, bound at activation),
    behind ONE settled-write queue, as `rolesHost`/`commandsHost`. The new page posts `{ type: 'chatPresets', edit }`
    — the presets page's own message, read by its own `presetEdit` — numbered for a pick, a tick, Add and Remove
    (`chatPresets` joins `PANEL_TRACKED`), plainly for typing, and reports focus as `chatPresets|<id>|<field>`; a
    focus release flushes this queue with the roles' and the commands'. The presets page keeps its wizard (*Add a
    model*) and its prune.
  - *Not built:* the mockup's Shortcuts panel (the key list lives in `package.json`, and VS Code's own editor is
    where a key is changed) — named here so its absence is a decision.
  - *Test plan, E4.6b:* the tab's html read as a tree (`pageTree`): one radio per model that can answer, the checked
    one, a stranded choice disabled with its reason, a refused row named; firing a radio, a prompt's box, its main
    tick, Add and Remove through the page's real script (`runPanel`) posts the presets page's own edit, and the roles'
    and commands' wiring post nothing for them (and the reverse); the host's queue: a typed field settles, a click
    goes straight through, a model edit lands in the row (`chatModelEdit`), a prompt edit in `chatPromptPresets`;
    the old page's chat section still draws its fields (the extraction is behaviour-neutral).

  **E4.6b built 2026-10-06** (`chatTabEmbed.ts`, `chatPresetsHost.ts`, `chatModelWizard.ts`; wired in
  `catalogSections.ts`, `catalogPageScript.ts`, `panelProvider.ts`, `busyMark.ts`, `catalogCss.ts`). As designed, with
  these differences:
  - *The add-model dialogs became their own module* (`chatModelWizard.ts`), so the editing core does not import the
    page that draws them; the presets panel's two complexity suppressions went with the move (written within the
    limit, not carried).
  - *Which edits settle* is decided in the pure page module (`chatPresetsPage.presetSettlesAs`, tested) rather than
    in the host: typed fields (name, text, starting text) settle per list/row/field; a tick, a pick or a press goes
    straight through. A presets write that fails is now reported, where the tab used to only log it.
  - *The host's queue is not unit-tested* (it imports `vscode`); its decisions are — `presetSettlesAs`,
    `chatModelEdit`, `editedRows`, `rowsAfterMain` — and its wiring is pinned by `noSecondPresetStore.test.ts` and
    `settingRefusedWiring.test.ts`.
  - *The two route branches share one* in `PanelProvider.receive` (`commands` or `chatPresets`), which keeps that
    method at its 50-line limit.

  **E4.6c as designed (2026-10-06), from a trace of how coai-mcp applies both (E2.2) and of the chat's launch.** The
  chat follows the server's rules exactly, so a row behaves the same when it reviews and when it chats:
  - *Carried.* `ModelPreset` gains optional `effort` and `systemPrompt`; `chatCatalogModels.OPTIONAL` copies them
    from the row and `chatRunSpec` hands them on. Today both are dropped at `asChatModel` and again at `chatRunSpec`.
  - *Effort, where the server sends one.* claude: `--effort <level>` on the launch (`ClaudeRuntime.cs:89-91`), and
    only a level `featureAvailability.effortRefusal` accepts — the TS mirror of the server's check — so a value the CLI
    would refuse never reaches it. A Team-server row: the request's `effort` field (`RemoteAsk.cs:98-103`), which the
    server applies to a vendor with measured levels and drops otherwise. codex and agy: none (`RosterBuilder.EffortFor`
    gives them none; agy's level is in its model id). `ChatLaunch` gains an OPTIONAL `effort` — every adapter's
    `argv` decides, and only claude's uses it. No thinking switch: no chat runtime has one.
  - *System prompt, inside the text, never in argv.* As the server does for every runtime but a Team server
    (`ReviewerPrompt.ComposePrompt`, `PersonInstruction`): a section before the person's words —
    "## What the person asked of this model" — on the FIRST turn a session hears (a new conversation, a model switch,
    a reload: the session object changed), and on EVERY turn of a forgetful (Team-server) session, which forgets each
    turn. Not a request field for a Team server: the server drops a system prompt from a prompt without a finding
    contract, which a chat never has (`ClientOptions.cs:98-102`). What is SHOWN and stored stays what the person
    typed; the section is in what is sent only, as the carried transcript already is.
  - *Test plan, E4.6c:* a row's effort and system prompt reach the chat model (`chatModelsOf`) and the run spec; the
    claude argv carries `--effort high` and no flag for an empty or refused level, and codex/agy argv never one (the
    pinned text-mode argv tests unchanged); the Team-server body carries `effort` only when set; the section is
    prepended on a session's first turn and not its second, on every turn of a forgetful session, never when the row
    has none, and the transcript keeps the typed text.

  **E4.6c built 2026-10-06** (`cliChatLaunch.chatLaunchFor`, `claudeAdapter`, `chatPrompt.rowInstructed`,
  `chatThread.hearsRowInstruction`; carried in `chatPresets.ts`, `chatCatalogModels.ts`; sent in `chatTurn.ts`,
  `remoteAsk.ts`, `remoteChatSession.ts`, `chatRemote.ts`). As designed, with these notes:
  - *"The session that heard it"* is the session OBJECT (`Thread.instructed`), so every way a session is replaced — a
    switch, a reload, a reset (`chatArchive`'s partition classifies the field with `session`) — sends it again
    without each path having to remember to. It is set by the turn's RESULT (`chatThread.instructedAfter`): one
    session object outlives its process, so a stopped or failed turn, or one answered by a new process
    (`contextLost`), leaves it unset and the next turn carries the instruction again.

  **Own review of E4.6 (2026-10-06)** — two reviewer agents (correctness; conventions) while the coai consultant is
  off; the owed consultations still run before the merge. Fixed, each with a RED test first:
  - the instruction marker was set BEFORE the send, so after a stop or a crash the row's system prompt was gone for
    the rest of the conversation (above);
  - unticking Chat on Models on a moved chat-only row bounced back: the downgrade repair read the person's explicit
    `uses: []` as an older build's missing key — only a MISSING key is repaired now (`catalogMigration.lostRows`);
  - a switch between two rows that offer one model saved nothing (the save guard compared the model only), so a
    reload resumed on the old row — `savedProviderId` joins the guard and `UNSAVED`;
  - a chat row's id rule was written twice (a moved preset, a model added on the tab) — one `freeChatRowId` now.

  **CodeRabbit on PR #688 (2026-10-06)** — no actionable comments; its architecture summary raised two medium notes:
  - *An interrupted move* (rows, record and chat model are separate settings writes). Fixed, RED first: a rerun ADOPTS
    the row an interrupted run wrote (its own id, ticked Chat, the preset's runtime/model/name, named by no record —
    `chatPresetMove.adoptedRow`) instead of writing `chat-<id>-2`; a run with nothing to move still remaps a chat
    model left naming a recorded preset (`catalogChatStep.remapped`). Neither ever overrides a chat model that names a
    row that exists — `coai.chatModel` has always held a row id — and `resumedPick` keeps the same rule.
  - *The fingerprint ignores launch fields* (executable, endpoint, Team server). **Not changed — for the owed risk
    consultation:** the frozen preset is not a second source after the move (the row is what is edited), and adding the
    launch fields would turn every such edit made in an older build into a duplicate row. Open question for the
    consultation: is a preset edited in an older build after the move a new model, or the same one to leave alone?

  **The owed consultations, 2026-10-06** — the consultant is back (codex `gpt-6-astra`). The cadence consultation for
  epics 1–3 and the risk consultation for epic 4 ran; every finding, and which test reproduced it, is in
  [RESULTS_catalog_consultations_2026-10-06.md](../research/RESULTS_catalog_consultations_2026-10-06.md). Fixed on
  this branch, each RED first: C1, C3, C4 (epics 1–3) and R1–R6 (epic 4). Still open:
  - *C2 — a row's options never reach a consultation* (effort, system prompt, timeout, key name): `COAI_CONSULTANTS`
    carries the launch fields only and `ConsultantResolver` rebuilds only those. **Built 2026-10-06 (C2a–C2c), each
    step RED first; the deviations from the design below:** a consultation FREEZES the system prompt in its record
    (`ConsultationRecord.RowInstruction`), because a turn's prompt is composed from the record alone; a question row keeps
    the question consultant's `RowBudget`, not the catalog row's timeout (the sweep and the fan-out deadline derive from
    it); a question row's catalog id is kept beside the rows (`QconsultSettings.catalogRows`). **Design (2026-10-06,
    from a read of every path):**
    - *The wire carries the WHOLE row.* A consultant entry of `COAI_CONSULTANTS` and a row of `COAI_QCONSULT_ROWS` gain
      `row`: exactly what `vendorsEnv` writes for that one row (the precedent is `modelCheckInput.ts`, which hands
      `--check-model` one row the same way), through one mapper factored out of `vendorsEnv` — never a second field
      list. It is written only to a binary whose `--features` lists `consultantRow` (an older one skips an unknown
      member silently, so a version check would not do). `vendor` does not change: an open consultation resumes by it.
    - *The server parses it with the reviewer row's own parser* (`PanelSettings.ParseVendors` over one row, as
      `ConsultantCheckMode.RowsOf` does) and takes the row's price, api settings, key name, dialect, system prompt,
      timeout and CLI effort; identity (id, runtime, the remote allowlist) stays the choice's. A `row` that does not
      parse refuses the consultant BY NAME — never a silent fall back to the five fields. `Frozen` (a resumed turn)
      takes today's row fields too.
    - *The fields reach the launch.* The row's own timeout bounds its turn when set; CLI effort reaches the claude and
      local consultants through the reviewers' own rule (`RosterBuilder.EffortFor`, shared, not copied); api effort and
      thinking already apply once the row's `Api` is filled. The system prompt is a person's instruction in the
      consultant prompt and the question prompt, redacted in the record as a reviewer's is. A `web` question row still
      gets the question and nothing else — the policy that row exists for.
    - *Build order:* C2a the wire and the parse (a consultant's `ProviderSettings` carries every field — tested at the
      resolver); C2b timeout and effort at the launch (tested at the adapters' requests); C2c the prompt slot (tested
      at the composed prompt). `panelServerDefaultsAgreement` learns the `row` member; every step RED first.
  - *R7 — the answer to the open question above:* a preset edited in an older build after the move is a conflicting
    revision of the SAME preset, not a new model. Planned, not built: keep the edited revision and show the conflict on
    Chat (the person chooses); widening the fingerprint alone would duplicate rows.
  - *The cadence consultation for epics 4–5* — **ran 2026-10-07** (consultation `7fec916c`, codex `gpt-6-astra`); its
    findings and what each came to are in the consultations record. Fixed on this branch, RED first: a row that consults
    now says on its card when this side's binary would drop its settings for a consultation (`consultantRow`), and an api
    row on a server too old for api rows says why its card is off, as the current page did. Moved to epic 5's
    prerequisites below: R7, extraction before deletion, and the rollout order.
  - *The epic 4 code round (2026-10-07, session `21ec1de8`, 8 reviewers on codex and gemini):* `proceed`, 12 findings.
    Accepted and fixed, RED first: the system prompt is redacted as it was SENT (trimmed; whitespace alone redacts
    nothing — found by two reviewers); `architecture.md` describes the `consultantRow` flow; the chat host's dependency on
    `chatPresetsPage.ts` joins epic 5's extraction step. Rejected with the code that refutes each: "the move resets a
    saved non-main chat model" (three reviewers) — today's chat already opens on the ticked MAIN preset over
    `coai.chatModel` (`chatCommand.readyForChat` on main), and the move keeps that; "a side with inherited rows keeps an
    orphaned preset id" — the read follows it through the record (`chatConfig.resumedPickFor`); "question rows drop the
    api module" — `QuestionFanOut.SettingsFor` applies it; a side's own row shadowing an inherited reference (by design,
    per-side launch facts); quadratic move work (capped at 64 presets); the non-main resume's model name (matches the
    uninterrupted run).
  - *Own review beside the epic 4 code round (2026-10-07):* `chatPresetsMoved` and `chatModelName` are model keys now
    (a repository could redirect the chat through them); an interrupted resume after an older build's edit takes the
    NEWEST record entry, as the uninterrupted run does. Known, not changed: a restore leaves a conversation saved since
    the move refused by name until the person picks again (no data is lost); which entry a LEGACY conversation id means
    when the record holds two is R7's conflict to show, not a guess.
  - *Not measured on a real call yet,* as for reviewers (the plan's open measurement): whether an older claude CLI
    refuses `--effort` in chat mode (the server names that refusal for a reviewer; the chat shows the CLI's own
    error), and how strongly a model follows an instruction placed in the first user turn rather than a system role.
  - *The wiring in `chatTurn.ts`* (it imports `vscode`) is pinned by reading the source; the two decisions it calls
    are tested as values (`aChatHonoursItsRow.test.ts`).

**Build order:** E4.1 → E4.2 → E4.5 → E4.3 → E4.4 → E4.6. E4.5 comes before the folded pages because it touches no
host-module seam, so it lands while E4.3's command shape settles.

**Test plan:** each tab run through the page's own script (`runPanel` with the catalog page), every control writing
its setting; the pickers limited to ticked rows, a stranded pick shown; "Try it" posts the sample and draws what the
binary answered; the folded pages' edits reach the same host functions their pages used (their existing tests move with
them); the MCP clients reader over fixture files, refusing to read past its own entry, with a companion assertion that
it still reads a known entry; `COAI_ROLES` at `ROLE_SWITCH_SINCE`'s boundary — a server under it gets it written, one at
or above it does not; the chat migration as a pure function (preset → row, `coai.chatModel` remapped, backup,
restore, idempotent, stopped at each write boundary and rerun), and a conversation stored under each old preset
(`providerId`, `chosenId`, a legacy `modelId`) resuming on its mapped row; the spend rows and forget marks read through
the table.

**Definition of done:** every tab of the new page works; the three separate pages are still reachable from the old page
until E5; each folded page's tests run the NEW page too, and any assertion over page source text is converted to run
the page (`todo/PLAN_the_page_tests_run_the_page.md`); all suites, lint, the seam and the layout render green; module
docs updated.

### Epic 5 — The switch-over, docs and release

**Prerequisites, from the epics 4–5 cadence consultation (2026-10-07):** (a) **R7 first** — a preset edited in an older
build after the move is otherwise unreachable (a changed CLI path or starting text leaves the move `unchanged` and Chat
keeps the old values; a changed name makes a second row); its tests cover both kinds of edit, a resolution that survives
a reload, and records written before any launch-field snapshot existed. (b) **Extract before deleting** —
`catalogSections` calls `PANEL_SECTIONS`, and `rolesEmbed`, `commandsEmbed` and `chatTabEmbed` import builders from the
three pages E5.1 deletes — and `chatPresetsHost` takes its command type and edit decisions from `chatPresetsPage.ts`
(epic 4's code round); move the shared builders, contracts and handlers out first, and keep the parity tests' inventory explicit
so deleting the old sections cannot shrink the test with them. (c) **The rollout order is its own milestone** (no release
now, by the owner's decision): an OLD coai-mcp still running re-reads the settings file a newer extension rewrites —
`mcp-v0.43.0`'s security lane refuses `signals`/`words` — so the old readers are restarted (or the file kept compatible)
before the new fields are enabled; then the installed binary's `--features` are checked, the extension activated, and
the written file read back. Full consultant behaviour needs a binary that lists `consultantRow`.

**R7, designed 2026-10-07 (prerequisite (a)).** Mapped first: the move record (`chatPresetMove.MovedPreset`, stored as
`coai.chatPresetsMoved`) holds `presetId, runtime, model, name, rowId` and no launch fields; `wasMoved` fingerprints on
those four, so an older build's edit of a CLI path, endpoint, starting text, Team server, remote vendor or the main tick
is silently `unchanged`, and an edit of name, model or runtime is a second row (`chat-<id>-2`) with a second entry for the
same `presetId`. And two readers disagree about such a pair: `catalogChatStep.recordedEntryOf` takes the NEWEST entry,
`chatCatalogModels.movedTo` (a legacy conversation id) the OLDEST.
- **The record carries a snapshot of EVERYTHING the move copies** (the plan round, finding 2): a new entry gains an
  optional `copied` — the name, runtime and model AND every launch field `rowOf` copies, as they were moved. A conflict
  compares the preset with the snapshot, never with the live row, so resolving one ends it. Optional, so every record
  written before it still parses (`movedRecordFrom` keeps a 5-field entry).
- **An edit after the move is a conflicting revision of the SAME preset, never a new row.** The move matches a preset to
  its entry by `presetId`, taking the NEWEST entry for that id (finding 5 — as `recordedEntryOf` does); it is a revision
  when anything in the snapshot differs from the preset. A revision is not moved and does not touch the row: it is
  recorded as pending, and the row keeps working as it is.
- **An entry with no snapshot** (written before it existed) takes its snapshot from its ROW on disk, at its first read
  by this build (finding 4): an older build's edit already made to the preset then differs from the row, and is raised
  as a conflict instead of being silently lost. Where the row is gone (the person deleted it — and a deleted row stays
  deleted), or the preset is gone, the entry is left as it is and raises nothing (finding 3); nothing is read from a
  value that is not there. Known cost: a row the person edited in the NEW page differs from its old preset too, so it
  may be raised once; *Keep the row* ends it.
- **The conflict shows on Chat** (the Settings page's Chat tab, beside the stranded-pick block, `chatTabEmbed`): the row
  as it is, the preset's edited values, and two choices — *Use the edited values* (the row takes them; the snapshot
  takes the preset's whole state) or *Keep the row* (the snapshot takes the preset's whole state, the row unchanged).
- **The choice is durable**: it is written to the record (the snapshot), not to memory, so it survives a reload, a
  window and a restart; a later, DIFFERENT edit raises the conflict again.
- **One reader for a pair already on disk** (a record that holds two entries for one id, written by epic 4's code):
  both readers take the NEWEST, the row an older-build edit went to — `movedTo` changes to match `recordedEntryOf`.
- **Tests, first**: an edit of EACH field the snapshot holds — the list derived from what `rowOf` copies, not written
  out by hand (finding 1) — raises a conflict and makes no second row; each choice, and the conflict gone after it; the
  choice surviving a reload (the record read back); a later different edit raising it again (finding 7); a 5-field
  record snapshotted from its row, raising an older-build edit and nothing for an unedited preset; a deleted row or a
  deleted preset raising nothing and throwing nothing; a legacy conversation id on a two-entry record resolving to the
  newest; the restore still clearing the record. **And the page itself** (findings 0, 6): the assembled Settings page
  run against the DOM shim, as `bundledPage.test.ts` runs pages, both choices clicked — the right message posted, no
  other row's action triggered — and the host applying it to the record.

**Progress, 2026-10-07: prerequisite (b) is done and merged (PR #699)** — on branch `refactor/catalog-e5-extract`, as a pure move (no behaviour
or markup change). The builders, contracts and handlers are in `rolesMessages.ts`, `rolesBlocks.ts`, `commandsMessages.ts`,
`commandsBlocks.ts`, `chatPresetsMessages.ts` and `chatPresetBlocks.ts`; the page-test shim is `test/pageScriptHarness.ts`
(`rolesPageHarness.ts` keeps `runRolesPage`); `panelView` exports the moved sections' builders and
`catalogSections.MOVED_SECTIONS` names them by place; `catalogTabsInPlace.test.ts` and `catalogPlaces.test.ts` list their
inventories literally (research/module_extension.md, "What the new Settings page took from the old pages"). Deviations
from the extraction map: `rolesFieldOf` and `ROLE_TABS` moved too (the editing core and the parser need them); the
limits and team-servers sections are exported as `limitsSection` / `teamServersSection`, which take the state, not as
their raw bodies, so the argument expression is not written twice; every test that used the generic shim was repointed,
not only the six new-page tests (`pageTree.ts`, which those tests read the page through, took `Node` from the roles
harness); and `roleEdit`, `presetEdit`, `roleBlock`, a role prompt's block and the version comparison were split into
smaller functions, because the new modules are held to `complexity: 4` and the CI ratchet refuses a suppression in a new
file — output compared with the previous build and identical.

**Progress, 2026-10-07: prerequisite (a), R7, is built on branch `feat/catalog-e5-r7`** (not pushed, no PR yet). The
snapshot is `MovedPreset.copied` over `chatPresetMove.COPIED_FIELDS`, which `rowOf` is now built from (`copiedOf`);
the match is the id, the newest entry (`entryOf`); an entry without a snapshot takes one from its row (`snapshotted`,
written by the migration); the conflict and both choices are `chatPresetRevision.ts` (pure), the page block
`chatTabEmbed.conflictBlock`, the message `presetEdit`'s `revision`, the host `chatPresetsHost.applyRevision`; `movedTo`
takes the newest entry (research/module_extension.md, "An older build's edit after the move is a revision"). Red first,
each with the real symptom: a name edit made `chat-p-1-2` ("the edit made a second row"); a CLI-path edit raised no
conflict (`[]`); a new entry had no `copied` (`undefined`); both choices wrote nothing (`[]` against
`['vendors','chatPresetsMoved']` / `['chatPresetsMoved']`); a 5-field entry raised nothing for an on-disk edit; a deleted
row came back as a second row; `movedTo('p-1')` gave `chat-p-1`, not the newest `chat-p-1-2`; the chat listed the
edited preset `p-1` beside `chat-p-1`; a 5-field record owing a remap got no snapshot; on the page `presetEdit` read the
message as `ignore`, no conflict block was drawn, and the source pin found no `revision` in the host. Deviations and
consequences: `wasMoved` is the id alone (was a fingerprint), so a positional `preset-N` that shifted onto another preset
is now raised as a conflict instead of moved into a row of its own (`chatPresetMove.test.ts` changed accordingly), and
the chat never lists an edited preset beside its row; a choice also updates the entry's `runtime/model/name`, so an older
build reading the record does not move the preset again; *Use the edited values* changes only the fields the edit
changed (a person's own later edit of another field on Models is kept); a run that only writes snapshots still writes
the remap an interrupted run owed (`opensOn` falls back to `remapOf`); `vaultKeyName` is in the snapshot (the move
copies it) but no preset edit can change it — it is the id the entry is matched by. Not done: the host's VS Code binding
is pinned by source reading, not run; `catalogChatStep.sameMove` (a side's inherited chat model) still compares
fingerprints.

1. **The new page is Settings**: the preview switch removed; the twelve old section builders, their commands and the
   three replaced pages (`rolesPage.ts`, `commandsPage.ts`, `chatPresetsPage.ts` and their panels) deleted; the
   sidebar's Bugz picker reads the rows ticked Bugz (the one sidebar change).
2. **Help and docs**: help in five languages (≈ 70 `HELP` keys and the articles that name tabs — written by the
   implementer in all five, as today); `research/module_extension.md`, `research/module_server.md`, `architecture.md`,
   CHANGELOG; POST_DEPLOY gains "a migrated install opens on Models with every old reviewer, consultant and chat model"
   and the downgrade path.
3. **Clean-up**: `new_design/` and its Sonar exclusion deleted; the restore command kept one more release (T5).
4. **Releases**: mcp 0.44.0 first (E2), the Team server deploy when the operator says, then the extension; post-deploy
   checks against the installed builds.

## Test plan

- **Every story: RED first**, then GREEN; `npm test`, `test:contract`, `test:seam`, `test:host`, the C# suites as MTP
  executables — never `dotnet test`. Before a release, ALL suites.
- **Migration and the write road (E1):** the fixtures above; idempotence; the byte-identical env block; an old-page edit
  reaching the env block; a two-side test (a change on side A leaves side B's env block alone); the workspace-value
  refusal.
- **Skew, every direction:**
  - new extension + coai-mcp 0.43.0 with a catalog it cannot fully express (two CLI instances, multi-use rows,
    effort): parsed by the REAL 0.43.0 binary (`--providers` over the written file, in `test:seam`), and the page's
    "ignored" notes;
  - coai-mcp 0.44.0 + the old extension;
  - an old extension against a file stamped by a newer build;
  - a forked side whose overlay predates the migration;
  - a process-env `COAI_VENDORS` beside the file's;
  - coai-mcp 0.44.0 against a Team server v1 with a non-empty system prompt;
  - a v1 export imported after migration.
- **Runners (E2):** each runtime's argv/body pinned with effort, system prompt and timeout; no launch record holds a
  system prompt; catastrophic regexes; both one-shot modes' exit codes.
- **Pages (E3–E4): tested by RUNNING them** (`bundledPage.test.ts` and the page harnesses), never by asserting page
  source text; every one of the mockup's 61 `check.mjs` flows has a counterpart page test.
- **Layout:** `render-page.mjs` screenshots of every tab at 1920 and 900 px in both themes, read before each PR and
  attached to it; equal heights asserted structurally.

## Boundaries with other plans

| Item | This plan | The other plan's part |
|---|---|---|
| Splitting `panelProvider.ts` | Deletes the settings handlers in E5 instead of moving them | [PLAN_the_panel_provider_is_too_big.md](PLAN_the_panel_provider_is_too_big.md) keeps its sidebar clusters; its vendor, Team-server, local-engine and price clusters are superseded — **this plan goes first** |
| Splitting `panelView.ts` / `roundsLog.ts` | Deletes the settings half of `panelView.ts` in E5 | [PLAN_two_files_outgrew_the_rule.md](PLAN_two_files_outgrew_the_rule.md) keeps the sidebar half and `roundsLog.ts` |
| Splitting `PanelSettings.cs` | E2 adds fields to `VendorDto` and its parsing | [PLAN_panel_settings_is_too_big.md](PLAN_panel_settings_is_too_big.md) moves the parsing — whichever lands second rebases onto the other; E2 adds no new section to the file |
| Local effort in the panel | E2/E3 expose it per instance | [PLAN_local_trust_and_vllm.md](PLAN_local_trust_and_vllm.md) keeps per-origin acknowledgement, vLLM keys, 401 reading, `num_ctx` refusal |
| A model's liveness | E2.1 fixes the probe's program; E3 draws the verdict and ✓ Check | [PLAN_provider_liveness.md](PLAN_provider_liveness.md) owns the three liveness states and their cache |
| How a probe says it is working | E3 uses it | [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) owns it |
| Arrow keys on tab strips | E3.1 consumes `tabKeys` | [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) steps 2–4 |
| The chat presets page writing on every keystroke | E4.3 replaces the page | [PLAN_chat_presets_write_every_keystroke.md](PLAN_chat_presets_write_every_keystroke.md) is superseded once E4.3 lands |
| Which vendor answers which caller by default | E4.2 keeps the shipped map (absent = shipped) | [PLAN_consultant_defaults_from_phase_0.md](PLAN_consultant_defaults_from_phase_0.md) owns the measurement |
| Checking a WSL consultant from Windows | D10 generalises the check record | [PLAN_a_wsl_consultant_is_checked_from_windows.md](PLAN_a_wsl_consultant_is_checked_from_windows.md) keeps the cross-side launch |
| The question consultant's API and web tails | E2.3 adds the API consultant; D4 the shared file | [PLAN_question_consultant_tails.md](PLAN_question_consultant_tails.md) keeps agy web and api web |
| Translations that go stale | D11 moves help with the tabs | [PLAN_a_stale_translation_is_invisible.md](PLAN_a_stale_translation_is_invisible.md) owns detecting staleness |
| Page tests that run the page | E3–E4 write only run-the-page tests | [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) owns the existing source-assertion backlog |
| The Security lane's calibration | none | [PLAN_security_lane_calibration_tail.md](PLAN_security_lane_calibration_tail.md) |

Disjoint from the rest of `todo/`. Each plan in the table gets the same row, pointing back here.

## Tails — after the redesign

| # | Tail | Why it waits |
|---|---|---|
| T1 | The old MCP server tab's sentence "reads them when your MCP client next starts it" (`panelView.ts:1864`) | The new page says the true one; T1 touches the old page only if E5 slips |
| T2 | **No security check triggers on `crypto`** — the signal is detected, no preset runs on it | A catalog change and a measurement; the routing table makes it visible first |
| T3 | Export writes the shared settings only; a side's own rows are not in the file | v2 (E1.5) exports the exporting side's rows; per-side export of the rest is its own decision |
| T4 | The mockup's own files are over 800 lines | The mockup is deleted in E5; product modules are born under the limit |
| T5 | `coai.migratedFrom` and the restore command dropped | One release after E5 |
| T6 | The extension's `vendorsFrom` turns a runtime it does not know into `codex` — an older panel reading a newer one's rows, and re-saving them so | coai-mcp refuses it by name since E2.1; the extension should keep the row and show it unrunnable, which widens the `Runtime` type |
| T7 | `COAI_BUGZ_MODEL` is written into the env block and read by nothing in coai-mcp (the collector takes `--model`) | Remove it, or give it a reader, when the Bugz picker moves to the catalog (E5.1) |
| T8 | Contract 2 is tested against the server in-process and a stub on a socket, never a deployed one: no live call has shown a remote row's effort APPLIED on the server's claude, or its system prompt taken | The deploy is the operator's decision; the measured call follows it, at a paid review's cost |
| T9 | `freeVendorId` gives a freed id to the next row added, so a removed row's spending history and vault key name are inherited by a new one | A retired-id list (bounded, per side) when the ledger keys on it; until then the remove dialog does not promise the id is not reused |

## Definition of Done

- [ ] Models is the only place a model is added, edited or removed; every feature refers to a row by id.
- [ ] A migrated install shows every reviewer, consultant, question-consultant row, chat model, Bugz model and
      security pair it had, with the same keys, prices and paths — proved by fixtures, by the real 0.43.0 binary, and
      on the operator's own install; the restore command puts the old settings back.
- [ ] coai-mcp 0.43.0 keeps working with the new extension, and the page says which controls it ignores.
- [ ] Effort reaches every runtime but Antigravity; system prompt and timeout reach every runner without ever touching
      argv or a log; the Team server carries them at contract v2 — each pinned by a test and observed once.
- [ ] A second instance of a CLI vendor probes the right program.
- [ ] Every control and state of the coverage audit (Design notes § 7) exists on the new page.
- [ ] Two columns from 1100 px, one below; side-by-side blocks one height; size and brightness on every page; "new" for
      7 days — screenshots at both widths, both themes.
- [ ] Help in five languages, `research/`, CHANGELOG and POST_DEPLOY describe the new page; `new_design/` gone.
- [ ] Every epic went through a coai plan round and code round with every finding resolved, and the session's own
      reviewers alongside; every pull-request thread resolved; the cadence consultations taken.
- [ ] Releases cut from tags after merge; post-deploy checks run against the installed builds.

## What the review changed (2026-10-04)

| Finding (who) | What changed |
|---|---|
| The old page's writes never reach the catalog; a later catalog write reverts them (gate ×2, blocking; own critic, blocking) | **The architecture.** No second store: the catalog IS `coai.vendors`, widened (D1), everything else refers by id (D2), one write road (D3). Nothing to keep in sync. |
| Downgrade/Sync edits silently overwritten (gate) | Same root, same fix; the downgrade path is tested and written down. |
| Migration ids for sources without an id; "keep every id" collides (gate; own critic) | Deterministic ids, an exact merge rule, a clash rule, fixtures (D3). |
| Which layer the derived keys go to (gate; own critic) | Migration per layer; no derived keys left to place. |
| `ModelInstance` could not round-trip today's rows: runtime, three-way `document`, chat `main`/`startingPrompt`, question rows, the shipped consultant pair (own critic) | The row is kept and widened; references keep the rest; absent = shipped stays. |
| The legacy wire cannot express the catalog (gate; own critic) | The table *What the legacy wire cannot carry*, with the note the page shows. |
| API-consultant transcripts not budgeted (gate) | A growth row with size, retirement and sweep. |
| The vault keyed by id vs a shared key (gate) | `vaultKeyName` is the key; Duplicate copies it; removal never deletes a key. |
| "Try it" and `--check-security`; a JavaScript matcher (gate; own critic) | `--check-security` in E2.4 and PROJECT.md; no JavaScript copy. |
| Regex safety unverified (gate) | NonBacktracking + timeout + caps, catastrophic-pattern RED tests. |
| System prompt on argv, in logs, to the Team server (gate; own critic) | Never argv, never logged, a cap; on the Team server behind an operator switch. |
| Effort is a free string (gate) | Levels in the shared file, validated on write, observed once per runtime. |
| ✓ Check: cost, concurrency, durable status (gate; own critic) | D10 built on `ConsultCheckState`. |
| No rollback (gate) | Backup + restore command, kept one release after the switch-over. |
| E2 too big; one coarse version gate (own critic) | Five stories, each with its own `*_SINCE`. |
| A workspace can set the model settings (own critic) | E1.1 reads the user layer and the side overlay only. |
| `COAI_MODELS` precedence and pristine output (own critic) | No new wire key at all. |
| The Team server needs a timeout too; client prompts on a shared box (own critic) | E2.5. |
| The claude-2 cause stated wrongly (fact-checker) | Rewritten: the probe runs the row id (`ReviewerRuntime.cs:226`), launches are fine. |
| Wrong line for local effort, missing `FEATURE_SINCE`, the fix site for the checkout sentence, `pageDocument`, `render-page.mjs` limits, five missing boundaries, the sidebar Bugz picker, the three replaced pages (fact-checker) | Each corrected in place. |
| The gate's commands: 4–5 epics of 3–5 stories, one gate per epic, consult on a cadence | Ten epics became five. The split was done on Opus — Fable is at its monthly spend limit (2026-10-02) — and is said so here. |

### The plan round of epic 1 (session `ac973900`, 2026-10-04, good_enough, 14 findings accepted)

| Finding | What changed |
|---|---|
| Restore cannot stick; the backup is overwritten; which keys and which layer (0, 1, 8) | Every written key backed up per layer, once; a `restored` mark stops re-migration (D3). |
| Multi-key writes are not atomic; a missing-row reference; two windows (2, 11) | A fixed write order with the marker last; rerun finds rows by id first; a missing reference is left out and logged. |
| Migrated rows reach `COAI_VENDORS` and the old page as reviewers (7) | They review nothing and are left out of the wire; the old page hides them; byte-identity measured on the overlapping fixture. |
| Prompts in an environment variable exceed its limit (4, 9) | Measured: the wire is a field whitelist, so nothing new crosses in E1; prompts never go in an env variable (D1). |
| A workspace value silently ignored; reads at some sites only (3, 10) | One reader, a scan test, a one-time notice with a copy offer. |
| Import replaces with no rollback (5) | Backup before import; the confirmation names missing vault keys. |
| The 64-row cap after migration (6) | Checked before writing; the layer is left untouched. |
| An older build strips the new fields on its first write (12) | Verified in `vendorsFrom`; what is lost and what is regained is written down; no second store. |
| `feature-availability.json` ahead of coai-mcp's own lists (13) | Frozen to today's lists in E1; the mirror tests stay. |

### The plan round of epic 4 (session `21ec1de8`, 2026-10-05, good_enough, 8 findings accepted, 2 rejected)

| Finding | What changed |
|---|---|
| The record's model name is not remapped (0) | Corrected: a record's preset references are `providerId` and `chosenId`; its `modelId` is a model name the move does not change (except a legacy one equal to a preset id). Each is mapped, with a resume test per old preset. |
| No failure path for the multi-store rewrite (1) | Settings keep epic 1's order (backup once, rows, references, marker last); FILES — records and spend lines — are never rewritten, only mapped on read through the stored `chatPresetRows` table. |
| An older binary meets a chat row (2) | coai-mcp never sees one (E1.4) and does not serve chat; for an older EXTENSION, `coai.chatModelPresets` is kept until E5. |
| The host modules' command shape is unspecified (3) | One exported command type per host, dispatched by prefix; a refusal sentence shown through the panel's refusal path. |
| Which source-text tests move or go (4) | DoD: each folded page's tests run the new page; source-text assertions converted; the reader carries a companion assertion. |
| `COAI_ROLES` at `ROLE_SWITCH_SINCE` untested (5) | A boundary test added to the test plan. |
| The MCP clients reader's timeout (7) | A pure file read; no process is launched. |
| Why E4.5 precedes E4.3 (9) | One sentence in the build order. |
| REJECTED — `catalogRows` and `feature-availability.json` unbudgeted (6) | Both exist: `catalogRows` on this branch (6903e000), row writes since E3, the file since E2. |
| REJECTED — nothing creates the use ticks (8) | The ticks exist on every Models card since E3; the E1 migration created the consultant rows. |

The gate's commands applied: one gate for the epic (the code round over the whole diff), autonomous work with
red-green tests. Not possible: the owed consultations (cadence for epics 4–5, risk for epic 4, and the ones owed from
epics 1–3) — the consultant is switched off in this installation (`COAI_CONSULT_ENABLED`), so the code round of this
epic waits on the person. The split stays on Opus — Fable is at its monthly spend limit.
