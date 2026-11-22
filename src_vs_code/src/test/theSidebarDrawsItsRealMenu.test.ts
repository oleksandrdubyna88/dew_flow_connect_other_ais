import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import * as nodeCrypto from 'node:crypto';
import { readFileSync } from 'node:fs';
import * as nodePath from 'node:path';
import { join } from 'node:path';
import { test } from 'node:test';
import { build } from 'esbuild';
import { panelHtml, PANEL_SECTIONS } from '../panelView';
import { sectionsOn } from '../panelSurface';
import { MENU_SECTIONS, menuPage } from '../sidebarMenu';
import { NONCE_PLACEHOLDER, skeletonPage, snapshotPage } from '../sidebarSnapshot';
import { type DomElement } from './pageDom';
import { runMenuPage } from './menuPageHarness';
import { panelState } from './panelPageHarness';
import { stylesheet } from './cssRules';

/**
 * The sidebar draws its REAL MENU before the old code loads, and the cold start ends when that menu works (v2 E4; owner rule 2026-11-23).
 *
 * <p><b>RUN, not read.</b> The sidebar module is BUNDLED the way `npm run bundle` builds it and called with a fake view. Every page it writes is RUN on the
 * shared DOM shim (`pageDom.ts`, through `menuPageHarness.ts`), and what the page posts is delivered to the module's listener as the webview delivers it. The
 * old code and chat are fakes whose loads are held open: a menu that waited for either would leave these tests with nothing to look at.</p>
 */

const ROOT = process.cwd();
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

const realRequire = (name: string): unknown => {
  if (name === 'node:crypto' || name === 'crypto') {
    return nodeCrypto;
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

/** Runs the menu the view now holds; what the page posts reaches the view's listener at once, as the webview delivers it. */
function showMenu(fake: Fake) {
  return runMenuPage(fake.view.webview.html, (message) => { fake.hear(message); });
}

/** Timers a test runs by hand, so "the moment after menuReady" and "the fallback" are moments the test chooses. */
function manualTimers() {
  const pending: { readonly run: () => void; readonly ms: number; cancelled: boolean }[] = [];

  return {
    setTimeout: (run: () => void, ms: number) => { const timer = { run, ms, cancelled: false }; pending.push(timer); return timer; },
    clearTimeout: (timer: unknown) => { if (timer !== undefined) (timer as { cancelled: boolean }).cancelled = true; },
    /** Runs every live timer whose delay is at most `ms`, shortest first, and the ones they start. */
    advance: (ms: number) => {
      for (let again = true; again;) {
        const due = pending.filter((one) => !one.cancelled && one.ms <= ms).sort((a, b) => a.ms - b.ms);
        again = due.length > 0;
        for (const timer of due) { timer.cancelled = true; timer.run(); }
      }
    },
    live: () => pending.filter((one) => !one.cancelled).length,
  };
}

const marksNow = (): string[] => ((globalThis as unknown as Record<symbol, { name: string }[] | undefined>)[MARKS] ?? []).map((one) => one.name);

type HandOver = { readonly opened: readonly string[]; readonly closed: readonly string[] } | undefined;

interface Sidebar {
  readonly resolveWith: (seams: unknown, context: unknown, view: unknown, resolveContext: unknown, token: unknown, legacy: () => Promise<unknown>, chat: () => Promise<unknown>) => void;
  readonly resolveSidebar: (context: unknown, view: unknown, resolveContext: unknown, token: unknown, legacy: () => Promise<unknown>, chat: () => Promise<unknown>) => void;
  readonly FALLBACK_MS: number;
}

const loadSidebar = async (): Promise<Sidebar> => (await bundled('sidebar', realRequire)) as unknown as Sidebar;

/** The old code as a fake: loading it is a promise the test holds open, and it records what it was handed and which marks existed when it was asked for. */
function heldLegacy() {
  const calls: { view: unknown; handOver: HandOver }[] = [];
  const askedAfter: string[][] = [];
  let loads = 0;
  let load!: (module: unknown) => void;
  let fail!: (error: unknown) => void;
  const loading = new Promise<unknown>((resolve, reject) => { load = resolve; fail = reject; });

  return {
    legacy: () => { loads += 1; askedAfter.push(marksNow()); return loading; },
    loads: () => loads,
    askedAfter,
    calls,
    finish: () => { load({ resolveView: (_context: unknown, view: unknown, _resolve: unknown, _token: unknown, handOver: HandOver) => { calls.push({ view, handOver }); } }); },
    fail: (error: unknown) => { fail(error); },
  };
}

/** Chat as a fake: it records that it was started, and which marks existed when it was. */
function fakeChat(outcome: 'starts' | 'throws' = 'starts') {
  const startedAfter: string[][] = [];

  return {
    chat: () => { startedAfter.push(marksNow()); return outcome === 'starts' ? Promise.resolve({}) : Promise.reject(new Error('chat is broken')); },
    started: () => startedAfter.length,
    startedAfter,
  };
}

const settle = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });
const nonceOf = (html: string): { policy: string | undefined; script: string | undefined } => ({
  policy: /script-src 'nonce-([^']+)'/u.exec(html)?.[1],
  script: /<script nonce="([^"]+)">/u.exec(html)?.[1],
});
const SEAMS = (timers: ReturnType<typeof manualTimers>, logged: string[] = []) => ({ setTimeout: timers.setTimeout, clearTimeout: timers.clearTimeout, log: (message: string) => { logged.push(message); } });
const CONTEXT = {};

