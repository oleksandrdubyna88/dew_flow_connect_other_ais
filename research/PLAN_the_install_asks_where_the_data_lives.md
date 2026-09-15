# PLAN — the install asks where the data lives, and the panel can move it there

> Status: **IMPLEMENTED, 2026-09-15.** The first install on a side asks where the data should live, a
> folder that already holds a history is adopted, the block copied to the clipboard carries the two
> variables, the panel can change the folder or move what is in it, and the move copies, verifies and
> then unlocks a separate delete. Extension 0.44.0. Still owed: the `coai` plan and code rounds, and
> one real run on a machine with a NAS — a tail, because every refusal and every count below is
> asserted, and what a live run adds is the one thing a test cannot have, which is a real SMB share.
>
> **What shipped differently from the plan below, and why:**
>
> 1. **`readSessions` was FIXED here rather than deferred.** The plan listed it as an independent
>    defect for its own document. It stopped being independent: it resolved the directory from
>    `process.env`, which a choice living in a SETTING is invisible to, so this feature would have made
>    it permanently wrong rather than merely wrong on partitioned installs. `panelProvider`'s storage
>    probes had the same defect and the same cure. A test now refuses any second answer to "where does
>    the data live" outside `dataDir.ts`.
> 2. **The extracted tail is one plan, not two**, for the same reason:
>    [PLAN_the_settings_file_ignores_the_side.md](../todo/PLAN_the_settings_file_ignores_the_side.md),
>    which is the server's half and needs a server release.
> 3. **`ALWAYS_PER_SIDE` turned out to need only the WRITE path**, not the read. The plan assumed both;
>    the two layers are read separately anyway — the overlay and the shared value — because the panel
>    says which one answered, so the reader needed no exception at all.
> 4. **The delete is command-palette only.** The plan implied a panel control gated on the move record,
>    which would have meant carrying that record through `PanelState`. The move's own success
>    notification names the command at the one moment it becomes relevant, which is where the
>    discoverability actually matters; a permanently visible *Delete…* is not obviously wanted.
> 5. **The guard that had to be amended taught something.** `wslNetwork.test.ts`'s call-graph guard
>    matched the whole file, so a docblock in another module that NAMED `writeWslconfig` — to say its
>    own call graph is guarded the same way — was reported as a new caller. Both guards now read code
>    rather than prose. That one stands between a refactor and a global networking file rewritten with
>    nobody's consent, so it was worth sharpening rather than working around.
> 6. **The inventory's RED run wrote its own evidence**: the failure named the twelve entries the
>    shipped list was losing. Nothing had to be argued.
> 7. **The gate's plan round found six more, and they were the best findings of the task.** The
>    re-paste gap (three reviewers, independently): a changed folder takes effect here at once while
>    the MCP client keeps its old entry, so the server writes to the old folder and a later delete
>    destroys something still being written to. The source re-read immediately before the delete,
>    which is the only defence available against a writer this extension can neither stop nor see. A
>    WAL sidecar warning rather than refusing, which had made the feature unreachable for every
>    installation that had ever been killed. Probing `<root>/<side>` rather than the picked root, and
>    confirming what was found before saving. A bounded probe. And a failed copy that says the
>    destination is partially written. Twelve of the eighteen findings were accepted; the six
>    rejected are recorded with their reasons in the session — four were factually wrong about what
>    had shipped, and two described work already done.
>
> Scope as built: `src_vs_code/src/` (`dataDir.ts`, `dataChoice.ts`, `dataMove.ts`, `dataCommands.ts`,
> `extension.ts`, `mcpBlock.ts`, `panelView.ts`, `panelProvider.ts`, `processLauncher.ts`,
> `versionProbe.ts`, `roundsDbRead.ts`, `settingsShape.ts`, `sideConfig.ts`), `package.json`,
> `shared/data-inventory.json`, the help content in five languages, and `sonarcloud.yml`.
>
> Related docs: [module_extension.md](module_extension.md),
> [module_server.md](module_server.md),
> [PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md),
> [PLAN_the_panel_shows_where_the_data_lives.md](PLAN_the_panel_shows_where_the_data_lives.md).
>
> This is the third and last part of issue [#115](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/115).
> The first shipped the resolution rule, the second made it visible; both left the acting half out on
> purpose. This is that half.

## The symptom

> *"After reinstalling the OS I want to go on writing to the database on the NAS."*

That is supported and there is no way to ask for it. `COAI_DATA_DIR` chooses a location and
`COAI_DATA_SIDE=<name>` partitions it per side ([dataDir.ts:21](../src_vs_code/src/dataDir.ts#L21),
`PanelSettings.ResolveDataDir` at [PanelSettings.cs:513](../src_mcp/src/Server/PanelSettings.cs#L513)),
and the panel now shows the resolved directory and hands over the `env` fragment
([panelView.ts:1208](../src_vs_code/src/panelView.ts#L1208)) — but **every way to use the feature is by
hand**: open an MCP client's config file, find the `coai` entry, add two keys, restart. Nothing asks,
at the one moment a person is already configuring this product and already has a paste on their
clipboard.

So a fresh machine starts a fresh history in `%LOCALAPPDATA%\coai-mcp`, silently, and the four years
of rounds on the NAS are reachable only by somebody who knows both variable names.

Second, smaller symptom: an installation that is **already** running in the default directory has no
way to relocate. Today it is told to stop the server and copy four entries by hand
([`movingHint`, panelView.ts:1279](../src_vs_code/src/panelView.ts#L1279)) — and that list is wrong,
see §5.

## Four structural facts this design is built on

Each was verified in the checkout; each kills an obvious alternative.

1. **The server's directory can only come from the client entry.** Its `settings.json` lives *inside*
   the directory the two variables select, so they are the two settings that can never live in it
   ([SettingsFile.cs:74](../src_mcp/src/Server/SettingsFile.cs#L74) resolves the data dir *before* any
   setting is read). The panel cannot tell a running server where to write.
2. **The install flow already hands over exactly the right vehicle.** `install()` puts
   `mcpServerBlock(target.fsPath)` on the clipboard ([extension.ts:798](../src_vs_code/src/extension.ts#L798))
   and `mcpServerBlock` already accepts an `env` argument
   ([mcpBlock.ts:60](../src_vs_code/src/mcpBlock.ts#L60)). A test forbids the panel from passing one
   ([install.test.ts:944](../src_vs_code/src/test/install.test.ts#L944)) **and its reason does not
   apply here**: the reason is that a pasted key freezes a setting that `settings.json` could
   otherwise change live — and these two keys can never be in `settings.json` at all (fact 1). They
   are the guard's one legitimate exception, and the guard must be amended to say so rather than
   worked around.
3. **The extension host resolves its own directory from `process.env` only**
   ([dataDir.ts:21](../src_vs_code/src/dataDir.ts#L21)), and a VS Code window has no such variable. If
   the choice is not persisted where the *host* reads it, then every person who uses this feature gets
   the panel reading `%LOCALAPPDATA%` while the server writes to the NAS — the exact "the two have come
   apart" state the storage section was built to diagnose. The feature would ship broken by
   construction.
4. **A chosen directory does not reach the `--log` child either.** The rounds list is read by spawning
   the server binary ([roundsDbRead.ts:59](../src_vs_code/src/roundsDbRead.ts#L59)), and
   `LaunchOptions` has no `env` ([processLauncher.ts:39](../src_vs_code/src/processLauncher.ts#L39)) —
   `spawn` passes none ([processLauncher.ts:113](../src_vs_code/src/processLauncher.ts#L113)), so the
   child inherits the host's environment, which is empty of both keys. **This is the blocker: until the
   launcher carries an env, a directory chosen anywhere but the host's environment is a directory the
   panel can never read.** It is step 1 of the build order for that reason, not step 6.

## What must be true when this is done

1. **The first install on a side asks.** When there is no install record for this side
   ([`installedKey(thisSide(storage))`, installer.ts:209](../src_vs_code/src/installer.ts#L209)), the
   flow asks whether to keep data in the default directory or choose one — before the block reaches the
   clipboard.
2. **Choosing a directory that already holds a history ADOPTS it.** This is the OS-reinstall case and
   the whole point: the flow reports what it found ("*1 284 rounds, 96 sessions — this side continues
   that history*") and continues. It must not refuse a non-empty directory. The *move* flow keeps the
   opposite rule (§6) and the plan says so in both places, because one sentence copied into the wrong
   flow destroys a history.
3. **The block that reaches the clipboard carries the choice.** One paste, the paste a person already
   makes once, and the server writes where they said.
4. **The panel reads the same directory**, with no environment variable set in the window, and says
   which layer answered — environment, this side's choice, the shared setting, or the default.
5. **The choice is per side and visible.** `Z:\coai` and `/mnt/z/coai` are the same NAS and not the
   same string; a Windows window and a WSL window must hold different values. It is editable in
   Settings, not only through the panel.
6. **An existing installation can move**, from the panel: pick a destination, refuse a non-empty one,
   copy, verify by reading the new location back, and only then offer to delete the old copy.
7. **What gets copied is complete.** Today's four-entry list loses a person's edited prompts, their
   whole spending history and the entire chat half of the product, silently (§5).
8. Nothing here writes into another program's config file. That decision
   ([mcpBlock.ts:2-4](../src_vs_code/src/mcpBlock.ts#L2-L4)) stands; this plan makes it unnecessary
   rather than reversing it.

## Design

### 1. Where the choice lives, and what wins

Two new settings, declared in `package.json` beside the others so they are discoverable and
documented:

| Setting | Type | Meaning |
|---|---|---|
| `coai.dataDirectory` | string, default `""` | The root. Empty means the platform default. |
| `coai.dataSide` | string, default `""` | The partition name inside it. Empty means "no partition". |

**Both are always per side**, independent of the `coai.perSideSettings` switch — a new
`ALWAYS_PER_SIDE` list in [settingsShape.ts](../src_vs_code/src/settingsShape.ts) honoured by
`readerFor` ([sideConfig.ts:23](../src_vs_code/src/sideConfig.ts#L23)) and `saveSetting`
([sideConfig.ts:71](../src_vs_code/src/sideConfig.ts#L71), whose gate at
[:77](../src_vs_code/src/sideConfig.ts#L77) is where the exception goes). Every existing entry of
`OVERLAID_SETTINGS` ([settingsShape.ts:461](../src_vs_code/src/settingsShape.ts#L461)) is a fact about
the *work*; these two are facts about a *side's filesystem*, where one shared string is not merely
inconvenient but wrong on at least one of the two sides.

Precedence, resolved by a pure function in `dataDir.ts`:

```
COAI_DATA_DIR in the host's environment   →  wins
this side's stored choice                 →  then
the shared setting value                  →  then
the platform default
```

Environment first because a window launched from a shell that exports it should agree with a server
launched from that same shell — and because it mirrors the server's own rule, where a variable beats
the settings file.

`coaiDataDir()` keeps its zero-argument signature (fifteen call sites, all `vscode`-free). Activation
installs the resolved choice once, through a setter beside the rule, before anything that reads it —
`extension.ts:225` constructs the chat store from it, so the setter runs first. The precedent for
module-level state with a stated lifetime is `reloadOffered`
([sideConfig.ts:119](../src_vs_code/src/sideConfig.ts#L119)).

`DataLocation` ([dataDir.ts:164](../src_vs_code/src/dataDir.ts#L164)) gains one field:
`source: 'environment' | 'this side' | 'shared setting' | 'default'`. The panel renders it. A person
whose panel and server disagree is then reading a sentence rather than deducing one.

### 2. The install question

In `install()` ([extension.ts:792](../src_vs_code/src/extension.ts#L792)), after the download succeeds
and before the clipboard write, and **only when this side has no install record**:

1. A `showQuickPick` of two: *"Keep it in the default folder — `<resolved default>`"* and
   *"Choose a folder… — a NAS or a drive that survives reinstalling the OS"*.
2. On the second, `vscode.window.showOpenDialog({ canSelectFolders: true, canSelectFiles: false,
   canSelectMany: false })`. This is the repository's first use of that API.
3. **Probe what is there, asynchronously.** Never `existsSync`: the use case is a NAS and a
   disconnected share blocks the extension host — the predecessor's finding, already honoured by
   `probePaths`/`reachable` in [panelProvider.ts](../src_vs_code/src/panelProvider.ts). Three outcomes:
   - a `coai.db` at the root → the pre-side layout; offer to adopt it with no side name;
   - subdirectories that hold a `coai.db` → offer them by name, plus "a new side";
   - nothing → say so plainly: this side starts with no history.
4. **The side name.** Offered with a default derived from this window (`windows`, the WSL distro
   lower-cased, otherwise the hostname), validated by the grammar that already exists on both sides
   (`isSafeSide`, [dataDir.ts:221](../src_vs_code/src/dataDir.ts#L221) and `PanelSettings.IsSafeSide`
   at [:441](../src_mcp/src/Server/PanelSettings.cs#L441)). Blank is allowed and means "only this
   installation uses this directory".
5. Save through `saveSetting`, then put the block on the clipboard.

The question is skipped entirely on a re-install or an update — it fires once per side, which is what
"first time on this PC or in WSL" means in code.

### 3. The block carries it

`mcpServerBlock(target.fsPath, storage.env)`, where `storage.env` is the field that already exists and
already holds exactly these two keys and no others
([dataDir.ts:159](../src_vs_code/src/dataDir.ts#L159)).

The guard at [install.test.ts:944](../src_vs_code/src/test/install.test.ts#L944) is amended rather than
deleted: a second argument is allowed **only** when it is that field, and a new unit test asserts the
field can never contain a third key. Its sibling at
[:917](../src_vs_code/src/test/install.test.ts#L917) is amended the same way and keeps its teeth: the
block carries a command, and at most the two keys that cannot live in a settings file.

`installedMessage` ([mcpBlock.ts:67](../src_vs_code/src/mcpBlock.ts#L67)) gains the directory, so the
notification says where the data will go.

### 4. The panel: change it later

`storageBlock` ([panelView.ts:1208](../src_vs_code/src/panelView.ts#L1208)) gains two controls —
*Change…* and, when the directory is not the default, *Move what is here…* — as two new entries in
`PANEL_COMMANDS` ([panelView.ts:2362](../src_vs_code/src/panelView.ts#L2362)), whose switch is checked
for exhaustiveness.

**`state.storage` must be added to `staticKey`** ([panelView.ts:2424](../src_vs_code/src/panelView.ts#L2424))
in the same change. It is absent today, which is harmless for a section of static text and fatal for
one with a button: the docstring above it records four separate controls that were frozen for the life
of the panel by exactly this omission.

In-flight state uses the existing `busy` shape — a map on the provider, rendered as a sentence, with
every button in the section `disabled` while it is set (`teamServerView.ts` is the model). A copy that
outlives the window is marked in `globalState` and reconciled at activation; there is no such sweep in
this extension today, so it is new concept and it is named here rather than discovered later.

### 5. The inventory, which is wrong today

`DATA_TO_MOVE` names four entries ([dataDir.ts:198](../src_vs_code/src/dataDir.ts#L198)). The server
and the extension between them write at least sixteen. Following the current on-screen instruction
therefore loses, with no error:

| Lost today | What it is |
|---|---|
| `prompts/` | the person's edited role and consultant prompts — the server silently falls back to the shipped text |
| `usage.jsonl` | the entire Spending tab |
| `chat-conversations/`, `chat-usage.jsonl`, `chat-doors.jsonl`, `pictures/` | the whole chat half of the product, including its money |
| `documents/`, `escalations/`, `consultations/`, `callers/` | the audit record |
| `settings.json` | the settings the server reads live |
| `coai.db-wal`, `coai.db-shm` | **committed rounds.** WAL is on (`PRAGMA journal_mode=WAL`), so an unclean stop leaves transactions in the sidecar; copying `coai.db` alone loses them and reports success |

`worktrees/`, `running/` and `servers/` stay behind — scratch, live pids, and sign-ins that belong to
the side that made them.

**The fix is not a longer literal.** A second hand-written list drifts exactly the way the first one
did, and the drift is silent. Add `shared/data-inventory.json` — one entry per path, with `move` /
`leave` and the reason — read by **both** suites, the way `shared/data-side-vectors.json` already is:
the TypeScript constants are asserted to match it, and a C# test asserts every path the server composes
under its data directory appears in it. A new persistent directory then fails a test on the day it is
written, which is the only mechanism that has ever kept a list like this honest here.

### 6. Move, verify, then offer the delete

Strictly ordered, and each step refuses rather than guessing:

1. **Refuse** if `whereData(...).refusal` is set, if the destination already holds a `coai.db`
   (a side's history is destroyed by copying over it — the *opposite* of the adopt rule in §2, and
   deliberately so), or if `coai.db-wal`/`coai.db-shm` exist or `running/` holds a live pid (something
   is writing).
2. **Fingerprint** the source: the SQL-counted totals from `readLog`, the session id set, the
   `usage.jsonl` line count, and a size map of every `move` entry.
3. **Copy**, driven by the shared inventory, under `withProgress`, with the whole wait covered.
4. **Verify** by re-reading the same four things *at the destination* — which is what step 1 of the
   build order makes possible at all.
5. **Only then** offer *Delete the old copy*, as a separate, separately-confirmed action, enabled only
   after a verification that matched. A true one-click move that deletes as it goes is out: it is the
   only variant where a half-failure loses history.

## Build order

1. **`LaunchOptions.env`** — additive, threaded through `capture` → `Run` → `readLog`/`readFindings`.
   Nothing else works without it (fact 4).
2. **The resolution rule** — precedence, `source`, the setter, `ALWAYS_PER_SIDE`, the two manifest
   settings. Pure and testable first.
3. **`shared/data-inventory.json`** and both suites reading it.
4. **The install question** and the block carrying the choice; the two guards amended.
5. **The panel section** — `staticKey`, the two commands, *Change…*.
6. **Move, verify, delete** — the provider half, on the pure predicates from steps 2 and 3.
7. **Docs**: `research/module_extension.md` (its "nothing here moves anything" section becomes the
   record of why it now does), `research/module_server.md`, `CHANGELOG.md`, and a new help article in
   **five** languages. While there: the `the-audit-log` article says logs live beside the binary in all
   five languages, and they moved under the data directory on 2026-09-06.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `processLauncher`: a launch given an env hands those variables to the child, and one given none changes nothing | the option does not exist |
| 2 | `readLog` asks the binary for the directory it was given, not the host's | the child reads the default |
| 3 | the environment beats a stored choice; a stored choice beats the shared setting; the shared setting beats the default — one test per step, each naming the layer in `source` | every layer resolves to the default |
| 4 | a stored choice is per side: two sides with one shared setting resolve to their own directories | both sides read one string, and one of them is a path that does not exist there |
| 5 | the block the installer copies carries the two keys and **only** those two | it carries none (and the amended guard must fail on a third key, asserted by adding one) |
| 6 | choosing a directory that already holds a `coai.db` adopts it and reports what was found | it is refused as non-empty, which is the feature inverted |
| 7 | the move refuses a destination that already holds a `coai.db`, and refuses while a WAL sidecar exists | it copies over a history, or copies a database missing its newest rounds |
| 8 | `DATA_TO_MOVE`/`DATA_TO_LEAVE` equal the shared inventory, and every path the server composes is in it | four of sixteen entries, and the C# side names paths the fixture has never heard of |
| 9 | `staticKey` changes when `storage` changes | the button never repaints — the bug the docstring records four times |
| 10 | the delete is refused until a verification has matched | it is offered beside the copy |
| 11 | every new command in `package.json` is named in a help article in five languages | `helpCoverage` goes red, which is the point |

Run: `cd src_vs_code && npm test` (and `npm run test:seam`, which drives the real binary across this
exact seam). C# side: the test project's executable, never `dotnet test`.

## Deliberately NOT in this plan

- **Writing an MCP client's config file.** The block carries the choice instead; the decision at
  [mcpBlock.ts:2-4](../src_vs_code/src/mcpBlock.ts#L2-L4) stands. There is also no comment-tolerant
  JSON writer in the dependency tree, and `.vscode/mcp.json` permits comments.
- **`SettingsFile.DataDirFrom` ignoring the side** ([SettingsFile.cs:74](../src_mcp/src/Server/SettingsFile.cs#L74)) —
  so on a partitioned install `settings.json` and `logs/` sit in the root while `coai.db` sits in
  `<root>/<side>`. It also does not trim, where `PanelSettings` does. A real defect, independent of
  this feature, needing a server release: its own plan.
- **`readSessions` resolving the directory a third way** ([extension.ts:997](../src_vs_code/src/extension.ts#L997)),
  ignoring `COAI_DATA_SIDE` entirely: its own plan, and a RED test first.
- A one-click move that deletes as it copies (§6).

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded in the pull request.
- [ ] `npm test` green with the count reported; `npm run test:seam` green; the C# suites green.
- [ ] A fresh side installs, is asked, adopts a directory that already holds a history, and the panel
      shows that history with **no** environment variable set in the window — stated as an observation,
      not an expectation.
- [ ] The two amended guards still fail when a third key is added to the block.
- [ ] `research/module_extension.md`, `research/module_server.md` and `CHANGELOG.md` updated; the new
      help article present in five languages and the stale `the-audit-log` article corrected in all
      five.
- [ ] The diff through the `coai` plan and code rounds, every finding resolved.
- [ ] Two follow-up plans filed in `todo/` for the deferred defects above.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date, and issue #115 closed.
