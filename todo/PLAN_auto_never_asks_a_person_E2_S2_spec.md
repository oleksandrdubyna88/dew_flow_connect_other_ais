# PLAN — E2.S2 spec: the one-time migration — every saved choice reset to `auto` once, and a later choice kept (CORNERSTONE)

> Status: **spec only, written 2026-11-17 by Fable before its code — nothing coded.** Cornerstone spec of story E2.S2
> of [PLAN_auto_never_asks_a_person_E2.md](PLAN_auto_never_asks_a_person_E2.md) (its § 8; parent
> [PLAN_auto_never_asks_a_person.md](PLAN_auto_never_asks_a_person.md), § 4.6 *the one-time migration*, `## Epics`
> § E2 done-when 3 and 4, notes N5–N8, § 10 Q1, § 8 PR 2 step 2, § 13 item 6). It keeps no change ledger: a change is
> an entry in the parent, and the parent's epoch 2 already holds one (entry 2.1). **This spec makes no change to the
> parent.** Where the parent cannot be met as written, the item is in *Reported to the coordinator* at the end, and
> what is built is the parent as written. It also settles the finding the plan gate on E2 accepted on 2026-11-17
> (codex, Major: a host that dies between a clear and its progress record) — D7, T14 and T15.
>
> Scope: `src_vs_code` only. NEW `src/onExhaustedMigration.ts` (pure) and `src/onExhaustedMigrationHost.ts` (host);
> one call and one test-mode export in `src/extension.ts`; the host module in `.github/workflows/sonarcloud.yml`;
> `notification-sites.json` regenerated; three test files, one fakes file, one real-editor scenario; one row in
> `research/module_tests.md`. No change to the value, the select, the wire or the gate-section lines (S1), to the help
> or the CHANGELOG (S3), to `src_mcp`, or to a workspace or folder value (Q1, the owner's decision).
> New words a person reads, and no others: the parent's notification sentence (§ S2.5) and one warning for a
> settings file that refuses the change (§ S2.5).
> Coder: Sonnet. Review: Opus. Read at `111b34d6` (the committed tree; S1's uncommitted edits in the same worktree
> move a few lines). Paths are from the repository root unless they start with `src/`, which is `src_vs_code/src/`.

## Changes to § 4 and § 6 of the stories document — NOT made here

The stories document lets this spec refine a name in C4–C6 and asks for § 4 and the S3 brief to be edited in the
same commit. This spec's author was told to edit no other file, so the edits are listed for the coordinator.

| # | Where | It says | This spec says | Why |
|---|---|---|---|---|
| X1 | C4 | `startOnExhaustedMigration(context, mirror)`; `mirror` is `() => mirrorSettings(settingsSync)` | The same function and position, with three arguments: `(context, version, after)`. `version` is `extensionVersion(context)`. `after` is `() => { mirrorSettings(settingsSync); void panel.render(); }` | An overlay clear fires no configuration event, so an open panel would go on drawing the old value; the catalog migration's callback does both (`extension.ts:456-459`). The version is handed in because its one reader is private to `extension.ts` (`:1341`) and must not be written twice |
| X2 | C5 | the key's value is the version that completed the run | The key holds one RECORD (§ S2.2). Its `completed` member is that version: the parent's marker. A bare string in the slot reads as completed too | N6 and N8 need a claim and what each location held, and C5 leaves "what else it holds" to this spec |
| X3 | C6 | touches the user layer and every overlay's `onExhausted` | Also: writes one member, `onExhaustedAutoMigration`, INTO each overlay record it clears (the witness, D7); leaves a location that holds `auto` unwritten (D2); has a second sentence, shown at most once per machine, for a settings file that refuses the change (D12) | D2, D7, D12 |
| X4 | § 6, `src/extension.ts`, S2 | one call after `:438` | one call after the first `mirrorSettings(settingsSync);`, AND a test-mode member of `CoaiExports` (`:102`, `:917-920`) for the real-editor scenario | D16. Both hunks are far from S1's `:434` |
| X5 | § 6 | — | S2 also adds a line to `src/test/host/scenarios.ts` | D16 |
| X6 | § 9.2, § 9.4 (S3) | "choose again afterwards and it is kept" | true on one machine. With Settings Sync it is not, as built (U1). S3 writes the sentence only after the coordinator settles U1 | § S2.7 |
| X7 | `todo/README.md` | — | a row for this file (text in U9) | `plan-lifecycle.mjs` |

## S2.0 — What was read

Read at `111b34d6`.

| # | Fact | Where |
|---|---|---|
| F1 | An overlay is the record under `coai.settingsOverlay@<sideKey>` in the client's `globalState`. `overlayKeyOf(key)` builds the key | `src/coaiInstall.ts:353-363` |
| F2 | `readOverlay` reads a slot that is not a record as `{}`. `writeOverlay` is one `update` of `{ ...readOverlay(), [section]: value }`: a member it does not name is kept | `src/sideSettings.ts:24-28`, `:33-41`, `:102-104` |
| F3 | `seedIfEmpty` seeds only when the record has NO member; `seedOverlay` copies every overlaid key the reader answers, the manifest default included | `src/sideSettings.ts:85-101` (`:91`); `src/settingsShape.ts:404-414` |
| F4 | `overlaidReader` answers a section from the overlay only when the record has that own property. Nothing in `src/` walks the members of an overlay record | `src/settingsShape.ts:392-395`; a search for `readOverlay`, `overlayKey` and `settingsOverlay@` over `src/` (seven files) |
| F5 | The side registry: `coai.sideSeen@<sideKey>`, pruned after 90 days, removed by **Forget**. An overlay is not removed with its registry record | `src/sidesSeen.ts:25`, `:30`, `prunedKeys`, `forgetSide` |
| F6 | A person's choice is written by `saveSetting`: into this side's overlay when the per-side switch is on, else into the user layer (`ConfigurationTarget.Global`). Never into a workspace | `src/sideConfig.ts:173-187` |
| F7 | `onExhausted` is read through `config.get` (the merged value: the registry's default, the user layer, the workspace, the folder), then this side's overlay over it | `src/sideConfig.ts:48`; `src/modelKeys.ts:66`; `src/sideSettings.ts:66-73` |
| F8 | `mirrorSettings` returns `void`: it starts a schedule and does not wait for the file. Every activation calls it once | `src/extension.ts:967-983`; `:438` |
| F9 | The configuration listener mirrors and re-renders on every `coai` change, in every window that hears it | `src/extension.ts:603-616` |
| F10 | The catalog migration: one queue per extension host (`CatalogTurns`); `inCatalogTurn(work)` takes one turn; this side's overlay is written by a fresh read-modify-write per key | `src/catalogMigrationHost.ts:34`, `:70-72`, `:280-283`; `src/catalogTurns.ts` |
| F11 | Activation writes `globalState` beside the run: the first-seen record and this side's stamp | `src/extension.ts:443`, `:447` |
| F12 | `notify` records, then shows, and never waits for the person. A notice's `code` is a string literal at the call site | `src/notify.ts:247-252`; `src/notice.ts` |
| F13 | A module that reaches `vscode`, directly or through an import, must be in `sonar.coverage.exclusions`, and one that does not must not be | `src/test/sonarExclusions.test.ts:60-98` |
| F14 | A real-editor scenario reads this extension's `globalState` only through `CoaiExports`; test-only members are spread in under `ExtensionMode.Test` | `src/extension.ts:98-110`, `:917-920`; `src/test/host/sideSeenScenario.ts` |
| F15 | Observed 2026-10-09: VS Code updated the extension in place before it activated. The new code then ran in a window whose settings registry did not know the new version's keys, until a reload. A registered key always answers a `defaultValue` through `inspect` (held to a real editor) | `research/PLAN_catalog_migration_waits_for_its_settings.md`; `src/test/host/migrationKeysScenario.ts` |
| F16 | Measured 2026-10-10: a `globalState` write from one side reaches an already-open window of the other side with no reload. The lag was NOT measured: 20 s and 23 s are upper bounds | `research/RESULTS_side_registry_reaches_the_other_window.md` |

Read in VS Code's own source, `microsoft/vscode` branch `main`, on 2026-11-17. It is not pinned to the editor a
person runs, and nothing below was observed in an editor from this checkout.

| # | Fact | Where |
|---|---|---|
| V1 | `Memento.update` changes the window's own copy at once (an object is stored as its JSON clone), and schedules ONE send, 0 ms later, of the extension's WHOLE `globalState` object. So the client stores one value per extension, not one per key | `src/vs/workbench/api/common/extHostMemento.ts`, `update` and the scheduler; `mainThreadStorage.ts`, `$setValue` |
| V2 | When the client's value changes, every window REPLACES its whole copy with it. A write a window had not sent yet is dropped with no error, and its promise still resolves | `extHostMemento.ts`, the `onDidChangeStorage` listener |
| V3 | So two windows that write DIFFERENT keys within the lag can lose one of the writes, and a write built on a stale copy puts back every key that copy held. `await update()` proves nothing; only a later read does | V1 and V2 together |
| V4 | `setKeysForSync` registers keys per extension id AND version. Settings Sync carries the values in its *Extensions* resource, and applies them on another machine only when the extension installed there has the SAME version | `extHostMemento.ts`, `ExtensionGlobalMemento`; `src/vs/platform/userDataSync/common/extensionsSync.ts`, the two `manifest.version === e.version` tests |

**Not measured, and no test here can measure it:** the lag of V2 between two real windows (F16 gives upper bounds
only). § S2.7 says what rests on it.

## S2.1 — Decisions

| # | Input (stories document § 8) | Decision |
|---|---|---|
| D1 | 1, N5 — the reach | Overlays are found by walking `store.keys()` for the prefix `overlayKeyOf('')`, never through the side registry. The registry forgets a side after 90 days or on **Forget**, and its overlay stays (F5): a side that comes back would bring a frozen `human` to a machine whose marker says done. The walk needs nothing from the registry |
| D2 | 1 — what is cleared | A location HOLDS a value to migrate when the key is present and its value is not the string `auto`. A junk word, `null` or a number is held too. A location that holds `auto` is not written: it is already what O1 wants, and an explicit `auto` in an overlay wins over a workspace value (S1's `workspacePolicyOf`), so removing it could take that side OFF `auto`. A slot that is not a record holds nothing and is never written. `onExhausted` is removed from a record by one `update` of the record without it; every other member is kept |
| D3 | the order | Overlays first, in key order. The user layer last. The user-layer clear is the one configuration event the run causes, and that event is what makes every other open window mirror (F9); it must come after the overlays are cleared |
| D4 | 2, N6 — two windows | No lock exists across the windows of a profile: no shared file system (a WSL or SSH window), and `globalState` is one last-writer-wins value (V1–V3). The mechanism is four things together (§ S2.4): **(a)** a CLAIM in the record, taken by write, pause, read back — a window goes on only if it then reads its own name; **(b)** nothing read before the claim is used after it; **(c)** each step that writes checks first that the claim is still its own; **(d)** the notification belongs to the window that reads its own name in the completed marker after a pause. The harm two windows could do to a later choice is stopped by the witnesses of D7, not by the claim. What rests on the lag is in § S2.7 |
| D5 | 3, N7 — the mirror | `after` (the mirror and the panel's redraw) is called after every clear, a pause and the read-back, and BEFORE the marker. It is called whenever this machine had something to clear (`held` is not empty): also in the run that resumes an interrupted one, and in a run that ends incomplete or refused, because overlays it cleared are already different. It is not awaited: `mirrorSettings` returns nothing (F8), and every activation mirrors again |
| D6 | 4, N8 — record before clear | What each location held is written into the record ONCE, before the first clear, and read back. A resumed run reuses it and never writes it again. A location is DUE only while no witness names it AND it still holds the recorded value (§ S2.2). A location that appears later is not in the record and is never touched |
| D7 | the gate's finding — a host that dies between a clear and its progress | Progress is never written after a clear. **Overlay:** the clear and its witness are ONE write of ONE record (the member `onExhaustedAutoMigration`), so there is no instant in between. **User layer:** the witness (`userBegun`) is written and read back BEFORE the clear is asked for. A resumed run that finds the witness never clears the user layer again, whatever it holds. So a `human` chosen after a clear that landed is kept, with or without a later record. The price is the opposite gap, and it is chosen: a host that dies after the witness and before the clear leaves the old value in place (§ S2.7, U2). When `config.update` REJECTS, the run knows the clear did not land, withdraws the witness, and the next activation tries again |
| D8 | 5, N9 — Settings Sync | **Not closed, and not closable inside the parent as written** (U1). Nothing is registered with `setKeysForSync`: by V4 it would help only when both machines sync *Extensions* and run one version, and it would add a silent failure on a machine that syncs extension state but not settings (its user value would never be cleared). As built, each machine's first run clears the user layer once, as the parent says |
| D9 | 9 — two writers of one overlay record | Each overlay clear is one turn of the catalog's queue (`inCatalogTurn`), so the catalog migration never holds a copy of the record across this write. Inside the turn the read and the `update` have no `await` between them |
| D10 | 6 — the marker | `completed` is a string: the version that completed the run. ANY string means done, older or newer: the migration is one-time, never per version. A bare string in the slot reads as completed. Anything else — a number, an array, `null`, a record without a string `completed` — is not a marker, and the run starts as a first run |
| D11 | 7 — the one notification | The parent's sentence, word for word, through `notify`: `as: 'information'`, `class: 'outcome'`, `source: 'onExhaustedMigrationHost'`, `code: 'on-exhausted-now-auto'`. The record gains `told: true` after it. A window that finds `completed` without `told` tells (a host can die in the pause after the marker). So: once, and twice only if a host dies between showing it and recording it. **A fresh install is told too**: nothing kept by this extension separates a fresh install from an upgrade from a version older than every record it keeps, a wrong guess would silence the one message N4 relies on, and the sentence is true for a new person. It does not name a workspace value: it is shown once per machine in whichever window completes the run, and a workspace value belongs to one window's folder. The gate section names it (S1) |
| D12 | 6, 11 — a failure | No marker; the next activation tries again. The person is told only when the SETTINGS FILE refused the user-layer clear — a state they can cure — and only once per machine while it lasts (`refusedTold` in the record). Everything else (a write that did not read back, a claim another window holds) is logged to the extension host's console and never a toast |
| D13 | the window must know `auto` | The run starts only when `inspect('onExhausted').defaultValue === 'auto'`. A window that updated in place still has the old registry (F15): there a cleared location would read the OLD default. Nothing is written in such a window, and its next activation runs the migration. This is the stories document's "a cleared location reads `auto`" made a condition instead of an assumption (U3 is the same window, seen from S1) |
| D14 | 8 — without the panel | The run is `migrateOnExhausted(ports)` in the pure module: a store, the user layer and six functions. No test builds a `PanelProvider`. The host starts it from `activate`. T30 holds both by reading the source |
| D15 | 10 — the workspace value | The user-layer port offers one read (`defaultValue`, `globalValue`) and one write (`undefined` to the Global target). It cannot see or write a workspace or folder value. **S2 needs no interface from S1**: the gate line is S1's `workspacePolicyOf(config.inspect('onExhausted'), 'onExhausted' in overlay)`, computed at every render from what is stored, and `after` redraws the panel. The witness member does not change `'onExhausted' in overlay` |
| D16 | 8 — `test:host` | One scenario is added (T31). It is the only test that runs the host module, and V1's JSON clone is exactly what a hand-made fake gets wrong. It needs a test-mode export (X4). The job cannot start on this machine and CI runs it with `continue-on-error`, so the coder reads the job's log and quotes the scenario's line in the PR; without that line the host wiring is reported as unverified |
| D17 | the constants | `SETTLE_MS = 2000`, `CLAIM_STALE_MS = 60000`, `CLAIM_TRIES = 3`. `SETTLE_MS` is a judgement, not a measurement (§ S2.7) |

Each of D1–D17 is a mechanism the parent does not name, chosen to meet something it does state. None changes the
parent's scope, goals, DoD, merge conditions, epics, stories, build order, contracts or approach:

| Decision | The parent's words it meets |
|---|---|
| D1 | N5: "how every overlay key is found — the side registry … or the `coai.settingsOverlay@` prefix" |
| D2 | § 4.6 "removes the saved `coai.onExhausted`"; O1 (everyone on `auto`). Leaving an `auto` is judged inside it and reported (U6) |
| D3, D5 | § 4.6 and N7: the mirror "once every clear succeeded and BEFORE it writes its marker" |
| D4 | N6: "the spec names the coordination … and the test"; done-when 3 "one result and one notification" |
| D6, D7 | N8: "clears a location only while it still holds that recorded value". A witness makes the condition stricter, never looser |
| D9 | § `## Epics` E2 scope: the catalog migration is "the precedent for queued runs" |
| D10 | § 4.6: the marker "= the version that ran it" |
| D11, D12 | § 4.6 "then one non-modal notification"; done-when 4; stories § 8 inputs 6, 7 and 11 hand the rest to this spec |
| D13 | requirement 7: "everyone is on `auto` after upgrading" — a clear that reads back as `human` is no migration |
| D14–D16 | done-when 3: "all of it is tested without the panel ever being opened"; Q1 |

## S2.2 — The record, the witnesses, and when a location is due

One `globalState` key, `coai.onExhaustedAutoMigration` (C5). Its value:

```ts
export interface MigrationRecord {
  /** The parent's marker: the extension version that completed the run on this machine. */
  readonly completed?: string;
  /** The window that wrote `completed` — only that window shows the notification. */
  readonly by?: string;
  /** The notification was shown. */
  readonly told?: true;
  /** The window running now, and when it said so (its own clock, `Date.now()`). */
  readonly claim?: { readonly by: string; readonly at: number };
  /** What each location held before the first clear (N8). Written once. Only locations that held a value to migrate. */
  readonly held?: Readonly<Record<string, string>>;
  /** The user layer's witness: the version that began clearing it. Written BEFORE that clear. */
  readonly userBegun?: string;
  /** The settings file refused the clear, and the person was told. */
  readonly refusedTold?: true;
}
```

- **A location id** is `'user'` or an overlay's whole `globalState` key.
- **`held[id]`** is the value as text: the string itself, else `JSON.stringify(value)`.
- **The witness of an overlay** is the member `onExhaustedAutoMigration` (the version) INSIDE that overlay's record.
  Every writer of an overlay keeps members it does not name (F2, F10), so the witness outlives a later choice.
  No reader asks for it (F4). A record that held only `onExhausted` keeps one member and so is still not seeded
  (F3): it was not seeded before either.
- **The witness of the user layer** is `userBegun` in the record.

A location is **due** when all three hold, read at that moment; otherwise it is **settled**:

| # | Condition | Why |
|---|---|---|
| 1 | no witness names it | a clear was made, or was begun (D7) |
| 2 | it holds a value to migrate (D2) | nothing to do otherwise |
| 3 | that value, as text, equals `held[id]` | else the person chose again after the record was made (N8) |

Every globalState read that decides anything is `recordOf(store.get(KEY))` made at that moment. The record is never
kept in a variable across an `await`.

## S2.3 — The pure module: `src/onExhaustedMigration.ts` (new, no `vscode`)

It imports `overlayKeyOf` from `./coaiInstall` (pure) and nothing that reaches `vscode`.

```ts
export const ON_EXHAUSTED_MIGRATION_KEY = 'coai.onExhaustedAutoMigration';
export const OVERLAY_WITNESS = 'onExhaustedAutoMigration';
export const SETTLE_MS = 2000;
export const CLAIM_STALE_MS = 60_000;
export const CLAIM_TRIES = 3;
/** The parent's § 4.6 sentence, word for word. Only the coordinator changes it. */
export const NOW_AUTO = 'When the rounds run out is now Auto — the gate raises no new hold and never waits for you; '
  + 'it logs and moves on. Your AI may still ask you a question its consultants could not settle. '
  + 'Change it in Reviews › The gate.';
export const NOT_RESET = 'When the rounds run out could not be reset to Auto in your settings: the settings file '
  + 'refused the change. Your earlier choice there is still in force. ConnectOtherAIs tries again at the next start.';

export type MigrationOutcome = 'migrated' | 'already' | 'not-ready' | 'busy' | 'incomplete' | 'refused';

/** The part of `vscode.Memento` the run uses. `update(key, undefined)` deletes. */
export interface MigrationStore {
  keys(): readonly string[];
  get(key: string): unknown;
  update(key: string, value: unknown): PromiseLike<void>;
}

/** The person's own layer of `coai.onExhausted`, and nothing of a workspace or a folder. */
export interface UserLayer {
  read(): { readonly defaultValue?: unknown; readonly globalValue?: unknown };
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
  /** The one notification. */
  readonly tell: () => Promise<void>;
  /** The settings file refused the clear: `why` is its own words. */
  readonly refused: (why: string) => Promise<void>;
  /** The extension host's console. Never a toast. */
  readonly log: (what: string, error?: unknown) => void;
}

/** What is stored, read as a record. A bare string is a completed marker; anything unreadable is an empty record. */
export function recordOf(stored: unknown): MigrationRecord;
/** The value as `held` keeps it, or `undefined` when there is nothing to migrate (absent, or `auto`). */
export function heldText(value: unknown): string | undefined;
/** What every location holds now: `'user'` and each overlay record found by its key. Witnessed overlays are left out. */
export function planOf(store: MigrationStore, userValue: unknown): Readonly<Record<string, string>>;
/** § S2.2's table. */
export function isDue(witnessed: boolean, current: unknown, held: string | undefined): boolean;
/** The user layer over a configuration that is asked for AGAIN at every call, writing the Global target only. */
export function userLayerOf(config: () => LayeredSetting, globalTarget: unknown): UserLayer;
/** The run. It never rejects. */
export function migrateOnExhausted(ports: MigrationPorts): Promise<MigrationOutcome>;
```

Rules the coder keeps:

- `userLayerOf` calls `config()` at every `read` and every `clear`. A `WorkspaceConfiguration` is a snapshot: one kept
  from the start of the run would never see a choice made during it.
- `recordOf` reads each member by its type and drops what does not fit. It never throws.
- `planOf` reads an overlay through the same test `readOverlay` uses (an object, not `null`, not an array). The
  prefix is `overlayKeyOf('')`, not a retyped string.
- `migrateOnExhausted` is a short list of steps, each a function of its own that answers an outcome to stop with or
  nothing to go on. That keeps every function at complexity ≤ 4 and under 50 lines (§ S2.9).
- An exception inside the run is caught once, at the top: `log`, then `'incomplete'`. The catch writes NOTHING. A
  claim left behind goes stale, exactly as a dead host's does.

## S2.4 — The run, step by step

`mine()` is one function: `const r = read(); return r.completed === undefined && r.claim?.by === ports.window`.
`read()` is `recordOf(ports.store.get(KEY))`.

| Step | What it does | Stops with |
|---|---|---|
| 0 ready | `ports.user.read().defaultValue !== 'auto'` → stop. Nothing is written, not even a claim (D13) | `not-ready` |
| 1 enter | Up to `CLAIM_TRIES` times: read. `completed` and `told` → stop. `completed` without `told` → step 7. A claim of ANOTHER window younger than `CLAIM_STALE_MS` → `pause(min(CLAIM_STALE_MS, at + CLAIM_STALE_MS − now))`, next try (the cap is for a clock ahead of this one: `at` is another host's clock). Else write `{ ...read(), claim: { by: window, at: now } }`, `pause(SETTLE_MS)`, read: `completed` → as above; `mine()` → step 2; else next try | `already`; `busy` when the tries are spent |
| 2 plan | `read().held` if it is there. Else `planOf(...)`, write `{ ...read(), held }`, and read it back: the same text, or stop (`log`). An empty `held` is written too | `incomplete` |
| 3 overlays | For each id of `held` but `'user'`, sorted: `await ports.turn(() => clearOverlay(id))`. Inside: `!mine()` → stop the run. Read the slot. Not a record, or not due → nothing. Due → ONE `update` of the record without `onExhausted` and with `[OVERLAY_WITNESS]: version`. No `await` between the read and the `update` | `busy` |
| 4 user layer | Only when `'user'` is in `held` and due. `!mine()` → stop. `await update(KEY, { ...read(), userBegun: version })`. Read: no `userBegun` → stop, nothing was cleared. THEN `await ports.user.clear()`. If it rejects: `await update(KEY, <read() without userBegun>)`, remember the reason, go on to step 5 | `busy`; `incomplete` |
| 5 settle | Only when `held` is not empty: `pause(SETTLE_MS)`; `left` = the ids of `held` that are due now; `ports.after()`. A remembered refusal → if `!read().refusedTold`: `await ports.refused(why)` and write `refusedTold: true`; release the claim. `left` not empty → `log` the ids, release the claim | `refused`; `incomplete` |
| 6 marker | `!mine()` → stop. Write `{ ...read() without claim and refusedTold, completed: version, by: window }` | `busy` |
| 7 tell | Entered from step 1 (`completed`, not `told`): first write `{ ...read(), by: window }`. Then `pause(SETTLE_MS)`, read. `completed`, `by === window`, not `told` → `await ports.tell()`, write `{ ...read(), told: true }`. `completed` otherwise → another window tells. No `completed` → the marker was lost; the next activation runs again | `migrated`; `already`; `incomplete` |

"Release the claim" is one write of the record without `claim`, made only when `mine()`.

The load-bearing orders, each pinned by a test:

1. `held` is written and read back before any clear (T11).
2. An overlay's clear and its witness are one `update` (T13).
3. `userBegun` is written and read back before `user.clear()` is called (T14, T15).
4. `after` comes after the clears and the pause, and before the marker (T10).
5. `tell` comes only after the marker was read back with this window's name (T23, T28).

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
| `tell` | `notify({ as: 'information', class: 'outcome', source: 'onExhaustedMigrationHost', code: 'on-exhausted-now-auto', title: NOW_AUTO })` |
| `refused` | `notify({ as: 'warning', class: 'failure', source: 'onExhaustedMigrationHost', code: 'on-exhausted-not-reset', title: NOT_RESET, detail: why })` |
| `log` | `console.error('ConnectOtherAIs: …', error)`, as activation logs a refused side stamp (`extension.ts:447-449`) |

The outcome is logged only when it is `incomplete` or `not-ready`.

`src/extension.ts`, three small hunks:

1. Directly after the first `mirrorSettings(settingsSync);` (`:438`) and before `onDataRootBack`:
   `void startOnExhaustedMigration(context, extensionVersion(context), () => { mirrorSettings(settingsSync); void panel.render(); });`
   with a comment that names this file. `activate` is already in `eslint-suppressions.json`; one statement adds no
   branch and its counts do not move.
2. `CoaiExports` (`:102`) gains `readonly onExhaustedMigration?: () => unknown;`, documented as present only in test
   mode, as `remoteSessions` is.
3. The returned object (`:917-920`) gains, inside the existing `ExtensionMode.Test` spread,
   `onExhaustedMigration: () => context.globalState.get(ON_EXHAUSTED_MIGRATION_KEY)`.

`.github/workflows/sonarcloud.yml`: `src_vs_code/src/onExhaustedMigrationHost.ts` joins `sonar.coverage.exclusions`
in its alphabetical place. The pure module must NOT be listed (F13).

## S2.6 — What each side's server runs afterwards (N5)

A cleared location reads `auto` in a window whose registry knows the new manifest. The settings file of a side
changes only when a window of that side mirrors.

| The side | When its file is rewritten | What its server then runs |
|---|---|---|
| this window's | now: the run calls `after` | S1's table: no key for a server that lists `onExhaustedAuto`; `good_enough` for an older one; nothing while its features are not settled |
| another side, a window open, the new extension | at its next mirror: a `coai` settings change (the user-layer clear is one, and it comes last, D3), a settled `--features` answer, or its next activation. Until then its file keeps the old value | then as the first row |
| another side, no window open | at its next activation | as the first row |
| another side, an OLDER extension (extensions install per remote) | at its next mirror, by the old writer, which writes the key only when the value differs from ITS default, `human`. A cleared overlay reads the old default, so it writes no key | a server 0.66.0 or newer: `auto`. An OLDER server: `human` — also where that side's overlay held `continue`, `good_enough` or `escalate` and ran it until now (§ S2.7, R5) |

## S2.7 — What S2 does not close

Each is also in *Reported to the coordinator* where it needs a word.

- **R1 — Settings Sync (U1).** The user layer is one file on every machine Settings Sync reaches; the marker is per
  machine. Machine A migrates and the person chooses Ask a human there. Machine B's first run finds no marker, finds
  `human`, and clears it; sync then removes it on A too. B's notification announces it on B. It happens at most once
  per further machine. Not closed inside the parent as written.
- **R2 — the opposite gap (U2).** A host that dies after `userBegun` was stored and before the settings write was
  asked for leaves the user value in place for good: the next run finds the witness and keeps the value (T15). The
  run completes and the notification says Auto, while the gate section shows the kept value. The window is the few
  instructions between two calls. The other order would reset a later `human`, which is the finding D7 answers.
- **R3 — what rests on the lag.** If a window's copy of `globalState` is older than `SETTLE_MS` when its pause ends,
  the claim does not exclude it. Then: a second run over the same locations (harmless where a witness stands); a
  second notification; a `held` made again from a stale copy, so a choice made in those seconds in a location
  that had not been cleared yet is recorded as the old value and cleared; and, by V3, a stale write can put a
  cleared overlay back with no witness — step 5 sees that only inside its own pause. None of this needs a crash, all
  of it needs a lag above two seconds. `SETTLE_MS` is not measured.
- **R4 — a window that updated in place.** D13 keeps the migration out of it. What that window's panel and mirror
  read for an unset `onExhausted` is S1's (U3).
- **R5 — an older extension on another side, with an older server.** The last row of § S2.6: a side that ran
  `continue`, `good_enough` or `escalate` from its overlay runs `human` on a server older than 0.66.0 until its own
  extension updates and writes `good_enough`. It follows from the reach the parent chose (N5).
- **R6 — an overlay seeded later by an older extension.** A window of an old version that turns the per-side switch
  on after the marker freezes the old default `human` into a new overlay. Nothing clears it: the run is once.
- **R7 — a kept overlay has no witness.** An overlay left alone because its value changed (condition 3) is not
  stamped. If the record is then lost (V3), a new plan records the later choice as the old value.
- **R8 — the notification, twice or never.** Twice when a host dies between showing it and writing `told`. Never
  only if `told` is written and the toast then fails to show; `notify` has already recorded it in the ledger (F12).
- **R9 — per profile.** The marker lives in a profile's `globalState`, so "once per machine" is once per profile of
  a machine. Each profile has its own user settings and overlays too.
- **R10 — the two-window fake is a reading of VS Code's source (V1–V3), not VS Code.** T29 pins the fake. No test
  runs two real windows. The owner's two-window hand check is the only observation there can be.

## S2.8 — Scenarios

**RED first** (stories document § 3): write each scenario before its code, see it fail on its assertion, make it
green, then break it — remove or change the production line named, see the stated failure, restore it. Report all
three. Every value the code computes is read back from the code: overlay keys from `overlayKey(side)` with a real
`Side`, the record's key and member names from the exported constants, a later choice written with the real
`writeOverlay` (`testing.md`, *A fixture the code REJECTS*). The clock is the `now` port; no test reads `Date.now()`.

### S2.8.0 — The fakes: `src/test/onExhaustedMigrationFakes.ts` (new; not a test file)

| Fake | What it models |
|---|---|
| `ClientState` | One client store and its windows, as V1–V3 read. `window()` answers a `MigrationStore` with its OWN copy. `update` changes that copy at once (JSON clone; `undefined` deletes), then REPLACES the client's whole value with the copy. `deliver()` replaces every window's copy with the client's value. Each window logs its writes (`key`, `value`). `dieAfter(n)` / `dieBefore(n)`: the n-th `update` lands (or does not) and its promise never resolves — the host died |
| `FakeUserSettings` | `inspect` over four layers (`defaultValue`, `globalValue`, `workspaceValue`, `workspaceFolderValue`) and `update(key, value, target)`, logged. `refuseWith(error)` makes `update` reject. `dieAfterClear()`: the removal lands and the promise never resolves |
| `Pauses` | `pause(ms)` logs `ms` and answers a promise the test resolves (`next()`), advancing the fake clock by `ms`. `auto()` resolves every pause at once, for one-window tests |
| `portsFor(window, …)` | `MigrationPorts` over the fakes, with one ordered event log (`write`, `clear user`, `pause`, `after`, `tell`, `refused`), a real `CatalogTurns` behind `turn`, and a hook the test can run before the n-th turn |

A run that "died" is never awaited. The test then advances the clock past `CLAIM_STALE_MS` and starts a new
activation: a new window name over the same client store.

### S2.8.1 — The decisions: `src/test/onExhaustedMigration.test.ts` (new, pure)

| # | Test | Scenario and assertion | Break-it: the line, and what goes red |
|---|---|---|---|
| T1 | `TheMarkerIsReadDefensively` | `recordOf`: `'0.88.0'`, `{ completed: '0.87.0' }` and `{ completed: '9.9.9' }` are completed; `42`, `null`, `[]`, `{}` and `{ completed: 5 }` are not; a `claim` with a non-number `at` is dropped; nothing throws | accept any truthy `completed` → `{ completed: 5 }` reads as done |
| T2 | `OverlaysAreFoundByTheirOwnKeys_AForgottenSideToo` | a store with a registry record for side 1 only, overlays for side 1 and side 2, a slot that holds a string, and the look-alike key `coai.settingsOverlayX`: `planOf` names both overlays and nothing else | find overlays through `sidesIn` → side 2 is missing |
| T3 | `OnlyAValueThatKeepsAPersonOffAutoIsHeld` | `heldText`: `human`, `continue`, `'humna'`, `null` and `5` answer a text; `undefined` and `auto` answer nothing. `planOf` leaves out an overlay that already carries the witness | drop the `auto` test → an `auto` location is planned |
| T4 | `ALocationIsDueOnlyWhileUnwitnessedAndUnchanged` | `isDue` over § S2.2's three conditions, one row each false, and one row all true | remove one condition → its row |
| T5 | `TheSentenceIsTheParentsAndNothingWider` | `NOW_AUTO` equals the parent's sentence, typed out in the test on purpose (this is the one literal that must be word for word); neither `NOW_AUTO` nor `NOT_RESET` contains "never asks you anything" | change a word of `NOW_AUTO` |
| T6 | `TheUserLayerReadsAndWritesThePersonsOwnLayerOnly` | `userLayerOf` over a fake with all four layers set: `read()` has exactly `defaultValue` and `globalValue`; `clear()` makes one `update('onExhausted', undefined, GLOBAL)`; the workspace and folder values are unchanged; a value set between two reads is seen by the second | keep the first configuration object → the second read is stale; pass another target → the target assertion |

### S2.8.2 — One window: `src/test/onExhaustedMigrationRun.test.ts` (new)

The usual fixture, **U**: the user layer `human`; overlay X (`human`, with `vendors` and `rounds` beside it);
overlay Y (`continue`), whose side has no registry record; overlay Z (`auto`); a slot that holds a string;
`workspaceValue: 'human'`, `workspaceFolderValue: 'continue'`; `defaultValue: 'auto'`.

| # | Test | Scenario and assertion | Break-it: the line, and what goes red |
|---|---|---|---|
| T7 | `EveryLocationIsClearedOnce_AndThePersonIsToldOnce` | U, one run → `migrated`. The user value is gone; X and Y have no `onExhausted`, carry the witness, and keep `vendors` and `rounds`; Z and the string slot are deep-equal to before; `completed` is the version and `told` is true; one `tell`, one `after`, one user-layer `update` | remove the `update` in `clearOverlay` → X still holds `human` |
| T8 | `ASecondActivationDoesNothing` | after T7's run, a new activation → `already`: no write to the store, no user-layer `update`, no pause, no `after`, no `tell`. (T7 is this file's positive: the same fixture, something happens) | remove step 1's `completed` and `told` stop → a claim is written |
| T9 | `AChoiceMadeAfterwardsIsKept` | after T7's run the person chooses: the user layer `human`, X `human` through `writeOverlay`. A new activation → both still `human`, X still carries the witness | as T8 |
| T10 | `TheMirrorComesAfterEveryClearAndBeforeTheMarker` | U: in the event log `after` follows the last clear and the pause, and at that moment the record has no `completed`. Second half: the first run dies in step 5's pause; the resumed run clears nothing and calls `after` once | move `ports.after()` below the marker write → first half; call `after` only when THIS run cleared something → second half |
| T11 | `WhatEachLocationHeldIsWrittenBeforeTheFirstClear_AndNeverAgain` | U: the first write that carries `held` precedes every overlay write and the user-layer `update`; `held` is `{ user: 'human', [X]: 'human', [Y]: 'continue' }`; after an interrupted run and changed values, the resumed run's record has the same `held` | plan again on resume (drop `read().held`) → `held` differs |
| T12 | `InterruptedAfterTheFirstClear_AChoiceMadeMeanwhileIsKept` (N8, the parent's test) | U: the host dies right after X's clear landed. The person sets X to `human` again (`writeOverlay`) and Y to `good_enough`. Resume → `migrated`: X is `human`, Y is `good_enough`, the user layer is cleared, one `tell` in all | ignore the overlay's witness → X is cleared; drop condition 3 → Y is cleared |
| T13 | `AnOverlayIsClearedAndWitnessedInOneWrite` | U: X's key is written exactly once, and no value ever written under X's key lacks both `onExhausted` and the witness | clear, then stamp in a second `update` → a written value with neither |
| T14 | `TheHostDiesRightAfterTheUserClear_AHumanChosenThenIsKept` (the gate's finding) | U, `dieAfterClear()`: the removal landed and nothing after it ran. (a) Resume with nothing chosen → `migrated`, the user layer still empty. (b) The person chooses `human` first; resume → `migrated`, the user layer is `human`, and no user-layer `update` was made by the resumed run | call `user.clear()` before writing `userBegun` → in (b) the resumed run clears `human` |
| T15 | `RetryingAnAttemptThatMayHaveLandedWouldResetALaterChoice_SoItIsNeverRetried` | U: the host dies after `userBegun` was stored and before `clear` was called. Resume → `migrated`; the user layer still holds `human`; the resumed run made no user-layer `update`. The test's comment says this is R2, chosen, and why | treat `userBegun` as "not yet cleared" (clear when the value still equals `held`) → the `update` count; and T14 (b) goes red with it |
| T16 | `ASettingsFileThatRefuses_WritesNoMarker_IsSaidOnce_AndTheNextStartFinishes` | U, `refuseWith(...)` → `refused`: no `completed`, no `userBegun`, no claim, `refusedTold`; `refused` called once with the error's text; X and Y cleared; `after` called. A second refusing activation → `refused`, and `refused` is NOT called again. The file is cured; the third activation → `migrated`, the user layer cleared, one `tell` | keep `userBegun` after a rejection → the third run leaves `human`; drop `refusedTold` → two `refused` calls |
| T17 | `AWriteThatDidNotLastIsSeenBeforeTheMarker` | U: inside step 5's pause the test puts X's old record back (a stale write, V3) → `incomplete`: no `completed`, no `tell`, `after` called, the claim released. The next activation clears X and completes | remove the `left` test → `completed` is written while X holds `human` |
| T18 | `AWindowThatDoesNotKnowAutoYetChangesNothing` | U with `defaultValue: 'human'` → `not-ready`: no write of any kind, no pause. With `'auto'` the same fixture migrates | remove step 0 → the store is written |
| T19 | `AFreshInstallIsToldOnce_AndOnlyTheRecordIsWritten` | nothing saved anywhere → `migrated`: every store write is under the record's key, no user-layer `update`, `held` is `{}`, `after` not called, one `tell`. With `version: ''` the marker reads `'unknown'` | call `after` unconditionally → the `after` count |
| T20 | `AWorkspaceValueIsNeverEdited` (Q1) | U → after the run `workspaceValue` is `human` and `workspaceFolderValue` is `continue`, and every user-layer `update` in the log has the Global target | as T6's second break-it |
| T21 | `TheCatalogMigrationAndThisRunTakeTurnsOnOneOverlay` | a catalog-like run is started through the same `CatalogTurns` (`migrate`): it reads X, waits on a gate the test holds, then writes X with a new `vendors`. This run reaches step 3 while the gate is held. The gate opens → `migrated`: X has the new `vendors`, no `onExhausted`, and the witness | call `clearOverlay` without `ports.turn` → the catalog-like write, made from the copy it read before the clear, puts `onExhausted` back: X's last assertion |
| T22 | `ACompletedRunThatNeverToldIsToldByTheNextActivation` | the record is `{ completed, by: 'gone', held: {} }` → `migrated`: one `tell`, `told` true, nothing cleared, no claim written. A further activation → `already`, no `tell` | stop on `completed` alone → no `tell` |

### S2.8.3 — Two windows: `src/test/onExhaustedMigrationTwoWindows.test.ts` (new)

Two windows, A and B, over one `ClientState` and one `FakeUserSettings`. The test resolves each pause itself and
calls `deliver()` before it does, unless the scenario says otherwise.

| # | Test | Scenario and assertion | Break-it: the line, and what goes red |
|---|---|---|---|
| T23 | `TwoWindowsStartingTogether_LeaveOneResultAndOneNotification` | U. A writes its claim; B, with nothing delivered, writes its own, which replaces A's at the client. Deliver; both pauses end. B → `migrated`; A waits out B's claim and → `already`. One `tell`; one user-layer `update`; `by` is B; **A's write log holds its claim and nothing else** | go on after the claim's pause without `mine()` → A writes `held` |
| T24 | `WindowBIsHeldAfterItsRead_AFinishes_ThePersonChooses_BChangesNothing` (N6's test) | The user layer and X hold `human`. B reads (nothing there), writes its claim and is HELD in its pause. A, which never saw B's claim, runs to `migrated`. The person chooses `human` in the user layer and in X. Deliver; B's pause ends → `already`. The user layer and X are `human`; one `tell`; `by` is A | none by one line: three hold it — the read after the pause (T23), `mine()` (T26) and the witnesses (T9, T14). Its teeth are T25 |
| T25 | `AMarkerAloneIsNoLock_AndThatIsThePoint` | T24's interleaving, driven through a NAIVE run written inside the test: read the marker, clear what it sees, write the marker, tell. It ends with two notifications and the person's `human` reset. The test asserts exactly that, and says in its name why the claim, the read-back and the witnesses exist | — (it runs no production line; it shows the fixture can see the defect) |
| T26 | `AWindowThatLostItsClaimWritesNothingMore` | U. A holds the claim and has cleared X. Before Y's turn the test writes another window's claim and delivers. A → `busy`: Y is untouched by A, A writes no marker and tells nothing | remove `mine()` from `clearOverlay` → Y is cleared by A |
| T27 | `AFreshClaimIsWaitedOut_AStaleOneIsTaken_AClockAheadIsCapped` | a claim of another window 10 s old → the first pause asked is `CLAIM_STALE_MS − 10000`, then the run completes. A claim `CLAIM_STALE_MS` old → the first pause is `SETTLE_MS`. A claim whose `at` is five minutes AHEAD → the pause is `CLAIM_STALE_MS`. A fresh foreign claim rewritten during each wait → `busy` after `CLAIM_TRIES` waits, and this window wrote nothing | remove the wait → the first pause is `SETTLE_MS`; remove the cap → a pause above `CLAIM_STALE_MS` |
| T28 | `OnlyTheWindowThatReadsItsOwnNameInTheMarkerTells` | U. In the pause after A's marker the test writes `by: 'other'` and delivers → A: `already`, no `tell` | tell without reading `by` back → one `tell` by A |
| T29 | `TheFakeBehavesAsTheEditorsMementoIsRead` | `ClientState` itself: a window sees its own `update` at once; the other sees it only after `deliver()`; a window's `update` replaces the client's WHOLE value, so a key the other window wrote and that was not delivered is gone; an object is stored as its JSON clone, an `undefined` member dropped. The comment cites V1–V3 and says what is NOT proved (R10) | — (it pins the double every test above rests on) |

### S2.8.4 — From activation, and in a real editor

| # | Test | Scenario and assertion | Break-it |
|---|---|---|---|
| T30 | `TheRunStartsFromActivation_NeverFromThePanel` in `onExhaustedMigration.test.ts` | reads the source: `extension.ts` calls `startOnExhaustedMigration(` exactly once; `panelProvider.ts` and `panelView.ts` never name it; neither new module imports `./panelProvider` or `./panelView`. Its companion asserts the same scan FINDS `./coaiInstall` in the pure module's imports, so the pattern is alive (`testing.md`, *A structural test that matches nothing*). A source scan is right here: there is no program a unit test can run (`.agents/PROJECT.md`) | move the call into `resolveWebviewView` |
| T31 | `theOnExhaustedMigrationCompletesInARealEditor`, `src/test/host/onExhaustedMigrationScenario.ts` (new), registered in `scenarios.ts` | the shipped extension, activated: `inspect('onExhausted').defaultValue === 'auto'` (D13's signal in a real registry); within 30 s (`until`) the exported record reads `completed` equal to the extension's version and `told` true; `globalValue` is `undefined`. It says what it does NOT drive: an update in place, two windows, a value saved before activation | — (CI only, D16) |

Two existing guards go red and green with this story, and are named in the summary: `sonarExclusions.test.ts`
(red while the host module is not listed) and `notificationSites.test.mjs` (red until `count-notifications.mjs
--write`).

**Count: 31 scenarios** — T1–T30 in the ordinary suite, T31 in a real editor.

## S2.9 — Files, commit order, commands, lint

**Files** (stories document § 6, with X4 and X5):

| File | What |
|---|---|
| `src_vs_code/src/onExhaustedMigration.ts` | new, pure: § S2.2–S2.4 |
| `src_vs_code/src/onExhaustedMigrationHost.ts` | new, host: § S2.5 |
| `src_vs_code/src/extension.ts` | the call, the `CoaiExports` member, the test-mode spread (§ S2.5). Nothing else, and nothing near `:434` (S1's) |
| `.github/workflows/sonarcloud.yml` | the host module in `sonar.coverage.exclusions` |
| `src_vs_code/notification-sites.json` | regenerated: two new sites |
| `src_vs_code/src/test/onExhaustedMigrationFakes.ts`, `onExhaustedMigration.test.ts`, `onExhaustedMigrationRun.test.ts`, `onExhaustedMigrationTwoWindows.test.ts` | new |
| `src_vs_code/src/test/host/onExhaustedMigrationScenario.ts`; one entry in `src/test/host/scenarios.ts` | new; one line |
| `research/module_tests.md` | one row: "A saved *When the rounds run out* is reset to Auto once — every side's overlay and the user layer, resumed, with two windows, and a later choice kept", naming the three test files, T31, and R10 as what is not covered |

Not touched: `package.json`, `src/settingsShape.ts`, `src/sideSettings.ts`, `src/catalogMigrationHost.ts`,
`src/panelProvider.ts`, `src/panelView.ts`, anything under `src_mcp`.

**Commit order.** S2 is coded after S1's commits are on the branch (the manifest default `auto` is D13's signal).

1. The fakes and T29. Then T1–T6 RED, the pure decisions GREEN.
2. T7–T22 RED, the run GREEN, each break-it.
3. T23–T28 RED and GREEN, each break-it; T25 is green from the start and says so.
4. The host module, the three `extension.ts` hunks, `sonarcloud.yml`, the census, T30, T31.
5. `research/module_tests.md`.

**Commands**, from `src_vs_code/` (stories document § 3):

```bash
npm ci                                                          # once
npm run compile                                                 # read the EXIT CODE: tsc emits even when red
node scripts/run-tests.mjs out/test/onExhaustedMigration.test.js
node scripts/run-tests.mjs out/test/onExhaustedMigrationRun.test.js
node scripts/run-tests.mjs out/test/onExhaustedMigrationTwoWindows.test.js
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
  decisions is two functions. Split, never suppress.
- The counts in `src_vs_code/eslint-suppressions.json` must not grow, and no entry is added for a new file.
- Both new modules stay far under the 800-line cap.
- The module that imports `vscode` is in `sonar.coverage.exclusions`; the pure one is not (F13).
- The two `notify` calls pass their `code` as a string literal at the call site; then
  `node scripts/count-notifications.mjs --write`, and `notification-sites.json` is committed.
- Tests import `../../scripts/inRunFolder.cjs` first. No test sleeps: every pause is the `pause` port.

## S2.10 — What the Opus reviewer checks

1. No `await` stands between the read of an overlay record and its `update`, and the witness is in that same
   `update` (T13).
2. `userBegun` is awaited AND read back before `user.clear()` is called; a rejection withdraws it; nothing else
   ever removes it (T14–T16).
3. No value of the record is carried across an `await`: every decision reads the store again.
4. `mine()` is asked before every write of steps 3, 4 and 6, and after the claim's pause.
5. The catch at the top of the run writes nothing.
6. `after` is called in step 5 whenever `held` is not empty, on every way out of that step.
7. The user-layer port is built on a function that asks for the configuration again, and only the Global target is
   ever passed (T6, T20).
8. The prefix comes from `overlayKeyOf('')`; the registry is not read.
9. Nothing calls `setKeysForSync`, and `package.json` is unchanged by this story.
10. `NOW_AUTO` is the parent's sentence; no new text says more than requirement 5.
11. Each break-it was run and its failure is quoted; T25 and T29 are reported as green from the start, with the
    reason; T31's line from the CI job is quoted, or the host wiring is reported as unverified.
12. `eslint-suppressions.json` shows no new entry and no higher count.

## Definition of Done — E2.S2

- [ ] T1–T30 green; each seen RED first on its assertion, and broken as its row says, with the three observations
      in the summary.
- [ ] T31 added; its result from CI quoted, or the host wiring reported as unverified.
- [ ] The user value and every overlay's value are cleared once; a location that holds `auto`, a slot that is no
      record, and a workspace or folder value are untouched (T7, T20).
- [ ] The marker is written only after every location reads as settled and after `after` (T10, T17).
- [ ] A resumed run keeps a choice made after the interruption, also when the host died right after a clear
      (T12, T14); an attempt that may have landed is never repeated (T15).
- [ ] Two windows leave one result and one notification (T23, T24, T26, T28); a second activation does nothing
      (T8); a later choice is kept (T9).
- [ ] The run starts from activation and no test opens the panel (T30).
- [ ] `npm run lint` and `npm test` green; suppressions not grown; `sonarExclusions.test.ts` and the notification
      census current; `plan-lifecycle.mjs` clean.
- [ ] § S2.7's residuals are listed in the PR summary, for S3's section in `research/module_extension.md`.
- [ ] N9 is NOT ticked here: it waits for the coordinator's word on U1.

## Reported to the coordinator

Each item says whether this spec's author judges it a change to the parent. Nothing in the parent was edited.

- **U1 — Settings Sync: "a choice made afterwards is kept" cannot hold across machines as the parent is written.
  Judged: fixing it IS a change.** *The conflict.* § 4.6 says "once per machine (a `globalState` marker …)", and the
  user layer is one synced file. Done-when 3 says "a choice made afterwards is kept". Machine B's first run clears a
  choice made on A after A's run (R1). *Why no mechanism inside the parent closes it.* The only thing that travels
  with the value is the settings file itself. `setKeysForSync` travels in another sync resource and only between
  equal extension versions (V4, read in VS Code's source): it would often arrive after B's first run, and on a
  machine that syncs extension state but not settings it would stop B's own old value from ever being cleared — a
  silent failure in place of an announced one. So it is not built. *What is built:* the parent as written.
  *The minimal fix.* A witness in the user layer itself: one manifest setting, `scope: application`, written by the
  run before it removes the value, in the style of `coai.catalogMigration` ("Written by ConnectOtherAIs, not by
  hand"). A run that finds it leaves the user value alone. D13 already guarantees the window knows the new key. In
  the code it replaces where `userBegun` is stored, and adds one scenario (machine B, the same settings, its own
  `globalState`: `human` kept). *Minimal wording:* § 4.6, after "= the version that ran it)": "; the user layer is
  one file on every machine Settings Sync reaches, so a witness is written into that layer before its value is
  removed (`coai.onExhaustedAutoMigration` in `settings.json`), and a machine that finds it leaves the user value
  alone". § 9: "one `globalState` key" → "one `globalState` key and one settings key". The E2 scope's `package.json`
  item gains the key. Until it is settled, S3 must not write "choose again afterwards and it is kept" without the
  Settings Sync exception (X6).
- **U2 — the gate's finding (a host that dies between a clear and its progress). Judged: settled inside the parent,
  NOT a change; one residual to record.** Overlays: closed, the clear and its witness are one write. User layer:
  closed in the direction the finding names (T14). The residual is the opposite gap (R2): a host that dies after the
  witness is stored and before the settings write is asked for leaves the old value in place for good, and the run
  still completes and says Auto. It cannot be closed: the settings file and `globalState` are two stores with no
  common write, so one of the two gaps must stay, and this one never resets a choice. With U1's fix both witnesses
  are in the settings file and the same gap remains. N8's "only while it still holds that recorded value" permits
  it. If the coordinator wants the residual in the parent, one sentence in N8 does it: "a location whose clear was
  begun is never cleared again, so a host that dies between the two leaves that one value in place".
- **U3 — a window that updated in place writes `human` for everyone, with S1 as briefed. Judged: NOT a plan change;
  an S1 defect to decide before S1 is final. Not verified by running.** F15 was observed on 2026-10-09 for NEW
  keys. The inference, not observed: in the same window the registry still holds the OLD default of
  `coai.onExhausted`, `human`, and S1 reads it through `config.get` (F7). `human` now differs from `DEFAULTS`, so the
  mirror writes `COAI_ON_EXHAUSTED=human` and the server holds on a person, for everyone who never chose anything,
  until that window reloads — and a hold raised then is this build's and is never released by `auto` (parent § 4.3).
  Before E2 such a window wrote nothing. *The fix inside C1 as written* ("a location with no value reads `auto`"):
  read `onExhausted` from the layers that HOLD a value (`inspect`: folder, workspace, user) and let
  `asOnExhausted(undefined)` answer `DEFAULTS`, never the registry's default. S2's part is D13.
- **U4 — `globalState` is one stored value per extension, not one per key (V1–V3). Judged: a finding beside this
  plan — a todo, not a build.** `sidesSeen.ts` and `cliPathKeep.ts` say one key per side means two windows never
  overwrite each other; by VS Code's source a write from a stale window replaces every key. S2 is built for it
  (read-backs, pauses, witnesses). `SETTLE_MS = 2000` is a judgement: the lag between two real windows was never
  measured (F16), and only the owner's machine can measure it. R3 lists what a longer lag costs.
- **U5 — "what grows" (§ 9). Judged: wording, NOT a change.** § 9 says "one `globalState` key … nothing with an
  in-flight state". It is still one key. While a run is in flight the record holds a claim and what each location
  held, which N6 and N8 themselves require, and each cleared overlay record gains one member. Minimal wording:
  "one `globalState` key — a record that also carries the claim and what each location held while a run is in
  flight — and one member in each overlay record it cleared".
- **U6 — a location that holds `auto` is left, not removed (D2). Judged: NOT a change.** The parent says "removes
  the saved `coai.onExhausted`". An `auto` can only have been saved by this version. Removing it from an overlay
  could hand that side to a workspace value, against O1.
- **U7 — words a person reads that the parent does not hold. Judged: NOT a change; the coordinator owns the
  texts.** (a) `NOT_RESET`, the warning for a settings file that refuses the clear, at most once per machine (D12).
  (b) A fresh install is told too (D11); the coordinator may flip it, but no reliable signal tells the two apart.
  (c) The notification does not name a workspace value in force (D11).
- **U8 — an older extension with an older server on another side (R5). Judged: a residual of the reach N5 chose,
  NOT a change.** Such a side that ran `continue`, `good_enough` or `escalate` from its overlay runs `human` until
  its own extension updates. S3's CHANGELOG draft should say that every side's extension is updated together.
- **U9 — edits this spec needs in files its author was told not to touch.** X1–X5 in § 4 and § 6 of the stories
  document, X6 in its S3 brief, and X7: `plan-lifecycle.mjs` wants a row in `todo/README.md`. Proposed row:
  `| [PLAN_auto_never_asks_a_person_E2_S2_spec.md](PLAN_auto_never_asks_a_person_E2_S2_spec.md) | The E2.S2 cornerstone spec — **spec only, written 2026-11-17 by Fable before its code; nothing coded.** The one-time migration: every overlay found by its key and the user layer, a claim taken by write, pause and read-back, a witness written with (overlay) or before (user layer) each clear, the mirror before the marker, one notification; 31 scenarios. Settings Sync (N9) is not closable inside the parent and is reported (U1). |`