/** The ids and visible words of the section headers a page drew, in order. */
const headersOf = (sections: readonly DomElement[]): { id: string | null; title: string }[] => sections.map((one) => ({ id: one.getAttribute('data-section'), title: one.querySelector('summary')?.textContent.trim() ?? '' }));

/* ---------------------------------------------------------------------------------------------- *
 * The page: the real menu, interactive, saying menuReady only when it is one.
 * ---------------------------------------------------------------------------------------------- */

test('the menu page draws every section header with its words, in order, collapsed, none inert; and posts ready, then menuReady', () => {
  const run = runMenuPage(menuPage('test-nonce'));

  assert.deepEqual(headersOf(run.sections()), MENU_SECTIONS.map((one) => ({ id: one.id, title: one.title })), 'the headers a person sees are the menu\'s definition, in its order');
  assert.ok(run.sections().every((one) => !one.open), 'every section starts collapsed, as the current sidebar does');
  assert.equal(run.doc.querySelectorAll('[inert]').length, 0, 'nothing in the menu is inert: it is live from its first paint');
  assert.deepEqual(run.posted.map((one) => one['type']), ['ready', 'menuReady'], 'ready is recorded, menuReady is the one that counts, and it comes after the headers exist');
});

test('a press on a header opens its section and tells the host, a second press closes it; the other sections are not touched', () => {
  const run = runMenuPage(menuPage('test-nonce'));
  const target = MENU_SECTIONS[1].id;

  run.press(target);
  assert.deepEqual(run.sections().filter((one) => one.open).map((one) => one.getAttribute('data-section')), [target], 'only the pressed section opened');
  const open = run.sections().find((one) => one.open);
  assert.match(open?.textContent ?? '', /loading…/u, 'an opened section says it is loading until the live page replaces the menu');
  assert.deepEqual(run.posted.at(-1), { type: 'section', id: target, open: true });

  run.press(target);
  assert.equal(run.sections().some((one) => one.open), false);
  assert.deepEqual(run.posted.at(-1), { type: 'section', id: target, open: false });
});

test('menuReady is said ONLY by a document that shows every header and lets a person use it: a skeleton, an inert copy, a missing header, a header without words say nothing', () => {
  const live = menuPage('test-nonce');
  const messagesOf = (html: string) => runMenuPage(html).posted.map((one) => one['type']);
  assert.ok(messagesOf(live).includes('menuReady'), 'the same harness, the real menu: it says menuReady (without this the refusals below could be a broken harness)');

  const first = MENU_SECTIONS[0].id;
  const refused: readonly (readonly [string, string])[] = [
    ['the old skeleton', skeletonPage('test-nonce')],
    ['an inert menu', live.replace('<body>', '<body inert>')],
    ['a menu whose first header is inert', live.replace(`data-section="${first}"`, `data-section="${first}" inert`)],
    ['a menu whose first header is hidden', live.replace(`data-section="${first}"`, `data-section="${first}" hidden`)],
    ['a menu with a header missing', live.replace(new RegExp(`<details class="section sec-${first}"[\\s\\S]*?</details>`, 'u'), '')],
    ['a menu with a header that has no words', live.replace(`<summary>${MENU_SECTIONS[0].title}</summary>`, '<summary> </summary>')],
  ];
  for (const [what, html] of refused) {
    assert.equal(messagesOf(html).includes('menuReady'), false, `${what} must not say menuReady`);
  }
  assert.ok(messagesOf(skeletonPage('test-nonce')).includes('ready'), 'the skeleton still says ready: ready alone is NOT the end of the start');
});

