import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { bodyOf, sourceOf } from './sourceReading';

/**
 * What `remoteAccountsHost.ts` may and may not do, read from its source (E1.S3, A3-T24 and A3-T26 and A3-T27, the host halves). The module imports
 * `vscode`, so no unit test can run it; each scan has a companion that proves the scan finds what it scans for.
 */

const host = sourceOf('remoteAccountsHost.ts');
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

test('A4-T14 (F4): the list clears its "ran less than a minute ago" mark only after the items are set, so an overtaken draw keeps it', () => {
  const draw = bodyOf(host, '  private async draw(');
  const cleared = draw.indexOf('this.floor = false');
  assert.ok(cleared > 0, 'the scan does not find where the mark is cleared');
  assert.ok(cleared > draw.indexOf('this.pick.items ='), 'the mark is cleared before the items are set');
});

test('A4-T14 (F5): the Stop fires the redraw only after the Stop is written', () => {
  const stop = bodyOf(host, '  async stop(');
  const written = stop.indexOf('await stopTheRound(');
  assert.ok(written > 0, 'the scan does not find the write of the Stop');
  assert.ok(stop.indexOf('this.landed.fire()') > written, 'the redraw is fired before the Stop is written, so the list reads the old state');
});

test('A3-T26 (host): readAccounts( is called once, with three arguments, inside the parse seam', () => {
  assert.equal(count(host, /\breadAccounts\(/gu), 1, 'readAccounts( is called more than once');
  assert.match(host, /parse:\s*\(run,\s*mode\)\s*=>\s*readAccounts\(run,\s*mode,\s*Date\.now\(\)\)/u, 'the parse seam is not (run, mode) => readAccounts(run, mode, Date.now())');
  assert.equal(count('parse: (run) => readAccounts(run, x, Date.now())', /parse:\s*\(run,\s*mode\)\s*=>\s*readAccounts\(run,\s*mode,\s*Date\.now\(\)\)/gu), 0, 'companion: the scan tells a parse seam without the mode');
});

test('A3-T27 (host): the one setTimeout is applied only from timerFor, and runAccounts( is reached only through callFor(', () => {
  assert.equal(count(host, /\bsetTimeout\(/gu), 1, 'the host has more than one setTimeout');
  assert.equal(count(host, /\bsetInterval\(/gu), 0, 'the host uses setInterval: a round is counted from the END of the last');
  const applications = count(host, /this\.apply\(/gu);
  assert.ok(applications >= 2, `the scan found ${applications} applications of a timer action, so it looked at nothing`);
  assert.equal(count(host, /this\.apply\(timerFor\(/gu), applications, 'a timer action is applied that did not come from timerFor');
  assert.equal(count(host, /\brunAccounts\(/gu), 1, 'runAccounts( is called more than once');
  assert.equal(count(host, /\bcallFor\(/gu), 1);
  assert.match(host, /callFor\(this\.test,\s*real,\s*this\.replaced\)/u, 'the call is not built from callFor(test, real, replaced)');
  assert.equal(count(host, /\breal\b/gu), 3, '`real` is reached other than through callFor');
  assert.equal(count('const x = real; setTimeout(f, 1);', /this\.apply\(timerFor\(/gu), 0, 'companion: the scan tells a timer that is not applied from timerFor');
});
