import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { test } from 'node:test';
import { ownedTemp } from './ownedTemp';
import { extensionContext, INSTALL_LOG, installEditor, LOADED, recordingEditor, writeShell } from './shellHarness';

/**
 * A restored chat tab starts the install work (v2 E4, accepted plan-gate finding).
 *
 * <p>The install work (binary check, `--setup-clients`, the update offer) starts after the sidebar's `menuReady` (or its fallback) or after the first command. A window
 * whose ONLY activation is a restored `coaiChat` tab (`onWebviewPanel:coaiChat`) — the person never opens the sidebar and never runs a command — would never run it, and an
 * interrupted client update would stay unfinished. So the chat module asks for the install start itself, once per host, on a timer AFTER the restore has painted and
 * finished. The real shell bundles run here over the recording editor, as `theChatRunsFromItsOwnModule.test.ts` runs them; the install module is the recording stub except
 * in the case that counts real `--setup-clients` runs.</p>
 */

type Door = { readonly install: () => Promise<{ readonly install?: { readonly finished: Promise<void> } } | undefined> };
type Serializer = { readonly deserializeWebviewPanel: (panel: unknown, state: unknown) => Thenable<void> | void };

interface World {
  readonly dir: string;
  readonly editor: ReturnType<typeof recordingEditor>;
  readonly entry: { readonly activate: (context: unknown) => Door; readonly deactivate: () => Thenable<void> | undefined };
  readonly doors: Door;
  /** What happened, in order: `paint`, `disposed` (the restore's own steps) and `start` (the stub install module was asked). */
  readonly order: string[];
  readonly installLog: () => readonly string[];
  readonly restore: () => void;
}

/** The shell in a scratch folder, activated against the recording editor; the install module is the recording stub unless `install` is `real`. */
async function shell(what: string, install: 'stub' | 'real' = 'stub'): Promise<World> {
  const dir = ownedTemp(what);
  await writeShell(dir, { install });
  const data = join(dir, 'data');
  mkdirSync(data, { recursive: true });
  process.env['COAI_DATA_DIR'] = data;
  const holder = globalThis as unknown as Record<symbol, unknown>;
  const order: string[] = [];
  // The stub pushes `start` into this list: the same push lands in `order`, so a start BEFORE the restore's paint shows.
  const log: string[] = [];
  const push = log.push.bind(log);
  log.push = (...items: string[]): number => { if (items.includes('start')) { order.push('start'); } return push(...items); };
  holder[LOADED] = [];
  holder[INSTALL_LOG] = log;
  holder[Symbol.for('coai.shell')] = undefined;
  const editor = recordingEditor();
  const restoreEditor = installEditor(editor);
  const entry = createRequire(__filename)(join(dir, 'extension.js')) as World['entry'];
  const doors = entry.activate(extensionContext(dir, 1));

  return { dir, editor, entry, doors, order, installLog: () => [...log], restore: () => { restoreEditor(); delete process.env['COAI_DATA_DIR']; } };
}

/** A `coaiChat` tab as the editor hands it over after a reload; it records its paint and its disposal into `order`. */
function restoredPanel(order: string[]): unknown {
  let html = '';

  return {
    webview: {
      options: undefined as unknown,
      set html(value: string) { html = value; order.push('paint'); },
      get html() { return html; },
      onDidReceiveMessage: () => ({ dispose: () => undefined }),
      postMessage: async () => true,
      cspSource: 'test',
      asWebviewUri: (uri: unknown) => uri,
    },
    iconPath: undefined as unknown,
    viewType: 'coaiChat',
    title: '',
    onDidDispose: () => ({ dispose: () => undefined }),
    onDidChangeViewState: () => ({ dispose: () => undefined }),
    dispose: () => { order.push('disposed'); },
    reveal: () => undefined,
  };
}

/** A conversation that is nowhere runs the whole restore road (drawn, migration waited for, store read, tab disposed) and needs no stored data. */
async function restoreOneTab(world: World): Promise<void> {
  const serializer = world.editor.serializers.get('coaiChat') as Serializer | undefined;
  assert.ok(serializer !== undefined, 'the shell registers no coaiChat serializer');
  await serializer.deserializeWebviewPanel(restoredPanel(world.order), { id: 'nowhere-to-be-found' });
}

/** Lets timers that were set for "soon" run, without sleeping a fixed time: polls until `done` or a ceiling. */
async function until(done: () => boolean, ceilingMs = 3000): Promise<void> {
  const end = Date.now() + ceilingMs;
  while (!done() && Date.now() < end) {
    await new Promise<void>((resolve) => { setTimeout(resolve, 5); });
  }
}

