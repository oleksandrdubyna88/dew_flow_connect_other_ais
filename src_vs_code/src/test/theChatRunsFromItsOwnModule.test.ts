import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { test } from 'node:test';
import { ids } from './idsOfTheShell';
import { ownedTemp } from './ownedTemp';
import { extensionContext, installEditor, LOADED, recordingEditor, ROOT, SPY, type Spy, writeShell } from './shellHarness';

/**
 * Chat runs WITHOUT the legacy module (v2 E4, story s4-chat-carried, tasks t2 and t3).
 *
 * <p><b>RUN, not read.</b> The shell is built the way `npm run bundle` builds it and written to a scratch folder: the real entry, the real `runtime`
 * and `chat` modules, and a TRIPWIRE in place of the legacy and the sidebar modules (a file that records its id and throws when it is required). The
 * entry's `activate` registers into a recording editor; the thunks it registered are then called as VS Code calls them. What each test reads is what
 * happened: which modules the loader required, what the chat module registered and started, what reached the disk.</p>
 *
 * <p>The host half of the story — the same commands in a real editor, with the legacy module loaded beside — is `src/test/host/scenarios.ts` (CI job
 * "extension · a real editor"); this box cannot run it.</p>
 */

type Door = { readonly chat: () => Promise<{ readonly chat?: { readonly adoptedCommands: () => readonly string[]; readonly adoptedSerializer: () => boolean } }> };

interface World {
  readonly dir: string;
  readonly editor: ReturnType<typeof recordingEditor>;
  readonly context: Record<string, unknown>;
  /** What `activate` returned: the doors a test host starts the modules through. */
  readonly doors: Door;
  readonly entry: { readonly activate: (context: unknown) => Door; readonly deactivate: () => Thenable<void> | undefined };
  readonly spy: Spy;
  readonly loaded: () => readonly string[];
  readonly restore: () => void;
}

/** The shell in a scratch folder, activated against the recording editor, with a data directory of its own that exists (a named root that is missing is refused). */
async function shell(what: string, mode = 1): Promise<World> {
  const dir = ownedTemp(what);
  await writeShell(dir, { spies: true });
  const data = join(dir, 'data');
  mkdirSync(data, { recursive: true });
  process.env['COAI_DATA_DIR'] = data;
  const spy: Spy = { drained: 0, started: 0, disposed: 0 };
  const holder = globalThis as unknown as Record<symbol, unknown>;
  holder[SPY] = spy;
  holder[LOADED] = [];
  // Nothing from an earlier world: the loader publishes its handle once per `globalThis`.
  holder[Symbol.for('coai.shell')] = undefined;
  const editor = recordingEditor();
  const restoreEditor = installEditor(editor);
  const context = extensionContext(dir, mode);
  const entry = createRequire(__filename)(join(dir, 'extension.js')) as World['entry'];
  const doors = entry.activate(context);

  return {
    dir, editor, context, doors, entry, spy,
    loaded: () => [...((holder[LOADED] as string[] | undefined) ?? [])],
    restore: () => { restoreEditor(); delete process.env['COAI_DATA_DIR']; },
  };
}

const manifest = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8')) as { readonly contributes: { readonly commands: readonly { readonly command: string }[] } };