test('a snapshot of the old page, replayed inert, says ready but never menuReady; the same page live says it', () => {
  const state = panelState('bugz', { phrases: [{ id: 'p1', name: 'Deploy the snapshot', text: 'deploy it' }] });
  const stored = panelHtml(state, NONCE_PLACEHOLDER);
  const stubs = { requestAnimationFrame: () => 0 };
  const inert = runMenuPage(snapshotPage(stored, 'n1'), undefined, stubs);

  assert.ok(inert.posted.some((one) => one['type'] === 'ready'));
  assert.equal(inert.posted.some((one) => one['type'] === 'menuReady'), false, 'an inert page cannot be used, so it is not the menu being ready');
  const live = runMenuPage(panelHtml(state, 'n2'), undefined, stubs);
  assert.deepEqual(headersOf(live.sections()), MENU_SECTIONS.map((one) => ({ id: one.id, title: one.title })), 'the old page draws the same headers the menu draws');
  assert.ok(live.posted.some((one) => one['type'] === 'menuReady'), 'the old page says menuReady too when it is shown live (the sidebar module may be absent)');
});

test('the old panel and the shell\'s menu draw the same sections: the same ids, words and order, and the old stylesheet carries the menu\'s rules', () => {
  const old = sectionsOn(PANEL_SECTIONS, 'sidebar').map((one) => ({ id: one.id, title: one.title }));
  assert.deepEqual(old, MENU_SECTIONS.map((one) => ({ id: one.id, title: one.title })), 'a section added to one list and not the other is a menu that changes when the old code loads');

  const menuRules = stylesheet(menuPage('n'));
  const oldRules = stylesheet(panelHtml(panelState('bugz'), 'n'));
  const asText = (rules: readonly { selector: string; body: string }[], selector: string) => rules.filter((rule) => rule.selector === selector).map((rule) => rule.body);
  const header = menuRules.filter((rule) => rule.selector.startsWith('.section') || rule.selector.startsWith('.sec-') || rule.selector === 'body' || rule.selector === '.hint');
  assert.ok(header.length >= 10, 'the scan still finds the menu\'s header rules (a pattern that matches nothing proves nothing)');
  for (const rule of header) {
    assert.deepEqual(asText(oldRules, rule.selector), asText(menuRules, rule.selector), `the rule ${rule.selector} differs between the menu and the old page`);
  }
  assert.equal(oldRules.some((rule) => rule.selector === '.sec-cadence > summary'), false, 'Consultation cadence is removed in v2, header and colour');
});

/* ---------------------------------------------------------------------------------------------- *
 * The module: paints the menu, hands the view to the old code AFTER menuReady, starts chat after it.
 * ---------------------------------------------------------------------------------------------- */

test('the menu is written before anything is loaded; menuReady is marked before the old code is asked for, and neither the old code nor chat runs inside the message handler', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const chat = fakeChat();
  const timers = manualTimers();
  const before = marksNow().length;

  sidebar.resolveWith(SEAMS(timers), CONTEXT, fake.view, {}, {}, old.legacy, chat.chat);

  assert.deepEqual(fake.view.webview.options, { enableScripts: true }, 'the options are the old provider\'s own, so its later assignment is no change');
  assert.deepEqual(headersOf(runMenuPage(fake.view.webview.html).sections()), MENU_SECTIONS.map((one) => ({ id: one.id, title: one.title })), 'the page the view holds is the menu');
  assert.equal(old.loads() + chat.started(), 0, 'nothing else is loaded while the menu is being written');

  const page = showMenu(fake);
  assert.deepEqual(page.posted.map((one) => one['type']), ['ready', 'menuReady']);
  assert.equal(old.loads() + chat.started(), 0, 'the message handler loaded nothing: the work is on a timer after it');
  assert.deepEqual(marksNow().slice(before), ['firstPaint', 'ready', 'menuReady'], 'first paint, ready, menuReady — nothing of the old code before the end of the start');

  timers.advance(0);
  assert.equal(old.loads(), 1, 'the old code is loaded in the background once the menu is ready');
  assert.equal(chat.started(), 1, 'and so is chat, so its context keys appear without anyone opening a chat');
  assert.ok(old.askedAfter[0]?.includes('menuReady') && chat.startedAfter[0]?.includes('menuReady'), 'both were asked for AFTER the menuReady mark existed');
  assert.equal(fake.listening(), 1, 'the module still listens until the old code has the view, so a press in between is not lost');
  old.finish();
  await settle();
  assert.equal(old.calls.length, 1);
  assert.equal(old.calls[0]?.view, fake.view, 'the SAME view, not a copy: there is no second registration');
  assert.equal(fake.listening(), 0, 'now the old provider is the view\'s only reader');
});

