# PLAN — The Security lane tab reads at a glance: two-column pickers, a general prompt first, coloured cards and real buttons

> Status: **epics 1–3 implemented and reviewed on the branch, 2026-10-04; epic 4 (help, screenshots, release) open.**
> The plan itself was reviewed twice that day:
> - by two of the session's own agents (a design critic and a fact-checker; see *What the review changed*);
> - then by the **coai plan round**, session `041939d9` on branch `docs/security-tab-plan` (qwen / glm-5.3,
>   verdict `proceed`, four findings, all accepted and folded in below).
>
> Scope, extension side (`src_vs_code/src`):
> - `securityLaneView.ts`, split into small modules, and `securityLane.ts`;
> - a new host module for the prompt files;
> - one-line hooks in `panelView.ts` and `panelProvider.ts`;
> - the five help articles.
>
> Scope, server side (`src_mcp`), production code and not only tests:
> - `shared/security-lane.json`, where the general prompt joins the catalogue as a new **"always"** kind;
> - `src_mcp/core/Security/SecurityCatalog.cs` (the `Always` record);
> - `src_mcp/core/Security/SecuritySignals.cs` (`Triggered`);
> - `src_mcp/src/Server/SecurityLaneSetting.cs` (`IsPresetWithoutTrigger`, `ReadPrompts` and its new complaint);
> - their C# tests.
>
> Also the TS/C# catalogue tests and the security-lane docs. Ends in an **MCP release**, then an **extension
> release**.
>
> Every `file:line` below was read at `origin/main` = `24f6be7e` on 2026-10-04. Line numbers move; re-read
> before cutting.
>
> Related docs: [module_security_lane.md](../research/module_security_lane.md),
> [security_prompt_catalog.md](../research/security_prompt_catalog.md),
> [PLAN_a_security_lane_runs_beside_the_gate.md](../research/PLAN_a_security_lane_runs_beside_the_gate.md),
> [PLAN_edit_roles_in_tabs.md](../research/PLAN_edit_roles_in_tabs.md),
> [PLAN_every_page_reads_alike.md](../research/PLAN_every_page_reads_alike.md).

## The goal, as the operator asked it (2026-10-04, five screenshots of Settings → Security lane)

