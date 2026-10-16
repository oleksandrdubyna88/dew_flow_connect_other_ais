// node --test scripts/lib/*.test.mjs — the manifest script's per-platform write (DL3 of todo/PLAN_kimi_and_vibe_on_linux.md) and its
// inventory of a home that holds the .env sign-in link (DL2). Runs nothing of Vibe's; makes folders only under this user's own temp
// folder, and removes them.
import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { inventoryOf, platformOf, unstaged, withEntry } from './vibe-manifest.mjs';

const WINDOWS = process.platform === 'win32';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-vibe-manifest-test-'));
after(() => rmSync(base, { recursive: true, force: true }));

const WIN = { version: '2.26.1', platform: 'win32', pinned: [{ path: 'p', sha256: 'a'.repeat(64), bytes: 1 }] };
const OLD = { version: '2.25.0', pinned: [] }; // written before the field existed
const LINUX = { version: '2.26.1', platform: 'linux', pinned: [{ path: 'p', sha256: 'b'.repeat(64), bytes: 1 }] };

test('an entry with no platform is the win32 entry (DL3, the plan gate F6)', () => {
  assert.equal(platformOf(OLD), 'win32');
  assert.equal(platformOf(LINUX), 'linux');
});

test('a linux entry is written BESIDE the win32 entry of the same version, never over it', () => {
  const next = withEntry({ versions: [WIN, OLD] }, LINUX);
  assert.deepEqual(next.versions, [OLD, WIN, LINUX]);
  assert.deepEqual(withEntry(next, { ...WIN, measured: 'again' }).versions.filter((v) => platformOf(v) === 'win32' && v.version === '2.26.1'),
    [{ ...WIN, measured: 'again' }], 'the same version AND platform is replaced, once');
});

test('a staged entry goes to `staged` and leaves `versions` — what the shipped C# reads — exactly as it was', () => {
  const prior = { versions: [WIN] };
  const next = withEntry(prior, LINUX, { stage: true });
  assert.deepEqual(next.versions, [WIN]);
  assert.deepEqual(next.staged, [LINUX]);
  assert.deepEqual(prior, { versions: [WIN] }, 'the prior manifest is not mutated');
  const promoted = unstaged(next, 'linux');
  assert.deepEqual(promoted.versions, [WIN, LINUX]);
  assert.deepEqual(promoted.staged, []);
});

test('the inventory names a link and never reads through it — the person\'s .env is not hashed (an unreadable target proves it)', { skip: WINDOWS && 'a file symlink needs a privilege on Windows, and Windows homes hold no .env link' }, () => {
  const home = join(base, 'home');
  mkdirSync(join(home, 'logs'), { recursive: true });
  writeFileSync(join(home, 'logs', 'vibe.log'), 'abc');
  const secret = join(base, 'person.env');
  writeFileSync(secret, 'MISTRAL_API_KEY=sk-test-never-read\n');
  chmodSync(secret, 0o000);
  symlinkSync(secret, join(home, '.env'), 'file');
  try {
    const listed = inventoryOf(home);
    assert.deepEqual(listed.find((e) => e.path === '.env'), { path: '.env', link: true, sha256: '', bytes: 0 });
    assert.equal(listed.find((e) => e.path === 'logs/vibe.log').bytes, 3);
    assert.equal(listed.find((e) => e.path === 'logs/').sha256, '');
  } finally {
    chmodSync(secret, 0o600);
  }
});