test('chat is started in the background even while the old code is still loading, and a chat that fails is logged, never thrown into the menu', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const chat = fakeChat('throws');
  const timers = manualTimers();
  const logged: string[] = [];

  sidebar.resolveWith(SEAMS(timers, logged), CONTEXT, fake.view, {}, {}, old.legacy, chat.chat);
  showMenu(fake);
  timers.advance(0);
  await settle();

  assert.equal(chat.started(), 1, 'chat did not wait for the old code');
  assert.equal(old.calls.length, 0);
  assert.deepEqual(logged, ['chat could not be started in the background']);
});

test('a section opened before the old code has the view shows "loading…" in that section only, asks for the old code at once, and stays open after the hand-over', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();
  sidebar.resolveWith(SEAMS(timers), CONTEXT, fake.view, {}, {}, old.legacy, fakeChat().chat);
  // The person opens two sections and closes one again, on a page whose menuReady never arrived at the host: only the ARRIVAL of a press asks for the old code.
  const page = runMenuPage(fake.view.webview.html, (message) => { if (message['type'] !== 'menuReady') fake.hear(message); });
  const [gates, consultations] = [MENU_SECTIONS[1].id, MENU_SECTIONS[2].id];

  page.press(gates);
  page.press(consultations);
  page.press(consultations);

  assert.deepEqual(page.sections().filter((one) => one.open).map((one) => one.getAttribute('data-section')), [gates], 'one section is open');
  assert.match(page.sections().find((one) => one.open)?.textContent ?? '', /loading…/u, 'and it says it is loading');
  assert.equal(old.loads(), 0, 'the press does not load the old code inside the handler either');
  timers.advance(0);
  assert.equal(old.loads(), 1, 'the old code is asked for at once, on the zero timer, not after the fallback');

  // A press made while the old code is loading is still the person's choice.
  page.press(MENU_SECTIONS[3].id);
  old.finish();
  await settle();

  assert.equal(old.calls.length, 1);
  assert.deepEqual(old.calls[0]?.handOver, { opened: [gates, MENU_SECTIONS[3].id], closed: [consultations] }, 'the old code is told which sections the person left open and which they closed');
});

test('a page that never says menuReady does not hold the old code and chat back for ever; a menuReady that arrives after the hand-over loads nothing again', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const chat = fakeChat();
  const timers = manualTimers();

  // The fallback must not fire inside the 2000 ms the start is allowed (coldstart-lib.mjs LIMITS.m2MaxMs): the old code loads in one block of the host's
  // thread, and one that began while menuReady was on its way would be counted as the menu's time.
  assert.ok(sidebar.FALLBACK_MS > 2000, 'the fallback is later than the start budget');
  sidebar.resolveWith(SEAMS(timers), CONTEXT, fake.view, {}, {}, old.legacy, chat.chat);
  timers.advance(sidebar.FALLBACK_MS - 1);
  assert.equal(old.loads() + chat.started(), 0, 'not before the fallback');
  timers.advance(sidebar.FALLBACK_MS);
  assert.equal(old.loads(), 1, 'the fallback loads the old code');
  assert.equal(chat.started(), 1, 'and starts chat');
  old.finish();
  await settle();
  fake.hear({ type: 'menuReady' });
  timers.advance(10_000);
  assert.equal(old.loads(), 1);
  assert.equal(chat.started(), 1);
});

test('a view that is disposed before the old code has it is never handed over, and nothing is left running', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();

  sidebar.resolveWith(SEAMS(timers), CONTEXT, fake.view, {}, {}, old.legacy, fakeChat().chat);
  showMenu(fake);
  timers.advance(0);
  assert.equal(old.loads(), 1);
  fake.dispose();
  old.finish();
  await settle();

  assert.equal(old.calls.length, 0, 'VS Code resolves a new view when the sidebar is shown again; the old code is not given a dead one');
  assert.equal(timers.live(), 0);
});

test('messages that are not a menu\'s are ignored: an unknown type, a section the menu does not have, something that is not an object', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();

  const chat = fakeChat();
  const menuReadyBefore = marksNow().filter((name) => name === 'menuReady').length;
  sidebar.resolveWith(SEAMS(timers), CONTEXT, fake.view, {}, {}, old.legacy, chat.chat);
  for (const message of [null, 7, 'menuReady', { type: 'nonsense' }, { type: 'section', id: 'cadence', open: true }, { type: 'section', id: 7, open: true }, { type: 'section', open: true }]) {
    fake.hear(message);
  }
  timers.advance(0);

  assert.equal(old.loads() + chat.started(), 0, 'none of them asked for the old code or chat (the fallback has not come)');
  assert.equal(marksNow().filter((name) => name === 'menuReady').length, menuReadyBefore, 'and none of them was taken for a menuReady');
});

