// node --test scripts/lib/vibe.test.mjs — the manifest script's listing and classifier (D4, C8 of
// research/PLAN_vibe_live_round_fixes.md). Runs nothing of Vibe's; makes folders only under this user's own temp folder, and
// removes them. The rules below are the script's own constants, so a rule the C# side holds and the script forgot fails here.
import assert from 'node:assert/strict';
import { chmodSync, existsSync, lstatSync, mkdirSync, mkdtempSync, readFileSync, readlinkSync, realpathSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, test } from 'node:test';
import {
  MINIMAL_UNIX, MINIMAL_WINDOWS, builtEnv, classOf, deepestChild, dirLink, files, homeListing, linkSignIn, minimalEnv, personsVibeHome,
  KEYRING_FAIL_BACKEND, armFilterOf, armsCellsError, armWanted, keyringArm, keyringSetup, pythonCommand, removeDirLink, run, vibePython, writeProbeKeyring,
} from './vibe.mjs';

const WINDOWS = process.platform === 'win32';
const POSIX_ONLY = WINDOWS && 'a POSIX file or process semantic — it runs on the Linux CI job and in WSL';
const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-vh-vibelib-'));
after(() => rmSync(base, { recursive: true, force: true }));

const RULES = {
  pinnedPrefix: 'logs/session/plugins/',
  varying: ['trusted_folders.toml', 'vibehistory', 'logs/vibe.log', 'logs/session/plugins/tmp/**', 'logs/session/unified/*/**'],
  varyingFolders: ['logs/session/plugins/data/**'],
};

const SESSION = '4f94d59a-afc4-fe1d-2354-ff6c5e3f799d';
const CHUNK = `logs/session/unified/${SESSION}/chunks/${'a'.repeat(64)}.json`;
const LISTING = [
  'vibehistory',
  CHUNK,
  `logs/session/unified/${SESSION}/CURRENT`,
  `logs/session/plugins/blobs/60/${'6'.repeat(64)}`,
  'logs/session/plugins/data/193149ad-d8e5-069e-8f34-aece24b2623d/vibe/',
  'logs/session/plugins/tmp/',
];

test('files() lists an empty folder as a/b/ only when asked for folders, and never lists one otherwise', () => {
  const dir = join(base, 'listing');
  mkdirSync(join(dir, 'a', 'b'), { recursive: true });
  mkdirSync(join(dir, 'a', 'full'), { recursive: true });
  writeFileSync(join(dir, 'a', 'full', 'f.txt'), 'x');

  assert.deepEqual(files(dir), ['a/full/f.txt']);
  assert.deepEqual(files(dir, { folders: true }), ['a/', 'a/b/', 'a/full/', 'a/full/f.txt']);
});

test('the classifier judges varying and varyingFolders before the pinned prefix, as the C# rules do', () => {
  const at = (path) => classOf(path, RULES, LISTING);

  assert.equal(at('logs/session/plugins/data/x/vibe/'), 'admitted-folder');
  assert.equal(at('logs/session/plugins/newthing/'), 'unknown', 'a folder under the pinned prefix that leads to no pinned file');
  assert.equal(at('logs/session/plugins/blobs/60/'), 'pinned-lead');
  assert.equal(at(`logs/session/plugins/blobs/60/${'6'.repeat(64)}`), 'pinned');
  assert.equal(at('logs/session/plugins/tmp/a'), 'varies', 'the staging folder lies under the prefix and is admitted by name');
  assert.equal(at('logs/session/plugins/data/x/vibe/state.json'), 'unknown', 'a FILE in a plugin\'s per-session data stops the script');
  assert.equal(at('logs/session/unified/'), 'varies-lead');
  assert.equal(at('hooks.toml'), 'unknown');
});

test('deepestChild is the longest persisted file path plus Vibe\'s 22-character atomic-write wrapper', () => {
  assert.equal(CHUNK.length, 134);
  assert.deepEqual(deepestChild(LISTING), { deepestChild: 156, deepestPersisted: CHUNK.replace(SESSION, '<id>').replace('a'.repeat(64), '<64 hex>') });
});

