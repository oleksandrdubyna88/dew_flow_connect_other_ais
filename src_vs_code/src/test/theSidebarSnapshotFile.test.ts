import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { mkdirSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { panelHtml } from '../panelView';
import {
  extensionVersionOf, NONCE_PLACEHOLDER, readSnapshot, SNAPSHOT_FILE, SNAPSHOT_MAX_BYTES, snapshotText,
} from '../sidebarSnapshot';
import { SidebarSnapshotWriter } from '../sidebarSnapshotStore';
import { ownedTemp } from './ownedTemp';
import { panelState } from './panelPageHarness';

/**
 * The sidebar's last snapshot: the file the old code writes and the reader that refuses what may not be replayed (v2 E4, story s3-sidebar).
 *
 * <p>Since 2026-11-23 the sidebar module draws its real menu and does not replay a snapshot (`theSidebarDrawsItsRealMenu.test.ts`): the writer and the
 * reader are kept, and held to their own rules here, until the owner decides whether the file goes.</p>
 */

const VERSION = '9.9.9-test';

const WITH_PHRASE = panelState('bugz', { phrases: [{ id: 'p1', name: 'Deploy the snapshot', text: 'deploy it' }] });

test('a snapshot that may not be replayed is no snapshot: damaged, from another build, over the cap, credential-like, or with a stray placeholder', () => {
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
  const control = ownedTemp('coai-sidebar-snapshot');
  writeFileSync(join(control, SNAPSHOT_FILE), snapshotText(VERSION, good) ?? '');
  assert.equal(readSnapshot(control, VERSION), good, 'the same reader replays a good file (without this the refusals below could be a broken fixture)');
  for (const [what, plant] of cases) {
    const dir = ownedTemp('coai-sidebar-snapshot');
    plant(dir);

    assert.equal(readSnapshot(dir, VERSION), undefined, `${what}: the reader must refuse it`);
  }
});

/* ---------------------------------------------------------------------------------------------- *
 * The writer, and the whole way round: what the old code stores is what the reader replays.
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

