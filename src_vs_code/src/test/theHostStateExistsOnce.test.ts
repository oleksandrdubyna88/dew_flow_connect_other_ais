import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { test } from 'node:test';
import { SHELL_KEY } from '../hostRuntime';
import type { HostRuntime } from '../runtimeState';
import { ids } from './idsOfTheShell';
import { ownedTemp } from './ownedTemp';
import { installEditor, recordingEditor, SPY, writeProbe, writeShell, extensionContext } from './shellHarness';

/**
 * State that must exist ONCE per host is held once (v2 E4, story s4-chat-carried, task t1; extension.md § 10 row 7).
 *
 * <p>Two bundles are built the way the shell builds modules — each with its own copy of `dataDir.ts`, `findingsBoard.ts` and `notify.ts`, which is what
 * made the state module-local twice — and loaded through the real loader (`shell/lazy.ts` inside the real entry). What the test reads is what one bundle
 * sets and the other sees: the data root, a published finding, the notify gap, and the identity of the runtime object itself.</p>
 */

const PROBE = `
import { coaiDataDir, useStorageSettings } from '../src/dataDir';
import { currentFindings, publishFindings } from '../src/findingsBoard';
import { hostRuntime } from '../src/hostRuntime';
import { forgetTheGap, theGap } from '../src/notify';
export const probe = { coaiDataDir, useStorageSettings, currentFindings, publishFindings, hostRuntime, forgetTheGap, theGap };
`;

interface Probe {
  readonly probe: {
    readonly coaiDataDir: () => string;
    readonly useStorageSettings: (thisSide: unknown, shared: unknown) => void;
    readonly currentFindings: () => readonly unknown[];
    readonly publishFindings: (findings: readonly unknown[]) => void;
    readonly hostRuntime: () => HostRuntime;
    readonly theGap: () => string;
  };
}

interface Handle {
  readonly runtime: () => HostRuntime;
  readonly module: <M>(id: string) => Promise<M>;
}

async function twoBundles(what: string): Promise<{ readonly a: Probe; readonly b: Probe; readonly handle: Handle; readonly inputs: readonly (readonly string[])[]; readonly restore: () => void }> {
  const dir = ownedTemp(what);
  await writeShell(dir);
  const inputs = [await writeProbe(dir, 'probeA', PROBE), await writeProbe(dir, 'probeB', PROBE)];
  const holder = globalThis as unknown as Record<symbol, unknown>;
  holder[SPY] = { drained: 0, started: 0, disposed: 0 };
  holder[SHELL_KEY] = undefined;
  delete process.env['COAI_DATA_DIR'];
  const editor = recordingEditor();
  const restoreEditor = installEditor(editor);
  const entry = createRequire(__filename)(join(dir, 'extension.js')) as { readonly activate: (context: unknown) => unknown };
  entry.activate(extensionContext(dir, 1));
  // The loader publishes its handle the first time it loads ANY module. The sidebar's tripwire is the cheapest load that starts nothing: it throws, the entry
  // falls back to the legacy tripwire, which throws too, and the rejection is the only trace — chat is never started, so nothing here writes a data directory.
  const provider = editor.viewProviders.get('coai.panel') as { readonly resolveWebviewView: (view: unknown, context: unknown, token: unknown) => Thenable<void> | void };
  await Promise.resolve(provider.resolveWebviewView({}, {}, {})).catch(() => undefined);
  const handle = holder[SHELL_KEY] as Handle | undefined;
  assert.ok(handle !== undefined, 'the loader published no handle after loading a module');

  return { a: await handle.module<Probe>('probeA'), b: await handle.module<Probe>('probeB'), handle, inputs, restore: () => { restoreEditor(); } };
}

test('the key the loader publishes under is the key the bundles read, and the chat module id is the same word on both sides', () => {
  assert.equal(SHELL_KEY, Symbol.for(ids().SHELL_KEY_NAME), 'src/hostRuntime.ts and shell/ids.ts name different keys, so a bundle would never find the shell');
  assert.equal(ids().MODULE_CHAT, 'chat', 'the id the legacy module loads the chat under (src/hostRuntime.ts MODULE_CHAT) is not the shell\'s');
});

