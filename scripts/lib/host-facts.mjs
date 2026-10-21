// lib/host-facts.mjs — the pure core of scripts/probe-native-linux-host.mjs: the host's facts (N-H1, N-V0, N-K0 of
// todo/PLAN_kimi_and_vibe_on_native_linux.md §5), read by this script's OWN raw reading and recorded as data — no verdict.
//
// The probe runs on import and cannot be imported by a test (the reason lib/vibe.test.mjs:214 gives), so everything it decides is
// here and takes its world INJECTED: `fsx` (lstat/stat/readdir/readFile/mkdir), `run` (a process runner), `listing` (a process
// lister), `hash`, `now`. host-facts.test.mjs drives it over fakes.
//
// THE RULE (§2 item 5 of the plan): nothing here opens, hashes, copies or prints a sign-in. `~/.vibe/.env` and every file under
// `~/.kimi-code/credentials/` are seen through `fsx.lstat` and `fsx.readdir` ONLY — a name (the slot's hex redacted), a type, a size,
// a mode, an owner, a mtime. `fsx.readFile` is used for /proc, /etc/os-release and the two data files make-series.sh wrote, and
// the test's spy fails the build the day it is called for anything else. The two Python programs below ask for a backend's CLASS
// NAME and a list of installed distributions; neither asks for a password.
import { posix } from 'node:path';
import { KEYRING_FAIL_BACKEND, builtEnv as vibeBuiltEnv, minimalEnv, personsVibeHome, vibePython } from './vibe.mjs';
import { builtEnv as kimiBuiltEnv } from './kimi-home.mjs';

/**
 * What counts as a desktop keyring daemon, by an executable name or a `comm` (the kernel cuts it at 15 characters: `gnome-keyring-d`). This is THIS
 * script's own reading of a definition the product shares (src_mcp/runners/Platform/KeyringSessionFacts.cs): shared/keyring-daemon-vectors.json holds
 * the cases, and host-facts.test.mjs and KeyringSessionFactsTests.cs each hold their own reading to them. `keepassxc-proxy` is NOT a daemon.
 */
export const DAEMON_NAMES = ['gnome-keyring-daemon', 'gnome-keyring-d', 'keepassxc'];
export const DAEMON_PREFIXES = ['kwalletd'];
export const isKeyringDaemon = (name) => typeof name === 'string' && (DAEMON_NAMES.includes(name) || DAEMON_PREFIXES.some((prefix) => name.startsWith(prefix)));
/** Installed distributions whose name holds a keyring-related word, as `[[name, version], …]` — names and versions, nothing else. */
export const PY_DISTRIBUTIONS = "import importlib.metadata as m,json;w=('keyring','secretstorage','jeepney','dbus','dotenv');print(json.dumps(sorted([d.metadata['Name'],d.version] for d in m.distributions() if d.metadata['Name'] and any(x in d.metadata['Name'].lower() for x in w))))";
/** The module and class name of the backend `keyring` would pick — `get_keyring()` and nothing after it. */
export const PY_KEYRING_CLASS = "import keyring;k=keyring.get_keyring();print(type(k).__module__+'.'+type(k).__name__)";

const attempt = (fn, fallback) => {
  try {
    return fn();
  } catch {
    return fallback;
  }
};
const statOf = (fsx, path) => attempt(() => (fsx.stat ?? fsx.lstat)(path), null);
const modeText = (mode) => (mode & 0o7777).toString(8).padStart(4, '0');
const typeOf = (st) => (st.isSymbolicLink() ? 'symlink' : st.isDirectory() ? 'directory' : st.isSocket() ? 'socket' : st.isFile() ? 'file' : 'other');
const iso = (ms) => new Date(ms).toISOString();

/** A slot's hex is a name of the person's account, not a fact about the host: any run of 8 or more hex digits becomes `<hex>`. */
// The slot's hex is redacted by the ONE helper the Kimi probes use (J8 of the E1 code gate).
export { redactSlot } from './kimi.mjs';
import { redactSlot } from './kimi.mjs';

/** A read that could not tell, as data: the error's code (`EACCES`, `EIO`, …) or `unknown`. It is NOT "absent" — a file that cannot be read may well be there. */
const unreadable = (error) => ({ state: 'unreadable', code: String(error?.code ?? 'unknown') });

/** Runs a read: `{ value }`, with `absent` as the value for ENOENT (the only error that means "not there"), or `{ failed: unreadable(error) }` for any other (G23). */
const readOr = (read, absent) => {
  try {
    return { value: read() };
  } catch (error) {
    return error?.code === 'ENOENT' ? { value: absent } : { failed: unreadable(error) };
  }
};

