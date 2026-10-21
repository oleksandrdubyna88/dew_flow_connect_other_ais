// node --test scripts/lib/*.test.mjs — the pure core of scripts/probe-native-linux-host.mjs (E1.S1, N-H1 / N-V0 / N-K0 of
// todo/PLAN_kimi_and_vibe_on_native_linux.md §5; scenario S-N19). The probe runs on import and cannot be imported by a test, so
// everything it decides lives in lib/host-facts.mjs and takes its world INJECTED: a file system, a process lister, a runner. Runs
// on every platform over fakes; it opens no real file, starts no process, and never sees a real sign-in.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { collectHostFacts, DAEMON_PREFIXES, hostFactsMode, keyringSession, mountTypeOf, pathEntries, PY_DISTRIBUTIONS, PY_KEYRING_CLASS, readBanner, redactSlot, resolveTool, signInRecord } from './host-facts.mjs';

const PROBE = join(dirname(fileURLToPath(import.meta.url)), '..', 'probe-native-linux-host.mjs');
const LINUX = process.platform === 'linux';
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-host-facts-test-'));
after(() => rmSync(base, { recursive: true, force: true }));
const SECRET = 'MISTRAL_API_KEY=SENTINEL-SECRET-DO-NOT-READ';
const TOKEN = '{"refresh_token":"SENTINEL-TOKEN-DO-NOT-READ"}';
const FAIL_BACKEND = 'keyring.backends.fail.Keyring';

/** An in-memory file system: `tree` maps a path to `{ content, mode, uid, mtimeMs }` or `{ socket }`; folders are implied. `reads` records every readFile. */
function fakeFs(tree) {
  const reads = [];
  const stats = (p) => {
    const e = tree[p];
    const folder = Object.keys(tree).some((k) => k.startsWith(`${p}/`));
    if (!e && !folder) throw Object.assign(new Error(`ENOENT ${p}`), { code: 'ENOENT' });
    const kind = e?.socket ? 'socket' : e ? 'file' : 'dir';
    return {
      isFile: () => kind === 'file', isDirectory: () => kind === 'dir', isSymbolicLink: () => false, isSocket: () => kind === 'socket',
      size: e?.content?.length ?? 0, mode: (kind === 'dir' ? 0o40755 : 0o100000) | (e?.mode ?? 0o644), uid: e?.uid ?? 0, mtimeMs: e?.mtimeMs ?? 1_700_000_000_000,
    };
  };
  return {
    reads,
    lstat: (p) => stats(p),
    readdir: (p) => [...new Set(Object.keys(tree).filter((k) => k.startsWith(`${p}/`)).map((k) => k.slice(p.length + 1).split('/')[0]))].sort((a, b) => a.localeCompare(b)),
    readFile: (p) => {
      reads.push(p);
      if (!(p in tree) || tree[p].content === undefined) throw Object.assign(new Error(`ENOENT ${p}`), { code: 'ENOENT' });
      return tree[p].content;
    },
  };
}

const HOME = '/home/u';
const world = (extra = {}) => ({
  '/proc/version': { content: 'Linux version 7.0.0-30-generic (buildd@lcy02) (gcc) #30-Ubuntu SMP\n' },
  '/etc/os-release': { content: 'NAME="Ubuntu"\nPRETTY_NAME="Ubuntu 26.04 LTS"\nID=ubuntu\n' },
  '/proc/sys/fs/protected_symlinks': { content: '1\n' },
  '/proc/self/mountinfo': { content: '24 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw\n30 24 0:27 / /tmp rw - tmpfs tmpfs rw\n' },
  [`${HOME}/.vibe/.env`]: { content: SECRET, mode: 0o600, uid: 1000, mtimeMs: 1_760_000_000_000 },
  [`${HOME}/.vibe/config.toml`]: { content: 'x' },
  [`${HOME}/.kimi-code/credentials/kimi-code-0123456789abcdef.json`]: { content: TOKEN, mode: 0o600, uid: 1000 },
  [`${HOME}/.kimi-code/credentials/kimi-code-env-x.json`]: { content: TOKEN, mode: 0o600, uid: 1000 },
  ...extra,
});

