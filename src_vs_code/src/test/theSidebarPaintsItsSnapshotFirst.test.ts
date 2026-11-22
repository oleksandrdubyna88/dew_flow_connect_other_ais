import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import * as nodeCrypto from 'node:crypto';
import * as nodeFs from 'node:fs';
import { mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import * as nodePath from 'node:path';
import { join } from 'node:path';
import { test } from 'node:test';
import { build } from 'esbuild';
import { panelHtml } from '../panelView';
import {
  extensionVersionOf, NONCE_PLACEHOLDER, readSnapshot, SNAPSHOT_FILE, SNAPSHOT_MAX_BYTES, snapshotText,
} from '../sidebarSnapshot';
import { SidebarSnapshotWriter } from '../sidebarSnapshotStore';
import { ownedTemp } from './ownedTemp';
import { panelState, runPanel } from './panelPageHarness';

/**
 * The sidebar's first paint does not wait for the old code (v2 E4, story s3-sidebar).
 *
 * <p><b>RUN, not read.</b> The sidebar module is BUNDLED the way `npm run bundle` builds it and called with a fake view; every page it writes is RUN on the
 * page harness (`.agents/PROJECT.md`: a webview page is tested by running it), and the page's own `ready` is delivered to the module's listener the way the
 * webview delivers it. The old code is a fake whose load is held open: a paint that waited for it would leave these tests with no page at all.</p>
 */

const ROOT = process.cwd();
const VERSION = '9.9.9-test';
const MARKS = Symbol.for('coai.measure.marks');

interface Config {
  readonly common: Record<string, unknown>;
  readonly bundles: readonly { readonly id: string; readonly entry: string }[];
}

/** One of `bundle-entries.json`'s bundles, built in memory with the options `npm run bundle` uses, and evaluated with the Node modules a module may require. */
async function bundled(id: string, require_: (name: string) => unknown): Promise<Record<string, unknown>> {
  const config = JSON.parse(readFileSync(join(ROOT, 'scripts', 'bundle-entries.json'), 'utf8')) as Config;
  const bundle = config.bundles.find((one) => one.id === id);
  assert.ok(bundle !== undefined, `bundle-entries.json has no ${id} bundle`);
  const result = await build({ absWorkingDir: ROOT, entryPoints: [bundle.entry], outfile: 'unused.js', bundle: true, write: false, logLevel: 'silent', ...config.common });
  const file = result.outputFiles[0];
  assert.ok(file !== undefined, 'esbuild produced no output');
  const holder = { exports: {} as Record<string, unknown> };
  new Function('module', 'exports', 'require', '__dirname', file.text)(holder, holder.exports, require_, join(ROOT, 'dist', 'modules'));

  return holder.exports;
}

const nodeModules: Record<string, unknown> = { 'node:fs': nodeFs, fs: nodeFs, 'node:path': nodePath, path: nodePath, 'node:crypto': nodeCrypto, crypto: nodeCrypto };

const realRequire = (name: string): unknown => {
  if (name === 'node:fs' || name === 'fs' || name === 'node:path' || name === 'path' || name === 'node:crypto' || name === 'crypto') {
    return nodeModules[name];
  }
  throw new Error(`the sidebar module may not load ${name}`);
};

type Listener = (message: unknown) => void;

/** A view as the host hands one over: html and options to write, one message listener to add, one disposal to hear. */
function fakeView() {
  const listeners: Listener[] = [];
  const disposals: (() => void)[] = [];
  const webview = {
    html: '',
    options: undefined as unknown,
    onDidReceiveMessage: (listener: Listener) => {
      listeners.push(listener);

      return { dispose: () => { listeners.splice(listeners.indexOf(listener), 1); } };
    },
    postMessage: () => Promise.resolve(true),
  };
  const view = { webview, onDidDispose: (listener: () => void) => { disposals.push(listener); return { dispose: () => undefined }; } };

  return {
    view,
    listening: () => listeners.length,
    hear: (message: unknown) => { for (const listener of [...listeners]) listener(message); },
    dispose: () => { for (const listener of disposals) listener(); },
  };
}

type Fake = ReturnType<typeof fakeView>;

/** Runs the page the view now holds and delivers what it posts to the view's listener, as the webview does. */
function showPage(fake: Fake): ReturnType<typeof runPanel> {
  const page = runPanel(panelState('bugz'), { html: fake.view.webview.html });
  for (const message of page.posted) {
    fake.hear(message);
  }

  return page;
}

/** Timers a test runs by hand, so "the moment after ready" and "the fallback" are moments the test chooses. */
function manualTimers() {
  const pending: { readonly run: () => void; readonly ms: number; cancelled: boolean }[] = [];

  return {
    setTimeout: (run: () => void, ms: number) => { const timer = { run, ms, cancelled: false }; pending.push(timer); return timer; },
    clearTimeout: (timer: unknown) => { if (timer !== undefined) (timer as { cancelled: boolean }).cancelled = true; },
    /** Runs every live timer whose delay is at most `ms`, shortest first. */
    advance: (ms: number) => { for (const timer of pending.filter((one) => !one.cancelled && one.ms <= ms).sort((a, b) => a.ms - b.ms)) { timer.cancelled = true; timer.run(); } },
    live: () => pending.filter((one) => !one.cancelled).length,
  };
}

const marksNow = (): string[] => ((globalThis as unknown as Record<symbol, { name: string }[] | undefined>)[MARKS] ?? []).map((one) => one.name);

interface Sidebar {
  readonly resolveWith: (seams: unknown, context: unknown, view: unknown, resolveContext: unknown, token: unknown, legacy: () => Promise<unknown>) => void;
  readonly resolveSidebar: (context: unknown, view: unknown, resolveContext: unknown, token: unknown, legacy: () => Promise<unknown>) => void;
  readonly FALLBACK_MS: number;
}

const loadSidebar = async (): Promise<Sidebar> => (await bundled('sidebar', realRequire)) as unknown as Sidebar;

/** A context whose global storage is a fresh folder, optionally holding a snapshot of the sidebar painted from `state`. */
function contextWith(snapshotOf?: ReturnType<typeof panelState>) {
  const dir = ownedTemp('coai-sidebar-snapshot');
  if (snapshotOf !== undefined) {
    const text = snapshotText(VERSION, panelHtml(snapshotOf, NONCE_PLACEHOLDER));
    assert.ok(text !== undefined, 'the fixture page was refused as a snapshot');
    writeFileSync(join(dir, SNAPSHOT_FILE), text);
  }

  return { dir, context: { globalStorageUri: { fsPath: dir }, extension: { packageJSON: { version: VERSION } } } };
}

/** A state with a phrase a person keeps, so the replayed page is known by something only this state drew. */
const WITH_PHRASE = panelState('bugz', { phrases: [{ id: 'p1', name: 'Deploy the snapshot', text: 'deploy it' }] });

/** The old code as a fake: loading it is a promise the test holds open, and it records what it was handed. */
function heldLegacy() {
  const calls: { view: unknown }[] = [];
  let loads = 0;
  let load!: (module: unknown) => void;
  let fail!: (error: unknown) => void;
  const loading = new Promise<unknown>((resolve, reject) => { load = resolve; fail = reject; });

  return {
    legacy: () => { loads += 1; return loading; },
    loads: () => loads,
    calls,
    finish: () => { load({ resolveView: (_context: unknown, view: unknown) => { calls.push({ view }); } }); },
    fail: (error: unknown) => { fail(error); },
  };
}

const settle = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });
const nonceOf = (html: string): { policy: string | undefined; script: string | undefined } => ({
  policy: /script-src 'nonce-([^']+)'/u.exec(html)?.[1],
  script: /<script nonce="([^"]+)">/u.exec(html)?.[1],
});
const SEAMS = (timers: ReturnType<typeof manualTimers>, logged: string[] = []) => ({ setTimeout: timers.setTimeout, clearTimeout: timers.clearTimeout, log: (message: string) => { logged.push(message); } });

