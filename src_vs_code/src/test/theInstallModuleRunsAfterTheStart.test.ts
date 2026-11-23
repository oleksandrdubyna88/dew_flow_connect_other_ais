import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { chmodSync, existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { test } from 'node:test';
import { ids } from './idsOfTheShell';
import { ownedTemp } from './ownedTemp';
import { extensionContext, INSTALL_LOG, installEditor, LOADED, recordingEditor, writeShell } from './shellHarness';

/**
 * The install module through the shell (v2 E4, story s5-install-module): `coai.installServer` is its command, nothing of it runs while `activate` registers, and the first
 * command starts it only after that command has answered. The last two cases run the REAL module, bundled the way `npm run bundle` builds it, over a fake
 * `coai-mcp` file in the extension's storage: one that answers exit 64 (a binary from before `--setup-clients`) and one that refuses a client.
 *
 * <p>Where the install work starts AFTER the sidebar's `menuReady` is `theSidebarDrawsItsRealMenu.test.ts`; the order of its own steps and its notices are
 * `installWork.test.ts` and `setupClients.test.ts`.</p>
 */

type Door = { readonly install: () => Promise<unknown> };

interface World {
  readonly dir: string;
  readonly editor: ReturnType<typeof recordingEditor>;
  readonly context: Record<string, unknown>;
  readonly entry: { readonly activate: (context: unknown) => Door; readonly deactivate: () => Thenable<void> | undefined };
  readonly doors: Door;
  readonly installLog: () => readonly string[];
  readonly restore: () => void;
}

/** The shell in a scratch folder, activated against the recording editor; the install module is the stub unless `install` is `real`. */
async function shell(what: string, install: 'stub' | 'real' = 'stub'): Promise<World> {
  const dir = ownedTemp(what);
  await writeShell(dir, { install });
  const data = join(dir, 'data');
  mkdirSync(data, { recursive: true });
  process.env['COAI_DATA_DIR'] = data;
  const holder = globalThis as unknown as Record<symbol, unknown>;
  holder[LOADED] = [];
  holder[INSTALL_LOG] = [];
  holder[Symbol.for('coai.shell')] = undefined;
  const editor = recordingEditor();
  const restoreEditor = installEditor(editor);
  const context = extensionContext(dir, 3);
  const entry = createRequire(__filename)(join(dir, 'extension.js')) as World['entry'];
  const doors = entry.activate(context);

  return {
    dir, editor, context, entry, doors,
    installLog: () => [...((holder[INSTALL_LOG] as string[] | undefined) ?? [])],
    restore: () => { restoreEditor(); delete process.env['COAI_DATA_DIR']; },
  };
}

const settle = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });

