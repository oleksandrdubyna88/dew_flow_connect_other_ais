# PLAN — E2 stories: the extension, everyone is on `auto` and can see what it did (`src_vs_code`)

> Status: **plan only, nothing coded — written 2026-11-17 by Opus when the epic started, on branch
> `feat/auto-mode-e2` at `origin/main` `b3abb791` (E1 merged as #1011, released as mcp 0.66.0 by #1012).** Epic E2 of
> [PLAN_auto_never_asks_a_person.md](PLAN_auto_never_asks_a_person.md) (its `## Epics`, § *E2*; the design is its § 4.5
> page item, § 4.6 and § 4.8). A stories document keeps no change ledger: a change to this epic is an entry in the
> parent. One item was reported to the coordinator as a proposed entry; the coordinator recorded it as the
> parent's entry 2.1 on 2026-11-17 (§ 10, R1).
>
> Scope: `src_vs_code` and the documents a reader reads. Three stories, the parent's intended cut, kept. **Cornerstone:
> E2.S2** (`model: fable`), the one-time migration. Its scenarios and brief are Fable's, in
> `todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md`. That file is written after this document and does not exist yet.
> This document fixes S2's boundary, the contracts S1 and S3 rely on, and the inputs the spec must settle. S1 and S3
> are briefed here in full. The parent's ledger line now reads `Cornerstones: 2 of 2 — E1.S1, E2.S2`.
>
> **Q1 is the owner's decision (2026-11-17): "workspace не трогаем".** A workspace-scoped `coai.onExhausted` is not
> migrated. The gate section names it. The parent's § 10 records the answer.

## 1. Goal, and why the cut holds

When E2 is done, everyone who installs the extension is on `auto` (O1). The manifest, the panel's `DEFAULTS` and the
server agree that the default is `auto`. A one-time migration clears every value the person saved: the user layer and
every side's overlay. The settings file is right for the binary the panel meets: nothing is written for a server that
lists `onExhaustedAuto`, and `good_enough` is written for an older server, which the gate section names. The rounds-log
page shows what `auto` did, and the help says it in five languages. The parent's E2 done-when 1–6 are the bar (§ 11
maps them).

The parent's three stories are kept, and the code confirms the cut:

- **S1** is the value and the wire: the type, the defaults, the select, the feature, the env cells and the gate-section
  lines. None of it writes a person's saved choice. It is the only story that touches `settingsShape.ts`,
  `panelView.ts`, `binaryFeatures.ts` and the settings-file writer.
- **S2** is the only story that WRITES a person's saved settings. It is a module of its own, started from activation,
  and it needs from S1 only the manifest default `auto`, so that a cleared location reads `auto`.
