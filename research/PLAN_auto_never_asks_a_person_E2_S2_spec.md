# PLAN — E2.S2 spec: the one-time migration — every saved choice reset to `auto` once, and a later choice kept (CORNERSTONE)

> Status: **IMPLEMENTED, 2026-11-19** — the E2.S2 cornerstone spec, built in #1018 (extension 0.89.0); promoted with its plan. Before that: **built 2026-11-17 in PR #1018 (feat/auto-mode-e2), with the Opus review's fixes: a copy that lost its record ends `incomplete` and writes no marker (T36b), the witness is withdrawn when the record is lost or the claim is taken over during its write without another run completing, and the notification is true for the server the window runs. Not yet merged or released.** Before that: **spec only, nothing coded — written 2026-11-17 by Fable before its code, and revised the same day by Fable
> to epoch 3 of the parent (the re-review after entry 2.2; approved by the owner, narrowed).** Cornerstone spec of story
> E2.S2 of [PLAN_auto_never_asks_a_person_E2.md](PLAN_auto_never_asks_a_person_E2.md) (its § 8, and § 10.1 — the
> sixteen items this revision follows; parent [PLAN_auto_never_asks_a_person.md](PLAN_auto_never_asks_a_person.md),
> § 4.6 *the one-time migration* as rewritten, requirements 5 and 7, § 5, § 9, § 11 C4 and C5, § 13 item 6, `## Epics`
> § E2 done-when 3 and 4, notes N5–N11, § 10 Q1, § 8 PR 2 step 2). It keeps no change ledger: a change is an entry in
> the parent, and the parent's epoch 3 holds one, entry 3.1 — the user layer's witness is one fixed word on every
> machine, and the read-back after the await is the value test alone. The coordinator recorded it from this spec's U10,
> and it is built here. **This spec makes no other change to the parent.** Where the parent cannot
> be met as written, or something was found that the parent does not hold, the item is in *Reported to the
> coordinator* at the end, and what is built is the parent as written. It still settles the finding the plan gate on E2
> accepted on 2026-11-17 (codex, Major: a host that dies between a clear and its progress record) — D7, T14 and T15.
>
> Scope: `src_vs_code` only. NEW `src/onExhaustedMigration.ts` (pure: the record, the decisions, the run),
> `src/onExhaustedTold.ts` (pure: the sentences and which one a window is shown) and
> `src/onExhaustedMigrationHost.ts` (host); one manifest property, `coai.onExhaustedResetToAuto`; one call and one
> test-mode export in `src/extension.ts`; one row in `src/configTransfer.ts`; one English sentence in
> `src/helpContent.ts` and one alias in `src/test/helpCoverage.test.ts`; the host module in
> `.github/workflows/sonarcloud.yml`; `notification-sites.json` regenerated; four test files, one fakes file, one
> real-editor scenario; one row in `research/module_tests.md`. No change to the value, the select, the wire, the reader
> or the gate-section lines (S1), to the help's articles or the CHANGELOG (S3), to `src_mcp`, or to a workspace or
> folder value (Q1, the owner's decision).
> Words a person reads: the parent's notification in its two forms and the warning for a settings file that refuses
> the change (§ S2.5), word for word; and three short texts the parent does not hold, listed for the coordinator (U16).
> Coder: Sonnet. Review: Opus. Read at `8067b162`; S1's fix forward landed on the branch while this was written
> (`b0095546`), and `modelKeys.ts` is cited from there. Paths are from the repository root unless they start with
> `src/`, which is `src_vs_code/src/`.

## What changed against `998a2a39` (this spec as first committed)

The parent's sort (its § *Re-review for epoch 3*) kept the reach, the overlay witness, the record before the clear, the
turns and the scenarios, and cut five things: D8, `userBegun` in `globalState`, D11's single sentence, the claim as
the coordination (D4), and § S2.6's wake by the user clear. Each row is one item of the stories document's § 10.1.

| § 10.1 | What changed | Where here |
|---|---|---|
| 1 | The status; X1–X7 are made; U1 is settled by entry 2.2 | the header; the next section |
| 2 | Facts added: V1–V3 verified; the listener mirrors on any `coai` change; `config.update` of an unregistered key is refused — read in VS Code's source AND observed here | § S2.0: F9, F17–F21, V5–V9 |
| 3 | The claim is a best effort. No value of the record crosses an `await`. The marker is written only from a copy where every location is settled, with no `await` between the check and the write | D4, D10; § S2.4 steps 3, 5 and 6 |
| 4 | The user layer's witness is the SETTING `coai.onExhaustedResetToAuto`: always written, the value read again after the await, withdrawn after a refused removal. `userBegun` is gone from the record | D7; § S2.2; § S2.4 step 4 |
| 5 | D8 replaced: Settings Sync is closed by the witness, within the parent's residuals | D8; § S2.7 R1 |
| 6 | The notification has two forms, chosen from the value the window runs, at the moment it tells | D11; § S2.3 (`onExhaustedTold.ts`) |
| 7 | `NOT_RESET` also when the witness write is refused | D12 |
| 8 | Ready means: the registry knows the witness setting. The `defaultValue === 'auto'` test is dropped (it follows) | D13 |
| 9 | The user-layer port reads and writes the witness too, Global target only | D15; § S2.3 |
| 10 | The ports for the witness; `tell` takes the chosen sentence | § S2.3; § S2.5 |
| 11 | Another side's file: its next mirror after its copy holds the change, its next activation at the latest | § S2.6 |
| 12 | R1–R4 rewritten; R5, R6, R9, R10 stay; R7 and R8 stay too; R11–R14 are new | § S2.7 |
| 13 | Scenarios (a)–(g) are T32–T38; T14–T16, T23 and T31 changed | § S2.8 |
| 14 | Files: `package.json`, the alias, the help sentence | § S2.9 |
| 15 | The reviewer's checks | § S2.10 |
| 16 | The DoD; where each of U1–U9 landed | the last two sections |

Changed beyond the list, each a mechanism for something the list or the parent states, and each reported (U14, U15, U19):
the plan records a witnessed location too, so the witness is asked in one place (D6); the overlays are cleared by a
loop that picks from a fresh read at every turn (D9); `after` is called on every way out of a run that took the claim
(D5); the marker is written as a new record with nothing carried into it (D10); the sentences live in a module of
their own; the notice's code is `on-exhausted-migrated` (it has two sentences now); T3, T5, T6, T9, T10 and T17–T20
follow their decisions; T39–T42 are new; a config file never carries the witness (D19); a refusal that names an
unknown key is a wait, not a warning (D13).

**After the coordinator's decisions on U10–U20 (2026-11-17).** The parent's entry 3.1 cured U10: the witness is one
fixed word, and after the awaited write only the value is tested. Changed here for it: D7 and D8; the witness in
§ S2.2; `USER_WITNESS_WORD` in place of `witnessText`, and `writeWitness()` with no argument (§ S2.3); step 4 and
order 3 (§ S2.4); R1 and R3 (§ S2.7); T6, T7, T19 and T31 (the word); T34, renamed and rebuilt on the value test; T41,
rebuilt on the value read again after the await; checks 2 and 3 of § S2.10. T14–T16 compared no witness text, so they
stand as they were. The count stays 42. What is still open is U21–U24.

## Changes to the stories document — NOT made here

The stories document asks for § 4, § 6 and § 9 to be edited in the same commit as a refinement. This spec's author was
told to edit no other file, so the edits are listed for the coordinator. X1–X7 of the first commit are made: C4–C6
carry X1–X3, § 6 carries X4 and X5, § 9.2 and § 9.4 carry X6, and the `todo/README.md` row exists (X7).

| # | Where | It says | This spec says | Why |
|---|---|---|---|---|
| X8 | § 10.1 item 8 | ready is "`inspect` of it is not `undefined`" | ready is "`inspect(key)?.defaultValue` is not `undefined`" — `unknownKeysOf` of `catalogMigrationRun.ts` | In an extension host `inspect` of an unknown key answers an object whose members are all `undefined` (V6). The signal this repository already holds to a real editor is the default (F18) |
| X9 | § 6, the file table | S2: two new modules; three test files | S2 also owns: new `src/onExhaustedTold.ts`; one row in `src/configTransfer.ts`; a fourth test file, `onExhaustedMigrationTwoMachines.test.ts` | D11, D19, T32–T34. None is a file S1 or S3 touches |
| X10 | § 4 C4, § 6 | `extension.ts:454-458` (the catalog callback), `:917-920` (the test-mode spread) | `:456-459` and `:919` at `8067b162` | reference repair |
| X11 | `todo/README.md` | the row of the first commit | the row in U20 | the text names D8 and U1 as open |

## S2.0 — What was read

Read at `8067b162` (line numbers from there; `modelKeys.ts` from `b0095546`).

| # | Fact | Where |
|---|---|---|
| F1 | An overlay is the record under `coai.settingsOverlay@<sideKey>` in the client's `globalState`. `overlayKeyOf(key)` builds the key | `src/coaiInstall.ts:353-363` |
| F2 | `readOverlay` reads a slot that is not a record as `{}`. `writeOverlay` is one `update` of `{ ...readOverlay(), [section]: value }`: a member it does not name is kept | `src/sideSettings.ts:24-28`, `:33-41`, `isRecord` (`:102`) |
| F3 | `seedIfEmpty` seeds only when the record has NO member; `seedOverlay` copies every overlaid key the reader answers. Since S1's fix forward the reader answers nothing for an unset `onExhausted`, so a new overlay no longer freezes a default | `src/sideSettings.ts:85`; `src/settingsShape.ts:406-416` |
| F4 | `overlaidReader` answers a section from the overlay only when the record has that own property. Nothing in `src/` walks the members of an overlay record | `src/settingsShape.ts:394-397`; a search for `readOverlay`, `overlayKey` and `settingsOverlay@` over `src/` |
| F5 | The side registry: `coai.sideSeen@<sideKey>`, pruned after 90 days, removed by **Forget**. An overlay is not removed with its registry record | `src/sidesSeen.ts:25`, `prunedKeys` (`:114`), `forgetSide` (`:261`) |
| F6 | A person's choice is written by `saveSetting`: into this side's overlay when the per-side switch is on, else into the user layer (`ConfigurationTarget.Global`). Never into a workspace | `src/sideConfig.ts:173-187` |
| F7 | `onExhausted` is read from the layers that HOLD a value — folder, then workspace, then user — never from the registry's default (S1's fix forward). This side's overlay wins over all of them, but only while `coai.perSideSettings` is on | `src/modelKeys.ts:49`, `:87`, `heldValue`; `src/sideConfig.ts:29-39`; `src/sideSettings.ts:66-73` |
| F8 | `mirrorSettings` returns `void`: it starts a schedule and does not wait for the file. Every activation calls it once | `src/extension.ts:969`; `:438` |
| F9 | The configuration listener mirrors and re-renders on ANY `coai` change, in every window that hears it — so a write of the witness setting wakes them as a removal does | `src/extension.ts:603-616` |
| F10 | The catalog migration: one queue per extension host (`CatalogTurns`); `inCatalogTurn(work)` takes one turn; this side's overlay is written by a fresh read-modify-write per key | `src/catalogMigrationHost.ts:34`, `:70-72`; `src/catalogTurns.ts` |
| F11 | Activation writes `globalState` beside the run: the first-seen record and this side's stamp | `src/extension.ts:445`, `:449` |
| F12 | `notify` records, then shows, and never waits for the person. A notice's `code` is a string literal at the call site | `src/notify.ts:247`; `src/notice.ts` |
| F13 | A module that reaches `vscode`, directly or through an import, must be in `sonar.coverage.exclusions`, and one that does not must not be | `src/test/sonarExclusions.test.ts:82` |
| F14 | A real-editor scenario reads this extension's `globalState` only through `CoaiExports`; test-only members are spread in under `ExtensionMode.Test` | `src/extension.ts:102`, `:919`; `src/test/host/sideSeenScenario.ts` |
| F15 | Observed 2026-10-09: VS Code updated the extension in place before it activated. The new code then ran in a window whose settings registry did not know the new version's keys, until a reload | `research/PLAN_catalog_migration_waits_for_its_settings.md` |
| F16 | Measured 2026-10-10: a `globalState` write from one side reaches an already-open window of the other side with no reload. The lag was NOT measured: 20 s and 23 s are upper bounds | `research/RESULTS_side_registry_reaches_the_other_window.md` |
| F17 | **Observed in that same window, 2026-10-09: the write of a key the registry did not know was refused** — `Unable to write to User Settings because coai.migratedFrom is not a registered configuration.`, from the operator's `notifications.jsonl` | the same plan, § *Symptom* |
| F18 | The signal this repository uses for "the registry knows a key": `inspect(key)?.defaultValue !== undefined`. `unknownKeysOf` asks it; `unregisteredKeyIn` reads VS Code's refusal. A real-editor scenario holds it to VS Code 1.141, for keys the manifest declares with NO default too | `src/catalogMigrationRun.ts:33-47`; `src/test/host/migrationKeysScenario.ts` |
| F19 | `coai.catalogMigration` is declared as a string with no default and no scope, described *"Written by ConnectOtherAIs, not by hand"* | `src_vs_code/package.json:994-1001` |
| F20 | Export config carries every declared setting whose user value differs from its default; Import config writes it. `NEVER_TRANSFERRED` keeps a key out, and it lists the catalog migration's marker for this reason: carried across, it would stop a migration the importer never declined | `src/configTransfer.ts:30-51`, `travels` |
| F21 | Every declared setting needs a `SETTING_ALIAS` whose words stand in the ENGLISH help | `src/test/helpCoverage.test.ts:408-410`, `:554-560` |

