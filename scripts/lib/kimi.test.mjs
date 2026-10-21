// node --test scripts/lib/*.test.mjs — the Kimi probe's scratch folder must be private before the person's sign-in is
// copied into it (epic 2's code round, codex, security: a caller-supplied --scratch could be a folder another local user
// can read, and the copied refresh token would sit there until the scrub). Runs nothing of Kimi's; makes folders and links
// only under this user's own temp folder, and removes them. CI runs it on Linux (ci.yml, "Probe script tests"), where the
// two mode-bit cases run; on Windows they skip, because Windows has no mode bits and the profile's ACL is what it relies on.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, rmdirSync, statSync, symlinkSync, unlinkSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join, parse } from 'node:path';
import { after, test } from 'node:test';
import { CredentialGuard, CredentialWatch, launchesOnCopiedCredentials, canaryCommand, copiedCredentialsRefusal, copyHome, credentialFingerprint, credentialShape, redactSlot, homeBin, isCredentialPath, kimiExecutable, manifest, manifestDiff, parseStreamJson, scratchRefusal, scrubHome, withoutTrailingSeparators } from './kimi.mjs';

const WINDOWS = process.platform === 'win32';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-kimi-scratch-test-'));
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

// SonarCloud S8786 on #803: an end-anchored separator run backtracked quadratically; the loop is linear and says the same.
test('trailing separators go, in linear time on a pathological run', () => {
  assert.equal(withoutTrailingSeparators('C:/a/b//'), 'C:/a/b');
  assert.equal(withoutTrailingSeparators('C:\\a\\b\\\\'), 'C:\\a\\b');
  assert.equal(withoutTrailingSeparators('/'), '');
  assert.equal(withoutTrailingSeparators('a'), 'a');
  const evil = `${'/'.repeat(200_000)}x`;
  const started = performance.now();
  assert.equal(withoutTrailingSeparators(evil), evil);
  assert.ok(performance.now() - started < 200, 'a run of separators that does not end the path is one pass');
});

/** A home whose credentials/<name>.json carries this refresh token. */
function homeWith(name, refresh, access = 'a') {
  const home = mkdtempSync(join(base, `${name}-`));
  mkdirSync(join(home, 'credentials'));
  writeFileSync(join(home, 'credentials', 'kimi.json'), JSON.stringify({ refresh_token: refresh, access_token: access, expires_at: 1 }));
  return home;
}
const refreshIn = (home) => JSON.parse(readFileSync(join(home, 'credentials', 'kimi.json'), 'utf8')).refresh_token;

// SonarCloud S3403 on #803 said `rotates !== false` is always true. It is not: `rotates` is null until the real home's
// first refresh, then whatever that refresh showed. All three states, and what each does to a copy that refreshed.
test('the credential guard: a copy that refreshed is written back unless the real home showed no rotation', () => {
  const unknown = homeWith('real-unknown', 'R1');
  const guardUnknown = new CredentialGuard(unknown);
  assert.equal(guardUnknown.rotates, null);
  assert.equal(guardUnknown.afterCopiedRun(homeWith('copy-unknown', 'R2'), 'u'), true, 'unknown counts as rotating: written back');
  assert.equal(refreshIn(unknown), 'R2');

  const rotating = homeWith('real-rotating', 'R1');
  const guardRotating = new CredentialGuard(rotating);
  writeFileSync(join(rotating, 'credentials', 'kimi.json'), JSON.stringify({ refresh_token: 'R3', access_token: 'b', expires_at: 2 }));
  assert.equal(guardRotating.afterRealRun('r'), true);
  assert.equal(guardRotating.rotates, true);
  assert.equal(guardRotating.afterCopiedRun(homeWith('copy-rotating', 'R4'), 'c'), true);
  assert.equal(refreshIn(rotating), 'R4');

  const steady = homeWith('real-steady', 'R1');
  const guardSteady = new CredentialGuard(steady);
  writeFileSync(join(steady, 'credentials', 'kimi.json'), JSON.stringify({ refresh_token: 'R1', access_token: 'b', expires_at: 2 }));
  assert.equal(guardSteady.afterRealRun('r'), true);
  assert.equal(guardSteady.rotates, false, 'the real home refreshed and kept its refresh token');
  assert.equal(guardSteady.afterCopiedRun(homeWith('copy-steady', 'R5'), 'c'), false, 'no rotation: nothing written back');
  assert.equal(refreshIn(steady), 'R1');
});

