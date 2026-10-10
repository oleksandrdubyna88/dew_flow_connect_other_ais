import assert from 'node:assert/strict';
import { promises as fsp } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';

import { ROOT_ANSWER_LIFETIME_MS, type FilePresence } from '../pathFamily';
import { PROBE_DEADLINE_MS, PathProbes, statProbe } from '../pathProbes';

/**
 * The bounded probes (todo/PLAN_paths_per_side.md E5.1, the cadence consultant's amendment): every probe has a deadline
 * that answers `unknown`, at most one probe is outstanding per path however many renders and expiries pass, an unknown
 * answer is never kept as fresh, and a share that comes back clears the finding without any settings edit.
 */

/** A disk whose answers a test hands out by hand — a path with no answer yet is a probe that has not settled. */
function disk(): { readonly stat: (path: string) => Promise<FilePresence>; answer(path: string, said: FilePresence): void; asked(): readonly string[] } {
  const waiting = new Map<string, ((said: FilePresence) => void)[]>();
  const asked: string[] = [];

  return {
    stat: (path) => {
      asked.push(path);

      return new Promise((resolve) => { waiting.set(path, [...(waiting.get(path) ?? []), resolve]); });
    },
    answer: (path, said) => {
      for (const resolve of waiting.get(path) ?? []) {
        resolve(said);
      }
      waiting.set(path, []);
    },
    asked: () => asked,
  };
}

/** A clock and timers a test advances by hand. */
function clock(): { now(): number; later(work: () => void, ms: number): void; advance(ms: number): void } {
  let at = 1_000_000;
  let timers: { readonly due: number; readonly work: () => void }[] = [];

  return {
    now: () => at,
    later: (work, ms) => { timers = [...timers, { due: at + ms, work }]; },
    advance: (ms) => {
      at += ms;
      const due = timers.filter((one) => one.due <= at);
      timers = timers.filter((one) => one.due > at);
      for (const one of due) {
        one.work();
      }
    },
  };
}

const tick = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });

test('a never-settling share beside a missing local folder: the local finding publishes, and the share stays ONE probe however many renders and expiries pass', async () => {
  const d = disk();
  const c = clock();
  const probes = new PathProbes(d.stat, { now: c.now, later: c.later });

  // Six renders, each asking about both paths, with the answer lifetime passing twice in between.
  for (let render = 0; render < 6; render += 1) {
    probes.presence('//nas/share', 'folder');
    probes.presence('D:\\old-project', 'folder');
    c.advance(ROOT_ANSWER_LIFETIME_MS + 1);
  }
  d.answer('D:\\old-project', 'absent');
  await tick();

  assert.equal(probes.presence('D:\\old-project', 'folder'), 'absent', 'the local folder\'s answer was held behind the stalled share');
  assert.equal(probes.presence('//nas/share', 'folder'), 'pending', 'a probe that has not answered is not an answer');
  assert.equal(d.asked().filter((one) => one === '//nas/share').length, 1, 'the share was asked more than once while its first probe was still running');
  assert.equal(probes.outstandingProbes(), 1, 'only the share is still being asked');
});

test('settled() answers unknown at the deadline and records it; the probe stays the one outstanding until it really settles, and then its answer replaces the unknown', async () => {
  const d = disk();
  const c = clock();
  const probes = new PathProbes(d.stat, { now: c.now, later: c.later });
  let changes = 0;
  probes.onChange(() => { changes += 1; });

  const waited = probes.settled('/mnt/z/coai', 'folder');
  c.advance(PROBE_DEADLINE_MS);
  assert.equal(await waited, 'unknown', 'a probe past its deadline is answered unknown, never waited for');
  assert.equal(probes.presence('/mnt/z/coai', 'folder'), 'unknown', 'the deadline\'s answer is what the page reads meanwhile');
  assert.equal(probes.outstandingProbes(), 1, 'the stalled probe was dropped, so a second one could start beside it');
  assert.equal(d.asked().length, 1);

  // The share comes back: the probe settles, the real answer replaces the unknown, listeners are told — no settings edit.
  d.answer('/mnt/z/coai', 'here');
  await tick();
  assert.equal(probes.presence('/mnt/z/coai', 'folder'), 'here', 'the recovery did not clear the unknown');
  assert.equal(probes.outstandingProbes(), 0);
  assert.equal(changes, 2, 'unknown, then here');
});