Read in VS Code's own source, `microsoft/vscode` branch `main`. V1–V4 on 2026-11-17 for the first commit; V1–V3 were
then verified by the coordinator and read again by the judge at the re-review (the parent § 11 C5, point (a)). V5–V9
on 2026-11-17 at commit `0f9b863a`, for this revision. None is pinned to the editor a person runs, and nothing below
was observed in an editor from this checkout unless the row says so.

| # | Fact | Where |
|---|---|---|
| V1 | `Memento.update` changes the window's own copy at once (an object is stored as its JSON clone), and schedules ONE send, 0 ms later, of the extension's WHOLE `globalState` object. So the client stores one value per extension, not one per key | `src/vs/workbench/api/common/extHostMemento.ts`, `update` and the scheduler; `mainThreadStorage.ts`, `$setValue` |
| V2 | When the client's value changes, every window REPLACES its whole copy with it. A write a window had not sent yet is dropped with no error, and its promise still resolves | `extHostMemento.ts`, the `onDidChangeStorage` listener |
| V3 | So two windows that write DIFFERENT keys within the lag can lose one of the writes, and a write built on a stale copy puts back every key that copy held. `await update()` proves nothing; only a later read does | V1 and V2 together |
| V4 | `setKeysForSync` registers keys per extension id AND version. Settings Sync carries the values in its *Extensions* resource, and applies them on another machine only when the extension installed there has the SAME version | `extHostMemento.ts`, `ExtensionGlobalMemento`; `src/vs/platform/userDataSync/common/extensionsSync.ts` |
| V5 | **`config.update` of a key the registry does not know is refused — when it writes a VALUE.** `validate` throws `ERROR_UNKNOWN_KEY` when the key is not among `configurationService.keys().default`, is no override identifier, and `operation.value !== undefined`. Removing a key (`undefined`) is NOT refused on this ground. An extension's `update` reaches it through `mainThreadConfiguration.ts` → `configurationService.updateValue`. Observed too: F17 | `src/vs/workbench/services/configuration/common/configurationEditing.ts`, `validate` (the comment *"Key to update must be a known setting from the registry unless … the operation is to delete the key"*) and `errorUnknownKey` |
| V6 | In an extension host `inspect(key)` answers an OBJECT for a key nobody registered; its `defaultValue` is `undefined`. A registered key with no declared default gets one from its type — `''` for a string | `src/vs/workbench/api/common/extHostConfiguration.ts`, `inspect`; `src/vs/platform/configuration/common/configurationRegistry.ts`, `getDefaultValue` |
| V7 | `globalValue` is the user value with the remote machine's user settings merged over the local ones. A write to the Global target goes to the LOCAL user file, unless the key's scope is a machine scope or the remote's own file already holds the key | `extHostConfiguration.ts`, `inspect`; `src/vs/workbench/services/configuration/browser/configurationService.ts`, `toEditableConfigurationTarget` |
| V8 | Settings Sync leaves out, by default, the settings whose scope is `machine` or `machine-overridable` (and those marked `ignoreSync`). This is the first half of the parent § 11 C5 point (b), now read. That `application` is shared by every profile was NOT read | `src/vs/platform/userDataSync/common/userDataSync.ts`, `getIgnoredSettings` |
| V9 | Sync's merge, when both machines changed their file since the last sync: a key ADDED on both sides with DIFFERENT values is a conflict (`handleConflict`, `hasConflicts`); added with the same value, it merges. A key removed on one side and unchanged on the other is removed on both; removed on one side and changed on the other is a conflict. What the editor shows for a conflict was not read | `src/vs/platform/userDataSync/common/settingsMerge.ts`, `merge` |

**Not measured, and no test here can measure it:** the lag of V2 between two real windows (F16 gives upper bounds
only), and when Settings Sync delivers. § S2.7 says what rests on them.

## S2.1 — Decisions

*Epoch 2 said …* in a row names what the first commit held and the parent's sort cut or the re-review replaced.

