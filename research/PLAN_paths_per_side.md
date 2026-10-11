# PLAN — a path in the settings belongs to a side: a missing folder is marked, and two sides get two fields

> Status: **IMPLEMENTED, 2026-10-11.** All six epics shipped: E0 measured 2026-10-10
> ([RESULTS](RESULTS_side_registry_reaches_the_other_window.md)); E1 #740, E5 #752 and E2 #751 (2026-10-10; E2 and E5
> released in extension 0.68.0 / coai-mcp 0.47.0), E3 #760 and E4 #764 (2026-10-11, the next extension release). Each
> epic's deviations are recorded under it ("E1 as built" … "E4 as built"); the largest: the released extension DROPS
> `executablePathBySide` on any vendor save (measured in E3 — a newer window says so once and offers **Restore**), and
> with `coai.perSideSettings` on the other side's boxes are not drawn (E4's own review). Known limits left open are listed
> at the end of "E4 as built". The feature review (`review_feature`) was skipped: no vendor is ticked for it. Scope: the
> extension's path-bearing settings and the pages that draw them (`src_vs_code`: `pathFamily.ts`, `sideSettings.ts`/`sideConfig.ts`, `settingsShape.ts`,
> `vendors.ts`/`vendorsWire.ts`, `escalationDirs.ts`, `dataDir.ts`, the Settings page's Models, Question consultant,
> MCP server and This side places, `panelProvider.ts`'s notices), and coai-mcp's settings reader and `--providers`
> answer (`src_mcp`: `PanelSettings.cs`, `QuestionConsultSettings.cs`, `ServerJsonContext.cs`, `StartupNotices.cs`).
>
> **Builds on `fix/qconsult-roots-other-os`, merged as PR #733 on 2026-10-10** (record: `research/PLAN_qconsult_roots_of_the_other_os.md`
> on that branch, `src_vs_code/src/pathFamily.ts`, `shared/path-family-vectors.json`): a question-consultant root spelled
> for the other OS that does not exist here is skipped on this side, not refused. This plan does not redo that; it
> generalises its rule to every path setting and adds what the operator asked for next. Every `pathFamily.ts`,
> `qconsultHost.ts`/`qconsultView.ts`/`qconsultWrite.ts` and fix-branch `QuestionConsultSettings.cs` line below is cited **on that branch**;
> every other line is `origin/main` at `ba462264`.
>
> **Its four open questions were decided with the operator on 2026-10-09** — see *Decided with the operator* (D1–D4);
> D3 (a missing data directory is always red) went against the recommendation and reshaped E5.
>
> Related docs: [module_extension.md](module_extension.md) (*Settings a side keeps to itself*, *A Team-server
> sign-in belongs to a SIDE*), [module_server.md](module_server.md),
> [architecture.md](architecture.md), [PLAN_a_wsl_consultant_is_checked_from_windows.md](../todo/PLAN_a_wsl_consultant_is_checked_from_windows.md).

## The request (operator, 2026-10-09, after extension 0.65.0)

The operator runs the extension in a WSL remote window and in a plain Windows window on one machine. VS Code's user
settings are shared between them, so a path written on one side is read on the other — a question-consultant root
`/home/jinx/git` written from WSL raised a failure toast on every Windows start (fixed on the branch above). Now, in
their words:

1. *"A folder from the settings that does not exist on THIS OS"* (a Windows question-consultant root `D:\old-project`
   that was deleted; today a warning toast on every start): **BOTH** — keep the notice at every start **AND** mark it
   brightly and noticeably on the Settings page.
2. *"If the plugin detects that it runs on both Windows and WSL, two paths must appear (a Windows path, a WSL path)"* —
   every path setting gets a Windows field and a WSL field once both sides are known to be in use.

## The symptom, and the goal

**Today**