const okRun = (stdout = '') => ({ status: 0, stdout, stderr: '' });
/** A runner that records every call and answers by `byCall`, else with an empty success. */
function fakeRunner(byCall) {
  const calls = [];
  const run = (file, args, options) => {
    calls.push({ file, args, options });
    return byCall ? byCall(file, args, options) : okRun('');
  };
  run.calls = calls;
  return run;
}

const baseDeps = (fsx, over = {}) => ({
  fsx, run: fakeRunner(), env: { PATH: '/usr/bin:/bin', HOME }, uid: 1000, home: HOME, platform: { node: 'v22.0.0', glibc: '2.39' },
  listing: () => [{ pid: 1, ppid: 0, comm: 'systemd' }], serverEnvNames: () => [], serverPath: '', series: '', scratch: '/scratch', hash: () => 'h', now: () => new Date(0),
  coaiMcp: '', vibe: '', kimi: '', ...over,
});

const signInReads = (fsx) => fsx.reads.filter((p) => p.endsWith('.vibe/.env') || p.includes('/credentials/'));

test('S-N19: the facts record holds ~/.vibe/.env and the credentials by lstat and NEVER opens either', () => {
  const fsx = fakeFs(world());
  const facts = collectHostFacts(baseDeps(fsx));
  assert.deepEqual(signInReads(fsx), [], "the facts record opened ~/.vibe/.env — a sign-in's bytes were read (§2 item 5)");
  assert.ok(fsx.reads.includes('/proc/version'), 'the positive twin: the spy sees the files the record IS allowed to read, so an empty list means something');
  const env = facts.v0.signIn;
  assert.equal(env.exists, true);
  assert.equal(env.type, 'file');
  assert.equal(env.size, SECRET.length);
  assert.equal(env.mode, '0600');
  assert.equal(env.uid, 1000);
  assert.equal(env.mtime, new Date(1_760_000_000_000).toISOString());
  assert.equal(JSON.stringify(facts).includes('SENTINEL'), false, 'no byte of a sign-in is in the record');
});

test('S-N19: the credentials folder is recorded by NAMES with the slot hex redacted, modes and owners — no file is opened', () => {
  const fsx = fakeFs(world());
  const facts = collectHostFacts(baseDeps(fsx, { kimi: '/opt/kimi/bin/kimi' }));
  const names = facts.k0.credentials.entries.map((e) => e.name);
  assert.deepEqual(names, ['kimi-code-<hex>.json', 'kimi-code-env-x.json']);
  assert.equal(facts.k0.credentials.entries[0].mode, '0600');
  assert.equal(facts.k0.credentials.entries[0].uid, 1000);
  assert.deepEqual(signInReads(fsx), []);
  assert.equal(JSON.stringify(facts).includes('0123456789abcdef'), false, 'the slot hex never reaches the record');
  assert.equal(redactSlot('kimi-code-0123456789abcdef.json'), 'kimi-code-<hex>.json');
  assert.equal(redactSlot('config.toml'), 'config.toml');
});

test('S-N19: an absent sign-in is a record that says so, not an exception or an empty fact', () => {
  assert.deepEqual(signInRecord(fakeFs({}), `${HOME}/.vibe/.env`), { path: `${HOME}/.vibe/.env`, exists: false });
});

test('S-N19: the raw keyring-session reading reports each fact BY ITSELF — "a set DBUS_SESSION_BUS_ADDRESS was recorded as unset"', () => {
  const base = { env: {}, serverNames: [], uid: 1000, fsx: fakeFs({ '/run/user/1000/bus': { socket: true } }), listing: () => [] };
  assert.deepEqual(keyringSession(base), { busAddressSet: false, serverBusAddressSet: false, busSocket: true, busSocketPath: '/run/user/1000/bus', daemons: [], listing: 'read' });
  assert.equal(keyringSession({ ...base, env: { DBUS_SESSION_BUS_ADDRESS: 'unix:path=/x' } }).busAddressSet, true, 'a set DBUS_SESSION_BUS_ADDRESS was recorded as unset');
  assert.equal(keyringSession({ ...base, env: { DBUS_SESSION_BUS_ADDRESS: '' } }).busAddressSet, false, 'an empty value is not set');
  assert.equal(keyringSession({ ...base, serverNames: ['PATH', 'DBUS_SESSION_BUS_ADDRESS'] }).serverBusAddressSet, true);
  assert.equal(keyringSession({ ...base, fsx: fakeFs({}) }).busSocket, false);
  assert.equal(JSON.stringify(keyringSession({ ...base, env: { DBUS_SESSION_BUS_ADDRESS: 'unix:path=/SENTINEL' } })).includes('SENTINEL'), false, 'the name is recorded, never the value');
});

