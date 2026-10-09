import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CatalogLayer, LayerWrite } from '../catalogMigration';
import { migrateOne, unknownKeysOf, unregisteredKeyIn } from '../catalogMigrationRun';

/**
 * The move into the catalog WAITS while this window does not know a key it is about to write
 * (research/PLAN_catalog_migration_waits_for_its_settings.md).
 *
 * <p>Extension 0.65.0, 2026-10-09, on the operator's Windows machine: VS Code updated the extension in place before it
 * had activated, the move ran, and its first write was refused — `Unable to write to User Settings because
 * coai.migratedFrom is not a registered configuration` — although the manifest declares the key. The person was shown
 * an ERROR toast on the one start every upgrade goes through. A key the window does not know yet is not a failure of
 * anyone's settings: the run writes nothing and says it is waiting.</p>
 */

/** VS Code's own words when a write names a key its registry does not hold. */
const REFUSAL = 'Unable to write to User Settings because coai.migratedFrom is not a registered configuration.';

/** A layer with one consultant that carries its own runtime: the move has something to write, the backup first. */
const LAYER: CatalogLayer = {
  vendors: [{ id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true }],
  consultants: { claude: { vendor: 'codex', runtime: 'codex', model: 'gpt-y', baseUrl: '', executablePath: '' } },
};

interface Heard {
  readonly written: LayerWrite['key'][];
  readonly stopped: unknown[];
  readonly waiting: (readonly string[])[];
}

/** The run's reports as the host would pass them, recording what each was told. */
function reportsInto(heard: Heard) {
  return {
    stopped: async (_layer: unknown, error: unknown) => {
      heard.stopped.push(error);
    },
    left: async () => undefined,
    waiting: (_layer: unknown, keys: readonly string[]) => {
      heard.waiting.push(keys);
    },
  };
}

function heard(): Heard {
  return { written: [], stopped: [], waiting: [] };
}

test('a write VS Code refuses because it does not know the key yet is a wait, never a stop', async () => {
  const told = heard();
  const layer = {
    name: 'your settings',
    read: () => LAYER,
    write: async (write: LayerWrite) => {
      throw new Error(write.key === 'migratedFrom' ? REFUSAL : `unexpected write of ${write.key}`);
    },
  };

  await migrateOne(layer, {}, reportsInto(told));

  assert.deepEqual(told.stopped, [], 'the person was shown an error for a key the window had not registered yet');
  assert.deepEqual(told.waiting, [['migratedFrom']], 'the run did not say it is waiting for the key it could not write');
});

test('a key the window does not know yet is found BEFORE any write — nothing is written', async () => {
  const told = heard();
  const layer = {
    name: 'your settings',
    read: () => LAYER,
    write: async (write: LayerWrite) => {
      told.written.push(write.key);
    },
    unknownKeys: (keys: readonly string[]) => keys.filter((key) => key === 'migratedFrom'),
  };

  const wrote = await migrateOne(layer, {}, reportsInto(told));

  assert.deepEqual(told.written, [], 'the run wrote while the window did not know a key it was about to write');
  assert.equal(wrote, false);
  assert.deepEqual(told.waiting, [['migratedFrom']]);
  assert.deepEqual(told.stopped, []);
});

test('a first write refused as unknown reports that NOTHING was written — no mirror, no redraw follows', async () => {
  const told = heard();
  const layer = {
    name: 'your settings',
    read: () => LAYER,
    write: async () => {
      throw new Error(REFUSAL);
    },
  };

  assert.equal(await migrateOne(layer, {}, reportsInto(told)), false,
    'the run said it wrote, so the host mirrored and redrew an unmigrated layer');
});

test('a refusal in another language is still a wait when the registry, asked again, does not know the key', async () => {
  const told = heard();
  let asked = 0;
  const layer = {
    name: 'your settings',
    read: () => LAYER,
    write: async () => {
      throw new Error('In die Benutzereinstellungen kann nicht geschrieben werden, weil coai.migratedFrom keine registrierte Konfiguration ist.');
    },
    // Known when the run checks before writing; unknown when asked again after the refusal.
    unknownKeys: (keys: readonly string[]) => (asked++ === 0 ? [] : [...keys]),
  };

  await migrateOne(layer, {}, reportsInto(told));

  assert.deepEqual(told.stopped, [], "a German editor's refusal of an unregistered key was shown as an error");
  assert.deepEqual(told.waiting, [['migratedFrom']]);
});

test('a refusal that is NOT about an unknown key still stops, and says so', async () => {
  const told = heard();
  const layer = {
    name: 'your settings',
    read: () => LAYER,
    write: async () => {
      throw new Error('EACCES: permission denied, open settings.json');
    },
  };

  await migrateOne(layer, {}, reportsInto(told));

  assert.equal(told.stopped.length, 1);
  assert.deepEqual(told.waiting, []);
});

test('a key with any default is known — an empty object or an empty string too — and one with none is not', () => {
  const defaults: Record<string, unknown> = { migratedFrom: {}, catalogMigration: '', vendors: [], chatModel: null };

  assert.deepEqual(unknownKeysOf(['migratedFrom', 'catalogMigration', 'vendors', 'chatModel', 'notDeclared'], (key) => defaults[key]),
    ['notDeclared']);
});

test('VS Code\'s refusal is read for the key it names, and nothing else is taken for it', () => {
  assert.equal(unregisteredKeyIn(new Error(REFUSAL)), 'migratedFrom');
  assert.equal(unregisteredKeyIn('Unable to write to User Settings because coai.vendors is not a registered configuration.'), 'vendors');
  assert.equal(unregisteredKeyIn(new Error('EACCES: permission denied')), '');
  assert.equal(unregisteredKeyIn(new Error('Unable to write because editor.fontSize is not a registered configuration.')), '',
    'another extension\'s key is not this migration\'s wait');
});

test('a layer that knows every key is written as before, in the order the plan gives', async () => {
  const told = heard();
  const layer = {
    name: 'your settings',
    read: () => LAYER,
    write: async (write: LayerWrite) => {
      told.written.push(write.key);
    },
    unknownKeys: () => [],
  };

  assert.equal(await migrateOne(layer, {}, reportsInto(told)), true);
  assert.equal(told.written[0], 'migratedFrom', 'the backup is no longer written first');
  assert.deepEqual(told.waiting, []);
});