test('a snapshot is painted and says ready BEFORE the old code is asked for; then the old code gets the same view', async () => {
  const sidebar = await loadSidebar();
  const { context } = contextWith(WITH_PHRASE);
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();
  const before = marksNow().length;

  sidebar.resolveWith(SEAMS(timers), context, fake.view, {}, {}, old.legacy);

  assert.deepEqual(fake.view.webview.options, { enableScripts: true }, 'the options are the old provider\'s own, so its later assignment is no change');
  assert.ok(fake.view.webview.html.includes('Deploy the snapshot'), 'the page is the last snapshot: it carries a phrase only the snapshot\'s state drew');
  assert.ok(fake.view.webview.html.includes('Refreshing…'), 'the person is told the page is not live yet');
  assert.ok(fake.view.webview.html.includes('<body inert>'), 'a page about to be replaced takes no press');
  assert.equal(old.loads(), 0, 'the old code is not loaded while the snapshot is being painted');

  const page = showPage(fake);
  assert.equal(page.posted.filter((one) => one['type'] === 'ready').length, 1, 'the snapshot page posts one ready');
  assert.equal(old.loads(), 0, 'ready alone does not load the old code: that is a timer\'s job, after the handler returned');
  assert.deepEqual(marksNow().slice(before), ['firstPaint', 'ready'], 'first paint, then ready, nothing of the old code');

  timers.advance(0);
  assert.equal(old.loads(), 1, 'the old code is loaded in the background once the page is ready');
  assert.equal(fake.listening(), 0, 'the module stopped listening: the old provider is the view\'s only reader');
  old.finish();
  await settle();
  assert.equal(old.calls.length, 1);
  assert.equal(old.calls[0]?.view, fake.view, 'the SAME view, not a copy: there is no second registration');
});