| # | Input (stories document § 8) | Decision |
|---|---|---|
| D1 | 1, N5 — the reach | Overlays are found by walking `store.keys()` for the prefix `overlayKeyOf('')`, never through the side registry. The registry forgets a side after 90 days or on **Forget**, and its overlay stays (F5): a side that comes back would bring a frozen `human` to a machine whose marker says done. The walk needs nothing from the registry |
| D2 | 1 — what is cleared | A location HOLDS a value to migrate when the key is present and its value is not the string `auto`. A junk word, `null` or a number is held too. A location that holds `auto` is not written: it is already what O1 wants, and an explicit `auto` in an overlay wins over a workspace value, so removing it could take that side OFF `auto` (the parent § 4.6 says the same). A slot that is not a record holds nothing and is never written. `onExhausted` is removed from a record by one `update` of the record without it; every other member is kept |
| D3 | the order | The parent's (§ 4.6): the claim, the record of what each location held, each overlay, the user layer, the mirror, the marker, the notification. Overlays in key order. The user layer comes after them: the run's settings write is the one configuration event the run causes, and that event is what wakes the other open windows (F9). *Epoch 2 said that event is the user-layer clear. Now it is the witness write or the removal, and a run that finds the witness already there causes none (§ S2.6)* |
| D4 | 2, N6 — two windows | No lock exists across the windows of a profile: no shared file system (a WSL or SSH window), and `globalState` is one stored value per extension, last writer wins (V1–V3). **The claim is a best effort.** It makes one window do the run in the usual case, where each window hears the other's write within the pause. Under a longer lag it excludes nothing: a window reads its own copy back. It stays as it was — write, pause, read back, go on only on reading this window's own name, and ask `mine()` before every later write of the record or of an overlay. **Nothing rests on it.** What correctness rests on is the parent's three points: (1) the user layer's witness is in the settings file, out of reach of any `globalState` write (D7); (2) an overlay's value and its witness are one record in one write, so a stale copy brings both back or neither (D7); (3) no value read from the record is carried across an `await` — every step reads the store again — and the marker is written only from a copy in which every recorded location is settled (D10). *Epoch 2 said the claim, the read-back and the witnesses together are the mechanism, and that the notification belongs to the window that reads its own name in the marker; the sort cut the claim as the coordination. The read of `by` stays, as a best effort of the same kind* |
| D5 | 3, N7 — the mirror | `after` (the mirror and the panel's redraw) is called ONCE by every run that took the claim, on every way out: after the clears and a pause and BEFORE the marker in a run that completes; at once in a run that stops early or throws, because what it cleared already differs. It is not awaited: `mirrorSettings` returns nothing (F8), and every activation mirrors again. A run that stops at step 0 or 1 took no claim and calls nothing. *Epoch 2 said it is called only when `held` is not empty. That was a value of the record deciding after an `await`; a fresh install now mirrors once too, which writes the same file again* |
| D6 | 4, N8 — record before clear | What each location held is written into the record ONCE, before the first clear, and read back. A resumed run reuses it and never writes it again. A location is DUE only while no witness names it AND it still holds the recorded value (§ S2.2). A location that appears later is not in the record and is never touched. The record lists every location that holds a value to migrate, witnessed or not: the witness is asked in ONE place, `isDue`, so one line holds it and one break-it shows it. *Epoch 2 also left a witnessed overlay out of the plan — two lines for one decision* |
| D7 | the gate's finding; entry 2.2 — the witnesses | Progress is never written after a clear. **Overlay:** the clear and its witness are ONE `update` of ONE record (the member `onExhaustedAutoMigration`, the version), so there is no instant in between. **User layer:** the witness is the SETTING `coai.onExhaustedResetToAuto` in the user layer itself (C5; D18). The run (1) reads it: there, whoever wrote it — this machine or another through Settings Sync — the layer is settled and nothing is written, whatever the value is; (2) not there: writes it — one fixed word, `USER_WITNESS_WORD`, the same on every machine and in every version (the parent's entry 3.1) — whether or not the layer holds a value to remove; (3) after the awaited write reads the value again, from a configuration asked for again, and `held` again from the store; (4) removes the value only when it is still the one in `held` — the value test alone; (5) a removal the settings file refuses withdraws the witness; a witness write it refuses removes nothing (D12). The price is the opposite gap, and it is chosen: a host that dies after the witness and before the removal leaves the old value in place (the parent's residual (i); § S2.7 R2). Two windows of one machine that both find no witness both write the same word, and only the value test stands between them (§ S2.7 R3). *The first text of this revision gave each run a witness text of its own and compared it after the await; entry 3.1 replaced that — two texts are a Settings Sync conflict (V9).* *Epoch 2 kept this witness as `userBegun` in `globalState`; the sort cut it — a per-machine witness cannot travel with a synced value, and a stale window's write can put it back or take it away* |
| D8 | 5, N9 — Settings Sync | **Closed by D7's witness, within the parent's residuals.** The user layer is one file on every machine Settings Sync reaches, and the witness travels in it beside the value it witnesses. A machine whose run finds the witness leaves the user value alone (T32, T33); a choice that arrives during the awaited write, and differs from the recorded value, stops the removal (T34). Two machines' witnesses are the same word, so Sync merges them (V9; entry 3.1). Still nothing calls `setKeysForSync` (V4). What stays open is § S2.7 R1. *Epoch 2 said "not closed, and not closable inside the parent as written"; entry 2.2 changed the parent* |
| D9 | 9 — two writers of one overlay record | Each overlay clear is one turn of the catalog's queue (`inCatalogTurn`), so the catalog migration never holds a copy of the record across this write. Inside the turn the read and the `update` have no `await` between them. Each turn chooses its overlay from a fresh read of the record and the store — the first id of `held`, in key order, that is due at that moment — so a list of ids is not carried across the turns either, and an overlay a stale write brought back during the run is cleared again. `OVERLAY_TURNS` bounds the loop |
| D10 | 6 — the marker | `completed` is a string: the version that completed the run. ANY string means done, older or newer: the migration is one-time, never per version. A bare string in the slot reads as completed. Anything else — a number, an array, `null`, a record without a string `completed` — is not a marker, and the run starts as a first run. **The marker is written only from a copy in which every location of `held` is settled, and the check and the write are one synchronous stretch**: read the record, `mine()`, the due test of every id, `update` — no `await` among them. So the stored value that carries the marker carries the cleared overlays (the parent § 4.6, point 3). The marker is a NEW record, `{ completed, by }`: `held`, the claim and `refusedTold` are not carried into it, as the parent § 9 says they are there only while a run is in flight. A marker a stale window's write drops takes the cleared overlays with it, and the next activation runs again (T35) |
| D11 | 7 — the notification | One per completed run, non-modal, **true for the window that shows it** (the parent § 4.6, point (e)). The window that tells asks, at that moment, what it runs and where that is set — this side's overlay while the per-side switch is on, else the folder, the workspace, the user layer, the reader's own order (F7) — and a pure function chooses: the first sentence when that value is `auto`, else the second with `{label}` from `labelOf` and `{where}` one of the parent's three places. Through `notify`: `as: 'information'`, `class: 'outcome'`, `source: 'onExhaustedMigrationHost'`, `code: 'on-exhausted-migrated'`. The record gains `told: true` after it. A window that finds `completed` without `told` tells (a host can die in the pause after the marker). So: once per completed run; twice only if a host dies between showing it and recording it, or in the parent's residuals (ii) and (iii). **A fresh install is told too** (the parent § 4.6): nothing the extension keeps tells it from an upgrade, and both sentences are true for it. *Epoch 2 said one sentence, which "does not name a workspace value"; the sort cut it — the second form names it* |
| D12 | 6, 11 — a failure | No marker; the next activation tries again. The person is told only when the SETTINGS FILE refused — the removal, or the witness write itself — and only once per machine while it lasts (`refusedTold` in the record). The sentence says *"Your earlier choice there is still in force"*, so it is shown only when the user layer holds a value to migrate at that moment; a refused witness with nothing to remove is logged and tried again (U12). Everything else (a write that did not read back, a claim another window holds, a witness that is not this run's) goes to the extension host's console and never a toast |
| D13 | the window must know the new manifest | The run starts only when the registry knows `coai.onExhaustedResetToAuto`: `inspect(key)?.defaultValue !== undefined`, asked through `unknownKeysOf` (F18, V6). A window updated in place still has the old registry (F15); there the witness could not be written (V5, F17). Nothing is written in such a window, not even a claim, and its next activation runs the migration. The belt, as the catalog migration has it: a witness write that is refused all the same, in VS Code's words for an unknown key (`unregisteredKeyIn`) or while the registry, asked again, does not know the key (those words are translated), ends the run as `not-ready` — logged, never the warning. *Epoch 2 said the signal is `inspect('onExhausted').defaultValue === 'auto'`. It is dropped: a registry that knows the new key holds the new manifest, and with S1's fix forward a cleared location reads `auto` whatever the registry says* |
| D14 | 8 — without the panel | The run is `migrateOnExhausted(ports)` in the pure module: a store, the user layer and eight functions. No test builds a `PanelProvider`. The host starts it from `activate`. T30 holds both by reading the source |
| D15 | 10 — the workspace value; the user-layer port | The port offers three reads — whether the registry knows the witness, the value's `globalValue`, the witness's `globalValue` — and three writes, each to the Global target: the witness, its withdrawal, the removal. It cannot see or write a workspace or folder value of either key: a workspace CAN set a key of this scope, which is why only `globalValue` is read (the parent § 4.6). It asks for the configuration again at every call. **S2 needs no interface from S1**: the gate line is S1's `workspacePolicyOf`, computed at every render from what is stored, and `after` redraws the panel. The witness member does not change `'onExhausted' in overlay` |
| D16 | 8 — `test:host` | One scenario is added (T31). It is the only test that runs the host module, and V1's JSON clone and V5's refusal are exactly what a hand-made fake gets wrong. It needs a test-mode export. The job cannot start on this machine and CI runs it with `continue-on-error`, so the coder reads the job's log and quotes the scenario's line in the PR; without that line the host wiring is reported as unverified |
| D17 | the constants | `SETTLE_MS = 2000`, `CLAIM_STALE_MS = 60000`, `CLAIM_TRIES = 3`, `OVERLAY_TURNS = 64`. `SETTLE_MS` is a judgement, not a measurement, and nothing rests on it (§ S2.7 R3). `OVERLAY_TURNS` is above any number of sides a profile has; a run that spends it ends `incomplete` and the next activation goes on |
| D18 | C5 — the manifest property | `coai.onExhaustedResetToAuto`, beside `coai.catalogMigration`: `"type": "string"`, NO `default`, NO `scope`, and a `markdownDescription` that opens *"Written by ConnectOtherAIs, not by hand."* (the text is in § S2.9; U16). The manifest's own test of settings needs two more lines in S2's commit: a `SETTING_ALIAS` row and one English sentence in the help (F21; stories § 6) |
| D19 | found here — Export config and Import config | The witness never travels in a config file: one row in `NEVER_TRANSFERRED` (F20), for the reason the catalog marker is there. An imported witness would settle the importer's user layer without its value ever having been cleared — on a machine whose own run had ended `refused` or `incomplete`. Settings Sync is different: it carries the value and the witness as one file. T42 (U14) |

D1–D6, D9, D10, D12 and D14–D17 are mechanisms the parent does not name, chosen to meet something it does state.
D7, D8, D11, D13 and D18 are the parent's own § 4.6 made exact. D19 is beside the parent and is reported. None changes
the parent's scope, goals, DoD, merge conditions, epics, stories, build order, contracts or approach:

| Decision | The parent's words it meets |
|---|---|
| D1 | N5: "how every overlay key is found"; § 4.6 "walking `globalState.keys()` for the overlay prefix" |
| D2 | § 4.6 "A location that already holds `auto` is left as it is" |
| D3, D5 | § 4.6 *The order* and *The mirror*: "once its clears are done and BEFORE the marker — also in a run that ends incomplete" |
| D4, D10 | § 4.6 *What `globalState` cannot do*: "The claim stays … nothing below rests on it"; "the marker is written only from a copy in which every recorded location is settled, checked with no `await` before the write" |
| D6 | N8: "clears a location only while it still holds that recorded value" |
| D7, D8 | § 4.6 *One witness per location*; entry 2.2; N9 |
| D9 | § 4.6 "one turn of the catalog migration's queue"; point 3, "every step reads again" |
| D11, D12 | § 4.6 *The notification*; requirement 5; done-when 4 |
| D13 | § 4.6 *A window that does not know the new manifest* |
| D14–D16 | done-when 3: "all of it is tested without the panel ever being opened"; Q1; § 4.6 "it is read from `inspect().globalValue` only" |
| D18 | § 4.6 *Manifest*; § 9 |
| D19 | requirement 7 — every saved choice of the person's own is reset once; reported, U14 |

## S2.2 — The record, the witnesses, and when a location is due

One `globalState` key, `coai.onExhaustedAutoMigration` (C5). Its value:

```ts
export interface MigrationRecord {
  /** The parent's marker: the extension version that completed the run on this profile of this machine. */
  readonly completed?: string;
  /** The window that wrote `completed` — that window shows the notification, when it reads its name back. */
  readonly by?: string;
  /** The notification was shown. */
  readonly told?: true;
  /** The window running now, and when it said so (its own clock, `Date.now()`). A best effort (D4). */
  readonly claim?: { readonly by: string; readonly at: number };
  /** What each location held before the first clear (N8). Written once. Only locations that held a value to migrate. */
  readonly held?: Readonly<Record<string, string>>;
  /** The settings file refused, and the person was told. */
  readonly refusedTold?: true;
}
```

`userBegun` is gone: the user layer's witness is not in this record (D7).

- **A location id** is `'user'` or an overlay's whole `globalState` key.
- **`held[id]`** is the value as text: the string itself, else `JSON.stringify(value)`.
- **The witness of an overlay** is the member `onExhaustedAutoMigration` (the version) INSIDE that overlay's record.
  Every writer of an overlay keeps members it does not name (F2, F10), so the witness outlives a later choice.
  No reader asks for it (F4). A record that held only `onExhausted` keeps one member and so is still not seeded
  (F3): it was not seeded before either.
- **The witness of the user layer** is the setting `coai.onExhaustedResetToAuto` in the user layer: present when
  its `globalValue` is not `undefined`, whatever it says. What the run writes is one fixed word,
  `USER_WITNESS_WORD` = `'auto'`, the same on every machine and in every version, so Settings Sync merges two
  machines' witnesses without a conflict (the parent's entry 3.1; V9). A run cannot tell its own write from
  another's, and does not need to. It is never read from a workspace or a folder.

A location is **due** when all three hold, read at that moment; otherwise it is **settled**:

| # | Condition | Why |
|---|---|---|
| 1 | no witness names it | a clear was made, or was begun, here or on another machine (D7) |
| 2 | it holds a value to migrate (D2) | nothing to do otherwise |
| 3 | that value, as text, equals `held[id]` | else the person chose again after the record was made (N8) |

Every read that decides anything is made at that moment: `recordOf(store.get(KEY))` for the record, the store for an
overlay, the port for the user layer. **Nothing read from the record or from the settings is kept in a variable across
an `await`.** What a run may carry across one is its own: the version, its window's name, and the reason a write of
its own was refused.

## S2.3 — The two pure modules (new, no `vscode`)

### `src/onExhaustedMigration.ts` — the record, the decisions, the run

It imports `overlayKeyOf` from `./coaiInstall`, `unknownKeysOf` and `unregisteredKeyIn` from `./catalogMigrationRun`,
and `InForce` and `sentenceFor` from `./onExhaustedTold`. Nothing that reaches `vscode`.

```ts
export const ON_EXHAUSTED_MIGRATION_KEY = 'coai.onExhaustedAutoMigration';
export const OVERLAY_WITNESS = 'onExhaustedAutoMigration';
/** The user layer's witness as the configuration names it under `coai` — the manifest's `coai.onExhaustedResetToAuto`. */
export const USER_WITNESS = 'onExhaustedResetToAuto';
/** What the witness says: one fixed word, the same on every machine and in every version (the parent's entry 3.1). */
export const USER_WITNESS_WORD = 'auto';
export const SETTLE_MS = 2000;
export const CLAIM_STALE_MS = 60_000;
export const CLAIM_TRIES = 3;
export const OVERLAY_TURNS = 64;

export type MigrationOutcome = 'migrated' | 'already' | 'not-ready' | 'busy' | 'incomplete' | 'refused';

/** The part of `vscode.Memento` the run uses. `update(key, undefined)` deletes. */
export interface MigrationStore {
  keys(): readonly string[];
  get(key: string): unknown;
  update(key: string, value: unknown): PromiseLike<void>;
}

/** The person's own layer of `coai.onExhausted` and of its witness, and nothing of a workspace or a folder. */
export interface UserLayer {
  /** Whether this window's settings registry knows the witness setting — the new manifest (D13). */
  known(): boolean;
  /** `globalValue` of `coai.onExhausted`. */
  value(): unknown;
  /** `globalValue` of `coai.onExhaustedResetToAuto`. */
  witness(): unknown;
  /** Writes `USER_WITNESS_WORD` into the user layer. Rejects when the settings file refuses. */
  writeWitness(): PromiseLike<void>;
  /** Takes the witness out again — after a removal the settings file refused. */
  withdrawWitness(): PromiseLike<void>;
  /** Removes the value from the user layer. Rejects when the settings file refuses. */
  clear(): PromiseLike<void>;
}

/** The two members of `vscode.WorkspaceConfiguration` the user layer needs. */
export interface LayeredSetting {
  inspect(key: string): { readonly defaultValue?: unknown; readonly globalValue?: unknown } | undefined;
  update(key: string, value: unknown, target: unknown): PromiseLike<void>;
}

export interface MigrationPorts {
  readonly store: MigrationStore;
  readonly user: UserLayer;
  /** `extensionVersion(context)`; an empty one is stored as `'unknown'`. */
  readonly version: string;
  /** This activation's own name: random, made once per activation. */
  readonly window: string;
  readonly now: () => number;
  readonly pause: (ms: number) => Promise<void>;
  /** One turn of the catalog's queue (`inCatalogTurn`). */
  readonly turn: <T>(work: () => Promise<T>) => Promise<T>;
  /** The mirror and the panel's redraw. Not awaited. */
  readonly after: () => void;
  /** What this window runs and where it is set — asked at the moment it tells, never earlier. */
  readonly inForce: () => InForce;
  /** The one notification, with the sentence chosen for this window. */
  readonly tell: (sentence: string) => Promise<void>;
  /** The settings file refused: `why` is its own words. */
  readonly refused: (why: string) => Promise<void>;
  /** The extension host's console. Never a toast. */
  readonly log: (what: string, error?: unknown) => void;
}

/** What is stored, read as a record. A bare string is a completed marker; anything unreadable is an empty record. */
export function recordOf(stored: unknown): MigrationRecord;
/** The value as `held` keeps it, or `undefined` when there is nothing to migrate (absent, or `auto`). */
export function heldText(value: unknown): string | undefined;
/** What every location holds now: `'user'` and each overlay record found by its key — witnessed or not (D6). */
export function planOf(store: MigrationStore, userValue: unknown): Readonly<Record<string, string>>;
/** § S2.2's table. The one place a witness is asked. */
export function isDue(witnessed: boolean, current: unknown, held: string | undefined): boolean;
/** The user layer over a configuration that is asked for AGAIN at every call, writing the Global target only. */
export function userLayerOf(config: () => LayeredSetting, globalTarget: unknown): UserLayer;
/** The run. It never rejects. */
export function migrateOnExhausted(ports: MigrationPorts): Promise<MigrationOutcome>;
```

### `src/onExhaustedTold.ts` — what a person is told, and which sentence

It imports `ON_EXHAUSTED_CHOICES` and `labelOf` from `./onExhaustedPanel` (S1's, pure) and nothing else.

```ts
/** The parent's § 4.6 sentences, word for word. Only the coordinator changes them. */
export const NOW_AUTO = 'When the rounds run out is now Auto — the gate raises no new hold and never waits for you; '
  + 'it logs and moves on. Your AI may still ask you a question its consultants could not settle, or when no '
  + 'consultant can be had. Change it in Reviews › The gate.';
export const NOT_RESET = 'When the rounds run out could not be reset to Auto in your settings: the settings file '
  + 'refused the change. Your earlier choice there is still in force. ConnectOtherAIs tries again at the next start.';

/** Where the value a window runs is set. */
export type SetWhere = 'side' | 'workspace' | 'user';
/** The parent's three places: "this side's own settings", "this workspace's .vscode/settings.json", "your user settings". */
export const WHERE: Readonly<Record<SetWhere, string>>;
/** The parent's second sentence: "When the rounds run out now defaults to Auto, but {label} stays in force in this
 *  window: {where} sets it. Change it in Reviews › The gate." */
export function staysInForce(label: string, where: string): string;

/** The stored value a window reads, and the location that holds it; no `where` when no location holds one. */
export interface InForce { readonly value: unknown; readonly where?: SetWhere }
export interface HeldLayers {
  readonly globalValue?: unknown;
  readonly workspaceValue?: unknown;
  readonly workspaceFolderValue?: unknown;
}
/** The first location that holds a value, in the reader's order (F7): this side's overlay — only while the
 *  per-side switch is on — then the folder, the workspace, the user layer. */
export function inForceOf(layers: HeldLayers | undefined, perSide: boolean, overlay: Readonly<Record<string, unknown>>): InForce;
/** `NOW_AUTO` when the window runs `auto` — no value, `auto`, or a word that is no choice — else the second sentence. */
export function sentenceFor(inForce: InForce): string;
```

Rules the coder keeps:

- `userLayerOf` calls `config()` at every read and every write. A `WorkspaceConfiguration` is a snapshot: one kept
  from the start of the run would never see a witness or a choice that arrived during it. `known()` is
  `unknownKeysOf([USER_WITNESS], (key) => config().inspect(key)?.defaultValue).length === 0` — the function the
  catalog migration already uses, not a second copy of the test.
- `recordOf` reads each member by its type and drops what does not fit. It never throws.
- `planOf` reads an overlay through the same test `readOverlay` uses (an object, not `null`, not an array). The
  prefix is `overlayKeyOf('')`, not a retyped string. It does not ask for a witness.
- `inForceOf` mirrors the reader: the overlay counts when the record HAS the property (`Object.hasOwn`), as
  `overlaidReader` does; among the layers it takes the first that is not `undefined` or `null`, as `heldValue`'s `??`
  does. `sentenceFor` treats a value as in force only when it is one of `ON_EXHAUSTED_CHOICES` other than `auto` —
  what `asOnExhausted` then answers. T39 holds both to the real reader.
- `migrateOnExhausted` is a short list of steps, each a function of its own that answers an outcome to stop with or
  nothing to go on. That keeps every function at complexity ≤ 4 and under 50 lines (§ S2.9).
- An exception inside the run is caught once, at the top: `log`, then `'incomplete'`. The catch writes NOTHING. A
  claim left behind goes stale, exactly as a dead host's does. `after` still runs when a step throws: it is called
  in a `finally` of the steps between the claim and the marker.

## S2.4 — The run, step by step

`read()` is `recordOf(ports.store.get(KEY))`, made where it is written, never earlier. `mine(r)` is
`r.completed === undefined && r.claim?.by === ports.window`. "No `await`" in a row means: from the read named to the
`update` named, the code is one synchronous stretch.

| Step | What it does | Stops with |
|---|---|---|
| 0 ready | `!ports.user.known()` → stop. Nothing is written, not even a claim (D13) | `not-ready` |
| 1 enter | Up to `CLAIM_TRIES` times: read. `completed` and `told` → stop. `completed` without `told` → step 7. A claim of ANOTHER window younger than `CLAIM_STALE_MS` → `pause(min(CLAIM_STALE_MS, at + CLAIM_STALE_MS − now))`, next try (the cap is for a clock ahead of this one: `at` is another host's clock). Else write `{ ...read(), claim: { by: window, at: now } }`, `pause(SETTLE_MS)`, read: `completed` → as above; `mine` → step 2; else next try. A best effort (D4) | `already`; `busy` when the tries are spent |
| 2 plan | Read; `!mine` → stop. `held` is there → go on. Else, with no `await`: `planOf(store, user.value())` and `update(KEY, { ...thatRead, held })`. After its `await`, read: the same text, or stop (`log`). An empty `held` is written too | `busy`; `incomplete` |
| 3 overlays | Up to `OVERLAY_TURNS` times, `await ports.turn(...)`. Inside one turn, with no `await` before its `update`: read the record; `!mine` → stop the run. Take the first id of that read's `held` but `'user'`, in key order, whose slot is a record and is due now (§ S2.2). None → this step is done. Else ONE `update` of that slot: the record without `onExhausted` and with `[OVERLAY_WITNESS]: version`; its promise is awaited as the turn's last act. The turns spent with one still due → `log`, stop | `busy`; `incomplete` |
| 4 user layer | Read; `!mine` → stop. `user.witness() !== undefined` → the layer is settled, nothing is written. Else `await user.writeWitness()` — whether or not the layer holds a value. **After the `await`, read both again:** `user.value()` and `read().held?.['user']`. The value holds nothing to migrate, or its text is not that `held` → leave the value. Else `user.clear()`, called with no `await` after that read, and awaited | `busy` |
| 4r a refusal | A witness write that rejects: VS Code's words name an unknown key (`unregisteredKeyIn(error) === USER_WITNESS`), or `!user.known()` when asked again → stop as `not-ready` (D13). Else stop as `refused`, with the reason to tell only when `heldText(user.value())` is not `undefined` now (D12); no removal is asked for. A removal that rejects: `await user.withdrawWitness()` — its own rejection is logged, not thrown (R14) — then stop as `refused`, with the reason to tell | `not-ready`; `refused` |
| 5 mirror | Steps 2–4 ended with nothing to stop for → `await pause(SETTLE_MS)`. Then, on EVERY way out of steps 2–4 — a stop, a throw, or the end of that pause — `ports.after()`, once (D5). A stop goes to 5s; a throw to the catch at the top | — |
| 5s leave | A reason to tell and `!read().refusedTold` → `await ports.refused(why)`. Then, with no `await`: read; `mine` → ONE write of that read without `claim`, and with `refusedTold: true` when a reason was told now or before. Not `mine` → nothing is written | the stop's outcome |
| 6 marker | With no `await` from the read to the `update`: read; `!mine` → stop. `left` = the ids of that read's `held` that are due NOW — an overlay from the store, `'user'` from `user.witness()` and `user.value()`. Not empty → `log` the ids, leave as in 5s. Else `update(KEY, { completed: version, by: window })` — a new record, nothing carried into it (D10) | `busy`; `incomplete` |
| 7 tell | Entered from step 1 (`completed`, not `told`): first write `{ ...read(), by: window }`. Then `pause(SETTLE_MS)`, read. `completed`, `by === window`, not `told` → `await ports.tell(sentenceFor(ports.inForce()))`, then write `{ ...read(), told: true }`. `completed` otherwise → another window tells. No `completed` → the marker was lost; the next activation runs again | `migrated`; `already`; `incomplete` |

The load-bearing orders, each pinned by a test:

1. `held` is written and read back before any clear (T11).
2. An overlay's clear and its witness are one `update` (T13).
3. The witness setting is written and awaited before `user.clear()` is called (T14, T15); the value is read again
   after that `await`, and only a value that still equals `held` is removed (T34, T41).
4. `after` comes after the clears and the pause, and before the marker; and on every early way out (T10).
5. The due test of every location and the marker's `update` have no `await` between them, and the test comes after
   `after` (T17, T36).
6. `tell` comes only after the marker was read back with this window's name (T23, T28), and its sentence is chosen
   then (T37).

## S2.5 — The host module and the call site

`src/onExhaustedMigrationHost.ts` (new; imports `vscode`). It holds no decision.

```ts
/** Called once from `activate`. Never rejects. */
export function startOnExhaustedMigration(context: vscode.ExtensionContext, version: string, after: () => void): Promise<void>;
```

It builds the ports and awaits `migrateOnExhausted`:

| Port | In the host |
|---|---|
| `store` | `context.globalState` |
| `user` | `userLayerOf(() => vscode.workspace.getConfiguration('coai'), vscode.ConfigurationTarget.Global)` |
| `version` | the argument: `extensionVersion(context)` at the call site (`extension.ts:1341`). Never read a second time here |
| `window` | `randomBytes(6).toString('hex')`, made inside the call |
| `now`, `pause` | `Date.now`; `(ms) => new Promise((done) => setTimeout(done, ms))` |
| `turn` | `inCatalogTurn` (`catalogMigrationHost.ts:70`) |
| `after` | the argument |
| `inForce` | one expression over a configuration asked for then: `inForceOf(config.inspect('onExhausted'), config.get('perSideSettings') === true, readOverlay(context.globalState, thisSide(context.globalStorageUri)))` — the same three reads `readerFor` makes (`sideConfig.ts:29-39`) |
| `tell` | `(sentence) => notify({ as: 'information', class: 'outcome', source: 'onExhaustedMigrationHost', code: 'on-exhausted-migrated', title: sentence })` |
| `refused` | `(why) => notify({ as: 'warning', class: 'failure', source: 'onExhaustedMigrationHost', code: 'on-exhausted-not-reset', title: NOT_RESET, detail: why })` |
| `log` | `console.error('ConnectOtherAIs: …', error)`, as activation logs a refused side stamp (`extension.ts:449`) |

The outcome is logged only when it is `incomplete` or `not-ready`.

`src/extension.ts`, three small hunks:

1. Directly after the first `mirrorSettings(settingsSync);` (`:438`) and before `onDataRootBack`:
   `void startOnExhaustedMigration(context, extensionVersion(context), () => { mirrorSettings(settingsSync); void panel.render(); });`
   with a comment that names this file. `activate` is already in `eslint-suppressions.json`; one statement adds no
   branch and its counts do not move.
2. `CoaiExports` (`:102`) gains `readonly onExhaustedMigration?: () => unknown;`, documented as present only in test
   mode, as `remoteSessions` is.
3. The returned object (`:919`) gains, inside the existing `ExtensionMode.Test` spread,
   `onExhaustedMigration: () => context.globalState.get(ON_EXHAUSTED_MIGRATION_KEY)`.

`.github/workflows/sonarcloud.yml`: `src_vs_code/src/onExhaustedMigrationHost.ts` joins `sonar.coverage.exclusions`
in its alphabetical place. The two pure modules must NOT be listed (F13).

## S2.6 — What each side's server runs afterwards (N5)

A cleared location reads `auto` in every window of this version: S1's reader answers from the layers that hold a
value, never the registry's default (F7). The settings file of a side changes only when a window of that side mirrors.

| The side | When its file is rewritten | What its server then runs |
|---|---|---|
| this window's | now: the run calls `after` | S1's table: no key for a server that lists `onExhaustedAuto`; `good_enough` for an older one; nothing while its features are not settled |
| another side, a window open, the new extension | at that window's next mirror AFTER its copy of `globalState` holds the change — its next activation at the latest. The run's own settings write (the witness, or the removal) is a `coai` change and wakes it (F9); a run that found the witness already there writes no setting, and then nothing wakes it. And the event can reach that window before its copy holds the cleared overlays (not measured): that mirror writes the old value again. The promise is the parent's N5 and no more | then as the first row |
| another side, no window open | at its next activation | as the first row |
| another side, an OLDER extension (extensions install per remote) | at its next mirror, by the old writer, which writes the key only when the value differs from ITS default, `human`. A cleared overlay reads the old default, so it writes no key | a server 0.66.0 or newer: `auto`. An OLDER server: `human` — also where that side's overlay held `continue`, `good_enough` or `escalate` and ran it until now (the parent's residual (iv); § S2.7 R5) |

*Epoch 2 said the user-layer clear comes last and wakes the other windows. The sort cut it: with the witness already
there no clear and no event happen.*

## S2.7 — What S2 does not close

The parent § 4.6 lists its residuals (i)–(vi). This is the same ground as the code meets it, each named with the
parent's number where it has one. S3 writes them into `research/module_extension.md`.

- **R1 — Settings Sync (the parent's (ii), Sync half; N9).** Closed for a machine whose run finds the witness. What
  stays: (a) a delivery that lands after the read-back and before the removal is written — the time of one settings
  write; (b) a machine whose run comes before Sync has pulled the other machine's file at all: it clears its own copy,
  and Sync's merge then removes on the other machine a choice made there that EQUALS the old value (V9: removed on
  one side, unchanged on the other; a choice that differs is a conflict instead);
  (c) found here, and in the parent since (U11): a delivery that lands between the first read of the witness and this
  run's own witness write. The run then writes the same word over the other machine's witness, which loses nothing,
  and the value test decides: it stops the removal when the delivered value differs from the recorded one (T34), and
  does not when the choice made elsewhere equals it (T34, second half). None is measured. *Closed by entry 3.1:* in
  window (b) the two machines' witnesses are the same word, so Sync merges them; the first text of this revision had
  them conflict (V9; U10).
- **R2 — the opposite gap (the parent's (i); N8).** A host that dies after the witness setting was written and before
  the removal was asked for leaves the user value in place for good: the next run finds the witness and keeps the
  value (T15). The run completes, and the notification names the kept value and "your user settings". The other order
  would reset a later `human`, which is the finding D7 answers.
- **R3 — what rests on the lag (the parent's (ii), window half, and (iii)).** The claim excludes nothing when a
  window's copy of `globalState` is older than `SETTLE_MS` at the end of its pause. Then: two runs over the same
  locations, and two notifications (T41). Where a witness stands — in an overlay, or in the settings file — the second
  run changes nothing. Two windows that both find NO witness in the user layer both write the same word, and since
  entry 3.1 only the value test stands between them: a window that reads after the other's removal has landed removes
  nothing (T41); a window that reads before it has landed asks for the removal a second time, which removes a choice
  only if the person made one in between — the time of one settings write (T41, second half). A stale window that writes ANY key after the run puts its whole copy back:
  the cleared overlay returns with its old value and no witness, and the marker goes with it; that side runs the old
  value until the next activation, which clears it and tells again (T35). A choice the person made in an overlay
  through a window whose copy lagged the run carries no witness, and that next run clears it too. The user layer's
  choice is kept in every one of these but that one instant between two removals. All of it needs a lag above two seconds or a window that lags the whole run;
  none needs a crash. `SETTLE_MS` is not measured.
- **R4 — a window that updated in place.** D13 keeps the migration out of it; its next activation runs. What that
  window's panel and mirror read meanwhile is S1's fix forward (stories § 7.3): `auto` when no layer holds a value.
- **R5 — an older extension on another side, with an older server (the parent's (iv)).** The last row of § S2.6.
- **R6 — an overlay seeded later by an older extension (the parent's (v)).** A window of an old version that turns
  the per-side switch on after the marker freezes the old default `human` into a new overlay. Nothing clears it.
- **R7 — a kept overlay has no witness.** An overlay left alone because its value changed (condition 3) is not
  stamped. If the record is then lost (R3), a new plan records the later choice as the old value and clears it.
- **R8 — the notification, twice or never.** Twice when a host dies between showing it and writing `told`, and in
  R3. Never only if `told` is written and the toast then fails to show; `notify` has already recorded it (F12).
- **R9 — per profile (the parent's (vi)).** The marker and the overlays live in a profile's `globalState`, so their
  "once" is once per profile of a machine. The user layer's "once" is once for every machine Sync reaches.
- **R10 — the fakes are a reading of VS Code's source (V1–V3, V5, V6), not VS Code.** T29 pins them. No test runs
  two real windows or two real machines. The owner's hand check is the only observation there can be.
- **R11 — a value in a remote machine's own user settings (found here; V7; U17; now the parent's (viii)).** `globalValue` merges the remote's
  user file over the local one, and a removal goes to the file that holds the key. So one run clears one of the two.
  A local value that the remote's had hidden, and the user file of every OTHER remote, are then witnessed and stay.
  The extension never writes a remote's user file itself (F6), so it takes a hand edit. Not observed.
- **R12 — a settings file that never accepts a write (found here; U13; now the parent's (vii)).** No marker is ever written, so every start
  runs again, and the notification is never shown; the warning is shown once when a value is held. Overlays are
  cleared at the first start.
- **R13 — the witness is not in a config file (D19).** A machine that imports a config file made on another one runs
  its own migration; nothing travels.
- **R14 — a withdrawal that is refused too.** After a refused removal the witness is withdrawn. If the file refuses
  that as well, the witness stays beside the value, and the next run keeps the value — R2's end by another road. It
  takes a file that accepts the witness and then refuses twice. Logged.

## S2.8 — Scenarios

**RED first** (stories document § 3): write each scenario before its code, see it fail on its assertion, make it
green, then break it — remove or change the production line named, see the stated failure, restore it. Report all
three. Every value the code computes is read back from the code: overlay keys from `overlayKey(side)` with a real
`Side`, the record's key and member names from the exported constants, the witness's word from `USER_WITNESS_WORD`, a later
choice written with the real `writeOverlay` (`testing.md`, *A fixture the code REJECTS*). The clock is the `now` port;
no test reads `Date.now()`.

### S2.8.0 — The fakes: `src/test/onExhaustedMigrationFakes.ts` (new; not a test file)

| Fake | What it models |
|---|---|
| `ClientState` | One profile's client store on one machine, and its windows, as V1–V3 read. `window()` answers a `MigrationStore` with its OWN copy, taken then. `update` changes that copy at once (JSON clone; `undefined` deletes), then REPLACES the client's whole value with the copy. `deliver()` replaces every window's copy with the client's value; `deliver(w)` only that window's — the others stay stale. Each window logs its writes (`key`, `value`). `dieAfter(n)` / `dieBefore(n)`: the n-th `update` lands (or does not) and its promise never resolves — the host died. Two `ClientState`s are two machines |
| `FakeUserSettings` | ONE settings file, as V5 and V6 read. A registry: `declare(key, defaultValue)`, `undeclare(key)`; `inspect(key)` answers an object over four layers (`defaultValue` only for a declared key, `globalValue`, `workspaceValue`, `workspaceFolderValue`). `config()` answers a SNAPSHOT with `inspect` and `update`. `update(key, value, target)` is logged. A VALUE written to an undeclared key rejects with VS Code's words (`Unable to write to User Settings because coai.<key> is not a registered configuration.`); a removal is accepted. `refuse(key, error)` / `cure(key)`: a write of that key rejects and nothing lands. `hold(key, 'landed')`: the next write of that key lands at once and its promise waits for `release()`; `hold(key, 'unlanded')`: every write of that key lands only at its `release()`, oldest first. `dieAfter(key)`: the write lands and its promise never resolves. The windows of one machine share it; two machines share it when Settings Sync has delivered — the test hands both the same object |
| `Pauses` | `pause(ms)` logs `ms` and answers a promise the test resolves (`next()`), advancing the fake clock by `ms`. `auto()` resolves every pause at once, for one-window tests |
| `portsFor(window, …)` | `MigrationPorts` over the fakes, with one ordered event log (`write`, `witness`, `withdraw`, `clear user`, `pause`, `after`, `tell` with its sentence, `refused`), a real `CatalogTurns` behind `turn`, a hook the test can run before the n-th turn, and an `after` the test may replace. `inForce` is the real `inForceOf` over the fake settings, the window's own copy, a real `Side` and a `perSide` the test sets (off unless said) |

A run that "died" is never awaited. The test then advances the clock past `CLAIM_STALE_MS` and starts a new
activation: a new window name over the same client store.

### S2.8.1 — The decisions: `src/test/onExhaustedMigration.test.ts` (new, pure)

| # | Test | Scenario and assertion | Break-it: the line, and what goes red |
|---|---|---|---|
| T1 | `TheMarkerIsReadDefensively` | `recordOf`: `'0.88.0'`, `{ completed: '0.87.0' }` and `{ completed: '9.9.9' }` are completed; `42`, `null`, `[]`, `{}` and `{ completed: 5 }` are not; a `claim` with a non-number `at` is dropped; nothing throws | accept any truthy `completed` → `{ completed: 5 }` reads as done |
| T2 | `OverlaysAreFoundByTheirOwnKeys_AForgottenSideToo` | a store with a registry record for side 1 only, overlays for side 1 and side 2, a slot that holds a string, and the look-alike key `coai.settingsOverlayX`: `planOf` names both overlays and nothing else | find overlays through `sidesIn` → side 2 is missing |
| T3 | `OnlyAValueThatKeepsAPersonOffAutoIsHeld` | `heldText`: `human`, `continue`, `'humna'`, `null` and `5` answer a text; `undefined` and `auto` answer nothing. `planOf` records an overlay that carries the witness and holds `human` as it records any other (D6) | drop the `auto` test → an `auto` location is planned |
| T4 | `ALocationIsDueOnlyWhileUnwitnessedAndUnchanged` | `isDue` over § S2.2's three conditions, one row each false, and one row all true | remove one condition → its row |
| T5 | `TheSentencesAreTheParentsAndNothingWider` | `NOW_AUTO`, `NOT_RESET`, `staysInForce('Ask a human', WHERE.user)` and the three `WHERE` texts each equal the parent's words, typed out in the test on purpose (these are the literals that must be word for word); none contains "never asks you anything" | change a word of `NOW_AUTO`; of the second sentence; of a place |
| T6 | `TheUserLayerReadsAndWritesThePersonsOwnLayerOnly` | `userLayerOf` over a fake with all four layers set for both keys: `value()` and `witness()` answer the `globalValue` — a witness set only in the workspace layer reads as absent; `writeWitness()` makes one `update('onExhaustedResetToAuto', USER_WITNESS_WORD, GLOBAL)`, `withdrawWitness()` one `update('onExhaustedResetToAuto', undefined, GLOBAL)`, `clear()` one `update('onExhausted', undefined, GLOBAL)`; the workspace and folder values of both keys are unchanged; `known()` is true for a declared witness key and false for an undeclared one; a value set between two reads is seen by the second | keep the first configuration object → the second read is stale; pass another target → the target assertion; read the witness through `get` → the workspace one is found |
| T30 | `TheRunStartsFromActivation_NeverFromThePanel` | reads the source: `extension.ts` calls `startOnExhaustedMigration(` exactly once; `panelProvider.ts` and `panelView.ts` never name it; none of the three new modules imports `./panelProvider` or `./panelView`. Its companion asserts the same scan FINDS `./coaiInstall` in the run module's imports, so the pattern is alive (`testing.md`, *A structural test that matches nothing*). A source scan is right here: there is no program a unit test can run (`.agents/PROJECT.md`) | move the call into `resolveWebviewView` |
| T39 | `TheValueInForceIsTheOneTheReaderAnswers_AndItsPlace` | `inForceOf`, one row each: this side's overlay holds `continue` and the switch is on → `side`; the same with the switch off → the next layer; an overlay that holds `auto` over a workspace `human` → `auto`, so `NOW_AUTO`; folder over workspace over user; nothing held → no `where`, `NOW_AUTO`; a junk word → `NOW_AUTO`. For EVERY row the value the sentence rests on equals what the real reader answers: `settingsFrom(sideConfigReader(userLayerReader(merged, inspect), perSide, store, side)).onExhausted`, with `merged` answering the OLD registry default `human` — the row "nothing held" then holds S2 to S1's fix forward | take the overlay with the switch off → its row; put the workspace over the folder → its row; answer the registry's default → the last row |
| T42 | `TheWitnessNeverTravelsInAConfigFile` | the real manifest declares `coai.onExhaustedResetToAuto` as a string with no `default` and no `scope`; `exportedSettings(declaredSettings(manifest), …)` with the witness set does not carry it; `importedConfig` of a file that carries it refuses it by name (the shape of `configTransferCatalog.test.ts`) | remove the `NEVER_TRANSFERRED` row → it is exported; add a `scope` to the property → the manifest assertion |

### S2.8.2 — One window: `src/test/onExhaustedMigrationRun.test.ts` (new)

The usual fixture, **U**: the user layer `human`, no witness; overlay X (`human`, with `vendors` and `rounds` beside
it); overlay Y (`continue`), whose side has no registry record; overlay Z (`auto`); a slot that holds a string;
`workspaceValue: 'human'` and `workspaceFolderValue: 'continue'` for `onExhausted`; both keys declared; the per-side
switch off. So a window over U runs `continue`, from the folder. **U0** is U without the two workspace values.

| # | Test | Scenario and assertion | Break-it: the line, and what goes red |
|---|---|---|---|
| T7 | `EveryLocationIsClearedOnce_AndThePersonIsToldOnce` | U, one run → `migrated`. The user value is gone and the witness is `USER_WITNESS_WORD`; X and Y have no `onExhausted`, carry the witness member, and keep `vendors` and `rounds`; Z and the string slot are deep-equal to before; the record is exactly `{ completed: version, by: window, told: true }`; one `tell`, one `after`, two user-layer `update`s: the witness, then the removal | remove the `update` in the overlay step → X still holds `human`; carry the old record into the marker → the record still has `held` |
| T8 | `ASecondActivationDoesNothing` | after T7's run, a new activation → `already`: no write to the store, no user-layer `update`, no pause, no `after`, no `tell`. (T7 is this file's positive: the same fixture, something happens) | remove step 1's `completed` and `told` stop → a claim is written |
| T9 | `AChoiceMadeAfterwardsIsKept_AlsoWithTheMarkerLost` | after T7's run the person chooses: the user layer `human`, X `human` through `writeOverlay`. (a) A new activation → `already`, both `human`. (b) The record is then deleted, as a stale write leaves it; a new activation runs in full → `migrated`: the user layer is `human` and X is `human`, each kept by its witness alone; no removal | (a) as T8; (b) remove condition 1 from `isDue` → X is cleared; skip the witness read in step 4 → the user layer is cleared |
| T10 | `TheMirrorComesAfterEveryClearAndBeforeTheMarker_AndOnEveryWayOut` | U: in the event log `after` follows the removal and the settle pause, and at that moment the record has no `completed`. Second half: a run that loses its claim before Y's turn → `busy`, and `after` was called once. Third half: the first run dies in the settle pause; the resumed run clears nothing and calls `after` once | move `ports.after()` below the marker write → first half; call `after` only on the way to the marker → second half |
| T11 | `WhatEachLocationHeldIsWrittenBeforeTheFirstClear_AndNeverAgain` | U: the first write that carries `held` precedes every overlay write and every user-layer `update`; `held` is `{ user: 'human', [X]: 'human', [Y]: 'continue' }`; after an interrupted run and changed values, the resumed run's record has the same `held` | plan again on resume (drop the `held` is there test) → `held` differs |
| T12 | `InterruptedAfterTheFirstClear_AChoiceMadeMeanwhileIsKept` (N8, the parent's test) | U: the host dies right after X's clear landed. The person sets X to `human` again (`writeOverlay`) and Y to `good_enough`. Resume → `migrated`: X is `human`, Y is `good_enough`, the user layer is cleared, one `tell` in all | ignore the overlay's witness → X is cleared; drop condition 3 → Y is cleared |
| T13 | `AnOverlayIsClearedAndWitnessedInOneWrite` | U: X's key is written exactly once, and no value ever written under X's key lacks both `onExhausted` and the witness | clear, then stamp in a second `update` → a written value with neither |
| T14 | `TheHostDiesRightAfterTheUserClear_AHumanChosenThenIsKept` (the gate's finding) | U, `dieAfter('onExhausted')`: the removal landed and nothing after it ran. (a) Resume with nothing chosen → `migrated`, the user layer still empty. (b) The person chooses `human` first; resume → `migrated`, the user layer is `human`, and the resumed run made no user-layer `update` | ask for the removal before the witness is written → in (b) the resumed run finds no witness and clears `human` |
| T15 | `RetryingAnAttemptThatMayHaveLandedWouldResetALaterChoice_SoItIsNeverRetried` | U0, `dieAfter('onExhaustedResetToAuto')`: the witness landed and the removal was never asked for. Resume → `migrated`; the user layer still holds `human`; the resumed run made no user-layer `update`; its `tell` is `staysInForce('Ask a human', WHERE.user)`. The test's comment says this is R2, chosen, and why | treat a witness as "not yet cleared" while the value still equals `held` → the `update` count; and T14 (b) goes red with it |
| T16 | `ARemovalTheSettingsFileRefuses_WithdrawsTheWitness_WritesNoMarker_IsSaidOnce_AndTheNextStartFinishes` | U, `refuse('onExhausted', …)`: the witness is accepted, the removal is not → `refused`: no witness left (written, then withdrawn, in the log), no `completed`, no claim, `refusedTold`; `refused` called once with the error's text; X and Y cleared; `after` called. A second refusing activation → `refused`, and `refused` is NOT called again. The file is cured; the third activation → `migrated`, the user layer cleared, one `tell` | keep the witness after a rejected removal → the third run finds it and leaves `human`; drop `refusedTold` → two `refused` calls |
| T17 | `AWriteThatDidNotLastIsSeenBeforeTheMarker` | U: inside the settle pause the test puts X's old record back (a stale write, V3) → `incomplete`: no `completed`, no `tell`, `after` called, the claim released. The next activation clears X and completes | remove the `left` test → `completed` is written while X holds `human` |
| T18 | `AWindowThatDoesNotKnowTheNewManifestChangesNothing` | U with the witness key undeclared → `not-ready`: no write of any kind — the store, the settings — and no pause. Declared, the same fixture migrates | remove step 0 → the store is written |
| T19 | `AFreshInstallIsToldOnce_AndWritesTheWitness` | nothing saved anywhere → `migrated`: every store write is under the record's key; ONE user-layer `update`, the witness; no removal; the first write that carries `held` has `held: {}`; `after` once; one `tell`, `NOW_AUTO`. With `version: ''` the marker reads `'unknown'` | drop the `'unknown'` fallback → the marker assertion |
| T20 | `AWorkspaceValueIsNeverEdited` (Q1) | U, with a workspace value of the witness key too → after the run `workspaceValue` is `human` and `workspaceFolderValue` is `continue`, the workspace witness is unchanged, and every user-layer `update` in the log has the Global target | as T6's second break-it |
| T21 | `TheCatalogMigrationAndThisRunTakeTurnsOnOneOverlay` | a catalog-like run is started through the same `CatalogTurns` (`migrate`): it reads X, waits on a gate the test holds, then writes X with a new `vendors`. This run reaches step 3 while the gate is held. The gate opens → `migrated`: X has the new `vendors`, no `onExhausted`, and the witness | clear an overlay without `ports.turn` → the catalog-like write, made from the copy it read before the clear, puts `onExhausted` back: X's last assertion |
| T22 | `ACompletedRunThatNeverToldIsToldByTheNextActivation` | the record is `{ completed, by: 'gone' }` → `migrated`: one `tell`, `told` true, nothing cleared, no claim written, no `after`. A further activation → `already`, no `tell` | stop on `completed` alone → no `tell` |
| T37 | `TheNotificationNamesAValueThatStaysInForce` — (f) | Four runs, each → `migrated` with one `tell`. (1) U, the per-side switch on, this side's overlay is X; before X's turn the person sets X to `good_enough` (`writeOverlay`): X is kept, and the sentence is `staysInForce(labelOf('good_enough'), WHERE.side)`. (2) U as it is: `staysInForce('Continue, and say so', WHERE.workspace)`. (3) U0 with the user layer `continue` and another machine's witness already there: `staysInForce('Continue, and say so', WHERE.user)`. (4) U0: everything is cleared → `NOW_AUTO` | always answer `NOW_AUTO` → (1)–(3); ask `ports.inForce()` before the clears → (4) says Ask a human stays |
| T38 | `ASettingsFileThatRefusesTheWitness_RemovesNothing` — (g) | U, `refuse('onExhaustedResetToAuto', …)` → `refused`: NO `update` of `onExhausted` in the log; the user layer still holds `human`; no witness; no `completed`; X and Y cleared; `after` called; `refused` called once with the error's text, and not again at a second refusing activation. Second half: the same with nothing in the user layer → `refused`, no marker, `refused` NOT called, the refusal in `log` (D12, U12). Cured → the next activation → `migrated` | go on to the removal after a rejected witness write → `human` is removed with no witness; tell whenever the file refuses → the second half's count |
| T40 | `ARefusalThatNamesAnUnknownKeyIsAWait_NotAWarning` | U; a hook before the first overlay turn undeclares the witness key — after step 0 has passed — so its write rejects with VS Code's words → `not-ready`: `refused` not called, no removal, no marker, the claim released, `after` called. Second half: the write rejects in other words (German) while the key is undeclared → `not-ready` too. Third half: the same German words with the key declared → `refused` | remove the belt → `refused` is called in the first two halves; drop the second question to the registry → the second half |

### S2.8.3 — Two windows: `src/test/onExhaustedMigrationTwoWindows.test.ts` (new)

Two windows, A and B, over one `ClientState` and one `FakeUserSettings`. The test resolves each pause itself and
calls `deliver()` before it does, unless the scenario says otherwise.

| # | Test | Scenario and assertion | Break-it: the line, and what goes red |
|---|---|---|---|
| T23 | `TwoWindowsStartingTogether_LeaveOneResultAndOneNotification` | U. A writes its claim; B, with nothing delivered, writes its own, which replaces A's at the client. Deliver; both pauses end. B → `migrated`; A waits out B's claim and → `already`. One `tell`; one removal; `by` is B; **A's write log holds its claim and nothing else**. The test's comment says: this is the DELIVERED case — each window heard the other within the claim's pause; when they do not, T41 and the parent's residual (iii) | go on after the claim's pause without `mine` → A writes `held` |
| T24 | `WindowBIsHeldAfterItsRead_AFinishes_ThePersonChooses_BChangesNothing` (N6's test) | The user layer and X hold `human`. B reads (nothing there), writes its claim and is HELD in its pause. A, which never saw B's claim, runs to `migrated`. The person chooses `human` in the user layer and in X. Deliver; B's pause ends → `already`. The user layer and X are `human`; one `tell`; `by` is A | none by one line: three hold it — the read after the pause (T23), `mine` (T26) and the witnesses (T9, T14). Its teeth are T25 |
| T25 | `AMarkerAloneIsNoLock_AndThatIsThePoint` | T24's interleaving, driven through a NAIVE run written inside the test: read the marker, clear what it sees, write the marker, tell. It ends with two notifications and the person's `human` reset. The test asserts exactly that, and says in its name why the witnesses exist | — (it runs no production line; it shows the fixture can see the defect) |
| T26 | `AWindowThatLostItsClaimWritesNothingMore` | U. A holds the claim and has cleared X. Before Y's turn the test writes another window's claim and delivers. A → `busy`: Y is untouched by A, A writes no marker and tells nothing | remove `mine` from the overlay turn → Y is cleared by A |
| T27 | `AFreshClaimIsWaitedOut_AStaleOneIsTaken_AClockAheadIsCapped` | a claim of another window 10 s old → the first pause asked is `CLAIM_STALE_MS − 10000`, then the run completes. A claim `CLAIM_STALE_MS` old → the first pause is `SETTLE_MS`. A claim whose `at` is five minutes AHEAD → the pause is `CLAIM_STALE_MS`. A fresh foreign claim rewritten during each wait → `busy` after `CLAIM_TRIES` waits, and this window wrote nothing | remove the wait → the first pause is `SETTLE_MS`; remove the cap → a pause above `CLAIM_STALE_MS` |
| T28 | `OnlyTheWindowThatReadsItsOwnNameInTheMarkerTells` | U. In the pause after A's marker the test writes `by: 'other'` and delivers → A: `already`, no `tell` | tell without reading `by` back → one `tell` by A |
| T29 | `TheFakesBehaveAsTheEditorIsRead` | `ClientState`: a window sees its own `update` at once; the other sees it only after `deliver()`; a window's `update` replaces the client's WHOLE value, so a key the other window wrote and that was not delivered is gone; an object is stored as its JSON clone, an `undefined` member dropped. `FakeUserSettings`: an undeclared key has no `defaultValue`, a declared string key with no default has `''`; a value written to an undeclared key is refused in VS Code's words, its removal is accepted; a snapshot does not see a later write. The comment cites V1–V3, V5 and V6 and says what is NOT proved (R10) | — (it pins the doubles every test rests on) |
| T35 | `AStaleWindowBringsAnOverlayBack_TheNextActivationClearsIt` — (d) | U0. S is a window whose copy predates the run and gets no delivery. A runs → `migrated` (`deliver(A)` only). The person chooses `human` in the user layer. S writes an unrelated key. Asserted as the residual it is: the client's value has no `completed` and X holds `human` with no witness member. Deliver to all; a new activation C → `migrated`: X is cleared and witnessed; `tell` was called a second time; the user layer is still `human`, and C made no user-layer `update` — the witness in the settings file was out of the stale write's reach | read the user layer's witness from the record instead of the settings → C removes `human` |
| T36 | `NoMarkerFromACopyWithAnUnsettledLocation` — (e) | U. S takes its copy after A's `held` is stored and before X's turn. A runs on; its settle pause ends; the test's `after` — the last call before the marker's check — makes S write an unrelated key and delivers, so A's copy now holds A's claim, `held`, and X and Y as they were. A → `incomplete`: no `completed` in any copy, no `tell`, the claim released. The user layer is cleared and witnessed all the same. The next activation completes | compute `left` before `ports.after()` (or before the pause) → `completed` is written while X holds `human` |
| T41 | `TwoWindowsThatNeverHearEachOther_RemoveTheUserValueOnce_AndMayTellTwice` | U0, `hold('onExhaustedResetToAuto', 'unlanded')`, and no `deliver()` until both runs ended. Both windows take the claim in their own copy, clear the overlays, read the user value (`human`) and no witness, and ask for their witness write before either lands. The test releases both in one synchronous stretch: the file holds the word, written twice. A goes on first, reads `human` again and removes it. B goes on, reads the value AGAIN, finds nothing to migrate, and asks for nothing. Exactly ONE `update('onExhausted', undefined, GLOBAL)` in the log. Both → `migrated`, and each tells: two `tell`s, asserted as the parent's residual (iii). After `deliver()` the stored value has `completed`, X and Y cleared and witnessed; a third activation → `already`. Second half, asserted as the residual it is (R3): with `hold('onExhausted', 'unlanded')` as well, B reads `human` before A's removal has landed, and the log holds TWO removals | remove with the value read BEFORE the awaited witness write (carry it across the `await`) → two removals in the first half |

### S2.8.4 — Two machines: `src/test/onExhaustedMigrationTwoMachines.test.ts` (new)

Machines A and B: two `ClientState`s — each its own overlays, record and windows — sharing ONE `FakeUserSettings`,
the synced user layer after Sync has delivered. What a real Sync does between two deliveries is not modelled (R1, R10).

| # | Test | Scenario and assertion | Break-it: the line, and what goes red |
|---|---|---|---|
| T32 | `ASecondMachineKeepsAChoiceMadeOnTheFirst` — (a) | The user layer `human`; A has overlay XA (`human`), B has XB (`human`). A runs → `migrated`. The person chooses `human` in the user layer. B runs → `migrated`: XB is cleared and witnessed; the user layer is `human`; B made NO user-layer `update`; B's `tell` is `staysInForce('Ask a human', WHERE.user)` | skip the read of the witness in step 4 → B writes the witness again and removes `human` |
| T33 | `AFirstMachineWithNothingToRemoveStillWritesTheWitness` — (b) | The user layer holds nothing. A runs → `migrated`: one user-layer `update`, the witness. The person chooses `human` on A. B runs → `migrated`, the user layer is `human`, B made no user-layer `update` | write the witness only when `'user'` is in `held` → B removes `human` |
| T34 | `AChoiceDeliveredDuringTheAwaitedWriteStopsTheRemoval` — (c), renamed (U22) | B's user layer `human`, recorded in B's `held`; no witness yet. `hold('onExhaustedResetToAuto', 'unlanded')`: B has read no witness and asked for its write. Before it lands the test delivers machine A's file: the witness, and `continue` — a choice made on A after A's run. Release → B's write puts the same word there again; B reads the value again, finds `continue` where `held` says `human`, and makes no removal. B → `migrated`, telling `staysInForce('Continue, and say so', WHERE.user)`. Second half, asserted as the residual it is (R1 (c)): the delivered choice is `human`, the recorded word itself → B removes it; the comment says no test of the value can tell the two apart | drop the comparison of the value with `held` → `continue` is removed |

### S2.8.5 — In a real editor

| # | Test | Scenario and assertion | Break-it |
|---|---|---|---|
| T31 | `theOnExhaustedMigrationCompletesInARealEditor`, `src/test/host/onExhaustedMigrationScenario.ts` (new), registered in `scenarios.ts` | the shipped extension, activated: `inspect('onExhaustedResetToAuto').defaultValue` is not `undefined` (D13's signal in a real registry, for a key declared with no default); within 30 s (`until`) the exported record reads `completed` equal to the extension's version and `told` true; `inspect('onExhausted').globalValue` is `undefined`; `inspect('onExhaustedResetToAuto').globalValue` equals `USER_WITNESS_WORD` — a real settings file accepted the witness. It says what it does NOT drive: an update in place, two windows, two machines, a value saved before activation | — (CI only, D16) |

Three existing guards go red and green with this story, and are named in the summary: `sonarExclusions.test.ts`
(red while the host module is not listed), `notificationSites.test.mjs` (red until `count-notifications.mjs
--write`) and `helpCoverage.test.ts` (red from the manifest property until the alias and the help sentence).

**Count: 42 scenarios** — T1–T30 and T32–T42 in the ordinary suite (41), T31 in a real editor. The stories
document's (a)–(g) are T32, T33, T34, T35, T36, T37 and T38; (c) carries a new name since entry 3.1 (U22).

## S2.9 — Files, commit order, commands, lint

**Files** (stories document § 6 and § 10.1 item 14, with X9):

| File | What |
|---|---|
| `src_vs_code/package.json` | one property beside `coai.catalogMigration` (D18): `"coai.onExhaustedResetToAuto": { "type": "string", "markdownDescription": "Written by ConnectOtherAIs, not by hand. It records that **When the rounds run out** in these settings was reset to Auto once. While it is here, a value you choose afterwards is kept — also on the other machines Settings Sync carries your settings to." }`. No `default`, no `scope` |
| `src_vs_code/src/onExhaustedMigration.ts` | new, pure: § S2.2–S2.4 |
| `src_vs_code/src/onExhaustedTold.ts` | new, pure: § S2.3 |
| `src_vs_code/src/onExhaustedMigrationHost.ts` | new, host: § S2.5 |
| `src_vs_code/src/extension.ts` | the call, the `CoaiExports` member, the test-mode spread (§ S2.5). Nothing else, and nothing in S1's lines |
| `src_vs_code/src/configTransfer.ts` | one row in `NEVER_TRANSFERRED` (D19): `onExhaustedResetToAuto: 'whether this machine\'s settings were reset to Auto once'`, with a comment that names this file |
| `src_vs_code/src/helpContent.ts` | one English sentence at the end of `the-gate`'s `whatCanGoWrong`: *"A value chosen before this version is reset to Auto once; `coai.onExhaustedResetToAuto` in your settings is written by ConnectOtherAIs, not by hand, and records it."* S3 rewrites it in place and translates it (stories § 6, § 9.2) |
| `src_vs_code/src/test/helpCoverage.test.ts` | one `SETTING_ALIAS` row: `'coai.onExhaustedResetToAuto': 'reset to auto once'` — words S3's own sentence keeps |
| `.github/workflows/sonarcloud.yml` | the host module in `sonar.coverage.exclusions` |
| `src_vs_code/notification-sites.json` | regenerated: two new sites |
| `src_vs_code/src/test/onExhaustedMigrationFakes.ts`, `onExhaustedMigration.test.ts`, `onExhaustedMigrationRun.test.ts`, `onExhaustedMigrationTwoWindows.test.ts`, `onExhaustedMigrationTwoMachines.test.ts` | new |
| `src_vs_code/src/test/host/onExhaustedMigrationScenario.ts`; one entry in `src/test/host/scenarios.ts` | new; one line |
| `research/module_tests.md` | one row: "A saved *When the rounds run out* is reset to Auto once — every side's overlay and the user layer, resumed, with two windows and two machines, and a later choice kept", naming the four test files, T31, and R10 as what is not covered |

Not touched: `src/modelKeys.ts`, `src/settingsShape.ts`, `src/onExhaustedPanel.ts` (S1's; the last is imported),
`src/sideSettings.ts`, `src/catalogMigrationHost.ts`, `src/catalogMigrationRun.ts` (both imported),
`src/panelProvider.ts`, `src/panelView.ts`, anything under `src_mcp`.

**Commit order.** S2 is coded after S1's commits — its fix forward included — are on the branch (T39 reads S1's
reader, and a cleared location must read `auto`).

1. The fakes and T29. Then T1–T6 and T39 RED, the pure decisions and `onExhaustedTold.ts` GREEN.
2. T7–T22, T37, T38 and T40 RED, the run GREEN, each break-it.
3. T23–T28, T35, T36 and T41 RED and GREEN, each break-it; T25 is green from the start and says so.
4. T32–T34 RED and GREEN, each break-it.
5. The manifest property with its alias and help sentence, the `NEVER_TRANSFERRED` row and T42; the host module,
   the three `extension.ts` hunks, `sonarcloud.yml`, the census, T30, T31.
6. `research/module_tests.md`.

**Commands**, from `src_vs_code/` (stories document § 3):

```bash
npm ci                                                          # once
npm run compile                                                 # read the EXIT CODE: tsc emits even when red
node scripts/run-tests.mjs out/test/onExhaustedMigration.test.js
node scripts/run-tests.mjs out/test/onExhaustedMigrationRun.test.js
node scripts/run-tests.mjs out/test/onExhaustedMigrationTwoWindows.test.js
node scripts/run-tests.mjs out/test/onExhaustedMigrationTwoMachines.test.js
node ../.agents/conventions/tools/low-priority.mjs npm test     # the whole suite (compile + run-tests)
npm run lint                                                    # eslint src — what CI runs
node scripts/count-notifications.mjs --write                    # after the two notify calls; commit the file
npm run test:host                                               # cannot start on this machine (no libnss3); CI runs it
cd .. && node .agents/conventions/tools/plan-lifecycle.mjs      # before every commit that touches todo/
```

The Release build, `test:seam`, `test:contract` and `test:on-exhausted-compat` of § 3 are S1's. S2 changes nothing
they read, and they must stay green.

**Lint.**

- Every new function: `complexity` ≤ 4 and ≤ 50 lines. The run is a list of step functions; a step that needs two
  decisions is two functions. Step 4 is at least three: the witness, the read-back, the refusal. Split, never suppress.
- The counts in `src_vs_code/eslint-suppressions.json` must not grow, and no entry is added for a new file.
- All three new modules stay far under the 800-line cap; the run module stays under 400 by keeping the words out.
- The module that imports `vscode` is in `sonar.coverage.exclusions`; the pure ones are not (F13).
- The two `notify` calls pass their `code` as a string literal at the call site; then
  `node scripts/count-notifications.mjs --write`, and `notification-sites.json` is committed.
- Tests import `../../scripts/inRunFolder.cjs` first. No test sleeps: every pause is the `pause` port.

## S2.10 — What the Opus reviewer checks

1. No `await` stands between the read of an overlay record and its `update`, and the witness is in that same
   `update` (T13). Each turn takes its overlay from a read made inside the turn.
2. The user layer's witness is the setting, and what is written is the one constant `USER_WITNESS_WORD` — no
   version, no window name. It is written and awaited before `user.clear()` is called. After that `await` the value
   is read again from a configuration asked for again, and `held` again from the store; the value test alone decides,
   and no `await` stands between that read and the call of `user.clear()`. The witness is withdrawn after a refused
   removal; it is never read from a workspace or folder layer; nothing else ever removes it (T6, T14–T16, T34, T38,
   T41).
3. No value read from the record or from the settings is carried across an `await`: every decision reads again. What
   crosses one is the run's own — the version, the window's name, the reason of a refusal.
4. No `await` stands between the due test of every location and the marker's `update`, and that test comes after
   `after()` (T17, T36). The marker is a new record: nothing of the old one is spread into it.
5. `mine` is asked before every write of steps 2, 3, 4 and 6, and after the claim's pause — and no comment or name
   calls the claim a lock.
6. The catch at the top of the run writes nothing. `after` is called once on every way out of a run that took the
   claim, a throw included.
7. The sentence is chosen at the moment of telling, from `ports.inForce()`, and `inForceOf` reads what the reader
   reads (T37, T39). The host's `inForce` makes the three reads `readerFor` makes.
8. The user-layer port is built on a function that asks for the configuration again, and only the Global target is
   ever passed (T6, T20).
9. The prefix comes from `overlayKeyOf('')`; the registry is not read. `known()` is `unknownKeysOf`, and the belt is
   `unregisteredKeyIn` — neither test is written a second time.
10. Nothing calls `setKeysForSync`. `package.json` gains exactly one property, with no `default` and no `scope`.
11. `NOW_AUTO`, the second sentence, its three places and `NOT_RESET` are the parent's words; no new text says more
    than requirement 5; `NOT_RESET` is shown only when a value is in force in the user layer.
12. Each break-it was run and its failure is quoted; T25 and T29 are reported as green from the start, with the
    reason; T35's and T41's residual assertions are named as residuals; T31's line from the CI job is quoted, or the
    host wiring is reported as unverified.
13. `eslint-suppressions.json` shows no new entry and no higher count.

## Definition of Done — E2.S2

- [ ] T1–T30 and T32–T42 green; each seen RED first on its assertion, and broken as its row says, with the three
      observations in the summary.
- [ ] T31 added; its result from CI quoted, or the host wiring reported as unverified.
- [ ] The user value and every overlay's value are cleared once; a location that holds `auto`, a slot that is no
      record, and a workspace or folder value are untouched (T7, T20).
- [ ] Each location's witness is written with its clear (an overlay, T13) or before it (the user layer, in the user
      settings, also when nothing is removed: T14, T15, T19, T33), and is withdrawn after a refused removal (T16).
- [ ] The marker is written only from a copy where every location is settled, with no `await` before the write, and
      after `after` (T10, T17, T36).
- [ ] A resumed run keeps a choice made after the interruption, also when the host died right after a clear
      (T12, T14); an attempt that may have landed is never repeated (T15).
- [ ] N9: a second machine keeps a choice made on the first, also when the first had nothing to remove, and a choice
      that arrives during the awaited write and differs from the recorded value stops the removal (T32–T34).
- [ ] Two windows leave one result; one notification when each hears the other within the claim's pause (T23, T24,
      T26, T28), and the user value is removed once when they do not and the second reads after the first's
      removal — two notifications then, the parent's residual (iii) (T41). A second activation does nothing (T8); a later choice is kept, also with the marker lost (T9).
- [ ] A stale window's write that brings a cleared overlay back is cleared by the next activation, and the user
      layer's choice survives it (T35).
- [ ] The notification says Auto only in a window that runs Auto, and otherwise names the value in force and where
      it is set (T37, T39); the warning is shown once while a refusal lasts, and never for an unknown key (T16, T38,
      T40).
- [ ] The run starts from activation and no test opens the panel (T30).
- [ ] `npm run lint` and `npm test` green; suppressions not grown; `sonarExclusions.test.ts`, the notification census
      and `helpCoverage.test.ts` current; `plan-lifecycle.mjs` clean.
- [ ] § S2.7's residuals are listed in the PR summary, for S3's section in `research/module_extension.md`.

## Reported to the coordinator

Each item says whether this spec's author judges it a change to the parent. Nothing in the parent or in the stories
document was edited.

**Where the nine items of the first commit landed** (stories § 10.1 item 16):

| Item | Landed |
|---|---|
| U1 — Settings Sync | the parent's entry 2.2 and § 4.6: the witness in the user settings. Built here: D7, D8, T32–T34 |
| U2 — the opposite gap | the parent's N8 (its last sentence) and residual (i). Here: R2, T15 |
| U3 — a window updated in place | the stories document § 7.3 (S1's fix forward, `b0095546`) and D13 here |
| U4 — `globalState` is one value | the parent § 10, *Beside the plan*: a todo of its own |
| U5 — "what grows" | the parent § 9, rewritten |
| U6 — a location that holds `auto` is left | the parent § 4.6, *What it clears* |
| U7 — words the parent did not hold | the parent § 4.6: the three texts, a fresh install told, the second form |
| U8 — an older extension with an older server | the parent § 4.6, residual (iv), and the stories document § 9.4's fifth sentence |
| U9 — edits in other files | done by the coordinator (X1–X7) |

**Found by this revision (U10–U20).** Each item stands as it was reported, built then as the parent stood; the
coordinator's decision of 2026-11-17 follows it in a table, and what is still open comes last (U21–U24).

- **U10 — two machines' witnesses are a Settings Sync conflict. Judged: writing it down is wording; curing it IS a
  change.** *The conflict.* § 4.6 says of the witness: "Its value names the version and the run that wrote it, so a
  run recognises its own write when it reads it back", and § 11 C5 (c) says how Sync's merge treats two machines'
  witness values was "not checked". It is read now (V9): a key added on both sides since the last sync with DIFFERENT
  values is a conflict, and two runs' texts always differ. So in the window the parent already names — a machine
  whose run comes before Sync has pulled the other machine's file — the person also gets a Settings Sync conflict on
  a key they never wrote. A second machine that starts later and runs within seconds of its start may well be inside
  that window; when Sync's first pull happens was not measured, and what the editor shows for the conflict was not
  read. *What the unique text buys, by the same reading:* one removal instead of two when two windows of ONE machine
  both write a witness (T41; the second removal finds nothing), and T34's first half, which Sync's own merge does not
  produce for two different texts. A delivery BEFORE this run's write lands is caught by the value test alone, with or
  without a unique text. *The cure, if the coordinator wants it:* the same fixed word on every machine — not the
  version, two machines can run two versions — and the read-back reduced to the value. *Minimal wording:* § 4.6, "Its
  value names the version and the run that wrote it, so a run recognises its own write when it reads it back" → "Its
  value is one fixed word, the same on every machine, so Settings Sync merges two machines' witnesses without a
  conflict"; in *The order*, "a witness that is not this run's, or a value no longer the one recorded" → "a value no
  longer the one recorded"; C5 follows. Here: `witnessText` becomes a constant, step 4 loses one comparison, T34's
  first half goes and T41 no longer says which window removes. *If it is only written down:* § 4.6, *Settings Sync
  during a run*, gains "and the two machines' witnesses, being two texts, are a conflict Sync asks the person to
  resolve".
- **U11 — one more Sync window than § 4.6 names. Judged: wording, NOT a change.** § 4.6 lists "a delivery that lands
  after that read and before the removal is written". There is a second: a delivery between the run's FIRST read of
  the witness and its own witness write. The run's write then replaces the other machine's witness, and the read-back
  cannot see it; the value test stops the removal unless the choice made elsewhere equals the recorded value (R1 (c) now,
  T34's comment). It is the size of one settings write, like the first. *Minimal wording:* in that sentence, "after
  that read and before the removal is written" → "between the first read of the witness and its write, or after the
  read-back and before the removal is written".
- **U12 — the warning when nothing is held. Judged: a reading inside the parent, NOT a change; say if you read it
  otherwise.** § 4.6: "When the settings file refuses the change … the person is told once per machine while it
  lasts", and the text says "Your earlier choice there is still in force". When the WITNESS is refused and the user
  layer holds nothing, that sentence is false. Built: no marker, tried again at the next start, logged — and the
  warning only when the user layer holds a value to migrate (D12; T38, second half). If "told" is meant in every
  case, one condition goes and T38's second half expects one call.
- **U13 — a settings file that never accepts a write is never told Auto. Judged: a residual to write down, NOT a
  change.** With "no marker is written" as the parent says, a person whose `settings.json` is read-only for good
  never completes the run: overlays are cleared at the first start, every start tries again, and the notification is
  never shown (R12). With no value in the user layer nothing is shown at all. *Minimal wording:* one more residual in
  § 4.6, "(vii) a settings file that never accepts a write: the run never completes, the notification is never shown,
  and every start tries again". Completing without the user layer's witness instead would be a change.
- **U14 — Export config and Import config (D19). Judged: NOT a change; one file more than the stories document
  lists.** A config file carries every declared setting that differs from its default (F20), so it would carry the
  witness, and an import would write it. On a machine whose own run ended `refused` or `incomplete`, an imported
  witness settles the user layer with its old value never cleared. Built: one row in `NEVER_TRANSFERRED` and T42, as
  the catalog migration's marker has. If the coordinator reads this as outside S2, D19, the row and T42 go, and R13
  becomes that residual.
- **U15 — the files (X9). Judged: NOT a change.** A third new module, `src/onExhaustedTold.ts`, so the run module
  stays small and the sentences have one home S3 can quote from; a fourth test file for the two machines; the
  `configTransfer.ts` row of U14. § 6's table needs the three rows.
- **U16 — words a person reads that the parent does not hold. Judged: NOT a change; the coordinator owns the
  texts.** (a) The manifest description of `coai.onExhaustedResetToAuto` (§ S2.9) — the parent gives only its opening
  words. (b) The one English help sentence (§ S2.9), which S3 rewrites. (c) The `NEVER_TRANSFERRED` reason, which an
  import shows when it refuses the key. (d) In the second sentence `{label}` is `labelOf`'s text, so Good enough
  reads "… but Good enough — take what’s true and move on stays in force …": a dash inside the sentence. C2 gives
  that label no short form; S1 owns it.
- **U17 — a value in a remote machine's own user settings (R11). Judged: a residual, NOT a change.** Read in VS
  Code's source (V7), not observed. It takes a hand edit of a remote's settings file. The notification names such a
  value as "your user settings".
- **U18 — the ready signal (X8) and two references (X10). Judged: wording in the stories document.** § 10.1 item 8
  says "`inspect` of it is not `undefined`"; an unknown key's `inspect` is an object (V6), so the signal is its
  `defaultValue`. The parent's "(a write of an unregistered key is refused)" is right as it stands: verified in
  `configurationEditing.ts` (V5) and observed here on 2026-10-09 (F17). It holds for a write of a value; a removal of
  an unknown key is not refused, which the run never needs.
- **U19 — mechanisms refined beyond § 10.1's list. Judged: NOT changes; each meets words the parent states.**
  (a) The plan records a witnessed location too, and the witness is asked only in `isDue` (D6) — one line, one
  break-it. (b) The overlays are cleared by a loop that picks from a fresh read at every turn, bounded by
  `OVERLAY_TURNS` (D9) — "every step reads again". (c) `after` is called once on every way out of a run that took the
  claim, a fresh install included (D5) — "also in a run that ends incomplete". (d) The marker is a new record,
  `{ completed, by }`, with `held` not carried into it (D10) — § 9, "while a run is in flight". (e) The
  `defaultValue === 'auto'` test is dropped, as § 10.1 item 8 left to this spec (D13). (f) A witness write refused for
  an unknown key ends the run as `not-ready`, never the warning (D13, T40) — the catalog migration's belt. (g) The
  notice's code is `on-exhausted-migrated`: one site, two sentences. (h) The pause before the mirror stays, as a best
  effort like the claim: it gives the marker's check a chance to see a write that came late (T17).
- **U20 — the `todo/README.md` row (X11).** Proposed text:
  `| [PLAN_auto_never_asks_a_person_E2_S2_spec.md](PLAN_auto_never_asks_a_person_E2_S2_spec.md) | The E2.S2 cornerstone spec — **spec only, written 2026-11-17 by Fable before its code and revised the same day to epoch 3 of its parent; nothing coded.** The one-time migration: every overlay found by its key and the user layer; a witness written with each clear (an overlay, in its record) or before it (the user layer, the setting coai.onExhaustedResetToAuto, which Settings Sync carries); a claim that is a best effort; nothing carried across an await; the marker only from a copy where every location is settled; the mirror before the marker; one notification, true for its window; 42 scenarios. Its findings are reported to the coordinator (U10–U20); none is built as a change. |`

**The coordinator's decisions (2026-11-17), and what each did here.**

| Item | Decision | Here |
|---|---|---|
| U10 | cured: the parent's entry 3.1 — one fixed word on every machine, the read-back reduced to the value test | built: `USER_WITNESS_WORD` is `'auto'`; D7, D8, § S2.2, step 4, R1, R3, T6, T7, T19, T31, T34, T41 |
| U11 | its wording is in the parent's *Settings Sync during a run* | the cure does not change that window: the run now writes the same word over the other machine's witness, which loses nothing, and the value test decides as before (R1 (c); T34) |
| U12 | built as written here | D12; T38, second half |
| U13 | the parent's residual (vii) | R12 |
| U14 | kept: D19, the `configTransfer.ts` row and T42 are inside S2 | no change |
| U15, U18, U20, X8–X11 | the coordinator edits the stories document and `todo/README.md` | the row's last sentence in U20 should now read: "Its one finding that changed the parent is entry 3.1 (U10), built here; the rest are residuals and wording." |
| U16 | the texts stand; the coordinator owns them | no change |
| U17 | the parent's residual (viii) | R11 |
| U19 | no word against it | no change |

**Still open after the decisions.**

- **U21 — two phrases of § 4.6 now ask for a read that decides nothing. Judged: wording, NOT a change.** *One witness
  per location* says the witness is "written — and read back — before the user value is removed", and *The order*
  says "then read the witness and the value AGAIN — a value no longer the one recorded stops the step". Entry 3.1
  makes the read-back the value test alone. So step 4 reads the value and `held` again, and does not read the
  witness: one word cannot be told from another machine's. *Minimal wording:* "written before the user value is
  removed"; "then read the value AGAIN". Built: entry 3.1. If the witness read is meant to decide something — a
  witness that is gone after the await stops the removal — that is one comparison and one half-scenario more, and it
  is not built.
- **U22 — (c) has a new name. Judged: wording in the stories document.** § 10.1 item 13 names
  `AWitnessDeliveredDuringTheAwaitedWriteStopsTheRemoval`. A delivered witness is the same word now and stops
  nothing; the delivered VALUE does. T34 is `AChoiceDeliveredDuringTheAwaitedWriteStopsTheRemoval`.
- **U23 — what two windows of one machine rest on now. Judged: wording for residual (iii), NOT a change.** With one
  word, two windows that both find no witness both write it, and only the value test stands between them. The second
  to read removes nothing when the first's removal has landed (T41). When it has not, the removal is asked for twice,
  and the second takes a choice only if the person made one in between — the time of one settings write (R3; T41,
  second half). The run-named text had the losing window stand aside. *Minimal wording:* residual (iii), after "can
  both run and both notify": "and can both ask for the user value's removal; the second removes a choice only if one
  was made in the time of one settings write".
- **U24 — C5 and § 10.1 item 4 of the stories document still describe the old witness. Judged: wording that follows
  entry 3.1.** C5: "a string naming the version and the run that wrote it"; item 4: "Its value names the version and
  this window's name … a witness that is not this run's … stops the step". Both become: one fixed word, `auto`, and
  the value test.