// ------------------------------------------------------------------------------------------------- Linux (plan §5, E1)

test('kimi is found where Moonshot\'s installer puts it, per platform (DL5: <KIMI_INSTALL_DIR or ~/.kimi-code>/bin/kimi)', () => {
  assert.equal(kimiExecutable('linux', {}, '/home/u'), '/home/u/.kimi-code/bin/kimi');
  assert.equal(kimiExecutable('linux', { KIMI_INSTALL_DIR: '/opt/kimi' }, '/home/u'), '/opt/kimi/bin/kimi');
  assert.equal(kimiExecutable('win32', {}, 'C:\\Users\\u'), 'C:\\Users\\u\\.kimi-code\\bin\\kimi.exe');
});

test('the bundled tools a copied home takes are the platform\'s: rg/fd on Linux, rg.exe/fd.exe on Windows', () => {
  assert.deepEqual(homeBin('linux'), ['rg', 'fd']);
  assert.deepEqual(homeBin('win32'), ['rg.exe', 'fd.exe']);
});

test('a canary hook runs the platform\'s Python by its bare name: python3 on Linux (Ubuntu has no bare `python`)', () => {
  assert.match(canaryCommand('/tmp/c.txt', 'linux'), /^python3 -c "/u);
  assert.match(canaryCommand('C:\\t\\c.txt', 'win32'), /^python -c "/u);
  assert.ok(canaryCommand('C:\\t\\c.txt', 'win32').endsWith('"C:/t/c.txt"'), 'the path still arrives as argv, forward slashes');
});

test('the scrub removes a credentials SYMLINK by its own name on Linux and never what it points at', { skip: WINDOWS && 'the Windows junction arm is the existing M-K8b path' }, () => {
  const person = join(base, 'person-creds');
  mkdirSync(join(person, 'credentials'), { recursive: true });
  writeFileSync(join(person, 'credentials', 'kimi.json'), '{"refresh_token":"keep"}');
  const home = join(base, 'copied-with-link');
  mkdirSync(home);
  symlinkSync(join(person, 'credentials'), join(home, 'credentials'), 'dir');
  const gone = scrubHome(home);
  assert.deepEqual(gone, ['credentials (link)']);
  assert.equal(readFileSync(join(person, 'credentials', 'kimi.json'), 'utf8'), '{"refresh_token":"keep"}');
});

// SonarCloud S3776 on #803: parseStreamJson was split at a cognitive complexity of 20; what it reads must not move.
test('stream-json: the LAST non-blank assistant text, the LAST resume hint, the lines parsed, each role counted', () => {
  const stream = [
    JSON.stringify({ role: 'assistant', content: 'first' }),
    '',
    'not json',
    JSON.stringify({ role: 'assistant', content: [{ type: 'text', text: 'sec' }, 'ond'] }),
    JSON.stringify({ role: 'assistant', content: '   ' }),
    JSON.stringify({ role: 'meta', type: 'session.resume_hint', session_id: 's-1' }),
    JSON.stringify({ role: 'meta', type: 'other', session_id: 'ignored' }),
    JSON.stringify({ role: 'meta', type: 'session.resume_hint', session_id: 's-2' }),
    JSON.stringify({ content: 'no role' }),
    JSON.stringify({ role: 'assistant', content: { not: 'text' } }),
  ].join('\n');

  assert.deepEqual(parseStreamJson(stream), {
    answer: 'second', sessionId: 's-2', lines: 8, roles: { assistant: 4, meta: 3, '?': 1 },
  });
  assert.deepEqual(parseStreamJson(undefined), { answer: '', sessionId: '', lines: 0, roles: {} });
});

// ------------------------------------------------------------------------------------------------- the credential-safe manifest (S-N2)
// DN5 item 2 of todo/PLAN_kimi_and_vibe_on_native_linux.md (gate finding 0): the confinement probe records the person's home around every launch,
// and hashing a file READS its bytes — for credentials/*.json that is a refresh token. The folder is recorded by lstat only; the hash function is
// injected so the test can prove it is never called for a path under it. Nothing here runs Kimi; the "secret" is a made-up word in a temp file.

/** A fake ~/.kimi-code: a config, a session file, and credentials/ holding the named files. */
function fakeKimiHome(name, credentialFiles) {
  const home = mkdtempSync(join(base, `${name}-`));
  mkdirSync(join(home, 'credentials'));
  mkdirSync(join(home, 'sessions'));
  writeFileSync(join(home, 'config.toml'), 'default_model = "k3"\n');
  writeFileSync(join(home, 'sessions', 'index.jsonl'), '{}\n');
  for (const f of credentialFiles) writeFileSync(join(home, 'credentials', f), '{"refresh_token":"SECRET-WORD"}');
  return home;
}

test('S-N2: manifest never calls the hash function for a path under credentials/, and records the folder by lstat only', () => {
  const home = fakeKimiHome('mf', ['kimi-code-env-x.json']);
  const hashed = [];
  const hash = (path) => { hashed.push(path.replaceAll('\\', '/')); return 'HASH'; };

  const m = manifest(home, { hash });

  assert.equal(hashed.some((p) => p.includes('/credentials/')), false, "manifest() hashed credentials/kimi-code-env-x.json — a credential file's bytes were read");
  assert.deepEqual(hashed.map((p) => p.slice(p.lastIndexOf('/') + 1)).sort(), ['config.toml', 'index.jsonl'], 'the other files are still hashed — the teeth');
  assert.equal(m['config.toml'], 'HASH');
  assert.match(m['credentials/kimi-code-env-x.json'], /^lstat:\d+:\d+:\d+$/u);
  assert.equal(JSON.stringify(m).includes('SECRET-WORD'), false);
});

test('S-N2: a credential slot\'s hex is redacted from the recorded name, and a refresh (a new mtime or size) still shows as a change', () => {
  const home = fakeKimiHome('mf-hex', ['kimi-code-env-0123456789abcdef.json']);
  const file = join(home, 'credentials', 'kimi-code-env-0123456789abcdef.json');
  const before = manifest(home, { hash: () => 'H' });
  assert.deepEqual(Object.keys(before).filter((k) => k.startsWith('credentials/')), ['credentials/kimi-code-env-<hex>.json']);

  writeFileSync(file, '{"refresh_token":"SECRET-WORD-ROTATED-AND-LONGER"}');
  const after = manifest(home, { hash: () => 'H' });

  assert.deepEqual(manifestDiff(before, after), [{ path: 'credentials/kimi-code-env-<hex>.json', change: 'changed' }]);
});

test('S-N2: `skip` makes more paths lstat-only without ever un-protecting credentials/, and credentials/ itself (the owned home link) counts as one', () => {
  const home = fakeKimiHome('mf-skip', ['kimi-code-env-y.json']);
  const hashed = [];
  const hash = (path) => { hashed.push(path.replaceAll('\\', '/')); return 'HASH'; };

  const m = manifest(home, { skip: (rel) => rel.startsWith('sessions/'), hash });

  assert.equal(hashed.some((p) => p.includes('/credentials/') || p.includes('/sessions/')), false);
  assert.match(m['sessions/index.jsonl'], /^lstat:/u);
  assert.match(m['credentials/kimi-code-env-y.json'], /^lstat:/u, 'a skip that does not name credentials/ un-protected it');
  assert.equal(isCredentialPath('credentials'), true, 'the link itself');
  assert.equal(isCredentialPath('credentials/kimi-code.json'), true);
  assert.equal(isCredentialPath('credentials-old/x'), false);
  assert.equal(isCredentialPath('config.toml'), false);
});

// ------------------------------------------------------------------------------------------------- the sign-in watch: shape for owned launches (J1)
// The Opus judge's blocker on E1: the probe built `new CredentialGuard(REAL_HOME)` at start, and its constructor parses every credentials/*.json and
// hashes the refresh token — then again after EVERY owned launch. An owned launch now watches the sign-in by lstat SHAPE; the content-reading guard is
// built only when a copied-credentials (legacy) cell runs. `read` is injected, so S-N2's twin proves when the reader is called.

test('S-N2 twin: an owned-only run never calls the credential reader for the real home, yet still sees the sign-in move', () => {
  const real = fakeKimiHome('watch-owned', ['kimi-code-env-x.json']);
  const opened = [];
  const watch = new CredentialWatch(real, { read: (home) => { opened.push(home); return {}; } });

  assert.equal(watch.afterRealRun('quiet'), false, 'nothing moved');
  writeFileSync(join(real, 'credentials', 'kimi-code-env-x.json'), '{"refresh_token":"SECRET-WORD-ROTATED-AND-LONGER"}');
  assert.equal(watch.afterRealRun('refreshed'), true, 'a rewritten credential file is seen by its shape');

  assert.deepEqual(opened, [], 'the credential reader was called for the real home — a refresh token was read by an owned launch');
  assert.equal(watch.rotates, null, 'rotation is unknown without reading, and says so');
  assert.deepEqual(watch.events, [{ tag: 'refreshed', where: 'real', refreshed: true, rotated: null }]);
  assert.equal(JSON.stringify(watch.events).includes('SECRET-WORD'), false);
});

test('the content guard is built lazily by content(), once, and a built guard also hears the real-home runs', () => {
  const real = homeWith('watch-lazy', 'R1');
  const opened = [];
  const read = (home) => { opened.push(home); return credentialFingerprint(home); };
  const watch = new CredentialWatch(real, { read });
  assert.equal(opened.length, 0);

  const guard = watch.content();
  assert.equal(opened.length, 1, 'built on demand: one read of the real home');
  assert.equal(watch.content(), guard, 'the same guard on a second call');

  writeFileSync(join(real, 'credentials', 'kimi.json'), JSON.stringify({ refresh_token: 'R2', access_token: 'b', expires_at: 2 }));
  assert.equal(watch.afterRealRun('r'), true);
  assert.equal(watch.rotates, true, 'with the guard built, a real-home refresh still teaches whether the token rotates');
});

test('a cell that would copy the person\'s sign-in is refused unless --allow-copied-credentials was passed', () => {
  assert.match(copiedCredentialsRefusal(false), /--allow-copied-credentials/u);
  assert.equal(copiedCredentialsRefusal(true), '');
});

test('credentialShape lists names with inode, size and mtime, opens nothing, and is empty for a home with no credentials/', () => {
  const home = fakeKimiHome('shape', ['b.json', 'a.json']);
  writeFileSync(join(home, 'credentials', 'note.txt'), 'not a credential file');
  const s = credentialShape(home);
  assert.deepEqual(Object.keys(s), ['a.json', 'b.json']);
  assert.match(s['a.json'], /^\d+:\d+:\d+$/u);
  assert.deepEqual(credentialShape(join(base, 'no-such-home')), {});
});

// J8: one slot-hex redaction for the manifest, the survivor markers and the refusal text (it was spelled three times).
test('J8: redactSlot turns a run of 8 or more hex digits into <hex> and leaves shorter runs and words alone', () => {
  assert.equal(redactSlot('kimi-code-env-0123456789abcdef.json'), 'kimi-code-env-<hex>.json');
  assert.equal(redactSlot('0123ABCD.survivors.json'), '<hex>.survivors.json');
  assert.equal(redactSlot('kimi-code-env-x.json'), 'kimi-code-env-x.json');
  assert.equal(redactSlot('abc1234.json'), 'abc1234.json', 'seven digits is not a slot');
});

// Re-check of J1 (the Opus judge, 2026-10-21): `copiedCredentials` was "has a home, not shared, not owned", so M-K7's empty-home and no-credentials arms - homes
// `copiedHome` never put a credential in - built the content-reading guard WITHOUT the flag, and every later owned launch re-read the sign-in. Only a home the probe
// actually filled with credentials is a copied-credentials launch.
test('re-check J1: M-K7 without --allow-copied-credentials never builds the content guard - the injected reader is not called for any launch of the cell', () => {
  const real = fakeKimiHome('watch-mk7', ['kimi-code-env-x.json']);
  const opened = [];
  const watch = new CredentialWatch(real, { read: (home) => { opened.push(home); return {}; } });
  const filled = new Set(); // copiedHome refused to fill any: the flag was not passed
  const m7 = [{ home: join(base, 'h1', 'home') }, { home: join(base, 'h2', 'home') }, { home: join(base, 'h3', 'home'), owned: true }, {}]; // empty, no-credentials, owned, real home

  for (const spec of m7) {
    if (launchesOnCopiedCredentials(spec, filled)) watch.content();
    watch.afterRealRun('m7');
  }

  assert.deepEqual(opened, [], 'an M-K7 arm built the content guard - every later launch re-read the person\'s credentials');
});

test('re-check J1: a home the probe filled with credentials is a copied-credentials launch, and only that one', () => {
  const filled = new Set([join(base, 'filled', 'home')]);

  assert.equal(launchesOnCopiedCredentials({ home: join(base, 'filled', 'home') }, filled), true);
  assert.equal(launchesOnCopiedCredentials({ home: join(base, 'other', 'home') }, filled), false);
  assert.equal(launchesOnCopiedCredentials({}, filled), false, 'no home: the real one, watched by shape');
});