/**
 * One path by `lstat` ALONE — type, size, mode, owner, mtime; `{ path, exists: false }` when it is not there (ENOENT), and
 * `{ path, state: 'unreadable', code }` for any other failure. The file is never opened.
 */
export function signInRecord(fsx, path) {
  const { value: st, failed } = readOr(() => fsx.lstat(path), null);
  if (failed) return { path, ...failed };
  return st === null ? { path, exists: false }
    : { path, exists: true, type: typeOf(st), size: st.size, mode: modeText(st.mode), uid: st.uid, mtime: iso(st.mtimeMs) };
}

/** A folder's entries by `readdir` + `lstat`: the first level only, names verbatim. A folder that cannot be listed is `{ state: 'unreadable', code }`, never `[]`. */
const entriesOf = (fsx, dir, rename = (name) => name) => {
  const { value: names, failed } = readOr(() => fsx.readdir(dir), []);
  return failed ?? names.map((name) => {
    const { path: _path, ...facts } = signInRecord(fsx, posix.join(dir, name));
    return { name: rename(name), ...facts };
  });
};

/** The credentials folder: NAMES with the slot's hex redacted, and each entry's lstat facts. No file under it is opened. */
export function credentialsRecord(fsx, dir) {
  const folder = signInRecord(fsx, dir);
  return folder.exists === true ? { ...folder, entries: entriesOf(fsx, dir, redactSlot) } : folder;
}

/** `/proc/version`: its text, whether it names Microsoft or WSL, and `read: false` when it is absent, unreadable or empty. */
export function readBanner(fsx) {
  const text = attempt(() => fsx.readFile('/proc/version').trim(), '');
  return { read: text.length > 0, text, namesWsl: /microsoft|wsl/iu.test(text) };
}

const unescapeMount = (field) => field.replaceAll(/\\([0-7]{3})/gu, (_, octal) => String.fromCodePoint(Number.parseInt(octal, 8)));
const holds = (mountPoint, path) => path === mountPoint || path.startsWith(mountPoint === '/' ? '/' : `${mountPoint}/`);

/** The file-system TYPE under `path` from `/proc/self/mountinfo` text: the longest mount point that holds it ('' when none does). */
export function mountTypeOf(mountinfo, path) {
  const mounts = mountinfo.split('\n').flatMap((line) => {
    const [left, right] = line.split(' - ');
    const fields = (left ?? '').split(' ');
    return right && fields.length > 4 ? [{ at: unescapeMount(fields[4]), type: right.split(' ')[0] }] : [];
  });
  const owner = mounts.filter((m) => holds(m.at, path)).sort((a, b) => b.at.length - a.at.length)[0];
  return owner?.type ?? '';
}

/**
 * DN10's keyring-session facts, by this script's OWN raw reading — decided by nothing, read by nothing. Names only: the variable's
 * presence in this process and in the VS Code server's environment (`serverNames` are its variable NAMES), a socket at
 * `$XDG_RUNTIME_DIR/bus` else `/run/user/<uid>/bus`, a keyring daemon in the listing. An unreadable listing is "cannot tell".
 */
export function keyringSession({ env, serverNames, uid, fsx, listing }) {
  const runtime = env.XDG_RUNTIME_DIR || `/run/user/${uid}`;
  const busSocketPath = posix.join(runtime, 'bus');
  const socket = attempt(() => fsx.lstat(busSocketPath).isSocket(), false);
  const entries = attempt(() => ({ list: listing() }), null);
  return {
    busAddressSet: Boolean(env.DBUS_SESSION_BUS_ADDRESS),
    serverBusAddressSet: serverNames.includes('DBUS_SESSION_BUS_ADDRESS'),
    busSocket: socket,
    busSocketPath,
    daemons: entries === null ? [] : daemonsIn(entries.list),
    listing: entries === null ? 'cannot tell' : 'read',
  };
}

const daemonsIn = (list) => list.flatMap((entry) => {
  const hit = [entry.comm, entry.name].find(isKeyringDaemon);
  return hit === undefined ? [] : [hit];
});

/**
 * `coai-mcp --host-facts` as a record: the parsed line when it answers, "no such mode" for exit 64 (an old binary — never an empty
 * fact), and a status for every other outcome. `run(file, args, options)` is the injected runner; `env`, when given, is the child's environment (the
 * product reads the session bus names from ITS OWN environment, so it must be given them).
 */
