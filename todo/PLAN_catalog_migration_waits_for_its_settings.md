# PLAN — the catalog migration waits until this window knows the settings it writes

> Status: **plan only, nothing implemented yet (2026-10-09).** Scope: `src_vs_code/src/catalogMigrationHost.ts`, a new
> vscode-free `catalogMigrationRun.ts`, tests. A patch release of the extension (0.65.1).
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_one_model_catalog.md](PLAN_one_model_catalog.md) (E1.3, the host half of the move).

## Symptom

Reported by the operator on 2026-10-09, the day extension 0.65.0 shipped, on Windows. The first start after the update
showed an ERROR toast:

> Moving the models in your settings into the catalog stopped part way; nothing that was written is lost, and the next
> start finishes it.

`notifications.jsonl` holds its detail: `Unable to write to User Settings because coai.migratedFrom is not a registered
configuration.` The 0.65.0 manifest DOES declare `coai.migratedFrom` (`package.json`), so the key was not yet in the
window's settings registry. VS Code's own log says why: the window started at 17:34:42, auto-updated the extension at
17:34:48–56 (0.64.0 → 0.65.0, no reload — the extension had not activated yet, activation is `onStartupFinished`),
activated 0.65.0 at 17:35:01, and the migration's first write (`migratedFrom`, the backup, written first) was refused at
17:35:16. Every catalog key is new in 0.65.0 (0.64.0's manifest has none of them), so an update in place is exactly the
upgrade that meets it. The next start (17:39) migrated without a word. Nothing was written: the refused write was the
first.

So the toast is wrong twice: it is an ERROR for something that is not a failure of the person's settings, and it is
shown on the one start every upgrading person goes through when VS Code updates in place.

## Goal

A migration never writes while the window's registry does not know a key it is about to write to `settings.json`; it
waits, without a toast, and runs again when the registry has caught up (a configuration change for `coai`) or after a
short backstop. Only if the window still does not know the keys after the backstop does the person see one WARNING —
not an error — naming the cure: reload the window. The side overlay (`globalState`) needs no registry and is unchanged.

## Design

1. **`catalogMigrationRun.ts`, vscode-free** — `migrateOne`, `readAndPlan` and `applyWrites` move out of the host as they
   are (a behaviour-preserving step, its own commit), taking a `RunLayer` (`name`, `read`, `write`, optional
   `unknownKeys(keys)`) and the reports (`stopped`, `left`, `waiting`) as arguments. This is what makes the host's run
   testable at all: today `catalogMigrationHost.ts` imports `vscode` and has no test.
2. **The wait.** Before any write, a `migrate` outcome asks the layer which of its writes' keys the registry does not
   know. Any → no write, `waiting(layer, keys)`, return `false`. The user layer answers from
   `config.inspect(key)?.defaultValue === undefined` (a registered key always has a default — VS Code fills one from the
   type when the manifest gives none; an unknown key has none). The side layer has no `unknownKeys`.
3. **The belt.** A write that fails anyway with VS Code's own refusal (`/is not a registered configuration/`) is treated
   the same way — the check and the write are two calls, and the registry can be between them.
4. **The host's `waiting`** arms ONE retry: the next `onDidChangeConfiguration` affecting `coai`, or a 30-second timer,
   whichever is first, then `scheduleCatalogMigration`. Never more than one armed. After three waits in one window it
   shows a warning — "This window has not loaded the settings of ConnectOtherAIs <version> yet, so your models are moved
   into the catalog after a reload" — with a *Reload Window* button, and stops retrying.

Rejected: retrying only on the next start — that is today's behaviour minus the toast, and leaves the move undone for a
whole session in which the new page is showing.

## Build order

1. Extract `catalogMigrationRun.ts` (no behaviour change); full `npm test` green.
2. RED: `catalogMigrationRun.test.ts` — a user-like layer whose first write throws VS Code's refusal text: today
   `stopped` is called; the test asserts `waiting` with `['migratedFrom']`, no `stopped`, nothing written. And a layer
   whose `unknownKeys` names `migratedFrom`: no write at all.
3. The wait and the belt; GREEN; break-it (drop the check → the second test red; drop the belt → the first red).
4. The host's retry and the warning; `extension.ts` unchanged except as the retry needs.
5. Docs: `research/module_extension.md` (the migration host), CHANGELOG via release-please's fix commit, POST_DEPLOY
   only if an item names the migration's first start.

## Test plan

- The two RED tests through the extracted run, with fake layers and fake reports.
- A test of the pure "which keys are unknown" decision over a fake `inspect` (registered key with a default, one
  without a declared default, an unknown key).
- `npm test`, eslint, the family checks, `test:host` (activation runs the migration).

## Definition of Done

- [ ] An update in place no longer shows an error toast; the move completes in the same window once the registry has the
      keys (RED first, teeth shown).
- [ ] A window that never learns the keys says so once, as a warning with Reload Window.
- [ ] Docs updated; released as 0.65.1; this plan promoted.