- **S3** is what a person reads: the rounds-log page (one function in `roundsLog.ts`'s page script) and the words. Its
  page half touches no file S1 or S2 touches. Its words describe S1 and S2 as built, so they come last.

The parent's "the copy action awaits `askFeatures()`" (E2.S1, § 4.6) does not survive contact with the code. See
§ 2.1 and § 10, R1.

## 2. What exists — verified at `b3abb791`

Paths are from the repository root. `git diff 4cb32a76 b3abb791 -- src_vs_code` is 46 files (the terminal-chat E3,
#1015, and E1's one-line seam pin). Every line below was opened at `b3abb791`.

| Fact | Where |
|---|---|
| `OnExhausted = 'continue' \| 'escalate' \| 'human' \| 'good_enough'` | `src_vs_code/src/settingsShape.ts:31` |
| `DEFAULTS.onExhausted: 'human'` | `settingsShape.ts:276` |
| `OVERLAID_SETTINGS` holds `'onExhausted'`; `seedOverlay` copies every overlaid key the shared reader answers, the manifest default included | `settingsShape.ts:315-316`; `:403-413` |
| `settingsFrom` → `asOnExhausted(read('onExhausted'))`; an unknown word falls back to `DEFAULTS.onExhausted` | `settingsShape.ts:422`; `:627-631` |
| `envBlock(settings, vendors, installedServerVersion, priceOf, features)`: one large function (already in `eslint-suppressions.json` for complexity and length); `COAI_ON_EXHAUSTED` written only when it differs from `DEFAULTS` | `settingsShape.ts:458-602`; `:551-553` |
| `settingsShape.ts` is 729 lines (cap 800, `eslint.config.mjs:113`) | — |
| The manifest: `coai.onExhausted`, enum of four, `"default": "human"` | `src_vs_code/package.json:354-364` (default `:362`) |
| `FEATURES` (no `onExhaustedAuto` yet); `BinaryFeatures.settled`; `settledFeatures()`; `FeaturesCache.known()` answers `[]` both before a settled answer and after a settled empty one (`NO_LIST`) | `src_vs_code/src/binaryFeatures.ts:18-50`; `:52-61`; `:67-69`; `:152-155`, `:175` |
| A binary older than `--features` answers 64 → `{ features: [], settled: true }` | `binaryFeatures.ts:79-82` |
| The settings file: `serverSettingsJson` (passes `features: []`); `SettingsWrite` ("each said, none defaulted"); `serverSettingsJsonWith` → `envBlock` | `src_vs_code/src/serverSettingsFile.ts:93-103`; `:105-115`; `:117-123` |
| `ServerSettingsSync`'s `features` getter, read on every sync | `src_vs_code/src/serverSettingsSync.ts:152-156`, `:166-176` |
| Activation: `FeaturesCache` whose `onSettled` mirrors again; `askFeatures`; `ServerSettingsSync` built with `() => features.known()`; the first `mirrorSettings`; the catalog migration's start and its mirror | `src_vs_code/src/extension.ts:414-417`; `:418`; `:419-437` (getter `:434`); `:438`; `:454-458` |
| `readCoaiConfiguration` → `readerFor` → `settingsFrom` | `extension.ts:1125-1132`; `src_vs_code/src/sideConfig.ts:29-38` |
| `onExhausted` is read through `config.get` (the merged value, workspace included), not through the user-layer door | `src_vs_code/src/modelKeys.ts:19-40`, `:66` |
| The per-side reader: this side's overlay first, the shared value otherwise | `src_vs_code/src/sideSettings.ts:68-75` |
| `writeOverlay` (a `globalState.update` of the whole record, no configuration event); `seedIfEmpty` → `seedOverlay` | `sideSettings.ts:33-41`; `:86-101` (`:95`) |
| `overlayKey` / `overlayKeyOf` = `coai.settingsOverlay@<sideKey>` | `src_vs_code/src/coaiInstall.ts:345-363` |
| The side registry: `coai.sideSeen@<sideKey>`, pruned after 90 days, listed by `Memento.keys()` | `src_vs_code/src/sidesSeen.ts:24`, `:30`, `:214`, `:270` |
| The catalog migration: user layer through `inspect().globalValue`, and this side's overlay only; runs queued through `CatalogTurns` in one extension host | `src_vs_code/src/catalogMigrationHost.ts:13-22`, `:32`, `:60-62` |
| The settings-file lock: an O_EXCL file in THIS side's data directory | `extension.ts:1172-1210` |
| Every clipboard block carries `serverEnv()` only — `COAI_DATA_DIR` and `COAI_DATA_SIDE` | `extension.ts:1456`, `:1502`; `src_vs_code/src/dataCommands.ts:243`; `src_vs_code/src/dataDir.ts:342-352` |
| …and two tests hold it: the call sites pass nothing but `serverEnv()`; a server is handed the two keys and only those two (since `a0eee1ca`, 2026-09-15) | `src_vs_code/src/test/install.test.ts:947-973`; `src_vs_code/src/test/theDirectoryAPersonChose.test.ts:197` |
| The gate section: `gateBody`, the select (four options), the `stale` line precedent `stopLocalSkew` | `src_vs_code/src/panelView.ts:1636-1675` (select `:1642-1648`); `:1677-1681` |
| `panelView.ts` is 3,527 lines, `panelProvider.ts` 4,959, `extension.ts` 1,948, `roundsLog.ts` 2,428 — every one already suppressed for `max-lines` | `src_vs_code/eslint-suppressions.json` |
| `PanelState.serverFeatures` — `undefined` until settled; set from `settledFeatures(features)` | `panelView.ts:319-323`; `src_vs_code/src/panelProvider.ts:1489` |
| The page harness for the panel (`runPanel`, `panelState`, `Page.html`, a select's `fire('change')`) | `src_vs_code/src/test/panelPageHarness.ts:293`, `:334`, `:505`; used by `refusedSelect.test.ts:24-30`, `:100-108` |
| The rounds log: `LogRow.note` ("why a SKIPPED round did not run"); `noteOf(round)` passes every round's note; the detail pane shows it only on the skipped branch; the verdict cell is the raw string; the verdict facet's options are drawn from the rows | `src_vs_code/src/roundsLog.ts:88-91`; `:544` with `src_vs_code/src/rounds.ts:439-444`; `:1783-1803` (skip `:1790-1793`); `:1907`; `:1050-1086` |
| The page-run harnesses: `runningPage` (click), `runningDomPage` (a real tree; a select's `value` and `dispatch('change')`) | `src_vs_code/src/test/roundsLogPageHarness.ts:176`, `:76`; `src_vs_code/src/test/pageDom.ts:82`, `:292` |
| The help: the `onExhausted` tooltip; the `the-gate` article (EN) and its four translations | `src_vs_code/src/help.ts:163-164`; `src_vs_code/src/helpContent.ts:138-155`; `helpDe/Es/Ru/Uk.ts` (`"the-gate"`, each ~`:107-113` / `:43-49`) |
| The five-language help test shape | `src_vs_code/src/test/kimiHelp.test.ts` |
| The defaults agreement test reads the C# (`SessionState.cs`, `PanelSettings.cs`) | `src_vs_code/src/test/panelServerDefaultsAgreement.test.ts:33-79`, `:89-110` |
| The C# the agreement reads: `PolicyOf` — `null or "" or "auto" => null`; the code constructor — `Autonomous = OnExhausted is null` | `src_mcp/src/Server/PanelSettings.cs:1092-1100`; `src_mcp/core/Rounds/SessionState.cs:87-92` |
| `--features` lists `onExhaustedAuto` | `src_mcp/src/Server/FeaturesMode.cs:50-52` |
| An old server's complaint for a word it does not know; the pinned-policy startup note | `PanelSettings.cs:1227-1236`; `:1244-1250` |
| The held-gate seam leg pins `COAI_ON_EXHAUSTED: 'human'` in the process env, because "the panel's writer leaves the policy out at its default (Ask a human)" | `src_vs_code/scripts/seam-held-gate.mjs:108-124` |
| The features seam asserts the real binary lists every `FEATURES` entry | `src_vs_code/scripts/seam-features.mjs:18-31` |
| The `*-compat` scripts: a released binary (`releasedServer`, `--tag=`) against the current one; exit 2 = no result | `src_vs_code/scripts/releasedHalves.mjs`; `live-command-models-compat.mjs` (the shape to copy) |
| The CHANGELOG: `## Server 0.66.0` (tells people to pin `COAI_ON_EXHAUSTED=human` in the client env until this extension); the was/now precedent `## Extension 0.31.6` | `src_vs_code/CHANGELOG.md:3-23` (`:23`); `:3642` |
| A new `notify` call site changes the census | `src_vs_code/scripts/count-notifications.mjs`, `src_vs_code/notification-sites.json` |
| A module that imports `vscode` must be in `sonar.coverage.exclusions`, and a test checks the list is exact | `.github/workflows/sonarcloud.yml:268`; `src_vs_code/src/test/sonarExclusions.test.ts:91-98` |
| CI: `npm ci`, `npm run lint`, `npm test`, `npm run package` (extension job); `npm run test:contract`, `npm run test:seam` after a Release build (main job); `test:host` in a real editor, `continue-on-error` | `.github/workflows/ci.yml:131-144`, `:558-584`, `:596-630` |

### 2.1 The parent's references for E2, checked

Every E2 reference in the parent (§ 2, § 4.5, § 4.6, § 4.8, § 8 PR 2, `## Epics` § E2, N5–N8) was opened at
`b3abb791`. These were wrong or out of date:

| Parent says | The tree says |
|---|---|
| § 2, § 4.6: the manifest's `coai.onExhausted` is `package.json:349` | `:354-364`, default `:362`. |
| § 2, § 4.1: `PanelSettings.PolicyOf` `:1082-1088`, `Config` `:1679-1683`; § 4.6: `WhyExhausted` `:1215-1224` | E1 moved them: `:1092-1100`, `:1711`, `:1227-1236`. |
| § 4.8: the 0.31.6 was/now entry is `CHANGELOG.md:3584` | `## Extension 0.31.6` is at `:3642`, moved down by the entries since. |
| `## Epics` § E2: the copy action is at `src/extension.ts:407` | `askFeatures` is at `:418`, and the copy action is `copyConfigBlock`, `:1482-1512`. |
| N7: the catalog migration's mirror is `extension.ts:445-448`; overlays are written at `sideSettings.ts:36-43` | `:454-458`; `writeOverlay` is `:33-41`. |
| N6: the catalog queue is `catalogMigrationHost.ts:34` | `const turns = new CatalogTurns()` is `:32`. |
| **§ 4.6, § 12 finding 3, E2 done-when 2, E2.S1: "A block the panel hands out for a client's `env` … the copy action awaits `askFeatures()`, and if no binary answers it writes `COAI_ON_EXHAUSTED=auto` explicitly"** | **No block the panel hands out carries a policy.** All three clipboard writes pass `serverEnv()`, which holds only `COAI_DATA_DIR` and `COAI_DATA_SIDE`. This has been so since `a0eee1ca` (2026-09-15), and two tests refuse anything else (§ 2). The policy reaches the server only through the settings file, and every settled `--features` answer rewrites that file. The risk finding 3 named does not exist, and building the step would put a pinned `COAI_ON_EXHAUSTED=auto` into a client entry, where it would silently override a later panel choice of Ask a human. **Not built; reported as R1 (§ 10).** |

Everything else matched, to the line or within one line: `settingsShape.ts:31`, `:276`, `:404-414` (now `:403-413`),
`:551-553`, `:627-631`; `panelView.ts:1642-1648`; `sideSettings.ts:95`; `binaryFeatures.ts:18`; `coaiInstall.ts:345-363`;
`roundsLog.ts:1790` (the skip branch) and `:1907`; `src_vs_code/README.md:44-45`; `ARCHITECTURE.md:80-82` (and its
stale "plan 3 rounds at 2; code 2 at 3", `:80`); `help.ts:163-164`; `helpContent.ts:143-149`;
`SettingsFile.cs:49-58` (the layer that puts the client env over the file; `Over` itself is `:122`); `FeaturesMode.cs`.
The root `README.md` § *When the rounds run out* is `:165-171` (the parent named no line).

## 3. Conventions every story follows

- **Branch.** `feat/auto-mode-e2`, one PR for the epic. One coder at a time on the branch (§ 6 for the two exceptions).
- **Models.** S2: spec by **Fable**, code by Sonnet, review by Opus. S1 and S3: brief by Opus (this document), code by
  Sonnet, review by Opus. Fixes by Sonnet.
- **RED first.** Write every scenario before its code and watch it fail for the right reason: the assertion, not a
  compile error. Add the empty member first when a test cannot compile without it. Then make it GREEN. Then break it:
  remove the production line named in the scenario, see the stated failure, and restore the line. Report all three
  in the summary.
- **Existing tests keep their meaning.** S1 flips the default to `auto`. A test that goes red ONLY because it encoded
  "the default is Ask a human" gets its expectation changed. Only these tests do: say each one in the PR, with the line
  and the reason. A test that goes red for any other reason is a defect in the change, not in the test.
- **A webview page is tested by RUNNING it** (`.agents/PROJECT.md`). The gate section is tested through
  `runPanel(panelState('gate', …))` (`panelPageHarness.ts`), and the rounds-log page through `runningPage` /
  `runningDomPage` (`roundsLogPageHarness.ts`). Never add a new assertion over a page's source text.
- **Lint.** A new function keeps `complexity` ≤ 4 and ≤ 50 lines. New logic goes into the new small modules named
  below; the big files get call sites only. The counts in `eslint-suppressions.json` must not grow (CI: *Lint
  suppressions only shrink*). Split, never suppress.
- **A new module that imports `vscode`** goes into `sonar.coverage.exclusions` in `.github/workflows/sonarcloud.yml`, or
  `sonarExclusions.test.ts` fails. Pure modules stay `vscode`-free.
- **A new `notify` call** (S2's notification): run `node scripts/count-notifications.mjs --write` and commit
  `notification-sites.json`.
- **Clock.** A page fixture with a date uses `todayAtLocal(...)` / `STARTED` from `roundsLogPageHarness.ts`, never a
  fixed day (`testing.md`, *A test that reads the clock expires*).
- **Temp folders.** Tests import `../../scripts/inRunFolder.cjs` first, as every test file does. A script owns its run
  folder through `startOwnedRun` (`scripts/ownedRun.mjs`).
- **No `src_mcp` change** in E2. The C# is read by a test, never edited.

Commands, from `src_vs_code/` (the conventions mount is `../.agents/conventions`):

```bash
npm ci                                                          # once
npm run compile                                                 # read the EXIT CODE: tsc emits even when red
node scripts/run-tests.mjs out/test/<name>.test.js              # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test     # the whole suite (compile + run-tests)
npm run lint                                                    # eslint src — what CI runs
node scripts/count-notifications.mjs --write                    # S2, after its notify call
```

The seam and the contract run against this repository's coai-mcp build. From the repository root:

```bash
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Release -m:4
#   read the exit code — a failed build leaves the previous binaries, and a seam over them proves nothing
cd src_vs_code
node ../.agents/conventions/tools/low-priority.mjs npm run test:seam
node ../.agents/conventions/tools/low-priority.mjs npm run test:contract
npm run test:on-exhausted-compat -- --tag=mcp-v0.65.4           # S1's new script (§ 7.4); exit 2 is NO result
npm run test:host                                               # cannot start on this machine (no libnss3); CI runs it
cd .. && node .agents/conventions/tools/plan-lifecycle.mjs      # before every commit that touches todo/
```

## 4. Shared contracts

S2's spec may refine a name in C4–C6. If it does, it updates this section and the S3 brief in the same commit.

- **C1 — the value (S1).** `OnExhausted = 'auto' | 'human' | 'continue' | 'good_enough' | 'escalate'`.
  `DEFAULTS.onExhausted === 'auto'`, and the manifest's default is `"auto"`. A location with no value reads `auto`.
- **C2 — the labels (S1).** `ON_EXHAUSTED_CHOICES: readonly { value: OnExhausted; label: string }[]` in the new pure
  module `src/onExhaustedPanel.ts`, in the select's order: Auto first. Every page, sentence and test that names a
  choice takes its label from here. The labels are `Auto`, `Ask a human`, `Continue, and say so`,
  `Good enough — take what’s true and move on` and `Climb the ladder`. S3's help names them exactly so.
- **C3 — the wire (S1).** `onExhaustedEnv(value, features, featuresSettled)` in the new pure module
  `src/onExhaustedWire.ts` is the parent § 4.6 table. `envBlock` gains a sixth parameter, `featuresSettled = false`,
  and calls it. `SettingsWrite` gains `featuresSettled: boolean`. `FeaturesCache.settled()` answers
  `readonly string[] | undefined`: `undefined` until this file's answer has settled.
- **C4 — the migration's entry (S2).** One exported host function, `startOnExhaustedMigration(context, mirror)`, in
  the new `src/onExhaustedMigrationHost.ts`. It is called once from `activate`, after the first `mirrorSettings`
  (`extension.ts:438`). `mirror` is `() => mirrorSettings(settingsSync)`. Its decisions live in a pure module beside
  it, `src/onExhaustedMigration.ts`.
- **C5 — the marker (S2).** A `globalState` key `coai.onExhaustedAutoMigration`, whose value is the extension version
  that completed the run (parent § 4.6). The spec settles what else it holds (N8, N9).
- **C6 — what the migration touches (S2).** It touches the user layer (`ConfigurationTarget.Global`) and every
  overlay's `onExhausted`. It never touches a workspace or folder value (Q1, the owner's decision), a client's `env`
  block, or the server settings file directly: the file changes only through `mirror`. Its one notification is the
  parent § 4.6 sentence, word for word. S3 quotes it, and only the coordinator changes it.

## 5. The notes the parent left to this epic

N5–N8 are S2's: the spec settles them (§ 8, inputs 1–4). N4 is settled by S1. "Old panel + new server" was E1's
window: an old panel never wrote its default `human`. A panel `human` now differs from the default `auto`, so it is
written and honoured (§ 7, the env table's second row). The Server 0.66.0 note told people to pin
`COAI_ON_EXHAUSTED=human` in the client env until this release. S3's CHANGELOG draft says to remove that pin once Ask
a human is chosen in the panel (§ 9.4). The server's pinned-policy startup note already tells them (E1.S3).

## 6. Order, and what may run in parallel

1. **S1 coded while Fable writes S2's spec.** S1 is fully briefed here and needs nothing from S2. The spec needs
   nothing from S1's code, only C1 and C4–C6.
2. **S2 coded after S1's commits are on the branch and the spec is committed.** S2's tests read a cleared location as
   `auto`, which is S1's manifest default.
3. **S3 last, in two halves.** The **page slice** (`roundsLog.ts`'s `detail()`, `LogRow.note`'s summary and its new
   test file) touches no file S1 or S2 touches. It may be coded in a second worktree branched from `b3abb791` at any
   time, and merged into `feat/auto-mode-e2` after S1. The **words** (help, READMEs, `ARCHITECTURE.md`,
   `research/module_extension.md`, the CHANGELOG draft) describe S1 and S2 as built, so they start after S2.

**The files, and who owns which lines.** A file or range one story owns is not edited by another.

| File | S1 | S2 | S3 |
|---|---|---|---|
| `src_vs_code/package.json` | `coai.onExhausted` (`:354-364`); one `scripts` entry `test:on-exhausted-compat` | — | — |
| `src/settingsShape.ts` | all of it (`:31`, `:276`, `envBlock`'s signature and `:551-553`, `:627-631`) | reads `OVERLAID_SETTINGS` at most | — |
| `src/onExhaustedWire.ts`, `src/onExhaustedPanel.ts` (new, pure) | all | — | reads the labels (C2) |
| `src/binaryFeatures.ts`, `src/serverSettingsFile.ts`, `src/serverSettingsSync.ts` | all | — | — |
| `src/extension.ts` | `:434` only (the getter) | one call after `:438` (C4) | — |
| `src/panelView.ts` | `gateBody`'s select and its two lines; one optional `PanelState` member | — | — |
| `src/panelProvider.ts` | one member in the state it builds (`:1489` neighbourhood) | — | — |
| `src/onExhaustedMigration.ts`, `src/onExhaustedMigrationHost.ts` (new) | — | all | — |
| `.github/workflows/sonarcloud.yml` | — | the host module in the exclusions | — |
| `notification-sites.json` | — | regenerated | — |
| `src/roundsLog.ts` | — | — | `detail()` (`:1783-1803`) and `LogRow.note`'s summary (`:88-91`) |
| `src/help.ts`, `src/helpContent.ts`, `src/helpDe/Es/Ru/Uk.ts` | — | — | all |
| `scripts/seam-held-gate.mjs` | the pin (`:108-124`) | — | — |
| `scripts/live-on-exhausted-compat.mjs` (new) | all | — | — |
| seam scripts and tests calling `serverSettingsJsonWith` | the new `featuresSettled` field only | — | — |
| `research/module_tests.md` | a row, if it adds a flow | its row (the migration is a flow) | a row for the page note |
| `README.md`, `src_vs_code/README.md`, `ARCHITECTURE.md`, `research/module_extension.md` | — | — | all |

The only real overlap is `extension.ts`: two hunks, about twenty lines apart, in different statements.
`settingsShape.ts` is S1's alone. S2 needs no edit there: the overlay keys come from `coaiInstall.ts` and the store's
`keys()`.

## 7. E2.S1 — the value, and what the server is sent · Opus brief, Sonnet code

**What it builds.** Parent § 4.6, panel half: the manifest, the shape, the select, the feature, the six env cells and
the two gate-section lines. Also the defaults-agreement test, the `test:*-compat` script, and the held-gate leg's pin
under the new default. This is § 8 PR 2, steps 1 and 5. Done-when 1 and 2 (§ 11). **Not built:** the copy action's
wait (R1). The copied block keeps carrying only the data folder, as two existing tests require.

### 7.1 The change

1. **The value.** `settingsShape.ts:31`: `export type OnExhausted = 'auto' | 'human' | 'continue' | 'good_enough' | 'escalate';`.
   Set `DEFAULTS.onExhausted` (`:276`) to `'auto'`. `asOnExhausted` (`:627-631`) accepts `'auto'`. It reads the
   choices from C2 rather than a fifth `||`, which keeps complexity ≤ 4:
   `ON_EXHAUSTED_CHOICES.some((c) => c.value === value) ? value as OnExhausted : DEFAULTS.onExhausted`. Import from
   `onExhaustedPanel.ts`, a `vscode`-free module that imports only the type. If that makes an import cycle, put the
   list in `settingsShape.ts` beside the type and re-export it from `onExhaustedPanel.ts`.
2. **The manifest.** `package.json` `coai.onExhausted`: enum
   `["auto", "human", "continue", "good_enough", "escalate"]`, `"default": "auto"`, and a `markdownDescription` that
   names all five. Write it as follows, auto first, with requirement 5's guarantee and nothing wider: *"When rounds run
   out with findings still gating, or nobody answered: `auto` (the default) — the gate raises no new hold and never
   waits for a person; a round nobody answered proceeds as `not_reviewed`, spent rounds as `good_enough`, each logged
   and noted on the round, and your AI may still ask you a question its consultants could not settle; `human` — stop
   and ask you; `continue` as-is (said out loud); `good_enough` — apply what is true, reject the rest with reasons;
   `escalate` the ladder (reviewer effort → reviewer model → arbiter model)."* Add `enumDescriptions` in the same
   order.
3. **The labels and the select** (C2). New `src/onExhaustedPanel.ts` (pure):
   - `ON_EXHAUSTED_CHOICES` as in C2.
   - `labelOf(value: OnExhausted): string`.
   - `workspacePolicyOf(inspected: { workspaceValue?: unknown; workspaceFolderValue?: unknown }, overlayHolds: boolean): OnExhausted | undefined`.
     It answers the folder value, else the workspace value, when that value is a known choice other than `auto`,
     the layer is the one in force, and this side's overlay does not hold the key (`overlayHolds` false). In every
     other case it answers `undefined`. This side's overlay wins over a workspace value (`sideSettings.ts:66-73`).
   - `gateNotes(value: OnExhausted, serverFeatures: readonly string[] | undefined, workspace: OnExhausted | undefined): readonly string[]`:
     at most two plain-text sentences, in this order.
     - **predates:** when `value === 'auto'`, `serverFeatures !== undefined` and `!serverFeatures.includes(FEATURES.onExhaustedAuto)`:
       *"This coai-mcp predates Auto: it runs Good enough and still asks a person when no reviewer answers — update it
       in Setup › MCP server."*
     - **workspace:** when `workspace !== undefined`: *"This workspace sets When the rounds run out to {labelOf(workspace)}
       in its .vscode/settings.json, which wins over the choice above. Remove it there to use your own."*

   `panelView.ts` `gateBody` draws the options from `ON_EXHAUSTED_CHOICES`: the same `option value / selected / label`
   markup, with each label passed through `escapeHtml`. Under the select, each note is
   `<div class="stale">${escapeHtml(note)}</div>`, the precedent `stopLocalSkew` sets (`:1677-1681`). The Auto label
   carries the parent's words as the option text: *"Auto — the gate raises no new hold and never waits for a person;
   it logs and moves on"*. So C2's `label` for `auto` is that full sentence. `labelOf('auto')` answers the word
   `Auto` for the workspace sentence: keep a `short` field or a separate map, and the coder chooses. `PanelState`
   gains `readonly workspacePolicy?: OnExhausted | undefined`. `panelProvider.ts` sets it beside `serverFeatures`
   (`:1489`):
   `workspacePolicy: workspacePolicyOf(config.inspect('onExhausted') ?? {}, this.perSide(config) && 'onExhausted' in readOverlay(this.context.globalState, thisSide(this.context.globalStorageUri)))`.
   Use the imports `panelProvider.ts` already has where they exist.
4. **The feature.** `binaryFeatures.ts` `FEATURES` gains
   `/** E1 of the auto plan: the policy auto, the meaning of an absent COAI_ON_EXHAUSTED. */ onExhaustedAuto: 'onExhaustedAuto'`.
   `seam-features.mjs` then asserts that the real binary lists it, with no edit. `FeaturesCache` gains
   `settled(): readonly string[] | undefined { return this.last.key === '' ? undefined : this.last.features; }`.
   `known()` stays for its other readers.
5. **The six cells** (C3). New `src/onExhaustedWire.ts` (pure):

   ```ts
   /** The parent § 4.6 table: what the settings file says about When the rounds run out, for the binary it meets. */
   export function onExhaustedEnv(value: OnExhausted, features: readonly string[], featuresSettled: boolean): Record<string, string>
   ```

   | `value` | lists `onExhaustedAuto` | settled WITHOUT it (incl. a 64) | not settled |
   |---|---|---|---|
   | `auto` | `{}` | `{ COAI_ON_EXHAUSTED: 'good_enough' }` | `{}` |
   | any other | `{ COAI_ON_EXHAUSTED: value }` | the same | the same |

   In `envBlock`, replace `:551-553` with `Object.assign(env, onExhaustedEnv(settings.onExhausted, features, featuresSettled));`.
   Add the sixth parameter, `/** Whether `features` is the binary's SETTLED answer — empty then means an older binary, not "not asked yet". */ featuresSettled = false`.
   `serverSettingsJsonWith` passes `write.featuresSettled`. `SettingsWrite` gains the required
   `readonly featuresSettled: boolean`. `serverSettingsJson` passes `false`. `ServerSettingsSync`'s `features` getter
   becomes `() => readonly string[] | undefined` (default `() => undefined`). `contentNow` passes
   `features: answer ?? []` and `featuresSettled: answer !== undefined`. `extension.ts:434` becomes
   `() => features.settled(),`.
   - Every other caller of `serverSettingsJsonWith` (§ 2: four seam scripts, `providersAnswer.contract.ts`,
     `run-seam.mjs` three times, `seam-round-counts-merged.mjs`, `seam-ledger-fields.mjs`) states `featuresSettled`.
     Pass `true` where `features` is the real binary's answer, and `false` where it is a list the script chose
     (`[]`, `['fastMode']`). Against the current binary, which lists `onExhaustedAuto`, every file they write stays
     byte for byte as before. Say so in the PR.
   - `theMirrorWaitsForTheDataRoot.test.ts:30` passes `() => []` as the getter. That now means "settled, nothing
     listed". Change it to `() => undefined`, the old meaning, and note it in the PR.
   - Fix the stale sentence in `serverSettingsFile.ts:20-21` ("`envBlock` also builds the block a person pastes"). It
     has not built one since `a0eee1ca`. Say that the pasted block carries only `serverEnv()`.
6. **The held-gate leg** (`scripts/seam-held-gate.mjs:108-124`). **The pinned `human` stays.** The leg proves that a
   HELD gate's notice, answer window and release work across the two implementations, and only a policy that asks a
   person holds. Under `auto` the leg would have nothing to read. Its home moves from the process env to the panel's
   own writer: `const settings = { ...DEFAULTS, onExhausted: 'human', thresholds: … }`, with `COAI_ON_EXHAUSTED` taken
   out of `start({...})`. The comment becomes: *"Ask a human, chosen through the panel's own writer. Since policy auto
   is the default, the writer writes `human` (it differs), and this leg is about a HELD gate."* Three reasons. After
   S1 the old comment is false: the writer no longer leaves the policy out. Through the writer, the leg also proves
   that a person's explicit Ask a human reaches the real server as `human` (the table's second row). And the env pin
   now draws E1's pinned-policy startup note into every run of the leg.
7. **The compat script** (§ 8 PR 2 step 5). Write `scripts/live-on-exhausted-compat.mjs` and the package script
   `"test:on-exhausted-compat": "npm run compile && node scripts/live-on-exhausted-compat.mjs"`, in the shape of
   `live-command-models-compat.mjs`: `startOwnedRun`, `releasedServer(work, flag('tag', ''))`, `newServer()`, and every
   `COAI_*` removed from the process env.
   - **The control.** Ask the old binary `--features` through the extension's own `readBinaryFeatures`
     (`out/binaryFeatures.js`), with a `Run` over `releasedHalves`' `run`. If it lists `onExhaustedAuto`, print
     "the released server already knows Auto; pass --tag=mcp-v0.65.4 or older. No result" and exit 2. Then the old
     binary's `--providers` over a file holding `COAI_ON_EXHAUSTED=auto` must list an unrecognised `COAI_ON_EXHAUSTED`
     (`WhyExhausted`). Without that, nothing below can see the key.
   - **Old.** The file is written by the extension's own `serverSettingsJsonWith(DEFAULTS, vendorsFrom([]), { …, features: old.features, featuresSettled: true })`.
     It holds `COAI_ON_EXHAUSTED: "good_enough"`, and the old binary's `--providers` lists nothing about
     `COAI_ON_EXHAUSTED`.
   - **New.** The same writer with the new binary's answer writes no `COAI_ON_EXHAUSTED`, and the new binary's
     `--providers` lists nothing unrecognised.

   Print a JSON summary: both versions, the old binary's sha256, and the three results. It is not in CI, like every
   `*-compat` script. Run it once and paste the output into the PR.

### 7.2 Scenarios — RED first

**`onExhaustedWire.test.ts`** (new, pure):

| # | Test | Expected | Break-it |
|---|---|---|---|
| 1 | `EveryCellOfTheTable` — for every value of `ON_EXHAUSTED_CHOICES` (derived, never retyped) × `{lists: ['onExhaustedAuto'], without: [], withoutButOthers: ['fastMode'], unsettled: [] with settled false}` | the table of § 7.1 item 5 | — |
| 2 | `AnUnsettledAnswerIsNotAnOlderServer` | `auto`, `[]`, `false` → `{}` | drop the `featuresSettled` test → `good_enough` |
| 3 | `AnOlderServerIsToldGoodEnough_NeverAuto` | `auto`, `[]`, `true` → `good_enough` (a 64 settles as `[]`) | return `{}` for `auto` always |
| 4 | `AskAHumanCrosses_NowThatItIsNotTheDefault` | `human` in every column → `human` (N4) | — |

**`envBlock` / the file / the sync** (extend `settingsShape.test.ts`, `settingsSync.test.ts`; follow their fixtures):

| # | Test | Expected |
|---|---|---|
| 5 | `APristinePanelWritesNoPolicy` | `envBlock(DEFAULTS)` has no `COAI_ON_EXHAUSTED`; `envBlock(DEFAULTS, DEFAULT_VENDORS, '', undefined, [], true)` has `good_enough` |
| 6 | `TheFileForASettledOlderBinarySaysGoodEnough` | `serverSettingsJsonWith(DEFAULTS, [], { …, features: [], featuresSettled: true })` parses to `{ COAI_ON_EXHAUSTED: 'good_enough', … }`; `serverSettingsJson(DEFAULTS, [])` has none |
| 7 | `TheSyncRewritesTheFileWhenTheAnswerSettles` | a `ServerSettingsSync` whose getter answers `undefined`, then `[]`, then `['onExhaustedAuto']`: the written files carry no key, then `good_enough`, then no key. Break-it: `contentNow` passing `featuresSettled: true` always → the first write has `good_enough` |
| 8 | `FeaturesCacheTellsUnsettledFromEmpty` (in the binary-features tests) | `settled()` is `undefined` before an answer; `[]` after a run answering 64; `undefined` again for another file (`forgetAnotherFile`) |

**`settingsShape.test.ts`**: `:38` expects `'auto'`, the one expectation that encoded the old default. Add:
`settingsFrom(reader({ onExhausted: 'auto' })).onExhausted === 'auto'`. The manifest's enum equals
`ON_EXHAUSTED_CHOICES.map((c) => c.value)` in order, read from `package.json` as `:75-82` reads the default. Keep the
existing `'panic'` → `DEFAULTS.onExhausted` (it now means `auto`, § 10, Q-b).

**`panelServerDefaultsAgreement.test.ts`** (one new test, `the policy the panel shows by default is the one the server runs with nothing named`):
read `PanelSettings.cs` and assert it matches `/null or "" or "auto" => null/`, failing with *"PolicyOf no longer reads
absent, empty and auto as no policy named — the shape this test reads has changed"*. Read `SessionState.cs` and
assert `/Autonomous = OnExhausted is null;/` (no policy named is policy auto). Then
`DEFAULTS.onExhausted === 'auto'`, and `envBlock(DEFAULTS)` writes no `COAI_ON_EXHAUSTED`. Break-it: set `DEFAULTS`
back to `'human'` → red with *"the panel shows … the server runs auto"*.

**The gate section** (`theGateSaysAuto.test.ts`, new, `runPanel(panelState('gate', …))`):

| # | Test | Setup | Expected |
|---|---|---|---|
| 9 | `AutoIsFirstAndDrawnForAPristinePanel` | `DEFAULTS` | the `onExhausted` control's value is `auto`; the first option's value is `auto` |
| 10 | `PickingAutoWritesAuto` | `{ ...DEFAULTS, onExhausted: 'human' }`, `picker.value = 'auto'`, `fire('change')` | `lastWrite(page)` names `onExhausted` with `auto` (as `refusedSelect.test.ts:24-30`) |
| 11 | `AnOlderBinaryIsNamed` | `serverFeatures: []` | `page.html` contains "predates Auto" once; with `serverFeatures: undefined`, `['onExhaustedAuto']`, or `[]` with `onExhausted: 'human'` → absent |
| 12 | `AWorkspaceValueIsNamed` | `workspacePolicy: 'human'` | "This workspace sets When the rounds run out to Ask a human" once; `undefined` → absent |
| 13 | `AHostileLabelCannotReachThePage` | — | a `workspacePolicyOf` given `'<b>'` answers `undefined` (only known choices pass) |

Break-it for #11: remove the `gateNotes` call from `gateBody` → #11 and #12 are red.

**`onExhaustedPanel.test.ts`** (pure): `workspacePolicyOf` — the folder value wins over the workspace value; `auto`,
an unknown word, or `overlayHolds` → `undefined`; `gateNotes` gives at most two notes, predates first.

**Seam:** `npm run test:seam` is green. The features leg now checks `onExhaustedAuto`, and the held-gate leg passes
with the pin moved. **Must stay green unchanged:** `install.test.ts:947-973` and
`theDirectoryAPersonChose.test.ts:197`. Name both in the PR as the reason no copy-action change exists (R1).

**`research/module_tests.md`**: one row, "the settings file says what the binary it meets will run", naming tests 1–8
and the compat script.

## 8. E2.S2 — the one-time migration · `model: fable` (CORNERSTONE)

Scenarios and brief: **Fable**, in `todo/PLAN_auto_never_asks_a_person_E2_S2_spec.md`. This section is the boundary
only.

**What it builds.** The parent § 4.6's *one-time migration*, N5–N8, and § 8 PR 2 step 2. That is done-when 3 and the
notification half of done-when 4. **Outputs S1 and S3 rely on:** C4–C6 and nothing else. **What S2 does not do:** no
change to the value, the select, the wire, the gate-section lines (S1), or the help and the CHANGELOG (S3). It never
edits a workspace or folder value. That is the owner's decision on Q1 (2026-11-17, "workspace не трогаем"), and S1's
gate line names such a value.

**Inputs the spec must settle**, each named because the code makes it a trap:

1. **N5 — the reach.** The overlays are `coai.settingsOverlay@<sideKey>` records in the client's `globalState`, shared
   by every window of the profile (`coaiInstall.ts:345-363`). There are two ways to find them. One is the side
   registry (`sidesSeen.ts`), which prunes a side unseen for 90 days. The other is a prefix walk over `Memento.keys()`
   (`sidesSeen.ts:214`), which reaches a pruned side's overlay too. The spec chooses one and says why. It also covers
   a slot that holds a non-record (`readOverlay` reads it as `{}`, `sideSettings.ts:24-28`), and how a key is REMOVED
   from a record: the read-modify-write of `writeOverlay` (`:33-41`). Another side's server settings file is rewritten
   only when a window of THAT side next mirrors it. That window may run an OLDER extension: extensions install per
   remote, and the old manifest's default is `human`, which writes nothing. The spec writes down what each side's
   server then runs.
2. **N6 — two windows at once.** Nothing coordinates across sides today. `underSettingsLock` is an O_EXCL file in ONE
   side's data directory (`extension.ts:1172-1210`), and a WSL window and a Windows window have different ones.
   `globalState.update` is last-writer-wins with no compare-and-set. The catalog queue (`CatalogTurns`,
   `catalogMigrationHost.ts:32`) serialises one extension host only. The spec names the coordination and the test
   that holds window B after its read while A finishes and the person chooses a value: one result, one notification.
3. **N7 — the mirror.** The user-layer clear fires `onDidChangeConfiguration`, and the activation listener mirrors.
   An overlay clear fires nothing. So the run calls `mirror` itself once every clear has succeeded and BEFORE it writes
   its marker. That is the catalog migration's precedent (`extension.ts:454-458`).
4. **N8 — record before clear.** A resumed run must keep a choice made after the interruption. Comparing values alone
   cannot do it: a location recorded as `human`, cleared, and then set to `human` again by the person reads exactly as
   "not yet cleared". So the record carries per-location progress, persisted after each clear. The test interrupts after
   the first clear, sets `human`, resumes, and finds `human` kept.
5. **N9 (new, found here) — Settings Sync.** The user layer is synced across machines. `globalState`, which holds the
   marker, is not: nothing here calls `setKeysForSync` (`sidesSeen.ts:13`, `cliPathKeep.ts:18`). Machine A migrates,
   and the person then chooses Ask a human there. Settings Sync carries `human` to machine B. B's first activation of
   the new version finds no marker and clears it, then syncs the removal back to A. That is a choice made after the
   migration, reset. The spec settles it. Options include syncing the marker key with `setKeysForSync`, or a record
   that rides with the synced value. The spec writes down what remains (sync timing).
6. **Marker semantics.** The value is the version that completed the run. The spec says what a marker from an older
   or a newer version means. A failed clear writes no marker, so the run is retried at the next activation. The spec
   says whether the person is told, and how often.
7. **The one notification.** It is non-modal and shown exactly once per completed migration, also with two windows
   and with a resumed run. It is a new `notify` call (census, § 3), and the spec names its `source` and `code`. The
   spec also says whether a FRESH install, which never ran the old default, is told, and how a fresh install is told
   apart from an upgrade. The text is C6's.
8. **Adoption without the panel.** The run starts from `activate` (`onStartupFinished`), never from the webview's
   resolve. It is tested with no panel ever opened, with a fake `Memento` that has `keys()` and a fake configuration
   that has `inspect` and `update`, as the catalog migration's tests do. The spec says whether a `test:host` scenario
   is added. That job cannot start on this machine, and CI runs it with `continue-on-error`.
9. **Two writers of one overlay record in one host.** The catalog migration rewrites this side's overlay record
   (`catalogMigrationHost.ts`, `applyWrites`) at the same activation. Both are read-modify-writes of the same
   `globalState` key, so one can undo the other's change. The spec orders them, for example inside `CatalogTurns`, or
   after the catalog run.
10. **The workspace value.** Q1 is the owner's decision: not edited. A test asserts that `workspaceValue` and
    `workspaceFolderValue` are unchanged after the run. The spec says whether the notification mentions a workspace
    value in force. The gate section already names it (S1).
11. **What a failure leaves.** `config.update` can reject (a read-only `settings.json`), and so can a `globalState`
    write. No marker is written, the next activation tries again, and the residual is written down.

**Files it touches** (C4; § 6 has who owns which lines): the new `src/onExhaustedMigration.ts` (pure) and
`src/onExhaustedMigrationHost.ts` (host, added to `sonarcloud.yml`); one call in `extension.ts` after `:438`;
`notification-sites.json`; its tests; its row in `research/module_tests.md`.

## 9. E2.S3 — what a person reads · Opus brief, Sonnet code

**What it builds.** The page item of parent § 4.5 and § 4.8; § 8 PR 2 steps 3 and 4 (the help and the docs).
Done-when 4 (the help), 5 and 6.

### 9.1 The page — may run in parallel (§ 6)

In `roundsLog.ts`'s page script, `detail(row)` (`:1783-1803`): a non-skipped row with a note shows it, under the
"asked by" lines and above the reviewers, in both non-skipped branches (reviewers present, or none recorded):

```js
var noted = row.note ? '<div class="reviewer roundnote">' + esc(row.note) + '</div>' : '';
```

Prepend `noted` after `asked` in the two returns that follow the skip branch. The skip branch is unchanged: it already
says the note, and saying it twice is a defect. Then `LogRow.note`'s summary (`:88-91`) says: *"Why a SKIPPED round did
not run, with `×N` when one row stands for N skips; or what policy auto decided for a round that ran — its reason, or
that it released a hold an older build had raised. Empty otherwise, and for every conversation (`noteOf`)."* The
verdict cell (`:1907`) and the verdict facet (`:1076-1086`, drawn from the rows) need no change: `not_reviewed` is a
raw string like every verdict. The test proves it.

**`roundsLogNotes.test.ts`** (new; `runningPage` / `runningDomPage` from `roundsLogPageHarness.ts`; sessions built
from `SESSION` with `STARTED`/`COMPLETED`):

| # | Test | Session | Expected |
|---|---|---|---|
| 1 | `AnUnreviewedRound_ShowsWhyWhenOpened` | a plan round, `verdict: 'not_reviewed'`, `note: 'policy auto: no reviewer answered — nothing was reviewed, and the stage proceeds without a review. codex: exit 1'` | open it (`click(clickInRow(…))`): the rows' html contains `class="reviewer roundnote"` and "no reviewer answered" |
| 2 | `AReleasedHold_SaysItWasReleased` | a code round, `verdict: 'call_human'`, `note: 'released by policy auto: this hold was raised by a build before auto'` | opened: the note is shown |
| 3 | `ARoundWithNoNote_DrawsNoNoteElement` | `SESSION` as is | opened: no `roundnote` |
| 4 | `ASkip_StillSaysItsReasonOnce` | the skipped fixture of `bundledPage.test.ts:143-162` | "no vendor is ticked to review features ×3" appears once; no `roundnote` |
| 5 | `ANoteIsEscaped` | `note: '<img src=x onerror=alert(1)>'` | opened html contains `&lt;img` and not `<img src=x` |
| 6 | `NotReviewedFiltersLikeAnyVerdict` | `runningDomPage` over a `proceed` round and a `not_reviewed` round | the `[data-filter="verdict"]` select offers `not_reviewed`; set its `value` to `not_reviewed` and `dispatch('change')`; the rows body holds one row, and it is the `not_reviewed` one |

Break-it for #1 and #2: remove `noted` from the two returns → both red; #3–#5 stay green. Break-it for #6: none, it
guards behaviour that already exists. Say so in the summary (`testing.md`, *A test that passed the first time*):
check once that the row really carries `not_reviewed` by asserting the verdict cell text before filtering.

### 9.2 The help, in five languages

- **`help.ts:163-164`** (`onExhausted` tooltip, English). Auto first: *"What happens when the rounds run out and
  findings still gate, or nobody answered. Auto (the default): the gate raises no new hold and never waits for you — a
  round nobody answered proceeds as `not_reviewed`, which is never a pass, and spent rounds as good enough; each is
  logged and noted on the round and the rounds log, and your AI may still ask you a question its consultants could not
  settle."* Then the other four as today. Never "never asks you anything".
- **`helpContent.ts`, article `the-gate`** (`:138-155`). In `setup`, the defaults sentence ends "… dealing off, and
  *Auto*". `usage` opens with Auto, in the words above, then *Ask a human* ("the gate stops and puts the decision in
  front of you — the way to keep a person in the loop"), and the other three as today. `whatCanGoWrong` gains three
  sentences:
  - A `coai.onExhausted` in a workspace's `.vscode/settings.json` wins over your choice and is never changed by the
    extension. **Reviews › The gate** names it.
  - A coai-mcp older than 0.66.0 does not know Auto: the panel sends Good enough, which still asks a person when nobody
    answered, and The gate says "predates Auto".
  - A `COAI_ON_EXHAUSTED` in your MCP client's `env` block wins over the panel. The server's startup notes name it.

  Then the one-time reset: *"A value chosen before this version is reset to Auto once, with one notification; choose
  again afterwards and it is kept."*
- **`helpDe/Es/Ru/Uk.ts`**, the same article: the same content translated. Control labels stay English, as everywhere
  in these files (`Auto`, `Ask a human`, `not_reviewed`, `.vscode/settings.json`).
- **`onExhaustedHelp.test.ts`** (new, the `kimiHelp.test.ts` shape). For every `HELP_LANGUAGES`, `bodyFor(the-gate)`
  is not a fallback, and its joined text contains `*Auto*`, `not_reviewed`, `.vscode/settings.json` and `0.66.0`. The
  English text contains "raises no new hold and never waits". No language contains "never asks you anything". The
  `help.ts` tooltip contains `Auto` and `not_reviewed`. `helpCoverage.test.ts`, `helpTooltips.test.ts` and
  `theHelpNamesTheSectionsAsTheyAre.test.ts` stay green.

### 9.3 The documents

- **`README.md` (root) § *When the rounds run out*** (`:165-171`): "Five answers", **Auto** first, in requirement 5's
  words, then the four as today.
- **`src_vs_code/README.md:44-45`**: the same five, one line.
- **`ARCHITECTURE.md:80-82`**: the five answers, and the defaults as they are, read from `DEFAULTS`: one round per
  role, two for the feature review, threshold 6 for the plan and 5 for every other role. Not "plan 3 rounds at 2;
  code 2 at 3".
- **`research/module_extension.md`**: one section, *Policy Auto in the panel (E2 of the auto plan)*, placed after
  *How settings reach the server* (`:4768`). It covers:
  - the value and its labels (C1, C2);
  - the six cells and why "not settled" writes nothing (every settled answer rewrites the file);
  - why the copied block carries no policy (R1);
  - the two gate-section lines;
  - the migration as S2 built it: reach, coordination, marker, notification, and the residuals its spec wrote down;
  - the rounds-log note.

  Fix the sentence at `:7261` only if it now reads wrong.

### 9.4 The CHANGELOG *Extension* entry

Written in the release pull request, as every versioned section is (`release-please-config.json` `$order`; E1 did the
same). S3 drafts it in the E2 PR's description in the was/now style of `## Extension 0.31.6` (`CHANGELOG.md:3642`):

| | was | now |
|---|---|---|
| *When the rounds run out*, default | Ask a human | **Auto** |
| A value you chose (your settings, each side's own settings) | kept | **reset to Auto once**, with one notification; a choice made afterwards is kept |
| A value in a workspace's `.vscode/settings.json` | in force | in force, never changed, and named in Reviews › The gate |
| Ask a human chosen in the panel | written as nothing (it was the default), so Server 0.66.0 ran Auto | written as `human` and honoured |
| A coai-mcp older than 0.66.0 | — | sent Good enough, and The gate says "predates Auto" |
| The rounds log | a note only on a skipped round | the note of every round, `not_reviewed` among the verdicts |

Then four plain sentences:
- requirement 5's guarantee;
- "If you put `COAI_ON_EXHAUSTED=human` in your MCP client's env block, as the Server 0.66.0 note suggested, choose
  Ask a human in the panel and remove it there";
- the pairing ("The MCP server to pair it with is `coai-mcp` 0.66.0");
- the rollback ("0.87.x shows Ask a human but writes nothing, so a 0.66.0 server runs Auto for it").

### 9.5 Scenarios' home and the module-tests row

`research/module_tests.md`: one row for the page note (tests 1–6 of § 9.1). The help test is coverage, not a flow, so
it gets no row.

## 10. Reported to the coordinator

- **R1 — the copy action (recorded by the coordinator as the parent's entry 2.1, 2026-11-17).** The parent's § 4.6, § 12 finding 3, E2 done-when 2
  ("a block is handed out only after the features settled, else with `COAI_ON_EXHAUSTED=auto` written out") and E2.S1
  ("the copy action") rest on a premise the code does not hold: no block the panel hands out carries a policy (§ 2.1).
  Building the step would add a pinned key to a client entry. That breaks two guards, and it silently freezes the
  policy for that client: a later Ask a human in the panel would lose to it, and E1's pinned-policy note does not fire
  for `auto`. This document does not build it. Proposed entry, the FIRST of epoch 2:
  `- 2.1 · 2026-11-17 · agent · epics, approach — the copied MCP block carries only COAI_DATA_DIR and COAI_DATA_SIDE (since a0eee1ca; install.test.ts and theDirectoryAPersonChose.test.ts), so no pasted block pins a policy: E2 adds no wait for features and no COAI_ON_EXHAUSTED to it, and done-when 2's second clause becomes "the copied block carries no COAI_ON_EXHAUSTED"`.
  The coordinator verified the premise (`extension.ts` passes `serverEnv()` only; `install.test.ts` guards it) and
  recorded it. Epoch 2 now holds one change: a second one, from the S2 spec or anywhere else, is a full stop.
- **Q-a — the extension release.** `main` holds #1015 (the terminal-chat E3), merged after Extension 0.87.0 and not
  released. The memory says its VSIX is with the owner for hand checks before `review_feature` and 0.88.0. E2's
  `extension-v*` release would ship #1015 too. Is the terminal-chat release cut first, or does E2's release carry both?
- **Q-b — a junk stored word.** `asOnExhausted` maps an unknown stored value to `DEFAULTS`, so after the flip a typo
  such as `"humna"` in `settings.json` reads as `auto`. Before, it read as `human`. The parent's requirement 9 ("an
  unknown policy word still stops") is about the server, and the panel never sends a word it does not know. S1 keeps
  the parent's literal (`asOnExhausted` falls back to the default, as every other malformed setting). Failing closed
  instead (unknown → `human`, then written) would be a change. The coordinator, 2026-11-17: kept as written — the panel
  then writes no key and the server runs `auto` too, so panel and server agree, and VS Code itself flags a value
  outside the enum.
- **N9 for the spec.** Settings Sync can reset a choice made after the migration on another machine (§ 8, input 5).
  Its answer may change the parent's "once per machine". If R1 is recorded as 2.1, any change the spec reports becomes
  2.2, which is a full stop. Consider settling both before recording either.
- **Stale status.** The E1 stories document's status still says "Not yet merged or released"; #1011 is merged and
  mcp 0.66.0 released. Not edited here.

Settled here as fixes that satisfy the parent as written (not changes):
- N4, by S1's second row of the table;
- the CHANGELOG draft in the PR description (E1's precedent);
- the held-gate pin kept `human`, moved to the panel's writer;
- `featuresSettled` as the way the "settled without" column is told apart from "not settled": the parent's "fifth
  `envBlock` parameter" is a plain list that cannot tell the two apart;
- the stale `serverSettingsFile.ts:20-21` sentence.

## 11. Definition of Done for E2

- [ ] S1: the type, `DEFAULTS`, the manifest (enum, default, description) and `asOnExhausted` agree on five values,
      Auto first; the defaults-agreement test reads the C# (parent done-when 1).
- [ ] S1: each of the six env cells has a test; "not settled" writes nothing and every settled answer rewrites the
      file; the copied block carries no `COAI_ON_EXHAUSTED` (the existing guards, R1); the compat script shows a new
      panel writing `good_enough` for mcp 0.65.4 and nothing for 0.66.0, with its control; the gate section says
      "predates Auto" (parent done-when 2).
- [ ] S1: the gate section names a workspace value in force; the held-gate seam leg passes with `human` chosen through
      the panel's writer; `npm run test:seam` and `test:contract` are green over a fresh Release build.
- [ ] S2: the spec — written by Fable before its code — built, and every scenario seen RED, GREEN and broken: the user
      value and every overlay cleared once, the marker written only after every clear and the mirror, a resumed run
      that keeps a later choice, two windows → one result and one notification, a second activation doing nothing, a
      workspace value untouched (the owner's Q1), N9 settled, all without the panel (parent done-when 3).
- [ ] The notification and the help state requirement 5's guarantee and nothing wider; no text says "never asks you
      anything" (parent done-when 4).
- [ ] The rounds-log page shows the note on a non-skipped row and filters `not_reviewed`, tested by running the page
      (parent done-when 5).
- [ ] Help in five languages; the READMEs, `ARCHITECTURE.md` and `research/module_extension.md`; the CHANGELOG
      *Extension* draft says a chosen value is reset to Auto once (parent done-when 6).
- [ ] `npm run lint` and `npm test` green; the suppression counts not grown; `sonarExclusions.test.ts` and the
      notification census current; `plan-lifecycle.mjs` clean; every test whose expectation changed is listed in the
      PR with its reason.
- [ ] Then the epic's code review, merge, and `extension-v*` release — after Q-a is answered — reported as
      committed / built / released / running.
