import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { regionOr } from '../logRegions';

const EXTENSION = path.join(__dirname, '..', '..', 'src', 'extension.ts');

/**
 * One tab of the rounds log that cannot be built must not take the others with it.
 *
 * <p>The 2026-09-23 defect in its general form. `refreshRoundsLog` built every tab's HTML as an
 * argument to ONE `update` call, so a throw in the consultations tab — an unpriced consultation
 * reaching `toFixed` — skipped the push for the spending tab, the blind-spot tab and the totals as
 * well. A person saw it as two unrelated bugs: the date switch did nothing, and *What it keeps
 * missing* never opened. Neither tab was broken; the push that carried them never ran.</p>
 */

const silent = (): void => undefined;

test('a tab that builds is passed through untouched', async () => {
  assert.equal(await regionOr('spending', () => '<p>numbers</p>', silent), '<p>numbers</p>');
  assert.equal(await regionOr('spending', async () => '<p>later</p>', silent), '<p>later</p>');
});

test('a tab that throws becomes a SAID failure in its own place, and nothing is thrown past it', async () => {
  const html = await regionOr('consultations', () => {
    throw new TypeError("Cannot read properties of undefined (reading 'toFixed')");
  }, silent);

  assert.match(html, /consultations/, 'it names which tab failed');
  assert.match(html, /reading &#39;toFixed&#39;|reading 'toFixed'/, 'it carries the reason, so an issue can quote it');
  assert.match(html, /other tabs/i, 'it says the rest of the page is still current');
});

test('a tab that REJECTS is caught the same way as one that throws', async () => {
  const html = await regionOr('consultations', () => Promise.reject(new Error('the server went away')), silent);

  assert.match(html, /the server went away/);
});

test('the reason is escaped: it is text from somewhere else, landing in a page', async () => {
  const html = await regionOr('spending', () => {
    throw new Error('<img src=x onerror=alert(1)>');
  }, silent);

  assert.ok(!html.includes('<img'), `an error message must never become markup; it said ${html}`);
});

test('every tab the log page is pushed is built through regionOr, never inline', () => {
  // The wiring lives in `extension.ts`, which imports `vscode` and so cannot run here. What CAN be
  // held is the shape: each builder call appears only as the body of a `regionOr`. Written back
  // inline as an argument to `update`, one throw would again silence every tab.
  const source = fs.readFileSync(EXTENSION, 'utf8');

  for (const call of ['panel.usageTab()', 'blindSpotsHtml(fresh)', 'panel.consultationsTab(fresh)']) {
    const all = source.split(call).length - 1;
    const wrapped = source.split(`() => ${call})`).length - 1;
    assert.ok(all > 0, `${call} is no longer called from extension.ts; this test must follow it`);
    assert.equal(wrapped, all, `${call} is built outside regionOr ${all - wrapped} time(s)`);
  }
});

test('a failure is written down where the extension host keeps its errors', async () => {
  const said: string[] = [];
  await regionOr('blind-spot', () => {
    throw new Error('boom');
  }, (message) => {
    said.push(message);
  });

  assert.equal(said.length, 1);
  assert.match(said[0] ?? '', /blind-spot/);
});
