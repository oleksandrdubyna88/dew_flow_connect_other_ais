# PLAN — a path in the settings belongs to a side: a missing folder is marked, and two sides get two fields

> Status: **plan only, nothing implemented yet (2026-10-09).** Scope: the extension's path-bearing settings and the
> pages that draw them (`src_vs_code`: `pathFamily.ts`, `sideSettings.ts`/`sideConfig.ts`, `settingsShape.ts`,
> `vendors.ts`/`vendorsWire.ts`, `escalationDirs.ts`, `dataDir.ts`, the Settings page's Models, Question consultant,
> MCP server and This side places, `panelProvider.ts`'s notices), and coai-mcp's settings reader and `--providers`
> answer (`src_mcp`: `PanelSettings.cs`, `QuestionConsultSettings.cs`, `ServerJsonContext.cs`, `StartupNotices.cs`).
>
> **Builds on the unmerged branch `fix/qconsult-roots-other-os`** (record: `research/PLAN_qconsult_roots_of_the_other_os.md`
> on that branch, `src_vs_code/src/pathFamily.ts`, `shared/path-family-vectors.json`): a question-consultant root spelled
> for the other OS that does not exist here is skipped on this side, not refused. This plan does not redo that; it
> generalises its rule to every path setting and adds what the operator asked for next. Every `pathFamily.ts`,
> `qconsultHost.ts`/`qconsultView.ts`/`qconsultWrite.ts` and fix-branch `QuestionConsultSettings.cs` line below is cited **on that branch**;
> every other line is `origin/main` at `ba462264`.
>
> **Its four open questions were decided with the operator on 2026-10-09** — see *Decided with the operator* (D1–D4);
> D3 (a missing data directory is always red) went against the recommendation and reshaped E5.
>
> Related docs: [module_extension.md](../research/module_extension.md) (*Settings a side keeps to itself*, *A Team-server
> sign-in belongs to a SIDE*), [module_server.md](../research/module_server.md),
> [architecture.md](../research/architecture.md), [PLAN_a_wsl_consultant_is_checked_from_windows.md](PLAN_a_wsl_consultant_is_checked_from_windows.md).

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
4. Older extensions and older coai-mcp builds keep working on the values they already read.

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
A future `coai.credsPath` ([PLAN_coai_finds_creds_where_it_is.md](PLAN_coai_finds_creds_where_it_is.md)) would be an
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

1. `executablePathBySide[family]`, when non-empty;
2. else the legacy `executablePath`, unless it is spelled for the other OS;
3. else empty — a PATH lookup — **and** a finding `otherSide` naming the skipped value.

Writes: with two fields shown, a field writes `executablePathBySide[<its family>]` **and, when it is THIS side's field,
mirrors the same value into the legacy `executablePath`** (the plan round, finding 0): an older extension reads the
legacy value only, so it keeps launching what this side last saved — exactly what it does today, never a stale or
empty value invented by the new storage. Editing the OTHER side's field writes only `BySide` (the other side's newer
extension reads it first; an older one there keeps today's behaviour). With one field (one side known), the field writes
the legacy value exactly as today. Two-field mode therefore never makes an older reader worse than today; it makes a
newer reader on each side right. The server applies rule 3 itself as defence in depth (an older extension still
sends the legacy value): an `ExecutablePath` spelled for the other OS is treated as empty, with an Information log line
— never a refusal.

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

### E0 — measure before building (no product code) · `plan/paths-per-side-e0-measure`

- **0.1** On the operator's machine, a `test:host`-style probe: a stamp written to `globalState` in a WSL window — is it
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
- **0.3** The VS Code floor (`engines.vscode ^1.85.0`) has `Memento.keys()` — confirm against `@types/vscode`; if not,
  the registry is one record under one key with per-side entries (races lose at most a `lastSeen`).

### E1 — one rule for every path setting · `feat/paths-per-side-e1-one-rule`

- **1.1 The registry as code.** `pathSettings.ts`: every path setting, its field, its kind (`folder`, `file`,
  `folderList`), whether it must already exist, and who says it (the table in (e)). RED: a census test that walks
  `package.json`'s `contributes.configuration` and fails on a setting or nested field named `*Path`/`*Directory`/`*Roots`/
  `*Directories` missing from the registry (today: seven fields in six settings; `coai.credsPath` would be the eighth).
- **1.2 A CLI path of the other OS is skipped, not run.** `pathForThisSide` (rules 2–3 of (c)) in the extension's
  `COAI_VENDORS` writer and launch paths (`vendorsWire.ts:115`, `chatPresets.ts:470`, `claudeCli.ts:32-41`), and the
  same in coai-mcp's vendor read (`PanelSettings.cs:1270`). RED (TS): a Windows-side reader given
  `executablePath: '/usr/local/bin/codex'` sends it to the server today. RED (C#): the probe of such a row answers
  `CliFound=false`; after, it probes the PATH name and logs one Information line. Vectors in
  `shared/path-family-vectors.json` (`executable` set), read checked by both halves.
- **1.3 `alsoWatchDataDirectories` reads the other side's entries as the other side's.** On Windows a `/…` entry stops
  being REFUSED (`escalationDirs.ts:84`) and becomes *"the other side's"*; on WSL a `\\wsl.localhost\…` entry stops
  failing as *"could not be read"*. RED: `watchedDirs(own, ['\\\\wsl.localhost\\Ubuntu\\x'], 'linux')` returns it as
  usable today.
- **1.4 A shared `dataDirectory` of the other OS is not resolved.** `chooseStorage` skips a shared-layer value spelled
  for the other OS (falls to the default) and `whereData` says so. RED: a `linux` host with shared `Z:\coai` resolves to
  `<cwd>/Z:\coai` today (inject `resolve`/platform).
- **1.5** The page names every such value *"the other side's — skipped here"* in the quiet hint tone, through the one
  `otherSideNote` (`pathFamily.ts:154-158`), generalised by setting. Page test: the Models card of a row with a POSIX path
  on a Windows page shows the note, and no **cannot review** badge is blamed on it.
- **1.6 *Export settings* leaves `qconsultRoots` out** (decided with the operator, D4) — added to `NEVER_TRANSFERRED`
  (`configTransfer.ts:29-40`) as *"paths on this machine"*, like `alsoWatchDataDirectories`; an import that carries it
  (an older export) refuses that entry by name and keeps the importer's own roots. RED: `exportedSettings` of a profile
  with roots set contains `qconsultRoots` today; an import of a v2 file holding it overwrites the importer's roots today.

### E5 — a missing path of this OS: a notice at every start and a bright mark · `feat/paths-per-side-e5-missing`

Built right after E1: it is the operator's item 1 and needs no side registry.

- **5.1 The findings.** `pathFindings(registry, read, family, isDirectory/isFile)` → `PathFinding[]`, one `stat` at a
  time, cached, unknown never cached (the `RootExistence` contract). RED: a this-OS root `D:\old-project` that is ENOENT
  yields `missing` — today the page asks the disk about other-OS roots only.
- **5.2 The server says which setting and which value.** `UnrecognisedSetting` gains a kind; `ProvidersAnswer` gains
  `SettingProblems` (additive, JSON name `settingProblems`); `providers.ts` parses it, an absent field reading as none.
  RED (C#): `--providers` with a missing root carries no `settingProblems`. Contract test (`test:contract`) on the
  field's shape.
- **5.3 The notice.** `setting-path-missing` per `(key, value)` through `notifyOnce`, with **Open in Settings**; the
  generic toast stops repeating a sentence whose `(key, value)` the structured answer covers. RED: the operator's case
  today produces the generic code with the cure *"running without those settings"*; after, one
  `setting-path-missing` with the place, and no generic toast. Unit test on the dedupe; `notificationSites` ratchet
  updated.
- **5.4 The mark.** `.path-missing` (`--err`, the `.skew` shape, `●`) beside the value on Question consultant, Models
  (CLI path), MCP server (watched folders); `aria-invalid`. Page test, RUN through `runPanel`: given a finding, the row
  holding `D:\old-project` has class `path-missing`, contains the word *missing*, and the input/row has
  `aria-invalid="true"`; given none, no such class anywhere. Break-it: draw it with `.stale` — the test must go red on
  the class, not on the text.
- **5.5 The tab count.** `tabStrip` takes an optional mark per tab key; the Consultants tab and its Question consultant
  sub-tab show `● 1` with `aria-label`. Page test through `pageTree`: the count is on the tab, in DOM order after the
  label; zero findings draw no badge.
- **5.6 Page and server agree.** The page prefers the server's `settingProblems` for `qconsultRoots`; its own `stat`
  only fills in while the server has not answered or is older (capability via `--features`). Seam leg
  `scripts/seam-path-missing.mjs` in `test:seam`: the real coai-mcp, a settings file with one existing and one deleted
  root, the page's findings against `--providers`' `settingProblems` — the same `(key, value, kind)` set.
- **5.7 A named data directory that is missing is always red (D3).** `dataDirFinding(chosen, exists)` (pure, beside
  `whereData`): `missing` when a named ROOT is absent; nothing for `source: 'default'`; nothing (the quiet note) for an
  absent side folder inside an existing root. The activation order puts the check before the settings-file write, and
  `writeSettingsFile`/`underSettingsLock` stop creating a missing named root (they still create the default location
  and a side folder). The choice flow creates the folder it was given or refuses the choice. REDs, each recorded:
  (1) unit — a named `Z:\coai` that does not exist yields no finding today, only the *"not there yet"* note;
  (2) unit, injected `createDirectory` — activation with a missing named root creates it today (the write that hides
  the condition); (3) page, RUN through `runPanel` — Setup › MCP server draws the missing root with `.path-missing` and
  *missing*, and a default location that does not exist yet draws **no** mark; (4) `test:host` — a fresh profile with no
  data directory anywhere starts with **no** `setting-path-missing` in the ledger (the first-start guarantee), and a
  profile whose overlay names a deleted folder gets exactly one per window start, and the folder is still absent after
  activation. Break-it: drop the `source` guard — (4)'s first half must go red.

### E2 — the sides this profile has been used on · `feat/paths-per-side-e2-sides-seen`

- **2.1** `sidesSeen.ts` (pure): `stampSide(stored, side, family, label, now)` and `knownFamilies(records, now)`, with
  the 90-day window and pruning. RED: `knownFamilies` of a Windows and a WSL record within 90 days is `{windows, posix}`;
  a 91-day-old WSL record is pruned.
- **2.2** Stamped at activation, at most once a day per side (`extension.ts`), never awaited by activation.
- **2.3** `coai.pathFieldsPerSide` (`auto`/`always`/`never`, `application` scope), declared, described in the five help
  languages, never exported (`configTransfer.ts` `NEVER_TRANSFERRED`).
- **2.4** Setup › This side lists the sides seen (label, family, last seen) with **Forget** and the switch, and a local
  window names itself (`thisSideLabel`, not *"this machine"*). Page test: two seeded records draw two rows; Forget posts
  the side key. `test:host` scenario: activation leaves a `coai.sideSeen@…` record for the test window's side.

### E3 — two values, stored and resolved · `feat/paths-per-side-e3-two-values`

- **3.1** `executablePathBySide` read and written (`vendors.ts:386`, `catalogEdit.ts`), carried by the catalog
  migration and by *Export settings* **without** its values (`configTransfer.ts:44`'s `MACHINE_PATH_FIELD` widened to
  both names). RED: a row with `executablePathBySide.posix` set still sends the legacy value to a WSL server today.
- **3.2** `pathForThisSide` rule 1, with the write rules of (c): two fields write `BySide` and this side's field also
  mirrors into the legacy value; one field writes the legacy value. REDs: a two-field save of THIS side's field leaves the
  legacy value stale for an older reader; a save of the OTHER side's field must not touch the legacy value.
- **3.3** Lists grouped by family through `otherSideHere` (O1). Unit: `/work` that exists on Windows groups as
  Windows'; `/home/jinx/git` groups as WSL / Linux's.
- **3.4** `dataDirectory`: the other side's value read from its overlay via the registry, never written from here.
- **3.5** Compatibility pinned: a settings file written by an older extension (legacy only) resolves identically; a
  server settings file written by the new extension parses in the oldest supported coai-mcp (the existing
  `live-*-compat` pattern, `package.json` scripts).

### E4 — the page draws two fields · `feat/paths-per-side-e4-two-fields`

- **4.1** The Models card: two CLI-path fields when both are known, this side's first, the other in a
  `fieldset`/`legend` with its caption; one field otherwise. Page test, RUN: with two families known the card holds two
  inputs whose labels name *Windows* and *WSL / Linux*; the other side's input has no Browse; with one family, one input.
- **4.2** Question consultant: two folder groups; Add a folder adds to this side's; the other group accepts typed text,
  refused when spelled for the wrong OS (the refusal shown beside the box, not as a toast).
- **4.3** MCP server: watched folders in two groups; the data directory with the other side's value read-only (D2) —
  page test: the other side's data directory is text, not an input, and offers no save.
- **4.4** `coai.pathFieldsPerSide = never` restores one field everywhere; `always` draws two with a single side seen
  (the other labelled *"the other side (not seen yet)"*).
- **4.5** `test:host` scenario: seed a WSL side record, open Settings › Models in the real editor, see two CLI-path
  fields; type the WSL path, and the settings file this side writes for its server still carries this side's value.

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
- `executablePathBySide`: at most two short strings per catalog row; retired with the row.
- Notices: `notifyOnce` records every arrival in the existing bounded ledger; one key per `(key, value)`.
- The findings cache: one entry per value list, replaced on change — the `RootExistence` bound.

## Risks

1. **`globalState` across open windows** — if a stamp reaches the other window only on its next start, "two fields"
   appears one restart late; if it never does, the fallback of E0.2 turns two fields on from the first other-OS path
   seen. Said on the This side page either way.
2. **Two extension versions on two sides** (a WSL remote installs its own copy): an older one keeps writing the legacy
   `executablePath`; the newer one reads `BySide` first, so it is not affected, and the older reads what it always read
   — and a two-field save mirrors this side's value into the legacy field, so the older reader on this side is never
   left with a stale value (E3.2's RED covers it).
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

- [ ] E0 measured and recorded in `research/`; the plan says whether "both known" flips live.
- [ ] Every path setting is in one registry; a census test fails on a new one that is not.
- [ ] A path spelled for the other OS is skipped, never refused and never a toast, on both halves, from one vector file.
- [ ] A missing path of this OS raises one `setting-path-missing` notice per window start, with **Open in Settings**,
      and no generic toast for the same folder (RED first, teeth shown).
- [ ] The Settings page marks it with `.path-missing` in `--err`, `aria-invalid`, and a count on its tab — page tests
      RUN the page.
- [ ] The page and coai-mcp agree on which paths are missing — the seam leg green against the real binary.
- [ ] A NAMED data directory that does not exist is red on the page and said at every start (D3); the default location
      on a first start is never red; nothing creates a missing named root (E5.7, `test:host` both halves).
- [ ] *Export settings* never carries `qconsultRoots` (D4).
- [ ] Sides seen are recorded, listed and forgettable; `coai.pathFieldsPerSide` declared and documented in five
      languages.
- [ ] Two labelled fields per path when both sides are known, one otherwise; older extensions and servers unaffected
      (compat legs green).
- [ ] `research/module_extension.md`, `research/module_server.md` and `research/architecture.md` updated; the help
      articles in five languages; CHANGELOG entries; this plan promoted with its deviations.