test('a cold chat command initialises chat and never loads the legacy module or the sidebar', async () => {
  const world = await shell('chat-cold');
  try {
    // The thunk the shell registered for a chat command. `forgetPickedConversation` touches nothing but the chat's own picker, so it is safe to run bare.
    const handler = world.editor.commands.get('coai.forgetPickedConversation');
    assert.ok(handler !== undefined, 'the shell registers no coai.forgetPickedConversation');
    assert.equal(world.spy.started, 0, 'chat started before any chat command ran: activate must only register');
    assert.equal(existsSync(join(world.dir, 'global-storage')), false, 'activate touched the extension storage');

    await handler();

    assert.deepEqual(world.loaded(), [], `a chat command loaded ${world.loaded().join(', ')}: the old activation must stay out of the chat path`);
    assert.equal(world.spy.started, 1, 'the chat command did not start the chat store and its housekeeping');
    // What chat needs of the old activation is initialised: the data root is the one the environment names, and the heartbeat has announced this window.
    const stored = join(world.dir, 'data');
    const twice = world.editor.registrations.filter((one, at, all) => all.indexOf(one) !== at);
    assert.deepEqual(twice, [], 'the editor was asked for the same registration twice: VS Code refuses a second one, and the old activation would have thrown');
    assert.equal(world.editor.serializers.size, 1, 'one coaiChat serializer, the shell\'s');
    assert.ok(existsSync(stored), 'the data directory vanished');
    // A second command is the same instance: the store, the migration and the housekeeping are not started again.
    await handler();
    assert.equal(world.spy.started, 1, 'a second chat command started chat again');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

test('every chat command and the coaiChat serializer are the shell\'s registrations, and each one reaches a handler in the chat module', async () => {
  const world = await shell('chat-handlers', 3);
  try {
    const registered = [...world.editor.commands.keys()];
    const declared = manifest.contributes.commands.map((one) => one.command);
    assert.deepEqual([...registered].sort(), [...declared].sort(), 'the shell registers other commands than the manifest declares');
    assert.ok(world.editor.serializers.has('coaiChat'), 'the shell registers no coaiChat serializer');

    const chat = (await world.doors.chat()).chat;
    assert.ok(chat !== undefined, 'the chat module offers no test door in test mode');
    const { CHAT_COMMANDS } = ids();
    assert.deepEqual([...chat.adoptedCommands()].sort(), [...CHAT_COMMANDS].sort(), 'the chat module did not hold exactly the commands the shell routes to it');
    assert.equal(chat.adoptedSerializer(), true, 'the chat module holds no coaiChat serializer');
    assert.deepEqual(world.loaded(), [], 'starting chat loaded the legacy module');
    assert.equal(world.spy.started, 1);
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

test('a restored coaiChat tab restores without the sidebar and without the legacy module', async () => {
  const world = await shell('chat-restore');
  try {
    const serializer = world.editor.serializers.get('coaiChat');
    assert.ok(serializer !== undefined, 'the shell registers no coaiChat serializer');
    const written: { html: string; disposed: boolean } = { html: '', disposed: false };
    const panel = {
      webview: {
        options: undefined as unknown,
        set html(value: string) { written.html = value; },
        get html() { return written.html; },
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
      dispose: () => { written.disposed = true; },
      reveal: () => undefined,
    };

    // A conversation that is nowhere (no record in the store, none in the memento) is the one answer that needs no stored data and still runs the WHOLE
    // restore road: the tab is drawn "Restoring…", the migration is waited for, the store is read, and the tab is disposed.
    await serializer.deserializeWebviewPanel(panel, { id: 'nowhere-to-be-found' });

    assert.deepEqual(world.loaded(), [], `a restored tab loaded ${world.loaded().join(', ')}`);
    assert.equal(world.spy.started, 1, 'restoring a tab did not start the chat store the restore reads');
    assert.equal(written.disposed, true, 'a conversation that is nowhere was not disposed');
    assert.match(written.html, /Restoring this conversation/u, 'the tab was not drawn before the store was read');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});

test('deactivate forwards to the chat module when it was loaded: the usage queue is drained and the heartbeat stopped, once', async () => {
  const world = await shell('chat-deactivate');
  try {
    await world.editor.commands.get('coai.forgetPickedConversation')?.();
    assert.equal(world.spy.started, 1);
    assert.equal(world.spy.drained, 0);

    await world.entry.deactivate();

    assert.equal(world.spy.drained, 1, 'deactivate did not drain the queued usage records');
    assert.equal(world.spy.disposed, 1, 'deactivate did not stop the heartbeat');
    // The host then disposes the context's subscriptions, which holds the same stop: it must not stop it a second time.
    for (const one of (world.context['subscriptions'] as { dispose?: () => void }[])) {
      one.dispose?.();
    }
    assert.equal(world.spy.disposed, 1, 'the heartbeat was stopped twice');
  } finally {
    world.restore();
  }
});

test('deactivate of a window that never used chat loads nothing and drains nothing', async () => {
  const world = await shell('chat-never');
  try {
    const answer = world.entry.deactivate();

    assert.equal(answer, undefined, 'deactivate answered for modules that were never loaded');
    assert.deepEqual(world.loaded(), []);
    assert.equal(world.spy.drained, 0);
    assert.equal(world.spy.started, 0);
  } finally {
    world.restore();
  }
});

test('the old activation registers no chat command and no coaiChat serializer, and the legacy module adopts only what is not chat\'s', () => {
  const { CHAT_COMMANDS } = ids();
  const old = readFileSync(join(ROOT, 'src', 'extension.ts'), 'utf8');
  const legacy = readFileSync(join(ROOT, 'shell', 'modules', 'legacy.ts'), 'utf8');

  assert.ok(CHAT_COMMANDS.length >= 14, 'the chat command list is nearly empty, so the scan below proves nothing');
  for (const command of CHAT_COMMANDS) {
    assert.ok(!old.includes(`registerCommand('${command}'`), `the old activation still registers ${command}, which would be registered twice in a window that loads both modules`);
  }
  assert.ok(!old.includes('registerWebviewPanelSerializer'), 'the old activation still registers a chat serializer');
  assert.match(legacy, /COMMANDS\.filter\(\(id\) => moduleOf\(id\) === MODULE_LEGACY\)/u, 'the legacy module still adopts every command, the chat ones included');
  assert.match(legacy, /serializer: \(\) => false/u, 'the legacy module still adopts a serializer');
});

test('the legacy sidebar\'s bug chat reaches the ONE chat instance through the shell: starting it a second way starts nothing again', async () => {
  const world = await shell('chat-bug');
  try {
    await world.editor.commands.get('coai.forgetPickedConversation')?.();
    assert.equal(world.spy.started, 1);
    const handle = (globalThis as unknown as Record<symbol, { readonly module: <M>(id: string) => Promise<M> }>)[Symbol.for('coai.shell')];
    assert.ok(handle !== undefined, 'the loader published no handle');
    const chat = await handle.module<{ readonly chooseFromBug: (context: unknown, bug: unknown) => Promise<void> }>('chat');
    // What the review page hands over is a bug the chat cannot use here; the call must still go through the one registry and refuse on its own terms.
    await chat.chooseFromBug(world.context, {}).catch(() => undefined);

    assert.equal(world.spy.started, 1, 'the bug chat started chat a second time');
    assert.deepEqual(world.loaded(), [], 'the bug chat loaded the legacy module');
  } finally {
    await world.entry.deactivate();
    world.restore();
  }
});