// ------------------------------------------------------------------------------------------------- Linux (plan §5, E1)

/** The names of one `private static readonly string[] <name> = [ … ];` in ProcessEnvironment.cs — the C# source of truth. */
function csharpList(name) {
  // reads-another-program: ProcessEnvironment.cs is the product's list; this script's copy must equal it (enumerate, never retype).
  const source = readFileSync(join(repo, 'src_mcp', 'runners', 'Processes', 'ProcessEnvironment.cs'), 'utf8');
  const at = source.indexOf(`string[] ${name} =`);
  assert.ok(at >= 0, `ProcessEnvironment.cs no longer declares ${name} — the parse would compare nothing`);
  const open = source.indexOf('[', at + `string[] ${name} =`.length);
  const close = source.indexOf('];', open);
  return [...source.slice(open, close).matchAll(/"([^"]+)"/gu)].map((m) => m[1]);
}

test('MINIMAL_UNIX is the product\'s MinimalUnix, name for name — and the same parse still finds MinimalWindows', () => {
  assert.deepEqual([...MINIMAL_UNIX].sort(), csharpList('MinimalUnix').sort());
  assert.deepEqual([...MINIMAL_WINDOWS].sort(), csharpList('MinimalWindows').sort(), 'the parse must still find a known list, or a broken parse passes');
});

test('the minimal environment is per platform: Windows matches names case-insensitively, Linux takes exact names only', () => {
  const env = { Path: 'C:\\w', SystemRoot: 'C:\\Windows', HOME: '/home/u', PATH: '/usr/bin', LANG: 'C.UTF-8', SECRET: 'x', path: '/lower' };
  const linux = minimalEnv('linux', env);
  assert.deepEqual(linux, { PATH: '/usr/bin', HOME: '/home/u', LANG: 'C.UTF-8' });
  assert.ok(Object.keys(linux).every((name) => MINIMAL_UNIX.includes(name)));
  const windows = minimalEnv('win32', { Path: 'C:\\w', SystemRoot: 'C:\\Windows', SECRET: 'x' });
  assert.deepEqual(windows, { PATH: 'C:\\w', SystemRoot: 'C:\\Windows' });
});

test('the built environment on Linux is MinimalUnix + VIBE_HOME + the fixed pins + the model — no Windows name', () => {
  const env = builtEnv('/tmp/h', 'mistral-medium-3.5', 'linux', { PATH: '/usr/bin', HOME: '/home/u', APPDATA: 'nope', MISTRAL_API_KEY: 'never' });
  assert.deepEqual(env, { PATH: '/usr/bin', HOME: '/home/u', VIBE_HOME: '/tmp/h', VIBE_CLI: 'python', PYTHONUTF8: '1', VIBE_ACTIVE_MODEL: 'mistral-medium-3.5' });
});

test('Vibe\'s own Python is found where uv puts it, per platform', () => {
  assert.equal(vibePython('linux', {}, '/home/u'), '/home/u/.local/share/uv/tools/mistral-vibe/bin/python');
  assert.equal(vibePython('linux', { XDG_DATA_HOME: '/data' }, '/home/u'), '/data/uv/tools/mistral-vibe/bin/python');
  assert.equal(vibePython('linux', { UV_TOOL_DIR: '/opt/uvt', XDG_DATA_HOME: '/data' }, '/home/u'), '/opt/uvt/mistral-vibe/bin/python');
  assert.equal(vibePython('win32', { APPDATA: 'C:\\Users\\u\\AppData\\Roaming' }, 'C:\\Users\\u'),
    'C:\\Users\\u\\AppData\\Roaming\\uv\\tools\\mistral-vibe\\Scripts\\python.exe');
  assert.equal(pythonCommand('linux'), 'python3', 'Ubuntu has no bare `python` unless python-is-python3 is installed');
  assert.equal(pythonCommand('win32'), 'python');
});

