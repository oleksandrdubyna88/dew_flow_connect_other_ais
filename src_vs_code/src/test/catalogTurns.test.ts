import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CatalogTurns } from '../catalogTurns';

/**
 * Who may touch the catalog's three keys, and when (PLAN_one_model_catalog.md E1.3/E1.4; PR #681's code round).
 *
 * <p>The migration reads a layer, then writes it. The old page's save reads it, then writes `vendors` and then the
 * entry. Interleaved, the migration read the save's new rows beside its OLD definition — `vendors` is written first
 * and its change event starts a run at once — moved that definition into a fresh row, and the save's reference then
 * left that row orphaned. Each turn is now whole: a save holds the catalog until both of its writes have landed.</p>
 *
 * <p>And a save is two triggers (`vendors`, then the entry), each asking for a run. A run that has not STARTED yet
 * reads the layer when it does, so a second request before then is the same run; a request during a run is a new one,
 * because that run has already read.</p>
 */

/** A promise and its resolver, so a test decides when a turn finishes. */
function gate(): { readonly open: () => void; readonly opened: Promise<void> } {
  let open = (): void => undefined;
  const opened = new Promise<void>((resolve) => {
    open = resolve;
  });

  return { open, opened };
}

test('two requests for a migration before it starts are one run', async () => {
  const turns = new CatalogTurns();
  const hold = gate();
  let runs = 0;
  const editing = turns.edit(() => hold.opened);

  const first = turns.migrate(async () => {
    runs += 1;
  });
  const second = turns.migrate(async () => {
    runs += 1;
  });
  hold.open();
  await Promise.all([editing, first, second]);

  assert.equal(runs, 1, 'the second request came before the run read anything, so it is the same run');
});

test('a request DURING a run is a new run, because that run has already read', async () => {
  const turns = new CatalogTurns();
  const reading = gate();
  let runs = 0;

  const first = turns.migrate(async () => {
    runs += 1;
    await reading.opened;
  });
  await Promise.resolve();
  await Promise.resolve();
  const second = turns.migrate(async () => {
    runs += 1;
  });
  reading.open();
  await Promise.all([first, second]);

  assert.equal(runs, 2);
});

test('a migration asked for in the middle of a save reads only after both of its writes', async () => {
  const turns = new CatalogTurns();
  const written: string[] = [];
  const between = gate();
  let migrationSaw: readonly string[] = [];

  const saving = turns.edit(async () => {
    written.push('vendors');
    // What the `vendors` write's change event does: ask for a run, without waiting for it.
    void turns.migrate(async () => {
      migrationSaw = [...written];
    });
    await between.opened;
    written.push('consultants');
  });
  between.open();
  await saving;
  await turns.settled();

  assert.deepEqual(migrationSaw, ['vendors', 'consultants'], 'the run read the save whole, never half of it');
});

test('an edit gives back what its work returned, and a failed one does not stop the next turn', async () => {
  const turns = new CatalogTurns();

  assert.equal(await turns.edit(async () => 42), 42);
  await assert.rejects(turns.edit(async () => {
    throw new Error('refused');
  }), /refused/u);
  assert.equal(await turns.edit(async () => 'after'), 'after');
});