test('S-N19: the bus socket is asked at $XDG_RUNTIME_DIR/bus when the variable is set, else at /run/user/<uid>/bus', () => {
  const base = { env: {}, serverNames: [], uid: 7, fsx: fakeFs({ '/xdg/bus': { socket: true } }), listing: () => [] };
  assert.equal(keyringSession({ ...base, env: { XDG_RUNTIME_DIR: '/xdg' } }).busSocket, true);
  assert.equal(keyringSession({ ...base, env: { XDG_RUNTIME_DIR: '/xdg' } }).busSocketPath, '/xdg/bus');
  assert.equal(keyringSession(base).busSocketPath, '/run/user/7/bus');
  assert.equal(keyringSession(base).busSocket, false);
  assert.equal(keyringSession({ ...base, fsx: fakeFs({ '/run/user/7/bus': { content: 'plain file' } }) }).busSocket, false, 'a file named bus is not a socket');
});

test('S-N19: a keyring daemon in the listing is named by its comm prefix (comm is cut at 15 characters); an unreadable listing is "cannot tell"', () => {
  const base = { env: {}, serverNames: [], uid: 1, fsx: fakeFs({}) };
  const listing = [{ pid: 1, comm: 'systemd' }, { pid: 2, comm: 'gnome-keyring-d' }, { pid: 3, comm: 'kwalletd6' }, { pid: 4, comm: 'keepassxc' }, { pid: 5, name: 'kwalletd5' }, { pid: 6, comm: 'sshd' }];
  const found = keyringSession({ ...base, listing: () => listing });
  assert.deepEqual(found.daemons, ['gnome-keyring-d', 'kwalletd6', 'keepassxc', 'kwalletd5']);
  assert.equal(found.listing, 'read');
  const blind = keyringSession({ ...base, listing: () => { throw new Error('EACCES'); } });
  assert.deepEqual(blind.daemons, []);
  assert.equal(blind.listing, 'cannot tell');
  assert.deepEqual(DAEMON_PREFIXES, ['gnome-keyring', 'kwalletd', 'keepassxc']);
});

test('S-N19: coai-mcp --host-facts is recorded as its line; exit 64 is "no such mode", never an empty fact', () => {
  const line = { server: 'coai-mcp 0.63.0', hostKind: 'linux', banner: { read: true } };
  const asked = [];
  const run = (file, args) => { asked.push([file, ...args]); return okRun(`${JSON.stringify(line)}\n`); };
  assert.deepEqual(hostFactsMode(run, '/bin/coai-mcp'), { status: 'read', line });
  assert.deepEqual(asked, [['/bin/coai-mcp', '--host-facts']]);
  assert.deepEqual(hostFactsMode(() => ({ status: 64, stdout: '', stderr: 'usage' }), '/bin/coai-mcp'), { status: 'no such mode' });
  assert.equal(hostFactsMode(() => okRun('not json'), '/bin/coai-mcp').status, 'unparseable');
  assert.equal(hostFactsMode(() => ({ status: 70, stdout: '', stderr: 'boom' }), '/bin/coai-mcp').status, 'failed');
  assert.equal(hostFactsMode(() => ({ status: null, stdout: '', stderr: '', error: Object.assign(new Error('x'), { code: 'ENOENT' }) }), '/bin/coai-mcp').status, 'unavailable');
  assert.equal(hostFactsMode(() => okRun(''), '/bin/coai-mcp').status, 'unparseable', 'an empty answer is not a fact');
});

