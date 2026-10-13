// node --test scripts/lib/*.test.mjs — the Kimi probe's scratch folder must be private before the person's sign-in is
// copied into it (epic 2's code round, codex, security: a caller-supplied --scratch could be a folder another local user
// can read, and the copied refresh token would sit there until the scrub). Runs nothing of Kimi's; makes folders and links
// only under this user's own temp folder, and removes them. CI runs it on Linux (ci.yml, "Probe script tests"), where the
// two mode-bit cases run; on Windows they skip, because Windows has no mode bits and the profile's ACL is what it relies on.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, rmdirSync, statSync, symlinkSync, unlinkSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join, parse } from 'node:path';
import { after, test } from 'node:test';
import { copyHome, scratchRefusal } from './kimi.mjs';

const WINDOWS = process.platform === 'win32';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'kimi-scratch-test-'));
after(() => rmSync(base, { recursive: true, force: true }));

/** A junction on Windows (no privilege needed); a plain symlink on POSIX, where the type argument is ignored. */
const makeLink = (target, at) => symlinkSync(target, at, 'junction');

/** Removes the link and never its target: rmdir unlinks a junction on Windows; on POSIX rmdir refuses a symlink (ENOTDIR). */
const removeLink = (at) => (WINDOWS ? rmdirSync(at) : unlinkSync(at));

test('a fresh folder under this user\'s own temp folder is admitted', () => {
  assert.equal(scratchRefusal(join(base, 'kcp-new')), '');
  mkdirSync(join(base, 'kcp-made'), { mode: 0o700 });
  assert.equal(scratchRefusal(join(base, 'kcp-made')), '');
});

test('a folder outside the profile is refused, naming it', () => {
  const outside = join(parse(homedir()).root, 'coai-kimi-scratch-not-private');
  const why = scratchRefusal(outside);
  assert.match(why, /refusing --scratch/u);
  assert.ok(why.includes(outside), why);
});

test('a folder reached through a link is refused — even one whose target is private', () => {
  const target = join(base, 'target');
  mkdirSync(target);
  const link = join(base, 'link');
  makeLink(target, link);
  try {
    assert.match(scratchRefusal(link), /link/u);
    assert.match(scratchRefusal(join(link, 'kcp-under')), /link/u, 'a link anywhere on the way');
  } finally {
    removeLink(link);
  }
});

test('a link pointing OUT of the profile is refused', () => {
  const link = join(base, 'out');
  makeLink(parse(homedir()).root, link);
  try {
    assert.match(scratchRefusal(join(link, 'kcp')), /link/u);
  } finally {
    removeLink(link);
  }
});

test('a dangling link is refused, never treated as an absent folder', () => {
  const target = join(base, 'gone');
  mkdirSync(target);
  const link = join(base, 'dangling');
  makeLink(target, link);
  rmdirSync(target);
  try {
    assert.notEqual(scratchRefusal(link), '');
  } finally {
    removeLink(link);
  }
});

test('a copied home is created owner-only where the platform has modes', { skip: WINDOWS && 'Windows has no mode bits: the profile ACL is what keeps it private' }, () => {
  const real = join(base, 'real-home');
  mkdirSync(real);
  const home = join(base, 'copied-home');
  copyHome(real, home, { credentials: false, config: false });
  assert.equal(statSync(home).mode & 0o077, 0);
});

test('an existing folder other users can read is refused where the platform has modes', { skip: WINDOWS && 'Windows has no mode bits' }, () => {
  const open = join(base, 'open');
  mkdirSync(open, { mode: 0o755 });
  assert.match(scratchRefusal(open), /other users/u);
});