test('with no snapshot the skeleton is painted and says ready, and the old code is loaded after it', async () => {
  const sidebar = await loadSidebar();
  const { context } = contextWith();
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();

  sidebar.resolveWith(SEAMS(timers), context, fake.view, {}, {}, old.legacy);

  assert.ok(fake.view.webview.html.includes('Loading ConnectOtherAIs'), 'the skeleton says what is happening');
  assert.equal(fake.view.webview.html.includes('Deploy the snapshot'), false);
  const page = showPage(fake);
  assert.deepEqual(page.posted, [{ type: 'ready' }], 'the skeleton acknowledges its document, and says nothing else');
  assert.equal(old.loads(), 0);
  timers.advance(0);
  assert.equal(old.loads(), 1);
  old.finish();
  await settle();
  assert.equal(old.calls[0]?.view, fake.view);
});

test('a page that never says ready does not hold the old code back for ever', async () => {
  const sidebar = await loadSidebar();
  const { context } = contextWith(WITH_PHRASE);
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();

  sidebar.resolveWith(SEAMS(timers), context, fake.view, {}, {}, old.legacy);
  timers.advance(sidebar.FALLBACK_MS - 1);
  assert.equal(old.loads(), 0, 'not before the fallback');
  timers.advance(sidebar.FALLBACK_MS);
  assert.equal(old.loads(), 1, 'the fallback loads it');
  fake.hear({ type: 'ready' });
  timers.advance(10_000);
  assert.equal(old.loads(), 1, 'a ready that arrives after the hand-over loads nothing again');
});

test('a view that is disposed before the hand-over is never handed over', async () => {
  const sidebar = await loadSidebar();
  const { context } = contextWith(WITH_PHRASE);
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();

  sidebar.resolveWith(SEAMS(timers), context, fake.view, {}, {}, old.legacy);
  showPage(fake);
  fake.dispose();
  timers.advance(10_000);

  assert.equal(old.loads(), 0, 'VS Code resolves a new view when the sidebar is shown again; the old code is not given a dead one');
  assert.equal(timers.live(), 0);
});

test('every paint has its OWN nonce: the policy and the script agree, none is the placeholder, none is ever replayed', async () => {
  const sidebar = await loadSidebar();
  const { context, dir } = contextWith(WITH_PHRASE);
  const timers = manualTimers();
  const nonces: string[] = [];
  const skeletons: string[] = [];
  for (let at = 0; at < 3; at += 1) {
    const fake = fakeView();
    sidebar.resolveWith(SEAMS(timers), context, fake.view, {}, {}, () => new Promise(() => undefined));
    const { policy, script } = nonceOf(fake.view.webview.html);
    assert.ok(policy !== undefined && policy === script, 'the content security policy admits exactly the script the page carries');
    assert.match(policy, /^[A-Za-z0-9_-]{22}$/u, '128 bits, base64url, as `webviewNonce` makes them');
    assert.equal(fake.view.webview.html.includes(NONCE_PLACEHOLDER), false);
    nonces.push(policy);
  }
  assert.equal(new Set(nonces).size, 3, 'a snapshot replayed three times was given three nonces');
  const stored = readFileSync(join(dir, SNAPSHOT_FILE), 'utf8');
  for (const nonce of nonces) {
    assert.equal(stored.includes(nonce), false, 'the file on disk never holds a nonce that was used');
  }
  assert.equal(stored.split(NONCE_PLACEHOLDER).length - 1, 2, 'the file holds the placeholder where the policy and the script tag take a nonce, and nowhere else');
  for (let at = 0; at < 2; at += 1) {
    const fake = fakeView();
    sidebar.resolveWith(SEAMS(timers), contextWith().context, fake.view, {}, {}, () => new Promise(() => undefined));
    skeletons.push(nonceOf(fake.view.webview.html).policy ?? '');
  }
  assert.notEqual(skeletons[0], skeletons[1], 'the skeleton is given a fresh nonce too');
});