test('two bundles see the same runtime: the data root, a published finding and the notify gap are set in one and seen in the other', async () => {
  const world = await twoBundles('state-once');
  try {
    // They ARE two bundles: each carries its own copies of the modules whose state used to be module-local.
    assert.notEqual(world.a.probe.coaiDataDir, world.b.probe.coaiDataDir, 'the two probes share one copy of dataDir.ts, so this proves nothing about two bundles');
    assert.notEqual(world.a.probe.publishFindings, world.b.probe.publishFindings, 'the two probes share one copy of findingsBoard.ts');

    // One object, by reference, from the loader's memoised runtime module.
    const runtime = world.handle.runtime();
    assert.equal(world.a.probe.hostRuntime(), runtime, 'bundle A holds a runtime other than the shell\'s');
    assert.equal(world.b.probe.hostRuntime(), runtime, 'bundle B holds a runtime other than the shell\'s');
    assert.equal(world.handle.runtime(), runtime, 'the shell answers a different runtime the second time');

    // The data root: set by A, read by B.
    world.a.probe.useStorageSettings({ directory: join(ownedTemp('state-root'), 'named'), side: '' }, { directory: '', side: '' });
    assert.equal(world.b.probe.coaiDataDir().endsWith('named'), true, 'the data root bundle A installed is not the one bundle B resolves');

    // The findings board: published by A, read by B.
    const finding = { key: 'coai.dataDirectory', state: 'missing', value: '/gone' };
    world.a.probe.publishFindings([finding]);
    assert.deepEqual(world.b.probe.currentFindings(), [finding], 'a finding published by bundle A is unheard by bundle B\'s writers');

    // The notify gap: B's `theGap()` reads what A's side of the runtime holds.
    assert.equal(world.b.probe.theGap(), '', 'the gap sentence is not empty before there is a gap');
    world.a.probe.hostRuntime().notify.gap = { lost: 3, since: '2026-11-22T10:00:00.000Z', until: '2026-11-22T10:00:05.000Z' };
    assert.notEqual(world.b.probe.theGap(), '', 'a gap recorded through bundle A is not seen by bundle B\'s notify');
    assert.equal(world.a.probe.hostRuntime().notify.run, world.b.probe.hostRuntime().notify.run, 'two run ids in one host');

    // The suppressor is the runtime's own: one budget, however many bundles notify.
    assert.equal(world.a.probe.hostRuntime().notify.bounds, world.b.probe.hostRuntime().notify.bounds, 'two suppressors in one host');
  } finally {
    world.restore();
  }
});

test('no bundle carries the state: the runtime sources are inputs of the runtime module only', async () => {
  const world = await twoBundles('state-inputs');
  try {
    for (const inputs of world.inputs) {
      assert.deepEqual(inputs.filter((one) => one === 'src/runtimeState.ts' || one === 'shell/modules/runtime.ts'), [], 'a module bundle carries the runtime state, so it holds a second copy of it');
    }
  } finally {
    world.restore();
  }
});

test('a bundle that carried the state would be a second host state: the check can tell (control)', async () => {
  const world = await twoBundles('state-control');
  try {
    const control = `import { createRuntime } from '../src/runtimeState'; export const probe = { made: createRuntime() };`;
    const dir = ownedTemp('state-control-bundles');
    const inputsA = await writeProbe(dir, 'controlA', control);
    await writeProbe(dir, 'controlB', control);
    const require = createRequire(__filename);
    const a = require(join(dir, 'modules', 'controlA.js')) as { readonly probe: { readonly made: HostRuntime } };
    const b = require(join(dir, 'modules', 'controlB.js')) as { readonly probe: { readonly made: HostRuntime } };

    assert.ok(inputsA.includes('src/runtimeState.ts'), 'the control does not carry the runtime state, so it controls nothing');
    assert.notEqual(a.probe.made, b.probe.made, 'two bundles that each carry the state share one object');
    assert.notEqual(a.probe.made.notify.run, b.probe.made.notify.run, 'two bundles that each carry the state mint the same run id');
  } finally {
    world.restore();
  }
});