export function hostFactsMode(run, binary, env) {
  const r = run(binary, ['--host-facts'], env ? { env } : {});
  if (r.error) return { status: 'unavailable', code: r.error.code ?? r.error.message };
  if (r.status === 64) return { status: 'no such mode' };
  if (r.status !== 0) return { status: 'failed', exit: r.status };
  const text = String(r.stdout ?? '').trim();
  const line = attempt(() => JSON.parse(text), undefined);
  return line === undefined || line === null || typeof line !== 'object' ? { status: 'unparseable', text: text.slice(0, 200) } : { status: 'read', line };
}

/** Where a tool resolves: the first of `dirs`, in order, holding an executable FILE of that name ('' is the current folder, as a shell reads it). */
export function resolveTool(fsx, name, dirs) {
  const candidates = dirs.map((dir) => `${dir === '' ? '.' : dir}/${name}`);
  const at = candidates.findIndex((path) => {
    const st = statOf(fsx, path);
    return st !== null && st.isFile() && (st.mode & 0o111) !== 0;
  });
  return at < 0 ? { found: false, path: '', index: -1 } : { found: true, path: candidates[at], index: at };
}

/** Every entry of a PATH text with its kind, owner and mode — facts for the lead; make-series.sh is what refuses a PATH. */
export function pathEntries(fsx, pathText) {
  return pathText.split(':').map((entry) => {
    if (entry === '') return { entry, kind: 'empty' };
    if (!entry.startsWith('/')) return { entry, kind: 'relative' };
    const st = statOf(fsx, entry);
    return st === null ? { entry, kind: 'missing' }
      : { entry, kind: st.isDirectory() ? 'directory' : 'other', uid: st.uid, mode: modeText(st.mode), worldWritable: (st.mode & 0o002) !== 0 };
  });
}

/** One short progress line before a slow step, through the injected sink (the probe writes it to STDERR; stdout stays the one JSON record). */
const say = (deps, line) => deps.progress?.(line);

const firstLine = (text) => String(text ?? '').trim().split('\n')[0] ?? '';
const pathDirs = (pathText) => pathText.split(':').filter((d) => d.startsWith('/'));

function osPrettyName(fsx) {
  const text = attempt(() => fsx.readFile('/etc/os-release'), '');
  return /^PRETTY_NAME="?([^"\n]*)"?$/mu.exec(text)?.[1] ?? '';
}

const mountsOf = (fsx, home, mountinfo) => {
  const credentials = posix.join(home, '.kimi-code', 'credentials');
  const places = ['/tmp', home, posix.join(home, '.local', 'share'), posix.join(home, '.vibe'), ...(attempt(() => fsx.lstat(credentials), null) ? [credentials] : [])];
  return Object.fromEntries(places.map((place) => [place, mountTypeOf(mountinfo, place)]));
};

const launcherPrograms = (fsx) => Object.fromEntries(['/bin/bash', '/usr/bin/nohup', '/usr/bin/setsid'].map((path) => {
  const st = statOf(fsx, path);
  return [path, { exists: st !== null, executable: st !== null && (st.mode & 0o111) !== 0 }];
}));

/** The folder with no trailing separator when the variable names an ABSOLUTE one, else '' (a relative one is cwd-relative, which the product does not search). */
const absoluteDir = (dir) => (typeof dir === 'string' && dir.startsWith('/') ? dir.replace(/\/+$/u, '') : '');

/** The folders uv installs executables into, most preferred first - VibeRuntime.UvToolFolders: an absolute UV_TOOL_BIN_DIR, XDG_BIN_HOME, $XDG_DATA_HOME/../bin, ~/.local/bin. */
function uvToolFolders(env, home) {
  const xdgData = absoluteDir(env.XDG_DATA_HOME);
  return [absoluteDir(env.UV_TOOL_BIN_DIR), absoluteDir(env.XDG_BIN_HOME), xdgData.includes('/') ? `${xdgData.slice(0, xdgData.lastIndexOf('/'))}/bin` : '', posix.join(home, '.local', 'bin')].filter((dir) => dir !== '');
}