test('a snapshot that may not be replayed opens on the skeleton: damaged, from another build, over the cap, credential-like, or with a stray placeholder', async () => {
  const sidebar = await loadSidebar();
  const good = panelHtml(WITH_PHRASE, NONCE_PLACEHOLDER);
  const secretState = panelState('bugz', { phrases: [{ id: 'p2', name: 'key', text: 'token: sk-live-0123456789abcdefghij' }] });
  const cases: readonly (readonly [string, (dir: string) => void])[] = [
    ['truncated JSON', (dir) => { writeFileSync(join(dir, SNAPSHOT_FILE), (snapshotText(VERSION, good) ?? '').slice(0, 200)); }],
    ['another build', (dir) => { writeFileSync(join(dir, SNAPSHOT_FILE), snapshotText('0.0.1', good) ?? ''); }],
    ['over the cap', (dir) => { writeFileSync(join(dir, SNAPSHOT_FILE), JSON.stringify({ v: 1, version: VERSION, html: good + 'x'.repeat(SNAPSHOT_MAX_BYTES) })); }],
    ['a page written with a real nonce', (dir) => { writeFileSync(join(dir, SNAPSHOT_FILE), JSON.stringify({ v: 1, version: VERSION, html: good.split(NONCE_PLACEHOLDER).join('abcdefghijklmnopqrstuv') })); }],
    ['a stray placeholder', (dir) => { writeFileSync(join(dir, SNAPSHOT_FILE), JSON.stringify({ v: 1, version: VERSION, html: `${good}${NONCE_PLACEHOLDER}` })); }],
    ['a credential in the page', (dir) => { writeFileSync(join(dir, SNAPSHOT_FILE), JSON.stringify({ v: 1, version: VERSION, html: panelHtml(secretState, NONCE_PLACEHOLDER) })); }],
    ['not JSON', (dir) => { writeFileSync(join(dir, SNAPSHOT_FILE), 'not json'); }],
    ['a folder where the file should be', (dir) => { mkdirSync(join(dir, SNAPSHOT_FILE)); }],
  ];
  for (const [what, plant] of cases) {
    const { context, dir } = contextWith();
    plant(dir);
    const fake = fakeView();
    sidebar.resolveWith(SEAMS(manualTimers()), context, fake.view, {}, {}, () => new Promise(() => undefined));

    assert.ok(fake.view.webview.html.includes('Loading ConnectOtherAIs'), `${what}: the skeleton was expected`);
    assert.equal(fake.view.webview.html.includes('Deploy the snapshot'), false, `${what}: nothing of the snapshot may be drawn`);
  }
});

test('when the old code cannot be loaded the person is told, in words, and the failure is logged', async () => {
  const sidebar = await loadSidebar();
  const { context } = contextWith(WITH_PHRASE);
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();
  const logged: string[] = [];

  sidebar.resolveWith(SEAMS(timers, logged), context, fake.view, {}, {}, old.legacy);
  showPage(fake);
  timers.advance(0);
  old.fail(new Error('Cannot find module <legacy>'));
  await settle();

  assert.ok(fake.view.webview.html.includes('could not start'), 'a snapshot that says "Refreshing…" for ever would be a lie');
  assert.ok(fake.view.webview.html.includes('Cannot find module &lt;legacy&gt;'), 'the reason, escaped');
  assert.equal(logged.length, 1);
});