test('an unknown answer is never fresh — the next read asks again, bounded to one probe — and a definitive one lives the lifetime', async () => {
  const d = disk();
  const c = clock();
  const probes = new PathProbes(d.stat, { now: c.now, later: c.later });

  probes.presence('/srv/x', 'folder');
  d.answer('/srv/x', 'unknown');
  await tick();
  assert.equal(probes.presence('/srv/x', 'folder'), 'unknown');
  assert.equal(d.asked().length, 2, 'an unknown answer was kept as if the disk had spoken');
  probes.presence('/srv/x', 'folder');
  assert.equal(d.asked().length, 2, 'a second probe started while the re-ask was outstanding');

  d.answer('/srv/x', 'absent');
  await tick();
  for (let render = 0; render < 5; render += 1) {
    assert.equal(probes.presence('/srv/x', 'folder'), 'absent');
  }
  assert.equal(d.asked().length, 2, 'a fresh definitive answer was asked again on a repaint');
  c.advance(ROOT_ANSWER_LIFETIME_MS + 1);
  assert.equal(probes.presence('/srv/x', 'folder'), 'absent', 'the last answer stands while the refresh runs');
  assert.equal(d.asked().length, 3, 'after the lifetime the disk is asked once more');
});

test('forget() drops what was said, so a setting change asks again — and a probe already running is not started twice', async () => {
  const d = disk();
  const c = clock();
  const probes = new PathProbes(d.stat, { now: c.now, later: c.later });

  probes.presence('D:\\a', 'folder');
  d.answer('D:\\a', 'here');
  await tick();
  probes.presence('D:\\b', 'folder');
  probes.forget();

  assert.equal(probes.presence('D:\\a', 'folder'), 'pending', 'a forgotten answer was still served');
  assert.equal(d.asked().filter((one) => one === 'D:\\a').length, 2);
  probes.presence('D:\\b', 'folder');
  assert.equal(d.asked().filter((one) => one === 'D:\\b').length, 1, 'forget() started a second probe beside the running one');
});

test('settled() on a fresh definitive answer waits for nothing, and a rejected stat is unknown rather than a throw', async () => {
  const d = disk();
  const c = clock();
  const probes = new PathProbes(d.stat, { now: c.now, later: c.later });
  probes.presence('D:\\a', 'folder');
  d.answer('D:\\a', 'absent');
  await tick();

  assert.equal(await probes.settled('D:\\a', 'folder'), 'absent');
  assert.equal(d.asked().length, 1);

  const refusing = new PathProbes(() => Promise.reject(new Error('EIO')), { now: c.now, later: c.later });
  assert.equal(await refusing.settled('D:\\z', 'folder'), 'unknown');
});

test('the real stat answers here for a folder asked as a folder, absent for it asked as a file, absent for nothing, unknown for a disk that would not say', async () => {
  const dir = await fsp.mkdtemp(path.join(os.tmpdir(), 'coai-probes-'));
  try {
    const file = path.join(dir, 'a.txt');
    await fsp.writeFile(file, 'x', 'utf8');
    const probe = statProbe((one) => fsp.stat(one));

    assert.equal(await probe(dir, 'folder'), 'here');
    assert.equal(await probe(dir, 'file'), 'absent', 'a folder is no CLI');
    assert.equal(await probe(file, 'file'), 'here');
    assert.equal(await probe(file, 'folder'), 'absent', 'a file is no folder');
    assert.equal(await probe(path.join(dir, 'gone'), 'folder'), 'absent');
    assert.equal(await probe(path.join(file, 'under-a-file'), 'folder'), 'absent', 'ENOTDIR is an absence');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }

  const denied = statProbe(() => Promise.reject(Object.assign(new Error('EACCES'), { code: 'EACCES' })));
  assert.equal(await denied('/root/x', 'folder'), 'unknown', 'a disk that would not say is not an absence');
});