- A folder of THIS OS that is gone (`D:\old-project` on Windows) is refused by the server
  (`src_mcp/src/Server/QuestionConsult/QuestionConsultSettings.cs:232`, *"… is not a directory on this machine — a disk
  row needs a folder that exists"*), travels as one free-text sentence in `--providers`' `unrecognised`
  (`src_mcp/src/Server/ServerJsonContext.cs:57-70`, parsed at `src_vs_code/src/providers.ts:105-112`) and becomes ONE
  toast per window run (`notifyOnce`, `src_vs_code/src/notify.ts:266`) under the generic code
  `server-did-not-understand-a-setting` (`src_vs_code/src/panelProvider.ts:764-793`) whose cure — *"The server is
  running without those settings"* (`:791`) — is wrong for a list where only one folder was dropped, and which offers no
  way to the place that fixes it. **The Settings page shows nothing beside that folder**: the page asks the disk only
  about roots spelled for the OTHER OS (`existingHere`, `pathFamily.ts:57-75`, called from `qconsultHost.ts:74-93`), and
  `rootRefusal` (`qconsultWrite.ts:339-356`) checks shape, never existence.
- Every other path setting is either shared by both sides with no notion of a side, or kept per side with no way to see
  the other side's value. The census below has seven path fields in six settings, and every one can reach the wrong
  side today: `qconsultRoots` is fixed on the branch, `dataDirectory` breaks only through its shared fallback, and the
  four `executablePath`s and `alsoWatchDataDirectories` fail outright.
- Nothing records that this profile has been used from both Windows and WSL, so nothing can decide when "two fields"
  are wanted.

**When this is done**

1. A path of this OS that does not exist is **said at every start** (one notice per window run, per setting and path,
   naming the place and offering *Open in Settings*) **and marked on the Settings page** in the editor's error colour,
   with a count on the tab that holds it — the page and the server answering from one rule.
2. A path spelled for the other OS is never refused and never a toast on this side: it is the other side's, said
   quietly — for every path setting, not only question-consultant roots.
3. Once a Windows side and a WSL side have both been seen, every path setting shows a **Windows** field and a **WSL**
   field, the other side's one clearly labelled; with one side seen, one field, exactly as today.
4. Older extensions and older coai-mcp builds keep working on the values they already read — no worse than one shared
   value today; an older extension on the OTHER side still reads the one legacy value (see *What it cannot do — mixed
   versions*, design (c)).

## Research — every setting that holds a path

| Setting | Declared | Read | Written / drawn | Shared or per side today | On the wrong side today |
|---|---|---|---|---|---|
| `coai.qconsultRoots` (folders a disk row may read) | `package.json:1084` | `qconsultSettings.ts:88-91` → `COAI_QCONSULT_ROOTS` (`:288`); server `QuestionConsultSettings.cs` | Consultants › Question consultant, `qconsultView.ts:254-262` (fix branch) | Shared unless `perSideSettings` (it is in `QCONSULT_SETTINGS`, spread into `OVERLAID_SETTINGS`, `settingsShape.ts:331`) | **Fixed on the branch**: other-OS root skipped. A missing root of THIS OS: generic toast, no page mark (above). **Exported** by *Export settings* (`configTransfer.ts:29-40` does not list it), though it is a path on this machine — removed by E1.6 (D4) |
| `coai.vendors[].executablePath` (a reviewer's CLI) | `package.json:411-440` (defaults only; not in the item schema) | `vendors.ts:386`; to the server in `COAI_VENDORS` (`vendorsWire.ts:115`, `settingsShape.ts:457`); server `PanelSettings.cs:1270`, probed `PanelService.cs:268-285` | Models card field, `panelView.ts:1257-1262` via `modelCard.ts:225`; Setup › Vendor keys table, `setupTab.ts:51` | Shared unless `perSideSettings` (`vendors` is overlaid) | A Windows `C:\…\codex.cmd` reaches WSL: the probe answers `CliFound=false`, the card shows the red **cannot review** badge (`panelView.ts:1132-1149`) and every round on that side loses the vendor. No notice, no hint that the path is the other side's |
| `coai.consultants.*.executablePath`, `coai.qconsultRows[].executablePath`, `coai.chatModelPresets[].executablePath` | `package.json:985`, `:1068`, `:747` | `consultSettings.ts:622`, `qconsultSettings.ts:205`, `chatPresets.ts:201` → launch `chatPresets.ts:470` | Not drawn on the Settings page: the catalog migration moved these definitions into catalog rows (`catalogMigration.ts:207-208`, `:289`) | Consultants/qconsult rows overlaid with the switch; chat presets **never** (`sideConfig.ts:43-46`) | Legacy definitions only, but still launched from when unmigrated: the same failure as a vendor row, with no card to show it |
| `coai.alsoWatchDataDirectories` (another installation's data folder) | `package.json:602` | `escalationWatcher.ts:27`, rules `escalationDirs.ts:71-101` | Setup › MCP server, `panelView.ts:1838-1851` (`.stale` line) | **Always shared** (not overlaid; a raw `getConfiguration().get`) | Windows: a `/…` path is REFUSED (`escalationDirs.ts:84`, `POSIX_ON_WINDOWS`). WSL: a `\\wsl.localhost\…` path written from Windows is not refused at all — it is read as a relative name and fails as *"could not be read"*. Each side's correct value is the other side's wrong one; one list cannot hold both today without a refusal on one side |
| `coai.dataDirectory` (+ `coai.dataSide`, a name, not a path) | `package.json:597`, `:610` | `dataDir.ts:165-191` (`chooseStorage`: environment → this side → **shared** → default) | Setup › MCP server, *Where this window keeps its data* (`panelView.ts:1853ff`) | **Always per side** (`ALWAYS_PER_SIDE`, `settingsShape.ts:351`; writes go to the overlay, `sideConfig.ts:122-124`) | The SHARED value is still the fallback for a side that never chose (`dataDir.ts:172-176`): a hand-written or pre-#115 `Z:\coai` read in WSL resolves to a relative path under the host's cwd (`resolve`, `dataDir.ts:41`, `:285`). A missing directory is a quiet note *"is not there yet"* (`dataDir.ts:359-362`) — the server creates it |
| `COAI_ROUND_WORKTREES` | env only | `PanelSettings.cs:882` | — | Machine-local by construction (`PanelSettings.cs:345-359`) | Not a setting: the extension never writes it. **Out of scope** |

Checked and **not** paths: `securityLane` and `roles` (prompt bodies live by id in the data directory,
`package.json` descriptions), `teamServers` (URLs, `application` scope), `bugzServer` (a URL), `credsKey`, `dataSide`.
A future `coai.credsPath` ([PLAN_coai_finds_creds_where_it_is.md](../todo/PLAN_coai_finds_creds_where_it_is.md)) would be an
eighth path field and must join the registry E1.1 builds.

### What already exists for "sides"

- **Identity.** `Side`/`sideKey` (`coaiInstall.ts:289`, `:337`) folds `remoteName` + distro-or-hostname + storage path;
  `thisSide(storage)` (`installer.ts:63`) builds it; `sideLabel(remoteName, WSL_DISTRO_NAME)` (`coaiInstall.ts:386`)
  names it — **empty for a local window** (so the This side page says *"this machine"*, `panelView.ts:1466-1479`);
  `thisSideLabel` (`consultantSides.ts:32`) adds `Windows`/`Linux`/`macOS` for a local window.
- **Storage.** `globalState` is the CLIENT's database, one per profile, shared by local and remote windows alike — the
  repository measured this the hard way (`module_extension.md`, *A Team-server sign-in belongs to a SIDE*: a WSL install
  record overwritten by a Windows press). The side overlay lives there under `overlayKey(side)` (`coaiInstall.ts:353`),
  read by `readOverlay` (`sideSettings.ts:24-28`) and applied only when `coai.perSideSettings` is on
  (`sideSettings.ts:66-73`), except `ALWAYS_PER_SIDE`.
- **Records of sides.** `coai.installedVersion@<sideKey>` (`coaiInstall.ts:325`) is written only by an install;
  `coai.firstSeenControls` (`newTags.ts:11-14`) is the precedent for a bounded record stamped at activation and pruned.
  **No record says which sides this profile has been opened on.** `alsoWatchDataDirectories` and the consultant tab's
  `healthSides` (`consultantSides.ts:64`) know about another installation only when a person named its folder.
- **The rule for spellings** (fix branch): `isPosixAbsolute`/`isWindowsAbsolute`/`spelledForTheOtherOs`
  (`pathFamily.ts:19-35`), the decision `otherSideHere` (`:45-47`), presence in three states via `directoryAt`
  (`:88-102`), the system-drive `qualified` (`:111-115`), the cache `RootExistence` (`:127-151`), and the vectors both
  halves read (`shared/path-family-vectors.json`); the server's twins `QuestionRoots.OtherSide`/`OtherSideHere`/
  `PresenceOf`/`Qualified`.

### How a missing path is reported today, and where a page mark would come from

- **Server → extension**: only free text. `PanelSettings.Unrecognised` (`PanelSettings.cs:549`) flattens
  `UnrecognisedSetting(Key, Sentence)` (`StartupNotes.cs:14`) to sentences; `StartupNotices.Unrecognised`
  (`StartupNotices.cs:69-77`) writes each into the notification ledger as `stand-down`. The KEY is lost on the wire, so
  the page cannot tie a sentence to a field. `ProvidersAnswer` has taken additive fields before
  (`VaultKeyNames`, `ServerJsonContext.cs:82`) — the precedent for one more.
- **Page colours already in use for errors**: the Settings page's `--err` token
  (`var(--vscode-errorForeground)`, `catalogCss.ts:14`) drawn as `.skew` (3px `--err` left border, 8% `--err` tint,
  `catalogCss.ts:98-101`) and `.badge.health.err::before` (a `●` in `--err`, `catalogCss.ts:155`); the old panel's
  `.badge.cannot-run` uses `--vscode-inputValidation-error*` (`panelView.ts:3122-3127`). `.stale`
  (`panelView.ts:3166-3169`) — what qconsult refusals and unreadable watched folders use today — is deliberately the
  *"worth noticing, not alarming"* tone, which is exactly not what the operator asked for.

## Design

### (a) How both sides are detected

| Option | How | For | Against |
|---|---|---|---|
| **A. A side registry in `globalState`** — *recommended* | Each window stamps `coai.sideSeen@<sideKey>` = `{family, label, firstSeen, lastSeen}` at activation (at most once a day); `family` is `windows` for `process.platform === 'win32'`, else `posix`. Both are known when a `windows` and a `posix` record were seen within 90 days | `globalState` is the one store every window of the profile shares (measured, above); per-side keys never race (each window writes only its own); not synced by Settings Sync (no `setKeysForSync`), so another machine's sides never appear | Invisible in `settings.json`; enumeration needs `Memento.keys()`; whether a stamp written by one window reaches an already-open other window without a reload is **not measured** — E0 measures it |
| B. A record in the shared user settings (`coai.sidesSeen`) | Each side appends itself | Visible; raises a configuration event live in every window | Settings Sync carries it to other MACHINES (their sides appear here); two windows read-modify-write one array; clutters the file the person edits |
| C. A shared file | A marker in a folder both sides reach | — | There is no such folder: Windows reaches WSL through `\\wsl.localhost` (which wakes the distro), WSL reaches Windows through `/mnt/<drive>`; a NAS is optional |
| D. Inferred from the values | A path spelled for the other OS in the shared settings means the other side exists | Free, no record | A guess: a pasted path proves nothing; useless before the first path is written |
| E. A switch the person sets | "This machine is used from Windows and WSL" | Exact | Asks the person what the extension can see |

**Recommendation: A, with E as an override** — a setting `coai.pathFieldsPerSide` (`auto` default / `always` / `never`,
`application` scope so it is the person's, not a side's). Setup › This side lists every side seen with its label and
last-seen date and a **Forget** per side (a side gone for good stops forcing two fields). D is not used to decide; it is
used to *say* (E1's "the other side's" note), which needs no record.

Granularity — **decided with the operator (D1)**: two **families** only, `windows` and `posix`, labelled **Windows** and
**WSL / Linux**. A second WSL distro and an SSH remote use the same *WSL / Linux* field; a folder present in one distro
and not the other is marked missing in the other, which is the honest answer. Full per-side separation remains
`coai.perSideSettings`.

### (b) How a path setting stores a Windows and a WSL value

| Option | Verdict |
|---|---|
| **O1. Lists: the spelling IS the side.** `qconsultRoots` and `alsoWatchDataDirectories` stay one flat list; each entry belongs to the family it is spelled for, decided by the existing rule (`otherSideHere`: spelled for the other OS **and** no folder here — so Windows' root-relative `/work` that exists is Windows'). | **Recommended for lists.** No new storage, no migration, every existing list already IS two-valued; older extensions read the same list; coai-mcp ≥ the fix release already skips the other side's roots. Needs `alsoWatchDataDirectories` to stop REFUSING the other OS's spelling (E1.3). |
| **O2. Scalars inside a row: a sibling field.** A vendor row gains `executablePathBySide: { windows?: string, posix?: string }`; `executablePath` stays as it is. | **Recommended for `vendors[].executablePath`** — the only scalar path still drawn. Additive JSON inside an object whose item schema has no `additionalProperties: false` (`package.json:411-440`); an older extension ignores it; coai-mcp never sees it (below). |
| O3. Change the value's type to `{windows, posix}` | Rejected: an older extension — a WSL remote installs its own copy and can lag a version — reads `typeof !== 'string'` as empty and silently loses the path; a list of objects is filtered to nothing. |
| O4. Move the path keys into the side overlay (like `dataDirectory`) | Rejected for lists and rows: an overlay cannot hold one field of a row (`module_extension.md`, catalog E1 — *"it can do none of the three for a field buried inside an object"*); an overlay write raises no configuration event in the other window; invisible in `settings.json`. **Kept** for `dataDirectory`, which already works that way. |
| O5. One registry setting `coai.paths` keyed by `vendors/<id>/executablePath` | Rejected: orphans on every row rename or delete; two places to edit one row. |

`dataDirectory` keeps its storage: this side's overlay value, and the other side's read **read-only** from that side's
overlay in the same `globalState` (its `overlayKey`, found through the side registry). The legacy consultant /
question-row / chat-preset `executablePath` fields get the skip rule (E1.2) and no second field — they are not drawn
and the catalog migration retires them.

**coai-mcp never needs to know there are two values.** The extension resolves this side's value before it writes the
server's settings file (`COAI_VENDORS`, `vendorsWire.ts:115`), and the settings file lives in THIS side's data
directory. So every coai-mcp version — old and new — keeps reading one string. Lists are passed whole, as today, so the
server's own `disabled` (*"inactive on this side"*) keeps working; a server older than the fix release still refuses
an other-OS root, and the page says so through the existing skew line by capability (`--features`).

### (c) How each side picks its own value — one pure function

`pathForThisSide(stored, family)` in `pathFamily.ts`, answered by new rows of `shared/path-family-vectors.json` so the
C# reads the same table:

1. `executablePathBySide[family]`, when the key is **PRESENT** (a string) — an explicitly empty value is a PATH lookup and
   is final: it never falls through to the legacy value (amended by the cadence consultation for epics 4-6, below);
   unless an older extension has saved since (the `mirrored` check below). *Amended 2026-10-11 (the risk consultant
   after E3, finding c):* the value rule 1 picks is then judged by rules 2-3 exactly as the legacy value is — qualified
   on the system drive, skipped only when the disk says absent — so a save that pins a family's value into its member
   never moves what that family runs (see *E3 as built*);
2. else the legacy `executablePath`, unless it is spelled for the other OS;
3. else empty — a PATH lookup — **and** a finding `otherSide` naming the skipped value.

**Writes — the same rule in every mode** (amended, cadence consultation 4-6, finding a; the first version wrote only the
legacy value in one-field mode, so after `always → never`, a **Forget**, or the other side's 90 days running out, a save
of B still resolved to the older `BySide` A). Every save of a CLI-path field writes `executablePathBySide[<its family>]` —
in one-field mode the one field IS this side's — **and, when it is THIS side's field, mirrors the same value into the
legacy `executablePath`** (the plan round, finding 0) and records it as `executablePathBySide.mirrored`. Editing the
OTHER side's field writes only `BySide[<that family>]`. Hiding a field (one-field mode) never deletes the other side's
value. **Clearing** a field writes `""` (present, empty → PATH lookup), never deletes the key: deleting it would resurrect
the legacy value, and a legacy `//server/share/codex` is spelled for neither OS, so it would be run on both sides.
**An older extension's save is detected, not lost**: when the stored legacy value differs from `mirrored`, something
that does not know `BySide` saved since — the legacy value then wins for the family it is spelled for (and, spelled for
neither OS, for BOTH families — the save cannot say whose it was), until the next new-extension save rewrites both. So what a side resolves is always what was
last saved for it, whatever the mode was at either moment. The server applies rule 3 itself as defence in depth (an
older extension still sends the legacy value): an `ExecutablePath` spelled for the other OS is treated as empty, with an
Information log line — never a refusal.

**What it cannot do — mixed versions** (cadence consultation 4-6, finding b, verified: extension-v0.65.0's
`vendorsWire.ts:115` sends `executablePath: v.executablePath` unchanged). There is ONE shared legacy value, so two
older readers on two sides cannot each keep their own choice: an older WSL extension reads the Windows path a newer
Windows save mirrored, and sends it to its server — coai-mcp ≥ 0.46.0 skips it as the other side's (a PATH lookup), an
older server probes it and reports the CLI not found. That is exactly what one shared value does today, so nothing gets
worse, but it is not fixed for an older reader: the cure is updating the extension on that side. Said in the help and
in the CHANGELOG when E3 ships.

### (d) The page: two fields when both sides are known, one otherwise

- **One side known** (or `coai.pathFieldsPerSide = never`): every place looks as it does today, plus E1's quiet
  *"the other side's — skipped here"* note beside any value spelled for the other OS.
- **Both known** (or `always`): each path setting draws two labelled fields, **this side's first**:
  - *This side — Windows* / *This side — WSL / Linux (WSL: Ubuntu)* (`thisSideLabel` in brackets), editable, with
    **Browse…** where the setting has a picker (Add a folder for roots, a file pick for a CLI);
  - *The other side — WSL / Linux (last seen 2026-10-09)* in a bordered `fieldset` with a `legend`, a muted caption
    *"Used by the server on that side. This window does not check it."*, **editable as text only** for CLI paths and
    list roots (decided with the operator, D2) — no picker, since this host cannot browse that disk — refused on save
    when spelled for the wrong OS. **Never `stat`ed from here**: a `stat` of `\\wsl.localhost\…` starts a stopped
    distro, and `/mnt/<drive>` may not be mounted; the other side's own window marks it.
  - Lists draw two groups (*Folders on Windows* / *Folders on WSL / Linux*); Remove works in both; Add a folder adds to
    this side's group; the other group takes typed text.
  - `dataDirectory`: this side's choice as today; the other side's value **read-only** (D2), from its overlay, with
    *"Change it from a window on that side."*
- Accessibility: each field has a `label`; the other side's group is a `fieldset`/`legend`; buttons `type="button"`.

### (e) The missing-folder notice, and the bright mark — one rule, two surfaces

One model, `PathFinding { key, place, value, family, state }` with `state` ∈ `missing` (the disk SAID no — ENOENT,
ENOTDIR, a file), `unknown` (it could not tell — EACCES, EBUSY, EIO), `otherSide` — the same three states the fix
branch already gives roots (`directoryAt`, `pathFamily.ts:88-102`; `QuestionRoots.PresenceOf` in C#), extended from
"roots spelled for the other OS" to **every** path of THIS side, one `stat` at a time, cached per value list and
forgotten on a configuration change (the `RootExistence` pattern, `pathFamily.ts:127-151`).

**Which half says it** — never both:

| Setting | Truth for the gate | Notice raised by | Page mark from |
|---|---|---|---|
| `qconsultRoots` | coai-mcp (it refuses the root) | the extension, from the server's **structured** answer | the server's answer; the page's own `stat` only while the server has not answered or is older |
| `vendors[].executablePath` (this side's) | coai-mcp's probe (`CliFound`) | the extension's `stat` of an explicit path (a missing file is precise; *not on PATH* is the probe's) | the extension's `stat`, beside the existing **cannot review** badge |
| `alsoWatchDataDirectories` | the extension (it reads them) | the extension | the extension |
| `dataDirectory` | the extension (it reads and writes there before any server runs) | the extension — **always** when a NAMED directory is missing (D3; see below) | the extension |

**The server's structured answer.** `ProvidersAnswer` gains an additive `SettingProblems: [{key, kind, value}]`
(`kind`: `missing-folder` / `unreadable-folder`), filled from the refusals it already makes; `UnrecognisedSetting`
gains the kind. The extension then drops from the generic `server-did-not-understand-a-setting` toast every sentence
whose `(key, value)` it now reports itself, so one missing folder is one notice. An older server sends no field: the
generic toast stays as today, and the page marks from its own `stat`.

**The notice — at every start.** Code `setting-path-missing`, `as: 'warning'`, `class: 'failure'`, subject
`<key>|<value>` (one counter per path, so fixing one clears one), through `notifyOnce` — once per window run, so every
start of every window on the side where it is missing, and never on the other side. Title: *"The question consultant's
folder D:\old-project does not exist on this machine (Windows) — a disk row skips it."* Cure: *"Create the folder, or
remove it from the list."* Action: **Open in Settings**, which runs `coai.openSettings` with the place
(`catalogPlaces.ts:94-102`) and the value to highlight.

**The data directory — always red when a NAMED one is missing (decided with the operator, D3).** Not only when its
drive or root is missing: a data directory that does not exist gets the bright mark on Setup › MCP server AND the
notice at every start, like any other missing folder; today's quiet *"is not there yet"* note (`dataDir.ts:359-362`)
is replaced by it for that case. What counts, decided here so the first start is never red:

- **Only a directory somebody NAMED is checked** — `chooseStorage`'s `source` is `environment`, `this side` or
  `shared setting` (`dataDir.ts:165-191`). The **default location** (`source: 'default'`, `%LOCALAPPDATA%\coai-mcp` or
  `~/.local/share/coai-mcp`) is created lazily by whichever half writes first and is **never** checked, so a fresh
  install's first start — before anything has created it — raises no mark and no notice.
- **The named root is what must exist.** With `dataSide`, the side folder `<root>/<side>` inside an existing root is
  created lazily (the server's `ResolveDataDir`, the move flow's `createDirectory(landing)`, `dataCommands.ts:528`), so
  its absence keeps the quiet note; a missing ROOT is red.
- **The check runs before anything creates it, and nothing creates a missing named root.** Today the extension's own
  settings-file write creates the resolved directory unconditionally at activation (`writeSettingsFile` and
  `underSettingsLock`, `extension.ts:948`, `:992`), which would both hide the condition and — on WSL with a
  `/mnt/z/coai` whose drive is not mounted — write into the bare mount point. Those two keep creating the default
  location and a side folder inside an existing named root, and **refuse** a missing named root instead, reported as
  the finding: the server is not handed a new settings file until the folder exists, and the notice says so (*"…does
  not exist on this machine (WSL / Linux). Nothing is written there until it does — connect the drive, create the
  folder, or choose another in Settings."*).
- **A folder chosen through *Where this window keeps its data* exists from the moment it is chosen** — the choice flow
  (`askWhereDataLives`, `dataCommands.ts:48`) creates it, or refuses the choice when it cannot, so choosing a new folder
  never makes the next start red.
- The other side's data directory is never checked from here (D2: it is read-only and never `stat`ed).

**The mark — bright, and seen from any tab.**

- Beside the value: a new class `.path-missing` drawn exactly like `.skew` — `border-left: 3px solid var(--err)`,
  `background: color-mix(in srgb, var(--err) 8%, transparent)` — with a leading `●` in `var(--err)` (the
  `.badge.health.err::before` shape), the value in `<code>`, and the sentence *"does not exist on this machine
  (Windows)"*; an `unknown` value gets the warning token `--warn` and *"could not be checked"*. Old-page places use the
  same rule with the `--vscode-inputValidation-error*` tokens of `.badge.cannot-run`.
- On the tab strip (`tabStrip.ts:170`, drawn by `catalogPage.ts:31`/`:62`): a count badge `● 1` in `--err` on the top
  tab and the sub-tab holding a missing path, with an `aria-label` (*"1 missing folder"*), so the mark is seen without
  opening the tab.
- The field itself gets `aria-invalid="true"` and `aria-describedby` the sentence.
- Never colour alone: the word *missing* and the `●` carry it in a high-contrast theme.

## Epics and stories

Each story: RED first, the failure message recorded, then green, then break-it on the line that decides. Page tests
RUN the page through `src/test/panelPageHarness.ts` (`runPanel`) and `src/test/pageTree.ts` — never a regex over HTML.

### E0 — measure before building (no product code) — ✅ measured 2026-10-10

**Result** ([RESULTS_side_registry_reaches_the_other_window.md](RESULTS_side_registry_reaches_the_other_window.md)):
a stamp written in a WSL window was visible to an already-open Windows window at its next read (≤ 23 s), and the
reverse (≤ 20 s), without a reload; `Memento.keys()` is available in both hosts. "Both known" therefore flips live;
the E0.2 fallback stays a person's override (`coai.pathFieldsPerSide = always`), not a detection signal.

- ✅ **0.1** On the operator's machine, a `test:host`-style probe: a stamp written to `globalState` in a WSL window — is it
  visible to an already-open Windows window through `Memento.keys()`/`get`, and how soon (reload / focus / live)?
  Recorded in `research/RESULTS_side_registry_reaches_the_other_window.md`. It needs both kinds of window on the
  operator's machine, so it runs when the operator is present; **nothing in E1–E5 waits on it**.
- **0.2 The design does not depend on 0.1** (the plan round, finding 1). "Both known" is evaluated on every render and at
  every activation from `globalState`, which every window of the profile reads at start (measured, *What already exists
  for "sides"*); so in the worst case two fields appear at the next window start. If 0.1 shows a stamp does NOT reach
  another window even at its start (globalState not shared after all), the fallback is already in the design and becomes
  the default: `coai.pathFieldsPerSide` (`application` scope, shared by both windows) is set to `always` by the first
  window that sees a path spelled for the other OS in the shared settings (option D used to SWITCH, not to guess — a
  person can turn it back to `never`), and the This side page says which signal turned two fields on. Release
  requirement for E2: one of the two signals is proven by a `test:host` scenario.
- ✅ **0.3** The VS Code floor (`engines.vscode ^1.85.0`) has `Memento.keys()` — confirmed: `@types/vscode` ^1.85 declares
  `keys(): readonly string[]` on `Memento`, and both hosts answered it in the measurement.

### E1 — one rule for every path setting · `feat/paths-per-side-e1-one-rule`

- ✅ **1.1 The registry as code.** `pathSettings.ts`: every path setting, its field, its kind (`folder`, `file`,
  `folderList`), whether it must already exist, and who says it (the table in (e)). RED: a census test that walks
  `package.json`'s `contributes.configuration` and fails on a setting or nested field named `*Path`/`*Directory`/`*Roots`/
  `*Directories` missing from the registry (today: seven fields in six settings; `coai.credsPath` would be the eighth).
- ✅ **1.2 A CLI path of the other OS is skipped, not run.** `pathForThisSide` (rules 2–3 of (c)) in the extension's
  `COAI_VENDORS` writer and launch paths (`vendorsWire.ts:115`, `chatPresets.ts:470`, `claudeCli.ts:32-41`), and the
  same in coai-mcp's vendor read (`PanelSettings.cs:1270`). RED (TS): a Windows-side reader given
  `executablePath: '/usr/local/bin/codex'` sends it to the server today. RED (C#): the probe of such a row answers
  `CliFound=false`; after, it probes the PATH name and logs one Information line. Vectors in
  `shared/path-family-vectors.json` (`executable` set), read checked by both halves.
- ✅ **1.3 `alsoWatchDataDirectories` reads the other side's entries as the other side's.** On Windows a `/…` entry stops
  being REFUSED (`escalationDirs.ts:84`) and becomes *"the other side's"*; on WSL a `\\wsl.localhost\…` entry stops
  failing as *"could not be read"*. RED: `watchedDirs(own, ['\\\\wsl.localhost\\Ubuntu\\x'], 'linux')` returns it as
  usable today.
- ✅ **1.4 A shared `dataDirectory` of the other OS is not resolved.** `chooseStorage` skips a shared-layer value spelled
  for the other OS (falls to the default) and `whereData` says so. RED: a `linux` host with shared `Z:\coai` resolves to
  `<cwd>/Z:\coai` today (inject `resolve`/platform).
- ✅ **1.5** The page names every such value *"the other side's — skipped here"* in the quiet hint tone, through the one
  `otherSideNote` (`pathFamily.ts:154-158`), generalised by setting. Page test: the Models card of a row with a POSIX path
  on a Windows page shows the note, and no **cannot review** badge is blamed on it.
- ✅ **1.6 *Export settings* leaves `qconsultRoots` out** (decided with the operator, D4) — added to `NEVER_TRANSFERRED`
  (`configTransfer.ts:29-40`) as *"paths on this machine"*, like `alsoWatchDataDirectories`; an import that carries it
  (an older export) refuses that entry by name and keeps the importer's own roots. RED: `exportedSettings` of a profile
  with roots set contains `qconsultRoots` today; an import of a v2 file holding it overwrites the importer's roots today.

**E1 as built (2026-10-10) — deviations and what is left.** Every story above is done, RED first, with a break-it on
its deciding line; the observed messages are in the commits and in `research/module_tests.md`.

- **Seven fields in SEVEN settings**, not six: `qconsultRoots`, `vendors[].executablePath`,
  `consultants.*.executablePath`, `qconsultRows[].executablePath`, `chatModelPresets[].executablePath`,
  `alsoWatchDataDirectories`, `dataDirectory`. The census can only see what the manifest DECLARES: the
  `qconsultRows` and `chatModelPresets` item schemas do not declare `executablePath`, so those two are registered by
  hand and a new undeclared nested path field would not be caught (the census checks properties, items, map entries
  and the keys of default rows).
- **1.2 reached more sites than the plan cited.** Besides `vendorsWire.ts` (`rowOnTheWire`), `chatPresets.ts`
  (`chatRunSpec`) and `claudeCli.ts`: `settingsShape.ts` (a consultant definition on `COAI_CONSULTANTS`),
  `qconsultSettings.ts` (`COAI_QCONSULT_ROWS`), `vendorTerminal.ts` (`executableFor`, `executableForRuntime`, and a
  self-update decided on ITS platform), `chatLaunch.ts`, `cliChatLaunch.ts` and the agy model list in
  `panelProvider.ts`. A scan test (`aCliPathOfTheOtherSideIsSkipped.test.ts`, `UNRESOLVED_READS`) counts every raw
  read of a stored `executablePath`, so a launch site that skips the rule goes red. `pathForThisSide(stored, side)`
  implements rules 2-3 only; rule 1 (`executablePathBySide`) is E3's.
- **1.2 asks the disk, as the roots do (the gate's cadence consultant, codex, before E1's code round — verified, accepted).**
  The first build skipped a CLI path purely by its SPELLING, but on Windows `/Program Files/nodejs/node.exe` is a legal
  root-relative path that launches from the drive root, so a configured CLI written that way silently became a
  different PATH installation, or nothing. Now the roots' rule (`otherSideHere`) applies to a FILE: spelled for the
  other OS, the path is qualified with the SYSTEM drive (`qualified` / `QuestionRoots.Qualified`) and asked of the disk
  as a file — there, or the disk could not tell → this side's, run QUALIFIED; confirmed absent (none, a directory, an
  unspellable name) → the other side's, the PATH lookup. `ExecutableSide { family, systemDrive, fileAt }` is passed to
  every site (`hostSide.hostExecutableSide()`, a SYNCHRONOUS `stat` installed by activation — `useFileProbe(fileAt(statSync))`,
  the `useStorageSettings` pattern, because the Settings page's modules bundle without the host and may reach no
  `node:fs`; before it is installed every path is `unknown` and KEPT. The page is told which rows the host skipped
  (`PanelState.otherSideClis`, `otherSideClis(rows, side)`) rather than asking the disk itself. The wire writers are synchronous, and the only
  paths ever asked are this machine's own spellings of the other OS's: `C:\usr\…` on Windows, a relative name in WSL,
  never a share); `fileAt` maps a `stat` as `directoryAt` does. The server: `ExecutablePaths.Here(path, places,
  presence)` with `FilePresenceOf` (`QuestionRoots.PresenceOf(…, wantsDirectory: false)`, one exception mapping for
  both), applied once per row after the distinct. The `executable` vectors gained `systemDrive`, `existsHere` and
  `unknownHere` (an existing `/Program Files/nodejs/node.exe` → `C:\Program Files\nodejs\node.exe`, an unknown one kept,
  a POSIX-side Windows spelling that IS a file here kept); the seam leg gained a case on a Windows host: a COPY of the
  fake CLI on the system drive spelled without its drive letter, with the row's fallback (`COAI_EXE_SEAM_CLI`) pointed
  at a file that does not exist — the copy must answer. RED (TS): `actual '', expected 'C:\\Program Files\\nodejs\\node.exe'`;
  RED (C#): `Expected answer.Path to be "C:\Program Files\nodejs\node.exe" … but "" has a length of 0`. Break-it: the
  server rule made lexical again → the seam red (`the server did not run the existing /Users/…/FakeCli.exe … cliFound:
  false … not-the-fallback`); the extension skipping an UNKNOWN path → `skipped on a guess`. The card's note now says
  *"… and no CLI on this machine — the other side's CLI …"* (`asksDisk` true for the CLI entries).
- **1.2 on the server: `ParseVendors` only.** Every `ParseVendors` caller gets the skip (`COAI_VENDORS`, a
  consultant's or a question row's catalog `row`, `--check-model`); the legacy consultant / question-row
  `executablePath` fields OUTSIDE a `row` are not skipped server-side — the extension already writes this side's value
  for them, and only an older extension's file would still carry the other side's.
- **"No cannot review blamed on it" (1.5) — the research table overstated the symptom.** The card's badge
  (`cannotRun` → `availabilityOf`) reads `auth` only, and a CLI that is not found is reported `cliFound: false` with
  `auth: "own auth"`, so the red badge was not drawn for a missing CLI path — the probe's note said "was not found on
  this machine". The page test therefore asserts the quiet note, no `cannot-run` and no `stale` on that card; the seam
  leg (`seam-cli-path-other-side.mjs`) reads the row's `cliFound`, version and note from `--providers` instead of the
  badge. Whether a missing CLI should badge at all is E5's question (5.4), not answered here.
- **1.3:** `POSIX_ON_WINDOWS` (the refusal) is gone; its advice survives as `WSL_FROM_WINDOWS`, appended to the
  other-side sentence on Windows. Entries of the other side are de-duplicated like any other.
- **1.4:** only the SHARED layer is skipped — the environment and this side's own overlay choice are this side's by
  construction. `chooseStorage` gained a `family` parameter (default: the host's) instead of an injected `resolve`.
- **1.5:** `otherSideNote(windows, setting)` takes its words from the registry (the roots' sentence is byte-identical);
  `PanelState.hostFamily` (absent = the host's) and `PanelState.otherSideClis` (the host's decision) let a page test draw
  both sides on one machine.
- **Not in E1:** the server's structured `settingProblems`, the missing-path notice and mark (E5), the side registry
  (E2), the second field (E3/E4). The same cadence consultation raised two points about E5 itself; they are written into
  E5 below (5.0, and 5.1 / 5.3 / 5.7 amended), not built here.

### E5 — a missing path of this OS: a notice at every start and a bright mark · `feat/paths-per-side-e5-missing`

Built right after E1: it is the operator's item 1 and needs no side registry.

- ✅ **5.0 The storage-write policy comes FIRST** (the gate's cadence consultant before E1's code round — verified on
  `fix`+E1: `notify.ts` `write` → `recordNotification(coaiDataDir(), …)` → `jsonlLedger.appendLine` → `mkdir(dirname(path),
  { recursive: true })`, so the notice about a missing NAMED data directory would itself create it, and hide the very
  condition it reports). **Decided: a notice whose subject is the missing data directory itself is held IN MEMORY —
  the toast, and the notifications page's view of this window run — and written to no ledger**, rather than to the
  default location's ledger. Why: the default location is not where this window's notifications page reads (it reads
  `coaiDataDir()`, the named directory), so a ledger row there would be invisible AND would split the history across two
  folders; the notice is re-raised at every window start while the condition holds (D3), so persistence adds nothing a
  person needs; and writing nothing is the only choice that cannot touch a bare `/mnt/z` mount point. Once the folder
  exists, ledger writes resume as today. Stories: (a) `notify`'s writer refuses to write while `dataDirFinding` says the
  named root is missing and keeps the record in a bounded in-memory list the page shows (RED: the notice's own write
  creates the folder — injected `mkdir`). **Lifecycle and cap** (E1's plan round): at most ONE held notice per setting
  key — a new missing path for the same key replaces it, and it is cleared the moment the path changes or the folder
  is found again; the list is therefore bounded by the number of path settings (the registry, `pathSettings.ts`), never
  by how often a person retypes a path. Test: three successive missing values for `dataDirectory` leave one held notice;
  a recovery clears it; (b) **an inventory of every writer that creates the data directory, derived
  by a scan test**, each either guarded the same way or recorded as creating only the default location or a side folder
  inside an existing root. Found by search on 2026-10-10, to be re-derived by the scan: `jsonlLedger.appendLine` (through
  `notificationsFile`, `notificationsSeen`, `chatUsageFile`, `chatDoorsFile`), `chatStoreFile`, `claudeProbeCache`,
  `consultPromptFile`, `securityPromptEditor`, `teamServerAuth` (the token file), `configTransferCommands` (prompt
  texts), `dataCommands` (the choice and move flows), `extension.ts` (`writeSettingsFile` / `underSettingsLock`) and
  `roundsDbRead`.
- ✅ **5.1 The findings.** `pathFindings(registry, read, family, isDirectory/isFile)` → `PathFinding[]`, one `stat` at a
  time, cached, unknown never cached (the `RootExistence` contract). RED: a this-OS root `D:\old-project` that is ENOENT
  yields `missing` — today the page asks the disk about other-OS roots only. **Amended (the gate's cadence consultant,
  verified: `existingHere` awaits each `stat` in turn with no deadline, so one stalled share stops every later path,
  while the 60 s expiry of `RootExistence` lets a SECOND probe start beside the stalled one):** each probe has a
  **deadline** (a timeout answers `unknown`), there is **at most one outstanding probe per path** (a path still being
  asked is not asked again — the expiry starts no second one), and an `unknown` path is never counted as missing and
  never raises the notice. Tests, with an injected stat and clock: a never-settling share probe followed by a missing
  local folder — the local `missing` finding still publishes and the outstanding work stays bounded (one per path,
  however many renders and expiries pass); an `unavailable → present` recovery (the share comes back) clears the finding
  without any settings edit.
- ✅ **5.2 The server says which setting and which value.** `UnrecognisedSetting` gains a kind; `ProvidersAnswer` gains
  `SettingProblems` (additive, JSON name `settingProblems`); `providers.ts` parses it, an absent field reading as none.
  RED (C#): `--providers` with a missing root carries no `settingProblems`. Contract test (`test:contract`) on the
  field's shape.
- ✅ **5.3 The notice.** `setting-path-missing` per `(key, value)` through `notifyOnce`, with **Open in Settings**, worded
  *"not found now"* — never *"deleted"*: an unmounted drive or a sleeping share reports absence too (the cadence
  consultant); the
  generic toast stops repeating a sentence whose `(key, value)` the structured answer covers. RED: the operator's case
  today produces the generic code with the cure *"running without those settings"*; after, one
  `setting-path-missing` with the place, and no generic toast. Unit test on the dedupe; `notificationSites` ratchet
  updated.
- ✅ **5.4 The mark.** `.path-missing` (`--err`, the `.skew` shape, `●`) beside the value on Question consultant, Models
  (CLI path), MCP server (watched folders); `aria-invalid`. Page test, RUN through `runPanel`: given a finding, the row
  holding `D:\old-project` has class `path-missing`, contains the word *missing*, and the input/row has
  `aria-invalid="true"`; given none, no such class anywhere. Break-it: draw it with `.stale` — the test must go red on
  the class, not on the text.
- ✅ **5.5 The tab count.** `tabStrip` takes an optional mark per tab key; the Consultants tab and its Question consultant
  sub-tab show `● 1` with `aria-label`. Page test through `pageTree`: the count is on the tab, in DOM order after the
  label; zero findings draw no badge.
- ✅ **5.6 Page and server agree.** The page prefers the server's `settingProblems` for `qconsultRoots`; its own `stat`
  only fills in while the server has not answered or is older (capability via `--features`). Seam leg
  `scripts/seam-path-missing.mjs` in `test:seam`: the real coai-mcp, a settings file with one existing and one deleted
  root, the page's findings against `--providers`' `settingProblems` — the same `(key, value, kind)` set.
- ✅ **5.7 A named data directory that is missing is always red (D3).** `dataDirFinding(chosen, exists)` (pure, beside
  `whereData`): `missing` when a named ROOT is absent; nothing for `source: 'default'`; nothing (the quiet note) for an
  absent side folder inside an existing root. The activation order puts the check before the settings-file write, and
  `writeSettingsFile`/`underSettingsLock` stop creating a missing named root (they still create the default location
  and a side folder). The choice flow makes no folder — the system dialog returns one that exists; the MOVE flow makes
  `<destination>/<side>` inside the folder a person just picked, the one intended exception (the pick is the consent;
  recovery is making the folder again or choosing one again — item A below). REDs, each recorded:
  (1) unit — a named `Z:\coai` that does not exist yields no finding today, only the *"not there yet"* note;
  (2) unit, injected `createDirectory` — activation with a missing named root creates it today (the write that hides
  the condition); (3) page, RUN through `runPanel` — Setup › MCP server draws the missing root with `.path-missing` and
  *missing*, and a default location that does not exist yet draws **no** mark; (4) `test:host` — a fresh profile with no
  data directory anywhere starts with **no** `setting-path-missing` in the ledger (the first-start guarantee), and a
  profile whose overlay names a deleted folder gets exactly one per window start, and the folder is still absent after
  activation **and after the notice has settled** (its toast shown and its record handled — 5.0: the notice itself must
  not recreate it). Break-it: drop the `source` guard — (4)'s first half must go red; route the notice back through the
  ledger — (4)'s "still absent" must go red.

**E5 as built (2026-10-10) — deviations and what is left.** Every story above is done, RED first, with a break-it on
its deciding line that COMPILES — four first breaks that did not (an unused import, an unused parameter, an unused
field, a narrowed `&& false`) left stale output under `noEmitOnError` and a false pass, and were replaced; the observed
messages are in the commits and in `research/module_tests.md`. Record: `research/module_extension.md` *A missing path
of this OS*, `research/module_server.md` *`--providers` says which setting and which value*, `research/architecture.md`.

- **No `otherSide` finding state.** Design (e) listed three states; `missing` and `unknown` are E5's, and a value
  spelled for the other OS is never a finding — E1 already decided it (`otherSideHere`, `otherSideClis`,
  `WatchedDir.otherSide`, `skippedShared`) and says so on the page, so `pathFindings.probeable` does not ask about it; a
  bare CLI name and a relative root are not asked either.
- **5.0 held notices and lost records.** EVERY ledger write is refused under a missing named root — a refused record is
  `lost` and counted into the ledger's write gap (`recordNotification` answers `landed | held | lost`) — while the
  missing-path notice itself is `held` (one per setting key, the newest replacing the older) and shown on this
  window's Notifications page as `heldArrival` (ledger `extension`, at an offset no acknowledgement covers, its detail
  saying it was written to no ledger). The inventory test derives the writers from the source rather than from 5.0(b)'s
  list, which was complete except for `extension.ts`'s second site (the settings lock) — found by the scan; every
  guard goes through one `findingsBoard.refuseUnderMissingRoot`.
- **5.1** `PathSetting.place` became the Settings page's place id (`consultants/qconsult`, `models`, `setup/mcp`),
  worded by `catalogPlaces.placeLabel`, and the registry gained `wireKey` (the server's name for the value), which
  `agreed` uses to match the server's `settingProblems` to a setting. The probes are a class of their own
  (`pathProbes.ts`: a 5 s deadline, one outstanding probe per path, an unknown answer never fresh), `presence()`
  synchronous so a repaint never waits.
- **5.2** `test:contract` gained a contract against the REAL coai-mcp (`providersAnswer.contract.ts`); the binary
  finder left `run-seam.mjs` for `src/test/coaiMcpBinary.ts`, shared. Only a root that is missing or unreadable
  carries a kind and a value; a root refused for where it is stays a sentence alone. `QuestionRoots.Validate` answers
  `Refusals` (`RootRefusal(Sentence, Kind, Value)`), `Refused` kept as the sentence projection.
- **5.3 Open in Settings lands on the PLACE**, not on the value: `coai.openSettings(place)` opens the tab, and the mark
  is what finds the row. The door is `notify.notifyOnceThen` (once per run + a button) — a fourth once-guard at the
  call site being what the funnel exists to prevent; the census population is 164. The watch runs from activation
  (`pathWatchHost.startPathWatch`), not from the panel.
- **5.3, found by `test:host` and fixed in E5: only a SETTLED answer resolves a said path.** The first run in a real
  editor showed sixteen ledger rows for one CLI path: every change to a `coai` setting drops the disk's answers and
  asks again, the gap read as "no longer missing", the notice was resolved and said again when the answer landed — a
  toast per keystroke in the settings. `PathWatch.stillSaid` / `settled`: the disk saying it is there, the server
  taking it, or the value leaving the settings resolve; `pending` and `unknown` keep it said. A unit RED, and a host
  scenario (`missingPathSaidOnceScenario.ts`: a missing CLI path of this side, three settings changes, the ledger says
  it once) that is red with the rule broken. The first-start scenario moved first in the host list, because the
  revision scenario's `D:\old\claude.exe` is rightly said as missing after it.
- **5.0, found by the host's second launch and fixed in E5: a writer that can wait, waits.** The chat store's heartbeat
  (`window-<pid>.json`) is written at activation beside the probe, and the guard read the board before the disk had
  answered — the folder was created. `findingsBoard.refuseUnderMissingRootOnceSettled` waits for the first data-root
  answer (bounded by the settle ceiling); every async writer asks it (the inventory test holds them to it), the sync
  atomic write alone keeps the sync guard, and `dataRootKnown()` lets a render's spawn decide in the same turn once the
  answer is in. The consultants survey is a writer by proxy — the server's `--consultants` writes
  `consultations/health/consultants.json` with a `CreateDirectory` — and is not started while the named root is
  missing (said as an unanswered probe). Left: the person-initiated checks (`--check-consultant`, `--check-model`,
  `--check-security`) may still let the server create the folder, as the move flow creates the folder it is given.
- **5.5** the badge's words are *"N missing folders or paths"* (one: *"folder or path"*); a top tab counts every
  sub-place under it, a sub-tab its own.
- **5.6 the agreement is one-way, by setting**: the server's problems replace this window's own findings for the
  settings the server judges (`saidBy: 'server'`, the roots) once a server that lists `settingProblems` has answered;
  an older server's silence replaces nothing.
- **5.7** `whereData` keeps its quiet note only for a side folder inside an EXISTING root; `probePaths` probes the root
  itself; the mirror's new outcome `no-data-root` is terminal — not retried, not reported, because the notice already
  says it. The choice flow makes no folder (see the risk consultation below); the MOVE flow makes `<destination>/<side>`
  inside the folder a person just picked (`dataCommands`, recorded in the inventory as the folder a person chose) and
  was not changed. The host cannot observe the toast or count it (a held notice is written
  to no ledger, and the bundle's module state is out of a scenario's reach), so `test:host` asserts what a host CAN see —
  the ordinary launch's ledger holds no `setting-path-missing` after a first start with an existing named directory; a
  SECOND launch (`run-host.mjs`, `COAI_DATA_DIR` naming a folder that is not there) finds it still absent after
  activation, after the notice has settled and after a settings change, and `settings.json` landing within seconds
  once the folder is made — with NO settings edit since the risk consultation below. The once-per-run and the button are
  unit values (`pathWatch.test.ts`, `findingsBoard.test.ts`), RED (3)'s "default location draws no mark" is a unit value
  (`dataDirFinding(undefined, …)`), and the planned host break-its (the `source` guard, the notice routed back through
  the ledger) are held by the unit break-its of the same lines.
- **5.0/5.7, the gate's plan round (items A, B) and its risk consultation of 5.7 (items C–F), 2026-10-10** — each claim
  verified before any code changed, RED first, every break-it compiling:
  - **A — refuted as stated.** The plan round took the CHOICE flow (`askWhereDataLives`) to create the folder a person
    chooses. It does not: the system dialog returns a folder that exists, the flow reads it and saves the setting, and
    `<root>/<side>` is made lazily through the one maker. The flow that makes a folder is the MOVE (`moveDataDirectory`
    + `copyInventory`, `<destination>/<side>` inside the folder just picked) — the intended exception, the pick being the
    consent; a person whose named folder is gone recovers by making it again or choosing a folder again (the notice's
    cure already says so). Pinned by `theChoiceMakesNoFolder.test.ts` (a scan — the flows open dialogs).
  - **B — done.** The inventory (`test/dataDirWriters.ts`) now counts the one maker's calls and VS Code's
    `writeFile` / `rename` / `copy`, which make missing parent folders; every writer under the data directory must make
    folders through the maker alone; and `everyWriterLeavesAMissingRootAbsent.test.ts` RUNS each such writer with the
    root absent and the board knowing nothing (the settings file is run by the host; the import backup is held by the
    scan, needing an editor).
  - **C — verified.** `--bugs-json`, `--pairs-json`, `--real-method`, `--file-at`, `--tree-at` (`RoundsDb.Open`'s
    `CreateDirectory`) and `--providers` (the answer schema provisioned in a field initialiser) created a missing data
    directory; seven other readers did not. Readers now open only an existing database (`RoundsDb.IsThere` /
    `OpenExisting`) and the schema is provisioned on first need; every mode the extension launches is classified
    (`everyServerModeTheExtensionLaunches.test.ts`) and every reader run by `AReaderModeNeverMakesTheDataDirectoryTests`.
    `bugzLiveContract` seeds through a writer mode (`--requeue-refused`) since `--bugs-json` makes no database.
  - **D — verified.** After `forget()` the root read `pending`, and a probe past its deadline `unknown` — neither a
    finding — so a ledger append or the heartbeat created the folder and the mirror wrote. The board keeps its own
    state (`noteDataRoot`, `absentDataRoot`): an absence the disk SAID holds until a probe CONFIRMS the root.
  - **E — partly already true.** All layers unset → the default is made, and a side folder inside an existing root is
    made — both held before and after; an explicit value equal to the default path was made when the board had no
    finding — refused now by the maker.
  - **F — verified.** A root removed between the guard and the recursive `mkdir` was made again. `dataDirMaker.ts`
    makes one non-recursive level at a time below a named root and never the root; the settings file, the consultant
    prompt override and an escalation answer no longer go through VS Code's writes. Recovery without a settings edit:
    `PathWatch` rechecks absent paths every 5 s (`RECHECK_MS`), a confirmed root resolves the notice and tells
    `onDataRootBack`, where the mirror writes again; the host's second launch now proves it without touching a setting.
    The planned host break-it of the recheck is held by the unit break-its of the same lines.
- **Not in E5:** the side registry (E2), the second field (E3/E4). Whether a missing CLI should wear the *cannot
  review* badge was left as E1 left it: the mark under the box is E5's answer, the badge stays `auth`-only.

### E2 — the sides this profile has been used on · `feat/paths-per-side-e2-sides-seen`

- ✅ **2.1** `sidesSeen.ts` (pure): `stampSide(stored, side, family, label, now)` and `knownFamilies(records, now)`, with
  the 90-day window and pruning. RED: `knownFamilies` of a Windows and a WSL record within 90 days is `{windows, posix}`;
  a 91-day-old WSL record is pruned.
- ✅ **2.2** Stamped at activation, at most once a day per side (`extension.ts`), never awaited by activation.
- ✅ **2.3** `coai.pathFieldsPerSide` (`auto`/`always`/`never`, `application` scope), declared, described in the five help
  languages, never exported (`configTransfer.ts` `NEVER_TRANSFERRED`).
- ✅ **2.4** Setup › This side lists the sides seen (label, family, last seen) with **Forget** and the switch, and a local
  window names itself (`thisSideLabel`, not *"this machine"*). Page test: two seeded records draw two rows; Forget posts
  the side key. `test:host` scenario: activation leaves a `coai.sideSeen@…` record for the test window's side.

**E2 as built (2026-10-10) — deviations and what is left.** Every story above is done, RED first, with a compiling
break-it on its deciding line; the observed messages are in the commits and in `research/module_tests.md`.

- **The store half lives in `sidesSeen.ts` too**: `stampThisSide` (stamp if due, then prune — never awaited by
  `extension.ts`), `sidesIn` and `forgetSide` take a structural store (`keys`/`get`/`update`), which `context.globalState`
  is, so the module stays `vscode`-free and is run by `sidesSeenStore.test.ts` against a map. Pruning happens at the
  stamp, not at every read; a read simply leaves out what is past the window.
- **This side's identity is one pure function**, `sideHereOf(thisSide(storage), process.platform)` (`sideKey`,
  `familyOf`, `thisSideLabel`), called by activation and by the panel. A first build put it in a `vscode`-importing
  module of its own; `sonarExclusions.test.ts` refused a new module no test can run, and taking the `Side` as an argument
  removed the need.
- **The `test:host` scenario reads the registry through what `activate` returns** (`CoaiExports { sidesSeen }`): a host
  has no other way into another extension's `globalState`. The editor's user-data directory outlives a run, so a same-day
  stamp from an earlier run satisfies it as well — what it proves is that a window of this profile holds a fresh record
  for this side.
- **Forget is refused for this window's own record**, on the page (no button on that row) and in the host
  (`forgetSide`): it is in use now and would only be stamped again at the next start.
- **The decision E3/E4 will read is already here**: `twoPathFields(view)` — the switch, or under `auto` both families
  seen within 90 days, this window counting as seen now (so its own stamp landing late changes nothing). `familyLabel`
  (**Windows** / **WSL / Linux**) joined `pathFamily.ts`. Today only the page's sentence reads it.
- **The help** paragraph is in the `settings-and-the-server` article in all five languages, the control labels in
  English as everywhere; `helpCoverage.test.ts` asks each language for them.
- **Not in E2:** the second path field (E3 stores it, E4 draws it), so the switch and the list are shown and recorded
  but change no field yet.
- **A refused stamp is logged, never an unobserved rejection** (the coordinator's accepted finding on E2's code):
  `stampThisSide` never rejects; a store that refuses goes to a reporter that activation wires to `console.error` in the
  extension host's log (never a toast), and since a refused write leaves no record the next start stamps again.

**Cadence consultation 4-6 (2026-10-10, before E3's plan round — design only, no E3/E4 code).** Three claims, each
checked against the plan and the code before the plan was amended; all three held.

- **(a) Storage depended on field visibility — held.** Design (c) preferred a non-empty `BySide[family]` while one-field
  mode wrote only the legacy value, so after `always → never`, a Forget or the 90 days running out, a save of B still
  resolved to the older `BySide` A; and an emptied `BySide` fell through to a legacy `//server/share/codex`, which
  `spelledForTheOtherOs` calls neither side's. Amended: one write rule in every mode (every save writes
  `BySide[<its family>]`, this side's also mirrors into the legacy value and `mirrored`), a present key wins and `""`
  means a PATH lookup, and an older extension's save is detected through `mirrored`. E3.2 gained the save → resolve tests.
- **(b) Two older readers cannot each keep their choice — held** (`vendorsWire.ts:115` at `extension-v0.65.0` sends the
  legacy `executablePath` unchanged). Stated plainly in design (c), the goal's item 4 and risk 2; E3.5 gained the
  mixed-version test.
- **(c) Ambiguous spellings landed in this window's group on both sides — held** (`otherSideHere` is false on both
  platforms for `//server/share` and relative paths). Amended: three groups in E3.3/E4.2 (this side's, the other
  side's, both sides / unclear — never hidden), an admission rule for text typed into the other side's box, and the
  E3.3-stat vs E4-never-stat question decided (this host's disk at this machine's own spelling, never across).

### E3 — two values, stored and resolved · `feat/paths-per-side-e3-two-values`

- ✅ **3.1** `executablePathBySide` read and written (`vendors.ts:386`, `catalogEdit.ts`), carried by the catalog
  migration and by *Export settings* **without** its values (`configTransfer.ts:44`'s `MACHINE_PATH_FIELD` widened to
  both names). RED: a row with `executablePathBySide.posix` set still sends the legacy value to a WSL server today.
- ✅ **3.2** `pathForThisSide` rule 1 (a PRESENT key wins, empty included), with the write rules of (c), the same in every
  mode: every save writes `BySide[<its family>]`, this side's field also mirrors into the legacy value and `mirrored`.
  REDs: a two-field save of THIS side's field leaves the legacy value stale for an older reader; a save of the OTHER
  side's field must not touch the legacy value. **Save → resolve, across modes** (cadence consultation 4-6, finding a):
  `always` save A → `never` → save B → resolves B; `never` save B → `always` → this side's field shows and resolves B;
  two fields with the other side's value P → **Forget** the other side (or its 90 days run out) → save B → resolves B
  and P is still stored; **clearing** this side's field → resolves `""` (a PATH lookup) even with a legacy
  `//server/share/codex` stored, and the other side's value untouched; the RED for each is today's design resolving the
  older value or the legacy one.
- ✅ **3.3** Lists grouped through `otherSideHere` (O1) into **three** groups, never two (cadence consultation 4-6,
  finding c, verified: `spelledForTheOtherOs` is false on both platforms for `//server/share/x` and for a relative
  path, so a two-way split put them in THIS window's group on each side): **this side's**, **the other side's**, and
  **both sides / unclear** — an entry spelled for neither OS (`//server/share`), a relative entry (still refused
  visibly by `rootRefusal`, its refusal shown in that group), and an entry whose `stat` here answered `unknown`. Nothing
  is ever hidden. The group is THIS window's verdict, so the two windows may group a root-relative `/work` differently
  (Windows' where `C:\work` exists, WSL / Linux's in WSL) — each is right for the server on its own side. Unit: `/work`
  that exists on Windows groups as Windows'; `/home/jinx/git` groups as WSL / Linux's; `//nas/share/x` and `work` group as
  both / unclear on both platforms; an `unknown` stat groups as unclear, never as the other side's.
  **The `stat` here vs E4's "never stat the other side" — not a conflict, decided:** the grouping asks THIS host's disk
  about THIS machine's own spelling of an entry (`qualified`: a root-relative path on the system drive on Windows, the
  literal name in WSL) — the disk this side's server reads. It never reaches across to the other side's disk
  (`\\wsl.localhost\…`, `/mnt/<drive>`), which is what E4's rule forbids (it wakes a stopped distro, or an unmounted drive
  answers nothing).
- ✅ **3.4** `dataDirectory`: the other side's value read from its overlay via the registry, never written from here.
- ✅ **3.5** Compatibility pinned: a settings file written by an older extension (legacy only) resolves identically; a
  server settings file written by the new extension parses in the oldest supported coai-mcp (the existing
  `live-*-compat` pattern, `package.json` scripts). **Mixed versions** (cadence consultation 4-6, finding b): a row a
  newer Windows extension saved in two-field mode (`BySide.windows` W, `BySide.posix` P, legacy W) consumed by the
  extension-v0.65.0 wire logic (`vendorsWire.ts:115` at that tag, run as a fixture) sends W — the limitation, pinned and
  named in the test — and coai-mcp ≥ 0.46.0 skips W on a POSIX side; then that older extension's next save (legacy P′,
  `BySide` untouched) is resolved as P′ by a newer WSL reader through the `mirrored` check, and still as W by the newer
  Windows reader.

**E3 as built (2026-10-11) — deviations and what is left.** Every story above is done on
`feat/paths-per-side-e3-two-values` (4e171192, aa692fb0, f5076c0b, 7b5de9b1, 8525d7f5; help 76cdd9b1), each with a
compiling break-it on its deciding line; the observed messages are in the commits and in `research/module_tests.md`.

- **3.1 reaches every launch, not only the wire**: `executableOf(row, side)` is what the terminal, the Claude probe, the
  chat launches and the agy model list run, and a copy of a row into a definition takes `storedForThisSide`; a scan test
  counts one-value resolutions (`executableHere`) apart, so a row routed through it goes red. Export refuses the per-side
  member by name on import (`MACHINE_PATH_FIELDS`).
- **3.2's write rule lives in `cliPathSave.ts`** (`savedCliPath`, `vendorFieldWrite` — the panel's one vendor write), and
  it PINS every other family's value into its member when a save would otherwise move it (a family reading the legacy
  value the save replaces, or an older save winning for it) — a property the plan implied and a test now states for every
  stored state.
- **3.3 is `pathGroups.ts`**, and a drive-relative name (`C:work`) joins *both / unclear* with the relative ones.
- **3.4 is `otherSideValues.ts`**: one row per side of the other family (a second distro is a second row, D1 being about
  fields, not folders); the store is typed `SideReader` (no `update`), so "never written from here" holds by type.
  `overlayKeyOf(sideKey)` was split out of `overlayKey`.
- **3.5 — the older extension DROPS the member (measured, a correction to the mixed-version text above).** The released
  extension's vendor write is `vendorsFrom(read('vendors')).map(…)`, and its `vendorsFrom` builds every row from the
  fields it knows — so any save of any vendor field by extension-v0.67.0 or v0.65.0 removes `executablePathBySide` from
  every row (measured by `scripts/live-paths-per-side-compat.mjs` on both tags). "`BySide` untouched" happens only for a
  hand edit; that case behaves as written (the `mirrored` check, pinned). After an older save the newer reader on the
  saving side resolves P′, and the newer reader on the OTHER side has lost W: the legacy rule gives it the PATH lookup
  (P′ is spelled for the other OS). Not worse than the single shared value it replaces, and not PREVENTABLE from the
  newer side — said in the help (five languages), the CHANGELOG and `module_extension.md`. What the newer side CAN do
  is say it and offer the value back on a click: *The consultation after E3* below.
- **The mixed-version fixture is the released extension itself, not a copy of `vendorsWire.ts:115`**: the compat script
  builds the tag (`releasedHalves.mjs`) and runs its `vendorsFrom` / `vendorsEnv`. Against extension-v0.67.0 (E1's rule
  included) the older reader sends `""` for the other side's path rather than W; against v0.65.0 it sends W. Both are
  accepted as the pinned limitation; mcp-v0.46.0 and this checkout's server skip W, mcp-v0.45.1 / v0.40.5 probe it and say
  the CLI is not found (reported, not asserted).
- **"The oldest supported coai-mcp"** has no constant in the code; the script defaults to the newest published release
  and was run against mcp-v0.46.0, v0.45.1 and v0.40.5 — the file this build writes parses in all three and runs this
  side's value.
- **E3 × E5, settled in the merge of E5 (#752):** E5's watch keyed a CLI finding on the legacy `executablePath`, and the
  card found its mark by it. With a per-side value that is the wrong value — a Windows window whose WSL side saved last
  said nothing (`pathWatch.test.ts` RED: `actual: []`). The watch now names `storedForThisSide(row, family)`, the value
  the box draws, and the card keys its mark on the same (`theMissingPathIsMarked.test.ts`; break-it: the card keyed on
  `v.executablePath` → `the finding about this side's value did not mark its box`). `runtimeFields` takes the card's
  `CliFieldFacts` (`otherSideCli`, `missingCli`, `family`) instead of two more positional parameters.
- **The consultation after E3 (2026-10-11): a plan-gate reviewer (one Major finding) and a risk consultant (findings a,
  b, c; advice only — every claim verified here before acting).**
  - **Finding c — a save changed the OTHER family's working CLI: REAL, fixed.** `pinnedFor` decided by the SPELLING
    whether another family reads the legacy value, while `pathForThisSide` asks the DISK. Reproduced RED in
    `cliPathSaveResolve.test.ts`: legacy `/tools/codex.exe`, `C:\tools\codex.exe` present on the Windows disk —
    Windows resolved `C:\tools\codex.exe`; after a WSL save of `/opt/other/codex` it resolved `''` (*the save broke
    the Windows side's working CLI*), and `C:\opt\other\codex` when the disk could not tell. The second claim was
    real too: saving the UNCHANGED path on Windows wrote the member `/tools/codex.exe`, which the reader returned raw
    (`'/tools/codex.exe'` instead of `C:\tools\codex.exe`). The every-state property test, run over three disks
    (absent, here, unknown), found the same on the POSIX side (a Windows-spelled legacy value that is a file in WSL).
    **Fix:** the reader judges a member by the same rule as the legacy value (`pathForThisSide` =
    rules 2-3 over `storedForThisSide`), and `pinnedFor` pins a family's stored value whenever it has a member or the save
    REPLACES the legacy value it reads — no spelling and no disk asked (the save cannot ask the other side's disk, and
    need not: the pinned member resolves as the legacy value did). Break-its (both compile): the spelling condition back
    in `pinnedFor` → three red; the raw member back in the reader → four red.
  - **The Major finding and findings a/b — a member an older save dropped: no silent restore; SAID, with Restore.** The
    consultant advised against restoring from a shadow copy on its own: it cannot tell an older save from a deliberate
    removal, `freeVendorId` reuses ids, overlays can hold different rows with one id, and Settings Sync can replace the
    settings under a copy. Built, inside the existing modules' shapes:
    `cliPathKeep.ts` (pure decisions), `cliPathKeeper.ts` (the vscode-free keeper, the `PathWatch` shape: a said set,
    `resolved` when a condition ends, one queue), `cliPathKeepHost.ts` (the wiring; a look at activation and on every
    `coai` settings change). The copy lives in `globalState` (shared by the Windows and the WSL window of one profile,
    never synced — no `setKeysForSync`), ONE key per layer and row, `coai.cliPathKept@<layer>@[id,runtime,model]` (the
    `sidesSeen.ts` pattern: no read-modify-write of a shared value), holding the member as stored — `mirrored` and
    explicit empty values included. **Identity is id + runtime + model**: the id alone is reused by `freeVendorId`; a
    row whose runtime or model changed never inherits a copy, and a copy no row carries is deleted. Only the USER layer
    is kept: a side that keeps its own settings (`coai.perSideSettings`) reads rows only it writes. A newer reader that
    finds the member gone while a copy exists says once per run and per row (`notifyOnceThen`,
    `cli-path-value-dropped`, an `offer`): *"… no longer holds a previously saved value — Windows: …; WSL / Linux: …"* —
    never who removed it — with one button, **Restore**. Restore never runs on its own and re-reads the row AT THE CLICK:
    refused (`cli-path-restore-refused`, and offered again with the row as it is now) when the row is gone (never
    recreated), carries a member again, its legacy value changed since the offer, or the copy changed. It writes the copy
    back EXACTLY and leaves the legacy value alone — so after an older WSL save of P′, restoring leaves legacy P′, the
    `mirrored` check still lets P′ win for WSL, and Windows runs W again (calling `savedCliPath(…, W, 'windows',
    'windows')` would have mirrored W into the legacy value — the test says so). Tests: `cliPathKeep.test.ts`, RED
    first against stubs (*the drop is said once, not on every look* `0 !== 1`; the wording; *the copy is still there to
    restore*; the restore's `0 !== 1`; *saved for it since* not matched) — the never-guarantees (reused id, own
    settings, per layer) pass vacuously against a stub and were proved by break-its instead; seven compiling break-its
    each turned a named test red. Two notice sites: `PLACES_THIS_SPEAKS` 164 → 166.
  - **A page button for Restore is E4's** (the owner's rule: every extension action is a panel button). E3 changes no
    page, and the Models card's CLI-path field is redrawn by E4.1, so the button joins it there (E4.1 below); for E3 the
    notice's **Restore** is the action.
  - **Said to people:** the help's settings paragraph (five languages) and the CHANGELOG's *Unreleased* paragraph now say
    the notice and **Restore**; `helpCoverage.test.ts` asks every language for `**Restore**` (RED on `en`; break-it:
    the Ukrainian label un-bolded → red). Finding c is NOT in the CHANGELOG: it fixed E3's own unreleased save rule, a
    defect no release had (dropped when main's extension 0.68.0 was merged in).
  - `scripts/live-paths-per-side-compat.mjs` still shows the release DROPPING the member (that is the release's
    behaviour); that the drop is now SAID is pinned by `cliPathKeep.test.ts` over the same shape (a row without the
    member after a copy was kept) — the script was not extended (it drives the released halves, not this build's host).
- **The code round (2026-10-11): the gate (codex, 4 reviewers, `proceed`) and this repository's own reviewer, run at
  once.** The gate: the German help paragraph — rejected (the owner keeps help in five languages); a test fixture cast
  — accepted, `catalogEdit.test.ts` widens a row through `isRecord` (`withRowFields`), its older twin too; a chat preset
  converted from a row dropping the other side's path — rejected, verified false (`chatModelsOf` is a read-time view
  built on each side from that side's reader and family; nothing persists it); quadratic grouping — rejected (a
  person's handful of folders). **The own reviewer found two real defects the gate did not:**
  - **A question-row or consultant edit on one side took the other side's CLI path away, silently.** A definition is
    written back RESOLVED for its side (`storedForThisSide`), while `catalogLaunch.sameLaunch` compared the LEGACY value
    and `withLaunch` wrote it into the legacy field alone — `mirrored` fell behind, the write read as an older
    extension's save, and with a value spelled for neither OS (`""`, a bare name) the legacy value won on BOTH sides; the
    member stayed, so the keeper said nothing. RED (`catalogEdit.test.ts`, the Windows window's write passed as
    `editThroughCatalog(…, 'windows')` so the test is the same on every host): a Windows switch flip on a question row
    and a Windows model edit on a consultant each left WSL running `''` in place of `/home/u/.local/bin/codex`. **Fix:**
    `Launch.family` (absent = this host's) names whose value the definition carries; `sameLaunch` compares
    `storedForThisSide(row, family)`; `withLaunch` leaves the path untouched when that side already runs the value, and
    otherwise saves it as that side's field through `savedCliPath` (`vendors.cliPathFields`, exported, reads the stored
    row). A bare name typed on Windows now moves nothing on WSL. Break-its (each compiles): `sameLaunch` on the legacy
    value → *the Windows-resolved definition did not match its own row*; the rewrite writing the legacy value alone →
    *a bare name typed on Windows took the WSL side's CLI path away* (`posix: 'codex.cmd'`); the rewrite always saving
    → the per-side member lost (`{ windows: '', posix: '', mirrored: '' }`).
  - **Restore changed nothing after an older save of a value spelled for neither OS.** Such a legacy value (a cleared
    path, a bare name) reads as the older save on BOTH sides, so the exact copy left both sides running it and the
    notice went away. RED (`cliPathKeep.test.ts`): after an older save of `""`, both sides ran `''` after Restore.
    **Fix:** `restoredMember` sets the copy's `mirrored` to that legacy value in this one case, so both sides run their
    kept values again; the legacy value is still never touched (older readers keep the older save). A spelled legacy
    value keeps the exact restore (the older save's own side keeps it). Break-it: the case disabled → red.
  - Also from the own review: the reused-id limit stated where `keepPlan` overclaimed (a copy is deleted only when a
    window of this version looks while its row is gone), and design (c)'s sentence corrected — a legacy value spelled for
    neither OS wins for BOTH families. The literal `code:` beside `DROPPED` in `cliPathKeepHost.ts` stays: the
    notification-sites census reads the literal at each call site.
  - **Code round 2** (codex, 4 reviewers, `proceed`): the keeper's said set and the compat script's failure list are
    replaced instead of changed in place — accepted (no behaviour change). Rejected with reasons: the German help (as
    in round 1); two windows saving a row at the same moment (VS Code's configuration API has no compare-and-swap — the
    same last-writer-wins every `coai.vendors` write had before E3); a stale copy written by one host (the next look on
    either host rewrites it, and Restore names its values and re-reads the row at the click); one notice per dropped row
    (only rows that had two values are kept — a batch Restore is E4's page).
- **Not in E3:** any page change — the two fields (E4.1), the three folder groups (E4.2) and the other side's data
  directory (E4.3) are stored and decided here and drawn there; and the panel's **Restore** button (E4.1).

### E4 — the page draws two fields · `feat/paths-per-side-e4-two-fields`

- ✅ **4.1** The Models card: two CLI-path fields when both are known, this side's first, the other in a
  `fieldset`/`legend` with its caption; one field otherwise. Page test, RUN: with two families known the card holds two
  inputs whose labels name *Windows* and *WSL / Linux*; the other side's input has no Browse; with one family, one input.
  **And a Restore button** beside the field while a previously saved value is missing (`cliPathKeep.ts`'s
  `DroppedValue`, handed to the page by the host) — the panel button E3's notice duplicates; it posts to
  `CliPathKeeper.restore`, which re-reads the row at the click.
- ✅ **4.2** Question consultant: the three groups of 3.3 (this side's, the other side's, both sides / unclear — never
  hidden); Add a folder adds to this side's; the other group accepts typed text (the refusal shown beside the box, not
  as a toast). **Admission into the other side's box** (cadence consultation 4-6, finding c): accepted only when THIS
  window would group the typed text as the other side's — an unambiguous absolute path of that OS (a drive letter or
  `\\server\share` for the Windows box; a single-slash `/…` for the WSL / Linux box) — so an entry never jumps groups on
  the next render. Refused beside the box, with the reason: a spelling of this OS ("that is this side's — add it
  above"); a relative path (`rootRefusal`'s words); on Windows, a `/…` whose `C:\…` exists here ("on this machine that is
  C:\…, this side's folder"); in WSL, a root-relative `\…` (no drive). `//server/share/…` is accepted into the both /
  unclear group rather than either side's. Page test, RUN: each admission and each refusal, and the typed entry landing
  in the group it was typed into.
- ✅ **4.3** MCP server: watched folders in two groups; the data directory with the other side's value read-only (D2) —
  page test: the other side's data directory is text, not an input, and offers no save.
- ✅ **4.4** `coai.pathFieldsPerSide = never` restores one field everywhere; `always` draws two with a single side seen
  (the other labelled *"the other side (not seen yet)"*).
- ✅ **4.5** `test:host` scenario: seed a WSL side record, open Settings › Models in the real editor, see two CLI-path
  fields; type the WSL path, and the settings file this side writes for its server still carries this side's value.
- **Where an edit of the other side's value is saved** (E4's plan round, finding 0 — accepted). Nothing of the other side
  lives in an overlay except its data directory (while the sides SHARE their settings — with `coai.perSideSettings` on,
  the other side's boxes are not drawn at all; see *E4 as built*), so: a **CLI path** typed into the other side's box
  goes through the panel's one vendor write, `vendorFieldWrite`, with THAT side's family as the field — `savedCliPath(stored, value,
  <other family>, here)` writes only `executablePathBySide[<other family>]` into the shared `coai.vendors` (it never
  changes the legacy value; `mirrored` is set to that unchanged legacy value; this side's member pinned as E3 pins it);
  a **list root** typed into the other side's group is appended to the one shared list (`qconsultRoots` /
  `alsoWatchDataDirectories`, design (b) O1 — the spelling is the side); the other side's **data directory** has no input at all (D2). Page tests save and RE-RENDER: the other
  side's box shows what was typed, this side's box is unchanged, and `pathForThisSide` for each family resolves what its
  box shows. The plan round's minor finding — a WSL `/tmp/project` refused on Windows because `C:\tmp\project` exists
  there — was rejected: a list holds one string, which the Windows server would read as that existing folder.

**E4 as built (2026-10-11) — deviations and what is left.** Every story above is done on
`feat/paths-per-side-e4-two-fields` (e289e4ea, 72d37eeb, 1c42cc0f, 157db833, 2524af82; the docs 1e74bab4), RED first, each deciding line
broken by a compiling break-it that went red; the observed messages are in the commits and in `research/module_tests.md`.

- **One decision, one shape, every place** — `pathSides.ts`: `pathSidesOf(view, family)` over E2's `twoPathFields` names
  the two sides or answers one field; `otherSideFieldset` draws the other side's group (`fieldset.other-side`, the legend,
  the caption every control inside is described by). One field is drawn byte for byte as before at the card and the
  folders; Setup › MCP server gains only an empty line in its markup.
- **4.1** — the card's field moved out of `panelView.ts` into `cliPathFields.ts`; `runtimeFields` lost its unused `id`
  parameter. This side's box gains its label (with the CLI path's help mark) only in two-field mode. **No Browse on
  either box**: the card never had a file picker for the CLI path, and E4 adds none (design (d) said Browse "where the
  setting has a picker"). The other side's box posts `executablePath.<family>` and `vendorFieldWrite` reads the family
  from the key (`cliBoxFamily`) — the widening finding 0 asked for, no second write path. Its admission rule is
  lexical (`cliPathRefusal`): a path spelled for THIS side's OS is refused; a bare name, a relative path, a
  `//server/share` and an empty box are kept. The refusal is kept per window in memory (`typedRefusals.ts`) — feedback,
  not a status — and forgotten when the box is saved or the window reloads. **Restore** is drawn beside the field in
  BOTH modes (one field too: a dropped value is missing whatever the switch says); the keeper now keeps what its last
  look found (`dropped()`) and tells listeners when it changes (`onChanged`), and the panel gets it as
  `panel.cliPathKeep` (the `questionConsults` precedent) — the press runs the keeper's own `restore`.
- **4.2** — the three groups are always drawn in two-field mode, an empty one saying *None.*; the typed box submits on
  `change` (Enter, or leaving the box) like every text box of the page — there is no separate Add button. A typed
  `/…` whose `C:\…` the disk could not answer for is REFUSED (it would group as unclear, never as the other side's);
  `//server/share/…` still passes every root's own rules (a bare share root is refused as a drive root). The refusal
  reaches the provider through a new `QconsultWriteHooks.besideTheBox`.
- **4.3 — the watched folders are display only.** `coai.alsoWatchDataDirectories` has no editor on the page (before E4
  or now — it is edited in `settings.json`), so its groups only say whose each folder is, by spelling alone
  (`NOTHING_ASKED`, the rule `escalationDirs.ts` reads them by). Finding 0's "a list root typed into the other side's
  group is appended to … `alsoWatchDataDirectories`" is therefore NOT built: there is no typed box for that list.
  The other side's data directory is text, as planned; a side that chose none is listed as such.
- **4.4** — the behaviour landed with 4.1–4.3, so its test was written after it; its teeth were proved by reverting each
  deciding line (the switch ignored, the not-seen legend). The legend reads *The other side — WSL / Linux (not seen
  yet)*.
- **4.5 — the host scenario does not seed a record and types nothing.** A host can neither write the extension's
  `globalState` (the bundle's module state is out of reach, as E5 found) nor type into or read a webview, so the
  scenario switches two fields on with *Always two* (the same `twoPathFields` decision), opens Settings › Models, saves
  the row through `vendorFieldWrite` box by box, and asserts what a host CAN see: the `settings.json` the real mirror
  writes names this side's value while the settings hold the other side's in its member. The seeded record and the two
  drawn boxes are the page tests. In the same runs the three clipboard scenarios failed once and passed in the next run
  — the shared-clipboard interference `answerCopy`'s scenario names ("Run it alone"), not this change.
- **Help and CHANGELOG** — the help's "comes in a following release" sentence is replaced in five languages, and
  `helpCoverage.test.ts` asks every language for the page's labels; the CHANGELOG's *Unreleased* gains the paragraph.
- **CSS** — `fieldset.other-side` (`min-width: 0`, so a long path wraps) in `catalogCss.ts`; `CATALOG_CSS` re-pinned.
- **The code round (2026-10-11): the gate (codex, 4 reviewers, `proceed`) and this repository's own review, run at
  once.** The gate: the German (and other) help — rejected: the help is full translations in five languages, English
  only in quoted control labels, and the owner keeps all five; the host scenario bypassing the panel's save path —
  accepted, closed by `cliBoxWiring.test.ts`, a wiring scan of the provider's `vendor` case (the page test runs the
  box's message and the host scenario runs the mirror; neither reaches the case in between, which imports `vscode`).
  Its two compiling break-its went red: the refusal not returning → *a refused box falls through to the save*; the
  save going around `vendorFieldWrite` → *the vendor case saves a field around the one vendor write — the other side's
  box would reach the legacy value*. Restore published before the copy is durable — rejected, verified false: a row is
  dropped only when its copy is already stored (`keepPlan.lookAt`). Quadratic root checks — rejected: a handful of
  typed folders, and the probes are cached (`RootExistence`).
- **This repository's own review found two real defects the gate did not:**
  - **With `coai.perSideSettings` on, the other side's boxes saved into THIS side's own settings**, which the other
    side never reads, while the page showed them saved (`vendors` and the question consultant's keys are overlaid).
    RED: *with coai.perSideSettings on the card draws 2 CLI-path boxes* and *… the other side's group draws 1 typed
    box*; the host's refusal (`cliPathRefusal`'s new `ownSettings`) returned `''`; the wiring scans
    (`ownSettingsWiring.test.ts`) found neither the vendor case nor `QconsultHost.typedRoot` asking. **Fix:** in their
    place a read-only sentence, *This side keeps its own settings — set the other side's value from a window on that
    side.*; the host refuses those writes beside where the box was (never a toast) — `executablePath.<other family>` in
    the vendor case before saving, the typed root in `QconsultHost` before the disk is asked. The page and the host
    decide by one rule, `settingsShape.keptPerSide` (the `vscode`-free half of `writesTheOverlay`). One-field mode and
    the other side's data directory are unchanged. Seven compiling break-its, each red (the card and the folders ignoring
    the switch, the pure refusal ignoring it, the provider / the question consultant / the page asking the wrong key,
    `keptPerSide` ignoring the switch).
  - **A legacy-only row showed THIS side's path in the other side's box**: `C:\tools\codex.exe` in the WSL / Linux box
    of a Windows window — a value the WSL side skips (a PATH lookup) and the box's own rule would refuse if edited.
    RED: *the WSL / Linux box shows 'C:\tools\codex.exe'* and, from WSL, *the Windows box of a WSL window shows
    '/usr/bin/codex'*. **Fix**, by spelling only: when the other family has no member and the shared value is spelled
    for this window's OS (`pathFamily.sharedValueOfTheOtherOs`), the box is drawn empty with a quiet hint in
    `otherSideNote`'s words — *The shared value C:\tools\codex.exe is this side's CLI; that side skips it and looks
    the CLI up on PATH.*; otherwise it shows what is stored. Four compiling break-its, each red (the member ignored, the
    spelling judged for the wrong family, the value shown anyway, the hint on the wrong boxes).
  - Also: the hashes above named the pre-rebase commits; `sideHere`'s doc comment had been left above
    `refusedBesideTheBox`; the CHANGELOG now says a typed `//server/share/…` goes to *Both sides / unclear*; and the
    "never `mirrored`" sentence (here and in `research/module_extension.md`) now says what `savedCliPath` does — it never
    changes the legacy value, and `mirrored` is set to that unchanged legacy value. The help says the sentence in five
    languages (`helpCoverage.test.ts`; RED on `en`, break-it: the Ukrainian sentence un-italicised → red).
- **Known limits (not built):** a typed root equal to a stored one under this window's case-folding is dropped
  silently (`rootAdded` de-duplicates; no refusal is drawn); a typed other-side root is checked against THIS side's
  profile and system folders (`rootRefusal` has only this side's places); the vendor write has no key allow-list — any
  `data-setting` a page posts is written onto the row (pre-existing, not E4's).
- **Left open:** a typed box for the watched folders (above); a file picker for this side's CLI path (never existed).

## Build order

1. E0 (measure; no product code).
2. E1 (one rule, skip-not-refuse everywhere) — removes the remaining cross-side failures on its own.
3. E5 (the operator's item 1) — can ship as soon as E1 is merged; needs a coai-mcp release for 5.2/5.6 and an
   extension release for the rest, mcp first (the release order in `POST_DEPLOY.md`).
4. E2 → E3 → E4 (the operator's item 2), one pull request each; E4's page waits on E3's storage.

Each epic is one branch and one pull request, gated per epic.

## Test plan

- **Unit (TS, `npm test`)**: `pathFamily` (`pathForThisSide`, findings, the new vector sets), `pathSettings` census,
  `sidesSeen`, `escalationDirs`, `dataDir.chooseStorage` and `dataDirFinding`, `configTransfer` (`qconsultRoots` never
  exported), the notice dedupe.
- **Unit (C#, the MCP test executable)**: the vendor read's skip, `SettingProblems` from every refusal kind,
  `StartupNotices`, the vectors read checked (`PathFamilyVectorsTests`).
- **Page (RUN, `runPanel` + `pageTree`)**: the mark's class and `aria-invalid`, the tab count, two fields vs one, the
  other side's group without Browse, the This side list.
- **`test:host` (real editor, `src/test/host/scenarios.ts`)**: the side stamp at activation; two fields with a seeded
  other side; the notice with **Open in Settings** landing on the place; the data directory's first start (no
  notice for the default location) and a deleted named one (one notice per start, not re-created) — E5.7.
- **`test:seam` (real coai-mcp)**: `seam-path-missing.mjs` (5.6) and a leg for 1.2 — a Windows-spelled CLI path on a
  POSIX server is probed as a PATH name, page note and server answer agreeing.
- **Contract (`test:contract`)**: `settingProblems` shape.
- Full C# suite, `npm test`, eslint, the family checks; break-it on every decision line (the family, the skip, the
  dedupe key, the mark's class, the 90-day window, the data directory's `source` guard).

## Growth

- `coai.sideSeen@<sideKey>`: one record (~200 bytes) per side ever opened on this profile — a handful; pruned 90 days
  after `lastSeen`, and on **Forget**.
- `executablePathBySide`: at most three short strings per catalog row (the two families and `mirrored`); retired with
  the row.
- Notices: `notifyOnce` records every arrival in the existing bounded ledger; one key per `(key, value)`.
- The findings cache: one entry per value list, replaced on change — the `RootExistence` bound.

## Risks

1. **`globalState` across open windows** — if a stamp reaches the other window only on its next start, "two fields"
   appears one restart late; if it never does, the fallback of E0.2 turns two fields on from the first other-OS path
   seen. Said on the This side page either way.
2. **Two extension versions on two sides** (a WSL remote installs its own copy): an older one keeps writing the legacy
   `executablePath`, and a newer one notices that save through `mirrored` and resolves it for its family (E3.5 —
   measured: its save also DROPS `executablePathBySide` from every row, so the other side's own value is lost until set
   again — a newer window then SAYS so once per start, naming the previously saved value, and offers **Restore**, never
   restoring on its own (`cliPathKeep.ts`; see *E3 as built*); a
   two-field save mirrors this side's value into the legacy field, so the older reader on THIS side is never left with
   a stale value (E3.2's RED covers it). **Not fixable without updating it:** an older reader on the OTHER side reads
   the one legacy value, i.e. the last value any side mirrored — exactly today's single shared value, and coai-mcp ≥
   0.46.0 skips it there as the other side's (design (c), *What it cannot do*).
3. **Settings Sync** carries `executablePathBySide` to another machine like it carries `executablePath` today — no worse;
   the paths are skipped or marked missing there.
4. **A slow or offline share** (`Z:\` not mounted): one `stat` at a time, never awaited by a render, `unknown` never
   cached as absent — the fix branch's contract.
5. **Waking WSL**: the other side's values are never `stat`ed from here (D).
6. **Double toasts with an older server**: the generic toast stays the only one until the server sends
   `settingProblems` — never two for one folder.

7. **A missing named data directory now stops the settings-file write** (D3, E5.7): until the folder is there the
   server keeps reading its previous settings file, or none. Said in the notice; the alternative — creating it — is
   what wrote into a bare `/mnt/z` mount point and hid the condition.

## Decided with the operator (2026-10-09)

The four questions this plan asked were answered the same day; the design above is written to these answers.

- **D1 — Two families only: Windows | WSL / Linux.** A second WSL distro and an SSH remote use the same *WSL / Linux*
  field; `coai.perSideSettings` remains for a full split. (As recommended.) → Design (a), (d); E4.
- **D2 — The other side's value is editable from this window as TEXT** — never `stat`ed from here — for CLI paths and
  list roots; the other side's **data directory is read-only**. (As recommended.) → Design (d); E3.4, E4.2, E4.3.
- **D3 — A data directory that does not exist is ALWAYS red** — the bright mark on the page and the notice at every
  start — not only when its drive or root is missing. (**Not** the recommendation, which was to keep the quiet note
  unless the root was gone.) What this plan decided to make it safe on a first start: only a directory somebody NAMED is
  checked — the default location, created lazily, is never red; a side folder inside an existing named root is created
  lazily too; nothing creates a missing named root, and the choice flow creates the folder it is given. → Design (e)
  *The data directory*; E5.7; risk 7.
- **D4 — `coai.qconsultRoots` leaves *Export settings*.** (As recommended.) → E1.6.

## Definition of Done

- [x] E0 measured and recorded in `research/`; the plan says whether "both known" flips live (it does).
- [x] Every path setting is in one registry; a census test fails on a new one that is not (E1.1; a nested field the
      manifest does not declare is registered by hand — see E1's deviations).
- [x] A path spelled for the other OS is skipped, never refused and never a toast, on both halves, from one vector file.
      (E1: every path setting in the extension; on the server the `COAI_VENDORS` / catalog-`row` CLI paths and, from the
      fix branch, the roots.)
- [x] A missing path of this OS raises one `setting-path-missing` notice per window start, with **Open in Settings**,
      and no generic toast for the same folder (RED first, teeth shown) — E5.3.
- [x] The Settings page marks it with `.path-missing` in `--err`, `aria-invalid`, and a count on its tab — page tests
      RUN the page — E5.4, E5.5.
- [x] The page and coai-mcp agree on which paths are missing — the seam leg green against the real binary — E5.2, E5.6.
- [x] A NAMED data directory that does not exist is red on the page and said at every start (D3); the default location
      on a first start is never red; nothing creates a missing named root (E5.7, `test:host` both halves — see E5's
      deviations for what a host can see).
- [x] *Export settings* never carries `qconsultRoots` (D4) — E1.6.
- [x] Sides seen are recorded, listed and forgettable; `coai.pathFieldsPerSide` declared and documented in five
      languages (E2).
- [x] Two labelled fields per path when both sides are known, one otherwise; older extensions and servers unaffected
      (compat legs green) — E4; E4 stores nothing new, so E3's compat legs stand (`pathsPerSideCompat.test.ts` green).
- [x] `research/module_extension.md`, `research/module_server.md` and `research/architecture.md` updated; the help
      articles in five languages; CHANGELOG entries; this plan promoted with its deviations.