test('the person\'s Vibe home is VIBE_HOME of this environment when set, else ~/.vibe (DL2)', () => {
  assert.equal(personsVibeHome({}, '/home/u', 'linux'), '/home/u/.vibe');
  assert.equal(personsVibeHome({ VIBE_HOME: '/srv/v' }, '/home/u', 'linux'), '/srv/v');
});

test('on Windows no .env link is made — the sign-in is the Credential Manager there', () => {
  const home = join(base, 'win-signin');
  mkdirSync(home);
  const r = linkSignIn(home, { personsHome: join(base, 'nobody'), platform: 'win32' });
  assert.equal(r.linked, false);
  assert.match(r.why, /Credential Manager/u);
  assert.equal(existsSync(join(home, '.env')), false);
});

test('the .env sign-in link points at the person\'s .env and is made WITHOUT reading it (an unreadable target still links)', { skip: POSIX_ONLY }, () => {
  const person = join(base, 'person-vibe');
  mkdirSync(person);
  const target = join(person, '.env');
  writeFileSync(target, 'MISTRAL_API_KEY=sk-test-never-read\n', { mode: 0o600 });
  chmodSync(target, 0o000); // a read would fail with EACCES — a link made by lstat alone does not care
  const home = join(base, 'owned-home');
  mkdirSync(home);
  try {
    const r = linkSignIn(home, { personsHome: person });
    assert.deepEqual(r, { linked: true, why: '' });
    assert.equal(lstatSync(join(home, '.env')).isSymbolicLink(), true);
    assert.equal(readlinkSync(join(home, '.env')), target);
  } finally {
    chmodSync(target, 0o600);
  }
});

test('no .env, or a .env that is itself a link, makes no link and leaves nothing behind', { skip: POSIX_ONLY }, () => {
  const empty = join(base, 'person-empty');
  mkdirSync(empty);
  const home1 = join(base, 'owned-1');
  mkdirSync(home1);
  const none = linkSignIn(home1, { personsHome: empty });
  assert.equal(none.linked, false);
  assert.match(none.why, /no \.env/u);
  assert.equal(existsSync(join(home1, '.env')), false);

  const chained = join(base, 'person-chained');
  mkdirSync(chained);
  writeFileSync(join(base, 'elsewhere.env'), 'X=1\n');
  symlinkFile(join(base, 'elsewhere.env'), join(chained, '.env'));
  const home2 = join(base, 'owned-2');
  mkdirSync(home2);
  const refused = linkSignIn(home2, { personsHome: chained });
  assert.equal(refused.linked, false);
  assert.match(refused.why, /itself a link/u);
  let left = true;
  try { lstatSync(join(home2, '.env')); } catch { left = false; }
  assert.equal(left, false, 'a refused link leaves nothing at $VIBE_HOME/.env');
});

/** A plain FILE symlink (POSIX) — the person's .env being a link itself, or a link in a listed home. */
const symlinkFile = (target, at) => symlinkSync(target, at, 'file');

test('a folder link on Linux is a symlink, and removing it by its own name leaves the target', { skip: POSIX_ONLY }, () => {
  const target = join(base, 'link-target');
  mkdirSync(target);
  writeFileSync(join(target, 'kept.txt'), 'kept');
  const at = join(base, 'a-link');
  assert.equal(dirLink(at, target), '');
  assert.equal(lstatSync(at).isSymbolicLink(), true);
  removeDirLink(at);
  assert.equal(existsSync(at), false);
  assert.equal(readFileSync(join(target, 'kept.txt'), 'utf8'), 'kept');
});

/** Whether `pid` is a live process (signal 0); a zombie whose parent died is reaped by init and answers no. */
const alive = (pid) => {
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    return e.code === 'EPERM';
  }
};