test('every paint has its OWN nonce: the policy and the script agree, and none is ever replayed', async () => {
  const sidebar = await loadSidebar();
  const timers = manualTimers();
  const nonces: string[] = [];
  for (let at = 0; at < 3; at += 1) {
    const fake = fakeView();
    sidebar.resolveWith(SEAMS(timers), CONTEXT, fake.view, {}, {}, () => new Promise(() => undefined), () => new Promise(() => undefined));
    const { policy, script } = nonceOf(fake.view.webview.html);
    assert.ok(policy !== undefined && policy === script, 'the content security policy admits exactly the script the page carries');
    assert.match(policy, /^[A-Za-z0-9_-]{22}$/u, '128 bits, base64url, as `webviewNonce` makes them');
    nonces.push(policy);
  }
  assert.equal(new Set(nonces).size, 3);
});

test('when the old code cannot be loaded the person is told, in words, and the failure is logged', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const timers = manualTimers();
  const logged: string[] = [];

  sidebar.resolveWith(SEAMS(timers, logged), CONTEXT, fake.view, {}, {}, old.legacy, fakeChat().chat);
  showMenu(fake);
  timers.advance(0);
  old.fail(new Error('Cannot find module <legacy>'));
  await settle();

  assert.ok(fake.view.webview.html.includes('could not start'), 'a menu that says "loading…" for ever would be a lie');
  assert.ok(fake.view.webview.html.includes('Cannot find module &lt;legacy&gt;'), 'the reason, escaped');
  assert.equal(logged.length, 1);
  assert.equal(fake.listening(), 0);
});

test('the real entry point: resolveSidebar draws the menu at once, and on its own timers hands the same view to the old code and starts chat', async () => {
  const sidebar = await loadSidebar();
  const fake = fakeView();
  const old = heldLegacy();
  const chat = fakeChat();

  sidebar.resolveSidebar(CONTEXT, fake.view, {}, {}, old.legacy, chat.chat);
  assert.deepEqual(headersOf(runMenuPage(fake.view.webview.html).sections()).map((one) => one.id), MENU_SECTIONS.map((one) => one.id));
  showMenu(fake);
  await new Promise((resolve) => { setTimeout(resolve, 20); });
  assert.equal(old.loads(), 1, 'real timers: the old code was asked for after menuReady');
  assert.equal(chat.started(), 1);
  old.finish();
  await settle();
  assert.equal(old.calls[0]?.view, fake.view);
});

/* ---------------------------------------------------------------------------------------------- *
 * The shell's entry: the sidebar module first, the old code as the fallback, chat through its own loader.
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

test('the entry resolves the sidebar through the sidebar module and gives it the entry\'s own loaders of the old code and of chat; it loads neither itself', async () => {
  const calls: string[] = [];
  const legacyModule = { resolveView: () => { calls.push('legacy.resolveView'); } };
  const chatModule = { start: (context: unknown) => { calls.push(`chat.start:${String(context !== undefined)}`); return {}; } };
  let handed: (() => Promise<unknown>) | undefined;
  let startChat: (() => Promise<unknown>) | undefined;
  const provider = await entryProvider({
    sidebar: {
      resolveSidebar: (_c: unknown, view: unknown, _r: unknown, _t: unknown, legacy: () => Promise<unknown>, chat: () => Promise<unknown>) => {
        calls.push(`sidebar.resolveSidebar:${String((view as { id: string }).id)}`);
        handed = legacy;
        startChat = chat;
      },
    },
    legacy: legacyModule,
    chat: chatModule,
  });
  await provider.resolveWebviewView({ id: 'view-1' }, {}, {});

  assert.deepEqual(calls, ['sidebar.resolveSidebar:view-1'], 'the old code and chat are not touched by the entry: the sidebar module decides when');
  assert.equal(await handed?.(), legacyModule, 'the loader it was given is the entry\'s own, and answers the one legacy module');
  await startChat?.();
  assert.deepEqual(calls, ['sidebar.resolveSidebar:view-1', 'chat.start:true'], 'the chat loader loads the chat module and STARTS it, which is what puts its context keys in place');
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