/** The tool lookups in the product's own order (DL5 of research/PLAN_kimi_and_vibe_on_linux.md); each answers `{ found, path, index }`. */
function toolsOf(fsx, env, home, pathText) {
  const onPath = pathDirs(pathText);
  const kimiBin = posix.join(env.KIMI_INSTALL_DIR || posix.join(home, '.kimi-code'), 'bin');
  const localBin = posix.join(home, '.local', 'bin');
  return {
    vibe: resolveTool(fsx, 'vibe', [...uvToolFolders(env, home), ...onPath]),
    kimi: resolveTool(fsx, 'kimi', [kimiBin, ...onPath]),
    rg: resolveTool(fsx, 'rg', [kimiBin, ...onPath, localBin]),
    fd: resolveTool(fsx, 'fd', [kimiBin, ...onPath, localBin]),
    flock: resolveTool(fsx, 'flock', ['/usr/bin', '/bin', ...onPath]),
  };
}

/** The minimal environment plus the two names the keyring-session reading looks at (`DBUS_SESSION_BUS_ADDRESS`, `XDG_RUNTIME_DIR`) when the session has them - nothing else of it. */
const SESSION_BUS_NAMES = ['DBUS_SESSION_BUS_ADDRESS', 'XDG_RUNTIME_DIR'];
const childEnvironment = (env) => ({ ...minimalEnv('linux', env), ...Object.fromEntries(SESSION_BUS_NAMES.filter((name) => env[name]).map((name) => [name, env[name]])) });

function coaiMcpOf({ run, hash, ...deps }, binary) {
  if (!binary) return { asked: false };
  say(deps, 'asking coai-mcp --version and --host-facts...');
  const version = firstLine(run(binary, ['--version'], {}).stdout);
  return { path: binary, version, sha256: attempt(() => hash(binary), ''), hostFacts: hostFactsMode(run, binary, childEnvironment(deps.env)) };
}

function h1Of(deps, seriesPath) {
  const { fsx, env, uid, home, platform } = deps;
  say(deps, 'reading the kernel banner, the OS and the mounts...');
  const mountinfo = attempt(() => fsx.readFile('/proc/self/mountinfo'), '');
  const nodeAbs = deps.series ? attempt(() => fsx.readFile(`${deps.series}/run/node.txt`).trim(), '') : '';
  return {
    banner: readBanner(fsx),
    osPrettyName: osPrettyName(fsx),
    node: platform.node,
    glibc: platform.glibc,
    uid,
    protectedSymlinks: attempt(() => fsx.readFile('/proc/sys/fs/protected_symlinks').trim(), ''),
    mounts: mountsOf(fsx, home, mountinfo),
    serverPath: seriesPath === '' ? { present: false } : { present: true, value: seriesPath, entries: pathEntries(fsx, seriesPath) },
    nodeAbsolute: nodeAbs === '' ? { present: false } : { present: true, path: nodeAbs, ...resolveFacts(fsx, nodeAbs) },
    tools: toolsOf(fsx, env, home, seriesPath === '' ? env.PATH ?? '' : seriesPath),
    launcherPrograms: launcherPrograms(fsx),
    coaiMcp: coaiMcpOf(deps, deps.coaiMcp),
  };
}

const resolveFacts = (fsx, path) => {
  const st = statOf(fsx, path);
  return { exists: st !== null, executable: st !== null && (st.mode & 0o111) !== 0 };
};

/** Runs one Python program of this file in the given environment; the stdout, or '' when it did not answer. */
const python = (run, interpreter, program, env, cwd) => {
  const r = run(interpreter, ['-c', program], { env, cwd });
  return r.status === 0 ? String(r.stdout ?? '').trim() : '';
};

function keyringReadings(deps, interpreter) {
  const { fsx, run, env, scratch } = deps;
  const freshHome = (name) => {
    const home = posix.join(scratch, name);
    fsx.mkdir?.(home);
    return home;
  };
  const built = vibeBuiltEnv(freshHome('v0-home-built'), 'none', 'linux', env);
  const forced = { ...vibeBuiltEnv(freshHome('v0-home-forced'), 'none', 'linux', env), PYTHON_KEYRING_BACKEND: KEYRING_FAIL_BACKEND };
  say(deps, "asking Vibe's Python for its keyring class (built environment)...");
  const builtEnvironment = python(run, interpreter, PY_KEYRING_CLASS, built, scratch);
  say(deps, "asking Vibe's Python for its keyring class (fail backend forced)...");
  const withFailBackend = python(run, interpreter, PY_KEYRING_CLASS, forced, scratch);
  return { builtEnvironment, withFailBackend, failBackendForced: withFailBackend === KEYRING_FAIL_BACKEND };
}