test('a timed-out run ends the child\'s whole process GROUP — a grandchild does not outlive it (plan §5: kill -- -<pgid>)', { skip: POSIX_ONLY }, async () => {
  const r = run('/bin/sh', ['-c', 'sleep 60 & echo $!; wait'], { encoding: 'utf8', timeout: 1500 });
  const grandchild = Number.parseInt(String(r.stdout).trim(), 10);
  assert.ok(Number.isInteger(grandchild) && grandchild > 0, `the fixture printed no pid: ${JSON.stringify(r.stdout)}`);
  const until = Date.now() + 3000;
  while (alive(grandchild) && Date.now() < until) await new Promise((done) => setTimeout(done, 50));
  assert.equal(alive(grandchild), false, `the grandchild (pid ${grandchild}) survived the timeout's tree kill`);
});

test('the home listing is names, sizes and mtimes — taken by lstat, so a link is named and never followed', { skip: POSIX_ONLY }, () => {
  const dir = join(base, 'listing-home');
  mkdirSync(join(dir, 'logs'), { recursive: true });
  writeFileSync(join(dir, 'logs', 'vibe.log'), 'abc');
  const secret = join(base, 'listing-secret');
  writeFileSync(secret, 'x'.repeat(99), { mode: 0o000 });
  symlinkFile(secret, join(dir, '.env'));
  const listing = homeListing(dir);
  assert.deepEqual(listing.map((e) => e.path), ['.env', 'logs/', 'logs/vibe.log']);
  const env = listing.find((e) => e.path === '.env');
  assert.equal(env.link, true);
  assert.notEqual(env.bytes, 99, 'the listing followed the link to its target');
  assert.equal(listing.find((e) => e.path === 'logs/vibe.log').bytes, 3);
  assert.equal(typeof statSync(dir).mtimeMs, 'number');
});

// The probe script runs on import (it reads argv and may exit), so no test can import it: the pure core of its childEnv is cut out of
// the source by its first and last line and run here with minimalEnv in scope. A rename or a reshaped function fails this loudly.
function probeChildEnvFor() {
  const source = readFileSync(join(repo, 'scripts', 'probe-vibe-confinement.mjs'), 'utf8').replaceAll('\r\n', '\n');
  const start = source.indexOf('function childEnvFor(');
  const end = source.indexOf('\n}\n', start);
  assert.ok(start >= 0 && end > start, 'childEnvFor is no longer a top-level function of the probe');
  return new Function('minimalEnv', `${source.slice(start, end + 2)}\nreturn childEnvFor;`)(minimalEnv);
}

const PIN = { VIBE_CLI: 'python', PYTHONUTF8: '1' };

test('on Linux the probe hands a child the minimal environment, never the operator\'s whole shell (RESULTS §31.4, cell L-V9)', () => {
  const childEnvFor = probeChildEnvFor();
  const shell = { PATH: '/usr/bin', HOME: '/home/op', LANG: 'C.UTF-8', SECRET: 'hunter2', GITHUB_TOKEN: 'x', AWS_PROFILE: 'p' };
  const env = childEnvFor({ home: '/h/1', env: { VIBE_ACTIVE_MODEL: 'm' } }, { cli: 'python', platform: 'linux', env: shell });
  assert.equal(env.SECRET, undefined, 'SECRET reached the child');
  assert.deepEqual(Object.keys(env).filter((k) => !(k in minimalEnv('linux', shell))).sort(),
    ['NoDefaultCurrentDirectoryInExePath', 'PYTHONUTF8', 'VIBE_ACTIVE_MODEL', 'VIBE_CLI', 'VIBE_HOME'], 'a variable beyond the minimal list reached the child');
  assert.equal(env.PATH, '/usr/bin');
  assert.equal(env.VIBE_HOME, '/h/1');
});

test('on Windows the probe still starts from the whole environment — the RESULTS cells there were measured with it', () => {
  const childEnvFor = probeChildEnvFor();
  const shell = { Path: 'C:\\x', SECRET: 'hunter2' };
  const env = childEnvFor({ home: 'H', env: { A: 'b' } }, { cli: 'python', platform: 'win32', env: shell });
  assert.deepEqual(env, { ...shell, NoDefaultCurrentDirectoryInExePath: '1', VIBE_HOME: 'H', A: 'b', ...PIN });
});