test('the kernel banner is read from /proc/version: text, whether it names WSL, and "not read" when it is absent or empty', () => {
  assert.deepEqual(readBanner(fakeFs({ '/proc/version': { content: 'Linux version 7.0.0-30-generic (x)\n' } })), { read: true, text: 'Linux version 7.0.0-30-generic (x)', namesWsl: false });
  assert.equal(readBanner(fakeFs({ '/proc/version': { content: 'Linux version 5.15.1-microsoft-standard-WSL2\n' } })).namesWsl, true);
  assert.deepEqual(readBanner(fakeFs({})), { read: false, text: '', namesWsl: false });
  assert.equal(readBanner(fakeFs({ '/proc/version': { content: '  \n' } })).read, false, 'an empty banner is not a reading');
});

test('the mount TYPE of a path is the longest mount point that holds it — octal escapes in a mount point are decoded', () => {
  const info = ['24 1 8:2 / / rw - ext4 /dev/sda2 rw', '30 24 0:27 / /tmp rw - tmpfs tmpfs rw', String.raw`31 24 0:28 / /mnt/my\040disk rw - nfs4 srv:/x rw`, '32 30 0:29 / /tmp/inner rw - overlay overlay rw'].join('\n');
  assert.equal(mountTypeOf(info, '/tmp'), 'tmpfs');
  assert.equal(mountTypeOf(info, '/tmp/x/y'), 'tmpfs');
  assert.equal(mountTypeOf(info, '/tmp/inner/z'), 'overlay');
  assert.equal(mountTypeOf(info, '/tmpfoo'), 'ext4', 'a prefix of the NAME is not a parent folder');
  assert.equal(mountTypeOf(info, '/root/.vibe'), 'ext4');
  assert.equal(mountTypeOf(info, '/mnt/my disk/a'), 'nfs4');
  assert.equal(mountTypeOf('', '/tmp'), '');
});

test('a tool resolves in the given order, only to an executable file; an empty PATH entry is a candidate in the current folder', () => {
  const fsx = fakeFs({ '/a/rg': { content: '', mode: 0o644 }, '/b/rg': { content: '', mode: 0o755 }, '/c/rg': { content: '', mode: 0o755 } });
  assert.deepEqual(resolveTool(fsx, 'rg', ['/a', '/b', '/c']), { found: true, path: '/b/rg', index: 1 });
  assert.deepEqual(resolveTool(fsx, 'fd', ['/a', '/b']), { found: false, path: '', index: -1 });
  assert.equal(resolveTool(fakeFs({ './rg': { content: '', mode: 0o755 } }), 'rg', ['']).found, true);
});

test('every PATH entry is recorded with its kind, owner and mode — facts, not a verdict', () => {
  const fsx = fakeFs({ '/ok/x': { content: '', mode: 0o755 }, '/bad/x': { content: '' } });
  const dirStat = (p, mode, uid) => ({ ...fsx.lstat(`${p}/x`), isFile: () => false, isDirectory: () => true, mode, uid });
  const withDirs = { ...fsx, lstat: (p) => (p === '/bad' ? dirStat('/bad', 0o40777, 1000) : p === '/ok' ? dirStat('/ok', 0o40755, 0) : fsx.lstat(p)) };
  const entries = pathEntries(withDirs, '/ok::rel:/bad:/gone');
  assert.deepEqual(entries.map((e) => [e.entry, e.kind]), [['/ok', 'directory'], ['', 'empty'], ['rel', 'relative'], ['/bad', 'directory'], ['/gone', 'missing']]);
  assert.deepEqual([entries[0].mode, entries[0].uid, entries[0].worldWritable], ['0755', 0, false]);
  assert.deepEqual([entries[3].mode, entries[3].uid, entries[3].worldWritable], ['0777', 1000, true]);
  assert.deepEqual(pathEntries(withDirs, ''), [{ entry: '', kind: 'empty' }]);
});

