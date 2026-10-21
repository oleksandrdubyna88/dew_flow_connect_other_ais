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
import { builtEnv as vibeBuiltEnv, personsVibeHome, vibePython } from './vibe.mjs';
import { builtEnv as kimiBuiltEnv } from './kimi-home.mjs';

/** A keyring daemon by the start of its `comm` (cut at 15 characters by the kernel: `gnome-keyring-d`). */
export const DAEMON_PREFIXES = ['gnome-keyring', 'kwalletd', 'keepassxc'];
/** What Python's keyring forced to FAIL answers (keyring/backends/fail.py): the class DN10 of the plan makes coai's Vibe launch carry. */
const FAIL_BACKEND = 'keyring.backends.fail.Keyring';
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
export const redactSlot = (name) => name.replaceAll(/[0-9a-f]{8,}/giu, '<hex>');

/** One path by `lstat` ALONE — type, size, mode, owner, mtime; `{ path, exists: false }` when it is not there. The file is never opened. */
export function signInRecord(fsx, path) {
  const st = attempt(() => fsx.lstat(path), null);
  return st === null ? { path, exists: false }
    : { path, exists: true, type: typeOf(st), size: st.size, mode: modeText(st.mode), uid: st.uid, mtime: iso(st.mtimeMs) };
}

/** A folder's entries by `readdir` + `lstat`: the first level only, names verbatim. */
const entriesOf = (fsx, dir, rename = (name) => name) => attempt(() => fsx.readdir(dir), []).map((name) => {
  const { path: _path, ...facts } = signInRecord(fsx, posix.join(dir, name));
  return { name: rename(name), ...facts };
});

/** The credentials folder: NAMES with the slot's hex redacted, and each entry's lstat facts. No file under it is opened. */
export function credentialsRecord(fsx, dir) {
  const folder = signInRecord(fsx, dir);
  return folder.exists ? { ...folder, entries: entriesOf(fsx, dir, redactSlot) } : folder;
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
  const hit = [entry.comm, entry.name].find((n) => typeof n === 'string' && DAEMON_PREFIXES.some((p) => n.startsWith(p)));
  return hit === undefined ? [] : [hit];
});

/**
 * `coai-mcp --host-facts` as a record: the parsed line when it answers, "no such mode" for exit 64 (an old binary — never an empty
 * fact), and a status for every other outcome. `run(file, args, options)` is the injected runner.
 */
export function hostFactsMode(run, binary) {
  const r = run(binary, ['--host-facts'], {});
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

/** The tool lookups in the product's own order (DL5 of research/PLAN_kimi_and_vibe_on_linux.md); each answers `{ found, path, index }`. */
function toolsOf(fsx, env, home, pathText) {
  const onPath = pathDirs(pathText);
  const kimiBin = posix.join(env.KIMI_INSTALL_DIR || posix.join(home, '.kimi-code'), 'bin');
  const localBin = posix.join(home, '.local', 'bin');
  return {
    vibe: resolveTool(fsx, 'vibe', [...(env.UV_TOOL_BIN_DIR ? [env.UV_TOOL_BIN_DIR] : []), localBin, ...onPath]),
    kimi: resolveTool(fsx, 'kimi', [kimiBin, ...onPath]),
    rg: resolveTool(fsx, 'rg', [kimiBin, ...onPath, localBin]),
    fd: resolveTool(fsx, 'fd', [kimiBin, ...onPath, localBin]),
    flock: resolveTool(fsx, 'flock', ['/usr/bin', '/bin', ...onPath]),
  };
}

function coaiMcpOf({ fsx, run, hash }, binary) {
  if (!binary) return { asked: false };
  const version = firstLine(run(binary, ['--version'], {}).stdout);
  return { path: binary, version, sha256: attempt(() => hash(binary), ''), hostFacts: hostFactsMode(run, binary) };
}

function h1Of(deps, seriesPath) {
  const { fsx, env, uid, home, platform } = deps;
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

function keyringReadings({ fsx, run, env, scratch }, interpreter) {
  const freshHome = (name) => {
    const home = posix.join(scratch, name);
    fsx.mkdir?.(home);
    return home;
  };
  const built = vibeBuiltEnv(freshHome('v0-home-built'), 'none', 'linux', env);
  const forced = { ...vibeBuiltEnv(freshHome('v0-home-forced'), 'none', 'linux', env), PYTHON_KEYRING_BACKEND: FAIL_BACKEND };
  const builtEnvironment = python(run, interpreter, PY_KEYRING_CLASS, built, scratch);
  const withFailBackend = python(run, interpreter, PY_KEYRING_CLASS, forced, scratch);
  return { builtEnvironment, withFailBackend, failBackendForced: withFailBackend === FAIL_BACKEND };
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
    distributions: hasPython ? attempt(() => JSON.parse(python(run, interpreter, PY_DISTRIBUTIONS, readEnv, scratch)), []) : [],
    keyringBackend: hasPython ? keyringReadings(deps, interpreter) : { status: 'no python' },
    keyringrc: configHomes.map((base) => signInRecord(fsx, posix.join(base, 'python_keyring', 'keyringrc.cfg'))),
    keyringSession: keyringSession({ env, serverNames: attempt(() => serverEnvNames(), []), uid, fsx, listing }),
  };
}

function k0Of(deps, pathText) {
  const { fsx, run, env, home, scratch, kimi } = deps;
  if (!kimi) return { asked: false };
  const kimiHome = posix.join(env.KIMI_CODE_HOME || posix.join(home, '.kimi-code'));
  const fresh = posix.join(scratch, 'k0-home');
  fsx.mkdir?.(fresh);
  const built = kimiBuiltEnv({ home: fresh, platform: 'linux', parent: env });
  const asked = (args) => run(kimi, args, { env: built, cwd: scratch });
  const version = firstLine(asked(['--version']).stdout);
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
 * make-series.sh wrote — '' when there is none.
 */
export function collectHostFacts(deps) {
  const serverPath = deps.series ? attempt(() => deps.fsx.readFile(`${deps.series}/run/path.txt`).trim(), '') : '';
  const k0 = k0Of(deps, serverPath === '' ? deps.env.PATH ?? '' : serverPath);
  return { schema: 'native-host-facts/1', at: deps.now().toISOString(), h1: h1Of(deps, serverPath), v0: v0Of(deps), k0 };
}