test('the real entry point: resolveSidebar paints at once and hands the same view to the old code on its own timer', async () => {
  const sidebar = await loadSidebar();
  const { context } = contextWith(WITH_PHRASE);
  const fake = fakeView();
  const old = heldLegacy();

  sidebar.resolveSidebar(context, fake.view, {}, {}, old.legacy);
  assert.ok(fake.view.webview.html.includes('Deploy the snapshot'));
  showPage(fake);
  await new Promise((resolve) => { setTimeout(resolve, 20); });
  assert.equal(old.loads(), 1, 'real timers: the old code was asked for after ready');
  old.finish();
  await settle();
  assert.equal(old.calls[0]?.view, fake.view);
});

/* ---------------------------------------------------------------------------------------------- *
 * The writer, and the whole way round: what the old code stores is what the module replays.
 * ---------------------------------------------------------------------------------------------- */

test('what the writer stores, the reader replays: the same page with a fresh nonce, and the real sidebar page is not mistaken for a credential', async () => {
  const dir = ownedTemp('coai-sidebar-snapshot');
  const timers: (() => void)[] = [];
  const writer = new SidebarSnapshotWriter({ directory: () => dir, version: VERSION, setTimer: (run) => { timers.push(run); return { stop: () => undefined }; } });
  const state = panelState('bugz', { phrases: [{ id: 'p1', name: 'Deploy the snapshot', text: 'deploy it' }] });
  writer.offer(() => panelHtml(state, NONCE_PLACEHOLDER));
  timers.shift()?.();
  await writer.settled();

  const stored = readSnapshot(dir, VERSION);
  assert.ok(stored !== undefined, 'the sidebar page the old code paints was refused by its own reader: a false positive would make the feature do nothing');
  assert.equal(stored, panelHtml(state, NONCE_PLACEHOLDER));
  assert.equal(readSnapshot(dir, '1.2.3'), undefined, 'another build reads nothing');
  assert.equal(readSnapshot(join(dir, 'missing'), VERSION), undefined);
  assert.equal(extensionVersionOf({}), '', 'a host that does not say its version gets no snapshot');
});

test('the writer waits, writes the NEWEST offer once per interval, and builds nothing until it writes', async () => {
  const dir = ownedTemp('coai-sidebar-snapshot');
  let now = 0;
  const timers: { run: () => void; ms: number }[] = [];
  const written: string[] = [];
  const writer = new SidebarSnapshotWriter({
    directory: () => dir, version: VERSION, now: () => now,
    setTimer: (run, ms) => { const timer = { run, ms }; timers.push(timer); return { stop: () => { timers.splice(timers.indexOf(timer), 1); } }; },
    write: (_path, text) => { written.push(text); return Promise.resolve(); },
  });
  let built = 0;
  const page = (phrase: string) => () => { built += 1; return panelHtml(panelState('bugz', { phrases: [{ id: 'p', name: phrase, text: phrase }] }), NONCE_PLACEHOLDER); };

  writer.offer(page('zzfirstzz'));
  writer.offer(page('zzsecondzz'));
  assert.equal(built, 0, 'offering is a function stored, not a page built');
  assert.equal(timers.length, 1, 'two offers, one timer');
  now = 1000;
  timers.shift()?.run();
  await writer.settled();
  assert.equal(built, 1);
  assert.equal(written.length, 1);
  assert.ok(written[0]?.includes('zzsecondzz') && !written[0].includes('zzfirstzz'), 'the newest offer is the one that was written');

  now = 3000;
  writer.offer(page('zzsecondzz'));
  assert.ok((timers[0]?.ms ?? 0) >= 8000, 'the next write is at least ten seconds after the last, not at the next tick');
  timers.shift()?.run();
  await writer.settled();
  assert.equal(written.length, 1, 'an unchanged page costs no write');
});

