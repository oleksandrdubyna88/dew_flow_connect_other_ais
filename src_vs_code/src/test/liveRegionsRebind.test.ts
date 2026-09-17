import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * A click must post ONE message, however long the panel has been open.
 *
 * <p>The panel patches four live regions on a five-second tick, and the controls inside a patched
 * region have to be bound again because the markup they lived in was replaced. Binding them on
 * every tick instead — which is what the code did — adds a listener each time without removing the
 * one before it: `addEventListener` adds, it does not replace. After a minute a single press on
 * *Open notifications* posts twelve messages and opens twelve windows. (codex, the S5 code round;
 * the questions region three lines away had the same defect and is fixed with it.)</p>
 *
 * <p><b>What this does not prove.</b> `panelView.ts` renders its script as text and the script is
 * not separable from the 2795-line file it lives in, so nothing here runs it — the notifications
 * page, which is, has its script EXECUTED in `notificationsPage.test.ts` for exactly this reason.
 * What is left is reading the source, and `testing.md` is clear about the price: a structural
 * assertion must pin the WHOLE condition, because one matching a fragment survives its own break.
 * So this asserts the pairing for every region by name, and asserts the shape it replaced is gone —
 * a test that only looked for the word `bindCommands` would pass against a build that called it on
 * every tick, which is the defect.</p>
 */

const PANEL = join(__dirname, '..', '..', 'src', 'panelView.ts');

/** Newlines normalised: this file is checked out CRLF on Windows and LF in CI. */
function panelSource(): string {
  return readFileSync(PANEL, 'utf8').replace(/\r\n/gu, '\n');
}

/** Every live region the panel patches, and nothing else claims to be one. */
const REGIONS = ['questions', 'rounds', 'consultations', 'notifications'];

test('each live region is re-bound exactly where its markup is replaced', () => {
  const source = panelSource();

  for (const region of REGIONS) {
    const replaced = `${region}.innerHTML = message.${region};`;
    assert.ok(source.includes(replaced), `${region} is no longer patched — this test is reading nothing`);
    assert.ok(
      source.includes(`${replaced}\n      bindCommands(${region});`),
      `${region} is patched without being re-bound in the same branch`,
    );
  }
});

test('nothing binds a live region from outside the branch that replaced it', () => {
  const source = panelSource();

  // The shape this replaced: a document-wide sweep on every message, whether anything changed or
  // not. Its absence is half the assertion — the pairing above is the other half.
  assert.ok(
    !source.includes("querySelectorAll('#live-"),
    'a region is being swept by selector again, which binds on ticks that changed nothing',
  );
  assert.equal(
    [...source.matchAll(/bindCommands\(/gu)].length,
    REGIONS.length + 2,
    'one binder, one call per region, one for the first paint, and one declaration',
  );
});
