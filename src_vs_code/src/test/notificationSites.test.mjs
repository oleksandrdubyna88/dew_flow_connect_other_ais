import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { INVENTORY, asText, count } from '../../scripts/count-notifications.mjs';

/**
 * The published count of places this extension speaks to a person cannot drift.
 *
 * <p><b>Why this test exists, precisely.</b> The notifications plan promises that every message is
 * written down, and quotes a number in three documents. Its first draft published 109 call sites, a
 * class table summing to 113, and "95 events" — which is neither 109 − 16 nor 113 − 16 but the sum
 * of the table's first four rows. Every test was green, because no test knew what the number was.
 * The plan round found it by hand; this makes finding it by hand unnecessary.</p>
 *
 * <p>It runs BEFORE the compile, beside `prepareGate` and `claudeAdapter`, because it needs no
 * build: the script and this test are both ESM and read the source tree directly.</p>
 */

test('the checked-in inventory is what the counter counts today', () => {
  const counted = count();
  const onDisk = readFileSync(INVENTORY, 'utf8');

  assert.equal(
    asText(counted),
    onDisk,
    'run `node scripts/count-notifications.mjs --write` — a call site was added, moved or removed',
  );
});

test('the count is internally consistent, which the hand-written one was not', () => {
  const counted = count();
  const byApi = Object.values(counted.byApi).reduce((total, n) => total + n, 0);

  assert.equal(byApi, counted.sites, 'the per-API split must sum to the total');
  assert.equal(counted.events, counted.sites - counted.modal, 'events are the sites that are not modal questions');
  assert.ok(counted.sites > 0, 'a counter that finds nothing would pass every other assertion here');
});

/**
 * The ratchet. 109 call sites cannot be routed through the funnel in one commit, and a whitelist
 * with a hundred entries in it is a lie dressed as enforcement. So the number is allowed to FALL
 * and never to rise: a new direct call makes the drift test above red, and lowering this constant
 * is the only sanctioned way to change it.
 */
const MOST_DIRECT_CALLS_ALLOWED = 105;

test('no call site is added outside the funnel — the count only ever falls', () => {
  const counted = count();

  assert.ok(
    counted.sites <= MOST_DIRECT_CALLS_ALLOWED,
    `${counted.sites} direct calls, and the ratchet stands at ${MOST_DIRECT_CALLS_ALLOWED}. `
    + 'A new message goes through notify() — see notify.ts.',
  );
});

test('and the scan still finds a call it is SUPPOSED to find', () => {
  // The companion `testing.md` asks for beside every prohibition: a scan that matches nothing
  // passes for ever. The funnel calls the API on purpose, so if this reaches zero the scan has
  // stopped working rather than the product having stopped showing messages.
  assert.ok(count().inTheFunnel > 0, 'the scan no longer matches the funnel itself');
});

test('the per-file breakdown accounts for every site', () => {
  const counted = count();
  const perFile = Object.values(counted.perFile).reduce((total, n) => total + n, 0);

  assert.equal(perFile, counted.sites);
  assert.ok(
    Object.keys(counted.perFile).every((path) => !path.includes('/test/')),
    'the tests are not product call sites, and two of them assert on these very names',
  );
});