test('a page that may not be kept REMOVES the file instead of leaving an older page behind; a closing window writes what it still holds', async () => {
  const dir = ownedTemp('coai-sidebar-snapshot');
  const log: string[] = [];
  const timers: (() => void)[] = [];
  const writer = new SidebarSnapshotWriter({ directory: () => dir, version: VERSION, setTimer: (run) => { timers.push(run); return { stop: () => undefined }; }, log: (message) => { log.push(message); } });
  const okState = panelState('bugz', { phrases: [{ id: 'p1', name: 'Deploy the snapshot', text: 'deploy it' }] });
  writer.offer(() => panelHtml(okState, NONCE_PLACEHOLDER));
  timers.shift()?.();
  await writer.settled();
  assert.ok(statSync(join(dir, SNAPSHOT_FILE)).size > 0);

  const secret = panelState('bugz', { phrases: [{ id: 'p2', name: 'k', text: '-----BEGIN RSA PRIVATE KEY-----' }] });
  writer.offer(() => panelHtml(secret, NONCE_PLACEHOLDER));
  // The next interval is far off, so the closing window is what writes this one.
  writer.dispose();
  assert.equal(readSnapshot(dir, VERSION), undefined, 'a credential-like page left no snapshot at all, old or new');
  assert.deepEqual(log, []);
});

/* ---------------------------------------------------------------------------------------------- *
 * The shell's entry: the sidebar module first, the old code as the fallback.
 * ---------------------------------------------------------------------------------------------- */

async function entryProvider(modules: Record<string, unknown>) {
  const config = JSON.parse(readFileSync(join(ROOT, 'scripts', 'bundle-entries.json'), 'utf8')) as Config;
  const bundle = config.bundles.find((one) => one.id === 'extension');
  assert.ok(bundle !== undefined);
  const code = (await build({ absWorkingDir: ROOT, entryPoints: [bundle.entry], outfile: 'unused.js', bundle: true, write: false, logLevel: 'silent', ...config.common })).outputFiles[0]?.text ?? '';
  let provider: { resolveWebviewView: (view: unknown, resolveContext: unknown, token: unknown) => Promise<unknown> } | undefined;
  const disposable = { dispose: () => undefined };
  const api = {
    commands: { registerCommand: () => disposable },
    window: { registerWebviewViewProvider: (_id: string, given: typeof provider) => { provider = given; return disposable; }, registerWebviewPanelSerializer: () => disposable, registerUriHandler: () => disposable },
    env: {}, version: '1',
  };
  const fakeRequire = (name: string): unknown => {
    if (name === 'vscode') return api;
    if (name === 'node:path' || name === 'path') return nodePath;
    const found = Object.entries(modules).find(([id]) => name.endsWith(`${id}.js`));
    if (found === undefined) throw new Error(`no module ${name}`);
    if (found[1] instanceof Error) throw found[1];

    return found[1];
  };
  const holder = { exports: {} as { activate?: (context: unknown) => unknown } };
  new Function('module', 'exports', 'require', '__dirname', 'process', code)(holder, holder.exports, fakeRequire, join(ROOT, 'dist'), { env: {}, platform: 'linux' });
  holder.exports.activate?.({ subscriptions: [] });
  assert.ok(provider !== undefined, 'the entry registered no view provider');

  return provider;
}

test('the entry resolves the sidebar through the sidebar module and gives it the entry\'s own loader of the old code; it resolves nothing itself', async () => {
  const calls: string[] = [];
  const legacyModule = { resolveView: () => { calls.push('legacy.resolveView'); } };
  let handed: (() => Promise<unknown>) | undefined;
  const provider = await entryProvider({
    sidebar: { resolveSidebar: (_c: unknown, view: unknown, _r: unknown, _t: unknown, legacy: () => Promise<unknown>) => { calls.push(`sidebar.resolveSidebar:${String((view as { id: string }).id)}`); handed = legacy; } },
    legacy: legacyModule,
  });
  const view = { id: 'view-1' };
  await provider.resolveWebviewView(view, {}, {});

  assert.deepEqual(calls, ['sidebar.resolveSidebar:view-1'], 'the old code is not touched by the entry: the sidebar module decides when');
  assert.equal(await handed?.(), legacyModule, 'the loader it was given is the entry\'s own, and answers the one legacy module');
});

test('when the sidebar module cannot be loaded or throws, the old code resolves the view alone, as before', async () => {
  for (const [what, sidebar] of [
    ['cannot be loaded', new Error('no such file')],
    ['throws', { resolveSidebar: () => { throw new Error('boom'); } }],
  ] as const) {
    const seen: unknown[] = [];
    const provider = await entryProvider({ sidebar, legacy: { resolveView: (_c: unknown, view: unknown) => { seen.push(view); } } });
    const view = { id: what };
    await provider.resolveWebviewView(view, {}, {});

    assert.deepEqual(seen, [view], `the sidebar module ${what}: the old code must still resolve the view`);
  }
});
