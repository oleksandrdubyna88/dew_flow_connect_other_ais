# PLAN — the gate's commands and the whole configuration are the person's to edit, export and import

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code` (a config export/import pair, a
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
   - `exportedSettings(declared, read)`: every setting the manifest declares (`contributes.configuration`)
     whose effective value (through `readerFor`, so the per-side overlay counts) differs from its declared
     default — compared as canonical JSON.
   - **Never exported**, because they are secrets or this machine's layout: `credsKey`, `dataDirectory`,
     `dataSide`, `alsoWatchDataDirectories`, `perSideSettings`; and any `executablePath` field inside a
     value (vendors, consultants) is removed — a path on this machine is not a setting another one can use.
     Tokens are not settings at all (files under the data directory, or VS Code secret storage) and so are
     never read. Team-server URLs and base URLs ARE exported: they are configuration a person typed, not
     secrets.
   - The file: `{ "format": "coai-config", "version": 1, "exportedAt": "…", "settings": {…}, "prompts": {"<id>": "<text>"} }`
     — prompts are every `<dataDir>/prompts/*.md` whose name passes the same id guard `promptFile` applies.
   - `importedConfig(text, declared)`: parses and validates — the format and version, `settings` an object,
     every key declared by THIS build and not in the never-exported list, every prompt id valid — and
     answers what will be applied and what is refused, by name. A file from a newer build with keys this
     one does not know is not an error: those keys are listed as refused, the rest applies.
2. **The host, `configTransferCommands.ts`**: `coai.exportConfig` (save dialog → `writeFileAtomically`) and
   `coai.importConfig` (open dialog → `importedConfig` → ONE modal summary — *"Apply N settings and M prompt
   texts from <file>? Not applied: …"* → `saveSetting` per key, prompt texts written atomically to
   `promptFile`). Import replaces each listed setting's value whole; settings not in the file are left as
   they are. It deletes nothing: a role the file lacks keeps its prompt files, which is the safe direction
   (`roleDeletions` owns deletion).
3. **The manifest**: both commands, in the `…` menu (`view/title`, a `config@1/2` group), with a help
   paragraph (the help-coverage test requires one).

**Tests (red first):** `configTransfer.test.ts` — a default is not exported; a changed setting is; the
per-side value is the one exported; the never-exported keys and nested `executablePath` never appear; a
round trip export → import gives back the same settings and prompts; a wrong format or version is refused
with a sentence; an unknown key, a never-exported key and a bad prompt id are refused by name while the rest
applies. Docs: `module_extension.md`, `module_tests.md`, CHANGELOG, help.

## Epic A — command texts become data (server)

- The shipped command texts move to embedded templates (`src_mcp/src/commands/command-*.md`, outside
  `src/prompts/` like `consult.md`), with placeholders for the computed parts; the MARKER phrases stay in
  code, outside the editable text, so no override can make the bench report a switch as not applied.
  Default output stays byte-identical (`GateCommandsTests` pins it).
- `<dataDir>/prompts/command-<id>.md` overrides a shipped text, through the existing `RolePrompts`
  mechanism; `GateCommands` receives its texts as data (it is pure).
- Custom commands: `COAI_COMMANDS` = `[{id, title, enabled, stage}]`, parsed by a `CommandsSetting` twin of
  `CommandModelsSetting`; an enabled one is appended to the orders with its text, one with no text is
  skipped with a sentence, as a role is. Written to the rounds database like every order.

## Epic B — an Edit commands page (extension)

- `coai.commands` setting + pure `commands.ts` / `commandsEdit.ts`, the env block, per-side.
- `commandsPage.ts` / `commandsPanel.ts` on the roles page's pattern: the shipped commands with their text
  shown and overridable (restore = delete the file), custom rows added and removed, a server-version note
  for a server too old to read them; opened from the *Gate* section of the panel.

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
