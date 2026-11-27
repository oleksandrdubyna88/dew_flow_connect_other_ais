import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { bodyOf, sourceOf } from './sourceReading';

/**
 * What `remoteAccountsHost.ts` may and may not do, read from its source (E1.S3, A3-T24 and A3-T26 and A3-T27, the host halves). The module imports
 * `vscode`, so no unit test can run it; each scan has a companion that proves the scan finds what it scans for.
 */

const host = sourceOf('remoteAccountsHost.ts');
/** The window's round since E2.S4: the driver, pure over its seams (`remoteAccountsDriver.ts`); the host only binds them. */
const driver = sourceOf('remoteAccountsDriver.ts');
const count = (text: string, pattern: RegExp): number => [...text.matchAll(pattern)].length;

/** Every `.update(` or write of a file or a `JSON.stringify(` — the ways a reading could be stored. */
const STORES = /\.update\(|writeFile|appendFile|JSON\.stringify\(|\.setKeysForSync\(/gu;

test('A3-T24 (host): nothing of a reading is stored by the host', () => {
  assert.equal(count(host, STORES), 0, 'an email reached globalState: the host stores something');
  assert.equal(count('context.globalState.update(KEY, memory);', STORES), 1, 'companion: the scan finds a globalState write');
  assert.equal(count('writeFile(path, JSON.stringify(memory));', STORES), 2, 'companion: the scan finds a file write and a stringify');
  assert.ok(count(host, /globalState\.get\(/gu) >= 1, 'companion: the host reads the state row, so the scan looked at the right file');
  assert.ok(!/\bconsole\./u.test(host), 'the host writes to the console: its log goes through the one default seam');
});

test('A4-T14 (F5): the Stop fires the redraw only after the Stop is written', () => {
  const stop = bodyOf(driver, '  async stop(');
  const written = stop.indexOf('await stopTheRound(');
  assert.ok(written > 0, 'the scan does not find the write of the Stop');
  const fired = stop.indexOf('this.world.landed(undefined)');
  assert.ok(fired >= 0, 'the scan does not find the redraw of the Stop');
  assert.ok(fired > written, 'the redraw is fired before the Stop is written, so the list reads the old state');
});

test('A3-T26 (host): readAccounts( is called once, with three arguments, inside the parse seam', () => {
  assert.equal(count(host, /\breadAccounts\(/gu), 1, 'readAccounts( is called more than once');
  assert.match(host, /parse:\s*\(run,\s*mode\)\s*=>\s*readAccounts\(run,\s*mode,\s*Date\.now\(\)\)/u, 'the parse seam is not (run, mode) => readAccounts(run, mode, Date.now())');
  assert.equal(count('parse: (run) => readAccounts(run, x, Date.now())', /parse:\s*\(run,\s*mode\)\s*=>\s*readAccounts\(run,\s*mode,\s*Date\.now\(\)\)/gu), 0, 'companion: the scan tells a parse seam without the mode');
});

test('A3-T27 (host): the one setTimeout is applied only from timerFor, and runAccounts( is reached only through callFor(', () => {
  assert.equal(count(host, /\bsetTimeout\(/gu), 1, 'the host has more than one setTimeout');
  assert.equal(count(host, /\bsetInterval\(/gu) + count(driver, /\bsetInterval\(|\bsetTimeout\(/gu), 0, 'a timer is set outside the host\'s one seam, or by setInterval: a round is counted from the END of the last');
  assert.equal(count(driver, /this\.world\.setTimer\(/gu), 1, 'the driver sets a timer other than in apply');
  const applications = count(driver, /this\.apply\(/gu);
  assert.ok(applications >= 2, `the scan found ${applications} applications of a timer action, so it looked at nothing`);
  assert.equal(count(driver, /this\.apply\(timerFor\(/gu), applications, 'a timer action is applied that did not come from timerFor');
  assert.equal(count(host, /\brunAccounts\(/gu), 1, 'runAccounts( is called more than once');
  assert.equal(count(host, /\bcallFor\(/gu), 1);
  assert.match(host, /callFor\(this\.test,\s*real,\s*this\.replaced\)/u, 'the call is not built from callFor(test, real, replaced)');
  assert.equal(count(host, /\breal\b/gu), 3, '`real` is reached other than through callFor');
  assert.equal(count('const x = real; setTimeout(f, 1);', /this\.apply\(timerFor\(/gu), 0, 'companion: the scan tells a timer that is not applied from timerFor');
});

/** One method of the class, from its opening line to the closing brace at the method's own indent. */
const methodOf = (text: string, opening: string): string => {
  const at = text.indexOf(opening);
  assert.ok(at >= 0, `there is no ${opening} to read`);
  const end = text.indexOf('\n  }\n', at);

  return end < 0 ? text.slice(at) : text.slice(at, end + 5);
};

test('A3-T22 (T34, host): a host\'s news is applied to the window\'s memory only while the generation is the round\'s, then the list is redrawn', () => {
  assert.match(driver, /landed:\s*\(memory,\s*host\)\s*=>\s*\{\s*this\.landedFrom\(memory,\s*host\);\s*\}/u, 'the landed seam does not pass the memory and the place on');
  const from = methodOf(driver, '  private landedFrom(');
  const applied = from.indexOf('this.memoryNow =');
  const fired = from.indexOf('this.world.landed(host)');
  assert.ok(applied >= 0 && fired >= 0, 'the scan does not find the memory or the redraw in landedFrom');
  assert.ok(applied < fired, 'the list is redrawn before the news is applied');
  assert.match(from.slice(0, applied), /this\.generation === this\.roundGeneration/u, 'the news is applied with no check of the generation');
  assert.match(methodOf(driver, '  private async oneRound('), /this\.roundGeneration = this\.generation/u, 'the round\'s generation is not recorded when it starts');
  assert.equal(count('private landedFrom(memory) { this.memoryNow = memory; this.landed.fire(); }', /this\.generation === this\.roundGeneration/gu), 0, 'companion: the scan tells a landing that does not check the generation');
});

test('A3-T22 (T36, host): a Start given while a round runs is run when that round ends, not dropped', () => {
  const start = methodOf(driver, '  async start(');
  assert.match(start, /if \(started && this\.running\) \{\s*this\.startPending = true;/u, 'a Start during a round does not leave a pending refresh');
  assert.match(start, /void this\.runNow\('refresh'\)/u, 'a Start with no round running does not refresh');
  assert.match(methodOf(driver, ' runNow(trigger: Trigger)'), /this\.runPending\(mine\.ran\)/u, 'the end of a round does not look for a pending Start');
  const pending = methodOf(driver, '  private runPending(');
  assert.match(pending, /ran && this\.startPending/u, 'the pending Start is run when no round of this call ran');
  assert.match(pending, /this\.startPending = false;\s*void this\.runNow\('refresh'\)/u, 'a pending Start is not cleared and run');
  assert.match(methodOf(driver, '  async stop('), /this\.startPending = false/u, 'a Stop leaves a pending Start behind');
});

test('A3-T37 (host): a window that is disposed starts no timer and no round, and its round publishes nothing', () => {
  const dispose = methodOf(driver, '  dispose(');
  assert.match(dispose, /this\.disposed = true/u, 'dispose does not mark the window');
  assert.match(dispose, /this\.generation \+= 1/u, 'dispose does not invalidate the round that is in flight');
  assert.match(methodOf(driver, '  private apply('), /if \(this\.disposed \|\| action\.kind === 'none'\) \{\s*return;/u, 'apply sets a timer after dispose');
  assert.match(methodOf(driver, ' runNow(trigger: Trigger)'), /this\.disposed/u, 'runNow starts a round after dispose');
  assert.equal(count('dispose(): void { clearTimeout(this.timer); }', /this\.disposed = true/gu), 0, 'companion: the scan tells a dispose that does not mark the window');
});

// ───────────────── E2.S3: the tab's wiring (the host halves of E2S3-T10, T11, T12 and T13) ─────────────────

const panel = sourceOf('remoteAccountsPanel.ts');

test('E2S3-T11 (host): one panel for the window; it is released on dispose, is searchable, keeps no serializer and reads no file', () => {
  assert.equal(count(host, /new AccountsPanel\(/gu), 1, 'the host builds more than one tab');
  assert.equal(count(panel, /createWebviewPanel\(/gu), 1);
  assert.equal(count(panel, /onDidDispose\(/gu), 1, 'the tab is not released when it is closed');
  assert.match(panel, /enableFindWidget:\s*true/u, 'the tab has no find bar');
  assert.match(panel, /localResourceRoots:\s*\[\]/u, 'the tab may read local files');
  assert.ok(!/registerWebviewPanelSerializer/u.test(panel + host), 'a serializer would restore a tab that draws a model this window does not have yet');
  assert.equal(count(host, /new vscode\.EventEmitter/gu), 1, 'companion: the host still owns its one emitter, so the scan read the right file');
});

test('E2S3-T12 (host): the icon opens the tab and asks the one question through askPermissionFirst, never through askPermission', () => {
  const open = bodyOf(host, 'async function openTab(');
  assert.ok(open.indexOf('accounts.openTab()') >= 0 && open.indexOf('askPermissionFirst()') > open.indexOf('accounts.openTab()'), 'the tab is not opened before the question, or the question is not the first-press one');
  assert.equal(count(host, /askPermissionFirst\(/gu), 1);
  assert.equal(count(host, /\.askPermission\(\)/gu), 1, 'askPermission( is called from more than the tab\'s own Allow');
  assert.match(host, /askPermission:\s*\(\)\s*=>\s*this\.sessions\.askPermission\(\)/u, 'the tab\'s Allow does not call askPermission');
  assert.match(host, /withdraw:\s*\(\)\s*=>\s*this\.sessions\.withdraw\(\)/u, 'the tab\'s Withdraw does not call withdraw');
  assert.match(host, /registerCommand\('coai\.terminalAccounts'/u, 'the icon\'s command is not registered by name');
});

test('E2S3-T10 (host): a command of the tab reaches the doors only through runAccountsCommand, and the only grant it can ask for is the question', () => {
  assert.equal(count(host, /runAccountsCommand\(/gu), 1);
  assert.ok(!/keeper\.allow\(|\.allow\(\)/u.test(host), 'the host writes a grant by itself');
  assert.equal(count(host, /commitHost\(/gu), 1, 'the host writes the refusals list from more than the one *Not this host*');
  assert.match(host, /commitHost\(\{\s*kind:\s*'person',\s*host,\s*answer:\s*'no'\s*\}\)/u, 'the one commit is not a person\'s "no"');
});