test("N-V0 asks Vibe's tool Python for keyring.get_keyring() twice — built environment, then plus the failing backend — and NEVER for a password", () => {
  assert.equal(/get_password|get_credential|set_password|delete_password/u.test(PY_KEYRING_CLASS + PY_DISTRIBUTIONS), false, 'a one-line program that reads or writes a key');
  const fsx = fakeFs(world({ [`${HOME}/.local/share/uv/tools/mistral-vibe/bin/python`]: { content: '', mode: 0o755 } }));
  const run = fakeRunner((file, args, options) => {
    if (args[1] === PY_DISTRIBUTIONS) return okRun('[["keyring","25.7.0"],["python-dotenv","1.0.1"]]');
    return okRun(options.env.PYTHON_KEYRING_BACKEND ? `${FAIL_BACKEND}\n` : 'keyring.backends.SecretService.Keyring\n');
  });
  const facts = collectHostFacts(baseDeps(fsx, { run }));
  const classCalls = run.calls.filter((c) => c.args[1] === PY_KEYRING_CLASS);
  assert.equal(classCalls.length, 2);
  assert.equal(classCalls[0].options.env.PYTHON_KEYRING_BACKEND, undefined, 'the first reading is the built environment alone');
  assert.equal(classCalls[1].options.env.PYTHON_KEYRING_BACKEND, FAIL_BACKEND, 'the second adds the forced backend');
  const allowed = ['PATH', 'HOME', 'USER', 'LOGNAME', 'SHELL', 'LANG', 'LC_ALL', 'LC_CTYPE', 'TERM', 'TMPDIR', 'TMP', 'TEMP', 'DOTNET_ROOT', 'VIBE_HOME', 'VIBE_CLI', 'PYTHONUTF8', 'VIBE_ACTIVE_MODEL', 'PYTHON_KEYRING_BACKEND'];
  for (const call of classCalls) {
    assert.equal(call.args[0], '-c');
    assert.ok(call.options.env.VIBE_HOME.startsWith('/scratch/'), 'a FRESH VIBE_HOME inside the phase scratch');
    assert.deepEqual(Object.keys(call.options.env).filter((k) => !allowed.includes(k)), [], 'env -i + the minimal list + the built names, nothing inherited');
  }
  assert.deepEqual(facts.v0.keyringBackend, { builtEnvironment: 'keyring.backends.SecretService.Keyring', withFailBackend: FAIL_BACKEND, failBackendForced: true });
  assert.deepEqual(facts.v0.distributions, [['keyring', '25.7.0'], ['python-dotenv', '1.0.1']]);
});

test("N-V0 is honest when Vibe's Python is absent: the fact is \"no python\", and no python is started", () => {
  const run = fakeRunner();
  const facts = collectHostFacts(baseDeps(fakeFs(world()), { run }));
  assert.equal(run.calls.length, 0);
  assert.equal(facts.v0.python.found, false);
  assert.equal(facts.v0.keyringBackend.status, 'no python');
});

test('N-K0 runs only when a kimi path is named, asks --version and acp --help, and never a login', () => {
  const fsx = fakeFs(world());
  const without = fakeRunner();
  assert.equal(collectHostFacts(baseDeps(fsx, { run: without })).k0.asked, false);
  assert.equal(without.calls.length, 0);
  const run = fakeRunner((file, args) => okRun(args[0] === '--version' ? 'kimi 2.2.0\n' : 'usage: kimi acp\n'));
  const facts = collectHostFacts(baseDeps(fsx, { run, kimi: '/opt/kimi/bin/kimi' }));
  assert.deepEqual(run.calls.map((c) => [c.file, ...c.args]), [['/opt/kimi/bin/kimi', '--version'], ['/opt/kimi/bin/kimi', 'acp', '--help']]);
  assert.equal(facts.k0.version, 'kimi 2.2.0');
  assert.equal(run.calls.some((c) => c.args.some((a) => /login|logout|setup/u.test(a))), false);
});