test('a replaced environment is the arm\'s own plus the pin on every platform, and `--cli inherit` drops the pin', () => {
  const childEnvFor = probeChildEnvFor();
  const shell = { SECRET: 'hunter2' };
  for (const platform of ['linux', 'win32']) {
    assert.deepEqual(childEnvFor({ replaceEnv: { A: '1' } }, { cli: 'python', platform, env: shell }), { A: '1', ...PIN });
    assert.equal('VIBE_CLI' in childEnvFor({ home: 'H' }, { cli: 'inherit', platform, env: { VIBE_CLI: 'rust' } }), false);
  }
});

// ------------------------------------------------------------------------------------------------------ M-V7's keyring arms (S-N17)
// DN4/DN5 item 5/DN10 of todo/PLAN_kimi_and_vibe_on_native_linux.md: on a native Linux host coai's Vibe launch sets PYTHON_KEYRING_BACKEND=
// keyring.backends.fail.Keyring, so M-V7 must measure THAT road — arms a and a2 carry it — and arms a3/a3c give Vibe a keyring that IS available
// (a fake HOME whose keyringrc.cfg names a probe-owned backend), so the claim "the forced backend beats an available one" is proven, not assumed.
// Nothing here runs Vibe or Python; the backend's text is checked as text.

const FAIL = 'keyring.backends.fail.Keyring';
const scratchOf = (name) => join(base, name);

test('S-N17: on Linux arms a and a2 carry PYTHON_KEYRING_BACKEND, a3 carries it AND a fake HOME, a3c the fake HOME only, b is as before', () => {
  const scratch = '/s/M-V7-run';
  const arm = (name) => keyringArm(name, 'linux', { scratch });

  assert.equal(arm('a').env.PYTHON_KEYRING_BACKEND, FAIL, "M-V7 arm a on Linux carries no PYTHON_KEYRING_BACKEND — it measures today's open keyring road, not the launch E2 ships (DN10)");
  assert.deepEqual(arm('a').env, { PYTHON_KEYRING_BACKEND: FAIL });
  assert.deepEqual(arm('a2').env, { VIBE_TEST_DISABLE_KEYRING: '1', PYTHON_KEYRING_BACKEND: FAIL });
  assert.deepEqual(arm('a3').env, { PYTHON_KEYRING_BACKEND: FAIL, HOME: '/s/M-V7-run/fake-home' }, 'a3 must carry the forced backend AND the fake HOME');
  assert.deepEqual(arm('a3c').env, { HOME: '/s/M-V7-run/fake-home' }, 'a3c is a3 without the variable — its teeth');
  assert.deepEqual(arm('b').env, {});
  assert.equal(KEYRING_FAIL_BACKEND, FAIL);
});

test('S-N17: a3 and a3c share one fake HOME and one marker file inside the arm scratch; a, a2 and b have neither', () => {
  const scratch = '/s/M-V7-run';
  const a3 = keyringArm('a3', 'linux', { scratch });
  const a3c = keyringArm('a3c', 'linux', { scratch });

  assert.equal(a3.fakeHome, '/s/M-V7-run/fake-home');
  assert.equal(a3c.fakeHome, a3.fakeHome);
  assert.equal(a3c.markerFile, a3.markerFile);
  assert.ok(a3.markerFile.startsWith(`${scratch}/`), `the marker file ${a3.markerFile} is outside the arm scratch`);
  for (const name of ['a', 'a2', 'b']) {
    const other = keyringArm(name, 'linux', { scratch });
    assert.deepEqual([other.fakeHome, other.markerFile, other.applies], ['', '', true], name);
  }
  assert.throws(() => keyringArm('nope', 'linux', { scratch }), /nope/u);
});

test('S-N17: on Windows every arm is today\'s — a empty, a2 only VIBE_TEST_DISABLE_KEYRING, b empty — and a3/a3c do not apply', () => {
  const arm = (name) => keyringArm(name, 'win32', { scratch: 'C:\\s\\M-V7' });

  assert.deepEqual(arm('a').env, {});
  assert.deepEqual(arm('a2').env, { VIBE_TEST_DISABLE_KEYRING: '1' });
  assert.deepEqual(arm('b').env, {});
  assert.deepEqual([arm('a3').applies, arm('a3c').applies], [false, false], 'a Linux-only arm was offered on Windows');
});

