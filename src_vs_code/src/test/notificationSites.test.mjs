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

  assert.equal(byApi, counted.direct, 'the per-API split must sum to the calls still made directly');
  assert.equal(
    counted.direct + counted.routed,
    counted.sites,
    'the population is what is routed plus what is not, and it must not shrink as work proceeds',
  );
  assert.equal(counted.events, counted.sites - counted.modal, 'events are the sites that are not modal questions');
  assert.ok(counted.sites > 0, 'a counter that finds nothing would pass every other assertion here');
});

/**
 * How many places this extension speaks to a person.
 *
 * <p>It started at 109 and is 111.</p>
 *
 * <ul>
 *   <li><b>110</b> — S3 SURFACES something never shown before: the server's own list of settings it
 *       could not understand, which crossed the wire on every probe and was dropped by the parser.
 *       A new message rather than a routed one.</li>
 *   <li><b>111</b> — the rebase onto main, 2026-09-17. Another lane shipped the copy
 *       acknowledgement's catch while this branch was routing, so main arrived carrying a message
 *       that had never been through the funnel. The ratchet went red on the rebase and named the
 *       file, which is the entire purpose of counting: a branch cannot quietly re-open the hole it
 *       is closing, and neither can the branch merging into it.</li>
 * </ul>
 *
 * <p>A new message is the only sanctioned way this number moves up, and each rise is written here
 * with its reason.</p>
 */
const PLACES_THIS_SPEAKS = 111;

test('the POPULATION changes only on purpose', () => {
  // Routing must not move it in either direction: the completeness promise is made over this
  // number, so it has to mean the same thing on the day the funnel lands as on the day the last
  // site is routed. An earlier version of the script made `sites` mean "still direct", and the
  // first routed modal quietly moved `events` from 93 to 89 — the population appearing to shrink
  // because the work was going well. Raising this constant is a deliberate act with a reason
  // beside it, which is what made the S3 addition visible instead of silent.
  assert.equal(count().sites, PLACES_THIS_SPEAKS, 'the number of places this extension speaks to a person');
});

/**
 * The ratchet. 109 call sites cannot be routed through the funnel in one commit, and a whitelist
 * with a hundred entries in it is a lie dressed as enforcement. So the number is allowed to FALL
 * and never to rise: a new direct call makes the drift test above red, and lowering this constant
 * is the only sanctioned way to change it.
 */
const MOST_DIRECT_CALLS_ALLOWED = 1;

test('no call site is added outside the funnel — the count only ever falls', () => {
  const counted = count();

  assert.ok(
    counted.direct <= MOST_DIRECT_CALLS_ALLOWED,
    `${counted.direct} direct calls, and the ratchet stands at ${MOST_DIRECT_CALLS_ALLOWED}. `
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

  assert.equal(perFile, counted.direct, 'the breakdown lists where the REMAINING direct calls are');
  assert.ok(
    Object.keys(counted.perFile).every((path) => !path.includes('/test/')),
    'the tests are not product call sites, and two of them assert on these very names',
  );
});