test('N-H1: the banner, the OS, the glibc, the uid, protected_symlinks and the mount types of the places coai writes are all recorded', () => {
  const h1 = collectHostFacts(baseDeps(fakeFs(world()))).h1;
  assert.equal(h1.banner.read, true);
  assert.equal(h1.banner.namesWsl, false);
  assert.equal(h1.osPrettyName, 'Ubuntu 26.04 LTS');
  assert.deepEqual([h1.node, h1.glibc, h1.uid, h1.protectedSymlinks], ['v22.0.0', '2.39', 1000, '1']);
  assert.equal(h1.mounts['/tmp'], 'tmpfs');
  assert.equal(h1.mounts[HOME], 'ext4');
  assert.equal(h1.mounts[`${HOME}/.vibe`], 'ext4');
  assert.equal(h1.mounts[`${HOME}/.kimi-code/credentials`], 'ext4');
  assert.equal(`${HOME}/.local/share` in h1.mounts, true);
});

test('N-H1: the installed coai-mcp is recorded by path, version, sha256 and its --host-facts answer (an old binary: "no such mode")', () => {
  const fsx = fakeFs(world({ '/opt/coai-mcp': { content: 'bin', mode: 0o755 } }));
  const run = fakeRunner((file, args) => (args[0] === '--version' ? okRun('coai-mcp 0.62.0\n') : { status: 64, stdout: '', stderr: 'usage' }));
  const h1 = collectHostFacts(baseDeps(fsx, { run, coaiMcp: '/opt/coai-mcp', hash: (p) => `sha-of-${p}` })).h1;
  assert.deepEqual(h1.coaiMcp, { path: '/opt/coai-mcp', version: 'coai-mcp 0.62.0', sha256: 'sha-of-/opt/coai-mcp', hostFacts: { status: 'no such mode' } });
});

const probe = (args, env = {}) => spawnSync(process.execPath, [PROBE, ...args], { encoding: 'utf8', env: { PATH: process.env.PATH, ...env }, timeout: 60_000 });

test('the probe refuses a malformed command line with 65 on every platform — an unknown flag, a missing value, a repeat, an unsafe value', () => {
  for (const args of [['--bogus', 'x'], ['--series'], ['--kimi', 'a b'], ['--kimi', '$(id)'], ['--kimi', '/a', '--kimi', '/b'], ['stray']]) {
    const r = probe(args);
    assert.equal(r.status, 65, `${args.join(' ')} → ${r.status} ${r.stderr}`);
    assert.equal(r.stdout, '');
  }
});

test('the probe reads /proc and the files of the Linux host, so off Linux it says so and exits 69 — it prints no record there', { skip: LINUX ? 'only meaningful off Linux' : false }, () => {
  const r = probe([]);
  assert.equal(r.status, 69);
  assert.equal(r.stdout, '');
});

test('[Linux only: reads /proc — runs on the Linux CI job] the probe, run for real over a fake home, prints one JSON record that holds the lstat facts of the sign-in and none of its bytes', { skip: LINUX ? false : 'reads /proc and the Linux host: runs on the Linux CI job' }, () => {
  const home = join(base, 'home');
  mkdirSync(join(home, '.vibe'), { recursive: true });
  mkdirSync(join(home, '.kimi-code', 'credentials'), { recursive: true });
  writeFileSync(join(home, '.vibe', '.env'), SECRET, { mode: 0o600 });
  writeFileSync(join(home, '.kimi-code', 'credentials', 'kimi-code-0123456789abcdef.json'), TOKEN, { mode: 0o600 });
  const scratch = join(base, 'scratch');
  const r = probe(['--scratch', scratch], { HOME: home });
  assert.equal(r.status, 0, r.stderr);
  const facts = JSON.parse(r.stdout);
  assert.equal(facts.schema, 'native-host-facts/1');
  assert.equal(facts.h1.banner.read, true, 'a real /proc/version was read');
  assert.equal(facts.h1.uid, process.getuid());
  assert.equal(facts.v0.signIn.size, SECRET.length);
  assert.equal(facts.v0.signIn.mode, '0600');
  assert.equal(facts.k0.asked, false, 'no kimi was named, so none was asked');
  assert.equal(/SENTINEL|0123456789abcdef/u.test(r.stdout), false, 'no byte or slot of a sign-in reached the record');
});