test('activate only registers: the install module is not loaded and not started', async () => {
  const world = await shell('install-activate');
  try {
    await settle();

    assert.deepEqual(world.installLog(), [], 'activate loaded or started the install module: it must wait for the first menuReady or the first command');
    assert.ok(world.editor.commands.has('coai.installServer'), 'the shell registers coai.installServer');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

test('coai.installServer is the install module\'s command: it reaches the module and never the legacy one', async () => {
  const world = await shell('install-command');
  try {
    await world.editor.commands.get('coai.installServer')?.();
    await settle();

    assert.ok(world.installLog().includes('command:coai.installServer'), 'the install module did not receive the command');
    assert.deepEqual([...((globalThis as unknown as Record<symbol, string[]>)[LOADED] ?? [])], [], 'the command loaded the legacy module or the sidebar (their tripwires)');
    const { INSTALL_COMMANDS } = ids();
    assert.deepEqual([...INSTALL_COMMANDS], ['coai.installServer']);
    assert.equal(ids().MODULE_INSTALL, 'install');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

test('the FIRST command starts the install work, after that command has answered, and only once however many commands follow', async () => {
  const world = await shell('install-first-command');
  try {
    const first = world.editor.commands.get('coai.forgetPickedConversation');
    assert.ok(first !== undefined);
    assert.deepEqual(world.installLog(), []);

    await first();
    await settle();
    assert.deepEqual(world.installLog(), ['start'], 'the first command did not start the install work, or started it more than once');

    await first();
    await world.editor.commands.get('coai.chatChoose')?.();
    await settle();
    assert.deepEqual(world.installLog(), ['start'], 'a later command asked for the install work again: the module decides once-ness, but the shell asks once');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

test('the install door of activate starts the work too (a test host reaches it this way)', async () => {
  const world = await shell('install-door');
  try {
    await world.doors.install();

    assert.deepEqual(world.installLog(), ['start']);
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

/* ---------------------------------------------------------------------------------------------- *
 * The REAL module over a fake coai-mcp in the extension's storage (POSIX: the fake is a script).
 * ---------------------------------------------------------------------------------------------- */

const posix = process.platform === 'win32' ? { skip: 'a fake binary is a shell script' } : {};

/** Puts a fake `coai-mcp` at the path the extension installs to, and answers where its argument log is. */
function putFakeServer(world: World, code: number, output: string): string {
  const storage = join(world.dir, 'global-storage');
  mkdirSync(storage, { recursive: true });
  assert.ok(process.platform === 'linux' || process.platform === 'darwin', 'this case runs where a script is a binary');
  const path = join(storage, 'coai-mcp');
  const log = join(world.dir, 'args.log');
  const quoted = (text: string): string => `'${text.replaceAll("'", "'\\''")}'`;
  writeFileSync(path, `#!/bin/sh\nprintf '%s\\n' "$*" >> ${quoted(log)}\nprintf '%s' ${quoted(output)}\nexit ${code}\n`);
  chmodSync(path, 0o755);

  return log;
}

/** The editor's file system answers `stat` truthfully for this test (the recording one says everything exists), and no network is reached. */
function truthfulStat(world: World): () => void {
  const real = world.editor.vscode['workspace'] as Record<string, unknown>;
  const fs = { readFile: async () => new Uint8Array(), writeFile: async () => undefined, stat: async (uri: { fsPath: string }) => statSync(uri.fsPath) };
  world.editor.vscode['workspace'] = new Proxy(real, { get: (target, key) => (key === 'fs' ? fs : target[key as string]) });
  const fetched = globalThis.fetch;
  globalThis.fetch = (() => Promise.reject(new Error('offline: no test reaches the network'))) as typeof fetch;

  return () => { globalThis.fetch = fetched; world.editor.vscode['workspace'] = real; };
}

const warnings = (world: World): number => world.editor.calls.filter((one) => one === 'window.showWarningMessage').length;

test('the real install module: a binary that answers exit 64 (before --setup-clients) is run once with --binary and nobody is told', posix, async () => {
  const world = await shell('install-real-64', 'real');
  const undo = truthfulStat(world);
  try {
    const log = putFakeServer(world, 64, 'unknown argument --setup-clients');

    await world.doors.install();
    await world.doors.install();

    const path = join(world.dir, 'global-storage', 'coai-mcp');
    // The update check's own `--version` probe asks the same binary; only the mode's runs are counted here.
    const runs = readFileSync(log, 'utf8').split('\n').filter((line) => line.startsWith('--setup-clients'));
    assert.deepEqual(runs, [`--setup-clients --binary ${path}`], 'one run per activation, with the extension\'s own binary named');
    assert.equal(warnings(world), 0, 'exit 64 must give no notification');
  } finally {
    undo();
    await world.entry.deactivate();
    world.restore();
  }
});

test('the real install module: a refused client is ONE warning, and it is not a question', posix, async () => {
  const world = await shell('install-real-refused', 'real');
  const undo = truthfulStat(world);
  try {
    putFakeServer(world, 78, JSON.stringify({ mode: 'setup-clients', clients: [{ cli: 'claude', result: 'changed' }, { cli: 'codex', result: 'refused', reason: 'foreign entry' }, { cli: 'agy', result: 'failed', reason: 'timed out' }] }));

    await world.doors.install();
    await settle();

    assert.equal(warnings(world), 1, 'two clients that failed are ONE notification');
    assert.equal(world.editor.calls.filter((one) => one.startsWith('window.showQuickPick') || one === 'window.showInputBox').length, 0, 'never a question to the person');
  } finally {
    undo();
    await world.entry.deactivate();
    world.restore();
  }
});

test('the real install module: with no binary installed nothing is spawned and nobody is told', posix, async () => {
  const world = await shell('install-real-none', 'real');
  const undo = truthfulStat(world);
  try {
    await world.doors.install();

    assert.equal(warnings(world), 0);
    assert.equal(existsSync(join(world.dir, 'global-storage', 'coai-mcp')), false);
  } finally {
    undo();
    await world.entry.deactivate();
    world.restore();
  }
});