test('S-N17: writeProbeKeyring writes the config and the backend ONLY under the fake home, names the marker, and never makes it', () => {
  const scratch = scratchOf('keyring-write');
  const fakeHome = join(scratch, 'fake-home');
  const markerFile = join(scratch, 'keyring-marker.txt');
  mkdirSync(scratch, { recursive: true });
  writeFileSync(join(scratch, 'sibling.txt'), 'untouched');

  const written = writeProbeKeyring(fakeHome, markerFile);

  assert.deepEqual(written.sort(), ['.config/python_keyring/keyringrc.cfg', 'keyring-backend/probe_marker.py']);
  assert.deepEqual(files(scratch), ['fake-home/.config/python_keyring/keyringrc.cfg', 'fake-home/keyring-backend/probe_marker.py', 'sibling.txt'],
    'something was written outside the fake home');
  assert.equal(existsSync(markerFile), false, 'the marker exists before any keyring call — arm a3c could never tell');
  const cfg = readFileSync(join(fakeHome, '.config', 'python_keyring', 'keyringrc.cfg'), 'utf8');
  assert.match(cfg, /^\[backend\]$/mu);
  assert.match(cfg, /^default-keyring = probe_marker\.MarkerKeyring$/mu);
  assert.ok(cfg.includes(`keyring-path = ${fakeHome.replaceAll('\\', '/')}/keyring-backend`), cfg);
});

test('S-N17: the probe backend appends the SERVICE name to the marker and returns nothing — no key exists anywhere', () => {
  const scratch = scratchOf('keyring-backend');
  const markerFile = join(scratch, 'marker.txt');
  writeProbeKeyring(join(scratch, 'fake-home'), markerFile);

  const py = readFileSync(join(scratch, 'fake-home', 'keyring-backend', 'probe_marker.py'), 'utf8');

  assert.match(py, /class MarkerKeyring\(KeyringBackend\):/u);
  assert.match(py, /priority = 1/u);
  assert.ok(py.includes(JSON.stringify(markerFile.replaceAll('\\', '/'))), 'the module does not name the marker file');
  assert.match(py, /open\(MARKER, 'a'/u, 'the marker is appended to');
  for (const verb of ['get_password', 'set_password', 'delete_password']) assert.match(py, new RegExp(`def ${verb}\\(`, 'u'), verb);
  assert.deepEqual(py.match(/^\s*return\b.*$/gmu).map((l) => l.trim()), ['return None', 'return None', 'return None'], 'the backend returns something other than None');
  assert.equal(/username|password\)|\.write\((?!service)/u.test(py.replace(/def \w+\([^)]*\)/gu, '')), false, 'the backend writes more than the service name');
});

test('S-N17: keyringSetup makes the fake home for a3 and a3c only', () => {
  const scratch = scratchOf('keyring-setup');
  mkdirSync(scratch, { recursive: true });

  const a = keyringSetup('a', 'linux', { scratch: join(scratch, 'a') });
  const a3 = keyringSetup('a3', 'linux', { scratch: join(scratch, 'a3') });
  const a3c = keyringSetup('a3c', 'linux', { scratch: join(scratch, 'a3c') });

  assert.equal(existsSync(join(scratch, 'a')), false, 'arm a made a folder');
  for (const k of [a3, a3c]) assert.equal(existsSync(join(k.fakeHome, '.config', 'python_keyring', 'keyringrc.cfg')), true, k.arm);
  assert.deepEqual(a.env, { PYTHON_KEYRING_BACKEND: FAIL });
});

