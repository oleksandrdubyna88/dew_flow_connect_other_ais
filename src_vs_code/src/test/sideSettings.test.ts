import assert from 'node:assert/strict';
import { test } from 'node:test';
import { KeyValueStore, readOverlay, seedIfEmpty, writeOverlay } from '../sideSettings';
import { overlaidReader } from '../settingsShape';
import { Side } from '../coaiInstall';

/**
 * Settings a side keeps to itself.
 *
 * <p>One machine, three working environments: a Windows window on one company's subscription and two
 * WSL distros on two others, each needing its own proxy, its own CLI paths and its own login. VS
 * Code hands ONE `settings.json` to every extension host, so the separation has to happen here.</p>
 *
 * <p>The store is a fake `Memento` — the suite cannot import `vscode`, which is the reason this
 * module exists as a module rather than as lines inside the panel provider.</p>
 */

function store(initial: Record<string, unknown> = {}): KeyValueStore & { seen: Record<string, unknown> } {
  const seen = { ...initial };

  return {
    seen,
    get: <T,>(key: string) => seen[key] as T | undefined,
    update: (key: string, value: unknown) => {
      seen[key] = value;

      return Promise.resolve();
    },
  };
}

const WINDOWS: Side = {
  remoteName: undefined,
  hostname: 'DESKTOP',
  storagePath: 'C:/Users/x/AppData/Roaming/Code/User/globalStorage/coai',
};

const WSL_A: Side = {
  remoteName: 'wsl',
  distro: 'Ubuntu-24.04',
  storagePath: '/home/x/.vscode-server/data/User/globalStorage/coai',
};

/** The same storage path as WSL_A — every distro mounts it at the same place. */
const WSL_B: Side = { ...WSL_A, distro: 'Ubuntu-Work' };

test('a side with no overlay reads as having none, not as broken', async () => {
  assert.deepEqual(readOverlay(store(), WINDOWS), {});
});

test('two WSL distros on one machine keep different values for the same setting', async () => {
  // The case that asked for this feature: one distro on one company's proxy, another on another's.
  const kept = store();

  await writeOverlay(kept, WSL_A, 'credsKey', 'company-a');
  await writeOverlay(kept, WSL_B, 'credsKey', 'company-b');

  assert.equal(readOverlay(kept, WSL_A)['credsKey'], 'company-a');
  assert.equal(readOverlay(kept, WSL_B)['credsKey'], 'company-b');
});

test('a write to one side leaves the other sides exactly as they were', async () => {
  const kept = store();
  await writeOverlay(kept, WINDOWS, 'maxConcurrency', 2);
  await writeOverlay(kept, WSL_A, 'maxConcurrency', 8);
  await writeOverlay(kept, WSL_A, 'credsKey', 'k');

  assert.deepEqual(readOverlay(kept, WINDOWS), { maxConcurrency: 2 });
  assert.deepEqual(readOverlay(kept, WSL_A), { maxConcurrency: 8, credsKey: 'k' });
});

test('turning the switch on seeds this side with what it reads today', async () => {
  const shared = (section: string) =>
    ({ maxConcurrency: 7, credsKey: 'shared', vendors: [{ id: 'codex' }] } as Record<string, unknown>)[section];
  const kept = store();

  const seeded = await seedIfEmpty(kept, WSL_A, shared);

  assert.equal(seeded['maxConcurrency'], 7);
  assert.equal(overlaidReader(shared, readOverlay(kept, WSL_A))('credsKey'), 'shared',
    'nothing changes until something is edited');
});

test('turning it off and on again does not discard what this side had configured', async () => {
  const kept = store();
  await writeOverlay(kept, WSL_A, 'credsKey', 'company-a');

  await seedIfEmpty(kept, WSL_A, () => 'shared');

  assert.equal(readOverlay(kept, WSL_A)['credsKey'], 'company-a', 'the seed overwrote a real setting');
});

test('the install record and the overlay live in the same store and never collide', async () => {
  // globalState is one database. A remembered version replacing a company's settings would be the
  // same class of defect as the two-sides install record, one layer up.
  const kept = store({ 'coai.installedVersion@local|C%3A%2Fx': '0.18.2' });

  await writeOverlay(kept, WINDOWS, 'credsKey', 'k');

  assert.equal(kept.seen['coai.installedVersion@local|C%3A%2Fx'], '0.18.2');
  assert.equal(Object.keys(kept.seen).length, 2);
});

test('rubbish in the overlay slot is no overlay, not a crash', async () => {
  // An older build, a hand-edited state file, a null: the page must render the shared settings
  // rather than throw on first paint.
  for (const rubbish of ['a string', 42, null, [1, 2, 3]]) {
    const kept = store({ [`coai.settingsOverlay@wsl|Ubuntu-24.04|${WSL_A.storagePath}`]: rubbish });

    assert.deepEqual(readOverlay(kept, WSL_A), {}, `${JSON.stringify(rubbish)} was taken for an overlay`);
  }
});
