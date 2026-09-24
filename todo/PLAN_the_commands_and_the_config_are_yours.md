# PLAN — the gate's commands and the whole configuration are the person's to edit, export and import

> Status: **Epic C built 2026-09-24 (its code round and PR open); Epics A and B not started.** Scope: `src_vs_code` (a config export/import pair, a
> commands page) and `src_mcp` (command texts as data, custom commands). Issue #467. Three epics, ONE gate
> each (the operator's instruction for this batch), built C → A → B.
>
> Related docs: [module_extension.md](../research/module_extension.md), [module_server.md](../research/module_server.md),
> [architecture.md](../research/architecture.md), [PLAN_the_split_is_sized_and_gated_once.md](../research/PLAN_the_split_is_sized_and_gated_once.md).

## The ask (issue #467)

1. *"New gate commands: one gate per epic, one gate per task — and where the existing prompts already say it,
   remove it from there."*
2. *"Make the prompts editable, and let me add new commands by hand, the same way as roles."*
3. *"In the menu, two buttons: Export config and Import config. Everything a person edited — prompts, every
   setting that differs from its default — written to a JSON file; import reads it back and applies it."*

## What is already true

- **Item 1 shipped with issue #131** (`research/PLAN_the_split_is_sized_and_gated_once.md`): the panel's
  *Gate* switch `epic | task` (`COAI_GATE_PER`, `PanelSettings.GatePer`, `GateCommands.GateScope`), and the
  split order's gate cadence is built from it — no command text still orders a gate per story.
  **One caller-facing sentence is left:** `.agents/conventions/common/coai-review-gate.md:37` (*"split this
  plan into epics and stories and close each one properly"*), which the extension pastes as its CLAUDE.md
  snippet. That file is a SHA-pinned shared rule of the conventions repository: rewording it is a
  conventions release and a pin move in every consumer — not something this repository can edit. Recorded
  as the open tail below; two stale code COMMENTS here (`settingsShape.ts:146`, `PanelSettings.cs:331`) are
  fixed in Epic A.
- **Command text is hardcoded C#** (`src_mcp/core/Commands/GateCommands.cs`: `Preamble` `:74-77`,
  `SplitCommand` `:129-137`, `GateCadence` `:175-195`, `AlreadySplitCommand` `:210-219`, `ModelCommand`
  `:240-250`, `AutonomyCommand` `:265-290`), parts of it computed per round (plan size, numbers, gate
  scope, model names), and three MARKER phrases are held by `shared/command-models.json` and the bench
  (`src_bench/CoaiBench/Running/SettingsApplied.cs:86-99`).
- **The roles page is the model to copy**: rows in a setting (`coai.roles`), texts in
  `<dataDir>/prompts/<id>.md` written only by `rolesPanel.ts` `writeText` (`:484-509`), override + restore
  default, mirrored to the server (`serverSettingsSync.ts`), read by `PanelSettings.ParseRoles` and
  `RolePrompts` (`src_mcp/src/Server/RolePrompts.cs`).
- **No export of settings exists.** The panel's `…` menu is `view/title` in `src_vs_code/package.json:886-922`;
  the rounds CSV export (`extension.ts:1335-1340`, `roundsExport.ts`) is the pattern for a save dialog + an
  atomic write.

## Epic C — Export config / Import config (first: independent, and what the person asked for most directly)

1. **A pure module, `configTransfer.ts`** (no `vscode`):
   - `exportedSettings(declared, globalValueOf)`: every setting the manifest declares
     (`contributes.configuration`) whose BASE value (`config.inspect(key).globalValue`) differs from its
     declared default — compared as canonical JSON. Per-side overrides are NOT transferred: they belong to one
     side of one machine, and an import writing them into the base layer would change every other side
     (codex and gemini, the plan round). The file says so in a `note` field.
   - **Classified, not listed from memory**: a test walks every setting the manifest declares and fails when
     one whose name or description says key, token, secret or password is exportable — so a secret added
     tomorrow cannot leak through a list nobody updated (codex, the plan round).
   - **Never exported**, because they are secrets or this machine's layout: `credsKey`, `dataDirectory`,
     `dataSide`, `alsoWatchDataDirectories`, `perSideSettings`; and any `executablePath` field inside a
     value (vendors, consultants) is removed — a path on this machine is not a setting another one can use.
     Tokens are not settings at all (files under the data directory, or VS Code secret storage) and so are
     never read. Team-server URLs and base URLs ARE exported: they are configuration a person typed, not
     secrets.
   - The file: `{ "format": "coai-config", "version": 1, "exportedAt": "…", "settings": {…}, "prompts": {"<id>": "<text>"} }`
     — prompts are every `<dataDir>/prompts/*.md` whose name passes the same id guard `promptFile` applies.
   - `importedConfig(text, declared)`: parses and validates — the format, a version this build writes (1;
     any other is refused with a sentence saying which build wrote it), `settings` an object, every key
     declared by THIS build and not in the never-exported list, every prompt id valid — and answers what will
     be applied and what is refused, each with its REASON (*unknown to this build*, *never transferred:
     secret or machine path*, *not a valid prompt name*). Unknown keys from a newer build are refused, the
     rest applies.
   - **This machine's paths are kept**: an imported `vendors` / `consultants` entry takes the `executablePath`
     the SAME entry (by id) already has here — whole-value replacement would otherwise wipe every local CLI
     path the export deliberately left out (gemini, the plan round).
2. **The host, `configTransferCommands.ts`**: `coai.exportConfig` (save dialog → `writeFileAtomically`) and
   `coai.importConfig` (open dialog → `importedConfig` → ONE modal summary — *"Apply N settings and M prompt
   texts (K of them replace text you have) from <file>? Not applied: …"* → the base value of each key,
   prompt texts written atomically to `promptFile`). Import replaces each listed setting's value whole;
   settings not in the file are left as they are. It deletes nothing: a role the file lacks keeps its prompt
   files (`roleDeletions` owns deletion).
   - **All or nothing** (local, codex, gemini, the plan round): the previous base value of every key and the
     previous text (or absence) of every prompt it touches are taken BEFORE the first write; a write that
     fails restores all of them and the notice names what failed. The restore itself is logged if it fails.
3. **The manifest**: both commands, in the `…` menu (`view/title`, a `config@1/2` group), with a help
   paragraph (the help-coverage test requires one).

**Tests (red first):** `configTransfer.test.ts` — a default is not exported; a changed setting is; the
per-side value is the one exported; the never-exported keys and nested `executablePath` never appear; a
round trip export → import gives back the same settings and prompts; a wrong format or version is refused
with a sentence; an unknown key, a never-exported key and a bad prompt id are refused by name while the rest
applies. Docs: `module_extension.md`, `module_tests.md`, CHANGELOG, help.

**As built (2026-09-24):** the all-or-nothing apply is its own pure module, `configApply.ts`, over an
`ApplyIo` seam, so the rollback is tested by failing a write on purpose rather than asserted; the declared
defaults are read through `settingRefused.sectionsOf` (exported, not copied). The classification test sets
every secret-looking key and runs a real export rather than checking a list. The help article is
`move-your-config`.

## Epic A — command texts become data (server)

**Where the texts are today:** `src_mcp/core/Commands/GateCommands.cs` — `Preamble` (:74), `Judgement`
(:148-155, five sizes), the measured sentence inside `SplitCommand` (:133-136), `GateCadence` (:175-195,
three scopes), `AnotherCodeRound` (:171), `AlreadySplitCommand` (:210-219, two scopes), `ModelCommand`
(:240-250), `AutonomyCommand` (:265-290). The one caller is `PanelService.cs:1419-1481`, which already
writes every order into the rounds database (`commands`, `:1520`) — a custom command appended there is
recorded for free.

1. **The shipped texts move to `shared/commands/<id>.md`**, embedded in `CoaiMcp.Core` the way
   `shared/builtin-roles.json` is (`CoaiMcp.Core.csproj:17`) — `shared/` because Epic B's page must show the
   same default text, and one file both halves read cannot drift. Fourteen ids, each one sentence family:
   `command-preamble`, `command-autonomy`, `command-model`, `command-split-none|small|medium|large|huge`,
   `command-split-measured`, `command-cadence-epic|task|single`, `command-another-code-round`,
   `command-already-split-epic|task`.
2. **Placeholders** for the computed parts, replaced by plain `string.Replace`: `{scope}` (autonomy),
   `{strongest}` / `{implementation}` (model), `{numbers}` / `{verdict}` (measured). An override that drops
   one simply does not say it — the order is the operator's words.
3. **The markers stay in code, OUTSIDE the editable text**, prefixed before it: `Work AUTONOMOUSLY. `,
   `GateCommands.ModelOrderMarker`, `GateCommands.GateOrderMarker`, and `This plan is a PIECE of a split
   that is already under way` — the phrases the bench (`SettingsApplied.cs:86-99`) and
   `shared/command-models.json` hold, so no override can make a switch read as not applied.
4. **`CommandTexts`** (core, pure): the shipped texts plus an override map; `Text(id)` answers a non-blank
   override, else the shipped text. `CommandContext.Texts` defaults to shipped only, so every existing test
   and caller is unchanged, and **the default orders stay byte-identical** — `GateCommandsTests` pins them,
   and a new test compares every order across every switch combination with the pre-change strings.
5. **Overrides from `<dataDir>/prompts/command-<id>.md`**, read through `RolePrompts` (its id guard and its
   rule that an EMPTY override is no override), once per round in `PanelService`, for the fourteen ids.
6. **Custom commands: `COAI_COMMANDS` = `[{id, title, enabled, stage}]`**, `stage` ∈ `plan | code | any`
   (default `any`), parsed by `CommandsSetting`, a twin of `CommandModelsSetting` (unreadable → no custom
   commands and one panel complaint; a row with a bad id — not a slug, or one of the fourteen shipped ids —
   dropped with a complaint naming it). An enabled command whose stage matches the round is appended AFTER
   the built-in orders with the text of `<dataDir>/prompts/command-<id>.md`; one with no text is left out,
   logged, and named in a new reply field `commandsSkipped` (absent when empty, like `notes`): *"custom
   command '<title>' has no text — write it in <file>"*.

**Tests (red first):** `CommandTextsTests` — every shipped id embedded and non-blank; an override wins, a
blank override does not; placeholders replaced. `GateCommandsTests` — byte-identical defaults across every
switch combination; an overridden autonomy order still opens with `Work AUTONOMOUSLY.` (and the other three
markers the same) whatever the override says. `CommandsSettingTests` — parsing, complaints, a shipped id and
a bad id refused by name, the stage default. Through `PanelService`: an override file changes the reply's
order and deleting it restores the default; an enabled custom command reaches the reply AND the rounds
database; a disabled one or one of the other stage does not; one with no text is in `commandsSkipped` and
not in `commands`. Docs: `module_server.md`, `module_core.md` if the commands live there, `module_tests.md`,
CHANGELOG, `PROJECT.md` if a setting list is kept there.

## Epic B — an Edit commands page (extension)

- `coai.commands` setting + pure `commands.ts` / `commandsEdit.ts`, the env block, per-side.
- `commandsPage.ts` / `commandsPanel.ts` on the roles page's pattern: the shipped commands with their text
  shown and overridable (restore = delete the file), custom rows added and removed, a server-version note
  for a server too old to read them; opened from the *Gate* section of the panel.
- **Tests**: the page RUN against the DOM shim (the roles page's harness) — adding, overriding, restoring and
  removing a command post the commands the parser reads (gemini, the plan round).

## Build order

C1 export → C2 import → (gate, PR) → A1 templates + overrides → A2 custom commands → (gate, PR) → B1 setting
and rules → B2 page → (gate, PR).

## Open tail

- The CLAUDE.md snippet's *"close each one properly"* (`coai-review-gate.md:37`) is a conventions-repository
  change with a release and a pin move in every consumer.

## Definition of Done

- [ ] C: Export writes every changed setting and prompt, never a secret or a machine path; Import applies
      them after one confirmation and names what it refused.
- [ ] A: every command's text can be overridden from a file, and a custom command reaches the caller; the
      default orders are byte-identical.
- [ ] B: the commands are edited on a page like the roles.
- [ ] Tests red first for every epic; both suites and lint green; docs updated; this plan promoted to `research/`.