test('S-N17: --arms picks M-V7 arms by the name before the colon, so the free live check can run a3 and a3c and nothing paid', () => {
  const names = ['a: empty short VIBE_HOME', 'a2: empty home + VIBE_TEST_DISABLE_KEYRING=1', 'a3: forced backend + a fake HOME', 'a3c: the same fake HOME, NO forced backend', 'b: VIBE_ACTIVE_MODEL=nope'];

  assert.deepEqual(names.filter((n) => armWanted(n, ['a3', 'a3c'])), [names[2], names[3]], 'a --arms filter let arm a, a2 or the paid arm b through');
  assert.deepEqual(names.filter((n) => armWanted(n, [])), names, 'no filter = every arm');
  assert.deepEqual(names.filter((n) => armWanted(n, ['a'])), [names[0]], 'a does not match a2, a3 or a3c');
});

test('G18: armWanted treats a missing filter (null or undefined) as "every arm", not as a crash on .length', () => {
  assert.equal(armWanted('a3: forced backend', undefined), true);
  assert.equal(armWanted('a3: forced backend', null), true);
});

// CodeRabbit (probe-vibe-confinement.mjs): `--arms ""` / `--arms ,` parsed to [], which armWanted reads as "no filter" - every arm ran, the PAID arm b included.
test('armFilterOf: an omitted --arms is "no filter"; a given one must name an arm - an empty value, a bare comma or a missing value is an error, never every arm', () => {
  assert.deepEqual(armFilterOf(['--cells', 'M-V7']), { filter: [], error: '' });
  assert.deepEqual(armFilterOf(['--arms', 'a3,a3c']), { filter: ['a3', 'a3c'], error: '' });
  assert.deepEqual(armFilterOf(['--arms', ' a3 , ,a3c ']), { filter: ['a3', 'a3c'], error: '' });
  for (const argv of [['--arms', ''], ['--arms', ','], ['--arms', ' , '], ['--arms'], ['--cells', 'M-V7', '--arms']]) {
    const { filter, error } = armFilterOf(argv);
    assert.match(error, /--arms/u, `${JSON.stringify(argv)} was accepted`);
    assert.deepEqual(filter, [], 'an error carries no filter');
  }
});

// CodeRabbit, second look (vibe.mjs:366, probe-vibe-confinement.mjs:82).
test('armFilterOf: another option is not an arm name - `--arms --cells M-V7` is an error, not a filter naming "--cells"', () => {
  for (const argv of [['--arms', '--cells', 'M-V7'], ['--arms', '-x'], ['--cells', 'M-V7', '--arms', '--repeats', '1']]) {
    const { filter, error } = armFilterOf(argv);
    assert.match(error, /--arms/u, `${JSON.stringify(argv)} was accepted`);
    assert.deepEqual(filter, []);
  }
  assert.deepEqual(armFilterOf(['--arms', 'a3', '--cells', 'M-V7']), { filter: ['a3'], error: '' }, 'a later option after a real value is fine');
});

test('armsCellsError: with --arms every selected cell must be an arm cell that has a wanted arm - the default cell list (M-V5 resumes with paid turns, M-V9 launches a control) is refused', () => {
  const byCell = { 'M-V7': [{ arm: 'a: forced' }, { arm: 'a3: fake home' }, { arm: 'b: paid' }], 'M-V9': [{ arm: 'hook' }] };
  assert.equal(armsCellsError([], ['M-V5', 'M-V7', 'M-V9'], byCell), '', 'no --arms: no constraint');
  assert.equal(armsCellsError(['a3'], ['M-V7'], byCell), '');
  assert.match(armsCellsError(['a3'], ['M-V5'], byCell), /M-V5/u, 'M-V5 has no arms: it would run its paid two-turn resume unchecked');
  assert.match(armsCellsError(['a3'], ['M-V7', 'M-V9'], byCell), /M-V9/u, 'M-V9 has no wanted arm: only its positive control would launch');
  assert.match(armsCellsError(['a3'], ['M-V1', 'M-V5', 'M-V7', 'M-V9'], byCell), /M-V1/u, 'the default cell list is not a compatible selection');
  assert.match(armsCellsError(['zz'], ['M-V7'], byCell), /M-V7/u, 'a filter that selects nothing in a cell is refused, not a successful run with no measurement');
});