1. **The tag legend is unreadable.** Sixteen `id (label)` pairs are joined with commas into one paragraph
   (`securityLaneView.ts:17-18`, drawn at `:30`). The operator asked for two columns (*"должно идти в 2 колонки,
   а то читать нереально"*).
2. **A prompt's name is the same size as its body text.** The card's `<legend>` is a bare `esc(p.id)`
   (`securityLaneView.ts:80`), with no class and no fieldset/legend rule in the shared CSS. The operator asked
   for a bigger font so the card can be told apart at a glance.
3. ~~**A model dropdown on every default prompt.**~~ **Withdrawn by the operator during planning.** The
   `gemini` checkbox is not hardcoded: it is one checkbox per *enabled* reviewer row (`securityLaneView.ts:82-83`),
   and the operator confirmed that is correct (*"оно берет доступные из ревьюверов. это ок. ничего не делай
   тут"*). This plan does not touch it.
4. **"Run when code matches" and "Prioritize source" are two run-on lines of 15 and 16 checkboxes**
   (`securityLaneView.ts:86-89`). They wrap mid-label, and the operator rarely needs them. Asked for: two
   parallel columns, inside a block that is **collapsed by default** and opens only on a click, because this
   is fine-tuning (*"это нужно очень редко при тонкой настройке"*).
5. **The general prompt is missing, and nothing says which prompts are shipped.**
   - `src_mcp/src/prompts/redteam-general.md` exists, but it is not in the catalogue
     (`shared/security-lane.json:20-33`). The tab draws only the catalogue plus what the person typed into
     "Add prompt".
   - The operator wants it **first in the list**.
   - Shipped prompts get a **green** frame and prompts the person added get a **purple** frame.
   - A shipped prompt the person changed turns **orange** and gains a **Restore default** button.
6. **"Add reviewer / prompt pair" and "Remove pair" are checkboxes** (`securityLaneView.ts:34`, `:63`). They
   should be buttons. The operator also asked: once I add a pair, where do I write a prompt of my own?
   Today the answer is a three-step path the page never explains:
   1. Type `redteam-x` into a text box (`:32`).
   2. Find the new card and press "Edit local prompt override". This creates an empty
      `<dataDir>/prompts/redteam-x.md` and opens it (`securityPromptEditor.ts:8-25`).
   3. Write the text there, then pair the prompt.

### The operator's rulings during planning (2026-10-04)

| Question | Ruling |
|---|---|
| Item 3, a per-prompt model dropdown | **Dropped.** Reviewer rows already supply the choice. |
| How the general prompt behaves | **A shipped prompt, shown first, that is only ON or OFF.** When it is on, it runs on **every** change, with no triggers and no conditions block. *"15 triggers do not cover 100 % anyway"* (*"15 тригеров 100 проц не покрывают всеравно"*). This replaces the operator's first answer the same day ("a preset with triggers"). It **reverses** the 2026-10-01 ruling that kept general "an additional custom prompt, outside the twelve presets" ([PLAN_a_security_lane_runs_beside_the_gate.md](../research/PLAN_a_security_lane_runs_beside_the_gate.md) §1). That plan gets a deviation note in the same change. |
| The Roles page's opposite use of green; other tabs with one-shot checkboxes | **Not touched** (*"нет. не трогай"*). |
| A hand-added general from before this change | **A visible warning plus a one-click fix.** |
| What makes a shipped prompt "edited" (orange) | **Its text or its conditions.** A local override file with usable text, OR triggers/focus that differ from the shipped ones. One Restore puts both back. |
| How a person writes their own prompt | **A "+ New custom prompt" button.** It asks for a name, creates the purple card and the file, and opens the file. The card says the text is missing until it is written. The pair's Prompt dropdown offers the same entry. |

## What is true today (verified 2026-10-04 on `origin/main` 24f6be7e)

| Fact | Where | Consequence for this plan |
|---|---|---|
| The tab is 94 lines of string templates with no CSS of its own. It is registered as a Settings section. | `securityLaneView.ts:1-93`, `panelView.ts:457` | The view has room to grow. Its CSS, script and commands live in new security modules and reach `panelView.ts` through one-line hooks (see the DoD), because `panelView.ts` is far past the 800-line cap. |
| **"Add pair" as a checkbox misbehaves after a no-op.** A successful add changes the markup and reloads the page, so the box comes back unticked. When the add does nothing (16 pairs, no free reviewer × prompt combination, no enabled reviewer), the box **stays ticked**. The next press unticks it and posts `false`, which adds nothing, so it takes a third press. | `securityLaneView.ts:34`, `securityLane.ts:155-160`, repaint via `surfaceSlot.ts:152` | Buttons fix it, and at the cap the button is disabled with a **visible** reason. (The first draft blamed the page's dedupe at `panelView.ts:612-614`. That was wrong, and the review corrected it.) |
| Buttons use a separate channel: `data-command` → `{type:'command'}` → `run()` in the provider, gated by `PANEL_COMMANDS`. **Commands are not serialised with setting writes.** Only `'prompt'` and `'setting'` messages go through `this.enqueue` (`panelProvider.ts:983`, `:986`). A command is `track(from, m, () => this.run(...))` (`:992-993`). | `panelView.ts:754-756`, `:3228+`; `panelProvider.ts:2147-2149` | A command that writes the lane must enqueue its read-modify-write. A **question to the person must stay outside the queue**: `render` awaits the queue (`panelProvider.ts:1700-1712`), so a dialog inside it would freeze every write and repaint. The house pattern is `askPerson` (`personWait.ts:35`), then enqueue (`customModel`, `panelProvider.ts:2499-2510`). |
| The page's paint key is its markup. A key change reloads the document (`surfaceSlot.ts:112-126`, `:152`); only live regions are patched. | `panelSurface.ts:11-16`, `:86-96` | A collapsed `<details>` inside the pane would snap shut on the next reload, and ticking a box inside it causes one. |
| **Page-local UI state already survives a reload** through `vscode.setState`. The harness supports it (`RunOptions.saved`). | `selectSearch.ts:102-119`; `test/panelPageHarness.ts:396-400`; `selectSearchPage.test.ts:260` | The open blocks are kept the same way. The host needs no new message and no script literal. |
| **Focus is already lost on every repaint of this tab.** A Security control's id is `securityLane||||trigger:redteam-authz:sql` (`idOf`, `panelView.ts:583-585`). `FOCUS_ID`'s last group allows only `[A-Za-z0-9_.-]`, so the `:` makes `focusLiteral` return `null`. | `panelView.ts:402`, `:414-416` | An existing defect. After a tick, the page reloads at the top and the person loses their place. With 13 cards that is unusable, so it gets a RED test and a fix. |
| A stored prompt list is merged with the catalogue as *stored first, then missing seeds appended*. **Every save writes the whole merged list back**, so one click freezes a snapshot of every preset's triggers into `settings.json`. | `securityLane.ts:60`, `:132-141`; `panelProvider.ts:1777-1779` | (a) A new shipped `redteam-general` would sort **last** for existing users, so order is a rendering rule. (b) A frozen snapshot would turn untouched presets orange the day the catalogue changes, and would block shipped updates. Storage is compacted on write (D2). |
| **What reaches the server is the merged lane**, not the stored array. | `securityEnv`, `securityLane.ts:17-20`; `settingsShape.ts:409`, `:431` | Compacting storage does not change the wire. |
| The server refuses a preset whose trigger list is empty, **per prompt**. Only that prompt's pairs are excluded and the lane stays on. `Triggered` answers `false` for an empty preset and `true` for an empty custom prompt. | `SecurityLaneSetting.cs:168-169`, `:176-186`; `SecurityRoster.cs:69`; `SecuritySignals.cs:91-92` | An "always" preset is a **new kind**, not "a preset with no triggers". If general were simply added to the catalogue with `triggers: []`, it would be refused. The catalogue entry carries `"always": true`, and both rules exempt it (§F). A hand-registered general with `triggers: []` (the old recipe in `security_prompt_catalog.md:30`, `module_security_lane.md:120`) keeps meaning exactly what it meant: run every time. |
| The server decides "has text" as follows: an override that is blank falls back to the shipped text. An override that is **only the `<!-- OPERATOR: … -->` placeholder**, or **over 64 KiB**, means no text, and the pair is excluded with *"prompt unavailable or over 64 KiB; write text at \<path\>"*. | `SecurityRoster.cs:89-90`, `:120-140`; `RolePrompts.cs:96-97` | The card's text state mirrors exactly this rule. A blank file must not turn a card orange: one click on Edit creates an empty file (`securityPromptEditor.ts:16-22`). |
| The page learns about other prompt overrides by reading them at render time. | `panelProvider.ts:1121-1122`, `qconsultHost.ts:61-65` | That is the precedent. Security prompts are edited in an ordinary text editor, so this plan also needs a watcher. The pattern is `watchHealth` (`consultantHealthPanel.ts:184-194`, private). |
| Two select sentinels exist (`__other__`, a custom endpoint). They are intercepted in `save()` and listed for select-search. | `panelView.ts:587-602`, `:709`; `SEARCH_FROM_OPTIONS = 15` (`selectSearch.ts:18`) | "+ New custom prompt…" is the **third** sentinel, with its own branch **and** an entry in the search list. With 13 presets the Prompt select reaches the search threshold. |
| `C#` and TS tests use `redteam-general` as an **always-run custom fixture**. | `SecurityLaneRoundTests.cs:49`, `:63`, `:200`, `:223-227`, `:261`; `SecurityCoverageTests.cs:15`, `:70`; `SecurityLaneSettingsTests.cs:57`; `SecurityLaneBoundaryTests.cs:133`; `SecurityEvidenceTests.cs:37`; `SecurityPresetTests.cs:13-17`; `SecurityProtocolTests.cs:17`; `securityLane.test.ts:67-79`, `:81`, `:91-93`; `helpPrompts.test.ts:72`; `scripts/seam-security.mjs:65` | Because general stays always-run, these fixtures keep their meaning, and S1 runs them unchanged to prove it. Three places change on purpose: the two catalogue tests (`SecurityPresetTests`, the TS seed and count tests) are rewritten to state the new rule, and `seam-security.mjs:65` moves its "custom prompt with an unknown trigger" leg to a custom id, because general is no longer custom. |
| The prompt cap is 32 **including the shipped presets**, in both the extension and the server. | `securityLane.ts:153-154`; `SecurityCatalog.cs:15`; `SecurityLaneSetting.cs:130`, `:159-160` | With 13 shipped presets, 19 custom prompts fit, not 20. A lane already at 12 + 20 overflows by one, and the server drops the last custom prompt and its pairs. |
| A palette already exists, with charts tokens and their fallbacks. | `roleTone.ts:64-73` | Same tokens; no second palette. |
| On the Roles page, green means **your own** prompt (`.prompt.mine`, rule at `rolesPage.ts:654`). A shipped one has a grey edge (`:649`), a `shipped` badge (`:380`) and Restore with no confirmation (`rolesPanel.ts:353-358`). | — | The operator's scheme inverts green's meaning between two tabs. Followed here as ruled; the operator ruled that Roles is not touched. |
| `.badge` is already a pill in the panel CSS. The `sec-*` prefix is the Settings **section** namespace (`.sec-prompts > summary`, …). | `panelView.ts:3142-3151`, `:2904-2908`; `panelSurface.ts:63`, `:119` | The new classes use the prefix `seclane-` and a scoped badge, so nothing existing is restyled. |
| The five **help articles** describe the tab ("tick the desired checks", "Edit local prompt override", "Twelve presets"). | `securityHelp.ts:7`; `helpRu/Uk/De/Es.ts` (`'security-lane'`) | All five change together. |

## Decisions (the operator may overrule any of them)

| # | Decision | Why |
|---|---|---|
| D1 | **General is an "always" prompt** (operator ruling). Its catalogue entry is `{ "id": "redteam-general", "always": true, "triggers": [], "focus": ["entry-point"] }`. "On" means it is paired with at least one reviewer by the existing checkboxes on its card, and then it is due on every change that has **readable code**. That means a path that is not prose (`.md`/`.markdown`/`.rst`) and a diff that is not withheld (binary, credential file). It is decided by paths, wherever the code lives (test, docs and research folders included), so a docs-only commit is a plain skip for it and never "incomplete coverage" (shipped in E1, after the own review found the first cut used the supporting-material hint). Its card has **no conditions block**, and its focus is the shipped one and is not editable. | The operator's reason: 15 lexical triggers never cover 100 % of the code. The cost is one more reviewer call on every round while it is on. That is the person's choice, and the card says so in one line ("runs on every change"). |
| D2 | **"Edited" means:** the override has *usable* text (by the server's rule above), OR the triggers/focus differ from the shipped ones as **sets**. For general, only the text counts, because it has no conditions. **Unknown members do not count.** **Storage is compacted on every write:** a shipped preset whose triggers and focus equal the shipped ones and that has no unknown members is left out of the stored `prompts`. | Then orange means *the person changed it*, and a future catalogue change reaches untouched presets instead of freezing them. Compaction is invisible on the wire (`securityEnv` sends the merged lane). If unknown members counted, a card could never turn green again, because Restore keeps them. |
| D3 | **Colour is never the only signal.** Each card carries a full-strength text badge: `default`, `edited`, `custom`. | WCAG 1.4.1. High-contrast themes flatten the charts tokens. |
| D4 | **Restore default asks first only when it would delete usable text.** The modal names the file. It also says if that file is open with unsaved changes; in that case Restore refuses, because saving the buffer would bring the file back. | Deleting prompt text is the only irreversible step here. Roles restores without asking because its text is on the page; this text is not. |
| D5 | **Remove pair carries the row's identity, not just its index.** The button's `data-id` is the JSON triple `[index, vendor, prompt]`. The queued write applies `run:<index>:remove` only when row *index* still holds that vendor (compared case-insensitively, as `uniqueRuns` and the server do) and that prompt, and refuses otherwise. | An index can shift between paint and click (another window, a removal above). No new field grammar is needed. Vendor ids come from user settings and are not proven free of `:` or `/`. |
| D6 | **A hand-added `redteam-general` from before this change** (operator ruling: a visible warning plus a one-click fix). With `triggers: []` or no `triggers` member, it already means "always", so nothing is shown. With **non-empty** stored triggers (a stored focus is replaced silently, because every hand-added general was stored with `focus: []`), its card shows *"General runs on every change; the conditions stored for it are ignored"* and a **Clear stored conditions** button (`prompt:redteam-general:restore`). The server ignores those members on an always prompt and reports the same sentence once, as a complaint. It does not refuse. | The person sees why the stored value no longer matters and fixes it in one click. The lane never stops running general because of an old leftover. |
| D7 | **"Remove custom prompt" becomes a button, and the file stays on disk.** The confirmation names the file and says how many pairs go with the prompt. | The prompt text is the person's work. |
| D8 | **The collapsed block has a one-line summary**, e.g. `Conditions — runs on: SQL · Authorization; focus: SQL, Entry points`. When every trigger is ticked it says `runs on: any security signal (15)`. | Collapsed must not mean hidden, and a 15-label summary is unreadable. |
| D9 | **Order: shipped in catalogue order (general first), then custom in stored order.** The same order is used for the cards and the pair's Prompt options. | A rendering rule. It never rewrites storage on its own. |
| D10 | **Enabling the lane still auto-pairs `redteam-authz`, and general starts OFF.** **`addRun` picks the first unpaired prompt in D9 order but never an "always" prompt**: general is switched on only from its own card. | General costs a reviewer on every round, so it is never switched on without the person asking. The first draft let "Add pair" pair general second; the E1 cadence critique caught it, and E1 ships "never", pinned by a test. |
| D11 | **Release order: MCP first, then the extension.** Either order is safe (§F); MCP first means the server already knows `always` when the first extension draws the card. | A new extension with an old server sends general as a custom prompt with `triggers: []`, which an old server already runs every time. An old extension with a new server shows no general card. |

## The design

### A. The tag legend in two columns (item 1)

- `signalLabels()` returns a `<ul class="seclane-tags">` with one `<li><code>id</code> label</li>` per signal.
  `entry-point` keeps its "focus only" note.
- CSS: `.seclane-tags { columns: calc(240rem/13) 2; list-style: none; padding: 0 }` and
  `.seclane-tags li { break-inside: avoid }`. There is no media query: the column-width form drops to one column
  by itself, and it follows the Settings text-size control. A `rem` breakpoint would use the initial size.

### B. Prompt cards: bigger names, three states, a fixed order (items 2, 5)

- `<fieldset class="seclane-prompt seclane-prompt--{shipped|edited|custom}">`, with the legend
  `<legend><span class="seclane-name">redteam-authz</span> <span class="seclane-badge">default</span></legend>`.
- `.seclane-name { font-size: calc(16rem/13); font-weight: 600 }`.
- Borders: `2px solid`, with `var(--vscode-charts-green, #b5cea8)`, `var(--vscode-charts-purple, #c586c0)` and
  `var(--vscode-charts-orange, #ce9178)`, and `border-radius: 3px`. The badge is full opacity.
- Pure functions in `securityLaneState.ts`:
  - `promptState(prompt, seed, text) → 'shipped' | 'edited' | 'custom'`, per D2;
  - `promptsInOrder(lane)`, per D9;
  - `conditionsSummary(prompt)`, per D8;
  - `compactPrompts(lane)`, per D2.
- Text state comes from the host: `none | blank | placeholder | oversized | written`, by the server's rule.
- **Edited** card: shows **Restore default**. **Custom** card: shows **Remove custom prompt**. Every button is
  `type="button"`, including the existing Edit button, which lacks it (`securityLaneView.ts:85`).
- **Custom** card whose text is not `written`: a `.stale` line reads *"No usable prompt text — write it in
  \<path\>. Until then the lane reports this pair as unable to run."* The path is the one this window's data
  directory resolves to, the same one `editSecurityPrompt` opens. An id that `promptFile` refuses
  (`rolesPrompts.ts:48-51`) gets its own message instead of an empty path.
- **Shipped** card whose override is `placeholder` or `oversized`: the same `.stale` treatment, because the
  server excludes it rather than falling back.
- **Caps are visible text**, not a tooltip: `Pairs: 16 of 16` beside a disabled **+ Add pair**, and
  `Prompts: 32 of 32 (13 shipped)` beside a disabled **+ New custom prompt**. When the lane is over the
  prompt cap (an old 12 + 20 lane), the overflowing card says the server will drop it.

### C. Conditions: collapsed, two columns, and the page keeps your place (item 4)

- Inside each card, below the reviewer checkboxes and the Edit button, there is
  `<details class="seclane-conditions" data-seclane-open="<prompt id>">`. Its summary follows D8.
- The body is a grid, `grid-template-columns: repeat(auto-fit, minmax(calc(240rem/13), 1fr))`:
  - left: **Run when code matches** (15 checkboxes, one per line), with its "All trigger tags" field under it;
  - right: **Prioritize source** (16), with its "All focus tags" field under it.
- **The open state survives a reload through page-local state.** This is the precedent at
  `selectSearch.ts:102-119`:
  - On `toggle`, the page merge-writes `vscode.setState({ ...getState(), seclaneOpen: [...] })`.
  - On load, it reopens blocks by comparing each element's `dataset.seclaneOpen` with the stored list. It
    never builds a selector from a stored id (the house rule at `panelView.ts:783-787`).
  - The markup is always drawn closed, so the paint key never moves on a toggle. The host is not involved.
  - Setting `open` from script fires `toggle` again; the handler writes the same list, which is harmless.
- **Focus comes back.** Widen `FOCUS_ID`'s last group to accept `:` (`panelView.ts:402`). The serialised literal
  already escapes `<`, so this does not reopen the hole its comment guards (`:404-412`).
- **When a button disappears after its own action:**
  - after **Remove pair**, focus goes to the pair that took its place, or to **+ Add pair**;
  - after **Restore default**, to that card's Edit button;
  - after **Remove custom prompt**, to **+ New custom prompt**.

  The page records this target in the same `setState` record before it posts the command.
- The "has no triggers" warning stays **outside** the fold, at the top of the card.
- **General's card has no fold at all** (D1). In its place is one line: *"Runs on every change while a reviewer
  is ticked."* Plus the D6 warning and button when they apply.

### D. Buttons, and writing your own prompt (item 6)

A pure `securityCommandWrite(command, id) → { field, value } | refusal` maps every button to the existing write
grammar, so the old-server test and the unit tests can drive the buttons without a host. The commands are
exported as `SECURITY_COMMANDS` and spread into `PANEL_COMMANDS`, as `QCONSULT_COMMANDS` is
(`qconsultWrite.ts:115`, `panelView.ts:3310`).

| Command | Button | Does |
|---|---|---|
| `addSecurityRun` | **+ Add reviewer / prompt pair** | enqueue → `addRun` (D10). Disabled at the cap or when no pair is possible, with the visible reason. |
| `removeSecurityRun` | **Remove pair** (`data-id` = `[i, vendor, prompt]`) | enqueue → identity check (D5) → `run:<i>:remove`. |
| `newSecurityPrompt` | **+ New custom prompt** | See the steps below the table. |
| `restoreSecurityPrompt` | **Restore default** | `askPerson` modal only if the text is usable (D4), with the dirty-editor check. Then enqueue: **write the conditions first** (`prompt:<id>:restore` resets triggers/focus to the seed and keeps unknown members), **then delete the file**. A failed delete (Windows `EBUSY`) is reported, not swallowed. |
| `removeSecurityPrompt` | **Remove custom prompt** | `askPerson` modal naming the file that stays and the pairs that go (D7), then enqueue → `prompt:<id>:remove`. |

The steps for `newSecurityPrompt`:

1. Ask **outside** the queue with `askPerson(() => showInputBox(...))`. The box is prefilled with `redteam-`,
   and `validateInput = name => newPromptProblem(lane, name)`, a pure function. It checks:
   - the name matches `^redteam-[a-z0-9-]+$`;
   - it is at most 80 characters;
   - it is not already registered;
   - the cap.

   The prompt line says *"If \<file\> already exists, its text is kept"*, because a file left by D7 is reused.
2. Enqueue: re-read the setting, re-run `newPromptProblem` (the cap may have been reached meanwhile), and
   write `addPrompt`.
3. After the queued save, open the file **only if the prompt is registered in the re-read setting**. `save()`
   can fail (`panelProvider.ts:2080-2092`), and a workspace-level `coai.securityLane` can shadow the write, after
   which `editSecurityPrompt` returns silently (`securityPromptEditor.ts:9`). Otherwise, tell the person why it
   was not added.

**The pair's Prompt select:**
- It gains a last option, `+ New custom prompt…` (`__newSecurityPrompt__`).
- `select()` (`securityLaneView.ts:13-16`) is widened to take a trailing action option.
- The page script gets a third sentinel branch beside `__other__` (`panelView.ts:594-602`), contributed as
  script text by the security module. It snaps the select back and posts `newSecurityPrompt` with the row's
  `[i, vendor, prompt]`.
- The sentinel joins the `selectSearchScript` list (`panelView.ts:709`).
- After creation, the queued step re-points **that** row only if its identity still matches (D5).

**Other page changes:**
- The "Add prompt (redteam-name)" text box (`securityLaneView.ts:32`) is removed. The `addPrompt` field stays,
  because the host uses it.
- The Prompts help paragraph (`:29`) is rewritten into three sentences:
  1. Pair a prompt with reviewers by the checkboxes on its card.
  2. Conditions decide whether it runs.
  3. Your own prompt is made with **+ New custom prompt**, and its text lives in the file the card names.

### E. Knowing whether a prompt was edited, and staying current

- A new host module, `securityPromptFiles.ts`:
  - `textStates(dataDir, ids)` follows the server's predicate (at most 32 files, each read through `promptFile`).
  - `deleteOverride` handles deletion.
  - It also does the create-and-open for the new-prompt flow.
- `PanelState` gets an optional `securityPromptText` field, filled next to `qconsultPromptOverrides`
  (`panelProvider.ts:1122`).
- **Watcher:**
  - `watchHealth` (`consultantHealthPanel.ts:184-194`) is extracted into a shared `watchGlob(base, pattern, changed)`,
    and both callers use it.
  - The base is `<dataDir>/prompts` (created first) with a non-recursive `redteam-*.md`. It is not the data
    directory, which holds the SQLite database.
  - Repaints are debounced (500 ms; autosave fires about every second) and requested only while the Settings
    page is open.
  - The watcher is disposed with the provider.

### F. The general prompt joins the catalogue (item 5)

- `shared/security-lane.json`: `redteam-general` becomes the **first** entry, with the D1 shape (`"always": true`).
  `generate-security-lane.mjs` regenerates the TS copy, and the parity test at `securityLane.test.ts:88` keeps
  them honest.
- **C#, the new kind:**
  - `SecurityCatalog` reads `always` into an id set, `SecurityCatalog.IsAlways(id)`. *As shipped in E1:* the
    `SecurityPrompt` record is NOT widened, because always-ness is a catalogue fact looked up by id.
  - `SecuritySignals.Triggered` answers for an always prompt by "any readable code file" (D1).
  - *As shipped in E1:* `IsPresetWithoutTrigger` is unchanged. `AddPrompt` routes an always prompt to a separate
    `ReadAlwaysPrompt`, which never reaches it. `ReadAlwaysPrompt` keeps the catalogue's conditions, raises the D6
    complaint for stored triggers only, and still refuses unknown members.
  - `SecurityRoster` records an always prompt that is not due as a plain skip ("no code file in this committed
    change"), unless files lay beyond the detector cap.
  - Every method stays within cyclomatic complexity 4 (CA1502 is an error for these files).
- **TS, the new kind:**
  - `securityAlways(id)` answers from the catalogue. *As shipped in E1:* `SecurityPrompt` is NOT widened, and the
    seed is projected to `id`/`triggers`/`focus`, so `always` never reaches the wire.
  - `triggerWarning` (`securityLaneView.ts:75-76`) and the seeded-trigger check (`securityLane.test.ts:81`) exempt it.
  - `trigger:`/`focus:` writes on an always prompt are refused. The refusal is **by field prefix**, not by prompt
    kind: `prompt:<id>:restore` is exempt, because it is how D6's **Clear stored conditions** button removes a
    leftover (for general, the seed's conditions are empty).
- The fixtures that use general as an always-run prompt keep their meaning and run **unchanged**, which is
  itself a check. `SecurityPresetTests` and the TS seed/count tests are rewritten to state the new rule:
  twelve conditional presets plus one always prompt. `seam-security.mjs:65` moves its custom-prompt leg to a
  custom id.
- `generate-help-prompts.mjs` / `helpPrompts.ts:73`: general moves out of "Additional security prompts".
- **Mixed versions:**
  - **New extension, 0.41/0.42 server.** General is sent with `triggers: []` and, to an old server, an unknown
    member `always`. **This must be checked before S1 is cut:** the old server refuses *unknown run fields*
    (`SecurityLaneSetting.cs:258`). If it also refuses unknown *prompt* members, the extension leaves
    `always` off the wire, because the server reads it from its own catalogue anyway. Without `always`, an
    old server treats the entry as custom with empty triggers and runs it every time (`SecuritySignals.cs:92`),
    with its text from `ForOptional` (embedded via `CoaiMcp.csproj:47`). The seam leg against the released
    0.42 binary (`COAI_MCP_DLL`, `run-seam.mjs:35-38`) proves *no complaint*. **One live round against 0.42
    with general paired** proves it runs, and is recorded in the PR.
  - **Old extension, new server.** No general card appears. A hand-added general keeps running every time.
  - No `SECURITY_SINCE` bump is needed.

## Reuse (per `.agents/conventions/common/reuse-first.md`)

| Need | Found | Move |
|---|---|---|
| Keep UI state across a reload | `vscode.setState` in `selectSearch.ts:102-119`; harness `RunOptions.saved` | Same mechanism, own key. |
| Restore focus after a reload | `FOCUS_ID` / `focusLiteral` (`panelView.ts:402-416`) | Widen the pattern. |
| Ask the person without blocking writes | `askPerson` (`personWait.ts:35`), `customModel` | Same shape. |
| Watch prompt files | `watchHealth` (`consultantHealthPanel.ts:184-194`) | Extract `watchGlob`. |
| A select entry that runs an action | `__other__` branch and the search sentinels (`panelView.ts:594-602`, `:709`) | A third branch and one more sentinel. |
| Commands owned by a section | `QCONSULT_COMMANDS` spread (`qconsultWrite.ts:115`, `panelView.ts:3310`) | `SECURITY_COMMANDS`. |
| Override file path | `promptFile` (`rolesPrompts.ts:48`) | The only path builder. |
| Palette | `roleTone.ts:64-73` | Same tokens. |

**Neighbours named rather than rewritten:**
- Other tabs may also have checkboxes that act as one-shot actions — not touched, by the operator's ruling.
- The Roles page's opposite use of green, and its word `shipped` against this tab's `default` — not touched, by the operator's ruling.

## Epics — the gate's split (2026-10-04)

The plan round's operator command split the work into epics, with **one gate round per epic**: each epic is
its own branch, started from the previous epic's commit, with its own `review_plan` and `review_code`
(`baseRef` = the previous epic's commit). There is a cadence consultation before epics 1–3 and another before
epic 4. Each epic is one commit.

The command asked for the split to be made with Fable. Fable was at its monthly spend limit (since
2026-10-02), so the split and the stories were done on **Opus 5.5**. The stories below carry the model they
were meant for.

| Epic | Branch | Stories (from *Build order* below) | Model |
|---|---|---|---|
| **E1 — General is an "always" prompt** (server, catalogue, the new kind on both sides) | `feat/security-tab-e1` | E1.1 the C# `always` kind and its complaint · E1.2 the catalogue entry, the generated TS, the TS kind and refusal · E1.3 the seam leg against 0.42, `helpPrompts`, the catalogue tests | Fable (security-relevant) → run on Opus |
| **E2 — The model and the host** | `feat/security-tab-e2` | E2.1 the pure state functions (S2), including `securityCommandWrite` and `newPromptProblem` · E2.2 `securityPromptFiles.ts` and `watchGlob` · E2.3 `PanelState.securityPromptText` and compaction in the write path. *Moved to E3.3 by the E1 cadence critique:* the provider cases and the `SECURITY_COMMANDS` spread, so each command lands with its button. | Opus |
| **E3 — The view and the page script** | `feat/security-tab-e3` | E3.1 the module split, the legend and the cards (A, B) · E3.2 the fold, page-local open state, the `FOCUS_ID` fix and the harness (C) · E3.3 the buttons, their `SECURITY_COMMANDS` and provider cases, the sentinel, the caps and the general card (D) | Opus |
| **E4 — Words, pictures, documents, releases** | `feat/security-tab-e4` | E4.1 the help articles in five languages and the release note · E4.2 the `render-page.mjs` demo state and screenshots · E4.3 the docs, the deviation and boundary notes, and promotion of this plan | Opus |

The PR carries the plan commit and the four epic commits. The MCP release and then the extension release
(D11) follow its merge.

## Build order — the stories (grouped into epics above)

Invariant: **the wire shape of `coai.securityLane` does not change.** The new write paths (`prompt:<id>:restore`,
the identity check, compaction) are extension-side only. The seam leg stays green after every story.

- **S1 — The catalogue, and the fixtures that leaned on general.**
  1. RED: the rewritten `SecurityPresetTests`, and the TS seed test.
  2. Add the `always` kind (C#: catalogue record, `Triggered`, `IsPresetWithoutTrigger`, `ReadPrompts`; TS: type, warning, write refusal), and run the existing general fixtures unchanged — they must stay green.
  3. Edit the JSON and regenerate. Fix `SecurityProtocolTests`, `helpPrompts`, `seam-security.mjs:65` and
     `securityLane.test.ts:91-93` (13 → 14 entries, title reworded).
  4. Pin the C# complaint text.
- **S2 — Pure model functions (`securityLaneState.ts`).**
  - `promptState`, `promptsInOrder`, `conditionsSummary`, `compactPrompts`;
  - `newPromptProblem` and `securityCommandWrite`;
  - the `prompt:<id>:restore` field and the D10 `addRun` order.

  Table-driven, every function within cyclomatic complexity 4.
- **S3 — Host.**
  1. `securityPromptFiles.ts` and `watchGlob`.
  2. `SECURITY_COMMANDS` and their cases, each one: `askPerson` outside the queue, then the enqueued write.
  3. `PanelState.securityPromptText`.
  4. Compaction applied in the lane write path.
- **S4 — The view and the page script.**
  1. Split into `securityLaneView.ts` (page body, pairs), `securityPromptCard.ts` (one card),
     `securityLaneStyle.ts` (CSS) and `securityLaneScript.ts` (sentinel branch, open state, focus targets).
  2. Widen `FOCUS_ID`.
  3. Extend the harness so `selected()` answers `details[data-seclane-open]` and dispatches `toggle`.
     Without that, a page test of the fold finds nothing and passes vacuously.
- **S5 — Words, pictures, documents, releases.**
  - The five help articles, with no "twelve" or "tick" left. A release note on general's change of meaning (D6).
  - `render-page.mjs` takes a seeded demo state: one edited preset, one custom prompt with no text, two pairs,
    one block open. Before and after screenshots at 700 px and 1300 px.
  - The docs listed in the DoD.
  - Then the live round against 0.42, the **MCP release**, and the **extension release** (D11).

## Test plan

Tests come first. Each RED one is watched failing with the real symptom, and then the teeth check: revert the
fix and see it fail again. Page tests run the page (`runPanel` / `panelState`). No new behavioural assertion is
made over page source text (`.agents/PROJECT.md:100-110`).

| Story | Test | What it would see if the behaviour were deleted |
|---|---|---|
| S1 | `SecurityPresetTests.Twelve_conditional_presets_and_general_always_runs_first` | general absent, not first, or carrying triggers; a conditional preset without triggers |
| S1 | `Triggered` is true for general on a signal-free diff, and still false for an empty conditional preset (`An_empty_preset_trigger_list_never_becomes_unconditional` stays) | general skipped; or "always" leaking to the twelve |
| S1 | a stored general **with** triggers runs, and the lane reports the pinned D6 complaint once | general refused, or the leftover silently honoured |
| S1 | `SecurityLaneRoundTests` and the other general fixtures pass **unchanged** | the old always-run meaning broken |
| S1 | the TS seed starts with `redteam-general` with `always: true`; a `trigger:redteam-general:sql` write is refused | first id `redteam-authz`; conditions editable on general |
| S1 | `seam-security.mjs` against the 0.42 binary: a lane carrying general raises no complaint | an old server refusing the new extension's lane |
| S2 | D6: a stored general carrying `triggers: ["sql"]` is cleared by **one** `prompt:redteam-general:restore` write, while `trigger:redteam-general:sql` is still refused | the one-click fix refused by the always-prompt guard, leaving the warning permanent |
| S2 | `promptState`: shipped / edited (usable text) / edited (conditions) / custom; a reordered set stays shipped; **an unknown member alone stays shipped**; `blank` text stays shipped; `placeholder` text counts as a problem, not as edited | each wrong colour |
| S2 | `compactPrompts` leaves out a seed equal to shipped, keeps an edited one, and keeps a seed carrying an unknown member; the merged lane (wire) is identical before and after | the snapshot frozen, or an unknown member lost |
| S2 | `promptsInOrder` with stored custom first puts general first and custom last; the pair select uses the same order | general drawn last (`securityLane.ts:60`) |
| S2 | `prompt:<id>:restore` resets triggers/focus to the seed and keeps an unknown member | the member dropped (the property `securityLane.test.ts:67` protects) |
| S2 | `securityCommandWrite('removeSecurityRun', [1,'codex','redteam-sql'])` refuses when row 1 holds another pair, and accepts `CODEX` against `codex` | the wrong pair removed |
| S1 (E1) | sixteen `addRun` writes on a fresh lane never pair `redteam-general` | general switched on by Add pair |
| S2 | `newPromptProblem`: bad pattern, over 80 characters, duplicate, and the cap at **13 shipped + 19 custom** | an invalid id written; the cap off by one |
| S3 | `textStates` (temp dir): absent → `none`, whitespace → `blank`, placeholder → `placeholder`, 64 KiB + 1 → `oversized`, text → `written` | Edit alone turning a card orange; a placeholder shown as fine |
| S3 | `newSecurityPrompt`: success registers, creates (`wx`) and opens. When the cap is reached between ask and write, nothing is written and the person is told. When the save is shadowed, the editor is not opened. | a prompt opened that the lane does not have |
| S3 | `restoreSecurityPrompt`: modal when text is usable, none when blank; conditions are written before the delete; a failed delete is reported; a dirty editor refuses | text deleted without asking; a half restore reported as success |
| S3 | `removeSecurityPrompt`: file still on disk; the modal names the pairs that go | the person's text deleted |
| S3 | the watcher: a write to `prompts/redteam-x.md` requests one debounced render; a write elsewhere in the data directory does not | a stale orange/green after an edit |
| S3 | each new `data-command` is in `PANEL_COMMANDS` and handled (existing guard) | an unhandled button |
| S4 | **RED (existing defect):** tick a trigger, reload with the saved state, and **focus** is on the same checkbox | focus `null` because of `FOCUS_ID` |
| S4 | open a block, reload with `saved`, and the block is open; a fresh page has every block closed | the block snapping shut / opening by default |
| S4 | after Remove pair, focus is on the next pair or **+ Add pair** | focus lost at the top of the page |
| S4 | an edited card has its class, its badge and Restore; a shipped one has none of them; the "has no triggers" warning is outside `<details>` | wrong state drawn, or the warning hidden |
| S4 | a custom prompt with `none` shows the "No usable prompt text" line with the real path | a silent custom prompt |
| S4 | the sentinel: choosing it posts `newSecurityPrompt` with the row's triple and snaps the select back; typing in the select-search never filters it out | a written `__newSecurityPrompt__` value; the entry hidden by search |
| S4 | caps: at 16 pairs, **+ Add pair** is disabled and `Pairs: 16 of 16` is visible text | a dead button with no reason |
| S4 | an id carrying `<script>` stays escaped in the legend, summary, badge, `data-id` triple and the open-state compare (extends `securityLaneMalformed.test.ts`) | raw markup |
| S4 | the old-server tab: every new button, routed through `securityCommandWrite`, cannot enable the lane | a vacuous pass over `[data-setting]` only |
| S5 | `helpCoverage.test.ts` and the five `security-lane` articles | a stale translation |
| S5 | **layout, by eye:** `render-page.mjs settings:securityLane` before and after, at two widths. The PR names the images looked at. | — (the DOM shims have no layout) |

## What the review changed (2026-10-04, design critic and fact-checker, own agents)

| Finding | From | What changed |
|---|---|---|
| The "Add pair dedupe defect" was misdiagnosed. A second press unticks and posts `false`; the stale box exists only after a no-op add. | both | The defect is restated; the RED test is replaced by the cap/no-op tests. |
| A dialog inside `this.enqueue` would freeze every write and repaint. | design | `askPerson` outside the queue, re-validation inside it, the editor opened only if registered. |
| The stored snapshot turns untouched presets orange after a catalogue change. | design | D2 compaction on write; unknown members do not count. |
| Focus is lost on every repaint of this tab (`FOCUS_ID` rejects `:`). | design | A new RED test and the widened pattern; focus targets named for vanished buttons. |
| A simpler open-state mechanism already exists. | design | `vscode.setState`, replacing a host message and a script literal. |
| A stored general goes from always-run to refused or on-signal. | design | Overtaken by the operator ruling the same day: general is an "always" prompt, so an old hand-added general keeps its meaning; D6 now covers only a leftover with stored conditions (warning + one-click fix). D11 is MCP first, either order safe. |
| Five C# test files and two TS files used general as an always-run fixture. | both | With general "always", they keep their meaning and run unchanged as a check; only the catalogue tests and the seam leg change (S1). |
| The sentinel was missing from select-search; it is the third sentinel, not the second; the identity is needed on the pair select. | both | All three are in §D. |
| `addRun` would pair general second. | fact-check, then the E1 cadence critique | E1 ships "never": D10 states it, and a test pins it. |
| The seam proves only "no complaint". | both | §F reworded; a live round against 0.42 added. |
| `.badge` and `sec-*` are taken; a `rem` breakpoint ignores text size. | design | The `seclane-` prefix, a scoped badge, column-width CSS. |
| The text state did not match the server (placeholder, oversized). | design | Five states mirroring `SecurityRoster.cs:120-140`. |
| The DoD claimed `panelView.ts` barely changes. | design | The DoD lists the exact hooks. |
| E1 code round (coai, 4 reviewers): the card claimed "runs on every code change" while a leftover kept general conditional on an older server, and restore had no control. | coai | E1 ships D6's warning and **Clear stored conditions** button early (command `clearSecurityConditions`, a queued write); the condition-free card text shows only once nothing is stored. E3 restyles the button; it does not add it. |
| E2 plan round (coai): the TS text-state rule would be a second copy of the server's; the data directory's source was unstated; the watcher glob was a second literal; reads must not block. | coai | A shared `shared/security-prompt-text-vectors.json` that both halves answer; the server's predicate extracted to `SecurityPromptText`. The data directory is the provider's `dataDir`, the one the seam's fifth leg compares with the server. The glob is built in `rolesPrompts.ts`, beside `promptFile`, and tested against it. Reads are async, size first. |
| E2 as built, deviations from §E. | build | The watcher is based at the DATA DIRECTORY with the pattern `prompts/redteam-*.md`, as the health watcher is, because `prompts/` may not exist yet; this avoids creating a folder at activation. It reuses the house `WATCH_DEBOUNCE_MS` (175 ms), not a new 500 ms. `fileWatch.ts` joins the Sonar coverage exclusions, because it imports `vscode`. |
| E2 code round (coai, 4 reviewers) and an own reviewer alongside: two suites agreeing with one file is not a live check of two implementations; .NET and JavaScript differ on whitespace and on byte-order marks (UTF-16/32 files misread); the caps were retyped; files were read on every render; an unreadable file read as no file; a restore on a custom card or a duplicate stored id could do the wrong thing. | coai + own | The server gained `--security-prompt-text` and the seam a ninth leg comparing both readings of eighteen real files; it caught a double-BOM disagreement on its first run. The extension decodes as `ReadAllText` does and uses .NET's whitespace set; there are eighteen vectors. The caps live under `limits` in the shared catalogue. Files are read only while Settings is open, through an mtime/size cache. There is a new `unreadable` state. `securityCommandWrite` refuses restore, clear and remove where they mean nothing, and compaction never drops a shipped id stored twice. |
| E3 plan round (coai): Restore asked only over usable text, so an oversized prompt was deleted unasked; a failed delete left a half-restored state with no named retry; a pair that moved while the person typed a new name left the prompt silently unpaired; the dirty-editor check named no API. | coai | Restore asks whenever the file holds ANY content (`restoreSteps`) and is idempotent: conditions written only when they differ, the file deleted only when present, so a second press is the retry, and the failure message says so. A moved pair gets "Prompt created; the pair changed while you typed — pair it from its card". Unsaved edits are found through `workspace.textDocuments` (background tabs included). Rejected with reasons: "setState is async" (it is synchronous, and a repaint replaces the document). |
| E3 as built, deviations from §C and §D. | build | **Focus after a tick** comes back through page-local `setState`, not only through the widened `FOCUS_ID`: the change handler releases focus to the host, so the repaint after a tick carries no focus at all. `FOCUS_ID` is still widened, for the held repaint while typing in a fold's tag field. After **Remove pair**, focus always goes to **+ Add pair** (not "the next pair"). The **harness** decodes entities in `data-*` values as a DOM does. `--security-prompt-text` (E2) is named in `.agents/PROJECT.md`'s list of one-shot modes, which a test enforces; it landed in this commit because E2's gate session had closed. |
| E3 code round — two own reviewer agents (correctness/security; conventions/UX/tests) in place of the coai code round, which was out of quota again on 2026-10-04, on the operator's explicit instruction. No XSS or injection hole found. Found: the host flows (the only code that deletes a person's text) had no tests; Restore decided before the queue and did not re-check, and deleted the file even when the conditions write was shadowed; a stale focus note could steal focus later and restored focus was invisible to the host; an id `promptFile` refuses got a fake path and a dead Edit button; an unusable override had no Restore; disabled buttons looked pressable; two ordering assertions passed when the element was missing; the badge ran into the name for screen readers; a custom prompt with no conditions was summarised as never running (also seen in the epic-4 screenshot). | own | The flows moved to `securityFlows.ts` with every effect injected and 11 tests over fakes (teeth checked with a compiling break). Restore re-reads the file state and the dirty check inside the queue, always writes the conditions, and deletes only once they took. Focus notes carry a time and are honoured for 4 s; the restore runs a microtask later, after the shared wiring. The card uses `promptFileIn`; Restore shows for any override with content; `:disabled` styled; assertions check existence first; a space separates name and badge; custom summaries say "runs on every change". |
| Wrong cites: `settingsScript` (`settingsPage.ts:70`), `.prompt.mine` (`rolesPage.ts:654`), "45" files (it is 32). | fact-check | Fixed or removed. |

## Growth surfaces

None. The open-block list lives in the webview's own state (at most 32 ids), and there is one watcher over at
most 32 files. No table, log or cache grows.

## Questions for the operator

Answered on 2026-10-04 and folded into the rulings table and D1/D6:
- general is on/off and runs on every change;
- Roles and other tabs are not touched;
- an old general gets a warning plus a one-click fix.

Open, and the plan proceeds on the answer in brackets:

- **Q2.** Should enabling the lane auto-pair general instead of authz? *(No, D10. General starts off, because it
  costs a reviewer on every round.)*

## Boundaries

| Item | Built by | Order |
|---|---|---|
| General becomes a shipped "always" prompt, replacing "custom, outside the twelve" | this plan; deviation note in [PLAN_a_security_lane_runs_beside_the_gate.md](../research/PLAN_a_security_lane_runs_beside_the_gate.md) | this change |
| Calibration campaign over the presets | [PLAN_security_lane_calibration_tail.md](PLAN_security_lane_calibration_tail.md). A thirteenth preset is one more row its campaign *may* pair. No prompt text changes here. | independent; a one-line boundary note there in this change |
| Tab semantics | [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) | independent |
| `panelView.ts` / `panelProvider.ts` size | [PLAN_two_files_outgrew_the_rule.md](PLAN_two_files_outgrew_the_rule.md), [PLAN_the_panel_provider_is_too_big.md](PLAN_the_panel_provider_is_too_big.md) | this plan adds only the hooks listed in the DoD |

## Definition of Done

- [ ] Items 1, 2, 4, 5 and 6 behave as described. Item 3 is untouched.
- [ ] Every test in the table exists. Each RED one was watched failing with the real symptom, and the teeth check was run.
- [ ] No new behavioural assertion over page source text (`.agents/PROJECT.md:100-110`).
- [ ] Every new function is within cyclomatic complexity 4.
- [ ] `panelView.ts` changes only at:
  - the `FOCUS_ID` pattern;
  - the `SECURITY_COMMANDS` spread;
  - the `selectSearchScript` sentinel list;
  - one script import and one CSS import;
  - the `PanelState.securityPromptText` field;
  - the section registration line.
- [ ] `panelProvider.ts` changes only at the command cases, the `PanelState` fill, and the watcher's construction and disposal.
- [ ] All green:
  - `npm run compile`, `npm test`, `npm run lint`, `npm run test:host`;
  - `npm run test:seam`, against 0.42.0 via `COAI_MCP_DLL` and against the new build;
  - the C# suite, run as its MTP executable and **never** `dotnet test` (`.agents/PROJECT.md:33-38`):
    `dotnet build dew_flow_connect_other_ais.slnx -c Debug`, then
    `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`.
- [ ] One live round against 0.42 with general paired, recorded in the PR.
- [ ] Screenshots before and after at two widths, looked at, and named in the PR.
- [ ] Help in all five languages updated. No "twelve" left in live text. A release note on general's change of meaning.
- [ ] Docs updated:
  - `research/module_security_lane.md` ("Prompts and settings", and the old "register general as custom" recipe at `:120`);
  - a short Security lane section in `research/module_extension.md`;
  - `research/security_prompt_catalog.md:30-33`;
  - the deviation note in `research/PLAN_a_security_lane_runs_beside_the_gate.md`;
  - the boundary line in `todo/PLAN_security_lane_calibration_tail.md`.
- [ ] `plan-lifecycle.mjs` and `pin-check.mjs` clean. This plan promoted to `research/` with its deviations when it ships.
- [ ] Review: a **coai code round** per epic, every finding resolved with a reason.
  - Own reviewer agents are not a standing alternative. They stood in once, on the operator's explicit
    instruction of 2026-10-03, when every coai provider was out of quota.
  - They stand in again only on a new explicit instruction from the operator. The evidence is quoted in the
    PR: the `providers` output, and the gate's round error (as on 2026-10-04, `qwen/PlanCritique: timeout`, round
    limit of 5 minutes).
- [ ] MCP release, then extension release (D11). The post-deploy check reads both versions.