function distributionsOf(deps, interpreter, readEnv) {
  say(deps, "asking Vibe's Python for its keyring-related distributions...");
  return attempt(() => JSON.parse(python(deps.run, interpreter, PY_DISTRIBUTIONS, readEnv, deps.scratch)), []);
}

function v0Of(deps) {
  const { fsx, run, env, uid, home, scratch, listing, serverEnvNames } = deps;
  const vibeHome = personsVibeHome(env, home, 'linux');
  const interpreter = vibePython('linux', env, home);
  const hasPython = statOf(fsx, interpreter) !== null;
  const configHomes = [posix.join(home, '.config'), ...(env.XDG_CONFIG_HOME ? [env.XDG_CONFIG_HOME] : [])];
  const readEnv = vibeBuiltEnv(posix.join(scratch, 'v0-home-built'), 'none', 'linux', env);
  return {
    vibeHome,
    signIn: signInRecord(fsx, posix.join(vibeHome, '.env')),
    names: entriesOf(fsx, vibeHome),
    python: { path: interpreter, found: hasPython },
    distributions: hasPython ? distributionsOf(deps, interpreter, readEnv) : [],
    keyringBackend: hasPython ? keyringReadings(deps, interpreter) : { status: 'no python' },
    keyringrc: configHomes.map((base) => signInRecord(fsx, posix.join(base, 'python_keyring', 'keyringrc.cfg'))),
    keyringSession: keyringSession({ env, serverNames: attempt(() => serverEnvNames(), []), uid, fsx, listing }),
  };
}

/**
 * kimi's environment for N-K0: an OWNED root (`<scratch>/k0` = home/ + an empty os/ + cache/, kimi-home.mjs's layout) so HOME is the empty os/,
 * KIMI_CODE_HOME the root's home/ and XDG_CACHE_HOME its cache/ — kimi's native-file extraction never lands in the person's ~/.cache (J2).
 */
function k0Environment({ env, scratch, ownedRoot }) {
  const owned = ownedRoot(posix.join(scratch, 'k0'));
  return kimiBuiltEnv({ home: owned.home, profile: owned.os, platform: 'linux', parent: env });
}

function k0Of(deps, pathText) {
  const { fsx, run, env, home, scratch, kimi } = deps;
  if (!kimi) return { asked: false };
  // The "no other kimi" guard (DN5): the probe's main() waits for it BEFORE the record is collected and hands the answer in as `kimiClear`.
  if (deps.kimiClear === false) return { asked: false, skipped: 'another kimi was running when N-K0 was due - two refreshers could sign the person out, so kimi was not started' };
  const kimiHome = posix.join(env.KIMI_CODE_HOME || posix.join(home, '.kimi-code'));
  const built = k0Environment(deps);
  const asked = (args) => run(kimi, args, { env: built, cwd: scratch });
  say(deps, 'asking kimi --version...');
  const version = firstLine(asked(['--version']).stdout);
  say(deps, 'asking kimi acp --help...');
  const help = asked(['acp', '--help']);
  return {
    asked: true,
    path: kimi,
    version,
    acpHelp: { status: help.status, firstLine: firstLine(help.stdout || help.stderr) },
    tools: toolsOf(fsx, env, home, pathText),
    credentials: credentialsRecord(fsx, posix.join(kimiHome, 'credentials')),
  };
}

/**
 * The whole record: N-H1 (`h1`), N-V0 (`v0`), N-K0 (`k0`, only when a kimi path is named — the owner installs and signs in first).
 * `deps` is the injected world (see the top of this file); `series` names the series folder whose `run/path.txt` and `run/node.txt`
 * make-series.sh wrote — '' when there is none. Three more injected seams beyond the file system and the runners: `progress(line)` (one short
 * line before each slow step; optional), `ownedRoot(path)` (kimi-home.mjs's — makes N-K0's `<scratch>/k0` home/os/cache), and `kimiClear`
 * (false = another kimi runs, N-K0 is skipped; the probe waits for it with proc.mjs's `waitForNoOtherKimi` before calling this).
 */
export function collectHostFacts(deps) {
  const serverPath = deps.series ? attempt(() => deps.fsx.readFile(`${deps.series}/run/path.txt`).trim(), '') : '';
  const h1 = h1Of(deps, serverPath);
  const v0 = v0Of(deps);
  const k0 = k0Of(deps, serverPath === '' ? deps.env.PATH ?? '' : serverPath);
  return { schema: 'native-host-facts/1', at: deps.now().toISOString(), h1, v0, k0 };
}