const quiet = (): Promise<void> => new Promise((resolve) => { setTimeout(resolve, 150); });

test('a restored chat tab with no sidebar and no command starts the install work once, after the restore has painted and finished', async () => {
  const world = await shell('install-chat-restore');
  try {
    assert.deepEqual(world.installLog(), [], 'activate alone asked for the install work');

    await restoreOneTab(world);
    assert.ok(world.order.includes('paint') && world.order.includes('disposed'), 'the restore did not run its whole road');
    assert.ok(!world.order.includes('start'), 'the install work was asked for before the restore had finished: it must wait for a timer after it');

    await until(() => world.installLog().length > 0);
    await quiet();

    assert.deepEqual(world.installLog(), ['start'], 'a chat-restore activation did not start the install work exactly once');
    assert.ok(world.order.indexOf('start') > world.order.lastIndexOf('paint'), 'the install work started before the chat panel painted');
    assert.ok(world.order.indexOf('start') > world.order.indexOf('disposed'), 'the install work started before the restore finished');
    assert.deepEqual([...((globalThis as unknown as Record<symbol, string[]>)[LOADED] ?? [])], [], 'the restore loaded the legacy module or the sidebar (their tripwires)');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

test('two restored chat tabs ask for the install work once', async () => {
  const world = await shell('install-chat-restore-two');
  try {
    await restoreOneTab(world);
    await restoreOneTab(world);
    await until(() => world.installLog().length > 0);
    await quiet();

    assert.deepEqual(world.installLog(), ['start'], 'every restored tab asked again: the chat module asks once per host');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

/* ---------------------------------------------------------------------------------------------- *
 * The REAL install module over a fake coai-mcp: how many `--setup-clients` runs the host makes.
 * ---------------------------------------------------------------------------------------------- */

const posix = process.platform === 'win32' ? { skip: 'a fake binary is a shell script' } : {};

/** A fake `coai-mcp` at the path the extension installs to; answers its argument log. It answers exit 64, so nothing is said to the person. */
function putFakeServer(world: World): string {
  const storage = join(world.dir, 'global-storage');
  mkdirSync(storage, { recursive: true });
  const path = join(storage, 'coai-mcp');
  const log = join(world.dir, 'args.log');
  writeFileSync(path, `#!/bin/sh\nprintf '%s\\n' "$*" >> '${log}'\nprintf '%s' 'unknown argument --setup-clients'\nexit 64\n`);
  chmodSync(path, 0o755);

  return log;
}

/** The editor's file system answers `stat` truthfully here, and no network is reached. */
function truthfulStat(world: World): () => void {
  const real = world.editor.vscode['workspace'] as Record<string, unknown>;
  const fs = { readFile: async () => new Uint8Array(), writeFile: async () => undefined, stat: async (uri: { fsPath: string }) => statSync(uri.fsPath) };
  world.editor.vscode['workspace'] = new Proxy(real, { get: (target, key) => (key === 'fs' ? fs : target[key as string]) });
  const fetched = globalThis.fetch;
  globalThis.fetch = (() => Promise.reject(new Error('offline: no test reaches the network'))) as typeof fetch;

  return () => { globalThis.fetch = fetched; world.editor.vscode['workspace'] = real; };
}

const setupRuns = (log: string): string[] => {
  try {
    return readFileSync(log, 'utf8').split('\n').filter((line) => line.startsWith('--setup-clients'));
  } catch {
    return [];
  }
};

test('the real install module: a chat restore alone runs --setup-clients once', posix, async () => {
  const world = await shell('install-chat-restore-real', 'real');
  const undo = truthfulStat(world);
  try {
    const log = putFakeServer(world);

    await restoreOneTab(world);
    await until(() => setupRuns(log).length > 0);
    await quiet();

    assert.equal(setupRuns(log).length, 1, 'a window that only restored a chat tab never finished its client update');
  } finally {
    undo();
    await world.entry.deactivate();
    world.restore();
  }
});

test('the real install module: the sidebar\'s ask (the shell\'s install door) and a chat restore together run --setup-clients exactly once', posix, async () => {
  const world = await shell('install-chat-restore-both', 'real');
  const undo = truthfulStat(world);
  try {
    const log = putFakeServer(world);

    // The sidebar asks through the entry's own `startInstall`, which is also the `install` door of `activate`.
    await (await world.doors.install())?.install?.finished;
    await restoreOneTab(world);
    await until(() => false, 400);
    await quiet();

    assert.equal(setupRuns(log).length, 1, 'the sidebar and the chat restore both asked, and the install work ran more than once');
  } finally {
    undo();
    await world.entry.deactivate();
    world.restore();
  }
});
